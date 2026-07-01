using System.Windows;
using System.Windows.Input;
using BlitzBrief.Core.Models;
using BlitzBrief.Windows.Platform;

namespace BlitzBrief.Windows;

public partial class FloatingToolbarWindow : Window
{
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
        WindowStyles.MakeNoActivate(this);
    }

    // Ein Handler für alle Workflow-Buttons; der Workflow steckt im Tag des Buttons.
    private void Workflow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: WorkflowType type })
        {
            WorkflowRequested?.Invoke(this, type);
        }
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
