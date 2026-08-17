using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The sidebar's Search all box and the volume explorer it reports into, which only talk to
/// each other through a broadcast.
/// </summary>
public class SearchAndExplorerTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }
    private VolumeExplorerViewModel Explorer { get; }

    public SearchAndExplorerTests()
    {
        Sidebar = NewSidebar();
        Explorer = NewExplorer();

        // Driven directly by these tests, so it must not also be answering broadcasts on a
        // thread of its own and reading the same database at the same time
        Detach(Explorer);
    }

    private async Task GivenTwoVolumesAsync()
    {
        var one = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await Volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        await AddFolderAsync(one.Id, "Code");
        await AddFolderAsync(two.Id, "Docs");

        await Sidebar.LoadAsync();
    }

    private SearchAllMessage SearchFor(string term)
    {
        var scopes = Sidebar.VirtualVolumes
            .SelectMany(node => node.Folders.Select(
                folder => new SearchScope(folder.Entry.TreeId, folder.Title, node.Name)))
            .ToList();

        return new SearchAllMessage(term, scopes);
    }

    #region Searching every folder

    [AvaloniaFact]
    public async Task AHitIsFoundInEveryVolumeAndHeadedByItsOwn()
    {
        await GivenTwoVolumesAsync();

        await Explorer.SearchAllAsync(SearchFor("AdventOfCode"));

        Assert.True(Explorer.IsFlatMode);
        Assert.Equal(
            [@"test2:\Docs", @"test:\Code"],
            Explorer.Files!.Select(file => file.Location).OrderBy(l => l, StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task ASearchThatMatchesNothingSaysSo()
    {
        await GivenTwoVolumesAsync();

        await Explorer.SearchAllAsync(SearchFor("nowhere"));

        Assert.Empty(Explorer.Files!);
        Assert.Equal("0 matches", Explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task ClearingTheTermGoesBackToTheFolderTheUserCameFrom()
    {
        await GivenTwoVolumesAsync();
        var folder = Sidebar.VirtualVolumes.First().Folders.Single();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(folder.Entry.TreeId, "Code", "test"));

        await Explorer.SearchAllAsync(SearchFor("AdventOfCode"));
        await Explorer.SearchAllAsync(SearchFor(string.Empty));

        Assert.False(Explorer.IsFlatMode);
        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    // Typing in the box is what a real search starts from, after the keystrokes settle
    [AvaloniaFact]
    public async Task TypingInTheSearchBoxReachesTheExplorer()
    {
        await GivenTwoVolumesAsync();
        using var probe = new MessageProbe<SearchAllMessage>();

        Sidebar.SearchAllTerm = "AdventOfCode";

        var message = await probe.FirstAsync();
        Assert.Equal("AdventOfCode", message.Term);

        // Both folders are offered to the search, each under the volume holding it
        Assert.Equal(
            [("Code", "test"), ("Docs", "test2")],
            message.Scopes.Select(scope => (scope.Name, scope.VolumeName)).OrderBy(pair => pair.Name));
    }

    [AvaloniaFact]
    public async Task PickingAFolderCallsOffASearchCoveringAllOfThem()
    {
        await GivenTwoVolumesAsync();
        Sidebar.SearchAllTerm = "AdventOfCode";

        var node = Sidebar.VirtualVolumes.Single(candidate => candidate.Name == "test2");
        node.SelectedFolder = node.Folders.Single();

        Assert.Equal(string.Empty, Sidebar.SearchAllTerm);
    }

    [AvaloniaFact]
    public async Task PickingAFolderTellsTheExplorerWhichVolumeHoldsIt()
    {
        await GivenTwoVolumesAsync();
        using var probe = new MessageProbe<FolderSelectedMessage>();

        var node = Sidebar.VirtualVolumes.Single(candidate => candidate.Name == "test2");
        node.SelectedFolder = node.Folders.Single();

        var message = await probe.FirstAsync();
        Assert.Equal("test2", message.VolumeName);
        Assert.Equal("Docs", message.Name);

        // And the explorer makes that the volume heading its paths
        await Explorer.ShowFolderAsync(message);
        Assert.Equal("test2:", Explorer.VolumePrefix);
        Assert.Equal(["Docs"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    #endregion

    #region The explorer view

    [AvaloniaFact]
    public async Task TheSearchBoxTakesTheCaretWhenFindAsks()
    {
        var view = new VolumeExplorerView { DataContext = Explorer };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Pump();

        view.Receive(new FocusFileSearchMessage());
        Pump();

        Assert.True(view.GetControl<TextBox>("FileSearchBox").IsFocused);
        window.Close();

        await Task.CompletedTask;
    }

    [AvaloniaFact]
    public async Task TheLocationColumnIsShownOnlyWhileTheRowsAreScattered()
    {
        await GivenTwoVolumesAsync();

        var view = new VolumeExplorerView { DataContext = Explorer };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Pump();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();
        var location = grid.Columns.Single(column => Equals(column.Header, "Location"));

        var folder = Sidebar.VirtualVolumes.First().Folders.Single();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(folder.Entry.TreeId, "Code", "test"));
        Pump();
        Assert.False(location.IsVisible);

        await Explorer.SearchAllAsync(SearchFor("AdventOfCode"));
        Pump();
        Assert.True(location.IsVisible);

        window.Close();
    }

    [AvaloniaFact]
    public async Task OpeningAFolderInTheGridNavigatesIntoIt()
    {
        await GivenTwoVolumesAsync();
        var folder = Sidebar.VirtualVolumes.First().Folders.Single();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(folder.Entry.TreeId, "Code", "test"));

        var advent = Explorer.Files!.Single(file => file.Name == "AdventOfCode");
        await Explorer.OpenItemCommand.ExecuteAsync(advent);

        Assert.Equal(["Code", "AdventOfCode"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task ACrumbGoesBackToTheFolderItNames()
    {
        await GivenTwoVolumesAsync();
        var folder = Sidebar.VirtualVolumes.First().Folders.Single();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(folder.Entry.TreeId, "Code", "test"));
        await Explorer.OpenItemCommand.ExecuteAsync(Explorer.Files!.Single(f => f.Name == "AdventOfCode"));

        await Explorer.NavigateToCommand.ExecuteAsync(Explorer.Breadcrumbs[0].Id);

        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task ThereIsNowhereToGoUpFromTheTopOfATree()
    {
        await GivenTwoVolumesAsync();
        var folder = Sidebar.VirtualVolumes.First().Folders.Single();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(folder.Entry.TreeId, "Code", "test"));

        Assert.False(Explorer.NavigateUpCommand.CanExecute(null));
    }

    #endregion
}
