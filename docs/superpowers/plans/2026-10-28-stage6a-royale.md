# 阶段 6A：单人吃鸡和毒圈 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增第五种模式“吃鸡”（`BattleMode.Royale = 4`，`--mode royale`）：只在河谷开、一局最多 16 辆车（真人 + 机器人，其他四种模式照旧 10 辆）、不复活、不能中途加入、限时 10 分钟；五圈毒圈（等待 → 收缩，约 9 分钟缩到 0），车的中心在当前圈外每整秒掉 `ceil(最大血量 × 这一圈的百分比)`，毒圈击毁不算谁的击杀；按阵亡先后排名，第 1 名“吃鸡！”。协议升到 v5（16 个座位、名次 5 位、毒圈公布消息、开局消息带已公布的圈、击毁原因、结算的伤害和存活时间）。客户端：当前圈边上的淡蓝光墙、圈外屏幕泛蓝和“在安全区外 −2%/秒”、比分板下的毒圈倒计时、小地图上的两个圈、结算页、“<名字> 死于毒圈”；房间里吃鸡锁定河谷、机器人 0～15、两列玩家。机器人会跑圈、只在圈里巡游、时间不够时边跑边打。验收：1 名玩家 + 15 个机器人在河谷上打完一局吃鸡，有人吃鸡。

**Architecture:** 规则全在 `Tank.Sim.Modes`：`ModeRules` 加 `Royale` 和按模式的人数上限 `MaxPlayersFor`（数组一律按最大的 16 开）；新的 `Zone.cs` 放时间表 `ZoneSchedule`、圈 `ZoneRing` 和纯函数 `ZoneState.At(圈, tick)`（仿真、服务器、客户端、机器人共用），`ZonePicker` 在开局时按对局种子和 5C 的导航格挑五个圆心（只在服务器和仿真测试里跑）。`MatchRules` 在吃鸡里每 tick 先按秒放毒（`Simulation.HurtByZone`，击毁事件带 `KillCause.Zone`），再记击毁、伤害，最后判定；排名照各自为战的 `EndSolo`。`Tank.Net` 升到 v5：圈只在等待段开始时公布（新消息 `ZoneMessage`），开局 / 重同步消息带开始 tick 和已公布的圈（`ZoneSync`），快照不带圈。客户端只读这些：引擎无关的文字、布局、小地图、光墙的几何都在各自的 `Logic/`（Art 测试覆盖），Godot 部分是 `World/ZoneWall` + `zone_wall.gdshader`、`Match/ZoneLineView`、`Match/ZoneWarningView` + `zone_vignette.gdshader`，以及计分板、击杀栏、小地图视图的小改动。机器人在 `BotDirector` / `Bot` 里加一条新的路段 `Leg.Zone`。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9.3、LiteNetLib 2.1.4、Godot 4.7.2 .NET 版。只做 macOS。

**设计依据：** [阶段 6 设计](../specs/2026-10-28-stage6-royale-design.md) 的 §1（6A 一行和用户的选择）、§2（模式和人数、出生、排名和结算；双人吃鸡的 §2.2 第二条、§2.3 的按队部分不在本计划）、§3（毒圈全部）、§6 里 6A 的测试和验收、§7 的“不做”。6B（补给箱）、6C（双人吃鸡）是后面的计划，本计划不做，但留好接口不用返工：排名按 `EndSolo`（6C 把一队当一个名次时只换比较的键），`ModeRules.MaxPlayersFor` 按模式给上限（6C 的 `Duos` 也是 16），`KillCause` 只追加，`MatchStartMessage` 尾部可以继续追加（6B 的箱子表）。参考：[5B 计划](2026-10-26-stage5b-standard.md)（加模式、升协议、房间、HUD、结算、画廊的做法），[5C 计划](2026-10-27-stage5c-bots.md)（机器人、整局测试、开销实测），[5D 计划](2026-10-28-stage5d-minimap.md)（格式、命令、小地图）。

**计划的深度：** 和 5A～5D 一样，不是逐字代码。每个任务写清：改哪些文件、对外接口（确切签名）、规则和算法要点、要先写的失败测试（具体输入和期望值）、命令和验收、提交说明。实现代码由执行者按 TDD 写（先写测试、确认失败、实现、确认通过）。接口一律以本文“共享接口”一节为准；实现中发现接口要改，先改本文这一节再动代码，并在跟进提交里说明。

---

## 执行方式

- **分支和目录**：`feat/stage6a-royale`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage6a`（从 main `cfdafdd` 拉出；第一个提交 `905a92e` 是设计，第二个是本计划）。下文的相对路径都相对于它。
- **逐任务派子代理**（superpowers:subagent-driven-development）：一个任务一个提交。派之前用 `python3 /Users/murphy/Desktop/dev/BattleCity-dev-scripts/brief6a.py N` 生成 `plan6a/brief-N.md`（本文的“执行方式”“执行前须知”“共享接口”和任务 N 全文），交给实现子代理读。提交前五个项目 0 警告、四个测试项目全部通过。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照阶段 6 设计 §2、§3、§6 的 6A 部分和本计划，列“需求 → 代码 → 测试”表），一个做代码质量审查（正确性、确定性、每 tick / 每帧不分配、线上格式的边界检查、原有四种模式行为不变、命名、注释）；按两份意见做**一个**跟进提交 `fix(<scope>): batch X review follow-up — …`。
- **每批之后记进度**：在 `docs/PROGRESS.md` 新建并维护“6A 节点记录”表（放在“5D 节点记录”上面；列：# / 节点 / 状态 / 提交 / 说明；说明里写各任务提交号、跟进提交号、测试数变化、截图目录和看到了什么、实测数字（服务器 tick p50/p99、每客户端下行 KB/s、快照字节、点亮开销、帧率、整局时长和名次）、遗留问题），同时更新“当前位置”的“阶段”“进度”“下一步”和“阶段总览”加阶段 6 一行。随跟进提交一起提交，或单独提交 `docs: progress — 6A batch X`。第 0 行“设计 + 计划”写设计 `905a92e` 和本计划的提交号。
- **顺序**：A → B → C → D → E，批内按任务号。B 要用 A 的全部类型；C（机器人）要用 A 的毒圈和 B 的服务器（开销实测走服务器）；D（客户端）要用 B 的线上字段，放在 C 之后是为了真对局截图里的机器人已经会跑圈。不并行。
- **6A 做完不等试玩**（设计 §1：用户试玩在 6C 之后）：最终验收清单全部打勾后 squash 合入本地 main（`git checkout main && git merge --squash feat/stage6a-royale && git commit`，说明 `feat: stage 6A — solo royale: the zone, 16 seats, protocol v5`，末行 `Tests: …`），不推远端；6B 从新的 main 拉分支。合入后在 main 上再跑一遍四个测试项目和 0 警告检查。
- **只做 macOS**：不写 Windows、Linux 专门的代码路径，不打其他平台的包。
- 设计已确认，中途不问用户，做完汇报。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage6a
```

**新工作目录的准备**（任务 1 第一步做一次；`build/` 和 `.godot/` 都在 `.gitignore` 里，新 worktree 没有）：

```bash
mkdir -p build/shots && cp -R /Users/murphy/Desktop/dev/BattleCity-stage5d/build/shots/base5d build/shots/base5d
ls build/shots/base5d          # heavy kill lamp orbit patrol wall 六张 png（5D 之后的试驾场基线，右下角带小地图）
dotnet build src/Tank.Client/Tank.Client.csproj && godot-mono --headless --path src/Tank.Client --import   # 生成 .godot/（字体等资源）
```

**构建与测试**（这台机器上 `dotnet test` 的输出是中文，`dotnet build` 的汇总行是英文）：

```bash
# 0 警告检查：每行应为 "0 Warning(s) 0 Error(s)"（或中文 "0 个警告 0 个错误"）
for p in src/Tank.Sim/Tank.Sim.csproj src/Tank.Net/Tank.Net.csproj src/Tank.Client/Tank.Client.csproj \
         src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do
  echo "$p: $(dotnet build $p 2>&1 | grep -E '警告|错误|Warning\(s\)|Error\(s\)' | tr '\n' ' ')"; done
for t in Sim Net Art Update; do echo "$t: $(dotnet test tests/Tank.$t.Tests 2>&1 | grep -E '通过!|失败!')"; done
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Zone" 2>&1 | tail -5          # 只跑某些类
dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~RoyaleMatch" --logger "console;verbosity=detailed" 2>&1 | grep -E 'royale|通过|失败'   # 整局，打印数字
dotnet run --project tests/Tank.Soak -c Release 2>&1 | tail -20                                # soak（任务 5 起多一行吃鸡）
```

- 成功的行形如 `已通过! - 失败: 0，通过: 1100 …`；`grep -E '通过!|失败!'` 两种都能抓到。
- **改动前的测试数**（本计划写作时在 `905a92e` 上实测）：Sim 1100、Net 294、Art 1238、Update 42；五个项目 0 警告。每个任务的提交说明末行写测试数变化，例如 `Tests: Sim 1100 → 1131.`（列出有变化的项目）；下文各任务给的数是“至少”，以实际为准。Update 本阶段不该变。
- 以往的偶发：构建完立刻跑 Sim 偶尔有 1 条失败、重跑都过。遇到时重跑一次，两次都失败才算真失败，并在进度里记下是哪条。计时测试放在各项目已有的 `Timing` 集合里（`TimingCollection`），Debug 下只打印、查宽松上限，真上限在 Release 下查。
- 客户端项目 `Tank.Client.csproj` 不把警告当错误（`TreatWarningsAsErrors=false`），“0 警告”靠上面的 grep 检查；其他项目由 `Directory.Build.props` 的 `TreatWarningsAsErrors=true` 保证。
- **Art 测试编进来的客户端代码**（`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`）：`scripts/Art/Mesh/**/*.cs`、**任何** `Logic/` 目录下的 `**/Logic/**/*.cs`、`Art/Palette.cs`、`Bootstrap/LaunchOptions.cs` 等。本阶段新的引擎无关代码放进 `Match/Logic/`（命名空间 `Tank.Client.Match`）、`World/Logic/`（`Tank.Client.World`）、`Minimap/Logic/`（`Tank.Client.Minimap`）、`UI/Logic/`（`Tank.Client.UI`）或 `Art/Mesh/`（`Tank.Client.Art.Meshes`），不用改 csproj；这些文件里不能 `using Godot`。Art 测试项目引用 `Tank.Sim` 和 `Tank.Net`，也编进了 `tests/Shared/*.cs`（`Allocation.AssertNone`，命名空间 `Tank.Testing`）。
- `Tank.Sim` 的 `internal` 只对 `Tank.Sim.Tests` 可见（`Simulation.TankRef` 摆车、改血、去掉保护；`NavGrid.SetCostForTests`），`Tank.Net` 的 `internal` 只对 `Tank.Net.Tests` 可见（`BattleServer.Director`）；客户端只能用 public 成员。
- 河谷在测试里：Sim 用 `RiverlandFixture.Field` / `RiverlandFixture.Grid`（共用、别改它们；要改的各自 `MapCatalog.Riverland.NewBattlefield()` / `NewNavGrid()`），Art 用 `MapCatalog.Riverland.NewBattlefield()`（第一次生成要一两秒，测试类里用静态 `Lazy` 共用）。河谷：1200 m 见方，`Border = 30`，可开车的范围 [30, 1170]²，16 个各自为战出生点 `RiverlandMap.Spawns.Solo`，40 个路点 `RiverlandMap.Waypoints`，地标 `RiverlandMap.Landmarks`（`Lake` 在深水里）。
- **坐标方向**：仿真的地面坐标是 (x, z)，`System.Numerics.Vector2(x, z)`；`Angles.Forward(yaw) = (−sin yaw, −cos yaw)`，yaw 0 朝 −z。圆周上的角度一律“从 +X 转向 +Z”（`RingMesh` 的约定），第 k 个采样点在 `2πk / N`。
- **确定性**：仿真和机器人只用自己的 `XorShift` 和仿真状态；圆心由服务器挑好再发给客户端，客户端从不自己挑（`MathF.Sin/Cos` 不同机器可能差最后一位，不影响：只有服务器算）。`ZoneState.At` 在客户端只用来画和写字，伤害只在服务器算。
- **每 tick 不分配**：`MatchRules.Update`、`BotDirector.Update` / `Command`、`ZoneState.At`、`BattleClient.ZoneAt`、`MinimapFeed`、HUD 每帧的路径都不许分配（字符串只在显示的值变了时建）；开局时（挑圈、建表）可以分配。

**Godot**（开发脚本在 `/Users/murphy/Desktop/dev/BattleCity-dev-scripts/`，见它的 `README.md`）：

```bash
S=/Users/murphy/Desktop/dev/BattleCity-dev-scripts
W=/Users/murphy/Desktop/dev/BattleCity-stage6a
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后先编译：Godot 命令行不编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs/.gdshader 后生成 .uid，和源文件一起提交
mkdir -p build/shots/6a-tNN                                 # build/ 在 .gitignore 里
# HUD 画廊（纯色底上的预制帧）：SCENE 传空字符串；带河谷小地图的屏 --screenshot-at 给 3 秒以上
$S/godot_run.sh $W "" 90 --hud-demo royale --screenshot $W/build/shots/6a-tNN/royale.png --screenshot-at 3 --quit-after 4
# 主场景开房、自动开局（真对局，房主线程上的服务器）；吃鸡 1 名玩家 + 15 个机器人
G="--host --mode royale --map riverland --bots 15 --autostart --seed 11"
$S/godot_run.sh $W "" 240 $=G --autodrive --fps-log --screenshot $W/build/shots/6a-tNN/match-120.png --screenshot-at 120 --quit-after 130
ls -la build/shots/6a-tNN/                                  # 成败看文件在不在（脚本只打印含 screenshot/error 的行）
# 试驾场六张回归截图，OUTDIR 相对于工作目录；和 5D 之后的基线整张比（试驾场本阶段不变）
$S/baseline4b.sh $W build/shots/6a-tNN-sandbox
python3 $S/pngdiffdirs.py build/shots/base5d build/shots/6a-tNN-sandbox
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束都要确认没有残留的 Godot 进程
```

- 截图路径必须是绝对路径；`--screenshot` 一次运行只截一张；每张都要人工看（用 Read 打开 PNG），在进度说明里写看到了什么。`--screenshot-at` 在对局里按对局场景的时钟算（对局场景创建起），毒圈按服务器的对局时钟（开局起）：第 1 圈 90 s 开始缩、180 s 缩完，开局前加载约 5～15 s，截“正在缩”的画面给 120 s 左右。
- 河谷第一次生成在 Debug 客户端里要几秒；`--autostart` 等地图就绪再开局，超时给 240 秒以上（吃鸡对局要等毒圈）。
- 凡是动了客户端的任务都重拍六张试驾场回归截图并和 `build/shots/base5d/` 比：差异只允许出现在云、地平线、草叶摇动、粒子上（以往 orbit 约 23%、heavy 约 8% 来自环绕镜头的时间差，看图判断）。
- 帧率：`--fps-log` 每秒打印平均和最差帧时间，`$S/fpsstats.sh LOG` 汇总；任务 10、11 记吃鸡对局（16 辆车 + 光墙）的帧率，和 5D 验收时河谷标准战的帧率比。

**提交**：

- 说明用英文一行，带范围前缀：`feat(sim): …`、`feat(net): …`、`feat(server): …`、`feat(bots): …`、`feat(client): …`、`test(sim): …`、`fix(<scope>): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。最后一行 `Tests: Sim 1100 → 1131.`（列出有变化的项目）。
- 作者用仓库配置（`火狐`），不加 `Co-Authored-By`，不推送远端。
- 只提交本任务的文件和它们的 `.uid`。别 `git add -A`。

---

## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Modes/BattleMode.cs` | 修改 | `Royale = 4`；`ModeRules`：`MaxPlayers = 16`、`DefaultMaxPlayers`、`MaxPlayersFor`、`MaxBotsFor`、`HasZone`、`OnlyMap`、`AllowsMap`，`LastMode`、`All`、`Id`、`HasTeams`、`DurationTicks` 认吃鸡 | 1 |
| `Modes/MatchRules.cs` | 修改 | 构造加 `seed`（吃鸡按种子排出生点顺序）；吃鸡的判定同各自为战；`StartTick`、`Entrants`、`LastedTicks`；构造加 `zone`，每 tick 放毒；`Standing.Damage` | 1、3 |
| `Modes/Zone.cs` | 新建 | `ZoneRing`、`ZoneStage`、`ZoneSchedule`（五圈的时间、半径、百分比，第 0 圈，检查）、`ZonePhase`、`ZoneState`（纯函数 `At`） | 2 |
| `Modes/ZonePicker.cs` | 新建 | 按种子和导航格挑五个圆心；`Land`（可通行、干地、和出生点连通的格） | 2 |
| `GameEvent.cs` | 修改 | `KillCause { Shell, Zone }`、`GameEvent.Cause`（末尾追加、默认 `Shell`） | 3 |
| `Simulation.cs`、`Simulation.Shells.cs` | 修改 | `HurtByZone(tankId, damage)`；私有 `Damage` 带击毁原因 | 3 |
| `Ai/BotDirector.cs`、`Ai/BotDirector.Zone.cs`（新）、`Ai/Bot.cs`、`Ai/Bot.Zone.cs`（新） | 修改/新建 | 跑圈：圈里的点、该不该先跑、圈里的路点；`Leg.Zone`；`RunningToZone`、`ZoneUrgent`；`Summary` 加跑圈次数 | 6 |

