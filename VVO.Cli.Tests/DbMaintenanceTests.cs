using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests;

public class DbMaintenanceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"VVO_DbMaintenance_{Guid.NewGuid()}");
    private TempCatalogue _catalogue = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_directory);

        _catalogue = await TempCatalogue.CreateAsync();
        var backups = await _catalogue.AddVolumeAsync("Backups");
        await _catalogue.AddFolderAsync(backups.Id, TestTree.Root("photos").File("a.jpg", 10).Folder("2024", y => y.File("b.jpg", 20)).Build());
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        try { Directory.Delete(_directory, true); } catch { }
        return Task.CompletedTask;
    }

    private string PathOf(string name) => Path.Combine(_directory, name);

    private Task<CliResult> RunAsync(params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path]);

    private Task<CliResult> RunAsync(ScriptedTerminal terminal, params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path], terminal: terminal);

    private static async Task<JsonElement> InfoOf(string path) =>
        (await CliRunner.RunAsync("db", "info", "--db", path)).Json;

    #region copy

    [Fact]
    public async Task ACopyHoldsTheSameCatalogue()
    {
        var target = PathOf("copy.vvo");

        var result = await RunAsync("db", "copy", target);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(target, result.Json.GetProperty("path").GetString());

        var copy = await InfoOf(target);
        Assert.Equal(1, copy.GetProperty("volumeCount").GetInt32());
        Assert.Equal(3, copy.GetProperty("recordCount").GetInt32());
    }

    [Fact]
    public async Task CopyingOverAFileTakesTheUsersWord()
    {
        var target = PathOf("copy.vvo");
        File.WriteAllText(target, "old");

        var result = await RunAsync(ScriptedTerminal.Typing("replace"), "db", "copy", target);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, (await InfoOf(target)).GetProperty("volumeCount").GetInt32());
    }

    [Theory]
    [InlineData(true, 6)]
    [InlineData(false, 4)]
    public async Task WithoutTheUsersWordTheTargetIsLeftAlone(bool interactive, int exitCode)
    {
        var target = PathOf("copy.vvo");
        File.WriteAllText(target, "old");

        var terminal = interactive ? ScriptedTerminal.Typing("") : ScriptedTerminal.Redirected();
        var result = await RunAsync(terminal, "db", "copy", target);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal("old", File.ReadAllText(target));
    }

    [Fact]
    public async Task ATargetHeldByAnotherProgramIsNotReportedAsTheCatalogueBeingBusy()
    {
        var target = PathOf("held.vvo");
        File.WriteAllText(target, "old");
        using var hold = new FileStream(target, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var waited = System.Diagnostics.Stopwatch.StartNew();

        var result = await RunAsync(ScriptedTerminal.Typing("replace"), "db", "copy", target);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("error", result.ErrorCode);
        Assert.Contains("held.vvo", result.Error.GetProperty("message").GetString());
        Assert.True(waited.Elapsed < TimeSpan.FromSeconds(2), $"Took {waited.Elapsed}.");
    }

    [Fact]
    public async Task CopyingOntoItselfIsAUsageError()
    {
        var before = File.ReadAllBytes(_catalogue.Path);

        var result = await RunAsync("db", "copy", _catalogue.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(_catalogue.Path));
    }

    [Fact]
    public async Task CopyingIntoAFolderThatDoesNotExistIsNotFound()
    {
        var result = await RunAsync("db", "copy", PathOf(@"missing\copy.vvo"));

        Assert.Equal(3, result.ExitCode);
    }

    #endregion

    #region shrink

    [Fact]
    public async Task ShrinkingAfterADeleteReclaimsSpaceAndKeepsWhatIsLeft()
    {
        var big = await _catalogue.AddVolumeAsync("Big");
        var tree = TestTree.Root("many");
        for (var i = 0; i < 3000; i++)
        {
            tree.File($"file{i:0000}-{new string('x', 40)}.txt", i);
        }

        var entry = await _catalogue.AddFolderAsync(big.Id, tree.Build());
        await _catalogue.Volumes.RemoveFolderAsync(entry.Id);
        var before = await _catalogue.SnapshotAsync();

        var result = await RunAsync("db", "shrink");

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Json.GetProperty("sizeAfter").GetInt64() < result.Json.GetProperty("sizeBefore").GetInt64());
        Assert.Equal(before, await _catalogue.SnapshotAsync());

        var backup = result.Json.GetProperty("backupPath").GetString();
        Assert.True(File.Exists(backup));
    }

    [Fact]
    public async Task ACancelledShrinkLeavesTheCatalogueReadable()
    {
        var before = await _catalogue.SnapshotAsync();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var result = await CliRunner.RunAsync(["db", "shrink", "--db", _catalogue.Path], cancellationToken: cancelled.Token);

        Assert.Equal(6, result.ExitCode);
        Assert.Equal(before, await _catalogue.SnapshotAsync());
    }

    #endregion

    #region export and import

    [Fact]
    public async Task AnExportImportsBackToTheSameCatalogue()
    {
        var json = PathOf("export.json");
        var imported = PathOf("imported.vvo");

        var exported = await RunAsync("db", "export", json);
        var result = await CliRunner.RunAsync("db", "import", json, "--db", imported);

        Assert.Equal(0, exported.ExitCode);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, result.Json.GetProperty("volumeCount").GetInt32());
        Assert.Equal(1, result.Json.GetProperty("folderCount").GetInt32());
        Assert.Equal(3, result.Json.GetProperty("recordCount").GetInt32());

        using var copy = await TempCatalogue.CreateAsync();
        File.Copy(imported, copy.Path, overwrite: true);
        Assert.Equal(await _catalogue.SnapshotAsync(), await copy.SnapshotAsync());
    }

    [Fact]
    public async Task ExportingOverTheCatalogueItselfIsAUsageError()
    {
        var before = File.ReadAllBytes(_catalogue.Path);

        var result = await RunAsync(ScriptedTerminal.Typing("replace"), "db", "export", _catalogue.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(_catalogue.Path));
    }

    [Fact]
    public async Task ImportingAnExportOverItselfIsAUsageError()
    {
        var json = PathOf("export.json");
        await RunAsync("db", "export", json);
        var before = File.ReadAllBytes(json);

        var result = await CliRunner.RunAsync(["db", "import", json, "--db", json], terminal: ScriptedTerminal.Typing("replace"));

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(json));
    }

    [Fact]
    public async Task ExportingOverAFileTakesTheUsersWord()
    {
        var json = PathOf("export.json");
        File.WriteAllText(json, "keep");

        var declined = await RunAsync(ScriptedTerminal.Redirected(), "db", "export", json);
        Assert.Equal(4, declined.ExitCode);
        Assert.Equal("keep", File.ReadAllText(json));

        var confirmed = await RunAsync(ScriptedTerminal.Typing("replace"), "db", "export", json);
        Assert.Equal(0, confirmed.ExitCode);
        Assert.NotEqual("keep", File.ReadAllText(json));
    }

    [Fact]
    public async Task ImportingOverACatalogueTakesTheUsersWord()
    {
        var json = PathOf("export.json");
        await RunAsync("db", "export", json);

        using var other = await TempCatalogue.CreateAsync();
        await other.AddVolumeAsync("Keep");
        var before = File.ReadAllBytes(other.Path);

        var declined = await CliRunner.RunAsync(["db", "import", json, "--db", other.Path], terminal: ScriptedTerminal.Typing("no"));
        Assert.Equal(6, declined.ExitCode);
        Assert.Equal(before, File.ReadAllBytes(other.Path));

        var confirmed = await CliRunner.RunAsync(["db", "import", json, "--db", other.Path], terminal: ScriptedTerminal.Typing("replace"));
        Assert.Equal(0, confirmed.ExitCode);
        Assert.Equal(await _catalogue.SnapshotAsync(), await other.SnapshotAsync());
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"formatVersion\": 999}")]
    public async Task AnExportThatCannotBeReadLeavesTheCatalogueAlone(string contents)
    {
        var json = PathOf("bad.json");
        File.WriteAllText(json, contents);

        using var other = await TempCatalogue.CreateAsync();
        await other.AddVolumeAsync("Keep");
        var before = await other.SnapshotAsync();

        var result = await CliRunner.RunAsync(
            ["db", "import", json, "--db", other.Path], terminal: ScriptedTerminal.Typing("replace"));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("invalid_file", result.ErrorCode);
        Assert.Equal(before, await other.SnapshotAsync());
    }

    [Fact]
    public async Task AMissingExportIsNotFound()
    {
        var target = PathOf("new.vvo");

        var result = await CliRunner.RunAsync("db", "import", PathOf("missing.json"), "--db", target);

        Assert.Equal(3, result.ExitCode);
        Assert.False(File.Exists(target));
    }

    #endregion
}
