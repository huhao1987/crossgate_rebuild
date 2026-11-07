# CG Port（魔力宝贝 Android 移植）

Godot 4.3 **.NET（C#）** 工程：客户端 + 自写服务端（单机）+ 协议框架。
**工程本身不包含任何原版素材**，运行时读取你本机的游戏目录；`testdata/` 是程序生成的原创占位素材，没有游戏目录时也能完整跑一遍。

## 现在能做什么

- 读取原版资源（图像、调色板、动画、客户端/服务端地图），在 Godot 里显示等距地图、物件、NPC、角色动画。
- 点击/触摸地面行走：客户端算路径，**每一步都由服务端校验**（防穿墙/瞬移）。
- 踩到传送点切换地图；在野外遇敌，进入简易战斗界面（攻击/防御/逃跑），打完回到地图。
- 战斗胜利得经验、升级、按掉落率拿道具；背包（20 格、可堆叠，显示道具图标）。
- 点 NPC 自动走过去对话：执行服务端原版 NPC 脚本（对话、给/收道具、加钱、传送、触发战斗、事件标记）。
- 自动存档（换地图、战斗结束、对话结束时），下次打开从存档位置继续。
- 单机模式下服务端逻辑在客户端进程里直接运行（`LocalSession`）。
- 首次运行设置：选择游戏目录后自动识别全部资源组（含动画格式）、调色板、服务端目录，生成配置；Android 上可申请"所有文件访问"权限。
- 大地图物件按 16×16 格区块加载：最大的地图有 8.8 万个物件，同时存在的精灵控制在几千个以内。
- 触屏：点按行走/对话，双指缩放（Android 手势已开启），界面按屏幕比例自适应。

## 架构

```
client/   Godot C# 客户端（Main、MapView、BattleUi、配置、纹理缓存）
   │ 只依赖 IGameSession 接口
   ▼
server/src/CgPort.Client    会话层：IGameSession / LocalSession（单机）/ 以后的 NetworkSession（联网）
server/src/CgPort.Server    世界（地图、移动校验、传送）、遇敌、战斗骨架
server/src/CgPort.Core      数据层（地图、通行、传送、战斗表、道具/NPC 表）+ 素材解码（Assets/）
server/src/CgPort.Protocol  cgmsv 协议框架（等封包日志后补全）
```
联网模式以后只需要实现一个 `NetworkSession`（内部走 cgmsv 协议），客户端代码不用改。

## 在 Mac 上运行

1. 安装 **.NET 8 SDK**：https://dotnet.microsoft.com/download/dotnet/8.0 （安装包需要输入一次管理员密码）。
2. 下载 **Godot 4.3 .NET 版**（不是标准版）：https://godotengine.org/download/archive/4.3-stable/ → "Godot Engine - .NET"，macOS。
3. 用 Godot .NET 打开本目录的 `project.godot`，第一次会自动编译 C# 代码，然后按 F5。
   - 有 `config.json`（已为你生成，指向 `/Users/hao/projects/crossgate/cg_single`）时直接用真实资源，从地图 1000 开始；
   - 没有有效配置时进入首次设置界面，可以选游戏目录，也可以"用测试素材试玩"。

操作：点地面行走；点 NPC 对话；右上角"背包"（或按 B）；滚轮或触控板捏合缩放；野外会遇敌。

## 打包到 Android（在 Mac 上）

这台云端环境下载不了 Android SDK，所以 APK 需要在你的 Mac 上打包（你已经装了 Android Studio，SDK 现成）：

1. 装好上面的 .NET 8 SDK 和 Godot 4.3 .NET 版后，在终端执行一次：`dotnet workload install android`（Godot 导出 C# 安卓包需要它）。
2. Godot 菜单 **编辑器 → 管理导出模板 → 下载并安装**（4.3 .NET 版的模板）。
3. Godot 菜单 **编辑器 → 编辑器设置 → 导出 → Android**：
   - Android SDK 路径：`~/Library/Android/sdk`
   - Java SDK 路径：`/Applications/Android Studio.app/Contents/jbr/Contents/Home`（Android Studio 自带的 JDK）
4. **项目 → 导出**，选已经配好的 "Android" 预设（arm64、包名 `com.hao.cgport`、所有文件访问权限），点"导出项目"，生成 `build/cgport.apk`。
5. 生成手机数据包：双击 `tools/打包手机数据.command`（或 `bash tools/make_phone_pack.sh push` 直接用 adb 推到手机）。
   会得到**一个文件** `~/projects/crossgate/CrossGate_phone/crossgate.cgpak`（约 7.5G），只含游戏真正读的图档、调色板和单机服务端数据；
   不用再拷上万个零碎文件。把它放到手机 `内部存储/CrossGate/`，再安装 APK，设置里游戏目录选 `/storage/emulated/0/CrossGate` 即可（自动找到包）。
   数据包格式：不压缩、文件依次存放 + 末尾索引，游戏直接在包里随机读，不解压（见 `server/src/CgPort.Core/GameFiles.cs`）。
6. 第一次打开会进入**首次设置**：点"授予存储权限"→ 在系统设置里为本应用打开"所有文件访问权限"→ 回来点"检测"→"开始游戏"。资源分组、动画格式、服务端目录都会自动识别。

注：Godot 4.3 的 C# 安卓导出官方标注为实验性，如果导出报错，把错误信息发给我。

## 配置要点（config.json）

| 键 | 含义 |
|---|---|
| `game_root` | 游戏目录 |
| `server_dir` | 游戏目录里的服务端目录名（例如 `_精简佣兵服务端`） |
| `sets` | 资源分组（照 cgmsv.yml），`anime_header`：BASE/EX 为 12，V3/PUK/Joy 为 20 |
| `start_map` / `start` | 开局地图编号和坐标（不填坐标时自动找一个传送落点） |
| `player_anime` | 角色动画编号 |
| `act_stand` / `act_walk` / `dir_offset` | 站立/行走动作号、动画方向偏移（**待用真实画面校准**） |
| `act_attack` / `act_hurt` / `act_dead` | 战斗里攻击/受击/倒下的动作号（推测值 2/3/5，**待校准**） |

**校准方法**：游戏里点右上角「动作」（或按 F2）打开动作浏览器，用「动作 ±」「方向 ±」逐个看，觉得是哪种动作就点「站立/行走/攻击/受击/倒下」，人物走路朝向不对就点「偏移 ±1」。设置立即生效并写回 `config.json`。动画编号一栏可以改成任意编号（比如怪物的）来看。
| `encounter_scale` / `encounters` | 遇敌概率系数、是否遇敌 |

## 已确认 / 待确认

见 `docs/RESEARCH.md`。用数据本身确认了：通行规则、地图方向、物件占位、传送表、战斗/道具/NPC 表的列结构。
**还要看真实画面或封包日志才能定的**：角色动画方向与动作号、系统色、战斗公式、遇敌细节、协议格式。

## 测试

```
# 服务端/会话/解码/脚本/背包/存档：80 项
cd server && dotnet run --project tests/CgPort.Tests
# 首次设置界面与自动识别
godot --path . -s res://test/SetupTest.cs
# 客户端端到端（占位素材）：开局 → 进小屋 → 回野外 → NPC 对话拿道具 → 遇敌 → 战斗胜利拿经验 → 自动存档
godot --path . -s res://test/E2E.cs
# 用真实数据做一致性检查
cd server && dotnet run -c Release --project src/CgPort.Tools -- validate /Users/hao/projects/crossgate/cg_single
```

`legacy_gdscript/` 是之前的 GDScript 版，保留作参考，Godot 不会加载它。
