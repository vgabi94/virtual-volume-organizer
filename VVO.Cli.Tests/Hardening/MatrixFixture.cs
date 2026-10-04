using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Tests.Hardening;

/// <summary>
/// A catalogue every command can be run against: a folder scanned from a real disk tree (so
/// rescan and compare --disk have something to read), a second volume to copy and move to, a
/// copy sharing the scanned tree, and an export to import. Fresh for each run, since half the
/// commands change it.
/// </summary>
public sealed class MatrixFixture : IDisposable
{
    public TempCatalogue Catalogue { get; }
    public DiskTree Disk { get; }

    /// <summary>
    /// A directory for files a command creates: copies, exports, new catalogues.
    /// </summary>
    public string Scratch { get; }

    public VirtualVolumeRecord Backups { get; private set; } = null!;
    public VirtualVolumeRecord Archive { get; private set; } = null!;
    public RootFolderMetadata Folder { get; private set; } = null!;
    public RootFolderMetadata Copy { get; private set; } = null!;
    public FileRecord SubFolder { get; private set; } = null!;
    public FileRecord File { get; private set; } = null!;
    public string ExportPath { get; private set; } = null!;

    private MatrixFixture(TempCatalogue catalogue)
    {
        Catalogue = catalogue;
        Disk = new DiskTree()
            .File("readme.txt", 10)
            .File(@"photos\beach.jpg", 20)
            .File(@"photos\trip\day1.jpg", 30);

        Scratch = Path.Combine(Path.GetTempPath(), $"VVO_Scratch_{Guid.NewGuid()}");
        Directory.CreateDirectory(Scratch);
    }

    public static async Task<MatrixFixture> CreateAsync()
    {
        var fixture = new MatrixFixture(await TempCatalogue.CreateAsync());
        var catalogue = fixture.Catalogue;

        fixture.Backups = await catalogue.AddVolumeAsync("Backups");
        fixture.Archive = await catalogue.AddVolumeAsync("Archive");

        var scan = await new FileScannerService().ScanDirectoryAsync(fixture.Disk.Root);
        fixture.Folder = await catalogue.Volumes.AddFolderAsync(fixture.Backups.Id, scan.Metadata, scan.Records.ToList());
        fixture.Copy = await catalogue.Volumes.CopyFolderAsync(fixture.Folder.Id, fixture.Archive.Id, "Copy");
        fixture.SubFolder = scan.Records.Single(record => record.Name == "photos");
        fixture.File = scan.Records.Single(record => record.Name == "beach.jpg");

        fixture.ExportPath = fixture.ScratchPath("export.json");
        await new DatabaseTransferService(catalogue.Database).ExportAsync(fixture.ExportPath);

        // The export left the service on the catalogue; nothing else is pointed elsewhere
        return fixture;
    }

    public string ScratchPath(string name) => Path.Combine(Scratch, name);

    public Task<string> SnapshotAsync() => Catalogue.SnapshotAsync();

    public void Dispose()
    {
        Disk.Dispose();
        Catalogue.Dispose();
        try { Directory.Delete(Scratch, true); } catch { }
    }
}
