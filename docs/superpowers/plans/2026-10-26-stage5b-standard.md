# 阶段 5B：标准战 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增第四种模式“标准战”：两队、不复活、限时 10 分钟，每队一个半径 25 m 的基地；全歼对方或把对方基地占满 100 分获胜，同一 tick 双方都满足是平局，超时平局。两张图（试驾场地图、河谷）都摆好两队基地；快照带两个基地的分数、开局消息带基地位置；客户端在地上画两个圈、比分板下面画进度条、计分板多一列“占领”、结算写胜负原因；两个实例在两张图上各打完一局标准战。

**Architecture:** 规则全在 `Tank.Sim.Modes`：`BattleMode.Standard = 3`；新的 `BaseCapture` 只管占领的计数（每辆车按 tick 攒、整数、确定），`MatchRules` 在标准战里每 tick 先记击毁、再让 `BaseCapture` 读这一 tick 的车和事件、最后判胜负并记下结束原因（`MatchEndReason`，所有模式都记）。基地是地图的一部分但不进地图编码：`TeamBase` 常数放在 `SandboxMap.Bases`、`RiverlandMap.Bases`，经 `MapDefinition.Bases` 交给服务器。`Tank.Net` 升到协议 v4：对局块追加两个基地分数和结束原因，计分板每人追加“占领”最高分，开局消息末尾追加基地。客户端只读这些：`Match/Logic` 里的引擎无关逻辑（文字、进度条、闪烁）由 Art 测试覆盖，视图在 `Match/`，地上的圈在 `World/BaseRings` + `Art/Mesh/RingMesh` + `base_ring.gdshader`。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9.3、LiteNetLib 2.1.4、Godot 4.7.2 .NET 版。只做 macOS。

**设计依据：** [阶段 5 设计](../specs/2026-10-22-stage5-design.md) 的 §3（5B 规则、基地和占领、网络、界面）、§6 的 5B 测试和每个小阶段的通用要求；§1 用户的选择照用（所有模式都能在两张图上开、试驾场地图也摆两队基地、机器人数量房主设、标准战限时 10 分钟）。5C（会寻路的机器人）、5D（小地图）不在本计划：5B 里机器人在标准战照旧是 `SkirmishBot`（直线开向路点、卡住换路点、打看得见的敌人）。参考：[5A 计划](2026-10-22-stage5a-riverland.md)（格式、命令、约定），[阶段 4 设计](../specs/2026-10-18-stage4-networking-design.md) §2、§5。

**计划的深度：** 和 5A 一样，不是逐字代码。每个任务写清：改哪些文件、对外接口（确切签名）、规则和算法要点、要先写的失败测试（具体输入和期望值）、命令和验收、提交说明。实现代码由执行者按 TDD 写（先写测试、确认失败、实现、确认通过）。接口一律以本文“共享接口”一节为准；实现中发现接口要改，先改本文这一节再动代码，并在跟进提交里说明。

---

## 执行方式

- **分支和目录**：`feat/stage5b-standard`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage5b`（从 main `82c0dd1` 拉出，第一个提交是本计划）。下文的相对路径都相对于它。
- **逐任务派子代理**（superpowers:subagent-driven-development）：一个任务一个提交。派之前用 `python3 /Users/murphy/Desktop/dev/BattleCity-dev-scripts/brief5b.py N` 生成 `plan5b/brief-N.md`（本文的“执行方式”“执行前须知”“共享接口”和任务 N 全文），交给实现子代理读。提交前五个项目 0 警告、四个测试项目全部通过。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照阶段 5 设计 §3、§6 的 5B 部分和本计划，列“需求 → 代码 → 测试”表），一个做代码质量审查（正确性、确定性、分配、命名、注释、线上格式的边界检查）；按两份意见做**一个**跟进提交 `fix(<scope>): batch X review follow-up — …`。
- **每批之后记进度**：在 `docs/PROGRESS.md` 新建并维护“5B 节点记录”表（放在“5A 节点记录”上面；列：# / 节点 / 状态 / 提交 / 说明；说明里写各任务提交号、跟进提交号、测试数变化、截图目录、遗留问题），同时更新“当前位置”的“进度”“下一步”和“阶段总览”里阶段 5 那一行。随跟进提交一起提交，或单独提交 `docs: progress — 5B batch X`。第 0 行“设计 + 计划”写本计划的提交号。
- **顺序**：A → B → C → D，批内按任务号。C（客户端）要用 B 的线上字段（`MatchBlock`、`PlayerStats`、`MatchStartMessage.Bases`），不并行。
- **用户试玩**在整个阶段 5（5A～5D）做完之后，5B 做完不等试玩：最终验收清单全部打勾后 squash 合入本地 main（`git checkout main && git merge --squash feat/stage5b-standard && git commit`，说明 `feat: stage 5B — standard battle: bases, capture, protocol v4`，末行 `Tests: …`），不推远端；5C 从新的 main 拉分支。合入后在 main 上再跑一遍四个测试项目和 0 警告检查。
- **只做 macOS**：不写 Windows、Linux 专门的代码路径，不打其他平台的包。
- 设计已确认，中途不问用户，做完汇报。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage5b
```

**基线截图**（`build/` 在 `.gitignore` 里，新 worktree 没有）：任务 1 的第一步先把 5A 的基线拷过来，之后所有回归截图都和它比：

```bash
mkdir -p build/shots && cp -R /Users/murphy/Desktop/dev/BattleCity-stage5a/build/shots/base build/shots/base
ls build/shots/base   # heavy kill lamp match-loopback orbit patrol wall 七张 png + orbit.log spin.log
```

**构建与测试**（这台机器上 `dotnet test` 的输出是中文，`dotnet build` 的汇总行是英文）：

```bash
# 0 警告检查：每行应为 "0 Warning(s) 0 Error(s)"（或中文 "0 个警告 0 个错误"）
for p in src/Tank.Sim/Tank.Sim.csproj src/Tank.Net/Tank.Net.csproj src/Tank.Client/Tank.Client.csproj \
         src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do
  echo "$p: $(dotnet build $p 2>&1 | grep -E '警告|错误|Warning\(s\)|Error\(s\)' | tr '\n' ' ')"; done
for t in Sim Net Art Update; do echo "$t: $(dotnet test tests/Tank.$t.Tests 2>&1 | grep -E '通过!|失败!')"; done
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~BaseCapture" 2>&1 | tail -5     # 只跑某些类
dotnet run --project tests/Tank.Soak -c Release 2>&1 | tail -20                                 # soak 四种链路
```

- 成功的行形如 `已通过! - 失败: 0，通过: 830 …`；`grep -E '通过!|失败!'` 两种都能抓到。
- **改动前的测试数**（本计划写作时在 `82c0dd1` 上实测）：Sim 830、Net 231、Art 1039、Update 42；五个项目 0 警告。每个任务的提交说明末行写测试数变化，例如 `Tests: Sim 830 → 838.`；下文各任务给的数是“至少”，以实际为准。
- 5A 留下的偶发：构建完立刻跑 Sim 偶尔有 1 条失败、重跑都过（计时测试已挪进不并行的 `Timing` 集合，可能还有别的）。遇到时重跑一次，两次都失败才算真失败，并在进度里记下是哪条。
- 客户端项目 `Tank.Client.csproj` 不把警告当错误（`TreatWarningsAsErrors=false`），“0 警告”靠上面的 grep 检查；其他项目由 `Directory.Build.props` 的 `TreatWarningsAsErrors=true` 保证。
- **Art 测试编进来的客户端代码**（`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`）：`scripts/Art/Mesh/**/*.cs`、**任何** `Logic/` 目录下的 `**/Logic/**/*.cs`、`Art/Palette.cs`、`Bootstrap/LaunchOptions.cs` 等（见 5A 计划同节）。新的引擎无关代码放进 `Art/Mesh/` 或某个 `Logic/` 子目录（命名空间沿用父目录的，例如 `Match/Logic/*.cs` 是 `Tank.Client.Match`，`Art/Mesh/*.cs` 是 `Tank.Client.Art.Meshes`），不用改 csproj；这些文件里不能 `using Godot`。Art 测试项目引用 `Tank.Sim` 和 `Tank.Net`。
- `Tank.Sim` 的 `internal` 只对 `Tank.Sim.Tests` 可见（例如 `Simulation.TankRef`，测试里用它摆车、改血、去掉保护），`Tank.Net` 的 `internal` 只对 `Tank.Net.Tests` 可见；客户端只能用 public 成员。
- `Tank.Net` 的命名空间：`Tank.Net.Protocol`（`BitReader`、`BitWriter`、`ProtocolException`、`Channel`）、`Tank.Net.Transport`、`Tank.Net.Timing`、`Tank.Net.Battle`（协议、服务器、客户端）。

**Godot**（开发脚本在 `/Users/murphy/Desktop/dev/BattleCity-dev-scripts/`，见它的 `README.md`）：

```bash
S=/Users/murphy/Desktop/dev/BattleCity-dev-scripts
W=/Users/murphy/Desktop/dev/BattleCity-stage5b
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后先编译：Godot 命令行不编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs/.gdshader 后生成 .uid，和源文件一起提交
mkdir -p build/shots/5b-tNN                                 # build/ 在 .gitignore 里
# HUD 画廊（纯色底上的预制帧）：SCENE 传空字符串
$S/godot_run.sh $W "" 30 --hud-demo standard --screenshot $W/build/shots/5b-tNN/standard.png --screenshot-at 1 --quit-after 2
# 主场景开房、自动开局（真对局，房主线程上的服务器）
$S/godot_run.sh $W "" 120 --host --mode standard --map riverland --bots 3 --autostart \
  --screenshot $W/build/shots/5b-tNN/match.png --screenshot-at 20 --quit-after 30
ls -la build/shots/5b-tNN/                                  # 成败看文件在不在（脚本只打印含 screenshot/error 的行）
# 试驾场六张回归截图（orbit、kill、lamp、wall、patrol、heavy），OUTDIR 相对于工作目录
$S/baseline4b.sh $W build/shots/5b-tNN-sandbox
python3 $S/pngdiffdirs.py build/shots/base build/shots/5b-tNN-sandbox
# 两个实例（房主 + 客户端），日志在 OUTDIR/TAG-host.log、TAG-guest.log
$S/pair4b.sh $W $W/build/shots/5b-tNN pair 200 "房主参数" "客户端参数" 3
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束都要确认没有残留的 Godot 进程
```

- 这个工作目录已经有 `.godot/`；如果字体等资源加载不到，先跑一次 `--import`。Godot 可能卡住不退出，`godot_run.sh` 会在超时秒数后强制结束；直接调 `godot-mono` 时自己套 `& P=$!; ( sleep 40; kill -9 $P ) & wait $P`。
- **截图**：路径必须是绝对路径；每张都要人工看（用 Read 打开 PNG），在进度说明里写看到了什么。凡是动了客户端或仿真的任务，都要重拍六张回归截图并和基线 `build/shots/base/` 比较：差异只允许出现在云、地平线、草叶摇动、粒子上（`pngdiffdirs.py` 给的比例以往 orbit 约 23%、heavy 约 8% 来自环绕镜头的时间差，要看图判断，不只看数）。本阶段不改试驾场，六张应和基线同量级。
- **河谷**第一次生成在 Debug 客户端里要几秒，开房后“地图生成中…”，`--autostart` 会等地图就绪再开局，超时给 90 秒以上。
- 帧率：本阶段只加两个圈和一条进度条，不单独测帧率；任务 10 测一次河谷标准战对局里的帧率作记录。

**提交**：

- 说明用英文一行，带范围前缀：`feat(sim): …`、`feat(net): …`、`feat(server): …`、`feat(client): …`、`test(sim): …`、`fix(<scope>): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。最后一行 `Tests: Sim 830 → 838.`（列出有变化的项目）。
- 作者用仓库配置（`火狐`），不加 `Co-Authored-By`，不推送远端。
- 只提交本任务的文件；`.uid` 和源文件一起提交。worktree 里有一批 4B/4C 漏交的 `.uid` 未跟踪（`git status` 里的 `?? …cs.uid`），不是本阶段的，别 `git add -A`。

---

## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Modes/TeamBase.cs` | 新建 | `TeamBase(Team, Centre, Radius)`、`StandardRadius = 25`、`TeamBase.Check`（一队一个） | 1 |
| `Maps/SandboxMap.cs`、`Maps/RiverlandMap.cs` | 修改 | 只加 `public static readonly IReadOnlyList<TeamBase> Bases`（地形、物件、出生点逐位不变） | 1 |
| `Maps/MapDefinition.cs`、`Maps/MapCatalog.cs` | 修改 | `MapDefinition` 可选参数 `bases`、`Bases` 属性；目录给两张图传基地 | 1 |
| `Modes/BaseCapture.cs` | 新建 | 占领计数：每 tick 读车和事件、按 tick 攒分、3 辆上限、守军暂停、挨打和阵亡清零、基地分数、每车最高分 | 2 |
| `Modes/BattleMode.cs` | 修改 | `Standard = 3`；`ModeRules`：`LastMode`、`All`、`Id`、`DurationTicks`（10 分钟）、`HasBases`、`JoinsMidMatch` | 3 |
| `Modes/MatchEndReason.cs` | 新建 | 结束原因 `None/TimeUp/Eliminated/Captured` | 3 |
| `Modes/MatchRules.cs` | 修改 | 构造可选 `bases`；`Capture`、`EndReason`、`CaptureBest(slot)`；标准战的判定；所有模式记结束原因 | 3 |

