# 阶段 5D：小地图 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 对局和试驾场的右下角加一张小地图（1600×900 基准下 240 × 240、离边 24），按 M 放大成全屏（居中、留边距、背景压暗），再按 M 或 Esc 关闭。底图在客户端按场地数据烘一次：地形明暗、水、公路和土路、沙地、树林（按树冠的密度）、房子、墙；房子塌、墙倒时只重画受影响的小块。图标每帧更新：两个基地（标准战：圈，己方蓝灰、对方红，有分时画进度弧）、自己（橙色箭头指车头，加淡色扇形表示镜头朝向）、队友（蓝灰色，四种车型四种小形状，被击毁画灰叉）、敌人（红色，只画当前快照里有的）；观战时箭头换到被跟随的车，自由视角时换成镜头焦点的圈。画廊 `--hud-demo minimap`、`map-full`。

**Architecture:** 引擎无关的部分都在新目录 `src/Tank.Client/scripts/Minimap/Logic/`（命名空间 `Tank.Client.Minimap`，Art 测试自动编进去）：`MinimapArea`（世界 ↔ 图上坐标）、`MinimapGround`（静态底图：地形、水、地面种类、树林，只读、可以在后台线程烘）、`MinimapImage`（底图加房子和墙，按 32 像素一块局部重画）、`MinimapFrame` + `MinimapFeed`（这一帧要画的图标，只从快照里的车、基地、自己的状态和镜头来）、`MinimapGlyphs`（箭头、四种形状、叉、扇形、进度弧的多边形）、`MinimapStyle`（颜色和尺寸）。Godot 部分在 `Minimap/`：`MinimapView`（一个 `Control`，画纹理和图标）和 `MinimapLayer`（一个 `CanvasLayer`：右下角的小图、全屏图和压暗的底、M/Esc、上传纹理、局部重画）。`MatchMain` 和 `SandboxMain` 每帧填一个 `MinimapFrame` 交给它；`HudGallery` 用预制的帧和河谷底图。仿真、联网不动。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9.3、Godot 4.7.2 .NET 版。只做 macOS。

**设计依据：** [阶段 5 设计](../specs/2026-10-22-stage5-design.md) 的 §5（5D 小地图）、§6 的 5D 测试（底图和图标的截图；单元测试确认小地图只画快照里有的敌人；全屏地图截图）和“每个小阶段”的通用要求、§7 的“不做”（最后位置残影、发信号）；§1 用户的选择照用（小地图做标准版）。参考：[5B 计划](2026-10-26-stage5b-standard.md)（客户端任务的格式、HUD 画廊、截图）、[5C 计划](2026-10-27-stage5c-bots.md)（命令、约定）。

**计划的深度：** 和 5A～5C 一样，不是逐字代码。每个任务写清：改哪些文件、对外接口（确切签名）、规则和算法要点、要先写的失败测试（具体输入和期望值）、命令和验收、提交说明。实现代码由执行者按 TDD 写（先写测试、确认失败、实现、确认通过）。接口一律以本文“共享接口”一节为准；实现中发现接口要改，先改本文这一节再动代码，并在跟进提交里说明。

---

## 执行方式

- **分支和目录**：`feat/stage5d-minimap`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage5d`（从 main `12dd2f0` 拉出，第一个提交是本计划）。下文的相对路径都相对于它。
- **逐任务派子代理**（superpowers:subagent-driven-development）：一个任务一个提交。派之前用 `python3 /Users/murphy/Desktop/dev/BattleCity-dev-scripts/brief5d.py N` 生成 `plan5d/brief-N.md`（本文的“执行方式”“执行前须知”“共享接口”和任务 N 全文），交给实现子代理读。提交前五个项目 0 警告、四个测试项目全部通过。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照阶段 5 设计 §5、§6 的 5D 部分、§7 和本计划，列“需求 → 代码 → 测试”表），一个做代码质量审查（正确性、坐标方向、每帧不分配、线程（后台烘底图只读不变的数据）、Godot 资源的释放、命名、注释、边界）；按两份意见做**一个**跟进提交 `fix(<scope>): batch X review follow-up — …`。
- **每批之后记进度**：在 `docs/PROGRESS.md` 新建并维护“5D 节点记录”表（放在“5C 节点记录”上面；列：# / 节点 / 状态 / 提交 / 说明；说明里写各任务提交号、跟进提交号、测试数变化、截图目录和看到了什么、实测数字（烘图耗时、局部重画的块数）、遗留问题），同时更新“当前位置”的“进度”“下一步”和“阶段总览”里阶段 5 那一行。随跟进提交一起提交，或单独提交 `docs: progress — 5D batch X`。第 0 行“设计 + 计划”写本计划的提交号。
- **顺序**：A → B → C，批内按任务号。B（Godot 视图和接线）要用 A 的全部类型，不并行。
- **用户试玩**在整个阶段 5（5A～5D）做完之后：最终验收清单全部打勾后 squash 合入本地 main（`git checkout main && git merge --squash feat/stage5d-minimap && git commit`，说明 `feat: stage 5D — minimap: baked map, live icons, full-screen map on M`，末行 `Tests: …`），不推远端。合入后在 main 上再跑一遍四个测试项目和 0 警告检查，然后告诉用户阶段 5 可以试玩了。
- **只做 macOS**：不写 Windows、Linux 专门的代码路径，不打其他平台的包。
- 设计已确认，中途不问用户，做完汇报。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage5d
```

**基线截图**（`build/` 在 `.gitignore` 里）：`build/shots/base/` 已经从 5C 拷好（六张试驾场回归图 heavy kill lamp orbit patrol wall，加 match-loopback 和两个 log）。任务 1 第一步 `ls build/shots/base` 确认在。任务 6 起试驾场右下角多了小地图，比较时要扣掉小地图那一块（见任务 6）。

**构建与测试**（这台机器上 `dotnet test` 的输出是中文，`dotnet build` 的汇总行是英文）：

```bash
# 0 警告检查：每行应为 "0 Warning(s) 0 Error(s)"（或中文 "0 个警告 0 个错误"）
for p in src/Tank.Sim/Tank.Sim.csproj src/Tank.Net/Tank.Net.csproj src/Tank.Client/Tank.Client.csproj \
         src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do
  echo "$p: $(dotnet build $p 2>&1 | grep -E '警告|错误|Warning\(s\)|Error\(s\)' | tr '\n' ' ')"; done
for t in Sim Net Art Update; do echo "$t: $(dotnet test tests/Tank.$t.Tests 2>&1 | grep -E '通过!|失败!')"; done
dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~Minimap" 2>&1 | tail -5     # 只跑某些类
```

- 成功的行形如 `已通过! - 失败: 0，通过: 1137 …`；`grep -E '通过!|失败!'` 两种都能抓到。
- **改动前的测试数**（本计划写作时在 `12dd2f0` 上）：Sim 1100、Net 294、Art 1137、Update 42；五个项目 0 警告。每个任务的提交说明末行写测试数变化，例如 `Tests: Art 1137 → 1160.`；下文各任务给的数是“至少”，以实际为准。本阶段不改 `Tank.Sim`、`Tank.Net`，Sim 和 Net 的数不该变。
- 以往的偶发：构建完立刻跑 Sim 偶尔有 1 条失败、重跑都过。遇到时重跑一次，两次都失败才算真失败，并在进度里记下是哪条。
- 客户端项目 `Tank.Client.csproj` 不把警告当错误（`TreatWarningsAsErrors=false`），“0 警告”靠上面的 grep 检查；其他项目由 `Directory.Build.props` 的 `TreatWarningsAsErrors=true` 保证。
- **Art 测试编进来的客户端代码**（`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`）：`scripts/Art/Mesh/**/*.cs`、**任何** `Logic/` 目录下的 `**/Logic/**/*.cs`、`Art/Palette.cs`、`Bootstrap/LaunchOptions.cs` 等。本阶段新的引擎无关代码一律放进 `scripts/Minimap/Logic/`（命名空间 `Tank.Client.Minimap`），不用改 csproj；这些文件里不能 `using Godot`。Art 测试项目引用 `Tank.Sim` 和 `Tank.Net`，也编进了 `tests/Shared/*.cs`（`Allocation.AssertNone`，命名空间 `Tank.Testing`）。
- Art 测试里已经有开回环主机的例子（`LoopbackSessionTests`：`BattleSession.HostLoopback(...)` + `AutoRoom` 每帧推进），任务 3 的“不泄露”整合测试照它写。
- 河谷的场地在 Art 测试里用 `MapCatalog.Riverland.NewBattlefield()`（第一次生成要一两秒，测试类里用一个静态 `Lazy` 共用；要改物件状态的测试各自 `NewBattlefield()`，它本来就给新的一份）。试驾场地图 `MapCatalog.Sandbox.NewBattlefield()`。
- **坐标方向**：仿真的地面坐标是 (x, z)；`Angles.Forward(yaw) = (−sin yaw, −cos yaw)`，所以 yaw 0 朝 −z。“map” 镜头预设从正上方看、上方是 −z（`CameraPresets.UpFor`），小地图跟它一致：**图上向右是 +x，向下是 +z，图像第 0 行是 z 最小的一行**；yaw 0 的箭头朝上，yaw 增大时逆时针转（yaw = π/2 朝 −x，即朝左）。

**Godot**（开发脚本在 `/Users/murphy/Desktop/dev/BattleCity-dev-scripts/`，见它的 `README.md`）：

