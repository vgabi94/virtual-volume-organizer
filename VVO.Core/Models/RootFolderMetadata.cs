namespace VVO.Core.Models;

// A folder placed in a virtual volume. Several entries may share one scanned tree,
// which is what makes a copy or a duplicate cost a single record.
public record RootFolderMetadata : IHasId
{
    public Guid Id { get; init; }

    // The virtual volume holding this folder
    public Guid VirtualVolumeId { get; init; }

    // Matches the Id of the root folder in the FileRecord table
    public Guid TreeId { get; init; }

    public DateTime LastScanned { get; init; }

    // Full physical path of the root folder
    public required string Path { get; init; }

    // Optional name given by the user to identify this root folder in the UI
    public string? Label { get; init; }

    // Optional description of the root folder
    public string? Description { get; init; }

    // Key of the icon the folder is listed under, opaque to the core, null for the default
    public string? Icon { get; init; }

    // Icon colour as #RRGGBB or #AARRGGBB, null while it follows the application default
    public string? Color { get; init; }
}
