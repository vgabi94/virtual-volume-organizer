using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using VVO.Cli;

namespace VVO.Cli.Tests;

public sealed record CliResult(int ExitCode, string Stdout, string Stderr)
{
    public JsonElement Json => JsonDocument.Parse(Stdout).RootElement;

    public JsonElement Error => Json.GetProperty("error");

    public string? ErrorCode => Json.TryGetProperty("error", out var error)
        ? error.GetProperty("code").GetString()
        : null;
}

/// <summary>
/// Runs the command tree in-process, the way vvo.exe would, with its output captured.
/// </summary>
public static class CliRunner
{
    public static Task<CliResult> RunAsync(params string[] args) => RunAsync(args, extend: null);

    /// <param name="extend">Adds commands that exist only for a test, built on the same services.</param>
    /// <param name="terminal">
    /// The person at the keyboard. Left out, there is nobody: input is redirected, as it is for an agent.
    /// </param>
    public static async Task<CliResult> RunAsync(
        string[] args,
        Action<RootCommand, IServiceProvider>? extend = null,
        ITerminal? terminal = null,
        CancellationToken cancellationToken = default)
    {
        // Fresh per run: the database service remembers the file it was last pointed at
        using var services = ServiceConfiguration.ConfigureServices(collection =>
            collection.AddSingleton(terminal ?? ScriptedTerminal.Redirected()));

        var root = CliApp.BuildRoot(services);
        extend?.Invoke(root, services);

        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await CliApp.RunAsync(root, args, stdout, stderr, cancellationToken);

        return new CliResult(exitCode, stdout.ToString(), stderr.ToString());
    }
}
