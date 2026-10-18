# CloveParentalGuard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Windows game-blocker: a SYSTEM service a child can't stop that blocks game launchers, installed games, and game websites, unlockable for a chosen duration with a parent code.

**Architecture:** A shared `CloveParentalGuard.Core` library holds all testable logic (code hashing, config, blocklist matching, hosts-file editing, lock/timer state, IPC contract, trust-boundary unlock handler). `CloveParentalGuard.Service` (Worker Service, runs as SYSTEM) wires Core to real process enumeration, the hosts file, and a named-pipe server. `CloveParentalGuard.Agent` (tray app in the child session) and `CloveParentalGuard.Admin` (UAC-elevated config tool) are thin UIs over the pipe / config.

**Tech Stack:** C# / .NET 8, xUnit, System.Text.Json, Named Pipes, WinForms (Agent + Admin), Microsoft.Extensions.Hosting (Worker Service).

## Global Constraints

- Target framework: `net8.0` for Core/Service/tests; `net8.0-windows` for Agent/Admin (WinForms).
- Plaintext unlock code is NEVER stored or written to config — only PBKDF2 (`Rfc2898DeriveBytes`, SHA256, 100000 iterations, 16-byte salt, 32-byte hash, Base64).
- All trust decisions (code verification, lock state, timer) live in the service/Core, never in the Agent.
- Config path: `C:\ProgramData\CloveParentalGuard\config.json` (admin-writable only).
- Named pipe name: `CloveParentalGuardPipe`.
- Hosts file section is bounded by sentinels `# BEGIN CloveParentalGuard` / `# END CloveParentalGuard`; never touch lines outside them.
- Service default state on (re)start is **locked** (fail-safe).
- Exe-name matching is case-insensitive; path matching uses normalized full paths.
- All public APIs use the exact signatures named in each task's **Interfaces** block.

---

### Task 1: Solution + Core/test scaffold

**Files:**
- Create: `CloveParentalGuard.sln`
- Create: `src/CloveParentalGuard.Core/CloveParentalGuard.Core.csproj`
- Create: `tests/CloveParentalGuard.Core.Tests/CloveParentalGuard.Core.Tests.csproj`
- Create: `tests/CloveParentalGuard.Core.Tests/SmokeTest.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: a buildable solution; `CloveParentalGuard.Core` referenced by the test project.

- [ ] **Step 1: Create solution and projects**

Run:
```bash
dotnet new sln -n CloveParentalGuard
dotnet new classlib -n CloveParentalGuard.Core -o src/CloveParentalGuard.Core -f net8.0
dotnet new xunit -n CloveParentalGuard.Core.Tests -o tests/CloveParentalGuard.Core.Tests -f net8.0
dotnet sln add src/CloveParentalGuard.Core/CloveParentalGuard.Core.csproj tests/CloveParentalGuard.Core.Tests/CloveParentalGuard.Core.Tests.csproj
dotnet add tests/CloveParentalGuard.Core.Tests/CloveParentalGuard.Core.Tests.csproj reference src/CloveParentalGuard.Core/CloveParentalGuard.Core.csproj
```
Delete the auto-generated `src/CloveParentalGuard.Core/Class1.cs` and `tests/CloveParentalGuard.Core.Tests/UnitTest1.cs`.

- [ ] **Step 2: Write smoke test**

`tests/CloveParentalGuard.Core.Tests/SmokeTest.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class SmokeTest
{
    [Fact]
    public void Solution_Builds_And_Tests_Run() => Assert.True(true);
}
```

- [ ] **Step 3: Run tests, verify pass**

Run: `dotnet test`
Expected: PASS, 1 test.

- [ ] **Step 4: Commit**

```bash
git add CloveParentalGuard.sln src tests
git commit -m "chore: scaffold CloveParentalGuard solution and Core test project"
```

---

### Task 2: Code hashing (PBKDF2)

**Files:**
- Create: `src/CloveParentalGuard.Core/CodeHasher.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/CodeHasherTests.cs`

**Interfaces:**
- Produces:
  - `record HashedCode(string Salt, string Hash, int Iterations)`
  - `static HashedCode CodeHasher.Hash(string code)`
  - `static bool CodeHasher.Verify(string code, HashedCode stored)`

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/CodeHasherTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class CodeHasherTests
{
    [Fact]
    public void Verify_Returns_True_For_Correct_Code()
    {
        var hashed = CodeHasher.Hash("secret123");
        Assert.True(CodeHasher.Verify("secret123", hashed));
    }

    [Fact]
    public void Verify_Returns_False_For_Wrong_Code()
    {
        var hashed = CodeHasher.Hash("secret123");
        Assert.False(CodeHasher.Verify("wrong", hashed));
    }

    [Fact]
    public void Hash_Uses_Random_Salt_So_Two_Hashes_Differ()
    {
        var a = CodeHasher.Hash("same");
        var b = CodeHasher.Hash("same");
        Assert.NotEqual(a.Salt, b.Salt);
        Assert.NotEqual(a.Hash, b.Hash);
    }

    [Fact]
    public void Plaintext_Never_Appears_In_HashedCode()
    {
        var hashed = CodeHasher.Hash("plaintextcode");
        Assert.DoesNotContain("plaintextcode", hashed.Salt + hashed.Hash);
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter CodeHasherTests`
Expected: FAIL (CodeHasher does not exist).

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/CodeHasher.cs`:
```csharp
using System.Security.Cryptography;

namespace CloveParentalGuard.Core;

public record HashedCode(string Salt, string Hash, int Iterations);

