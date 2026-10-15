# 阶段 2A：战斗核心 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在试驾场里打 6 辆静止靶车：血量、车体和炮塔两个装甲盒、穿甲弹和高爆弹的击穿与跳弹、散布与缩圈、换弹种和有限弹药、击毁（炮塔飞起、焦黑、冒烟）与 10 秒后复活；界面上有命中大字、伤害数字、头顶血条、散布圈，左下角面板加血条和弹药栏。

**Architecture:** 全部战斗规则写在不依赖 Godot 的 `Tank.Sim` 里：命中检测用三维有向盒（`OrientedBox`），击穿、伤害、溅射是纯函数（`Combat/Penetration`），散布是纯函数（`Combat/Dispersion`），随机数由 `(tick, tankId, shotIndex)` 推出的种子加不同的盐得到（`Combat/ShotRandom`），所以同样的输入永远得到同样的结果，阶段 4 联网时服务器和客户端直接共用。`Simulation` 拆成两个分部类文件：`Simulation.cs` 管坦克（行驶、瞄准、散布、换弹、开火、复活、补给），`Simulation.Shells.cs` 管炮弹（飞行、命中装甲、爆炸、溅射、击毁）。客户端只根据仿真事件做表现：`TargetRange` 管靶车模型和复活计时，`CombatPopups`、`HealthBars`、`StatusPanel`、`Reticle` 管界面；炮塔飞行轨迹 `TurretToss` 和弹出动画曲线 `PopupCurves` 不依赖 Godot，在 `Tank.Art.Tests` 里测试。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、Godot 4.7.2 .NET 版（Forward+，macOS 上走 Metal）、Godot 着色语言

**设计依据：** `docs/superpowers/specs/2026-10-14-stage2a-combat-design.md`（全文）；上级文档 `docs/superpowers/specs/2026-10-13-third-person-tank-design.md` §4.3、§4.4、§5.1～§5.4。

---
## 写计划时已核实、与设计的差异

写这份计划时，先在临时 worktree（`/Users/murphy/Desktop/dev/BattleCity-stage2a-draft`，分支 `draft/stage2a-combat`，从 `feat/stage2a-combat` 的 `85722f3` 拉出）里按任务顺序把下文全部实现了一遍，一个任务一个提交。下文的代码和补丁由脚本从这些提交直接导出，没有手抄。实测结果：

- 每个提交单独检出后：客户端编译 0 错误，5 个测试项目全过。Sim 测试数依次是 160、167、179、185、194、205、221、258（Task 1～8），Art 依次是 183、183、187（Task 9～11）；Core 62、Net 50、Update 42 不变。
- 截图逐张看过：蓝灰色靶车；击穿（红）、跳弹（黄）、未击穿（灰）三种大字和“-200”伤害数字、高爆弹未击穿“-41”；炮塔带着炮管飞起约 4 m、落在靶车右侧；车身焦黑、冒黑烟；10 秒后复活；新的左下角面板；行进开炮后散布圈变大。
- 帧率：关垂直同步，`spin`、`orbit` 机位不限速时 210～330 帧，最差帧 13 ms 以内。
- 草稿经过一轮代码审查：没有严重问题。按审查意见改了一处（炮弹起点就在别的坦克装甲盒内时，原来会按“最近的面”算成擦边跳弹，改为取“迎着炮弹飞行方向的面”），并补了 5 个测试（平行于面、穿过棱、起点在盒内、两辆坦克前后排时取近的、给活着的坦克 Revive）。

与设计文字不同、但有意为之的地方（执行时不要“改回去”）：

- **`HitResult` 多一个 `None`**，排在第一位，是非命中事件的默认值，免得 `ShellFired` 等事件的 `Result` 看上去像“击穿”。
- **打中残骸也发 `ShellExploded`**：设计只说穿甲弹消失、高爆弹照常爆炸；这里穿甲弹也在残骸上发一个 `ShellExploded`（没有溅射），客户端好在那里冒一团烟，不然炮弹会在半空无声消失。
- **随机数种子取开火那一 tick**：`ShotRandom.Seed(开火 tick, 射手 id, 射手的第几发)`，存进炮弹，散布、穿深浮动、伤害浮动各用不同的盐。炮弹还带上自己的弹种数据（`Round`）和口径，命中时不用回头查射手。
- **`Revive` 的事件由下一次 `Step` 报告**：`Step` 开头会清空事件表，所以 `Revive` 把事件先放进待发队列，下一个 `Step` 再报，tick 记为那一步。
- **散布的转动项**：车体转速、炮塔转速都按“这一 tick 的角度变化 / Dt”算，炮塔用相对车体的转角。车体原地转向时炮塔反向转动保持瞄准，两项会同时生效（各自封顶 1 倍）。行驶项用前进最高速归一，倒车同样按它算。
- **装甲盒的定义方式**：`ArmorBox(Width, Length, Bottom, Top, Armor)`，以锚点（车体原点或炮塔座圈）为中心，比直接给盒心和半尺寸更贴近设计表的写法。
- **残骸不显示头顶血条**；复活后再显示。
- **散布圈严格按比例画**：第三人称下静止时半径只有约 2 px，全速行驶约 7 px，行进开炮后约 10～15 px，比准星的实心圈（17 px）还小，看起来是圈内的一个小黄圈；狙击镜 2×/4×/8× 下按倍率放大。设计要求“半径是当前散布圈在瞄准点距离上的投影”，所以没有放大；试玩时请用户确认够不够明显（Task 13 列为待确认项）。
- **命中大字画在命中点上方 52 px**，不压在准星下面；伤害数字是红色，溅射伤害小一号。
- **新增调色板颜色**：设计给出的 `TargetBody` #6F8FB5、`Charred` #2B2622 之外，还有 `HitPierced` #FF4D4D（击穿、伤害数字、低血量）、`HitBlocked` #B9BDC8（未击穿、弹药为 0）、`Health` #6CD45A（血条）、`WreckSmoke` #4A4650（残骸黑烟）。“跳弹！”用现有的 `UiAccent`。
- **穿甲弹打中装甲**时在命中点放一个小号的灰色爆炸（`BattleFx.Explosion` 新增 `size` 参数）；坦克被击毁时放一个 1.8 倍的大爆炸。
- **截图用的新参数**：`--range-demo pen|ricochet|nopen|he|kill` 把玩家坦克停在 0 号靶车前方 45 m、与靶车正前方成 0°/76°/60° 的位置，瞄准车体前装甲或炮塔前装甲中心，3 秒后开炮（`kill` 一直开到击毁）；`--shot-after fire|hit|kill:秒` 在玩家第一次开炮、第一次命中坦克、第一次击毁坦克之后定时截图；新机位 `target` 从 0 号靶车右前方看它（炮塔落在那一侧）。
- **靶车位置**（设计只给了要求，这里是用脚本在地图上搜出来、再由测试校验的）：正面 (160, 304) 48 m；侧面 (248, 300) 57 m；45° (216, 244) 87 m；车尾 (140, 256) 95 m；半躲在石头后 (280, 328) 80 m（从出生点看，车体两侧的三条视线里有两条被石头挡住）；远处 (380, 272) 189 m。
- **`SandboxMap` 的 `BigHill`、`LaneHalfWidth`、`DistanceToSegment` 改为 `internal`**，靶车测试要用。
- **`Simulation.TankRef(id)`** 是 `internal`，测试用它直接把血量改成 1 来测击毁，`Revive`、`Restock` 也用它找坦克。

## 留给以后的问题（不在本阶段处理）

- **跨平台确定性**：`MathF.Exp`、`MathF.Acos`、`Quaternion`、`Vector3.Normalize` 的结果在 x64 和 ARM 上不保证逐位相同。试驾场没有影响；阶段 4 联网前要决定是否改成定点数，或在预测里容忍误差。
- **输入里的弹种默认是穿甲弹**：`InputCmd` 不写 `Ammo` 就是 AP，装着高爆弹的坦克收到这样的输入会换回穿甲弹并重新装填。以后的 AI、回放、网络代码每个 tick 都必须带上弹种。
- **高爆溅射不考虑遮挡**，只算到车体盒的距离（符合设计 §3.7）。
- 草丛仍然不挡炮弹；部件损伤、跳弹后的二次弹道都没做（设计已排除）。
## 执行前须知

- **工作目录**：直接在现有 worktree 里执行，不要新建：`/Users/murphy/Desktop/dev/BattleCity-stage2a`（分支 `feat/stage2a-combat`）。下文所有路径都相对于它。
- 每个 shell 先执行下面这段。`dotnet` 不在默认 PATH 里；`godot_run` 保证 Godot 无论如何都会被强制结束（曾有一次 `--quit-after` 卡住 15 小时）。

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage2a
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
# demoshot NAME DEMO EVENT:SECONDS QUIT [MORE ARGS]: scripted shooting (--range-demo), screenshot SECONDS after EVENT
demoshot() {
  local name=$1 demo=$2 after=$3 quit=$4; shift 4
  godot_run $(( ${quit%.*} + 30 )) --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo "$demo" --shot-after "$after" \
    --screenshot "$PWD/build/shots/$name.png" --quit-after "$quit" "$@" 2>&1 | grep -E "Screenshot|ERROR|SHADER|Exception"
}
# fpslog NAME PRESET SECONDS: frame-time log with vsync off, also kept in build/shots/NAME.log
fpslog() {
  godot_run $(( $3 + 30 )) --path src/Tank.Client --disable-vsync res://scenes/Sandbox.tscn -- --camera-preset "$2" \
    --fps-log --quit-after "$3" 2>&1 | tee "build/shots/$1.log" | grep -E "fps-log|ERROR|SHADER|Exception"
}
# importall: let Godot generate .uid files for new scripts and shaders
importall() { godot_run 240 --headless --path src/Tank.Client --import > /dev/null 2>&1; }
```

  `demoshot` 要等 Task 10 加入 `--range-demo` 和 `--shot-after` 之后才能用。
- **补丁的用法**：修改已有文件的步骤给的是 `git apply` 补丁，在仓库根目录原样运行即可（heredoc 用了 `'PATCH'`，内容不会被 shell 展开）。打不上时先 `git status` 看是不是漏了前面的步骤，不要手工改。新建的文件给完整内容，用 Write 工具写入。
- **从命令行启动的 Godot 不会编译 C#**：改了 `.cs` 之后，截图前一定先 `dotnet build src/Tank.Client/Tank.Client.csproj`，否则跑的还是旧程序集。只改着色器则不需要。
- **截图**：
  - 截图存在 `build/shots/`（已被 `.gitignore` 忽略）。
  - 每张截图都**必须用 Read 工具打开**，逐条核对该步列出的要求；不符合就先修再继续。
  - 输出里出现 `SHADER ERROR`、`ERROR:` 或 `Exception` 时先修复。没有出现 `Screenshot saved` 说明出错或事件没发生，看完整输出。
  - 游戏窗口会抢键盘焦点。带 `--camera-preset` 或 `--range-demo` 时游戏忽略所有输入；`--autodrive` 截图期间不要碰键盘。
- **测帧**：macOS 会把不在最前面的窗口限制在 60 帧，日志会整齐地显示 `60 fps`，这不是真实开销；前 7～20 秒的数据不算，至少跑 30 秒。测帧时不截图。
- **每个任务结束前**都运行一次 `pgrep -fl 'MacOS/Godot'`，确认本任务启动的 Godot 都已退出。
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`。`Tank.Sim` 和两个测试项目里任何警告都会让构建失败。`Tank.Art.Tests` 还会编译客户端里几份不依赖 Godot 的源码（本计划新增 `TurretToss.cs`、`PopupCurves.cs`），它们同样不能有警告、不能引用 Godot。客户端项目关掉了该选项，但也要保持 0 警告。
- 新增 `src/Tank.Client` 下的脚本后，提交前运行 `importall`，把生成的 `.cs.uid` 一起提交。下文的 `git add` 按目录添加，会带上它们。
- **不要改**：旧 Battle City 代码（`src/Tank.Core`、`src/Tank.Net`、`src/Tank.Server`，客户端的 `scripts/Bootstrap/` 中除 `LaunchOptions.cs` 以外的文件、`scripts/UI/`、`scripts/View/`、`scripts/Controls/`）和 `project.godot`。
- **坐标约定**（同前几个阶段）：Y 向上，−Z 为前方；偏航 0 朝 −Z，正值向左转；车体空间原点在接地中心。`TankState.Position` 是二维 `(x, z)`。
- **测试项目与数量**：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。开始前是 Core 62、Sim 144、Net 50、Update 42、Art 178。每个任务末尾写明了做完之后 Sim 和 Art 的测试数；对不上就说明漏了或多了测试。
- 提交信息用约定式前缀（feat/fix/test/docs/chore）。每完成一个任务，在 `docs/PROGRESS.md` 的“2A 节点记录”里追加一行（任务名、提交号、测试数），和该任务的代码一起提交或紧接着提交。
- 分支最后用 squash merge 并入 `main`，不推送远程，除非用户要求。
## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Geometry/OrientedBox.cs` | 新建 | 三维有向盒：线段求交（返回进入的面）、点到盒的距离 |
| `Combat/ShotRandom.cs` | 新建 | 由 `(tick, tankId, shotIndex)` 推出种子，按盐取 [0,1) 随机数 |
| `Combat/AmmoType.cs` | 新建 | 弹种 `AP`、`HE` |
| `Combat/ShellSpec.cs` | 新建 | 一种炮弹的穿深、伤害、初速、溅射半径、载弹量 |
| `Combat/Armor.cs` | 新建 | `ArmorFace`、`HitPart`、五个面的装甲厚度 `Armor`、装甲盒 `ArmorBox` |
| `Combat/BloomFactors.cs` | 新建 | 行驶、车体转向、炮塔转动、开炮四项散布扩大系数 |
| `Combat/Penetration.cs` | 新建 | 入射角、跳弹、等效装甲、穿甲弹和高爆弹的命中结果、溅射伤害 |
| `Combat/Dispersion.cs` | 新建 | 散布扩大目标值、缩圈、圈半径、开火偏移 |
| `Combat/ArmorHit.cs` | 新建 | 线段对车体盒和炮塔盒求交，取近的，给出命中面和装甲厚度 |
| `Combat/Shell.cs` | 修改 | 炮弹带上弹种、弹种数据、口径、随机种子 |
| `Combat/Ballistics.cs` | 修改 | 初速取当前弹种 |
| `Vehicles/TankSpec.cs`、`TankCatalog.cs` | 修改 | 新增战斗参数，去掉 `ShellSpeed`；中坦填设计表数值 |
| `Vehicles/TankState.cs` | 修改 | 血量、是否击毁、当前弹种、两种弹药剩余、散布扩大量 |
| `Vehicles/TankPose.cs` | 修改 | 世界坐标的车体盒、炮塔盒 |
| `InputCmd.cs` | 修改 | 新增想用的弹种 |
| `GameEvent.cs` | 修改 | 四种新事件、`HitResult`、四个新字段 |
| `Simulation.cs` | 修改 | 改为分部类；散布更新、换弹种、弹药、补给、复活；残骸不动 |
| `Simulation.Shells.cs` | 新建 | 炮弹飞行、命中装甲、爆炸与溅射、扣血与击毁 |
| `Maps/TargetSpot.cs` | 新建 | 靶车的位置和朝向 |
| `Maps/SandboxMap.cs` | 修改 | 6 个靶车位置 |

**客户端 `src/Tank.Client`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `scripts/Art/Palette.cs` | 修改 | 靶车蓝灰、焦黑和 4 种界面/特效颜色 |
| `shaders/tank_toon.gdshader` | 修改 | `char_amount` 把整车混向焦黑色 |
| `scripts/Vehicles/TurretToss.cs` | 新建 | 炮塔飞起的轨迹和转动（不依赖 Godot，可测试） |
| `scripts/Vehicles/TankModel.cs` | 修改 | 可选涂装；炮塔下加一个飞起用的支点；`Wreck`/`Restore` 与焦黑渐变 |
| `scripts/Combat/WreckSmoke.cs` | 新建 | 残骸上持续冒的黑烟 |
| `scripts/Combat/BattleFx.cs` | 修改 | 爆炸可调大小 |
| `scripts/Combat/ShellViews.cs` | 修改 | 曳光按弹种上色（穿甲黄、高爆橙） |
| `scripts/Sandbox/TargetRange.cs` | 新建 | 6 辆靶车：加入仿真、模型、击毁表现、10 秒后复活 |
| `scripts/Sandbox/SandboxInput.cs` | 修改 | 1/2 换弹种，R 补给 |
| `scripts/Sandbox/RangeDemo.cs` | 新建 | `--range-demo` 的脚本化打靶 |
| `scripts/Sandbox/SandboxMain.cs` | 修改 | 接入以上全部；事件分发；散布圈像素；头顶血条和弹出字的数据 |
| `scripts/Bootstrap/LaunchOptions.cs` | 修改 | `--range-demo`、`--shot-after` |
| `scripts/Cameras/CameraPresets.cs` | 修改 | `target` 机位 |
| `scripts/Overlay/StatusPanel.cs` | 改写 | 三行：血条；速度和装填；两个弹药块 |
| `scripts/Overlay/Reticle.cs` | 修改 | 散布圈 |
| `scripts/Overlay/HudStyle.cs` | 修改 | 描边文字可整体淡出 |
| `scripts/Overlay/PopupCurves.cs` | 新建 | 弹出字的缩放、淡出、上飘曲线（不依赖 Godot，可测试） |
| `scripts/Overlay/CombatPopups.cs` | 新建 | 命中大字和伤害数字，对象池 |
| `scripts/Overlay/HealthBars.cs` | 新建 | 靶车头顶的胶囊血条 |

**测试**：`tests/Tank.Sim.Tests` 新增 `OrientedBoxTests`、`ShotRandomTests`、`CombatSpecTests`、`PenetrationTests`、`DispersionTests`、`AmmoAndAimTests`、`ArmorHitTests`、`Duel`（两车对射的测试夹具）、`TankHitTests`、`HighExplosiveTests`、`TargetSpotTests`，改 `TestWorld`、`SimulationTests`、`TankPoseTests`；`tests/Tank.Art.Tests` 新增 `TurretTossTests`、`PopupCurvesTests`，改 `PaletteTests` 和项目文件（链接两份新源码）。

---
## 任务一览与批次

| 批次 | 任务 | 做完后 Sim / Art 测试数 |
|---|---|---|
| A 仿真基础 | 1 三维有向盒与随机数；2 装甲、弹种与车型数据；3 击穿规则与事件字段；4 散布 | 160 / 178 → 167 → 179 → 185 |
| B 仿真战斗 | 5 换弹种、弹药与开火散布；6 装甲盒随车体和炮塔；7 炮弹命中坦克、溅射、击毁与复活；8 靶车位置 | 194 → 205 → 221 → 258 / 178 |
| C 客户端表现 | 9 靶车涂装、焦黑、炮塔飞起、黑烟；10 试驾场靶车、换弹种按键、曳光、打靶演示 | 258 / 183 |
| D 界面 | 11 面板、散布圈、命中大字、伤害数字、头顶血条 | 258 / 187 |
| E 验收 | 12 截图与帧率；13 用户试玩与收尾 | — |

每批做完先过规格审查，再过代码质量审查，然后在 `docs/PROGRESS.md` 记一行。

---

### Task 1：三维有向盒与每发炮弹的随机数

装甲盒是会随车体倾斜、随炮塔转动的三维盒子，命中检测要知道炮弹从哪个面进入；溅射要算点到盒子的距离。随机数不用有状态的生成器，而是由 `(tick, tankId, shotIndex)` 哈希出种子，每种用途加不同的盐，这样结果与调用顺序无关。

**Files:**
- Create: `src/Tank.Sim/Geometry/OrientedBox.cs`、`src/Tank.Sim/Combat/ShotRandom.cs`
- Test: `tests/Tank.Sim.Tests/OrientedBoxTests.cs`、`tests/Tank.Sim.Tests/ShotRandomTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/OrientedBoxTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Tests;

public sealed class OrientedBoxTests
{
    // 2 wide, 1 tall, 4 long, centred at (10, 5, 10).
    private static readonly OrientedBox Box = new(new Vector3(10, 5, 10), new Vector3(1, 0.5f, 2), Quaternion.Identity);

    [Theory]
    [InlineData(10, 5, 0, BoxFace.NegZ, 0.4f)]     // from −Z: the box face at z = 8 is 8 of 20 m in
    [InlineData(10, 5, 20, BoxFace.PosZ, 0.4f)]
    [InlineData(0, 5, 10, BoxFace.NegX, 0.45f)]
    [InlineData(20, 5, 10, BoxFace.PosX, 0.45f)]
    [InlineData(10, 15, 10, BoxFace.PosY, 0.475f)]
    [InlineData(10, -5, 10, BoxFace.NegY, 0.475f)]
    public void Raycast_ReportsTheEntryFace(float x, float y, float z, BoxFace expected, float expectedT)
    {
        Vector3 from = new(x, y, z);
        Vector3 to = Box.Center * 2 - from;   // straight through the centre and out the far side
        Assert.True(Box.Raycast(from, to, out float t, out BoxFace face));
        Assert.Equal(expected, face);
        Assert.Equal(expectedT, t, 1e-5f);
    }

    [Fact]
    public void Raycast_MissesAndStopsShort()
    {
        Assert.False(Box.Raycast(new Vector3(0, 5, 0), new Vector3(0, 5, 20), out _, out _));   // passes beside it
        Assert.False(Box.Raycast(new Vector3(10, 5, 0), new Vector3(10, 5, 7.9f), out _, out _)); // ends before the face
    }

    [Fact]
    public void Raycast_FromInside_ReportsTheFaceItWouldHaveComeThrough()
    {
        // Starting near the +X face but flying +X: it came in through the −X face, whose normal opposes its flight.
        Assert.True(Box.Raycast(new Vector3(10.8f, 5, 10), new Vector3(30, 5, 11), out float t, out BoxFace face));
        Assert.Equal(0f, t);
        Assert.Equal(BoxFace.NegX, face);
        Assert.True(Vector3.Dot(Box.Normal(face), new Vector3(19.2f, 0, 1)) < 0, "the face must oppose the flight");
    }

    [Fact]
    public void Raycast_ParallelToAFace_HitsOnlyWithinItsSlab()
    {
        Assert.True(Box.Raycast(new Vector3(0, 5.4f, 10), new Vector3(20, 5.4f, 10), out _, out BoxFace face));   // just under the top
        Assert.Equal(BoxFace.NegX, face);
        Assert.False(Box.Raycast(new Vector3(0, 5.6f, 10), new Vector3(20, 5.6f, 10), out _, out _));              // just over it
    }

    [Fact]
    public void Raycast_ThroughAnEdge_PicksTheLaterEntryPlane()
    {
        // Diagonal through the (−X, −Z) vertical edge region: it enters the X slab at t = 0.45 and the Z slab at 0.4,
        // so the entry is through the −X face at 0.45.
        Assert.True(Box.Raycast(new Vector3(0, 5, 0), new Vector3(20, 5, 20), out float t, out BoxFace face));
        Assert.Equal(BoxFace.NegX, face);
        Assert.Equal(0.45f, t, 1e-5f);
    }

    [Fact]
    public void Raycast_UsesTheBoxRotation()
    {
        // Turned 90° about Y: the box's −Z (front) face now looks along world −X.
        var turned = Box with { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2) };
        Assert.True(turned.Raycast(new Vector3(0, 5, 10), new Vector3(20, 5, 10), out float t, out BoxFace face));
        Assert.Equal(BoxFace.NegZ, face);
        Assert.Equal(0.4f, t, 1e-5f);
        Vector3 normal = turned.Normal(face);
        Assert.Equal(-1f, normal.X, 1e-5f);
    }

    [Fact]
    public void DistanceTo_IsZeroInside_AndMeasuresToTheNearestPoint()
    {
        Assert.Equal(0f, Box.DistanceTo(new Vector3(10.5f, 5, 11)));
        Assert.Equal(3f, Box.DistanceTo(new Vector3(14, 5, 10)), 1e-5f);
        Assert.Equal(5f, Box.DistanceTo(new Vector3(14, 9.5f, 10)), 1e-5f);   // 3 along X, 4 along Y
    }
}
```

新建 `tests/Tank.Sim.Tests/ShotRandomTests.cs`：

```csharp
using Tank.Sim.Combat;

namespace Tank.Sim.Tests;

public sealed class ShotRandomTests
{
    [Fact]
    public void SameShot_SameNumbers()
    {
        uint a = ShotRandom.Seed(120, 3, 7), b = ShotRandom.Seed(120, 3, 7);
        Assert.Equal(a, b);
        Assert.Equal(ShotRandom.Unit(a, ShotRandom.DamageRoll), ShotRandom.Unit(b, ShotRandom.DamageRoll));
    }

    [Fact]
    public void Tick_Tank_AndShotIndex_AllChangeTheSeed()
    {
        uint seed = ShotRandom.Seed(120, 3, 7);
        Assert.NotEqual(seed, ShotRandom.Seed(121, 3, 7));
        Assert.NotEqual(seed, ShotRandom.Seed(120, 4, 7));
        Assert.NotEqual(seed, ShotRandom.Seed(120, 3, 8));
    }

    [Fact]
    public void Range_StaysInBounds_AndCoversThem()
    {
        float low = 1, high = 0;
        for (int shot = 0; shot < 2000; shot++)
        {
            float x = ShotRandom.Range(ShotRandom.Seed(1, 1, shot), ShotRandom.PenetrationRoll, 0.75f, 1.25f);
            Assert.InRange(x, 0.75f, 1.25f);
            low = MathF.Min(low, x);
            high = MathF.Max(high, x);
        }
        Assert.True(low < 0.76f && high > 1.24f, $"rolls only spanned {low}..{high}");
    }

    [Fact]
    public void Salts_AreIndependent()
    {
        uint seed = ShotRandom.Seed(5, 1, 0);
        Assert.NotEqual(ShotRandom.Unit(seed, ShotRandom.PenetrationRoll), ShotRandom.Unit(seed, ShotRandom.DamageRoll));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`OrientedBox`、`BoxFace`、`ShotRandom` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Combat/ShotRandom.cs`：

```csharp
namespace Tank.Sim.Combat;

/// <summary>
/// Stateless random numbers for one shot. The seed comes from (tick, tank id, shot index) and each use has its own salt,
/// so the same shot gives the same dispersion and rolls on every machine, in any order.
/// </summary>
public static class ShotRandom
{
    public const uint DispersionAngle = 1;
    public const uint DispersionRadius = 2;
    public const uint PenetrationRoll = 3;
    public const uint DamageRoll = 4;

    public static uint Seed(int tick, int tankId, int shotIndex) => Mix(Mix(Mix((uint)tick) + (uint)tankId) + (uint)shotIndex);

    /// <summary>Uniform in [0, 1).</summary>
    public static float Unit(uint seed, uint salt) => (Mix(seed ^ (salt * 0x9E3779B9u)) >> 8) / 16777216f;

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public static float Range(uint seed, uint salt, float min, float max) => min + (max - min) * Unit(seed, salt);

    /// <summary>An integer hash with good avalanche (Chris Wellons' lowbias32).</summary>
    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;
        return x;
    }
}
```

新建 `src/Tank.Sim/Geometry/OrientedBox.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Geometry;

/// <summary>A face of an <see cref="OrientedBox"/>, named by its outward normal in the box's own axes.</summary>
public enum BoxFace : byte { NegX, PosX, NegY, PosY, NegZ, PosZ }

/// <summary>
/// A box in 3D space: centre, half sizes along its own X, Y and Z axes, and the rotation from those axes to world space.
/// </summary>
public readonly record struct OrientedBox(Vector3 Center, Vector3 HalfExtents, Quaternion Rotation)
{
    /// <summary>Radius of the smallest sphere around the centre that holds the whole box.</summary>
    public float BoundingRadius => HalfExtents.Length();

    public static Vector3 LocalNormal(BoxFace face) => face switch
    {
        BoxFace.NegX => -Vector3.UnitX,
        BoxFace.PosX => Vector3.UnitX,
        BoxFace.NegY => -Vector3.UnitY,
        BoxFace.PosY => Vector3.UnitY,
        BoxFace.NegZ => -Vector3.UnitZ,
        _ => Vector3.UnitZ,
    };

    /// <summary>Outward normal of <paramref name="face"/> in world space.</summary>
    public Vector3 Normal(BoxFace face) => Vector3.Transform(LocalNormal(face), Rotation);

    /// <summary>A world point in the box's own axes, relative to its centre.</summary>
    public Vector3 ToLocal(Vector3 world) => Vector3.Transform(world - Center, Quaternion.Conjugate(Rotation));

    /// <summary>Distance from <paramref name="point"/> to the box (0 inside).</summary>
    public float DistanceTo(Vector3 point)
    {
        Vector3 local = Vector3.Abs(ToLocal(point));
        return Vector3.Max(local - HalfExtents, Vector3.Zero).Length();
    }

    /// <summary>
    /// Where segment <paramref name="from"/>→<paramref name="to"/> first touches the box: <paramref name="t"/> in [0, 1]
    /// and the face it enters through. A segment that starts inside reports t = 0 and the face it would have come in
    /// through: the one facing most squarely against its direction.
    /// </summary>
    public bool Raycast(Vector3 from, Vector3 to, out float t, out BoxFace face)
    {
        t = 0;
        face = BoxFace.NegZ;
        Quaternion inverse = Quaternion.Conjugate(Rotation);
        Vector3 start = Vector3.Transform(from - Center, inverse);
        Vector3 delta = Vector3.Transform(to - from, inverse);
        float tMin = 0f, tMax = 1f;
        int entryAxis = -1;
        bool entryPositive = false;
        for (int axis = 0; axis < 3; axis++)
        {
            float s = start[axis], d = delta[axis], h = HalfExtents[axis];
            if (MathF.Abs(d) < 1e-8f)
            {
                if (MathF.Abs(s) > h) return false;
                continue;
            }
            // Moving towards +axis enters through the −axis face, and the other way round.
            float near = ((d > 0 ? -h : h) - s) / d, far = ((d > 0 ? h : -h) - s) / d;
            if (near > tMin)
            {
                tMin = near;
                entryAxis = axis;
                entryPositive = d < 0;
            }
            tMax = MathF.Min(tMax, far);
            if (tMin > tMax) return false;
        }
        t = tMin;
        face = entryAxis < 0 ? FaceAgainst(delta) : FaceOf(entryAxis, entryPositive);
        return true;
    }

    /// <summary>The face whose outward normal points most directly against <paramref name="localDirection"/>.</summary>
    private static BoxFace FaceAgainst(Vector3 localDirection)
    {
        int best = 0;
        for (int axis = 1; axis < 3; axis++)
            if (MathF.Abs(localDirection[axis]) > MathF.Abs(localDirection[best])) best = axis;
        return FaceOf(best, localDirection[best] < 0);
    }

    private static BoxFace FaceOf(int axis, bool positive) => (BoxFace)(axis * 2 + (positive ? 1 : 0));
}
```


