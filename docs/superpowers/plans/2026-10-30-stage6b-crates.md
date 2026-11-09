# 阶段 6B：补给箱 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 吃鸡里加补给箱：开局按对局种子在河谷上摆 48 个箱子（约六成在房子旁，其余在路边和树林边），五种（维修 25%、弹药 20%、加固装甲 20%、快速装填 20%、光学设备 15%）；活车车身碰到就吃、立刻生效、箱子消失，同一 tick 编号小的吃到；效果只对吃到的车、这一局有效：回 30% 血、两种弹各补一半、炮弹伤害 −10%/层、装填 −8%/层、视野 +10%/层，三种叠层最多 3 层，满了照吃不起作用。**不发箱子表**：服务器每次算点亮（5 Hz）给每个玩家查一遍哪些箱子在他的视野里（距离 ≤ 带光学的视野、炮塔顶到牌子顶的视线不被地形、岩石、房子、墙挡），快照只带当前看得见的箱子（编号、位置、种类、还在不在），客户端记住见过的直到再看见它已空；自己吃到只发给自己（箱子、种类、效果），别人吃掉的不广播；三种叠层只在自己的快照里。协议升到 v6。客户端：见过的箱子画成带彩色牌子的小木箱（牌子上下浮动），被吃掉时一团闪光；吃到时屏幕中间偏下飘一行字（“+ 快速装填 2/3”“+ 维修 270”“光学设备已满”）；状态面板上方一排叠层图标；小地图和全屏图上见过、还在的箱子是五色小方块。机器人不交战时顺路去 150 m 内看得见的箱子（血少先维修、弹少先弹药、再没满层的），来不及进圈就不去。验收：1 名玩家 + 15 个机器人的整局里真人和机器人都吃到箱子，效果和叠层上限对。

**Architecture:** 规则全在 `Tank.Sim.Modes`：新的 `Crates.cs` 放 `CrateKind`、`Crate`、`CrateRules`（尺寸、效果的数和算式）、`CrateSet`（一局的箱子表：谁吃了、站着的掩码、每 tick 的拾取判定、“这辆车看得见哪些箱子”的 64 位掩码）；`CratePlacer`（开局按种子和导航格摆箱，只在服务器和仿真测试里跑）；`Vision/CrateSight`（一辆车看不看得见一个点，机器人和服务器共用）。效果落在仿真里：`TankState` 加三个叠层字节，`Simulation.TakeCrate` 改血、弹、叠层并发 `CratePicked` 事件；装甲在 `StrikeTank`/`Explode` 算完击穿之后乘，装填在 `TankStep` 的两处 `ReloadTime` 乘（客户端预测也走它，所以叠层要进自己的快照），视野在 `Spotting` 读 `ViewRange` 的两处乘。`MatchRules` 构造末尾加 `crates`，`Update` 第一步 `Collect`。`Tank.Net` 升到 v6：快照的自己块后面多一个“看得见的箱子”块（和基线相同就一位），`OwnTank` 多三个 2 位叠层，新消息 `Crate = 27`（只发给吃到的人），开局 / 重同步消息尾部带这个座位已知、还站着的箱子；服务器每个座位一个 `ulong` 的“见过”掩码和“当前看得见”掩码，机器人座位不算；客户端 `CrateKnowledge`（引擎无关）记住见过的。客户端画面：`Art/Mesh/CrateMesh`（引擎无关网格）+ `World/CrateViews`，HUD 的 `Match/Logic/CrateHud.cs`（叠层、飘字、文字）+ 两个 `HudPanel` 视图，小地图 `MinimapFrame.Crates`。机器人在 `Bot` 里加一条 `Leg.Crate`。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9.3、LiteNetLib 2.1.4、Godot 4.7.2 .NET 版。只做 macOS。

**设计依据：** [阶段 6 设计](../specs/2026-10-28-stage6-royale-design.md) 的 §1（6B 一行和用户的选择：开过去就吃、立刻生效）、§4（补给箱全部，含 2026-10-29 改动记录：不发箱子表、不广播别人吃箱子、快照只带看得见的、小地图只画见过的）、§6 里箱子的测试和验收、§7 的“不做”（空投、背包和按键、捡残骸里的东西、观战时看别人的叠层）。6C（双人吃鸡）是后面的计划，本计划不做，但留好接口：`CrateSet.VisibleTo` 按一辆车算，6C 的“队友共享看见的箱子”是服务器把同队两辆车的掩码按位或；`CrateKnowledge` 不假定一人一队。参考：[6A 计划](2026-10-28-stage6a-royale.md)（加协议字段、按座位发、客户端存、画廊、截图的做法；本计划的任务格式和它一样），[5C 计划](2026-10-27-stage5c-bots.md)（机器人、整局测试）。

**计划的深度：** 和 5A～6A 一样，不是逐字代码。每个任务写清：改哪些文件、对外接口（确切签名）、规则和算法要点、要先写的失败测试（具体输入和期望值）、命令和验收、提交说明。实现代码由执行者按 TDD 写（先写测试、确认失败、实现、确认通过）。接口一律以本文“共享接口”一节为准；实现中发现接口要改，先改本文这一节再动代码，并在跟进提交里说明。

---

## 执行方式

- **分支和目录**：`feat/stage6b-crates`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage6b`（从 main `04b58f6` 拉出，6A 已合入；第一个提交是本计划）。下文的相对路径都相对于它。
- **逐任务派子代理**（superpowers:subagent-driven-development）：一个任务一个提交。派之前用 `python3 /Users/murphy/Desktop/dev/BattleCity-dev-scripts/brief6b.py N` 生成 `plan6b/brief-N.md`（本文的“执行方式”“执行前须知”“共享接口”和任务 N 全文），交给实现子代理读。提交前五个项目 0 警告、四个测试项目全部通过。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照阶段 6 设计 §4、§6 的箱子部分和本计划，列“需求 → 代码 → 测试”表），一个做代码质量审查（正确性、确定性、每 tick / 每帧不分配、线上格式的边界检查、原有五种模式里没有箱子时行为不变、命名、注释）；按两份意见做**一个**跟进提交 `fix(<scope>): batch X review follow-up — …`。
- **每批之后记进度**：在 `docs/PROGRESS.md` 新建并维护“6B 节点记录”表（放在“6A 节点记录”上面；列：# / 节点 / 状态 / 提交 / 说明；说明里写各任务提交号、跟进提交号、测试数变化、截图目录和看到了什么、实测数字（服务器 tick p50/p99、快照字节、每客户端下行 KB/s、箱子视野一遍的毫秒数、帧率、整局里真人和机器人各吃到几个箱子）、遗留问题），同时更新“当前位置”的“阶段”“进度”“下一步”和“阶段总览”的阶段 6 一行。随跟进提交一起提交，或单独提交 `docs: progress — 6B batch X`。第 0 行“设计 + 计划”写设计 `905a92e`（§4 的改动 `6d89e19`）和本计划的提交号。
- **顺序**：A → B → C → D → E，批内按任务号。B 要用 A 的全部类型；C（机器人）要用 A 的箱子表和视线、B 的服务器（开销实测走服务器）；D（客户端）要用 B 的线上字段，放在 C 之后是为了真对局截图里的机器人已经会捡箱子。不并行。
- **6B 做完不等试玩**（设计 §1：用户试玩在 6C 之后）：最终验收清单全部打勾后 squash 合入本地 main（`git checkout main && git merge --squash feat/stage6b-crates && git commit`，说明 `feat: stage 6B — supply crates: seeded placement, five effects, sent by sight, protocol v6`，末行 `Tests: …`），不推远端；6C 从新的 main 拉分支。合入后在 main 上再跑一遍四个测试项目和 0 警告检查。
- **只做 macOS**：不写 Windows、Linux 专门的代码路径，不打其他平台的包。
- 设计已确认，中途不问用户，做完汇报。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage6b
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
dotnet run --project tests/Tank.Soak -c Release 2>&1 | tail -20                                # soak（吃鸡一行照旧）
```

- 成功的行形如 `已通过! - 失败: 0，通过: 1100 …`；`grep -E '通过!|失败!'` 两种都能抓到。
- **改动前的测试数**（本计划写作时在 `04b58f6` 上实测）：Sim 1273、Net 369、Art 1386、Update 42；五个项目 0 警告。每个任务的提交说明末行写测试数变化，例如 `Tests: Sim 1273 → 1300.`（列出有变化的项目）；下文各任务给的数是“至少”，以实际为准。Update 本阶段不该变。
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
W=/Users/murphy/Desktop/dev/BattleCity-stage6b
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后先编译：Godot 命令行不编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs/.gdshader 后生成 .uid，和源文件一起提交
mkdir -p build/shots/6b-tNN                                 # build/ 在 .gitignore 里
# HUD 画廊（纯色底上的预制帧）：SCENE 传空字符串；带河谷小地图的屏 --screenshot-at 给 3 秒以上
$S/godot_run.sh $W "" 90 --hud-demo royale-crates --screenshot $W/build/shots/6b-tNN/royale-crates.png --screenshot-at 3 --quit-after 4
# 主场景开房、自动开局（真对局，房主线程上的服务器）；吃鸡 1 名玩家 + 15 个机器人
G="--host --mode royale --map riverland --bots 15 --autostart --seed 11"
$S/godot_run.sh $W "" 240 $=G --autodrive --fps-log --screenshot $W/build/shots/6b-tNN/match-120.png --screenshot-at 120 --quit-after 130
ls -la build/shots/6b-tNN/                                  # 成败看文件在不在（脚本只打印含 screenshot/error 的行）
# 试驾场六张回归截图，OUTDIR 相对于工作目录；和 5D 之后的基线整张比（试驾场本阶段不变）
$S/baseline4b.sh $W build/shots/6b-tNN-sandbox
python3 $S/pngdiffdirs.py build/shots/base5d build/shots/6b-tNN-sandbox
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束都要确认没有残留的 Godot 进程
```

- 截图路径必须是绝对路径；`--screenshot` 一次运行只截一张；每张都要人工看（用 Read 打开 PNG），在进度说明里写看到了什么。`--screenshot-at` 在对局里按对局场景的时钟算（对局场景创建起），毒圈按服务器的对局时钟（开局起）：第 1 圈 90 s 开始缩、180 s 缩完，开局前加载约 5～15 s，截“正在缩”的画面给 120 s 左右。
- 河谷第一次生成在 Debug 客户端里要几秒；`--autostart` 等地图就绪再开局，超时给 240 秒以上（吃鸡对局要等毒圈）。
- 凡是动了客户端的任务都重拍六张试驾场回归截图并和 `build/shots/base5d/` 比：差异只允许出现在云、地平线、草叶摇动、粒子上（以往 orbit 约 23%、heavy 约 8% 来自环绕镜头的时间差，看图判断）。
- 帧率：`--fps-log` 每秒打印平均和最差帧时间，`$S/fpsstats.sh LOG` 汇总；任务 9、10 记吃鸡对局（16 辆车 + 光墙 + 48 个箱子）的帧率，和 6A 验收时的 115～116 帧比。

**提交**：

- 说明用英文一行，带范围前缀：`feat(sim): …`、`feat(net): …`、`feat(server): …`、`feat(bots): …`、`feat(client): …`、`test(sim): …`、`fix(<scope>): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。最后一行 `Tests: Sim 1273 → 1300.`（列出有变化的项目）。
- 作者用仓库配置（`火狐`），不加 `Co-Authored-By`，不推送远端。
- 只提交本任务的文件和它们的 `.uid`。别 `git add -A`。

---


---

## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Modes/Crates.cs` | 新建 | `CrateKind`、`Crate`、`CrateRules`（尺寸、效果的数和算式、`IsUseful`）、`CrateSet`（一局的箱子表、拾取、看得见的掩码）、`CrateTake` | 1、3 |
| `Modes/CratePlacer.cs` | 新建 | 开局按种子摆 48 个箱子（房子旁 / 路边 / 树林边、间距、不压物件、不在水里和陡坡上、在能开到的陆地上） | 2 |
| `Vision/CrateSight.cs` | 新建 | 一辆车看不看得见一个地面点上的牌子（距离 ≤ 带光学的视野、视线不被挡） | 3 |
| `Vehicles/TankState.cs` | 修改 | `ArmorLayers`、`ReloadLayers`、`OpticsLayers` | 1 |
| `Vehicles/TankStep.cs` | 修改 | 两处 `Spec.ReloadTime` 改成 `CrateRules.ReloadTime(spec, layers)` | 1 |
| `Vision/Spotting.cs` | 修改 | `Sees`、`Look` 读视野改成 `CrateRules.ViewRange(observer)` | 1 |
| `Simulation.cs`、`Simulation.Shells.cs` | 修改 | `TakeCrate`；`StrikeTank`/`Explode` 的炮弹伤害乘装甲系数 | 1 |
| `GameEvent.cs` | 修改 | `GameEventType.CratePicked = 8`（末尾追加） | 1 |
| `Modes/BattleMode.cs` | 修改 | `ModeRules.HasCrates` | 1 |
| `Modes/MatchRules.cs` | 修改 | 构造末尾加 `crates`；`Crates`；`Update` 第一步 `Collect` | 1 |
| `Ai/BotDirector.Crates.cs`（新）、`Ai/Bot.Crates.cs`（新）、`Ai/Bot.cs`、`Ai/BotDirector.cs` | 新建/修改 | 顺路捡箱：`Leg.Crate`、挑箱子、时间够不够；`Summary` 加捡箱次数 | 6 |

