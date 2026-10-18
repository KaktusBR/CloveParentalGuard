using System.IO;

namespace CloveParentalGuard.Core.Tests;

public class ConfigStoreTests
{
    [Fact]
    public void Load_Missing_File_Returns_Default()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var cfg = ConfigStore.Load(path);
        Assert.NotNull(cfg);
        Assert.Contains(60, cfg.DurationsMinutes);
    }

    [Fact]
    public void Save_Then_Load_Roundtrips()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var cfg = CloveParentalGuardConfig.Default();
        cfg.Code = CodeHasher.Hash("abc");
        cfg.BlockedExecutables.Add("steam.exe");
        cfg.BlockedDomains.Add("poki.com");
        ConfigStore.Save(path, cfg);

        var loaded = ConfigStore.Load(path);
        Assert.Contains("steam.exe", loaded.BlockedExecutables);
        Assert.Contains("poki.com", loaded.BlockedDomains);
        Assert.True(CodeHasher.Verify("abc", loaded.Code!));
        File.Delete(path);
    }
}
