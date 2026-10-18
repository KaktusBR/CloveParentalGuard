namespace CloveParentalGuard.Core;

public class LockManager
{
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _unlockedUntil;

    public LockManager(Func<DateTimeOffset> clock) => _clock = clock;

    public bool IsLocked => Remaining is null;

    public TimeSpan? Remaining
    {
        get
        {
            if (_unlockedUntil is null) return null;
            var left = _unlockedUntil.Value - _clock();
            return left > TimeSpan.Zero ? left : null;
        }
    }

    public void Unlock(TimeSpan duration) => _unlockedUntil = _clock() + duration;

    public void Lock() => _unlockedUntil = null;
}
