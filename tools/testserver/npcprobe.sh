#!/bin/bash
# npcprobe.sh 地图 x y NPC名 [额外RAW] → 站到 NPC 旁边说话，打印收到的 BZ 窗口行
cd /home/claude/cgserver
RAW_SEND="SAY /warp $1 0 $2 $(($3+1));WAIT ${PW:-1500};TALK $4;WAIT 1500${5:+;$5}" TALK_AT=0:0:0 HOLD_MS=${HOLD:-1500} timeout 90 dotnet run --project src/CgPort.Tools --no-build -- net 127.0.0.1:9030 ${ACC:-test1} /home/claude/wine/cgmsv/data/map 2>&1 | grep -E "对话|找不到" 
grep -aE "^(S|C) (BZ|xD)" talk_trace_${ACC:-test1}.txt | cut -c1-${W:-600}
