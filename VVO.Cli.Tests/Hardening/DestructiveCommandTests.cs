using System.CommandLine.Parsing;

namespace VVO.Cli.Tests.Hardening;

// Nothing destructive happens without a person typing the word at a terminal
public class DestructiveCommandTests
{
    /// <summary>
    /// Every way to destroy something: the matrix's destructive commands, and each command that
    /// writes a file over one already there.
    /// </summary>
    public static IEnumerable<object[]> Invocations()
    {
        foreach (var command in CommandMatrix.Cases.Where(item => item.Destructive))
            yield return [command.Path, command.Valid, command.ConfirmationWord!];

        yield return ["db new over a file", (Func<MatrixFixture, string[]>)(f => ["db", "new", f.Catalogue.Path]), "replace"];
        yield return ["db copy over a file", (Func<MatrixFixture, string[]>)(f => ["db", "copy", Existing(f), "--db", f.Catalogue.Path]), "replace"];
        yield return ["db export over a file", (Func<MatrixFixture, string[]>)(f => ["db", "export", Existing(f), "--db", f.Catalogue.Path]), "replace"];
        yield return ["db import over a catalogue", (Func<MatrixFixture, string[]>)(f => ["db", "import", f.ExportPath, "--db", f.Catalogue.Path]), "replace"];
    }

    private static string Existing(MatrixFixture fixture)
    {
        var path = fixture.ScratchPath("existing.bin");
        if (!File.Exists(path))
            File.WriteAllText(path, "keep me");

        return path;
    }

    // Everything a destructive command could touch: the catalogue, and the file it would replace
    private static async Task<string> StateAsync(MatrixFixture fixture)
    {
        var existing = fixture.ScratchPath("existing.bin");
        return await fixture.SnapshotAsync()
            + Convert.ToBase64String(File.ReadAllBytes(fixture.Catalogue.Path))
            + (File.Exists(existing) ? File.ReadAllText(existing) : "");
    }

    [Theory]
    [MemberData(nameof(Invocations))]
    public async Task WithoutATerminalNothingIsReadOrChanged(string name, Func<MatrixFixture, string[]> args, string word)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var invocation = args(fixture);
        var before = await StateAsync(fixture);
        var terminal = ScriptedTerminal.Redirected();

        var result = await CliRunner.RunAsync(invocation, terminal: terminal);

        Assert.True(result.ExitCode == 4, $"{name}: {result.Stdout}");
        Assert.Equal(0, terminal.Reads);
        Assert.Equal(before, await StateAsync(fixture));
        Assert.False(string.IsNullOrWhiteSpace(result.Error.GetProperty("message").GetString()));
        Assert.DoesNotContain($"Type {word}", result.Stderr);
    }

    // What the agent hands the user has to do what the agent tried, from wherever they paste it
    [Theory]
    [MemberData(nameof(Invocations))]
    public async Task TheCommandHandedBackRunsAsTheSameCommand(string name, Func<MatrixFixture, string[]> args, string _)
    {
        using var fixture = await MatrixFixture.CreateAsync();

        var first = await CliRunner.RunAsync(args(fixture), terminal: ScriptedTerminal.Redirected());
        var command = first.Error.GetProperty("command").GetString()!;

        var again = await CliRunner.RunAsync(
            CommandLineParser.SplitCommandLine(command).Skip(1).ToArray(), terminal: ScriptedTerminal.Redirected());

        Assert.True(again.ExitCode == 4, $"{name}: {again.Stdout}");
        Assert.Equal(command, again.Error.GetProperty("command").GetString());
        Assert.StartsWith("vvo ", command);
    }

    [Theory]
    [MemberData(nameof(Invocations))]
    public async Task AnyOtherAnswerChangesNothing(string name, Func<MatrixFixture, string[]> args, string word)
    {
        string?[] answers =
        [
            "yes",
            word.ToUpperInvariant(),
            char.ToUpperInvariant(word[0]) + word[1..],
            $" {word}",
            $"{word} ",
            // Cyrillic 'е' and 'а' look like the Latin ones they stand in for
            word.Replace('e', 'е').Replace('a', 'а'),
            "",
            null
        ];

        foreach (var answer in answers)
        {
            using var fixture = await MatrixFixture.CreateAsync();
            var invocation = args(fixture);
            var before = await StateAsync(fixture);
            var terminal = answer == null ? ScriptedTerminal.Typing() : ScriptedTerminal.Typing(answer);

            var result = await CliRunner.RunAsync(invocation, terminal: terminal);

            Assert.True(result.ExitCode == 6, $"{name} answered '{answer}': {result.Stdout}");
            Assert.Equal(before, await StateAsync(fixture));
        }
    }

    [Theory]
    [MemberData(nameof(Invocations))]
    public async Task TheWordGoesAhead(string name, Func<MatrixFixture, string[]> args, string word)
    {
        using var fixture = await MatrixFixture.CreateAsync();

        var result = await CliRunner.RunAsync(args(fixture), terminal: ScriptedTerminal.Typing(word));

        Assert.True(result.ExitCode == 0, $"{name}: {result.Stdout}");
    }

    // Piping the word in is how an agent would try to answer for the user
    [Theory]
    [InlineData("folder delete", "delete")]
    [InlineData("folder rescan", "update")]
    [InlineData("rm", "delete")]
    public async Task AWordPipedIntoTheProcessIsNotConsent(string path, string word)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var before = await StateAsync(fixture);
        var command = CommandMatrix.Cases.Single(item => item.Path == path);

        var result = await ProcessRunner.RunAsync(command.Valid(fixture), stdin: $"{word}\n");

        Assert.Equal(4, result.ExitCode);
        Assert.Equal("confirmation_required", result.ErrorCode);
        Assert.Equal(before, await StateAsync(fixture));
    }
}