要点：`Raycast` 用逐轴“平板”求交，记下最后一个抬高 `tMin` 的轴，它就是进入面；起点已在盒内时（没有轴抬高过 `tMin`），取“法线最迎着飞行方向”的面，这样炮口顶进别的坦克时按正对的那块装甲结算，而不是算成擦边跳弹。

- [ ] **Step 4：运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"`
Expected: `通过: 160`，失败 0。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): oriented 3D boxes and per-shot random numbers"
```

---

### Task 2：装甲、弹种与车型数据

把设计 §3.1、§3.2 的数据加进 `TankSpec` 和 `TankState`：血量、口径、车体盒和炮塔盒（各五个面的装甲）、穿甲弹和高爆弹（穿深、伤害、初速、溅射半径、载弹量）、百米散布、缩圈时间、四项散布扩大系数。`ShellSpeed` 由两种炮弹各自的初速取代，`Ballistics.Aim` 改用当前弹种的初速。`AddTank` 让新坦克满血、满弹、装着穿甲弹。炮弹 `Shell` 预先加上本阶段要用的字段（弹种、弹种数据、口径、种子），Task 5 才开始填。

**Files:**
- Create: `src/Tank.Sim/Combat/AmmoType.cs`、`ShellSpec.cs`、`Armor.cs`、`BloomFactors.cs`
- Modify: `src/Tank.Sim/Vehicles/TankSpec.cs`、`TankCatalog.cs`、`TankState.cs`、`src/Tank.Sim/Combat/Shell.cs`、`Ballistics.cs`、`src/Tank.Sim/Simulation.cs`
- Test: 新建 `tests/Tank.Sim.Tests/CombatSpecTests.cs`；改 `SimulationTests.cs`、`TankPoseTests.cs`（不再引用 `ShellSpeed`）

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/CombatSpecTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class CombatSpecTests
{
    public static TheoryData<string> SpecIds()
    {
        var ids = new TheoryData<string>();
        foreach (TankSpec spec in TankCatalog.All) ids.Add(spec.Id);
        return ids;
    }

    private static TankSpec Get(string id) => TankCatalog.All.Single(s => s.Id == id);

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void CombatNumbers_ArePositive(string id)
    {
        TankSpec s = Get(id);
        Assert.True(s.MaxHealth > 0);
        Assert.All(new[] { s.Caliber, s.Dispersion, s.AimTime, s.Bloom.Moving, s.Bloom.HullTurn, s.Bloom.TurretTurn, s.Bloom.Fire },
            v => Assert.True(v > 0, $"{id} has a non-positive combat value"));
        foreach (ShellSpec round in new[] { s.Ap, s.He })
        {
            Assert.True(round.Penetration > 0 && round.Damage > 0 && round.Speed > 0 && round.Capacity > 0, $"{id}: {round}");
            Assert.True(round.SplashRadius >= 0, $"{id}: {round}");
        }
        Assert.True(s.He.SplashRadius > 0, $"{id}: HE must splash");
        foreach (ArmorBox box in new[] { s.HullArmor, s.TurretArmor })
        {
            Armor a = box.Armor;
            Assert.All(new[] { box.Width, box.Length, box.Top - box.Bottom, a.Front, a.Side, a.Rear, a.Top, a.Bottom },
                v => Assert.True(v > 0, $"{id}: {box}"));
        }
    }

    [Theory]
    [MemberData(nameof(SpecIds))]
    public void ArmorBoxes_SitInsideTheFootprint_TurretOnTop(string id)
    {
        TankSpec s = Get(id);
        Assert.True(s.HullArmor.Width <= s.HullWidth && s.HullArmor.Length <= s.HullLength, $"{id}: hull box sticks out");
        Assert.True(s.TurretArmor.Width <= s.HullWidth, $"{id}: turret box wider than the hull");
        Assert.True(s.TurretOffset + s.TurretArmor.Length / 2 <= s.HullLength / 2, $"{id}: turret box past the nose");
        Assert.True(s.HullArmor.Bottom >= 0, $"{id}: hull box under the ground");
        Assert.True(s.TurretArmor.Bottom > s.HullArmor.Bottom && s.TurretArmor.Top > s.HullArmor.Top, $"{id}: turret box not above the hull box");
        Assert.True(s.TrunnionHeight > s.TurretArmor.Bottom && s.TrunnionHeight < s.TurretArmor.Top, $"{id}: gun pivot outside the turret box");
    }

    [Fact]
    public void Medium_MatchesTheDesignTable()
    {
        TankSpec m = TankCatalog.Medium;
        Assert.Equal(900, m.MaxHealth);
        Assert.Equal(85f, m.Caliber);
        Assert.Equal(new Armor(90, 60, 40, 20, 20), m.HullArmor.Armor);
        Assert.Equal(new Armor(120, 80, 50, 25, 25), m.TurretArmor.Armor);
        Assert.Equal(new ShellSpec(175, 220, 780, 0, 40), m.Ap);
        Assert.Equal(new ShellSpec(45, 280, 624, 3, 15), m.He);
        Assert.Equal(0.36f, m.Dispersion);
        Assert.Equal(2.1f, m.AimTime);
        Assert.Equal(new BloomFactors(2.0f, 1.5f, 1.0f, 3.0f), m.Bloom);
    }

    [Fact]
    public void Armor_LooksUpFaces_AndTheThinnest()
    {
        var armor = new Armor(Front: 90, Side: 60, Rear: 40, Top: 20, Bottom: 25);
        Assert.Equal(60f, armor[ArmorFace.Side]);
        Assert.Equal(25f, armor[ArmorFace.Bottom]);
        Assert.Equal(20f, armor.Thinnest);
        Assert.Equal(ArmorFace.Front, Armor.FaceOf(Geometry.BoxFace.NegZ));
        Assert.Equal(ArmorFace.Side, Armor.FaceOf(Geometry.BoxFace.PosX));
        Assert.Equal(ArmorFace.Top, Armor.FaceOf(Geometry.BoxFace.PosY));
    }

    [Fact]
    public void ArmorBox_CentreAndHalfSizes()
    {
        var box = new ArmorBox(Width: 3.2f, Length: 6.2f, Bottom: 0.3f, Top: 1.74f, default);
        Assert.Equal(new Vector3(1.6f, 0.72f, 3.1f), box.HalfExtents);
        Assert.Equal(new Vector3(0, 1.02f, 0), box.LocalCenter);
    }

    [Fact]
    public void NewTank_HasFullHealthAndAmmo_WithApLoaded()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0);
        TankState t = sim.Tanks[0];
        Assert.Equal(900, t.Health);
        Assert.False(t.IsDestroyed);
        Assert.Equal(AmmoType.AP, t.Ammo);
        Assert.Equal(40, t.ApLeft);
        Assert.Equal(15, t.HeLeft);
        Assert.Equal(0f, t.Bloom);
    }

    [Fact]
    public void Launch_UsesTheLoadedRoundsSpeed()
    {
        TankState tank = TestWorld.Tank(100, 100) with { Ammo = AmmoType.HE };
        var launch = Ballistics.Aim(tank, TestWorld.FlatField());
        Assert.Equal(624f, launch.Velocity.Length(), 0.01f);
    }
}
```

修改 `tests/Tank.Sim.Tests/SimulationTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/SimulationTests.cs b/tests/Tank.Sim.Tests/SimulationTests.cs
index 24a9597..33044d8 100644
--- a/tests/Tank.Sim.Tests/SimulationTests.cs
+++ b/tests/Tank.Sim.Tests/SimulationTests.cs
@@ -60,7 +60,7 @@ public sealed class SimulationTests
         Assert.Equal(tankId, fired.TankId);
         var expectedMuzzle = new Vector3(100, Spec.TrunnionHeight, 100 - Spec.TurretOffset - Spec.GunLength);
         Assert.True(Vector3.Distance(expectedMuzzle, fired.Position) < 1e-3f, $"muzzle at {fired.Position}");
-        Assert.Equal(Spec.ShellSpeed, sim.Shells[0].Velocity.Length(), 0.5f);
+        Assert.Equal(Spec.Ap.Speed, sim.Shells[0].Velocity.Length(), 0.5f);
         Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);
     }
 
PATCH
```

修改 `tests/Tank.Sim.Tests/TankPoseTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TankPoseTests.cs b/tests/Tank.Sim.Tests/TankPoseTests.cs
index bee56ad..6cfe8db 100644
--- a/tests/Tank.Sim.Tests/TankPoseTests.cs
+++ b/tests/Tank.Sim.Tests/TankPoseTests.cs
@@ -20,7 +20,7 @@ public sealed class TankPoseTests
             {
                 s.HullLength, s.HullWidth, s.MaxForwardSpeed, s.MaxReverseSpeed, s.Acceleration, s.Braking, s.HullTraverse,
                 s.TurretTraverse, s.GunPitchSpeed, s.GunDepression, s.GunElevation, s.MaxClimb, s.TrunnionHeight, s.GunLength,
-                s.ShellSpeed, s.ReloadTime,
+                s.ReloadTime, s.Caliber, s.Dispersion, s.AimTime,
             }, v => Assert.True(v > 0, $"{s.Id} has a non-positive parameter"));
         }
     }
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`ArmorBox`、`ShellSpec`、`TankSpec.Ap` 等找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Combat/AmmoType.cs`：

```csharp
namespace Tank.Sim.Combat;

/// <summary>Round types. AP pierces armour; HE has little penetration but bursts with splash.</summary>
public enum AmmoType : byte
{
    AP = 0,
    HE = 1,
}
```

新建 `src/Tank.Sim/Combat/Armor.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Combat;

public enum ArmorFace : byte { Front, Side, Rear, Top, Bottom }

public enum HitPart : byte { Hull, Turret }

/// <summary>Plate thickness of each face of an armour box, mm.</summary>
public readonly record struct Armor(float Front, float Side, float Rear, float Top, float Bottom)
{
    public float this[ArmorFace face] => face switch
    {
        ArmorFace.Front => Front,
        ArmorFace.Side => Side,
        ArmorFace.Rear => Rear,
        ArmorFace.Top => Top,
        _ => Bottom,
    };

    public float Thinnest => MathF.Min(MathF.Min(MathF.Min(Front, Side), MathF.Min(Rear, Top)), Bottom);

    /// <summary>Armour face for a box face in hull or turret axes (−Z is forward).</summary>
    public static ArmorFace FaceOf(BoxFace face) => face switch
    {
        BoxFace.NegZ => ArmorFace.Front,
        BoxFace.PosZ => ArmorFace.Rear,
        BoxFace.PosY => ArmorFace.Top,
        BoxFace.NegY => ArmorFace.Bottom,
        _ => ArmorFace.Side,
    };
}

/// <summary>
/// A hit box: <see cref="Width"/> × <see cref="Length"/> centred on its anchor (the hull origin, or the turret ring for the
/// turret), reaching from <see cref="Bottom"/> to <see cref="Top"/> above it. Defined separately from the toy model.
/// </summary>
public readonly record struct ArmorBox(float Width, float Length, float Bottom, float Top, Armor Armor)
{
    public Vector3 HalfExtents => new(Width / 2, (Top - Bottom) / 2, Length / 2);

    /// <summary>Centre relative to the anchor.</summary>
    public Vector3 LocalCenter => new(0, (Bottom + Top) / 2, 0);
}
```

修改 `src/Tank.Sim/Combat/Ballistics.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Combat/Ballistics.cs b/src/Tank.Sim/Combat/Ballistics.cs
index ec32a76..7a58d85 100644
--- a/src/Tank.Sim/Combat/Ballistics.cs
+++ b/src/Tank.Sim/Combat/Ballistics.cs
@@ -18,7 +18,10 @@ public static class Ballistics
     private static readonly Vector3 GravityStep = new(0, -SimConstants.Gravity * SimConstants.Dt, 0);
     private static readonly Vector3 GravityDrop = new(0, -0.5f * SimConstants.Gravity * SimConstants.Dt * SimConstants.Dt, 0);
 
-    /// <summary>Where and how a shot fired now would leave the barrel. <see cref="Blocked"/> when the barrel is inside a prop or the ground.</summary>
+    /// <summary>
+    /// Where and how a shot of the loaded round fired now would leave the barrel, before dispersion. <see cref="Blocked"/>
+    /// when the barrel is inside a prop or the ground.
+    /// </summary>
     public readonly record struct Launch(Vector3 Origin, Vector3 Velocity, bool Blocked, Vector3 BlockPoint);
 
     public static Launch Aim(in TankState tank, Battlefield field)
@@ -26,7 +29,7 @@ public static class Ballistics
         TankPose pose = TankPose.Compute(tank, field.Terrain);
         Vector3 trunnion = pose.Trunnion(tank.Spec);
         Vector3 muzzle = pose.Muzzle(tank.Spec, tank.TurretYaw, tank.GunPitch);
-        Vector3 velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.ShellSpeed;
+        Vector3 velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.Shell(tank.Ammo).Speed;
         bool blocked = field.Raycast(trunnion, muzzle, out RayHit hit);
         return new Launch(muzzle, velocity, blocked, blocked ? hit.Point : Vector3.Zero);
     }
PATCH
```

新建 `src/Tank.Sim/Combat/BloomFactors.cs`：

```csharp
namespace Tank.Sim.Combat;

/// <summary>
/// How much each action widens the aim circle. The circle's radius is the base dispersion × (1 + Bloom); driving, hull
/// turning and turret turning add their factor at full rate (proportionally below it), and each shot adds <see cref="Fire"/>.
/// </summary>
public readonly record struct BloomFactors(float Moving, float HullTurn, float TurretTurn, float Fire);
```

修改 `src/Tank.Sim/Combat/Shell.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Combat/Shell.cs b/src/Tank.Sim/Combat/Shell.cs
index 815f5af..a66bc22 100644
--- a/src/Tank.Sim/Combat/Shell.cs
+++ b/src/Tank.Sim/Combat/Shell.cs
@@ -11,4 +11,15 @@ public struct Shell
 
     /// <summary>Seconds since it left the muzzle.</summary>
     public float Age;
+
+    public AmmoType Ammo;
+
+    /// <summary>The round's stats, copied from the firing tank so the shell resolves its hits on its own.</summary>
+    public ShellSpec Round;
+
+    /// <summary>Calibre of the gun that fired it, mm.</summary>
+    public float Caliber;
+
+    /// <summary><see cref="ShotRandom.Seed"/> of the shot; its hit rolls use it too.</summary>
+    public uint Seed;
 }
PATCH
```

新建 `src/Tank.Sim/Combat/ShellSpec.cs`：

```csharp
namespace Tank.Sim.Combat;

/// <summary>
/// One kind of round for a gun: penetration (mm), damage (hit points), muzzle speed (m/s), splash radius (m, 0 = none)
/// and how many the tank carries.
/// </summary>
public readonly record struct ShellSpec(float Penetration, int Damage, float Speed, float SplashRadius, int Capacity);
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index 07c3630..f42b475 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -37,12 +37,22 @@ public sealed class Simulation
     /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
     public ReadOnlySpan<GameEvent> Events => CollectionsMarshal.AsSpan(_events);
 
+    /// <summary>Adds a tank with full health and ammunition, AP loaded.</summary>
     /// <returns>The new tank's id. It occupies the next slot in <see cref="Tanks"/>.</returns>
     public int AddTank(TankSpec spec, Vector2 position, float yaw)
     {
         if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
         int id = _nextId++;
-        _tanks[TankCount++] = new TankState { Id = id, Spec = spec, Position = Field.ClampToPlayable(position), Yaw = Angles.Wrap(yaw) };
+        _tanks[TankCount++] = new TankState
+        {
+            Id = id,
+            Spec = spec,
+            Position = Field.ClampToPlayable(position),
+            Yaw = Angles.Wrap(yaw),
+            Health = spec.MaxHealth,
+            ApLeft = spec.Ap.Capacity,
+            HeLeft = spec.He.Capacity,
+        };
         return id;
     }
 
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index 2e50984..9447656 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -1,8 +1,9 @@
+using Tank.Sim.Combat;
 using Tank.Sim.Geometry;
 
 namespace Tank.Sim.Vehicles;
 
-/// <summary>Vehicle table (design §4.4). Stage 1 ships the medium tank only.</summary>
+/// <summary>Vehicle table (design §4.4). Only the medium tank so far.</summary>
 public static class TankCatalog
 {
     private const float Kmh = 1f / 3.6f;
@@ -26,8 +27,16 @@ public static class TankCatalog
         TrunnionHeight = 2.1f,
         TurretOffset = 0.3f,
         GunLength = 3.2f,
-        ShellSpeed = 780f,
         ReloadTime = 6.5f,
+        MaxHealth = 900,
+        Caliber = 85,
+        HullArmor = new ArmorBox(Width: 3.2f, Length: 6.2f, Bottom: 0.3f, Top: 1.74f, new Armor(Front: 90, Side: 60, Rear: 40, Top: 20, Bottom: 20)),
+        TurretArmor = new ArmorBox(Width: 2.9f, Length: 2.9f, Bottom: 1.68f, Top: 2.8f, new Armor(Front: 120, Side: 80, Rear: 50, Top: 25, Bottom: 25)),
+        Ap = new ShellSpec(Penetration: 175, Damage: 220, Speed: 780, SplashRadius: 0, Capacity: 40),
+        He = new ShellSpec(Penetration: 45, Damage: 280, Speed: 780 * 0.8f, SplashRadius: 3, Capacity: 15),
+        Dispersion = 0.36f,
+        AimTime = 2.1f,
+        Bloom = new BloomFactors(Moving: 2.0f, HullTurn: 1.5f, TurretTurn: 1.0f, Fire: 3.0f),
     };
 
     public static IReadOnlyList<TankSpec> All { get; } = new[] { Medium };
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankSpec.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankSpec.cs b/src/Tank.Sim/Vehicles/TankSpec.cs
index 6ca0cc7..2cee88c 100644
--- a/src/Tank.Sim/Vehicles/TankSpec.cs
+++ b/src/Tank.Sim/Vehicles/TankSpec.cs
@@ -1,3 +1,5 @@
+using Tank.Sim.Combat;
+
 namespace Tank.Sim.Vehicles;
 
 /// <summary>Per-vehicle constants. Metres, seconds, radians, m/s. Hull origin is the ground contact centre.</summary>
@@ -28,6 +30,29 @@ public sealed record TankSpec
     public required float TurretOffset { get; init; }
 
     public required float GunLength { get; init; }
-    public required float ShellSpeed { get; init; }
     public required float ReloadTime { get; init; }
+
+    public required int MaxHealth { get; init; }
+
+    /// <summary>Gun calibre, mm (for the three-calibre overmatch rule).</summary>
+    public required float Caliber { get; init; }
+
+    /// <summary>Hull hit box, anchored at the hull origin.</summary>
+    public required ArmorBox HullArmor { get; init; }
+
+    /// <summary>Turret hit box, anchored at the turret ring (<see cref="TurretOffset"/> ahead of the hull origin); turns with the turret.</summary>
+    public required ArmorBox TurretArmor { get; init; }
+
+    public required ShellSpec Ap { get; init; }
+    public required ShellSpec He { get; init; }
+
+    /// <summary>Aim-circle radius at 100 m when fully aimed, m.</summary>
+    public required float Dispersion { get; init; }
+
+    /// <summary>Seconds for the aim circle to close after moving (three time constants).</summary>
+    public required float AimTime { get; init; }
+
+    public required BloomFactors Bloom { get; init; }
+
+    public ShellSpec Shell(AmmoType ammo) => ammo == AmmoType.HE ? He : Ap;
 }
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankState.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankState.cs b/src/Tank.Sim/Vehicles/TankState.cs
index ee3f9b8..08accee 100644
--- a/src/Tank.Sim/Vehicles/TankState.cs
+++ b/src/Tank.Sim/Vehicles/TankState.cs
@@ -1,4 +1,5 @@
 using System.Numerics;
+using Tank.Sim.Combat;
 
 namespace Tank.Sim.Vehicles;
 
@@ -26,4 +27,20 @@ public struct TankState
     public float Reload;
 
     public int ShotCount;
+
+    public int Health;
+
+    /// <summary>A wreck ignores input and takes no more damage, but still blocks tanks and shells.</summary>
+    public bool IsDestroyed;
+
+    /// <summary>Round in the breech; firing uses this type.</summary>
+    public AmmoType Ammo;
+
+    public int ApLeft;
+    public int HeLeft;
+
+    /// <summary>How far the aim circle is opened up; 0 = fully aimed (see <see cref="Combat.Dispersion"/>).</summary>
+    public float Bloom;
+
+    public readonly int AmmoLeft(AmmoType ammo) => ammo == AmmoType.HE ? HeLeft : ApLeft;
 }
PATCH
```


- [ ] **Step 4：运行仿真测试和客户端编译**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"; dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Error\(s\)"; dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "通过|失败"`
Expected: Sim `通过: 167`；客户端 `0 Error(s)`；Art `通过: 178`。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): armour boxes, health, two ammo types and dispersion stats for the medium tank"
```

---

### Task 3：击穿规则与战斗事件字段

设计 §3.6、§3.7 的规则写成纯函数：入射角、跳弹（> 70° 且口径 ≤ 3 倍装甲）、等效装甲、穿甲弹命中、高爆弹直接命中、溅射。四舍五入用“远离零”（`MathF.Round` 默认是银行家舍入）。同时给 `GameEvent` 加上四种新事件、`HitResult` 和四个新字段（都有默认值，旧的调用不用改）。

**Files:**
- Create: `src/Tank.Sim/Combat/Penetration.cs`
- Modify: `src/Tank.Sim/GameEvent.cs`
- Test: `tests/Tank.Sim.Tests/PenetrationTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/PenetrationTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class PenetrationTests
{
    private static readonly ShellSpec Ap = TankCatalog.Medium.Ap;   // 175 mm, 220 damage
    private static readonly ShellSpec He = TankCatalog.Medium.He;   // 45 mm, 280 damage, 3 m
    private const float Caliber = 85f;

    private static IEnumerable<uint> Seeds(int count) => Enumerable.Range(0, count).Select(i => ShotRandom.Seed(i, 1, i));

    [Fact]
    public void ImpactAngle_IsZeroHeadOn_AndGrowsWithObliquity()
    {
        Vector3 normal = Vector3.UnitZ;   // plate facing +Z, shell flying −Z into it
        Assert.Equal(0f, Penetration.ImpactAngle(new Vector3(0, 0, -780), normal), 1e-5f);
        Vector3 oblique = new(MathF.Sin(Angles.Deg(60)), 0, -MathF.Cos(Angles.Deg(60)));
        Assert.Equal(Angles.Deg(60), Penetration.ImpactAngle(oblique * 500, normal), 1e-4f);
    }

    [Theory]
    [InlineData(69f, false)]
    [InlineData(71f, true)]
    public void Ricochet_StartsPast70Degrees(float degrees, bool bounces) =>
        Assert.Equal(bounces, Penetration.Ricochets(Angles.Deg(degrees), Caliber, armor: 90));

    [Fact]
    public void Ricochet_NeverHappensWhenTheCalibreOvermatchesThePlate()
    {
        Assert.False(Penetration.Ricochets(Angles.Deg(85), Caliber, armor: 25));   // 85 > 3 × 25
        Assert.True(Penetration.Ricochets(Angles.Deg(85), Caliber, armor: 30));    // 85 ≤ 3 × 30
    }

    [Fact]
    public void EffectiveArmor_IsThicknessOverCosine()
    {
        Assert.Equal(90f, Penetration.EffectiveArmor(90, 0), 1e-4f);
        Assert.Equal(180f, Penetration.EffectiveArmor(90, Angles.Deg(60)), 1e-3f);
        Assert.True(float.IsFinite(Penetration.EffectiveArmor(90, Angles.Deg(90))));
    }

    [Fact]
    public void Ap_RolledDamage_StaysWithin25Percent_AndRepeatsForTheSameSeed()
    {
        foreach (uint seed in Seeds(500))
        {
            Impact hit = Penetration.Ap(Ap, Caliber, armor: 40, angle: 0, seed);
            Assert.Equal(HitResult.Penetrated, hit.Result);   // 175 × 0.75 > 40
            Assert.InRange(hit.Damage, 165, 275);             // 220 × [0.75, 1.25]
            Assert.Equal(hit, Penetration.Ap(Ap, Caliber, 40, 0, seed));
        }
    }

    [Fact]
    public void Ap_PenetrationRoll_DecidesBorderlineShots()
    {
        // 175 × [0.75, 1.25] = 131…219 against 175 mm: about half go through.
        var results = Seeds(400).Select(s => Penetration.Ap(Ap, Caliber, armor: 175, angle: 0, s)).ToList();
        int pierced = results.Count(r => r.Result == HitResult.Penetrated);
        Assert.InRange(pierced, 120, 280);
        Assert.All(results.Where(r => r.Result == HitResult.NotPenetrated), r => Assert.Equal(0, r.Damage));
    }

    [Fact]
    public void Ap_NeverPiercesBeyondItsBestRoll()
    {
        // 120 mm at 60° is 240 mm effective, more than 175 × 1.25.
        Assert.All(Seeds(200), s => Assert.Equal(HitResult.NotPenetrated, Penetration.Ap(Ap, Caliber, 120, Angles.Deg(60), s).Result));
    }

    [Fact]
    public void Ap_Ricochet_DealsNoDamage()
    {
        Impact hit = Penetration.Ap(Ap, Caliber, armor: 60, angle: Angles.Deg(80), seed: 1);
        Assert.Equal(new Impact(HitResult.Ricochet, 0), hit);
    }

    [Fact]
    public void HeDirect_Pierced_DealsRolledFullDamage()
    {
        Impact hit = Penetration.HeDirect(He, armor: 20, angle: 0, seed: 7);   // 45 × 0.75 > 20
        Assert.Equal(HitResult.Penetrated, hit.Result);
        Assert.InRange(hit.Damage, 210, 350);
    }

    [Fact]
    public void HeDirect_NotPierced_DealsHalfDamageLessArmour()
    {
        // 90 mm head-on: 280 × 0.5 − 90 × 1.1 = 41.
        Assert.Equal(new Impact(HitResult.NotPenetrated, 41), Penetration.HeDirect(He, armor: 90, angle: 0, seed: 3));
        // 120 mm at 30°: 138.6 effective → 140 − 152.4 < 0 → 0. HE never ricochets, even at 80°.
        Assert.Equal(new Impact(HitResult.NotPenetrated, 0), Penetration.HeDirect(He, 120, Angles.Deg(30), 3));
        Assert.Equal(HitResult.NotPenetrated, Penetration.HeDirect(He, 90, Angles.Deg(80), 3).Result);
    }

    [Fact]
    public void Splash_FadesWithDistance_LessTheThinnestArmour()
    {
        Assert.Equal(118, Penetration.Splash(He, distance: 0, thinnestArmor: 20));     // 140 − 22
        Assert.Equal(48, Penetration.Splash(He, distance: 1.5f, thinnestArmor: 20));   // 70 − 22
        Assert.Equal(1, Penetration.Splash(He, distance: 2.5f, thinnestArmor: 20));    // 23.3 − 22
        Assert.Equal(0, Penetration.Splash(He, distance: 2.8f, thinnestArmor: 20));    // armour soaks it all
        Assert.Equal(0, Penetration.Splash(He, distance: 3f, thinnestArmor: 0));       // edge of the radius
        Assert.Equal(0, Penetration.Splash(Ap, distance: 0, thinnestArmor: 0));        // AP has no splash
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`Penetration`、`Impact`、`HitResult` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Combat/Penetration.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;

namespace Tank.Sim.Combat;

/// <summary>What a shell did to the plate it struck, and the damage it dealt.</summary>
public readonly record struct Impact(HitResult Result, int Damage);

/// <summary>
/// Armour rules (design §3.6–3.7). Impact angles are radians between the reversed flight direction and the struck
/// face's outward normal: 0 is head-on. Rolls are ±25%, drawn from the shot's seed.
/// </summary>
public static class Penetration
{
    public static readonly float RicochetAngle = Angles.Deg(70);
    public const float OvermatchRatio = 3f;
    public const float MinRoll = 0.75f;
    public const float MaxRoll = 1.25f;
    private const float HeShare = 0.5f;          // an HE round that doesn't pierce, or a near miss, does up to half its damage
    private const float HeArmorFactor = 1.1f;     // … less this much per mm of armour
    private const float MinCos = 1e-3f;           // keeps effective armour finite at a grazing 90°

    public static float ImpactAngle(Vector3 velocity, Vector3 normal) =>
        MathF.Acos(System.Math.Clamp(Vector3.Dot(-Vector3.Normalize(velocity), normal), -1f, 1f));

    /// <summary>Steeper than 70° bounces off, unless the calibre is more than three times the plate (overmatch).</summary>
    public static bool Ricochets(float angle, float caliber, float armor) => angle > RicochetAngle && caliber <= OvermatchRatio * armor;

    public static float EffectiveArmor(float armor, float angle) => armor / MathF.Max(MathF.Cos(angle), MinCos);

    public static Impact Ap(ShellSpec round, float caliber, float armor, float angle, uint seed)
    {
        if (Ricochets(angle, caliber, armor)) return new Impact(HitResult.Ricochet, 0);
        return Pierces(round, armor, angle, seed)
            ? new Impact(HitResult.Penetrated, RolledDamage(round, seed))
            : new Impact(HitResult.NotPenetrated, 0);
    }

    /// <summary>HE never ricochets. If it doesn't pierce it still hurts: half damage less 1.1 × the effective armour.</summary>
    public static Impact HeDirect(ShellSpec round, float armor, float angle, uint seed)
    {
        if (Pierces(round, armor, angle, seed)) return new Impact(HitResult.Penetrated, RolledDamage(round, seed));
        float damage = round.Damage * HeShare - EffectiveArmor(armor, angle) * HeArmorFactor;
        return new Impact(HitResult.NotPenetrated, RoundHalfUp(MathF.Max(0f, damage)));
    }

    /// <summary>
    /// HE splash on a tank <paramref name="distance"/> m from the burst (to its nearest point): half damage fading to
    /// nothing at the splash radius, less 1.1 × its thinnest armour; never negative.
    /// </summary>
    public static int Splash(ShellSpec round, float distance, float thinnestArmor)
    {
        if (round.SplashRadius <= 0 || distance >= round.SplashRadius) return 0;
        float damage = round.Damage * HeShare * (1f - distance / round.SplashRadius) - thinnestArmor * HeArmorFactor;
        return RoundHalfUp(MathF.Max(0f, damage));
    }

    private static bool Pierces(ShellSpec round, float armor, float angle, uint seed) =>
        round.Penetration * ShotRandom.Range(seed, ShotRandom.PenetrationRoll, MinRoll, MaxRoll) >= EffectiveArmor(armor, angle);

    private static int RolledDamage(ShellSpec round, uint seed) =>
        RoundHalfUp(round.Damage * ShotRandom.Range(seed, ShotRandom.DamageRoll, MinRoll, MaxRoll));

    private static int RoundHalfUp(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}
```

