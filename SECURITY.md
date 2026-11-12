# Security policy

Clicksmith is local-only and has no networking or telemetry subsystem.

## Design boundaries

- Only **Clicksmith Engine** holds Input Monitoring and Accessibility. The settings app holds neither.
- No IPC command can trigger an action; every action starts with a physical mouse press.
- **Open app** accepts a bundle identifier only (no paths, URLs or arguments) and only launches apps installed under /Applications, ~/Applications or the system app folders.
- **Type text** is limited to 500 characters without control characters and is skipped while a password field (secure input) is active.
- Blocking left/right click is off by default. When allowed, the ⌃⌥⌘⎋ emergency pause is forced on.
- Button blocking disappears if the engine stops or crashes.
- Configuration and IPC files are read only if they are regular files owned by you and not writable by others; the settings folder is 0700, files are 0600.
- The installer verifies every source file against `MANIFEST.sha256` and rejects extra files before building.

## Known limits

- A program already running as you can edit your files, including Clicksmith's configuration. It still cannot trigger actions without a button press or lock you out.
- Text snippets are stored unencrypted. Don't store passwords in them.
- Local builds are ad-hoc signed, so macOS asks for permissions again after a rebuild. Developer ID signing fixes this for releases.
- The manifest detects changed or extra files in a source download; it does not replace downloading from a trusted place.

## Reporting a vulnerability

Please use GitHub's private Security Advisory flow rather than a public issue.
