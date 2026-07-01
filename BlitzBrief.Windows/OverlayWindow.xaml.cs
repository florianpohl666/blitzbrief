using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using BlitzBrief.Windows.Platform;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace BlitzBrief.Windows;

public partial class OverlayWindow : Window
{
    private readonly DispatcherTimer meterTimer;
    private readonly DispatcherTimer spinnerTimer;
    private readonly Rectangle[] bars;
    private readonly double[] barHeights;
    private float targetLevel;
    private int phase;

    public OverlayWindow()
    {
        InitializeComponent();

        bars = [.. LevelBars.Children.OfType<Rectangle>()];
        barHeights = new double[bars.Length];

        meterTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        meterTimer.Tick += UpdateMeter;

        spinnerTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        spinnerTimer.Tick += (_, _) => SpinnerRotate.Angle = (SpinnerRotate.Angle + 6) % 360;
    }

    // WS_EX_NOACTIVATE: das Overlay darf der Zielanwendung niemals den Fokus nehmen.
    // WS_EX_TOOLWINDOW: kein Alt-Tab-Eintrag.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        WindowStyles.MakeNoActivate(this, toolWindow: true);
    }

    public void ShowListening(string label)
    {
        StatusText.Text = label;
        LevelBars.Visibility = Visibility.Visible;
        MicDot.Visibility = Visibility.Visible;
        Spinner.Visibility = Visibility.Collapsed;
        spinnerTimer.Stop();

        targetLevel = 0f;
        StartMicBlink();
        meterTimer.Start();
        ShowPositioned();
    }

    public void ShowProcessing(string label)
    {
        StatusText.Text = label;
        LevelBars.Visibility = Visibility.Collapsed;
        MicDot.Visibility = Visibility.Collapsed;
        Spinner.Visibility = Visibility.Visible;
        meterTimer.Stop();
        StopMicBlink();
        spinnerTimer.Start();
        ShowPositioned();
    }

    public void HideOverlay()
    {
        meterTimer.Stop();
        spinnerTimer.Stop();
        StopMicBlink();
        Hide();
    }

    public void SetLevel(float level) => targetLevel = level;

    private void ShowPositioned()
    {
        if (!IsVisible)
        {
            Show();
        }

        // Erst nach dem Show kennt WPF die endgültige Größe (SizeToContent).
        UpdateLayout();
        var area = SystemParameters.WorkArea;
        Left = area.Left + (area.Width - ActualWidth) / 2;
        Top = area.Bottom - ActualHeight - 64;
        Topmost = true;
    }

    private void UpdateMeter(object? sender, EventArgs e)
    {
        phase++;
        var mid = (bars.Length - 1) / 2.0;
        for (var i = 0; i < bars.Length; i++)
        {
            // Mittige Balken stärker, leichtes Wabern für den Wellen-Look.
            var shape = 1.0 - Math.Abs(i - mid) / (mid + 1);
            var wobble = 0.75 + 0.25 * Math.Sin((phase * 0.5) + i * 0.9);
            var target = 4.0 + targetLevel * 22.0 * shape * wobble;
            // Glätten Richtung Zielhöhe.
            barHeights[i] += (target - barHeights[i]) * 0.35;
            bars[i].Height = Math.Max(4.0, barHeights[i]);
        }
    }

    private void StartMicBlink()
    {
        var blink = new DoubleAnimation(1.0, 0.35, TimeSpan.FromMilliseconds(700))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        MicDot.BeginAnimation(OpacityProperty, blink);
    }

    private void StopMicBlink() => MicDot.BeginAnimation(OpacityProperty, null);
}
