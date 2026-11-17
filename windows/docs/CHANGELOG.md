# Changelog (Windows)

## 2.5.0 Beta 1 — private beta prep
- Light mode: copper, sage, amber and grey text darkened to at least 4.75:1 contrast (WCAG AA); dark mode unchanged.
- Skipped actions are visible: a banner on the Overview and a line in “Why isn't my button working?” when text wasn't typed (password field, sign-in prompt, busy app) or an app wasn't opened. The password-field check waits up to 750 ms instead of 400 ms.
- Test mode notes that text is never typed into password fields.
- Beta guide for testers (installation, SmartScreen, Smart App Control, what to test, how to report).

## 2.5.0 Beta 1 — install like an app, feel like one

### Install and update
- **Prebuilt releases** for x64 and ARM64 — no .NET SDK or building needed.
- **One-line terminal install** (`irm … | iex`): downloads the newest release, verifies SHA-256, runs the forge installer.
- **Setup.exe** (per user, no administrator rights) with Nordic wizard artwork, runtime check, setup backup and the previous version kept.
- **In-app update:** Settings › Updates › Download and install — verified package, setup backup, automatic rollback.
- The two installers replace each other cleanly (one Apps & features entry).

### Reliability and performance
- The mouse hook now runs on its **own input thread** that does nothing but decide; file writes, logging, config loading and app lookups moved to other threads, so Windows has no reason to drop the hook.
- All decision logic (taps, holds, test mode, Find a button) moved into a tested `ButtonRouter`; **13 simulated-press tests** cover it.
- App lookups cached per process; status writes coalesced; no drop shadows (smoother scrolling).

### Design and usability
- **Every control in the Nordic style:** buttons, switches, drop-downs, sliders, scroll bars, menus, tooltips, text fields, dialog title bars and the tray menu — in light and dark mode.
- **Real controls** everywhere: keyboard focus ring, Enter/Space, Narrator names; focus and scroll position survive page updates.
- **New mapping starts with “press the button”**, with a list as fallback.
- **Mice card** shows the mice Windows sees; **welcome guide** on first start; **real app icons**; editor sizes to the screen.

### DPI and extra buttons
- **Extra buttons (F13–F24):** turns a key sent by the mouse's own software into a full button, with app rules and hold actions.
- **DPI probe** (read-only) in Settings › Diagnostics checks whether a mouse exposes a readable vendor channel.

### Security
- **Open app** no longer allows the Windows folder and refuses shells and script hosts (cmd, PowerShell, wscript, mshta, rundll32 …) everywhere.
- **Type text** is skipped when a password field has focus (UI Automation), not only on sign-in prompts.

### Tests
- 99 self-tests (up from 69).

## 2.4.0 Beta 1 — first Windows build
Core remapping, per-app actions, hold actions, profiles, test mode, backups, safe source updates, forge installer.
