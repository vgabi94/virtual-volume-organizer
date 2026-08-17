using Avalonia.Headless.XUnit;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// The virtual volume commands, each of which opens a dialog and so was unreachable until the
/// test could stand in for the user answering it.
/// </summary>
public class SidebarVolumeCommandTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }

    public SidebarVolumeCommandTests()
    {
        Sidebar = NewSidebar();
    }

    private async Task<VirtualVolumeNode> GivenVolumeAsync(string name = "test", string icon = "HardDrive")
    {
        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog =>
        {
            dialog.VolumeName = name;
            dialog.SelectedIcon = dialog.Icons.First(choice => choice.Key == icon);
        });

        await Sidebar.NewVirtualVolumeCommand.ExecuteAsync(null);
        return Sidebar.VirtualVolumes.Single(node => node.Name == name);
    }

    private void ConfirmDeletes()
    {
        AnswerDialogs<ConfirmDeleteDialogViewModel>(
            dialog => dialog.Confirmation = ConfirmDeleteDialogViewModel.RequiredPhrase);
    }

    #region Creating

    [AvaloniaFact]
    public async Task CreatingAVolumeStoresItAndListsIt()
    {
        await GivenVolumeAsync("Backups", "CompactDisc");

        var stored = Assert.Single(await Volumes.GetVirtualVolumesAsync());
        Assert.Equal("Backups", stored.Name);
        Assert.Equal("CompactDisc", stored.Icon);

        var listed = Assert.Single(Sidebar.VirtualVolumes);
        Assert.Equal("Backups", listed.Name);
        Assert.Same(listed, Sidebar.SelectedVirtualVolume);
    }

    [AvaloniaFact]
    public async Task CancellingTheDialogCreatesNothing()
    {
        AnswerDialogs(_ => false);

        await Sidebar.NewVirtualVolumeCommand.ExecuteAsync(null);

        Assert.Empty(await Volumes.GetVirtualVolumesAsync());
        Assert.Empty(Sidebar.VirtualVolumes);
    }

    [AvaloniaFact]
    public async Task ConfirmingWithNoNameCreatesNothing()
    {
        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog => dialog.VolumeName = "   ");

        await Sidebar.NewVirtualVolumeCommand.ExecuteAsync(null);

        Assert.Empty(await Volumes.GetVirtualVolumesAsync());
    }

    [AvaloniaFact]
    public async Task VolumesAreListedByName()
    {
        await GivenVolumeAsync("Zulu");
        await GivenVolumeAsync("Alpha");
        await GivenVolumeAsync("Mike");

        Assert.Equal(["Alpha", "Mike", "Zulu"], Sidebar.VirtualVolumes.Select(node => node.Name));
    }

    #endregion

    #region Editing

    [AvaloniaFact]
    public async Task EditingAVolumeRewritesItEverywhere()
    {
        var node = await GivenVolumeAsync("Backups");

        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog =>
        {
            dialog.VolumeName = "Old Backups";
            dialog.SelectedIcon = dialog.Icons.First(choice => choice.Key == "FloppyDisk");
        });

        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(node);

        Assert.Equal("Old Backups", node.Name);
        Assert.Equal("FloppyDisk", node.Record.Icon);

        var stored = Assert.Single(await Volumes.GetVirtualVolumesAsync());
        Assert.Equal("Old Backups", stored.Name);
    }

    [AvaloniaFact]
    public async Task CancellingAnEditChangesNothing()
    {
        var node = await GivenVolumeAsync("Backups");
        AnswerDialogs(_ => false);

        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(node);

        Assert.Equal("Backups", node.Name);
    }

    [AvaloniaFact]
    public async Task EditingFallsBackToTheSelectedVolumeWhenTheMenuBarAsks()
    {
        var node = await GivenVolumeAsync("Backups");
        Sidebar.SelectedVirtualVolume = node;

        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog => dialog.VolumeName = "Renamed");
        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(null);

        Assert.Equal("Renamed", node.Name);
    }

    #endregion

    #region Deleting

    [AvaloniaFact]
    public async Task DeletingAVolumeTakesItAndItsFoldersWithIt()
    {
        var node = await GivenVolumeAsync();
        var folder = await AddFolderAsync(node.Id);
        ConfirmDeletes();

        await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(node);

        Assert.Empty(await Volumes.GetVirtualVolumesAsync());
        Assert.Empty(Sidebar.VirtualVolumes);
        Assert.Empty(await Database.FindItemsAsync<VVO.Core.Models.FileRecord>(
            record => record.RootFolderId == folder.TreeId));
    }

    [AvaloniaFact]
    public async Task ADeleteThatIsNotConfirmedLeavesTheVolumeAlone()
    {
        var node = await GivenVolumeAsync();
        AnswerDialogs(_ => false);

        await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(node);

        Assert.Single(await Volumes.GetVirtualVolumesAsync());
        Assert.Single(Sidebar.VirtualVolumes);
    }

    [AvaloniaFact]
    public async Task TheConfirmationNamesTheVolumeAndCountsItsFolders()
    {
        var node = await GivenVolumeAsync("Backups");
        await AddFolderAsync(node.Id, "Code");
        await AddFolderAsync(node.Id, "Docs");
        await Sidebar.LoadAsync();

        ConfirmDeleteDialogViewModel? shown = null;
        AnswerDialogs(dialog =>
        {
            shown = dialog.DataContext as ConfirmDeleteDialogViewModel;
            return false;
        });

        await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(Sidebar.VirtualVolumes.Single());

        Assert.NotNull(shown);
        Assert.Equal("Backups", shown!.ItemName);
        Assert.Contains("2 folders", shown.Message);
    }

    #endregion

    #region Undo and redo

    [AvaloniaFact]
    public async Task CreatingAVolumeCanBeUndoneAndRedone()
    {
        await GivenVolumeAsync("Backups");
        Assert.True(Sidebar.UndoCommand.CanExecute(null));

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Empty(await Volumes.GetVirtualVolumesAsync());
        Assert.Empty(Sidebar.VirtualVolumes);

        await Sidebar.RedoCommand.ExecuteAsync(null);
        Assert.Single(await Volumes.GetVirtualVolumesAsync());
        Assert.Equal("Backups", Sidebar.VirtualVolumes.Single().Name);
    }

    // A redo that minted a fresh id would leave the next undo with nothing to delete
    [AvaloniaFact]
    public async Task ARedoneVolumeKeepsTheIdentityItHad()
    {
        var node = await GivenVolumeAsync("Backups");
        var id = node.Id;

        await Sidebar.UndoCommand.ExecuteAsync(null);
        await Sidebar.RedoCommand.ExecuteAsync(null);

        Assert.Equal(id, Sidebar.VirtualVolumes.Single().Id);
        Assert.Equal(id, (await Volumes.GetVirtualVolumesAsync()).Single().Id);

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Empty(await Volumes.GetVirtualVolumesAsync());
    }

    [AvaloniaFact]
    public async Task EditingAVolumeCanBeUndoneAndRedone()
    {
        var node = await GivenVolumeAsync("Backups");

        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog =>
        {
            dialog.VolumeName = "Renamed";
            dialog.SelectedIcon = dialog.Icons.First(choice => choice.Key == "FloppyDisk");
        });
        await Sidebar.EditVirtualVolumeCommand.ExecuteAsync(node);

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Backups", Sidebar.VirtualVolumes.Single().Name);
        Assert.Equal("HardDrive", Sidebar.VirtualVolumes.Single().Record.Icon);

        await Sidebar.RedoCommand.ExecuteAsync(null);
        Assert.Equal("Renamed", Sidebar.VirtualVolumes.Single().Name);
        Assert.Equal("FloppyDisk", Sidebar.VirtualVolumes.Single().Record.Icon);
    }

    // The tree went with the delete, so no earlier step can be replayed against the database
    [AvaloniaFact]
    public async Task DeletingAVolumeClearsWhatCouldBeUndone()
    {
        var node = await GivenVolumeAsync();
        await AddFolderAsync(node.Id);
        Assert.True(Sidebar.UndoCommand.CanExecute(null));

        ConfirmDeletes();
        await Sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(node);

        Assert.False(Sidebar.UndoCommand.CanExecute(null));
        Assert.False(Sidebar.RedoCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task NothingCanBeRedoneOnceAFreshStepIsRecorded()
    {
        await GivenVolumeAsync("First");
        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.True(Sidebar.RedoCommand.CanExecute(null));

        await GivenVolumeAsync("Second");

        Assert.False(Sidebar.RedoCommand.CanExecute(null));
    }

    #endregion

    #region Expanding

    [AvaloniaFact]
    public async Task ExpandingAndCollapsingReachesEveryVolume()
    {
        await GivenVolumeAsync("One");
        await GivenVolumeAsync("Two");

        Sidebar.CollapseAllVirtualVolumesCommand.Execute(null);
        Assert.All(Sidebar.VirtualVolumes, node => Assert.False(node.IsExpanded));

        Sidebar.ExpandAllVirtualVolumesCommand.Execute(null);
        Assert.All(Sidebar.VirtualVolumes, node => Assert.True(node.IsExpanded));
    }

    [AvaloniaFact]
    public async Task ACollapsedVolumeIsRememberedAgainstItsDatabase()
    {
        var node = await GivenVolumeAsync();

        node.IsExpanded = false;

        Assert.False(Settings.IsVolumeExpanded(Database.DbPath, node.Id));
    }

    [AvaloniaFact]
    public async Task SelectingAVolumeMakesItTheOneTheMenuBarActsOn()
    {
        await GivenVolumeAsync("One");
        var two = await GivenVolumeAsync("Two");

        Sidebar.SelectVirtualVolumeCommand.Execute(two);

        Assert.Same(two, Sidebar.SelectedVirtualVolume);
        Assert.Equal(two.Id, Sidebar.TargetVirtualVolumeId);
    }

    #endregion
}
