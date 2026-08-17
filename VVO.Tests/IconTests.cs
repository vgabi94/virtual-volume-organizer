using VVO.UI;

namespace VVO.Tests;

/// <summary>
/// The icon helpers pick a resource key and say whether it has to be flipped. Only that choice
/// is covered: resolving a key to a geometry needs a running application.
/// </summary>
public class IconTests
{
    [Theory]
    [InlineData("jpg", "FileImageSolid")]
    [InlineData("JPEG", "FileImageSolid")]
    [InlineData(".png", "FileImageSolid")]
    [InlineData("mp3", "FileAudioSolid")]
    [InlineData("mkv", "FileVideoSolid")]
    [InlineData("7z", "FileZipperSolid")]
    [InlineData("pdf", "FilePdfSolid")]
    [InlineData("docx", "FileWordSolid")]
    [InlineData("xlsx", "FileExcelSolid")]
    [InlineData("csv", "FileCsvSolid")]
    [InlineData("pptx", "FilePowerpointSolid")]
    [InlineData("md", "FileLinesSolid")]
    [InlineData("cs", "FileCodeSolid")]
    [InlineData("json", "FileCog")]
    [InlineData("exe", "FileEarmarkBinaryFill")]
    [InlineData("iso", "CompactDiscSolid")]
    [InlineData("sqlite", "DatabaseSolid")]
    public void FileIconsMapAnExtensionToItsKind(string extension, string expected)
    {
        Assert.Equal(expected, FileIcons.KeyFor(extension, isFolder: false));
    }

    [Theory]
    [InlineData("")]
    [InlineData("qqq")]
    [InlineData(".gitignore")]
    public void FileIconsFallBackToThePlainFileIcon(string extension)
    {
        Assert.Equal("FileSolid", FileIcons.KeyFor(extension, isFolder: false));
    }

    // The extension is beside the point for a folder: one named 'archive.zip' is still a folder
    [Fact]
    public void FileIconsIgnoreTheExtensionOfAFolder()
    {
        Assert.Equal("FolderSolid", FileIcons.KeyFor("zip", isFolder: true));
        Assert.Equal("FolderSolid", FileIcons.KeyFor(string.Empty, isFolder: true));
    }

    [Fact]
    public void TheExecutableIconIsTheOneDrawnUpsideDown()
    {
        Assert.True(FileIcons.IsFlipped(FileIcons.KeyFor("exe", isFolder: false)));

        Assert.False(FileIcons.IsFlipped(FileIcons.KeyFor("txt", isFolder: false)));
        Assert.False(FileIcons.IsFlipped(FileIcons.KeyFor(string.Empty, isFolder: true)));
    }

    [Fact]
    public void FolderIconsDefaultToOneOfTheirOwnKeys()
    {
        Assert.Contains(FolderIcons.Default, FolderIcons.Keys);
        Assert.Equal("Folder", FolderIcons.Default);
    }

    [Fact]
    public void VirtualVolumeIconsDefaultToOneOfTheirOwnKeys()
    {
        Assert.Contains(VirtualVolumeIcons.Default, VirtualVolumeIcons.Keys);
    }

    [Fact]
    public void IconKeysAreNotRepeated()
    {
        Assert.Equal(FolderIcons.Keys.Count, FolderIcons.Keys.Distinct().Count());
        Assert.Equal(VirtualVolumeIcons.Keys.Count, VirtualVolumeIcons.Keys.Distinct().Count());
    }

    // A flipped key that no longer names an offered icon flips nothing, and nothing says so
    [Fact]
    public void EveryFlippedVirtualVolumeIconIsStillOffered()
    {
        var flipped = VirtualVolumeIcons.Keys.Where(VirtualVolumeIcons.IsFlipped).Order().ToList();

        Assert.Equal(["Archive", "HardDrive", "UsbDriveFill"], flipped);
    }

    [Theory]
    [InlineData("HardDrive")]
    [InlineData("UsbDriveFill")]
    [InlineData("Archive")]
    public void TheVirtualVolumeIconsDrawnUpsideDownAreFlipped(string key)
    {
        Assert.Contains(key, VirtualVolumeIcons.Keys);
        Assert.True(VirtualVolumeIcons.IsFlipped(key));
    }

    [Theory]
    [InlineData("CompactDisc")]
    [InlineData("FloppyDisk")]
    [InlineData("Smartphone")]
    public void TheRestOfTheVirtualVolumeIconsAreLeftAlone(string key)
    {
        Assert.False(VirtualVolumeIcons.IsFlipped(key));
    }

    [Fact]
    public void NoFolderIconIsFlipped()
    {
        Assert.All(FolderIcons.Keys, key => Assert.False(FolderIcons.IsFlipped(key)));
    }

    [Fact]
    public void AnAbsentIconKeyIsNotFlipped()
    {
        Assert.False(FolderIcons.IsFlipped(null));
        Assert.False(VirtualVolumeIcons.IsFlipped(null));
    }
}