```bash
S=/Users/murphy/Desktop/dev/BattleCity-dev-scripts
W=/Users/murphy/Desktop/dev/BattleCity-stage5d
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后先编译：Godot 命令行不编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs 后生成 .uid，和源文件一起提交
mkdir -p build/shots/5d-tNN                                 # build/ 在 .gitignore 里
# HUD 画廊（纯色底上的预制帧）：SCENE 传空字符串
$S/godot_run.sh $W "" 60 --hud-demo minimap --screenshot $W/build/shots/5d-tNN/minimap.png --screenshot-at 3 --quit-after 4
# 主场景开房、自动开局（真对局，房主线程上的服务器）
$S/godot_run.sh $W "" 150 --host --mode standard --map riverland --bots 9 --autostart --autodrive \
  --screenshot $W/build/shots/5d-tNN/match.png --screenshot-at 60 --quit-after 70
ls -la build/shots/5d-tNN/                                  # 成败看文件在不在（脚本只打印含 screenshot/error 的行）
# 试驾场六张回归截图，OUTDIR 相对于工作目录；比较时扣掉右下角的小地图（任务 6 起）
$S/baseline4b.sh $W build/shots/5d-tNN-sandbox
python3 $S/pngdiffdirs.py build/shots/base build/shots/5d-tNN-sandbox --ignore-minimap
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束都要确认没有残留的 Godot 进程
```

- 截图路径必须是绝对路径；`--screenshot` 一次运行只截一张；每张都要人工看（用 Read 打开 PNG），在进度说明里写看到了什么。
- 河谷第一次生成在 Debug 客户端里要几秒；`--autostart` 等地图就绪再开局，超时给 90 秒以上。画廊的河谷底图在 `_Ready` 里同步生成和烘（长的第一帧），`--screenshot-at` 给 3 秒以上。
- `pngdiffdirs.py --ignore-minimap`：写计划时已加进开发脚本——把每张图右下角小地图的方框（按图宽 / 1600 缩放的 `HudLayout.Minimap` 再外扩 4 像素）里的像素排除在比较外，另外单独报告方框外的差异。没有这个参数时行为不变。

**提交**：

- 说明用英文一行，带范围前缀：`feat(client): …`、`test(client): …`、`fix(<scope>): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。最后一行 `Tests: Art 1137 → 1160.`（列出有变化的项目）。
- 作者用仓库配置（`火狐`），不加 `Co-Authored-By`，不推送远端。
- 只提交本任务的文件和它们的 `.uid`。别 `git add -A`。

---

## 文件结构

**客户端引擎无关 `src/Tank.Client/scripts/Minimap/Logic/`**（命名空间 `Tank.Client.Minimap`，Art 测试编进去）

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `MinimapArea.cs` | 新建 | 图上显示的地面方块（可开车的矩形扩成正方形）、世界 ↔ 单位坐标 ↔ 像素 | 1 |
| `MinimapStyle.cs` | 新建 | 底图和图标的颜色（全部取自 `Palette`）、光照方向、树林和明暗的系数、图标尺寸 | 1、3 |
| `MinimapGround.cs` | 新建 | 静态底图：每像素地形明暗、水、地面种类、树林密度；只读场地里不变的东西，可在后台线程烘 | 1 |
| `MinimapImage.cs` | 新建 | 底图 + 房子和墙（站着的、倒了的），记住每个砖石物件站没站着；`Refresh` 只重画变了的 32 像素块 | 2 |
| `MinimapFrame.cs` | 新建 | 一帧的图标：车（位置、阵营、车型、是否残骸）、基地（位置、半径、己方否、分数）、视点（箭头或圈、镜头朝向和视角）、自己的残骸；预分配，不分配 | 3 |
| `MinimapFeed.cs` | 新建 | 从对局的数据（`client.Others`、基地、`MatchBlock`）和试驾场的数据填 `MinimapFrame` | 3 |
| `MinimapGlyphs.cs` | 新建 | 图标的几何：箭头、四种车型形状、叉、扇形、进度弧的点（写进调用者给的缓冲） | 3 |

**客户端 Godot `src/Tank.Client/scripts/`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Minimap/MinimapView.cs` | 新建 | `Control`：在给定的方框里画底图纹理、基地、扇形、车、视点 | 4 |
| `Minimap/MinimapLayer.cs` | 新建 | `CanvasLayer`：右下角小图、全屏图（压暗的底、地图名、关闭提示）、`ToggleFull`/`CloseFull`、后台烘底图、纹理上传和局部重画 | 4 |
| `Match/Logic/HudLayout.cs` | 修改 | `HudBoxes` 加 `Minimap`、`FullMap`；常数 `MinimapSize`、`FullMapMargin` 等 | 3 |
| `Match/Logic/HudDemoFrames.cs` | 修改 | `HudDemo` 加可选的 `MinimapDemo`；新画廊屏 `minimap`、`map-full` | 4 |
| `Match/HudGallery.cs` | 修改 | 有 `MinimapDemo` 的屏：生成河谷、烘底图、加 `MinimapLayer` | 4 |
| `Driving/DriveInput.cs` | 修改 | 新动作 `MapToggle`（M），对局和试驾场共用 | 4 |
| `Bootstrap/LaunchOptions.cs` | 修改 | `--full-map`：场景起来后把全屏地图打开（截图用） | 3 |
| `Match/MatchMain.cs`、`Match/MatchMain.Camera.cs` | 修改 | 建 `MinimapLayer`、每帧填帧、M 和 Esc、结算时关、换场地实例时跟上 | 5 |
| `Match/MatchAutomation.cs` | 修改 | `--full-map` 打开全屏图 | 5 |
| `Sandbox/SandboxMain.cs`、`Sandbox/TargetRange.cs` | 修改 | 建 `MinimapLayer`、每帧填帧（自己、看得见的靶车）、M 和 Esc、`--full-map` | 6 |
| `World/BattleWorld.cs` | 修改 | 公开它建的 `DirtMask`（给底图的“哪里是裸地”），不改它怎么建 | 4 |
| `Cameras/OrbitCamera.cs` | 修改 | `GroundHeading()`、`HalfFov()`：扇形的朝向和半角，对局和试驾场共用 | B 跟进 |
| `Bootstrap/Logic/ShotPlan.cs`、`Bootstrap/CaptureRun.cs`、`Bootstrap/Main.cs` | 修改 | 到点的截图等底图（最多 20 s），`--quit-after` 跟着等 | B 跟进 |

**测试 `tests/Tank.Art.Tests`**

| 新文件 / 改动 | 任务 |
|---|---|
| `MinimapAreaTests`、`MinimapGroundTests` | 1 |
| `MinimapImageTests` | 2 |
| `MinimapFeedTests`（含回环主机的“不泄露”整合测试）、`MinimapGlyphsTests`；`HudLayoutTests` 加小地图和全屏图的方框；`LaunchOptionsTests` 加 `--full-map` | 3 |
| `HudDemoFramesTests` 加两屏 | 4 |
| `ShotPlanTests` 加等底图的三条；`MinimapGlyphsTests` 改图标尺寸、加基地弧在圈外 | B 跟进 |

**开发工具**（仓库外，写计划时已加）：`brief5d.py`；`pngdiffdirs.py` 的 `--ignore-minimap`。

---

## 共享接口

所有任务都按这里的名字和签名写。改接口先改这一节。

### 必须保持源码兼容的现有签名

- `HudBoxes` 是位置记录：只在**末尾**追加 `Minimap`、`FullMap` 两个参数（`HudLayout.For` 是唯一的构造处）。已有的测试和调用者不改。
- `HudDemo(string Name, MatchHudFrame Frame, FeedLine[] Kills, bool NetPanelOpen, bool ScoreboardOpen = false)` 末尾追加 `MinimapDemo? Minimap = null`；`HudDemoFrames.Names` 末尾追加 `"minimap"`、`"map-full"`，已有的屏不变（截图不变）。
- `LaunchOptions` 只加 `bool FullMap { get; init; }` 和参数 `--full-map`。
- `BattleWorld` 只加一个只读属性，不改建法（试驾场回归截图除了右下角不许变）。

### 坐标和底图（任务 1、2）

```csharp
namespace Tank.Client.Minimap;

/// <summary>
/// The square of ground the map shows: the playable rectangle (Border in from each edge of the terrain) grown to a square
/// about its centre. Unit coordinates run 0..1 across it: u = +x to the right, v = +z downwards (image row 0 is the
/// smallest z), as the "map" camera preset sees the ground.
/// </summary>
public readonly record struct MinimapArea(float MinX, float MinZ, float Size)
{
    public static MinimapArea Of(Battlefield field);
    public Vector2 ToUnit(Vector2 ground);          // (x, z) → (u, v); outside the square gives values outside 0..1
    public Vector2 ToGround(Vector2 unit);
    public float MetresPerPixel(int pixels) => Size / pixels;
}

/// <summary>
/// The map's still layer, baked once (stage 5 design §5): per pixel the ground's kind and grass (as the terrain shader
/// colours it), steep rock, water by depth, the forest by crown density, all lit by a hillshade from the north-west.
/// Reads only what never changes in a battle (terrain, surface, water, the trees' places), so it may be baked on a worker
/// thread while the scene goes on; houses and walls are <see cref="MinimapImage"/>'s.
/// </summary>
public sealed class MinimapGround
{
    public const int DefaultPixels = 1024;

    /// <param name="notGrassAt">The bare-ground mask, 0 (grass) … 1 (dirt, sand or road) at (x, z): the scenery's own
    /// (<c>DirtMask.At</c>), so grass shows where the 3D ground has it.</param>
    public static MinimapGround Bake(Battlefield field, Func<float, float, float> notGrassAt, int pixels = DefaultPixels);

    public MinimapArea Area { get; }
    public int Pixels { get; }
    /// <summary>Pixels × Pixels × 4 bytes, RGBA, sRGB, row-major from the top row (smallest z); alpha 255. Don't change it.</summary>
    public ReadOnlySpan<byte> Rgba { get; }
}

/// <summary>
/// The ground plus the houses and walls as they stand (stage 5 design §5): standing ones in their colours with an
/// outline, fallen ones as faint rubble. Keeps every house's and wall's standing; <see cref="Refresh"/> repaints only the
/// tiles (<see cref="TileSize"/> pixels square) a prop that rose or fell touches, from the ground up, so the result is
/// always what a whole repaint would give.
/// </summary>
public sealed class MinimapImage
{
    public const int TileSize = 32;

    public MinimapImage(MinimapGround ground, Battlefield field);   // paints everything once

    public MinimapGround Ground { get; }
    public int Pixels => Ground.Pixels;
    public byte[] Rgba { get; }               // the picture the view uploads; changed in place by Refresh

    /// <summary>
    /// Brings the picture up to <paramref name="field"/> (the same map; a resync may hand a new instance): nothing when
    /// its <see cref="Battlefield.StateVersion"/> and instance are the ones last seen, else the tiles of every house or
    /// wall whose standing changed. Returns whether any pixel may have changed. No allocation.
    /// </summary>
    public bool Refresh(Battlefield field);

    /// <summary>The tiles the last <see cref="Refresh"/> repainted (tests and the log).</summary>
    public int LastRepaintedTiles { get; }
}
```

