# 新地图「河谷」Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 1200 m 的地图「河谷」（起伏地形、树林、浅水、岩石、草丛、可摧毁的房子、沙地、公路），连同它需要的新玩法件（地面和水的速度、树、房子、物件空间索引）、地图数据和协议 v3、服务器选图、客户端画法和试驾场入口。

**Architecture:** 规则和地图生成都在 `Tank.Sim`：`Battlefield` 新增地面类型网格、水位和内部的物件空间索引；`PropKind` 追加 `House`、`Tree`；河谷由 `RiverlandMap` 按种子生成，经 `MapCatalog` 交给服务器和试驾场。`Tank.Net` 把地面和水追加进地图编码（协议 v3），服务器按 `MapDefinition` 选图并缓存。客户端只从 `Battlefield` 读地面、水和物件来画（这样联网对局拿到数据也能画），新东西尽量放新文件，现有文件只在验证过能和 4B 干净合并的位置加挂点。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、LiteNetLib 2.1.4、Godot 4.7.2 .NET 版。

**设计依据：** [新地图「河谷」设计](../specs/2026-10-19-riverland-map-design.md)（下文简称“设计”）。上级：总体设计 §3、§4、§6；阶段 2C、3、4 设计。

**计划的深度：** 用户要求直接开发，这份计划不是从草稿导出的逐字代码。每个任务写清：改哪些文件、对外接口（签名）、规则和算法要点、要先写的测试用例（输入和期望）、命令和验收。实现代码由执行者按 TDD 写；接口一律以本文“共享接口”一节为准，需要改接口时先改本文再动代码。

---

## 执行方式

- **分支和目录**：`feat/map-riverland`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-map`（从 main `719164a` 拉出，Godot 资源已导入）。下文路径都相对于它。
- **逐任务派子代理实现**（superpowers:subagent-driven-development）：一个任务一个提交，提交前全部测试通过、所有项目 0 警告。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照设计和本计划），一个做代码质量审查；按意见做一个跟进提交。
- **批次之间**：在 `docs/PROGRESS-riverland.md` 记一行节点记录（提交号、测试数变化、截图目录、帧率），随分支提交。
- **C 批（联网）和 D、E 批（客户端）互不相交**，可以并行：C 在单独的临时 worktree 里做，做完 cherry-pick 回本分支。
- **用户试玩**在 F 批之后；试玩确认后才 squash 合入 main（合入前先合并 main 上别人提交的 `docs/PROGRESS.md` 改动）。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-map
```

**构建与测试**（`dotnet` 的输出在这台机器上是中文，按中文关键词 grep）：

```bash
# 0 警告检查：每行应为 "0 个警告 0 个错误"（或英文 "0 Warning(s) 0 Error(s)"）
for p in src/Tank.Sim/Tank.Sim.csproj src/Tank.Net/Tank.Net.csproj src/Tank.Client/Tank.Client.csproj \
         src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do
  echo "$p: $(dotnet build $p 2>&1 | grep -E '警告|错误|Warning\(s\)|Error\(s\)' | tr '\n' ' ')"; done
dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过!|失败!"     # Net、Art、Core、Update 同理
dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"                 # 全部五个
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~PropIndex" 2>&1 | tail -5   # 只跑某个类
```

- 改动前 main 上的测试数：Sim 637、Net 208、Art 512、Core 62、Update 42（合计 1461）。每个任务的提交说明末尾写测试数变化。
- 客户端项目不把警告当错误，“0 警告”靠上面的 grep 检查。
- 引擎无关的客户端代码放 `src/Tank.Client/scripts/Art/Mesh/`（Art 测试自动包含）；放别处的要在 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj` 第 24 行之后追加 `<Compile Include>`（不要插在第 14 行后面，4B 在那里加了通配）。

**Godot**：

```bash
S=/Users/murphy/Desktop/dev/BattleCity-dev-scripts
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后必须先编译；Godot 命令行不会编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs/.gdshader/.gdshaderinc 后生成 .uid，和源文件一起提交
mkdir -p build/shots/<任务>
$S/godot_run.sh $PWD res://scenes/Sandbox.tscn 40 --map riverland --camera-preset overview \
  --screenshot $PWD/build/shots/<任务>/overview.png --screenshot-at 4 --quit-after 6
ls -la build/shots/<任务>/                                  # 成功与否看文件在不在（脚本的 grep 不打印 Screenshot saved）
# 帧率（--disable-vsync 放在 -- 之前；至少跑 35 秒，前 7～20 秒不算；不要同时截图）
godot-mono --path src/Tank.Client --disable-vsync res://scenes/Sandbox.tscn -- --map riverland --camera-preset spin \
  --fps-log --quit-after 35 > build/shots/<任务>/spin.log 2>&1 & P=$!; ( sleep 65; kill -9 $P ) >/dev/null 2>&1 & wait $P
