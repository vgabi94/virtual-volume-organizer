using VVO.Cli.Output;
using VVO.Core;

namespace VVO.Cli.Tests;

public class AppearanceTests
{
    [Fact]
    public void EveryVolumeIconTheGuiOffersIsAccepted()
    {
        Assert.All(IconKeys.Volumes, key => Assert.Equal(key, Appearance.VolumeIcon(key)));
    }

    [Fact]
    public void EveryFolderIconTheGuiOffersIsAccepted()
    {
        Assert.All(IconKeys.Folders, key => Assert.Equal(key, Appearance.FolderIcon(key)));
    }

    [Fact]
    public void AnIconIsMatchedWithoutRegardToCaseAndStoredAsTheGuiSpellsIt()
    {
        Assert.Equal("CompactDisc", Appearance.VolumeIcon("compactdisc"));
        Assert.Equal("FolderImage", Appearance.FolderIcon("FOLDERIMAGE"));
    }

    [Fact]
    public void AnUnknownIconIsAUsageErrorListingTheChoices()
    {
        var error = Assert.Throws<CliException>(() => Appearance.VolumeIcon("Rocket"));

        Assert.Equal(ExitCode.Usage, error.ExitCode);
        Assert.Contains("HardDrive", error.Message);
        Assert.Contains("StarFilled", error.Message);
    }

    // The two lists are separate: a volume's icon is not a folder's
    [Fact]
    public void AVolumeIconIsNotAFolderIcon()
    {
        Assert.Throws<CliException>(() => Appearance.FolderIcon("HardDrive"));
        Assert.Throws<CliException>(() => Appearance.VolumeIcon("Folder"));
    }

    [Theory]
    [InlineData("#3366CC")]
    [InlineData("#3366cc")]
    [InlineData("#FF3366CC")]
    [InlineData("#ff3366cc")]
    public void HexColoursAreAccepted(string color)
    {
        Assert.Equal(color, Appearance.Color(color));
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    public void NoneClearsTheColour(string color)
    {
        Assert.Null(Appearance.Color(color));
    }

    [Theory]
    [InlineData("3366CC")]
    [InlineData("#36C")]
    [InlineData("#3366CG")]
    [InlineData("#3366CC0")]
    [InlineData("red")]
    [InlineData("")]
    public void AnythingElseIsAUsageError(string color)
    {
        var error = Assert.Throws<CliException>(() => Appearance.Color(color));

        Assert.Equal(ExitCode.Usage, error.ExitCode);
    }
}