`Refresh` 认得的新实例必须和建图时的场地**共用同一个物件数组**（`Battlefield.Fresh()` 的副本就是这样）；物件数组不同（比如对局开始消息带着地图、`map.Build()` 新建的场地，哪怕是同一张图）会抛 `ArgumentException`：调用方这时要新建 `MinimapImage`（地图没变时底图可以照用）。

```csharp
namespace Tank.Client.Art.Meshes;

public static class GroundCover   // 已有：地面着色器层权重的 C# 孪生
{
    /// <summary>
    /// Weight (0..1) the terrain gives its rock layer at a point: the same noisy slope <see cref="GrassWeight"/> reads, so
    /// the two agree on where rock takes over. <paramref name="normalY"/> is the surface normal's Y.
    /// </summary>
    public static float RockWeight(float x, float z, float normalY);   // GrassWeight 也改用它，两者一致
}
```

### 图标（任务 3）

```csharp
namespace Tank.Client.Minimap;

public enum MinimapShape : byte { Light, Medium, Heavy, Td }   // TankCatalog.All's order

public readonly record struct MinimapTank(Vector2 Position, Side Side, MinimapShape Shape, bool Destroyed);

/// <summary>A base: <paramref name="Points"/> 0–100 is how far it has been taken (MatchBlock.BaseProgress of its team).</summary>
public readonly record struct MinimapBase(Vector2 Centre, float Radius, bool Own, byte Points);

public enum EyeKind : byte { None, Arrow, Ring }

/// <summary>
/// Where the player looks from: an <see cref="EyeKind.Arrow"/> on a tank (the own, or the one followed) pointing along
/// <paramref name="Yaw"/>, or a <see cref="EyeKind.Ring"/> on the free camera's focus (or the own wreck's close-up); the
/// fan opens along <paramref name="CameraYaw"/> by ±<paramref name="HalfFov"/> (the camera's horizontal half-angle).
/// </summary>
public readonly record struct MinimapEye(EyeKind Kind, Vector2 Position, float Yaw, float CameraYaw, float HalfFov);

/// <summary>One frame's icons, filled again every frame; nothing here allocates once built.</summary>
public sealed class MinimapFrame
{
    public const int MaxTanks = 32;          // headroom over BattleProtocol.MaxPlayers (10) and the sandbox's targets; one more is dropped (Debug.Assert)
    public const int MaxBases = 2;

    public ReadOnlySpan<MinimapTank> Tanks { get; }
    public ReadOnlySpan<MinimapBase> Bases { get; }   // 0 or 2
    public MinimapEye Eye { get; set; }
    public Vector2? OwnWreck { get; set; }   // the own wreck while the eye is elsewhere (dead, watching): an orange cross

    public void Clear();                      // tanks, bases, eye (None) and own wreck
    public void AddTank(in MinimapTank tank); // ignored past MaxTanks
    public void AddBase(in MinimapBase b);    // ignored past 2
}

public static class MinimapFeed
{
    /// <summary>TankCatalog.All's index (an entity's Spec) as a shape; an unknown index draws as Medium.</summary>
    public static MinimapShape ShapeOf(int specIndex);
    public static MinimapShape ShapeOf(TankSpec spec);

    /// <summary>
    /// Every tank in <paramref name="others"/> — the snapshot's, which the server has already filtered to what the
    /// player's team may see (net design §5.5) — and nothing else: no scoreboard row, no memory of earlier frames. Sides
    /// by <see cref="MatchColors.SideOf"/>; the tank with id <paramref name="eyeTankId"/> (the one followed, which
    /// carries the arrow) is left out; 0 leaves none out.
    /// </summary>
    public static void AddOthers(MinimapFrame frame, IReadOnlyList<RemoteTank> others, BattleMode mode, byte ownTeam, int eyeTankId);

    /// <summary>A standard match's two bases (none when <paramref name="bases"/> is null or empty), points from <paramref name="match"/>.</summary>
    public static void AddBases(MinimapFrame frame, IReadOnlyList<TeamBase>? bases, byte ownTeam, in MatchBlock match);
}

/// <summary>Icon geometry in map-view pixels (y down), written into caller buffers; see the task for the shapes.</summary>
public static class MinimapGlyphs
{
    public const int ArrowPoints = 4, FanPoints = 12, ArcMaxPoints = 33;
    public const int DiamondPoints = 4, OctagonPoints = 8, SquarePoints = 4, TrianglePoints = 3;   // Light, Medium, Heavy, Td
    public const int ShapeMaxPoints = OctagonPoints;
    public static int Arrow(Vector2 centre, float yaw, float size, Span<Vector2> into);        // returns the count written
    public static int Shape(MinimapShape shape, Vector2 centre, float size, Span<Vector2> into);
    public static (Vector2 A, Vector2 B, Vector2 C, Vector2 D) Cross(Vector2 centre, float size);   // two strokes A–B, C–D
    public static int Fan(Vector2 apex, float cameraYaw, float halfFov, float length, Span<Vector2> into);
    /// <summary>The progress arc: from 12 o'clock clockwise through points/100 of a turn; 0 points gives 0 points written.</summary>
    public static int Arc(Vector2 centre, float radius, byte points, Span<Vector2> into);
    /// <summary>A heading on the map: yaw 0 is up (−y), yaw grows anticlockwise, as <c>Angles.Forward</c> on the ground.</summary>
    public static Vector2 Direction(float yaw) => new(-MathF.Sin(yaw), -MathF.Cos(yaw));
}
```

```csharp
namespace Tank.Client.Minimap;

/// <summary>
/// One map's icon sizes in base pixels: a tank shape's and a wreck cross's extent, the eye arrow's length, the fan's
/// radius, the smallest base ring; then the line widths (批次 B 审查后从常数挪进来，每张图各一套): the cross's strokes,
/// a base's ring and progress arc, every icon's outline, the eye's ring.
/// </summary>
public readonly record struct MinimapIconSizes(float Tank, float Arrow, float Cross, float FanLength, float BaseMinRadius,
    float CrossWidth, float RingWidth, float ArcWidth, float OutlineWidth, float EyeRingWidth)
{
    public const float ArcGap = 0.5f;
    public float ArcRadius(float ringRadius) => ringRadius + RingWidth / 2 + ArcGap + ArcWidth / 2;   // 进度弧在圈外，不压圈
    public float FillRadius(float ringRadius) => ringRadius - RingWidth / 2;                          // 圈里的淡色圆盘
}

public static class MinimapStyle   // 底图的系数和派生色（任务 1、2）之外，图标的：
{
    public static readonly MinimapIconSizes CornerIcons;   // 小图：10、15、9、40、9；线宽 2.5、2.5、3、1.5、2
    public static readonly MinimapIconSizes FullIcons;     // 全屏：15、22、13、90、12；线宽 3、3、4、2、2.5
    public static MinimapIconSizes Icons(bool full);
    public static Vector4 TankColor(Side side, bool destroyed);   // rgb + 不透明度：自己橙、队友蓝灰、敌人红；队友残骸 UiDim、敌人残骸红 55%
    public static Vector3 BaseRing(bool own);                     // 己方 AllyBody、对方 EnemyBody
    public static Vector3 BaseArc(bool own);                      // 比分板下进度条的颜色（不闪）
    public static readonly Vector3 Eye;                           // 视点（箭头、圈）和自己的残骸叉：TankBody 橙
    public static readonly Vector3 Fan;                           // 扇形：UiText
    public const float FanOpacity = 0.30f;
    public static readonly Vector3 IconOutline;                   // UiOutline（宽度见 MinimapIconSizes.OutlineWidth）
    public const float EyeRingShare = 0.5f;                       // 视点圈的半径 = 箭头长 × 0.5
    public const float BaseFillOpacity = 0.25f;                   // 基地圈里的圆盘：圈的颜色、25% 不透明
}
```

`Side` 是 `Tank.Client.Match.Side`（`MatchColors.cs`），`RemoteTank`、`MatchBlock` 来自 `Tank.Net.Battle`，`TeamBase`、`BattleMode` 来自 `Tank.Sim.Modes`，`TankSpec`、`TankCatalog` 来自 `Tank.Sim.Vehicles`，`Vector2` 是 `System.Numerics.Vector2`。

### 布局（任务 3）

```csharp
// HudLayout
public const float MinimapSize = 240f;          // the corner map, 24 from the right and bottom edges (Margin)
public const float FullMapMargin = 64f;         // the full map: the largest square that leaves this much above and below (and at the sides)
public const float FullMapHintHeight = 28f;     // under the square: "M / Esc 关闭"
// HudBoxes gains, at the end: HudRect Minimap, HudRect FullMap
```

`Minimap = (size.X − Margin − MinimapSize, size.Y − Margin − MinimapSize, MinimapSize, MinimapSize)`；`FullMap` 是边长 `min(size.X, size.Y) − 2·FullMapMargin` 的正方形，水平居中，顶在 `FullMapMargin`（地图名在方框上沿里面，提示在方框下面 `FullMapHintHeight` 的那一条里）。

### Godot 视图（任务 4）