**联网 `src/Tank.Net`、服务器 `src/Tank.Server`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Battle/BattleProtocol.cs` | 修改 | `Version = 4` | 5 |
| `Battle/BattleSnapshot.cs` | 修改 | `MatchBlock.BaseProgress0/BaseProgress1/EndReason`、`PlayerStats.Capture`（含 `Equals`、`GetHashCode`） | 5 |
| `Battle/BattleSnapshotCodec.cs` | 修改 | `WriteMatch`/`ReadMatch`（第 217～238 行）追加三项；`WriteStats`/`ReadStats`（第 299～332 行）追加占领 | 5 |
| `Battle/BattleMessages.cs` | 修改 | `MatchStartMessage.Bases` 和它的读写（`WriteMatchStartTail` 第 224 行、`MaxMatchStartTailBytes` 第 231 行、`ReadMatchStart` 第 233 行）；`ReadMode`（第 515 行）认标准战；`ReadRoomInfo`（第 467 行）容忍别的版本的未知模式 | 5 |
| `Battle/BattleServer.Match.cs` | 修改 | 标准战开局要基地、`MatchRules` 带基地、快照填新字段、开局消息带基地、结束时打日志 | 6 |
| `src/Tank.Server/Program.cs` | 修改 | 用法字符串和注释加 `standard`（第 14、21 行） | 6 |

**客户端 `src/Tank.Client`**（路径相对 `src/Tank.Client/`）

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `scripts/UI/Logic/ModeText.cs` | 修改 | “标准战”和它的规则一句话 | 7 |
| `scripts/Bootstrap/LaunchOptions.cs` | 修改 | 用法注释（第 11 行）写 `standard`（解析本来就走 `ModeRules`） | 7 |
| `scripts/Match/Logic/HudText.cs` | 修改 | 标准战的比分板（存活）、结算的胜负原因 | 7 |
| `scripts/Match/Logic/CaptureBars.cs` | 新建（引擎无关） | `CaptureBar`、`CaptureBars`、`CaptureText`（哪几条、文字）、`CaptureAlarm`（己方基地在涨就闪） | 7 |
| `scripts/Match/Logic/MatchHudFrame.cs` | 修改 | `ResultsInfo.Reason`、`MatchHudFrame.Capture` | 7 |
| `scripts/Match/Logic/ScoreboardModel.cs` | 修改 | `ScoreRow.Capture` | 7 |
| `scripts/Match/Logic/HudLayout.cs` | 修改 | 进度条的位置 `CaptureBars`、`CaptureRow(i)` | 8 |
| `scripts/Match/CaptureBarsView.cs` | 新建 | 比分板下的进度条（己方基地被占红色、在涨时闪） | 8 |
| `scripts/Match/MatchHud.cs`、`Match/ScoreboardView.cs`、`Match/CenterBanner.cs` | 修改 | 挂进度条；计分板“占领”列（只在标准战）；结算按原因 | 8 |
| `scripts/Match/Logic/HudDemoFrames.cs` | 修改 | 画廊 `standard`、`capture` | 8 |
| `scripts/Art/Mesh/RingMesh.cs` | 新建（引擎无关） | 贴地的圆环网格 | 9 |
| `shaders/base_ring.gdshader` | 新建 | 圈的颜色、向内渐隐的抖动透明边 | 9 |
| `scripts/World/BaseRings.cs` | 新建 | 两个基地的圈（己方蓝灰、对方红） | 9 |
| `scripts/Match/MatchMain.cs` | 修改 | 标准战建圈 | 9 |
| `scripts/Sandbox/Logic/MapSites.cs` | 修改 | 两张图都加 `base0`、`base1` 机位（截图用） | 9 |

**测试**

| 项目 | 新文件 | 任务 |
|---|---|---|
| `tests/Tank.Sim.Tests` | `MapBaseTests` | 1 |
| | `BaseCaptureTests` | 2 |
| | `StandardRulesTests`（并改 `MatchRulesTests` 的模式清单和中途加入两条） | 3 |
| | `StandardMatchTests`（两张真图上的整局） | 4 |
| `tests/Tank.Net.Tests/Battle` | `StandardWireTests`（并改 `BattleProtocolTests` 第 329 行的版本断言） | 5 |
| | `StandardServerTests`、`TestMaps.Standard`/`StandardWalled` | 6 |
| `tests/Tank.Art.Tests` | `StandardHudTextTests`、`CaptureBarsTests`（并扩充 `ModeTextTests`、`LaunchOptionsTests`、`RoomModelTests`、`ScoreboardModelTests`、`MatchHudFrameTests`） | 7 |
| | 扩充 `HudLayoutTests`、`HudDemoFramesTests` | 8 |
| | `RingMeshTests`（并扩充 `MapSitesTests`） | 9 |

**开发工具**（仓库外，`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe/`）：写计划时已加了 `Bases.cs` 和 `Program.cs` 里的 `bases` 命令（见任务 1）。

---

## 共享接口

所有任务都按这里的名字和签名写。改接口先改这一节。

### 必须保持源码兼容的现有签名

下面这些有现成的调用方（已 grep 核对），只能**追加可选参数、新字段或新重载**，不能改已有参数：

| 签名 | 调用方 |
|---|---|
| `MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null)` | `BattleServer.Match.TryStartMatch`（第 61 行）、`MatchRulesTests`、Net 测试 |
| `MapDefinition(string id, string name, Func<Battlefield> create, SpawnLayout spawns, IReadOnlyList<Vector2> waypoints)` | `MapCatalog`、`TestMaps.Small/Gated/Broken/Mixed`、`MapCatalogTests`、`RoomMapModelTests` |
| `ModeRules.All`、`Id`、`TryParse`、`HasTeams`、`Respawns`、`JoinsMidMatch`、`DurationTicks` | 服务器、`Tank.Server`、`LaunchOptions`、`RoomScreen`（下拉表）、`ModeTextTests`、`RoomRulesTests`、`MatchRulesTests` |
| `struct MatchBlock`、`struct PlayerStats`（对象初始化器写法） | 服务器 `Build`、编解码、`HudDemoFrames`、`BattleProtocolTests`、Art 测试 |
| `MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData? Map, IReadOnlyList<PropState> Props, int MyTankId)` | 服务器、`BattleClient.Match`、`BattleProtocolTests`、`MapWireV3Tests`、`RiverlandMatchTests` |
| `ResultsInfo(byte WinnerTeam, byte WinnerSlot, int TicksLeft, bool IsHost)` | `MatchHudFrame.From`、`HudDemoFrames`、Art 测试 |
| `MatchHudFrame(...)` 的 14 个位置参数 | `MatchHudFrame.From`、`HudDemoFrames.Frame` |
| `ScoreRow(byte Slot, string Name, byte Team, string TankName, int Kills, int Deaths, bool Alive, int Rank, Side Side, string Status)` | `ScoreboardModel`、`ScoreboardView`、`ScoreboardModelTests` |
| `HudText.Results(BattleMode, byte, byte, in ResultsInfo, in PlateValues, ReadOnlySpan<PlayerStats>, string?[])`、`HudText.Plate(...)` | `CenterBanner`、`HudDemoFramesTests`、`HudTextTests` |
| `HudBoxes`（`HudLayout.For` 构造） | `HudPanel.Boxes` 的所有视图、`HudLayoutTests` |

### 仿真：基地和占领（任务 1、2）

```csharp
namespace Tank.Sim.Modes;

/// <summary>One team's base (stage 5 design §3.2): a circle on the ground the other team captures. X is world X, Y world Z.</summary>
public readonly record struct TeamBase(byte Team, Vector2 Centre, float Radius)
{
    public const float StandardRadius = 25f;

    /// <summary>
    /// Null or empty: no bases. Otherwise exactly two, one for team 0 and one for team 1 (any order), each with a finite
    /// centre and a radius in (0, 100]; ArgumentException otherwise. Returns the list (empty for null).
    /// </summary>
    public static IReadOnlyList<TeamBase> Check(IReadOnlyList<TeamBase>? bases, string paramName);
}

// SandboxMap / RiverlandMap: one new member each, nothing else changes (SandboxFingerprintTests, RiverlandFingerprintTests stay green)
public static readonly IReadOnlyList<TeamBase> Bases;   // sandbox: (0, (40, 306)), (1, (300, 56)); riverland: (0, (152, 398)), (1, (1024, 852)); radius StandardRadius

// MapDefinition: one optional parameter and one property
public MapDefinition(string id, string name, Func<Battlefield> create, SpawnLayout spawns, IReadOnlyList<Vector2> waypoints,
    IReadOnlyList<TeamBase>? bases = null);
public IReadOnlyList<TeamBase> Bases { get; }   // TeamBase.Check(bases): empty, or one per team

/// <summary>
/// Base capture (stage 5 design §3.2), in whole ticks so it is exact and the same everywhere: a tank earns 1 tick per
/// tick it counts, a point is 30 ticks, a base falls at 3000. Keyed by tank id; allocation-free after construction.
/// </summary>
public sealed class BaseCapture
{
    public const int TicksPerPoint = SimConstants.TickRate;          // 30: one point a second
    public const int PointsToWin = 100;
    public const int TicksToWin = PointsToWin * TicksPerPoint;       // 3000
    public const int MaxCounted = 3;                                 // attackers that count at once, lowest ids first

    public BaseCapture(IReadOnlyList<TeamBase> bases);               // TeamBase.Check, and not empty (ArgumentException)
    public IReadOnlyList<TeamBase> Bases { get; }

    /// <summary>
    /// One tick, after <c>Simulation.Step</c>: <paramref name="tanks"/> is <c>Simulation.Tanks</c>, <paramref name="events"/>
    /// <c>Simulation.Events</c>. See the rules in task 2.
    /// </summary>
    public void Update(ReadOnlySpan<TankState> tanks, ReadOnlySpan<GameEvent> events);

    public int TicksOn(byte baseTeam);        // the attackers' ticks on that team's base: the sum over the other team's tanks still in the battle
    public byte Progress(byte baseTeam);      // min(100, TicksOn / TicksPerPoint): what the wire and the HUD show
    public bool IsCaptured(byte baseTeam);    // TicksOn ≥ TicksToWin
    public int TicksOf(int tankId);           // the tank's own ticks; 0 for a tank not in the battle
    public byte BestOf(int tankId);           // the most whole points it held at once this match (0–100); 0 for an unknown tank
}
```

### 仿真：模式和规则（任务 3）

```csharp
namespace Tank.Sim.Modes;

public enum BattleMode : byte { TeamBattle = 0, Annihilation = 1, FreeForAll = 2, Standard = 3 }   // values on the wire: append only

public static class ModeRules
{
    public const BattleMode LastMode = BattleMode.Standard;           // the wire readers' upper bound
    public static readonly IReadOnlyList<BattleMode> All;             // TeamBattle, Annihilation, FreeForAll, Standard (the room's list in this order)
    public static bool HasBases(BattleMode mode) => mode == BattleMode.Standard;
    public static bool JoinsMidMatch(BattleMode mode) => mode is BattleMode.TeamBattle or BattleMode.Standard;
    // HasTeams: Standard true; Respawns: Standard false; DurationTicks(Standard) = 10 × 60 × 30; Id(Standard) = "standard"
}

/// <summary>Why a match ended. Values on the wire (2 bits): append only, at most 3.</summary>
public enum MatchEndReason : byte { None = 0, TimeUp = 1, Eliminated = 2, Captured = 3 }

public sealed class MatchRules
{
    /// <param name="bases">The map's bases: required (TeamBase.Check, not empty) in a standard match, ignored in the others.</param>
    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null, IReadOnlyList<TeamBase>? bases = null);
    public BaseCapture? Capture { get; }                 // non-null exactly in a standard match
    public MatchEndReason EndReason { get; }             // None while playing; set with Phase = Ended in every mode
    public byte CaptureBest(byte slot);                  // Capture?.BestOf(the slot's tank) ?? 0
}
```

### 联网（任务 5、6）

```csharp
// Tank.Net.Battle.BattleProtocol
public const int Version = 4;   // 4: standard battle — the match block carries the bases' progress and the end reason, the
                                // scoreboard each player's capture, the match start the bases. ConnectKey stays "tank3d-v2".

// Tank.Net.Battle.MatchBlock: three new fields (Equals and GetHashCode take them in)
public byte BaseProgress0;           // how far team 1 has taken team 0's base, 0–100 (0 outside a standard match)
public byte BaseProgress1;           // how far team 0 has taken team 1's base
public MatchEndReason EndReason;     // the rules' EndReason
// wire, after TeamScore1: u8 BaseProgress0 · u8 BaseProgress1 · 2 bits EndReason; read: a progress over 100 → ProtocolException

// Tank.Net.Battle.PlayerStats: one new field
public byte Capture;                 // MatchRules.CaptureBest(slot): the most points the player's tank held this match, 0–100
// wire, after Rank: 7 bits; read: over 100 → ProtocolException

// Tank.Net.Battle.MatchStartMessage: one optional parameter
public sealed record MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData? Map,
    IReadOnlyList<PropState> Props, int MyTankId, IReadOnlyList<TeamBase>? Bases = null);   // null or empty: no bases
// wire, after MyTankId: u8 count (0 or 2) · count × { u8 team, f32 x, f32 z, f32 radius }
// read: count ≤ MaxBases; TeamBase.Check's rules (teams 0 and 1 once each, finite, 0 < radius ≤ 100) and |x|, |z| ≤
//       MaxCoordinateCm × PositionUnit, else ProtocolException; Bases is never null after reading (an empty array)

// Tank.Net.Battle.BattleMessages
public const int MaxBases = 2;
// ReadMode: mode ≤ (byte)ModeRules.LastMode
// ReadRoomInfo: an unknown mode byte is refused only when the room's version is ours; a room of another version keeps the
//               raw value ((BattleMode)byte), so the LAN list still shows it as 版本不符 instead of dropping it