grep -E "fps-log|ERROR|SHADER|Exception" build/shots/<任务>/spin.log
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束检查没有残留的 Godot 进程
```

- 截图路径必须是绝对路径。截图要人工逐张看（用 Read 打开 PNG）。
- 试驾场六张回归截图（orbit、kill、lamp、wall、patrol、heavy）：`$S/baseline4b.sh $PWD build/shots/<目录>`。批次 A 开工前先拍一套基线到 `build/shots/base/`，之后凡是动了客户端或仿真的任务，都要重拍并和基线比较（像素差只允许出现在粒子和地平线抖动上；比较用 `pngdiff.py` 第 1～37 行的 `cmp(a, b)`）。
- 另一个会话（4B）在用 Godot，窗口会抢焦点；测帧率时如果整段都是 60 帧，说明窗口被挡，重测。

**提交**：

- 说明用英文一行，带范围前缀：`feat(sim): …`、`feat(net): …`、`feat(client): …`、`feat(server): …`、`test(sim): …`、`fix(…): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。末尾一行 `Tests: Sim 637 → 652.` 之类。
- 作者用仓库配置（火狐），不加 Co-Authored-By。不推送远程。
- 只提交本任务的文件。`.uid` 和源文件一起提交。

## 和 4B 并行开发的硬规则

4B 的会话正在 `/Users/murphy/Desktop/dev/BattleCity-stage4b-draft` 开发客户端对局，之后要变基到合入了河谷的 main 上。下面的规则是对着 4B 草稿（`2e75b3f`）用三方合并逐条验证过的：

- **整个文件不要改**：`Sandbox/SandboxHud.cs`、`Sandbox/SandboxInput.cs`、`Bootstrap/Main.cs`、`UI/*`、`Overlay/HealthBars.cs`、`Vehicles/TankModel.cs`、`Combat/WreckSmoke.cs`、`Combat/ShellViews.cs`，以及 `Tank.Net` 的 `BattleClient.Match.cs`、`BattleClientTypes.cs`、`Client/ClockSync.cs`。
- **不要改名、移动** `Sandbox/SandboxWorld.cs`；不要改 `SandboxWorld.Build` 和 `Update` 的签名。
- **不要新建**这些路径：`World/MapStyle.cs`、`World/BattleWorld.cs`、`Vehicles/TankActor.cs`、`Overlay/CombatHud.cs`、`Driving/`、`Online/`、`Match/`、`UI/RoomScreen.cs`、`UI/Logic/`、`Bootstrap/Main.Flow.cs`、`Bootstrap/Logic/ShotPlan.cs`、`Bootstrap/CaptureRun.cs`；也不要建和 4B 同名的测试文件（`AimMath`、`AutoRoom`、`FireLatch`、`HudDemoFrames`、`HudLayout`、`HudText`、`KillFeed`、`LanRoomRow`、`LoopbackSession`、`MatchColors`、`MatchHudFrame`、`ModeText`、`NetPanelText`、`RenderMath`、`RoomModel`、`RoomRules`、`ScoreboardModel`、`SpectateState`、`ThreadedHost`、`WreckTracker`、`ShotPlan` 加 `Tests.cs`）。`Tank.Sim.Maps` 里不能有叫 `MapStyle` 的类型。
- **现有文件只在这些位置改**（行号是 main `719164a` 上的；行号会随前面的任务移动，以引用的代码为准）：

