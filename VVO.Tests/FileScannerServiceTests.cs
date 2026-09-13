using VVO.Core.Services;

namespace VVO.Tests;

public class FileScannerServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FileScannerService _service;

    public FileScannerServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"VVO_Test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_testRoot);
        _service = new FileScannerService();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    private void CreateFile(string relativePath, int sizeBytes)
    {
        var fullPath = Path.Combine(_testRoot, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir!);
        }
        File.WriteAllBytes(fullPath, new byte[sizeBytes]);
    }
        
    [Fact]
    public async Task ReadFileAsync_ReturnsTheFileAsACatalogueRecord()
    {
        CreateFile("notes.txt", 20);

        var record = await _service.ReadFileAsync(Path.Combine(_testRoot, "notes.txt"));

        Assert.Equal("notes.txt", record.Name);
        Assert.False(record.IsFolder);
        Assert.Equal(20, record.Size);
        Assert.Equal(Guid.Empty, record.RootFolderId);
        Assert.Null(record.ParentId);
    }

    [Fact]
    public async Task ReadFileAsync_RejectsAMissingFile()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _service.ReadFileAsync(Path.Combine(_testRoot, "gone.txt")));
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootEmpty()
    {
        // 1 root -> empty
        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.IsFolder && r.ParentId == null);
        Assert.Equal(0, rootRecord.Size);
        Assert.Single(records);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithFile10Bytes()
    {
        // 2 root -> file 10 bytes
        CreateFile("file.txt", 10);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var fileRecord = records.Single(r => !r.IsFolder);

        Assert.Equal(2, records.Count());
        Assert.Equal(10, fileRecord.Size);
        Assert.Equal(10, rootRecord.Size);
        Assert.Equal(rootRecord.Id, fileRecord.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithFolderFile10AndFile20()
    {
        // 3 root -> (folder -> file 10 bytes), file 20 bytes
        CreateFile(Path.Combine("folder", "file1.txt"), 10);
        CreateFile("file2.txt", 20);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var folderRecord = records.Single(r => r.IsFolder && r.ParentId != null);
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");

        Assert.Equal(4, records.Count());
        Assert.Equal(10, file1.Size);
        Assert.Equal(20, file2.Size);
        Assert.Equal(10, folderRecord.Size);
        Assert.Equal(30, rootRecord.Size);
            
        Assert.Equal(folderRecord.Id, file1.ParentId);
        Assert.Equal(rootRecord.Id, folderRecord.ParentId);
        Assert.Equal(rootRecord.Id, file2.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithTwoFolders()
    {
        // 4 root -> (folder1 -> file 10 bytes), (folder2 -> file 20 bytes)
        CreateFile(Path.Combine("folder1", "file1.txt"), 10);
        CreateFile(Path.Combine("folder2", "file2.txt"), 20);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var folder1 = records.Single(r => r.IsFolder && r.Name == "folder1");
        var folder2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");

        Assert.Equal(5, records.Count());
        Assert.Equal(10, file1.Size);
        Assert.Equal(20, file2.Size);
        Assert.Equal(10, folder1.Size);
        Assert.Equal(20, folder2.Size);
        Assert.Equal(30, rootRecord.Size);

        Assert.Equal(folder1.Id, file1.ParentId);
        Assert.Equal(folder2.Id, file2.ParentId);
        Assert.Equal(rootRecord.Id, folder1.ParentId);
        Assert.Equal(rootRecord.Id, folder2.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithDeeplyNestedFolder()
    {
        // 5 root -> (folder -> (folder -> (folder -> file 10 bytes)))
        CreateFile(Path.Combine("folder", "folder2", "folder3", "file.txt"), 10);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var f3 = records.Single(r => r.IsFolder && r.Name == "folder3");
        var file = records.Single(r => r.Name == "file.txt");

        Assert.Equal(5, records.Count());
        Assert.Equal(10, file.Size);
        Assert.Equal(10, f3.Size);
        Assert.Equal(10, f2.Size);
        Assert.Equal(10, f1.Size);
        Assert.Equal(10, rootRecord.Size);

        Assert.Equal(f3.Id, file.ParentId);
        Assert.Equal(f2.Id, f3.ParentId);
        Assert.Equal(f1.Id, f2.ParentId);
        Assert.Equal(rootRecord.Id, f1.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithDeeplyNestedFoldersAndFiles()
    {
        // 6 root -> (folder -> (folder -> (folder -> (folder -> file 50 bytes), file 10 bytes)))
        CreateFile(Path.Combine("folder", "folder2", "folder3", "folder4", "file1.txt"), 50);
        CreateFile(Path.Combine("folder", "folder2", "folder3", "file2.txt"), 10);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var f3 = records.Single(r => r.IsFolder && r.Name == "folder3");
        var f4 = records.Single(r => r.IsFolder && r.Name == "folder4");
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");

        Assert.Equal(7, records.Count());
        Assert.Equal(50, file1.Size);
        Assert.Equal(10, file2.Size);
        Assert.Equal(50, f4.Size);
        Assert.Equal(60, f3.Size);
        Assert.Equal(60, f2.Size);
        Assert.Equal(60, f1.Size);
        Assert.Equal(60, rootRecord.Size);

        Assert.Equal(f4.Id, file1.ParentId);
        Assert.Equal(f3.Id, f4.ParentId);
        Assert.Equal(f3.Id, file2.ParentId);
        Assert.Equal(f2.Id, f3.ParentId);
        Assert.Equal(f1.Id, f2.ParentId);
        Assert.Equal(rootRecord.Id, f1.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithEmptyFolder()
    {
        // 7 root -> (folder -> empty)
        Directory.CreateDirectory(Path.Combine(_testRoot, "folder"));

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var folder = records.Single(r => r.IsFolder && r.Name == "folder");

        Assert.Equal(2, records.Count());
        Assert.Equal(0, folder.Size);
        Assert.Equal(0, rootRecord.Size);

        Assert.Equal(rootRecord.Id, folder.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithMultipleEmptyFolders()
    {
        // 8 root -> (folder -> empty), (folder -> (folder -> empty))
        Directory.CreateDirectory(Path.Combine(_testRoot, "folder1"));
        Directory.CreateDirectory(Path.Combine(_testRoot, "folder2", "folder3"));

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder1");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var f3 = records.Single(r => r.IsFolder && r.Name == "folder3");

        Assert.Equal(4, records.Count());
        Assert.Equal(0, f1.Size);
        Assert.Equal(0, f2.Size);
        Assert.Equal(0, f3.Size);
        Assert.Equal(0, rootRecord.Size);

        Assert.Equal(rootRecord.Id, f1.ParentId);
        Assert.Equal(rootRecord.Id, f2.ParentId);
        Assert.Equal(f2.Id, f3.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithMixedEmptyFoldersAndFiles()
    {
        // 9 root -> (folder1 -> file 10 bytes), (folder2 -> empty), file 20 bytes
        CreateFile(Path.Combine("folder1", "file1.txt"), 10);
        Directory.CreateDirectory(Path.Combine(_testRoot, "folder2"));
        CreateFile("file2.txt", 20);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder1");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");

        Assert.Equal(5, records.Count());
        Assert.Equal(10, file1.Size);
        Assert.Equal(20, file2.Size);
        Assert.Equal(10, f1.Size);
        Assert.Equal(0, f2.Size);
        Assert.Equal(30, rootRecord.Size);

        Assert.Equal(f1.Id, file1.ParentId);
        Assert.Equal(rootRecord.Id, f1.ParentId);
        Assert.Equal(rootRecord.Id, f2.ParentId);
        Assert.Equal(rootRecord.Id, file2.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithMultipleFiles()
    {
        // 10 root -> file 10 bytes, file 20 bytes, file 30 bytes, file 40 bytes, file 50 bytes
        CreateFile("file1.txt", 10);
        CreateFile("file2.txt", 20);
        CreateFile("file3.txt", 30);
        CreateFile("file4.txt", 40);
        CreateFile("file5.txt", 50);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var files = records.Where(r => !r.IsFolder).ToList();

        Assert.Equal(6, records.Count());
        Assert.Equal(5, files.Count);
        Assert.Equal(150, rootRecord.Size);

        foreach (var file in files)
        {
            Assert.Equal(rootRecord.Id, file.ParentId);
        }
        Assert.Contains(files, f => f.Size == 10);
        Assert.Contains(files, f => f.Size == 20);
        Assert.Contains(files, f => f.Size == 30);
        Assert.Contains(files, f => f.Size == 40);
        Assert.Contains(files, f => f.Size == 50);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithFoldersContainingMultipleFiles()
    {
        // 11 root -> (folder1 -> file 10 bytes, file 20 bytes, file 30 bytes), (folder2 -> file 40 bytes, file 50 bytes)
        CreateFile(Path.Combine("folder1", "file1.txt"), 10);
        CreateFile(Path.Combine("folder1", "file2.txt"), 20);
        CreateFile(Path.Combine("folder1", "file3.txt"), 30);
        CreateFile(Path.Combine("folder2", "file4.txt"), 40);
        CreateFile(Path.Combine("folder2", "file5.txt"), 50);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder1");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var files = records.Where(r => !r.IsFolder).ToList();

        Assert.Equal(8, records.Count());
        Assert.Equal(60, f1.Size);
        Assert.Equal(90, f2.Size);
        Assert.Equal(150, rootRecord.Size);

        Assert.Equal(3, files.Count(f => f.ParentId == f1.Id));
        Assert.Equal(2, files.Count(f => f.ParentId == f2.Id));
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithMixedDeepNesting()
    {
        // 12 root -> (folder1 -> file1 10 bytes, (folder2 -> file2 20 bytes)), file3 30 bytes
        CreateFile(Path.Combine("folder1", "file1.txt"), 10);
        CreateFile(Path.Combine("folder1", "folder2", "file2.txt"), 20);
        CreateFile("file3.txt", 30);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder1");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");
        var file3 = records.Single(r => r.Name == "file3.txt");

        Assert.Equal(6, records.Count());
        Assert.Equal(10, file1.Size);
        Assert.Equal(20, file2.Size);
        Assert.Equal(30, file3.Size);
        Assert.Equal(20, f2.Size);
        Assert.Equal(30, f1.Size);
        Assert.Equal(60, rootRecord.Size);

        Assert.Equal(rootRecord.Id, f1.ParentId);
        Assert.Equal(f1.Id, f2.ParentId);
        Assert.Equal(f1.Id, file1.ParentId);
        Assert.Equal(f2.Id, file2.ParentId);
        Assert.Equal(rootRecord.Id, file3.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithComplexMixedNesting()
    {
        // 13 root -> (folder1 -> (folder2 -> file1 10 bytes), file2 20 bytes), (folder3 -> file3 30 bytes, (folder4 -> empty)), file4 40 bytes
        CreateFile(Path.Combine("folder1", "folder2", "file1.txt"), 10);
        CreateFile(Path.Combine("folder1", "file2.txt"), 20);
        CreateFile(Path.Combine("folder3", "file3.txt"), 30);
        Directory.CreateDirectory(Path.Combine(_testRoot, "folder3", "folder4"));
        CreateFile("file4.txt", 40);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var f1 = records.Single(r => r.IsFolder && r.Name == "folder1");
        var f2 = records.Single(r => r.IsFolder && r.Name == "folder2");
        var f3 = records.Single(r => r.IsFolder && r.Name == "folder3");
        var f4 = records.Single(r => r.IsFolder && r.Name == "folder4");
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");
        var file3 = records.Single(r => r.Name == "file3.txt");
        var file4 = records.Single(r => r.Name == "file4.txt");

        Assert.Equal(9, records.Count());
        Assert.Equal(10, file1.Size);
        Assert.Equal(20, file2.Size);
        Assert.Equal(30, file3.Size);
        Assert.Equal(40, file4.Size);
            
        Assert.Equal(10, f2.Size);
        Assert.Equal(30, f1.Size);
        Assert.Equal(0, f4.Size);
        Assert.Equal(30, f3.Size);
        Assert.Equal(100, rootRecord.Size);

        Assert.Equal(rootRecord.Id, f1.ParentId);
        Assert.Equal(rootRecord.Id, f3.ParentId);
        Assert.Equal(f1.Id, f2.ParentId);
        Assert.Equal(f3.Id, f4.ParentId);
            
        Assert.Equal(f2.Id, file1.ParentId);
        Assert.Equal(f1.Id, file2.ParentId);
        Assert.Equal(f3.Id, file3.ParentId);
        Assert.Equal(rootRecord.Id, file4.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithZeroByteFile()
    {
        // 14 root -> file 0 bytes
        CreateFile("file.txt", 0);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var file = records.Single(r => !r.IsFolder);

        Assert.Equal(2, records.Count());
        Assert.Equal(0, file.Size);
        Assert.Equal(0, rootRecord.Size);

        Assert.Equal(rootRecord.Id, file.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootWithZeroByteFileInFolder()
    {
        // 15 root -> (folder -> file 0 bytes), file 10 bytes
        CreateFile(Path.Combine("folder", "file1.txt"), 0);
        CreateFile("file2.txt", 10);

        var (metadata, records) = await _service.ScanDirectoryAsync(_testRoot);

        var rootRecord = records.Single(r => r.ParentId == null);
        var folder = records.Single(r => r.IsFolder && r.Name == "folder");
        var file1 = records.Single(r => r.Name == "file1.txt");
        var file2 = records.Single(r => r.Name == "file2.txt");

        Assert.Equal(4, records.Count());
        Assert.Equal(0, file1.Size);
        Assert.Equal(10, file2.Size);
        Assert.Equal(0, folder.Size);
        Assert.Equal(10, rootRecord.Size);

        Assert.Equal(folder.Id, file1.ParentId);
        Assert.Equal(rootRecord.Id, folder.ParentId);
        Assert.Equal(rootRecord.Id, file2.ParentId);
    }

    [Fact]
    public async Task ScanDirectoryAsync_PopulatesTimestamps()
    {
        CreateFile(Path.Combine("folder", "file.txt"), 10);

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot);

        Assert.All(records, r =>
        {
            Assert.NotEqual(default(DateTime), r.Created);
            Assert.NotEqual(default(DateTime), r.Modified);
            Assert.Equal(DateTimeKind.Utc, r.Created.Kind);
            Assert.Equal(DateTimeKind.Utc, r.Modified.Kind);
        });
    }

    [Fact]
    public async Task ScanDirectoryAsync_TimestampsAreMillisecondTruncated()
    {
        CreateFile(Path.Combine("folder", "file1.txt"), 10);
        CreateFile("file2.txt", 20);

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot);

        Assert.All(records, r =>
        {
            Assert.Equal(0, r.Created.Ticks % TimeSpan.TicksPerMillisecond);
            Assert.Equal(0, r.Modified.Ticks % TimeSpan.TicksPerMillisecond);
        });
    }

    [Fact]
    public async Task ScanDirectoryAsync_ModifiedMatchesTheFileSystem()
    {
        CreateFile("file.txt", 10);
        var actual = File.GetLastWriteTimeUtc(Path.Combine(_testRoot, "file.txt"));

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot);
        var scanned = records.Single(r => r.Name == "file.txt").Modified;

        // Truncation only ever rounds down, so the scanned value trails the real one
        Assert.InRange(actual - scanned, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ScanDirectoryAsync_RootRecordHasTimestamps()
    {
        CreateFile("file.txt", 10);
        var actual = Directory.GetLastWriteTimeUtc(_testRoot);

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot);
        var rootRecord = records.Single(r => r.ParentId == null);

        Assert.NotEqual(default(DateTime), rootRecord.Created);
        Assert.Equal(DateTimeKind.Utc, rootRecord.Modified.Kind);
        Assert.InRange(actual - rootRecord.Modified, TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task ScanDirectoryAsync_FolderTimestampsSurviveSizeAssignment()
    {
        CreateFile(Path.Combine("folder", "file.txt"), 10);

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot);
        var folder = records.Single(r => r.IsFolder && r.Name == "folder");

        Assert.Equal(10, folder.Size);
        Assert.NotEqual(default(DateTime), folder.Modified);
        Assert.Equal(DateTimeKind.Utc, folder.Modified.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ScanDirectoryAsync_RejectsABlankPath(string path)
    {
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _service.ScanDirectoryAsync(path));
    }

    [Fact]
    public async Task ScanDirectoryAsync_RejectsAPathThatIsNotThere()
    {
        var missing = Path.Combine(_testRoot, "no-such-folder");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => _service.ScanDirectoryAsync(missing));
    }

    [Fact]
    public async Task ScanDirectoryAsync_ReportsItsProgress()
    {
        CreateFile("a.txt", 10);
        var reported = new List<string>();

        await _service.ScanDirectoryAsync(_testRoot, new Progress<string>(reported.Add));

        // Progress arrives on the captured context, so give the posted callbacks a turn
        await Task.Yield();
        Assert.NotEmpty(reported);
    }

    // The count is what tells a long scan apart from a hung one
    [Fact]
    public async Task ScanDirectoryAsync_ReportsHowMuchItHasFound()
    {
        for (var i = 0; i < 50; i++)
        {
            CreateFile($"file{i}.txt", 1);
        }

        var reported = new List<string>();
        await _service.ScanDirectoryAsync(_testRoot, new Progress<string>(reported.Add));
        await Task.Yield();

        Assert.Contains(reported, message => message.Contains("50") && message.Contains("entries"));
    }

    #region Hidden and system entries

    private void CreateHiddenFile(string name, int sizeBytes)
    {
        CreateFile(name, sizeBytes);
        File.SetAttributes(Path.Combine(_testRoot, name), FileAttributes.Hidden);
    }

    // The default a scan inherits, which on a system drive drops ProgramData and every AppData
    [Fact]
    public async Task ScanDirectoryAsync_LeavesOutHiddenEntriesByDefault()
    {
        CreateFile("visible.txt", 10);
        CreateHiddenFile("hidden.txt", 20);

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot);

        Assert.Contains(records, record => record.Name == "visible.txt");
        Assert.DoesNotContain(records, record => record.Name == "hidden.txt");
    }

    [Fact]
    public async Task ScanDirectoryAsync_TakesHiddenEntriesWhenAskedTo()
    {
        CreateFile("visible.txt", 10);
        CreateHiddenFile("hidden.txt", 20);

        var scan = await _service.ScanDirectoryAsync(_testRoot, includeHiddenAndSystem: true);

        Assert.Contains(scan.Records, record => record.Name == "hidden.txt");
        Assert.Equal(0, scan.SkippedFolders);
    }

    // Left out on purpose is not the same as refused, and only the refusals are warned about
    [Fact]
    public async Task ScanDirectoryAsync_DoesNotCountHiddenEntriesAsSkipped()
    {
        CreateHiddenFile("hidden.txt", 20);

        var scan = await _service.ScanDirectoryAsync(_testRoot);

        Assert.Equal(0, scan.SkippedFolders);
    }

    [Fact]
    public async Task ScanDirectoryAsync_CountsHiddenFilesTowardsTheirFolderWhenIncluded()
    {
        CreateFile("visible.txt", 10);
        CreateHiddenFile("hidden.txt", 20);

        var scan = await _service.ScanDirectoryAsync(_testRoot, includeHiddenAndSystem: true);
        var root = scan.Records.Single(record => record.Id == scan.Metadata.TreeId);

        Assert.Equal(30, root.Size);
    }

    #endregion

    [Fact]
    public async Task ScanDirectoryAsync_RunsWithNothingListening()
    {
        CreateFile("a.txt", 10);

        var (_, records) = await _service.ScanDirectoryAsync(_testRoot, progress: null);

        Assert.Equal(2, records.Count());
    }

    [Fact]
    public async Task ScanDirectoryAsync_StopsWhenCancelled()
    {
        CreateFile("a.txt", 10);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _service.ScanDirectoryAsync(_testRoot, null, cancellation.Token));
    }


    #region Names that are not text

    // Built here rather than passed in: a lone surrogate does not survive being serialised as
    // theory data, and would reach the test as something else entirely
    private static string Half(params int[] codeUnits) =>
        new string(codeUnits.Select(unit => (char)unit).ToArray());

    private const int HighSurrogate = 0xD800;
    private const int LowSurrogate = 0xDC6D;
    private const string Replacement = "\uFFFD";

    // Windows stores a name as UTF-16 code units and never checks that they spell anything, so
    // a stray half of a surrogate pair is a name a real drive can hold
    [Fact]
    public async Task AFileNamedWithAStrayLowSurrogateIsCataloguedUnderTheReplacementCharacter()
    {
        CreateFile($"a{Half(LowSurrogate)}.txt", 10);

        Assert.Equal($"a{Replacement}.txt", await ScannedFileNameAsync());
    }

    [Fact]
    public async Task AFileNamedWithAStrayHighSurrogateIsCataloguedUnderTheReplacementCharacter()
    {
        CreateFile($"a{Half(HighSurrogate)}.txt", 10);

        Assert.Equal($"a{Replacement}.txt", await ScannedFileNameAsync());
    }

    [Fact]
    public async Task EveryStrayHalfInANameIsReplaced()
    {
        CreateFile($"a{Half(HighSurrogate, HighSurrogate)}.txt", 10);

        Assert.Equal($"a{Replacement}{Replacement}.txt", await ScannedFileNameAsync());
    }

    [Fact]
    public async Task AWholeCharacterOutsideTheBasicPlaneIsLeftAsItIs()
    {
        CreateFile("a\U0001F600.txt", 10);

        Assert.Equal("a\U0001F600.txt", await ScannedFileNameAsync());
    }

    [Fact]
    public async Task AFolderNamedWithHalfACharacterIsCataloguedToo()
    {
        Directory.CreateDirectory(Path.Combine(_testRoot, $"sub{Half(LowSurrogate)}"));

        var scan = await _service.ScanDirectoryAsync(_testRoot);
        var folder = scan.Records.Single(record => record.IsFolder && record.ParentId != null);

        Assert.Equal($"sub{Replacement}", folder.Name);
    }

    private async Task<string> ScannedFileNameAsync()
    {
        var scan = await _service.ScanDirectoryAsync(_testRoot);
        var name = scan.Records.Single(record => !record.IsFolder).Name;

        // The property that matters, and the one the database enforces: a whole character made
        // of two surrogates is fine, half of one is not
        var strict = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true);
        var encoded = Record.Exception(() => strict.GetBytes(name));
        Assert.Null(encoded);

        return name;
    }

    #endregion
}