public static class CodeHasher
{
    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static HashedCode Hash(string code)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] hash = Derive(code, salt, Iterations);
        return new HashedCode(Convert.ToBase64String(salt), Convert.ToBase64String(hash), Iterations);
    }

    public static bool Verify(string code, HashedCode stored)
    {
        byte[] salt = Convert.FromBase64String(stored.Salt);
        byte[] expected = Convert.FromBase64String(stored.Hash);
        byte[] actual = Derive(code, salt, stored.Iterations);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(code, salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter CodeHasherTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/CodeHasher.cs tests/CloveParentalGuard.Core.Tests/CodeHasherTests.cs
git commit -m "feat: add PBKDF2 code hashing"
```

---

### Task 3: Config model + JSON store

**Files:**
- Create: `src/CloveParentalGuard.Core/CloveParentalGuardConfig.cs`
- Create: `src/CloveParentalGuard.Core/ConfigStore.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/ConfigStoreTests.cs`

**Interfaces:**
- Consumes: `HashedCode` (Task 2).
- Produces:
  - `class CloveParentalGuardConfig` with mutable props: `HashedCode? Code`, `List<string> BlockedExecutables`, `List<string> BlockedFolders`, `List<string> BlockedDomains`, `List<int> DurationsMinutes`; `static CloveParentalGuardConfig Default()`.
  - `static CloveParentalGuardConfig ConfigStore.Load(string path)` (returns `Default()` if file missing)
  - `static void ConfigStore.Save(string path, CloveParentalGuardConfig config)` (creates directory if needed)

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/ConfigStoreTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter ConfigStoreTests`
Expected: FAIL.

- [ ] **Step 3: Implement config model**

`src/CloveParentalGuard.Core/CloveParentalGuardConfig.cs`:
```csharp
namespace CloveParentalGuard.Core;

public class CloveParentalGuardConfig
{
    public HashedCode? Code { get; set; }
    public List<string> BlockedExecutables { get; set; } = new();
    public List<string> BlockedFolders { get; set; } = new();
    public List<string> BlockedDomains { get; set; } = new();
    public List<int> DurationsMinutes { get; set; } = new();

    public static CloveParentalGuardConfig Default() => new()
    {
        BlockedExecutables = new()
        {
            "steam.exe", "EpicGamesLauncher.exe", "Battle.net.exe",
            "GalaxyClient.exe", "Origin.exe", "EADesktop.exe",
            "RiotClientServices.exe", "Ubisoft Connect.exe", "upc.exe"
        },
        BlockedFolders = new()
        {
            @"C:\Program Files (x86)\Steam\steamapps\common",
            @"C:\Program Files\Epic Games"
        },
        BlockedDomains = new()
        {
            "poki.com", "crazygames.com", "miniclip.com", "roblox.com", "now.gg",
            "coolmathgames.com", "addictinggames.com", "kongregate.com"
        },
        DurationsMinutes = new() { 30, 60, 120, 180 }
    };
}
```

- [ ] **Step 4: Implement store**

`src/CloveParentalGuard.Core/ConfigStore.cs`:
```csharp
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
```

- [ ] **Step 5: Run, verify pass**

Run: `dotnet test --filter ConfigStoreTests`
Expected: PASS, 2 tests.

- [ ] **Step 6: Commit**

```bash
git add src/CloveParentalGuard.Core/CloveParentalGuardConfig.cs src/CloveParentalGuard.Core/ConfigStore.cs tests/CloveParentalGuard.Core.Tests/ConfigStoreTests.cs
git commit -m "feat: add config model and JSON store"
```

---

### Task 4: Blocklist matcher

**Files:**
- Create: `src/CloveParentalGuard.Core/BlocklistMatcher.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/BlocklistMatcherTests.cs`

**Interfaces:**
- Consumes: `CloveParentalGuardConfig` (Task 3).
- Produces: `static bool BlocklistMatcher.IsBlocked(string processName, string? executablePath, CloveParentalGuardConfig config)`
  - `processName` may include or omit `.exe`; match is case-insensitive against `BlockedExecutables`.
  - If `executablePath` is non-null and resides under any `BlockedFolders` entry (case-insensitive, normalized), it is blocked.

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/BlocklistMatcherTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class BlocklistMatcherTests
{
    private static CloveParentalGuardConfig Cfg() => new()
    {
        BlockedExecutables = new() { "steam.exe", "Battle.net.exe" },
        BlockedFolders = new() { @"C:\Games\steamapps\common" }
    };

    [Fact]
    public void Blocks_By_Exe_Name_Case_Insensitive()
        => Assert.True(BlocklistMatcher.IsBlocked("STEAM.EXE", null, Cfg()));

    [Fact]
    public void Blocks_By_Exe_Name_Without_Extension()
        => Assert.True(BlocklistMatcher.IsBlocked("steam", null, Cfg()));

    [Fact]
    public void Blocks_By_Folder_Root()
        => Assert.True(BlocklistMatcher.IsBlocked("game.exe", @"C:\Games\steamapps\common\Doom\doom.exe", Cfg()));

    [Fact]
    public void Allows_Unrelated_Process()
        => Assert.False(BlocklistMatcher.IsBlocked("notepad.exe", @"C:\Windows\notepad.exe", Cfg()));
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter BlocklistMatcherTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/BlocklistMatcher.cs`:
```csharp
using System.IO;

namespace CloveParentalGuard.Core;

public static class BlocklistMatcher
{
    public static bool IsBlocked(string processName, string? executablePath, CloveParentalGuardConfig config)
    {
        var name = processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? processName
            : processName + ".exe";

        foreach (var blocked in config.BlockedExecutables)
            if (string.Equals(blocked, name, StringComparison.OrdinalIgnoreCase))
                return true;

        if (!string.IsNullOrEmpty(executablePath))
        {
            var full = NormalizeDir(Path.GetFullPath(executablePath));
            foreach (var folder in config.BlockedFolders)
            {
                var root = NormalizeDir(Path.GetFullPath(folder));
                if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    || full.Equals(root, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    private static string NormalizeDir(string path)
        => path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter BlocklistMatcherTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/BlocklistMatcher.cs tests/CloveParentalGuard.Core.Tests/BlocklistMatcherTests.cs
git commit -m "feat: add blocklist matcher"
```

---

### Task 5: Hosts-file editor (pure string transform)

**Files:**
- Create: `src/CloveParentalGuard.Core/HostsFileEditor.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/HostsFileEditorTests.cs`

**Interfaces:**
- Produces:
  - `static string HostsFileEditor.Apply(string currentHosts, IEnumerable<string> domains)` — returns hosts text with a CloveParentalGuard sentinel block (replacing any existing one) that maps each domain and its `www.` variant to `127.0.0.1`.
  - `static string HostsFileEditor.Remove(string currentHosts)` — returns hosts text with the CloveParentalGuard sentinel block removed.

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/HostsFileEditorTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class HostsFileEditorTests
{
    [Fact]
    public void Apply_Adds_Domain_And_Www_Variant()
    {
        var result = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        Assert.Contains("127.0.0.1 poki.com", result);
        Assert.Contains("127.0.0.1 www.poki.com", result);
        Assert.Contains("# BEGIN CloveParentalGuard", result);
        Assert.Contains("# END CloveParentalGuard", result);
    }

    [Fact]
    public void Apply_Preserves_Existing_Content()
    {
        var result = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        Assert.Contains("127.0.0.1 localhost", result);
    }

    [Fact]
    public void Apply_Twice_Does_Not_Duplicate_Block()
    {
        var once = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        var twice = HostsFileEditor.Apply(once, new[] { "crazygames.com" });
        Assert.Single(Occurrences(twice, "# BEGIN CloveParentalGuard"));
        Assert.Contains("crazygames.com", twice);
        Assert.DoesNotContain("poki.com", twice);
    }

    [Fact]
    public void Remove_Strips_Block_And_Keeps_Rest()
    {
        var applied = HostsFileEditor.Apply("127.0.0.1 localhost\n", new[] { "poki.com" });
        var removed = HostsFileEditor.Remove(applied);
        Assert.DoesNotContain("# BEGIN CloveParentalGuard", removed);
        Assert.DoesNotContain("poki.com", removed);
        Assert.Contains("127.0.0.1 localhost", removed);
    }

    private static IEnumerable<int> Occurrences(string text, string token)
    {
        int i = 0;
        while ((i = text.IndexOf(token, i, StringComparison.Ordinal)) != -1) { yield return i; i += token.Length; }
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter HostsFileEditorTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/HostsFileEditor.cs`:
```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace CloveParentalGuard.Core;

public static class HostsFileEditor
{
    private const string Begin = "# BEGIN CloveParentalGuard";
    private const string End = "# END CloveParentalGuard";

    public static string Apply(string currentHosts, IEnumerable<string> domains)
    {
        var cleaned = Remove(currentHosts).TrimEnd('\r', '\n');
        var sb = new StringBuilder();
        if (cleaned.Length > 0) sb.Append(cleaned).Append('\n');
        sb.Append(Begin).Append('\n');
        foreach (var domain in domains)
        {
            var d = domain.Trim();
            if (d.Length == 0) continue;
            sb.Append("127.0.0.1 ").Append(d).Append('\n');
            sb.Append("127.0.0.1 www.").Append(d).Append('\n');
        }
        sb.Append(End).Append('\n');
        return sb.ToString();
    }

    public static string Remove(string currentHosts)
    {
        var pattern = Regex.Escape(Begin) + ".*?" + Regex.Escape(End) + @"\r?\n?";
        var result = Regex.Replace(currentHosts, pattern, "", RegexOptions.Singleline);
        return result;
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter HostsFileEditorTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/HostsFileEditor.cs tests/CloveParentalGuard.Core.Tests/HostsFileEditorTests.cs
git commit -m "feat: add hosts-file editor"
```

---

### Task 6: Lock/timer state machine

**Files:**
- Create: `src/CloveParentalGuard.Core/LockManager.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/LockManagerTests.cs`

**Interfaces:**
- Produces:
  - `class LockManager(Func<DateTimeOffset> clock)`
  - `bool IsLocked { get; }` — true unless an unlock is active and not expired.
  - `TimeSpan? Remaining { get; }` — time left while unlocked, else null.
  - `void Unlock(TimeSpan duration)`
  - `void Lock()`

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/LockManagerTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class LockManagerTests
{
    [Fact]
    public void Defaults_To_Locked()
    {
        var lm = new LockManager(() => DateTimeOffset.UnixEpoch);
        Assert.True(lm.IsLocked);
        Assert.Null(lm.Remaining);
    }

    [Fact]
    public void Unlock_Makes_It_Unlocked_Until_Expiry()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromHours(1));
        Assert.False(lm.IsLocked);
        Assert.Equal(TimeSpan.FromHours(1), lm.Remaining);
    }

    [Fact]
    public void Relocks_After_Expiry()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromMinutes(30));
        now = now.AddMinutes(31);
        Assert.True(lm.IsLocked);
        Assert.Null(lm.Remaining);
    }

    [Fact]
    public void Lock_Forces_Locked_Immediately()
    {
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromHours(2));
        lm.Lock();
        Assert.True(lm.IsLocked);
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter LockManagerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/LockManager.cs`:
```csharp
namespace CloveParentalGuard.Core;

public class LockManager
{
    private readonly Func<DateTimeOffset> _clock;
    private DateTimeOffset? _unlockedUntil;

    public LockManager(Func<DateTimeOffset> clock) => _clock = clock;

    public bool IsLocked => Remaining is null;

    public TimeSpan? Remaining
    {
        get
        {
            if (_unlockedUntil is null) return null;
            var left = _unlockedUntil.Value - _clock();
            return left > TimeSpan.Zero ? left : null;
        }
    }

    public void Unlock(TimeSpan duration) => _unlockedUntil = _clock() + duration;

    public void Lock() => _unlockedUntil = null;
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter LockManagerTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/LockManager.cs tests/CloveParentalGuard.Core.Tests/LockManagerTests.cs
git commit -m "feat: add lock/timer state machine"
```

---

### Task 7: IPC contract + protocol

**Files:**
- Create: `src/CloveParentalGuard.Core/PipeMessages.cs`
- Create: `src/CloveParentalGuard.Core/PipeProtocol.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/PipeProtocolTests.cs`

**Interfaces:**
- Produces:
  - `record PipeRequest(string Type, string? Code = null, int DurationMinutes = 0)` where `Type` is `"status"` or `"unlock"`.
  - `record PipeResponse(bool Success, bool IsLocked, int RemainingSeconds, string? Error = null)`
  - `static string PipeProtocol.SerializeRequest(PipeRequest r)` / `static PipeRequest PipeProtocol.DeserializeRequest(string s)`
  - `static string PipeProtocol.SerializeResponse(PipeResponse r)` / `static PipeResponse PipeProtocol.DeserializeResponse(string s)`

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/PipeProtocolTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class PipeProtocolTests
{
    [Fact]
    public void Request_Roundtrips()
    {
        var r = new PipeRequest("unlock", "code1", 60);
        var back = PipeProtocol.DeserializeRequest(PipeProtocol.SerializeRequest(r));
        Assert.Equal("unlock", back.Type);
        Assert.Equal("code1", back.Code);
        Assert.Equal(60, back.DurationMinutes);
    }

    [Fact]
    public void Response_Roundtrips()
    {
        var r = new PipeResponse(true, false, 3600, null);
        var back = PipeProtocol.DeserializeResponse(PipeProtocol.SerializeResponse(r));
        Assert.True(back.Success);
        Assert.False(back.IsLocked);
        Assert.Equal(3600, back.RemainingSeconds);
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter PipeProtocolTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/PipeMessages.cs`:
```csharp
namespace CloveParentalGuard.Core;

public record PipeRequest(string Type, string? Code = null, int DurationMinutes = 0);

public record PipeResponse(bool Success, bool IsLocked, int RemainingSeconds, string? Error = null);
```

`src/CloveParentalGuard.Core/PipeProtocol.cs`:
```csharp
using System.Text.Json;

namespace CloveParentalGuard.Core;

public static class PipeProtocol
{
    public static string SerializeRequest(PipeRequest r) => JsonSerializer.Serialize(r);
    public static PipeRequest DeserializeRequest(string s) =>
        JsonSerializer.Deserialize<PipeRequest>(s) ?? throw new FormatException("bad request");

    public static string SerializeResponse(PipeResponse r) => JsonSerializer.Serialize(r);
    public static PipeResponse DeserializeResponse(string s) =>
        JsonSerializer.Deserialize<PipeResponse>(s) ?? throw new FormatException("bad response");
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter PipeProtocolTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/PipeMessages.cs src/CloveParentalGuard.Core/PipeProtocol.cs tests/CloveParentalGuard.Core.Tests/PipeProtocolTests.cs
git commit -m "feat: add IPC message contract and protocol"
```

---

### Task 8: Unlock handler (trust boundary)

**Files:**
- Create: `src/CloveParentalGuard.Core/UnlockHandler.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/UnlockHandlerTests.cs`

**Interfaces:**
- Consumes: `LockManager` (Task 6), `CloveParentalGuardConfig` + `CodeHasher` (Tasks 2-3), `PipeRequest`/`PipeResponse` (Task 7).
- Produces:
  - `class UnlockHandler(LockManager lockManager, Func<CloveParentalGuardConfig> configProvider)`
  - `PipeResponse Handle(PipeRequest request)` — for `"status"`, returns current lock state; for `"unlock"`, verifies code against config and, on success, unlocks for the requested duration **only if** the duration is in `config.DurationsMinutes`. Wrong code or invalid duration → `Success=false` with `Error`.

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/UnlockHandlerTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class UnlockHandlerTests
{
    private static (UnlockHandler handler, LockManager lm) Build(DateTimeOffset now)
    {
        var cfg = CloveParentalGuardConfig.Default();
        cfg.Code = CodeHasher.Hash("parent");
        var lm = new LockManager(() => now);
        return (new UnlockHandler(lm, () => cfg), lm);
    }

    [Fact]
    public void Status_Reports_Locked_By_Default()
    {
        var (handler, _) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("status"));
        Assert.True(resp.Success);
        Assert.True(resp.IsLocked);
    }

    [Fact]
    public void Correct_Code_And_Valid_Duration_Unlocks()
    {
        var (handler, lm) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 60));
        Assert.True(resp.Success);
        Assert.False(resp.IsLocked);
        Assert.False(lm.IsLocked);
        Assert.Equal(3600, resp.RemainingSeconds);
    }

    [Fact]
    public void Wrong_Code_Does_Not_Unlock()
    {
        var (handler, lm) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("unlock", "nope", 60));
        Assert.False(resp.Success);
        Assert.True(lm.IsLocked);
        Assert.NotNull(resp.Error);
    }

    [Fact]
    public void Invalid_Duration_Rejected()
    {
        var (handler, lm) = Build(DateTimeOffset.UnixEpoch);
        var resp = handler.Handle(new PipeRequest("unlock", "parent", 999));
        Assert.False(resp.Success);
        Assert.True(lm.IsLocked);
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter UnlockHandlerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/UnlockHandler.cs`:
```csharp
namespace CloveParentalGuard.Core;

public class UnlockHandler
{
    private readonly LockManager _lockManager;
    private readonly Func<CloveParentalGuardConfig> _configProvider;

    public UnlockHandler(LockManager lockManager, Func<CloveParentalGuardConfig> configProvider)
    {
        _lockManager = lockManager;
        _configProvider = configProvider;
    }

    public PipeResponse Handle(PipeRequest request)
    {
        if (request.Type == "unlock")
        {
            var cfg = _configProvider();
            if (cfg.Code is null)
                return Fail("No parent code is configured.");
            if (string.IsNullOrEmpty(request.Code) || !CodeHasher.Verify(request.Code, cfg.Code))
                return Fail("Incorrect code.");
            if (!cfg.DurationsMinutes.Contains(request.DurationMinutes))
                return Fail("Invalid duration.");

            _lockManager.Unlock(TimeSpan.FromMinutes(request.DurationMinutes));
        }
        return Status();
    }

    private PipeResponse Status()
    {
        var remaining = (int)(_lockManager.Remaining?.TotalSeconds ?? 0);
        return new PipeResponse(true, _lockManager.IsLocked, remaining);
    }

    private PipeResponse Fail(string error) =>
        new(false, _lockManager.IsLocked, (int)(_lockManager.Remaining?.TotalSeconds ?? 0), error);
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter UnlockHandlerTests`
Expected: PASS, 4 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/UnlockHandler.cs tests/CloveParentalGuard.Core.Tests/UnlockHandlerTests.cs
git commit -m "feat: add unlock handler trust boundary"
```

---

### Task 9: Process scan decision logic

**Files:**
- Create: `src/CloveParentalGuard.Core/ProcessInfo.cs`
- Create: `src/CloveParentalGuard.Core/IProcessProvider.cs`
- Create: `src/CloveParentalGuard.Core/ProcessScanner.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/ProcessScannerTests.cs`

**Interfaces:**
- Consumes: `BlocklistMatcher` (Task 4), `LockManager` (Task 6), `CloveParentalGuardConfig` (Task 3).
- Produces:
  - `record ProcessInfo(int Pid, string Name, string? Path)`
  - `interface IProcessProvider { IEnumerable<ProcessInfo> GetProcesses(); }`
  - `interface IProcessKiller { void Kill(int pid); }`
  - `class ProcessScanner(LockManager lockManager, Func<CloveParentalGuardConfig> configProvider, IProcessProvider provider, IProcessKiller killer)`
  - `IReadOnlyList<ProcessInfo> ScanAndEnforce()` — when locked, kills every blocked process and returns the list it killed; when unlocked, kills nothing and returns empty.

- [ ] **Step 1: Write failing tests**

`tests/CloveParentalGuard.Core.Tests/ProcessScannerTests.cs`:
```csharp
namespace CloveParentalGuard.Core.Tests;

public class ProcessScannerTests
{
    private class FakeProvider : IProcessProvider
    {
        public List<ProcessInfo> Processes = new();
        public IEnumerable<ProcessInfo> GetProcesses() => Processes;
    }
    private class FakeKiller : IProcessKiller
    {
        public List<int> Killed = new();
        public void Kill(int pid) => Killed.Add(pid);
    }

    private static CloveParentalGuardConfig Cfg() => new() { BlockedExecutables = new() { "steam.exe" } };

    [Fact]
    public void Kills_Blocked_Process_When_Locked()
    {
        var provider = new FakeProvider { Processes = { new ProcessInfo(10, "steam.exe", null), new ProcessInfo(11, "notepad.exe", null) } };
        var killer = new FakeKiller();
        var lm = new LockManager(() => DateTimeOffset.UnixEpoch); // locked
        var scanner = new ProcessScanner(lm, Cfg, provider, killer);

        var killed = scanner.ScanAndEnforce();

        Assert.Equal(new[] { 10 }, killer.Killed);
        Assert.Single(killed);
        Assert.Equal("steam.exe", killed[0].Name);
    }

    [Fact]
    public void Kills_Nothing_When_Unlocked()
    {
        var provider = new FakeProvider { Processes = { new ProcessInfo(10, "steam.exe", null) } };
        var killer = new FakeKiller();
        var now = DateTimeOffset.UnixEpoch;
        var lm = new LockManager(() => now);
        lm.Unlock(TimeSpan.FromHours(1));
        var scanner = new ProcessScanner(lm, Cfg, provider, killer);

        var killed = scanner.ScanAndEnforce();

        Assert.Empty(killer.Killed);
        Assert.Empty(killed);
    }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test --filter ProcessScannerTests`
Expected: FAIL.

- [ ] **Step 3: Implement**

`src/CloveParentalGuard.Core/ProcessInfo.cs`:
```csharp
namespace CloveParentalGuard.Core;

public record ProcessInfo(int Pid, string Name, string? Path);
```

`src/CloveParentalGuard.Core/IProcessProvider.cs`:
```csharp
namespace CloveParentalGuard.Core;

public interface IProcessProvider { IEnumerable<ProcessInfo> GetProcesses(); }

public interface IProcessKiller { void Kill(int pid); }
```

`src/CloveParentalGuard.Core/ProcessScanner.cs`:
```csharp
namespace CloveParentalGuard.Core;

public class ProcessScanner
{
    private readonly LockManager _lockManager;
    private readonly Func<CloveParentalGuardConfig> _configProvider;
    private readonly IProcessProvider _provider;
    private readonly IProcessKiller _killer;

    public ProcessScanner(LockManager lockManager, Func<CloveParentalGuardConfig> configProvider,
        IProcessProvider provider, IProcessKiller killer)
    {
        _lockManager = lockManager;
        _configProvider = configProvider;
        _provider = provider;
        _killer = killer;
    }

    public IReadOnlyList<ProcessInfo> ScanAndEnforce()
    {
        if (!_lockManager.IsLocked) return Array.Empty<ProcessInfo>();
        var cfg = _configProvider();
        var killed = new List<ProcessInfo>();
        foreach (var p in _provider.GetProcesses())
        {
            if (!BlocklistMatcher.IsBlocked(p.Name, p.Path, cfg)) continue;
            try { _killer.Kill(p.Pid); killed.Add(p); }
            catch { /* process may have exited or be protected; ignore */ }
        }
        return killed;
    }
}
```

- [ ] **Step 4: Run, verify pass**

Run: `dotnet test --filter ProcessScannerTests`
Expected: PASS, 2 tests.

- [ ] **Step 5: Commit**

```bash
git add src/CloveParentalGuard.Core/ProcessInfo.cs src/CloveParentalGuard.Core/IProcessProvider.cs src/CloveParentalGuard.Core/ProcessScanner.cs tests/CloveParentalGuard.Core.Tests/ProcessScannerTests.cs
git commit -m "feat: add process scan/enforce logic"
```

---

### Task 10: Service real adapters (process + hosts file)

**Files:**
- Create: `src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj`
- Create: `src/CloveParentalGuard.Service/SystemProcessProvider.cs`
- Create: `src/CloveParentalGuard.Service/SystemProcessKiller.cs`
- Create: `src/CloveParentalGuard.Service/HostsFileApplier.cs`
- Test: `tests/CloveParentalGuard.Core.Tests/HostsFileApplierTests.cs` (temp-file integration test for the applier; lives in the existing test project)

**Interfaces:**
- Consumes: `IProcessProvider`, `IProcessKiller` (Task 9), `HostsFileEditor` (Task 5), `CloveParentalGuardConfig` (Task 3).
- Produces:
  - `class SystemProcessProvider : IProcessProvider` (enumerates real processes; `Path` is null when inaccessible).
  - `class SystemProcessKiller : IProcessKiller` (`Process.GetProcessById(pid).Kill()`).
  - `class HostsFileApplier(string hostsPath)` with `void ApplyBlock(IEnumerable<string> domains)` and `void RemoveBlock()` that read/transform/write the real file via `HostsFileEditor`.

- [ ] **Step 1: Create the service project + reference Core**

Run:
```bash
dotnet new worker -n CloveParentalGuard.Service -o src/CloveParentalGuard.Service -f net8.0
dotnet sln add src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj
dotnet add src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj reference src/CloveParentalGuard.Core/CloveParentalGuard.Core.csproj
dotnet add src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj package Microsoft.Extensions.Hosting.WindowsServices
```

- [ ] **Step 2: Write failing temp-file test for HostsFileApplier**

`tests/CloveParentalGuard.Core.Tests/HostsFileApplierTests.cs`:
```csharp
using System.IO;
using CloveParentalGuard.Service;

namespace CloveParentalGuard.Core.Tests;

public class HostsFileApplierTests
{
    [Fact]
    public void ApplyBlock_Then_RemoveBlock_Roundtrips_File()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".hosts");
        File.WriteAllText(path, "127.0.0.1 localhost\n");
        var applier = new HostsFileApplier(path);

        applier.ApplyBlock(new[] { "poki.com" });
        var blocked = File.ReadAllText(path);
        Assert.Contains("127.0.0.1 poki.com", blocked);
        Assert.Contains("127.0.0.1 localhost", blocked);

        applier.RemoveBlock();
        var restored = File.ReadAllText(path);
        Assert.DoesNotContain("poki.com", restored);
        Assert.Contains("127.0.0.1 localhost", restored);
        File.Delete(path);
    }
}
```
Add a reference so the test project can see the service types:
```bash
dotnet add tests/CloveParentalGuard.Core.Tests/CloveParentalGuard.Core.Tests.csproj reference src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj
```

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test --filter HostsFileApplierTests`
Expected: FAIL (HostsFileApplier does not exist).

- [ ] **Step 4: Implement adapters**

`src/CloveParentalGuard.Service/HostsFileApplier.cs`:
```csharp
using System.IO;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

public class HostsFileApplier
{
    private readonly string _hostsPath;
    public HostsFileApplier(string hostsPath) => _hostsPath = hostsPath;

    public void ApplyBlock(IEnumerable<string> domains)
    {
        var current = File.Exists(_hostsPath) ? File.ReadAllText(_hostsPath) : "";
        File.WriteAllText(_hostsPath, HostsFileEditor.Apply(current, domains));
    }

    public void RemoveBlock()
    {
        if (!File.Exists(_hostsPath)) return;
        var current = File.ReadAllText(_hostsPath);
        File.WriteAllText(_hostsPath, HostsFileEditor.Remove(current));
    }
}
```

`src/CloveParentalGuard.Service/SystemProcessProvider.cs`:
```csharp
using System.Diagnostics;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

public class SystemProcessProvider : IProcessProvider
{
    public IEnumerable<ProcessInfo> GetProcesses()
    {
        foreach (var p in Process.GetProcesses())
        {
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied / exited */ }
            yield return new ProcessInfo(p.Id, p.ProcessName, path);
        }
    }
}
```

`src/CloveParentalGuard.Service/SystemProcessKiller.cs`:
```csharp
using System.Diagnostics;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

public class SystemProcessKiller : IProcessKiller
{
    public void Kill(int pid)
    {
        var p = Process.GetProcessById(pid);
        p.Kill(entireProcessTree: true);
    }
}
```

- [ ] **Step 5: Run, verify pass**

Run: `dotnet test --filter HostsFileApplierTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/CloveParentalGuard.Service tests/CloveParentalGuard.Core.Tests/HostsFileApplierTests.cs CloveParentalGuard.sln tests/CloveParentalGuard.Core.Tests/CloveParentalGuard.Core.Tests.csproj
git commit -m "feat: add service process/hosts adapters"
```

---

### Task 11: Named-pipe server

**Files:**
- Create: `src/CloveParentalGuard.Service/PipeServer.cs`
- Test: manual (documented at end of step)

**Interfaces:**
- Consumes: `UnlockHandler` (Task 8), `PipeProtocol` (Task 7).
- Produces:
  - `class PipeServer(UnlockHandler handler, string pipeName)` with `Task RunAsync(CancellationToken ct)` — accepts one client connection at a time, reads a UTF-8 line (one JSON request), passes it to the handler, writes back one JSON response line, then loops.
  - Pipe ACL grants read/write to authenticated users so the child's Agent can connect, but the handler still enforces the code.

- [ ] **Step 1: Implement pipe server**

`src/CloveParentalGuard.Service/PipeServer.cs`:
```csharp
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

public class PipeServer
{
    private readonly UnlockHandler _handler;
    private readonly string _pipeName;

    public PipeServer(UnlockHandler handler, string pipeName)
    {
        _handler = handler;
        _pipeName = pipeName;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            using var server = CreateServer();
            try
            {
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server, Encoding.UTF8, false, 1024, leaveOpen: true);
                using var writer = new StreamWriter(server, new UTF8Encoding(false)) { AutoFlush = true };

                var line = await reader.ReadLineAsync(ct);
                if (line is null) continue;

                PipeResponse response;
                try
                {
                    var request = PipeProtocol.DeserializeRequest(line);
                    response = _handler.Handle(request);
                }
                catch (Exception ex)
                {
                    response = new PipeResponse(false, true, 0, "Bad request: " + ex.Message);
                }
                await writer.WriteLineAsync(PipeProtocol.SerializeResponse(response));
            }
            catch (OperationCanceledException) { break; }
            catch { /* drop this client, accept the next */ }
        }
    }

    private NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));

        return NamedPipeServerStreamAcl.Create(
            _pipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous,
            0, 0, security);
    }
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/CloveParentalGuard.Service/PipeServer.cs
git commit -m "feat: add named-pipe server with ACL"
```

Manual test deferred to Task 12 (Worker host), where the pipe is exercised end-to-end with the Agent.

---

### Task 12: Worker host wiring

**Files:**
- Modify: `src/CloveParentalGuard.Service/Worker.cs` (replace template contents)
- Modify: `src/CloveParentalGuard.Service/Program.cs` (replace template contents)
- Create: `src/CloveParentalGuard.Service/Constants.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: a runnable Windows Service that, every 2s, runs `ProcessScanner.ScanAndEnforce()` and synchronizes the hosts file with lock state, while serving the pipe.

