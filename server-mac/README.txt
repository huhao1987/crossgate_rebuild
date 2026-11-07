Mac 上运行原版服务端（最轻量方案）

原理：用 Wine 直接运行 cgmsv.exe（不开虚拟机），数据库用服务端自带的 sqlite（data.db3，不装 MySQL）。
在我的云端环境实测：这种方式启动约 20 秒，5065 张地图全部加载，登录进游戏正常。

第一次准备（只需一次，在"终端"里运行）：
  softwareupdate --install-rosetta --agree-to-license
  brew tap gcenx/wine && brew install --cask wine-crossover，然后 xattr -dr com.apple.quarantine "/Applications/Wine Crossover.app"

以后每次：双击 start_server.command。
  - 第一次会复制服务端到 ~/projects/crossgate/cgmsv_mac（约 600MB），原目录 cg_single 不动。
  - 窗口里会显示手机要填的 IP，端口 9030。
  - 关窗口就是关服务端。

注意：Mac 版 Wine 我没法在你的 Mac 上实测，第一次运行如果报错，把窗口里的文字发给我。