| 文件 | 可以改的位置 | 不能碰的位置 |
|---|---|---|
| `Sandbox/SandboxWorld.cs` | 第 7 行后加 using；第 26 行（`_walls` 字段）后加字段；第 43 行（`terrain.Build(...)`）**之后**插入；第 60 行（`trees.Build(field);`）后建新视图；第 75 行（`_walls.Sync(...)`）后加一行调用；第 79 行 `OnEvent` 可以改写；类末尾加私有方法 | 类注释；`Build`/`Update` 签名；第 42、43、57 行；`Update` 开头几行 |
| `Sandbox/SandboxMain.cs` | 第 66 行（标题）、71 行（建仿真）、73 行（出生点）、76～77 行（靶车）、250 行（机位）、216 行（爆炸）附近 | 第 32、34 行常量；第 84 行 `_world.Build(...)`；187～197、231～240、256～274 行 |
| `Sandbox/TargetRange.cs` | 第 60、71、76 行；第 75 行后插一行提前返回；`FillInputs`、`ReviveDue` 里加“没有巡逻车”的保护 | 第 45～50 行 `Target` 类；第 77～82 行 |
| `Sandbox/SandboxAutomation.cs`、`Sandbox/RangeDemo.cs`、`Cameras/CameraPresets.cs`、`Cameras/OrbitCamera.cs` | 都可以改；`CameraPresets.Get/IsKnown/Names/StreamsAroundCamera/Spawn` 只能加可选参数，不能改已有参数 | — |
| `Bootstrap/LaunchOptions.cs` | 第 13 行后加帮助文字；第 50 行后加属性；第 83 行后加 `"--map"` 分支；第 118 行后加解析函数 | 不加新的 `using`；类注释第 10～11、16～17 行；第 27～31、67～72、121 行 |
| `World/GrassField.cs` | `CollectNearbyRocks`（第 220～230 行）及其以后 | 第 66～79 行；`Build` 签名 |
| `World/BushField.cs` | `Build`（第 55～57 行起）、`SyncStanding`（第 136 行起） | `Update`（第 106～124 行） |
| `Art/Palette.cs` | 第 37 行（`Bark`）或第 16 行之后加新颜色 | 第 55～56 行 |
| `tests/Tank.Art.Tests/Tank.Art.Tests.csproj` | 第 24 行之后追加 | 第 14～17 行 |
| 测试 | 新建测试文件 | 不要在 `LaunchOptionsTests.cs`、`PaletteTests.cs`、`BattleIntegrationTests.cs` 末尾追加 |

  其余客户端文件（`TerrainMesh`、`WallViews`、`TreeLine`、`RockMeshes`、`Scenery`、`DirtMask`、`DustTrail`、`BattleFx`、`PropFx`、`StatusPanel`、着色器）4B 都没改，可以改。
- **4B 用到、必须保持源码兼容的接口**：`Battlefield(HeightField, IEnumerable<Prop>, float)` 构造函数；`Battlefield.Props/PropById/Durability/Raycast/ClampToPlayable/Terrain/SetDurability/HasProp`；`HeightField` 的构造函数和 `HeightAt/SizeX/SizeZ`；`TankPose.Compute`、`Ballistics.Aim/PredictImpact`、`Dispersion.Radius/ReferenceRange`、`TankMotion.Footprint`；`SandboxMap.BushPatches/Seed/Spawns/Waypoints`；`MapData` 的六参数构造函数和 `Of/Build`；`CameraPresets.Get/IsKnown/Names/StreamsAroundCamera`；`OrbitCamera.Follow`。
- **试驾场地图逐位不变**：`SandboxMap.Create()` 的高度、物件及其编号不变（批次 A 第一个任务加指纹测试）；默认（不带 `--map`）时客户端画面不变。
- **4B 变基时要补的地方**写在本文末尾“4B 变基清单”，每个任务改到相关处时顺手补充。

---

## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Maps/PropIndex.cs` | 新建 | 内部的均匀网格空间索引：按用途分组（`PropSet`），`Near`/`Along` 查询返回按下标升序、去重的候选 | 1 |
| `Maps/Battlefield.cs` | 修改 | 建索引；`Raycast` 走索引；`Near`/`Along`/`All`；`UseIndex` 测试开关；地面图、水位、`SurfaceAt`/`WaterDepthAt`/`SpeedFactorAt`；`StateVersion`；枝叶组 | 1、2、3 |
| `Vehicles/TankMotion.cs` | 修改 | 行驶、撞倒、推出、压瓦砾走索引；目标速度乘地面系数；树和房子的撞倒规则 | 1、2、3、4 |
| `Simulation.Props.cs`、`Simulation.Shells.cs` | 修改 | 穿过、溅射走索引；圆形物件按圆柱算 | 1、3 |
| `Vision/Spotting.cs` | 修改 | 遮挡计数走索引、算上树冠 | 1、3 |
| `Maps/SurfaceKind.cs`、`Maps/SurfaceMap.cs`、`Maps/GroundSpeed.cs` | 新建 | 地面类型、地面网格、速度系数 | 2 |
| `Maps/MapData.cs` | 修改 | 带地面类型和水位 | 2 |
| `Maps/Prop.cs` | 修改 | `House`、`Tree`；`IsRound`、`IsFoliage`、`FoliageRadius`、`LastKind`；`IsWall` 含房子；耐久；废墟范围 | 3、4 |
| `Vehicles/TankSpec.cs`、`Vehicles/TankCatalog.cs` | 修改 | `HouseRamSpeed` | 4 |
| `Maps/Building/Polyline.cs`、`Maps/Building/HeightSculpt.cs`、`Maps/Building/PlacementGrid.cs`、`Maps/Building/PoissonDisc.cs`、`Maps/Building/Passability.cs` | 新建 | 生成用的通用工具：平滑折线和距离、地形塑形、网格加速的邻近检查、泊松圆盘采样、可通行格子与连通 | 5 |
| `Maps/RiverlandMap.cs` | 新建 | 河谷的对外入口：常数、`Create()`、出生点、路点、试驾场站位、地标 | 6、7 |
| `Maps/Riverland/RiverlandLayout.cs`、`RiverlandTerrain.cs`、`RiverlandSurface.cs`、`RiverlandScatter.cs`、`RiverlandVillages.cs` | 新建 | 河谷的布局数据、地形、地面、岩石草丛树、房子和院墙 | 6 |
| `Maps/MapDefinition.cs`、`Maps/MapCatalog.cs` | 新建 | 地图目录：名字、显示名、生成函数、出生点、路点、缓存的 `MapData` | 7 |

