#!/bin/bash
# 把 APK 和数据包装到手机。手机先打开「开发者选项 → USB 调试」，用数据线连上 Mac，手机上点「允许 USB 调试」。
# 双击运行。数据包约 7.5G，第一次推要几分钟到十几分钟；以后手机上已经有同样大小的包就跳过。
P="$HOME/projects/crossgate"
APK="$P/cgport.apk"
PACK="$P/CrossGate_phone/crossgate.cgpak"
ADB=$(command -v adb || true)
for a in "$HOME/Library/Android/sdk/platform-tools/adb" /opt/homebrew/bin/adb /usr/local/bin/adb; do [ -z "$ADB" ] && [ -x "$a" ] && ADB="$a"; done
if [ -z "$ADB" ]; then
  echo "找不到 adb。打开 Android Studio → Settings → Android SDK → SDK Tools，勾上 Android SDK Platform-Tools 安装。"
  read -n1 -p "按任意键退出"; exit 1
fi
echo "等待手机连接（手机上如果弹出「允许 USB 调试」请点允许）……"
"$ADB" wait-for-device
"$ADB" devices -l | sed -n 2p

if [ -f "$APK" ]; then
  echo "安装 $APK ……"
  "$ADB" install -r "$APK" || { echo "安装失败（如果提示签名不一致，先在手机上卸载旧的 CG Port 再试）"; read -n1 -p "按任意键退出"; exit 1; }
else
  echo "没找到 $APK"; read -n1 -p "按任意键退出"; exit 1
fi

if [ -f "$PACK" ]; then
  LOCAL=$(stat -f%z "$PACK")
  REMOTE=$("$ADB" shell "stat -c %s /storage/emulated/0/CrossGate/crossgate.cgpak 2>/dev/null" | tr -d '\r')
  if [ "$LOCAL" = "$REMOTE" ]; then
    echo "手机上已经有数据包（大小一样），跳过。"
  else
    echo "推数据包到手机 /storage/emulated/0/CrossGate/（约 $((LOCAL/1024/1024)) MB）……"
    "$ADB" shell mkdir -p /storage/emulated/0/CrossGate
    "$ADB" push "$PACK" /storage/emulated/0/CrossGate/
  fi
else
  echo "没找到数据包 $PACK，先双击 tools/打包手机数据.command 生成。"
fi
AUDIO="$P/CrossGate_phone/audio"
if [ -d "$AUDIO" ]; then
  echo "推声音包（音乐/音效，约 30MB，只推有变化的）……"
  "$ADB" shell mkdir -p /storage/emulated/0/CrossGate/audio
  "$ADB" push --sync "$AUDIO/." /storage/emulated/0/CrossGate/audio/
fi
echo
echo "装好了。手机上打开 CG Port："
echo "  1. 点「授予存储权限」，在系统设置里给它打开「所有文件访问权限」，返回"
echo "  2. 游戏目录选 /storage/emulated/0/CrossGate，点「检测」→「开始游戏」"
echo "  3. 联网玩：手机和 Mac 连同一个 Wi-Fi，服务器填 Mac 的 IP（start_server.command 窗口里有），端口 9030"
read -n1 -p "按任意键关闭"
