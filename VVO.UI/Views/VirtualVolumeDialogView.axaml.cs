using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI.Views;

public partial class VirtualVolumeDialogView : Window
{
    public VirtualVolumeDialogView()
    {
        InitializeComponent();
    }

    private void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