**联网 `src/Tank.Net`、服务器 `src/Tank.Server`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Battle/BattleProtocol.cs` | 修改 | `Version = 5`；`BattleMessage.Zone = 26`；注释 | 4 |
| `Battle/BattleSnapshot.cs` | 修改 | `MatchBlock.Entrants`、`PlayerStats.Damage`、`PlayerStats.Lasted`（含 `Equals`、`GetHashCode`） | 4 |
| `Battle/BattleSnapshotCodec.cs` | 修改 | `WriteMatch`/`ReadMatch` 加 `Entrants`；`WriteStats`/`ReadStats` 名次 4 → 5 位、加伤害和存活秒数 | 4 |
| `Battle/BattleMessages.cs` | 修改 | 事件加 2 位击毁原因；`ZoneMessage`、`ZoneSync` 和读写；`MatchStartMessage.Zone` 尾部追加 | 4 |
| `Battle/BattleClient.cs`、`Battle/BattleClient.Match.cs` | 修改 | 收 `Zone` 消息；`HasZone`、`ZoneStartTick`、`ZoneRings`、`ZoneAt` | 4 |
| `Battle/BattleServer.cs`、`Battle/BattleServer.Room.cs`、`Battle/BattleServer.Match.cs` | 修改 | 按模式的真人和机器人上限、吃鸡锁定河谷、开局挑圈、按时公布、开局消息带圈、各自为战式的点亮、伤害和存活时间（对手的伤害赛后才给）、局域网答复按模式的上限 | 5 |
| `src/Tank.Server/Program.cs` | 修改 | 用法加 `royale`、`--bots 0 to 15`；`--mode royale` 配非河谷的 `--map` 报错退出（代码 2） | 5 |
| `tests/Tank.Soak/BattleSoak.cs` | 修改 | 多一行“吃鸡”链路（河谷、4 个客户端 + 12 个机器人）；原有四行不变 | 5 |

**客户端 `src/Tank.Client`**（路径相对 `src/Tank.Client/`）

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `scripts/UI/Logic/ModeText.cs` | 修改 | “吃鸡”和规则一句话 | 1 |
| `scripts/UI/Logic/RoomRules.cs` | 修改 | 按模式的上限算机器人、吃鸡只在河谷的开局阻止原因（和服务器交叉测试） | 5 |
| `scripts/UI/Logic/RoomModel.cs`、`scripts/UI/RoomScreen.cs` | 修改 | 模式列表带“为什么不能换”、吃鸡锁定地图、机器人 0～`MaxBots`、多于 8 行分两列 | 7 |
| `scripts/Bootstrap/LaunchOptions.cs`、`Bootstrap/Main.Flow.cs` | 修改 | `--mode royale`、`HostMap`（吃鸡强制河谷并警告）、`--shot-after outside:S` | 7 |
| `scripts/Match/Logic/ZoneHud.cs` | 新建 | `ZoneHud`（一帧的毒圈 HUD 数据）、`ZoneText`（比分板下那行、圈外提示） | 8 |
| `scripts/Match/Logic/HudText.cs`、`MatchHudFrame.cs`、`KillFeed.cs`、`ScoreboardModel.cs`、`HudLayout.cs` | 修改 | 吃鸡结算标题和副标题；帧带 `Zone`、`ResultsInfo.Entrants`；“死于毒圈”；伤害和存活时间两列；`ZoneLine`、`ZoneWarning` 两个方框 | 8 |
| `scripts/Match/Logic/ZoneCamera.cs` | 新建 | `zone` 镜头预设（截图用）：圈边内侧看光墙 | 8 |
| `scripts/Minimap/Logic/MinimapFrame.cs`、`MinimapFeed.cs`、`MinimapStyle.cs` | 修改 | `MinimapZone`、`MinimapFrame.Zone`、`MinimapFeed.AddZone`、圈的颜色和线宽 | 8 |
| `scripts/Art/Palette.cs` | 修改 | `ZoneWall`（淡蓝） | 8 |
| `scripts/Match/Logic/HudDemoFrames.cs` | 修改 | 画廊加 `royale`、`royale-outside`、`royale-map`、`results-royale-win`、`results-royale` | 8 |
| `scripts/Match/ZoneLineView.cs`、`Match/ZoneWarningView.cs`、`shaders/zone_vignette.gdshader` | 新建 | 比分板下的毒圈行；圈外的泛蓝暗角和提示字 | 9 |
| `scripts/Match/MatchHud.cs`、`ScoreboardView.cs`、`KillFeedView.cs`、`scripts/Minimap/MinimapView.cs` | 修改 | 挂两个新视图；吃鸡的计分板列和紧凑行；毒圈击杀行；小地图画圈和圈外压暗 | 9 |
| `scripts/Art/Mesh/ZoneWallMesh.cs` | 新建（引擎无关） | 单位圆柱面（光墙的网格） | 10 |
| `scripts/World/Logic/ZoneWallShape.cs` | 新建（引擎无关） | 光墙的常数、按角度采墙脚的地面高度 | 10 |
| `scripts/World/ZoneWall.cs`、`shaders/zone_wall.gdshader` | 新建 | 当前圈的光墙 | 10 |
| `scripts/Match/MatchMain.cs`、`MatchMain.Events.cs`、`MatchMain.Camera.cs`、`MatchAutomation.cs`、`scripts/Cameras/CameraPresets.cs` | 修改 | 建光墙、每帧的毒圈状态、HUD 和小地图的圈、毒圈击杀进击杀栏、无队模式的观战、`zone` 预设、`--shot-after outside` | 10 |

**测试**

| 新文件 / 改动 | 任务 |
|---|---|
| Sim `RoyaleRulesTests`；改 `StandardRulesTests`（第 103～113 行）、`MatchRulesTests`（第 340～351、449 行）；Net `BattleProtocolTests` 第 331 行（10 → 16）；Art `ModeTextTests` 认得吃鸡 | 1 |
| Sim `ZoneScheduleTests`、`ZoneStateTests`、`ZonePickerTests` | 2 |
| Sim `ZoneDamageTests`、`MatchStatsTests`；`MatchRulesTests` 的 `Match` 帮手加 `seed`、`zone`（需要时挪成共用的 `RulesMatch.cs`） | 3 |
| Net `RoyaleWireTests`、`ZoneClientTests`；改 `StandardWireTests` 第 23 行（版本） | 4 |
| Net `RoyaleServerTests`；各处遍历 `ModeRules.All` 在试驾场上开局的测试跳过不许开的组合；Art `RoomRulesTests` 交叉测试加吃鸡 | 5 |
| Sim `BotZoneTests`、`RoyaleMatch.cs` + `RoyaleMatchTests`、`VisionLoadTests`（16 辆）；`BotArena` 加 `seed`、`zone`；Net `BotLoadTests` 加 15 个机器人的吃鸡 | 6 |
| Art `RoomModelTests`、`LaunchOptionsTests` 加用例 | 7 |
| Art `ZoneTextTests`、`ZoneCameraTests`；`HudTextTests`、`KillFeedTests`、`ScoreboardModelTests`、`MatchHudFrameTests`、`HudLayoutTests`、`MinimapFeedTests`、`PaletteTests`、`HudDemoFramesTests` 加用例 | 8 |
| Art `ZoneWallMeshTests`、`ZoneWallShapeTests` | 10 |

**开发工具**（仓库外，写计划时已加）：`brief6a.py`（同 `brief5d.py`，指向本计划，写到 `plan6a/brief-N.md`）。

---

## 共享接口

所有任务都按这里的名字和签名写。改接口先改这一节。

### 必须保持源码兼容的现有签名

- `BattleMode` 的值只追加（`Royale = 4`），`ModeRules.All` 末尾追加 `Royale`，原有四个的顺序、`Id` 不变。
- `ModeRules.MaxPlayers` 的意思变成“任何模式最多的座位数”（16）：所有按它开的数组（`MatchRules._players`、`BattleSnapshot.Players`、服务器的座位、客户端的名字表、计分板）自动变成 16，别的不用改；**每种模式的上限**一律用 `ModeRules.MaxPlayersFor(mode)`。`BattleProtocol.MaxPlayers = ModeRules.MaxPlayers` 照旧（16）。
- `MatchRules` 构造只在**末尾**追加参数：`seed`（任务 1）、`zone`（任务 3）。现有调用者（服务器、`BotArena`、测试）不改。
- `GameEvent` 末尾追加 `KillCause Cause = KillCause.Shell`；所有现有的 `new GameEvent(...)` 不改。
- `MatchStartMessage` 末尾追加 `ZoneSync? Zone = null`；`ResultsInfo` 末尾追加 `int Entrants = 0`；`MatchHudFrame` 末尾追加 `ZoneHud Zone = default`；`MatchHudFrame.From` 末尾追加可选参数 `ZoneHud zone = default`；`FeedLine` 末尾追加 `bool Zone = false`；`ScoreRow` 末尾追加 `int Damage = 0, int Lasted = 0`；`HudBoxes` 末尾追加 `ZoneLine`、`ZoneWarning`（`HudLayout.For` 是唯一的构造处）；`MinimapIconSizes` 末尾追加 `float ZoneWidth = 2f, float NextZoneWidth = 1.5f`；`HudDemoFrames.Names` 末尾追加五屏，已有的屏不变（截图不变）。
- `Viewer(byte Team, int OwnTankId, bool OwnDestroyed, bool FreeForAll)` 不改签名：服务器传 `!ModeRules.HasTeams(mode)`（各自为战和吃鸡都是“人人为敌，死后看全场”）。

### 模式（任务 1）

```csharp
namespace Tank.Sim.Modes;

public enum BattleMode : byte
{
    TeamBattle = 0, Annihilation = 1, FreeForAll = 2, Standard = 3,
    /// <summary>Every tank for itself, up to 16, no respawns, on the riverland only; the zone closes in five rings; the last one left wins (stage 6 design §2, §3).</summary>
    Royale = 4,
}

public static class ModeRules
{
    public const int MaxPlayers = 16;            // the most seats any mode has: arrays, the server's seats, the wire
    public const int DefaultMaxPlayers = 10;     // team battle, annihilation, free-for-all, standard
    public const int RoyaleMaxPlayers = 16;
    public const BattleMode LastMode = BattleMode.Royale;
    public static readonly IReadOnlyList<BattleMode> All;            // TeamBattle, Annihilation, FreeForAll, Standard, Royale
    public static int MaxPlayersFor(BattleMode mode);                // Royale 16, the others 10
    public static int MaxBotsFor(BattleMode mode) => MaxPlayersFor(mode) - 1;
    public static bool HasTeams(BattleMode mode);                    // false for FreeForAll and Royale
    public static bool HasZone(BattleMode mode) => mode == BattleMode.Royale;
    /// <summary>The one map the mode is played on (a MapCatalog id), or null for any: royale → RiverlandMap.Id.</summary>
    public static string? OnlyMap(BattleMode mode);
    /// <summary>Whether <paramref name="mapId"/> (ignoring case) may host <paramref name="mode"/>.</summary>
    public static bool AllowsMap(BattleMode mode, string mapId);
    // Id(Royale) == "royale"; DurationTicks(Royale) == 10 min; Respawns, JoinsMidMatch, HasBases: false for Royale
}

public sealed class MatchRules
{
    /// <param name="seed">The match seed: in a royale the solo spawn points are taken in an order drawn from it; ignored otherwise.</param>
    /// <param name="zone">A royale's six rings (<see cref="ZoneSchedule.Check"/>): ring 0, then rings 1–5. Null: no zone (unit tests; the server always passes one). Ignored outside a royale.</param>
    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null, IReadOnlyList<TeamBase>? bases = null,
        uint seed = 0, IReadOnlyList<ZoneRing>? zone = null);

    public const uint SpawnSalt = 0x5A17_0C5Eu;  // XorShift(seed ^ SpawnSalt) shuffles a royale's spawn order
    public int StartTick { get; }                // the simulation tick the match started at (was the private _startTick)
    public int Entrants { get; }                 // the distinct seats that have joined this match, leavers included (a royale: those it started with); ≤ MaxPlayers (B 批跟进)
    /// <summary>How long <paramref name="slot"/>'s tank lasted: to its going out, or to the end (or now) if still in; 0 for an empty seat.</summary>
    public int LastedTicks(byte slot);

    // 任务 3：
    public IReadOnlyList<ZoneRing> ZoneRings { get; }        // the six rings; empty without a zone
    public ZoneState Zone { get; }                           // at Sim.Tick, as of the last Update (Phase None without a zone)
    /// <summary>How many of ZoneRings the players know, ring 0 included: 1 + ZoneSchedule.AnnouncedBy(elapsed) (which counts rings 1–5 only); 0 without a zone.</summary>
    public int KnownRings { get; }                           // A 批跟进：原名 ZoneRingsAnnounced
    public ReadOnlySpan<ZoneRing> AnnouncedRings { get; }    // the rings the players know: ZoneRings[..KnownRings]
}

public struct Standing { /* … */ public int Damage; }    // 任务 3：damage dealt to other sides' tanks, each hit at most the target's health left
```

### 毒圈（任务 2）

```csharp
namespace Tank.Sim.Modes;   // Modes/Zone.cs

/// <summary>A safe circle on the ground: centre (x, z), radius m (0: closed).</summary>
public readonly record struct ZoneRing(Vector2 Centre, float Radius)
{
    public bool Contains(Vector2 p) => Radius > 0f && Vector2.DistanceSquared(p, Centre) <= Radius * Radius;
}

/// <summary>One ring's part of the schedule (stage 6 design §3.1): its waiting and shrinking ticks, the radius it shrinks to, the damage outside while it applies (% of max health a second).</summary>
public readonly record struct ZoneStage(int WaitTicks, int ShrinkTicks, float Radius, int DamagePercent);

public static class ZoneSchedule
{
    public const int Rings = 5;
    /// <summary>Ring k (1–5) at index k − 1: (90 s, 90 s, 420 m, 1 %), (60, 60, 250, 2), (50, 45, 140, 4), (40, 35, 60, 6), (30, 40, 0, 10).</summary>
    public static ReadOnlySpan<ZoneStage> Stages { get; }
    public static ZoneStage Stage(int ring);          // 1–5, ArgumentOutOfRangeException otherwise
    public static int WaitStart(int ring);            // ticks from the match start: 0, 5400, 9000, 11850, 14100
    public static int ShrinkStart(int ring);          // 2700, 7200, 10500, 13050, 15000
    public static int ShrinkEnd(int ring);            // 5400, 9000, 11850, 14100, 16200
    public static int ClosedAt => ShrinkEnd(Rings);   // 16200 (540 s)
    /// <summary>Rings 1–5 announced by <paramref name="elapsedTicks"/>: those whose waiting has begun (1 from the start, 5 from 470 s).</summary>
    public static int AnnouncedBy(int elapsedTicks);
    /// <summary>Ring 0: the circle round the playable rectangle (riverland: (600, 600), 806.1 m), the whole map inside it.</summary>
    public static ZoneRing Outer(Battlefield field);
    /// <summary>ceil(maxHealth × percent / 100) in integers; 0 for 0 %.</summary>
    public static int DamageOf(int maxHealth, int percent);
    /// <summary>
    /// <paramref name="rings"/> as a match takes them: exactly 1 + Rings, every radius finite and ≥ 0, every centre finite, each ring k ≥ 1
    /// inside ring k − 1 (|c_k − c_{k−1}| + r_k ≤ r_{k−1} + 0.01 m). The radii needn't be the schedule's (tests use small ones); radius 0
    /// before ring 5 is allowed (the rest must then be points within the slack). Returns a copy, an array. ArgumentException (ArgumentNullException for null), param <paramref name="paramName"/>.
    /// </summary>
    public static ZoneRing[] Check(IReadOnlyList<ZoneRing>? rings, string paramName);
}

public enum ZonePhase : byte { None, Waiting, Shrinking, Closed }

/// <summary>
/// The zone at one moment (stage 6 design §3.1): <see cref="Ring"/> is the ring being waited for or shrunk to (1–5; 5 once closed);
/// <see cref="Current"/> the safe circle now; <see cref="Next"/> the ring announced (waiting) or being shrunk to, when known;
/// <see cref="TicksLeft"/> the ticks left in this waiting or shrinking (0 once closed); <see cref="DamagePercent"/> what being outside
/// costs a second now (0 while ring 1 waits; ring k − 1's while ring k waits; ring k's while it shrinks; ring 5's once closed).
/// </summary>
public readonly record struct ZoneState(int Ring, ZonePhase Phase, ZoneRing Current, ZoneRing Next, bool HasNext, int TicksLeft, int DamagePercent)
{
    /// <summary>
    /// The pure function every side shares: <paramref name="rings"/>[0] is ring 0, [k] ring k, as far as known (a client's list may stop
    /// short: a ring not known yet holds the last known one as <see cref="Current"/> and gives no <see cref="Next"/>); empty → default
    /// (Phase None). <paramref name="elapsedTicks"/> from the match start, fractional for drawing, below 0 read as 0. Shrinking interpolates
    /// centre and radius linearly from ring k − 1 to ring k. No allocation.
    /// </summary>
    public static ZoneState At(ReadOnlySpan<ZoneRing> rings, float elapsedTicks);

    /// <summary>Outside the safe circle: farther than its radius from its centre, or anywhere once the radius is 0. False with Phase None.</summary>
    public bool IsOutside(Vector2 p);

    /// <summary>Where to be: <see cref="Next"/> when known, else <see cref="Current"/>.</summary>
    public ZoneRing Target => HasNext ? Next : Current;

    /// <summary>Ticks until <see cref="Target"/> is all there is: waiting, the rest of it plus the shrink; shrinking, <see cref="TicksLeft"/>; closed or None, 0.</summary>
    public int TicksToTarget => Phase switch { ZonePhase.Waiting => TicksLeft + ZoneSchedule.Stage(Ring).ShrinkTicks, ZonePhase.Shrinking => TicksLeft, _ => 0 };
}

public static class ZonePicker   // Modes/ZonePicker.cs
{
    public const int Tries = 64;
    public const uint SeedSalt = 0x20E0_6A6Au;   // the server draws with XorShift(matchSeed ^ SeedSalt)
    /// <summary>
    /// The six rings of a match (stage 6 design §3.1): ring 0 = <see cref="ZoneSchedule.Outer"/>, then for k = 1–5 a centre drawn
    /// uniformly in the disc of radius r_{k−1} − r_k round ring k − 1's centre (angle 2π·u, distance (r_{k−1} − r_k)·√v, u and v
    /// from one <see cref="XorShift"/> seeded with <paramref name="seed"/> as given: the caller xors in SeedSalt), so ring k lies wholly inside
    /// ring k − 1, and kept only where the point is dry (WaterDepthAt ≤ 0) and its cell is <see cref="Land"/>; after <see cref="Tries"/> misses,
    /// the centre of the land cell nearest c_{k−1} among those whose centres lie in that disc (row-order scan, first of equals; a land cell's
    /// centre is dry); only a disc with no land cell at all keeps c_{k−1} (can't happen on the riverland, see the code's comment). Radii from the
    /// schedule, never more than ring k − 1's (a map smaller than the riverland: the sandbox's ring 0 is 269 m, under ring 1's 420). Reads
    /// <paramref name="grid"/> and <paramref name="field"/>, changes neither. Allocates (once a match).
    /// </summary>
    /// <exception cref="InvalidOperationException">No cell is land (the spawns don't stand on open, dry cells: a broken map).</exception>
    public static ZoneRing[] Pick(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed);
    internal static ZoneRing[] Pick(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed, out int scanned);   // tests: rings that fell back to the scan
    /// <summary>
    /// Per grid cell (j × CellsX + i): open on <paramref name="grid"/>, dry at its centre (<c>WaterDepthAt ≤ 0</c>) and reachable from
    /// a spawn point's cell (<see cref="NavGrid.TryNearestOpen"/> within 12 m) over open cells, 8 ways, a diagonal only where both side
    /// cells are open (as <see cref="PathFinder"/> moves). Water cells are passed through (the fords), never kept.
    /// </summary>
    public static bool[] Land(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns);
}
```

### 毒圈伤害和击毁原因（任务 3）

```csharp
namespace Tank.Sim;

/// <summary>What knocked a tank out (on TankDestroyed events; Shell on every other event). Values on the wire (2 bits): append only, at most 3.</summary>
public enum KillCause : byte { Shell = 0, Zone = 1 }

public readonly record struct GameEvent(/* … as now … */, int PropId = 0, KillCause Cause = KillCause.Shell);

public sealed partial class Simulation
{
    /// <summary>
    /// Takes <paramref name="damage"/> off live tank <paramref name="tankId"/> whatever its protection (the zone: stage 6 design §3.2). At 0
    /// it becomes a wreck and a TankDestroyed event (TankId 0, ShellId 0, Cause Zone, Position the wreck) joins this step's
    /// <see cref="Events"/>: call it after <see cref="Step"/>, before the events are read. No TankHit event. A wreck or damage ≤ 0: nothing.
    /// </summary>
    /// <exception cref="ArgumentException">No such tank.</exception>
    public void HurtByZone(int tankId, int damage);
}
```

`MatchRules.Update` 的顺序（吃鸡、有圈时；其他模式只是没有第 1 步）：1. 放毒：`elapsed = Sim.Tick − StartTick`，`Zone = ZoneState.At(ZoneRings, elapsed)`；对上次 `Update` 以来跨过的每个开局后的整秒 s（字段 `_lastZoneSecond`；正常每 tick 至多一个，跳过了 `Update` 时补上），取那一秒的圈 `z = ZoneState.At(ZoneRings, s × TickRate)`（正好是这一 tick 时就是 `Zone`），`z.DamagePercent > 0` 时每辆活着、`z.IsOutside(现在的位置)` 的车 `HurtByZone(id, ZoneSchedule.DamageOf(spec.MaxHealth, z.DamagePercent))`；2. 读这一 tick 的事件（含第 1 步的毒圈击毁）：击毁照旧（毒圈击毁的 `TankId` 为 0，不算任何人的击杀）、伤害（见任务 3）；3. 占领、复活、判定；4. 记下每个座位的车现在的血量（下一 tick 算伤害上限用，也是下一 tick 阵亡时排名用的“阵亡前的血量”）。

### 线上格式 v5（任务 4）

```csharp
namespace Tank.Net.Battle;

public enum BattleMessage : byte { /* … */ Events = 25, /// <summary>A ring announced (v5).</summary> Zone = 26, /* … */ }

public static class BattleProtocol { public const int Version = 5; /* MaxPlayers = ModeRules.MaxPlayers (16) */ }

/// <summary>Ring <paramref name="Ring"/> (1–5) of match <paramref name="MatchNumber"/>, sent reliably when its waiting begins (stage 6 design §3.5).</summary>
public sealed record ZoneMessage(int MatchNumber, byte Ring, ZoneRing Circle);

/// <summary>A royale's zone in a match start: the match's start tick and the rings announced so far, ring 0 first (2 to 6 of them).</summary>
public sealed record ZoneSync(int StartTick, IReadOnlyList<ZoneRing> Rings);

public sealed record MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData? Map, IReadOnlyList<PropState> Props, int MyTankId,
    IReadOnlyList<TeamBase>? Bases = null, ZoneSync? Zone = null);

public struct MatchBlock { /* … */ public byte Entrants; }        // a royale: MatchRules.Entrants ("共 N 名"); 0 in the other modes (B 批跟进，偏差 24)
public struct PlayerStats { /* … */ public byte Rank; public int Damage; public int Lasted; }   // Rank 0 (none) or 1–16; Lasted: whole seconds (see task 5 for who gets what)

public sealed partial class BattleClient
{
    public bool HasZone { get; }                       // the match start carried a zone
    public int ZoneStartTick { get; }
    public ReadOnlySpan<ZoneRing> ZoneRings { get; }    // ring 0 … the last announced
    /// <summary>The zone at server tick <paramref name="serverTick"/> (fractional: the interpolator's render tick), Phase None without a zone. No allocation.</summary>
    public ZoneState ZoneAt(double serverTick);
}
```

线上的细节（写在 `BattleMessages` / `BattleSnapshotCodec` 里，任务 4）：

- 事件：每个事件在 `PropId` 之后多写 `Cause` 2 位；读时 `> (byte)KillCause.Zone` 抛 `ProtocolException("Unknown kill cause.")`。
- `ZoneMessage`：类型字节、`MatchNumber` VarUInt、`Ring` 一个字节、圆心 x、z 和半径三个 float。读：`Ring` 在 1～5，三个数有限，半径 ≥ 0 且 ≤ ±100 km 的坐标上限（`MaxBaseCoordinateMetres`），圆心也在上限内，否则 `ProtocolException`。写的时候同样查（`Ring` 不在 1～5 或越界 → `ArgumentException`，`MatchNumber` 为负 → `ArgumentOutOfRangeException`（B 批跟进），一个字节都不写）。
- `MatchStartMessage` 的尾部在基地之后：一个字节圈数 n（0 = 没有毒圈，读成 `Zone = null`；否则 2 ≤ n ≤ 6），n > 0 时 `StartTick` VarUInt（`ReadTick`）和 n 个圈（三个 float，同上检查）。`MaxMatchStartTailBytes(props, rings)` 加圈数字节 1，有毒圈时再加 `5 + rings × 12`（按这条消息的圈数预留，带地图的写法照旧只扩一次、多留不过 64 字节）。`StartTick` 不在 0～`MaxTick` 也算写不了。写的时候 `Zone.Rings` 不是 2～6 个、或有坐标越界 → `ArgumentException`（写之前查，坏消息一个字节都不写，同基地）。
- `MatchBlock.Entrants`：`EndReason` 之后一个字节；读时 `> BattleProtocol.MaxPlayers` 抛。服务器只在有毒圈的模式里填（其他模式 0，偏差 24），填的时候再压到 `MaxPlayers`（B 批跟进）。
- `PlayerStats`：名次 `RankBits = 5`（16 名放不进 4 位）；`Capture` 之后 `Damage` VarUInt、`Lasted` VarUInt。名次 0 是“还没有名次”（对局中每一行、有队伍的模式每一行），1～16 是名次；读时 17～31 → `ProtocolException("Rank out of range.")`；写时（只查活动的行，写之前查、一个字节都不写）名次 > `MaxPlayers`、`Damage` 或 `Lasted` 为负 → `ArgumentOutOfRangeException`（B 批跟进）。
- 局域网答复的 `MaxPlayers` 由服务器按房间当前模式填（`MaxPlayersFor`），读的上限是 `BattleProtocol.MaxPlayers`（16）。

客户端的圈（`BattleClient.Match.cs`）：固定的 `ZoneRing[1 + ZoneSchedule.Rings]` 加计数。开局消息（新对局或重同步）带 `Zone` 且 `ModeRules.HasZone(Mode)` → 用它整个替换（计数 = `Rings.Count`）；不带、或模式没有毒圈（B 批跟进：别的模式的开局消息带了圈也不认）→ `HasZone = false`、计数 0。圈和客户端别的状态一样只在 `OnData` 里写（`Update` 轮询时由传输层调），只能在同一个线程上读（游戏里是轮询的 Godot 主线程）。`ZoneMessage`：对局号不是当前的、或 `Ring` > 计数（中间缺了圈）→ 丢掉；`Ring` < 计数（重复）→ 不变；`Ring == 计数` → 追加。离开对局清空。

### 房间、启动参数（任务 5、7）

```csharp
namespace Tank.Client.UI;

