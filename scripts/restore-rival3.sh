#!/bin/bash
# Turns rival3-dpi v3.5 back on (e.g. while testing Clicksmith). Safe to run repeatedly.
export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
BACKUP="$HOME/Library/Application Support/Clicksmith/rival3-dpi.plist.backup"
PLIST="$HOME/Library/LaunchAgents/se.samuel.rival3-dpi.plist"
if [[ -f "$BACKUP" && ! -f "$PLIST" ]]; then mv "$BACKUP" "$PLIST"; fi
if [[ ! -f "$PLIST" ]]; then echo "No rival3-dpi installation found."; exit 1; fi
launchctl bootstrap "gui/$(id -u)" "$PLIST" 2>/dev/null || true
launchctl kickstart -k "gui/$(id -u)/se.samuel.rival3-dpi" 2>/dev/null || true
echo "✅ rival3-dpi is running. Avoid mapping the DPI button in Clicksmith at the same time."
