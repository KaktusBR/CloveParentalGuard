namespace CloveParentalGuard.Core;

/// <summary>
/// The parent's own settings (config.json). The built-in and online blocklists are separate
/// (see <see cref="BlocklistFile"/>); the Blocked* lists here are the parent's ADDITIONS to them.
/// </summary>
public class CloveParentalGuardConfig
{
    public HashedCode? Code { get; set; }
    public List<string> BlockedExecutables { get; set; } = new();
    public List<string> BlockedFolders { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
    public List<string> BlockedCommandLines { get; set; } = new();

    /// <summary>Entries (any kind, exact match, case-insensitive) that must never be blocked, even if a list says so.</summary>
    public List<string> Allow { get; set; } = new();

    public List<int> DurationsMinutes { get; set; } = new();

    public static CloveParentalGuardConfig Default() => new()
    {
        DurationsMinutes = new() { 30, 60, 120, 180 }
    };
}