public static class RoomRules
{
    // PlanFor：机器人 = min(room.Bots, max(0, ModeRules.MaxPlayersFor(room.Mode) − humans))
    // StartBlockReason：在地图生成之后、人数之前：!ModeRules.AllowsMap(room.Mode, room.Map) → "吃鸡只在河谷开：换到河谷。"
}

/// <summary>One line of the host's mode list: why the room can't switch to it now (null: it can).</summary>
public sealed record ModeChoice(BattleMode Mode, string Name, string? Blocked);

public sealed class RoomModel
{
    public int MaxBots { get; }                          // ModeRules.MaxBotsFor(Mode)
    public IReadOnlyList<ModeChoice> ModeChoices { get; } // ModeRules.All's order; Blocked: "真人超过 10 个，不能换到团队对战。"
    public string? MapHint { get; }                      // "吃鸡只在河谷开" when the mode fixes the map, else null
    // CanSetMap = isHost && !MatchRunning && ModeRules.OnlyMap(Mode) is null
    // 没有队伍的模式行数（含“机器人 × N 开局时补入”那一行）多于 SoloColumnRows = 8 时分两列：前 ceil(n/2) 行一列，其余一列；
    // 第一列标题 "玩家 · 总数"，第二列标题 ""
}
```

```csharp
namespace Tank.Client.Bootstrap;

public sealed record LaunchOptions
{
    /// <summary>The room's first map when hosting: the mode's only map when it has one (royale: riverland), else --map, else riverland.</summary>
    public string HostMap { get; }
    // --mode royale 配 --map sandbox：Warnings 加 "--mode royale is played on the riverland only; hosting on the riverland."
    // ShotMoments 末尾加 "outside"：--shot-after outside:SECONDS（自己的坦克第一次在安全区外后再过 SECONDS 秒截图，仅吃鸡对局）
}
```

### 对局 HUD（任务 8）

```csharp
namespace Tank.Client.Match;   // Match/Logic/ZoneHud.cs

/// <summary>One frame's zone for the HUD: off outside a royale; Outside when the own live tank is out of the safe circle.</summary>
public readonly record struct ZoneHud(bool On, int Ring, ZonePhase Phase, int SecondsLeft, bool Outside, int Percent)
{
    /// <summary><paramref name="state"/> as the HUD shows it: SecondsLeft = HudText.CeilSeconds(TicksLeft), Percent = DamagePercent; Off for Phase None.</summary>
    public static ZoneHud From(in ZoneState state, bool outside);
}

public static class ZoneText
{
    public const string ClosedLine = "毒圈已闭合";
    /// <summary>"毒圈 2/5 · 0:45 后缩小" (waiting), "毒圈缩小中 0:30" (shrinking), ClosedLine, "" when off. Allocates: call it when the values changed.</summary>
    public static string Line(in ZoneHud zone);
    /// <summary>"在安全区外 −2%/秒" (U+2212 minus).</summary>
    public static string Outside(int percent);
}

public static class HudText
{
    public const string RoyaleWinTitle = "吃鸡！";
    // Results(Royale, …)：名次 1 → (RoyaleWinTitle, "共 N 名", Win)；名次 n > 1 → ("第 n 名", "共 N 名", Neutral)；没有名次 → ("对局结束", "共 N 名", Neutral)。
    // N = info.Entrants。各自为战的结算不变。比分板照旧 SoloKills（"击毁" 自己的击毁数，"存活" 12/16）。
}

public readonly record struct ResultsInfo(byte WinnerTeam, byte WinnerSlot, int TicksLeft, bool IsHost, MatchEndReason Reason = MatchEndReason.None, int Entrants = 0);

public readonly record struct FeedLine(string? Killer, Side KillerSide, string Victim, Side VictimSide, bool Friendly, double ExpiresAt, bool Zone = false)
{
    public const string ZoneTail = "死于毒圈";   // Text: "{Victim} 死于毒圈"
}

public sealed class KillFeed { public void AddZone(string victim, Side victimSide, double now); }

public readonly record struct ScoreRow(/* … */, int Capture = 0, int Damage = 0, int Lasted = 0);   // Lasted: seconds

public static class ScoreboardModel
{
    /// <summary>The 伤害 cell: the number, except another player's while the match is on (the server sends 0 until the end): blank.</summary>
    public static string DamageCell(in ScoreRow row, bool ended);
    /// <summary>The 存活 cell once the match has ended: "m:ss" (HudText.Clock).</summary>
    public static string LastedCell(in ScoreRow row);
}

public static class HudLayout
{
    public const float ZoneLineHeight = 28f;                                     // under the mode label, where the capture bars go in a standard battle
    public const float ZoneWarningWidth = 600f, ZoneWarningHeight = 44f, ZoneWarningTopShare = 0.18f;
    // HudBoxes 末尾：HudRect ZoneLine = (plate.X, CaptureTop, PlateWidth, ZoneLineHeight)，
    //               HudRect ZoneWarning = (cx − ZoneWarningWidth/2, size.Y × ZoneWarningTopShare, ZoneWarningWidth, ZoneWarningHeight)
}

/// <summary>The "zone" camera preset (screenshots of the wall up close): 25 m inside the current circle on the side nearest <paramref name="near"/>, 5 m above the ground, looking out at the wall 8 m up (20 at first: the whole frame was wall against the sky, deviation 30).</summary>
public static class ZoneCamera
{
    public const string Preset = "zone";
    public static (Vector3 Eye, Vector3 Target) Pose(in ZoneRing current, Vector2 near, Func<float, float, float> groundAt);
}
```

```csharp
namespace Tank.Client.Minimap;

/// <summary>The zone on the map: the safe circle now (light blue, outside it darkened) and the next one (white) when known.</summary>
public readonly record struct MinimapZone(Vector2 Centre, float Radius, bool HasNext, Vector2 NextCentre, float NextRadius);

public sealed class MinimapFrame { public MinimapZone? Zone { get; set; } /* Clear() sets it null */ }

public static class MinimapFeed
{
    /// <summary>The zone of <paramref name="state"/>: nothing for Phase None; the next circle while waiting or shrinking, none once closed.</summary>
    public static void AddZone(MinimapFrame frame, in ZoneState state);
}

public static class MinimapStyle
{
    public static readonly Vector3 ZoneLine;          // Palette.ZoneWall
    public static readonly Vector3 NextZoneLine;      // Palette.UiText
    public static readonly Vector3 ZoneShade;         // Palette.UiBackground
    public const float ZoneShadeOpacity = 0.35f;
    // CornerIcons 的 ZoneWidth 2、NextZoneWidth 1.5；FullIcons 的 3、2
}
```

```csharp
namespace Tank.Client.Art;
// Palette 加一项（和其他颜色写在一起）：
public static readonly Vector3 ZoneWall = Hex("7CCBFF");   // the wall, the circle on the map, the outside tint
```

### 光墙（任务 10）

```csharp
namespace Tank.Client.Art.Meshes;
/// <summary>An open unit cylinder: segments + 1 columns (the last repeats the first at UV.x 1, so the stripes don't seam), two rows; vertex (cos θ, y, sin θ), y 0 or 1, θ = 2πk/segments from +X towards +Z; UV = (k / segments, y); one quad a segment wound to face outwards (normals along the radius); the shader draws both sides (cull_disabled, deviation 30).</summary>
public static class ZoneWallMesh { public static MeshData Build(int segments); }

namespace Tank.Client.World;   // World/Logic/ZoneWallShape.cs
public static class ZoneWallShape
{
    public const int Segments = 256;
    public const float Height = 60f, Sink = 3f;     // m: the wall rises Height above the ground under it, its foot Sink below
    public const float MinRadius = 1f;              // below this the wall is hidden
    /// <summary>The ground (or water surface) under each of the Segments points round <paramref name="circle"/>, into <paramref name="into"/> (length Segments). No allocation.</summary>
    public static void SampleBase(Func<float, float, float> groundAt, in ZoneRing circle, Span<float> into);
}

public partial class ZoneWall : Node3D
{
    public void Build(Battlefield field);                         // the mesh, the material, the 256 × 1 base texture
    /// <summary>Once a frame: the wall on <paramref name="circle"/> (hidden below MinRadius); the base heights re-sampled when the circle moved more than 0.5 m.</summary>
    public void Show(in ZoneRing circle);
}
```

### 机器人（任务 6）

```csharp
namespace Tank.Sim.Ai;

public sealed partial class BotDirector
{
    public const float ZoneRunShare = 0.5f;      // a bot runs for a point this share of the target ring's radius from its centre …
    public const float ZoneInsideShare = 0.8f;   // … and counts as in once within this share (20 % clear of the edge); wander waypoints too
    public const float ZoneSnap = 12f;           // the point moves to the nearest open cell within this, m
    public const float ZoneArriveRadius = 20f;   // a small ring (the last, radius 0) counts as reached within this of its centre, m (C 批跟进：原 ZoneHoldRadius = 8f，见偏差 26)
    public const float ZoneWanderRadius = 40f;   // in a ring smaller than this a bot holds once there instead of wandering
    public const float ZoneDetour = 1.3f;        // straight distance × this when no whole path is held
    public const float ZoneSpeedShare = 0.6f;    // of MaxForwardSpeed, for the time to get there
    public const int ZoneMarginTicks = 15 * SimConstants.TickRate;
    public const float GoldenAngle = 2.39996323f;   // bot tank id × this: the angle its point lies at
    public const float ZoneWanderShare = 0.6f;   // a wander point in the ring lies within this share of its radius (任务 6 实现时加的常数)
    public const int ZoneTries = 8;              // points tried for one ring (running), waypoints drawn (wandering)
    public const int ZoneRetryTicks = SimConstants.TickRate;   // a point (or waypoint) in the ring no path reaches: the next asked for after this (任务 6 实现时加); a run that tried ZoneTries points holds this long, then starts over (C 批跟进)

    /// <summary>The match's zone as of this tick's Update (Phase None outside a royale or without rings).</summary>
    internal ZoneState Zone { get; }
    internal bool ZoneOn { get; }                // Zone.Phase != None
    internal static bool WellInside(Vector2 p, in ZoneRing ring);   // within InsideLimit of the centre: where a run stops
    internal static bool InRing(Vector2 p, in ZoneRing ring);       // within max(radius, ZoneArriveRadius)
    internal static float InsideLimit(in ZoneRing ring);            // max(ZoneInsideShare × radius, ZoneArriveRadius) (C 批跟进)
    internal static float ToCircle(Vector2 a, Vector2 b, Vector2 centre, float radius);   // how far along a→b it first comes within radius of centre (C 批跟进：到达时间量到停下的圆)
    internal Vector2 ZoneWanderPoint(Bot bot, in ZoneRing target);  // a random point in the ring (the bot's own random numbers), snapped as ZonePoint
    /// <summary>Whether a bot with <paramref name="distance"/> m to go at <paramref name="topSpeed"/> m/s must drop fighting to get into its ring: outside the safe circle already, or the estimate plus ZoneMarginTicks reaches ZoneState.TicksToTarget.</summary>
    internal static bool MustRun(float distance, float topSpeed, int ticksToTarget, bool outsideCurrent);
    /// <summary>The point in <paramref name="target"/> for <paramref name="bot"/> (try 0, 1, … turns it by another GoldenAngle).</summary>
    internal Vector2 ZonePoint(Bot bot, in ZoneRing target, int attempt);
}

public sealed partial class Bot
{
    public bool RunningToZone { get; }   // heading into the ring it isn't in
    public bool ZoneUrgent { get; }      // running and out of time (or outside the safe circle): fires on the move, never stops to fight; once set, stays set to the run's end (C 批跟进)
    public int ZoneRuns { get; }         // runs started this match
}
```

---

## 任务总览

| 批 | 任务 | 内容 |
|---|---|---|
| A 仿真规则 | 1 | 模式和人数：`Royale`、`MaxPlayersFor`、吃鸡的判定和排名、出生点按种子 |
| | 2 | 毒圈：时间表、`ZoneState.At`、挑圆心（导航格上的陆地） |
| | 3 | 圈外掉血、`KillCause.Zone`、伤害、存活时间、参赛人数 |
| B 联网 | 4 | 协议 v5：编解码、`ZoneMessage`、开局消息的圈、客户端存圈 |
| | 5 | 服务器：按模式的上限、只在河谷、开局挑圈、按时公布、16 个座位；房间规则交叉测试；soak 吃鸡一行 |
| C 机器人 | 6 | 跑圈、圈里巡游、来不及时边跑边打；1 + 15 无画面整局；16 辆车的开销实测 |
| D 客户端 | 7 | 房间和启动参数 |
| | 8 | HUD 逻辑：毒圈文字、结算、击杀栏、计分板、布局、小地图的圈、画廊的帧 |
| | 9 | HUD 视图：毒圈行、圈外泛蓝、计分板和结算、击杀栏、小地图画圈；画廊截图 |
| | 10 | 光墙和对局接线 |
| E 收尾 | 11 | 验收 |
| | 12 | 文档 |

---

## 批次 A：仿真规则

### Task 1：模式和人数

**Files:**
- Modify: `src/Tank.Sim/Modes/BattleMode.cs`、`src/Tank.Sim/Modes/MatchRules.cs`、`src/Tank.Client/scripts/UI/Logic/ModeText.cs`（只加吃鸡的名字和规则：Art 的 `ModeTextTests` 遍历 `ModeRules.All`）
- Test: `tests/Tank.Sim.Tests/RoyaleRulesTests.cs`（新）；改 `StandardRulesTests`（第 103～113 行：五种模式、`LastMode`）、`MatchRulesTests`（第 340～351 行同；第 449 行 `MaxPlayers` 10 → 16，并查 `MaxPlayersFor`）、`tests/Tank.Net.Tests/Battle/BattleProtocolTests.cs` 第 331 行（`BattleProtocol.MaxPlayers` 10 → 16）、`ModeTextTests`
- 实做时补的（`MaxPlayers` 变 16 的连带）：原有四种模式的人数上限不能跟着变 16，所以任务 5 的三处提前在这里改成 `ModeRules.MaxPlayersFor(mode)`：`BattleServer.WriteRoomInfo` 的 `MaxPlayers`、`BattleServer.Match.cs` 补机器人循环的上限、`RoomRules.PlanFor`（`RoomMapTests` 钉住团队战房间答复 10）。测试跟着改：`BattleProtocolTests` 房间信息的人数上限用例 11 → 17，`Garbage_in_the_headers…` 里手工造的开局消息原来靠模式字节 4 不认识才抛（模式现在是一个字节，吃鸡 = 4 合法）：改成正确的头（模式字节、对局号、tick、结束 tick、带地图标志）再查“Map shorter”；`StandardWireTests` 满快照 16 行 780 字节（6235 位，多的六行每行 59 位）；Art `LanRoomRowTests`、`RoomMapModelTests` 的房间信息用 `MaxPlayersFor(mode)`；`LaunchOptionsTests` 的 `--bots` 上限 9 → 15、`--players` 上限 10 → 16（任务 7 要的就是这两个数）、坏模式的警告列出五种。

第一步：做“执行前须知”里新工作目录的准备（基线截图、`.godot/`）。

**规则**

- `ModeRules` 按“共享接口”。`HasTeams(mode) => mode is not (BattleMode.FreeForAll or BattleMode.Royale)`；`DurationTicks(Royale)` 走 `SoloMinutes`（10 分钟）；`Id(Royale) = "royale"`；`Respawns`、`JoinsMidMatch`、`HasBases` 对吃鸡都是 false（照现在的写法自然成立，测试钉住）。`OnlyMap(Royale) = RiverlandMap.Id`（`Tank.Sim.Maps`，同一个程序集），其他 null；`AllowsMap` 忽略大小写比较。
- `MaxPlayers` 改成 16 以后，`MatchRules._players`、`_rankScratch` 自动 16；`EndSolo` 的插入排序注释改成 n ≤ 16。
- `MatchRules.Judge`：`case BattleMode.FreeForAll or BattleMode.Royale`（同一段：活着 ≤ 1 → `Eliminated`，否则到点 → `TimeUp`，都走 `EndSolo`）。排名规则不变（设计 §2.3 的“同一 tick 阵亡按剩余血量”：A 批跟进按“阵亡那一 tick 之前的血量”，再按击毁数、座位号，见偏差 3）。
- 出生点：吃鸡在构造时建 `_spawnOrder`（`int[]`，`Solo` 的下标 0…n−1），用 `XorShift(seed ^ SpawnSalt)` 做 Fisher–Yates（`for i = n−1 … 1: j = NextUInt() % (i+1)`）；`PickSpawn` 在吃鸡里按这个顺序找第一个空的点，其他模式照旧按列表顺序（各自为战不变，见偏差 2）。`Solo` 为空的吃鸡在 `PickSpawn` 里照旧抛。
- `StartTick`（原来私有的 `_startTick`）、`Entrants`（进过这一局的不同座位数，离开不减；B 批跟进前是每次 `Join` 加 1，团队战里同一个座位离开再进会重复计、超过 16 后快照读不了）、`LastedTicks(slot)`：没进过 → 0；`EliminatedAt ≥ 0` → `EliminatedAt − StartTick`；否则 `(EndedAt ≥ 0 ? EndedAt : Sim.Tick) − StartTick`。
- `ModeText.Name(Royale) = "吃鸡"`；`Rule(Royale) = $"最多 {ModeRules.RoyaleMaxPlayers} 辆车，人人为敌，被击毁就出局；毒圈缩小 {ZoneSchedule.Rings} 次，圈外掉血；最后留下的一辆吃鸡，{minutes} 分钟后按剩余血量定名次。"`——`ZoneSchedule` 任务 2 才有：本任务先写字面的 5，任务 2 换成常数。

**先写的测试**（`RoyaleRulesTests`）

- 常数：`All` 是五种且末尾是 `Royale`、`Id` 是 `royale`、`TryParse("ROYALE")` 成功、`LastMode == Royale`；`MaxPlayers == 16`、`MaxPlayersFor(Royale) == 16`、其余四种 10、`MaxBotsFor` 15 / 9；`HasTeams`、`Respawns`、`JoinsMidMatch`、`HasBases` 对吃鸡都 false；`HasZone` 只有吃鸡；`OnlyMap(Royale) == "riverland"`、其余 null；`AllowsMap(Royale, "Riverland")` 真、`(Royale, "sandbox")` 假、`(Standard, "sandbox")` 真；`DurationTicks(Royale) == 18000`。
- 16 人：河谷的 `Spawns`（`MapCatalog.Riverland.Spawns`，配一个小平地的 `Simulation` 就行——出生点只是位置）开吃鸡，16 个座位都 `Join`：16 辆车落在 16 个不同的 `Solo` 点上，`Team == slot`，`Entrants == 16`。
- 按种子：同一个种子两次，前 6 个座位的出生点相同；种子 1～20 里，座位 0 的出生点至少有 8 种；各自为战不管种子是几，座位 0 都在 `Solo[0]`（不变）。
- 判定：吃鸡 3 人，`TankRef` 把座位 1 的血打到 0（造一个击毁：照 `MatchRulesTests` 的 `Match` 帮手的 `Weaken` + `Attack`）、再打掉座位 2 → `Eliminated`，`WinnerSlot` 是座位 0，名次 1、2、3 按阵亡后先；同一 tick 打掉两辆（两辆车各对一辆开炮，弹道同 tick 命中）→ 击毁多的在前，再按座位号；`durationTicks` 给 60，三人都活着到点 → `TimeUp`，按血量排。
- `LastedTicks`：没进过的座位 0；阵亡的 = 阵亡 tick − 开局 tick；结束时还活着的 = 结束 tick − 开局 tick。
- `ModeTextTests`：吃鸡的名字是“吃鸡”，规则里有“毒圈”。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Royale"`

**步骤**

- [ ] 准备工作目录（基线截图、`.godot/`）
- [ ] 写测试，确认失败（编译失败也算）
- [ ] 实现
- [ ] 改上面列出的旧测试（模式列表、10 → 16）
- [ ] 五个项目 0 警告；四个测试项目全过
- [ ] 提交

**提交**：`feat(sim): royale mode — up to 16 seats, solo ranking, spawn order drawn from the match seed`，末行 `Tests: Sim 1100 → N, Art 1238 → M.`

### Task 2：毒圈的时间表、状态和圆心