**联网 `src/Tank.Net`、服务器 `src/Tank.Server`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Battle/BattleProtocol.cs` | 修改 | `Version = 6`；`BattleMessage.Crate = 27`；注释 | 4 |
| `Battle/BattleSnapshot.cs` | 修改 | `CrateView`、`BattleSnapshot.Crates`/`CrateCount`（`CopyFrom`）、`OwnTank` 三个叠层 | 4 |
| `Battle/BattleSnapshotCodec.cs` | 修改 | 自己块后面的箱子块（和基线相同就一位）、`OwnTank` 叠层 2 位 × 3 | 4 |
| `Battle/BattleMessages.cs` | 修改 | `CrateMessage` 和读写；`MatchStartMessage.Crates` 尾部追加（无圈分支不再提前 return） | 4 |
| `Battle/CrateKnowledge.cs` | 新建（引擎无关） | 客户端“见过的箱子”表：从快照、开局消息、拾取消息更新 | 4 |
| `Battle/BattleClient.cs`、`Battle/BattleClient.Match.cs` | 修改 | 收 `Crate` 消息；`Crates`、`TryDequeuePickup`；`OwnState` 带叠层 | 4 |
| `Battle/Visibility.cs` | 修改 | `Wants`：`CratePicked` 谁都不发 | 4 |
| `Battle/BattleServer.cs`、`Battle/BattleServer.Match.cs` | 修改 | 开局摆箱（`CratePlacerOverride`）、每次点亮后的箱子视野掩码、快照的箱子块和叠层、吃到的人收 `Crate` 消息、重同步带已知的箱子、日志 | 5 |
| `src/Tank.Server/Program.cs` | 修改 | 日志一行箱子数（开局） | 5 |

**客户端 `src/Tank.Client`**（路径相对 `src/Tank.Client/`）

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `scripts/Art/Palette.cs` | 修改 | 五种箱子的颜色、`Crate(kind)` | 7 |
| `scripts/Art/Mesh/CrateMesh.cs` | 新建（引擎无关） | 木箱、牌子杆、带图标的牌子（五种图标） | 7 |
| `scripts/World/CrateViews.cs` | 新建 | 见过的箱子的节点：建、浮动、被吃掉时闪光并移除 | 7 |
| `scripts/Combat/BattleFx.cs` | 修改 | `Pickup(at, color)` 闪光 | 7 |
| `scripts/Match/Logic/CrateHud.cs` | 新建 | `BuffHud`、`PickupLine`、`PickupFeed`、`CrateText` | 8 |
| `scripts/Match/Logic/HudLayout.cs`、`MatchHudFrame.cs`、`HudDemoFrames.cs` | 修改 | `Pickup`、`Buffs` 两个框；帧带 `Buffs`；画廊 `royale-crates`，`HudDemo.Pickups` | 8 |
| `scripts/Minimap/Logic/MinimapFrame.cs`、`MinimapFeed.cs`、`MinimapStyle.cs` | 修改 | `MinimapCrate`、`MinimapFrame.Crates`、`MinimapFeed.AddCrates`、方块大小和颜色 | 8 |
| `scripts/Bootstrap/Logic/ShotPlan.cs`、`Bootstrap/LaunchOptions.cs` | 修改 | `--shot-after pickup:S` | 8 |
| `scripts/Match/PickupToastView.cs`、`Match/BuffRowView.cs` | 新建 | 飘字；叠层图标一排 | 9 |
| `scripts/Match/MatchHud.cs`、`HudGallery.cs`、`scripts/Minimap/MinimapView.cs`、`scripts/Overlay/CombatHud.cs` | 修改 | 挂两个新视图、`OnPickup`；画廊重放飘字；小地图画方块；装填环按带叠层的装填时间 | 9 |
| `scripts/Match/MatchMain.cs`、`MatchMain.Events.cs`、`MatchAutomation.cs` | 修改 | 建 `CrateViews`、每帧同步、拾取消息 → 飘字 + 闪光 + 截图时刻 | 9 |

**测试**

| 新文件 / 改动 | 任务 |
|---|---|
| Sim `CrateRulesTests`、`CratePickupTests`、`CrateEffectsTests`；`TankStepTests`/`SpottingTests` 加叠层用例 | 1 |
| Sim `CratePlacerTests`（河谷种子 1～50） | 2 |
| Sim `CrateSightTests`、`CrateSetVisibilityTests` | 3 |
| Net `CrateWireTests`、`CrateClientTests`、`CrateKnowledgeTests`；改 `RoyaleWireTests`（版本 6、最坏快照的字节数） | 4 |
| Net `CrateServerTests`（回环：只知道自己看见过的、别人刚吃掉的不知道、重同步、吃到的人收消息） | 5 |
| Sim `BotCrateTests`；`RoyaleMatch` 加箱子统计、`RoyaleMatchTests` 加用例；Net `BotLoadTests` 打印箱子视野一遍的用时 | 6 |
| Art `PaletteTests`、`CrateMeshTests` | 7 |
| Art `CrateHudTests`（文字、飘字队列、叠层）、`HudLayoutTests`、`MatchHudFrameTests`、`MinimapFeedTests`、`HudDemoFramesTests`、`ShotPlanTests`、`LaunchOptionsTests` 加用例 | 8 |

**开发工具**（仓库外，写计划时已加）：`brief6b.py`（同 `brief6a.py`，指向本计划，写到 `plan6b/brief-N.md`）。任务 2 在 `riverland-probe` 加 `crates SEED OUTDIR` 子命令（见任务 2）。

---

## 共享接口

所有任务都按这里的名字和签名写。改接口先改这一节。

### 必须保持源码兼容的现有签名

- `GameEventType` 只追加 `CratePicked = 8`；`GameEvent` 不加字段：`CratePicked` 事件用 `TankId` = 吃到的车、`PropId` = 箱子编号、`Damage` = 第一个数（见 `CrateTake.Amount`）、`Position` = 箱子位置（y 是地面高度）。线上事件读取的类型上限随之到 `CratePicked`，但服务器从不发它（`Viewer.Wants` 返回 false）。
- `TankState` 末尾追加三个 `byte`：`ArmorLayers`、`ReloadLayers`、`OpticsLayers`（`Fresh` 清零；复活、换车清零）。
- `MatchRules` 构造只在**末尾**追加 `IReadOnlyList<Crate>? crates = null`。
- `MatchStartMessage` 末尾追加 `IReadOnlyList<Crate>? Crates = null`；`OwnTank` 末尾追加三个叠层；`BattleSnapshot` 加 `CrateCount` 和 `Crates` 数组（`CopyFrom` 必须拷）；`MatchHudFrame` 末尾追加 `BuffHud Buffs = default`，`From` 末尾追加 `BuffHud buffs = default`；`HudBoxes` 末尾追加 `Pickup`、`Buffs`；`MinimapIconSizes` 末尾追加 `float Crate = 6f`；`HudDemo` 末尾追加 `PickupLine[]? Pickups = null`；`HudDemoFrames.Names` 末尾追加 `royale-crates`，已有的屏不变（截图不变）。
- `Viewer` 不改签名。`ShotMoment` 末尾追加 `Pickup`。
- 原有五种模式没有箱子时（`MatchRules.Crates == null`）一切照旧：拾取不跑、快照的箱子块计数 0、开局消息箱子数 0、`OwnTank` 叠层 0。试驾场没有箱子。

### 箱子（任务 1、3）

```csharp
namespace Tank.Sim.Modes;   // Modes/Crates.cs

public enum CrateKind : byte { Repair = 0, Ammo = 1, Armor = 2, Reload = 3, Optics = 4 }

/// <summary>A supply crate on the ground (stage 6 design §4): its id in the match's set (0-based), ground position (x, z), kind.</summary>
public readonly record struct Crate(byte Id, Vector2 Position, CrateKind Kind);

/// <summary>What one pickup did: the kind; Amount = health restored (repair), AP rounds added (ammo) or 1 (a layer added), 0 when it did nothing; Amount2 = HE rounds added (ammo only); Layers = the tank's layers of that kind afterwards (0 for repair, ammo).</summary>
public readonly record struct CrateTake(CrateKind Kind, int Amount, int Amount2, byte Layers)
{
    public bool Wasted => Amount == 0 && Amount2 == 0;   // full health / full racks / three layers already: the crate is gone all the same
}

public static class CrateRules
{
    public const int MaxCrates = 64;            // a ulong mask per viewer; the riverland places CratePlacer.Count = 48
    public const int Kinds = 5;
    public const float HalfSize = 0.6f;         // 1.2 m square; the hull's OBB touching this circle eats it
    public const float Height = 1.0f;           // the box
    public const float SignTop = 1.8f;          // the sign's top above the ground: what sight is tested against
    public const int MaxLayers = 3;
    public const float RepairShare = 0.30f, AmmoShare = 0.5f, ArmorPerLayer = 0.10f, ReloadPerLayer = 0.08f, OpticsPerLayer = 0.10f;
    // the two ceilings in integer thousandths: 1400 × 0.3f in float is a hair above 420 and would ceil to 421
    public static int RepairAmount(int maxHealth) => (maxHealth * 300 + 999) / 1000;                  // 600 → 180, 900 → 270, 1400 → 420, 950 → 285
    public static int AmmoAmount(int capacity) => (capacity * 500 + 999) / 1000;                      // 40 → 20, 15 → 8, 25 → 13, 30 → 15, 12 → 6
    public static float ArmorFactor(int layers) => 1f - ArmorPerLayer * layers;                      // 1, 0.9, 0.8, 0.7
    /// <summary>Shell damage after the armour layers: round half up, never below 1 for a hit that did damage (damage ≤ 0 stays as it is).</summary>
    public static int ScaleShellDamage(int damage, int layers);
    public static float ReloadTime(TankSpec spec, int layers) => spec.ReloadTime * (1f - ReloadPerLayer * layers);   // 4 s → 3.68, 3.36, 3.04
    public static float ViewRange(in TankState tank) => tank.Spec.ViewRange * (1f + OpticsPerLayer * tank.OpticsLayers);  // 270 → 297, 324, 351
    public static byte Layers(in TankState tank, CrateKind kind);          // Armor/Reload/Optics; 0 for Repair, Ammo
    /// <summary>Whether the crate would do anything for the tank right now (design §4.5: bots skip crates that do nothing).</summary>
    public static bool IsUseful(in TankState tank, CrateKind kind);        // repair: Health < MaxHealth; ammo: ApLeft < Ap.Capacity || HeLeft < He.Capacity; layers: < MaxLayers
    public static string Id(CrateKind kind);                               // "repair", "ammo", "armor", "reload", "optics" (logs, tests)
}

/// <summary>The crates of one match. Built once at the start (allocates), then allocation-free. Only the server and the simulation tests hold one; clients know crates only through <c>Tank.Net</c>'s <c>CrateKnowledge</c>.</summary>
public sealed class CrateSet
{
    /// <param name="crates">Ids must be 0..n−1 in order, n ≤ MaxCrates; ArgumentException otherwise.</param>
    public CrateSet(IReadOnlyList<Crate> crates);
    public int Count { get; }
    public Crate this[int id] { get; }
    public ReadOnlySpan<Crate> All { get; }
    public bool IsStanding(int id);
    public int TakenBy(int id);                   // the tank id that ate it, 0 while it stands
    public int StandingCount { get; }
    public ulong StandingMask { get; }            // bit id set while the crate stands
    public int Version { get; }                   // bumps on every Take (the bots and tests watch it)
    /// <summary>Marks the crate eaten by <paramref name="tankId"/>; false if it was gone already.</summary>
    public bool Take(int id, int tankId);
    /// <summary>One tick's pickups (design §4.2): tanks in slot order (ids only grow, so that is id order — the lower id wins a crate two hulls touch in the same tick), live ones only, whose hull OBB (<c>TankMotion.Footprint</c>) intersects the crate's circle (HalfSize); each eaten crate goes through <c>sim.TakeCrate</c>. Allocation-free.</summary>
    public void Collect(Simulation sim);
    /// <summary>Which crates <paramref name="observer"/> sees right now (bit per id, eaten ones included — seeing an empty spot is how a client learns a crate is gone): <c>CrateSight.Sees</c> for each crate within <c>CrateRules.ViewRange(observer)</c>. Allocation-free.</summary>
    public ulong VisibleTo(in TankState observer, Battlefield field);
    /// <summary>This tick's pickups (since the last Collect), in the order eaten: the server turns the human ones into Crate messages.</summary>
    public ReadOnlySpan<CratePickup> Pickups { get; }
}

public readonly record struct CratePickup(int TankId, byte CrateId, CrateTake Take);
```

```csharp
namespace Tank.Sim.Vision;   // Vision/CrateSight.cs

public static class CrateSight
{
    /// <summary>Whether the tank's crew sees a crate's sign at <paramref name="crate"/>: within CrateRules.ViewRange(observer), and the ray from the turret top (<c>Spotting.TurretTop</c>) to (x, ground + CrateRules.SignTop, z) is not stopped by <c>Battlefield.Raycast</c> (terrain, rocks, houses, brick and stone walls; fences, bushes and trees don't hide a crate). Wrecks see nothing. Allocation-free.</summary>
    public static bool Sees(in TankState observer, Vector2 crate, Battlefield field);
}
```

```csharp
// Simulation.cs（任务 1）
/// <summary>Applies <paramref name="crate"/> to the live tank <paramref name="tankId"/> (design §4.2) and emits a CratePicked event (TankId = tankId, PropId = crate.Id, Damage = Amount, Position = the crate on the ground). ArgumentException for an unknown tank; a wreck takes nothing (returns a take with Amount 0 and emits nothing).</summary>
public CrateTake TakeCrate(int tankId, in Crate crate);

// GameEvent.cs
public enum GameEventType : byte { …, PropDestroyed = 7, CratePicked = 8 }

// Modes/BattleMode.cs
public static bool HasCrates(BattleMode mode) => HasZone(mode);   // royale (and 6C's duos)

// Modes/MatchRules.cs
public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null, IReadOnlyList<TeamBase>? bases = null,
    uint seed = 0, IReadOnlyList<ZoneRing>? zone = null, IReadOnlyList<Crate>? crates = null);
public CrateSet? Crates { get; }     // null without crates or outside HasCrates modes
// Update() 的顺序：Collect crates（有的话）→ UpdateZone → HurtOutsideZone → 读事件 → 占领 → 复活 → Judge → NoteHealth
```

### 摆放（任务 2）

```csharp
namespace Tank.Sim.Modes;   // Modes/CratePlacer.cs

/// <summary>Places a match's crates from the match seed (design §4.1). Server and simulation tests only; deterministic for a seed.</summary>
public static class CratePlacer
{
    public const uint SeedSalt = 0x0C8A_7E55u;
    public const int Count = 48;
    public const float HouseShare = 0.6f;                  // 29 of 48 by a house, 19 by roads and forest edges
    public const float RoadShare = 0.5f;                   // of the non-house crates, rounded half up: 10 of 19 by roads, the other 9 by forests
    public const float HouseMin = 4f, HouseMax = 15f;      // metres from the house's footprint
    public const float RoadReach = 8f;                     // a Road surface sample within this many metres: "by the road"
    public const float ForestReach = 20f; public const int ForestTrees = 3;   // ≥ 3 trunks within 20 m and none within TrunkClear: "by the forest"
    public const float TrunkClear = 3f, PropClear = 2f;    // no tree trunk within 3 m; blockers' footprints ≥ 2 m away (rocks, walls, houses — HouseMin covers houses)
    public const float Spacing = 25f, SpawnClear = 20f, EdgeMargin = 10f;   // between crates; from any spawn point (or they'd be eaten at tick 0); inside the playable area
    public const float MaxSlopeDegrees = 8f;
    public const int Attempts = 40_000;
    /// <summary>The kinds for <paramref name="count"/> crates: 25 / 20 / 20 / 20 / 15 % each rounded to the nearest whole crate (half up), the surplus taken off the rarest kinds (optics first) and a shortfall made up with repair (48 → 12, 10, 10, 10, 6; 10 → 3, 2, 2, 2, 1), shuffled with <paramref name="rng"/>.</summary>
    public static CrateKind[] Kinds(int count, ref XorShift rng);
    /// <summary>Count crates (fewer only if the map runs out of places within Attempts draws: logged by the caller), ids 0..n−1 in the order placed: house crates, then road crates, then forest crates.</summary>
    public static Crate[] Place(Battlefield field, NavGrid grid, IReadOnlyList<SpawnPoint> spawns, uint seed);
    // 下面几个 public 是给测试和 riverland-probe 用的
    public static bool ByHouse(Battlefield field, Vector2 p);     // some standing house's footprint 4–15 m away (DistanceToPoint)
    public static bool ByRoad(Battlefield field, Vector2 p);      // SurfaceAt(p) == Road, or a Road sample within RoadReach (2 m grid)
    public static bool ByForest(Battlefield field, Vector2 p);    // ≥ ForestTrees trunks within ForestReach, nearest ≥ TrunkClear, not under any crown (Prop.FoliageRadius)
    /// <summary>The ground is fit for a crate: dry, slope ≤ 8° (NormalAt), inside [Border + EdgeMargin, Size − Border − EdgeMargin]², the nav cell open and in <paramref name="land"/> (ZonePicker.Land: reachable dry land), no blocker footprint within PropClear, no trunk within TrunkClear, no spawn within SpawnClear.</summary>
    public static bool Clear(Battlefield field, NavGrid grid, bool[] land, IReadOnlyList<SpawnPoint> spawns, Vector2 p);
}
```

### 线上格式 v6（任务 4）

```csharp
// BattleProtocol.cs
public const int Version = 6;                      // v6: crates by sight in the snapshot, own buff layers, the Crate message, known crates in MatchStart
public enum BattleMessage : byte { …, Zone = 26, Crate = 27 }

