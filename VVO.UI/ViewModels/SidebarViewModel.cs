using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.UI.Messages;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;

namespace VVO.UI.ViewModels;

public record FolderItem
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    // The name the folder was scanned under, which a cleared label falls back to
    public required string ScannedName { get; init; }

    // Set while this folder is the one waiting on the clipboard to be moved
    public bool IsCut { get; init; }

    // Not displayed in UI
    public required RootFolderMetadata Entry { get; init; }

    public Geometry? IconData => FolderIcons.Lookup(Entry.Icon);
    public bool IsIconFlipped => FolderIcons.IsFlipped(Entry.Icon);
    public IBrush IconBrush => IconColors.Brush(Entry.Color);

    // Menu items live in a popup with its own name scope, where a binding to the sidebar
    // by name does not resolve. Reaching the commands through the row itself always does.
    public required SidebarViewModel Owner { get; init; }
}

public partial class VirtualVolumeNode : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Name))]
    [NotifyPropertyChangedFor(nameof(IconData))]
    [NotifyPropertyChangedFor(nameof(IsIconFlipped))]
    [NotifyPropertyChangedFor(nameof(IconBrush))]
    public partial VirtualVolumeRecord Record { get; set; }

    [ObservableProperty]
    public partial bool IsExpanded { get; set; } = true;

    [ObservableProperty]
    public partial FolderItem? SelectedFolder { get; set; }

    public Guid Id => Record.Id;
    public string Name => Record.Name;
    public Geometry? IconData => VirtualVolumeIcons.Lookup(Record.Icon);
    public bool IsIconFlipped => VirtualVolumeIcons.IsFlipped(Record.Icon);
    public IBrush IconBrush => IconColors.Brush(Record.Color);

    public SidebarViewModel Owner { get; }

    public AvaloniaList<FolderItem> Folders { get; } = new();

    public VirtualVolumeNode(VirtualVolumeRecord record, SidebarViewModel owner)
    {
        Record = record;
        Owner = owner;
    }
}