**Files:**
- Create: `src/Tank.Sim/Modes/Zone.cs`、`src/Tank.Sim/Modes/ZonePicker.cs`
- Modify: `src/Tank.Client/scripts/UI/Logic/ModeText.cs`（规则里的 5 换成 `ZoneSchedule.Rings`）
- Test: `tests/Tank.Sim.Tests/ZoneScheduleTests.cs`、`ZoneStateTests.cs`、`ZonePickerTests.cs`

**规则**

- 时间表（设计 §3.1 的表，偏差 18：先照用，整局测试要调再改这里并记偏差）：

  | 圈 | 等待 s | 收缩 s | 半径 m | 圈外 %/s | `WaitStart` | `ShrinkStart` | `ShrinkEnd`（tick） |
  |---|---|---|---|---|---|---|---|
  | 1 | 90 | 90 | 420 | 1 | 0 | 2700 | 5400 |
  | 2 | 60 | 60 | 250 | 2 | 5400 | 7200 | 9000 |
  | 3 | 50 | 45 | 140 | 4 | 9000 | 10500 | 11850 |
  | 4 | 40 | 35 | 60 | 6 | 11850 | 13050 | 14100 |
  | 5 | 30 | 40 | 0 | 10 | 14100 | 15000 | 16200 |

- `Outer(field)`：可开车矩形 `[Border, SizeX − Border] × [Border, SizeZ − Border]` 的中心、半对角线。
- `ZoneState.At`（`e = max(0, elapsed)`；`n = rings.Length`；`n == 0` → `default`）：
  - `e ≥ ClosedAt`：`Ring 5, Closed`，`Current = rings[min(5, n−1)]`，`HasNext = false`，`TicksLeft 0`，`DamagePercent = Stage(5)` 的 10。
  - 否则找 `WaitStart(k) ≤ e < ShrinkEnd(k)` 的 k。`k − 1 ≥ n`（客户端连上一个圈都没收到）：`Current = rings[n−1]`、`HasNext = false`，阶段照时间算。
  - 等待（`e < ShrinkStart(k)`）：`Current = rings[k−1]`，`Next = rings[k]`（`k < n` 时，`HasNext = true`），`TicksLeft = ceil(ShrinkStart(k) − e)`，`DamagePercent = k == 1 ? 0 : Stage(k−1).DamagePercent`。
  - 收缩：`t = (e − ShrinkStart(k)) / ShrinkTicks`，`k < n` 时 `Current = (lerp(c_{k−1}, c_k, t), lerp(r_{k−1}, r_k, t))`、`Next = rings[k]`、`HasNext = true`；`k ≥ n` 时 `Current = rings[k−1]`、`HasNext = false`；`TicksLeft = ceil(ShrinkEnd(k) − e)`；`DamagePercent = Stage(k).DamagePercent`。
  - `TicksToTarget`：等待 `TicksLeft + Stage(Ring).ShrinkTicks`，收缩 `TicksLeft`，闭合 0。`IsOutside`：`Phase == None` → false；`Current.Radius ≤ 0` → true；否则距离平方 > 半径平方。
- `ZonePicker.Land`：BFS（预分配的 `int[]` 队列，开局一次，可以分配），起点是每个出生点 `TryNearestOpen(p, 12)` 的格；走开着的格，对角只在两个侧格都开着时走；水里的格照走（浅滩连通两岸）但结果里是 false（`field.WaterDepthAt(CentreOf(i, j)) > 0`）。
- `ZonePicker.Pick`：见“共享接口”；圆心所在格 `NavGrid.CellOf(c)` 在格子上、`Land` 为真，且这一点本身是干的（`WaterDepthAt(c) ≤ 0`，A 批跟进加的：只查格心时种子 16、17 的第 5 圈圆心在 2～7 cm 的水里）才收下；64 次都没中 → 扫允许的圆盘（`c_{k−1}` 周围 `r_{k−1} − r_k`）里格心离 `c_{k−1}` 最近的 `Land` 格，取它的格心（偏差 23）；`Land` 一个真都没有 → `InvalidOperationException`。半径取 `min(Stage(k).Radius, r_{k−1})`（实现时改：试驾场的第 0 圈只有 269 m，比第 1 圈的 420 m 小，照表取半径就套不住；河谷上不起作用），所以 `r_{k−1} − r_k` 不会为负。
- 注释写清：挑圆心只在服务器（和仿真测试）里跑，客户端从不调用；`At` 每 tick 调，不分配。

**先写的测试**

- `ZoneScheduleTests`：`Stages` 五项照表；`WaitStart`/`ShrinkStart`/`ShrinkEnd` 照表、`ClosedAt == 16200`；`AnnouncedBy(0) == 1`、`(5399) == 1`、`(5400) == 2`、`(14100) == 5`、`(99999) == 5`；`Stage(0)`、`Stage(6)` 抛；`DamageOf(1000, 1) == 10`、`DamageOf(820, 4) == 33`（32.8 进 1）、`DamageOf(1150, 6) == 69`、`DamageOf(1000, 0) == 0`；`Outer(河谷)` 圆心 (600, 600)、半径 806.10 ± 0.01；`Outer` 对试驾场地图也是可开车矩形的外接圆；`Check`：6 个嵌套的圈过，5 个、7 个抛，第 2 圈有一点出了第 1 圈抛，负半径、NaN 抛。
- `ZoneStateTests`（圈：r0 = ((600, 600), 806)、r1 = ((500, 600), 420)、r2 = ((450, 600), 250)、r3 = ((450, 640), 140)、r4 = ((470, 640), 60)、r5 = ((470, 650), 0)）：
  - `At(0)`：圈 1、等待、`Current == r0`、`Next == r1`、`TicksLeft 2700`、`DamagePercent 0`、`TicksToTarget 5400`；`At(−5) == At(0)`。
  - `At(2700)`：收缩、`Current == r0`（t = 0）、`DamagePercent 1`、`TicksLeft 2700`；`At(4050)`：圆心 (550, 600)、半径 613；`At(4050.5f)` 的半径比 `At(4050)` 小。
  - `At(5400)`：圈 2、等待、`Current == r1`、`Next == r2`、`DamagePercent 1`；`At(7200)`：收缩、`DamagePercent 2`；`At(15000)`：圈 5 收缩、`DamagePercent 10`。
  - `At(16200)`、`At(20000)`：闭合、`Current == r5`、`HasNext` 假、`TicksLeft 0`、`IsOutside(r5.Centre)` 真。
  - 只知道 r0、r1：`At(5400)` 是圈 2 等待、`Current == r1`、`HasNext` 假；只知道 r0：`At(3000)` 收缩、`Current == r0`、`HasNext` 假；空表 → `Phase None`、`IsOutside` 假。
  - `IsOutside`：正好在圈边上（距离 == 半径）是 false，多 0.01 m 是 true。
  - `Allocation.AssertNone`：预热后 1000 次 `At`。
- `ZonePickerTests`（`RiverlandFixture.Field`、`RiverlandFixture.Grid`、`RiverlandMap.Spawns.Solo`）：
  - 种子 1～200（A 批跟进从 1～20 扩的，共用的场地和格子，Debug 约 4 s）：每次 6 个圈、`rings[0] == Outer`、半径照表；每个 k ≥ 1：`|c_k − c_{k−1}| + r_k ≤ r_{k−1} + 0.01`；圆心是干的、所在格 `Land` 为真；1000 个圈里落到扫描的 ≤ 2 个（打印个数和圆心离水最近的距离）。
  - 合成格子：河谷的格子 `Copy()` 后只留两个单格的岛（离中心约 200 m 和 270 m），出生点在岛上 → 64 次都抽不中，第 1 圈圆心正是近的那个岛的格心，之后几圈都在那一格里；`Land` 全假（没有出生点、或格子全关）→ `InvalidOperationException`。
  - 同一种子两次逐个相等；20 个种子的第 1 圈圆心至少 15 种。
  - `Pick` 不改场地和格子（`field.StateVersion`、`grid.Version` 不变）。
  - `Land`：16 个出生点所在格都是 true；`RiverlandMap.Landmarks.Lake` 所在格 false；一个 `Blocked` 的格 false。
  - 连通：试驾场地图的格子 `Copy()`，用 `SetCostForTests` 把一块 5 × 5 的格围上一圈 `Blocked` → 围起来的格都是 false，围墙外紧挨着的开着的格是 true。
  - 试驾场地图 5 个种子：嵌套成立（规则对小图也成立；试驾场不开吃鸡，只测函数）。
  - 计时（`Timing` 集合）：河谷 `Pick` 打印毫秒数，Debug 下 < 2000 ms。

**步骤**：同任务 1（不用再准备目录）。

**提交**：`feat(sim): the zone — five-ring schedule, the shared zone state, ring centres drawn on driveable land`，末行 `Tests: Sim N → M.`

### Task 3：圈外掉血、击毁原因、伤害和存活时间

**Files:**
- Modify: `src/Tank.Sim/GameEvent.cs`、`Simulation.cs`、`Simulation.Shells.cs`、`Modes/MatchRules.cs`
- Test: `tests/Tank.Sim.Tests/ZoneDamageTests.cs`、`MatchStatsTests.cs`；`MatchRulesTests` 的 `Match` 帮手加可选的 `seed`、`zone`（新测试类要用时挪到 `tests/Tank.Sim.Tests/RulesMatch.cs`，`internal`）

**规则**

- `KillCause`、`GameEvent.Cause`、`HurtByZone` 按“共享接口”。私有 `Damage(ref target, damage, attackerId, shellId)` 加参数 `KillCause cause = KillCause.Shell`，击毁事件带上它。`HurtByZone` 用 `TankRef`，不看 `IsProtected`。
- `MatchRules` 构造：吃鸡且 `zone` 不为 null → `ZoneRings = ZoneSchedule.Check(zone, nameof(zone))` 的副本（数组），`Zone = ZoneState.At(ZoneRings, 0)`，`KnownRings = 1 + AnnouncedBy(0)`（= 2）；其他情况 `ZoneRings` 空、`Zone` default、`KnownRings` 0。
- `Update` 的顺序见“共享接口”。放毒的循环按 `Sim.Tanks` 的下标走（`HurtByZone` 不改车的顺序）；整秒是“开局后的整秒”（`elapsed % TickRate == 0`，elapsed > 0），每个整秒恰好掉一次：A 批跟进不再假定每个整秒的 tick 都有 `Update`，记下 `_lastZoneSecond`，把跨过的整秒逐个补上。`Zone` 和 `KnownRings` 每次 `Update` 都更新（结束后不再变）。
- 伤害（所有模式都记，界面只在吃鸡用，见偏差 6）：`TankHit` 和 `SplashDamage`，`e.Damage > 0`，开炮的座位 ≥ 0 且和挨打的车不同队（各自为战、吃鸡里就是不同座位）：`dealt = min(e.Damage, _health[挨打的座位])`、`_health[…] −= dealt`、开炮的 `Standing.Damage += dealt`。`_health` 是每个座位上一次 `Update` 结束时车的血量（座位的车不在仿真里 → 0）；`Join` 时设成满血。挨打的车不是任何座位的（不会发生）→ 不记。
- 判定照任务 1；毒圈击毁照普通阵亡记（`Deaths++`、`Alive = false`、`EliminatedAt`），没有人加击杀。
- 不分配：`_health` 在构造时按 `MaxPlayers` 开。

**先写的测试**（`ZoneDamageTests`，`TestWorld.FlatField()`（200 m 见方）上的吃鸡；测试用的小圈：r0 = ((100, 100), 40)、r1 = ((110, 100), 30)、r2 = ((112, 100), 20)、r3 = ((112, 100), 12)、r4 = ((112, 100), 6)、r5 = ((112, 100), 0)；车摆在 (20, 100)（r0 外）和 (100, 100)（圈里））

- `HurtByZone`：开局保护中的车照样掉血；血到 0 → 这一步的 `Events` 里有一个 `TankDestroyed`（`TankId 0`、`ShellId 0`、`Cause Zone`、位置是车的位置），没有 `TankHit`；对残骸再调什么都不发生；没有这辆车 → `ArgumentException`。
- 第 0 圈不掉血：圈外的车从开局到 elapsed 2699 血量不变。
- 收缩段按秒掉：elapsed 2700（第 1 圈开始缩，1%）掉 `DamageOf(max, 1)`，2701～2729 不变，2730 再掉一次；第 2 圈等待段（5400～7199）照第 1 圈的 1% 掉；7200 起 2%。
- 圈里的车不掉血；车开出圈（`TankRef` 挪位置）下一个整秒开始掉。
- 闭合：满血的车在 elapsed 16200 起每秒 10%，第 10 次（elapsed 16470）被击毁；`Kills` 谁都没加，它的 `Deaths == 1`、`EliminatedAt` 是那个 tick。
- 不是吃鸡（各自为战）的同一场景，传了 `zone` 也不掉血（被忽略）；吃鸡不传 `zone` → `Zone.Phase == None`、不掉血、`KnownRings == 0`。
- `KnownRings`：开局 2；elapsed 5400 时 3；14100 起 6；`AnnouncedRings` 的长度跟着变。
- 排名：两辆车都在圈外，血量不同，第 0 圈之后血少的先死 → 名次在后；最后一辆活着的赢（`Eliminated`）。
- `Allocation.AssertNone`：预热后 300 个 tick 的 `Step` + `Update`（跨过一个整秒、有车在圈外掉血）不分配。

**先写的测试**（`MatchStatsTests`）

- 伤害：A 打中 B 一发穿甲（`Match` 帮手的 `Attack`），A 的 `Damage` 等于那发的伤害；B 只剩 50 血时再挨一发 300 的 → A 只加 50；自己的 HE 溅到自己、团队战里打队友 → 不加；毒圈掉的血谁的 `Damage` 都不加。
- `Entrants`：各自为战 3 人开局 → 3；团队战开局 2 人、中途进 1 人 → 3，离开一个仍是 3。
- `LastedTicks` 同任务 1 的规则，配毒圈击毁再测一次。

**步骤**：同任务 1。

**提交**：`feat(sim): the zone hurts — damage outside the circle each second, zone kills credited to no one, damage dealt and time lasted`，末行 `Tests: Sim N → M.`

**批次 A 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 B：联网

### Task 4：协议 v5

**Files:**
- Modify: `src/Tank.Net/Battle/BattleProtocol.cs`、`BattleSnapshot.cs`、`BattleSnapshotCodec.cs`、`BattleMessages.cs`、`BattleClient.cs`（`OnData` 的分派加 `BattleMessage.Zone`）、`BattleClient.Match.cs`
- Test: `tests/Tank.Net.Tests/Battle/RoyaleWireTests.cs`、`ZoneClientTests.cs`（用 `BattleTestPackets` / `FakeServerTransport` 直接喂包，照现有的客户端健壮性测试）；`StandardWireTests` 第 23 行的版本断言挪进 `RoyaleWireTests`（`== 5`）

**规则**：见“共享接口”的线上格式和客户端的圈。另外：

- `BattleProtocol.Version` 的注释写 5 改了什么（照 4 的写法）。
- `PlayerStats.Equals`/`GetHashCode` 带上 `Damage`、`Lasted`；`MatchBlock` 带上 `Entrants`。
- `ZoneMessage` 的读写和别的消息放在一起（`Write(BitWriter, ZoneMessage)`、`ReadZone(ref BitReader)`）；客户端处理时 `ProtocolException` 照现有的消息一样记进 `LastPacketError`、不崩。
- `BattleClient.ZoneAt(serverTick)`：`HasZone` 假 → `default`；否则 `ZoneState.At(ZoneRings, (float)(serverTick − ZoneStartTick))`。
- 服务器这一步还不发圈（任务 5）；服务器照旧能编译、原有测试照过。

**先写的测试**（`RoyaleWireTests`）

- `Version == 5`、`BattleMessage.Zone == 26`、`MaxPlayers == 16`。
- 快照往返：16 个座位都有人，名次 1～16、座位 7 的 `Damage = 12345`、`Lasted = 539`、`Entrants = 16`；名次 0 和 16 往返（B 批跟进：原先测名次 31 往返，现在 17～31 读写都拒）；没变的计分板走“相同”位；手工改坏 `Entrants = 17` → `ProtocolException`。
- 事件往返：`TankDestroyed` 带 `Cause Zone`；其他事件 `Cause Shell`；手工把原因位写成 2、3 → `ProtocolException`。
- `ZoneMessage` 往返；`Ring` 0、6 → 读时抛；半径 NaN、−1、圆心 2e5 m → 读时抛。
- 开局消息：带 `ZoneSync(StartTick 120, 3 个圈)` 往返（用试驾场地图，也走 `Write(w, m, encodedMap)` 那条路一次）；不带 → `Zone == null`；`Rings` 1 个或 7 个 → 写时 `ArgumentException` 且 writer 里什么都没写；手工圈数字节 7 → 读时抛。
- 局域网答复：`MaxPlayers = 16` 读得出，17 抛。

**先写的测试**（`ZoneClientTests`）

- 开局消息带 r0、r1 → `HasZone`、`ZoneRings.Length == 2`、`ZoneAt(StartTick + 2700).Phase == Shrinking`；`ZoneMessage` 第 2 圈 → 长度 3；再来一次第 2 圈 → 仍是 3；第 4 圈（缺第 3 圈）→ 仍是 3；别的对局号 → 不变；同一局的重同步开局消息带 4 个圈 → 长度 4；新的一局不带圈 → `HasZone` 假；离开对局 → 清空。
- `Allocation.AssertNone`：预热后 `ZoneAt` 1000 次。

**步骤**：同任务 1。

**提交**：`feat(net): protocol v5 — 16 seats on the wire, zone announcements, the zone in the match start, kill cause, damage and time lasted`，末行 `Tests: Net N → M.`

### Task 5：服务器的吃鸡房间和对局

**Files:**
- Modify: `src/Tank.Net/Battle/BattleServer.cs`、`BattleServer.Room.cs`、`BattleServer.Match.cs`、`src/Tank.Server/Program.cs`、`src/Tank.Client/scripts/UI/Logic/RoomRules.cs`、`tests/Tank.Soak/BattleSoak.cs`
- Test: `tests/Tank.Net.Tests/Battle/RoyaleServerTests.cs`；遍历 `ModeRules.All` 并在试驾场上开局的现有测试（`grep -rn "ModeRules.All" tests/`）跳过 `!ModeRules.AllowsMap(mode, map)` 的组合或给吃鸡换河谷；Art `RoomRulesTests` 的交叉测试（第 85 行起）覆盖五种模式 × 两张图

**规则**

- （任务 1 已做：`WriteRoomInfo` 的 `MaxPlayersFor(_mode)`、补机器人循环上限 `MaxPlayersFor(_mode)`、`RoomRules.PlanFor` 的 `MaxPlayersFor(room.Mode)`。）
- （A 批跟进已做，本任务不再改：构造 `_bots = Clamp(options.Bots, 0, MaxBotsFor(options.Mode))`；`SetBots` 夹到 `MaxBotsFor(_mode)`；`SetMode` 换了模式后 `_bots = Min(_bots, MaxBotsFor(新模式))`；`AllocateSeat` 在已占的座位 ≥ `MaxPlayersFor(_mode)` 时返回 null（“房间已满。”）——数的是所有占着的座位：真人（连着或在重连宽限里）和对局中的机器人；房间里没有机器人入座，就是真人数；对局中的 10 人模式因此不会有第 11 辆车，和以前 10 个座位时一样。测试在 `tests/Tank.Net.Tests/Battle/ModeSeatTests.cs`：四种 10 人模式第 11 个真人被拒、吃鸡第 17 个被拒、1 真人 + 9 机器人的团队战对局里后来的人被拒、团队战选项 `Bots = 15` → 9、`SetBots(15)` → 9、吃鸡换各自为战 → 9。）
- 构造：`CheckMaps` 加 `!ModeRules.AllowsMap(options.Mode, options.Map.Id)` → `ArgumentException("royale is played on the riverland only", nameof(options))`。
- `AllocateSeat`：见上（已做）。座位数组是 16，10 人模式第 11 个真人进不来（偏差 22）。已有令牌的重连不受影响（`SeatWithToken` 在前）。
- `SetMode`：解析成功后，真人数 > `MaxPlayersFor(新模式)` → 不换（`_log($"can't switch to {id}: {n} players")`）；新模式有 `OnlyMap` → `FindMap(它)` 为 null 时不换，否则 `_maps.Prepare(map)`、`_roomMap = map`；（换了模式后压机器人数已做。）
- `SetBots`：（已做。）`SetMap`：`!AllowsMap(_mode, map.Id)` 时不换。
- `TryStartMatch`：地图状态检查之后、基地检查之前：`!AllowsMap(_mode, _roomMap.Id)` → `reason = "royale is played on the riverland only"`；补机器人的循环上限 `MaxPlayersFor(_mode)`；导航格只拷一份：有机器人或吃鸡时 `map.Definition.NewNavGrid()`，交给 `ZonePicker` 和 `SeatBots`（`SeatBots` 加 `NavGrid grid` 参数，没有机器人时照旧返回 null）；吃鸡：`ZonePicker.Pick(sim.Field, grid, map.Definition.Spawns.Solo, seating.MatchSeed ^ ZonePicker.SeedSalt)`（在加车之前）；`new MatchRules(..., seed: seating.MatchSeed, zone: rings)`；日志 `zone: ring 1 (x, z) r 420, ring 2 …`（五个圈，整数米；只在服务器日志里，验收用）。B 批跟进：拷格子、挑圈、建规则（它检查圈）放在同一个 `try` 里，任何一步抛 → `RemoveBots()`、日志 `match not started: <哪一步> failed: …`、`reason = "<哪一步> failed to start"`（吃鸡里挑圈和建规则都算 `the zone`），返回 false，房间照旧；测试钩子 `internal Func<Battlefield, NavGrid, IReadOnlyList<SpawnPoint>, uint, ZoneRing[]>? ZonePickerOverride`（同 `DirectorMade`：手工给圈，或抛 / 给规则不收的圈表示挑圈失败）。
- 公布：字段 `_zoneSent`，开局时 = `rules.KnownRings`（2）。`TickMatch` 在 `rules.Update()` 之后、发事件和快照之前：`while (_zoneSent < rules.KnownRings)` → 给每个 `InMatch` 且连着的座位可靠地发 `ZoneMessage(_matchNumber, (byte)_zoneSent, rules.ZoneRings[_zoneSent])`，日志 `zone ring {k} announced: (x, z) r R`，`_zoneSent++`。每个 tick 的路径不分配（`_writer` 复用）。
- `SendMatchStart`：吃鸡时 `Zone = new ZoneSync(rules.StartTick, rules.AnnouncedRings.ToArray())`（开局和重同步时分配，可以）。
- `ViewerOf`：`!ModeRules.HasTeams(_mode)`。
- 快照：`MatchBlock.Entrants` = 有毒圈的模式里 `min(rules.Entrants, MaxPlayers)`，其他模式 0（B 批跟进，偏差 24）；计分板每行 `Damage = playing && line.Team != viewer.Team ? 0 : p.Damage`（对手的伤害赛后才给，同占领：伤害一涨就告诉别人谁在交火），`Lasted = (!playing || (!p.Alive && !ModeRules.Respawns(mode))) ? rules.LastedTicks(slot) / TickRate : 0`（活着的人对局中是 0，计分板不会每秒变；B 批跟进：会复活的模式里等复活的车对局中也是 0，原先发“开局到现在”，每秒变一次，soak 手机那行快照 101 → 149 B）。
- `WriteRoomInfo`：`MaxPlayers = MaxPlayersFor(_mode)`。
- `Program`：用法 `--mode teambattle|annihilation|ffa|standard|royale`、`--bots: 0 to 15`（`BattleProtocol.MaxPlayers − 1`，开局时再按模式夹）；解析完 `!AllowsMap(Mode, Map.Id)` → `ArgumentException("--mode royale is played on the riverland only (--map riverland)")`，主函数照现有的写法打印错误和用法、返回 2。
- `RoomRules`：见“共享接口”。注释照旧写“和 `TryStartMatch` 同样的检查”。
- Soak：`Scenario` 加可选的 `BattleMode Mode = BattleMode.TeamBattle, int Bots = 4, bool Riverland = false`；在四行之后加一行 `new Scenario("royale wifi 30±20ms 5% loss", new LinkConditions(30, 20, 5), 2f, Mode: BattleMode.Royale, Bots: 12, Riverland: true)`（`Map = MapCatalog.Riverland, Maps = new[] { MapCatalog.Riverland }`；河谷在建服务器时同步生成，计时从对局开始后才算，照旧）；表头那行说明写清最后一行是吃鸡、16 辆车。原有四行的配置一个字都不改。

