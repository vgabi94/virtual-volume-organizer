using Avalonia.Controls;
using CommunityToolkit.Mvvm.Messaging;
using Avalonia.Threading;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.UI;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// A database of its own, a window to open dialogs over, and the seams put back afterwards.
/// The window is real: it is what the commands under test show their dialogs on.
/// </summary>
public abstract class UiTestBase : IDisposable
{
    private readonly string _dbPath;
    private readonly string _settingsPath;
    private readonly List<string> _spilled = [];

    protected DatabaseService Database { get; }
    protected VirtualVolumeService Volumes { get; }
    protected DatabaseTransferService Transfer { get; }
    protected UndoService Undo { get; }
    protected Settings Settings { get; }
    protected Window Shell { get; }

    /// <summary>Dialogs answered during the test, in the order they were opened.</summary>
    protected List<Window> Opened { get; } = [];

    /// <summary>Messages put to the user, as title and body.</summary>
    protected List<(string Title, string Message)> Told { get; } = [];

    protected UiTestBase()
    {
        _dbPath = TempPath("vvo");
        _settingsPath = TempPath("json");

        Database = new DatabaseService();
        Database.EnsureDatabaseReadyAsync(_dbPath).GetAwaiter().GetResult();
        Volumes = new VirtualVolumeService(Database);
        Transfer = new DatabaseTransferService(Database);
        Undo = new UndoService();
        Settings = new Settings(_settingsPath);

        Shell = new Window { Width = 900, Height = 600 };
        Shell.Show();

        Dialogs.Owner = () => Shell;
        Dialogs.Told = (title, message) =>
        {
            Told.Add((title, message));
            return Task.CompletedTask;
        };

        // Nothing answers a dialog until a test says how; a command that opens one before then
        // is a test that forgot to set it up, and cancelling is the harmless reading
        AnswerDialogs(_ => false);
    }

