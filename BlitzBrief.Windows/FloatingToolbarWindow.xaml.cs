using System.Windows;
using System.Windows.Input;
using BlitzBrief.Core.Models;

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
