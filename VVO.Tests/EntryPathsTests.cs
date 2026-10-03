using VVO.Core;
using VVO.Core.Services;

namespace VVO.Tests;

public class EntryPathsTests
{
    private static readonly FolderTree Code = TestTree.Root("Code", path: @"C:\Code")
        .File("readme.md", 10)
        .Folder("AdventOfCode", folder => folder
            .Folder("2024", year => year.File("day1.cs", 5)))
        .Build();

    private static EntryPaths PathsOf(FolderTree tree, string volumeName = "test") =>
        new(tree.Metadata, volumeName, tree.Records);

    [Fact]
    public void TheRootIsTheFolderBelowTheVolume()
    {
        var paths = PathsOf(Code);

        Assert.Equal(@"test:\Code", paths.CataloguePathOf(Code.Metadata.TreeId));
        Assert.Equal(@"C:\Code", paths.PhysicalPathOf(Code.Metadata.TreeId));
    }

    [Fact]
    public void ADirectChildHangsOffTheRoot()
    {
        var paths = PathsOf(Code);
        var readme = Code.Record("readme.md").Id;

        Assert.Equal(@"test:\Code\readme.md", paths.CataloguePathOf(readme));
        Assert.Equal(@"C:\Code\readme.md", paths.PhysicalPathOf(readme));
    }

    [Fact]
    public void ADeeplyNestedRecordCarriesEveryFolderAboveIt()
    {
        var paths = PathsOf(Code);
        var day1 = Code.Record("day1.cs").Id;

        Assert.Equal(@"test:\Code\AdventOfCode\2024\day1.cs", paths.CataloguePathOf(day1));
        Assert.Equal(@"C:\Code\AdventOfCode\2024\day1.cs", paths.PhysicalPathOf(day1));
    }

    [Fact]
    public void ALabelTakesThePlaceOfTheScannedName()
    {
        var labelled = Code with { Metadata = Code.Metadata with { Label = "Projects" } };
        var paths = PathsOf(labelled);

        Assert.Equal("Projects", paths.Title);
        Assert.Equal(@"test:\Projects\readme.md", paths.CataloguePathOf(Code.Record("readme.md").Id));

        // The disk still has the folder under the name it was scanned with
        Assert.Equal(@"C:\Code\readme.md", paths.PhysicalPathOf(Code.Record("readme.md").Id));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void WithoutALabelTheTitleIsTheScannedName(string? label)
    {
        var unlabelled = Code with { Metadata = Code.Metadata with { Label = label } };

        Assert.Equal("Code", PathsOf(unlabelled).Title);
    }

    [Fact]
    public void TwoEntriesSharingATreePlaceARecordDifferently()
    {
        var copy = Code.Metadata with { Id = Guid.NewGuid(), Label = "Backup", Path = @"E:\Code" };
        var readme = Code.Record("readme.md").Id;

        var original = PathsOf(Code, "test");
        var copied = new EntryPaths(copy, "archive", Code.Records);

        Assert.Equal(@"test:\Code\readme.md", original.CataloguePathOf(readme));
        Assert.Equal(@"archive:\Backup\readme.md", copied.CataloguePathOf(readme));
        Assert.Equal(@"E:\Code\readme.md", copied.PhysicalPathOf(readme));
    }

    [Fact]
    public void AnEntryWithNoPathOnDiskHasNoPhysicalPaths()
    {
        var pathless = Code with { Metadata = Code.Metadata with { Path = string.Empty } };
        var paths = PathsOf(pathless);

        Assert.Empty(paths.PhysicalPathOf(Code.Metadata.TreeId));
        Assert.Empty(paths.PhysicalPathOf(Code.Record("day1.cs").Id));
        Assert.Equal(@"test:\Code\AdventOfCode\2024\day1.cs", paths.CataloguePathOf(Code.Record("day1.cs").Id));
    }

    [Fact]
    public void ARecordOutsideTheTreeIsNotFound()
    {
        Assert.Throws<CatalogueItemNotFoundException>(() => PathsOf(Code).CataloguePathOf(Guid.NewGuid()));
    }

    [Fact]
    public void ARecordThatLostItsParentIsAnErrorRatherThanAWrongPath()
    {
        var orphaned = Code.Records
            .Where(record => record.Name != "2024")
            .ToList();

        var paths = new EntryPaths(Code.Metadata, "test", orphaned);

        Assert.Throws<InvalidOperationException>(() => paths.CataloguePathOf(Code.Record("day1.cs").Id));
    }

    [Fact]
    public void ParentsRunningInACircleAreAnErrorRatherThanAHang()
    {
        var a = Code.Record("AdventOfCode");
        var b = Code.Record("2024");
        var circular = Code.Records
            .Select(record => record.Id == a.Id ? record with { ParentId = b.Id } : record)
            .ToList();

        var paths = new EntryPaths(Code.Metadata, "test", circular);

        Assert.Throws<InvalidOperationException>(() => paths.CataloguePathOf(Code.Record("day1.cs").Id));
    }

    [Fact]
    public void RecordsWithoutTheRootAreRefused()
    {
        var rootless = Code.Records.Where(record => record.Id != Code.Metadata.TreeId);

        Assert.Throws<ArgumentException>(() => new EntryPaths(Code.Metadata, "test", rootless));
    }
}
