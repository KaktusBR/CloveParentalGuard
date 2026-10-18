using System.Text;

namespace CloveParentalGuard.Core.Tests;

public class BlocklistSignerTests
{
    private const string Pass = "correct horse battery staple";
    private static readonly byte[] Data = Encoding.UTF8.GetBytes("{\"hello\":\"world\"}");

    [Fact]
    public void Signed_Data_Verifies()
    {
        var (pub, priv) = BlocklistSigner.GenerateKeyPair(Pass);
        var sig = BlocklistSigner.Sign(Data, priv, Pass);
        Assert.True(BlocklistSigner.Verify(Data, sig, new[] { pub }));
    }

    [Fact]
    public void Tampered_Data_Fails()
    {
        var (pub, priv) = BlocklistSigner.GenerateKeyPair(Pass);
        var sig = BlocklistSigner.Sign(Data, priv, Pass);
        var tampered = Encoding.UTF8.GetBytes("{\"hello\":\"WORLD\"}");
        Assert.False(BlocklistSigner.Verify(tampered, sig, new[] { pub }));
    }

    [Fact]
    public void Signature_From_Untrusted_Key_Fails()
    {
        var (_, priv) = BlocklistSigner.GenerateKeyPair(Pass);
        var (otherPub, _) = BlocklistSigner.GenerateKeyPair(Pass);
        var sig = BlocklistSigner.Sign(Data, priv, Pass);
        Assert.False(BlocklistSigner.Verify(Data, sig, new[] { otherPub }));
    }

    [Fact]
    public void Either_Of_Two_Trusted_Keys_Is_Accepted()
    {
        var (primary, _) = BlocklistSigner.GenerateKeyPair(Pass);
        var (backup, backupPriv) = BlocklistSigner.GenerateKeyPair(Pass);
        var sig = BlocklistSigner.Sign(Data, backupPriv, Pass);
        Assert.True(BlocklistSigner.Verify(Data, sig, new[] { primary, backup }));
    }

    [Fact]
    public void Wrong_Passphrase_Cannot_Sign()
    {
        var (_, priv) = BlocklistSigner.GenerateKeyPair(Pass);
        Assert.ThrowsAny<Exception>(() => BlocklistSigner.Sign(Data, priv, "not the passphrase!!"));
    }

    [Fact]
    public void Weak_Passphrase_Is_Refused()
        => Assert.Throws<ArgumentException>(() => BlocklistSigner.GenerateKeyPair("short"));

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !!!")]
    [InlineData("AAAA")]
    public void Garbage_Signature_Is_False_Not_An_Exception(string sig)
    {
        var (pub, _) = BlocklistSigner.GenerateKeyPair(Pass);
        Assert.False(BlocklistSigner.Verify(Data, sig, new[] { pub }));
    }

    [Fact]
    public void Garbage_Public_Key_Is_Skipped_Not_An_Exception()
    {
        var (pub, priv) = BlocklistSigner.GenerateKeyPair(Pass);
        var sig = BlocklistSigner.Sign(Data, priv, Pass);
        Assert.True(BlocklistSigner.Verify(Data, sig, new[] { "garbage", pub }));
    }
}

public class BlocklistValidatorTests
{
    private static byte[] Json(Action<BlocklistFile> edit)
    {
        var f = new BlocklistFile { Version = 5 };
        edit(f);
        return f.ToBytes();
    }

    [Fact]
    public void Valid_List_Parses()
    {
        var list = BlocklistValidator.Parse(Json(f =>
        {
            f.Executables.Add("Modrinth App.exe");
            f.Folders.Add(@"C:\Users\*\AppData\Roaming\ModrinthApp");
            f.Folders.Add(@"C:\Amazon Games");
            f.Domains.Add("poki.com");
            f.CommandLines.Add("net.minecraft.client.main.Main");
        }));
        Assert.Equal(5, list.Version);
        Assert.Equal(5, list.EntryCount);
    }

