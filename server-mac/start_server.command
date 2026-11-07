#!/bin/bash
# 魔力宝贝原版服务端（cgmsv）—— Mac 轻量启动：Wine 直接运行 + sqlite 数据库（不需要虚拟机、不需要 MySQL）
# 双击运行。第一次会把服务端复制一份到 ~/projects/crossgate/cgmsv_mac 并改成 sqlite 模式，原目录不动。
set -e
SRC="$HOME/projects/crossgate/cg_single/_精简佣兵服务端"
DST="$HOME/projects/crossgate/cgmsv_mac"

# 1. Wine
WINE=$(command -v wine || command -v wine64 || true)
# Homebrew 的 wine-stable 已下架：也找 gcenx 的 Wine Crossover 和 CrossOver 自带的 wine
for w in "/Applications/Wine Crossover.app/Contents/Resources/wine/bin/wine" \
         "/Applications/Wine Crossover.app/Contents/Resources/wine/bin/wine64" \
         "/Applications/CrossOver.app/Contents/SharedSupport/CrossOver/bin/wine"; do
  [ -z "$WINE" ] && [ -x "$w" ] && WINE="$w"
done
if [ -z "$WINE" ]; then
  echo "没有找到 Wine。请先在终端运行下面两行（只需一次）："
  echo "  softwareupdate --install-rosetta --agree-to-license"
  echo "  brew tap gcenx/wine && brew install --cask wine-crossover"
  echo "  xattr -dr com.apple.quarantine \"/Applications/Wine Crossover.app\""
  read -n1 -p "按任意键退出"; exit 1
fi

# 2. 第一次：复制服务端（不含 phpstudy/Navicat 等用不到的大目录），切换到 sqlite
if [ ! -f "$DST/cgmsv.exe" ]; then
  echo "第一次运行：复制服务端到 $DST ..."
  rsync -a --exclude phpstudy_pro --exclude Navicat9 --exclude 'log/*.log' "$SRC/" "$DST/"
  perl -pi -e 's/^dbengine=0/dbengine=1/' "$DST/setup.cf"
  mkdir -p "$DST/log" "$DST/GMLog"
fi

# 3. 启动（中文代码页，否则中文地图文件名读不到）
export LANG=zh_CN.UTF-8 LC_ALL=zh_CN.UTF-8 WINEDEBUG=-all WINEPREFIX="$HOME/.cgmsv-wine"
IP=$(ipconfig getifaddr en0 2>/dev/null || ipconfig getifaddr en1 2>/dev/null || echo "本机IP")
echo "服务端启动中（约 20 秒）。手机连同一个 Wi-Fi，在移植版里填：$IP 端口 9030"
echo "关闭这个窗口 = 关闭服务端"
cd "$DST"
case "$WINE" in
  */CrossOver.app/*)
    # CrossOver 自带的 wine 要用"容器"（bottle），不认 WINEPREFIX：第一次建一个 64 位的 cgmsv 容器
    CX="$(dirname "$WINE")"
    if ! "$CX/cxbottle" --bottle cgmsv --status >/dev/null 2>&1 && [ ! -d "$HOME/Library/Application Support/CrossOver/Bottles/cgmsv" ]; then
      echo "第一次运行：创建 CrossOver 容器 cgmsv（约 1 分钟）..."
      "$CX/cxbottle" --bottle cgmsv --create --template win10_64
    fi
    exec "$WINE" --bottle cgmsv --wait-children "$DST/cgmsv.exe" ;;
  *) exec "$WINE" "$DST/cgmsv.exe" ;;
esac
