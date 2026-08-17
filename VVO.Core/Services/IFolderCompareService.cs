using VVO.Core.Models;

namespace VVO.Core.Services;

public interface IFolderCompareService
{
    /// <summary>
    /// Compares two scanned folder trees and returns how the right one differs from the left.
    /// A subtree that is uniformly added, removed or unchanged collapses to a single result;
    /// only mixed subtrees are expanded.
    /// </summary>
    /// <param name="leftRecords">
    /// Every record of the left tree, including the root folder record itself,
    /// whose Id must match the TreeId of <paramref name="leftMetadata"/>.
    /// </param>
    /// <param name="rightRecords">
    /// Every record of the right tree, including the root folder record itself,
    /// whose Id must match the TreeId of <paramref name="rightMetadata"/>.
    /// </param>
    /// <param name="includeUnchanged">
    /// When false (the default) only differences are returned.
    /// </param>
    Task<IReadOnlyList<ComparisonResult>> CompareAsync(
        RootFolderMetadata leftMetadata,
        IReadOnlyCollection<FileRecord> leftRecords,
        RootFolderMetadata rightMetadata,
        IReadOnlyCollection<FileRecord> rightRecords,
        bool includeUnchanged = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