- [ ] **Step 1: Add shared constants**

`src/CloveParentalGuard.Service/Constants.cs`:
```csharp
namespace CloveParentalGuard.Service;

public static class Constants
{
    public const string PipeName = "CloveParentalGuardPipe";
    public static readonly string ConfigPath =
        @"C:\ProgramData\CloveParentalGuard\config.json";
    public static readonly string HostsPath =
        Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\drivers\etc\hosts");
    public static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(2);
}
```

- [ ] **Step 2: Implement Worker**

`src/CloveParentalGuard.Service/Worker.cs`:
```csharp
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Service;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly LockManager _lockManager;
    private readonly ProcessScanner _scanner;
    private readonly HostsFileApplier _hosts;
    private readonly PipeServer _pipeServer;
    private bool _hostsBlocked = true; // assume locked → blocked on start

    public Worker(ILogger<Worker> logger)
    {
        _logger = logger;
        _lockManager = new LockManager(() => DateTimeOffset.Now);
        var handler = new UnlockHandler(_lockManager, LoadConfig);
        _scanner = new ProcessScanner(_lockManager, LoadConfig,
            new SystemProcessProvider(), new SystemProcessKiller());
        _hosts = new HostsFileApplier(Constants.HostsPath);
        _pipeServer = new PipeServer(handler, Constants.PipeName);
    }

    private CloveParentalGuardConfig LoadConfig() => ConfigStore.Load(Constants.ConfigPath);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = _pipeServer.RunAsync(stoppingToken);
        SyncHosts(forced: true);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _scanner.ScanAndEnforce();
                SyncHosts(forced: false);
            }
            catch (Exception ex) { _logger.LogError(ex, "scan loop error"); }
            await Task.Delay(Constants.ScanInterval, stoppingToken);
        }
    }

    private void SyncHosts(bool forced)
    {
        var shouldBlock = _lockManager.IsLocked;
        if (!forced && shouldBlock == _hostsBlocked) return;
        try
        {
            if (shouldBlock) _hosts.ApplyBlock(LoadConfig().BlockedDomains);
            else _hosts.RemoveBlock();
            _hostsBlocked = shouldBlock;
        }
        catch (Exception ex) { _logger.LogError(ex, "hosts sync error"); }
    }
}
```