修改 `src/Tank.Sim/GameEvent.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/GameEvent.cs b/src/Tank.Sim/GameEvent.cs
index c73195c..124c904 100644
--- a/src/Tank.Sim/GameEvent.cs
+++ b/src/Tank.Sim/GameEvent.cs
@@ -1,4 +1,5 @@
 using System.Numerics;
+using Tank.Sim.Combat;
 
 namespace Tank.Sim;
 
@@ -6,10 +7,34 @@ public enum GameEventType : byte
 {
     ShellFired,
     ShellExploded,
+    TankHit,
+    SplashDamage,
+    TankDestroyed,
+    TankRevived,
+}
+
+/// <summary>What a shell did to the armour it struck. <see cref="None"/> on events that aren't hits.</summary>
+public enum HitResult : byte
+{
+    None,
+    Penetrated,
+    Ricochet,
+    NotPenetrated,
 }
 
 /// <summary>
 /// Something presentation should react to. <see cref="Velocity"/> is zero when not meaningful.
 /// A shell that leaves the map or times out emits no event; clients key any per-shell visuals by <c>Shell.Id</c> and retire them when it disappears.
+/// <list type="bullet">
+/// <item><see cref="GameEventType.ShellFired"/>: <see cref="TankId"/> fired <see cref="ShellId"/> from <see cref="Position"/> at <see cref="Velocity"/>.</item>
+/// <item><see cref="GameEventType.ShellExploded"/>: an HE burst, or any shell striking the ground, a rock or a wreck, at <see cref="Position"/>.</item>
+/// <item><see cref="GameEventType.TankHit"/>: a shell from <see cref="TankId"/> struck <see cref="TargetTankId"/> at <see cref="Position"/>;
+/// <see cref="Velocity"/> holds the struck face's outward normal.</item>
+/// <item><see cref="GameEventType.SplashDamage"/>: an HE burst at <see cref="Position"/> hurt <see cref="TargetTankId"/>.</item>
+/// <item><see cref="GameEventType.TankDestroyed"/>: <see cref="TargetTankId"/> was knocked out by <see cref="TankId"/>; <see cref="Position"/> is the wreck.</item>
+/// <item><see cref="GameEventType.TankRevived"/>: <see cref="TargetTankId"/> (also <see cref="TankId"/>) is back at full health.</item>
+/// </list>
 /// </summary>
-public readonly record struct GameEvent(GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity);
+public readonly record struct GameEvent(
+    GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity,
+    int TargetTankId = 0, HitResult Result = HitResult.None, int Damage = 0, AmmoType Ammo = AmmoType.AP);
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"`
Expected: `通过: 179`。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): penetration, ricochet and HE damage rules; combat event fields"
```

---

### Task 4：散布

设计 §3.4：每 tick 由行驶、车体转向、炮塔转动算出扩大目标值；低于目标立即升到目标，高于目标按 τ = 缩圈时间 / 3 指数衰减；开火后加 3.0；开火偏移在圈内均匀取角度、半径取圈半径 × u（u 均匀），偏移换算成炮口方向的小偏转。

**Files:**
- Create: `src/Tank.Sim/Combat/Dispersion.cs`
- Test: `tests/Tank.Sim.Tests/DispersionTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/DispersionTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class DispersionTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    [Fact]
    public void Target_AddsEachMotionInProportion_CappedAtFullRate()
    {
        Assert.Equal(0f, Dispersion.Target(Spec, 0, 0, 0));
        Assert.Equal(2.0f, Dispersion.Target(Spec, Spec.MaxForwardSpeed, 0, 0), 1e-5f);
        Assert.Equal(1.0f, Dispersion.Target(Spec, -Spec.MaxForwardSpeed / 2, 0, 0), 1e-5f);
        Assert.Equal(1.5f, Dispersion.Target(Spec, 0, -Spec.HullTraverse, 0), 1e-5f);
        Assert.Equal(1.0f, Dispersion.Target(Spec, 0, 0, Spec.TurretTraverse * 3), 1e-5f);
        Assert.Equal(4.5f, Dispersion.Target(Spec, Spec.MaxForwardSpeed, Spec.HullTraverse, Spec.TurretTraverse), 1e-5f);
    }

    [Fact]
    public void Step_JumpsUpAtOnce()
    {
        Assert.Equal(2f, Dispersion.Step(0.5f, 2f, Spec.AimTime));
    }

    [Fact]
    public void Step_ClosesWithinAboutOneAimTime()
    {
        float bloom = 2f;
        int ticks = (int)MathF.Round(Spec.AimTime * SimConstants.TickRate);
        for (int i = 0; i < ticks; i++) bloom = Dispersion.Step(bloom, 0f, Spec.AimTime);
        Assert.InRange(bloom, 0.05f * 2f * 0.9f, 0.05f * 2f * 1.1f);   // three time constants: e⁻³ ≈ 5% left
    }

    [Fact]
    public void Radius_GrowsWithBloom()
    {
        Assert.Equal(0.36f, Dispersion.Radius(Spec, 0), 1e-6f);
        Assert.Equal(1.44f, Dispersion.Radius(Spec, 3), 1e-5f);
    }

    [Fact]
    public void Deflect_StaysInsideTheCircle_SameSeedSameOffset()
    {
        Vector3 aim = Angles.Direction(0.4f, -0.05f);
        float radius = 0.36f;
        int inner = 0;
        for (int shot = 0; shot < 1000; shot++)
        {
            uint seed = ShotRandom.Seed(10, 1, shot);
            Vector3 dir = Dispersion.Deflect(aim * 780f, radius, seed);
            Assert.Equal(1f, dir.Length(), 1e-5f);
            Assert.Equal(dir, Dispersion.Deflect(aim * 780f, radius, seed));
            float offsetAt100 = MathF.Tan(MathF.Acos(MathF.Min(1f, Vector3.Dot(dir, aim)))) * Dispersion.ReferenceRange;
            Assert.True(offsetAt100 <= radius * 1.001f, $"shot {shot} landed {offsetAt100} m off");
            if (offsetAt100 < radius / 2) inner++;
        }
        // Uniform radius share: half the shots fall in the inner half of the radius (a quarter of the area).
        Assert.InRange(inner, 430, 570);
    }

    [Fact]
    public void Deflect_WorksStraightUp()
    {
        Vector3 dir = Dispersion.Deflect(Vector3.UnitY, 0.36f, 5);
        Assert.True(dir.Y > 0.99f);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`Dispersion` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Combat/Dispersion.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Combat;

/// <summary>
/// The aim circle (design §3.4). Each tick moving, hull turning and turret turning set a bloom target; bloom jumps up to
/// it at once and decays back towards it with time constant AimTime / 3, so a tank that stops is aimed within about one
/// AimTime. Firing adds <see cref="BloomFactors.Fire"/>. Shells leave inside the circle, more often near its centre.
/// </summary>
public static class Dispersion
{
    /// <summary>Aim circles are quoted at this range, m.</summary>
    public const float ReferenceRange = 100f;

    /// <summary>Bloom from motion this tick; each rate counts in proportion to the tank's top rate, capped at that.</summary>
    public static float Target(TankSpec spec, float speed, float hullTurnRate, float turretTurnRate)
    {
        BloomFactors f = spec.Bloom;
        return f.Moving * Share(speed, spec.MaxForwardSpeed)
               + f.HullTurn * Share(hullTurnRate, spec.HullTraverse)
               + f.TurretTurn * Share(turretTurnRate, spec.TurretTraverse);
    }

    /// <summary>One tick: rise to <paramref name="target"/> at once, or decay towards it.</summary>
    public static float Step(float bloom, float target, float aimTime) =>
        bloom <= target ? target : target + (bloom - target) * MathF.Exp(-SimConstants.Dt * 3f / aimTime);

    /// <summary>Aim-circle radius at <see cref="ReferenceRange"/>, m.</summary>
    public static float Radius(TankSpec spec, float bloom) => spec.Dispersion * (1f + bloom);

    /// <summary>
    /// <paramref name="direction"/> turned by a random offset inside a circle of <paramref name="radius"/> at 100 m: a
    /// uniform angle around the circle and a uniform share of the radius, so shots bunch towards the centre. Unit length.
    /// </summary>
    public static Vector3 Deflect(Vector3 direction, float radius, uint seed)
    {
        Vector3 forward = Vector3.Normalize(direction);
        Vector3 side = Vector3.Cross(forward, Vector3.UnitY);
        side = side.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(side);
        Vector3 up = Vector3.Cross(side, forward);
        float around = ShotRandom.Unit(seed, ShotRandom.DispersionAngle) * Angles.TwoPi;
        float off = radius * ShotRandom.Unit(seed, ShotRandom.DispersionRadius) / ReferenceRange;
        return Vector3.Normalize(forward + side * (off * MathF.Cos(around)) + up * (off * MathF.Sin(around)));
    }

    private static float Share(float rate, float top) => MathF.Min(MathF.Abs(rate) / top, 1f);
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"`
Expected: `通过: 185`。

- [ ] **Step 5：提交，并结束批次 A**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): aim circle bloom and shot dispersion"
```

批次 A 结束：规格审查 → 代码质量审查 → 在 `docs/PROGRESS.md` 记一行（提交号、Sim 185）。

---

### Task 5：换弹种、弹药与开火散布

设计 §3.3、§3.4、§3.8 的补给部分接进 `Simulation`：

- 每个坦克在行驶、瞄准之后、开火之前更新散布（`UpdateBloom`，用这一 tick 车体和炮塔转过的角度算转速）；
- `TryFire`：先倒数装填；输入的弹种与当前不同就换弹并重新开始整个装填，这一 tick 不开火；当前弹种没弹时不能开火；开火时按散布圈偏转炮弹方向，扣一发弹药，散布加 3.0，`ShellFired` 带上弹种；
- `Sanitize` 把非法弹种当作“不换”；
- `Restock(id)` 补满两种弹药；`TankRef(id)` 按 id 找坦克（`internal`，测试也用）。

原来的“准星精确预测落点”测试改为：开出的炮弹方向在散布圈内，并且落点等于用实际出膛速度预测的落点。

**Files:**
- Modify: `src/Tank.Sim/InputCmd.cs`、`src/Tank.Sim/Simulation.cs`
- Test: 新建 `tests/Tank.Sim.Tests/AmmoAndAimTests.cs`；改 `TestWorld.cs`（`Cmd` 加弹种参数）、`SimulationTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/AmmoAndAimTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class AmmoAndAimTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;
    private static readonly int ReloadTicks = (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate);

    private static Simulation NewSim(out int tankId)
    {
        var sim = new Simulation(TestWorld.FlatField());
        tankId = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        return sim;
    }

    private static List<GameEvent> Run(Simulation sim, InputCmd cmd, int ticks)
    {
        var events = new List<GameEvent>();
        var inputs = new[] { cmd };
        for (int i = 0; i < ticks; i++)
        {
            sim.Step(inputs);
            events.AddRange(sim.Events);
        }
        return events;
    }

    [Fact]
    public void Firing_SpendsARoundOfTheLoadedType_AndTagsTheShell()
    {
        var sim = NewSim(out _);
        var events = Run(sim, TestWorld.Cmd(fire: true), 1);
        Assert.Equal(39, sim.Tanks[0].ApLeft);
        Assert.Equal(15, sim.Tanks[0].HeLeft);
        Assert.Equal(AmmoType.AP, Assert.Single(events, e => e.Type == GameEventType.ShellFired).Ammo);
        Assert.Equal(AmmoType.AP, sim.Shells[0].Ammo);
        Assert.Equal(Spec.Ap, sim.Shells[0].Round);
    }

    [Fact]
    public void SwitchingAmmo_RestartsTheReload_ThenFiresTheNewType()
    {
        var sim = NewSim(out _);
        var he = TestWorld.Cmd(fire: true, ammo: AmmoType.HE);
        var events = Run(sim, he, 1);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired);   // swapping rounds, not firing
        Assert.Equal(AmmoType.HE, sim.Tanks[0].Ammo);
        Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);

        events = Run(sim, he, ReloadTicks + 1);
        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.Equal(1 + ReloadTicks, fired.Tick);
        Assert.Equal(AmmoType.HE, fired.Ammo);
        Assert.Equal(Spec.He.Speed, fired.Velocity.Length(), 0.5f);
        Assert.Equal(14, sim.Tanks[0].HeLeft);
    }

    [Fact]
    public void SwitchingBackMidReload_StartsAFreshFullReload()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(fire: true), 1);            // fire AP: reload 6.5 s
        Run(sim, TestWorld.Cmd(), 60);                     // 2 s in
        Run(sim, TestWorld.Cmd(ammo: AmmoType.HE), 1);     // swap
        Assert.Equal(Spec.ReloadTime, sim.Tanks[0].Reload, 1e-4f);
    }

    [Fact]
    public void AnEmptyRack_CantFire_UntilRestocked()
    {
        var sim = NewSim(out int id);
        sim.TankRef(id).ApLeft = 0;
        Assert.DoesNotContain(Run(sim, TestWorld.Cmd(fire: true), 5), e => e.Type == GameEventType.ShellFired);

        sim.Restock(id);
        Assert.Equal(40, sim.Tanks[0].ApLeft);
        Assert.Equal(15, sim.Tanks[0].HeLeft);
        Assert.Single(Run(sim, TestWorld.Cmd(fire: true), 1), e => e.Type == GameEventType.ShellFired);
    }

    [Fact]
    public void Restock_RejectsAnUnknownTank()
    {
        var sim = NewSim(out _);
        Assert.Throws<ArgumentException>(() => sim.Restock(99));
    }

    [Fact]
    public void Sanitize_KeepsTheLoadedRound_ForAnUnknownAmmoType()
    {
        var tank = TestWorld.Tank(100, 100) with { Ammo = AmmoType.HE };
        Assert.Equal(AmmoType.HE, Simulation.Sanitize(TestWorld.Cmd(ammo: (AmmoType)7), tank).Ammo);
        Assert.Equal(AmmoType.AP, Simulation.Sanitize(TestWorld.Cmd(ammo: AmmoType.AP), tank).Ammo);
    }

    [Fact]
    public void Firing_OpensTheAimCircle_WhichThenCloses()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(fire: true), 1);
        Assert.Equal(Spec.Bloom.Fire, sim.Tanks[0].Bloom, 1e-4f);
        Run(sim, TestWorld.Cmd(), (int)MathF.Round(Spec.AimTime * SimConstants.TickRate));
        Assert.InRange(sim.Tanks[0].Bloom, 0f, Spec.Bloom.Fire * 0.06f);
    }

    [Fact]
    public void Driving_OpensTheAimCircle_StoppingClosesIt()
    {
        var sim = NewSim(out _);
        Run(sim, TestWorld.Cmd(throttle: 1), 150);   // 5 s: at top speed
        Assert.Equal(Spec.Bloom.Moving, sim.Tanks[0].Bloom, 0.05f);
        Run(sim, TestWorld.Cmd(), 90);               // braked to a stop well inside 3 s, then settling
        Assert.True(sim.Tanks[0].Bloom < 0.5f, $"bloom still {sim.Tanks[0].Bloom}");
    }

    [Fact]
    public void Dispersion_IsTheSameForTheSameShot()
    {
        Vector3 FirstShot()
        {
            var sim = NewSim(out _);
            Run(sim, TestWorld.Cmd(throttle: 1), 30);   // open the circle so the offset is large
            return Assert.Single(Run(sim, TestWorld.Cmd(throttle: 1, fire: true), 1), e => e.Type == GameEventType.ShellFired).Velocity;
        }
        Assert.Equal(FirstShot(), FirstShot());
    }
}
```

修改 `tests/Tank.Sim.Tests/SimulationTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/SimulationTests.cs b/tests/Tank.Sim.Tests/SimulationTests.cs
index 33044d8..c169082 100644
--- a/tests/Tank.Sim.Tests/SimulationTests.cs
+++ b/tests/Tank.Sim.Tests/SimulationTests.cs
@@ -147,16 +147,20 @@ public sealed class SimulationTests
     }
 
     [Fact]
-    public void Reticle_PredictsTheExactImpactOfTheNextShot()
+    public void Reticle_PredictsTheCentreOfTheAimCircle_AndTheShellFollowsItsOwnPrediction()
     {
         var sim = NewSim(out _);
         var aim = TestWorld.Cmd(aimPitch: -0.05f);
-        Run(sim, aim, 30);
+        Run(sim, aim, 90);   // gun settled and the aim circle closed
         var launch = Tank.Sim.Combat.Ballistics.Aim(sim.Tanks[0], sim.Field);
         Assert.False(launch.Blocked);
-        Assert.True(Tank.Sim.Combat.Ballistics.PredictImpact(launch.Origin, launch.Velocity, sim.Field, out Vector3 predicted));
+        float radius = Tank.Sim.Combat.Dispersion.Radius(Spec, sim.Tanks[0].Bloom);
 
         var events = Run(sim, TestWorld.Cmd(aimPitch: -0.05f, fire: true), 90);
+        GameEvent fired = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
+        float off = MathF.Acos(MathF.Min(1f, Vector3.Dot(Vector3.Normalize(fired.Velocity), Vector3.Normalize(launch.Velocity))));
+        Assert.True(MathF.Tan(off) * 100f <= radius * 1.001f, $"left {MathF.Tan(off) * 100f} m off at 100 m, circle {radius} m");
+        Assert.True(Tank.Sim.Combat.Ballistics.PredictImpact(fired.Position, fired.Velocity, sim.Field, out Vector3 predicted));
         GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
         Assert.Equal(predicted, boom.Position);
     }
PATCH
```

修改 `tests/Tank.Sim.Tests/TestWorld.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TestWorld.cs b/tests/Tank.Sim.Tests/TestWorld.cs
index 87e4e52..26cd779 100644
--- a/tests/Tank.Sim.Tests/TestWorld.cs
+++ b/tests/Tank.Sim.Tests/TestWorld.cs
@@ -1,4 +1,5 @@
 using System.Numerics;
+using Tank.Sim.Combat;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Terrain;
@@ -43,8 +44,8 @@ internal static class TestWorld
     public static TankState Tank(float x, float z, float yaw = 0f) =>
         new() { Id = 1, Spec = TankCatalog.Medium, Position = new Vector2(x, z), Yaw = yaw };
 
-    public static InputCmd Cmd(sbyte throttle = 0, sbyte steer = 0, float aimYaw = 0f, float aimPitch = 0f, bool fire = false) =>
-        new(0, throttle, steer, aimYaw, aimPitch, fire);
+    public static InputCmd Cmd(sbyte throttle = 0, sbyte steer = 0, float aimYaw = 0f, float aimPitch = 0f, bool fire = false, AmmoType ammo = AmmoType.AP) =>
+        new(0, throttle, steer, aimYaw, aimPitch, fire, ammo);
 
     public static void Drive(ref TankState t, Battlefield field, InputCmd cmd, int ticks, params Obb2[] obstacles)
     {
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败（`InputCmd` 没有弹种参数，`Restock`、`TankRef` 不存在）。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/InputCmd.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/InputCmd.cs b/src/Tank.Sim/InputCmd.cs
index 9676aa5..a31cae0 100644
--- a/src/Tank.Sim/InputCmd.cs
+++ b/src/Tank.Sim/InputCmd.cs
@@ -1,8 +1,11 @@
+using Tank.Sim.Combat;
+
 namespace Tank.Sim;
 
 /// <summary>
 /// One tick of driver input. <see cref="Throttle"/> and <see cref="Steer"/> are -1, 0 or 1 (Steer +1 = turn right).
 /// <see cref="Fire"/> is level-triggered: hold to fire whenever reloaded; don't edge-detect.
 /// Aim is a world-space direction from the gun trunnion (yaw as in Geometry.Angles, pitch positive up).
+/// <see cref="Ammo"/> is the round the driver wants loaded; changing it unloads the gun and starts a full reload.
 /// </summary>
-public readonly record struct InputCmd(int Tick, sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire);
+public readonly record struct InputCmd(int Tick, sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire, AmmoType Ammo = AmmoType.AP);
PATCH
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index f42b475..a345a23 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -9,7 +9,8 @@ namespace Tank.Sim;
 
 /// <summary>
 /// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted, each tank
-/// drives, aims and fires (in slot order) against that snapshot, then every shell flies and resolves its hits.
+/// drives, aims, updates its aim circle and fires (in slot order) against that snapshot, then every shell flies and
+/// resolves its hits.
 /// </summary>
 public sealed class Simulation
 {
@@ -68,16 +69,33 @@ public sealed class Simulation
         {
             ref TankState tank = ref _tanks[i];
             InputCmd input = Sanitize(inputs[i], tank);
+            float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
             TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i));
             TankMotion.AimGun(ref tank, input, Field.Terrain);
+            UpdateBloom(ref tank, yaw, turretYaw);
             TryFire(ref tank, input);
         }
         StepShells();
     }
 
+    /// <summary>Refills both ammunition racks (sandbox only for now).</summary>
+    public void Restock(int tankId)
+    {
+        ref TankState tank = ref TankRef(tankId);
+        tank.ApLeft = tank.Spec.Ap.Capacity;
+        tank.HeLeft = tank.Spec.He.Capacity;
+    }
+
+    internal ref TankState TankRef(int tankId)
+    {
+        for (int i = 0; i < TankCount; i++)
+            if (_tanks[i].Id == tankId) return ref _tanks[i];
+        throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
+    }
+
     /// <summary>
-    /// Inputs come from the network: clamp the axes, and replace a non-finite aim with the turret's current heading
-    /// so a bad packet can't spin the turret.
+    /// Inputs come from the network: clamp the axes, replace a non-finite aim with the turret's current heading so a bad
+    /// packet can't spin the turret, and keep the loaded round if the requested one isn't a known type.
     /// </summary>
     internal static InputCmd Sanitize(in InputCmd input, in TankState tank)
     {
@@ -86,7 +104,17 @@ public sealed class Simulation
         bool aimValid = float.IsFinite(input.AimYaw) && float.IsFinite(input.AimPitch);
         float aimYaw = aimValid ? input.AimYaw : Angles.Wrap(tank.Yaw + tank.TurretYaw);
         float aimPitch = aimValid ? input.AimPitch : 0f;
-        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch };
+        AmmoType ammo = input.Ammo is AmmoType.AP or AmmoType.HE ? input.Ammo : tank.Ammo;
+        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch, Ammo = ammo };
+    }
+
+    /// <summary>Opens the aim circle for this tick's driving, hull turn and turret turn, or lets it settle.</summary>
+    private static void UpdateBloom(ref TankState tank, float oldYaw, float oldTurretYaw)
+    {
+        float hullRate = Angles.Wrap(tank.Yaw - oldYaw) / SimConstants.Dt;
+        float turretRate = Angles.Wrap(tank.TurretYaw - oldTurretYaw) / SimConstants.Dt;
+        float target = Dispersion.Target(tank.Spec, tank.Speed, hullRate, turretRate);
+        tank.Bloom = Dispersion.Step(tank.Bloom, target, tank.Spec.AimTime);
     }
 
     /// <summary>Every tank collides against where the others were at the start of the tick, independent of slot order.</summary>
@@ -107,28 +135,47 @@ public sealed class Simulation
         return _others.AsSpan(0, n);
     }
 
+    /// <summary>
+    /// Counts the reload down, swaps rounds if the input asks for the other type (which restarts the reload), then fires
+    /// if asked, loaded and a round of that type is left. The shell leaves inside the aim circle, which then opens.
+    /// </summary>
     private void TryFire(ref TankState tank, in InputCmd input)
     {
         tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
         if (tank.Reload < ReloadEpsilon) tank.Reload = 0f;
-        if (!input.Fire || tank.Reload > 0 || ShellCount == MaxShells) return;
+        if (input.Ammo != tank.Ammo)
+        {
+            tank.Ammo = input.Ammo;
+            tank.Reload = tank.Spec.ReloadTime;
+            return;
+        }
+        if (!input.Fire || tank.Reload > 0 || tank.AmmoLeft(tank.Ammo) == 0 || ShellCount == MaxShells) return;
 
         Ballistics.Launch launch = Ballistics.Aim(tank, Field);
+        uint seed = ShotRandom.Seed(Tick, tank.Id, tank.ShotCount);
+        float speed = launch.Velocity.Length();
         var shell = new Shell
         {
             Id = _nextId++,
             OwnerTankId = tank.Id,
             Position = launch.Origin,
-            Velocity = launch.Velocity,
+            Velocity = Dispersion.Deflect(launch.Velocity, Dispersion.Radius(tank.Spec, tank.Bloom), seed) * speed,
+            Ammo = tank.Ammo,
+            Round = tank.Spec.Shell(tank.Ammo),
+            Caliber = tank.Spec.Caliber,
+            Seed = seed,
         };
         tank.Reload = tank.Spec.ReloadTime;
         tank.ShotCount++;
-        _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity));
+        tank.Bloom += tank.Spec.Bloom.Fire;
+        if (tank.Ammo == AmmoType.HE) tank.HeLeft--;
+        else tank.ApLeft--;
+        _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity, Ammo: shell.Ammo));
 
         // Barrel pushed into a rock or the ground: the shell bursts where the barrel meets it instead of spawning inside.
         if (launch.Blocked)
         {
-            _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, tank.Id, shell.Id, launch.BlockPoint, Vector3.Zero));
+            _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, tank.Id, shell.Id, launch.BlockPoint, Vector3.Zero, Ammo: shell.Ammo));
             return;
         }
         _shells[ShellCount++] = shell;
@@ -142,7 +189,7 @@ public sealed class Simulation
             ShellStep step = Ballistics.Step(ref shell, Field, out RayHit rayHit);
             if (step == ShellStep.Flying) continue;
             if (step == ShellStep.Hit)
-                _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, rayHit.Point, Vector3.Zero));
+                _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, rayHit.Point, Vector3.Zero, Ammo: shell.Ammo));
             _shells[i] = _shells[--ShellCount];
         }
     }