**联网 `src/Tank.Net`、`src/Tank.Server`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Battle/BattleProtocol.cs` | 修改 | 版本 3 | 8 |
| `Battle/BattleMessages.cs` | 修改 | 地图编码追加段；物件上限 16384；种类上限取自仿真；开局物件状态数上限 | 8 |
| `Battle/BattleServerOptions.cs`、`Battle/BattleServer.Match.cs`、`Battle/BattleServer.cs` | 修改 | 按选项选图、缓存地图数据和编码；不可靠包上限 1000 | 9 |
| `Tank.Server/Program.cs` | 修改 | 新模式也能用 `--map` | 9 |

**客户端 `src/Tank.Client`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `scripts/Bootstrap/LaunchOptions.cs` | 修改 | `--map`（原样保存，使用方校验） | 10 |
| `scripts/Sandbox/SandboxSites.cs` | 新建（引擎无关） | 每张图的试驾场站点：仿真地图、玩家出生、靶车、有无巡逻车、机位锚点、演示 | 10 |
| `scripts/Sandbox/RiverlandDemos.cs` | 新建（引擎无关） | 河谷的四个演示 | 10、16 |
| `scripts/Sandbox/SandboxMain.cs`、`TargetRange.cs`、`SandboxAutomation.cs`、`RangeDemo.cs` | 修改 | 从站点取数据 | 10 |
| `scripts/Cameras/CameraPresets.cs`、`OrbitCamera.cs` | 修改 | 河谷机位；`map` 正交俯视；镜头不沉到水下 | 10、12 |
| `scripts/Art/Mesh/GroundLayers.cs` | 新建 | 从地面网格和水位烘出四个通道（非草地、沙、路、备用），模糊，双线性取值 | 11 |
| `scripts/World/DirtMask.cs`、`World/TerrainMesh.cs`、`shaders/terrain_toon.gdshader`、`Art/Palette.cs` | 修改 | 分层遮罩；地形着色器的沙地、公路、河床、湿岸；远景地面大小 | 11 |
| `scripts/World/TreeLine.cs`、`World/GrassField.cs` | 修改 | 图外树带避开水和路；长草时查附近的桶 | 11、15 |
| `scripts/Art/Mesh/WaterMesh.cs`、`scripts/World/WaterSurface.cs`、`shaders/water.gdshader` | 新建 | 水面 | 12 |
| `scripts/Combat/DustTrail.cs`、`Combat/BattleFx.cs` | 修改 | 涉水水花、落水水柱（自己查水面） | 12 |
| `scripts/Overlay/GroundLine.cs` | 新建 | 状态面板上方的地面提示（由 `SandboxWorld` 挂上） | 12 |
| `scripts/Art/Mesh/TreeFall.cs`、`scripts/World/ForestViews.cs`、`shaders/tree.gdshader`、`shaders/dither.gdshaderinc`、`shaders/bush.gdshader` | 新建、修改 | 林中树、倒下动画、挡视线时半透明 | 13 |
| `scripts/Art/Mesh/HouseShapes.cs`、`scripts/World/HouseViews.cs`、`shaders/house.gdshader`、`scripts/World/WallViews.cs`、`scripts/Combat/PropFx.cs` | 新建、修改 | 房子、倒塌、废墟、特效；墙段视图排除房子 | 14 |
| `scripts/World/RockMeshes.cs`、`World/BushField.cs`、`World/TerrainMesh.cs` | 修改 | 性能：岩石按扇区合并、碎石和草丛按扇区分组、必要时远处地形降采样 | 15 |

**测试**：`tests/Tank.Sim.Tests` 新增 `SandboxFingerprintTests`、`PropIndexTests`、`PropIndexEquivalenceTests`、`GroundSpeedTests`、`SurfaceMapTests`、`MapDataSurfaceTests`、`TreeTests`、`TreeSpottingTests`、`HouseTests`、`HouseRamTests`、`MapBuildingTests`、`RiverlandMapTests`、`RiverlandLayoutTests`、`RiverlandSpawnTests`、`RiverlandSitesTests`、`MapCatalogTests`，以及共用的 `RiverlandFixture`（整个测试进程只生成一次河谷）；`tests/Tank.Net.Tests/Battle/` 新增 `MapWireV3Tests`、`ServerMapTests`、`RiverlandMatchTests`；`tests/Tank.Art.Tests` 新增 `LaunchOptionsMapTests`、`SandboxSitesTests`、`GroundLayersTests`、`WaterMeshTests`、`TreeFallTests`、`HouseShapesTests`、`RiverlandDemoTests`、`RiverlandPresetsTests`。

---

## 共享接口

所有任务都按这里的名字和签名写。改动接口要先改这一节。

### 仿真：地面和水（任务 2）

```csharp
namespace Tank.Sim.Maps;