// Tank.Net.Battle.BattleServer
// TryStartMatch, standard on a map with no bases → false, reason "the map has no bases" (before the bots are seated)
// the phase turning Ended → one log line: "match ended: {ModeRules.Id(mode)}, {team 0|team 1|slot N|draw}, {time up|eliminated|captured}"
```

### 客户端（任务 7～9）

```csharp
namespace Tank.Client.UI;
public static class ModeText   // Name(Standard) == "标准战"; Rule(Standard) see task 7

namespace Tank.Client.Match;   // Match/Logic/CaptureBars.cs, engine-free

/// <summary>One progress bar under the plate: whose base, and its points (1–100).</summary>
public readonly record struct CaptureBar(bool OwnBase, byte Points);

/// <summary>The bars to show, own base first: none outside a standard match, and only bases with points.</summary>
public readonly record struct CaptureBars(int Count, CaptureBar First, CaptureBar Second)
{
    public static readonly CaptureBars None = default;
    public CaptureBar this[int index] { get; }   // 0 → First, 1 → Second; ArgumentOutOfRangeException at or beyond Count
}

public static class CaptureText
{
    public static CaptureBars Bars(BattleMode mode, byte ownTeam, in MatchBlock match);   // allocation-free
    public static string Label(in CaptureBar bar);   // "我方基地 37" / "敌方基地 62" (invariant culture)
}

/// <summary>Whether the own base is being taken right now: its points rose within <see cref="HoldSeconds"/>.</summary>
public sealed class CaptureAlarm
{
    public const double HoldSeconds = 1.5;
    public const double BlinkHz = 2.0;
    public bool Update(byte ownBasePoints, double now);   // true while rising; a drop to a lower value or 0 clears it at once; the first call only sets the baseline
    public bool Update(in CaptureBars bars, double now);  // the own base's bar's points, 0 without one (batch C follow-up)
    public static bool IsDarkHalf(double now);            // the blink's dark half; bright at 0
}

// Match/Logic/MatchColors.cs (batch C follow-up)
public enum CaptureFill : byte { Danger, DangerDim, Ally }
public static Vector3 BaseRing(byte ownTeam, byte baseTeam);          // own base Palette.AllyBody, the other Palette.EnemyBody
public static CaptureFill FillOf(in CaptureBar bar, bool blinkDark);  // own: Danger / DangerDim; enemy: always Ally
public static Vector3 FillColor(CaptureFill fill);                    // Danger(Dim) Palette.UiDanger, Ally MatchColors.Ui(Side.Ally)

// Match/Logic/MatchHudFrame.cs
public readonly record struct ResultsInfo(byte WinnerTeam, byte WinnerSlot, int TicksLeft, bool IsHost,
    MatchEndReason Reason = MatchEndReason.None);
public readonly record struct MatchHudFrame(/* the 14 parameters as they are */, CaptureBars Capture = default);
// MatchHudFrame.From fills Capture = CaptureText.Bars(mode, ownTeam, match) and Results.Reason = match.EndReason

// Match/Logic/ScoreboardModel.cs
public readonly record struct ScoreRow(/* the 10 parameters as they are */, int Capture = 0);   // PlayerStats.Capture

// Match/Logic/HudLayout.cs
public const float CaptureTop = 124f, CaptureBarHeight = 26f, CaptureBarGap = 6f;   // under the mode label (it ends at 116)
// HudBoxes: one new last member `HudRect CaptureBars` (PlateWidth wide, centred, two rows and the gap tall) and
public HudRect CaptureRow(int index);   // row 0 or 1 of it

// Art/Mesh/RingMesh.cs (namespace Tank.Client.Art.Meshes), engine-free
public static class RingMesh
{
    /// <summary>
    /// A flat annulus draped on the ground: <paramref name="segments"/> quads around, each vertex at heightAt(x, z) +
    /// <paramref name="lift"/> with its normal up; UV.x 0 on the inner edge, 1 on the outer; UV.y the turn (0…1).
    /// </summary>
    public static MeshData Build(System.Numerics.Vector2 centre, float inner, float outer, int segments,
        Func<float, float, float> heightAt, float lift);
}

// World/BaseRings.cs (Godot)
public partial class BaseRings : Node3D
{
    public const float BandWidth = 1.8f, OuterOver = 0.2f, Lift = 0.15f;   // the band runs from radius − 1.6 to radius + 0.2
    public const int Segments = 160;
    public void Build(IReadOnlyList<TeamBase> bases, byte ownTeam, HeightField terrain);   // own base AllyBody, the other EnemyBody
}
```

---

## 任务总览

| 批 | 任务 | 内容 | 依赖 |
|---|---|---|---|
| A 仿真 | 1 | 基地：`TeamBase`、两张图的基地常数（probe 找、测试查）、`MapDefinition.Bases` | — |
| | 2 | `BaseCapture`：占领计数的全部规则 | 1 |
| | 3 | `BattleMode.Standard`、`ModeRules`、`MatchEndReason`、`MatchRules` 的标准战判定 | 2 |
| | 4 | 两张真图上的无画面整局：全灭、占领、超时；机器人跑标准战不出错、不分配 | 3 |
| B 联网 | 5 | 协议 v4：对局块、计分板、开局消息的基地；模式上限 | 3 |
| | 6 | 服务器跑标准战：开局要基地、快照字段、开局带基地、中途加入、结束日志；`Tank.Server --mode standard` | 5 |
| C 客户端 | 7 | 引擎无关的文字和数据：模式名和规则、比分板、结算原因、进度条模型、闪烁、计分板“占领” | 6 |
| | 8 | HUD 视图：进度条、计分板“占领”列、结算；画廊 `standard`、`capture` | 7 |
| | 9 | 地上的圈：`RingMesh`、着色器、`BaseRings`、对局里建圈；`base0`/`base1` 机位 | 7 |
| D 收尾 | 10 | 验收：测试、soak、截图、两实例在两张图上各打一局标准战、专用服务器 | 8、9 |
| | 11 | 文档：`docs/01～04`、README、进度 | 10 |

---

## 批次 A：仿真

### Task 1：两张图的基地

**Files:**
- Create: `src/Tank.Sim/Modes/TeamBase.cs`
- Modify: `src/Tank.Sim/Maps/SandboxMap.cs`（在 `Spawns` 旁加 `Bases`）、`src/Tank.Sim/Maps/RiverlandMap.cs`（在 `Spawns` 旁加 `Bases`）、`src/Tank.Sim/Maps/MapDefinition.cs`（构造可选参数、`Bases`）、`src/Tank.Sim/Maps/MapCatalog.cs`（传 `SandboxMap.Bases`、`RiverlandMap.Bases`）
- Test: `tests/Tank.Sim.Tests/MapBaseTests.cs`；`MapCatalogTests` 加一条
- 开发工具（仓库外）：`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe/Bases.cs`（写计划时已建）、`README.md` 里 `bases` 命令的说明（都已建好）

**接口**：共享接口“仿真：基地和占领”的 `TeamBase`、`Bases`、`MapDefinition`。

**规则/要点**（阶段 5 设计 §3.2）

- **位置已经用 probe 找好**（写计划时在 `82c0dd1` 上跑的，生成结果和 5A 指纹相同），直接写成常数：

  | 图 | 队 0 基地 | 队 1 基地 | 离本队最近出生点 | 偏离正后方 | 圈里外环的岩石 | 最陡 | 两基地相距 |
  |---|---|---|---|---|---|---|---|
  | 河谷 | (152, 398) | (1024, 852) | 45.7 m / 46.7 m | 54° / 13° | 3 / 1 | 3.5° / 6.7° | 983 m |
  | 试驾场地图 | (40, 306) | (300, 56) | 50.8 m / 44.0 m | 42° / 45° | 1 / 1 | 5.2° / 4.3° | 361 m |

  5A 的出生点不动（两队中心相距 899 m），所以河谷两基地约 1000 m，不是设计的约 750 m（见“与设计的偏差”1）。河谷队 0 的出生点在台地南崖脚下，正后方（西北）是台地崖和岩石带，40～80 m 内没有不带岩石的平地圆，选的是出生点西南的草地；队 1 在出生点正后方偏 13°。试驾场地图 88 块岩石散得很匀，出生点外 40～80 m 内没有完全没有岩石的 27 m 圆。
- **复查命令**：`cd $S/riverland-probe && dotnet run -c Release -p:Worktree=$W -- bases riverland 152 398 1024 852`（试驾场把 `riverland` 换成 `sandbox` 和对应坐标）按测试的规则逐条报 `ok`；`bases MAP 2` 按带余量的规则（坡度 7°、各项净空多 2 m、离出生点 44～76 m）重新搜索、每队按“岩石数、偏离角、离 60 m 的差”排序打印前几名；`bases MAP ascii X1 Z1 X2 Z2` 打 4 m 一格的字符图（R 岩石、H 房子、W 墙栅栏、T 树、b 草丛、~ 水、/ 坡 > 8°、0/1 出生点）。生成有变化（指纹测试会先失败）时用它重找。
- **测试的规则**（`MapBaseTests` 对两张图都查，`Field = map.NewBattlefield()`）：
  - 两个基地，一队一个，半径 `StandardRadius`；
  - 离本队最近的出生点 40～80 m；`(基地 − 本队出生点中心)` 和 `(本队中心 − 敌队中心)` 的夹角 ≤ 60°；基地离敌队出生点中心比本队出生点中心远；
  - 平地：圆心 27 m（R + 2）内每 2 m 一个采样点，`Terrain.NormalAt` 坡度 ≤ 8°、`WaterDepthAt == 0`；圆心离图边（可玩区）≥ `Border + R + 2`；
  - 空旷：岩石、房子、墙、栅栏的占地离圆心 ≥ 12 m；房子、墙、栅栏的占地离圆心 ≥ R + 2；树干（`Tree` 的中心）离圆心 ≥ R + 2；草丛的圆和基地的圆不相交（圆心距 ≥ R + 草丛半径）；和圆（R + 2）相交的岩石至多 4 块；
  - 连通：`new Passability(field, 32°, 1.6 m)` 下圆心和两队全部出生点在同一个连通块；
  - 河谷两基地相距 950～1020 m，试驾场地图 330～400 m（记下实测）。
- `TeamBase.Check`：null 或空 → 空数组；否则必须正好两个、队号 0 和 1 各一个、中心有限、半径在 (0, 100]，否则 `ArgumentException`（参数名用调用方给的）。`MapDefinition` 构造时调用；`Bases` 存检查后的列表。
- 两张图的 `Bases` 都放在出生点数组之后（静态初始化按顺序）；不改 `Create()`，两张图的指纹测试不变。

**先写的测试**

- `MapBaseTests`（`[Theory]` 按 `"sandbox"`、`"riverland"` 取 `MapCatalog.Get(id)`；河谷用 `RiverlandFixture` 的场地，不重复生成）：
  1. `Each_team_has_one_base_of_radius_25`
  2. `Each_base_lies_40_to_80_m_behind_its_teams_spawns`
  3. `Each_base_is_flat_dry_ground_inside_the_playable_area`
  4. `Each_base_is_clear_of_houses_walls_trees_and_bushes_with_at_most_four_rocks_at_its_rim`
  5. `Each_base_is_reachable_from_both_teams_spawns`
  6. `The_bases_are_as_far_apart_as_measured`：河谷 950～1020 m，试驾场地图 330～400 m。
  7. `Team_base_check_rejects_one_team_twice_a_third_base_and_bad_radii`：`[(0,…),(0,…)]`、三个基地、只有一个、半径 0、半径 −1、半径 101、中心 NaN 都抛 `ArgumentException`；null 和空返回空列表。
- `MapCatalogTests.The_catalog_maps_carry_their_bases`：`MapCatalog.Sandbox.Bases` 引用等于 `SandboxMap.Bases`，河谷同理；`TestMaps` 式的自定义 `MapDefinition`（不给 `bases`）的 `Bases` 为空。

**命令**

```bash
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~MapBase|FullyQualifiedName~MapCatalog|FullyQualifiedName~Fingerprint" 2>&1 | grep -E '通过!|失败!'
cd /Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe && dotnet run -c Release -p:Worktree=$W -- bases riverland 152 398 1024 852 && dotnet run -c Release -p:Worktree=$W -- bases sandbox 40 306 300 56
```

**验收**

- [ ] 第一步已拷好基线截图目录 `build/shots/base/`。
- [ ] 全部测试通过、0 警告；两张图的指纹测试不变；Sim 至少 +14（约 844；`[Theory]` 每个参数算一条）。
- [ ] 进度里记下两张图的基地坐标、实测间距、每个基地外环的岩石数。

**步骤**

- [ ] 拷基线截图目录（见“执行前须知”）
- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完
- [ ] 提交

**提交**：`feat(sim): team bases on both maps — open flat ground behind each team's spawns, found with the probe and checked by tests`，末行 `Tests: Sim 830 → 844.`

### Task 2：占领计数 `BaseCapture`

**Files:**
- Create: `src/Tank.Sim/Modes/BaseCapture.cs`
- Test: `tests/Tank.Sim.Tests/BaseCaptureTests.cs`

**接口**：共享接口“仿真：基地和占领”的 `BaseCapture`。

**规则/要点**（阶段 5 设计 §3.2，逐条确定）

