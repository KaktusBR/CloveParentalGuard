using System.IO;

namespace CloveParentalGuard.Core;

public enum BlocklistOutcome { Updated, UpToDate, Rejected }

public sealed record BlocklistUpdateResult(BlocklistOutcome Outcome, string Message, BlocklistFile? List = null);

/// <summary>The decision logic for a downloaded list. Pure: no network, no disk.</summary>
public static class BlocklistUpdater
{
    /// <summary>Order matters: authenticity first, then structure, then freshness.</summary>
    public static BlocklistUpdateResult Evaluate(byte[] json, string signatureBase64,
        IReadOnlyCollection<string> trustedKeys, long currentVersion)
    {
        if (trustedKeys.Count == 0)
            return new(BlocklistOutcome.Rejected, "No trusted signing keys are built into this app.");
        if (json.Length == 0 || json.Length > BlocklistValidator.MaxBytes)
            return new(BlocklistOutcome.Rejected, "List has an unexpected size.");

        if (!BlocklistSigner.Verify(json, signatureBase64, trustedKeys))
            return new(BlocklistOutcome.Rejected, "Signature check failed - list ignored.");

        BlocklistFile list;
        try { list = BlocklistValidator.Parse(json); }
        catch (BlocklistException ex) { return new(BlocklistOutcome.Rejected, "List rejected: " + ex.Message); }

        if (list.Version < currentVersion)
            return new(BlocklistOutcome.Rejected, "List is older than the one in use - ignored.");
        if (list.Version == currentVersion)
            return new(BlocklistOutcome.UpToDate, "Already up to date.", list);

        return new(BlocklistOutcome.Updated, $"Updated: {list.EntryCount} entries.", list);
    }
}

/// <summary>On-disk copy of the last verified list. Re-verified on every load, so tampering is ignored.</summary>
public sealed class BlocklistCache
{
    private readonly string _dir;
    private readonly IReadOnlyCollection<string> _keys;

    public BlocklistCache(string directory, IReadOnlyCollection<string> trustedKeys)
    {
        _dir = directory;
        _keys = trustedKeys;
    }

    public string ListPath => Path.Combine(_dir, "blocklist-cache.json");
    private string SigPath => Path.Combine(_dir, "blocklist-cache.sig");

    /// <summary>The cached list if (and only if) it still verifies and validates.</summary>
    public BlocklistFile? Load()
    {
        try
        {
            if (!File.Exists(ListPath) || !File.Exists(SigPath)) return null;
            var json = File.ReadAllBytes(ListPath);
            var sig = File.ReadAllText(SigPath);
            if (!BlocklistSigner.Verify(json, sig, _keys)) return null;
            return BlocklistValidator.Parse(json);
        }
        catch { return null; }
    }

    public void Save(byte[] json, string signatureBase64)
    {
        Directory.CreateDirectory(_dir);
        // Signature first: a reader that sees the new list always finds a matching signature.
        WriteAtomic(SigPath, System.Text.Encoding.ASCII.GetBytes(signatureBase64.Trim()));
        WriteAtomic(ListPath, json);
    }

    public void Clear()
    {
        foreach (var p in new[] { ListPath, SigPath })
            try { File.Delete(p); } catch { /* best effort */ }
    }

    public DateTime LastWriteUtc => File.Exists(ListPath) ? File.GetLastWriteTimeUtc(ListPath) : DateTime.MinValue;

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
