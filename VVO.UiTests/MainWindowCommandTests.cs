using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

public class MainWindowCommandTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }
    private MainWindowViewModel MainWindow { get; }

    public MainWindowCommandTests()
    {
        Sidebar = NewSidebar();
        MainWindow = NewMainWindow(Sidebar);
    }

    #region Dialogs off the menu bar

    [AvaloniaFact]
    public async Task AboutOpensTheAboutDialog()
    {
        await MainWindow.ShowAboutCommand.ExecuteAsync(Shell);

        Assert.IsType<AboutDialogView>(Assert.Single(Opened));
    }

    [AvaloniaFact]
    public async Task KeyboardShortcutsOpensItsDialog()
    {
        await MainWindow.ShowShortcutsCommand.ExecuteAsync(Shell);

        Assert.IsType<ShortcutsDialogView>(Assert.Single(Opened));
    }

    [AvaloniaFact]
    public async Task NothingOpensWithoutAWindowToOpenItOver()
    {
        await MainWindow.ShowAboutCommand.ExecuteAsync(new Border());

        Assert.Empty(Opened);
    }

    #endregion

    #region Options

    [AvaloniaFact]
    public async Task SavingTheOptionsDialogWritesTheSettingsAndTellsTheSidebar()
    {
        AnswerDialogs<OptionsDialogViewModel>(dialog =>
        {
            dialog.ShowFolderDetailsAlways = true;
            dialog.MaxRecentDatabases = 3;
        });

        await MainWindow.ShowOptionsCommand.ExecuteAsync(Shell);

        Assert.True(Settings.Data.ShowFolderDetailsAlways);
        Assert.Equal(3, Settings.Data.MaxRecentFiles);
        Assert.True(Sidebar.ShowFolderDetailsAlways);
    }

    [AvaloniaFact]
    public async Task CancellingTheOptionsDialogChangesNothing()
    {
        AnswerDialogs(_ => false);

        await MainWindow.ShowOptionsCommand.ExecuteAsync(Shell);

        Assert.False(Settings.Data.ShowFolderDetailsAlways);
    }

    #endregion

    #region Tools

    [AvaloniaFact]
    public async Task ShrinkingReportsWhatItReclaimed()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var folder = await AddFolderAsync(volume.Id);
        await Volumes.RemoveFolderAsync(folder.Id);

        await MainWindow.ShrinkDatabaseCommand.ExecuteAsync(null);

        var (title, message) = Assert.Single(Told);
        Assert.Equal("Shrink Database", title);
        Assert.Contains("The database is", message);
    }

    [AvaloniaFact]
    public async Task ScanningWithNoVolumeToScanIntoSaysSo()
    {
        await MainWindow.AddFolderCommand.ExecuteAsync(Shell);

        var (title, message) = Assert.Single(Told);
        Assert.Equal("Add Folder", title);
        Assert.Contains("no virtual volumes", message);
    }

    #endregion

    #region Find

    [AvaloniaFact]
    public async Task FindPutsTheCaretInTheExplorersSearchBox()
    {
        var explorer = NewExplorer();
        var view = new VolumeExplorerView { DataContext = explorer };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Pump();

        MainWindow.FindCommand.Execute(null);
        Pump();

        var box = view.GetControl<TextBox>("FileSearchBox");
        Assert.True(box.IsFocused);
        window.Close();

        await Task.CompletedTask;
    }

    #endregion

    #region Status

    [AvaloniaFact]
    public void AStatusMessageIsShownAndTakenDownAgain()
    {
        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Scanning...", true));
        Assert.True(MainWindow.IsStatusMessageVisible);
        Assert.True(MainWindow.IsCancelVisible);
        Assert.Equal("Scanning...", MainWindow.StatusMessage);

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
        Assert.False(MainWindow.IsStatusMessageVisible);
        Assert.False(MainWindow.IsCancelVisible);
    }

    [AvaloniaFact]
    public void TheStartPageIsPutAwayOnceADatabaseIsOpen()
    {
        Assert.True(MainWindow.IsStartPageVisible);

        WeakReferenceMessenger.Default.Send(new HideStartPage());

        Assert.False(MainWindow.IsStartPageVisible);
    }

    [AvaloniaFact]
    public void TheDatabaseStatusYieldsItsPlaceToARunningOperation()
    {
        MainWindow.Receive(new DatabaseReady());
        Assert.True(MainWindow.IsDatabaseStatusVisible);
        Assert.Contains("Last Write", MainWindow.LastWriteText);

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Working..."));
        Assert.False(MainWindow.IsDatabaseStatusVisible);
    }

    [AvaloniaFact]
    public async Task ScanningAFolderPlacesItAndSelectsIt()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await Sidebar.LoadAsync();

        var entry = await AddFolderAsync(volume.Id, "Code");
        var root = (await Database.FindItemsAsync<FileRecord>(record => record.Id == entry.TreeId)).Single();

        Sidebar.Receive(new FolderAddedMessage(entry, root));

        Assert.Equal("Code", Sidebar.VirtualVolumes.Single().Folders.Single().Title);
        Assert.Equal("Code", Sidebar.SelectedFolder?.Title);
    }

    #endregion
}
