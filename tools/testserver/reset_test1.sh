#!/bin/bash
# 回归测试之间：把 test1（hao）恢复到基准状态——见习剑士 Lv2、宠物 使魔+树精、背包只留 8–11、满血、站在法兰城登入点附近
cd /home/claude/cgserver
TALK_AT=0:0:0 TALK_SAY="/dolua gm_jobx_undo.lua;/dolua gm_petreset.lua;/dolua gm_bagtrim.lua;/dolua gm_heal.lua;/warp 1000 0 242 100" HOLD_MS=1500 timeout 60 dotnet run --project src/CgPort.Tools --no-build -- net 127.0.0.1:9030 test1 /home/claude/wine/cgmsv/data/map >/dev/null 2>&1
grep -a "^S Zd gm_" talk_trace_test1.txt | cut -c1-80 | tr '\n' ' '
