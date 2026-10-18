# CloveParentalGuard — Design

**Date:** 2026-06-28
**Status:** Approved design, pending implementation plan

## Purpose

A Windows application that blocks games on a child's PC. It runs automatically at
startup, cannot be stopped by a standard (non-admin) user, and blocks game
launchers, installed games, and browser-based game sites. When a blocked item is
accessed, the child sees a message explaining games are blocked. Entering the
parent's secret code unlocks gaming for a chosen duration (e.g. 1h, 2h); when the
timer expires, blocking resumes automatically.

## Enforcement model

- **Parent / child PC.** An administrator (parent) installs and configures
  CloveParentalGuard. The child uses a **standard Windows account** and cannot stop,
  pause, or uninstall the service. The parent holds the unlock code.

## Architecture

A Windows service runs in isolated Session 0 and cannot draw UI on the user's
desktop. The system therefore splits into two cooperating components plus an
admin tool.

### 1. CloveParentalGuard Service (the enforcer)
- Runs as **LocalSystem (SYSTEM)**, **auto-start**, with **failure recovery**
  configured to restart automatically if killed.
- Service ACL prevents a standard user from stopping/pausing/uninstalling it.
- Responsibilities:
  - Read configuration (blocklist, hashed code, durations) from
    `C:\ProgramData\CloveParentalGuard\`.
  - Scan running processes on an interval (~2s) and terminate blocked processes
    while locked.
  - Manage the hosts file for browser-game domain blocking.
  - Verify the unlock code and own the unlock **timer** (single source of truth
    for lock state).
  - Expose a **named pipe** for the agent to query state and request unlocks.
- **This is the only security-relevant component.** All trust decisions
  (code verification, lock state, timer) happen here.

### 2. CloveParentalGuard Agent (UI only)
- Small tray application that **auto-starts in the child's user session**
  (per-user Run key or scheduled task at logon).
- Responsibilities:
  - Show the "blocked" popup when the service signals a block event.
  - Show the unlock dialog (code entry + duration picker).
  - Show a tray countdown while unlocked.
- Has **no enforcement power**. It only communicates with the service over the
  named pipe. If the child kills the agent, enforcement continues unaffected and
  the agent respawns.

### 3. CloveParentalGuard Admin (configuration tool)
- A separate desktop tool that **requires UAC elevation** (admin only), so the
  child's standard account cannot run it.
- Responsibilities:
  - Install / uninstall the service.
  - Set or change the parent code.
  - Edit the blocklist (launcher exes, game folders, game domains).
  - Set default / available unlock durations.

## Trust rule

The unlock code is verified **inside the service**, never in the agent. The agent
sends `{enteredCode, chosenDuration}` over the pipe; the service verifies against
a **salted PBKDF2 hash** (plaintext code is never stored or transmitted in
config) and decides whether to unlock.

## Detection / blocking strategy

**Block launchers + game processes + game domains.**

### Process blocking
- Config holds:
  - A list of **launcher/game executable names** (e.g. `steam.exe`,
    `EpicGamesLauncher.exe`, `Battle.net.exe`, `GalaxyClient.exe`,
    `Origin.exe`, `RiotClientServices.exe`).
  - A list of **game install-folder roots** (e.g. `...\steamapps\common`, the
    Epic games folder, etc.).
- On each scan, while **locked**, the service terminates any process whose
  executable name is in the list **or** whose executable path resides under a
  blocked folder root. It signals the agent to show the block message.
- When **locked**, launchers are fully blocked — they will not even open.

### Browser-game blocking (hosts file)
- While **locked**, the service rewrites
  `C:\Windows\System32\drivers\etc\hosts` (admin-writable only) to point a
  curated list of gaming domains (e.g. `poki.com`, `crazygames.com`,
  `miniclip.com`, `roblox.com`, `now.gg`) at `0.0.0.0`.
- While **unlocked**, those entries are removed.
- CloveParentalGuard's hosts edits live between clearly marked sentinel lines
  (`# BEGIN CloveParentalGuard` / `# END CloveParentalGuard`) so the rest of the hosts file is
  preserved.

## Unlock flow

1. Child opens a blocked launcher/game (or game site) → it is killed / blocked →
   agent shows: *"Games are blocked. Enter the parent code to unlock."*
2. Child or parent enters the fixed code and picks a duration (e.g. 1h / 2h /
   custom).
3. Agent sends code + duration to the service over the named pipe.
4. Service verifies the PBKDF2 hash:
   - **Success:** enters unlocked state for the chosen duration, stops killing
     processes, removes the hosts entries, and shows a tray countdown.
   - **Failure:** agent shows "incorrect code". (Optional later: backoff after
     repeated failures.)
5. When the timer expires, the service re-locks: restores hosts entries, kills
   running blocked processes again, and shows a "time's up" message.

**One unlock covers everything** — launchers, installed games, and browser game
sites share the same lock state and timer.

## Data / configuration

- Location: `C:\ProgramData\CloveParentalGuard\` (admin-writable; readable by service).
- `config.json`: blocklist (exe names, folder roots, domains), available
  durations, settings.
- Code stored as `{salt, pbkdf2Hash, iterations}` — never plaintext.
- Lock state / current timer kept in service memory; on service restart it
  defaults to **locked** (fail-safe).

## Tech stack

- **C# / .NET.**
  - Service: .NET Worker Service (Windows Service host).
  - Agent: WPF or WinForms tray app.
  - Admin tool: WPF or WinForms, manifest requiring `requireAdministrator`.
  - IPC: named pipe (`NamedPipeServerStream` / client) with a simple
    request/response message contract.
  - Process control: `System.Diagnostics.Process` / Win32 APIs.
  - Packaged for install (service registration with auto-start + recovery).

## Known limits (honest scope)

- This reliably stops a **normal child**. It is **not** bypass-proof against a
  determined teen with time and physical access:
  - Safe Mode can start the PC without the service.
  - Booting from USB / another OS bypasses everything.
  - A second admin account, a VPN, or DNS-over-HTTPS can defeat user-mode and
    hosts-based blocking.
- **Deferred to a later version (not in v1):** Safe Mode hardening, lockdown of
  alternate accounts, DNS/network-layer filtering (WFP/proxy).
- **Added during implementation (security review):** brute-force lockout on the
  unlock code (escalating backoff after repeated failures), and named-pipe
  hardening (FirstPipeInstance + continuous ownership, per-client read timeout).
- Browser-game and game-launcher coverage is **list-based**; the parent will
  occasionally need to add new game exes, folders, or domains as they appear.

## v1 scope summary

In scope:
- SYSTEM service with auto-start + recovery, unstoppable by standard user.
- Process scanning + termination of launchers/games while locked.
- Hosts-file domain blocking for browser games while locked.
- Agent tray app: block message, unlock dialog (code + duration), countdown.
- Admin tool: install/uninstall, set code, edit blocklist, set durations.
- Fixed parent code (PBKDF2-hashed), single timer covering all blocking.

Also added in v1 (from security review): brute-force lockout on the unlock code
and named-pipe squatting/DoS hardening.

Out of scope (v1): Safe Mode/account hardening, network-layer filtering,
rotating codes, remote management.
