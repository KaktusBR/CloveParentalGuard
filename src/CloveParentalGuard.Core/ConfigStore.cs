using System.IO;
using System.Text.Json;

namespace CloveParentalGuard.Core;

public static class ConfigStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static CloveParentalGuardConfig Load(string path)
    {
        if (!File.Exists(path)) return CloveParentalGuardConfig.Default();
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CloveParentalGuardConfig>(json, Options) ?? CloveParentalGuardConfig.Default();
    }

    public static void Save(string path, CloveParentalGuardConfig config)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(config, Options));
    }
}