/// <summary>What the ground is made of, per 2 m cell (design §2.1). Values are on the wire: append only.</summary>
public enum SurfaceKind : byte { Grass = 0, Dirt = 1, Sand = 2, Road = 3 }

/// <summary>Immutable grid of <see cref="SurfaceKind"/>, one per height-field cell: cell (i, j) covers x ∈ [i·CellSize, (i+1)·CellSize), z likewise.</summary>
public sealed class SurfaceMap
{
    public const SurfaceKind LastKind = SurfaceKind.Road;
    public SurfaceMap(int cellsX, int cellsZ, float cellSize, SurfaceKind[] cells); // validates sizes, finite cellSize > 0, every kind ≤ LastKind; copies cells
    public static SurfaceMap Uniform(int cellsX, int cellsZ, float cellSize, SurfaceKind kind = SurfaceKind.Grass);
    public int CellsX { get; }
    public int CellsZ { get; }
    public float CellSize { get; }
    public ReadOnlySpan<SurfaceKind> Cells { get; }          // row-major: j * CellsX + i
    public SurfaceKind this[int i, int j] { get; }            // i, j clamped to the grid
    public SurfaceKind At(float x, float z);                  // the cell under (x, z), clamped
    public bool IsUniform(SurfaceKind kind);                  // every cell is kind
}

/// <summary>Top-speed factors of the ground and of shallow water (design §2.1–2.3).</summary>
public static class GroundSpeed
{
    public const float Road = 1.15f, Sand = 0.8f;
    public const float WaterDepthScale = 3f, WaterMin = 0.5f, WaterMax = 0.8f;
    public static float Of(SurfaceKind kind);                 // Road 1.15, Sand 0.8, Grass/Dirt 1
    public static float InWater(float depth);                 // Clamp(1 − depth / 3, 0.5, 0.8)
    public static float Factor(SurfaceKind kind, float waterDepth); // waterDepth > 0 ? InWater(depth) : Of(kind)
}
```

`Battlefield` 新增（构造函数加两个可选参数，三参数写法照常可用）：

```csharp
public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border, SurfaceMap? surface = null, float? waterLevel = null);
// surface null → SurfaceMap.Uniform(SamplesX − 1, SamplesZ − 1, CellSize); otherwise its size and cell size must match the terrain's cells (ArgumentException)
// waterLevel must be finite when given (ArgumentException)
public SurfaceMap Surface { get; }
public float? WaterLevel { get; }
public SurfaceKind SurfaceAt(Vector2 ground);
public float WaterDepthAt(Vector2 ground);   // max(0, WaterLevel − Terrain.HeightAt); 0 without water
public float SpeedFactorAt(Vector2 ground);  // GroundSpeed.Factor(SurfaceAt, WaterDepthAt)
public int StateVersion { get; }             // +1 whenever any prop's standing or hit points change (Damage, Destroy, Restore, SetDurability)
```

`MapData` 新增（六参数构造函数照常可用，默认全草地、无水）：

```csharp
public MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border,
    SurfaceKind[]? surface, int? waterLevelCm);
public SurfaceKind[] Surface { get; }   // (samplesX − 1) × (samplesZ − 1), row-major; all Grass when null was given
public int? WaterLevelCm { get; }       // |value| ≤ MaxWaterLevelCm (ArgumentException)
public const int MaxWaterLevelCm = 1_000_000;
```

### 仿真：物件（任务 3、4）

```csharp
public enum PropKind : byte { Rock = 0, Bush = 1, BrickWall = 2, StoneWall = 3, Fence = 4, House = 5, Tree = 6 }

