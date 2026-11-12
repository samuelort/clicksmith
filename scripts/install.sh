#!/bin/bash
# Verifies, builds, installs Clicksmith to /Applications and opens it.
#   ./scripts/install.sh            with the forge animation
#   ./scripts/install.sh --no-anim  plain and quick
#   ./scripts/install.sh --dev      skip the release check (for code you changed yourself)
set -euo pipefail
export PATH="/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
cd "$(dirname "$0")/.."
DEV=0
for a in "$@"; do
  [ "$a" = "--no-anim" ] && export CLICKSMITH_NO_ANIM=1
  [ "$a" = "--dev" ] && DEV=1
done
UI_LOG="$HOME/Library/Logs/Clicksmith-install.log"

source scripts/lib/ui.sh
source scripts/lib/build-steps.sh

DEST_DIR="/Applications"
[ -w "$DEST_DIR" ] || DEST_DIR="$HOME/Applications"
DEST="$DEST_DIR/Clicksmith.app"

cs_stop_app() {
  if pgrep -x Clicksmith >/dev/null; then pkill -x Clicksmith; sleep 0.6; echo "stopped running app"; else echo "app was not running"; fi
}
cs_copy_app() {
  mkdir -p "$DEST_DIR" || return 1
  rm -rf "$DEST" || return 1
  ditto build/Clicksmith.app "$DEST" || return 1
}
cs_restart_engine() {
  if launchctl print "gui/$(id -u)/app.clicksmith.engine" >/dev/null 2>&1; then
    launchctl kickstart -k "gui/$(id -u)/app.clicksmith.engine" || return 1
    echo "Engine restarted on the new version" > "$UI_TMP/engine.txt"
  else
    echo "First run: the welcome screen will start the engine" > "$UI_TMP/engine.txt"
  fi
}
cs_launch() { open "$DEST" || return 1; sleep 1; }

UI_TOTAL=$((CS_BUILD_STEPS + 5))
ui_log_header "install $CS_VERSION"
ui_banner "Installer $CS_VERSION · log: ${UI_LOG/#$HOME/~}"

if [ "$DEV" = 1 ]; then
  UI_I=$((UI_I + 1))
  printf '  %s!%s  Release check skipped (--dev)\n' "$Y" "$R"
else
  ui_step "Checking the release files"  cs_verify_source
  ui_note "$(tail -n 1 "$UI_LOG" 2>/dev/null)"
fi
cs_build_all
ui_step "Cooling the old version"       cs_stop_app
ui_step "Installing to ${DEST_DIR/#$HOME/~}"  cs_copy_app
ui_step "Waking up the engine"          cs_restart_engine
ui_note "$(cat "$UI_TMP/engine.txt" 2>/dev/null)"
ui_step "Opening Clicksmith"            cs_launch

ui_finale "Clicksmith $CS_VERSION is forged and installed"
ui_box "Next steps" \
  "1. First run: follow the welcome window." \
  "   Already set up: click the mouse icon in the menu bar" \
  "   and choose Open Clicksmith..." \
  "2. Allow \"Clicksmith Engine\" in System Settings >" \
  "   Privacy & Security > Input Monitoring and Accessibility." \
  "   After a rebuild: remove old entries, then add again." \
  "" \
  "Health check:    ./scripts/verify.sh 20" \
  "Something odd?   ./scripts/diagnose.sh"
