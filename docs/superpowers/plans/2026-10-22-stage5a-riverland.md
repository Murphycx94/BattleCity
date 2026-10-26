# 阶段 5A：河谷地图 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 新增 1200 m 的地图「河谷」（起伏地形、台地、树林、浅水、岩石、草丛、可摧毁的房子、沙地和公路），连同它需要的新玩法件（地面和水的速度系数、树、房子、物件空间索引）、地图数据和协议 v3、服务器选图和缓存、房间里选图、客户端的画法（地面、水、树、房子、水花、地面提示），试驾场和对局都能在河谷上开，两个实例能在河谷上打完一局。

**Architecture:** 规则和地图生成都在 `Tank.Sim`：`Battlefield` 新增地面类型网格、水位、内部的均匀网格物件索引和“物件状态版本号”；`PropKind` 追加 `House`、`Tree`；河谷由 `RiverlandMap` 按种子生成，经 `MapDefinition`/`MapCatalog` 交给服务器、试驾场和测试。`Tank.Net` 把地面和水追加进地图编码（协议 v3），房间状态和局域网发现带上地图名，房主能换图；服务器按 `MapDefinition` 准备地图（生成、编码各一次，房主线程和专用服务器在后台线程做），生成完才能开局。客户端只从 `Battlefield` 读地面、水和物件来画，地图图层直接接进试驾场和对局共用的 `World/BattleWorld`，所以对局拿到服务器的数据就画出同样的河谷。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9.3、LiteNetLib 2.1.4、Godot 4.7.2 .NET 版。只做 macOS。

**设计依据：** [新地图「河谷」设计](../specs/2026-10-19-riverland-map-design.md)（下文简称“河谷设计”）的 §1～§7；[阶段 5 设计](../specs/2026-10-22-stage5-design.md) §2 的改动以它为准（作废河谷设计 §8；房间里选图、协议 v3 带选图字段、对局窗口标题、核对 `MaxUnreliableBytes`、机器人照旧走直线）。参考：[旧计划](2026-10-19-riverland-map-old.md)（写于 4B 之前，共享接口和任务表沿用了能用的部分），[阶段 4 设计](../specs/2026-10-18-stage4-networking-design.md) §7、§8.1。

**计划的深度：** 和旧计划一样，这份计划不是逐字代码。每个任务写清：改哪些文件、对外接口（确切签名）、规则和算法要点、要先写的失败测试（具体输入和期望值）、命令和验收、提交说明。实现代码由执行者按 TDD 写（先写测试、确认失败、实现、确认通过）。接口一律以本文“共享接口”一节为准；实现中发现接口要改，先改本文这一节再动代码，并在跟进提交里说明。

---

## 执行方式

- **分支和目录**：`feat/stage5a-riverland`，工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage5a`（从 main `7633d77` 拉出，加了两个文档提交 `cc9f105`、`80d6eca`）。下文的相对路径都相对于它。
- **逐任务派子代理**（superpowers:subagent-driven-development）：一个任务一个提交。提交前五个项目 0 警告、四个测试项目全部通过。
- **每批做完**：并行派两个审查子代理，一个做规格审查（逐条对照河谷设计、阶段 5 设计 §2 和本计划，列“需求 → 代码 → 测试”表），一个做代码质量审查（正确性、确定性、分配、命名、注释）；按两份意见做**一个**跟进提交 `fix(<scope>): batch X review follow-up — …`。
- **每批之后记进度**：在 `docs/PROGRESS.md` 新建并维护“5A 节点记录”表（列：# / 节点 / 状态 / 提交 / 说明；说明里写各任务提交号、跟进提交号、测试数变化、截图目录、帧率、遗留问题），同时更新“当前位置”的“进度”“下一步”。随跟进提交一起提交，或单独提交 `docs: progress — 5A batch X`。
- **顺序**：A → B → C → D → E → F，批内按任务号。C（联网）和 D、E（客户端）互不相交的地方只有 D 的任务 16 依赖 C 的任务 13，为了简单不并行。
- **用户试玩**在整个阶段 5（5A～5D）做完之后，5A 做完不等试玩：最终验收清单全部打勾后 squash 合入本地 main（`git checkout main && git merge --squash feat/stage5a-riverland && git commit`，说明 `feat: stage 5A — riverland map, room map choice, protocol v3`，末行 `Tests: …`），不推远端；5B 从新的 main 拉分支。
- **只做 macOS**：不写 Windows、Linux 专门的代码路径，不打其他平台的包。
- 设计已确认，中途不问用户，做完汇报。

## 执行前须知

每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage5a
```

**构建与测试**（这台机器上 `dotnet test` 的输出是中文，`dotnet build` 的汇总行是英文）：

```bash
# 0 警告检查：每行应为 "0 Warning(s) 0 Error(s)"（或中文 "0 个警告 0 个错误"）
for p in src/Tank.Sim/Tank.Sim.csproj src/Tank.Net/Tank.Net.csproj src/Tank.Client/Tank.Client.csproj \
         src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do
  echo "$p: $(dotnet build $p 2>&1 | grep -E '警告|错误|Warning\(s\)|Error\(s\)' | tr '\n' ' ')"; done
for t in Sim Net Art Update; do echo "$t: $(dotnet test tests/Tank.$t.Tests 2>&1 | grep -E '通过!|失败!')"; done
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~PropIndex" 2>&1 | tail -5     # 只跑某些类
dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~RiverlandPerformance" --logger "console;verbosity=detailed" 2>&1 | grep -E "ms|通过!|失败!"   # Release 下的耗时
```

- 成功的行形如 `已通过! - 失败: 0，通过: 639 …`；`grep -E '通过!|失败!'` 两种都能抓到。
- **改动前的测试数**（本计划写作时在 `80d6eca` 上实测）：Sim 639、Net 170、Art 882、Update 42，合计 1733；五个项目 0 警告。`Tank.Core` 和它的测试项目已在 4C 删除，测试项目只有 Sim、Net、Art、Update 四个。每个任务的提交说明末行写测试数变化，例如 `Tests: Sim 639 → 650.`；下文各任务给的数是“至少”，以实际为准。
- 客户端项目 `Tank.Client.csproj` 不把警告当错误（`TreatWarningsAsErrors=false`），“0 警告”靠上面的 grep 检查；其他项目由 `Directory.Build.props` 的 `TreatWarningsAsErrors=true` 保证。
- **Art 测试编进来的客户端代码**（`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`）：`scripts/Art/Mesh/**/*.cs`、**任何** `Logic/` 目录下的 `**/Logic/**/*.cs`、`Art/Palette.cs`、`Bootstrap/LaunchOptions.cs`、`Sandbox/RangeDemo.cs`、`Sandbox/TankInterpolation.cs`、`Sandbox/FrameTimeLog.cs`、`Overlay/PopupCurves.cs`、`Overlay/SpotText.cs`、`Vehicles/{TankLook,TankLooks,TankParts,TankAnimator,TurretToss}.cs`。新的引擎无关代码放进 `Art/Mesh/` 或某个 `Logic/` 子目录（命名空间沿用父目录的，例如 `Sandbox/Logic/*.cs` 是 `Tank.Client.Sandbox`），不用改 csproj；这些文件里不能 `using Godot`。Art 测试项目引用 `Tank.Sim` 和 `Tank.Net`，能直接用 `MapCatalog`、`RiverlandMap`。
- `Tank.Sim` 的 `internal` 只对 `Tank.Sim.Tests` 可见，`Tank.Net` 的 `internal` 只对 `Tank.Net.Tests` 可见；客户端只能用 public 成员。
- `Tank.Net` 的命名空间（4C 整理后）：`Tank.Net.Protocol`（`BitReader`、`BitWriter`、`ProtocolException`、`Channel`）、`Tank.Net.Transport`（`LiteNetServerTransport`、`LiteNetClientTransport`、`LoopbackNetwork`、`LanDiscovery`）、`Tank.Net.Timing`（`ServerRunner`、`ClockSync`）、`Tank.Net.Battle`（协议、服务器、客户端）。

**Godot**（开发脚本在 `/Users/murphy/Desktop/dev/BattleCity-dev-scripts/`，见它的 `README.md`）：

```bash
S=/Users/murphy/Desktop/dev/BattleCity-dev-scripts
W=/Users/murphy/Desktop/dev/BattleCity-stage5a
dotnet build src/Tank.Client/Tank.Client.csproj            # 每次改 .cs 后先编译：Godot 命令行不编译 C#
godot-mono --headless --path src/Tank.Client --import      # 新增 .cs/.gdshader/.gdshaderinc 后生成 .uid，和源文件一起提交
mkdir -p build/shots/5a-tNN                                 # build/ 在 .gitignore 里
# 试驾场（SCENE 给 res://scenes/Sandbox.tscn）；参数直接跟在超时秒数后面
$S/godot_run.sh $W res://scenes/Sandbox.tscn 60 --map riverland --camera-preset overview \
  --screenshot $W/build/shots/5a-tNN/overview.png --screenshot-at 6 --quit-after 8
# 主场景（菜单、房间、对局）：SCENE 传空字符串
$S/godot_run.sh $W "" 90 --host --map riverland --mode ffa --bots 3 --autostart \
  --screenshot $W/build/shots/5a-tNN/match.png --screenshot-at 25 --quit-after 35
ls -la build/shots/5a-tNN/                                  # 成败看文件在不在（脚本只打印含 screenshot/error 的行）
# 试驾场六张回归截图（orbit、kill、lamp、wall、patrol、heavy），OUTDIR 相对于工作目录
$S/baseline4b.sh $W build/shots/5a-tNN-sandbox
python3 $S/pngdiffdirs.py build/shots/base build/shots/5a-tNN-sandbox
# 帧率：--disable-vsync 放在 -- 之前；跑 40 秒，前 20 秒不算；不要同时截图
godot-mono --path src/Tank.Client --disable-vsync res://scenes/Sandbox.tscn -- --map riverland --camera-preset spin \
  --fps-log --quit-after 40 > build/shots/5a-tNN/spin.log 2>&1 & P=$!; ( sleep 75; kill -9 $P ) >/dev/null 2>&1 & wait $P
grep -E "fps-log|ERROR|SHADER|Exception" build/shots/5a-tNN/spin.log
# 两个实例（房主 + 客户端），日志在 OUTDIR/TAG-host.log、TAG-guest.log
$S/pair4b.sh $W $W/build/shots/5a-tNN pair 150 "房主参数" "客户端参数" 3
pgrep -fl 'MacOS/Godot'                                     # 每个任务结束都要确认没有残留的 Godot 进程
```

- 这个工作目录已经有 `.godot/`；如果字体等资源加载不到，先跑一次 `--import`。
- `godot_run.sh WORKTREE SCENE TIMEOUT args…` 会在 TIMEOUT 秒后强制结束；河谷第一次生成在客户端（Debug 构建）里可能要几秒，超时给 60 秒以上，`--screenshot-at` 从场景开始跑算起。
- **截图**：路径必须是绝对路径；每张都要人工看（用 Read 打开 PNG），在进度说明里写看到了什么。凡是动了客户端或仿真的任务，都要重拍六张回归截图并和基线 `build/shots/base/` 比较：差异只允许出现在云、地平线、草叶摇动、粒子上（`pngdiffdirs.py` 给的比例以往 orbit 约 23%、heavy 约 8% 来自环绕镜头的时间差，要看图判断，不只看数）。
- **基线**：批次 A 开工前（任务 1 的第一步）先拍一套基线到 `build/shots/base/`，再测试驾场 `spin`、`orbit` 的帧率存到 `build/shots/base/{spin,orbit}.log`，作为“试驾场帧率不下降”的对照。
- **帧率**：窗口要在最前面。macOS 会把后台窗口限到 60 帧，整段都是 60 帧说明窗口被挡，重测。测帧率时不截图。目标（河谷设计 §6.9）：河谷 1600×900、关垂直同步，`spin`、`orbit` 平均 60 帧以上、20 秒以后单帧最差不超过 33 ms；试驾场比基线低不超过 10%。

**提交**：

- 说明用英文一行，带范围前缀：`feat(sim): …`、`feat(net): …`、`feat(server): …`、`feat(client): …`、`test(sim): …`、`perf(client): …`、`fix(<scope>): batch X review follow-up — …`、`docs: …`。小写开头，用破折号分句。最后一行 `Tests: Sim 639 → 650.`（列出有变化的项目）。
- 作者用仓库配置（`火狐`），不加 `Co-Authored-By`，不推送远端。
- 只提交本任务的文件；`.uid` 和源文件一起提交。

---

## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Maps/PropIndex.cs` | 新建 | 内部的 8 m 均匀网格索引：按用途分组（`PropSet`），`Near`/`Along` 返回按下标升序、去重的候选 | 1、3 |
| `Maps/Battlefield.cs` | 修改 | 建索引；`Raycast`、`BushesNear` 走索引；`Near`/`Along`/`All`/`UseIndex`；地面图、水位、`SurfaceAt`/`WaterDepthAt`/`SpeedFactorAt`；`StateVersion`；`Fresh()`；`Bushes` 改名 `Foliage` | 1、2、3 |
| `Vehicles/TankMotion.cs` | 修改 | 撞倒、推出、穿透深度、压瓦砾走索引；目标速度乘地面系数；树和房子的撞倒规则 | 1、2、3、4 |
| `Simulation.Props.cs` | 修改 | 穿过（`KnockThrough`）、溅射（`SplashProps`）走索引；圆形物件按圆柱算 | 1、3 |
| `Vision/Spotting.cs` | 修改 | `BushesHiding` 走索引、算上树冠 | 1、3 |
| `Maps/SurfaceKind.cs`、`Maps/SurfaceMap.cs`、`Maps/GroundSpeed.cs` | 新建 | 地面类型、地面网格、速度系数 | 2 |
| `Maps/MapData.cs` | 修改 | 带地面类型和水位 | 2 |
| `Maps/Prop.cs` | 修改 | `House`、`Tree`；`LastKind`、`CrownPerTrunk`、`IsRound`、`IsFoliage`、`FoliageRadius`；`IsWall` 含房子；耐久；废墟范围 | 3、4 |
| `Vehicles/TankSpec.cs`、`Vehicles/TankCatalog.cs` | 修改 | `HouseRamSpeed` | 4 |
| `Maps/Building/ValueNoise.cs`、`Polyline.cs`、`DistanceGrid.cs`、`HeightSculpt.cs`、`PlacementGrid.cs`、`PoissonDisc.cs`、`Passability.cs` | 新建 | 生成用的通用工具 | 5 |
| `Maps/RiverlandMap.cs` | 新建 | 河谷的对外入口：常数、`Create()`、出生点、路点、试驾场站位、地标 | 6～9 |
| `Maps/Riverland/RiverlandLayout.cs`、`RiverlandTerrain.cs`、`RiverlandSurface.cs`、`RiverlandVillages.cs`、`RiverlandScatter.cs` | 新建 | 布局常数、地形和水、地面、房子院墙栅栏、岩石草丛树（都是 `internal`） | 6、7、8 |
| `Maps/MapDefinition.cs`、`Maps/MapCatalog.cs` | 新建 | 地图目录 | 9 |
| `Maps/SandboxMap.cs` | 修改 | 只加 `public const string Id = "sandbox";`（地形、物件逐位不变） | 9 |

**联网 `src/Tank.Net`、服务器 `src/Tank.Server`**

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `Battle/BattleServer.cs` | 修改 | `MaxUnreliableBytes` 1150 → 1000（`internal`）和 `ChannelFor`；房间信息带地图；每 tick 看后台地图好了没有 | 10、12、13 |
| `Protocol/BitWriter.cs` | 修改 | `WriteAligned`（按字节原样拷贝，不带长度） | 11 |
| `Battle/BattleProtocol.cs` | 修改 | `Version = 3`、`MaxMapIdBytes` | 11、13 |
| `Battle/BattleMessages.cs` | 修改 | 地图编码追加段、物件上限 16384、种类上限取自仿真、开局物件状态数上限、预编码地图的开局写法；房间状态和房间信息带地图、`RoomCommandKind.SetMap` | 11、13 |
| `Battle/ServerMaps.cs` | 新建 | 服务器的地图准备：生成和编码各一次、可在后台线程、按 `MapDefinition` 缓存 | 12 |
| `Battle/BattleServerOptions.cs` | 修改 | `Map`、`Maps`、`PrepareMapsInBackground` | 12、13 |
| `Battle/BattleServer.Match.cs` | 修改 | 按房间的地图开局（出生点、路点、预编码的地图字节）；地图没准备好不开局 | 12 |
| `Battle/BattleServer.Room.cs` | 修改 | `SetMap` 命令；房间状态带地图和“地图已就绪” | 13 |
| `Battle/BattleClient.cs` | 修改 | `SetMap(string mapId)` | 13 |
| `src/Tank.Server/Program.cs` | 修改 | `--map sandbox\|riverland`（默认 riverland），后台准备地图 | 12 |

**客户端 `src/Tank.Client`**（路径相对 `src/Tank.Client/`）

| 文件 | 新建/修改 | 职责 | 任务 |
|---|---|---|---|
| `scripts/World/WallViews.cs` | 修改 | 只画砖墙、石墙和栅栏（房子不再当墙段画） | 4 |
| `scripts/Bootstrap/LaunchOptions.cs` | 修改 | `--map`（用 `MapCatalog` 校验，存规范 id） | 15 |
| `scripts/Sandbox/Logic/MapSites.cs` | 新建（引擎无关） | 每张图的站点：地图、窗口标题、试驾场出生、靶车、有无巡逻车、机位锚点、机位名单、演示 | 15、24 |
| `scripts/UI/Logic/MapText.cs` | 新建（引擎无关） | 地图显示名、试驾场和对局的窗口标题 | 15 |
| `scripts/Sandbox/SandboxMain.cs` | 修改 | 从站点取地图、标题、出生点；`map` 正交机位 | 15、18 |
| `scripts/Sandbox/TargetRange.cs` | 修改 | 靶车取自站点；没有巡逻车时不建、不驱动、不补弹 | 15 |
| `scripts/Sandbox/SandboxAutomation.cs` | 修改 | 演示和机位按站点查、写错时按站点给警告 | 15 |
| `scripts/Sandbox/RangeDemo.cs` | 修改 | 可选 `Spots`、`AimAt` | 15 |
| `scripts/Cameras/CameraPresets.cs` | 修改 | 按站点的机位；`map` 正交俯视；`UpFor`、`IsOrthographic`、`IsKnownFor` | 15 |
| `scripts/Cameras/OrbitCamera.cs` | 修改 | 镜头不沉到水面以下 | 18 |
| `scripts/World/BattleWorld.cs` | 修改 | 地面图层、水面、树、房子的挂点（建、每帧同步、事件）；`UseMapView`；状态版本门控 | 15、17、18、21、22、23 |
| `scripts/Online/Logic/HostSettings.cs`、`ThreadedHost.cs`、`LoopbackHost.cs` | 修改 | 房间的初始地图；房主线程在后台准备地图 | 16 |
| `scripts/Bootstrap/Main.Flow.cs` | 修改 | 开房默认河谷（`--map` 可改） | 16 |
| `scripts/UI/Logic/RoomModel.cs`、`UI/Logic/RoomRules.cs`、`UI/RoomScreen.cs` | 修改 | 房间里的地图选择、标题、“地图生成中…” | 16 |
| `scripts/Online/Logic/LanRoomRow.cs`、`Online/Logic/AutoRoom.cs` | 修改 | 房间列表多一列地图；自动开局等地图就绪 | 16 |
| `scripts/Match/MatchMain.cs`、`Match/MatchMain.Camera.cs`、`Match/MatchAutomation.cs` | 修改 | 对局窗口标题 `坦克 3D · 河谷`；机位按站点；自由镜头焦点高度取地形和水位中较高的 | 16、18 |
| `scripts/Art/Mesh/GroundLayers.cs` | 新建（引擎无关） | 地面类型烘成 RGBA（非草地、沙、路、备用），3×3 模糊，双线性取值 | 17 |
| `scripts/World/DirtMask.cs`、`World/TerrainMesh.cs`、`shaders/terrain_toon.gdshader`、`scripts/Art/Palette.cs` | 修改 | 分层遮罩；地形着色器的沙地、公路、河床、湿岸 | 17 |
| `scripts/World/GrassField.cs`、`World/TreeLine.cs` | 修改 | 长草时按桶查物件；图外树带避开水和公路 | 17 |
| `scripts/Art/Mesh/WaterMesh.cs` | 新建（引擎无关） | 水面网格 | 18 |
| `scripts/World/WaterSurface.cs`、`shaders/water.gdshader`、`shaders/dither.gdshaderinc` | 新建 | 水面节点和着色器；抖动函数从 `bush.gdshader` 抽出来共用 | 18 |
| `shaders/bush.gdshader` | 修改 | 改用 `dither.gdshaderinc`（画面不变） | 18 |
| `scripts/Combat/Logic/SplashRules.cs` | 新建（引擎无关） | 什么时候算涉水、什么时候算落水 | 19 |
| `scripts/Combat/DustTrail.cs`、`Combat/BattleFx.cs` | 修改 | 涉水水花、落水水柱（自己查水面） | 19 |
| `scripts/Overlay/Logic/GroundText.cs` | 新建（引擎无关） | 地面提示的文字 | 20 |
| `scripts/Overlay/StatusPanel.cs`、`Overlay/CombatHud.cs` | 修改 | 状态面板上方的地面提示（试驾场和对局共用） | 20 |
| `scripts/Art/Mesh/TreeShapes.cs`、`Art/Mesh/TreeFall.cs`（新建） | 修改、新建 | 林中树的树干粗细、按物件缩放、倒下姿态 | 21 |
| `scripts/World/ForestViews.cs`、`shaders/tree.gdshader`、`scripts/Combat/PropFx.cs` | 新建、修改 | 林中树、倒下动画、挡视线时半透明、倒树特效 | 21 |
| `scripts/Art/Mesh/HouseShapes.cs`、`World/HouseViews.cs`、`shaders/house.gdshader` | 新建 | 房子、倒塌、废墟 | 22 |
| `scripts/Art/Mesh/SectorGrid.cs` | 新建（引擎无关） | 按扇区分组的键（林中树先用，任务 23 的岩石和草丛也用） | 21、23 |
| `scripts/World/RockMeshes.cs`、`World/BushField.cs`、`World/TerrainMesh.cs` | 修改 | 性能：岩石按扇区合并、碎石和草丛按扇区分组、必要时远处地形降采样 | 23 |
| `scripts/Sandbox/Logic/RiverlandDemos.cs` | 新建（引擎无关） | 河谷的四个演示 | 24 |

**测试**

| 项目 | 新文件 | 任务 |
|---|---|---|
| `tests/Tank.Sim.Tests` | `SandboxFingerprintTests`、`PropIndexTests`、`PropIndexEquivalenceTests` | 1 |
| | `GroundSpeedTests`、`SurfaceMapTests`、`BattlefieldGroundTests`、`GroundDrivingTests`、`MapDataSurfaceTests` | 2 |
| | `TreeTests`、`TreeSpottingTests` | 3 |
| | `HouseTests`、`HouseRamTests` | 4 |
| | `MapBuildingTests` | 5 |
| | `RiverlandFixture`（共用：整个测试进程只生成一次）、`RiverlandTerrainTests` | 6 |
| | `RiverlandVillageTests` | 7 |
| | `RiverlandScatterTests` | 8 |
| | `RiverlandSpawnTests`、`RiverlandSitesTests`、`MapCatalogTests`、`RiverlandFingerprintTests`、`RiverlandPerformanceTests` | 9 |
| `tests/Tank.Net.Tests/Battle` | `UnreliableLimitTests` | 10 |
| | `MapWireV3Tests`（并改 `BattleProtocolTests` 里的版本断言） | 11 |
| | `ServerMapTests`、`TestMaps`（共用的小地图） | 12 |
| | `RoomMapTests` | 13 |
| | `RiverlandMatchTests` | 14 |
| `tests/Tank.Art.Tests` | `LaunchOptionsMapTests`、`MapSitesTests`、`MapTextTests` | 15 |
| | `RoomMapModelTests` | 16 |
| | `GroundLayersTests`（并扩充 `TerrainShaderParityTests`） | 17 |
| | `WaterMeshTests` | 18 |
| | `SplashRulesTests` | 19 |
| | `GroundTextTests` | 20 |
| | `TreeFallTests`、`ForestTreeShapeTests`（含 `SectorGrid` 的用例） | 21 |
| | `HouseShapesTests` | 22 |
| | `SectorGridTests`（只在加了新的引擎无关帮助方法时） | 23 |
| | `RiverlandDemoTests` | 24 |

---

## 共享接口

所有任务都按这里的名字和签名写。改接口先改这一节。

### 必须保持源码兼容的现有签名

下面这些有现成的调用方（已 grep 核对），只能**追加可选参数或新重载**，不能改已有参数：

| 签名 | 调用方 |
|---|---|
| `Battlefield(HeightField terrain, IEnumerable<Prop> props, float border)` | `SandboxMap.Create`、`MapData.Build`、`tests/Tank.Sim.Tests/TestWorld.Field` 等大量测试 |
| `Battlefield.Props / Terrain / Border / HasProp / PropById / IsStanding / Durability / SetDurability / ClampToPlayable / Raycast(Vector3, Vector3, out RayHit) / BushesNear(Vector2, float) / BushesNear(Vector2, float, List<Prop>)` | 仿真、`BattleClient.Match`、`CombatHud`、`AimMath`、测试 |
| `MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border)`、`MapData.Of(Battlefield)`、`MapData.Build()` | `BattleMessages.ReadMap`、`BattleServer.Match`、`BattleClient.Match`、`TankStepTests`、`BattleProtocolTests` |
| `record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)`；`PropKind` 现有的值 0～4 | 线上格式和所有地图 |
| `TankMotion.Drive(ref TankState, in InputCmd, Battlefield, ReadOnlySpan<Obb2>, List<int>? felled = null, bool knockDown = true)`、`TankMotion.Footprint`、`TankStep.Advance` | 仿真、`BattlePredictor`、测试 |
| `BattleMessages.WriteMap(BitWriter, MapData)`、`ReadMap(ref BitReader)`、`Write(BitWriter, MatchStartMessage)`、`ReadMatchStart` | 服务器、客户端、`BattleProtocolTests` |
| `RoomStateMessage(BattleMode Mode, byte HostSlot, byte Bots, bool MatchRunning, IReadOnlyList<RoomPlayer> Players)` | 服务器、`BattleProtocolTests`、`RoomRulesTests` |
| `RoomInfoMessage(int ProtocolVersion, string RoomName, BattleMode Mode, int Players, int MaxPlayers, bool MatchRunning, int Port)` | 服务器、`LanRoomRowTests`、`DiscoveryTests`、`BattleProtocolTests` |
| `HostSettings(string PlayerName, string RoomName, int Port, BattleMode Mode, int Bots, uint? Seed, double? MatchSeconds)` | `Main.Flow.Host`、`ReconnectSessionTests`、`MatchHudFrameTests` |
| `BattleServerOptions` 现有的 init 属性 | 服务器入口、Soak、Net 和 Art 测试 |
| `RangeDemo(int Target, float AngleOffFront, float Distance, DemoAim Aim, AmmoType Ammo, bool KeepFiring, float PlayerTurn = 0f, float DriveSeconds = 0f, double FireAfter = DefaultFireAfter, SN.Vector2? Stand = null, bool PatrolDrives = false)`、`RangeDemo.Find/Names/Spot/AimLocal/AimPoint` | `RangeDemoTests` |
| `CameraPresets.Get(string name, Vector3 tank, float tankYaw, double time, Func<float, float, float> groundAt, int targetIndex)`、`Spawn(string?)`、`IsKnown(string?)`、`Names`、`StreamsAroundCamera(string?)` | `SandboxMain`、`MatchMain`、`MatchAutomation`、`SandboxAutomation` |
| `OrbitCamera.Follow(Vector3 pivot, Vector3 sniperEye, HeightField terrain)` | `SandboxMain`、`MatchMain.Camera`（三处） |
| `BattleWorld.Build(Battlefield, in TankState focus, MapStyle style, bool streamsAroundCamera)`、`Update(TankState?, TankPose?, Vector3, float)`、`OnEvent(in GameEvent)`、`SetField(Battlefield)` | `SandboxMain`、`MatchMain` |
| `TerrainMesh.Build(HeightField, DirtMask)`、`DirtMask(float sizeX, float sizeZ, IReadOnlyList<Vector2> grassIslands, uint seed)`、`GrassField.Build(HeightField, DirtMask, IReadOnlyList<Prop>)`、`TreeLine.Build(Battlefield)` | `BattleWorld` |
| `TreeShapes.Conifer(uint seed)`、`TreeShapes.Broadleaf(uint seed)` | `TreeLine`、`VegetationTests` |
| `CombatHud.Build(...)`、`CombatHud.Refresh(in TankState player, bool spotted)` | `SandboxHud`、`MatchMain` |

### 仿真：空间索引（任务 1；任务 3 让 `Foliage` 组带上树冠）

```csharp
namespace Tank.Sim.Maps;

/// <summary>The groups of props the simulation queries (riverland design §4.1).</summary>
internal enum PropSet : byte { Blockers, ShellStoppers, Flimsy, Walls, Foliage }

// Battlefield (internal; Tank.Sim.Tests sees it):
internal const float IndexCellSize = 8f;
internal ReadOnlySpan<int> All(PropSet set);          // ascending indices of the set's props (what the old private arrays held)
/// Ascending, distinct indices of the set's props whose registered circle (table below) reaches the disc (center, radius):
/// a superset of what an exact test there can hit. All(set) when the index is off or buffer is too small for the result.
internal ReadOnlySpan<int> Near(PropSet set, Vector2 center, float radius, Span<int> buffer);
/// The same for the ground segment from → to widened by margin (+ 0.01 m against cell-corner misses).
internal ReadOnlySpan<int> Along(PropSet set, Vector2 from, Vector2 to, float margin, Span<int> buffer);
internal bool UseIndex { get; set; } = true;          // tests only: false makes Near/Along return All(set)
```

