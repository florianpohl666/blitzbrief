using System.Windows;
using System.Windows.Input;
using BlitzBrief.Core;
using BlitzBrief.Core.Models;
using BlitzBrief.Core.Security;
using BlitzBrief.Core.Settings;
using BlitzBrief.Windows.Platform;

namespace BlitzBrief.Windows;

public partial class SettingsWindow : Window
{
    private readonly AppSettings settings;
    private readonly SettingsStore settingsStore;
    private readonly ApiKeyStore apiKeyStore;

    public event EventHandler? SettingsSaved;

    private ModifierKeys _captureModifiersPeak;
    private bool _captureHadNonModifier;
    private bool _loading;

    public SettingsWindow(AppSettings settings, SettingsStore settingsStore, ApiKeyStore apiKeyStore)
    {
        InitializeComponent();
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.apiKeyStore = apiKeyStore;
        LoadValues();
    }

    // Zentrale Zuordnung Workflow -> Hotkey-Textbox (Laden, Validieren, Speichern, Zurücksetzen);
    // die einzige Stelle, die beim Hinzufügen eines Workflows erweitert werden muss.
    private (WorkflowType Type, System.Windows.Controls.TextBox Box)[] HotkeyBoxes =>
    [
        (WorkflowType.Transcription, TranscriptionHotkeyBox),
        (WorkflowType.TextImprover, TextImproverHotkeyBox),
        (WorkflowType.BlitzBriefEasy, BlitzBriefEasyHotkeyBox),
        (WorkflowType.BlitzBriefKontext, KontextHotkeyBox),
        (WorkflowType.BlitzBriefKontextGpt, KontextGptHotkeyBox),
        (WorkflowType.DampfAblassen, DampfHotkeyBox),
        (WorkflowType.EmojiText, EmojiHotkeyBox)
    ];

    private void LoadValues()
    {
        _loading = true;
        try
        {
            ApiKeyStatusText.Text = apiKeyStore.IsConfigured
                ? $"OpenAI API Key gespeichert: {apiKeyStore.DisplayValue}"
                : "Noch kein OpenAI API Key gespeichert.";

            LanguageBox.SelectedIndex = settings.Language switch
            {
                "en" => 1,
                "" => 2,
                _ => 0
            };
            TranscriptionModelBox.Text = settings.TranscriptionModel;
            KontextGptModelBox.Text = settings.KontextGptModel;
            HotkeyModeBox.SelectedIndex = settings.HotkeyMode == HotkeyMode.Hold ? 1 : 0;
            AutoPasteBox.IsChecked = settings.AutoPaste;
            DoubleTapEnabledBox.IsChecked = settings.DoubleTapEnabled;
            DoubleTapModifierBox.SelectedIndex = settings.DoubleTapModifier switch
            {
                ModifierKey.Alt => 1,
                ModifierKey.Shift => 2,
                _ => 0
            };
            PreRollEnabledBox.IsChecked = settings.PreRollEnabled;
            RealtimeBox.IsChecked = settings.UseRealtimeTranscription;
            DebugModeBox.IsChecked = settings.DebugMode;
            foreach (var (type, box) in HotkeyBoxes)
            {
                box.Text = settings.WorkflowHotkeys[type];
            }

            ToneBox.SelectedIndex = settings.TextImprovement.Tone switch
            {
                TextTone.Formal => 0,
                TextTone.Casual => 2,
                TextTone.JornMinimal => 3,
                TextTone.JornCommands => 4,
                _ => 1
            };
            SkipRewriteBox.IsChecked = settings.TextImprovement.SkipRewrite;
            ContextBox.Text = settings.TextImprovement.Context;
            TextPromptBox.Text = settings.TextImprovement.SystemPrompt;
            DampfPromptBox.Text = PromptBuilder.ShouldReplaceLegacyDampfPrompt(settings.DampfAblassen.SystemPrompt)
                ? PromptBuilder.DefaultDampfAblassenPrompt
                : settings.DampfAblassen.SystemPrompt;
            EmojiDensityBox.SelectedIndex = settings.EmojiText.EmojiDensity switch
            {
                EmojiDensity.Wenig => 0,
                EmojiDensity.Viel => 2,
                _ => 1
            };
            CustomTermsBox.Text = string.Join(Environment.NewLine, settings.CustomTerms);
            CommandsInfoBadge.Visibility = ToneBox.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
            RefreshMicrophoneStatus();
        }
        finally
        {
            _loading = false;
        }
    }

