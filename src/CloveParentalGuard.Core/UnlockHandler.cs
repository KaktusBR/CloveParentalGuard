namespace CloveParentalGuard.Core;

public class UnlockHandler
{
    private const int FailThreshold = 5;
    private const int BaseLockoutSeconds = 30;
    private const int MaxLockoutSeconds = 3600;

    private readonly LockManager _lockManager;
    private readonly Func<CloveParentalGuardConfig> _configProvider;
    private readonly Func<DateTimeOffset> _clock;
    private readonly BlockEventLog? _blockLog;

    private int _failedAttempts;
    private DateTimeOffset? _lockoutUntil;

    public UnlockHandler(LockManager lockManager, Func<CloveParentalGuardConfig> configProvider,
        Func<DateTimeOffset> clock, BlockEventLog? blockLog = null)
    {
        _lockManager = lockManager;
        _configProvider = configProvider;
        _clock = clock;
        _blockLog = blockLog;
    }

    public PipeResponse Handle(PipeRequest request)
    {
        if (request.Type == "unlock")
            return HandleUnlock(request);
        return Status();
    }

    private PipeResponse HandleUnlock(PipeRequest request)
    {
        // Enforce lockout first — do not even check the code while locked out.
        if (_lockoutUntil is { } until && _clock() < until)
        {
            var wait = (int)Math.Ceiling((until - _clock()).TotalSeconds);
            return Fail($"Too many attempts. Try again in {wait} seconds.");
        }

        var cfg = _configProvider();
        if (cfg.Code is null)
            return Fail("No parent code is configured.");

        if (string.IsNullOrEmpty(request.Code) || !CodeHasher.Verify(request.Code, cfg.Code))
        {
            RegisterFailure();
            return Fail("Incorrect code.");
        }

        // Correct code: clear failure tracking regardless of duration validity.
        _failedAttempts = 0;
        _lockoutUntil = null;

        if (!cfg.DurationsMinutes.Contains(request.DurationMinutes))
            return Fail("Invalid duration.");

        _lockManager.Unlock(TimeSpan.FromMinutes(request.DurationMinutes));
        return Status();
    }

    private void RegisterFailure()
    {
        _failedAttempts++;
        if (_failedAttempts < FailThreshold) return;
        var exponent = _failedAttempts - FailThreshold; // 0, 1, 2, ...
        var seconds = Math.Min(BaseLockoutSeconds * (long)Math.Pow(2, exponent), MaxLockoutSeconds);
        _lockoutUntil = _clock() + TimeSpan.FromSeconds(seconds);
    }

    private PipeResponse Status()
    {
        var remaining = (int)(_lockManager.Remaining?.TotalSeconds ?? 0);
        var (seq, recent) = _blockLog?.Snapshot() ?? (0L, Array.Empty<BlockEvent>());
        return new PipeResponse(true, _lockManager.IsLocked, remaining, null,
            _configProvider().DurationsMinutes, seq, recent);
    }

    private PipeResponse Fail(string error) =>
        new(false, _lockManager.IsLocked, (int)(_lockManager.Remaining?.TotalSeconds ?? 0), error);
}