// BattleSnapshot.cs
/// <summary>A crate the viewer sees this tick: id, kind, whether it still stands, ground position in cm.</summary>
public struct CrateView { public byte Id; public CrateKind Kind; public bool Standing; public int X, Z; }   // Equals/GetHashCode
public struct OwnTank { …; public byte ArmorLayers, ReloadLayers, OpticsLayers; }
public sealed class BattleSnapshot { …; public int CrateCount; public readonly CrateView[] Crates = new CrateView[CrateRules.MaxCrates]; }
// 快照的写法（自己块之后、计分块之前）：有基线 → 1 位 “和基线一样”（CrateCount 和每行都相等）；否则 7 位 count（≤ 64，否则坏包），每行 6 位 id（严格递增，否则坏包）、1 位 standing、3 位 kind（≥ 5 坏包）、VarUInt x cm、VarUInt z cm（VarUInt 厘米，无符号：负坐标写时抛；地图从原点起）。
// OwnTank 末尾 2 位 × 3 叠层（0～3；写时 > 3 封到 3）。

// BattleMessages.cs
/// <summary>To the one player who ate a crate (design §4.4): the crate, where it was, what it did (CrateTake's numbers).</summary>
public readonly record struct CrateMessage(int MatchNumber, byte CrateId, CrateKind Kind, Vector2 Position, int Amount, int Amount2, byte Layers);   // a struct: one is made per pickup on the server tick, so no allocation
public static void Write(BitWriter w, in CrateMessage m);     // type byte, VarUInt match, byte id (< MaxCrates), 3-bit kind, floats x z (RingFits' coordinate bound), VarUInt amount, VarUInt amount2, 2-bit layers；`MatchNumber < 0` 抛 `ArgumentOutOfRangeException`，其余 `ArgumentException`；读不查 layers（2 位本身 0～3）
public static CrateMessage ReadCrate(ref BitReader r);     // ProtocolException on id ≥ MaxCrates, kind ≥ 5
public sealed record MatchStartMessage(…, ZoneSync? Zone = null, IReadOnlyList<Crate>? Crates = null);
// 尾部：圈之后 byte count（0～MaxCrates；无圈分支改成写 0 后继续）、每个 byte id、3-bit kind（补齐到 byte：用 WriteBits 3 位即可）、float x、float z；读的时候 id 递增、kind < 5、坐标有限且在 ±100 km。MaxMatchStartTailBytes 加 1 + 实际箱子数 × CrateBytes（不是 1 + 64 × 10）。
public const int CrateBytes = 10;

// Visibility.cs — Viewer.Wants: case CratePicked: return false（谁都不发；吃到的人走 Crate 消息）

// CrateKnowledge.cs（引擎无关，客户端的“见过的箱子”）
public sealed class CrateKnowledge
{
    public struct Entry { public bool Known, Standing, Visible; public CrateKind Kind; public Vector2 Position; }
    public ReadOnlySpan<Entry> All { get; }       // MaxCrates entries, indexed by id
    public int KnownCount { get; }                // Known entries
    public int StandingKnown { get; }             // Known && Standing: what the minimap and the world draw
    public int Version { get; }                   // bumps whenever any entry changes
    public void Clear();
    /// <summary>From a MatchStart: these are known and standing (a resync keeps what the server says the seat knows; a fresh start passes none).</summary>
    public void Reset(IReadOnlyList<Crate>? known);
    /// <summary>From the newest snapshot: every row becomes Known with its kind, position and Standing; rows not in it keep what they had but Visible = false. Allocation-free.</summary>
    public void Apply(BattleSnapshot snapshot);
    /// <summary>From a Crate message: the crate is gone (Known, Standing = false).</summary>
    public void Take(byte id);
    /// <summary>From a Crate message with what it was and where: known with that kind and position, and gone (a crate never seen keeps no kind or position otherwise).</summary>
    public void Take(byte id, CrateKind kind, Vector2 position);
}

// BattleClient.Match.cs
public CrateKnowledge Crates { get; }                 // cleared on LeaveMatch; Reset on MatchStart; Apply after the newest snapshot
public bool TryDequeuePickup(out CrateMessage pickup); // the player's own pickups, in order
// Crate 消息的忽略规则同 Zone：不在对局、对局号不对、模式没有箱子 → 忽略（不计坏包）；格式错 → ProtocolException（计坏包）。
```

### 服务器（任务 5）

```csharp
// BattleServer.Match.cs
internal Func<Battlefield, NavGrid, IReadOnlyList<SpawnPoint>, uint, Crate[]>? CratePlacerOverride;   // tests
// 开局（HasCrates）：crates = CratePlacer.Place(field, grid, spawns, seating.MatchSeed ^ CratePlacer.SeedSalt)（或 override），开局前先 `new CrateSet(crates)` 并检查每个箱子的坐标在快照的 cm 范围内（0 ≤ ToCm ≤ MaxCoordinateCm，否则按“the crates”失败），再传给 MatchRules；日志 "crates: 48 placed (29 by houses)"；挑圈、摆箱、建规则任何一步出错照 6A 撤掉机器人、房间不变。
// 每个座位（按 slot）：ulong _crateKnown[MaxPlayers]、_crateVisible[MaxPlayers]，开局清零，EndMatch 清零；不随 ResetForMatch 清（宽限内重连保留）。
// TickMatch：vision.Update() 之后，sim.Tick % VisionTracker.UpdateInterval == 0（和第一个 tick）时对每个在对局里、有活车的真人座位 _crateVisible[slot] = crates.VisibleTo(tank, field)，_crateKnown[slot] |= _crateVisible[slot]；死了或没车：_crateVisible = 0（已知的不变）。
// 快照 Build：Crates 行 = _crateVisible[slot] 的每一位（id 递增），Standing = crates.IsStanding(id)；OwnTank 叠层来自车的状态；不是 HasCrates 的模式 CrateCount = 0。
// rules.Update() 之后：sim.Events 里每个 CratePicked，SeatOfTank(e.TankId) 是真人且在对局里 → 可靠发 CrateMessage(_matchNumber, id, kind, position, amount, amount2, layers)（amount 等从 crates.Pickups 取）；机器人不发。
// SendMatchStart：Crates = _crateKnown[slot] 里还站着的（id 递增），位置按厘米取整后发（`KnownStanding`）；`TellPickups` 的 `CrateMessage` 位置同样取整：三条路给客户端的位置一致。箱子相关成员在 `BattleServer.Crates.cs`。
```

### 对局 HUD 和小地图（任务 8）

```csharp
namespace Tank.Client.Match;   // Match/Logic/CrateHud.cs

/// <summary>The player's own buff layers for the HUD (from BattleClient.Own; all 0 when there is no tank).</summary>
public readonly record struct BuffHud(byte Armor, byte Reload, byte Optics)
{
    public static BuffHud From(in TankState own);
    public bool Any => Armor + Reload + Optics > 0;
}

/// <summary>One pickup toast; ExpiresAt is on the scene clock.</summary>
public readonly record struct PickupLine(CrateKind Kind, int Amount, int Amount2, byte Layers, double ExpiresAt)
{
    public string Text => CrateText.Pickup(Kind, Amount, Amount2, Layers);   // allocates
}

/// <summary>The last few pickups, newest last; like KillFeed. Capacity 3, Lifetime 2.5 s.</summary>
public sealed class PickupFeed
{
    public const int Capacity = 3; public const double Lifetime = 2.5;
    public int Revision { get; }
    public void Add(CrateKind kind, int amount, int amount2, byte layers, double now);
    public void Clear();
    public ReadOnlySpan<PickupLine> Visible(double now);   // allocation-free
}

public static class CrateText
{
    public static string Name(CrateKind kind);            // 维修 / 弹药 / 加固装甲 / 快速装填 / 光学设备
    /// <summary>"+ 维修 270", "+ 弹药 穿甲 20 · 高爆 8", "+ 快速装填 2/3", "光学设备已满", "维修已满", "弹药已满" (Amount == 0 && Amount2 == 0).</summary>
    public static string Pickup(CrateKind kind, int amount, int amount2, byte layers);
    public static string Layers(byte layers);             // "×2"
}