| 组 | 成员 | 登记半径（以占地中心为圆心） |
|---|---|---|
| `Blockers` | `IsSolid`（岩石、墙、栅栏、房子、树） | 占地外接圆 `Footprint.HalfExtents.Length()` |
| `ShellStoppers` | `StopsShells`（岩石、墙、房子） | 占地外接圆 |
| `Flimsy` | `IsFlimsy`（栅栏、草丛、树） | 占地外接圆（树就是树干） |
| `Walls` | `IsWall`（砖墙、石墙、房子） | 废墟占地外接圆 `RubbleFootprint.HalfExtents.Length()` |
| `Foliage` | `IsFoliage`（草丛、树） | `FoliageRadius`（树按树冠） |

- 实现在 `Maps/PropIndex.cs`（`internal sealed class PropIndex`），`Battlefield` 构造时建好，之后不变。格子覆盖 `[0, SizeX] × [0, SizeZ]`，8 m 一格；每组一份 CSR 数组（`int[] cellStart`、`int[] items`），一个物件登记进它登记圆的外接方框覆盖的所有格子，超出地图的部分夹到边上的格子。
- 查询不写任何内部状态（去重靠 `Span<int>.Sort()` 后原地去重），多线程只读安全；调用方传 `stackalloc int[512]`（`Raycast` 用 1024）。
- `Battlefield` 原来的 `internal ReadOnlySpan<int> Blockers/Flimsy/Walls/Bushes` 删掉，调用方改用 `All`/`Near`/`Along`；`Bushes` 的唯一调用方是 `Spotting.BushesHiding`。`BushesNear` 两个公开重载的语义不变（只返回立着的草丛）。

### 仿真：地面和水（任务 2）

```csharp
namespace Tank.Sim.Maps;

/// <summary>What the ground is made of, per 2 m cell (riverland design §2.1). Values are on the wire: append only.</summary>
public enum SurfaceKind : byte { Grass = 0, Dirt = 1, Sand = 2, Road = 3 }

/// <summary>Immutable grid of SurfaceKind, one per height-field cell: cell (i, j) covers x ∈ [i·CellSize, (i+1)·CellSize), z likewise.</summary>
public sealed class SurfaceMap
{
    public const SurfaceKind LastKind = SurfaceKind.Road;
    public SurfaceMap(int cellsX, int cellsZ, float cellSize, SurfaceKind[] cells);   // checks sizes ≥ 1, finite cellSize > 0, length, every kind ≤ LastKind; copies cells
    public static SurfaceMap Uniform(int cellsX, int cellsZ, float cellSize, SurfaceKind kind = SurfaceKind.Grass);
    public int CellsX { get; }
    public int CellsZ { get; }
    public float CellSize { get; }
    public ReadOnlySpan<SurfaceKind> Cells { get; }        // row-major: j * CellsX + i
    public SurfaceKind this[int i, int j] { get; }          // i, j clamped to the grid
    public SurfaceKind At(float x, float z);                // the cell under (x, z), clamped to the grid
    public bool IsUniform(SurfaceKind kind);                // every cell is kind
}

/// <summary>Top-speed factors of the ground and of shallow water (riverland design §2.1–2.3).</summary>
public static class GroundSpeed
{
    public const float Road = 1.15f, Sand = 0.8f;
    public const float WaterDepthScale = 3f, WaterMin = 0.5f, WaterMax = 0.8f;
    public static float Of(SurfaceKind kind);                       // Road 1.15, Sand 0.8, Grass and Dirt 1
    public static float InWater(float depth);                       // Clamp(1 − depth / 3, 0.5, 0.8)
    public static float Factor(SurfaceKind kind, float waterDepth); // waterDepth > 0 ? InWater(waterDepth) : Of(kind)
}
```

`Battlefield` 新增（三参数构造照常可用）：

```csharp
public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border, SurfaceMap? surface = null, float? waterLevel = null);
// surface null → SurfaceMap.Uniform(SamplesX − 1, SamplesZ − 1, CellSize); otherwise its CellsX/CellsZ/CellSize must equal the
// terrain's cells (ArgumentException). waterLevel must be finite when given (ArgumentException).
public SurfaceMap Surface { get; }
public float? WaterLevel { get; }
public SurfaceKind SurfaceAt(Vector2 ground);
public float WaterDepthAt(Vector2 ground);    // max(0, WaterLevel − Terrain.HeightAt); 0 without water
public float SpeedFactorAt(Vector2 ground);   // GroundSpeed.Factor(SurfaceAt, WaterDepthAt)
public int StateVersion { get; }              // +1 whenever a prop's standing or hit points really change (Damage, Destroy, Restore, SetDurability)
public Battlefield Fresh();                   // same terrain, props, surface and water (shared, immutable); every prop standing at full hit points; StateVersion 0
```

`MapData` 新增（六参数构造照常可用，等于全草地、无水）：

```csharp
public MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border,
    SurfaceKind[]? surface, int? waterLevelCm);
public const int MaxWaterLevelCm = 1_000_000;
public SurfaceKind[] Surface { get; }   // (samplesX − 1) × (samplesZ − 1), row-major; all Grass when null was given (ArgumentException on a wrong length or kind > SurfaceMap.LastKind)
public int? WaterLevelCm { get; }       // |value| ≤ MaxWaterLevelCm (ArgumentException)
// Of(field): + field.Surface.Cells, WaterLevelCm = round(field.WaterLevel / HeightUnit); Build(): passes both on.
```

`TankMotion` 的目标速度：油门不为 0 时乘 `field.SpeedFactorAt(t.Position)`（车体中心），再乘原有的瓦砾系数和坡度修正（河谷设计 §2.3）。加速度、刹车、转向不变。

### 仿真：物件（任务 3、4）

```csharp
public enum PropKind : byte { Rock = 0, Bush = 1, BrickWall = 2, StoneWall = 3, Fence = 4, House = 5, Tree = 6 }

public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    public const PropKind LastKind = PropKind.Tree;
    public const float RubbleSpread = 0.5f;                                                   // unchanged
    public const float CrownPerTrunk = 7f;                                                    // a tree's crown radius = trunk radius × this
    public bool IsSolid => Kind != PropKind.Bush;                                            // unchanged formula: now also houses and trees
    public bool IsWall => Kind is PropKind.BrickWall or PropKind.StoneWall or PropKind.House; // "masonry" (riverland design §2.5)
    public bool StopsShells => Kind == PropKind.Rock || IsWall;                               // unchanged formula: now also houses
    public bool IsFlimsy => IsDestructible && !StopsShells;                                   // unchanged formula: now also trees
    public bool IsRound => Kind is PropKind.Bush or PropKind.Tree;                            // footprint is a circle of radius HalfExtents.X
    public bool IsFoliage => Kind is PropKind.Bush or PropKind.Tree;                          // hides tanks (riverland design §2.4)
    public float FoliageRadius => Kind == PropKind.Tree ? Footprint.HalfExtents.X * CrownPerTrunk : Footprint.HalfExtents.X;
    public int MaxDurability => Kind switch { BrickWall => 300, StoneWall => 900, House => 500, Fence or Bush or Tree => 1, _ => 0 };
    public float DistanceTo(Vector3 point);   // round kinds: the upright cylinder of HalfExtents.X (a tree's trunk); else the prism, as now
    public Obb2 RubbleFootprint { get; }      // House: HalfExtents + (RubbleSpread, RubbleSpread); walls: + (0, RubbleSpread), as now
}
```

- `Battlefield` 构造时检查：`IsRound` 的物件 `HalfExtents.X == HalfExtents.Y`（原来只查草丛）。
- 房子的占地：`HalfExtents.X` 是长的一半（沿 `AxisRight`，屋脊方向），`HalfExtents.Y` 是宽的一半；正面（门）朝 `AxisForward`。`Bottom` 是地块地面往下 0.3 m（同 `WallLine.Sink`），`Height` 到屋脊再加 0.3 m。
- 树的占地：圆，半径 = 树干半径（0.35～0.6 m）；`Bottom` = 地面 − 0.3 m，`Height` = 树高 + 0.3 m。

`TankSpec` 新增：

```csharp
/// <summary>Above this speed (m/s, either way) the hull knocks a house down outright, losing half its speed (riverland design §2.5).</summary>
public required float HouseRamSpeed { get; init; }   // TankCatalog: Medium, Heavy 20 * Kmh; Light, Td float.PositiveInfinity
```

### 仿真：生成工具（任务 5）

```csharp
namespace Tank.Sim.Maps.Building;

public static class ValueNoise   // the same hash/value-noise idea as TerrainGenerator, public and seeded by uint (TerrainGenerator itself is untouched)
{
    public static float Value(uint seed, float x, float z);              // [0, 1), one feature per unit
    public static float Fbm(uint seed, float x, float z, int octaves);   // [0, 1]
    public static float Signed(uint seed, float x, float z, int octaves); // Fbm * 2 − 1
}

public sealed class DistanceGrid   // per height-field sample: distance to the nearest stamped line, and how far along it
{
    public DistanceGrid(int samplesX, int samplesZ, float cellSize);    // Distance all +∞, Along all 0, Line all −1
    public int SamplesX { get; } public int SamplesZ { get; } public float CellSize { get; }
    public float[] Distance { get; } public float[] Along { get; } public int[] Line { get; }   // index j * SamplesX + i
    public float DistanceAt(float x, float z);                           // bilinear
}

public sealed class Polyline
{
    public Polyline(IReadOnlyList<Vector2> controlPoints, float step = 2f);   // centripetal Catmull-Rom through every control point, resampled every `step` m
    public IReadOnlyList<Vector2> Points { get; }
    public float Length { get; }
    public Vector2 PointAt(float along);
    public Vector2 TangentAt(float along);                     // unit
    public float DistanceTo(Vector2 p, out float along);       // brute force over the segments
    public void Stamp(DistanceGrid grid, float reach, int lineId);   // every sample within reach gets min(distance) and its along, Line = lineId when nearer
}

public static class HeightSculpt
{
    public static float SmoothStep(float edge0, float edge1, float x);
    public static float Bump(float distance, float radius);    // TerrainGenerator.Hill's (1 − d²)² profile: 1 at 0, 0 at radius and beyond
    public static void Flatten(float[] heights, int samplesX, float cellSize, in Obb2 area, float height, float blend);   // area to height, eased back to the old heights over blend m
}

public sealed class PlacementGrid   // circles in a uniform grid, for neighbour checks while scattering
{
    public PlacementGrid(float sizeX, float sizeZ, float cellSize);
    public void Add(Vector2 center, float radius);
    public bool AnyWithin(Vector2 center, float radius, float gap);   // some added circle has distance < radius + its radius + gap
    public int Count { get; }
}

public static class PoissonDisc   // Bridson's sampling, deterministic for a given rng state
{
    public static List<Vector2> Sample(ref XorShift rng, Vector2 min, Vector2 max, Func<Vector2, float> minDistance,
        Func<Vector2, bool> accept, int attempts = 30);
}

public sealed class Passability   // riverland design §9: 2 m cells (the terrain's); 5C builds its 4 m nav grid separately
{
    public Passability(Battlefield field, float maxSlopeRadians, float clearance);   // clearance: footprints of rocks, houses and walls grown by this
    public bool IsPassable(int i, int j);
    public bool IsPassableAt(Vector2 p);
    public int ComponentAt(Vector2 p);           // −1 on an impassable cell
    public bool Connected(Vector2 a, Vector2 b);
}
```

不可通行：坡度大于 `maxSlopeRadians`（河谷用四种车 `MaxClimb` 的最小值 32°）、岩石、房子、砖墙、石墙（占地加宽 `clearance`，河谷用 1.6 m，约半个中坦车宽）、可玩区以外。树、水、栅栏、草丛可通行。连通按 4 邻接的洪泛填充。

### 仿真：地图（任务 6～9）

```csharp
namespace Tank.Sim.Maps;

public static class RiverlandMap
{
    public const string Id = "riverland";
    public const int Seed = 20261019;
    public const int Samples = 601;
    public const float CellSize = 2f, Border = 30f, WaterLevel = 0f;
    public static Battlefield Create();                              // generates the whole map (riverland design §3.12)
    public static readonly SpawnLayout Spawns;                       // Team: 5 (team 0, west) + 5 (team 1, east); Solo: 16
    public static readonly IReadOnlyList<Vector2> Waypoints;         // 35–45
    public static readonly Vector2 Spawn;                            // the test drive's spawn (riverland design §3.11)
    public static readonly float SpawnYaw;
    public static readonly IReadOnlyList<TargetSpot> TargetSpots;    // 8, in riverland design §3.11 order
    public const int RoadTarget = 0, HouseTarget = 1, FordTarget = 2, ForestTarget = 3, BushTarget = 4,
                     PlateauTarget = 5, BeachTarget = 6, FieldTarget = 7;
    public static readonly RiverlandLandmarks Landmarks;
}

public sealed record RiverlandLandmarks(Vector2 Town, Vector2 Ford, Vector2 SouthFord, Vector2 ForestEdge,
    Vector2 PlateauRim, Vector2 Dunes, Vector2 Lake, Vector2 MapCentre);

public sealed class MapDefinition
{
    public MapDefinition(string id, string name, Func<Battlefield> create, SpawnLayout spawns, IReadOnlyList<Vector2> waypoints);
    public string Id { get; }                         // "sandbox", "riverland"
    public string Name { get; }                       // "试驾场地图", "河谷"
    public Func<Battlefield> Create { get; }          // the generator
    public SpawnLayout Spawns { get; }
    public IReadOnlyList<Vector2> Waypoints { get; }
    public MapData Data { get; }                      // MapData.Of(the generated field), built once (Lazy, ExecutionAndPublication)
    public bool IsDataReady { get; }                  // Data has been built
    public Battlefield NewBattlefield();              // the generated field (built once, kept private) .Fresh(): exact floats, fresh prop state
}

public static class MapCatalog
{
    public static MapDefinition Sandbox { get; }      // ("sandbox", "试驾场地图", SandboxMap.Create, SandboxMap.Spawns, SandboxMap.Waypoints)
    public static MapDefinition Riverland { get; }    // ("riverland", "河谷", RiverlandMap.Create, RiverlandMap.Spawns, RiverlandMap.Waypoints)
    public static IReadOnlyList<MapDefinition> All { get; }          // Sandbox first, then Riverland
    public static bool TryGet(string? id, out MapDefinition map);    // ordinal, ignore case; false for null or unknown
    public static MapDefinition Get(string id);                      // ArgumentException for an unknown id
}

// SandboxMap: one new line, nothing else changes
public const string Id = "sandbox";
```

物件编号的顺序（河谷设计 §3.12）：岩石、草丛、树、房子、院墙和栅栏，从 1 起连续。摆放顺序和编号顺序无关。

### 联网（任务 10～13）

```csharp
// Tank.Net.Protocol.BitWriter
public void WriteAligned(ReadOnlySpan<byte> bytes);   // appends the bytes as they are (no length); InvalidOperationException unless BitLength % 8 == 0
public void Reserve(int extraBytes);                  // grows once to fit that many more bytes (C 批跟进)

// Tank.Net.Battle.BattleProtocol
public const int Version = 3;                         // ConnectKey stays "tank3d-v2": an old client still connects and is told the versions differ
public const int MaxMapIdBytes = 16;

// Tank.Net.Battle.BattleMessages
public const int MaxMapProps = 16384;                 // MaxPropsPerMessage (4096) stays for event batches
// WriteMap: the v2 bytes unchanged, then: u8 flags (bit 0: water; other bits must be 0) · [VarInt waterLevelCm] ·
//           VarUInt runs · runs × { VarUInt length ≥ 1, u8 kind ≤ SurfaceMap.LastKind } (lengths sum to the cell count)
// ReadMap: prop kind ≤ Prop.LastKind; prop count ≤ MaxMapProps and ≤ RemainingBits / 240 before allocating;
//          runs ≤ cells and ≤ RemainingBits / 16 before allocating; water level within MapData.MaxWaterLevelCm
// ReadMatchStart: prop-state count ≤ the map's prop count (≤ MaxMapProps without a map)
public static void Write(BitWriter w, MatchStartMessage m, ReadOnlySpan<byte> encodedMap);   // the same bytes as Write(w, m), the map copied in (WriteAligned); reserves the whole message first

// C 批跟进：开局消息的地图可省（重同步不重发整张图），请求重同步带标志，房间状态的地图三态
public sealed record MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData? Map, IReadOnlyList<PropState> Props, int MyTankId);
// wire: type · mode · VarUInt number, tick, end · u8 flags (bit 0: the map follows) · [map] · props · VarUInt tank
public sealed record MatchStateRequest(bool NeedMap);  // wire: type · u8 flags (bit 0: send the map)
public enum MapState : byte { Generating = 0, Ready = 1, Failed = 2 }

public sealed record RoomStateMessage(BattleMode Mode, byte HostSlot, byte Bots, bool MatchRunning, IReadOnlyList<RoomPlayer> Players,
    string Map = SandboxMap.Id, MapState MapState = MapState.Ready);   // wire: after the players, String(Map, MaxMapIdBytes), u8 MapState (≤ Failed)
public sealed record RoomInfoMessage(int ProtocolVersion, string RoomName, BattleMode Mode, int Players, int MaxPlayers, bool MatchRunning, int Port,
    string Map = SandboxMap.Id);                          // wire: after the port, String(Map, MaxMapIdBytes); read only when ProtocolVersion ≥ 3 (else Map = "")
public enum RoomCommandKind : byte { SetMode = 0, SetTeam = 1, SetTank = 2, SetBots = 3, SetReady = 4, StartMatch = 5, ReturnToRoom = 6, SetMap = 7 }

// Tank.Net.Battle.BattleClient
public void SetMap(string mapId) => SendRoom(RoomCommandKind.SetMap, mapId);

// Tank.Net.Battle.BattleServerOptions
public MapDefinition Map { get; init; } = MapCatalog.Sandbox;          // the room's first map (the library default; the game's entry points pass riverland)
public IReadOnlyList<MapDefinition> Maps { get; init; } = MapCatalog.All;   // what SetMap may pick, by Id (ignore case)
public bool PrepareMapsInBackground { get; init; }                    // false: generate and encode on the calling thread (tests, loopback host)

// Tank.Net.Battle.BattleServer
internal const int MaxUnreliableBytes = 1000;         // was 1150: LiteNetLib 2.1.4 sends at most 1023 unreliable bytes (MTU 1024, discovery off)
internal static Channel ChannelFor(int bytes);        // bytes > MaxUnreliableBytes ? Reliable : Unreliable
public MapDefinition RoomMap { get; }                 // the room's current map
public bool RoomMapReady { get; }                     // its data generated and encoded
public MapState RoomMapState { get; }                 // Generating / Ready / Failed
public MapState MapStateOf(MapDefinition map);        // Tank.Server exits (code 3) when its first map fails
// constructor: Map and Maps not null (ArgumentNullException); every id ≤ MaxMapIdBytes in UTF-8, unique ignoring case, Map ∈ Maps (ArgumentException)

// Tank.Net.Battle.ServerMaps (internal)
internal sealed class PreparedMap { MapDefinition Definition; MapData Data; byte[] EncodedMap; }   // a class: no value equality over a byte[]
internal sealed class ServerMaps
{
    public ServerMaps(bool background, Action<string> log);
    public void Prepare(MapDefinition map);           // no-op if prepared or under way; else generate, warm map.NewBattlefield(), encode, on a worker (Task.Run) or right here; never throws a generator's exception
    public MapState StateOf(MapDefinition map);
    public bool IsReady(MapDefinition map);
    public bool IsFailed(MapDefinition map);
    public PreparedMap Get(MapDefinition map);        // InvalidOperationException if not ready
    public int Encodings { get; }                     // how many maps were encoded (tests: once per map)
}
```

- 服务器启动时 `Prepare(options.Map)`；`SetMap` 时先 `Prepare(新图)` 再换房间的图。`TryStartMatch` 在地图生成中时返回 false，原因 `"the map is still being generated"`；生成失败时原因 `"the map failed to generate"`。每个 `Tick` 看一下：房间的图的状态变了就 `_roomDirty = true`（房间状态里 `MapState` 跟着变）。开局用 `map.Definition.NewBattlefield()`（准备时已建好，`Fresh()` 约 0.1 ms），不在 tick 线程上从数据重建。
- 准备失败（生成抛异常，前台后台一样）：记日志 `map <id> failed: <异常>`，这张图是 `Failed`，房间状态如实告诉客户端；属于程序错误，不做重试。
- 重同步：同一连接已收到本局地图的座位，`RequestMatchState` 的回答不带地图（几个字节，不是约 542 KB）；新加入、中途加入、换连接重连照旧带图；客户端收到不带图的开局而自己没有这局的场地时当协议错误丢掉，带 `NeedMap` 再请求，直到收到带图的开局。

### 客户端（任务 15～24）

```csharp
// Bootstrap/LaunchOptions.cs
public string? Map { get; init; }   // --map: the MapCatalog id (canonical lower case); null when not given or unknown (with a warning)

// Online/Logic/HostSettings.cs: one optional trailing parameter
public sealed record HostSettings(string PlayerName, string RoomName, int Port, BattleMode Mode, int Bots, uint? Seed, double? MatchSeconds,
    string Map = SandboxMap.Id);

// Sandbox/Logic/MapSites.cs (engine-free; namespace Tank.Client.Sandbox)
public sealed record CameraAnchor(Vector2 Eye, float EyeHeight, Vector2 Focus, float FocusHeight);   // heights above the ground (or water) there
public sealed record MapSite(MapDefinition Map, string Title, Vector2 Spawn, float SpawnYaw, IReadOnlyList<TargetSpot> Targets,
    bool HasPatrol, IReadOnlyDictionary<string, CameraAnchor> Anchors, IReadOnlyDictionary<string, Vector2> PresetSpawns,
    Vector2 SpinEye, Vector2 MapCentre, float MapSize,
    IReadOnlyList<string> PresetNames, IReadOnlyList<string> DemoNames, Func<string?, RangeDemo?> FindDemo);
// PresetSpawns: where a preset puts the player (sandbox: bush, patrol; riverland: ford); any other preset uses Spawn
public static class MapSites
{
    public static MapSite Sandbox { get; }
    public static MapSite Riverland { get; }
    public static MapSite For(string? mapId);   // null, empty or unknown → Sandbox
    public static Func<float, float, float> SurfaceHeight(Battlefield field);   // max(ground, water): what fixed cameras stand over (test drive and match)
}

// UI/Logic/MapText.cs (engine-free; namespace Tank.Client.UI)
public static class MapText
{
    public const string AppTitle = "坦克 3D";
    public static string Name(string? mapId);           // the catalog's Name; an unknown id as it is; null or "" → "—"
    public static string SandboxTitle(string mapId);    // "坦克 3D · 试驾场" for sandbox, "坦克 3D · 试驾场 · 河谷" for riverland
    public static string MatchTitle(string? mapId);     // "坦克 3D · 河谷", "坦克 3D · 试驾场地图"; unknown map → "坦克 3D · 对局"
}

// Sandbox/RangeDemo.cs: two more optional parameters at the end
public sealed record RangeDemo(..., bool PatrolDrives = false, IReadOnlyList<TargetSpot>? Spots = null, SN.Vector3? AimAt = null)
{
    public TargetSpot Spot => (Spots ?? SandboxMap.TargetSpots)[Target];
    // AimAt set: the gun is laid on that world point (Command), and Bearing faces it; else as now (the target's armour face)
}

// Cameras/CameraPresets.cs: optional trailing parameters and new helpers only
public static SN.Vector2 Spawn(string? name, MapSite? site = null);              // null site = MapSites.Sandbox
public static (Vector3 Eye, Vector3 Target) Get(string name, Vector3 tank, float tankYaw, double time,
    Func<float, float, float> groundAt, int targetIndex, MapSite? site = null);
public static bool IsKnownFor(string? name, MapSite site);                      // site.PresetNames.Contains(name)
public static IReadOnlyList<string> Names { get; }                              // every site's presets (IsKnown stays "known on some map")
public static bool StreamsAroundCamera(string? name);                           // + overview, town, ford, forest, plateau, dunes, map
public static bool IsOrthographic(string? name);                                // "map"
public static Vector3 UpFor(string? name);                                      // "map": Vector3.Forward (north up on screen); else Vector3.Up
public static bool Look(Camera3D camera, string preset, Vector3 eye, Vector3 target, MapSite site, BattleWorld world, bool mapViewOn);
// looks along Get's (eye, target) with UpFor; "map" the first time: orthographic, Size = MapSize + 40, world.UseMapView(); returns mapViewOn (test drive and match)

// Cameras/OrbitCamera.cs
public void Follow(Vector3 pivot, Vector3 sniperEye, HeightField terrain, float? waterLevel = null);   // floor = max(ground, water) + clearance

// World/BattleWorld.cs — the hook points (riverland design §6, stage 5 design §2)
// Build: dirt = DirtMask.FromField(field) ?? new DirtMask(…style…); terrain.Build(field.Terrain, dirt, field.WaterLevel);
//        then WaterSurface, ForestViews, HouseViews next to the walls; grass and tree line as now.
// Update (both branches, the free-camera one too): SyncMapLayers(cameraPos, hull or null, delta) after _walls.Sync.
//        Task 23: the bushes' and walls' SyncStanding run only when the _field instance or its StateVersion changed; _walls.Fall every frame.
// OnEvent: _walls, _forest and _houses each get the event.
public void UseMapView();   // the "map" preset: fog off, no distance fade on trees (tree line and forest)

// World/WallViews.cs (task 23: Sync(field, delta) split in two)
public void SyncStanding(Battlefield field);   // looks at every wall and fence: rubble, a fence starting to topple, a restored prop standing up
public void Fall(float delta);                 // moves the toppling fences on, every frame

// World/DirtMask.cs
public static DirtMask? FromField(Battlefield field);   // layered (GroundLayers) when the field has water or a non-uniform surface; else null
public bool IsLayered { get; }
// World/TerrainMesh.cs
public void Build(HeightField field, DirtMask dirt, float? waterLevel = null);   // a layered mask: dirt_mask is its RGBA texture, layered = true, water_level set

// World/WaterSurface.cs
public partial class WaterSurface : Node3D
{
    public const string Group = "water_surface";
    public void Build(Battlefield field);          // joins Group always; builds meshes only with water
    public float? Level { get; }
    public float DepthAt(float x, float z);        // max(0, Level − terrain height), 0 without water
    public static WaterSurface? Find(Node anyNodeInTree);   // GetTree().GetFirstNodeInGroup(Group); callers cache it
}

// World/ForestViews.cs, World/HouseViews.cs
public void Build(Battlefield field);
public void Sync(Battlefield field, Vector3 cameraPos, Obb2? ownHull, float delta);   // field passed every call (a resync swaps it), never cached
public void OnEvent(in GameEvent e);               // remembers which way a felled tree / collapsing house was pushed
public void SetFarView(bool far);                  // ForestViews only: drop the distance fade for the map preset

// Overlay/StatusPanel.cs
public void SetGround(string? text);               // the line over the vehicle name; null hides it; redraws only on change

// World/TreeLine.cs
public void SetFarView(bool far);                  // drop the distance fade (the map preset)

// Signatures that change at their only call sites (SandboxMain, MatchMain):
public void TargetRange.Build(Simulation sim, VisionTracker vision, bool parkPatrol, MapSite site);   // no patrol when !site.HasPatrol (PatrolIndex −1)
public SandboxAutomation(LaunchOptions options, Node host, bool standStill, MapSite site);
public MatchAutomation(LaunchOptions options, bool screenshotTaken, MapSite? site = null);           // HasPreset = IsKnownFor(preset, site ?? MapSites.Sandbox)
```

`BattleWorld` 里的挂点（替代旧计划给 `SandboxWorld` 的那些行号；`SandboxWorld` 已不存在，4B 把它变成了 `World/BattleWorld.cs`）：

| 位置（现有代码） | 加什么 | 任务 |
|---|---|---|
| `Build` 第 43～44 行（`new DirtMask(...)`、`terrain.Build(...)`） | `DirtMask.FromField(field) ?? new DirtMask(...)`；`terrain.Build(field.Terrain, dirt, field.WaterLevel)` | 17 |
| `Build` 第 51～53 行（`WallViews`）之后 | 建 `WaterSurface`、`ForestViews`、`HouseViews` | 18、21、22 |
| `Update` 第 77 行和第 89 行（两处 `_walls.Sync(...)`）之后 | `SyncMapLayers(cameraPos, hull, delta)`：自由镜头分支 hull 为 null | 21、22 |
| `OnEvent` 第 93 行 | 也交给 `_forest`、`_houses` | 21、22 |
| `Update` 两处 `_bushes.SyncStanding`、`_walls.Sync` | 只在 `_field.StateVersion` 或 `_field` 实例变了时做全量同步 | 23 |

---

## 任务总览

