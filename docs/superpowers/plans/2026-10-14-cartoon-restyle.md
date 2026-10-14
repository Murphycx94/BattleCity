# 卡通画风改造与高草丛 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把阶段 1.5 的半写实试驾场整体改成《全面吃鸡模拟器》（TABG）那样的柔和卡通风格：纯色加平滑渐变、蓝紫色的浅阴影、圆润的造型、粗圆体界面；在仿真里加入不挡车、不挡炮弹的高草丛，客户端把它画成能把坦克藏进去的卡通草丛；顺带完成阶段 1.5 计划未执行的特效、性能、截图和试玩任务；窗口不被系统限速时 1600×900 在 M3 上稳定 60 帧以上。

**Architecture:** 所有颜色集中在不依赖 Godot 的 `Tank.Client.Art.Palette`（sRGB），网格库、外观数据和着色器默认值都引用它，测试检查着色器里标了 `// Palette.名字` 的默认值与之一致。所有卡通着色器 `#include` 同一个 `toon_light.gdshaderinc`，用它的 `light()` 取代 Godot 的 PBR 光照（包裹漫反射、阴影偏向调色板的蓝紫色、无高光、可选边缘光）。天空换成程序化的 `sky_toon.gdshader`，后期改为 Linear 色调映射、关 SSAO、弱泛光、FXAA 代替 MSAA。地形、草、岩石、树、坦克、特效都换成纯色卡通版本，照片素材全部删除。`Tank.Sim` 新增 `PropKind.Bush`：不参与碰撞和射线，`SandboxMap` 种 6 片草丛；客户端用不依赖 Godot 的 `BushShape` 生成草丛网格，`BushField` 用 MultiMesh 绘制，并逐丛计算网点透明度和被坦克推开的程度。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、Godot 4.7.2 .NET 版（Forward+，macOS 上走 Metal）、Godot 着色语言；字体：站酷快乐体（ZCOOL KuaiLe，SIL OFL 1.1）

**设计依据：** `docs/superpowers/specs/2026-10-14-cartoon-restyle-design.md`（全文）；仍然有效的部分见 `docs/superpowers/specs/2026-10-13-stage1.5-visuals-design.md`；上级文档 `docs/superpowers/specs/2026-10-13-third-person-tank-design.md`。

---

## 执行记录（2026-10-14，Task 1～16 已完成，Task 17 待用户试玩）

### 执行中偏离本计划的地方（均已评审）

- **调色板重调**：`Rock` #9E9890、`RockTop` #BDB6A8（比计划偏暖）；`TankBase` #64723A、`TankCamoA` #B8A263、`TankCamoB` #3A4A25（坦克要和草地拉开对比）；`Track` #4A4846；新增 `SlopeEarth` #9C9A64（陡坡泥土，原来的橙褐在坡面上太跳）。设计文档的调色板表已同步。
- **卡通光照**（`toon_light.gdshaderinc`）：阴影按通道钳制，并加了带保护的亮度上限；终止线变软；边缘光按反照率缩放；`toon_mottle` 拆到独立的 include（`cf329ae`、`d7538c1`、`50ab04d`、`2a9f131`）。
- **岩石**：改为以平底为基准（`RockShape.Fit`）的更饱满的岩石，颜色更暖、各块有差异（`1915730`、`49abe20`）。草背面也绘制，坦克与草地的对比再调了一轮。
- **特效池**：粒子、烟团、碎石、曳光对象池化，每次开炮不再分配，网格共享；曳光按帧长度计算且连续；狙击镜内抑制自己炮口的烟（`acc52f4`、`2a9f131`、`92fc1b6`）。
- **界面**：`HudStyle`/`StatusPanel` 缓存字符串，每帧不分配（`2a9f131`）。
- **高草丛参数**（取代计划里"每片 6～12 丛、半径 1.8～3.2 m、高 3.0～4.0 m"）：每片 10～14 丛，丛心散布半径 `BushSpread = 6` m，重叠系数 0.4（圆心距 ≥ 0.4 × 半径和），半径 2.6～3.6 m，高 3.4～4.2 m，每片至多尝试 120 次；每片第一丛固定在片心，半径至少 3.0 m（出生点旁的 0 号片至少 3.3 m）；片心丛放不下时 `PlantBushes` 抛 `InvalidOperationException`（指明是哪一片）；新增覆盖率测试，保证每片都有能把坦克整个藏住的位置（`95e6fab`、`4fe4074`）。
- **草丛客户端**：更大的"蓬松核心"、圆形占地辅助函数、阴影只用单独的 `ShadowsOnly` 实例（`9be08a5`）；挡在镜头与自己坦克之间的草丛也会变淡（2D 线段测试）；`SeeThrough` 由 0.4 改为 0.5，自己的坦克在草丛里看得更清（`4fe4074`）。设计文档 §6 已同步。
- **`src/Tank.Sim` 改动范围**：除计划列出的四个文件外，还动了 `Geometry/Obb2.cs`（圆与 Obb2 相交的辅助方法，供草丛占地使用）。
- **Task 15 没有做任何调整**：不限速时 `spin`、`orbit`、`bush`、`autodrive` 都稳定在 128～146 fps，所以没有启用 FSR（`Scenery.RenderScale = 1.0`），阴影距离、草的密度和半径保持原值，没有 Task 15 的提交。

### 最终参数

渲染缩放 1.0；平行光阴影最远 300 m；草半径 90 m、最远一档密度 0.9；`BushField`：`NearCamera` 6 m、`OwnTankMargin` 1 m、`SeeThrough` 0.5、`FadeSeconds` 0.3 s、`ReboundSeconds` 0.5 s。

### Task 15 帧时间（`--disable-vsync --fps-log`，M3，1600×900）

macOS 会把后台窗口限制在 60 fps：每次运行开头 7～20 秒是整齐的 60 fps 行（被限速，不是真实开销，已忽略）；之后解除限速。日志里的"process"一列在限速前后有 18～30 ms 的假值，以 fps 和不限速行的数值为准。

| 机位 | 不限速时的 fps | 帧时间（平均/最差） | draws / prims |
|---|---|---|---|
| `spin` | 128～145（其中一行 59，是限速切换瞬间） | 约 3.5～7 ms | 220～260 / 950k～1090k |
| `orbit` | 134～146，连续 13 行 | 3.4～6.5 ms / 最差 14 ms | 150～370 / 940k～1540k |
| `bush` | 145（30 秒日志，第 2 行起） | 3.0～4.0 ms / 最差 11.5 ms | 304 / 1330k |
| `autodrive` | 134～146（35 秒日志，约 17 秒起） | 4～13 ms / 最差 35 ms（一次开炮尖峰） | 100～370 / 600k～1300k |

均达到 60 fps 目标，未启用 FSR。

### 测试数

Tank.Core 62、Tank.Sim 138、Tank.Net 50、Tank.Update 42、Tank.Art 156（计划里写的 Sim 132、Art 150 已过时：之后加了草丛覆盖率、片心丛（含放不下时报错）、圆形占地和着色器相关的测试）。

### 交给 Task 17 试玩的观察

最终截图在 `build/shots/cartoon-*.png`。需要重点看：草丛里的网点透明（`cartoon-bush`）、狙击镜暗角与粒子（`cartoon-sniper`）、云的形状在长焦下偏大偏糊、矮草在近处显得很密很亮、远处的草丛显得很小很暗（`cartoon-hill`）。

## 写计划时已核实、与设计的差异

写这份计划时，先把 worktree（`feat/stage15-visuals` 的 `6bfaad1`，**连同未提交的任务 16 粒子代码**）复制到一份临时副本，在副本里按任务顺序把下文全部实现了一遍：每个任务结束时客户端都能编译，测试数与下文一致，并用下文的命令截图、逐张看过。下文的代码由脚本从这份副本里直接导出，没有手抄；导出后又从起点把计划逐任务重放一遍，每个任务都编译通过、测试通过，最终结果与副本逐字一致。

实测结果：
- 卡通观感：亮面接近调色板原色（取样：坦克底色 #6E8B3D 画出来是 #6F8E61，岩石 #A9B4C2 画出来是 #A7BBD1，泥土 #D9934E 画出来是 #D39E64），暗面偏蓝紫，没有纯黑。
- 帧率：关 MSAA、开 FXAA、渲染缩放 1.0，`spin` 和 `orbit` 机位窗口不被限速时稳定在约 145 帧，最差帧时间 3.5～10 ms，从未低于 60 帧；`autodrive`（开炮、扬尘、草格子流式生成）只测到被系统限速的状态，稳定 60 帧，没有掉帧。所以 FSR 没有启用（`Scenery.RenderScale = 1.0`）。
- 对照：阶段 1.5 的画面开 MSAA 4× 时 `spin` 只测到 60 帧（无法排除被限速）；同样条件下只关 MSAA 就到 120～145 帧，与此前评审"MSAA 约占 6.5 ms"的结论一致。

与设计文字不同、但有意为之的地方：

- **调色板不依赖 Godot**：`Palette` 用 `System.Numerics.Vector3` 存 sRGB，`TankLooks`、`TreeShapes` 等纯 C# 代码可以直接用，`Tank.Art.Tests` 也能编译它；Godot 一侧用扩展方法 `PaletteColors.ToColor()`。除设计表里的 16 种颜色外，还加了 `Leaves`、`Needles`、`Bark`、`Flash`、`Fire`、`Smoke`、`RockDust`、`SoilDust`、`UiText`、`UiOutline`，保证"所有颜色集中在一处"。
- **阴影着色的实现**：Godot 会把 `DIFFUSE_LIGHT` 再乘以 `ALBEDO`，所以"向 Shadow 混合约 50%"写成按通道的系数 `mix(ALBEDO, Shadow, 0.5) / ALBEDO × 0.55`（`toon_shadow_level = 0.55`），效果就是暗部颜色 = 原色与蓝紫色各半、亮度约一半，再加上环境光。
- **环境光**用固定颜色（`SkyTop` 与 `SkyHorizon` 的中间色，强度 0.6），不用天空辐照度；反射关闭。这样不需要每帧重算天空的辐照度贴图（天空着色器用了 `TIME`，否则会触发实时更新）。太阳强度取 0.8，实测这时亮面正好是调色板原色。
- **着色器文件名**：设计点名的 `terrain_toon.gdshader`、`tank_toon.gdshader` 是新文件，旧的 `terrain.gdshader`、`tank.gdshader` 删除；`rock`、`grass`、`tree`、`far_ground` 原地改写，文件名不变；新增 `sky_toon.gdshader`、`puff.gdshader`（特效）、`bush.gdshader` 和 `toon_light.gdshaderinc`。`common.gdshaderinc` 只保留噪声函数。
- **MSAA**：在 `Scenery.ConfigureViewport` 里对试驾场的视口关闭，`project.godot` 的 `msaa_3d=2` 不动，因为旧的 Battle City 客户端（`Main.tscn`）也在用它。
- **岩石**：改用超椭球（指数 3）代替"向立方体插值"，去掉碎屑噪声，团块噪声 0.13 → 0.06。新增测试要求相邻面夹角小于 50°（阶段 1.5 的岩石是 80°～95°）；贴合碰撞框的规则和测试不变，只更新了一个顶点的固定值。
- **草叶**：仍是每簇 5～7 片、每片 3 段，但最后一段收成一个顶点（每片 7 个顶点、5 个三角形），各行向上收拢，所以叶尖是短而钝的三角形。叶高 0.25～0.45 m，根部宽 0.16 m。
- **树**：针叶树每层的边缘用 3 个点倒圆，8 条边；阔叶树的树冠球用 1 级细分的二十面体（每个 80 个三角形）。阶段 1.5 的三角形预算（针叶树 ≤ 320、阔叶树 ≤ 700）保持不变。
- **坦克倒角**：车体 0.04 → 0.06、炮塔和防盾 0.05 → 0.075、细节件 0.015 → 0.0225、履带板 0.01 → 0.015。两处例外：上车体后板太短，0.06 的倒角放不下，所以把后板上端从 (−3.25, 1.42) 提到 (−3.25, 1.46)；履带齿只有 2.5 cm 高，倒角保持 0.006。`TankLook` 去掉不再使用的 `MudHeight`、`WheelMudHeight`。炮的原点在炮耳，所以炮的材质多一个 `height_offset`，底部渐暗按整车高度算。
- **特效**：烟团、尘土、碎石都是不透明的卡通小球（`puff.gdshader` 加卡通光照），靠缩放曲线"鼓起—回弹—缩小"消失，不用半透明贴片，因此没有排序问题，开销也更小。镜头震动（0.08 m、0.15 s）已在未提交的代码里，保留。
- **调试参数**：未提交的代码里有几行用环境变量 `FX_DELAY`、`FX_GROUND` 截特效的临时代码（标着 `TMPFX`）。Task 1 把它们换成正式参数 `--fx-shot SECONDS`；Task 10 再加 `--sniper`（以狙击镜视角启动），这样特效和狙击镜都能截图验收。
- **字体**：选站酷快乐体（ZCOOL KuaiLe），许可证 SIL OFL 1.1，已核实允许随软件再分发和商用、没有保留字体名。它本身只有常规字重，界面用 `FontVariation.VariationEmbolden = 0.7` 加粗。许可证文件沿用上游的文件名 `OFL.txt`，不改名为 `LICENSE`。文件从 `google/fonts` 固定提交 `66928797f271fed5ee428544e7229685e2d90127` 下载，计划里给出 SHA-256 校验。
- **草丛被推开的回弹**：`BushField` 给每丛记住"最后一次与坦克重叠时的坦克中心"和一个推开强度（进入时约 0.12 s 升到 1，离开后 0.5 s 降到 0，平滑缓动），写进 MultiMesh 的实例自定义数据；着色器以这个中心、配合全局参数 `tank_axes`、`tank_footprint` 里的车体朝向和尺寸，把叶片水平推开最多 0.6 m。核心球不被推开，只跟风轻微摆动。
- **网点透明**：4×4 拜耳矩阵；阴影也会跟着变成网点，影响不明显，接受。叶片和核心的法线都从同一个中心向外，整丛的明暗像一个软球；双面绘制时着色器把 Godot 对背面法线的翻转再翻回来。
- **仿真**：`Prop.IsSolid` 为计算属性（草丛为 false）；`Battlefield` 构造时要求草丛的两个半轴相等；`BushesNear` 返回 `IEnumerable<Prop>`，按草丛边缘到点的距离判断。6 个草丛区中心是手工挑的平地：(222, 296)（出生点旁）、(150, 250)、(270, 250)、(60, 200)、(330, 120)、(250, 70)；每丛 `Bottom` 比地面低 0.3 m（同岩石），露出地面 3～4 m。草丛排在岩石之后编号，所以岩石的编号和形状都不变。
- **客户端**：草也长在草丛底下（只有实心物件挡草）。`bush` 机位把坦克放在出生点旁那片草丛的中心，镜头在草丛区外 17 m：一丛不包住坦克的草丛正好挡在中间、保持不透明，包住坦克的几丛变成网点，两种效果在一张图里都能看到。

## 执行前须知

- **不要新建 worktree**，直接在现有的 worktree 里执行：`/Users/murphy/Desktop/dev/BattleCity-stage15`（分支 `feat/stage15-visuals`）。下文所有路径都相对于它。
- worktree 里有**未提交、未审查**的任务 16 粒子代码（`OrbitCamera.cs`、`BattleFx.cs`、`ShellViews.cs`、`SandboxMain.cs` 的改动，新文件 `DustTrail.cs`、`ParticleKit.cs`）。**不要还原它们**：Task 1 去掉其中的临时调试代码后，把它们作为检查点提交；Task 9 再按卡通风格改写。
- 每个 shell 先执行下面这段（`dotnet` 不在默认 PATH；`godot_run` 保证 Godot 无论如何都会被强制结束——曾有一次 `--quit-after` 卡住 15 小时）：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage15
mkdir -p build/shots
# godot_run SECONDS ARGS...: run Godot, kill it hard after SECONDS no matter what
godot_run() {
  local limit=$1; shift
  godot-mono "$@" & local pid=$!
  ( sleep "$limit"; kill -9 "$pid" 2>/dev/null ) 2>/dev/null & local killer=$!
  wait "$pid"; kill "$killer" 2>/dev/null; wait "$killer" 2>/dev/null
}
# shot NAME PRESET [MORE ARGS]: one screenshot 3 s in, saved as build/shots/NAME.png
shot() {
  local name=$1 preset=$2; shift 2
  godot_run 45 --path src/Tank.Client res://scenes/Sandbox.tscn -- --camera-preset "$preset" \
    --screenshot "$PWD/build/shots/$name.png" --screenshot-at 3 --quit-after 3.5 "$@" 2>&1 | grep -E "Screenshot|ERROR|SHADER|Exception"
}
# fxshot NAME DELAY [MORE ARGS]: autodrive; screenshot DELAY s after the first shot fired after 3 s
fxshot() {
  local name=$1 delay=$2; shift 2
  godot_run 45 --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --fx-shot "$delay" \
    --screenshot "$PWD/build/shots/$name.png" --quit-after 12 "$@" 2>&1 | grep -E "Screenshot|ERROR|SHADER|Exception"
}
# fpslog NAME PRESET SECONDS: frame-time log with vsync off, also kept in build/shots/NAME.log
fpslog() {
  godot_run $(( $3 + 30 )) --path src/Tank.Client --disable-vsync res://scenes/Sandbox.tscn -- --camera-preset "$2" \
    --fps-log --quit-after "$3" 2>&1 | tee "build/shots/$1.log" | grep -E "fps-log|ERROR|SHADER|Exception"
}
# importall: let Godot generate .uid / .import files for new scripts, shaders and assets
importall() { godot_run 240 --headless --path src/Tank.Client --import > /dev/null 2>&1; }
```

  `fxshot`、`fpslog` 在 Task 1 加入 `--fx-shot` 之后才能用；`--fps-log`、`--camera-preset` 早已存在。
- **从命令行启动的 Godot 不会编译 C#**：改了 `.cs` 之后，截图前一定先 `dotnet build src/Tank.Client/Tank.Client.csproj`，否则跑的还是旧程序集。只改着色器则不需要。
- **截图**：
  - 截图存在 `build/shots/`（已被 `.gitignore` 忽略）。
  - 每张截图都**必须用 Read 工具打开**，逐条核对该步列出的观感要求；不符合就先调参数再继续。
  - 输出里出现 `SHADER ERROR`、`ERROR:` 或 `Exception` 时先修复。没有出现 `Screenshot saved` 说明 `_Ready` 出错，看完整输出。
  - 游戏窗口会抢键盘焦点。带 `--camera-preset` 时游戏忽略所有输入；`--autodrive` 截图期间不要碰键盘。
- **测帧**：macOS 会把不在最前面的窗口限制在 60 帧，日志会整齐地显示 `60 fps`，这不是真实开销；窗口在最前面时能到 120～145 帧。第一行包含启动开销，忽略。测帧时不截图。
- **每个任务结束前**都运行一次 `pgrep -fl 'MacOS/Godot'`，确认本任务启动的 Godot 都已退出。
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`，`Tank.Art.Tests` 会编译网格库和 `Palette.cs`，那里的任何警告都会让测试项目构建失败。客户端项目关掉了该选项，但也要保持 0 警告。
- 新增或删除 `src/Tank.Client` 下的脚本、着色器、素材后，提交前运行 `importall`，把生成的 `.uid`、`.import` 一起提交；删除脚本或着色器时连同 `.uid` 一起删。下文的 `git add` 按目录添加，会带上它们。
- **不要改**：旧 Battle City 客户端代码（`scripts/Bootstrap/` 中除 `LaunchOptions.cs` 以外的文件、`scripts/UI/`、`scripts/View/`、`scripts/Controls/`）和 `project.godot`。`src/Tank.Sim/**` 只在 Task 11、12 修改。
- **坐标约定**（同阶段 1.5）：Y 向上，−Z 为前方；车体空间原点在接地中心；侧视轮廓 `(z, y)`，俯视轮廓 `(x, z)`。网格库三角形从外侧看逆时针，`GodotMesh.ToArrayMesh` 负责翻转。
- 分支最后用 squash merge 并入 `main`，所以删除的照片素材不会留在 `main` 的历史里。
- 提交信息用约定式前缀（feat/fix/perf/chore/docs/test）。

## 文件结构

**新建**

| 文件 | 职责 |
|---|---|
| `src/Tank.Client/scripts/Art/Palette.cs` | 全部颜色（sRGB，纯 C#），`Hex`、`Linear` |
| `src/Tank.Client/scripts/Art/PaletteColors.cs` | `Vector3.ToColor()`：调色板 → Godot `Color` |
| `src/Tank.Client/shaders/toon_light.gdshaderinc` | 共用卡通光照 `light()`、边缘光 `toon_rim()` |
| `src/Tank.Client/shaders/sky_toon.gdshader` | 渐变天空、太阳光晕、卡通云 |
| `src/Tank.Client/shaders/terrain_toon.gdshader` | 纯色分层地形加大尺度色斑（替换 `terrain.gdshader`） |
| `src/Tank.Client/shaders/tank_toon.gdshader` | 三色迷彩、底部渐暗、轮毂/胶圈、履带（替换 `tank.gdshader`） |
| `src/Tank.Client/shaders/puff.gdshader` | 不透明卡通烟团、尘土、碎石 |
| `src/Tank.Client/shaders/bush.gdshader` | 草丛：渐变、风、推开、网点透明 |
| `src/Tank.Client/scripts/Art/Mesh/BushShape.cs` | 程序化草丛网格（纯 C#） |
| `src/Tank.Client/scripts/World/BushField.cs` | 草丛 MultiMesh、逐丛透明与推开 |
| `src/Tank.Client/scripts/Overlay/HudStyle.cs` | 界面字体、颜色、描边文字与圆环 |
| `src/Tank.Client/scripts/Overlay/StatusPanel.cs` | 左下角状态栏（速度、装填，带图标） |
| `src/Tank.Client/scripts/Overlay/SniperVignette.cs` | 狙击镜暗角 |
| `src/Tank.Client/assets/fonts/ZCOOLKuaiLe-Regular.ttf`、`OFL.txt` | 界面字体及其许可证 |
| `tests/Tank.Art.Tests/PaletteTests.cs`、`BushShapeTests.cs` | 调色板与着色器默认值、草丛网格 |
| `tests/Tank.Sim.Tests/BushTests.cs` | 草丛的碰撞、射线、查询 |

**改写**：`Scenery.cs`、`TerrainMesh.cs`、`RockMeshes.cs`、`GrassField.cs`、`TankModel.cs`、`TankParts.cs`、`TankLook(s).cs`、`GrassClump.cs`、`RockShape.cs`、`TreeShapes.cs`、`Materials.cs`、`BattleFx.cs`、`ShellViews.cs`、`DustTrail.cs`、`ParticleKit.cs`、`Reticle.cs`、`CameraPresets.cs`、`SandboxMain.cs`、`LaunchOptions.cs`、`GroundCover.cs`（注释）；着色器 `far_ground`、`grass`、`rock`、`tree`、`common`；`Tank.Sim` 的 `Prop.cs`、`Battlefield.cs`、`TankMotion.cs`、`SandboxMap.cs`；`assets/CREDITS.md`；测试 `VegetationTests.cs`、`RockFullnessTests.cs`、`MeshAssert.cs`、`TankLookTests.cs`、`SandboxMapTests.cs`、`Tank.Art.Tests.csproj`。

**删除**：`shaders/terrain.gdshader`、`shaders/tank.gdshader`（及 `.uid`）；`assets/textures/**`、`assets/hdri/**`（Poly Haven 的 13 个文件及 `.import`）；`tools/fetch_assets.py`。

**测试数**：`Tank.Sim.Tests` 120 → 127（Task 11）→ 132（Task 12）；`Tank.Art.Tests` 131 → 135（Task 2）→ 136（Task 4）→ 138（Task 5）→ 142（Task 6）→ 144（Task 7）→ 150（Task 13）。`Tank.Core.Tests` 62、`Tank.Net.Tests` 50、`Tank.Update.Tests` 42 不变。

---

### Task 1：检查点——提交做到一半的粒子特效

未提交的粒子代码结构是对的（对象池、炮口/落点/扬尘/拖尾、镜头震动），设计要求保留结构、之后按卡通风格调整观感。这一步只做三件事：把 `TMPFX` 临时调试代码换成正式参数 `--fx-shot`，确认能编译、能跑，提交为检查点；再拍一组阶段 1.5 的基准截图，留给 Task 16 对比。

**Files:**
- Modify: `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`
- 提交（未提交的现有改动）：`src/Tank.Client/scripts/Cameras/OrbitCamera.cs`、`scripts/Combat/BattleFx.cs`、`scripts/Combat/ShellViews.cs`、新文件 `scripts/Combat/DustTrail.cs`、`scripts/Combat/ParticleKit.cs`

- [ ] **Step 1: 确认起点**

```bash
git status --short
git log --oneline -1
```

Expected：HEAD 是 `docs: cartoon restyle and tall bush design`（或之后只多了本计划的提交）；改动正好是上面列的 4 个修改和 2 个新文件。

- [ ] **Step 2: 加 `--fx-shot`，去掉 TMPFX**

在 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
/// </summary>
```

   替换为：

```csharp
/// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）
/// </summary>
```

2. 找到：

```csharp
    public bool FpsLog { get; init; }

```

   替换为：

```csharp
    public bool FpsLog { get; init; }
    public double? FxShotDelay { get; init; }

```

3. 找到：

```csharp
                "--fps-log" => o with { FpsLog = true },
                "--screenshot" => o with { ScreenshotPath = Next() },
```

   替换为：

```csharp
                "--fps-log" => o with { FpsLog = true },
                "--fx-shot" => o with { FxShotDelay = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.05 },
                "--screenshot" => o with { ScreenshotPath = Next() },
```

用以下内容整体替换 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`：

