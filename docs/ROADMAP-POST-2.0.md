# Post-2.0 exploration: Clicksmith Signals

Status: research only. Not implemented, tested, or promised for any release.

## Goal

Explore optional, discreet lighting signals for selected notifications or events on compatible devices, without affecting the input engine.

## Device limitation

SteelSeries states that the Rival 3 Wireless Gen 2 has scroll-wheel indicators for DPI, battery levels, and pairing, with no customizable lighting configuration in GG. This project does not claim notification-lighting support for that model. Other Rival models must be assessed separately.

Manufacturer reference:
https://support.steelseries.com/hc/en-us/articles/37028766350349-Can-I-change-the-illumination-settings-on-the-Rival-3-Wireless-Gen-2

GameSense is a possible integration route for supported devices, not evidence that a particular mouse supports external lighting control:
https://github.com/SteelSeries/gamesense-sdk

## Platform research

- Windows: investigate the notification listener API, application packaging requirements, and explicit user consent. Do not assume it works in the current app architecture without a prototype.
- macOS: investigate explicit app integrations or user-triggered events. Do not assume access to a general stream of other apps' notifications.

Windows reference:
https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/notification-listener

## Proposed safeguards

- Disabled by default; separate, explicit consent for notification access.
- App allowlist, quiet hours, bounded brightness and duration, and a test button.
- No notification-content logging or remote transmission by default.
- Isolated from button mapping, with graceful failure and no input-engine dependency.
- Preserve normal device status indicators and restore previous lighting where supported.
- No undocumented device-write commands in production without a reviewed protocol and hardware validation.

## Release gates

1. Confirm documented lighting support for an exact model and connection mode.
2. Build a standalone, opt-in hardware prototype.
3. Validate notification permissions and data minimization on each platform.
4. Test disconnect/reconnect, sleep/wake, quiet hours, and failure recovery.
5. Publish a compatibility matrix and measured battery/performance impact.

No dates or feature-parity commitments are made.