// HudLayout.cs — 1600×900 下：Pickup (500, 480, 600, 114.4)（横幅框下 18 px：y = 0.28 H + 210 + 18；三行高：44 + 2 × 0.8 × 44，最新在最下，HudBoxes.PickupRow(i)），Buffs (24, 596, 360, 32)（状态面板框顶 662 上方：− StatusNameRoom 36 − 30）
// MatchHudFrame：BuffHud Buffs = default；From(..., BuffHud buffs = default)
// Minimap/Logic：public readonly record struct MinimapCrate(Vector2 Position, CrateKind Kind);
//   MinimapFrame：MaxCrates = CrateRules.MaxCrates、ReadOnlySpan<MinimapCrate> Crates、AddCrate(in MinimapCrate)、Clear 清
//   MinimapFeed.AddCrates(MinimapFrame frame, CrateKnowledge crates)：Known && Standing 的，按 id
//   MinimapIconSizes 末尾 float Crate = 6f（CornerIcons 6、FullIcons 9）；MinimapStyle.CrateFill(CrateKind) → Palette.Crate(kind)
// Palette：CrateRepair 4CCB5A、CrateAmmo F0B429、CrateArmor 9A8FD0、CrateReload C65BD1、CrateOptics 2EC4B6；public static Vector3 Crate(CrateKind kind)
// ShotMoment.Pickup；--shot-after pickup:S（自己第一次吃到箱子后 S 秒；只在吃鸡对局里）
```

### 机器人（任务 6）

```csharp
// Ai/BotDirector.Crates.cs
public const float CrateRange = 150f;            // design §4.5
public const float CrateHealthShare = 0.6f;      // below this, repair first
public const float CrateAmmoShare = 0.5f;        // below this (AP + HE over the capacities), ammo next
public const int CrateGiveUpTicks = 20 * SimConstants.TickRate;
public const int CrateShunTicks = 2 * CrateGiveUpTicks;   // 放弃（没吃到）的箱子记进每车的 `ulong` 掩码，`CrateShunTicks` 后或换圈时清
internal CrateSet? Crates => Rules.Crates;
/// <summary>Picks the crate a bot that isn't fighting should fetch, or −1: standing, not in the bot's shunned mask (bit = crate id), within CrateRange, IsUseful, CrateSight.Sees from the bot's tank, and the time allows (design §4.5: distance to the crate plus from the crate to the target ring's InsideLimit circle, both × ZoneDetour, must not MustRun); by priority (repair when health < 60 %, then ammo when below half, then any useful layer), then distance, then id. Allocation-free.</summary>
internal int CrateFor(in TankState me, ulong shunned);   // 射线最后、只对能胜出的候选，结果与（优先级, 距离, id）的定义相同
// Ai/Bot.Crates.cs
public int CrateRuns { get; }       // detours begun
public int CratesTaken { get; }     // crates this bot ran for and ate (TakenBy == TankId)
// Leg.Crate；WatchCrates(director, me, tick) 在 WatchZone/CheckArrival 之后、Picker.Pick 之前：每 VisionTracker.UpdateInterval 个 tick 查一次（按车号错开），只在 director.Crates != null、不在跑圈、不急、上一 tick 没目标也没贴得太近的敌人、_leg 是 Waypoint / Zone（巡游）/ Arrived 时开始；到了（箱子没了，或 StoppedAt(Goal)）或超过 CrateGiveUpTicks → NextWaypoint；圈变了、要跑圈 → 照旧由 WatchZone 覆盖。开始在 6 tick 的查看里；到了 / 没了 / 路走完 / 超时在 `CheckArrival` 每 tick 查。
// BotDirector.Summary() 前缀加 "{runs} crate runs, {taken} fetched; "（只在有箱子时）
```

---

## 任务总览

| 批 | 任务 | 内容 |
|---|---|---|
| A 仿真 | 1 | 箱子的数据、效果和拾取：`CrateKind`/`Crate`/`CrateRules`/`CrateSet`、叠层进 `TankState`、装甲 / 装填 / 视野的算式接进仿真、`TakeCrate`、`CratePicked`、`MatchRules.Collect` |
| | 2 | 摆放：`CratePlacer`，河谷种子 1～50 实测；`riverland-probe crates` 画图 |
| | 3 | 看见箱子：`CrateSight`、`CrateSet.VisibleTo` |
| B 联网 | 4 | 协议 v6：快照的箱子块和叠层、`CrateMessage`、开局消息的箱子、`CrateKnowledge`、客户端 |
| | 5 | 服务器：开局摆箱、每次点亮后的视野掩码、按座位发、吃到的人收消息、重同步；回环测试 |
| C 机器人 | 6 | 顺路捡箱；整局测试加箱子；开销实测 |
| D 客户端 | 7 | 画面：颜色、木箱和牌子的网格、`CrateViews`、拾取闪光 |
| | 8 | HUD 逻辑：飘字、叠层、布局、小地图的方块、画廊、`--shot-after pickup` |
| | 9 | HUD 视图和接线：两个新面板、小地图画方块、`MatchMain` 接线、装填环、画廊和真对局截图 |
| E 收尾 | 10 | 验收 |
| | 11 | 文档 |

---

## 批次 A：仿真

### Task 1：箱子的数据、效果和拾取

**Files:**
- Create: `src/Tank.Sim/Modes/Crates.cs`
- Modify: `src/Tank.Sim/Vehicles/TankState.cs`、`Vehicles/TankStep.cs`、`Vision/Spotting.cs`、`Simulation.cs`、`Simulation.Shells.cs`、`GameEvent.cs`、`Modes/BattleMode.cs`、`Modes/MatchRules.cs`
- Test: `tests/Tank.Sim.Tests/CrateRulesTests.cs`、`CrateEffectsTests.cs`、`CratePickupTests.cs`；`RulesMatch` 帮手加可选的 `crates`

**规则**

- `CrateRules`、`CrateSet`、`CrateTake`、`CratePickup` 按“共享接口”。`ScaleShellDamage(d, n)`：`d ≤ 0` 原样；否则 `max(1, RoundHalfUp(d × ArmorFactor(n)))`（`Penetration.RoundHalfUp` 是 internal，同一程序集能用）。
- `TankState` 末尾三个 `byte`；`Simulation.Fresh` 不用改（新结构默认 0），`Revive` 不清叠层（吃鸡不复活；团队战复活用 `Respawn` → `Fresh`，清零）。
- **装甲**：`StrikeTank` 里 `int dealt = IsProtected(target) ? 0 : CrateRules.ScaleShellDamage(impact.Damage, target.ArmorLayers)`；`Explode` 里 `damage = CrateRules.ScaleShellDamage(damage, target.ArmorLayers)`（保护判断照旧在后）。事件里的 `Damage` 就是乘过的数（`MatchRules.OnHurt` 记的伤害和真掉的血一致）。毒圈伤害不乘（`HurtByZone` 不经过这里）。
- **装填**：`TankStep` 的两处 `tank.Spec.ReloadTime` → `CrateRules.ReloadTime(tank.Spec, tank.ReloadLayers)`（换弹种和开炮后）。`Reload` 的倒计时不变。
- **视野**：`Spotting.Sees` 和 `Look` 里 `observer.Spec.ViewRange` → `CrateRules.ViewRange(observer)`；`DirectRange`、隐蔽、草丛的算式不变（发现距离 = 15 + (视野 − 15) × (1 − 隐蔽) 里的“视野”就是带光学的）。
- **`TakeCrate`**：`ref TankState t = ref TankRef(tankId)`；残骸 → 返回 `new CrateTake(kind, 0, 0, 0)`、不发事件。按种类：维修 `gain = min(RepairAmount(MaxHealth), MaxHealth − Health)`，`Health += gain`，`Amount = gain`；弹药 `ap = min(AmmoAmount(Ap.Capacity), Ap.Capacity − ApLeft)`、`he` 同理，`Amount = ap`、`Amount2 = he`；叠层 `layers < MaxLayers` → `layers++`、`Amount = 1`，否则 `Amount = 0`；`Layers = 之后的层数`。发 `GameEvent(CratePicked, Tick, tankId, 0, new Vector3(p.X, Field.Terrain.HeightAt(p.X, p.Y), p.Y), Vector3.Zero, Damage: take.Amount, PropId: crate.Id)`。在两步之间调用时事件进 `_pending`（同 `Respawn`），`Step` 里调用进 `_events`——`Collect` 在 `MatchRules.Update` 里（Step 之后、读事件之前）调用，所以要让 `TakeCrate` 直接加进 `_events`（这一 tick 的事件表还没被读）：照 `HurtByZone` 的做法，`Damage()` 直接 `_events.Add`，不走 `_pending`。
- **`CrateSet.Collect(sim)`**：清 `Pickups`；对 `sim.Tanks` 每个活车（`!IsDestroyed`），`hull = TankMotion.Footprint(Spec, Position, Yaw)`；对每个站着的箱子（先比圆心距离平方 ≤ (HalfSize + 半对角线)² 再做 `hull.IntersectsCircle(crate.Position, HalfSize)`），吃到：`Take(id, tankId)`、`take = sim.TakeCrate(tankId, crate)`、`Pickups` 加一项。一辆车一 tick 可以吃多个（两个箱子相距 ≥ 25 m，不会发生，不用特殊处理）。`Pickups` 用预分配的数组（容量 `MaxCrates`）。
- **`MatchRules`**：吃鸡且 `crates != null` → `Crates = new CrateSet(crates)`；`Update` 第一步 `Crates?.Collect(_sim)`（在 `UpdateZone` 之前，这样同一 tick 先吃维修再挨毒）。`NoteHealth` 照旧在最后（修过的血被记进 `_health`，下一 tick 的伤害封顶按新血算）。
- `ModeRules.HasCrates`。
- 箱子**不是物件**（设计 §4.1）：不进 `Battlefield.Props`、不进导航格，车不被它挡、炮弹穿过它、`Raycast` 不算它——`CrateSet` 只是规则层的一张表。
- 不分配：`Collect`、`VisibleTo`（任务 3）、`TakeCrate` 每 tick 不分配；`CrateSet` 构造时分配。

**先写的测试**

- `CrateRulesTests`：`RepairAmount` 四种车 180 / 270 / 420 / 285；`AmmoAmount(40) == 20`、`(15) == 8`、`(25) == 13`、`(12) == 6`；`ArmorFactor(0..3)`；`ScaleShellDamage(100, 1) == 90`、`(5, 3) == 4`（3.5 进 4）、`(1, 3) == 1`、`(0, 3) == 0`、`(−3, 2) == −3`；`ReloadTime(中坦, 2) ≈ 3.36`；`ViewRange(中坦 3 层) ≈ 351`；`IsUseful`：满血的车维修 false、掉 1 血 true，满弹弹药 false、少一发 true，3 层装甲 false；`Layers`；`Id` 五个；`CrateSet` 构造：id 不连续、超过 64 → `ArgumentException`；`Take` 两次第二次 false、`TakenBy`、`StandingMask`、`StandingCount`、`Version` 加一。
- `CrateEffectsTests`（`TestWorld.FlatField()`，两辆中坦）：
  - 装甲：同一发（`ShotRandom` 确定）打 0 层和 2 层的车，2 层掉的血 = `ScaleShellDamage(0 层掉的血, 2)`，`TankHit` 事件的 `Damage` 等于掉的血；HE 溅射同理；3 层 ×0.7；毒圈 `HurtByZone(…, 100)` 对 3 层的车照掉 100。
  - 装填：`ReloadLayers = 2` 的车开炮后 `Reload ≈ 3.36`，换弹种后也是；0 层 4.0。
  - 视野：`OpticsLayers = 1` 的中坦在 400 m 平地上 290 m 看得见一辆停着、`Camo = Concealment(0, 0)` 的轻坦（0 层看不见；真实轻坦 Still 0.40 时发现距离只有 168 / 184 m，另测隐蔽 0.4 在 175 m 处 0 层看不见、1 层看得见）；`Sees` 和 `Look` 都测。
  - `TakeCrate`：满血维修 `Amount 0`、血不变；掉 500 血的中坦维修回 270；掉 100 血回 100；弹药：打空的车各补 20 / 8，只差 3 发的补 3；装甲 0 → 1 → 2 → 3，第四次 `Amount 0`、`Layers 3`、`Wasted`；每次都发一个 `CratePicked`（`TankId`、`PropId`、`Damage` 对，位置的 y 是地面高度）；残骸 `Amount 0` 且没有事件；没有这辆车 → `ArgumentException`。
- `CratePickupTests`（`RulesMatch`，吃鸡，`crates:` 三个箱子 (100, 100) 维修、(130, 100) 弹药、(160, 100) 装甲）：
  - 车停在 (100, 100)：下一次 `Run(1)` 后箱子被吃（`IsStanding(0)` false、`TakenBy(0)` 是它、`Pickups` 一项、事件里一个 `CratePicked`），再 `Run(1)` 什么都不发生。
  - 车停在 (100.9, 100)（车体边缘碰到 0.6 的圆）吃到；(104, 100) 吃不到。
  - 两辆车同一 tick 压同一个箱子：id 小的吃到（`TakenBy` 是小的那辆），大的 `Pickups` 里没有。
  - 残骸压着箱子不吃；开局保护中的活车照吃。
  - 不是吃鸡（各自为战）传了 `crates` → `Crates == null`、箱子不被吃；吃鸡不传 → `Crates == null`。
  - 先吃维修再挨毒：车在圈外、只剩 10 血，站上维修箱的那一整秒，用第 2 圈缩圈时的 2 %（18 血；第 1 圈的 1 % = 9 杀不死 10 血的车），血 = 10 + 270 − 毒圈的那一下（不死）。
  - `Allocation.AssertNone`：预热后 300 tick（车来回开过箱子，至少吃掉一个）不分配。
- 现有测试：`TankStepTests`（或相近的）加“0 层时装填时间和以前完全一样”；`SpottingTests` 加“0 层时和以前一样”——用现有用例跑通即可。

**步骤**

- [ ] 工作目录准备（见“执行前须知”）
- [ ] 先写 `CrateRulesTests`，跑、确认编译失败 / 失败；写 `Crates.cs` 直到通过
- [ ] 写 `CrateEffectsTests`，确认失败；改 `TankState`、`TankStep`、`Spotting`、`Simulation`，通过
- [ ] 写 `CratePickupTests`，确认失败；改 `MatchRules`、`BattleMode`、`GameEvent`，通过
- [ ] 四个测试项目全过、五个项目 0 警告（客户端项目要能编译：`TankState` 多了字段对它没影响）
- [ ] 提交

**提交**：`feat(sim): supply crates — kinds, five effects, buff layers on the tank, pickup by hull contact, CratePicked`，末行 `Tests: Sim 1273 → N.`

### Task 2：摆放

**Files:**
- Create: `src/Tank.Sim/Modes/CratePlacer.cs`
- Test: `tests/Tank.Sim.Tests/CratePlacerTests.cs`
- 仓库外：`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe/Crates.cs`（新子命令 `crates SEED OUTDIR`）

**规则**

- 常数和签名按“共享接口”。`Place`：
  1. `land = ZonePicker.Land(field, grid, spawns)`（任务共用，开局一次）；`rng = new XorShift(seed)`；`kinds = Kinds(Count, ref rng)`；`houseQuota = round(Count × HouseShare)` = 29。
  2. 收集站着的房子（`field.Props` 里 `Kind == House && field.IsStanding(id)`）。没有房子（测试地图）→ `houseQuota = 0`。
  3. 房子旁：循环到够 29 个或用完 `Attempts` 的一半：抽一栋房子（`rng.NextUInt() % n`），抽角度 `θ = NextFloat × 2π` 和距离 `d = HouseMin + NextFloat × (HouseMax − HouseMin)`，`p = 从足迹中心沿 θ 走 (足迹在 θ 方向的半宽 + d)`（半宽用 `Obb2` 的 `HalfExtents` 投影近似：`|cos| × 半长 + |sin| × 半宽` 在足迹坐标系里算），然后要求 `ByHouse(p)`（确切的 4～15 m，`DistanceToPoint`）、`Clear(...)`、离已放的 ≥ `Spacing`。
  4. 其余 19 个分两段，都在 `[Border + EdgeMargin, Size − Border − EdgeMargin]²` 里均匀抽点、要求 `!ByHouse(p)`、`Clear`、间距：先路边 `roadQuota = round-half-up((Count − houseQuota) × RoadShare)` = 10 个，要求 `ByRoad(p)`，次数上限是房子阶段剩下次数的一半；再树林边到 48 个或用完 `Attempts`，要求 `ByForest(p)`。路边阶段提前用完次数时树林阶段补足（类别配额是目标，不是保证；河谷种子 1～50 实测全部满足）。
  5. 箱子 id 按放的顺序 0..n−1，种类 `kinds[i]`。
- `ByRoad`：`SurfaceAt(p) == Road`，或以 p 为中心、`RoadReach` 内按 2 m 步长扫格点有 `Road`。`ByForest`：`field.Near(PropSet.Foliage, p, ForestReach, buffer)`（internal，可用）里的树（`Kind == Tree`、站着的）数 ≥ 3、最近的树干中心距离 ≥ `TrunkClear`、没有一棵的树冠（`FoliageRadius`）盖住 p。`Clear`：水深 ≤ 0；`NormalAt(p).Y ≥ cos 8°`（再查 p 四周 ±1 m 四点）；在范围内；`grid.IsOpenAt(p)` 且 `land[cell]`；`Near(PropSet.Blockers, p, PropClear + 最大足迹半径…)` 简化成 `Near(PropSet.Blockers, p, 12, buffer)` 后逐个 `Footprint.DistanceToPoint(p) ≥ PropClear`（房子因 HouseMin 自然满足）；树干 `Near(Foliage, p, TrunkClear, buffer)` 里没有树；每个出生点距离 ≥ `SpawnClear`。
- `Kinds`：份额 12 / 10 / 10 / 10 / 6（最大余数法从 25/20/20/20/15 % 算，`count` 任意），Fisher–Yates 用 `rng`。
- 注释写清：只在服务器和仿真测试里跑；`Attempts` 用完放不够 48 个也返回（调用者记日志）；同一种子同一结果。
- **probe**：`riverland-probe` 加 `crates SEED OUTDIR`（`Crates.cs`，照 `Sites.cs`）：`CratePlacer.Place(field, grid, RiverlandMap.Spawns.Solo, seed ^ CratePlacer.SeedSalt)`，打印个数、三类各几个（`ByHouse` / `ByRoad` / `ByForest`）、种类计数、最近两个的距离、离水最近的距离、最陡的坡、用时；导出 `crates-SEED.png`（全图 1 m 一像素，底是 `props.png` 那套：房子红、树冠绿、岩石灰、水蓝，箱子按五种颜色画 5 像素的方块、周围 25 m 的淡圈）。写计划时没跑，任务里跑种子 3、7、11 看图。

**先写的测试**（`CratePlacerTests`，`RiverlandFixture.Field` / `Grid`，`RiverlandMap.Spawns.Solo`）

- 种子 1～50（共用 `Land`，一次算好传进去的话需要 `Place` 的重载 `Place(field, grid, spawns, seed, bool[] land)`——加这个 internal 重载，public 的那个算 `Land` 后调它）：每次恰好 48 个；id 0..47；前 29 个 `ByHouse`、后 19 个都 `!ByHouse`；29～38 `ByRoad`；39～47 `ByForest`；两两距离 ≥ 25；每个 `Clear` 为真（水深 ≤ 0、坡 ≤ 8°、格开着、`land` 真、离出生点 ≥ 20）；种类计数 12 / 10 / 10 / 10 / 6；打印 50 个种子里最少的尝试次数余量（用 `Attempts` 的 1/4 以内放完算够宽裕：记进度）。
- 同一种子两次逐个相等；种子 1 和 2 的第一个箱子不同；20 个种子的第 0 个箱子至少 15 种位置。
- `Kinds(48)` 计数对；`Kinds(10)` → 3 / 2 / 2 / 2 / 1；`Kinds(1)` 一个；两次不同 rng 顺序不同。
- 合成：`TestWorld.FlatField()`（没有房子、没有路、没有树）→ `Place` 返回 0 个（没有合格的类别）；在平地上手工摆 4 栋房子 + 一条 Road 带（`SurfaceMap` 可写的话；不行就只测房子）→ 全是房子旁的；一片 30 棵树 → `ByForest` 在林边真、林心（被树冠盖住或离树干 < 3 m）假、空地假。
- `Place` 不改场地和格子（`StateVersion`、`grid.Version` 不变）。
- 计时（`Timing` 集合）：河谷 `Place`（含 `Land`）打印毫秒数，Debug 下 < 3000 ms。

**步骤**

- [ ] 写测试、确认失败；实现；通过
- [ ] probe 跑种子 3、7、11，看三张图（Read），把看到的（箱子是不是在房子旁、路边、林边；有没有挤在一起）写进进度
- [ ] 提交

**提交**：`feat(sim): crate placement — 48 crates a match from the seed, by houses, roads and forest edges, on reachable dry land`，末行 `Tests: Sim N → M.`

### Task 3：看见箱子

**Files:**
- Create: `src/Tank.Sim/Vision/CrateSight.cs`
- Modify: `src/Tank.Sim/Modes/Crates.cs`（`VisibleTo`）
- Test: `tests/Tank.Sim.Tests/CrateSightTests.cs`、`CrateSetVisibilityTests.cs`

**规则**

- `CrateSight.Sees`：残骸 false；`d² > ViewRange(observer)²` false；`pose = TankPose.Compute(observer, field.Terrain)`，`eye = Spotting.TurretTop(observer, pose)`，`top = (x, HeightAt(x, z) + SignTop, z)`；`!field.Raycast(eye, top, out _)`。不看草丛（箱子不是车，没有隐蔽）。
- `VisibleTo`：`ulong mask = 0`；对每个箱子（吃掉的也算）`if (CrateSight.Sees(observer, crate.Position, field)) mask |= 1UL << id`。观察者是残骸 → 0。
- 两者每次调用不分配（`Raycast` 用 `stackalloc`）。

**先写的测试**

- `CrateSightTests`（`TestWorld.FlatField()`，带一面墙或一块岩石的场地——用 5B/6A 测试里现成的带墙地图 `TestMaps`/`TestWorld` 的墙版本）：空地 200 m 看得见、`ViewRange + 1` m 看不见、光学 1 层后 290 m 看得见（中坦）；墙后看不见、墙没了（`SetDurability` 0 / `IsStanding` false）看得见；山后（用高度场抬一座土丘的 `FlatField` 变体，或河谷上台地崖下 vs 崖上两个已知点）看不见；残骸看不见；栅栏、草丛、树不挡（在视线上放一棵树照样看见）。
- `CrateSetVisibilityTests`：3 个箱子，车在中间空地 → 掩码 0b111；吃掉一个还在掩码里（空位也“看见”）；一个藏在墙后 → 那一位 0；残骸 → 0；`Allocation.AssertNone`：预热后 1000 次 `VisibleTo`（48 个箱子的河谷 `RiverlandFixture`，一辆车停在小镇）不分配；计时：河谷 48 个箱子、16 辆车各算一遍的毫秒数打印（Release 上限 2 ms（实测 0.9～1.1 ms，出生点处 110 条视线）、Debug 上限 10 ms 只打印）。

**步骤**：写测试、确认失败、实现、通过、提交。

**提交**：`feat(sim): crate sight — a tank sees a crate's sign within its view range along a clear line, and the set gives a viewer its mask`，末行 `Tests: Sim N → M.`

