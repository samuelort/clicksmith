#!/bin/bash
# Collects everything needed to diagnose a problem into ~/Desktop/Clicksmith-diagnose.txt.
# Contains no input data. Run it while the problem is happening (e.g. while the app hangs).
export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
cd "$(dirname "$0")/.."
OUT="$HOME/Desktop/Clicksmith-diagnose.txt"
{
  echo "== System =="; sw_vers; uname -m
  echo; echo "== Source check =="
  if [ -f MANIFEST.sha256 ]; then shasum -a 256 -c --quiet MANIFEST.sha256 2>&1 && echo "matches release" ; else echo "no manifest"; fi
  echo; echo "== App process =="; pgrep -lx Clicksmith || echo "not running"
  if pgrep -x Clicksmith >/dev/null; then
    echo; echo "== Main thread sample (3 s) =="
    sample Clicksmith 3 2>/dev/null | grep -A45 "main-thread" | head -60
  fi
  echo; echo "== App log =="; tail -40 "$HOME/Library/Logs/Clicksmith-app.log" 2>&1
  echo; echo "== Engine =="; launchctl print "gui/$(id -u)/app.clicksmith.engine" 2>&1 | grep -E "state =|pid =|last exit|Could not find"
  echo; echo "== Engine log =="; tail -30 "$HOME/Library/Logs/Clicksmith-engine.log" 2>&1
  echo; echo "== rival3-dpi =="; launchctl print "gui/$(id -u)/se.samuel.rival3-dpi" 2>&1 | grep -E "state =|pid =" || echo "not loaded"
} > "$OUT" 2>&1
chmod 600 "$OUT"
cat "$OUT"
echo; echo "Saved to $OUT"