```csharp
using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// Stage-1 offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin
/// --fps-log --screenshot FILE --screenshot-at S --fx-shot S --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float AimRange = 900f;
    private const float SniperEyeAboveTrunnion = 0.45f;
    private const float MinAimDistance = 8f;
    private const float GrassCameraLean = 0.4f;   // grass focus sits this fraction of the way from tank to camera
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;
    private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks
    private const double FxShotWarmup = 3;          // --fx-shot: the first shot after this many seconds starts the clock
    private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead

    private readonly SandboxInput _input = new();
    private readonly InputCmd[] _commands = new InputCmd[1];
    private readonly FrameTimeLog _frameLog = new();
    private LaunchOptions _options = new();
    private Simulation _sim = null!;
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private TankModel _tank = null!;
    private GrassField _grass = null!;
    private DustTrail _dust = null!;
    private bool _playerFired;
    private ShellViews _shells = null!;
    private BattleFx _fx = null!;
    private Reticle _reticle = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _screenshotTaken;
    private double _firstShotTime = -1;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();

        _sim = new Simulation(SandboxMap.Create());
        _sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        _previous = _current = _sim.Tanks[0];
        _aimYaw = _current.Yaw;

        AddChild(Scenery.CreateEnvironment());
        AddChild(Scenery.CreateSun());
        Scenery.ConfigureViewport(GetViewport());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        var dirt = new DirtMask(_sim.Field.Terrain.SizeX, _sim.Field.Terrain.SizeZ, SandboxMap.Seed);
        terrain.Build(_sim.Field.Terrain, dirt);
        var rocks = new RockMeshes();
        AddChild(rocks);
        rocks.Build(_sim.Field.Props, _sim.Field.Terrain);
        _grass = new GrassField();
        AddChild(_grass);
        _grass.Build(_sim.Field.Terrain, dirt, _sim.Field.Props);
        if (!CameraPresets.StreamsAroundCamera(_options.CameraPreset))
            _grass.Update(new Vector3(SandboxMap.Spawn.X, 0, SandboxMap.Spawn.Y), int.MaxValue);   // fill what is in range before the first frame
        var trees = new TreeLine();
        AddChild(trees);
        trees.Build(_sim.Field);
        _tank = new TankModel();
        AddChild(_tank);
        _tank.Build(TankCatalog.Medium);
        _dust = new DustTrail();
        _tank.AddChild(_dust);
        _dust.Build(TankLooks.For(TankCatalog.Medium.Id).TrackX, TankCatalog.Medium.HullLength / 2f);
        _shells = new ShellViews();
        AddChild(_shells);
        _fx = new BattleFx();
        AddChild(_fx);
        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.SetAngles(_current.Yaw, 0.25f);
        var hud = new CanvasLayer();
        AddChild(hud);
        _reticle = new Reticle();
        hud.AddChild(_reticle);

        if (_options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, hill or spin.");
        if (!_options.AutoDrive && !HasPreset) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool HasPreset => CameraPresets.IsKnown(_options.CameraPreset);

    public override void _Input(InputEvent e)
    {
        if (HasPreset) return; // fixed-camera runs ignore stray input so screenshots stay repeatable
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        if (e.IsActionReleased("fire_gun") || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (HasPreset) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when captured:
                _camera.Look(motion.ScreenRelative);
                break;
            case InputEventMouseButton { Pressed: true } wheel when captured && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;
        }
        if (e.IsActionPressed("sniper_toggle")) _camera.ToggleSniper();
        if (e.IsActionPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _accumulator = Math.Min(_accumulator + delta, MaxCatchUp);
        while (_accumulator >= SimConstants.Dt)
        {
            _accumulator -= SimConstants.Dt;
            StepSimulation();
        }

        float alpha = (float)(_accumulator / SimConstants.Dt);
        TankState tank = Interpolate(_previous, _current, alpha);
        TankPose pose = TankPose.Compute(tank, _sim.Field.Terrain);
        _tank.Apply(pose, tank.TurretYaw, tank.GunPitch);
        _tank.Animate(tank.Speed, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, (float)delta);
        _dust.Update(tank.Speed);
        if (_playerFired)
        {
            _fx.Muzzle(_tank.MuzzleTransform);
            _camera.Shake(FireShake, FireShakeSeconds);
            _playerFired = false;
            if (_firstShotTime < 0 && _time > FxShotWarmup) _firstShotTime = _time;
        }
        _tank.Visible = !_camera.Sniper;
        if (_options.AutoDrive) _camera.SetAngles(tank.Yaw + 0.35f, 0.18f);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (HasPreset)
        {
            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _sim.Field.Terrain.HeightAt);
            _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
        }
        else if (!_input.FreeLook) UpdateAim(pose, tank.Spec);
        float trackHalf = TankLooks.For(tank.Spec.Id).TrackX + 0.29f + FootprintMargin;
        _grass.SetTankFootprint(new Obb2(new SN.Vector2(pose.Position.X, pose.Position.Z),
            new SN.Vector2(MathF.Max(tank.Spec.HullWidth / 2 + FootprintMargin, trackHalf), tank.Spec.HullLength / 2 + FootprintMargin), tank.Yaw));
        // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
        // camera is in the dense band); fixed far-away presets stream around their eye.
        Vector3 tankPos = pose.Position.ToGodot(), cameraPos = _camera.Camera.GlobalPosition;
        _grass.Update(CameraPresets.StreamsAroundCamera(_options.CameraPreset) ? cameraPos
            : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
        _shells.Sync(_sim.Shells, alpha);
        UpdateReticle(tank);
        RunAutomation(delta);
    }

    private void StepSimulation()
    {
        var (throttle, steer, fire) = _options.AutoDrive ? AutoDrive() : HasPreset ? ((sbyte)0, (sbyte)0, false) : _input.Sample();
        if (_options.AutoDrive && _options.FxShotDelay is not null) _aimPitch = FxShotPitch;   // shells land in view
        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire);
        _sim.Step(_commands);
        foreach (GameEvent e in _sim.Events)
        {
            if (e.Type == GameEventType.ShellFired)
            {
                bool mine = e.TankId == _current.Id;
                // Flash at the visible barrel tip for the player's tank (the sim event keeps its own muzzle position).
                if (mine)
                {
                    _playerFired = true; // the flash is spawned after this frame's pose update, at the barrel tip as drawn
                    _tank.Fire(_current.TurretYaw);
                }
                else _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
            }
            else if (e.Type == GameEventType.ShellExploded)
            {
                // Shells that stop above the ground hit a rock: grey dust instead of brown.
                bool onRock = e.Position.Y > _sim.Field.Terrain.HeightAt(e.Position.X, e.Position.Z) + 0.5f;
                _fx.Explosion(e.Position.ToGodot(), onRock);
            }
        }
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);

    /// <summary>Casts from the orbit focus along the camera's view; the turret then chases that point.</summary>
    private void UpdateAim(TankPose pose, TankSpec spec)
    {
        Camera3D cam = _camera.Camera;
        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
        SN.Vector3 origin = cam.GlobalPosition.ToSim() + look * (_camera.Sniper ? 0f : _camera.Distance);
        SN.Vector3 end = origin + look * AimRange;
        SN.Vector3 target = _sim.Field.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
        SN.Vector3 trunnion = pose.Trunnion(spec);
        // Looking steeply down puts the target almost under the turret, where yaw swings wildly; keep it at least MinAimDistance out.
        SN.Vector2 flat = new(target.X - trunnion.X, target.Z - trunnion.Z);
        if (flat.Length() < MinAimDistance)
        {
            SN.Vector2 dir = new(look.X, look.Z);
            dir = dir.LengthSquared() > 1e-6f ? SN.Vector2.Normalize(dir) : Angles.Forward(_aimYaw);
            target = new SN.Vector3(trunnion.X + dir.X * MinAimDistance, target.Y, trunnion.Z + dir.Y * MinAimDistance);
        }
        (_aimYaw, _aimPitch) = Angles.AimAngles(target - trunnion, _aimYaw);
    }

    private void UpdateReticle(in TankState tank)
    {
        // Same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).
        Ballistics.Launch launch = Ballistics.Aim(tank, _sim.Field);
        SN.Vector3 impact = launch.BlockPoint;
        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _sim.Field, out impact);
        Camera3D cam = _camera.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        string reload = tank.Reload > 0 ? $"{tank.Reload:0.0} 秒" : "就绪";
        string zoom = _camera.Sniper ? $"   狙击 {_camera.Zoom:0}×" : "";
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime,
            $"速度 {MathF.Abs(tank.Speed) * 3.6f:0} km/h   装填 {reload}{zoom}");
    }

    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
    };

    private void RunAutomation(double delta)
    {
        if (_options.FpsLog)
        {
            var rid = GetViewport().GetViewportRid();
            if (_frameLog.Add(delta,
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
                    Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
                    (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
                    (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)) is { } line)
                GD.Print(line);
        }
        double shotAt = _options.FxShotDelay is { } fxDelay ? (_firstShotTime < 0 ? double.MaxValue : _firstShotTime + fxDelay) : _options.ScreenshotAt;
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= shotAt)
        {
            _screenshotTaken = true;
            Error error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }
}
```

完成后 `grep -n "TMPFX\|FX_DELAY\|FX_GROUND" src/Tank.Client/scripts/Sandbox/SandboxMain.cs` 应无输出。

- [ ] **Step 3: 编译并生成 `.uid`**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

`DustTrail.cs.uid`、`ParticleKit.cs.uid` 应出现。

- [ ] **Step 4: 截图确认特效能跑，并留阶段 1.5 基准图**

```bash
fxshot t1-muzzle 0.05
fxshot t1-impact 0.3
for p in front side hill close orbit; do shot stage15-$p $p; done
```

打开 `t1-muzzle.png`、`t1-impact.png`：炮口有发光闪光或烟、落点附近有火光或尘土即可（观感在 Task 9 重做）。`stage15-*.png` 是半写实画面，只作对比基准，不需要逐条核对。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Client/scripts
git commit -m "feat(client): pooled particle effects and --fx-shot (checkpoint before cartoon restyle)"
```

### Task 2：调色板、程序化天空与后期

**Files:**
- Create: `src/Tank.Client/scripts/Art/Palette.cs`
- Create: `src/Tank.Client/scripts/Art/PaletteColors.cs`
- Create: `src/Tank.Client/shaders/sky_toon.gdshader`
- Create: `tests/Tank.Art.Tests/PaletteTests.cs`
- Modify: `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`
- Modify（整体替换）: `src/Tank.Client/scripts/World/Scenery.cs`

- [ ] **Step 1: 写调色板测试**

`PaletteTests` 检查十六进制解析、设计表里的每个颜色、sRGB→线性换算，以及"着色器里标了 `// Palette.名字` 的默认值与调色板一致"。最后这条要求至少找到一处标记，所以在写天空着色器之前它会失败。

创建 `tests/Tank.Art.Tests/PaletteTests.cs`：

```csharp
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Tank.Client.Art;

namespace Tank.Art.Tests;

public class PaletteTests
{
    [Fact]
    public void Hex_reads_rrggbb_with_or_without_hash()
    {
        Assert.Equal(new Vector3(155, 217, 74) / 255f, Palette.Hex("#9BD94A"));
        Assert.Equal(Palette.Hex("#9BD94A"), Palette.Hex("9bd94a"));
        Assert.Throws<FormatException>(() => Palette.Hex("#12345"));
        Assert.Throws<FormatException>(() => Palette.Hex("#GG0000"));
    }

    [Fact]
    public void Design_table_colours_are_exact()
    {
        Assert.Equal(Palette.Hex("9BD94A"), Palette.GrassLight);
        Assert.Equal(Palette.Hex("4E9A33"), Palette.GrassDark);
        Assert.Equal(Palette.Hex("D9934E"), Palette.Dirt);
        Assert.Equal(Palette.Hex("A9B4C2"), Palette.Rock);
        Assert.Equal(Palette.Hex("CBD5DF"), Palette.RockTop);
        Assert.Equal(Palette.Hex("4FA3E8"), Palette.SkyTop);
        Assert.Equal(Palette.Hex("DDF2FB"), Palette.SkyHorizon);
        Assert.Equal(Palette.Hex("5B5A8C"), Palette.Shadow);
        Assert.Equal(Palette.Hex("6E8B3D"), Palette.TankBase);
        Assert.Equal(Palette.Hex("C9B26B"), Palette.TankCamoA);
        Assert.Equal(Palette.Hex("3F5A2A"), Palette.TankCamoB);
        Assert.Equal(Palette.Hex("4A4A55"), Palette.Track);
        Assert.Equal(Palette.Hex("7CC243"), Palette.BushLight);
        Assert.Equal(Palette.Hex("3B7D2E"), Palette.BushDark);
        Assert.Equal(Palette.Hex("1E2333"), Palette.UiPanel);
        Assert.Equal(0.7f, Palette.UiPanelAlpha);
        Assert.Equal(Palette.Hex("FFC93C"), Palette.UiAccent);
    }

    [Fact]
    public void Linear_follows_the_srgb_curve()
    {
        Assert.Equal(0f, Palette.Linear(Vector3.Zero).X);
        Assert.Equal(1f, Palette.Linear(Vector3.One).Y, 5);
        Assert.Equal(0.2140f, Palette.Linear(new Vector3(0.5f)).Z, 3);
    }

    /// <summary>A shader uniform default tagged <c>// Palette.Name</c> must spell that entry's sRGB value to 3 decimals.</summary>
    [Fact]
    public void Shader_defaults_tagged_with_a_palette_name_match_it()
    {
        string shaders = Path.Combine(RepoRoot(), "src", "Tank.Client", "shaders");
        var tag = new Regex(@"vec3\(\s*([\d.]+)\s*,\s*([\d.]+)\s*,\s*([\d.]+)\s*\).*//\s*Palette\.(\w+)");
        int checkedCount = 0;
        foreach (string file in Directory.GetFiles(shaders, "*.gdshader*").Where(f => !f.EndsWith(".uid")))
        foreach (string line in File.ReadLines(file))
        {
            Match m = tag.Match(line);
            if (!m.Success) continue;
            FieldInfo field = typeof(Palette).GetField(m.Groups[4].Value, BindingFlags.Public | BindingFlags.Static)
                ?? throw new Xunit.Sdk.XunitException($"{Path.GetFileName(file)}: no Palette.{m.Groups[4].Value}");
            var expected = (Vector3)field.GetValue(null)!;
            var actual = new Vector3(Parse(m.Groups[1].Value), Parse(m.Groups[2].Value), Parse(m.Groups[3].Value));
            Assert.True(Vector3.Distance(expected, actual) < 0.002f, $"{Path.GetFileName(file)}: {line.Trim()} should be {expected}");
            checkedCount++;
        }
        Assert.True(checkedCount > 0, "no tagged shader defaults found");
    }

    private static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    private static string RepoRoot([CallerFilePath] string here = "") => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
```

在 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```xml
    <Compile Include="..\..\src\Tank.Client\scripts\Art\Mesh\**\*.cs" LinkBase="Mesh" />
    <!-- Tank look data, part builders and animation state are engine-neutral too. -->
```

   替换为：

```xml
    <Compile Include="..\..\src\Tank.Client\scripts\Art\Mesh\**\*.cs" LinkBase="Mesh" />
    <!-- The palette is engine-neutral too, so look data and vertex colours can use it. -->
    <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
    <!-- Tank look data, part builders and animation state are engine-neutral too. -->
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -3
```

Expected：编译失败，`Palette` 不存在（CS0103/CS0246）。

- [ ] **Step 3: 调色板与 Godot 转换**

创建 `src/Tank.Client/scripts/Art/Palette.cs`：

```csharp
using System.Globalization;
using System.Numerics;

namespace Tank.Client.Art;

/// <summary>
/// Every colour of the cartoon look, in one place (sRGB 0–1, before conversion to linear). Engine-neutral so the mesh
/// library and look data can use it too; <c>PaletteColors.ToColor</c> turns an entry into a Godot <c>Color</c>.
/// Shader uniforms that default to one of these carry a <c>// Palette.Name</c> comment, and a test keeps them in step.
/// </summary>
public static class Palette
{
    public static readonly Vector3 GrassLight = Hex("9BD94A");
    public static readonly Vector3 GrassDark = Hex("4E9A33");
    public static readonly Vector3 Dirt = Hex("D9934E");
    public static readonly Vector3 Rock = Hex("A9B4C2");
    public static readonly Vector3 RockTop = Hex("CBD5DF");
    public static readonly Vector3 SkyTop = Hex("4FA3E8");
    public static readonly Vector3 SkyHorizon = Hex("DDF2FB");
    public static readonly Vector3 Shadow = Hex("5B5A8C");
    public static readonly Vector3 TankBase = Hex("6E8B3D");
    public static readonly Vector3 TankCamoA = Hex("C9B26B");
    public static readonly Vector3 TankCamoB = Hex("3F5A2A");
    public static readonly Vector3 Track = Hex("4A4A55");
    public static readonly Vector3 BushLight = Hex("7CC243");
    public static readonly Vector3 BushDark = Hex("3B7D2E");
    public static readonly Vector3 UiPanel = Hex("1E2333");
    public const float UiPanelAlpha = 0.7f;
    public static readonly Vector3 UiAccent = Hex("FFC93C");

    // Not in the design table; picked to sit with the colours above.
    public static readonly Vector3 Leaves = Hex("6CC04A");      // broadleaf crowns
    public static readonly Vector3 Needles = Hex("3E9A5B");     // conifer tiers, a cooler green
    public static readonly Vector3 Bark = Hex("9A6A43");        // warm brown trunks
    public static readonly Vector3 Flash = Hex("FFE45C");       // muzzle flash, tracer
    public static readonly Vector3 Fire = Hex("FF9A3C");        // fireball
    public static readonly Vector3 Smoke = Hex("F4F1EA");       // cartoon smoke puffs
    public static readonly Vector3 RockDust = Hex("C9CDD3");    // puffs from a shell on rock
    public static readonly Vector3 SoilDust = Hex("E3C79A");    // puffs from a shell on soil, track dust
    public static readonly Vector3 UiText = Hex("FFFFFF");
    public static readonly Vector3 UiOutline = Hex("141826");

    /// <summary>"RRGGBB" (an optional leading '#') to sRGB 0–1.</summary>
    public static Vector3 Hex(string hex)
    {
        string h = hex.TrimStart('#');
        if (h.Length != 6 || !int.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int rgb))
            throw new FormatException($"'{hex}' is not an RRGGBB colour.");
        return new Vector3((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF) / 255f;
    }

    /// <summary>sRGB 0–1 to linear, per channel (the standard piecewise curve), for vertex colours the shaders use as-is.</summary>
    public static Vector3 Linear(Vector3 srgb) => new(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z));

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
}
```

创建 `src/Tank.Client/scripts/Art/PaletteColors.cs`：

```csharp
using Godot;

namespace Tank.Client.Art;

/// <summary>Godot side of <see cref="Palette"/>.</summary>
public static class PaletteColors
{
    /// <summary>sRGB palette entry as a Godot colour (Godot colours are sRGB; source_color uniforms linearise them).</summary>
    public static Color ToColor(this System.Numerics.Vector3 srgb, float alpha = 1f) => new(srgb.X, srgb.Y, srgb.Z, alpha);
}
```

- [ ] **Step 4: 运行，只剩着色器标记那条失败**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "^\s+失败|Failed |失败!|通过!|Passed!"
```

Expected：`Shader_defaults_tagged_with_a_palette_name_match_it` 失败（"no tagged shader defaults found"），其余通过。

- [ ] **Step 5: 天空着色器与新的场景设置**

天空按视线高度在 `SkyHorizon` 和 `SkyTop` 之间渐变，太阳周围加光晕，云是两层低频噪声取阈值得到的柔边白云，缓慢飘动；太阳方向保持阶段 1.5 对齐 HDRI 时的方向 (0.5546, 0.742, 0.3767)。后期：Linear 色调映射、曝光 1.0，关 SSAO，泛光强度 0.45、阈值 1.1（只有发光特效会亮），指数雾颜色取 `SkyHorizon`、密度 0.0009，关 MSAA 改 FXAA，渲染缩放常量 `RenderScale = 1.0`（小于 1 时自动用 FSR 1）。

创建 `src/Tank.Client/shaders/sky_toon.gdshader`：

```glsl
// Cartoon sky: a smooth gradient from the horizon colour up to the zenith colour, a soft glow around the sun, and
// soft-edged white clouds from thresholded low-frequency noise drifting slowly across a virtual cloud plane.
// The sun direction comes from the scene's DirectionalLight3D (LIGHT0_DIRECTION).
shader_type sky;

#include "res://shaders/common.gdshaderinc"

uniform vec3 sky_top : source_color = vec3(0.31, 0.639, 0.91);        // Palette.SkyTop
uniform vec3 sky_horizon : source_color = vec3(0.867, 0.949, 0.984);  // Palette.SkyHorizon
uniform float gradient_power = 0.55;     // < 1 keeps the pale band near the horizon narrow
uniform vec3 sun_glow : source_color = vec3(1.0, 0.97, 0.86);
uniform float sun_glow_size = 24.0;      // larger = tighter halo
uniform float sun_glow_strength = 0.55;
uniform float sun_disc = 0.9992;         // cos of the disc radius
uniform vec3 cloud_light : source_color = vec3(1.0, 1.0, 1.0);
uniform vec3 cloud_shade : source_color = vec3(0.78, 0.84, 0.95);  // undersides, a pale blue-grey
uniform float cloud_cover = 0.6;         // noise threshold; lower = more cloud
uniform float cloud_softness = 0.04;
uniform float cloud_scale = 1.1;         // noise features per unit of the cloud plane
uniform vec2 cloud_drift = vec2(0.006, 0.002);

// Two octaves only: rounded, puffy blobs rather than wispy fbm detail.
float puff_noise(vec2 p) {
	return value_noise2(p) * 0.68 + value_noise2(p * 2.6 + vec2(5.2, 1.3)) * 0.32;
}

void sky() {
	vec3 dir = normalize(EYEDIR);
	float up = clamp(dir.y, 0.0, 1.0);
	vec3 color = mix(sky_horizon, sky_top, pow(up, gradient_power));

	float toward_sun = max(dot(dir, LIGHT0_DIRECTION), 0.0);
	color = mix(color, sun_glow, pow(toward_sun, sun_glow_size) * sun_glow_strength);
	color = mix(color, vec3(1.0), smoothstep(sun_disc, sun_disc + 0.0004, toward_sun));

	if (dir.y > 0.0) {
		// Project onto a flat cloud layer; dividing by the height makes clouds shrink and crowd towards the horizon.
		vec2 plane = dir.xz / (dir.y + 0.12) * cloud_scale + TIME * cloud_drift;
		float n = puff_noise(plane);
		float body = smoothstep(cloud_cover, cloud_cover + cloud_softness, n);
		float lit = smoothstep(cloud_cover, cloud_cover + 0.22, puff_noise(plane + vec2(0.06, 0.09)));   // brighter where the cloud is thick, shaded on one side
		vec3 cloud = mix(cloud_shade, cloud_light, lit);
		color = mix(color, cloud, body * smoothstep(0.0, 0.12, dir.y));
	}
	COLOR = color;
}
```

用以下内容整体替换 `src/Tank.Client/scripts/World/Scenery.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.World;

/// <summary>
/// Cartoon sky (procedural gradient, sun glow and drifting clouds), a sun, and the post stack: Linear tone mapping so the
/// palette's colours reach the screen unchanged, no SSAO, a little glow for emissive effects only, light exponential
/// fog in the horizon colour, and FXAA instead of MSAA (MSAA was the biggest single frame cost on the M3).
/// </summary>
public static class Scenery
{
    /// <summary>The direction stage 1.5 lined up with the HDRI sun (elevation 48°, east of the spawn), kept so shadows don't move.</summary>
    public static readonly Vector3 SunDirection = new Vector3(0.5546f, 0.7420f, 0.3767f).Normalized();

    /// <summary>3D render scale. 1 = native; below 1 FSR 1 upscales (only if 60 fps needs it).</summary>
    public const float RenderScale = 1.0f;

    private static readonly Color SunColor = new(1.0f, 0.97f, 0.92f);
    private const float SunEnergy = 0.8f;
    private const float AmbientEnergy = 0.6f;
    private const float FogDensity = 0.0009f;

    public static WorldEnvironment CreateEnvironment()
    {
        var sky = new Sky
        {
            SkyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky_toon.gdshader") },
            RadianceSize = Sky.RadianceSizeEnum.Size64,
        };
        var environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = sky,
            // A flat sky-coloured ambient: shadows and back faces pick up the sky's blue instead of going black.
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Palette.SkyTop.ToColor().Lerp(Palette.SkyHorizon.ToColor(), 0.5f),
            AmbientLightEnergy = AmbientEnergy,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,   // no specular anywhere in the toon look
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            TonemapExposure = 1.0f,
            SsaoEnabled = false,
            GlowEnabled = true,
            GlowIntensity = 0.45f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.1f,   // only emissive effects (flash, fireball, tracer) go over this
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive,
            FogEnabled = true,
            FogMode = Godot.Environment.FogModeEnum.Exponential,
            FogLightColor = Palette.SkyHorizon.ToColor(),
            FogDensity = FogDensity,
            FogSkyAffect = 0f,          // the sky shader already fades to the horizon colour
            FogSunScatter = 0f,
            FogAerialPerspective = 0f,
        };
        return new WorldEnvironment { Environment = environment };
    }

    public static DirectionalLight3D CreateSun() => new()
    {
        Basis = Basis.LookingAt(-SunDirection, Vector3.Up),
        LightColor = SunColor,
        LightEnergy = SunEnergy,
        LightAngularDistance = 0.8f, // soft shadow edges
        ShadowEnabled = true,
        ShadowBlur = 1.5f,
        DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits,
        DirectionalShadowMaxDistance = 300f,
        DirectionalShadowSplit1 = 0.05f,
        DirectionalShadowSplit2 = 0.15f,
        DirectionalShadowSplit3 = 0.4f,
    };

    /// <summary>FXAA instead of MSAA (overrides project.godot's msaa_3d for this viewport only), optional FSR, soft shadow filter.</summary>
    public static void ConfigureViewport(Viewport viewport)
    {
        viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        viewport.Scaling3DMode = RenderScale < 1f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
        viewport.Scaling3DScale = RenderScale;
        RenderingServer.DirectionalSoftShadowFilterSetQuality(RenderingServer.ShadowQuality.SoftLow);
    }
}
```

- [ ] **Step 6: 测试全部通过**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 135`（原 131 个 + 4 个）。

- [ ] **Step 7: 编译、导入、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

```bash
shot t2-hill hill
shot t2-front front
```

打开两张图核对（地面、坦克此时还是半写实材质，颜色偏亮、偏怪是正常的，下面几个任务逐个替换）：
- 天空是干净的蓝色渐变，地平线处接近白色，没有照片天空。
- 天上有几团边缘柔和、略带蓝灰暗面的白云。
- 远处的树和山淡入地平线的浅色，没有灰蒙蒙的厚雾。
- 物体边缘有 FXAA 的轻微柔化，没有明显锯齿闪烁。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 8: 提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): cartoon palette, procedural sky and toon post stack"
```

### Task 3：卡通光照与地形

共用光照 `toon_light.gdshaderinc`：包裹漫反射（w = 0.5）；背光和阴影里颜色向 `Shadow` 混合 50%、亮度约一半；不加高光；边缘光按需开启。地形改为三种纯色（草地取 `GrassDark`→`GrassLight` 的 55% 处、`Dirt`、`Rock`），分层权重与原来完全相同（`GroundCover` 继续是它的 C# 等价实现），层间平滑过渡，再乘一层 40 m 和 12 m 两种尺度、±12% 的明暗色斑。地图外的远景地面用同样的草地颜色和色斑。

**Files:**
- Modify: `src/Tank.Client/scripts/World/TerrainMesh.cs`
- Modify（整体替换）: `src/Tank.Client/shaders/far_ground.gdshader`
- Delete: `src/Tank.Client/shaders/terrain.gdshader`
- Create: `src/Tank.Client/shaders/terrain_toon.gdshader`
- Create: `src/Tank.Client/shaders/toon_light.gdshaderinc`
- Modify: `src/Tank.Client/scripts/Art/Mesh/GroundCover.cs`

- [ ] **Step 1: 光照与地形着色器**

用以下内容整体替换 `src/Tank.Client/shaders/far_ground.gdshader`：

```glsl
// Ground beyond the map edge: the terrain's grass colour and mottling, nothing else. Only ever seen far away and through fog.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform float grass_tone = 0.55;
uniform float mottle_large = 40.0;
uniform float mottle_small = 12.0;
uniform float mottle_amount = 0.12;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	float mottle = (value_noise2(world_pos.xz / mottle_large) - 0.5) + (value_noise2(world_pos.xz / mottle_small + vec2(7.3, 2.1)) - 0.5) * 0.6;
	ALBEDO = mix(grass_dark, grass_light, grass_tone) * (1.0 + mottle * 2.0 * mottle_amount / 1.6);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
```

创建 `src/Tank.Client/shaders/terrain_toon.gdshader`：

```glsl
// Cartoon ground: grass, dirt and rock layers picked by slope, height and the baked dirt-patch mask (same rules and
// noise as before, mirrored in C# by GroundCover so grass grows only where the grass layer wins). Each layer is one
// flat palette colour times a large, soft light/dark mottling; layers blend smoothly. No textures.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform sampler2D dirt_mask : hint_default_black, filter_linear, repeat_disable;
uniform vec2 map_size = vec2(400.0, 400.0);
uniform float grass_max_slope = 27.0;   // degrees; set from GroundCover
uniform float rock_min_slope = 38.0;
uniform float dry_height = 22.0;        // hill tops above this turn patchy (not mirrored by GroundCover)

uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform float grass_tone = 0.55;        // the grass layer's colour between grass_dark and grass_light
uniform vec3 dirt_color : source_color = vec3(0.851, 0.576, 0.306);   // Palette.Dirt
uniform vec3 rock_color : source_color = vec3(0.663, 0.706, 0.761);   // Palette.Rock
uniform float mottle_large = 40.0;      // metres per feature
uniform float mottle_small = 12.0;
uniform float mottle_amount = 0.12;     // ± brightness

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	vec3 n = normalize(world_normal);
	// Layer weights: keep in step with GroundCover.GrassWeight.
	float slope = degrees(acos(clamp(n.y, 0.0, 1.0))) + (value_noise2(world_pos.xz * 0.35) - 0.5) * 6.0;
	float dirt_patch = texture(dirt_mask, world_pos.xz / map_size).r;
	dirt_patch = smoothstep(0.25, 0.95, dirt_patch + (value_noise2(world_pos.xz * 2.2) - 0.5) * 0.45);
	float dry = smoothstep(dry_height, dry_height + 10.0, world_pos.y) * 0.6 * (0.35 + 0.65 * value_noise2(world_pos.xz * 0.15));
	float rock_w = smoothstep(rock_min_slope - 4.0, rock_min_slope + 3.0, slope);
	float dirt_w = max(max(smoothstep(grass_max_slope - 2.0, grass_max_slope + 6.0, slope), dirt_patch), dry) * (1.0 - rock_w);
	float grass_w = max(1.0 - dirt_w - rock_w, 0.0);

	vec3 grass = mix(grass_dark, grass_light, grass_tone);
	vec3 albedo = grass * grass_w + dirt_color * dirt_w + rock_color * rock_w;
	float mottle = (value_noise2(world_pos.xz / mottle_large) - 0.5) + (value_noise2(world_pos.xz / mottle_small + vec2(7.3, 2.1)) - 0.5) * 0.6;
	ALBEDO = albedo * (1.0 + mottle * 2.0 * mottle_amount / 1.6);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
```

创建 `src/Tank.Client/shaders/toon_light.gdshaderinc`：

```glsl
// Shared cartoon lighting (design §4). Include it in a spatial shader to replace Godot's PBR light():
// - wrapped diffuse, saturate((N·L + w) / (1 + w)), so faces turned away from the sun never go black;
// - where the sun doesn't reach (turned away or in shadow), the colour moves towards the palette's blue-violet
//   Shadow colour instead of just getting darker;
// - no specular highlights. Other lights (the flash and fireball lamps) add plain wrapped diffuse.
// The sky-coloured ambient light from the environment is added by Godot on top.
// Rim light is opt-in per shader: add toon_rim(NORMAL, VIEW) to EMISSION in fragment().

uniform vec3 toon_shadow_color : source_color = vec3(0.357, 0.353, 0.549);  // Palette.Shadow
uniform float toon_wrap = 0.5;
uniform float toon_shadow_mix = 0.5;      // how far the unlit colour moves towards toon_shadow_color
uniform float toon_shadow_level = 0.55;   // brightness of the sun's fill on the unlit side (0 = only ambient)
uniform vec3 toon_rim_color : source_color = vec3(0.867, 0.949, 0.984);     // Palette.SkyHorizon
uniform float toon_rim_strength = 0.0;    // 0.25 on tanks and rocks

vec3 toon_rim(vec3 normal, vec3 view) {
	return toon_rim_color * toon_rim_strength * pow(1.0 - clamp(dot(normal, view), 0.0, 1.0), 3.0);
}

