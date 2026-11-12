#!/bin/bash
# Checks that Clicksmith is healthy and measures the engine's real CPU and memory use.
#   ./scripts/verify.sh          10-second measurement
#   ./scripts/verify.sh 30       30-second measurement
# Tip: use the trackpad and move the mouse during the measurement.
export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
cd "$(dirname "$0")/.."
source scripts/lib/ui.sh
SECONDS_TO_MEASURE="${1:-10}"
SUPPORT="$HOME/Library/Application Support/Clicksmith"
LABEL="app.clicksmith.engine"
PASS=0; WARN=0; FAIL=0

ok()   { PASS=$((PASS + 1)); printf '  %s✔%s %s\n' "$OK" "$R" "$1"; }
warn() { WARN=$((WARN + 1)); printf '  %s!%s %s\n' "$Y" "$R" "$1"; }
bad()  { FAIL=$((FAIL + 1)); printf '  %s✘%s %s\n' "$ERR" "$R" "$1"; }
json() { /usr/bin/plutil -extract "$1" raw -o - "$SUPPORT/engine-status.json" 2>/dev/null; }

UI_ANIM=0 ui_banner "Health check"

printf '  %sInstallation%s\n' "$B" "$R"
APP=""; APPV=""
for d in /Applications "$HOME/Applications"; do [ -d "$d/Clicksmith.app" ] && APP="$d/Clicksmith.app"; done
if [ -n "$APP" ]; then
  APPV="$(defaults read "$APP/Contents/Info" CFBundleShortVersionString 2>/dev/null)"
  ok "Installed: $APP ($APPV)"
  codesign --verify --deep --strict "$APP" 2>/dev/null && ok "Signature valid ($(codesign -dv "$APP" 2>&1 | grep -q adhoc && echo ad-hoc || echo Developer ID))" || bad "Signature invalid"
else
  bad "Clicksmith.app not found in Applications"
fi
if [ -f MANIFEST.sha256 ]; then
  shasum -a 256 -c --quiet MANIFEST.sha256 >/dev/null 2>&1 && ok "Source folder matches the release" || warn "Source folder differs from the release manifest"
fi

echo; printf '  %sEngine%s\n' "$B" "$R"
PID="$(launchctl print "gui/$(id -u)/$LABEL" 2>/dev/null | awk '/^\tpid = /{print $3}')"
if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then ok "Running (pid $PID)"; else bad "Not running — open Clicksmith and click Start Engine"; fi
[ -f "$HOME/Library/LaunchAgents/$LABEL.plist" ] && ok "Starts at login" || warn "Not set to start at login"
if [ -f "$SUPPORT/engine-status.json" ]; then
  EV="$(json engineVersion)"
  if [ -n "$APPV" ] && [ -n "$EV" ] && [ "$EV" != "$APPV" ]; then warn "Engine is $EV but the app is $APPV — restart the engine"; elif [ -n "$EV" ]; then ok "Engine version $EV"; fi
  [ "$(json inputMonitoring)" = "granted" ] && ok "Input Monitoring granted" || bad "Input Monitoring not granted"
  [ "$(json accessibility)" = "true" ] && ok "Accessibility granted" || bad "Accessibility not granted"
  N="$(json listeningDevices)"; [ -n "$N" ] && ok "Reading $N HID interface(s); idle built-in devices are skipped"
  W="$(json lastWakeRecovery)"; [ -n "$W" ] && ok "Last wake recovery: $W" || warn "No sleep/wake recovery recorded yet (sleep the Mac once to test)"
  E="$(json lastError)"; [ -n "$E" ] && warn "Last engine message: $E"
else
  bad "No engine status file yet"
fi

echo; printf '  %sConflicts & files%s\n' "$B" "$R"
if launchctl print "gui/$(id -u)/se.samuel.rival3-dpi" >/dev/null 2>&1; then
  warn "rival3-dpi is also running — the DPI button may fire twice"
else ok "rival3-dpi not running (no double shortcuts)"; fi
check_private() {
  local p="$1" m o
  [ -e "$p" ] || return 0
  m="$(stat -f %Lp "$p")"; o="$(stat -f %u "$p")"
  if [ -L "$p" ]; then bad "$2 is a symlink"
  elif [ "$o" != "$(id -u)" ]; then bad "$2 is owned by someone else"
  elif [ $((8#$m & 8#077)) -ne 0 ]; then warn "$2 is readable by others ($m)"
  else ok "$2 is private ($m)"; fi
}
check_private "$SUPPORT" "Settings folder"
check_private "$SUPPORT/config.json" "config.json"
check_private "$SUPPORT/engine-status.json" "engine-status.json"
check_private "$HOME/Library/Logs/Clicksmith-engine.log" "Engine log"

if [ -n "$PID" ] && kill -0 "$PID" 2>/dev/null; then
  echo; printf '  %sResource use%s (measuring %ss — use the trackpad and mouse now)\n' "$B" "$R" "$SECONDS_TO_MEASURE"
  cpu_secs() { ps -o cputime= -p "$1" | awk -F'[:.-]' '{ n=NF; s=$(n-1)+$n/100; if(n>=3)s+=$(n-2)*60; if(n>=4)s+=$(n-3)*3600; print s }'; }
  C0="$(cpu_secs "$PID")"; sleep "$SECONDS_TO_MEASURE"; C1="$(cpu_secs "$PID")"
  PCT="$(awk -v a="$C0" -v b="$C1" -v t="$SECONDS_TO_MEASURE" 'BEGIN{printf "%.2f", (b-a)/t*100}')"
  RSS_MB="$(ps -o rss= -p "$PID" | awk '{printf "%.1f", $1/1024}')"
  awk -v p="$PCT" 'BEGIN{exit !(p < 1.0)}' && ok "Engine CPU: ${PCT}% average" || warn "Engine CPU: ${PCT}% average (expected under 1%)"
  awk -v m="$RSS_MB" 'BEGIN{exit !(m < 60)}' && ok "Engine memory: ${RSS_MB} MB" || warn "Engine memory: ${RSS_MB} MB"
  APID="$(pgrep -x Clicksmith | head -1)"
  [ -n "$APID" ] && ok "Settings app memory: $(ps -o rss= -p "$APID" | awk '{printf "%.1f", $1/1024}') MB (can be quit; the engine keeps working)"
fi

echo
printf '  %s%d passed%s · %s%d warnings%s · %s%d failed%s\n\n' "$OK" "$PASS" "$R" "$Y" "$WARN" "$R" "$ERR" "$FAIL" "$R"
printf '  Manual tests: docs/TESTPLAN.md\n\n'
[ "$FAIL" -eq 0 ]
