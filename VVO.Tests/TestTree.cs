using VVO.Core.Models;

namespace VVO.Tests;

public static class TestTree
{
    // Shared by every entry that does not override it, so two trees built independently
    // compare as identical. Whole seconds, so values are already millisecond-clean.
    public static readonly DateTime DefaultTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static TreeBuilder Root(
        string name = "root",
        string? path = null,
        DateTime? created = null,
        DateTime? modified = null)
    {
        var rootId = Guid.NewGuid();

        var state = new TreeBuilder.State
        {
            RootId = rootId,
            Metadata = new RootFolderMetadata
            {
                Id = Guid.NewGuid(),
                TreeId = rootId,
                Path = path ?? $@"C:\{name}",
                LastScanned = DefaultTime
            }
        };

        state.Records.Add(new FileRecord
        {
            Id = rootId,
            RootFolderId = rootId,
            ParentId = null,
            IsFolder = true,
            Name = name,
            Size = 0,
            Created = created ?? DefaultTime,
            Modified = modified ?? DefaultTime
        });

        return new TreeBuilder(state, rootId);
    }
}

public sealed class TreeBuilder
{
    internal sealed class State
    {
        public List<FileRecord> Records { get; } = [];
        public required Guid RootId { get; init; }
        public required RootFolderMetadata Metadata { get; init; }
    }

    private readonly State _state;
    private readonly Guid _parentId;

    private long _fileBytes;

    internal TreeBuilder(State state, Guid parentId)
    {
        _state = state;
        _parentId = parentId;
    }

    public TreeBuilder File(string name, long size = 0, DateTime? modified = null, DateTime? created = null)
    {
        _state.Records.Add(new FileRecord
        {
            Id = Guid.NewGuid(),
            RootFolderId = _state.RootId,
            ParentId = _parentId,
            IsFolder = false,
            Name = name,
            Size = size,
            Created = created ?? TestTree.DefaultTime,
            Modified = modified ?? TestTree.DefaultTime
        });

        _fileBytes += size;
        return this;
    }

    public TreeBuilder Folder(
        string name,
        Action<TreeBuilder>? configure = null,
        long? size = null,
        DateTime? modified = null,
        DateTime? created = null)
    {
        var id = Guid.NewGuid();
        var index = _state.Records.Count;

        _state.Records.Add(new FileRecord
        {
            Id = id,
            RootFolderId = _state.RootId,
            ParentId = _parentId,
            IsFolder = true,
            Name = name,
            Size = 0,
            Created = created ?? TestTree.DefaultTime,
            Modified = modified ?? TestTree.DefaultTime
        });

        var child = new TreeBuilder(_state, id);
        configure?.Invoke(child);

        // An explicit size stays local to this folder: ancestors still roll up the bytes
        // actually below it, so a wrong rollup can be staged on one folder alone.
        _state.Records[index] = _state.Records[index] with { Size = size ?? child._fileBytes };
        _fileBytes += child._fileBytes;

        return this;
    }

    public FolderTree Build()
    {
        var records = _state.Records;
        records[0] = records[0] with { Size = records.Where(r => !r.IsFolder).Sum(r => r.Size) };

        return new FolderTree(_state.Metadata, records);
    }
}

public sealed record FolderTree(RootFolderMetadata Metadata, IReadOnlyList<FileRecord> Records)
{
    public FolderTree Shuffled(int seed = 1)
    {
        var shuffled = Records.ToList();
        var random = new Random(seed);

        for (var i = shuffled.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (shuffled[i], shuffled[j]) = (shuffled[j], shuffled[i]);
        }

        return this with { Records = shuffled };
    }

    public FileRecord Record(string name)
    {
        return Records.Single(record => record.Name == name);
    }
}
