using VVO.UI;

namespace VVO.Tests;

public class CataloguePathTests
{
    [Fact]
    public void RootReadsAsADriveLetter()
    {
        Assert.Equal("test:", CataloguePath.Root("test"));
    }

    [Fact]
    public void RootPathReadsAsADriveRoot()
    {
        Assert.Equal(@"test:\", CataloguePath.RootPath("test"));
    }

    // The virtual volume stands in for the drive and the scanned folder is the first step below it
    [Fact]
    public void CombineHangsTheRootFolderOffTheVolumeWithoutDoublingTheSeparator()
    {
        Assert.Equal(@"test:\Code", CataloguePath.Combine(CataloguePath.RootPath("test"), "Code"));
    }

    [Fact]
    public void CombineSeparatesTheStepsBelowTheRootFolder()
    {
        var path = CataloguePath.Combine(
            CataloguePath.Combine(CataloguePath.RootPath("test"), "Code"), "AdventOfCode");

        Assert.Equal(@"test:\Code\AdventOfCode", path);
    }

    [Fact]
    public void CombineOntoNothingIsJustTheName()
    {
        Assert.Equal("folder", CataloguePath.Combine(string.Empty, "folder"));
    }
}
