using System.CommandLine;
using System.Text.Json;

namespace VVO.Cli.Tests.Hardening;

// What every command promises about its output, checked on every command at once
public class OutputContractTests
{
    private static async Task<CliResult> RunValidAsync(MatrixCase command, params string[] extra)
    {
        using var fixture = await MatrixFixture.CreateAsync();
        var terminal = ScriptedTerminal.Typing(command.ConfirmationWord ?? "");

        return await CliRunner.RunAsync([.. command.Valid(fixture), .. extra], terminal: terminal);
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.All), MemberType = typeof(CommandMatrix))]
    public async Task StdoutIsOneJsonDocumentAndNothingElse(MatrixCase command)
    {
        var result = await RunValidAsync(command);

        Assert.Equal(0, result.ExitCode);

        // Parsing the whole of stdout fails on anything written beside the document
        using var document = JsonDocument.Parse(result.Stdout);
        Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.All), MemberType = typeof(CommandMatrix))]
    public async Task QuietLeavesStderrToThePromptAlone(MatrixCase command)
    {
        var result = await RunValidAsync(command, "--quiet");

        Assert.Equal(0, result.ExitCode);

        if (command.Destructive)
        {
            Assert.Contains($"Type {command.ConfirmationWord} to confirm:", result.Stderr);
            Assert.DoesNotContain("...", result.Stderr);
        }
        else
        {
            Assert.Equal(string.Empty, result.Stderr);
        }
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.All), MemberType = typeof(CommandMatrix))]
    public async Task EveryResultCanBeShownAsATable(MatrixCase command)
    {
        var result = await RunValidAsync(command, "--format", "table");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Stdout));
        Assert.ThrowsAny<JsonException>(() => JsonDocument.Parse(result.Stdout));
    }

    [Theory]
    [MemberData(nameof(CommandMatrix.All), MemberType = typeof(CommandMatrix))]
    public async Task EveryCommandHasHelp(MatrixCase command)
    {
        var result = await CliRunner.RunAsync([.. command.Path.Split(' '), "--help"]);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Description:", result.Stdout);
        Assert.Contains($" {command.Path}", result.Stdout);
    }

    // An agent learns a command from its help; an option without a description says nothing
    [Fact]
    public void EveryArgumentAndOptionIsDescribed()
    {
        var undescribed = new List<string>();

        foreach (var (path, command) in CommandMatrix.LeavesWithPaths())
        {
            if (string.IsNullOrWhiteSpace(command.Description))
                undescribed.Add(path);

            undescribed.AddRange(command.Arguments
                .Where(argument => string.IsNullOrWhiteSpace(argument.Description))
                .Select(argument => $"{path} <{argument.Name}>"));

            undescribed.AddRange(command.Options
                .Where(option => string.IsNullOrWhiteSpace(option.Description))
                .Select(option => $"{path} {option.Name}"));
        }

        Assert.Empty(undescribed);
    }
}