- [ ] **Step 3: Implement Program with Windows Service host**

`src/CloveParentalGuard.Service/Program.cs`:
```csharp
using CloveParentalGuard.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(o => o.ServiceName = "CloveParentalGuard");
builder.Services.AddHostedService<Worker>();
var host = builder.Build();
host.Run();
```

- [ ] **Step 4: Build**

Run: `dotnet build src/CloveParentalGuard.Service/CloveParentalGuard.Service.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Manual end-to-end smoke (elevated)**

Document and run (in an elevated shell):
```powershell
dotnet publish src/CloveParentalGuard.Service -c Release -r win-x64 --self-contained -o publish/service
sc.exe create CloveParentalGuard binPath= "<repo>\publish\service\CloveParentalGuard.Service.exe" start= auto obj= LocalSystem
sc.exe start CloveParentalGuard
```
Expected: service Running; opening Steam is killed within ~2s; `etc\hosts` contains the CloveParentalGuard block. Stop with `sc.exe stop CloveParentalGuard` for now.

- [ ] **Step 6: Commit**

```bash
git add src/CloveParentalGuard.Service/Worker.cs src/CloveParentalGuard.Service/Program.cs src/CloveParentalGuard.Service/Constants.cs
git commit -m "feat: wire CloveParentalGuard worker service"
```

---

### Task 13: Agent tray app (UI over pipe)

**Files:**
- Create: `src/CloveParentalGuard.Agent/CloveParentalGuard.Agent.csproj`
- Create: `src/CloveParentalGuard.Agent/PipeClient.cs`
- Create: `src/CloveParentalGuard.Agent/UnlockForm.cs`
- Create: `src/CloveParentalGuard.Agent/Program.cs`

**Interfaces:**
- Consumes: `PipeProtocol`, `PipeRequest`, `PipeResponse` (Task 7).
- Produces:
  - `class PipeClient(string pipeName)` with `PipeResponse Send(PipeRequest request)` (connects, writes one line, reads one line).
  - `UnlockForm` — code textbox + duration dropdown + Unlock button.
  - Tray icon showing locked/unlocked state and a countdown tooltip; polls status every 5s; opens `UnlockForm` on double-click.

- [ ] **Step 1: Create the agent project**

Run:
```bash
dotnet new winforms -n CloveParentalGuard.Agent -o src/CloveParentalGuard.Agent -f net8.0-windows
dotnet sln add src/CloveParentalGuard.Agent/CloveParentalGuard.Agent.csproj
dotnet add src/CloveParentalGuard.Agent/CloveParentalGuard.Agent.csproj reference src/CloveParentalGuard.Core/CloveParentalGuard.Core.csproj
```
Delete the template `Form1.cs` and `Form1.Designer.cs`.

- [ ] **Step 2: Implement pipe client**

`src/CloveParentalGuard.Agent/PipeClient.cs`:
```csharp
using System.IO;
using System.IO.Pipes;
using System.Text;
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Agent;