void light() {
	float wrapped = clamp((dot(NORMAL, LIGHT) + toon_wrap) / (1.0 + toon_wrap), 0.0, 1.0);
	vec3 sun = LIGHT_COLOR / PI;   // Godot pre-multiplies LIGHT_COLOR by PI
	if (LIGHT_IS_DIRECTIONAL) {
		float lit = wrapped * ATTENUATION;   // ATTENUATION carries the shadow map
		// DIFFUSE_LIGHT is multiplied by ALBEDO afterwards, so express "albedo mixed towards the shadow colour" as a
		// per-channel factor on the albedo.
		vec3 shaded = mix(ALBEDO, toon_shadow_color, toon_shadow_mix) / max(ALBEDO, vec3(0.02)) * toon_shadow_level;
		DIFFUSE_LIGHT += sun * mix(shaded, vec3(1.0), lit);
	} else {
		DIFFUSE_LIGHT += sun * wrapped * ATTENUATION;
	}
}
```

- [ ] **Step 2: 地形网格改用新着色器，删除旧着色器**

在 `src/Tank.Client/scripts/World/TerrainMesh.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// Smooth ground built from the sim's height field: same vertices as the sim, per-vertex normals from
/// <see cref="HeightField.NormalAt"/>, split into 32×32-cell chunks for culling. A large plane below the map edge
/// carries the far-ground shader out to the horizon.
/// </summary>
```

   替换为：

```csharp
/// Smooth ground built from the sim's height field: same vertices as the sim, per-vertex normals from
/// <see cref="HeightField.NormalAt"/>, split into 32×32-cell chunks for culling, drawn with the cartoon terrain shader.
/// A large plane below the map edge carries the far-ground shader (same grass colour) out to the horizon.
/// </summary>
```

2. 找到：

```csharp
    {
        ShaderMaterial material = Materials.FromShader("terrain")
            .WithSet("grass", Materials.Grass)
            .WithSet("dirt", Materials.Dirt)
            .WithSet("rock", Materials.GroundRock)
            .With("dirt_mask", dirt.ToTexture())
```

   替换为：

```csharp
    {
        ShaderMaterial material = Materials.FromShader("terrain_toon")
            .With("dirt_mask", dirt.ToTexture())
```

3. 找到：

```csharp
            Position = new Vector3(field.SizeX / 2f, FarGroundY, field.SizeZ / 2f),
            MaterialOverride = Materials.FromShader("far_ground").With("grass_albedo", Materials.Map(Materials.Grass, "diff")),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
```

   替换为：

```csharp
            Position = new Vector3(field.SizeX / 2f, FarGroundY, field.SizeZ / 2f),
            MaterialOverride = Materials.FromShader("far_ground"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
```

删除 `src/Tank.Client/shaders/terrain.gdshader` 和它的 `.uid`：

```bash
git rm src/Tank.Client/shaders/terrain.gdshader src/Tank.Client/shaders/terrain.gdshader.uid
```

在 `src/Tank.Client/scripts/Art/Mesh/GroundCover.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// <summary>
/// C# twin of the terrain shader's layer weights (<c>terrain.gdshader</c>: slope, dirt patch and rock weights, including
/// their value-noise perturbation), so the things that grow on the ground (grass) stop where the drawn grass layer does.
```

   替换为：

```csharp
/// <summary>
/// C# twin of the terrain shader's layer weights (<c>terrain_toon.gdshader</c>: slope, dirt patch and rock weights, including
/// their value-noise perturbation), so the things that grow on the ground (grass) stop where the drawn grass layer does.
```

- [ ] **Step 3: 测试、编译、截图**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 135`（`GroundCoverTests` 不受影响；着色器标记测试现在也检查地形和光照里的颜色）。

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

```bash
shot t3-hill hill
shot t3-front front
```

核对 `t3-hill.png`：
- 地面是均匀明亮的草绿色，没有任何照片纹理，近看能隐约看出大块的明暗色斑。
- 泥地斑块是橙褐色纯色，边缘柔和；大山和右侧小山的陡坡露出蓝灰色岩石层。
- 地图边缘外的远景地面与地图内颜色一致，没有接缝。
- 坦克的影子是偏蓝紫的浅色，不是黑色。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 4: 提交**

```bash
git add src/Tank.Client
git commit -m "feat(client): toon lighting and flat-colour terrain"
```

### Task 4：卡通草

草叶更宽、更短（0.25～0.45 m），尖端收成一个顶点，各行向上收拢，叶尖短而钝。颜色从根部的 `GrassDark` 过渡到尖部的 `GrassLight`，每簇有 ±16% 的明暗变化。风摆、远处淡出、坦克下方压平的逻辑原样保留。

**Files:**
- Modify（整体替换）: `src/Tank.Client/scripts/Art/Mesh/GrassClump.cs`
- Modify（整体替换）: `src/Tank.Client/shaders/grass.gdshader`
- Modify: `tests/Tank.Art.Tests/VegetationTests.cs`

- [ ] **Step 1: 改测试**

在 `tests/Tank.Art.Tests/VegetationTests.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
    {
        var seen = new HashSet<int>();
```

   替换为：

```csharp
    {
        Assert.Equal(7, GrassClump.VerticesPerBlade);    // three pairs and a single tip vertex
        Assert.Equal(5, GrassClump.TrianglesPerBlade);   // two quads and the tip triangle
        var seen = new HashSet<int>();
```

2. 找到：

```csharp
            if (t == 0) Assert.Equal(0f, p.Y);
            int row = i % GrassClump.VerticesPerBlade / 2;
            Assert.Equal(row / (float)GrassClump.Segments, t, 5);
        }
```

   替换为：

```csharp
            if (t == 0) Assert.Equal(0f, p.Y);
            int k = i % GrassClump.VerticesPerBlade;
            if (k == GrassClump.VerticesPerBlade - 1) Assert.Equal(1f, t);              // the tip
            else if (k >= 2) Assert.True(t > clump.Colors[i - 2].X && t < 1f);         // rows climb towards it
            else Assert.Equal(0f, t);                                                  // the root pair
        }
```

3. 找到：

```csharp
        MeshAssert.WindingMatchesNormals(clump);
    }
```

   替换为：

```csharp
        MeshAssert.WindingMatchesNormals(clump);
    }

    [Fact]
    public void Blades_are_wide_short_and_blunt_tipped()
    {
        Assert.Equal(0.25f, GrassClump.MinHeight);
        Assert.Equal(0.45f, GrassClump.MaxHeight);
        MeshData clump = GrassClump.Build(5);
        int v = GrassClump.VerticesPerBlade;
        for (int blade = 0; blade < clump.VertexCount / v; blade++)
        {
            int first = blade * v;
            Vector3 rootL = clump.Positions[first], rootR = clump.Positions[first + 1];
            Vector3 lastL = clump.Positions[first + v - 3], lastR = clump.Positions[first + v - 2], tip = clump.Positions[first + v - 1];
            Assert.True(Vector3.Distance(rootL, rootR) >= 0.14f, "wide cartoon blades");
            float tipWidth = Vector3.Distance(lastL, lastR);
            float tipLength = Vector3.Distance((lastL + lastR) / 2, tip);
            Assert.True(tipLength < tipWidth, $"blade {blade}: tip {tipLength} long on a {tipWidth} base is too pointed");
        }
    }
```

4. 找到：

```csharp
        MeshData far = GrassClump.Build(3, segments: 1, blades: 3);
        Assert.Equal(3 * 4, far.VertexCount);
        Assert.Equal(3 * 2, far.TriangleCount);
    }
```

   替换为：

```csharp
        MeshData far = GrassClump.Build(3, segments: 1, blades: 3);
        Assert.Equal(3 * 3, far.VertexCount);     // root pair + tip
        Assert.Equal(3 * 1, far.TriangleCount);
    }
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "^\s+失败|Failed |失败!|通过!"
```

Expected：4 个失败——`Clumps_have_five_to_seven_three_segment_blades`、`Blades_grow_from_the_ground_with_a_root_to_tip_gradient`、`Blades_are_wide_short_and_blunt_tipped`、`Clump_blade_and_segment_counts_are_tunable`。

- [ ] **Step 3: 草簇与草的着色器**

用以下内容整体替换 `src/Tank.Client/scripts/Art/Mesh/GrassClump.cs`：

```csharp
using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A tuft of 5–7 wide, short cartoon blades rooted at y = 0. Each blade is a strip of <c>segments</c> rows of vertex
/// pairs that closes in a single tip vertex; the rows bunch up towards the top and stay wide, so the tip is a short,
/// blunt triangle rather than a needle. Vertex colour R is the height along the blade (0 at the root, 1 at the tip)
/// for wind and shading; G is a per-blade shade in [0, 1]. Single-sided geometry; the grass shader draws both faces.
/// </summary>
public static class GrassClump
{
    public const int Segments = 3;
    public const int VerticesPerBlade = Segments * 2 + 1;
    public const int TrianglesPerBlade = Segments * 2 - 1;
    public const float MinHeight = 0.25f;
    public const float MaxHeight = 0.45f;

    private const float SpreadRadius = 0.2f;
    private const float RootWidth = 0.16f;
    private const float Taper = 0.45f;   // width lost by the top row, as a fraction of the root width

    /// <param name="segments">Segments per blade (default <see cref="Segments"/>); the last one is the tip triangle.</param>
    /// <param name="blades">Blade count; 0 (default) picks 5-7 from the seed.</param>
    public static MeshData Build(uint seed, int segments = Segments, int blades = 0)
    {
        if (segments < 1) throw new ArgumentOutOfRangeException(nameof(segments), "Need at least one segment.");
        if (blades < 0) throw new ArgumentOutOfRangeException(nameof(blades), "Blade count cannot be negative.");
        if (blades == 0) blades = 5 + (int)(SeededNoise.Hash(0, 0, 0, seed) % 3);
        var b = new MeshBuilder();
        for (int i = 0; i < blades; i++)
        {
            float R(int k) => SeededNoise.Hash01(i, k, 1, seed);
            float yaw = R(0) * MathF.Tau;
            float radius = MathF.Sqrt(R(1)) * SpreadRadius;
            float rootAngle = R(2) * MathF.Tau;
            var root = new Vector3(MathF.Cos(rootAngle) * radius, 0, MathF.Sin(rootAngle) * radius);
            float height = MinHeight + R(3) * (MaxHeight - MinHeight);
            float lean = 0.15f + R(4) * 0.45f;
            float shade = R(5);
            var facing = new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw));   // the way the blade bends
            var across = new Vector3(facing.Z, 0, -facing.X);               // blade width direction
            Vector3 Spine(float t) => root + Vector3.UnitY * (height * t * (1 - 0.25f * lean * t)) + facing * (lean * height * t * t);
            Vector3 Normal(float t)
            {
                Vector3 tangent = Vector3.Normalize(Vector3.UnitY * height + facing * (2 * lean * height * t) + Vector3.UnitY * 1e-3f);
                return Vector3.Normalize(Vector3.Cross(across, tangent) + Vector3.UnitY * 0.6f);
            }

            int first = b.VertexCount;
            for (int row = 0; row < segments; row++)
            {
                float t = MathF.Sin((float)row / segments * MathF.PI / 2);   // rows bunch up near the top: a short tip
                float width = RootWidth * (1 - Taper * t * t);
                var color = new Vector4(t, shade, 0, 1);
                b.AddVertex(Spine(t) - across * (width / 2), Normal(t), new Vector2(0, t), color);
                b.AddVertex(Spine(t) + across * (width / 2), Normal(t), new Vector2(1, t), color);
            }
            int tip = b.AddVertex(Spine(1f), Normal(1f), new Vector2(0.5f, 1f), new Vector4(1f, shade, 0, 1));
            for (int row = 0; row < segments - 1; row++)
            {
                int l0 = first + row * 2, r0 = l0 + 1, l1 = l0 + 2, r1 = l0 + 3;
                b.AddQuad(l0, r0, r1, l1);
            }
            int lastL = first + (segments - 1) * 2;
            b.AddTriangle(lastL, lastL + 1, tip);
        }
        return b.Build();
    }
}
```

用以下内容整体替换 `src/Tank.Client/shaders/grass.gdshader`：

```glsl
// Cartoon grass clumps (MultiMesh). Vertex colour R = height along the blade (0 root → 1 tip), G = per-blade shade.
// INSTANCE_CUSTOM: r = unused, g = wind phase, b = brightness variation.
// Blades go from the palette's dark grass at the root to its light grass at the tip, each clump a little lighter or
// darker. They sway in the wind (more towards the tip), lie flat under the tank, and shrink away near the edge of the
// grass radius.
shader_type spatial;
render_mode cull_disabled, world_vertex_coords;

#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 grass_dark : source_color = vec3(0.306, 0.604, 0.2);     // Palette.GrassDark
uniform vec3 grass_light : source_color = vec3(0.608, 0.851, 0.29);   // Palette.GrassLight
uniform float brightness_variation = 0.16;
uniform float blade_variation = 0.08;
uniform vec2 wind_direction = vec2(0.8, 0.6);
uniform float wind_strength = 0.08;     // metres of tip sway
uniform float wind_speed = 1.3;
uniform float fade_start = 66.0;        // metres from the streaming focus (the tank); GrassField.Radius is 90
uniform float fade_end = 84.0;
uniform float tank_margin = 0.7;        // blades lie flat inside the tank footprint and recover over this distance
uniform float flat_height = 0.1;        // fraction of its height a blade keeps under the tank

// Set every frame by GrassField (one tank for now; an array is the way to support several).
global uniform vec4 tank_footprint;     // centre x, centre z, half width, half length
global uniform vec4 tank_axes;          // right x, right z, forward x, forward z
global uniform vec2 grass_focus;

varying float height01;
varying float brightness;

void vertex() {
	height01 = COLOR.r;
	brightness = 1.0 + brightness_variation * (INSTANCE_CUSTOM.b - 0.5) * 2.0 + blade_variation * (COLOR.g - 0.5) * 2.0;
	vec3 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float fade = 1.0 - smoothstep(fade_start, fade_end, length(root.xz - grass_focus));

	float phase = dot(root.xz, wind_direction) * 0.18 + TIME * wind_speed + INSTANCE_CUSTOM.g * 6.2832;
	float gust = sin(phase) * 0.7 + sin(phase * 2.3 + 1.7) * 0.3;
	VERTEX.xz += normalize(wind_direction) * gust * wind_strength * height01 * height01;
	// Flatten blades inside the (grown) tank footprint, recovering smoothly outside it: signed distance to the box in the tank's frame.
	vec2 d = VERTEX.xz - tank_footprint.xy;
	vec2 local = vec2(dot(d, tank_axes.xy), dot(d, tank_axes.zw));
	vec2 q = abs(local) - tank_footprint.zw;
	float box_distance = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0);
	float standing = mix(flat_height, 1.0, smoothstep(0.0, tank_margin, box_distance));
	VERTEX.y = root.y + (VERTEX.y - root.y) * standing;
	VERTEX = root + (VERTEX - root) * fade;
}

void fragment() {
	ALBEDO = mix(grass_dark, grass_light, smoothstep(0.0, 1.0, height01)) * brightness;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
```

- [ ] **Step 4: 测试通过**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 136`。

- [ ] **Step 5: 编译、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
shot t4-front front
shot t4-close close
```

核对：
- 近处的草是一簇簇圆钝、宽叶的卡通草，根部深绿、叶尖亮黄绿，不再是细长尖叶。
- 草和地面的颜色衔接自然，没有一圈圈明显的色差。
- 坦克底下和履带两侧的草被压平，没有草叶穿过履带。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 6: 提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): wide blunt cartoon grass"
```

### Task 5：圆润岩石

岩石外形从"向立方体插值"改为超椭球（指数 3，处处光滑），去掉高频碎屑噪声，团块噪声减半；包围盒仍精确等于碰撞框，阶段 1.5 的贴合测试不改。着色器改成纯色 `Rock`，朝上的面过渡到 `RockTop`，加 0.25 的边缘光；碎石用同一着色器、亮 12%。

**Files:**
- Modify: `src/Tank.Client/scripts/Art/Mesh/RockShape.cs`
- Modify: `src/Tank.Client/scripts/World/RockMeshes.cs`
- Modify（整体替换）: `src/Tank.Client/shaders/rock.gdshader`
- Modify: `tests/Tank.Art.Tests/MeshAssert.cs`
- Modify: `tests/Tank.Art.Tests/RockFullnessTests.cs`

- [ ] **Step 1: 加"表面平滑"测试，更新固定值**

`MeshAssert.MaxCreaseDegrees` 求共享一条边的两个三角形的最大夹角。`Rock_vertex_golden_value` 的固定值换成新外形的值，所以它也会先失败。

在 `tests/Tank.Art.Tests/MeshAssert.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
    }
}
```

   替换为：

```csharp
    }

    /// <summary>Largest angle (degrees) between the face normals of two triangles that share an edge (after welding).</summary>
    public static float MaxCreaseDegrees(MeshData mesh)
    {
        int[] ids = WeldIds(mesh);
        var faces = new Dictionary<(int, int), Vector3>();
        float sharpest = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            int ia = mesh.Indices[3 * t], ib = mesh.Indices[3 * t + 1], ic = mesh.Indices[3 * t + 2];
            Vector3 normal = Vector3.Normalize(Vector3.Cross(mesh.Positions[ib] - mesh.Positions[ia], mesh.Positions[ic] - mesh.Positions[ia]));
            int a = ids[ia], b = ids[ib], c = ids[ic];
            foreach (var (u, v) in new[] { (a, b), (b, c), (c, a) })
            {
                if (faces.TryGetValue((v, u), out Vector3 other))
                    sharpest = MathF.Max(sharpest, MathF.Acos(Math.Clamp(Vector3.Dot(normal, other), -1f, 1f)) * 180f / MathF.PI);
                faces[(u, v)] = normal;
            }
        }
        return sharpest;
    }
}
```

在 `tests/Tank.Art.Tests/RockFullnessTests.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
    }

    [Fact]
    public void Elongated_rock_is_well_formed()
```

   替换为：

```csharp
    }

    /// <summary>Cartoon rocks are smooth lumps: neighbouring faces never meet at a sharp crease.</summary>
    [Theory]
    [MemberData(nameof(Boxes))]
    public void Rock_surface_bends_gently(Vector3 half)
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            float sharpest = MeshAssert.MaxCreaseDegrees(RockShape.Build(half, seed));
            Assert.True(sharpest < 50f, $"seed {seed}: crease of {sharpest:0.0}°");   // the stage-1.5 rocks reached 80–95°
        }
    }

    [Fact]
    public void Elongated_rock_is_well_formed()
```

2. 找到：

```csharp
        MeshData rock = RockShape.Build(new Vector3(2.0f, 1.6f, 1.2f), 42);
        MeshAssert.Near(new Vector3(-0.016365292f, 0.4061242f, -1.1733338f), rock.Positions[100]);
    }
```

   替换为：

```csharp
        MeshData rock = RockShape.Build(new Vector3(2.0f, 1.6f, 1.2f), 42);
        MeshAssert.Near(new Vector3(-0.013951308f, 0.4417045f, -1.1956836f), rock.Positions[100]);
    }
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Art.Tests --filter "FullyQualifiedName~Rock" 2>&1 | grep -E "^\s+失败|crease|失败!"
```

Expected：3 个失败——`Rock_surface_bends_gently` 的两组参数（夹角 80°～95°）和 `Rock_vertex_golden_value`。

- [ ] **Step 3: 岩石外形、着色器与材质**

在 `src/Tank.Client/scripts/Art/Mesh/RockShape.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// <summary>
/// Irregular boulder: an icosphere pushed part-way towards a cube, roughened with seeded noise, then scaled so its
/// bounding box is exactly ±<c>halfExtents</c>. The fit is exact, so the visible rock always matches the collision box.
/// </summary>
```

   替换为：

```csharp
/// <summary>
/// Smooth cartoon boulder: an icosphere blown out into a rounded box (a superellipsoid, so there are no creases), given
/// a few soft lumps by seeded noise, then scaled so its bounding box is exactly ±<c>halfExtents</c>. The fit is exact,
/// so the visible rock always matches the collision box.
/// </summary>
```

2. 找到：

```csharp
{
    private const float Squareness = 0.6f;
    private const float LumpAmplitude = 0.13f;
    private const float LumpFrequency = 1.6f;
    private const float ChipAmplitude = 0.05f;
    private const float ChipFrequency = 4.5f;

```

   替换为：

```csharp
{
    private const float Roundness = 3.0f;        // superellipsoid exponent: 2 = sphere, higher = boxier with rounded edges
    private const float LumpAmplitude = 0.06f;
    private const float LumpFrequency = 1.3f;

```

3. 找到：

```csharp
            Vector3 d = sphere.Positions[i];
            float maxAxis = MathF.Max(MathF.Abs(d.X), MathF.Max(MathF.Abs(d.Y), MathF.Abs(d.Z)));
            Vector3 boxy = Vector3.Lerp(d, d / maxAxis, Squareness);
            float bump = 1f + LumpAmplitude * SeededNoise.Fbm(d * LumpFrequency, seed, 3)
                            + ChipAmplitude * SeededNoise.Fbm(d * ChipFrequency, seed + 7919u, 2);
            raw[i] = boxy * bump;
```

   替换为：

```csharp
            Vector3 d = sphere.Positions[i];
            float norm = MathF.Pow(MathF.Pow(MathF.Abs(d.X), Roundness) + MathF.Pow(MathF.Abs(d.Y), Roundness) + MathF.Pow(MathF.Abs(d.Z), Roundness), 1f / Roundness);
            Vector3 boxy = d / norm;
            float bump = 1f + LumpAmplitude * SeededNoise.Fbm(d * LumpFrequency, seed, 2);
            raw[i] = boxy * bump;
```

在 `src/Tank.Client/scripts/World/RockMeshes.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// <summary>
/// One noise-shaped boulder per rock prop, sized to its collision box (so what you see is about what you hit) and
/// buried a little so it never floats, plus a few loose pebbles around each one in a single MultiMesh (no collision).
```

   替换为：

```csharp
/// <summary>
/// One smooth cartoon boulder per rock prop, sized to its collision box (so what you see is about what you hit) and
/// buried a little so it never floats, plus a few loose pebbles around each one in a single MultiMesh (no collision).
```

2. 找到：

```csharp
    private static readonly SN.Vector3 PebbleHalf = new(0.6f, 0.4f, 0.5f);

```

   替换为：

```csharp
    private static readonly SN.Vector3 PebbleHalf = new(0.6f, 0.4f, 0.5f);
    private const float RimStrength = 0.25f;
    private const float PebbleBrightness = 1.12f;

```

3. 找到：

```csharp
    {
        ShaderMaterial material = Materials.FromShader("rock").WithSet("rock", Materials.RockFace);
        var pebbles = new List<Transform3D>();
        // Loose pebbles read lighter (sun-bleached, no moss) so they stand out against grass.
        ShaderMaterial pebbleMaterial = Materials.FromShader("rock").WithSet("rock", Materials.RockFace)
            .With("tint", new Vector3(1.6659f, 1.5168f, 1.295f)).With("moss_amount", 0.0f).With("tile", 1.2f);
        foreach (Prop prop in props)
```

   替换为：

```csharp
    {
        ShaderMaterial material = Materials.FromShader("rock").With("toon_rim_strength", RimStrength);
        var pebbles = new List<Transform3D>();
        // Loose pebbles read a little lighter so they stand out against the grass.
        ShaderMaterial pebbleMaterial = Materials.FromShader("rock").With("brightness", PebbleBrightness);
        foreach (Prop prop in props)
```

用以下内容整体替换 `src/Tank.Client/shaders/rock.gdshader`：

```glsl
// Cartoon boulders and pebbles: one flat palette colour that lightens towards the top faces, a faint large mottling
// so big rocks aren't perfectly flat, and a sky-coloured rim light. No textures.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 rock_color : source_color = vec3(0.663, 0.706, 0.761);   // Palette.Rock
uniform vec3 rock_top : source_color = vec3(0.796, 0.835, 0.875);     // Palette.RockTop
uniform float brightness = 1.0;         // pebbles are a touch lighter
uniform float mottle_amount = 0.05;

varying vec3 world_pos;
varying vec3 world_normal;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	float top = smoothstep(0.35, 0.85, normalize(world_normal).y);
	float mottle = value_noise3(world_pos * 0.6) - 0.5;
	ALBEDO = mix(rock_color, rock_top, top) * brightness * (1.0 + mottle * 2.0 * mottle_amount);
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW);
}

```

- [ ] **Step 4: 测试通过**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 138`。`Rock_cross_sections_nearly_fill_the_collision_box` 不改也通过。

- [ ] **Step 5: 编译、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
shot t5-hill hill
shot t5-close close
```

核对：
- 岩石是圆润的大石头，像鹅卵石，没有棱角和细碎凹凸，也没有贴图。
- 颜色是浅蓝灰，顶面更亮，背光面带一点蓝紫；轮廓边缘有一圈淡淡的天空色边缘光。
- 岩石周围的碎石同色、稍亮。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 6: 提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): smooth cartoon rocks"
```

### Task 6：卡通树

阔叶树：暖棕色树干，树冠是 3～5 个轻微起伏的圆球，颜色 `Leaves`。针叶树：3～4 层圆锥叠起，每层边缘用 3 个点倒圆，颜色 `Needles`。每棵树有少量明暗和色相变化，底部稍暗（顶点色）。林带的分区、数量、可见距离都不变。

**Files:**
- Modify（整体替换）: `src/Tank.Client/scripts/Art/Mesh/TreeShapes.cs`
- Modify: `src/Tank.Client/shaders/tree.gdshader`
- Modify: `tests/Tank.Art.Tests/VegetationTests.cs`

- [ ] **Step 1: 写测试**

在 `tests/Tank.Art.Tests/VegetationTests.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
    [Fact]
    public void Trees_differ_by_seed()
```

   替换为：

```csharp
    [Fact]
    public void Broadleaf_crowns_are_three_to_five_round_blobs()
    {
        int blobVertices = Icosphere.Build(TreeShapes.BlobSubdivisions).VertexCount;
        var seen = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            MeshData tree = TreeShapes.Broadleaf(seed);
            int foliage = Enumerable.Range(0, tree.VertexCount).Count(i => tree.Colors[i].W > 0f);
            Assert.Equal(0, foliage % blobVertices);
            seen.Add(foliage / blobVertices);
        }
        Assert.Equal(new[] { 3, 4, 5 }, seen.OrderBy(n => n));
    }

    [Fact]
    public void Conifers_stack_three_or_four_tiers()
    {
        int tierVertices = TreeShapes.Tier(1f, 1f).VertexCount;
        var seen = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            MeshData tree = TreeShapes.Conifer(seed);
            int foliage = Enumerable.Range(0, tree.VertexCount).Count(i => tree.Colors[i].W > 0f);
            Assert.Equal(0, foliage % tierVertices);
            seen.Add(foliage / tierVertices);
        }
        Assert.Equal(new[] { 3, 4 }, seen.OrderBy(n => n));
    }

    [Fact]
    public void Tree_tiers_have_rounded_rims()
    {
        MeshData tier = TreeShapes.Tier(2f, 3f);
        MeshAssert.Closed(tier);
        Assert.True(MeshAssert.MaxCreaseDegrees(tier) < 70f, "the rim is bevelled, not a knife edge");
    }

    [Fact]
    public void Foliage_is_a_bright_green()
    {
        foreach (MeshData tree in new[] { TreeShapes.Conifer(2), TreeShapes.Broadleaf(2) })
        {
            Vector4 brightest = Enumerable.Range(0, tree.VertexCount).Where(i => tree.Colors[i].W > 0f)
                .Select(i => tree.Colors[i]).MaxBy(c => c.Y);
            Assert.True(brightest.Y > brightest.X && brightest.Y > brightest.Z, "green dominates");
            Assert.True(brightest.Y > 0.25f, $"crown top too dark: {brightest}");   // linear; the stage-1.5 pines topped out near 0.09
        }
    }

    [Fact]
    public void Trees_differ_by_seed()
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -3
```

Expected：编译失败，`TreeShapes` 没有 `BlobSubdivisions` 和 `Tier`。

- [ ] **Step 3: 树的网格与着色器**

用以下内容整体替换 `src/Tank.Client/scripts/Art/Mesh/TreeShapes.cs`：

```csharp
using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Cartoon background trees, rooted at the origin and about 7–9 m tall at scale 1, leaning a little.
/// Broadleaf: a warm brown trunk under 3–5 smooth, slightly lumpy green balls. Conifer: 3–4 stacked cones whose rims
/// are bevelled round. Vertex colour RGB is the base colour (linear) already darkened towards the ground (cheap
/// ambient occlusion); A is how much the part sways in the wind (0 trunk, up to 1 at the crown).
/// </summary>
public static class TreeShapes
{
    public const int BlobSubdivisions = 1;
    private const int TierSides = 8;
    private const int TrunkSides = 6;
    private const float TreeHeight = 8.5f;
    private const float MaxLean = 0.06f;
    private const float AoFloor = 0.6f;                     // brightness at the foot of the tree
    private const float BlobLumps = 0.06f;
    private static readonly Vector3 Bark = Palette.Linear(Palette.Bark);
    private static readonly Vector3 Needles = Palette.Linear(Palette.Needles);
    private static readonly Vector3 Leaves = Palette.Linear(Palette.Leaves);

    public static MeshData Conifer(uint seed)
    {
        float Rnd(int a, int b) => SeededNoise.Hash01(a, b, 0, seed);
        var parts = new List<(MeshData, Matrix4x4)> { Part(Trunk(0.24f, 2.4f), Bark, 1f, 0f, Matrix4x4.Identity) };
        int tiers = 3 + (int)(SeededNoise.Hash(9, 9, 9, seed) % 2);
        float y = 1.6f, radius = 2.6f, height = 3.4f;
        for (int tier = 0; tier < tiers; tier++)
        {
            float r = radius * (0.9f + 0.2f * Rnd(tier, 0));
            var place = Matrix4x4.CreateRotationY(Rnd(tier, 1) * MathF.Tau)
                * Matrix4x4.CreateTranslation((Rnd(tier, 3) - 0.5f) * 0.3f, y, (Rnd(tier, 4) - 0.5f) * 0.3f);
            parts.Add(Part(Tier(r, height * (0.9f + 0.2f * Rnd(tier, 5))), Needles, 0.9f + 0.2f * Rnd(tier, 6), y, place));
            y += height * 0.55f;
            radius *= 0.75f;
            height *= 0.85f;
        }
        return Lean(parts, seed);
    }

    public static MeshData Broadleaf(uint seed)
    {
        float Rnd(int a, int b) => SeededNoise.Hash01(a, b, 0, seed);
        var parts = new List<(MeshData, Matrix4x4)> { Part(Trunk(0.28f, 4.4f), Bark, 1f, 0f, Matrix4x4.Identity) };
        int blobs = 3 + (int)(SeededNoise.Hash(7, 7, 7, seed) % 3);
        for (int blob = 0; blob < blobs; blob++)
        {
            float r = blob == 0 ? 2.0f : 1.3f + 0.5f * Rnd(blob, 0);
            float angle = blob * MathF.Tau / Math.Max(blobs - 1, 1) + Rnd(blob, 1) * 0.8f;
            float spread = blob == 0 ? 0f : 1.2f + 0.4f * Rnd(blob, 3);
            float cy = blob == 0 ? 5.4f : 4.7f + 0.9f * Rnd(blob, 4);
            var offset = new Vector3(MathF.Cos(angle) * spread, cy, MathF.Sin(angle) * spread);
            parts.Add(Part(Blob(r, seed + (uint)blob * 31u), Leaves, 0.9f + 0.2f * Rnd(blob, 2), cy, Matrix4x4.CreateTranslation(offset)));
        }
        return Lean(parts, seed);
    }

    /// <summary>One conifer tier of base radius <paramref name="radius"/>: a cone whose rim is bevelled round.</summary>
    internal static MeshData Tier(float radius, float height) => Lathe.Build(new[]
    {
        new Vector2(0, 0), new Vector2(radius * 0.85f, 0.03f * height), new Vector2(radius * 0.98f, 0.09f * height),
        new Vector2(radius * 0.97f, 0.15f * height), new Vector2(radius * 0.85f, 0.24f * height), new Vector2(0, height),
    }, TierSides, smoothAngleDeg: 75f);

    /// <summary>A smooth, softly lumpy ball for a broadleaf crown.</summary>
    private static MeshData Blob(float radius, uint seed)
    {
        MeshData sphere = Icosphere.Build(BlobSubdivisions);
        var positions = new Vector3[sphere.VertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 d = sphere.Positions[i];
            positions[i] = d * radius * new Vector3(1f, 0.85f, 1f) * (1f + BlobLumps * SeededNoise.Fbm(d * 1.4f, seed, 2));
        }
        Vector3[] normals = RockShape.SmoothNormals(positions, sphere.Indices);
        var b = new MeshBuilder();
        for (int i = 0; i < positions.Length; i++) b.AddVertex(positions[i], normals[i], sphere.Uvs[i]);
        for (int t = 0; t < sphere.Indices.Count; t += 3) b.AddTriangle(sphere.Indices[t], sphere.Indices[t + 1], sphere.Indices[t + 2]);
        return b.Build();
    }

    /// <summary>Colours a part: base colour × brightness variation × height-based darkening.</summary>
    private static (MeshData, Matrix4x4) Part(MeshData mesh, Vector3 baseColor, float variation, float partY, Matrix4x4 place)
    {
        bool isBark = baseColor == Bark;
        Vector3 tinted = baseColor * (isBark ? 1f : variation);
        MeshData coloured = mesh.WithColors(p =>
        {
            float height = Math.Clamp((partY + p.Y) / TreeHeight, 0f, 1f);
            float ao = AoFloor + (1f - AoFloor) * height;
            return new Vector4(tinted * ao, isBark ? 0f : Math.Clamp(height, 0.15f, 1f));
        });
        return (coloured, place);
    }

    private static MeshData Lean(List<(MeshData Mesh, Matrix4x4 Transform)> parts, uint seed)
    {
        float tilt = (SeededNoise.Hash01(0, 0, 7, seed) - 0.5f) * 2f * MaxLean;
        float bearing = SeededNoise.Hash01(0, 1, 7, seed) * MathF.Tau;
        var lean = Matrix4x4.CreateFromAxisAngle(new Vector3(MathF.Cos(bearing), 0, MathF.Sin(bearing)), tilt);
        return Merge.Build(parts.Select(p => (p.Mesh, p.Transform * lean)));
    }

    private static MeshData Trunk(float radius, float height) =>
        Lathe.Build(new[] { new Vector2(radius, -0.3f), new Vector2(radius * 0.65f, height) }, TrunkSides);
}
```

在 `src/Tank.Client/shaders/tree.gdshader` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```glsl
// Background trees (MultiMesh). Vertex colour RGB = base colour (linear, already darkened towards the ground), A = how much that part sways.
// INSTANCE_CUSTOM: r = brightness variation, g = wind phase, b = hue shift (blue-green .. yellow-green).
shader_type spatial;
```

   替换为：

```glsl
// Cartoon background trees (MultiMesh). Vertex colour RGB = base colour (linear, already darker towards the ground),
// A = how much that part sways. INSTANCE_CUSTOM: r = brightness variation, g = wind phase, b = hue shift
// (a little bluer .. a little yellower), applied to foliage only.
shader_type spatial;
```

2. 找到：

```glsl

uniform float wind_strength = 0.04;     // fraction of height the crown leans in a gust
uniform float wind_speed = 0.7;
uniform float variation = 0.35;
uniform float hue_shift = 0.35;

varying vec3 base_color;
varying float foliage;

```

   替换为：

```glsl

#include "res://shaders/toon_light.gdshaderinc"

uniform float wind_strength = 0.03;     // fraction of height the crown leans in a gust
uniform float wind_speed = 0.7;
uniform float variation = 0.2;
uniform float hue_shift = 0.18;

varying vec3 base_color;

```

3. 找到：

```glsl
	float h = INSTANCE_CUSTOM.b - 0.5;
	vec3 hue = vec3(1.0 + hue_shift * h, 1.0 + 0.15 * h, 1.0 - hue_shift * h);
	base_color = COLOR.rgb * (1.0 - variation * 0.5 + variation * INSTANCE_CUSTOM.r) * mix(vec3(1.0), hue, step(0.01, COLOR.a));
	foliage = step(0.01, COLOR.a);
	float phase = TIME * wind_speed + INSTANCE_CUSTOM.g * 6.2832;
```

   替换为：

```glsl
	float h = INSTANCE_CUSTOM.b - 0.5;
	float foliage = step(0.01, COLOR.a);
	vec3 hue = vec3(1.0 + hue_shift * h, 1.0, 1.0 - hue_shift * h);
	base_color = COLOR.rgb * (1.0 - variation * 0.5 + variation * INSTANCE_CUSTOM.r) * mix(vec3(1.0), hue, foliage);
	float phase = TIME * wind_speed + INSTANCE_CUSTOM.g * 6.2832;
```

4. 找到：

```glsl
	ALBEDO = base_color;
	ROUGHNESS = 0.92;
	SPECULAR = 0.15;
	BACKLIGHT = base_color * 0.35 * foliage;
}
```

   替换为：

```glsl
	ALBEDO = base_color;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
```

- [ ] **Step 4: 测试通过**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 142`。阶段 1.5 的树测试（闭合、高度 6～10 m、三角形预算、底部更暗、树干不摆动）不改也通过。

- [ ] **Step 5: 编译、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
shot t6-front front
shot t6-side side
```

核对：
- 远处林带是明亮的卡通树：阔叶树是几团圆球树冠加棕色树干，针叶树是几层叠起的绿色圆锥。
- 树的颜色比地面略深、彼此有一点明暗差别，不再是暗沉的墨绿。
- 树和天空、地面之间没有突兀的黑边。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 6: 提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): cartoon trees"
```

### Task 7：卡通坦克

几何和动画不变，倒角加大约 50%（例外见"与设计的差异"）。新着色器 `tank_toon.gdshader`：物体空间的大块三色迷彩（`TankBase`、`TankCamoA`、`TankCamoB`），色块边界平滑；越往下越暗，接地处约 80%；轮毂比车体浅一档、胶圈用 `Track` 色；履带板直接用 `Track` 色；全部加 0.25 的边缘光。防盾、炮管用同一套材质（只多一个高度偏移）。

**Files:**
- Modify: `src/Tank.Client/scripts/Vehicles/TankLook.cs`
- Modify: `src/Tank.Client/scripts/Vehicles/TankLooks.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Vehicles/TankModel.cs`
- Modify: `src/Tank.Client/scripts/Vehicles/TankParts.cs`
- Delete: `src/Tank.Client/shaders/tank.gdshader`
- Create: `src/Tank.Client/shaders/tank_toon.gdshader`
- Modify: `tests/Tank.Art.Tests/TankLookTests.cs`

- [ ] **Step 1: 写测试**

在 `tests/Tank.Art.Tests/TankLookTests.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
using System.Numerics;
using Tank.Client.Art.Meshes;
```

   替换为：

```csharp
using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
```

2. 找到：

```csharp
            Assert.True(MeshAssert.SignedVolume(mesh) > 0, $"{name} is inside out");
        }
    }

