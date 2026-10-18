namespace CloveParentalGuard.Core;

/// <summary>One "this app was blocked" event, identified by a monotonic sequence.</summary>
public record BlockEvent(string AppName, long Seq, long UnixMs);

/// <summary>
/// Thread-safe ring buffer of recent block events. The service (session 0) cannot
/// show UI, so it records blocked apps here and the user-session agent reads them
/// off the status response to raise notifications.
/// </summary>
public class BlockEventLog
{
    private const int MaxRetained = 32;

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly LinkedList<BlockEvent> _events = new();
    private long _seq;

    public BlockEventLog(Func<DateTimeOffset> clock) => _clock = clock;

    /// <summary>Records one event per distinct app name in the batch.</summary>
    public void Record(IEnumerable<string> appNames)
    {
        lock (_gate)
        {
            var now = _clock().ToUnixTimeMilliseconds();
            foreach (var name in appNames)
            {
                _events.AddLast(new BlockEvent(name, ++_seq, now));
                while (_events.Count > MaxRetained) _events.RemoveFirst();
            }
        }
    }

    /// <summary>Current sequence high-water mark plus the retained event window.</summary>
    public (long Seq, IReadOnlyList<BlockEvent> Recent) Snapshot()
    {
        lock (_gate)
        {
            return (_seq, _events.ToArray());
        }
    }
}
