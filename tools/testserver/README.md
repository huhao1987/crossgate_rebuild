测试服务端（Wine 跑的 cgmsv）用的辅助脚本。gm_*.lua 放进 cgmsv 目录（存成 GBK），游戏里 /dolua 文件名 执行。
- reset_test1.sh：回归测试每个测试之前把 test1（hao）恢复成基准状态（见习剑士 Lv2、使魔 + 树精、背包只留 8–11 格、满血、法兰城）。
- run_all_tests.sh：完整回归（每个测试前重置、测试之间隔开），结果在 /tmp/runall.out。
- npcprobe.sh 地图 x y NPC名 [额外指令]：站到 NPC 旁边说话，打印服务端回的窗口包（研究窗口格式用）。
- gm_win.lua 类型 按钮掩码 数据：给自己弹任意窗口（本服 NPC 不会弹的窗口类型用它测）。
- gm_dmg.lua：背包里有耐久的道具耐久减半（测修理）。gm_bagtrim.lua：清背包 12–27 格。
