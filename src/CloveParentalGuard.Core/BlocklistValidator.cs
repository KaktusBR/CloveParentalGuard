using System.Text.Json;
using System.Text.RegularExpressions;

namespace CloveParentalGuard.Core;

public sealed class BlocklistException : Exception
{
    public BlocklistException(string message) : base(message) { }
}

/// <summary>
/// Strict parser for blocklist files. All-or-nothing: a single bad entry rejects the whole file,
/// so a half-valid list is never applied.
/// </summary>
public static class BlocklistValidator
{
    public const int MaxBytes = 256 * 1024;
    public const int MaxEntriesPerList = 1000;
    public const int SupportedSchema = 1;

    private static readonly Regex ExeName = new(@"^[A-Za-z0-9][A-Za-z0-9 ._+()&'-]{0,100}\.exe$", RegexOptions.CultureInvariant);
    private static readonly Regex Hostname = new(
        @"^(?=.{4,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}$", RegexOptions.CultureInvariant);
    private static readonly Regex DrivePath = new(@"^[A-Za-z]:\\[^<>""|?\x00-\x1f]+$", RegexOptions.CultureInvariant);

    public static BlocklistFile Parse(byte[] json)
    {
        if (json.Length == 0) throw new BlocklistException("empty file");
        if (json.Length > MaxBytes) throw new BlocklistException("file too large");

        BlocklistFile list;
        try { list = JsonSerializer.Deserialize<BlocklistFile>(json, BlocklistFile.JsonOptions)
                     ?? throw new BlocklistException("empty document"); }
        catch (JsonException ex) { throw new BlocklistException("invalid JSON: " + ex.Message); }

        if (list.SchemaVersion != SupportedSchema)
            throw new BlocklistException($"unsupported schema {list.SchemaVersion} (update the app)");
        if (list.Version < 1) throw new BlocklistException("missing version");

        Check("executables", list.Executables, CheckExecutable);
        Check("folders", list.Folders, CheckFolder);
        Check("domains", list.Domains, CheckDomain);
        Check("commandLines", list.CommandLines, CheckCommandLine);
        return list;
    }

    private static void Check(string name, List<string>? entries, Action<string> rule)
    {
        if (entries is null) throw new BlocklistException($"{name}: missing");
        if (entries.Count > MaxEntriesPerList) throw new BlocklistException($"{name}: too many entries");
        foreach (var e in entries)
        {
            if (e is null) throw new BlocklistException($"{name}: null entry");
            try { rule(e); }
            catch (BlocklistException ex) { throw new BlocklistException($"{name}: '{e}' {ex.Message}"); }
        }
    }

    private static void CheckExecutable(string e)
    {
        if (!ExeName.IsMatch(e)) throw new BlocklistException("is not a plain .exe file name");
        if (ProtectedZones.IsProtectedProcessName(e)) throw new BlocklistException("is a protected process");
    }

    private static void CheckFolder(string e)
    {
        if (!DrivePath.IsMatch(e)) throw new BlocklistException("is not an absolute drive path");
        if (e.Contains("..", StringComparison.Ordinal)) throw new BlocklistException("contains '..'");
        if (ProtectedZones.IsProtectedFolderRule(e)) throw new BlocklistException("covers a protected or too-broad folder");

        // A wildcard must be followed by something literal, otherwise it matches every folder at that level.
        var segments = e.TrimEnd('\\').Split('\\');
        var firstWild = Array.FindIndex(segments, s => s.Contains('*'));
        if (firstWild >= 0)
        {
            if (firstWild < 2) throw new BlocklistException("wildcard too early in the path");
            if (!segments.Skip(firstWild + 1).Any(s => s.Length > 0 && !s.Contains('*')))
                throw new BlocklistException("wildcard must be followed by a literal folder");
        }
    }

    private static void CheckDomain(string e)
    {
        if (!Hostname.IsMatch(e)) throw new BlocklistException("is not a valid lowercase hostname");
        if (ProtectedZones.IsProtectedDomain(e)) throw new BlocklistException("is a protected domain");
    }

    private static void CheckCommandLine(string e)
    {
        if (e.Length < 8 || e.Length > 200) throw new BlocklistException("must be 8-200 characters");
        if (e != e.Trim() || e.Any(c => c < ' ' || c > '~')) throw new BlocklistException("must be printable ASCII without edge spaces");
    }
}
