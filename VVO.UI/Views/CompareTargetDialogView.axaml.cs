using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI.Views;

public partial class CompareTargetDialogView : Window
{
    public CompareTargetDialogView()
    {
        InitializeComponent();
    }

    private void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