```csharp
namespace Tank.Client.Minimap;

public partial class MinimapLayer : CanvasLayer
{
    /// <summary>Starts baking <paramref name="field"/>'s ground on a worker thread (the corner shows an empty panel until it is done).</summary>
    public void Build(Battlefield field, Func<float, float, float> notGrassAt, string? mapName);
    /// <summary>The gallery's: an already baked ground, shown at once.</summary>
    public void BuildBaked(MinimapGround ground, Battlefield field, string? mapName);
    /// <summary>Once a frame: the frame to draw; <paramref name="field"/> as the scene has it now (redraws the houses and walls that changed).</summary>
    public void Show(MinimapFrame frame, Battlefield field);
    /// <summary>The picture is uploaded and has been drawn (uploaded in an earlier frame): scripted screenshots wait for it.</summary>
    public bool HasPicture { get; }
    public bool FullOpen { get; }
    public void ToggleFull();
    public void CloseFull();
    /// <summary>Hides both maps (the results screen) or shows the corner one again.</summary>
    public void SetHidden(bool hidden);
}
```

`MinimapLayer.Layer = 2`（和 `MatchHud` 同层，后加的在上面；暂停层是 3，盖得住它）。

### 视点（任务 5 加，批次 C 审查后补记）

```csharp
namespace Tank.Client.Minimap;   // Minimap/Logic/MinimapEyes.cs，测试 MinimapEyesTests

public readonly record struct MinimapEyeChoice(MinimapEye Eye, Vector2? OwnWreck, int EyeTankId);

public static class MinimapEyes
{
    /// <summary>活着或固定机位：箭头在自己；跟随：箭头在被跟随的车（EyeTankId，AddOthers 跳过它）、自己的残骸橙叉；
    /// 残骸特写和自由视角：圈在残骸或镜头焦点上；扇形照画。</summary>
    public static MinimapEyeChoice Pick(bool hasTank, bool watching, SpectateState.Mode spectate, RemoteTank? followed, …);
    public static float HalfFov(float verticalFovDegrees, float aspect);   // 水平半视角
    public static float CameraYaw(Vector3 forward, Vector3 up);            // 镜头在地面上的朝向（俯视时用 up）
}
```

### 截图等底图、镜头的帮手（批次 B 审查后加）

```csharp
namespace Tank.Client.Bootstrap;

public enum ShotCall { None, Take, TakeWithoutMap }

public sealed class ShotPlan   // 已有；加：
{
    public const double MapWaitSeconds = 20;   // 到点的截图最多等底图这么久（从到点算起）
    /// <summary>到点且 mapReady → Take（一次）；到点、底图没好 → None 并 HoldingForMap，过了 MapWaitSeconds → TakeWithoutMap。</summary>
    public ShotCall TakeScreenshot(double time, bool mapReady);
    public bool TakeScreenshot(double time);   // 不等底图的旧写法 = TakeScreenshot(time, true) != None
    public bool HoldingForMap { get; }         // 上一次 TakeScreenshot 是否在等底图
    public bool QuitDue(double time);          // 等底图的时候 --quit-after 也跟着等
}

// CaptureRun.EndFrame(delta, time, bool mapReady)；TakeWithoutMap 时 GD.PrintErr("[minimap] screenshot taken before the map was baked")。
// MatchAutomation / SandboxAutomation：EndFrame(delta, time, bool mapReady)、bool HoldingScreenshot；
// 场景传 mapReady = 没有小地图层 || _minimap.HasPicture。MatchMain / SandboxMain.HoldingScreenshot：Main 按进程时钟的
// --quit-after 也等它。
```

```csharp
namespace Tank.Client.Cameras;

public partial class OrbitCamera   // 已有；加（对局和试驾场共用，替掉两个场景里各自的 CameraYaw / CameraHalfFov）：
{
    public float GroundHeading();   // MinimapEyes.CameraYaw(−basis.Z, basis.Y)：镜头在地面上的朝向（固定预设的也算）
    public float HalfFov();         // MinimapEyes.HalfFov(Fov, 视口可见矩形的宽 / 高)
}
```

---

## 任务总览

| 批 | 任务 | 内容 |
|---|---|---|
| A 逻辑 | 1 | `MinimapArea`、`MinimapStyle`、`MinimapGround`（静态底图） |
| | 2 | `MinimapImage`（房子和墙、局部重画） |
| | 3 | `MinimapFrame`、`MinimapFeed`（含“不泄露”测试）、`MinimapGlyphs`、布局、`--full-map` |
| B 视图和接线 | 4 | `MinimapView`、`MinimapLayer`、M 键动作、画廊两屏 |
| | 5 | 对局接线：每帧填帧、观战和自由视角、M/Esc、结算、换场地 |
| | 6 | 试驾场接线、回归截图扣掉小地图比较 |
| C 收尾 | 7 | 验收 |
| | 8 | 文档 |

---

## 批次 A：逻辑

### Task 1：坐标和静态底图

**Files:**
- Create: `src/Tank.Client/scripts/Minimap/Logic/MinimapArea.cs`、`MinimapStyle.cs`、`MinimapGround.cs`
- Test: `tests/Tank.Art.Tests/MinimapAreaTests.cs`、`MinimapGroundTests.cs`

**规则**

- `MinimapArea.Of(field)`：可开车的矩形 `[Border, SizeX − Border] × [Border, SizeZ − Border]`，边长取两边较长的，绕矩形中心扩成正方形（两张图都是正方形，扩不扩一样，但规则要对长方形也成立）。
- 每个像素取它中心点的地面 (x, z) 算颜色：
  1. **裸地颜色**按地面种类：`Road` → `Palette.Road`，`Sand` → `Palette.Sand`，`Dirt` → `Palette.Earth`，`Grass` → `Palette.Dirt`（草地上露出来的土，试驾场大部分地面就是这种）。
  2. **草**：法线由高度场中心差分算（步长一个地形格），`w = GroundCover.GrassWeight(x, z, normal.Y, notGrassAt(x, z))`，颜色 `lerp(裸地, MinimapStyle.Grass, w)`，`Grass = lerp(GrassDark, GrassLight, 0.55)`。和地面着色器同一条规则，所以草在小地图上和 3D 地面上长在同一处。
  3. **陡坡**：每个像素都按 `GroundCover.RockWeight(x, z, normal.Y)` 混向 `Palette.Rock`（`SmoothStep(Rock−4°, Rock+3°)`，坡度带着和 `GrassWeight` 同一个噪声，没有 ≥ 38° 的门槛），和地面着色器的岩石层一致。
  4. **明暗**：光从西北上方来 `L = normalize(−1, 1.6, −1)`（x、y、z），`shade = 0.72 + 0.42 · max(0, n·L)`，再按高度在图上各像素中心的高低之间加 ±6% 的亮度（越高越亮；只看图上这块，不看整个高度场）；乘到颜色上后夹到 0～255。
  5. **水**：`WaterLevel` 存在且地面低于水面时，深度 `d = level − h`，颜色 `lerp(WaterShallow, WaterDeep, SmoothStep(0.3, 3.0, d))`，不加明暗；水边 0.3 m 以内（`0 < d < 0.3`）混 50% 的 `Palette.Foam` 画出岸线。
  6. **树林**：每棵树（`PropKind.Tree`，**不管站没站着**：底图只烘一次，见偏差 2）在密度缓冲里按树冠半径 `prop.FoliageRadius` 加一个圆盘（中心 1、边缘 0.3、圆外 0，线性）。**树冠合拢**（偏差 12）：`crown = density > 0` 的像素做一次形态学闭运算（先膨胀后腐蚀），圆盘半径 `k = ceil(CanopyGapMetres / 每像素米数)`，`CanopyGapMetres = 3.3`（河谷 1024 像素下 3 像素）；膨胀和腐蚀都用两遍的精确平方欧氏距离变换（Felzenszwalb–Huttenlocher，O(n²)，不逐像素扫圆盘），图外的地面两边都不算。闭出来的范围在 `dilate(crowns, k)` 里：林子的外沿不会往外长超过 k 像素，孤树还是小点。每像素 `cover = max(min(1, density), 合拢 ? CanopyCover : 0)`，`CanopyCover = 0.85`，颜色 `lerp(颜色, MinimapStyle.Forest, 0.8 · cover)`；`Forest = lerp(Needles, Leaves, 0.4)` 再乘 0.78（比草深，树林一眼能看出来）。树林画在水上面不用管（树不长在水里）。
- 所有颜色来自 `Palette`（sRGB 的 0～1 向量），`MinimapStyle` 只放混合系数和派生色；字节 = `round(clamp01(c) · 255)`，alpha 255。
- `Bake` 只读地形、地面种类、水面、物件表（树的位置和半径），不读站立状态，也不改场地：可以在后台线程跑。不要用 `Parallel`（Godot 的线程池和测试都简单些），单线程。
- 性能：河谷 1024² 在 Release 下目标 ≤ 400 ms；测试只打印耗时并查宽松的上限（Debug、机器忙时 20 s，只防病态的变慢）。树的圆盘只扫它覆盖的像素。

**先写的测试**（`MinimapAreaTests`）

- 河谷：`Area.Size == SizeX − 2·Border`、`MinX == Border`；`ToUnit(ToGround(u)) == u`（容差 1e-5）；可开车矩形的四个角落在 (0,0)～(1,1)。
- 人造 300 × 200 的场地（`new Battlefield(HeightField…, props: [], border: 10)`）：正方形边长 280，`MinZ == −40`（可开车的 280 × 180，短边两头各扩 50）。
- `ToUnit` 的方向：(MinX + Size, MinZ) → (1, 0)，(MinX, MinZ + Size) → (0, 1)。

**先写的测试**（`MinimapGroundTests`，像素用 `Area` 反算到具体地点）

