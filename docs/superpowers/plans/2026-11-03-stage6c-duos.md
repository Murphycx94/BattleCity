# 阶段 6C：双人吃鸡 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 加第六种模式“双人吃鸡”（`BattleMode.Duos = 5`，`--mode duos`）：河谷上最多 16 辆车分 8 队、每队 2 人，毒圈和补给箱照吃鸡；房间里每人选第 1～8 队（满 2 人的不能选），新进房间的真人默认进一个空队，机器人开局时先补只有一个人的队、再两两成队；队友在同一个出生点左右相隔 10 m 一起出生；队友共享点亮（已有的按队点亮）和见过的补给箱；一队两人都阵亡才出局，按队排名（最后一人阵亡的时刻，同 tick 按剩余血量、再按击毁数），结算页标题按本队名次（“吃鸡！” / “第 N 名”），副标题“共 N 名”是开局的队数，列表按队分组；阵亡后先跟随活着的队友观战，全队出局才能看全场。客户端里队友蓝灰（车身、名字条、小地图、击毁提示），其余全红。机器人离队友超过 60 m 就往队友那边靠（队友是真人时也一样），跑圈时两人去圈里同一点附近，交战时优先打队友正在打的目标。协议升到 v7（线上布局不变，只是多了模式值 5）。验收：1 名玩家 + 15 个机器人打完一局双人吃鸡。

**Architecture:** 规则在 `Tank.Sim.Modes`：`BattleMode.Duos`、`ModeRules` 的新判断（`IsDuos`、`HasAllies`、`TeamsFor`；`HasZone`/`HasCrates`/`OnlyMap`/`MaxPlayersFor` 把双人吃鸡算进去）、新的 `DuoSeating`（房间分队的规则：真人、机器人各进哪队，服务器和客户端的房间镜像共用）、`DuoSpawns`（队友的两个出生位置）；`MatchRules` 让双人吃鸡的车按队号 0～7 入场、每队一个按种子挑的出生点、按队判胜负和排名、`Places`（结算的“共 N 名”）。`HasTeams` 的含义不变：**只指两队的三种模式**（团队对战、团队歼灭、标准战），凡是“有没有队友”的地方改用 `HasAllies`。`Tank.Net` 升到 v7：读模式时接受 5；服务器房间按 `DuoSeating` 分队、`SetTeam` 在双人吃鸡里收 0～7、换模式时重新分队；对局里 `Viewer` 让活着的队友的眼睛继续给死者看、全队出局才看全场，补给箱的视野按队取并，`MatchBlock.Entrants` 发 `Places`。机器人在 `Tank.Sim.Ai` 里加队友：领队 / 跟随、`Leg.Mate`、跑圈的点按队取、`TargetPicker` 优先打队友的目标。客户端：`MatchColors.SideOf` 用 `HasAllies`（车身、名字条、小地图、击毁提示一处改全有），比分板按队分组（组间细线，不加横幅）、比分牌“本队击毁 / 存活队伍”、结算走吃鸡的那一套、观战先跟队友；房间里双人吃鸡的选队下拉框和按队分组的名单；`--team N` 启动参数。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9.3、LiteNetLib 2.1.4、Godot 4.7.2 .NET 版。只做 macOS。

**设计依据：** [阶段 6 设计](../specs/2026-10-28-stage6-royale-design.md) 的 §1（6C 一行：1 名玩家 + 15 个机器人打完一局双人吃鸡）、§2（共同规则：16 辆车、只在河谷、双人至少 2 个队有车、不复活不中途加入、出生、按队排名和结算、观战先跟队友）、§5（分队、对局中、机器人）、§6 里双人的测试和验收（排名双人按队、联网双人分队规则、无画面整局双人吃鸡、双人的房间分队和名字条颜色截图）、§7 的“不做”（倒地等队友扶、组队语音和标记、观战时看别人的叠层）。参考：[6A 计划](2026-10-28-stage6a-royale.md)（加模式、16 座、排名、结算、画廊）、[6B 计划](2026-10-30-stage6b-crates.md)（本计划的格式；`CrateSet.VisibleTo` 按一辆车算、“队友共享看见的箱子”留给 6C 的接口；`CrateKnowledge` 不假定一人一队）、[5C 计划](2026-10-27-stage5c-bots.md)（机器人、整局测试）。

**计划的深度：** 和 5A～6B 一样，不是逐字代码。每个任务写清：改哪些文件、对外接口（确切签名）、规则和算法要点、要先写的失败测试（具体输入和期望值）、命令和验收、提交说明。实现代码由执行者按 TDD 写（先写测试、确认失败、实现、确认通过）。接口一律以本文“共享接口”一节为准；实现中发现接口要改，先改本文这一节再动代码，并在跟进提交里说明。

---

## 执行方式

- **分支和目录**：`feat/stage6c-duos`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage6c`（从 main `2e075ca` 拉出，6A、6B 已合入；第一个提交是本计划）。下文的相对路径都相对于它。
- **逐任务派子代理**（superpowers:subagent-driven-development）：一个任务一个提交。派之前用 `python3 /Users/murphy/Desktop/dev/BattleCity-dev-scripts/brief6c.py N` 生成 `plan6c/brief-N.md`（本文的“执行方式”“执行前须知”“共享接口”和任务 N 全文），交给实现子代理读。提交前五个项目 0 警告、四个测试项目全部通过。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照阶段 6 设计 §2、§5、§6 的双人部分和本计划，列“需求 → 代码 → 测试”表），一个做代码质量审查（正确性、确定性、每 tick / 每帧不分配、线上格式的边界检查、**原有五种模式行为不变**（`HasTeams` 改没改到的地方）、命名、注释）；按两份意见做**一个**跟进提交 `fix(<scope>): batch X review follow-up — …`。
- **每批之后记进度**：在 `docs/PROGRESS.md` 新建并维护“6C 节点记录”表（放在“6B 节点记录”上面；列：# / 节点 / 状态 / 提交 / 说明；说明里写各任务提交号、跟进提交号、测试数变化、截图目录和看到了什么、实测数字（服务器 tick p50/p99、快照字节、每客户端下行 KB/s、帧率、整局里队友平均相距多远、机器人靠拢几次）、遗留问题），同时更新“当前位置”的“阶段”“进度”“下一步”和“阶段总览”的阶段 6 一行。随跟进提交一起提交，或单独提交 `docs: progress — 6C batch X`。第 0 行“设计 + 计划”写设计 `905a92e` 和本计划的提交号。
- **顺序**：A → B → C → D → E，批内按任务号。B 要用 A 的模式、分队和排名；C（机器人）要用 A 的规则和 B 的服务器（开销实测走服务器）；D（客户端）要用 B 的房间和线上字段，放在 C 之后是为了真对局截图里的机器人已经会跟队友。不并行。
- **6C 做完不等试玩**：最终验收清单全部打勾后 squash 合入本地 main（`git checkout main && git merge --squash feat/stage6c-duos && git commit`，说明 `feat: stage 6C — duos royale: eight teams of two, shared sight, team ranks, protocol v7`，末行 `Tests: …`），不推远端。合入后在 main 上再跑一遍四个测试项目和 0 警告检查。之后用户试玩整个阶段 6。
- **只做 macOS**：不写 Windows、Linux 专门的代码路径，不打其他平台的包。
- 设计已确认，中途不问用户，做完汇报。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage6c
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
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Duo" 2>&1 | tail -5             # 只跑某些类
dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~DuoMatch" --logger "console;verbosity=detailed" 2>&1 | grep -E 'duos|通过|失败'   # 整局，打印数字
dotnet run --project tests/Tank.Soak -c Release 2>&1 | tail -20                                # soak（吃鸡、双人吃鸡各一行）
```

- 成功的行形如 `已通过! - 失败: 0，通过: 1400 …`；`grep -E '通过!|失败!'` 两种都能抓到。
- **改动前的测试数**（本计划写作时在 `2e075ca` 上实测）：Sim 1400、Net 465、Art 1478、Update 42；五个项目 0 警告。每个任务的提交说明末行写测试数变化，例如 `Tests: Sim 1400 → 1430.`（列出有变化的项目）；下文各任务给的数是“至少”，以实际为准。Update 本阶段不该变。
- 以往的偶发：构建完立刻跑 Sim 偶尔有 1 条失败、重跑都过。遇到时重跑一次，两次都失败才算真失败，并在进度里记下是哪条。这台机器内存常年吃紧（6B 时 Sim 全量跑过 45 分钟）：Sim 全量跑超过 20 分钟没输出时加 `--blame-hang-timeout 5m` 重跑一次，不要直接杀掉了事。计时测试放在各项目已有的 `Timing` 集合里（`TimingCollection`），Debug 下只打印、查宽松上限，真上限在 Release 下查。
- 客户端项目 `Tank.Client.csproj` 不把警告当错误（`TreatWarningsAsErrors=false`），“0 警告”靠上面的 grep 检查；其他项目由 `Directory.Build.props` 的 `TreatWarningsAsErrors=true` 保证。
- **Art 测试编进来的客户端代码**（`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`）：`scripts/Art/Mesh/**/*.cs`、**任何** `Logic/` 目录下的 `**/Logic/**/*.cs`、`Art/Palette.cs`、`Bootstrap/LaunchOptions.cs` 等。本阶段新的引擎无关代码放进 `Match/Logic/`（命名空间 `Tank.Client.Match`）、`UI/Logic/`（`Tank.Client.UI`）或 `Minimap/Logic/`（`Tank.Client.Minimap`），不用改 csproj；这些文件里不能 `using Godot`。Art 测试项目引用 `Tank.Sim` 和 `Tank.Net`，也编进了 `tests/Shared/*.cs`（`Allocation.AssertNone`，命名空间 `Tank.Testing`）。
- `Tank.Sim` 的 `internal` 只对 `Tank.Sim.Tests` 可见（`Simulation.TankRef` 摆车、改血、去掉保护），`Tank.Net` 的 `internal` 只对 `Tank.Net.Tests` 可见（`BattleServer.Director`、`CrateVisibleOf` 等）；客户端只能用 public 成员。
- 河谷在测试里：Sim 用 `RiverlandFixture.Field` / `RiverlandFixture.Grid`（共用、别改它们；要改的各自 `MapCatalog.Riverland.NewBattlefield()` / `NewNavGrid()`），Art 用 `MapCatalog.Riverland.NewBattlefield()`（第一次生成要一两秒，测试类里用静态 `Lazy` 共用）。河谷：1200 m 见方，`Border = 30`，可开车的范围 [30, 1170]²，16 个各自为战出生点 `RiverlandMap.Spawns.Solo`（每个 10 m 内没有岩石、房子、墙、篱笆，15 m 内没有岩石，10 m 内坡度 < 8°：`RiverlandSpawnTests`），40 个路点 `RiverlandMap.Waypoints`。
- **坐标方向**：仿真的地面坐标是 (x, z)，`System.Numerics.Vector2(x, z)`；`Angles.Forward(yaw) = (−sin yaw, −cos yaw)`，yaw 0 朝 −z；车头的右手方向是 `(cos yaw, −sin yaw)`（`MatchRules.PickSpawn` 的约定）。
- **队号**：仿真里 `TankState.Team` / `Standing.Team` 是点亮共享的单位（`VisionTracker` 按队算，`Viewer` 按队给）。团队模式 0、1；各自为战和吃鸡里规则把队号设成座位号（各自一队，0～15）；**双人吃鸡里是 0～7**。线上队号 4 位（快照的车、比分板、房间名单），不用改。房间里座位的 `Team`：双人吃鸡 0～7，其余模式 0～1（各自为战和吃鸡里规则不看它）。
- **确定性**：仿真和机器人只用自己的 `XorShift` 和仿真状态；出生点的顺序、机器人的队都由对局种子和座位决定。
- **每 tick 不分配**：`MatchRules.Update`、`BotDirector.Update` / `Command`、服务器的 `Build` / 箱子视野、HUD 每帧的路径都不许分配（字符串只在显示的值变了时建）；开局时（排名、建表）可以分配——`EndDuos` 只在对局结束那一 tick 跑一次，也照 `EndSolo` 用预分配的暂存数组。

**Godot**（开发脚本在 `/Users/murphy/Desktop/dev/BattleCity-dev-scripts/`，见它的 `README.md`）：

