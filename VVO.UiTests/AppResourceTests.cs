using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using VVO.UI;

namespace VVO.UiTests;

/// <summary>
/// Every icon the code asks for by key has to be in the resource dictionary. Without a running
/// application a lookup quietly hands back null, so this is only answerable here — and a
/// missing or misspelled key is otherwise invisible until the icon fails to draw.
/// </summary>
public class AppResourceTests
{
    [AvaloniaFact]
    public void TheApplicationIsUpWithItsOwnResources()
    {
        Assert.NotNull(Application.Current);
        Assert.True(Application.Current!.TryFindResource("Folder", out _));
    }

    [AvaloniaFact]
    public void EveryFolderIconResolves()
    {
        Assert.All(FolderIcons.Keys, key => Assert.NotNull(FolderIcons.Lookup(key)));
    }

    [AvaloniaFact]
    public void EveryVirtualVolumeIconResolves()
    {
        Assert.All(VirtualVolumeIcons.Keys, key => Assert.NotNull(VirtualVolumeIcons.Lookup(key)));
    }

    [AvaloniaTheory]
    [InlineData("jpg", false)]
    [InlineData("mp3", false)]
    [InlineData("mkv", false)]
    [InlineData("zip", false)]
    [InlineData("pdf", false)]
    [InlineData("docx", false)]
    [InlineData("xlsx", false)]
    [InlineData("csv", false)]
    [InlineData("pptx", false)]
    [InlineData("txt", false)]
    [InlineData("cs", false)]
    [InlineData("json", false)]
    [InlineData("exe", false)]
    [InlineData("iso", false)]
    [InlineData("sqlite", false)]
    [InlineData("qqq", false)]
    [InlineData("", true)]
    public void EveryFileIconResolves(string extension, bool isFolder)
    {
        var key = FileIcons.KeyFor(extension, isFolder);

        Assert.NotNull(FileIcons.Lookup(key));
    }

    [AvaloniaFact]
    public void AnIconKeyThatIsNotThereResolvesToNothingRatherThanThrowing()
    {
        Assert.Null(FileIcons.Lookup("NoSuchIconKey"));
    }

    // A blank key is what a folder carries before the user picks an icon
    [AvaloniaFact]
    public void ABlankIconKeyFallsBackToTheDefaultIcon()
    {
        Assert.NotNull(FolderIcons.Lookup(null));
        Assert.NotNull(FolderIcons.Lookup("   "));
        Assert.NotNull(VirtualVolumeIcons.Lookup(null));
    }

    [AvaloniaFact]
    public void TheIconsNamedByTheMenusAndToolbarsAreAllThere()
    {
        string[] keys =
        [
            "FilePlus", "FileOpenRound", "SaveAs", "DiskPlus", "DiskRemove", "PencilBox",
            "ScanSearch", "ChevronRight", "ChevronDown", "ArrowExpandVertical",
            "ArrowCollapseVertical", "Undo", "Redo", "ContentCopy", "ContentCut",
            "ContentPaste", "ContentDuplicate", "Delete", "FileCompare", "Alert",
            "DatabaseCheck", "ArrowBack", "MagnifyingGlassLight",
            "ArrowsCounterClockwiseFill"
        ];

        Assert.All(keys, key =>
        {
            Assert.True(Application.Current!.TryFindResource(key, out var resource), $"missing icon '{key}'");
            Assert.IsAssignableFrom<Geometry>(resource);
        });
    }

    // The default shade comes from the theme, which only exists with an application up
    [AvaloniaFact]
    public void TheDefaultIconShadeComesFromTheTheme()
    {
        Assert.NotEqual(Colors.Gray, IconColors.Default);
    }

    [AvaloniaFact]
    public void AStoredColourStillWinsOverTheThemeShade()
    {
        var brush = Assert.IsType<SolidColorBrush>(IconColors.Brush("#FF3366"));

        Assert.Equal(Color.Parse("#FF3366"), brush.Color);
    }

    [AvaloniaFact]
    public void TheSizesTheIconsAreDrawnAtAreDefined()
    {
        foreach (var key in new[] { "IconMedium", "IconChevron", "IconVolume" })
        {
            Assert.True(Application.Current!.TryFindResource(key, out var resource), $"missing '{key}'");
            Assert.IsType<double>(resource);
        }
    }
}
