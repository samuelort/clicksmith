# shellcheck shell=bash
# Build steps shared by build-app.sh and install.sh. Run from the repository root.

CS_VERSION="1.2.6"
CS_BUILD="${BUILD_NUMBER:-11}"
CS_SIGN="${SIGN_IDENTITY:--}"
CS_APP="build/Clicksmith.app"
CS_ENGINE="$CS_APP/Contents/Library/LoginItems/Clicksmith Engine.app"

# Makes sure the files about to be built are exactly the released ones
# (no changed, missing or extra source files). Skipped with --dev for local edits.
cs_verify_source() {
  [ -f MANIFEST.sha256 ] || { echo "MANIFEST.sha256 is missing — this isn't a release package."; return 1; }
  if ! shasum -a 256 -c --quiet MANIFEST.sha256; then
    echo "Some files differ from the Clicksmith $CS_VERSION release (listed above)."
    echo "Unzip a fresh copy of the release, or run with --dev if you changed the code yourself."
    return 1
  fi
  awk '{ sub(/^[0-9a-f]+  /, ""); print }' MANIFEST.sha256 | sort > "$UI_TMP/manifest.txt"
  find Sources Tests scripts Package.swift Resources -type f ! -name .DS_Store | sort > "$UI_TMP/files.txt"
  local extra
  extra="$(comm -13 "$UI_TMP/manifest.txt" "$UI_TMP/files.txt")"
  if [ -n "$extra" ]; then
    echo "Files that aren't part of the release:"
    echo "$extra"
    echo "Unzip a fresh copy of the release, or run with --dev if you added them yourself."
    return 1
  fi
  echo "All $(wc -l < "$UI_TMP/manifest.txt" | tr -d ' ') files match the $CS_VERSION release"
}

cs_check_toolchain() {
  command -v swift >/dev/null || { echo "Swift not found. Install Command Line Tools: xcode-select --install"; return 1; }
  swift --version || return 1
}

cs_compile_tests()  { swift build -c release --product ClicksmithSelfTest || return 1; }
cs_compile_engine() { swift build -c release --product ClicksmithEngine || return 1; }
cs_compile_app()    { swift build -c release --product Clicksmith || return 1; }

cs_bin() { swift build -c release --show-bin-path; }

cs_selftest() {
  local bin; bin="$(cs_bin)" || return 1
  "$bin/ClicksmithSelfTest" > "$UI_TMP/selftest.txt" 2>&1
  local rc=$?
  cat "$UI_TMP/selftest.txt"
  return $rc
}

cs_assemble() {
  local bin; bin="$(cs_bin)" || return 1
  rm -rf build
  mkdir -p "$CS_APP/Contents/MacOS" "$CS_APP/Contents/Resources" \
           "$CS_ENGINE/Contents/MacOS" "$CS_ENGINE/Contents/Resources" || return 1
  cp "$bin/Clicksmith" "$CS_APP/Contents/MacOS/Clicksmith" || return 1
  cp "$bin/ClicksmithEngine" "$CS_ENGINE/Contents/MacOS/ClicksmithEngine" || return 1
  cs__plist "app.clicksmith.Clicksmith" "Clicksmith" "Clicksmith" > "$CS_APP/Contents/Info.plist"
  cs__plist "app.clicksmith.engine" "Clicksmith Engine" "ClicksmithEngine" > "$CS_ENGINE/Contents/Info.plist"
  plutil -lint "$CS_APP/Contents/Info.plist" "$CS_ENGINE/Contents/Info.plist" || return 1
}

cs__plist() {
cat <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleIdentifier</key><string>$1</string>
  <key>CFBundleName</key><string>$2</string>
  <key>CFBundleDisplayName</key><string>$2</string>
  <key>CFBundleExecutable</key><string>$3</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$CS_VERSION</string>
  <key>CFBundleVersion</key><string>$CS_BUILD</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.utilities</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHumanReadableCopyright</key><string>© 2026 Clicksmith contributors. MIT License.</string>
</dict></plist>
PLIST
}

cs_icons() {
  local set="build/AppIcon.iconset"
  rm -rf "$set"
  mkdir -p "$set" || return 1
  cp Resources/AppIcon.iconset/*.png "$set/" || return 1
  iconutil -c icns "$set" -o "$CS_APP/Contents/Resources/AppIcon.icns" || return 1
  cp "$CS_APP/Contents/Resources/AppIcon.icns" "$CS_ENGINE/Contents/Resources/AppIcon.icns" || return 1
  cp Resources/MenuBar*.png Resources/LogoTile.png "$CS_APP/Contents/Resources/" || return 1
  rm -rf "$set"
}

cs_sign() {
  local ts=()
  [ "$CS_SIGN" != "-" ] && ts=(--timestamp)
  codesign --force --options runtime ${ts[@]+"${ts[@]}"} --sign "$CS_SIGN" --identifier app.clicksmith.engine "$CS_ENGINE" || return 1
  codesign --force --options runtime ${ts[@]+"${ts[@]}"} --sign "$CS_SIGN" --identifier app.clicksmith.Clicksmith "$CS_APP" || return 1
  codesign --verify --deep --strict "$CS_APP" || return 1
}

cs_build_all() {
  ui_step "Checking the toolbox"        cs_check_toolchain
  ui_step "Forging the self-tests"      cs_compile_tests
  ui_step "Forging the input engine"    cs_compile_engine
  ui_step "Forging the settings app"    cs_compile_app
  ui_step "Stress-testing the metal"    cs_selftest
  ui_note "$(tail -n 1 "$UI_TMP/selftest.txt" 2>/dev/null)"
  ui_step "Assembling the app bundle"   cs_assemble
  ui_step "Painting the icons"          cs_icons
  ui_step "Stamping the signature"      cs_sign
}
CS_BUILD_STEPS=8
