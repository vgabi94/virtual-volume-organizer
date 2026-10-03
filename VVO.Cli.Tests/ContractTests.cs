using System.Runtime.CompilerServices;
using System.Text.Json;
using VVO.Cli.Contract;
using VVO.Cli.Output;
using VVO.Core.Models;

namespace VVO.Cli.Tests;

/// <summary>
/// The JSON agents read, held against checked-in snapshots under Contract/. A failure here means
/// the contract changed: if that was meant, update the snapshot in the same commit.
/// </summary>
public class ContractTests
{
    private static readonly Guid VolumeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EntryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TreeId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid FileId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly DateTime Scanned = new(2026, 3, 1, 9, 15, 0, DateTimeKind.Utc);
    private static readonly DateTime Written = new(2025, 12, 24, 18, 0, 30, DateTimeKind.Utc);

    private static readonly RootFolderMetadata Entry = new()
    {
        Id = EntryId,
        VirtualVolumeId = VolumeId,
        TreeId = TreeId,
        LastScanned = Scanned,
        Path = @"D:\Photos",
        Label = null,
        Description = null,
        Icon = null,
        Color = null
    };

    private static readonly FileRecord Root = new()
    {
        Id = TreeId, RootFolderId = TreeId, ParentId = null, IsFolder = true,
        Name = "Photos", Size = 2048, Created = Written, Modified = Written
    };

    private static readonly FileRecord File = new()
    {
        Id = FileId, RootFolderId = TreeId, ParentId = TreeId, IsFolder = false,
        Name = "beach.jpg", Size = 2048, Created = Written, Modified = Written
    };

    private static void AssertMatchesSnapshot(object value, string name, [CallerFilePath] string here = "")
    {
        var actual = JsonSerializer.Serialize(value, Json.Options).ReplaceLineEndings("\n");
        var path = Path.Combine(Path.GetDirectoryName(here)!, "Contract", $"{name}.json");

        if (!System.IO.File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, actual + "\n");
            Assert.Fail($"No snapshot for {name}; wrote one to {path}. Review it and run again.");
        }

        Assert.Equal(System.IO.File.ReadAllText(path).ReplaceLineEndings("\n").TrimEnd(), actual);
    }

    [Fact]
    public void Volume()
    {
        var volume = new VirtualVolumeRecord { Id = VolumeId, Name = "Backups", Icon = "HardDrive", Color = "#FF8800" };

        AssertMatchesSnapshot(VolumeDto.From(volume, 3), "volume");
    }

    [Fact]
    public void FolderEntryWithoutTheOptionalFields()
    {
        AssertMatchesSnapshot(FolderEntryDto.From(Entry, Root, 1), "folder-entry");
    }

    [Fact]
    public void FolderEntryWithEveryField()
    {
        var entry = Entry with { Label = "Holidays", Description = "Summer 2025", Icon = "FolderImage", Color = "#3366CC" };

        AssertMatchesSnapshot(FolderEntryDto.From(entry, Root, 1), "folder-entry-full");
    }

    [Fact]
    public void Record()
    {
        var paths = new Core.EntryPaths(Entry, "Backups", [Root, File]);
        var placement = PlacementDto.From(Entry, paths, FileId);

        AssertMatchesSnapshot(RecordDto.From(File, [placement]), "record");
    }

    [Fact]
    public void TreeEntry()
    {
        var paths = new Core.EntryPaths(Entry, "Backups", [Root, File]);

        AssertMatchesSnapshot(
            new TreeEntryDto(1, RecordDto.From(File, [PlacementDto.From(Entry, paths, FileId)])), "tree-entry");
    }

    [Fact]
    public void ComparisonRow()
    {
        var right = File with { Id = Guid.NewGuid(), Size = 4096, Modified = Scanned };
        var row = new ComparisonResult
        {
            RelativePath = "beach.jpg",
            Status = ComparisonStatus.Changed,
            Changes = ChangeKind.Size | ChangeKind.Modified,
            Left = File,
            Right = right
        };

        // The right side was read from disk, so the id it was given there is not reported
        AssertMatchesSnapshot(ComparisonRowDto.From(row, leftStored: true, rightStored: false), "comparison-row");
    }

    [Fact]
    public void CollapsedComparisonRow()
    {
        var row = new ComparisonResult
        {
            RelativePath = "Old",
            Status = ComparisonStatus.Removed,
            Left = Root with { Id = FileId, ParentId = TreeId, Name = "Old" },
            DescendantCount = 12
        };

        AssertMatchesSnapshot(ComparisonRowDto.From(row, leftStored: true, rightStored: false), "comparison-row-collapsed");
    }

    [Fact]
    public void Error()
    {
        var error = new CliException(
            ExitCode.ConfirmationRequired,
            ErrorCodes.ConfirmationRequired,
            "Deleting this folder cannot be undone. Run the command in a terminal to confirm.",
            new Dictionary<string, object?> { ["command"] = @"vvo folder delete 22222222-2222-2222-2222-222222222222 --db D:\b.vvo" });

        AssertMatchesSnapshot(error.ToEnvelope(), "error");
    }
}
