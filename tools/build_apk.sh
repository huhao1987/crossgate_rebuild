#!/bin/bash
# 导出并签名 APK（个人用，release 也用 Godot 的 debug keystore）。用法：tools/build_apk.sh [输出路径]
cd "$(dirname "$0")/.."
G=${GODOT:-/home/claude/Godot_v4.3-stable_mono_linux_x86_64/Godot_v4.3-stable_mono_linux.x86_64}
export GODOT_ANDROID_KEYSTORE_RELEASE_PATH=${GODOT_ANDROID_KEYSTORE_RELEASE_PATH:-$HOME/.local/share/godot/keystores/debug.keystore}
export GODOT_ANDROID_KEYSTORE_RELEASE_USER=${GODOT_ANDROID_KEYSTORE_RELEASE_USER:-androiddebugkey}
export GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD=${GODOT_ANDROID_KEYSTORE_RELEASE_PASSWORD:-android}
"$G" --headless --path . --export-release Android "${1:-build/cgport.apk}" 2>&1 | grep -E "ERROR|failed" | grep -v EditorSettings || echo "导出成功"
