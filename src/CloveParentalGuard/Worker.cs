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
    private string? _appliedDomainsKey;

    // The enforced list: newest of the built-in list and the verified downloaded one.
    private readonly BlocklistFile _builtIn;
    private readonly BlocklistCache _cache;
    private volatile BlocklistFile _list;
    private DateTime _cacheStamp;

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _builtIn = BuiltInBlocklist.Load();
        _cache = new BlocklistCache(Constants.DataDir, BuiltInBlocklist.TrustedKeys());
        _list = EffectiveBlocklist.Pick(_builtIn, _cache.Load());
        _cacheStamp = _cache.LastWriteUtc;
        _lockManager = new LockManager(Clock);
        var handler = new UnlockHandler(_lockManager, LoadConfig, Clock, _blockLog);
        _scanner = new ProcessScanner(_lockManager, LoadConfig,
            new SystemProcessProvider(), new SystemProcessKiller());
        _hosts = new HostsFileApplier(Constants.HostsPath);
        _pipeServer = new PipeServer(handler, Constants.PipeName);
    }

    // The parent's config merged with the active blocklist - this is what gets enforced.
    private CloveParentalGuardConfig LoadConfig() =>
        EffectiveBlocklist.Build(ConfigStore.Load(Constants.ConfigPath), _list);

    // The --admin window can download a newer list into the cache; pick that up (re-verified on load).
    private void ReloadListIfCacheChanged()
    {
        var stamp = _cache.LastWriteUtc;
        if (stamp == _cacheStamp) return;
        _cacheStamp = stamp;
        _list = EffectiveBlocklist.Pick(_builtIn, _cache.Load());
    }

    private async Task RefreshListLoop(CancellationToken ct)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(20), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await BlocklistFetcher.UpdateAsync(_cache, _list.Version, ct);
                _logger.LogInformation("Blocklist refresh: {Outcome} - {Message}", result.Outcome, result.Message);
                ReloadListIfCacheChanged();
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { _logger.LogWarning(ex, "Blocklist refresh failed"); }

            try { await Task.Delay(Constants.BlocklistRefreshInterval, ct); } catch (OperationCanceledException) { return; }
        }
    }

    // "minecraft.exe" → "Minecraft" for a tidy notification label.
    private static string FriendlyName(ProcessInfo p)
    {
        // The game runs inside java/javaw; show "Minecraft" rather than "Javaw".
        if (p.CommandLine is { } cl && cl.Contains("minecraft", StringComparison.OrdinalIgnoreCase)
            || p.CommandLine is { } cl2 && (cl2.Contains("KnotClient", StringComparison.OrdinalIgnoreCase)
                                            || cl2.Contains("forgeclient", StringComparison.OrdinalIgnoreCase)))
            return "Minecraft";

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
        _ = RefreshListLoop(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                ReloadListIfCacheChanged();
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

        // Re-apply when the domain list itself changes while locked (new online list, edited config).
        var domains = shouldBlock
            ? LoadConfig().BlockedDomains.Where(d => !ProtectedZones.IsProtectedDomain(d)).ToList()
            : null;
        var key = domains is null ? null : string.Join('\n', domains.Order(StringComparer.Ordinal));
        if (!forced && shouldBlock == _hostsBlocked && key == _appliedDomainsKey) return;
        try
        {
            if (domains is not null) _hosts.ApplyBlock(domains);
            else _hosts.RemoveBlock();
            _hostsBlocked = shouldBlock;
            _appliedDomainsKey = key;
        }
        catch (Exception ex) { _logger.LogError(ex, "hosts sync error"); }
    }
}