```bash
S=/Users/murphy/Desktop/dev/BattleCity-dev-scripts
W=/Users/murphy/Desktop/dev/BattleCity-stage6c
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后先编译：Godot 命令行不编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs/.gdshader 后生成 .uid，和源文件一起提交
mkdir -p build/shots/6c-tNN                                 # build/ 在 .gitignore 里
# HUD 画廊（纯色底上的预制帧）：SCENE 传空字符串；带河谷小地图的屏 --screenshot-at 给 3 秒以上
$S/godot_run.sh $W "" 90 --hud-demo duos --screenshot $W/build/shots/6c-tNN/duos.png --screenshot-at 3 --quit-after 4
# 房间（不开局）：房主开双人吃鸡、加 15 个机器人，名单按队分组、机器人按开局时的补法显示成占位行
$S/godot_run.sh $W "" 60 --host --mode duos --bots 15 --screenshot $W/build/shots/6c-tNN/room-duos.png --screenshot-at 8 --quit-after 10
# 主场景开房、自动开局（真对局，房主线程上的服务器）；双人吃鸡 1 名玩家 + 15 个机器人（玩家在第 1 队，队友是机器人 1）
G="--host --mode duos --map riverland --bots 15 --autostart --seed 11"
$S/godot_run.sh $W "" 240 $=G --autodrive --fps-log --screenshot $W/build/shots/6c-tNN/match-60.png --screenshot-at 60 --quit-after 70
ls -la build/shots/6c-tNN/                                  # 成败看文件在不在（脚本只打印含 screenshot/error 的行）
# 试驾场六张回归截图，OUTDIR 相对于工作目录；和 5D 之后的基线整张比（试驾场本阶段不变）
$S/baseline4b.sh $W build/shots/6c-tNN-sandbox
python3 $S/pngdiffdirs.py build/shots/base5d build/shots/6c-tNN-sandbox
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束都要确认没有残留的 Godot 进程
```

- 截图路径必须是绝对路径；`--screenshot` 一次运行只截一张；每张都要人工看（用 Read 打开 PNG），在进度说明里写看到了什么。`--screenshot-at` 在对局里按对局场景的时钟算（对局场景创建起），毒圈按服务器的对局时钟（开局起）。
- 河谷第一次生成在 Debug 客户端里要几秒；`--autostart` 等地图就绪再开局，超时给 240 秒以上。
- 凡是动了客户端的任务都重拍六张试驾场回归截图并和 `build/shots/base5d/` 比：差异只允许出现在云、地平线、草叶摇动、粒子上（以往 orbit 约 23%、heavy 约 8% 来自环绕镜头的时间差；6B 时这个 worktree 对未改的 HEAD 也差 kill 12%、lamp 23%，**先在未改的 HEAD 上拍一组对照**，和对照比）。
- 帧率：`--fps-log` 每秒打印平均和最差帧时间，`$S/fpsstats.sh LOG` 汇总；任务 8、9 记双人吃鸡对局的帧率，和 6B 验收时的 113～114 帧比。
- 画廊原有的屏（`royale`、`results-royale-win`、`results-royale`、`royale-crates`、`scoreboard`、`results-ffa` 等）本阶段必须**逐字节不变**（`cmp`）：比分板和结算的改动只对双人吃鸡生效。

**提交**：

- 说明用英文一行，带范围前缀：`feat(sim): …`、`feat(net): …`、`feat(server): …`、`feat(bots): …`、`feat(client): …`、`test(sim): …`、`fix(<scope>): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。最后一行 `Tests: Sim 1400 → 1430.`（列出有变化的项目）。
- 作者用仓库配置（`火狐`），不加 `Co-Authored-By`，不推送远端。
- 只提交本任务的文件和它们的 `.uid`。别 `git add -A`。

---

## 文件结构

新建：

| 文件 | 内容 |
|---|---|
| `src/Tank.Sim/Modes/DuoSeating.cs` | `DuoSeating`：房间里真人、机器人进哪一队（任务 1） |
| `src/Tank.Sim/Modes/DuoSpawns.cs` | `DuoSpawns`：一队两人在出生点左右的位置（任务 1） |
| `src/Tank.Sim/Modes/MatchRules.Duos.cs` | `MatchRules` 的 partial：双人吃鸡的判胜负、按队排名、`Places`（任务 2；`MatchRules` 改成 `partial`） |
| `src/Tank.Sim/Ai/Bot.Duo.cs` | 机器人跟队友：领队 / 跟随、`Leg.Mate`（任务 5） |
| `src/Tank.Sim/Ai/BotDirector.Duo.cs` | 队友查找、队友的目标、跑圈的点按队取（任务 5） |
| `src/Tank.Client/scripts/Match/Logic/SpectateCandidates.cs` | 观战能跟谁（任务 7） |
| `tests/Tank.Sim.Tests/DuoModeTests.cs`、`DuoSeatingTests.cs`、`DuoSpawnTests.cs`、`DuoRulesTests.cs`、`DuoRankTests.cs`、`BotDuoTests.cs`、`DuoMatch.cs`、`DuoMatchTests.cs` | 仿真和机器人测试 |
| `tests/Tank.Net.Tests/Battle/DuoRoomTests.cs`、`DuoServerTests.cs`、`DuoWireTests.cs` | 联网测试 |
| `tests/Tank.Art.Tests/RoomDuoModelTests.cs`、`DuoHudTests.cs`、`SpectateCandidatesTests.cs` | 客户端逻辑测试 |

修改（主要的）：

| 文件 | 改什么 |
|---|---|
| `src/Tank.Sim/Modes/BattleMode.cs` | `Duos = 5`；`ModeRules` 的新常数和判断（共享接口） |
| `src/Tank.Sim/Modes/MatchRules.cs` | `partial`；双人的 `Join`、出生点、`Judge` 一支 |
| `src/Tank.Sim/Ai/Bot.cs`、`Bot.Zone.cs`、`BotDirector.cs`、`BotDirector.Zone.cs`、`TargetPicker.cs` | `Leg.Mate`、跟随的人不自己闲逛、跑圈的点、优先目标 |
| `src/Tank.Net/Battle/BattleProtocol.cs` | `Version = 7` |
| `src/Tank.Net/Battle/BattleServer.Room.cs`、`BattleServer.Match.cs`、`BattleServer.Crates.cs`、`Visibility.cs`、`BattleServer.cs` | 分队、换模式、开局的机器人、`Viewer`、箱子视野按队、`Entrants` |
| `src/Tank.Server/Program.cs` | `--mode duos`（用法行） |
| `tests/Tank.Soak/BattleSoak.cs` | 加双人吃鸡一行 |
| `src/Tank.Client/scripts/Match/Logic/MatchColors.cs`、`ScoreboardModel.cs`、`HudText.cs`、`HudDemoFrames.cs` | 队友颜色、按队分组、比分牌、结算、画廊 |
| `src/Tank.Client/scripts/Match/ScoreboardView.cs`、`ScorePlate.cs`、`MatchMain.cs`、`MatchMain.Camera.cs` | 组间细线、观战、接线 |
| `src/Tank.Client/scripts/UI/Logic/RoomModel.cs`、`RoomRules.cs`、`ModeText.cs`、`UI/RoomScreen.cs` | 选队、按队分组的名单、开局判断 |
| `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（和读它的地方） | `--team N` |

---

## 共享接口

### 必须保持源码兼容的现有签名

- `ModeRules.HasTeams(mode)` 的**含义不变**：只对团队对战、团队歼灭、标准战为真（两队、队号 0/1、队伍出生点、队伍击毁数、房间里“换到对面”）。凡是“有没有队友”（同队共享、蓝灰色、观战跟队友）的地方改用 `HasAllies`；每处都要过一遍（任务 1 列了仿真的、任务 3/4 列了联网的、任务 7 列了客户端的）。
- `MatchRules` 的构造函数、`Join(byte slot, TankSpec spec, byte team)`、`Of`、`Entrants`、`WinnerTeam`、`WinnerSlot`、`LastedTicks` 签名不变（`Join` 的 `team` 在双人吃鸡里是 0～7）。
- `Viewer(byte Team, int OwnTankId, bool OwnDestroyed, bool SeesAllWhenDead)`：第四个参数原名 `FreeForAll`，意思放宽成“死了就能看全场”（各自为战、吃鸡照旧真；双人吃鸡里全队出局才真），批次 B 跟进时改名 `SeesAllWhenDead`（只在 `Tank.Net` 和它的测试里用，客户端不用）。
- `TargetPicker.Pick(Simulation sim, VisionTracker vision, int tick)` 现有调用不变：新加一个可选参数 `int prefer = 0`。
- `RoomRowView`、`StartPlan` 只在末尾加带默认值的参数。

### 模式（任务 1）

```csharp
namespace Tank.Sim.Modes;

public enum BattleMode : byte { TeamBattle = 0, Annihilation = 1, FreeForAll = 2, Standard = 3, Royale = 4,
    /// <summary>Eight teams of two, up to 16 tanks, no respawns, on the riverland only; the zone and the crates as a royale; the last team left wins (stage 6 design §5).</summary>
    Duos = 5 }

public static class ModeRules
{
    public const int DuoTeams = 8;            // 第 1～8 队，队号 0～7
    public const int DuoTeamSize = 2;
    public const BattleMode LastMode = BattleMode.Duos;
    // All: { TeamBattle, Annihilation, FreeForAll, Standard, Royale, Duos }（房间列表的顺序）
    // MaxPlayersFor: Royale、Duos → 16；其余 10。MaxBotsFor 照旧 = MaxPlayersFor − 1
    // HasTeams: 不变（两队的三种）
    public static bool IsDuos(BattleMode mode) => mode == BattleMode.Duos;
    /// <summary>Whether tanks have team-mates (shared sight, the ally colours, following one when dead): the two-team modes and duos.</summary>
    public static bool HasAllies(BattleMode mode) => HasTeams(mode) || IsDuos(mode);
    /// <summary>The teams a seat may pick in the room: 2 in the two-team modes, <see cref="DuoTeams"/> in duos, 0 where nobody picks (free-for-all, royale).</summary>
    public static int TeamsFor(BattleMode mode) => IsDuos(mode) ? DuoTeams : HasTeams(mode) ? 2 : 0;
    // HasZone: Royale 或 Duos；HasCrates 照旧 = HasZone；OnlyMap: HasZone → RiverlandMap.Id
    // Respawns、JoinsMidMatch、HasBases: 双人吃鸡都是 false
    // DurationTicks: Duos 10 分钟（和吃鸡一样）
    // Id: Duos → "duos"；TryParse 照旧按 All 找
}
```

### 分队（任务 1；服务器任务 3、客户端任务 6 用）

```csharp
namespace Tank.Sim.Modes;

/// <summary>
/// How a duos room is teamed (stage 6 design §5.1), the server's rule and the client's mirror of it. <paramref name="counts"/>
/// is the seats on each team now (length <see cref="ModeRules.DuoTeams"/>).
/// </summary>
public static class DuoSeating
{
    /// <summary>A person coming into the room (or re-teamed when the room switches to duos, in slot order): the lowest-numbered empty team; none empty, the lowest with one seat; all full, <see cref="MatchRules.NoTeam"/>.</summary>
    public static byte ForPerson(ReadOnlySpan<int> counts);

    /// <summary>A bot seated at the match start (bot seats are filled in slot order): the lowest-numbered team with exactly one seat; none, the lowest empty one; all full, <see cref="MatchRules.NoTeam"/>.</summary>
    public static byte ForBot(ReadOnlySpan<int> counts);

    /// <summary>Whether a seat may move to <paramref name="team"/>: 0–7 with fewer than <see cref="ModeRules.DuoTeamSize"/> seats on it, not counting the mover (<paramref name="current"/>: its team now, or NoTeam).</summary>
    public static bool HasRoom(ReadOnlySpan<int> counts, byte team, byte current);

    /// <summary>The teams with at least one seat in <paramref name="counts"/>.</summary>
    public static int Manned(ReadOnlySpan<int> counts);
}
```

`ArgumentException` 当 `counts.Length != DuoTeams`。没有状态、不分配。

### 出生（任务 1）

```csharp
namespace Tank.Sim.Modes;

/// <summary>Where the two of a duos team appear (stage 6 design §2.2): beside their team's point, <see cref="Spacing"/> apart, facing as it does.</summary>
public static class DuoSpawns
{
    public const float Spacing = 10f;   // 两车中心相距，m

    /// <summary>A spot counts as clear with no prop's footprint within this of the hull's footprint, m.</summary>
    public const float PropClearance = 1f;

    /// <summary>
    /// Member <paramref name="member"/> (0 or 1) of the team whose point is <paramref name="point"/>: half <see cref="Spacing"/>
    /// to the left (member 0) or right (1) of the heading — or, when either of the two spots isn't clear on <paramref name="field"/>
    /// for a heavy's hull (a prop within <see cref="PropClearance"/>, water under it, or outside the playable area), behind (0)
    /// and ahead (1) instead. Same yaw as the point. Deterministic; no allocation.
    /// </summary>
    public static SpawnPoint Member(in SpawnPoint point, int member, Battlefield field);
}
```