- **整数**：每辆车的分按 tick 记（`int`），1 分 = 30 tick；基地分数 = 进攻方各车 tick 之和，≥ 3000 tick（100 分）算占领。显示和线上取 `min(100, 和 / 30)`（向下取整，所以 100 只在真正占下时出现）。单车的 tick 封顶 `TicksToWin`（防溢出；一辆车到 3000 时基地已经占下）。
- **记录**：内部固定容量 `Simulation.MaxTanks` 的三组数组（车号、tick、最高分），按车号线性找；构造时分配，`Update` 不分配（有测试）。
- **`Update(tanks, events)` 的顺序**：
  1. **对齐名单**：`tanks` 里没有的车（玩家离开、被移出仿真）连同它的分和最高分一起去掉；新出现的车 tick 0。
  2. **清零**：`events` 里 `TankHit` 或 `SplashDamage` 且 `Damage > 0` 的 `TargetTankId`、`TankDestroyed` 的 `TargetTankId`，tick 清 0，并记为“本 tick 不攒”。伤害来源不限（敌人、队友、溅射）；保护期内挨打记的是 0 伤害，不清零。
  3. **攒分**，对每个基地（队 `T`，进攻方是另一队 `A = 1 − T`）：
     - 车体中心在圈里：`Vector2.DistanceSquared(tank.Position, centre) <= radius * radius`（`TankState.Position` 就是车体中心，和 `TankMotion.Footprint` 的中心相同）；
     - 圈里有任何活着的（`!IsDestroyed`）`T` 队车 → 这个基地本 tick 谁都不攒（已攒的保留）；
     - 否则圈里活着的 `A` 队车、除去本 tick 被清零的，按**车号从小到大**取前 `MaxCounted` 辆（不依赖槽位顺序，自己选最小的三个），每辆 tick + 1（封顶）；
     - 两个基地互不影响；只有车号的队是 0 或 1 的车参与（标准战只有两队）。
  4. **最高分**：每辆车 `best = max(best, tick / 30)`。
- `TicksOn(T)` = 当前名单里 `A` 队所有车（在圈外的、已毁的都算，已毁的是 0）tick 之和——车离圈保留分，所以基地分数里也保留；挨打或阵亡那辆清零，基地分数随之下降。
- `TicksOn`、`Progress`、`IsCaptured` 的参数不是 0 或 1 时抛 `ArgumentOutOfRangeException`。
- 不读 `Simulation`，只读传进来的两段 span：单元测试直接造 `TankState`（`Id`、`Team`、`Position`、`IsDestroyed`）和 `GameEvent`。

**先写的测试**（`BaseCaptureTests`；基地 `T0 = (0, (100, 100), 25)`、`T1 = (1, (300, 100), 25)`；“进攻车”指队 1 的车在 T0 里，除非另说）

1. `A_lone_attacker_earns_a_point_a_second`：队 1 车 id 5 在 (110, 100)：29 次 `Update` 后 `TicksOf(5) == 29`、`Progress(0) == 0`；第 30 次后 30、1；`Progress(1) == 0`。
2. `At_most_three_attackers_count_and_the_lowest_ids_are_chosen`：队 1 的 5 辆车 id 8、2、10、4、6（span 里就按这个顺序）都在圈里：30 次后 id 2、4、6 各 30，id 8、10 为 0，`Progress(0) == 3`；把 id 4 移出圈，再 30 次：2、6、8 各多 30（8 补上），4 保留 30、10 仍 0。
3. `The_hull_centre_decides_who_is_inside`：车在 (125, 100)（正好 25 m）算在圈里；(125.01, 100) 不算。
4. `A_live_defender_inside_pauses_every_attacker_and_a_wreck_does_not`：两辆进攻车 60 次 → 各 60；队 0 车进圈，再 60 次 → 仍各 60；这辆守军 `IsDestroyed = true`（还在圈里），再 30 次 → 各 90。守军在圈外不影响。
5. `Damage_resets_only_the_tank_that_took_it`：进攻车 id 3、4 各 90 后，这一 tick 的事件有 `TankHit`（目标 3，`Damage` 120）：`TicksOf(3) == 0`、`TicksOf(4) == 91`；`SplashDamage`（目标 4，`Damage` 15）同样只清 4。
6. `A_hit_for_no_damage_resets_nothing`：`TankHit` 目标 3、`Damage` 0（保护期）→ 3 照常 +1。
7. `Damage_from_a_team_mate_resets_too`：`TankHit` 的 `TankId` 是同队车、目标 3、`Damage` 50 → 3 清零。
8. `Death_resets_and_a_wreck_never_earns`：`TankDestroyed` 目标 3 → 0；之后 3 `IsDestroyed`，在圈里 60 次仍 0，`BestOf(3)` 保留之前的。
9. `Leaving_keeps_the_points_and_damage_outside_still_resets`：60 次后移出圈，再 60 次 → 60、`Progress(0) == 2`；圈外挨打 → 0、`Progress(0) == 0`；回圈 30 次 → 30。
10. `The_base_falls_at_one_hundred_points_and_not_a_tick_sooner`：3 辆进攻车：999 次后 `TicksOn(0) == 2997`、`Progress(0) == 99`、`!IsCaptured(0)`；第 1000 次后 `IsCaptured(0)`、`Progress(0) == 100`。
11. `Attackers_outside_still_count_towards_the_base`：两辆车各 600 后一辆出圈；`TicksOn(0)` 仍 1200；出圈那辆被移出 span（离开对局）后 `TicksOn(0) == 600`、`TicksOf(那辆) == 0`。
12. `Each_team_earns_only_on_the_other_teams_base`：队 0 车在 T0 里不攒（它是守军）；队 0 车在 T1 里攒 `Progress(1)`；同时队 1 车在 T0 里攒 `Progress(0)`，两边独立。
13. `Best_keeps_the_most_whole_points_held_at_once`：90 次（3 分）→ 挨打 → 30 次（1 分）：`BestOf == 3`、`TicksOf == 30`；未知车号 `BestOf == 0`、`TicksOf == 0`。
14. `A_tanks_ticks_stop_at_the_win`：一辆车 3100 次 → `TicksOf == 3000`。
15. `Bad_arguments_are_refused`：空基地表、两个队 0 的基地 → `ArgumentException`；`Progress(2)` → `ArgumentOutOfRangeException`。
16. `Updating_allocates_nothing`：6 辆车（各队 3 辆、进出圈、带挨打事件）预热 100 次后再 1000 次，`GC.GetAllocatedBytesForCurrentThread()` 差 0。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~BaseCapture" 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告；Sim 至少 +16（约 860）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完
- [ ] 提交

**提交**：`feat(sim): base capture in whole ticks — three lowest-id attackers, defenders pause, any damage or death resets the tank's points`，末行 `Tests: Sim 844 → 860.`

### Task 3：`BattleMode.Standard` 和 `MatchRules` 的标准战

**Files:**
- Create: `src/Tank.Sim/Modes/MatchEndReason.cs`
- Modify: `src/Tank.Sim/Modes/BattleMode.cs`（`Standard`、`ModeRules` 的六处）、`src/Tank.Sim/Modes/MatchRules.cs`（构造第 47 行、`Update` 第 123 行、`Judge` 第 162 行、`End` 第 187 行、`EndSolo` 第 198 行；新属性）
- Test: `tests/Tank.Sim.Tests/StandardRulesTests.cs`；改 `MatchRulesTests.Only_a_team_battle_takes_players_in_mid_match`（第 330 行，改名 `Team_battle_and_standard_take_players_in_mid_match`，标准战也能中途加入）和 `The_mode_constants_are_the_designs_numbers`（第 439 行，加标准战 10 分钟）

**接口**：共享接口“仿真：模式和规则”。

**规则/要点**（阶段 5 设计 §3.1）

- `ModeRules`：`StandardMinutes = 10`；`Id(Standard) == "standard"`；`All` 追加在最后（房间下拉表“团队对战 / 团队歼灭 / 各自为战 / 标准战”）；`HasTeams(Standard)`、`!Respawns(Standard)`、`JoinsMidMatch(Standard)`、`HasBases(Standard)`。枚举注释改成“四种”，`TeamBattle` 的注释保留 7 分钟。
- `MatchRules` 构造：`ModeRules.HasBases(mode)` 时 `TeamBase.Check(bases)` 后必须不空（否则 `ArgumentException`，参数名 `bases`），建 `Capture = new BaseCapture(bases)`；其他模式忽略 `bases`、`Capture` 为 null。
- `Update()`：`OnDestroyed` 照旧 → `Capture?.Update(_sim.Tanks, _sim.Events)` → 复活（标准战不复活）→ `Judge()`。
- **标准战的判定**（`Judge` 新的一支）：
  - `won0 = AliveCount(1) == 0 || Capture.IsCaptured(1)`；`won1 = AliveCount(0) == 0 || Capture.IsCaptured(0)`；
  - 两个都真 → 平局 `End(NoTeam, reason)`；只有一个 → 那队赢；都假且到时 → 平局 `TimeUp`；
  - 原因：赢的一方（或平局时任一方）是靠占领满足的 → `Captured`，否则 `Eliminated`。推理：一队占下对方基地要有自己的车活着攒分（阵亡清零），所以“甲队占领”和“甲队全灭”不会同 tick 出现；平局只有“双方同时全灭”（`Eliminated`）和“双方同时占领”（`Captured`）两种，不会混着；同一队既占领又全歼对方时按 `Captured`。
- **所有模式都记结束原因**（`End`、`EndSolo` 带原因参数）：团队对战到时 `TimeUp`；团队歼灭全灭 `Eliminated`、到时 `TimeUp`；各自为战剩一辆或没人 `Eliminated`、到时 `TimeUp`。只有标准战的界面用它，但线上一并带上。
- `CaptureBest(slot)`：`Capture?.BestOf(_players[slot].TankId) ?? 0`（离开的人 `Active` 假，服务器不发这一行）。
- 中途加入：`AcceptsJoins` 本来就按 `JoinsMidMatch` 判断，标准战进来的是一辆新车（受保护 5 秒），进哪队由调用方（服务器的 `ThinnerTeam`）决定；不复活不变。
- 分配：`Updating_the_rules_and_the_bots_allocates_nothing_even_as_players_come_and_go`（第 452 行）扩到标准战（`[Theory]` 或循环四种模式）。

**先写的测试**（`StandardRulesTests`；照 `MatchRulesTests` 的写法：平地 `TestWorld.FlatField()`、同一份 `Layout`（队 0 在 z 150 朝北、队 1 在 z 100 朝南），基地 `Bases = [(0, (100, 180), 25), (1, (100, 70), 25)]`（各在本队出生线后方 30 m，出生点都在圈外；平地 200 m 见方），测试里用 `Sim.TankRef(id)` 摆车、`ProtectedUntil = 0` 去掉保护）

1. `The_standard_mode_is_the_designs`：`(byte)BattleMode.Standard == 3`；`ModeRules.All` 依次四种、`Id` 依次 `teambattle annihilation ffa standard`；`TryParse("STANDARD")` 真；`DurationTicks(Standard) == 18000`；`HasTeams`、`!Respawns`、`JoinsMidMatch`、`HasBases` 只对标准战真；`LastMode == Standard`。
2. `A_standard_match_needs_both_bases_and_the_other_modes_ignore_them`：`new MatchRules(Standard, sim, Layout)` 和只给一个基地都抛 `ArgumentException`；团队对战给不给基地都行，`Capture` 为 null。
3. `Wiping_out_the_other_team_wins_with_the_reason`：队 0 两辆、队 1 一辆；把队 1 那辆 `Weaken` 到 1 血、队 0 一辆 `Attack` 它：结束、`WinnerTeam == 0`、`EndReason == Eliminated`、`EndedAt` 等于 `TankDestroyed` 事件的 tick。
4. `Capturing_the_enemy_base_wins_on_the_exact_tick`：队 1 三辆摆进队 0 基地（离圆心 ≤ 10 m、彼此 ≥ 8 m）、队 0 一辆留在出生点（圈外）：从摆好后的第一 tick 起第 1000 次 `Update` 结束，`WinnerTeam == 1`、`EndReason == Captured`、`Capture.Progress(0) == 100`；第 999 次后还在 `Playing`。
5. `A_defender_in_the_base_holds_the_capture`：同上，但队 0 那辆也在圈里：1100 次后仍 `Playing`、`Progress(0) == 0`。
6. `Both_bases_falling_on_one_tick_is_a_draw`：队 0 一辆在队 1 基地、队 1 一辆在队 0 基地、都没有守军：第 3000 次结束，`WinnerTeam == NoTeam`、`EndReason == Captured`。
7. `Both_teams_wiped_out_on_one_tick_is_a_draw`：一对一、各 1 血、相距 40 m 面对面、同一 tick 互射（两边同车型、同时装填好、同时 `Fire`，平地对称，炮弹同一 tick 命中）：两个 `TankDestroyed` 同 tick，`WinnerTeam == NoTeam`、`EndReason == Eliminated`。（偶有散布打偏：固定种子下选一个两发都中的距离；测试先断言两个击毁事件同 tick，再断言平局。）
8. `Time_up_is_a_draw`：`durationTicks: 300`，一对一不动：第 300 tick 结束、`NoTeam`、`TimeUp`。
9. `A_player_may_join_a_running_standard_match_as_a_new_tank`：一对一跑 5 tick 后 `AcceptsJoins` 真，`Join(2, spec, team: 1)` 成功、新车满血受保护，`AliveCount(1) == 2`；队 1 原来那辆阵亡后比赛不结束（新车还活着）。
10. `Every_mode_records_why_it_ended`：团队对战到时 → `TimeUp`；团队歼灭全灭 → `Eliminated`、到时 → `TimeUp`；各自为战剩一辆 → `Eliminated`、到时 → `TimeUp`；进行中 → `None`。
11. `Capture_best_is_kept_per_slot`：队 1 的 slot 3 在队 0 基地 95 tick → `CaptureBest(3) == 3`；非标准战 `CaptureBest` 恒 0。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~StandardRules|FullyQualifiedName~MatchRules" 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告（`Tank.Net`、`Tank.Client` 里按 `BattleMode` 写的 `switch` 照编，`default` 分支兜住；`BattleMessages.ReadMode` 在任务 5 才放开，这之前线上读不到标准战）；Sim 至少 +11（约 871）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完
- [ ] 提交