| 批 | 任务 | 内容 | 依赖 |
|---|---|---|---|
| A 仿真基础 | 1 | 试驾场指纹；物件空间索引（现有种类）；逐 tick 等价测试 | — |
| | 2 | 地面类型、水、速度系数；`MapData` 带地面和水；`StateVersion`、`Fresh` | 1 |
| | 3 | 树：种类、撞倒、炮弹、溅射、树冠遮挡 | 1、2 |
| | 4 | 房子：种类、伤害、撞塌、废墟、`HouseRamSpeed`；`WallViews` 只画墙和栅栏 | 3 |
| B 河谷生成 | 5 | 生成工具：噪声、折线和距离场、塑形、摆放网格、泊松圆盘、可通行和连通 | 2 |
| | 6 | 河谷地形、河、湖、浅滩、公路和土路、地面类型 | 5 |
| | 7 | 村镇：房子、院墙、栅栏、地块压平 | 4、6 |
| | 8 | 岩石、草丛、树；物件编号顺序；生成耗时 | 3、7 |
| | 9 | 出生点、路点、试驾场站位、地标；`MapDefinition`、`MapCatalog`；河谷指纹、等价、分配、耗时 | 8 |
| C 联网 | 10 | 不可靠包上限 1000 | — |
| | 11 | 协议 v3：地图追加段、上限、种类上限、预编码写法 | 2、4 |
| | 12 | 服务器选图、准备（生成、编码各一次、后台）、`Tank.Server --map` | 9、11 |
| | 13 | 房间选图：房间状态、房间信息、`SetMap`、地图生成中不能开局 | 12 |
| | 14 | 河谷对局测试：开局消息 < 1 MiB、三种模式、小地图零修正和物件一致 | 13 |
| D 客户端地面和水 | 15 | `--map`、站点、试驾场接河谷（标题、出生点、靶车、机位、`map` 俯视） | 9 |
| | 16 | 房间界面选图、局域网列表的地图列、对局窗口标题、开房默认河谷 | 13、15 |
| | 17 | 地面着色（沙、路、河床、湿岸）、长草规则、图外树带；对局世界用场地的地面 | 15 |
| | 18 | 水面、镜头不入水、自由镜头焦点高度 | 17 |
| | 19 | 涉水水花、落水水柱 | 18 |
| | 20 | 地面提示（`CombatHud`） | 15 |
| E 客户端物件 | 21 | 林中树、倒下动画、半透明、倒树特效 | 17 |
| | 22 | 房子、倒塌、废墟、特效 | 17 |
| F 收尾 | 23 | 性能：状态版本门控、岩石合并、碎石和草丛分扇区、远处地形（按需） | 21、22 |
| | 24 | 河谷演示（`house`、`ram-house`、`trees`、`ford`）和无画面测试 | 22 |
| | 25 | 验收截图、帧率、两实例河谷对局、局域网列表 | 23、24 |
| | 26 | 文档：`docs/01～04`、README | 25 |

第一次能看到完整的河谷（树和房子都画出来）是 E 批之后；D 批时树和房子是看不见的障碍，截图只看地面和水。

---

## 批次 A：仿真基础

### Task 1：试驾场指纹、物件空间索引、逐 tick 等价

**Files:**
- Create: `src/Tank.Sim/Maps/PropIndex.cs`
- Modify: `src/Tank.Sim/Maps/Battlefield.cs`（第 23～31 行的索引数组、第 54～58 行建组、第 98～115 行 `Raycast`、第 119～133 行 `BushesNear`、第 138～148 行的内部 span）、`src/Tank.Sim/Vehicles/TankMotion.cs`（`KnockDown` 第 135～160 行、`OnRubble` 第 169～174 行、`PenetrationDepth` 第 229～237 行、`Resolve` 第 239～250 行）、`src/Tank.Sim/Simulation.Props.cs`（`KnockThrough` 第 67～83 行、`SplashProps` 第 100～117 行）、`src/Tank.Sim/Vision/Spotting.cs`（`BushesHiding` 第 74～93 行）
- Test: `tests/Tank.Sim.Tests/SandboxFingerprintTests.cs`、`PropIndexTests.cs`、`PropIndexEquivalenceTests.cs`

**接口**：共享接口“空间索引”。本任务 `Foliage` 组只有草丛（登记半径 = `HalfExtents.X`）。

**规则/要点**

- 河谷设计 §4.1。结果必须和全表扫描**逐位相同**：候选按下标升序、去重，调用方对候选做的精确判断和原来一样，所以遍历顺序（事件顺序、推出顺序、最近命中的平局规则）不变。
- 各调用处换成的查询：

| 调用处 | 组 | 查询 |
|---|---|---|
| `Battlefield.Raycast` | `ShellStoppers` | `Along(from.xz, to.xz, 0)`，缓冲 1024 |
| `Battlefield.BushesNear`（两个重载） | `Foliage` | `Near(point, radius)`，再只留 `Kind == Bush` |
| `TankMotion.KnockDown` | `Blockers` | `Near(hull.Center, hull.HalfExtents.Length())`（与 `Overlaps` 的外接圆预判一致） |
| `TankMotion.OnRubble` | `Walls` | `Near(hull.Center, hull.HalfExtents.Length())` |
| `TankMotion.PenetrationDepth` | `Blockers` | `Near(box.Center, box.HalfExtents.Length())` |
| `TankMotion.Resolve` | `Blockers` | 每轮迭代开始按当前车体中心查：`Near(box.Center, box.HalfExtents.Length() + ResolveSlack)`，`ResolveSlack = 4f`（一轮里多次推出的总位移不会超过它；等价测试兜底） |
| `Simulation.KnockThrough` | `Flimsy` | `Along(from.xz, to.xz, 0)`（原有的中点预判保留） |
| `Simulation.SplashProps` | `Walls`、`Flimsy` | `Near(at.xz, radius)` 各一次 |
| `Spotting.BushesHiding` | `Foliage` | `Near(ground, BushReach)`（原有的 `apart > BushReach + radius` 预判正好是这个圆） |

- `Along` 返回与“线段加宽 `margin + 0.01` 的胶囊”相交的格子里的物件（可以多，不能少）；零长度线段等于一个圆。
- 缓冲区不够装下去重前的候选时，返回 `All(set)`（结果仍正确，只是慢）。
- 不分配：查询只写调用方的缓冲区；`Simulation`、`TankMotion` 里用 `stackalloc`。
- `UseIndex = false` 只给测试；默认 true。

**先写的测试**（`SandboxFingerprintTests` 在改代码之前写、跑、通过，作为护栏）

- `SandboxFingerprintTests.The_sandbox_map_is_bit_for_bit_what_it_was`：对 `SandboxMap.Create()` 做 FNV-1a 64 位（初值 `14695981039346656037`，乘数 `1099511628211`，每个 32 位数按小端 4 个字节喂进去）：先按 `j` 外 `i` 内喂每个 `Terrain.Sample(i, j)` 的 `BitConverter.SingleToUInt32Bits`，再按顺序喂每个物件的 `Id`、`(uint)Kind`、`Center.X`、`Center.Y`、`HalfExtents.X`、`HalfExtents.Y`、`Yaw`、`Bottom`、`Height`（浮点取位）。期望 `0x13FE9A2B55551ED0`；物件 351 个：岩石 88、草丛 220、砖墙和石墙 34、栅栏 9（本计划写作时在 `80d6eca` 上实测，Debug、Release 相同）。
- `PropIndexTests`（合成场地：300 m × 300 m 平地，`XorShift(7)` 摆 150 块岩石（半宽 1～5 m、任意朝向）、250 丛草（半径 2～4.6 m）、150 段砖墙石墙、50 段栅栏）：
  1. `Near_returns_ascending_distinct_indices_covering_everything_in_reach`：2000 个随机圆（半径 0～30 m，圆心可在图外 50 m 内），每组：结果严格升序；⊇ `{i ∈ All(set) | 距离(圆心_i, c) ≤ r + 登记半径_i}`；⊆ `All(set)`。
  2. `Along_covers_every_prop_whose_circle_reaches_the_widened_segment`：2000 条随机线段（长 0～400 m，含零长度、正好落在 x = 8k 格线上的水平和竖直线段），margin 0～3 m，同样三条断言（用点到线段距离）。
  3. `A_buffer_too_small_falls_back_to_the_whole_set`：长度 1 的缓冲、至少 2 个候选 → 返回的序列等于 `All(set)`。
  4. `With_the_index_off_queries_return_the_whole_set`。
  5. `Each_set_holds_the_props_its_kind_says`：五组的 `All` 分别等于 `IsSolid`、`StopsShells`、`IsFlimsy`、`IsWall`、`Kind == Bush` 的下标。
  6. `Raycast_agrees_with_and_without_the_index_on_the_sandbox`：`XorShift(11)` 生成 5000 条线段（端点高 0～20 m、长 0～300 m），两种开关下 `(bool, BitConverter.SingleToUInt32Bits(T), PropId)` 相同。
  7. `Queries_allocate_nothing`：预热后 1000 次 `Near` + `Along` + `Raycast`，`GC.GetAllocatedBytesForCurrentThread()` 差 0。
- `PropIndexEquivalenceTests`（共用一个跑两遍、逐 tick 比较的帮助方法 `RunTwice(Func<Battlefield> field, …)`：一遍 `UseIndex = true`，一遍 false；每 tick 比较全部 `TankState`（record 相等）、`Events` 序列、`ShellCount`、每个物件的耐久，以及 `VisionTracker.IsVisible` 的所有车对）：
  1. `Sandbox_battle_is_identical_with_and_without_the_index`：`SandboxMap.Create()`，8 辆车（四种各两辆，放在 `SandboxMap.Spawns.Solo[0..7]`，队 0～7），输入由 `XorShift(5)` 脚本给：每 45 tick 换一次油门和转向，瞄准偏航匀速扫，装填好就开火，每 3 发换一次弹种；1800 tick。
  2. `Dense_field_battle_is_identical_with_and_without_the_index`：300 m 平地，1500 丛草、400 块岩石、40 条砖墙石墙（6～20 m）、20 条栅栏；同样的脚本 1200 tick。

**命令**

```bash
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~SandboxFingerprint" 2>&1 | grep -E '通过!|失败!'   # 改代码前就该通过
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~PropIndex" 2>&1 | grep -E '通过!|失败!'           # 先失败（类型不存在），实现后通过
```

**验收**

- [ ] 第一步：拍基线 `$S/baseline4b.sh $W build/shots/base`，测试驾场 `spin`、`orbit` 帧率存到 `build/shots/base/spin.log`、`orbit.log`（见“执行前须知”）。
- [ ] 全部测试通过，五个项目 0 警告；现有测试一条没改。
- [ ] 六张回归截图（`build/shots/5a-t01-sandbox/`）与基线一致。
- [ ] 测试数：Sim 639 → 至少 649。

**步骤**

- [ ] 拍基线截图和试驾场帧率（见“验收”第一条）
- [ ] 写 `SandboxFingerprintTests`，在没改代码时跑通过
- [ ] 写其余“先写的测试”，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完，截图逐张看过
- [ ] 提交

**提交**：`feat(sim): uniform-grid prop index behind every prop query — results identical to the full scan, sandbox fingerprint pinned`，末行 `Tests: Sim 639 → 649.`

### Task 2：地面类型、水、速度系数

**Files:**
- Create: `src/Tank.Sim/Maps/SurfaceKind.cs`、`src/Tank.Sim/Maps/SurfaceMap.cs`、`src/Tank.Sim/Maps/GroundSpeed.cs`
- Modify: `src/Tank.Sim/Maps/Battlefield.cs`（构造、`Surface`、`WaterLevel`、`SurfaceAt`、`WaterDepthAt`、`SpeedFactorAt`、`StateVersion`、`Fresh`；`Damage`/`Destroy`/`Restore`/`SetDurability` 记版本）、`src/Tank.Sim/Maps/MapData.cs`、`src/Tank.Sim/Vehicles/TankMotion.cs`（`TargetSpeed` 第 183～194 行）
- Test: `tests/Tank.Sim.Tests/GroundSpeedTests.cs`、`SurfaceMapTests.cs`、`BattlefieldGroundTests.cs`、`GroundDrivingTests.cs`、`MapDataSurfaceTests.cs`

**接口**：共享接口“地面和水”。

**规则/要点**

- 河谷设计 §2.1～2.3、§4.2、§4.5。系数按车体中心（`t.Position`）取；`TargetSpeed` 里 `target *= field.SpeedFactorAt(t.Position)` 放在瓦砾系数之前（乘法顺序不影响试驾场：系数是 1f，结果逐位不变）。
- 水深用 `Terrain.HeightAt`（双线性），服务器和客户端的 `Battlefield` 都从同一份 `MapData` 建，所以预测不会因地面产生偏差。
- `StateVersion` 只在状态真的变了时加一：`Damage` 掉了血（不管倒没倒）、`Destroy` 把立着的放倒、`Restore` 改了耐久或立着与否、`SetDurability` 改了值。查询不改它。
- `Fresh()` 共享 `HeightField`、`SurfaceMap` 和物件数组（都是不可变的），重建索引和状态。
- `MapData.Of` 的水位：`(int)MathF.Round(level / HeightUnit)`；`Build` 用 `WaterLevelCm * HeightUnit`。

**先写的测试**

- `GroundSpeedTests`：
  1. `Ground_factors_are_the_designs`：`Of(Grass) == 1`、`Of(Dirt) == 1`、`Of(Sand) == 0.8f`、`Of(Road) == 1.15f`。
  2. `Water_factor_is_one_minus_a_third_of_the_depth_clamped`（Theory）：0.01 → 0.8、0.6 → 0.8、0.9 → 0.7、1.2 → 0.6、1.3 → 0.5667（±1e-4）、1.5 → 0.5、1.6 → 0.5、3 → 0.5。
  3. `Water_replaces_the_ground_factor`：`Factor(Road, 0.5f) == 0.8f`、`Factor(Sand, 1.2f) == 0.6f`、`Factor(Road, 0) == 1.15f`、`Factor(Sand, 0) == 0.8f`。
  4. `Top_speeds_match_the_design_table`（Theory，四种车）：`MaxForwardSpeed × 3.6 × 系数` 等于河谷设计 §2.3 的表（±0.1 km/h）：轻坦 69 / 60 / 48 / 34 / 30，中坦 57.5 / 50 / 40 / 28.3 / 25，重坦 40.3 / 35 / 28 / 19.8 / 17.5，TD 46 / 40 / 32 / 22.7 / 20（公路 / 草地 / 沙地 / 1.3 m / 1.6 m）。
- `SurfaceMapTests`：
  1. `Construction_checks_sizes_kinds_and_cell_size`：长度不符、`(SurfaceKind)4`、`cellSize` 为 0 或 NaN、`cellsX` 为 0 → `ArgumentException`。
  2. `The_cells_are_copied`：构造后改源数组，地面图不变。
  3. `A_point_falls_in_the_cell_it_lies_in`：4 × 3、格宽 2：`At(3.99f, 0.5f)` 是格 (1, 0)，`At(4f, 0.5f)` 是格 (2, 0)，`At(1f, 2f)` 是格 (0, 1)；`this[i, j] == Cells[j * 4 + i]`。
  4. `Points_off_the_grid_clamp_to_the_edge_cells`：`At(-5, -5)` 是 (0, 0)，`At(1e6f, 1e6f)` 和 `At(8f, 6f)` 是 (3, 2)。
  5. `Uniform_is_uniform`：`Uniform(…, Sand).IsUniform(Sand)` 真、`IsUniform(Grass)` 假；不给种类时是草地。
- `BattlefieldGroundTests`：
  1. `The_three_argument_constructor_gives_grass_and_no_water`：`TestWorld.FlatField()` 的 `Surface` 是 100 × 100、格宽 2、全草地；`WaterLevel` 为 null；任意点水深 0、系数 1。
  2. `A_mismatched_surface_or_a_bad_water_level_is_refused`：99 × 100、格宽 1 的地面图，`waterLevel` 为 NaN 或无穷 → `ArgumentException`。
  3. `Water_depth_is_the_level_above_the_ground`：地面 −1.3、水位 0 → 1.3（±1e-5）；地面 +0.5 → 0；没有水 → 0。
  4. `Speed_factor_combines_ground_and_water`：干的公路 1.15；水深 1.2 的公路 0.6；干沙 0.8。
  5. `State_version_counts_real_changes_only`：一段砖墙加一块栅栏：初值 0；砖墙 `Damage` 100 → 1；`Damage` 0 → 仍 1；栅栏 `Destroy` → 2，再 `Destroy` → 仍 2；砖墙 `Restore` → 3，再 `Restore` → 仍 3；`SetDurability(墙, 300)`（已是 300）→ 仍 3，`SetDurability(墙, 0)` → 4；`IsStanding`、`Raycast` 不改它。
  6. `Fresh_gives_the_same_ground_and_props_all_standing`：放倒一段墙后 `Fresh()`：`Terrain` 和 `Surface` 是同一个实例，`Props` 相等，`WaterLevel` 相同，那段墙立着、满血，`StateVersion` 0。
- `GroundDrivingTests`（200 m 平地，整张图一种地面；中坦从静止全油门开 15 秒，`TestWorld.Drive`）：
  1. `Top_speed_on_each_ground`（Theory）：公路 57.5、沙地 40、草地 50、土地 50 km/h（±0.05）。
  2. `In_water_the_depth_sets_the_top_speed`：地面 −1.2、水位 0 → 30 km/h；−0.3 → 40；−1.6 → 25。
  3. `Reverse_is_scaled_too`：公路上倒车最高 18 × 1.15 = 20.7 km/h。
  4. `Ground_and_rubble_multiply`：沙地上压着一段砖墙的瓦砾 → 50 × 0.8 × 0.7 = 28 km/h。
  5. `Acceleration_is_unchanged`：从静止全油门 10 tick，公路和草地上的 `Speed` 逐位相同。
  6. `The_hull_centre_decides`：x < 100 是公路、其余是沙地；车头朝西从 x = 110 开过去：车头先进公路时上限仍是沙地的，车体中心过了 x = 100 后上限变成公路的。
  7. `Prediction_and_simulation_agree_on_road_and_water`：一张带公路、沙地和 1 m 深水沟的 200 m 图，一份原样、一份经 `MapData.Of(…).Build()` 往返；中坦按固定脚本开 300 tick：`TankStep.Advance(knockDown: false)` 在往返图上 vs `Simulation.Step` 在往返图上，位置逐位相同。
- `MapDataSurfaceTests`：
  1. `Of_and_Build_carry_the_surface_and_the_water_level`：混合地面、水位 −0.37 → `Surface` 相同、`WaterLevelCm == -37`；`Build()` 后每个格子中心的 `SurfaceAt` 相同、`WaterLevel == -0.37f`。
  2. `The_six_argument_constructor_means_all_grass_and_no_water`：3 × 3 采样点 → `Surface` 长 4、全草地，`WaterLevelCm` 为 null。
  3. `A_bad_surface_or_water_level_is_refused`：长度 3、种类 9、水位 1 000 001 → `ArgumentException`。
  4. `The_data_of_a_built_field_is_the_same_data`：`Of(Build(Of(f)))` 的高度、地面、水位与 `Of(f)` 相同。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Ground|FullyQualifiedName~SurfaceMap|FullyQualifiedName~MapDataSurface" 2>&1 | grep -E '通过!|失败!'`

**验收**

- [ ] 全部测试通过、0 警告；`SandboxFingerprintTests` 和全部现有测试不改照过。
- [ ] 测试数：Sim 至少 +26（约 675）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): ground kinds, shallow water and their top-speed factors — battlefield and map data carry them`，末行 `Tests: Sim 649 → 675.`

### Task 3：树

**Files:**
- Modify: `src/Tank.Sim/Maps/Prop.cs`（`Tree = 6`、`LastKind`、`CrownPerTrunk`、`IsRound`、`IsFoliage`、`FoliageRadius`、`MaxDurability`、`DistanceTo`）、`src/Tank.Sim/Maps/Battlefield.cs`（圆形检查改为 `IsRound`）、`src/Tank.Sim/Maps/PropIndex.cs`（`Foliage` 按 `FoliageRadius` 登记）、`src/Tank.Sim/Vehicles/TankMotion.cs`、`src/Tank.Sim/Simulation.Props.cs`、`src/Tank.Sim/Vision/Spotting.cs`
- Test: `tests/Tank.Sim.Tests/TreeTests.cs`、`TreeSpottingTests.cs`；`PropIndexEquivalenceTests.cs` 加一条

**接口**：共享接口“物件”（本任务只加 `Tree = 6`，`House = 5` 在任务 4 加；`LastKind = Tree`）。

**规则/要点**（河谷设计 §2.4、§4.3）

- 坦克：`KnockDown` 里树总是让路：`givesWay = Fence || Tree || BrickWall && (…)`；树的“碰到”按树干圆算：`hull.IntersectsCircle(center, HalfExtents.X)`（不用方框，否则方框角会撞倒碰不到的树）。撞倒照常进 `felled`、报 `PropDestroyed`（推的方向是车头方向，和栅栏一样）。树不让坦克减速。
- 树**从不推车**：`Resolve`、`PenetrationDepth` 跳过 `Kind == Tree`（“坦克不受阻挡”在圆形树干上才严格成立；客户端预测 `knockDown: false` 时树不进 `ignored`，同样不挡）。
- 炮弹：`KnockThrough` 的圆柱分支从 `Kind == Bush` 改为 `IsRound`（树按树干圆柱）；树冠不参与。高爆溅射用 `DistanceTo`（圆形物件按圆柱），爆点到树干圆柱的距离小于溅射半径就倒。炮口在树干里时那棵不倒（原有的 `t > 0` 规则）。
- 隐蔽：`BushesHiding` 遍历 `Foliage`：半径用 `FoliageRadius`，圆柱从 `Bottom` 到 `Top`；“在树下”和“在草丛里”同一规则（`InsideBush × 半径`）；树和草丛合计最多 `MaxBushes`。`Sighting.Bushes` 的含义变为“枝叶”数（名字不改，调试文字照旧写“草×N”）。
- 视线、`Raycast`：树不挡（`StopsShells` 为假）。
- 恢复：`RestoreProps` 原规则（`IsSolid` 且车压在占地上就跳过）对树照样生效。

**先写的测试**

- `TreeTests`（平地，树干半径 0.5、高 12，在 (100, 80)）：
  1. `A_tree_is_solid_flimsy_round_foliage`：`IsSolid`、`IsFlimsy`、`IsRound`、`IsFoliage` 真；`StopsShells`、`IsWall` 假；`MaxDurability == 1`；`FoliageRadius == 3.5f`；`Prop.LastKind == PropKind.Tree`；旧的五个值不变（Rock 0 … Fence 4）。
  2. `A_tree_must_have_a_round_footprint`：半宽 (0.5, 0.4) → `ArgumentException`。
  3. `Any_tank_knocks_a_tree_down_without_being_slowed`（Theory，四种车，起速 10 km/h，全油门向北）：树倒了；有一条 `PropDestroyed`，`TankId` 是这辆车、`ShellId` 0、`Velocity` ≈ (0, 0, −1)；每 tick 的位置和速度与同样起步、图上没有树的对照逐位相同。
  4. `Passing_beside_the_trunk_leaves_it_standing`：车体侧面离树干中心 0.55 m 开过（只进了树冠）→ 树立着；0.45 m → 倒。
  5. `Prediction_drives_through_trees_and_leaves_them_standing`：`TankMotion.Drive(knockDown: false)` → 树立着，轨迹和 `knockDown: true` 那遍逐位相同。
  6. `A_shell_through_the_trunk_fells_it_and_flies_on`：中坦在树南 50 m、穿甲弹瞄着树干后 20 m 的石墙：事件依次是树的 `PropDestroyed`（同一个 `ShellId`，位置在树干表面）、石墙的 `PropDamaged`。
  7. `A_shell_through_the_crown_only_leaves_it_standing`：瞄着离树干 1.5 m、树冠高度的点 → 树立着。
  8. `An_HE_burst_fells_the_trees_in_its_radius`：高爆打在树脚 1 m 外的地面 → 倒；打在 10 m 外 → 立着。
  9. `A_gun_fired_from_inside_a_trunk_leaves_that_tree_standing`：树干中心放在这辆车 `Ballistics.Aim` 的炮口上，开炮 → 这棵没有 `PropDestroyed`。
  10. `Restore_skips_a_tree_with_a_tank_on_its_trunk`：两棵倒树，一辆车停在其中一棵的树干上 → `RestoreProps` 后那棵仍倒、另一棵立着。
  11. `Rays_and_sight_lines_pass_through_trees`：穿过树干的 `Raycast` 不命中物件；`Spotting.Look` 的 `LineClear` 为真。
  12. `Distance_to_a_tree_is_to_its_trunk_cylinder`：`DistanceTo((103, 1, 100))` = 2.5；`DistanceTo((100, 20, 100))` = 20 − `Top`。
- `TreeSpottingTests`（观察者在 120 m 外的中坦）：
  1. `A_crown_near_the_target_hides_like_a_bush`：树冠 3.5 m 的树在视线上、离目标 8 m → `Look(...).Bushes == 1`，`Camo` 比没有树时多 0.25。
  2. `Trees_and_bushes_count_together_up_to_two`：两棵树 + 一丛草都在视线上 → 2。
  3. `Under_a_tree_counts_within_three_quarters_of_the_crown`：目标车体中心离树干 2.5 m（0.75 × 3.5 = 2.625）→ 1；在 3.0 m、视线不经过树冠内圈 → 0。
  4. `A_felled_tree_hides_nothing`：放倒后 → 0。
  5. `A_crown_far_from_the_target_does_not_count`：树冠在离目标 30 m 处的视线上 → 0。
  6. `Sees_agrees_with_Look_among_trees`：`XorShift(3)` 在一片 200 棵树的小林子周围放 500 组观察者和目标：`Sees(...) == Look(...).Seen`。
- `PropIndexEquivalenceTests.Synthetic_forest_battle_is_identical_with_and_without_the_index`：300 m 平地，7 m 抖动网格上 2400 棵树（树干 0.35～0.6）、200 丛草、100 块岩石；8 辆车用`PropIndexEquivalenceTests` 里共用的 `RunTwice` 和脚本输入（`XorShift(5)`：每 45 tick 换一次油门和转向、瞄准偏航匀速扫、装填好就开火、每 3 发换一次弹种）开进林子 1200 tick。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Tree|FullyQualifiedName~PropIndexEquivalence" 2>&1 | grep -E '通过!|失败!'`

**验收**

- [ ] 全部测试通过、0 警告；试驾场指纹不变；六张回归截图与基线一致（`build/shots/5a-t03-sandbox/`）。
- [ ] 测试数：Sim 至少 +19（约 694）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): trees — felled by any touch or shell through the trunk, crowns hide like bushes`，末行 `Tests: Sim 675 → 694.`

### Task 4：房子

**Files:**
- Modify: `src/Tank.Sim/Maps/Prop.cs`（`House = 5`、`IsWall`、`MaxDurability`、`RubbleFootprint`）、`src/Tank.Sim/Vehicles/TankSpec.cs`（`HouseRamSpeed`）、`src/Tank.Sim/Vehicles/TankCatalog.cs`（四种车各加一行，挨着第 31、66、101、140 行的 `BrickWallRamSpeed`）、`src/Tank.Sim/Vehicles/TankMotion.cs`（`KnockDown`）、`src/Tank.Client/scripts/World/WallViews.cs`（第 55 行的过滤）
- Test: `tests/Tank.Sim.Tests/HouseTests.cs`、`HouseRamTests.cs`

**接口**：共享接口“物件”“`TankSpec`”。

**规则/要点**（河谷设计 §2.5、§4.3）

- 归类：`IsWall` 含房子，于是挡弹、直接命中伤害（穿甲 × 0.5、高爆全额）、溅射、炮管顶墙、瓦砾减速、恢复、`CombatHud` 的墙血条和伤害数字都自动覆盖房子。耐久 500。
- 废墟：`RubbleFootprint` 对房子四边各加 0.5 m；对墙仍只加厚度方向。
- 撞：`KnockDown` 里 `ramHouse = |t.Speed| > t.Spec.HouseRamSpeed`；`givesWay` 加 `House && (ramHouse || (!knockDown && inside))`，其中 `inside` 对砖墙和房子都算（车体已经在里面：客户端预测撞进去后速度减半到 20 以下也照样让路）。`broke |= (BrickWall || House) && !inside`，同一 tick 只减半一次（原逻辑）。
- 客户端 `WallViews.Build` 的过滤改为 `prop.Kind is PropKind.BrickWall or PropKind.StoneWall or PropKind.Fence`（否则房子会被当成墙段画，`WallLine.Thickness(House)` 会抛异常）。试驾场没有房子，画面不变。

**先写的测试**

- `HouseTests`（平地，12 × 7 m、高 7.5 m 的房子，中心 (100, 60)，正面朝南）：
  1. `A_house_is_masonry`：`IsWall`、`StopsShells`、`IsSolid` 真；`IsFlimsy`、`IsRound`、`IsFoliage` 假；`MaxDurability == 500`；`RubbleFootprint.HalfExtents == (6.5, 4.0)`；砖墙的仍是 `HalfExtents + (0, 0.5)`。
  2. `Shots_to_bring_a_house_down_at_average_damage`（Theory，8 行）：`ceil(500 / (Damage × (AP ? 0.5 : 1)))` 等于河谷设计 §2.5 的表：穿甲 轻 8、中 5、重 4、TD 3；高爆 3、2、2、2。
  3. `Medium_AP_brings_a_house_down_shot_by_shot`：中坦在南 40 m 连续开穿甲弹：每发一条 `PropDamaged`；倒塌发生在累计伤害第一次 ≥ 500 的那一发（`PropDestroyed` 同一 tick），发数在 4～7；每发炮弹都停在房子上（命中后下一 tick `ShellCount == 0`）；房后 10 m 的靶车没有 `TankHit`。
  4. `HE_splash_hurts_a_house_like_a_wall`：房前 2 m 放一块岩石，中坦用高爆打岩石：房子收到一条溅射的 `PropDamaged`（`Velocity` 为零），伤害等于 `WallDamage.Splash(中坦高爆, house.DistanceTo(爆点))`。
  5. `A_standing_house_stops_rays_and_sight_and_its_ruin_does_not`：穿过房子的 `Raycast` 命中它；房后 80 m 处的车 `Look(...).LineClear` 为假；`SetDurability(房子, 0)` 后都通。
  6. `Driving_on_a_ruin_is_slowed_like_rubble`：中坦在草地上的废墟里最高 35 km/h；车体只压到四边加宽的 0.5 m 时也减速。
  7. `A_barrel_pushed_into_a_house_strikes_its_face`：车停在房子边、炮管伸进墙里开穿甲弹 → 当 tick 一条 `PropDamaged`（`Velocity` 非零，位置在墙面上）。
  8. `Restore_skips_a_house_with_a_tank_on_it`：废墟上停着车 → `RestoreProps` 后仍是废墟。
  9. `Houses_are_in_the_masonry_blocker_and_stopper_sets`：`All(Walls)`、`All(Blockers)`、`All(ShellStoppers)` 都含它，`All(Flimsy)` 不含。
  10. `Shooting_houses_allocates_nothing_once_warmed_up`：预热后 300 tick 连续打房子，分配 0（照 `ShellPropTests` 的写法）。
- `HouseRamTests`（房子在车头前 10 m，车体正对屋墙）：
  1. `Ram_speeds_are_the_designs`：中坦、重坦 `HouseRamSpeed == 20f / 3.6f`（±1e-5），轻坦、TD 无穷大；`BrickWallRamSpeed` 不变（重坦 15，其余无穷大）。
  2. `At_21_kmh_medium_and_heavy_tanks_knock_a_house_down`（Theory：中坦、重坦）：起速设为 21 km/h、全油门 → 房子倒（`PropDestroyed`，`ShellId` 0，`TankId` 这辆车），撞的那一 tick 速度是撞前的一半（相对误差 1e-4），之后穿过废墟（×0.7）。
  3. `Light_tanks_and_tank_destroyers_are_stopped_at_any_speed`（Theory：轻坦 40 km/h、TD 35 km/h）：房子立着，车在 3 tick 内停住，车体不进房子占地。
  4. `At_19_kmh_a_house_stops_every_tank`（Theory，四种车）：起速 19 km/h、油门 0（滑行，撞上时低于 20）→ 房子立着，车停住。
  5. `Reversing_into_a_house_fast_enough_knocks_it_down`：重坦状态速度设为 −25 km/h、油门 −1，房子在车尾后 5 m → 倒。
  6. `A_brick_wall_and_a_house_in_one_tick_halve_the_speed_once`：重坦 30 km/h，下一 tick 的车体同时压上一段砖墙和房子 → 两样都倒，速度只减半一次。
  7. `Prediction_drives_into_the_house_and_on_without_a_second_halving`：`knockDown: false`：房子立着，撞入那一 tick 速度减半，之后车体在房子里继续开（不被推出、不再减半）；60 tick 的位置和速度与 `knockDown: true` 那遍逐位相同。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~House|FullyQualifiedName~Ram" 2>&1 | grep -E '通过!|失败!'`；客户端 `dotnet build src/Tank.Client/Tank.Client.csproj`。

