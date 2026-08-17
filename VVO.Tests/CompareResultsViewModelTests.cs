using VVO.Core.Models;
using VVO.UI.ViewModels;

namespace VVO.Tests;

public class CompareResultsViewModelTests
{
    private const string SourceRoot = @"D:\Source";
    private const string TargetRoot = @"E:\Target";

    private static CompareResultsViewModel Results(params ComparisonResult[] results)
    {
        return new CompareResultsViewModel("Source", SourceRoot, "Target", TargetRoot, results);
    }

    private static ComparisonResult Result(
        string path,
        ComparisonStatus status,
        ChangeKind changes = ChangeKind.None,
        long leftSize = 0,
        long rightSize = 0,
        int? descendants = null,
        bool isFolder = false,
        string? name = null)
    {
        FileRecord? Record(long size) => new()
        {
            Id = Guid.NewGuid(),
            IsFolder = isFolder,
            Name = name ?? System.IO.Path.GetFileName(path),
            Size = size
        };

        return new ComparisonResult
        {
            RelativePath = path,
            Status = status,
            Changes = changes,
            Left = status == ComparisonStatus.Added ? null : Record(leftSize),
            Right = status == ComparisonStatus.Removed ? null : Record(rightSize),
            DescendantCount = descendants
        };
    }

    #region Which rows are listed

    [Fact]
    public void NoDifferencesLeavesTheGridEmpty()
    {
        var viewModel = Results();

        Assert.False(viewModel.HasDifferences);
        Assert.Empty(viewModel.Rows);
    }

    // Those rows exist to give a tree its parents and are only noise in a flat list
    [Fact]
    public void AFolderThatOnlyDiffersBelowItselfIsNotListed()
    {
        var viewModel = Results(
            Result("parent", ComparisonStatus.Changed, ChangeKind.None, isFolder: true),
            Result(@"parent\file.txt", ComparisonStatus.Changed, ChangeKind.Size));

        Assert.Equal([@"parent\file.txt"], viewModel.Rows.Select(row => row.Path));
    }

    [Fact]
    public void AddedAndRemovedRowsAreAlwaysListed()
    {
        var viewModel = Results(
            Result("gone.txt", ComparisonStatus.Removed),
            Result("new.txt", ComparisonStatus.Added));

        Assert.True(viewModel.HasDifferences);
        Assert.Equal(2, viewModel.Rows.Count);
    }

    [Fact]
    public void EachKindOfDifferenceIsFlaggedForItsCopyButton()
    {
        var viewModel = Results(
            Result("gone.txt", ComparisonStatus.Removed),
            Result("changed.txt", ComparisonStatus.Changed, ChangeKind.Size));

        Assert.True(viewModel.HasRemoved);
        Assert.True(viewModel.HasChanged);
        Assert.False(viewModel.HasAdded);
    }

    #endregion

    #region How a row reads

    [Theory]
    [InlineData(ComparisonStatus.Added, "+")]
    [InlineData(ComparisonStatus.Removed, "−")]
    [InlineData(ComparisonStatus.Changed, "~")]
    public void TheStatusIsShownAsASymbolAndNamedInTheTooltip(ComparisonStatus status, string symbol)
    {
        var row = Assert.Single(Results(Result("a.txt", status, ChangeKind.Size)).Rows);

        Assert.Equal(symbol, row.Symbol);
        Assert.Equal(status.ToString(), row.StatusName);
    }

    [Theory]
    [InlineData(ChangeKind.Size, "size")]
    [InlineData(ChangeKind.Modified, "modified")]
    [InlineData(ChangeKind.Size | ChangeKind.Modified, "size, modified")]
    public void WhatChangedIsSpelledOut(ChangeKind changes, string expected)
    {
        var row = Assert.Single(Results(Result("a.txt", ComparisonStatus.Changed, changes)).Rows);

        Assert.Equal(expected, row.Detail);
    }

    [Fact]
    public void AnAddedOrRemovedRowHasNothingToSayAboutWhatChanged()
    {
        var row = Assert.Single(Results(Result("a.txt", ComparisonStatus.Added)).Rows);

        Assert.Equal(string.Empty, row.Detail);
    }

    [Fact]
    public void BothSidesOfASizeAreShownAndSortedOnTheirBytes()
    {
        var row = Assert.Single(Results(
            Result("a.txt", ComparisonStatus.Changed, ChangeKind.Size, leftSize: 1024, rightSize: 2048)).Rows);

        Assert.Equal("1 KB", row.LeftSize);
        Assert.Equal(1024, row.LeftSizeSortPath);
        Assert.Equal("2 KB", row.RightSize);
        Assert.Equal(2048, row.RightSizeSortPath);
    }

