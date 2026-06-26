using System.Windows;
using System.Windows.Controls;
using SWM = System.Windows.Media;

namespace BlitzBrief.Windows;

public sealed class DebugOutputWindow : Window
{
    public DebugOutputWindow(string rawTranscript, string stage1, string stage2)
    {
        Title = "BlitzBrief – Debug";
        Width = 720;
        Height = 720;
        MinWidth = 500;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        FontFamily = new SWM.FontFamily("Segoe UI");
        FontSize = 13;
        Background = new SWM.SolidColorBrush(SWM.Color.FromRgb(245, 247, 250));

        var outer = new Grid { Margin = new Thickness(20) };
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        outer.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        outer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var labelRaw = MakeLabel("Whisper roh (vor Kommandoersetzung)", topMargin: 0);
        Grid.SetRow(labelRaw, 0);
        outer.Children.Add(labelRaw);

        var boxRaw = MakeTextBox(rawTranscript);
        Grid.SetRow(boxRaw, 1);
        outer.Children.Add(boxRaw);

        var label1 = MakeLabel("Stufe 1 – nach Kommandoersetzung", topMargin: 12);
        Grid.SetRow(label1, 2);
        outer.Children.Add(label1);

        var box1 = MakeTextBox(stage1);
        Grid.SetRow(box1, 3);
        outer.Children.Add(box1);

        var label2 = MakeLabel("Stufe 2 – Ergebnis nach Rewrite", topMargin: 12);
        Grid.SetRow(label2, 4);
        outer.Children.Add(label2);

        var box2 = MakeTextBox(stage2);
        Grid.SetRow(box2, 5);
        outer.Children.Add(box2);

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
        Grid.SetRow(closeBtn, 6);
        outer.Children.Add(closeBtn);

        Content = outer;
    }

    private static TextBlock MakeLabel(string text, int topMargin)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
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
