#!/bin/bash
# 把手机版要用的游戏文件打成一个数据包 crossgate.cgpak（约 7.5G，一个文件），放到 ~/projects/crossgate/CrossGate_phone/，
# 可选用 adb 推到手机 /storage/emulated/0/CrossGate/。手机上游戏目录选 /storage/emulated/0/CrossGate 即可（会自动找到里面的 .cgpak）。
# 用法：bash ~/projects/crossgate/cgport/tools/make_phone_pack.sh          只打包
#       bash ~/projects/crossgate/cgport/tools/make_phone_pack.sh push     打包后推到手机（需要 USB 调试 + adb）
set -e
P="$HOME/projects/crossgate"
SRC="$P/cg_single"
DST="$P/CrossGate_phone"
mkdir -p "$DST"
DOTNET=$(command -v dotnet || true)
for d in /usr/local/share/dotnet/dotnet /opt/homebrew/bin/dotnet "$HOME/.dotnet/dotnet"; do [ -z "$DOTNET" ] && [ -x "$d" ] && DOTNET="$d"; done
[ -z "$DOTNET" ] && { echo "找不到 dotnet（Godot .NET 版需要的 .NET SDK）"; exit 1; }
cd "$P/cgport/server"
"$DOTNET" run -c Release --project src/CgPort.Tools -- pack "$SRC" "$DST/crossgate.cgpak"
# 旧版拷的零碎文件不再需要
for old in bin data _精简佣兵服务端; do [ -e "$DST/$old" ] && echo "提示：$DST/$old 是旧版零碎文件，可以删掉"; done
ls -lh "$DST/crossgate.cgpak"
if [ "$1" = "push" ]; then
  adb shell mkdir -p /storage/emulated/0/CrossGate
  adb push "$DST/crossgate.cgpak" /storage/emulated/0/CrossGate/
  echo "已推到手机 /storage/emulated/0/CrossGate/crossgate.cgpak"
fi
