using System.CommandLine;
using System.Text.Json;
using VVO.Core.Models;
using VVO.Core.Services;

namespace VVO.Cli.Tests;

public class OutputTests
{
    private static void AddTestCommands(RootCommand root, IServiceProvider services)
    {
        var echo = new Command("echo");
        echo.SetJsonAction(services, _ => Task.FromResult<object?>(new
        {
            Name = "photos",
            Utc = new DateTime(2026, 3, 1, 12, 30, 0, DateTimeKind.Utc),
            Unspecified = new DateTime(2026, 3, 1, 12, 30, 0, DateTimeKind.Unspecified),
            Status = ComparisonStatus.Unchanged,
            Label = (string?)null
        }));
        root.Subcommands.Add(echo);

        var kind = new Argument<string>("kind");
        var fail = new Command("fail") { kind };
        fail.SetJsonAction(services, context => throw (context.ParseResult.GetValue(kind) switch
        {
            "not-found" => new CatalogueItemNotFoundException("There is no folder entry 'x'.", "entryId"),
            "file" => new FileNotFoundException("The path 'a' does not exist."),
            "directory" => new DirectoryNotFoundException("The path 'b' does not exist."),
            "argument" => new ArgumentException("A virtual volume needs a name.", "name"),
            "invalid-data" => new InvalidDataException("'x.vvo' is not a catalogue."),
            "cancelled" => new OperationCanceledException(),
            "busy" => new DatabaseBusyException("x.vvo", new IOException()),
            _ => new InvalidOperationException("Something broke.")
        }));
        root.Subcommands.Add(fail);

        var progress = new Command("progress");
        progress.SetJsonAction(services, context =>
        {
            context.Progress.Report("Scanning...");
            return Task.FromResult<object?>(new { Done = true });
        });
        root.Subcommands.Add(progress);

        var wait = new Command("wait");
        wait.SetJsonAction(services, async context =>
        {
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return null;
        });
        root.Subcommands.Add(wait);

        var required = new Command("required") { new Argument<string>("value") };
        required.SetJsonAction(services, _ => Task.FromResult<object?>(null));
        root.Subcommands.Add(required);
    }

    private static Task<CliResult> RunAsync(params string[] args) =>
        CliRunner.RunAsync(args, AddTestCommands);

    [Fact]
    public async Task SuccessWritesOneJsonDocumentAndNothingElse()
    {
        var result = await RunAsync("echo");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("photos", result.Json.GetProperty("name").GetString());
        Assert.Equal(string.Empty, result.Stderr);

        // Parsing the whole of stdout as one document proves nothing else was written there
        using var _ = JsonDocument.Parse(result.Stdout);
    }

    [Fact]
    public async Task DatesAreUtcIsoAndEnumsAreCamelCaseStrings()
    {
        var json = (await RunAsync("echo")).Json;

        Assert.Equal("2026-03-01T12:30:00Z", json.GetProperty("utc").GetString());
        Assert.Equal("2026-03-01T12:30:00Z", json.GetProperty("unspecified").GetString());
        Assert.Equal("unchanged", json.GetProperty("status").GetString());
    }

    [Fact]
    public async Task NullsAreWrittenRatherThanLeftOut()
    {
        var json = (await RunAsync("echo")).Json;

        Assert.Equal(JsonValueKind.Null, json.GetProperty("label").ValueKind);
    }

    [Theory]
    [InlineData("not-found", 3, "not_found", "There is no folder entry 'x'.")]
    [InlineData("file", 3, "not_found", "The path 'a' does not exist.")]
    [InlineData("directory", 3, "not_found", "The path 'b' does not exist.")]
    [InlineData("argument", 2, "usage", "A virtual volume needs a name.")]
    [InlineData("invalid-data", 1, "invalid_database", "'x.vvo' is not a catalogue.")]
    [InlineData("cancelled", 6, "cancelled", "The operation was cancelled.")]
    [InlineData("busy", 5, "database_busy", "'x.vvo' is in use by another program. Try again once it is done.")]
    [InlineData("other", 1, "error", "Something broke.")]
    public async Task ExceptionsMapToTheirErrorAndExitCodes(string kind, int exitCode, string code, string message)
    {
        var result = await RunAsync("fail", kind);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(code, result.Error.GetProperty("code").GetString());
        Assert.Equal(message, result.Error.GetProperty("message").GetString());
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("required")]
    [InlineData("echo", "--unknown")]
    [InlineData]
    public async Task ParseErrorsAreUsageJsonWithHelpOnStderr(params string[] args)
    {
        var result = await RunAsync(args);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.Error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(result.Error.GetProperty("message").GetString()));
        Assert.Contains("Usage:", result.Stderr);
    }

    [Fact]
    public async Task HelpForTheFailingCommandIsTheOneShown()
    {
        var result = await RunAsync("required");

        Assert.Contains("required <value>", result.Stderr);
    }

    [Fact]
    public async Task ProgressGoesToStderr()
    {
        var result = await RunAsync("progress");

        Assert.Contains("Scanning...", result.Stderr);
        Assert.DoesNotContain("Scanning...", result.Stdout);
        Assert.True(result.Json.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task QuietSilencesProgressAndLeavesStdoutAlone()
    {
        var loud = await RunAsync("progress");
        var quiet = await RunAsync("progress", "--quiet");

        Assert.Equal(string.Empty, quiet.Stderr);
        Assert.Equal(loud.Stdout, quiet.Stdout);
    }

    [Fact]
    public async Task CancellingMidCommandReportsCancelled()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await CliRunner.RunAsync(["wait"], AddTestCommands, cancellationToken: cancellation.Token);

        Assert.Equal(6, result.ExitCode);
        Assert.Equal("cancelled", result.Error.GetProperty("code").GetString());
    }
}