    private void ToneBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CommandsInfoBadge.Visibility = ToneBox.SelectedIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        _ = AutoSave();
    }

    private void Setting_Changed(object sender, RoutedEventArgs e) => _ = AutoSave();

    private void ComboBox_Changed(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => _ = AutoSave();

    private async Task AutoSave()
    {
        if (_loading) return;
        try
        {
            CollectSettingsFromUI();

            var hotkeyError = ValidateHotkeys(
                HotkeyBoxes.ToDictionary(entry => entry.Type, entry => entry.Box.Text.Trim()));

            if (hotkeyError is not null)
            {
                SaveStatusText.Text = hotkeyError;
                return;
            }

            await settingsStore.SaveAsync(settings);
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            SaveStatusText.Text = "Gespeichert.";
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = ex.Message;
        }
    }

    private void CollectSettingsFromUI()
    {
        settings.Language = LanguageBox.SelectedIndex switch
        {
            1 => "en",
            2 => "",
            _ => "de"
        };
        settings.TranscriptionModel = ModelFromCombo(TranscriptionModelBox, "gpt-4o-mini-transcribe");
        settings.KontextGptModel = ModelFromCombo(KontextGptModelBox, "gpt-4o-mini-transcribe");
        settings.HotkeyMode = HotkeyModeBox.SelectedIndex == 1 ? HotkeyMode.Hold : HotkeyMode.Toggle;
        settings.AutoPaste = AutoPasteBox.IsChecked == true;
        settings.DoubleTapEnabled = DoubleTapEnabledBox.IsChecked == true;
        settings.DoubleTapModifier = DoubleTapModifierBox.SelectedIndex switch
        {
            1 => ModifierKey.Alt,
            2 => ModifierKey.Shift,
            _ => ModifierKey.Ctrl
        };
        settings.PreRollEnabled = PreRollEnabledBox.IsChecked == true;
        settings.UseRealtimeTranscription = RealtimeBox.IsChecked == true;
        settings.AudioInputDeviceNumber = MicrophoneBox.SelectedValue is int selectedMicrophone
            ? selectedMicrophone
            : 0;
        foreach (var (type, box) in HotkeyBoxes)
        {
            settings.WorkflowHotkeys[type] = box.Text.Trim();
        }

        settings.TextImprovement.Tone = ToneBox.SelectedIndex switch
        {
            0 => TextTone.Formal,
            2 => TextTone.Casual,
            3 => TextTone.JornMinimal,
            4 => TextTone.JornCommands,
            _ => TextTone.Neutral
        };
        settings.TextImprovement.SkipRewrite = SkipRewriteBox.IsChecked == true;
        settings.TextImprovement.Context = ContextBox.Text.Trim();
        settings.TextImprovement.SystemPrompt = TextPromptBox.Text.Trim();
        settings.DampfAblassen.SystemPrompt = PromptBuilder.ShouldReplaceLegacyDampfPrompt(DampfPromptBox.Text)
            ? PromptBuilder.DefaultDampfAblassenPrompt
            : DampfPromptBox.Text.Trim();
        settings.EmojiText.EmojiDensity = EmojiDensityBox.SelectedIndex switch
        {
            0 => EmojiDensity.Wenig,
            2 => EmojiDensity.Viel,
            _ => EmojiDensity.Mittel
        };
        settings.CustomTerms = CustomTermsBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.DebugMode = DebugModeBox.IsChecked == true;
    }

    private void CheckMicrophone_Click(object sender, RoutedEventArgs e)
    {
        RefreshMicrophoneStatus();
    }

    private void RefreshMicrophoneStatus()
    {
        try
        {
            var devices = AudioRecorder.AvailableDevices();
            MicrophoneBox.ItemsSource = devices;
            if (devices.Count == 0)
            {
                MicrophoneStatusText.Text = "Kein Aufnahmegerät gefunden. Bitte Windows-Mikrofonzugriff und Standardgerät prüfen.";
                return;
            }

            var selected = devices.Any(d => d.Index == settings.AudioInputDeviceNumber)
                ? settings.AudioInputDeviceNumber
                : devices[0].Index;
            MicrophoneBox.SelectedValue = selected;
            MicrophoneStatusText.Text = "Gefunden: " + string.Join("; ", devices.Select(d => $"{d.Index}: {d.Name} ({d.Channels} Kanäle)"));
        }
        catch (Exception ex)
        {
            MicrophoneStatusText.Text = "Mikrofonprüfung fehlgeschlagen: " + ex.Message;
        }
    }

    private void HotkeyBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox box)
        {
            box.SelectAll();
            _captureModifiersPeak = ModifierKeys.None;
            _captureHadNonModifier = false;
            HotkeyHelpText.Text = "Drücke jetzt die gewünschte Tastenkombination. Esc bricht ab, Backspace löscht das Feld.";
        }
    }

    private void HotkeyBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            _captureModifiersPeak = ModifierKeys.None;
            _captureHadNonModifier = false;
            Keyboard.ClearFocus();
            HotkeyHelpText.Text = "Hotkey-Aufnahme abgebrochen.";
            return;
        }

        if (key == Key.Back)
        {
            _captureModifiersPeak = ModifierKeys.None;
            _captureHadNonModifier = false;
            box.Text = "";
            HotkeyHelpText.Text = "Hotkey gelöscht. Bitte vor dem Speichern eine neue Kombination wählen.";
            return;
        }

        _captureModifiersPeak |= Keyboard.Modifiers;

        if (IsModifierKey(key))
        {
            HotkeyHelpText.Text = CountModifiers(Keyboard.Modifiers) >= 2
                ? "Lasse alle Tasten los, um diese Modifier-Kombination zu speichern, oder drücke noch eine weitere Taste."
                : "Halte mindestens zwei Modifier-Tasten (Strg, Alt, Umschalt, Win) oder Modifier + weitere Taste.";
            return;
        }

        _captureHadNonModifier = true;
        var hotkey = BuildHotkeyText(key);
        if (hotkey is null)
        {
            HotkeyHelpText.Text = "Bitte eine Kombination aus Strg, Alt, Umschalt oder Windows-Taste plus einer weiteren Taste drücken.";
            return;
        }

        box.Text = hotkey;
        HotkeyHelpText.Text = $"Hotkey gesetzt: {hotkey}";
        _captureModifiersPeak = ModifierKeys.None;
        Keyboard.ClearFocus();
        _ = AutoSave();
    }

    private void HotkeyBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (!IsModifierKey(key))
        {
            return;
        }

        if (_captureHadNonModifier || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        var hotkey = BuildModifierOnlyText(_captureModifiersPeak);
        if (hotkey is null)
        {
            return;
        }

        box.Text = hotkey;
        HotkeyHelpText.Text = $"Hotkey gesetzt: {hotkey}";
        _captureModifiersPeak = ModifierKeys.None;
        Keyboard.ClearFocus();
        _ = AutoSave();
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.DefaultHotkeys();
        foreach (var (type, box) in HotkeyBoxes)
        {
            box.Text = defaults[type];
        }

        HotkeyHelpText.Text = "Standard-Hotkeys wiederhergestellt.";
        _ = AutoSave();
    }

    private void ResetGeneral_Click(object sender, RoutedEventArgs e)
    {
        // Setzt nur die allgemeinen Einstellungen auf die Standardwerte. _loading unterdrückt
        // die einzelnen Change-Events der Controls, danach speichern wir einmal gebündelt.
        _loading = true;
        try
        {
            LanguageBox.SelectedIndex = 0;                       // Deutsch
            TranscriptionModelBox.Text = "gpt-4o-mini-transcribe";
            HotkeyModeBox.SelectedIndex = 1;                     // Halten
            AutoPasteBox.IsChecked = true;                       // Ergebnis automatisch einfügen
            RealtimeBox.IsChecked = true;                        // Echtzeit-Transkription
            DoubleTapEnabledBox.IsChecked = true;                // Diktat-Doppeltipp
            PreRollEnabledBox.IsChecked = true;                  // Mikrofon aktiv halten
        }
        finally
        {
            _loading = false;
        }

        SaveStatusText.Text = "Allgemeine Einstellungen zurückgesetzt.";
        _ = AutoSave();
    }

    private void PasteApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = System.Windows.Clipboard.GetText().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            if (string.IsNullOrWhiteSpace(text) || !text.StartsWith("sk-", StringComparison.Ordinal))
            {
                SaveStatusText.Text = "Zwischenablage enthält keinen plausiblen OpenAI API Key.";
                return;
            }

            ApiKeyBox.Password = text;
            System.Windows.Clipboard.Clear();
            SaveStatusText.Text = "API Key eingefügt.";
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = ex.Message;
        }
    }

    private void SaveApiKey_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(ApiKeyBox.Password))
            {
                SaveStatusText.Text = "Bitte API Key eintragen.";
                return;
            }

            apiKeyStore.Save(ApiKeyBox.Password);
            ApiKeyBox.Clear();
            LoadValues();
            SaveStatusText.Text = "API Key lokal gespeichert.";
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = ex.Message;
        }
    }

    private void DeleteApiKey_Click(object sender, RoutedEventArgs e)
    {
        apiKeyStore.Delete();
        LoadValues();
        SaveStatusText.Text = "API Key gelöscht.";
    }

    // Editierbare ComboBox robust auslesen: beim Auswählen liefert .Text während des
    // SelectionChanged teils noch den alten oder einen leeren Wert (WPF-Eigenheit). Der
    // ausgewählte Eintrag ist zuverlässig; nur bei frei getipptem Wert greift .Text.
    private static string ModelFromCombo(System.Windows.Controls.ComboBox box, string fallback)
    {
        var value = (box.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString();
        if (string.IsNullOrWhiteSpace(value))
        {
            value = box.Text;
        }

        return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    private static string? ValidateHotkeys(Dictionary<WorkflowType, string> hotkeys)
    {
        foreach (var (type, hotkey) in hotkeys)
        {
            if (!HotkeyParser.TryParse(hotkey, out _))
            {
                return $"Hotkey für {type.DisplayName()} ist ungültig.";
            }
        }

        var duplicate = hotkeys
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        return duplicate is null ? null : $"Hotkey doppelt vergeben: {duplicate.Key}";
    }

    // Gedrückte Modifier als Hotkey-Textteile ("Ctrl", "Alt", …) in kanonischer Reihenfolge.
    private static List<string> ModifierParts(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts;
    }

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
            or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static string? BuildModifierOnlyText(ModifierKeys modifiers)
    {
        var parts = ModifierParts(modifiers);
        return parts.Count >= 2 ? string.Join("+", parts) : null;
    }

    private static int CountModifiers(ModifierKeys modifiers) => ModifierParts(modifiers).Count;

    private static string? BuildHotkeyText(Key key)
    {
        if (IsModifierKey(key))
        {
            return null;
        }

        var parts = ModifierParts(Keyboard.Modifiers);
        if (parts.Count == 0) return null;

        var keyText = KeyToText(key);
        if (keyText is null) return null;

        parts.Add(keyText);
        return string.Join("+", parts);
    }

    private static string? KeyToText(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return key.ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((int)(key - Key.D0)).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((int)(key - Key.NumPad0)).ToString();
        if (key is >= Key.F1 and <= Key.F24) return key.ToString();

        return key switch
        {
            Key.Space => "Space",
            Key.OemPlus => "Plus",
            Key.OemMinus => "Minus",
            Key.OemComma => "Comma",
            Key.OemPeriod => "Period",
            _ => null
        };
    }
}