```

   替换为：

```csharp
            Assert.True(MeshAssert.SignedVolume(mesh) > 0, $"{name} is inside out");
        }
    }

    [Fact]
    public void Camouflage_uses_the_palette()
    {
        Assert.Equal(new[] { Palette.TankBase, Palette.TankCamoA, Palette.TankCamoB }, TankLooks.Medium.Camo);
    }

    [Fact]
    public void Bevels_are_half_again_as_big_as_stage_one_point_five()
    {
        // Stage 1.5 used 0.04 / 0.05 / 0.015 / 0.05; the cartoon look rounds every edge about 50% more.
        Assert.Equal(0.06f, TankParts.HullBevel, 4);
        Assert.Equal(0.075f, TankParts.TurretBevel, 4);
        Assert.Equal(0.0225f, TankParts.DetailBevel, 4);
        Assert.Equal(0.075f, TankParts.MantletBevel, 4);
    }

```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -3
```

Expected：编译失败，`TankParts.HullBevel` 等不可访问（它们原来是 private）。

- [ ] **Step 3: 倒角、外观数据**

在 `src/Tank.Client/scripts/Vehicles/TankLook.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp

    /// <summary>Object-space height (hull / turret, metres) below which mud builds up on the paint.</summary>
    public required float MudHeight { get; init; }

    /// <summary>Same, measured from the axle of each wheel, for the running gear.</summary>
    public required float WheelMudHeight { get; init; }

    public required DetailPart[] Details { get; init; }

    /// <summary>Three camouflage colours (base, second, third), sRGB 0–1.</summary>
    public required SN.Vector3[] Camo { get; init; }
```

   替换为：

```csharp

    public required DetailPart[] Details { get; init; }

    /// <summary>Three camouflage colours (base, first and second blotch colour), sRGB 0–1; normally palette entries.</summary>
    public required SN.Vector3[] Camo { get; init; }
```

在 `src/Tank.Client/scripts/Vehicles/TankLooks.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
using SN = System.Numerics;
```

   替换为：

```csharp
using Tank.Client.Art;
using SN = System.Numerics;
```

2. 找到：

```csharp
        LowerHullWidth = 1.9f,
        UpperHull = new SN.Vector2[] { new(-3.3f, 1.28f), new(3.15f, 1.28f), new(3.15f, 1.55f), new(2.8f, 1.62f), new(-1.6f, 1.62f), new(-3.25f, 1.42f) },
        UpperHullWidth = 3.0f,
```

   替换为：

```csharp
        LowerHullWidth = 1.9f,
        UpperHull = new SN.Vector2[] { new(-3.3f, 1.28f), new(3.15f, 1.28f), new(3.15f, 1.55f), new(2.8f, 1.62f), new(-1.6f, 1.62f), new(-3.25f, 1.46f) },
        UpperHullWidth = 3.0f,
```

3. 找到：

```csharp
        RoadWheelWidth = 0.48f,
        MudHeight = 1.25f,
        WheelMudHeight = 0.62f,
        Details = new DetailPart[]
```

   替换为：

```csharp
        RoadWheelWidth = 0.48f,
        Details = new DetailPart[]
```

4. 找到：

```csharp
        },
        Camo = new SN.Vector3[] { new(0.33f, 0.36f, 0.22f), new(0.22f, 0.19f, 0.13f), new(0.56f, 0.51f, 0.37f) },
    };
```

   替换为：

```csharp
        },
        Camo = new[] { Palette.TankBase, Palette.TankCamoA, Palette.TankCamoB },
    };
```

在 `src/Tank.Client/scripts/Vehicles/TankParts.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
{
    private const float HullBevel = 0.04f;
    private const float TurretBevel = 0.05f;
    private const float DetailBevel = 0.015f;
    private const int WheelSides = 16;
```

   替换为：

```csharp
{
    // Cartoon look: every chamfer about 50% wider than stage 1.5 (0.04 / 0.05 / 0.015 / 0.05), so edges read as rounded.
    public const float HullBevel = 0.06f;
    public const float TurretBevel = 0.075f;
    public const float DetailBevel = 0.0225f;
    public const float MantletBevel = 0.075f;
    private const int WheelSides = 16;
```

2. 找到：

```csharp
    public static MeshData Mantlet(TankLook look) =>
        Box.Build(look.MantletSize, 0.05f).Transformed(M.CreateTranslation(0, 0, look.MantletBack - look.MantletSize.Z / 2));

```

   替换为：

```csharp
    public static MeshData Mantlet(TankLook look) =>
        Box.Build(look.MantletSize, MantletBevel).Transformed(M.CreateTranslation(0, 0, look.MantletBack - look.MantletSize.Z / 2));

```

3. 找到：

```csharp
        var parts = new List<(MeshData, M)> { (Wheel(radius * 0.86f, width * 0.8f, left: false), M.Identity) };
        MeshData tooth = Box.Build(new SN.Vector3(0.08f, 0.13f, 0.09f), 0.012f).WithColor(new SN.Vector4(0, 0, 0, 1));
        foreach (float x in new[] { -SprocketTeethX, SprocketTeethX })
```

   替换为：

```csharp
        var parts = new List<(MeshData, M)> { (Wheel(radius * 0.86f, width * 0.8f, left: false), M.Identity) };
        MeshData tooth = Box.Build(new SN.Vector3(0.08f, 0.13f, 0.09f), 0.018f).WithColor(new SN.Vector4(0, 0, 0, 1));
        foreach (float x in new[] { -SprocketTeethX, SprocketTeethX })
```

4. 找到：

```csharp
        return Merge.Build(
            (Box.Build(new SN.Vector3(look.TrackWidth, t, look.LinkPitch * 0.86f), 0.01f), M.Identity),
            (Box.Build(new SN.Vector3(look.TrackWidth * 0.95f, 0.025f, 0.03f), 0.006f), M.CreateTranslation(0, t / 2 + 0.012f, 0)),
            (Box.Build(new SN.Vector3(0.06f, 0.07f, look.LinkPitch * 0.5f), 0.008f), M.CreateTranslation(0, -(t / 2 + 0.035f), 0)));
    }
```

   替换为：

```csharp
        return Merge.Build(
            (Box.Build(new SN.Vector3(look.TrackWidth, t, look.LinkPitch * 0.86f), 0.015f), M.Identity),
            (Box.Build(new SN.Vector3(look.TrackWidth * 0.95f, 0.025f, 0.03f), 0.006f), M.CreateTranslation(0, t / 2 + 0.012f, 0)),
            (Box.Build(new SN.Vector3(0.06f, 0.07f, look.LinkPitch * 0.5f), 0.012f), M.CreateTranslation(0, -(t / 2 + 0.035f), 0)));
    }
```

- [ ] **Step 4: 测试通过**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 144`。所有部件仍是闭合、朝外的实体（上车体后板的修改就是为了让 0.06 的倒角放得下）。

- [ ] **Step 5: 坦克着色器与模型**

用以下内容整体替换 `src/Tank.Client/scripts/Vehicles/TankModel.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Client.World;
using Tank.Sim.Vehicles;

namespace Tank.Client.Vehicles;

/// <summary>
/// Procedural cartoon tank built from its <see cref="TankLook"/>, painted with tank_toon.gdshader. Node layout keeps the stage-1 contract: the root sits at the
/// hull origin on the ground, "Turret" yaws, "Gun" pitches, the barrel points −Z. Below that:
/// Body (visual nod) → Hull; Turret (a sibling of Body, so nod never tilts the gun) → Gun → Mantlet, Barrel (recoil), Muzzle; Running gear → Wheels, Tracks.
/// </summary>
public partial class TankModel : Node3D
{
    private const float RimStrength = 0.25f;
    private const int PaintMode = 0, WheelMode = 1, TrackMode = 2;   // tank_toon.gdshader "mode"

    private readonly Node3D _body = new() { Name = "Body" };
    private readonly Node3D _turret = new() { Name = "Turret" };
    private readonly Node3D _gun = new() { Name = "Gun" };
    private readonly Node3D _barrel = new() { Name = "Barrel" };
    private readonly Marker3D _muzzle = new() { Name = "Muzzle" };
    private readonly Node3D _wheels = new() { Name = "Wheels" };
    private readonly MultiMeshInstance3D _tracks = new() { Name = "Tracks" };
    private readonly List<WheelNode> _wheelNodes = new();
    private readonly record struct WheelNode(Node3D Node, float Radius, bool Left, float Phase);
    private MultiMesh _linkMesh = null!;
    private float[] _linkBuffer = Array.Empty<float>();
    private float _placedLeft = float.NaN;
    private float _placedRight = float.NaN;
    private TankLook _look = null!;
    private BeltPath _belt = null!;
    private int _linksPerSide;
    private Vector3 _nodPivot;
    private Vector3 _turretBase;

    public TankAnimator Animator { get; private set; } = null!;

    /// <summary>Barrel tip in world space, for muzzle effects.</summary>
    public Transform3D MuzzleTransform => _muzzle.GlobalTransform;

    public void Build(TankSpec spec)
    {
        _look = TankLooks.For(spec.Id);
        Animator = new TankAnimator(_look.TrackX);
        _nodPivot = new Vector3(0, _look.RoadWheelY, 0);
        ShaderMaterial paint = ToonMaterial(_look, PaintMode);
        // Same paint on the gun; its origin is the trunnion, so lift it by that height for the bottom-to-top shading.
        ShaderMaterial gunPaint = ToonMaterial(_look, PaintMode).With("height_offset", spec.TrunnionHeight);

        AddChild(_body);
        _body.AddChild(new MeshInstance3D { Name = "Hull", Mesh = TankParts.Hull(_look).ToArrayMesh(), MaterialOverride = paint });

        _turretBase = new Vector3(0, 0, -spec.TurretOffset);
        _turret.Position = _turretBase;
        AddChild(_turret); // sibling of Body: the hull nods under it, but the gun line stays exactly the sim's
        _turret.AddChild(new MeshInstance3D { Name = "TurretShell", Mesh = TankParts.Turret(_look).ToArrayMesh(), MaterialOverride = paint });

        _gun.Position = new Vector3(0, spec.TrunnionHeight, _look.GunPivotZ);
        _turret.AddChild(_gun);
        _gun.AddChild(new MeshInstance3D { Name = "Mantlet", Mesh = TankParts.Mantlet(_look).ToArrayMesh(), MaterialOverride = gunPaint });
        _gun.AddChild(_barrel);
        float barrelLength = spec.GunLength + _look.GunPivotZ;
        _barrel.AddChild(new MeshInstance3D { Mesh = TankParts.Barrel(_look, barrelLength).ToArrayMesh(), MaterialOverride = gunPaint });
        _muzzle.Position = new Vector3(0, 0, -barrelLength);
        _gun.AddChild(_muzzle);

        AddChild(_wheels);
        BuildWheels();
        AddChild(_tracks);
        BuildTracks(ToonMaterial(_look, TrackMode));
    }

    public void Apply(TankPose pose, float turretYaw, float gunPitch)
    {
        GlobalTransform = new Transform3D(new Basis(pose.Rotation.ToGodot()), pose.Position.ToGodot());
        _turret.Rotation = new Vector3(0, turretYaw, 0);
        _gun.Rotation = new Vector3(gunPitch, 0, 0);
    }

    /// <summary>Kicks the barrel back; it returns over <see cref="TankAnimator.RecoilReturnTime"/>.</summary>
    public void Fire(float turretYaw) => Animator.Fire(turretYaw);

    /// <summary>Rolls wheels and tracks, nods the hull and runs recoil. Call once per frame after <see cref="Apply"/>.</summary>
    public void Animate(float speed, float yawRate, float delta)
    {
        Animator.Step(speed, yawRate, delta);
        // The odometer grows without bound; wrap the wheel angle (2π) and the belt offset (belt length) separately, each on its own period.
        foreach (WheelNode wheel in _wheelNodes)
        {
            float rolled = wheel.Left ? Animator.LeftDistance : Animator.RightDistance;
            wheel.Node.Rotation = new Vector3(wheel.Phase - Mathf.PosMod(rolled / wheel.Radius, Mathf.Tau), 0, 0);
        }
        if (IsVisibleInTree()) PlaceLinks(Mathf.PosMod(Animator.LeftDistance, _belt.Length), Mathf.PosMod(Animator.RightDistance, _belt.Length));
        var nod = new Basis(Vector3.Right, Animator.Nod);
        _body.Transform = new Transform3D(nod, _nodPivot - nod * _nodPivot);
        // Anchor at ring height (where the turret sits on the hull) so the nod moves it as the ring moves; only the position follows, orientation stays level.
        var ring = Vector3.Up * _look.TurretBase;
        _turret.Position = _body.Transform * (_turretBase + ring) - ring;
        _barrel.Position = new Vector3(0, 0, Animator.Recoil);
    }

    private static ShaderMaterial ToonMaterial(TankLook look, int mode) =>
        Materials.FromShader("tank_toon")
            .With("mode", mode)
            .With("camo_base", look.Camo[0].ToColor())
            .With("camo_a", look.Camo[1].ToColor())
            .With("camo_b", look.Camo[2].ToColor())
            .With("track_color", Palette.Track.ToColor())
            .With("toon_rim_strength", RimStrength);

    private void BuildWheels()
    {
        float halfThickness = _look.TrackThickness / 2;
        ShaderMaterial wheelMaterial = ToonMaterial(_look, WheelMode);   // lighter hub, track-coloured tyre (vertex-colour mask)
        _belt = new BeltPath(TankParts.TrackLoop(_look));
        _linksPerSide = (int)MathF.Round(_belt.Length / _look.LinkPitch);
        int teeth = TankParts.SprocketTeethCount(_look);
        float linkSpacing = _belt.Length / _linksPerSide;
        // The wheels roll on the belt's centre line (radius + half the link thickness). The sprocket instead turns exactly one
        // tooth per link, so its teeth never drift out of mesh with the links as the odometer grows.
        float sprocketRadius = teeth * linkSpacing / MathF.Tau;
        foreach (bool left in new[] { true, false })
        {
            float x = left ? -_look.TrackX : _look.TrackX;
            ArrayMesh road = TankParts.Wheel(_look.RoadWheelRadius, _look.RoadWheelWidth, left).ToArrayMesh();
            for (int i = 0; i < _look.RoadWheelCount; i++)
                AddWheel(road, new Vector3(x, _look.RoadWheelY, _look.RoadWheelZ(i)), _look.RoadWheelRadius + halfThickness, left, 0f, wheelMaterial);
            WheelSpot s = _look.Sprocket, d = _look.Idler;
            AddWheel(TankParts.Sprocket(s.Radius, _look.RoadWheelWidth, left, teeth).ToArrayMesh(), new Vector3(x, s.Y, s.Z), sprocketRadius, left, SprocketPhase(s, linkSpacing), wheelMaterial);
            AddWheel(TankParts.Wheel(d.Radius, _look.RoadWheelWidth * 0.8f, left).ToArrayMesh(), new Vector3(x, d.Y, d.Z), d.Radius + halfThickness, left, 0f, wheelMaterial);
        }
    }

    /// <summary>
    /// Wheel angle at which a sprocket tooth sits in a gap between two links (the belt offset 0 layout puts link i at
    /// arc length i·spacing, so gaps are at half-steps), measured like the tooth angles: from +Y towards +Z.
    /// </summary>
    private float SprocketPhase(WheelSpot sprocket, float linkSpacing)
    {
        var centre = new System.Numerics.Vector2(sprocket.Z, sprocket.Y);
        var top = centre + new System.Numerics.Vector2(0, sprocket.Radius + _look.TrackThickness / 2);
        float bestArc = 0, best = float.MaxValue;
        for (int i = 0; i < 2000; i++)
        {
            float arc = _belt.Length * i / 2000f;
            float distance = System.Numerics.Vector2.DistanceSquared(_belt.Sample(arc).Position, top);
            if (distance < best) (best, bestArc) = (distance, arc);
        }
        float gapArc = (MathF.Floor(bestArc / linkSpacing) + 0.5f) * linkSpacing;
        System.Numerics.Vector2 offset = _belt.Sample(gapArc).Position - centre;
        return MathF.Atan2(offset.X, offset.Y);
    }

    private void AddWheel(Mesh mesh, Vector3 position, float rollRadius, bool left, float phase, Material material)
    {
        var node = new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = position };
        _wheels.AddChild(node);
        _wheelNodes.Add(new WheelNode(node, rollRadius, left, phase));
    }

    private void BuildTracks(Material material)
    {
        _linkMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = TankParts.TrackLink(_look).ToArrayMesh(),
            InstanceCount = _linksPerSide * 2,
            CustomAabb = TrackBounds(),
        };
        _linkBuffer = new float[_linksPerSide * 2 * LinkFloats];
        _tracks.Multimesh = _linkMesh;
        _tracks.MaterialOverride = material;
        PlaceLinks(0, 0);
    }

    /// <summary>Static bounds of both tracks, so the engine never recomputes them as the links move.</summary>
    private Aabb TrackBounds()
    {
        float minZ = float.MaxValue, maxZ = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 400; i++)
        {
            var p = _belt.Sample(_belt.Length * i / 400f).Position;
            minZ = MathF.Min(minZ, p.X); maxZ = MathF.Max(maxZ, p.X); maxY = MathF.Max(maxY, p.Y);
        }
        const float Margin = 0.15f; // grousers, guide horns, link size
        float halfWidth = _look.TrackX + _look.TrackWidth / 2 + Margin;
        return new Aabb(new Vector3(-halfWidth, 0, minZ - Margin), new Vector3(2 * halfWidth, maxY + Margin, maxZ - minZ + 2 * Margin));
    }

    private const int LinkFloats = 12; // Transform3D multimesh layout: three rows of (basis row, origin component)

    private void PlaceLinks(float leftOffset, float rightOffset)
    {
        if (leftOffset == _placedLeft && rightOffset == _placedRight) return;
        (_placedLeft, _placedRight) = (leftOffset, rightOffset);
        float spacing = _belt.Length / _linksPerSide;
        for (int side = 0; side < 2; side++)
        {
            float x = side == 0 ? -_look.TrackX : _look.TrackX;
            float rolled = side == 0 ? leftOffset : rightOffset;
            for (int i = 0; i < _linksPerSide; i++)
            {
                var (p, t) = _belt.Sample(rolled + i * spacing); // (z, y) position and direction of travel
                var outward = new Vector3(0, -t.X, t.Y);          // right of travel on a counter-clockwise loop
                Vector3 across = Vector3.Right, normal = Vector3.Right.Cross(outward);
                int o = (side * _linksPerSide + i) * LinkFloats;
                _linkBuffer[o + 0] = across.X; _linkBuffer[o + 1] = outward.X; _linkBuffer[o + 2] = normal.X; _linkBuffer[o + 3] = x;
                _linkBuffer[o + 4] = across.Y; _linkBuffer[o + 5] = outward.Y; _linkBuffer[o + 6] = normal.Y; _linkBuffer[o + 7] = p.Y;
                _linkBuffer[o + 8] = across.Z; _linkBuffer[o + 9] = outward.Z; _linkBuffer[o + 10] = normal.Z; _linkBuffer[o + 11] = p.X;
            }
        }
        _linkMesh.Buffer = _linkBuffer;
    }
}
```

删除 `src/Tank.Client/shaders/tank.gdshader` 和它的 `.uid`：

```bash
git rm src/Tank.Client/shaders/tank.gdshader src/Tank.Client/shaders/tank.gdshader.uid
```

创建 `src/Tank.Client/shaders/tank_toon.gdshader`：

```glsl
// Cartoon tank paint. Big three-colour camouflage blotches computed in object space (they move with the part), with
// soft edges between colours; the paint darkens towards the bottom of the hull (about 80% at the ground) instead of
// realistic wear and mud; a sky-coloured rim light. Modes:
//   0 paint  — hull, turret, mantlet, barrel (height_offset lifts parts whose origin isn't on the ground, e.g. the gun);
//   1 wheel  — hub one step lighter than the hull, rubber tyre in the track colour (vertex colour R: 1 hub, 0 tyre);
//   2 track  — plain track colour.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 camo_base : source_color = vec3(0.431, 0.545, 0.239);   // Palette.TankBase
uniform vec3 camo_a : source_color = vec3(0.788, 0.698, 0.42);       // Palette.TankCamoA
uniform vec3 camo_b : source_color = vec3(0.247, 0.353, 0.165);      // Palette.TankCamoB
uniform vec3 track_color : source_color = vec3(0.29, 0.29, 0.333);   // Palette.Track
uniform int mode = 0;
uniform float camo_scale = 0.38;         // noise features per metre: a few big blotches per side
uniform float camo_edge = 0.035;         // width of the soft edge between two colours (noise units)
uniform float bottom_shade = 0.8;        // brightness at the ground
uniform float full_height = 1.7;         // object-space height (plus height_offset) where the paint reaches full brightness
uniform float height_offset = 0.0;
uniform float hub_lighten = 1.18;
uniform float wheel_shade = 0.86;        // the running gear sits low, so it gets roughly the bottom of the gradient

varying vec3 object_pos;

void vertex() {
	object_pos = VERTEX;
}

void fragment() {
	vec3 albedo;
	if (mode == 2) {
		albedo = track_color;
	} else if (mode == 1) {
		albedo = mix(track_color, camo_base * hub_lighten, COLOR.r) * wheel_shade;
	} else {
		vec3 p = object_pos * camo_scale;
		float a = smoothstep(0.58 - camo_edge, 0.58 + camo_edge, fbm3(p + vec3(3.1, 0.0, 7.7)));
		float b = smoothstep(0.52 - camo_edge, 0.52 + camo_edge, fbm3(p * 1.15 + vec3(11.3, 5.2, 1.9)));
		albedo = mix(mix(camo_base, camo_b, b), camo_a, a);
		albedo *= mix(bottom_shade, 1.0, smoothstep(0.2, full_height, object_pos.y + height_offset));
	}
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW);
}
```

- [ ] **Step 6: 编译、导入、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

```bash
shot t7-close close
shot t7-closerear closerear
shot t7-side side
```

核对：
- 坦克是橄榄绿底色加大块沙黄、深绿迷彩，色块边缘柔和，没有掉漆、泥污、金属反光。
- 车体下部明显比上部暗一些；负重轮轮毂比车体浅、外圈胶圈和履带是深蓝灰色。
- 棱角是明显的圆角，看起来像玩具坦克，不像塑料方块；轮廓有淡淡的边缘光。
- 炮管、防盾的颜色和车体一致，没有发黑。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 7: 提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): toon tank paint and rounder bevels"
```

