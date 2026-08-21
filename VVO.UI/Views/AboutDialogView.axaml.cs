using Avalonia.Controls;
using Avalonia.Interactivity;
using VVO.UI.ViewModels;

namespace VVO.UI.Views;

public partial class AboutDialogView : Window
{
    public AboutDialogView()
    {
        InitializeComponent();
    }

    private async void OnNoticesClicked(object sender, RoutedEventArgs e)
    {
        var notices = new DocumentDialogView { DataContext = DocumentDialogViewModel.Notices() };
        await Dialogs.ShowAsync(notices, this);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
