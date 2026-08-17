using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.UI.Messages;

namespace VVO.UI.ViewModels;

public record RecentDatabase(string Name, string Path, bool IsMissing)
{
    public string Tooltip => IsMissing ? $"{Path} — no longer there" : Path;
}

public partial class StartPageViewModel: ViewModelBase
{
    private readonly Settings _settings;
    private readonly IDatabaseService _databaseService;

    public AvaloniaList<RecentDatabase> RecentDatabases { get; } = new();

    public bool HaveRecentFiles => RecentDatabases.Count > 0;

    public StartPageViewModel(IUndoService undoManager
        , Settings settings
        , IDatabaseService databaseService)
        : base(undoManager)
    {
        _settings = settings;
        _databaseService = databaseService;

        RefreshRecentDatabases();
    }

    [RelayCommand]
    private async Task NewDatabase(Visual startPage)
    {
        var topLevel = TopLevel.GetTopLevel(startPage);
        if (topLevel == null)
            return;

        if (topLevel is Window parentWindow)
        {
            var dialog = new Views.NewDatabaseDialogView();
            var vm = new NewDatabaseDialogViewModel();
            dialog.DataContext = vm;

            var result = await Dialogs.ShowAsync(dialog, parentWindow);
            if (result && !string.IsNullOrEmpty(vm.DatabasePath))
            {
                await _databaseService.EnsureDatabaseReadyAsync(vm.DatabasePath);
                await _databaseService.InsertItemsAsync([new DatabaseMetadata()
                    {
                        Name = vm.CustomName,
                        Path = vm.DatabasePath
                    }
                ]);

                Opened(vm.DatabasePath);
            }
        }
    }

    [RelayCommand]
    private async Task OpenDatabase(Visual startPage)
    {
        var path = await DatabaseFiles.PickToOpenAsync(startPage);
        if (path == null)
            return;

        await _databaseService.EnsureDatabaseReadyAsync(path);
        Opened(path);
    }

    [RelayCommand]
    private void RemoveRecent(RecentDatabase? recent)
    {
        if (recent == null)
            return;

        _settings.RemoveRecentFile(recent.Path);
        RefreshRecentDatabases();
    }

    [RelayCommand]
    private async Task OpenRecent(RecentDatabase? recent)
    {
        if (recent == null)
            return;

        // The list is only as fresh as the last refresh, so the file can still be gone
        if (!File.Exists(recent.Path))
        {
            await Dialogs.TellAsync("Open Database", $"'{recent.Path}' is no longer there.", Icon.Warning);

            _settings.RemoveRecentFile(recent.Path);
            RefreshRecentDatabases();
            return;
        }

        await _databaseService.EnsureDatabaseReadyAsync(recent.Path);
        Opened(recent.Path);
    }

    /// <summary>
    /// Opens the catalogue the application was started on, which is how a double-clicked .vvo
    /// arrives. A path that cannot be opened is reported: the alternative is a double-click that
    /// leaves the start page up with no word of why.
    /// </summary>
    public async Task OpenAtStartupAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            if (!File.Exists(path))
            {
                await Dialogs.TellAsync("Open Database", $"'{path}' is no longer there.", Icon.Warning);
                return;
            }

            await _databaseService.EnsureDatabaseReadyAsync(path);
            Opened(path);
        }
        catch (Exception e)
        {
            await Logger.ShowErrorAsync(e);
        }
    }

    private void Opened(string path)
    {
        _settings.AddRecentFile(path);
        RefreshRecentDatabases();

        WeakReferenceMessenger.Default.Send(new DatabaseReady());
        WeakReferenceMessenger.Default.Send(new HideStartPage());
    }

    private void RefreshRecentDatabases()
    {
        RecentDatabases.Clear();

        foreach (var path in _settings.Data.RecentFiles)
        {
            RecentDatabases.Add(new RecentDatabase(
                Path.GetFileNameWithoutExtension(path), path, !File.Exists(path)));
        }

        OnPropertyChanged(nameof(HaveRecentFiles));
    }
}
