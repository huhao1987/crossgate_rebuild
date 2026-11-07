#!/bin/bash
# 陪练号：挂一个机器人角色在线，配合你测试组队 / 交易 / 名片 / 邮件。
# 先双击 server-mac/start_server.command 开服务端，再双击这个。关掉窗口就是让它下线。
cd "$(dirname "$0")/../server" || exit 1
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet"
if ! command -v dotnet >/dev/null; then
  echo "没有找到 .NET 8 SDK。请先安装：https://dotnet.microsoft.com/download/dotnet/8.0"
  read -n1 -p "按任意键退出"; exit 1
fi
read -p "陪练号账号（直接回车 = test2）: " ACC; ACC=${ACC:-test2}
read -p "密码（直接回车 = 123456）: " PW; PW=${PW:-123456}
read -p "服务器（直接回车 = 127.0.0.1:9030）: " HOST; HOST=${HOST:-127.0.0.1:9030}
echo
echo "启动中……第一次会编译一下，稍等。"
echo "进游戏后站到它旁边，在聊天里对它说："
echo "  过来 / 组队 / 离队 / 交易 / 名片 / 帮助"
echo "跟它交易它会自动同意（它什么都不给）；给它写信它会回信。"
echo
dotnet run --project src/CgPort.Tools -c Release -- bot "$HOST" "$ACC" "$PW"
read -n1 -p "陪练号已下线，按任意键关闭"
