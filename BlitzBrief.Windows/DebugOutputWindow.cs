using System.Windows;
using System.Windows.Controls;
using BlitzBrief.Core;
using BlitzBrief.Windows.Platform;
using SWM = System.Windows.Media;
using SWS = System.Windows.Shapes;

namespace BlitzBrief.Windows;

public sealed class DebugOutputWindow : Window
{
    // Ein Abschnitt im Fenster: Überschrift + Inhalt. Text-Abschnitte bekommen eine
    // scrollbare Textbox (Star-Höhe), Element-Abschnitte ein festes Element (Auto-Höhe).
    private abstract record Section(string Label);
    private sealed record TextSection(string Label, string Content, double Weight) : Section(Label);
    private sealed record ElementSection(string Label, UIElement Element) : Section(Label);

    /// <param name="surroundings">Cursor-Kontext (nur Kontext-Modus) – Text links/rechts vom Cursor.</param>
    /// <param name="trim">Silero-Trim-Diagnose (nur Kontext-Modus) – Onset, getrimmte ms, Wahrscheinlichkeits-Verlauf.</param>
    public DebugOutputWindow(
        string rawTranscript, string stage1, string stage2,
        CursorSurroundings? surroundings = null, SpeechTrimInfo? trim = null)
    {
        Title = "BlitzBrief – Debug";
        Width = 760;
        Height = 900;
        MinWidth = 520;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        FontFamily = new SWM.FontFamily("Segoe UI");
        FontSize = 13;
        Background = new SWM.SolidColorBrush(SWM.Color.FromRgb(245, 247, 250));

        // Abschnitte in Pipeline-Reihenfolge: Cursor-Kontext → Audio-Trim → Transkriptionsstufen.
        var sections = new List<Section>();

        if (surroundings is not null)
        {
            var sentence = SentenceContext.CurrentSentence(surroundings.Preceding);
            var leftHint = sentence is null
                ? "KEIN offener Satz erkannt → Fortsetzung wird großgeschrieben"
                : $"an Whisper-1 gehängt: \"{sentence}\"";
            sections.Add(new TextSection(
                $"Kontext links – vor Cursor ({surroundings.Preceding?.Length ?? 0} Zeichen) · {leftHint}",
                FormatContext(surroundings.Preceding), 0.8));
            sections.Add(new TextSection(
                $"Kontext rechts – nach Cursor ({surroundings.Following?.Length ?? 0} Zeichen)",
                FormatContext(surroundings.Following), 0.8));
        }

        if (trim is not null)
        {
            var o = trim.Onset;
            var maxProb = o.Probabilities.Count > 0 ? o.Probabilities.Max() : 0f;
            string metrics;
            if (!trim.ModelAvailable)
            {
                metrics = "Silero-VAD nicht verfügbar (ONNX Runtime konnte nicht geladen werden) → kein Trim.";
            }
            else
            {
                metrics =
                    $"Sprachbeginn: {(o.Detected ? o.OnsetMs + " ms" : "nicht erkannt")}\n" +
                    $"Getrimmt: {trim.TrimmedMs} ms\n" +
                    $"max. Sprachwahrscheinlichkeit: {maxProb:F2}\n" +
                    $"Fenster: {o.Probabilities.Count} × {o.FrameMs} ms";
            }
            sections.Add(new TextSection("Audio-Trim (Silero-VAD)", metrics, 0.6));
            if (trim.ModelAvailable && o.Probabilities.Count > 0)
            {
                sections.Add(new ElementSection(
                    "Sprachwahrscheinlichkeit  (grau = getrimmt · blau = behalten · rot = Schwelle · grün = Schnitt)",
                    BuildProbabilityGraph(o)));
            }
        }

        sections.Add(new TextSection("Whisper roh (vor Kommandoersetzung)", rawTranscript, 1));
        sections.Add(new TextSection("Stufe 1 – nach Kommandoersetzung", stage1, 1));
        sections.Add(new TextSection("Stufe 2 – Ergebnis nach Rewrite", stage2, 1));

        var outer = new Grid { Margin = new Thickness(20) };
        foreach (var section in sections)
        {
            outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Label
            outer.RowDefinitions.Add(section is TextSection ts
                ? new RowDefinition { Height = new GridLength(ts.Weight, GridUnitType.Star) }
                : new RowDefinition { Height = GridLength.Auto });                     // Inhalt
        }
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });      // Button

        var row = 0;
        for (var i = 0; i < sections.Count; i++)
        {
            var label = MakeLabel(sections[i].Label, topMargin: i == 0 ? 0 : 12);
            Grid.SetRow(label, row++);
            outer.Children.Add(label);

            UIElement content = sections[i] switch
            {
                TextSection t => MakeTextBox(t.Content),
                ElementSection e => e.Element,
                _ => MakeTextBox("")
            };
            Grid.SetRow((FrameworkElement)content, row++);
            outer.Children.Add(content);
        }

        var closeBtn = new System.Windows.Controls.Button
        {
            Content = "Schließen",
            Width = 120,
            Height = 34,
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Padding = new Thickness(14, 6, 14, 6),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeBtn.Click += (_, _) => Close();
        Grid.SetRow(closeBtn, row);
        outer.Children.Add(closeBtn);

        Content = outer;
    }

    // Kleines Diagramm: pro 32-ms-Fenster ein Balken (Silero-Sprachwahrscheinlichkeit 0..1),
    // Schwellen-Linie und Schnitt-Markierung. Zeichnet bei Größenänderung neu.
    private static FrameworkElement BuildProbabilityGraph(SpeechOnset onset)
    {
        var gray = new SWM.SolidColorBrush(SWM.Color.FromRgb(176, 186, 199));
        var blue = new SWM.SolidColorBrush(SWM.Color.FromRgb(41, 121, 196));
        var red = new SWM.SolidColorBrush(SWM.Color.FromRgb(214, 69, 65));
        var green = new SWM.SolidColorBrush(SWM.Color.FromRgb(34, 153, 84));

        var canvas = new Canvas { Background = SWM.Brushes.White, ClipToBounds = true };
        var probs = onset.Probabilities;
        // Schnitt-Fenster = Onset abzüglich Pad; aus OnsetFrame, sonst 0.
        var cutFrame = onset.Detected ? onset.OnsetFrame : 0;

        void Redraw()
        {
            canvas.Children.Clear();
            double w = canvas.ActualWidth, h = canvas.ActualHeight;
            var n = probs.Count;
            if (w <= 0 || h <= 0 || n == 0)
            {
                return;
            }

            var bw = w / n;
            for (var i = 0; i < n; i++)
            {
                var p = Math.Clamp(probs[i], 0f, 1f);
                var barH = h * p;
                var bar = new SWS.Rectangle
                {
                    Width = Math.Max(1.0, bw - 0.5),
                    Height = Math.Max(0.0, barH),
                    Fill = i < cutFrame ? gray : blue
                };
                Canvas.SetLeft(bar, i * bw);
                Canvas.SetTop(bar, h - barH);
                canvas.Children.Add(bar);
            }

            var ty = h * (1 - onset.Threshold);
            canvas.Children.Add(new SWS.Line
            {
                X1 = 0, X2 = w, Y1 = ty, Y2 = ty,
                Stroke = red, StrokeThickness = 1, StrokeDashArray = new SWM.DoubleCollection { 3, 3 }
            });

            if (onset.Detected)
            {
                var cx = cutFrame * bw;
                canvas.Children.Add(new SWS.Line
                {
                    X1 = cx, X2 = cx, Y1 = 0, Y2 = h,
                    Stroke = green, StrokeThickness = 2
                });
            }
        }

        canvas.SizeChanged += (_, _) => Redraw();

        return new Border
        {
            BorderBrush = new SWM.SolidColorBrush(SWM.Color.FromRgb(201, 210, 223)),
            BorderThickness = new Thickness(1),
            Height = 150,
            Child = canvas
        };
    }

    // Unterscheidet "nicht gelesen" (null) von "gelesen, aber leer".
    private static string FormatContext(string? text) => text switch
    {
        null => "<null – kein Kontext gelesen (App ohne Textzugriff oder Timeout)>",
        "" => "<leer – gelesen, aber kein Text an dieser Cursorseite>",
        _ => text
    };

    private static TextBlock MakeLabel(string text, int topMargin)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, topMargin, 0, 5),
            Foreground = new SWM.SolidColorBrush(SWM.Color.FromRgb(23, 32, 42))
        };
    }

    private static System.Windows.Controls.TextBox MakeTextBox(string text)
    {
        return new System.Windows.Controls.TextBox
        {
            Text = text,
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(9, 6, 9, 6),
            BorderBrush = new SWM.SolidColorBrush(SWM.Color.FromRgb(201, 210, 223)),
            BorderThickness = new Thickness(1),
            Background = SWM.Brushes.White,
            VerticalContentAlignment = VerticalAlignment.Top
        };
    }
}