### Task 8：素材——删除 Poly Haven，加入字体

此时已经没有着色器采样贴图。删除 13 个照片素材及其 `.import`、下载脚本，`Materials` 只保留加载着色器和设参数，`common.gdshaderinc` 只保留噪声。加入站酷快乐体及其 OFL 许可证。

**Files:**
- Delete: `src/Tank.Client/assets/textures/`、`src/Tank.Client/assets/hdri/`（全部）、`tools/fetch_assets.py`
- Create: `src/Tank.Client/assets/fonts/ZCOOLKuaiLe-Regular.ttf`、`src/Tank.Client/assets/fonts/OFL.txt`（下载）
- Modify（整体替换）: `src/Tank.Client/assets/CREDITS.md`、`src/Tank.Client/scripts/Art/Materials.cs`、`src/Tank.Client/shaders/common.gdshaderinc`

- [ ] **Step 1: 确认没有代码再引用贴图**

```bash
grep -rn "assets/textures\|assets/hdri\|WithSet\|Materials.Map\|far_sample\|unpack_normal\|bend_normal" src/Tank.Client/scripts src/Tank.Client/shaders
```

Expected：只有 `Materials.cs` 自身和 `common.gdshaderinc` 自身的定义行，没有调用。

- [ ] **Step 2: 删除照片素材和下载脚本**

```bash
git rm -rq src/Tank.Client/assets/textures src/Tank.Client/assets/hdri tools/fetch_assets.py
```

- [ ] **Step 3: 下载字体并校验**

```bash
mkdir -p src/Tank.Client/assets/fonts
C=66928797f271fed5ee428544e7229685e2d90127
curl -sSLf -o src/Tank.Client/assets/fonts/ZCOOLKuaiLe-Regular.ttf https://raw.githubusercontent.com/google/fonts/$C/ofl/zcoolkuaile/ZCOOLKuaiLe-Regular.ttf
curl -sSLf -o src/Tank.Client/assets/fonts/OFL.txt https://raw.githubusercontent.com/google/fonts/$C/ofl/zcoolkuaile/OFL.txt
shasum -a 256 src/Tank.Client/assets/fonts/*
head -3 src/Tank.Client/assets/fonts/OFL.txt
```

Expected：
- `812a6fc1fe54b6d73a419245c32dfeba8aa33104d5be90d1cf6af082007cb71d  …/ZCOOLKuaiLe-Regular.ttf`（约 1.5 MB）
- `538078469839b4a2e7ad22bef4ebe41681a4e53749bb2a072144024f1d6d703d  …/OFL.txt`
- 许可证第一行是 `Copyright 2018 The ZCOOL KuaiLe Project Authors (https://github.com/googlefonts/zcool-kuaile)`，第三行是 `This Font Software is licensed under the SIL Open Font License, Version 1.1.`，版权行里没有 "Reserved Font Name"。

校验不一致就停下汇报，不要换别的来源。

- [ ] **Step 4: 署名、材质工具、噪声头文件**

用以下内容整体替换 `src/Tank.Client/assets/CREDITS.md`：

```markdown
# 素材署名

画面全部由代码程序化生成，不使用照片贴图。唯一的外部素材是界面字体：

| 用途 | 素材 | 作者 | 许可证 | 来源 | 本地文件 |
|---|---|---|---|---|---|
| 界面字体 | 站酷快乐体（ZCOOL KuaiLe） | Liu Bingke、Yang Kang、Wu Shaojie（The ZCOOL KuaiLe Project Authors） | [SIL Open Font License 1.1](fonts/OFL.txt) | https://github.com/googlefonts/zcool-kuaile ，文件取自 [google/fonts](https://github.com/google/fonts/tree/66928797f271fed5ee428544e7229685e2d90127/ofl/zcoolkuaile) | `fonts/ZCOOLKuaiLe-Regular.ttf` |

OFL 允许随软件一起使用、修改和再分发，包括商用；不允许单独出售字体文件本身。字体与 `fonts/OFL.txt` 必须一起分发。

SHA-256：
- `ZCOOLKuaiLe-Regular.ttf`：`812a6fc1fe54b6d73a419245c32dfeba8aa33104d5be90d1cf6af082007cb71d`
- `OFL.txt`：`538078469839b4a2e7ad22bef4ebe41681a4e53749bb2a072144024f1d6d703d`
```

用以下内容整体替换 `src/Tank.Client/scripts/Art/Materials.cs`：

```csharp
using Godot;

namespace Tank.Client.Art;

/// <summary>Loads the cartoon shaders into materials and sets their parameters.</summary>
public static class Materials
{
    public static ShaderMaterial FromShader(string name) => new() { Shader = GD.Load<Shader>($"res://shaders/{name}.gdshader") };

    public static ShaderMaterial With(this ShaderMaterial material, string name, Variant value)
    {
        material.SetShaderParameter(name, value);
        return material;
    }
}
```

用以下内容整体替换 `src/Tank.Client/shaders/common.gdshaderinc`：

```glsl
// Shared noise for the cartoon shaders: cheap hash-based value noise and fbm.
// The noise is for looks only; anything the game logic must agree with comes from C# (GroundCover mirrors hash12 and
// value_noise2 float for float, so keep the two in step).

float hash12(vec2 p) {
	vec3 p3 = fract(vec3(p.xyx) * 0.1031);
	p3 += dot(p3, p3.yzx + 33.33);
	return fract((p3.x + p3.y) * p3.z);
}

float hash13(vec3 p) {
	p = fract(p * 0.1031);
	p += dot(p, p.zyx + 31.32);
	return fract((p.x + p.y) * p.z);
}

// Smooth noise in [0, 1], one feature per unit.
float value_noise2(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	float a = hash12(i);
	float b = hash12(i + vec2(1.0, 0.0));
	float c = hash12(i + vec2(0.0, 1.0));
	float d = hash12(i + vec2(1.0, 1.0));
	return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float value_noise3(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	vec3 u = f * f * (3.0 - 2.0 * f);
	float n000 = hash13(i);
	float n100 = hash13(i + vec3(1.0, 0.0, 0.0));
	float n010 = hash13(i + vec3(0.0, 1.0, 0.0));
	float n110 = hash13(i + vec3(1.0, 1.0, 0.0));
	float n001 = hash13(i + vec3(0.0, 0.0, 1.0));
	float n101 = hash13(i + vec3(1.0, 0.0, 1.0));
	float n011 = hash13(i + vec3(0.0, 1.0, 1.0));
	float n111 = hash13(i + vec3(1.0, 1.0, 1.0));
	float x00 = mix(n000, n100, u.x);
	float x10 = mix(n010, n110, u.x);
	float x01 = mix(n001, n101, u.x);
	float x11 = mix(n011, n111, u.x);
	return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
}

// Four octaves, still in [0, 1].
float fbm2(vec2 p) {
	float sum = 0.0;
	float weight = 0.5;
	for (int i = 0; i < 4; i++) {
		sum += value_noise2(p) * weight;
		p = p * 2.03 + vec2(17.1, 9.3);
		weight *= 0.5;
	}
	return sum / 0.9375;
}

float fbm3(vec3 p) {
	float sum = 0.0;
	float weight = 0.5;
	for (int i = 0; i < 3; i++) {
		sum += value_noise3(p) * weight;
		p = p * 2.03 + vec3(17.1, 9.3, 4.7);
		weight *= 0.5;
	}
	return sum / 0.875;
}
```

- [ ] **Step 5: 编译、导入、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
ls src/Tank.Client/assets/fonts/
```

Expected：多出 `ZCOOLKuaiLe-Regular.ttf.import`（`importer="font_data_dynamic"`）。

```bash
shot t8-front front
```

画面与 Task 7 相同，输出里没有 `ERROR`（没有找不到资源的报错）。字体在 Task 10 才用上。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 6: 提交**

```bash
git add -A src/Tank.Client/assets src/Tank.Client/scripts src/Tank.Client/shaders tools
git commit -m "chore(assets): drop Poly Haven assets, bundle ZCOOL KuaiLe font"
git show --stat HEAD | tail -3
```

### Task 9：卡通特效

结构不变（对象池：炮口 4 套、落点 6 套；扬尘挂在坦克上；曳光池），观感按设计 §5.8 重做：
- 开炮：明黄色发光球闪 0.08 s；3～5 个白色烟团沿炮管方向先后喷出，"鼓起—回弹—缩小"，约 0.8 s。
- 落点：橙黄火球快速鼓起再塌缩；6～10 个白灰烟团向外滚开再缩小，打在岩石上偏灰；8 块小石头受重力飞散。
- 履带扬尘：车速超过 10 km/h 时，履带后方不断冒出浅棕色小烟团，越快越多。
- 曳光：亮黄色头部加 7 m 的短拖尾。
- 镜头震动：已有（0.08 m、0.15 s），不变。

烟团、尘土、碎石都是不透明的卡通小球，靠缩放消失，不需要半透明排序。

**Files:**
- Modify（整体替换）: `src/Tank.Client/scripts/Combat/BattleFx.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Combat/DustTrail.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Combat/ParticleKit.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Combat/ShellViews.cs`
- Create: `src/Tank.Client/shaders/puff.gdshader`

- [ ] **Step 1: 特效着色器与工具**

用以下内容整体替换 `src/Tank.Client/scripts/Combat/ParticleKit.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// Shared building blocks for the cartoon effects: one low-poly ball mesh drawn with the toon puff shader (opaque, coloured
/// per particle), glowing materials for flashes, and "pop" scale curves (swell past full size, settle, shrink to nothing).
/// </summary>
public static class ParticleKit
{
    /// <summary>Particles fly far from their emitter (LocalCoords off), so never cull them by the default small box.</summary>
    public static readonly Aabb WideBounds = new(new Vector3(-30, -10, -30), new Vector3(60, 40, 60));

    private static readonly Lazy<ShaderMaterial> PuffShader = new(() => Materials.FromShader("puff"));

    /// <summary>Unit-diameter ball for puffs and fireballs; coarse on purpose (cartoon, and cheap in numbers).</summary>
    public static SphereMesh Ball(Material material) => new() { Radius = 0.5f, Height = 1f, RadialSegments = 12, Rings = 6, Material = material };

    /// <summary>Toon-lit opaque material that takes its colour from the particle (ParticleProcessMaterial.Color / ColorRamp).</summary>
    public static Material Puff => PuffShader.Value;

    /// <summary>Bright, glowing material for flashes and fireballs; energy above the glow threshold so bloom picks it up.</summary>
    public static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
        DisableReceiveShadows = true,
        Roughness = 1f,
    };

    /// <summary>
    /// Scale over a particle's life: from <paramref name="start"/> it swells to <paramref name="overshoot"/> at
    /// <paramref name="peakAt"/>, springs back to 1, holds, then shrinks to nothing. Reads as a springy cartoon "pop".
    /// </summary>
    public static CurveTexture Pop(float start = 0.15f, float overshoot = 1.25f, float peakAt = 0.18f, float shrinkFrom = 0.55f)
    {
        var curve = new Curve { MaxValue = overshoot };
        curve.AddPoint(new Vector2(0f, start));
        curve.AddPoint(new Vector2(peakAt, overshoot));
        curve.AddPoint(new Vector2(peakAt * 1.8f, 0.92f));
        curve.AddPoint(new Vector2(peakAt * 2.6f, 1f));
        curve.AddPoint(new Vector2(shrinkFrom, 1f));
        curve.AddPoint(new Vector2(1f, 0f));
        return new CurveTexture { Curve = curve };
    }

    public static GpuParticles3D OneShot(int amount, double lifetime, float explosiveness, ParticleProcessMaterial process, Mesh drawPass) => new()
    {
        Amount = amount,
        Lifetime = lifetime,
        OneShot = true,
        Explosiveness = explosiveness,
        Emitting = false,
        LocalCoords = false,
        ProcessMaterial = process,
        DrawPass1 = drawPass,
        VisibilityAabb = WideBounds,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
    };
}
```

创建 `src/Tank.Client/shaders/puff.gdshader`：

```glsl
// Cartoon smoke, dust and debris particles: opaque, toon-lit balls coloured by the particle colour. They never fade;
// their scale curve pops them up and shrinks them away instead, so there is no transparency to sort.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/toon_light.gdshaderinc"

void fragment() {
	ALBEDO = COLOR.rgb;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
```

- [ ] **Step 2: 炮口、落点、扬尘、曳光**

用以下内容整体替换 `src/Tank.Client/scripts/Combat/BattleFx.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// Cartoon muzzle blasts and shell impacts, built once and reused round-robin (no allocation per shot).
/// Muzzle: a bright yellow flash ball (0.08 s), then 3–5 white puffs pushed out along the barrel one after another,
/// each swelling past full size, settling and shrinking away (~0.8 s).
/// Impact: an orange fireball that balloons and collapses, 6–10 white-grey puffs rolling outward (greyer on rock) and
/// 8 small stones thrown up under gravity.
/// </summary>
public partial class BattleFx : Node3D
{
    private const int MuzzleSlots = 4;
    private const int ImpactSlots = 6;
    private const float MuzzleFlashTime = 0.08f;
    private const float MuzzleFlashSize = 1.3f;
    private const float FireballGrowTime = 0.1f;
    private const float FireballShrinkTime = 0.22f;
    private const float FireballSize = 3.4f;
    private const int MuzzlePuffs = 5;    // 3–5 used per shot
    private const int ImpactPuffs = 10;   // 6–10 used per impact
    private const int Stones = 8;

    private readonly List<MuzzleSlot> _muzzles = new();
    private readonly List<ImpactSlot> _impacts = new();
    private int _nextMuzzle;
    private int _nextImpact;

    public override void _Ready()
    {
        for (int i = 0; i < MuzzleSlots; i++) _muzzles.Add(new MuzzleSlot(this));
        for (int i = 0; i < ImpactSlots; i++) _impacts.Add(new ImpactSlot(this));
    }

    /// <param name="muzzle">Barrel tip; its −Z axis points along the barrel.</param>
    public void Muzzle(Transform3D muzzle)
    {
        MuzzleSlot slot = _muzzles[_nextMuzzle];
        _nextMuzzle = (_nextMuzzle + 1) % _muzzles.Count;
        slot.Play(muzzle);
    }

    public void Explosion(Vector3 at, bool onRock)
    {
        ImpactSlot slot = _impacts[_nextImpact];
        _nextImpact = (_nextImpact + 1) % _impacts.Count;
        slot.Play(at, onRock ? Palette.RockDust.ToColor() : Palette.Smoke.ToColor());
    }

    /// <summary>A glowing ball with a lamp in it; Play pops it up and away with tweens.</summary>
    private sealed class Flash
    {
        private readonly MeshInstance3D _ball;
        private readonly OmniLight3D _light;
        private readonly float _lightEnergy;

        public Flash(Node parent, Color color, float energy, float lightEnergy, float lightRange)
        {
            _ball = new MeshInstance3D
            {
                Mesh = ParticleKit.Ball(ParticleKit.Glow(color, energy)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Visible = false,
            };
            _light = new OmniLight3D { LightColor = color, LightEnergy = 0, OmniRange = lightRange, ShadowEnabled = false };
            _ball.AddChild(_light);
            parent.AddChild(_ball);
            _lightEnergy = lightEnergy;
        }

        /// <summary>Grows from 30% to <paramref name="size"/> in <paramref name="grow"/> s (overshooting a little), then shrinks to nothing in <paramref name="shrink"/> s.</summary>
        public void Play(Vector3 at, float size, float grow, float shrink)
        {
            _ball.Position = at;
            _ball.Scale = Vector3.One * size * 0.3f;
            _ball.Visible = true;
            _light.LightEnergy = _lightEnergy;
            Tween tween = _ball.CreateTween();
            tween.TweenProperty(_ball, "scale", Vector3.One * size, grow).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            tween.TweenProperty(_ball, "scale", Vector3.One * 0.01f, shrink).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            tween.Parallel().TweenProperty(_light, "light_energy", 0f, shrink);
            tween.TweenCallback(Callable.From(() => _ball.Visible = false));
        }
    }

    private sealed class MuzzleSlot
    {
        private readonly Flash _flash;
        private readonly GpuParticles3D _smoke;

        public MuzzleSlot(Node parent)
        {
            _flash = new Flash(parent, Palette.Flash.ToColor(), 2.5f, 5f, 12f);
            _smoke = ParticleKit.OneShot(MuzzlePuffs, 0.8, 0.7f, new ParticleProcessMaterial
            {
                Direction = new Vector3(0, 0, -1),
                Spread = 12f,
                InitialVelocityMin = 3f,
                InitialVelocityMax = 9f,
                DampingMin = 8f,
                DampingMax = 12f,
                Gravity = new Vector3(0, 0.6f, 0),
                ScaleMin = 1.0f,
                ScaleMax = 1.6f,
                ScaleCurve = ParticleKit.Pop(),
                Color = Palette.Smoke.ToColor(),
            }, ParticleKit.Ball(ParticleKit.Puff));
            parent.AddChild(_smoke);
        }

        public void Play(Transform3D muzzle)
        {
            _flash.Play(muzzle.Origin - muzzle.Basis.Z * 0.3f, MuzzleFlashSize, MuzzleFlashTime * 0.4f, MuzzleFlashTime * 0.6f);
            _smoke.GlobalTransform = muzzle;
            _smoke.AmountRatio = (3 + GD.Randi() % 3) / (float)MuzzlePuffs;
            _smoke.Restart();
        }
    }

    private sealed class ImpactSlot
    {
        private readonly Flash _fireball;
        private readonly GpuParticles3D _puffs;
        private readonly ParticleProcessMaterial _puffProcess;
        private readonly GpuParticles3D _stones;

        public ImpactSlot(Node parent)
        {
            _fireball = new Flash(parent, Palette.Fire.ToColor(), 1.3f, 8f, 20f);
            _puffProcess = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.6f,
                Direction = Vector3.Up,
                Spread = 75f,
                InitialVelocityMin = 3.5f,
                InitialVelocityMax = 7f,
                DampingMin = 5f,
                DampingMax = 7f,
                Gravity = new Vector3(0, 0.4f, 0),
                ScaleMin = 1.1f,
                ScaleMax = 1.9f,
                ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.2f, peakAt: 0.15f, shrinkFrom: 0.5f),
                Color = Palette.Smoke.ToColor(),
            };
            _puffs = ParticleKit.OneShot(ImpactPuffs, 1.2, 0.9f, _puffProcess, ParticleKit.Ball(ParticleKit.Puff));
            _stones = ParticleKit.OneShot(Stones, 1.3, 1f, new ParticleProcessMaterial
            {
                Direction = Vector3.Up,
                Spread = 50f,
                InitialVelocityMin = 6f,
                InitialVelocityMax = 11f,
                Gravity = new Vector3(0, -9.8f, 0),
                AngularVelocityMin = -360f,
                AngularVelocityMax = 360f,
                ScaleMin = 0.18f,
                ScaleMax = 0.32f,
                ScaleCurve = ParticleKit.Pop(start: 1f, overshoot: 1f, peakAt: 0.1f, shrinkFrom: 0.8f),
                Color = (Palette.Rock * 0.8f).ToColor(),
            }, ParticleKit.Ball(ParticleKit.Puff));
            parent.AddChild(_puffs);
            parent.AddChild(_stones);
        }

        public void Play(Vector3 at, Color puffColor)
        {
            _fireball.Play(at + Vector3.Up * 0.6f, FireballSize, FireballGrowTime, FireballShrinkTime);
            _puffProcess.Color = puffColor;
            _puffs.GlobalPosition = at + Vector3.Up * 0.3f;
            _puffs.AmountRatio = (6 + GD.Randi() % 5) / (float)ImpactPuffs;
            _puffs.Restart();
            _stones.GlobalPosition = at + Vector3.Up * 0.3f;
            _stones.Restart();
        }
    }
}
```

用以下内容整体替换 `src/Tank.Client/scripts/Combat/DustTrail.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// Little light-brown cartoon dust puffs popping up behind both tracks above 10 km/h, more of them the faster the tank
/// goes. Attach to the tank model; puffs are emitted in world space so they stay where they were kicked up.
/// </summary>
public partial class DustTrail : Node3D
{
    private const float StartSpeed = 10f / 3.6f;
    private const float FullSpeed = 45f / 3.6f;

    private readonly List<GpuParticles3D> _emitters = new();

    /// <param name="trackX">Track centre offset from the hull centre line.</param>
    /// <param name="rearZ">Hull-space Z just behind the tracks.</param>
    public void Build(float trackX, float rearZ)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(0.25f, 0.05f, 0.2f),
            Direction = new Vector3(0, 1, 0.6f),
            Spread = 30f,
            InitialVelocityMin = 0.8f,
            InitialVelocityMax = 2f,
            DampingMin = 1.5f,
            DampingMax = 2.5f,
            Gravity = new Vector3(0, 0.2f, 0),
            ScaleMin = 0.45f,
            ScaleMax = 0.8f,
            ScaleCurve = ParticleKit.Pop(start: 0.2f, overshoot: 1.15f, peakAt: 0.2f, shrinkFrom: 0.45f),
            Color = Palette.SoilDust.ToColor(),
        };
        var drawPass = ParticleKit.Ball(ParticleKit.Puff);
        foreach (float side in new[] { -1f, 1f })
        {
            var emitter = new GpuParticles3D
            {
                Amount = 24,
                Lifetime = 1.0,
                Emitting = false,
                LocalCoords = false,
                ProcessMaterial = process,
                DrawPass1 = drawPass,
                VisibilityAabb = ParticleKit.WideBounds,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Position = new Vector3(side * trackX, 0.15f, rearZ),
            };
            AddChild(emitter);
            _emitters.Add(emitter);
        }
    }

    public void Update(float speed)
    {
        float strength = Mathf.Clamp((MathF.Abs(speed) - StartSpeed) / (FullSpeed - StartSpeed), 0f, 1f);
        foreach (GpuParticles3D emitter in _emitters)
        {
            emitter.Emitting = MathF.Abs(speed) > StartSpeed;
            emitter.AmountRatio = 0.35f + 0.65f * strength;
        }
    }
}
```

用以下内容整体替换 `src/Tank.Client/scripts/Combat/ShellViews.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;

namespace Tank.Client.Combat;

/// <summary>
/// Bright yellow cartoon tracers for live shells, pooled: a glowing head and a short tapering trail that grows to
/// <see cref="TrailLength"/> as the shell leaves the muzzle. Drawn one tick behind the sim to match interpolated tanks.
/// </summary>
public partial class ShellViews : Node3D
{
    private const float TrailLength = 7f;
    private static readonly Color TracerColor = Palette.Flash.ToColor();

    private readonly List<(Node3D Root, Node3D Trail)> _pool = new();
    private readonly StandardMaterial3D _headMaterial = new()
    {
        AlbedoColor = TracerColor,
        EmissionEnabled = true,
        Emission = TracerColor,
        EmissionEnergyMultiplier = 4f,
        DisableReceiveShadows = true,
    };
    private readonly StandardMaterial3D _trailMaterial = new()
    {
        AlbedoColor = new Color(TracerColor, 0.7f),
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
        EmissionEnabled = true,
        Emission = TracerColor,
        EmissionEnergyMultiplier = 2f,
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        DisableReceiveShadows = true,
    };

    /// <param name="alpha">Fraction of the way from the previous tick to the latest one.</param>
    public void Sync(ReadOnlySpan<Shell> shells, float alpha)
    {
        while (_pool.Count < shells.Length) _pool.Add(NewTracer());
        for (int i = 0; i < _pool.Count; i++)
        {
            var (root, trail) = _pool[i];
            root.Visible = i < shells.Length;
            if (!root.Visible) continue;
            Shell shell = shells[i];
            float age = MathF.Max(shell.Age - (1f - alpha) * SimConstants.Dt, 0f);
            Vector3 position = (shell.Position - shell.Velocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
            Vector3 heading = shell.Velocity.ToGodot().Normalized();
            if (Mathf.Abs(heading.Y) < 0.99f) root.LookAtFromPosition(position, position + heading, Vector3.Up);
            else root.Position = position;
            float length = MathF.Min(TrailLength, age * shell.Velocity.Length());
            trail.Visible = length > 0.5f;
            trail.Scale = new Vector3(1, MathF.Max(length, 0.01f), 1);
            trail.Position = new Vector3(0, 0, length / 2);
        }
    }

    private (Node3D, Node3D) NewTracer()
    {
        var root = new Node3D();
        root.AddChild(new MeshInstance3D
        {
            Mesh = new CapsuleMesh { Radius = 0.14f, Height = 1.6f, RadialSegments = 8, Rings = 2 },
            MaterialOverride = _headMaterial,
            RotationDegrees = new Vector3(90, 0, 0),   // capsule axis Y → along the flight line
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        // Unit-height cone along +Z (behind the head), widest at the head; stretched to the trail length each frame.
        var trail = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.14f, Height = 1f, RadialSegments = 8, Rings = 1 },
            MaterialOverride = _trailMaterial,
            RotationDegrees = new Vector3(90, 0, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        root.AddChild(trail);
        AddChild(root);
        return (root, trail);
    }
}
```

- [ ] **Step 3: 编译、导入、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

```bash
fxshot t9-flash 0.03
fxshot t9-boom 0.12
fxshot t9-puffs 0.3
fxshot t9-front 0.2 --camera-preset front
```

核对：
- `t9-flash`：炮口有一个明黄色发光小球，周围被照亮。
- `t9-boom`：落点（准星附近）有一个橙黄色发光火球。
- `t9-puffs`：落点处有一团白灰色、边缘圆润的卡通烟球；炮口附近有白色烟团；履带后方留下一串浅棕色小尘团。
- `t9-front`（从前方看行驶中的坦克）：能看到炮口前方的白烟团、远处落点的橙色火球，烟团是不透明的圆球，有亮面和蓝紫暗面。
- 所有烟团都没有方形贴片边缘，也没有半透明叠加的脏灰色。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 4: 提交**

```bash
git add src/Tank.Client
git commit -m "feat(client): cartoon muzzle, impact, dust and tracer effects"
```

### Task 10：界面

- 字体：站酷快乐体，用 `FontVariation` 加粗；所有文字带深色描边。
- 准星：中心白点（深色描边）；预测落点处是 3 px 粗的白圈加深色描边；装填进度是 `UiAccent` 色的进度环，装填完成时圆环弹一下（1.0 → 1.25 → 1.0，0.2 s）。
- 状态栏：左下角圆角底板（`UiPanel`，透明度 0.7），速度表图标加大号速度，炮弹图标加装填状态（就绪时显示强调色的"就绪"）。
- 狙击镜：屏幕四周深色暗角，倍率显示在准星下方。
- 新参数 `--sniper` 以狙击镜视角启动，方便截图。旧菜单不改。

**Files:**
- Modify: `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`
- Create: `src/Tank.Client/scripts/Overlay/HudStyle.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Overlay/Reticle.cs`
- Create: `src/Tank.Client/scripts/Overlay/SniperVignette.cs`
- Create: `src/Tank.Client/scripts/Overlay/StatusPanel.cs`
- Modify: `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`

- [ ] **Step 1: 界面主题、准星、状态栏、暗角**

创建 `src/Tank.Client/scripts/Overlay/HudStyle.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>
/// The cartoon HUD theme: the bundled rounded CJK font (ZCOOL KuaiLe, OFL), palette colours, and outlined text.
/// Text is always drawn with a dark outline so it reads over bright sky and grass alike.
/// </summary>
public static class HudStyle
{
    public const string FontPath = "res://assets/fonts/ZCOOLKuaiLe-Regular.ttf";
    public const int Outline = 6;

    private const float Embolden = 0.7f;   // ZCOOL KuaiLe is a regular weight; thicken it into a chunky cartoon face

    private static readonly Lazy<Font> LoadedFont = new(() => new FontVariation { BaseFont = GD.Load<FontFile>(FontPath), VariationEmbolden = Embolden });

    public static Font Font => LoadedFont.Value;
    public static Color Text => Palette.UiText.ToColor();
    public static Color OutlineColor => Palette.UiOutline.ToColor();
    public static Color Accent => Palette.UiAccent.ToColor();
    public static Color Panel => Palette.UiPanel.ToColor(Palette.UiPanelAlpha);

    /// <summary>Text with a dark outline; <paramref name="baseline"/> is the left end of the text baseline.</summary>
    public static void DrawOutlined(CanvasItem canvas, Vector2 baseline, string text, int size, Color color, int outline = Outline)
    {
        canvas.DrawStringOutline(Font, baseline, text, HorizontalAlignment.Left, -1, size, outline, OutlineColor);
        canvas.DrawString(Font, baseline, text, HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>Same, centred horizontally on <paramref name="centerX"/>.</summary>
    public static void DrawOutlinedCentered(CanvasItem canvas, float centerX, float baselineY, string text, int size, Color color, int outline = Outline)
    {
        float width = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        DrawOutlined(canvas, new Vector2(centerX - width / 2, baselineY), text, size, color, outline);
    }

    public static float TextWidth(string text, int size) => Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;

    /// <summary>A ring with a dark outline: the outline is drawn first, wider, then the coloured ring on top.</summary>
    public static void DrawOutlinedArc(CanvasItem canvas, Vector2 center, float radius, float from, float to, Color color, float width, float outline = 2f)
    {
        canvas.DrawArc(center, radius, from, to, 48, OutlineColor, width + 2 * outline, true);
        canvas.DrawArc(center, radius, from, to, 48, color, width, true);
    }
}
```

用以下内容整体替换 `src/Tank.Client/scripts/Overlay/Reticle.cs`：

```csharp
using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Centre dot = where the player wants to aim; thick ring = where the gun would hit right now, with a reload ring in the
/// accent colour around it that pops (1 → 1.25 → 1 in 0.2 s) when the gun is ready. In sniper mode the zoom shows
/// below the centre. Every mark has a dark outline.
/// </summary>
public partial class Reticle : Control
{
    private const float RingRadius = 17f;
    private const float RingWidth = 3f;
    private const float ReloadRadius = 25f;
    private const float ReloadWidth = 4f;
    private const float DotRadius = 3f;
    private const float PopSeconds = 0.2f;
    private const float PopScale = 0.25f;

    private Vector2? _impact;
    private float _reload = 1f;
    private float _zoom;
    private float _popLeft;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="impact">Screen position of the predicted hit, or null if it is behind the camera.</param>
    /// <param name="reloadProgress">0 just fired … 1 ready.</param>
    /// <param name="zoom">Sniper magnification, or 0 outside sniper mode.</param>
    public void UpdateState(Vector2? impact, float reloadProgress, float zoom)
    {
        float reload = Mathf.Clamp(reloadProgress, 0f, 1f);
        if (_reload < 1f && reload >= 1f) _popLeft = PopSeconds;
        _impact = impact;
        _reload = reload;
        _zoom = zoom;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_popLeft > 0) _popLeft = MathF.Max(0f, _popLeft - (float)delta);
    }

    public override void _Draw()
    {
        Vector2 center = Size / 2;
        DrawCircle(center, DotRadius + 2f, HudStyle.OutlineColor, true, -1f, true);
        DrawCircle(center, DotRadius, HudStyle.Text, true, -1f, true);
        if (_zoom > 0) HudStyle.DrawOutlinedCentered(this, center.X, center.Y + 64f, $"{_zoom:0}×", 26, HudStyle.Text);
        if (_impact is not { } p) return;

        float pop = 1f + PopScale * MathF.Sin(MathF.PI * (1f - _popLeft / PopSeconds)) * (_popLeft > 0 ? 1f : 0f);
        HudStyle.DrawOutlinedArc(this, p, RingRadius * pop, 0, Mathf.Tau, HudStyle.Text, RingWidth);
        if (_reload < 1f)
            HudStyle.DrawOutlinedArc(this, p, ReloadRadius, -Mathf.Pi / 2, -Mathf.Pi / 2 + Mathf.Tau * _reload, HudStyle.Accent, ReloadWidth);
        else if (_popLeft > 0)
            HudStyle.DrawOutlinedArc(this, p, ReloadRadius * pop, 0, Mathf.Tau, HudStyle.Accent, ReloadWidth);
    }
}
```

创建 `src/Tank.Client/scripts/Overlay/SniperVignette.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>Dark rounded vignette around the screen edge, shown while looking through the sniper sight.</summary>
public partial class SniperVignette : TextureRect
{
    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        StretchMode = StretchModeEnum.Scale;
        Color dark = Palette.UiOutline.ToColor();
        Texture = new GradientTexture2D
        {
            Width = 256,
            Height = 256,
            Fill = GradientTexture2D.FillEnum.Radial,
            FillFrom = new Vector2(0.5f, 0.5f),
            FillTo = new Vector2(1.0f, 0.5f),
            Gradient = new Gradient
            {
                Offsets = new[] { 0f, 0.62f, 0.9f, 1f },
                Colors = new[] { new Color(dark, 0f), new Color(dark, 0f), new Color(dark, 0.75f), new Color(dark, 0.92f) },
            },
        };
        Visible = false;
    }
}
```

创建 `src/Tank.Client/scripts/Overlay/StatusPanel.cs`：

```csharp
using Godot;

namespace Tank.Client.Overlay;

/// <summary>
/// Bottom-left rounded panel: a speedometer icon with the speed as a big outlined number, and a shell icon with the
/// reload state ("就绪" in the accent colour, or the seconds left). Icons are drawn with primitives, no image files.
/// </summary>
public partial class StatusPanel : Control
{
    private static readonly Vector2 PanelSize = new(318f, 92f);
    private const float Margin = 24f;
    private const int NumberSize = 40;
    private const int UnitSize = 20;

    private readonly StyleBoxFlat _box = new()
    {
        BgColor = HudStyle.Panel,
        CornerDetail = 8,
        AntiAliasing = true,
    };

    private string _speed = "0";
    private string _reload = "就绪";
    private bool _ready = true;

    public override void _Ready()
    {
        _box.SetCornerRadiusAll(18);
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <param name="speedKmh">Ground speed, km/h (sign ignored).</param>
    /// <param name="reloadSeconds">Seconds until the gun is ready; 0 = ready.</param>
    public void UpdateState(float speedKmh, float reloadSeconds)
    {
        _speed = $"{MathF.Abs(speedKmh):0}";
        _ready = reloadSeconds <= 0f;
        _reload = _ready ? "就绪" : $"{reloadSeconds:0.0}";
        QueueRedraw();
    }

    public override void _Draw()
    {
        var origin = new Vector2(Margin, Size.Y - Margin - PanelSize.Y);
        DrawStyleBox(_box, new Rect2(origin, PanelSize));
        float baseline = origin.Y + PanelSize.Y / 2 + NumberSize * 0.36f;

        Vector2 gauge = origin + new Vector2(38f, PanelSize.Y / 2);
        DrawSpeedometer(gauge);
        float x = gauge.X + 28f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), _speed, NumberSize, HudStyle.Text);
        x += HudStyle.TextWidth(_speed, NumberSize) + 6f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), "km/h", UnitSize, HudStyle.Text, 4);

        Vector2 shell = origin + new Vector2(196f, PanelSize.Y / 2);
        DrawShell(shell, _ready ? HudStyle.Accent : HudStyle.Text);
        x = shell.X + 20f;
        HudStyle.DrawOutlined(this, new Vector2(x, baseline), _reload, _ready ? 34 : NumberSize, _ready ? HudStyle.Accent : HudStyle.Text);
        if (!_ready) HudStyle.DrawOutlined(this, new Vector2(x + HudStyle.TextWidth(_reload, NumberSize) + 6f, baseline), "秒", UnitSize, HudStyle.Text, 4);
    }