public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    public const PropKind LastKind = PropKind.Tree;
    public const float CrownPerTrunk = 7f;           // a tree's crown radius = its trunk radius × this
    public bool IsSolid => Kind != PropKind.Bush;                                            // unchanged: now also trees and houses
    public bool IsWall => Kind is PropKind.BrickWall or PropKind.StoneWall or PropKind.House; // masonry (design §2.5)
    public bool IsRound => Kind is PropKind.Bush or PropKind.Tree;                         // footprint is a circle of radius HalfExtents.X
    public bool IsFoliage => Kind is PropKind.Bush or PropKind.Tree;                       // hides tanks (design §2.4)
    public float FoliageRadius => Kind == PropKind.Tree ? Footprint.HalfExtents.X * CrownPerTrunk : Footprint.HalfExtents.X;
    public bool StopsShells => Kind == PropKind.Rock || IsWall;                            // unchanged formula: now also houses
    public bool IsFlimsy => IsDestructible && !StopsShells;                                // unchanged formula: now also trees
    public int MaxDurability => Kind switch { BrickWall => 300, StoneWall => 900, House => 500, Fence or Bush or Tree => 1, _ => 0 };
    public float DistanceTo(Vector3 point);          // round kinds: upright cylinder of HalfExtents.X (a tree's trunk)
    public Obb2 RubbleFootprint { get; }             // House: HalfExtents + (RubbleSpread, RubbleSpread); walls: + (0, RubbleSpread)
}
```

`TankSpec` 新增 `public required float HouseRamSpeed { get; init; }`：中坦、重坦 `20 * Kmh`，轻坦、TD `float.PositiveInfinity`。

### 仿真：空间索引（任务 1，3 加 `Foliage`）

```csharp
/// <summary>The groups of props the simulation queries (design §4.1).</summary>
internal enum PropSet : byte { Blockers, ShellStoppers, Flimsy, Walls, Foliage }

// Battlefield (internal):
internal const float IndexCellSize = 8f;
internal ReadOnlySpan<int> All(PropSet set);
/// Ascending, de-duplicated indices of the set's props whose set radius (below) reaches the disc (center, radius):
/// a superset of the props an exact test there can hit. Returns All(set) when the index is off or buffer is too small.
internal ReadOnlySpan<int> Near(PropSet set, Vector2 center, float radius, Span<int> buffer);
/// The same for the props whose set radius reaches the ground segment from → to widened by margin (cells walked along the segment).
internal ReadOnlySpan<int> Along(PropSet set, Vector2 from, Vector2 to, float margin, Span<int> buffer);
internal bool UseIndex { get; set; } = true;   // tests only: false makes Near/Along return All(set)
```

各组成员和登记半径：

| 组 | 成员 | 登记半径（以占地中心为圆心） |
|---|---|---|
| `Blockers` | `IsSolid` | 占地外接圆 `Footprint.HalfExtents.Length()` |
| `ShellStoppers` | `StopsShells` | 占地外接圆 |
| `Flimsy` | `IsFlimsy` | 占地外接圆（树就是树干） |
| `Walls` | `IsWall` | 废墟占地外接圆 `RubbleFootprint.HalfExtents.Length()` |
| `Foliage` | `IsFoliage` | `FoliageRadius` |

- 一个物件登记进它登记圆的外接方框覆盖的所有格子；超出地图的部分夹到边上的格子。
- 查询不写任何内部状态（去重靠排序），调用方用 `stackalloc int[256]` 之类的缓冲区。
- `Battlefield.Bushes`（内部）改名 `Foliage`；`BushesNear` 的语义不变（只返回草丛）。

### 仿真：地图（任务 6、7）

```csharp
namespace Tank.Sim.Maps;

public sealed class MapDefinition
{
    public MapDefinition(string id, string name, Func<Battlefield> create, SpawnLayout spawns, IReadOnlyList<Vector2> waypoints);
    public string Id { get; }                       // "sandbox", "riverland"
    public string Name { get; }                     // "试驾场", "河谷"
    public Func<Battlefield> Create { get; }        // a fresh battlefield straight from the generator
    public SpawnLayout Spawns { get; }
    public IReadOnlyList<Vector2> Waypoints { get; }
    public MapData Data { get; }                    // MapData.Of(Create()), built once on first use (thread-safe Lazy)
}

public static class MapCatalog
{
    public static MapDefinition Sandbox { get; }
    public static MapDefinition Riverland { get; }
    public static IReadOnlyList<MapDefinition> All { get; }       // Sandbox first
    public static bool TryGet(string? id, out MapDefinition map); // ordinal, ignore case
}

public static class RiverlandMap
{
    public const string Id = "riverland";
    public const int Seed = 20261019;
    public const int Samples = 601;
    public const float CellSize = 2f, Border = 30f, WaterLevel = 0f;
    public static Battlefield Create();
    public static readonly SpawnLayout Spawns;                 // Team: 5 (team 0, west) + 5 (team 1, east); Solo: 16
    public static readonly IReadOnlyList<Vector2> Waypoints;   // about 40
    public static readonly Vector2 Spawn;                      // the test drive's spawn (design §3.11)
    public static readonly float SpawnYaw;
    public static readonly IReadOnlyList<TargetSpot> TargetSpots;   // 8, in design §3.11 order
    public const int RoadTarget = 0, HouseTarget = 1, FordTarget = 2, ForestTarget = 3, BushTarget = 4,
                     PlateauTarget = 5, BeachTarget = 6, FieldTarget = 7;
    public static readonly RiverlandLandmarks Landmarks;
}

