using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// Deleting catalogued files from the volume explorer: confirmation, the tree, and the
/// sidebar size that follows it.
/// </summary>
public class ExplorerDeleteTests : UiTestBase
{
    private void ConfirmDeletes()
    {
        AnswerDialogs<ConfirmDeleteDialogViewModel>(
            dialog => dialog.Confirmation = ConfirmDeleteDialogViewModel.RequiredPhrase);
    }

    private async Task<(VolumeExplorerViewModel Explorer, RootFolderMetadata Entry)> GivenAFolderShownAsync()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));

        return (explorer, entry);
    }

    [AvaloniaFact]
    public async Task DeletingAFileRemovesItFromTheFolder()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(explorer.Files!, file => file.Name == "readme");
        Assert.Contains(explorer.Files!, file => file.Name == "AdventOfCode");
        Assert.Equal("1 item · 20 B", explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task DeletingAFolderTakesWhatIsUnderIt()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "AdventOfCode");
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(explorer.Files!, file => file.Name == "AdventOfCode");
        Assert.Empty(await Database.FindItemsAsync<FileRecord>(
            record => record.RootFolderId == entry.TreeId && record.Name == "day1.txt"));
    }

    [AvaloniaFact]
    public async Task DeletingSeveralSelectedRowsRemovesEach()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        explorer.ReplaceSelection(explorer.Files!.ToList());
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Empty(explorer.Files!);
    }

    [AvaloniaFact]
    public async Task ADeleteThatIsNotConfirmedLeavesTheFileAlone()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        AnswerDialogs(_ => false);

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Contains(explorer.Files!, file => file.Name == "readme");
    }

    [AvaloniaFact]
    public async Task DeletingAFileUpdatesTheSidebarSize()
    {
        var sidebar = NewSidebar();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddFolderAsync(volume.Id, "Code");
        await sidebar.LoadAsync();

        var explorer = NewExplorer();
        Detach(explorer);
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Equal("20 B", sidebar.VirtualVolumes.Single().Folders.Single().Subtitle);
    }

    [AvaloniaFact]
    public async Task DeletingFromASearchPutsTheRemainingHitsBack()
    {
        var (explorer, _) = await GivenAFolderShownAsync();
        explorer.SearchTerm = "e";
        await Until(() => explorer.IsFlatMode, "the search");
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.DoesNotContain(explorer.Files!, file => file.Name == "readme");
        Assert.True(explorer.IsFlatMode);
    }

    [AvaloniaFact]
    public async Task GoingUpAfterADeleteShowsTheFolderAtItsNewSize()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddDeepFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        await explorer.OpenItemCommand.ExecuteAsync(explorer.Files!.Single(file => file.Name == "AdventOfCode"));
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "2023");
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);
        await explorer.NavigateUpCommand.ExecuteAsync(null);

        Assert.Equal("10 B", explorer.Files!.Single(file => file.Name == "AdventOfCode").Size);
        Assert.Equal("2 items · 20 B", explorer.ItemSummary);
    }

    [AvaloniaFact]
    public async Task DeletingFromASearchAcrossVolumesLeavesTheOtherHits()
    {
        var explorer = NewExplorer();
        Detach(explorer);

        var one = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await Volumes.CreateVirtualVolumeAsync("test2", "CompactDisc");
        var code = await AddFolderAsync(one.Id, "Code");
        var docs = await AddFolderAsync(two.Id, "Docs");
        await explorer.SearchAllAsync(new SearchAllMessage("readme",
        [
            new SearchScope(code.TreeId, "Code", "test", code.Path),
            new SearchScope(docs.TreeId, "Docs", "test2", docs.Path)
        ]));
        explorer.SelectedFile = explorer.Files!.Single(file => file.Location.Contains(@"test:\Code"));
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(["readme"], explorer.Files!.Select(file => file.Name));
        Assert.Contains(@"test2:\Docs", explorer.Files!.Single().Location);
        Assert.True(explorer.IsFlatMode);
    }

    [AvaloniaFact]
    public async Task DeletingAFileUpdatesEverySidebarRowSharingTheTree()
    {
        var sidebar = NewSidebar();
        var one = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var two = await Volumes.CreateVirtualVolumeAsync("copy", "CompactDisc");
        var entry = await AddFolderAsync(one.Id, "Code");
        await Volumes.CopyFolderAsync(entry.Id, two.Id);
        await sidebar.LoadAsync();

        var explorer = NewExplorer();
        Detach(explorer);
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        ConfirmDeletes();

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(
            ["20 B", "20 B"],
            sidebar.VirtualVolumes.SelectMany(node => node.Folders).Select(folder => folder.Subtitle));
    }

    [AvaloniaFact]
    public async Task CancellingFromTheStatusBarLeavesTheFileWhereItWas()
    {
        var explorer = NewExplorer();
        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        var entry = await AddFolderAsync(volume.Id, "Code");
        await explorer.ShowFolderAsync(new FolderSelectedMessage(entry.TreeId, "Code", "test", entry.Path));
        explorer.SelectedFile = explorer.Files!.Single(file => file.Name == "readme");
        ConfirmDeletes();

        using var probe = new MessageProbe<UpdateStatusMessage>(message =>
        {
            if (message.IsCancellable)
            {
                explorer.Receive(new CancelRequestedMessage());
            }
        });

        await explorer.DeleteCommand.ExecuteAsync(null);

        Assert.Contains(explorer.Files!, file => file.Name == "readme");
        Assert.Equal(3, (await Database.FindItemsAsync<FileRecord>(
            record => record.RootFolderId == entry.TreeId)).Count);
    }
}
