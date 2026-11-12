#!/bin/bash
# Merge a local Clicksmith 1.2.4 source folder into this premium GitHub package.
# Run from the extracted GitHub package root:
#   ./import-1.2.4-source.sh ~/Downloads/clicksmith
set -euo pipefail
SRC="${1:-$HOME/Downloads/clicksmith}"
DST="$(cd "$(dirname "$0")" && pwd)"
[ -d "$SRC" ] || { echo "Source folder not found: $SRC"; exit 1; }
[ -f "$SRC/Package.swift" ] || { echo "Package.swift not found in $SRC"; exit 1; }
[ -f "$SRC/Sources/ClicksmithCore/IPC.swift" ] || { echo "Clicksmith source not found in $SRC"; exit 1; }
grep -q 'version = "1.2.4"' "$SRC/Sources/ClicksmithCore/IPC.swift" \
  || { echo "This is not Clicksmith 1.2.4."; exit 1; }
if [ -f "$SRC/MANIFEST.sha256" ]; then
  (cd "$SRC" && shasum -a 256 -c --quiet MANIFEST.sha256) \
    || { echo "Source does not match its 1.2.4 manifest."; exit 1; }
fi
for name in Sources Tests Resources Package.swift MANIFEST.sha256; do
  [ -e "$SRC/$name" ] || continue
  rm -rf "$DST/$name"
  cp -a "$SRC/$name" "$DST/$name"
done
# Bring over real build/install scripts, but preserve this package note if present.
if [ -d "$SRC/scripts" ]; then
  rm -rf "$DST/scripts"
  cp -a "$SRC/scripts" "$DST/scripts"
fi
printf '\nSource merged successfully. Next:\n  git status\n  git add .\n  git commit -m "Clicksmith 1.2.4 beta"\n'
