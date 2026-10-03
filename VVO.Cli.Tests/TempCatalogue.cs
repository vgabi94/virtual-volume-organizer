using System.Text.Json;
using VVO.Core.Models;
using VVO.Core.Services;
using VVO.Tests;

namespace VVO.Cli.Tests;

/// <summary>
/// A catalogue of its own for one test, seeded through the Core services rather than the CLI so
/// a command is never tested against data it wrote itself.
/// </summary>
public sealed class TempCatalogue : IDisposable
{
    private readonly string _directory;

    public string Path { get; }
    public DatabaseService Database { get; } = new();
    public VirtualVolumeService Volumes { get; }

    private TempCatalogue()
    {
        // A directory per catalogue, so LiteDB's side files go with it
        _directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"VVO_Cli_{Guid.NewGuid()}");
        Directory.CreateDirectory(_directory);

        Path = System.IO.Path.Combine(_directory, "catalogue.vvo");
        Volumes = new VirtualVolumeService(Database);
    }

    public static async Task<TempCatalogue> CreateAsync()
    {
        var catalogue = new TempCatalogue();
        await catalogue.Database.EnsureDatabaseReadyAsync(catalogue.Path);
        return catalogue;
    }

    public Task<VirtualVolumeRecord> AddVolumeAsync(string name, string icon = "HardDrive", string? color = null)
    {
        return Volumes.CreateVirtualVolumeAsync(name, icon, color);
    }

    public Task<RootFolderMetadata> AddFolderAsync(Guid volumeId, FolderTree tree)
    {
        return Volumes.AddFolderAsync(volumeId, tree.Metadata, tree.Records.ToList());
    }

    /// <summary>
    /// Everything the catalogue holds, in a form that compares equal exactly when nothing changed.
    /// </summary>
    public async Task<string> SnapshotAsync()
    {
        var snapshot = new
        {
            Databases = (await Database.ReadItemsAsync<DatabaseMetadata>()).OrderBy(item => item.Id),
            Volumes = (await Database.ReadItemsAsync<VirtualVolumeRecord>()).OrderBy(item => item.Id),
            Folders = (await Database.ReadItemsAsync<RootFolderMetadata>()).OrderBy(item => item.Id),
            Files = (await Database.ReadItemsAsync<FileRecord>()).OrderBy(item => item.Id)
        };

        return JsonSerializer.Serialize(snapshot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }
}
