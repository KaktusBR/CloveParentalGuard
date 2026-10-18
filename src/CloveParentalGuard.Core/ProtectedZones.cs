using System.IO;

namespace CloveParentalGuard.Core;

/// <summary>
/// Things the blocklist may never touch, however it got there: core Windows processes,
/// browsers, this program itself, and the domains Windows and GitHub need. Enforced when
/// a list is validated AND again at kill time, so even a mistake in a signed list cannot
/// take the PC down.
/// </summary>
public static class ProtectedZones
{
    private static readonly HashSet<string> ProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "csrss", "winlogon", "lsass", "services", "smss", "wininit",
        "dwm", "system", "registry", "taskmgr", "cmd", "conhost", "powershell", "pwsh",
        "dllhost", "rundll32", "regedit", "mmc", "msiexec", "sihost", "taskhostw", "ctfmon",
        "runtimebroker", "searchhost", "startmenuexperiencehost", "shellexperiencehost",
        "trustedinstaller", "tiworker", "wuauclt", "audiodg", "fontdrvhost", "lsm",
        "chrome", "msedge", "firefox", "brave", "opera", "iexplore",
        "cloveparentalguard",
    };

    private static readonly string[] ProtectedDomains =
    {
        "microsoft.com", "windows.com", "windowsupdate.com", "live.com", "office.com",
        "office365.com", "msftconnecttest.com", "msftncsi.com", "bing.com", "msn.com",
        "azure.com", "github.com", "githubusercontent.com", "google.com", "gstatic.com",
        "cloudflare.com", "localhost",
    };

    // Folders so broad that blocking them would block everything underneath.
    private static readonly HashSet<string> GenericRoots = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Users", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData",
        @"C:\Windows", @"C:\Windows\System32", @"C:\Temp", @"C:\Games",
    };

    private static string Windows =>
        Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\');

    private static string[] OwnFolders => new[]
    {
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "CloveParentalGuard"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "CloveParentalGuard"),
    };

    public static bool IsProtectedProcessName(string exeName)
    {
        var n = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;
        return ProcessNames.Contains(n);
    }

    public static bool IsProtectedDomain(string domain)
    {
        var d = domain.Trim().TrimEnd('.').ToLowerInvariant();
        return ProtectedDomains.Any(p => d == p || d.EndsWith("." + p, StringComparison.Ordinal));
    }

    /// <summary>True for a process image that must never be killed (Windows itself, this program).</summary>
    public static bool IsProtectedImagePath(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        string full;
        try { full = Path.GetFullPath(imagePath); } catch { return false; }
        return IsUnder(full, Windows) || OwnFolders.Any(f => IsUnder(full, f));
    }

    /// <summary>True if a folder rule (may contain * segments) would cover protected or overly broad ground.</summary>
    public static bool IsProtectedFolderRule(string rule)
    {
        var norm = rule.Trim().TrimEnd('\\');
        if (norm.Length < 3) return true;                       // "C:" and shorter
        if (GenericRoots.Contains(norm)) return true;
        if (IsUnder(norm, Windows) || IsUnder(Windows, norm)) return true;
        if (OwnFolders.Any(f => IsUnder(norm, f) || IsUnder(f, norm))) return true;
        return false;
    }

    private static bool IsUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
}