public class PipeClient
{
    private readonly string _pipeName;
    public PipeClient(string pipeName) => _pipeName = pipeName;

    public PipeResponse Send(PipeRequest request)
    {
        using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut);
        client.Connect(3000);
        using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        using var reader = new StreamReader(client, Encoding.UTF8);
        writer.WriteLine(PipeProtocol.SerializeRequest(request));
        var line = reader.ReadLine() ?? throw new IOException("no response");
        return PipeProtocol.DeserializeResponse(line);
    }
}
```

- [ ] **Step 3: Implement unlock form**

`src/CloveParentalGuard.Agent/UnlockForm.cs`:
```csharp
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Agent;

public class UnlockForm : Form
{
    private readonly PipeClient _client;
    private readonly TextBox _code = new() { PlaceholderText = "Parent code", Width = 220, UseSystemPasswordChar = true };
    private readonly ComboBox _duration = new() { Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.Firebrick };

    public UnlockForm(PipeClient client, IEnumerable<int> durations)
    {
        _client = client;
        Text = "CloveParentalGuard — Unlock";
        Width = 280; Height = 220; FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen; MaximizeBox = false; MinimizeBox = false;

        foreach (var d in durations) _duration.Items.Add(d);
        if (_duration.Items.Count > 0) _duration.SelectedIndex = 0;

        var info = new Label { Text = "Games are blocked. Enter the parent code to unlock.", AutoSize = false, Width = 240, Height = 40, Left = 16, Top = 12 };
        _code.Left = 16; _code.Top = 56;
        _duration.Left = 16; _duration.Top = 88;
        var unlock = new Button { Text = "Unlock", Left = 16, Top = 120, Width = 220 };
        _status.Left = 16; _status.Top = 154;
        unlock.Click += (_, _) => DoUnlock();

        Controls.AddRange(new Control[] { info, _code, _duration, unlock, _status });
    }