    /// <summary>A half-dial with a needle.</summary>
    private void DrawSpeedometer(Vector2 c)
    {
        const float R = 17f;
        HudStyle.DrawOutlinedArc(this, c + new Vector2(0, 6), R, Mathf.Pi, Mathf.Tau, HudStyle.Text, 4f);
        Vector2 tip = c + new Vector2(0, 6) + new Vector2(MathF.Cos(-Mathf.Pi * 0.3f), MathF.Sin(-Mathf.Pi * 0.3f)) * (R - 4f);
        DrawLine(c + new Vector2(0, 6), tip, HudStyle.OutlineColor, 7f, true);
        DrawLine(c + new Vector2(0, 6), tip, HudStyle.Accent, 3.5f, true);
        DrawCircle(c + new Vector2(0, 6), 4f, HudStyle.Text, true, -1f, true);
    }

    /// <summary>An upright shell: a rounded case with a pointed nose.</summary>
    private void DrawShell(Vector2 c, Color color)
    {
        var body = new[]
        {
            c + new Vector2(-7, 18), c + new Vector2(7, 18), c + new Vector2(7, -4), c + new Vector2(4, -13),
            c + new Vector2(0, -18), c + new Vector2(-4, -13), c + new Vector2(-7, -4),
        };
        var outline = body.Select(p => c + (p - c) * 1.22f).ToArray();
        DrawColoredPolygon(outline, HudStyle.OutlineColor);
        DrawColoredPolygon(body, color);
        DrawLine(c + new Vector2(-7, 10), c + new Vector2(7, 10), HudStyle.OutlineColor, 2f, true);
    }
}
```

- [ ] **Step 2: 接入试驾场，加 `--sniper`**

在 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）
/// </summary>
```

   替换为：

```csharp
/// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
/// </summary>
```

2. 找到：

```csharp
    public double? FxShotDelay { get; init; }

```

   替换为：

```csharp
    public double? FxShotDelay { get; init; }
    public bool Sniper { get; init; }

```

3. 找到：

```csharp
                "--fps-log" => o with { FpsLog = true },
                "--fx-shot" => o with { FxShotDelay = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.05 },
```

   替换为：

```csharp
                "--fps-log" => o with { FpsLog = true },
                "--sniper" => o with { Sniper = true },
                "--fx-shot" => o with { FxShotDelay = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.05 },
```

在 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin
/// --fps-log --screenshot FILE --screenshot-at S --fx-shot S --quit-after S]
/// </summary>
```

   替换为：

```csharp
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin
/// --fps-log --sniper --screenshot FILE --screenshot-at S --fx-shot S --quit-after S]
/// </summary>
```

2. 找到：

```csharp
    private Reticle _reticle = null!;
    private double _accumulator;
```

   替换为：

```csharp
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;
    private SniperVignette _vignette = null!;
    private double _accumulator;
```

3. 找到：

```csharp
        _camera.SetAngles(_current.Yaw, 0.25f);
        var hud = new CanvasLayer();
        AddChild(hud);
        _reticle = new Reticle();
        hud.AddChild(_reticle);

```

   替换为：

```csharp
        _camera.SetAngles(_current.Yaw, 0.25f);
        if (_options.Sniper) _camera.ToggleSniper();
        var hud = new CanvasLayer();
        AddChild(hud);
        _vignette = new SniperVignette();
        hud.AddChild(_vignette);
        _reticle = new Reticle();
        hud.AddChild(_reticle);
        _status = new StatusPanel();
        hud.AddChild(_status);

```

4. 找到：

```csharp
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        string reload = tank.Reload > 0 ? $"{tank.Reload:0.0} 秒" : "就绪";
        string zoom = _camera.Sniper ? $"   狙击 {_camera.Zoom:0}×" : "";
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime,
            $"速度 {MathF.Abs(tank.Speed) * 3.6f:0} km/h   装填 {reload}{zoom}");
    }
```

   替换为：

```csharp
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f);
        _status.UpdateState(tank.Speed * 3.6f, tank.Reload);
        _vignette.Visible = _camera.Sniper;
    }
```

- [ ] **Step 3: 编译、导入、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

```bash
godot_run 45 --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --screenshot "$PWD/build/shots/t10-hud.png" --screenshot-at 5 --quit-after 5.5 2>&1 | grep -E "Screenshot|ERROR"
godot_run 45 --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --sniper --screenshot "$PWD/build/shots/t10-sniper.png" --screenshot-at 7.5 --quit-after 8 2>&1 | grep -E "Screenshot|ERROR"
shot t10-front front
```

核对：
- `t10-hud`：左下角是深色半透明圆角底板，里面依次是白色速度表图标、大号描边数字速度（行驶中约 50）、小字 `km/h`、白色炮弹图标、大号装填秒数和"秒"。字体是圆润的粗体，中文正常显示，没有方框或缺字。
- 预测落点处是粗白圈加深色描边，外面是一段黄色的装填进度弧，同样带描边；屏幕中心有一个描边的小白点。
- `t10-front`：装填完成，状态栏显示黄色的"就绪"和黄色炮弹图标。
- `t10-sniper`：屏幕四周是深色圆形暗角，中心清楚；中心下方有描边的"2×"。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 4: 提交**

```bash
git add src/Tank.Client
git commit -m "feat(client): cartoon HUD with status panel and sniper vignette"
```

### Task 11：仿真——草丛物件

`PropKind.Bush = 1`；`Prop.IsSolid`（岩石 true、草丛 false）；`TankMotion` 的推出和重叠检查、`Battlefield.Raycast`（炮弹和瞄准都用它）跳过非实心物件；`Battlefield` 要求草丛占地是圆（两个半轴相等）；新增 `BushesNear(Vector2 point, float radius)`。此时地图里还没有草丛，玩法不变。

**Files:**
- Modify: `src/Tank.Sim/Maps/Battlefield.cs`
- Modify（整体替换）: `src/Tank.Sim/Maps/Prop.cs`
- Modify: `src/Tank.Sim/Vehicles/TankMotion.cs`
- Create: `tests/Tank.Sim.Tests/BushTests.cs`

- [ ] **Step 1: 写测试**

创建 `tests/Tank.Sim.Tests/BushTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class BushTests
{
    private static Prop Bush(int id, float x, float z, float radius = 3f, float height = 3.5f) =>
        new(id, PropKind.Bush, new Obb2(new Vector2(x, z), new Vector2(radius, radius), 0), -0.2f, height);

    private static Prop Rock(int id, float x, float z) =>
        new(id, PropKind.Rock, new Obb2(new Vector2(x, z), new Vector2(1, 5), 0), -1, 10);

    [Fact]
    public void Rocks_are_solid_and_bushes_are_not()
    {
        Assert.True(Rock(1, 0, 0).IsSolid);
        Assert.False(Bush(2, 0, 0).IsSolid);
    }

    [Fact]
    public void A_tank_drives_through_a_bush_as_if_it_were_not_there()
    {
        var open = TestWorld.Tank(100, 130);
        var through = TestWorld.Tank(100, 130);
        TestWorld.Drive(ref open, TestWorld.FlatField(), TestWorld.Cmd(throttle: 1), 4 * SimConstants.TickRate);
        TestWorld.Drive(ref through, TestWorld.FlatField(Bush(1, 100, 110)), TestWorld.Cmd(throttle: 1), 4 * SimConstants.TickRate);
        Assert.True(open.Position.Y < 100, "the run passes the bush at z = 110");
        Assert.Equal(open.Position, through.Position);
        Assert.Equal(open.Speed, through.Speed);
    }

    [Fact]
    public void A_tank_can_stop_and_turn_inside_a_bush()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100, 100));
        var t = TestWorld.Tank(100, 100);
        TestWorld.Drive(ref t, field, TestWorld.Cmd(steer: 1), SimConstants.TickRate);
        Assert.Equal(new Vector2(100, 100), t.Position);
        Assert.NotEqual(0f, t.Yaw);
    }

    [Fact]
    public void Rays_pass_through_a_bush_and_hit_the_ground_behind_it()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 60, 100));
        // Descends from 1 m to −1 m over 100 m: crosses the bush at x≈60 (y≈0.2) and meets the ground at x = 70.
        Assert.True(field.Raycast(new Vector3(20, 1, 100), new Vector3(120, -1, 100), out RayHit hit));
        Assert.Equal(RayHit.TerrainId, hit.PropId);
        Assert.Equal(70f, hit.Point.X, 1e-2f);
    }

    [Fact]
    public void Rays_still_stop_at_a_rock_behind_a_bush()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 50, 100), Rock(2, 60, 100));
        Assert.True(field.Raycast(new Vector3(20, 1, 100), new Vector3(120, 1, 100), out RayHit hit));
        Assert.Equal(2, hit.PropId);
        Assert.Equal(59f, hit.Point.X, 1e-3f);
    }

    [Fact]
    public void BushesNear_finds_bushes_whose_edge_is_within_the_radius()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100, 100, radius: 2f), Bush(2, 120, 100, radius: 3f), Rock(3, 102, 100), Bush(4, 150, 100));
        int[] near = field.BushesNear(new Vector2(110, 100), 8f).Select(p => p.Id).OrderBy(id => id).ToArray();
        Assert.Equal(new[] { 1, 2 }, near);   // bush 1's edge is 8 m away, bush 2's 7 m; the rock and bush 4 don't count
        Assert.DoesNotContain(field.BushesNear(new Vector2(110, 100), 7.5f), p => p.Id == 1);
    }

    [Fact]
    public void A_bush_footprint_must_be_round()
    {
        var oval = new Prop(1, PropKind.Bush, new Obb2(new Vector2(60, 100), new Vector2(2, 3), 0), 0, 3);
        Assert.Throws<ArgumentException>(() => TestWorld.FlatField(oval));
    }
}
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Sim.Tests 2>&1 | tail -3
```

Expected：编译失败，`PropKind` 没有 `Bush`、`Prop` 没有 `IsSolid`、`Battlefield` 没有 `BushesNear`。

- [ ] **Step 3: 实现**

在 `src/Tank.Sim/Maps/Battlefield.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
                throw new ArgumentException($"Prop {prop.Id} must have positive height, got {prop.Height}.", nameof(props));
        }
```

   替换为：

```csharp
                throw new ArgumentException($"Prop {prop.Id} must have positive height, got {prop.Height}.", nameof(props));
            if (prop.Kind == PropKind.Bush && prop.Footprint.HalfExtents.X != prop.Footprint.HalfExtents.Y)
                throw new ArgumentException($"Bush {prop.Id} must have a round footprint (equal half extents).", nameof(props));
        }
```

2. 找到：

```csharp

    /// <summary>Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against ground and props.</summary>
    public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
```

   替换为：

```csharp

    /// <summary>Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against ground and solid props (bushes are see-through).</summary>
    public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
```

3. 找到：

```csharp
        {
            if (Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out float t) && (!found || t < best))
```

   替换为：

```csharp
        {
            if (!prop.IsSolid) continue;
            if (Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out float t) && (!found || t < best))
```

4. 找到：

```csharp
    }
}
```

   替换为：

```csharp
    }

    /// <summary>Bushes whose edge comes within <paramref name="radius"/> of <paramref name="point"/> (their footprint is a circle of radius HalfExtents.X).</summary>
    public IEnumerable<Prop> BushesNear(Vector2 point, float radius)
    {
        foreach (var prop in _props)
            if (prop.Kind == PropKind.Bush && Vector2.Distance(prop.Footprint.Center, point) - prop.Footprint.HalfExtents.X <= radius)
                yield return prop;
    }
}
```

用以下内容整体替换 `src/Tank.Sim/Maps/Prop.cs`：

```csharp
using Tank.Sim.Geometry;

namespace Tank.Sim.Maps;

public enum PropKind : byte
{
    Rock = 0,
    /// <summary>Tall grass a tank can hide in: no collision, shells and aim rays pass through. Round footprint.</summary>
    Bush = 1,
}

/// <summary>A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>.</summary>
public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
{
    public float Top => Bottom + Height;

    /// <summary>Blocks tanks, shells and rays. Bushes don't; they only hide what is inside them.</summary>
    public bool IsSolid => Kind != PropKind.Bush;
}
```

在 `src/Tank.Sim/Vehicles/TankMotion.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp

    /// <summary>Applies throttle and steering, then moves the hull and pushes it out of props and <paramref name="obstacles"/>.</summary>
    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles)
```

   替换为：

```csharp

    /// <summary>Applies throttle and steering, then moves the hull and pushes it out of solid props and <paramref name="obstacles"/>.</summary>
    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles)