**提交**：`feat(sim): the standard battle mode — two bases, no respawns, ten minutes; a wipe-out or a capture wins, both on one tick draw; every match records why it ended`，末行 `Tests: Sim 860 → 871.`

### Task 4：两张真图上的无画面整局

**Files:**
- Test: `tests/Tank.Sim.Tests/StandardMatchTests.cs`（只加测试；发现规则问题就修，修的部分写进提交说明）

**规则/要点**（阶段 5 设计 §6：“全灭、占领、超时三种结束；10 分钟限时”）

- 每个用例是 `[Theory]`，参数 `"sandbox"`、`"riverland"`：`MapDefinition map = MapCatalog.Get(id)`、`sim = new Simulation(map.NewBattlefield())`（河谷由 `RiverlandFixture` 只生成一次）、`rules = new MatchRules(Standard, sim, map.Spawns, bases: map.Bases)`；玩家照真对局 `Join` 到本队出生点，每 tick `sim.Step` + `rules.Update()`。摆车只用 `TankRef`（不改地图）。
- 驾驶用测试里的小帮手（同 `MatchRulesTests.Match`：静止、`Attack`），不引入新 AI。

**先写的测试**（`StandardMatchTests`）

1. `A_standard_match_ends_when_one_side_is_wiped_out`：队 0 一辆、队 1 一辆；跑过 150 tick 保护期；把队 1 那辆摆到队 0 基地圆心、队 0 那辆摆到从圆心往本队出生点中心方向 40 m 处（这段在两张图上都是测试查过的平地，没有草丛和岩石挡着），面对面，队 1 那辆 1 血，队 0 `Attack`：结束、`WinnerTeam == 0`、`Eliminated`，用时 < 10 秒（300 tick）。
2. `A_standard_match_ends_when_a_base_is_captured`：队 0 三辆、队 1 一辆；三辆摆进队 1 基地（离圆心 5～10 m 三个方向），队 1 那辆留在出生点：从摆好起第 1000 tick 结束，`WinnerTeam == 0`、`Captured`、`Capture.Progress(1) == 100`；三辆的 `CaptureBest` 各 33。
3. `A_standard_match_nobody_settles_is_a_draw_at_ten_minutes`：一对一在出生点不动：第 17999 tick 后 `Playing`、`TicksLeft == 1`；第 18000 tick 后 `Ended`、`NoTeam`、`TimeUp`；`EndedAt - 开局 tick == 18000`。
4. `Ten_bots_play_a_standard_match_without_trouble`：5 对 5，全部由 `SkirmishBot`（路点 `map.Waypoints`，种子 1～10）驾驶 1800 tick（60 秒）：不抛异常；`Capture.Progress` 都在 0～100；比赛要么还在打，要么已按规则结束（`EndReason != None`）。
5. `A_standard_tick_with_ten_bots_allocates_nothing`（只跑河谷）：同上预热 300 tick 后再 300 tick，`Simulation.Step` + `VisionTracker.Update` + `rules.Update` + 机器人命令分配 0。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~StandardMatch" 2>&1 | grep -E '通过!|失败!'`（10 分钟整局两张图各一次，Debug 下应在 10 秒内）

**验收**：全部测试通过、0 警告；Sim 至少 +9（约 880；`[Theory]` 每个参数算一条）。

**步骤**

- [ ] 写用例，跑（规则已在任务 3 实现，这些测试多半直接通过；不通过就是规则或摆位的问题，查清再修）
- [ ] 四个测试项目全过、五个项目 0 警告
- [ ] 提交

**提交**：`test(sim): whole standard matches on both maps — ended by a wipe-out, a capture and the ten-minute clock; ten bots allocate nothing`，末行 `Tests: Sim 871 → 880.`

**批次 A 收尾**：两路审查（规格审查逐条对照阶段 5 设计 §3.1、§3.2 和 §6 的占领规则测试清单）→ 跟进提交 → 进度第 A 行（基地坐标、实测间距、测试数）。

## 批次 B：联网

### Task 5：协议 v4

**Files:**
- Modify: `src/Tank.Net/Battle/BattleProtocol.cs`（`Version = 4` 和注释）、`src/Tank.Net/Battle/BattleSnapshot.cs`（`MatchBlock`、`PlayerStats` 的新字段和比较）、`src/Tank.Net/Battle/BattleSnapshotCodec.cs`（`WriteMatch`/`ReadMatch`、`WriteStats`/`ReadStats`）、`src/Tank.Net/Battle/BattleMessages.cs`（`MatchStartMessage.Bases`、`MaxBases`、开局消息尾部、`ReadMode`、`ReadRoomInfo`）
- Test: `tests/Tank.Net.Tests/Battle/StandardWireTests.cs`；`BattleProtocolTests` 第 329 行的版本断言改为 4

**接口**：共享接口“联网”。

**规则/要点**（阶段 5 设计 §3.3）

- **对局块**：`TeamScore1` 之后写 `BaseProgress0`、`BaseProgress1` 各一个字节（设计说的“各 1 字节”），再写 2 位 `EndReason`。读：进度 > 100 → `ProtocolException("Base progress out of range.")`；原因 2 位读出来就是 0～3，都合法。所有模式都写（非标准战是 0、0）。
- **计分板**：`Rank` 之后写 7 位 `Capture`；读出 > 100 → `ProtocolException`。只在计分板变了时整张发（现有逻辑），所以占领最高分每秒最多变一次。
- **开局消息**：`MyTankId` 之后写基地数（字节，0 或 2）和每个基地 `u8 team, f32 x, f32 z, f32 radius`（`WriteFloat`）。`MaxMatchStartTailBytes` 加上 `1 + MaxBases × 13`，保证预编码地图的写法一次留够。读：数 > `MaxBases` → `ProtocolException`；逐个读后按 `TeamBase.Check` 的规则和坐标上限检查（违反的转成 `ProtocolException`，不让 `ArgumentException` 漏出去）；读出的 `Bases` 是数组（空就是空数组），不是 null。
- **模式上限**：`ReadMode` 用 `ModeRules.LastMode`；房间状态、开局消息、房间信息都经它，所以都认标准战。`ReadRoomInfo` 先读出版本：版本等于我们的才按 `ReadMode` 检查，别的版本的模式字节原样转成 `BattleMode`（局域网列表照样列出、写“版本不符”）。
- **协议版本**：v3 → v4（阶段 5 设计 §3.3：5A 和 5B 的构建互相连上会读错）；`ConnectKey` 不变（旧客户端还能连上并被告知版本不符）。
- 快照大小：多 2 字节加 2 位；计分板每个在场的人多 7 位。不影响不可靠包上限 1000 字节（测试核对 10 人满员的标准战快照）。

**先写的测试**（`StandardWireTests`）

1. `The_protocol_is_version_4`：`BattleProtocol.Version == 4`、`ConnectKey == "tank3d-v2"`。
2. `Standard_mode_reads_in_the_room_and_the_match_start`：`RoomStateMessage(Standard, …)`、`RoomInfoMessage(4, …, Standard, …)`、`MatchStartMessage(Standard, …)` 往返相同；模式字节 4 的房间状态 → `ProtocolException`。
3. `A_room_of_another_version_with_a_mode_we_do_not_know_is_still_listed`：手写版本 5、模式字节 9、其余合法的房间信息 → 读出不抛、`ProtocolVersion == 5`、`(byte)Mode == 9`；同样内容但版本 4 → `ProtocolException`。
4. `The_match_block_carries_both_bases_and_the_end_reason`：`BaseProgress0 = 37`、`BaseProgress1 = 100`、`EndReason = Captured` 的快照（有基线、无基线各一次）往返相同；`MatchBlock` 只差 `BaseProgress1` 时 `Equals` 为假。
5. `A_base_progress_over_100_is_refused`：手写进度字节 101 → `ProtocolException`。
6. `The_scoreboard_carries_each_players_capture`：两个玩家 `Capture` 33、100 往返相同；只改 `Capture` 时计分板会重发（编码器比较认得它）；手写 101 → `ProtocolException`。
7. `The_match_start_carries_the_bases`：带 `RiverlandMap.Bases` 的开局消息往返后 `Bases` 逐个相同（浮点逐位）；不带基地的读出空数组；`Write(w, m, encodedMap)` 和 `Write(w, m)` 带基地时逐字节相同。
8. `Bad_bases_in_a_match_start_are_refused`：手写：数 3；两个队 0；队号 2；半径 0、NaN、101；x = 1e7 m；各自 → `ProtocolException`。
9. `A_match_start_cut_short_anywhere_in_its_bases_is_refused`：带基地的开局消息从基地数那个字节起逐字节截断，每种都 `ProtocolException`。
10. `A_full_standard_snapshot_fits_an_unreliable_packet`：10 个玩家、32 个实体全新（无基线）、计分板全变、基地 99/37：编码字节数 < `BattleServer.MaxUnreliableBytes`（记下实际大小）。

**命令**：`dotnet test tests/Tank.Net.Tests 2>&1 | grep -E '通过!|失败!'`；`dotnet test tests/Tank.Art.Tests 2>&1 | grep -E '通过!|失败!'`（`LanRoomRowTests` 用的是 `BattleProtocol.Version`，应不改照过）；soak 四种链路。

**验收**：全部测试通过、0 警告；soak PASS（数字和改动前同量级，记进度）；Net 至少 +11（约 242）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完
- [ ] 提交

**提交**：`feat(net): protocol v4 — the match block carries both bases and the end reason, the scoreboard each player's capture, the match start the bases`，末行 `Tests: Net 231 → 242.`

### Task 6：服务器跑标准战

**Files:**
- Modify: `src/Tank.Net/Battle/BattleServer.Match.cs`（`TryStartMatch`：标准战查基地、第 61 行 `MatchRules` 带基地；`TickMatch` 第 139 行结束时的日志；`Build` 第 237 行起的对局块和计分板；`SendMatchStart` 第 300 行带基地）、`src/Tank.Server/Program.cs`（第 14、21 行的用法）
- Modify: `tests/Tank.Net.Tests/Battle/TestMaps.cs`（`Standard(id)`、`StandardWalled(id)`）
- Test: `tests/Tank.Net.Tests/Battle/StandardServerTests.cs`

**接口**：共享接口“联网”的 `BattleServer` 两条。

**规则/要点**（阶段 5 设计 §3.1、§3.3）

- `TryStartMatch`：在补机器人**之前**：`ModeRules.HasBases(_mode) && _roomMap.Bases.Count == 0` → `reason = "the map has no bases"`、返回 false。开局：`new MatchRules(_mode, _sim, spawns, duration, bases: map.Definition.Bases)`。机器人照旧 `SkirmishBot`（阶段 5 设计：会寻路和分工的是 5C）。
- 快照：`BaseProgress0 = rules.Capture?.Progress(0) ?? 0`、`BaseProgress1` 同理、`EndReason = rules.EndReason`；计分板每行 `Capture = rules.CaptureBest(slot)`。基地分数全队可见、不看点亮（阶段 5 设计 §3.3：在基地里不等于被点亮，进攻车的位置照旧按点亮规则下发——`Viewer.Sees` 不变）。
- 开局消息：`Bases` 在 `ModeRules.HasBases(_mode)` 时是 `_matchMap.Definition.Bases`，否则 null（客户端只在标准战画圈）。重同步、中途加入、重连发的开局消息同样带。
- 中途加入：`HandleHello` 里本来就是 `!seat.InMatch && _rules.AcceptsJoins` 就 `PutIntoMatch`，标准战现在 `AcceptsJoins` 为真：新座位按 `ThinnerTeam` 分队、开座位的车（默认中坦，`BattleSeat.TankIndex = 1`），进来是一辆新车。
- 结束日志：`TickMatch` 里 `rules.Phase == Ended` 且 `_endedAtFrame < 0` 的那一 tick 打一行 `match ended: standard, team 0, captured`（格式见共享接口；各自为战赢家写 `slot N`；平局写 `draw`；原因 `time up`/`eliminated`/`captured`）。所有模式都打。
- `Tank.Server`：用法两处写成 `--mode teambattle|annihilation|ffa|standard`（解析本来就走 `ModeRules.TryParse`）。
- `TestMaps.Standard(id)`：`Small` 加基地 `[(0, (125, 105), 25), (1, (75, 105), 25)]`——两个基地都在两队出生点之间（队 0 出生点 x 40、队 1 x 160），各离对方出生点 35 m，测试里开 20 m 就进圈（测试图不必守“基地在本队后方”的规则）。`StandardWalled(id)`：同上再加一排石墙（`WallLine` 段，`StoneWall`，x = 115，z 20～190），挡住队 1 出生点看队 1 基地（x 75）的视线。

**先写的测试**（`StandardServerTests`，用 `BattleHarness`）