public partial class SidebarViewModel : ViewModelBase
    , IRecipient<FolderAddedMessage>
    , IRecipient<DatabaseReady>
    , IRecipient<CancelRequestedMessage>
{
    private const int SearchDelayMilliseconds = 300;

    private readonly IDatabaseService _databaseService;
    private readonly IFileScannerService _fileScannerService;
    private readonly IFolderCompareService _folderCompareService;
    private readonly IVirtualVolumeService _virtualVolumeService;
    private readonly IDatabaseTransferService _databaseTransferService;
    private readonly Settings _settings;

    private CancellationTokenSource? _compareCancellation;
    private CancellationTokenSource? _writeCancellation;

    // The folder waiting to be pasted, and whether pasting it should empty its source
    private RootFolderMetadata? _clipboardEntry;
    private bool _clipboardIsCut;

    // Raised by every keystroke, so one still waiting out its delay can be abandoned
    private int _searchToken;
    private bool _suppressSearch;

    public AvaloniaList<VirtualVolumeNode> VirtualVolumes { get; } = new();

    [ObservableProperty]
    public partial VirtualVolumeNode? SelectedVirtualVolume { get; set; }

    // The selection lives on whichever volume owns it, mirrored here so the menu bar has a
    // single place to reach it from
    [ObservableProperty]
    public partial FolderItem? SelectedFolder { get; set; }

    [ObservableProperty]
    public partial string DatabaseName { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCopyCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    public partial string DatabasePath { get; set; }

    [ObservableProperty]
    public partial bool ShowFolderDetailsAlways { get; set; }

    // Searches every folder at once; the hits are listed by the volume explorer
    [ObservableProperty]
    public partial string SearchAllTerm { get; set; } = string.Empty;

    // Where the menu bar's Add Folder lands. Null while the database holds no virtual volume.
    public Guid? TargetVirtualVolumeId => SelectedVirtualVolume?.Id ?? VirtualVolumes.FirstOrDefault()?.Id;

    private IEnumerable<FolderItem> AllFolders => VirtualVolumes.SelectMany(node => node.Folders);

    public SidebarViewModel(IUndoService undoManager
        , IDatabaseService databaseService
        , IFileScannerService fileScannerService
        , IFolderCompareService folderCompareService
        , IVirtualVolumeService virtualVolumeService
        , IDatabaseTransferService databaseTransferService
        , Settings settings)
        : base(undoManager)
    {
        WeakReferenceMessenger.Default.RegisterAll(this);
        _databaseService = databaseService;
        _fileScannerService = fileScannerService;
        _folderCompareService = folderCompareService;
        _virtualVolumeService = virtualVolumeService;
        _databaseTransferService = databaseTransferService;
        _settings = settings;

        ShowFolderDetailsAlways = settings.Data.ShowFolderDetailsAlways;
    }

    partial void OnShowFolderDetailsAlwaysChanged(bool value)
    {
        _settings.SetShowFolderDetailsAlways(value);
    }

    public void Receive(CancelRequestedMessage message)
    {
        _compareCancellation?.Cancel();
        _writeCancellation?.Cancel();
    }

    /// <summary>
    /// Runs a rewrite of the database under the status bar's Cancel button. Emptying a volume of
    /// large trees or replacing one takes a while, and the transaction it runs in means stopping
    /// costs nothing but the time already spent.
    /// </summary>
    private async Task WritingAsync(string what, Func<IProgress<string>, CancellationToken, Task> write)
    {
        using var cancellation = new CancellationTokenSource();
        _writeCancellation = cancellation;

        var progress = new Progress<string>(message =>
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, message, IsCancellable: true)));

        try
        {
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, what, IsCancellable: true));
            await write(progress, cancellation.Token);
        }
        finally
        {
            _writeCancellation = null;
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
        }
    }

    // Each virtual volume owns a separate list with its own selection, so picking a folder
    // anywhere has to drop the selection everywhere else for the sidebar to read as a
    // single choice.
    private void OnVirtualVolumeNodeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VirtualVolumeNode node)
            return;

        if (e.PropertyName == nameof(VirtualVolumeNode.IsExpanded))
        {
            _settings.SetVolumeExpanded(_databaseService.DbPath, node.Id, node.IsExpanded);
            return;
        }

        if (e.PropertyName != nameof(VirtualVolumeNode.SelectedFolder))
            return;

        if (node.SelectedFolder == null)
            return;

        foreach (var other in VirtualVolumes)
        {
            if (!ReferenceEquals(other, node))
            {
                other.SelectedFolder = null;
            }
        }

        SelectedVirtualVolume = node;
        SelectedFolder = node.SelectedFolder;

        // The explorer is about to show one folder, so a search covering all of them is over
        ClearSearchTerm();

        WeakReferenceMessenger.Default.Send(new FolderSelectedMessage(
            node.SelectedFolder.Entry.TreeId, node.SelectedFolder.Title, node.Name,
            node.SelectedFolder.Entry.Path));
    }

    #region Search

    partial void OnSearchAllTermChanged(string value)
    {
        if (_suppressSearch)
            return;

        _ = SearchAllAfterDelayAsync(value);
    }

    private async Task SearchAllAfterDelayAsync(string term)
    {
        var token = ++_searchToken;

        try
        {
            await Task.Delay(SearchDelayMilliseconds);
            if (token != _searchToken)
                return;

            WeakReferenceMessenger.Default.Send(new SearchAllMessage(term, SearchScopes()));
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    // A tree shared by a copy and its original is one place to look, listed under whichever
    // folder came first and the volume that one sits in
    private IReadOnlyList<SearchScope> SearchScopes()
    {
        return VirtualVolumes
            .SelectMany(node => node.Folders.Select(
                folder => new SearchScope(
                    folder.Entry.TreeId, folder.Title, node.Name, folder.Entry.Path)))
            .GroupBy(scope => scope.TreeId)
            .Select(group => group.First())
            .ToList();
    }

    private void ClearSearchTerm()
    {
        if (SearchAllTerm.Length == 0)
            return;

        _suppressSearch = true;
        SearchAllTerm = string.Empty;
        _suppressSearch = false;

        // Abandons any keystroke still waiting out its delay
        _searchToken++;
    }

    #endregion

    #region Virtual volume commands

    [RelayCommand]
    private void SelectVirtualVolume(VirtualVolumeNode? node)
    {
        if (node != null)
        {
            SelectedVirtualVolume = node;
        }
    }

    [RelayCommand]
    private void ExpandAllVirtualVolumes() => SetAllExpanded(true);

    [RelayCommand]
    private void CollapseAllVirtualVolumes() => SetAllExpanded(false);

    private void SetAllExpanded(bool expanded)
    {
        foreach (var node in VirtualVolumes)
        {
            node.IsExpanded = expanded;
        }
    }

    [RelayCommand]
    private async Task NewVirtualVolume()
    {
        try
        {
            var window = Dialogs.Owner();
            if (window == null)
                return;

            var viewModel = VirtualVolumeDialogViewModel.ForNewVolume();
            var dialog = new Views.VirtualVolumeDialogView { DataContext = viewModel };

            if (!await Dialogs.ShowAsync(dialog, window) || !viewModel.CanConfirm)
                return;

            var name = viewModel.VolumeName.Trim();

            // Minted on the first run and put back under the same identity on a redo, so the
            // undo that follows still knows what to delete
            VirtualVolumeRecord? created = null;

            await _undoManager.ExecuteAsync(
                execute: async () =>
                {
                    if (created == null)
                    {
                        created = await _virtualVolumeService.CreateVirtualVolumeAsync(
                            name, viewModel.Icon, viewModel.ColorHex);
                    }
                    else
                    {
                        await _virtualVolumeService.RestoreVirtualVolumeAsync(created);
                    }

                    SelectedVirtualVolume = AddVirtualVolumeNode(created, []);
                },
                undo: async () =>
                {
                    await _virtualVolumeService.DeleteVirtualVolumeAsync(created!.Id);
                    RemoveVirtualVolumeNode(created.Id);
                });
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand]
    private async Task EditVirtualVolume(VirtualVolumeNode? node)
    {
        node ??= SelectedVirtualVolume;
        if (node == null)
            return;

        try
        {
            var window = Dialogs.Owner();
            if (window == null)
                return;

            var viewModel = VirtualVolumeDialogViewModel.ForExistingVolume(node.Record);
            var dialog = new Views.VirtualVolumeDialogView { DataContext = viewModel };

            if (!await Dialogs.ShowAsync(dialog, window) || !viewModel.CanConfirm)
                return;

            var before = node.Record;
            var after = before with
            {
                Name = viewModel.VolumeName.Trim(),
                Icon = viewModel.Icon,
                Color = viewModel.ColorHex
            };

            await _undoManager.ExecuteAsync(
                () => ApplyVirtualVolumeAsync(after),
                () => ApplyVirtualVolumeAsync(before));
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand]
    private async Task DeleteVirtualVolume(VirtualVolumeNode? node)
    {
        node ??= SelectedVirtualVolume;
        if (node == null)
            return;

        var window = Dialogs.Owner();
        if (window == null)
            return;

        var confirmation = ConfirmDeleteDialogViewModel.ForVirtualVolume(node.Name, node.Folders.Count);
        var confirmDialog = new Views.ConfirmDeleteDialogView { DataContext = confirmation };

        if (!await Dialogs.ShowAsync(confirmDialog, window))
            return;

        try
        {
            await WritingAsync("Deleting virtual volume...", (progress, token) =>
                _virtualVolumeService.DeleteVirtualVolumeAsync(node.Id, progress, token));

            if (_clipboardEntry?.VirtualVolumeId == node.Id)
            {
                ClearClipboard();
            }

            RemoveVirtualVolumeNode(node.Id);

            // The scanned trees are gone with it, and any step still on the stack may be
            // describing one of them
            _undoManager.Clear();
        }
        catch (OperationCanceledException)
        {
            // Rolled back with it, so the volume is still listed where it was
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand]
    private void AddFolder(VirtualVolumeNode? node)
    {
        var window = Dialogs.Owner();
        if (node == null || window == null)
            return;

        SelectedVirtualVolume = node;
        WeakReferenceMessenger.Default.Send(new AddFolderMessage(window, node.Id));
    }

    private async Task ApplyVirtualVolumeAsync(VirtualVolumeRecord record)
    {
        await _virtualVolumeService.UpdateVirtualVolumeAsync(
            record.Id, record.Name, record.Icon, record.Color);

        // Looked up rather than captured: a volume deleted and put back again is a new node
        var node = VirtualVolumes.FirstOrDefault(candidate => candidate.Id == record.Id);
        if (node != null)
        {
            node.Record = record;
        }
    }

    #endregion

    #region Folder commands

    [RelayCommand]
    private void CopyFolder(FolderItem? item)
    {
        if (item != null)
        {
            SetClipboard(item.Entry, cut: false);
        }
    }

    [RelayCommand]
    private void CutFolder(FolderItem? item)
    {
        if (item != null)
        {
            SetClipboard(item.Entry, cut: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task PasteBesideFolder(FolderItem? item)
    {
        return item == null ? Task.CompletedTask : PasteIntoAsync(item.Entry.VirtualVolumeId);
    }

    [RelayCommand(CanExecute = nameof(CanPaste))]
    private Task PasteIntoVirtualVolume(VirtualVolumeNode? node)
    {
        return node == null ? Task.CompletedTask : PasteIntoAsync(node.Id);
    }

    private bool CanPaste() => _clipboardEntry != null;

    private async Task PasteIntoAsync(Guid virtualVolumeId)
    {
        var entry = _clipboardEntry;
        if (entry == null)
            return;

        // A cut folder is already where it would land, so pasting it back into its own volume
        // is how the user calls the move off. A copy pasted there duplicates it instead.
        if (_clipboardIsCut && entry.VirtualVolumeId == virtualVolumeId)
        {
            ClearClipboard();
            return;
        }

        try
        {
            var source = AllFolders.FirstOrDefault(folder => folder.Entry.Id == entry.Id);
            if (source == null)
            {
                ClearClipboard();
                return;
            }

            if (_clipboardIsCut)
            {
                var from = entry.VirtualVolumeId;

                await _undoManager.ExecuteAsync(
                    () => MoveListedFolderAsync(entry.Id, virtualVolumeId),
                    () => MoveListedFolderAsync(entry.Id, from));

                ResetClipboard();
            }
            else
            {
                await CopyListedFolderAsync(source, virtualVolumeId, label: null);
            }
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    private void SetClipboard(RootFolderMetadata entry, bool cut)
    {
        UnmarkCut();

        _clipboardEntry = entry;
        _clipboardIsCut = cut;

        if (cut)
        {
            MarkCut(entry.Id, true);
        }

        NotifyPasteChanged();
    }

    private void ClearClipboard()
    {
        UnmarkCut();
        ResetClipboard();
    }

    // The listed row has already been rebuilt by whatever consumed the clipboard
    private void ResetClipboard()
    {
        _clipboardEntry = null;
        _clipboardIsCut = false;
        NotifyPasteChanged();
    }

    private void UnmarkCut()
    {
        if (_clipboardEntry != null && _clipboardIsCut)
        {
            MarkCut(_clipboardEntry.Id, false);
        }
    }

    private void MarkCut(Guid entryId, bool isCut)
    {
        var item = AllFolders.FirstOrDefault(folder => folder.Entry.Id == entryId);
        if (item != null && item.IsCut != isCut)
        {
            ReplaceListedFolder(item with { IsCut = isCut });
        }
    }

    private void NotifyPasteChanged()
    {
        PasteBesideFolderCommand.NotifyCanExecuteChanged();
        PasteIntoVirtualVolumeCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task EditFolder(FolderItem? item)
    {
        if (item == null)
            return;

        try
        {
            var window = Dialogs.Owner();
            if (window == null)
                return;

            var viewModel = new FolderDialogViewModel(item.Entry, item.ScannedName);
            var dialog = new Views.FolderDialogView { DataContext = viewModel };

            if (!await Dialogs.ShowAsync(dialog, window) || !viewModel.CanConfirm)
                return;

            // A name matching the scanned one is no label at all, so the folder keeps
            // following whatever the next scan finds
            var name = viewModel.FolderName.Trim();
            var label = name == item.ScannedName ? null : name;

            var before = item.Entry;

            await _undoManager.ExecuteAsync(
                () => ApplyFolderAsync(before.Id, label, viewModel.Description, viewModel.Icon, viewModel.ColorHex),
                () => ApplyFolderAsync(before.Id, before.Label, before.Description, before.Icon, before.Color));
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand]
    private async Task DuplicateFolder(FolderItem? item)
    {
        if (item == null)
            return;

        try
        {
            var window = Dialogs.Owner();
            if (window == null)
                return;

            // A duplicate shares everything with its original but the label, which is the only
            // thing that can tell the two rows apart.
            var viewModel = new NameDialogViewModel("Name for the duplicate", "Duplicate", item.Title);
            var dialog = new Views.NameDialogView { DataContext = viewModel };

            if (!await Dialogs.ShowAsync(dialog, window) || string.IsNullOrWhiteSpace(viewModel.Name))
                return;

            await CopyListedFolderAsync(item, item.Entry.VirtualVolumeId, viewModel.Name.Trim());
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand]
    private async Task DeleteFolder(FolderItem? item)
    {
        if (item == null)
            return;

        var window = Dialogs.Owner();
        if (window == null)
            return;

        var confirmation = ConfirmDeleteDialogViewModel.ForFolder(item.Title);
        var confirmDialog = new Views.ConfirmDeleteDialogView { DataContext = confirmation };

        if (!await Dialogs.ShowAsync(confirmDialog, window))
            return;

        try
        {
            await WritingAsync("Deleting folder...", (progress, token) =>
                _virtualVolumeService.RemoveFolderAsync(item.Entry.Id, progress, token));

            if (_clipboardEntry?.Id == item.Entry.Id)
            {
                ClearClipboard();
            }

            RemoveListedFolder(item.Entry.Id);

            // The scanned tree goes with the last folder pointing at it, and any step still on
            // the stack may be describing that tree
            _undoManager.Clear();
        }
        catch (OperationCanceledException)
        {
            // Rolled back with it, so the folder is still listed where it was
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand]
    private async Task Compare(FolderItem? item)
    {
        if (item == null)
            return;

        try
        {
            var window = Dialogs.Owner();
            if (window == null)
                return;

            var choices = AllFolders
                .Where(folder => folder.Entry.Id != item.Entry.Id)
                .Select(folder => new FolderChoice(folder.Entry.Id, folder.Title, folder.Path))
                .ToList();

            var target = new CompareTargetDialogViewModel(choices);
            var dialog = new Views.CompareTargetDialogView { DataContext = target };

            if (await Dialogs.ShowAsync(dialog, window))
            {
                await RunComparisonAsync(item, target, window);
            }
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    /// <summary>
    /// Reads the folder again from wherever it is now and replaces what was catalogued with it,
    /// but only once the user has seen what that would change and said so.
    /// </summary>
    [RelayCommand]
    private async Task UpdateFolder(FolderItem? item)
    {
        if (item == null)
            return;

        try
        {
            var window = Dialogs.Owner();
            if (window == null)
                return;

            var path = await FolderPicker.PickAsync(window, "Select Folder to Update From");
            if (string.IsNullOrEmpty(path))
                return;

            var scan = await ProposeUpdateAsync(item, path, window);
            if (scan == null)
                return;

            await ApplyUpdateAsync(item, scan);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    /// <summary>
    /// Scans the folder and puts the differences to the user. Returns the scan to update from,
    /// or null when the user turned it down or called it off.
    /// </summary>
    private async Task<ScanResult?> ProposeUpdateAsync(FolderItem item, string path, Window owner)
    {
        ScanResult scan;
        IReadOnlyList<ComparisonResult> differences;

        using var cancellation = new CancellationTokenSource();
        _compareCancellation = cancellation;

        var progress = new Progress<string>(message =>
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, message, true)));

        try
        {
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Reading folder...", true));

            var catalogued = await _databaseService.FindItemsAsync<FileRecord>(
                record => record.RootFolderId == item.Entry.TreeId);

            // Matching the setting the catalogued side was scanned under, or everything it left
            // out would be applied as a deletion
            scan = await _fileScannerService.ScanDirectoryAsync(
                path, progress, cancellation.Token,
                includeHiddenAndSystem: _settings.Data.ScanHiddenAndSystem);

            differences = await _folderCompareService.CompareAsync(
                item.Entry, catalogued, scan.Metadata, scan.Records,
                includeUnchanged: false, progress, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            _compareCancellation = null;
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
        }

        // Whatever the scan was refused would be applied as a deletion, so it has to be said
        // before the differences are approved rather than after
        await ScanWarning.TellIfShortAsync(scan, path);

        var proposal = new Views.CompareResultsView
        {
            DataContext = new CompareResultsViewModel(
                item.Title, item.Entry.Path, path, scan.Metadata.Path, differences, isUpdate: true)
        };

        return await Dialogs.ShowAsync(proposal, owner) ? scan : null;
    }

    private async Task ApplyUpdateAsync(FolderItem item, ScanResult scan)
    {
        var treeId = item.Entry.TreeId;

        try
        {
            RootFolderMetadata? updated = null;

            await WritingAsync("Updating folder...", async (progress, token) =>
                updated = await _virtualVolumeService.UpdateFolderContentsAsync(
                    item.Entry.Id, scan.Metadata, scan.Records, progress, token));

            var root = scan.Records.First(record => record.Id == scan.Metadata.TreeId);
            RelistTree(treeId, root, updated!);
        }
        catch (OperationCanceledException)
        {
            // Rolled back with it, so the folder still holds what it was catalogued with
        }
    }

    /// <summary>
    /// Puts the rows standing on a tree back the way it now reads. A copy or a duplicate shares
    /// the tree it was made from, so all of them are listed afresh rather than only the one the
    /// update was asked for.
    /// </summary>
    private void RelistTree(Guid treeId, FileRecord root, RootFolderMetadata updated)
    {
        var listed = AllFolders.Where(folder => folder.Entry.TreeId == treeId).ToList();

        foreach (var folder in listed)
        {
            var entry = folder.Entry.Id == updated.Id ? updated : folder.Entry;

            ReplaceListedFolder(folder with
            {
                Title = !string.IsNullOrWhiteSpace(entry.Label) ? entry.Label : root.Name,
                Subtitle = FormattingUtils.FormatBytes(root.Size),
                Path = entry.Path,
                ScannedName = root.Name,
                Entry = entry
            });
        }
    }

    private async Task CopyListedFolderAsync(FolderItem source, Guid targetVirtualVolumeId, string? label)
    {
        // Minted on the first run and put back under the same identity on a redo
        RootFolderMetadata? copy = null;

        await _undoManager.ExecuteAsync(
            execute: async () =>
            {
                if (copy == null)
                {
                    copy = await _virtualVolumeService.CopyFolderAsync(
                        source.Entry.Id, targetVirtualVolumeId, label);
                }
                else
                {
                    await _virtualVolumeService.RestoreFolderAsync(copy);
                }

                ListFolder(source with
                {
                    Entry = copy,
                    Title = label ?? source.Title,
                    IsCut = false
                });
            },
            undo: async () =>
            {
                await _virtualVolumeService.RemoveFolderAsync(copy!.Id);
                RemoveListedFolder(copy.Id);
            });
    }

    private async Task MoveListedFolderAsync(Guid entryId, Guid targetVirtualVolumeId)
    {
        await _virtualVolumeService.MoveFolderAsync(entryId, targetVirtualVolumeId);

        var item = AllFolders.FirstOrDefault(folder => folder.Entry.Id == entryId);
        if (item == null)
            return;

        RemoveListedFolder(entryId);
        ListFolder(item with
        {
            Entry = item.Entry with { VirtualVolumeId = targetVirtualVolumeId },
            IsCut = false
        });
    }

    private async Task ApplyFolderAsync(
        Guid entryId, string? label, string? description, string? icon, string? color)
    {
        var updated = await _virtualVolumeService.UpdateFolderAsync(entryId, label, description, icon, color);

        var listed = AllFolders.FirstOrDefault(folder => folder.Entry.Id == entryId);
        if (listed != null)
        {
            ReplaceListedFolder(Restyled(listed, updated));
        }
    }

    #endregion

    #region Database commands

    private async Task RunComparisonAsync(FolderItem left, CompareTargetDialogViewModel target, Window owner)
    {
        using var cancellation = new CancellationTokenSource();
        _compareCancellation = cancellation;

        var progress = new Progress<string>(message =>
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, message, true)));

        try
        {
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Reading folder...", true));

            // The whole tree is needed, root record included: the comparison is anchored on it
            var leftRecords = await _databaseService.FindItemsAsync<FileRecord>(
                record => record.RootFolderId == left.Entry.TreeId);

            RootFolderMetadata rightEntry;
            IReadOnlyCollection<FileRecord> rightRecords;
            string rightName;

            if (target.SelectedFolder != null)
            {
                var entryId = target.SelectedFolder.Id;
                var entry = (await _databaseService.FindItemsAsync<RootFolderMetadata>(
                    record => record.Id == entryId)).SingleOrDefault();

                if (entry == null)
                    return;

                rightEntry = entry;
                rightName = target.SelectedFolder.Name;
                rightRecords = await _databaseService.FindItemsAsync<FileRecord>(
                    record => record.RootFolderId == entry.TreeId);
            }
            else
            {
                // Matching the setting the catalogued side was scanned under, or everything
                // it left out would read as removed
                var scan = await _fileScannerService.ScanDirectoryAsync(
                    target.LiveFolderPath, progress, cancellation.Token,
                    includeHiddenAndSystem: _settings.Data.ScanHiddenAndSystem);

                rightEntry = scan.Metadata;
                rightName = target.LiveFolderPath;
                rightRecords = scan.Records.ToList();

                // Anything the scan was refused reads as removed against the catalogued side,
                // so the differences below are wrong rather than merely incomplete
                await ScanWarning.TellIfShortAsync(scan, target.LiveFolderPath);
            }

            var results = await _folderCompareService.CompareAsync(
                left.Entry, leftRecords, rightEntry, rightRecords,
                includeUnchanged: false, progress, cancellation.Token);

            new Views.CompareResultsView
            {
                DataContext = new CompareResultsViewModel(
                    left.Title, left.Entry.Path, rightName, rightEntry.Path, results)
            }.Show(owner);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            _compareCancellation = null;
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
        }
    }

    [RelayCommand]
    private async Task NewDatabase(Visual visual)
    {
        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is Window parentWindow)
        {
            var dialog = new Views.NewDatabaseDialogView();
            var vm = new NewDatabaseDialogViewModel();
            dialog.DataContext = vm;

            var result = await Dialogs.ShowAsync(dialog, parentWindow);
            if (result && !string.IsNullOrEmpty(vm.DatabasePath))
            {
                string dbName = !string.IsNullOrWhiteSpace(vm.CustomName)
                    ? vm.CustomName
                    : System.IO.Path.GetFileNameWithoutExtension(vm.DatabasePath);

                await _databaseService.EnsureDatabaseReadyAsync(vm.DatabasePath);
                await _databaseService.InsertItemsAsync([new DatabaseMetadata
                {
                    Name = dbName,
                    Path = vm.DatabasePath
                }]);

                _settings.AddRecentFile(vm.DatabasePath);

                WeakReferenceMessenger.Default.Send(new DatabaseReady());
                WeakReferenceMessenger.Default.Send(new HideStartPage());

                // A fresh database holds nothing to put a scanned folder in
                await NewVirtualVolume();
            }
        }
    }

    [RelayCommand]
    private async Task OpenDatabase(Visual visual)
    {
        try
        {
            var path = await DatabaseFiles.PickToOpenAsync(visual);
            if (path == null)
                return;

            await _databaseService.EnsureDatabaseReadyAsync(path);
            _settings.AddRecentFile(path);

            WeakReferenceMessenger.Default.Send(new DatabaseReady());
            WeakReferenceMessenger.Default.Send(new HideStartPage());
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand(CanExecute = nameof(HasDatabase))]
    private async Task SaveCopy(Visual visual)
    {
        try
        {
            var source = _databaseService.DbPath;
            if (string.IsNullOrEmpty(source) || !System.IO.File.Exists(source))
                return;

            var suggested = $"{System.IO.Path.GetFileNameWithoutExtension(source)} copy";
            var target = await DatabaseFiles.PickToSaveAsync(visual, suggested);

            if (target == null || string.Equals(target, source, StringComparison.OrdinalIgnoreCase))
                return;

            await _databaseService.CopyToAsync(target);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand(CanExecute = nameof(HasDatabase))]
    private async Task Export(Visual visual)
    {
        try
        {
            var source = _databaseService.DbPath;
            if (string.IsNullOrEmpty(source))
                return;

            var suggested = System.IO.Path.GetFileNameWithoutExtension(source);
            var target = await DatabaseFiles.PickJsonToSaveAsync(visual, suggested);
            if (target == null)
                return;

            var progress = new Progress<string>(message =>
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, message)));

            try
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Exporting..."));
                await _databaseTransferService.ExportAsync(target, progress);
            }
            finally
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
            }
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    /// <summary>
    /// Reads an export into a database of its own and opens it, rather than merging it into
    /// whatever is already open: two catalogues carrying the same ids cannot share a file.
    /// </summary>
    [RelayCommand]
    private async Task Import(Visual visual)
    {
        try
        {
            var json = await DatabaseFiles.PickJsonToOpenAsync(visual);
            if (json == null)
                return;

            var suggested = System.IO.Path.GetFileNameWithoutExtension(json);
            var target = await DatabaseFiles.PickToSaveAsync(visual, suggested, "Import Into");
            if (target == null)
                return;

            var progress = new Progress<string>(message =>
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, message)));

            try
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Importing..."));
                await _databaseTransferService.ImportAsync(json, target, progress);
            }
            finally
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
            }

            _settings.AddRecentFile(target);

            WeakReferenceMessenger.Default.Send(new DatabaseReady());
            WeakReferenceMessenger.Default.Send(new HideStartPage());
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    private bool HasDatabase() => !string.IsNullOrEmpty(DatabasePath);

    #endregion

    public void Receive(FolderAddedMessage message)
    {
        var item = MakeFolderItem(message.RootRecord, message.Entry);
        ListFolder(item);

        // Selecting the item is what broadcasts FolderSelectedMessage
        var node = VirtualVolumes.FirstOrDefault(n => n.Id == message.Entry.VirtualVolumeId);
        if (node != null)
        {
            node.SelectedFolder = item;
        }
    }

    public async void Receive(DatabaseReady message)
    {
        try
        {
            await LoadAsync();
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    // A load can be asked for while one is already running: opening a database broadcasts, and
    // nothing waits on the answer. Left to overlap, each would fill the list the other had
    // just emptied and the sidebar would end up listing every volume twice.
    private readonly SemaphoreSlim _loading = new(1, 1);

    /// <summary>
    /// Fills the sidebar from the open database. Separate from the message handler above, which
    /// hands back before the work is done and so cannot be waited on.
    /// </summary>
    public async Task LoadAsync()
    {
        await _loading.WaitAsync();
        try
        {
            // The recorded steps describe records in the database that is being left behind
            _undoManager.Clear();

            var query = await _databaseService.ReadItemsAsync<DatabaseMetadata>();
            var databaseMetadata = query.FirstOrDefault();
            if (databaseMetadata != null)
            {
                DatabaseName = databaseMetadata.Name;
                DatabasePath = databaseMetadata.Path;
            }

            foreach (var node in VirtualVolumes)
            {
                node.PropertyChanged -= OnVirtualVolumeNodeChanged;
            }

            ClearClipboard();
            ClearSearchTerm();
            VirtualVolumes.Clear();
            SelectedVirtualVolume = null;

            var entries = await _databaseService.ReadItemsAsync<RootFolderMetadata>();

            var folders = new List<FolderItem>();
            foreach (var entry in entries)
            {
                var rootFolderRecords =
                    await _databaseService.FindItemsAsync<FileRecord>(file => file.Id == entry.TreeId);

                var rootRecord = rootFolderRecords.SingleOrDefault();
                if (rootRecord != null)
                {
                    folders.Add(MakeFolderItem(rootRecord, entry));
                }
            }

            var virtualVolumes = await _virtualVolumeService.GetVirtualVolumesAsync();
            foreach (var record in virtualVolumes)
            {
                AddVirtualVolumeNode(record, folders);
            }

            var first = VirtualVolumes.FirstOrDefault(node => node.Folders.Count > 0);
            if (first != null)
            {
                first.SelectedFolder = first.Folders[0];
            }
            else
            {
                SelectedVirtualVolume = VirtualVolumes.FirstOrDefault();
            }
        }
        finally
        {
            _loading.Release();
        }
    }

    // Placed by name so a volume created, deleted or put back by an undo lands where the
    // sidebar already reads as sorted
    private VirtualVolumeNode AddVirtualVolumeNode(
        VirtualVolumeRecord record, IReadOnlyCollection<FolderItem> knownFolders)
    {
        var node = new VirtualVolumeNode(record, this)
        {
            IsExpanded = _settings.IsVolumeExpanded(_databaseService.DbPath, record.Id)
        };

        foreach (var folder in knownFolders.Where(item => item.Entry.VirtualVolumeId == record.Id))
        {
            node.Folders.Add(folder);
        }

        node.PropertyChanged += OnVirtualVolumeNodeChanged;

        var at = VirtualVolumes
            .TakeWhile(existing => string.Compare(
                existing.Name, record.Name, StringComparison.OrdinalIgnoreCase) <= 0)
            .Count();

        VirtualVolumes.Insert(at, node);

        return node;
    }

    private void RemoveVirtualVolumeNode(Guid virtualVolumeId)
    {
        var node = VirtualVolumes.FirstOrDefault(candidate => candidate.Id == virtualVolumeId);
        if (node == null)
            return;

        node.PropertyChanged -= OnVirtualVolumeNodeChanged;
        VirtualVolumes.Remove(node);
        _settings.ForgetVolume(_databaseService.DbPath, virtualVolumeId);

        if (SelectedFolder?.Entry.VirtualVolumeId == virtualVolumeId)
        {
            SelectedFolder = null;
        }

        if (SelectedVirtualVolume?.Id == virtualVolumeId)
        {
            SelectedVirtualVolume = VirtualVolumes.FirstOrDefault();
        }
    }

    private void ListFolder(FolderItem item)
    {
        VirtualVolumes
            .FirstOrDefault(node => node.Id == item.Entry.VirtualVolumeId)?
            .Folders.Add(item);
    }

    private void ReplaceListedFolder(FolderItem item)
    {
        foreach (var node in VirtualVolumes)
        {
            var index = node.Folders.IndexOf(
                node.Folders.FirstOrDefault(folder => folder.Entry.Id == item.Entry.Id)!);

            if (index < 0)
                continue;

            var wasSelected = ReferenceEquals(node.SelectedFolder, node.Folders[index]);
            node.Folders[index] = item;

            if (wasSelected)
            {
                node.SelectedFolder = item;
            }
        }
    }

    private void RemoveListedFolder(Guid entryId)
    {
        foreach (var node in VirtualVolumes)
        {
            var listed = node.Folders.FirstOrDefault(folder => folder.Entry.Id == entryId);
            if (listed != null)
            {
                node.Folders.Remove(listed);
            }
        }

        if (SelectedFolder?.Entry.Id == entryId)
        {
            SelectedFolder = null;
        }
    }

    private FolderItem MakeFolderItem(FileRecord rootRecord, RootFolderMetadata entry)
    {
        return new FolderItem
        {
            Title = !string.IsNullOrWhiteSpace(entry.Label) ? entry.Label : rootRecord.Name,
            Subtitle = FormattingUtils.FormatBytes(rootRecord.Size),
            Path = entry.Path,
            Description = entry.Description ?? string.Empty,
            ScannedName = rootRecord.Name,
            Entry = entry,
            Owner = this
        };
    }

    private static FolderItem Restyled(FolderItem item, RootFolderMetadata entry)
    {
        return item with
        {
            Title = !string.IsNullOrWhiteSpace(entry.Label) ? entry.Label : item.ScannedName,
            Description = entry.Description ?? string.Empty,
            Entry = entry
        };
    }

}
