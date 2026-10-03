namespace VVO.Tests;

/// <summary>
/// Records each report as it is made. <see cref="Progress{T}"/> posts its reports to the thread
/// pool, so a test reading them straight after the call can find some still on their way.
/// </summary>
public sealed class ReportedProgress(List<string> reported) : IProgress<string>
{
    private readonly Lock _lock = new();

    public void Report(string value)
    {
        lock (_lock)
        {
            reported.Add(value);
        }
    }
}
