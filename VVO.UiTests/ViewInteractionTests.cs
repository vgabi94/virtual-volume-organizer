using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// What the views do for themselves: the handlers markup wires up, which no view model can be
/// driven to reach.
/// </summary>
public class ViewInteractionTests : UiTestBase
{
    private Window Showing(Control content)
    {
        var window = new Window { Content = content, Width = 1000, Height = 700 };
        window.Show();
        Pump();

        _shown.Add(window);
        return window;
    }

    private readonly List<Window> _shown = [];

    public override void Dispose()
    {
        foreach (var window in _shown)
        {
            window.Close();
        }

        base.Dispose();
    }

    #region The file grid

    private async Task<VolumeExplorerViewModel> GivenAFolderShownAsync()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        return explorer;
    }

    // The grid has no row activation command, so opening a folder is the view's own doing
    [AvaloniaFact]
    public async Task DoubleTappingAFolderRowOpensIt()
    {
        var explorer = await GivenAFolderShownAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var row = view.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(candidate => (candidate.DataContext as FileItem)?.Name == "AdventOfCode");

        var cell = row.GetVisualDescendants().OfType<DataGridCell>().First();
        cell.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));

        await Until(() => explorer.Breadcrumbs.Count == 2, "the folder to open");
        Assert.Equal(["Code", "AdventOfCode"], explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task DoubleTappingAnythingButARowOpensNothing()
    {
        var explorer = await GivenAFolderShownAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));
        Pump();

        Assert.Equal(["Code"], explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task DoubleTappingAFileRowOpensNothing()
    {
        var explorer = await GivenAFolderShownAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var row = view.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(candidate => (candidate.DataContext as FileItem)?.Name == "readme");

        row.GetVisualDescendants().OfType<DataGridCell>().First()
            .RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));
        Pump();

        Assert.Equal(["Code"], explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    // The Location column sits outside the visual tree and is driven from the code-behind, so
    // the view has to let go of one view model before it starts following the next
    [AvaloniaFact]
    public async Task TheGridFollowsWhicheverExplorerItIsGivenLast()
    {
        var first = await GivenAFolderShownAsync();
        var second = NewExplorer();
        Detach(second);

        var view = new VolumeExplorerView { DataContext = first };
        var window = Showing(view);

        view.DataContext = second;
        Pump();

        var location = window.GetVisualDescendants().OfType<DataGrid>().Single()
            .Columns.Single(column => Equals(column.Header, "Location"));

        Assert.False(location.IsVisible);

        // The one it let go of no longer drives the column
        first.SearchTerm = "day1";
        await Until(() => first.IsFlatMode, "the abandoned search to run");
        Assert.False(location.IsVisible);
    }

    [AvaloniaFact]
    public void AGridWithNoExplorerAtAllHidesTheLocationColumn()
    {
        var view = new VolumeExplorerView();
        var window = Showing(view);

        var location = window.GetVisualDescendants().OfType<DataGrid>().Single()
            .Columns.Single(column => Equals(column.Header, "Location"));

        Assert.False(location.IsVisible);
    }

    #endregion

    #region Copying paths out of the explorer

    private async Task<VolumeExplorerViewModel> GivenAFolderShownWithItsDiskPathAsync()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(
            new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));

        return explorer;
    }

    private static DataGridRow RowOf(Visual view, string name) =>
        view.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(row => (row.DataContext as FileItem)?.Name == name);

    private static string? TipOf(Visual row) =>
        row.GetVisualDescendants()
            .OfType<StackPanel>()
            .Select(ToolTip.GetTip)
            .OfType<string>()
            .FirstOrDefault();

    // Without this the menu would copy whatever row happened to be selected beforehand
    [AvaloniaFact]
    public async Task RightClickingARowMakesItTheOneTheCopyCommandsActOn()
    {
        var explorer = await GivenAFolderShownWithItsDiskPathAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var cell = RowOf(view, "readme").GetVisualDescendants().OfType<DataGridCell>().First();
        cell.RaiseEvent(new ContextRequestedEventArgs());
        Pump();

        Assert.Equal("readme", explorer.SelectedFile?.Name);
    }

    [AvaloniaFact]
    public async Task TheContextMenuOffersTheCopyCommandsAndDelete()
    {
        var explorer = await GivenAFolderShownWithItsDiskPathAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.ContextMenu!.Open(grid);
        Pump();

        var items = grid.ContextMenu.Items.OfType<MenuItem>().ToList();
        Assert.Equal(5, items.Count);
        Assert.Same(explorer.CopyCommand, items[0].Command);
        Assert.Same(explorer.CopyAllCommand, items[1].Command);
        Assert.Same(explorer.CopyPhysicalCommand, items[2].Command);
        Assert.Same(explorer.CopyPhysicalAllCommand, items[3].Command);
        Assert.Same(explorer.DeleteCommand, items[4].Command);

        grid.ContextMenu.Close();
    }

    [AvaloniaFact]
    public async Task RightClickingAnUnselectedRowDropsTheRestOfTheSelection()
    {
        var explorer = await GivenAFolderShownWithItsDiskPathAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItem = explorer.Files!.Single(file => file.Name == "AdventOfCode");
        Pump();

        var cell = RowOf(view, "readme").GetVisualDescendants().OfType<DataGridCell>().First();
        cell.RaiseEvent(new ContextRequestedEventArgs());
        Pump();

        Assert.Equal("readme", explorer.SelectedFile?.Name);
        Assert.Equal(["readme"], explorer.SelectedFiles.Select(file => file.Name));
    }

    [AvaloniaFact]
    public async Task RightClickingInsideTheSelectionLeavesIt()
    {
        var explorer = await GivenAFolderShownWithItsDiskPathAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        grid.SelectedItems.Clear();
        foreach (var file in explorer.Files!)
        {
            grid.SelectedItems.Add(file);
        }
        Pump();

        var cell = RowOf(view, "readme").GetVisualDescendants().OfType<DataGridCell>().First();
        cell.RaiseEvent(new ContextRequestedEventArgs());
        Pump();

        Assert.Equal(2, explorer.SelectedFiles.Count);
        Assert.Contains(explorer.SelectedFiles, file => file.Name == "readme");
        Assert.Contains(explorer.SelectedFiles, file => file.Name == "AdventOfCode");
    }

    // The view models are covered against a stub; this is the one path to the real clipboard
    [AvaloniaFact]
    public async Task CopyingPutsThePathOnTheClipboardTheApplicationActuallyUses()
    {
        var explorer = await GivenAFolderShownWithItsDiskPathAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        await explorer.CopyPhysicalCommand.ExecuteAsync(null);

        await Until(
            () => Shell.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() != null,
            "the copy");

        Assert.Equal(@"D:\Code\readme.md", await Shell.Clipboard!.TryGetTextAsync());
    }

    [AvaloniaFact]
    public async Task HoveringAnEntryOffersBothOfItsPaths()
    {
        var explorer = await GivenAFolderShownWithItsDiskPathAsync();
        var view = new VolumeExplorerView { DataContext = explorer };
        Showing(view);

        var tip = TipOf(RowOf(view, "readme"));

        Assert.Contains(@"test:\Code\readme.md", tip);
        Assert.Contains(@"D:\Code\readme.md", tip);
    }

    #endregion

    #region The comparison results

    private CompareResultsViewModel SomeResults()
    {
        FileRecord Record(string name, long size) => new()
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            Name = name,
            Size = size
        };

        var results = new List<ComparisonResult>
        {
            new() { RelativePath = "added.txt", Status = ComparisonStatus.Added, Right = Record("added.txt", 10) },
            new() { RelativePath = "gone.txt", Status = ComparisonStatus.Removed, Left = Record("gone.txt", 20) },
            new()
            {
                RelativePath = @"src\changed.txt",
                Status = ComparisonStatus.Changed,
                Changes = ChangeKind.Size | ChangeKind.Modified,
                Left = Record("changed.txt", 30),
                Right = Record("changed.txt", 40)
            },
            // Carries no changes of its own: a parent that exists only to hold the row below it
            new() { RelativePath = "src", Status = ComparisonStatus.Changed, Left = Record("src", 30) }
        };

        return new CompareResultsViewModel("Code", @"D:\Code", "Backup", @"E:\Backup", results);
    }

    private static Button ButtonNamed(Window window, string content) =>
        window.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, content));

    private static void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    // The column trims a long path, so the tip is the only place it can be read in full
    [AvaloniaFact]
    public void HoveringAComparisonRowOffersThePathUnderItsOwnRoot()
    {
        var view = new CompareResultsView { DataContext = SomeResults() };
        view.Show();
        Pump();
        _shown.Add(view);

        var added = view.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(row => (row.DataContext as ComparisonRow)?.Path == "added.txt");

        var removed = view.GetVisualDescendants()
            .OfType<DataGridRow>()
            .Single(row => (row.DataContext as ComparisonRow)?.Path == "gone.txt");

        // Added exists only under the target, removed only under the source
        Assert.Equal(@"E:\Backup\added.txt", TipOf(added));
        Assert.Equal(@"D:\Code\gone.txt", TipOf(removed));
    }

    [AvaloniaFact]
    public async Task EachRowIsMarkedWithWhatBecameOfIt()
    {
        var view = new CompareResultsView { DataContext = SomeResults() };
        view.Show();
        Pump();
        _shown.Add(view);

        var rows = view.GetVisualDescendants().OfType<DataGridRow>().ToList();
        var classes = rows
            .Where(row => row.DataContext is ComparisonRow)
            .ToDictionary(row => ((ComparisonRow)row.DataContext!).Path, row => row.Classes.ToList());

        Assert.Contains("Added", classes["added.txt"]);
        Assert.Contains("Removed", classes["gone.txt"]);
        Assert.Contains("Changed", classes[@"src\changed.txt"]);

        // and each row carries only its own marking, since rows are reused as the grid scrolls
        Assert.DoesNotContain("Removed", classes["added.txt"]);

        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task CopyingPutsTheFullPathsOfOneStatusOnTheClipboard()
    {
        var view = new CompareResultsView { DataContext = SomeResults() };
        view.Show();
        Pump();
        _shown.Add(view);

        Press(ButtonNamed(view, "Copy added"));
        await Until(() => view.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() != null, "the copy");

        // An added entry only exists under the target, so that is the root its path is under
        Assert.Equal(@"E:\Backup\added.txt", await view.Clipboard!.TryGetTextAsync());

        Press(ButtonNamed(view, "Copy removed"));
        await Until(() => view.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == @"D:\Code\gone.txt", "the copy");

        Press(ButtonNamed(view, "Copy changed"));
        await Until(
            () => view.Clipboard!.TryGetTextAsync().GetAwaiter().GetResult() == @"D:\Code\src\changed.txt",
            "the copy");
    }

    [AvaloniaFact]
    public async Task CopyingFromResultsWithNothingOfThatStatusPutsNothingOnTheClipboard()
    {
        var view = new CompareResultsView
        {
            DataContext = new CompareResultsViewModel("Code", @"D:\Code", "Backup", @"E:\Backup", [])
        };
        view.Show();
        Pump();
        _shown.Add(view);

        Press(ButtonNamed(view, "Copy added"));
        Pump();

        Assert.Null(await view.Clipboard!.TryGetTextAsync());
    }

    [AvaloniaFact]
    public void CopyingFromAWindowWithNoResultsBehindItIsHarmless()
    {
        var view = new CompareResultsView();
        view.Show();
        Pump();
        _shown.Add(view);

        Press(ButtonNamed(view, "Copy added"));
        Pump();

        Assert.Empty(Told);
    }

    // The window is a dialog when it is proposing an update, and which of its two buttons was
    // pressed is the whole of the answer the update waits on
    private static CompareResultsView ProposedUpdate() => new()
    {
        DataContext = new CompareResultsViewModel(
            "Code", @"D:\Code", "Backup", @"E:\Backup", [], isUpdate: true)
    };

    [AvaloniaFact]
    public async Task ApprovingAProposedUpdateHandsBackYes()
    {
        var view = ProposedUpdate();
        var pending = view.ShowDialog<bool>(Shell);
        Pump();

        Press(ButtonNamed(view, "Update"));
        Pump();

        Assert.True(await pending);
    }

    [AvaloniaFact]
    public async Task TurningDownAProposedUpdateHandsBackNo()
    {
        var view = ProposedUpdate();
        var pending = view.ShowDialog<bool>(Shell);
        Pump();

        Press(ButtonNamed(view, "Cancel"));
        Pump();

        Assert.False(await pending);
    }

    #endregion
}
