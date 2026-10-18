using System.Reflection;

namespace CloveParentalGuard.Core;

/// <summary>
/// Data baked into the exe at build time: the blocklist as it was when this version was built
/// (the offline fallback and the version floor) and the public keys allowed to sign updates.
/// Neither needs a signature - they are part of the exe the parent chose to install.
/// </summary>
public static class BuiltInBlocklist
{
    public static BlocklistFile Load()
    {
        using var s = Resource("blocklist.json");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return BlocklistValidator.Parse(ms.ToArray());
    }

    public static IReadOnlyList<string> TrustedKeys()
    {
        using var r = new StreamReader(Resource("blocklist-public-keys.txt"));
        return r.ReadToEnd()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => !l.StartsWith('#'))
            .ToList();
    }

    private static Stream Resource(string name) =>
        typeof(BuiltInBlocklist).Assembly.GetManifestResourceStream(name)
        ?? throw new InvalidOperationException("missing embedded resource " + name);
}
