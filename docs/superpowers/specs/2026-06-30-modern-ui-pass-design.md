# CloveParentalGuard — Modern UI/UX Pass

**Date:** 2026-06-30
**Scope:** UI only. No backend, protocol, or enforcement changes. The 36 existing tests stay green.

## Goal

Replace the default WinForms boxes with an intentional, Apple-leaning design across the three
user-facing surfaces, and add an auto-popping "blocked" window with inline unlock.

## Decisions (locked with user)

1. **Unlock stays global** (all games for X minutes). No per-game enforcement, no checkboxes.
2. **Blocked window auto-pops** when a game is launched, with an inline parent-code + duration to
   unlock on the spot. It is the *same window class* as the tray "Unlock", just constructed in a
   "blocked" mode pre-labeled with the game name. Shown at most once per ~30s, single instance.
3. **Stay in WinForms** with a small custom design system. Keep the standard Win11 title bar
   (rounded corners / themed chrome come free); modernize the content.

## Visual system (`Theme.cs` + styled controls)

Palette: window `#F5F5F7`, surface `#FFFFFF`, text `#1D1D1F`, secondary `#6E6E73`,
accent `#0A84FF`, danger `#FF3B30`, success `#34C759`.

Type: Segoe UI Variable (fallback Segoe UI) — Title ~20pt semibold, Body ~10.5pt, Caption ~9pt.

New controls:
- `PillButton` — custom-painted rounded (~8px) flat button; `Primary` (filled accent, white text)
  and `Secondary` (subtle gray) variants; hover + pressed states; fixed height ~40px.
- `RoundedPanel` — rounded surface used as a card and as the input wrapper (hosts a borderless TextBox).
- `Theme.Apply(form)` helper sets fonts/background.

Layout uses `TableLayoutPanel` / `FlowLayoutPanel` + `AutoScaleMode.Dpi` so nothing clips at high DPI
(the root cause of the cut-off Unlock button).

## Windows

### Shared unlock window (`UnlockForm`, reused)
Two modes via constructor:
- **Unlock mode** (from tray): controller glyph, title "Unlock games".
- **Blocked mode** (auto-pop): red lock glyph, title "<Game> is blocked", subtitle "A parent can unlock below."

Body (both): rounded parent-code field, duration as a row of segmented pills (from status durations,
fallback 30/60/120/180), full-width accent **Unlock** pill. Errors show inline in danger color
(wrong code, lockout text passed through from the service). Success shows "✓ Unlocked for N minutes",
then closes.

### Setup window (`SetupForm`)
~480×440. Controller glyph header, subtitle, two rounded code fields (code + confirm), accent
**Install/Update** pill + secondary **Uninstall**. On success the content **swaps to a success state**:
green check, "CloveParentalGuard is protecting this PC", helper line, **Done** button. Errors/in-progress shown
clearly (larger status text, danger color on failure). Privileged work still runs via the existing
self-elevated `--install` / `--uninstall` relaunch.

### AdminForm
Light pass only: shared font + a `PillButton` Save. Not redesigned.

## Behavior: blocked auto-pop (`TrayApp`)

Existing status poll already reports `BlockSeq` + `RecentBlocks`. On a new block:
- if no unlock window is currently open AND the ~30s cooldown has elapsed, open `UnlockForm` in
  blocked mode for the first fresh game name;
- if a window is already open, skip (it already serves the purpose).
The tray menu (status header, Unlock…, Exit) is unchanged.

## Out of scope / unchanged

Core, Worker, Installer, PipeServer/PipeClient/protocol, LockManager, BlockEventLog, enforcement.

## Verification

Logic unchanged → run `dotnet test` (expect 36 green) and `Build-CloveParentalGuard.ps1`, then exercise the
three windows by running the app. No new unit tests (nothing testable changed).
