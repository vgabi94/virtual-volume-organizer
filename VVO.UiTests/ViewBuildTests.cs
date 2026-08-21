using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using VVO.Core.Models;
using VVO.UI;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// Every view is built, given its view model and laid out. Compiled XAML is only parsed when a
/// view is actually instantiated, so a mismatched tag or a binding to a property that no longer
/// exists is invisible until then — which is how two broken icon entries reached the app.
/// </summary>
public class ViewBuildTests : UiTestBase
{
    private static T Laid<T>(T control) where T : Window
    {
        control.Show();
        Pump();
        return control;
    }

    private Window Hosted(Control view)
    {
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Pump();
        return window;
    }

    private static IEnumerable<TextBlock> TextOf(Visual root) =>
        root.GetVisualDescendants().OfType<TextBlock>();

    #region The panes

    [AvaloniaFact]
    public async Task TheMainWindowBuildsWithEveryPaneInIt()
    {
        var sidebar = NewSidebar();
        var window = Laid(new MainWindowView { DataContext = NewMainWindow(sidebar) });

        Assert.NotNull(window.GetVisualDescendants().OfType<Menu>().FirstOrDefault());
        Assert.NotNull(window.GetVisualDescendants().OfType<GridSplitter>().FirstOrDefault());

        await Task.CompletedTask;
        window.Close();
    }

    [AvaloniaFact]
    public void TheMenuBarCarriesEveryTopLevelHeading()
    {
        var window = Laid(new MainWindowView { DataContext = NewMainWindow() });

        var menu = window.GetVisualDescendants().OfType<Menu>().Single();
        var headings = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToList();

        Assert.Equal(["_File", "_Edit", "_View", "_Volume", "_Tools", "_Help"], headings);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheSidebarShowsTheVolumesAndFoldersItIsGiven()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await AddFolderAsync(volume.Id, "Code");

        var sidebar = NewSidebar();
        await sidebar.LoadAsync();

        var window = Hosted(new SidebarView { DataContext = sidebar });
        var text = TextOf(window).Select(block => block.Text).ToList();

        Assert.Contains("test", text);
        Assert.Contains("Code", text);
        window.Close();
    }

    [AvaloniaFact]
    public async Task TheVolumeExplorerShowsTheContentsOfTheSelectedFolder()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddFolderAsync(volume.Id, "Code");

        var explorer = NewExplorer();
        await explorer.ShowFolderAsync(new VVO.UI.Messages.FolderSelectedMessage(entry.TreeId, "Code", "test"));

        var window = Hosted(new VolumeExplorerView { DataContext = explorer });
        var text = TextOf(window).Select(block => block.Text).ToList();

