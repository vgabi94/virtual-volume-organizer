namespace VVO.Core.Models;

public record DatabaseMetadata : IHasId
{
    public Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
}