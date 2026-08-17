using VVO.UI;

namespace VVO.Tests;

/// <summary>
/// What the application makes of the command line it was started on, which is where a
/// double-clicked catalogue arrives once .vvo is associated with the executable.
/// </summary>
public class StartupCatalogueTests
{
    [Fact]
    public void TheCatalogueIsTheArgumentTheShellHandedOver()
    {
        Assert.Equal(@"D:\Discs\music.vvo", StartupCatalogue.From([@"D:\Discs\music.vvo"]));
    }

    [Theory]
    [InlineData(".VVO")]
    [InlineData(".Vvo")]
    public void TheExtensionIsMatchedWhateverCaseItIsWrittenIn(string extension)
    {
        Assert.Equal("music" + extension, StartupCatalogue.From(["music" + extension]));
    }

    // Started from a shortcut or the debugger, which is the ordinary case
    [Fact]
    public void NothingIsOpenedWhenTheApplicationWasStartedOnNothing()
    {
        Assert.Null(StartupCatalogue.From([]));
        Assert.Null(StartupCatalogue.From(null));
    }

    [Fact]
    public void AFileOfSomeOtherKindIsNotACatalogue()
    {
        Assert.Null(StartupCatalogue.From([@"D:\Discs\export.json"]));
        Assert.Null(StartupCatalogue.From([@"D:\Discs\notes"]));
    }

    // The framework reads its own switches from the same list
    [Fact]
    public void ASwitchIsNeverTakenForAPath()
    {
        Assert.Null(StartupCatalogue.From(["--some-switch"]));
        Assert.Equal("music.vvo", StartupCatalogue.From(["--some-switch", "music.vvo"]));
    }

    [Fact]
    public void TheFirstCatalogueWinsWhenSeveralWereHandedOver()
    {
        Assert.Equal("first.vvo", StartupCatalogue.From(["first.vvo", "second.vvo"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankArgumentIsNotAPath(string blank)
    {
        Assert.Null(StartupCatalogue.From([blank]));
    }

    // Whether the file is there is the caller's business: it has somewhere to say so
    [Fact]
    public void APathIsHandedBackWithoutBeingLookedForOnDisk()
    {
        Assert.Equal(@"Z:\gone\missing.vvo", StartupCatalogue.From([@"Z:\gone\missing.vvo"]));
    }
}
