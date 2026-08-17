using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Messaging;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.UI;
using VVO.UI.Messages;
using VVO.UI.ViewModels;

namespace VVO.UiTests;

/// <summary>
/// Scanning a folder on disk into a virtual volume, which is the only command that reads the
/// file system rather than the catalogue.
/// </summary>
public class ScanTests : UiTestBase
{
    private SidebarViewModel Sidebar { get; }
    private MainWindowViewModel MainWindow { get; }

    public ScanTests()
    {
        Sidebar = NewSidebar();
        MainWindow = NewMainWindow(Sidebar);
    }

    /// <summary>A folder holding a file and a subfolder with a file of its own.</summary>
    private string GivenAFolderOnDisk()
    {
        var root = TempDirectory();
        File.WriteAllText(Path.Combine(root, "readme.md"), "hello");
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "main.cs"), "code");

        return root;
    }

    private async Task GivenAVolumeAsync()
    {
        await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await Sidebar.LoadAsync();
    }

    #region Scanning

    [AvaloniaFact]
    public async Task AScannedFolderIsListedUnderTheVolumeItWentInto()
    {
        await GivenAVolumeAsync();
        var scanned = GivenAFolderOnDisk();
        PickFolder(scanned);

        await MainWindow.AddFolderCommand.ExecuteAsync(Shell);

        var listed = Sidebar.VirtualVolumes.Single().Folders.Single();
        Assert.Equal(Path.GetFileName(scanned), listed.Title);
        Assert.Equal(scanned, listed.Entry.Path);
    }

    [AvaloniaFact]
    public async Task EverythingUnderTheScannedFolderIsCatalogued()
    {
        await GivenAVolumeAsync();
        PickFolder(GivenAFolderOnDisk());

        await MainWindow.AddFolderCommand.ExecuteAsync(Shell);

        var entry = Sidebar.VirtualVolumes.Single().Folders.Single().Entry;
        var records = await Database.FindItemsAsync<VVO.Core.Models.FileRecord>(
            record => record.RootFolderId == entry.TreeId);

        Assert.Equal(
            ["main.cs", "readme.md", "src"],
            records.Where(record => record.Id != entry.TreeId)
                .Select(record => record.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    [AvaloniaFact]
    public async Task AScanTellsTheWindowWhatItIsDoingAndStopsWhenItIsDone()
    {
        await GivenAVolumeAsync();
        PickFolder(GivenAFolderOnDisk());

        await MainWindow.AddFolderCommand.ExecuteAsync(Shell);

        Assert.False(MainWindow.IsStatusMessageVisible);
        Assert.False(MainWindow.IsCancelVisible);
        Assert.NotEmpty(MainWindow.StatusMessage);
    }

    [AvaloniaFact]
    public async Task PickingNoFolderScansNothing()
    {
        await GivenAVolumeAsync();
        PickFolder(null);

        await MainWindow.AddFolderCommand.ExecuteAsync(Shell);

        Assert.Empty(Sidebar.VirtualVolumes.Single().Folders);
        Assert.False(MainWindow.IsStatusMessageVisible);
    }

    [AvaloniaFact]
    public async Task AFolderScannedFromTheSidebarGoesIntoTheVolumeItsButtonBelongsTo()
    {
        await Volumes.CreateVirtualVolumeAsync("first", "HardDrive");
        await Volumes.CreateVirtualVolumeAsync("second", "CompactDisc");
        await Sidebar.LoadAsync();

        PickFolder(GivenAFolderOnDisk());

        var second = Sidebar.VirtualVolumes.Single(node => node.Name == "second");
        Sidebar.AddFolderCommand.Execute(second);
        await Until(() => second.Folders.Count > 0, "the scan to finish");

        Assert.Empty(Sidebar.VirtualVolumes.Single(node => node.Name == "first").Folders);
    }

    [AvaloniaFact]
    public async Task TheSidebarsScanButtonNeedsBothAVolumeAndAWindow()
    {
        await GivenAVolumeAsync();
        using var probe = new MessageProbe<AddFolderMessage>();

        Sidebar.AddFolderCommand.Execute(null);
        Assert.Empty(probe.All);

        Dialogs.Owner = () => null;
        Sidebar.AddFolderCommand.Execute(Sidebar.VirtualVolumes.Single());
        Assert.Empty(probe.All);
    }

    [AvaloniaFact]
    public async Task ScanningWithNothingToScanIntoIsRefusedWithAnExplanation()
    {
        await Sidebar.LoadAsync();

        await MainWindow.AddFolderCommand.ExecuteAsync(Shell);

        Assert.Contains("no virtual volumes", Assert.Single(Told).Message);
    }

    [AvaloniaFact]
    public async Task AScanOfAFolderThatIsNoLongerThereIsReportedRatherThanLost()
    {
        await GivenAVolumeAsync();
        PickFolder(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        WeakReferenceMessenger.Default.Send(new AddFolderMessage(Shell, Sidebar.TargetVirtualVolumeId!.Value));

        await Until(() => Told.Count > 0, "the failure to be reported");
        Assert.Equal("Error", Told[0].Title);
    }

    #endregion

    #region Cancelling

    [AvaloniaFact]
    public void CancellingAsksWhoeverIsWorkingToStopRatherThanStoppingThemDirectly()
    {
        using var probe = new MessageProbe<CancelRequestedMessage>();

        MainWindow.CancelCommand.Execute(null);

        Assert.Single(probe.All);
    }

    [AvaloniaFact]
    public void CancellingWithNothingRunningIsHarmless()
    {
        MainWindow.Receive(new CancelRequestedMessage());
        Sidebar.Receive(new CancelRequestedMessage());

        Assert.Empty(Told);
    }

    // Cancelling is the user's decision, not a failure to report back to them
    [AvaloniaFact]
    public async Task CancellingDuringTheWalkStoresNothingAndSaysNothing()
    {
        var volumeId = await ScanIntoAVolumeAsync(new CancellingScanner(duringTheWalk: true));

        Assert.Empty(await Volumes.GetFoldersAsync(volumeId));
        Assert.Empty(Told);
    }

    // The save is one transaction, so a cancel between the walk and the commit takes it all
    [AvaloniaFact]
    public async Task CancellingBeforeTheSaveCommitsStoresNothing()
    {
        var volumeId = await ScanIntoAVolumeAsync(new CancellingScanner(duringTheWalk: false));

        Assert.Empty(await Volumes.GetFoldersAsync(volumeId));
        Assert.Empty(Told);
    }

    #endregion

    #region Folders the scan was refused

    /// <summary>
    /// Stands in for a scan that met a folder it could not open. Nothing that can be staged on
    /// the file system refuses the account running the tests reliably enough to rely on.
    /// </summary>
    private static ScanResult AScanOf(string rootPath, int skipped)
    {
        var rootId = Guid.NewGuid();

        return new ScanResult(
            new RootFolderMetadata
            {
                Id = Guid.NewGuid(),
                TreeId = rootId,
                Path = rootPath,
                LastScanned = DateTime.UtcNow
            },
            [new FileRecord { Id = rootId, RootFolderId = rootId, IsFolder = true, Name = "root", Size = 0 }],
            skipped);
    }

    private sealed class RefusedScanner : IFileScannerService
    {
        private readonly int _skipped;

        public RefusedScanner(int skipped) => _skipped = skipped;

        public Task<ScanResult> ScanDirectoryAsync(
            string rootPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? label = null,
            string? description = null,
            bool includeHiddenAndSystem = false)
        {
            return Task.FromResult(AScanOf(rootPath, _skipped));
        }
    }

    /// <summary>
    /// Presses Cancel from inside the operation, either while the walk is running or once it
    /// has handed its records over and the save is about to begin.
    /// </summary>
    private sealed class CancellingScanner : IFileScannerService
    {
        private readonly bool _duringTheWalk;

        public CancellingScanner(bool duringTheWalk) => _duringTheWalk = duringTheWalk;

        public Task<ScanResult> ScanDirectoryAsync(
            string rootPath,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? label = null,
            string? description = null,
            bool includeHiddenAndSystem = false)
        {
            WeakReferenceMessenger.Default.Send(new CancelRequestedMessage());

            if (_duringTheWalk)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(AScanOf(rootPath, skipped: 0));
        }
    }

    private async Task<Guid> ScanIntoAVolumeAsync(IFileScannerService scanner)
    {
        var sidebar = NewSidebar();
        var mainWindow = NewMainWindow(sidebar, scanner);

        var volume = await Volumes.CreateVirtualVolumeAsync("test", "HardDrive");
        await sidebar.LoadAsync();
        sidebar.SelectedVirtualVolume = sidebar.VirtualVolumes.Single();

        PickFolder(GivenAFolderOnDisk());
        await mainWindow.AddFolderCommand.ExecuteAsync(null);

        return volume.Id;
    }

    // The records go in either way; without this the catalogue is quietly short
    [AvaloniaFact]
    public async Task AScanThatCouldNotOpenEverythingSaysSo()
    {
        await ScanIntoAVolumeAsync(new RefusedScanner(skipped: 4));

        var (title, message) = Assert.Single(Told);
        Assert.Equal(ScanWarning.Title, title);
        Assert.Contains("4 folders", message);
    }

    [AvaloniaFact]
    public async Task AScanThatReadEverythingSaysNothing()
    {
        await ScanIntoAVolumeAsync(new RefusedScanner(skipped: 0));

        Assert.Empty(Told);
    }

    [AvaloniaFact]
    public void OneRefusedFolderIsNotReportedAsFolders()
    {
        Assert.Contains("1 folder ", ScanWarning.Describe(1, @"D:\Music"));
        Assert.Contains("2 folders ", ScanWarning.Describe(2, @"D:\Music"));
    }

    [AvaloniaFact]
    public void TheWarningNamesTheFolderAndWhatUsuallyCausesIt()
    {
        var message = ScanWarning.Describe(3, @"D:\Music");

        Assert.Contains(@"D:\Music", message);
        Assert.Contains("Controlled Folder Access", message);
    }

    #endregion

    #region Leaving

    // Nothing shuts down here: headless has no desktop lifetime to shut down, which is exactly
    // the case the command has to survive
    [AvaloniaFact]
    public void LeavingWithoutADesktopToCloseIsHarmless()
    {
        MainWindow.ExitCommand.Execute(null);

        Assert.Empty(Told);
    }

    #endregion
}