- 人造平地：全是 `Grass`、`notGrassAt` 恒为 0 → 每个像素都是草色乘同一个明暗（全图像素相同，且绿色分量最大）；`notGrassAt` 恒为 1 → 都接近 `Palette.Dirt` 乘明暗，过半的像素差 ≤ 1。
- 人造场地一半 `Road`（`notGrassAt` 在路上为 1）→ 路那半的像素接近 `Palette.Road` 乘明暗。
- 裸地的容差（上面两条）：蒙版为 1 时斑块噪声还能透过来的草，`|Grass − 裸地| · GroundCover.FlatGrassWeightRange(1).Most · 明暗 · 255`，再加 1（取整）；和 3D 地面一样。
- 方向：`z < 中线` 是路、其余是草 → 直接读图像的行：第 2 行是路、第 `Pixels − 3` 行是草（不经过 `ToUnit`）。
- 小规则：水深 0.1 m 的像素等于 `lerp(WaterShallow, Foam, 0.5)`；50° 的坡是 `Palette.Rock` 乘明暗。
- 人造场地有水：一个低洼处低于 `WaterLevel` 2 m → 那里是水色（蓝分量最大、和 `lerp(WaterShallow, WaterDeep, SmoothStep(0.3, 3, 2))` 每通道差 ≤ 3）。
- 明暗：一座朝西北的坡比朝东南的坡亮（各取坡中间一个像素，亮度和之差 > 30）。
- 树林：人造场地中间一片 30 棵树 → 那片中心的像素比没树的地方绿分量少、更暗，并且离最近的树冠 10 m 以外的像素和没树的场地逐字节相同；一棵孤树改动的像素都在树冠半径加 1 像素以内。
- 河谷的树林（共用一个 `Lazy` 烘好的河谷）：`RiverlandMap.Forests` 的每个矩形往里缩 `ForestJitter + 20 m`，里面 ≥ 90% 的像素是绿的（G > R + 20）且比平地上的草暗；离每个树干都比 `最大树冠半径 + (k + 1) · 每像素米数` 远的像素，和同一场地去掉树（地形、地面、水、其余物件不变）烘出来的逐字节相同。
- 河谷整图：`Pixels == 1024`、`Rgba.Length == 1024·1024·4`、alpha 全是 255；`RiverlandMap` 的河上某点（从 `RiverlandMap` 的常数或 `MapSites` 里挑一个已知在水里的点，比如浅滩 ford 中心）是水色，基地中心不是水；打印烘图耗时并断言 < 20 s。
- 同一输入烘两次逐字节相同（确定）。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~Minimap"`

**步骤**

- [ ] 写测试，确认因为类型不存在而编译失败
- [ ] 实现三个文件
- [ ] 测试通过；五个项目 0 警告；四个测试项目全过
- [ ] 提交

**提交**：`feat(client): minimap ground — area, palette and the still layer: hillshade, water, ground kinds, forest`，末行 `Tests: Art 1137 → N.`

### Task 2：房子和墙、局部重画

**Files:**
- Create: `src/Tank.Client/scripts/Minimap/Logic/MinimapImage.cs`
- Modify: `MinimapStyle.cs`（房子、墙、废墟的颜色和宽度）
- Test: `tests/Tank.Art.Tests/MinimapImageTests.cs`

**规则**

- 画的物件：房子（`PropKind.House`）、砖墙、石墙（`BrickWall`、`StoneWall`）。篱笆、灌木、岩石不画（偏差 6）。
- 站着的房子：脚印（`Obb2`）里填 `Palette.RoofTile`，再沿脚印边画 1 像素 `Palette.UiOutline`（混 60%）。倒了的房子：脚印里混 45% 的 `Palette.Plaster`（一片浅色废墟），不描边。
- 站着的墙：脚印沿 `AxisRight` 方向不变、厚度方向至少画 2 像素宽（墙只有几十厘米厚，1 m 一像素会画不出来），砖墙 `Palette.Brick`、石墙 `Palette.Stone`，不混。倒了的墙：同样的条混 35% 的 `Palette.Plaster`。
- 判断像素在不在脚印里：像素中心反算到地面，`Obb2` 的点内测试（加宽的是两个半宽里**较小**的那个，不假定是 `AxisForward`：`max(halfExtent, 1 px 对应的米数)`）。
- 记住每个砖石物件（按 `Battlefield.Props` 的下标）上次画的站立状态；建图时把每个砖石物件登记到它脚印外扩 1 像素后覆盖的块（`TileSize` = 32 像素，块号 → 物件下标列表，建一次）。
- `Refresh(field)`：同一个实例且 `StateVersion` 没变 → 返回 false，什么都不做。换了实例时要和建图时共用同一个物件数组（建图时记下 `field.Props` 的引用），否则抛 `ArgumentException`。否则逐个砖石物件比 `field.IsStanding(id)` 和记住的值，变了的把它的块标脏；每个脏块：从 `Ground.Rgba` 拷回这一块，再把登记在这块的所有砖石物件按当前站立状态画上（只画在这块里）；更新记住的值和 `StateVersion`、实例。块的拷贝和画都在预分配的数组上做，**不分配**（脏块标记用建图时分配好的 `bool[]` 和 `int[]`）。
- 物件只受血量变化不倒（`StateVersion` 变了但站立没变）→ 返回 false、0 个块。

**先写的测试**

- 试驾场地图：建 `MinimapImage`；挑一面砖墙中段的像素 → 是 `Brick` 色；挑一座房子（试驾场地图没有房子就用河谷）脚印中心的像素 → 是 `RoofTile` 色。
- 河谷：`field.SetDurability(houseId, 0)` 推倒一座房子 → `Refresh` 返回 true，`LastRepaintedTiles` 在 1～4 之间；整张图和一个新的 `new MinimapImage(ground, field)`（同一个 `ground`，场地是推倒后的）逐字节相同；变了的像素全在这座房子脚印外扩 1 像素的范围里。
- 把它立回去（`SetDurability(houseId, max)`）→ 再 `Refresh` 后和最初的图逐字节相同。
- 打一面墙但不打倒（`SetDurability(wallId, max − 1)`）→ `Refresh` 返回 false，0 个块。
- 什么都没变 → `Refresh` 返回 false；`Allocation.AssertNone` 下连续 `Refresh` 一个推倒又立起的物件（预热一轮后）不分配。
- 换一个实例（`field.Fresh()`，全部站着）给一个推倒过房子的图 → `Refresh` 后和全部站着的图逐字节相同；物件数组不同的同图场地 → 抛。
- 随机对照（批次 A 审查后加）：河谷和试驾场各取 `Fresh()`，5 个种子（测试里的 `System.Random`），每轮随机翻转约 30% 的房子和墙（站着的推倒、倒了的立起），`Refresh` 后和 `new MinimapImage(ground, field)` 逐字节相同；人造场地上一面斜着跨过块边的墙、一面压在房子上的墙，逐个推倒立起，每步都和整张重画相同。
- 小规则：站着的房子边上的像素是描边色；45° 的斜墙（两种轴向：薄的是 `AxisForward` 或 `AxisRight`）每行至少 2 像素、4 连通成一片。

**步骤**：同任务 1。

**提交**：`feat(client): minimap houses and walls — standing and fallen, tiles repainted when one rises or falls`，末行 `Tests: Art N → M.`

### Task 3：图标、布局和 `--full-map`

