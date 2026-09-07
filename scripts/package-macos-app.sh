#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.0.0}"
OUTPUT_DIR="${2:-artifacts/macos}"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]] || { echo "Use a numeric version." >&2; exit 1; }
swift build -c release
BIN_DIR="$(swift build -c release --show-bin-path)"
APP_PATH="$OUTPUT_DIR/SteamBacklogPicker.app"
[[ ! -e "$APP_PATH" ]] || { echo "Use a clean output directory." >&2; exit 1; }
mkdir -p "$APP_PATH/Contents/MacOS"
cp "$BIN_DIR/SteamBacklogPickerMac" "$APP_PATH/Contents/MacOS/SteamBacklogPickerMac"
cat > "$APP_PATH/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.steambacklogpicker.mac</string>
<key>CFBundleName</key><string>SteamBacklogPicker</string>
<key>CFBundleExecutable</key><string>SteamBacklogPickerMac</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>$VERSION</string>
<key>CFBundleVersion</key><string>$VERSION</string>
<key>LSMinimumSystemVersion</key><string>13.0</string>
<key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
plutil -lint "$APP_PATH/Contents/Info.plist"
if [[ -n "${MACOS_SIGNING_IDENTITY:-}" ]]; then
  codesign --force --options runtime --timestamp --sign "$MACOS_SIGNING_IDENTITY" "$APP_PATH"
else
  codesign --force --sign - "$APP_PATH"
  echo "Development app: ad-hoc signature only; not Developer ID signed or notarized."
fi
codesign --verify --strict "$APP_PATH"
ARCHIVE_NAME="SteamBacklogPicker-${VERSION}-macos-$(uname -m)-development.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP_PATH" "$OUTPUT_DIR/$ARCHIVE_NAME"
(cd "$OUTPUT_DIR" && shasum -a 256 "$ARCHIVE_NAME" > "$ARCHIVE_NAME.sha256")
