using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using VVO.Core.Services;
using VVO.UI.Messages;

namespace VVO.UI.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
    , IRecipient<HideStartPage>
    , IRecipient<UpdateStatusMessage>
    , IRecipient<AddFolderMessage>
    , IRecipient<CancelRequestedMessage>
    , IRecipient<DatabaseReady>
{
    private readonly IFileScannerService _fileScannerService;
    private readonly IDatabaseService _databaseService;
    private readonly IVirtualVolumeService _virtualVolumeService;
    private readonly Settings _settings;

    [ObservableProperty]
    public partial bool IsStartPageVisible { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatabaseStatusVisible))]
    public partial bool IsStatusMessageVisible { get; set; } = false;

    [ObservableProperty]
    public partial string StatusMessage { get; set; }

    [ObservableProperty]
    public partial bool IsCancelVisible { get; set; } = false;

    [ObservableProperty]
    public partial string LastWriteText { get; set; } = string.Empty;

    // Shares its place with the running-operation status, so it only shows when nothing
    // else has that spot
    public bool IsDatabaseStatusVisible => HasDatabase && !IsStatusMessageVisible;

    /// <summary>
    /// Whether a catalogue is open. The commands that act on one are dead until it is, which is
    /// the whole of what the start page stands in front of.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDatabaseStatusVisible))]
    [NotifyCanExecuteChangedFor(nameof(ShrinkDatabaseCommand))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    [NotifyCanExecuteChangedFor(nameof(AddFolderCommand))]
    public partial bool HasDatabase { get; set; }

    public SidebarViewModel SidebarViewModel { get; }
    public VolumeExplorerViewModel VolumeExplorerViewModel { get; }
    public StartPageViewModel StartPageViewModel { get; }

    private CancellationTokenSource? _cancellationTokenSource = null;

    public MainWindowViewModel(IUndoService undoManager
        , IFileScannerService fileScannerService
        , IDatabaseService databaseService
        , IVirtualVolumeService virtualVolumeService
        , Settings settings
        , SidebarViewModel sidebarViewModel
        , StartPageViewModel startPageViewModel
        , VolumeExplorerViewModel volumeExplorerViewModel)
        : base(undoManager)
    {
        _fileScannerService = fileScannerService;
        _databaseService = databaseService;
        _virtualVolumeService = virtualVolumeService;
        _settings = settings;
        SidebarViewModel = sidebarViewModel;
        StartPageViewModel = startPageViewModel;
        VolumeExplorerViewModel = volumeExplorerViewModel;

        WeakReferenceMessenger.Default.RegisterAll(this);
        _databaseService.Modified += OnDatabaseModified;
    }

    [RelayCommand]
    private async Task ShowAbout(Visual owner)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window)
            return;

        await Dialogs.ShowAsync(new Views.AboutDialogView { DataContext = new AboutDialogViewModel() }, window);
    }

    [RelayCommand]
    private async Task ShowLicense(Visual owner)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window)
            return;

        await Dialogs.ShowAsync(
            new Views.DocumentDialogView { DataContext = DocumentDialogViewModel.License() }, window);
    }

    [RelayCommand]
    private async Task ShowShortcuts(Visual owner)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window)
            return;

        await Dialogs.ShowAsync(new Views.ShortcutsDialogView { DataContext = new ShortcutsDialogViewModel() }, window);
    }

    [RelayCommand]
    private async Task ShowOptions(Visual owner)
    {
        if (TopLevel.GetTopLevel(owner) is not Window window)
            return;

        var viewModel = new OptionsDialogViewModel(_settings);
        var dialog = new Views.OptionsDialogView { DataContext = viewModel };

        if (await Dialogs.ShowAsync(dialog, window))
        {
            viewModel.Apply();

            // The sidebar holds its own copy of the flag so the View menu can bind to it
            SidebarViewModel.ShowFolderDetailsAlways = viewModel.ShowFolderDetailsAlways;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDatabase))]
    private void Find()
    {
        WeakReferenceMessenger.Default.Send(new FocusFileSearchMessage());
    }

    [RelayCommand]
    private void Exit()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }

    [RelayCommand(CanExecute = nameof(HasDatabase))]
    private async Task ShrinkDatabaseAsync()
    {
        var path = _databaseService.DbPath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        long before;
        long after;

        using var cts = new CancellationTokenSource();
        _cancellationTokenSource = cts;

        var progressReporter = new Progress<string>(message =>
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(true, message, IsCancellable: true)));

        try
        {
            WeakReferenceMessenger.Default.Send(
                new UpdateStatusMessage(true, "Shrinking database...", IsCancellable: true));

            before = new FileInfo(path).Length;
            await _databaseService.ShrinkDatabaseAsync(progressReporter, cts.Token);
            after = new FileInfo(path).Length;
        }
        catch (OperationCanceledException)
        {
            // Stopping was asked for, and nothing was lost by stopping
            return;
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
            return;
        }
        finally
        {
            _cancellationTokenSource = null;
            WeakReferenceMessenger.Default.Send(new UpdateStatusMessage(false, "Done"));
        }

        var reclaimed = before - after;
        var message = reclaimed > 0
            ? $"{FormattingUtils.FormatBytes(reclaimed)} reclaimed. The database is now {FormattingUtils.FormatBytes(after)}."
            : $"Nothing to reclaim. The database is {FormattingUtils.FormatBytes(after)}.";

        // Left behind by the shrink itself, and nothing else will ever mention it
        var backup = _databaseService.ShrinkBackupPath();
        if (backup != null)
        {
            message += $"\n\nThe database as it was before the shrink was kept beside it as "
                       + $"'{Path.GetFileName(backup)}' ({FormattingUtils.FormatBytes(new FileInfo(backup).Length)}). "
                       + "Delete it once you are satisfied with the result.";
        }

        await Dialogs.TellAsync("Shrink Database", message);
    }

    // Broadcast rather than cancelled directly, so that whichever view model owns the
    // running operation stops it. This one answers for its own scan below.
    [RelayCommand]
    private void Cancel()
    {
        WeakReferenceMessenger.Default.Send(new CancelRequestedMessage());
    }

    public void Receive(CancelRequestedMessage message)
    {
        _cancellationTokenSource?.Cancel();
    }

    // The menu bar has no virtual volume to point at, so it falls back on whichever one the
    // sidebar is standing in.
    [RelayCommand(CanExecute = nameof(HasDatabase))]
    private async Task AddFolderAsync(Visual mainWindow)
    {
        var targetVolumeId = SidebarViewModel.TargetVirtualVolumeId;
        if (targetVolumeId == null)
        {
            await Dialogs.TellAsync("Add Folder", "There are no virtual volumes yet. Create one first.");
            return;
        }

        await ScanIntoAsync(mainWindow, targetVolumeId.Value);
    }

    private async Task ScanIntoAsync(Visual mainWindow, Guid virtualVolumeId)
    {
        var drivePath = await FolderPicker.PickAsync(mainWindow, "Select Folder to Scan");
        if (string.IsNullOrEmpty(drivePath))
            return;

        using var cts = new CancellationTokenSource();
        _cancellationTokenSource = cts;
        var progressReporter = new Progress<string>(message =>
        {
            StatusMessage = message;
        });

        try
        {
            IsStatusMessageVisible = true;
            IsCancelVisible = true;
            
            var scan = await _fileScannerService.ScanDirectoryAsync(
                drivePath, progressReporter, cts.Token,
                includeHiddenAndSystem: _settings.Data.ScanHiddenAndSystem);
            (progressReporter as IProgress<string>).Report("Folder scan complete. Saving to database...");
            var readonlyFileRecordList = scan.Records.ToImmutableList();

            var entry = await _virtualVolumeService.AddFolderAsync(
                virtualVolumeId, scan.Metadata, readonlyFileRecordList, progressReporter, cts.Token);

            var rootFileRecord = readonlyFileRecordList.First(f => f.Id == entry.TreeId);
            WeakReferenceMessenger.Default.Send(new FolderAddedMessage(entry, rootFileRecord));

            // The catalogue is short of whatever was refused, and nothing else would say so
            await ScanWarning.TellIfShortAsync(scan, drivePath);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsStatusMessageVisible = false;
            IsCancelVisible = false;
            _cancellationTokenSource = null;
        }
    }

    public void Receive(HideStartPage message)
    {
        IsStartPageVisible = false;
    }

    public void Receive(UpdateStatusMessage message)
    {
        IsStatusMessageVisible = message.IsVisible;
        IsCancelVisible = message.IsVisible && message.IsCancellable;
        if (message.IsVisible)
        {
            StatusMessage = message.Message;
        }
    }

    // The write itself reports in, on whichever background thread carried it out
    private void OnDatabaseModified(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(RefreshLastWrite);
    }

    public void Receive(DatabaseReady message)
    {
        RefreshLastWrite();
    }

    private void RefreshLastWrite()
    {
        var path = _databaseService.DbPath;
        HasDatabase = !string.IsNullOrEmpty(path) && File.Exists(path);

        LastWriteText = HasDatabase
            ? $"Last Write {File.GetLastWriteTime(path):yyyy-MM-dd HH:mm:ss}"
            : string.Empty;
    }

    public async void Receive(AddFolderMessage message)
    {
        try
        {
            await ScanIntoAsync(message.Visual, message.VirtualVolumeId);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }
}