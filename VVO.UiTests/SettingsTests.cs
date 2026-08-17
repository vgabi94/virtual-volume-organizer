using System.Text.Json;
using Avalonia.Headless.XUnit;
using VVO.UI;

namespace VVO.UiTests;

/// <summary>
/// The settings file, which is written on every change and read once at startup. Everything
/// here survives a restart, so each test reads the file back through a second instance.
/// </summary>
public class SettingsTests : UiTestBase
{
    private Settings Reopened(Settings settings) => new(PathOf(settings));

    // The path is not exposed, so it is the one the test handed over
    private readonly Dictionary<Settings, string> _paths = [];

    private string PathOf(Settings settings) => _paths[settings];

    private Settings At(string path)
    {
        var settings = new Settings(path);
        _paths[settings] = path;

        return settings;
    }

    private Settings Fresh() => At(TempPath("json"));

    #region Reading and writing

    [AvaloniaFact]
    public void AFreshSettingsFileIsWrittenWhereThereIsNone()
    {
        var path = TempPath("json");
        Assert.False(File.Exists(path));

        var settings = At(path);

        Assert.True(File.Exists(path));
        Assert.Empty(settings.Data.RecentFiles);
        Assert.Equal(7, settings.Data.MaxRecentFiles);
    }

    [AvaloniaFact]
    public void WhatWasSavedIsWhatTheNextStartReads()
    {
        var settings = Fresh();
        settings.AddRecentFile(@"D:\Discs.vvo");
        settings.SetShowFolderDetailsAlways(true);
        settings.SetMaxRecentFiles(3);

        var reopened = Reopened(settings);

        Assert.Equal([@"D:\Discs.vvo"], reopened.Data.RecentFiles);
        Assert.True(reopened.Data.ShowFolderDetailsAlways);
        Assert.Equal(3, reopened.Data.MaxRecentFiles);
    }

    [AvaloniaFact]
    public void AFileThatIsNotSettingsAtAllFallsBackOnTheDefaults()
    {
        var path = TempPath("json");
        File.WriteAllText(path, "this is not settings");

        var settings = At(path);

        Assert.Empty(settings.Data.RecentFiles);
        Assert.Equal(7, settings.Data.MaxRecentFiles);

        // and the unreadable file has been replaced, so the next start does not trip over it
        Assert.Contains("maxRecentFiles", File.ReadAllText(path), StringComparison.OrdinalIgnoreCase);
    }

    // A file written by an older version, or edited by hand, can be missing members outright
    [AvaloniaFact]
    public void SettingsMissingTheirMembersAreFilledInRatherThanLeftEmpty()
    {
        var path = TempPath("json");
        File.WriteAllText(path, "{}");

        var settings = At(path);

        Assert.NotNull(settings.Data.RecentFiles);
        Assert.NotNull(settings.Data.CollapsedVolumes);
        Assert.Equal(7, settings.Data.MaxRecentFiles);

        // A cap of zero read straight from the file would discard every entry as it was added
        settings.AddRecentFile(@"D:\Discs.vvo");
        Assert.Single(settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public void SettingsBesideTheExecutableAreUsedWhenNoPathIsGiven()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "settings.json");

        _ = new Settings();

        Assert.True(File.Exists(beside));
    }

    #endregion

    #region Recent databases

