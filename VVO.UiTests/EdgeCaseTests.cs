using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using VVO.UI;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// The corners each command was written to survive: work overtaken by newer work, a clipboard
/// pointing at something that has gone, and a database that stops answering part way through.
/// </summary>
public class EdgeCaseTests : UiTestBase
{
    #region Shrinking

    [AvaloniaFact]
    public async Task ShrinkingADatabaseThatIsNotThereDoesNothing()
    {
        var unopened = new VVO.Core.Services.DatabaseService();
        var mainWindow = NewMainWindowOver(unopened);

        await mainWindow.ShrinkDatabaseCommand.ExecuteAsync(null);

        Assert.Empty(Told);
    }

    [AvaloniaFact]
    public async Task ADatabaseThatCannotBeShrunkIsReportedAndTheStatusTakenDown()
    {
        var mainWindow = NewMainWindow();

        using (DatabaseTakenAway())
        {
            await mainWindow.ShrinkDatabaseCommand.ExecuteAsync(null);
        }

        Assert.Equal("Error", Assert.Single(Told).Title);
        Assert.False(mainWindow.IsStatusMessageVisible);
    }

    // A shrink can run for a while on a large catalogue, so it has to be one the user can stop
    [AvaloniaFact]
    public async Task ShrinkingOffersCancelAndSaysWhichStepItIsOn()
    {
        using var probe = new MessageProbe<UpdateStatusMessage>();
        var mainWindow = NewMainWindow();

        await mainWindow.ShrinkDatabaseCommand.ExecuteAsync(null);

        Assert.Contains(probe.All, message => message.IsVisible && message.IsCancellable);
        Assert.Contains(probe.All, message => message.Message.StartsWith("Rebuilding indexes"));
        Assert.False(mainWindow.IsStatusMessageVisible);
    }

    #endregion

    #region Dialogs off the menu bar

    [AvaloniaFact]
    public async Task NoMenuDialogOpensOverSomethingThatIsNotAWindow()
    {
        var mainWindow = NewMainWindow();
        var orphan = new Border();

        await mainWindow.ShowShortcutsCommand.ExecuteAsync(orphan);
        await mainWindow.ShowOptionsCommand.ExecuteAsync(orphan);

        Assert.Empty(Opened);
    }

    #endregion

    #region A scan called off