### 规则（任务 1、2）

```csharp
public sealed partial class MatchRules
{
    // 构造：Royale 或 Duos（HasZone）都按种子洗出生点顺序 _spawnOrder；双人吃鸡而 spawns.Solo.Count < DuoTeams → InvalidOperationException（偏差 18）
    // Join：双人吃鸡 team > 7 → ArgumentOutOfRangeException(nameof(team))；!HasSpot(slot, team) → InvalidOperationException($"Team {team} is full.")
    //       实际队号 = 双人吃鸡 ? team : HasTeams ? team : slot
    //       双人吃鸡里离开过又入场的座位（只有开局那一 tick 能）先交还原来那一队的队员位，那一队没人了就清掉它的 _joinedTeams 位（偏差 18）
    // 出生（双人吃鸡，私有 DuoSpawn）：DuoSpawns.Member(_spawns.Solo[_spawnOrder[team]], 这一队最小的空队员位（0 左、1 右）, _sim.Field)，不再找空位、不挪；PickSpawn 只管其余模式

    /// <summary>
    /// How many places the results run to (stage 6 design §2.3: "共 N 名"): in duos the teams that joined this match, in every
    /// other mode <see cref="Entrants"/>.
    /// </summary>
    public int Places { get; }   // 双人吃鸡：_joinedTeams 的位数；其余 = Entrants

    /// <summary>Duos: the teams with a live tank (0 outside duos).</summary>
    public int AliveTeams { get; }

    /// <summary>
    /// Whether slot may join team as far as the team's member spots go — the one check behind Join's "Team N is full." (批次 B 跟进).
    /// Duos: team 0–7 with a member spot free; the spots of seats that have left stay taken, except the one this slot itself still
    /// holds there when it has left and joins again. Every other mode: true. No allocation.
    /// </summary>
    public bool HasSpot(byte slot, byte team);
}
```

- `Judge`：`case BattleMode.Duos:` `AliveTeams <= 1` → `EndDuos(Eliminated)`；否则到点 → `EndDuos(TimeUp)`。
- `EndDuos`（只在结束那一 tick 跑）：入场过的队（`_joinedTeams` 的位）各算：`alive`（活着的人数）、`health`（活着的人的血之和）、`outAt`（全队出局的 tick = 两人 `EliminatedAt` 的较大者；有人活着时不用）、`fallHealth`（在 `outAt` 那一 tick 出局的人的 `_healthBeforeFall` 之和）、`kills`（两人击毁数之和）。排序（稳定插入排序，n ≤ 8）：有人活着的在前，按 `health` 多的在前；出局的按 `outAt` 晚的在前，同 tick 按 `fallHealth` 多的在前；再按 `kills` 多的在前；再按队号小的在前。第 k 名的队两个座位的 `Rank` 都是 k。`WinnerTeam` = 第 1 名的队，`WinnerSlot` = `NoSlot`。
- 设计 §2.3 的“同一 tick 阵亡的并列，按剩余血量（都为 0 时按击毁数）分先后”按上面实现；剩余血量对出局的队就是“出局前一 tick 的血”（和 `EndSolo` 一样）。

### 线上格式 v7（任务 3）

- `BattleProtocol.Version = 7`。布局一位不改：模式字节接受 0～5（`ReadMode` 照 `ModeRules.LastMode`），房间名单、快照里的队号照旧 4 位。
- 意思上的变化（写进 `docs/03`）：双人吃鸡里 `MatchBlock.WinnerTeam` 是赢的队（0～7）、`WinnerSlot` 为 `NoSlot`；`MatchBlock.Entrants` 在有毒圈的模式里发 `rules.Places`（吃鸡是座位数，双人吃鸡是队数）；`PlayerStats.Rank` 在双人吃鸡里是队的名次；房间名单的 `Team` 在双人吃鸡里是 0～7。

### 服务器（任务 3、4）

- `AllocateSeat`：双人吃鸡 → `Team = DuoSeating.ForPerson(房间里各队的座位数)`；其余模式照旧 `ThinnerTeam`。
- `SetTeam`：双人吃鸡里 `team <= 7 && DuoSeating.HasRoom(counts, team, seat.Team)` 才改；两队模式照旧 `team <= 1`；其余模式忽略。
- `SwitchMode(mode)`：换成功后，**换到双人吃鸡**：所有真人座位按座位号顺序先清空再逐个 `ForPerson`；**从双人吃鸡换出去**（到任何模式）：所有真人座位按座位号顺序逐个 `ThinnerTeam`（团队模式的规则；各自为战和吃鸡里规则不看它，但房间外的不变量“非双人吃鸡队号 ≤ 1”要保持）。别的换法照旧不动队号。
- `TryStartMatch`：双人吃鸡里机器人 `Team = DuoSeating.ForBot(counts)`；开局条件除了 `seated ≥ 2`，双人吃鸡还要 `DuoSeating.Manned(counts) >= 2`，否则 `reason = "fewer than two teams"`（机器人撤回）。
- `PutIntoMatch`（对局中进来的真人）：`rules.HasSpot(seat.Slot, seat.Team)` 为假就留在房间等下一局，日志 `slot N: team T is full in this match; waiting for the next`（队号 ≥ 8 时 `slot N: has no team in this match; waiting for the next`）；不再自己数 `Standing`（批次 B 跟进）。
- `ViewerOf`：`SeesAllWhenDead` = `!ModeRules.HasAllies(_mode)` 或（双人吃鸡且 `rules.AliveCount(standing.Team) == 0`）。
- 箱子视野（`RefreshCrateSight`）：双人吃鸡里一个真人座位的“当前看得见” = 同队每辆活车的 `CrateSet.VisibleTo` 按位或（队友是机器人也算）；每队每次只算一遍（预分配的 `ulong[DuoTeams]` 暂存），不分配。吃鸡和各自为战照旧（各自一队，就是自己的）。“见过”“告知已没”照旧按座位记。
- `Build`：`Entrants = HasZone ? min(rules.Places, 16) : 0`。
- `WinnerText`：`HasAllies` 且 `WinnerTeam != NoTeam` → `team N`。
- 日志：开局那行照旧；双人吃鸡紧跟着多一行 `teams: 0+1 2+3 - 4 …`（第 0～7 队依次，每队的座位号（从 0 起，和日志里的 `slot N` 一样）用 `+` 连，没人的队写 `-`），结束那行照旧 `match ended: duos, team N, …`（任务 4 实施时改：原文写成了 `winner team N`，现有的结束行没有 winner 这个词）。

### 机器人（任务 5）

```csharp
public sealed partial class BotDirector
{
    public const float MateRange = 60f;     // 离活着的队友超过这个就去靠拢，m
    public const float MateArrive = 25f;    // 靠拢到这个以内就算到了，m
    public const float ZoneMateSpread = 12f; // 跑圈时跟随者的点比领队的点再往圈心挪这么多，m
    public const int RegroupQuietTicks = 5 * SimConstants.TickRate; // 靠拢放弃后这么久不再发起（领队这期间会折回）；批次 C 跟进加

    /// <summary>Duos: the slot in <c>Sim.Tanks</c> of <paramref name="me"/>'s team-mate (alive or a wreck), or −1 (outside duos, or none).</summary>
    internal int MateSlotOf(in TankState me);

    /// <summary>Duos: whether the bot <paramref name="bot"/> follows its team-mate: the mate is alive and is a person, or a bot with a lower tank id.</summary>
    internal bool Follows(Bot bot, in TankState me);

    /// <summary>
    /// The enemy <paramref name="me"/>'s team-mate is fighting, for the picker to prefer (0: none): a bot mate's <see cref="Bot.TargetId"/>;
    /// a person's, the tank it last hit (TankHit or SplashDamage) within <see cref="TargetPicker.HitMemoryTicks"/>.
    /// </summary>
    internal int MateTarget(in TankState me, int tick);
}

public sealed partial class Bot
{
    /// <summary>Duos: heading for its team-mate (<c>Leg.Mate</c>).</summary>
    public bool Regrouping { get; }

    /// <summary>Times it began heading for its team-mate this match.</summary>
    public int Regroups { get; }
}
```

- `TargetPicker.Pick(sim, vision, tick, int prefer = 0)`：排名的第一条变成“是 `prefer`”（只要它本来就是候选），其余照旧（最近打过我的、血最少、最近、id 小）；持有和复查的规则不变（换到队友的目标最迟在下一次复查）。
- `BotDirector.ZonePoint`：双人吃鸡里角度按**本队较小的那个坦克 id**（队友死了也一样，id 不变）而不是自己的；跟随者的点再往圈心挪 `ZoneMateSpread`（从圈心到点的方向上）。吃鸡里照旧。
- `Summary()` 在双人吃鸡里加 `N regroups; `（放在 `zone runs` 之后，前缀照旧）；`Describe()` 不变（任务 5 实施时改：`Describe` 是开局那行，那时靠拢次数总是 0；偏差 19）。

### 客户端（任务 6～8）

```csharp
namespace Tank.Client.UI;
public sealed record TeamChoice(byte Team, string Name, bool Full);   // Name "第 3 队"；Full：满 2 人且不是自己的队
public sealed record RoomRowView(..., bool Ghost, string Group = "");  // 双人吃鸡里 "第 N 队"，其余 ""
public readonly record struct StartPlan(int Humans, int Bots, int Team0, int Team1, int BotsToTeam0, int BotsToTeam1, int TeamsManned = 0);
// RoomModel 加：IsDuos、MyTeam（byte）、TeamChoices（IReadOnlyList<TeamChoice>，双人吃鸡 8 项，其余空）、CanPickTeam
// RoomRules 加：public static int[] DuoBots(RoomStateMessage room)  // 开局时每队会补几个机器人（长度 8），照 DuoSeating.ForBot

namespace Tank.Client.Match;
public enum PlateKind : byte { TeamKills, TeamAlive, SoloKills, DuoTeams }   // 末尾加
// HudText.Plate：双人吃鸡 → DuoTeams：Left = 本队击毁之和，Right = 有活车的队数，RightOf = match.Entrants（队数）
// 文案："本队击毁" / "存活队伍"

/// <summary>Who a dead player may watch (stage 6 design §2.3, §5.2): live team-mates in a mode with them; in duos, once none is left, every live tank; everyone alive in a free-for-all or royale.</summary>
public static class SpectateCandidates
{
    public static void Fill(BattleMode mode, byte ownTeam, IReadOnlyList<RemoteTank> others, List<int> into);
    /// <summary>Whether the camera may go free (F): no team-mates in the mode, or (duos) none of them alive.</summary>
    public static bool MayRoam(BattleMode mode, byte ownTeam, IReadOnlyList<RemoteTank> others);
}
```

- `MatchColors.SideOf`：`HasAllies(mode) && team == ownTeam ? Ally : Enemy`。
- `ScoreboardModel.Build`：双人吃鸡按队分组：进行中，有活车的队在前，再按队击毁之和多的在前，再按队号；结束后按服务器的 `Rank`；队内按座位号。名次栏：进行中是组的序号，结束后是服务器的队名次（两人一样）。`ScoreRow` 不变。
- `ScoreboardView`：双人吃鸡用吃鸡的列（伤害、存活时间）；不画队伍横幅，组和组之间画一条 2 px 的细线（不占行高：16 行照旧 550 px）；名字颜色：自己橙、队友蓝灰、其余白（照吃鸡的白，不用红：表里一片红读不清）。
- 结算：`HudText.Results` 在 `HasZone` 的模式里都走 `RoyaleResults`（本队名次、“共 N 名”是 `Entrants`）。
- 画廊新屏：`duos`（对局 HUD：比分牌两块、小地图上队友蓝灰、其余红、击毁提示里有队友）、`scoreboard-duos`（16 行 8 组）、`results-duos-win`（第 1 名，共 8 名）、`results-duos`（第 3 名）。

---

## 任务总览