    private void DoUnlock()
    {
        if (_duration.SelectedItem is not int minutes) return;
        try
        {
            var resp = _client.Send(new PipeRequest("unlock", _code.Text, minutes));
            if (resp.Success) { MessageBox.Show($"Unlocked for {minutes} minutes."); Close(); }
            else _status.Text = resp.Error ?? "Unlock failed.";
        }
        catch (Exception ex) { _status.Text = "Service unavailable: " + ex.Message; }
    }
}
```

- [ ] **Step 4: Implement tray program**

`src/CloveParentalGuard.Agent/Program.cs`:
```csharp
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Agent;

static class Program
{
    private const string PipeName = "CloveParentalGuardPipe";

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        var client = new PipeClient(PipeName);
        var durations = new List<int> { 30, 60, 120, 180 };

        var tray = new NotifyIcon
        {
            Icon = SystemIcons.Shield,
            Visible = true,
            Text = "CloveParentalGuard"
        };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Unlock…", null, (_, _) => OpenUnlock(client, durations));
        menu.Items.Add("Exit", null, (_, _) => { tray.Visible = false; Application.Exit(); });
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => OpenUnlock(client, durations);

        var timer = new System.Windows.Forms.Timer { Interval = 5000 };
        timer.Tick += (_, _) =>
        {
            try
            {
                var s = client.Send(new PipeRequest("status"));
                tray.Text = s.IsLocked ? "CloveParentalGuard: LOCKED"
                    : $"CloveParentalGuard: unlocked ({s.RemainingSeconds / 60} min left)";
            }
            catch { tray.Text = "CloveParentalGuard: service offline"; }
        };
        timer.Start();