    protected string TempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.{extension}");
        _spilled.Add(path);
        return path;
    }

    /// <summary>
    /// Answers every dialog the code under test opens. The dialog is handed over first so its
    /// view model can be filled in, which is what standing in for the user amounts to.
    /// </summary>
    protected void AnswerDialogs(Func<Window, bool> answer)
    {
        Dialogs.Answer = dialog =>
        {
            Opened.Add(dialog);
            return Task.FromResult(answer(dialog));
        };
    }

    /// <summary>Answers a dialog by filling in its view model and confirming.</summary>
    protected void AnswerDialogs<T>(Action<T> fillIn) where T : class
    {
        AnswerDialogs(dialog =>
        {
            if (dialog.DataContext is not T viewModel)
                return false;

            fillIn(viewModel);
            return true;
        });
    }

    // The messenger is shared and holds recipients weakly, so one test's view model can still
    // be listening while the next one runs unless it is let go of here
    private readonly List<object> _recipients = [];

    private T Track<T>(T recipient) where T : notnull
    {
        _recipients.Add(recipient);
        return recipient;
    }

    protected SidebarViewModel NewSidebar()
    {
        return Track(new SidebarViewModel(
            Undo, Database, new FileScannerService(), new FolderCompareService(),
            Volumes, Transfer, Settings));
    }

    protected VolumeExplorerViewModel NewExplorer() => Track(new VolumeExplorerViewModel(Undo, Database));

    protected StartPageViewModel NewStartPage() => Track(new StartPageViewModel(Undo, Settings, Database));

    /// <summary>
    /// A main window over the real scanner, or over one supplied in its place: a scan that has
    /// to report folders it was refused cannot be staged on the file system.
    /// </summary>
    protected MainWindowViewModel NewMainWindow(
        SidebarViewModel? sidebar = null, IFileScannerService? scanner = null)
    {
        sidebar ??= NewSidebar();

        return Track(new MainWindowViewModel(
            Undo, scanner ?? new FileScannerService(), Database, Volumes, Settings,
            sidebar, NewStartPage(), NewExplorer()));
    }

    /// <summary>Lets the posted work run, which is what a real message loop would do.</summary>
    protected static void Pump() => Dispatcher.UIThread.RunJobs();

    /// <summary>
    /// Waits for work started but not handed back — a keystroke waiting out its delay, or a
    /// broadcast being answered — rather than guessing at how long it takes.
    /// </summary>
    protected static async Task Until(Func<bool> settled, string what)
    {
        for (var attempt = 0; attempt < 200; attempt++)
        {
            Pump();
            if (settled())
                return;

            await Task.Delay(25);
        }

        Assert.Fail($"waited in vain for {what}");
    }

    /// <summary>
    /// Holds the database file open to itself, which is what a read runs into when the drive
    /// it sits on has gone or another program has taken it.
    /// </summary>
    protected IDisposable DatabaseTakenAway() =>
        File.Open(Database.DbPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

    /// <summary>
    /// Stops a view model reacting to broadcasts. A broadcast is answered without anything
    /// waiting on the answer, so a view model the test drives itself would otherwise be
    /// arriving at its own state alongside the one under test.
    /// </summary>
    protected static void Detach(object recipient) =>
        WeakReferenceMessenger.Default.UnregisterAll(recipient);

    /// <summary>
    /// Listens for a broadcast so a test can assert on what was sent rather than on what some
    /// other view model made of it, which it has no way of waiting for.
    /// </summary>
    protected sealed class MessageProbe<T> : IDisposable where T : class
    {
        private readonly TaskCompletionSource<T> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<T> All { get; } = [];

        /// <param name="onMessage">
        /// Run as the message is sent, on the sender's own thread, which is where a test has to
        /// act to get in the way of work already under way.
        /// </param>
        public MessageProbe(Action<T>? onMessage = null)
        {
            WeakReferenceMessenger.Default.Register<MessageProbe<T>, T>(this, (probe, message) =>
            {
                probe.All.Add(message);
                probe._first.TrySetResult(message);
                onMessage?.Invoke(message);
            });
        }

        /// <summary>The first message sent, waited for only as long as it takes.</summary>
        public Task<T> FirstAsync() => _first.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() =>
            WeakReferenceMessenger.Default.UnregisterAll(this);
    }

    protected async Task<RootFolderMetadata> AddFolderAsync(Guid volumeId, string name = "Code")
    {
        var rootId = Guid.NewGuid();

        var records = new List<FileRecord>
        {
            new() { Id = rootId, RootFolderId = rootId, ParentId = null, IsFolder = true, Name = name, Size = 30 },
            new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = rootId, IsFolder = false, Name = "readme.md", Size = 10 },
            new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = rootId, IsFolder = true, Name = "AdventOfCode", Size = 20 }
        };

        var metadata = new RootFolderMetadata
        {
            Id = Guid.NewGuid(),
            TreeId = rootId,
            Path = $@"D:\{name}",
            LastScanned = DateTime.UtcNow
        };

        return await Volumes.AddFolderAsync(volumeId, metadata, records);
    }

    /// <summary>
    /// A tree deep enough for a path to be rebuilt from a hit: name/AdventOfCode/2023/day1.txt.
    /// </summary>
    protected async Task<RootFolderMetadata> AddDeepFolderAsync(Guid volumeId, string name = "Code")
    {
        var rootId = Guid.NewGuid();
        var adventId = Guid.NewGuid();
        var yearId = Guid.NewGuid();

        var records = new List<FileRecord>
        {
            new() { Id = rootId, RootFolderId = rootId, ParentId = null, IsFolder = true, Name = name, Size = 60 },
            new() { Id = adventId, RootFolderId = rootId, ParentId = rootId, IsFolder = true, Name = "AdventOfCode", Size = 50 },
            new() { Id = yearId, RootFolderId = rootId, ParentId = adventId, IsFolder = true, Name = "2023", Size = 40 },
            new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = yearId, IsFolder = false, Name = "day1.txt", Size = 40 },
            new() { Id = Guid.NewGuid(), RootFolderId = rootId, ParentId = rootId, IsFolder = false, Name = "readme.md", Size = 10 }
        };

        var metadata = new RootFolderMetadata
        {
            Id = Guid.NewGuid(),
            TreeId = rootId,
            Path = $@"D:\{name}",
            LastScanned = DateTime.UtcNow
        };

        return await Volumes.AddFolderAsync(volumeId, metadata, records);
    }

    /// <summary>Chooses a path in place of the file picker.</summary>
    protected void PickFiles(Func<string, string, string?> pick) => DatabaseFiles.Picking = pick;

    /// <summary>Chooses a fresh temporary path of the right kind for every picker.</summary>
    protected void PickFreshFiles() => PickFiles((_, extension) => TempPath(extension));

    /// <summary>Chooses a folder in place of the folder picker.</summary>
    protected static void PickFolder(string? path) => FolderPicker.Picking = _ => path;

    /// <summary>
    /// Makes every dialog throw on being opened, which is how the error path of a command that
    /// shows one is reached without a database in a state no user could put it in.
    /// </summary>
    protected void FailDialogs(string message = "the dialog could not be shown") =>
        Dialogs.Answer = _ => throw new InvalidOperationException(message);

    /// <summary>A directory of its own, cleared away with the rest of the test's leavings.</summary>
    protected string TempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(path);
        _spilledDirectories.Add(path);

        return path;
    }

    private readonly List<string> _spilledDirectories = [];

    public virtual void Dispose()
    {
        Dialogs.Reset();
        DatabaseFiles.Reset();
        FolderPicker.Reset();
        TextClipboard.Reset();

        foreach (var recipient in _recipients)
        {
            WeakReferenceMessenger.Default.UnregisterAll(recipient);
        }

        Shell.Close();

        foreach (var path in _spilled.Where(File.Exists))
        {
            try { File.Delete(path); } catch { }
        }

        foreach (var path in _spilledDirectories.Where(Directory.Exists))
        {
            try { Directory.Delete(path, recursive: true); } catch { }
        }

        GC.SuppressFinalize(this);
    }
}
