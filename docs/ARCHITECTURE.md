# Architecture

Clicksmith deliberately separates configuration/UI from input handling.

## Processes

| Process | Responsibility | macOS permissions |
|---|---|---|
| `Clicksmith.app` | AppKit lifecycle, menu bar, SwiftUI settings, config and diagnostics | none |
| `Clicksmith Engine.app` | HID input, action posting, button suppression, wake recovery | Input Monitoring, Accessibility |

## Input hot path

`IOHID callback → watched-bit rising edge → repeat guard → action`

Shortcut and media-key posting must not perform file I/O, logging, script execution, networking or UI work.

`CompiledConfig` pre-resolves mappings by physical device and frontmost bundle ID. App switches only rebuild watched lists when entering/leaving a profile that has overrides.

## Device policy

All pointing devices can be enumerated for the UI. Input report callbacks are attached to external mice, and to built-in devices only when a mapping/learning operation needs them. This avoids spending CPU on trackpad motion that Clicksmith does not use.

## Per-app profiles

`NSWorkspace.didActivateApplicationNotification` provides the active bundle ID. Override priority is:

1. enabled override for exact bundle ID
2. default action
3. no action

## Wake recovery

Rather than attempting to repair a potentially stale HID stack after sleep, the engine records wake state, waits for the configurable delay, removes suppression and exits. `launchd` KeepAlive starts a fresh process, which creates a new `IOHIDManager` and explicitly enumerates devices.

## IPC

App → engine:
- `config.json`
- `learn-request.json`
- `resume-request.json`
- Darwin notifications as change pings

Engine → app:
- `engine-status.json`
- `learn-result.json`
- Darwin notification pings

Local IPC files live in `~/Library/Application Support/Clicksmith` and are validated for owner and permissions.


## Health model (2.0)

`HealthModel.evaluate` in ClicksmithCore turns engine status, permissions, pause flags and the number of connected mice into one `HealthState`. Priority: stopped → missing permissions → emergency pause → pause → no mouse → ready. The UI (`Health+AppState.swift`) only maps it to wording and colour, so every screen agrees and every rule is self-tested.

## Open-app policy (2.0)

`AppLocationPolicy` allows apps under /Applications, ~/Applications, /System/Applications, /System/Library/CoreServices and the Safari cryptex. The engine checks it before launching.
