using VVO.Core.Models;

namespace VVO.Core.Services;

/// <summary>
/// What a scan found, and what it could not get into. A folder the scan is refused — by
/// permissions, or by something like Controlled Folder Access standing in the way — is left out
/// of the records, so <paramref name="SkippedFolders"/> is what says the catalogue is short.
/// </summary>
public record ScanResult(
    RootFolderMetadata Metadata,
    IReadOnlyList<FileRecord> Records,
    int SkippedFolders)
{
    /// <summary>
    /// Most callers want only what was found; the count is for the ones that report it.
    /// </summary>
    public void Deconstruct(out RootFolderMetadata metadata, out IEnumerable<FileRecord> records)
    {
        metadata = Metadata;
        records = Records;
    }
}

public interface IFileScannerService
{
    /// <param name="includeHiddenAndSystem">
    /// Entries marked hidden or system are left out by default, which on a system drive is
    /// ProgramData and every AppData folder. They are not refusals and never reach
    /// <see cref="ScanResult.SkippedFolders"/>.
    /// </param>
    Task<ScanResult> ScanDirectoryAsync(
        string rootPath,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default,
        string? label = null,
        string? description = null,
        bool includeHiddenAndSystem = false);
}
