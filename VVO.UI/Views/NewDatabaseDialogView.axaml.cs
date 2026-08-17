using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI.Views;

public partial class NewDatabaseDialogView : Window
{
    public NewDatabaseDialogView()
    {
        InitializeComponent();
    }

    private void OnCreateClicked(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