1. `A_standard_match_starts_with_the_maps_bases`：`Map = TestMaps.Standard("s")`、两个客户端、`StartMatch(Standard)`：两端 `MatchInfo.Bases` 等于地图的基地；同一张图开团队对战时 `MatchInfo.Bases` 为空。
2. `A_map_without_bases_holds_a_standard_start`：`TestMaps.Small("x")`：房主选标准战、开局 → 不开（`MatchRunning` 假），日志 `can't start: the map has no bases`，房间里没有多出机器人；改成团队歼灭能开。
3. `Three_attackers_take_the_enemy_base_through_the_network`：`Standard("s")`，客户端 A、B、C（队 0）和 D（队 1，不动；开局前各自 `SetTeam`，否则按人少的队会分成 2 对 2）。A、B、C 油门全开向东，各自 `client.Own.Position.X ≥ 60` 后松油门、踩住；跑 45 秒：四个客户端都看到 `Match.Phase == Ended`、`WinnerTeam == 0`、`EndReason == Captured`、`BaseProgress1 == 100`；A、B、C 的计分板 `Capture` 各 ≥ 30（都在 33 左右）；D 收到的 `BaseProgress1` 序列不减。服务器日志有 `match ended: standard, team 0, captured`。
4. `Base_progress_reaches_a_team_that_cannot_see_the_attackers`：`StandardWalled("w")`，队 0 一个客户端 A 开进队 1 基地，队 1 的 D 站在出生点（墙后、离 A > 15 m）：在 `BaseProgress1 ≥ 5` 之前和之后，D 的每份快照都不含 A 的车（`Latest.TryFind(A.OwnTankId, …)` 假），而 `BaseProgress1` 在涨。
5. `A_standard_match_runs_out_of_time_in_a_draw`：`MatchSeconds = 3`、两个客户端不动：结束、`WinnerTeam == NoTeam`、`EndReason == TimeUp`；日志 `match ended: standard, draw, time up`。
6. `A_player_joining_a_running_standard_match_gets_a_new_tank`：两个客户端开局 3 秒后加第三个：它收到带基地的开局消息、`OwnTankId != 0`、在人少的队（平手时队 0）、车型中坦；它的车在计分板上 `Alive`。
7. `Wiping_out_the_other_side_ends_it_for_everyone`：`Standard("s")` 一对一，A 对准 D 开火（D 不动，用 `MatchRulesTests` 同样的瞄法从客户端发：瞄 D 的车体，`Fire`）直到 D 被毁：两端 `WinnerTeam == 0`、`Eliminated`。（服务器测试里 `Simulation` 是私有的，不改血量；中坦打中坦几发能毁，跑 60 秒内结束。）
8. `Riverland_standard_with_bots_runs_and_ends_cleanly`：`Map = MapCatalog.Riverland`、两个客户端 + 3 个机器人、`MatchSeconds = 20`：`MatchInfo.Bases` 等于 `RiverlandMap.Bases`；20 秒后结束，`EndReason` 是 `TimeUp`、`Eliminated` 或 `Captured` 之一；日志没有 `error`，有 `match ended: standard`。

**命令**

```bash
dotnet test tests/Tank.Net.Tests --filter "FullyQualifiedName~Standard" 2>&1 | grep -E '通过!|失败!'
dotnet run --project src/Tank.Server -- --mode nope; echo "exit $?"     # 用法里有 standard，exit 2
timeout 20 dotnet run --project src/Tank.Server -c Release -- --map sandbox --mode standard --bots 2 --port 27094   # 能起来
```

**验收**：全部测试通过、0 警告；soak PASS；Net 至少 +8（约 250）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完
- [ ] 提交

**提交**：`feat(server): standard battles — matches need the map's bases, snapshots carry the capture and the end reason, starts carry the bases, players join mid-match`，末行 `Tests: Net 242 → 250.`

**批次 B 收尾**：两路审查（规格审查对照阶段 5 设计 §3.3 和 §6 的协议测试；代码质量审查看边界检查、分配和日志）→ 跟进提交 → 进度第 B 行（快照实测大小、soak 数字）。

## 批次 C：客户端

### Task 7：引擎无关的文字和数据

**Files:**
- Create: `src/Tank.Client/scripts/Match/Logic/CaptureBars.cs`
- Modify: `src/Tank.Client/scripts/UI/Logic/ModeText.cs`、`Bootstrap/LaunchOptions.cs`（第 11 行用法注释）、`Match/Logic/HudText.cs`（`Plate`、`Results`）、`Match/Logic/MatchHudFrame.cs`（`ResultsInfo.Reason`、`MatchHudFrame.Capture`、`From`）、`Match/Logic/ScoreboardModel.cs`（`ScoreRow.Capture`、`Build` 填它）
- Test: `tests/Tank.Art.Tests/StandardHudTextTests.cs`、`CaptureBarsTests.cs`；扩充 `ModeTextTests`、`LaunchOptionsTests`、`RoomModelTests`、`ScoreboardModelTests`、`MatchHudFrameTests`

**接口**：共享接口“客户端”的 `ModeText`、`CaptureBar`、`CaptureBars`、`CaptureText`、`CaptureAlarm`、`ResultsInfo`、`MatchHudFrame.Capture`、`ScoreRow.Capture`。

**规则/要点**（阶段 5 设计 §3.4）

- `ModeText.Name(Standard) == "标准战"`；`Rule(Standard)`：`"两队对抗，被击毁就出局；全歼对方或占满对方基地（100 分）获胜，{minutes} 分钟内没分出胜负算平局。"`（分钟数取 `ModeRules`，即 10）。房间标题、下拉表、局域网列表的模式列都经 `ModeText.Name`，自动显示“标准战”；`LanRoomRow` 的模式列宽 8 放得下。
- `HudText.Plate`：标准战和团队歼灭一样是 `PlateKind.TeamAlive`（“我方存活 / 敌方存活”，存活 / 本队在场的全部，中途加入的也算进分母）。
- `HudText.Results`：标准战按 `info.Reason`（标题照旧 胜利 / 失败 / 平局）：

  | 胜负 | 原因 | 副标题 |
  |---|---|---|
  | 胜 | `Captured` | 占领了敌方基地 |
  | 负 | `Captured` | 我方基地被占领 |
  | 胜 | `Eliminated` | 全歼敌人 |
  | 负 | `Eliminated` | 我方全灭 |
  | 平 | `TimeUp`（或 `None`） | 时间到，平局 |
  | 平 | `Eliminated` | 双方同归于尽 |
  | 平 | `Captured` | 双方同时占领了对方基地 |

  其他三种模式的结算文字不变。
- `CaptureText.Bars`：只在标准战；己方基地是 `ownTeam` 那队的（`BaseProgress0` 或 `BaseProgress1`），先列己方、再列敌方；分数 0 的不列。`Label`：`"我方基地 37"`、`"敌方基地 62"`。不分配（结果是值类型）。
- `CaptureAlarm.Update(points, now)`：记上一次的分和最近一次上涨的时刻；分比上次大 → 记下 `now`；分比上次小或为 0 → 立即不闪；返回 `now - 上次上涨 < HoldSeconds`。第一次调用只记基线、返回假（批次 C 跟进：中途加入或重连时己方基地已有 37 分、没人在占，不该闪）。（快照每秒最多让分数涨 1 到 3 分，1.5 秒能连上两次上涨，持续被占时一直闪，停下后 1.5 秒内停闪。）
- `MatchHudFrame.From`：`Capture = CaptureText.Bars(mode, ownTeam, match)`；`Results` 带 `match.EndReason`。
- `ScoreboardModel.Build`：每行 `Capture = p.Capture`；排序不变（标准战和团队模式一样按击毁、阵亡、槽位）。
- 这些文件都在 `Logic/` 里，不能 `using Godot`。

**先写的测试**

- `StandardHudTextTests`：
  1. `The_standard_plate_counts_the_living_like_annihilation`：标准战 8 人（队 0 的 4 人里 3 活，队 1 的 4 人里 1 活），自己在队 0 → `TeamAlive`、`3/4`、`1/4`；自己在队 1 → 左右对调；`Parts` 的说明文字是“我方存活”“敌方存活”。
  2. `Standard_results_name_why_the_match_ended`（`[Theory]`，上表七行，`ownTeam` 0，赢家 0 / 1 / `NoTeam`）：标题和副标题逐字相等。
  3. `The_other_modes_results_ignore_the_reason`：团队歼灭在 `Reason = Captured` 时仍是“敌方全军覆没”等原来的文字。
- `CaptureBarsTests`：
  1. `No_bars_outside_a_standard_match`：团队对战、`BaseProgress0 = 50` → `Count == 0`。
  2. `The_own_base_comes_first_and_empty_bases_are_left_out`：标准战、`BaseProgress0 = 37`、`BaseProgress1 = 62`：`ownTeam` 0 → `[我方 37, 敌方 62]`；`ownTeam` 1 → `[我方 62, 敌方 37]`；`BaseProgress1 = 0` → 只有我方 37。
  3. `Labels_read_whose_base_and_the_points`：`"我方基地 37"`、`"敌方基地 100"`；`de-DE` 文化下不变。
  4. `The_indexer_refuses_a_bar_that_is_not_there`。
  5. `The_alarm_blinks_while_the_own_base_rises`：`Update(0, 0)` 假；`(1, 1.0)` 真；`(1, 2.0)` 真（0.99 s）；`(1, 2.6)` 假；`(2, 3.0)` 真；`(0, 3.1)` 假；`(3, 3.2)` 真、`(2, 3.3)` 假（下降立即停）。
- `ModeTextTests`：`Name(Standard) == "标准战"`；`Rule(Standard)` 含“10 分钟”和“100 分”。
- `LaunchOptionsTests`：`--mode standard` → `Mode == Standard`；`--mode nope` 的警告里列出四个 id（含 `standard`）。
- `RoomModelTests`：房间状态 `(Standard, map "riverland")` → `Title == "标准战 · 河谷"`、`RuleLine == ModeText.Rule(Standard)`。
- `ScoreboardModelTests`：标准战两人 `Capture` 41、0 → 行里 `Capture` 相同。
- `MatchHudFrameTests`：回环房主（`HostSettings` 的 `Mode = Standard`，试驾场地图）开一局：第一帧 `Capture.Count == 0`、`Results.Reason == None`、`Plate.Kind == TeamAlive`。

**命令**：`dotnet test tests/Tank.Art.Tests 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告；Art 至少 +20（约 1059）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完
- [ ] 提交

**提交**：`feat(client): standard battle words and data — the mode's name and rule, alive counts, why the match ended, the capture bars and their alarm, the capture column`，末行 `Tests: Art 1039 → 1059.`

### Task 8：HUD 视图和画廊

**Files:**
- Create: `src/Tank.Client/scripts/Match/CaptureBarsView.cs`
- Modify: `src/Tank.Client/scripts/Match/Logic/HudLayout.cs`（常数、`HudBoxes.CaptureBars`、`CaptureRow`）、`Match/MatchHud.cs`（挂视图、可见性）、`Match/ScoreboardView.cs`（“占领”列）、`Match/CenterBanner.cs`（`ResultsKey` 加原因）、`Match/Logic/HudDemoFrames.cs`（`standard`、`capture`）
- Test: 扩充 `tests/Tank.Art.Tests/HudLayoutTests.cs`、`HudDemoFramesTests.cs`

**接口**：共享接口“客户端”的 `HudLayout` 三个常数、`HudBoxes.CaptureBars`、`CaptureRow`。

**规则/要点**（阶段 5 设计 §3.4）

- **进度条**（`CaptureBarsView : HudPanel`）：在比分板和模式名下面（`CaptureTop` 124），和比分板同宽 360、居中；每条高 26、两条间隔 6。每条：圆角底板（`HudStyle.Panel`，0.85 不透明），里面按分数比例填色，左侧文字 `CaptureText.Label`（字号 18、描边 4），右侧不另写数字。颜色：己方基地的条红色（`Palette.UiDanger`），`CaptureAlarm` 为真时闪（不透明度在 1 和 0.35 之间按 2 Hz 方波，用场景时钟 `now`）；敌方基地的条用 `MatchColors.Ui(Side.Ally)`（我们在占，蓝灰），不闪。数值、条数或闪烁相位变了才 `QueueRedraw`。
- `MatchHud`：新建 `_capture = Add(new CaptureBarsView())`，`Refresh` 里 `_capture.Update(frame, now)`；计分板或结算显示时隐藏（和比分板一样让位）。
- **计分板“占领”列**：只在标准战。标准战的列位置：击毁 560、阵亡 640、占领 720（居中）、状态 790；其他模式的列位置不变（现有截图不变）。表头第 6 列“占领”，值是 `ScoreRow.Capture` 的整数。敌方的占领只在对局结束后显示：对局进行中服务器对敌方各行发 0（偏差 5），这一列的敌方行在对局中留空（不写 0，免得看着像“没占过”），己方各行一直是真数。结算页的计分表是同一个视图，同样有这一列，那时所有行都是真数。
- **结算**：`CenterBanner.ResultsKey` 加 `MatchEndReason`，原因变了重算文字。
- **画廊**（`HudDemoFrames.Names` 追加 `"standard"`、`"capture"`）：
  - `standard`：标准战 8 人（队 0 的 4 人里 3 活、队 1 的 4 人里 2 活），剩 6:40；`BaseProgress0 = 37`（己方基地被占、红色；画廊时钟冻结在 0，用 `CaptureAlarm` 的第一次读数只是基线、不闪，条是常亮的红色）、`BaseProgress1 = 62`；击杀栏两行。
  - `capture`：标准战结算，队 0 赢、`Reason = Captured`；6 人，队 0 三人的 `Capture` 41、33、26，队 1 三人 0、0、12；标题“胜利”、副标题“占领了敌方基地”；计分表有“占领”列。
- 画廊帧也走 `MatchHudFrame` 的 `Capture` 字段（`Frame(...)` 帮手按模式和对局块算好）。

**先写的测试**

- `HudLayoutTests`（现有的几种屏幕尺寸）：
  1. `The_capture_bars_hang_under_the_mode_label_as_wide_as_the_plate`：`CaptureBars.Y == 124`、宽 360、居中、高 58；在模式名下面不重叠；`CaptureRow(1).Y == 124 + 32`。
  2. `The_capture_bars_clear_the_kill_feed_the_net_panel_and_the_banner`：不和击杀栏、F3 面板、中央横幅、状态面板相交；在屏幕里。
