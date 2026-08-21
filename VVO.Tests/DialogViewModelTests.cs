using Avalonia.Media;
using VVO.Core.Models;
using VVO.UI;
using VVO.UI.ViewModels;

namespace VVO.Tests;

/// <summary>
/// The dialogs decide for themselves when their confirm button lights up and what they hand
/// back. None of that needs a running application: without one the default icon colour falls
/// back to grey, which is all these need it to be.
/// </summary>
public class DialogViewModelTests
{
    private static RootFolderMetadata Entry(
        string? label = null, string? description = null, string? icon = null, string? color = null)
    {
        return new RootFolderMetadata
        {
            Id = Guid.NewGuid(),
            Path = @"D:\Code",
            Label = label,
            Description = description,
            Icon = icon,
            Color = color
        };
    }

    #region Naming a duplicate

    [Fact]
    public void ADuplicateCannotBeConfirmedWithoutAName()
    {
        var viewModel = new NameDialogViewModel("Name for the duplicate", "Duplicate");

        Assert.False(viewModel.CanConfirm);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankNameIsNoName(string name)
    {
        var viewModel = new NameDialogViewModel("Name", "Duplicate", "Code");

        viewModel.Name = name;

        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void ADuplicateOpensOnTheNameItIsCopying()
    {
        var viewModel = new NameDialogViewModel("Name", "Duplicate", "Code");

        Assert.Equal("Code", viewModel.Name);
        Assert.True(viewModel.CanConfirm);
    }

    #endregion

    #region New database

    [Fact]
    public void ADatabaseNeedsBothANameAndAPath()
    {
        var viewModel = new NewDatabaseDialogViewModel();
        Assert.False(viewModel.CanCreate);

        viewModel.CustomName = "Discs";
        Assert.False(viewModel.CanCreate);

        viewModel.DatabasePath = @"D:\Temp\Discs.vvo";
        Assert.True(viewModel.CanCreate);
    }

    [Fact]
    public void ABlankNameDoesNotCountAsOne()
    {
        var viewModel = new NewDatabaseDialogViewModel
        {
            CustomName = "   ",
            DatabasePath = @"D:\Temp\Discs.vvo"
        };

        Assert.False(viewModel.CanCreate);
    }

    #endregion

    #region Choosing what to compare against

    [Fact]
    public void ComparingNeedsATarget()
    {
        var viewModel = new CompareTargetDialogViewModel([]);

        Assert.False(viewModel.CanCompare);
    }

    [Fact]
    public void EitherKindOfTargetIsEnough()
    {
        var choice = new FolderChoice(Guid.NewGuid(), "Code", @"D:\Code");

        var stored = new CompareTargetDialogViewModel([choice]) { SelectedFolder = choice };
        var live = new CompareTargetDialogViewModel([choice]) { LiveFolderPath = @"D:\Elsewhere" };

        Assert.True(stored.CanCompare);
        Assert.True(live.CanCompare);
    }

    // The two targets are mutually exclusive, so the caller never has to decide which wins
    [Fact]
    public void ChoosingAStoredFolderAbandonsThePathTypedIn()
    {
        var choice = new FolderChoice(Guid.NewGuid(), "Code", @"D:\Code");
        var viewModel = new CompareTargetDialogViewModel([choice]) { LiveFolderPath = @"D:\Elsewhere" };

        viewModel.SelectedFolder = choice;

        Assert.Equal(string.Empty, viewModel.LiveFolderPath);
        Assert.True(viewModel.CanCompare);
    }

    [Fact]
    public void TypingAPathAbandonsTheStoredFolder()
    {
        var choice = new FolderChoice(Guid.NewGuid(), "Code", @"D:\Code");
        var viewModel = new CompareTargetDialogViewModel([choice]) { SelectedFolder = choice };

        viewModel.LiveFolderPath = @"D:\Elsewhere";

        Assert.Null(viewModel.SelectedFolder);
        Assert.True(viewModel.CanCompare);
    }

    [Fact]
    public void ClearingThePathDoesNotDropTheStoredFolder()
    {
        var choice = new FolderChoice(Guid.NewGuid(), "Code", @"D:\Code");
        var viewModel = new CompareTargetDialogViewModel([choice]) { SelectedFolder = choice };

        viewModel.LiveFolderPath = string.Empty;

        Assert.Same(choice, viewModel.SelectedFolder);
    }

    #endregion

    #region Virtual volume appearance

    [Fact]
    public void ANewVolumeOpensOnTheDefaultIconAndNoName()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();

        Assert.Equal("New Virtual Volume", viewModel.Header);
        Assert.Equal("Create", viewModel.ConfirmText);
        Assert.Equal(string.Empty, viewModel.VolumeName);
        Assert.Equal(VirtualVolumeIcons.Default, viewModel.Icon);
        Assert.False(viewModel.CanConfirm);
    }

    [Fact]
    public void EditingAVolumeOpensOnWhatIsStored()
    {
        var record = new VirtualVolumeRecord
        {
            Id = Guid.NewGuid(),
            Name = "Backups",
            Icon = "CompactDisc",
            Color = "#FFFF3366"
        };

        var viewModel = VirtualVolumeDialogViewModel.ForExistingVolume(record);

        Assert.Equal("Edit Virtual Volume", viewModel.Header);
        Assert.Equal("Save", viewModel.ConfirmText);
        Assert.Equal("Backups", viewModel.VolumeName);
        Assert.Equal("CompactDisc", viewModel.Icon);
        Assert.False(viewModel.IsDefaultColor);
    }

    [Fact]
    public void AVolumeNeedsAName()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();

        viewModel.VolumeName = "  ";
        Assert.False(viewModel.CanConfirm);

        viewModel.VolumeName = "Backups";
        Assert.True(viewModel.CanConfirm);
    }

