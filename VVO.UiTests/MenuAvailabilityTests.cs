using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// Which menu entries answer to being clicked. The start page stands in front of a catalogue, so
/// everything that acts on one is dead until one is open — and has to come alive once it is.
/// </summary>
public class MenuAvailabilityTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }
    private MainWindowViewModel MainWindow { get; }

    public MenuAvailabilityTests()
    {
        Sidebar = NewSidebar();
        MainWindow = NewMainWindow(Sidebar);
    }

    /// <summary>
    /// Every entry that acts on a catalogue, asked with the parameter the menu bar binds to it.
    /// Rebuilt on each call so it reads the state as it now stands.
    /// </summary>
    private Dictionary<string, bool> ActingOnACatalogue() => new()
    {
        ["File > Save a Copy"] = Sidebar.SaveCopyCommand.CanExecute(Shell),
        ["File > Export to JSON"] = Sidebar.ExportCommand.CanExecute(Shell),
        ["Edit > Cut"] = Sidebar.CutFolderCommand.CanExecute(Sidebar.SelectedFolder),
        ["Edit > Copy"] = Sidebar.CopyFolderCommand.CanExecute(Sidebar.SelectedFolder),
        ["Edit > Duplicate"] = Sidebar.DuplicateFolderCommand.CanExecute(Sidebar.SelectedFolder),
        ["Edit > Delete"] = Sidebar.DeleteFolderCommand.CanExecute(Sidebar.SelectedFolder),
        ["Edit > Find"] = MainWindow.FindCommand.CanExecute(null),
        ["View > Expand All Volumes"] = Sidebar.ExpandAllVirtualVolumesCommand.CanExecute(null),
        ["View > Collapse All Volumes"] = Sidebar.CollapseAllVirtualVolumesCommand.CanExecute(null),
        ["Volume > New Virtual Volume"] = Sidebar.NewVirtualVolumeCommand.CanExecute(null),
        ["Volume > Edit Virtual Volume"] = Sidebar.EditVirtualVolumeCommand.CanExecute(Sidebar.SelectedVirtualVolume),
        ["Volume > Delete Virtual Volume"] = Sidebar.DeleteVirtualVolumeCommand.CanExecute(Sidebar.SelectedVirtualVolume),
        ["Volume > Add Folder"] = MainWindow.AddFolderCommand.CanExecute(Shell),
        ["Volume > Edit Folder"] = Sidebar.EditFolderCommand.CanExecute(Sidebar.SelectedFolder),
        ["Volume > Compare Folders"] = Sidebar.CompareCommand.CanExecute(Sidebar.SelectedFolder),
        ["Volume > Update Folder"] = Sidebar.UpdateFolderCommand.CanExecute(Sidebar.SelectedFolder),
        ["Tools > Shrink Database"] = MainWindow.ShrinkDatabaseCommand.CanExecute(null)
    };

    /// <summary>Entries that are a way in, or that say something about the application itself.</summary>
    private Dictionary<string, bool> StandingOnTheirOwn() => new()
    {
        ["File > New Database"] = Sidebar.NewDatabaseCommand.CanExecute(Shell),
        ["File > Open Database"] = Sidebar.OpenDatabaseCommand.CanExecute(Shell),
        ["File > Import from JSON"] = Sidebar.ImportCommand.CanExecute(Shell),
        ["File > Exit"] = MainWindow.ExitCommand.CanExecute(null),
        ["Tools > Options"] = MainWindow.ShowOptionsCommand.CanExecute(Shell),
        ["Help > Keyboard Shortcuts"] = MainWindow.ShowShortcutsCommand.CanExecute(Shell),
        ["Help > License"] = MainWindow.ShowLicenseCommand.CanExecute(Shell),
        ["Help > About"] = MainWindow.ShowAboutCommand.CanExecute(Shell)
    };

    private static void AssertAll(Dictionary<string, bool> state, bool expected)
    {
        var wrong = state.Where(entry => entry.Value != expected).Select(entry => entry.Key).ToList();

        Assert.True(wrong.Count == 0,
            $"expected {(expected ? "enabled" : "disabled")}: {string.Join(", ", wrong)}");
    }

    /// <summary>A catalogue opened the way leaving the start page opens one.</summary>
    private async Task OpenACatalogueAsync(bool withAFolder = true)
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        if (withAFolder)
        {
            await AddFolderAsync(volume.Id, "Code");
        }

        await Sidebar.LoadAsync();
        MainWindow.Receive(new DatabaseReady());
    }

    #region On the start page

    [AvaloniaFact]
    public void NothingThatActsOnACatalogueAnswersWhileTheStartPageIsUp()
    {
        Assert.True(MainWindow.IsStartPageVisible);

        AssertAll(ActingOnACatalogue(), expected: false);
    }

    // Already dead of their own accord: nothing has been done yet to undo
    [AvaloniaFact]
    public void UndoAndRedoAreDeadOnTheStartPage()
    {
        Assert.False(MainWindow.UndoCommand.CanExecute(null));
        Assert.False(MainWindow.RedoCommand.CanExecute(null));
    }

    // Likewise: there is nothing on the clipboard to paste
    [AvaloniaFact]
    public void PastingIsDeadOnTheStartPage()
    {
        Assert.False(Sidebar.PasteBesideFolderCommand.CanExecute(null));
        Assert.False(Sidebar.PasteIntoVirtualVolumeCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public void TheWaysIntoACatalogueAndTheHelpEntriesStayOpen()
    {
        AssertAll(StandingOnTheirOwn(), expected: true);
    }

    #endregion

    #region Once a catalogue is open

    [AvaloniaFact]
    public async Task EverythingComesAliveOnceACatalogueWithAFolderIsOpen()
    {
        await OpenACatalogueAsync();

        AssertAll(ActingOnACatalogue(), expected: true);
    }

    // A catalogue can be open and still hold nothing to act on, which the folder entries answer
    // for separately from the catalogue itself
    [AvaloniaFact]
    public async Task AFolderEntryStaysDeadUntilThereIsAFolderToActOn()
    {
        await OpenACatalogueAsync(withAFolder: false);

        Assert.Null(Sidebar.SelectedFolder);
        Assert.False(Sidebar.CompareCommand.CanExecute(Sidebar.SelectedFolder));
        Assert.False(Sidebar.DeleteFolderCommand.CanExecute(Sidebar.SelectedFolder));
        Assert.False(Sidebar.UpdateFolderCommand.CanExecute(Sidebar.SelectedFolder));

        Assert.True(Sidebar.NewVirtualVolumeCommand.CanExecute(null));
        Assert.True(MainWindow.ShrinkDatabaseCommand.CanExecute(null));
        Assert.True(MainWindow.AddFolderCommand.CanExecute(Shell));
    }

    // The sidebar menus hand over the row they were opened on, which stands in for a selection
    // that was never made
    [AvaloniaFact]
    public async Task ARowHandedOverDirectlyAnswersWithNothingSelected()
    {
        await OpenACatalogueAsync();

        var folder = Sidebar.VirtualVolumes.SelectMany(node => node.Folders).Single();
        var volume = Sidebar.VirtualVolumes.Single();

        Sidebar.SelectedFolder = null;
        Sidebar.SelectedVirtualVolume = null;

        Assert.True(Sidebar.CompareCommand.CanExecute(folder));
        Assert.True(Sidebar.DeleteFolderCommand.CanExecute(folder));
        Assert.True(Sidebar.EditVirtualVolumeCommand.CanExecute(volume));
        Assert.False(Sidebar.CompareCommand.CanExecute(null));
    }

    [AvaloniaFact]
    public async Task LosingTheSelectionPutsTheFolderEntriesBackToSleep()
    {
        await OpenACatalogueAsync();
        Assert.True(Sidebar.CompareCommand.CanExecute(Sidebar.SelectedFolder));

        Sidebar.SelectedFolder = null;

        Assert.False(Sidebar.CompareCommand.CanExecute(Sidebar.SelectedFolder));
    }

    #endregion

    #region The menu bar itself

    // A command bound to a name that no longer exists resolves to null, which reads on screen as
    // an entry greyed out for no reason
    [AvaloniaFact]
    public async Task EveryMenuEntryIsBoundToACommandThatExists()
    {
        await OpenACatalogueAsync();

        var window = new MainWindowView { DataContext = MainWindow };
        window.Show();
        Pump();

        var entries = window.GetVisualDescendants().OfType<Menu>().Single()
            .Items.OfType<MenuItem>()
            .SelectMany(top => top.Items.OfType<MenuItem>())
            .ToList();

        Assert.NotEmpty(entries);

        var unbound = entries
            .Where(entry => entry.Command == null && entry.ToggleType == MenuItemToggleType.None)
            .Select(entry => entry.Header?.ToString())
            .ToList();

        Assert.True(unbound.Count == 0, $"bound to nothing: {string.Join(", ", unbound)}");

        window.Close();
    }

    #endregion
}
