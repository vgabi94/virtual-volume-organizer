using Avalonia.Controls;
using Avalonia.Interactivity;

namespace VVO.UI.Views;

public partial class NoticesDialogView : Window
{
    public NoticesDialogView()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
