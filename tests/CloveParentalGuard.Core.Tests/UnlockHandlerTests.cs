namespace CloveParentalGuard.Core.Tests;

public class UnlockHandlerTests
{
    private static (UnlockHandler handler, LockManager lm) Build(DateTimeOffset now)
        => Build(() => now);

    private static (UnlockHandler handler, LockManager lm) Build(Func<DateTimeOffset> clock)
    {
        var cfg = CloveParentalGuardConfig.Default();
        cfg.Code = CodeHasher.Hash("parent");
        var lm = new LockManager(clock);
        return (new UnlockHandler(lm, () => cfg, clock), lm);
    }

    [Fact]
    public void Status_Reports_Locked_By_Default()
    {
        var (handler, _) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("status"));
        Assert.True(resp.Success);
        Assert.True(resp.IsLocked);
    }

    [Fact]
    public void Correct_Code_And_Valid_Duration_Unlocks()
    {
        var (handler, lm) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 60));
        Assert.True(resp.Success);
        Assert.False(resp.IsLocked);
        Assert.False(lm.IsLocked);
        Assert.Equal(3600, resp.RemainingSeconds);
    }

    [Fact]
    public void Wrong_Code_Does_Not_Unlock()
    {
        var (handler, lm) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("unlock", "nope", 60));
        Assert.False(resp.Success);
        Assert.True(lm.IsLocked);
        Assert.NotNull(resp.Error);
    }

    [Fact]
    public void Invalid_Duration_Rejected()
    {
        var (handler, lm) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 999));
        Assert.False(resp.Success);
        Assert.True(lm.IsLocked);
        Assert.NotNull(resp.Error);
    }

    [Fact]
    public void Locks_Out_After_Five_Wrong_Attempts()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (handler, lm) = Build(() => now);
        for (int i = 0; i < 5; i++)
            Assert.False(handler.Handle(new PipeRequest("unlock", "wrong", 60)).Success);

        // Correct code is now rejected because we are locked out.
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 60));
        Assert.False(resp.Success);
        Assert.True(lm.IsLocked);
        Assert.Contains("Try again", resp.Error);
    }

    [Fact]
    public void Lockout_Expires_Then_Correct_Code_Unlocks()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (handler, lm) = Build(() => now);
        for (int i = 0; i < 5; i++)
            handler.Handle(new PipeRequest("unlock", "wrong", 60));

        now = now.AddSeconds(31); // first lockout is 30s
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 60));
        Assert.True(resp.Success);
        Assert.False(lm.IsLocked);
    }

    [Fact]
    public void Status_Includes_Configured_Durations()
    {
        var (handler, _) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("status"));
        Assert.NotNull(resp.Durations);
        Assert.Equal(new[] { 30, 60, 120, 180 }, resp.Durations);
    }

    [Fact]
    public void Correct_Code_Resets_Failure_Counter()
    {
        var now = DateTimeOffset.UnixEpoch;
        var (handler, _) = Build(() => now);
        for (int i = 0; i < 4; i++)
            handler.Handle(new PipeRequest("unlock", "wrong", 60));

        Assert.True(handler.Handle(new PipeRequest("unlock", "parent", 60)).Success);

        // Counter reset: 4 more wrong attempts must not trigger lockout yet.
        for (int i = 0; i < 4; i++)
            handler.Handle(new PipeRequest("unlock", "wrong", 60));
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 60));
        Assert.True(resp.Success);
    }
}