**先写的测试**（`RoyaleServerTests`，`BattleHarness`，`Maps = MapCatalog.All`，`Map = MapCatalog.Riverland`）

- 座位：（A 批跟进的 `ModeSeatTests` 已测，不重复。）
- 换模式：试驾场地图的团队战房间，房主 `SetMode(Royale)` → 房间状态 `Mode == Royale`、`Map == "riverland"`；`SetMap("sandbox")` 被忽略；`SetBots(15)` → 15；吃鸡房间 11 个真人时 `SetMode(TeamBattle)` 被忽略；服务器的地图表里没有河谷时 `SetMode(Royale)` 被忽略。
- 选项：`new BattleServer(..., new BattleServerOptions { Mode = Royale, Map = MapCatalog.Sandbox })` 抛 `ArgumentException`（`Bots = 15` 的团队战选项 → 9 已测）。
- 开局：1 个真人 + 15 个机器人的吃鸡（种子 3）→ 16 辆车、16 个不同的出生点；客户端 `HasZone`、`ZoneRings.Length == 2`、`ZoneStartTick == Server.Rules.StartTick`、圈和 `Server.Rules.ZoneRings` 的前两个逐个相等；日志里有 `zone: ring 1`。
- 公布：2 个真人、没有机器人的吃鸡跑到开局后 180.2 s → 两个客户端 `ZoneRings.Length == 3`，第 3 个等于服务器的 `ZoneRings[2]`；日志 `zone ring 2 announced`；这时一个客户端断开重连（照 `BattleServerTests` 里重连的写法）→ 开局消息带 3 个圈。
- 计分板：吃鸡里 A 打中 B（照 `StandardServerTests` 里让一方开炮打中另一方的写法），对局中 A 看自己的 `Damage` > 0、B 看 A 的 `Damage == 0`；对局结束后 B 看 A 的是真数；`Entrants`；阵亡的人 `Lasted` > 0、活着的 0。（实际写法：河谷的出生点相隔几百米、炮打不着，两个人一死一局就结束、看不到“阵亡的有、活着的 0”，所以这两条用测试地图 `TestMaps.RoyaleStandIn()`——带墙的小图、id 用 `riverland`、三个各自为战出生点：墙西两个相距 30 m、墙东一个——三个真人、`MatchSeconds = 75` 让对局按时结束，`Entrants == 3`。其余几条用真河谷。）
- 点亮：吃鸡里死了的人看得见所有车（`Viewer` 的各自为战规则）：同一张测试地图，B 生前看不见墙东的 C，阵亡后快照里三辆车都在。
- （另加）`BattleServerTests`：吃鸡第 1 圈缩小时服务器每 tick 0 分配。
- 局域网答复：团队战房间 `MaxPlayers == 10`，吃鸡 16。
- 原有四种模式：`BotLoadTests`、`StandardServerTests`、`RiverlandMatchTests` 照过（不改）。
- Art `RoomRulesTests`：交叉测试五种模式 × 两张图，服务器拒绝和客户端说的原因一一对应（吃鸡 + 试驾场地图：服务器构造就拒，测试从河谷开吃鸡房间再看 `StartBlockReason` 为 null；手工造 `RoomStateMessage(Royale, …, Map: "sandbox")` → 原因是“吃鸡只在河谷开：换到河谷。”）；`PlanFor`：吃鸡 1 人 + 机器人 15 → 15 个，团队战 1 人 + 机器人 15（旧房间状态）→ 9 个。

**Soak**：`dotnet run --project tests/Tank.Soak -c Release`，原有四行的修正、误差、下行和以前同量级（和 5D 验收时的输出比，记进度）；吃鸡一行的每客户端下行最大 KB/s、快照字节、tick p50/p99 记进度。下行超过 20 KB/s 或 p99 超预算就是失败：先看是哪部分（快照里的实体数、计分板 16 行），在本任务里修，修不下来记进度里写清，并在任务 6 的实测里一起看。

**步骤**：同任务 1；另跑 soak。

**提交**：`feat(server): royale rooms and matches — per-mode seat caps, riverland only, rings drawn at the start and announced on time`，末行 `Tests: Net N → M, Art K → L.`

**批次 B 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 C：机器人

### Task 6：跑圈和整局

**Files:**
- Create: `src/Tank.Sim/Ai/BotDirector.Zone.cs`、`src/Tank.Sim/Ai/Bot.Zone.cs`
- Modify: `src/Tank.Sim/Ai/BotDirector.cs`（`Update` 里取 `Zone`、`Summary`）、`Bot.cs`（`Leg.Zone`、`Command` 里的 `WatchZone` 和 `onTheWay`、`Move`/`CheckArrival`/`GiveUpLeg`/`Begin` 认 `Leg.Zone`、`NextWaypoint` 在吃鸡里走过滤）、`tests/Tank.Sim.Tests/BotArena.cs`（构造加 `uint seed = 0, IReadOnlyList<ZoneRing>? zone = null`，交给 `MatchRules`；构造里的格子先建（`Map.NewNavGrid()`），再建规则和导演，导演用同一份格子；静态 `BotArena.Royale(MapDefinition map, uint seed)`：先 `Map.NewBattlefield()` 和 `Map.NewNavGrid()` 挑圈 `ZonePicker.Pick(field, grid, Map.Spawns.Solo, seed ^ ZonePicker.SeedSalt)`，再用这份场地和格子建竞技场——构造需要的话加一个接收现成场地和格子的私有重载——和服务器一样在加车之前挑好）
- Test: `tests/Tank.Sim.Tests/BotZoneTests.cs`、`RoyaleMatch.cs`（照 `WholeMatch.cs`）、`RoyaleMatchTests.cs`、`VisionLoadTests.cs`；`tests/Tank.Net.Tests/BotLoadTests.cs` 加一条

**规则**（设计 §3.4；吃鸡里所有机器人都是 `Wander`，在各自为战的规则上加这些）

- `BotDirector.Update`：`_royale = ModeRules.HasZone(Rules.Mode)`；每 tick `Zone = Rules.Zone`（不分配）。`Zone.Phase == None` 时下面都不做。
- 目标圈 `T = Zone.Target`；“在圈里” = `|me − T.Centre| ≤ max(T.Radius, ZoneArriveRadius)`（C 批跟进：原 `ZoneHoldRadius` 8 m，偏差 26）。
- `Bot.WatchZone`（每个 `Command` 里、`CheckArrival` 之前）：
  1. 记下看到的 `Zone.Ring`。圈号变了：在跑 → 换成新圈里的点（`ZonePoint(..., attempt: 0)`）重新寻路；在巡游、目标路点不在 `T` 的 `ZoneInsideShare` 里 → 换路点。
  2. 不在圈里、也没在跑 → 开跑：`RunningToZone = true`、`ZoneRuns++`、`Begin(Leg.Zone, ZonePoint(this, T, 0))`。
  3. 在跑、离圈心 ≤ `ZoneInsideShare × T.Radius`（或到了点，见 `CheckArrival`）→ 不跑了：`T.Radius < ZoneWanderRadius` → `Begin(Leg.Arrived, …)` 原地守（照样交战）；否则 `NextWaypoint()`（过滤后的）。
  4. `ZoneUrgent = Zone.IsOutside(me) || (RunningToZone && (ZoneUrgent || MustRun(剩下的路程, spec.MaxForwardSpeed, Zone.TicksToTarget, false)))`（C 批跟进：一次急了就急到这次跑完，不在“停下打”和“跑”之间来回切）；剩下的路程量到这次跑**停下的圆**（`InsideLimit`，0.8R，最后一圈 20 m），不是量到更里面的点（C 批跟进）：手里有到这个点的整条路时从车沿路走到第一次进那个圆为止的折线长（`ToCircle`），否则到圆的直线距离 × `ZoneDetour`。`MustRun` 的估计：`ceil(路程 / (topSpeed × ZoneSpeedShare) × TickRate) + ZoneMarginTicks ≥ ticksToTarget`。
- `Command` 里：`onTheWay = (FallingBack || Retreating || ZoneUrgent) && _leg is Leg.Waypoint or Leg.Via or Leg.End or Leg.Zone`——有目标时边走边打（`FireOnTheMove`），不停下，不倒车躲近敌（设计“剩余时间不够时放下交战先跑，被打时照样还击”“在圈外时不追圈外的敌人”，见偏差 10）。不急的时候照旧停下交战，打完接着跑。
- `ZonePoint(bot, T, attempt)`：`θ = (bot.TankId + attempt) × GoldenAngle`，`p = T.Centre + (cos θ, sin θ) × ZoneRunShare × max(T.Radius, ZoneArriveRadius)`（实现时改：原文乘 `T.Radius`，最后一圈半径 0 时所有机器人的点都落在同一个圆心上挤成一团；按编号散开，C 批跟进后离圆心 10 m）；`Grid.TryNearestOpen(p, ZoneSnap)` 找到且 `WellInside`（离圈心 ≤ `InsideLimit`）→ 用那个格心；否则试圈心的格；都不行用 `p`。
- `Leg.Zone`：像 `Leg.End` 一样开到点上停下（`Move` 里 `stopThere` 认它，`Ask` 认它）；`CheckArrival`：`Reached(me, WaypointRadius)` 或 `StoppedAt(Goal)` → 第 3 条（实现时补：到了点却不在圈里——点没吸附上、落在圈外——当作没到，换下一个点；巡游的随机点到了 → 下一个路点）；`GiveUpLeg`：`attempt + 1` 换一个点（最多 8 次；C 批跟进：之后原地守 `ZoneRetryTicks` 再从第 0 个点重来，原来守到下一次圈号变——最后一圈之后没有下一圈，探针里第 5 圈结束时 4～8 辆守在圈外；第 2 条再开跑时用当前的 attempt，换圈时归 0）。
- 寻路找不到路（实现时加，整局探针里看出来的）：吃鸡里 `Leg.Zone` 和巡游的 `Leg.Waypoint` 找不到路时不再等 5 s 重问、三次才放弃，而是立刻 `GiveUpLeg` 换下一个点，`ZoneRetryTicks`（1 s）后再问——圈里哪个点都行，毒圈不等人。挤在小圈里时，站着不动的车（`PathService` 的圆盘）常把目标点围住，原来两次“找不到”就是 10 s 以上原地等（无弹探针种子 1 卡 17 s）。（C 批跟进）在跑时找不到路就先原地守 `ZoneRetryTicks` 再试下一个点；车不在开阔地上（自己的格关着，或周围 8 格有关着的）时先直开到 4 格内最近的开阔格（自己和周围 8 格都开着）再问（`LeaveClosedGround`）；站在关着的格上时搜索从 12 m 内**同一高度**（±2 m）最近的开着的格开始（`SearchStart`，原来吸附到最近的开着的格，在崖脚常常是崖顶）。
- （C 批跟进，偏差 27）吃鸡里没有队友，5C 的让队友规则一条都不起作用：现在吃鸡里机器人绕开**所有**车（`AroundMates` 对每辆车都弯路、让路）；原地守着的机器人被一辆想开过去的机器人（在开、等路或倒车，目标在它身后）贴上（两圆相距 < 1 m）时沿车身前后挪开（`MakeRoom`），挪完在新地方接着守；卡住时倒车的转向先和卡住时正在转的方向相反（挡住的就是那个方向），同一处再卡就换另一边（`ReverseSteer`）；巡游拿到的路只要有一点出了要去的圈（`InRing`）就不要，换一个点（连续 8 次就原地守到换圈：`WanderLeavesRing`）。代码在 `Bot.Crowd.cs`，都只在吃鸡里（`ZoneOn`）起作用。
- 吃鸡里巡游时目标圈小于 `ZoneWanderRadius`（实现时补）：`NextWaypoint` 直接原地守（`Leg.Arrived`），不抽路点也不抽随机点——否则在最后一圈里每 tick 换一个“到了”的随机点。
- 巡游的路点（吃鸡）：`NextWaypoint` 在 `Map.Waypoints` 里用机器人自己的随机数抽，最多抽 8 次，要离 `T.Centre` ≤ `ZoneInsideShare × T.Radius`、不是现在这个；抽不到 → `Begin(Leg.Zone, 圈里随机一点)`（`T.Centre + 方向(随机角) × 随机数 × 0.6 × T.Radius`，吸附同上），`RunningToZone` 不变（假）。巡游的圈是正在去的圈 `T`，等待段也是（偏差 9）。
- 撤退：吃鸡没有队友和基地，`RetreatTarget` 本来就返回 false，不改。
- `Summary()` 在吃鸡里前面加 `"{跑圈次数} zone runs; "`。
- 确定性：只用机器人自己的随机数、`TankId`、仿真状态；按坦克 id 的顺序（照旧）。不分配。

**先写的测试**（`BotZoneTests`，`BotArena` 的 400 m 平地，吃鸡，测试圈：r0 = ((200, 200), 283)、r1 = ((300, 300), 80)、r2 = ((310, 300), 50)、r3 = ((320, 300), 30)、r4 = ((320, 300), 15)、r5 = ((320, 300), 0)；`TankRef` 在第一个 tick 前把车挪到要的地方）

- `MustRun`：100 m、10 m/s（有效 6 m/s，100 m 要 17 s = 500 tick，加 450）→ `ticksToTarget` 900 真、1000 假；`outsideCurrent` 真时总是真。
- 跑圈：一个机器人在 (60, 60)（r1 外），开局 2 s 内 `RunningToZone`、`Goal` 离 r1 圆心 ≤ 0.8 × 80；到 elapsed 5400（第 1 圈缩完）时它在 r1 里。
- 散开：4 个机器人都在 r1 外的一角，各自的 `Goal` 两两相距 ≥ 10 m。
- 圈里不跑：一个机器人开局在 r1 里 → 从不 `RunningToZone`（到 elapsed 5400 为止），它巡游的 `Goal` 都在 r1 的 0.8 里（平地的 9 个路点里在 r1 0.8 以内的不够两个时，走圈里随机点）。
- 急的时候边跑边打：机器人在 r0 外（圈外，`IsOutside`），一个敌人在 60 m 外看得见 → 它一直在开（`Activity == Driving`，从不 `Engaging`），炮口对着敌人，装好弹、瞄准好时开炮（20 s 内至少一发 `ShellFired`）。（实现时改：上面的 r0 半径 283 m 套住了整个 400 m 平地——角上离圆心也只有 269 m——没有“r0 外”；这条用自己的一组圈，第 0 圈 ((330, 330), 40)，往里同心缩，机器人从 (30, 150) 出发，前 10 s 以上在安全区外。）
- 不急时照旧交战：机器人在 r1 外 30 m、时间还早（elapsed 0），敌人看得见 → 停下 `Engaging`。
- 闭合：elapsed 16200 后的机器人都往圆心去（`Goal` 离 r5 圆心 ≤ `ZoneHoldRadius`），到了就守着不来回抖（到达后 2 s 里 `Goal` 变化 ≤ 1 次、`ZoneRuns` 不再涨）。（实现时改：从第 5 圈开始缩的那一刻看——那时 4 个机器人都已在圆心 8 m 内守着——再看 2 s，再查到闭合后 2 s 活着的都往圆心去。按原文从 16200 看是空的：这组圈的第 4 圈只有 15 m，离圆心 4 m 以外的车在第 5 圈收缩时有 10 s 以上在圈外、每秒掉 10%，闭合前就都死了。4 个机器人没有弹，只看跑圈。C 批跟进：到达半径改成 `ZoneArriveRadius` 20 m，测试圈的第 4 圈改成 25 m（0.8 × 25 = 20，停在 20 m 的车仍在第 4 圈里）；查的是第 5 圈收缩时都在圆心 20 m 内守着，到闭合后 2 s 一直守着、不再开跑。）
- （实现时加）`Fifteen_unarmed_bots_run_every_ring_of_a_royale`：带弹的整局在第 2 圈缩完之前就打完了（见下面的数），后面的圈整局里跑不到；所以再各打一局“机器人没有弹”的，只有毒圈能淘汰：到闭合附近结束、第 1～4 圈缩完时圈外活着的机器人 ≤ 1、395 s 前没有机器人死于毒圈、最长卡住 < 10 s。（C 批跟进）种子改成 7、8、11、19（8 是三辆车在最后一圈互等寻路 42 s 的那局；3 拿掉，见偏差 27）；悬崖脚下的陷阱修好后 3 放回：3、7、8、11、19（偏差 28）。
- （C 批跟进）`Fifteen_tough_bots_fight_through_every_ring_of_a_royale`（种子 3、7、8、11、19）：带弹、每辆车血量 ×50（`RoyaleMatch.ToughHealth`；毒圈按最大血量的百分比扣，照样杀），炮弹几乎打不死，整局打到第 5 圈：第 1～4 圈缩完时圈外 0 辆、第 1 名活着或最后一个死于毒圈、最长卡住 < 10 s、炮弹命中 ≥ 20、第 2～5 圈里有机器人变急（`ZoneUrgent` 由假变真）且其中有正在停下交战的（“放下交战先跑”真的发生）。带弹整局的第 1～4 圈：比赛在那一圈缩完之前就结束了时明说跳过（打印出来、并查结束时间确实在那之前），不再拿 −1 去比 ≤ 1。
- （C 批跟进）`BotZoneTests` 加：最后一圈的点一个都到不了（被六辆不动的车围住）时试完 8 个点会重来，围栏一开 10 s 内进圈（原来守到下一圈，最后一圈之后就永远守在圈外）；最后一圈圆心挤满时从 32 m 外来的车在 `ZoneArriveRadius` 里停下守着；跑圈的到达时间量到停下的圆、一旦急了就急到底。`BotCrowdTests`（新）：吃鸡里绕开迎面来的任何车、守着的车给贴上来的机器人让路、四周格都关着时开向开阔地、崖脚从同一高度搜索、巡游不出圈、卡在转向时倒车反着转。每条都先确认去掉对应改动会失败。
- `Allocation.AssertNone`：4 个机器人的吃鸡，预热后 300 个 tick（跨过开跑、换圈）不分配。

**先写的测试**（`RoyaleMatch` + `RoyaleMatchTests`，河谷，照服务器开局：座位 0 是不动的真人（中坦，命令全 0），座位 1～15 是机器人（`BotSeating(seed)` 抽车型、`BotSeed(slot)`），`BotArena.Royale`，打到结束或 10 分钟）

