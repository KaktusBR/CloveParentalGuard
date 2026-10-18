# CloveParentalGuard

A Windows parental game-blocker. It runs as a protected system service, blocks game launchers, games and browser-game sites by default, and lets a parent unlock play for a limited time with a code. When the time is up, everything locks again by itself.

> **Status: early development (v0.1.0).** It works and is in daily use, but expect rough edges and breaking changes between 0.x releases.

> **Built with Claude.** CloveParentalGuard was designed and implemented with
> [Claude Code](https://claude.com/claude-code) (Anthropic) — from brainstorming and the design
> spec through a test-driven, reviewed implementation. See [`docs/design.md`](docs/design.md) and
> [`docs/implementation-plan.md`](docs/implementation-plan.md). Open source under the
> [MIT License](LICENSE) — fork it, change it, ship it.

---

## Features

- **Blocks games and launchers.** Any process on the blocklist is killed within about 2 seconds of starting. Blocking works by program name (`steam.exe`) and by folder (everything running from `C:\Program Files\Epic Games`). A `*` in a folder rule matches one path segment, e.g. `C:\Users\*\AppData\Roaming\.minecraft`.
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
- **Administrator accounts** — any Windows administrator can stop the service (`sc stop CloveParentalGuard`) or edit the `hosts` file. Keep the child on a standard account.
- **Renamed or portable games** — rules match by program name and folder. A game running from an unlisted location (for example a separate Java install used by a third-party Minecraft launcher) needs its own folder rule.

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

### Changing the blocklist, code or durations

Run `CloveParentalGuard.exe --admin` as administrator. You can edit:

- **Parent code** (blank keeps the current one)
- **Blocked executables** — one name per line, e.g. `Minecraft.exe`
- **Blocked folders** — one path per line; `*` matches a single path segment
- **Blocked domains** — one per line, e.g. `poki.com`
- **Durations** — minutes, comma-separated

Executable and folder rules take effect within a couple of seconds. Domain changes reach the `hosts` file the next time the PC locks; restart the **CloveParentalGuard** service to apply them straight away. The settings live in `C:\ProgramData\CloveParentalGuard\config.json`.

### Default blocklist

Steam, Epic Games, Battle.net, GOG Galaxy, Origin/EA app, Riot, Ubisoft Connect and Minecraft (launcher and runtime), plus the game folders for Steam and Epic, and browser-game sites such as poki.com, crazygames.com, miniclip.com, roblox.com and minecraft.net. Edit the list to suit.

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

Source layout: `src/CloveParentalGuard` (app: service, tray, setup, UI), `src/CloveParentalGuard.Core` (testable logic: matching, scanning, lock, unlock, hashing, config), `tests/CloveParentalGuard.Core.Tests`.

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
git tag v0.1.0
git push origin v0.1.0
```

The version number lives in [`Directory.Build.props`](Directory.Build.props); keep it in step with the tag.

---

## License

[MIT](LICENSE) — © 2026 KaktusBR. Free to use, copy, modify and distribute, including commercially. Keep the copyright and license notice. Forks and pull requests welcome.
