# Beta testing checklist

Please test with the settings app closed as well as open.

1. A mapped button fires once per press.
2. Per-app override activates in the right app and reverts immediately after switching away.
3. Pause and emergency pause stop mappings and button suppression.
4. Sleep the Mac for at least 30 seconds, wake it, wait ~5 seconds, then retest the mapped button.
5. Power the mouse off/on and retest.
6. Run `./scripts/verify.sh 20` while using the trackpad and mouse.
7. Check Overview, Buttons, Apps and Settings in both light and dark mode.

Report Mac model, macOS version, mouse model, connection type and Clicksmith version with any failure.
