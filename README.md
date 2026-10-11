# GameGuard

A Windows parental game-blocker that runs as a SYSTEM service and is hardened against tampering by standard (child) user accounts.

> **Built with Claude.** GameGuard was designed and implemented end-to-end by
> [Claude](https://claude.com/claude-code) (Anthropic's Claude Code, model Claude Opus 4.8) — from
> brainstorming and the design spec through a test-driven, reviewed implementation.
> See [`docs/design.md`](docs/design.md) and [`docs/implementation-plan.md`](docs/implementation-plan.md).
> Open source under the [MIT License](LICENSE) — fork it, change it, ship it.

## What It Does

GameGuard prevents children from launching configured games, game launchers, or accessing browser-based gaming sites. It:

- Kills blocked processes as soon as they appear (process scanning loop).
- Injects DNS-level blocks into the Windows `hosts` file for browser games and game CDN domains.
- Locks the system in the blocked state by default — even after a reboot the child cannot play until a parent unlocks it.
- Shows the child a tray icon / dialog explaining they cannot play, and allows them to request a timed unlock by entering the parent code.

---

## Honest Limits

GameGuard is designed to stop a casual child, not a determined attacker. Know the following:

- **Safe Mode / Recovery Console** — Windows Safe Mode does not run third-party services. A child who boots into Safe Mode can launch blocked games. Mitigate by setting a BIOS/UEFI password and disabling alternative boot devices.
- **USB-boot / second OS** — Booting from a USB drive or a second OS installation completely bypasses GameGuard (it lives on the primary Windows install). Lock down the BIOS boot order and set a BIOS password to close this gap.
- **Second admin account** — Any Windows administrator can stop the service (`sc stop GameGuard`) or delete the `hosts` entries. GameGuard is not a substitute for limiting which accounts have administrator rights on the machine.

---

## One File, Several Roles

GameGuard ships as a **single self-contained `GameGuard.exe`** (no .NET install needed on the target PC). The same binary behaves differently depending on how it is launched:

| Launch | Role |
|---|---|
| **Double-click** | Setup window — set the parent code and Install / Update / Uninstall. Self-elevates (one UAC prompt) for the privileged steps. |
| *(run as a service)* | SYSTEM enforcement worker — process blocking + `hosts` injection; serves the hardened named pipe (`GameGuardPipe`). |
| `--agent` | Per-session tray app — controller tray icon, status/unlock menu, and a notification when a game is blocked. Started at logon for standard users. |
| `--preview` | Opens a sample blocked-game popup for UI work. It does not contact the service or unlock games. |
| `--admin` | Settings window — edit the PBKDF2-hashed unlock code and the game/folder/domain blocklist in `C:\ProgramData\GameGuard\config.json`. |
| `--install` / `--uninstall` | The elevated install/uninstall steps (invoked by the setup window). |

---

## Installing (for a parent)

You only need the one `GameGuard.exe` file. No .NET install, no scripts.

1. **Double-click `GameGuard.exe`.** A small setup window opens.
2. **Type a parent code** (twice to confirm) and click **Install**.
3. **Approve the one Windows (UAC) prompt.** Admin rights are required *once* to register the protective service — after that the child needs nothing and cannot remove it.
4. Done. Games are blocked immediately, the controller tray icon appears, and protection survives reboots.

To change the code or blocklist later, run `GameGuard.exe` again (the setup window's **Update**), or open the settings window with `GameGuard.exe --admin`.

### Updating to a new version

Download the newer `GameGuard.exe`, run it, and click **Update** (approve the UAC prompt). Setup stops the running service, closes the old tray agent, swaps in the new binary, and restarts everything — your parent code and blocklist are preserved. (Run the *new* download, not the copy already in Program Files, so there's a fresh binary to install.)

---

## Building the EXE (for developers)

Prerequisites: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Windows 10/11 (x64).

```powershell
.\install\Build-GameGuard.ps1
```

This produces a single `.\publish\GameGuard.exe` (self-contained win-x64). Hand that one file to the parent.

---

## How Unlock Works

1. The child clicks the tray icon or the "GameGuard" block notification.
2. An unlock dialog appears asking for the **parent code**.
3. The parent enters the code and selects a duration (e.g. 1 hour, 2 hours).
4. The service verifies the code against the stored PBKDF2 hash, lifts the process block and removes the `hosts` entries for the selected period.
5. When the timer expires the service **automatically re-locks** — the `hosts` block is re-injected and process scanning resumes. No manual re-lock is needed.

### Brute-Force Lockout

After **5 consecutive wrong code attempts** the service enforces an escalating backoff lockout:

- 1st lockout: 30 seconds
- Each subsequent lockout doubles the wait (60 s → 120 s → … capped at **1 hour**)

During a lockout, unlock attempts are rejected immediately without checking the code. **Parents must also wait out any active lockout** — there is no override shortcut. If you forget your code, uninstall and reinstall the service from an admin account.

---

## Adding New Games, Folders, or Domains

Launch the Admin tool (elevated) and edit the blocklist:

- **Executables / paths** — add the full path to the game's `.exe` or the folder containing it. Any process whose image path starts with a blocked folder path will be killed.
- **Domains** — add domain names (e.g. `miniclip.com`, `poki.com`). The service writes `0.0.0.0 <domain>` blocks into the Windows `hosts` file, redirecting DNS lookups to the loopback address so browsers cannot reach the site.

Changes take effect immediately — the service picks up the new config within its next scan cycle (default: 5 seconds).

---

## Uninstallation

Run `GameGuard.exe`, click **Uninstall**, and approve the UAC prompt. (You can run the copy in `C:\Program Files\GameGuard\` or the one you originally downloaded.)

This stops and removes the Windows Service, unregisters the logon scheduled task, strips the `# BEGIN GameGuard … # END GameGuard` block from the `hosts` file, and deletes the config under `C:\ProgramData\GameGuard`. The installed `GameGuard.exe` in Program Files is left behind (a program cannot delete its own running image) — remove that folder manually if you wish.

---

## Running Tests

```powershell
dotnet test
```

All 36 unit tests should pass. The test suite covers code hashing, config serialisation, lock manager state, process scanning, hosts-file editing, pipe protocol, the block-event log, and unlock handler logic.

---

## License

[MIT](LICENSE) — © 2026 KaktusBR. You are free to use, copy, modify, merge, publish,
distribute, sublicense, and sell copies, including commercially. Just keep the copyright
and license notice. Forks and pull requests welcome.

## Authorship

This project was created with [Claude Code](https://claude.com/claude-code) (Anthropic's
Claude Opus 4.8). Claude drove the full workflow — requirements brainstorming, the design
spec, the bite-sized implementation plan, and a test-driven implementation executed and
reviewed task-by-task (including an automated security pass that added the brute-force
lockout and named-pipe hardening). Commits are co-authored accordingly.
