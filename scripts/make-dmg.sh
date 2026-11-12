#!/bin/bash
# Packages build/Clicksmith.app into build/Clicksmith-<version>.dmg (drag-to-Applications layout).
#   ./scripts/make-dmg.sh            uses the existing build
#   ./scripts/make-dmg.sh --build    builds first
set -euo pipefail
export PATH="/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
cd "$(dirname "$0")/.."
[ "${1:-}" = "--build" ] && ./scripts/build-app.sh --no-anim

source scripts/lib/build-steps.sh
APP="build/Clicksmith.app"
[ -d "$APP" ] || { echo "❌ $APP not found. Run ./scripts/build-app.sh first."; exit 1; }

DMG="build/Clicksmith-$CS_VERSION.dmg"
STAGE="$(mktemp -d)/Clicksmith"
mkdir -p "$STAGE"
ditto "$APP" "$STAGE/Clicksmith.app"
ln -s /Applications "$STAGE/Applications"

SIGNED="Developer ID"
codesign -dv "$APP" 2>&1 | grep -q "Signature=adhoc" && SIGNED="ad-hoc"
cat > "$STAGE/Read Me First.txt" <<TXT
Clicksmith $CS_VERSION — Forge every mouse button into something useful.

1. Drag Clicksmith into Applications and open it.
2. Follow the welcome window.
3. Allow "Clicksmith Engine" under System Settings > Privacy & Security >
   Input Monitoring and Accessibility.

This build is signed: $SIGNED.
$( [ "$SIGNED" = "ad-hoc" ] && echo "Ad-hoc builds aren't notarized: the first time, right-click Clicksmith > Open > Open.
For builds you share with others, use scripts/release-notarize.sh." )

Local only. No telemetry, no network, no keystroke logging. MIT License.
TXT

rm -f "$DMG"
hdiutil create -volname "Clicksmith $CS_VERSION" -srcfolder "$STAGE" -fs HFS+ -format UDZO -imagekey zlib-level=9 -ov "$DMG" >/dev/null
rm -rf "$(dirname "$STAGE")"

if [ -n "${SIGN_IDENTITY:-}" ] && [ "${SIGN_IDENTITY}" != "-" ]; then
  codesign --force --sign "$SIGN_IDENTITY" --timestamp "$DMG"
fi
hdiutil verify "$DMG" >/dev/null
echo "✅ $DMG ($(du -h "$DMG" | cut -f1), $SIGNED)"