| 批 | 任务 | 内容 |
|---|---|---|
| A 仿真 | 1 | 模式和分队：`BattleMode.Duos`、`ModeRules`、`DuoSeating`、`DuoSpawns`、`MatchRules` 入场和出生；`HasTeams` 在仿真里的每处过一遍 |
| | 2 | 按队判胜负和排名：`Judge`、`EndDuos`、`Places`、`AliveTeams` |
| B 联网 | 3 | 协议 v7 和房间：版本、模式值、分队、选队、换模式、开局的机器人和条件、专用服务器 `--mode duos` |
| | 4 | 对局里的服务器：`Viewer`、箱子视野按队、`Entrants`、日志；回环测试；soak 一行 |
| C 机器人 | 5 | 跟队友：领队 / 跟随、`Leg.Mate`、跑圈同一点、优先打队友的目标；整局 `DuoMatch`；开销 |
| D 客户端 | 6 | 房间：`RoomModel` / `RoomRules` / `ModeText`、选队下拉框、按队分组的名单、`--team N` |
| | 7 | 对局 HUD 逻辑：颜色、比分板分组、比分牌、结算、观战、画廊四屏 |
| | 8 | 视图和接线：比分板细线、比分牌、观战接线、画廊和真对局截图 |
| E 收尾 | 9 | 验收 |
| | 10 | 文档 |

---

## 批次 A：仿真

### Task 1：模式和分队

**Files:**
- Create: `src/Tank.Sim/Modes/DuoSeating.cs`、`src/Tank.Sim/Modes/DuoSpawns.cs`
- Modify: `src/Tank.Sim/Modes/BattleMode.cs`、`src/Tank.Sim/Modes/MatchRules.cs`（改成 `public sealed partial class`）
- Test: `tests/Tank.Sim.Tests/DuoModeTests.cs`、`DuoSeatingTests.cs`、`DuoSpawnTests.cs`、`DuoRulesTests.cs`；改已有的“模式全集”类测试（`ModeRules.All` 的个数、`LastMode`、`MaxPlayersFor` 的表）

**规则**

- `ModeRules` 按“共享接口”。`ModeText`（客户端）在任务 6 才加“双人吃鸡”：本任务之后客户端编译要过——`ModeText.Name` 的 `switch` 若没有默认分支会编不过或抛，先给 Duos 一个临时的 `"双人吃鸡"`（任务 6 再补规则说明）；客户端其余用到 `BattleMode` 的 `switch` 也照此处理，`grep -rn "BattleMode\." src/Tank.Client` 过一遍。
- **`HasTeams` 在仿真里的每处**（`grep -rn "HasTeams" src/Tank.Sim`）逐个判断：`MatchRules.Join` 的队号检查和实际队号、`OnDestroyed` 的队伍击毁数（只两队）、`PickSpawn`（双人吃鸡走自己的一支）、`BotDirector.Roles` 的 `RetreatTarget`（**不改**：双人吃鸡里机器人不撤退，见偏差 9）。改了哪些、没改哪些写进提交说明。
- `MatchRules` 构造里 `_spawnOrder` 的条件从 `mode == Royale` 改成 `ModeRules.HasZone(mode)`。
- **`Join`（双人吃鸡）**：`team > 7` → `ArgumentOutOfRangeException`；`_joinedTeams` 记队（`uint` 位）；`_teamSeats[team]`（这一队入场过的座位数，`int[DuoTeams]`）≥ 2 → `InvalidOperationException($"Team {team} is full.")`；出生点 `PickSpawn` 的双人一支：`DuoSpawns.Member(_spawns.Solo[_spawnOrder[team % n]], _teamSeats[team], _sim.Field)`，然后 `_teamSeats[team]++`。不找空位、不挪（两人的位置已经错开，河谷的 16 个各自为战出生点两两相距 ≥ 268 m）。批次 A 跟进改成：`_teamSeats` 换成每队 2 位的队员位掩码 `_teamMembers`、新入场的取最小的空位，开局那一 tick 离开又入场的座位先交还原位；构造时双人吃鸡要 ≥ 8 个各自为战出生点，于是不再 `% n`（偏差 18）。
- **`DuoSpawns.Member`**：右手方向 `side = (cos yaw, −sin yaw)`，前方 `forward = Angles.Forward(yaw)`；先试左右：0 号 `p − side × 5`、1 号 `p + side × 5`；两个位置都“空”才用，否则前后：0 号 `p − forward × 5`、1 号 `p + forward × 5`（前后也不空就还是用前后，测试保证河谷不会走到这一步）。“空”：重坦在这个位置、这个朝向的车身 `TankMotion.Footprint(TankCatalog.Heavy, at, yaw)` 和每个实心物件的占地（`Field.Props` 里 `PropSet.Blockers` / `IsSolid` 的 `Footprint`，树也算，灌木不算——车能开过灌木；偏差 17）距离 ≥ `PropClearance`（OBB 对 OBB：用 `Obb2` 现有的距离 / 相交方法；没有就按车身四角和物件 OBB 的 `DistanceToPoint` 近似，写进注释）、四角和中心都不在水里（`WaterDepthAt == 0`）、`ClampToPlayable(at) == at`。只用 `field` 的查询，不分配（物件用 `Field.PropsNear` 之类的空间索引若有就用，没有就遍历——只在开局调用 16 次）。
- **`DuoSeating`** 按“共享接口”，纯函数。

**先写的测试**

- `DuoModeTests`：`Duos == 5`；`LastMode == Duos`；`All` 六个、最后一个是 `Duos`；`Id(Duos) == "duos"`、`TryParse("DUOS")`；`MaxPlayersFor(Duos) == 16`、`MaxBotsFor == 15`；`HasZone`、`HasCrates` 真，`HasTeams`、`Respawns`、`JoinsMidMatch`、`HasBases` 假；`HasAllies`：三种团队模式和双人吃鸡真、各自为战和吃鸡假；`TeamsFor`：2/2/0/2/0/8；`OnlyMap(Duos) == "riverland"`、`AllowsMap(Duos, "sandbox")` 假；`DurationTicks(Duos) == 10 × 60 × 30`。
- `DuoSeatingTests`：
  - `ForPerson`：全空 → 0；`{1,0,…}` → 1；`{1,1,1,1,1,1,1,1}` → 0；`{2,1,1,…,1}` → 1；全满 → `NoTeam`；`{2,0,2,…}` → 1。
  - `ForBot`：全空 → 0；`{1,0,…}` → 0；`{0,0,1,…}` → 2；`{2,1,0…}` → 1；`{2,2,0,…}` → 2；全满 → `NoTeam`。
  - 一间房 1 个真人在 0 队 + 15 个机器人依次 `ForBot`：0 队 2 人、1～7 队各 2 人。3 个真人在 0、1、2 队 + 13 个机器人：每队 2 人。2 个真人同在 3 队 + 14 个机器人：机器人去 0、0、1、1、2、2、4、4、5、5、6、6、7、7。
  - `HasRoom`：满的队假、自己所在的满队（`current == team`）真、队号 8 假；`Manned`；长度不是 8 → `ArgumentException`。
- `DuoSpawnTests`（河谷，`RiverlandFixture.Field`）：16 个 `Solo` 点每个的 0 号、1 号：两车中心相距 10 m ± 0.01；朝向和点一样；重坦车身离每个实心物件（`PropSet.Blockers` / `IsSolid`，含树，不含灌木：和 `RiverlandSpawnTests.IsBlocking` 一样灌木不挡）≥ 1 m、四角和中心不在水里、在可开车范围里、**每个队员 6 m 内**坡度 < 8°（照 `RiverlandSpawnTests` 的查法；车身也都在出生点查过的 10 m 圆里，`RiverlandSpawnTests` 已保证那里 < 8°；为什么不是 10 m 见偏差 17）、和 `Solo[0]` 连通（`Passability`，照 `RiverlandSpawnTests` 的连通查法）；记下有几个点走了“前后”（预期 0，非 0 写进偏差）。平地上（`TestWorld.FlatField()`）左右；在左边 3 m 处放一块岩石 → 前后。
- `DuoRulesTests`（`RulesMatch` 帮手，河谷或平地都行，双人吃鸡，种子 7）：
  - 4 个座位队号 0、0、5、5：都入场；同队两车相距 10 m，0 队的点是 `Solo[order[0]]`（按 `SpawnSalt` 洗出的顺序，测试里照 `MatchRules` 的洗法自己算一遍），5 队的是 `Solo[order[5]]`；`Standing.Team` 是 0、0、5、5；`TankState.Team` 一样。
  - 第三个座位进 0 队 → `InvalidOperationException`；队号 8 → `ArgumentOutOfRangeException`。
  - 同队互相看得见、点亮共享：0 队一车看见一辆敌车后，另一车 `vision.IsVisible(敌车, 0)` 真（`VisionTracker` 按队，不用改，测一下）。
  - 队友互相打不算击毁、不算伤害（`OnHurt`、`OnDestroyed` 已按队判，测一下）。
  - 吃鸡（`Royale`）同样 4 个座位队号 0、0、5、5 → 队号照旧是座位号（回归）。
  - 同一种子两次开局出生位置逐位相同；种子 7 和 8 不同。

**步骤**

- [ ] 工作目录准备（见“执行前须知”）
- [ ] 写 `DuoModeTests`、`DuoSeatingTests`，确认失败；改 `BattleMode.cs`、写 `DuoSeating.cs`，通过；改已有的模式全集测试
- [ ] 写 `DuoSpawnTests`，确认失败；写 `DuoSpawns.cs`，通过
- [ ] 写 `DuoRulesTests`，确认失败；改 `MatchRules`，通过
- [ ] `HasTeams` 的每处过一遍；客户端临时补 `ModeText`；四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`feat(sim): duos — mode 5, team seating rules, team-mates spawn side by side, eight teams in the rules`，末行 `Tests: Sim 1400 → N.`

### Task 2：按队判胜负和排名

**Files:**
- Create: `src/Tank.Sim/Modes/MatchRules.Duos.cs`
- Modify: `src/Tank.Sim/Modes/MatchRules.cs`（`Judge` 加一支、`Places`/`AliveTeams` 的字段）
- Test: `tests/Tank.Sim.Tests/DuoRankTests.cs`

**规则**

- 按“共享接口”的“规则”一节。`AliveTeams`：活车所在队的个数（按 `_players` 里 `Active && Alive` 的 `Team` 取位，`BitOperations.PopCount`），只在双人吃鸡里算，其余模式 0。
- `EndDuos` 用预分配的暂存（`byte[DuoTeams]` 的队顺序、几个 `int[DuoTeams]`），不分配；`Finish(reason)` 照旧。
- 离开对局（`Leave`）算出局（`EliminatedAt` 记 tick），和各自为战一样；一队两人都离开也是出局。
- `Places`：双人吃鸡 `PopCount(_joinedTeams)`，其余 `Entrants`。
- 结束后 `Of(slot).Rank` 对入场过的座位都 ≥ 1；没入场的 0。

**先写的测试**（`DuoRankTests`，`RulesMatch` 帮手，双人吃鸡，`TankRef` 直接改血 / 毁车）

- 3 队（0、1、2 队各两人）：先毁 1 队一人 → 没结束；再毁 1 队另一人 → 没结束、`AliveTeams == 2`；毁 2 队两人 → 结束 `Eliminated`，0 队第 1、2 队第 2（后出局）、1 队第 3；两人名次一样；`WinnerTeam == 0`、`WinnerSlot == NoSlot`；`Places == 3`。
- 同 tick 出局：1 队、2 队最后一人同一 tick 被毁（一 tick 里两个 `TankDestroyed`），出局前 1 队剩 300 血、2 队剩 100 → 1 队排前；两队出局前血一样（都 0：同一 tick 先被打到 0 的情形用 `_healthBeforeFall` 都设 0）→ 击毁数多的在前；再一样 → 队号小的在前。
- 一队里一人早死、一人晚死：队的出局时刻按晚的算（早死那人的 tick 不影响）。
- 到点（`durationTicks: 60`）：两队都还活着，0 队活着两人共 800 血、1 队活一人 900 血 → 1 队第 1（`TimeUp`）；`Rank` 照此。
- 只剩一队一开始就结束（只有一队入场）：第一次 `Update` 就 `Eliminated`，那队第 1。
- 离开：1 队两人都 `Leave` → 算出局；`Places` 不减。
- `AliveTeams` 在吃鸡里是 0；吃鸡的排名（`RoyaleRulesTests`）不变（跑一遍）。
- `Allocation.AssertNone`：预热后跑到结束（含 `EndDuos` 那一 tick）不分配。

**步骤**