**验收**

- [ ] 全部测试通过、0 警告；`RamTests` 不改照过；六张回归截图（`build/shots/5a-t04-sandbox/`，特别是 `wall`）与基线一致。
- [ ] 测试数：Sim 至少 +17（约 711）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): houses — masonry with 500 hit points, rammed down by medium and heavy tanks over 20 km/h, ruins slow like rubble`，末行 `Tests: Sim 694 → 711.`

**批次 A 收尾**：两路审查 → 一个跟进提交 → `docs/PROGRESS.md` 新建“5A 节点记录”表，记第 1 行（批次 A：四个任务的提交号、跟进提交号、Sim 639 → 约 711、基线截图和帧率所在目录）。

## 批次 B：河谷生成

整批的共同约定：

- 全部用 `XorShift`（每一步一个固定种子：`RiverlandMap.Seed` 加该步的常数偏移）和 `ValueNoise` 按固定顺序生成；不用 `System.Random`、不并行、不依赖字典遍历顺序（`ArchitectureTests` 会抓 `Random` 字段）。
- 摆放顺序（河谷设计 §3.12）：地形底子 → 台地、山、沙丘 → 河谷压低 → 挖河和湖 → 压平公路 → 房子地块（定房子位置并压平）→ 陆地最低高度和边界带 → 地面类型 → 岩石、草丛、树（避开路、水和房子地块）。物件编号按岩石、草丛、树、房子、院墙和栅栏的顺序最后统一分配。
- 布局常数集中在 `Maps/Riverland/RiverlandLayout.cs`（`internal static`），测试按它取点（`InternalsVisibleTo` 已有），不在测试里另写一份坐标。
- 测试共用 `tests/Tank.Sim.Tests/RiverlandFixture.cs`：`internal static class RiverlandFixture { public static Battlefield Field => …; }`，整个测试进程只调一次 `RiverlandMap.Create()`（`Lazy<Battlefield>`），只读使用；要改物件状态的测试用 `Field.Fresh()`。任务 9 之后改为 `MapCatalog.Riverland.NewBattlefield()`。
- 找候选点（出生点、路点、靶车、演示站位）用开发脚本目录里的一次性工具：`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe/`（新建的控制台项目，`ProjectReference` 到本工作目录的 `src/Tank.Sim/Tank.Sim.csproj`，打印候选和检查结果），不放进仓库；README 里补一行用法。

### Task 5：生成工具

**Files:**
- Create: `src/Tank.Sim/Maps/Building/ValueNoise.cs`、`DistanceGrid.cs`、`Polyline.cs`、`HeightSculpt.cs`、`PlacementGrid.cs`、`PoissonDisc.cs`、`Passability.cs`
- Test: `tests/Tank.Sim.Tests/MapBuildingTests.cs`

**接口**：共享接口“生成工具”。

**规则/要点**

- `Polyline`：向心 Catmull-Rom（alpha 0.5）穿过每个控制点，首尾用镜像的虚拟点；按弧长每 `step` 米重采样。`Stamp` 只处理每段线段外扩 `reach` 的方框里的采样点（代价和带宽面积成正比，河谷的几条路、河一起不超过几十毫秒）。
- `HeightSculpt.Flatten`：方框内设为 `height`，方框外 `blend` 米内按 smoothstep 过渡回原高度（按到方框的距离）。
- `PoissonDisc`：Bridson 算法，背景格宽 `minDistance` 的下界 / √2；`minDistance` 按位置变（林缘变稀用）；`accept` 拒绝的点不当活跃点。
- `Passability`：格子即地形格（2 m）；格子中心的坡度用 `HeightField.NormalAt`；被占的格子：格子中心落在任一岩石、房子、砖墙、石墙占地（各边加宽 `clearance`）里；连通是 4 邻接洪泛，`ComponentAt` 返回连通块编号。构造时一次算完，之后只读。

**先写的测试**（`MapBuildingTests`）

1. `A_straight_polyline_measures_and_finds_points`：控制点 (0,0)、(100,0)，step 2：`Length` 100（±0.01），`PointAt(50)` = (50, 0)，`DistanceTo((50, 10), out along)` = 10、`along` = 50。
2. `A_curved_polyline_passes_through_its_control_points`：(0,0)、(50,30)、(100,0)、(150,40)：每个控制点到折线的距离 < 0.01，`TangentAt` 是单位向量。
3. `Stamping_writes_the_distance_within_reach_only`：100 × 100 m 网格、格宽 2，从 (0,50) 到 (100,50) 的线 stamp `reach` 10：采样点 (20, 56) 的距离 6（±0.01）、`Along` 20；(20, 70) 仍是 +∞、`Line` −1。
4. `Two_lines_keep_the_nearer`：再 stamp 一条 (0,60)→(100,60)：(20, 56) 距离 4、`Line` 是第二条的编号。
5. `Value_noise_is_seeded_and_bounded`：同种子同点同值，不同种子不同；10 000 个点都在 [0, 1]；`Signed` 在 [−1, 1]。
6. `Bump_is_one_at_the_centre_and_zero_from_the_radius`：`Bump(0, 10)` = 1，`Bump(5, 10)` = (1 − 0.25)² = 0.5625，`Bump(10, 10)` = `Bump(12, 10)` = 0。
7. `Flatten_levels_the_box_and_eases_back`：斜坡上压平一个 10 × 6 m 方框到 5 m、过渡 3 m：方框内的采样点都是 5；离方框 3 m 以外不变；过渡带单调。
8. `Placement_grid_checks_the_gap`：加 (10,10) 半径 2：`AnyWithin((14.5,10), 2, gap: 0.6)` 真，`gap: 0.4` 假；跨格子边界的圆也找得到（(15.9, 10) 半径 0.1 与加在 (16.1, 10) 的圆）。
9. `Poisson_samples_keep_their_distance`：200 × 200 m、最小间距 6：两两间距 ≥ 6；数量 > 600；同一 `XorShift` 种子结果相同；`accept` 拒绝的区域（x < 50）里没有点。
10. `Poisson_spacing_may_vary_with_position`：`minDistance = x < 100 ? 6 : 12`：两半各自满足自己的间距。
11. `Passability_marks_slopes_props_and_the_border`：`TestWorld` 平地：可玩区里的格子都可通行，边界带里的不可；40° 的坡上不可；一块岩石盖住的格子（加宽 1.6 m）不可；树、水、栅栏、草丛的格子可通行。
12. `A_walled_square_is_cut_off`：一圈石墙（`WallLine`）围成 20 × 20 m 的院子：院内一点和院外一点 `Connected` 为假，`ComponentAt` 不同；院墙上开一个 6 m 的口子（栅栏）后为真。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~MapBuilding" 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告；测试数 Sim 至少 +12（约 723）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): map building tools — value noise, polylines and distance grids, sculpting, placement grid, Poisson disc, passability`，末行 `Tests: Sim 711 → 723.`

### Task 6：河谷的地形、河、湖、浅滩、公路和地面

**Files:**
- Create: `src/Tank.Sim/Maps/RiverlandMap.cs`（这一步 `Create()` 只有地形、地面和水，没有物件；`Spawns` 等在任务 9）、`src/Tank.Sim/Maps/Riverland/RiverlandLayout.cs`、`RiverlandTerrain.cs`、`RiverlandSurface.cs`
- Test: `tests/Tank.Sim.Tests/RiverlandFixture.cs`、`RiverlandTerrainTests.cs`

**接口**：`RiverlandMap.Id/Seed/Samples/CellSize/Border/WaterLevel/Create()`；内部 `RiverlandTerrain.Build(...)` 返回高度数组和各条线的 `DistanceGrid`，`RiverlandSurface.Build(...)` 返回 `SurfaceKind[]`（签名由实现者定，都是 `internal`）。

**布局常数**（`RiverlandLayout`，河谷设计 §3；坐标 x 向东、z 向南，单位米）

| 项 | 值 |
|---|---|
| 河中心线控制点 | (640, −20)（图外延伸）、(640, 0)、(600, 150)、(615, 300)、(670, 440)、(700, 560)、(665, 700)、(720, 850)、(840, 980)、(930, 1100)、(980, 1200)、(990, 1220)（图外延伸） |
| 河 | 水面半宽 18 m，河心深 1.3 m；沙岸 6～10 m（噪声扰动），从水边升到 +1 m；河谷：中心线 120 m 内平滑降到 +1.5 m |
| 浅滩 | 东西公路过河处（约 (700, 560)）、沙丘土路过河处（约 (850, 1000)）：路中心 ±12 m 内水深 0.5 m，河底是土 |
| 湖 | 中心 (330, 800)，半径 85 m，岸线 `85 + 12 × ValueNoise.Signed(角度)`，湖心深 1.6 m，10 m 沙滩 |
| 台地 | x 60～420、z 80～320，抬高 22 m，边缘噪声扰动 ±10 m，陡坡宽 6～8 m（> 38°）；南坡道中心线 (240, 420) → (232, 320)，东坡道 (420, 220) → (525, 232)，都宽 14 m、约 12°；台地顶平缓起伏（细噪声 ±1 m） |
| 山 | 西南 (180, 1060) r 140 h 18；东 (1000, 350) r 120 h 15；小丘 (470, 180) r 60 h 6、(340, 640) r 50 h 5、(560, 1110) r 55 h 7、(840, 290) r 60 h 8、(1100, 560) r 45 h 6 |
| 沙丘 | x 860～1170、z 880～1170：`h += 2 sin(2π u / 40)`，`u = (x + z)/√2`（沙垄东北—西南走向），边缘 30 m 淡入；地面全是沙 |
| 公路（8 m） | 东西：(30, 560)、(250, 520)、(450, 545)、(700, 560)、(860, 600)、(1000, 640)、(1170, 620)；北路：(900, 30)、(900, 180)、(800, 400)、(760, 575)；南路：(640, 555)、(590, 700)、(560, 800)、(500, 1000)、(520, 1170) |
| 土路（6 m，地面 `Dirt`） | 台地路：(250, 520)、(240, 420)、(232, 320)、(200, 220)；沙丘路：(500, 1000)、(700, 1010)、(850, 1000)、(1000, 1050) |
| 树林矩形（任务 8 用，地面在这里就是土） | 东 x 860～1160 z 420～800；西 50～240 × 620～880；北 680～840 × 40～200；东北 1040～1160 × 120～260；中 300～420 × 300～420；边缘用 `ValueNoise` 扰动 ±25 m |
| 村镇中心（任务 7 用） | 小镇 (700, 560)、北村 (900, 180)、西村 (250, 520)、南村 (500, 1000)；农庄 (440, 680)、(545, 330)、(790, 780)、(650, 880)、(115, 470)（实现时可在 ±40 m 内挪到合格的地方，挪了就改这里的常数） |

**算法要点**（`RiverlandTerrain`）

1. 底子：`h = 4 + 4 × Signed(n150) + 1 × Signed(n30)`，`n150` 特征尺度 150 m、`n30` 30 m（`ValueNoise.Fbm` 2～3 个八度）。
2. 台地、山、小丘、沙丘依次叠加（台地边缘用陡的 smoothstep：7 m 内升 22 m；坡道走廊里改成沿中心线线性 12°）。
3. 河谷：`w = 1 − SmoothStep(60, 120, d河)`，`h = lerp(h, 1.5, w)`；湖周 120 m 同样降到 +1.5。
4. 挖河：`d < 18`：`h = −1.3 × (1 − (d/18)²)`；`18 ≤ d < 26 + 扰动`：从 0 升到 +1（沙岸），再 14 m 内过渡到河谷地面。浅滩：路中心 ±12 m 内 `h = max(h, −0.5)`。湖同理（−1.6，10 m 沙滩）。
5. 公路和土路：沿中心线每 2 m 取原地形高度，60 m 滑动平均，再从两端各扫一遍把纵坡夹到 ≤ tan 8°（浅滩段固定为 −0.5）；路面半宽内（公路 4 m、土路 3 m）`h = 路面高`，外侧 5 m 路肩 smoothstep 回原地形。用 `Polyline.Stamp` 得到每个采样点到路的距离和沿线位置。
6. 陆地最低高度：离河中心线 30 m 外、湖岸线 15 m 外的采样点 `h = max(h, 0.4)`。
7. 边界带 30 m：`h += 8 × SmoothStep(0, 30, 30 − 到图边距离)`，但离河中心线 40 m 内不抬（河流到图边）。
8. 地面（`RiverlandSurface`，每格按格子中心判断，优先级从高到低）：公路（`d路 ≤ 4`）→ 土路（`d土路 ≤ 3`）→ 浅滩河底（浅滩范围内、水深 > 0）`Dirt` → 沙（河岸 `d河 < 26 + 扰动`、河床、湖滩、沙丘矩形）→ 树林（扰动后的矩形内）`Dirt` → 草地。村里院子的土地在任务 7 加。

**先写的测试**（`RiverlandTerrainTests`）

1. `Riverland_has_the_designed_size`：601 × 601 采样、格宽 2、边界 30、水位 0；`Surface` 600 × 600。
2. `Generation_is_deterministic`：生成两次，高度逐位相同、地面相同。
3. `Water_lies_only_in_the_river_and_the_lake`：所有 `WaterDepthAt > 0` 的格子中心，离河中心线 < 30 m 或离湖心 < 110 m。
4. `The_deepest_water_is_the_lake_centre`：全图最深 ≤ 1.62 m；湖心 (330, 800) 1.6（±0.1）；河心在 (615, 300) 1.3（±0.1）。
5. `The_fords_are_half_a_metre_deep`：两处浅滩中心的水深 0.5（±0.05），浅滩河底地面是 `Dirt`。
6. `Dry_land_stays_above_the_water`：离河中心线 40 m、湖岸 25 m 以外，所有采样点高 ≥ 0.4。
7. `Roads_are_flat_across_and_gentle_along`：每条公路和土路每 2 m 取一点：横向 ±3 m（土路 ±2 m）的高差 ≤ 0.1 m；沿线 10 m 窗口的坡度 ≤ 8°（±0.3°）；除两处浅滩 ±12 m，路中心不在水里。
8. `Road_cells_are_road_and_dirt_road_cells_dirt`：公路中心线上的格子都是 `Road`，土路中心线上都是 `Dirt`。
9. `The_plateau_stands_22_metres_up_behind_cliffs_with_two_ramps`：台地中心 (240, 200) 比台地南边 60 m 外的地面高 ≥ 18 m；台地四边各至少有一段坡度 > 38° 的陡坡；两条坡道中心线上坡度 ≤ 14°，坡道顶端和台地顶、底端和平地的高差 ≤ 1 m。
10. `Hills_and_dunes_are_where_the_layout_puts_them`：西南山顶比 200 m 外高 ≥ 14 m；东山 ≥ 11 m；沙丘区内除土路（和它的浅滩）外的格子都是 `Sand`，离河 40 m 外沿 (1,1) 方向每 40 m 有一道高差约 4 m（3～5 m）的沙垄。
11. `Banks_are_sand_and_open_plains_grass`：离公路和土路 15 m 以外的河段，河中心线两侧 22 m 处是 `Sand`；离路、水、树林、沙丘都超过 50 m 的格子是 `Grass`（用布局函数挑 20 个这样的点）。
12. `The_border_rises_except_where_the_river_runs_out`：图边一圈的采样点比往里 30 m 的点高 ≥ 6 m（离河中心线 40 m 内的除外）；河在北边、南边出界处，图边上有水。
13. `Terrain_alone_generates_quickly`：`Stopwatch` 量 `Create()`：Release ≤ 0.8 s，Debug ≤ 4 s（用 `#if DEBUG` 分开上限，并 `ITestOutputHelper` 打印实际耗时）。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~RiverlandTerrain" 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告；Sim 至少 +13（约 736）。可选：用 probe 工具把高度和地面导成 PNG（灰度和调色板）放进 `build/shots/5a-t06/`，人工看一眼河、湖、台地、路的形状和河谷设计 §3.2 的示意图大致相符。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): riverland terrain — plateau, hills, dunes, river, lake and fords, levelled roads, ground kinds`，末行 `Tests: Sim 723 → 736.`

### Task 7：村镇和房子

**Files:**
- Create: `src/Tank.Sim/Maps/Riverland/RiverlandVillages.cs`
- Modify: `src/Tank.Sim/Maps/RiverlandMap.cs`（`Create()` 加上房子、院墙、栅栏；房子地块压平；院子地面 `Dirt`）、`RiverlandTerrain.cs`/`RiverlandSurface.cs`（接受地块）
- Test: `tests/Tank.Sim.Tests/RiverlandVillageTests.cs`

**规则/要点**（河谷设计 §3.6）

- 四种房：小屋 8 × 6 m 高 6.5 m、农舍 12 × 7 m 高 7.5 m、两层楼 9 × 7 m 高 9 m、谷仓 14 × 9 m 高 8 m（`HalfExtents = (长/2, 宽/2)`，`Bottom` = 地块高 − 0.3，`Height` = 高 + 0.3）。小镇以两层楼和农舍为主，村子以小屋和农舍为主，农庄一栋农舍或谷仓加一栋小屋。
- 沿路摆：对每个村镇，在它半径内（小镇 160 m、北村和南村 110 m、西村 90 m）沿公路（西村、南村也用土路）两侧走，房子长边平行于路、正面（`AxisForward`）朝路，离路边 3～10 m（`XorShift`），左右间隔 4～12 m；候选不合格就往前挪 2 m 再试。
- 合格：地块（占地四边各加 3 m）不压路（离路边 ≥ 3 m）、不进水（地块内各采样点高 ≥ 0.6）、不进树林（扰动后的矩形外）、地块坡度不大（地块内原地形高差 ≤ 3 m）、和已有地块不重叠、离河中心线 ≥ 30 m。
- 合格后用 `HeightSculpt.Flatten` 把地块压平到地块内的平均高度（过渡 3 m），地块地面设为 `Dirt`。
- 数量：小镇约 22（20～24）、北村约 10（8～12）、西村约 8（6～10）、南村约 10（8～12）、农庄 5 处各 1～2 栋，合计 55～60。
- 院墙和栅栏（`WallLine`，长度取 2 m 的整数倍）：小镇里 6 处院子，房子背后 4～8 m 一道砖墙或石墙（10～16 m 长）加侧面栅栏；每个农庄一圈栅栏（约 30 × 24 m，留一个 4 m 的口子朝路）。墙线不压路、不进水、不压房子地块。
- 结果用一个 `PlacementGrid` 记下（任务 8 的岩石、草丛、树要避开地块）。

**先写的测试**（`RiverlandVillageTests`）

1. `There_are_55_to_60_houses_spread_over_the_villages`：总数 55～60；小镇中心 160 m 内 20～24、北村 110 m 内 8～12、西村 90 m 内 6～10、南村 110 m 内 8～12；其余在 5 个农庄 40 m 内，每处 1～2 栋。
2. `Houses_come_in_four_sizes`：每栋的（长, 宽, 高）是四种之一（±0.01 m）。
3. `Each_house_stands_on_a_level_plot`：地块（四边加 3 m）内采样点的高差 ≤ 0.3 m。
4. `Houses_keep_off_roads_water_forests_and_each_other`：占地离最近路边（公路 4 m、土路 3 m 半宽之外）≥ 3 m；地块内无水；不在树林里；任意两块地块不重叠。
5. `Village_houses_face_their_road_from_3_to_10_metres`：村镇里（农庄除外）每栋离最近路边 3～10 m，`AxisForward` 与“指向最近路点”的夹角 < 30°。
6. `Neighbours_along_a_road_leave_4_to_12_metres`：同一侧相邻两栋的间隔在 4～12 m（每个村镇里沿路排序后相邻的）。
7. `Walls_and_fences_are_whole_segments_off_the_roads`：每段墙、栅栏都是 2 m 段（`WallLine.SegmentCount` 不抛），离路边 ≥ 1 m，不进水，不压地块里的房子占地。
8. `Each_farm_has_a_fence_ring_with_a_gap`：每个农庄有一圈栅栏，包住它的房子，且有一个 ≥ 4 m 的口子。
9. `Yard_ground_is_dirt`：每栋房子地块里的格子是 `Dirt`（压着路的格子除外，地块本来就不压路）。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~RiverlandVillage|FullyQualifiedName~RiverlandTerrain" 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告（任务 6 的测试照过：压平地块不碰路和水）；Sim 至少 +9（约 745）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): riverland villages — 55–60 houses facing their roads on level plots, yard walls and farm fences`，末行 `Tests: Sim 736 → 745.`

### Task 8：岩石、草丛、树和编号顺序

**Files:**
- Create: `src/Tank.Sim/Maps/Riverland/RiverlandScatter.cs`
- Modify: `src/Tank.Sim/Maps/RiverlandMap.cs`（最后按岩石、草丛、树、房子、院墙和栅栏分配编号）
- Test: `tests/Tank.Sim.Tests/RiverlandScatterTests.cs`

**规则/要点**（河谷设计 §3.7～3.9）

- **树**（树干半径 0.35～0.6，树高 9～15，`Bottom` = 地面 − 0.3）：五片树林各用 `PoissonDisc`，最小间距 6 m，林缘 20 m 内最小间距从 6 渐增到 12（变稀）；零散树约 400 棵：河岸（河中心线 30～60 m）、路边（路边 4～15 m）、村子周围，单棵或 3～5 棵一丛（丛内间距 ≥ 3 m）。不种：离路边 3 m 内、水里（树干圆内任何采样点有水）、房子地块、坡度 > 30°、和岩石重叠。合计约 5000（4000～6000）。
- **草丛**：约 70 片，片中心之间 ≥ 40 m，放在 13 m 内坡度 < 10° 的平地，优先河岸、林缘、村外、山坡下（按位置给权重，`XorShift` 抽）；每片 12～18 丛，规则同试驾场（半径 3.2～4.6、高 4.2～5.2、中心一丛 ≥ 3.8）；不放在路中心 6 m 内、水里、房子地块、离林缘 20 m 以上的林子深处。合计约 1000（800～1300）。
- **岩石**：约 600（500～700）：台地陡坡脚下和坡道两边约 200，两座圆山和小丘上约 100，沙丘约 80，平原上成堆约 220（3～8 块一堆）；尺寸和下沉同试驾场；不压路（离路边 3 m）、不进地块、不下水；离出生点 15 m 以上的检查在任务 9（出生点挑在岩石之后）。
- 邻近检查都用 `PlacementGrid`。
- 编号：岩石 1…，草丛接着，树接着，房子接着，院墙和栅栏最后（`WallLine.Segments(terrain, firstId)`）。
- 耗时：整张图 `Create()` 在 M3 上 Release ≤ 1.5 s。

**先写的测试**（`RiverlandScatterTests`）

1. `Prop_ids_run_in_kind_order`：编号从 1 起连续；种类顺序是 `Rock*`、`Bush*`、`Tree*`、`House*`、然后 `BrickWall|StoneWall|Fence`。
2. `Counts_are_in_the_designed_ranges`：岩石 500～700、草丛 800～1300、树 4000～6000、房子 55～60；物件总数 ≤ 16384（`BattleMessages.MaxMapProps`）。
3. `Trees_have_designed_sizes`：树干 0.35～0.6、高 9～15、占地是圆。
4. `Forest_trees_are_at_least_6_metres_apart`：五片树林矩形内的树两两间距 ≥ 6 m；林子里的树占全部的 ≥ 80%。
5. `Nothing_grows_on_roads_in_water_or_on_plots`：树、岩石离路边 ≥ 3 m；草丛中心离路中心 ≥ 6 m；三者都不在水里（中心处水深 0）、不在任何房子地块里。
6. `Trees_avoid_steep_ground_and_rocks`：每棵树中心坡度 ≤ 30°，树干圆不与岩石占地重叠。
7. `Bush_patches_are_40_metres_apart_on_flat_ground`：按片的中心（每片第一丛）两两 ≥ 40 m；中心 13 m 内坡度 < 10°。
8. `Rocks_pile_where_the_layout_says`：台地陡坡带 30 m 内 ≥ 150 块；沙丘矩形内 ≥ 60 块。
9. `The_whole_map_generates_within_budget`：`Stopwatch` 量 `RiverlandMap.Create()`：Release ≤ 1.5 s，Debug ≤ 6 s；打印耗时。
10. `Riverland_battle_is_identical_with_and_without_the_index`：`RiverlandFixture.Field.Fresh()`，8 辆车放在小镇西口、东林林缘、浅滩两岸（布局常数附近的空地，测试里挑离物件 10 m 的点），用`PropIndexEquivalenceTests` 里共用的 `RunTwice` 和脚本输入（`XorShift(5)`：每 45 tick 换一次油门和转向、瞄准偏航匀速扫、装填好就开火、每 3 发换一次弹种）跑 900 tick，`UseIndex` 开关两遍逐 tick 相同。

**命令**：`dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Riverland" 2>&1 | grep -E '通过!|失败!'`；耗时看 `dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~generates_within_budget" --logger "console;verbosity=detailed"`。

