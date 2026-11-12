# Contributing

Thanks for helping make Clicksmith calmer and more reliable.

## Engineering rules

1. Keep the input hot path free of logging, disk I/O, scripts, UI work and networking.
2. Do not weaken wake recovery without evidence from real hardware.
3. Add a core check for parser, config validation, mapping resolution, learning or safety changes.
4. Keep AppState writes change-driven; avoid system work from SwiftUI `body`.
5. Preserve the Nordic palette and one-accent visual system.
6. No telemetry or input logging.

## Before a PR

```bash
swift build -c release
"$(swift build -c release --show-bin-path)/ClicksmithSelfTest"
```

On a Mac, also run:

```bash
./scripts/verify.sh 20
```

Describe the mouse model and macOS version used for manual input testing.