- [ ] 写 `DuoRankTests`，确认失败；写 `MatchRules.Duos.cs`、改 `Judge`，通过
- [ ] 四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`feat(sim): duos — the last team left wins, teams ranked by when their last tank fell, places for the results`，末行 `Tests: Sim N → M.`

**批次 A 收尾**：两路审查 → 跟进提交 → 进度（6C 节点记录 0、A 两行）。

---

## 批次 B：联网

### Task 3：协议 v7 和房间

**Files:**
- Modify: `src/Tank.Net/Battle/BattleProtocol.cs`、`BattleServer.Room.cs`、`BattleServer.Match.cs`（`TryStartMatch`）、`BattleServer.cs`（`RoomInfoMessage` 的座位数已按 `MaxPlayersFor`，核对）、`src/Tank.Server/Program.cs`
- Test: `tests/Tank.Net.Tests/Battle/DuoRoomTests.cs`、`DuoWireTests.cs`；改已有的版本号断言

**规则**

- `Version = 7`；`ReadMode` 已按 `LastMode`，核对模式 5 往返、6 被拒。
- 房间按“共享接口”的“服务器”一节：`AllocateSeat`、`SetTeam`、`SwitchMode`、`TryStartMatch`。房间里各队的座位数：`_seats` 里非空座位（真人，含重连宽限期里的；房间里还没有机器人）按 `Team` 数，换队的人自己不算（`HasRoom` 的 `current`）。
- `BroadcastRoom` 的名单队号：`HasTeams || IsDuos` → `s.Team`，其余 → `s.Slot`（照旧）。
- `HasTeams` 在联网里的每处（`grep -rn "HasTeams" src/Tank.Net src/Tank.Server`）逐个判断，改了哪些写进提交说明。
- 专用服务器：用法行和说明加 `duos`；`--mode duos --map sandbox` → 退出码 2（`OnlyMap` 已覆盖，测一下）。

**先写的测试**

- `DuoWireTests`：`RoomStateMessage` 模式 5、名单队号 7 往返；`MatchStartMessage` 模式 5 往返；模式 6 → `ProtocolException`；`Version == 7`。
- `DuoRoomTests`（照现有房间测试的回环帮手）：
  - 双人吃鸡的房间里依次进 3 个真人 A、B、C：队 0、1、2。B `SetTeam 0` → A、B 同在 0 队；C `SetTeam 0` 被拒（满）、`SetTeam 7` 成功、`SetTeam 8` 和 `SetTeam x` 忽略；A `SetTeam 0`（自己的队）不变。
  - 团队对战的房间 4 人（队 0、1、0、1）→ 房主换到双人吃鸡：队 0、1、2、3（按座位号逐个 `ForPerson`）；再换回团队对战：队 0、1、0、1（按座位号逐个 `ThinnerTeam`）；换到吃鸡 → 队号不动；吃鸡 → 双人吃鸡 → 重新分。
  - 1 个真人 + `SetBots 15`，开局：16 个座位，真人在 0 队、机器人 1 也在 0 队，其余两两在 1～7 队；`Standing.Team` 和座位的 `Team` 一致；房间名单的队号照此。
  - 1 个真人 + 1 个机器人 → 开不了（`fewer than two teams`），机器人撤回；+ 2 个机器人 → 能开（0 队 2 人、1 队 1 人）。
  - 2 个真人同在 3 队 + 0 个机器人 → 开不了；一人换到 4 队 → 能开。
  - 11 个真人的吃鸡房间 → 换到双人吃鸡成功（16 座）；换到团队对战照旧被拒。
  - 换到双人吃鸡时地图换成河谷（`OnlyMap`，照吃鸡的测试）。
- 专用服务器：`--mode duos` 起得来，日志有 `duos, map riverland`；`--mode duos --map sandbox` 退出码 2。

**步骤**

- [ ] 写 `DuoWireTests`、`DuoRoomTests`，确认失败；改协议版本、房间，通过
- [ ] 改 `Program.cs`；四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`feat(net): protocol v7 — duos rooms: pick one of eight teams, newcomers on an empty one, bots fill the singles then pair up`，末行 `Tests: Net 465 → N.`

### Task 4：对局里的服务器

**Files:**
- Modify: `src/Tank.Net/Battle/Visibility.cs`（只改注释和 `SeesAll` 的说明）、`BattleServer.Match.cs`（`ViewerOf`、`Build`、`WinnerText`、日志）、`BattleServer.Crates.cs`（`RefreshCrateSight`）、`tests/Tank.Soak/BattleSoak.cs`
- Test: `tests/Tank.Net.Tests/Battle/DuoServerTests.cs`；`BotLoadTests` 加双人吃鸡一行

**规则**

- 按“共享接口”的“服务器”一节。`ViewerOf` 里“全队出局”用 `rules.AliveCount(standing.Team) == 0`。
- 箱子视野：`RefreshCrateSight` 先清 `_teamCrateSight`（`ulong[ModeRules.DuoTeams]`）和一个“这一队算过了”的位；对每个真人座位：双人吃鸡里取它的队，没算过就把同队每辆活车的 `VisibleTo` 或起来；吃鸡和各自为战照旧只算自己的车。计时照旧包住整遍。
- `Build`：`Entrants` 发 `Places`；其余不变（队友的伤害在进行中可见：已经是 `p.Team != viewer.Team ? 0 : …`）。
- **满队到不了 `Join`**：`MatchRules.Join` 在双人吃鸡里会抛 `InvalidOperationException("Team N is full.")`（批次 A 跟进）。`BattleServer.Match.cs` 的 `PutIntoMatch`（`BattleServer.Room.cs` 在 `AcceptsJoins` 时调用）不能拿满队去调它：房间按 `DuoSeating` 分队、座位的队号本来就合法，这里再加一道守卫（满了就记日志、不入场，或断言），并测一下满队的座位走不到 `Join`。
- soak：加一行 `duos wifi 30±20ms 5% loss`（`Mode: BattleMode.Duos, Bots: 12, Riverland: true`，其余照吃鸡那行），阈值照吃鸡那行。
- `BotLoadTests`：吃鸡那行的双人吃鸡版本（1 真人 + 15 机器人，Release 下打印 tick p50/p99、快照字节）；上限和吃鸡一样。

**先写的测试**（`DuoServerTests`，回环；照 `CrateServerTests` / `RoyaleServerTests` 的帮手，`CratePlacerOverride` 手摆箱子，`TankRef` 摆车）

- 两个真人 A、B 同在 0 队，一个机器人 C 在 1 队（再加一个 D 凑两队）：A 的快照里总有 B（队友）；B 看见 C 时 A 的快照里也有 C（点亮共享，`Viewer.Sees` 已按队）。
- A 阵亡、B 活着：A 的快照只有队里看得见的车（不是全场）；B 也阵亡后：A 的快照有全场的车。吃鸡里阵亡就看全场（回归）。
- 箱子：一个箱子只在 B 的视野里（A 那边被房子挡住、或在 A 的视野外）→ 下一次视野更新后 A 的快照有这一行、`CrateKnownOf(A)` 有这一位；B 吃掉它 → A 的快照里这一行 `Standing = false`、A 没收到 `CrateMessage`（只有 B 收到）。只在 1 队视野里的箱子 A 的快照没有。
- 一个真人 + 一个机器人队友：机器人看见的箱子进真人的快照。
- `MatchBlock.Entrants`：4 队开局 → 4；吃鸡 16 个座位 → 16（回归）。
- 结束：`WinnerTeam` 是赢的队，日志 `match ended: duos, team N, …`；比分板 `Rank` 两人一样。
- 开局日志有 `teams:` 一行。
- 不分配：预热后 300 tick 的服务器 tick（双人吃鸡、2 真人 + 6 机器人）不分配（照现有的服务器分配测试）。
- 满队：开局时一队已有两个座位入场，第三个座位（不管怎么来的）不会走到 `MatchRules.Join`、服务器不抛。

**步骤**

- [ ] 写 `DuoServerTests`，确认失败；改服务器，通过
- [ ] soak 和 `BotLoadTests` 加行，Release 跑一遍记数
- [ ] 四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`feat(server): duos — the dead watch through their team-mate's eyes, team-mates share the crates they see, places in the match block`，末行 `Tests: Net N → M.`

**批次 B 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 C：机器人

### Task 5：跟队友

**Files:**
- Create: `src/Tank.Sim/Ai/Bot.Duo.cs`、`src/Tank.Sim/Ai/BotDirector.Duo.cs`
- Modify: `src/Tank.Sim/Ai/Bot.cs`（`Leg.Mate`、`Command` 里调 `WatchMate`、`NextWaypoint` 的跟随一支、各处 `switch`）、`Bot.Zone.cs`、`BotDirector.cs`（`_duos`、命中记录、`Describe`）、`BotDirector.Zone.cs`（`ZonePoint`）、`TargetPicker.cs`（`prefer`）
- Test: `tests/Tank.Sim.Tests/BotDuoTests.cs`、`DuoMatch.cs`、`DuoMatchTests.cs`；`BotArena` 加 `Duos(map, seed)`

**规则**

- **谁跟谁**：双人吃鸡里，一个机器人的队友活着时：队友是真人，或者是坦克 id 更小的机器人 → 它是**跟随者**；否则是**领队**（队友死了就各自为战，照吃鸡）。
- **跟随者不自己闲逛**：它的 `NextWaypoint`（闲逛选下一个点、跑圈结束、捡箱结束都走这里）在队友活着时改成就地 `Leg.Arrived`（等着），队友离开 `MateRange` 时 `WatchMate` 带它去。
- **`WatchMate`**（每个命令，`WatchZone` 之后、`CheckArrival` 之前）：双人吃鸡、队友活着、自己不在跑圈（`RunningToZone` 假）、不在捡箱（`Leg.Crate`）、不在暂停（`Leg.Paused`）时：
  - 不在 `Leg.Mate`：离队友 > `MateRange`，且队友在本圈里（`BotDirector.InRing(队友位置, Zone.Target)`；没毒圈时不查），且队友不是一个正在靠拢**自己**的机器人（`director.Find(mateId) is { Regrouping: true }` 且它的队友是我）→ `Begin(Leg.Mate, 队友位置)`、`Regroups++`。；领队（队友是跟随者机器人）另外要等跟随者自己来：跟随者能靠拢（不在跑圈、捡箱、暂停）时领队不折回（任务 5 实施时加：按坦克 id 的命令顺序领队先看，照原文领队会先折回；偏差 19）
  - 在 `Leg.Mate`：离队友 ≤ `MateArrive` → 跟随者 `Leg.Arrived`、领队 `NextWaypoint`；否则每 `BotDirector.ReplanTicks` 把 `Goal` 更新成队友现在的位置（`_needPath`，照撤退的做法）。队友死了 / 离开圈 / 自己开始跑圈 → 结束（跟随者变领队、照吃鸡闲逛）。
- **批次 C 审查跟进加的规则**（偏差 19 (e)～(i)）：跟随者在等队友（`Leg.Arrived`）时队友阵亡或离开，立刻照吃鸡闲逛（不再等到下一圈）；领队闲逛时跟随者正在靠拢它、且相距 > `MateRange`，领队就地等（跑圈优先，等的时候照常交战），跟随者不靠拢了（到了、放弃、阵亡、离开）再接着闲逛；领队折回看的是跟随者“能不能靠拢”（`CanRegroup`：不在跑圈、捡箱、暂停，也不在放弃后的 `RegroupQuietTicks` 冷却里）；靠拢的路线出圈就放弃这次靠拢（进冷却），不出圈再跑圈；跟随者只捡离活着的队友 `MateRange` 以内的箱子；靠拢在 `MateArrive` 加上当前刹车距离处结束（停在 25 m 左右，不再滑到队友跟前）；队友没动（离 `Goal` 不超过 `WaypointRadius`）不重新要路；记“真人最后打中谁”时不记打本队的；机器人队友的目标暂时丢失（`TargetLost`）时不算它在打谁。
- `Leg.Mate` 进 `Bot.cs` 各处 `switch`（`Revived`、`_needPath`、`Steering.Toward` 的 `stopThere` 不停、`CheckArrival`）；`onTheWay` 不含它（靠拢时照常停下交战）。
- **跑圈**：`ZonePoint` 按“共享接口”：`key = 双人吃鸡 ? 本队较小的坦克 id : bot.TankId`；跟随者（按上面的规则，开局两人都活着时就定了；用 `Follows`）的点从圈心方向再往里挪 `ZoneMateSpread`（不越过圈心：挪的距离不超过点到圈心距离的一半）。`ZoneWanderPoint` 不改（跟随者不闲逛）。
- **优先目标**：`Command` 里 `Picker.Pick(sim, vision, tick, director.MateTarget(me, tick))`。`MateTarget`：队友是机器人 → 它的 `TargetId`；是真人 → 导演记的“这辆车最近打中谁、哪个 tick”（`BotDirector` 已在读 `TankHit` / `SplashDamage` 给 `NoteHit`，顺手记每辆车的最后一次命中：`int[MaxPlayers]` 两个数组按 `Sim` 的车序号或 id 存），`HitMemoryTicks` 内有效、目标还活着。不是候选（看不见、太远）就照旧排。
- 吃鸡里一切照旧：`_duos` 假时 `WatchMate`、`MateTarget`、`ZonePoint` 的改动都不生效（测试回归：吃鸡整局的数字和改动前逐位相同——`RoyaleMatchTests` 的回放测试）。
- 不分配、确定。

