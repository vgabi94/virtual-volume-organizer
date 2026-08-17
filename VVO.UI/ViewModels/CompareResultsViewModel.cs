using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Collections;
using VVO.Core.Models;

namespace VVO.UI.ViewModels;

public record ComparisonRow
{
    public required ComparisonStatus Status { get; init; }

    // Shown in place of the status name, which is carried by the tooltip instead
    public required string Symbol { get; init; }
    public required string StatusName { get; init; }

    public required string IconKey { get; init; }
    public Avalonia.Media.Geometry? Icon => FileIcons.Lookup(IconKey);
    public bool IsIconFlipped => FileIcons.IsFlipped(IconKey);

    public string Path { get; init; } = string.Empty;

    // Under the root it exists in, which is the same path the copy buttons hand over. The
    // column trims long paths, so hovering is the only way to read one in full.
    public string FullPath { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string LeftSize { get; init; } = string.Empty;
    public long LeftSizeSortPath { get; init; }
    public string RightSize { get; init; } = string.Empty;
    public long RightSizeSortPath { get; init; }
    public int Items { get; init; }
}

public class CompareResultsViewModel
{
    private readonly string _sourceRootPath;
    private readonly string _targetRootPath;

    public string SourceName { get; }
    public string TargetName { get; }
    public string Summary { get; }
    public AvaloniaList<ComparisonRow> Rows { get; }

    /// <summary>
    /// Set when the differences are being put to the user for approval rather than merely
    /// reported, which is what an update asks for before it touches anything.
    /// </summary>
    public bool IsUpdate { get; }

    public string WindowTitle => IsUpdate ? "Update Folder" : "Comparison Results";

    // Turning the update down and closing the window are the same button
    public string CloseText => IsUpdate ? "Cancel" : "Close";

    public string Proposal => IsUpdate
        ? "Updating replaces what is catalogued for this folder with what was just scanned."
        : string.Empty;

    public bool HasDifferences => Rows.Count > 0;

    public bool HasAdded => Rows.Any(row => row.Status == ComparisonStatus.Added);
    public bool HasRemoved => Rows.Any(row => row.Status == ComparisonStatus.Removed);
    public bool HasChanged => Rows.Any(row => row.Status == ComparisonStatus.Changed);

    /// <summary>
    /// The full paths of one status, one per line. A subtree that is wholly added or wholly
    /// removed is a single row, so what comes back is that folder rather than its contents.
    /// </summary>
    public string PathsFor(ComparisonStatus status)
    {
        // An added entry exists only under the target, everything else under the source
        var root = status == ComparisonStatus.Added ? _targetRootPath : _sourceRootPath;

        return string.Join(Environment.NewLine, Rows
            .Where(row => row.Status == status)
            .Select(row => System.IO.Path.Combine(root, row.Path)));
    }

    public CompareResultsViewModel(
        string sourceName,
        string sourceRootPath,
        string targetName,
        string targetRootPath,
        IReadOnlyList<ComparisonResult> results,
        bool isUpdate = false)
    {
        SourceName = sourceName;
        TargetName = targetName;
        _sourceRootPath = sourceRootPath;
        _targetRootPath = targetRootPath;
        IsUpdate = isUpdate;

        // A folder that merely contains a difference is reported as Changed with no change
        // flags of its own. Those rows exist to give a tree its parents and are only noise
        // in a flat list, where the differences below them are listed anyway.
        Rows = new AvaloniaList<ComparisonRow>(results
            .Where(result => result.Status != ComparisonStatus.Changed || result.Changes != ChangeKind.None)
            .Select(result => ToRow(result, sourceRootPath, targetRootPath)));

        Summary = BuildSummary(results);
    }

    // A collapsed folder carries the rollup of everything beneath it and its descendants are
    // never listed separately, so the rows can simply be added up.
    private static string BuildSummary(IReadOnlyList<ComparisonResult> results)
    {
        var removed = results.Where(result => result.Status == ComparisonStatus.Removed).ToList();
        var added = results.Where(result => result.Status == ComparisonStatus.Added).ToList();
        var changed = results.Count(result =>
            result.Status == ComparisonStatus.Changed && result.Changes != ChangeKind.None);

        var removedBytes = removed.Sum(result => result.Left?.Size ?? 0);
        var addedBytes = added.Sum(result => result.Right?.Size ?? 0);

        var summary =
            $"{CountEntries(removed)} removed · {FormattingUtils.FormatBytes(removedBytes)}" +
            $"     {CountEntries(added)} added · {FormattingUtils.FormatBytes(addedBytes)}";

        return changed > 0 ? $"{summary}     {changed} changed" : summary;
    }

    private static int CountEntries(IEnumerable<ComparisonResult> results)
    {
        return results.Sum(result => 1 + (result.DescendantCount ?? 0));
    }

    private static ComparisonRow ToRow(ComparisonResult result, string sourceRoot, string targetRoot)
    {
        var record = result.Left ?? result.Right;
        var isFolder = record?.IsFolder ?? false;
        var extension = isFolder || record == null ? string.Empty : System.IO.Path.GetExtension(record.Name);

        // An added entry exists only under the target, everything else under the source
        var root = result.Status == ComparisonStatus.Added ? targetRoot : sourceRoot;

        return new ComparisonRow
        {
            Status = result.Status,
            Symbol = SymbolFor(result.Status),
            StatusName = result.Status.ToString(),
            IconKey = FileIcons.KeyFor(extension, isFolder),
            Path = result.RelativePath,
            FullPath = System.IO.Path.Combine(root, result.RelativePath),
            Detail = Describe(result.Changes),
            LeftSize = result.Left == null ? string.Empty : FormattingUtils.FormatBytes(result.Left.Size),
            LeftSizeSortPath = result.Left?.Size ?? 0,
            RightSize = result.Right == null ? string.Empty : FormattingUtils.FormatBytes(result.Right.Size),
            RightSizeSortPath = result.Right?.Size ?? 0,
            Items = 1 + (result.DescendantCount ?? 0)
        };
    }

    private static string SymbolFor(ComparisonStatus status)
    {
        return status switch
        {
            ComparisonStatus.Added => "+",
            ComparisonStatus.Removed => "−",
            _ => "~"
        };
    }

    private static string Describe(ChangeKind changes)
    {
        if (changes == ChangeKind.None)
            return string.Empty;

        var parts = new List<string>();
        if (changes.HasFlag(ChangeKind.Size))
        {
            parts.Add("size");
        }

        if (changes.HasFlag(ChangeKind.Modified))
        {
            parts.Add("modified");
        }

        return string.Join(", ", parts);
    }
}