    [Fact]
    public void TheMissingSideOfAnAddedRowIsBlankRatherThanZero()
    {
        var added = Assert.Single(Results(Result("a.txt", ComparisonStatus.Added, rightSize: 10)).Rows);
        var removed = Assert.Single(Results(Result("b.txt", ComparisonStatus.Removed, leftSize: 10)).Rows);

        Assert.Equal(string.Empty, added.LeftSize);
        Assert.Equal(string.Empty, removed.RightSize);
    }

    [Fact]
    public void ACollapsedSubtreeCountsItselfAndEverythingUnderIt()
    {
        var row = Assert.Single(Results(
            Result("folder", ComparisonStatus.Added, descendants: 4, isFolder: true)).Rows);

        Assert.Equal(5, row.Items);
    }

    [Fact]
    public void ASingleFileCountsAsOneItem()
    {
        var row = Assert.Single(Results(Result("a.txt", ComparisonStatus.Added)).Rows);

        Assert.Equal(1, row.Items);
    }

    [Fact]
    public void ARowIsIconedByWhatItIs()
    {
        var file = Assert.Single(Results(Result("a.mp3", ComparisonStatus.Added)).Rows);
        var folder = Assert.Single(Results(Result("art", ComparisonStatus.Added, isFolder: true)).Rows);

        Assert.Equal("FileAudioSolid", file.IconKey);
        Assert.Equal("FolderSolid", folder.IconKey);
    }

    #endregion

    #region The summary line

    [Fact]
    public void TheSummaryAddsUpBothSidesWithTheirBytes()
    {
        var viewModel = Results(
            Result("gone.txt", ComparisonStatus.Removed, leftSize: 1024),
            Result("new.txt", ComparisonStatus.Added, rightSize: 2048));

        Assert.Contains("1 removed · 1 KB", viewModel.Summary);
        Assert.Contains("1 added · 2 KB", viewModel.Summary);
    }

    [Fact]
    public void ChangedEntriesAreCountedOnlyWhenThereAreSome()
    {
        var without = Results(Result("gone.txt", ComparisonStatus.Removed));
        var with = Results(
            Result("gone.txt", ComparisonStatus.Removed),
            Result("edited.txt", ComparisonStatus.Changed, ChangeKind.Size));

        Assert.DoesNotContain("changed", without.Summary);
        Assert.Contains("1 changed", with.Summary);
    }

    // A folder carrying a rollup is never listed alongside its contents, so the rows add up
    [Fact]
    public void ACollapsedSubtreeCountsTowardsTheSummaryAsAWhole()
    {
        var viewModel = Results(
            Result("folder", ComparisonStatus.Removed, leftSize: 500, descendants: 9, isFolder: true));

        Assert.Contains("10 removed · 500 B", viewModel.Summary);
    }

    [Fact]
    public void AFolderThatOnlyDiffersBelowItselfIsNotCountedAsChanged()
    {
        var viewModel = Results(
            Result("parent", ComparisonStatus.Changed, ChangeKind.None, isFolder: true));

        Assert.DoesNotContain("changed", viewModel.Summary);
    }

    [Fact]
    public void AnEmptyComparisonStillReadsAsZeroes()
    {
        Assert.Contains("0 removed · 0 B", Results().Summary);
        Assert.Contains("0 added · 0 B", Results().Summary);
    }

    #endregion

    #region Copying paths

    // An added entry exists only under the target, everything else under the source
    [Fact]
    public void AddedPathsAreCopiedUnderTheTargetRoot()
    {
        var viewModel = Results(Result(@"sub\new.txt", ComparisonStatus.Added));

        Assert.Equal(@"E:\Target\sub\new.txt", viewModel.PathsFor(ComparisonStatus.Added));
    }

    [Fact]
    public void RemovedAndChangedPathsAreCopiedUnderTheSourceRoot()
    {
        var viewModel = Results(
            Result(@"sub\gone.txt", ComparisonStatus.Removed),
            Result(@"sub\edited.txt", ComparisonStatus.Changed, ChangeKind.Size));

        Assert.Equal(@"D:\Source\sub\gone.txt", viewModel.PathsFor(ComparisonStatus.Removed));
        Assert.Equal(@"D:\Source\sub\edited.txt", viewModel.PathsFor(ComparisonStatus.Changed));
    }

    [Fact]
    public void EachCopiedPathIsOnItsOwnLine()
    {
        var viewModel = Results(
            Result("one.txt", ComparisonStatus.Removed),
            Result("two.txt", ComparisonStatus.Removed));

        Assert.Equal(
            [@"D:\Source\one.txt", @"D:\Source\two.txt"],
            viewModel.PathsFor(ComparisonStatus.Removed).Split(Environment.NewLine));
    }

    [Fact]
    public void CopyingAStatusWithNoRowsGivesNothing()
    {
        var viewModel = Results(Result("gone.txt", ComparisonStatus.Removed));

        Assert.Equal(string.Empty, viewModel.PathsFor(ComparisonStatus.Added));
    }

    #endregion
}