**Files:**
- Create: `src/Tank.Client/scripts/Minimap/Logic/MinimapFrame.cs`、`MinimapFeed.cs`、`MinimapGlyphs.cs`
- Modify: `MinimapStyle.cs`（图标的颜色和尺寸）、`src/Tank.Client/scripts/Match/Logic/HudLayout.cs`、`src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（参数和帮助文字）
- Test: `tests/Tank.Art.Tests/MinimapFeedTests.cs`、`MinimapGlyphsTests.cs`；`HudLayoutTests`、`LaunchOptionsTests` 加用例

**规则**

- `MinimapFeed.AddOthers`：对 `others` 里的每个实体（**只用它们**）：`Side = MatchColors.SideOf(mode, ownTeam, entity.Team, isOwn: false)`，`Shape = ShapeOf(entity.Spec)`，`Destroyed = entity.Has(TankFlags.Destroyed)`，位置用插值后的 `RemoteTank.Position`；`Id == eyeTankId` 的跳过。服务器只把本队看得见的敌人（和所有残骸、所有队友）放进快照，客户端收不到的就画不出来（阶段 4 设计 §5.5；`Viewer.Sees`）；这里不许读计分板、事件或以前的帧。
- `AddBases`：`bases` 为空什么都不加；每个基地 `Own = base.Team == ownTeam`，`Points = base.Team == 0 ? match.BaseProgress0 : match.BaseProgress1`（`BaseProgressN` 是 N 队基地被占的进度）。
- `ShapeOf(specIndex)`：0 Light、1 Medium、2 Heavy、3 Td，其他 Medium。`ShapeOf(TankSpec)` 按 `TankCatalog.All` 里的下标。
- 形状（`size` 是图标外接圆的直径）：Light 菱形（4 点）、Medium 圆（8 边形）、Heavy 正方形（4 点）、Td 尖朝下的三角形（3 点）——都不随车头转，方便一眼认车型；箭头是 4 点的“镖”形（尖、右后角、尾部凹口、左后角），尖朝 `Direction(yaw)`，长 = `size`。
- 叉：两条对角线，半长 `size/2`。
- 扇形：顶点 + 从 `cameraYaw − halfFov` 到 `cameraYaw + halfFov` 的 11 个弧上的点（共 12 个），弧半径 `length`。
- 进度弧：从 12 点钟方向顺时针（屏幕上 y 向下，所以角度从 −π/2 开始、往 +x 方向转）走 `points/100` 圈，点数 `1 + ceil(32 · points/100)`，0 分写 0 个点。
- `MinimapStyle` 里的图标尺寸（基准像素；批次 B 审查后放大，线宽也挪进 `MinimapIconSizes`，见“共享接口”）：小图 车 10、箭头 15、叉 9（线宽 2.5）、扇形长 40、基地圈最小半径 9、圈宽 2.5、弧宽 3、描边 1.5、视点圈宽 2；全屏图 车 15、箭头 22、叉 13（线宽 3）、扇形长 90、基地圈最小半径 12、圈宽 3、弧宽 4、描边 2、视点圈宽 2.5。颜色：自己 `Palette.TankBody`（橙）、队友 `Palette.AllyBody`、敌人 `Palette.EnemyBody`、队友残骸 `Palette.UiDim`、敌人残骸 `EnemyBody` 的 55% 透明度、自己的残骸橙色叉；扇形 `Palette.UiText` 的 30% 不透明度；基地圈己方 `AllyBody`、对方 `EnemyBody`，进度弧用 `MatchColors.FillColor(MatchColors.FillOf(bar, blinkDark: false))` 的颜色（己方基地被占是红，对方基地是蓝灰，和比分板下的进度条一致）；所有车图标描 `OutlineWidth` 宽的 `Palette.UiOutline`。
- 布局见“共享接口”的布局一节。
- `--full-map`：`LaunchOptions.FullMap = true`；帮助文字加 `--full-map（进场后打开全屏地图，截图用）`。

**先写的测试**（`MinimapFeedTests`）

- 只画快照里的：`others` = [队友 A（team 0）、敌人 B（team 1）]，`ownTeam` 0、团队战 → 两个图标，A 是 `Ally`、B 是 `Enemy`，位置等于各自的 `RemoteTank.Position`。
- 下一帧 `others` 只剩 A（B 出了视野）→ `Clear` 后再填，只有 A：没有残影。
- 各自为战：同队号的也是 `Enemy`。
- 残骸：`Flags = Destroyed` 的敌人 → `Destroyed = true`、`Side = Enemy`。
- 跟随：`eyeTankId = A.Id` → 只有 B。
- `MaxTanks` 之外的被忽略，不抛；Debug 下每丢一辆有一次 `Debug.Assert` 失败（测试临时换上计数的 `TraceListener`：Debug 5 次、Release 0 次）。
- 基地：两个基地、`ownTeam` 1、`BaseProgress0 = 37`、`BaseProgress1 = 0` → team 0 的基地 `Own = false, Points = 37`，team 1 的 `Own = true, Points = 0`；`bases = null` → 0 个。
- `ShapeOf`：四个下标、越界 → Medium；`ShapeOf(TankCatalog.Td) == Td`。
- `Allocation.AssertNone`：预热后 `Clear` + `AddOthers`（10 个车）+ `AddBases` 不分配。
- **不泄露的整合测试**（照 `LoopbackSessionTests`）：`BattleSession.HostLoopback`，团队战、试驾场地图、3 个机器人、种子 7，每帧推进并 `Clear` + `AddOthers(frame, client.Others, …)`，跑 20 虚拟秒；每帧断言：敌人图标数 == `client.Others` 里敌方实体数，且每个敌人图标的位置都能在 `client.Others` 里找到；并且至少有一帧计分板（`client.Latest.Players`）上活着的敌人比敌人图标多（证明确实有看不见的敌人、它们没被画出来）。如果 20 秒里每帧所有敌人都看得见，换种子或改成河谷再试，在提交说明里写用的是哪个。回环主机跑在虚拟时钟上，这个帧数每次都一样；测试用自己的端口 47105。

**先写的测试**（`MinimapGlyphsTests`）

- `Direction(0) == (0, −1)`、`Direction(π/2) ≈ (−1, 0)`。
- 箭头 yaw 0：尖是 y 最小的点，正好在 `centre + (0, −size/2)`；yaw π/2：尖在左边。
- 四种形状点数 4、8、4、3，互不相同；都在外接圆里（离中心 ≤ size/2 + 1e-4）。
- 扇形：12 个点，第一个是顶点，第 2 和最后一个点和顶点的夹角是 `2·halfFov`。
- 进度弧：0 分 0 个点；100 分首尾重合（一整圈）；50 分最后一个点在正下方 `centre + (0, radius)`；25 分最后一个点在右边 `centre + (radius, 0)`（顺时针）。
- 写进去的点不超过各自的常数（`ArrowPoints` 等）。

**先写的测试**（`HudLayoutTests`、`LaunchOptionsTests`）

- 1600×900：`Minimap == (1336, 636, 240, 240)`；和 `StatusPanel`、`KillFeed`、`SpectateBar`、`Scoreboard`、`CaptureBars`、`Results` 都不相交；`FitsIn`。2560×900、1600×1200 也一样不相交、贴右下角。小地图也在常驻块互不重叠的那一组里（比分牌、模式标签、击杀栏、F3 面板、状态面板），也不碰复活横幅。
- `FullMap`：1600×900 下边长 772、`X == 414`、`Y == 64`；下面还留着 `FullMapHintHeight`（`FullMap.Bottom + FullMapHintHeight ≤ size.Y`，因为 `FullMapHintHeight ≤ FullMapMargin`）；窗口小到放不下边距时边长是 0，不是负数。
- `--full-map` 解析为 `FullMap = true`，没有时 false。

**步骤**：同任务 1。

**提交**：`feat(client): minimap icons — frame, feed from the snapshot only, glyphs, layout and --full-map`，末行 `Tests: Art M → K.`

**批次 A 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 B：视图和接线

### Task 4：视图、图层和画廊

**Files:**
- Create: `src/Tank.Client/scripts/Minimap/MinimapView.cs`、`MinimapLayer.cs`
- Modify: `Driving/DriveInput.cs`（`internal static readonly StringName MapToggle = "map_toggle";`，`RegisterActions` 里 `AddKey(MapToggle, Key.M)`）、`World/BattleWorld.cs`（`public DirtMask? Ground…` 只读属性，名字照文件里的习惯，交出它已经建好的蒙版；没有蒙版的场地给 null，调用方用 `_ => 0f`）、`Match/Logic/HudDemoFrames.cs`、`Match/HudGallery.cs`
- Test: `HudDemoFramesTests` 加两屏

**`MinimapView`**（`Control`，鼠标穿透）

- `Setup(bool full)`，图标尺寸读 `MinimapStyle.Icons(full)`；`Draw` 的输入：纹理、`MinimapArea`、`MinimapFrame`，在自己的整个矩形里画。
- 世界 → 屏幕：`rect.Position + area.ToUnit(ground) · rect.Size`。
- 顺序：圆角深色底框（`HudStyle.RoundedBox`，`Palette.UiPanel` 85%，小图圆角 10、全屏 14）→ 底图（`DrawTextureRect`，内缩 4 像素；底图没烘好时只画底框）→ 基地（圈里填圈的颜色 25% 的圆盘到圈的内沿；圈 `DrawArc` 一圈，宽 `RingWidth`，半径 `max(按比例, 最小半径)`；进度弧宽 `ArcWidth`，画在圈**外** `ArcRadius(r) = r + 圈宽/2 + 0.5 + 弧宽/2`，不压圈——批次 B 审查后改）→ 扇形（`DrawColoredPolygon`）→ 残骸叉（宽 `CrossWidth`）→ 队友 → 敌人 → 自己的残骸 → 视点（箭头填色加描边，或圈：半径 = 箭头长的 0.5、宽 `EyeRingWidth`、橙色）。超出方框的图标夹到方框边（`Clip Contents` 打开，简单）。
- 多边形点用预分配的 `Vector2[]`（Godot 的 `Vector2`，每种一个，按最大点数分配，用 `MinimapGlyphs` 写进 `System.Numerics.Vector2` 的 `Span` 再拷），`_Draw` 里不 `new`。每帧 `QueueRedraw`（图标在动）。
- 纹理过滤：`TextureFilter = LinearWithMipmaps`（1024 缩到 232 不闪）。

**`MinimapLayer`**（`CanvasLayer`，`Layer = 2`）

- 子节点：右下角的小图 `MinimapView`；全屏组（压暗的 `ColorRect`：`Palette.UiBackground` 60%，全屏、鼠标穿透；全屏的 `MinimapView`；方框上沿里的地图名（18 号、`HudStyle` 描边字）；方框下 `FullMapHintHeight` 那一条里居中的 “M / Esc 关闭”（16 号、`UiDim`））。方框都按 `HudLayout.For(视口大小)`，窗口改大小时重排（同 `HudPanel`）。
- `Build(field, notGrassAt, mapName)`：`Task.Run(() => MinimapGround.Bake(...))`；`Show` 里看到任务完成就在主线程建 `MinimapImage`（会画一遍房子和墙，读当前站立状态）、`Image.CreateFromData(Pixels, Pixels, false, Rgba8, rgba)` + `GenerateMipmaps()` + `ImageTexture.CreateFromImage`。任务出错：`GD.PrintErr("[minimap] bake failed: …")`，小地图一直只画底框和图标（不崩）。
- `Show(frame, field)`：底图好了就 `image.Refresh(field)`，返回 true 时标脏、累计重画的块数；上传（`Image.SetData` + `GenerateMipmaps()` + `texture.Update(image)`）合并起来，**最多每 0.2 s 一次**，脏了没传的在下一个 0.2 s 到时再传一次（批次 B 审查后改：连着塌也只传几次）；每次上传打印一行 `[minimap] repainted N tiles`（N 是上次上传以来重画的块；房子塌、墙倒才有；验收靠它），每帧不格式化字符串。底图取到时打印 `[minimap] baked 1024 px in N ms (ready after M ms)`（N 烘图本身，M 从 `Build` 到取到）；烘了 5 秒还没好打印一次 `[minimap] still baking after 5 s`；把 frame 交给可见的那个视图。
- `HasPicture`：底图已上传、并且是在之前的帧上传的（截图读回的是上一帧画的，同一帧传上去还拍不到）。对局和试驾场的自动截图到点时，有小地图层而 `!HasPicture` 就等（最多 20 s，见“截图等底图”接口和偏差 13），`--quit-after` 跟着等。
- 后台任务挂一个 `OnlyOnFaulted` 的续体 `GD.PrintErr("[minimap] bake failed: …")`：没人再等的任务出错也被观察到，不会变成 UnobservedTaskException；`Show` 里取到出错的任务不再打印。
- 一个场景一张图：按 `Build` 时的 `field.Terrain` 引用认图；同一地形、物件数组不同的场地用同一个底图新建 `MinimapImage`；地形不同的场地 `GD.PrintErr` 一次、留着旧图（不错画）。
- `ToggleFull` / `CloseFull`：全屏开的时候小图藏起来；`SetHidden(true)` 两个都藏、并关掉全屏。
- `_ExitTree` 里先让两个视图放掉纹理引用（`MinimapView.DropTexture()`），再释放纹理和图像（`Dispose`），后台任务还没完的不等它（完成后结果没人要，GC 收走）。

**画廊**

- `HudDemoFrames`：新类型 `public sealed record MinimapDemo(string MapId, MinimapFrame Frame, bool FullOpen)`；屏 `minimap`：`Standard()` 的 HUD 帧 + 河谷的小图；屏 `map-full`：同样的帧、`FullOpen = true`。预制帧（河谷，`RiverlandMap.Bases`、`RiverlandMap` 的出生点常数）：
  - 两个基地，己方（team 0）被占 37、对方被占 62；
  - 自己：箭头在己方基地前 120 m、yaw 指向对方基地，镜头比车头往右偏 0.4 弧度、`HalfFov` 0.9；
  - 队友 4 辆：四种车型各一辆，其中一辆是残骸；
  - 敌人 3 辆在地图中间一带，一辆残骸；
  - 用到的点都在可开车的矩形里。
- `HudGallery`：`demo.Minimap is { } m` 时，`_Ready` 里 `MapCatalog.Get(m.MapId).NewBattlefield()`、`new DirtMask` 照 `BattleWorld` 的建法（`DirtMask.FromField(field)`，为 null 时 `_ => 0f`）、`MinimapGround.Bake`（同步），加 `MinimapLayer`，`BuildBaked(ground, field, 地图名)`，`FullOpen` 时 `ToggleFull()`；`_Process` 每帧 `Show(m.Frame, field)`。

**先写的测试**（`HudDemoFramesTests`）

- `Names` 末尾是 `minimap`、`map-full`，已有的名字和顺序不变；两屏都 `TryGet` 得到、`Minimap` 不为 null，`map-full` 的 `FullOpen` 为 true；预制帧里有 2 个基地、7 辆车（队友 4、敌人 3，残骸 2）、视点是箭头；每个点都在河谷可开车的矩形里。

**截图和验收**（`build/shots/5d-t4/`，都要看）：

```bash
$S/godot_run.sh $W "" 90 --hud-demo minimap  --screenshot $W/build/shots/5d-t4/minimap.png  --screenshot-at 3 --quit-after 4
$S/godot_run.sh $W "" 90 --hud-demo map-full --screenshot $W/build/shots/5d-t4/map-full.png --screenshot-at 3 --quit-after 4
$S/godot_run.sh $W "" 30 --hud-demo standard --screenshot $W/build/shots/5d-t4/standard.png --screenshot-at 1 --quit-after 2   # 已有的屏：没有小地图
```

看：底图认得出河、浅滩、公路、村子的房子、树林、沙丘；方向和 `--camera-preset map` 的俯视一致（河谷的河在图上的走向和俯视截图同向；需要时拍一张 `--host --map riverland --camera-preset map` 对照）；两个基地的圈和进度弧（己方红弧约 1/3 圈、对方蓝灰弧约 2/3 圈）；橙色箭头朝对方基地、扇形往右偏；四种形状认得出；全屏图居中、背景压暗、地图名和提示在。记进度。

**步骤**

- [ ] 写 `HudDemoFramesTests` 的用例，确认失败
- [ ] 实现视图、图层、画廊、M 键动作、`BattleWorld` 的属性
- [ ] Art 测试过；五个项目 0 警告；四个测试项目全过
- [ ] 三张截图看过
- [ ] 提交（含新 `.uid`）

**提交**：`feat(client): minimap view and layer — corner map, full map, gallery screens minimap and map-full`

### Task 5：对局接线

**Files:**
- Modify: `src/Tank.Client/scripts/Match/MatchMain.cs`、`MatchMain.Camera.cs`、`MatchAutomation.cs`

**规则**

- `BuildScene`：`_minimap = Add(new MinimapLayer())`，加在 `_hud` 之后；`Build(field, 蒙版.At 或 _ => 0f, MapText 里地图的名字)`（房间状态还没来时名字为 null，不画）。
- 每帧在 `_hud.Refresh` 之后填 `_minimapFrame`（字段，建一次）并 `_minimap.Show(_minimapFrame, field)`：
  - `MinimapFeed.AddBases(frame, _client.MatchInfo?.Bases, OwnTeam(own), _client.Match)`；
  - 视点：
    - 活着，或者有固定镜头预设：`Arrow`，位置和 yaw 用画出来的自己（`own.Position`、`own.Yaw`）；
    - 死了、在跟随别人（`_spectate.Current == Follow` 且找得到那辆车）：`Arrow` 放在那辆车上（插值后的位置和 yaw），`eyeTankId` = 它的 id，`OwnWreck = own.Position`；
    - 死了、自由视角：`Ring` 放在 `_focus`，`OwnWreck = own.Position`；
    - 死了、残骸特写：`Ring` 放在 `own.Position`，`OwnWreck = own.Position`；
    - 还没有自己的车：`None`；
    - `CameraYaw = _camera.GroundHeading()`，`HalfFov = _camera.HalfFov()` = `atan(tan(fov/2) · 宽/高)`（`Camera3D.Fov` 是竖直视角；开镜时 fov 变小，扇形跟着变窄；两个帮手在 `OrbitCamera`，试驾场共用）。
  - `MinimapFeed.AddOthers(frame, _client.Others, Mode, OwnTeam(own), eyeTankId)`。
- 按键：`_UnhandledInput` 里 `MapToggle` 按下（不在暂停、不在结算、小地图层可见——加载中不响应）→ `_minimap.ToggleFull()`；Esc：全屏开着时只 `CloseFull()` 并吃掉这个键，不开暂停层；不影响开车、开炮、转镜头（全屏图不抓鼠标、不暂停）。
- 结算：`ShowResults(true)` 时 `_minimap.SetHidden(true)`；暂停层打开时全屏图关掉（`ShowPause` 里 `CloseFull()`）。
- 换场地实例（`TakeField`）：一个场景一张图（新对局有新场景），这里换进来的只会是同一局重同步的 `Fresh()` 副本，`MinimapImage.Refresh` 认得；`TakeField` 不重新烘（批次 B 审查后删掉了那条到不了、还会用旧蒙版的分支）。图层自己兜底：物件数组不同但地形相同就用同一个底图新建 `MinimapImage`，地形不同就报错一次、留着旧图（见任务 4）。
- `--full-map`：`MatchAutomation` 在场景起来后第一帧有小地图时打开全屏（只打开一次）。
- 截图：`_automation.EndFrame(delta, _time, mapReady: _minimap is null || _minimap.HasPicture)`（到点的截图等底图，批次 B 审查后加）。
- 加载层（`_loading`）盖着的时候小地图也藏着（同 `_hud.Visible`）。

**验收**（`build/shots/5d-t5/`，都要看、写进进度）：

```bash
G="--host --mode standard --map riverland --bots 9 --autostart"
$S/godot_run.sh $W "" 150 $=G --autodrive --seed 11 --screenshot $W/build/shots/5d-t5/drive-60.png --screenshot-at 60 --quit-after 70
$S/godot_run.sh $W "" 150 $=G --autodrive --seed 11 --full-map --screenshot $W/build/shots/5d-t5/full-60.png --screenshot-at 60 --quit-after 70
$S/godot_run.sh $W "" 150 $=G --camera-preset map --seed 11 --screenshot $W/build/shots/5d-t5/map-preset.png --screenshot-at 30 --quit-after 40
$S/godot_run.sh $W "" 90 --host --mode teambattle --map sandbox --bots 3 --autostart --autodrive --screenshot $W/build/shots/5d-t5/sandbox-map-60.png --screenshot-at 60 --quit-after 70
```

看：右下角小地图在，箭头位置和朝向与画面里的车一致（`--autodrive` 时车头方向）、扇形朝镜头看的方向；队友蓝灰四种形状；敌人只在看得见时出现（和画面上的血条对得上）；比分板下有进度条时基地圈上有弧；全屏图压暗、居中；`map-preset` 那张俯视和小地图的底图走向一致。另外 `--hud-demo` 已有的屏不受影响（任务 4 已拍）。

**步骤**

- [ ] 实现接线
- [ ] 五个项目 0 警告；四个测试项目全过
- [ ] 四张截图看过
- [ ] 提交

**提交**：`feat(client): minimap in the match — live icons, the spectate eye, M and Esc, hidden on the results`

### Task 6：试驾场接线和回归截图

**Files:**
- Modify: `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`、`Sandbox/TargetRange.cs`、`Sandbox/SandboxAutomation.cs`（`--full-map`）

**规则**

- `SandboxMain`：`_minimap = Add(new MinimapLayer())`，在 HUD 之后；`Build(_sim.Field, 蒙版, 地图名)`。
- 每帧：视点 `Arrow` 在画出来的玩家车（插值后的位置和 yaw；残骸时 `Ring` + `OwnWreck`，试驾场没有观战）；`TargetRange.FillMinimap(MinimapFrame frame)`（新方法）：每个靶车，`IsHidden` 的跳过，其余 `Side.Enemy`、`ShapeOf(spec)`、插值后的位置、`Destroyed = !Alive`；不加基地（试驾场不是标准战）。
- 按键：M 开关全屏；Esc 在全屏开着时只关全屏（第一下 Esc 放鼠标、第二下退出的规则在全屏关着时照旧）。
- `--full-map` 同对局。

**回归截图**：`baseline4b.sh` 重拍六张，`pngdiffdirs.py build/shots/base build/shots/5d-t6-sandbox --ignore-minimap`：方框外和基线同量级（以往 orbit 约 23%、heavy 约 8% 的云、草、粒子抖动），方框里是小地图。六张都看，确认小地图的箭头和车对得上、看得见的靶车是红色、看不见的没画（`patrol` 那张开着 `--spot-debug`，对照标签）。然后把这六张拷成 `build/shots/base5d/`，作为 5D 之后的基线（进度里写明）。

另外拍 `--full-map` 的一张：

```bash
$S/godot_run.sh $W res://scenes/Sandbox.tscn 40 --map riverland --full-map --screenshot $W/build/shots/5d-t6/sandbox-riverland-full.png --screenshot-at 6 --quit-after 8
```

**步骤**

- [ ] 实现接线
- [ ] 五个项目 0 警告；四个测试项目全过
- [ ] 六张回归截图（扣掉小地图）比较、看过；全屏一张看过
- [ ] 提交

**提交**：`feat(client): minimap in the test drive — the player's arrow and the targets in sight`