**先写的测试**

- `BotDuoTests`（`BotArena.Duos`，平地或河谷的小场景，`TankRef` 摆车）：
  - 真人 P（不动）和机器人 B 同队，B 在 P 80 m 外闲逛 → 若干 tick 内 B `Regrouping`、`Goal` 在 P 附近；到 25 m 内停下（`Activity` Holding）；P 挪到 70 m 外（`TankRef` 改位置）→ B 又去；`Regroups == 2`。
  - 两个机器人 B1（id 小）、B2 同队相距 100 m：B2 是跟随者，靠拢；B1 不因为 B2 在靠拢自己而折回（`B1.Regrouping` 假）。
  - 跟随者到了之后不自己闲逛（几百 tick 里位置不离开 25 m 以外，除非领队走了）。
  - 队友在圈外时不去靠拢；自己在跑圈时不去靠拢；捡箱途中不打断。
  - 队友死了：跟随者变回闲逛（`Leg.Waypoint` 或吃鸡的圈内闲逛）。
  - 跑圈：同队两个机器人的 `ZonePoint` 角度一样、跟随者的点离圈心近 12 m（或一半）；吃鸡里两个机器人角度不同（回归）。
  - 优先目标：B 和队友 B' 同时看得见敌车 E1（近、满血）和 E2（远、满血）；B' 的目标是 E2 → B 选 E2；没有 `prefer` 时 B 选 E1（回归）。真人队友：P 打中 E2（造一个 `TankHit` 事件进导演）→ B 选 E2；3 s 后失效。
  - `Allocation.AssertNone`：预热后 600 tick 不分配。
- `DuoMatch` / `DuoMatchTests`（照 `RoyaleMatch` / `RoyaleMatchTests`；或者把 `RoyaleMatch.Play` 参数化成模式，二选一，写进提交说明）：座位 0 是不动的真人（中坦）在 0 队，15 个机器人按 `DuoSeating.ForBot` 分队、按 `BotSeating` 抽车型和种子；整局跑到结束或 10 分钟。记：结束原因和时刻、赢的队、各队名次、每 tick 两人都活着的队里两车的距离（中位数、超过 100 m 的 tick 占比）、靠拢次数、跑圈次数、机器人吃的箱子数、最长卡住、圈结束时圈外的机器人。
  - 种子 3、7、11、19（带弹）：都在 10 分钟内分出胜负（`Eliminated` 或 `TimeUp` 都行，记）、名次 1～N 每个名次正好一队、两人名次一样、`Places` == 8；机器人吃到箱子 ≥ 3；最长卡住 < 6 s（照吃鸡整局的上限）；队友距离中位数 ≤ 60 m（**按实测定**：先跑出数字，下限写成实测的 1.5 倍以内，写进偏差）。
  - 不带弹的一局（照 `RoyaleBots.Unarmed`）：只有毒圈杀人，打满或接近 10 分钟，最后一圈结束时圈外 0（第 5 圈不断言，照吃鸡）。
  - 同一种子回放两次逐位相同（事件序列、名次）。
  - Release 下打印一行 `duos seed S: … ended at … winner team T, regroups R, mate distance median M m (P% over 100 m), crates C, stuck X s`。

**步骤**

- [ ] 写 `BotDuoTests`，确认失败；写 `Bot.Duo.cs`、`BotDirector.Duo.cs`、改 `TargetPicker`、`ZonePoint`，通过
- [ ] 写 `DuoMatch` 和测试，Release 跑出数字，按实测定下限
- [ ] `RoyaleMatchTests` 回放、`BotLoadTests` 吃鸡和双人吃鸡两行（Release）
- [ ] 四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`feat(bots): duos — bots keep within 60 m of their team-mate, run to one point of the ring, fire at the mate's target`，末行 `Tests: Sim N → M.`

**批次 C 收尾**：两路审查 → 跟进提交 → 进度（写整局的数字）。

---

## 批次 D：客户端

### Task 6：房间

**Files:**
- Modify: `src/Tank.Client/scripts/UI/Logic/RoomModel.cs`、`RoomRules.cs`、`ModeText.cs`、`src/Tank.Client/scripts/UI/RoomScreen.cs`、`src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（和把它交给房间的地方）
- Test: `tests/Tank.Art.Tests/RoomDuoModelTests.cs`；`RoomRulesTests` 的服务器对照测试加双人吃鸡；`ModeTextTests`、`LaunchOptionsTests`

**规则**

- `ModeText`：`Name(Duos) = "双人吃鸡"`；`Rule(Duos)` = `$"最多 {RoyaleMaxPlayers} 辆车，两人一队共 {DuoTeams} 队，队友共享视野；毒圈缩小 {ZoneSchedule.Rings} 次，圈外掉血；一队两人都被击毁才出局，最后留下的一队吃鸡，{minutes} 分钟后按剩余血量定名次。"`
- `RoomRules.PlanFor`：双人吃鸡里 `Team0/Team1/BotsTo*` 都 0，`TeamsManned` = 真人所在的队加上 `DuoBots` 补出来的队的个数。`DuoBots(room)`：按真人的队数一遍 `counts`，再 `bots` 次 `ForBot` 累加，返回每队补几个。`StartBlockReason`：双人吃鸡 `TeamsManned < 2` → `"至少要有两队有车：让朋友选别的队，或加几个机器人。"`（放在“至少两辆坦克”之后）。`NotHereReason`、`MapLockedHint` 的文案对双人吃鸡也成立（“吃鸡只在河谷开”），不改。
- `RoomModel`（双人吃鸡）：两列，第 1 列是 0～3 队、第 2 列 4～7 队（一队不会被拆到两列）；每列里按队号、队内按座位号排，每队之后跟它的机器人占位行（“机器人 × 1 · 开局时补入”）；只列有人或会补机器人的队；每行 `Group = "第 N 队"`。第 1 列标题 `队伍 · K`（K 是开局会有车的队数），第 2 列标题空（照吃鸡两列的做法）。`TeamChoices` 8 项，`Full` = 那队已有 2 人且不是自己的队；`MyTeam`；`CanPickTeam = IsDuos && me is not null`；`CanSwitchTeam` 照旧只两队模式真。`Signature` 带上 `Group` 和 `TeamChoices`。
- `RoomScreen`：双人吃鸡里藏起“换到对面”，显示一个“选队”下拉框（`OptionButton`，8 项“第 N 队”，满的禁用、提示“已满”，选中自己的队；选了发 `SetTeam`）；名单每行左边一小格写 `Group`（同队第二行留空），组和组之间留 6 px 的空（不要横幅）；16 行 + 8 组在 1920×1080 里放得下（截图看）。
- **`--team N`**（1～8；超出范围警告并忽略）：`LaunchOptions.Team`（0 起的 `byte?`）；进了房间（收到第一份 `RoomState`、不在对局里）发一次 `SetTeam(Team)`（房主、客人都行；两队模式里 1、2 有效）。用于验收的两人同队截图。

**先写的测试**

- **先恢复任务 3 临时去掉的那一行**：`tests/Tank.Art.Tests/RoomRulesTests.cs` 里“双人吃鸡、2 个真人同在 0 队、0 个机器人”（两张图）那一行在任务 3 里注释掉了（服务器已拒“fewer than two teams”，客户端 `RoomRules` 还不知道双人吃鸡），本任务把它放回并让客户端判断一致（Art 测试数因此 1490 → 1488，本任务补回）。

- `RoomDuoModelTests`：
  - 房主一人在 0 队 + 15 个机器人：第 1 列 0 队（房主、机器人 × 1）、1～3 队（各“机器人 × 2”）；第 2 列 4～7 队；标题 `队伍 · 8`；`TeamChoices[0].Full` 假（自己的队）、其余空队不满。
  - 3 个真人在 0、0、5 队 + 3 个机器人：0 队满（别人看是 `Full`），机器人补 5 队一个、1 队两个；标题 `队伍 · 3`。
  - 1 人 + 1 个机器人：`CanStart` 假，提示“至少要有两队有车…”；+ 2 个机器人：能开。
  - 吃鸡房间的列、标题、`TeamChoices` 空（回归：`RoomRoyaleModelTests` 全过）。
- `RoomRulesTests`：现有的“和服务器同一结论”的对照测试加双人吃鸡的几种房间（含不能开的两种），客户端 `StartBlockReason` 为 null ⇔ 服务器 `TryStartMatch` 成功；`DuoBots` 和服务器实际补的队一致。
- `ModeTextTests`：名字、规则里有“两人一队”。`LaunchOptionsTests`：`--team 3` → 2；`--team 9`、`--team x` 警告。

**步骤**

- [ ] 写测试，确认失败；改 `ModeText`、`RoomRules`、`RoomModel`，通过
- [ ] 改 `RoomScreen`、`LaunchOptions` 和接线；`dotnet build`、`--import`
- [ ] 截图：`room-duos.png`（房主 + 15 个机器人，见“执行前须知”）、吃鸡房间 `room-royale.png`（回归，和 6A 的同类图一样）、两人同队：`$S/pair4b.sh` 房主 `--host --mode duos --bots 4 --team 1 …`、客人 `--join 127.0.0.1 --team 1 …`，两张都看见两人在第 1 队；都看过
- [ ] 试驾场六张回归（先拍对照）；四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`feat(client): duos rooms — pick one of eight teams, the list grouped by team with the bots to come, --team`，末行 `Tests: Art 1478 → N.`

### Task 7：对局 HUD 逻辑

**Files:**
- Create: `src/Tank.Client/scripts/Match/Logic/SpectateCandidates.cs`
- Modify: `src/Tank.Client/scripts/Match/Logic/MatchColors.cs`、`ScoreboardModel.cs`、`HudText.cs`、`HudDemoFrames.cs`
- Test: `tests/Tank.Art.Tests/DuoHudTests.cs`、`SpectateCandidatesTests.cs`；`HudDemoFramesTests`（新屏名）

**规则**

- 按“共享接口”的“客户端”一节。
- `HasTeams` 在客户端的每处（`grep -rn "HasTeams\|BattleMode.Royale" src/Tank.Client/scripts`）逐个判断：颜色、比分板分组、名次栏、结算、观战、房间、比分牌、`MatchMain` 里的吃鸡判断（`== BattleMode.Royale` 的地方多半要改成 `HasZone`）。改了哪些写进提交说明。
- 比分板“名次”的规则见“共享接口”。`ScoreboardModel` 不分配（排序照旧插入排序；队击毁之和在排序前按队累加进一个 `int[DuoTeams]` 的暂存——`Build` 是静态的：用 `stackalloc`）。
- `HudDemoFrames`：四个新屏（“共享接口”），数据照 `royale` / `results-royale-win` 的写法：8 队、16 个名字、各自的击毁 / 伤害 / 存活时间；`duos` 屏的小地图上自己、队友（蓝灰）、几辆红的敌车和几个箱子，击毁提示里一条“队友击毁敌人”、一条“敌人击毁队友”。原有的屏一个字节都不变。

**先写的测试**

- `DuoHudTests`：
  - `SideOf(Duos, 3, 3, false) == Ally`、`(Duos, 3, 4, false) == Enemy`；吃鸡里同队号（座位号恰好相等不会发生，但测）仍是 `Enemy`；团队模式照旧。
  - 比分板：8 队 16 行，进行中：有活车的队在前、再按队击毁之和、再按队号，队内按座位；名次栏是组序号（两人一样）；结束后按服务器 `Rank`；`Side` 自己 / 队友 / 敌人。
  - 比分牌：自己 0 队（自己 2 杀、队友 1 杀）、活着的有 0、2、5 队、开局 8 队 → `DuoTeams`，`Left 3`、`Right 3`、`RightOf 8`；文案“本队击毁”“3”“存活队伍”“3/8”。
  - 结算：自己 `Rank 1` → “吃鸡！”、“共 8 名”；`Rank 3` → “第 3 名”。
- `SpectateCandidatesTests`：双人吃鸡，队友活着 → 只有队友，`MayRoam` 假；队友死了 → 所有活车、`MayRoam` 真；吃鸡：所有活车、`MayRoam` 真；团队对战：活着的队友、`MayRoam` 假（回归）。
- `HudDemoFramesTests`：四个新名字都能建；原有的名单不变。

**步骤**

- [ ] 写测试，确认失败；改逻辑，通过
- [ ] 四个测试项目全过、五个项目 0 警告（视图还没接：本任务不截图，任务 8 截）
- [ ] 提交

**提交**：`feat(client): duos HUD logic — team-mates in the ally colours, the board grouped by team, team kills and teams left, the team's place`，末行 `Tests: Art N → M.`

