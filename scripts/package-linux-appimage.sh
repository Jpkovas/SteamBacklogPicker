#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <version> <output-dir>"
  exit 1
fi

VERSION="$1"
OUTPUT_DIR="$2"
PROJECT="src/Presentation/SteamBacklogPicker.Linux/SteamBacklogPicker.Linux.csproj"
PUBLISH_DIR="$OUTPUT_DIR/publish"
PORTABLE_PACKAGE_PATH="$OUTPUT_DIR/SteamBacklogPicker-${VERSION}-linux-x64"
APPIMAGE_PATH="$OUTPUT_DIR/SteamBacklogPicker-${VERSION}-linux-x64.AppImage"
APPDIR="$OUTPUT_DIR/SteamBacklogPicker.AppDir"
FEED_PATH="$OUTPUT_DIR/linux-appimage-update.json"

mkdir -p "$OUTPUT_DIR"

dotnet publish "$PROJECT" \
  --configuration Release \
  --framework net8.0 \
  --runtime linux-x64 \
  --self-contained true \
  /p:Version="$VERSION" \
  /p:AssemblyVersion="$VERSION" \
  /p:FileVersion="$VERSION" \
  /p:InformationalVersion="$VERSION" \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$PUBLISH_DIR"

APPIMAGETOOL="${APPIMAGETOOL_PATH:-}"
if [[ -z "$APPIMAGETOOL" ]]; then
  APPIMAGETOOL="$(command -v appimagetool || true)"
fi

if [[ -n "$APPIMAGETOOL" ]]; then
  if [[ ! -x "$APPIMAGETOOL" ]]; then
    echo "APPIMAGETOOL_PATH is not executable: $APPIMAGETOOL" >&2
    exit 1
  fi

  rm -rf "$APPDIR"
  mkdir -p "$APPDIR/usr/bin"

  cp "$PUBLISH_DIR/SteamBacklogPicker.Linux" "$APPDIR/usr/bin/SteamBacklogPicker.Linux"
  chmod +x "$APPDIR/usr/bin/SteamBacklogPicker.Linux"

  cat > "$APPDIR/AppRun" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/bin/SteamBacklogPicker.Linux" "$@"
EOF
  chmod +x "$APPDIR/AppRun"

  cat > "$APPDIR/steam-backlog-picker.desktop" <<'EOF'
[Desktop Entry]
Name=SteamBacklogPicker
Exec=SteamBacklogPicker.Linux
Icon=steam-backlog-picker
Type=Application
Categories=Game;Utility;
Terminal=false
EOF

  cat > "$APPDIR/steam-backlog-picker.svg" <<'EOF'
<svg xmlns="http://www.w3.org/2000/svg" width="128" height="128" viewBox="0 0 128 128">
  <rect width="128" height="128" rx="24" fill="#1b2838"/>
  <circle cx="64" cy="64" r="42" fill="#66c0f4"/>
  <circle cx="64" cy="64" r="28" fill="#2a475e"/>
  <path fill="#ffffff" d="M42 74h44v10H42zm0-30h44v10H42zm0 15h44v10H42z"/>
</svg>
EOF

  ARCH=x86_64 "$APPIMAGETOOL" "$APPDIR" "$APPIMAGE_PATH"
  chmod +x "$APPIMAGE_PATH"
  PACKAGE_PATH="$APPIMAGE_PATH"
  PACKAGE_LABEL="Native AppImage"
else
  cp "$PUBLISH_DIR/SteamBacklogPicker.Linux" "$PORTABLE_PACKAGE_PATH"
  chmod +x "$PORTABLE_PACKAGE_PATH"
  PACKAGE_PATH="$PORTABLE_PACKAGE_PATH"
  PACKAGE_LABEL="Portable Linux package"
fi

SHA256=$(sha256sum "$PACKAGE_PATH" | awk '{print $1}')

DOWNLOAD_URL="https://github.com/${GITHUB_REPOSITORY}/releases/download/${GITHUB_REF_NAME}/$(basename "$PACKAGE_PATH")"
SIGNATURE=""

if [[ -n "${SBP_LINUX_UPDATE_PRIVATE_KEY_PATH:-}" ]]; then
  if [[ ! -f "$SBP_LINUX_UPDATE_PRIVATE_KEY_PATH" ]]; then
    echo "SBP_LINUX_UPDATE_PRIVATE_KEY_PATH does not point to a readable file: $SBP_LINUX_UPDATE_PRIVATE_KEY_PATH" >&2
    exit 1
  fi

  SIGNATURE_PAYLOAD=$(printf '%s\n%s\n%s' \
    "$VERSION" \
    "$DOWNLOAD_URL" \
    "$(printf '%s' "$SHA256" | tr '[:lower:]' '[:upper:]')")

  SIGNATURE=$(printf '%s' "$SIGNATURE_PAYLOAD" \
    | openssl dgst -sha256 -sign "$SBP_LINUX_UPDATE_PRIVATE_KEY_PATH" -binary \
    | base64 \
    | tr -d '\n')
fi

if [[ -n "$SIGNATURE" ]]; then
  cat > "$FEED_PATH" <<JSON
{
  "version": "${VERSION}",
  "downloadUrl": "${DOWNLOAD_URL}",
  "sha256": "${SHA256}",
  "signature": "${SIGNATURE}"
}
JSON
else
  cat > "$FEED_PATH" <<JSON
{
  "version": "${VERSION}",
  "downloadUrl": "${DOWNLOAD_URL}",
  "sha256": "${SHA256}"
}
JSON
fi

echo "$PACKAGE_LABEL generated at: $PACKAGE_PATH"
echo "Feed generated at: $FEED_PATH"
