namespace VVO.Core.Models;

// Relative to the left tree: Added means present on the right side only
public enum ComparisonStatus
{
    Added,
    Removed,
    Changed,
    Unchanged
}

// Creation time is deliberately absent: copying a file changes it without changing the contents
[Flags]
public enum ChangeKind
{
    None = 0,
    Size = 1,
    Modified = 2
}

public record ComparisonResult
{
    // Path relative to the compared root folder, empty for the root itself.
    // Not unique within a result list: a file replaced by a folder (or the reverse)
    // is reported as a Removed and an Added row sharing the same path.
    public required string RelativePath { get; init; }

    // For folders this describes the whole subtree, not the folder entry itself
    public required ComparisonStatus Status { get; init; }

    // Always None for folders, so a Changed row without any change flags is a folder
    // that differs only below itself
    public ChangeKind Changes { get; init; }

    public FileRecord? Left { get; init; }
    public FileRecord? Right { get; init; }

    // Number of entries below a subtree that collapsed into a single row, null elsewhere
    public int? DescendantCount { get; init; }
}
