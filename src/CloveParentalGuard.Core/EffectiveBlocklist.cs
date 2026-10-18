namespace CloveParentalGuard.Core;

public static class EffectiveBlocklist
{
    /// <summary>
    /// What is actually enforced: the list (online, or built-in) plus the parent's additions,
    /// minus anything the parent has allowed. Returns a new config; the input is not modified.
    /// </summary>
    public static CloveParentalGuardConfig Build(CloveParentalGuardConfig local, BlocklistFile list)
    {
        var allow = new HashSet<string>(local.Allow, StringComparer.OrdinalIgnoreCase);

        List<string> Merge(IEnumerable<string> fromList, IEnumerable<string> fromParent) =>
            fromList.Concat(fromParent)
                .Where(e => !string.IsNullOrWhiteSpace(e) && !allow.Contains(e))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        return new CloveParentalGuardConfig
        {
            Code = local.Code,
            DurationsMinutes = local.DurationsMinutes,
            Allow = local.Allow,
            BlockedExecutables = Merge(list.Executables, local.BlockedExecutables),
            BlockedFolders = Merge(list.Folders, local.BlockedFolders),
            BlockedDomains = Merge(list.Domains, local.BlockedDomains),
            BlockedCommandLines = Merge(list.CommandLines, local.BlockedCommandLines),
        };
    }

    /// <summary>The newest of the verified cached list and the built-in one.</summary>
    public static BlocklistFile Pick(BlocklistFile builtIn, BlocklistFile? cached) =>
        cached is not null && cached.Version > builtIn.Version ? cached : builtIn;
}
