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
APPIMAGE_PATH="$OUTPUT_DIR/SteamBacklogPicker-${VERSION}-linux-x64.AppImage"
FEED_PATH="$OUTPUT_DIR/linux-appimage-update.json"

mkdir -p "$OUTPUT_DIR"

dotnet publish "$PROJECT" \
  --configuration Release \
  --framework net8.0 \
  --runtime linux-x64 \
  --self-contained true \
  /p:PublishSingleFile=true \
  /p:IncludeNativeLibrariesForSelfExtract=true \
  --output "$PUBLISH_DIR"

cp "$PUBLISH_DIR/SteamBacklogPicker.Linux" "$APPIMAGE_PATH"
chmod +x "$APPIMAGE_PATH"

SHA256=$(sha256sum "$APPIMAGE_PATH" | awk '{print $1}')

DOWNLOAD_URL="https://github.com/${GITHUB_REPOSITORY}/releases/download/${GITHUB_REF_NAME}/$(basename "$APPIMAGE_PATH")"

cat > "$FEED_PATH" <<JSON
{
  "version": "${VERSION}",
  "downloadUrl": "${DOWNLOAD_URL}",
  "sha256": "${SHA256}"
}
JSON

echo "AppImage placeholder generated at: $APPIMAGE_PATH"
echo "Feed generated at: $FEED_PATH"
