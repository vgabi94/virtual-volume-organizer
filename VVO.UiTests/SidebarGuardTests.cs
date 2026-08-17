using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using VVO.UI;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// What each sidebar command does when it is handed nothing to act on, has no window to open a
/// dialog over, or fails part way through. None of these are reachable by clicking through the
/// window, and all of them are what stands between a mishap and a crash.
/// </summary>
public class SidebarGuardTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }

    public SidebarGuardTests()
    {
        Sidebar = NewSidebar();
    }

    private async Task<FolderItem> GivenAFolderAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await Sidebar.LoadAsync();

        return Sidebar.VirtualVolumes.Single().Folders.Single();
    }

    private static void NoOwnerWindow() => Dialogs.Owner = () => null;

    #region Commands handed nothing

    [AvaloniaFact]
    public async Task EveryFolderCommandHandedNoFolderDoesNothing()
    {
        await GivenAFolderAsync();

        Sidebar.CopyFolderCommand.Execute(null);
        Sidebar.CutFolderCommand.Execute(null);
        await Sidebar.EditFolderCommand.ExecuteAsync(null);
        await Sidebar.DuplicateFolderCommand.ExecuteAsync(null);
        await Sidebar.DeleteFolderCommand.ExecuteAsync(null);
        await Sidebar.PasteBesideFolderCommand.ExecuteAsync(null);

        Assert.Empty(Opened);
        Assert.Single(Sidebar.VirtualVolumes.Single().Folders);
    }

    [AvaloniaFact]
    public async Task EveryVolumeCommandHandedNoVolumeFallsBackOnTheSelectedOne()
    {
        await GivenAFolderAsync();
        Sidebar.SelectedVirtualVolume = null;

        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(null);
        await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(null);
        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(null);

        Assert.Empty(Opened);
        Assert.Single(Sidebar.VirtualVolumes);
    }

    [AvaloniaFact]
    public async Task SelectingNoVolumeLeavesTheSelectionAsItWas()
    {
        await GivenAFolderAsync();
        var selected = Sidebar.SelectedVirtualVolume;

        Sidebar.SelectVirtualVolumeCommand.Execute(null);

        Assert.Same(selected, Sidebar.SelectedVirtualVolume);
    }

    #endregion

    #region Commands with no window to open a dialog over

    [AvaloniaFact]
    public async Task NoVolumeDialogOpensWithoutAWindowToOpenItOver()
    {
        await GivenAFolderAsync();
        NoOwnerWindow();

        await Sidebar.NewVirtualVolumeCommand.ExecuteAsync(null);
        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(Sidebar.VirtualVolumes.Single());
        await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(Sidebar.VirtualVolumes.Single());

        Assert.Empty(Opened);
        Assert.Single(Sidebar.VirtualVolumes);
    }

    [AvaloniaFact]
    public async Task NoFolderDialogOpensWithoutAWindowToOpenItOver()
    {
        var folder = await GivenAFolderAsync();
        NoOwnerWindow();

        await Sidebar.EditFolderCommand.ExecuteAsync(folder);
        await Sidebar.DuplicateFolderCommand.ExecuteAsync(folder);
        await Sidebar.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Empty(Opened);
        Assert.Single(Sidebar.VirtualVolumes.Single().Folders);
    }

    [AvaloniaFact]
    public async Task TheDatabaseCommandsNeedAWindowOfTheirOwnRatherThanAnyVisual()
    {
        await GivenAFolderAsync();

        await Sidebar.NewDatabaseCommand.ExecuteAsync(new Border());

        Assert.Empty(Opened);
    }

    #endregion

    #region Commands that fail part way through

    [AvaloniaFact]
    public async Task AVolumeThatCannotBeCreatedIsReported()
    {
        FailDialogs();

        await Sidebar.NewVirtualVolumeCommand.ExecuteAsync(null);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task AVolumeThatCannotBeEditedIsReported()
    {
        await GivenAFolderAsync();
        FailDialogs();

        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(Sidebar.VirtualVolumes.Single());

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBeEditedIsReported()
    {
        var folder = await GivenAFolderAsync();
        FailDialogs();

        await Sidebar.EditFolderCommand.ExecuteAsync(folder);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBeDuplicatedIsReported()
    {
        var folder = await GivenAFolderAsync();
        FailDialogs();

        await Sidebar.DuplicateFolderCommand.ExecuteAsync(folder);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task AVolumeThatCannotBeDeletedIsReported()
    {
        await GivenAFolderAsync();
        AnswerDialogs(_ => true);

        using (DatabaseTakenAway())
        {
            await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(Sidebar.VirtualVolumes.Single());
        }

        Assert.Equal("Error", Assert.Single(Told).Title);
        Assert.Single(Sidebar.VirtualVolumes);
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBeDeletedIsReported()
    {
        var folder = await GivenAFolderAsync();
        AnswerDialogs(_ => true);

        using (DatabaseTakenAway())
        {
            await Sidebar.DeleteFolderCommand.ExecuteAsync(folder);
        }

        Assert.Equal("Error", Assert.Single(Told).Title);
        Assert.Single(Sidebar.VirtualVolumes.Single().Folders);
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBePastedIsReported()
    {
        var folder = await GivenAFolderAsync();
        var second = await Volumes.CreateVirtualVolumeAsync("second", "CompactDisc");
        await Sidebar.LoadAsync();

        folder = Sidebar.VirtualVolumes.Single(node => node.Name == "test").Folders.Single();
        Sidebar.CopyFolderCommand.Execute(folder);

        using (DatabaseTakenAway())
        {
            await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(
                Sidebar.VirtualVolumes.Single(node => node.Id == second.Id));
        }

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task ADatabaseThatCannotBeOpenedIsReported()
    {
        PickFiles((_, _) => Path.Combine(TempDirectory(), "nowhere", "gone.vvo"));

        await Sidebar.OpenDatabaseCommand.ExecuteAsync(Shell);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task ACopyThatCannotBeWrittenIsReported()
    {
        await GivenAFolderAsync();
        PickFiles((_, _) => Path.Combine(TempDirectory(), "nowhere", "copy.vvo"));

        await Sidebar.SaveCopyCommand.ExecuteAsync(Shell);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    [AvaloniaFact]
    public async Task AnExportThatCannotBeWrittenIsReported()
    {
        await GivenAFolderAsync();
        PickFiles((_, _) => Path.Combine(TempDirectory(), "nowhere", "export.json"));

        await Sidebar.ExportCommand.ExecuteAsync(Shell);

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    // The catalogue is copied by copying the file, so a copy onto itself would truncate it
    [AvaloniaFact]
    public async Task SavingACopyOverTheDatabaseItselfIsRefused()
    {
        await GivenAFolderAsync();
        PickFiles((_, _) => Database.DbPath);

        await Sidebar.SaveCopyCommand.ExecuteAsync(Shell);

        Assert.Empty(Told);
        Assert.Equal("test", (await Volumes.GetVirtualVolumesAsync()).Single().Name);
    }

    // Nothing is open at the start, so there is no path to copy or to export
    [AvaloniaFact]
    public async Task TheDatabaseCommandsDoNothingBeforeADatabaseIsOpen()
    {
        var unopened = new VVO.Core.Services.DatabaseService();
        var sidebar = new SidebarViewModel(
            Undo, unopened, new VVO.Core.Services.FileScannerService(),
            new VVO.Core.Services.FolderCompareService(), new VVO.Core.Services.VirtualVolumeService(unopened),
            new VVO.Core.Services.DatabaseTransferService(unopened), Settings);

        try
        {
            await sidebar.SaveCopyCommand.ExecuteAsync(Shell);
            await sidebar.ExportCommand.ExecuteAsync(Shell);

            Assert.Empty(Told);
        }
        finally
        {
            Detach(sidebar);
        }
    }

    #endregion

    #region A database that has gone

    [AvaloniaFact]
    public async Task ASidebarThatCannotReadItsDatabaseReportsItRatherThanEmptyingItself()
    {
        await GivenAFolderAsync();

        using (DatabaseTakenAway())
        {
            await Assert.ThrowsAnyAsync<Exception>(() => Sidebar.LoadAsync());
        }

        // and the failure is reported rather than thrown when it arrives by broadcast
        using (DatabaseTakenAway())
        {
            Sidebar.Receive(new VVO.UI.Messages.DatabaseReady());
            await Until(() => Told.Count > 0, "the failure to be reported");
        }

        Assert.Equal("Error", Told[0].Title);
    }

    #endregion
}