PATCH
```


- [ ] **Step 4：运行仿真测试和客户端编译**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"; dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Error\(s\)"`
Expected: `通过: 194`；客户端 `0 Error(s)`（`SandboxMain` 里的 `new InputCmd(...)` 没写弹种，默认穿甲弹，照常编译）。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): choose AP or HE, finite ammunition, restock, shots leave inside the aim circle"
```

---

### Task 6：装甲盒随车体和炮塔

`TankPose` 给出世界坐标的车体盒（随车体的高度、俯仰、侧倾）和炮塔盒（再加上炮塔转角，盒心在座圈上方）。`ArmorHit.Find` 对两个盒求交、取近的，返回命中点、命中面外法线、哪一面、车体还是炮塔、该面装甲厚度。

**Files:**
- Create: `src/Tank.Sim/Combat/ArmorHit.cs`
- Modify: `src/Tank.Sim/Vehicles/TankPose.cs`
- Test: `tests/Tank.Sim.Tests/ArmorHitTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/ArmorHitTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class ArmorHitTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;

    /// <summary>A tank at (100, 100) on flat ground; yaw 0 faces north (−Z).</summary>
    private static (OrientedBox Hull, OrientedBox Turret) Boxes(float yaw = 0, float turretYaw = 0, Tank.Sim.Terrain.HeightField? ground = null)
    {
        var tank = TestWorld.Tank(100, 100, yaw);
        TankPose pose = TankPose.Compute(tank, ground ?? TestWorld.Flat());
        return (pose.HullBox(Spec), pose.TurretBox(Spec, turretYaw));
    }

    private static ArmorHit Shoot(Vector3 from, Vector3 to, float yaw = 0, float turretYaw = 0, Tank.Sim.Terrain.HeightField? ground = null)
    {
        var (hull, turret) = Boxes(yaw, turretYaw, ground);
        Assert.True(ArmorHit.Find(Spec, hull, turret, from, to, 1f, out ArmorHit hit), "missed");
        return hit;
    }

    [Theory]
    [InlineData(100, 1.0f, 50, 100, 1.0f, 150, ArmorFace.Front, 90f)]    // from the north, low
    [InlineData(150, 1.0f, 100, 50, 1.0f, 100, ArmorFace.Side, 60f)]     // from the east
    [InlineData(100, 1.0f, 150, 100, 1.0f, 50, ArmorFace.Rear, 40f)]     // from the south
    [InlineData(100, 30f, 101.5f, 100, -5f, 101.5f, ArmorFace.Top, 20f)] // straight down on the rear deck
    [InlineData(100, -5f, 101.5f, 100, 30f, 101.5f, ArmorFace.Bottom, 20f)]
    public void HullFaces(float fx, float fy, float fz, float tx, float ty, float tz, ArmorFace face, float thickness)
    {
        ArmorHit hit = Shoot(new Vector3(fx, fy, fz), new Vector3(tx, ty, tz));
        Assert.Equal(HitPart.Hull, hit.Part);
        Assert.Equal(face, hit.Face);
        Assert.Equal(thickness, hit.Thickness);
    }

    [Fact]
    public void Turret_IsHitAboveTheHull_WithItsOwnArmour()
    {
        ArmorHit hit = Shoot(new Vector3(100, 2.3f, 50), new Vector3(100, 2.3f, 150));
        Assert.Equal(HitPart.Turret, hit.Part);
        Assert.Equal(ArmorFace.Front, hit.Face);
        Assert.Equal(120f, hit.Thickness);
        Assert.Equal(100 - Spec.TurretOffset - Spec.TurretArmor.Length / 2, hit.Point.Z, 1e-3f);
    }

    [Fact]
    public void TurnedTurret_ShowsItsSideToAShotFromAhead()
    {
        ArmorHit hit = Shoot(new Vector3(100, 2.3f, 50), new Vector3(100, 2.3f, 150), turretYaw: MathF.PI / 2);
        Assert.Equal(HitPart.Turret, hit.Part);
        Assert.Equal(ArmorFace.Side, hit.Face);
        Assert.Equal(80f, hit.Thickness);
        Assert.Equal(1f, hit.Normal.Z * -1f, 1e-4f);   // the side plate now faces north
    }

    [Fact]
    public void TiltedHull_TiltsItsFaces()
    {
        // Nose up a 0.3 grade: the ground is 30 m up at z = 100 and the tilted front plate's centre about 31.9 m. A level
        // shot from the north meets that plate, whose normal now leans upward by the slope.
        ArmorHit hit = Shoot(new Vector3(100, 31.9f, 50), new Vector3(100, 31.9f, 150), ground: TestWorld.RampNorth(0.3f));
        Assert.Equal(ArmorFace.Front, hit.Face);
        Assert.True(hit.Normal.Y > 0.25f, $"normal {hit.Normal}");
        float angle = Penetration.ImpactAngle(new Vector3(0, 0, 1), hit.Normal);
        Assert.Equal(MathF.Atan(0.3f), angle, 0.02f);
    }

    [Fact]
    public void YawedHull_ShowsItsSide()
    {
        ArmorHit hit = Shoot(new Vector3(100, 1.0f, 50), new Vector3(100, 1.0f, 150), yaw: MathF.PI / 2);
        Assert.Equal(ArmorFace.Side, hit.Face);
    }

    [Fact]
    public void AShellStartingInsideTheHull_StrikesThePlateFacingIt()
    {
        // A muzzle poked into the hull from behind, firing forward: treat it as entering the rear plate head-on, not a graze.
        ArmorHit hit = Shoot(new Vector3(100, 1.0f, 102.9f), new Vector3(100, 1.0f, 90f));
        Assert.Equal(0f, hit.T);
        Assert.Equal(ArmorFace.Rear, hit.Face);
        Assert.True(Penetration.ImpactAngle(new Vector3(0, 0, -1), hit.Normal) < 0.01f);
    }

    [Fact]
    public void Find_TakesTheNearerBox_AndHonoursMaxT()
    {
        var (hull, turret) = Boxes();
        // From high in front, diving onto the hull's glacis area: the turret is behind and out of the way.
        Vector3 from = new(100, 1.6f, 80), to = new(100, 1.6f, 120);
        Assert.True(ArmorHit.Find(Spec, hull, turret, from, to, 1f, out ArmorHit hit));
        Assert.Equal(HitPart.Hull, hit.Part);
        Assert.False(ArmorHit.Find(Spec, hull, turret, from, to, hit.T * 0.9f, out _));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`ArmorHit`、`TankPose.HullBox` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Combat/ArmorHit.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Combat;

/// <summary>Where a shell's path enters a tank: segment parameter, point, face normal, which plate, and its thickness (mm).</summary>
public readonly record struct ArmorHit(float T, Vector3 Point, Vector3 Normal, ArmorFace Face, HitPart Part, float Thickness)
{
    /// <summary>
    /// The nearer entry of segment <paramref name="from"/>→<paramref name="to"/> into the hull or turret box, if it comes
    /// before <paramref name="maxT"/>.
    /// </summary>
    public static bool Find(TankSpec spec, in OrientedBox hull, in OrientedBox turret, Vector3 from, Vector3 to, float maxT, out ArmorHit hit)
    {
        hit = default;
        bool found = false;
        if (hull.Raycast(from, to, out float t, out BoxFace face) && t < maxT)
        {
            hit = Make(spec.HullArmor, hull, HitPart.Hull, from, to, t, face);
            maxT = t;
            found = true;
        }
        if (turret.Raycast(from, to, out t, out face) && t < maxT)
        {
            hit = Make(spec.TurretArmor, turret, HitPart.Turret, from, to, t, face);
            found = true;
        }
        return found;
    }

    private static ArmorHit Make(ArmorBox box, in OrientedBox world, HitPart part, Vector3 from, Vector3 to, float t, BoxFace face)
    {
        ArmorFace plate = Armor.FaceOf(face);
        return new ArmorHit(t, Vector3.Lerp(from, to, t), world.Normal(face), plate, part, box.Armor[plate]);
    }
}
```

修改 `src/Tank.Sim/Vehicles/TankPose.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankPose.cs b/src/Tank.Sim/Vehicles/TankPose.cs
index 4e15598..dc6fdb8 100644
--- a/src/Tank.Sim/Vehicles/TankPose.cs
+++ b/src/Tank.Sim/Vehicles/TankPose.cs
@@ -1,4 +1,5 @@
 using System.Numerics;
+using Tank.Sim.Combat;
 using Tank.Sim.Geometry;
 using Tank.Sim.Terrain;
 
@@ -40,6 +41,18 @@ public readonly record struct TankPose(Vector3 Position, Quaternion Rotation)
     public Vector3 Muzzle(TankSpec spec, float turretYaw, float gunPitch) =>
         Trunnion(spec) + GunDirection(turretYaw, gunPitch) * spec.GunLength;
 
+    /// <summary>The hull's armour box in world space; it tilts with the hull.</summary>
+    public OrientedBox HullBox(TankSpec spec) =>
+        new(Position + Vector3.Transform(spec.HullArmor.LocalCenter, Rotation), spec.HullArmor.HalfExtents, Rotation);
+
+    /// <summary>The turret's armour box in world space: centred over the turret ring, tilted with the hull, turned by <paramref name="turretYaw"/>.</summary>
+    public OrientedBox TurretBox(TankSpec spec, float turretYaw)
+    {
+        Vector3 ring = new(0, 0, -spec.TurretOffset);
+        Quaternion rotation = Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, turretYaw);
+        return new OrientedBox(Position + Vector3.Transform(ring + spec.TurretArmor.LocalCenter, Rotation), spec.TurretArmor.HalfExtents, rotation);
+    }
+
     /// <summary>Turret yaw and gun pitch (hull-relative) that point the barrel along world <paramref name="direction"/>.</summary>
     public (float TurretYaw, float GunPitch) ToHullAngles(Vector3 direction, float fallbackYaw = 0f) =>
         Angles.AimAngles(Vector3.Transform(direction, Quaternion.Inverse(Rotation)), fallbackYaw);
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"`
Expected: `通过: 205`。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): hull and turret hit boxes on the tank pose"
```

---

### Task 7：炮弹命中坦克、溅射、击毁与复活

`Simulation` 改为分部类，炮弹相关的代码搬到新文件 `Simulation.Shells.cs`：

- 每 tick 炮弹先照旧对地形和岩石求交，再对除射手外每辆坦克的两个装甲盒求交（先用包围球粗筛），按距离取最近的；
- 打中活坦克：按弹种结算（穿甲弹可能跳弹，高爆弹不跳弹、在装甲上爆炸），发 `TankHit`（`Velocity` 放命中面法线），扣血；
- 打中残骸：只停下炮弹，发 `ShellExploded`，高爆弹照常溅射别人；
- 爆炸 `Explode`：发 `ShellExploded`；有溅射半径（高爆弹）时，对半径内除射手、被直接命中者、残骸以外的坦克按到车体盒的距离计算溅射，伤害大于 0 才发 `SplashDamage` 并扣血；
- 扣血 `Damage`：降到 0 时置为残骸、速度归零，发 `TankDestroyed`；
- 炮管顶进岩石时的爆炸也走 `Explode`，所以高爆弹会溅射；
- 残骸不再行驶、瞄准、开炮，但仍在碰撞快照里，照样挡车；
- `Revive(id)`：满血、清残骸、散布归零、装填清零，事件放进待发队列，由下一次 `Step` 报告。

测试夹具 `Duel` 摆好两辆坦克（射手在 (100, 150) 朝北，靶车在 (100, 100)），提供“瞄准车体上某点”“开一炮并跟踪半秒”。

**Files:**
- Create: `src/Tank.Sim/Simulation.Shells.cs`
- Modify: `src/Tank.Sim/Simulation.cs`
- Test: `tests/Tank.Sim.Tests/Duel.cs`、`TankHitTests.cs`、`HighExplosiveTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/Duel.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>
/// Two medium tanks on flat ground: the shooter in slot 0 at (100, 150) facing north, the target in slot 1 at (100, 100)
/// turned to <c>targetYaw</c> (π shows the shooter its front). The target's input holds its turret straight ahead.
/// </summary>
internal sealed class Duel
{
    public static readonly TankSpec Spec = TankCatalog.Medium;
    public static readonly Vector2 ShooterAt = new(100, 150);
    public static readonly Vector2 TargetAt = new(100, 100);

    private readonly InputCmd[] _inputs = new InputCmd[2];

    public Duel(float targetYaw, params Prop[] props)
    {
        Sim = new Simulation(TestWorld.FlatField(props));
        ShooterId = Sim.AddTank(Spec, ShooterAt, 0f);
        TargetId = Sim.AddTank(Spec, TargetAt, targetYaw);
        TargetYaw = targetYaw;
    }

    public Simulation Sim { get; }
    public int ShooterId { get; }
    public int TargetId { get; }
    public float TargetYaw { get; }
    public TankState Shooter => Sim.Tanks[0];
    public TankState Target => Sim.Tanks[1];

    /// <summary>A point on the target: <paramref name="local"/> in hull space (x right, y up, −z forward), turned by its yaw.</summary>
    public Vector3 OnTarget(Vector3 local) =>
        new Vector3(TargetAt.X, 0, TargetAt.Y) + Vector3.Transform(local, Quaternion.CreateFromAxisAngle(Vector3.UnitY, TargetYaw));

    /// <summary>Lays the gun on <paramref name="point"/> and lets turret, gun and aim circle settle for two seconds.</summary>
    public InputCmd AimAt(Vector3 point, AmmoType ammo = AmmoType.AP)
    {
        TankPose pose = TankPose.Compute(Shooter, Sim.Field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(point - pose.Trunnion(Spec));
        var aim = new InputCmd(0, 0, 0, yaw, pitch, false, ammo);
        Run(aim, 60);
        return aim;
    }

    /// <summary>Fires once at the current aim and follows the shell for half a second.</summary>
    public List<GameEvent> Fire(InputCmd aim)
    {
        var events = Run(aim with { Fire = true }, 1);
        events.AddRange(Run(aim, 15));
        return events;
    }

    public List<GameEvent> Run(InputCmd shooter, int ticks, InputCmd? target = null)
    {
        var events = new List<GameEvent>();
        for (int i = 0; i < ticks; i++)
        {
            _inputs[0] = shooter;
            _inputs[1] = target ?? new InputCmd(0, 0, 0, TargetYaw, 0, false);
            Sim.Step(_inputs);
            events.AddRange(Sim.Events);
        }
        return events;
    }
}
```

新建 `tests/Tank.Sim.Tests/HighExplosiveTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class HighExplosiveTests
{
    private static readonly TankSpec Spec = TankCatalog.Medium;
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);

    [Fact]
    public void DirectHit_OnThickArmour_DealsReducedDamage_AndBursts()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));   // swapping to HE restarts the reload
        var events = duel.Fire(aim);

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(AmmoType.HE, hit.Ammo);
        Assert.Equal(HitResult.NotPenetrated, hit.Result);
        Assert.Equal(41, hit.Damage);                                  // 280 × 0.5 − 90 × 1.1 (a near head-on shot)
        Assert.Equal(900 - 41, duel.Target.Health);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(hit.Position, boom.Position);
        Assert.True(events.IndexOf(boom) > events.IndexOf(hit), "the hit is reported before the burst");
        Assert.DoesNotContain(events, e => e.Type == GameEventType.SplashDamage);   // the struck tank isn't splashed too
    }

    [Fact]
    public void GroundBurst_SplashesTheTankNearby_FadingWithDistance()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(new Vector3(0, 0, -4.1f)), AmmoType.HE);   // the ground 1 m before its nose
        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));
        var events = duel.Fire(aim);

        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(boom.Position, splash.Position);
        Assert.Equal(duel.TargetId, splash.TargetTankId);
        Assert.Equal(duel.ShooterId, splash.TankId);
        float distance = TankPose.Compute(duel.Target, duel.Sim.Field.Terrain).HullBox(Spec).DistanceTo(boom.Position);
        Assert.InRange(distance, 0.3f, 2f);
        Assert.Equal(Penetration.Splash(Spec.He, distance, 20), splash.Damage);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900 - splash.Damage, duel.Target.Health);
    }

    [Fact]
    public void BlockedBarrel_Burst_SplashesANeighbour_ButNeverTheShooter()
    {
        // A narrow rock face 3 m ahead of the shooter's trunnion: the barrel pokes into it and HE bursts at the face.
        float face = 100 - Spec.TurretOffset - 3f;
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, face - 2), new Vector2(1.5f, 2), 0), -1, 6);
        var sim = new Simulation(TestWorld.FlatField(rock));
        int shooter = sim.AddTank(Spec, new Vector2(100, 100), 0f);
        int neighbour = sim.AddTank(Spec, new Vector2(103.3f, 99), 0f);   // just clear of the shooter's right side
        var he = new InputCmd(0, 0, 0, 0, 0, false, AmmoType.HE);
        var inputs = new[] { he, new InputCmd(0, 0, 0, 0, 0, false) };
        for (int i = 0; i < (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate) + 1; i++) sim.Step(inputs);
        inputs[0] = he with { Fire = true };
        sim.Step(inputs);

        GameEvent boom = Assert.Single(sim.Events.ToArray(), e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(face, boom.Position.Z, 0.01f);
        GameEvent splash = Assert.Single(sim.Events.ToArray(), e => e.Type == GameEventType.SplashDamage);
        Assert.Equal(neighbour, splash.TargetTankId);
        Assert.Equal(shooter, splash.TankId);
        Assert.True(splash.Damage > 0);
        Assert.Equal(900, sim.Tanks[0].Health);
    }

    [Fact]
    public void Splash_CanWreckATank_ButSkipsWrecks()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        InputCmd aim = duel.AimAt(duel.OnTarget(new Vector3(0, 0, -4.1f)), AmmoType.HE);
        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));
        var events = duel.Fire(aim);
        int splash = events.FindIndex(e => e.Type == GameEventType.SplashDamage);
        int destroyed = events.FindIndex(e => e.Type == GameEventType.TankDestroyed);
        Assert.True(splash >= 0 && destroyed > splash, "expected SplashDamage then TankDestroyed");

        duel.Run(aim, (int)MathF.Round(Spec.ReloadTime * SimConstants.TickRate));
        events = duel.Fire(aim);
        Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.SplashDamage);
    }
}
```

新建 `tests/Tank.Sim.Tests/TankHitTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class TankHitTests
{
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);     // hull-space centre of the front plate
    private static readonly Vector3 TurretFront = new(0, 2.24f, -0.3f - 1.45f);

    [Fact]
    public void Ap_HeadOn_Penetrates_AndTakesHealth()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(duel.ShooterId, hit.TankId);
        Assert.Equal(duel.TargetId, hit.TargetTankId);
        Assert.Equal(HitResult.Penetrated, hit.Result);
        Assert.Equal(AmmoType.AP, hit.Ammo);
        Assert.InRange(hit.Damage, 165, 275);
        Assert.Equal(103.1f, hit.Position.Z, 0.01f);                  // on the front plate, which faces the shooter (+Z)
        Assert.Equal(1f, hit.Velocity.Z, 1e-3f);                       // Velocity carries the plate's normal
        Assert.Equal(900 - hit.Damage, duel.Target.Health);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellExploded);   // AP doesn't burst
        Assert.Equal(0, duel.Sim.ShellCount);
    }

    [Fact]
    public void Ap_GlancingOffTheFrontPlate_Ricochets()
    {
        var duel = new Duel(targetYaw: MathF.PI + Angles.Deg(75));   // front plate turned 75° away from the shot
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(HitResult.Ricochet, hit.Result);
        Assert.Equal(0, hit.Damage);
        Assert.Equal(900, duel.Target.Health);
    }

    [Fact]
    public void Ap_AgainstTheAngledTurretFront_DoesNotPenetrate()
    {
        // 120 mm at about 60° is about 240 mm effective, beyond the best roll of 175 × 1.25.
        var duel = new Duel(targetYaw: MathF.PI + Angles.Deg(60));
        var events = duel.Fire(duel.AimAt(duel.OnTarget(TurretFront)));

        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(HitResult.NotPenetrated, hit.Result);
        Assert.Equal(0, hit.Damage);
        Assert.Equal(900, duel.Target.Health);
    }

    [Fact]
    public void ARockInTheWay_TakesTheShellFirst()
    {
        var rock = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 125), new Vector2(3, 1), 0), -1, 6);
        var duel = new Duel(MathF.PI, rock);
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent boom = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(126f, boom.Position.Z, 0.01f);
    }

    [Fact]
    public void OfTwoTanksInLine_TheNearerTakesTheShell_WhateverTheirSlots()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(Duel.Spec, Duel.ShooterAt, 0f);
        int far = sim.AddTank(Duel.Spec, new Vector2(100, 85), MathF.PI);    // checked first
        int near = sim.AddTank(Duel.Spec, Duel.TargetAt, MathF.PI);
        var pose = Tank.Sim.Vehicles.TankPose.Compute(sim.Tanks[0], sim.Field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(new Vector3(100, 1.02f, 103.1f) - pose.Trunnion(Duel.Spec));
        var aim = new InputCmd(0, 0, 0, yaw, pitch, false);
        var hold = new InputCmd(0, 0, 0, MathF.PI, 0, false);
        var events = new List<GameEvent>();
        for (int i = 0; i < 80; i++)
        {
            sim.Step(new[] { i == 60 ? aim with { Fire = true } : aim, hold, hold });
            events.AddRange(sim.Events.ToArray());
        }
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(near, hit.TargetTankId);
        Assert.NotEqual(far, hit.TargetTankId);
    }

    [Fact]
    public void LastHitPoints_WreckTheTank()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        var events = duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        int hit = events.FindIndex(e => e.Type == GameEventType.TankHit);
        int destroyed = events.FindIndex(e => e.Type == GameEventType.TankDestroyed);
        Assert.True(hit >= 0 && destroyed > hit, "expected TankHit then TankDestroyed");
        GameEvent wreck = events[destroyed];
        Assert.Equal(duel.ShooterId, wreck.TankId);
        Assert.Equal(duel.TargetId, wreck.TargetTankId);
        Assert.Equal(100f, wreck.Position.Z, 1e-3f);
        Assert.Equal(0, duel.Target.Health);
        Assert.True(duel.Target.IsDestroyed);
    }

    [Fact]
    public void AWreck_IgnoresInput_StillStopsShells_AndTakesNoDamage()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Fire(aim);
        Assert.True(duel.Target.IsDestroyed);

        // Its driver floors it and fires: nothing happens.
        var wreckInput = new InputCmd(0, 1, 1, 0, 0, true);
        var events = duel.Run(aim, 200, wreckInput);   // past the shooter's reload too
        Assert.Equal(new Vector2(100, 100), duel.Target.Position);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired && e.TankId == duel.TargetId);

        // A second shell stops on the wreck: a puff, no hit, no damage.
        events = duel.Fire(aim);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        GameEvent puff = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.Equal(103.1f, puff.Position.Z, 0.01f);
        Assert.Equal(0, duel.Target.Health);
    }

    [Fact]
    public void AWreck_StillBlocksOtherTanks()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));
        duel.Run(new InputCmd(0, 1, 0, 0, 0, false), 300);   // the shooter drives north into it
        Assert.True(duel.Shooter.Position.Y > 100 + Duel.Spec.HullLength - 0.01f, $"drove into the wreck: {duel.Shooter.Position}");
    }

    [Fact]
    public void Revive_RestoresTheTank_AndTheNextStepReportsIt()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));

        duel.Sim.Revive(duel.TargetId);
        Assert.Equal(900, duel.Target.Health);
        Assert.False(duel.Target.IsDestroyed);
        Assert.Equal(0f, duel.Target.Reload);
        Assert.Equal(0f, duel.Target.Bloom);

        var events = duel.Run(default, 1, new InputCmd(0, 1, 0, MathF.PI, 0, false));
        GameEvent revived = Assert.Single(events, e => e.Type == GameEventType.TankRevived);
        Assert.Equal(duel.TargetId, revived.TargetTankId);
        Assert.Equal(duel.Sim.Tick, revived.Tick);
        duel.Run(default, 30, new InputCmd(0, 1, 0, MathF.PI, 0, false));
        Assert.True(duel.Target.Position.Y > 100.5f, "a revived tank drives again");
    }

    [Fact]
    public void Revive_OnALiveTank_JustTopsItUp()
    {
        var duel = new Duel(MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 500;
        duel.Sim.Revive(duel.TargetId);
        Assert.Equal(900, duel.Target.Health);
        Assert.False(duel.Target.IsDestroyed);
    }

    [Fact]
    public void Revive_RejectsAnUnknownTank()
    {
        var duel = new Duel(MathF.PI);
        Assert.Throws<ArgumentException>(() => duel.Sim.Revive(99));
    }

    [Fact]
    public void SameShot_SameResult()
    {
        GameEvent Shoot()
        {
            var duel = new Duel(MathF.PI);
            return Assert.Single(duel.Fire(duel.AimAt(duel.OnTarget(HullFront))), e => e.Type == GameEventType.TankHit);
        }
        Assert.Equal(Shoot(), Shoot());
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`Simulation.Revive` 不存在；先把 `Revive` 签名加上的话，命中相关测试应当失败（炮弹还会直接穿过坦克）。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Simulation.Shells.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim;

/// <summary>Shell flight and everything a shell can do when it lands: strike armour, burst, splash, wreck a tank.</summary>
public sealed partial class Simulation
{
    private readonly OrientedBox[] _hullBoxes = new OrientedBox[MaxTanks];
    private readonly OrientedBox[] _turretBoxes = new OrientedBox[MaxTanks];

    /// <summary>
    /// Flies every shell one tick. Its path this tick stops at whichever comes first: the ground or a rock, or a tank's
    /// hull or turret box (never the shooter's own). Shells that leave the map or time out vanish without an event.
    /// </summary>
    private void StepShells()
    {
        if (ShellCount == 0) return;
        SnapshotHitBoxes();
        for (int i = ShellCount - 1; i >= 0; i--)
        {
            ref Shell shell = ref _shells[i];
            Vector3 from = shell.Position;
            ShellStep step = Ballistics.Step(ref shell, Field, out RayHit rayHit);
            float groundT = step == ShellStep.Hit ? rayHit.T : 1f;
            if (FindTankHit(shell.OwnerTankId, from, shell.Position, groundT, out int slot, out ArmorHit hit))
                StrikeTank(shell, slot, hit, shell.Position - from);
            else if (step == ShellStep.Hit)
                Explode(shell, rayHit.Point, directSlot: -1);
            else if (step == ShellStep.Flying)
                continue;
            _shells[i] = _shells[--ShellCount];
        }
    }

    /// <summary>Tanks don't move while shells fly, so their boxes are worked out once per tick.</summary>
    private void SnapshotHitBoxes()
    {
        for (int j = 0; j < TankCount; j++)
        {
            ref readonly TankState t = ref _tanks[j];
            TankPose pose = TankPose.Compute(t, Field.Terrain);
            _hullBoxes[j] = pose.HullBox(t.Spec);
            _turretBoxes[j] = pose.TurretBox(t.Spec, t.TurretYaw);
        }
    }

    /// <summary>The nearest armour box the segment enters before <paramref name="maxT"/>, skipping the shooter.</summary>
    private bool FindTankHit(int ownerId, Vector3 from, Vector3 to, float maxT, out int slot, out ArmorHit hit)
    {
        slot = -1;
        hit = default;
        Vector3 middle = (from + to) * 0.5f;
        float halfLength = Vector3.Distance(from, to) * 0.5f;
        for (int j = 0; j < TankCount; j++)
        {
            if (_tanks[j].Id == ownerId) continue;
            ref readonly OrientedBox hull = ref _hullBoxes[j];
            ref readonly OrientedBox turret = ref _turretBoxes[j];
            float reach = MathF.Max(hull.BoundingRadius, Vector3.Distance(hull.Center, turret.Center) + turret.BoundingRadius);
            if (Vector3.Distance(hull.Center, middle) > halfLength + reach) continue;   // nowhere near this tank
            if (!ArmorHit.Find(_tanks[j].Spec, hull, turret, from, to, maxT, out ArmorHit candidate)) continue;
            (slot, hit, maxT) = (j, candidate, candidate.T);
        }
        return slot >= 0;
    }

    /// <summary>
    /// A shell meets a tank's armour. A live tank takes the rolled result (AP may bounce; HE never does, and bursts on the
    /// plate). A wreck just stops the shell: AP is spent, HE still bursts and splashes the tanks around it.
    /// </summary>
    private void StrikeTank(in Shell shell, int slot, in ArmorHit hit, Vector3 direction)
    {
        ref TankState target = ref _tanks[slot];
        if (target.IsDestroyed)
        {
            Explode(shell, hit.Point, slot);
            return;
        }
        float angle = Penetration.ImpactAngle(direction, hit.Normal);
        Impact impact = shell.Ammo == AmmoType.HE
            ? Penetration.HeDirect(shell.Round, hit.Thickness, angle, shell.Seed)
            : Penetration.Ap(shell.Round, shell.Caliber, hit.Thickness, angle, shell.Seed);
        _events.Add(new GameEvent(GameEventType.TankHit, Tick, shell.OwnerTankId, shell.Id, hit.Point, hit.Normal,
            target.Id, impact.Result, impact.Damage, shell.Ammo));
        Damage(ref target, impact.Damage, shell.OwnerTankId, shell.Id);
        if (shell.Ammo == AmmoType.HE) Explode(shell, hit.Point, slot);
    }

    /// <summary>
    /// The shell bursts at <paramref name="at"/>. A round with a splash radius (HE) hurts every other live tank in range,
    /// except the shooter and <paramref name="directSlot"/> (the tank it struck directly, already dealt with).
    /// </summary>
    private void Explode(in Shell shell, Vector3 at, int directSlot)
    {
        _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, at, Vector3.Zero, Ammo: shell.Ammo));
        if (shell.Round.SplashRadius <= 0) return;
        for (int j = 0; j < TankCount; j++)
        {
            ref TankState target = ref _tanks[j];
            if (j == directSlot || target.Id == shell.OwnerTankId || target.IsDestroyed) continue;
            float distance = TankPose.Compute(target, Field.Terrain).HullBox(target.Spec).DistanceTo(at);
            int damage = Penetration.Splash(shell.Round, distance, target.Spec.HullArmor.Armor.Thinnest);
            if (damage <= 0) continue;
            _events.Add(new GameEvent(GameEventType.SplashDamage, Tick, shell.OwnerTankId, shell.Id, at, Vector3.Zero,
                target.Id, HitResult.None, damage, shell.Ammo));
            Damage(ref target, damage, shell.OwnerTankId, shell.Id);
        }
    }

    /// <summary>Takes <paramref name="damage"/> off a live tank. At zero it becomes a wreck and stops where it is.</summary>
    private void Damage(ref TankState target, int damage, int attackerId, int shellId)
    {
        if (damage <= 0 || target.IsDestroyed) return;
        target.Health -= damage;
        if (target.Health > 0) return;
        target.Health = 0;
        target.IsDestroyed = true;
        target.Speed = 0f;
        Vector3 wreck = TankPose.Compute(target, Field.Terrain).Position;
        _events.Add(new GameEvent(GameEventType.TankDestroyed, Tick, attackerId, shellId, wreck, Vector3.Zero, TargetTankId: target.Id));
    }
}
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index a345a23..5811751 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -12,7 +12,7 @@ namespace Tank.Sim;
 /// drives, aims, updates its aim circle and fires (in slot order) against that snapshot, then every shell flies and
 /// resolves its hits.
 /// </summary>
