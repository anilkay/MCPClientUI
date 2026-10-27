#!/usr/bin/env bash
# Downloads appimagetool and the AppImage runtime it embeds, at fixed versions checked against their
# SHA-256, into the current folder. Without --runtime-file appimagetool would fetch the newest
# runtime itself, so a release would ship whatever was published that day.
# Usage: get-appimagetool.sh   (then APPIMAGETOOL=$PWD/appimagetool APPIMAGE_RUNTIME=$PWD/runtime-x86_64)
set -euo pipefail

tool_version=1.9.1
tool_sha256=ed4ce84f0d9caff66f50bcca6ff6f35aae54ce8135408b3fa33abfc3cb384eb0
runtime_version=20251108
runtime_sha256=2fca8b443c92510f1483a883f60061ad09b46b978b2631c807cd873a47ec260d

curl -fsSL -o appimagetool \
  "https://github.com/AppImage/appimagetool/releases/download/$tool_version/appimagetool-x86_64.AppImage"
curl -fsSL -o runtime-x86_64 \
  "https://github.com/AppImage/type2-runtime/releases/download/$runtime_version/runtime-x86_64"
printf '%s  %s\n' "$tool_sha256" appimagetool "$runtime_sha256" runtime-x86_64 | sha256sum -c -
chmod +x appimagetool