```

2. 找到：

```csharp

    /// <summary>Deepest overlap of <paramref name="box"/> with any prop or obstacle; 0 when clear.</summary>
    private static float PenetrationDepth(in Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
```

   替换为：

```csharp

    /// <summary>Deepest overlap of <paramref name="box"/> with any solid prop or obstacle; 0 when clear.</summary>
    private static float PenetrationDepth(in Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
```

3. 找到：

```csharp
        for (int i = 0; i < props.Count; i++)
            if (Obb2.TryGetPenetration(box, props[i].Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
        foreach (Obb2 other in obstacles)
```

   替换为：

```csharp
        for (int i = 0; i < props.Count; i++)
            if (props[i].IsSolid && Obb2.TryGetPenetration(box, props[i].Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
        foreach (Obb2 other in obstacles)
```

4. 找到：

```csharp
            bool pushed = false;
            for (int i = 0; i < props.Count; i++) pushed |= PushOut(ref box, props[i].Footprint);
            foreach (Obb2 other in obstacles) pushed |= PushOut(ref box, other);
```

   替换为：

```csharp
            bool pushed = false;
            for (int i = 0; i < props.Count; i++)
                if (props[i].IsSolid) pushed |= PushOut(ref box, props[i].Footprint);   // bushes don't block
            foreach (Obb2 other in obstacles) pushed |= PushOut(ref box, other);
```

- [ ] **Step 4: 测试通过，客户端仍能编译**

```bash
dotnet test tests/Tank.Sim.Tests 2>&1 | tail -1
dotnet test tests/Tank.Core.Tests 2>&1 | tail -1
```

Expected：`Passed: 127`（原 120 个 + 7 个）；`Tank.Core.Tests` 62 个通过（含架构测试）。

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): non-solid bush props"
```

### Task 12：试驾场的草丛区

`SandboxMap` 在岩石之后种 6 片草丛，每片 6～12 丛、宽 10～25 m，放在坡度小于 10° 的平地上；每丛半径 1.8～3.2 m、露出地面 3.0～4.0 m，种子化生成。草丛不压岩石，离出生点至少 20 m（算上半径），不挡出生点到大山的通道（离通道中线至少 6 m）。离出生点最近的一片在通道东侧约 40 m 处。客户端这时还不画草丛：`RockMeshes` 只画岩石，`GrassField` 只避开实心物件。

**Files:**
- Modify: `src/Tank.Client/scripts/World/GrassField.cs`
- Modify: `src/Tank.Client/scripts/World/RockMeshes.cs`
- Modify（整体替换）: `src/Tank.Sim/Maps/SandboxMap.cs`
- Modify（整体替换）: `tests/Tank.Sim.Tests/SandboxMapTests.cs`

- [ ] **Step 1: 写测试**

用以下内容整体替换 `tests/Tank.Sim.Tests/SandboxMapTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SandboxMapTests
{
    [Fact]
    public void Create_IsDeterministic()
    {
        Battlefield a = SandboxMap.Create(), b = SandboxMap.Create();
        Assert.Equal(a.Props.Count, b.Props.Count);
        Assert.Equal(a.Props[0], b.Props[0]);
        Assert.Equal(a.Terrain.HeightAt(123, 45), b.Terrain.HeightAt(123, 45));
    }

    [Fact]
    public void Spawn_IsPlayable_AndClearOfRocks()
    {
        Battlefield field = SandboxMap.Create();
        Assert.Equal(SandboxMap.Spawn, field.ClampToPlayable(SandboxMap.Spawn));
        Obb2 hull = TankMotion.Footprint(TankCatalog.Medium, SandboxMap.Spawn, 0);
        Assert.All(field.Props, p => Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _)));
    }

    [Fact]
    public void Terrain_HasAHillWorthClimbing()
    {
        Battlefield field = SandboxMap.Create();
        float rise = field.Terrain.HeightAt(200, 150) - field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y);
        Assert.True(rise > 15, $"hill only {rise}m above spawn");
    }

    [Fact]
    public void Rocks_SitOnTheGround()
    {
        Battlefield field = SandboxMap.Create();
        Assert.All(field.Props, p =>
            Assert.True(p.Top > field.Terrain.HeightAt(p.Footprint.Center.X, p.Footprint.Center.Y) + 1f, $"rock {p.Id} is buried"));
    }

    [Fact]
    public void DrivingStraightFromSpawn_ClimbsTheBigHill()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        var input = new[] { TestWorld.Cmd(throttle: 1) };
        float highest = 0f;
        for (int i = 0; i < 20 * SimConstants.TickRate; i++)
        {
            sim.Step(input);
            Vector2 p = sim.Tanks[0].Position;
            highest = MathF.Max(highest, sim.Field.Terrain.HeightAt(p.X, p.Y));
        }
        Assert.True(highest > 18f, $"only reached {highest}m");
    }

    [Fact]
    public void Terrain_HasSlopesSteeperThanTheClimbLimit()
    {
        var terrain = SandboxMap.Create().Terrain;
        float steepest = 0f;
        for (float z = 0; z <= terrain.SizeZ; z += 2f)
        for (float x = 0; x <= terrain.SizeX; x += 2f)
            steepest = MathF.Max(steepest, MathF.Acos(terrain.NormalAt(x, z).Y));
        Assert.True(steepest > TankCatalog.Medium.MaxClimb, $"steepest {steepest} rad");
    }

    private static readonly Vector2 BigHill = new(200, 150);

    private static float DistanceToLane(Vector2 p)
    {
        Vector2 ab = BigHill - SandboxMap.Spawn;
        float t = Math.Clamp(Vector2.Dot(p - SandboxMap.Spawn, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, SandboxMap.Spawn + ab * t);
    }

    private static Prop[] Bushes(Battlefield field) => field.Props.Where(p => p.Kind == PropKind.Bush).ToArray();

    [Fact]
    public void Six_bush_patches_of_six_to_twelve_bushes()
    {
        Battlefield field = SandboxMap.Create();
        Assert.Equal(6, SandboxMap.BushPatches.Count);
        foreach (Vector2 patch in SandboxMap.BushPatches)
        {
            Prop[] members = Bushes(field).Where(b => Vector2.Distance(b.Footprint.Center, patch) <= SandboxMap.BushPatchRadius).ToArray();
            Assert.InRange(members.Length, 6, 12);
            float width = members.Max(a => members.Max(b => Vector2.Distance(a.Footprint.Center, b.Footprint.Center) + a.Footprint.HalfExtents.X + b.Footprint.HalfExtents.X));
            Assert.InRange(width, 10f, 25f);
        }
        Assert.Equal(Bushes(field).Length, SandboxMap.BushPatches.Sum(patch => Bushes(field).Count(b => Vector2.Distance(b.Footprint.Center, patch) <= SandboxMap.BushPatchRadius)));
    }

    [Fact]
    public void Bushes_are_taller_than_a_tank_and_round()
    {
        Battlefield field = SandboxMap.Create();
        Assert.All(Bushes(field), b =>
        {
            Assert.InRange(b.Footprint.HalfExtents.X, 1.8f, 3.2f);
            Assert.Equal(b.Footprint.HalfExtents.X, b.Footprint.HalfExtents.Y);
            float ground = field.Terrain.HeightAt(b.Footprint.Center.X, b.Footprint.Center.Y);
            Assert.InRange(b.Top - ground, 3.0f, 4.0f);
            Assert.True(b.Top - ground > TankCatalog.Medium.TrunnionHeight + 0.5f, "a bush hides the whole tank");
            Assert.False(b.IsSolid);
        });
    }

    [Fact]
    public void Bushes_grow_on_flat_ground_clear_of_rocks_the_spawn_and_the_lane()
    {
        Battlefield field = SandboxMap.Create();
        Prop[] rocks = field.Props.Where(p => p.Kind == PropKind.Rock).ToArray();
        Assert.All(Bushes(field), b =>
        {
            Vector2 c = b.Footprint.Center;
            float r = b.Footprint.HalfExtents.X;
            Assert.True(MathF.Acos(field.Terrain.NormalAt(c.X, c.Y).Y) < 12f * MathF.PI / 180f, $"bush {b.Id} on a slope");
            Assert.All(rocks, rock => Assert.False(Obb2.TryGetPenetration(b.Footprint, rock.Footprint, out _), $"bush {b.Id} overlaps rock {rock.Id}"));
            Assert.True(DistanceToLane(c) - r >= 6f, $"bush {b.Id} narrows the spawn-to-hill lane");
            Assert.True(Vector2.Distance(c, SandboxMap.Spawn) - r >= 20f, $"bush {b.Id} crowds the spawn");
            Assert.Equal(c, field.ClampToPlayable(c));
        });
    }

    [Fact]
    public void The_nearest_bush_patch_is_beside_the_lane_about_forty_metres_out()
    {
        Vector2 nearest = SandboxMap.BushPatches.MinBy(p => Vector2.Distance(p, SandboxMap.Spawn));
        Assert.InRange(Vector2.Distance(nearest, SandboxMap.Spawn), 35f, 45f);
        Assert.InRange(DistanceToLane(nearest), 12f, 30f);
    }

    [Fact]
    public void Bushes_come_after_the_rocks_so_rock_ids_are_unchanged()
    {
        Battlefield field = SandboxMap.Create();
        int lastRock = field.Props.Where(p => p.Kind == PropKind.Rock).Max(p => p.Id);
        Assert.All(Bushes(field), b => Assert.True(b.Id > lastRock));
        Assert.Equal(Bushes(field), Bushes(SandboxMap.Create()));
    }
}
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Sim.Tests 2>&1 | tail -3
```

Expected：编译失败，`SandboxMap` 没有 `BushPatches`、`BushPatchRadius`。

- [ ] **Step 3: 种草丛**

用以下内容整体替换 `src/Tank.Sim/Maps/SandboxMap.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// 400m × 400m test ground: rolling noise, four hills, scattered rocks and six patches of tall bushes. The spawn faces
/// the big hill (north); the nearest bush patch sits beside that lane about 40 m out.
/// </summary>
public static class SandboxMap
{
    public const int Seed = 20261013;
    public const int Samples = 201;
    public const float CellSize = 2f;
    public const float Border = 10f;
    private const int RockAttempts = 48;
    private const float SpawnClearance = 30f;

    public static readonly Vector2 Spawn = new(200, 330);

    /// <summary>Faces the big hill to the north.</summary>
    public const float SpawnYaw = 0f;

    private const float LaneHalfWidth = 6f;
    private static readonly Vector2 BigHill = new(200, 150);

    /// <summary>Centres of the bush patches, hand-placed on flat ground; the first is the one near the spawn.</summary>
    public static readonly IReadOnlyList<Vector2> BushPatches = new Vector2[]
    {
        new(222, 296), new(150, 250), new(270, 250), new(60, 200), new(330, 120), new(250, 70),
    };

    /// <summary>Every bush of a patch has its centre within this distance of the patch centre.</summary>
    public const float BushPatchRadius = 12.5f;
    private const float BushSpread = 8f;          // bush centres lie within this radius, so a patch is at most ~22 m across
    private const int MinBushes = 6, MaxBushes = 12;
    private const float MinBushRadius = 1.8f, MaxBushRadius = 3.2f;
    private const float MinBushHeight = 3.0f, MaxBushHeight = 4.0f;
    private const float BushSink = 0.3f;          // footprint starts this far below the ground, like the rocks
    private const float BushMaxSlope = 10f * MathF.PI / 180f;
    private const float BushSpawnClearance = 20f;
    private const float BushOverlap = 0.7f;       // neighbours may overlap: centres at least this × the sum of radii apart
    private const int BushAttemptsPerPatch = 80;

    private static readonly TerrainGenerator.Hill[] Hills =
    {
        new(BigHill.X, BigHill.Y, 70, 22),
        new(90, 250, 45, 12),
        new(310, 230, 30, 16),
        new(140, 90, 35, 9),
    };

    public static Battlefield Create()
    {
        HeightField terrain = TerrainGenerator.Generate(Seed, Samples, CellSize, roughness: 4f, Hills);
        List<Prop> props = ScatterRocks(terrain);
        props.AddRange(PlantBushes(terrain, props));   // after the rocks, so rock ids (and their shapes) don't change
        return new Battlefield(terrain, props, Border);
    }

    /// <summary>6–12 round bushes per patch on flat ground, clear of rocks, the spawn and the spawn-to-hill lane.</summary>
    private static List<Prop> PlantBushes(HeightField terrain, List<Prop> rocks)
    {
        var bushes = new List<Prop>();
        int nextId = rocks.Count + 1;
        for (int patch = 0; patch < BushPatches.Count; patch++)
        {
            var rng = new XorShift((uint)(Seed + 7919 * (patch + 1)));
            int wanted = MinBushes + (int)(rng.NextUInt() % (MaxBushes - MinBushes + 1));
            int planted = 0;
            for (int attempt = 0; attempt < BushAttemptsPerPatch && planted < wanted; attempt++)
            {
                float angle = rng.NextFloat() * Angles.TwoPi, reach = MathF.Sqrt(rng.NextFloat()) * BushSpread;
                float radius = MinBushRadius + rng.NextFloat() * (MaxBushRadius - MinBushRadius);
                float height = MinBushHeight + rng.NextFloat() * (MaxBushHeight - MinBushHeight);
                Vector2 center = BushPatches[patch] + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * reach;
                var footprint = new Obb2(center, new Vector2(radius, radius), 0f);
                if (!CanGrowBush(terrain, footprint, rocks, bushes)) continue;
                float ground = terrain.HeightAt(center.X, center.Y);
                bushes.Add(new Prop(nextId++, PropKind.Bush, footprint, ground - BushSink, height + BushSink));
                planted++;
            }
        }
        return bushes;
    }

    private static bool CanGrowBush(HeightField terrain, Obb2 footprint, List<Prop> rocks, List<Prop> bushes)
    {
        Vector2 c = footprint.Center;
        float r = footprint.HalfExtents.X;
        if (MathF.Acos(terrain.NormalAt(c.X, c.Y).Y) > BushMaxSlope) return false;
        if (Vector2.Distance(c, Spawn) - r < BushSpawnClearance) return false;
        if (DistanceToSegment(c, Spawn, BigHill) - r < LaneHalfWidth) return false;
        if (c.X < Border + r || c.Y < Border + r || c.X > terrain.SizeX - Border - r || c.Y > terrain.SizeZ - Border - r) return false;
        foreach (Prop rock in rocks)
            if (Obb2.TryGetPenetration(footprint, rock.Footprint, out _)) return false;
        foreach (Prop bush in bushes)
            if (Vector2.Distance(c, bush.Footprint.Center) < BushOverlap * (r + bush.Footprint.HalfExtents.X)) return false;
        return true;
    }

    private static List<Prop> ScatterRocks(HeightField terrain)
    {
        var rng = new XorShift(Seed);
        var rocks = new List<Prop>();
        for (int k = 0; k < RockAttempts; k++)
        {
            var center = new Vector2(40 + rng.NextFloat() * 320, 40 + rng.NextFloat() * 320);
            var half = new Vector2(1 + rng.NextFloat() * 2, 1 + rng.NextFloat() * 2.5f);
            float height = 1.5f + rng.NextFloat() * 3f;
            float yaw = rng.NextFloat() * Angles.TwoPi;
            if (Vector2.Distance(center, Spawn) < SpawnClearance) continue;
            if (DistanceToSegment(center, Spawn, BigHill) < LaneHalfWidth) continue;
            float ground = terrain.HeightAt(center.X, center.Y);
            // Sink the base 1m so the rock never floats on a slope.
            rocks.Add(new Prop(rocks.Count + 1, PropKind.Rock, new Obb2(center, half, yaw), ground - 1f, height + 1f));
        }
        return rocks;
    }

    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = System.Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }
}
```

- [ ] **Step 4: 测试通过**

```bash
dotnet test tests/Tank.Sim.Tests 2>&1 | tail -1
```

Expected：`Passed: 132`。原有的 `Create_IsDeterministic`、`Spawn_IsPlayable_AndClearOfRocks`、`Rocks_SitOnTheGround`、`DrivingStraightFromSpawn_ClimbsTheBigHill` 不改也通过。

- [ ] **Step 5: 客户端只把岩石画成岩石**

在 `src/Tank.Client/scripts/World/GrassField.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
        {
            Obb2 grown = prop.Footprint with { HalfExtents = prop.Footprint.HalfExtents + new SN.Vector2(RockMargin) };
```

   替换为：

```csharp
        {
            if (!prop.IsSolid) continue;   // grass grows on under bushes
            Obb2 grown = prop.Footprint with { HalfExtents = prop.Footprint.HalfExtents + new SN.Vector2(RockMargin) };
```

在 `src/Tank.Client/scripts/World/RockMeshes.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
        {
            float bottom = LowestGround(prop.Footprint, terrain) - BuryDepth;
```

   替换为：

```csharp
        {
            if (prop.Kind != PropKind.Rock) continue;   // bushes have their own view (BushField)
            float bottom = LowestGround(prop.Footprint, terrain) - BuryDepth;
```

- [ ] **Step 6: 编译、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
shot t12-hill hill
```

画面与 Task 10 的 hill 图一样：没有多出奇怪的石头（草丛还不可见），草地完整。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 7: 提交**

```bash
git add src/Tank.Sim src/Tank.Client tests/Tank.Sim.Tests
git commit -m "feat(sim): six bush patches in the sandbox map"
```

### Task 13：草丛网格

`BushShape.Build(seed)` 生成单位尺寸（半径 1、高 1）的草丛，由 `BushField` 按物件的半径和高度缩放：30～50 片宽大的卡通叶片从中心附近升起、向外弯（金角螺旋均匀分布），中间是 1～2 个轻微起伏的扁球作"蓬松核心"挡住视线。法线全部从同一个中心向外，整丛像一个软球一样受光。顶点色：R 高度、G 每片明暗、B 叶片 1 / 核心 0。

**Files:**
- Create: `tests/Tank.Art.Tests/BushShapeTests.cs`
- Create: `src/Tank.Client/scripts/Art/Mesh/BushShape.cs`

- [ ] **Step 1: 写测试**

创建 `tests/Tank.Art.Tests/BushShapeTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Art.Meshes;

namespace Tank.Art.Tests;

public class BushShapeTests
{
    private static int[] LeafVertices(MeshData bush) => Enumerable.Range(0, bush.VertexCount).Where(i => bush.Colors[i].Z > 0.5f).ToArray();
    private static int[] CoreVertices(MeshData bush) => Enumerable.Range(0, bush.VertexCount).Where(i => bush.Colors[i].Z <= 0.5f).ToArray();

    [Fact]
    public void Bushes_have_thirty_to_fifty_leaves_and_one_or_two_core_balls()
    {
        int coreBall = Icosphere.Build(BushShape.CoreSubdivisions).VertexCount;
        var leaves = new HashSet<int>();
        var cores = new HashSet<int>();
        for (uint seed = 1; seed <= 40; seed++)
        {
            MeshData bush = BushShape.Build(seed);
            int leafVertices = LeafVertices(bush).Length;
            Assert.Equal(0, leafVertices % BushShape.VerticesPerLeaf);
            int count = leafVertices / BushShape.VerticesPerLeaf;
            Assert.InRange(count, 30, 50);
            leaves.Add(count);
            int core = CoreVertices(bush).Length;
            Assert.Equal(0, core % coreBall);
            cores.Add(core / coreBall);
        }
        Assert.True(leaves.Count > 5, "leaf counts vary with the seed");
        Assert.Equal(new[] { 1, 2 }, cores.OrderBy(n => n));
    }

    [Fact]
    public void A_unit_bush_fits_its_unit_cylinder_and_stands_on_the_ground()
    {
        for (uint seed = 1; seed <= 20; seed++)
        {
            MeshData bush = BushShape.Build(seed);
            var (min, max) = MeshAssert.Bounds(bush);
            Assert.True(min.Y >= -0.05f, $"seed {seed}: {min.Y} below ground");
            Assert.InRange(max.Y, 0.9f, 1.0f);
            Assert.All(bush.Positions, p => Assert.True(new Vector2(p.X, p.Z).Length() <= 1.0f, $"seed {seed}: {p} outside the radius"));
            Assert.True(bush.Positions.Max(p => new Vector2(p.X, p.Z).Length()) > 0.85f, "leaves reach the edge");
        }
    }

    [Fact]
    public void Leaves_lean_out_from_the_middle_and_carry_a_root_to_tip_gradient()
    {
        MeshData bush = BushShape.Build(3);
        int[] leaf = LeafVertices(bush);
        int v = BushShape.VerticesPerLeaf;
        for (int first = leaf[0]; first <= leaf[^1]; first += v)
        {
            Vector3 root = (bush.Positions[first] + bush.Positions[first + 1]) / 2, tip = bush.Positions[first + v - 1];
            Assert.True(new Vector2(tip.X, tip.Z).Length() > new Vector2(root.X, root.Z).Length() + 0.2f, "leaf leans outward");
            Assert.Equal(0f, bush.Colors[first].X);
            Assert.Equal(1f, bush.Colors[first + v - 1].X);
        }
    }

    [Fact]
    public void Normals_are_rounded_like_one_big_ball()
    {
        MeshData bush = BushShape.Build(5);
        for (int i = 0; i < bush.VertexCount; i++)
        {
            Vector3 p = bush.Positions[i], n = bush.Normals[i];
            Vector3 fromCentre = p - new Vector3(0, BushShape.ShadingCentreY, 0);
            Assert.True(Vector3.Dot(n, fromCentre) > 0f, $"vertex {i}: normal {n} points into the bush");
            Assert.Equal(1f, n.Length(), 3);
        }
    }

    [Fact]
    public void The_core_is_a_closed_ball_that_fills_the_lower_middle()
    {
        MeshData bush = BushShape.Build(8);
        Vector3[] core = CoreVertices(bush).Select(i => bush.Positions[i]).ToArray();
        float reach = core.Max(p => new Vector2(p.X, p.Z).Length());
        Assert.InRange(reach, 0.45f, 0.9f);
        Assert.True(core.Max(p => p.Y) <= 0.85f && core.Max(p => p.Y) >= 0.5f);
        MeshAssert.WindingMatchesNormals(BushShape.Core(0.6f, 0.5f, 1u));
        MeshAssert.Closed(BushShape.Core(0.6f, 0.5f, 1u));
    }

    [Fact]
    public void Same_seed_same_bush()
    {
        Assert.Equal(BushShape.Build(9).Positions, BushShape.Build(9).Positions);
        Assert.NotEqual(BushShape.Build(9).Positions, BushShape.Build(10).Positions);
    }
}
```

- [ ] **Step 2: 运行，确认失败**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -3
```

Expected：编译失败，`BushShape` 不存在。

- [ ] **Step 3: 实现**

创建 `src/Tank.Client/scripts/Art/Mesh/BushShape.cs`：

```csharp
using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// A tall cartoon bush of unit size (radius 1, height 1, rooted at y = 0); the bush view scales each instance to its
/// prop's radius and height. 30–50 broad leaves rise from near the middle and arch outward, around a "fluffy core" of
/// one or two softly lumpy balls that blocks the view through the gaps.
/// Normals all point away from one shading centre, so the whole bush lights like a single soft ball.
/// Vertex colour: R = height along the leaf / up the core (0 root → 1 top, for the colour gradient and wind),
/// G = per-leaf shade in [0, 1], B = 1 on leaves and 0 on the core (the core barely sways), A = 1.
/// </summary>
public static class BushShape
{
    public const int LeafRows = 3;
    public const int VerticesPerLeaf = LeafRows * 2 + 1;
    public const int CoreSubdivisions = 2;
    public const float ShadingCentreY = 0.3f;

    private const int MinLeaves = 30, MaxLeaves = 50;
    private const float LeafWidth = 0.3f;
    private const float LeafTaper = 0.45f;
    private const float CoreY = 0.38f;
    private const float CoreLumps = 0.07f;

    public static MeshData Build(uint seed)
    {
        var b = new MeshBuilder();
        int leaves = MinLeaves + (int)(SeededNoise.Hash(1, 2, 3, seed) % (MaxLeaves - MinLeaves + 1));
        for (int i = 0; i < leaves; i++) AddLeaf(b, i, seed);
        int balls = 1 + (int)(SeededNoise.Hash(4, 5, 6, seed) % 2);
        for (int k = 0; k < balls; k++)
        {
            float R(int n) => SeededNoise.Hash01(k, n, 9, seed);
            float halfWidth = 0.5f + 0.15f * R(0), halfHeight = 0.32f + 0.08f * R(1);
            float angle = R(2) * MathF.Tau, offset = balls == 1 ? 0f : 0.2f;
            var at = new Vector3(MathF.Cos(angle) * offset, CoreY, MathF.Sin(angle) * offset);
            MeshData ball = Core(halfWidth, halfHeight, seed + (uint)k * 101u).Transformed(Matrix4x4.CreateTranslation(at));
            int first = b.VertexCount;
            for (int v = 0; v < ball.VertexCount; v++)
            {
                Vector3 p = ball.Positions[v];
                b.AddVertex(p, Shading(p), ball.Uvs[v], new Vector4(Math.Clamp(p.Y, 0f, 1f), 0.5f, 0f, 1f));
            }
            for (int t = 0; t < ball.Indices.Count; t += 3)
                b.AddTriangle(first + ball.Indices[t], first + ball.Indices[t + 1], first + ball.Indices[t + 2]);
        }
        return b.Build();
    }

    /// <summary>A closed, softly lumpy ellipsoid centred on the origin (smooth normals).</summary>
    public static MeshData Core(float halfWidth, float halfHeight, uint seed)
    {
        MeshData sphere = Icosphere.Build(CoreSubdivisions);
        var positions = new Vector3[sphere.VertexCount];
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3 d = sphere.Positions[i];
            positions[i] = d * new Vector3(halfWidth, halfHeight, halfWidth) * (1f + CoreLumps * SeededNoise.Fbm(d * 1.7f, seed, 2));
        }
        Vector3[] normals = RockShape.SmoothNormals(positions, sphere.Indices);
        var b = new MeshBuilder();
        for (int i = 0; i < positions.Length; i++) b.AddVertex(positions[i], normals[i], sphere.Uvs[i]);
        for (int t = 0; t < sphere.Indices.Count; t += 3) b.AddTriangle(sphere.Indices[t], sphere.Indices[t + 1], sphere.Indices[t + 2]);
        return b.Build();
    }

    /// <summary>One broad leaf: rows of vertex pairs climbing and arching outward, closed by a blunt tip vertex.</summary>
    private static void AddLeaf(MeshBuilder b, int index, uint seed)
    {
        float R(int n) => SeededNoise.Hash01(index, n, 1, seed);
        float angle = (index + R(0) * 0.8f) * 2.39996f;   // golden-angle spiral so leaves spread evenly round the bush
        var outward = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
        var across = new Vector3(-outward.Z, 0, outward.X);
        float rootRadius = 0.08f + 0.27f * R(1), tipRadius = 0.6f + 0.33f * R(2);
        float height = 0.65f + 0.35f * R(3);
        float shade = R(4);
        float twist = (R(5) - 0.5f) * 0.6f;   // tilt the blade a little about its own axis
        Vector3 Spine(float t) => outward * (rootRadius + (tipRadius - rootRadius) * MathF.Pow(t, 1.4f)) + Vector3.UnitY * (height * t * (1.15f - 0.15f * t));

        int first = b.VertexCount;
        for (int row = 0; row < LeafRows; row++)
        {
            float t = MathF.Sin((float)row / LeafRows * MathF.PI / 2);
            float half = LeafWidth * (1 - LeafTaper * t * t) / 2;
            Vector3 side = Vector3.Normalize(across + Vector3.UnitY * twist) * half;
            Vector3 left = Spine(t) - side, right = Spine(t) + side;
            var color = new Vector4(t, shade, 1f, 1f);
            b.AddVertex(left, Shading(left), new Vector2(0, t), color);
            b.AddVertex(right, Shading(right), new Vector2(1, t), color);
        }
        Vector3 tip = Spine(1f);
        int tipIndex = b.AddVertex(tip, Shading(tip), new Vector2(0.5f, 1f), new Vector4(1f, shade, 1f, 1f));
        for (int row = 0; row < LeafRows - 1; row++)
        {
            int l0 = first + row * 2;
            b.AddQuad(l0, l0 + 1, l0 + 3, l0 + 2);
        }
        int last = first + (LeafRows - 1) * 2;
        b.AddTriangle(last, last + 1, tipIndex);
    }

    private static Vector3 Shading(Vector3 p)
    {
        Vector3 d = p - new Vector3(0, ShadingCentreY, 0);
        return d.LengthSquared() < 1e-8f ? Vector3.UnitY : Vector3.Normalize(d);
    }
}
```

- [ ] **Step 4: 测试通过**

```bash
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
```

Expected：`Passed: 150`。

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

- [ ] **Step 5: 提交**

```bash
git add src/Tank.Client/scripts/Art tests/Tank.Art.Tests
git commit -m "feat(art): procedural bush mesh"
```

### Task 14：草丛渲染、透明与推开，`bush` 机位

- `bush.gdshader`：`BushDark` → `BushLight` 渐变；风摆幅度比矮草大（叶尖 0.22 m，核心 30%）；坦克靠近时叶片被水平推开最多 0.6 m，离开后 0.5 s 回弹；`INSTANCE_CUSTOM.r` 小于 1 时用 4×4 拜耳矩阵做网点透明。
- `BushField`：每丛一个实例，4 种网格变体；每帧算出透明度（离镜头 6 m 内，或与扩大 1 m 的己方车体相交的草丛，0.3 s 内渐变到 30%）和推开强度，只上传变化了的实例。
- `bush` 机位：坦克出生在离出生点最近的草丛区中心，镜头在西侧 17 m 外看着它。

**Files:**
- Modify: `src/Tank.Client/scripts/Cameras/CameraPresets.cs`
- Modify（整体替换）: `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`
- Create: `src/Tank.Client/scripts/World/BushField.cs`
- Create: `src/Tank.Client/shaders/bush.gdshader`

- [ ] **Step 1: 着色器与草丛视图**

创建 `src/Tank.Client/scripts/World/BushField.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws every bush prop with one MultiMesh per mesh variant (bush.gdshader), each instance scaled to its prop's radius
/// and height. Every frame it works out, per bush:
/// - opacity: bushes within <see cref="NearCamera"/> of the camera, and bushes wrapping the player's own tank (hull grown
///   by <see cref="OwnTankMargin"/>), fade to <see cref="SeeThrough"/> over <see cref="FadeSeconds"/> so they never hide
///   your own view; the shader does it with screen-door dithering, so nothing needs sorting;
/// - push: while the tank overlaps a bush its leaves part around the hull; after it leaves they spring back over
///   <see cref="ReboundSeconds"/> (the shader pushes away from the last overlapping tank position).
/// Only instances whose values changed are re-uploaded.
/// </summary>
public partial class BushField : Node3D
{
    public const float NearCamera = 6f;
    public const float OwnTankMargin = 1f;
    public const float SeeThrough = 0.3f;
    public const float FadeSeconds = 0.3f;
    public const float ReboundSeconds = 0.5f;
    private const float PushMargin = 0.4f;     // a bush starts parting this far before the hull touches its edge
    private const float GrowSeconds = 0.12f;   // how fast the leaves get out of the way
    private const int Variants = 4;
    private const float Sink = 0.15f;          // roots a little below the ground on uneven terrain

    private readonly List<Bush> _bushes = new();

    private sealed class Bush
    {
        public required MultiMesh Multimesh { get; init; }
        public required int Index { get; init; }
        public required SN.Vector2 Center { get; init; }
        public required float Radius { get; init; }
        public required float Bottom { get; init; }
        public required float Top { get; init; }
        public float Opacity = 1f;
        public float Push;
        public SN.Vector2 PushFrom;
        public Color Uploaded = new(1f, 0f, 0f, 0f);
    }

    public int Count => _bushes.Count;

    public void Build(IReadOnlyList<Prop> props, HeightField terrain)
    {
        Prop[] bushes = props.Where(p => p.Kind == PropKind.Bush).ToArray();
        ShaderMaterial material = Materials.FromShader("bush");
        for (int v = 0; v < Variants; v++)
        {
            Prop[] mine = bushes.Where((_, i) => i % Variants == v).ToArray();
            if (mine.Length == 0) continue;
            var multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseCustomData = true,
                Mesh = BushShape.Build(31u + (uint)v * 977u).ToArrayMesh(),
                InstanceCount = mine.Length,
            };
            for (int i = 0; i < mine.Length; i++)
            {
                Prop prop = mine[i];
                float ground = terrain.HeightAt(prop.Footprint.Center.X, prop.Footprint.Center.Y) - Sink;
                float radius = prop.Footprint.HalfExtents.X;
                float yaw = (prop.Id * 0.6180339f % 1f) * Mathf.Tau;
                var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(radius, prop.Top - ground, radius));
                multimesh.SetInstanceTransform(i, new Transform3D(basis, prop.Footprint.Center.Ground(ground)));
                multimesh.SetInstanceCustomData(i, new Color(1f, 0f, 0f, 0f));
                _bushes.Add(new Bush { Multimesh = multimesh, Index = i, Center = prop.Footprint.Center, Radius = radius, Bottom = ground, Top = prop.Top });
            }
            AddChild(new MultiMeshInstance3D
            {
                Multimesh = multimesh,
                MaterialOverride = material,
                ExtraCullMargin = 1.5f,   // wind and push move leaves a little outside the instance bounds
            });
        }
    }

    /// <param name="camera">Camera position this frame.</param>
    /// <param name="hull">The player's tank hull footprint.</param>
    public void Update(Vector3 camera, Obb2 hull, float delta)
    {
        Obb2 own = hull with { HalfExtents = hull.HalfExtents + new SN.Vector2(OwnTankMargin) };
        Obb2 pushing = hull with { HalfExtents = hull.HalfExtents + new SN.Vector2(PushMargin) };
        foreach (Bush bush in _bushes)
        {
            var circle = new Obb2(bush.Center, new SN.Vector2(bush.Radius), 0f);
            bool clear = DistanceToBush(camera, bush) < NearCamera || Obb2.TryGetPenetration(own, circle, out _);
            float target = clear ? SeeThrough : 1f;
            bush.Opacity = Mathf.MoveToward(bush.Opacity, target, (1f - SeeThrough) * delta / FadeSeconds);

            if (Obb2.TryGetPenetration(pushing, circle, out _))
            {
                bush.PushFrom = hull.Center;
                bush.Push = Mathf.MoveToward(bush.Push, 1f, delta / GrowSeconds);
            }
            else bush.Push = Mathf.MoveToward(bush.Push, 0f, delta / ReboundSeconds);

            var data = new Color(bush.Opacity, Ease(bush.Push), bush.PushFrom.X, bush.PushFrom.Y);
            if (data == bush.Uploaded) continue;
            bush.Multimesh.SetInstanceCustomData(bush.Index, data);
            bush.Uploaded = data;
        }
    }

    /// <summary>Smoothstep, so the spring-back eases out instead of stopping dead.</summary>
    private static float Ease(float t) => t * t * (3f - 2f * t);

    /// <summary>Distance from the eye to the bush's upright cylinder (0 inside it).</summary>
    private static float DistanceToBush(Vector3 eye, Bush bush)
    {
        float horizontal = MathF.Max(new Vector2(eye.X - bush.Center.X, eye.Z - bush.Center.Y).Length() - bush.Radius, 0f);
        float vertical = eye.Y > bush.Top ? eye.Y - bush.Top : eye.Y < bush.Bottom ? bush.Bottom - eye.Y : 0f;
        return MathF.Sqrt(horizontal * horizontal + vertical * vertical);
    }
}
```

创建 `src/Tank.Client/shaders/bush.gdshader`：

```glsl
// Tall cartoon bushes (MultiMesh, mesh from BushShape). Vertex colour: R = height along leaf/core (0..1),
// G = per-leaf shade, B = 1 on leaves, 0 on the core. INSTANCE_CUSTOM (written by BushField):
//   r = opacity (1 solid; lower = screen-door dithered so the player can see through bushes next to the camera or
//       around their own tank), g = push strength (1 while the tank is inside, springing back to 0 after it leaves),
//   b, a = tank centre (x, z) to push away from; the tank's axes and half size come from the grass globals.
// Leaves sway more than grass; the whole bush lights like one soft ball (normals from BushShape, kept on back faces).
shader_type spatial;
render_mode cull_disabled, world_vertex_coords;

#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 bush_dark : source_color = vec3(0.231, 0.49, 0.18);     // Palette.BushDark
uniform vec3 bush_light : source_color = vec3(0.486, 0.761, 0.263);  // Palette.BushLight
uniform float shade_variation = 0.12;
uniform vec2 wind_direction = vec2(0.8, 0.6);
uniform float wind_strength = 0.22;     // metres of sway at the top of a leaf
uniform float wind_speed = 1.1;
uniform float core_sway = 0.3;          // the core moves this fraction of a leaf
uniform float push_distance = 0.6;      // metres a leaf is pushed out of the tank's way
uniform float push_reach = 1.2;         // leaves this far outside the hull are still pushed (fading out)

global uniform vec4 tank_footprint;     // centre x, centre z, half width, half length (BushField pushes from its own stored centre)
global uniform vec4 tank_axes;          // right x, right z, forward x, forward z

varying float height01;
varying float shade;
varying float opacity;

// 4×4 Bayer threshold in (0, 1) for the screen-door transparency.
float bayer4(vec2 frag) {
	ivec2 p = ivec2(mod(frag, 4.0));
	int i = p.x + p.y * 4;
	const float m[16] = float[16](0.0, 8.0, 2.0, 10.0, 12.0, 4.0, 14.0, 6.0, 3.0, 11.0, 1.0, 9.0, 15.0, 7.0, 13.0, 5.0);
	return (m[i] + 0.5) / 16.0;
}

void vertex() {
	height01 = COLOR.r;
	shade = 1.0 + shade_variation * (COLOR.g - 0.5) * 2.0;
	opacity = INSTANCE_CUSTOM.r;
	vec3 root = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
	float sway_weight = height01 * height01 * mix(core_sway, 1.0, COLOR.b);

	float phase = dot(root.xz, wind_direction) * 0.12 + TIME * wind_speed + root.x * 0.37;
	float gust = sin(phase) * 0.7 + sin(phase * 1.9 + 1.3) * 0.3;
	VERTEX.xz += normalize(wind_direction) * gust * wind_strength * sway_weight;

	// Part around the tank: push horizontally away from the hull box centred at the stored tank position.
	float strength = INSTANCE_CUSTOM.g;
	if (strength > 0.001) {
		vec2 d = VERTEX.xz - INSTANCE_CUSTOM.ba;
		vec2 local = vec2(dot(d, tank_axes.xy), dot(d, tank_axes.zw));
		vec2 half_size = tank_footprint.zw;
		vec2 q = abs(local) - half_size;
		float box_distance = length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0);
		vec2 away_local = box_distance > 0.0 ? local - clamp(local, -half_size, half_size) : (q.x > q.y ? vec2(sign(local.x), 0.0) : vec2(0.0, sign(local.y)));
		vec2 away = normalize(away_local.x * tank_axes.xy + away_local.y * tank_axes.zw + vec2(1e-5));
		float amount = push_distance * strength * (1.0 - smoothstep(0.0, push_reach, box_distance)) * mix(0.3, 1.0, height01) * COLOR.b;
		VERTEX.xz += away * amount;
	}
}

void fragment() {
	if (opacity < 0.999 && bayer4(FRAGCOORD.xy) > opacity) discard;
	if (!FRONT_FACING) NORMAL = -NORMAL;   // undo Godot's back-face flip: keep the bush's ball-shaped normals
	ALBEDO = mix(bush_dark, bush_light, smoothstep(0.0, 1.0, height01)) * shade;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
}
```

- [ ] **Step 2: 机位与接入**

在 `src/Tank.Client/scripts/Cameras/CameraPresets.cs` 中依次做以下替换（每段原文在文件中只出现一次）：

1. 找到：

```csharp
/// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
/// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s.
/// </summary>
```

   替换为：

```csharp
/// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
/// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
/// inside the bush patch nearest the spawn and looks at it from outside the patch.
/// </summary>
```

2. 找到：

```csharp

    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit";

```

   替换为：

```csharp

    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush";

    /// <summary>Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, the rest use the normal spawn.</summary>
    public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;

```

3. 找到：

```csharp
                return (tank + yaw * new Vector3(3.4f, 0.9f, -4.8f), tank + yaw * new Vector3(1.3f, 0.65f, -2.95f));
            case "hill":
```

   替换为：

```csharp
                return (tank + yaw * new Vector3(3.4f, 0.9f, -4.8f), tank + yaw * new Vector3(1.3f, 0.65f, -2.95f));
            case "bush": // the tank parked inside a bush patch, seen from outside it (west, towards the lane)
                return (tank + new Vector3(-16f, 3.4f, 6f), tank + TankFocus);
            case "hill":
```

用以下内容整体替换 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`：

```csharp
using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// Stage-1 offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated.
/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin|orbit|bush|...
/// --fps-log --sniper --screenshot FILE --screenshot-at S --fx-shot S --quit-after S]
/// </summary>
public partial class SandboxMain : Node3D
{
    private const double MaxCatchUp = 0.25;
    private const float AimRange = 900f;
    private const float SniperEyeAboveTrunnion = 0.45f;
    private const float MinAimDistance = 8f;
    private const float GrassCameraLean = 0.4f;   // grass focus sits this fraction of the way from tank to camera
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;
    private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks
    private const double FxShotWarmup = 3;          // --fx-shot: the first shot after this many seconds starts the clock
    private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead

    private readonly SandboxInput _input = new();
    private readonly InputCmd[] _commands = new InputCmd[1];
    private readonly FrameTimeLog _frameLog = new();
    private LaunchOptions _options = new();
    private Simulation _sim = null!;
    private TankState _previous;
    private TankState _current;
    private OrbitCamera _camera = null!;
    private TankModel _tank = null!;
    private GrassField _grass = null!;
    private BushField _bushes = null!;
    private DustTrail _dust = null!;
    private bool _playerFired;
    private ShellViews _shells = null!;
    private BattleFx _fx = null!;
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;
    private SniperVignette _vignette = null!;
    private double _accumulator;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _screenshotTaken;
    private double _firstShotTime = -1;

    public override void _Ready()
    {
        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
        GetWindow().Title = "坦克 3D · 试驾场";
        SandboxInput.RegisterActions();

        _sim = new Simulation(SandboxMap.Create());
        _sim.AddTank(TankCatalog.Medium, CameraPresets.Spawn(_options.CameraPreset), SandboxMap.SpawnYaw);
        _previous = _current = _sim.Tanks[0];
        _aimYaw = _current.Yaw;

        AddChild(Scenery.CreateEnvironment());
        AddChild(Scenery.CreateSun());
        Scenery.ConfigureViewport(GetViewport());
        var terrain = new TerrainMesh();
        AddChild(terrain);
        var dirt = new DirtMask(_sim.Field.Terrain.SizeX, _sim.Field.Terrain.SizeZ, SandboxMap.Seed);
        terrain.Build(_sim.Field.Terrain, dirt);
        var rocks = new RockMeshes();
        AddChild(rocks);
        rocks.Build(_sim.Field.Props, _sim.Field.Terrain);
        _bushes = new BushField();
        AddChild(_bushes);
        _bushes.Build(_sim.Field.Props, _sim.Field.Terrain);
        _grass = new GrassField();
        AddChild(_grass);
        _grass.Build(_sim.Field.Terrain, dirt, _sim.Field.Props);
        if (!CameraPresets.StreamsAroundCamera(_options.CameraPreset))
            _grass.Update(new Vector3(_current.Position.X, 0, _current.Position.Y), int.MaxValue);   // fill what is in range before the first frame
        var trees = new TreeLine();
        AddChild(trees);
        trees.Build(_sim.Field);
        _tank = new TankModel();
        AddChild(_tank);
        _tank.Build(TankCatalog.Medium);
        _dust = new DustTrail();
        _tank.AddChild(_dust);
        _dust.Build(TankLooks.For(TankCatalog.Medium.Id).TrackX, TankCatalog.Medium.HullLength / 2f);
        _shells = new ShellViews();
        AddChild(_shells);
        _fx = new BattleFx();
        AddChild(_fx);
        _camera = new OrbitCamera();
        AddChild(_camera);
        _camera.SetAngles(_current.Yaw, 0.25f);
        if (_options.Sniper) _camera.ToggleSniper();
        var hud = new CanvasLayer();
        AddChild(hud);
        _vignette = new SniperVignette();
        hud.AddChild(_vignette);
        _reticle = new Reticle();
        hud.AddChild(_reticle);
        _status = new StatusPanel();
        hud.AddChild(_status);

        if (_options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit or bush.");
        if (!_options.AutoDrive && !HasPreset) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    private bool HasPreset => CameraPresets.IsKnown(_options.CameraPreset);

    public override void _Input(InputEvent e)
    {
        if (HasPreset) return; // fixed-camera runs ignore stray input so screenshots stay repeatable
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        if (e.IsActionReleased("fire_gun") || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (HasPreset) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when captured:
                _camera.Look(motion.ScreenRelative);
                break;
            case InputEventMouseButton { Pressed: true } wheel when captured && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;
        }
        if (e.IsActionPressed("sniper_toggle")) _camera.ToggleSniper();
        if (e.IsActionPressed("ui_cancel")) Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        _accumulator = Math.Min(_accumulator + delta, MaxCatchUp);
        while (_accumulator >= SimConstants.Dt)
        {
            _accumulator -= SimConstants.Dt;
            StepSimulation();
        }

        float alpha = (float)(_accumulator / SimConstants.Dt);
        TankState tank = Interpolate(_previous, _current, alpha);
        TankPose pose = TankPose.Compute(tank, _sim.Field.Terrain);
        _tank.Apply(pose, tank.TurretYaw, tank.GunPitch);
        _tank.Animate(tank.Speed, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, (float)delta);
        _dust.Update(tank.Speed);
        if (_playerFired)
        {
            _fx.Muzzle(_tank.MuzzleTransform);
            _camera.Shake(FireShake, FireShakeSeconds);
            _playerFired = false;
            if (_firstShotTime < 0 && _time > FxShotWarmup) _firstShotTime = _time;
        }
        _tank.Visible = !_camera.Sniper;
        if (_options.AutoDrive) _camera.SetAngles(tank.Yaw + 0.35f, 0.18f);
        _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
        if (HasPreset)
        {
            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _sim.Field.Terrain.HeightAt);
            _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
        }
        else if (!_input.FreeLook) UpdateAim(pose, tank.Spec);
        float trackHalf = TankLooks.For(tank.Spec.Id).TrackX + 0.29f + FootprintMargin;
        _grass.SetTankFootprint(new Obb2(new SN.Vector2(pose.Position.X, pose.Position.Z),
            new SN.Vector2(MathF.Max(tank.Spec.HullWidth / 2 + FootprintMargin, trackHalf), tank.Spec.HullLength / 2 + FootprintMargin), tank.Yaw));
        // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
        // camera is in the dense band); fixed far-away presets stream around their eye.
        Vector3 tankPos = pose.Position.ToGodot(), cameraPos = _camera.Camera.GlobalPosition;
        _grass.Update(CameraPresets.StreamsAroundCamera(_options.CameraPreset) ? cameraPos
            : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
        _bushes.Update(cameraPos, TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw), (float)delta);
        _shells.Sync(_sim.Shells, alpha);
        UpdateReticle(tank);
        RunAutomation(delta);
    }

    private void StepSimulation()
    {
        var (throttle, steer, fire) = _options.AutoDrive ? AutoDrive() : HasPreset ? ((sbyte)0, (sbyte)0, false) : _input.Sample();
        if (_options.AutoDrive && _options.FxShotDelay is not null) _aimPitch = FxShotPitch;   // shells land in view
        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire);
        _sim.Step(_commands);
        foreach (GameEvent e in _sim.Events)
        {
            if (e.Type == GameEventType.ShellFired)
            {
                bool mine = e.TankId == _current.Id;
                // Flash at the visible barrel tip for the player's tank (the sim event keeps its own muzzle position).
                if (mine)
                {
                    _playerFired = true; // the flash is spawned after this frame's pose update, at the barrel tip as drawn
                    _tank.Fire(_current.TurretYaw);
                }
                else _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
            }
            else if (e.Type == GameEventType.ShellExploded)
            {
                // Shells that stop above the ground hit a rock: grey dust instead of brown.
                bool onRock = e.Position.Y > _sim.Field.Terrain.HeightAt(e.Position.X, e.Position.Z) + 0.5f;
                _fx.Explosion(e.Position.ToGodot(), onRock);
            }
        }
        _previous = _current;
        _current = _sim.Tanks[0];
    }

    private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);

    /// <summary>Casts from the orbit focus along the camera's view; the turret then chases that point.</summary>
    private void UpdateAim(TankPose pose, TankSpec spec)
    {
        Camera3D cam = _camera.Camera;
        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
        SN.Vector3 origin = cam.GlobalPosition.ToSim() + look * (_camera.Sniper ? 0f : _camera.Distance);
        SN.Vector3 end = origin + look * AimRange;
        SN.Vector3 target = _sim.Field.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
        SN.Vector3 trunnion = pose.Trunnion(spec);
        // Looking steeply down puts the target almost under the turret, where yaw swings wildly; keep it at least MinAimDistance out.
        SN.Vector2 flat = new(target.X - trunnion.X, target.Z - trunnion.Z);
        if (flat.Length() < MinAimDistance)
        {
            SN.Vector2 dir = new(look.X, look.Z);
            dir = dir.LengthSquared() > 1e-6f ? SN.Vector2.Normalize(dir) : Angles.Forward(_aimYaw);
            target = new SN.Vector3(trunnion.X + dir.X * MinAimDistance, target.Y, trunnion.Z + dir.Y * MinAimDistance);
        }
        (_aimYaw, _aimPitch) = Angles.AimAngles(target - trunnion, _aimYaw);
    }

    private void UpdateReticle(in TankState tank)
    {
        // Same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).
        Ballistics.Launch launch = Ballistics.Aim(tank, _sim.Field);
        SN.Vector3 impact = launch.BlockPoint;
        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _sim.Field, out impact);
        Camera3D cam = _camera.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f);
        _status.UpdateState(tank.Speed * 3.6f, tank.Reload);
        _vignette.Visible = _camera.Sniper;
    }

    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
    };

    private void RunAutomation(double delta)
    {
        if (_options.FpsLog)
        {
            var rid = GetViewport().GetViewportRid();
            if (_frameLog.Add(delta,
                    RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
                    Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
                    (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
                    (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)) is { } line)
                GD.Print(line);
        }
        double shotAt = _options.FxShotDelay is { } fxDelay ? (_firstShotTime < 0 ? double.MaxValue : _firstShotTime + fxDelay) : _options.ScreenshotAt;
        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= shotAt)
        {
            _screenshotTaken = true;
            Error error = GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
    }
}
```

- [ ] **Step 3: 编译、导入、截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "error|rror\(s\)|arning\(s\)"
```

Expected：`0 Warning(s)`、`0 Error(s)`。

```bash
importall
git status --short src/Tank.Client | grep '\.uid$' || true
```

新文件的 `.uid` 应出现在列表里。

```bash
shot t14-bush bush
shot t14-hill hill
shot t14-front front
godot_run 45 --path src/Tank.Client res://scenes/Sandbox.tscn -- --camera-preset bush --autodrive --screenshot "$PWD/build/shots/t14-drive.png" --screenshot-at 1.2 --quit-after 1.6 2>&1 | grep -E "Screenshot|ERROR"
```

核对：
- `t14-bush`：画面中间有一丛不透明的高草丛挡住了坦克的大半，只能从缝隙和网点里看出坦克；坦克周围包住它的几丛是均匀的网点（约 30% 不透明），能透过它们看到坦克和地面。草丛比坦克高，是"圆球核心 + 向外翘的宽叶"的卡通灌木，下深上浅。
- `t14-hill`：地图上能看到几片成团的高草丛，出生点附近通道东侧有一片，草丛没有压在岩石上，也没有挡在出生点到大山的通道上。
- `t14-front`：出生点附近没有草丛挡住坦克，画面与 Task 10 相同。
- `t14-drive`（坦克从草丛中心起步 1.2 s，车速约 17 km/h）：坦克正从一丛不透明的草丛后面开出来；它刚离开的几丛仍是网点状（0.3 s 内恢复不透明）；输出没有错误。叶片被推开、开出后回弹的效果要在动态中看，留到 Task 17 试玩确认。

如果网点在 FXAA 下闪烁得厉害或者整片发灰，先把 `BushField.SeeThrough` 调到 0.35～0.4 再看；仍不行就在 `bush.gdshader` 里换 8×8 拜耳矩阵（设计 §10 的对策），并在汇报中说明。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 4: 提交**

```bash
git add src/Tank.Client
git commit -m "feat(client): tall bushes with wind, push-out and see-through fade"
```

### Task 15：性能检查（原阶段 1.5 计划 Task 17）

**Files:**
- 视情况修改：`src/Tank.Client/scripts/World/Scenery.cs`、`src/Tank.Client/scripts/World/GrassField.cs`、`src/Tank.Client/shaders/grass.gdshader`

- [ ] **Step 1: 环视与绕车测帧**

测帧期间保持游戏窗口在最前面，不要切换应用：

```bash
fpslog perf-spin spin 15
fpslog perf-orbit orbit 15
fpslog perf-bush bush 10
```

验收：去掉第一行、以及窗口被系统限速的行（整齐的 `60 fps`），`spin` 和 `orbit` 各有至少连续 10 行 ≥ 60 fps。写计划时实测这两个机位不限速时约 145 fps。如果全部是整齐的 60 fps，说明窗口一直被限速：重跑，并在开始后立即点一下游戏窗口。

- [ ] **Step 2: 自动驾驶测帧**

覆盖开炮、特效、扬尘、草格子边走边生成：

```bash
godot_run 50 --path src/Tank.Client --disable-vsync res://scenes/Sandbox.tscn -- --autodrive --fps-log --quit-after 20 2>&1 | tee build/shots/perf-autodrive.log | grep -E "fps-log|ERROR"
```

验收同上：除第一行外没有低于 60 fps 的行。第一次开炮那一秒允许一次尖峰（粒子管线首次编译）。

- [ ] **Step 3: 不达标时按顺序调整，每改一项重测一次**

| 顺序 | 调整 | 位置 |
|---|---|---|
| 1 | 渲染缩放 1.0 → 0.85（自动启用 FSR 1） | `Scenery.RenderScale` |
| 2 | 阴影最远距离 300 → 200 m | `Scenery.CreateSun` 的 `DirectionalShadowMaxDistance` |
| 3 | 草最远一档密度 0.9 → 0.6 | `GrassField.LodDensity` |
| 4 | 草半径 90 → 72 m，同时 `fade_start`/`fade_end` 改为 52/68 | `GrassField.Radius`、`grass.gdshader` |

把 Step 1、2 的平均和最差帧时间、fps、draws、prims 写进汇报。

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

- [ ] **Step 4: 提交（有调整才提交）**

```bash
git add src/Tank.Client
git commit -m "perf(client): tune render scale and grass for 60 fps on M3"
```

### Task 16：最终截图对比、全量测试与执行记录（原阶段 1.5 计划 Task 18）

**Files:**
- Modify: `docs/superpowers/plans/2026-10-14-cartoon-restyle.md`（本文件，加执行记录）
- Modify: `docs/superpowers/plans/2026-10-13-stage1.5-visuals.md`（在其 Task 16～19 标题下注明"已并入 2026-10-14-cartoon-restyle 计划"）

- [ ] **Step 1: 全部测试与构建**

```bash
dotnet test tests/Tank.Core.Tests 2>&1 | tail -1
dotnet test tests/Tank.Sim.Tests 2>&1 | tail -1
dotnet test tests/Tank.Net.Tests 2>&1 | tail -1
dotnet test tests/Tank.Update.Tests 2>&1 | tail -1
dotnet test tests/Tank.Art.Tests 2>&1 | tail -1
dotnet build src/Tank.Server -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"
dotnet build src/Tank.Client/Tank.Client.csproj -c Release 2>&1 | grep -E "rror\(s\)|arning\(s\)"
git diff main --stat -- src/Tank.Sim
```

Expected：
- 测试依次为 62、132、50、42、150 个，全部通过。
- 两个构建都是 0 警告、0 错误。
- `src/Tank.Sim` 的改动只有 `Prop.cs`、`Battlefield.cs`、`TankMotion.cs`、`SandboxMap.cs`。

- [ ] **Step 2: 卡通版截图**

```bash
for p in front side hill close orbit bush closerear sprocket; do shot cartoon-$p $p; done
fxshot cartoon-fx 0.25 --camera-preset front
godot_run 45 --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --sniper --screenshot "$PWD/build/shots/cartoon-sniper.png" --screenshot-at 7.5 --quit-after 8 2>&1 | grep -E "Screenshot|ERROR"
```

- [ ] **Step 3: 逐对比较**

用 Read 依次打开 Task 1 留下的 `stage15-<机位>.png` 和对应的 `cartoon-<机位>.png`（front、side、hill、close、orbit 五对），每对用一两句话写清变化，重点：
- 天空：照片 → 程序化渐变和卡通云。
- 地面：照片质感 → 纯色加色斑；草 → 宽钝的卡通草。
- 岩石：贴图巨石 → 圆润的浅蓝灰石头。
- 坦克：写实涂装 → 卡通迷彩、底部渐暗、圆角。
- 光影：AgX、SSAO、灰暗阴影 → 明亮、蓝紫色浅阴影、无纯黑。
- 新增：高草丛（`cartoon-bush`）、卡通特效（`cartoon-fx`）、界面（全部截图左下角、`cartoon-sniper`）。

再列出仍然看着不对劲的地方，交给 Task 17。

- [ ] **Step 4: 写执行记录**

在本文件 `## 写计划时已核实、与设计的差异` 之前插入 `## 执行记录（YYYY-MM-DD，已完成）`，内容：
- 执行中偏离本计划的地方（每条写清原因）。
- 调过的参数和最终数值。
- Task 15 的帧时间（各机位的平均/最差、fps、draws、prims），是否启用了 FSR。
- 测试数：Tank.Sim 132、Tank.Art 150。

在阶段 1.5 计划的 Task 16、17、18、19 标题下各加一行：`> 已并入 2026-10-14-cartoon-restyle.md（卡通改造计划）执行。`

```bash
git add docs/superpowers/plans
git commit -m "docs: record cartoon restyle execution notes in the plans"
```

```bash
pgrep -fl 'MacOS/Godot' || echo "no Godot left"
```

必须输出 `no Godot left`；如果还有本任务启动的 Godot，用 `kill -9` 结束它。

### Task 17：用户试玩与收尾（原阶段 1.5 计划 Task 19）

**Files:**
- 视情况修改：`src/Tank.Client/scripts/Art/Palette.cs`、`src/Tank.Client/shaders/*.gdshader*`、`src/Tank.Client/scripts/World/*.cs`、`src/Tank.Client/scripts/Combat/*.cs`、`src/Tank.Client/scripts/Overlay/*.cs`、`src/Tank.Client/scripts/Vehicles/TankLooks.cs`

- [ ] **Step 1: 交给用户试玩**

请用户运行（先 `dotnet build src/Tank.Client/Tank.Client.csproj`）：

```bash
godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn
```

并逐项确认：

| # | 检查项 |
|---|---|
| 1 | 第一眼就是柔和的卡通风格，接近 TABG：颜色鲜亮、对比柔和、阴影偏蓝紫、没有纯黑，没有照片纹理 |
| 2 | 坦克：迷彩色块大而柔和，底部渐暗，圆角明显；不显得"塑料" |
| 3 | 行驶时履带滚动、轮子转动、车体点头都正常；履带后方冒浅棕色小尘团 |
| 4 | 开炮：黄色闪光、白烟团依次喷出再缩小、炮管后坐、镜头轻震 |
| 5 | 落点：橙色火球、白灰烟团散开再缩小、小石块飞散；打岩石烟偏灰 |
| 6 | 草、树、草丛随风摆动；开远后草在边缘自然淡出 |
| 7 | 向北偏东约 40 m 有一片高草丛：能开进去、速度不变；在草丛里镜头附近和包住自己的草丛变成网点，看得见自己；叶片被车推开，开出后回弹 |
| 8 | 从草丛外看（可以停在草丛里再把镜头拉远绕到外侧），其他没有包住坦克的草丛能挡住坦克 |
| 9 | 界面：字体圆润清楚；准星落点圈和装填环清楚，装填完成时弹一下；左下状态栏的速度、装填一目了然；Shift 进入狙击镜有暗角和倍率 |
| 10 | 全程流畅，没有明显卡顿 |

- [ ] **Step 2: 按反馈调整**

只改参数，不改结构：
- 颜色：改 `Palette` 里的值，同时改着色器里标着 `// Palette.名字` 的默认值（`PaletteTests` 会检查两者一致），并运行 `dotnet test tests/Tank.Art.Tests`。
- 光照：`toon_light.gdshaderinc` 的 `toon_shadow_mix`、`toon_shadow_level`、`toon_wrap`；`Scenery` 的 `SunEnergy`、`AmbientEnergy`、`FogDensity`。
- 草丛：`BushField` 顶部常量（`NearCamera`、`SeeThrough`、`FadeSeconds`、`ReboundSeconds`），`bush.gdshader` 的 `wind_strength`、`push_distance`。
- 特效：`BattleFx`、`DustTrail` 顶部常量和粒子参数；`ParticleKit.Pop` 的曲线。
- 界面：`Reticle`、`StatusPanel` 顶部常量，`HudStyle.Embolden`。

每轮调整后重拍对应截图确认。

- [ ] **Step 3: 提交（有调整才提交）**

```bash
git add src/Tank.Client tests
git commit -m "chore(client): tune cartoon visuals after playtest"
```

- [ ] **Step 4: 合并**

使用 superpowers:finishing-a-development-branch，按既定方式 squash merge 进 `main`（这样 `main` 的历史里不会留下照片素材）。

---

## 附：计划自查

**设计覆盖**

| 设计章节 | 任务 |
|---|---|
| §2 风格目标（纯色、高饱和低对比、蓝紫暗部、无纯黑、圆润、无描边） | Task 2–9（每个任务的截图核对项） |
| §2 性能目标 60 帧 | Task 2（关 MSAA、FXAA）、Task 15 |
| §3 调色板 `Palette`，着色器默认值同源 | Task 2（`PaletteTests` 检查 `// Palette.名字` 标记），之后每个着色器都打了标记 |
| §4 共用光照：包裹漫反射 w=0.5、阴影向 Shadow 混合约 50%、边缘光 pow(1−N·V,3)×0.25 仅坦克和岩石、无高光、环境光取天空色 0.6 | Task 3（`toon_light.gdshaderinc`）、Task 2（环境光）、Task 5、Task 7（边缘光 0.25） |
| §5.1 程序化天空、光晕、卡通云、太阳方向不变 | Task 2 |
| §5.2 Linear 曝光 1.0、关 SSAO、弱泛光、指数雾取 SkyHorizon、关 MSAA 用 FXAA、渲染缩放 1.0/0.85+FSR | Task 2、Task 15（FSR 作为第一项调整） |
| §5.3 `terrain_toon`、分层规则与 `GroundCover` 一致、纯色加 40 m/12 m ±12% 色斑、平滑过渡、远景同色 | Task 3 |
| §5.4 草：更宽更短 0.25–0.45 m、钝的单顶点叶尖、GrassDark→GrassLight、实例明暗、风/淡出/压平保留 | Task 4 |
| §5.5 岩石：低噪声更圆、Rock→RockTop、边缘光、碎石同色稍浅、贴合规则不变 | Task 5 |
| §5.6 树：阔叶 3–5 个低噪声圆球、暖棕树干；针叶 3–4 层圆边圆锥；色相变化、底部稍暗；林带不变 | Task 6 |
| §5.7 坦克：倒角 +50%、`tank_toon` 三色平滑迷彩、底部 ~80%、轮毂浅一档、胶圈 Track、细节件同材质 | Task 7 |
| §5.8 特效：炮口闪光 0.08 s、3–5 个烟团弹性缓动 ~0.8 s；火球、6–10 个烟团、8 块碎屑、岩石偏灰；>10 km/h 扬尘；黄色短拖尾；开炮震动；池化 | Task 1（检查点）、Task 9 |
| §5.9 界面：可再分发的粗圆体中文字体及许可证；准星 3 px 白圈+描边+白点；装填环 UiAccent、完成时 1→1.25→1/0.2 s；状态栏圆角底板+图标+描边大数字；狙击暗角、倍率在准星下 | Task 8（字体）、Task 10 |
| §6.1 `PropKind.Bush`、`IsSolid`、碰撞和射线跳过、`BushesNear`、圆形占地、6 片 6–12 丛 10–25 m 平地、最近一片在通道旁约 40 m、半径 1.8–3.2、高 3–4、固定种子；三类测试 | Task 11、Task 12 |
| §6.2 程序化草丛（30–50 叶片、1–2 个核心球、尺寸取自物件）、MultiMesh、`bush.gdshader` 渐变+风+推开 0.6 m/回弹 0.5 s（复用 `tank_footprint`）、6 m 内和包住己方坦克的草丛网点透明约 70%、0.3 s 渐变 | Task 13、Task 14 |
| §7 删除 Poly Haven 13 个文件及 `.import`、`fetch_assets.py`、CREDITS 条目；加字体与许可证、更新 CREDITS；squash merge | Task 8、Task 17 |
| §8 保留的结构；替换的着色器；`Scenery`；未提交的粒子代码先保留结构再改观感；阶段 1.5 的 Task 17–19 并入 | Task 1、Task 2–9、Task 15–17（Task 16 在旧计划里注明） |
| §9 验收：测试、front/side/hill/close/orbit/bush 截图对比、orbit 和 spin 各 10 秒 60 帧、试玩（含开进草丛） | Task 16、Task 15、Task 17 |
| §10 风险：坦克"塑料感"、网点闪烁（8×8 备选）、字体许可证、去 MSAA 后的锯齿 | Task 7 核对项、Task 14 Step 3、Task 8 Step 3、Task 4/15 |

**占位检查**：全文没有 TBD、TODO 或"类似 Task N"。每个新建或整体替换的文件都给出完整内容；对现有文件的修改以"找到 / 替换为"给出，每段原文都在当时的文件中只出现一次。这些内容由脚本从临时副本导出，再从起点逐任务重放验证：每个任务后客户端编译 0 警告 0 错误，测试数与各任务写明的一致，最终文件与副本逐字一致。

**命名一致性**：以下名称在各任务中保持一致——
- `Palette`（`GrassLight`…`UiAccent`、`Leaves`、`Needles`、`Bark`、`Flash`、`Fire`、`Smoke`、`RockDust`、`SoilDust`、`UiText`、`UiOutline`、`UiPanelAlpha`、`Hex`、`Linear`）、`PaletteColors.ToColor`
- 着色器参数 `toon_shadow_color`、`toon_shadow_mix`、`toon_shadow_level`、`toon_wrap`、`toon_rim_color`、`toon_rim_strength`、函数 `toon_rim`；全局参数 `tank_footprint`、`tank_axes`、`grass_focus`（已在 `project.godot` 声明）
- `Scenery.SunDirection/RenderScale/CreateEnvironment/CreateSun/ConfigureViewport`、`Materials.FromShader/With`
- `GrassClump.Segments/VerticesPerBlade/TrianglesPerBlade/MinHeight/MaxHeight`、`TreeShapes.BlobSubdivisions/Tier`、`TankParts.HullBevel/TurretBevel/DetailBevel/MantletBevel`、`MeshAssert.MaxCreaseDegrees`
- `BushShape.Build/Core/LeafRows/VerticesPerLeaf/CoreSubdivisions/ShadingCentreY`、`BushField.Build/Update/NearCamera/OwnTankMargin/SeeThrough/FadeSeconds/ReboundSeconds`
- `PropKind.Bush`、`Prop.IsSolid`、`Battlefield.BushesNear`、`SandboxMap.BushPatches/BushPatchRadius`、`CameraPresets.Spawn`、机位名 `bush`
- `HudStyle`、`Reticle.UpdateState(Vector2?, float, float)`、`StatusPanel.UpdateState(float, float)`、`SniperVignette`
- `BattleFx.Muzzle(Transform3D)/Explosion(Vector3, bool)`、`DustTrail.Build/Update`、`ParticleKit.Ball/Puff/Glow/Pop/OneShot`
- 启动参数 `--fx-shot`、`--sniper`（`LaunchOptions.FxShotDelay/Sniper`）

**Godot API 核对**：全部 C# 代码都已对 GodotSharp 4.7.2 编译通过；以下成员已在 `~/.nuget/packages/godotsharp/4.7.2/lib/net8.0/GodotSharp.xml` 中逐条找到：
- `Environment.ToneMapper.Linear`、`Environment.AmbientSource.Color`、`Environment.ReflectionSource.Disabled`、`Environment.AmbientLightColor`、`Environment.GlowHdrThreshold`、`Environment.FogLightColor`、`Environment.FogSkyAffect`
- `Viewport.Msaa3D`、`Viewport.Msaa.Disabled`、`Viewport.ScreenSpaceAAEnum.Fxaa`、`Viewport.Scaling3DMode`、`Viewport.Scaling3DModeEnum.Fsr`、`Viewport.Scaling3DScale`、`RenderingServer.DirectionalSoftShadowFilterSetQuality`
- `ShaderMaterial`、`Sky.RadianceSizeEnum.Size64`、`DirectionalLight3D.ShadowMode.Parallel4Splits`
- `GpuParticles3D.AmountRatio`、`GpuParticles3D.Restart`、`ParticleProcessMaterial.Color/ScaleCurve/EmissionSphereRadius/AngularVelocityMin`、`Curve.AddPoint`、`Curve.MaxValue`、`CurveTexture`、`SphereMesh`、`CapsuleMesh`、`Tween.TransitionType.Back`
- `MultiMesh.UseCustomData`、`MultiMesh.SetInstanceCustomData`、`MultiMesh.SetInstanceTransform`、`GeometryInstance3D.ExtraCullMargin`
- `FontFile`、`FontVariation.BaseFont`、`FontVariation.VariationEmbolden`、`Font.GetStringSize`、`CanvasItem.DrawStringOutline`、`CanvasItem.DrawString`、`CanvasItem.DrawArc`、`CanvasItem.DrawCircle(Vector2, float, Color, bool, float, bool)`、`CanvasItem.DrawColoredPolygon`、`CanvasItem.DrawStyleBox`、`StyleBoxFlat.SetCornerRadiusAll/CornerDetail/AntiAliasing/BgColor`、`GradientTexture2D.FillEnum.Radial`、`TextureRect.StretchModeEnum.Scale`

着色器只用了 Godot 着色语言的标准内置量和渲染模式：`light()` 中的 `NORMAL`、`LIGHT`、`LIGHT_COLOR`、`ATTENUATION`、`LIGHT_IS_DIRECTIONAL`、`ALBEDO`、`DIFFUSE_LIGHT`；`fragment()` 中的 `FRONT_FACING`、`FRAGCOORD`、`VIEW`、`EMISSION`、`discard`；`vertex()` 中的 `INSTANCE_CUSTOM`、`COLOR`、`MODEL_MATRIX`、`TIME`；天空着色器的 `EYEDIR`、`LIGHT0_DIRECTION`；`global uniform`；`world_vertex_coords`、`cull_disabled`；`#include`。全部在 Metal 后端（Apple M3）实际运行过，没有着色器错误。

**已知的不确定点**：
- `ViewportGetMeasuredRenderTimeGpu` 在 Metal 上返回 0，`--fps-log` 的 `gpu` 一栏是 `n/a`；`process` 一栏在窗口被限速时没有参考意义，以 fps 为准。
- macOS 的限速无法从命令行可靠关闭：`autodrive` 只测到限速下稳定 60 帧。
- 草丛网点透明会让它们的影子也变成网点，实测不显眼。
