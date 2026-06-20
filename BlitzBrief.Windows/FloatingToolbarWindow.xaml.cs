using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using BlitzBrief.Core.Models;

namespace BlitzBrief.Windows;

public partial class FloatingToolbarWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;

    public event EventHandler<WorkflowType>? WorkflowRequested;

    public FloatingToolbarWindow()
    {
        InitializeComponent();
        Left = 24;
        Top = 24;
    }

    // WS_EX_NOACTIVATE: Klicks auf die Leiste lassen den Tastaturfokus in der
    // Zielanwendung, damit das automatische Einfügen dort ankommt.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExNoActivate);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);

    private void Transcription_Click(object sender, RoutedEventArgs e)
    {
        WorkflowRequested?.Invoke(this, WorkflowType.Transcription);
    }

    private void TextImprover_Click(object sender, RoutedEventArgs e)
    {
        WorkflowRequested?.Invoke(this, WorkflowType.TextImprover);
    }

    private void Dampf_Click(object sender, RoutedEventArgs e)
    {
        WorkflowRequested?.Invoke(this, WorkflowType.DampfAblassen);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }

    private void Toolbar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
