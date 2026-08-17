using VVO.Core.Models;

namespace VVO.Core.Services;

/// <summary>
/// The whole catalogue in one JSON document. Trees no folder entry refers to are left out, so
/// an export carries what the database shows and not what it is still holding on to.
/// </summary>
public record DatabaseExport
{
    public int FormatVersion { get; init; } = DatabaseTransferService.CurrentFormatVersion;
    public DateTime Exported { get; init; }

    public IReadOnlyCollection<DatabaseMetadata> Databases { get; init; } = [];
    public IReadOnlyCollection<VirtualVolumeRecord> VirtualVolumes { get; init; } = [];
    public IReadOnlyCollection<RootFolderMetadata> Folders { get; init; } = [];
    public IReadOnlyCollection<FileRecord> Files { get; init; } = [];
}

public interface IDatabaseTransferService
{
    /// <summary>
    /// Writes the open database to a JSON file.
    /// </summary>
    Task ExportAsync(string jsonPath, IProgress<string>? progress = null);

    /// <summary>
    /// Reads a JSON file into a database of its own, replacing whatever is at that path, and
    /// leaves it as the open database.
    /// </summary>
    Task ImportAsync(string jsonPath, string databasePath, IProgress<string>? progress = null);
}