- `HudDemoFramesTests`：
  1. 名单含 `standard`、`capture`（现有的“画廊有这些名字”和“每帧自洽”“比分板和玩家一致”两条 `[Theory]` 自动覆盖新帧）。
  2. `The_standard_screen_shows_both_bars_own_base_first`：`Frame.Capture.Count == 2`、第一条是己方 37、第二条敌方 62；`Plate` 是 `3/4`、`2/4`。
  3. `The_capture_screen_is_a_win_by_capture_with_the_capture_column`：标题“胜利”、副标题“占领了敌方基地”；`ScoreboardModel.Build` 出来的行里 `Capture` 依次（按本队排序）含 41、33、26。

**命令**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | grep -E '通过!|失败!'
dotnet build src/Tank.Client/Tank.Client.csproj && godot-mono --headless --path src/Tank.Client --import
mkdir -p build/shots/5b-t8
for d in standard capture scoreboard results-win results-draw annihilation; do
  $S/godot_run.sh $W "" 30 --hud-demo $d --screenshot $W/build/shots/5b-t8/$d.png --screenshot-at 1 --quit-after 2; done
$S/baseline4b.sh $W build/shots/5b-t8-sandbox && python3 $S/pngdiffdirs.py build/shots/base build/shots/5b-t8-sandbox
```

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +5（约 1064）。
- [ ] 截图逐张看过：`standard` 两条进度条在比分板和“标准战”下面、己方红条“我方基地 37”、敌方蓝灰条“敌方基地 62”，不压击杀栏；比分板“我方存活 3/4 / 敌方存活 2/4”；`capture` 结算“胜利 · 占领了敌方基地”，计分表有“占领”列、数字对齐；`scoreboard`、`results-win`、`results-draw`、`annihilation` 和改动前一样（没有“占领”列、没有进度条）。
- [ ] 试驾场六张回归与基线同量级。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（截图逐张看过）
- [ ] 提交

**提交**：`feat(client): standard battle HUD — capture bars under the plate (red and blinking while our base falls), the capture column, results by reason, gallery screens standard and capture`，末行 `Tests: Art 1059 → 1064.`

### Task 9：地上的圈

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/RingMesh.cs`、`shaders/base_ring.gdshader`、`scripts/World/BaseRings.cs`
- Modify: `src/Tank.Client/scripts/Match/MatchMain.cs`（`BuildScene` 之后、知道自己队伍时建圈）、`scripts/Sandbox/Logic/MapSites.cs`（两张图的 `base0`、`base1` 机位和 `PresetNames`）
- Test: `tests/Tank.Art.Tests/RingMeshTests.cs`；扩充 `MapSitesTests`

**接口**：共享接口“客户端”的 `RingMesh`、`BaseRings`。

**规则/要点**（阶段 5 设计 §3.4：“两个基地在地面上各画一个圈（己方蓝灰、对方红），贴地，半透明的抖动边”）

- `RingMesh.Build`：`segments` 段围一圈，内、中、外三圈顶点（共 `3 × segments` 个、首尾共用，不留缝；中圈 UV.x 0.5 是批次 C 跟进加的：1.8 m 宽的带横跨地形格时，只有内外两圈的话带子中间会拉成直线、切进双线性插值的地面里，中圈让带子在宽度方向也跟着地面弯），每段两个四边形、四个三角形，每个顶点高 `heightAt(x, z) + lift`、法线朝上、UV.x 内 0 中 0.5 外 1、UV.y 按角度 0～1（UV.y 在最后一段从 (segments − 1)/segments 插回 0，那一段的 UV.y 是倒着走的“缝”；着色器不读 UV.y，所以看不出来，以后要按 UV.y 画图案就得把首列顶点复制一份）；三角形朝上（逆时针从上看，和 `GridMesh` 同约定，测试核对法线点积 > 0）。`inner < outer`、`segments ≥ 3`、`lift ≥ 0`，否则 `ArgumentOutOfRangeException`。
- `BaseRings.Build`：每个基地一圈，带子由 `RingBand.Around(radius)`（`World/Logic/`，有测试）给出：内径 `Radius − 1.6`（小于 0 时取 0）、外径 `Radius + 0.2`，实线从 UV.x `1 − SolidLine / (外径 − 内径)` 起（按夹过之后真实的带宽算，半径不到 1.6 m 的基地实线也是 `SolidLine` 宽）；`Segments` 160（外圈每段约 1 m，比地形格 2 m 密，贴地起伏不穿模），高度取 `TerrainMesh.VisualHeightAt(terrain, x, z)`（画出来的地面）+ `Lift`；颜色 `MatchColors.BaseRing(ownTeam, team)`：己方 `Palette.AllyBody`、对方 `Palette.EnemyBody`（由 `ownTeam` 决定，观战时也按自己的队；两个队各有测试）；不投影。批次 C 跟进按坦克高度的截图定了 `Lift` 0.3（0.15 时从 4 m 高看，远侧的边整段埋在 0.25～0.45 m 的草里）、`SolidLine` 0.4 m（0.2 m 时跟车镜头在出生距离 46 m 外看，圈的远侧不到一个像素，断成虚线）。
- `base_ring.gdshader`：`unshaded`、`cull_disabled`；颜色 uniform；不透明度从外缘 0.85 向内缘渐到 0（按 UV.x 的 `smoothstep`），用 `dither.gdshaderinc` 的 `bayer4` 做屏幕抖动丢片元（项目不排序透明物体，和草丛、水边一样）；外缘 `SolidLine`（0.4 m）内一条实线（不透明度 1），远处也看得清边界。`unshaded` 是有意的：树、车的影子不压暗圈，所以影子横过圈的地方看起来像被圈切断——当作地上的标线来看，这样更清楚，不改。
- `MatchMain`：场景建好、`_client.MatchInfo?.Bases` 不为空时建一次 `BaseRings`（重同步换了 `Battlefield` 也不重建：基地不变）；自己的队按现有的 `OwnTeam(own)`。试驾场不画（试驾场不是对局，没有基地规则）。
- `MapSites`：两张图都加锚点 `base0`、`base1`：焦点是该基地圆心（离地 0 m），眼睛在圆心往本队出生点中心方向 45 m、高 30 m；`PresetNames` 加这两个；试驾场地图原来没有锚点（它的机位是 `CameraPresets` 的常数），加这两个不影响别的机位。用途是对局截图（`--camera-preset base1`）。批次 C 跟进又加了两组，同一条线上、同样在 `StreamsAroundCamera` 里（草围着镜头生成）：`base0near`、`base1near`——坦克高度看圈，眼睛离圆心 30 m、高 4 m，焦点圆心离地 0 m；`base0far`、`base1far`——跟车镜头在出生距离看圈：一辆车停在离圆心 46 m、车头朝基地时 `OrbitCamera` 开局的样子（14 m 轨道、俯仰 0.25 rad），眼睛离圆心 46 + 14 cos 0.25 ≈ 59.6 m、高 4.5 + 14 sin 0.25 ≈ 8 m，焦点离圆心 46 m、高 4.5 m。
- 草：圈抬高 0.15 m，草叶可能遮住一部分；截图验收时看清楚圈的边，看不清就把 `Lift` 提到 0.3（同时改常数和测试），不另做清草。（批次 C 跟进从坦克高度看过：0.15 看不清，已提到 0.3；`RingMeshTests` 用自己的 lift，不受这个常数影响。）

**先写的测试**

- `RingMeshTests`：
  1. `A_ring_has_two_vertices_and_two_triangles_per_segment`：`segments` 8 → 16 个顶点、16 个三角形。
  2. `Every_vertex_lies_on_its_circle_at_the_ground_plus_the_lift`：圆心 (10, 20)、内 3 外 5、`heightAt = (x, z) => 0.1f * x`、`lift` 0.15：内圈顶点离圆心 3、外圈 5（± 1e-4）；y = 0.1x + 0.15。
  3. `The_ring_closes_without_a_seam`：最后一段的三角形用的是第一对顶点（没有重复顶点、没有缝）。
  4. `Triangles_face_up_and_uv_runs_inner_to_outer`：每个三角形的几何法线 y > 0；内圈 UV.x 0、外圈 1。
  5. `Bad_sizes_are_refused`：`inner ≥ outer`、`segments` 2、`lift` −1。
- `MapSitesTests`：两张图都有 `base0`、`base1`，焦点等于 `SandboxMap.Bases` / `RiverlandMap.Bases` 的圆心，眼睛离焦点 45 m、在本队出生点那一侧；`PresetNames` 含它们；`CameraPresets.IsKnownFor("base1", site)` 真。

**命令**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | grep -E '通过!|失败!'
dotnet build src/Tank.Client/Tank.Client.csproj && godot-mono --headless --path src/Tank.Client --import
mkdir -p build/shots/5b-t9
for m in riverland sandbox; do for b in base0 base1; do
  $S/godot_run.sh $W "" 120 --host --mode standard --map $m --bots 1 --autostart --camera-preset $b \
    --screenshot $W/build/shots/5b-t9/$m-$b.png --screenshot-at 25 --quit-after 30; done; done
$S/godot_run.sh $W "" 120 --host --mode standard --map riverland --bots 1 --autostart \
  --screenshot $W/build/shots/5b-t9/riverland-follow.png --screenshot-at 25 --quit-after 30     # 跟车镜头：自己的基地在身后 46 m
for b in base0near base1near base0far base1far; do                                               # 批次 C 跟进：坦克高度、出生距离的跟车镜头
  $S/godot_run.sh $W "" 150 --host --mode standard --map riverland --bots 1 --autostart --camera-preset $b \
    --screenshot $W/build/shots/5b-cfix/riverland-$b-final.png --screenshot-at 25 --quit-after 30; done
$S/godot_run.sh $W "" 120 --host --mode teambattle --map riverland --bots 1 --autostart --camera-preset base0 \
  --screenshot $W/build/shots/5b-t9/teambattle-base0.png --screenshot-at 25 --quit-after 30     # 非标准战：没有圈