**批次 B 收尾**：两路审查 → 跟进提交 → 进度。

---

## 批次 C：收尾

### Task 7：验收

不改代码（发现问题就修，修的提交单独写 `fix(client): …`）。

- [ ] 五个项目 0 警告；四个测试项目全过（记数字；Sim、Net 不变）。
- [ ] 画廊：`--hud-demo minimap`、`map-full` 两张（阶段 5 设计 §5、§6），看过。
- [ ] 对局（河谷标准战、1 名 `--autodrive` + 9 个机器人）：
  - 小地图 60 秒、180 秒各一张；`--full-map` 一张；
  - 观战：不开车（不加 `--autodrive`，站在出生点迟早被打掉），`--shot-after death:8`（死后 8 秒，残骸特写之后自动跟随队友）一张，箭头换到被跟随的车、自己的残骸是橙色叉；这一局没死就换种子再跑；
  - 房子塌了的局部重画：整局验收的日志里 `grep "\[minimap\]"`，有 `baked … ms` 一行和至少一行 `repainted N tiles`（机器人会撞塌、打塌房子；整局没有就记进度，测试已覆盖）；
  - 看完写进度：图标是不是跟画面里的车对得上、敌人是不是只在看得见时出现。
- [ ] 各自为战（`--mode ffa --map riverland --bots 5`）一张：没有基地、其他车都是红色。
- [ ] 试驾场：六张回归截图（扣掉小地图）和 `build/shots/base/` 同量级；`build/shots/base5d/` 在。
- [ ] 发布脚本的冒烟参数仍够用：`godot-mono --headless --path src/Tank.Client -- --host --port 27091 --bots 1 --autostart --quit-after 10` 输出里 `[server] match started` 和 `[match] entered match` 都有，没有 `[minimap] bake failed`（无头模式下也不该崩）。
- [ ] `pgrep -fl 'MacOS/Godot'` 没有残留。

