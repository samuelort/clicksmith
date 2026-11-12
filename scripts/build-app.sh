#!/bin/bash
# Builds Clicksmith.app (with the nested "Clicksmith Engine.app") into ./build.
#   ./scripts/build-app.sh                                              ad-hoc signed local build
#   SIGN_IDENTITY="Developer ID Application: …" ./scripts/build-app.sh   release signing
#   --no-anim                                                           skip the forge animation
set -euo pipefail
export PATH="/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
cd "$(dirname "$0")/.."
for a in "$@"; do [ "$a" = "--no-anim" ] && export CLICKSMITH_NO_ANIM=1; done
UI_LOG="$HOME/Library/Logs/Clicksmith-build.log"

source scripts/lib/ui.sh
source scripts/lib/build-steps.sh

UI_TOTAL=$CS_BUILD_STEPS
ui_log_header "build $CS_VERSION"
UI_ANIM=0 ui_banner "Build $CS_VERSION · log: ${UI_LOG/#$HOME/~}"
cs_build_all
ui_finale "Built $CS_APP"