    [Theory]
    [InlineData("explorer.exe")]
    [InlineData("svchost.exe")]
    [InlineData("chrome.exe")]
    [InlineData("CloveParentalGuard.exe")]
    [InlineData(@"C:\Windows\notepad.exe")]   // path, not a name
    [InlineData("noextension")]
    [InlineData("a.exe; calc")]
    public void Dangerous_Executables_Are_Rejected(string exe)
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f => f.Executables.Add(exe))));

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\Windows\System32")]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Users")]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Users\*")]                         // every user profile
    [InlineData(@"C:\*\Games")]                         // wildcard too early
    [InlineData(@"C:\Program Files\CloveParentalGuard")]
    [InlineData(@"C:\Games\..\Windows")]
    [InlineData(@"relative\path")]
    public void Dangerous_Folders_Are_Rejected(string folder)
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f => f.Folders.Add(folder))));

    [Theory]
    [InlineData("microsoft.com")]
    [InlineData("update.microsoft.com")]
    [InlineData("windowsupdate.com")]
    [InlineData("github.com")]
    [InlineData("raw.githubusercontent.com")]
    [InlineData("localhost")]
    [InlineData("Poki.com")]            // must be lowercase
    [InlineData("not a domain")]
    [InlineData("nodot")]
    public void Dangerous_Domains_Are_Rejected(string domain)
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f => f.Domains.Add(domain))));

    [Theory]
    [InlineData("short")]
    [InlineData(" leading space-and-long-enough")]
    public void Bad_Command_Lines_Are_Rejected(string fragment)
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f => f.CommandLines.Add(fragment))));

    [Fact]
    public void One_Bad_Entry_Rejects_The_Whole_File()
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f =>
        {
            f.Domains.Add("poki.com");
            f.Executables.Add("explorer.exe");
        })));

    [Fact]
    public void Unknown_Schema_Is_Rejected()
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f => f.SchemaVersion = 99)));

    [Fact]
    public void Missing_Version_Is_Rejected()
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f => f.Version = 0)));

    [Fact]
    public void Too_Many_Entries_Are_Rejected()
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Json(f =>
        {
            for (var i = 0; i <= BlocklistValidator.MaxEntriesPerList; i++) f.Domains.Add($"site{i}.example.org");
        })));

    [Fact]
    public void Oversized_File_Is_Rejected()
        => Assert.Throws<BlocklistException>(() =>
            BlocklistValidator.Parse(new byte[BlocklistValidator.MaxBytes + 1]));

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("null")]
    public void Garbage_Is_Rejected(string text)
        => Assert.Throws<BlocklistException>(() => BlocklistValidator.Parse(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void Serialized_List_Contains_Only_List_Data()
    {
        var text = Encoding.UTF8.GetString(new BlocklistFile { Version = 1 }.ToBytes());
        Assert.DoesNotContain("entryCount", text);
    }

    [Fact]
    public void Built_In_List_Is_Valid_And_Covers_Minecraft()
    {
        var list = BuiltInBlocklist.Load();
        Assert.Contains("MinecraftLauncher.exe", list.Executables);
        Assert.Contains("knot.KnotClient", list.CommandLines);
        Assert.True(list.Version >= 1);
    }
}

public class BlocklistUpdaterTests : IDisposable
{
    private const string Pass = "correct horse battery staple";
    private readonly (string Pub, string Priv) _key = BlocklistSigner.GenerateKeyPair(Pass);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cpg-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private (byte[] Json, string Sig) Signed(long version, params string[] domains)
    {
        var f = new BlocklistFile { Version = version };
        f.Domains.AddRange(domains);
        var json = f.ToBytes();
        return (json, BlocklistSigner.Sign(json, _key.Priv, Pass));
    }

    private BlocklistUpdateResult Eval((byte[] Json, string Sig) s, long current)
        => BlocklistUpdater.Evaluate(s.Json, s.Sig, new[] { _key.Pub }, current);

    [Fact]
    public void Newer_Signed_List_Is_Accepted()
    {
        var r = Eval(Signed(10, "poki.com"), current: 5);
        Assert.Equal(BlocklistOutcome.Updated, r.Outcome);
        Assert.Equal(10, r.List!.Version);
    }

    [Fact]
    public void Same_Version_Is_UpToDate()
        => Assert.Equal(BlocklistOutcome.UpToDate, Eval(Signed(5, "poki.com"), current: 5).Outcome);

    [Fact]
    public void Older_Signed_List_Is_Rejected_Rollback_Protection()
        => Assert.Equal(BlocklistOutcome.Rejected, Eval(Signed(3, "poki.com"), current: 5).Outcome);

    [Fact]
    public void Tampered_List_Is_Rejected()
    {
        var (json, sig) = Signed(10, "poki.com");
        var evil = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(json).Replace("poki.com", "evil.org"));
        var r = BlocklistUpdater.Evaluate(evil, sig, new[] { _key.Pub }, 5);
        Assert.Equal(BlocklistOutcome.Rejected, r.Outcome);
    }

    [Fact]
    public void Signed_But_Dangerous_List_Is_Still_Rejected()
    {
        var r = Eval(Signed(10, "windowsupdate.com"), current: 5);
        Assert.Equal(BlocklistOutcome.Rejected, r.Outcome);
    }

    [Fact]
    public void No_Trusted_Keys_Means_Nothing_Is_Accepted()
    {
        var (json, sig) = Signed(10, "poki.com");
        Assert.Equal(BlocklistOutcome.Rejected,
            BlocklistUpdater.Evaluate(json, sig, Array.Empty<string>(), 0).Outcome);
    }

    [Fact]
    public void Cache_Roundtrip_Returns_The_List()
    {
        var cache = new BlocklistCache(_dir, new[] { _key.Pub });
        var (json, sig) = Signed(10, "poki.com");
        cache.Save(json, sig);
        Assert.Equal(10, cache.Load()!.Version);
    }

    [Fact]
    public void Tampered_Cache_Is_Ignored()
    {
        var cache = new BlocklistCache(_dir, new[] { _key.Pub });
        var (json, sig) = Signed(10, "poki.com");
        cache.Save(json, sig);
        File.WriteAllText(cache.ListPath, File.ReadAllText(cache.ListPath).Replace("poki.com", "evil.org"));
        Assert.Null(cache.Load());
    }

    [Fact]
    public void Missing_Cache_Is_Null()
        => Assert.Null(new BlocklistCache(_dir, new[] { _key.Pub }).Load());
}