public sealed record RiverlandLandmarks(Vector2 Town, Vector2 Ford, Vector2 SouthFord, Vector2 ForestEdge,
    Vector2 PlateauRim, Vector2 Dunes, Vector2 Lake, Vector2 MapCentre);
```

### 联网（任务 8、9）

```csharp
// BattleProtocol
public const int Version = 3;                        // ConnectKey unchanged
// BattleMessages
public const int MaxMapProps = 16384;                // MaxPropsPerMessage (4096) stays for event batches
// WriteMap: the v2 bytes unchanged, then: u8 flags (bit 0: water; other bits 0) · [VarInt waterLevelCm] ·
//           VarUInt runs · runs × { VarUInt length ≥ 1, u8 kind ≤ SurfaceMap.LastKind } (lengths sum to the cell count)
// BattleServerOptions
public MapDefinition Map { get; init; } = MapCatalog.Sandbox;
// BattleServer
private const int MaxUnreliableBytes = 1000;         // was 1150, over LiteNetLib's 1023-byte single packet
```

### 客户端（任务 10～14）

```csharp
// Bootstrap/LaunchOptions.cs
public string? Map { get; init; }                    // --map, raw; SandboxSites validates

// Sandbox/SandboxSites.cs (engine-free; linked into Tank.Art.Tests)
public sealed record PresetAnchors(IReadOnlyDictionary<string, (Vector2 Eye, float EyeHeight, Vector2 Focus, float FocusHeight)> Fixed,
    Vector2 SpinEye, Vector2 MapCentre, float MapSize);
public sealed record SandboxSite(string Id, string Title, Func<Battlefield> Create, Vector2 Spawn, float SpawnYaw,
    IReadOnlyList<TargetSpot> Targets, bool HasPatrol, PresetAnchors Anchors,
    IReadOnlyList<string> PresetNames, IReadOnlyList<string> DemoNames, Func<string?, RangeDemo?> FindDemo);
public static class SandboxSites
{
    public static SandboxSite Sandbox { get; }
    public static SandboxSite Riverland { get; }
    public static SandboxSite For(string? id, out string? warning);   // null/empty → Sandbox, no warning; unknown → Sandbox + "Unknown --map 'x'; using sandbox (use sandbox, riverland)."
}

// Sandbox/RangeDemo.cs: one more optional positional parameter at the end
public sealed record RangeDemo(..., bool PatrolDrives = false, IReadOnlyList<TargetSpot>? Spots = null)
{ public TargetSpot Spot => (Spots ?? SandboxMap.TargetSpots)[Target]; }

// Cameras/CameraPresets.cs: optional trailing parameters only
public static SN.Vector2 Spawn(string? name, SandboxSite? site = null);
public static (Vector3 Eye, Vector3 Target) Get(string name, Vector3 tank, float tankYaw, double time,
    Func<float, float, float> groundAt, int targetIndex, SandboxSite? site = null);
public static bool IsKnownFor(string? name, SandboxSite site);

// World/WaterSurface.cs
public partial class WaterSurface : Node3D
{
    public const string Group = "water_surface";
    public void Build(Battlefield field);            // no-op (and no mesh) without water
    public float? Level { get; }
    public float DepthAt(float x, float z);          // ≥ 0
    public static WaterSurface? Find(Node anyNodeInTree);   // the one in the tree's group, cached by callers
}

// World/DirtMask.cs
public static DirtMask? FromField(Battlefield field);  // layered mask when the field has water or a non-uniform surface; else null
public bool IsLayered { get; }

// World/TerrainMesh.cs
public void ApplyGround(DirtMask mask, float? waterLevel);   // after Build: swaps the mask texture, sets ground_layers and water_level

// World/ForestViews.cs, World/HouseViews.cs
public void Build(Battlefield field);
public void Sync(Battlefield field, Vector3 cameraPos, Obb2? ownHull, float delta);   // field passed every call, never cached
public void OnEvent(in GameEvent e);                 // remembers which way a felled tree / collapsing house was pushed
```

`SandboxWorld` 里：第 26 行后加字段 `_water`、`_forest`、`_houses`、`_groundLine`；第 43 行后插入

```csharp
        if (DirtMask.FromField(field) is { } ground) { dirt = ground; terrain.ApplyGround(ground, field.WaterLevel); }
