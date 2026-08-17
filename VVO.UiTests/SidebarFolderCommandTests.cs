using Avalonia.Headless.XUnit;
using VVO.Core.Models;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

public class SidebarFolderCommandTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }

    public SidebarFolderCommandTests()
    {
        Sidebar = NewSidebar();
    }

    /// <summary>Two volumes, with one folder in the first, as the sidebar shows them.</summary>
    private async Task<(VirtualVolumeNode One, VirtualVolumeNode Two, FolderItem Folder)> GivenAsync()
    {
        var one = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await Volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        await AddFolderAsync(one.Id, "Code");

        await Sidebar.LoadAsync();

        return (Sidebar.VirtualVolumes.Single(node => node.Id == one.Id),
                Sidebar.VirtualVolumes.Single(node => node.Id == two.Id),
                Sidebar.VirtualVolumes.Single(node => node.Id == one.Id).Folders.Single());
    }

    private void ConfirmDeletes()
    {
        AnswerDialogs<ConfirmDeleteDialogViewModel>(
            dialog => dialog.Confirmation = ConfirmDeleteDialogViewModel.RequiredPhrase);
    }

    private Task<IReadOnlyCollection<FileRecord>> TreeOfAsync(Guid treeId)
    {
        return Database.FindItemsAsync<FileRecord>(record => record.RootFolderId == treeId);
    }

    #region Loading

    [AvaloniaFact]
    public async Task TheSidebarListsWhatTheDatabaseHolds()
    {
        var (one, two, folder) = await GivenAsync();

        Assert.Equal(["test", "test2"], Sidebar.VirtualVolumes.Select(node => node.Name));
        Assert.Equal("Code", folder.Title);
        Assert.Equal(@"D:\Code", folder.Path);
        Assert.Empty(two.Folders);
    }

    [AvaloniaFact]
    public async Task TheFirstFolderIsSelectedSoTheExplorerHasSomethingToShow()
    {
        var (_, _, folder) = await GivenAsync();

        Assert.Same(folder, Sidebar.SelectedFolder);
    }

    #endregion

    #region Copy, cut and paste

    [AvaloniaFact]
    public async Task NothingCanBePastedUntilAFolderIsOnTheClipboard()
    {
        var (_, two, _) = await GivenAsync();

        Assert.False(Sidebar.PasteIntoVirtualVolumeCommand.CanExecute(two));
        Assert.False(Sidebar.PasteBesideFolderCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task CopyingThenPastingPutsASecondFolderInTheOtherVolume()
    {
        var (one, two, folder) = await GivenAsync();

        Sidebar.CopyFolderCommand.Execute(folder);
        Assert.True(Sidebar.PasteIntoVirtualVolumeCommand.CanExecute(two));

        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(two);

        Assert.Single(one.Folders);
        Assert.Single(two.Folders);

        // The copy shares the original's tree, which is what makes it cost one record
        Assert.Equal(folder.Entry.TreeId, two.Folders.Single().Entry.TreeId);
        Assert.NotEqual(folder.Entry.Id, two.Folders.Single().Entry.Id);
    }

    [AvaloniaFact]
    public async Task CuttingThenPastingMovesTheFolderRatherThanCopyingIt()
    {
        var (one, two, folder) = await GivenAsync();

        Sidebar.CutFolderCommand.Execute(folder);
        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(two);

        Assert.Empty(one.Folders);
        Assert.Equal("Code", two.Folders.Single().Title);
        Assert.Equal(two.Id, two.Folders.Single().Entry.VirtualVolumeId);
    }

    [AvaloniaFact]
    public async Task ACutFolderIsDimmedUntilItIsPastedOrCalledOff()
    {
        var (one, _, folder) = await GivenAsync();

        Sidebar.CutFolderCommand.Execute(folder);

        Assert.True(one.Folders.Single().IsCut);
    }

    // A cut folder is already where it would land, so pasting it back is how the move is called off
    [AvaloniaFact]
    public async Task PastingACutFolderIntoItsOwnVolumeCallsTheMoveOff()
    {
        var (one, _, folder) = await GivenAsync();
        Sidebar.CutFolderCommand.Execute(folder);

        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(one);

        Assert.Single(one.Folders);
        Assert.False(one.Folders.Single().IsCut);
        Assert.False(Sidebar.PasteIntoVirtualVolumeCommand.CanExecute(one));
    }

    // A copy pasted into its own volume duplicates it instead
    [AvaloniaFact]
    public async Task PastingACopyIntoItsOwnVolumeMakesASecondRow()
    {
        var (one, _, folder) = await GivenAsync();
        Sidebar.CopyFolderCommand.Execute(folder);

        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(one);

        Assert.Equal(2, one.Folders.Count);
    }

    [AvaloniaFact]
    public async Task PastingBesideAFolderLandsInThatFoldersVolume()
    {
        var (one, two, folder) = await GivenAsync();
        await AddFolderAsync(two.Record.Id, "Docs");
        await Sidebar.LoadAsync();

        var code = Sidebar.VirtualVolumes.Single(n => n.Id == one.Id).Folders.Single();
        var docs = Sidebar.VirtualVolumes.Single(n => n.Id == two.Id).Folders.Single();

        Sidebar.CopyFolderCommand.Execute(code);
        await Sidebar.PasteBesideFolderCommand.ExecuteAsync(docs);

        Assert.Equal(2, Sidebar.VirtualVolumes.Single(n => n.Id == two.Id).Folders.Count);
    }

    [AvaloniaFact]
    public async Task TheClipboardIsEmptiedWhenTheDatabaseChanges()
    {
        var (_, _, folder) = await GivenAsync();
        Sidebar.CopyFolderCommand.Execute(folder);

        await Sidebar.LoadAsync();

        Assert.False(Sidebar.PasteBesideFolderCommand.CanExecute(null));
    }

    #endregion

    #region Duplicating

    [AvaloniaFact]
    public async Task DuplicatingAddsARowUnderItsOwnName()
    {
        var (one, _, folder) = await GivenAsync();
        AnswerDialogs<NameDialogViewModel>(dialog => dialog.Name = "Code (old)");

        await Sidebar.DuplicateFolderCommand.ExecuteAsync(folder);

        Assert.Equal(["Code", "Code (old)"], one.Folders.Select(item => item.Title));
        Assert.Equal(folder.Entry.TreeId, one.Folders[1].Entry.TreeId);
    }

    [AvaloniaFact]
    public async Task ADuplicateOpensOnTheNameItIsCopying()
    {
        var (_, _, folder) = await GivenAsync();

        string? offered = null;
        AnswerDialogs(dialog =>
        {
            offered = (dialog.DataContext as NameDialogViewModel)?.Name;
            return false;
        });

        await Sidebar.DuplicateFolderCommand.ExecuteAsync(folder);

        Assert.Equal("Code", offered);
    }

    [AvaloniaFact]
    public async Task CancellingADuplicateAddsNothing()
    {
        var (one, _, folder) = await GivenAsync();
        AnswerDialogs(_ => false);

        await Sidebar.DuplicateFolderCommand.ExecuteAsync(folder);

        Assert.Single(one.Folders);
    }

    #endregion

    #region Editing

    [AvaloniaFact]
    public async Task EditingAFolderRewritesHowItIsListedAndStored()
    {
        var (one, _, folder) = await GivenAsync();

        AnswerDialogs<FolderDialogViewModel>(dialog =>
        {
            dialog.FolderName = "My Code";
            dialog.Description = "everything I write";
            dialog.SelectedIcon = dialog.Icons.First(choice => choice.Key == "FolderStar");
        });

        await Sidebar.EditFolderCommand.ExecuteAsync(folder);

        var listed = one.Folders.Single();
        Assert.Equal("My Code", listed.Title);
        Assert.Equal("everything I write", listed.Description);
        Assert.Equal("FolderStar", listed.Entry.Icon);

        var stored = Assert.Single(await Volumes.GetFoldersAsync(one.Id));
        Assert.Equal("My Code", stored.Label);
    }

    // A name matching the scanned one is no label at all, so the folder follows the next scan
    [AvaloniaFact]
    public async Task NamingAFolderWhatItWasScannedAsClearsItsLabel()
    {
        var (one, _, folder) = await GivenAsync();

        AnswerDialogs<FolderDialogViewModel>(dialog => dialog.FolderName = "My Code");
        await Sidebar.EditFolderCommand.ExecuteAsync(folder);

        AnswerDialogs<FolderDialogViewModel>(dialog => dialog.FolderName = "Code");
        await Sidebar.EditFolderCommand.ExecuteAsync(one.Folders.Single());

        Assert.Null((await Volumes.GetFoldersAsync(one.Id)).Single().Label);
        Assert.Equal("Code", one.Folders.Single().Title);
    }

    #endregion

    #region Deleting

    [AvaloniaFact]
    public async Task DeletingAFolderTakesItsTreeWithIt()
    {
        var (one, _, folder) = await GivenAsync();
        ConfirmDeletes();

        await Sidebar.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Empty(one.Folders);
        Assert.Empty(await Volumes.GetFoldersAsync(one.Id));
        Assert.Empty(await TreeOfAsync(folder.Entry.TreeId));
    }

    [AvaloniaFact]
    public async Task ADeleteThatIsNotConfirmedLeavesTheFolderAlone()
    {
        var (one, _, folder) = await GivenAsync();
        AnswerDialogs(_ => false);

        await Sidebar.DeleteFolderCommand.ExecuteAsync(folder);

        Assert.Single(one.Folders);
        Assert.NotEmpty(await TreeOfAsync(folder.Entry.TreeId));
    }

    [AvaloniaFact]
    public async Task DeletingOneOfTwoFoldersSharingATreeLeavesTheTree()
    {
        var (one, two, folder) = await GivenAsync();
        Sidebar.CopyFolderCommand.Execute(folder);
        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(two);

        ConfirmDeletes();
        await Sidebar.DeleteFolderCommand.ExecuteAsync(one.Folders.Single());

        Assert.NotEmpty(await TreeOfAsync(folder.Entry.TreeId));
        Assert.Single(two.Folders);
    }

    #endregion

    #region Undo and redo

    [AvaloniaFact]
    public async Task APasteCanBeUndoneAndRedone()
    {
        var (one, two, folder) = await GivenAsync();
        Sidebar.CopyFolderCommand.Execute(folder);
        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(two);

        var copyId = two.Folders.Single().Entry.Id;

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Empty(two.Folders);
        Assert.Empty(await Volumes.GetFoldersAsync(two.Id));

        await Sidebar.RedoCommand.ExecuteAsync(null);
        Assert.Equal(copyId, two.Folders.Single().Entry.Id);
    }

    [AvaloniaFact]
    public async Task AMoveCanBeUndoneAndRedone()
    {
        var (one, two, folder) = await GivenAsync();
        Sidebar.CutFolderCommand.Execute(folder);
        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(two);

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Single(one.Folders);
        Assert.Empty(two.Folders);
        Assert.Equal(one.Id, (await Volumes.GetFoldersAsync(one.Id)).Single().VirtualVolumeId);

        await Sidebar.RedoCommand.ExecuteAsync(null);
        Assert.Empty(one.Folders);
        Assert.Single(two.Folders);
    }

    [AvaloniaFact]
    public async Task ADuplicateCanBeUndoneAndRedone()
    {
        var (one, _, folder) = await GivenAsync();
        AnswerDialogs<NameDialogViewModel>(dialog => dialog.Name = "Code (old)");
        await Sidebar.DuplicateFolderCommand.ExecuteAsync(folder);

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Single(one.Folders);

        await Sidebar.RedoCommand.ExecuteAsync(null);
        Assert.Equal(["Code", "Code (old)"], one.Folders.Select(item => item.Title));
    }

    [AvaloniaFact]
    public async Task AnEditCanBeUndoneAndRedone()
    {
        var (one, _, folder) = await GivenAsync();

        AnswerDialogs<FolderDialogViewModel>(dialog =>
        {
            dialog.FolderName = "My Code";
            dialog.Description = "notes";
        });
        await Sidebar.EditFolderCommand.ExecuteAsync(folder);

        await Sidebar.UndoCommand.ExecuteAsync(null);
        Assert.Equal("Code", one.Folders.Single().Title);
        Assert.Equal(string.Empty, one.Folders.Single().Description);

        await Sidebar.RedoCommand.ExecuteAsync(null);
        Assert.Equal("My Code", one.Folders.Single().Title);
        Assert.Equal("notes", one.Folders.Single().Description);
    }

    [AvaloniaFact]
    public async Task DeletingAFolderClearsWhatCouldBeUndone()
    {
        var (one, two, folder) = await GivenAsync();
        Sidebar.CopyFolderCommand.Execute(folder);
        await Sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(two);
        Assert.True(Sidebar.UndoCommand.CanExecute(null));

        ConfirmDeletes();
        await Sidebar.DeleteFolderCommand.ExecuteAsync(one.Folders.Single());

        Assert.False(Sidebar.UndoCommand.CanExecute(null));
    }

    #endregion
}
