using System.CommandLine;
using VVO.Cli;

namespace VVO.Cli.Tests;

public class DatabaseFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"VVO_DbFile_{Guid.NewGuid()}");

    public DatabaseFileTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); } catch { }
    }

    // Stand-ins for the commands that open a catalogue and the ones that create one
    private static void AddCommands(RootCommand root, IServiceProvider services)
    {
        var openDb = DatabaseFile.CreateOption();
        var open = new Command("open") { openDb };
        open.SetJsonAction(services, async context =>
            new { Path = await DatabaseFile.OpenExistingAsync(context, openDb) });
        root.Subcommands.Add(open);

        var path = new Argument<string>("path");
        var create = new Command("create") { path };
        create.SetJsonAction(services, async context =>
            new { Path = await DatabaseFile.CreateOrReplaceAsync(context, context.ParseResult.GetRequiredValue(path)) });
        root.Subcommands.Add(create);
    }

    private static Task<CliResult> RunAsync(string[] args, ScriptedTerminal? terminal = null) =>
        CliRunner.RunAsync(args, AddCommands, terminal);

    private string PathOf(string name) => Path.Combine(_directory, name);

    [Fact]
    public async Task AnExistingCatalogueOpens()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        var result = await RunAsync(["open", "--db", catalogue.Path]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(catalogue.Path, result.Json.GetProperty("path").GetString());
    }

    [Fact]
    public async Task WithoutDbItIsAUsageError()
    {
        var result = await RunAsync(["open"]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task AMissingFileIsNotFoundAndIsNotCreated()
    {
        var path = PathOf("missing.vvo");

        var result = await RunAsync(["open", "--db", path]);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
        Assert.False(File.Exists(path));
    }

    [Theory]
    [InlineData("This is not a catalogue.")]
    [InlineData(null)]
    public async Task AFileThatIsNotACatalogueIsLeftAsItWas(string? text)
    {
        var path = PathOf("notes.vvo");

        // Text, or a catalogue's first bytes without the rest: a copy that was cut short
        byte[] contents;
        if (text != null)
        {
            contents = System.Text.Encoding.UTF8.GetBytes(text);
        }
        else
        {
            using var catalogue = await TempCatalogue.CreateAsync();
            contents = File.ReadAllBytes(catalogue.Path)[..4096];
        }

        File.WriteAllBytes(path, contents);

        var result = await RunAsync(["open", "--db", path]);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("invalid_database", result.ErrorCode);
        Assert.Equal(contents, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task ACatalogueHeldByAnotherProgramIsDatabaseBusy()
    {
        using var catalogue = await TempCatalogue.CreateAsync();
        using var hold = new FileStream(catalogue.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await RunAsync(["open", "--db", catalogue.Path]);

        Assert.Equal(5, result.ExitCode);
        Assert.Equal("database_busy", result.ErrorCode);
    }

    [Fact]
    public async Task AFolderIsAUsageError()
    {
        var result = await RunAsync(["open", "--db", _directory]);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal("usage", result.ErrorCode);
    }

    [Fact]
    public async Task ARelativePathIsReportedInFull()
    {
        using var catalogue = await TempCatalogue.CreateAsync();

        // Beside the test run rather than in the temp folder, which can be on another drive
        var name = $"{Guid.NewGuid()}.vvo";
        File.Copy(catalogue.Path, name);

        try
        {
            var result = await RunAsync(["open", "--db", name]);

            Assert.Equal(Path.GetFullPath(name), result.Json.GetProperty("path").GetString());
        }
        finally
        {
            File.Delete(name);
        }
    }

    [Fact]
    public async Task CreatingANewFileAsksNothing()
    {
        var path = PathOf("new.vvo");
        var terminal = ScriptedTerminal.Redirected();

        var result = await RunAsync(["create", path], terminal);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(path));
        Assert.Equal(0, terminal.Reads);
    }

    [Fact]
    public async Task ReplacingAFileTakesTheUsersWord()
    {
        var path = PathOf("old.vvo");
        File.WriteAllText(path, "This is not a catalogue.");

        var result = await RunAsync(["create", path], ScriptedTerminal.Typing("replace"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Type replace to confirm:", result.Stderr);
        Assert.NotEqual("This is not a catalogue.", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(true, 6, "cancelled")]
    [InlineData(false, 4, "confirmation_required")]
    public async Task AFileIsNotReplacedWithoutTheUsersWord(bool interactive, int exitCode, string code)
    {
        var path = PathOf("old.vvo");
        File.WriteAllText(path, "Keep me.");

        var terminal = interactive ? ScriptedTerminal.Typing("delete") : ScriptedTerminal.Redirected();
        var result = await RunAsync(["create", path], terminal);

        Assert.Equal(exitCode, result.ExitCode);
        Assert.Equal(code, result.ErrorCode);
        Assert.Equal("Keep me.", File.ReadAllText(path));
    }

    [Fact]
    public async Task CreatingInAFolderThatDoesNotExistIsNotFound()
    {
        var result = await RunAsync(["create", PathOf(@"nowhere\new.vvo")]);

        Assert.Equal(3, result.ExitCode);
        Assert.Equal("not_found", result.ErrorCode);
    }
}
