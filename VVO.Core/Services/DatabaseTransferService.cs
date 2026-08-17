using System.Text.Json;
using System.Text.Json.Serialization;
using VVO.Core.Models;

namespace VVO.Core.Services;

public class DatabaseTransferService : IDatabaseTransferService
{
    public const int CurrentFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IDatabaseService _databaseService;

    public DatabaseTransferService(IDatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public async Task ExportAsync(string jsonPath, IProgress<string>? progress = null)
    {
        progress?.Report("Reading the catalogue...");

        var databases = await _databaseService.ReadItemsAsync<DatabaseMetadata>();
        var volumes = await _databaseService.ReadItemsAsync<VirtualVolumeRecord>();
        var folders = await _databaseService.ReadItemsAsync<RootFolderMetadata>();

        progress?.Report("Reading the scanned files...");

        var referenced = folders.Select(entry => entry.TreeId).ToHashSet();
        var files = (await _databaseService.ReadItemsAsync<FileRecord>())
            .Where(record => referenced.Contains(record.RootFolderId))
            .ToList();

        var document = new DatabaseExport
        {
            Exported = DateTime.UtcNow,
            Databases = databases,
            VirtualVolumes = volumes,
            Folders = folders,
            Files = files
        };

        progress?.Report($"Writing {files.Count} records...");

        await using var stream = File.Create(jsonPath);
        await JsonSerializer.SerializeAsync(stream, document, Options);
    }

    public async Task ImportAsync(string jsonPath, string databasePath, IProgress<string>? progress = null)
    {
        progress?.Report("Reading the export...");

        DatabaseExport? document;
        await using (var stream = File.OpenRead(jsonPath))
        {
            document = await JsonSerializer.DeserializeAsync<DatabaseExport>(stream, Options);
        }

        if (document == null)
        {
            throw new InvalidDataException($"'{jsonPath}' holds no export.");
        }

        if (document.FormatVersion > CurrentFormatVersion)
        {
            throw new InvalidDataException(
                $"'{jsonPath}' was written in format {document.FormatVersion}, which this version does not read.");
        }

        progress?.Report("Building the database...");

        // The import owns the file it lands in, so anything already there is cleared out rather
        // than merged with: two catalogues sharing an id would otherwise collide on insert.
        await _databaseService.EnsureDatabaseReadyAsync(databasePath, replace: true);

        await _databaseService.TransactionAsync(db =>
        {
            Insert(db, document.Databases);
            Insert(db, document.VirtualVolumes);
            Insert(db, document.Folders);
            Insert(db, document.Files);
        });
    }

    private void Insert<T>(LiteDB.LiteDatabase db, IReadOnlyCollection<T> items)
    {
        if (items.Count > 0)
        {
            db.GetCollection<T>(_databaseService.TableName<T>()).InsertBulk(items);
        }
    }
}
