using System.Diagnostics;
using System.Text.Json;
using VVO.Cli.Output;

namespace VVO.Cli.Mcp;

public sealed record RunningServer(int Pid, DateTime StartedAt, string? Db);

/// <summary>
/// A file per running server, which 'vvo mcp stop' deletes to tell the server to end. Nothing is
/// killed: a pid may by now belong to some other program.
/// </summary>
public sealed class RunningServers(string directory)
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan ExitTimeout = TimeSpan.FromSeconds(5);

    public static RunningServers Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VVO", "mcp"));

    /// <summary>
    /// Records this server as running until the returned registration is disposed, and cancels
    /// <paramref name="stop"/> once someone asks it to end.
    /// </summary>
    public IAsyncDisposable Register(string? db, CancellationTokenSource stop)
    {
        Directory.CreateDirectory(directory);

        using var process = Process.GetCurrentProcess();
        var server = new RunningServer(Environment.ProcessId, process.StartTime.ToUniversalTime(), db);

        // Unique even for servers sharing a process, as the tests run them
        var file = Path.Combine(directory, $"{server.Pid}-{Guid.NewGuid():N}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(server, Json.Options));

        var watching = new CancellationTokenSource();
        return new Registration(file, watching, Watch(file, stop, watching.Token));
    }

    public async Task<List<RunningServer>> StopAllAsync(CancellationToken cancellationToken)
    {
        var stopped = new List<RunningServer>();
        if (!Directory.Exists(directory))
            return stopped;

        var exits = new List<Task>();

        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            var server = Read(file);

            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            // Left behind by a server that crashed
            if (server == null || ProcessOf(server) is not { } process)
                continue;

            stopped.Add(server);

            if (server.Pid != Environment.ProcessId)
            {
                exits.Add(WaitForExitAsync(process, cancellationToken));
            }
            else
            {
                process.Dispose();
            }
        }

        await Task.WhenAll(exits);
        return stopped;
    }

    private static async Task Watch(string file, CancellationTokenSource stop, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!File.Exists(file))
                {
                    await stop.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task WaitForExitAsync(Process process, CancellationToken cancellationToken)
    {
        using (process)
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(ExitTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
        }
    }

    private static RunningServer? Read(string file)
    {
        try
        {
            return JsonSerializer.Deserialize<RunningServer>(File.ReadAllText(file), Json.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // The start time tells a server apart from a program that was given its pid after it ended
    private static Process? ProcessOf(RunningServer server)
    {
        try
        {
            var process = Process.GetProcessById(server.Pid);
            if ((process.StartTime.ToUniversalTime() - server.StartedAt).Duration() < TimeSpan.FromSeconds(1))
                return process;

            process.Dispose();
            return null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private sealed class Registration(string file, CancellationTokenSource watching, Task watch) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await watching.CancelAsync();
            await watch;
            watching.Dispose();

            File.Delete(file);
        }
    }
}