    [Fact]
    public void EveryOfferedIconIsListedToChooseFrom()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();

        Assert.Equal(VirtualVolumeIcons.Keys, viewModel.Icons.Select(choice => choice.Key));
        Assert.True(viewModel.Icons.Single(choice => choice.Key == "HardDrive").IsFlipped);
    }

    [Fact]
    public void AnIconKeyThatIsNoLongerOfferedFallsBackToTheFirst()
    {
        var record = new VirtualVolumeRecord
        {
            Id = Guid.NewGuid(),
            Name = "Backups",
            Icon = "SomethingRemoved"
        };

        var viewModel = VirtualVolumeDialogViewModel.ForExistingVolume(record);

        Assert.Equal(VirtualVolumeIcons.Keys[0], viewModel.Icon);
    }

    #endregion

    #region Colour

    // Null is what tells the row to keep following the application default
    [Fact]
    public void AVolumeFollowingTheDefaultStoresNoColour()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();

        Assert.True(viewModel.IsDefaultColor);
        Assert.Null(viewModel.ColorHex);
    }

    [Fact]
    public void AChosenColourIsStored()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();

        viewModel.SelectedColor = Color.Parse("#FF3366");

        Assert.False(viewModel.IsDefaultColor);
        Assert.Equal(Color.Parse("#FF3366").ToString(), viewModel.ColorHex);
    }

    [Fact]
    public void ResettingTheColourPutsItBackOnTheDefault()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();
        viewModel.SelectedColor = Color.Parse("#FF3366");

        Assert.True(viewModel.ResetColorCommand.CanExecute(null));
        viewModel.ResetColorCommand.Execute(null);

        Assert.True(viewModel.IsDefaultColor);
        Assert.Null(viewModel.ColorHex);
    }

    [Fact]
    public void ThereIsNothingToResetWhileTheColourIsAlreadyTheDefault()
    {
        Assert.False(VirtualVolumeDialogViewModel.ForNewVolume().ResetColorCommand.CanExecute(null));
    }

    [Fact]
    public void AColourThatCannotBeReadFallsBackToTheDefault()
    {
        var record = new VirtualVolumeRecord
        {
            Id = Guid.NewGuid(),
            Name = "Backups",
            Icon = "HardDrive",
            Color = "not a colour"
        };

        Assert.True(VirtualVolumeDialogViewModel.ForExistingVolume(record).IsDefaultColor);
    }

    [Fact]
    public void TheChosenColourIsWhatThePreviewIsPainted()
    {
        var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();
        viewModel.SelectedColor = Color.Parse("#FF3366");

        var brush = Assert.IsType<SolidColorBrush>(viewModel.SelectedBrush);
        Assert.Equal(Color.Parse("#FF3366"), brush.Color);
    }

    #endregion

    #region Folder appearance

    [Fact]
    public void AFolderWithNoLabelOpensOnTheNameItWasScannedUnder()
    {
        var viewModel = new FolderDialogViewModel(Entry(), "Code");

        Assert.Equal("Code", viewModel.FolderName);
        Assert.Equal(string.Empty, viewModel.Description);
        Assert.Equal(FolderIcons.Default, viewModel.Icon);
    }

    [Fact]
    public void AFolderWithALabelOpensOnTheLabel()
    {
        var viewModel = new FolderDialogViewModel(Entry(label: "My Code", description: "notes"), "Code");

        Assert.Equal("My Code", viewModel.FolderName);
        Assert.Equal("notes", viewModel.Description);
    }

    [Fact]
    public void AFolderNeedsAName()
    {
        var viewModel = new FolderDialogViewModel(Entry(), "Code");

        viewModel.FolderName = "   ";
        Assert.False(viewModel.CanConfirm);

        viewModel.FolderName = "Code";
        Assert.True(viewModel.CanConfirm);
    }

    [Fact]
    public void TheFolderIconsAreTheOnesOnOffer()
    {
        var viewModel = new FolderDialogViewModel(Entry(), "Code");

        Assert.Equal(FolderIcons.Keys, viewModel.Icons.Select(choice => choice.Key));
        Assert.All(viewModel.Icons, choice => Assert.False(choice.IsFlipped));
    }

    #endregion

    #region Icon colours

    [Fact]
    public void AStoredColourIsWhatAnIconIsPaintedWith()
    {
        var brush = Assert.IsType<SolidColorBrush>(IconColors.Brush("#FF3366"));

        Assert.Equal(Color.Parse("#FF3366"), brush.Color);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a colour")]
    public void AnythingElseFallsBackToTheDefaultShade(string? color)
    {
        var brush = Assert.IsType<SolidColorBrush>(IconColors.Brush(color));

        Assert.Equal(IconColors.Default, brush.Color);
    }

    #endregion

    #region About

    [Fact]
    public void TheAboutBoxNamesTheApplicationAndCreditsTheIcons()
    {
        var viewModel = new AboutDialogViewModel();

        Assert.Equal("Virtual Volume Organizer", viewModel.AppName);
        Assert.Contains("MahApps.Metro.IconPacks", viewModel.IconsCredit);
    }

    // Each document is embedded under a fixed name, which nothing but loading it proves
    [Fact]
    public void TheNoticesDocumentIsTheOneEmbeddedInTheAssembly()
    {
        var viewModel = DocumentDialogViewModel.Notices();

        Assert.Equal("Third-Party Notices", viewModel.Title);
        Assert.Contains("THIRD-PARTY NOTICES", viewModel.Text);
        Assert.Contains("Font Awesome Free", viewModel.Text);
        Assert.DoesNotContain("is missing from this build", viewModel.Text);
    }

    [Fact]
    public void TheLicenseDocumentIsTheOneEmbeddedInTheAssembly()
    {
        var viewModel = DocumentDialogViewModel.License();

        Assert.Equal("License", viewModel.Title);
        Assert.Contains("MIT License", viewModel.Text);
        Assert.Contains("WITHOUT WARRANTY OF ANY KIND", viewModel.Text);
        Assert.DoesNotContain("is missing from this build", viewModel.Text);
    }

    // A build that dropped the resource says so rather than showing an empty window
    [Fact]
    public void ADocumentThatIsNotInTheBuildSaysSo()
    {
        var viewModel = new DocumentDialogViewModel("Missing", "NO-SUCH-DOCUMENT.txt");

        Assert.Contains("is missing from this build", viewModel.Text);
    }

    // The SDK appends the source revision after a '+', which is noise on screen
    [Fact]
    public void TheVersionIsShownWithoutTheBuildRevision()
    {
        var version = new AboutDialogViewModel().Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.DoesNotContain('+', version);
    }

    #endregion
}