### Task 8：视图和接线

**Files:**
- Modify: `src/Tank.Client/scripts/Match/ScoreboardView.cs`、`ScorePlate.cs`、`MatchMain.cs`、`MatchMain.Camera.cs`（`FillCandidates` 用 `SpectateCandidates`、F 键用 `MayRoam`）、`HudGallery.cs`（若新屏要登记）、`MatchMain.Events.cs`（若击毁提示要改）
- Test: 无新的单元测试（视图），截图验收

**规则**

- `ScoreboardView`：双人吃鸡 `_royale` 列（用 `HasZone`），`_teams` 横幅只两队模式；双人吃鸡在组间画细线（共享接口）；名字颜色按共享接口。高度 `Height(0, 16)` 照旧 550。
- `ScorePlate`：`PlateKind.DuoTeams` 的两块。
- 观战：`FillCandidates` → `SpectateCandidates.Fill`；`teamless` → `SpectateCandidates.MayRoam`（每帧调，不分配）。死后先跟随队友（`SpectateState.OnDeath(autoFollow: true)` 已对不复活模式开着，候选里只有队友时就跟队友）。
- 名字条、车身、小地图、击毁提示的颜色都经 `MatchColors.SideOf`，不用另改；截图确认队友蓝灰、敌人红。
- 结算页：吃鸡的那一套（标题、副标题），表按任务 7 的分组。

**截图**（`build/shots/6c-t8/`，都看过、写进进度）

- 画廊：`duos`、`scoreboard-duos`、`results-duos-win`、`results-duos`；原有九屏（`royale`、`royale-outside`、`royale-map`、`results-royale-win`、`results-royale`、`royale-crates`、`scoreboard`、`results-ffa`、`results-win`）和 6B 的 `build/shots/6b-t10/` 或重拍的 HEAD 对照 `cmp` 逐字节相同。
- 真对局（种子 11，`--autodrive`）：60 s 时第三人称里队友在镜头里（名字条蓝灰；`--camera-preset` 用现有的能看见队友的那个，或开局 8 s 时截——两人并排出生），一张带敌车的（名字条红，找有敌车在视野里的时刻：`--shot-after` 里有能用的就用，没有就多试几个秒数）；全屏图 `--full-map` 上队友蓝灰；死后观战跟着队友（`--shot-after death:8`，不开车：玩家不动、很快被打死；看观战条写的是队友的名字）；结束页（`--shot-after ended:2`）。
- 帧率：一局 `--fps-log` 的汇总，和 6B 的 113～114 比。
- 试驾场六张回归（和对照比）。

**步骤**

- [ ] 改视图和接线；`dotnet build`、`--import`
- [ ] 画廊截图、`cmp` 原有屏；真对局截图；帧率
- [ ] 试驾场六张回归；四个测试项目全过、五个项目 0 警告；`pgrep` 没有残留
- [ ] 提交

**提交**：`feat(client): duos in the match — the grouped board and plate drawn, the dead follow their team-mate first`，末行 `Tests: unchanged (Sim N, Net N, Art N, Update 42).`

**批次 D 收尾**：两路审查 → 跟进提交 → 进度（写截图看到了什么）。

---

## 批次 E：收尾

### Task 9：验收

不改代码（发现问题就修，修的提交单独写 `fix(<scope>): …`）。写 `accept6c.sh` 放在开发脚本目录（照 `accept6b.sh`，阶段：`build release soak gallery room match idle pair modes sandbox server smoke left`）跑下面这些，结果写 `build/shots/6c-t9/RESULTS.md`。

- [ ] 五个项目 0 警告；四个测试项目全过（记数字；Update 42 不变）。
- [ ] Release：`DuoMatch` 整局（全部种子）、`RoyaleMatch` 整局（回归）、`BotLoadTests` 两行（吃鸡、双人吃鸡的 tick p50/p99、快照字节；对比 6B 的 p99 0.937 ms）。
- [ ] soak：原有五行和双人吃鸡一行全过，记吃鸡和双人吃鸡的下行 KB/s（6B 吃鸡 4.0 KB/s）。
- [ ] 整局（设计 §1 的 6C 验收）：`--host --mode duos --map riverland --bots 15 --autostart --autodrive --seed 5 --fps-log --shot-after ended:2 --screenshot …/duos-ended.png --quit-after 700`：服务器日志 `match started: duos`、`teams:` 一行、`match ended: duos, team N, …`；结算页看过。第二局不开车（`--shot-after death:8`，观战跟队友）。都记：用时、赢的队、玩家那队的名次、机器人靠拢次数、吃的箱子。
- [ ] 截图（设计 §6）：房间分队（`room-duos`、两人同队的 `pair`）、名字条颜色（队友蓝灰、敌人红）、小地图和全屏图上的队友、比分板分组、结算页（第 1 名和第 N 名：画廊各一张，真对局一张）、观战跟队友。
- [ ] 回归：原有五种模式各开一局短的（吃鸡、团队对战、团队歼灭、各自为战、标准战；截图和 6B 同类的一样）；试驾场六张；画廊原有屏逐字节相同；吃鸡整局回放测试逐位相同。
- [ ] 专用服务器 `--mode duos` 日志、`--mode duos --map sandbox` 退出码 2；冒烟参数仍够用。
- [ ] `pgrep -fl 'MacOS/Godot'` 没有残留。

**提交**：没有代码改动就不提交，结果记 `RESULTS.md` 和进度（下一个任务一起提交）。

### Task 10：文档

**Files:** `docs/01-game-design.md`、`docs/03-netcode.md`、`docs/04-architecture-and-plan.md`、`README.md`、`docs/README.md`（若文档表要动）、`docs/PROGRESS.md`

内容以代码为准，不写没做的东西：

- `docs/01`：模式表加双人吃鸡；吃鸡一节加“双人吃鸡”小节（分队、出生、共享视野和箱子、按队排名、结算、观战、机器人跟队友）；HUD 表的比分牌和比分板分组；原来写“双人吃鸡（6C）还没做”的地方改掉；中途离开的队友不再算进本队击毁和进行中的比分板排序（偏差 20 (d)）。
- `docs/03`：协议 7（版本历史一句：只多了模式值 5）、`Entrants` 和 `WinnerTeam`、`Rank` 在双人吃鸡里的意思、视野过滤里“死了先看队友的、全队出局看全场”、箱子视野按队取并、soak 双人吃鸡一行的数。
- `docs/04`：仓库结构加新文件；`HasTeams` / `HasAllies` 的分工一段；测试表；§6 路线 6C ✅（阶段 6 完成）；风险表的 6C 一行写实测。
- `README.md`：进度表 6C ✅、特性一句里加双人吃鸡。
- `docs/PROGRESS.md`：6C 节点记录最后一行，“当前位置”改为 6C 完成、阶段 6 全部做完、下一步用户试玩。

**命令**：`git diff --stat`；文档里提到的每个类名、常数、数字用 grep 在代码里核对一遍。

**提交**：`docs: stage 6C — duos in docs 01, 03 and 04 and the README`，末行 `Tests: unchanged (…)`。

**批次 E 收尾**：两路审查（规格审查对照设计 §2、§5、§6 的双人部分和下面的验收清单；文档审查对照代码）→ 跟进提交 → 进度 → 验收清单全部打勾后 squash 合入本地 main（见“执行方式”），合入后在 main 上再跑一遍四个测试项目和 0 警告检查。

---

## 验收清单（6C）

- [x] 四个测试项目全部通过，五个项目 0 警告；Update 的测试数不变，Sim、Net、Art 只增不减。（任务 9：五个项目 0 警告；Sim 1487（1 跳过，悬崖脚已知问题）、Net 498、Art 1575、Update 42；`build/shots/6c-t9/RESULTS.md` 第 1 行）
- [x] 模式：`BattleMode.Duos = 5`、“双人吃鸡”、`--mode duos`；最多 16 辆车；只在河谷（房间锁图、专用服务器 `--map sandbox` 报错退出）；10 分钟；不复活、不中途加入；毒圈和补给箱照吃鸡。（`DuoModeTests`、`DuoRoomTests`；专用服务器 `--mode duos --map sandbox` 退出码 2，`RESULTS.md` 第 7 行；`room-duos.png` 锁图、“双人吃鸡只在河谷开”）
- [x] 分队：房间里能选第 1～8 队、满 2 人的不能选；新进房间的真人默认进空队；机器人先补只有一个人的队、再两两成队；换到双人吃鸡时重新分队、换出去照各模式原来的规则；至少 2 队有车才能开局（客户端按钮和服务器同一结论）。（`DuoSeatingTests`、`DuoRoomTests`、`RoomRulesTests`（对照服务器）、`RoomDuoModelTests`；`room-duos.png`、`pair-host.png`/`pair-guest.png`）
- [x] 出生：每队一个按种子挑的出生点，队友在它左右相隔 10 m，都不压在物件上、不在水里（河谷 16 个点全测）。（`DuoSpawnTests` 河谷 16 点全测、`DuoRulesTests`；`spawn.png`）
- [x] 对局中：队友共享点亮和见过的补给箱；队友蓝灰（车身、名字条、小地图、击毁提示），其余红；一队两人都阵亡才出局；阵亡后先跟随队友观战、全队出局才看全场。（`DuoRulesTests.A_team_shares_its_sight`、`DuoServerTests`、`DuoHudTests`、`SpectateCandidatesTests`；`spawn.png`、`full-map.png`、`minimap-60.png`、`feed-23.png`、`follow-mate.png`）
- [x] 排名：按队，最后一人阵亡的时刻，同 tick 按剩余血量、再按击毁数；到点按活着的血量之和；结算标题按本队名次（“吃鸡！” / “第 N 名”）、副标题“共 N 名”是开局的队数；列表按队分组。（`DuoRankTests`、`DuoHudTests`；`gallery-results-duos-win.png`、`duos-ended.png`）
- [x] 机器人：离队友超过 60 m 往队友那边靠（队友是真人时也一样）；跑圈时两人去同一点附近；交战时优先打队友正在打的目标；确定、每 tick 不分配。（`BotDuoTests`、`DuoMatchTests`；偏差 19；`RESULTS.md` 队友距离表）
- [x] 联网 v7：模式 5 往返；房间名单的队号 0～7；`Entrants` 发队数；不泄露：死者在队友活着时只看队里看得见的；回环测试。（`DuoWireTests`、`DuoServerTests`）
- [x] 无画面整局：1 名不动真人 + 15 个机器人的双人吃鸡多个种子都在限时内分出胜负，没有机器人长时间卡住，机器人吃到箱子，队友平均相距在下限内，每 tick 0 分配，服务器 tick p99 在预算内。（`DuoMatchTests` Release 6/6；`BotLoadTests` 双人吃鸡 p99 0.846 ms；`RESULTS.md` 第 2 行）
- [x] 真对局：1 名玩家 + 15 个机器人打完一局双人吃鸡（日志、结算页）。（种子 5 开车 / 不开车各一局，`fullmatch.log`、`duos-ended.png`、`duos-idle-ended.png`；`RESULTS.md` 第 4 行）
- [x] 截图：房间分队（含两人同队）、名字条颜色、小地图上的队友、比分板分组、结算页（第 1 名和第 N 名）、观战跟队友，都看过。（`build/shots/6c-t9/` 都看过；“玩家打中敌人”那张没拍到，敌人红名字条看 `minimap-60.png`）
- [x] 回归：原有五种模式的行为和截图不变；试驾场六张同量级；soak 原有五行通过；画廊原有的屏逐字节相同；吃鸡整局回放逐位相同。（`RESULTS.md` 第 6 行：五种模式各一局、试驾场六张、画廊九屏逐字节、吃鸡 14 行和回放逐位相同；soak 六行）
- [x] 文档 `docs/01`、`docs/03`、`docs/04`、README 已按代码更新；`docs/PROGRESS.md` 的“6C 节点记录”完整。（任务 10 `6512154`、批次 E 跟进）
- [ ] 每批两路审查和跟进提交都做了；squash 合入本地 main，合入后 main 上全部测试通过、0 警告；没有推远端。