**批次 A 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 B：联网

### Task 4：协议 v6

**Files:**
- Create: `src/Tank.Net/Battle/CrateKnowledge.cs`
- Modify: `src/Tank.Net/Battle/BattleProtocol.cs`、`BattleSnapshot.cs`、`BattleSnapshotCodec.cs`、`BattleMessages.cs`、`BattleClient.cs`、`BattleClient.Match.cs`、`Visibility.cs`
- Test: `tests/Tank.Net.Tests/Battle/CrateWireTests.cs`、`CrateClientTests.cs`、`CrateKnowledgeTests.cs`；改 `RoyaleWireTests`（版本 5 → 6、最坏快照字节数）、`StandardWireTests`（815 → 新数）

**规则**

- 类型和格式按“共享接口”。**快照**：`Write` 在自己块（`HasOwn`/`same`/`WriteOwn`）之后、计分块之前：`baseline != null` → `WriteBool(sameCrates)`（`CrateCount` 相等且每行 `Equals`）；不同或没基线 → 7 位 `CrateCount`、每行 6 位 id、1 位 standing、3 位 kind、`VarUInt` x cm、`VarUInt` z cm（`AddCoordinate` 的范围）。读：`count > 64` 坏包；id 不严格递增坏包；kind ≥ 5 坏包；`sameCrates` 时从基线拷行。`CheckStats` 前也检查 `CrateCount ≤ MaxCrates`（写时 `ArgumentOutOfRangeException`）。`OwnTank`：`Destroyed` 之后追加 3 × 2 位叠层。`BattleSnapshot.CopyFrom` 拷 `CrateCount` 和前 `CrateCount` 行。
- **`CrateMessage`**：写 / 读照 `ZoneMessage`；写时 `MatchNumber < 0`、`CrateId ≥ MaxCrates`、`Layers > 3`、负的 amount、坐标不在 ±100 km 抛 `ArgumentException`；读时 id / kind / layers 越界、坐标非有限 → `ProtocolException`。
- **开局消息**：`WriteMatchStartTail` 的无圈分支写 `0` 后不 return，接着写箱子：`byte count`（`Crates?.Count ?? 0`，> 64 写时抛），每个 `byte id`、3 位 kind、float x、float z；读：count > 64 坏包、id 严格递增、kind < 5、坐标有限且 |x|,|z| ≤ `MaxBaseCoordinateMetres`。`MaxMatchStartTailBytes` 加 `1 + MaxCrates × CrateBytes`。`CheckedBases`/`CheckZone` 旁加 `CheckCrates`（写前检查，不写半个包）。
- **`CrateKnowledge`**：64 项 `Entry` 数组；`Apply(snapshot)`：先把所有 `Visible = false`（循环 64），再对每行 `Known = true`、`Visible = true`、`Kind`、`Position = FromCm`、`Standing = row.Standing`；任何一项的 `Known/Standing/Kind/Position` 变了 `Version++`（`Visible` 变不算）。`Reset(known)`：`Clear` 后每个 `Known = Standing = true`。`Take(id)`：`Known = true`、`Standing = false`、`Version++`（已经倒了不动）。`StandingKnown`、`KnownCount` 维护成字段（不每次数）。
- **客户端**：`Crates` 属性（一个实例，复用）；`HandleMatchStart` 在 `SetZone` 后 `Crates.Reset(ModeRules.HasCrates(msg.Mode) ? msg.Crates : null)`；`LeaveMatch` → `Clear`；`HandleSnapshot` 在“只处理最新快照”的守卫之后 `Crates.Apply(snap)`；`OnData` 加 `case BattleMessage.Crate: HandleCrate(BattleMessages.ReadCrate(ref r))`：不在对局 / 对局号不对 / `!HasCrates(MatchInfo.Mode)` → 忽略；否则 `Crates.Take(id)`、入队（`Queue<CrateMessage>`，`TryDequeuePickup` 出队；`LeaveMatch` 清；队列按 `MaxCrates` 预开容量）。`OwnState` 把 `OwnTank` 的三个叠层写进 `TankState`（预测器算装填就对了）。
- **`Viewer.Wants`**：`CratePicked` → `sent = default; return false`。
- 文档注释里写清：箱子表从不下发；一行“看见”的是位置，`Standing` 说它还在不在。

**先写的测试**

- `CrateWireTests`：
  - `Version == 6`、`Crate == 27`。
  - 快照往返：3 行（含一行 `Standing false`）+ 叠层 (1, 2, 3) → 解码相等；和基线相同 → `sameCrates` 一位、解码后行相等；基线有 3 行、当前 0 行 → 解码 0 行；手写比特：count 65 → `ProtocolException`；id 不递增 → 坏包；kind 5 → 坏包；x 超范围 → 坏包；写 65 行 → `ArgumentOutOfRangeException`。
  - 16 行满快照 + 12 个看得见的箱子 + 叠层 3/3/3 的字节数（`Assert.Equal(N, w.ByteLength)`，记下 N；期望 ≈ 845 + 1 + 12 × 8 ≈ 940 < 1000）；48 行全可见 > 1000 时走可靠通道（`ChannelFor`）——只断言 `> MaxUnreliableBytes`。`RoyaleWireTests` 原 845 的断言改成新数（多了叠层 6 位和箱子块的位数：按实际）；`StandardWireTests` 815 → 新数。
  - `CrateMessage` 往返；越界各一条；读 `kind 7` 坏包。
  - 开局消息：无圈无箱子（团队战）往返、尾部字节数和 v5 时相比只多 1；有圈 + 3 个箱子往返；64 个；65 个写时抛、读 65 / 200 坏包；id 不递增坏包。
- `CrateKnowledgeTests`：`Apply` 两行 → 两个 Known、Visible；第二个快照只带第 0 行 → 第 1 行 Known 仍真、Visible 假、Standing 不变；带 `Standing false` 的行 → `Standing` 假、`StandingKnown` 减一；`Take(5)` 未知的箱子 → Known 真 Standing 假；`Reset` 两个 → 其余清掉；`Version` 只在内容变时加（同一快照 `Apply` 两次第二次不加）；`Apply` 不分配（`Allocation.AssertNone`）。
- `CrateClientTests`（照 `ZoneClientTests`：`a.OnData(0, bytes)`）：吃鸡对局里收 `Crate` 消息 → `TryDequeuePickup` 得到它、`Crates.All[id].Standing` 假；对局号不对 / 不在对局 / 团队战 → 忽略且 `DroppedPackets` 不加；坏消息 → `DroppedPackets` 加一；快照带箱子行 → `Crates` 更新、`Own.ReloadLayers` 跟着 `OwnTank`；重同步的开局消息带 2 个已知箱子 → `Reset` 后 2 个 Known Standing；离局 → 清空。

**步骤**：写测试、确认失败、实现、通过（`RoyaleWireTests`/`StandardWireTests` 的数改成实测）、提交。

**提交**：`feat(net): protocol v6 — the crates a viewer sees in the snapshot, own buff layers, the Crate message, known crates in MatchStart, CrateKnowledge on the client`，末行 `Tests: Net 369 → N.`

### Task 5：服务器

**Files:**
- Modify: `src/Tank.Net/Battle/BattleServer.cs`、`BattleServer.Match.cs`、`src/Tank.Server/Program.cs`（日志）
- Test: `tests/Tank.Net.Tests/Battle/CrateServerTests.cs`；`TestMaps.RoyaleStandIn` 加几个箱子的 override 帮手

**规则**

- 按“共享接口 → 服务器”。开局：`HasCrates(_mode)` 时在 `PickZone` 之后 `crates = CratePlacerOverride?.Invoke(field, grid, spawns, seed) ?? CratePlacer.Place(...)`，种子 `seating.MatchSeed ^ CratePlacer.SeedSalt`；个数 < `Count` 记一条警告日志；传给 `MatchRules`。日志 `match started` 那行后加 `crates: 48 placed`。
- `_crateKnown`/`_crateVisible`：`ulong[MaxPlayers]`，`TryStartMatch` 成功时和 `EndMatch` 清零。**不**在 `ResetForMatch` 里清。
- `TickMatch`：`vision.Update()` 之后 `if (crates != null && (sim.Tick % VisionTracker.UpdateInterval == 0 || 第一个 tick)) RefreshCrateSight(sim)`：对每个在对局的真人座位 `slot`，`standing = rules.Of(slot)`，有活车 → `_crateVisible[slot] = crates.VisibleTo(tank, field)`、`_crateKnown[slot] |= …`；否则 `_crateVisible[slot] = 0`。`rules.Update()` 之后 `foreach pickup in crates.Pickups`：`SeatOfTank(pickup.TankId)` 是真人且 `InMatch` → `_writer.Reset(); Write(CrateMessage(...)); Send(seat, Reliable)`。
- `Build`：`snapshot.CrateCount = 0`；`HasCrates` 时按 `_crateVisible[slot]` 的位从低到高填行（`Standing = crates.IsStanding(id)`，`X/Z = ToCm`）；`Own` 的叠层来自 `ToOwn(t)`（`ToOwn` 加三行）。
- `SendMatchStart`：`Crates = HasCrates ? 已知且站着的列表 : null`（按位循环建一个 `List<Crate>`：开局 / 重同步时可以分配）。
- `Program.cs`：监听行打常量 48（'48 crates a match'），实际摆放数由服务器 `crates: N placed (M by houses)` 日志经回调到控制台。
- 不分配：`RefreshCrateSight`、`Build` 的箱子行、`Pickups` 循环（`CrateMessage` 是 `readonly record struct`，写进 `_writer` 不分配）。

**先写的测试**（`CrateServerTests`，`RoyaleStandIn` + `ZonePickerOverride` 的大圈 + `CratePlacerOverride` 给 4 个箱子：墙西两个（一个在 Shooter 车前 3 m、一个 40 m 外）、墙东一个（Hidden 旁）、地图外 (40, 500) 一个（平地 200 m 的替身图上没有超出 270 m 的点））

- **只知道自己看见过的**：开局 1 s 后，西边客户端的 `Crates` 里有西边两个（Known、Visible、Standing），没有东边的；东边客户端反过来；两边都没有远处的。
- **开过去吃到**：让 Shooter 前进压过 3 m 外的箱子 → 它收到 `CrateMessage`（id、kind、amount 对：维修箱给掉过血的车），`Crates.All[id].Standing` 假，`Own.ArmorLayers`（装甲箱）变 1 并在快照里稳定；东边客户端一直不知道这个箱子。
- **别人吃掉的不广播**：西边另一个客户端（第三个）看得见那个箱子：它只在下一个点亮周期的快照里看到 `Standing false`，没有收到 `CrateMessage`；用 `Simulation.Respawn` 传送到墙后看不见那个位置，再由 Shooter 吃掉另一个箱子 → 它的表里那个箱子仍 `Standing true`（直到再看见）。
- **重同步**：吃到箱子的 Shooter 断线 0.5 s 后同令牌回来 → 开局消息的 `Crates` 正好是它之前 Known 且还站着的；它吃掉的那个不在里面。
- **死了不再看见新的**：被击毁的客户端 `_crateVisible` 为 0（快照 0 行），已知的还在（`KnownCount` 不变）。
- **非吃鸡**：团队战的快照 `CrateCount == 0`、开局消息 `Crates` 为 null 或空（读回永远是空数组），`OwnTank` 叠层 0；`BattleServerUnitTests` 的用例不变。
- 探针式断言：全程每个客户端 `LastPacketError == null`、`Resyncs == 0`。
- 另加的测试：座位被释放（宽限过了）清掉两个掩码；开局失败 ×3（摆放抛、表的 id 从 1 起、坐标 x = −1 出快照范围）；每 tick 不分配（`FakeServerTransport`，两次拾取在窗口内）；探针测试嗅探线上包（全程 `CratePicked` 事件 0 个、`Crate` 消息只到吃到的人、每个点亮 tick 用 `VisibleTo` 独立重算核对服务器的掩码）；重同步按请求（喂一个基线缺失的快照让客户端自己请求，开局消息带已知且还站着的两个、位置到厘米、`Version` 不白跳）。
- `BotLoadTests` 15 机器人那条打印 `RefreshCrateSight` 累计用时（真人只有 1 个：也打印“按 16 个真人算”的一遍用时，用 `CrateSetVisibilityTests` 的数）。

**步骤**：写测试、确认失败、实现、通过、`dotnet run --project tests/Tank.Soak -c Release` 五行全过（吃鸡一行的下行记进度）、提交。

**提交**：`feat(server): crates in the match — placed at the start, seen per seat at 5 Hz, sent by sight, the eater alone told, known crates on resync`，末行 `Tests: Net N → M.`

**批次 B 收尾**：两路审查（规格审查要用回环探针核对“没见过的箱子、别人吃箱子的时机不泄露”：每个快照的箱子行 ⊆ 服务器那一刻的 `_crateVisible`）→ 跟进提交 → 进度。

---

## 批次 C：机器人

### Task 6：顺路捡箱

**Files:**
- Create: `src/Tank.Sim/Ai/BotDirector.Crates.cs`、`Ai/Bot.Crates.cs`
- Modify: `Ai/Bot.cs`（`Leg.Crate` 进各处的路段列表：`Revived`、`onTheWay`、`stopThere`、`Begin`、`TakeAnswer` 的 NoPath、`CheckArrival`、`GiveUpLeg`、`NotFound`）、`Ai/Bot.Zone.cs`（圈变了取消捡箱；`WanderLeavesRing` 对 `Leg.Crate` 不适用）、`Ai/BotDirector.cs`（`Summary`）
- Test: `tests/Tank.Sim.Tests/BotCrateTests.cs`；`RoyaleMatch.cs`（箱子统计）、`RoyaleMatchTests`；`BotArena` 加 `crates`

**规则**

