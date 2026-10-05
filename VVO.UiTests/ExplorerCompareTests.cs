using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// Comparing a folder the explorer lists, at any depth: with a folder picked inside any listed
/// one, with a folder on disk, or with a second selected folder.
/// </summary>
public class ExplorerCompareTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }
    private VolumeExplorerViewModel Explorer { get; }

    private RootFolderMetadata _releases = null!;
    private RootFolderMetadata _code = null!;

    public ExplorerCompareTests()
    {
        Sidebar = NewSidebar();
        Explorer = NewExplorer();
    }

    // v1 and v2 differ in each way a file can: changed, removed and added
    private async Task GivenReleasesShownAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        _code = await AddDeepFolderAsync(volume.Id, "Code");

        var rootId = Guid.NewGuid();
        var v1 = Guid.NewGuid();
        var v2 = Guid.NewGuid();
        _releases = await Volumes.AddFolderAsync(volume.Id,
            new RootFolderMetadata { Id = Guid.NewGuid(), TreeId = rootId, Path = @"D:\Releases", LastScanned = DateTime.UtcNow },
            [
                new() { Id = rootId, RootFolderId = rootId, IsFolder = true, Name = "Releases", Size = 5 },
                new() { Id = v1, RootFolderId = rootId, ParentId = rootId, IsFolder = true, Name = "v1", Size = 2 },
                new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = v1, Name = "a.txt", Size = 1 },
                new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = v1, Name = "b.txt", Size = 1 },
                new() { Id = v2, RootFolderId = rootId, ParentId = rootId, IsFolder = true, Name = "v2", Size = 3 },
                new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = v2, Name = "a.txt", Size = 2 },
                new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = v2, Name = "c.txt", Size = 1 },
                new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = rootId, Name = "notes.txt", Size = 0 }
            ]);

        await Sidebar.LoadAsync();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(_releases.TreeId, "Releases", "test", _releases.Path));
    }

    private FileItem Item(string name) => Explorer.Files!.Single(file => file.Name == name);

    private void Select(params string[] names) => Explorer.ReplaceSelection(names.Select(Item).ToList());

    private CompareResultsViewModel? Results =>
        Shell.OwnedWindows.OfType<CompareResultsView>().SingleOrDefault()?.DataContext as CompareResultsViewModel;

    // Opening a folder in the dialog reads it, which a synchronous answer could not wait for
    private void AnswerTheTargetDialog(Func<CompareTargetDialogViewModel, Task> fillIn) =>
        VVO.UI.Dialogs.Answer = async dialog =>
        {
            Opened.Add(dialog);
            await fillIn((CompareTargetDialogViewModel)dialog.DataContext!);
            return true;
        };

    public override void Dispose()
    {
        foreach (var window in Shell.OwnedWindows.ToList())
        {
            window.Close();
        }

        base.Dispose();
    }

    #region What can be compared

    [AvaloniaFact]
    public async Task OneOrTwoFoldersCanBeComparedAndNothingElse()
    {
        await GivenReleasesShownAsync();

        Select("v1");
        Assert.True(Explorer.CompareCommand.CanExecute(null));
        Select("v1", "v2");
        Assert.True(Explorer.CompareCommand.CanExecute(null));

        Select("notes");
        Assert.False(Explorer.CompareCommand.CanExecute(null));
        Select("v1", "notes");
        Assert.False(Explorer.CompareCommand.CanExecute(null));
        Select("v1", "v2", "notes");
        Assert.False(Explorer.CompareCommand.CanExecute(null));
        Select();
        Assert.False(Explorer.CompareCommand.CanExecute(null));
    }

    #endregion

    #region Two selected folders

    [AvaloniaFact]
    public async Task TwoSelectedFoldersAreComparedWithEachOtherWithoutAsking()
    {
        await GivenReleasesShownAsync();
        Select("v1", "v2");

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Empty(Opened);
        var results = Results!;
        Assert.Equal(@"test:\Releases\v1", results.SourceName);
        Assert.Equal(@"test:\Releases\v2", results.TargetName);

        // Below the folders compared, not from the top of the tree
        Assert.Equal(["a.txt", "b.txt", "c.txt"], results.Rows.Select(row => row.Path).Order());
        Assert.Equal(@"D:\Releases\v1\b.txt", results.PathsFor(ComparisonStatus.Removed));
        Assert.Equal(@"D:\Releases\v2\c.txt", results.PathsFor(ComparisonStatus.Added));
    }

    [AvaloniaFact]
    public async Task AFolderCaughtInALoopOfParentsIsReportedRatherThanCompared()
    {
        await GivenReleasesShownAsync();
        Select("v1", "v2");

        var records = await Database.FindItemsAsync<FileRecord>(record => record.RootFolderId == _releases.TreeId);
        var v1 = records.Single(record => record.Name == "v1");
        var v2 = records.Single(record => record.Name == "v2");
        await Database.UpdateItemsAsync([v1 with { ParentId = v2.Id }, v2 with { ParentId = v1.Id }]);

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Null(Results);
        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    #endregion

    #region One folder, and what the user picks

    [AvaloniaFact]
    public async Task EveryListedFolderIsOfferedItsOwnIncluded()
    {
        await GivenReleasesShownAsync();
        Select("v1");

        IReadOnlyList<FolderChoice> offered = [];
        AnswerDialogs<CompareTargetDialogViewModel>(dialog => offered = dialog.Folders);

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Equal(["Code", "Releases"], offered.Select(choice => choice.Name).Order());
    }

    [AvaloniaFact]
    public async Task AFolderIsComparedWithOnePickedInsideAListedFolder()
    {
        await GivenReleasesShownAsync();
        Select("v1");

        AnswerTheTargetDialog(async dialog =>
        {
            var releases = dialog.Folders.Single(choice => choice.Name == "Releases");
            await releases.OpenAsync();
            dialog.SelectedFolder = releases.Children.Single(choice => choice.Name == "v2");
        });

        await Explorer.CompareCommand.ExecuteAsync(null);

        var results = Results!;
        Assert.Equal(@"Releases\v2", results.TargetName);
        Assert.Equal(["a.txt", "b.txt", "c.txt"], results.Rows.Select(row => row.Path).Order());
    }

    [AvaloniaFact]
    public async Task AFolderIsComparedWithAFolderOnDisk()
    {
        await GivenReleasesShownAsync();
        Select("v1");

        var live = TempDirectory();
        File.WriteAllText(Path.Combine(live, "new.txt"), "x");
        AnswerDialogs<CompareTargetDialogViewModel>(dialog => dialog.LiveFolderPath = live);

        await Explorer.CompareCommand.ExecuteAsync(null);

        var results = Results!;
        Assert.Equal(live, results.TargetName);
        Assert.Contains(results.Rows, row => row.Path == "new.txt" && row.Status == ComparisonStatus.Added);
        Assert.Contains(results.Rows, row => row.Path == "b.txt" && row.Status == ComparisonStatus.Removed);
    }

    [AvaloniaFact]
    public async Task CancellingTheDialogComparesNothing()
    {
        await GivenReleasesShownAsync();
        Select("v1");

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Single(Opened);
        Assert.Null(Results);
    }

    #endregion

    #region The folders on offer

    [AvaloniaFact]
    public async Task AListedFolderReadsItsSubfoldersWhenOpenedAndListsTheirsAhead()
    {
        await GivenReleasesShownAsync();
        Select("v1");

        FolderChoice code = null!;
        AnswerTheTargetDialog(async dialog =>
        {
            code = dialog.Folders.Single(choice => choice.Name == "Code");

            // Not read yet, but it has to show that it can be opened
            Assert.True(Assert.Single(code.Children).IsPlaceholder);
            dialog.SelectedFolder = code.Children[0];
            Assert.Null(dialog.SelectedFolder);

            await code.OpenAsync();
        });

        await Explorer.CompareCommand.ExecuteAsync(null);

        var advent = Assert.Single(code.Children);
        Assert.Equal(@"Code\AdventOfCode", advent.Label);
        Assert.Equal(@"D:\Code\AdventOfCode", advent.Path);
        Assert.Equal("2023", Assert.Single(advent.Children).Name);
    }

    [AvaloniaFact]
    public async Task OpeningAFolderInTheDialogListsItsSubfolders()
    {
        var treeId = Guid.NewGuid();
        IReadOnlyCollection<FileRecord> folders =
        [
            new() { Id = treeId, RootFolderId = treeId, IsFolder = true, Name = "Code" },
            new() { Id = Guid.NewGuid(), RootFolderId = treeId, ParentId = treeId, IsFolder = true, Name = "lib" }
        ];
        var code = new FolderChoice(treeId, "Code", @"D:\Code", _ => Task.FromResult(folders));
        var window = new CompareTargetDialogView { DataContext = new CompareTargetDialogViewModel([code]) };
        window.Show();
        Pump();

        var item = window.GetVisualDescendants().OfType<TreeViewItem>().Single();
        item.IsExpanded = true;
        await Until(() => code.Children is [{ Name: "lib" }], "the subfolders to be listed");

        Assert.True(code.IsExpanded);
        window.Close();
    }

    #endregion

    #region Hardening: what the selection can be

    [AvaloniaFact]
    public async Task TheCommandIsToldEveryTimeTheSelectionMoves()
    {
        await GivenReleasesShownAsync();
        var told = 0;
        Explorer.CompareCommand.CanExecuteChanged += (_, _) => told++;

        Select("v1");
        Select("v1", "v2");
        Select("notes");

        Assert.True(told >= 3, $"Told {told} times.");
        Assert.False(Explorer.CompareCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task FoldersFoundInTwoTreesAreComparedWithEachOther()
    {
        await GivenReleasesShownAsync();
        await Explorer.SearchAllAsync(new SearchAllMessage("2",
        [
            new SearchScope(_code.TreeId, "Code", "test", _code.Path),
            new SearchScope(_releases.TreeId, "Releases", "test", _releases.Path)
        ]));
        Select("2023", "v2");

        await Explorer.CompareCommand.ExecuteAsync(null);

        var results = Results!;
        Assert.Equal(@"test:\Code\AdventOfCode\2023", results.SourceName);
        Assert.Equal(@"test:\Releases\v2", results.TargetName);
        Assert.Equal(["a.txt", "c.txt", "day1.txt"], results.Rows.Select(row => row.Path).Order());
        Assert.Equal(@"D:\Code\AdventOfCode\2023\day1.txt", results.PathsFor(ComparisonStatus.Removed));
    }

    // Nothing to put in front of the paths on disk, so the rows are named by where they are below
    [AvaloniaFact]
    public async Task FoldersListedWithoutAPathOnDiskAreComparedByTheCatalogueAlone()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var rootId = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await Volumes.AddFolderAsync(volume.Id,
            new RootFolderMetadata { Id = Guid.NewGuid(), TreeId = rootId, Path = string.Empty, LastScanned = DateTime.UtcNow },
            [
                new() { Id = rootId, RootFolderId = rootId, IsFolder = true, Name = "Imported" },
                new() { Id = a, RootFolderId = rootId, ParentId = rootId, IsFolder = true, Name = "a" },
                new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = a, Name = "only-in-a.txt", Size = 1 },
                new() { Id = b, RootFolderId = rootId, ParentId = rootId, IsFolder = true, Name = "b" }
            ]);
        await Sidebar.LoadAsync();
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(rootId, "Imported", "test"));
        Select("a", "b");

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Equal("only-in-a.txt", Results!.PathsFor(ComparisonStatus.Removed));
    }

    #endregion

    #region Hardening: folders that change underneath

    [AvaloniaFact]
    public async Task AFolderPickedThatIsGoneByTheTimeItIsComparedShowsNothing()
    {
        await GivenReleasesShownAsync();
        Select("v1");

        AnswerTheTargetDialog(async dialog =>
        {
            var releases = dialog.Folders.Single(choice => choice.Name == "Releases");
            await releases.OpenAsync();
            var v2 = releases.Children.Single(choice => choice.Name == "v2");
            dialog.SelectedFolder = v2;

            await Volumes.RemoveRecordsAsync([v2.FolderId]);
        });

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Null(Results);
        Assert.Empty(Told);
    }

    [AvaloniaFact]
    public async Task ASelectedFolderDeletedBeforeTheComparisonShowsNothing()
    {
        await GivenReleasesShownAsync();
        Select("v1", "v2");
        await Volumes.RemoveRecordsAsync([Item("v1").Id]);

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Null(Results);
        Assert.Empty(Told);
    }

    #endregion

    #region Hardening: running

    [AvaloniaFact]
    public async Task TheStatusBarOffersToCallItOffAndIsClearedAfter()
    {
        await GivenReleasesShownAsync();
        Select("v1", "v2");
        using var reported = new MessageProbe<UpdateStatusMessage>();

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Contains(reported.All, message => message.IsVisible && message.IsCancellable);
        Assert.False(reported.All[^1].IsVisible);
    }

    [AvaloniaFact]
    public async Task CallingOffAComparisonWithDiskShowsNoResults()
    {
        await GivenReleasesShownAsync();
        Select("v1");
        AnswerDialogs<CompareTargetDialogViewModel>(dialog => dialog.LiveFolderPath = TempDirectory());
        using var reported = new MessageProbe<UpdateStatusMessage>(_ => Sidebar.Receive(new CancelRequestedMessage()));

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Null(Results);
        Assert.Empty(Told);
    }

    [AvaloniaFact]
    public async Task WithNothingToAnswerItTheRequestIsReportedRatherThanLost()
    {
        await GivenReleasesShownAsync();
        Select("v1", "v2");
        Detach(Sidebar);

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Null(Results);
        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task NothingIsComparedWithoutAWindowToShowItOver()
    {
        await GivenReleasesShownAsync();
        Select("v1");
        VVO.UI.Dialogs.Owner = () => null;

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Empty(Opened);
        Assert.Empty(Told);
    }

    #endregion

    #region Hardening: the dialog's folders

    private static FolderChoice Choice(string name, string path, FileRecord[] folders) =>
        new(folders[0].RootFolderId, name, path, _ => Task.FromResult<IReadOnlyCollection<FileRecord>>(folders));

    private static FileRecord[] Folders(string root, params (string Name, string Parent)[] below)
    {
        var rootId = Guid.NewGuid();
        var records = new List<FileRecord> { new() { Id = rootId, RootFolderId = rootId, IsFolder = true, Name = root } };
        foreach (var (name, parent) in below)
        {
            records.Add(new FileRecord
            {
                Id = Guid.NewGuid(), RootFolderId = rootId, IsFolder = true, Name = name,
                ParentId = parent == root ? rootId : records.Single(record => record.Name == parent).Id
            });
        }

        return [.. records];
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBeReadIsReportedAndLeftWithNothingToOpen()
    {
        var broken = new FolderChoice(Guid.NewGuid(), "Broken", @"D:\Broken",
            _ => Task.FromException<IReadOnlyCollection<FileRecord>>(new IOException("the catalogue is gone")));

        await broken.OpenAsync();

        Assert.Empty(broken.Children);
        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task OpeningAFolderTwiceAtOnceReadsItOnce()
    {
        var reads = 0;
        var release = new TaskCompletionSource<IReadOnlyCollection<FileRecord>>();
        var folders = Folders("r", ("a", "r"));
        var choice = new FolderChoice(folders[0].Id, "r", @"D:\r", _ =>
        {
            reads++;
            return release.Task;
        });

        var first = choice.OpenAsync();
        var second = choice.OpenAsync();
        release.SetResult(folders);
        await Task.WhenAll(first, second);

        Assert.Equal(1, reads);
        Assert.Equal("a", Assert.Single(choice.Children).Name);
    }

    [AvaloniaFact]
    public async Task SubfoldersAreOfferedInNameOrderWhateverTheirCase()
    {
        var choice = Choice("r", @"D:\r", Folders("r", ("beta", "r"), ("Alpha", "r"), ("gamma", "r"), ("Delta", "r")));

        await choice.OpenAsync();

        Assert.Equal(["Alpha", "beta", "Delta", "gamma"], choice.Children.Select(child => child.Name));
    }

    [AvaloniaFact]
    public async Task AFolderListedWithoutAPathOffersItsSubfoldersWithoutOneEither()
    {
        var choice = Choice("r", string.Empty, Folders("r", ("a", "r"), ("b", "a")));

        await choice.OpenAsync();
        await choice.Children[0].OpenAsync();

        Assert.Equal(string.Empty, choice.Children[0].Path);
        Assert.Equal(string.Empty, choice.Children[0].Children[0].Path);
        Assert.Equal(@"r\a\b", choice.Children[0].Children[0].Label);
    }

    // The sidebar's title heads what the results call a folder inside it, the label included
    [AvaloniaFact]
    public async Task ALabelledFolderHeadsTheNamesOfTheFoldersInsideIt()
    {
        await GivenReleasesShownAsync();
        await Volumes.UpdateFolderAsync(_releases.Id, "Shipped", null, null, null);
        await Sidebar.LoadAsync();
        Select("v1");

        AnswerTheTargetDialog(async dialog =>
        {
            var shipped = dialog.Folders.Single(choice => choice.Name == "Shipped");
            await shipped.OpenAsync();
            dialog.SelectedFolder = shipped.Children.Single(choice => choice.Name == "v2");
        });

        await Explorer.CompareCommand.ExecuteAsync(null);

        Assert.Equal(@"Shipped\v2", Results!.TargetName);
    }

    [AvaloniaFact]
    public async Task AFolderInsideAndAFolderOnDiskStandInForEachOther()
    {
        var choice = Choice("r", @"D:\r", Folders("r", ("a", "r")));
        await choice.OpenAsync();
        var dialog = new CompareTargetDialogViewModel([choice]) { SelectedFolder = choice.Children[0] };

        dialog.LiveFolderPath = @"D:\Elsewhere";
        Assert.Null(dialog.SelectedFolder);

        dialog.SelectedFolder = choice.Children[0];
        Assert.Empty(dialog.LiveFolderPath);
        Assert.True(dialog.CanCompare);
    }

    // The sidebar's own Compare picks from the same tree of folders
    [AvaloniaFact]
    public async Task TheSidebarComparesAListedFolderWithOneInsideAnother()
    {
        await GivenReleasesShownAsync();
        var code = Sidebar.VirtualVolumes.SelectMany(node => node.Folders).Single(folder => folder.Title == "Code");

        AnswerTheTargetDialog(async dialog =>
        {
            var releases = dialog.Folders.Single(choice => choice.Name == "Releases");
            await releases.OpenAsync();
            dialog.SelectedFolder = releases.Children.Single(choice => choice.Name == "v1");
        });

        await Sidebar.CompareCommand.ExecuteAsync(code);

        Assert.Equal("Code", Results!.SourceName);
        Assert.Equal(@"Releases\v1", Results.TargetName);
    }

    #endregion

    #region Hardening: the menu

    [AvaloniaFact]
    public async Task TheMenusCompareIsOnForTwoFoldersAndOffWithAFileAmongThem()
    {
        await GivenReleasesShownAsync();
        var window = new Window { Content = new VolumeExplorerView { DataContext = Explorer }, Width = 1000, Height = 700 };
        window.Show();
        Pump();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single();

        foreach (var (names, enabled) in new[] { (new[] { "v1", "v2" }, true), (new[] { "v1", "notes" }, false) })
        {
            grid.SelectedItems.Clear();
            foreach (var name in names)
            {
                grid.SelectedItems.Add(Item(name));
            }

            Pump();
            grid.ContextMenu!.Open(grid);
            Pump();

            // The menu takes the grid's data context only once it is open
            var compare = grid.ContextMenu.Items.OfType<MenuItem>().Single(item => item.Command == Explorer.CompareCommand);
            Assert.Equal(enabled, compare.IsEffectivelyEnabled);
            grid.ContextMenu.Close();
        }

        window.Close();
    }

    #endregion
}
