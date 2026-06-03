using System.Drawing;
using System.IO;
using System.Windows;
using Blitztext.Core.Models;
using Blitztext.Core.Security;
using Blitztext.Core.Settings;
using Blitztext.Core.Workflow;
using Blitztext.Windows.Platform;
using Forms = System.Windows.Forms;

namespace Blitztext.Windows;

public sealed class TrayController : IDisposable
{
    private readonly AppSettings settings;
    private readonly SettingsStore settingsStore;
    private readonly ApiKeyStore apiKeyStore;
    private readonly WorkflowRunner workflowRunner;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly HotkeyManager hotkeyManager = new();
    private readonly AudioRecorder audioRecorder = new();
    private readonly ClipboardPasteService clipboardPasteService = new();
    private SettingsWindow? settingsWindow;
    private FloatingToolbarWindow? floatingToolbarWindow;
    private CancellationTokenSource? processingCts;
    private WorkflowType? activeWorkflow;
    private bool disposed;

    public TrayController(
        AppSettings settings,
        SettingsStore settingsStore,
        ApiKeyStore apiKeyStore,
        WorkflowRunner workflowRunner)
    {
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.apiKeyStore = apiKeyStore;
        this.workflowRunner = workflowRunner;

        notifyIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Blitztext",
            Visible = false,
            ContextMenuStrip = BuildMenu()
        };
        notifyIcon.DoubleClick += (_, _) => ShowSettings();
        hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        hotkeyManager.HotkeyReleased += OnHotkeyReleased;
    }

    public void Start()
    {
        AppLog.Write("TrayController.Start");
        notifyIcon.Visible = true;
        RegisterHotkeys();
        SetStatus("Blitztext bereit");
        ShowFloatingToolbar();
        ShowSettings();
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Blitztext", null, (_, _) => ToggleWorkflow(WorkflowType.Transcription));
        menu.Items.Add("Text verbessern", null, (_, _) => ToggleWorkflow(WorkflowType.TextImprover));
        menu.Items.Add("Ärger beruhigen", null, (_, _) => ToggleWorkflow(WorkflowType.DampfAblassen));
        menu.Items.Add("Emoji ergänzen", null, (_, _) => ToggleWorkflow(WorkflowType.EmojiText));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Desktop-Leiste anzeigen", null, (_, _) => ShowFloatingToolbar());
        menu.Items.Add("Einstellungen", null, (_, _) => ShowSettings());
        menu.Items.Add("Beenden", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void RegisterHotkeys()
    {
        hotkeyManager.UnregisterAll();
        foreach (var (type, hotkey) in settings.WorkflowHotkeys)
        {
            if (!hotkeyManager.TryRegister(type, hotkey, out var error))
            {
                notifyIcon.ShowBalloonTip(4500, "Blitztext Hotkey", error, Forms.ToolTipIcon.Warning);
            }
        }
    }

    private void OnHotkeyPressed(object? sender, WorkflowType type)
    {
        AppLog.Write($"HotkeyPressed type={type} mode={settings.HotkeyMode} active={activeWorkflow}");
        if (settings.HotkeyMode == HotkeyMode.Hold)
        {
            if (activeWorkflow is null)
            {
                StartRecording(type);
            }
            return;
        }

        ToggleWorkflow(type);
    }

    private void OnHotkeyReleased(object? sender, WorkflowType type)
    {
        AppLog.Write($"HotkeyReleased type={type} mode={settings.HotkeyMode} active={activeWorkflow}");
        if (settings.HotkeyMode == HotkeyMode.Hold && activeWorkflow == type)
        {
            _ = StopAndProcessAsync(type);
        }
    }

    private void ToggleWorkflow(WorkflowType type)
    {
        AppLog.Write($"ToggleWorkflow type={type} active={activeWorkflow}");
        if (activeWorkflow is null)
        {
            StartRecording(type);
            return;
        }

        if (activeWorkflow == type)
        {
            _ = StopAndProcessAsync(type);
            return;
        }

        CancelActiveWorkflow();
        StartRecording(type);
    }

    private void StartRecording(WorkflowType type)
    {
        AppLog.Write($"StartRecording requested type={type} apiConfigured={apiKeyStore.IsConfigured}");
        if (!apiKeyStore.IsConfigured)
        {
            ShowSettings();
            notifyIcon.ShowBalloonTip(4000, "OpenAI API Key fehlt", "Bitte hinterlege deinen lokalen OpenAI API Key.", Forms.ToolTipIcon.Info);
            return;
        }

        try
        {
            processingCts?.Cancel();
            processingCts = new CancellationTokenSource();
            audioRecorder.Start(settings.AudioInputDeviceNumber);
            activeWorkflow = type;
            SetStatus($"{type.DisplayName()}: Aufnahme läuft");
            notifyIcon.ShowBalloonTip(1200, "Blitztext", "Aufnahme läuft.", Forms.ToolTipIcon.Info);
            AppLog.Write($"StartRecording succeeded type={type}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"StartRecording failed: {ex}");
            activeWorkflow = null;
            SetStatus("Aufnahmefehler");
            notifyIcon.ShowBalloonTip(5000, "Blitztext", ex.Message, Forms.ToolTipIcon.Error);
        }
    }

    private async Task StopAndProcessAsync(WorkflowType type)
    {
        if (activeWorkflow is null)
        {
            return;
        }

        var token = processingCts?.Token ?? CancellationToken.None;
        string? audioPath = null;
        try
        {
            AppLog.Write($"StopAndProcess start type={type}");
            var recording = audioRecorder.Stop();
            audioPath = recording.Path;
            SetStatus($"{type.DisplayName()}: wird verarbeitet");
            var result = await workflowRunner.ProcessAsync(type, recording.Path, recording.Duration, token);
            clipboardPasteService.CopyText(result);
            if (settings.AutoPaste)
            {
                clipboardPasteService.Paste();
            }

            SetStatus($"{type.DisplayName()}: fertig");
            notifyIcon.ShowBalloonTip(2500, "Blitztext", "Text ist in der Zwischenablage.", Forms.ToolTipIcon.Info);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Blitztext bereit");
        }
        catch (Exception ex)
        {
            SetStatus("Fehler");
            notifyIcon.ShowBalloonTip(6000, "Blitztext", ex.Message, Forms.ToolTipIcon.Error);
            if (audioPath is not null && File.Exists(audioPath))
            {
                TryDelete(audioPath);
            }
        }
        finally
        {
            activeWorkflow = null;
        }
    }

    private void CancelActiveWorkflow()
    {
        processingCts?.Cancel();
        audioRecorder.Cancel();
        activeWorkflow = null;
        SetStatus("Blitztext bereit");
    }

    private void ShowSettings()
    {
        AppLog.Write("ShowSettings requested.");
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            AppLog.Write("ShowSettings dispatcher entered.");
            if (settingsWindow is null)
            {
                AppLog.Write("Creating SettingsWindow.");
                settingsWindow = new SettingsWindow(settings, settingsStore, apiKeyStore);
                settingsWindow.SettingsSaved += (_, _) =>
                {
                    RegisterHotkeys();
                    SetStatus("Einstellungen gespeichert");
                };
                settingsWindow.Closed += (_, _) => settingsWindow = null;
                System.Windows.Application.Current.MainWindow = settingsWindow;
                AppLog.Write("SettingsWindow created.");
            }

            if (!settingsWindow.IsVisible)
            {
                AppLog.Write("SettingsWindow.Show");
                settingsWindow.Show();
            }

            settingsWindow.WindowState = WindowState.Normal;
            settingsWindow.ShowInTaskbar = true;
            settingsWindow.Topmost = true;
            settingsWindow.Activate();
            settingsWindow.Focus();
            settingsWindow.Topmost = false;
            AppLog.Write($"SettingsWindow visible={settingsWindow.IsVisible} state={settingsWindow.WindowState} title={settingsWindow.Title}");
        });
    }

    private void ShowFloatingToolbar()
    {
        AppLog.Write("ShowFloatingToolbar requested.");
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (floatingToolbarWindow is null)
            {
                floatingToolbarWindow = new FloatingToolbarWindow();
                floatingToolbarWindow.WorkflowRequested += (_, type) =>
                {
                    AppLog.Write($"WorkflowRequested from floating toolbar type={type}");
                    ToggleWorkflow(type);
                };
                floatingToolbarWindow.Closed += (_, _) => floatingToolbarWindow = null;
            }

            if (!floatingToolbarWindow.IsVisible)
            {
                floatingToolbarWindow.Show();
            }

            floatingToolbarWindow.WindowState = WindowState.Normal;
            floatingToolbarWindow.Topmost = true;
            floatingToolbarWindow.Activate();
        });
    }

    private void SetStatus(string text)
    {
        notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        processingCts?.Cancel();
        audioRecorder.Dispose();
        hotkeyManager.Dispose();
        floatingToolbarWindow?.Close();
        settingsWindow?.Close();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
