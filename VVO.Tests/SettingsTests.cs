using VVO.UI;
using VVO.UI.ViewModels;

namespace VVO.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _path;

    public SettingsTests()
    {
        _path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}-settings.json");
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            try { File.Delete(_path); } catch { }
        }
    }

    private Settings Load() => new(_path);

    #region Recent databases

    [Fact]
    public void ANewSettingsFileStartsEmpty()
    {
        var settings = Load();

        Assert.Empty(settings.Data.RecentFiles);
        Assert.True(File.Exists(_path));
    }

    [Fact]
    public void TheMostRecentDatabaseComesFirst()
    {
        var settings = Load();

        settings.AddRecentFile(@"C:\one.vvo");
        settings.AddRecentFile(@"C:\two.vvo");

        Assert.Equal([@"C:\two.vvo", @"C:\one.vvo"], settings.Data.RecentFiles);
    }

    [Fact]
    public void ReopeningADatabaseMovesItBackToTheTopRatherThanRepeatingIt()
    {
        var settings = Load();

        settings.AddRecentFile(@"C:\one.vvo");
        settings.AddRecentFile(@"C:\two.vvo");
        settings.AddRecentFile(@"C:\one.vvo");

        Assert.Equal([@"C:\one.vvo", @"C:\two.vvo"], settings.Data.RecentFiles);
    }

    [Fact]
    public void ABlankPathIsNotRemembered()
    {
        var settings = Load();

        settings.AddRecentFile("   ");

        Assert.Empty(settings.Data.RecentFiles);
    }

    [Fact]
    public void TheOldestDatabaseDropsOffOnceTheCapIsReached()
    {
        var settings = Load();
        settings.SetMaxRecentFiles(2);

        settings.AddRecentFile(@"C:\one.vvo");
        settings.AddRecentFile(@"C:\two.vvo");
        settings.AddRecentFile(@"C:\three.vvo");

        Assert.Equal([@"C:\three.vvo", @"C:\two.vvo"], settings.Data.RecentFiles);
    }

    [Fact]
    public void LoweringTheCapTrimsWhatIsAlreadyRemembered()
    {
        var settings = Load();
        settings.AddRecentFile(@"C:\one.vvo");
        settings.AddRecentFile(@"C:\two.vvo");
        settings.AddRecentFile(@"C:\three.vvo");

        settings.SetMaxRecentFiles(1);

        Assert.Equal([@"C:\three.vvo"], settings.Data.RecentFiles);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ACapOfNothingIsRefused(int cap)
    {
        var settings = Load();
        var before = settings.Data.MaxRecentFiles;

        settings.SetMaxRecentFiles(cap);

        Assert.Equal(before, settings.Data.MaxRecentFiles);
    }

    [Fact]
    public void ForgettingADatabaseDropsItFromTheList()
    {
        var settings = Load();
        settings.AddRecentFile(@"C:\one.vvo");
        settings.AddRecentFile(@"C:\two.vvo");

        settings.RemoveRecentFile(@"C:\one.vvo");

        Assert.Equal([@"C:\two.vvo"], settings.Data.RecentFiles);
    }

    [Fact]
    public void ClearingDropsThemAll()
    {
        var settings = Load();
        settings.AddRecentFile(@"C:\one.vvo");

        settings.ClearRecentFiles();

        Assert.Empty(settings.Data.RecentFiles);
    }

    #endregion

    #region Expanded volumes

    [Fact]
    public void AVolumeTheUserHasNeverTouchedIsOpen()
    {
        var settings = Load();

        Assert.True(settings.IsVolumeExpanded(@"C:\one.vvo", Guid.NewGuid()));
    }

    [Fact]
    public void ACollapsedVolumeStaysCollapsed()
    {
        var settings = Load();
        var volume = Guid.NewGuid();

        settings.SetVolumeExpanded(@"C:\one.vvo", volume, expanded: false);

        Assert.False(settings.IsVolumeExpanded(@"C:\one.vvo", volume));
    }

    [Fact]
    public void ExpandingACollapsedVolumeOpensItAgain()
    {
        var settings = Load();
        var volume = Guid.NewGuid();
        settings.SetVolumeExpanded(@"C:\one.vvo", volume, expanded: false);

        settings.SetVolumeExpanded(@"C:\one.vvo", volume, expanded: true);

        Assert.True(settings.IsVolumeExpanded(@"C:\one.vvo", volume));
    }

    // Two databases can hold volumes with ids of their own, and neither should speak for the other
    [Fact]
    public void CollapsingAVolumeSaysNothingAboutAnotherDatabase()
    {
        var settings = Load();
        var volume = Guid.NewGuid();

        settings.SetVolumeExpanded(@"C:\one.vvo", volume, expanded: false);

        Assert.True(settings.IsVolumeExpanded(@"C:\two.vvo", volume));
    }

    [Fact]
    public void ADeletedVolumeIsForgottenRatherThanLeftCollapsed()
    {
        var settings = Load();
        var volume = Guid.NewGuid();
        settings.SetVolumeExpanded(@"C:\one.vvo", volume, expanded: false);

        settings.ForgetVolume(@"C:\one.vvo", volume);

        Assert.True(settings.IsVolumeExpanded(@"C:\one.vvo", volume));
    }

    [Fact]
    public void AVolumeStateIsNotKeptForADatabaseWithNoPath()
    {
        var settings = Load();

        settings.SetVolumeExpanded(string.Empty, Guid.NewGuid(), expanded: false);

        Assert.Empty(settings.Data.CollapsedVolumes);
    }

    #endregion

    #region Persistence

    [Fact]
    public void EverythingSurvivesAReload()
    {
        var volume = Guid.NewGuid();
        var settings = Load();

        settings.AddRecentFile(@"C:\one.vvo");
        settings.SetMaxRecentFiles(4);
        settings.SetShowFolderDetailsAlways(true);
        settings.SetVolumeExpanded(@"C:\one.vvo", volume, expanded: false);

        var reloaded = Load();

        Assert.Equal([@"C:\one.vvo"], reloaded.Data.RecentFiles);
        Assert.Equal(4, reloaded.Data.MaxRecentFiles);
        Assert.True(reloaded.Data.ShowFolderDetailsAlways);
        Assert.False(reloaded.IsVolumeExpanded(@"C:\one.vvo", volume));
    }

    // A cap of zero read back from a broken file would silently discard every entry as it is added
    [Fact]
    public void AFileMissingItsMembersFallsBackToTheDefaults()
    {
        File.WriteAllText(_path, "{}");

        var settings = Load();

        Assert.NotNull(settings.Data.RecentFiles);
        Assert.NotNull(settings.Data.CollapsedVolumes);
        Assert.True(settings.Data.MaxRecentFiles > 0);
    }

    [Fact]
    public void AFileThatCannotBeReadFallsBackToTheDefaults()
    {
        File.WriteAllText(_path, "this is not json");

        var settings = Load();

        Assert.Empty(settings.Data.RecentFiles);
        Assert.True(settings.Data.MaxRecentFiles > 0);
    }

    #endregion

    #region Options dialog

    [Fact]
    public void TheOptionsDialogOpensOnWhatIsStored()
    {
        var settings = Load();
        settings.SetShowFolderDetailsAlways(true);
        settings.SetMaxRecentFiles(9);
        settings.SetScanHiddenAndSystem(true);

        var viewModel = new OptionsDialogViewModel(settings);

        Assert.True(viewModel.ShowFolderDetailsAlways);
        Assert.Equal(9, viewModel.MaxRecentDatabases);
        Assert.True(viewModel.ScanHiddenAndSystem);
    }

    // Scanning them is the exception, so a fresh install leaves them out
    [Fact]
    public void HiddenAndSystemEntriesAreLeftOutOfAScanUntilAskedFor()
    {
        Assert.False(Load().Data.ScanHiddenAndSystem);
    }

    [Fact]
    public void AskingForHiddenAndSystemEntriesSurvivesARestart()
    {
        Load().SetScanHiddenAndSystem(true);

        Assert.True(Load().Data.ScanHiddenAndSystem);
    }

    [Fact]
    public void SavingTheOptionsDialogWritesThemBack()
    {
        var settings = Load();
        var viewModel = new OptionsDialogViewModel(settings)
        {
            ShowFolderDetailsAlways = true,
            MaxRecentDatabases = 3,
            ScanHiddenAndSystem = true
        };

        viewModel.Apply();

        Assert.True(Load().Data.ShowFolderDetailsAlways);
        Assert.Equal(3, Load().Data.MaxRecentFiles);
        Assert.True(Load().Data.ScanHiddenAndSystem);
    }

    [Fact]
    public void ChangingTheOptionsDialogWithoutSavingLeavesTheSettingsAlone()
    {
        var settings = Load();
        var viewModel = new OptionsDialogViewModel(settings)
        {
            ShowFolderDetailsAlways = true
        };

        Assert.False(settings.Data.ShowFolderDetailsAlways);
        Assert.False(viewModel.ShowFolderDetailsAlways == settings.Data.ShowFolderDetailsAlways);
    }

    #endregion
}