    [AvaloniaFact]
    public void TheMostRecentDatabaseIsListedFirstAndOnlyOnce()
    {
        var settings = Fresh();

        settings.AddRecentFile(@"D:\One.vvo");
        settings.AddRecentFile(@"D:\Two.vvo");
        settings.AddRecentFile(@"D:\One.vvo");

        Assert.Equal([@"D:\One.vvo", @"D:\Two.vvo"], settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public void TheOldestDatabaseDropsOffTheEndOfTheList()
    {
        var settings = Fresh();
        settings.SetMaxRecentFiles(2);

        settings.AddRecentFile(@"D:\One.vvo");
        settings.AddRecentFile(@"D:\Two.vvo");
        settings.AddRecentFile(@"D:\Three.vvo");

        Assert.Equal([@"D:\Three.vvo", @"D:\Two.vvo"], settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public void LoweringTheCapTrimsTheListThatIsAlreadyThere()
    {
        var settings = Fresh();
        settings.AddRecentFile(@"D:\One.vvo");
        settings.AddRecentFile(@"D:\Two.vvo");
        settings.AddRecentFile(@"D:\Three.vvo");

        settings.SetMaxRecentFiles(1);

        Assert.Equal([@"D:\Three.vvo"], Reopened(settings).Data.RecentFiles);
    }

    [AvaloniaFact]
    public void ACapOfNoneIsRefused()
    {
        var settings = Fresh();

        settings.SetMaxRecentFiles(0);

        Assert.Equal(7, settings.Data.MaxRecentFiles);
    }

    [AvaloniaFact]
    public void SettingTheCapToWhatItAlreadyIsChangesNothing()
    {
        var settings = Fresh();

        settings.SetMaxRecentFiles(7);

        Assert.Equal(7, settings.Data.MaxRecentFiles);
    }

    [AvaloniaFact]
    public void ANamelessDatabaseIsNotRemembered()
    {
        var settings = Fresh();

        settings.AddRecentFile("   ");

        Assert.Empty(settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public void ForgettingADatabaseThatIsNotListedChangesNothing()
    {
        var settings = Fresh();
        settings.AddRecentFile(@"D:\One.vvo");

        settings.RemoveRecentFile(@"D:\Elsewhere.vvo");

        Assert.Equal([@"D:\One.vvo"], settings.Data.RecentFiles);
    }

    [AvaloniaFact]
    public void TheWholeListCanBeCleared()
    {
        var settings = Fresh();
        settings.AddRecentFile(@"D:\One.vvo");
        settings.AddRecentFile(@"D:\Two.vvo");

        settings.ClearRecentFiles();

        Assert.Empty(Reopened(settings).Data.RecentFiles);
    }

    #endregion

    #region Which volumes are folded away

    [AvaloniaFact]
    public void AVolumeNobodyHasTouchedIsOpen()
    {
        Assert.True(Fresh().IsVolumeExpanded(@"D:\Discs.vvo", Guid.NewGuid()));
    }

    [AvaloniaFact]
    public void AFoldedVolumeIsStillFoldedOnTheNextStart()
    {
        var settings = Fresh();
        var volume = Guid.NewGuid();

        settings.SetVolumeExpanded(@"D:\Discs.vvo", volume, expanded: false);

        Assert.False(Reopened(settings).IsVolumeExpanded(@"D:\Discs.vvo", volume));
    }

    // Only the folded ones are written down, so unfolding the last of them leaves nothing behind
    [AvaloniaFact]
    public void UnfoldingTheLastVolumeTakesTheDatabaseOffTheList()
    {
        var settings = Fresh();
        var volume = Guid.NewGuid();
        settings.SetVolumeExpanded(@"D:\Discs.vvo", volume, expanded: false);

        settings.SetVolumeExpanded(@"D:\Discs.vvo", volume, expanded: true);

        Assert.Empty(Reopened(settings).Data.CollapsedVolumes);
    }

    [AvaloniaFact]
    public void OneFoldedVolumeSaysNothingAboutTheOthersInTheSameDatabase()
    {
        var settings = Fresh();
        var folded = Guid.NewGuid();
        var open = Guid.NewGuid();

        settings.SetVolumeExpanded(@"D:\Discs.vvo", folded, expanded: false);

        Assert.False(settings.IsVolumeExpanded(@"D:\Discs.vvo", folded));
        Assert.True(settings.IsVolumeExpanded(@"D:\Discs.vvo", open));
        Assert.True(settings.IsVolumeExpanded(@"D:\Elsewhere.vvo", folded));
    }

    [AvaloniaFact]
    public void FoldingAVolumeThatIsAlreadyFoldedChangesNothing()
    {
        var settings = Fresh();
        var volume = Guid.NewGuid();
        settings.SetVolumeExpanded(@"D:\Discs.vvo", volume, expanded: false);

        settings.SetVolumeExpanded(@"D:\Discs.vvo", volume, expanded: false);

        Assert.Single(Reopened(settings).Data.CollapsedVolumes[@"D:\Discs.vvo"]);
    }

    [AvaloniaFact]
    public void UnfoldingAVolumeThatWasNeverFoldedChangesNothing()
    {
        var settings = Fresh();

        settings.SetVolumeExpanded(@"D:\Discs.vvo", Guid.NewGuid(), expanded: true);

        Assert.Empty(settings.Data.CollapsedVolumes);
    }

    [AvaloniaFact]
    public void AVolumeInADatabaseWithNoPathIsNotRecorded()
    {
        var settings = Fresh();

        settings.SetVolumeExpanded(string.Empty, Guid.NewGuid(), expanded: false);

        Assert.Empty(settings.Data.CollapsedVolumes);
    }

    // A deleted volume leaves its id behind, and the id would fold whatever took its place
    [AvaloniaFact]
    public void ForgettingAVolumeLeavesNothingForANewOneToInherit()
    {
        var settings = Fresh();
        var volume = Guid.NewGuid();
        settings.SetVolumeExpanded(@"D:\Discs.vvo", volume, expanded: false);

        settings.ForgetVolume(@"D:\Discs.vvo", volume);

        Assert.True(settings.IsVolumeExpanded(@"D:\Discs.vvo", volume));
    }

    #endregion

    #region Showing folder details

    [AvaloniaFact]
    public void TurningFolderDetailsOnAndOffAgainIsRemembered()
    {
        var settings = Fresh();

        settings.SetShowFolderDetailsAlways(true);
        Assert.True(Reopened(settings).Data.ShowFolderDetailsAlways);

        settings.SetShowFolderDetailsAlways(false);
        Assert.False(Reopened(settings).Data.ShowFolderDetailsAlways);
    }

    [AvaloniaFact]
    public void SettingFolderDetailsToWhatTheyAlreadyAreDoesNotRewriteTheFile()
    {
        var settings = Fresh();
        var written = File.GetLastWriteTimeUtc(PathOf(settings));

        settings.SetShowFolderDetailsAlways(false);

        Assert.Equal(written, File.GetLastWriteTimeUtc(PathOf(settings)));
    }

    [AvaloniaFact]
    public void TheFileIsPlainEnoughToBeEditedByHand()
    {
        var settings = Fresh();
        settings.AddRecentFile(@"D:\Discs.vvo");

        using var document = JsonDocument.Parse(File.ReadAllText(PathOf(settings)));

        Assert.Equal(
            @"D:\Discs.vvo",
            document.RootElement.GetProperty("RecentFiles")[0].GetString());
    }

    #endregion
}
