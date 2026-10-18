using System.Runtime.Versioning;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

[SupportedOSPlatform("windows")]
public class Worker : BackgroundService
{
    private static readonly Func<DateTimeOffset> Clock = () => DateTimeOffset.Now;

    private readonly ILogger<Worker> _logger;
    private readonly LockManager _lockManager;
    private readonly ProcessScanner _scanner;
    private readonly HostsFileApplier _hosts;
    private readonly PipeServer _pipeServer;
    private readonly BlockEventLog _blockLog = new(Clock);
    private bool _hostsBlocked = true; // assume locked → blocked on start

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _lockManager = new LockManager(Clock);
        var handler = new UnlockHandler(_lockManager, LoadConfig, Clock, _blockLog);
        _scanner = new ProcessScanner(_lockManager, LoadConfig,
            new SystemProcessProvider(), new SystemProcessKiller());
        _hosts = new HostsFileApplier(Constants.HostsPath);
        _pipeServer = new PipeServer(handler, Constants.PipeName);
    }

    private CloveParentalGuardConfig LoadConfig() => ConfigStore.Load(Constants.ConfigPath);

    // "minecraft.exe" → "Minecraft" for a tidy notification label.
    private static string FriendlyName(ProcessInfo p)
    {
        var name = p.Name;
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];
        return name.Length == 0 ? p.Name
            : char.ToUpperInvariant(name[0]) + name[1..];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = _pipeServer.RunAsync(stoppingToken)
            .ContinueWith(t => _logger.LogError(t.Exception, "CloveParentalGuard pipe server faulted"),
                TaskContinuationOptions.OnlyOnFaulted);
        SyncHosts(forced: true);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var killed = _scanner.ScanAndEnforce();
                if (killed.Count > 0)
                    _blockLog.Record(killed.Select(FriendlyName).Distinct());
                SyncHosts(forced: false);
            }
            catch (Exception ex) { _logger.LogError(ex, "scan loop error"); }
            await Task.Delay(Constants.ScanInterval, stoppingToken);
        }
    }

    private void SyncHosts(bool forced)
    {
        var shouldBlock = _lockManager.IsLocked;
        if (!forced && shouldBlock == _hostsBlocked) return;
        try
        {
            if (shouldBlock) _hosts.ApplyBlock(LoadConfig().BlockedDomains);
            else _hosts.RemoveBlock();
            _hostsBlocked = shouldBlock;
        }
        catch (Exception ex) { _logger.LogError(ex, "hosts sync error"); }
    }
}
