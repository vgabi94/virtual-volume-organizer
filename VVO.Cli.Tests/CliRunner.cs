using System.CommandLine;
using System.Text.Json;
using VVO.Cli;

namespace VVO.Cli.Tests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public JsonElement Json => JsonDocument.Parse(Stdout).RootElement;
}

/// <summary>
/// Runs the command tree in-process, the way vvo.exe would, with its output captured.
/// </summary>
public static class CliRunner
{
    public static async Task<CliResult> RunAsync(params string[] args)
    {
        // Fresh per run: the database service remembers the file it was last pointed at
        using var services = ServiceConfiguration.ConfigureServices();

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliApp.BuildRoot(services)
            .Parse(args)
            .InvokeAsync(new InvocationConfiguration { Output = stdout, Error = stderr });

        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }
}
