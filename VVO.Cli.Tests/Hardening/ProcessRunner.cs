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

    public static async Task<CliResult> RunAsync(string[] args, string? stdin = null)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        start.ArgumentList.Add(typeof(CliApp).Assembly.Location);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

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
            Assert.Fail($"vvo {string.Join(' ', args)} did not finish within {Timeout}.");
        }

        return new CliResult(process.ExitCode, await stdout, await stderr);
    }
}