**验收**：全部测试通过、0 警告；Release 生成耗时记进度；Sim 至少 +10（约 755）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): riverland rocks, bush patches and five forests — ids in kind order, generation under 1.5 s`，末行 `Tests: Sim 745 → 755.`

### Task 9：出生点、路点、试驾场站位、地标和地图目录

**Files:**
- Create: `src/Tank.Sim/Maps/MapDefinition.cs`、`src/Tank.Sim/Maps/MapCatalog.cs`
- Modify: `src/Tank.Sim/Maps/RiverlandMap.cs`（`Spawns`、`Waypoints`、`Spawn`、`SpawnYaw`、`TargetSpots`、靶车下标常数、`Landmarks`）、`src/Tank.Sim/Maps/SandboxMap.cs`（只加 `Id`）
- Test: `tests/Tank.Sim.Tests/RiverlandSpawnTests.cs`、`RiverlandSitesTests.cs`、`MapCatalogTests.cs`、`RiverlandFingerprintTests.cs`、`RiverlandPerformanceTests.cs`；`RiverlandFixture` 改用 `MapCatalog.Riverland.NewBattlefield()`
- 开发工具（仓库外）：`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe/`

**接口**：共享接口“地图”。

**规则/要点**（河谷设计 §3.10、§3.11、§4.4）

- 候选用 probe 工具按规则找好，写成常数（和试驾场一样），由测试检查。规则：所有点 10 m 内坡度 < 8°；离任何挡车物件（岩石、房子、墙、栅栏）、水、房子地块 ≥ 10 m；10 m 内没有树；离岩石 ≥ 15 m（出生点）；所有出生点和路点在同一个 `Passability` 连通块里（`maxSlope` 32°、`clearance` 1.6 m）。
- 两队：队 0 在西（中心 x < 400）、队 1 在东（中心 x > 800），两个中心相距 ≥ 700 m；每队 5 个点彼此 8～15 m，车头朝对方中心（`Angles.YawOf`，同 `SandboxMap.TeamSpawns`）。各自为战 16 个，两两 ≥ 150 m，车头朝地图中心 (600, 600)。路点 35～45 个，两两 ≥ 60 m，四个象限各 ≥ 6 个。
- 试驾场出生：东西公路中心线上、小镇以西约 150 m（沿公路从 (700, 560) 往回 150 m 的点，由布局的折线算出，不写死），车头朝东（`Angles.YawOf(new Vector2(1, 0))`）。
- 8 辆静止靶车（`TargetSpot`，常数，按河谷设计 §3.11 的顺序和意图）：0 中坦在公路上小镇西口、正对玩家约 80 m；1 重坦在小镇一栋房子背后（从出生点看不见，打塌那栋房子才看得见）；2 轻坦在浅滩中间；3 TD 在对岸东林林缘的树冠下；4 中坦在西岸一片草丛里；5 重坦在台地边上、约 300 m；6 轻坦在河岸沙滩上；7 中坦在南边开阔地。
- `Landmarks`：`Town` (700, 560)、`Ford` 小镇浅滩中心、`SouthFord` 沙丘路浅滩中心、`ForestEdge` 东林西缘公路旁、`PlateauRim` 台地东南角、`Dunes` (1015, 1025)、`Lake` (330, 800)、`MapCentre` (600, 600)。
- `MapDefinition.NewBattlefield()`：第一次调用时生成并私下保存一份，之后每次返回它的 `Fresh()`。`Data` 是 `Lazy<MapData>`（`ExecutionAndPublication`），与 `NewBattlefield` 共用同一份生成结果（不生成两遍）。
- `MapCatalog.Sandbox` 的显示名是“试驾场地图”（阶段 5 设计 §2 的说法），`Riverland` 是“河谷”。

**先写的测试**

- `RiverlandSpawnTests`（照 `SandboxSpawnTests` 的写法，`AssertOpenGround` 按上面的规则）：
  1. `Each_team_has_five_spawns_west_and_east_facing_each_other`：每队 5 个；中心相距 ≥ 700 m；队 0 中心 x < 400、队 1 > 800；队内两两 8～15 m；车头偏差 < 10°；都在空地上。
  2. `Sixteen_solo_spawns_spread_over_the_map`：16 个，两两 ≥ 150 m，朝向地图中心（< 10°），都在空地上，离岩石 ≥ 15 m。
  3. `Waypoints_cover_the_map_on_open_ground`：35～45 个，两两 ≥ 60 m，四个象限各 ≥ 6 个，都在空地上。
  4. `Every_spawn_and_waypoint_is_reachable_from_every_other`：`Passability` 下全部在同一个连通块。
  5. `Spawns_keep_off_water_plots_and_forests`：10 m 内没有水、地块、树。
- `RiverlandSitesTests`：
  1. `The_test_drive_starts_on_the_road_west_of_town_facing_east`：`SurfaceAt(Spawn) == Road`；离 `Town` 130～170 m；`SpawnYaw` 与正东偏差 < 10°。
  2. `Target_0_is_a_medium_tank_on_the_road_at_the_west_entrance`：中坦，`Road`，离出生点 70～90 m，车头朝出生点（< 15°）。
  3. `Target_1_hides_behind_a_house_until_it_falls`：重坦；从出生点车的炮塔顶 `Spotting.Look` 到它：`LineClear` 为假，两条视线中至少一条最先打到的是一栋房子；把那栋房子 `SetDurability(…, 0)` 后 `LineClear` 为真。
  4. `Target_2_wades_in_the_ford`：轻坦，水深 0.3～0.7 m，在 `Ford` 20 m 内。
  5. `Target_3_waits_under_a_crown_across_the_river`：TD，在河东岸，有一棵树离它 ≤ 0.75 × 树冠半径；离东林矩形边 ≤ 30 m。
  6. `Target_4_sits_in_a_bush_on_the_west_bank`：中坦，有一丛草离它 ≤ 0.75 × 半径，在河西岸、离河中心线 ≤ 120 m。
  7. `Target_5_watches_from_the_plateau_rim_far_away`：重坦，离出生点 250～350 m，比出生点高 ≥ 18 m。
  8. `Target_6_stands_on_beach_sand`：轻坦，`Sand`、干的、离河中心线 18～30 m。
  9. `Target_7_stands_in_open_ground_to_the_south`：中坦，z 比出生点大 ≥ 150，15 m 内没有挡车的物件。
  10. `Every_target_is_clear_of_solid_props_and_inside_the_playable_area`：车体占地不与岩石、房子、墙、栅栏重叠；在可玩区内。
- `MapCatalogTests`：
  1. `The_catalog_lists_sandbox_then_riverland`：`All` 依次是 `Sandbox`、`Riverland`；`Id`、`Name` 分别是 `sandbox`/“试驾场地图”、`riverland`/“河谷”；`SandboxMap.Id == "sandbox"`、`RiverlandMap.Id == "riverland"`。
  2. `Lookup_ignores_case_and_rejects_unknown_ids`：`TryGet("RiverLand")` 真；`TryGet("x")`、`TryGet(null)` 假；`Get("x")` 抛 `ArgumentException`。
  3. `Spawns_and_waypoints_are_the_maps_own`：`Sandbox.Spawns == SandboxMap.Spawns` 等（引用相等）。
  4. `Map_data_is_built_once`：自定义 `MapDefinition`（创建函数计数）：`IsDataReady` 先假；取两次 `Data` 是同一个实例、创建函数只调一次；之后 `IsDataReady` 真。
  5. `New_battlefields_share_the_geometry_but_not_the_state`：两次 `NewBattlefield()` 是不同实例；放倒其中一个的物件，另一个不受影响；地形采样、物件逐个相同；创建函数总共只调一次（和 `Data` 共用）。
  6. `Riverland_data_round_trips_to_the_same_ground`：`MapCatalog.Riverland.Data.Build()` 的 `Surface` 与生成结果相同、`WaterLevel` 0、物件相同；100 个随机点的高度差 ≤ 0.005 m。
- `RiverlandFingerprintTests`：
  1. `Riverland_is_bit_for_bit_what_it_was`：FNV-1a 64（初值 `14695981039346656037`、乘数 `1099511628211`，每个 32 位数按小端 4 字节喂）：先喂每个高度采样（`j` 外 `i` 内，`SingleToUInt32Bits`），再喂每个物件的 `Id`、`(uint)Kind`、中心 x/z、半宽 x/z、朝向、`Bottom`、`Height`（浮点取位），最后每个地面格子一个字节、水位的厘米数；期望值在本任务完成时实测后写成常数（以后谁改了生成就会失败，改常数要在提交说明里写原因）。
  2. `Stepping_ten_tanks_on_riverland_allocates_nothing`：10 辆车放在 `Spawns.Solo` 前 10 个点，由 `SkirmishBot`（路点用 `RiverlandMap.Waypoints`）驾驶，预热 300 tick 后再跑 300 tick，`Simulation.Step` + `VisionTracker.Update` 分配 0。
- `RiverlandPerformanceTests`（只打印和设宽松的 Debug 上限，严格上限只在 Release 断言）：
  1. `A_tick_with_ten_tanks_is_cheap`：同上 10 辆车，900 tick 平均：Release ≤ 0.5 ms，Debug ≤ 3 ms。
  2. `Spotting_sixteen_tanks_is_cheap`：16 辆车在 `Spawns.Solo`，`VisionTracker.Update` 平均：Release ≤ 1.5 ms，Debug ≤ 8 ms。

- **批次 A 审查留下的（查询缓冲余量）**：`RiverlandQueryHeadroomTests.Query_candidates_stay_under_half_the_buffer`——在河谷上沿真实的行驶和射击路径（东林林中、小镇街道、台地坡脚岩石带各一条，中坦开过、每 tick 对视野内物件做点亮、炮弹沿路径飞），统计 `Resolve`（Blockers Near）、点亮遮挡（Foliage Near）、`KnockThrough`（Flimsy Along）、`Raycast`（ShellStoppers Along）每次查询的候选数最大值，断言都小于各自缓冲区（512 / 1024）的一半；统计用测试里的独立计数（对每个查询点在测试里调用 `Battlefield.Near/Along` 复算候选数），不在仿真里加计数器。

**命令**

```bash
dotnet run --project /Users/murphy/Desktop/dev/BattleCity-dev-scripts/riverland-probe -c Release   # 找候选、检查规则
dotnet test tests/Tank.Sim.Tests --filter "FullyQualifiedName~Riverland|FullyQualifiedName~MapCatalog" 2>&1 | grep -E '通过!|失败!'
dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~RiverlandPerformance|FullyQualifiedName~within_budget" --logger "console;verbosity=detailed" 2>&1 | grep -E "ms|通过!|失败!"
```

**验收**

- [ ] 全部测试通过、0 警告；Release 下生成 ≤ 1.5 s、10 车 tick ≤ 0.5 ms、16 车点亮 ≤ 1.5 ms，数字记进度。
- [ ] Sim 至少 +25（约 780）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(sim): riverland spawns, waypoints, test-drive spots and landmarks — map definitions and the map catalog`，末行 `Tests: Sim 755 → 780.`

**批次 B 收尾**：两路审查（规格审查要逐条对照河谷设计 §3 和 §7 的仿真部分）→ 跟进提交 → 进度第 2 行（各项生成耗时、物件数、指纹常数）。

## 批次 C：联网

### Task 10：不可靠包上限

开工前已核对（本计划写作时）：main 上 `src/Tank.Net/Battle/BattleServer.cs` 第 20 行仍是 `MaxUnreliableBytes = 1150`；LiteNetLib 2.1.4 的 `NetConstants.InitialMtu` 是 1024、`MtuDiscovery` 默认关（我们的传输没打开），`NetPeer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable)` 是 1023；用两个 `NetManager` 实测：1023 字节的不可靠包能收到，1024 字节起 `Send` 抛 `LiteNetLib.TooBigPacketException`（“exceeded maximum of 1023 bytes”）。快照落在 1024～1150 字节时这一 tick 后面的发送全部丢掉（异常由 `ServerRunner` 记日志吞掉）。问题还在，照修。

**Files:**
- Modify: `src/Tank.Net/Battle/BattleServer.cs`（第 20 行改为 `internal const int MaxUnreliableBytes = 1000;`，注释写明上面的依据；加 `internal static Channel ChannelFor(int bytes)`）、`src/Tank.Net/Battle/BattleServer.Match.cs`（第 200 行改用 `ChannelFor(_writer.ByteLength)`）
- Test: `tests/Tank.Net.Tests/Battle/UnreliableLimitTests.cs`

**先写的测试**

1. `The_unreliable_limit_fits_one_LiteNetLib_packet`：`BattleServer.MaxUnreliableBytes <= LiteNetLib.NetConstants.InitialMtu - LiteNetLib.NetConstants.HeaderSize`（1023）。
2. `Bigger_snapshots_go_reliable`：`ChannelFor(1000) == Channel.Unreliable`，`ChannelFor(1001) == Channel.Reliable`。
3. `LiteNetLib_carries_the_limit_and_refuses_a_byte_more`：真 UDP：`LiteNetServerTransport(0, 2, BattleProtocol.ConnectKey)` 加 `LiteNetClientTransport("127.0.0.1", 端口, …)`，连上后服务器向客户端发 1000 字节不可靠包，客户端 2 秒内收到 1000 字节；直接对底层 `NetPeer`（`transport.Manager`）发 1024 字节不可靠包抛 `TooBigPacketException`。绑不了端口时照 `DiscoveryTests` 的做法直接返回。

**命令**：`dotnet test tests/Tank.Net.Tests --filter "FullyQualifiedName~UnreliableLimit" 2>&1 | grep -E '通过!|失败!'`

**验收**：全部测试通过、0 警告；Net 170 → 至少 173。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`fix(net): unreliable snapshots capped at 1000 bytes — LiteNetLib 2.1.4 sends at most 1023 unreliable bytes, bigger ones threw`，末行 `Tests: Net 170 → 173.`

### Task 11：协议 v3 的地图编码

**Files:**
- Modify: `src/Tank.Net/Protocol/BitWriter.cs`（`WriteAligned`）、`src/Tank.Net/Battle/BattleProtocol.cs`（第 34 行 `Version = 3`）、`src/Tank.Net/Battle/BattleMessages.cs`（`MaxMapProps`；`WriteMap` 第 162～187 行追加段；`ReadMap` 第 189～219 行；第 205 行种类上限改为 `Prop.LastKind`；`ReadProps` 加上限参数；`ReadMatchStart` 用地图的物件数做上限；新重载 `Write(BitWriter, MatchStartMessage, ReadOnlySpan<byte>)`）
- Modify test: `tests/Tank.Net.Tests/Battle/BattleProtocolTests.cs` 第 328 行 `Assert.Equal(2, …)` 改为 3（口令那行不变）
- Test: `tests/Tank.Net.Tests/Battle/MapWireV3Tests.cs`

**接口**：共享接口“联网”的 `WriteAligned`、`Version`、`MaxMapProps`、地图编码和预编码写法。

**规则/要点**（河谷设计 §5.1）

- v2 的字节一个不变，后面**必有**追加段：标志字节（第 0 位有水，其余位必须为 0，否则 `ProtocolException`）；有水时 `VarInt` 水位厘米；地面按行程编码：`VarUInt` 段数，然后每段 `VarUInt` 长度（≥ 1）+ 1 字节种类（≤ `SurfaceMap.LastKind`），长度合计必须等于格子数 `(sx − 1) × (sz − 1)`。
- 读之前先查剩余位数再分配：物件数 ≤ `MaxMapProps` 且 ≤ `RemainingBits / 240`（每个物件至少 30 字节）；段数 ≤ 格子数且 ≤ `RemainingBits / 16`。水位超出 `MapData.MaxWaterLevelCm`、长度合计不符、零长度段都抛 `ProtocolException`（`MapData` 构造的 `ArgumentException` 照旧转成 `ProtocolException`）。
- 开局消息的物件状态数 ≤ 地图物件数（`ReadProps(ref r, max)`）；事件批次仍是 `MaxPropsPerMessage`（4096）。
- `Write(w, m, encodedMap)`：写消息头，`WriteAligned(encodedMap)`，再写物件状态和车号；与 `Write(w, m)` 逐字节相同（`MatchStart` 的地图前只有整字节字段，所以是对齐的）。
- 口令 `ConnectKey` 不变：旧版本连上来照样收到“版本不一致：房主 v3，你的客户端 v2。”。

**先写的测试**（`MapWireV3Tests`）

1. `The_v2_part_of_the_sandbox_map_is_byte_for_byte_unchanged`：`WriteMap(MapData.Of(SandboxMap.Create()))` 共 53053 字节；前 53047 字节的 FNV-1a 64（初值 `14695981039346656037`、乘数 `1099511628211`，逐字节喂）等于 `0xF0121BE3E7E7EC72`（v2 编码，写作时在 `80d6eca` 上实测）；最后 6 字节是 `00 01 C0 B8 02 00`（无水、1 段、长 40000、草地）。
2. `Surface_water_trees_and_houses_round_trip`：一张 41 × 41 的小图：混合四种地面、水位 −37 cm、2 棵树、1 栋房子、1 段砖墙 → `ReadMap` 后高度、物件、地面、水位都相同。
3. `A_map_without_water_has_no_level`：`WaterLevelCm` 为 null 往返仍为 null，标志字节是 0。
4. `Unknown_flag_bits_are_refused`：把标志字节改成 0x02 → `ProtocolException`。
5. `Run_lengths_must_add_up_to_the_cells`：段长合计少 1、多 1、有一段长 0 → `ProtocolException`。
6. `Surface_kinds_and_prop_kinds_beyond_the_last_are_refused`：地面种类 4、物件种类 7 → `ProtocolException`；物件种类 5、6 可以读。
7. `Too_many_props_or_runs_are_refused_before_allocating`：物件数写 16385 → `ProtocolException`；物件数写 16384 但后面只有 10 字节 → `ProtocolException`，且 `GC.GetAllocatedBytesForCurrentThread` 增量 < 64 KB；段数写成格子数 + 1 → `ProtocolException`。
8. `A_water_level_beyond_the_limit_is_refused`：水位 1 000 001 → `ProtocolException`。
9. `Every_truncation_of_a_v3_map_is_a_protocol_error`：小图编码的每个前缀长度都抛 `ProtocolException`。
10. `More_prop_states_than_map_props_are_refused`：小图（5 个物件）的开局消息带 6 条物件状态 → `ProtocolException`；5 条可以。
11. `A_prepared_map_writes_the_same_match_start`：`Write(w, m)` 与 `Write(w2, m, 预编码的地图字节)` 的字节相同（试驾场地图、物件状态 2 条、车号 7）。
12. `WriteAligned_needs_a_byte_boundary`：写 1 位后 `WriteAligned` 抛 `InvalidOperationException`；对齐时原样追加、`ByteLength` 加上长度。

**命令**：`dotnet test tests/Tank.Net.Tests 2>&1 | grep -E '通过!|失败!'`（`BattleProtocolTests` 的截断、往返、90 KB 测试应不改照过，试驾场开局消息现在是 53060 字节）

**验收**：全部测试通过、0 警告；Net 至少 +12（约 185）；Soak 照跑一遍 `dotnet run --project tests/Tank.Soak -c Release`，四种链路 PASS。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(net): protocol v3 — map data carries ground kinds and the water level, 16384 props, prepared map bytes for the match start`，末行 `Tests: Net 173 → 185.`

### Task 12：服务器选图、准备和缓存、`Tank.Server --map`

**Files:**
- Create: `src/Tank.Net/Battle/ServerMaps.cs`
- Modify: `src/Tank.Net/Battle/BattleServerOptions.cs`（`Map`、`PrepareMapsInBackground`；`Maps` 在任务 13）、`src/Tank.Net/Battle/BattleServer.cs`（构造时 `Prepare(options.Map)`；`RoomMap`、`RoomMapReady`；`internal ServerMaps PreparedMaps`）、`src/Tank.Net/Battle/BattleServer.Match.cs`（第 45～48 行改用准备好的地图和它的出生点；第 55 行路点用 `RoomMap.Waypoints`；`SendMatchStart` 第 273～286 行用预编码字节）、`src/Tank.Server/Program.cs`（第 18～19 行用法、第 77～103 行 `--map`；`PrepareMapsInBackground = true`；地图准备好打一行日志）
- Test: `tests/Tank.Net.Tests/Battle/ServerMapTests.cs`、`tests/Tank.Net.Tests/Battle/TestMaps.cs`

**接口**：共享接口“联网”的 `BattleServerOptions.Map/PrepareMapsInBackground`、`ServerMaps`、`BattleServer.RoomMap/RoomMapReady`。

**规则/要点**（河谷设计 §5.2，阶段 5 设计 §2）

- `ServerMaps.Prepare`：前台模式当场 `new PreparedMap(map, map.Data, 编码)`；后台模式 `Task.Run` 做同样的事，完成后写进一个 `ConcurrentDictionary<MapDefinition, PreparedMap>`；正在做的记在另一个字典里，重复调用不重复做。`MapDefinition.Data` 本身是线程安全的 `Lazy`，同一进程里的多个服务器共用生成结果，编码每个 `ServerMaps` 一次。
- `TryStartMatch`：房间的图没准备好 → `reason = "the map is still being generated"`，不补机器人、返回 false。准备好了：`_map = prepared.Data`、`_sim = new Simulation(prepared.Data.Build())`、`MatchRules(..., RoomMap.Spawns, ...)`、机器人用 `RoomMap.Waypoints`，记下 `prepared` 给 `SendMatchStart`（每个座位的开局消息直接拷贝 `prepared.EncodedMap`）。日志 `match started: <模式>, map <id>, …`。
- 机器人在河谷上照旧是 `SkirmishBot`：直线开向路点、卡住 3 秒换路点（阶段 5 设计 §2；寻路是 5C），只把路点表换成 `RoomMap.Waypoints`。
- 不在 tick 线程里生成：后台模式下 tick 线程只读字典。前台模式（默认）给测试和回环房主用，生成发生在构造或 `SetMap` 的调用线程上。
- `Tank.Server`：`--map sandbox|riverland`，默认 `riverland`（阶段 5 设计：新房间默认河谷）；不认识的名字 `unknown map 'x'` 退出码 2；用法改为 `usage: Tank.Server [--port N] [--mode teambattle|annihilation|ffa] [--map sandbox|riverland] [--bots N] [--name ROOM]`，第二行写明默认 `riverland`；启动日志加上地图名；准备好时打 `map <id> ready in <ms> ms`。
- `TestMaps`（Net 测试共用）：`TestMaps.Small(string id)`：101 × 101 平地、格宽 2、边界 4、无物件，出生点队 0 两个 (40, 100)、(40, 110)、队 1 两个 (160, 100)、(160, 110)，各自为战 4 个，路点 4 个，返回 `MapDefinition`；`TestMaps.Gated(string id, ManualResetEventSlim gate, Action onCreate)`：创建函数先 `onCreate()` 再等 `gate`。

**先写的测试**（`ServerMapTests`，用 `BattleHarness` 和假传输）

1. `The_default_room_map_is_the_sandbox`：`new BattleServerOptions().Map == MapCatalog.Sandbox`；默认服务器 `RoomMapReady` 构造后立即为真（前台）。
2. `A_match_is_played_on_the_rooms_map`：`Map = TestMaps.Small("small")`：开局后客户端 `Field` 的地形尺寸 201 m、物件 0 个；服务器上两辆车的出生位置是 `Small` 的出生点。
3. `Bots_drive_towards_the_maps_waypoints`：`Small` 上 1 个机器人、1 个客户端，跑 20 秒：服务器日志没有 `error`；机器人的车离开出生点 ≥ 20 m，且离 `Small` 最近的路点比开局时近（只从外面看，不读机器人的内部状态）。
4. `The_map_is_encoded_once_however_many_join`：开两局、中途加入一人、请求一次重同步：`PreparedMaps.Encodings == 1`。
5. `In_the_background_the_map_is_generated_off_the_calling_thread`：`Gated` 地图、`PrepareMapsInBackground = true`：`onCreate` 记下的线程不是测试线程；放行前 `RoomMapReady` 假、`TryStartMatch` 返回 false 且原因含 `generating`；放行并等 `RoomMapReady`（最多 5 秒）后 `TryStartMatch` 成功。
6. `In_the_foreground_the_map_is_ready_at_once`：同样的 `Gated` 地图先放行、前台模式：构造后 `RoomMapReady` 真，`onCreate` 在测试线程上。
7. `A_failed_generation_is_logged_and_never_ready`：创建函数抛异常、后台模式：日志里有 `map <id> failed`，`RoomMapReady` 一直假，`TryStartMatch` 一直 false。
8. `Riverland_match_starts_send_the_prepared_bytes`：`Map = MapCatalog.Riverland`：开局消息里地图段的字节等于 `PreparedMaps.Get(...).EncodedMap`（从假传输记下的包里截出来比）。

**命令**

```bash
dotnet test tests/Tank.Net.Tests --filter "FullyQualifiedName~ServerMap" 2>&1 | grep -E '通过!|失败!'
dotnet run --project src/Tank.Server -- --map nope; echo "exit $?"                     # unknown map 'nope'，exit 2
timeout 20 dotnet run --project src/Tank.Server -c Release -- --map riverland --mode ffa --bots 3 --port 27094   # 日志：map riverland ready in … ms
```

**验收**：全部测试通过、0 警告；Soak 四种链路 PASS（数字与任务 11 后相同）；Net 至少 +8（约 193）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(server): rooms play on a map definition — prepared once (in the background for real hosts), Tank.Server --map`，末行 `Tests: Net 185 → 193.`

### Task 13：房间里选图（协议、服务器、`BattleClient`）

**Files:**
- Modify: `src/Tank.Net/Battle/BattleMessages.cs`（`RoomStateMessage` 第 20 行、`RoomCommandKind` 第 22～31 行、`RoomInfoMessage` 第 47 行；读写第 87～132 行、第 296～319 行）、`src/Tank.Net/Battle/BattleProtocol.cs`（`MaxMapIdBytes`）、`src/Tank.Net/Battle/BattleServerOptions.cs`（`Maps`）、`src/Tank.Net/Battle/BattleServer.Room.cs`（`HandleRoomCommand` 第 102～135 行加 `SetMap`；`BroadcastRoom` 第 157～166 行带地图）、`src/Tank.Net/Battle/BattleServer.cs`（`WriteRoomInfo` 第 88～92 行带地图；`Tick` 里地图刚就绪就 `_roomDirty = true`）、`src/Tank.Net/Battle/BattleClient.cs`（第 94～100 行旁加 `SetMap`）
- Test: `tests/Tank.Net.Tests/Battle/RoomMapTests.cs`

**接口**：共享接口“联网”的 `RoomStateMessage`、`RoomInfoMessage`、`RoomCommandKind.SetMap`、`MaxMapIdBytes`、`BattleServerOptions.Maps`、`BattleClient.SetMap`。

**规则/要点**（阶段 5 设计 §2）

- 线上：房间状态在玩家表之后加 `String(Map, MaxMapIdBytes)`、`Bool(MapReady)`；房间信息在端口之后加 `String(Map, MaxMapIdBytes)`。`ReadRoomInfo` 先读版本：版本 ≥ 3 才读地图（v2 的房间回复里没有它，`Map = ""`，房间列表照旧显示“版本不符”而不是整条丢掉）。`ReadRoomCommand` 的上限改为 `SetMap`。
- `SetMap`：只有房主、只在 `_sim is null` 时；参数按 `Maps` 里的 `Id` 不区分大小写找，找不到就忽略；找到就 `RoomMap = 它`、`Prepare(它)`、`_roomDirty = true`。其余命令不变。
- 房间状态的 `MapReady` = `RoomMapReady`；地图在后台准备好的那一 tick 广播一次房间状态。
- 房间信息（局域网发现）带当前房间地图的 `Id`。

**先写的测试**（`RoomMapTests`）

1. `Room_state_round_trips_the_map_and_its_readiness`：`("riverland", MapReady: false)` 往返相同；默认参数构造的是 `("sandbox", true)`。
2. `Room_info_round_trips_the_map`：v3 往返带 `"riverland"`。
3. `A_v2_room_info_reads_without_a_map`：手写 v2 布局（版本 2、名字、模式、人数、上限、对局中、端口，后面没有字节）→ 读出 `Map == ""`，不抛。
4. `A_map_id_longer_than_the_limit_is_refused`：手写一条长 17 字节的地图名 → `ProtocolException`。
5. `The_set_map_command_is_known_and_the_next_is_not`：命令 7 能读，8 抛 `ProtocolException`。
6. `The_host_switches_the_map_and_everyone_sees_it`：`Maps = [Small("a"), Small("b")]`、`Map = a`：房主 `SetMap("B")` → 两个客户端的 `Room.Map == "b"`；开局后 `Field` 是 `b` 的。
7. `Only_the_host_and_only_in_the_room_may_switch`：非房主 `SetMap("b")` 无效；对局中房主 `SetMap("b")` 无效；`SetMap("nope")` 无效。
8. `A_map_still_generating_holds_the_start`：`Maps = [a, Gated("g")]`、后台模式：房主 `SetMap("g")` → 房间状态 `MapReady == false`；房主 `StartMatch` 无效（`MatchRunning` 仍假，日志 `can't start: the map is still being generated`）；放行后 1 秒内房间状态 `MapReady == true`，再 `StartMatch` 开局。
9. `Lan_discovery_answers_with_the_rooms_map`：`WriteRoomInfo` 写出的包读回来 `Map == RoomMap.Id`；`SetMap` 之后跟着变。
10. `Switching_back_needs_no_second_preparation`：a → b → a：`PreparedMaps.Encodings == 2`。

**命令**：`dotnet test tests/Tank.Net.Tests 2>&1 | grep -E '通过!|失败!'`；`dotnet test tests/Tank.Art.Tests 2>&1 | grep -E '通过!|失败!'`（`RoomRulesTests`、`LanRoomRowTests` 应不改照过）

**验收**：全部测试通过、0 警告；Net 至少 +10（约 203）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(net): the host picks the room's map — room state and LAN answers carry it, starts wait for its generation`，末行 `Tests: Net 193 → 203.`

### Task 14：河谷上的对局测试

**Files:**
- Modify: `tests/Tank.Net.Tests/Battle/TestMaps.cs`（加 `TestMaps.Mixed()`）
- Test: `tests/Tank.Net.Tests/Battle/RiverlandMatchTests.cs`

**规则/要点**

- `TestMaps.Mixed()`：301 m × 301 m（151 × 151 采样、格宽 2、边界 6），平地 +1 m；x 140～160 一条水沟（地面 −1、水位 0，水深 1 m），沟两岸 6 m 沙地，z 140～148 一条东西向公路（`Road`，过沟处是浅滩：那一段地面 −0.5）；水沟以东 x 200 处一排 10 棵树（间距 7 m）；(250, 100) 一栋 12 × 7 × 7.5 的房子；出生点队 0 在 (40, 144)、(40, 160)，队 1 在 (260, 144)、(260, 160)；各自为战 4 个；路点 4 个。
- 预测和服务器的已知差别（阶段 4 设计 §7.4）：撞塌房子（和 4A 以来重坦撞穿砖墙一样）后，客户端要等物件状态到了才按废墟减速，可能有一两次厘米级的修正，所以“零修正”只要求开过水、沙、路和撞倒树的阶段。

**先写的测试**（`RiverlandMatchTests`）

1. `A_riverland_match_start_is_under_one_MiB_and_round_trips`：`Write(w, new MatchStartMessage(…, MapCatalog.Riverland.Data, …))` < 1 048 576 字节（记下实际大小）；读回的高度、物件、地面、水位与 `Data` 相同。
2. `Every_mode_starts_and_runs_on_riverland`（Theory，三种模式）：`Map = MapCatalog.Riverland`、2 个客户端 + 3 个机器人：开局，跑 10 秒：两端都 `InMatch`、有车；服务器日志没有 `error`；开局那一 tick 每辆车都在 `RiverlandMap.Spawns` 的某个点 20 m 内。
3. `Driving_through_water_sand_road_and_trees_is_never_corrected`：`Mixed`、完美链路、两个客户端：A 从西边出生点沿公路开过浅滩再开进那排树（脚本：油门全开，转向让它沿 z 144 走），B 停着；15 秒后再静止 1 秒：A 的 `Predictor.Corrections == 0`，位置与服务器相同（同 `On_a_perfect_link_the_prediction_is_never_corrected` 的比法）；树至少倒了 2 棵。
4. `After_ramming_trees_and_a_house_both_sides_agree_on_every_prop`：接着让 A 以 > 20 km/h 撞房子（中坦）：结束后服务器和两个客户端 `Field` 上每个物件的耐久（按 `QuantizeDurability` 比）相同，房子倒了；A 的修正次数 ≤ 3、最大 < 0.5 m。
5. `Clients_see_the_same_ground_and_water_as_the_server`：`Mixed` 开局后，200 个随机点上客户端 `Field.SurfaceAt`、`WaterDepthAt`（±0.005）、`SpeedFactorAt` 与服务器相同。
6. `A_late_joiner_gets_the_same_riverland`：团队对战在河谷上开局 3 秒后加入第三个客户端：它的 `Field` 的高度数组和物件与服务器的 `MapData.Of(…)` 相同。