- 种子 3、7、11、19（`[Theory]`），每局：`EndReason == Eliminated`（不是到点）；结束时 elapsed ≤ `ClosedAt + 20 s`；`WinnerSlot != NoSlot`；最长卡住 < 10 s（`BotArena.LongestStuckSeconds`，同 5C 的 `StuckLimit`）；炮弹命中 ≥ 20、炮弹击毁 ≥ 6（机器人真的在打）；**elapsed 395 s（第 3 圈缩完）之前没有机器人死于毒圈**（会跑圈）；第 1～4 圈各自缩完的那个 tick，圈外活着的机器人 ≤ 1（C 批跟进：比赛已在那之前结束的圈明说跳过）。
- 四局里至少三局结束时第 1 名还活着（“有人吃鸡”；另一种是最后几辆同一秒死于毒圈，名次照规则给，见偏差 3）。
- 同一种子打两遍：结束 tick、第 1 名、事件数逐个相同。
- `Allocation.AssertNone`：种子 3 的一局，从 elapsed 2700 起（第 1 圈在缩、有车在圈外）300 个 tick 不分配。
- 打印每局：结束时间、第 1 名座位和车型、毒圈击毁数、炮弹击毁数、各圈缩完时圈外的车、导演 `Summary()`；写进进度。时间表要调（例如总是到点、或第 1 圈缩完时一堆机器人还在圈外）就改 `ZoneSchedule` 并记进偏差 18。

**开销实测**（都在 `Timing` 集合）

- `VisionLoadTests`（Sim）：河谷上 16 辆车（16 个出生点，开局后让它们按 `BotArena` 跑 60 s 打散），每 tick `VisionTracker.Update` 的用时 p50/p99/最大，和 10 辆车的同一测量放在一起打印（点亮的开销随车数平方涨，记下比例）；Debug 下只查 < 20 ms 的宽松上限。
- `BotLoadTests`（Net）加 `Fifteen_bots_in_a_royale_on_the_riverland_keep_the_server_tick_p99_under_2_ms`：照九个机器人那条，1 个不动的客户端 + 15 个机器人，`Mode` 吃鸡，窗口到对局结束或 300 s；另外打印客户端 `Stats.SnapshotBytes` 的平均和最大、`BytesInPerSecond` 的最大（C 批跟进：分开记活着时和被击毁后——各自为战里死了的人看全场 16 辆车，是发得最多的座位）；Release 下 p99 < 2 ms（Debug 10 ms）。超了：先用 riverland-probe 的 `match … --time` 的办法（或在测试里分段计时）看是点亮、寻路还是规则，在本任务里优化到预算内；优化不下来，在进度里写清实测数和瓶颈，提交说明里也写，批次审查时决定。

**命令**：`dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~RoyaleMatch|FullyQualifiedName~VisionLoad" --logger "console;verbosity=detailed"`；`dotnet test tests/Tank.Net.Tests -c Release --filter "FullyQualifiedName~BotLoad" --logger "console;verbosity=detailed"`

**步骤**

- [ ] 写 `BotZoneTests`，确认失败
- [ ] 实现跑圈
- [ ] 写整局和开销的测试，跑 Release 看数字；需要时调时间表 / 参数
- [ ] 五个项目 0 警告；四个测试项目全过（5C 的标准战整局测试照过：吃鸡以外机器人的行为不变）
- [ ] 提交

**提交**：`feat(bots): bots run the zone — points spread in the ring, wandering inside it, firing on the move when time is short; a 1 + 15 royale played through`，末行 `Tests: Sim N → M, Net K → L.`

**批次 C 收尾**：两路审查（规格审查对照设计 §3.4、§6 的整局一条）→ 跟进提交 → 进度（整局和开销的数字）。

---

## 批次 D：客户端

### Task 7：房间和启动参数

**Files:**
- Modify: `src/Tank.Client/scripts/UI/Logic/RoomModel.cs`、`scripts/UI/RoomScreen.cs`、`scripts/Bootstrap/LaunchOptions.cs`、`scripts/Bootstrap/Main.Flow.cs`
- Test: `tests/Tank.Art.Tests/RoomModelTests.cs`（或现有的房间模型测试文件）、`LaunchOptionsTests` 加用例

**规则**

- `RoomModel` 按“共享接口”。`Blocked` 的人数是房间里的真人（`!IsBot`，掉线的也占座）；当前模式自己从不 blocked。签名（`Signature`）里加 `MaxBots`、每个模式的 `Blocked`、`MapHint`、列数。
- `RoomScreen`：
  - 模式下拉框：每次 `Apply` 时对每一项 `SetItemDisabled(i, choice.Blocked is not null)`、`SetItemTooltip(i, choice.Blocked ?? "")`；房主看得到的提示行（`_hint`）在有 blocked 的模式时不额外说，提示放在悬停里（设计“按钮给出原因”）。
  - 地图下拉框：`Disabled = !m.CanSetMap`；`MapHint` 不为 null 时在下拉框下面加一行 14 号 `UiKit.Dim` 字。
  - 机器人下拉框：一次建 0～`BattleProtocol.MaxPlayers − 1`（0～15）项；`Apply` 时 `SetItemDisabled(n, n > m.MaxBots)`，选中项夹到 `MaxBots`。
  - 两列：照 `BuildColumn` 建两个 `ColumnSide.Everyone` 的列，第二列标题为空时不画标题行（留同样的高度，两列顶端对齐）。
- `LaunchOptions`：帮助文字 `--mode … |royale`、`--players N` 的“最多 16”（`BattleProtocol.MaxPlayers`）；`HostMap` 和警告按“共享接口”（`--host` 时才警告）；`--shot-after outside:S`。`Main.Flow.Host` 用 `_options.HostMap`。
- 注意 `AutoRoom` 先发 `SetMode` 再发 `SetBots`（现在就是这个顺序）：先换到吃鸡，15 个机器人才不会被压成 9。

**先写的测试**

- `RoomModel`：吃鸡房间、房主、1 个真人、机器人 15 → `MaxBots == 15`、`CanSetMap` 假、`MapHint == "吃鸡只在河谷开"`，列表是真人 1 行 + “机器人 × 15 开局时补入” 1 行，一列；9 个真人 + 机器人 7 → 10 行 → 两列，5 + 5，第一列标题“玩家 · 16”、第二列标题空；16 个真人 → 8 + 8。吃鸡房间 11 个真人时，`ModeChoices` 里四种老模式的 `Blocked` 是“真人超过 10 个，不能换到团队对战。”这样的句子，吃鸡的是 null。团队战 `MaxBots == 9`、`MapHint` null、`CanSetMap` 真；各自为战 2 行一列、团队模式两列照旧（原有测试不改）。
- `LaunchOptions`：`--host --mode royale --map sandbox` → `HostMap == "riverland"` 且 `Warnings` 有一条；`--host --mode royale` → `"riverland"`；`--host --mode ffa --map sandbox` → `"sandbox"`、没有警告；不给 `--map` → `"riverland"`；`--bots 15` 过、`--bots 16` → 15 加警告；`--shot-after outside:2` 解析得出。

**截图**（`build/shots/6a-t7/`，都要看）：

```bash
$S/godot_run.sh $W "" 40 --host --mode royale --bots 15 --screenshot $W/build/shots/6a-t7/room-royale.png --screenshot-at 8 --quit-after 10
$S/godot_run.sh $W "" 40 --host --mode standard --bots 9 --screenshot $W/build/shots/6a-t7/room-standard.png --screenshot-at 8 --quit-after 10
```

看：吃鸡房间标题“吃鸡 · 河谷”、规则一句、地图下拉框灰掉且下面写“吃鸡只在河谷开”、机器人“15 个机器人”、玩家列（1 人 + 机器人一行，一列）；标准战房间和 5D 一样。另外六张试驾场回归截图和 `base5d` 比。

**步骤**：写测试 → 确认失败 → 实现 → 0 警告、全过 → 截图看过 → 提交。

**提交**：`feat(client): royale in the room — mode list with reasons, the map locked to the riverland, bots up to 15, two columns of players`，末行 `Tests: Art N → M.`

### Task 8：HUD 逻辑

**Files:**
- Create: `src/Tank.Client/scripts/Match/Logic/ZoneHud.cs`、`Match/Logic/ZoneCamera.cs`
- Modify: `Match/Logic/HudText.cs`、`MatchHudFrame.cs`、`KillFeed.cs`、`ScoreboardModel.cs`、`HudLayout.cs`、`HudDemoFrames.cs`、`scripts/Minimap/Logic/MinimapFrame.cs`、`MinimapFeed.cs`、`MinimapStyle.cs`、`scripts/Art/Palette.cs`
- Test: `tests/Tank.Art.Tests/ZoneTextTests.cs`、`ZoneCameraTests.cs`；`HudTextTests`、`KillFeedTests`、`ScoreboardModelTests`、`MatchHudFrameTests`、`HudLayoutTests`、`MinimapFeedTests`、`PaletteTests`、`HudDemoFramesTests` 加用例

**规则**

- `ZoneHud`、`ZoneText`、`HudText` 的吃鸡结算、`ResultsInfo.Entrants`、`FeedLine.Zone`、`KillFeed.AddZone`、`ScoreRow` 的两列、两个单元格、两个方框、`MinimapZone` / `AddZone` / 颜色、`Palette.ZoneWall`、`ZoneCamera` 都按“共享接口”。
- `MatchHudFrame.From(..., ZoneHud zone = default)`：帧的 `Zone = zone`；`ResultsInfo` 带 `match.Entrants`。圈外的判定（自己的车活着、`ZoneAt(…).IsOutside(画出来的位置)`）由场景做（任务 10），`From` 只转交。
- `ScoreboardModel.Build`：`ScoreRow` 带 `p.Damage`、`p.Lasted`；排序不变（吃鸡和各自为战一样：对局中活着的在前，结束后按服务器的名次）。`DamageCell`：`row.Side != Side.Own && !ended` → `""`，否则数字（`ToString(InvariantCulture)`，只在重建时分配）。`LastedCell`：`HudText.Clock(row.Lasted)`。
- `HudText.Plate`：吃鸡走 `default` 分支（`SoloKills`），不改；测试钉住“击毁 3”“存活 12/16”。
- `ZoneCamera.Pose`：`dir = normalize(near − centre)`（重合时 (1, 0)）；`eye = centre + dir × max(0, r − 25)`，高 `groundAt(eye) + 5`；`target = centre + dir × r`，高 `groundAt(target) + 20`。`r < 1` 时 eye 在圆心上方 40 m、target 圆心地面。
- 画廊（`HudDemoFrames`）：`Roster` 扩到 16 个名字（在现有 10 个后面加“机器人 7”～“机器人 12”，已有的屏只用前面的，截图不变）。新五屏（都是 `BattleMode.Royale`、自己座位 0、16 人参赛）：
  - `royale`：对局中 6:15，`Plate` 击毁 3 / 存活 12/16，`ZoneHud(圈 2、等待、45 s)`；击杀栏三行，其中一行 `机器人 9 死于毒圈`；右下角河谷小地图：当前圈 ((560, 640), 420)、下一个圈 ((520, 610), 250)，自己的箭头在两个圈之间、三个敌人。
  - `royale-outside`：`ZoneHud(圈 3、收缩、30 s、Outside、4)`（屏幕泛蓝、“在安全区外 −4%/秒”），小地图当前圈 ((520, 610), 180)、下一个 ((500, 600), 140)，箭头在当前圈外。
  - `royale-map`：同 `royale` 的帧，全屏地图打开。
  - `results-royale-win`：结束，自己名次 1，16 行都有名次、击毁、伤害、存活时间（第 1 名 9:12，最后一名 0:48），`Entrants 16`。
  - `results-royale`：同上，自己名次 7。

**先写的测试**

- `ZoneTextTests`：等待 45 s → “毒圈 2/5 · 0:45 后缩小”；收缩 30 s → “毒圈缩小中 0:30”；闭合 → “毒圈已闭合”；`On` 假 → `""`；`ZoneHud.From`：`TicksLeft 1`（不到一秒）→ `SecondsLeft 1`，`Phase None` → `On` 假；`Outside(2) == "在安全区外 −2%/秒"`，减号是 U+2212。
- `HudTextTests`：吃鸡名次 1 → (“吃鸡！”, “共 16 名”, Win)；名次 7 → (“第 7 名”, “共 16 名”, Neutral)；名次 0 → “对局结束”；各自为战的结算和以前一样（原有测试不改）；吃鸡的比分板 `SoloKills`、`Parts` 是“击毁”“3”“存活”“12/16”。
- `KillFeedTests`：`AddZone("小林", Enemy, now)` 的 `Text == "小林 死于毒圈"`、`Killer` 为 null、`Zone` 真；和普通的“被击毁”行分得开。
- `ScoreboardModelTests`：吃鸡 16 行，对局中自己的伤害格是数字、别人的是空，结束后都是数字；`LastedCell` 552 → “9:12”；结束后按名次排、16 行。
- `MatchHudFrameTests`：`From(..., zone)` 原样带出 `Zone`；回环吃鸡对局的 `Results.Entrants` 等于参赛人数（照现有回环测试的写法，2 人吃鸡 + 机器人 2，跑几秒看 `Entrants == 4`）。
- `HudLayoutTests`：1600×900 下 `ZoneLine == (620, 124, 360, 28)`、`ZoneWarning == (500, 162, 600, 44)`；`ZoneLine` 不碰比分牌、模式标签、击杀栏、F3 面板、状态面板、小地图；`ZoneWarning` 不碰比分牌、模式标签、`ZoneLine`、击杀栏、横幅；2560×900、1600×1200 也成立。
- `MinimapFeedTests`：等待段 → `Zone` 带当前圈和下一个圈；收缩段同；闭合 → 没有下一个；`Phase None` → `Zone` 为 null；`Clear` 后为 null；不分配。
- `PaletteTests`：`ZoneWall` 和天空、水、己方、敌方的颜色都分得开（照现有“分得开”的那几条的阈值）。
- `ZoneCameraTests`：圆心 (600, 600)、半径 420、near (900, 600) → eye 在 (995, 600)、target 在 (1020, 600)，eye 在圈里、target 在圈边上；高度按 `groundAt` 加 5 和 20；near 等于圆心时不出 NaN。
- `HudDemoFramesTests`：`Names` 末尾是五个新名字，已有的名字和顺序不变；五屏都 `TryGet` 得到、`Frame.Mode == Royale`、16 个 `Active`；`royale-outside` 的 `Zone.Outside` 真；两个结算屏的自己名次 1 和 7、`Entrants 16`；`royale`、`royale-outside`、`royale-map` 有河谷小地图、`Zone` 的圈心都在可开车的矩形里。

**步骤**：同任务 7（这个任务不截图：视图在任务 9）。

**提交**：`feat(client): royale HUD logic — zone line and warning text, chicken-dinner results, zone kills in the feed, damage and time lasted, the zone on the minimap`，末行 `Tests: Art N → M.`

### Task 9：HUD 视图和画廊

**Files:**
- Create: `src/Tank.Client/scripts/Match/ZoneLineView.cs`、`Match/ZoneWarningView.cs`、`src/Tank.Client/shaders/zone_vignette.gdshader`
- Modify: `Match/MatchHud.cs`、`Match/ScoreboardView.cs`、`Match/KillFeedView.cs`、`scripts/Minimap/MinimapView.cs`

**规则**

- `ZoneLineView`（`HudPanel` 那样的 `Control`，在 `HudBoxes.ZoneLine`）：`frame.Zone.On` 时画一条圆角深色底（`HudStyle.RoundedBox`，`Palette.UiPanel` 70%）和居中的 18 号描边字 `ZoneText.Line`；收缩段字用 `Palette.ZoneWall`，等待和闭合用 `UiText`；只在 `ZoneHud` 变了时重建字符串、重画。计分板或结算盖着时藏起来（同占领条）。
- `ZoneWarningView`（全屏 `Control`，鼠标穿透）：一个全屏 `ColorRect` 带 `zone_vignette` 着色器（`canvas_item`，颜色 `ZoneWall`，`alpha = strength × smoothstep(0.35, 0.75, length((UV − 0.5) × vec2(aspect, 1)) / aspect 归一)`，四周浓、中间透明，最浓处 0.45），`strength` 在 `Outside` 变真时 0.25 s 内升到 1、变假时降到 0（每帧 `Mathf.MoveToward`，只改 uniform，不改布局）；`Outside` 时在 `HudBoxes.ZoneWarning` 里居中画 28 号描边字 `ZoneText.Outside(percent)`，颜色 `ZoneWall`。结算时藏起来。它在 `MatchHud` 里最先加（在比分牌、击杀栏之下）。
- `MatchHud.Refresh` 把帧交给两个新视图；`ApplyVisibility` 里 `ZoneLine` 跟占领条一样在计分板 / 结算时藏。
- `ScoreboardView`：吃鸡用第三套列 `Royale = new(Kill: 560f, Death: 0f, Capture: 0f, Status: 790f)` 加伤害列 x 660、表头 `名次 名字 车型 击毁 伤害 状态`（结束后最后一列表头“存活”、值 `LastedCell`）；行数 > 10 时用紧凑尺寸（行高 30、正文 20 号、状态 18 号），16 行能放进 `ScoreboardHeight`（56 + 16 × 30 + 14 = 550 ≤ 576），结算表格也放得下（底在页脚基线之上）。其他模式的排版一个像素都不变。
- `KillFeedView`：`line.Zone` 的行画成“受害者名字（阵营色）+ ‘ 死于毒圈’（`ZoneWall`）”，宽度照现有的量法。
- `MinimapView`：`frame.Zone is { } z` 时，在底图之后、基地之前画：1. 圈外压暗：`r ≥ 0.5` 像素时 `DrawArc(圆心, r + D/2, 0, τ, 96, 颜色 ZoneShade × ZoneShadeOpacity, 宽 D)`，D = 方框对角线（一条很宽的弧盖住圈外的整块，`ClipContents` 已开）；`r < 0.5` 时整块 `DrawRect`；2. 当前圈 `DrawArc(…, 宽 ZoneWidth, ZoneLine)`；3. 有下一个圈时 `DrawArc(…, 宽 NextZoneWidth, NextZoneLine)`。圆心半径按 `MinimapArea.ToUnit` 和方框大小换算（半径 × 方框边长 / `Area.Size`）。不分配。

**截图和验收**（`build/shots/6a-t9/`，都要看）：

```bash
for d in royale royale-outside royale-map results-royale-win results-royale; do
  $S/godot_run.sh $W "" 90 --hud-demo $d --screenshot $W/build/shots/6a-t9/$d.png --screenshot-at 3 --quit-after 4; done
for d in standard results-ffa scoreboard minimap; do   # 已有的屏：和 5D 时一样
  $S/godot_run.sh $W "" 90 --hud-demo $d --screenshot $W/build/shots/6a-t9/$d.png --screenshot-at 3 --quit-after 4; done
```

看：比分板“击毁 3 · 6:15 · 存活 12/16”，下面一行“毒圈 2/5 · 0:45 后缩小”；击杀栏有“机器人 9 死于毒圈”；小地图上淡蓝的当前圈（圈外压暗一层）和白色的下一个圈，全屏图同样；`royale-outside` 四周泛蓝、中间偏上“在安全区外 −4%/秒”、毒圈行“毒圈缩小中 0:30”；两张结算：标题“吃鸡！”/“第 7 名”、副标题“共 16 名”、16 行都在、伤害和存活时间两列；已有四屏和 5D 的同名截图（`/Users/murphy/Desktop/dev/BattleCity-stage5d/build/shots/5d-t4/`、`5d-t7/` 里有的）比没变。另外六张试驾场回归截图和 `base5d` 比。

**步骤**：实现 → 0 警告、全过 → 截图看过 → 提交（含新 `.uid`）。

**提交**：`feat(client): royale HUD views — the zone line, the blue tint outside, royale scoreboard and results, zone kills, the circles on the minimap`

