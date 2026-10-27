#!/usr/bin/env bash
# Packs a published linux-x64 build of the Avalonia app into a single McpClientUI AppImage,
# which runs on most distributions with a double click (after chmod +x).
# Usage: package-appimage.sh PUBLISH_DIR VERSION OUTPUT_FILE
# Needs appimagetool (APPIMAGETOOL=/path/to/appimagetool, or on PATH) and, to keep builds
# reproducible, APPIMAGE_RUNTIME=/path/to/runtime-x86_64; get-appimagetool.sh fetches both.
set -euo pipefail

publish=$1
version=$2
output=$3
tool=${APPIMAGETOOL:-appimagetool}
runtime_args=()
if [ -n "${APPIMAGE_RUNTIME:-}" ]; then runtime_args=(--runtime-file "$APPIMAGE_RUNTIME"); fi
root=$(cd "$(dirname "$0")/../../.." && pwd)
appdir=$(mktemp -d)/McpClientUI.AppDir

mkdir -p "$appdir/usr/lib/mcpclientui" "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps"
cp -R "$publish"/. "$appdir/usr/lib/mcpclientui/"
chmod +x "$appdir/usr/lib/mcpclientui/McpClientUI.Avalonia"

# Entry point: the app itself, no wrapper environment needed (the build is self-contained).
printf '%s\n' '#!/bin/sh' \
  'here=$(dirname "$(readlink -f "$0")")' \
  'exec "$here/usr/lib/mcpclientui/McpClientUI.Avalonia" "$@"' > "$appdir/AppRun"
chmod +x "$appdir/AppRun"

printf '%s\n' '[Desktop Entry]' \
  'Type=Application' \
  'Name=MCP Client' \
  'Comment=Try out MCP servers: tools, resources and prompts over HTTP or stdio' \
  'Exec=McpClientUI.Avalonia' \
  'Icon=mcpclientui' \
  'Categories=Development;' \
  'Terminal=false' > "$appdir/usr/share/applications/mcpclientui.desktop"
cp "$appdir/usr/share/applications/mcpclientui.desktop" "$appdir/mcpclientui.desktop"

cp "$root/assets/logo/png/256.png" "$appdir/usr/share/icons/hicolor/256x256/apps/mcpclientui.png"
cp "$root/assets/logo/png/256.png" "$appdir/mcpclientui.png"

# Extract-and-run: CI runners (and containers) have no FUSE for appimagetool itself.
ARCH=x86_64 VERSION="$version" APPIMAGE_EXTRACT_AND_RUN=1 "$tool" --no-appstream "${runtime_args[@]}" "$appdir" "$output"
rm -rf "$(dirname "$appdir")"
