using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using VVO.Core.Models;
using VVO.UI.ViewModels;

namespace VVO.UI.Views;

public partial class CompareResultsView : Window
{
    public CompareResultsView()
    {
        InitializeComponent();
    }

    // Rows carry no styling classes of their own, and they are recycled as the grid scrolls,
    // so every class is set explicitly on each load rather than only the matching one.
    private void OnRowLoading(object? sender, DataGridRowEventArgs e)
    {
        if (e.Row.DataContext is not ComparisonRow row)
            return;

        e.Row.Classes.Set("Added", row.Status == ComparisonStatus.Added);
        e.Row.Classes.Set("Removed", row.Status == ComparisonStatus.Removed);
        e.Row.Classes.Set("Changed", row.Status == ComparisonStatus.Changed);
    }

    private void OnCopyAddedClicked(object sender, RoutedEventArgs e) => _ = CopyAsync(ComparisonStatus.Added);

    private void OnCopyRemovedClicked(object sender, RoutedEventArgs e) => _ = CopyAsync(ComparisonStatus.Removed);

    private void OnCopyChangedClicked(object sender, RoutedEventArgs e) => _ = CopyAsync(ComparisonStatus.Changed);

    private async Task CopyAsync(ComparisonStatus status)
    {
        if (DataContext is not CompareResultsViewModel viewModel || Clipboard == null)
            return;

        var paths = viewModel.PathsFor(status);
        if (paths.Length > 0)
        {
            await Clipboard.SetTextAsync(paths);
        }
    }

    // Answered rather than simply closed: shown to have an update approved, the window is a
    // dialog and the caller is waiting on which of the two buttons was pressed
    private void OnUpdateClicked(object sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