    // Cancelled the moment the status appears, which is as soon as there is anything to cancel
    [AvaloniaFact]
    public async Task AScanCalledOffLeavesNothingBehind()
    {
        var sidebar = NewSidebar();
        var mainWindow = NewMainWindow(sidebar);
        await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await sidebar.LoadAsync();

        var scanned = TempDirectory();
        File.WriteAllText(Path.Combine(scanned, "readme.md"), "hello");
        PickFolder(scanned);

        void CancelAsSoonAsItStarts(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainWindowViewModel.IsStatusMessageVisible)
                && mainWindow.IsStatusMessageVisible)
            {
                mainWindow.Receive(new CancelRequestedMessage());
            }
        }

        mainWindow.PropertyChanged += CancelAsSoonAsItStarts;
        await mainWindow.AddFolderCommand.ExecuteAsync(Shell);
        mainWindow.PropertyChanged -= CancelAsSoonAsItStarts;

        Assert.Empty(sidebar.VirtualVolumes.Single().Folders);
        Assert.Empty(Told);
        Assert.False(mainWindow.IsStatusMessageVisible);
    }

    #endregion

    #region A clipboard pointing at something that has gone

    [AvaloniaFact]
    public async Task PastingWithAnEmptyClipboardDoesNothing()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        Assert.False(sidebar.PasteIntoVirtualVolumeCommand.CanExecute(sidebar.VirtualVolumes.Single()));
        await sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(sidebar.VirtualVolumes.Single());

        Assert.Single(sidebar.VirtualVolumes.Single().Folders);
    }

    [AvaloniaFact]
    public async Task PastingAFolderThatHasSinceBeenDeletedGivesUpTheClipboard()
    {
        var sidebar = NewSidebar();
        var first = await Volumes.CreateVirtualVolumeAsync("first", "HardDrive");
        var second = await Volumes.CreateVirtualVolumeAsync("second", "CompactDisc");
        var entry = await AddFolderAsync(first.Id, "Code");
        await sidebar.LoadAsync();

        sidebar.CopyFolderCommand.Execute(
            sidebar.VirtualVolumes.Single(node => node.Id == first.Id).Folders.Single());

        // Taken out from under the clipboard, as a second window on the same database would
        await Volumes.RemoveFolderAsync(entry.Id);
        await sidebar.LoadAsync();

        await sidebar.PasteIntoVirtualVolumeCommand.ExecuteAsync(
            sidebar.VirtualVolumes.Single(node => node.Id == second.Id));

        Assert.False(sidebar.PasteIntoVirtualVolumeCommand.CanExecute(null));
        Assert.Empty(sidebar.VirtualVolumes.SelectMany(node => node.Folders));
    }

    [AvaloniaFact]
    public async Task DeletingAVolumeGivesUpAClipboardPointingIntoIt()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        sidebar.CopyFolderCommand.Execute(sidebar.VirtualVolumes.Single().Folders.Single());
        Assert.True(sidebar.PasteIntoVirtualVolumeCommand.CanExecute(null));

        AnswerDialogs(_ => true);
        await sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(sidebar.VirtualVolumes.Single());

        Assert.False(sidebar.PasteIntoVirtualVolumeCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task DeletingTheVolumeAFolderIsSelectedInClearsTheSelection()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        Assert.NotNull(sidebar.SelectedFolder);

        AnswerDialogs(_ => true);
        await sidebar.DeleteVirtualVolumeCommand.ExecuteAsync(sidebar.VirtualVolumes.Single());

        Assert.Null(sidebar.SelectedFolder);
        Assert.Null(sidebar.SelectedVirtualVolume);
    }

    #endregion

    #region Dialogs confirmed with nothing filled in

    [AvaloniaFact]
    public async Task AFolderConfirmedWithNoNameIsLeftAsItWas()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        AnswerDialogs<FolderDialogViewModel>(dialog => dialog.FolderName = "   ");
        await sidebar.EditFolderCommand.ExecuteAsync(sidebar.VirtualVolumes.Single().Folders.Single());

        Assert.Equal("Code", sidebar.VirtualVolumes.Single().Folders.Single().Title);
    }

    [AvaloniaFact]
    public async Task AVolumeConfirmedWithNoNameIsNotCreated()
    {
        var sidebar = NewSidebar();

        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog => dialog.VolumeName = "   ");
        await sidebar.NewVirtualVolumeCommand.ExecuteAsync(null);

        Assert.Empty(await Volumes.GetVirtualVolumesAsync());
    }

    [AvaloniaFact]
    public async Task ADuplicateConfirmedWithNoNameIsNotMade()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        AnswerDialogs<NameDialogViewModel>(dialog => dialog.Name = "   ");
        await sidebar.DuplicateFolderCommand.ExecuteAsync(sidebar.VirtualVolumes.Single().Folders.Single());

        Assert.Single(sidebar.VirtualVolumes.Single().Folders);
    }

    #endregion

    #region Transfers called off at the second step

    [AvaloniaFact]
    public async Task AnExportWithNowhereToWriteToDoesNothing()
    {
        var sidebar = NewSidebar();
        await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await sidebar.LoadAsync();

        PickFiles((_, _) => null);
        await sidebar.ExportCommand.ExecuteAsync(Shell);

        Assert.Empty(Told);
    }

    [AvaloniaFact]
    public async Task AnImportWithNowhereToLandDoesNothing()
    {
        var sidebar = NewSidebar();
        await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await sidebar.LoadAsync();

        var json = TempPath("json");
        PickFiles((_, extension) => extension == "json" ? json : null);
        await sidebar.ExportCommand.ExecuteAsync(Shell);

        var before = Database.DbPath;
        await sidebar.ImportCommand.ExecuteAsync(Shell);

        Assert.Equal(before, Database.DbPath);
    }

    #endregion

    #region Searches overtaken by newer ones

    [AvaloniaFact]
    public async Task OnlyTheLastOfABurstOfKeystrokesReachesTheExplorer()
    {
        var sidebar = NewSidebar();
        await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await sidebar.LoadAsync();

        using var probe = new MessageProbe<SearchAllMessage>();

        sidebar.SearchAllTerm = "A";
        sidebar.SearchAllTerm = "Ad";
        sidebar.SearchAllTerm = "Adv";

        await Until(() => probe.All.Count > 0, "the search to be broadcast");
        await Task.Delay(400);

        Assert.Equal("Adv", Assert.Single(probe.All).Term);
    }

    [AvaloniaFact]
    public async Task ASearchThatCannotBeBroadcastIsReported()
    {
        var sidebar = NewSidebar();
        await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await sidebar.LoadAsync();

        using var probe = new MessageProbe<SearchAllMessage>(
            _ => throw new InvalidOperationException("the explorer is not listening"));

        sidebar.SearchAllTerm = "Advent";

        await Until(() => Told.Count > 0, "the failure to be reported");
        Assert.Equal("Error", Told[0].Title);
    }

    [AvaloniaFact]
    public async Task ASearchOfAVolumeThatCannotBeReadIsReported()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        using (DatabaseTakenAway())
        {
            explorer.SearchTerm = "day1";
            await Until(() => Told.Count > 0, "the failure to be reported");
        }

        Assert.Equal("Error", Told[0].Title);
    }

    // The folders of a tree are read once and kept, since every hit needs them to say where it is
    [AvaloniaFact]
    public async Task SearchingTwiceReadsTheFoldersOnlyOnce()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        explorer.SearchTerm = "day1";
        await Until(() => explorer.IsFlatMode && explorer.Files!.Count == 1, "the first search");

        explorer.SearchTerm = "2023";
        await Until(() => explorer.Files!.Count == 1 && explorer.Files[0].Name == "2023", "the second search");

        Assert.Equal(@"test:\Code\AdventOfCode", Assert.Single(explorer.Files ?? []).Location);
    }

    // Two hits under the same folder assemble that folder's path once between them
    [AvaloniaFact]
    public async Task HitsSharingAFolderAllSayTheSameThingAboutWhereTheyAre()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        explorer.SearchTerm = "d";
        await Until(() => explorer.IsFlatMode && explorer.Files!.Count == 3, "the search to run");

        Assert.Equal(
            [@"test:\Code", @"test:\Code", @"test:\Code\AdventOfCode\2023"],
            explorer.Files!.Select(file => file.Location).OrderBy(location => location, StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task SearchingEveryFolderTwiceReadsThemOnlyOnce()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        var scopes = new[] { new SearchScope(entry.TreeId, "Code", "test") };

        await explorer.SearchAllAsync(new SearchAllMessage("day1", scopes));
        await explorer.SearchAllAsync(new SearchAllMessage("2023", scopes));

        Assert.Equal(@"test:\Code\AdventOfCode", Assert.Single(explorer.Files ?? []).Location);
    }

    #endregion

    #region Work overtaken by the user

    private async Task<VolumeExplorerViewModel> GivenAFolderShownAsync()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        return explorer;
    }

    // A search reports itself before it reads anything, so the user opening a folder at that
    // moment is the narrowest window there is for one to overtake the other
    [AvaloniaFact]
    public async Task AFolderOpenedWhileASearchRunsWinsOverTheSearch()
    {
        var explorer = await GivenAFolderShownAsync();
        var advent = explorer.Files!.Single(file => file.Name == "AdventOfCode");

        var overtaken = false;
        using var reported = new MessageProbe<UpdateStatusMessage>(message =>
        {
            if (overtaken || !message.IsVisible)
                return;

            overtaken = true;
            explorer.OpenItemCommand.Execute(advent);
        });

        explorer.SearchTerm = "day1";

        await Until(() => explorer.Breadcrumbs.Count == 2, "the folder to open");
        Assert.False(explorer.IsFlatMode);
        Assert.Equal(["Code", "AdventOfCode"], explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task AFolderOpenedWhileASearchOfEveryVolumeRunsWinsOverTheSearch()
    {
        var explorer = await GivenAFolderShownAsync();
        var advent = explorer.Files!.Single(file => file.Name == "AdventOfCode");
        var entry = (await Database.ReadItemsAsync<VVO.Core.Models.RootFolderMetadata>()).Single();

        var overtaken = false;
        using var reported = new MessageProbe<UpdateStatusMessage>(message =>
        {
            if (overtaken || !message.IsVisible)
                return;

            overtaken = true;
            explorer.OpenItemCommand.Execute(advent);
        });

        await explorer.SearchAllAsync(new SearchAllMessage(
            "day1", [new SearchScope(entry.TreeId, "Code", "test")]));

        await Until(() => explorer.Breadcrumbs.Count == 2, "the folder to open");
        Assert.False(explorer.IsFlatMode);
    }

    // Going up one and jumping to the root at once: the crumb the user pressed last is where
    // they end up, rather than wherever finished reading last
    [AvaloniaFact]
    public async Task ACrumbPressedWhileAnotherFolderIsBeingReadWinsOverIt()
    {
        var explorer = await GivenAFolderShownAsync();
        var root = explorer.Breadcrumbs[0].Id;

        await explorer.OpenItemCommand.ExecuteAsync(explorer.Files!.Single(file => file.Name == "AdventOfCode"));
        await explorer.OpenItemCommand.ExecuteAsync(explorer.Files!.Single(file => file.Name == "2023"));

        var overtaken = false;
        using var reported = new MessageProbe<UpdateStatusMessage>(message =>
        {
            if (overtaken || !message.IsVisible)
                return;

            overtaken = true;
            explorer.NavigateToCommand.Execute(root);
        });

        await explorer.NavigateUpCommand.ExecuteAsync(null);

        await Until(() => explorer.Breadcrumbs.Count == 1, "the crumb to be followed");
        Assert.Equal(["Code"], explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    // The explorer answers the sidebar without anything waiting on it, so a failure anywhere
    // in that answer — including in whatever it broadcasts on the way — has nowhere to go
    [AvaloniaFact]
    public async Task AFolderTheExplorerCannotFinishShowingIsReported()
    {
        var explorer = NewExplorer();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");

        using var reported = new MessageProbe<UpdateStatusMessage>(
            message => { if (!message.IsVisible) throw new InvalidOperationException("nobody is listening"); });

        WeakReferenceMessenger.Default.Send(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        await Until(() => Told.Count > 0, "the failure to be reported");
        Assert.Equal("Error", Told[0].Title);
    }

    #endregion

    private MainWindowViewModel NewMainWindowOver(VVO.Core.Services.IDatabaseService database)
    {
        var sidebar = new SidebarViewModel(
            Undo, database, new VVO.Core.Services.FileScannerService(),
            new VVO.Core.Services.FolderCompareService(), new VVO.Core.Services.VirtualVolumeService(database),
            new VVO.Core.Services.DatabaseTransferService(database), Settings);

        var mainWindow = new MainWindowViewModel(
            Undo, new VVO.Core.Services.FileScannerService(), database,
            new VVO.Core.Services.VirtualVolumeService(database), Settings,
            sidebar, NewStartPage(), NewExplorer());

        Detach(sidebar);
        Detach(mainWindow);

        return mainWindow;
    }
}