        // The volume heads the path the way a drive letter does
        Assert.Contains("test:", text);
        Assert.Contains("Code", text);
        Assert.Contains("AdventOfCode", text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheStartPageBuildsWithNoRecentDatabases()
    {
        var window = Hosted(new StartPageView { DataContext = NewStartPage() });

        Assert.NotEmpty(window.GetVisualDescendants().OfType<Button>());
        window.Close();
    }

    [AvaloniaFact]
    public void TheStartPageListsTheDatabasesItRemembers()
    {
        Settings.AddRecentFile(@"D:\Temp\Discs.vvo");
        var window = Hosted(new StartPageView { DataContext = NewStartPage() });

        Assert.Contains("Discs", TextOf(window).Select(block => block.Text));
        window.Close();
    }

    #endregion

    #region The dialogs

    [AvaloniaFact]
    public void TheAboutDialogNamesTheApplicationAndItsVersion()
    {
        var viewModel = new AboutDialogViewModel();
        var window = Laid(new AboutDialogView { DataContext = viewModel });

        var text = TextOf(window).Select(block => block.Text).ToList();
        Assert.Contains("Virtual Volume Organizer", text);
        Assert.Contains($"Version {viewModel.Version}", text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheShortcutsDialogListsEveryGesture()
    {
        var viewModel = new ShortcutsDialogViewModel();
        var window = Laid(new ShortcutsDialogView { DataContext = viewModel });

        var text = TextOf(window).Select(block => block.Text).ToList();
        Assert.All(viewModel.Shortcuts, row => Assert.Contains(row.Gesture, text));
        window.Close();
    }

    [AvaloniaFact]
    public void TheOptionsDialogShowsWhatIsStored()
    {
        Settings.SetDarkTheme(false);
        Settings.SetShowFolderDetailsAlways(true);
        Settings.SetScanHiddenAndSystem(false);
        var window = Laid(new OptionsDialogView { DataContext = new OptionsDialogViewModel(Settings) });

        // One box per flag, in the order the dialog lists them
        var checks = window.GetVisualDescendants().OfType<CheckBox>().ToList();
        Assert.Equal(3, checks.Count);
        Assert.False(checks[0].IsChecked);
        Assert.True(checks[1].IsChecked);
        Assert.False(checks[2].IsChecked);
        window.Close();
    }

    [AvaloniaFact]
    public void TheDeleteConfirmationNamesWhatIsGoing()
    {
        var window = Laid(new ConfirmDeleteDialogView
        {
            DataContext = ConfirmDeleteDialogViewModel.ForFolder("Code")
        });

        var text = TextOf(window).Select(block => block.Text).ToList();
        Assert.Contains("Code", text);
        Assert.Contains(text, line => line?.Contains("cannot be undone") == true);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNameDialogOpensOnTheNameItWasGiven()
    {
        var window = Laid(new NameDialogView
        {
            DataContext = new NameDialogViewModel("Name for the duplicate", "Duplicate", "Code")
        });

        var box = window.GetVisualDescendants().OfType<TextBox>().First();
        Assert.Equal("Code", box.Text);
        window.Close();
    }

    [AvaloniaFact]
    public void TheNewDatabaseDialogBuilds()
    {
        var window = Laid(new NewDatabaseDialogView { DataContext = new NewDatabaseDialogViewModel() });

        Assert.NotEmpty(window.GetVisualDescendants().OfType<TextBox>());
        window.Close();
    }

    [AvaloniaFact]
    public void TheVirtualVolumeDialogOffersEveryIcon()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();
        var window = Laid(new VirtualVolumeDialogView { DataContext = viewModel });

        var list = window.GetVisualDescendants().OfType<ItemsControl>()
            .First(control => control.ItemsSource == viewModel.Icons);

        Assert.Equal(VirtualVolumeIcons.Keys.Count, viewModel.Icons.Count);
        Assert.NotNull(list);
        window.Close();
    }

    [AvaloniaFact]
    public void TheFolderDialogOpensOnTheFolderItIsEditing()
    {
        var entry = new RootFolderMetadata { Id = Guid.NewGuid(), Path = @"D:\Code", Label = "My Code" };
        var window = Laid(new FolderDialogView { DataContext = new FolderDialogViewModel(entry, "Code") });

        var boxes = window.GetVisualDescendants().OfType<TextBox>().Select(box => box.Text).ToList();
        Assert.Contains("My Code", boxes);
        window.Close();
    }

    [AvaloniaFact]
    public void TheCompareTargetDialogListsTheFoldersOnOffer()
    {
        var choices = new List<FolderChoice> { new(Guid.NewGuid(), "Code", @"D:\Code") };
        var window = Laid(new CompareTargetDialogView
        {
            DataContext = new CompareTargetDialogViewModel(choices)
        });

        var list = window.GetVisualDescendants().OfType<ListBox>().Single();
        Assert.Equal(choices, list.ItemsSource);
        Assert.Contains("Code", TextOf(window).Select(block => block.Text));
        window.Close();
    }

    [AvaloniaFact]
    public void TheCompareResultsWindowShowsTheDifferencesItWasGiven()
    {
        var results = new List<ComparisonResult>
        {
            new()
            {
                RelativePath = @"sub\gone.txt",
                Status = ComparisonStatus.Removed,
                Left = new FileRecord { Id = Guid.NewGuid(), Name = "gone.txt", Size = 10 }
            }
        };

        var viewModel = new CompareResultsViewModel("Source", @"D:\Source", "Target", @"E:\Target", results);
        var window = Laid(new CompareResultsView { DataContext = viewModel });

        Assert.True(viewModel.HasDifferences);
        Assert.NotEmpty(window.GetVisualDescendants().OfType<DataGrid>());
        window.Close();
    }

    [AvaloniaFact]
    public void TheCompareResultsWindowSaysSoWhenThereAreNoDifferences()
    {
        var viewModel = new CompareResultsViewModel("Source", @"D:\Source", "Target", @"E:\Target", []);
        var window = Laid(new CompareResultsView { DataContext = viewModel });

        Assert.Contains("No differences found.", TextOf(window).Select(block => block.Text));
        window.Close();
    }

    // Shown to have an update approved, the same window offers a button to approve it with and
    // turns its Close into the way of turning the update down
    [AvaloniaFact]
    public void TheCompareResultsWindowAsksForTheUpdateItIsProposing()
    {
        var viewModel = new CompareResultsViewModel(
            "Source", @"D:\Source", "Target", @"E:\Target", [], isUpdate: true);

        var window = Laid(new CompareResultsView { DataContext = viewModel });
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();

        Assert.Equal("Update Folder", window.Title);
        Assert.Contains(buttons, button => Equals(button.Content, "Update") && button.IsVisible);
        Assert.Contains(buttons, button => Equals(button.Content, "Cancel") && button.IsCancel);
        Assert.Contains(viewModel.Proposal, TextOf(window).Select(block => block.Text));

        window.Close();
    }

    [AvaloniaFact]
    public void TheCompareResultsWindowOnlyOffersToCloseWhenItIsMerelyReporting()
    {
        var viewModel = new CompareResultsViewModel("Source", @"D:\Source", "Target", @"E:\Target", []);

        var window = Laid(new CompareResultsView { DataContext = viewModel });
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();

        Assert.Equal("Comparison Results", window.Title);
        Assert.Contains(buttons, button => Equals(button.Content, "Close") && button.IsCancel);
        Assert.DoesNotContain(buttons, button => Equals(button.Content, "Update") && button.IsVisible);

        window.Close();
    }

    #endregion

    #region Dialog results

    [AvaloniaFact]
    public async Task ConfirmingADialogHandsBackTrue()
    {
        var dialog = new ConfirmDeleteDialogView { DataContext = ConfirmDeleteDialogViewModel.ForFolder("Code") };
        var pending = dialog.ShowDialog<bool>(Shell);
        Pump();

        dialog.Close(true);
        Pump();

        Assert.True(await pending);
    }

    [AvaloniaFact]
    public async Task CancellingADialogHandsBackFalse()
    {
        var dialog = new NameDialogView { DataContext = new NameDialogViewModel("Name", "Duplicate") };
        var pending = dialog.ShowDialog<bool>(Shell);
        Pump();

        dialog.Close(false);
        Pump();

        Assert.False(await pending);
    }

    #endregion

    #region View resolution

    [AvaloniaFact]
    public void AViewModelIsMatchedToItsView()
    {
        var locator = new ViewLocator();
        var built = locator.Build(NewStartPage());

        Assert.IsType<StartPageView>(built);
    }

    // A view model whose view has not been written yet says so in place of the view
    [AvaloniaFact]
    public void AViewModelWithNoViewSaysSoInsteadOfBuilding()
    {
        var built = new ViewLocator().Build(new OrphanViewModel(Undo));

        var placeholder = Assert.IsType<TextBlock>(built);
        Assert.StartsWith("Not Found:", placeholder.Text);
    }

    [AvaloniaFact]
    public void NothingIsBuiltForNothing()
    {
        Assert.Null(new ViewLocator().Build(null));
    }

    [AvaloniaFact]
    public void OnlyViewModelsAreMatched()
    {
        Assert.True(new ViewLocator().Match(NewStartPage()));
        Assert.False(new ViewLocator().Match("just a string"));
        Assert.False(new ViewLocator().Match(null));
    }

    private sealed class OrphanViewModel(VVO.Core.Services.IUndoService undo) : ViewModelBase(undo);

    #endregion
}