### Task 10：光墙和对局接线

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/ZoneWallMesh.cs`、`scripts/World/Logic/ZoneWallShape.cs`、`scripts/World/ZoneWall.cs`、`src/Tank.Client/shaders/zone_wall.gdshader`
- Modify: `scripts/Match/MatchMain.cs`、`MatchMain.Events.cs`、`MatchMain.Camera.cs`、`MatchAutomation.cs`、`scripts/Cameras/CameraPresets.cs`
- Test: `tests/Tank.Art.Tests/ZoneWallMeshTests.cs`、`ZoneWallShapeTests.cs`

**规则**

- `ZoneWallMesh.Build(segments)`：见“共享接口”；三角形两面都有（或单面 + 着色器 `cull_disabled`，二选一，测试照实现写）。
- `ZoneWallShape.SampleBase`：第 k 个点在 `centre + r × (cos 2πk/N, sin 2πk/N)`，值 `groundAt(x, z)`（场景传 `TerrainMesh.VisualHeightAt` 和水面取大的那个，同 `BaseRings` 的贴地法）。
- `zone_wall.gdshader`（`spatial`，`render_mode unshaded, cull_disabled, depth_draw_never, shadows_disabled, blend_add, fog_disabled`；加法混合不用排序，项目不排半透明物体）：uniform `centre`（vec2）、`radius`、`height`、`sink`、`base_heights`（`sampler2D`，256 × 1、`filter_linear`、`repeat_enable`）、`wall_color`（`ZoneWall`）。顶点：`θ = UV.x × τ`，`x = centre.x + radius × cos θ`、`z = centre.y + radius × sin θ`，`y = texture(base_heights, vec2(UV.x, 0.5)).r − sink + UV.y × (height + sink)`，写世界坐标（`world_vertex_coords`）。片元：`fade = 1 − smoothstep(0.0, 1.0, UV.y)`（从下往上淡出）；竖条纹 `stripe = 0.75 + 0.25 × sin(UV.x × τ × 360 − TIME × 0.6)`（缓慢流动）；离镜头近更亮 `near = 1 − smoothstep(30, 250, 到镜头的距离)`；`ALBEDO = wall_color × fade × stripe × (0.35 + 0.65 × near)`（加法混合：颜色就是亮度）。数是起点，按截图调，调了写进进度。
- `ZoneWall`（`Node3D`）：`Build(field)` 建 `MeshInstance3D`（`CastShadow = Off`，`CustomAabb` 盖住整张图和 −50～+300 m 的高度，因为顶点在着色器里挪）、`ShaderMaterial`、`Image`（`Rf`，256 × 1）和 `ImageTexture`，预分配 `float[256]` 和 `byte[1024]`；`Show(circle)`：半径 < `MinRadius` 藏起来；圆心或半径比上次采样时差 > 0.5 m 才重新采样、`Image.SetData` + `ImageTexture.Update`；每帧只设 `centre`、`radius` 两个 uniform（用缓存的 `StringName`）。
- `MatchMain`：
  - `BuildScene` 里 `ModeRules.HasZone(Mode)` 时 `_zoneWall = Add(new ZoneWall())`、`Build(field)`。
  - 每帧（`StepMatch`，在 `_hud.Refresh` 之前）：`ZoneState zone = _client.ZoneAt(_client.Interpolator.RenderTick)`；`_zoneWall?.Show(zone.Current)`；`bool outside = hasTank && !own.IsDestroyed && zone.IsOutside(own.Position)`（画出来的自己）；`ZoneHud.From(zone, outside)` 交给 `MatchHudFrame.From`；第一次 `outside` 真时 `_automation.NotifyOutside(_time)`。
  - 小地图：`ShowMinimap` 里 `MinimapFeed.AddZone(frame, zone)`（在 `AddBases` 之后）。
  - 结算时光墙照旧（场景里的东西），HUD 的毒圈行和泛蓝由 `MatchHud` 藏。
- `MatchMain.Events.FeedKill`：`e.Cause == KillCause.Zone` → `_hud.OnZoneKill(victim, victimSide, _time)`（`MatchHud` 加这个方法，转给 `KillFeed.AddZone`），不走“被击毁”那条。
- `MatchMain.Camera`：`bool freeForAll = !ModeRules.HasTeams(Mode)`（第 158 行：吃鸡死后也能自由视角、没人可跟时自动自由视角）。
- `zone` 镜头预设：`CameraPresets` 把 `ZoneCamera.Preset` 加进河谷的预设名和 `StreamsAroundCamera`；`MatchMain.Camera` 里预设是它时，每帧用 `ZoneCamera.Pose(zone.Current, 自己的位置（没有车时地图中心）, groundAt)` 摆镜头（其他预设照旧走 `CameraPresets.Get`）。试驾场不认这个预设（试驾场没有毒圈）。
- `MatchAutomation`：`--shot-after outside:S` 照 `death` 的写法（`NotifyOutside(time)` 记第一次）。

**先写的测试**

- `ZoneWallMeshTests`：`Build(8)` 有 2 × 9 个顶点，UV.x 从 0 到 1、UV.y 只有 0 和 1，第 0 列和第 8 列位置相同、UV.x 0 和 1；每个三角形的顶点都在单位圆上（x² + z² = 1，容差 1e-5）；`Build(2)` 抛。
- `ZoneWallShapeTests`：平地 `groundAt = 7` → 256 个都是 7；`groundAt = x` 的斜面、圆心 (100, 0)、半径 10 → 第 0 个 110、第 64 个（θ = π/2）100、第 128 个 90；`into` 长度不对抛；不分配。

**截图和验收**（`build/shots/6a-t10/`，都要看、写进进度）：

```bash
G="--host --mode royale --map riverland --bots 15 --autostart --seed 11"
$S/godot_run.sh $W "" 240 $=G --autodrive --fps-log --screenshot $W/build/shots/6a-t10/drive-120.png --screenshot-at 120 --quit-after 130 > build/shots/6a-t10/drive.log
$S/godot_run.sh $W "" 240 $=G --camera-preset zone --screenshot $W/build/shots/6a-t10/wall-near.png --screenshot-at 110 --quit-after 120
$S/godot_run.sh $W "" 240 $=G --camera-preset overview --screenshot $W/build/shots/6a-t10/wall-far.png --screenshot-at 130 --quit-after 140
$S/godot_run.sh $W "" 300 $=G --shot-after outside:2 --screenshot $W/build/shots/6a-t10/outside.png --quit-after 280
$S/godot_run.sh $W "" 240 $=G --autodrive --full-map --screenshot $W/build/shots/6a-t10/full-map.png --screenshot-at 100 --quit-after 110
$S/fpsstats.sh build/shots/6a-t10/drive.log
```

看：光墙近处（竖条纹、从下往上淡出、亮）、远处（整圈看得见、淡）、跟着缩（120 s 时比开局小）；不动的玩家被圈扫过后屏幕泛蓝、“在安全区外 −1%/秒”（第 1 圈 1%）、比分板下“毒圈缩小中 m:ss”；全屏地图的两个圈和画面里的墙对得上；帧率（和 5D 河谷标准战比）；`outside` 这一局没被圈扫到就换个 `--seed`（看服务器日志里第 1 圈的圆心挑一个出生点在圈外的），记下用的种子。另外六张试驾场回归截图和 `base5d` 比（试驾场没有吃鸡，应同量级）。

**步骤**：写两个测试 → 确认失败 → 实现 → 0 警告、全过 → 截图看过 → 提交（含 `.uid`、`.gdshader.uid`）。

**提交**：`feat(client): the zone in the match — the light wall, the HUD's zone and the tint outside, the circles on the minimap, zone kills in the feed`

**批次 D 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 E：收尾

### Task 11：验收

不改代码（发现问题就修，修的提交单独写 `fix(<scope>): …`）。写一个 `accept6a.sh` 放在开发脚本目录（照 `accept5d.sh`）跑下面这些。

- [ ] 五个项目 0 警告；四个测试项目全过（记数字）。
- [ ] 整局（设计 §1 的验收）：`--host --mode royale --map riverland --bots 15 --autostart --autodrive --seed 5 --fps-log --shot-after ended:2 --screenshot …/royale-ended.png --quit-after 700`，服务器日志里 `match ended: royale, slot N, eliminated`、五个 `zone ring k announced`、`bots: … zone runs`；结算截图（第 N 名或吃鸡！、共 16 名、16 行）；帧率汇总。玩家没赢就再跑一局不加 `--autodrive` 的看机器人吃鸡；两局都写进进度。
- [ ] 截图（设计 §6 的 6A 部分）：光墙近处、远处；圈外泛蓝；比分板和毒圈倒计时（等待、收缩、闭合各一张：`--screenshot-at` 60、120、560）；小地图和全屏图上的圈；结算页第 1 名（画廊 `results-royale-win`）和第 N 名（真对局）；击杀栏的“死于毒圈”（真对局的截图里有就用，没有用画廊的）。都看过、写进进度。
- [ ] 观战：不开车的一局，死后 8 秒（`--shot-after death:8`）自动跟随一辆活着的车，Q/E 能切、F 自由视角（吃鸡跟各自为战一样）。
- [ ] 房间：`room-royale` 一张（任务 7 已拍，再拍一次）；房主 + 一个客户端（`pair4b.sh`），客户端看到吃鸡房间、地图锁着。
- [ ] 回归：原有四种模式各开一局短的（`--mode teambattle|annihilation|ffa|standard --map riverland --bots 9 --autostart --autodrive --screenshot-at 30`），截图和 5D 验收时同类的看起来一样（比分板、计分板 10 行的排版没变）；试驾场六张回归截图和 `base5d` 同量级；soak 原有四行的修正和误差和 5D 时同量级，吃鸡一行在预算内。
- [ ] 开销（写进进度）：服务器 tick p50/p99（任务 6 的 `BotLoadTests`，Release）、每客户端下行 KB/s 和快照字节（soak 吃鸡一行、`BotLoadTests` 的打印）、点亮开销（`VisionLoadTests`）、帧率（整局的 `--fps-log`）。
- [ ] 专用服务器：`dotnet run --project src/Tank.Server -- --mode royale --map sandbox` 打印错误和用法、退出码 2；`--mode royale`（默认河谷）正常起来、日志写 `royale, map riverland`。
- [ ] 发布脚本的冒烟参数仍够用：`godot-mono --headless --path src/Tank.Client -- --host --port 27091 --bots 1 --autostart --quit-after 10` 输出里 `[server] match started` 和 `[match] entered match` 都有。
- [ ] `pgrep -fl 'MacOS/Godot'` 没有残留。

**提交**：没有代码改动就不提交，结果记进度（下一个任务一起提交）。

### Task 12：文档

**Files:** `docs/01-game-design.md`、`docs/04-architecture-and-plan.md`、`README.md`、`docs/PROGRESS.md`

内容以代码为准，不写没做的东西：

- `docs/01-game-design.md`：模式一节加吃鸡（只在河谷、16 辆、不复活、10 分钟、五圈的表、圈外掉血、毒圈击毁不算击杀、排名、结算、观战）；HUD 一节加毒圈行、圈外提示、光墙、小地图的圈、击杀栏；房间一节加人数上限和换模式的限制；不做的（设计 §7）；还没做的改为 6B、6C。
- `docs/04-architecture-and-plan.md`：仓库结构加 `Modes/Zone.cs`、`ZonePicker.cs`、`BotDirector.Zone.cs`、`World/ZoneWall`、`Match/ZoneLineView`、`ZoneWarningView` 等；`MatchRules.Update` 的顺序；协议 v5 的变化（消息表加 `Zone = 26`、开局消息尾部、事件的击毁原因、计分板的名次 5 位和两项、对局块的参赛人数）；§5 测试表加新的测试类；§6 阶段路线 6A 已完成。
- `README.md`：进度表加阶段 6、6A ✅；模式表加吃鸡；`--mode royale`。
- `docs/PROGRESS.md`：6A 节点记录最后一行，“当前位置”改为 6A 完成、下一步 6B。

**命令**：`git diff --stat`；文档里提到的每个类名、常数、数字用 grep 在代码里核对一遍。

**步骤**

- [ ] 按上面逐个文件改
- [ ] 用 grep 核对
- [ ] 提交

**提交**：`docs: stage 6A — royale and the zone in docs 01 and 04 and the README`，末行 `Tests: unchanged (…)`（按实际）。

**批次 E 收尾**：两路审查（规格审查对照设计 §1 的 6A 行、§2、§3、§6 的 6A 部分和下面的验收清单；文档审查对照代码）→ 跟进提交 → 进度 → 验收清单全部打勾后 squash 合入本地 main（见“执行方式”），合入后在 main 上再跑一遍四个测试项目和 0 警告检查。

---

## 验收清单（6A）

E 批规格审查给的勾选版（`plan6a/checklist-E.md`），按代码和 `build/shots/6a-t11/RESULTS.md` 核过；[x] 满足，[-] 部分满足（原因在括号里）。

- [x] 四个测试项目全部通过，五个项目 0 警告；Update 42 不变，Sim 1100 → 1273、Net 294 → 369、Art 1238 → 1386 只增不减（`RESULTS.md` 第 1 行；E 批跟进后重跑相同）。
- [x] 吃鸡模式：`BattleMode.Royale = 4`、房间里“吃鸡”（`ModeText`）、`--mode royale`（`LaunchOptions`）；只在河谷：`ModeRules.OnlyMap/AllowsMap`、房主换到吃鸡时换图锁图（`BattleServer.Room.cs`，`RoyaleServerTests`）、专用服务器 `--mode royale --map sandbox` 报错退出 2（`Program.cs`，手工 rc=2）；16/10 座（`MaxPlayersFor`，`ModeSeatTests`）；机器人 0～15、换回 10 人模式压到 9（`ModeSeatTests`）；真人 >10 换不过去且列表给原因（`RoomModel`，`RoyaleServerTests`、`RoomRoyaleModelTests`）；满 2 辆可开（`RoomRulesTests`）；不复活、不中途加入（`RoyaleRulesTests`）；10 分钟（`ModeRules.DurationTicks`）。
- [x] 出生：按对局种子洗 16 个各自为战出生点（`MatchRules`，`RoyaleRulesTests`），朝向沿用 `SpawnLayout.Solo`（朝地图中央，`RiverlandSpawnTests`），开局保护照旧。
- [x] 毒圈：第 0 圈是可开车范围的外接圆（`ZoneScheduleTests`）；五圈按表等待、收缩（`Zone.cs`，`ZoneScheduleTests`）；嵌套、圆心在能开到的陆地上、同种子同结果（`ZonePicker`，`ZonePickerTests`，河谷种子 1～200 实测 1000 个圈）；`ZoneState.At` 纯函数，仿真、服务器、客户端、机器人共用（`ZoneStateTests`、`ZoneClientTests`）；等待段开始时才公布、重同步的开局消息带已公布的圈（`BattleServer.Match.cs`，`RoyaleServerTests`，探针 23021 次检查没有提前泄露）。
- [x] 圈外掉血：车的中心在圈外每整秒掉 `ceil(最大血量 × 百分比)`，第 0 圈不掉，收缩段按正在缩向的圈（`ZoneSchedule.DamageOf`，`ZoneDamageTests`）；保护不挡、不算击杀、不发 `TankHit`（`ZoneDamageTests`）；击杀栏“<名字> 死于毒圈”（`KillFeed`，`KillFeedTests`；人人收到 `RoyaleServerTests`）。
- [x] 排名：按阵亡先后、同 tick 按阵亡前血量 / 击毁 / 座位、超时按血量（`MatchRules`，`RoyaleRulesTests`）；结算“吃鸡！”/“第 N 名”、“共 16 名”、每行名次 / 名字 / 车型 / 击毁 / 伤害 / 存活时间（`HudText`、`ScoreboardModel`、`ScoreboardView`，`HudTextTests`、`ScoreboardModelTests`；截图 `royale-ended.png`、`gallery-results-royale-win.png`）。
- [x] 画面和 HUD：光墙（`ZoneWallShape`、`zone_wall.gdshader`、`ZoneWall`，`ZoneWallShapeTests`、`ZoneWallMeshTests`；`wall-near.png`、`wall-far.png`）；圈外泛蓝和“在安全区外 −N%/秒”（`ZoneWarningView`、`ZoneHud`，`ZoneTextTests`；`outside.png`）；比分板左击毁右“存活 N/16”、毒圈行三态（`HudText`、`ZoneHud`，`ZoneTextTests`；`zone-60.png` 等待、`zone-120.png` 收缩，闭合只有测试和画廊，见下）；小地图和全屏图的当前圈（圈外压暗）和下一圈（`MinimapView`、`MinimapFeed`，`MinimapFeedTests`；`full-map.png`）。
- [x] 机器人：跑圈往圈里分散的点去（`BotDirector.Zone.cs`，`BotZoneTests`）；只在圈里巡游（`Bot.Zone.cs`，`BotZoneTests`、`BotCrowdTests`）；来不及时边跑边打、圈外不停下来打（`Bot.cs`，`BotZoneTests`；守着的车例外，偏差 10）；确定、每 tick 不分配（`RoyaleMatchTests`、`BotZoneTests`）。
- [x] 无画面整局：`RoyaleMatchTests`（带弹种子 3/7/11/19，不带弹和 ×50 血 3/7/8/11/19）：限时内 Eliminated、最长卡住 < 10 s（实测 1.7～5.9 s）、第 3 圈缩完前没有机器人死于毒圈、第 1～4 圈缩完时圈外 0 辆、每 tick 0 分配；服务器 tick p99 0.868 ms < 2 ms（`BotLoadTests`，Release）。
- [x] 协议 v5：16 个座位、名次 5 位、`ZoneMessage` = 26、开局消息的圈、击毁原因 2 位、伤害和存活时间、参赛人数（`BattleProtocol`、`BattleMessages`、`BattleSnapshotCodec`，`RoyaleWireTests`、`ZoneClientTests`）；编解码往返和边界测试。
- [x] 16 辆车的开销实测并写进进度：点亮一次更新 0.175 ms（`VisionLoadTests`）、快照活着 67/83 B、死后看全场 39/164 B、≤ 2.5 KB/s、soak 吃鸡行 4.1 KB/s 71 B、帧率 115～116（`RESULTS.md` 开销 / 帧率行，`PROGRESS.md` E1 行）。
- [-] 回归：四种模式的截图和 5D 一致（`mode-*.png`）、试驾场六张和 `base5d` 差 0.7～13.6%（5D 时 2～19%）、soak 原四行的修正和误差同量级；但陡坡修正改了试驾场 soak 的击毁数（偏差 28）、各自为战 10 人房间从一列变两列（偏差 14）——“行为不变”只算部分，都已如实记。
- [-] 画廊 `royale`、`royale-outside`、`royale-map`、`results-royale-win`、`results-royale`（`HudDemoFrames`，`HudDemoFramesTests`）和真对局的光墙近远、圈外、倒计时（等待 / 收缩）、小地图、全屏图、结算（第 11 名）、房间（含房主 + 客户端）都看过；“毒圈已闭合”和真对局第 1 名只有画廊 / 测试——带弹的 15 个机器人 105～240 s 就打完，真对局到不了（`RESULTS.md` 遗留 1，偏差 18，试玩后调）。
- [x] 文档 `docs/01`、`docs/04`、README 已按代码更新（`87656c1`）；`docs/03`、`docs/README.md`、`LaunchOptions.cs` 文件头在 E 批跟进里更新；`docs/PROGRESS.md` 的“6A 节点记录”完整。
- [x] 每批两路审查和跟进提交都做了（A `4967bc7`、B `bf1d1bb`、C `9336dbf`、D `1a93e3c`、E 见进度 E2 行）；squash 合入本地 main、合入后 main 上全部测试通过、0 警告、没有推远端——见进度“6A 节点记录”最后一行。

---

## 与设计的偏差

写计划时按现在的代码定下来的做法，和阶段 6 设计字面不一样或设计没说的地方：