/// <summary>
/// Guards the repo itself: a release (or any CI run) fails if blocklist.json was edited
/// without re-signing, or if the signing keys are missing from the app.
/// </summary>
public class PublishedBlocklistTests
{
    private static string RepoFile(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, name);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(name + " not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void App_Trusts_Primary_And_Backup_Keys()
        => Assert.True(BuiltInBlocklist.TrustedKeys().Count >= 2,
            "Expected the primary and backup public keys in blocklist-public-keys.txt");

    [Fact]
    public void Committed_Blocklist_Is_Validly_Signed()
    {
        var result = BlocklistUpdater.Evaluate(
            File.ReadAllBytes(RepoFile("blocklist.json")),
            File.ReadAllText(RepoFile("blocklist.sig")),
            BuiltInBlocklist.TrustedKeys(),
            currentVersion: 0);

        Assert.True(result.Outcome != BlocklistOutcome.Rejected,
            result.Message + " - run: dotnet run --project tools/BlocklistTool -- sign");
    }

    [Fact]
    public void Built_In_Copy_Matches_The_File_In_The_Repo()
        => Assert.Equal(File.ReadAllBytes(RepoFile("blocklist.json")).Length,
            typeof(BuiltInBlocklist).Assembly.GetManifestResourceStream("blocklist.json")!.Length);
}

public class EffectiveBlocklistTests
{
    [Fact]
    public void Merges_List_And_Parent_Additions_Without_Duplicates()
    {
        var list = new BlocklistFile { Version = 1 };
        list.Executables.AddRange(new[] { "steam.exe", "Foo.exe" });
        var local = new CloveParentalGuardConfig { BlockedExecutables = new() { "foo.exe", "bar.exe" } };

        var eff = EffectiveBlocklist.Build(local, list);
        Assert.Equal(3, eff.BlockedExecutables.Count);
        Assert.Contains("bar.exe", eff.BlockedExecutables);
    }

    [Fact]
    public void Parent_Allow_List_Overrides_The_Online_List()
    {
        var list = new BlocklistFile { Version = 1 };
        list.Domains.AddRange(new[] { "poki.com", "miniclip.com" });
        var local = new CloveParentalGuardConfig { Allow = new() { "POKI.COM" } };

        var eff = EffectiveBlocklist.Build(local, list);
        Assert.DoesNotContain("poki.com", eff.BlockedDomains);
        Assert.Contains("miniclip.com", eff.BlockedDomains);
    }

    [Fact]
    public void Build_Does_Not_Modify_The_Input_And_Keeps_Code_And_Durations()
    {
        var local = new CloveParentalGuardConfig { DurationsMinutes = new() { 15 } };
        var eff = EffectiveBlocklist.Build(local, new BlocklistFile { Version = 1 });
        Assert.Equal(new[] { 15 }, eff.DurationsMinutes);
        Assert.Empty(local.BlockedExecutables);
    }

    [Fact]
    public void Pick_Prefers_The_Newer_List()
    {
        var builtIn = new BlocklistFile { Version = 5 };
        Assert.Same(builtIn, EffectiveBlocklist.Pick(builtIn, new BlocklistFile { Version = 4 }));
        Assert.Same(builtIn, EffectiveBlocklist.Pick(builtIn, null));
        var newer = new BlocklistFile { Version = 6 };
        Assert.Same(newer, EffectiveBlocklist.Pick(builtIn, newer));
    }
}

public class ProtectedZonesTests
{
    [Theory]
    [InlineData(@"C:\Windows\explorer.exe", true)]
    [InlineData(@"C:\Windows\System32\svchost.exe", true)]
    [InlineData(@"C:\Program Files\CloveParentalGuard\CloveParentalGuard.exe", true)]
    [InlineData(@"C:\Program Files (x86)\Steam\steam.exe", false)]
    [InlineData(null, false)]
    public void Protected_Image_Paths(string? path, bool expected)
        => Assert.Equal(expected, ProtectedZones.IsProtectedImagePath(path));

    [Fact]
    public void Scanner_Never_Kills_A_Protected_Image_Even_If_Listed()
    {
        var killer = new FakeKiller();
        var scanner = new ProcessScanner(
            new LockManager(() => DateTimeOffset.Now),
            () => new CloveParentalGuardConfig { BlockedExecutables = new() { "explorer.exe" } },
            new FakeProvider(new ProcessInfo(1, "explorer", @"C:\Windows\explorer.exe")),
            killer);
        scanner.ScanAndEnforce();
        Assert.Empty(killer.Killed);
    }

    private sealed class FakeKiller : IProcessKiller
    {
        public List<int> Killed { get; } = new();
        public void Kill(int pid) => Killed.Add(pid);
    }

    private sealed class FakeProvider : IProcessProvider
    {
        private readonly ProcessInfo[] _p;
        public FakeProvider(params ProcessInfo[] p) => _p = p;
        public IEnumerable<ProcessInfo> GetProcesses() => _p;
    }
}
