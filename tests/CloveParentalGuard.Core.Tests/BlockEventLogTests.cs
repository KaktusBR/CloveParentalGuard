namespace CloveParentalGuard.Core.Tests;

public class BlockEventLogTests
{
    private static BlockEventLog New(out List<DateTimeOffset> _)
    {
        var now = DateTimeOffset.UnixEpoch;
        _ = new List<DateTimeOffset>();
        return new BlockEventLog(() => now);
    }

    [Fact]
    public void Empty_Log_Has_Zero_Seq()
    {
        var log = New(out _);
        var (seq, recent) = log.Snapshot();
        Assert.Equal(0, seq);
        Assert.Empty(recent);
    }

    [Fact]
    public void Record_Assigns_Monotonic_Sequence_Per_Name()
    {
        var log = New(out _);
        log.Record(new[] { "Minecraft", "Steam" });
        var (seq, recent) = log.Snapshot();

        Assert.Equal(2, seq);
        Assert.Equal(new[] { "Minecraft", "Steam" }, recent.Select(e => e.AppName));
        Assert.Equal(new[] { 1L, 2L }, recent.Select(e => e.Seq));
    }

    [Fact]
    public void Window_Is_Bounded_But_Seq_Keeps_Growing()
    {
        var log = New(out _);
        for (int i = 0; i < 50; i++) log.Record(new[] { $"app{i}" });

        var (seq, recent) = log.Snapshot();
        Assert.Equal(50, seq);
        Assert.True(recent.Count <= 32);
        Assert.Equal("app49", recent[^1].AppName); // newest retained
    }
}