1. **`MatchRules` 的吃鸡可以没有圈**（`zone` 为 null：只用在单元测试里，等于 16 个座位的各自为战）。服务器总是传五个圈；“只在河谷”由服务器和房间保证，不由规则保证。
2. **出生点按对局种子挑只在吃鸡里做**：设计 §2.2 写“按对局种子挑”，现在的各自为战是按列表顺序取第一个空点；为了不改原有模式，各自为战照旧，吃鸡在开局时用种子把 16 个点排一个顺序。
3. **同一 tick 阵亡的“按剩余血量”**：阵亡的车血量都是 0，所以按的是“阵亡那一 tick 之前的血量”——`MatchRules` 每次 `Update` 结束时记下的各座位血量（`_health`，这一 tick 的炮弹和毒圈都还没扣），阵亡（或离开）时存进 `_healthBeforeFall`；再按击毁数、再按座位号（A 批跟进；原先跳过血量直接按击毁数，闭合后同一秒死于毒圈时座位号可能定了第 1 名）。闭合后所有车同时按秒掉 10%，最后几辆可能同一秒死于毒圈：这时第 1 名给阵亡前血多的，标题照样“吃鸡！”；整局测试要求四局里至少三局第 1 名活到最后。伤害上限另用一份 `_hitRoom`（`_health` 减去这一 tick 已记的命中），两者不互相干扰。
4. **击毁原因进事件**：现在 `TankDestroyed` 的 `TankId = 0` 表示“开炮的人对你隐藏”，毒圈击毁不能再用它区分，所以 `GameEvent` 加 `KillCause`（线上 2 位，只追加）。
5. **毒圈击毁发给所有人**（B 批跟进改正：原先这里写“照普通击毁的规则、看得见那辆车的人才收到”，不对）：击毁事件照普通击毁的规则发，而 `Viewer.Sees` 对残骸总是真——炮弹击毁本来就人人都收到（击杀栏是全场的），毒圈击毁也一样，每个客户端都收到 `TankDestroyed`（`Cause = Zone`、`TankId = 0`），击杀栏人人写“<名字> 死于毒圈”。`RoyaleServerTests` 有端到端的测试（测试地图上手工给的圈，圈外的车死于毒圈，墙后看不见它的人也收到）。“存活 12/16”来自计分板，不受影响。
6. **伤害**：每发最多算到目标剩下的血（同一 tick 先挨炮再挨毒时可能多算一次毒圈前的血，可以忽略）；不算自己和同队；毒圈的伤害不算谁的。所有模式都记、都发，只有吃鸡的界面用。对局中别人的伤害发 0（同占领：一涨就说明谁在交火），结束后发真数。
7. **存活时间**按整秒发；对局中活着的人发 0（计分板不会每秒变一次），阵亡的和结束后的发真数。会复活的模式（团队战）里对局中一律发 0（等复活的车也是：B 批跟进），结束后照样发真数（界面只在吃鸡用）。
8. **“共 16 名”的 16**从新的 `MatchBlock.Entrants`（这一局进过的人数）来：设计说“按开局的车数”，客户端从计分板数不出来（离开的人那一行是空的）。
9. **机器人巡游的圈**：设计说等待段挑当前圈里的路点、收缩段挑正在缩向的圈里的；但等待段时下一个圈已经公布，机器人不在下一个圈里就要跑，挑当前圈里、下一个圈外的路点会让它出去又跑回来。所以两段都挑正在去的圈（`ZoneState.Target`）里、离圈心 80% 半径以内的路点；一个都没有时在圈里随机取点。
10. **“在圈外时不追圈外的敌人”**：机器人从不追人（看见了就停下打），所以做成：在当前圈外（正在掉血）或来不及时，有目标也不停下，边开边打（照 5C 撤退时的 `FireOnTheMove`），也不倒车躲太近的敌人。守着（`Leg.Arrived`：最后一圈到圆心 20 m 以内、按定义在半径 0 的圈外）的车例外——`Bot.cs` 的 `onTheWay` 只在 `_leg` 是 Waypoint / Via / End / Zone 时成立，守着的车照样停下交战、倒车躲近敌（E 批审查指出，没改代码）。
11. **到达时间的估计**：手里有到点的整条路时按路长，否则直线 × 1.3；车速按 `MaxForwardSpeed` 的 60%；再留 15 s 余量。和“剩余时间”比的是到目标圈缩完为止的 tick（等待段 = 剩下的等待 + 收缩）。
12. **跑圈的点**：离圈心半径的一半，角度 = 坦克 id × 黄金角（按编号散开），吸附到 12 m 内开着的格；半径小于 40 m 的圈到了就守着不巡游；最后一圈（半径 0）到圆心 `ZoneArriveRadius` = 20 m 以内算到（C 批跟进，原 8 m，见偏差 26）。
13. **光墙的画法**：加法混合、不写深度、不受雾（项目不给半透明物体排序；加法混合和顺序无关），墙脚的高度用 256 × 1 的纹理在着色器里贴地（墙高 60 m 从墙脚的地面算、往下埋 3 m），圈挪了 0.5 m 以上才重新采样；半径小于 1 m 时藏起来。数（亮度、条纹、近处加亮的距离）是起点，按截图调。
14. **16 行的排版**：房间里没有队伍、行数多于 8 时分两列（各自为战也算：10 人的各自为战房间从一列变两列，是计划的决定，`RoomRoyaleModelTests` 钉住；D 批跟进确认）；计分板和结算在行数多于 10 时用紧凑行（行高 30），吃鸡多一列伤害、结束后最后一列是存活时间；其他模式排版不变。
15. **换模式的原因放在模式列表里**：做不到的那项灰掉，悬停显示“真人超过 10 个，不能换到团队对战。”（D 批跟进查过 Godot 4.7.2 的源码：`PopupMenuItems::get_tooltip` 经 `_get_mouse_over` 取项，后者只跳过隐藏的项，不看禁用，所以禁用项的 tooltip 照样显示）；服务器也照样拒绝。机器人数换到 10 人模式时压到 9（`MaxBotsFor`），开局时再按空座位压（照旧）。
16. **客户端开房 `--mode royale --map sandbox`**：警告一句、在河谷开（客户端不该因为参数退出）；专用服务器照设计报错退出（代码 2）。
17. **公布消息带对局号**，开局 / 重同步消息带对局开始的 tick 和第 0 圈（第 0 圈由服务器发，客户端不自己算；开始 tick 不能从 `EndTick` 倒推，测试会改对局时长）。开局时第 1 圈的等待已经开始，所以开局消息里总是有第 0、1 两个圈。
18. **时间表照设计的表**；整局测试（任务 6）要调时改 `ZoneSchedule` 并在这里补记调了什么、为什么。没有调。带弹的整局都在第 2 圈缩完（300 s）之前结束（任务 6 实测：1 个不动的真人 + 15 个机器人，种子 3、7、11、19 在 227、205、241、158 s 打完，C 批跟进后），原先以为是机器人往圈里聚得太快；C 批审查对比过同样 8 个种子在真实时间表和“圈从不缩”下的整局：第一次击毁都在 14～23 s、15 个击毁里的 13 个都在约 115 s 前完成，整局结束都约 140 s，两种一样——结束得早是因为密度（16 辆车、共用 5C 的路点、约 20 s 起就交火），不是因为机器人往圈里聚。所以时间表不动，等 6C 之后的试玩（有真人，节奏可能不同）再判断。后面几圈由“机器人没有弹”和“血量 ×50”的整局跑到（C 批跟进，见任务 6）。
19. **`zone` 镜头预设**（只在对局里有）：截光墙近处用，设计没说；远处用已有的 `overview`。还加了 `--shot-after outside:S`（第一次在圈外后截图）。
20. **`ZoneState.At` 的时间可以是小数**：画墙按插值的渲染 tick 平滑地缩；伤害只在整 tick 上算。圈还没收到时（客户端落后），当前圈停在最后一个知道的圈、没有下一个圈。
21. **整秒从开局算**：`(Sim.Tick − StartTick) % 30 == 0`，第一次在开局后第 1 秒（第 0 圈时百分比是 0，实际从第 90 秒开始掉）。
22. **10 人模式的房间里第 11 个真人被拒**（“房间已满。”），虽然服务器有 16 个座位；吃鸡房间 16 个。数的是所有占着的座位（A 批跟进，提前于任务 5 做）：对局中机器人也占座，10 人模式的对局里真人 + 机器人到 10 就不再收人，和 16 个座位之前一样。
23. **挑圆心落到扫描**（A 批跟进）：设计 §3.1 说“取 64 次都不合格就退到当前圆心”（原先照做），但第 0 圈的圆心 (600, 600) 在河谷上是关着的格，第 1 圈一退回就违反同一条的“圆心落在能开车到的陆地上”。现在 64 次没中就确定地扫允许的圆盘，取格心离上一个圆心最近的陆地格的格心（陆地格的格心按定义是干的），仍然套在上一个圈里；只有圆盘里一个陆地格都没有才保留上一个圆心（河谷上不会发生：第 1 圈的圆盘 386 m，之后每个圆盘 ≥ 60 m 且上一个圆心所在的陆地格的格心离它不到 2.9 m）。抽中的点本身还要是干的（`WaterDepthAt ≤ 0`）。河谷种子 1～200 实测：1000 个圈 0 个落到扫描、全部是干的，离最近的水至少 1 m（1 m 网格上量）。
24. **`Entrants` 只在吃鸡里发**（B 批跟进）：`MatchRules.Entrants` 改成进过这一局的不同座位数（每个座位只算一次，所以不会超过 16）；快照的 `MatchBlock.Entrants` 只在有毒圈的模式里填真数（“共 N 名”只有吃鸡的结算用），其他模式发 0，服务器写的时候再压到 `MaxPlayers`。原先团队战、标准战中途能进人，同一个座位离开再进每次加 1，到 17 时读的一方抛 `Entrants out of range.`，之后每个快照都丢、所有客户端卡住直到这局结束（审查的回环探针：两人对局 + 一个客户端进出 16 次，卡在 tick 1081）。
25. **陡坡规则修正：坦克不再困在悬崖脚下**（任务 6 之后的修复，改的是第三人称设计 §4.1 第 3 条的实现）：原规则看目的地的坡度只用 `NormalAt`（左右各 1 m 的差分），把悬崖的坡度摊到了坡脚、坡顶 1 m 内的平地上；在那里一步要降够悬崖坡度的一半才放行，平地上几乎不降，于是除了正对着离开悬崖，每一步都被拒，靠上转角、岩石或别的车就一步也走不了（河谷南坡道东侧和台地坡脚的夹角 (246, 326)：不带弹的吃鸡种子 1 里一辆机器人卡了 91.8 s，改后 11.8 s）。现在：(1) 要两种量法都说太陡、也都不算下坡才拒绝，另一种是双线性面在那一点自己的坡度（`HeightField.SurfaceNormalAt`）；“下坡”除了原来按降幅算，也认“朝向在倾斜线 60° 以内且不往上走”（均匀坡上两者一样，坡度拐弯处才不同）。(2) 陡坡上挡车的物件堵住下坡的路（沿倾斜线 1 m 处车体会碰到它）时，可以横着走（往上不陡于爬坡上限，岩石出了 1 m 就不行：试驾坡上斜着往上最多抬高约 2.3 m）；一步被拒时车照样转向，转进物件就被推出来（哪怕往坡上推一点），否则顶着岩石下坡的车永远转不开。原规则放行的每一步照旧（同样的算式），平常开车不变：soak 五行和改前相同（插桩数过新规则改变结果的步数：0），整局测试带弹四局和不带弹 7、11、19 不变，不带弹 3 的最长卡住 8.6 → 6.5 s。看得见的变化只在陡坡边：车能开到悬崖脚下、坡顶边缘（原来停在 1 m 外）并沿坡脚开，慢速也能从坡顶开下悬崖，顶着陡坡上的岩石能转开、沿岩石横移。扫描（中坦，1 m 网格，8 个朝向，前进、倒车、先原地转再开）：河谷完全出不来的点 1110 → 2（都是悬崖半腰两三块岩石围成的兜，只有从正上方开下悬崖才进得去），试驾场 9 → 0；4 m 网格河谷 104 → 0、试驾场 1 → 0。（这把尺子只要求车挪开 1 m、只扫中坦，漏了整局里机器人困住的那几处，见偏差 28。）
26. **最后一圈的到达和重试**（C 批跟进）：原来最后一圈（半径 0）到圆心 8 m 以内才算进圈，15 辆车挤不进 8 m，站着的车（寻路的圆盘）又把路封住，到不了的车 8 秒内试完 8 个点就“原地守到下一圈”——最后一圈之后没有下一圈，不带弹的探针里第 5 圈结束时有 4～8 辆守在圈外，“在跑却守着”的车·tick 每局 374～1757。现在：小圈（只有最后一圈）以 `ZoneArriveRadius` = 20 m 为“进了”和“跑停下”的半径（`InsideLimit` = max(0.8R, 20 m)），跑圈的点离圆心 10 m；试完 8 个点（或找不到路）先守 `ZoneRetryTicks` 再从第 0 个点重来。闭合后的伤害照旧对所有车（圆半径 0），20 m 只是机器人“到了”的判定。
27. **吃鸡里的拥挤和卡住**（C 批跟进，`Bot.Crowd.cs`，只在 `ZoneOn` 时）：任务 6 之后的探针（不带弹，种子 1～20）里种子 8 最长卡住 42.1 s、12 是 28.9 s。根因：(1) 种子 8、12 的是最后一圈 8 m 的到达半径（偏差 26）：几辆车都在圆心 12 m 外，彼此和守着的车把对方的寻路都封住，人人“等路”、没人挪——5C 的卡住处理（倒车、左右交替、让队友）里只有倒车会动，而“找不到路”按设计不倒车；让队友在吃鸡里根本不生效（人人各自一队）。(2) 修完 (1) 后剩下的长时间卡住大多是地形：车倒进岩石或崖脚关着的格里，从那里搜索会吸附到最近的开着的格——常在崖顶，或一个四面关着的小兜——于是要么永远“找不到路”，要么拿到一条开不上去的路一直顶着崖壁；倒车总按“单双次”转向，顶在岩石角上时第一次往往转进被挡住的那边。(3) 巡游的路点在圈里，路却绕到圈外（隔着崖），圈缩时 5～6 辆被留在圈外。改法见任务 6 的 C 批跟进条目：绕开所有车、守着的车让路、倒车反着转、从同一高度搜索、不在开阔地上找不到路时先开向开阔地、巡游的路出圈就不要。种子 1～20 的结果（不带弹 / 血量 ×50；最长卡住 s，第 1～4 圈缩完时圈外的车）：不带弹最长卡住 4.1～12.0 s，超过 10 s 的有种子 2（11.2）、12（12.0）、14（10.0）；×50 最长 3.9～10.7 s，只有种子 12 超过（10.7）；圈外：不带弹种子 2（第 2 圈 1）、3（第 3 圈 2）、10（第 4 圈 1）、14（第 3 圈 5），×50 全 0。剩下的都是地形陷阱：河谷南坡道西侧坡脚和岩石 38、83、196 之间（约 (197～226, 318～333)），台地崖脚和岩石 27、104、123 之间（约 (413～430, 195～212)），以及崖脚和岩石 174、178、182 之间的窄道（约 (450～470, 235～245)）——车在那里既转不动也开不动（`TankMotion` 拒绝每一步），或者导航格说能走、车体过不去；要在运动或导航格里修（例如把这些窄缝在格子上关掉），留到后面（已修，见偏差 28）。另外 `BotArena.LongestStuckSeconds` 转过 20° 就重新计时，车在原地来回摇（倒车时转、前进时转回来）时量不出来：上面几处陷阱里有车实际在原地 50～70 s，量到的只有 7～10 s——圈外的计数能看出来。整局测试的“不带弹”种子把 3 换成 8（3 的两辆就卡在南坡道西侧，第 3 圈缩完时圈外 2 辆）。
28. **河谷悬崖脚下的陷阱**（C 批跟进之后的修复，改的是第三人称设计 §4.1 第 3 条的实现和吃鸡机器人）：偏差 27 留下的三处，探针（整局，种子 1～20，不带弹 / 血量 ×50）里找到车的位置一个个复现。(1) 南坡道西侧坡脚（197～226, 318～333）：平台和坡道的崖脚被岩石 38、83、93、138、196 围成兜，兜里的平地放不下中坦以上的车体，出口不是比车窄就是要沿崖面走；车从坡顶、坡道边开下悬崖（往下总是放行）掉进去，车中心停在崖面上，离岩石超过 1 m 就既不能横着走（偏差 25 的岩石探针只看 1 m）也下不去；另外车中心刚滑过坡道边缘（西缘 (226, 333)、东缘 (241, 325)）时，除了开下悬崖每一步都被拒（沿边开算“陡坡上横着走”），机器人不肯往下开就停在那里。(2) 台地东崖、北崖的崖面（(413～430, 195～212)、(415, 230～245)、北崖 (390～415, 79～86)）：崖脚排着岩石，车沿崖面横着走，碰到斜着伸进路上的岩石时，岩石把车往坡上推一点，推出来的这一步就算“爬坡”被拒——岩石的推加上坡的规则，每一步都被拒。(3) 崖与岩石 174、178 之间（455, 240）：车体卡在两块岩石中间，往一边转不过去；按着那边的转向键时连照直开也不行（转不动的那一步连位移一起退回），机器人一直朝路的方向转，就“既转不动也开不动”（修到一半时探针里不带弹种子 7 的一辆轻坦在那里 357 s）。偏差 25 的扫描没找到它们：它只要求车从原地挪开 1 m（兜里的车能在崖面上挪 1 m，只是哪条离开崖面的路都被拒）、只扫中坦、8 个朝向、只看不按转向键的开法；这些姿态都是岩石加陡坡一起才堵死的。改法（`TankMotion`）：一、车站在太陡的地上（两种量法都陡）时，油门送去的那一步本身放行，碰撞把它推得往坡上一点也不算爬坡（转向被拒时本来就这样算），只有两样都拒才拒；二、“下路被堵”多一种判法：车转成面朝坡下，沿倾斜线每 0.5 m 往下挪，中心到能爬的地面之前就碰到挡车的物件（8 m 内）也算堵住——这时只放行不往上走的一步（横着、往下），原来 1 m 探针加“不陡于爬坡上限”的那条照旧；三、车中心刚滑过陡坡的上沿、沿倾斜线往上半个车宽内就是能爬的地面（上坡那一侧还在坡顶上）时，这一步放行，能沿边开（车头翘过爬坡上限照旧开不动）；从坡脚进 2 m 一格的陡坡时坡顶在半个车宽之外，所以任何悬崖都爬不上去（测试钉住一格高 5.5 m 的低崖，四种车 5 个朝向）；四、选位置多一个候选：转不动时照直开的那一步（同样的规则），排在“新朝向、旧朝向在推出后的位置”之后、“原地不动”之前。一到三只在原规则拒绝时起作用，原来放行的每一步照旧（同样的算式）；四只在前两个候选都卡在物件里时起作用。机器人（`Bot.Crowd.cs`，只在 `ZoneOn` 时）：不开下悬崖——前方刹车距离加半个车长内有比自己低、太陡的地面就松油门，往没有落差的一侧转（`DropAhead`、`AwayFromDrop`；导航格从不领车下崖，是绕车和切弯把车带出了边：不带弹种子 7 里那辆轻坦全速上南坡道绕车，从西缘掉进 (1) 的兜）；等路的车也给挤它的车让路，倒车尾巴朝着自己的车也算挤（修的过程中探针里见过：最后一圈里一辆车夹在岩石和守着的车之间 46 s）；巡游的路连续 3 次出圈就倒车一次再找（同样见过：小圈里被车围住，圈内每条路都要绕过它们，原地等了 10 s 以上）。扫描（新尺子：车在点上能放下的朝向里，没有一个能开到能爬的地面上、离原地 ≥ 1 m：先试直接开或先原地转再开 3 s，不行再做最多 1000 个姿态的搜索——8 个指令各按 0.8 s，离原地远的先试——四种车，8 个朝向）：1 m 网格，河谷 轻/中/重/歼 直接开不出来的点 688/869/1022/1010 → 184/353/542/507，怎么开都出不来的 2/3/70/18 → 0/0/0/0；试驾场直接开不出来的 0/0/55/0 → 0/0/12/0，怎么开都出不来的 0 → 0；测试里的 2 m 网格河谷重坦 14、歼击 4 → 0。偏差 25 的旧尺子（中坦、挪 1 m）：河谷 2 → 0。整局探针（种子 1～20）：最长卡住 不带弹 4.1～12.0 s → 3.9～7.0 s，×50 3.9～10.7 s → 4.1～6.8 s；第 1～4 圈缩完时圈外的车 不带弹种子 2、3、10、14 共 9 辆 → 全部 0，×50 本来就 0；同一处 3 m 内停得最久的车（量卡住时车原地来回摇量不出来的那种，偏差 27）不带弹 15～169.5 s → 14.5～41.5 s，×50 12.5～195 s → 14.5～27.5 s。整局测试：不带弹种子 3 放回（3、7、8、11、19），最长卡住 5.2～5.9 s，带弹 1.7～2.3 s，×50 4.2～5.0 s，第 1～4 圈缩完时圈外都是 0。soak：吃鸡一行和改前相同；试驾场四行变了（击毁 4/6/4/5 → 5/5/4/4 等），只因为四：插桩数过，60 s 里有 33～77 个 tick 是车挤着别的车或顶着墙、按着转向却转不动，原来原地不动，现在照直开；把四关掉，五行和改前完全相同。全部 PASS。
29. **计分板的尺寸进逻辑层，画廊的毒圈击杀**（任务 9 实现时加）：行高、正文和状态字号（常规 42 / 24 / 22，超过 10 行紧凑 30 / 20 / 18）和表格高度的算式从 `ScoreboardView` 的私有常数挪到 `Match/Logic/ScoreboardMetrics.cs`（`ScoreboardMetrics.For(rows)`、`Height(groups, rows)`），Art 测试钉住“16 行放进 `ScoreboardHeight`、结算表格底在页脚基线之上”；视图本身的数一个没变。画廊的预制帧里带 `Zone` 的击杀行要走 `KillFeed.AddZone`，所以 `MatchHud` 加了 `OnZoneKill(victim, side, now)`、`HudGallery` 按 `FeedLine.Zone` 分发（任务 10 接线真对局时也用它）。
30. **光墙和 `zone` 镜头的实现选择**（任务 10 实现时）：(1) `zone` 预设**不**进 `MapSite.PresetNames`（试驾场用它判断预设，加进去试驾场就认了）：`MatchAutomation` 末尾多一个 `hasZone` 参数（`MatchMain.Setup` 传 `ModeRules.HasZone(Mode)`），有毒圈时 `--camera-preset zone` 才算已知，未知预设的警告里也列它；`CameraPresets.StreamsAroundCamera` 照计划加。(2) `ShotPlan` 加 `ShotMoment.Outside`（对局的时刻表认 `outside`，试驾场警告 never happens），任务 7 只解析了参数，没人记这个时刻。(3) `ZoneWallMesh` 单面（朝外）+ 着色器 `cull_disabled`：8 段是 2 × 9 个顶点、16 个三角形。(4) `ZoneCamera.TargetHeight` 20 → 8 m：从 5 m 高、25 m 外看 20 m 高的点，整幅画面都是天空前的光墙，加法混合在天空上一片白；8 m 让墙后的地面进画面（`ZoneCameraTests` 的 20 改 8）。(5) 着色器的亮度按截图调：近处 1.0 → 0.6、远处 0.35 → 0.45（`NEAR_SHARE`、`FAR_SHARE`），条纹 0.75 ± 0.25 → 0.55 ± 0.45（原来波谷也饱和成白、近处看不出条纹）。(6) 近景截图在 200 s 截（计划 110 s）：110 s 时第 0→1 圈半径约 720 m、圆心 (561, 574)，圈的西侧在地图外，镜头站在远地平面上看一面灰白；200 s 是第 1 圈（420 m，整个在图内）。同一张图里站着不动的自己已被圈扫过，顺带看到了圈外警告和泛蓝。

31. **比分板“存活 N/M”的 M 是在座人数**（E 批审查）：`HudText` 的 `CountAll` 数计分板里 `Active` 的行，有人退房就掉到 15；结算的“共 N 名”从 `MatchBlock.Entrants`（偏差 8）来，仍是 16。两处可能不一致，试玩时若觉得别扭再统一。
32. **启动参数的机器人数和真人数不看模式**（E 批审查）：客户端 `--bots` 对所有模式都放到 15（`LaunchOptions.MaxBots` = `BattleProtocol.MaxPlayers − 1`）、`--players` 放到 16；专用服务器 `--bots` 收 0～15 不看 `--mode`（`Program.cs`）。多出来的由 `BattleServer` 开房时按 `ModeRules.MaxBotsFor(mode)` 静默压缩（`BattleServer.cs:83`，团队对战 `--bots 15` 实际 9），没有警告。
33. **soak 的吃鸡一行只跑 60 s**（`BattleSoak.cs`）：第 1 圈从 90 s 起缩、第 2 圈 180 s 公布，所以这一行走不到毒圈伤害、毒圈击毁和 `Zone` 消息，只量了 16 辆车的带宽和 tick；毒圈的线上行为由 `RoyaleServerTests`（端到端）和 `RoyaleWireTests` 覆盖。
34. **没有专门钉住“非吃鸡模式的机器人行为不变”的测试**：靠 5C 的老测试（都过）和 `BotCrowd`、`Bot.Zone` 只在 `ZoneOn` 时生效；陡坡修正（偏差 25、28）改了所有模式卡在墙边和车边时的走法（soak 试驾场四行的击毁数 4/6/4/5 → 5/5/4/4）。专用服务器 `--mode royale --map sandbox` 的退出码 2 只有手工验收（`RESULTS.md`），没有测试。
