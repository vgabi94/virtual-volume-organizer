using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Tests;

public class FolderCompareServiceTests
{
    private static readonly DateTime Later = TestTree.DefaultTime.AddHours(1);

    private readonly FolderCompareService _service = new();

    private Task<IReadOnlyList<ComparisonResult>> Compare(
        FolderTree left,
        FolderTree right,
        bool includeUnchanged = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return _service.CompareAsync(left.Metadata, left.Records, right.Metadata, right.Records,
            includeUnchanged, progress, cancellationToken);
    }

    private static FolderTree Sample(string rootName = "root") => TestTree.Root(rootName)
        .File("a.txt", 10)
        .Folder("sub", f => f
            .File("b.txt", 20)
            .Folder("deep", d => d.File("c.txt", 30)))
        .Build();

    private static (string Path, ComparisonStatus Status)[] Rows(IReadOnlyList<ComparisonResult> results)
    {
        return results.Select(r => (r.RelativePath, r.Status)).ToArray();
    }

    private static string PathOf(params string[] parts) => Path.Combine(parts);

    private static List<FileRecord> WithoutRoot(FolderTree tree)
    {
        return tree.Records.Where(r => r.Id != tree.Metadata.TreeId).ToList();
    }

    // Reporting synchronously on the worker thread is what makes the cancellation tests
    // deterministic; Progress<T> would post the callback elsewhere and race the comparison.
    private sealed class RecordingProgress : IProgress<string>
    {
        public List<string> Messages { get; } = [];

        public void Report(string value) => Messages.Add(value);
    }

