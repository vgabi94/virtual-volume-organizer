using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// Comparing a catalogued folder against another one — either a second entry in the catalogue
/// or a folder still on disk.
/// </summary>
public class SidebarCompareTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }

    public SidebarCompareTests()
    {
        Sidebar = NewSidebar();
    }

    private async Task GivenTwoFoldersAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddDeepFolderAsync(volume.Id, "Code");
        await AddFolderAsync(volume.Id, "Docs");

        await Sidebar.LoadAsync();
    }

    private FolderItem Folder(string title) =>
        Sidebar.VirtualVolumes.SelectMany(node => node.Folders).Single(folder => folder.Title == title);

    private CompareResultsView? Results =>
        Shell.OwnedWindows.OfType<CompareResultsView>().FirstOrDefault();

    private void CloseResults() => Results?.Close();

    #region Against another catalogued folder

    [AvaloniaFact]
    public async Task ComparingTwoCataloguedFoldersShowsWhatIsInOneAndNotTheOther()
    {
        await GivenTwoFoldersAsync();
        var docs = Folder("Docs");

        AnswerDialogs<CompareTargetDialogViewModel>(dialog =>
            dialog.SelectedFolder = dialog.Folders.Single(choice => choice.Name == "Docs"));

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        var view = Results;
        Assert.NotNull(view);

        var viewModel = (CompareResultsViewModel)view!.DataContext!;
        Assert.Equal("Code", viewModel.SourceName);
        Assert.Equal("Docs", viewModel.TargetName);
        Assert.True(viewModel.HasDifferences);

        CloseResults();
        Assert.NotEqual(Guid.Empty, docs.Entry.Id);
    }

    // The folder on offer is whatever the catalogue held when the dialog opened, and it can
    // have been deleted from another window before the comparison starts
    [AvaloniaFact]
    public async Task ComparingAgainstAFolderThatHasGoneShowsNothingRatherThanFailing()
    {
        await GivenTwoFoldersAsync();

        AnswerDialogs<CompareTargetDialogViewModel>(dialog =>
            dialog.SelectedFolder = new FolderChoice(Guid.NewGuid(), "Elsewhere", @"D:\Elsewhere"));

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Null(Results);
        Assert.Empty(Told);
    }

    [AvaloniaFact]
    public async Task AFolderIsNotOfferedAsSomethingToCompareItselfAgainst()
    {
        await GivenTwoFoldersAsync();

        IReadOnlyList<FolderChoice> offered = [];
        AnswerDialogs<CompareTargetDialogViewModel>(dialog =>
        {
            offered = dialog.Folders;
            dialog.SelectedFolder = null;
        });

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Equal(["Docs"], offered.Select(choice => choice.Name));
    }

    #endregion

    #region Against a folder still on disk

    [AvaloniaFact]
    public async Task ComparingAgainstAFolderOnDiskScansItFirst()
    {
        await GivenTwoFoldersAsync();

        var live = TempDirectory();
        File.WriteAllText(Path.Combine(live, "readme.md"), "hello");

        AnswerDialogs<CompareTargetDialogViewModel>(dialog => dialog.LiveFolderPath = live);

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        var viewModel = (CompareResultsViewModel)Results!.DataContext!;
        Assert.Equal(live, viewModel.TargetName);
        Assert.True(viewModel.HasDifferences);

        CloseResults();
    }

    [AvaloniaFact]
    public async Task AComparisonOffersToBeCalledOffWhileItRunsAndClearsTheStatusWhenDone()
    {
        await GivenTwoFoldersAsync();
        var mainWindow = NewMainWindow(Sidebar);
        using var reported = new MessageProbe<UpdateStatusMessage>();

        AnswerDialogs<CompareTargetDialogViewModel>(dialog =>
            dialog.SelectedFolder = dialog.Folders.Single(choice => choice.Name == "Docs"));

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Contains(reported.All, message => message.IsVisible && message.IsCancellable);
        Assert.False(mainWindow.IsStatusMessageVisible);
        Assert.False(mainWindow.IsCancelVisible);

        CloseResults();
    }

    // Cancelling on the first report puts the request in before the scan starts, which is what
    // pressing the button the moment it appears amounts to
    [AvaloniaFact]
    public async Task AComparisonCalledOffPartWayThroughShowsNoResults()
    {
        await GivenTwoFoldersAsync();
        using var reported = new MessageProbe<UpdateStatusMessage>(
            _ => Sidebar.Receive(new CancelRequestedMessage()));

        AnswerDialogs<CompareTargetDialogViewModel>(dialog => dialog.LiveFolderPath = TempDirectory());

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Null(Results);
        Assert.Empty(Told);
    }

    #endregion

    #region What is not compared

    [AvaloniaFact]
    public async Task CancellingTheTargetDialogComparesNothing()
    {
        await GivenTwoFoldersAsync();
        AnswerDialogs(_ => false);

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Null(Results);
    }

    [AvaloniaFact]
    public async Task ThereIsNothingToCompareWithoutAFolderToCompare()
    {
        await GivenTwoFoldersAsync();

        await Sidebar.CompareCommand.ExecuteAsync(null);

        Assert.Empty(Opened);
    }

    [AvaloniaFact]
    public async Task NothingIsComparedWithoutAWindowToShowTheResultsOver()
    {
        await GivenTwoFoldersAsync();
        VVO.UI.Dialogs.Owner = () => null;

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Empty(Opened);
    }

    [AvaloniaFact]
    public async Task AComparisonThatCannotBeStartedIsReported()
    {
        await GivenTwoFoldersAsync();
        FailDialogs();

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    #endregion

    #region The results window

    [AvaloniaFact]
    public async Task TheResultsGridMarksEachRowWithWhatBecameOfIt()
    {
        await GivenTwoFoldersAsync();

        AnswerDialogs<CompareTargetDialogViewModel>(dialog =>
            dialog.SelectedFolder = dialog.Folders.Single(choice => choice.Name == "Docs"));

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));
        Pump();

        var view = Results!;
        var grid = view.GetVisualDescendants().OfType<DataGrid>().Single();
        var statuses = ((CompareResultsViewModel)view.DataContext!).Rows
            .Select(row => row.StatusName)
            .Distinct()
            .ToList();

        Assert.NotEmpty(statuses);
        Assert.NotNull(grid);

        CloseResults();
    }

    [AvaloniaFact]
    public async Task TheResultsWindowClosesOnItsOwnButton()
    {
        await GivenTwoFoldersAsync();

        AnswerDialogs<CompareTargetDialogViewModel>(dialog =>
            dialog.SelectedFolder = dialog.Folders.Single(choice => choice.Name == "Docs"));

        await Sidebar.CompareCommand.ExecuteAsync(Folder("Code"));
        Pump();

        var view = Results!;
        var close = view.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel);
        close.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));

        Assert.False(view.IsVisible);
    }

    #endregion
}
