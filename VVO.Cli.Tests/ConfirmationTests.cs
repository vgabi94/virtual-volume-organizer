using System.CommandLine;
using System.CommandLine.Parsing;
using VVO.Cli;

namespace VVO.Cli.Tests;

public class ConfirmationTests
{
    private static readonly ConfirmationRequest Request = new(
        "Delete Folder", "photos", "Deleting this folder cannot be undone.", "delete");

    // Stands in for a destructive command: asks, and reports having gone ahead
    private static void AddDanger(RootCommand root, IServiceProvider services)
    {
        var danger = new Command("danger")
        {
            new Argument<string[]>("items") { Arity = ArgumentArity.ZeroOrMore },
            new Option<string>("--db").TakesPath(),
            new Option<string>("--label")
        };

        danger.SetJsonAction(services, async context =>
        {
            await Confirmation.RequireAsync(context, Request);
            return new { Done = true };
        });

        root.Subcommands.Add(danger);

        var file = new Argument<string>("file").TakesPath();
        var overwrite = new Command("overwrite") { file };
        overwrite.SetJsonAction(services, async context =>
        {
            await Confirmation.RequireAsync(context, Request);
            return null;
        });

        root.Subcommands.Add(overwrite);
    }

    private static Task<CliResult> RunAsync(ScriptedTerminal terminal, params string[] args) =>
        CliRunner.RunAsync(["danger", .. args], AddDanger, terminal);

    [Fact]
    public async Task TheExactWordGoesAhead()
    {
        var result = await RunAsync(ScriptedTerminal.Typing("delete"));

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Json.GetProperty("done").GetBoolean());
    }

    [Fact]
    public async Task ThePromptShowsTheWarningOnStderr()
    {
        var result = await RunAsync(ScriptedTerminal.Typing("delete"));

        Assert.Contains("Delete Folder", result.Stderr);
        Assert.Contains("photos", result.Stderr);
        Assert.Contains("Deleting this folder cannot be undone.", result.Stderr);
        Assert.Contains("Type delete to confirm:", result.Stderr);
        Assert.DoesNotContain("Type delete", result.Stdout);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("Delete")]
    [InlineData("DELETE")]
    [InlineData(" delete ")]
    [InlineData("delete ")]
    [InlineData("")]
    public async Task AnythingElseDeclines(string typed)
    {
        var result = await RunAsync(ScriptedTerminal.Typing(typed));

        Assert.Equal(6, result.ExitCode);
        Assert.Equal("cancelled", result.ErrorCode);
    }

    [Fact]
    public async Task EndOfInputDeclines()
    {
        var result = await RunAsync(ScriptedTerminal.Typing());

        Assert.Equal(6, result.ExitCode);
        Assert.Equal("cancelled", result.ErrorCode);
    }

    [Fact]
    public async Task WithoutATerminalNothingIsAskedOrRead()
    {
        var terminal = ScriptedTerminal.Redirected();

        var result = await RunAsync(terminal, "a", "--db", "x.vvo");

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("confirmation_required", result.ErrorCode);
        Assert.Equal(0, terminal.Reads);
        Assert.DoesNotContain("Type delete", result.Stderr);
        Assert.Equal(
            "Deleting this folder cannot be undone. Run the command in a terminal to confirm.",
            result.Error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task QuietStillShowsThePrompt()
    {
        var result = await CliRunner.RunAsync(
            ["danger", "--quiet"], AddDanger, ScriptedTerminal.Typing("delete"));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Type delete to confirm:", result.Stderr);
    }

    [Fact]
    public async Task TheCommandMakesTheDatabasePathAbsolute()
    {
        var result = await RunAsync(ScriptedTerminal.Redirected(), "a", "--db", "x.vvo");

        var expected = $"vvo danger a --db {Path.GetFullPath("x.vvo")}";
        Assert.Equal(expected, result.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TheCommandMakesTheDatabasePathAbsoluteWhenJoinedWithEquals()
    {
        var result = await RunAsync(ScriptedTerminal.Redirected(), "--db=x.vvo");

        Assert.Equal(
            $"vvo danger --db {Path.GetFullPath("x.vvo")}",
            result.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TheCommandMakesAPositionalPathAbsolute()
    {
        var result = await CliRunner.RunAsync(["overwrite", "x.vvo"], AddDanger, ScriptedTerminal.Redirected());

        Assert.Equal(
            $"vvo overwrite {Path.GetFullPath("x.vvo")}",
            result.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TheCommandLeavesArgumentsThatAreNotPathsAsTyped()
    {
        var result = await RunAsync(ScriptedTerminal.Redirected(), "x.vvo", "--label", "y.vvo");

        Assert.Equal("vvo danger x.vvo --label y.vvo", result.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TheCommandQuotesWhatNeedsQuoting()
    {
        var result = await RunAsync(
            ScriptedTerminal.Redirected(), "two words", "--label", "say \"hi\"", "--db", @"C:\My Catalogues\b.vvo");

        Assert.Equal(
            """vvo danger "two words" --label "say \"hi\"" --db "C:\My Catalogues\b.vvo" """.TrimEnd(),
            result.Error.GetProperty("command").GetString());
    }

    [Fact]
    public async Task TheCommandRunsAsGiven()
    {
        var first = await RunAsync(
            ScriptedTerminal.Redirected(), "two words", "plain", "--db", @"C:\My Catalogues\b.vvo");
        var command = first.Error.GetProperty("command").GetString()!;

        // What the user pastes is split back into arguments the way the shell would
        var args = CommandLineParser.SplitCommandLine(command).Skip(1).ToArray();
        var second = await CliRunner.RunAsync(args, AddDanger, ScriptedTerminal.Redirected());

        Assert.Equal(command, second.Error.GetProperty("command").GetString());
    }
}