$S/baseline4b.sh $W build/shots/5b-t9-sandbox && python3 $S/pngdiffdirs.py build/shots/base build/shots/5b-t9-sandbox
```

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +6（约 1070）。
- [ ] 截图逐张看过：两张图上 `base0` 是蓝灰的圈、`base1` 是红圈，贴着地、边缘抖动渐隐、外缘清楚、草里也看得见；圈在基地的平地上，没有压在房子、树上（岩石按“与设计的偏差”2 允许在外环：河谷 base0 东边圈上压着一丛岩石，试驾场地图 base1 北边圈上有一块）；从坦克高度（`base0near`、`base1near`）看得见圈的近边和两侧，跟车镜头在 46 m 外（`base0far`、`base1far`）外缘实线连成一圈不断；`teambattle-base0` 没有圈；回归六张与基线同量级。
- [ ] 日志里 `[match] entered match Standard on riverland`、`[server] match started: standard, map riverland`。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（截图逐张看过）
- [ ] 提交

**提交**：`feat(client): base rings on the ground — ours blue-grey, theirs red, a dithered translucent band draped on the terrain; base cameras on both maps`，末行 `Tests: Art 1064 → 1070.`

**批次 C 收尾**：两路审查（规格审查对照阶段 5 设计 §3.4 逐条：圈、进度条、比分板、计分板、结算、画廊；代码质量审查看分配、重画条件、`Logic/` 里没有 Godot）→ 跟进提交 → 进度第 C 行（截图目录、看到了什么）。

## 批次 D：收尾

### Task 10：验收

不改代码（发现问题就修，修的提交单独写 `fix(<scope>): …`）。

- [ ] 五个项目 0 警告；四个测试项目全过（记数字）；soak 四种链路 PASS（记数字，和改动前同量级）。
- [ ] 画廊截图 `build/shots/5b-t10/`：`standard`、`capture`，和现有的 `scoreboard`、`results-win`、`results-lose`、`results-draw`、`annihilation`、`ffa`（应和改动前一样）。
- [ ] 试驾场六张回归截图与 `build/shots/base/` 同量级。
- [ ] 房间：`--host --mode standard` 开房，标题“标准战 · 河谷”，模式下拉表最后一项“标准战”，规则那行是标准战的；`--map sandbox --mode standard` 显示“标准战 · 试驾场地图”。
- [ ] 两实例在两张图上各打一局标准战（阶段 5 设计 §6 的 5B 验收）：

  ```bash
  for m in riverland sandbox; do
  $S/pair4b.sh $W $W/build/shots/5b-t10 pair-$m 240 \
    "--host --name 甲 --mode standard --map $m --players 2 --bots 2 --autostart --match-seconds 90 --netgraph --screenshot $W/build/shots/5b-t10/pair-$m-host.png --screenshot-at 40" \
    "--join 127.0.0.1 --name 乙 --tank heavy --autodrive --netgraph --screenshot $W/build/shots/5b-t10/pair-$m-guest.png --screenshot-at 40" 3
  done
  grep -hE "match started|entered match|match ended|match over|ERROR|Exception" $W/build/shots/5b-t10/pair-*.log
  ```

  客户端（队 1）在河谷那局另拍一张 `--camera-preset base1`（客户端参数加 `--camera-preset base1`、截图路径换成 `pair-riverland-guest-base1.png`，单独跑一次 `pair4b.sh`）：它自己的圈必须是蓝灰的（`MatchColors.BaseRing` 按客户端自己的队上色）。

  两端日志都有 `[match] entered match Standard on <map>`，房主有 `[server] match started: standard, map <map>`、`match ended: standard, …` 和 `match over, back to the room`，没有异常；截图里有两个圈（视野里的那个）、比分板是“我方存活 / 敌方存活”；F3 面板的对账次数和最大修正与 5A 同量级。`--match-seconds 90` 下多半以 `time up` 结束，日志里的原因照实记下。
- [ ] 帧率参考：河谷标准战对局（房主 `--mode standard --map riverland --bots 3 --autostart --camera-preset base0 --fps-log`，跑 40 秒、前 20 秒不算），和 5A 的河谷对局同量级，数字记进度。
- [ ] 专用服务器：`dotnet run --project src/Tank.Server -c Release -- --map riverland --mode standard --bots 3 --port 27094` + 客户端 `--join 127.0.0.1:27094 --autostart` 进局（日志 `entered match Standard on riverland`）。
- [ ] 发布脚本的冒烟参数仍够用：`godot-mono --headless --path src/Tank.Client -- --host --port 27091 --bots 1 --autostart --quit-after 10` 输出里 `[server] match started` 和 `[match] entered match` 都有。
- [ ] `pgrep -fl 'MacOS/Godot'` 没有残留。

**提交**：没有代码改动就不提交，结果记进度（下一个任务一起提交）。

### Task 11：文档

**Files:** `docs/01-game-design.md`、`docs/03-netcode.md`、`docs/04-architecture-and-plan.md`、`README.md`、`docs/PROGRESS.md`（`docs/02-tech-stack.md` 只在协议表提到版本时改）

内容以代码为准，不写没做的东西：

- `docs/01-game-design.md`：§2.2 模式表加“标准战”一列（两队、不复活、10 分钟、全歼或占领胜、同 tick 双胜平局、超时平局、中途加入：可以，进人少的队，一辆新车）；新一节“基地和占领”（半径 25 m；每秒 1 分、最多 3 辆按车号取；守军暂停；挨打和阵亡清零；离圈保留；满 100 胜；两张图的基地坐标和间距）；§9 HUD 加进度条、“占领”列、结算原因；§11 还没有做的改为 5C、5D 和阶段 6。
- `docs/03-netcode.md`：§2.1 参数表（协议版本 4）；§4.2 `MatchStart` 带基地、`RoomState`/`RoomInfo` 的模式可以是标准战（别的版本的未知模式照样列出）；快照的对局块（两个基地分数、结束原因）和计分板（占领；对局进行中敌方各行发 0，结束后才对所有人发真数，见偏差 5）；§9.1 房间流程里标准战中途加入；§9.1 还要写“没有重生的对局里一人一局只有一辆车”：服务器按重连令牌记下这一局拿过车的人，座位释放后（退回菜单、断线超过宽限）凭同一令牌回来只能在房间里等下一局，宽限内回来拿回原来的车（或残骸）；客户端按房间地址记住上次的令牌，再进同一房间时带上——这一条四种模式都生效：任何模式里宽限内从菜单再进同一房间，都凭令牌拿回原座位和原来的车，不占第二个座位（以前只有自动重连带令牌）。房间地址先规整再做键（去空格、小写、去掉 `[ ]`；IP 地址按标准写法，IPv4 映射的 IPv6 地址写成 IPv4；`localhost` 和 `::1` 都算 127.0.0.1；端口照留；不查 DNS）。§9.1 还要写：对局里没有一个人类玩家了（在对局里的，连着的或宽限内断开的）就结束，在房间里等下一局的人不算。§12 已知限制加“重启游戏（令牌丢了）或改过的客户端不带令牌，仍会作为新玩家拿到一辆新车；不按名字认人（名字能改、会重名）；令牌存在运行中的这个游戏里，两个人轮流用同一个游戏实例就共用令牌；同一台主机换一条路（局域网 IP 和 127.0.0.1、主机名和 IP）是另一个键，认不出是同一个房间”。
- `docs/04-architecture-and-plan.md`：§1 仓库结构加 `Modes/BaseCapture`、`TeamBase`、`MatchEndReason`；§3 客户端加 `CaptureBarsView`、`World/BaseRings`、`Art/Mesh/RingMesh`；§5 测试表加新的测试类；§6 阶段路线 5B 已完成。
- `README.md`：进度表 5B ✅；模式说明加标准战；启动参数表 `--mode … |standard`；专用服务器的 `--mode` 加 `standard`；画廊名加 `standard`、`capture`。
- `docs/PROGRESS.md`：5B 节点记录最后一行（验收数字、截图目录），“当前位置”改为 5B 完成、下一步 5C。

**命令**：`git diff --stat`；文档里提到的每个启动参数、类名、数字用 grep 在代码里核对一遍。

**步骤**

- [ ] 按上面逐个文件改
- [ ] 用 grep 把文档里的参数、类名、数字和代码核对一遍
- [ ] 提交

**提交**：`docs: stage 5B — standard battle, bases and capture, protocol v4 in docs 01, 03, 04 and the README`，末行 `Tests: unchanged (Sim 880, Net 250, Art 1070, Update 42).`（按实际）

**批次 D 收尾**：两路审查（规格审查对照阶段 5 设计 §6 的 5B 部分和下面的验收清单；文档审查对照代码）→ 跟进提交 → 进度 → 验收清单全部打勾后 squash 合入本地 main（见“执行方式”），合入后在 main 上再跑一遍四个测试项目和 0 警告检查。

---

## 验收清单（5B）

- [ ] 四个测试项目全部通过，Sim、Net、Client、Server、Soak 五个项目 0 警告；测试数比改动前（Sim 830、Net 231、Art 1039、Update 42）只增不减。
- [ ] 两张图的指纹测试不变（地形、物件、出生点没动）；试驾场六张回归截图与基线一致。
- [ ] 两张图各有两队基地，测试查过：离本队出生点 40～80 m、在本队后方、平地、干地、空旷、和两队出生点连通；河谷两基地相距约 983 m、试驾场地图约 361 m。
- [ ] 占领规则的单元测试全过：每秒 1 分、3 辆上限按车号取、挨打清零（只清挨打那辆，0 伤害不清）、阵亡清零、守军暂停（残骸不算）、离圈保留、圈外挨打也清零、满 100 获胜（不早一 tick）、同 tick 双胜平局（双方占领、双方全灭）；分配 0。
- [ ] 标准战 10 分钟；两张真图上的无画面整局分别以全灭、占领、超时结束；结束原因在所有模式里都记。
- [ ] 协议 v4：对局块的两个基地分数和结束原因、计分板的占领、开局消息的基地都能往返；越界和截断都报 `ProtocolException`；满员标准战快照小于不可靠包上限。
- [ ] 服务器：标准战开局要基地；快照带基地分数（看不见进攻车的一方也收到）；开局消息带基地；中途加入进来一辆新车；结束时打 `match ended: …`；`Tank.Server --mode standard`。
- [ ] 客户端：房间显示“标准战”；比分板“我方存活 / 敌方存活”；比分板下的进度条（己方基地被占红色、在涨时闪）；计分板“占领”列（只在标准战）；结算原因七种；地上两个圈（己方蓝灰、对方红，抖动渐隐的边）；画廊 `standard`、`capture`。
- [ ] 截图逐张看过：画廊、两张图上的两个圈、跟车镜头、非标准战没有圈、两实例对局。
- [ ] 两个实例在河谷和试驾场地图上各打完一局标准战（`pair4b.sh`）：两端进局、结算、回房间，没有异常。
- [ ] 文档 `docs/01、03、04`、README 已按代码更新；`docs/PROGRESS.md` 的“5B 节点记录”完整。
- [ ] 每批两路审查和跟进提交都做了；squash 合入本地 main，合入后 main 上全部测试通过、0 警告；没有推远端。
- [ ] 用户试玩放在整个阶段 5 结束后。

---

## 与设计的偏差

写计划时按现在的代码定下来的做法，和阶段 5 设计字面不一样的地方：

1. **河谷两基地相距约 983 m，不是约 750 m**：5A 的两队出生区中心相距 899 m（5A 批次 B 记录），按用户定的做法不挪出生点，基地放在各自出生区后方 40～80 m 的平地上，所以两基地约 1000 m。设计 §3.2 的“约 750 m”作废，测试按 950～1020 m 查。
2. **“空旷”的具体规则**：圆心 12 m 内没有岩石、房子、墙、栅栏；圆（加 2 m）里没有房子、墙、栅栏、树干、水，草丛的圆和基地的圆不相交；外环里允许至多 4 块岩石。原因：河谷队 0 的出生点在台地崖脚的岩石带旁，出生点外 40～80 m 内没有一块完全没有岩石的平地圆（选中的位置外环有 3 块）；试驾场地图 88 块岩石散得很匀，同样找不到（两个基地外环各 1 块）。岩石在外环只是守方的掩体，不影响进圈。
3. **“后方”按 60° 算**：基地在“本队出生区中心背对敌队出生区中心”的方向两侧 60° 以内、且离敌队比本队出生区远。河谷队 0 的正后方（西北）是台地崖，基地在出生点西南（偏 54°）；试驾场地图两个基地偏 42°、45°（正后方那块地上有草丛片）。试驾场地图的基地离出生点 51 m、44 m，不是“约 40 m”。
4. **中途加入**：设计 §3.1 说标准战中途加入“和团队歼灭一样”进来就是一辆新车——现有代码和 `docs/01` 里团队歼灭**不能**中途加入（只有团队对战能）。本计划按设计对标准战本身的要求做：标准战能中途加入（进人少的队、默认中坦、一辆新车），团队歼灭不变。原计划的已知漏洞（阵亡的玩家退回菜单或断线超过 30 秒宽限后再进，作为新座位拿到一辆新车）在任务 6 里堵上了：服务器在没有重生的模式里按重连令牌记下这一局拿过车的玩家（`BattleServer._hadTank`，开局和收局时清空），座位释放后凭同一令牌回来的，`HandleHello` 给座位、保留令牌，但不放进对局，在房间里等下一局（日志 `already had a tank in this match`）；宽限内回来照旧拿回原座位和原来的车（或残骸）。活着离开再回来也一样不给新车（`MatchRules.Leave` 在没有重生的模式里本来就算出局）。令牌要客户端带回来：客户端 `SeatTokens`（`Online/Logic/`）按房间地址（规整过：小写、去括号、IP 标准写法、`localhost`/`::1` 算 127.0.0.1）记住上次的令牌，`Main.Join` 再进同一房间时带上。这对四种模式都生效（以前从菜单进房间不带令牌，只有自动重连带）：团队对战、团队歼灭、混战里宽限内从菜单回来，同样拿回原座位和原来的车，不占第二个座位——这是修正，保留。等在房间里的人不让对局继续：对局里一个人类玩家都没有了（宽限内断开的还算在里面）就结束，和以前“没人了”一样。剩下的限制：重启游戏（令牌只在内存里）或改过的客户端不带令牌，仍作为新玩家拿到新车；不按名字认人——名字能改、会重名，误伤新玩家。写进 `docs/03` 的已知限制。
5. **线上多了结束原因和每人的占领分**：设计 §3.3 只列了两个基地分数。结算要写“占领了敌方基地 / 全歼敌人 / 时间到”等，客户端从分数和存活数推不干净，所以对局块加 2 位 `EndReason`（所有模式都填）；计分板的“占领”列要每人“本局攒过的最高分”，所以 `PlayerStats` 加 7 位 `Capture`。但对局进行中服务器只对己方（自己和队友）发真数，敌方各行发 0，对局结束（`Phase == Ended`）后才对所有人发真数：站在基地里不算被发现（设计 §3.3），敌方的占领分在涨会告诉守方圈里是哪个玩家、开的什么车。看的人的队按座位的队，和“被发现”灯一样（阵亡后观战也是本队）。写的一方把两个基地分数和每人的占领都封顶 100，不会写出读的一方拒收的值，也不会被 7 位截断。
6. **两种同时获胜的平局文字**：设计只给了“时间到，平局”。双方同 tick 全灭写“双方同归于尽”（和团队歼灭已有的说法一致），双方同 tick 占领写“双方同时占领了对方基地”。同一队既占领又全歼对方时按“占领了敌方基地”。
7. **整数计分**：每辆车按 tick 攒（30 tick = 1 分），基地满 3000 tick 获胜；显示和线上向下取整，所以 100 只在真正占下时出现；单车封顶 3000 tick。
8. **挨打那一 tick 不攒分**：被清零的车同一 tick 不再 +1（结束时正好是 0）。伤害包括队友误伤和溅射；保护期内挨打记 0 伤害，不清零。残骸不算守军、也不攒。
9. **离开对局的车带走它的分**：玩家离开（车被移出仿真）后，它攒的分从基地分数里去掉。
10. **闪烁的条件**：己方基地的条只要分数大于 0 就是红色；分数在 1.5 秒内涨过时闪（停涨后 1.5 秒内停，降了立即停）。设计写“己方基地被占时进度条为红色并闪烁”，这里把“被占时”定为“分数在涨”。
11. **基地不进地图编码**：基地是 `MapDefinition`（仿真常数）的一部分，开局消息只在标准战时带上，`MapData` 和地图指纹不变；非标准战的开局消息不带基地，客户端也就不画圈。服务器在没有基地的地图（只有测试地图会这样）上拒绝开标准战，原因 `the map has no bases`。
12. **别的版本的房间带着不认识的模式也列出来**：局域网发现的回复里，只有版本和我们相同时才检查模式字节；别的版本的房间照样列出、写“版本不符”（否则 v4 的标准战房间会让 v3 客户端整条丢掉，反之亦然）。
13. **圈的尺寸**：设计只说“圈”。画成从半径 − 1.6 m 到半径 + 0.2 m 的一条带，外缘 0.4 m 实线、向内抖动渐隐，抬高 0.3 m，160 段、内中外三圈顶点（批次 C 跟进按坦克高度和出生距离的截图从 0.2 m 实线、抬高 0.15 m、两圈顶点改来）。
14. **计分板“占领”列的位置**：只有标准战挪列（击毁 560、阵亡 640、占领 720、状态 790），其他模式的列位置和截图不变。
15. **机器人**：标准战里机器人照旧在路点之间直线开、打看得见的敌人，不会去占领或回防（5C）。所以两实例验收的标准战多半以超时结束；全灭、占领两种结束靠无画面测试和网络测试证明。
