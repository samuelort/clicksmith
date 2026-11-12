# Security policy

## Design boundaries

Clicksmith is local-only and intentionally has no networking or telemetry subsystem.

- Only Clicksmith Engine receives Input Monitoring and Accessibility permissions.
- IPC cannot directly request action injection; mapped actions begin with a physical mouse press.
- Open-app actions use bundle identifiers, not arbitrary command lines.
- Text actions are length-limited and skipped while secure input is active.
- Left/right-click blocking is disabled by default. When enabled, the emergency pause hotkey is forced on.
- User data files are owner-private (`0700` directory, `0600` sensitive files).

## Known limits

- A malicious process already running as the same macOS user can generally modify that user's files, including Clicksmith configuration.
- Typed-text snippets are stored locally and are not encrypted. Do not store passwords or secrets as snippets.
- Local ad-hoc builds may cause macOS to treat rebuilt binaries as a new permission identity.
- A checksum manifest detects accidental/partial changes inside a source release; it is not a substitute for downloading releases from a trusted origin or for signed releases.

## Reporting a vulnerability

Please do not open a public issue for an exploitable vulnerability. Use GitHub's private Security Advisory flow for the repository.