- 按“共享接口 → 机器人”。`CrateFor`：候选 = 站着、`IsUseful`、`DistanceSquared ≤ CrateRange²`、`CrateSight.Sees(me, p, field)`；优先级 `0` 维修（`Health < CrateHealthShare × MaxHealth` 且是维修箱）、`1` 弹药（`(ApLeft + HeLeft) < CrateAmmoShare × (Ap.Capacity + He.Capacity)` 且是弹药箱）、`2` 其他有用的；同级近的先、再 id 小的。时间：`toCrate = |p − me| × ZoneDetour`、`onward = max(0, |p − target.Centre| − InsideLimit(target)) × ZoneDetour`，`MustRun(toCrate + onward, MaxForwardSpeed, zone.TicksToTarget, zone.IsOutside(p))` 为真就不去（闭合后 `TicksToTarget == 0` → 从不去）。箱子在正在去的圈外（`!InRing(p, target)`）也不去。
- `WatchCrates`：`(tick + TankId) % VisionTracker.UpdateInterval == 0` 时查；在 `Leg.Crate` 上：箱子没了（`!IsStanding`，自己吃到就是）或 `StoppedAt(Goal)` 或 `tick − _crateBegun ≥ CrateGiveUpTicks` → 自己吃到 `CratesTaken++`（按 `TakenBy == TankId`），`NextWaypoint(me)`；不在 `Leg.Crate` 且满足开始条件、`CrateFor ≥ 0` → `CrateRuns++`、`Begin(Leg.Crate, p, keepPath: false)`。有目标（交战）时不开始，但已在路上的 `Leg.Crate` 继续（`onTheWay` 不含 Crate：停下交战，打完接着去）。`Steering.Toward` 的 `stopThere` 对 `Leg.Crate` 为真（停在箱子上就吃到了，车中心到箱子 < 0.6 + 半车宽）。`Bot.Zone.cs` 里圈变了：`Leg.Crate` 的目标不在新目标圈里 → `NextWaypoint`；`!InRing` 要跑圈照旧覆盖。
- `BotDirector.Summary` 前缀 `"{runs} crate runs, {taken} fetched; "`（`Crates != null` 时）。`BotArena` 构造末尾加 `IReadOnlyList<Crate>? crates = null`；`BotArena.Royale(map, seed)` 用 `CratePlacer.Place`。
- 不分配、确定：`CrateFor` 只循环；不用 `_rng`（不改变现有种子的重放：`RoyaleMatchTests` 的重放相等测试仍过；但机器人的走法变了，整局的数会变——记进度）。

**先写的测试**

- `BotCrateTests`（`BotArena`，吃鸡，平地，`crates:` 手摆；`Unarmed` 机器人）：
  - 掉血的机器人 60 m 外有维修箱 → `CrateRuns` 变 1、`Goal` 是箱子、`Activity Driving`，开到后 `CratesTaken == 1`、血回了、`Leg` 回到巡游（`Goal` 变）；满血的不去（`CrateRuns` 0）。
  - 优先级：血 50% 且弹药 20% 时，维修 50 m、弹药 30 m → 先去维修；血 80% → 先去弹药；三层满的装甲箱不去；`CrateRange + 1` m 的不去。
  - 墙后的箱子不去（看不见）；墙倒了去。
  - 时间：把 `TicksToTarget` 弄小（圈快缩完、箱子在圈的另一头）→ 不去；箱子在目标圈外 → 不去。
  - 交战中（放一个看得见的敌人）不开始；已经在去的路上遇到敌人 → 停下打（`Activity Engaging`），敌人没了继续去。
  - 被别人先吃了 → 放弃、`CratesTaken` 0、回巡游；`CrateGiveUpTicks` 后放弃。
  - 圈变了且箱子不在新圈里 → 放弃。
  - 放弃的箱子不再选（批次 C 审查跟进）：四块岩石围住的箱子（看得见牌子、没路）30 s 内只跑 1 次、`Stucks ≤ 2`、`not found ≤ 2`、不卡住（`LongestStuckSeconds < 10`）、机器人走开；超时放弃后 10 s 内 `CrateRuns` 仍是 1；`CrateShunTicks` 过了再选（格子用 `NavGrid.SetCostForTests` 封住再打开：路通了就 `CrateRuns == 2` 并吃到）；换圈清掉（换圈后马上再选，`CrateShunTicks` 未到）。
  - `Allocation.AssertNone`：4 个机器人、6 个箱子、一堵砖墙挡在一个机器人和它最近的箱子之间（射线真打到物件）、10 s（窗口里 `CrateRuns` 和 `CratesTaken` 都增加）不分配。
- `RoyaleMatch`：加 `CratesTaken`（全部）、`BotCratesTaken`、`PlayerCratesTaken`、`CrateRuns`（从 `Summary` 解析或从机器人取）、`CrateRepicks`（同一车刚放弃又去的）、`StandingAtEnd`、`TanksWithThreeLayers`、`FirstFullStackSeconds`；`RoyaleMatchTests`：带弹 4 个种子每局 `BotCratesTaken ≥ 3`（实测 13～26）、不带弹（车型 `Ap/He.Capacity = 0`：只清零弹药的旧写法会被弹药箱重新武装，种子 7 225 s 就互相打完）≥ 10（实测 13～23）、×50 ≥ 5（实测 7～22）；五局不带弹里至少一局有车叠满 3 层（`TanksWithThreeLayers`）；`CrateRepicks` ≤ 3；重放相等照旧；`A_royale_tick_allocates_nothing` 照旧；最长卡住照旧 < 10 s；各圈圈外照旧。
- Net `BotLoadTests` 吃鸡一行：打印 `crate runs / fetched`、tick p99（≤ 2 ms）和 `max` 旁边窗口内的 GC 次数（gen0/1/2）与暂停毫秒（偶发的慢 tick 一眼能看出是不是回收）。

**步骤**：写测试、确认失败、实现、通过；Release 跑 `RoyaleMatch`（打印每局的箱子数、结束时间、最长卡住，和 6A 的 E1 行比）；提交。

**提交**：`feat(bots): bots fetch crates — a useful crate in sight within 150 m when not fighting and the ring allows, repair first when hurt, ammo when low`，末行 `Tests: Sim N → M.`

**批次 C 收尾**：两路审查 → 跟进提交 → 进度（整局数字、tick p99、一遍视野的毫秒）。

---

## 批次 D：客户端

### Task 7：画面——颜色、木箱和牌子、`CrateViews`、闪光

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/CrateMesh.cs`、`scripts/World/CrateViews.cs`
- Modify: `scripts/Art/Palette.cs`、`scripts/Combat/BattleFx.cs`
- Test: `tests/Tank.Art.Tests/PaletteTests.cs`、`CrateMeshTests.cs`

**规则**

- `Palette`：新块 `// The supply crates (stage 6 design §4)`：`CrateRepair = Hex("4CCB5A")`、`CrateAmmo = Hex("F0B429")`、`CrateArmor = Hex("9A8FD0")`、`CrateReload = Hex("C65BD1")`、`CrateOptics = Hex("2EC4B6")`；`public static Vector3 Crate(CrateKind kind)`。
- `CrateMesh`（引擎无关，`MeshData`，顶点色线性）：`Body()`：`Box.Build((1.2, 1.0, 1.2), bevel 0.05)` 颜色 `Linear(Wood)`，四面各两条 `Planks` 色的窄条（薄盒）；`Post()`：0.08 × 0.9 的杆（`Box`），`Linear(Door)`；`Sign(CrateKind)`：0.7 × 0.5 × 0.06 的牌子，正反两面颜色 `Linear(Crate(kind))`，上面的图标是白色（`Linear(UiText)`）的几个盒子 / 环 / 棱柱，厚 0.02，略凸出：维修 = 十字（两个盒子）、弹药 = 一发竖着的弹（`Lathe` 小圆柱 + 尖）、加固装甲 = 盾（五边形 `Prism`）、快速装填 = 一个开口的环（3/4 圈）加一个小三角箭头、光学设备 = 两个小环并排，环都用 `CrateMesh.Arc`（实心环段；`RingMesh` 是贴地的平带）；`Build(CrateKind)` = `Merge`（箱 + 杆 + 牌子，牌子中心在 1.55 m，牌子的 y 基准单独给 `CrateViews` 浮动用：返回 `(MeshData Box, MeshData Sign)` 两份，牌子含杆）。常数 `SignCentreHeight = 1.55f`、`BobAmplitude = 0.12f`、`BobPeriod = 2.4f`。
- `CrateViews : Node3D`：`Build(Battlefield field)`（只记 `_terrain`，预建五种牌子网格和箱体网格各一份 `ArrayMesh`，材质 `Materials.FromShader("house")`——顶点色 toon；`CustomAabb`），`Sync(CrateKnowledge crates, double time, BattleFx fx)`：`crates.Version` 变了才扫 64 项：`Known && Standing` 且还没有节点 → 建（`MeshInstance3D` 箱 + 子节点牌子，位置 `(x, 箱底四角 HeightAt 的最小值, z)`，绕 y 随 id 转 `id × CrateMesh.YawPerId`（0.7）弧度）；有节点且不再 `Known && Standing` → `QueueFree`，上一帧它在视野里（`Entry.Visible`）或是自己吃的（`NoteOwnPickup`）才先 `fx.Pickup(位置 + (0, 1.6, 0), Palette.Crate(kind).ToColor())`（`CrateFlash.FlashesOnRemove`，偏差 40）。每帧（Version 没变也做）：每个存在的牌子 `Position.Y = SignCentreHeight + BobAmplitude × sin(2π (time / BobPeriod) + id)`。节点存在 `MeshInstance3D?[64]`，每帧不分配。
- `BattleFx.Pickup(Vector3 at, Color color)`：复用 `Flash` 槽（`ParticleKit.GlowBall` + `OmniLight3D`），0.5 s 长到半径 1.5 m 再淡出；颜色按箱子。**不做音效**（偏差 12：整个游戏目前没有一处音频；音效是跨阶段的事）。
- `--camera-preset`：截箱子特写用新增的吃鸡专用预设 `crate`（`Match/Logic/CrateCamera.cs`，偏差 38）。

**先写的测试**

- `PaletteTests`：五个精确的十六进制；两两距离 ≥ 0.25；每个离 `ZoneWall`、`Ally`、`Enemy`、`GrassTop`（草的主色，按 `Palette` 里实际的名字）≥ 0.2；`Crate(kind)` 五个映射。
- `CrateMeshTests`：`Body` 的包围盒 1.2 × 1.0 × 1.2（±0.06）、底在 y = 0、三角形数 > 0、没有 NaN、法线单位长；`Sign(kind)` 五种各不相同（顶点数或包围盒不同）、牌子中心高度 ≈ `SignCentreHeight`、宽 0.7；所有网格 `HasColors`；`MeshAssert` 的常规检查（照 `ZoneWallMeshTests`）。

**步骤**：写测试、确认失败、实现、通过；`godot-mono --import`（新 `.cs` 的 `.uid`）；试驾场六张回归和 `base5d` 比（不该变：试驾场没有箱子）；提交。

**提交**：`feat(client): crates in the world — five crate colours, the box and sign meshes, CrateViews from what the client knows, a pickup flash`，末行 `Tests: Art 1386 → N.`

### Task 8：HUD 逻辑

**Files:**
- Create: `src/Tank.Client/scripts/Match/Logic/CrateHud.cs`
- Modify: `scripts/Match/Logic/HudLayout.cs`、`MatchHudFrame.cs`、`HudDemoFrames.cs`、`scripts/Minimap/Logic/MinimapFrame.cs`、`MinimapFeed.cs`、`MinimapStyle.cs`、`scripts/Bootstrap/Logic/ShotPlan.cs`、`scripts/Bootstrap/LaunchOptions.cs`
- Test: `tests/Tank.Art.Tests/CrateHudTests.cs`；`HudLayoutTests`、`MatchHudFrameTests`、`MinimapFeedTests`、`HudDemoFramesTests`、`ShotPlanTests`、`LaunchOptionsTests` 加用例

**规则**

- 按“共享接口 → 对局 HUD 和小地图”。`CrateText.Pickup`：`Amount == 0 && Amount2 == 0` → `"{Name}已满"`；维修 `"+ 维修 {Amount}"`；弹药 `"+ 弹药 穿甲 {Amount} · 高爆 {Amount2}"`（其中一种为 0 时只写另一种：`"+ 弹药 高爆 8"`）；叠层 `"+ {Name} {Layers}/3"`。全部 `string.Create(InvariantCulture, …)`。
- `PickupFeed`：固定数组 3 项，满了丢最老的；`Visible(now)` 跳过 `ExpiresAt ≤ now` 的。
- `HudLayout`：常数 `PickupWidth = 600, PickupHeight = 44, PickupGap = 18, PickupRowShare = 0.8`，`Pickup` 框顶在横幅框底 + 18、高 `PickupBoxHeight = 44 + (PickupFeed.Capacity − 1) × 0.8 × 44 = 114.4`（三行：最新一行在最下，旧的每行往上 0.8 × 44，`HudBoxes.PickupRow(i)`；偏差 41）、`BuffsHeight = 32, BuffsGap = 30`（`StatusPanel.Y − StatusNameRoom − BuffsGap`（行的顶边；1600×900 下 662 − 36 − 30 = 596），宽 = 状态面板宽）；`HudLayoutTests.All()` 加两个框；`Buffs` 不和状态面板、小地图重叠（`MinimapScreens` 三个尺寸），`Pickup` 不和横幅、毒圈提示重叠。
- `MatchHudFrame.From` 末尾 `BuffHud buffs = default`；`MatchMain` 传 `BuffHud.From(own)`。
- 小地图：`MinimapCrate`、`MinimapFrame.Crates`（`MaxCrates`，`Debug.Assert` 溢出）、`AddCrate`、`Clear` 清；`MinimapFeed.AddCrates(frame, CrateKnowledge)`（循环 64，`Known && Standing`）；`MinimapIconSizes.Crate`（角 6、全屏 9）；`MinimapStyle.CrateFill(kind)`。
- `HudDemoFrames`：`royale-crates`：`RoyaleFrame` + `Zone` 等待 + `Buffs (1, 2, 0)`，`Pickups` 三行（“+ 快速装填 2/3”“+ 维修 270”“光学设备已满”，`ExpiresAt` 10），小地图 `RoyaleMap` 加 6 个 `MinimapCrate`（五种各一加一个）；`HudDemo.Pickups`；`Names` 末尾追加；`HudDemoFramesTests` 的顺序测试把它加进 `RoyaleScreens`。
- `ShotMoment.Pickup`（末尾）、`MatchMoments` 加、`TryParseMoment("pickup")`、警告文字、`LaunchOptions.ShotMoments` 加 `"pickup"`、文件头注释。

**先写的测试**

