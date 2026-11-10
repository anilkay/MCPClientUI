#!/usr/bin/env bash
# Packs a published linux-x64 build of the Avalonia app into a .deb for Debian and Ubuntu: it installs
# to /opt/mcpclientui, adds an "MCP Client" entry with the logo to the application menu and a
# `mcpclientui` command. Needs dpkg-deb (any Debian/Ubuntu machine, including CI runners).
# Usage: package-deb.sh PUBLISH_DIR VERSION OUTPUT_FILE
set -euo pipefail

publish=$1
version=$2
output=$3
root=$(cd "$(dirname "$0")/../../.." && pwd)
pkg=$(mktemp -d)/mcpclientui

mkdir -p "$pkg/DEBIAN" "$pkg/opt/mcpclientui" "$pkg/usr/bin" "$pkg/usr/share/applications" \
    "$pkg/usr/share/icons/hicolor/256x256/apps" "$pkg/usr/share/icons/hicolor/512x512/apps"
cp -R "$publish"/. "$pkg/opt/mcpclientui/"
chmod 755 "$pkg/opt/mcpclientui/McpClientUI.Avalonia"
ln -s /opt/mcpclientui/McpClientUI.Avalonia "$pkg/usr/bin/mcpclientui"

printf '%s\n' '[Desktop Entry]' \
    'Type=Application' \
    'Name=MCP Client' \
    'Comment=Try out MCP servers: tools, resources and prompts over HTTP or stdio' \
    'Exec=/opt/mcpclientui/McpClientUI.Avalonia' \
    'Icon=mcpclientui' \
    'Categories=Development;' \
    'StartupWMClass=McpClientUI.Avalonia' \
    'Terminal=false' > "$pkg/usr/share/applications/mcpclientui.desktop"
cp "$root/assets/logo/png/256.png" "$pkg/usr/share/icons/hicolor/256x256/apps/mcpclientui.png"
cp "$root/assets/logo/png/512.png" "$pkg/usr/share/icons/hicolor/512x512/apps/mcpclientui.png"

# The build is self-contained; what it still needs from the system is the usual .NET and Avalonia basics.
size=$(du -sk "$pkg/opt" | cut -f1)
printf '%s\n' "Package: mcpclientui" \
    "Version: $version" \
    "Section: devel" \
    "Priority: optional" \
    "Architecture: amd64" \
    "Installed-Size: $size" \
    "Depends: libc6, libgcc-s1, libstdc++6, zlib1g, libfontconfig1, libice6, libsm6, libx11-6" \
    "Recommends: libicu-dev | libicu74 | libicu72 | libicu70" \
    "Maintainer: anilkay <aanilkay@gmail.com>" \
    "Homepage: https://github.com/anilkay/MCPClientUI" \
    "Description: MCP Client: try out MCP servers" \
    " Connects to MCP servers over Streamable HTTP or stdio and lets you browse their tools," \
    " resources and prompts, fill in arguments and call them." > "$pkg/DEBIAN/control"

# Refresh the menu and icon caches after (un)installing.
printf '%s\n' '#!/bin/sh' 'set -e' \
    'if command -v update-desktop-database >/dev/null 2>&1; then update-desktop-database -q /usr/share/applications || true; fi' \
    'if command -v gtk-update-icon-cache >/dev/null 2>&1; then gtk-update-icon-cache -q -t /usr/share/icons/hicolor || true; fi' \
    'exit 0' > "$pkg/DEBIAN/postinst"
cp "$pkg/DEBIAN/postinst" "$pkg/DEBIAN/postrm"
chmod 755 "$pkg/DEBIAN/postinst" "$pkg/DEBIAN/postrm"

dpkg-deb --root-owner-group --build "$pkg" "$output"
rm -rf "$(dirname "$pkg")"