-public sealed class Simulation
+public sealed partial class Simulation
 {
     public const int MaxTanks = 32;
     public const int MaxShells = 256;
@@ -22,6 +22,7 @@ public sealed class Simulation
     private readonly Obb2[] _footprints = new Obb2[MaxTanks];
     private readonly Obb2[] _others = new Obb2[MaxTanks];
     private readonly List<GameEvent> _events = new(16);
+    private readonly List<GameEvent> _pending = new(4);   // raised between steps (Revive); reported by the next Step
     /// <summary>Float drift in the countdown must not cost an extra tick of reload.</summary>
     private const float ReloadEpsilon = 1e-4f;
     private int _nextId = 1;
@@ -64,10 +65,13 @@ public sealed class Simulation
             throw new ArgumentException($"Expected {TankCount} inputs, got {inputs.Length}.", nameof(inputs));
         _events.Clear();
         Tick++;
+        foreach (GameEvent e in _pending) _events.Add(e with { Tick = Tick });
+        _pending.Clear();
         SnapshotFootprints();
         for (int i = 0; i < TankCount; i++)
         {
             ref TankState tank = ref _tanks[i];
+            if (tank.IsDestroyed) continue;   // wrecks don't drive, aim or fire, but still block (see SnapshotFootprints)
             InputCmd input = Sanitize(inputs[i], tank);
             float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
             TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i));
@@ -78,6 +82,22 @@ public sealed class Simulation
         StepShells();
     }
 
+    /// <summary>
+    /// Brings a wreck back where it stands: full health, aim circle closed, gun loaded (sandbox only for now). The
+    /// <see cref="GameEventType.TankRevived"/> event is reported by the next <see cref="Step"/>.
+    /// </summary>
+    public void Revive(int tankId)
+    {
+        ref TankState tank = ref TankRef(tankId);
+        tank.Health = tank.Spec.MaxHealth;
+        tank.IsDestroyed = false;
+        tank.Bloom = 0f;
+        tank.Reload = 0f;
+        tank.Speed = 0f;
+        Vector3 at = TankPose.Compute(tank, Field.Terrain).Position;
+        _pending.Add(new GameEvent(GameEventType.TankRevived, Tick, tankId, 0, at, Vector3.Zero, TargetTankId: tankId));
+    }
+
     /// <summary>Refills both ammunition racks (sandbox only for now).</summary>
     public void Restock(int tankId)
     {
@@ -175,22 +195,9 @@ public sealed class Simulation
         // Barrel pushed into a rock or the ground: the shell bursts where the barrel meets it instead of spawning inside.
         if (launch.Blocked)
         {
-            _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, tank.Id, shell.Id, launch.BlockPoint, Vector3.Zero, Ammo: shell.Ammo));
+            Explode(shell, launch.BlockPoint, directSlot: -1);
             return;
         }
         _shells[ShellCount++] = shell;
     }
-
-    private void StepShells()
-    {
-        for (int i = ShellCount - 1; i >= 0; i--)
-        {
-            ref Shell shell = ref _shells[i];
-            ShellStep step = Ballistics.Step(ref shell, Field, out RayHit rayHit);
-            if (step == ShellStep.Flying) continue;
-            if (step == ShellStep.Hit)
-                _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, rayHit.Point, Vector3.Zero, Ammo: shell.Ammo));
-            _shells[i] = _shells[--ShellCount];
-        }
-    }
 }
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"; dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Error\(s\)"`
Expected: `通过: 221`；客户端 `0 Error(s)`。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): shells strike tank armour, HE bursts and splashes, tanks are wrecked and revived"
```

---

### Task 8：试驾场靶车位置

设计 §4.1 的 6 个靶车位置写进 `SandboxMap.TargetSpots`，朝向按“相对出生点转多少度”给出。测试逐条校验设计的要求：距离、平地（车体四角和中心坡度 < 8°）、在可行驶区内、不压石头和草丛、离出生点到大山的通道足够远、彼此不重叠、朝向对、从出生点看得见（5 号半躲在石头后）、彼此不挡视线。

**Files:**
- Create: `src/Tank.Sim/Maps/TargetSpot.cs`
- Modify: `src/Tank.Sim/Maps/SandboxMap.cs`（另把 `BigHill`、`LaneHalfWidth`、`DistanceToSegment` 改为 `internal`）
- Test: `tests/Tank.Sim.Tests/TargetSpotTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/TargetSpotTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class TargetSpotTests
{
    private const int RockSpot = 4, FarSpot = 5;
    private const float GunHeight = 2.1f;   // the spawn tank's trunnion
    private const float PlateHeight = 1.0f;
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly IReadOnlyList<TargetSpot> Spots = SandboxMap.TargetSpots;

    public static TheoryData<int> Indices() => new() { 0, 1, 2, 3, 4, 5 };

    private static Obb2 Hull(TargetSpot s) => TankMotion.Footprint(TankCatalog.Medium, s.Position, s.Yaw);

    private static IEnumerable<Vector2> Corners(Obb2 b)
    {
        foreach (int x in new[] { -1, 1 })
        foreach (int z in new[] { -1, 1 })
            yield return b.Center + b.AxisRight * (x * b.HalfExtents.X) + b.AxisForward * (z * b.HalfExtents.Y);
    }

    [Fact]
    public void ThereAreSix() => Assert.Equal(6, Spots.Count);

    [Theory]
    [MemberData(nameof(Indices))]
    public void StandsAtTheRightRange(int i)
    {
        float distance = Vector2.Distance(Spots[i].Position, SandboxMap.Spawn);
        if (i == FarSpot) Assert.InRange(distance, 180f, 200f);
        else Assert.InRange(distance, 30f, 200f);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void StandsOnFlatPlayableGround(int i)
    {
        Obb2 hull = Hull(Spots[i]);
        foreach (Vector2 p in Corners(hull).Append(hull.Center))
        {
            Assert.Equal(p, Field.ClampToPlayable(p));
            float slope = MathF.Acos(Field.Terrain.NormalAt(p.X, p.Y).Y);
            Assert.True(slope < Angles.Deg(8), $"spot {i}: {slope * 180 / MathF.PI:F1}° at {p}");
        }
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void IsClearOfRocksBushesAndTheLane(int i)
    {
        Obb2 hull = Hull(Spots[i]);
        foreach (Prop p in Field.Props)
        {
            if (p.Kind == PropKind.Bush) Assert.False(hull.IntersectsCircle(p.Footprint.Center, p.Footprint.HalfExtents.X), $"spot {i} in bush {p.Id}");
            else Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _), $"spot {i} on rock {p.Id}");
        }
        float lane = SandboxMap.DistanceToSegment(hull.Center, SandboxMap.Spawn, SandboxMap.BigHill) - hull.HalfExtents.Length();
        Assert.True(lane >= SandboxMap.LaneHalfWidth, $"spot {i} is {lane:F1} m from the lane");
        Obb2 spawn = TankMotion.Footprint(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        for (int j = 0; j < Spots.Count; j++)
            if (j != i) Assert.False(Obb2.TryGetPenetration(hull, Hull(Spots[j]), out _), $"spots {i} and {j} overlap");
        Assert.False(Obb2.TryGetPenetration(hull, spawn, out _));
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 90f)]
    [InlineData(2, 45f)]
    [InlineData(3, 180f)]
    [InlineData(4, 0f)]
    [InlineData(5, 0f)]
    public void ShowsTheSpawnTheRightFace(int i, float degreesOffFront)
    {
        Vector2 toSpawn = Vector2.Normalize(SandboxMap.Spawn - Spots[i].Position);
        float off = MathF.Acos(Math.Clamp(Vector2.Dot(Angles.Forward(Spots[i].Yaw), toSpawn), -1f, 1f));
        Assert.Equal(Angles.Deg(degreesOffFront), off, 0.01f);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void CanBeSeenFromTheSpawn_ExceptTheRockOneIsHalfHidden(int i)
    {
        int clear = 0, rock = 0;
        foreach (RayHit? hit in SightLines(Spots[i]))
        {
            if (hit is null) clear++;
            else if (hit.Value.PropId != RayHit.TerrainId) rock++;
        }
        if (i == RockSpot)
        {
            Assert.True(clear >= 1 && rock >= 1, $"rock spot: {clear} clear, {rock} behind a rock");
        }
        else Assert.Equal(3, clear);
    }

    [Theory]
    [MemberData(nameof(Indices))]
    public void NoOtherTargetStandsInTheWay(int i)
    {
        Vector3 eye = Eye();
        Vector3 aim = Plate(Spots[i].Position);
        for (int j = 0; j < Spots.Count; j++)
            if (j != i) Assert.False(Intersect.SegmentPrism(Hull(Spots[j]), -10, 10, eye, aim, out _), $"spot {j} blocks spot {i}");
    }

    /// <summary>Lines from the spawn gun to the left end, middle and right end of the hull as seen from the spawn; null when clear.</summary>
    private static IEnumerable<RayHit?> SightLines(TargetSpot spot)
    {
        Vector2 toSpawn = Vector2.Normalize(SandboxMap.Spawn - spot.Position);
        Vector2 across = new(-toSpawn.Y, toSpawn.X);
        foreach (float side in new[] { -1.6f, 0f, 1.6f })
            yield return Field.Raycast(Eye(), Plate(spot.Position + across * side), out RayHit hit) ? hit : null;
    }

    private static Vector3 Eye() => new(SandboxMap.Spawn.X, Field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y) + GunHeight, SandboxMap.Spawn.Y);

    private static Vector3 Plate(Vector2 p) => new(p.X, Field.Terrain.HeightAt(p.X, p.Y) + PlateHeight, p.Y);
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败，`SandboxMap.TargetSpots`、`TargetSpot` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/SandboxMap.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/SandboxMap.cs b/src/Tank.Sim/Maps/SandboxMap.cs
index 4694a0e..9db7c92 100644
--- a/src/Tank.Sim/Maps/SandboxMap.cs
+++ b/src/Tank.Sim/Maps/SandboxMap.cs
@@ -5,8 +5,8 @@ using Tank.Sim.Terrain;
 namespace Tank.Sim.Maps;
 
 /// <summary>
-/// 400m × 400m test ground: rolling noise, four hills, 80-odd big rocks and twelve wide patches of tall bushes. The spawn
-/// faces the big hill (north); the nearest bush patch sits beside that lane about 40 m out.
+/// 400m × 400m test ground: rolling noise, four hills, 80-odd big rocks, twelve wide patches of tall bushes and six
+/// practice targets. The spawn faces the big hill (north); the nearest bush patch sits beside that lane about 40 m out.
 /// </summary>
 public static class SandboxMap
 {
@@ -28,8 +28,9 @@ public static class SandboxMap
     /// <summary>Faces the big hill to the north.</summary>
     public const float SpawnYaw = 0f;
 
-    private const float LaneHalfWidth = 6f;
-    private static readonly Vector2 BigHill = new(200, 150);
+    /// <summary>Rocks, bushes and targets keep this far from the straight lane between the spawn and the big hill.</summary>
+    internal const float LaneHalfWidth = 6f;
+    internal static readonly Vector2 BigHill = new(200, 150);
 
     /// <summary>
     /// Centres of the bush patches, hand-placed on flat ground (under 10° within 13 m), at least 45 m apart and spread over
@@ -56,6 +57,21 @@ public static class SandboxMap
     private const float BushOverlap = 0.35f;      // neighbours may overlap: centres at least this × the sum of radii apart
     private const int BushAttemptsPerPatch = 200;
 
+    /// <summary>
+    /// Six practice targets, hand-placed on flat open ground 30–200 m from the spawn, clear of rocks, bushes and the
+    /// spawn-to-hill lane. Seen from the spawn they show, in order: the front, the side, a 45° angle, the rear, a front
+    /// half hidden behind a rock, and a front far away (180–200 m).
+    /// </summary>
+    public static readonly IReadOnlyList<TargetSpot> TargetSpots = new[]
+    {
+        Facing(new Vector2(160, 304), turnDegrees: 0),
+        Facing(new Vector2(248, 300), turnDegrees: 90),
+        Facing(new Vector2(216, 244), turnDegrees: 45),
+        Facing(new Vector2(140, 256), turnDegrees: 180),
+        Facing(new Vector2(280, 328), turnDegrees: 0),
+        Facing(new Vector2(380, 272), turnDegrees: 0),
+    };
+
     private static readonly TerrainGenerator.Hill[] Hills =
     {
         new(BigHill.X, BigHill.Y, 70, 22),
@@ -174,7 +190,14 @@ public static class SandboxMap
         return true;
     }
 
-    private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
+    /// <summary>A target whose hull is turned <paramref name="turnDegrees"/> to its left from facing the spawn.</summary>
+    private static TargetSpot Facing(Vector2 position, float turnDegrees)
+    {
+        Vector2 toSpawn = Spawn - position;
+        return new TargetSpot(position, Angles.Wrap(MathF.Atan2(-toSpawn.X, -toSpawn.Y) + Angles.Deg(turnDegrees)));
+    }
+
+    internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
     {
         Vector2 ab = b - a;
         float t = System.Math.Clamp(Vector2.Dot(p - a, ab) / ab.LengthSquared(), 0f, 1f);
PATCH
```

新建 `src/Tank.Sim/Maps/TargetSpot.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Maps;

/// <summary>Where a practice target stands: ground position (X is world X, Y is world Z) and hull yaw.</summary>
public readonly record struct TargetSpot(Vector2 Position, float Yaw);
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "通过|失败"`
Expected: `通过: 258`。

- [ ] **Step 5：提交，并结束批次 B**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): six practice target spots in the sandbox map"
```

批次 B 结束：规格审查 → 代码质量审查 → `docs/PROGRESS.md` 记一行（Sim 258）。

---

### Task 9：靶车涂装、焦黑、炮塔飞起、黑烟

- 调色板加设计给的 `TargetBody`、`Charred`，以及界面和特效要用的 4 种颜色；`PaletteTests` 校验两种设计色。
- `tank_toon.gdshader` 加 `char_amount`，把整车颜色混向焦黑色（`char_color` 默认值标了 `// Palette.Charred`，`PaletteTests` 会检查它与调色板一致）。
- `TurretToss`（不依赖 Godot）：炮塔从原位飞起约 4 m，转 1.25 圈，1.1 秒后倾斜着落在车体右侧，圆顶边缘贴地。
- `TankModel`：`Build` 可传入涂装；炮塔节点下加一个支点 `Toss`（在圆顶中间），完好时它刚好抵消自身高度，所以外观不变；`Wreck(炮塔转角)` 之后每帧按 `TurretToss` 摆放炮塔，焦黑在 0.6 秒内渐满；`Restore()` 复原。
- `WreckSmoke`：残骸上一股持续冒的深色卡通烟团。
- `BattleFx.Explosion` 加 `size` 参数（火球、烟团大小和速度随之缩放）。

**Files:**
- Create: `src/Tank.Client/scripts/Vehicles/TurretToss.cs`、`src/Tank.Client/scripts/Combat/WreckSmoke.cs`
- Modify: `src/Tank.Client/scripts/Art/Palette.cs`、`src/Tank.Client/shaders/tank_toon.gdshader`、`src/Tank.Client/scripts/Vehicles/TankModel.cs`、`src/Tank.Client/scripts/Combat/BattleFx.cs`
- Test: 新建 `tests/Tank.Art.Tests/TurretTossTests.cs`；改 `PaletteTests.cs`、`Tank.Art.Tests.csproj`（链接 `TurretToss.cs`）

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/PaletteTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/PaletteTests.cs b/tests/Tank.Art.Tests/PaletteTests.cs
index feb719d..a8c38f1 100644
--- a/tests/Tank.Art.Tests/PaletteTests.cs
+++ b/tests/Tank.Art.Tests/PaletteTests.cs
@@ -38,6 +38,8 @@ public class PaletteTests
         Assert.Equal(Palette.Hex("1E2333"), Palette.UiPanel);
         Assert.Equal(0.7f, Palette.UiPanelAlpha);
         Assert.Equal(Palette.Hex("FFC93C"), Palette.UiAccent);
+        Assert.Equal(Palette.Hex("6F8FB5"), Palette.TargetBody);   // stage 2A design §4.2
+        Assert.Equal(Palette.Hex("2B2622"), Palette.Charred);
     }
 
     [Fact]
PATCH
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index 3d46a8f..01123ab 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -15,7 +15,7 @@
     <!-- The palette is engine-neutral too, so look data and vertex colours can use it. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
-    <Compile Include="..\..\src\Tank.Client\scripts\Vehicles\TankLook.cs;..\..\src\Tank.Client\scripts\Vehicles\TankLooks.cs;..\..\src\Tank.Client\scripts\Vehicles\TankParts.cs;..\..\src\Tank.Client\scripts\Vehicles\TankAnimator.cs" LinkBase="Vehicles" />
+    <Compile Include="..\..\src\Tank.Client\scripts\Vehicles\TankLook.cs;..\..\src\Tank.Client\scripts\Vehicles\TankLooks.cs;..\..\src\Tank.Client\scripts\Vehicles\TankParts.cs;..\..\src\Tank.Client\scripts\Vehicles\TankAnimator.cs;..\..\src\Tank.Client\scripts\Vehicles\TurretToss.cs" LinkBase="Vehicles" />
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\..\src\Tank.Sim\Tank.Sim.csproj" />
PATCH
```

新建 `tests/Tank.Art.Tests/TurretTossTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Vehicles;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class TurretTossTests
{
    private static readonly TankLook Look = TankLooks.Medium;
    private static readonly TurretToss Toss = new(Look, TankCatalog.Medium.TurretOffset);

    [Fact]
    public void Starts_where_the_intact_turret_is()
    {
        var (position, rotation) = Toss.At(0f, startYaw: 0.7f);
        Assert.Equal(Toss.Pivot, position);
        Assert.Equal(new Vector3(0, Look.TurretBase + TurretToss.PivotAboveRing, -TankCatalog.Medium.TurretOffset), position);
        Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
        Assert.True(MathF.Abs(Quaternion.Dot(expected, rotation)) > 0.9999f, $"rotation {rotation}");
    }

    [Fact]
    public void Flies_about_four_metres_up()
    {
        float highest = 0f;
        for (int i = 0; i <= 110; i++) highest = MathF.Max(highest, Toss.At(i / 100f, 0f).Position.Y - Toss.Pivot.Y);
        Assert.InRange(highest, TurretToss.Apex - 0.4f, TurretToss.Apex + 0.4f);
    }

    [Fact]
    public void Lands_beside_the_hull_and_stays()
    {
        var landed = Toss.At(TurretToss.FlightTime, 0.3f);
        Assert.Equal(Toss.Landing, landed.Position);
        Assert.Equal(landed, Toss.At(TurretToss.FlightTime + 5f, 0.3f));
        Assert.True(Toss.Landing.X - Toss.DomeRadius >= Look.UpperHullWidth / 2, "the landed dome overlaps the hull");
    }

    [Fact]
    public void Landed_dome_rests_on_the_ground()
    {
        var (position, rotation) = Toss.At(TurretToss.FlightTime, 1.1f);
        float lowest = position.Y + TurretToss.LowestRimPoint(rotation, Toss.DomeRadius);
        Assert.Equal(0f, lowest, 0.02f);
    }

    [Fact]
    public void Spins_more_than_a_full_turn()
    {
        // Sample the forward direction's heading; it must sweep past a full circle.
        float swept = 0f, last = 0f;
        for (int i = 1; i <= 100; i++)
        {
            Vector3 forward = Vector3.Transform(-Vector3.UnitZ, Toss.At(TurretToss.FlightTime * i / 100f, 0f).Rotation);
            float heading = MathF.Atan2(-forward.X, -forward.Z);
            float step = heading - last;
            if (step > MathF.PI) step -= MathF.Tau;
            if (step < -MathF.PI) step += MathF.Tau;
            swept += step;
            last = heading;
        }
        Assert.True(MathF.Abs(swept) > MathF.Tau, $"swept only {swept} rad");
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败（项目文件链接的 `TurretToss.cs` 不存在，或 `Palette.TargetBody` 找不到）。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Art/Palette.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Art/Palette.cs b/src/Tank.Client/scripts/Art/Palette.cs
index b6ba39c..0a6dbf4 100644
--- a/src/Tank.Client/scripts/Art/Palette.cs
+++ b/src/Tank.Client/scripts/Art/Palette.cs
@@ -27,6 +27,8 @@ public static class Palette
     public static readonly Vector3 UiPanel = Hex("1E2333");
     public const float UiPanelAlpha = 0.7f;
     public static readonly Vector3 UiAccent = Hex("FFC93C");
+    public static readonly Vector3 TargetBody = Hex("6F8FB5");   // blue-grey practice targets (stage 2A design)
+    public static readonly Vector3 Charred = Hex("2B2622");      // a knocked-out tank's scorched paint
 
     // Not in the design table; picked to sit with the colours above.
     public static readonly Vector3 Leaves = Hex("6CC04A");      // broadleaf crowns
@@ -39,6 +41,10 @@ public static class Palette
     public static readonly Vector3 SoilDust = Hex("E3C79A");    // puffs from a shell on soil, track dust
     public static readonly Vector3 UiText = Hex("FFFFFF");
     public static readonly Vector3 UiOutline = Hex("141826");
+    public static readonly Vector3 HitPierced = Hex("FF4D4D");   // "击穿！", damage numbers, a low health bar
+    public static readonly Vector3 HitBlocked = Hex("B9BDC8");   // "未击穿" ("跳弹！" uses UiAccent)
+    public static readonly Vector3 Health = Hex("6CD45A");       // a healthy health bar
+    public static readonly Vector3 WreckSmoke = Hex("4A4650");   // dark smoke from a wreck
 
     /// <summary>"RRGGBB" (an optional leading '#') to sRGB 0–1.</summary>
     public static Vector3 Hex(string hex)
PATCH
```

修改 `src/Tank.Client/scripts/Combat/BattleFx.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Combat/BattleFx.cs b/src/Tank.Client/scripts/Combat/BattleFx.cs
index 1fb8713..a1271e7 100644
--- a/src/Tank.Client/scripts/Combat/BattleFx.cs
+++ b/src/Tank.Client/scripts/Combat/BattleFx.cs
@@ -58,9 +58,10 @@ public partial class BattleFx : Node3D
         Oldest(_muzzles).Play(muzzle, withSmoke);
     }
 
-    public void Explosion(Vector3 at, bool onRock)
+    /// <param name="size">1 for a shell burst; larger for a tank blowing up, smaller for a shell striking armour.</param>
+    public void Explosion(Vector3 at, bool onRock, float size = 1f)
     {
-        Oldest(_impacts).Play(at, onRock ? Palette.RockDust.ToColor() : Palette.Smoke.ToColor());
+        Oldest(_impacts).Play(at, onRock ? Palette.RockDust.ToColor() : Palette.Smoke.ToColor(), size);
     }
 
     private interface ISlot
@@ -190,6 +191,8 @@ public partial class BattleFx : Node3D
 
     private sealed class ImpactSlot : ISlot
     {
+        private const float PuffScaleMin = 1.1f, PuffScaleMax = 1.9f, PuffSpeedMax = 7f;
+
         private readonly Flash _fireball;
         private readonly GpuParticles3D _puffs;
         private readonly ParticleProcessMaterial _puffProcess;
@@ -205,12 +208,12 @@ public partial class BattleFx : Node3D
                 Direction = Vector3.Up,
                 Spread = 75f,
                 InitialVelocityMin = 3.5f,
-                InitialVelocityMax = 7f,
+                InitialVelocityMax = PuffSpeedMax,
                 DampingMin = 5f,
                 DampingMax = 7f,
                 Gravity = new Vector3(0, 0.4f, 0),
-                ScaleMin = 1.1f,
-                ScaleMax = 1.9f,
+                ScaleMin = PuffScaleMin,
+                ScaleMax = PuffScaleMax,
                 ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.2f, peakAt: 0.15f, shrinkFrom: 0.5f),
                 Color = Palette.Smoke.ToColor(),
             };
@@ -237,10 +240,13 @@ public partial class BattleFx : Node3D
 
         public void Advance(float dt) => _fireball.Advance(dt);
 
-        public void Play(Vector3 at, Color puffColor)
+        public void Play(Vector3 at, Color puffColor, float size)
         {
-            _fireball.Play(at + Vector3.Up * 0.6f, FireballSize, FireballGrowTime, FireballShrinkTime);
+            _fireball.Play(at + Vector3.Up * 0.6f * size, FireballSize * size, FireballGrowTime, FireballShrinkTime * MathF.Max(1f, size));
             _puffProcess.Color = puffColor;
+            _puffProcess.ScaleMin = PuffScaleMin * size;
+            _puffProcess.ScaleMax = PuffScaleMax * size;
+            _puffProcess.InitialVelocityMax = PuffSpeedMax * size;
             _puffs.GlobalPosition = at + Vector3.Up * 0.3f;
             _puffs.AmountRatio = (6 + GD.Randi() % 5) / (float)ImpactPuffs;
             _puffs.Restart();
PATCH
```

新建 `src/Tank.Client/scripts/Combat/WreckSmoke.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Combat;

/// <summary>
/// A steady column of dark cartoon puffs rising from a wreck: opaque toon balls that swell as they climb and shrink away
/// (like every other puff, no transparency). Attach to the tank model; puffs are emitted in world space.
/// </summary>
public partial class WreckSmoke : Node3D
{
    private GpuParticles3D _emitter = null!;

    /// <param name="at">Hull-space spot the smoke pours from (the open turret ring).</param>
    public void Build(Vector3 at)
    {
        _emitter = new GpuParticles3D
        {
            Amount = 14,
            Lifetime = 3.2,
            Emitting = false,
            LocalCoords = false,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
                EmissionSphereRadius = 0.6f,
                Direction = Vector3.Up,
                Spread = 12f,
                InitialVelocityMin = 1.2f,
                InitialVelocityMax = 2.0f,
                DampingMin = 0.2f,
                DampingMax = 0.4f,
                Gravity = new Vector3(0.25f, 0.5f, 0),   // a light breeze bends the column
                ScaleMin = 0.9f,
                ScaleMax = 1.5f,
                ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.6f, peakAt: 0.35f, shrinkFrom: 0.7f),
                Color = Palette.WreckSmoke.ToColor(),
            },
            DrawPass1 = ParticleKit.PuffBall,
            VisibilityAabb = ParticleKit.WideBounds,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = at,
        };
        AddChild(_emitter);
    }

    public bool Emitting
    {
        get => _emitter.Emitting;
        set => _emitter.Emitting = value;
    }
}
```

修改 `src/Tank.Client/scripts/Vehicles/TankModel.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankModel.cs b/src/Tank.Client/scripts/Vehicles/TankModel.cs
index 9af3f15..df1de9e 100644
--- a/src/Tank.Client/scripts/Vehicles/TankModel.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankModel.cs
@@ -9,21 +9,27 @@ namespace Tank.Client.Vehicles;
 /// <summary>
 /// Procedural cartoon tank built from its <see cref="TankLook"/>, painted with tank_toon.gdshader. Node layout keeps the stage-1 contract: the root sits at the
 /// hull origin on the ground, "Turret" yaws, "Gun" pitches, the barrel points −Z. Below that:
