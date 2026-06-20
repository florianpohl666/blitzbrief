using System.Drawing;
using System.IO;
using System.Windows;
using BlitzBrief.Core.Models;
using BlitzBrief.Core.Security;
using BlitzBrief.Core.Settings;
using BlitzBrief.Core.Workflow;
using BlitzBrief.Windows.Platform;
using Forms = System.Windows.Forms;

namespace BlitzBrief.Windows;

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
    private OverlayWindow? overlayWindow;
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
            Text = "BlitzBrief",
            Visible = false,
            ContextMenuStrip = BuildMenu()
        };
        notifyIcon.DoubleClick += (_, _) => ShowSettings();
        hotkeyManager.HotkeyPressed += OnHotkeyPressed;
        hotkeyManager.HotkeyReleased += OnHotkeyReleased;
        hotkeyManager.DoubleTapDetected += (_, type) =>
        {
            AppLog.Write($"DoubleTapDetected type={type}");
            ToggleWorkflow(type);
        };
        audioRecorder.AudioLevelChanged += (_, level) => overlayWindow?.SetLevel(level);
    }

    public void Start()
    {
        AppLog.Write("TrayController.Start");
        notifyIcon.Visible = true;
        RegisterHotkeys();
        ConfigureAudio();
        SetStatus("BlitzBrief bereit");
        ShowFloatingToolbar();
        ShowSettings();
    }

    private void ConfigureAudio()
    {
        try
        {
            audioRecorder.Configure(settings.AudioInputDeviceNumber, settings.PreRollEnabled, settings.PreRollMilliseconds);
        }
        catch (Exception ex)
        {
            AppLog.Write($"ConfigureAudio failed: {ex}");
        }
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("BlitzBrief", null, (_, _) => ToggleWorkflow(WorkflowType.Transcription));
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
                notifyIcon.ShowBalloonTip(4500, "BlitzBrief Hotkey", error, Forms.ToolTipIcon.Warning);
            }
        }

        hotkeyManager.ConfigureDoubleTap(settings.DoubleTapEnabled, settings.DoubleTapModifier, WorkflowType.Transcription);
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
            audioRecorder.Arm();
            activeWorkflow = type;
            SetStatus($"{type.DisplayName()}: Aufnahme läuft");
            ShowOverlayListening(type.DisplayName());
            AppLog.Write($"StartRecording succeeded type={type}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"StartRecording failed: {ex}");
            activeWorkflow = null;
            SetStatus("Aufnahmefehler");
            notifyIcon.ShowBalloonTip(5000, "BlitzBrief", ex.Message, Forms.ToolTipIcon.Error);
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
            ShowOverlayProcessing();
            var result = await workflowRunner.ProcessAsync(type, recording.Path, recording.Duration, token);
            AppLog.Write($"StopAndProcess result chars={result.Text.Length} preview={result.Text[..Math.Min(60, result.Text.Length)]}");
            clipboardPasteService.CopyText(result.Text);
            if (settings.AutoPaste)
            {
                if (settings.AutoPasteDelay)
                {
                    await Task.Delay(300, token);
                }
                await clipboardPasteService.PasteAsync(token);
            }

            HideOverlay();
            SetStatus($"{type.DisplayName()}: fertig");

            if (settings.DebugMode && result.Stage1Transcript is not null)
            {
                var s1 = result.Stage1Transcript;
                var s2 = result.Text;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    new DebugOutputWindow(s1, s2).Show());
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Write($"StopAndProcess cancelled type={type}");
            HideOverlay();
            SetStatus("BlitzBrief bereit");
        }
        catch (Exception ex)
        {
            AppLog.Write($"StopAndProcess error type={type}: {ex}");
            HideOverlay();
            SetStatus("Fehler");
            notifyIcon.ShowBalloonTip(6000, "BlitzBrief", ex.Message, Forms.ToolTipIcon.Error);
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
        HideOverlay();
        SetStatus("BlitzBrief bereit");
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
                    ConfigureAudio();
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
        });
    }

    private void ShowOverlayListening(string label)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            overlayWindow ??= new OverlayWindow();
            overlayWindow.ShowListening(label);
        });
    }

    private void ShowOverlayProcessing()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => overlayWindow?.ShowProcessing("Wird verarbeitet"));
    }

    private void HideOverlay()
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() => overlayWindow?.HideOverlay());
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
        overlayWindow?.Close();
        floatingToolbarWindow?.Close();
        settingsWindow?.Close();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