- `CrateHudTests`：`CrateText.Name` 五个；`Pickup` 六种文字（上面的例子 + `"+ 弹药 高爆 8"` + `"维修已满"` + `"弹药已满"`）；`Layers(2) == "×2"`；`BuffHud.From` 从 `TankState` 取三个字节、`Any`；`PickupFeed`：加 4 条留 3 条（最老的没了）、过期的不在 `Visible`、`Revision` 每次 `Add` 加、`Visible` 不分配。
- `HudLayoutTests`：1600×900 下 `Pickup == (500, 480, 600, 114.4)`、`Buffs == (24, 596, 360, 32)`；三种尺寸都在屏内、不重叠（上面列的）；`PickupFeed.Capacity` 行都在 `Pickup` 框里。
- `MatchHudFrameTests`：`From` 不传 `buffs` → default；传了带上。
- `MinimapFeedTests`：`AddCrates` 只加 `Known && Standing` 的、按 id；`Clear` 清箱子；`Filling_a_frame_allocates_nothing` 加箱子。
- `HudDemoFramesTests`：`royale-crates` 自洽（有 `Pickups` 3 条、`Buffs.Any`、小地图 6 个箱子）、顺序测试过。
- `ShotPlanTests`/`LaunchOptionsTests`：`--shot-after pickup:2` 解析；对局里 `OnMoment(Pickup)` 后 2 s 截。

**步骤**：写测试、确认失败、实现、通过、提交。

**提交**：`feat(client): crate HUD logic — pickup toasts and text, buff layers, two HUD boxes, crates on the minimap frame, the royale-crates gallery screen, --shot-after pickup`，末行 `Tests: Art N → M.`

### Task 9：HUD 视图和接线

**Files:**
- Create: `src/Tank.Client/scripts/Match/PickupToastView.cs`、`Match/BuffRowView.cs`
- Modify: `scripts/Match/MatchHud.cs`、`HudGallery.cs`、`scripts/Minimap/MinimapView.cs`、`scripts/Overlay/CombatHud.cs`、`scripts/Match/MatchMain.cs`、`MatchMain.Events.cs`、`MatchAutomation.cs`
- Test: 无新的 Art 测试（视图是 Godot）；截图验收

**规则**

- `PickupToastView : HudPanel`：`Boxes.Pickup`（三行高）里从下往上画 `PickupFeed.Visible(now)` 的每行（`Boxes.PickupRow(i)`：最新在最下，旧的每行往上 44 的 0.8，都在框里），字号 28、居中描边（字宽随 `Revision` 缓存），颜色 `Palette.Crate(kind)`，最后 0.35 s 用 `PopupCurves.Alpha` 淡出，头 0.25 s 用 `PopupCurves`（新加 `PickupScale(age)`，照 `VerdictScale`，Art 测试一条）弹入；`Revision` 变或有行在显示时 `QueueRedraw`（每帧，行在淡出）。
- `BuffRowView : HudPanel`：`Boxes.Buffs` 里从左到右三个 32 × 32 的图标，图形占满约 30 px（加固装甲 = 盾，顶点 y 1～31；快速装填 = 开口环半径 13、线宽 4 + 箭头；光学设备 = 两片镜半径 8、线宽 5、深色镜片，上面两个目镜；用 `DrawColoredPolygon`/`DrawArc`/`DrawLine`，颜色 `Palette.Crate(kind)`，深色描边 2），间距 76，右下角 `CrateText.Layers(n)` 字号 20；0 层的不画；`Buffs` 变了才重画；和状态面板一起隐藏（死了、结算、计分板）。
- `MatchHud`：挂两个视图（`ZoneWarningView` 之后）；`OnPickup(in CrateMessage m, double now)` → `_pickups.Add(...)`；`Refresh` 传 `frame.Buffs`；`ApplyVisibility` 结算 / 计分板时藏飘字和叠层。`HudGallery`：`_Ready` 里重放 `demo.Pickups`（`now 0`，`ExpiresAt` 由帧给）。
- `MinimapView`：圈之后、基地和视野扇形之前（设计 §4.3“在基地、扇形下面”）画箱子：实心方块（`MinimapGlyphs.Square(centre, size, into)` 新加，4 点）+ `IconOutline` 描边，边长 `Icons.Crate`。
- `CombatHud`：装填环的分母 `CrateRules.ReloadTime(player.Spec, player.ReloadLayers)`；状态面板的“装填 N.N 秒”照旧用 `Reload`。
- `MatchMain`：`BuildScene` 里 `HasCrates(Mode)` → `_crates = Add(new CrateViews()); _crates.Build(field)`；`StepMatch` 第 11 步旁 `_crates?.Sync(_client.Crates, _time, _fx)`；事件循环后 `while (_client.TryDequeuePickup(out var p)) { _hud.OnPickup(p, _time); _automation.NotifyPickup(_time); }`（闪光由 `CrateViews.Sync` 在箱子没了时放，只放给“看着它被吃掉”的：上一帧它在视野里，或是自己吃的——拾取消息出队时 `_crates?.NoteOwnPickup(id)`，同一帧的 `Sync` 用掉；偏差 40）；`ShowMinimap` 里 `MinimapFeed.AddCrates(_minimapFrame, _client.Crates)`；`_hud.Refresh(MatchHudFrame.From(..., zone, BuffHud.From(own)))`。`MatchAutomation.NotifyPickup(double)` → `OnMoment(ShotMoment.Pickup)`。重同步（`TakeField`）：`CrateViews` 不重建（表由 `CrateKnowledge.Reset` 给、`Version` 变了自然同步：被 `Reset` 丢掉的节点要删——`Sync` 对 `!Known` 的有节点也删，不闪）。

**截图**（`build/shots/6b-t9/`，都要用 Read 看过、写进进度）

- 画廊 `--hud-demo royale-crates`（`--screenshot-at 4 --quit-after 5`）：三行飘字、叠层图标 ×1 ×2、小地图六个方块。
- 真对局（种子按 probe 选一个出生点 40 m 内有箱子的）：`--host --mode royale --map riverland --bots 15 --autostart --autodrive --seed S --shot-after pickup:1`（飘字 + 闪光刚过 + 叠层）；`--camera-preset crate --screenshot-at 10`（离自己最近的已知箱子特写：木箱、彩色牌子、图标；偏差 38）；`--full-map --screenshot-at 60`（全屏图上见过的箱子方块）；`--screenshot-at 150`（小地图角上的方块、看得见的箱子在场景里）。
- 回归：画廊 `royale`、`standard`、`minimap`、`scoreboard` 和 6A 逐字节相同（新框里没内容时不画）；试驾场六张和 `base5d` 同量级；帧率 `--fps-log` 150 s（16 辆车 + 光墙 + 箱子）和 6A 的 115～116 比。

**步骤**：实现（无新 Art 测试；`PopupCurves.PickupScale` 一条）、`dotnet build`、`--import`、截图、回归、提交。

**提交**：`feat(client): crates on screen — pickup toasts, the buff row, crate squares on the minimap, CrateViews and the flash wired into the match, the reload ring by the buffed time`，末行 `Tests: Art N → M.`

**批次 D 收尾**：两路审查（规格审查对照设计 §4.3、§6 的截图清单）→ 跟进提交 → 进度。

---

## 批次 E：收尾

### Task 10：验收

不改代码（发现问题就修，修的提交单独写 `fix(<scope>): …`）。写 `accept6b.sh` 放在开发脚本目录（照 `accept6a.sh`，阶段：`build release soak gallery match idle shots modes sandbox server smoke left`）跑下面这些。

- [ ] 五个项目 0 警告；四个测试项目全过（记数字；Update 42 不变）。
- [ ] 整局（设计 §1 的 6B 验收）：`--host --mode royale --map riverland --bots 15 --autostart --autodrive --seed 5 --fps-log --shot-after ended:2 --screenshot …/royale-ended.png --quit-after 700`：服务器日志 `crates: 48 placed`、`bots: N crate runs, M crates`，M ≥ 3；玩家（`--autodrive`）吃到至少 1 个（日志 `[match] picked up` 一行：任务 9 在客户端收到 `CrateMessage` 时打一行日志）——没吃到就换种子再跑一局，两局都记。第二局不开车（`--shot-after death:8`）看机器人；都记箱子数。
- [ ] 截图（设计 §6）：箱子特写、拾取提示 + 闪光、叠层图标（≥ 2 种）、小地图和全屏图上的箱子、画廊 `royale-crates`；都看过、写进进度。
- [ ] 不泄露（回环探针，`CrateServerTests` 的断言在真对局形态下再跑一遍：`BotLoadTests` 式的 1 真人 + 15 机器人，每个快照的箱子行 ⊆ 那一刻 `_crateVisible`，别人吃箱子前后客户端的表不变直到再看见）。
- [ ] 效果上限（Release 整局 `RoyaleMatch` 不带弹：有机器人叠到 3 层、第 4 个同类箱子 `Wasted`；血不超过上限、弹不超过携弹量）。
- [ ] 回归：原有四种模式各开一局短的（截图和 6A 同类的一样；快照 `CrateCount 0`）；试驾场六张和 `base5d` 同量级；soak 五行全过、吃鸡一行下行和 6A 的 4.1 KB/s 同量级（记数）；画廊 `royale` 等和 6A 逐字节相同。
- [ ] 开销（写进进度）：服务器 tick p50/p99（`BotLoadTests` 吃鸡一行，Release，对比 6A 的 0.024 / 0.868 ms）、箱子视野一遍（16 辆车 × 48 个箱子）的毫秒数、最坏快照字节数（`CrateWireTests` 的数）、真对局快照 avg/max、帧率（`fpsstats.sh`）。
- [ ] 专用服务器 `--mode royale` 日志有 `crates 48`；冒烟参数仍够用（`[server] match started`、`[match] entered match`）。
- [ ] `pgrep -fl 'MacOS/Godot'` 没有残留。

**提交**：没有代码改动就不提交，结果记 `build/shots/6b-t10/RESULTS.md` 和进度（下一个任务一起提交）。

### Task 11：文档

**Files:** `docs/01-game-design.md`、`docs/03-netcode.md`、`docs/04-architecture-and-plan.md`、`README.md`、`docs/PROGRESS.md`

内容以代码为准，不写没做的东西：

- `docs/01`：§2 吃鸡一节加“补给箱”小节（摆放规则和数、五种效果的表、拾取、叠层上限、满了照吃；画面和 HUD：箱子、飘字、叠层图标、小地图；不做的）；§2.3 机器人加捡箱一段（优先级、150 m、时间够不够）；原来“顺路捡箱子（6B）还没做”的两处改掉；§5.5 弹药“对局里不补弹”改成“对局里只有吃鸡的弹药箱补弹”；§6 点亮加光学设备的视野；§7 HUD 表加两行。
- `docs/03`：协议 6（版本历史一句）、消息表加 `Crate = 27`、快照的箱子块和 `OwnTank` 叠层、开局消息尾部的箱子、“按视野下发、不发表、不广播别人吃”的一段（§5 视野过滤旁）、最坏快照字节数更新、soak 吃鸡一行的数更新、tick 顺序里加“箱子视野（5 Hz）”和“拾取消息”。
- `docs/04`：仓库结构加 `Modes/Crates.cs`、`CratePlacer.cs`、`Vision/CrateSight.cs`、`Battle/CrateKnowledge.cs`、`Art/Mesh/CrateMesh.cs`、`World/CrateViews.cs`、`Match/Logic/CrateHud.cs`、两个视图；`MatchRules.Update` 顺序；§5 测试表加新测试类；§6 路线 6B 已完成；§8 风险表的 6B 一行写实测。
- `README.md`：进度表 6B ✅；特性一句里加补给箱。
- `docs/PROGRESS.md`：6B 节点记录最后一行，“当前位置”改为 6B 完成、下一步 6C。

**命令**：`git diff --stat`；文档里提到的每个类名、常数、数字用 grep 在代码里核对一遍。

**提交**：`docs: stage 6B — supply crates in docs 01, 03 and 04 and the README`，末行 `Tests: unchanged (…)`。

**批次 E 收尾**：两路审查（规格审查对照设计 §4、§6 的箱子部分和下面的验收清单；文档审查对照代码）→ 跟进提交 → 进度 → 验收清单全部打勾后 squash 合入本地 main（见“执行方式”），合入后在 main 上再跑一遍四个测试项目和 0 警告检查。

---

## 验收清单（6B）

- [x] 四个测试项目全部通过，五个项目 0 警告；Update 的测试数不变，Sim、Net、Art 只增不减。（Sim 1273 → 1398+、Net 369 → 464+、Art 1386 → 1478、Update 42；`RESULTS.md` 第 1 项；E 批跟进后数见进度）
- [x] 摆放：河谷每局 48 个，29 个在房子旁 4～15 m、10 个在路边、9 个在树林边；两两 ≥ 25 m；不在水里、坡 ≤ 8°、不压物件、在能开到的陆地上、离出生点 ≥ 20 m；种类 12 / 10 / 10 / 10 / 6；同一种子同一结果（测试，种子 1～50）；probe 的图看过。（`CratePlacerTests` 种子 1～50；`6b-t2/crates-{3,7,11}.png`）
- [x] 拾取：活车车身（OBB）碰到 0.6 m 的圆就吃，立刻生效，箱子消失；同一 tick 两辆车碰到同一个编号小的吃到；残骸不吃；开局保护不挡。（`CratePickupTests`；偏差 1）
- [x] 效果：维修回 `ceil(30%)` 不超上限；弹药两种各补 `ceil(一半)` 不超携弹量；装甲 −10%/层只乘炮弹伤害（毒圈不乘、不改击穿）；装填 −8%/层（客户端预测一致）；视野 +10%/层；三种最多 3 层；满了照吃不起作用；`CratePicked` 事件。（`CrateRulesTests`、`CrateEffectsTests`；第 4 个同类箱子只有单元测试，偏差 44；整局上限探针 `RESULTS.md` 第 9 项）
- [x] 联网 v6：箱子表不下发；快照只带这个座位当前看得见的箱子（位置、种类、还在不在），每次点亮（5 Hz）算一遍，按炮塔顶到牌子顶的视线和带光学的视野；客户端记住见过的直到再看见已空；自己吃到只发给自己（`Crate` 消息）、别人吃掉的不广播；叠层只在自己的快照里；重同步带已知、还在的箱子；编解码往返和边界测试；回环测试证明不泄露。（`CrateWireTests`、`CrateServerTests` 探针、`ViewerTests`、`CrateKnowledgeTests`、`CrateClientTests`；河谷 1 + 15 探针 `RESULTS.md` 第 8 项；重同步见偏差 45）
- [x] 画面和 HUD：见过的箱子在场景里（木箱、彩色牌子和图标、牌子浮动），没见过的不画；被吃掉时闪光；吃到时屏幕中间偏下飘字三种文案；状态面板上方叠层图标带层数、0 层不显示；小地图和全屏图上见过还在的箱子五色方块，自己吃的马上消失，别人吃的再看见那处才消失。（`CrateFlashTests`、`CrateHudTests`、`MinimapFeedTests`；真对局只见一种叠层图标，偏差 43）
- [x] 机器人：不交战时去 150 m 内看得见的有用箱子，血 < 60% 维修优先、弹 < 一半弹药优先、再没满层的；满层和没用的不去；来不及进圈不去；确定、每 tick 不分配（测试）。（`BotCrateTests`、`RoyaleMatchTests.A_royale_replays_from_its_seed`）
- [x] 无画面整局：1 名不动真人 + 15 个机器人的吃鸡里机器人吃到箱子（数记进度）、五局不带弹里至少一局有车叠满 3 层（`TanksWithThreeLayers`）；原有的整局断言照旧；服务器 tick p99 在预算内。（Release 整局 18/18；不带弹种子 3、8 叠满；`BotLoadTests` p99 0.937 ms）
- [x] 16 辆车的箱子视野开销、最坏快照字节数、真对局快照和下行、帧率都实测并写进进度。（进度 E1 行、`RESULTS.md` 开销表）
- [x] 回归：原有四种模式快照 `CrateCount 0`、行为和截图不变；试驾场六张和 `base5d` 同量级；soak 五行通过；画廊原有的屏逐字节相同。（`mode-*.png`、`CrateServerTests` 团队战、试驾场在 worktree 漂移内、soak 五行、画廊九屏 `cmp` 相同）
- [x] 画廊 `royale-crates`；真对局的箱子特写、拾取提示、叠层图标、小地图和全屏图上的箱子都看过。（`RESULTS.md` 截图清单）
- [x] 文档 `docs/01`、`docs/03`、`docs/04`、README 已按代码更新；`docs/PROGRESS.md` 的“6B 节点记录”完整。（`9e8cf6e` 和批 E 跟进）
- [x] 每批两路审查和跟进提交都做了；squash 合入本地 main，合入后 main 上全部测试通过、0 警告；没有推远端。（A `577a097`、B `f22f7a5`、C `6014bf9`、D `dd2224b`、E `cb16915`；squash 合入本地 main `2ac211d`，合入后全过、0 警告）

