#!/bin/bash
# Public release: Developer ID signing → DMG → Apple notarization → stapling → Gatekeeper check.
# Needs an Apple Developer Program membership. Never commit credentials.
#
# One-time setup:
#   xcrun notarytool store-credentials clicksmith-notary --apple-id you@example.com --team-id TEAMID
# Each release:
#   export SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
#   export NOTARY_PROFILE=clicksmith-notary
#   ./scripts/release-notarize.sh
set -euo pipefail
export PATH="/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
cd "$(dirname "$0")/.."
: "${SIGN_IDENTITY:?Set SIGN_IDENTITY to your Developer ID Application identity}"
: "${NOTARY_PROFILE:?Set NOTARY_PROFILE (see the comment at the top)}"
source scripts/lib/build-steps.sh

SIGN_IDENTITY="$SIGN_IDENTITY" ./scripts/build-app.sh --no-anim
./scripts/make-dmg.sh
DMG="build/Clicksmith-$CS_VERSION.dmg"

echo "==> Notarizing (this can take a few minutes)"
xcrun notarytool submit "$DMG" --keychain-profile "$NOTARY_PROFILE" --wait
echo "==> Stapling"
xcrun stapler staple "$DMG"
xcrun stapler validate "$DMG"
echo "==> Gatekeeper"
spctl --assess --type open --context context:primary-signature --verbose "$DMG"
shasum -a 256 "$DMG" | tee "$DMG.sha256"
echo "✅ Notarized release: $DMG"
