namespace VVO.Cli.Output;

/// <summary>
/// Writes each report as it is made. <see cref="Progress{T}"/> would post them to the thread
/// pool, where they can land out of order or after the command has already finished.
/// </summary>
public sealed class StderrProgress(TextWriter error) : IProgress<string>
{
    private readonly Lock _lock = new();

    public void Report(string value)
    {
        lock (_lock)
        {
            error.WriteLine(value);
        }
    }
}

public sealed class SilentProgress : IProgress<string>
{
    public static SilentProgress Instance { get; } = new();

    public void Report(string value)
    {
    }
}
