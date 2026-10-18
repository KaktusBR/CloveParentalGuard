using System.IO;
using System.Runtime.Versioning;

namespace CloveParentalGuard.Core;

[SupportedOSPlatform("windows")]
public class HostsFileApplier
{
    private readonly string _hostsPath;
    public HostsFileApplier(string hostsPath) => _hostsPath = hostsPath;

    public void ApplyBlock(IEnumerable<string> domains)
    {
        var current = File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath) : "";
        File.WriteAllText(_hostsPath, HostsFileEditor.Apply(current, domains));
    }

    public void RemoveBlock()
    {
        if (!File.Exists(_hostsPath)) return;
        var current = File.ReadAllText(_hostsPath);
        File.WriteAllText(_hostsPath, HostsFileEditor.Remove(current));
    }
}
