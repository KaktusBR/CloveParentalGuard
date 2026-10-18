using System.IO;
using System.Runtime.Versioning;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Core.Tests;

public class HostsFileApplierTests
{
    [SupportedOSPlatform("windows")]
    [Fact]
    public void ApplyBlock_Then_RemoveBlock_Roundtrips_File()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hosts");
        File.WriteAllText(path, "127.0.0.1 localhost\n");
        var applier = new HostsFileApplier(path);

        applier.ApplyBlock(new[] { "poki.com" });
        var blocked = File.ReadAllText(path);
        Assert.Contains("0.0.0.0 poki.com", blocked);
        Assert.Contains("127.0.0.1 localhost", blocked);

        applier.RemoveBlock();
        var restored = File.ReadAllText(path);
        Assert.DoesNotContain("poki.com", restored);
        Assert.Contains("127.0.0.1 localhost", restored);
        File.Delete(path);
    }
}