-/// Body (visual nod) → Hull; Turret (a sibling of Body, so nod never tilts the gun) → Gun → Mantlet, Barrel (recoil: tube and bulb), Muzzle; Running gear → Wheels, Tracks.
+/// Body (visual nod) → Hull; Turret (a sibling of Body, so nod never tilts the gun) → Toss (pivot in the dome, moved only
+/// when the tank is wrecked) → TurretBody → TurretShell, Gun → Mantlet, Barrel (recoil: tube and bulb), Muzzle;
+/// Running gear → Wheels, Tracks. A wreck chars black and throws its turret (<see cref="TurretToss"/>) until <see cref="Restore"/>.
 /// </summary>
 public partial class TankModel : Node3D
 {
     private const float RimStrength = 0.25f;
     private const int PaintMode = 0, WheelMode = 1, TrackMode = 2;   // tank_toon.gdshader "mode"
+    private const float CharSeconds = 0.6f;
 
     private readonly Node3D _body = new() { Name = "Body" };
     private readonly Node3D _turret = new() { Name = "Turret" };
+    private readonly Node3D _toss = new() { Name = "Toss" };
+    private readonly Node3D _turretBody = new() { Name = "TurretBody" };
     private readonly Node3D _gun = new() { Name = "Gun" };
     private readonly Node3D _barrel = new() { Name = "Barrel" };
     private readonly Marker3D _muzzle = new() { Name = "Muzzle" };
     private readonly Node3D _wheels = new() { Name = "Wheels" };
     private readonly MultiMeshInstance3D _tracks = new() { Name = "Tracks" };
     private readonly List<WheelNode> _wheelNodes = new();
+    private readonly List<ShaderMaterial> _materials = new();
     private readonly record struct WheelNode(Node3D Node, float Radius, bool Left, float Phase);
     private MultiMesh _linkMesh = null!;
     private float[] _linkBuffer = Array.Empty<float>();
@@ -34,43 +40,74 @@ public partial class TankModel : Node3D
     private int _linksPerSide;
     private Vector3 _nodPivot;
     private Vector3 _turretBase;
+    private TankPaint _paint;
+    private TurretToss _tossPath = null!;
+    private float _wreckTime = -1f;   // seconds since the kill; negative while intact
+    private float _tossYaw;
+    private float _charShown;
 
     public TankAnimator Animator { get; private set; } = null!;
 
     /// <summary>Barrel tip in world space, for muzzle effects.</summary>
     public Transform3D MuzzleTransform => _muzzle.GlobalTransform;
 
-    public void Build(TankSpec spec)
+    /// <param name="paint">Overrides the look's paint (the practice targets are blue-grey).</param>
+    public void Build(TankSpec spec, TankPaint? paint = null)
     {
         _look = TankLooks.For(spec.Id);
+        _paint = paint ?? _look.Paint;
         Animator = new TankAnimator(_look.TrackX);
+        _tossPath = new TurretToss(_look, spec.TurretOffset);
         _nodPivot = new Vector3(0, _look.RoadWheelY, 0);
-        ShaderMaterial paint = ToonMaterial(_look, PaintMode);
+        ShaderMaterial paintMaterial = ToonMaterial(PaintMode);
         // Same paint on the gun; its origin is the trunnion, so lift it by that height for the bottom-to-top shading.
-        ShaderMaterial gunPaint = ToonMaterial(_look, PaintMode).With("height_offset", spec.TrunnionHeight);
+        ShaderMaterial gunPaint = ToonMaterial(PaintMode).With("height_offset", spec.TrunnionHeight);
 
         AddChild(_body);
-        _body.AddChild(new MeshInstance3D { Name = "Hull", Mesh = TankParts.Hull(_look).ToArrayMesh(), MaterialOverride = paint });
+        _body.AddChild(new MeshInstance3D { Name = "Hull", Mesh = TankParts.Hull(_look).ToArrayMesh(), MaterialOverride = paintMaterial });
 
         _turretBase = new Vector3(0, 0, -spec.TurretOffset);
         _turret.Position = _turretBase;
         AddChild(_turret); // sibling of Body: the hull nods under it, but the gun line stays exactly the sim's
-        _turret.AddChild(new MeshInstance3D { Name = "TurretShell", Mesh = TankParts.Turret(_look).ToArrayMesh(), MaterialOverride = paint });
+        // The toss pivot sits in the dome; TurretBody undoes its height, so intact the turret is exactly where it was.
+        float pivotHeight = _tossPath.Pivot.Y;
+        _toss.Position = new Vector3(0, pivotHeight, 0);
+        _turret.AddChild(_toss);
+        _turretBody.Position = new Vector3(0, -pivotHeight, 0);
+        _toss.AddChild(_turretBody);
+        _turretBody.AddChild(new MeshInstance3D { Name = "TurretShell", Mesh = TankParts.Turret(_look).ToArrayMesh(), MaterialOverride = paintMaterial });
 
         _gun.Position = new Vector3(0, spec.TrunnionHeight, _look.GunPivotZ);
-        _turret.AddChild(_gun);
+        _turretBody.AddChild(_gun);
         _gun.AddChild(new MeshInstance3D { Name = "Mantlet", Mesh = TankParts.Mantlet(_look).ToArrayMesh(), MaterialOverride = gunPaint });
         _gun.AddChild(_barrel);
         float barrelLength = spec.GunLength + _look.GunPivotZ;
         _barrel.AddChild(new MeshInstance3D { Mesh = TankParts.Barrel(_look, barrelLength).ToArrayMesh(), MaterialOverride = gunPaint });
-        _barrel.AddChild(new MeshInstance3D { Name = "MuzzleBulb", Mesh = TankParts.Muzzle(_look, barrelLength).ToArrayMesh(), MaterialOverride = ToonMaterial(_look, TrackMode) });
+        _barrel.AddChild(new MeshInstance3D { Name = "MuzzleBulb", Mesh = TankParts.Muzzle(_look, barrelLength).ToArrayMesh(), MaterialOverride = ToonMaterial(TrackMode) });
         _muzzle.Position = new Vector3(0, 0, -barrelLength);
         _gun.AddChild(_muzzle);
 
         AddChild(_wheels);
         BuildWheels();
         AddChild(_tracks);
-        BuildTracks(ToonMaterial(_look, TrackMode));
+        BuildTracks(ToonMaterial(TrackMode));
+    }
+
+    public bool IsWrecked => _wreckTime >= 0f;
+
+    /// <summary>Knocks the tank out: the turret flies off and the paint chars black over <see cref="CharSeconds"/>.</summary>
+    public void Wreck(float turretYaw)
+    {
+        _wreckTime = 0f;
+        _tossYaw = turretYaw;
+    }
+
+    /// <summary>Puts the turret back and the paint back to normal (a revived tank).</summary>
+    public void Restore()
+    {
+        _wreckTime = -1f;
+        _toss.Transform = new Transform3D(Basis.Identity, new Vector3(0, _tossPath.Pivot.Y, 0));
+        SetChar(0f);
     }
 
     public void Apply(TankPose pose, float turretYaw, float gunPitch)
@@ -83,9 +120,14 @@ public partial class TankModel : Node3D
     /// <summary>Kicks the barrel back; it returns over <see cref="TankAnimator.RecoilReturnTime"/>.</summary>
     public void Fire(float turretYaw) => Animator.Fire(turretYaw);
 
-    /// <summary>Rolls wheels and tracks, nods the hull and runs recoil. Call once per frame after <see cref="Apply"/>.</summary>
+    /// <summary>Rolls wheels and tracks, nods the hull, runs recoil and the wreck. Call once per frame after <see cref="Apply"/>.</summary>
     public void Animate(float speed, float yawRate, float delta)
     {
+        if (IsWrecked)
+        {
+            AnimateWreck(delta);
+            return;
+        }
         Animator.Step(speed, yawRate, delta);
         // The odometer grows without bound; wrap the wheel angle (2π) and the belt offset (belt length) separately, each on its own period.
         foreach (WheelNode wheel in _wheelNodes)
@@ -102,18 +144,39 @@ public partial class TankModel : Node3D
         _barrel.Position = new Vector3(0, 0, Animator.Recoil);
     }
 
-    private static ShaderMaterial ToonMaterial(TankLook look, int mode) =>
-        Materials.FromShader("tank_toon")
+    /// <summary>A wreck doesn't roll or nod: the turret follows its toss path (in hull space) and the paint chars.</summary>
+    private void AnimateWreck(float delta)
+    {
+        _wreckTime += delta;
+        var (position, rotation) = _tossPath.At(_wreckTime, _tossYaw);
+        _turret.Transform = Transform3D.Identity;   // the toss path already includes the turret's yaw and ring offset
+        _toss.Transform = new Transform3D(new Basis(rotation.ToGodot()), position.ToGodot());
+        SetChar(Mathf.Clamp(_wreckTime / CharSeconds, 0f, 1f));
+    }
+
+    private void SetChar(float amount)
+    {
+        if (amount == _charShown) return;
+        _charShown = amount;
+        foreach (ShaderMaterial material in _materials) material.SetShaderParameter("char_amount", amount);
+    }
+
+    private ShaderMaterial ToonMaterial(int mode)
+    {
+        ShaderMaterial material = Materials.FromShader("tank_toon")
             .With("mode", mode)
-            .With("body_color", look.Paint.Body.ToColor())
-            .With("hub_color", look.Paint.Hub.ToColor())
-            .With("track_color", look.Paint.Track.ToColor())
+            .With("body_color", _paint.Body.ToColor())
+            .With("hub_color", _paint.Hub.ToColor())
+            .With("track_color", _paint.Track.ToColor())
             .With("toon_rim_strength", RimStrength);
+        _materials.Add(material);
+        return material;
+    }
 
     private void BuildWheels()
     {
         float halfThickness = _look.TrackThickness / 2;
-        ShaderMaterial wheelMaterial = ToonMaterial(_look, WheelMode);   // cream hub, track-coloured tyre (vertex-colour mask)
+        ShaderMaterial wheelMaterial = ToonMaterial(WheelMode);   // cream hub, track-coloured tyre (vertex-colour mask)
         _belt = new BeltPath(TankParts.TrackLoop(_look));
         _linksPerSide = (int)MathF.Round(_belt.Length / _look.LinkPitch);
         int teeth = TankParts.SprocketTeethCount(_look);
PATCH
```

新建 `src/Tank.Client/scripts/Vehicles/TurretToss.cs`：

```csharp
using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>
/// A knocked-out tank's turret flight, presentation only (no collision): the dome and gun pop up about
/// <see cref="Apex"/> m, spinning, and land tilted on the ground beside the hull, where they stay until the tank is
/// revived. Everything is in hull space and describes the turret's pivot: the middle of the dome,
/// <see cref="PivotAboveRing"/> above the turret ring. Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public sealed class TurretToss
{
    public const float FlightTime = 1.1f;
    public const float Apex = 4f;                   // the flight peaks about this far above the turret's resting place
    public const float PivotAboveRing = 0.4f;
    private const float Lift = 4.5f;                 // parabola height that, with the drop to the ground, peaks near Apex
    private const float SpinTurns = 1.25f;
    private const float Clearance = 0.3f;            // gap between the landed dome and the hull side
    private const float LandBehind = 0.8f;           // lands a little behind the turret ring
    private static readonly float LandingRoll = 18f * MathF.PI / 180f;
    private static readonly float LandingPitch = 8f * MathF.PI / 180f;   // gun drooping towards the ground
    private static readonly float WobbleRoll = 25f * MathF.PI / 180f;

    /// <param name="turretOffset">Turret ring centre ahead of the hull origin (<c>TankSpec.TurretOffset</c>).</param>
    public TurretToss(TankLook look, float turretOffset)
    {
        DomeRadius = look.TurretProfile.Max(p => p.X);
        Pivot = new SN.Vector3(0, look.TurretBase + PivotAboveRing, -turretOffset);
        float rest = -LowestRimPoint(Landed(0f), DomeRadius);
        Landing = new SN.Vector3(look.UpperHullWidth / 2 + DomeRadius + Clearance, rest, -turretOffset + LandBehind);
    }

    public float DomeRadius { get; }

    /// <summary>The pivot's place on an intact tank.</summary>
    public SN.Vector3 Pivot { get; }

    /// <summary>The pivot's place once landed: beside the right side of the hull, the tilted dome's rim on the ground.</summary>
    public SN.Vector3 Landing { get; }

    /// <summary>Pivot position and turret orientation <paramref name="time"/> s after the kill.</summary>
    /// <param name="startYaw">Turret yaw (hull-relative) at the moment of the kill.</param>
    public (SN.Vector3 Position, SN.Quaternion Rotation) At(float time, float startYaw)
    {
        float s = Math.Clamp(time / FlightTime, 0f, 1f);
        SN.Vector3 along = SN.Vector3.Lerp(Pivot, Landing, s);
        float height = Pivot.Y + (Landing.Y - Pivot.Y) * s + 4f * Lift * s * (1f - s);
        float spin = SpinTurns * MathF.Tau * (1f - (1f - s) * (1f - s));
        float roll = LandingRoll * s + WobbleRoll * MathF.Sin(MathF.PI * s);
        var rotation = SN.Quaternion.CreateFromYawPitchRoll(startYaw + spin, -LandingPitch * s, roll);
        return (new SN.Vector3(along.X, height, along.Z), rotation);
    }

    /// <summary>The landed tilt without the final spin (yaw doesn't change heights).</summary>
    private static SN.Quaternion Landed(float yaw) => SN.Quaternion.CreateFromYawPitchRoll(yaw, -LandingPitch, LandingRoll);

    /// <summary>Lowest point of the dome's base rim, relative to the pivot, for a turret turned by <paramref name="rotation"/>.</summary>
    public static float LowestRimPoint(SN.Quaternion rotation, float radius)
    {
        float lowest = float.MaxValue;
        for (int i = 0; i < 72; i++)
        {
            float a = MathF.Tau * i / 72;
            var rim = new SN.Vector3(radius * MathF.Cos(a), -PivotAboveRing, radius * MathF.Sin(a));
            lowest = MathF.Min(lowest, SN.Vector3.Transform(rim, rotation).Y);
        }
        return lowest;
    }
}
```

修改 `src/Tank.Client/shaders/tank_toon.gdshader`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/shaders/tank_toon.gdshader b/src/Tank.Client/shaders/tank_toon.gdshader
index 75f2e66..7e0de78 100644
--- a/src/Tank.Client/shaders/tank_toon.gdshader
+++ b/src/Tank.Client/shaders/tank_toon.gdshader
@@ -5,6 +5,7 @@
 //              on the ground (the gun);
 //   1 wheel  — cream hub, rubber tyre in the track colour (vertex colour R: 1 hub, 0 tyre);
 //   2 track  — plain track colour (tracks, muzzle bulb).
+// char_amount (0 … 1) scorches every mode towards char_color: a knocked-out tank burns black over about half a second.
 shader_type spatial;
 render_mode cull_back;
 
@@ -18,6 +19,8 @@ uniform float bottom_shade = 0.85;       // brightness at the ground
 uniform float full_height = 1.7;         // object-space height (plus height_offset) where the paint reaches full brightness
 uniform float height_offset = 0.0;
 uniform float wheel_shade = 0.9;         // the running gear sits low, so it gets roughly the bottom of the gradient
+uniform vec3 char_color : source_color = vec3(0.169, 0.149, 0.133);   // Palette.Charred
+uniform float char_amount = 0.0;
 
 varying float object_height;
 
@@ -35,6 +38,7 @@ void fragment() {
 		albedo = mix(hub_color, body_color, COLOR.r);
 		albedo *= mix(bottom_shade, 1.0, smoothstep(0.2, full_height, object_height + height_offset));
 	}
+	albedo = mix(albedo, char_color, char_amount);
 	ALBEDO = albedo;
 	ROUGHNESS = 1.0;
 	METALLIC = 0.0;
PATCH
```


- [ ] **Step 4：运行测试，编译客户端，生成 .uid**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "通过|失败"; dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Error\(s\)"; importall; git status --short | grep uid`
Expected: Art `通过: 183`；客户端 `0 Error(s)`；出现 `TurretToss.cs.uid`、`WreckSmoke.cs.uid` 两个新文件。

- [ ] **Step 5：截图回归**（本任务还没有靶车，只确认玩家坦克外观没变）

Run: `shot t9-front front`
Expected: 输出 `Screenshot saved`。用 Read 打开 `build/shots/t9-front.png`：橙色玩具坦克与改动前一样（圆顶炮塔在原位、没有变黑），没有着色器报错。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): target paint, charred wrecks, turret toss, wreck smoke and sized blasts"
```

---

### Task 10：试驾场靶车、换弹种按键、曳光、打靶演示

- `TargetRange`：按 `SandboxMap.TargetSpots` 把 6 辆中坦加进仿真（排在玩家之后），蓝灰色模型，每辆带一股黑烟；靶车输入为零、炮塔朝正前方；`TankDestroyed` 时炮塔飞起、焦黑、冒烟、1.8 倍大爆炸；击毁 10 秒（按仿真 tick 计）后 `Revive`；`TankRevived` 时复原。
- `SandboxInput`：1 换穿甲弹、2 换高爆弹（一直保持到再按）；R 补满弹药（只生效一次）。
- `ShellViews`：穿甲弹黄色曳光，高爆弹橙色。
- `SandboxMain`：命令数组变为 1 + 6；事件分发集中到 `OnEvent`；穿甲弹打中装甲时冒一个小号灰色爆炸。
- 截图工具：`RangeDemo`（`--range-demo`）、`--shot-after`、`target` 机位。脚本化运行和固定机位一样忽略玩家输入。

**Files:**
- Create: `src/Tank.Client/scripts/Sandbox/TargetRange.cs`、`src/Tank.Client/scripts/Sandbox/RangeDemo.cs`
- Modify: `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`、`SandboxInput.cs`、`src/Tank.Client/scripts/Combat/ShellViews.cs`、`src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`、`src/Tank.Client/scripts/Cameras/CameraPresets.cs`

- [ ] **Step 1：实现**（这一步全是 Godot 表现代码，没有单元测试，靠下面的截图验收）

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index cf5bcee..ae47b4e 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -10,6 +10,7 @@ namespace Tank.Client.Bootstrap;
 /// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
 /// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
+/// --range-demo pen|ricochet|nopen|he|kill（试驾场：自动对靶车开炮，用于截图）  --shot-after fire|hit|kill:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克或第一次击毁坦克后再过 SECONDS 秒截图）
 /// </summary>
 public sealed record LaunchOptions
 {
@@ -32,6 +33,11 @@ public sealed record LaunchOptions
     public bool FpsLog { get; init; }
     public double? FxShotDelay { get; init; }
     public bool Sniper { get; init; }
+    public string? RangeDemo { get; init; }
+
+    /// <summary>"fire", "hit" or "kill" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
+    public string? ShotAfterEvent { get; init; }
+    public double ShotAfterDelay { get; init; }
 
     public static LaunchOptions Parse(IReadOnlyList<string> args)
     {
@@ -57,6 +63,8 @@ public sealed record LaunchOptions
                 "--fps-log" => o with { FpsLog = true },
                 "--sniper" => o with { Sniper = true },
                 "--fx-shot" => o with { FxShotDelay = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double f) ? f : 0.05 },
+                "--range-demo" => o with { RangeDemo = Next() },
+                "--shot-after" => ParseShotAfter(o, Next()),
                 "--screenshot" => o with { ScreenshotPath = Next() },
                 "--screenshot-at" => o with { ScreenshotAt = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double s) ? s : o.ScreenshotAt },
                 "--quit-after" => o with { QuitAfter = double.TryParse(Next(), System.Globalization.CultureInfo.InvariantCulture, out double q) ? q : 0 },
@@ -65,4 +73,13 @@ public sealed record LaunchOptions
         }
         return o;
     }
+
+    private static LaunchOptions ParseShotAfter(LaunchOptions o, string value)
+    {
+        string[] parts = value.Split(':');
+        if (parts.Length != 2 || parts[0] is not ("fire" or "hit" or "kill")
+            || !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double delay))
+            return o;
+        return o with { ShotAfterEvent = parts[0], ShotAfterDelay = delay };
+    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Cameras/CameraPresets.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Cameras/CameraPresets.cs b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
index 242c7c3..0f7a766 100644
--- a/src/Tank.Client/scripts/Cameras/CameraPresets.cs
+++ b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
@@ -6,7 +6,8 @@ namespace Tank.Client.Cameras;
 /// Fixed viewpoints for repeatable screenshots and frame-time runs (<c>--camera-preset NAME</c>).
 /// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
 /// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
-/// inside the bush patch nearest the spawn and looks at it from outside the patch.
+/// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at the first practice
+/// target from its front-right, on the side where its turret lands (pair it with --range-demo).
 /// </summary>
 public static class CameraPresets
 {
@@ -19,11 +20,13 @@ public static class CameraPresets
     private static readonly Vector2 SpinEye = new(200f, 200f);
     private const float OrbitPeriod = 12f;
     private const float OrbitRadius = 14f;
+    private static readonly Vector3 TargetEye = new(7f, 4.5f, -13f);    // front-right of the target (its forward is −Z)
+    private static readonly Vector3 TargetFocus = new(1.5f, 1.6f, 0f);  // between the hull and where the turret lands
 
     /// <summary>Presets whose eye is far from the tank: world detail (grass) should then stream around the eye, not the tank.</summary>
     public static bool StreamsAroundCamera(string? name) => name is "hill" or "spin";
 
-    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush";
+    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush" or "target";
 
     /// <summary>Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, the rest use the normal spawn.</summary>
     public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;
@@ -46,6 +49,11 @@ public static class CameraPresets
                 return (tank + yaw * new Vector3(3.4f, 0.9f, -4.8f), tank + yaw * new Vector3(1.3f, 0.65f, -2.95f));
             case "bush": // the tank parked inside a bush patch, seen from outside it (west, towards the lane)
                 return (tank + new Vector3(-16f, 3.4f, 6f), tank + TankFocus);
+            case "target":
+                var spot = Tank.Sim.Maps.SandboxMap.TargetSpots[0];
+                var at = new Vector3(spot.Position.X, groundAt(spot.Position.X, spot.Position.Y), spot.Position.Y);
+                var turn = new Basis(Vector3.Up, spot.Yaw);
+                return (at + turn * TargetEye, at + turn * TargetFocus);
             case "hill":
                 return (new Vector3(HillEye.X, groundAt(HillEye.X, HillEye.Y) + 28f, HillEye.Y), HillTarget);
             case "orbit": // circles the parked tank at 14 m, level with the turret: stresses anything keyed to the camera
PATCH
```

修改 `src/Tank.Client/scripts/Combat/ShellViews.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Combat/ShellViews.cs b/src/Tank.Client/scripts/Combat/ShellViews.cs
index 6351c81..ca275c9 100644
--- a/src/Tank.Client/scripts/Combat/ShellViews.cs
+++ b/src/Tank.Client/scripts/Combat/ShellViews.cs
@@ -7,34 +7,46 @@ using Tank.Sim.Combat;
 namespace Tank.Client.Combat;
 
 /// <summary>
-/// Bright yellow cartoon tracers for live shells, pooled: a glowing head and a short tapering trail that grows to
-/// <see cref="TrailLength"/> as the shell leaves the muzzle. Drawn one tick behind the sim to match interpolated tanks.
+/// Cartoon tracers for live shells, pooled: a glowing head and a short tapering trail that grows to
+/// <see cref="TrailLength"/> as the shell leaves the muzzle. AP is yellow, HE orange. Drawn one tick behind the sim to
+/// match interpolated tanks.
 /// </summary>
 public partial class ShellViews : Node3D
 {
     private const float TrailLength = 7f;
-    private static readonly Color TracerColor = Palette.Flash.ToColor();
 
-    private readonly List<(Node3D Root, Node3D Trail)> _pool = new();
-    private readonly StandardMaterial3D _headMaterial = new()
-    {
-        AlbedoColor = TracerColor,
-        EmissionEnabled = true,
-        Emission = TracerColor,
-        EmissionEnergyMultiplier = 4f,
-        DisableReceiveShadows = true,
-    };
-    private readonly StandardMaterial3D _trailMaterial = new()
+    private readonly List<Tracer> _pool = new();
+    private readonly (Material Head, Material Trail) _ap = TracerMaterials(Palette.Flash.ToColor());
+    private readonly (Material Head, Material Trail) _he = TracerMaterials(Palette.Fire.ToColor());
+
+    private sealed class Tracer
     {
-        AlbedoColor = new Color(TracerColor, 0.7f),
-        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
-        BlendMode = BaseMaterial3D.BlendModeEnum.Add,
-        EmissionEnabled = true,
-        Emission = TracerColor,
-        EmissionEnergyMultiplier = 2f,
-        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
-        DisableReceiveShadows = true,
-    };
+        public required Node3D Root;
+        public required MeshInstance3D Head;
+        public required MeshInstance3D Trail;
+        public AmmoType Ammo;
+    }
+
+    private static (Material Head, Material Trail) TracerMaterials(Color color) => (
+        new StandardMaterial3D
+        {
+            AlbedoColor = color,
+            EmissionEnabled = true,
+            Emission = color,
+            EmissionEnergyMultiplier = 4f,
+            DisableReceiveShadows = true,
+        },
+        new StandardMaterial3D
+        {
+            AlbedoColor = new Color(color, 0.7f),
+            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
+            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
+            EmissionEnabled = true,
+            Emission = color,
+            EmissionEnergyMultiplier = 2f,
+            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
+            DisableReceiveShadows = true,
+        });
 
     /// <param name="alpha">Fraction of the way from the previous tick to the latest one.</param>
     /// <param name="frameDelta">Render frame time, seconds; the tracer covers at least the distance flown in one frame.</param>
@@ -43,10 +55,13 @@ public partial class ShellViews : Node3D
         while (_pool.Count < shells.Length) _pool.Add(NewTracer());
         for (int i = 0; i < _pool.Count; i++)
         {
-            var (root, trail) = _pool[i];
+            Tracer tracer = _pool[i];
+            Node3D root = tracer.Root;
+            MeshInstance3D trail = tracer.Trail;
             root.Visible = i < shells.Length;
             if (!root.Visible) continue;
             Shell shell = shells[i];
+            if (shell.Ammo != tracer.Ammo) Paint(tracer, shell.Ammo);
             float age = MathF.Max(shell.Age - (1f - alpha) * SimConstants.Dt, 0f);
             Vector3 position = (shell.Position - shell.Velocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
             float speed = shell.Velocity.Length();
@@ -62,26 +77,35 @@ public partial class ShellViews : Node3D
         }
     }
 
-    private (Node3D, Node3D) NewTracer()
+    private void Paint(Tracer tracer, AmmoType ammo)
+    {
+        var (head, trail) = ammo == AmmoType.HE ? _he : _ap;
+        tracer.Head.MaterialOverride = head;
+        tracer.Trail.MaterialOverride = trail;
+        tracer.Ammo = ammo;
+    }
+
+    private Tracer NewTracer()
     {
         var root = new Node3D();
-        root.AddChild(new MeshInstance3D
+        var head = new MeshInstance3D
         {
             Mesh = new CapsuleMesh { Radius = 0.14f, Height = 1.6f, RadialSegments = 8, Rings = 2 },
-            MaterialOverride = _headMaterial,
+            MaterialOverride = _ap.Head,
             RotationDegrees = new Vector3(90, 0, 0),   // capsule axis Y → along the flight line
             CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
-        });
+        };
+        root.AddChild(head);
         // Unit-height cone along +Z (behind the head), widest at the head; stretched to the trail length each frame.
         var trail = new MeshInstance3D
         {
             Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 0.14f, Height = 1f, RadialSegments = 8, Rings = 1 },
-            MaterialOverride = _trailMaterial,
+            MaterialOverride = _ap.Trail,
             RotationDegrees = new Vector3(90, 0, 0),
             CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
         };
         root.AddChild(trail);
         AddChild(root);
-        return (root, trail);
+        return new Tracer { Root = root, Head = head, Trail = trail, Ammo = AmmoType.AP };
     }
 }
PATCH
```

新建 `src/Tank.Client/scripts/Sandbox/RangeDemo.cs`：

```csharp
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// Scripted shooting at the practice targets, for screenshots (<c>--range-demo NAME</c>): the player's tank is parked
/// <see cref="Distance"/> m from target <see cref="Target"/>, <see cref="AngleOffFront"/> degrees round from straight
/// ahead of it, lays its gun on <see cref="AimLocal"/> (target hull space: x right, y up, −z forward) and fires from
/// <see cref="FireAfter"/> s on, once or (<see cref="KeepFiring"/>) whenever reloaded.
/// </summary>
public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, SN.Vector3 AimLocal, AmmoType Ammo, bool KeepFiring)
{
    public const double FireAfter = 3.0;
    private static readonly SN.Vector3 HullFront = new(0, 1.02f, -3.1f);
    private static readonly SN.Vector3 TurretFront = new(0, 2.24f, -1.75f);

    /// <summary>pen, ricochet, nopen, he or kill; null for any other name.</summary>
    public static RangeDemo? Find(string? name) => name switch
    {
        "pen" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, false),
        "ricochet" => new RangeDemo(0, 76f, 45f, HullFront, AmmoType.AP, false),
        "nopen" => new RangeDemo(0, 60f, 45f, TurretFront, AmmoType.AP, false),
        "he" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.HE, false),
        "kill" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, true),
        _ => null,
    };

    public TargetSpot Spot => SandboxMap.TargetSpots[Target];

    /// <summary>Where the player's tank is parked.</summary>
    public SN.Vector2 PlayerPosition => Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;

    /// <summary>The player's hull faces the target.</summary>
    public float PlayerYaw
    {
        get
        {
            SN.Vector2 toTarget = Spot.Position - PlayerPosition;
            return MathF.Atan2(-toTarget.X, -toTarget.Y);
        }
    }

    /// <summary>The aim point in world space, given the ground height under the target.</summary>
    public SN.Vector3 AimPoint(float groundY)
    {
        SN.Vector3 local = SN.Vector3.Transform(AimLocal, SN.Quaternion.CreateFromAxisAngle(SN.Vector3.UnitY, Spot.Yaw));
        return new SN.Vector3(Spot.Position.X + local.X, groundY + local.Y, Spot.Position.Y + local.Z);
    }
}
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
index 14f1e0e..8215eb1 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
@@ -1,15 +1,18 @@
 using Godot;
+using Tank.Sim.Combat;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
 /// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
-/// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost.
+/// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
+/// AP or HE (kept until changed); R asks for a restock, consumed once by the next tick.
 /// </summary>
 public sealed class SandboxInput
 {
     private bool _fireLatched;
     private bool _fireHeld;
+    private bool _restock;
 
     public static void RegisterActions()
     {
@@ -20,14 +23,23 @@ public sealed class SandboxInput
         AddKey("sniper_toggle", Key.Shift);
         AddMouse("fire_gun", MouseButton.Left);
         AddMouse("free_look", MouseButton.Right);
+        AddKey("ammo_ap", Key.Key1);
+        AddKey("ammo_he", Key.Key2);
+        AddKey("restock", Key.R);
     }
 
+    /// <summary>The round the player wants loaded.</summary>
+    public AmmoType Ammo { get; private set; } = AmmoType.AP;
+
     public bool FreeLook => Input.IsActionPressed("free_look");
 
     public void OnInputEvent(InputEvent e)
     {
         if (e.IsActionPressed("fire_gun")) _fireLatched = _fireHeld = true;
         else if (e.IsActionReleased("fire_gun")) _fireHeld = false;
+        else if (e.IsActionPressed("ammo_ap")) Ammo = AmmoType.AP;
+        else if (e.IsActionPressed("ammo_he")) Ammo = AmmoType.HE;
+        else if (e.IsActionPressed("restock")) _restock = true;
     }
 
     /// <summary>Drops any pending or held fire (focus lost, so a release may never arrive).</summary>
@@ -37,6 +49,14 @@ public sealed class SandboxInput
         _fireHeld = false;
     }
 
+    /// <summary>True once after R was pressed.</summary>
+    public bool TakeRestock()
+    {
+        bool restock = _restock;
+        _restock = false;
+        return restock;
+    }
+
     public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
     {
         sbyte throttle = (sbyte)(Held("drive_forward") - Held("drive_back"));
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 8aee5dd..0531260 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -15,9 +15,10 @@ using SN = System.Numerics;
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// Stage-1 offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated.
-/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin|orbit|bush|...
-/// --fps-log --sniper --screenshot FILE --screenshot-at S --fx-shot S --quit-after S]
+/// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0;
+/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock.
+/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin|orbit|bush|target|...
+/// --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
 /// </summary>
 public partial class SandboxMain : Node3D
 {
@@ -31,9 +32,11 @@ public partial class SandboxMain : Node3D
     private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks
     private const double FxShotWarmup = 3;          // --fx-shot: the first shot after this many seconds starts the clock
     private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead
+    private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
+    private const float DemoCameraPitch = 0.1f;     // --range-demo: looking over the tank at the target
 
     private readonly SandboxInput _input = new();
-    private readonly InputCmd[] _commands = new InputCmd[1];
+    private readonly InputCmd[] _commands = new InputCmd[1 + SandboxMap.TargetSpots.Count];
     private readonly FrameTimeLog _frameLog = new();
     private LaunchOptions _options = new();
     private Simulation _sim = null!;
@@ -46,6 +49,7 @@ public partial class SandboxMain : Node3D
     private DustTrail _dust = null!;
     private bool _playerFired;
     private ShellViews _shells = null!;
+    private TargetRange _targets = null!;
     private BattleFx _fx = null!;
     private Reticle _reticle = null!;
     private StatusPanel _status = null!;
@@ -56,6 +60,9 @@ public partial class SandboxMain : Node3D
     private float _aimPitch;
     private bool _screenshotTaken;
     private double _firstShotTime = -1;
+    private RangeDemo? _demo;
+    private bool _demoFired;
+    private double _triggerTime = -1;   // --shot-after: when the awaited event happened
 
     public override void _Ready()
     {
@@ -63,9 +70,13 @@ public partial class SandboxMain : Node3D
         GetWindow().Title = "坦克 3D · 试驾场";
         SandboxInput.RegisterActions();
 
+        _demo = RangeDemo.Find(_options.RangeDemo);
         _sim = new Simulation(SandboxMap.Create());
-        _sim.AddTank(TankCatalog.Medium, CameraPresets.Spawn(_options.CameraPreset), SandboxMap.SpawnYaw);
+        _sim.AddTank(TankCatalog.Medium, _demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), _demo?.PlayerYaw ?? SandboxMap.SpawnYaw);
         _previous = _current = _sim.Tanks[0];
+        _targets = new TargetRange();
+        AddChild(_targets);
+        _targets.Build(_sim);
         _aimYaw = _current.Yaw;
 
         AddChild(Scenery.CreateEnvironment());
@@ -114,15 +125,20 @@ public partial class SandboxMain : Node3D
 
         if (_options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
         if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
-            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit or bush.");
-        if (!_options.AutoDrive && !HasPreset) Input.MouseMode = Input.MouseModeEnum.Captured;
+            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush or target.");
+        if (_options.RangeDemo is { } demo && _demo is null)
+            GD.PushWarning($"Unknown --range-demo '{demo}'; use pen, ricochet, nopen, he or kill.");
+        if (!_options.AutoDrive && !IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
     }
 
     private bool HasPreset => CameraPresets.IsKnown(_options.CameraPreset);
 
+    /// <summary>Fixed-camera and scripted runs ignore stray input so screenshots stay repeatable.</summary>
+    private bool IgnoresInput => HasPreset || _demo is not null;
+
     public override void _Input(InputEvent e)
     {
-        if (HasPreset) return; // fixed-camera runs ignore stray input so screenshots stay repeatable
+        if (IgnoresInput) return;
         // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
         if (e.IsActionReleased("fire_gun") || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
     }
@@ -136,7 +152,7 @@ public partial class SandboxMain : Node3D
 
     public override void _UnhandledInput(InputEvent e)
     {
-        if (HasPreset) return;
+        if (IgnoresInput) return;
         bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
         switch (e)
         {
@@ -180,13 +196,14 @@ public partial class SandboxMain : Node3D
         }
         _tank.Visible = !_camera.Sniper;
         if (_options.AutoDrive) _camera.SetAngles(tank.Yaw + 0.35f, 0.18f);
+        else if (_demo is not null) _camera.SetAngles(_demo.PlayerYaw, DemoCameraPitch);
         _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
         if (HasPreset)
         {
             var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _sim.Field.Terrain.HeightAt);
             _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
         }
-        else if (!_input.FreeLook) UpdateAim(pose, tank.Spec);
+        else if (_demo is null && !_input.FreeLook) UpdateAim(pose, tank.Spec);
         TankLook look = TankLooks.For(tank.Spec.Id);
         float trackHalf = look.TrackX + look.TrackWidth / 2 + FootprintMargin;
         _grass.SetTankFootprint(new Obb2(new SN.Vector2(pose.Position.X, pose.Position.Z),
@@ -197,6 +214,7 @@ public partial class SandboxMain : Node3D
         _grass.Update(CameraPresets.StreamsAroundCamera(_options.CameraPreset) ? cameraPos
             : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
         _bushes.Update(cameraPos, TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw), (float)delta);
+        _targets.Sync(_sim, (float)delta);
         _shells.Sync(_sim.Shells, alpha, (float)delta);
         UpdateReticle(tank);
         RunAutomation(delta);
@@ -204,36 +222,71 @@ public partial class SandboxMain : Node3D
 
     private void StepSimulation()
     {
-        var (throttle, steer, fire) = _options.AutoDrive ? AutoDrive() : HasPreset ? ((sbyte)0, (sbyte)0, false) : _input.Sample();
+        var (throttle, steer, fire) = _demo is not null ? DemoInput(_demo) : _options.AutoDrive ? AutoDrive() : HasPreset ? ((sbyte)0, (sbyte)0, false) : _input.Sample();
         if (_options.AutoDrive && _options.FxShotDelay is not null) _aimPitch = FxShotPitch;   // shells land in view
-        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire);
+        if (!HasPreset && _input.TakeRestock()) _sim.Restock(_current.Id);
+        _targets.ReviveDue(_sim);
+        _commands[0] = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire, _demo?.Ammo ?? _input.Ammo);
+        _targets.FillInputs(_commands.AsSpan(1), _sim.Tick + 1);
         _sim.Step(_commands);
-        foreach (GameEvent e in _sim.Events)
+        foreach (GameEvent e in _sim.Events) OnEvent(e);
+        _previous = _current;
+        _current = _sim.Tanks[0];
+    }
+
+    private void OnEvent(in GameEvent e)
+    {
+        NoteTrigger(e);
+        switch (e.Type)
         {
-            if (e.Type == GameEventType.ShellFired)
-            {
-                bool mine = e.TankId == _current.Id;
-                // Flash at the visible barrel tip for the player's tank (the sim event keeps its own muzzle position).
-                if (mine)
-                {
-                    _playerFired = true; // the flash is spawned after this frame's pose update, at the barrel tip as drawn
-                    _tank.Fire(_current.TurretYaw);
-                }
-                else _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
-            }
-            else if (e.Type == GameEventType.ShellExploded)
-            {
-                // Shells that stop above the ground hit a rock: grey dust instead of brown.
+            case GameEventType.ShellFired when e.TankId == _current.Id:
+                // Flash at the visible barrel tip (the sim event keeps its own muzzle position): spawned after this frame's pose update.
+                _playerFired = true;
+                _demoFired = true;
+                _tank.Fire(_current.TurretYaw);
+                break;
+            case GameEventType.ShellFired:
+                _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
+                break;
+            case GameEventType.ShellExploded:
+                // Shells that stop above the ground hit a rock or a tank: grey dust instead of brown.
                 bool onRock = e.Position.Y > _sim.Field.Terrain.HeightAt(e.Position.X, e.Position.Z) + 0.5f;
                 _fx.Explosion(e.Position.ToGodot(), onRock);
-            }
+                break;
+            case GameEventType.TankHit when e.Ammo == AmmoType.AP:
+                _fx.Explosion(e.Position.ToGodot(), onRock: true, ArmorHitSize);   // HE gets its own ShellExploded
+                break;
         }
-        _previous = _current;
-        _current = _sim.Tanks[0];
+        _targets.OnEvent(e, _sim, _fx);
     }
 
     private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);
 
+    /// <summary>--range-demo: stand still, lay the gun on the demo's aim point, fire once (or keep firing) after the warm-up.</summary>
+    private (sbyte Throttle, sbyte Steer, bool Fire) DemoInput(RangeDemo demo)
+    {
+        TankPose pose = TankPose.Compute(_current, _sim.Field.Terrain);
+        SN.Vector2 spot = demo.Spot.Position;
+        SN.Vector3 aim = demo.AimPoint(_sim.Field.Terrain.HeightAt(spot.X, spot.Y));
+        (_aimYaw, _aimPitch) = Angles.AimAngles(aim - pose.Trunnion(_current.Spec), _aimYaw);
+        bool fire = _time >= RangeDemo.FireAfter && (demo.KeepFiring || !_demoFired);
+        return (0, 0, fire);
+    }
+
+    /// <summary>--shot-after: remembers when the awaited event first happened.</summary>
+    private void NoteTrigger(in GameEvent e)
+    {
+        if (_triggerTime >= 0 || _options.ShotAfterEvent is not { } awaited) return;
+        bool due = awaited switch
+        {
+            "fire" => e.Type == GameEventType.ShellFired && e.TankId == _current.Id,
+            "hit" => e.Type == GameEventType.TankHit,
+            "kill" => e.Type == GameEventType.TankDestroyed,
+            _ => false,
+        };
+        if (due) _triggerTime = _time;
+    }
+
     /// <summary>Casts from the orbit focus along the camera's view; the turret then chases that point.</summary>
     private void UpdateAim(TankPose pose, TankSpec spec)
     {
@@ -289,7 +342,9 @@ public partial class SandboxMain : Node3D
                     (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame)) is { } line)
                 GD.Print(line);
         }
-        double shotAt = _options.FxShotDelay is { } fxDelay ? (_firstShotTime < 0 ? double.MaxValue : _firstShotTime + fxDelay) : _options.ScreenshotAt;
+        double shotAt = _options.ShotAfterEvent is not null ? (_triggerTime < 0 ? double.MaxValue : _triggerTime + _options.ShotAfterDelay)
+            : _options.FxShotDelay is { } fxDelay ? (_firstShotTime < 0 ? double.MaxValue : _firstShotTime + fxDelay)
+            : _options.ScreenshotAt;
         if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= shotAt)
         {
             _screenshotTaken = true;
PATCH
```

新建 `src/Tank.Client/scripts/Sandbox/TargetRange.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Client.Sandbox;

/// <summary>
/// The six practice targets on <see cref="SandboxMap.TargetSpots"/>: blue-grey medium tanks that sit still with their
/// guns straight ahead. A knocked-out target blows up, throws its turret, chars and smokes; ten seconds later it is
/// revived where it stands.
/// </summary>
public partial class TargetRange : Node3D
{
    public const float ReviveSeconds = 10f;
    private const float BlastSize = 1.8f;
    private static readonly Vector3 SmokeAt = new(0, 1.7f, -0.3f);   // the open turret ring
    private static readonly TankPaint Paint = new(Palette.TargetBody, Palette.Track, Palette.TankHub);

    private readonly List<Target> _targets = new();

    private sealed class Target
    {
        public required int Id;
        public required int Slot;   // index in Simulation.Tanks
        public required TargetSpot Spot;
        public required TankModel Model;
        public required WreckSmoke Smoke;
        public int WreckedTick = -1;
    }

    public int Count => _targets.Count;

    /// <summary>Adds the targets to the simulation (after the player, so they take the next slots) and builds their models.</summary>
    public void Build(Simulation sim)
    {
        foreach (TargetSpot spot in SandboxMap.TargetSpots)
        {
            int slot = sim.TankCount;
            int id = sim.AddTank(TankCatalog.Medium, spot.Position, spot.Yaw);
            var model = new TankModel();
            AddChild(model);
            model.Build(TankCatalog.Medium, Paint);
            var smoke = new WreckSmoke();
            model.AddChild(smoke);
            smoke.Build(SmokeAt);
            _targets.Add(new Target { Id = id, Slot = slot, Spot = spot, Model = model, Smoke = smoke });
        }
    }

    public bool IsTarget(int tankId) => Find(tankId) is not null;

    /// <summary>Every target holds still with its gun straight ahead. <paramref name="commands"/> starts at the first target's slot.</summary>
    public void FillInputs(Span<InputCmd> commands, int tick)
    {
        for (int i = 0; i < _targets.Count; i++) commands[i] = new InputCmd(tick, 0, 0, _targets[i].Spot.Yaw, 0f, false);
    }

    /// <summary>Revives wrecks that have lain for <see cref="ReviveSeconds"/>. Call before stepping the simulation.</summary>
    public void ReviveDue(Simulation sim)
    {
        int due = (int)MathF.Round(ReviveSeconds * SimConstants.TickRate);
        foreach (Target t in _targets)
            if (t.WreckedTick >= 0 && sim.Tick - t.WreckedTick >= due)
            {
                sim.Revive(t.Id);
                t.WreckedTick = -1;
            }
    }

    /// <summary>Reacts to <see cref="GameEventType.TankDestroyed"/> and <see cref="GameEventType.TankRevived"/> for targets.</summary>
    public void OnEvent(in GameEvent e, Simulation sim, BattleFx fx)
    {
        Target? target = Find(e.TargetTankId);
        if (target is null) return;
        if (e.Type == GameEventType.TankDestroyed)
        {
            target.WreckedTick = e.Tick;
            target.Model.Wreck(sim.Tanks[target.Slot].TurretYaw);
            target.Smoke.Emitting = true;
            fx.Explosion(e.Position.ToGodot() + Vector3.Up * 1.2f, onRock: false, BlastSize);
        }
        else if (e.Type == GameEventType.TankRevived)
        {
            target.Model.Restore();
            target.Smoke.Emitting = false;
        }
    }

    /// <summary>Places every target model on its (still) tank and runs its wreck animation.</summary>
    public void Sync(Simulation sim, float delta)
    {
        foreach (Target target in _targets)
        {
            TankState state = sim.Tanks[target.Slot];
            target.Model.Apply(TankPose.Compute(state, sim.Field.Terrain), state.TurretYaw, state.GunPitch);
            target.Model.Animate(0f, 0f, delta);
        }
    }

    private Target? Find(int tankId)
    {
        foreach (Target t in _targets)
            if (t.Id == tankId) return t;
        return null;
    }
}
```


- [ ] **Step 2：编译，生成 .uid**

Run: `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E " error |Error\(s\)"; importall; git status --short | grep uid`
Expected: `0 Error(s)`；出现 `TargetRange.cs.uid`、`RangeDemo.cs.uid`。

- [ ] **Step 3：截图验收**

```bash
demoshot t10-pen pen hit:0.05 5
demoshot t10-ricochet ricochet hit:0.05 5
demoshot t10-toss kill kill:0.45 45 --camera-preset target
demoshot t10-wreck kill kill:3 45 --camera-preset target
demoshot t10-revived kill kill:10.6 55 --camera-preset target
```

`kill` 要连开约 5 炮（每炮装填 6.5 秒），每次运行 30～40 秒。每张都要输出 `Screenshot saved`，然后逐张用 Read 打开核对：
- `t10-pen`：玩家坦克背后视角，远处一辆蓝灰色靶车，命中处有一团小灰烟或火光；
- `t10-ricochet`：靶车侧着车身，命中处有小爆炸；
- `t10-toss`：机位在靶车右前方，近处是蓝灰色车体，圆顶炮塔连着炮管飞在车体上方约一个车身高，正在变黑，车体周围有大爆炸的白色烟团；
- `t10-wreck`：车体整体焦黑，炮塔倾斜着落在车体一侧的地上，车体上方冒深色烟柱；
- `t10-revived`：靶车恢复蓝灰色、炮塔回到原位（残余的黑烟还在飘散是正常的）。

- [ ] **Step 4：提交，并结束批次 C**

```bash
git add src/Tank.Client
git commit -m "feat(client): practice targets in the sandbox, AP/HE keys and tracers, scripted range demos"
```

批次 C 结束：规格审查 → 代码质量审查 → `docs/PROGRESS.md` 记一行。

---

### Task 11：面板、散布圈、命中大字、伤害数字、头顶血条

- `StatusPanel` 改为三行：心形图标 + 胶囊血条 + 血量数字；速度和装填（不变）；两个弹药块“1 穿甲 40”“2 高爆 15”，当前弹种用强调色描边，弹药为 0 时变灰。只在显示内容变化时重画。
- `Reticle`：在“当前会打到哪”的位置画散布圈，半径由 `SandboxMain.AimCirclePixels` 按真实比例投影（圈在 100 m 处的半径 × 炮口到落点的距离 / 100，再按镜头到落点的距离和视角换成像素）。插值时 `Bloom` 也插值。
- `PopupCurves`（不依赖 Godot）：大字“鼓起—回弹—停住—淡出”，伤害数字小幅鼓起、上飘 2 m、淡出。
- `CombatPopups`：24 格对象池，红“击穿！”、黄“跳弹！”、灰“未击穿”画在命中点上方 52 px；伤害数字从靶车头顶（离地 3.4 m）往上飘，溅射小一号。不用 Tween。
- `HealthBars`：靶车头顶的胶囊血条，只在 250 m 内、在画面中、没被击毁时显示，满血也显示；低于 30% 变红。
- `HudStyle` 的描边文字加 `alpha` 参数，让描边也能淡出。

**Files:**
- Create: `src/Tank.Client/scripts/Overlay/PopupCurves.cs`、`CombatPopups.cs`、`HealthBars.cs`
- Modify: `src/Tank.Client/scripts/Overlay/StatusPanel.cs`（整体改写）、`Reticle.cs`、`HudStyle.cs`、`src/Tank.Client/scripts/Sandbox/SandboxMain.cs`
- Test: 新建 `tests/Tank.Art.Tests/PopupCurvesTests.cs`；改 `Tank.Art.Tests.csproj`（链接 `PopupCurves.cs`）

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/PopupCurvesTests.cs`：

```csharp
using Tank.Client.Overlay;

namespace Tank.Art.Tests;

public class PopupCurvesTests
{
    [Fact]
    public void Verdict_pops_past_full_size_then_settles()
    {
        float peak = 0f;
        for (float t = 0; t < 0.3f; t += 0.005f) peak = MathF.Max(peak, PopupCurves.VerdictScale(t));
        Assert.True(peak > 1.25f, $"peak {peak}");
        Assert.True(PopupCurves.VerdictScale(0f) < 0.5f);
        Assert.Equal(1f, PopupCurves.VerdictScale(0.3f));
        Assert.Equal(1f, PopupCurves.VerdictScale(PopupCurves.VerdictLife));
    }

    [Fact]
    public void Damage_pops_less_than_a_verdict()
    {
        float peak = 0f;
        for (float t = 0; t < 0.3f; t += 0.005f) peak = MathF.Max(peak, PopupCurves.DamageScale(t));
        Assert.InRange(peak, 1.05f, 1.25f);
    }

    [Fact]
    public void Alpha_holds_then_fades_to_nothing()
    {
        Assert.Equal(1f, PopupCurves.Alpha(0.2f, PopupCurves.VerdictLife));
        Assert.InRange(PopupCurves.Alpha(PopupCurves.VerdictLife - PopupCurves.FadeTime / 2, PopupCurves.VerdictLife), 0.45f, 0.55f);
        Assert.Equal(0f, PopupCurves.Alpha(PopupCurves.VerdictLife, PopupCurves.VerdictLife));
    }

    [Fact]
    public void Rise_climbs_steadily_to_its_height()
    {
        Assert.Equal(0f, PopupCurves.Rise(0f));
        float last = 0f;
        for (float t = 0.05f; t <= PopupCurves.DamageLife; t += 0.05f)
        {
            float rise = PopupCurves.Rise(t);
            Assert.True(rise > last);
            last = rise;
        }
        Assert.Equal(PopupCurves.DamageRise, PopupCurves.Rise(PopupCurves.DamageLife), 3);
    }
}
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index 01123ab..93252a3 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -14,6 +14,8 @@
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Mesh\**\*.cs" LinkBase="Mesh" />
     <!-- The palette is engine-neutral too, so look data and vertex colours can use it. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
+    <!-- Popup timing curves are engine-neutral too. -->
+    <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs" LinkBase="Overlay" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Vehicles\TankLook.cs;..\..\src\Tank.Client\scripts\Vehicles\TankLooks.cs;..\..\src\Tank.Client\scripts\Vehicles\TankParts.cs;..\..\src\Tank.Client\scripts\Vehicles\TankAnimator.cs;..\..\src\Tank.Client\scripts\Vehicles\TurretToss.cs" LinkBase="Vehicles" />
   </ItemGroup>
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS|通过|失败" | head -5`
Expected: 编译失败（链接的 `PopupCurves.cs` 不存在）。

- [ ] **Step 3：实现**

新建 `src/Tank.Client/scripts/Overlay/CombatPopups.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Sim;

namespace Tank.Client.Overlay;

/// <summary>
/// Big outlined cartoon words where a shell strikes a tank — red "击穿！", yellow "跳弹！", grey "未击穿" — and damage
/// numbers ("-220") floating up from the target's head (splash damage a size smaller). A verdict sits just above the
/// point struck, so the reticle (usually right on it) doesn't cover it. Anchored in the world and drawn
/// in screen space; pooled slots animated by <see cref="PopupCurves"/>, no tweens. When every slot is busy the oldest
/// is reused.
/// </summary>
public partial class CombatPopups : Control
{
    private const int Slots = 24;
    private const int VerdictSize = 46;
    private const int DamageSize = 38;
    private const int SplashSize = 30;
    private const float VerdictLift = 0.6f;   // a verdict drifts up this far over its life, m
    private const float VerdictAbove = 52f;   // pixels above the point struck, clear of the reticle drawn over it

    private readonly Popup[] _popups = new Popup[Slots];
    private Camera3D? _camera;

    private struct Popup
    {
        public bool Active;
        public bool IsDamage;
        public Vector3 World;
        public string Text;
        public Color Color;
        public int Size;
        public float Age;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void SetCamera(Camera3D camera) => _camera = camera;

    /// <summary>The verdict for a <see cref="GameEventType.TankHit"/>, at the point struck.</summary>
    public void Verdict(Vector3 at, HitResult result)
    {
        (string text, Color color) = result switch
        {
            HitResult.Penetrated => ("击穿！", Palette.HitPierced.ToColor()),
            HitResult.Ricochet => ("跳弹！", Palette.UiAccent.ToColor()),
            _ => ("未击穿", Palette.HitBlocked.ToColor()),
        };
        Spawn(new Popup { World = at, Text = text, Color = color, Size = VerdictSize });
    }

    /// <summary>A damage number rising from <paramref name="head"/> (above the target).</summary>
    public void Damage(Vector3 head, int damage, bool splash)
    {
        Spawn(new Popup
        {
            IsDamage = true,
            World = head,
            Text = $"-{damage}",
            Color = Palette.HitPierced.ToColor(),
            Size = splash ? SplashSize : DamageSize,
        });
    }

    private void Spawn(Popup popup)
    {
        int slot = 0;
        for (int i = 0; i < Slots; i++)
        {
            if (!_popups[i].Active) { slot = i; break; }
            if (_popups[i].Age > _popups[slot].Age) slot = i;
        }
        popup.Active = true;
        _popups[slot] = popup;
    }

    public override void _Process(double delta)
    {
        bool any = false;
        for (int i = 0; i < Slots; i++)
        {
            ref Popup p = ref _popups[i];
            if (!p.Active) continue;
            p.Age += (float)delta;
            p.Active = p.Age < (p.IsDamage ? PopupCurves.DamageLife : PopupCurves.VerdictLife);
            any = true;
        }
        if (any) QueueRedraw();
    }

    public override void _Draw()
    {
        if (_camera is null) return;
        for (int i = 0; i < Slots; i++)
        {
            ref Popup p = ref _popups[i];
            if (!p.Active) continue;
            float life = p.IsDamage ? PopupCurves.DamageLife : PopupCurves.VerdictLife;
            float rise = p.IsDamage ? PopupCurves.Rise(p.Age) : VerdictLift * p.Age / life;
            Vector3 world = p.World + Vector3.Up * rise;
            if (_camera.IsPositionBehind(world)) continue;
            float scale = p.IsDamage ? PopupCurves.DamageScale(p.Age) : PopupCurves.VerdictScale(p.Age);
            float alpha = PopupCurves.Alpha(p.Age, life);
            Vector2 at = _camera.UnprojectPosition(world) - new Vector2(0, p.IsDamage ? 0f : VerdictAbove);
            DrawSetTransform(at, 0f, new Vector2(scale, scale));
            HudStyle.DrawOutlinedCentered(this, 0f, p.Size * 0.36f, p.Text, p.Size, new Color(p.Color, alpha), HudStyle.Outline, alpha);
        }
        DrawSetTransform(Vector2.Zero);
    }
}
```

新建 `src/Tank.Client/scripts/Overlay/HealthBars.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Overlay;

/// <summary>
/// Cartoon capsule health bars floating over other tanks' heads, in screen space: green, red below 30%, with a dark
/// outline. Shown only for live tanks within <see cref="MaxRange"/> m of the camera and in front of it (full health too).
/// Feed the bars every frame with <see cref="Set"/>, then they draw themselves.
/// </summary>
public partial class HealthBars : Control
{
    public const float MaxRange = 250f;
    private const float Width = 64f, Height = 11f, Outline = 2.5f;
    private const float LowHealth = 0.3f;

    private readonly StyleBoxFlat _back = new() { BgColor = HudStyle.OutlineColor, CornerDetail = 6, AntiAliasing = true };
    private readonly StyleBoxFlat _fill = new() { CornerDetail = 6, AntiAliasing = true };
    private Bar[] _bars = Array.Empty<Bar>();
    private Camera3D? _camera;

    private struct Bar
    {
        public bool Shown;
        public Vector3 Head;
        public float Share;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _back.SetCornerRadiusAll((int)(Height / 2 + Outline));
        _fill.SetCornerRadiusAll((int)(Height / 2));
    }

    public void Setup(Camera3D camera, int count)
    {
        _camera = camera;
        _bars = new Bar[count];
    }

    /// <param name="head">World point just above the tank, where the bar sits.</param>
    /// <param name="share">Health left, 0 … 1.</param>
    /// <param name="alive">False for a wreck: no bar.</param>
    public void Set(int index, Vector3 head, float share, bool alive)
    {
        _bars[index] = new Bar { Shown = alive, Head = head, Share = Math.Clamp(share, 0f, 1f) };
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_camera is null) return;
        Vector3 eye = _camera.GlobalPosition;
        foreach (Bar bar in _bars)
        {
            if (!bar.Shown || bar.Head.DistanceTo(eye) > MaxRange || _camera.IsPositionBehind(bar.Head)) continue;
            Vector2 at = _camera.UnprojectPosition(bar.Head);
            if (!GetViewportRect().HasPoint(at)) continue;
            var back = new Rect2(at.X - Width / 2 - Outline, at.Y - Height / 2 - Outline, Width + 2 * Outline, Height + 2 * Outline);
            DrawStyleBox(_back, back);
            if (bar.Share <= 0f) continue;
            _fill.BgColor = bar.Share > LowHealth ? Palette.Health.ToColor() : Palette.HitPierced.ToColor();
            DrawStyleBox(_fill, new Rect2(at.X - Width / 2, at.Y - Height / 2, MathF.Max(Height, Width * bar.Share), Height));
        }
    }
}
```

修改 `src/Tank.Client/scripts/Overlay/HudStyle.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/HudStyle.cs b/src/Tank.Client/scripts/Overlay/HudStyle.cs
index 401efe9..5798b73 100644
--- a/src/Tank.Client/scripts/Overlay/HudStyle.cs
+++ b/src/Tank.Client/scripts/Overlay/HudStyle.cs
@@ -22,18 +22,21 @@ public static class HudStyle
     public static Color Accent => Palette.UiAccent.ToColor();
     public static Color Panel => Palette.UiPanel.ToColor(Palette.UiPanelAlpha);
 
-    /// <summary>Text with a dark outline; <paramref name="baseline"/> is the left end of the text baseline.</summary>
-    public static void DrawOutlined(CanvasItem canvas, Vector2 baseline, string text, int size, Color color, int outline = Outline)
+    /// <summary>
+    /// Text with a dark outline; <paramref name="baseline"/> is the left end of the text baseline. <paramref name="alpha"/>
+    /// fades the outline too (the fill's alpha is the colour's own).
+    /// </summary>
+    public static void DrawOutlined(CanvasItem canvas, Vector2 baseline, string text, int size, Color color, int outline = Outline, float alpha = 1f)
     {
-        canvas.DrawStringOutline(Font, baseline, text, HorizontalAlignment.Left, -1, size, outline, OutlineColor);
+        canvas.DrawStringOutline(Font, baseline, text, HorizontalAlignment.Left, -1, size, outline, new Color(OutlineColor, alpha));
         canvas.DrawString(Font, baseline, text, HorizontalAlignment.Left, -1, size, color);
     }
 
     /// <summary>Same, centred horizontally on <paramref name="centerX"/>.</summary>
-    public static void DrawOutlinedCentered(CanvasItem canvas, float centerX, float baselineY, string text, int size, Color color, int outline = Outline)
+    public static void DrawOutlinedCentered(CanvasItem canvas, float centerX, float baselineY, string text, int size, Color color, int outline = Outline, float alpha = 1f)
     {
         float width = Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
-        DrawOutlined(canvas, new Vector2(centerX - width / 2, baselineY), text, size, color, outline);
+        DrawOutlined(canvas, new Vector2(centerX - width / 2, baselineY), text, size, color, outline, alpha);
     }
 
     public static float TextWidth(string text, int size) => Font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
PATCH
```

新建 `src/Tank.Client/scripts/Overlay/PopupCurves.cs`：

```csharp
namespace Tank.Client.Overlay;

/// <summary>
/// Timing of the cartoon hit popups, engine-neutral so Tank.Art.Tests can check it. A verdict ("击穿！") pops in past
/// full size, springs back, holds and fades; a damage number pops smaller, floats up and fades. Ages in seconds.
/// </summary>
public static class PopupCurves
{
    public const float VerdictLife = 1.1f;
    public const float DamageLife = 1.3f;
    public const float FadeTime = 0.35f;
    public const float DamageRise = 2.0f;           // metres a damage number floats up over its life
    private const float PopIn = 0.12f, Settle = 0.25f;
    private const float VerdictPeak = 1.35f, DamagePeak = 1.15f;
    private const float VerdictStart = 0.2f, DamageStart = 0.5f;

    public static float VerdictScale(float age) => Pop(age, VerdictStart, VerdictPeak);

    public static float DamageScale(float age) => Pop(age, DamageStart, DamagePeak);

    /// <summary>1 until the last <see cref="FadeTime"/> s of <paramref name="life"/>, then down to 0.</summary>
    public static float Alpha(float age, float life) => Math.Clamp((life - age) / FadeTime, 0f, 1f);

    /// <summary>Height a damage number has floated, m: quick at first, easing out.</summary>
    public static float Rise(float age)
    {
        float t = Math.Clamp(age / DamageLife, 0f, 1f);
        return DamageRise * (1f - (1f - t) * (1f - t));
    }

    /// <summary>From <paramref name="start"/> up to <paramref name="peak"/> at <see cref="PopIn"/>, back to 1 at <see cref="Settle"/>, then 1.</summary>
    private static float Pop(float age, float start, float peak)
    {
        if (age <= 0f) return start;
        if (age < PopIn) return start + (peak - start) * Smooth(age / PopIn);
        if (age < Settle) return peak + (1f - peak) * Smooth((age - PopIn) / (Settle - PopIn));
        return 1f;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
```

修改 `src/Tank.Client/scripts/Overlay/Reticle.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/Reticle.cs b/src/Tank.Client/scripts/Overlay/Reticle.cs
index 6218750..7bb7ba3 100644
--- a/src/Tank.Client/scripts/Overlay/Reticle.cs
+++ b/src/Tank.Client/scripts/Overlay/Reticle.cs
@@ -4,8 +4,9 @@ namespace Tank.Client.Overlay;
 
 /// <summary>
 /// Centre dot = where the player wants to aim; thick ring = where the gun would hit right now, with a reload ring in the
-/// accent colour around it that pops (1 → 1.25 → 1 in 0.2 s) when the gun is ready. In sniper mode the zoom shows
-/// below the centre. Every mark has a dark outline.
+/// accent colour around it that pops (1 → 1.25 → 1 in 0.2 s) when the gun is ready; a thin circle around the same point
+/// = the aim circle (where the shell may land), true to scale, so it is small in the third-person view and grows in
+/// the sniper sight and while moving. In sniper mode the zoom shows below the centre. Every mark has a dark outline.
 /// </summary>
 public partial class Reticle : Control
 {
@@ -16,10 +17,13 @@ public partial class Reticle : Control
     private const float DotRadius = 3f;
     private const float PopSeconds = 0.2f;
     private const float PopScale = 0.25f;
+    private const float AimCircleWidth = 2f;
+    private const float MinAimCircle = 1.5f;   // smaller than this it would just be a blob on the dot
 
     private Vector2? _impact;
     private float _reload = 1f;
     private float _zoom;
+    private float _aimCircle;
     private float _popLeft;
     private string _zoomText = "";
 
@@ -32,8 +36,10 @@ public partial class Reticle : Control
     /// <param name="impact">Screen position of the predicted hit, or null if it is behind the camera.</param>
     /// <param name="reloadProgress">0 just fired … 1 ready.</param>
     /// <param name="zoom">Sniper magnification, or 0 outside sniper mode.</param>
-    public void UpdateState(Vector2? impact, float reloadProgress, float zoom)
+    /// <param name="aimCircle">Aim-circle radius on screen, pixels.</param>
+    public void UpdateState(Vector2? impact, float reloadProgress, float zoom, float aimCircle)
     {
+        _aimCircle = aimCircle;
         float reload = Mathf.Clamp(reloadProgress, 0f, 1f);
         if (_reload < 1f && reload >= 1f) _popLeft = PopSeconds;
         _impact = impact;
@@ -58,6 +64,8 @@ public partial class Reticle : Control
         if (_zoom > 0) HudStyle.DrawOutlinedCentered(this, center.X, center.Y + 64f, _zoomText, 26, HudStyle.Text);
         if (_impact is not { } p) return;
 
+        if (_aimCircle >= MinAimCircle)
+            HudStyle.DrawOutlinedArc(this, p, _aimCircle, 0, Mathf.Tau, HudStyle.Accent, AimCircleWidth, 1.5f);
         float pop = 1f + PopScale * MathF.Sin(MathF.PI * (1f - _popLeft / PopSeconds)) * (_popLeft > 0 ? 1f : 0f);
         HudStyle.DrawOutlinedArc(this, p, RingRadius * pop, 0, Mathf.Tau, HudStyle.Text, RingWidth);
         if (_reload < 1f)
PATCH
```

修改 `src/Tank.Client/scripts/Overlay/StatusPanel.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/StatusPanel.cs b/src/Tank.Client/scripts/Overlay/StatusPanel.cs
index 584d425..8967231 100644
--- a/src/Tank.Client/scripts/Overlay/StatusPanel.cs
+++ b/src/Tank.Client/scripts/Overlay/StatusPanel.cs
@@ -1,16 +1,18 @@
 using Godot;
+using Tank.Client.Art;
+using Tank.Sim.Combat;
 
 namespace Tank.Client.Overlay;
 
 /// <summary>
-/// Bottom-left rounded panel: a speedometer icon with the speed as a big outlined number, and a shell icon with the
-/// reload state ("就绪" in the accent colour, or the seconds left). Icons are drawn with primitives, no image files.
-/// Redraws only when the shown speed (whole km/h), reload (tenths of a second) or ready state changes.
+/// Bottom-left rounded panel in three rows: a capsule health bar with the hit points; a speedometer icon with the speed
+/// as a big outlined number and a shell icon with the reload state ("就绪" in the accent colour, or the seconds left);
+/// and two ammo chips, "1 穿甲 40" and "2 高爆 15", the loaded one outlined in the accent colour, an empty one greyed.
+/// Icons are drawn with primitives, no image files. Redraws only when something it shows changes.
 /// </summary>
 public partial class StatusPanel : Control
 {
     private const float Margin = 24f;
-    private const float PanelHeight = 92f;
     private const float PanelAlpha = 0.85f;   // more opaque than the HUD default so it stays readable under the sniper vignette
     private const float Pad = 16f;
     private const float IconColumn = 38f;
@@ -18,6 +20,12 @@ public partial class StatusPanel : Control
     private const int NumberSize = 40;
     private const int ReadySize = 34;
     private const int UnitSize = 20;
+    private const int HealthSize = 22;
+    private const int ChipSize = 22;
+    private const float HealthRow = 34f, GaugeRow = 64f, ChipRow = 40f, RowGap = 4f;
+    private const float BarHeight = 16f;
+    private const float ChipPad = 10f, ChipGap = 12f;
+    private const float LowHealth = 0.3f;
 
     // Shell icon, centred on the origin; the outline is the same polygon scaled up. Built once.
     private static readonly Vector2[] ShellBody =
@@ -25,11 +33,18 @@ public partial class StatusPanel : Control
         new(-7, 18), new(7, 18), new(7, -4), new(4, -13), new(0, -18), new(-4, -13), new(-7, -4),
     };
     private static readonly Vector2[] ShellOutline = ScaleAll(ShellBody, 1.22f);
+    private static readonly Vector2[] Heart = HeartShape();
+    private static readonly Vector2[] HeartOutline = ScaleAll(Heart, 1.3f);
 
-    private readonly StyleBoxFlat _box = new()
+    private readonly StyleBoxFlat _box = new() { BgColor = new Color(HudStyle.Panel, PanelAlpha), CornerDetail = 8, AntiAliasing = true };
+    private readonly StyleBoxFlat _barBack = new() { BgColor = HudStyle.OutlineColor, CornerDetail = 8, AntiAliasing = true };
+    private readonly StyleBoxFlat _barFill = new() { CornerDetail = 8, AntiAliasing = true };
+    private readonly StyleBoxFlat _chip = new() { BgColor = new Color(HudStyle.OutlineColor, 0.6f), CornerDetail = 6, AntiAliasing = true };
+    private readonly StyleBoxFlat _chipLoaded = new()
     {
-        BgColor = new Color(HudStyle.Panel, PanelAlpha),
-        CornerDetail = 8,
+        BgColor = new Color(HudStyle.Accent, 0.22f),
+        BorderColor = HudStyle.Accent,
+        CornerDetail = 6,
         AntiAliasing = true,
     };
 
@@ -38,34 +53,51 @@ public partial class StatusPanel : Control
     private bool _ready = true;
     private string _speedText = "0";
     private string _reloadText = "就绪";
+    private int _health = -1, _maxHealth = 1;
+    private string _healthText = "";
+    private AmmoType _ammo;
+    private int _apLeft = -1, _heLeft = -1;
+    private string _apText = "", _heText = "";
     private float _panelWidth;
     private float _shellOffset;   // x of the shell column relative to the speedometer centre
+    private float _chipWidth;
 
     public override void _Ready()
     {
         _box.SetCornerRadiusAll(18);
+        _barBack.SetCornerRadiusAll((int)(BarHeight / 2 + 2));
+        _barFill.SetCornerRadiusAll((int)(BarHeight / 2));
+        _chip.SetCornerRadiusAll(10);
+        _chipLoaded.SetCornerRadiusAll(10);
+        _chipLoaded.SetBorderWidthAll(3);
         SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
         MouseFilter = MouseFilterEnum.Ignore;
-        // Sized for the widest content ("888 km/h", "8.8 秒") so the panel never resizes while playing.
+        // Sized for the widest content ("888 km/h", "8.8 秒", "2 高爆 88") so the panel never resizes while playing.
         float speedW = HudStyle.TextWidth("888", NumberSize) + 6f + HudStyle.TextWidth("km/h", UnitSize);
         float reloadW = HudStyle.TextWidth("8.8", NumberSize) + 6f + HudStyle.TextWidth("秒", UnitSize);
-        _shellOffset = 28f - 14f + HudStyle.TextWidth("888", NumberSize) + 6f + HudStyle.TextWidth("km/h", UnitSize) + ColumnGap + 14f;
+        _shellOffset = 28f - 14f + speedW + ColumnGap + 14f;
         _panelWidth = IconColumn + 28f - 14f + speedW + ColumnGap + 20f + 20f + reloadW + Pad;
+        _chipWidth = HudStyle.TextWidth("2 高爆 88", ChipSize) + 2 * ChipPad;
     }
 
+    private static float PanelHeight => Pad + HealthRow + RowGap + GaugeRow + RowGap + ChipRow + Pad;
+
     /// <param name="speedKmh">Ground speed, km/h (sign ignored).</param>
     /// <param name="reloadSeconds">Seconds until the gun is ready; 0 = ready.</param>
-    public void UpdateState(float speedKmh, float reloadSeconds)
+    public void UpdateState(float speedKmh, float reloadSeconds, int health, int maxHealth, AmmoType ammo, int apLeft, int heLeft)
     {
         int speed = (int)MathF.Round(MathF.Abs(speedKmh));
         bool ready = reloadSeconds <= 0f;
         int tenths = ready ? 0 : (int)MathF.Round(reloadSeconds * 10f);
-        if (speed == _speed && ready == _ready && tenths == _reloadTenths) return;
+        if (speed == _speed && ready == _ready && tenths == _reloadTenths && health == _health && maxHealth == _maxHealth
+            && ammo == _ammo && apLeft == _apLeft && heLeft == _heLeft) return;
         if (speed != _speed) _speedText = speed.ToString();
         if (tenths != _reloadTenths || ready != _ready) _reloadText = ready ? "就绪" : $"{tenths / 10}.{tenths % 10}";
-        _speed = speed;
-        _ready = ready;
-        _reloadTenths = tenths;
+        if (health != _health) _healthText = health.ToString();
+        if (apLeft != _apLeft) _apText = $"1 穿甲 {apLeft}";
+        if (heLeft != _heLeft) _heText = $"2 高爆 {heLeft}";
+        (_speed, _ready, _reloadTenths, _health, _maxHealth) = (speed, ready, tenths, health, maxHealth);
+        (_ammo, _apLeft, _heLeft) = (ammo, apLeft, heLeft);
         QueueRedraw();
     }
 
@@ -73,9 +105,39 @@ public partial class StatusPanel : Control
     {
         var origin = new Vector2(Margin, Size.Y - Margin - PanelHeight);
         DrawStyleBox(_box, new Rect2(origin, new Vector2(_panelWidth, PanelHeight)));
-        float baseline = origin.Y + PanelHeight / 2 + NumberSize * 0.36f;
+        float y = origin.Y + Pad;
+        DrawHealth(origin.X, y + HealthRow / 2);
+        y += HealthRow + RowGap;
+        DrawGauges(origin.X, y + GaugeRow / 2);
+        y += GaugeRow + RowGap;
+        DrawChips(origin.X, y + ChipRow / 2);
+    }
+
+    private void DrawHealth(float left, float middle)
+    {
+        Vector2 heart = new(left + IconColumn, middle);
+        DrawSetTransform(heart);
+        DrawColoredPolygon(HeartOutline, HudStyle.OutlineColor);
+        DrawColoredPolygon(Heart, Palette.HitPierced.ToColor());
+        DrawSetTransform(Vector2.Zero);
 
-        Vector2 gauge = origin + new Vector2(IconColumn, PanelHeight / 2);
+        float numberWidth = HudStyle.TextWidth("8888", HealthSize);
+        float barLeft = heart.X + 24f, barRight = left + _panelWidth - Pad - numberWidth - 10f;
+        DrawStyleBox(_barBack, new Rect2(barLeft - 2, middle - BarHeight / 2 - 2, barRight - barLeft + 4, BarHeight + 4));
+        float share = Math.Clamp(_health / (float)Math.Max(_maxHealth, 1), 0f, 1f);
+        if (share > 0f)
+        {
+            _barFill.BgColor = share > LowHealth ? Palette.Health.ToColor() : Palette.HitPierced.ToColor();
+            float width = MathF.Max(BarHeight, (barRight - barLeft) * share);   // never narrower than its round ends
+            DrawStyleBox(_barFill, new Rect2(barLeft, middle - BarHeight / 2, width, BarHeight));
+        }
+        HudStyle.DrawOutlined(this, new Vector2(barRight + 10f, middle + HealthSize * 0.36f), _healthText, HealthSize, HudStyle.Text, 4);
+    }
+
+    private void DrawGauges(float left, float middle)
+    {
+        float baseline = middle + NumberSize * 0.36f;
+        Vector2 gauge = new(left + IconColumn, middle);
         DrawSpeedometer(gauge);
         float x = gauge.X + 28f;
         HudStyle.DrawOutlined(this, new Vector2(x, baseline), _speedText, NumberSize, HudStyle.Text);
@@ -83,7 +145,7 @@ public partial class StatusPanel : Control
         HudStyle.DrawOutlined(this, new Vector2(x, baseline), "km/h", UnitSize, HudStyle.Text, 4);
 
         // The reload column starts after the widest possible speed text, so it doesn't move as the speed changes.
-        Vector2 shell = new(gauge.X + _shellOffset, origin.Y + PanelHeight / 2);
+        Vector2 shell = new(gauge.X + _shellOffset, middle);
         DrawShell(shell, _ready ? HudStyle.Accent : HudStyle.Text);
         x = shell.X + 20f;
         int size = _ready ? ReadySize : NumberSize;
@@ -91,6 +153,21 @@ public partial class StatusPanel : Control
         if (!_ready) HudStyle.DrawOutlined(this, new Vector2(x + HudStyle.TextWidth(_reloadText, NumberSize) + 6f, baseline), "秒", UnitSize, HudStyle.Text, 4);
     }
 
+    private void DrawChips(float left, float middle)
+    {
+        float x = left + Pad;
+        DrawChip(x, middle, _apText, _ammo == AmmoType.AP, _apLeft);
+        DrawChip(x + _chipWidth + ChipGap, middle, _heText, _ammo == AmmoType.HE, _heLeft);
+    }
+
+    private void DrawChip(float x, float middle, string text, bool loaded, int roundsLeft)
+    {
+        var rect = new Rect2(x, middle - ChipRow / 2 + 2, _chipWidth, ChipRow - 4);
+        DrawStyleBox(loaded ? _chipLoaded : _chip, rect);
+        Color color = roundsLeft == 0 ? Palette.HitBlocked.ToColor() : loaded ? HudStyle.Accent : HudStyle.Text;
+        HudStyle.DrawOutlinedCentered(this, rect.Position.X + rect.Size.X / 2, middle + ChipSize * 0.36f, text, ChipSize, color, 4);
+    }
+
     /// <summary>A half-dial with a needle.</summary>
     private void DrawSpeedometer(Vector2 c)
     {
@@ -112,6 +189,20 @@ public partial class StatusPanel : Control
         DrawSetTransform(Vector2.Zero);
     }
 
+    /// <summary>A small cartoon heart, about 18 px across, centred on the origin.</summary>
+    private static Vector2[] HeartShape()
+    {
+        var points = new Vector2[32];
+        for (int i = 0; i < points.Length; i++)
+        {
+            float t = Mathf.Tau * i / points.Length;
+            float x = 16f * MathF.Pow(MathF.Sin(t), 3);
+            float y = 13f * MathF.Cos(t) - 5f * MathF.Cos(2 * t) - 2f * MathF.Cos(3 * t) - MathF.Cos(4 * t);
+            points[i] = new Vector2(x, -y - 2f) * 0.55f;
+        }
+        return points;
+    }
+
     private static Vector2[] ScaleAll(Vector2[] points, float factor)
     {
         var scaled = new Vector2[points.Length];
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 0531260..ee9cc44 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -34,6 +34,7 @@ public partial class SandboxMain : Node3D
     private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead
     private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
     private const float DemoCameraPitch = 0.1f;     // --range-demo: looking over the tank at the target
+    private const float HeadHeight = 3.4f;          // health bars and damage numbers float this high over a tank's origin
 
     private readonly SandboxInput _input = new();
     private readonly InputCmd[] _commands = new InputCmd[1 + SandboxMap.TargetSpots.Count];
@@ -53,6 +54,8 @@ public partial class SandboxMain : Node3D
     private BattleFx _fx = null!;
     private Reticle _reticle = null!;
     private StatusPanel _status = null!;
+    private CombatPopups _popups = null!;
+    private HealthBars _bars = null!;
     private SniperVignette _vignette = null!;
     private double _accumulator;
     private double _time;
@@ -118,6 +121,12 @@ public partial class SandboxMain : Node3D
         AddChild(hud);
         _vignette = new SniperVignette();
         hud.AddChild(_vignette);
+        _bars = new HealthBars();
+        hud.AddChild(_bars);
+        _bars.Setup(_camera.Camera, _targets.Count);
+        _popups = new CombatPopups();
+        hud.AddChild(_popups);
+        _popups.SetCamera(_camera.Camera);
         _reticle = new Reticle();
         hud.AddChild(_reticle);
         _status = new StatusPanel();
@@ -217,6 +226,7 @@ public partial class SandboxMain : Node3D
         _targets.Sync(_sim, (float)delta);
         _shells.Sync(_sim.Shells, alpha, (float)delta);
         UpdateReticle(tank);
+        UpdateHealthBars();
         RunAutomation(delta);
     }
 
@@ -253,8 +263,13 @@ public partial class SandboxMain : Node3D
                 bool onRock = e.Position.Y > _sim.Field.Terrain.HeightAt(e.Position.X, e.Position.Z) + 0.5f;
                 _fx.Explosion(e.Position.ToGodot(), onRock);
                 break;
-            case GameEventType.TankHit when e.Ammo == AmmoType.AP:
-                _fx.Explosion(e.Position.ToGodot(), onRock: true, ArmorHitSize);   // HE gets its own ShellExploded
+            case GameEventType.TankHit:
+                if (e.Ammo == AmmoType.AP) _fx.Explosion(e.Position.ToGodot(), onRock: true, ArmorHitSize);   // HE gets its own ShellExploded
+                _popups.Verdict(e.Position.ToGodot(), e.Result);
+                if (e.Damage > 0) _popups.Damage(HeadOf(e.TargetTankId), e.Damage, splash: false);
+                break;
+            case GameEventType.SplashDamage:
+                _popups.Damage(HeadOf(e.TargetTankId), e.Damage, splash: true);
                 break;
         }
         _targets.OnEvent(e, _sim, _fx);
@@ -262,6 +277,25 @@ public partial class SandboxMain : Node3D
 
     private (sbyte Throttle, sbyte Steer, bool Fire) AutoDrive() => (1, (sbyte)(Math.Sin(_time * 0.4) > 0.5 ? 1 : 0), true);
 
+    private void UpdateHealthBars()
+    {
+        for (int i = 0; i < _targets.Count; i++)
+        {
+            TankState target = _sim.Tanks[1 + i];   // the targets follow the player's slot
+            _bars.Set(i, HeadOf(target), target.Health / (float)target.Spec.MaxHealth, !target.IsDestroyed);
+        }
+    }
+
+    private Vector3 HeadOf(in TankState tank) =>
+        (TankPose.Compute(tank, _sim.Field.Terrain).Position + SN.Vector3.UnitY * HeadHeight).ToGodot();
+
+    private Vector3 HeadOf(int tankId)
+    {
+        foreach (TankState tank in _sim.Tanks)
+            if (tank.Id == tankId) return HeadOf(tank);
+        return Vector3.Zero;
+    }
+
     /// <summary>--range-demo: stand still, lay the gun on the demo's aim point, fire once (or keep firing) after the warm-up.</summary>
     private (sbyte Throttle, sbyte Steer, bool Fire) DemoInput(RangeDemo demo)
     {
@@ -316,11 +350,25 @@ public partial class SandboxMain : Node3D
         Camera3D cam = _camera.Camera;
         Vector3 point = impact.ToGodot();
         Vector2? screen = !hasImpact || cam.IsPositionBehind(point) ? null : cam.UnprojectPosition(point);
-        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f);
-        _status.UpdateState(tank.Speed * 3.6f, tank.Reload);
+        _reticle.UpdateState(screen, 1f - tank.Reload / tank.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f, AimCirclePixels(tank, launch.Origin, point));
+        _status.UpdateState(tank.Speed * 3.6f, tank.Reload, tank.Health, tank.Spec.MaxHealth, tank.Ammo, tank.ApLeft, tank.HeLeft);
         _vignette.Visible = _camera.Sniper;
     }
 
+    /// <summary>
+    /// The aim circle's radius at the predicted impact, as seen by the camera: the circle is quoted at 100 m from the gun,
+    /// so it measures <c>radius × gun distance / 100</c> there, and shrinks with the camera's distance to that point.
+    /// </summary>
+    private float AimCirclePixels(in TankState tank, SN.Vector3 muzzle, Vector3 impact)
+    {
+        Camera3D cam = _camera.Camera;
+        float fromCamera = cam.GlobalPosition.DistanceTo(impact);
+        if (fromCamera < 0.1f) return 0f;
+        float worldRadius = Dispersion.Radius(tank.Spec, tank.Bloom) * SN.Vector3.Distance(muzzle, impact.ToSim()) / Dispersion.ReferenceRange;
+        float focal = GetViewport().GetVisibleRect().Size.Y / 2f / MathF.Tan(Mathf.DegToRad(cam.Fov) / 2f);
+        return worldRadius / fromCamera * focal;
+    }
+
     private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
     {
         Position = SN.Vector2.Lerp(a.Position, b.Position, t),
@@ -328,6 +376,7 @@ public partial class SandboxMain : Node3D
         TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
         GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
         Speed = a.Speed + (b.Speed - a.Speed) * t,
+        Bloom = a.Bloom + (b.Bloom - a.Bloom) * t,
     };
 
     private void RunAutomation(double delta)
PATCH
```


- [ ] **Step 4：运行测试，编译客户端，生成 .uid**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "通过|失败"; dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Error\(s\)"; importall; git status --short | grep uid`
Expected: Art `通过: 187`；客户端 `0 Error(s)`；出现 `PopupCurves.cs.uid`、`CombatPopups.cs.uid`、`HealthBars.cs.uid`。

- [ ] **Step 5：截图验收**

```bash
demoshot t11-pen pen hit:0.3 6 --sniper
demoshot t11-ricochet ricochet hit:0.3 6 --sniper
demoshot t11-nopen nopen hit:0.3 6 --sniper
demoshot t11-he he hit:0.5 12 --sniper
godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --shot-after fire:0.15 \
  --screenshot "$PWD/build/shots/t11-moving.png" --quit-after 12 2>&1 | grep -E "Screenshot|ERROR|Exception"
```

逐张用 Read 打开核对：
- `t11-pen`：靶车上方红色描边大字“击穿！”，其上方红色“-数字”（165～275 之间）；靶车头顶血条变短；准星圈没有压住大字；
- `t11-ricochet`：黄色“跳弹！”，没有伤害数字，血条满；
- `t11-nopen`：灰色“未击穿”，没有伤害数字；
- `t11-he`：灰色“未击穿”和红色“-41”；左下角弹药栏“2 高爆 14”被黄色描边高亮；
- `t11-moving`：左下角面板三行（血条 900、速度和装填、两个弹药块，“1 穿甲 39”高亮）；准星实心圈里有一个黄色小圈（散布圈），远处几辆靶车头顶有绿色血条。

- [ ] **Step 6：提交，并结束批次 D**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): health and ammo in the status panel, aim circle, hit verdicts, damage numbers and health bars"
```

批次 D 结束：规格审查 → 代码质量审查 → `docs/PROGRESS.md` 记一行（Art 187）。

---

### Task 12：截图与帧率

设计 §5 的截图和帧率验收。全部测试先跑一遍。

- [ ] **Step 1：全部测试**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: Sim 258、Art 187、Core 62、Net 50、Update 42，全部通过。

- [ ] **Step 2：验收截图**

```bash
demoshot final-pen pen hit:0.3 6 --sniper
demoshot final-ricochet ricochet hit:0.3 6 --sniper
demoshot final-nopen nopen hit:0.3 6 --sniper
demoshot final-damage pen hit:0.8 6 --sniper
demoshot final-toss kill kill:0.45 45 --camera-preset target
demoshot final-wreck kill kill:4 50 --camera-preset target
godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --autodrive --shot-after fire:0.15 \
  --screenshot "$PWD/build/shots/final-bloom-open.png" --quit-after 12 2>&1 | grep -E "Screenshot|ERROR|Exception"
godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo pen --sniper \
  --screenshot "$PWD/build/shots/final-bloom-closed.png" --screenshot-at 2.8 --quit-after 3.2 2>&1 | grep -E "Screenshot|ERROR|Exception"
```

对照设计 §5.2 逐张核对：击穿、跳弹、未击穿各一张；伤害数字（`final-damage` 里“-数字”已经飘到靶车上方）；炮塔飞起的瞬间；焦黑的残骸；新的左下角面板（任意一张）；散布圈扩大（`final-bloom-open`，行进中刚开炮）和收拢（`final-bloom-closed`，静止 2.8 秒、狙击镜 2×，黄色小圈明显小于前者）。

- [ ] **Step 3：测帧**（窗口必须在最前面，期间不要切走）

```bash
fpslog fps-spin spin 35
fpslog fps-orbit orbit 35
```

Expected: 去掉前面被限速的 60 帧行之后，两个机位都稳定在 60 帧以上（写计划时实测 210～330 帧）。把结果记进 `docs/PROGRESS.md`。

- [ ] **Step 4：确认 Godot 都已退出**

Run: `pgrep -fl 'MacOS/Godot' || echo none`
Expected: `none`。

---

### Task 13：用户试玩与收尾

- [ ] **Step 1：交给用户试玩**

告诉用户启动命令（窗口模式，鼠标瞄准，左键开炮，右键自由视角，Shift 狙击镜，滚轮缩放，1/2 换弹种，R 补弹，Esc 释放鼠标）：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage2a
dotnet build src/Tank.Client/Tank.Client.csproj && godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn
```

