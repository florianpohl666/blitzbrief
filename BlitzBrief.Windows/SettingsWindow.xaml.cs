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

    public SettingsWindow(AppSettings settings, SettingsStore settingsStore, ApiKeyStore apiKeyStore)
    {
        InitializeComponent();
        this.settings = settings;
        this.settingsStore = settingsStore;
        this.apiKeyStore = apiKeyStore;
        LoadValues();
    }

    private void LoadValues()
    {
        ApiKeyStatusText.Text = apiKeyStore.IsConfigured
            ? $"OpenAI API Key gespeichert: {apiKeyStore.DisplayValue}"
            : "Noch kein OpenAI API Key gespeichert.";

        LanguageBox.Text = settings.Language;
        TranscriptionModelBox.Text = settings.TranscriptionModel;
        HotkeyModeBox.SelectedIndex = settings.HotkeyMode == HotkeyMode.Hold ? 1 : 0;
        AutoPasteBox.IsChecked = settings.AutoPaste;
        AutoPasteDelayBox.IsChecked = settings.AutoPasteDelay;
        DoubleTapEnabledBox.IsChecked = settings.DoubleTapEnabled;
        DoubleTapModifierBox.SelectedIndex = settings.DoubleTapModifier switch
        {
            ModifierKey.Alt => 1,
            ModifierKey.Shift => 2,
            _ => 0
        };
        PreRollEnabledBox.IsChecked = settings.PreRollEnabled;
        DebugModeBox.IsChecked = settings.DebugMode;
        TranscriptionHotkeyBox.Text = settings.WorkflowHotkeys[WorkflowType.Transcription];
        TextImproverHotkeyBox.Text = settings.WorkflowHotkeys[WorkflowType.TextImprover];
        DampfHotkeyBox.Text = settings.WorkflowHotkeys[WorkflowType.DampfAblassen];
        EmojiHotkeyBox.Text = settings.WorkflowHotkeys[WorkflowType.EmojiText];
        ToneBox.SelectedIndex = settings.TextImprovement.Tone switch
        {
            TextTone.Formal => 0,
            TextTone.Casual => 2,
            TextTone.JornMinimal => 3,
            TextTone.JornCommands => 4,
            _ => 1
        };
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

    private void ToneBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        CommandsInfoBadge.Visibility = ToneBox.SelectedIndex == 4
            ? Visibility.Visible
            : Visibility.Collapsed;
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

        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
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
    }

    private void HotkeyBox_PreviewKeyUp(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox box)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key is not (Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin))
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
    }

    private void ResetHotkeys_Click(object sender, RoutedEventArgs e)
    {
        var defaults = AppSettings.DefaultHotkeys();
        TranscriptionHotkeyBox.Text = defaults[WorkflowType.Transcription];
        TextImproverHotkeyBox.Text = defaults[WorkflowType.TextImprover];
        DampfHotkeyBox.Text = defaults[WorkflowType.DampfAblassen];
        EmojiHotkeyBox.Text = defaults[WorkflowType.EmojiText];
        HotkeyHelpText.Text = "Standard-Hotkeys wiederhergestellt. Bitte speichern, damit sie aktiv werden.";
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

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            settings.Language = string.IsNullOrWhiteSpace(LanguageBox.Text) ? "de" : LanguageBox.Text.Trim();
            settings.TranscriptionModel = string.IsNullOrWhiteSpace(TranscriptionModelBox.Text)
                ? "gpt-4o-mini-transcribe"
                : TranscriptionModelBox.Text.Trim();
            settings.HotkeyMode = HotkeyModeBox.SelectedIndex == 1 ? HotkeyMode.Hold : HotkeyMode.Toggle;
            settings.AutoPaste = AutoPasteBox.IsChecked == true;
            settings.AutoPasteDelay = AutoPasteDelayBox.IsChecked == true;
            settings.DoubleTapEnabled = DoubleTapEnabledBox.IsChecked == true;
            settings.DoubleTapModifier = DoubleTapModifierBox.SelectedIndex switch
            {
                1 => ModifierKey.Alt,
                2 => ModifierKey.Shift,
                _ => ModifierKey.Ctrl
            };
            settings.PreRollEnabled = PreRollEnabledBox.IsChecked == true;
            settings.AudioInputDeviceNumber = MicrophoneBox.SelectedValue is int selectedMicrophone
                ? selectedMicrophone
                : 0;

            var hotkeys = new Dictionary<WorkflowType, string>
            {
                [WorkflowType.Transcription] = TranscriptionHotkeyBox.Text.Trim(),
                [WorkflowType.TextImprover] = TextImproverHotkeyBox.Text.Trim(),
                [WorkflowType.DampfAblassen] = DampfHotkeyBox.Text.Trim(),
                [WorkflowType.EmojiText] = EmojiHotkeyBox.Text.Trim()
            };

            var hotkeyError = ValidateHotkeys(hotkeys);
            if (hotkeyError is not null)
            {
                SaveStatusText.Text = hotkeyError;
                return;
            }

            settings.WorkflowHotkeys[WorkflowType.Transcription] = TranscriptionHotkeyBox.Text.Trim();
            settings.WorkflowHotkeys[WorkflowType.TextImprover] = TextImproverHotkeyBox.Text.Trim();
            settings.WorkflowHotkeys[WorkflowType.DampfAblassen] = DampfHotkeyBox.Text.Trim();
            settings.WorkflowHotkeys[WorkflowType.EmojiText] = EmojiHotkeyBox.Text.Trim();
            settings.TextImprovement.Tone = ToneBox.SelectedIndex switch
            {
                0 => TextTone.Formal,
                2 => TextTone.Casual,
                3 => TextTone.JornMinimal,
                4 => TextTone.JornCommands,
                _ => TextTone.Neutral
            };
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

            await settingsStore.SaveAsync(settings);
            SettingsSaved?.Invoke(this, EventArgs.Empty);
            SaveStatusText.Text = "Gespeichert.";
        }
        catch (Exception ex)
        {
            SaveStatusText.Text = ex.Message;
        }
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

    private static string? BuildModifierOnlyText(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        return parts.Count >= 2 ? string.Join("+", parts) : null;
    }

    private static int CountModifiers(ModifierKeys modifiers) =>
        (modifiers.HasFlag(ModifierKeys.Control) ? 1 : 0) +
        (modifiers.HasFlag(ModifierKeys.Alt) ? 1 : 0) +
        (modifiers.HasFlag(ModifierKeys.Shift) ? 1 : 0) +
        (modifiers.HasFlag(ModifierKeys.Windows) ? 1 : 0);

    private static string? BuildHotkeyText(Key key)
    {
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
        {
            return null;
        }

        var parts = new List<string>();
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        if (parts.Count == 0)
        {
            return null;
        }

        var keyText = KeyToText(key);
        if (keyText is null)
        {
            return null;
        }

        parts.Add(keyText);
        return string.Join("+", parts);
    }

    private static string? KeyToText(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
        {
            return key.ToString();
        }

        if (key is >= Key.D0 and <= Key.D9)
        {
            return ((int)(key - Key.D0)).ToString();
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return ((int)(key - Key.NumPad0)).ToString();
        }

        if (key is >= Key.F1 and <= Key.F24)
        {
            return key.ToString();
        }

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