---

## 与设计的偏差

写计划时按现在的代码定下来的做法，和阶段 6 设计 §4 字面不一样或设计没说的地方：

1. **拾取判定用车身的 OBB 对箱子的圆**（`TankMotion.Footprint(...).IntersectsCircle(p, 0.6)`），不是“车身的圆对箱子的圆”：代码里没有车身圆，物件压车（`Simulation.Props` 的 `UnderATank`）就是这么判的，更准、也现成。
2. **种类按固定份额 12 / 10 / 10 / 10 / 6 洗牌**，不是逐个按比例抽：每局五种都有、数量确定，测试好写；设计写的比例恰好对应这几个数。
3. **“树林边”的判法是地图无关的**：20 m 内 ≥ 3 棵树、最近的树干 ≥ 3 m、不在任何树冠下——林间空地也算得上“林边”；没有用 `RiverlandLayout` 的树林矩形（那是河谷专有的 internal 数据，箱子的规则不该绑在一张图上）。probe 的图确认分布看得过去。
4. **摆放多了三条设计没写的规则**：离任何出生点 ≥ 20 m（不然开局第一帧就被吃了）、离可开车范围的边 ≥ 10 m、所在格开着且和出生点连通（`ZonePicker.Land`：箱子得开得到）。
5. **弹药补半数向上取整**：轻 / 中坦 20 + 8、重坦 13 + 8、歼击车 15 + 6（设计写“向上取整”，这里把数列出来）。
6. **装甲层的取整**：乘完 `RoundHalfUp`，有伤害的至少 1。
7. **叠层在 `TankState` 里**（三个字节），而不是旁表：装填要进客户端预测（`TankStep` 两端共用），只有放进车的状态并通过 `OwnTank` 下发，预测的装填时间才和服务器一致。
8. **`CratePicked` 事件不加字段**：`TankId` 是吃到的车、`PropId` 是箱子编号、`Damage` 是第一个数；服务器从不发它（`Viewer.Wants` false），吃到的人收的是专门的 `Crate` 消息（含种类、位置、两个数、层数）。机器人和测试直接读 `CrateSet`。
9. **视野掩码是每个座位一个 `ulong`**（所以 `MaxCrates = 64`，河谷放 48）；“看见”的是**位置**（吃掉的也算看见），`Standing` 说它还在不在——这就是“客户端记住见过的箱子，直到再看见它已空”。死了的人不再看见新的（设计没说；各自为战式的“死后看全场”不适用于箱子，不然观战能帮队友（6C）找箱子）。两次点亮之间的快照行来自上一次的掩码、`Standing` 取当刻；两次点亮之间被击毁的车最多再带 5 tick 旧行（探针实测 ≤ 3）。
10. **每个座位的“见过”掩码不随宽限内重连清掉**（`ResetForMatch` 不碰它）：重同步的开局消息带已知的箱子，正是为了重连。
11. **`Crate` 消息里带位置**：客户端一定在快照里见过这个箱子才可能压到它，但吃到和第一次看见可能在同一个点亮周期里，带上位置就不依赖这一点。
12. **不做音效**：整个客户端目前没有一处音频（没有播放器、没有音频资源、画面全由代码生成）；给箱子单独引入第一段音频超出本阶段，留给以后统一做。被吃掉时只有闪光。
13. **飘字在 HUD 的 `MatchHud` 里**（新框 `Pickup`，横幅下 18 px），不在 `CombatPopups`（那是世界坐标锚定的伤害数字）；叠层图标也在 `MatchHud` 的新框 `Buffs` 里（状态面板上方 30 px）而不是 `StatusPanel` 里：这样画廊（只画 `MatchHud` + 小地图）能截到。
14. **满血吃维修、满弹吃弹药也飘“已满”**（设计只给了叠层的例子）。
15. **机器人的优先级**按“维修（血 < 60%）> 弹药（< 一半）> 没满层的任一种”，同级近的先；箱子在正在去的圈外不去（设计只说“到箱子再到圈里的路程超过剩余时间的就不去”）；去箱子的路上遇到敌人照旧停下打，打完接着去（“不在交战时”指开始条件）。机器人不用随机数挑箱子，老种子的重放仍逐 tick 相等。“弹 < 一半”按两种弹的剩余合计对两种携弹量之和算（`CrateAmmoShare`，`BotDirector.Crates.cs`），不是任一种少于一半。
16. **机器人查箱子每 6 tick 一次**（按车号错开），和点亮同频，为了 15 个机器人的 tick 预算。
17. **soak 的吃鸡一行不改**（60 s，机器人会吃到几个箱子；带宽多的只有箱子行）。
18. **维修和弹药的向上取整用整数千分法**（`(whole × 300 + 999) / 1000`、`× 500`），不用 `MathF.Ceiling(x × 0.3f)`：`1400 × 0.3f` 在 float 里落在 420 之上一点，会取到 421。数字和设计一致（180 / 270 / 420 / 285；20 / 8 / 13 / 15 / 6）。
19. **种类份额的取整**是“各自四舍五入、多出的从最稀有的种类（光学起）扣、不足补维修”，不是严格的最大余数法（那会给 12 / 10 / 10 / 9 / 7）：按设计百分比对应的 12 / 10 / 10 / 10 / 6 做，10 个时 3 / 2 / 2 / 2 / 1。
20. **非房子的 19 个先路边再树林边（10 / 9，`RoadShare = 0.5`）**，id 房 → 路 → 林，而不是一条 `ByRoad || ByForest` 合起来抽：第一次 probe 发现合起来抽时约九成落在树林里（树多路少）、村子之间的公路空着。路边阶段的次数上限是房子阶段剩余的一半，树林阶段用剩下的；哪一段放不够由下一段补足。河谷种子 1～50 实测三段都放满，最多用 1091 / 40000 次。
21. **箱子视野一遍 16 辆实测 0.9～1.1 ms（Release）**，不是计划的 < 0.5 ms；测试上限 2 ms（Debug 10 ms 只打印）。成本是 `HeightField.Raycast` 每米一步的地形行进（≈ 8 µs 一条，出生点处 110 条）；服务器只给真人座位算、每秒 5 次。批次 B 实测 tick p99，不够再在服务器侧错开座位或按（炮塔顶、`StateVersion`）缓存掩码，射线本身不改。
22. **任务 1 的两条测试按可行的数改**：视野用 `Concealment(0, 0)` 的轻坦（真实隐蔽下 290 m 看不见任何车），另加隐蔽 0.4 在 175 m 的用例；“先吃维修再挨毒”用第 2 圈的 2 %（第 1 圈 1 % × 900 = 9 杀不死 10 血）。
23. **多了两个 internal 重载**：`CratePlacer.Place(…, bool[] land, out int attempts)`（测试读用了多少次）、`CrateSight.LineClear(eye, crate, field)`（`VisibleTo` 对一辆车只算一次炮塔顶）；`TestWorld.WideFlatField()` 400 m 平地给 290 m 的视野测试用；`CrateSet` 对越界编号：`IsStanding`/`Take` 返回 false，`TakenBy`/索引器抛。
24. **开局消息的箱子段预留按实际个数**而不是 1 + 64 × 10；`CrateMessage` 负对局号抛 `ArgumentOutOfRangeException`；叠层写时封到 3 而不是靠位宽截断。
25. **快照的箱子坐标是 VarUInt 厘米（无符号）**：负坐标写时抛；`CratePlacer` 的 `EdgeMargin` 保证不会出现，服务器开局再按同一范围检查一遍。开局消息和 `Crate` 消息里的位置服务器先按厘米取整再发，三条路给客户端的位置完全一致（重同步后 `Version` 不会白跳）。
26. **客户端的拾取队列预开 64、满了丢最旧**；新开局（非重同步）清空；`CrateKnowledge.Clear` 只在有已知时加 `Version`；`Take` 带种类和位置的重载给 `Crate` 消息用。
27. **服务器**：座位被释放（宽限过了）时清掉该槽的两个掩码；对局结束日志 `crates: T of N taken, H by players, B by bots`；开局前先 `new CrateSet(table)` 并查坐标范围，让坏表按“the crates”失败；`Program.cs` 监听行打 `CratePlacer.Count` 常量；箱子成员放在 `BattleServer.Crates.cs`。
28. **宽限内断线期间自己吃到的箱子**：`Crate` 消息发不出去（座位未连接），效果照常生效（叠层走 `OwnTank`），飘字丢失，客户端重同步后在行里再看见空位。批 D 的 HUD 不能假设每次自己吃到都有 `Crate` 消息。
29. **`Viewer.Wants(CratePicked)` 有单元测试**，探针测试嗅探线上包：全程 `CratePicked` 事件 0 个、`Crate` 消息只到吃到的人，并在每个点亮 tick 用 `VisibleTo` 独立重算一遍核对服务器的掩码。
30. **整局的箱子下限按实测下调**：带弹 ≥ 3（实测 13～26）、不带弹 ≥ 10（计划 15，实测 13～23）、×50 ≥ 5（计划 15，实测 7～22）；×50 的机器人一直在交战，“不交战时”是开始条件。
31. **不带弹的整局给机器人车型弹舱容量 0**，不再只清零弹药：清零的车会去吃弹药箱重新武装互杀（种子 7 225 s 就打完）。Sim / Net / Client 没有除以 `Capacity` 的地方。
32. **捡箱的结束判定每 tick 在 `CheckArrival` 里**（到了 / 没了 / 路走完 / 超时），开始仍按 6 tick 一查。
33. **`CrateFor` 便宜的条件先查、射线最后只对能胜出的候选**（优先级更高或同级更近才射），结果与定义相同（规格审查给了证明）；一次查看先算一次炮塔顶用 `LineClear`。
34. **没有圈的吃鸡（只有测试）不查圈和时间**。
35. **放弃的箱子记进每车的掩码**（`CrateShunTicks` = 40 s 后或换圈时清），`CrateFor` 跳过：原来放弃后 ≤ 6 tick 又选同一个，被岩石 / 停着的车封住的箱子会让机器人原地反复寻路（审查复现 60 s 不动、300 次重选）。
36. **`Bot.CratesTaken` 只计跑去并吃到的**（`TakenBy == TankId`），`Summary` 写 `fetched`；服务器局末日志的 `by bots` 按 `TakenBy` 算、含顺路撞上的，两个数可以不等。
37. **超时计时在交战中照走**（上限简单，顶多多算一次跑箱）。
38. **箱子特写用新的吃鸡专用镜头预设 `crate`**（`Match/Logic/CrateCamera.cs`，照 `zone`）：出生点离箱子 ≥ 20 m（偏差 4），跟车的预设（眼睛离车 7.7～11 m）看得到的箱子最近也在 26 m 外，河谷的固定地标 1～1500 号种子最近 22 m；新预设对准离自己最近的已知、还在的箱子，从牌子正面斜 30°、3.6 m 外、1.9 m 高看。
39. **叠层图标**：32 × 32 的格子里图形占满约 30 px（盾、开口环加箭头、两片镜），从左往右排、0 层不占位（间距 76），右下角层数 20 px。
40. **闪光只在“看着它被吃掉”时放**（上一帧它在视野里，或是自己吃的）；再看见空位、重同步后不再知道的、离局清空的箱子直接消失、不闪：设计写“被吃掉时”闪，迟到的闪光会让人以为附近有人刚吃了它。闪光在 1.6 m 高（牌子高度），跟车镜头里不被车身挡住。
41. **飘字框按 3 行定高**（`PickupFeed.Capacity`），最新在最下、旧的往上，都在框里，不压横幅；画廊冻结在时钟 0，画廊的飘字按 `ExpiresAt − Lifetime` 加入、出生时间在时钟之后的画成已定格；客户端每次收到 `Crate` 消息打一行 `[match] picked up <kind> <a>/<b> layers <n>`（验收用）。
42. **拾取闪光用 4 个专用槽**（`Flash.Recolour` 换成箱子的颜色），不和炮口、命中共用。牌子浮动最高到 1.92 m，高于视线判定的 1.8 m（只画已知的箱子，不影响信息）。加固装甲的淡紫离友军蓝灰 0.199，颜色测试的下限是 0.19。小地图的箱子方块画在圈之后、基地和视野扇形之前（设计 §4.3），计划原文写在基地之后。
43. **真对局里只拍到一种叠层图标**（任务 10 要求“叠层图标 ≥ 2 种”）：自动驾驶的玩家在 16 辆车的吃鸡里 25～40 s 就阵亡（6A 遗留的机器人密度问题，6A 偏差 18）。验收扫了 14 个种子，只有 153（光学）、11（装甲）各吃到一个叠层箱，没有一局吃到两种。两种同屏只在画廊 `royale-crates` 里（盾 ×1、装填 ×2）。画廊和真对局走同一个 `BuffRowView` 和 `BuffHud`（`CrateHudTests`、`MatchHudFrameTests`），真对局里一种叠层的图标（`pickup-flash.png`，光学 ×1）看过。留给试玩看。
44. **“第 4 个同类箱子 `Wasted`”没在整局里出现**（任务 10 要求在 Release 整局里看到）：机器人按设计 §4.5 不去满层的箱子，验收的整局探针（4 局 70.7 万次车·tick 检查）里也没有顺路撞上。这一条由 `CrateEffectsTests.A_layer_crate_adds_a_layer_up_to_three_and_the_fourth_is_wasted` 覆盖（装甲、装填、光学三例：箱子消失、`Wasted`、层数仍是 3、照发 `CratePicked`）；`CrateSet.Collect` 不看有没有用都吃掉。“满了照吃不起作用”在整局里由满血吃维修 4 次、满弹吃弹药 4 次走通，叠层 ≤ 3 由同一探针逐 tick 检查。
45. **重同步带的是“见过、没看见已空”的箱子**，不是设计 §4.4 字面的“见过、还在的”：按字面做，每次重同步客户端都会知道自己见过、后来在视野外被别人吃掉的箱子已经没了，改过的客户端每秒请求一次重同步（服务器限 1 次/秒）就能以 1 s 精度得知“某个见过的箱子刚被吃了”，违背改动记录“别人吃箱子的时机不泄露”的本意。每个座位多记一个“看见已空”的掩码（快照行里 `Standing` 为假的、自己吃的），重同步发 见过 ∧ ¬看见已空，客户端的表和重同步前一致。
