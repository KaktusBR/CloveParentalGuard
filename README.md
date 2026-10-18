# CloveParentalGuard

A Windows parental game-blocker. It runs as a protected system service, blocks game launchers, games and browser-game sites by default, and lets a parent unlock play for a limited time with a code. When the time is up, everything locks again by itself.

> **Status: early development (v0.2.0).** It works and is in daily use, but expect rough edges and breaking changes between 0.x releases.

> **Built with Claude.** CloveParentalGuard was designed and implemented with
> [Claude Code](https://claude.com/claude-code) (Anthropic) — from brainstorming and the design
> spec through a test-driven, reviewed implementation. See [`docs/design.md`](docs/design.md) and
> [`docs/implementation-plan.md`](docs/implementation-plan.md). Open source under the
> [MIT License](LICENSE) — fork it, change it, ship it.

---

## Features

- **Blocks games and launchers.** Any process on the blocklist is killed within about 2 seconds of starting. Blocking works by program name (`steam.exe`) and by folder (everything running from `C:\Program Files\Epic Games`). A `*` in a folder rule matches one path segment, e.g. `C:\Users\*\AppData\Roaming\.minecraft`.
- **Blocks Minecraft whichever launcher starts it.** Besides listing known launchers (official, Modrinth, Prism, MultiMC, ATLauncher, TLauncher, CurseForge and more), the service recognises the Minecraft game itself by its Java command line (vanilla, Forge, NeoForge, Fabric, Quilt), so a launcher it has never heard of is still stopped.
- **Blocklist updates without a new version.** The list of launchers, game folders, sites and Minecraft patterns lives in [`blocklist.json`](blocklist.json) in this repo. The service downloads it at startup and every 6 hours, so new launchers and sites are covered without reinstalling. It is cryptographically signed, so only the maintainer can change what installed copies block (see [The blocklist](#the-blocklist)).
- **Blocks browser games.** While locked, the blocklist domains are written to the Windows `hosts` file and removed again on unlock.
- **Locked by default.** Games stay blocked after a reboot or service restart until a parent unlocks them.
- **Timed unlock with a parent code.** A parent enters the code and picks a duration (default 30 min, 1 h, 2 h or 3 h; configurable). The code is stored only as a salted PBKDF2 hash.
- **Brute-force protection.** After 5 wrong codes, unlocking is refused for 30 seconds, doubling with each further failure up to 1 hour.
- **Friendly for the child.** A tray icon shows the status. When a game is blocked, a window pops up explaining why and offering the unlock prompt.
- **Countdown and warnings.** Click the tray icon for a live `mm:ss` countdown. Windows notifications appear when games are unlocked, at 10, 5 and 1 minute before time runs out, and when time is up.
- **Tamper-resistant.** The service runs as SYSTEM and the program lives in `Program Files`, so a child on a standard account cannot stop, edit or delete it.
- **Clean install and uninstall.** One self-contained exe, no .NET install needed. It appears in Windows **Installed apps**, and uninstalling removes the service, scheduled task, `hosts` entries, config and install folder.

## Honest limits

CloveParentalGuard is built to stop a casual child, not a determined attacker.

- **Safe Mode / recovery** — Windows Safe Mode does not run third-party services. Set a BIOS/UEFI password and restrict boot devices.
- **USB boot / second OS** — booting another OS bypasses it completely. Lock down the BIOS boot order.
- **Internet needed for list updates** — without it the last downloaded (or built-in) list keeps working; only new additions wait.
- **Administrator accounts** — any Windows administrator can stop the service (`sc stop CloveParentalGuard`) or edit the `hosts` file. Keep the child on a standard account.
- **Renamed or portable games** — other games are matched by program name and folder, so a game running from an unlisted location needs its own folder rule. Minecraft is also matched by Java command line, but only for `java.exe` / `javaw.exe`; a launcher that ships its Java under another file name would need a rule.

---

## Install (for a parent)

1. Download `CloveParentalGuard.exe` from the [latest release](../../releases/latest).
2. Double-click it. A small setup window opens.
3. Type a **parent code** twice and click **Install**.
4. Approve the Windows (UAC) prompt. Admin rights are needed only once, to register the service.

Games are blocked immediately, the tray icon appears, and protection starts automatically at every boot and logon. The downloaded file is copied to `C:\Program Files\CloveParentalGuard\`; you can delete the download afterwards.

### Update

Download the newer exe, run it and click **Update**. Your parent code and blocklist are kept. Leave the code fields blank to keep the current code.

### Uninstall

Either run the exe and click **Uninstall**, or use **Settings → Apps → Installed apps → CloveParentalGuard**. Both ask for the Windows admin prompt. This removes the service, scheduled task, `hosts` entries, config (`C:\ProgramData\CloveParentalGuard`) and the install folder. If the install folder is still in use it is deleted a few seconds later; close any open CloveParentalGuard window if it lingers.

> **Upgrading from the old "GameGuard" name?** Uninstall using the old `GameGuard.exe` first, then install the new one. The name change means the new version does not take over the old service.

---

## Using it

### Unlocking

1. Click the tray icon (or the popup that appears when a game is blocked) and choose **Unlock…**.
2. Enter the parent code and pick a duration.
3. The service lifts the block. When time runs out it locks again on its own; nothing to do.

A wrong code is rejected and counted toward the lockout described above. If the code is lost, uninstall and reinstall from an administrator account.

### Tray icon

| Action | Result |
|---|---|
| Click the icon | Menu with the status and a live countdown (`unlocked, 42:07 left`) |
| Menu → **Unlock…** | Opens the unlock window |
| Hover | Tooltip with the time left and when it ends |

### Changing your settings

Run `CloveParentalGuard.exe --admin` as administrator. You can:

- See which blocklist is active, **Fetch latest** now, or **Reset to built-in**
- Change the **parent code** (blank keeps the current one)
- Add **extra blocked** programs (`Game.exe`), folders (`*` matches one path segment) and sites (`example.com`). These are added on top of the online list.
- Add an **allow list**: anything listed there is never blocked, even if the online list says so
- Change the unlock **durations** (minutes, comma-separated)

Changes apply within a few seconds, including sites. Settings live in `C:\ProgramData\CloveParentalGuard\config.json`, which only administrators can read.

---

## The blocklist

What gets blocked is the combination of three lists:

1. **The online list**: [`blocklist.json`](blocklist.json) in this repo (Steam, Epic, Battle.net, GOG, EA, Riot, Ubisoft, Amazon Games, Minecraft and its many launchers, browser-game sites, and so on).
2. **The built-in list**: the same file as it was when your version was built. It is used on first install without internet and whenever a download fails.
3. **Your extras** from `--admin`, minus your allow list.

The service uses whichever of the built-in and downloaded lists is newer. Updating `blocklist.json` never creates a new app version.

### Why it is safe to download a list

The list is public, but nobody except the maintainer can change what your PC blocks:

- **Signed.** Every list is signed with the maintainer's private key (ECDSA P-256). The app contains only the matching *public* keys and ignores any list whose signature does not verify, so a hijacked GitHub account or repository still cannot push entries. The private key never goes to GitHub.
- **No rollbacks.** A validly signed but *older* list is rejected.
- **Strictly checked.** Even a correctly signed list is rejected whole if any entry is unsafe: no core Windows processes or browsers, no system folders, no domains that Windows, Microsoft or GitHub depend on, no over-broad wildcards, and size limits. The scanner also refuses at run time to kill anything inside `C:\Windows` or this program's own folder.
- **Verified again on use.** The cached copy is re-verified every time it is loaded, and the data folder is writable only by SYSTEM and administrators.
- **Key rotation.** The app trusts two keys, a primary and an offline backup, so a lost or compromised key can be replaced in a normal release.
- **Fails safe.** No internet, bad signature or bad file: the last good list (or the built-in one) stays in force.

### Updating the list (maintainer)

The private keys live in `%USERPROFILE%\.cloveparentalguard\`, encrypted with a passphrase, and are never committed (`*.pem` is git-ignored).

```powershell
# one-time: create the two keys (the tool asks for a passphrase and adds the public key to the app)
dotnet run --project tools/BlocklistTool -- keygen primary
dotnet run --project tools/BlocklistTool -- keygen backup    # keep this one offline (USB stick in a drawer)
```

Release a new build once after that, so the public keys are built into the app.

To change what is blocked:

1. Edit [`blocklist.json`](blocklist.json) (add a program, folder, site or Java command-line pattern).
2. Sign it. This checks the entries with the same rules as the app, raises the version, and writes `blocklist.sig`:
   ```powershell
   dotnet run --project tools/BlocklistTool -- sign
   ```
3. Commit **both** `blocklist.json` and `blocklist.sig`, and push to `main`. Installed copies pick it up within hours, or immediately with **Fetch latest**.

`dotnet run --project tools/BlocklistTool -- verify` checks the committed pair. Do not edit either file by hand after signing (a changed byte makes the signature fail), and keep `.gitattributes` as it is so git never rewrites line endings. To use the backup key: `sign --key backup`.

Also turn on two-factor authentication for the GitHub account and protect the `main` branch. The signature is the real protection, but there is no reason to make an attack easier.

---

## How it works

One self-contained exe plays several roles depending on how it is started:

| Launch | Role |
|---|---|
| Double-click | Setup window: install, update or uninstall (self-elevates once) |
| Run by Windows as a service | SYSTEM worker: scans and kills blocked processes, manages the `hosts` block, answers the unlock requests |
| `--agent` | Per-user tray app: status, countdown, notifications, unlock window. Started at logon by a scheduled task |
| `--admin` | Settings window for the code, blocklist and durations |
| `--preview` | Shows a sample blocked-game popup (UI work; does not unlock anything) |
| `--install` / `--uninstall` | The elevated install steps, run by the setup window |

The tray app talks to the service over a locked-down named pipe (`CloveParentalGuardPipe`). Unlock state is held in the service's memory only, which is why a reboot always starts locked.

Source layout: `src/CloveParentalGuard` (app: service, tray, setup, UI), `src/CloveParentalGuard.Core` (testable logic: matching, scanning, lock, unlock, hashing, config, blocklist signing and validation), `tools/BlocklistTool` (maintainer tool for keys and signing), `tests/CloveParentalGuard.Core.Tests`.

---

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Windows 10/11 x64.

```powershell
.\install\Build-CloveParentalGuard.ps1   # produces .\publish\CloveParentalGuard.exe
dotnet test                              # runs the unit tests
```

Close any running copy of the exe in `publish\` before rebuilding, or the old file cannot be replaced.

### Releases

Releases are built by GitHub Actions. Pushing a version tag builds the exe and attaches it to a release:

```powershell
git tag v0.2.0
git push origin v0.2.0
```

The version number lives in [`Directory.Build.props`](Directory.Build.props); keep it in step with the tag.

---

## License

[MIT](LICENSE) — © 2026 KaktusBR. Free to use, copy, modify and distribute, including commercially. Keep the copyright and license notice. Forks and pull requests welcome.
