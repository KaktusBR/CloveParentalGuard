namespace CloveParentalGuard.Core;

public class ProcessScanner
{
    private readonly LockManager _lockManager;
    private readonly Func<CloveParentalGuardConfig> _configProvider;
    private readonly IProcessProvider _provider;
    private readonly IProcessKiller _killer;

    public ProcessScanner(LockManager lockManager, Func<CloveParentalGuardConfig> configProvider,
        IProcessProvider provider, IProcessKiller killer)
    {
        _lockManager = lockManager;
        _configProvider = configProvider;
        _provider = provider;
        _killer = killer;
    }

    public IReadOnlyList<ProcessInfo> ScanAndEnforce()
    {
        if (!_lockManager.IsLocked) return Array.Empty<ProcessInfo>();
        var cfg = _configProvider();
        var killed = new List<ProcessInfo>();
        foreach (var p in _provider.GetProcesses())
        {
            if (!BlocklistMatcher.IsBlocked(p.Name, p.Path, cfg, p.CommandLine)) continue;
            // Last line of defence, whatever the lists say: never kill Windows or ourselves.
            if (ProtectedZones.IsProtectedImagePath(p.Path)) continue;
            try { _killer.Kill(p.Pid); killed.Add(p); }
            catch { /* process may have exited or be protected; ignore */ }
        }
        return killed;
    }
}
