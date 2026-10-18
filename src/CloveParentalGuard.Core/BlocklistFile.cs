using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloveParentalGuard.Core;

/// <summary>
/// The online blocklist: plain data, no behaviour. It is trusted only after
/// <see cref="BlocklistSigner"/> has verified its signature and
/// <see cref="BlocklistValidator"/> has accepted every entry.
/// </summary>
public sealed class BlocklistFile
{
    /// <summary>Format version. Bump only for breaking format changes.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Monotonic (unix seconds, set by the signing tool). Older lists are rejected.</summary>
    public long Version { get; set; }

    public List<string> Executables { get; set; } = new();
    public List<string> Folders { get; set; } = new();
    public List<string> Domains { get; set; } = new();
    public List<string> CommandLines { get; set; } = new();

    [JsonIgnore]
    public int EntryCount => Executables.Count + Folders.Count + Domains.Count + CommandLines.Count;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Canonical bytes: indented camelCase JSON, LF line endings, no BOM.</summary>
    public byte[] ToBytes()
    {
        var json = JsonSerializer.Serialize(this, JsonOptions).Replace("\r\n", "\n") + "\n";
        return new System.Text.UTF8Encoding(false).GetBytes(json);
    }
}
