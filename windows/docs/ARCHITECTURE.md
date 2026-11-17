# Architecture (Windows)

| Process | Role | Needs |
|---|---|---|
| `Clicksmith.exe` (WPF) | Window, tray icon, settings, editor, diagnostics | Nothing special |
| `Clicksmith.Engine.exe` (hidden) | Mouse hook, actions, long press, test mode, sleep recovery | Runs as you; no admin, no driver |
| `Clicksmith.Core.dll` | Settings model, validation, migrations, rules, long press, updates | Pure .NET, self-tested |

**Hot path:** `low-level mouse hook → button + down/up → resolved action for the app in front → queue`. The hook callback does no I/O, logging or UI work and returns at once; actions are sent from a separate thread with `SendInput`. Clicksmith's own injected input is marked and ignored.

**Resolution** (`CompiledConfig`) happens on config load and app switch, never per click: exact app override → default → nothing. Switching between apps that have no overrides costs nothing.

**Long press (2.2):** a mapping with a hold action holds back the button's own press. A 15 ms timer runs only while a button is down; the hold action fires once at the threshold (250–1500 ms), otherwise the tap action fires on release. Pausing, switching profile or app, sleep and emergency pause cancel a pending press without firing; its release is still swallowed so no half-click reaches the app.

**Blocking:** side and middle buttons can be blocked; left/right only after an explicit opt-in, which forces the Ctrl+Alt+Shift+F12 emergency pause on. If the engine stops, Windows removes the hook and the mouse works normally again.

**Sleep and unlock:** Windows can silently drop a low-level hook. The engine installs a fresh hook, re-registers the emergency shortcut and re-reads the app in front after resume (configurable delay), after unlock, and every 15 minutes while idle.

**Settings:** `%APPDATA%\Clicksmith\config.json`, schema 2 (profiles). Schema 1 (one implicit profile) is migrated automatically. Every save is validated, the previous file is copied to `Configuration Backups` (last 20), then written atomically. A failed backup aborts the save.

**IPC:** the app writes `config.json` and `engine-request.json` (identify, test mode, resume, reload, quit); the engine watches the folder and writes `engine-status.json` (heartbeat every 10 s) and `engine-result.json`. Files are size-limited and links are refused. No request can trigger an action.

**Updates (2.4):** `update.cmd` pulls, verifies every file against `MANIFEST.sha256`, backs up the setup, moves the current install to `Clicksmith.previous`, installs, waits for the new engine to report in, and puts the previous version back automatically if any of that fails. The in-app check only reads public release metadata from GitHub (tags `windows-v*`) when you ask, or at start-up if enabled.
