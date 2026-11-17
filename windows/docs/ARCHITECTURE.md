# Architecture (Windows)

| Process / part | Role | Needs |
|---|---|---|
| `Clicksmith.exe` (WPF) | Window, tray, settings, editor, diagnostics, updates | Nothing special |
| `Clicksmith.Engine.exe` (hidden) | Input and actions | Runs as you; no admin, no driver |
| `Clicksmith.Core.dll` | Model, validation, migrations, `ButtonRouter`, updates | Pure .NET, self-tested |
| `Clicksmith.DpiProbe.exe` | Read-only HID diagnostics | Nothing special |

## Engine threads

| Thread | Does | Never does |
|---|---|---|
| **Input** | Low-level mouse hook, F13–F24 hot keys, long-press timer, `ButtonRouter` | File I/O, logging, process lookups, waiting |
| **Control** | Config (parsed on the thread pool), app in front, requests, emergency shortcut, sleep/unlock recovery, status | Mouse input |
| **Actions** | `SendInput`, open app, type text (with password-field check) | Anything on the input path |
| **File writer / log** | Coalesced atomic writes, log lines | — |

The control thread publishes an immutable `RouterSnapshot` (compiled config, active, app in front, identify/test state); the input thread reads it with one volatile load per event. No locks on the input path.

## Decisions

`ButtonRouter.OnInput(trigger, down, now, snapshot)` returns *swallow / action / result*:
exact app override → default → nothing; repeat guard; a hold action holds the press back until it's clear whether it's a tap or a hold (250–1500 ms); test mode reports and never runs; Find a button reports and swallows the next press. Pause, app/profile switch, emergency and sleep cancel a pending press without firing, and its release is still swallowed.

## Extra buttons

F13–F24 are registered as hot keys (only the keys in use, plus all of them briefly during Find a button). The press arrives as `WM_HOTKEY`; the release is read from the key state on the 15 ms timer, so holds work too.

## Recovery

On resume (after a configurable delay), unlock, and every 15 minutes while idle, the input thread installs a fresh hook and re-registers hot keys; the control thread re-registers the emergency shortcut and re-reads the app in front.

## Settings, IPC, updates

`%APPDATA%\Clicksmith\config.json` (schema 2, profiles; schema 1 migrated). Each save is validated, the previous file goes to `Configuration Backups` (last 20), then an atomic write. The app writes `engine-request.json`; the engine writes `engine-status.json` (heartbeat 10 s) and `engine-result.json`. Files are size-limited; links are refused; no request can trigger an action.

Releases contain `PACKAGE.sha256`; installers verify it, back up the setup, keep `Clicksmith.previous`, and restore it automatically if installing fails. `release-windows.json` lists each package's SHA-256 for the web installer and in-app updates.