**提交**：没有代码改动就不提交，结果记进度（下一个任务一起提交）。

### Task 8：文档

**Files:** `docs/01-game-design.md`、`docs/04-architecture-and-plan.md`、`README.md`、`docs/PROGRESS.md`

内容以代码为准，不写没做的东西：

- `docs/01-game-design.md`：HUD 一节加小地图（位置和大小、M 全屏、Esc 关、底图内容、图标和颜色、四种形状、观战和自由视角、不泄露、试驾场也有）；不做的（最后位置残影、发信号）；§11 还没做的改为阶段 6。
- `docs/04-architecture-and-plan.md`：仓库结构加 `Minimap/`、`Minimap/Logic/` 和各类的职责；客户端每帧的顺序里加小地图；底图在后台线程烘、局部重画；§5 测试表加新的测试类；§6 阶段路线 5D 已完成、阶段 5 完成。
- `README.md`：进度表 5D ✅、阶段 5 完成；按键表加 M。
- `docs/PROGRESS.md`：5D 节点记录最后一行，“当前位置”改为阶段 5 完成、等用户试玩。

**命令**：`git diff --stat`；文档里提到的每个类名、常数、数字用 grep 在代码里核对一遍。

**步骤**

- [ ] 按上面逐个文件改
- [ ] 用 grep 核对
- [ ] 提交

**提交**：`docs: stage 5D — the minimap in docs 01 and 04 and the README`，末行 `Tests: unchanged (…)`（按实际）。

**批次 C 收尾**：两路审查（规格审查对照阶段 5 设计 §5、§6 的 5D 部分和下面的验收清单；文档审查对照代码）→ 跟进提交 → 进度 → 验收清单全部打勾后 squash 合入本地 main（见“执行方式”），合入后在 main 上再跑一遍四个测试项目和 0 警告检查。

---

## 验收清单（5D）

- [ ] 四个测试项目全部通过，五个项目 0 警告；Sim、Net 的测试数不变，Art 只增不减。
- [ ] 小地图在对局和试驾场的右下角（1600×900 下 240 × 240、离边 24），不和别的 HUD 块重叠；M 放大成全屏（居中、留边距、背景压暗），M 或 Esc 关闭；全屏不暂停、不放鼠标；结算时藏起来。
- [ ] 底图：地形明暗、水、公路、土路、沙地、树林（按树冠密度）、房子、墙；颜色全来自 `Palette`；方向和 “map” 俯视一致；在后台线程烘，烘的时候对局照常。
- [ ] 房子塌、墙倒时只重画受影响的块，结果和整张重画逐字节相同（测试）。
- [ ] 图标：两个基地（标准战，己方蓝灰、对方红、有分时画进度弧）；自己橙色箭头 + 镜头扇形；队友蓝灰四种形状、残骸灰叉；敌人红色、残骸淡红叉；各自为战没有队友；观战时箭头在被跟随的车上、自由视角是焦点圈、自己的残骸橙色叉。
- [ ] 不泄露：小地图的车只来自快照（`client.Others`），单元测试和回环整合测试都证明了。
- [ ] 画廊 `--hud-demo minimap`、`map-full`；对局、全屏、观战、各自为战、试驾场的截图都看过。
- [ ] 试驾场六张回归截图除了小地图那块和基线同量级；新基线 `build/shots/base5d/`。
- [ ] 文档 `docs/01`、`docs/04`、README 已按代码更新；`docs/PROGRESS.md` 的“5D 节点记录”完整。
- [ ] 每批两路审查和跟进提交都做了；squash 合入本地 main，合入后 main 上全部测试通过、0 警告；没有推远端。
- [ ] 用户试玩放在整个阶段 5 结束后——也就是现在。

---

## 与设计的偏差

写计划时按现在的代码定下来的做法，和阶段 5 设计字面不一样或设计没说的地方：

1. **小图显示整张图**（可开车的范围扩成正方形），不跟着车卷动：两张图都不大（河谷约 1100 m 见方，小图上约 4.7 m 一像素），整张图一眼看全，也和全屏图一致。
2. **树林只烘一次**：设计说底图只在“房子塌、墙倒”时局部重画；树被撞倒、打倒不重画，树林照开局时的样子。
3. **敌人的残骸**：设计只说了队友残骸画灰叉。服务器总把残骸发给所有人（`Viewer.Sees`），所以敌人残骸也画，用淡红叉，和队友的灰叉分得开。
4. **自己的残骸**：观战时自己的残骸画橙色叉，箭头去了被跟随的车；残骸特写时视点是圈（不是箭头），扇形照画。
5. **进度弧的颜色**跟比分板下的进度条一致：己方基地被占是红，对方基地被我们占是蓝灰；不闪。
6. **不画**篱笆、灌木、岩石（设计列的底图内容里没有它们）；墙在 1 m 一像素下太细，至少画 2 像素宽。
7. **全屏地图不暂停、不放鼠标**：开着照样开车、开炮、转镜头；Esc 在全屏开着时只关全屏；暂停层打开时、按住 Tab 看计分板时（批次 C 审查后加：计分板和全屏图同层，会被压在下面）全屏关掉；结算时两个都藏。
8. **底图在后台线程烘**（对局里），没烘好时小地图只画底框和图标；画廊里同步烘。
9. **全屏图上方写地图名、下方写“M / Esc 关闭”**（设计没说）。
10. **试驾场回归截图**：设计要求和改动前一致，但设计又要求试驾场也有小地图，所以比较时扣掉右下角小地图的方框（`pngdiffdirs.py --ignore-minimap`），之后用 `build/shots/base5d/` 作新基线。
11. **画廊只有两屏带小地图**（`minimap`、`map-full`，用河谷）；已有的 16 屏不加，截图不变。
12. **树冠合拢**（批次 A 审查后加）：设计说树林按树冠密度画，但河谷的林子每 ~61 m² 一棵树、一个树冠 ~35 m²，只按密度画时林子里只有 ~57% 的像素在树冠下，整片看起来是棕色的土上撒绿点（林内平均色 (140, 137, 92)，`Forest` 是 (63, 132, 66)）。所以树冠的像素先做一次半径 3.3 m 的闭运算（任务 1 规则 6），合拢处至少 `CanopyCover = 0.85` 的覆盖：林子内部读作一片树林（≥ 90% 的像素是深绿），外沿和孤树不变大（闭运算不出 `dilate(crowns, k)`），宽于 ~6.6 m 的空地和路照样露出来。
13. **自动截图等底图、基地圈里填色**（批次 B 审查后加）：机器忙时 Debug 烘图要 7 s 多，6 s 的截图拍到的是空的底框，所以对局和试驾场的自动截图到点时如果小地图还没有图（`MinimapLayer.HasPicture`）就等，最多等到点后 20 s（`ShotPlan.MapWaitSeconds`），超时照拍并 `GD.PrintErr("[minimap] screenshot taken before the map was baked")`；`--quit-after` 跟着等（Main 按进程时钟的退出也等）。画廊同步烘，不用等。基地：设计只说了圈和进度弧；进度弧改画在圈外（不压圈），圈里填圈的颜色 25% 的圆盘——填色说是谁的基地，弧说进度。
14. **敌人也画车型形状**（批次 C 文档核对时补记）：设计写敌人是“红色点”；为了和队友一致、一眼看出敌人是什么车，敌人和队友一样按车型画四种形状（`MinimapFeed.ShapeOf`），只是颜色用红。车型本来就在快照里（客户端已经能看到敌人的车），不多泄露信息。