    private sealed class CancelOnPhase(CancellationTokenSource source, string prefix) : IProgress<string>
    {
        public void Report(string value)
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                source.Cancel();
            }
        }
    }

    #region Validation

    [Fact]
    public void MissingLeftRoot_Throws()
    {
        var tree = Sample();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            _ = _service.CompareAsync(tree.Metadata, WithoutRoot(tree), tree.Metadata, tree.Records);
        });

        Assert.Equal("leftRecords", exception.ParamName);
    }

    [Fact]
    public void MissingRightRoot_Throws()
    {
        var tree = Sample();

        var exception = Assert.Throws<ArgumentException>(() =>
        {
            _ = _service.CompareAsync(tree.Metadata, tree.Records, tree.Metadata, WithoutRoot(tree));
        });

        Assert.Equal("rightRecords", exception.ParamName);
    }

    [Fact]
    public void EmptyRecordList_Throws()
    {
        var tree = Sample();

        Assert.Throws<ArgumentException>(() =>
        {
            _ = _service.CompareAsync(tree.Metadata, [], tree.Metadata, tree.Records);
        });
    }

    [Fact]
    public void ValidationFailure_ThrowsRatherThanFaultingTheTask()
    {
        var tree = Sample();
        Task<IReadOnlyList<ComparisonResult>>? task = null;

        Assert.Throws<ArgumentException>(() =>
        {
            task = _service.CompareAsync(tree.Metadata, WithoutRoot(tree), tree.Metadata, tree.Records);
        });

        Assert.Null(task);
    }

    #endregion

    #region Identity

    [Fact]
    public async Task IdenticalTrees_ReturnNoDifferences()
    {
        Assert.Empty(await Compare(Sample(), Sample()));
    }

    [Fact]
    public async Task IdenticalTrees_CollapseToTheRootWhenUnchangedIsIncluded()
    {
        var results = await Compare(Sample(), Sample(), includeUnchanged: true);

        var only = Assert.Single(results);
        Assert.Equal(ComparisonStatus.Unchanged, only.Status);
        Assert.Equal(string.Empty, only.RelativePath);
        Assert.Equal(ChangeKind.None, only.Changes);
        Assert.Equal(5, only.DescendantCount);
    }

    [Fact]
    public async Task DifferentRootNames_AreNotADifference()
    {
        Assert.Empty(await Compare(Sample("Backup"), Sample("DriveD")));
    }

    #endregion

    #region Leaves

    [Fact]
    public async Task AddedFile_CarriesTheRightSideOnly()
    {
        var left = TestTree.Root().Build();
        var right = TestTree.Root().File("a.txt", 10).Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("a.txt", ComparisonStatus.Added)
        };
        Assert.Equal(expected, Rows(results));

        Assert.Null(results[1].Left);
        Assert.Equal(right.Record("a.txt"), results[1].Right);
        Assert.Null(results[1].DescendantCount);
    }

    [Fact]
    public async Task RemovedFile_CarriesTheLeftSideOnly()
    {
        var left = TestTree.Root().File("a.txt", 10).Build();
        var right = TestTree.Root().Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("a.txt", ComparisonStatus.Removed)
        };
        Assert.Equal(expected, Rows(results));

        Assert.Equal(left.Record("a.txt"), results[1].Left);
        Assert.Null(results[1].Right);
        Assert.Null(results[1].DescendantCount);
    }

    [Fact]
    public async Task FileSizeDiffers_IsChangedBySizeAlone()
    {
        var left = TestTree.Root().File("a.txt", 10).Build();
        var right = TestTree.Root().File("a.txt", 20).Build();

        var changed = (await Compare(left, right)).Single(r => r.RelativePath == "a.txt");

        Assert.Equal(ComparisonStatus.Changed, changed.Status);
        Assert.Equal(ChangeKind.Size, changed.Changes);
    }

    [Fact]
    public async Task FileModifiedDiffers_IsChangedByModifiedAlone()
    {
        var left = TestTree.Root().File("a.txt", 10).Build();
        var right = TestTree.Root().File("a.txt", 10, modified: Later).Build();

        var changed = (await Compare(left, right)).Single(r => r.RelativePath == "a.txt");

        Assert.Equal(ChangeKind.Modified, changed.Changes);
    }

    [Fact]
    public async Task FileSizeAndModifiedDiffer_IsChangedByBoth()
    {
        var left = TestTree.Root().File("a.txt", 10).Build();
        var right = TestTree.Root().File("a.txt", 20, modified: Later).Build();

        var changed = (await Compare(left, right)).Single(r => r.RelativePath == "a.txt");

        Assert.Equal(ChangeKind.Size | ChangeKind.Modified, changed.Changes);
    }

    [Fact]
    public async Task FileCreatedDiffersOnly_IsUnchanged()
    {
        var left = TestTree.Root().File("a.txt", 10).Build();
        var right = TestTree.Root().File("a.txt", 10, created: Later).Build();

        Assert.Empty(await Compare(left, right));
    }

    #endregion

    #region Container rows

    [Fact]
    public async Task ChangedLeaf_MarksEveryAncestorAsChanged()
    {
        var left = TestTree.Root().Folder("sub", f => f.Folder("deep", d => d.File("a.txt", 10))).Build();
        var right = TestTree.Root().Folder("sub", f => f.Folder("deep", d => d.File("a.txt", 20))).Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("sub", ComparisonStatus.Changed),
            (PathOf("sub", "deep"), ComparisonStatus.Changed),
            (PathOf("sub", "deep", "a.txt"), ComparisonStatus.Changed)
        };
        Assert.Equal(expected, Rows(results));

        Assert.All(results.Take(3), r => Assert.Equal(ChangeKind.None, r.Changes));
        Assert.Equal(ChangeKind.Size, results[3].Changes);
    }

    [Fact]
    public async Task TwoChangedSiblings_AreBothReported()
    {
        var left = TestTree.Root().File("a.txt", 10).File("b.txt", 10).Build();
        var right = TestTree.Root().File("a.txt", 20).File("b.txt", 20).Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("a.txt", ComparisonStatus.Changed),
            ("b.txt", ComparisonStatus.Changed)
        };
        Assert.Equal(expected, Rows(results));
    }

    #endregion

    #region Collapse

    [Fact]
    public async Task AddedSubtree_CollapsesToOneRow()
    {
        var left = TestTree.Root().Build();
        var right = TestTree.Root()
            .Folder("photos", p => p
                .File("a.jpg", 10)
                .Folder("2024", y => y.File("b.jpg", 20)))
            .Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("photos", ComparisonStatus.Added)
        };
        Assert.Equal(expected, Rows(results));
        Assert.Equal(3, results[1].DescendantCount);
    }

    [Fact]
    public async Task RemovedSubtree_CollapsesToOneRow()
    {
        var left = TestTree.Root()
            .Folder("photos", p => p
                .File("a.jpg", 10)
                .Folder("2024", y => y.File("b.jpg", 20)))
            .Build();
        var right = TestTree.Root().Build();

        var results = await Compare(left, right);

        var removed = results.Single(r => r.Status == ComparisonStatus.Removed);
        Assert.Equal("photos", removed.RelativePath);
        Assert.Equal(3, removed.DescendantCount);
        Assert.Null(removed.Right);
    }

    [Fact]
    public async Task Collapse_StopsAtTheHighestUniformFolder()
    {
        var left = TestTree.Root().Build();
        var right = TestTree.Root().Folder("photos", p => p.Folder("2024", y => y.File("a.jpg", 10))).Build();

        var results = await Compare(left, right);

        Assert.Equal(2, results.Count);
        Assert.Equal("photos", results[1].RelativePath);
        Assert.DoesNotContain(results, r => r.RelativePath.Contains("2024"));
    }

    [Fact]
    public async Task MixedSubtree_DoesNotCollapse()
    {
        var left = TestTree.Root().Folder("sub", f => f.File("keep.txt", 10)).Build();
        var right = TestTree.Root().Folder("sub", f => f.File("keep.txt", 10).File("new.txt", 20)).Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("sub", ComparisonStatus.Changed),
            (PathOf("sub", "new.txt"), ComparisonStatus.Added)
        };
        Assert.Equal(expected, Rows(results));
    }

    [Fact]
    public async Task UnchangedSubtree_CollapsesToItsHighestFolder()
    {
        var left = TestTree.Root()
            .Folder("edit", e => e.File("a.txt", 10))
            .Folder("keep", k => k.Folder("inner", i => i.File("b.txt", 20)))
            .Build();
        var right = TestTree.Root()
            .Folder("edit", e => e.File("a.txt", 99))
            .Folder("keep", k => k.Folder("inner", i => i.File("b.txt", 20)))
            .Build();

        var results = await Compare(left, right, includeUnchanged: true);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("edit", ComparisonStatus.Changed),
            (PathOf("edit", "a.txt"), ComparisonStatus.Changed),
            ("keep", ComparisonStatus.Unchanged)
        };
        Assert.Equal(expected, Rows(results));
        Assert.Equal(2, results[3].DescendantCount);
    }

    [Fact]
    public async Task UnchangedSibling_ProducesNoRowsUnderAChangedParent()
    {
        var left = TestTree.Root()
            .Folder("edit", e => e.File("a.txt", 10))
            .Folder("keep", k => k.File("b.txt", 20))
            .Build();
        var right = TestTree.Root()
            .Folder("edit", e => e.File("a.txt", 99))
            .Folder("keep", k => k.File("b.txt", 20))
            .Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("edit", ComparisonStatus.Changed),
            (PathOf("edit", "a.txt"), ComparisonStatus.Changed)
        };
        Assert.Equal(expected, Rows(results));
        Assert.DoesNotContain(results, r => r.RelativePath.StartsWith("keep", StringComparison.Ordinal));
    }

    #endregion

    #region Kind mismatch

    [Fact]
    public async Task FileReplacedByFolder_IsReportedAsRemovedThenAdded()
    {
        var left = TestTree.Root().File("data", 10).Build();
        var right = TestTree.Root().Folder("data", d => d.File("inner.txt", 20)).Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("data", ComparisonStatus.Removed),
            ("data", ComparisonStatus.Added)
        };
        Assert.Equal(expected, Rows(results));

        Assert.Null(results[1].DescendantCount);
        Assert.Equal(1, results[2].DescendantCount);
    }

    #endregion

    #region Structural edges

    [Fact]
    public async Task EmptiedFolder_IsChangedRatherThanRemoved()
    {
        var left = TestTree.Root().Folder("sub", f => f.File("a.txt", 10).File("b.txt", 20)).Build();
        var right = TestTree.Root().Folder("sub").Build();

        var results = await Compare(left, right);

        var expected = new[]
        {
            (string.Empty, ComparisonStatus.Changed),
            ("sub", ComparisonStatus.Changed),
            (PathOf("sub", "a.txt"), ComparisonStatus.Removed),
            (PathOf("sub", "b.txt"), ComparisonStatus.Removed)
        };
        Assert.Equal(expected, Rows(results));
    }

    [Fact]
    public async Task AddedEmptyFolder_HasNoDescendants()
    {
        var left = TestTree.Root().Build();
        var right = TestTree.Root().Folder("sub").Build();

        var results = await Compare(left, right);

        Assert.Equal(ComparisonStatus.Added, results[1].Status);
        Assert.Equal(0, results[1].DescendantCount);
    }

    [Fact]
    public async Task EmptyFoldersOnBothSides_AreUnchanged()
    {
        var left = TestTree.Root().Folder("sub").Build();
        var right = TestTree.Root().Folder("sub").Build();

        Assert.Empty(await Compare(left, right));
    }

    [Fact]
    public async Task FolderRollupSize_IsIgnored()
    {
        var left = TestTree.Root().Folder("sub", f => f.File("a.txt", 10), size: 999).Build();
        var right = TestTree.Root().Folder("sub", f => f.File("a.txt", 10)).Build();

        Assert.Empty(await Compare(left, right));
    }

    [Fact]
    public async Task FolderTimestamps_AreIgnored()
    {
        var left = TestTree.Root().Folder("sub", f => f.File("a.txt", 10)).Build();
        var right = TestTree.Root()
            .Folder("sub", f => f.File("a.txt", 10), modified: Later, created: Later)
            .Build();

        Assert.Empty(await Compare(left, right));
    }

    #endregion

    #region Ordering

    [Fact]
    public async Task Siblings_AreOrderedByNameIgnoringCase()
    {
        var left = TestTree.Root().Build();
        var right = TestTree.Root().File("c.txt", 1).File("A.txt", 1).File("b.txt", 1).Build();

        var results = await Compare(left, right);

        var expected = new[] { string.Empty, "A.txt", "b.txt", "c.txt" };
        Assert.Equal(expected, results.Select(r => r.RelativePath));
    }

    [Fact]
    public async Task ResultOrder_IsIndependentOfInputOrder()
    {
        var left = Sample();
        var right = TestTree.Root()
            .File("a.txt", 99)
            .Folder("sub", f => f
                .File("b.txt", 20)
                .Folder("deep", d => d.File("c.txt", 30)))
            .Build();

        var ordered = await Compare(left, right);
        var shuffled = await Compare(left.Shuffled(3), right.Shuffled(9));

        Assert.NotEmpty(ordered);
        Assert.Equal(Rows(ordered), Rows(shuffled));
    }

    [Fact]
    public async Task ParentRow_PrecedesItsChildren()
    {
        var left = TestTree.Root().Folder("sub", f => f.File("a.txt", 10)).Build();
        var right = TestTree.Root().Folder("sub", f => f.File("a.txt", 20)).Build();

        var paths = (await Compare(left, right)).Select(r => r.RelativePath).ToList();

        Assert.True(paths.IndexOf("sub") < paths.IndexOf(PathOf("sub", "a.txt")));
    }

    #endregion

    #region Matching and paths

    [Fact]
    public async Task NamesAreMatchedIgnoringCase()
    {
        var left = TestTree.Root().File("File.TXT", 10).Build();
        var right = TestTree.Root().File("file.txt", 10).Build();

        Assert.Empty(await Compare(left, right));
    }

    [Fact]
    public async Task NestedRelativePath_UsesThePlatformSeparatorAndStaysRelative()
    {
        var left = TestTree.Root()
            .Folder("sub", f => f.Folder("deep", d => d.File("keep.txt", 5)))
            .Build();
        var right = TestTree.Root()
            .Folder("sub", f => f.Folder("deep", d => d.File("keep.txt", 5).File("a.txt", 10)))
            .Build();

        var results = await Compare(left, right);

        Assert.Contains(results, r => r.RelativePath == PathOf("sub", "deep", "a.txt"));
        Assert.DoesNotContain(results, r => Path.IsPathRooted(r.RelativePath));
    }

    [Fact]
    public async Task RootRow_HasAnEmptyRelativePath()
    {
        var left = TestTree.Root().File("a.txt", 10).Build();
        var right = TestTree.Root().File("a.txt", 20).Build();

        var results = await Compare(left, right);

        Assert.Equal(string.Empty, results[0].RelativePath);
    }

    #endregion

    #region Records predating the timestamp fields

    [Fact]
    public async Task RecordsWithDefaultTimestamps_MatchEachOther()
    {
        var left = TestTree.Root().File("a.txt", 10, modified: default(DateTime)).Build();
        var right = TestTree.Root().File("a.txt", 10, modified: default(DateTime)).Build();

        Assert.Empty(await Compare(left, right));
    }

    [Fact]
    public async Task RecordWithDefaultTimestamp_IsChangedAgainstAScannedRecord()
    {
        var left = TestTree.Root().File("a.txt", 10, modified: default(DateTime)).Build();
        var right = TestTree.Root().File("a.txt", 10).Build();

        var changed = (await Compare(left, right)).Single(r => r.RelativePath == "a.txt");

        Assert.Equal(ChangeKind.Modified, changed.Changes);
    }

    #endregion

    #region Malformed input

    [Fact]
    public async Task OrphanedRecords_AreIgnored()
    {
        var tree = Sample();
        var orphan = new FileRecord
        {
            Id = Guid.NewGuid(),
            RootFolderId = tree.Metadata.TreeId,
            ParentId = Guid.NewGuid(),
            IsFolder = false,
            Name = "orphan.txt",
            Size = 500,
            Created = TestTree.DefaultTime,
            Modified = TestTree.DefaultTime
        };

        var results = await _service.CompareAsync(
            tree.Metadata, tree.Records.Append(orphan).ToList(), tree.Metadata, tree.Records);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ForeignRootFolderId_IsNotConsultedWhenNavigating()
    {
        var left = TestTree.Root().Build();
        var right = TestTree.Root().Build();
        var foreign = new FileRecord
        {
            Id = Guid.NewGuid(),
            RootFolderId = Guid.NewGuid(),
            ParentId = right.Metadata.TreeId,
            IsFolder = false,
            Name = "foreign.txt",
            Size = 10,
            Created = TestTree.DefaultTime,
            Modified = TestTree.DefaultTime
        };

        var results = await _service.CompareAsync(
            left.Metadata, left.Records, right.Metadata, right.Records.Append(foreign).ToList());

        Assert.Contains(results, r => r.RelativePath == "foreign.txt" && r.Status == ComparisonStatus.Added);
    }

    [Fact]
    public async Task InputRecords_AreNotMutated()
    {
        var left = Sample();
        var right = TestTree.Root().File("a.txt", 999).Build();

        var leftSnapshot = left.Records.ToList();
        var rightSnapshot = right.Records.ToList();

        await Compare(left, right);

        Assert.Equal(leftSnapshot, left.Records);
        Assert.Equal(rightSnapshot, right.Records);
    }

    [Fact]
    public async Task SameTreeOnBothSides_ReturnsNothing()
    {
        var tree = Sample();

        var results = await _service.CompareAsync(tree.Metadata, tree.Records, tree.Metadata, tree.Records);

        Assert.Empty(results);
    }

    #endregion

    #region Limits

    [Fact]
    public async Task DeeplyNestedTrees_AreCompared()
    {
        const int depth = 500;

        var results = await Compare(Nested(depth, 10), Nested(depth, 20));

        Assert.Equal(depth + 2, results.Count);
        Assert.Equal(ComparisonStatus.Changed, results[^1].Status);
        Assert.Equal(ChangeKind.Size, results[^1].Changes);
    }

    [Fact]
    public async Task LargeIdenticalTrees_ReturnNothing()
    {
        Assert.Empty(await Compare(Wide(200, 50), Wide(200, 50)));
    }

    private static FolderTree Nested(int depth, long leafSize)
    {
        var builder = TestTree.Root();
        Nest(builder, depth);

        return builder.Build();

        void Nest(TreeBuilder current, int remaining)
        {
            if (remaining == 0)
            {
                current.File("leaf.txt", leafSize);
                return;
            }

            current.Folder($"d{remaining:D4}", child => Nest(child, remaining - 1));
        }
    }

    private static FolderTree Wide(int folders, int filesPerFolder)
    {
        var builder = TestTree.Root();

        for (var f = 0; f < folders; f++)
        {
            builder.Folder($"folder{f:D4}", folder =>
            {
                for (var i = 0; i < filesPerFolder; i++)
                {
                    folder.File($"file{i:D4}.txt", i);
                }
            });
        }

        return builder.Build();
    }

    #endregion

    #region Cancellation and progress

    [Fact]
    public async Task PreCancelledToken_Throws()
    {
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Compare(Sample(), Sample(), cancellationToken: source.Token));
    }

    [Fact]
    public async Task CancellationDuringIndexing_Throws()
    {
        using var source = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Compare(Sample(), Sample(),
                progress: new CancelOnPhase(source, "Indexing"), cancellationToken: source.Token));
    }

    [Fact]
    public async Task CancellationBeforeComparison_Throws()
    {
        using var source = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Compare(Sample(), Sample(),
                progress: new CancelOnPhase(source, "Comparing"), cancellationToken: source.Token));
    }

    [Fact]
    public async Task Progress_ReceivesBothPhases()
    {
        var progress = new RecordingProgress();

        await Compare(Sample(), Sample(), progress: progress);

        Assert.Equal(2, progress.Messages.Count);
        Assert.StartsWith("Indexing", progress.Messages[0]);
        Assert.StartsWith("Comparing", progress.Messages[1]);
    }

    [Fact]
    public async Task NullProgress_IsAccepted()
    {
        var left = Sample();
        var right = Sample();

        var results = await _service.CompareAsync(
            left.Metadata, left.Records, right.Metadata, right.Records, progress: null);

        Assert.Empty(results);
    }

    #endregion

    #region Metadata

    [Fact]
    public async Task MetadataFieldsBesidesId_AreIgnored()
    {
        var left = Sample();
        var right = Sample();

        var relabelled = right.Metadata with
        {
            Path = @"D:\somewhere\else",
            Label = "A different label",
            Description = "A different description",
            LastScanned = TestTree.DefaultTime.AddYears(1)
        };

        var results = await _service.CompareAsync(left.Metadata, left.Records, relabelled, right.Records);

        Assert.Empty(results);
    }

    #endregion
}
