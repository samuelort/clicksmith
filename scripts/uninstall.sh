#!/bin/bash
# Removes Clicksmith. Add --purge to also delete mappings, settings and logs.
set -uo pipefail
export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
LABEL="app.clicksmith.engine"
SUPPORT="$HOME/Library/Application Support/Clicksmith"

pkill -x Clicksmith 2>/dev/null || true
launchctl bootout "gui/$(id -u)/$LABEL" 2>/dev/null || true
rm -f "$HOME/Library/LaunchAgents/$LABEL.plist" "$SUPPORT/$LABEL.plist"
rm -rf "/Applications/Clicksmith.app" "$HOME/Applications/Clicksmith.app"
tccutil reset Accessibility "$LABEL" >/dev/null 2>&1 || true
tccutil reset ListenEvent "$LABEL" >/dev/null 2>&1 || true

if [[ -f "$SUPPORT/rival3-dpi.plist.backup" ]]; then
  "$(dirname "$0")/restore-rival3.sh" || true
fi

if [[ "${1:-}" == "--purge" ]]; then
  rm -rf "$SUPPORT" "$HOME/Library/Logs/Clicksmith-engine.log"* "$HOME/Library/Logs/Clicksmith-app.log"* \
         "$HOME/Library/Logs/Clicksmith-install.log" "$HOME/Library/Logs/Clicksmith-build.log"
  echo "Mappings, settings and logs deleted."
else
  echo "Mappings kept in: $SUPPORT (run with --purge to delete)."
fi
echo "✅ Clicksmith removed."
