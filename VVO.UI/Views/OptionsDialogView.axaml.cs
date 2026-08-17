using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI.Views;

public partial class OptionsDialogView : Window
{
    public OptionsDialogView()
    {
        InitializeComponent();
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
