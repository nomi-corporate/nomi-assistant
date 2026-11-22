using System.Collections.Concurrent;

namespace Nomi.Assistant.Application;

/// <summary>
/// Per-user sliding window that bounds model calls (and their cost). In memory, per instance:
/// enough for the single development API; a shared store is needed before scaling out.
/// </summary>
public sealed class ProposalRateLimiter(TimeProvider clock, int limit = 30, TimeSpan? window = null)
{
    private readonly TimeSpan _window = window ?? TimeSpan.FromMinutes(10);
    private readonly ConcurrentDictionary<Guid, Queue<DateTimeOffset>> _calls = new();

    public bool TryAcquire(Guid userId)
    {
        var now = clock.GetUtcNow();
        var calls = _calls.GetOrAdd(userId, _ => new Queue<DateTimeOffset>());
        lock (calls)
        {
            while (calls.Count > 0 && now - calls.Peek() >= _window) calls.Dequeue();
            if (calls.Count >= limit) return false;
            calls.Enqueue(now);
            return true;
        }
    }
}
