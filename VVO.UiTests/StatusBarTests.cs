using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Messaging;
using VVO.UI;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The Modern layout gathers the item summary, a running operation and the database status into a
/// status bar under the explorer; the Classic one keeps them in the menu bar and the explorer header.
/// </summary>
public class StatusBarTests : UiTestBase
{
    private const int StatusBarRow = 2;

    private (MainWindowView Window, MainWindowViewModel Model) Opened(bool modern)
    {
        var model = NewMainWindow();
        model.Receive(new HideStartPage());
        model.Receive(new DatabaseReady());
        model.VolumeExplorerViewModel.ItemSummary = "3 items · 45 B";

        var window = new MainWindowView { DataContext = model };
        window.Show();
        Layout.Apply(modern);
        Pump();

        return (window, model);
    }

    private static T Classed<T>(Window window, string name) where T : Control =>
        window.GetVisualDescendants().OfType<T>().Single(control => control.Classes.Contains(name));

    private static bool Shown(Control control) => control.IsEffectivelyVisible;

    [AvaloniaFact]
    public void TheModernLayoutShowsTheItemSummaryInTheStatusBarRatherThanTheHeader()
    {
        var (window, _) = Opened(modern: true);

        Assert.True(Shown(Classed<Border>(window, "StatusBar")));
        var summary = Classed<TextBlock>(window, "StatusSummary");
        Assert.True(Shown(summary));
        Assert.Equal("3 items · 45 B", summary.Text);
        Assert.False(Shown(Classed<TextBlock>(window, "HeaderSummary")));
        window.Close();
    }

    [AvaloniaFact]
    public void TheClassicLayoutKeepsTheItemSummaryInTheHeaderAndHasNoStatusBar()
    {
        var (window, _) = Opened(modern: false);

        Assert.False(Shown(Classed<Border>(window, "StatusBar")));
        Assert.False(Shown(Classed<TextBlock>(window, "StatusSummary")));
        var header = Classed<TextBlock>(window, "HeaderSummary");
        Assert.True(Shown(header));
        Assert.Equal("3 items · 45 B", header.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheStartPageHasNoStatusBar()
    {
        var model = NewMainWindow();
        var window = new MainWindowView { DataContext = model };
        window.Show();
        Layout.Apply(true);
        Pump();

        Assert.False(Shown(Classed<Border>(window, "StatusBar")));
        Assert.False(Shown(Classed<TextBlock>(window, "StatusSummary")));
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true, StatusBarRow)]
    [InlineData(false, 0)]
    public void ARunningOperationShowsItsMessageAndCancelWhereTheLayoutPutsThem(bool modern, int row)
    {
        var (window, _) = Opened(modern);
        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Scanning...", true));
        Pump();

        var status = Classed<StackPanel>(window, "StatusMessage");
        Assert.True(Shown(status));
        Assert.Equal(row, Grid.GetRow(status));
        Assert.Equal("Scanning...", Classed<TextBlock>(window, "StatusMessage").Text);

        var cancel = status.GetVisualDescendants().OfType<Button>().Single();
        Assert.True(Shown(cancel));
        using var probe = new MessageProbe<CancelRequestedMessage>();
        cancel.Command!.Execute(cancel.CommandParameter);
        Assert.Single(probe.All);
        window.Close();
    }

    [AvaloniaFact]
    public void TheModernLayoutShowsARunningOperationOnTheRightInTheLastWriteIconsPlace()
    {
        var (window, _) = Opened(modern: true);
        var icon = Classed<PathIcon>(window, "DatabaseStatus");
        var iconRight = icon.Bounds.Right;

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Scanning... 1 files", true));
        Pump();

        var status = Classed<StackPanel>(window, "StatusMessage");
        Assert.False(Shown(icon));
        Assert.Equal(HorizontalAlignment.Right, status.HorizontalAlignment);
        Assert.Equal(iconRight, status.Bounds.Right, 0.5);
        Assert.Equal(TextAlignment.Right, Classed<TextBlock>(window, "StatusMessage").TextAlignment);

        // The summary keeps the left
        var summary = Classed<TextBlock>(window, "StatusSummary");
        Assert.True(Shown(summary));
        Assert.True(summary.Bounds.Right < status.Bounds.Left);

        // A count that grows reads further left, and what follows it stays put
        var indicator = status.Children[1];
        var indicatorAt = indicator.TranslatePoint(default, window);
        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Scanning... 1,234,567 files", true));
        Pump();
        Assert.Equal(indicatorAt, indicator.TranslatePoint(default, window));

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "", false));
        Pump();
        Assert.False(Shown(status));
        Assert.True(Shown(icon));
        Assert.Equal(iconRight, icon.Bounds.Right);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true, StatusBarRow)]
    [InlineData(false, 0)]
    public void TheDatabaseStatusTellsTheLastWriteWhereverTheLayoutPutsIt(bool modern, int row)
    {
        var (window, model) = Opened(modern);

        var icon = Classed<PathIcon>(window, "DatabaseStatus");
        Assert.True(Shown(icon));
        Assert.Equal(row, Grid.GetRow(icon));
        Assert.Equal(model.LastWriteText, ToolTip.GetTip(icon));
        Assert.Contains("Last Write", model.LastWriteText);
        window.Close();
    }
}
