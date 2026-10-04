using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;
using VVO.UI.Views;

namespace VVO.UiTests;

/// <summary>
/// What the Modern explorer draws beyond the Classic one: the open volume's icon and colour
/// heading the path, and an extension column that is a template rather than plain text.
/// </summary>
public class ExplorerLookTests : UiTestBase
{
    private static Color ColourOf(IBrush? brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;

    private static void AssertShowsVolumeOf(VolumeExplorerViewModel explorer, VirtualVolumeNode node)
    {
        Assert.Same(node.IconData, explorer.VolumeIconData);
        Assert.Equal(node.IsIconFlipped, explorer.IsVolumeIconFlipped);
        Assert.Equal(ColourOf(node.IconBrush), ColourOf(explorer.VolumeIconBrush));
    }

    private async Task<(SidebarViewModel Sidebar, VolumeExplorerViewModel Explorer, VirtualVolumeNode Node)> OpenedAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("BluRay", "HardDrive", "#FF3366");
        await AddFolderAsync(volume.Id);

        var sidebar = NewSidebar();
        var explorer = NewExplorer();
        await sidebar.LoadAsync();

        var node = sidebar.VirtualVolumes.Single();
        node.SelectedFolder = node.Folders.Single();
        await Until(() => explorer.VolumeIconData != null, "the folder to open");

        return (sidebar, explorer, node);
    }

    [AvaloniaFact]
    public async Task TheOpenVolumesIconAndColourAreTheSidebars()
    {
        var (_, explorer, node) = await OpenedAsync();

        AssertShowsVolumeOf(explorer, node);
        Assert.True(explorer.IsVolumeIconFlipped);
    }

    [AvaloniaFact]
    public async Task EditingTheVolumesLookIsTakenUp()
    {
        var (sidebar, explorer, node) = await OpenedAsync();
        AnswerDialogs<VirtualVolumeDialogViewModel>(dialog =>
        {
            dialog.SelectedIcon = dialog.Icons.Single(icon => icon.Key == "CompactDisc");
            dialog.SelectedColor = Color.Parse("#2BB3A3");
        });

        await sidebar.EditVirtualVolumeCommand.ExecuteAsync(node);

        AssertShowsVolumeOf(explorer, node);
        Assert.False(explorer.IsVolumeIconFlipped);
        Assert.Equal(Color.Parse("#2BB3A3"), ColourOf(explorer.VolumeIconBrush));
    }

    [AvaloniaFact]
    public async Task ASearchListingSeveralFoldersShowsNoVolumeUntilItIsCalledOff()
    {
        var (_, explorer, node) = await OpenedAsync();
        var folder = node.Folders.Single();
        SearchScope[] scopes = [new(folder.Entry.TreeId, folder.Title, node.Name, folder.Entry.Path, node.Record)];

        await explorer.SearchAllAsync(new SearchAllMessage("readme", scopes));

        Assert.True(explorer.IsFlatMode);
        Assert.Null(explorer.VolumeIconData);
        Assert.Null(explorer.VolumeIconBrush);

        await explorer.SearchAllAsync(new SearchAllMessage("", scopes));

        AssertShowsVolumeOf(explorer, node);
    }

    #region Sorting

    private async Task<DataGrid> ListedAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var rootId = Guid.NewGuid();
        FileRecord File(string name, long size) =>
            new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = rootId, IsFolder = false, Name = name, Size = size };
        var entry = await Volumes.AddFolderAsync(volume.Id,
            new RootFolderMetadata { Id = Guid.NewGuid(), TreeId = rootId, Path = @"D:\Code", LastScanned = DateTime.UtcNow },
            [
                new() { Id = rootId, RootFolderId = rootId, ParentId = null, IsFolder = true, Name = "Code", Size = 2410 },
                File("b.zip", 300), File("a.txt", 10), File("c.cs", 2000), File("d.md", 100)
            ]);

        var explorer = NewExplorer();
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        var window = new Window { Content = new VolumeExplorerView { DataContext = explorer }, Width = 900, Height = 600 };
        window.Show();
        Pump();

        return window.GetVisualDescendants().OfType<DataGrid>().Single();
    }

    private static List<string> SortedBy(DataGrid grid, string header)
    {
        grid.Columns.Single(column => (string?)column.Header == header).Sort(ListSortDirection.Ascending);
        Pump();

        return grid.CollectionView.Cast<FileItem>().Select(item => item.Name).ToList();
    }

    [AvaloniaFact]
    public async Task SortingByExtensionOrdersByTheExtension()
    {
        var grid = await ListedAsync();

        Assert.Equal(["c", "d", "a", "b"], SortedBy(grid, "Extension"));
    }

    [AvaloniaFact]
    public async Task SortingBySizeOrdersByTheBytesRatherThanTheText()
    {
        var grid = await ListedAsync();

        Assert.Equal(["a", "d", "b", "c"], SortedBy(grid, "Size"));
    }

    #endregion
}
