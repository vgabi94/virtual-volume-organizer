using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using VVO.UI;

namespace VVO.UiTests;

/// <summary>
/// A listed file's icon is coloured by its kind through styles alone, so a theme that names no
/// colour for a kind still draws it, in the plain icon colour.
/// </summary>
public class FileTypeColourTests : UiTestBase
{
    // Every icon FileIcons hands out, folders included
    private static readonly string[] Kinds =
    [
        "FolderSolid", "FileImageSolid", "FileAudioSolid", "FileVideoSolid", "FileZipperSolid",
        "FilePdfSolid", "FileWordSolid", "FileExcelSolid", "FileCsvSolid", "FilePowerpointSolid",
        "FileLinesSolid", "FileCodeSolid", "FileCog", "FileEarmarkBinaryFill", "CompactDiscSolid",
        "DatabaseSolid", "FileSolid"
    ];

    private PathIcon Drawn(string kind, ThemeVariant variant)
    {
        var icon = new PathIcon { Tag = kind, Classes = { "FileTypeIcon" } };
        Shell.RequestedThemeVariant = variant;
        Shell.Content = icon;
        Shell.UpdateLayout();

        return icon;
    }

    private static Color IconBrushIn(ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource("IconBrush", variant, out var found));

        return Assert.IsAssignableFrom<ISolidColorBrush>(found).Color;
    }

    [AvaloniaTheory]
    [InlineData("Dark")]
    [InlineData("Light")]
    public void APlainThemeDrawsEveryKindInTheIconColour(string variantName)
    {
        var variant = variantName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;

        Assert.All(Kinds, kind =>
            Assert.Equal(IconBrushIn(variant), Assert.IsAssignableFrom<ISolidColorBrush>(Drawn(kind, variant).Foreground).Color));
    }

    [AvaloniaFact]
    public void AKindGivenAColourIsDrawnInIt()
    {
        Shell.Resources["FileImageSolidBrush"] = new SolidColorBrush(Colors.HotPink);

        var image = Drawn("FileImageSolid", ThemeVariant.Dark);
        Assert.Equal(Colors.HotPink, Assert.IsAssignableFrom<ISolidColorBrush>(image.Foreground).Color);

        var other = Drawn("FileZipperSolid", ThemeVariant.Dark);
        Assert.Equal(IconBrushIn(ThemeVariant.Dark), Assert.IsAssignableFrom<ISolidColorBrush>(other.Foreground).Color);
    }

    // Every icon FileIcons can return has a style, so none is left out of a theme's colouring
    [AvaloniaFact]
    public void EveryIconFileIconsHandsOutIsAKind()
    {
        string[] samples = ["", "jpg", "mp3", "mp4", "zip", "pdf", "docx", "xlsx", "csv", "pptx", "txt",
            "cs", "json", "exe", "iso", "db", "xyz"];

        Assert.Equal(
            Kinds.Order(),
            samples.Select(extension => FileIcons.KeyFor(extension, isFolder: extension == "")).Distinct().Order());
    }
}
