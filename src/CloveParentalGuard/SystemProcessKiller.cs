using System.Diagnostics;
using System.Runtime.Versioning;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

[SupportedOSPlatform("windows")]
public class SystemProcessKiller : IProcessKiller
{
    public void Kill(int pid)
    {
        var p = Process.GetProcessById(pid);
        p.Kill(entireProcessTree: true);
    }
}
