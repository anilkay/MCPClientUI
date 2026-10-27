#!/usr/bin/env bash
# Wraps a macOS publish of the Avalonia app into "MCP Client.app", so it opens with a double click
# and has the app icon. Runs on macOS (plutil, iconutil, codesign).
#
#   bash src/McpClientUI.Avalonia/macOS/package-app.sh <publish folder> <version> <output folder>
#
# The bundle is signed ad hoc (no Apple Developer account): Apple Silicon needs a signature to run
# at all. It is not notarized, so a downloaded copy has to be allowed once (see the README).
set -euo pipefail

APP_NAME="MCP Client"              # the .app name and what Finder shows
EXECUTABLE="McpClientUI.Avalonia"  # the apphost: the project's AssemblyName
here=$(cd "$(dirname "$0")" && pwd)
ICON_PNGS="$here/../../../assets/logo/png"   # 16 … 1024 px, drawn by assets/logo/make-logo.ps1

publish=$1
version=$2
out=$3
app="$out/$APP_NAME.app"

rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"

cp "$here/Info.plist" "$app/Contents/Info.plist"
plutil -replace CFBundleShortVersionString -string "$version" "$app/Contents/Info.plist"
plutil -replace CFBundleVersion -string "$version" "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"

iconset=$(mktemp -d)/AppIcon.iconset
mkdir -p "$iconset"
cp "$ICON_PNGS/16.png" "$iconset/icon_16x16.png"
cp "$ICON_PNGS/32.png" "$iconset/icon_16x16@2x.png"
cp "$ICON_PNGS/32.png" "$iconset/icon_32x32.png"
cp "$ICON_PNGS/64.png" "$iconset/icon_32x32@2x.png"
cp "$ICON_PNGS/128.png" "$iconset/icon_128x128.png"
cp "$ICON_PNGS/256.png" "$iconset/icon_128x128@2x.png"
cp "$ICON_PNGS/256.png" "$iconset/icon_256x256.png"
cp "$ICON_PNGS/512.png" "$iconset/icon_256x256@2x.png"
cp "$ICON_PNGS/512.png" "$iconset/icon_512x512.png"
cp "$ICON_PNGS/1024.png" "$iconset/icon_512x512@2x.png"
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"

# macOS treats every file in Contents/MacOS as code, the .NET assemblies too, so each one is signed
# (a non-Mach-O file keeps its signature in extended attributes, which ditto's zip preserves). The
# main executable is signed last, with the bundle.
executable="$app/Contents/MacOS/$EXECUTABLE"
find "$app/Contents/MacOS" -type f ! -path "$executable" -print0 |
    xargs -0 codesign --force --sign - --timestamp=none
codesign --force --sign - --timestamp=none "$app"
codesign --verify --strict --verbose=2 "$app"
