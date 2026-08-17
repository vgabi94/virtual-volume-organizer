using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI.Views;

public partial class ConfirmDeleteDialogView : Window
{
    public ConfirmDeleteDialogView()
    {
        InitializeComponent();
    }

    private void OnDeleteClicked(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
