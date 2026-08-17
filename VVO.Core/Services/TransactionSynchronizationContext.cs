using System.Collections.Concurrent;

namespace VVO.Core.Services;

/// <summary>
/// Runs the continuations of an awaited operation on the single thread that started it,
/// rather than letting the thread pool pick one up.
/// </summary>
internal sealed class TransactionSynchronizationContext : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _pending = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        _pending.Add((d, state));
    }

    // Already on the owning thread, so there is nothing to hand over
    public override void Send(SendOrPostCallback d, object? state)
    {
        d(state);
    }

    /// <summary>
    /// Runs queued continuations until the given task finishes, then rethrows whatever it
    /// faulted with.
    /// </summary>
    public void RunToCompletion(Task task)
    {
        task.ContinueWith(
            _ => _pending.CompleteAdding(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        foreach (var (callback, state) in _pending.GetConsumingEnumerable())
        {
            callback(state);
        }

        task.GetAwaiter().GetResult();
    }
}