        Application.Run();
        tray.Dispose();
    }

    private static void OpenUnlock(PipeClient client, List<int> durations)
    {
        using var form = new UnlockForm(client, durations);
        form.ShowDialog();
    }
}
```

- [ ] **Step 5: Build**

Run: `dotnet build src/CloveParentalGuard.Agent/CloveParentalGuard.Agent.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Manual end-to-end test**

With the service running (Task 12) and a code configured (Task 14), run the agent: a tray shield appears, double-click opens the unlock dialog, wrong code shows "Incorrect code", correct code + duration unlocks (Steam now launches; hosts entries cleared); after the timer the tray shows LOCKED again and Steam is killed.

- [ ] **Step 7: Commit**

```bash
git add src/CloveParentalGuard.Agent CloveParentalGuard.sln
git commit -m "feat: add agent tray app and unlock dialog"
```

---

### Task 14: Admin config tool (UAC-elevated)

**Files:**
- Create: `src/CloveParentalGuard.Admin/CloveParentalGuard.Admin.csproj`
- Create: `src/CloveParentalGuard.Admin/app.manifest`
- Create: `src/CloveParentalGuard.Admin/AdminForm.cs`
- Create: `src/CloveParentalGuard.Admin/Program.cs`

**Interfaces:**
- Consumes: `CloveParentalGuardConfig`, `ConfigStore`, `CodeHasher` (Tasks 2-3).
- Produces: a form that loads/saves `Constants.ConfigPath`, sets the parent code (hashed), and edits blocked exes/folders/domains and durations. Manifest forces elevation so a standard user can't run it.

- [ ] **Step 1: Create project + force elevation**

Run:
```bash
dotnet new winforms -n CloveParentalGuard.Admin -o src/CloveParentalGuard.Admin -f net8.0-windows
dotnet sln add src/CloveParentalGuard.Admin/CloveParentalGuard.Admin.csproj
dotnet add src/CloveParentalGuard.Admin/CloveParentalGuard.Admin.csproj reference src/CloveParentalGuard.Core/CloveParentalGuard.Core.csproj
```
Delete the template `Form1.cs` / `Form1.Designer.cs`.

`src/CloveParentalGuard.Admin/app.manifest`:
```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges>
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
```
Add to `src/CloveParentalGuard.Admin/CloveParentalGuard.Admin.csproj` inside `<PropertyGroup>`:
```xml
<ApplicationManifest>app.manifest</ApplicationManifest>
```

- [ ] **Step 2: Implement admin form**

`src/CloveParentalGuard.Admin/AdminForm.cs`:
```csharp
using CloveParentalGuard.Core;

namespace CloveParentalGuard.Admin;

public class AdminForm : Form
{
    private const string ConfigPath = @"C:\ProgramData\CloveParentalGuard\config.json";
    private CloveParentalGuardConfig _config = ConfigStore.Load(ConfigPath);

    private readonly TextBox _code = new() { PlaceholderText = "New parent code (blank = keep)", Width = 360, UseSystemPasswordChar = true };
    private readonly TextBox _exes = new() { Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _folders = new() { Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _domains = new() { Multiline = true, Width = 360, Height = 90, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _durations = new() { Width = 360 };

    public AdminForm()
    {
        Text = "CloveParentalGuard — Admin";
        Width = 410; Height = 560; StartPosition = FormStartPosition.CenterScreen;

        _exes.Text = string.Join(Environment.NewLine, _config.BlockedExecutables);
        _folders.Text = string.Join(Environment.NewLine, _config.BlockedFolders);
        _domains.Text = string.Join(Environment.NewLine, _config.BlockedDomains);
        _durations.Text = string.Join(",", _config.DurationsMinutes);

        int y = 12;
        AddRow("Parent code:", _code, ref y);
        AddRow("Blocked executables (one per line):", _exes, ref y);
        AddRow("Blocked folders (one per line):", _folders, ref y);
        AddRow("Blocked domains (one per line):", _domains, ref y);
        AddRow("Durations (minutes, comma-separated):", _durations, ref y);

        var save = new Button { Text = "Save", Left = 16, Top = y + 8, Width = 360 };
        save.Click += (_, _) => Save();
        Controls.Add(save);
    }

    private void AddRow(string label, Control input, ref int y)
    {
        Controls.Add(new Label { Text = label, Left = 16, Top = y, AutoSize = true });
        input.Left = 16; input.Top = y + 20;
        Controls.Add(input);
        y = input.Top + input.Height + 12;
    }

    private void Save()
    {
        if (!string.IsNullOrWhiteSpace(_code.Text))
            _config.Code = CodeHasher.Hash(_code.Text.Trim());

        _config.BlockedExecutables = Lines(_exes.Text);
        _config.BlockedFolders = Lines(_folders.Text);
        _config.BlockedDomains = Lines(_domains.Text);
        _config.DurationsMinutes = _durations.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => int.TryParse(s, out _)).Select(int.Parse).ToList();

        if (_config.Code is null)
        {
            MessageBox.Show("Set a parent code before saving.");
            return;
        }

        try
        {
            ConfigStore.Save(ConfigPath, _config);
            MessageBox.Show("Saved. Restart the CloveParentalGuard service to apply blocklist changes.");
        }
        catch (Exception ex) { MessageBox.Show("Save failed: " + ex.Message); }
    }

    private static List<string> Lines(string text) =>
        text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}
```

