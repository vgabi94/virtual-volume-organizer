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

    public ExplorerCompareTests()
    {
        Sidebar = NewSidebar();
        Explorer = NewExplorer();
    }

    // v1 and v2 differ in each way a file can: changed, removed and added
    private async Task GivenReleasesShownAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddDeepFolderAsync(volume.Id, "Code");

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
}
