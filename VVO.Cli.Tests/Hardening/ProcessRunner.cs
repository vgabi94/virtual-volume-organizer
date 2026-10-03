using System.Diagnostics;
using System.Text;
using VVO.Cli;

namespace VVO.Cli.Tests.Hardening;

/// <summary>
/// Runs the built vvo as its own process, for what only a process has: the console encoding,
/// redirected stdin, and the exit code the shell sees.
/// </summary>
public static class ProcessRunner
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static Task<CliResult> RunAsync(string[] args, string? stdin = null)
    {
        var start = Start();
        start.ArgumentList.Add(typeof(CliApp).Assembly.Location);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        return RunAsync(start, string.Join(' ', args), stdin);
    }

    /// <summary>
    /// Runs a command line as typed, split into arguments by Windows' own rules, the way a command
    /// pasted into a terminal is.
    /// </summary>
    /// <param name="arguments">Everything after "vvo".</param>
    public static Task<CliResult> RunCommandLineAsync(string arguments)
    {
        var start = Start();
        start.Arguments = $"\"{typeof(CliApp).Assembly.Location}\" {arguments}";

        return RunAsync(start, arguments, stdin: null);
    }

    private static ProcessStartInfo Start() =>
        new("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

    private static async Task<CliResult> RunAsync(ProcessStartInfo start, string shown, string? stdin)
    {
        using var process = Process.Start(start)!;

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
        }

        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            Assert.Fail($"vvo {shown} did not finish within {Timeout}.");
        }

        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
