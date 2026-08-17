using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// Moving about inside one scanned tree, and the explorer's own search box, which looks only
/// within the folder the user is standing in.
/// </summary>
public class ExplorerNavigationTests : UiTestBase
{
    private VolumeExplorerViewModel Explorer { get; }

    public ExplorerNavigationTests()
    {
        Explorer = NewExplorer();
    }

    private async Task<RootFolderMetadata> GivenAFolderShownAsync()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");

        Detach(Explorer);
        await Explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        return entry;
    }

    private FileItem Item(string name) => Explorer.Files!.Single(file => file.Name == name);

    #region Going up and down

    [AvaloniaFact]
    public async Task GoingUpReturnsToTheFolderAbove()
    {
        await GivenAFolderShownAsync();
        await Explorer.OpenItemCommand.ExecuteAsync(Item("AdventOfCode"));
        await Explorer.OpenItemCommand.ExecuteAsync(Item("2023"));

        Assert.True(Explorer.NavigateUpCommand.CanExecute(null));
        await Explorer.NavigateUpCommand.ExecuteAsync(null);

        Assert.Equal(["Code", "AdventOfCode"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task GoingUpFromTheTopIsRefusedRatherThanEmptyingThePath()
    {
        await GivenAFolderShownAsync();

        await Explorer.NavigateUpCommand.ExecuteAsync(null);

        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task OpeningAFileLeavesTheUserWhereTheyAre()
    {
        await GivenAFolderShownAsync();

        await Explorer.OpenItemCommand.ExecuteAsync(Item("readme"));

        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task OpeningNothingAtAllLeavesTheUserWhereTheyAre()
    {
        await GivenAFolderShownAsync();

        await Explorer.OpenItemCommand.ExecuteAsync(null);

        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task ACrumbForTheFolderAlreadyShownDoesNothing()
    {
        await GivenAFolderShownAsync();
        await Explorer.OpenItemCommand.ExecuteAsync(Item("AdventOfCode"));

        await Explorer.NavigateToCommand.ExecuteAsync(Explorer.Breadcrumbs[^1].Id);

        Assert.Equal(["Code", "AdventOfCode"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task ACrumbForAFolderNoLongerOnThePathDoesNothing()
    {
        await GivenAFolderShownAsync();

        await Explorer.NavigateToCommand.ExecuteAsync(Guid.NewGuid());

        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    [AvaloniaFact]
    public async Task WhatIsInAFolderIsSummarisedByCountAndSize()
    {
        await GivenAFolderShownAsync();

        Assert.Contains("2 items", Explorer.ItemSummary);
        Assert.Contains("60 B", Explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task AFolderHoldingOneThingIsCountedInTheSingular()
    {
        await GivenAFolderShownAsync();

        await Explorer.OpenItemCommand.ExecuteAsync(Item("AdventOfCode"));

        Assert.Contains("1 item ", Explorer.ItemSummary);
    }

    #endregion

    #region The search box

    [AvaloniaFact]
    public async Task TypingInTheBoxListsWhatTheVolumeHoldsWhereverItIs()
    {
        await GivenAFolderShownAsync();

        Explorer.SearchTerm = "day1";

        await Until(() => Explorer.IsFlatMode, "the search to run");
        Assert.Equal(["day1"], Explorer.Files!.Select(file => file.Name));
        Assert.Equal(@"test:\Code\AdventOfCode\2023", Item("day1").Location);
    }

    [AvaloniaFact]
    public async Task ClearingTheBoxGoesBackToTheFolderTheUserWasIn()
    {
        await GivenAFolderShownAsync();
        Explorer.SearchTerm = "day1";
        await Until(() => Explorer.IsFlatMode, "the search to run");

        Explorer.SearchTerm = string.Empty;

        await Until(() => !Explorer.IsFlatMode, "the search to be called off");
        Assert.Equal(["Code"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
    }

    // Only the last keystroke of a burst is searched for, so typing does not query on every letter
    [AvaloniaFact]
    public async Task OnlyTheTermTheUserStoppedOnIsSearchedFor()
    {
        await GivenAFolderShownAsync();

        Explorer.SearchTerm = "d";
        Explorer.SearchTerm = "da";
        Explorer.SearchTerm = "day1";

        await Until(() => Explorer.IsFlatMode, "the search to run");
        Assert.Equal(["day1"], Explorer.Files!.Select(file => file.Name));
    }

    [AvaloniaFact]
    public async Task ASearchMatchingNothingInTheVolumeSaysSo()
    {
        await GivenAFolderShownAsync();

        Explorer.SearchTerm = "nowhere";

        await Until(() => Explorer.ItemSummary == "0 matches", "the search to run");
        Assert.Empty(Explorer.Files!);
    }

    [AvaloniaFact]
    public async Task OneHitIsCountedInTheSingular()
    {
        await GivenAFolderShownAsync();

        Explorer.SearchTerm = "day1";

        await Until(() => Explorer.ItemSummary == "1 match", "the search to run");
    }

    [AvaloniaFact]
    public async Task AFolderFoundBySearchOpensAtWhereItActuallyIs()
    {
        await GivenAFolderShownAsync();
        Explorer.SearchTerm = "2023";
        await Until(() => Explorer.IsFlatMode, "the search to run");

        await Explorer.OpenItemCommand.ExecuteAsync(Item("2023"));

        // The whole path is rebuilt, not appended to wherever the user happened to be
        Assert.Equal(["Code", "AdventOfCode", "2023"], Explorer.Breadcrumbs.Select(crumb => crumb.Name));
        Assert.False(Explorer.IsFlatMode);
    }

    [AvaloniaFact]
    public async Task PickingAnotherFolderCallsOffTheSearchInTheOldOne()
    {
        var first = await GivenAFolderShownAsync();
        Explorer.SearchTerm = "day1";
        await Until(() => Explorer.IsFlatMode, "the search to run");

        await Explorer.ShowFolderAsync(new FolderSelectedMessage(first.TreeId, "Code", "test"));

        Assert.Equal(string.Empty, Explorer.SearchTerm);
        Assert.False(Explorer.IsFlatMode);
    }

    #endregion

    #region Answering the sidebar

    [AvaloniaFact]
    public async Task TheExplorerFollowsTheFolderTheSidebarBroadcasts()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");

        WeakReferenceMessenger.Default.Send(new FolderSelectedMessage(entry.TreeId, "Code", "test"));

        await Until(() => Explorer.Breadcrumbs.Count == 1, "the explorer to follow");
        Assert.Equal("test:", Explorer.VolumePrefix);
    }

    [AvaloniaFact]
    public async Task TheExplorerFollowsASearchTheSidebarBroadcasts()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");

        WeakReferenceMessenger.Default.Send(new SearchAllMessage(
            "day1", [new SearchScope(entry.TreeId, "Code", "test")]));

        await Until(() => Explorer.IsFlatMode, "the explorer to follow");
        Assert.Equal(["day1"], Explorer.Files!.Select(file => file.Name));
    }

    // The database can go while the window is still open — an unplugged drive, a file taken
    // by something else — and the explorer is told about it on a thread nothing is waiting on
    [AvaloniaFact]
    public async Task ADatabaseThatCannotBeReadIsReportedRatherThanLost()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");

        using (DatabaseTakenAway())
        {
            WeakReferenceMessenger.Default.Send(new SearchAllMessage(
                "day1", [new SearchScope(entry.TreeId, "Code", "test")]));

            await Until(() => Told.Count > 0, "the failure to be reported");
        }

        Assert.Equal("Error", Told[0].Title);
    }

    [AvaloniaFact]
    public async Task AFolderThatCannotBeReadIsReportedRatherThanLost()
    {
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        Detach(Explorer);

        using (DatabaseTakenAway())
        {
            await Explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test"));
        }

        Assert.Equal("Error", Assert.Single(Told).Title);
    }

    #endregion
}
