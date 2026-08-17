namespace VVO.Core.Models;

public record FileRecord : IHasId
{
    public Guid Id { get; init; }

    // Id of the root folder of the tree this record belongs to, for fast deletion
    public Guid RootFolderId { get; init; }

    // Links to the immediate parent (null if this is the root folder)
    public Guid? ParentId { get; init; }

    public bool IsFolder { get; init; }
    public required string Name { get; init; }

    // Actual size (in bytes) for files, or computed total size for folders
    public long Size { get; init; }

    // Always UTC
    public DateTime Created { get; init; }
    public DateTime Modified { get; init; }
}
