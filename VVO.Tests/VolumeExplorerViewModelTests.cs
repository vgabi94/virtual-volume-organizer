using VVO.Core.Models;
using VVO.Core.Services;
using VVO.UI;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.Tests;

public class VolumeExplorerViewModelTests : IDisposable
{
    private readonly string _dbPath;
    private readonly DatabaseService _database;
    private readonly VirtualVolumeService _volumes;
    private readonly VolumeExplorerViewModel _explorer;

    public VolumeExplorerViewModelTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.vvo");
        _database = new DatabaseService();
        _database.EnsureDatabaseReadyAsync(_dbPath).GetAwaiter().GetResult();
        _volumes = new VirtualVolumeService(_database);
        _explorer = new VolumeExplorerViewModel(new UndoService(), _database, _volumes);
    }

    public void Dispose()
    {
        TextClipboard.Reset();
        Dialogs.Reset();

        if (File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); } catch { }
        }
    }

    /// <summary>
    /// A folder holding 'AdventOfCode\notes.txt' and 'readme.md', stored in a virtual volume.
    /// </summary>
    private async Task<RootFolderMetadata> AddCodeAsync(Guid volumeId, string name = "Code")
    {
        var tree = TestTree.Root(name)
            .File("readme.md", 10)
            .Folder("AdventOfCode", folder => folder.File("notes.txt", 20))
            .Build();

        return await _volumes.AddFolderAsync(volumeId, tree.Metadata, tree.Records.ToList());
    }

    private Task ShowAsync(RootFolderMetadata entry, string name, string volumeName)
    {
        return _explorer.ShowFolderAsync(
            new FolderSelectedMessage(entry.TreeId, name, volumeName, entry.Path));
    }

    /// <summary>
    /// Takes whatever a copy command puts on the clipboard, which a headless test has no
    /// window to reach. Undone by <see cref="Dispose"/>.
    /// </summary>
    private static List<string> Copied()
    {
        var copied = new List<string>();
        TextClipboard.Writing = text =>
        {
            copied.Add(text);
            return Task.CompletedTask;
        };

        return copied;
    }

    private Task SearchAllAsync(string term, params SearchScope[] scopes)
    {
        return _explorer.SearchAllAsync(new SearchAllMessage(term, scopes));
    }

    #region Browsing one folder

    [Fact]
    public async Task SelectingAFolderListsWhatIsDirectlyInsideIt()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        Assert.NotNull(_explorer.Files);
        Assert.Equal(["AdventOfCode", "readme"], _explorer.Files!.Select(file => file.Name));
    }

    [Fact]
    public async Task TheVolumeHeadsThePathAndTheFolderIsTheFirstCrumb()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        Assert.Equal("test:", _explorer.VolumePrefix);
        Assert.Equal(["Code"], _explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [Fact]
    public async Task OpeningAFolderAddsItToThePath()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        var advent = _explorer.Files!.Single(file => file.Name == "AdventOfCode");
        await _explorer.OpenItemCommand.ExecuteAsync(advent);

        Assert.Equal(["Code", "AdventOfCode"], _explorer.Breadcrumbs.Select(crumb => crumb.Name));
        Assert.Equal(["notes"], _explorer.Files!.Select(file => file.Name));
    }

    [Fact]
    public async Task GoingUpLeavesTheFolderAgain()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");
        await _explorer.OpenItemCommand.ExecuteAsync(_explorer.Files!.Single(f => f.Name == "AdventOfCode"));

        await _explorer.NavigateUpCommand.ExecuteAsync(null);

        Assert.Equal(["Code"], _explorer.Breadcrumbs.Select(crumb => crumb.Name));
        Assert.False(_explorer.NavigateUpCommand.CanExecute(null));
    }

    [Fact]
    public async Task BrowsingCountsTheItemsAndTheirSize()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        Assert.Equal("2 items · 30 B", _explorer.ItemSummary);
    }

    // A location only says something when the rows are scattered, which they are not here
    [Fact]
    public async Task BrowsingLeavesTheLocationEmpty()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        Assert.False(_explorer.IsFlatMode);
        Assert.All(_explorer.Files!, file => Assert.Equal(string.Empty, file.Location));
    }

    [Fact]
    public async Task TheFolderIsListedUnderTheLabelTheSidebarShows()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        // The sidebar shows a label the user chose, not the name the folder was scanned under
        await ShowAsync(entry, "My Code", "test");

        Assert.Equal(["My Code"], _explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    #endregion

    #region Searching every folder

    [Fact]
    public async Task SearchAllFindsMatchesInEveryFolderItCovers()
    {
        var one = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await _volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        var code = await AddCodeAsync(one.Id);
        var docs = await AddCodeAsync(two.Id, "Docs");

        await SearchAllAsync("notes",
            new SearchScope(code.TreeId, "Code", "test"),
            new SearchScope(docs.TreeId, "Docs", "test2"));

        Assert.True(_explorer.IsFlatMode);
        Assert.Equal(2, _explorer.Files!.Count);
        Assert.Equal("2 matches", _explorer.ItemSummary);
    }

    [Fact]
    public async Task AHitIsLocatedUnderTheVolumeHoldingIt()
    {
        var one = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await _volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        var code = await AddCodeAsync(one.Id);
        var docs = await AddCodeAsync(two.Id, "Docs");

        await SearchAllAsync("notes",
            new SearchScope(code.TreeId, "Code", "test"),
            new SearchScope(docs.TreeId, "Docs", "test2"));

        Assert.Equal(
            [@"test2:\Docs\AdventOfCode", @"test:\Code\AdventOfCode"],
            _explorer.Files!.Select(file => file.Location).OrderBy(location => location, StringComparer.Ordinal));
    }

    [Fact]
    public async Task AHitAtTheTopOfATreeIsLocatedAtTheFolderItself()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var code = await AddCodeAsync(volume.Id);

        await SearchAllAsync("readme", new SearchScope(code.TreeId, "Code", "test"));

        Assert.Equal(@"test:\Code", Assert.Single(_explorer.Files!).Location);
    }

    // The folder at the top of a tree is what the search is anchored on, not a result in it
    [Fact]
    public async Task SearchAllLeavesOutTheFoldersAtTheTopOfATree()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var code = await AddCodeAsync(volume.Id);

        await SearchAllAsync("Code", new SearchScope(code.TreeId, "Code", "test"));

        Assert.Equal(["AdventOfCode"], _explorer.Files!.Select(file => file.Name));
    }

    [Fact]
    public async Task SearchAllOnlyCoversTheFoldersItWasGiven()
    {
        var one = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await _volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        var code = await AddCodeAsync(one.Id);
        await AddCodeAsync(two.Id, "Docs");

        await SearchAllAsync("notes", new SearchScope(code.TreeId, "Code", "test"));

        Assert.Equal(@"test:\Code\AdventOfCode", Assert.Single(_explorer.Files!).Location);
    }

    [Fact]
    public async Task AnEmptyTermGoesBackToTheFolderTheUserCameFrom()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var code = await AddCodeAsync(volume.Id);
        await ShowAsync(code, "Code", "test");

        var scope = new SearchScope(code.TreeId, "Code", "test");
        await SearchAllAsync("notes", scope);
        await SearchAllAsync(string.Empty, scope);

        Assert.False(_explorer.IsFlatMode);
        Assert.Equal(["Code"], _explorer.Breadcrumbs.Select(crumb => crumb.Name));
        Assert.Equal(["AdventOfCode", "readme"], _explorer.Files!.Select(file => file.Name));
    }

    [Fact]
    public async Task SearchAllFindsNothingWhenNoNameMatches()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var code = await AddCodeAsync(volume.Id);

        await SearchAllAsync("nowhere", new SearchScope(code.TreeId, "Code", "test"));

        Assert.Empty(_explorer.Files!);
        Assert.Equal("0 matches", _explorer.ItemSummary);
    }

    // The hit can be in a tree other than the one the sidebar has selected
    [Fact]
    public async Task OpeningAHitMovesTheExplorerIntoTheTreeItCameFrom()
    {
        var one = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await _volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        var code = await AddCodeAsync(one.Id);
        var docs = await AddCodeAsync(two.Id, "Docs");

        await ShowAsync(code, "Code", "test");
        await SearchAllAsync("AdventOfCode",
            new SearchScope(code.TreeId, "Code", "test"),
            new SearchScope(docs.TreeId, "Docs", "test2"));

        var inDocs = _explorer.Files!.Single(file => file.Location.StartsWith("test2:"));
        await _explorer.OpenItemCommand.ExecuteAsync(inDocs);

        Assert.Equal("test2:", _explorer.VolumePrefix);
        Assert.Equal(["Docs", "AdventOfCode"], _explorer.Breadcrumbs.Select(crumb => crumb.Name));
        Assert.Equal(["notes"], _explorer.Files!.Select(file => file.Name));
    }

    #endregion

    #region The paths an entry carries

    [Fact]
    public async Task AListedEntryCarriesItsCataloguePathAndThePathItCameFromOnDisk()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        var readme = _explorer.Files!.Single(file => file.Name == "readme");
        Assert.Equal(@"test:\Code\readme.md", readme.VirtualPath);
        Assert.Equal(@"C:\Code\readme.md", readme.PhysicalPath);
    }

    // The column drops the extension, which the path has to keep
    [Fact]
    public async Task APathKeepsTheExtensionTheNameColumnLeavesOut()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        var readme = _explorer.Files!.Single(file => file.Name == "readme");
        Assert.EndsWith("readme.md", readme.VirtualPath);
        Assert.EndsWith("readme.md", readme.PhysicalPath);
    }

    [Fact]
    public async Task PathsGrowAsTheUserNavigatesIntoAFolder()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");
        await _explorer.OpenItemCommand.ExecuteAsync(
            _explorer.Files!.Single(file => file.Name == "AdventOfCode"));

        var notes = _explorer.Files!.Single(file => file.Name == "notes");
        Assert.Equal(@"test:\Code\AdventOfCode\notes.txt", notes.VirtualPath);
        Assert.Equal(@"C:\Code\AdventOfCode\notes.txt", notes.PhysicalPath);
    }

    // A hit is placed by the folder it actually sits in, not by wherever the user was standing
    [Fact]
    public async Task ASearchHitCarriesThePathsOfWhereItLives()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");
        await SearchAllAsync("notes", new SearchScope(entry.TreeId, "Code", "test", entry.Path));

        var notes = _explorer.Files!.Single();
        Assert.Equal(@"test:\Code\AdventOfCode\notes.txt", notes.VirtualPath);
        Assert.Equal(@"C:\Code\AdventOfCode\notes.txt", notes.PhysicalPath);
    }

    [Fact]
    public async Task AnEntryWithNoPathOnDiskIsDescribedByItsCataloguePathAlone()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await _explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        var readme = _explorer.Files!.Single(file => file.Name == "readme");
        Assert.Empty(readme.PhysicalPath);
        Assert.Equal(@"test:\Code\readme.md", readme.PathTip);
    }

    [Fact]
    public async Task ATipShowsBothPathsWhenThereAreBoth()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await ShowAsync(entry, "Code", "test");

        var readme = _explorer.Files!.Single(file => file.Name == "readme");
        Assert.Equal(
            $@"test:\Code\readme.md{Environment.NewLine}C:\Code\readme.md",
            readme.PathTip);
    }

    #endregion

    #region Copying paths

    [Fact]
    public async Task CopyPutsTheCataloguePathOfTheSelectedEntryOnTheClipboard()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        var copied = Copied();
        _explorer.SelectedFile = _explorer.Files!.Single(file => file.Name == "readme");
        await _explorer.CopyCommand.ExecuteAsync(null);

        Assert.Equal([@"test:\Code\readme.md"], copied);
    }

    [Fact]
    public async Task CopyPhysicalPutsThePathOnDiskOnTheClipboard()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        var copied = Copied();
        _explorer.SelectedFile = _explorer.Files!.Single(file => file.Name == "readme");
        await _explorer.CopyPhysicalCommand.ExecuteAsync(null);

        Assert.Equal([@"C:\Code\readme.md"], copied);
    }

    // Folders are listed ahead of files, and the copy follows the order on screen
    [Fact]
    public async Task CopyAllTakesEveryEntryOnScreenOnePerLine()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        var copied = Copied();
        await _explorer.CopyAllCommand.ExecuteAsync(null);

        Assert.Equal(
            [$@"test:\Code\AdventOfCode{Environment.NewLine}test:\Code\readme.md"],
            copied);
    }

    [Fact]
    public async Task CopyPhysicalAllTakesEveryPathOnDisk()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        var copied = Copied();
        await _explorer.CopyPhysicalAllCommand.ExecuteAsync(null);

        Assert.Equal(
            [$@"C:\Code\AdventOfCode{Environment.NewLine}C:\Code\readme.md"],
            copied);
    }

    [Fact]
    public async Task CopyingAcrossASearchTakesTheHitsFromEveryTree()
    {
        var one = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await _volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        var code = await AddCodeAsync(one.Id);
        var docs = await AddCodeAsync(two.Id, "Docs");

        await ShowAsync(code, "Code", "test");
        await SearchAllAsync("readme",
            new SearchScope(code.TreeId, "Code", "test", code.Path),
            new SearchScope(docs.TreeId, "Docs", "test2", docs.Path));

        var copied = Copied();
        await _explorer.CopyAllCommand.ExecuteAsync(null);

        var lines = copied.Single().Split(Environment.NewLine);
        Assert.Contains(@"test:\Code\readme.md", lines);
        Assert.Contains(@"test2:\Docs\readme.md", lines);
    }

    [Fact]
    public async Task CopyPutsEverySelectedPathOnTheClipboard()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        var copied = Copied();
        _explorer.ReplaceSelection(_explorer.Files!.ToList());
        await _explorer.CopyCommand.ExecuteAsync(null);

        var lines = copied.Single().Split(Environment.NewLine);
        Assert.Contains(@"test:\Code\AdventOfCode", lines);
        Assert.Contains(@"test:\Code\readme.md", lines);
    }

    [Fact]
    public async Task NothingIsCopiedUntilAnEntryIsSelected()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        Assert.False(_explorer.CopyCommand.CanExecute(null));
        Assert.False(_explorer.CopyPhysicalCommand.CanExecute(null));

        _explorer.SelectedFile = _explorer.Files!.First();

        Assert.True(_explorer.CopyCommand.CanExecute(null));
        Assert.True(_explorer.CopyPhysicalCommand.CanExecute(null));
    }

    // Nothing on disk to point at, so the two physical commands have nothing to offer
    [Fact]
    public async Task TheDiskPathCannotBeCopiedForATreeListedWithoutOne()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);

        await _explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));
        _explorer.SelectedFile = _explorer.Files!.First();

        Assert.True(_explorer.CopyCommand.CanExecute(null));
        Assert.True(_explorer.CopyAllCommand.CanExecute(null));
        Assert.False(_explorer.CopyPhysicalCommand.CanExecute(null));
        Assert.False(_explorer.CopyPhysicalAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task MovingToAnotherFolderDropsTheSelectionTheCopyCommandsActOn()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        _explorer.SelectedFile = _explorer.Files!.Single(file => file.Name == "AdventOfCode");
        await _explorer.OpenItemCommand.ExecuteAsync(_explorer.SelectedFile);

        Assert.Null(_explorer.SelectedFile);
        Assert.False(_explorer.CopyCommand.CanExecute(null));
        Assert.False(_explorer.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task NothingIsDeletedUntilAnEntryIsSelected()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");

        Assert.False(_explorer.DeleteCommand.CanExecute(null));

        _explorer.SelectedFile = _explorer.Files!.First();

        Assert.True(_explorer.DeleteCommand.CanExecute(null));
    }

    [Fact]
    public async Task ADeleteWithNoWindowLeavesTheFileAlone()
    {
        var volume = await _volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddCodeAsync(volume.Id);
        await ShowAsync(entry, "Code", "test");
        _explorer.SelectedFile = _explorer.Files!.Single(file => file.Name == "readme");

        Dialogs.Owner = () => null;
        await _explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Contains(_explorer.Files!, file => file.Name == "readme");
        Assert.Equal(4, (await _database.FindItemsAsync<FileRecord>(
            record => record.RootFolderId == entry.TreeId)).Count);
    }

    #endregion
}
