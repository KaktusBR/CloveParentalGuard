using System.Diagnostics;
using System.Runtime.Versioning;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

[SupportedOSPlatform("windows")]
public class SystemProcessProvider : IProcessProvider
{
    public IEnumerable<ProcessInfo> GetProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied / exited */ }
            yield return new ProcessInfo(p.Id, p.ProcessName, path);
        }
    }
}
