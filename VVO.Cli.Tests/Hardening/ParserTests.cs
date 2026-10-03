using System.Text.Json;
using VVO.Core.Models;
using VVO.Tests;

namespace VVO.Cli.Tests.Hardening;

// How arguments are read, where System.CommandLine's defaults would surprise an agent
public class ParserTests : IAsyncLifetime
{
    private TempCatalogue _catalogue = null!;
    private RootFolderMetadata _entry = null!;

    public async Task InitializeAsync()
    {
        _catalogue = await TempCatalogue.CreateAsync();
        var volume = await _catalogue.AddVolumeAsync("Backups");
        _entry = await _catalogue.AddFolderAsync(volume.Id, TestTree.Root("files")
            .File("@home.txt").File("-draft.txt").File("-q.txt").File("plain.txt")
            .Build());
    }

    public Task DisposeAsync()
    {
        _catalogue.Dispose();
        return Task.CompletedTask;
    }

    private Task<CliResult> RunAsync(params string[] args) =>
        CliRunner.RunAsync([.. args, "--db", _catalogue.Path]);

    private static IEnumerable<string?> HitNames(CliResult result) =>
        result.Json.GetProperty("hits").EnumerateArray().Select(hit => hit.GetProperty("name").GetString());

    #region '@' is a character

    [Fact]
    public async Task ASearchTermStartingWithAtIsSearchedFor()
    {
        var result = await RunAsync("search", "@home");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["@home.txt"], HitNames(result));
    }

    [Fact]
    public async Task AVolumeNameStartingWithAtIsKept()
    {
        var result = await RunAsync("volume", "create", "@work");

        Assert.Equal("@work", result.Json.GetProperty("name").GetString());
    }

    [Fact]
    public async Task ALabelStartingWithAtIsKept()
    {
        var result = await RunAsync("folder", "update", _entry.Id.ToString(), "--label", "@me");

        Assert.Equal("@me", result.Json.GetProperty("title").GetString());
    }

    [Fact]
    public async Task AFileNameStartingWithAtIsAPath()
    {
        var name = $"@{Guid.NewGuid()}.vvo";
        try
        {
            var result = await CliRunner.RunAsync("db", "new", name);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(name));
        }
        finally
        {
            File.Delete(name);
        }
    }

    // An existing file named like a response file must not be read as one either
    [Fact]
    public async Task AnAtArgumentNamingARealFileIsNotExpanded()
    {
        var name = $"{Guid.NewGuid()}.rsp";
        File.WriteAllText(name, "--limit 1");
        try
        {
            var result = await RunAsync("search", $"@{name}");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(HitNames(result));
        }
        finally
        {
            File.Delete(name);
        }
    }

    #endregion

    #region A leading dash

    [Fact]
    public async Task ATermStartingWithADashIsATerm()
    {
        var result = await RunAsync("search", "-draft");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["-draft.txt"], HitNames(result));
    }

    // Only a term that is also an option's name is taken for the option
    [Fact]
    public async Task ATermNamedLikeAnOptionIsTheOption()
    {
        var result = await RunAsync("search", "-q");

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task AfterADoubleDashEvenThatIsATerm()
    {
        var result = await CliRunner.RunAsync("search", "--db", _catalogue.Path, "--", "-q");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["-q.txt"], HitNames(result));
    }

    #endregion

    #region Directives and option names

    // [suggest] used to answer with completions in plain text, where stdout holds only JSON
    [Theory]
    [InlineData("[suggest]")]
    [InlineData("[diagram]")]
    [InlineData("[anything]")]
    public async Task ADirectiveIsIgnoredAndTheCommandAnswersInJson(string directive)
    {
        var plain = await CliRunner.RunAsync("volume", "list", "--db", _catalogue.Path);
        var directed = await CliRunner.RunAsync(directive, "volume", "list", "--db", _catalogue.Path);

        Assert.Equal(0, directed.ExitCode);
        Assert.Equal(plain.Stdout, directed.Stdout);
    }

    [Fact]
    public async Task ATermInBracketsIsSearchedFor()
    {
        var result = await RunAsync("search", "[suggest]");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(HitNames(result));
    }

    [Fact]
    public async Task OptionNamesAreCaseSensitive()
    {
        var result = await CliRunner.RunAsync("volume", "list", "--DB", _catalogue.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task GivingTheCatalogueTwiceIsAUsageError()
    {
        using var other = await TempCatalogue.CreateAsync();

        var result = await CliRunner.RunAsync("volume", "list", "--db", _catalogue.Path, "--db", other.Path);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    #endregion

    [Fact]
    public async Task AParseErrorStillLeavesOnlyJsonOnStdout()
    {
        var result = await CliRunner.RunAsync("[suggest]", "volume", "list", "--unknown");

        using var _ = JsonDocument.Parse(result.Stdout);
        Assert.Contains("Usage:", result.Stderr);
    }
}
