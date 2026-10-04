using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using VVO.UI;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// The Modern sidebar keeps a volume's Scan, Edit and Delete buttons out of sight, its folder
/// count standing in for them, until the row is pointed at or selected. The Classic one always
/// shows them.
/// </summary>
public class SidebarLookTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; } = null!;

    public SidebarLookTests()
    {
        Sidebar = NewSidebar();
    }

    private async Task<Window> ListedAsync(bool modern)
    {
        await Volumes.CreateVirtualVolumeAsync("first", "HardDrive");
        await Volumes.CreateVirtualVolumeAsync("second", "HardDrive");
        await Sidebar.LoadAsync();

        var window = new Window { Content = new SidebarView { DataContext = Sidebar }, Width = 300, Height = 600 };
        window.Show();
        Layout.Apply(modern);
        Pump();

        return window;
    }

    private VirtualVolumeNode Node(string name) => Sidebar.VirtualVolumes.Single(node => node.Name == name);

    private static ToggleButton Row(Window window, VirtualVolumeNode node) =>
        window.GetVisualDescendants().OfType<ToggleButton>().Single(row => row.DataContext == node);

    private static List<Button> Actions(Window window, VirtualVolumeNode node) =>
        Row(window, node).GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("VolumeAction")).ToList();

    private static TextBlock Count(Window window, VirtualVolumeNode node) =>
        Row(window, node).GetVisualDescendants().OfType<TextBlock>()
            .Single(text => text.Classes.Contains("VolumeCount"));

    private static void AssertActionsShown(Window window, VirtualVolumeNode node, bool shown)
    {
        var actions = Actions(window, node);
        Assert.Equal(3, actions.Count);
        Assert.All(actions, button => Assert.Equal(shown, button.IsEffectivelyVisible));
    }

    // Opening the catalogue selects the first volume, so the second is the one left alone
    [AvaloniaFact]
    public async Task AModernVolumeNeitherSelectedNorPointedAtShowsItsCountInsteadOfItsActions()
    {
        var window = await ListedAsync(modern: true);

        Assert.Same(Node("first"), Sidebar.SelectedVirtualVolume);
        AssertActionsShown(window, Node("second"), shown: false);
        Assert.True(Count(window, Node("second")).IsEffectivelyVisible);
        window.Close();
    }

    [AvaloniaFact]
    public async Task SelectingAModernVolumeShowsItsActionsInPlaceOfItsCount()
    {
        var window = await ListedAsync(modern: true);

        Sidebar.SelectedVirtualVolume = Node("second");
        Pump();

        AssertActionsShown(window, Node("second"), shown: true);
        Assert.False(Count(window, Node("second")).IsEffectivelyVisible);
        AssertActionsShown(window, Node("first"), shown: false);
        window.Close();
    }

    [AvaloniaFact]
    public async Task PointingAtAModernVolumeShowsItsActions()
    {
        var window = await ListedAsync(modern: true);
        var row = Row(window, Node("second"));
        var centre = row.TranslatePoint(new Point(row.Bounds.Width / 2, row.Bounds.Height / 2), window)!.Value;
        AssertActionsShown(window, Node("second"), shown: false);

        window.MouseMove(centre, RawInputModifiers.None);
        Pump();

        AssertActionsShown(window, Node("second"), shown: true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task AClassicVolumeAlwaysShowsItsActions()
    {
        var window = await ListedAsync(modern: false);

        AssertActionsShown(window, Node("first"), shown: true);
        AssertActionsShown(window, Node("second"), shown: true);
        window.Close();
    }

    [AvaloniaFact]
    public async Task OnlyOneVolumeIsMarkedSelected()
    {
        await ListedAsync(modern: true);

        Sidebar.SelectedVirtualVolume = Node("first");
        Sidebar.SelectedVirtualVolume = Node("second");

        Assert.False(Node("first").IsSelected);
        Assert.True(Node("second").IsSelected);

        Sidebar.SelectedVirtualVolume = null;
        Assert.False(Node("second").IsSelected);
    }

    // Scan, Edit and Delete, in the order they sit in the row
    [AvaloniaFact]
    public async Task ASelectedModernVolumesActionsStillRun()
    {
        var window = await ListedAsync(modern: true);
        var node = Node("first");
        Sidebar.SelectedVirtualVolume = node;
        Pump();
        using var scans = new MessageProbe<AddFolderMessage>();

        foreach (var button in Actions(window, node))
        {
            button.Command!.Execute(button.CommandParameter);
        }

        await Until(() => Opened.Count == 2, "the edit and delete dialogs");
        Assert.Equal(node.Id, Assert.Single(scans.All).VirtualVolumeId);
        Assert.IsType<VirtualVolumeDialogView>(Opened[0]);
        Assert.IsType<ConfirmDeleteDialogView>(Opened[1]);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheSidebarKeepsItsWidthLimitsInEitherLayout(bool modern)
    {
        var window = new MainWindowView { DataContext = NewMainWindow() };
        window.Show();
        Layout.Apply(modern);
        Pump();

        var splitter = window.GetVisualDescendants().OfType<GridSplitter>().Single();
        var column = ((Grid)splitter.GetVisualParent()!).ColumnDefinitions[0];
        Assert.Equal(180, column.MinWidth);
        Assert.Equal(600, column.MaxWidth);
        window.Close();
    }
}
