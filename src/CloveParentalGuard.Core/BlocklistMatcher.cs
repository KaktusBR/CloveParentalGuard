using System.IO;
using System.Text.RegularExpressions;

namespace CloveParentalGuard.Core;

public static class BlocklistMatcher
{
    public static bool IsBlocked(string processName, string? executablePath, CloveParentalGuardConfig config,
        string? commandLine = null)
    {
        if (!string.IsNullOrEmpty(commandLine))
            foreach (var fragment in config.BlockedCommandLines)
                if (fragment.Length > 0
                    && commandLine.Contains(fragment, StringComparison.OrdinalIgnoreCase))
                    return true;

        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        foreach (var blocked in config.BlockedExecutables)
            if (string.Equals(blocked, name, StringComparison.OrdinalIgnoreCase))
                return true;

        if (!string.IsNullOrEmpty(executablePath))
        {
            string full;
            try { full = NormalizeDir(Path.GetFullPath(executablePath)); }
            catch { full = ""; }

            if (full.Length > 0)
            {
                foreach (var folder in config.BlockedFolders)
                {
                    string root;
                    try { root = NormalizeDir(Path.GetFullPath(folder)); }
                    catch { continue; }

                    if (root.Contains('*'))
                    {
                        // A single * matches one path segment, allowing rules such as
                        // C:\Users\*\AppData\Roaming\.minecraft without blocking javaw.exe globally.
                        var pattern = "^" + Regex.Escape(root)
                            .Replace(@"\*", @"[^\\]*", StringComparison.Ordinal)
                            + @"(?:\\.*)?$";
                        if (Regex.IsMatch(full, pattern,
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                            return true;
                        continue;
                    }

                    if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        || full.Equals(root, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
        }
        return false;
    }

    private static string NormalizeDir(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