```

第 60 行后建 `WaterSurface`、`ForestViews`、`HouseViews`、`GroundLine`；第 75 行后加 `SyncMapLayers(player, cameraPos, delta);`；`OnEvent` 改为把事件也交给 `_forest` 和 `_houses`。

---

## 任务总览

| 批 | 任务 | 内容 | 依赖 |
|---|---|---|---|
| A 仿真基础 | 1 | 试驾场指纹、空间索引（现有物件种类）、逐 tick 等价测试 | — |
| | 2 | 地面类型、水、速度系数、`MapData` 带地面和水、`StateVersion` | 1 |
| | 3 | 树：种类、撞倒、炮弹、溅射、枝叶遮挡 | 1、2 |
| | 4 | 房子：种类、伤害、撞塌、废墟、`HouseRamSpeed` | 3 |
| B 河谷生成 | 5 | 生成工具：折线、塑形、摆放网格、泊松圆盘、可通行和连通 | 2 |
| | 6 | 河谷的地形、水、地面、岩石、草丛、树、房子、院墙 | 3、4、5 |
| | 7 | 出生点、路点、试驾场站位、地标、`MapCatalog` | 6 |
| C 联网 | 8 | 协议 v3：地图追加段、上限、种类上限 | 2、4 |
| | 9 | 服务器选图、缓存、编码一次、`--map`、不可靠包上限、河谷对局测试 | 7、8 |
| D 客户端地面和水 | 10 | `--map`、站点、靶车、机位、演示入口（河谷先用现有部件画出来） | 7 |
| | 11 | 地面着色（沙、路、河床、湿岸）、长草规则、图外树带 | 10 |
| | 12 | 水面、镜头不入水、涉水水花、落水水柱、地面提示 | 11 |
| E 客户端物件 | 13 | 林中树、倒下动画、半透明、树的特效 | 10 |
| | 14 | 房子、倒塌、废墟、特效；墙段视图排除房子 | 10 |
| F 收尾 | 15 | 性能：岩石合并、碎石和草丛分扇区、远处地形（按需） | 11～14 |
| | 16 | 河谷演示、验收截图、帧率、文档 | 15 |

C 批可以和 D、E 批并行。第一次能试玩河谷是 E 批之后（之前树和房子还没画，是看不见的障碍）。

---

<!-- 以下各批的任务细节由分批的计划子代理补全 -->

## 批次 A：仿真基础

<!-- BATCH A -->

## 批次 B：河谷生成

<!-- BATCH B -->

## 批次 C：联网

<!-- BATCH C -->

## 批次 D：客户端地面和水

<!-- BATCH D -->

## 批次 E：客户端物件

<!-- BATCH E -->

## 批次 F：收尾

<!-- BATCH F -->

---

## 验收清单

- [ ] 全部测试通过，Sim、Net、Client、Server、Soak 五个项目 0 警告。
- [ ] `SandboxMap` 指纹测试通过；试驾场六张回归截图和基线一致。
- [ ] 空间索引等价测试（试驾场、合成树林、河谷）通过；不分配内存的测试在河谷上通过。
- [ ] 河谷生成 Release ≤ 1.5 s；河谷上 10 辆车每 tick 仿真平均 ≤ 0.5 ms；16 辆车点亮更新平均 ≤ 1.5 ms（Release，M3）。
- [ ] 协议 v3：河谷开局消息 < 1 MiB；服务器 `--map riverland` 三种模式都能开局；小地图对局完美网络零修正。
- [ ] 截图逐张看过：`map` 俯视、`overview`、`town`、`ford`（坦克在水里溅水花）、`forest`（倒下的树）、`plateau`、`dunes`、`house` 演示塌前后、`ram-house`、`trees`、`ford` 演示。
- [ ] 帧率（关垂直同步，1600×900）：河谷 `spin`、`orbit` 平均 ≥ 60 帧、热身后单帧 ≤ 33 ms；试驾场比改动前低不超过 10%。
- [ ] 用户试玩确认。

## 4B 变基清单

4B 的会话把草稿变基到合入河谷的 main 之后，要补这些（每个任务改到相关处时在这里补一条）：

- `World/BattleWorld.cs` 自由镜头分支（提前返回的那段）里也要调用 `SyncMapLayers`，否则观战时树倒、房塌看不到。
- `MatchMain.cs` 里写死的 `MapStyle.Sandbox`：改为先看 `DirtMask.FromField(field)`，没有分层遮罩时才用沙盒的草地岛。
- 自由镜头的焦点高度取地形高度和水位中较大的那个（`MatchMain.Camera.cs` 约第 158 行）。
- 地面提示（`GroundLine`）挪进 `CombatHud`，从 `SandboxWorld` 里去掉。
- `TargetRange` 第 77～82 行的冲突：保留 4B 的 `TankActor` 写法，并保留河谷加的“没有巡逻车就提前返回”。
- `SandboxMain` 第 66、71、73、76～77 行改成从站点取数据：变基时保留这些改动。
- 重新导出 4B 的计划；`feat/stage4b-client-match` 也移到新的 main 上。
