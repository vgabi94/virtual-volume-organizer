namespace VVO.Core.Models;

public record VirtualVolumeRecord : IHasId
{
    public Guid Id { get; init; }
    public required string Name { get; init; }

    // Key of the icon the volume is listed under, opaque to the core
    public required string Icon { get; init; }

    // Icon colour as #RRGGBB or #AARRGGBB, null while it follows the application default
    public string? Color { get; init; }
}
