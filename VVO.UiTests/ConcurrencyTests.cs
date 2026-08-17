using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// Two parts of the window reaching for the database at once. Each one opens the file for
/// itself, and a broadcast is answered without anything waiting on it, so overlapping is
/// ordinary rather than exceptional.
/// </summary>
public class ConcurrencyTests : UiTestBase
{
    private async Task<RootFolderMetadata> GivenACataloguedDatabaseAsync()
    {
        await Database.InsertItemsAsync<DatabaseMetadata>(
            [new DatabaseMetadata { Id = Guid.NewGuid(), Name = "Discs", Path = Database.DbPath }]);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        return await AddFolderAsync(volume.Id, "Code");
    }

    [AvaloniaFact]
    public async Task TheSidebarAndTheExplorerReadingAtOnceBothGetTheirAnswer()
    {
        var entry = await GivenACataloguedDatabaseAsync();

        var sidebar = NewSidebar();
        var explorer = NewExplorer();
        Detach(explorer);

        await Task.WhenAll(
            sidebar.LoadAsync(),
            explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test")),
            Volumes.GetVirtualVolumesAsync());

        Assert.Empty(Told);
        Assert.Equal("test", sidebar.VirtualVolumes.Single().Name);
        Assert.Equal("Code", Assert.Single(explorer.Breadcrumbs).Name);
    }

    // Opening a database broadcasts, and the sidebar answers by loading; the explorer is
    // still showing the folder from the database being left behind.
    [AvaloniaFact]
    public async Task ADatabaseOpenedWhileTheExplorerIsReadingReachesTheSidebar()
    {
        var entry = await GivenACataloguedDatabaseAsync();

        var sidebar = NewSidebar();
        var explorer = NewExplorer();
        Detach(explorer);

        var reading = explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));
        WeakReferenceMessenger.Default.Send(new DatabaseReady());

        await reading;
        await sidebar.LoadAsync();
        Pump();

        Assert.Empty(Told);
        Assert.Equal("Discs", sidebar.DatabaseName);
        Assert.Equal("test", sidebar.VirtualVolumes.Single().Name);
    }

    [AvaloniaFact]
    public async Task TwoLoadsAtOnceLeaveTheSidebarListingEachVolumeOnce()
    {
        await GivenACataloguedDatabaseAsync();
        var sidebar = NewSidebar();

        await Task.WhenAll(sidebar.LoadAsync(), sidebar.LoadAsync());

        Assert.Equal("test", sidebar.VirtualVolumes.Single().Name);
        Assert.Single(sidebar.VirtualVolumes.Single().Folders);
    }

    [AvaloniaFact]
    public async Task SavingACopyWhileTheDatabaseIsBeingWrittenToCopiesTheWholeOfIt()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var sidebar = NewSidebar();

        var target = TempPath("vvo");
        PickFiles((_, _) => target);

        // A scan writing its records is what the copy has to wait for
        var scanning = AddFolderAsync(volume.Id, "Code");
        var copying = sidebar.SaveCopyCommand.ExecuteAsync(Shell);
        await Task.WhenAll(scanning, copying);

        Assert.Empty(Told);

        var copy = new VVO.Core.Services.DatabaseService();
        await copy.EnsureDatabaseReadyAsync(target);
        Assert.Equal("test", (await new VVO.Core.Services.VirtualVolumeService(copy)
            .GetVirtualVolumesAsync()).Single().Name);
        Assert.EndsWith("Code", (await copy.ReadItemsAsync<RootFolderMetadata>()).Single().Path);
    }
}