**命令**：`dotnet test tests/Tank.Net.Tests --filter "FullyQualifiedName~RiverlandMatch" 2>&1 | grep -E '通过!|失败!'`（第一次会生成河谷，Debug 下几秒）

**验收**：全部测试通过、0 警告；Soak PASS；开局消息大小记进度；Net 至少 +6（约 209）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`test(net): matches on riverland and a small mixed map — start under 1 MiB, ground and props agree, no corrections through water and trees`，末行 `Tests: Net 203 → 209.`

**批次 C 收尾**：两路审查（规格审查对照河谷设计 §5、阶段 5 设计 §2 的协议部分）→ 跟进提交 → 进度第 3 行（开局消息大小、Soak 数字）。

## 批次 D：客户端地面和水

### Task 15：`--map`、站点、试驾场接河谷

**Files:**
- Create: `src/Tank.Client/scripts/Sandbox/Logic/MapSites.cs`、`src/Tank.Client/scripts/UI/Logic/MapText.cs`
- Modify: `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（文件头第 9～21 行的说明加 `--map`；第 50～79 行之间加 `Map` 属性；第 99～130 行的 switch 加 `"--map"`；加 `ParseMap`）、`src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（第 71 行标题、第 73 行自动化、第 76 行建仿真、第 78 行出生点、第 82 行靶车、第 103 行 `HasPreset`、第 250～255 行机位）、`src/Tank.Client/scripts/Sandbox/TargetRange.cs`（第 56 行 `PatrolIndex` 改实例属性；第 64～76 行 `Build`；第 117～125 行 `FillInputs`；第 137～148 行 `ReviveDue`）、`src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs`（构造加站点、第 38 行、第 51～59 行）、`src/Tank.Client/scripts/Sandbox/RangeDemo.cs`（第 33～35 行加 `Spots`、`AimAt`；第 126～135 行 `Command`；第 137 行 `Spot`；第 143 行 `Bearing`）、`src/Tank.Client/scripts/Cameras/CameraPresets.cs`、`src/Tank.Client/scripts/World/BattleWorld.cs`（`UseMapView`）、`src/Tank.Client/scripts/World/TreeLine.cs`（`SetFarView`）
- Test: `tests/Tank.Art.Tests/LaunchOptionsMapTests.cs`、`MapSitesTests.cs`、`MapTextTests.cs`

**接口**：共享接口“客户端”的 `LaunchOptions.Map`、`MapSite`/`MapSites`/`CameraAnchor`、`MapText`、`RangeDemo` 的两个新参数、`CameraPresets` 的新重载、`BattleWorld.UseMapView`。`MapSite.PresetSpawns` 是某个机位把玩家放在哪（不在表里的用 `Spawn`）。

**规则/要点**（河谷设计 §6.1、§6.8，阶段 5 设计 §2）

- `--map`：`MapCatalog.TryGet` 认得就存规范 id（`"RIVERLAND"` → `"riverland"`），不认得警告 `Bad --map 'x'; use sandbox|riverland. Using the default map.` 并保持 null。试驾场用 `MapSites.For(options.Map)`（null → 试驾场地图）；开房的默认在任务 16。
- 站点：
  - `MapSites.Sandbox`：`MapCatalog.Sandbox`、标题 `坦克 3D · 试驾场`（现在的标题，不变）、`SandboxMap.Spawn`/`SpawnYaw`/`TargetSpots`、有巡逻车、`PresetSpawns` = { bush → `SandboxMap.BushPatches[0]`, patrol → `SandboxMap.PatrolWatch` }、机位名单是现在 `CameraPresets.Names` 的 13 个、`SpinEye` (200, 200)、`MapCentre` (200, 200)、`MapSize` 400、演示 `RangeDemo.Names`/`RangeDemo.Find`。试驾场的机位继续走 `CameraPresets` 里现有的常数（截图逐位不变），`Anchors` 为空表。
  - `MapSites.Riverland`：`MapCatalog.Riverland`、标题 `坦克 3D · 试驾场 · 河谷`、`RiverlandMap.Spawn`/`SpawnYaw`/`TargetSpots`、没有巡逻车、`PresetSpawns` = { ford → 小镇浅滩西岸公路上离水边 15 m、车头朝东 }、机位名单 `front side close closerear sprocket spin orbit target overview town ford forest plateau dunes map`、`SpinEye` = `MapCentre` = (600, 600)、`MapSize` 1200、演示暂时为空（任务 24 填）。
  - 河谷锚点的初值（实现时按截图微调，测试检查在图内、在地面和水面之上）：`overview` 眼 (560, 720) 高 120、焦点 (790, 560) 高 0；`town` 眼 (640, 610) 高 25、焦点 (705, 560) 高 3；`ford` 眼 (672, 592) 高 9、焦点 (700, 560) 高 0；`forest` 眼 (850, 640) 高 8、焦点 (905, 600) 高 4；`plateau` 眼 (330, 400) 高 40、焦点 (240, 280) 高 22；`dunes` 眼 (900, 1000) 高 25、焦点 (1020, 1060) 高 2；`map` 眼 (600, 600) 高 700、焦点 (600, 600) 高 0。
- `CameraPresets`：现有分支不动；加 `site` 参数后，`site` 是河谷时 `overview…map` 走锚点（高度 = 那一点的 `max(地面, 水位)` + 锚点高度），`spin` 用 `site.SpinEye`，`target` 用 `site.Targets[targetIndex]`；`Names` 是所有站点名单的并集（`IsKnown` 语义变成“某张图上有”）；`StreamsAroundCamera` 加 `overview town ford forest plateau dunes map`；`IsOrthographic("map")` 真；`UpFor("map")` 是 `Vector3.Forward`（屏幕上方是北）。
- `SandboxMain`：`_site = MapSites.For(_options.Map)`；标题 `_site.Title`；`_sim = new Simulation(_site.Map.NewBattlefield())`；出生点 `demo?.PlayerPosition ?? CameraPresets.Spawn(preset, _site)`、朝向 `demo?.PlayerYaw ?? _site.SpawnYaw`；`HasPreset => CameraPresets.IsKnownFor(preset, _site)`；`LookAtFromPosition(eye, target, CameraPresets.UpFor(preset))`；`map` 机位第一次用时把相机设成正交（`Projection = Orthogonal`、`Size = _site.MapSize + 40`）并调 `_world.UseMapView()`。
- `TargetRange.Build(Simulation sim, VisionTracker vision, bool parkPatrol, MapSite site)`：靶车取 `site.Targets`；`site.HasPatrol` 为假时不建巡逻车，`PatrolIndex` 为 −1，`FillInputs` 和 `ReviveDue` 跳过巡逻那几行。
- `SandboxAutomation(LaunchOptions options, Node host, bool standStill, MapSite site)`：`Demo = site.FindDemo(options.RangeDemo)`；警告按站点写：`Unknown --camera-preset 'yard' on 河谷; use front, side, …`、`Unknown --range-demo 'pen' on 河谷; use …`（没有演示时写 `河谷 has no range demos yet`）。
- `RangeDemo`：`Spot` 取 `Spots ?? SandboxMap.TargetSpots`；`AimAt` 给了时 `Command` 把炮瞄在这一点、`Bearing` 朝它；试驾场的 14 个演示不变（`RangeDemoTests` 不改照过）。
- `BattleWorld.UseMapView()`：关雾（`Build` 里留住 `Scenery.CreateEnvironment()` 返回的 `WorldEnvironment`），`TreeLine.SetFarView(true)`（去掉各扇区的 `VisibilityRangeEnd`），之后建的 `ForestViews` 也照做（任务 21）。
- `MapText`：见共享接口。

**先写的测试**

- `LaunchOptionsMapTests`：
  1. `Map_takes_a_catalog_id`：`--map riverland` → `"riverland"`；`--map RiverLand` → `"riverland"`；`--map sandbox` → `"sandbox"`；不给 → null。
  2. `An_unknown_map_warns_and_keeps_the_default`：`--map moon` → null，警告原文 `Bad --map 'moon'; use sandbox|riverland. Using the default map.`
  3. `A_map_without_a_value_is_ignored_with_the_usual_warning`：`--map --sandbox` → null，警告 `--map needs a value but is followed by --sandbox; --map is ignored.`，`Sandbox` 照样为真。
- `MapSitesTests`：
  1. `Unknown_or_missing_ids_get_the_sandbox`：`For(null)`、`For("")`、`For("moon")` 都是 `Sandbox`；`For("RIVERLAND")` 是 `Riverland`。
  2. `The_sandbox_site_is_todays_test_drive`：出生 (200, 330)、朝向 0、靶车就是 `SandboxMap.TargetSpots`、有巡逻车、标题 `坦克 3D · 试驾场`、机位名单等于旧的 13 个、演示名单等于 `RangeDemo.Names`。
  3. `The_riverland_site_has_its_own_spots_and_no_patrol`：出生和靶车来自 `RiverlandMap`、没有巡逻车、标题 `坦克 3D · 试驾场 · 河谷`；机位名单含 `overview town ford forest plateau dunes map orbit spin target`，不含 `yard gate patrol bush hill`。
  4. `Riverland_anchors_are_on_the_map_and_above_ground_and_water`：每个锚点的眼和焦点在 [0, 1200] 内；眼的高度（地面或水面以上）≥ 2 m；`map` 的眼 ≥ 500 m。（用 `MapCatalog.Riverland.NewBattlefield()`。）
  5. `The_ford_preset_starts_on_the_road_beside_the_water`：`PresetSpawns["ford"]` 上 `SurfaceAt == Road`、水深 0，20 m 内有水深 > 0.3 的点。
- `MapTextTests`：
  1. `Map_names_come_from_the_catalog`：`Name("sandbox") == "试驾场地图"`、`Name("riverland") == "河谷"`、`Name("moon") == "moon"`、`Name(null) == Name("") == "—"`。
  2. `Test_drive_titles`：`SandboxTitle("sandbox") == "坦克 3D · 试驾场"`、`SandboxTitle("riverland") == "坦克 3D · 试驾场 · 河谷"`。
  3. `Match_titles`：`MatchTitle("riverland") == "坦克 3D · 河谷"`、`MatchTitle("sandbox") == "坦克 3D · 试驾场地图"`、`MatchTitle(null) == "坦克 3D · 对局"`。
- `RangeDemoTests` 旁边的新文件里（`MapSitesTests` 内）：
  4. `A_demo_with_spots_and_an_aim_point_uses_them`：`new RangeDemo(1, 0, 0, DemoAim.HullFront, AmmoType.HE, true, Stand: p, Spots: RiverlandMap.TargetSpots, AimAt: q)`：`Spot == RiverlandMap.TargetSpots[1]`，`Bearing == YawOf(q.xz − p)`，`Command(...)` 的瞄准角指向 `q`。

**命令**

```bash
dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~LaunchOptionsMap|FullyQualifiedName~MapSites|FullyQualifiedName~MapText" 2>&1 | grep -E '通过!|失败!'
dotnet build src/Tank.Client/Tank.Client.csproj && godot-mono --headless --path src/Tank.Client --import
mkdir -p build/shots/5a-t15
for p in map overview town ford forest plateau dunes spin; do
  $S/godot_run.sh $W res://scenes/Sandbox.tscn 90 --map riverland --camera-preset $p \
    --screenshot $W/build/shots/5a-t15/$p.png --screenshot-at 8 --quit-after 10; done
$S/godot_run.sh $W res://scenes/Sandbox.tscn 90 --map riverland --screenshot $W/build/shots/5a-t15/spawn.png --screenshot-at 8 --quit-after 10
$S/godot_run.sh $W res://scenes/Sandbox.tscn 60 --map riverland --camera-preset yard --quit-after 4   # 应有 “Unknown --camera-preset 'yard' on 河谷” 警告
$S/baseline4b.sh $W build/shots/5a-t15-sandbox
```

**验收**

- [ ] 全部测试通过、0 警告；Art 882 → 至少 894。
- [ ] 截图逐张看过：`map` 是整张图的正上方正交俯视（北在上，看得出河、湖、台地、公路的走向，地面此时还是旧的配色）；`spawn` 里玩家在公路上朝东，能看到 8 辆靶车里在视野内的几辆；其余机位对准各自的地方（树和房子还看不见，是正常的）。窗口标题的文字由 `MapTextTests` 固定，`SandboxMain` 只是设它。
- [ ] 试驾场六张回归截图与基线一致；`--range-demo` 和 `--camera-preset yard` 在河谷上给出警告。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): --map and per-map sites — the test drive opens on riverland with its targets, cameras and a top-down map view`，末行 `Tests: Art 882 → 894.`

### Task 16：房间里选图、局域网列表的地图列、窗口标题、开房默认河谷

**Files:**
- Modify: `src/Tank.Client/scripts/Online/Logic/HostSettings.cs`（第 9 行加 `string Map = SandboxMap.Id`）、`Online/Logic/ThreadedHost.cs`（第 34～43 行加 `Map = MapCatalog.Get(settings.Map)`、`PrepareMapsInBackground = true`）、`Online/Logic/LoopbackHost.cs`（第 24～33 行加 `Map`，前台）、`Bootstrap/Main.Flow.cs`（第 20 行传 `_options.Map ?? MapCatalog.Riverland.Id`）、`UI/Logic/RoomModel.cs`（第 33～62 行加属性；第 67～95 行 `Build`；第 151～156 行 `Signature`）、`UI/Logic/RoomRules.cs`（第 23～31 行）、`UI/RoomScreen.cs`（第 29～30 行旁加 `_map`；第 127～130 行 `Apply`；第 156～164 行 `BuildSide`）、`Online/Logic/LanRoomRow.cs`（第 17 行列宽、第 25～27 行文字）、`Online/Logic/AutoRoom.cs`（第 108～115 行 `ReadyToStart`）、`Match/MatchMain.cs`（删第 33 行的 `Title` 常数；第 87～91 行 `Setup` 记下站点；第 98 行标题）、`Match/MatchMain.Camera.cs`（第 120 行 `CameraPresets.Get` 带站点）、`Bootstrap/Main.Flow.cs` 第 108 行的日志（`[match] entered match {模式} on {地图} as slot {座位}`，前缀不变，`tools/ship.sh` 的冒烟测试照样匹配）、`Match/MatchAutomation.cs`（第 34～41 行构造加可选 `MapSite? site = null`，`HasPreset = IsKnownFor(preset, site ?? MapSites.Sandbox)`；第 64～65 行警告按站点）
- Test: `tests/Tank.Art.Tests/RoomMapModelTests.cs`

**规则/要点**（阶段 5 设计 §2）

- 开房（菜单“开房间”和 `--host`）的地图：`--map` 给了就用它，否则河谷。`HostSettings` 的默认值是试驾场地图，只为了现有测试（Art 测试里的房主不必生成河谷）。
- 房间界面：模式下面多一行“地图（房主）”，下拉项是 `MapCatalog.All` 的显示名（试驾场地图 / 河谷）；选中项跟着房间状态；房主在房间里（不在对局中）能改，改了发 `SetMap`。标题改为 `模式 · 地图`（如 `团队对战 · 河谷`）。地图没就绪时“开始对战”按钮不可用，下面写 `地图生成中…`。
- `RoomModel` 新增：`string MapId`、`string MapName`、`bool CanSetMap`（房主且不在对局中）；`Signature` 加上地图和就绪。`RoomRules.StartBlockReason`：对局进行中之后，第一条检查 `!room.MapReady` → `"地图生成中…"`。
- **C 批跟进后**：房间状态的 `MapReady`（bool）已换成 `MapState`（`Generating`/`Ready`/`Failed`），下文的 `MapReady` 都按 `MapState == Ready` 读；`RoomRules.StartBlockReason` 要认两种原因（对应服务器的 `"the map is still being generated"` 和 `"the map failed to generate"`）：`Generating` → `"地图生成中…"`，`Failed` → 如 `"地图生成失败，换一张图。"`，交叉测试（第 5 条）两种都对上服务器。
- 局域网列表：在模式后面加一列地图（宽 8 列），`MapText.Name(info.Map)`；v2 的房间这一列是 `—`，仍显示“版本不符”。
- `AutoRoom.ReadyToStart`：`room.MapReady` 为假时不开局（返回 Wait；地图就绪后照常开），否则 `--autostart` 会在生成中发出被拒的开局、之后再也不发。
- 对局窗口标题：`MatchMain._Ready` 时房间状态已知就设 `MapText.MatchTitle(_client.Room?.Map)`；未知（中途加入时开局消息先于房间状态到）就在之后第一个 `_client.Room` 不为空的帧设一次。离开对局时 `Main.Flow.LeaveMatch` 照旧改回 `坦克 3D`。
- 对局的机位：`MatchMain.Setup` 时 `_site = MapSites.For(session.Client.Room?.Map)`（未知时试驾场站点，只影响脚本截图用的固定机位）。

**先写的测试**（`RoomMapModelTests`）

1. `The_host_sees_the_map_and_may_change_it_in_the_room`：房间状态 `Map "riverland"`、房主视角：`MapName == "河谷"`、`CanSetMap`、`Title == "团队对战 · 河谷"`。
2. `Guests_and_running_matches_may_not_change_the_map`：非房主 `CanSetMap` 假；对局中的房主也假。
3. `A_map_still_generating_blocks_the_start`：`MapReady: false`：`RoomRules.StartBlockReason == "地图生成中…"`，房主的 `CanStart` 假、`StartHint == "地图生成中…"`；对局中时仍是“对局进行中，等这一局结束。”优先。
4. `The_signature_follows_the_map_and_its_readiness`：只改地图或只改 `MapReady`，`Signature` 都变。
5. `The_start_rule_matches_the_server_while_a_map_generates`：真服务器（回环网络，`Maps = [MapCatalog.Sandbox, 等放行的图]`、后台准备；等放行的图在测试里自己建：`new MapDefinition("gated", "gated", () => { gate.Wait(); return SandboxMap.Create(); }, SandboxMap.Spawns, SandboxMap.Waypoints)`），房主选 `Gated`：预测不能开局、原因 `地图生成中…`，房主发开局后服务器确实没开；放行、等房间状态 `MapReady` 后，预测能开、服务器真开了。
6. `The_lan_row_shows_the_map`：v3 房间 `Map "riverland"` → 一行里有 `河谷`；v2 房间 → 有 `—` 和 `版本不符`。
7. `Auto_room_waits_for_the_map_before_starting`：`AutoRoom(null, null, null, autoStart: true)`，房主、1 人：`MapReady` 假时 `Next` 返回 `Wait`，真时返回 `StartMatch`。
8. `Host_settings_default_to_the_sandbox_map`：`new HostSettings("A", "A的房间", 0, BattleMode.TeamBattle, 0, null, null).Map == "sandbox"`。

**命令**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | grep -E '通过!|失败!'
mkdir -p build/shots/5a-t16
$S/godot_run.sh $W "" 60 --host --name 甲 --screenshot $W/build/shots/5a-t16/room-riverland.png --screenshot-at 1 --quit-after 2      # 刚开房：可能拍到“地图生成中…”
$S/godot_run.sh $W "" 60 --host --name 甲 --screenshot $W/build/shots/5a-t16/room.png --screenshot-at 8 --quit-after 10
$S/godot_run.sh $W "" 60 --host --name 甲 --map sandbox --mode ffa --screenshot $W/build/shots/5a-t16/room-sandbox.png --screenshot-at 6 --quit-after 8
$S/godot_run.sh $W "" 120 --host --map riverland --mode ffa --bots 3 --autostart --screenshot $W/build/shots/5a-t16/match.png --screenshot-at 20 --quit-after 30
$S/pair4b.sh $W $W/build/shots/5a-t16 lan 40 "--host --name 甲 --map riverland" "--name 乙 --screenshot $W/build/shots/5a-t16/lan.png --screenshot-at 12 --quit-after 15" 3
```

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +8（约 902）。
- [ ] 截图看过：房间标题 `团队对战 · 河谷`、地图下拉显示“河谷”；`room-sandbox` 显示“各自为战 · 试驾场地图”；`match` 是河谷上的对局（地形和试驾场明显不同；日志里 `[server] match started: ffa, map riverland`）；`lan` 是第二个实例停在菜单、列表里那一行有“河谷”。
- [ ] `match` 那次的日志有 `[match] entered match FreeForAll on riverland`；对局窗口标题的文字由 `MapTextTests` 固定。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): pick the map in the room — rooms open on riverland, the LAN list and the match window show the map`，末行 `Tests: Art 894 → 902.`

### Task 17：地面着色、长草规则、图外树带；对局世界用场地的地面

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/GroundLayers.cs`
- Modify: `src/Tank.Client/scripts/World/DirtMask.cs`（`FromField`、`IsLayered`、RGBA 贴图）、`World/TerrainMesh.cs`（`Build` 加 `float? waterLevel = null`）、`shaders/terrain_toon.gdshader`、`scripts/Art/Palette.cs`（`Sand`、`Road`、`Riverbed`、`WetSand`、`Earth`）、`scripts/World/GrassField.cs`（`CollectNearbyRocks` 第 226～236 行改查桶）、`scripts/World/TreeLine.cs`（第 44～50 行之后跳过水和公路）、`scripts/World/BattleWorld.cs`（第 43～44 行）
- Test: `tests/Tank.Art.Tests/GroundLayersTests.cs`；扩充 `TerrainShaderParityTests.cs`

**接口**

```csharp
namespace Tank.Client.Art.Meshes;
public sealed class GroundLayers   // engine-free; one texel per surface cell
{
    public static bool Needed(SurfaceMap surface, float? waterLevel);   // water, or any cell that isn't grass
    public GroundLayers(SurfaceMap surface);
    public int Width { get; }   public int Height { get; }             // = CellsX, CellsZ
    public float SizeX { get; } public float SizeZ { get; }
    public byte[] ToRgba();      // R: not grass (dirt, sand, road) dilated by one cell, then blurred; G: sand, B: road, A: 0 — each 3×3 box-blurred, edges clamped; row j = Z
    public float NotGrassAt(float x, float z);   // bilinear on the blurred R (texel centres at (i + 0.5)·cell), 0..1: GroundCover's dirt-mask value
}
```

**规则/要点**（河谷设计 §6.2）

- 着色器只用一张贴图（`dirt_mask`，层叠模式下是 RGBA）：`dirt_patch` 那一行照旧读 `.r`，所以草的规则（`GroundCover.GrassWeight`）不用改：非草地格子 R ≈ 1，草的权重压到 0.5 以下，草叶自然不长在土、沙、公路和水里（水下的格子都是沙或土）。新增 `uniform bool layered = false;`、`uniform float water_level = -1000.0;` 和四个颜色（带 `// Palette.X` 标注，`PaletteTests` 自动核对）。层叠模式：泥地层的颜色改为 `mix(earth, sand, smoothstep(0.3, 0.7, g))`，再 `mix(…, road, smoothstep(0.35, 0.65, b))`（公路边缘不会有 2 m 锯齿），水下（`world_pos.y < water_level`）按 `smoothstep(water_level, water_level - 0.4, y)` 混到 `riverbed`，水边 0.25 m 内混一条 `wet_sand`。非层叠模式（试驾场）这些都不起作用，画面逐位不变。
- 颜色初值（截图后可调）：`Sand` E6D3A0、`Road` 9A948C、`Riverbed` B49A6E、`WetSand` CDB585、`Earth` A88B65。
- `DirtMask.FromField(field)`：`GroundLayers.Needed(field.Surface, field.WaterLevel)` 为真时返回层叠的遮罩，否则 null；`BattleWorld.Build` 用 `DirtMask.FromField(field) ?? new DirtMask(…style…)`，于是对局（`MatchMain` 仍传 `MapStyle.Sandbox`）拿到河谷数据时自动用地面图，试驾场地图仍用草岛。
- `GrassField`：第一次填格子时把 `IsSolid` 的物件（加宽 `RockMargin`）按 16 m 的草格子分桶（外接圆的方框覆盖的格子都放），之后 `CollectNearbyRocks` 只取那一格的桶；结果和全扫描相同（试驾场草的截图不变）。
- `TreeLine`：在地图内的候选点，水深 > 0 或是公路就跳过；判断放在所有随机数都取完之后（试驾场的随机序列不变，树带逐位不变）。
- 远景地面 `FarGroundSize` 4000 m 以地图中心为中心，已经盖得住 1200 m 的图，不改。

**先写的测试**

- `GroundLayersTests`：
  1. `Layers_are_needed_for_water_or_any_non_grass_cell`：全草无水假；有水真；一格公路真。
  2. `A_single_road_cell_blurs_into_its_neighbours`：5 × 5 草地中间一格公路：那格和它 8 个邻格的 B、R 都是 28（255 / 9 取整），其余 0；G 全 0。
  3. `Uniform_sand_stays_full_to_the_edges`：全沙：R、G 处处 255，B 0。
  4. `Not_grass_reads_like_the_gpu`：10 × 10 土块中间 `NotGrassAt` 是 1；远处草地 0；两个纹素中心的正中间等于两者平均（按模糊后的值手算）。
  5. `Bytes_are_row_major_rgba`：长度 `Width × Height × 4`，第 (i, j) 格在 `(j × Width + i) × 4`。
  6. `Grass_stops_where_the_ground_is_not_grass`：层叠遮罩下，平地（法线 Y = 1）公路格子中心的 `GroundCover.GrassWeight` < 0.5，离非草格子 6 m 以上的草地格子 > 0.5。
- `TerrainShaderParityTests.Layered_ground_keeps_the_grass_rule`：着色器里仍有原来七条（不改），并且有 `uniform bool layered`、`uniform float water_level`、`smoothstep(0.3, 0.7,`、`smoothstep(0.35, 0.65,`，`dirt_patch` 仍由 `texture(dirt_mask, world_pos.xz / map_size).r` 得到。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~GroundLayers|FullyQualifiedName~TerrainShaderParity|FullyQualifiedName~Palette" 2>&1 | grep -E '通过!|失败!'`；截图：`--map riverland` 分别配 `--camera-preset map`、`overview`、`ford`、`plateau`、`dunes` 和不带机位（出生点），`--screenshot-at 8 --quit-after 10`，放到 `build/shots/5a-t17/`；`$S/baseline4b.sh $W build/shots/5a-t17-sandbox`。

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +7（约 909）。
- [ ] 截图看过：`map` 上公路是灰色的带子、边缘不锯齿，河岸、湖滩、沙丘是沙色，树林地面是土色，河床（水面还没画）是深一点的湿沙色、水边有湿沙带；草叶只在草地上；图外树带不站在河道出口的水里。
- [ ] 试驾场六张回归截图与基线一致（草、树带、地面都不变）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): ground layers — sand, roads, riverbed and wet banks in the terrain shader; grass only on grass; the match world uses the field's ground`，末行 `Tests: Art 902 → 909.`

### Task 18：水面、镜头不入水、自由镜头焦点

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/WaterMesh.cs`、`scripts/World/WaterSurface.cs`、`shaders/water.gdshader`、`shaders/dither.gdshaderinc`
- Modify: `shaders/bush.gdshader`（第 30～36 行的注释和 `bayer4` 挪到 `dither.gdshaderinc`，改为 `#include`）、`scripts/Art/Palette.cs`（`WaterShallow`、`WaterDeep`、`Foam`）、`scripts/World/BattleWorld.cs`（第 53 行后建 `WaterSurface`）、`scripts/Cameras/OrbitCamera.cs`（第 82～97 行 `Follow`）、`scripts/Sandbox/SandboxMain.cs`（第 250 行传水位）、`scripts/Match/MatchMain.Camera.cs`（第 117、151、160 行传水位；第 159 行焦点高度）
- Test: `tests/Tank.Art.Tests/WaterMeshTests.cs`

**接口**

```csharp
namespace Tank.Client.Art.Meshes;
public static class WaterMesh
{
    /// Cells firstI..firstI+cellsX × firstJ..firstJ+cellsZ that have a corner below level, as a flat sheet at level:
    /// vertex (i, j) at (i·cell, level, j·cell), normal up, UV = (level − height(i, j), 0) — signed, negative on dry corners —
    /// triangles split along the same diagonals as GridMesh. MeshData.Empty when no cell is wet.
    public static MeshData Build(int firstI, int firstJ, int cellsX, int cellsZ, float cellSize, Func<int, int, float> height, float level);
}
```

**规则/要点**（河谷设计 §6.3）

- `WaterSurface.Build(field)`：总是加入分组；有水时按 32 × 32 格分块（和 `TerrainMesh` 一样），高度用 `TerrainMesh.VisualHeightAt`（和画出来的地形同一个高度），没水的块不建；不投影（`CastShadow = Off`）。`DepthAt` 用仿真地形 `field.Terrain.HeightAt`。
- 对角线和地形一致、深度是有符号的线性插值，所以深度 0 的线正好落在画出来的岸线上，不需要深度贴图。
- `water.gdshader`：不透明（项目不排序半透明）；`depth = UV.x`：`depth < 0` 丢弃；浅色到深色按 `smoothstep(0.0, 1.4, depth)`；岸边一条随 `TIME` 轻微浮动的白色泡沫带（`depth < 0.12 + 0.04 sin(…)`）；少量平涂色块波纹（`value_noise2` 随时间平移）；最浅的 0.06 m 用 `bayer4` 抖动成半透明的边。颜色 uniform 带 `// Palette.WaterShallow` 等标注。初值：`WaterShallow` 7FD6D8、`WaterDeep` 3E9CC4、`Foam` F4FBFF。
- `dither.gdshaderinc` 只放 `bayer4`，`bush.gdshader` 改成 include 后逐字节相同的算法（草丛截图不变）。
- 镜头：`Follow` 的地面下限取 `max(地面, 水位) + GroundClearance`；对局自由镜头的焦点高度取地形和水位中较高的那个（阶段 5 设计 §2）。

**先写的测试**（`WaterMeshTests`）

