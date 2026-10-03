using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

public class NumberAndTimeTests : IAsyncLifetime
{
    private TempCatalogue _catalogue = null!;
    private RootFolderMetadata _entry = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        var volume = await _catalogue.AddVolumeAsync("Backups");
        _entry = await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("files").File("a.txt", 1).Build());
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private async Task<FileRecord> StoreAsync(Func<FileRecord, FileRecord> shape)
    {
        var record = shape(new FileRecord
        {
            Id = Guid.NewGuid(), RootFolderId = _entry.TreeId, ParentId = _entry.TreeId, Name = "odd.bin",
            Created = TestTree.DefaultTime, Modified = TestTree.DefaultTime
        });

        await _catalogue.Database.InsertItemsAsync([record]);
        return record;
    }

    private async Task<System.Text.Json.JsonElement> StatAsync(FileRecord record) =>
        (await CliRunner.RunAsync("stat", record.Id.ToString(), "--db", _catalogue.Path)).Json.GetProperty("record");

    // JSON readers in JavaScript hold integers exactly up to 2^53; a catalogue of a large drive
    // stays well inside that, but past int.MaxValue a 32-bit slip would show at once
    [Theory]
    [InlineData(2_147_483_648L)]
    [InlineData(18_000_000_000_000L)]
    [InlineData(9_007_199_254_740_992L)]
    public async Task LargeSizesAreWrittenExactly(long size)
    {
        var record = await StoreAsync(item => item with { Size = size });

        Assert.Equal(size, (await StatAsync(record)).GetProperty("size").GetInt64());
    }

    public static IEnumerable<object[]> Times() =>
    [
        [new DateTime(1601, 1, 1, 0, 0, 0, DateTimeKind.Utc), "1601-01-01T00:00:00Z"],
        [new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc), "1980-01-01T00:00:00Z"],
    ];

    // LiteDB clamps the very end of the range in local time, so it comes back hours early. No file
    // carries such a date; what matters is that it is still a valid time in UTC
    [Fact]
    public async Task TheLastTimeThereIsStaysAValidUtcTime()
    {
        var last = new DateTime(9999, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var record = await StoreAsync(item => item with { Modified = last });

        var written = (await StatAsync(record)).GetProperty("modified").GetString()!;

        Assert.EndsWith("Z", written);
        Assert.InRange(DateTime.Parse(written, null, System.Globalization.DateTimeStyles.RoundtripKind), last.AddDays(-1), DateTime.MaxValue);
    }

    [Theory]
    [MemberData(nameof(Times))]
    public async Task ExtremeTimesComeOutAsUtc(DateTime time, string expected)
    {
        var record = await StoreAsync(item => item with { Created = time, Modified = time });

        var stat = await StatAsync(record);

        Assert.Equal(expected, stat.GetProperty("created").GetString());
        Assert.Equal(expected, stat.GetProperty("modified").GetString());
    }

    // An old catalogue can hold a time written as local; it comes out as the same moment in UTC
    [Fact]
    public async Task ALocalTimeComesOutAsTheSameMomentInUtc()
    {
        var local = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Local);
        var record = await StoreAsync(item => item with { Modified = local });

        var written = (await StatAsync(record)).GetProperty("modified").GetDateTime();

        Assert.Equal(local.ToUniversalTime(), written.ToUniversalTime());
    }

    [Theory]
    [InlineData("search", "a", "--limit", "2147483647")]
    [InlineData("tree", "ENTRY", "--depth", "2147483647")]
    public async Task TheLargestNumbersAreAccepted(params string[] args)
    {
        var result = await CliRunner.RunAsync(
            [.. args.Select(arg => arg == "ENTRY" ? _entry.Id.ToString() : arg), "--db", _catalogue.Path]);

        Assert.Equal(0, result.ExitCode);
    }

    [Theory]
    [InlineData("--limit", "2147483648")]
    [InlineData("--limit", "1e3")]
    [InlineData("--limit", "10abc")]
    [InlineData("--limit", "")]
    [InlineData("--limit", "1.5")]
    [InlineData("--depth", "99999999999")]
    [InlineData("--depth", "two")]
    public async Task ANumberThatDoesNotParseIsAUsageError(string option, string value)
    {
        string[] args = option == "--limit"
            ? ["search", "a", option, value, "--db", _catalogue.Path]
            : ["tree", _entry.Id.ToString(), option, value, "--db", _catalogue.Path];

        var result = await CliRunner.RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }
}