请用户确认设计 §5.4：打靶车能看到击穿、跳弹、未击穿三种结果和伤害数字；击毁时炮塔会飞起；散布的变化明显。另外请用户特别看两处：
1. 散布圈按真实比例画，第三人称下只是准星里的一个小黄圈（狙击镜里大一些），够不够明显；如果不够，可选“放大显示”（不改仿真）；
2. 命中大字的位置、大小和停留时间是否合适。

- [ ] **Step 2：按反馈调整**

每条反馈单独修改、截图确认、提交，并在 `docs/PROGRESS.md` 的节点记录里追加一行。

- [ ] **Step 3：合入 main**

用户确认后：

```bash
cd /Users/murphy/Desktop/dev/BattleCity
git merge --squash feat/stage2a-combat
git commit -m "feat: stage 2A combat — armour, AP/HE, dispersion, practice targets, wrecks and combat HUD"
```

然后更新 `docs/PROGRESS.md`（阶段总览里 2A 标为已合入，写上提交号；“当前位置”改为阶段 2B），一起提交到 main。不推送远程，除非用户要求。删除写计划用的临时 worktree 和分支：

```bash
git worktree remove /Users/murphy/Desktop/dev/BattleCity-stage2a-draft --force
git branch -D draft/stage2a-combat
```

---

## 附：计划自查

- **设计覆盖**：§3.1 车型数据 → Task 2；§3.2 坦克状态 → Task 2；§3.3 输入与换弹 → Task 5；§3.4 散布 → Task 4、5；§3.5 命中检测 → Task 1、6、7；§3.6 穿甲弹 → Task 3、7；§3.7 高爆弹 → Task 3、7；§3.8 血量、击毁、复活、补给 → Task 5、7；§3.9 事件 → Task 3、5、7；§4.1 靶车 → Task 8、10；§4.2 外观与表现 → Task 9、10、11；§5 验收 → 各任务的测试、Task 12、13。
- **测试数衔接**：Sim 144 → 160 → 167 → 179 → 185 → 194 → 205 → 221 → 258；Art 178 → 183 → 187。写计划时在草稿里逐个提交检出验证过。
- **命名一致**：`OrientedBox`/`BoxFace`、`ShotRandom.Seed/Unit/Range`、`ArmorBox`/`Armor`/`ArmorFace`/`HitPart`、`ShellSpec`、`BloomFactors`、`Penetration.Ap/HeDirect/Splash`、`Dispersion.Target/Step/Radius/Deflect`、`ArmorHit.Find`、`TankPose.HullBox/TurretBox`、`Simulation.Revive/Restock/TankRef`、`TurretToss.At`、`PopupCurves.*` 在定义它们的任务之后才被使用。
