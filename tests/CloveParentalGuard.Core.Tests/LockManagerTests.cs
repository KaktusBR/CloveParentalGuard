namespace CloveParentalGuard.Core.Tests;

public class LockManagerTests
{
    [Fact]
    public void Defaults_To_Locked()
    {
        var lm = new LockManager(() => DateTimeOffset.UnixEpoch);
        Assert.True(lm.IsLocked);
        Assert.Null(lm.Remaining);
    }

    [Fact]
    public void Unlock_Makes_It_Unlocked_Until_Expiry()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromHours(1));
        Assert.False(lm.IsLocked);
        Assert.Equal(TimeSpan.FromHours(1), lm.Remaining);
    }

    [Fact]
    public void Relocks_After_Expiry()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromMinutes(30));
        now = now.AddMinutes(31);
        Assert.True(lm.IsLocked);
        Assert.Null(lm.Remaining);
    }

    [Fact]
    public void Lock_Forces_Locked_Immediately()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromHours(2));
        lm.Lock();
        Assert.True(lm.IsLocked);
    }
}
