using Avalonia.Headless.XUnit;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// Reading a catalogued folder again from wherever it is now, which is put to the user as the
/// differences it would apply before anything is written.
/// </summary>
public class SidebarUpdateTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }

    public SidebarUpdateTests()
    {
        Sidebar = NewSidebar();
    }

    private async Task<FolderItem> GivenACataloguedFolderAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await Sidebar.LoadAsync();

        return Listed();
    }

    /// <summary>A folder on disk holding something other than what the catalogue has.</summary>
    private string GivenAFolderOnDisk(params string[] files)
    {
        var path = TempDirectory();
        foreach (var name in files)
        {
            File.WriteAllText(Path.Combine(path, name), name);
        }

        PickFolder(path);
        return path;
    }

    private FolderItem Listed() => Sidebar.VirtualVolumes.SelectMany(node => node.Folders).First();

    private IReadOnlyCollection<FileRecord> Stored(Guid treeId) =>
        Database.FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId).GetAwaiter().GetResult();

    /// <summary>Approves the proposal the update puts up, taking it as it was filled in.</summary>
    private void ApproveProposal(Action<CompareResultsViewModel>? inspect = null)
    {
        AnswerDialogs<CompareResultsViewModel>(proposal => inspect?.Invoke(proposal));
    }

    #region What the update writes

    [AvaloniaFact]
    public async Task UpdatingAFolderPutsWhatWasScannedInPlaceOfWhatWasCatalogued()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt", "todo.md");
        ApproveProposal();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        var stored = Stored(folder.Entry.TreeId);
        Assert.Contains(stored, record => record.Name == "notes.txt");
        Assert.Contains(stored, record => record.Name == "todo.md");
        Assert.DoesNotContain(stored, record => record.Name == "readme.md");
    }

    [AvaloniaFact]
    public async Task UpdatingAFolderRecordsWhereItWasReadFrom()
    {
        var folder = await GivenACataloguedFolderAsync();
        var live = GivenAFolderOnDisk("notes.txt");
        ApproveProposal();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        var entry = Assert.Single(await Volumes.GetFoldersAsync(folder.Entry.VirtualVolumeId));
        Assert.Equal(live, entry.Path);
        Assert.True(entry.LastScanned > folder.Entry.LastScanned);
    }

    // The row is what the user is looking at, and nothing else would say the folder had changed
    [AvaloniaFact]
    public async Task TheSidebarRowFollowsWhatTheUpdatePutInTheFolder()
    {
        var folder = await GivenACataloguedFolderAsync();
        var live = GivenAFolderOnDisk("notes.txt");
        ApproveProposal();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        var relisted = Listed();
        Assert.Equal(Path.GetFileName(live), relisted.Title);
        Assert.Equal(live, relisted.Path);
        Assert.NotEqual(folder.Subtitle, relisted.Subtitle);
    }

    // A duplicate stands on the same scanned tree, so it holds what the update put there too
    [AvaloniaFact]
    public async Task AFolderSharingTheTreeIsRelistedWithTheRest()
    {
        var folder = await GivenACataloguedFolderAsync();
        await Volumes.CopyFolderAsync(folder.Entry.Id, folder.Entry.VirtualVolumeId, "A copy");
        await Sidebar.LoadAsync();

        GivenAFolderOnDisk("notes.txt");
        ApproveProposal();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(Listed());

        var rows = Sidebar.VirtualVolumes.SelectMany(node => node.Folders).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.NotEqual(folder.Subtitle, row.Subtitle));

        // and the copy keeps the label it was given, which the update has no business changing
        Assert.Contains(rows, row => row.Title == "A copy");
    }

    [AvaloniaFact]
    public async Task AFolderIsUpdatedEvenWhenNothingAboutItHasChanged()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var live = GivenAFolderOnDisk("notes.txt");

        // Catalogued from the folder it is then read again from, so the two sides match
        var scan = await new VVO.Core.Services.FileScannerService().ScanDirectoryAsync(live);
        await Volumes.AddFolderAsync(volume.Id, scan.Metadata, scan.Records);
        await Sidebar.LoadAsync();

        CompareResultsViewModel? proposal = null;
        ApproveProposal(shown => proposal = shown);

        await Sidebar.UpdateFolderCommand.ExecuteAsync(Listed());

        Assert.NotNull(proposal);
        Assert.False(proposal!.HasDifferences);
        Assert.Single(Stored(Listed().Entry.TreeId), record => record.Name == "notes.txt");
    }

    #endregion

    #region What is put to the user first

    [AvaloniaFact]
    public async Task TheProposalShowsTheDifferencesTheUpdateWouldApply()
    {
        var folder = await GivenACataloguedFolderAsync();
        var live = GivenAFolderOnDisk("notes.txt");

        CompareResultsViewModel? proposal = null;
        ApproveProposal(shown => proposal = shown);

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.NotNull(proposal);
        Assert.True(proposal!.IsUpdate);
        Assert.True(proposal.HasDifferences);
        Assert.Equal("Code", proposal.SourceName);
        Assert.Equal(live, proposal.TargetName);

        // What is in the catalogue and not on disk goes, and what is on disk and not in the
        // catalogue arrives
        Assert.True(proposal.HasRemoved);
        Assert.True(proposal.HasAdded);
    }

    [AvaloniaFact]
    public async Task TurningDownTheProposalLeavesTheFolderAsItWas()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt");
        AnswerDialogs(_ => false);

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Contains(Stored(folder.Entry.TreeId), record => record.Name == "readme.md");
        Assert.Equal(folder, Listed());
    }

    #endregion

    #region Stopping part way

    [AvaloniaFact]
    public async Task AnUpdateOffersToBeCalledOffWhileItRunsAndClearsTheStatusWhenDone()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt");
        var mainWindow = NewMainWindow(Sidebar);
        using var reported = new MessageProbe<UpdateStatusMessage>();
        ApproveProposal();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Contains(reported.All, message => message.IsVisible && message.IsCancellable);
        Assert.Contains(reported.All, message => message.Message.StartsWith("Updating folder"));

        // The relisted row sends the explorer off to read the folder again, which puts a status
        // of its own up behind this one
        await Until(() => !mainWindow.IsStatusMessageVisible, "the status to clear");
        Assert.False(mainWindow.IsCancelVisible);
    }

    // Cancelling on the first report puts the request in before the scan starts, which is what
    // pressing the button the moment it appears amounts to
    [AvaloniaFact]
    public async Task AnUpdateCalledOffWhileItReadsTheFolderProposesNothing()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt");
        using var reported = new MessageProbe<UpdateStatusMessage>(
            _ => Sidebar.Receive(new CancelRequestedMessage()));

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Empty(Opened);
        Assert.Contains(Stored(folder.Entry.TreeId), record => record.Name == "readme.md");
        Assert.Empty(Told);
    }

    // The write is one transaction, so the folder comes away holding exactly what it had
    [AvaloniaFact]
    public async Task AnUpdateCalledOffWhileItWritesLeavesTheFolderAsItWas()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt");
        ApproveProposal();

        using var reported = new MessageProbe<UpdateStatusMessage>(message =>
        {
            if (message.Message.StartsWith("Updating folder"))
            {
                Sidebar.Receive(new CancelRequestedMessage());
            }
        });

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Contains(Stored(folder.Entry.TreeId), record => record.Name == "readme.md");
        Assert.Empty(Told);
    }

    #endregion

    #region What is not updated

    [AvaloniaFact]
    public async Task ThereIsNothingToUpdateWithoutAFolderToUpdate()
    {
        await GivenACataloguedFolderAsync();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(null);

        Assert.Empty(Opened);
    }

    [AvaloniaFact]
    public async Task NothingIsUpdatedWithoutAWindowToProposeItOver()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt");
        VVO.UI.Dialogs.Owner = () => null;

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Empty(Opened);
    }

    [AvaloniaFact]
    public async Task NothingIsUpdatedWhenNoFolderIsPickedToUpdateFrom()
    {
        var folder = await GivenACataloguedFolderAsync();
        PickFolder(null);

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Empty(Opened);
        Assert.Contains(Stored(folder.Entry.TreeId), record => record.Name == "readme.md");
    }

    // Which is what picking a folder on a drive that has been unplugged since amounts to
    [AvaloniaFact]
    public async Task AFolderThatCannotBeReadIsReportedRatherThanUpdatedFrom()
    {
        var folder = await GivenACataloguedFolderAsync();
        PickFolder(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}"));

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Equal("Error", Assert.Single(Told).Title);
        Assert.Contains(Stored(folder.Entry.TreeId), record => record.Name == "readme.md");
    }

    [AvaloniaFact]
    public async Task AnUpdateThatCannotBeProposedIsReported()
    {
        var folder = await GivenACataloguedFolderAsync();
        GivenAFolderOnDisk("notes.txt");
        FailDialogs();

        await Sidebar.UpdateFolderCommand.ExecuteAsync(folder);

        Assert.Equal("Error", Assert.Single(Told).Title);
        Assert.Contains(Stored(folder.Entry.TreeId), record => record.Name == "readme.md");
    }

    #endregion
}