- [ ] **Step 3: Implement program**

`src/CloveParentalGuard.Admin/Program.cs`:
```csharp
namespace CloveParentalGuard.Admin;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new AdminForm());
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/CloveParentalGuard.Admin/CloveParentalGuard.Admin.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Manual test**

Run the built exe; UAC prompts for elevation. Set a code, adjust lists, Save. Confirm `C:\ProgramData\CloveParentalGuard\config.json` is written and contains a hashed code (no plaintext). As a standard user, confirm the tool refuses to run without admin.

- [ ] **Step 6: Commit**

```bash
git add src/CloveParentalGuard.Admin CloveParentalGuard.sln
git commit -m "feat: add UAC-elevated admin config tool"
```

---

### Task 15: Install/uninstall scripts + README

**Files:**
- Create: `install/Install-CloveParentalGuard.ps1`
- Create: `install/Uninstall-CloveParentalGuard.ps1`
- Create: `README.md`

**Interfaces:**
- Consumes: published Service/Agent/Admin binaries.
- Produces: scripts that publish, register the service (auto-start + failure recovery), and set the Agent to run at logon for all users; plus an uninstall that reverses it and strips the hosts block.

- [ ] **Step 1: Install script**

`install/Install-CloveParentalGuard.ps1`:
```powershell
#Requires -RunAsAdministrator
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$pub  = Join-Path $root "publish"

dotnet publish "$root/src/CloveParentalGuard.Service" -c Release -r win-x64 --self-contained -o "$pub/service"
dotnet publish "$root/src/CloveParentalGuard.Agent"   -c Release -r win-x64 --self-contained -o "$pub/agent"
dotnet publish "$root/src/CloveParentalGuard.Admin"   -c Release -r win-x64 --self-contained -o "$pub/admin"

$svcExe = Join-Path $pub "service/CloveParentalGuard.Service.exe"
sc.exe create CloveParentalGuard binPath= "`"$svcExe`"" start= auto obj= LocalSystem DisplayName= "CloveParentalGuard"
# Restart on crash: 1st/2nd/subsequent failures restart after 5s
sc.exe failure CloveParentalGuard reset= 0 actions= restart/5000/restart/5000/restart/5000
sc.exe start CloveParentalGuard

# Agent at logon for all users (runs in the interactive session)
$agentExe = Join-Path $pub "agent/CloveParentalGuard.Agent.exe"
$action  = New-ScheduledTaskAction -Execute $agentExe
$trigger = New-ScheduledTaskTrigger -AtLogOn
$principal = New-ScheduledTaskPrincipal -GroupId "S-1-5-32-545" -RunLevel Limited # BUILTIN\Users
Register-ScheduledTask -TaskName "CloveParentalGuardAgent" -Action $action -Trigger $trigger -Principal $principal -Force

Write-Host "Installed. Run the Admin tool to set the parent code:`n  $($pub)\admin\CloveParentalGuard.Admin.exe"
```

- [ ] **Step 2: Uninstall script**

`install/Uninstall-CloveParentalGuard.ps1`:
```powershell
#Requires -RunAsAdministrator
$ErrorActionPreference = "SilentlyContinue"
sc.exe stop CloveParentalGuard
sc.exe delete CloveParentalGuard
Unregister-ScheduledTask -TaskName "CloveParentalGuardAgent" -Confirm:$false

# Strip the CloveParentalGuard block from hosts
$hosts = "$env:SystemRoot\System32\drivers\etc\hosts"
if (Test-Path $hosts) {
    $content = Get-Content $hosts -Raw
    $cleaned = [System.Text.RegularExpressions.Regex]::Replace(
        $content, "# BEGIN CloveParentalGuard.*?# END CloveParentalGuard\r?\n?", "",
        [System.Text.RegularExpressions.RegexOptions]::Singleline)
    Set-Content -Path $hosts -Value $cleaned -Encoding ascii
}
Write-Host "Uninstalled. Config left at C:\ProgramData\CloveParentalGuard (delete manually if desired)."
```

- [ ] **Step 3: README**

`README.md` — document: what it is, the honest limits (Safe Mode/USB-boot caveat from the spec), build prerequisites (.NET 8 SDK), install steps (run `Install-CloveParentalGuard.ps1` elevated, then set a code in the Admin tool), how unlock works, and how to add new games/domains. Include a one-line summary of each component (Service/Agent/Admin).

- [ ] **Step 4: Full test pass + commit**

Run: `dotnet test`
Expected: all unit tests PASS.
```bash
git add install README.md
git commit -m "feat: add install/uninstall scripts and README"
```

---

## Self-Review

**Spec coverage:**
- Blocks games / launchers → Tasks 4, 9, 10, 12. ✓
- Runs on startup → service auto-start (Task 15) + agent logon task (Task 15). ✓
- Only admin can disable → service runs as SYSTEM, standard user can't stop; Admin tool requires elevation (Tasks 12, 14, 15). ✓
- Show "can't play" message → Agent block/unlock UI (Task 13). ✓
- Code unlocks → UnlockHandler + CodeHasher (Tasks 2, 8). ✓
- Selectable timer (1h/2h) → durations in config + LockManager + unlock dialog (Tasks 3, 6, 13). ✓
- Auto re-block on expiry → LockManager.Remaining + Worker scan loop + hosts sync (Tasks 6, 12). ✓
- Browser games → HostsFileEditor + HostsFileApplier + Worker SyncHosts (Tasks 5, 10, 12). ✓
- PBKDF2, never plaintext → Task 2 + Admin save (Tasks 2, 14). ✓
- Fail-safe locked on restart → Worker default + LockManager default (Tasks 6, 12). ✓
- Known limits documented → README (Task 15). ✓

**Placeholder scan:** No TBD/TODO; every code step contains full code. README (Task 15) is the one prose deliverable, with explicit required contents listed.

**Type consistency:** `HashedCode`, `CloveParentalGuardConfig`, `PipeRequest`/`PipeResponse`, `LockManager`, `UnlockHandler`, `ProcessScanner`, `IProcessProvider`/`IProcessKiller`, `HostsFileApplier` signatures are used identically across producing and consuming tasks. Pipe name `CloveParentalGuardPipe` and config path are centralized (Agent uses literals matching `Constants`; both equal `CloveParentalGuardPipe` / the ProgramData path).
