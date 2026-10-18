using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

[SupportedOSPlatform("windows")]
public class SystemProcessProvider : IProcessProvider
{
    public IEnumerable<ProcessInfo> GetProcesses()
    {
        var processes = Process.GetProcesses();

        // Command lines are only needed to recognise the Minecraft game running inside Java,
        // so query WMI once per scan and only when a Java process exists.
        Dictionary<int, string>? javaCommandLines = null;
        if (processes.Any(p => IsJava(p.ProcessName)))
            javaCommandLines = JavaCommandLines();

        foreach (var p in processes)
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied / exited */ }

            string? commandLine = null;
            javaCommandLines?.TryGetValue(p.Id, out commandLine);
            yield return new ProcessInfo(p.Id, p.ProcessName, path, commandLine);
        }
    }

    private static bool IsJava(string name) =>
        name.Equals("java", StringComparison.OrdinalIgnoreCase)
        || name.Equals("javaw", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<int, string> JavaCommandLines()
    {
        var result = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, CommandLine FROM Win32_Process " +
                "WHERE Name = 'java.exe' OR Name = 'javaw.exe'");
            foreach (ManagementObject mo in searcher.Get())
            {
                using (mo)
                {
                    if (mo["CommandLine"] is string cl && mo["ProcessId"] is uint pid)
                        result[(int)pid] = cl;
                }
            }
        }
        catch { /* WMI unavailable: fall back to name/folder rules only */ }
        return result;
    }
}