1. `Only_cells_with_a_corner_under_water_are_built`：5 × 5 采样（4 × 4 格）、中间采样 −1、其余 +1、水位 0：只有围着中心的 4 格出三角形（8 个），所有顶点 y = 0。
2. `Depth_is_the_signed_level_above_each_corner`：每个顶点 `UV.X == 0 − height(i, j)`（中心 1，四周 −1）。
3. `Diagonals_match_the_terrain`：同一格的两个三角形用的顶点和 `GridMesh.Build` 的那一格相同（按位置比）。
4. `The_shoreline_is_where_the_terrain_meets_the_level`：每条两端深度异号的三角形边，深度线性插值为 0 的点与地形高度线性插值等于水位的点重合（±1e-5）。
5. `A_dry_chunk_is_empty_and_a_wet_one_full`：全干 → `MeshData.Empty`；全湿 → 每格 2 个三角形。
6. `Chunks_line_up_in_world_space`：`firstI = 32, firstJ = 64` 的块，顶点 x、z 是全局下标 × 格宽，法线都朝上。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~WaterMesh|FullyQualifiedName~Palette" 2>&1 | grep -E '通过!|失败!'`；截图 `map overview ford`（湖看 `map` 和 `overview`）放到 `build/shots/5a-t18/`；`--map riverland --camera-preset ford --autodrive --screenshot-at 8` 一张（车开进水里，固定机位）；跟车镜头贴着水面的情形在任务 24 的 `ford` 演示里检查（跟车、车在河中间）；`$S/baseline4b.sh $W build/shots/5a-t18-sandbox`（草丛用了新 include，要逐张比）。

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +6（约 915）。
- [ ] 截图看过：河、湖、浅滩有水，岸线贴着地形，浅处浅色、深处深色，岸边有泡沫带，边缘没有缝也没有水漫到岸上；俯视图上水只在河和湖里。
- [ ] 试驾场六张回归截图与基线一致（没有水，草丛不变）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): cartoon water sheets on wet cells only — the shore lands on the drawn terrain, cameras stay above water`，末行 `Tests: Art 909 → 915.`

### Task 19：涉水水花、落水水柱

**Files:**
- Create: `src/Tank.Client/scripts/Combat/Logic/SplashRules.cs`
- Modify: `src/Tank.Client/scripts/Combat/DustTrail.cs`（第 22～58 行加一对水花发射器；第 60～77 行 `Update` 查水深）、`scripts/Combat/BattleFx.cs`（新的水柱槽位，`Explosion` 第 73 行先查水面）
- Test: `tests/Tank.Art.Tests/SplashRulesTests.cs`

**接口**

```csharp
namespace Tank.Client.Combat;
public static class SplashRules   // engine-free
{
    public const float WadeDepth = 0.05f;        // deeper than this under the hull centre: spray instead of dust
    public const float WadeOutDepth = 0.03f;     // a wading tank goes back to dust only at this depth or less (hysteresis)
    public const float BurstAboveWater = 0.5f;   // a burst at most this high over the water, on ground under water, raises a column
    public static bool Wading(float depth, bool wasWading);   // depth > (wasWading ? WadeOutDepth : WadeDepth)
    public static bool IsWaterBurst(float depthUnderBurst, float burstY, float? waterLevel);
    public static float SprayStrength(float speed);   // 0 below 3 km/h, rising to 1 at 45 km/h
}
```

**规则/要点**（河谷设计 §6.6）

- 特效自己判断：`DustTrail` 和 `BattleFx` 第一次用到时 `WaterSurface.Find(this)` 并缓存（试驾场地图上它存在但没有水，`DepthAt` 恒 0）；调用它们的地方一行不改，所以试驾场、对局里的所有车都自动有水花。
- 涉水：车体中心处水深 > `WadeDepth` 时，尘土发射器停，水花发射器开：车两侧白色泡沫和水滴（`Palette.Foam`，往两侧和上方溅），数量随 `SprayStrength(速度)`；出水后切回尘土。
- 落水：爆点下的地面在水下、爆点不高于水面 0.5 m → 不放尘土和火球，在水面高度起一道白色水柱（几团竖着拉长的泡沫球，1.2 秒）和一圈向外扩的水环（贴水面的一圈小泡沫）。新池子 `SplashSlots = 10`。

**先写的测试**（`SplashRulesTests`）

1. `Wading_starts_just_under_the_hull`：`Wading(0.04f)` 假、`Wading(0.06f)` 真。
2. `A_burst_on_flooded_ground_near_the_surface_raises_a_column`：水深 0.8、爆点在水面上 0.3 → 真；0.6 → 假。
3. `Dry_ground_or_no_water_never_splashes`：水深 0 → 假；没有水位 → 假。
4. `A_burst_under_the_surface_still_splashes`：爆点在水面下 0.4（打在河底）→ 真。
5. `Spray_grows_with_speed`：3 km/h 以下 0，45 km/h 及以上 1，24 km/h 在 0.4～0.6。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~SplashRules" 2>&1 | grep -E '通过!|失败!'`；截图：`$S/godot_run.sh $W res://scenes/Sandbox.tscn 90 --map riverland --camera-preset ford --autodrive --screenshot $W/build/shots/5a-t19/wade.png --screenshot-at 6 --quit-after 9`（`ford` 机位的出生点在浅滩西岸，自动驾驶开下水）；`--fx-shot 3 --quit-after 16` 同机位截落水水柱（炮口压低，炮弹落在前方约 40 m，即浅滩东半边的水里）；回归六张。

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +5（约 920）。
- [ ] 截图看过：车在水里时两侧有白色水花、没有土色尘土；炮弹落水处是白色水柱和水环，不是土色烟。试驾场回归截图一致（`kill` 演示的爆炸、尘土照旧）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): spray when wading and water columns for shells landing in water — the effects look the water up themselves`，末行 `Tests: Art 915 → 920.`

### Task 20：地面提示

**Files:**
- Create: `src/Tank.Client/scripts/Overlay/Logic/GroundText.cs`
- Modify: `src/Tank.Client/scripts/Overlay/StatusPanel.cs`（`SetGround`；`_Draw` 第 117～121 行在车名上方画一行）、`scripts/Overlay/CombatHud.cs`（`Refresh` 第 119～134 行里算）
- Test: `tests/Tank.Art.Tests/GroundTextTests.cs`

**接口**

```csharp
namespace Tank.Client.Overlay;
public static class GroundText   // engine-free
{
    /// "公路 ×1.15", "沙地 ×0.80", "水深 0.8 m ×0.73"; null when the factor is 1 (within 0.005). Numbers invariant-culture;
    /// a depth under 0.1 m shows as 0.1. The factor is the simulation's (Battlefield.SpeedFactorAt), not a client table.
    public static string? Of(SurfaceKind kind, float waterDepth, float factor);
}
```

**规则/要点**（河谷设计 §6.7，阶段 5 设计 §2：放进 `CombatHud`）

- `CombatHud.Refresh` 在 `_aiming` 时按 `player.Position` 取 `_field.SurfaceAt`、`WaterDepthAt`、`SpeedFactorAt`，交给 `_status.SetGround(GroundText.Of(...))`；试驾场（`SandboxHud : CombatHud`）和对局自动都有，阵亡、结算时随状态面板一起隐藏。
- `StatusPanel`：字号 20、`HudStyle.Text` 颜色、描边 4，左对齐在面板左内边距，基线在车名基线上方 30 px；文字变了才重画。

**先写的测试**（`GroundTextTests`）

1. `Roads_and_sand_name_the_ground`：`Of(Road, 0, 1.15f) == "公路 ×1.15"`、`Of(Sand, 0, 0.8f) == "沙地 ×0.80"`。
2. `Water_names_the_depth`：`Of(Sand, 0.8f, 0.7333f) == "水深 0.8 m ×0.73"`、`Of(Dirt, 1.6f, 0.5f) == "水深 1.6 m ×0.50"`。
3. `Very_shallow_water_shows_a_tenth`：`Of(Grass, 0.03f, 0.8f) == "水深 0.1 m ×0.80"`。
4. `Plain_ground_says_nothing`：`Of(Grass, 0, 1f)`、`Of(Dirt, 0, 1f)`、`Of(Grass, 0, 1.004f)` 都是 null。
5. `Numbers_ignore_the_machines_culture`：在 `de-DE` 文化下仍是 `×1.15`（小数点）。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~GroundText" 2>&1 | grep -E '通过!|失败!'`；截图：`--map riverland --screenshot …/road.png --screenshot-at 8`（出生在公路上，显示“公路 ×1.15”）、`--map riverland --camera-preset ford --autodrive --screenshot …/water.png --screenshot-at 6`（显示水深）；回归六张（试驾场全是草地，状态面板不变）。

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +5（约 925）。
- [ ] 截图看过：提示在车名上方、不压住“被点亮”灯和计分板的位置；试驾场没有提示。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): ground hint over the status panel — road, sand and water depth with the simulation's speed factor`，末行 `Tests: Art 920 → 925.`

**批次 D 收尾**：两路审查（规格审查对照河谷设计 §6.1～6.3、§6.6、§6.7 和阶段 5 设计 §2 的房间部分）→ 跟进提交 → 进度第 4 行（截图目录、河谷 `spin` 的帧率先测一次作参考）。

## 批次 E：客户端物件

### Task 21：林中树

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/TreeFall.cs`、`scripts/Art/Mesh/SectorGrid.cs`、`scripts/World/ForestViews.cs`
- Modify: `scripts/Art/Mesh/TreeShapes.cs`（`Conifer`、`Broadleaf` 加可选的树干半径参数；`Shape`、`Measure`；同文件加 `TreeFit`）、`shaders/tree.gdshader`（不透明度、抖动）、`scripts/Combat/PropFx.cs`（第 45～56 行加 `Tree`）、`scripts/World/BattleWorld.cs`（建、同步、事件、`UseMapView`）
- Test: `tests/Tank.Art.Tests/TreeFallTests.cs`、`ForestTreeShapeTests.cs`（含 `SectorGrid` 的用例）

**接口**

```csharp
namespace Tank.Client.Art.Meshes;
public static class TreeShapes   // existing; the defaults keep the tree line exactly as it is
{
    public static MeshData Conifer(uint seed, float trunkRadius = 0.24f);
    public static MeshData Broadleaf(uint seed, float trunkRadius = 0.28f);
    public readonly record struct Shape(float TrunkRadius, float CrownRadius, float Height);   // at scale 1
    public static Shape Measure(MeshData tree, float trunkRadius);   // crown: widest foliage vertex (COLOR.a > 0); height: highest vertex
}
// TreeShapes also has (T21): public const float ConiferTrunk = 0.24f, BroadleafTrunk = 0.28f; (the defaults above)
public static class TreeFit
{
    public const int Variants = 2;                                   // meshes per kind; a tree takes Id % Variants
    public const float ElsewhereShare = 0.4f;
    public readonly record struct ConiferRegion(Vector2 Min, Vector2 Max, float Share);   // batch E review
    public static IReadOnlyList<ConiferRegion> ForestsOf(string? mapId);   // the riverland's five (RiverlandMap.Forests grown by ForestJitter); none on any other map
    public static (MeshData Mesh, TreeShapes.Shape Shape) Variant(bool conifer, int variant);   // the forest mesh, trunk = crown / CrownPerTrunk
    public static Vector3 Scale(in Prop tree, Shape shape);          // (crown / shape crown, height / shape height, same as x); prop height less the 0.3 m sink
    public static float ConiferShare(IReadOnlyList<ConiferRegion> forests, float x, float z);   // the riverland: north and north-east forests 0.8, east 0.5, west and middle 0.3; elsewhere 0.4
    public static bool IsConifer(in Prop tree, IReadOnlyList<ConiferRegion> forests);           // hash(Id) < ConiferShare(centre)
}
public static class TreeFall
{
    public const float Seconds = 1.2f;
    public const float RestAngle = 1.48f;                            // about 85°: the crown on the ground, the root end a little up
    public static float Angle(float seconds);                        // RestAngle × (t / Seconds)², clamped to [0, RestAngle]
    public static Vector3 Axis(Vector2 push, int propId);            // (push.Y, 0, −push.X) normalised: tipping "up" about it by +angle leans it along push; zero push → a direction from the id
    public static Vector2 Along(Vector3 axis);                       // (−axis.Z, axis.X): the way the crown goes (batch E review)
    public static Matrix4x4 Pose(Matrix4x4 upright, Vector3 axis, float seconds);   // the upright transform turned about its root (batch E review)
}
public readonly record struct SectorGrid(float SectorSize)
{
    public (int X, int Z) KeyOf(float x, float z);                   // floor division, negatives too
    public int Touching(Vector2 from, Vector2 to, float margin, Span<(int X, int Z)> keys);   // sectors the widened segment meets; returns the count
    public int InView(Vector2 eye, Vector2? tank, float near, float reach, Span<(int X, int Z)> keys);   // around the eye + along eye → tank, each once (batch E review)
    public static Dictionary<(int X, int Z), List<int>> GroupByCentre(IReadOnlyList<Prop> props, Func<Prop, bool> which, float sectorSize);   // indices ascending per sector
    public static bool Crosses(Vector2 a, Vector2 b, Vector2 min, Vector2 max);   // segment vs axis-aligned box (T22, the houses)
    public static bool Crosses(in Obb2 box, Vector2 a, Vector2 b);                // the same in the box's own frame (batch E review)
}
public static class PropExtents { public static float HeightAboveGround(this in Prop prop); }   // Height − WallLine.Sink (batch E review)
```

```csharp
namespace Tank.Client.World;   // World/Logic, engine-free (batch E review)
public enum PropChange : byte { None, Fell, Rose }
public struct PropDown          // a tree, house or fence going down over Duration: See(standing, snap) / Pushed(push) → re-aim? / Step(delta) → at rest?
public sealed class SeeThroughFades   // NearCamera 6, SeeThrough 0.5, FadeSeconds 0.3, MaxSectors 64; BeginFrame / MarkInWay / Step(delta, changed)
public static class Lists { public static void RemoveSwapAt<T>(this List<T> list, int index); }
// Views (Godot): ForestViews.SectorSize 80, VisibleRange 520, ShadowRange 150; HouseViews.SectorSize; ForestViews.Build(Battlefield, IReadOnlyList<TreeFit.ConiferRegion>);
// BattleWorld.Forests; MapStyle(…, string? MapId = null), MapStyle.For(mapId); MapSites.IdOfGrid(HeightField); PropFx.OnEvent(…, forests)
```

```csharp
namespace Tank.Sim.Maps;   // batch E review: the client's source for the conifer regions, no wire or determinism change
public static class RiverlandMap { public const float ForestJitter; public static readonly IReadOnlyList<RiverlandForest> Forests; }   // east, west, north, north-east, centre
public sealed record RiverlandForest(string Name, Vector2 Min, Vector2 Max);
```

**规则/要点**（河谷设计 §6.4）

- 林中树的网格：每种（针叶、阔叶）各 2 个变体，树干半径取 `冠半径 / 7`（按 `Measure` 量出的冠半径算，迭代一次），这样按物件缩放后，视觉树干半径 = 物件树干半径（锥度让 1 m 高处差不超过 20%）。图外树带仍用默认参数（不变）。
- `ForestViews.Build`：`IsConifer` 分种、`Id % 2` 分变体；`SectorGrid(80)` 按树干中心分扇区；每个扇区每种每变体一个 `MultiMesh`（`UseCustomData`），可见的那份 `VisibilityRangeEnd = 520`、边距 60、`FadeMode = Self`、不投影；另一份只投影（`ShadowsOnly`、`dither = false`），`VisibilityRangeEnd = 150`。实例：绕 Y 随 id 转、`TreeFit.Scale`、落在 `VisualHeightAt − 0.1`；自定义数据 r/g/b 同 `TreeLine`（亮度、风相、色相，取自 id 的哈希），a = 1 − 不透明度。
- 倒下：`OnEvent` 记下 `PropDestroyed` 里树被推的方向；`Sync` 在 `field.StateVersion` 或 `field` 实例变了时扫一遍立着与否：刚倒的开始计时，`TreeFall.Angle` 绕树根、绕 `TreeFall.Axis` 转，1.2 秒后躺平不再更新；恢复的立刻立回去。只有正在倒的树每帧更新。
- 半透明：离镜头 6 m 以内的树、以及树冠圆和“镜头 → 自己的车”这条线段（地面投影）相交的树，0.3 秒内淡到 0.5，离开后淡回（同 `BushField`）；只查 `SectorGrid.Touching(镜头, 车, 4.2)` 覆盖的扇区里的树和正在淡入淡出的树。`ownHull` 为 null（自由镜头）时只看离镜头 6 m。
- `tree.gdshader`：`float opacity = 1.0 - INSTANCE_CUSTOM.a;`，`uniform bool dither = true;`，`#include "res://shaders/dither.gdshaderinc"`，片元里 `if (dither && opacity < 0.999 && bayer4(FRAGCOORD.xy) > opacity) discard;`。`TreeLine` 写的 a 是 0，画面不变。
- 倒树特效（`PropFx`）：在树冠高度（`Top − 3`）放一团树叶（针叶用 `Palette.Needles`、阔叶用 `Palette.Leaves`）加木屑（`Palette.Bark`），大小 2.0。
- `BattleWorld`：在 `WallViews` 之后建；两个 `Update` 分支都 `SyncMapLayers`（自由镜头的 hull 为 null）；`OnEvent` 交给它；`UseMapView` 时 `SetFarView(true)`。

**先写的测试**

- `TreeFallTests`：
  1. `The_fall_starts_slow_and_rests_after_1_2_seconds`：`Angle(0) == 0`、`Angle(-1) == 0`、`Angle(0.3f) < 0.1f × RestAngle`、`Angle(0.6f) == 0.25f × RestAngle`（±1e-5）、`Angle(1.2f) == Angle(5f) == RestAngle`。
  2. `The_fall_never_swings_back`：0～1.5 秒每 0.01 秒单调不减。
  3. `A_tree_falls_the_way_it_was_pushed`：`push = (0.6, 0.8)`：轴水平、与 push 垂直、单位长；绕轴把 (0,1,0) 转 π/2 得到 (0.6, 0, 0.8)（`Quaternion.CreateFromAxisAngle`，±1e-5）。
  4. `An_unpushed_tree_still_falls_somewhere_fixed`：`push = 0`：同一个 id 两次结果相同，单位长，不同 id 一般不同。
- `ForestTreeShapeTests`：
  5. `The_tree_line_keeps_its_trees`：`Conifer(7)` 与 `Conifer(7, 0.24f)` 顶点逐个相同，`Broadleaf(11)` 与 `Broadleaf(11, 0.28f)` 同。
  6. `Forest_trees_have_a_trunk_a_seventh_of_the_crown`：林中树四个变体 `Measure` 出的树干半径 / 冠半径 = 1/7（±5%）。
  7. `A_tree_is_drawn_the_size_of_its_prop`：树干 0.5、高 12（`Height` 12.3）的物件：视觉冠半径 3.5（±5%）、高 12（±2%）、1 m 高处树干半径在 0.4～0.6。
  8. `Conifers_and_broadleaves_mix_by_region`：北林中心 `ConiferShare` ≥ 0.7，西林中心 ≤ 0.4；`IsConifer` 对同一物件结果固定。
  9. `Sector_keys_floor_and_cover`：`KeyOf(79.9f, 0) == (0, 0)`、`KeyOf(80, 0) == (1, 0)`、`KeyOf(-0.1f, 0) == (-1, 0)`；从 (10, 10) 到 (170, 10)、宽 4.2 的线段碰到 (0,0)、(1,0)、(2,0) 三格（和 z 方向的 (−1)、(1) 行不碰）。
  10. `Grouping_by_centre_puts_each_prop_in_one_sector_in_order`：随机 500 个物件，每个只出现一次，在它中心所在的扇区，扇区内下标升序。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~TreeFall|FullyQualifiedName~ForestTreeShape|FullyQualifiedName~Vegetation" 2>&1 | grep -E '通过!|失败!'`；截图到 `build/shots/5a-t21/`：`forest`、`overview`、`map`；倒下的树以任务 24 的 `trees` 演示截图为准，这里只用 `--tank heavy --autodrive --screenshot-at 25` 碰碰运气（拍不到倒树不算失败）；回归六张。

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +10（约 935）。
- [ ] 截图看过：五片树林的位置和俯视图一致，树的大小和物件一致（树干粗细合理、不悬空不陷地）；倒下的树躺在被推的方向上；镜头和车之间的树是抖动的半透明；远处树林按距离淡出，没有成片突然出现。试驾场回归截图一致（树带、草丛不变）。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): forests — trees drawn to their props in sector multimeshes, falling the way they were pushed, see-through near the camera`，末行 `Tests: Art 925 → 935.`

### Task 22：房子

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/HouseShapes.cs`、`scripts/World/HouseViews.cs`、`shaders/house.gdshader`
- Modify: `scripts/Art/Palette.cs`（`Plaster`、`Planks`、`RoofTile`、`RoofSlate`、`Door`、`WindowGlass`、`PlasterDust`）、`scripts/Combat/PropFx.cs`（`House`：倒塌和直接命中的碎屑）、`scripts/World/BattleWorld.cs`
- Test: `tests/Tank.Art.Tests/HouseShapesTests.cs`

**接口**

```csharp
namespace Tank.Client.Art.Meshes;
public enum HouseStyle : byte { Cottage, Farmhouse, TwoStorey, Barn }   // 8×6×6.5, 12×7×7.5, 9×7×9, 14×9×8 (riverland design §3.6)
public static class HouseShapes
{
    public static HouseStyle StyleFor(float length, float width, float height);   // the nearest of the four sizes
    /// A cartoon house filling the box exactly: x ∈ [−length/2, length/2] (the ridge runs along X), z ∈ [−width/2, width/2]
    /// with the door on the −Z face (the prop's AxisForward once the node turns by the footprint's yaw, as WallViews does),
    /// y ∈ [0, height] with the ridge at height; walls of plaster or planks, a gable roof of tiles or slates, door, windows,
    /// a chimney on some; vertex colours from one of several colour sets picked by seed.
    public static MeshData House(HouseStyle style, float length, float width, float height, uint seed);
    public static MeshData Ruin(float length, float width, uint seed);   // a heap of chunks and roof pieces over (length + 1) × (width + 1), 1–2.2 m high
    // batch E review:
    public const int Variants = 3;                                       // looks per style
    public const float RuinShift = 0.5f;
    public static (float Length, float Width, float Height) SizeOf(in Prop house);
    public static uint SeedFor(in Prop house);                           // style × 3 + 1 … 3 by a hash of the id
    public static (Vector3 Wall, Vector3 Roof) ColoursOf(uint seed);     // sRGB, for the effects
    public static Vector2 RuinOffset(Vector2? push);                     // RuinShift m along the push, none without one
}
```

**规则/要点**（河谷设计 §6.5）

- `HouseViews.Build`：每栋一个 `MeshInstance3D`，网格按（样式, `seed % 3`）缓存；节点 `Transform` 同 `WallViews`：`new Basis(Vector3.Up, yaw)`，原点在 `Bottom + 0.3`（地块地面）；同一位置另有一个隐藏的废墟节点。材质 `house.gdshader`：顶点色 + `toon_light`，`instance uniform float opacity = 1.0;`，`dither.gdshaderinc` 抖动。
- 倒塌：`Sync` 发现倒了（状态版本门控）→ 0.5 秒内下沉 1.5 m、Y 方向压扁到 0.15，结束时隐藏房子、显示废墟；恢复时立刻换回完好的。`OnEvent` 记推的方向（废墟堆朝那边偏 0.5 m）。
- 半透明：同树（离镜头 6 m 内，或占地（加宽 0.5 m）与“镜头 → 自己的车”线段相交）。
- 特效（`PropFx`）：房子倒下 → 一团大烟尘（`Palette.PlasterDust`，大小 3.5，在 2 m 高）和三波碎块（灰泥块 `Palette.Plaster` 在 1 m、瓦片 `Palette.RoofTile` 在 4 m、再一波灰泥在屋脊高度，大小 2.5）；直接命中房子 → 灰泥碎屑（代替砖色）。
- 血条和伤害数字：`CombatHud` 已按 `IsWall` 覆盖房子（任务 4），血条在屋脊下 1 m，不用改。
- 颜色初值：`Plaster` F1E4C8、`Planks` B07A4A、`RoofTile` CF5E45、`RoofSlate` 5D6878、`Door` 7A4B2E、`WindowGlass` A9DDF2、`PlasterDust` EADBC0。

**先写的测试**（`HouseShapesTests`）

1. `Sizes_map_to_the_four_styles`：(8, 6, 6.5) 小屋、(12, 7, 7.5) 农舍、(9, 7, 9) 两层楼、(14, 9, 8) 谷仓；(11.5, 7, 7.4) → 农舍。
2. `A_house_fills_its_box_exactly`（Theory，四种 × 种子 1～3）：顶点 x 的最小、最大是 ∓长/2，z 是 ∓宽/2，y 是 0 和高（都 ±0.01）。
3. `The_ridge_runs_along_x`：y > 高 − 0.01 的顶点 |z| < 0.3。
4. `The_door_is_on_the_front`：带 `Door` 颜色的顶点 z 都在 −宽/2 附近（±0.08），+Z 面上没有。
5. `Normals_are_clean_and_the_mesh_is_light`：法线都是有限的单位向量；三角形 < 3000。
6. `A_ruin_covers_the_widened_footprint_low`：`Ruin(12, 7, s)` 的 x、z 在 ±6.5、±4 内，高在 1～2.2 之间。
7. `Seeds_pick_different_colour_sets`：种子 1、2、3 的墙色或屋顶色至少有两组不同。

