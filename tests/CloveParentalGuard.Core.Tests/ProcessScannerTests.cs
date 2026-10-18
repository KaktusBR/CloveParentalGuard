namespace CloveParentalGuard.Core.Tests;

public class ProcessScannerTests
{
    private class FakeProvider : IProcessProvider
    {
        public List<ProcessInfo> Processes = new();
        public IEnumerable<ProcessInfo> GetProcesses() => Processes;
    }
    private class FakeKiller : IProcessKiller
    {
        public List<int> Killed = new();
        public void Kill(int pid) => Killed.Add(pid);
    }

    private static CloveParentalGuardConfig Cfg() => new() { BlockedExecutables = new() { "steam.exe" } };

    [Fact]
    public void Kills_Blocked_Process_When_Locked()
    {
        var provider = new FakeProvider { Processes = { new ProcessInfo(10, "steam.exe", null), new ProcessInfo(11, "notepad.exe", null) } };
        var killer = new FakeKiller();
        var lm = new LockManager(() => DateTimeOffset.UnixEpoch); // locked
        var scanner = new ProcessScanner(lm, Cfg, provider, killer);

        var killed = scanner.ScanAndEnforce();

        Assert.Equal(new[] { 10 }, killer.Killed);
        Assert.Single(killed);
        Assert.Equal("steam.exe", killed[0].Name);
    }

    [Fact]
    public void Kills_Nothing_When_Unlocked()
    {
        var provider = new FakeProvider { Processes = { new ProcessInfo(10, "steam.exe", null) } };
        var killer = new FakeKiller();
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromHours(1));
        var scanner = new ProcessScanner(lm, Cfg, provider, killer);

        var killed = scanner.ScanAndEnforce();

        Assert.Empty(killer.Killed);
        Assert.Empty(killed);
    }
}
