#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: $0 <version> <output-dir>"
  exit 1
fi

VERSION="$1"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(\.[0-9]+)?$ ]]; then
  echo "Version must contain three or four numeric components." >&2
  exit 1
fi
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

  if [[ -e "$APPDIR" ]]; then
    echo "Use a clean output directory; AppDir already exists: $APPDIR" >&2
    exit 1
  fi
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

  RUNTIME_ARGS=()
  if [[ -n "${APPIMAGE_RUNTIME_PATH:-}" ]]; then
    RUNTIME_ARGS=(--runtime-file "$APPIMAGE_RUNTIME_PATH")
  fi
  ARCH=x86_64 "$APPIMAGETOOL" "${RUNTIME_ARGS[@]}" "$APPDIR" "$APPIMAGE_PATH"
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
printf '%s  %s\n' "$SHA256" "$(basename "$PACKAGE_PATH")" > "$PACKAGE_PATH.sha256"

DOWNLOAD_URL="${SBP_LINUX_DOWNLOAD_URL:-}"
if [[ -z "$DOWNLOAD_URL" && -n "${GITHUB_REPOSITORY:-}" && -n "${GITHUB_REF_NAME:-}" && "${GITHUB_EVENT_NAME:-}" == "release" ]]; then
  DOWNLOAD_URL="https://github.com/${GITHUB_REPOSITORY}/releases/download/${GITHUB_REF_NAME}/$(basename "$PACKAGE_PATH")"
fi
if [[ -z "$DOWNLOAD_URL" ]]; then
  echo "$PACKAGE_LABEL generated at: $PACKAGE_PATH"
  echo "No published download URL supplied; no update feed was generated."
  exit 0
fi
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

python3 - "$FEED_PATH" "$VERSION" "$DOWNLOAD_URL" "$SHA256" "$SIGNATURE" <<'PY'
import json, sys
path, version, url, checksum, signature = sys.argv[1:]
feed = {"version": version, "downloadUrl": url, "sha256": checksum}
if signature:
    feed["signature"] = signature
with open(path, "w", encoding="utf-8") as stream:
    json.dump(feed, stream, indent=2)
    stream.write("\n")
PY

echo "$PACKAGE_LABEL generated at: $PACKAGE_PATH"
echo "Feed generated at: $FEED_PATH"