**命令**：`dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~HouseShapes|FullyQualifiedName~Palette" 2>&1 | grep -E '通过!|失败!'`；截图到 `build/shots/5a-t22/`：`town`、`overview`、`map`；用靶车 1 那栋房子看倒塌：`--map riverland --camera-preset town --autodrive --fx-shot 3 --quit-after 16`（不一定打得到房子，倒塌的截图以任务 24 的 `house`、`ram-house` 为准）；回归六张。

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +7（约 942）。
- [ ] 截图看过：房子正好盖住各自的占地（坦克撞上去的位置和看到的墙面一致），面朝公路，颜色有变化，屋顶不超出墙外；镜头和车之间的房子半透明。试驾场回归截图一致。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): cartoon houses — four styles filling their boxes, sinking into ruins with dust and debris, see-through near the camera`，末行 `Tests: Art 935 → 942.`

**批次 E 收尾**：两路审查（规格审查对照河谷设计 §6.4、§6.5）→ 跟进提交 → 进度第 5 行。从这里起河谷可以完整地开起来看。

## 批次 F：收尾

### Task 23：性能

**Files:**
- Modify: `src/Tank.Client/scripts/World/BattleWorld.cs`（`Update` 两个分支：`_bushes.SyncStanding`、`_walls.Sync` 的全量同步只在 `_field.StateVersion` 或 `_field` 实例变了时做；倒下动画进行中的仍每帧推进）、`scripts/World/WallViews.cs`（把“看立着与否”和“推进倒下的栅栏”分开）、`scripts/Overlay/CombatHud.cs`（第 129～133 行：受损的墙和房子的名单只在版本变了时重建，每帧只给名单里的定位）、`scripts/World/RockMeshes.cs`（按 80 m 扇区把岩石合并成一个网格，碎石按扇区一个 `MultiMesh`）、`scripts/World/BushField.cs`（按扇区分 `MultiMesh`；`Update` 只看镜头和车附近扇区里的草丛和正在淡入淡出的）、需要时 `scripts/World/TerrainMesh.cs`（远处的块换半分辨率，按距离切换）
- Test: `tests/Tank.Art.Tests/SectorGridTests.cs`（只在需要新的引擎无关代码时加；合并岩石用现有的 `Merge.Build`）

**规则/要点**（河谷设计 §6.9）

- 先测再改：任务开始时先跑河谷 `spin`、`orbit` 和试驾场 `spin`、`orbit` 各一次，记下 `fps-log` 的 cpu/gpu/process，按瓶颈依次做：状态版本门控 → 岩石合并 → 草丛分扇区 → 地形远处降采样（只有前面做完仍不到 60 帧才做）。每做一项重测一次，记进度。
- 合并岩石不改形状：每块岩石的 `MeshData` 按它原来的 `Position`/`Rotation` 变换后 `Merge.Build`；试驾场截图要和基线一致（光照、边缘光一样）。
- 草丛分扇区后，`SyncStanding`、透明度、推开的结果和原来逐丛相同（只是分组变了）。
- 不改仿真。

**先写的测试**（`SectorGridTests`，只有加了新的分组帮助方法时才写）：`Merged_rock_sectors_hold_every_rock_once`：试驾场的岩石按 `GroupByCentre` 分组后，合并网格的顶点数之和等于逐块网格顶点数之和。

**命令**：帧率四次（见“执行前须知”），日志放 `build/shots/5a-t23/`；`$S/baseline4b.sh $W build/shots/5a-t23-sandbox`。

**验收**

- [ ] 河谷 1600×900、关垂直同步：`spin`、`orbit` 平均 ≥ 60 帧，20 秒后单帧最差 ≤ 33 ms；试驾场 `spin`、`orbit` 比 `build/shots/base/` 的低不超过 10%。
- [ ] 试驾场六张回归截图一致；全部测试通过、0 警告。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`perf(client): riverland at 60 fps — state-version gated syncs, rocks merged and bushes grouped by sector`，末行按实际写（没有新测试时写 `Tests: unchanged (Sim 780, Net 209, Art 942, Update 42).`）。

### Task 24：河谷的演示

**Files:**
- Create: `src/Tank.Client/scripts/Sandbox/Logic/RiverlandDemos.cs`
- Modify: `src/Tank.Client/scripts/Sandbox/Logic/MapSites.cs`（河谷的 `DemoNames = RiverlandDemos.Names`、`FindDemo = RiverlandDemos.Find`）
- Test: `tests/Tank.Art.Tests/RiverlandDemoTests.cs`

**接口**

```csharp
namespace Tank.Client.Sandbox;
public static class RiverlandDemos   // engine-free
{
    public static IReadOnlyList<string> Names { get; }   // "house", "ram-house", "trees", "ford"
    public static RangeDemo? Find(string? name);         // RangeDemo with Spots = RiverlandMap.TargetSpots, Stand and AimAt; null for any other name
}
```

**规则/要点**（河谷设计 §6.8）

- `house`：站在小镇里一栋房子（挑一栋 4 m 内没有栅栏、草丛、树的）正面 50～60 m，`AimAt` 是那面墙的中点（墙高一半），高爆弹，`KeepFiring`，直到塌。
- `ram-house`：站在另一栋房子正面 45 m，`AimAt` 是它的中心，`DriveSeconds` 6；中坦（默认）全油门开过去，撞上时 > 20 km/h，房子整栋塌、车穿过废墟。
- `trees`：站在东西公路上、东林西缘以西 40 m，`AimAt` 是林缘里 40 m 的点，`DriveSeconds` 8；配 `--tank heavy`，撞倒几棵树。
- `ford`：站在小镇浅滩以西 40 m 的公路上，`AimAt` 是对岸的公路点，`DriveSeconds` 5；中坦，截图时（`--screenshot-at` 见命令）在河中间。
- 站位和目标用 probe 工具挑、写成常数，`RiverlandDemoTests` 在无画面的完整仿真里跑一遍固定下来（照 `RangeDemoTests.RunDemo` 的写法：`Simulation` + 玩家 + 8 辆靶车，靶车的指令同 `TargetRange.FillInputs`）。

**先写的测试**（`RiverlandDemoTests`，地图用 `MapCatalog.Riverland.NewBattlefield()`）

1. `Riverland_lists_its_four_demos`：`Names` 四个，`Find` 都不为 null，`Find("pen")`、`Find(null)` 为 null；`MapSites.Riverland.DemoNames` 就是它们；`RangeDemo.Names` 不变。
2. `The_house_demo_brings_its_house_down`：中坦，30 秒仿真内那栋房子 `PropDestroyed`，而且它是这期间第一个被毁的物件；别的房子都立着。
3. `The_ram_demo_knocks_its_house_down_over_ram_speed`：中坦：那栋房子的 `PropDestroyed` 的 `ShellId` 是 0、`TankId` 是玩家；撞上前一 tick 的速度 > 20 km/h。
4. `The_trees_demo_fells_at_least_three_trees`：重坦：开车期间 ≥ 3 棵树 `PropDestroyed`（`ShellId` 0）；车停下时在东林矩形内。
5. `The_ford_demo_is_mid_river_at_the_shot`：中坦：在 `FireTick + 4.5 秒` 那一 tick，车体中心水深 ≥ 0.3、离 `Landmarks.Ford` ≤ 15 m。
6. `Demo_stands_are_clear_ground`：四个站位上玩家（中坦和重坦）的车体占地不与挡车的物件重叠，在可玩区内，不在水里。

**命令**

```bash
dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~RiverlandDemo|FullyQualifiedName~RangeDemo" 2>&1 | grep -E '通过!|失败!'
mkdir -p build/shots/5a-t24; R=($S/godot_run.sh $W res://scenes/Sandbox.tscn 120 --map riverland)
"${R[@]}" --range-demo house --shot-after prop:0.4 --screenshot $W/build/shots/5a-t24/house-hit.png --quit-after 30
"${R[@]}" --range-demo house --shot-after down:1.5 --screenshot $W/build/shots/5a-t24/house-down.png --quit-after 40
"${R[@]}" --range-demo ram-house --shot-after down:1 --screenshot $W/build/shots/5a-t24/ram-house.png --quit-after 20
"${R[@]}" --range-demo trees --tank heavy --shot-after down:1.2 --screenshot $W/build/shots/5a-t24/trees.png --quit-after 20
"${R[@]}" --range-demo ford --screenshot $W/build/shots/5a-t24/ford.png --screenshot-at 7.5 --quit-after 10
```

**验收**

- [ ] 全部测试通过、0 警告；Art 至少 +6（约 948）。
- [ ] 五张截图看过：打房子有伤害数字和墙血条、塌了变废墟有烟尘碎块；撞塌房子车在废墟里；重坦撞进林子、树倒在车头方向；浅滩里车在河中间、两侧水花、镜头在水面之上。

**步骤**

- [ ] 写“先写的测试”里的用例，跑，确认失败（类型或成员不存在 = 编译失败也算）
- [ ] 实现到全部通过；四个测试项目全过、五个项目 0 警告
- [ ] 按“命令”“验收”跑完（有截图的逐张看过，有帧率的记下）
- [ ] 提交

**提交**：`feat(client): riverland range demos — shell a house down, ram one, push into the forest, wade the ford`，末行 `Tests: Art 942 → 948.`

### Task 25：验收截图、帧率、两实例对局

不改代码（发现问题就修，修的提交单独写 `fix(<scope>): …`）。

- [ ] 五个项目 0 警告；四个测试项目全过（记数字）；Soak 四种链路 PASS（记数字）；Release 下河谷生成、10 车 tick、16 车点亮的耗时（`dotnet test tests/Tank.Sim.Tests -c Release --filter "FullyQualifiedName~RiverlandPerformance|FullyQualifiedName~within_budget" --logger "console;verbosity=detailed"`）。
- [ ] 截图 `build/shots/5a-t25/`，逐张看并在进度里写一句：`map` 俯视（和河谷设计 §3.2 的示意对照）、`overview`、`town`、`ford`（`--autodrive` 车在水里溅水花）、`forest`（任务 24 `trees` 之后的倒树）、`plateau`、`dunes`、`house` 演示塌前塌后、`ram-house`、`trees`、`ford` 演示、地面提示（公路、水深各一张）。
- [ ] 试驾场六张回归截图与 `build/shots/base/` 一致。
- [ ] 帧率：河谷 `spin`、`orbit`；试驾场 `spin`、`orbit`；数字写进度。
- [ ] 房间：开房默认河谷、标题 `团队对战 · 河谷`；`--map sandbox` 开房显示试驾场地图；局域网列表（第二个实例停在菜单）那一行有“河谷”。
- [ ] 两实例在河谷上打完一局（阶段 5 设计 §1 的 5A 验收）：

  ```bash
  $S/pair4b.sh $W $W/build/shots/5a-t25 pair 200 \
    "--host --name 甲 --mode teambattle --map riverland --players 2 --bots 2 --autostart --match-seconds 60 --netgraph --screenshot $W/build/shots/5a-t25/pair-host.png --screenshot-at 40" \
    "--join 127.0.0.1 --name 乙 --tank heavy --autodrive --netgraph --screenshot $W/build/shots/5a-t25/pair-guest.png --screenshot-at 40" 3
  grep -hE "match started|entered match|match over|ERROR|Exception" $W/build/shots/5a-t25/pair-*.log
  ```

  两端日志都有 `[match] entered match TeamBattle on riverland`，房主有 `[server] match started: teambattle, map riverland` 和 `match over, back to the room`，没有异常；两张截图里是河谷（树、房子、水）、F3 面板的对账次数和最大修正合理（与 4C T7 同量级）。
- [ ] 发布脚本的冒烟参数在河谷默认下仍够用：`godot-mono --headless --path src/Tank.Client -- --host --port 27091 --bots 1 --autostart --quit-after 10`，输出里 `[server] match started` 和 `[match] entered match` 都有（`tools/ship.sh` 的 `smoke_test_macos` 用的就是这组参数）。
- [ ] 专用服务器：`dotnet run --project src/Tank.Server -c Release -- --map riverland --mode ffa --bots 3 --port 27094` + 客户端 `--join 127.0.0.1:27094 --autostart` 进局。
- [ ] `pgrep -fl 'MacOS/Godot'` 没有残留。

**提交**：没有代码改动就不提交，结果记进度（下一个任务一起提交）。

### Task 26：文档

**Files:** `docs/01-game-design.md`、`docs/02-tech-stack.md`、`docs/03-netcode.md`、`docs/04-architecture-and-plan.md`、`README.md`、`docs/PROGRESS.md`

内容以代码为准，不写没做的东西：

- `docs/01-game-design.md`：§2.1 试驾场（`--map`；河谷试驾场 8 辆静止靶车、没有巡逻车）；§2.2 三种模式都能在两张图上开；§3 地图改写成两张图（河谷的尺寸、布局、地面类型、水、树林、村镇、出生点；试驾场地图那张表保留）；§4 车型加地面和水的速度系数表、撞房子（中坦、重坦 20 km/h 以上）；§6 点亮加树冠；§7 可破坏物加树和房子（耐久 500、打塌发数表、废墟）；§8 画面风格加水、树林、房子；§9 HUD 加地面提示；§11 还没有做的改为 5B～5D 和阶段 6。
- `docs/02-tech-stack.md`：第 23 行“一张 400 m 的地图”改为两张图（最大 1200 m、约 7000 个物件，靠网格索引）；§5 表里“地图”一行写 v3 带地面和水。
- `docs/03-netcode.md`：§2.1 参数表（协议版本 3、地图物件上限 16384、不可靠包上限 1000 字节）；§4.1 第 117 行“超过 1150 字节的快照”改为 1000 并写原因（LiteNetLib 单包 1023）；§4.2 `RoomCommand` 加选地图、`RoomState`/`RoomInfo` 带地图、`MatchStart` 的地图带地面和水；§4.6 地图编码 v3 和大小（试驾场约 53 KB、河谷实测值）；§9.1 房间流程加选图和“地图生成中…”；§9.2 发现的回复带地图、列表有地图列；§9.3 房主线程和专用服务器在后台准备地图、`Tank.Server --map`；§12 已知限制加“物件事件不按可见性裁剪”“撞塌房子后预测等物件状态才压上废墟”。
- `docs/04-architecture-and-plan.md`：§1 仓库结构加 `Maps/Building`、`Maps/Riverland`、`MapCatalog`；§3 客户端目录加 `WaterSurface`、`ForestViews`、`HouseViews`、`Sandbox/Logic/MapSites`；§5 测试表加新的测试类；§6 阶段路线把阶段 5 写成 5A～5D、5A 已完成；§8 风险加地图体积和生成耗时。
- `README.md`：进度表（阶段 5 拆成 5A～5D，5A ✅）；“运行”里开房默认河谷、`--map`；启动参数表加 `--map sandbox|riverland`；房间说明加选地图；专用服务器加 `--map`（默认 riverland）；局域网列表多了地图列。
- `docs/PROGRESS.md`：5A 节点记录最后一行（验收数字、截图目录、帧率），“当前位置”改为 5A 完成、下一步 5B。

**命令**：`git diff --stat`；文档里提到的每个启动参数、类名、数字用 grep 在代码里核对一遍。

**步骤**

- [ ] 按上面逐个文件改
- [ ] 用 grep 把文档里的参数、类名、数字和代码核对一遍
- [ ] 提交

**提交**：`docs: stage 5A — riverland, room map choice and protocol v3 in docs 01–04 and the README`，末行 `Tests: unchanged (Sim 780, Net 209, Art 948, Update 42).`（按实际）

**批次 F 收尾**：两路审查（规格审查对照河谷设计 §6.8、§6.9、§7 和阶段 5 设计 §6 的 5A 部分；文档审查对照代码）→ 跟进提交 → 进度 → 下面的验收清单全部打勾后 squash 合入本地 main（见“执行方式”），合入后在 main 上再跑一遍四个测试项目和 0 警告检查。

---

## 验收清单（5A）

- [ ] 四个测试项目全部通过，Sim、Net、Client、Server、Soak 五个项目 0 警告；测试数比改动前（Sim 639、Net 170、Art 882、Update 42）只增不减。
- [ ] `SandboxFingerprintTests` 通过（试驾场地图逐位不变）；试驾场六张回归截图与基线一致；不带 `--map` 时试驾场画面、靶车、巡逻车、全部 14 个演示不变。
- [ ] 空间索引等价测试（试驾场、密集合成场地、合成树林、河谷）通过；河谷上 10 辆车 tick 不分配内存。
- [ ] 河谷生成 Release ≤ 1.5 s；河谷 10 辆车每 tick 仿真平均 ≤ 0.5 ms；16 辆车点亮更新平均 ≤ 1.5 ms（Release，M3）。
- [ ] 河谷的规则测试全过：水只在河湖、最深 ≤ 1.6 m、浅滩 0.5 m；公路纵坡 ≤ 8°、不下水（浅滩除外）；房子在平地、面朝路 3～10 m、不重叠；树、岩石、草丛的避让；出生点和路点的间距、平地、空旷、全部连通；8 辆靶车各自的意图。
- [ ] 协议 v3：v2 部分逐字节不变；河谷开局消息 < 1 MiB（记实际大小）；各项上限和逐字节截断都报 `ProtocolException`；小地图对局开过水、沙、路和树零修正，撞树撞房子后两边物件状态一致。
- [ ] `MaxUnreliableBytes` 1000，有对着 LiteNetLib 实际上限的测试。
- [ ] 服务器：三种模式都能在河谷上开；每张图生成和编码各一次；房主线程和专用服务器在后台生成；`Tank.Server --map sandbox|riverland`（默认 riverland），写错退出 2。
- [ ] 房间里选图：只有房主、只在房间里能改；房间状态和局域网回复都带地图；地图生成中“开始对战”不可用并写“地图生成中…”；新房间默认河谷；`--map` 指定开房的地图和试驾场的地图。
- [ ] 窗口标题：试驾场 `坦克 3D · 试驾场`（试驾场地图）、`坦克 3D · 试驾场 · 河谷`；对局 `坦克 3D · 河谷`。
- [ ] 截图逐张看过：`map` 俯视、`overview`、`town`、`ford`（坦克在水里溅水花）、`forest`（倒下的树）、`plateau`、`dunes`、`house` 演示塌前塌后、`ram-house`、`trees`、`ford` 演示、地面提示、房间、局域网列表、两实例对局。
- [ ] 帧率（关垂直同步，1600×900）：河谷 `spin`、`orbit` 平均 ≥ 60 帧、20 秒后单帧 ≤ 33 ms；试驾场比基线低不超过 10%。
- [ ] 两个实例在河谷上打完一局（`pair4b.sh`）：两端进局、结算、回房间，没有异常。
- [ ] 文档 `docs/01～04`、README 已按代码更新；`docs/PROGRESS.md` 的“5A 节点记录”完整。
- [ ] 每批两路审查和跟进提交都做了；squash 合入本地 main，合入后 main 上全部测试通过、0 警告；没有推远端。
- [ ] 用户试玩放在整个阶段 5 结束后。

---

## 与设计的偏差

写计划时按现在的代码定下来的做法，和河谷设计、阶段 5 设计的字面不一样的地方：

1. **默认地图分两层**：`BattleServerOptions.Map` 的默认值仍是试驾场地图（现有的 Net、Art 测试和 Soak 都建在它上面，不必每个测试都生成河谷）；“新房间默认河谷”落在游戏的入口：客户端开房（菜单和 `--host`）和 `Tank.Server` 的 `--map` 默认值都是河谷。`HostSettings` 的可选参数默认也是试驾场地图，同样只为测试。
2. **地图在哪个线程生成**：只有真房间（`ThreadedHost`、`Tank.Server`）在后台线程生成（`PrepareMapsInBackground`）；测试和 `--loopback` 回环房主在调用线程上同步生成，保持确定性。
3. **房间信息的地图字段只在 v3 起读**：局域网发现的回复里，版本 ≥ 3 才读地图名，v2 房间的回复照样列出来并显示“版本不符”（否则多读一个字段会把旧房间整条丢掉）。
4. **开局消息不带地图名**：客户端的对局窗口标题和固定机位按房间状态里的地图取；中途加入时开局消息先于房间状态到，标题在房间状态到的那一帧再设。
5. **显示名**：试驾场的那张图在目录和房间里叫“试驾场地图”（阶段 5 设计 §2 的说法），不是河谷设计 §4.4 的“试驾场”；试驾场用它时窗口标题保持现在的 `坦克 3D · 试驾场`（设计只给了河谷的标题），对局标题从 `坦克 3D · 对局` 改为按地图。
6. **树不推车**：树在撞倒判断里按树干圆算，在推出和穿透深度里直接跳过，而不是“和栅栏一样”按方框参与推出；这样“碰到树干才倒、不受阻挡”在圆形树干上严格成立，客户端预测也不必把树放进让路名单。
7. **撞塌房子后的预测**：撞塌房子（和 4A 以来重坦撞穿砖墙一样）后，客户端要等物件状态到了才按废墟减速，可能有一两次厘米级修正；对局测试的“零修正”只要求开过水、沙、路和撞倒树。
8. **河床和湿岸不占贴图通道**：着色器按高度和水位画河床和水边湿沙带，地面贴图的 A 通道空着；草的规则（`GroundCover`）不用改，因为层叠贴图的 R 通道就是“不是草地”的遮罩。
9. **水深放在 UV.x、带符号**：水面网格的每个顶点用 UV.x 存“水位 − 地面”（干的角是负数），这样岸线落在深度为 0 的插值线上；不用顶点色（避免颜色被压缩成 8 位）。
10. **空间索引的推出余量**：`Resolve` 每轮按当前车体中心查、加 4 m 余量，靠逐 tick 等价测试保证和全表扫描相同，而不是数学上严格的上界。
11. **可通行格子是 2 m**：5A 的连通检查用地形格子（2 m），阶段 5 设计 §4.1 的 4 m 通行网格是 5C 的事。
12. **远景地面不改**：`TerrainMesh.FarGroundSize` 本来就是 4000 m、以地图中心为中心，盖得住 1200 m 的图（河谷设计 §6.2 说要加大）。
13. **树种和房型由客户端推出**：线上的物件格式不变（河谷设计 §2.4），针叶还是阔叶、房子的样式和颜色由客户端按位置、尺寸和编号哈希决定，不同机器结果一样。
14. **林中树的树干**：`TreeShapes` 加一个可选的树干半径参数，林中树按“冠半径 / 7”建网格，保证视觉树干和碰撞树干相差不超过 20%；图外树带用默认值，不变。
15. **站点叫 `MapSite`**：旧计划叫 `SandboxSite`；现在对局也用它取固定机位，所以改名并放在 `Sandbox/Logic/`（自动编进 Art 测试）。演示沿用 `RangeDemo`，加 `Spots`、`AimAt` 两个可选参数，不另起一个类型。
16. **试驾场用缓存的生成结果**：`MapDefinition.NewBattlefield()` 返回生成一次后的 `Fresh()`（新加的 `Battlefield.Fresh()`），浮点和直接生成完全相同；服务器准备地图时就建好它，开局用它的 `Fresh()`（和 `Data.Build()` 逐位相同），不在 tick 线程上重建地图。
17. **地面提示画在状态面板里**：不新建 `GroundLine` 节点，由 `StatusPanel.SetGround` 画在车名上方，`CombatHud` 每帧给文字，试驾场和对局共用。
18. **耗时断言只在 Release 严格**：生成 ≤ 1.5 s、tick ≤ 0.5 ms、点亮 ≤ 1.5 ms 的断言在 Release 构建下用设计的数，Debug 下用宽松的上限，验收看 Release 的实测数字。
19. **`MaxUnreliableBytes` 的核对结果**：问题在 main 上仍然存在（LiteNetLib 2.1.4、MTU 发现关闭时单个不可靠包上限 1023 字节，1024 起抛 `TooBigPacketException`），按河谷设计 §5.3 改为 1000。
20. **`Tank.Server` 也默认河谷**：阶段 5 设计只说服务器 `--map` 指定初始地图；按“新房间默认河谷”把默认值定为 riverland。

**实施中新增的偏差**

- **树的总数约 3500，不是 5000**（任务 8，`ad2302a`）：设计按每棵 49 m²（7² m²）估算；按“树干间距 ≥ 6 m、林缘 20 m 内放宽到 12 m”的泊松圆盘采样实际约 60 m² 一棵，五片林子 3114 棵加约 400 棵零散的。测试的范围改为 3000～4500。客户端画树（任务 21、23）按约 3500 棵估算。
- **`Polyline.Stamp` 修过一个洞**（任务 8）：很缓的弯处相邻小段的转角被舍入成 0，外侧会漏掉单个采样点，河谷里留下了 2.3 m 的尖刺（约 (602, 395)）。`HalfTurnSine` 改用两方向弦长的一半，有回归测试。
- **`plateau` 机位的焦点高 2 m，不是 22 m**（任务 15）：锚点高度是“那一点的地面（或水面）之上”，焦点落在台地顶上，地面本身已高约 22 m；按计划的 22 会再叠一次，看向台地上空。
- **局域网列表的列宽和字号**（任务 16）：模式列从 12 缩到 8、新的地图列 10（各按最宽的名字“团队对战”“试驾场地图”），房间列表字号 16，这样一行连地址也放得下。
- **水面网格在图边取 `max(画出的高度, 仿真高度)`**（任务 18）：地形在最外 8 m 画得沉到远景地面（−0.3 m，低于水位 0），只按画出的高度算水深，干的图边会围一圈水；取两者较高的那个，图边只在真有水的地方（河道出口）有水面。
- **长草遮罩先把“非草地”外扩一格再模糊**（批次 D 审查）：只模糊时公路、沙地边上那格的 R 只有 6/9，加上 ±0.225 的噪声，约 14% 的公路采样点长草。R 先按 8 邻域外扩一格、再 3×3 模糊，每个非草地格子中心 R = 1，最坏噪声下也不长草；G、B 不变。河谷上（每格 4×4 采样）公路/沙地/土地长草的比例 13.99%/1.48%/2.49% → 0/0/0.0003%，草地格子长草的比例 98.84% → 96.04%（草和公路、沙滩之间多了约一格的土边）。着色器随后让沙和公路按它们占 R 的比例（`g / max(r, ε)`、`b / max(r, ε)`，夹到 1）混色（阈值降到 `smoothstep(0.15, 0.55)`、`smoothstep(0.2, 0.5)`，因为直边外扩那圈中心的比例只有 1/2），这圈外扩的边就是它围着的公路或沙的颜色，不再是一圈褐色土边。
- **河床和湿沙带在图边淡出**（批次 D 审查）：着色器的湿沙带和河床两个混合都乘 `smoothstep(0.0, 8.0, 到图边的距离)`（和 `TerrainMesh` 的 8 m 下沉同宽），否则干的图边下沉到水位以下，会画成一圈河床色；试驾场（非层叠）画面逐位不变，有测试核对。
- **只有炮弹爆炸会变成水柱**（批次 D 审查）：`BattleFx.ShellBurst` 只用于 `ShellExploded`（落在水里就是水柱，否则照常爆炸）；`Explosion` 不再查水，穿甲弹打在涉水坦克的装甲上仍是灰色小烟、坦克被击毁仍是火球。两处 `ShellExploded` 的调用改成 `ShellBurst`（计划原本说调用处一行不改）。
- **涉水有回差**（批次 D 审查）：水深超过 0.05 m 开始溅水花，降到 0.03 m 及以下才回到尘土（`SplashRules.WadeOutDepth`、`Wading(depth, wasWading)`），岸边水深在 0.05 m 附近抖动时不会每帧切换。
- **特效找水面找到为止**（批次 D 审查）：`DustTrail`、`BattleFx` 在没找到 `WaterSurface` 时每次都重新找（找到后缓存），`WaterSurface.Find` 跳过已排队删除的节点（上一局的场景还没释放时）。
- **林中阔叶的树冠被拉高**（批次 E 审查）：`TreeFit.Scale` 按物件的冠半径和高度缩放，阔叶网格横向约 1.0 倍、竖向约 1.5 倍（物件比网格高得多），树冠看起来偏长；针叶接近等比。保持现状（碰撞用的是物件，画面和它一致）。
- **废墟偏移可能伸出加宽的占地**（批次 E 审查）：废墟堆铺满（长 + 1）×（宽 + 1），即加宽 0.5 m 的占地，再朝推的方向偏 0.5 m，最多伸出加宽占地 0.5 m。仿真里的废墟只减速、不挡车，没有碰撞上的后果。
- **对局里推的方向来得晚**（批次 E 审查）：`BattleClient.OnEventBatch` 收到就设物件血量（预测要用），`PropDestroyed` 事件要等渲染时钟到了才放，比倒下晚约 0.1 秒。客户端按 `PropDown` 处理：事件到时物件已经在倒（或已倒好），树按推的方向重新定轴（躺平的立刻改躺向），房子的废墟重新偏移，栅栏重新定倒向；对局里实测晚到 0.08～0.17 秒，倒下角度还不到 1°。
- **建好场景后的第一次同步直接到终态**（批次 E 审查）：中途加入或场景建好前已经倒下的树、房子、栅栏，第一次同步时直接躺平（树 `Fallen = Seconds`）、换成废墟（没有推的方向就不偏）、栅栏平躺，不再全部同时播一遍倒下；之后的重同步（新的 `Battlefield` 实例）照旧只给变了的播动画。
- **针叶比例的区域来自 `RiverlandMap.Forests`**（批次 E 审查）：仿真加了公开的 `RiverlandMap.Forests`（名字 + 矩形，委托内部的 `RiverlandLayout.Forests`，有测试核对）和 `ForestJitter`；`TreeFit.ForestsOf(mapId)` 只在河谷用这五片林子，其他地图一律 0.4。地图名在对局里取房间状态的；中途加入时房间状态还没到，按地形网格的尺寸认出地图（`MapSites.IdOfGrid`）。线上格式和确定性不变。
- **倒下的树全部参与透明判断**（批次 E 审查）：倒树的树冠可能离树干 15 m 远，超出扇区余量；立着的树按扇区查（余量为全图最宽的树冠，`Build` 时算），不立着的树单独列表、每帧都查。
- **淡入淡出、扇区查找合为共用代码**（批次 E 审查）：`SeeThroughFades`（引擎无关，常量 6 m、0.5、0.3 秒、64 个扇区（批次 F 审查改为 256，见下），`BushField` 的同名常量也取自它）和 `SectorGrid.InView`；树和房子共用，行为不变。房子塌下的碎块、直接命中的碎屑按房子自己的墙色、屋顶色（`HouseShapes.ColoursOf`）。
- **T21、T22 多出的公开成员**：`TreeFit.Variants/ElsewhereShare/Variant`、`TreeShapes.ConiferTrunk/BroadleafTrunk`、`SectorGrid.Crosses`、视图的常量（`ForestViews.SectorSize/VisibleRange/ShadowRange`、`HouseViews.SectorSize`），连同批次 E 审查加的，记在任务 21、22 的接口块里。
- **任务 23 只做了状态版本门控和草丛的扇区查找**：先测再改的结果是河谷已经在目标以上（窗口置顶、1600×900、关垂直同步，20 秒后：`spin` 120～129 帧、`orbit` 约 121 帧，常被显示器的 120 帧上限卡住；不置顶时 130～139 帧），瓶颈在 GPU，不在脚本或绘制调用。按秒表分段测，`_Process` 里的脚本每帧约 0.6～0.7 ms，其中 `BushField.Update`（每帧遍历约 1000 丛）占 0.38 ms，全量同步 0.03 ms。所以：草丛、墙、栅栏、墙血条的全量同步只在 `StateVersion` 或 `Battlefield` 实例变了时做（`WallViews.Sync` 拆成 `SyncStanding` 和每帧的 `Fall`）；`BushField` 按 80 m 扇区登记草丛，`Update` 只看 `SectorGrid.InView`（镜头附近和镜头到车这条线，余量 = 最大草丛半径 + 加宽车体的对角半长 + 1 m）里的草丛和还没静止的（淡入淡出、回弹中），视野太宽、扇区数超过 256 时退回逐丛，结果和逐丛完全相同（运行时逐帧核对过：没看的草丛都不满足变透明或推开的条件、且已静止），每帧 0.38 → 0.03 ms。**没做**：岩石合并（把岩石整个去掉试过：绘制调用 826 → 562，CPU 渲染 0.67 → 0.56 ms，帧率不变）、草丛按扇区分 `MultiMesh`（现在 4 个变体共 8 次绘制，分扇区估计要多出上百次绘制，GPU 剔除的收益又测不出来）、地形远处降采样（帧率够）。所以没有 `SectorGridTests` 的新用例。
- **`trees` 演示站在林缘以西 37 m，不是 40 m**（任务 24）：计划写“东林西缘以西 40 m”。在无画面的仿真里试了林缘以西 36～40 m 的站位、正东两侧 5° 以内的每个朝向：林缘的树相距 5～10 m，大多数朝向只撞倒两棵；选的是 37 m、偏北 2° 的那条线，它两侧各半度的朝向撞倒的棵数和它一样多（重坦油门下三棵、滑行停下时第四棵），浮点末位的差别不会让结果跳变。`AimAt` 仍是林缘里 40 m 的点。测试只要求油门下至少两棵、停在东林的矩形里（CI 在 Linux x64 上跑，`MathF` 可能和 Apple arm64 差一位）。
- **扇区查找溢出时退回逐个查**（批次 F 审查）：`SectorGrid.InView` 写不下视野里的全部扇区时返回 −1（原来静默截断），草丛、树、房子都退回逐个查；`SeeThroughFades.MaxSectors` 从 64 改为和草丛一样的 256（`BushField` 也取它），原来 `BushField` 自己按包围盒估算扇区数的预检去掉。80 m 的扇区下河谷总共约 16 × 16 个，实际不会溢出。
- **偶发的尖峰**（任务 25，未能复现）：试驾场 `orbit` 的一次测帧里有一帧 48 ms，重测没再出现；第一次两实例对局里房主开局后（`[match] entered match` 之后、房主建场景期间）连记 3 行“server is overloaded”，F3 的服务器 tick 最大 293 ms（`TickProbe` 不算开局那一 tick，所以是一局里某个 tick 本身被拖住了，像是服务器线程整个停住：GC 挂起所有托管线程或系统调度，原因未确认）。同样的命令此后又跑了 4 次（任务 25 重跑 1 次，tick 最大 3.2 ms；批次 F 审查 3 次，共 9 次开局），都没有 overloaded。记为已知现象；再出现时看它是否总在房主建场景的那几秒。
