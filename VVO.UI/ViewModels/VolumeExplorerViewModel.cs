using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Collections;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.UI.Messages;

namespace VVO.UI.ViewModels;

public record FileItem
{
    public required Guid Id { get; init; }
    public required bool IsFolder { get; init; }
    public required string IconKey { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Size { get; init; } = string.Empty;
    public long SizeSortPath { get; init; }
    public string Modified { get; init; } = string.Empty;
    public DateTime ModifiedSortPath { get; init; }
    public string Created { get; init; } = string.Empty;
    public DateTime CreatedSortPath { get; init; }
    public string Extension { get; init; } = string.Empty;

    // Path of the containing folder, shown only when the list is not a single folder
    public string Location { get; init; } = string.Empty;

    // Full catalogue path of this entry, headed by the volume the way a drive letter heads one
    public string VirtualPath { get; init; } = string.Empty;

    // Full path on disk, empty while the tree this came from was listed without a scanned path
    public string PhysicalPath { get; init; } = string.Empty;

    // Both paths where there are both: the column trims the name, so hovering is the only way
    // to read either one in full
    public string PathTip => PhysicalPath.Length == 0
        ? VirtualPath
        : $"{VirtualPath}{Environment.NewLine}{PhysicalPath}";

    public Geometry? Icon => FileIcons.Lookup(IconKey);
    public bool IsIconFlipped => FileIcons.IsFlipped(IconKey);
}

public record BreadcrumbItem
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>
/// What heads the paths of one scanned tree: the virtual volume holding it, which stands in
/// for the drive, the name the folder itself is listed under, and the path it was scanned from.
/// </summary>
public record TreeLabel(string VolumeName, string FolderName, string RootPath = "");

public partial class VolumeExplorerViewModel : ViewModelBase
    , IRecipient<FolderSelectedMessage>
    , IRecipient<SearchAllMessage>
    , IRecipient<CancelRequestedMessage>
{
    private const int SearchDelayMilliseconds = 300;

    private readonly IDatabaseService _databaseService;
    private readonly IVirtualVolumeService _virtualVolumeService;
    private readonly IFileScannerService _fileScannerService;
    private readonly Settings _settings;

    // Folder records of the current volume, fetched on the first search and reused afterwards.
    // Only folders are held: a file can never be part of another entry's location.
    private readonly Dictionary<Guid, FileRecord> _folders = new();

    // Locations already assembled from _folders, filled in as results ask for them
    private readonly Dictionary<Guid, string> _paths = new();

    // The same, on disk. Kept apart because a tree can be listed without a scanned path.
    private readonly Dictionary<Guid, string> _physicalPaths = new();

    // What each tree is listed under in the sidebar, which is what a location starts with
    private readonly Dictionary<Guid, TreeLabel> _rootLabels = new();

    // The trees a search covers, which are the ones the sidebar is listing
    private readonly HashSet<Guid> _liveTrees = new();

    private Guid _treeId;
    private bool _foldersCoverEveryTree;
    private bool _suppressSearchReload;

    // Raised by every navigation, so a query that a later one has overtaken can be dropped
    // instead of writing stale contents over the folder the user has moved on to.
    private int _navigationToken;

    // Separately raised so that a keystroke still waiting out its delay can be abandoned
    private int _searchToken;

    // The term a search-all listing was built from, so a delete can put the hits back
    private string _everywhereTerm = string.Empty;

    private CancellationTokenSource? _writeCancellation;

    [ObservableProperty]
    public partial AvaloniaList<FileItem>? Files { get; set; }

    // What the copy and delete commands act on, set by the grid as the selection moves
    [ObservableProperty]
    public partial FileItem? SelectedFile { get; set; }

    public AvaloniaList<FileItem> SelectedFiles { get; } = new();

    [ObservableProperty]
    public partial string ItemSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string SearchTerm { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsFlatMode { get; set; }

    // Stands where a drive letter would, ahead of the crumbs and outside them: there is no
    // folder behind it to navigate to
    [ObservableProperty]
    public partial string VolumePrefix { get; set; } = string.Empty;

    // Root folder first, current folder last
    public AvaloniaList<BreadcrumbItem> Breadcrumbs { get; } = new();

    public VolumeExplorerViewModel(IUndoService undoManager
        , IDatabaseService databaseService
        , IVirtualVolumeService virtualVolumeService
        , IFileScannerService fileScannerService
        , Settings settings)
        : base(undoManager)
    {
        _databaseService = databaseService;
        _virtualVolumeService = virtualVolumeService;
        _fileScannerService = fileScannerService;
        _settings = settings;
        WeakReferenceMessenger.Default.RegisterAll(this);

        SelectedFiles.CollectionChanged += (_, _) => NotifySelectionCommands();
    }

    public async void Receive(FolderSelectedMessage message)
    {
        try
        {
            await ShowFolderAsync(message);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    public async void Receive(SearchAllMessage message)
    {
        try
        {
            await SearchAllAsync(message);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    /// <summary>
    /// Shows the tree of the selected folder. Separate from the message handler above, which
    /// hands back before the work is done and so cannot be waited on.
    /// </summary>
    public async Task ShowFolderAsync(FolderSelectedMessage message)
    {
        _treeId = message.TreeId;
        _rootLabels[message.TreeId] = new TreeLabel(message.VolumeName, message.Name, message.RootPath);
        _folders.Clear();
        _paths.Clear();
        _physicalPaths.Clear();
        _foldersCoverEveryTree = false;
        ClearSearch();

        await NavigateAsync([new BreadcrumbItem { Id = message.TreeId, Name = message.Name }]);
    }

    /// <summary>
    /// Lists what every folder in the given scopes holds matching the term. An empty term calls
    /// the search off and goes back to the folder the user came from.
    /// </summary>
    public async Task SearchAllAsync(SearchAllMessage message)
    {
        _liveTrees.Clear();
        foreach (var scope in message.Scopes)
        {
            _liveTrees.Add(scope.TreeId);
            _rootLabels[scope.TreeId] = new TreeLabel(scope.VolumeName, scope.Name, scope.RootPath);
        }

        if (string.IsNullOrWhiteSpace(message.Term))
        {
            // The path was left untouched while searching, so the folder the user came
            // from is still there to go back to
            if (Breadcrumbs.Count > 0)
            {
                await NavigateAsync(Breadcrumbs.ToList());
            }

            return;
        }

        // This search covers everything the explorer's own box searches within
        ClearSearch();

        await SearchEverywhereAsync(message.Term);
    }

    [RelayCommand]
    private async Task OpenItemAsync(FileItem? item)
    {
        if (item is not { IsFolder: true })
            return;

        // A hit from a search lives somewhere else, so its path has to be rebuilt rather than
        // appended to where the user happens to be standing.
        var path = IsFlatMode
            ? BuildPathTo(item.Id)
            : Breadcrumbs.Append(new BreadcrumbItem { Id = item.Id, Name = item.Name }).ToList();

        if (path.Count == 0)
            return;

        // A search covering every folder can land the user in a tree other than the one the
        // sidebar has selected, and that becomes the tree the explorer is standing in
        _treeId = path[0].Id;

        ClearSearch();
        await NavigateAsync(path);
    }

    #region Copying paths

    /// <summary>
    /// The catalogue path of the selection, and the same for everything the grid is listing.
    /// The physical pair of each is empty for a tree that was listed without a scanned path,
    /// which is what leaves the two physical commands disabled.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopy))]
    private Task CopyAsync() => TextClipboard.WriteAsync(SelectedLines(file => file.VirtualPath));

    private bool CanCopy() => Selection().Count > 0;

    [RelayCommand(CanExecute = nameof(CanCopyAll))]
    private Task CopyAllAsync() => TextClipboard.WriteAsync(Lines(file => file.VirtualPath));

    private bool CanCopyAll() => Files is { Count: > 0 };

    [RelayCommand(CanExecute = nameof(CanCopyPhysical))]
    private Task CopyPhysicalAsync() => TextClipboard.WriteAsync(SelectedLines(file => file.PhysicalPath));

    private bool CanCopyPhysical() => Selection().Any(file => file.PhysicalPath.Length > 0);

    [RelayCommand(CanExecute = nameof(CanCopyPhysicalAll))]
    private Task CopyPhysicalAllAsync() => TextClipboard.WriteAsync(Lines(file => file.PhysicalPath));

    private bool CanCopyPhysicalAll() => Files?.Any(file => file.PhysicalPath.Length > 0) == true;

    private string Lines(Func<FileItem, string> path)
    {
        return Files == null
            ? string.Empty
            : string.Join(Environment.NewLine, Files
                .Select(path)
                .Where(value => value.Length > 0));
    }

    private string SelectedLines(Func<FileItem, string> path)
    {
        return string.Join(Environment.NewLine, Selection()
            .Select(path)
            .Where(value => value.Length > 0));
    }

    /// <summary>
    /// The grid's selection, or the single row tests and the SelectedItem binding set when
    /// the multi-select list has not been filled.
    /// </summary>
    private IReadOnlyList<FileItem> Selection()
    {
        return SelectedFiles.Count > 0
            ? SelectedFiles
            : SelectedFile == null ? [] : [SelectedFile];
    }

    /// <summary>
    /// Replaces the rows the copy and delete commands act on. The view pushes the grid's
    /// selection here because SelectedItems cannot be bound.
    /// </summary>
    public void ReplaceSelection(IReadOnlyList<FileItem> items)
    {
        SelectedFiles.Clear();
        SelectedFiles.AddRange(items);
    }

    // The grid drives the selection and reloads replace the list, so every copy command has to
    // be told when either moves
    partial void OnSelectedFileChanged(FileItem? value)
    {
        NotifySelectionCommands();
    }

    private void NotifySelectionCommands()
    {
        CopyCommand.NotifyCanExecuteChanged();
        CopyPhysicalCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilesChanged(AvaloniaList<FileItem>? value)
    {
        CopyAllCommand.NotifyCanExecuteChanged();
        CopyPhysicalAllCommand.NotifyCanExecuteChanged();
    }

    #endregion

    #region Deleting

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var items = Selection().ToList();
        if (items.Count == 0)
            return;

        var window = Dialogs.Owner();
        if (window == null)
            return;

        var confirmation = items.Count == 1
            ? items[0].IsFolder
                ? ConfirmDeleteDialogViewModel.ForCatalogueFolder(items[0].Name)
                : ConfirmDeleteDialogViewModel.ForCatalogueFile(items[0].Name)
            : ConfirmDeleteDialogViewModel.ForCatalogueItems(items.Count, items.Any(item => item.IsFolder));

        var confirmDialog = new Views.ConfirmDeleteDialogView { DataContext = confirmation };
        if (!await Dialogs.ShowAsync(confirmDialog, window))
            return;

        try
        {
            IReadOnlyList<FileRecord> roots = [];

            await WritingAsync("Deleting...", async (progress, token) =>
                roots = await _virtualVolumeService.RemoveRecordsAsync(
                    items.Select(item => item.Id).ToList(), progress, token));

            var deleted = items.Select(item => item.Id).ToHashSet();
            if (items.Any(item => item.IsFolder))
            {
                _folders.Clear();
                _paths.Clear();
                _physicalPaths.Clear();
                _foldersCoverEveryTree = false;
            }

            foreach (var root in roots)
            {
                WeakReferenceMessenger.Default.Send(new TreeContentsChangedMessage(root));
            }

            await RefreshAfterDeleteAsync(deleted);
        }
        catch (OperationCanceledException)
        {
            // Rolled back with it, so the files are still listed where they were
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    private bool CanDelete() => Selection().Count > 0;

    public void Receive(CancelRequestedMessage message)
    {
        _writeCancellation?.Cancel();
    }

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

    private async Task RefreshAfterDeleteAsync(HashSet<Guid> deleted)
    {
        if (IsFlatMode)
        {
            if (!string.IsNullOrWhiteSpace(SearchTerm))
            {
                await SearchAsync(SearchTerm);
                return;
            }

            if (_everywhereTerm.Length > 0)
            {
                await SearchEverywhereAsync(_everywhereTerm);
                return;
            }
        }

        var remaining = Breadcrumbs.TakeWhile(crumb => !deleted.Contains(crumb.Id)).ToList();
        if (remaining.Count == 0 && Breadcrumbs.Count > 0)
        {
            remaining = [Breadcrumbs[0]];
        }

        if (remaining.Count > 0)
        {
            await NavigateAsync(remaining);
        }
    }

    #endregion

    #region Adding

    [RelayCommand(CanExecute = nameof(CanAddToCurrentFolder))]
    private async Task AddFoldersAsync()
    {
        if (!CanAddToCurrentFolder())
            return;

        var window = Dialogs.Owner();
        if (window == null)
            return;

        var paths = await FolderPicker.PickManyAsync(window, "Select Folder(s) to Add");
        if (paths.Count == 0)
            return;

        try
        {
            var parentId = Breadcrumbs[^1].Id;
            var records = new List<FileRecord>();
            var scans = new List<(ScanResult Scan, string Path)>();

            using (var cancellation = new CancellationTokenSource())
            {
                _writeCancellation = cancellation;
                var progress = new Progress<string>(message =>
                    WeakReferenceMessenger.Default.Send(
                        new UpdateStatusMessage(true, message, IsCancellable: true)));

                WeakReferenceMessenger.Default.Send(
                    new UpdateStatusMessage(true, "Scanning folders...", IsCancellable: true));

                try
                {
                    foreach (var path in paths)
                    {
                        var scan = await _fileScannerService.ScanDirectoryAsync(
                            path, progress, cancellation.Token,
                            includeHiddenAndSystem: _settings.Data.ScanHiddenAndSystem);
                        scans.Add((scan, path));
                        records.AddRange(Graft(scan.Records, parentId, _treeId));
                    }
                }
                finally
                {
                    _writeCancellation = null;
                    WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
                }
            }

            foreach (var (scan, path) in scans)
            {
                await ScanWarning.TellIfShortAsync(scan, path);
            }

            await CommitAddedAsync(parentId, records, "Adding folders...");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    [RelayCommand(CanExecute = nameof(CanAddToCurrentFolder))]
    private async Task AddFilesAsync()
    {
        if (!CanAddToCurrentFolder())
            return;

        var window = Dialogs.Owner();
        if (window == null)
            return;

        var paths = await DiskFiles.PickManyAsync(window, "Select File(s) to Add");
        if (paths.Count == 0)
            return;

        try
        {
            var parentId = Breadcrumbs[^1].Id;
            var records = new List<FileRecord>(paths.Count);

            foreach (var path in paths)
            {
                var record = await _fileScannerService.ReadFileAsync(path);
                records.Add(record with { ParentId = parentId, RootFolderId = _treeId });
            }

            await CommitAddedAsync(parentId, records, "Adding files...");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    private bool CanAddToCurrentFolder() => Breadcrumbs.Count > 0 && !IsFlatMode;

    private void NotifyAddCommands()
    {
        AddFoldersCommand.NotifyCanExecuteChanged();
        AddFilesCommand.NotifyCanExecuteChanged();
    }

    private static IEnumerable<FileRecord> Graft(
        IReadOnlyList<FileRecord> scanned, Guid parentId, Guid treeId)
    {
        return scanned.Select(record => record with
        {
            RootFolderId = treeId,
            ParentId = record.ParentId == null ? parentId : record.ParentId
        });
    }

    private async Task CommitAddedAsync(
        Guid parentId, IReadOnlyCollection<FileRecord> records, string status)
    {
        AddRecordsResult? result = null;

        await WritingAsync(status, async (progress, token) =>
            result = await _virtualVolumeService.AddRecordsAsync(parentId, records, progress, token));

        if (result == null)
            return;

        if (records.Any(record => record.IsFolder))
        {
            _folders.Clear();
            _paths.Clear();
            _physicalPaths.Clear();
            _foldersCoverEveryTree = false;
        }

        WeakReferenceMessenger.Default.Send(new TreeContentsChangedMessage(result.Root));

        if (result.Skipped.Count > 0)
        {
            var names = string.Join(", ", result.Skipped.Select(name => $"'{name}'"));
            await Dialogs.TellAsync(
                "Some items were not added",
                $"{names} already exist in this folder and were left out.");
        }

        await NavigateAsync(Breadcrumbs.ToList());
    }

    #endregion

    [RelayCommand(CanExecute = nameof(CanNavigateUp))]
    private async Task NavigateUpAsync()
    {
        if (!CanNavigateUp())
            return;

        await NavigateAsync(Breadcrumbs.Take(Breadcrumbs.Count - 1).ToList());
    }

    private bool CanNavigateUp() => Breadcrumbs.Count > 1;

    [RelayCommand]
    private async Task NavigateToAsync(Guid folderId)
    {
        var target = Breadcrumbs.FirstOrDefault(crumb => crumb.Id == folderId);
        if (target == null || target == Breadcrumbs[^1])
            return;

        await NavigateAsync(Breadcrumbs
            .TakeWhile(crumb => crumb.Id != folderId)
            .Append(target)
            .ToList());
    }

    partial void OnSearchTermChanged(string value)
    {
        if (_suppressSearchReload)
            return;

        _ = SearchAfterDelayAsync(value);
    }

    private async Task SearchAfterDelayAsync(string term)
    {
        var token = ++_searchToken;

        try
        {
            await Task.Delay(SearchDelayMilliseconds);
            if (token != _searchToken)
                return;

            if (string.IsNullOrWhiteSpace(term))
            {
                // The path was left untouched while searching, so the folder the user came
                // from is still there to go back to.
                if (Breadcrumbs.Count > 0)
                {
                    await NavigateAsync(Breadcrumbs.ToList());
                }

                return;
            }

            await SearchAsync(term);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    private async Task SearchAsync(string term)
    {
        _everywhereTerm = string.Empty;
        var treeId = _treeId;
        var token = ++_navigationToken;

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Searching volume"));

        try
        {
            await EnsureFoldersLoadedAsync(treeId);

            var records = await _databaseService.FindItemsAsync<FileRecord>(record =>
                record.RootFolderId == treeId && record.Id != treeId && record.Name.Contains(term));

            if (token != _navigationToken)
                return;

            ApplyRecords(records, flat: true);
        }
        finally
        {
            if (token == _navigationToken)
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
            }
        }
    }

    private async Task SearchEverywhereAsync(string term)
    {
        _everywhereTerm = term;
        var token = ++_navigationToken;

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Searching every folder"));

        try
        {
            await EnsureEveryFolderLoadedAsync();

            // The name is indexed but a substring match cannot use that index, so this is a
            // scan either way and the trees are sorted out afterwards
            var records = await _databaseService.FindItemsAsync<FileRecord>(record =>
                record.ParentId != null && record.Name.Contains(term));

            if (token != _navigationToken)
                return;

            ApplyRecords(
                records.Where(record => _liveTrees.Contains(record.RootFolderId)).ToList(),
                flat: true);
        }
        finally
        {
            if (token == _navigationToken)
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
            }
        }
    }

    /// <summary>
    /// Loads the folder at the end of the given path and, once the contents are in hand,
    /// adopts that path as the current location.
    /// </summary>
    private async Task NavigateAsync(IReadOnlyList<BreadcrumbItem> path)
    {
        _everywhereTerm = string.Empty;
        var folderId = path[^1].Id;
        var token = ++_navigationToken;

        WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, "Reading files from database"));

        try
        {
            var records = await _databaseService.FindItemsAsync<FileRecord>(record => record.ParentId == folderId);
            if (token != _navigationToken)
                return;

            // Applied together so a superseded navigation can never leave the breadcrumb
            // describing one folder while the grid shows another.
            Breadcrumbs.Clear();
            Breadcrumbs.AddRange(path);

            var known = _rootLabels.TryGetValue(path[0].Id, out var label);
            VolumePrefix = known ? CataloguePath.Root(label!.VolumeName) : string.Empty;

            NavigateUpCommand.NotifyCanExecuteChanged();
            NotifyAddCommands();

            // The crumbs already spell out where we are, so the folder holding these records
            // needs no walk back up through the folder records to name it
            var virtualFolder = path.Aggregate(
                CataloguePath.RootPath(label?.VolumeName ?? string.Empty),
                (parent, crumb) => CataloguePath.Combine(parent, crumb.Name));

            var physicalFolder = path
                .Skip(1)
                .Aggregate(
                    label?.RootPath ?? string.Empty,
                    (parent, crumb) => JoinPhysical(parent, crumb.Name));

            ApplyRecords(records, flat: false, virtualFolder, physicalFolder);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
        finally
        {
            if (token == _navigationToken)
            {
                WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
            }
        }
    }

    private void ApplyRecords(
        IReadOnlyCollection<FileRecord> records,
        bool flat,
        string virtualFolder = "",
        string physicalFolder = "")
    {
        IsFlatMode = flat;
        SelectedFile = null;
        SelectedFiles.Clear();
        NotifyAddCommands();

        Files = new AvaloniaList<FileItem>(records
            .OrderByDescending(record => record.IsFolder)
            .ThenBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
            // Hits from a search are scattered, so each one is placed by its own parent rather
            // than by the one folder the rest of the list shares
            .Select(record => flat
                ? ToFileItem(record, GetPath(record.ParentId), GetPhysicalPath(record.ParentId), flat: true)
                : ToFileItem(record, virtualFolder, physicalFolder, flat: false)));

        if (flat)
        {
            // Hits are scattered across the volume, so totalling their sizes would count a
            // folder and the files inside it twice over.
            ItemSummary = $"{records.Count} {(records.Count == 1 ? "match" : "matches")}";
            return;
        }

        // Every child folder already carries the total of everything beneath it, so the direct
        // children add up to the whole subtree without a second query.
        var total = FormattingUtils.FormatBytes(records.Sum(record => record.Size));
        ItemSummary = $"{records.Count} {(records.Count == 1 ? "item" : "items")} · {total}";
    }

    private async Task EnsureFoldersLoadedAsync(Guid treeId)
    {
        if (_folders.Count > 0)
            return;

        var folders = await _databaseService.FindItemsAsync<FileRecord>(
            record => record.RootFolderId == treeId && record.IsFolder);

        foreach (var folder in folders)
        {
            _folders[folder.Id] = folder;
        }
    }

    // Folders are a small share of a catalogue, so holding every one of them is what makes a
    // location cheap to assemble for a hit in any tree
    private async Task EnsureEveryFolderLoadedAsync()
    {
        if (_foldersCoverEveryTree)
            return;

        var folders = await _databaseService.FindItemsAsync<FileRecord>(record => record.IsFolder);

        _paths.Clear();
        foreach (var folder in folders)
        {
            _folders[folder.Id] = folder;
        }

        _foldersCoverEveryTree = true;
    }

    private string GetPath(Guid? folderId)
    {
        if (folderId == null)
            return string.Empty;

        if (_paths.TryGetValue(folderId.Value, out var cached))
            return cached;

        if (!_folders.TryGetValue(folderId.Value, out var folder))
            return string.Empty;

        var label = RootLabel(folder);

        // The volume stands in for the drive and the folder itself is the first step below it
        var path = folder.ParentId == null
            ? CataloguePath.Combine(CataloguePath.RootPath(label.VolumeName), label.FolderName)
            : CataloguePath.Combine(GetPath(folder.ParentId), folder.Name);

        _paths[folderId.Value] = path;
        return path;
    }

    private string GetPhysicalPath(Guid? folderId)
    {
        if (folderId == null)
            return string.Empty;

        if (_physicalPaths.TryGetValue(folderId.Value, out var cached))
            return cached;

        if (!_folders.TryGetValue(folderId.Value, out var folder))
            return string.Empty;

        // The root of the tree is the scanned path itself; everything below hangs off it
        var path = folder.ParentId == null
            ? RootLabel(folder).RootPath
            : JoinPhysical(GetPhysicalPath(folder.ParentId), folder.Name);

        _physicalPaths[folderId.Value] = path;
        return path;
    }

    // A tree is headed by what the sidebar shows, not by the name it was scanned under
    private TreeLabel RootLabel(FileRecord root)
    {
        return _rootLabels.TryGetValue(root.Id, out var label)
            ? label
            : new TreeLabel(string.Empty, root.Name);
    }

    private List<BreadcrumbItem> BuildPathTo(Guid folderId)
    {
        var path = new List<BreadcrumbItem>();
        Guid? current = folderId;

        while (current != null && _folders.TryGetValue(current.Value, out var folder))
        {
            path.Insert(0, new BreadcrumbItem
            {
                Id = folder.Id,

                // The root is listed under the name the sidebar shows, which may be a label
                // the user chose rather than the scanned folder name.
                Name = folder.ParentId == null ? RootLabel(folder).FolderName : folder.Name
            });

            current = folder.ParentId;
        }

        return path;
    }

    private void ClearSearch()
    {
        _suppressSearchReload = true;
        SearchTerm = string.Empty;
        _suppressSearchReload = false;

        // Abandons any keystroke still waiting out its delay
        _searchToken++;
    }

    // The extension has a column of its own. A folder keeps whatever dots are in its name,
    // and so does a file that is nothing but an extension, such as .gitignore, which would
    // otherwise be left with no name at all.
    private static string NameWithoutExtension(FileRecord record)
    {
        if (record.IsFolder)
            return record.Name;

        var stem = Path.GetFileNameWithoutExtension(record.Name);
        return stem.Length > 0 ? stem : record.Name;
    }

    private static FileItem ToFileItem(
        FileRecord record, string virtualFolder, string physicalFolder, bool flat)
    {
        // Without the leading dot: the column header already says what these are
        var extension = record.IsFolder ? string.Empty : Path.GetExtension(record.Name).TrimStart('.');

        return new FileItem
        {
            Id = record.Id,
            IsFolder = record.IsFolder,
            IconKey = FileIcons.KeyFor(extension, record.IsFolder),
            Name = NameWithoutExtension(record),
            Size = FormattingUtils.FormatBytes(record.Size),
            SizeSortPath = record.Size,
            Modified = FormattingUtils.FormatTimestamp(record.Modified),
            ModifiedSortPath = record.Modified,
            Created = FormattingUtils.FormatTimestamp(record.Created),
            CreatedSortPath = record.Created,
            Extension = extension,

            // Only a scattered list has to say where each entry came from
            Location = flat ? virtualFolder : string.Empty,

            // Built from the record's own name: the column shows it without its extension
            VirtualPath = CataloguePath.Combine(virtualFolder, record.Name),
            PhysicalPath = JoinPhysical(physicalFolder, record.Name)
        };
    }

    // An unknown root leaves everything below it unknown too, rather than rooting the path
    // somewhere it never was
    private static string JoinPhysical(string parent, string name)
    {
        return parent.Length == 0 ? string.Empty : Path.Combine(parent, name);
    }
}
