using System.Diagnostics;
using System.Windows;
using BlitzBrief.Core;
using BlitzBrief.Core.Models;
using BlitzBrief.Core.OpenAI;
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
    private readonly IRealtimeTranscriber realtimeTranscriber;
    private readonly ICursorContextReader cursorContextReader;
    private readonly Forms.NotifyIcon notifyIcon;
    private readonly HotkeyManager hotkeyManager = new();
    private readonly AudioRecorder audioRecorder = new();
    private readonly ClipboardPasteService clipboardPasteService = new();
    private SettingsWindow? settingsWindow;
    private FloatingToolbarWindow? floatingToolbarWindow;
    private OverlayWindow? overlayWindow;
    private CancellationTokenSource? processingCts;
    private WorkflowType? activeWorkflow;
    private IRealtimeTranscriptionSession? activeSession;
    private string? activeSessionPrompt;
    private string? activeContext;
    private CursorSurroundings? activeSurroundings;
    private bool disposed;

    public TrayController(
        AppSettings settings,
        SettingsStore settingsStore,
        ApiKeyStore apiKeyStore,
        WorkflowRunner workflowRunner,
        IRealtimeTranscriber realtimeTranscriber,
        ICursorContextReader cursorContextReader)
    {
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.apiKeyStore = apiKeyStore;
        this.workflowRunner = workflowRunner;
        this.realtimeTranscriber = realtimeTranscriber;
        this.cursorContextReader = cursorContextReader;

        notifyIcon = new Forms.NotifyIcon
        {
            Icon = AppIcon.ForTray(),
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
        // Live-PCM während der Aufnahme an die laufende Realtime-Session streamen.
        audioRecorder.FrameAvailable += (_, pcm) => activeSession?.Append(pcm);
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
        menu.Items.Add("Blitzbrief-Easy", null, (_, _) => ToggleWorkflow(WorkflowType.BlitzBriefEasy));
        menu.Items.Add("Blitzbrief-Kontext", null, (_, _) => ToggleWorkflow(WorkflowType.BlitzBriefKontext));
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

            // Realtime-Session VOR dem Scharfschalten öffnen, damit der Pre-Roll-Block live mitgesendet wird.
            StartRealtimeSession(type);
            audioRecorder.Arm();

            // Kontext-Modus: Text rund um den Cursor lesen, solange der Fokus noch in der Zielapp
            // liegt (Overlay/Toolbar sind WS_EX_NOACTIVATE). Nach dem Armen, damit währenddessen
            // gesprochenes Audio in den Pre-Roll/Recorder läuft und nicht verloren geht.
            activeSurroundings = CaptureSurroundings(type);
            activeContext = activeSurroundings is null ? null : SentenceContext.CurrentSentence(activeSurroundings.Preceding);

            activeWorkflow = type;
            SetStatus($"{type.DisplayName()}: Aufnahme läuft");
            ShowOverlayListening(type.DisplayName());
            AppLog.Write($"StartRecording succeeded type={type} realtime={(activeSession is not null)}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"StartRecording failed: {ex}");
            AbortRealtimeSession();
            activeWorkflow = null;
            SetStatus("Aufnahmefehler");
            notifyIcon.ShowBalloonTip(5000, "BlitzBrief", ex.Message, Forms.ToolTipIcon.Error);
        }
    }

    // Liest im Kontext-Modus den Text rund um den Cursor; sonst kein Kontext.
    private CursorSurroundings? CaptureSurroundings(WorkflowType type)
    {
        if (type != WorkflowType.BlitzBriefKontext)
        {
            return null;
        }

        var surroundings = cursorContextReader.Read();
        var sentence = SentenceContext.CurrentSentence(surroundings.Preceding);
        AppLog.Write($"Kontext gelesen: links={(sentence is null ? "<null>" : $"\"{sentence}\"")} " +
                     $"rechtsLänge={surroundings.Following?.Length ?? 0}");
        return surroundings;
    }

    // Kontext-Modus: Diktat passend zur Einfügestelle formen (Leerzeichen links/rechts, Auto-Punkt
    // bei Satzeinschub entfernen). Andere Modi: bisheriges Verhalten – immer ein Leerzeichen anhängen.
    private static string ComposeInsertText(WorkflowType type, string text, CursorSurroundings? surroundings)
    {
        if (type == WorkflowType.BlitzBriefKontext && surroundings is not null)
        {
            return SmartInsert.Format(text, surroundings.Preceding, surroundings.Following);
        }

        return text + " ";
    }

    private void StartRealtimeSession(WorkflowType type)
    {
        activeSession = null;
        activeSessionPrompt = null;

        // Kontext-Modus läuft auf whisper-1 (batch-only) → IsRealtimeModel ist false → kein Realtime.
        var model = WorkflowRunner.TranscriptionModelFor(type, settings);
        if (!settings.UseRealtimeTranscription || !IsRealtimeModel(model))
        {
            return;
        }

        var apiKey = apiKeyStore.Load();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        try
        {
            // Dauer beim Start unbekannt -> hasEnoughAudio: true; das Echo bei sehr kurzem
            // Audio fängt der Retry-Pfad im WorkflowRunner ab.
            var prompt = PromptBuilder.BuildWorkflowWhisperPrompt(type, settings, hasEnoughAudio: true);
            activeSession = realtimeTranscriber.CreateSession(
                apiKey, model, settings.Language, prompt, AudioRecorder.SampleRate);
            activeSessionPrompt = prompt;
            AppLog.Write($"Realtime session created model={model}");
        }
        catch (Exception ex)
        {
            AppLog.Write($"Realtime session create failed, falling back to batch: {ex.Message}");
            activeSession = null;
            activeSessionPrompt = null;
        }
    }

    private static bool IsRealtimeModel(string model) =>
        model.Contains("transcribe", StringComparison.OrdinalIgnoreCase);

    private async Task StopAndProcessAsync(WorkflowType type)
    {
        if (activeWorkflow is null)
        {
            return;
        }

        var token = processingCts?.Token ?? CancellationToken.None;
        var session = activeSession;
        var sessionPrompt = activeSessionPrompt;
        var context = activeContext;
        var surroundings = activeSurroundings;
        activeSession = null;
        activeSessionPrompt = null;
        activeContext = null;
        activeSurroundings = null;

        try
        {
            AppLog.Write($"StopAndProcess start type={type}");
            var totalStopwatch = Stopwatch.StartNew();
            var recording = audioRecorder.Stop();
            SetStatus($"{type.DisplayName()}: wird verarbeitet");
            ShowOverlayProcessing();

            string? realtimeTranscript = await FinishRealtimeAsync(session, recording.Duration, token);

            var audio = new RecordedAudio(
                recording.Pcm,
                AudioRecorder.SampleRate,
                recording.Duration,
                realtimeTranscript,
                sessionPrompt,
                context);

            var result = await workflowRunner.ProcessAsync(type, audio, token);
            AppLog.Write($"StopAndProcess result chars={result.Text.Length} totalMs={totalStopwatch.ElapsedMilliseconds} realtime={(realtimeTranscript is not null)} preview={result.Text[..Math.Min(60, result.Text.Length)]}");
            var insertText = ComposeInsertText(type, result.Text, surroundings);
            clipboardPasteService.CopyText(insertText);
            if (settings.AutoPaste)
            {
                await clipboardPasteService.PasteAsync(token);
            }

            HideOverlay();
            SetStatus($"{type.DisplayName()}: fertig ({totalStopwatch.ElapsedMilliseconds} ms)");

            if (settings.DebugMode && result.Stage1Transcript is not null)
            {
                var raw = result.RawTranscript ?? result.Stage1Transcript;
                var s1 = result.Stage1Transcript;
                var s2 = result.Text;
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    new DebugOutputWindow(raw, s1, s2).Show());
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Write($"StopAndProcess cancelled type={type}");
            HideOverlay();
            SetStatus("BlitzBrief bereit");
        }
        catch (EmptyRecordingException)
        {
            // Leere/zu kurze Aufnahme ist kein Fehler – still ignorieren, kein Popup.
            AppLog.Write($"StopAndProcess empty recording type={type}");
            HideOverlay();
            SetStatus("BlitzBrief bereit");
        }
        catch (Exception ex)
        {
            AppLog.Write($"StopAndProcess error type={type}: {ex}");
            HideOverlay();
            SetStatus("Fehler");
            notifyIcon.ShowBalloonTip(6000, "BlitzBrief", ex.Message, Forms.ToolTipIcon.Error);
        }
        finally
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            activeWorkflow = null;
        }
    }

    // Committet die Realtime-Session und liefert das finale Transkript; bei Fehlern null
    // (-> WorkflowRunner transkribiert per Batch nach). Zu kurze Aufnahmen erst gar nicht senden.
    private static async Task<string?> FinishRealtimeAsync(
        IRealtimeTranscriptionSession? session, TimeSpan duration, CancellationToken token)
    {
        if (session is null)
        {
            return null;
        }

        if (TranscriptionQualityService.ShouldRejectRecording(duration))
        {
            session.Abort();
            return null;
        }

        try
        {
            var sw = Stopwatch.StartNew();
            var transcript = await session.CompleteAsync(token);
            AppLog.Write($"Realtime transcript in {sw.ElapsedMilliseconds}ms chars={transcript.Length}");
            return transcript;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Realtime failed, fallback to batch: {ex.Message}");
            return null;
        }
    }

    private void AbortRealtimeSession()
    {
        var session = activeSession;
        activeSession = null;
        activeSessionPrompt = null;
        if (session is not null)
        {
            session.Abort();
            _ = session.DisposeAsync();
        }
    }

    private void CancelActiveWorkflow()
    {
        processingCts?.Cancel();
        AbortRealtimeSession();
        audioRecorder.Cancel();
        activeWorkflow = null;
        activeContext = null;
        activeSurroundings = null;
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
                settingsWindow = new SettingsWindow(settings, settingsStore, apiKeyStore)
                {
                    Icon = AppIcon.ForWpf()
                };
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

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        processingCts?.Cancel();
        AbortRealtimeSession();
        audioRecorder.Dispose();
        hotkeyManager.Dispose();
        overlayWindow?.Close();
        floatingToolbarWindow?.Close();
        settingsWindow?.Close();
        notifyIcon.Visible = false;
        notifyIcon.Dispose();
    }
}