---

## 与设计的偏差

写计划时按现在的代码定下来的做法，和阶段 6 设计 §2、§5 字面不一样或设计没说的地方：

1. **`HasTeams` 的含义不变，另加 `HasAllies`、`IsDuos`、`TeamsFor`**：`HasTeams` 在三十多处表示“两队”（队伍出生点、队伍击毁数、0/1 队号检查、“换到对面”、比分板的我方 / 敌方横幅），双人吃鸡不是两队；只有“有没有队友”的地方（颜色、观战、视野给死者）改用 `HasAllies`。
2. **队友左右各偏 5 m 出生**（相隔 10 m），朝向同出生点；左右有一边不空就改成前后。河谷的 16 个各自为战出生点 10 m 内没有岩石、房子、墙、篱笆，坡度 < 8°，所以左右 5 m 两个位置都在原来查过的范围里；树和水另查（`DuoSpawnTests`）。8 个队用的出生点按对局种子从 16 个里挑：第 k 队用洗过的第 k 个，和吃鸡同一个洗法（`SpawnSalt`）。
3. **按队排名的细则**：到点时活着的队按活着的人的血之和；出局的队按最后一人的出局 tick，同 tick 按那一 tick 出局的人出局前的血之和，再按两人击毁之和，最后按队号。设计只写了“剩余血量（都为 0 时按击毁数）”。`WinnerSlot` 在双人吃鸡里是 `NoSlot`，赢家看 `WinnerTeam`。
4. **“共 N 名”是开局的队数**：`MatchRules.Places`，线上照旧放在 `MatchBlock.Entrants`（字段名不改，意思写进 `docs/03`）。
5. **死者先跟队友的视野**：双人吃鸡里阵亡的人在队友活着时只能看到队里看得见的车（和团队模式一样），全队出局后才看全场（和吃鸡一样）；设计只写了“先跟随队友观战”。不这样的话，死者能把看全场的信息告诉活着的队友。F 键自由视角也是全队出局后才能用。
6. **队友共享见过的补给箱**：设计 §5.2 只写了“共享点亮”。箱子的视野本来就是“看得见”的一种，6B 计划也留好了接口（服务器把同队两辆车的掩码按位或）；不共享的话，队友的小地图上一个箱子、自己的没有，读起来像漏发。
7. **机器人分领队和跟随者**：设计写“离队友超过 60 m 就往队友那边靠”。两个机器人都按这条走会互相追；所以队友是真人、或 id 更小的机器人时它跟随（不自己闲逛，到了 25 m 内就等着），另一个领队照吃鸡闲逛，只有在跟随者没在靠拢自己时、相距超过 60 m 才折回（细则见偏差 19：跟随者来不了才折回，跟随者正在靠拢时领队原地等）。
8. **“队友正在打的目标”对真人队友是“3 秒内打中过的那辆”**：服务器不知道真人瞄着谁，只知道打中了谁。对机器人队友是它的当前目标。
9. **双人吃鸡里机器人不撤退**：团队模式里血少的机器人退向队友（`RetreatTarget` 只看 `HasTeams`），双人吃鸡不开：设计没要求，和毒圈、靠拢搅在一起容易卡；靠拢本身已经让它待在队友旁边。
10. **1 个真人 + 1 个机器人开不了双人吃鸡**：机器人先补只有一个人的队（设计 §5.1），于是只有一队；设计 §2.1 又要至少 2 队有车。房间按钮说明原因（“至少要有两队有车…”）。
11. **协议升到 v7 但线上布局不变**：只有模式值 5 是新的；不升版本的话 v6 客户端进双人吃鸡房间会在读模式时报“坏包”，升了能在握手时清楚地说“版本不一致”。
12. **比分牌两块是“本队击毁”和“存活队伍 N/M”**（M 是开局的队数）；设计没写比分牌。
13. **比分板按队分组用组间细线，不用团队模式的横幅**：16 行 + 8 条 38 px 的横幅放不进 576 px 的比分板（现在 550 px）。名字颜色自己橙、队友蓝灰、其余白（照吃鸡；不用红，16 行里一片红读不清），车身、名字条、小地图、击毁提示里其余的车照设计是红。
14. **从双人吃鸡换到任何模式都重新按“人少的队”分 0/1**，包括各自为战和吃鸡（那里规则不看队号）：保持“非双人吃鸡的座位队号 ≤ 1”，之后换到团队模式不会带着 5 号队进去。
15. **加了 `--team N` 启动参数**：验收要拍“两人同队”的房间，没有它就得在界面上点。
16. **房间里双人吃鸡的名单是两列、每列四队**（第 1～4 队、第 5～8 队），只列有人或会补机器人的队，每行左边写队名、组间留空；选队用下拉框（8 项，满的禁用）。设计只写了“房间里每个人能选队”。
17. **队友出生位置的坡度只查每人 6 m 内，灌木不算挡路**（任务 1）：计划原写“10 m 内坡度 < 8°”。河谷有三个出生点在一侧队员位 5～10 m 外有坡（10 m 内最陡 22.7°、8.4°、10.2°），车身下面是平的（6 m 内都 < 8°：车身对角半长 4.2 m 之外还留一米多），每个车身也都在出生点自己查过的 10 m 圆里；改成前后站也不行（22.7° 的那个点 (116, 96) 往后 5 m 处 10 m 内有 46.8°）。“空”只算实心物件（`PropSet.Blockers` / `IsSolid`，含树）：车能开过灌木，和 `RiverlandSpawnTests.IsBlocking` 一样不算灌木。
18. **`MatchRules` 不接受少于 8 个各自为战出生点的双人吃鸡布局；开局那一 tick 离开又入场的座位交还它的队员位**（批次 A 跟进）：少于 8 个点时两队会共用一个点、两队的 0 号车落在同一处，所以构造时直接抛 `InvalidOperationException`（河谷 16 个、测试布局 10 个）。队员位记成每队 2 位的掩码（0 左、1 右），新入场的取最小的空位，于是回到原队的人回到自己腾出的位置、不会落到留下的队友身上；换到别的队时原队的位交还，原队没人了就不算进 `Places`（否则会多出一个没人占的名次）。对局中途离开的座位不交还（双人吃鸡不让中途入场），那一队照旧算进 `Places`。
19. **机器人跟队友的实施细节**（任务 5）：(a) 领队只在跟随者自己来不了（跑圈、捡箱、暂停，或在放弃靠拢后的冷却里：(g)）时才折回：两个机器人按坦克 id 顺序下命令，领队先看，照“跟随者没在靠拢自己”这一条领队会抢先折回（和偏差 7 的意思相反）；(b) 靠拢被放弃（规则见 (g)）后 `RegroupQuietTicks`（5 s，批次 C 跟进前借用 `ReplanTicks`）内不再开始；路上到了旧目标点而队友已走开时目标立刻改成队友现在的位置；(c) 靠拢次数写进对局结束那行的 `Summary()`（`… zone runs; N regroups; …`），不写进开局的 `Describe()`；(d) 实测（Release，`DuoMatchTests`）：任务 5 时带弹种子 19、3、7、11 队友距离中位数 28、48、49、51 m，超过 100 m 的 tick 占 11～32 %（只算两个机器人的队 12.1、24.6、4.9、18.5 %，不带弹种子 8 13.6 %）——当时归因于“各按自己的速度跑圈、领队闲逛走开”，批次 C 审查查明主要原因是**慢的跟随者追一个一直在闲逛的领队、怎么也追不上**（例如 11.1 m/s 的歼击车追 16.7 m/s 的轻坦，一次靠拢追几分钟；平地探针：轻坦领队 + 重坦跟随者 80 s 里 38 % 的 tick 超过 100 m、最远 167 m），由 (f) 领队等跟随者修好。批次 C 跟进后：种子 3、19、11、7 中位数 31、33、43、55 m（55 m 是种子 7 不动的真人被圈甩下），机器人队超过 100 m 4.2、9.0、11.4、4.7 %（不带弹种子 8 0.5 %），剩下的没有逐一追查（规则上两人跑圈时各按自己的速度、不靠拢，领队捡箱时也会分开）；`DuoMatchTests` 加了机器人队超过 100 m ≤ 15 % 的断言（最差的 1.5 倍是 17 %），中位数上限 60 m 不变；最长卡住带弹 2.1～5.3 s、不带弹 4.1 s。不带弹的一局仍用种子 8：种子 3 在跟进后有一个领队折回去找站在 (432, 221) 一带悬崖脚的跟随者，三次找不到路（相隔至少 5 s）才放弃，原地 11 s（悬崖脚陷阱见 `BotDuoTests` 里跳过的测试；种子 7、11、19 最长卡住 2.8、5.9、4.2 s，第 1～4 圈收圈时圈外都是 0）。(e) 跟随者在等队友时（`_heldForMate`）队友阵亡或离开就照吃鸡闲逛：原来只有 `Leg.Mate` 那一支会放开，等着的跟随者要等到下一圈（60～180 s）。(f) 领队闲逛时跟随者正在靠拢它、相距超过 `MateRange`，领队就地等（`_waitingForMate`），跟随者不再靠拢就接着闲逛；跑圈优先，等的时候照常交战。(g) 领队折回看跟随者的 `CanRegroup(tick)`（含放弃后 `RegroupQuietTicks` = 5 s 的冷却）：原来只看 `FreeToRegroup`，卡住的跟随者一次次放弃、领队一次也不回头；靠拢放弃的规则是：路上卡住三次，或三次找不到路（每次相隔至少 `ReplanTicks`，共 ≥ 10 s），或路线出圈。(h) 靠拢的路线出圈（`PathLeavesRing`，原来的 `WanderLeavesRing` 加上 `Leg.Mate`）就放弃这次靠拢、进冷却：原来会开出圈、跑圈回来、再走同一条路，直到换圈。(i) 跟随者只捡离活着的队友 `MateRange`（60 m）以内的箱子（`CrateFor` 加一个参数），领队和没队友的照旧；靠拢在离队友 `MateArrive` + 当前刹车距离时结束，停在 25 m 左右（任务里没有这条：领队等跟随者之后，全速的轻坦到 25 m 才松油门、滑到离停着的领队 7 m，在悬崖边堵死了领队唯一的出路，不带弹种子 8 有一个领队原地 66.5 s）；队友离 `Goal` 不超过 `WaypointRadius` 时不重新要路；真人打本队（误伤、HE 溅射）不记成“最后打中”；机器人队友的目标 `TargetLost` 时 `MateTarget` 为 0。吃鸡不受影响：吃鸡整局种子 3、7 带弹和 3 不带弹改前后事件数和事件哈希都一样。
20. **客户端的几处细节**（批次 D 及其跟进）：(a) 双人吃鸡房间地图下的提示写“双人吃鸡只在河谷开”（`RoomModel.DuoMapLockedHint`，任务 8），不用吃鸡共用的“吃鸡只在河谷开”；(b) 画廊的 `duos` 屏击毁提示里是“队友击毁敌人”和“敌人击毁敌人”，没有“敌人击毁队友”：队友死了在小地图上是灰色残骸，和“小地图上队友蓝灰”这一屏要看的东西冲突；“红色击毁者 / 蓝灰受害者”那一行在验收时（任务 9）的真对局里看；(c) 对局中比分板上队友的伤害照实显示（批次 D 跟进）：服务器对本队发真数（`docs/03` 的“伤害同占领的规矩”），只有敌人的在结束前留空，和占领一样；吃鸡里其余的人都是敌人，不变；(d) 队友对局中途离开后不再算进“本队击毁”和进行中的比分板排序：服务器把离开的座位发成空行，客户端看不到它的击毁数；服务器结束时的排名照旧算两人（预期如此）；(e) 房间名单里队号不在 0～7 的座位（线上队号 4 位，最大 15；服务器换到双人吃鸡时会给每个座位重新分队，所以现在不会出现）列在第二列最后，组名“未分队”，不算进标题的队数；第二列（第 5～8 队）没有行时不画（`RoomColumnView.Shown`）。
