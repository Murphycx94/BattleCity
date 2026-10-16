# 阶段 2C：可破坏物 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 加上砖墙段、石墙段和栅栏：墙被炮弹打成瓦砾，栅栏被打倒或撞倒，草丛被打掉；瓦砾可以通行但减速；重坦高速撞穿砖墙；试驾场西边摆一个院子（院里第 7 辆靶车）和几处掩体；T 键恢复所有物件；墙的血条、伤害数字和倒塌、倒下、打掉的特效。

**Architecture:** 物件的形状（`Prop`）不变，状态（是否立着、剩余耐久）放进 `Battlefield`，只有仿真能改。射线、坦克碰撞、`BushesNear` 都按当前状态查询，所以炮弹、瞄准射线和准星预测不用改调用方式。炮弹阶段在原来“找最近的命中”之后，先打掉线段上停下之前穿过的栅栏和草丛，打中墙时按弹种扣耐久再爆炸，高爆弹爆炸时再波及墙、栅栏、草丛（`Simulation.Props.cs`、`Combat/WallDamage.cs`）。`TankMotion.Drive` 在推出碰撞之前撞倒栅栏、让高速重坦撞穿砖墙，算目标速度时在瓦砾上打 0.7 折。墙按“一条线”写（`WallLine`），展开成 2 m 一段的物件。客户端每帧按状态显示墙、瓦砾、倒下的栅栏和草丛（`WallViews`、`BushField.SyncStanding`），特效和血条按事件和状态做。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、Godot 4.7.2 .NET 版（Forward+，macOS 上走 Metal）

**设计依据：** `docs/superpowers/specs/2026-10-16-stage2c-destructibles-design.md`（全文）；上级文档 `docs/superpowers/specs/2026-10-13-third-person-tank-design.md` §3.2、§4.2、§5.4、§5.5。

---
## 写计划时已核实、与设计的差异

写这份计划时，先在临时 worktree（`/Users/murphy/Desktop/dev/BattleCity-stage2c-draft`，分支 `draft/stage2c-destructibles`，从设计提交 `eb38ae5` 拉出）里按任务顺序把下文全部实现了一遍，一个任务一个提交。下文的代码和补丁由脚本从这些提交直接导出，没有手抄。实测结果：

- 每个提交单独检出后：客户端编译 0 警告 0 错误，Sim 和 Art 测试全过。Sim 测试数依次是 338、350、377、392、396、452、452、452、452（Task 1～9），Art 依次是 342、342、342、342、342、374、384、384、436；Core 62、Net 50、Update 42 不变。
- 截图逐张看过：院子全景（东面石墙、三面砖墙、西面栅栏门、院里靶车）；中坦一发穿甲弹打在石墙上（伤害数字、墙面上的小血条）；TD 两发高爆弹打穿石墙（缺口里有瓦砾，透过缺口看到靶车）；栅栏门北边那块被炮弹打倒，向院里平躺；重坦撞穿北墙（一大团砖尘，速度减半到 13 km/h，停在缺口里没撞到靶车）；中坦被北墙挡住；草丛被打掉（叶子从顶上喷出）。
- 帧率（关垂直同步，orbit）84～145 帧；2B 的版本同条件 137～146 帧，绘制次数和图元数基本相同（墙段多十几次绘制）。
- 草稿经过一轮代码审查，没有严重问题。审查还实测了：沙盘上四辆车往院子里连续打穿甲弹和高爆弹 2700 tick，热身后每 tick 不分配内存。按审查意见改了：
  - **炮管顶着的墙在同一 tick 已经倒了**（被先结算的炮弹打塌、或被后面槽位的车撞倒），原来还会对着瓦砾再报一次“墙受伤”，现在重新看炮管，没有别的东西挡着就让炮弹从炮口飞出（Task 3 的 `GoOff`，有测试：两辆重坦隔着一段砖墙同时开高爆弹）；
  - **在草丛里开炮会打掉自己藏身的草丛**：总体设计 §6 的隐蔽规则默认草丛还在，所以改为线段起点所在的草丛或栅栏不算被穿过，设计文档已同步（Task 3）；
  - 补测试：炮弹停下之后的栅栏和草丛不倒；同一 tick 里前一发打塌的墙后一发能穿过；打塌墙时 `PropDestroyed` 在 `ShellExploded` 之前；原地转向撞倒栅栏；打墙和撞墙时每 tick 不分配内存（并确认计量期间真的打中、撞倒了东西）；墙的直接伤害和打坦克时的伤害随机数一致；低速重坦全程没超过 15 km/h；
  - `PropDamaged` 直接命中时带上炮弹速度、溅射时为零，客户端据此区分（原来按命中点离墙多远猜，在两段墙的接缝处会猜错）；
  - 炮弹穿过时的推开方向不再可能是零向量；`PropDestroyed` 带上弹种；事件表初始容量加到 `MaxTanks × 4`；
  - 瓦砾碎块的深浅原来被着色器又映射了一次，几乎看不出差别，改为直接乘顶点色；碎块特效池从 6 个加到 12 个；
  - 撞墙演示的油门安排放进 `RangeDemo.Throttle`，演示脚本和测试共用；
  - `Battlefield.PropById` 从客户端的 Task 8 挪到 Task 2；几处注释。

与设计文字不同、但有意为之的地方（执行时不要“改回去”）：

- **栅栏和草丛的耐久记为 1**（总体设计写 40 和 1）：炮弹穿过、爆炸波及、坦克碰到都直接毁掉它们，耐久用不上。
- **瓦砾减速按“车体压上去”算**：车体占地和瓦砾范围（墙段厚度方向两边各加宽 0.5 m）重叠就减速，不是只看车体中心。只看中心的话，过一段 0.6 m 厚的墙只减速一瞬间。
- **墙的血条**比靶车的小（44 × 8 像素），放在墙面中间、墙顶下 1 m（设计原文是“墙顶上方，样式同靶车”，放在墙顶会和院里靶车的血条叠在一起），设计文档已同步。
- **截图演示**：`ram` 开 2.6 秒（设计原文 2.5 秒；2.6 秒正好撞进院子又碰不到靶车）；`fence` 从西偏北打（正西停车，车和镜头都顶在后面的大石头上）；另加 `gate` 机位近看栅栏门。设计文档已同步。
- **离通道 6 m** 按“墙段的外接圆在通道半宽 6 m 之外”算，和岩石、草丛用的规则一样。

## 留给以后的问题（不在本阶段处理）

- 炮管伸出去压着一块栅栏、车体却没碰到它时（TD 的炮管伸出车头 2.8 m），开炮不会打倒这块栅栏：从炮耳到炮口这一截不检查栅栏和草丛。车再往前一点就会撞倒它。
- 物件状态取的是最新一个 tick，而坦克和炮弹画的是插值后、晚一个 tick 的位置，所以墙会比炮弹到达早约 33 ms 倒下。和现有的爆炸特效一样，看不出来。
- `TankMotion.Drive` 会改动共用的 `Battlefield`。阶段 4 的客户端预测如果直接调用它，会在本地撞倒物件，要另想办法（例如预测时不撞倒，等服务器的物件状态）。
- 物件状态的联网同步（总体设计 §8：物件 id → 耐久的增量）留给阶段 4；视线和点亮留给阶段 3，本阶段只保证被毁的物件不再挡东西。

---
## 执行前须知

- **工作目录**：直接在现有 worktree 里执行，不要新建：`/Users/murphy/Desktop/dev/BattleCity-stage2c`（分支 `feat/stage2c-destructibles`）。下文所有路径都相对于它。
- 每个 shell 先执行下面这段。`dotnet` 不在默认 PATH 里；`godot_run` 保证 Godot 无论如何都会被强制结束（曾有一次 `--quit-after` 卡住 15 小时）。

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage2c
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
# atshot NAME SECONDS [MORE ARGS]: one screenshot SECONDS in, no camera preset
atshot() {
  local name=$1 at=$2; shift 2
  godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --screenshot "$PWD/build/shots/$name.png" \
    --screenshot-at "$at" --quit-after "$(echo "$at + 0.5" | bc)" "$@" 2>&1 | grep -E "Screenshot|ERROR|SHADER|Exception|WARNING"
}
# fpslog NAME PRESET SECONDS: frame-time log with vsync off, also kept in build/shots/NAME.log
fpslog() {
  godot_run $(( $3 + 30 )) --path src/Tank.Client --disable-vsync res://scenes/Sandbox.tscn -- --camera-preset "$2" \
    --fps-log --quit-after "$3" 2>&1 | tee "build/shots/$1.log" | grep -E "fps-log|ERROR|SHADER|Exception"
}
# importall: let Godot import resources and generate .uid files for new scripts
importall() { godot_run 240 --headless --path src/Tank.Client --import > /dev/null 2>&1; }
```

- **第一次截图前先运行一次 `importall`**：这个 worktree 是新建的，还没有 Godot 的资源导入缓存（`.godot/imported`），不导入的话字体加载失败，界面文字全部缺失。
- **补丁的用法**：修改已有文件的步骤给的是 `git apply` 补丁，在仓库根目录原样运行即可（heredoc 用了 `'PATCH'`，内容不会被 shell 展开）。打不上时先 `git status` 看是不是漏了前面的步骤，不要手工改。新建的文件给完整内容，用 Write 工具写入。
- **从命令行启动的 Godot 不会编译 C#**：改了 `.cs` 之后，截图前一定先 `dotnet build src/Tank.Client/Tank.Client.csproj`，否则跑的还是旧程序集（例如 `--tank heavy` 会被当成写错的名字，开出来的还是中坦）。
- **截图**：
  - 截图存在 `build/shots/`（已被 `.gitignore` 忽略）。
  - 每张截图都**必须用 Read 工具打开**，逐条核对该步列出的要求；不符合就先修再继续。
  - 输出里出现 `SHADER ERROR`、`ERROR:` 或 `Exception` 时先修复。没有出现 `Screenshot saved` 说明出错或事件没发生，看完整输出。
  - 游戏窗口会抢键盘焦点。带 `--camera-preset` 或 `--range-demo` 时游戏忽略所有输入；`atshot` 不带机位，截图期间不要动鼠标，否则镜头会跟着转。
- **测帧**：macOS 会把不在最前面的窗口限制在 60 帧，日志会整齐地显示 `60 fps`，这不是真实开销；前 7～20 秒的数据不算，至少跑 30 秒。测帧时不截图。
- **每个任务结束前**都运行一次 `pgrep -fl 'MacOS/Godot'`，确认本任务启动的 Godot 都已退出。
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`。`Tank.Sim` 和测试项目里任何警告都会让构建失败。`Tank.Art.Tests` 会编译客户端里不依赖 Godot 的源码（网格代码、外观数据、`TurretToss`、`LaunchOptions.cs`、`RangeDemo.cs`），它们同样不能有警告、不能引用 Godot。本计划新建的 `Art/Mesh/WallShapes.cs` 也在其中。客户端项目关掉了该选项，但也要保持 0 警告。
- 新增 `src/Tank.Client` 下的脚本后，提交前运行 `importall`，把生成的 `.cs.uid` 一起提交。下文的 `git add` 按目录添加，会带上它们。
- **不要改**：旧 Battle City 代码（`src/Tank.Core`、`src/Tank.Net`、`src/Tank.Server`，客户端的 `scripts/Bootstrap/` 中除 `LaunchOptions.cs` 以外的文件、`scripts/UI/`、`scripts/View/`、`scripts/Controls/`）和 `project.godot`。
- **坐标约定**（同前几个阶段）：Y 向上，−Z 为前方；偏航 0 朝 −Z，正值向左转；`InputCmd.Steer` 为 +1 时向右转；车体空间原点在接地中心。`TankState.Position` 是二维 `(x, z)`。
- **测试项目与数量**：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。开始前是 Core 62、Sim 323、Net 50、Update 42、Art 342。每个任务末尾写明了做完之后 Sim 和 Art 的测试数；对不上就说明漏了或多了测试。
- 提交信息用约定式前缀（feat/fix/test/docs/chore）。每完成一批，在 `docs/PROGRESS.md` 的“2C 节点记录”里追加一行（任务、提交号、测试数）。
- 分支最后用 squash merge 并入 `main`，不推送远程，除非用户要求。
## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Maps/Prop.cs` | 修改 | 新种类 `BrickWall`、`StoneWall`、`Fence`；`MaxDurability`、`IsDestructible`、`IsWall`、`StopsShells`、`RubbleFootprint`、`DistanceTo` |
| `Maps/WallLine.cs` | 新建 | 一条线展开成 2 m 一段的墙段或栅栏；各种类的厚度、高度和下埋深度 |
| `Maps/Battlefield.cs` | 修改 | 每个物件的状态（是否立着、剩余耐久）；`IsStanding`、`Durability`、`PropById`；`Raycast` 只算岩石和立着的墙；`BushesNear` 跳过被打掉的草丛；仿真用的按下标访问和改状态的方法 |
| `Geometry/Intersect.cs` | 修改 | 线段对竖直圆柱（草丛） |
| `Combat/WallDamage.cs` | 新建 | 墙受到的直接命中和溅射伤害 |
| `Combat/Ballistics.cs` | 修改 | 炮管顶住的是哪个物件（`Launch.BlockPropId`） |
| `GameEvent.cs` | 修改 | `PropDamaged`、`PropDestroyed`，事件末尾新增 `PropId` |
| `Simulation.cs`、`Simulation.Shells.cs` | 修改 | 炮弹阶段接上物件规则；炮管顶进墙里等于直接命中；每辆车开完后报告它撞倒的物件 |
| `Simulation.Props.cs` | 新建 | 炮弹打掉穿过的栅栏和草丛、打墙、爆炸波及物件；报告撞倒的物件；`RestoreProps` |
| `Vehicles/TankSpec.cs`、`TankCatalog.cs` | 修改 | `BrickWallRamSpeed`：重坦 15 km/h，其他车无穷大 |
| `Vehicles/TankMotion.cs` | 修改 | 推出碰撞前撞倒栅栏和（高速重坦）砖墙；碰撞只算立着的物件；瓦砾上最高速 × 0.7 |
| `Maps/SandboxMap.cs` | 修改 | `WallLines`：院子和几处掩体；第 7 辆靶车 `YardTarget` |

**客户端 `src/Tank.Client`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `scripts/Art/Mesh/WallShapes.cs` | 新建 | 墙段、栅栏、瓦砾堆的网格；栅栏倒下的姿态（`FenceFall`） |
| `shaders/wall.gdshader` | 新建 | 墙的卡通着色：砖缝、石缝画在物体空间里；瓦砾、木头用纯色 |
| `scripts/Art/Palette.cs` | 修改 | 砖、砖缝、石、石缝、木头、砖尘的颜色 |
| `scripts/World/WallViews.cs` | 新建 | 每帧按状态显示墙或瓦砾，栅栏被打倒时朝被推开的方向倒下 |
| `scripts/World/BushField.cs` | 修改 | 被打掉的草丛缩成看不见，恢复后再显示 |
| `scripts/World/SimConvert.cs` | 修改 | `Matrix4x4` 转 Godot 的 `Transform3D` |
| `scripts/Sandbox/SandboxWorld.cs` | 修改 | 建墙的外观，每帧同步墙和草丛 |
| `scripts/Sandbox/SandboxInput.cs`、`SandboxMain.cs` | 修改 | T 键恢复物件；事件交给墙的外观和物件特效 |
| `scripts/Combat/BattleFx.cs` | 修改 | 碎块和尘土（`Debris`） |
| `scripts/Combat/PropFx.cs` | 新建 | 墙被打中、墙塌、栅栏倒、草丛被打掉各用什么特效 |
| `scripts/Overlay/HealthBars.cs`、`scripts/Sandbox/SandboxHud.cs` | 修改 | 血条大小可调；受损的墙显示小血条，墙被打中弹出伤害数字 |
| `scripts/Sandbox/RangeDemo.cs`、`SandboxAutomation.cs`、`scripts/Bootstrap/LaunchOptions.cs`、`scripts/Cameras/CameraPresets.cs` | 修改 | 截图用的演示 `wall`、`breach`、`fence`、`ram`、`bush`；`--shot-after prop|down`；`yard`、`gate` 机位 |

**测试**：`tests/Tank.Sim.Tests` 新增 `WallLineTests`、`PropStateTests`、`ShellPropTests`、`RamTests`、`RestorePropsTests`、`SandboxWallsTests`，改 `GeometryTests`（线段对圆柱）、`TargetSpotTests`（7 辆靶车）；`tests/Tank.Art.Tests` 新增 `WallShapesTests`，改 `PaletteTests`、`RangeDemoTests`、`LaunchOptionsTests`。

---
## 任务一览与批次

| 批次 | 任务 | 做完后 Sim / Art 测试数 |
|---|---|---|
| A 仿真规则 | 1 物件种类和墙线；2 物件状态；3 炮弹对物件；4 坦克对物件；5 恢复 | 338 / 342 → 350 / 342 → 377 / 342 → 392 / 342 → 396 / 342 |
| B 试驾场摆放 | 6 院子、掩体和第 7 辆靶车 | 452 / 374 |
| C 外观 | 7 墙、栅栏、瓦砾的外观，草丛被打掉，T 键；8 特效、墙的血条和伤害数字 | 452 / 384 → 452 / 384 |
| D 截图演示 | 9 打墙、打穿、栅栏门、撞墙、草丛五个演示，`yard`、`gate` 机位 | 452 / 436 |
| E 验收 | 10 截图与帧率；11 用户试玩与收尾 | — |

每批做完先过规格审查，再过代码质量审查，然后在 `docs/PROGRESS.md` 记一行。

---

### Task 1：物件种类和墙线

设计 §2.1、§3.1。`PropKind` 加上砖墙段、石墙段和栅栏；`Prop` 仍是不可变的形状数据，新增按种类推出的规则：耐久（砖墙 300、石墙 900，栅栏和草丛 1，岩石 0 表示不可破坏）、是不是墙、挡不挡炮弹（岩石和墙挡）、瓦砾的范围（墙段在厚度方向两边各加宽 0.5 m）。`IsSolid` 的含义不变：完好时挡坦克，栅栏也算。墙按“一条线”写（`WallLine`），展开成 2 m 一段，长度方向沿占地的 `AxisRight`，厚度方向沿 `AxisForward`；每段和岩石一样往地下埋 0.3 m，顶面在地面以上 3 m（栅栏 1.5 m），地面高度取段的中心。

**Files:**
- Modify: `src/Tank.Sim/Maps/Prop.cs`
- Create: `src/Tank.Sim/Maps/WallLine.cs`
- Test: 新建 `tests/Tank.Sim.Tests/WallLineTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/WallLineTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class WallLineTests
{
    [Fact]
    public void Lays_two_metre_segments_end_to_end_along_the_line()
    {
        Prop[] segments = new WallLine(PropKind.BrickWall, new Vector2(100, 100), new Vector2(106, 100)).Segments(TestWorld.Flat(), 7).ToArray();

        Assert.Equal(new[] { 7, 8, 9 }, segments.Select(s => s.Id));
        Assert.Equal(new[] { 101f, 103f, 105f }, segments.Select(s => s.Footprint.Center.X));
        Assert.All(segments, s =>
        {
            Assert.Equal(PropKind.BrickWall, s.Kind);
            Assert.Equal(100f, s.Footprint.Center.Y, 1e-4f);
            Assert.Equal(new Vector2(1f, 0.3f), s.Footprint.HalfExtents);
            Assert.Equal(1f, s.Footprint.AxisRight.X, 1e-5f);   // long along the line, thin across it
        });
    }

    [Fact]
    public void A_slanted_line_turns_its_segments_to_follow_it()
    {
        var from = new Vector2(50, 50);
        var to = from + Vector2.Normalize(new Vector2(3, -4)) * 4f;
        Prop[] segments = new WallLine(PropKind.StoneWall, from, to).Segments(TestWorld.Flat(), 1).ToArray();

        Assert.Equal(2, segments.Length);
        Assert.All(segments, s => Assert.Equal(1f, Vector2.Dot(s.Footprint.AxisRight, Vector2.Normalize(to - from)), 1e-5f));
        Assert.Equal(from + (to - from) * 0.25f, segments[0].Footprint.Center);
        Assert.Equal(new Vector2(1f, 0.5f), segments[0].Footprint.HalfExtents);
    }

    [Theory]
    [InlineData(PropKind.BrickWall, 0.6f, 3f)]
    [InlineData(PropKind.StoneWall, 1f, 3f)]
    [InlineData(PropKind.Fence, 0.2f, 1.5f)]
    public void Each_kind_has_its_size_and_stands_on_the_ground(PropKind kind, float thickness, float height)
    {
        var line = new WallLine(kind, new Vector2(100, 60), new Vector2(100, 64));   // north–south on a slope rising north
        foreach (Prop s in line.Segments(TestWorld.RampNorth(0.1f), 1))
        {
            float ground = TestWorld.RampNorth(0.1f).HeightAt(s.Footprint.Center.X, s.Footprint.Center.Y);
            Assert.Equal(thickness / 2, s.Footprint.HalfExtents.Y, 1e-5f);
            Assert.Equal(ground - WallLine.Sink, s.Bottom, 1e-4f);
            Assert.Equal(ground + height, s.Top, 1e-4f);
        }
    }

    [Theory]
    [InlineData(5f)]
    [InlineData(0f)]
    public void Rejects_a_line_that_is_not_a_whole_number_of_segments(float length) =>
        Assert.Throws<InvalidOperationException>(() =>
            new WallLine(PropKind.BrickWall, new Vector2(100, 100), new Vector2(100 + length, 100)).Segments(TestWorld.Flat(), 1).ToArray());

    [Theory]
    [InlineData(PropKind.Rock)]
    [InlineData(PropKind.Bush)]
    public void Only_walls_and_fences_come_in_lines(PropKind kind) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WallLine(kind, new Vector2(100, 100), new Vector2(102, 100)).Segments(TestWorld.Flat(), 1).ToArray());

    [Theory]
    [InlineData(PropKind.Rock, 0, true, false, true)]
    [InlineData(PropKind.Bush, 1, false, false, false)]
    [InlineData(PropKind.BrickWall, 300, true, true, true)]
    [InlineData(PropKind.StoneWall, 900, true, true, true)]
    [InlineData(PropKind.Fence, 1, true, false, false)]
    public void Kinds_have_their_rules(PropKind kind, int durability, bool solid, bool wall, bool stopsShells)
    {
        var prop = new Prop(1, kind, new Obb2(Vector2.Zero, Vector2.One, 0), 0, 1);
        Assert.Equal(durability, prop.MaxDurability);
        Assert.Equal(durability > 0, prop.IsDestructible);
        Assert.Equal(solid, prop.IsSolid);
        Assert.Equal(wall, prop.IsWall);
        Assert.Equal(stopsShells, prop.StopsShells);
    }

    [Fact]
    public void Rubble_spreads_half_a_metre_past_both_faces_of_its_wall()
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(100, 100), new Vector2(102, 100)).Segments(TestWorld.Flat(), 1).Single();
        Obb2 rubble = wall.RubbleFootprint;
        Assert.Equal(wall.Footprint.Center, rubble.Center);
        Assert.Equal(new Vector2(1f, 0.8f), rubble.HalfExtents);
        Assert.True(rubble.Contains(new Vector2(101, 100.79f)) && !rubble.Contains(new Vector2(101, 100.81f)));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`WallLine`、`PropKind.BrickWall`、`Prop.MaxDurability` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/Prop.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/Prop.cs b/src/Tank.Sim/Maps/Prop.cs
index 2363cfd..0d9b33d 100644
--- a/src/Tank.Sim/Maps/Prop.cs
+++ b/src/Tank.Sim/Maps/Prop.cs
@@ -7,13 +7,48 @@ public enum PropKind : byte
     Rock = 0,
     /// <summary>Tall grass a tank can hide in: no collision, shells and aim rays pass through. Round footprint.</summary>
     Bush = 1,
+    /// <summary>A 2 m segment of brick wall: stops tanks and shells until it is shot (or a fast heavy tank rams it) to rubble.</summary>
+    BrickWall = 2,
+    /// <summary>A 2 m segment of stone wall: like brick, but three times as tough and no tank can ram it down.</summary>
+    StoneWall = 3,
+    /// <summary>A 2 m wooden fence panel: stops tanks until one touches it; shells fly through and knock it down.</summary>
+    Fence = 4,
 }
 
-/// <summary>A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>.</summary>
+/// <summary>
+/// A static object: a ground footprint extruded from <see cref="Bottom"/> up by <see cref="Height"/>. Walls and fences run
+/// along the footprint's <see cref="Obb2.AxisRight"/> and are thin along <see cref="Obb2.AxisForward"/> (see <see cref="WallLine"/>).
+/// Whether it still stands, and its hit points, live in <see cref="Battlefield"/>.
+/// </summary>
 public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float Bottom, float Height)
 {
+    /// <summary>Rubble spreads this far beyond each face of the wall it was.</summary>
+    public const float RubbleSpread = 0.5f;
+
     public float Top => Bottom + Height;
 
-    /// <summary>Blocks tanks, shells and rays. Bushes don't; they only hide what is inside them.</summary>
+    /// <summary>Blocks tanks while standing: rocks, walls and fences. Bushes don't; they only hide what is inside them.</summary>
     public bool IsSolid => Kind != PropKind.Bush;
+
+    public bool IsWall => Kind is PropKind.BrickWall or PropKind.StoneWall;
+
+    /// <summary>Stops shells and rays while standing: rocks and walls. Shells fly through fences and bushes, knocking them down.</summary>
+    public bool StopsShells => Kind == PropKind.Rock || IsWall;
+
+    /// <summary>
+    /// Hit points when intact: a brick wall 300, a stone wall 900; fences and bushes 1, since anything that reaches them
+    /// knocks them down; rocks 0: they can't be destroyed.
+    /// </summary>
+    public int MaxDurability => Kind switch
+    {
+        PropKind.BrickWall => 300,
+        PropKind.StoneWall => 900,
+        PropKind.Fence or PropKind.Bush => 1,
+        _ => 0,
+    };
+
+    public bool IsDestructible => MaxDurability > 0;
+
+    /// <summary>Where a wall's rubble lies once it is down: its footprint widened by <see cref="RubbleSpread"/> on both faces.</summary>
+    public Obb2 RubbleFootprint => Footprint with { HalfExtents = Footprint.HalfExtents + new System.Numerics.Vector2(0, RubbleSpread) };
 }
PATCH
```

新建 `src/Tank.Sim/Maps/WallLine.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// A straight run of wall or fence from <see cref="Start"/> to <see cref="End"/> on the ground (X is world X, Y is world Z),
/// the way maps place walls (design §3.2). <see cref="Segments"/> lays it out as separate 2 m props, each with its own id.
/// </summary>
public readonly record struct WallLine(PropKind Kind, Vector2 Start, Vector2 End)
{
    public const float SegmentLength = 2f;

    /// <summary>Each segment's base sits this far under the ground at its centre, so it never floats on uneven ground.</summary>
    public const float Sink = 0.3f;

    private const float LengthTolerance = 0.01f;

    /// <summary>How thick a segment of <paramref name="kind"/> is: brick 0.6 m, stone 1 m, fence 0.2 m.</summary>
    public static float Thickness(PropKind kind) => kind switch
    {
        PropKind.BrickWall => 0.6f,
        PropKind.StoneWall => 1f,
        PropKind.Fence => 0.2f,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only walls and fences are laid in lines."),
    };

    /// <summary>How tall a segment of <paramref name="kind"/> stands above the ground: walls 3 m, fences 1.5 m.</summary>
    public static float Height(PropKind kind) => kind switch
    {
        PropKind.BrickWall or PropKind.StoneWall => 3f,
        PropKind.Fence => 1.5f,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Only walls and fences are laid in lines."),
    };

    /// <summary>How many 2 m segments the line holds. Throws unless its length is a whole number of them (to within 1 cm).</summary>
    public int SegmentCount
    {
        get
        {
            float length = Vector2.Distance(Start, End);
            int count = (int)MathF.Round(length / SegmentLength);
            if (count < 1 || MathF.Abs(length - count * SegmentLength) > LengthTolerance)
                throw new InvalidOperationException($"Wall line {Start}→{End} is {length:F2} m long, not a whole number of {SegmentLength} m segments.");
            return count;
        }
    }

    /// <summary>
    /// The line's segments from <see cref="Start"/> on, numbered <paramref name="firstId"/>, <paramref name="firstId"/> + 1, …:
    /// footprints long along the line (<see cref="Obb2.AxisRight"/>) and <see cref="Thickness"/> across it, standing
    /// <see cref="Height"/> above the ground at their centre with <see cref="Sink"/> more below it.
    /// </summary>
    public IEnumerable<Prop> Segments(HeightField terrain, int firstId)
    {
        float thickness = Thickness(Kind);
        int count = SegmentCount;
        Vector2 along = Vector2.Normalize(End - Start);
        float yaw = MathF.Atan2(-along.Y, along.X);   // Angles.Right(yaw) == along
        var half = new Vector2(SegmentLength / 2, thickness / 2);
        for (int k = 0; k < count; k++)
        {
            Vector2 center = Start + along * (SegmentLength * (k + 0.5f));
            float ground = terrain.HeightAt(center.X, center.Y);
            yield return new Prop(firstId + k, Kind, new Obb2(center, half, yaw), ground - Sink, Height(Kind) + Sink);
        }
    }
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 338、Art 342。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): brick walls, stone walls and fences as props, laid out in lines"
```

---

### Task 2：物件状态

设计 §3.1。每个物件的状态（是否立着、剩余耐久）放进 `Battlefield`。公开的只读查询 `IsStanding(id)`、`Durability(id)`、`PropById(id)`；改状态的 `Damage`、`Destroy`、`Restore` 和按下标访问的方法是 `internal`，只给仿真用（测试项目能看到）。所有查询都按当前状态走：`Raycast` 只算地面、岩石和立着的墙（栅栏、草丛、瓦砾都不挡）；`BushesNear` 跳过被打掉的草丛；`TankMotion` 的碰撞只和立着的、挡坦克的物件做推出。为了不在每个 tick 里过滤，`Battlefield` 建好时把岩石和墙、挡坦克的、栅栏和草丛、墙、草丛各列一份下标表。

**Files:**
- Modify: `src/Tank.Sim/Maps/Battlefield.cs`、`src/Tank.Sim/Vehicles/TankMotion.cs`
- Test: 新建 `tests/Tank.Sim.Tests/PropStateTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/PropStateTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Tests;

public sealed class PropStateTests
{
    // Across the X axis at x = 60, facing a ray along +X at z = 100.
    private static Prop Wall(int id, PropKind kind = PropKind.BrickWall, float x = 60) =>
        new WallLine(kind, new Vector2(x, 99), new Vector2(x, 101)).Segments(TestWorld.Flat(), id).Single();

    private static Prop Rock(int id, float x) => new(id, PropKind.Rock, new Obb2(new Vector2(x, 100), new Vector2(1, 5), 0), -1, 10);

    private static Prop Bush(int id, float x, float z = 100) => new(id, PropKind.Bush, new Obb2(new Vector2(x, z), new Vector2(3, 3), 0), -0.2f, 4);

    private static readonly Vector3 From = new(20, 1, 100), To = new(120, 1, 100);

    [Fact]
    public void Every_prop_starts_standing_at_full_hit_points()
    {
        Battlefield field = TestWorld.FlatField(Wall(1), Wall(2, PropKind.StoneWall, 70), Wall(3, PropKind.Fence, 80), Rock(4, 90), Bush(5, 40));
        Assert.All(field.Props, p => Assert.True(field.IsStanding(p.Id)));
        Assert.Equal(new[] { 300, 900, 1, 0, 1 }, field.Props.Select(p => field.Durability(p.Id)));
    }

    [Fact]
    public void Asking_about_an_unknown_prop_throws() =>
        Assert.Throws<ArgumentException>(() => TestWorld.FlatField(Wall(1)).IsStanding(2));

    [Fact]
    public void PropById_finds_a_prop_by_its_id()
    {
        Battlefield field = TestWorld.FlatField(Rock(7, 90), Wall(3));
        Assert.Equal(PropKind.BrickWall, field.PropById(3).Kind);
        Assert.Equal(7, field.PropById(7).Id);
        Assert.Throws<ArgumentException>(() => field.PropById(1));
    }

    [Fact]
    public void A_standing_wall_stops_rays()
    {
        Assert.True(TestWorld.FlatField(Wall(1)).Raycast(From, To, out RayHit hit));
        Assert.Equal(1, hit.PropId);
        Assert.Equal(59.7f, hit.Point.X, 1e-3f);
    }

    [Fact]
    public void Rays_pass_over_rubble_and_stop_at_what_lies_behind()
    {
        Battlefield field = TestWorld.FlatField(Wall(1), Rock(2, 80));
        Assert.True(field.Damage(field.IndexOf(1), 300));
        Assert.True(field.Raycast(From, To, out RayHit hit));
        Assert.Equal(2, hit.PropId);
    }

    [Fact]
    public void Rays_pass_through_fences_and_bushes()
    {
        Battlefield field = TestWorld.FlatField(Wall(1, PropKind.Fence), Bush(2, 40));
        Assert.False(field.Raycast(From, To, out _));
    }

    [Fact]
    public void Damage_wears_a_wall_down_and_the_blow_that_empties_it_destroys_it()
    {
        Battlefield field = TestWorld.FlatField(Wall(1));
        int wall = field.IndexOf(1);
        Assert.False(field.Damage(wall, 120));
        Assert.Equal(180, field.Durability(1));
        Assert.True(field.IsStanding(1));
        Assert.True(field.Damage(wall, 500));
        Assert.Equal(0, field.Durability(1));
        Assert.False(field.IsStanding(1));
        Assert.False(field.Damage(wall, 50));   // already down
    }

    [Fact]
    public void Destroy_knocks_a_prop_down_once()
    {
        Battlefield field = TestWorld.FlatField(Wall(1, PropKind.Fence));
        Assert.True(field.Destroy(0));
        Assert.False(field.IsStanding(1));
        Assert.False(field.Destroy(0));
    }

    [Fact]
    public void Rocks_cannot_be_damaged_or_destroyed()
    {
        Battlefield field = TestWorld.FlatField(Rock(1, 60));
        Assert.Throws<InvalidOperationException>(() => field.Damage(0, 100));
        Assert.Throws<InvalidOperationException>(() => field.Destroy(0));
        Assert.True(field.IsStanding(1));
    }

    [Fact]
    public void Restore_puts_a_prop_back_at_full_hit_points()
    {
        Battlefield field = TestWorld.FlatField(Wall(1, PropKind.StoneWall));
        field.Damage(0, 1000);
        field.Restore(0);
        Assert.True(field.IsStanding(1));
        Assert.Equal(900, field.Durability(1));
    }

    [Fact]
    public void BushesNear_skips_bushes_that_were_shot_away()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, 100), Bush(2, 110));
        field.Destroy(field.IndexOf(1));
        Assert.Equal(new[] { 2 }, field.BushesNear(new Vector2(105, 100), 5f).Select(p => p.Id));
        var results = new List<Prop>();
        field.BushesNear(new Vector2(105, 100), 5f, results);
        Assert.Equal(new[] { 2 }, results.Select(p => p.Id));
    }

    [Fact]
    public void A_tank_is_stopped_by_a_wall_but_drives_over_its_rubble()
    {
        // A three-segment brick wall across the road at z = 90; the tank starts at z = 110 heading north (−Z).
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(97, 90), new Vector2(103, 90)).Segments(TestWorld.Flat(), 1).ToArray();
        Battlefield field = TestWorld.FlatField(wall);
        var blocked = TestWorld.Tank(100, 110);
        TestWorld.Drive(ref blocked, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(blocked.Position.Y > 90 + 0.3f + TankFront, $"drove into the wall: z = {blocked.Position.Y}");

        foreach (Prop segment in wall) field.Destroy(field.IndexOf(segment.Id));
        var through = TestWorld.Tank(100, 110);
        TestWorld.Drive(ref through, field, TestWorld.Cmd(throttle: 1), 5 * SimConstants.TickRate);
        Assert.True(through.Position.Y < 90 - TankFront, $"stopped at the rubble: z = {through.Position.Y}");
    }

    private static float TankFront => TestWorld.Tank(0, 0).Spec.HullLength / 2;
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Battlefield.IsStanding`、`Durability`、`PropById`、`Damage`、`Destroy`、`IndexOf` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/Battlefield.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/Battlefield.cs b/src/Tank.Sim/Maps/Battlefield.cs
index 4a9d634..2bcf4d2 100644
--- a/src/Tank.Sim/Maps/Battlefield.cs
+++ b/src/Tank.Sim/Maps/Battlefield.cs
@@ -10,12 +10,22 @@ public readonly record struct RayHit(float T, Vector3 Point, int PropId)
     public const int TerrainId = 0;
 }
 
-/// <summary>Terrain plus static props, and the rectangle tanks may drive in.</summary>
+/// <summary>
+/// Terrain, the props on it and the rectangle tanks may drive in. Props keep their shapes; what changes as the battle
+/// goes on is their state: whether each still stands, and its hit points (only the simulation changes it). Every query
+/// here (rays, tank collision, bushes) sees the props as they stand now.
+/// </summary>
 public sealed class Battlefield
 {
     private readonly Prop[] _props;
-    private readonly Prop[] _solid;
-    private readonly Prop[] _bushes;
+    private readonly Dictionary<int, int> _indexOf;
+    private readonly int[] _durability;
+    private readonly bool[] _standing;
+    private readonly int[] _shellStoppers;
+    private readonly int[] _blockers;
+    private readonly int[] _flimsy;
+    private readonly int[] _walls;
+    private readonly int[] _bushes;
 
     public Battlefield(HeightField terrain, IEnumerable<Prop> props, float border)
     {
@@ -23,20 +33,26 @@ public sealed class Battlefield
             throw new ArgumentOutOfRangeException(nameof(border), $"Border {border}m leaves no playable area.");
         Terrain = terrain;
         _props = props.ToArray();
-        var seen = new HashSet<int>();
-        foreach (var prop in _props)
+        _indexOf = new Dictionary<int, int>(_props.Length);
+        for (int i = 0; i < _props.Length; i++)
         {
+            Prop prop = _props[i];
             if (prop.Id == RayHit.TerrainId)
                 throw new ArgumentException($"Prop id {prop.Id} is reserved for terrain.", nameof(props));
-            if (!seen.Add(prop.Id))
+            if (!_indexOf.TryAdd(prop.Id, i))
                 throw new ArgumentException($"Duplicate prop id {prop.Id}.", nameof(props));
             if (prop.Height <= 0)
                 throw new ArgumentException($"Prop {prop.Id} must have positive height, got {prop.Height}.", nameof(props));
             if (prop.Kind == PropKind.Bush && prop.Footprint.HalfExtents.X != prop.Footprint.HalfExtents.Y)
                 throw new ArgumentException($"Bush {prop.Id} must have a round footprint (equal half extents).", nameof(props));
         }
-        _solid = _props.Where(p => p.IsSolid).ToArray();
-        _bushes = _props.Where(p => p.Kind == PropKind.Bush).ToArray();
+        _durability = _props.Select(p => p.MaxDurability).ToArray();
+        _standing = _props.Select(_ => true).ToArray();
+        _shellStoppers = IndicesWhere(p => p.StopsShells);
+        _blockers = IndicesWhere(p => p.IsSolid);
+        _flimsy = IndicesWhere(p => p.Kind is PropKind.Fence or PropKind.Bush);
+        _walls = IndicesWhere(p => p.IsWall);
+        _bushes = IndicesWhere(p => p.Kind == PropKind.Bush);
         Border = border;
     }
 
@@ -44,16 +60,30 @@ public sealed class Battlefield
     public IReadOnlyList<Prop> Props => _props;
     public float Border { get; }
 
+    /// <summary>The prop with id <paramref name="propId"/>; throws if there is none.</summary>
+    public Prop PropById(int propId) => _props[IndexOf(propId)];
+
+    /// <summary>True until the prop is destroyed (a wall turned to rubble, a fence knocked flat, a bush shot away). Rocks always stand.</summary>
+    public bool IsStanding(int propId) => _standing[IndexOf(propId)];
+
+    /// <summary>Hit points left: <see cref="Prop.MaxDurability"/> when intact, 0 once destroyed (and always 0 for a rock, which can't be).</summary>
+    public int Durability(int propId) => _durability[IndexOf(propId)];
+
     public Vector2 ClampToPlayable(Vector2 p) =>
         Vector2.Clamp(p, new Vector2(Border, Border), new Vector2(Terrain.SizeX - Border, Terrain.SizeZ - Border));
 
-    /// <summary>Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against ground and solid props (bushes are see-through).</summary>
+    /// <summary>
+    /// Nearest hit of segment <paramref name="from"/>→<paramref name="to"/> against the ground, rocks and standing walls.
+    /// Fences, bushes and rubble don't stop it.
+    /// </summary>
     public bool Raycast(Vector3 from, Vector3 to, out RayHit hit)
     {
         bool found = Terrain.Raycast(from, to, out float best);
         int propId = RayHit.TerrainId;
-        foreach (var prop in _solid)
+        foreach (int i in _shellStoppers)
         {
+            if (!_standing[i]) continue;
+            ref readonly Prop prop = ref _props[i];
             if (Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out float t) && (!found || t < best))
             {
                 found = true;
@@ -65,20 +95,81 @@ public sealed class Battlefield
         return found;
     }
 
-    /// <summary>Bushes whose edge comes within <paramref name="radius"/> of <paramref name="point"/> (their footprint is a circle of radius HalfExtents.X).</summary>
+    /// <summary>Standing bushes whose edge comes within <paramref name="radius"/> of <paramref name="point"/> (their footprint is a circle of radius HalfExtents.X).</summary>
     public IEnumerable<Prop> BushesNear(Vector2 point, float radius)
     {
-        foreach (var prop in _bushes)
-            if (Vector2.Distance(prop.Footprint.Center, point) - prop.Footprint.HalfExtents.X <= radius)
-                yield return prop;
+        foreach (int i in _bushes)
+            if (_standing[i] && IsNear(_props[i], point, radius))
+                yield return _props[i];
     }
 
     /// <summary>Same as <see cref="BushesNear(Vector2, float)"/> but fills a caller-owned list (cleared first), without allocating.</summary>
     public void BushesNear(Vector2 point, float radius, List<Prop> results)
     {
         results.Clear();
-        foreach (var prop in _bushes)
-            if (Vector2.Distance(prop.Footprint.Center, point) - prop.Footprint.HalfExtents.X <= radius)
-                results.Add(prop);
+        foreach (int i in _bushes)
+            if (_standing[i] && IsNear(_props[i], point, radius))
+                results.Add(_props[i]);
     }
+
+    private static bool IsNear(in Prop bush, Vector2 point, float radius) =>
+        Vector2.Distance(bush.Footprint.Center, point) - bush.Footprint.HalfExtents.X <= radius;
+
+    // The simulation's view: props by index into Props, and the means to change their state.
+
+    /// <summary>Indices of rocks, walls and fences: what blocks tanks while it stands.</summary>
+    internal ReadOnlySpan<int> Blockers => _blockers;
+
+    /// <summary>Indices of fences and bushes: shells fly through them and knock them down.</summary>
+    internal ReadOnlySpan<int> Flimsy => _flimsy;
+
+    /// <summary>Indices of brick and stone walls: what HE splashes and what leaves rubble.</summary>
+    internal ReadOnlySpan<int> Walls => _walls;
+
+    internal int IndexOf(int propId) =>
+        _indexOf.TryGetValue(propId, out int index) ? index : throw new ArgumentException($"No prop {propId}.", nameof(propId));
+
+    internal ref readonly Prop PropAt(int index) => ref _props[index];
+
+    internal bool StandingAt(int index) => _standing[index];
+
+    internal int DurabilityAt(int index) => _durability[index];
+
+    /// <summary>Takes <paramref name="amount"/> off a standing prop's hit points. At zero it is destroyed.</summary>
+    /// <returns>True if this destroyed it.</returns>
+    internal bool Damage(int index, int amount)
+    {
+        RequireDestructible(index);
+        if (!_standing[index] || amount <= 0) return false;
+        _durability[index] = System.Math.Max(0, _durability[index] - amount);
+        if (_durability[index] > 0) return false;
+        _standing[index] = false;
+        return true;
+    }
+
+    /// <summary>Knocks a prop down outright, whatever its hit points.</summary>
+    /// <returns>True if it was standing.</returns>
+    internal bool Destroy(int index)
+    {
+        RequireDestructible(index);
+        if (!_standing[index]) return false;
+        _durability[index] = 0;
+        _standing[index] = false;
+        return true;
+    }
+
+    /// <summary>Puts a prop back as it was at the start: standing, at full hit points.</summary>
+    internal void Restore(int index)
+    {
+        _durability[index] = _props[index].MaxDurability;
+        _standing[index] = true;
+    }
+
+    private void RequireDestructible(int index)
+    {
+        if (!_props[index].IsDestructible) throw new InvalidOperationException($"Prop {_props[index].Id} ({_props[index].Kind}) can't be destroyed.");
+    }
+
+    private int[] IndicesWhere(Func<Prop, bool> predicate) =>
+        Enumerable.Range(0, _props.Length).Where(i => predicate(_props[i])).ToArray();
 }
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankMotion.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankMotion.cs b/src/Tank.Sim/Vehicles/TankMotion.cs
index 100dbc1..6ad4fa0 100644
--- a/src/Tank.Sim/Vehicles/TankMotion.cs
+++ b/src/Tank.Sim/Vehicles/TankMotion.cs
@@ -27,7 +27,7 @@ public static class TankMotion
     public static Obb2 Footprint(TankSpec spec, Vector2 center, float yaw) =>
         new(center, new Vector2(spec.HullWidth / 2, spec.HullLength / 2), yaw);
 
-    /// <summary>Applies throttle and steering, then moves the hull and pushes it out of solid props and <paramref name="obstacles"/>.</summary>
+    /// <summary>Applies throttle and steering, then moves the hull and pushes it out of standing solid props and <paramref name="obstacles"/>.</summary>
     public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles)
     {
         TankSpec spec = t.Spec;
@@ -143,13 +143,12 @@ public static class TankMotion
         return best;
     }
 
-    /// <summary>Deepest overlap of <paramref name="box"/> with any solid prop or obstacle; 0 when clear.</summary>
+    /// <summary>Deepest overlap of <paramref name="box"/> with any standing solid prop or obstacle; 0 when clear.</summary>
     private static float PenetrationDepth(in Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
     {
         float deepest = 0f;
-        IReadOnlyList<Prop> props = field.Props;
-        for (int i = 0; i < props.Count; i++)
-            if (props[i].IsSolid && Obb2.TryGetPenetration(box, props[i].Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
+        foreach (int i in field.Blockers)
+            if (field.StandingAt(i) && Obb2.TryGetPenetration(box, field.PropAt(i).Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
         foreach (Obb2 other in obstacles)
             if (Obb2.TryGetPenetration(box, other, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
         return deepest;
@@ -157,12 +156,11 @@ public static class TankMotion
 
     private static Vector2 Resolve(Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
     {
-        IReadOnlyList<Prop> props = field.Props;
         for (int iteration = 0; iteration < CollisionIterations; iteration++)
         {
             bool pushed = false;
-            for (int i = 0; i < props.Count; i++)
-                if (props[i].IsSolid) pushed |= PushOut(ref box, props[i].Footprint);   // bushes don't block
+            foreach (int i in field.Blockers)
+                if (field.StandingAt(i)) pushed |= PushOut(ref box, field.PropAt(i).Footprint);   // rubble, fallen fences and bushes don't block
             foreach (Obb2 other in obstacles) pushed |= PushOut(ref box, other);
             if (!pushed) break;
         }
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 350、Art 342。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): props keep their state — walls wear down and fall, every query sees what still stands"
```

---

### Task 3：炮弹对物件

设计 §2.2、§3.2、§3.4。炮弹阶段的顺序不变，每发炮弹每 tick：按 2A 的做法找出线段停在哪里（地面、岩石、立着的墙，或坦克的装甲盒）；线段在停下之前穿过的栅栏和草丛全部被毁（草丛按竖直圆柱判断，新加 `Intersect.SegmentCylinder`），但线段起点所在的那一个不算：那是开炮时炮口所在的草丛，躲在草丛里开炮不会打掉自己的掩护（之后的 tick，炮弹飞进草丛时已经把它打掉了）；停在墙上时按弹种扣耐久（`Combat/WallDamage.cs`：穿甲弹是随机伤害的一半，高爆弹是全部），然后爆炸；高爆弹爆炸时，除了 2A 的坦克溅射，半径内其他立着的墙受到“伤害 × 0.5 × (1 − 距离 / 半径)”的溅射（距离是爆点到墙段立体外框的最近距离，`Prop.DistanceTo`），半径内的栅栏和草丛被毁。炮管顶进墙里开炮等于在墙面上直接命中这段墙（`Ballistics.Launch` 新增 `BlockPropId`）；如果这段墙在同一 tick 里已经倒了（被先结算的炮弹打塌，或被后面槽位的车撞倒），`GoOff` 重新看剩下的一截炮管：有别的东西挡着就在那里爆炸，没有就让炮弹从炮口飞出。`PropDamaged` 直接命中时带着炮弹速度、溅射时速度为零，客户端据此区分；`Damage` 和 `TankHit` 一样是这一发的伤害。事件表的初始容量加到 `MaxTanks × 4`（高爆弹打进草丛一次能报十几个物件被毁）。事件：`PropDamaged`（只有墙）和 `PropDestroyed`，`GameEvent` 末尾新增 `PropId`；打中墙时顺序是 `PropDamaged` →（`PropDestroyed`）→ `ShellExploded` → 爆炸波及的结果。物件相关的代码放在新的 `Simulation.Props.cs`。

**Files:**
- Modify: `src/Tank.Sim/Geometry/Intersect.cs`、`Maps/Prop.cs`、`Combat/Ballistics.cs`、`GameEvent.cs`、`Simulation.cs`、`Simulation.Shells.cs`
- Create: `src/Tank.Sim/Combat/WallDamage.cs`、`src/Tank.Sim/Simulation.Props.cs`
- Test: 新建 `tests/Tank.Sim.Tests/ShellPropTests.cs`（含“打墙、栅栏、草丛时每 tick 不分配内存”）；改 `GeometryTests.cs`、`PropStateTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Sim.Tests/GeometryTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/GeometryTests.cs b/tests/Tank.Sim.Tests/GeometryTests.cs
index eb22dca..757afef 100644
--- a/tests/Tank.Sim.Tests/GeometryTests.cs
+++ b/tests/Tank.Sim.Tests/GeometryTests.cs
@@ -100,3 +100,36 @@ public sealed class GeometryTests
     public void SegmentPrism_ParallelOutsideTheBox_Misses() =>
         Assert.False(Intersect.SegmentPrism(Square(0, 0), 0, 2, new Vector3(-5, 1, 3), new Vector3(5, 1, 3), out _));
 }
+
+public sealed class SegmentCylinderTests
+{
+    private static readonly Vector2 Center = new(10, 20);
+
+    [Fact]
+    public void Finds_where_a_level_segment_enters_the_side()
+    {
+        Assert.True(Intersect.SegmentCylinder(Center, 1f, 0f, 2f, new Vector3(5, 1, 20), new Vector3(15, 1, 20), out float t));
+        Assert.Equal(0.4f, t, 1e-5f);
+    }
+
+    [Theory]
+    [InlineData(1, 22)]   // beside it
+    [InlineData(3, 20)]   // over it
+    public void Misses_a_segment_that_passes_by(float y, float z) =>
+        Assert.False(Intersect.SegmentCylinder(Center, 1f, 0f, 2f, new Vector3(5, y, z), new Vector3(15, y, z), out _));
+
+    [Fact]
+    public void Starts_at_zero_inside() =>
+        Assert.True(Intersect.SegmentCylinder(Center, 1f, 0f, 2f, new Vector3(10, 1, 20), new Vector3(15, 1, 20), out float t) && t == 0f);
+
+    [Fact]
+    public void Enters_through_the_top()
+    {
+        Assert.True(Intersect.SegmentCylinder(Center, 1f, 0f, 2f, new Vector3(10, 6, 20), new Vector3(10, -2, 20), out float t));
+        Assert.Equal(0.5f, t, 1e-5f);
+    }
+
+    [Fact]
+    public void A_short_segment_that_stops_short_misses() =>
+        Assert.False(Intersect.SegmentCylinder(Center, 1f, 0f, 2f, new Vector3(5, 1, 20), new Vector3(8.9f, 1, 20), out _));
+}
PATCH
```

修改 `tests/Tank.Sim.Tests/PropStateTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/PropStateTests.cs b/tests/Tank.Sim.Tests/PropStateTests.cs
index cfca3bc..2952028 100644
--- a/tests/Tank.Sim.Tests/PropStateTests.cs
+++ b/tests/Tank.Sim.Tests/PropStateTests.cs
@@ -132,3 +132,24 @@ public sealed class PropStateTests
 
     private static float TankFront => TestWorld.Tank(0, 0).Spec.HullLength / 2;
 }
+
+public sealed class PropDistanceTests
+{
+    [Fact]
+    public void Measures_to_a_wall_box_and_its_top()
+    {
+        var wall = new Prop(1, PropKind.BrickWall, new Obb2(new Vector2(0, 0), new Vector2(1, 0.3f), 0), 0, 3);
+        Assert.Equal(0f, wall.DistanceTo(new Vector3(0.5f, 1, 0)));
+        Assert.Equal(2f, wall.DistanceTo(new Vector3(0, 1, 2.3f)), 1e-5f);
+        Assert.Equal(5f, wall.DistanceTo(new Vector3(4, 7, 0)), 1e-5f);   // 3 out past the end, 4 above the top
+    }
+
+    [Fact]
+    public void Measures_to_a_bush_as_a_cylinder()
+    {
+        var bush = new Prop(1, PropKind.Bush, new Obb2(new Vector2(0, 0), new Vector2(2, 2), 0), 0, 3);
+        Assert.Equal(1f, bush.DistanceTo(new Vector3(3, 1, 0)), 1e-5f);
+        Assert.Equal(1f, bush.DistanceTo(new Vector3(0, 1, -3)), 1e-5f);   // a box would say 1 at a corner too, but not here
+        Assert.Equal(MathF.Sqrt(2) * 3 - 2, bush.DistanceTo(new Vector3(3, 1, 3)), 1e-4f);
+    }
+}
PATCH
```

新建 `tests/Tank.Sim.Tests/ShellPropTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Shells against walls, fences and bushes, fired across a <see cref="Duel"/> (shooter at z = 150, target at z = 100).</summary>
public sealed class ShellPropTests
{
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);
    private static readonly int ReloadTicks = (int)MathF.Round(Duel.Spec.ReloadTime * SimConstants.TickRate);

    /// <summary>A three-segment line across the line of fire at <paramref name="z"/>, x = 97 … 103: ids 1, 2, 3 (2 is in the way).</summary>
    private static Prop[] Line(PropKind kind, float z, int firstId = 1) =>
        new WallLine(kind, new Vector2(97, z), new Vector2(103, z)).Segments(TestWorld.Flat(), firstId).ToArray();

    private static Prop Bush(int id, float x, float z, float radius) =>
        new(id, PropKind.Bush, new Obb2(new Vector2(x, z), new Vector2(radius, radius), 0), -0.2f, 4f);

    private static (Duel Duel, InputCmd Aim) Aimed(AmmoType ammo, params Prop[] props)
    {
        var duel = new Duel(targetYaw: MathF.PI, props);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), ammo);
        if (ammo == AmmoType.HE) duel.Run(aim, ReloadTicks);   // swapping to HE restarts the reload
        return (duel, aim);
    }

    [Fact]
    public void AP_on_a_wall_takes_half_its_rolled_damage_and_stops_there()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Line(PropKind.BrickWall, 125));
        var events = duel.Fire(aim);

        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(2, hurt.PropId);
        Assert.Equal(duel.ShooterId, hurt.TankId);
        Assert.InRange(hurt.Damage, 83, 138);   // 220 × 0.75 … 1.25 × 0.5
        Assert.Equal(125.3f, hurt.Position.Z, 1e-2f);   // the wall's near face
        Assert.True(hurt.Velocity.Z < -700f, "a direct hit carries the shell's velocity");
        Assert.Equal(300 - hurt.Damage, duel.Sim.Field.Durability(2));
        Assert.Equal(hurt.Position, Assert.Single(events, e => e.Type == GameEventType.ShellExploded).Position);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(Duel.Spec.MaxHealth, duel.Target.Health);
    }

    [Fact]
    public void HE_on_a_wall_takes_its_full_rolled_damage_then_bursts_and_splashes_the_segments_beside_it()
    {
        var (duel, aim) = Aimed(AmmoType.HE, Line(PropKind.StoneWall, 125));
        var events = duel.Fire(aim);

        GameEvent[] hurt = events.Where(e => e.Type == GameEventType.PropDamaged).ToArray();
        Assert.Equal(new[] { 2, 1, 3 }, hurt.Select(e => e.PropId));   // the struck wall first, then the splash in prop order
        Assert.InRange(hurt[0].Damage, 210, 350);                       // 280 × 0.75 … 1.25
        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.True(events.IndexOf(hurt[0]) < events.IndexOf(burst) && events.IndexOf(burst) < events.IndexOf(hurt[1]));
        foreach (GameEvent splash in hurt.Skip(1))
        {
            Prop wall = duel.Sim.Field.Props[splash.PropId - 1];
            Assert.Equal(burst.Position, splash.Position);
            Assert.Equal(Vector3.Zero, splash.Velocity);   // a splash, not a direct hit
            Assert.Equal(WallDamage.Splash(Duel.Spec.He, wall.DistanceTo(burst.Position)), splash.Damage);
            Assert.InRange(splash.Damage, 1, hurt[0].Damage - 1);
        }
        Assert.Equal(900 - hurt[0].Damage, duel.Sim.Field.Durability(2));
    }

    [Fact]
    public void Enough_hits_turn_a_wall_to_rubble_and_the_next_shell_flies_over_it_into_the_tank()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Line(PropKind.BrickWall, 125));
        List<GameEvent> events = new();
        for (int shot = 0; shot < 6 && duel.Sim.Field.IsStanding(2); shot++)
        {
            events = duel.Fire(aim);
            duel.Run(aim, ReloadTicks);
        }

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        GameEvent last = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        Assert.True(events.IndexOf(last) < events.IndexOf(down) && events.IndexOf(down) < events.IndexOf(burst), "damaged, destroyed, then the burst");
        Assert.Equal((2, last.ShellId, duel.ShooterId), (down.PropId, down.ShellId, down.TankId));
        Assert.Equal(new Vector3(0, 0, -1), down.Velocity, new Vec3Comparer(0.05f));   // pushed the way the shell flew (north)
        Assert.Equal(0, duel.Sim.Field.Durability(2));

        var through = duel.Fire(aim);
        Assert.Contains(through, e => e.Type == GameEventType.TankHit && e.TargetTankId == duel.TargetId);
        Assert.DoesNotContain(through, e => e.Type is GameEventType.PropDamaged or GameEventType.PropDestroyed);
    }

    [Fact]
    public void A_shell_knocks_a_fence_flat_on_its_way_and_still_hits_the_tank()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Line(PropKind.Fence, 110));
        var events = duel.Fire(aim);

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((2, duel.ShooterId), (down.PropId, down.TankId));
        Assert.Equal(110f, down.Position.Z, 0.15f);
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.True(events.IndexOf(down) < events.IndexOf(hit));
        Assert.False(duel.Sim.Field.IsStanding(2));
        Assert.True(duel.Sim.Field.IsStanding(1) && duel.Sim.Field.IsStanding(3));
    }

    [Fact]
    public void A_shell_shoots_away_a_bush_it_flies_through()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Bush(1, 100, 115, 3f), Bush(2, 110, 115, 3f));
        var events = duel.Fire(aim);

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(1, down.PropId);
        Assert.Contains(events, e => e.Type == GameEventType.TankHit);
        Assert.False(duel.Sim.Field.IsStanding(1));
        Assert.True(duel.Sim.Field.IsStanding(2));
    }

    [Fact]
    public void An_HE_burst_flattens_the_fences_and_bushes_within_its_radius()
    {
        // The burst is on the target's front plate at (100, 1.02, 103.1); medium HE splashes 3 m.
        Prop near = new WallLine(PropKind.Fence, new Vector2(102, 104.5f), new Vector2(104, 104.5f)).Segments(TestWorld.Flat(), 1).Single();
        Prop far = new WallLine(PropKind.Fence, new Vector2(106, 104.5f), new Vector2(108, 104.5f)).Segments(TestWorld.Flat(), 2).Single();
        var (duel, aim) = Aimed(AmmoType.HE, near, far, Bush(3, 97, 105, 1f));
        var events = duel.Fire(aim);

        GameEvent burst = Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent[] down = events.Where(e => e.Type == GameEventType.PropDestroyed).ToArray();
        Assert.Equal(new[] { 1, 3 }, down.Select(e => e.PropId));
        Assert.All(down, e => Assert.Equal((burst.Position, AmmoType.HE), (e.Position, e.Ammo)));
        Assert.True(down[0].Velocity.X > 0 && down[1].Velocity.X < 0, "pushed away from the burst");
        Assert.True(duel.Sim.Field.IsStanding(2));
    }

    [Fact]
    public void A_barrel_pushed_into_a_wall_fires_point_blank_into_it()
    {
        // The wall's near face is 3 m ahead of the shooter's trunnion: the barrel pokes into it.
        float face = Duel.ShooterAt.Y - Duel.Spec.TurretOffset - 3f;
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(97, face - 0.3f), new Vector2(103, face - 0.3f)).Segments(TestWorld.Flat(), 1).ToArray();
        var duel = new Duel(targetYaw: MathF.PI, wall);
        var aim = new InputCmd(0, 0, 0, 0f, 0f, false);
        duel.Run(aim, 60);
        var events = duel.Fire(aim);

        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(2, hurt.PropId);
        Assert.InRange(hurt.Damage, 83, 138);
        Assert.Equal(face, hurt.Position.Z, 1e-2f);
        Assert.Equal(0, duel.Sim.ShellCount);
    }

    [Fact]
    public void Direct_damage_rolls_like_a_piercing_hit_on_a_tank_and_AP_does_half()
    {
        for (uint seed = 1; seed < 200; seed++)
        {
            // On 1 mm of armour head-on every round pierces, so the tank's damage is just the roll.
            int heOnTank = Penetration.HeDirect(Duel.Spec.He, 1f, 0f, seed).Damage;
            int apOnTank = Penetration.Ap(Duel.Spec.Ap, Duel.Spec.Caliber, 1f, 0f, seed).Damage;
            Assert.Equal(heOnTank, WallDamage.Direct(Duel.Spec.He, AmmoType.HE, seed));
            Assert.InRange(WallDamage.Direct(Duel.Spec.Ap, AmmoType.AP, seed), apOnTank / 2, (apOnTank + 1) / 2);
        }
    }

    [Fact]
    public void A_fence_or_bush_past_where_the_shell_stops_stays_standing()
    {
        // This tick's path reaches past both the target (z ≈ 103) and the wall (z = 125.3), to z ≈ 94.5 and 120.5.
        Prop[] behindTank = Line(PropKind.Fence, 96, firstId: 1);
        var (duel, aim) = Aimed(AmmoType.AP, behindTank.Append(Bush(4, 100, 92.5f, 3f)).ToArray());
        var events = duel.Fire(aim);
        Assert.Contains(events, e => e.Type == GameEventType.TankHit);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDestroyed);

        Prop[] wallThenFence = Line(PropKind.StoneWall, 125, firstId: 1).Concat(Line(PropKind.Fence, 122, firstId: 4)).ToArray();
        (duel, aim) = Aimed(AmmoType.AP, wallThenFence);
        events = duel.Fire(aim);
        Assert.Contains(events, e => e.Type == GameEventType.PropDamaged && e.PropId == 2);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.PropDestroyed);
    }

    [Fact]
    public void A_tank_firing_from_inside_a_bush_keeps_its_cover()
    {
        var (duel, aim) = Aimed(AmmoType.AP, Bush(1, Duel.ShooterAt.X, Duel.ShooterAt.Y, 4.2f), Bush(2, 100, 115, 3f));
        var events = duel.Fire(aim);
        Assert.True(duel.Sim.Field.IsStanding(1), "the bush the gun fired from");
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(2, down.PropId);   // the one it flew through
        Assert.Contains(events, e => e.Type == GameEventType.TankHit);
    }

    [Fact]
    public void A_wall_one_shell_brings_down_lets_the_next_through_in_the_same_tick()
    {
        // One brick segment, worn down to 1 hit point, 2.5 m in front of the target; two shooters either side fire at the
        // target together, both lines crossing the segment.
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(99, 106), new Vector2(101, 106)).Segments(TestWorld.Flat(), 1).Single();
        var sim = new Simulation(TestWorld.FlatField(wall));
        sim.Field.Damage(0, wall.MaxDurability - 1);
        int left = sim.AddTank(Duel.Spec, new Vector2(96, 150), 0f), right = sim.AddTank(Duel.Spec, new Vector2(104, 150), 0f);
        int target = sim.AddTank(Duel.Spec, Duel.TargetAt, MathF.PI);
        var plate = new Vector3(100, 1.02f, 103.1f);
        InputCmd Aim(int slot, bool fire)
        {
            var (yaw, pitch) = Angles.AimAngles(plate - TankPose.Compute(sim.Tanks[slot], sim.Field.Terrain).Trunnion(Duel.Spec));
            return new InputCmd(0, 0, 0, yaw, pitch, fire);
        }
        var events = new List<GameEvent>();
        for (int tick = 0; tick < 90; tick++)
        {
            bool fire = tick == 60;
            sim.Step(new[] { Aim(0, fire), Aim(1, fire), new InputCmd(0, 0, 0, MathF.PI, 0f, false) });
            events.AddRange(sim.Events);
        }

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(left, down.TankId);   // the lower slot's shell goes first
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal((right, target, down.Tick), (hit.TankId, hit.TargetTankId, hit.Tick));
    }

    [Fact]
    public void A_barrel_in_a_wall_that_came_down_this_tick_fires_out_through_the_gap()
    {
        // Two heavy tanks nose to nose across one brick segment, 5 cm off it, both barrels in it; both fire HE on the same
        // tick. The first burst brings the segment down; the second barrel is then clear, so its shell flies out of the
        // muzzle and into the first tank's front.
        TankSpec heavy = TankCatalog.Heavy;
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(99, 100), new Vector2(101, 100)).Segments(TestWorld.Flat(), 1).Single();
        var sim = new Simulation(TestWorld.FlatField(wall));
        float back = 0.3f + 0.05f + heavy.HullLength / 2;
        Assert.True(back - heavy.TurretOffset - 0.3f < heavy.GunLength, "both barrels reach past the wall's face");
        int north = sim.AddTank(heavy, new Vector2(100, 100 - back), MathF.PI);   // facing south
        int south = sim.AddTank(heavy, new Vector2(100, 100 + back), 0f);         // facing north
        int reload = (int)MathF.Ceiling(heavy.ReloadTime * SimConstants.TickRate);
        var events = new List<GameEvent>();
        for (int tick = 0; tick <= reload + 2; tick++)
        {
            bool fire = tick == reload + 1;   // after the reload that switching to HE starts
            sim.Step(new[] { new InputCmd(0, 0, 0, MathF.PI, 0f, fire, AmmoType.HE), new InputCmd(0, 0, 0, 0f, 0f, fire, AmmoType.HE) });
            events.AddRange(sim.Events);
        }

        Assert.Equal(2, events.Count(e => e.Type == GameEventType.ShellFired));
        GameEvent hurt = Assert.Single(events, e => e.Type == GameEventType.PropDamaged);
        Assert.Equal(north, hurt.TankId);
        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal(north, down.TankId);
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal((south, north), (hit.TankId, hit.TargetTankId));
        Assert.True(events.IndexOf(down) < events.IndexOf(hit));
    }

    [Fact]
    public void Shooting_walls_fences_and_bushes_allocates_nothing_once_warmed_up()
    {
        // Four tanks, two firing AP and two HE, into a stone wall with fences and bushes before it; everything is put
        // back every second, so the measured ticks keep knocking props down.
        HeightField flat = TestWorld.Flat();
        var props = new List<Prop>();
        props.AddRange(new WallLine(PropKind.StoneWall, new Vector2(90, 100), new Vector2(110, 100)).Segments(flat, 1));
        props.AddRange(new WallLine(PropKind.Fence, new Vector2(90, 110), new Vector2(110, 110)).Segments(flat, props.Count + 1));
        for (int k = 0; k < 4; k++) props.Add(Bush(props.Count + 1, 92 + 5 * k, 120, 2f));
        var sim = new Simulation(TestWorld.Field(flat, props.ToArray()));
        for (int k = 0; k < 4; k++) sim.AddTank(Duel.Spec, new Vector2(94 + 4 * k, 150), 0f);
        var inputs = new InputCmd[4];
        for (int k = 0; k < 4; k++) inputs[k] = new InputCmd(0, 0, 0, 0f, -0.02f, true, k % 2 == 0 ? AmmoType.AP : AmmoType.HE);

        int hurt = 0, down = 0;
        void Run(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                sim.Step(inputs);
                foreach (GameEvent e in sim.Events)
                {
                    if (e.Type == GameEventType.PropDamaged) hurt++;
                    if (e.Type == GameEventType.PropDestroyed) down++;
                }
                if (sim.Tick % SimConstants.TickRate == 0)
                    for (int i = 0; i < sim.Field.Props.Count; i++) sim.Field.Restore(i);
            }
        }
        Run(12 * SimConstants.TickRate);   // past the first HE reload and a couple of shots each
        (hurt, down) = (0, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(12 * SimConstants.TickRate);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(hurt >= 6 && down >= 6, $"{hurt} walls hurt and {down} props down while measured");
    }

    [Theory]
    [InlineData(0f, 140)]
    [InlineData(1.5f, 70)]
    [InlineData(2.999f, 0)]
    [InlineData(3f, 0)]
    [InlineData(10f, 0)]
    public void Splash_is_half_damage_fading_to_nothing_at_the_radius(float distance, int damage) =>
        Assert.Equal(damage, WallDamage.Splash(Duel.Spec.He, distance));   // 280, 3 m

    [Fact]
    public void AP_does_not_splash() => Assert.Equal(0, WallDamage.Splash(Duel.Spec.Ap, 0f));

    private sealed class Vec3Comparer(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`WallDamage`、`Intersect.SegmentCylinder`、`Prop.DistanceTo`、`GameEventType.PropDamaged` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Combat/Ballistics.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Combat/Ballistics.cs b/src/Tank.Sim/Combat/Ballistics.cs
index 34d5690..6cf68e1 100644
--- a/src/Tank.Sim/Combat/Ballistics.cs
+++ b/src/Tank.Sim/Combat/Ballistics.cs
@@ -20,9 +20,10 @@ public static class Ballistics
 
     /// <summary>
     /// Where and how a shot of the loaded round fired now would leave the barrel, before dispersion. <see cref="Blocked"/>
-    /// when the barrel is inside a prop or the ground.
+    /// when the barrel is inside a rock, a standing wall or the ground: <see cref="BlockPoint"/> is where it goes in, and
+    /// <see cref="BlockPropId"/> what it hit (<see cref="RayHit.TerrainId"/> for the ground).
     /// </summary>
-    public readonly record struct Launch(Vector3 Origin, Vector3 Velocity, bool Blocked, Vector3 BlockPoint);
+    public readonly record struct Launch(Vector3 Origin, Vector3 Velocity, bool Blocked, Vector3 BlockPoint, int BlockPropId = RayHit.TerrainId);
 
     public static Launch Aim(in TankState tank, Battlefield field)
     {
@@ -31,7 +32,7 @@ public static class Ballistics
         Vector3 muzzle = pose.Muzzle(tank.Spec, tank.TurretYaw, tank.GunPitch);
         Vector3 velocity = pose.GunDirection(tank.TurretYaw, tank.GunPitch) * tank.Spec.Round(tank.Ammo).Speed;
         bool blocked = field.Raycast(trunnion, muzzle, out RayHit hit);
-        return new Launch(muzzle, velocity, blocked, blocked ? hit.Point : Vector3.Zero);
+        return new Launch(muzzle, velocity, blocked, blocked ? hit.Point : Vector3.Zero, blocked ? hit.PropId : RayHit.TerrainId);
     }
 
     /// <summary>
PATCH
```

新建 `src/Tank.Sim/Combat/WallDamage.cs`：

```csharp
namespace Tank.Sim.Combat;

/// <summary>
/// What shells do to walls (design §5.5). Walls have no armour: a shell that strikes one always does damage, AP half its
/// rolled damage (the shell stops), HE all of it (then bursts). An HE burst splashes every wall in its radius with half
/// its damage, fading to nothing at the edge, unrolled. The damage roll is the same ±25% as against tanks.
/// </summary>
public static class WallDamage
{
    public const float ApShare = 0.5f;
    public const float SplashShare = 0.5f;

    public static int Direct(ShellSpec round, AmmoType ammo, uint seed)
    {
        float roll = ShotRandom.Range(seed, ShotRandom.DamageRoll, Penetration.MinRoll, Penetration.MaxRoll);
        return RoundHalfUp(round.Damage * roll * (ammo == AmmoType.AP ? ApShare : 1f));
    }

    /// <summary>Splash on a wall <paramref name="distance"/> m from the burst (to its nearest point); 0 at or past the radius.</summary>
    public static int Splash(ShellSpec round, float distance)
    {
        if (round.SplashRadius <= 0 || distance >= round.SplashRadius) return 0;
        return RoundHalfUp(round.Damage * SplashShare * (1f - distance / round.SplashRadius));
    }

    private static int RoundHalfUp(float value) => (int)MathF.Round(value, MidpointRounding.AwayFromZero);
}
```

修改 `src/Tank.Sim/GameEvent.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/GameEvent.cs b/src/Tank.Sim/GameEvent.cs
index 124c904..9d96a04 100644
--- a/src/Tank.Sim/GameEvent.cs
+++ b/src/Tank.Sim/GameEvent.cs
@@ -11,6 +11,8 @@ public enum GameEventType : byte
     SplashDamage,
     TankDestroyed,
     TankRevived,
+    PropDamaged,
+    PropDestroyed,
 }
 
 /// <summary>What a shell did to the armour it struck. <see cref="None"/> on events that aren't hits.</summary>
@@ -27,14 +29,23 @@ public enum HitResult : byte
 /// A shell that leaves the map or times out emits no event; clients key any per-shell visuals by <c>Shell.Id</c> and retire them when it disappears.
 /// <list type="bullet">
 /// <item><see cref="GameEventType.ShellFired"/>: <see cref="TankId"/> fired <see cref="ShellId"/> from <see cref="Position"/> at <see cref="Velocity"/>.</item>
-/// <item><see cref="GameEventType.ShellExploded"/>: an HE burst, or any shell striking the ground, a rock or a wreck, at <see cref="Position"/>.</item>
+/// <item><see cref="GameEventType.ShellExploded"/>: an HE burst, or any shell striking the ground, a rock, a wall or a wreck, at <see cref="Position"/>.</item>
 /// <item><see cref="GameEventType.TankHit"/>: a shell from <see cref="TankId"/> struck <see cref="TargetTankId"/> at <see cref="Position"/>;
 /// <see cref="Velocity"/> holds the struck face's outward normal.</item>
 /// <item><see cref="GameEventType.SplashDamage"/>: an HE burst at <see cref="Position"/> hurt <see cref="TargetTankId"/>.</item>
 /// <item><see cref="GameEventType.TankDestroyed"/>: <see cref="TargetTankId"/> was knocked out by <see cref="TankId"/>; <see cref="Position"/> is the wreck.</item>
 /// <item><see cref="GameEventType.TankRevived"/>: <see cref="TargetTankId"/> (also <see cref="TankId"/>) is back at full health.</item>
+/// <item><see cref="GameEventType.PropDamaged"/>: shell <see cref="ShellId"/> from <see cref="TankId"/> did <see cref="Damage"/>
+/// to wall <see cref="PropId"/> (the shell's damage, as on <see cref="GameEventType.TankHit"/>: the wall may have had fewer
+/// hit points left). A direct hit: <see cref="Position"/> is where it struck and <see cref="Velocity"/> the shell's;
+/// a splash: <see cref="Position"/> is the burst and <see cref="Velocity"/> zero.</item>
+/// <item><see cref="GameEventType.PropDestroyed"/>: <see cref="TankId"/> destroyed prop <see cref="PropId"/> (a wall turned to rubble,
+/// a fence knocked flat, a bush shot away) with shell <see cref="ShellId"/> (<see cref="Ammo"/> its round), or, when that is
+/// 0, by driving into it. <see cref="Position"/> is where: the point the shell struck or entered it, the burst, or for a
+/// tank the prop's centre on the ground. <see cref="Velocity"/> is the level unit direction it was pushed (the shell's
+/// flight, outward from the burst, the hull's travel): a fence falls that way.</item>
 /// </list>
 /// </summary>
 public readonly record struct GameEvent(
     GameEventType Type, int Tick, int TankId, int ShellId, Vector3 Position, Vector3 Velocity,
-    int TargetTankId = 0, HitResult Result = HitResult.None, int Damage = 0, AmmoType Ammo = AmmoType.AP);
+    int TargetTankId = 0, HitResult Result = HitResult.None, int Damage = 0, AmmoType Ammo = AmmoType.AP, int PropId = 0);
PATCH
```

修改 `src/Tank.Sim/Geometry/Intersect.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Geometry/Intersect.cs b/src/Tank.Sim/Geometry/Intersect.cs
index 8c3abaa..94247aa 100644
--- a/src/Tank.Sim/Geometry/Intersect.cs
+++ b/src/Tank.Sim/Geometry/Intersect.cs
@@ -22,6 +22,36 @@ public static class Intersect
         return true;
     }
 
+    /// <summary>
+    /// Where segment <paramref name="from"/>→<paramref name="to"/> first enters the upright cylinder of radius
+    /// <paramref name="radius"/> round <paramref name="center"/> (ground X, Z) between <paramref name="bottom"/> and
+    /// <paramref name="top"/>; <paramref name="t"/> is in [0, 1] (0 when it starts inside).
+    /// </summary>
+    public static bool SegmentCylinder(Vector2 center, float radius, float bottom, float top, Vector3 from, Vector3 to, out float t)
+    {
+        t = 0;
+        float tMin = 0f, tMax = 1f;
+        if (!Slab(from.Y, to.Y - from.Y, bottom, top, ref tMin, ref tMax)) return false;
+        Vector2 offset = new Vector2(from.X, from.Z) - center;
+        Vector2 delta = new(to.X - from.X, to.Z - from.Z);
+        float a = delta.LengthSquared(), b = Vector2.Dot(offset, delta), c = offset.LengthSquared() - radius * radius;
+        if (a < 1e-12f)
+        {
+            if (c > 0) return false;   // a vertical segment: inside the circle or not at all
+        }
+        else
+        {
+            float discriminant = b * b - a * c;
+            if (discriminant < 0) return false;
+            float root = MathF.Sqrt(discriminant);
+            tMin = MathF.Max(tMin, (-b - root) / a);
+            tMax = MathF.Min(tMax, (-b + root) / a);
+            if (tMin > tMax) return false;
+        }
+        t = tMin;
+        return true;
+    }
+
     private static bool Slab(float start, float delta, float min, float max, ref float tMin, ref float tMax)
     {
         if (MathF.Abs(delta) < 1e-8f) return start >= min && start <= max;
PATCH
```

修改 `src/Tank.Sim/Maps/Prop.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/Prop.cs b/src/Tank.Sim/Maps/Prop.cs
index 0d9b33d..1affcb4 100644
--- a/src/Tank.Sim/Maps/Prop.cs
+++ b/src/Tank.Sim/Maps/Prop.cs
@@ -1,3 +1,4 @@
+using System.Numerics;
 using Tank.Sim.Geometry;
 
 namespace Tank.Sim.Maps;
@@ -49,6 +50,17 @@ public readonly record struct Prop(int Id, PropKind Kind, Obb2 Footprint, float
 
     public bool IsDestructible => MaxDurability > 0;
 
+    /// <summary>Distance from <paramref name="point"/> to the prop's solid (a bush's is an upright cylinder); 0 inside.</summary>
+    public float DistanceTo(Vector3 point)
+    {
+        var ground = new Vector2(point.X, point.Z);
+        float across = Kind == PropKind.Bush
+            ? MathF.Max(Vector2.Distance(ground, Footprint.Center) - Footprint.HalfExtents.X, 0f)
+            : Footprint.DistanceToPoint(ground);
+        float up = point.Y > Top ? point.Y - Top : point.Y < Bottom ? Bottom - point.Y : 0f;
+        return MathF.Sqrt(across * across + up * up);
+    }
+
     /// <summary>Where a wall's rubble lies once it is down: its footprint widened by <see cref="RubbleSpread"/> on both faces.</summary>
-    public Obb2 RubbleFootprint => Footprint with { HalfExtents = Footprint.HalfExtents + new System.Numerics.Vector2(0, RubbleSpread) };
+    public Obb2 RubbleFootprint => Footprint with { HalfExtents = Footprint.HalfExtents + new Vector2(0, RubbleSpread) };
 }
PATCH
```

新建 `src/Tank.Sim/Simulation.Props.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim;

/// <summary>What shells do to props (design §5.5): wear walls down to rubble, knock fences flat, shoot bushes away.</summary>
public sealed partial class Simulation
{
    /// <summary>
    /// Fences and bushes the shell's path enters before <paramref name="stopT"/> (where it stops this tick, or 1) go down;
    /// the shell flies on. One the path starts inside is left standing: that is where the gun was when it fired (a tank
    /// in a bush doesn't shoot its own cover away), since a shell that flew into one has knocked it down already.
    /// </summary>
    private void KnockThrough(in Shell shell, Vector3 from, Vector3 to, float stopT)
    {
        Vector3 middle = (from + to) * 0.5f;
        float halfLength = Vector3.Distance(from, to) * 0.5f;
        foreach (int i in Field.Flimsy)
        {
            if (!Field.StandingAt(i)) continue;
            ref readonly Prop prop = ref Field.PropAt(i);
            Vector2 center = prop.Footprint.Center;
            if (Vector2.Distance(center, new Vector2(middle.X, middle.Z)) > halfLength + prop.Footprint.HalfExtents.Length()) continue;
            bool crossed = prop.Kind == PropKind.Bush
                ? Intersect.SegmentCylinder(center, prop.Footprint.HalfExtents.X, prop.Bottom, prop.Top, from, to, out float t)
                : Intersect.SegmentPrism(prop.Footprint, prop.Bottom, prop.Top, from, to, out t);
            if (crossed && t > 0f && t <= stopT && Field.Destroy(i))
                PropDown(i, shell.OwnerTankId, shell.Id, shell.Ammo, Vector3.Lerp(from, to, t), Level(to - from, prop.Footprint.AxisForward));
        }
    }

    /// <summary>
    /// A shell strikes wall <paramref name="index"/> at <paramref name="at"/>: it loses <see cref="WallDamage.Direct"/> hit
    /// points (perhaps falling to rubble), then the shell bursts there, splashing everything but this wall.
    /// </summary>
    private void StrikeWall(in Shell shell, int index, Vector3 at, Vector3 direction)
    {
        int damage = WallDamage.Direct(shell.Round, shell.Ammo, shell.Seed);
        HurtWall(shell, index, at, damage, shell.Velocity, Level(direction, Field.PropAt(index).Footprint.AxisForward));
        Explode(shell, at, directSlot: -1, directProp: index);
    }

    /// <summary>
    /// An HE burst at <paramref name="at"/>: every other standing wall within the splash radius takes
    /// <see cref="WallDamage.Splash"/>; every fence and bush within it goes down.
    /// </summary>
    private void SplashProps(in Shell shell, Vector3 at, int directProp)
    {
        float radius = shell.Round.SplashRadius;
        if (radius <= 0) return;
        foreach (int i in Field.Walls)
        {
            if (i == directProp || !Field.StandingAt(i)) continue;
            ref readonly Prop wall = ref Field.PropAt(i);
            int damage = WallDamage.Splash(shell.Round, wall.DistanceTo(at));
            if (damage > 0) HurtWall(shell, i, at, damage, Vector3.Zero, Outward(at, wall));
        }
        foreach (int i in Field.Flimsy)
        {
            if (!Field.StandingAt(i)) continue;
            ref readonly Prop prop = ref Field.PropAt(i);
            if (prop.DistanceTo(at) < radius && Field.Destroy(i)) PropDown(i, shell.OwnerTankId, shell.Id, shell.Ammo, at, Outward(at, prop));
        }
    }

    /// <summary>
    /// Takes <paramref name="damage"/> off a standing wall, reporting it (with the shell's <paramref name="velocity"/> for a
    /// direct hit, zero for a splash), and its fall if this brings it down.
    /// </summary>
    private void HurtWall(in Shell shell, int index, Vector3 at, int damage, Vector3 velocity, Vector2 push)
    {
        int propId = Field.PropAt(index).Id;
        _events.Add(new GameEvent(GameEventType.PropDamaged, Tick, shell.OwnerTankId, shell.Id, at, velocity,
            Damage: damage, Ammo: shell.Ammo, PropId: propId));
        if (Field.Damage(index, damage)) PropDown(index, shell.OwnerTankId, shell.Id, shell.Ammo, at, push);
    }

    private void PropDown(int index, int tankId, int shellId, AmmoType ammo, Vector3 at, Vector2 push) =>
        _events.Add(new GameEvent(GameEventType.PropDestroyed, Tick, tankId, shellId, at, new Vector3(push.X, 0, push.Y),
            Ammo: ammo, PropId: Field.PropAt(index).Id));

    /// <summary>Level unit direction from the burst to the prop's centre (the prop's own forward if the burst is right over it).</summary>
    private static Vector2 Outward(Vector3 burst, in Prop prop) =>
        Level(new Vector3(prop.Footprint.Center.X - burst.X, 0, prop.Footprint.Center.Y - burst.Z), prop.Footprint.AxisForward);

    /// <summary><paramref name="v"/> flattened onto the ground and normalised; <paramref name="fallback"/> if it is (nearly) vertical.</summary>
    private static Vector2 Level(Vector3 v, Vector2 fallback)
    {
        var flat = new Vector2(v.X, v.Z);
        return flat.LengthSquared() > 1e-8f ? Vector2.Normalize(flat) : fallback;
    }
}
```

修改 `src/Tank.Sim/Simulation.Shells.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.Shells.cs b/src/Tank.Sim/Simulation.Shells.cs
index d8dc568..6c21018 100644
--- a/src/Tank.Sim/Simulation.Shells.cs
+++ b/src/Tank.Sim/Simulation.Shells.cs
@@ -6,33 +6,34 @@ using Tank.Sim.Vehicles;
 
 namespace Tank.Sim;
 
-/// <summary>Shell flight and everything a shell can do when it lands: strike armour, burst, splash, wreck a tank.</summary>
+/// <summary>Shell flight and everything a shell can do when it lands: strike armour or a wall, burst, splash, wreck a tank.</summary>
 public sealed partial class Simulation
 {
     private readonly OrientedBox[] _hullBoxes = new OrientedBox[MaxTanks];
     private readonly OrientedBox[] _turretBoxes = new OrientedBox[MaxTanks];
 
-    /// <summary>Shots fired this tick into a rock or the ground, in firing order. A tank fires at most once a tick.</summary>
+    /// <summary>Shots fired this tick into a rock, a wall or the ground, in firing order. A tank fires at most once a tick.</summary>
     private readonly BarrelBurst[] _barrelBursts = new BarrelBurst[MaxTanks];
     private int _barrelBurstCount;
 
-    /// <summary>A shell that bursts where its barrel meets a rock or the ground, instead of leaving the muzzle.</summary>
-    private readonly record struct BarrelBurst(Shell Shell, Vector3 At);
+    /// <summary>
+    /// A shell that bursts where its barrel meets a rock, a wall (<see cref="PropId"/>; a direct hit on it) or the ground
+    /// (<see cref="RayHit.TerrainId"/>), instead of leaving the muzzle.
+    /// </summary>
+    private readonly record struct BarrelBurst(Shell Shell, Vector3 At, int PropId);
 
     /// <summary>
     /// The shell phase of a tick, in two groups, each in firing order. This tick's shots that burst in a blocked barrel go
     /// off first (as the gun fires, before anything in flight can land), so they come before shells fired on earlier
-    /// ticks. Then every shell in flight flies one tick; its path stops at whichever comes first: the ground or a rock, or
-    /// a tank's hull or turret box (never the shooter's own). Shells that leave the map or time out vanish without an
-    /// event. Those still flying keep their order for the next tick.
+    /// ticks. Then every shell in flight flies one tick; its path stops at whichever comes first: the ground, a rock or a
+    /// standing wall, or a tank's hull or turret box (never the shooter's own). Fences and bushes it passes on the way are
+    /// knocked down. Shells that leave the map or time out vanish without an event. Those still flying keep their order
+    /// for the next tick. A wall a shell brings down no longer stops the shells after it, nor a barrel pushed into it
+    /// (<see cref="GoOff"/>).
     /// </summary>
     private void StepShells()
     {
-        for (int i = 0; i < _barrelBurstCount; i++)
-        {
-            ref readonly BarrelBurst burst = ref _barrelBursts[i];
-            Explode(burst.Shell, burst.At, directSlot: -1);
-        }
+        for (int i = 0; i < _barrelBurstCount; i++) GoOff(_barrelBursts[i]);
         _barrelBurstCount = 0;
         if (ShellCount == 0) return;
         SnapshotHitBoxes();
@@ -43,16 +44,48 @@ public sealed partial class Simulation
             Vector3 from = shell.Position;
             ShellStep step = Ballistics.Step(ref shell, Field, out RayHit rayHit);
             float groundT = step == ShellStep.Hit ? rayHit.T : 1f;
-            if (FindTankHit(shell.OwnerTankId, from, shell.Position, groundT, out int slot, out ArmorHit hit))
+            bool hitTank = FindTankHit(shell.OwnerTankId, from, shell.Position, groundT, out int slot, out ArmorHit hit);
+            KnockThrough(shell, from, shell.Position, hitTank ? hit.T : groundT);
+            if (hitTank)
                 StrikeTank(shell, slot, hit, shell.Position - from);
             else if (step == ShellStep.Hit)
-                Explode(shell, rayHit.Point, directSlot: -1);
+                StrikeProp(shell, rayHit.PropId, rayHit.Point, shell.Position - from);
             else if (step == ShellStep.Flying)
                 _shells[kept++] = shell;   // still flying: compact in place (kept <= i), keeping firing order
         }
         ShellCount = kept;
     }
 
+    /// <summary>
+    /// A shot fired into a blocked barrel. Usually it bursts where the barrel goes in. But if what blocked it was a wall
+    /// that has come down since the gun fired (shot down by an earlier burst, or rammed by a tank in a later slot), the
+    /// rest of the barrel decides: the shell bursts on whatever else is in it, or else leaves the muzzle and flies this
+    /// tick like any other shot (after this tick's other shots, if a slot is free; if not, it bursts in the rubble).
+    /// </summary>
+    private void GoOff(in BarrelBurst burst)
+    {
+        int index = burst.PropId == RayHit.TerrainId ? -1 : Field.IndexOf(burst.PropId);
+        if (index < 0 || !Field.PropAt(index).IsWall || Field.StandingAt(index))
+        {
+            StrikeProp(burst.Shell, burst.PropId, burst.At, burst.Shell.Velocity);
+            return;
+        }
+        if (Field.Raycast(burst.At, burst.Shell.Position, out RayHit next)) StrikeProp(burst.Shell, next.PropId, next.Point, burst.Shell.Velocity);
+        else if (ShellCount < MaxShells) _shells[ShellCount++] = burst.Shell;
+        else Explode(burst.Shell, burst.At, directSlot: -1);
+    }
+
+    /// <summary>
+    /// The shell stops at <paramref name="at"/> on the ground, a rock, or wall <paramref name="propId"/> (still standing:
+    /// rays only stop at standing walls, and <see cref="GoOff"/> checks again), flying along <paramref name="direction"/>.
+    /// </summary>
+    private void StrikeProp(in Shell shell, int propId, Vector3 at, Vector3 direction)
+    {
+        int index = propId == RayHit.TerrainId ? -1 : Field.IndexOf(propId);
+        if (index >= 0 && Field.PropAt(index).IsWall) StrikeWall(shell, index, at, direction);
+        else Explode(shell, at, directSlot: -1);
+    }
+
     /// <summary>Tanks don't move while shells fly, so their boxes are worked out once per tick.</summary>
     private void SnapshotHitBoxes()
     {
@@ -110,9 +143,10 @@ public sealed partial class Simulation
 
     /// <summary>
     /// The shell bursts at <paramref name="at"/>. A round with a splash radius (HE) hurts every other live tank in range,
-    /// except the shooter and <paramref name="directSlot"/> (the tank it struck directly, already dealt with).
+    /// except the shooter and <paramref name="directSlot"/> (the tank it struck directly, already dealt with), then the
+    /// props around it (<see cref="SplashProps"/>; <paramref name="directProp"/> is the wall it struck, if any).
     /// </summary>
-    private void Explode(in Shell shell, Vector3 at, int directSlot)
+    private void Explode(in Shell shell, Vector3 at, int directSlot, int directProp = -1)
     {
         _events.Add(new GameEvent(GameEventType.ShellExploded, Tick, shell.OwnerTankId, shell.Id, at, Vector3.Zero, Ammo: shell.Ammo));
         if (shell.Round.SplashRadius <= 0) return;
@@ -130,6 +164,7 @@ public sealed partial class Simulation
                 target.Id, HitResult.None, damage, shell.Ammo));
             Damage(ref target, damage, shell.OwnerTankId, shell.Id);
         }
+        SplashProps(shell, at, directProp);
     }
 
     /// <summary>
PATCH
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index e50f62a..726ac85 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -28,8 +28,11 @@ public sealed partial class Simulation
     private readonly Obb2[] _footprints = new Obb2[MaxTanks];
     private readonly Obb2[] _others = new Obb2[MaxTanks];
 
-    /// <summary>Sized for a busy tick (every tank firing, plus some hits) so a normal step never grows it.</summary>
-    private readonly List<GameEvent> _events = new(MaxTanks * 2);
+    /// <summary>
+    /// Sized for a busy tick (every tank firing, plus hits, and an HE burst in a bush patch, which alone can report a dozen
+    /// props down) so a normal step never grows it.
+    /// </summary>
+    private readonly List<GameEvent> _events = new(MaxTanks * 4);
 
     /// <summary>Events raised between steps (<see cref="Revive"/>); the next <see cref="Step"/> reports them.</summary>
     private readonly List<GameEvent> _pending = new(4);
@@ -215,11 +218,12 @@ public sealed partial class Simulation
         else tank.ApLeft--;
         _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity, Ammo: shell.Ammo));
 
-        // Barrel pushed into a rock or the ground: the shell bursts where the barrel meets it instead of spawning inside.
-        // It goes off in the shell phase, so every tank has had its turn before the splash, whatever the slots.
+        // Barrel pushed into a rock, a wall or the ground: the shell bursts where the barrel meets it instead of spawning
+        // inside (a wall takes it as a direct hit). It goes off in the shell phase, so every tank has had its turn before
+        // the splash, whatever the slots.
         if (launch.Blocked)
         {
-            _barrelBursts[_barrelBurstCount++] = new BarrelBurst(shell, launch.BlockPoint);
+            _barrelBursts[_barrelBurstCount++] = new BarrelBurst(shell, launch.BlockPoint, launch.BlockPropId);
             return;
         }
         _shells[ShellCount++] = shell;
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 377、Art 342；客户端 0 警告 0 错误（`GameEvent` 和 `Launch` 只在末尾加了带默认值的字段）。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): shells wear walls down to rubble, knock fences flat and shoot bushes away; HE splashes props"
```

---

### Task 4：坦克对物件

设计 §2.3、§3.3。车型数据新增 `BrickWallRamSpeed`：重坦 15 km/h，其他车无穷大（撞不倒）。`TankMotion.Drive` 算出这一 tick 要去的位置后，先撞倒和这个位置的车体占地重叠的栅栏；速度超过本车 `BrickWallRamSpeed` 时，重叠的砖墙段也变成瓦砾，速度减半（同一 tick 撞穿几段只减半一次）；然后才和还立着的物件做推出，所以车能直接开过去。算目标速度时，车体占地和瓦砾范围重叠就把最高速乘 0.7。`Drive` 新增可选参数带回撞倒的物件编号，`Simulation` 每辆车开完后据此发 `PropDestroyed`（`ShellId` 为 0，`Position` 是物件中心在地面上的点，`Velocity` 是车前进的方向；原地转向时没有前进方向，取从车体中心指向物件的方向）。`Drive` 会改动共用的 `Battlefield`，注释里写明只有推进战斗的仿真能调用它（阶段 4 的客户端预测不能直接用）。

**Files:**
- Modify: `src/Tank.Sim/Vehicles/TankSpec.cs`、`TankCatalog.cs`、`TankMotion.cs`、`src/Tank.Sim/Simulation.cs`、`Simulation.Props.cs`
- Test: 新建 `tests/Tank.Sim.Tests/RamTests.cs`（含原地转向撞倒栅栏、“撞墙时每 tick 不分配内存”）

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/RamTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Tanks driving north (−Z) into a three-segment line across x = 97 … 103 at z = 90: ids 1, 2, 3.</summary>
public sealed class RamTests
{
    private const float LineZ = 90f;
    private static readonly InputCmd Full = TestWorld.Cmd(throttle: 1);

    private static Battlefield Across(PropKind kind) =>
        TestWorld.FlatField(new WallLine(kind, new Vector2(97, LineZ), new Vector2(103, LineZ)).Segments(TestWorld.Flat(), 1).ToArray());

    /// <summary>A <paramref name="spec"/> tank at rest whose nose is <paramref name="gap"/> m short of the line.</summary>
    private static TankState Facing(TankSpec spec, float gap) =>
        new() { Id = 1, Spec = spec, Position = new Vector2(100, LineZ + 0.5f + gap + spec.HullLength / 2), Yaw = 0f };

    private static bool AllDown(Battlefield field) => field.Props.All(p => !field.IsStanding(p.Id));

    private static bool AllStanding(Battlefield field) => field.Props.All(p => field.IsStanding(p.Id));

    [Fact]
    public void Only_the_heavy_tank_rams_brick_walls_and_from_15_kmh()
    {
        Assert.Equal(15f / 3.6f, TankCatalog.Heavy.BrickWallRamSpeed, 1e-5f);
        Assert.All(TankCatalog.All.Where(s => s != TankCatalog.Heavy), s => Assert.Equal(float.PositiveInfinity, s.BrickWallRamSpeed));
    }

    [Theory]
    [InlineData("light")]
    [InlineData("medium")]
    [InlineData("heavy")]
    [InlineData("td")]
    public void Any_tank_knocks_fences_flat_and_drives_on_as_if_they_were_not_there(string id)
    {
        TankSpec spec = TankCatalog.ById(id);
        Battlefield fenced = Across(PropKind.Fence);
        TankState through = Facing(spec, 10f), open = Facing(spec, 10f);
        var felled = new List<int>();
        for (int i = 0; i < 4 * SimConstants.TickRate; i++)
        {
            TankMotion.Drive(ref through, Full, fenced, ReadOnlySpan<Obb2>.Empty, felled);
            TankMotion.Drive(ref open, Full, TestWorld.FlatField(), ReadOnlySpan<Obb2>.Empty);
        }
        Assert.Equal(new[] { 1, 2, 3 }, felled.Order());   // every panel the hull touched (it is wider than one)
        Assert.True(AllDown(fenced));
        Assert.Equal(open.Position, through.Position);
        Assert.Equal(open.Speed, through.Speed);
    }

    [Fact]
    public void A_heavy_tank_above_15_kmh_breaks_through_a_brick_wall_at_half_its_speed()
    {
        Battlefield field = Across(PropKind.BrickWall);
        TankState heavy = Facing(TankCatalog.Heavy, 12f);   // room to pass 15 km/h first
        var felled = new List<int>();
        float before = 0f;
        int ticks = 0;
        while (felled.Count == 0 && ticks++ < 10 * SimConstants.TickRate)
        {
            before = heavy.Speed;
            TankMotion.Drive(ref heavy, Full, field, ReadOnlySpan<Obb2>.Empty, felled);
        }

        Assert.Equal(new[] { 1, 2, 3 }, felled.Order());   // all in one tick: the hull spans the three segments
        Assert.True(AllDown(field));
        Assert.True(before > TankCatalog.Heavy.BrickWallRamSpeed, $"rammed at {before * 3.6f:F1} km/h");
        float accelerated = MathF.Min(before + TankCatalog.Heavy.Acceleration * SimConstants.Dt, TankCatalog.Heavy.MaxForwardSpeed);
        Assert.Equal(accelerated * 0.5f, heavy.Speed, 1e-4f);   // halved once, not once per segment

        TestWorld.Drive(ref heavy, field, Full, 2 * SimConstants.TickRate);
        Assert.True(heavy.Position.Y < LineZ - TankCatalog.Heavy.HullLength / 2, $"stuck at z = {heavy.Position.Y}");
    }

    [Fact]
    public void A_heavy_tank_slower_than_15_kmh_is_stopped_by_a_brick_wall()
    {
        Battlefield field = Across(PropKind.BrickWall);
        TankState heavy = Facing(TankCatalog.Heavy, 2f);   // reaches only about 12 km/h by the wall
        float fastest = 0f;
        for (int i = 0; i < 3 * SimConstants.TickRate; i++)
        {
            TankMotion.Drive(ref heavy, Full, field, ReadOnlySpan<Obb2>.Empty);
            fastest = MathF.Max(fastest, heavy.Speed);
        }
        Assert.True(fastest < TankCatalog.Heavy.BrickWallRamSpeed, $"reached {fastest * 3.6f:F1} km/h");
        Assert.True(AllStanding(field));
        Assert.True(heavy.Position.Y > LineZ + 0.3f + TankCatalog.Heavy.HullLength / 2 - 0.01f);
    }

    [Theory]
    [InlineData("medium", PropKind.BrickWall)]
    [InlineData("light", PropKind.BrickWall)]
    [InlineData("heavy", PropKind.StoneWall)]
    public void Other_rams_are_stopped_like_a_rock(string id, PropKind kind)
    {
        Battlefield field = Across(kind);
        TankState tank = Facing(TankCatalog.ById(id), 25f);   // flat out by the time it arrives
        TestWorld.Drive(ref tank, field, Full, 5 * SimConstants.TickRate);
        Assert.True(AllStanding(field));
        Assert.True(tank.Position.Y > LineZ + WallLine.Thickness(kind) / 2 + tank.Spec.HullLength / 2 - 0.01f);
    }

    [Fact]
    public void Rubble_holds_a_tank_to_70_percent_of_its_top_speed()
    {
        // A long brick wall shot to rubble along the tank's whole run, heading east along it.
        Prop[] wall = new WallLine(PropKind.BrickWall, new Vector2(60, 100), new Vector2(160, 100)).Segments(TestWorld.Flat(), 1).ToArray();
        Battlefield field = TestWorld.FlatField(wall);
        foreach (Prop segment in wall) field.Destroy(field.IndexOf(segment.Id));
        TankState onRubble = TestWorld.Tank(70, 100, yaw: -MathF.PI / 2), open = TestWorld.Tank(70, 100, yaw: -MathF.PI / 2);
        TestWorld.Drive(ref onRubble, field, Full, 6 * SimConstants.TickRate);
        TestWorld.Drive(ref open, TestWorld.FlatField(), Full, 6 * SimConstants.TickRate);

        Assert.True(onRubble.Position.X < 155, "still on the rubble");
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed, open.Speed, 1e-4f);
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed * TankMotion.RubbleSpeedFactor, onRubble.Speed, 1e-4f);
    }

    [Fact]
    public void A_hull_just_clear_of_the_rubble_is_not_slowed()
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(98, 100), new Vector2(100, 100)).Segments(TestWorld.Flat(), 1).Single();
        Battlefield field = TestWorld.FlatField(wall);
        field.Destroy(0);
        // Heading east 1.65 m south of the rubble's edge (z = 100.8): the hull reaches 1.6 m to either side.
        TankState tank = TestWorld.Tank(60, 102.45f, yaw: -MathF.PI / 2);
        TestWorld.Drive(ref tank, field, Full, 6 * SimConstants.TickRate);
        Assert.Equal(TankCatalog.Medium.MaxForwardSpeed, tank.Speed, 1e-4f);
    }

    [Fact]
    public void Turning_on_the_spot_into_a_fence_knocks_it_away_from_the_hull()
    {
        // A fence panel square across the hull's right-front corner: turning right on the spot swings the corner into it.
        Prop panel = new WallLine(PropKind.Fence, new Vector2(102.6f, 95.5f), new Vector2(102.6f, 97.5f)).Segments(TestWorld.Flat(), 1).Single();
        var sim = new Simulation(TestWorld.FlatField(panel));
        int id = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        Assert.False(Obb2.TryGetPenetration(TankMotion.Footprint(TankCatalog.Medium, new Vector2(100, 100), 0f), panel.Footprint, out _), "clear to start");
        var events = new List<GameEvent>();
        for (int i = 0; i < 2 * SimConstants.TickRate && sim.Field.IsStanding(1); i++)
        {
            sim.Step(new[] { TestWorld.Cmd(steer: 1) });
            events.AddRange(sim.Events);
        }

        GameEvent down = Assert.Single(events, e => e.Type == GameEventType.PropDestroyed);
        Assert.Equal((id, 0), (down.TankId, down.ShellId));
        Vector3 away = Vector3.Normalize(new Vector3(panel.Footprint.Center.X - 100, 0, panel.Footprint.Center.Y - 100));
        Assert.True(Vector3.Dot(away, down.Velocity) > 0.999f, $"pushed {down.Velocity}");
        Assert.NotEqual(0f, sim.Tanks[0].Yaw);   // the turn went on
    }

    [Fact]
    public void Ramming_allocates_nothing_once_warmed_up()
    {
        // Every 3 s a medium tank is put back 4.65 m short of a row of fences and a heavy tank 5.95 m short of a brick
        // wall, everything stands again, and both drive flat out: the medium knocks fences down, the heavy breaks through.
        HeightField flat = TestWorld.Flat();
        var props = new List<Prop>();
        props.AddRange(new WallLine(PropKind.Fence, new Vector2(94, 90), new Vector2(106, 90)).Segments(flat, 1));
        props.AddRange(new WallLine(PropKind.BrickWall, new Vector2(124, 90), new Vector2(136, 90)).Segments(flat, props.Count + 1));
        var sim = new Simulation(TestWorld.Field(flat, props.ToArray()));
        int medium = sim.AddTank(TankCatalog.Medium, new Vector2(100, 98), 0f), heavy = sim.AddTank(TankCatalog.Heavy, new Vector2(130, 100), 0f);
        var inputs = new[] { Full, Full };
        int fences = 0, walls = 0;
        void Run(int cycles)
        {
            for (int cycle = 0; cycle < cycles; cycle++)
            {
                sim.TankRef(medium) = sim.TankRef(medium) with { Position = new Vector2(100, 98), Yaw = 0f, Speed = 0f };
                sim.TankRef(heavy) = sim.TankRef(heavy) with { Position = new Vector2(130, 100), Yaw = 0f, Speed = 0f };
                for (int i = 0; i < sim.Field.Props.Count; i++) sim.Field.Restore(i);
                for (int tick = 0; tick < 3 * SimConstants.TickRate; tick++)
                {
                    sim.Step(inputs);
                    foreach (GameEvent e in sim.Events)
                        if (e.Type == GameEventType.PropDestroyed) _ = e.PropId <= 6 ? fences++ : walls++;
                }
            }
        }
        Run(2);
        (fences, walls) = (0, 0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run(4);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(fences >= 8 && walls >= 8, $"{fences} fences and {walls} walls knocked down while measured");
    }

    [Fact]
    public void The_simulation_reports_what_a_tank_drove_down()
    {
        var sim = new Simulation(Across(PropKind.Fence));
        int id = sim.AddTank(TankCatalog.Medium, Facing(TankCatalog.Medium, 3f).Position, 0f);
        var events = new List<GameEvent>();
        for (int i = 0; i < 2 * SimConstants.TickRate; i++)
        {
            sim.Step(new[] { Full });
            events.AddRange(sim.Events);
        }

        GameEvent[] down = events.Where(e => e.Type == GameEventType.PropDestroyed).ToArray();
        Assert.Equal(new[] { 1, 2, 3 }, down.Select(e => e.PropId).Order());
        Assert.All(down, e =>
        {
            Assert.Equal((id, 0), (e.TankId, e.ShellId));
            Assert.Equal(new Vector3(0, 0, -1), e.Velocity);   // pushed the way the hull was going
            Assert.Equal(LineZ, e.Position.Z, 1e-4f);
        });
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`TankSpec.BrickWallRamSpeed`、`TankMotion.RubbleSpeedFactor` 找不到，`Drive` 没有第五个参数。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Simulation.Props.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.Props.cs b/src/Tank.Sim/Simulation.Props.cs
index c4a3098..e24f159 100644
--- a/src/Tank.Sim/Simulation.Props.cs
+++ b/src/Tank.Sim/Simulation.Props.cs
@@ -2,12 +2,34 @@ using System.Numerics;
 using Tank.Sim.Combat;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
+using Tank.Sim.Vehicles;
 
 namespace Tank.Sim;
 
-/// <summary>What shells do to props (design §5.5): wear walls down to rubble, knock fences flat, shoot bushes away.</summary>
+/// <summary>
+/// What shells and tanks do to props (design §4.2, §5.5): shells wear walls down to rubble, knock fences flat and shoot
+/// bushes away; tanks knock fences flat, and a fast heavy tank brick walls.
+/// </summary>
 public sealed partial class Simulation
 {
+    /// <summary>Ids of the props the tank now driving knocked down this tick; sized so a normal tick never grows it.</summary>
+    private readonly List<int> _felled = new(8);
+
+    /// <summary>Reports what <paramref name="tank"/> just drove down (<see cref="_felled"/>), pushed the way it was going.</summary>
+    private void ReportFelled(in TankState tank)
+    {
+        Vector2 heading = Angles.Forward(tank.Yaw);
+        foreach (int id in _felled)
+        {
+            int index = Field.IndexOf(id);
+            Vector2 center = Field.PropAt(index).Footprint.Center;
+            // Turning on the spot it has no travel: pushed away from the hull's centre instead.
+            Vector2 push = tank.Speed != 0 ? heading * MathF.Sign(tank.Speed) : Level(new Vector3(center.X - tank.Position.X, 0, center.Y - tank.Position.Y), heading);
+            PropDown(index, tank.Id, shellId: 0, ammo: default, new Vector3(center.X, Field.Terrain.HeightAt(center.X, center.Y), center.Y), push);
+        }
+        _felled.Clear();
+    }
+
     /// <summary>
     /// Fences and bushes the shell's path enters before <paramref name="stopT"/> (where it stops this tick, or 1) go down;
     /// the shell flies on. One the path starts inside is left standing: that is where the gun was when it fired (a tank
PATCH
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index 726ac85..4be8fa8 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -9,11 +9,12 @@ namespace Tank.Sim;
 
 /// <summary>
 /// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted; each tank drives
-/// (a casemate gun's hull turning by itself towards an aim outside the arc), aims, updates its aim circle and fires (in
-/// slot order) against that snapshot, touching no other tank, so its turn doesn't depend on its slot (except that when
-/// the shell pool is full lower slots get to fire first, and shell ids follow slot order). Then the shell phase: shots
-/// that burst in a blocked barrel go off, then every shell in flight flies and resolves its hits, each group in firing
-/// order (shots of the same tick in slot order).
+/// (a casemate gun's hull turning by itself towards an aim outside the arc, knocking down fences and, rammed fast
+/// enough, brick walls in its way), aims, updates its aim circle and fires (in slot order) against that snapshot,
+/// touching no other tank, so its turn doesn't depend on its slot (except that when the shell pool is full lower slots
+/// get to fire first, shell ids follow slot order, and a prop one tank knocks down is already down for the next). Then
+/// the shell phase: shots that burst in a blocked barrel go off, then every shell in flight flies and resolves its hits,
+/// each group in firing order (shots of the same tick in slot order).
 /// </summary>
 public sealed partial class Simulation
 {
@@ -100,7 +101,8 @@ public sealed partial class Simulation
             if (tank.IsDestroyed) continue;   // wrecks don't drive, aim or fire, but still block (see SnapshotFootprints)
             InputCmd input = TankMotion.TraverseHull(tank, Sanitize(inputs[i], tank), Field.Terrain);
             float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
-            TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i));
+            TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i), _felled);
+            ReportFelled(tank);
             TankMotion.AimGun(ref tank, input, Field.Terrain);
             UpdateBloom(ref tank, yaw, turretYaw);
             TryFire(ref tank, input);
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index 9dd227b..6e13167 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -28,6 +28,7 @@ public static class TankCatalog
         GunDepression = Angles.Deg(8),
         GunElevation = Angles.Deg(20),
         MaxClimb = Angles.Deg(32),
+        BrickWallRamSpeed = float.PositiveInfinity,
         TrunnionHeight = 1.85f,
         TurretOffset = 0f,
         GunLength = 3.0f,
@@ -60,6 +61,7 @@ public static class TankCatalog
         GunDepression = Angles.Deg(7),
         GunElevation = Angles.Deg(20),
         MaxClimb = Angles.Deg(32),
+        BrickWallRamSpeed = float.PositiveInfinity,
         TrunnionHeight = 2.1f,
         TurretOffset = 0.3f,
         GunLength = 3.2f,
@@ -92,6 +94,7 @@ public static class TankCatalog
         GunDepression = Angles.Deg(6),
         GunElevation = Angles.Deg(18),
         MaxClimb = Angles.Deg(32),
+        BrickWallRamSpeed = 15 * Kmh,
         TrunnionHeight = 2.55f,
         TurretOffset = 0.2f,
         GunLength = 3.8f,
@@ -128,6 +131,7 @@ public static class TankCatalog
         GunDepression = Angles.Deg(6),
         GunElevation = Angles.Deg(15),
         MaxClimb = Angles.Deg(32),
+        BrickWallRamSpeed = float.PositiveInfinity,
         TrunnionHeight = 1.85f,
         TurretOffset = 2.8f,
         GunLength = 3.5f,   // the muzzle 2.8 m past the nose (playtest)
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankMotion.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankMotion.cs b/src/Tank.Sim/Vehicles/TankMotion.cs
index 6ad4fa0..9ee4295 100644
--- a/src/Tank.Sim/Vehicles/TankMotion.cs
+++ b/src/Tank.Sim/Vehicles/TankMotion.cs
@@ -24,11 +24,22 @@ public static class TankMotion
     /// <summary>If collisions leave less than this fraction of the intended step, the hull was blocked head-on and stalls.</summary>
     private const float StallRatio = 0.25f;
 
+    /// <summary>Top speed on rubble, as a share of the usual (design §3.2).</summary>
+    public const float RubbleSpeedFactor = 0.7f;
+
+    /// <summary>Breaking through brick walls costs this share of the hull's speed (once a tick, however many it breaks).</summary>
+    private const float RamSpeedKept = 0.5f;
+
     public static Obb2 Footprint(TankSpec spec, Vector2 center, float yaw) =>
         new(center, new Vector2(spec.HullWidth / 2, spec.HullLength / 2), yaw);
 
-    /// <summary>Applies throttle and steering, then moves the hull and pushes it out of standing solid props and <paramref name="obstacles"/>.</summary>
-    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles)
+    /// <summary>
+    /// Applies throttle and steering, knocks down what gives way where the hull is going (<see cref="KnockDown"/>; their
+    /// ids are added to <paramref name="felled"/> if given), then moves the hull and pushes it out of standing solid props
+    /// and <paramref name="obstacles"/>. Knocking props down changes <paramref name="field"/> for everyone using it, so
+    /// only the simulation stepping the battle should drive on it (a client predicting its own tank must not).
+    /// </summary>
+    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles, List<int>? felled = null)
     {
         TankSpec spec = t.Spec;
         const float dt = SimConstants.Dt;
@@ -37,12 +48,13 @@ public static class TankMotion
         float turnRate = spec.HullTraverse * (MathF.Abs(t.Speed) > MovingThreshold ? MovingTurnFactor : 1f);
         t.Yaw = Angles.Wrap(t.Yaw - System.Math.Sign(input.Steer) * turnRate * dt);
 
-        float target = TargetSpeed(t, input.Throttle, field.Terrain);
+        float target = TargetSpeed(t, input.Throttle, field);
         bool accelerating = target * t.Speed >= 0 && MathF.Abs(target) > MathF.Abs(t.Speed);
         t.Speed = Scalar.MoveTowards(t.Speed, target, (accelerating ? spec.Acceleration : spec.Braking) * dt);
 
         Vector2 start = t.Position;
         Vector2 intended = Angles.Forward(t.Yaw) * (t.Speed * dt);
+        KnockDown(ref t, Footprint(spec, start + intended, t.Yaw), field, felled);
         Vector2 dest = field.ClampToPlayable(Resolve(Footprint(spec, start + intended, t.Yaw), field, obstacles));
         if (IsTooSteepToEnter(spec, field.Terrain, start, dest)) dest = start;
         (t.Position, t.Yaw) = SettleWithoutOverlap(spec, dest, t.Yaw, oldYaw, start, field, obstacles);
@@ -101,10 +113,48 @@ public static class TankMotion
         return MathF.Atan2(front - back, t.Spec.HullLength);
     }
 
-    private static float TargetSpeed(in TankState t, sbyte throttle, HeightField terrain)
+    /// <summary>
+    /// What gives way to a hull about to stand on <paramref name="hull"/> (design §4.2): any fence, and brick walls when it
+    /// is faster than its <see cref="TankSpec.BrickWallRamSpeed"/>, which then costs it half its speed (once, however many
+    /// it breaks). They go down before the hull is pushed out of what still stands, so it drives on through.
+    /// </summary>
+    private static void KnockDown(ref TankState t, in Obb2 hull, Battlefield field, List<int>? felled)
+    {
+        bool ramming = MathF.Abs(t.Speed) > t.Spec.BrickWallRamSpeed;
+        bool broke = false;
+        foreach (int i in field.Blockers)
+        {
+            ref readonly Prop prop = ref field.PropAt(i);
+            bool givesWay = prop.Kind == PropKind.Fence || (ramming && prop.Kind == PropKind.BrickWall);
+            if (!givesWay || !field.StandingAt(i) || !Overlaps(hull, prop.Footprint)) continue;
+            field.Destroy(i);
+            felled?.Add(prop.Id);
+            broke |= prop.Kind == PropKind.BrickWall;
+        }
+        if (broke) t.Speed *= RamSpeedKept;
+    }
+
+    /// <summary>The hull lies partly on the rubble of a wall.</summary>
+    private static bool OnRubble(in Obb2 hull, Battlefield field)
+    {
+        foreach (int i in field.Walls)
+            if (!field.StandingAt(i) && Overlaps(hull, field.PropAt(i).RubbleFootprint)) return true;
+        return false;
+    }
+
+    private static bool Overlaps(in Obb2 a, in Obb2 b)
+    {
+        float reach = a.HalfExtents.Length() + b.HalfExtents.Length();
+        return Vector2.DistanceSquared(a.Center, b.Center) <= reach * reach && Obb2.TryGetPenetration(a, b, out _);
+    }
+
+    /// <summary>The speed the throttle asks for: lower on rubble (<see cref="RubbleSpeedFactor"/>) and uphill.</summary>
+    private static float TargetSpeed(in TankState t, sbyte throttle, Battlefield field)
     {
         TankSpec spec = t.Spec;
         float target = throttle > 0 ? spec.MaxForwardSpeed : throttle < 0 ? -spec.MaxReverseSpeed : 0f;
+        if (target != 0 && OnRubble(Footprint(spec, t.Position, t.Yaw), field)) target *= RubbleSpeedFactor;
+        HeightField terrain = field.Terrain;
         float uphill = ForwardSlope(t, terrain) * MathF.Sign(target);
         if (uphill <= 0) return target;
         if (uphill >= spec.MaxClimb) return 0f;
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankSpec.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankSpec.cs b/src/Tank.Sim/Vehicles/TankSpec.cs
index 74f6104..a972a4b 100644
--- a/src/Tank.Sim/Vehicles/TankSpec.cs
+++ b/src/Tank.Sim/Vehicles/TankSpec.cs
@@ -36,6 +36,12 @@ public sealed record TankSpec
     public required float GunElevation { get; init; }
     public required float MaxClimb { get; init; }
 
+    /// <summary>
+    /// Above this speed (m/s, either way) the hull drives straight through brick walls, turning them to rubble and losing
+    /// half its speed (design §4.2: only the heavy tank, above 15 km/h). <see cref="float.PositiveInfinity"/>: it can't.
+    /// </summary>
+    public required float BrickWallRamSpeed { get; init; }
+
     /// <summary>Gun pivot height above the hull origin.</summary>
     public required float TrunnionHeight { get; init; }
 
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 392、Art 342。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): tanks knock fences flat, a heavy tank above 15 km/h rams through brick walls, rubble slows to 70%"
```

---

### Task 5：恢复

设计 §2.4、§3.5。`Simulation.RestoreProps()` 把所有被毁或受损的物件恢复原样；如果某段墙或某个栅栏的占地和任何一辆坦克（包括残骸）的车体占地重叠，这次跳过它。草丛不挡车，总是恢复。不发事件，客户端每帧按状态更新外观。

**Files:**
- Modify: `src/Tank.Sim/Simulation.Props.cs`
- Test: 新建 `tests/Tank.Sim.Tests/RestorePropsTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/RestorePropsTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class RestorePropsTests
{
    // A brick wall (ids 1–3) and a fence (4–6) across x = 97 … 103, at z = 60 and z = 140, and a bush (7) at (40, 100).
    private static Simulation Yard()
    {
        HeightField flat = TestWorld.Flat();
        var props = new List<Prop>();
        props.AddRange(new WallLine(PropKind.BrickWall, new Vector2(97, 60), new Vector2(103, 60)).Segments(flat, 1));
        props.AddRange(new WallLine(PropKind.Fence, new Vector2(97, 140), new Vector2(103, 140)).Segments(flat, 4));
        props.Add(new Prop(7, PropKind.Bush, new Obb2(new Vector2(40, 100), new Vector2(3, 3), 0), -0.2f, 4f));
        return new Simulation(TestWorld.Field(flat, props.ToArray()));
    }

    [Fact]
    public void Everything_comes_back_as_it_was()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        field.Damage(field.IndexOf(1), 120);   // damaged
        field.Damage(field.IndexOf(2), 999);   // rubble
        field.Destroy(field.IndexOf(5));        // knocked flat
        field.Destroy(field.IndexOf(7));        // shot away

        sim.RestoreProps();

        Assert.All(field.Props, p =>
        {
            Assert.True(field.IsStanding(p.Id), $"prop {p.Id} still down");
            Assert.Equal(p.MaxDurability, field.Durability(p.Id));
        });
    }

    [Fact]
    public void A_wall_or_fence_with_a_tank_on_it_waits_for_the_next_time()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        field.Damage(field.IndexOf(2), 999);
        field.Destroy(field.IndexOf(5));
        int onRubble = sim.AddTank(TankCatalog.Heavy, new Vector2(100, 60), 0f);
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 140), 0f);
        sim.RestoreProps();
        Assert.False(field.IsStanding(2));
        Assert.False(field.IsStanding(5));
        Assert.True(field.IsStanding(1) && field.IsStanding(4), "the rest is untouched (never down)");

        // Knocked out where it stands: a wreck holds the rubble down too.
        sim.TankRef(onRubble).IsDestroyed = true;
        sim.RestoreProps();
        Assert.False(field.IsStanding(2));
    }

    [Fact]
    public void Once_the_tank_has_moved_off_the_next_restore_brings_it_back()
    {
        Simulation sim = Yard();
        Battlefield field = sim.Field;
        field.Damage(field.IndexOf(2), 999);
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 60), MathF.PI / 2);   // across the rubble, heading west
        sim.RestoreProps();
        Assert.False(field.IsStanding(2));

        var west = new[] { TestWorld.Cmd(throttle: 1) };
        for (int i = 0; i < 3 * SimConstants.TickRate; i++) sim.Step(west);
        Assert.True(sim.Tanks[0].Position.X < 90, "drove off the rubble");
        sim.RestoreProps();
        Assert.True(field.IsStanding(2));
        Assert.Equal(300, field.Durability(2));
    }

    [Fact]
    public void A_bush_comes_back_even_around_a_tank()
    {
        Simulation sim = Yard();
        sim.Field.Destroy(sim.Field.IndexOf(7));
        sim.AddTank(TankCatalog.Medium, new Vector2(40, 100), 0f);
        sim.RestoreProps();
        Assert.True(sim.Field.IsStanding(7));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Simulation.RestoreProps` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Simulation.Props.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.Props.cs b/src/Tank.Sim/Simulation.Props.cs
index e24f159..4268e07 100644
--- a/src/Tank.Sim/Simulation.Props.cs
+++ b/src/Tank.Sim/Simulation.Props.cs
@@ -30,6 +30,32 @@ public sealed partial class Simulation
         _felled.Clear();
     }
 
+    /// <summary>
+    /// Puts every destroyed or damaged prop back as it was (sandbox only for now), except a wall or fence with a tank, or a
+    /// wreck, on its footprint: that one stays as it is until a later call. Bushes block nothing, so they always come
+    /// back. No events: views follow <see cref="Battlefield.IsStanding"/> and <see cref="Battlefield.Durability"/>.
+    /// </summary>
+    public void RestoreProps()
+    {
+        for (int i = 0; i < Field.Props.Count; i++)
+        {
+            ref readonly Prop prop = ref Field.PropAt(i);
+            if (!prop.IsDestructible || Field.DurabilityAt(i) == prop.MaxDurability) continue;
+            if (prop.IsSolid && UnderATank(prop.Footprint)) continue;
+            Field.Restore(i);
+        }
+    }
+
+    private bool UnderATank(in Obb2 footprint)
+    {
+        for (int j = 0; j < TankCount; j++)
+        {
+            ref readonly TankState t = ref _tanks[j];
+            if (Obb2.TryGetPenetration(TankMotion.Footprint(t.Spec, t.Position, t.Yaw), footprint, out _)) return true;
+        }
+        return false;
+    }
+
     /// <summary>
     /// Fences and bushes the shell's path enters before <paramref name="stopT"/> (where it stops this tick, or 1) go down;
     /// the shell flies on. One the path starts inside is left standing: that is where the gun was when it fired (a tank
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 396、Art 342。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): restore every prop, leaving walls and fences under a tank for later"
```

---

### Task 6：院子、掩体和第 7 辆靶车

设计 §3.6。`SandboxMap.WallLines` 按线写出院子和掩体，`Create` 在岩石和草丛之后展开它们，所以原有物件的编号和形状都不变。院子在出生点西边约 40 m 的平地上，内部 18 × 12.8 m、中心约 (160, 340)：东边石墙 7 段（朝出生点），北边、南边砖墙各 9 段，西边砖墙 2 段 + 栅栏 3 个 + 砖墙 2 段。转角处各留 1 cm，墙段之间不重叠。院外：出生点西北约 30 m 一段 3 段长的砖墙、东边约 45 m 一段 2 段长的石墙，两排各 3 个的栅栏（一排在通往院子的路上，一排在东北方向）。第 7 辆靶车是中坦，停在院子正中，车头朝出生点（`SandboxMap.YardTarget` = 6），从出生点看三条视线都被石墙挡住。测试检查摆放规则；原有的“从出生点看得见靶车”和“打靶演示停在空地上”的测试会自动把墙算进去；`RangeDemoTests` 另加“原有演示的弹道上没有墙和岩石”。

**Files:**
- Modify: `src/Tank.Sim/Maps/SandboxMap.cs`
- Test: 新建 `tests/Tank.Sim.Tests/SandboxWallsTests.cs`；改 `TargetSpotTests.cs`、`ShellPropTests.cs`、`tests/Tank.Art.Tests/RangeDemoTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/RangeDemoTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/RangeDemoTests.cs b/tests/Tank.Art.Tests/RangeDemoTests.cs
index e880f03..5c0df24 100644
--- a/tests/Tank.Art.Tests/RangeDemoTests.cs
+++ b/tests/Tank.Art.Tests/RangeDemoTests.cs
@@ -32,6 +32,20 @@ public class RangeDemoTests
             Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(s.Spec, s.Position, s.Yaw), out _), "parked on a target");
     }
 
+    [Theory]
+    [MemberData(nameof(DemosAndTanks))]
+    public void Shoots_along_a_line_no_wall_or_rock_stands_in(string demo, string tank)
+    {
+        RangeDemo d = RangeDemo.Find(demo)!;
+        TankSpec spec = TankCatalog.ById(tank);
+        var parked = new TankState { Spec = spec, Position = d.PlayerPosition, Yaw = d.PlayerYaw };
+        System.Numerics.Vector3 trunnion = TankPose.Compute(parked, Field.Terrain).Trunnion(spec);
+        System.Numerics.Vector2 spot = d.Spot.Position;
+        System.Numerics.Vector3 aim = d.AimPoint(Field.Terrain.HeightAt(spot.X, spot.Y));
+        bool blocked = Field.Raycast(trunnion, aim, out RayHit hit);
+        Assert.False(blocked, $"stopped by {(blocked && hit.PropId != RayHit.TerrainId ? Field.Props.Single(p => p.Id == hit.PropId).Kind : "the ground")} at {hit.Point}");
+    }
+
     [Fact]
     public void Knows_every_demo_and_nothing_else()
     {
PATCH
```

新建 `tests/Tank.Sim.Tests/SandboxWallsTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class SandboxWallsTests
{
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly Prop[] Walls = Field.Props.Where(p => p.IsWall || p.Kind == PropKind.Fence).ToArray();
    private static readonly Vector2 Yard = SandboxMap.TargetSpots[SandboxMap.YardTarget].Position;

    public static TheoryData<int> WallIds()
    {
        var data = new TheoryData<int>();
        foreach (Prop p in Walls) data.Add(p.Id);
        return data;
    }

    private static Prop ById(int id) => Field.Props.Single(p => p.Id == id);

    [Fact]
    public void The_yard_and_the_cover_hold_25_brick_9_stone_and_9_fence_segments()
    {
        Assert.Equal(25, Walls.Count(p => p.Kind == PropKind.BrickWall));
        Assert.Equal(9, Walls.Count(p => p.Kind == PropKind.StoneWall));
        Assert.Equal(9, Walls.Count(p => p.Kind == PropKind.Fence));
    }

    [Fact]
    public void Wall_ids_follow_on_from_the_bushes()
    {
        int lastBush = Field.Props.Where(p => p.Kind == PropKind.Bush).Max(p => p.Id);
        Assert.Equal(Enumerable.Range(lastBush + 1, Walls.Length), Walls.Select(p => p.Id));
        Assert.Equal(Field.Props.Count, Walls[^1].Id);
    }

    [Fact]
    public void No_two_segments_overlap()
    {
        for (int a = 0; a < Walls.Length; a++)
        for (int b = a + 1; b < Walls.Length; b++)
            Assert.False(Obb2.TryGetPenetration(Walls[a].Footprint, Walls[b].Footprint, out _), $"{Walls[a].Id} and {Walls[b].Id} overlap");
    }

    [Theory]
    [MemberData(nameof(WallIds))]
    public void Stands_on_flat_open_ground_clear_of_rocks_bushes_targets_the_spawn_and_the_lane(int id)
    {
        Prop wall = ById(id);
        Obb2 box = wall.Footprint;
        foreach (Prop p in Field.Props)
        {
            if (p.Kind == PropKind.Rock) Assert.False(Obb2.TryGetPenetration(box, p.Footprint, out _), $"on rock {p.Id}");
            if (p.Kind == PropKind.Bush) Assert.False(box.IntersectsCircle(p.Footprint.Center, p.Footprint.HalfExtents.X), $"in bush {p.Id}");
        }
        foreach (TargetSpot t in SandboxMap.TargetSpots)
            Assert.False(Obb2.TryGetPenetration(box, TankMotion.Footprint(t.Spec, t.Position, t.Yaw), out _), "on a target");
        Assert.False(Obb2.TryGetPenetration(box, TankMotion.Footprint(TankCatalog.Heavy, SandboxMap.Spawn, SandboxMap.SpawnYaw), out _));
        float lane = SandboxMap.DistanceToSegment(box.Center, SandboxMap.Spawn, SandboxMap.BigHill) - box.HalfExtents.Length();
        Assert.True(lane >= SandboxMap.LaneHalfWidth, $"{lane:F1} m from the lane");
        Assert.Equal(box.Center, Field.ClampToPlayable(box.Center));
        float slope = MathF.Acos(Field.Terrain.NormalAt(box.Center.X, box.Center.Y).Y);
        Assert.True(slope < Angles.Deg(8), $"{slope * 180 / MathF.PI:F1}° under it");
    }

    [Fact]
    public void The_yard_is_walled_in_but_for_the_fence_gate_on_its_far_side()
    {
        // From the yard target's turret height, in every direction: the ray either meets a wall or leaves through the gate.
        var eye = new Vector3(Yard.X, Field.Terrain.HeightAt(Yard.X, Yard.Y) + 2f, Yard.Y);
        int throughTheGate = 0;
        for (int degrees = 0; degrees < 360; degrees += 5)
        {
            Vector2 d = Angles.Forward(Angles.Deg(degrees));
            Vector3 far = eye + new Vector3(d.X, 0, d.Y) * 30f;
            if (Field.Raycast(eye, far, out RayHit hit) && hit.PropId != RayHit.TerrainId && ById(hit.PropId).IsWall) continue;
            float z = Yard.Y + d.Y * (150.69f - Yard.X) / d.X;   // where the ray crosses the west wall's line
            Assert.True(d.X < 0 && z is > 337 and < 343, $"the ray at {degrees}° gets out (crosses x = 150.69 at z = {z:F1})");
            throughTheGate++;
        }
        Assert.True(throughTheGate >= 3, "the gate is open to rays (fences don't stop them)");
    }

    [Fact]
    public void The_yard_faces_the_spawn_with_stone()
    {
        var eye = new Vector3(SandboxMap.Spawn.X, Field.Terrain.HeightAt(SandboxMap.Spawn.X, SandboxMap.Spawn.Y) + 2.1f, SandboxMap.Spawn.Y);
        var plate = new Vector3(Yard.X, Field.Terrain.HeightAt(Yard.X, Yard.Y) + 1f, Yard.Y);
        Assert.True(Field.Raycast(eye, plate, out RayHit hit));
        Assert.Equal(PropKind.StoneWall, ById(hit.PropId).Kind);
    }

    [Fact]
    public void A_tank_can_still_drive_from_the_spawn_up_the_big_hill()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        var input = new[] { TestWorld.Cmd(throttle: 1) };
        for (int i = 0; i < 15 * SimConstants.TickRate; i++) sim.Step(input);
        Assert.All(sim.Field.Props.Where(p => p.IsDestructible && p.Kind != PropKind.Bush), p => Assert.True(sim.Field.IsStanding(p.Id)));
        Assert.True(sim.Tanks[0].Position.Y < 200, $"only got to z = {sim.Tanks[0].Position.Y}");
    }
}
```

修改 `tests/Tank.Sim.Tests/ShellPropTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/ShellPropTests.cs b/tests/Tank.Sim.Tests/ShellPropTests.cs
index b03a06a..bea062c 100644
--- a/tests/Tank.Sim.Tests/ShellPropTests.cs
+++ b/tests/Tank.Sim.Tests/ShellPropTests.cs
@@ -59,7 +59,7 @@ public sealed class ShellPropTests
         Assert.True(events.IndexOf(hurt[0]) < events.IndexOf(burst) && events.IndexOf(burst) < events.IndexOf(hurt[1]));
         foreach (GameEvent splash in hurt.Skip(1))
         {
-            Prop wall = duel.Sim.Field.Props[splash.PropId - 1];
+            Prop wall = duel.Sim.Field.Props.Single(p => p.Id == splash.PropId);
             Assert.Equal(burst.Position, splash.Position);
             Assert.Equal(Vector3.Zero, splash.Velocity);   // a splash, not a direct hit
             Assert.Equal(WallDamage.Splash(Duel.Spec.He, wall.DistanceTo(burst.Position)), splash.Damage);
PATCH
```

修改 `tests/Tank.Sim.Tests/TargetSpotTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TargetSpotTests.cs b/tests/Tank.Sim.Tests/TargetSpotTests.cs
index 8c1e66c..42dcac2 100644
--- a/tests/Tank.Sim.Tests/TargetSpotTests.cs
+++ b/tests/Tank.Sim.Tests/TargetSpotTests.cs
@@ -7,13 +7,13 @@ namespace Tank.Sim.Tests;
 
 public sealed class TargetSpotTests
 {
-    private const int RockSpot = 4, FarSpot = 5;
+    private const int RockSpot = 4, FarSpot = 5, YardSpot = SandboxMap.YardTarget;
     private const float GunHeight = 2.1f;   // the spawn tank's trunnion
     private const float PlateHeight = 1.0f;
     private static readonly Battlefield Field = SandboxMap.Create();
     private static readonly IReadOnlyList<TargetSpot> Spots = SandboxMap.TargetSpots;
 
-    public static TheoryData<int> Indices() => new() { 0, 1, 2, 3, 4, 5 };
+    public static TheoryData<int> Indices() => new() { 0, 1, 2, 3, 4, 5, 6 };
 
     private static Obb2 Hull(TargetSpot s) => TankMotion.Footprint(s.Spec, s.Position, s.Yaw);
 
@@ -25,7 +25,10 @@ public sealed class TargetSpotTests
     }
 
     [Fact]
-    public void ThereAreSix() => Assert.Equal(6, Spots.Count);
+    public void ThereAreSeven() => Assert.Equal(7, Spots.Count);
+
+    [Fact]
+    public void TheYardTarget_IsAMediumTank() => Assert.Same(TankCatalog.Medium, Spots[YardSpot].Spec);
 
     [Fact]
     public void AllFourKindsOfTankStandThere()
@@ -60,13 +63,13 @@ public sealed class TargetSpotTests
 
     [Theory]
     [MemberData(nameof(Indices))]
-    public void IsClearOfRocksBushesAndTheLane(int i)
+    public void IsClearOfRocksBushesWallsAndTheLane(int i)
     {
         Obb2 hull = Hull(Spots[i]);
         foreach (Prop p in Field.Props)
         {
             if (p.Kind == PropKind.Bush) Assert.False(hull.IntersectsCircle(p.Footprint.Center, p.Footprint.HalfExtents.X), $"spot {i} in bush {p.Id}");
-            else Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _), $"spot {i} on rock {p.Id}");
+            else Assert.False(Obb2.TryGetPenetration(hull, p.Footprint, out _), $"spot {i} on {p.Kind} {p.Id}");
         }
         float lane = SandboxMap.DistanceToSegment(hull.Center, SandboxMap.Spawn, SandboxMap.BigHill) - hull.HalfExtents.Length();
         Assert.True(lane >= SandboxMap.LaneHalfWidth, $"spot {i} is {lane:F1} m from the lane");
@@ -83,6 +86,7 @@ public sealed class TargetSpotTests
     [InlineData(3, 180f)]
     [InlineData(4, 0f)]
     [InlineData(5, 0f)]
+    [InlineData(6, 0f)]
     public void ShowsTheSpawnTheRightFace(int i, float degreesOffFront)
     {
         Vector2 toSpawn = Vector2.Normalize(SandboxMap.Spawn - Spots[i].Position);
@@ -92,18 +96,18 @@ public sealed class TargetSpotTests
 
     [Theory]
     [MemberData(nameof(Indices))]
-    public void CanBeSeenFromTheSpawn_ExceptTheRockOneIsHalfHidden(int i)
+    public void CanBeSeenFromTheSpawn_ExceptTheRockOneIsHalfHidden_AndTheYardOneIsWalledIn(int i)
     {
-        int clear = 0, rock = 0;
+        int clear = 0, rock = 0, wall = 0;
         foreach (RayHit? hit in SightLines(Spots[i]))
         {
             if (hit is null) clear++;
-            else if (hit.Value.PropId != RayHit.TerrainId) rock++;
-        }
-        if (i == RockSpot)
-        {
-            Assert.True(clear >= 1 && rock >= 1, $"rock spot: {clear} clear, {rock} behind a rock");
+            else if (hit.Value.PropId == RayHit.TerrainId) continue;
+            else if (Field.Props.Single(p => p.Id == hit.Value.PropId).IsWall) wall++;
+            else rock++;
         }
+        if (i == RockSpot) Assert.True(clear >= 1 && rock >= 1, $"rock spot: {clear} clear, {rock} behind a rock");
+        else if (i == YardSpot) Assert.Equal(3, wall);
         else Assert.Equal(3, clear);
     }
 
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`SandboxMap.YardTarget` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/SandboxMap.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/SandboxMap.cs b/src/Tank.Sim/Maps/SandboxMap.cs
index ef2a34e..8f9c09f 100644
--- a/src/Tank.Sim/Maps/SandboxMap.cs
+++ b/src/Tank.Sim/Maps/SandboxMap.cs
@@ -6,8 +6,9 @@ using Tank.Sim.Vehicles;
 namespace Tank.Sim.Maps;
 
 /// <summary>
-/// 400m × 400m test ground: rolling noise, four hills, 80-odd big rocks, twelve wide patches of tall bushes and six
-/// practice targets. The spawn faces the big hill (north); the nearest bush patch sits beside that lane about 40 m out.
+/// 400m × 400m test ground: rolling noise, four hills, 80-odd big rocks, twelve wide patches of tall bushes, a walled
+/// yard with a few walls and fences round the spawn, and seven practice targets. The spawn faces the big hill (north);
+/// the nearest bush patch sits beside that lane about 40 m out, the yard about 40 m to the west.
 /// </summary>
 public static class SandboxMap
 {
@@ -59,10 +60,11 @@ public static class SandboxMap
     private const int BushAttemptsPerPatch = 200;
 
     /// <summary>
-    /// Six practice targets, hand-placed on flat open ground 30–200 m from the spawn, clear of rocks, bushes and the
-    /// spawn-to-hill lane, all four kinds of tank among them. Seen from the spawn they show, in order: a medium tank's
+    /// Seven practice targets, hand-placed on flat open ground 30–200 m from the spawn, clear of rocks, bushes, walls and
+    /// the spawn-to-hill lane, all four kinds of tank among them. Seen from the spawn they show, in order: a medium tank's
     /// front (most range demos shoot this one), a heavy tank's side, a light tank at 45°, a tank destroyer's rear, a medium
-    /// tank's front half hidden behind a rock, and a heavy tank's front far away (180–200 m).
+    /// tank's front half hidden behind a rock, a heavy tank's front far away (180–200 m), and a medium tank's front
+    /// inside the yard, out of sight behind its stone wall (<see cref="YardTarget"/>).
     /// </summary>
     public static readonly IReadOnlyList<TargetSpot> TargetSpots = new[]
     {
@@ -72,6 +74,31 @@ public static class SandboxMap
         Facing(new Vector2(140, 256), turnDegrees: 180, TankCatalog.Td),
         Facing(new Vector2(280, 328), turnDegrees: 0, TankCatalog.Medium),
         Facing(new Vector2(380, 272), turnDegrees: 0, TankCatalog.Heavy),
+        Facing(new Vector2(160, 340), turnDegrees: 0, TankCatalog.Medium),
+    };
+
+    /// <summary>The index in <see cref="TargetSpots"/> of the target inside the yard.</summary>
+    public const int YardTarget = 6;
+
+    /// <summary>
+    /// Hand-placed walls and fences (stage 2C design §3.6). The yard, 18 × 12.8 m inside around (160, 340): a stone wall
+    /// on the east side, facing the spawn; brick on the north and south; brick on the west with a gate of three fence
+    /// panels in the middle. The corners meet with a centimetre to spare. Then cover near the spawn: a brick wall north-west
+    /// of it, a stone wall to the east and two rows of fence panels, one on the way to the yard. Seen from the spawn none
+    /// of them hides the first six targets.
+    /// </summary>
+    public static readonly IReadOnlyList<WallLine> WallLines = new WallLine[]
+    {
+        new(PropKind.StoneWall, new Vector2(169.51f, 333), new Vector2(169.51f, 347)),
+        new(PropKind.BrickWall, new Vector2(151, 333.3f), new Vector2(169, 333.3f)),
+        new(PropKind.BrickWall, new Vector2(151, 346.7f), new Vector2(169, 346.7f)),
+        new(PropKind.BrickWall, new Vector2(150.69f, 333), new Vector2(150.69f, 337)),
+        new(PropKind.Fence, new Vector2(150.69f, 337), new Vector2(150.69f, 343)),
+        new(PropKind.BrickWall, new Vector2(150.69f, 343), new Vector2(150.69f, 347)),
+        new(PropKind.BrickWall, new Vector2(186, 302), new Vector2(192, 302)),
+        new(PropKind.StoneWall, new Vector2(243, 320), new Vector2(247, 320)),
+        new(PropKind.Fence, new Vector2(210, 318), new Vector2(216, 318)),
+        new(PropKind.Fence, new Vector2(182, 330), new Vector2(182, 336)),
     };
 
     private static readonly TerrainGenerator.Hill[] Hills =
@@ -87,6 +114,7 @@ public static class SandboxMap
         HeightField terrain = GenerateTerrain();
         List<Prop> props = ScatterRocks(terrain);
         props.AddRange(PlantBushes(terrain, props));   // after the rocks, so rock ids (and their shapes) don't change
+        foreach (WallLine line in WallLines) props.AddRange(line.Segments(terrain, props.Count + 1));   // and the walls after both
         return new Battlefield(terrain, props, Border);
     }
 
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 452、Art 374；客户端 0 警告 0 错误。此时墙已经挡坦克和炮弹，但还看不见（Task 7 加外观）；靶车有 7 辆。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests
git commit -m "feat(sim): a walled yard west of the sandbox spawn with a seventh target inside, and walls and fences for cover"
```

---

### Task 7：墙、栅栏、瓦砾的外观，草丛被打掉，T 键

设计 §4.1、§4.4。网格在 `Art/Mesh/WallShapes.cs`（不依赖 Godot，有外观测试）：墙段是顶边倒圆、两端也倒圆的厚板（段与段之间露出一道浅槽）；栅栏是两根立柱加两条横杆；瓦砾是一堆砖块或圆石，铺满瓦砾范围、高不过 0.8 m；`FenceFall` 给出栅栏绕倒下那一侧底边转过去的姿态，0.35 秒倒平，越倒越快。着色器 `wall.gdshader` 在物体空间里画错开的砖缝（模式 0）或大块石缝（模式 1），瓦砾和木头用纯色（模式 2，碎块的深浅在顶点色 R 里）。`WallViews` 每个墙段建一个立着的节点和（墙才有）一个瓦砾节点，每帧按 `Battlefield` 的状态切换；栅栏倒下的方向取它的 `PropDestroyed` 事件里被推开的方向。`BushField.SyncStanding` 把被打掉的草丛缩成看不见，恢复后再显示。T 键调用 `RestoreProps`（固定机位和演示运行时不响应）。新增 `yard` 机位从东南方高处俯看院子和附近的掩体。

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/WallShapes.cs`、`scripts/World/WallViews.cs`、`shaders/wall.gdshader`
- Modify: `src/Tank.Client/scripts/Art/Palette.cs`、`scripts/World/BushField.cs`、`scripts/World/SimConvert.cs`、`scripts/Sandbox/SandboxWorld.cs`、`SandboxInput.cs`、`SandboxMain.cs`、`SandboxAutomation.cs`、`scripts/Cameras/CameraPresets.cs`
- Test: 新建 `tests/Tank.Art.Tests/WallShapesTests.cs`；改 `PaletteTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/PaletteTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/PaletteTests.cs b/tests/Tank.Art.Tests/PaletteTests.cs
index a8c38f1..c57b6d1 100644
--- a/tests/Tank.Art.Tests/PaletteTests.cs
+++ b/tests/Tank.Art.Tests/PaletteTests.cs
@@ -42,6 +42,14 @@ public class PaletteTests
         Assert.Equal(Palette.Hex("2B2622"), Palette.Charred);
     }
 
+    [Fact]
+    public void Wall_colours_keep_stone_apart_from_the_rocks_and_brick_apart_from_the_tanks()
+    {
+        Assert.True(Vector3.Distance(Palette.Stone, Palette.Rock) > 0.08f, "stone walls read as built, not as boulders");
+        Assert.True(Vector3.Distance(Palette.Brick, Palette.TankBody) > 0.2f, "brick is redder than the orange tanks");
+        Assert.True(Palette.BrickMortar.X > Palette.Brick.X && Palette.StoneMortar.X < Palette.Stone.X, "joints show up");
+    }
+
     [Fact]
     public void Linear_follows_the_srgb_curve()
     {
PATCH
```

新建 `tests/Tank.Art.Tests/WallShapesTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Art.Meshes;
using Tank.Sim.Maps;

namespace Tank.Art.Tests;

public class WallShapesTests
{
    private const float Sink = WallLine.Sink;

    private static (Vector3 Min, Vector3 Max) Bounds(MeshData mesh) =>
        (mesh.Positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min), mesh.Positions.Aggregate(new Vector3(float.MinValue), Vector3.Max));

    [Theory]
    [InlineData(PropKind.BrickWall)]
    [InlineData(PropKind.StoneWall)]
    public void A_wall_block_fills_its_prop_exactly(PropKind kind)
    {
        float thickness = WallLine.Thickness(kind), height = WallLine.Height(kind);
        MeshData block = WallShapes.Block(WallLine.SegmentLength, height, thickness, Sink);
        var (min, max) = Bounds(block);
        Assert.Equal(new Vector3(-1f, -Sink, -thickness / 2), min, new Near(1e-4f));
        Assert.Equal(new Vector3(1f, height, thickness / 2), max, new Near(1e-4f));
        MeshAssert.Closed(block);
        MeshAssert.WindingMatchesNormals(block);
    }

    [Fact]
    public void A_fence_panel_stays_inside_its_prop()
    {
        float thickness = WallLine.Thickness(PropKind.Fence), height = WallLine.Height(PropKind.Fence);
        MeshData fence = WallShapes.Fence(WallLine.SegmentLength, height, thickness, Sink);
        var (min, max) = Bounds(fence);
        Assert.True(min.X >= -1f - 1e-4f && max.X <= 1f + 1e-4f, $"x {min.X} … {max.X}");
        Assert.True(min.Z >= -thickness / 2 - 1e-4f && max.Z <= thickness / 2 + 1e-4f, $"z {min.Z} … {max.Z}");
        Assert.Equal(-Sink, min.Y, 1e-4f);
        Assert.Equal(height, max.Y, 1e-4f);
        Assert.True(max.X - min.X > 1.9f, "the rails run the whole panel");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void A_fallen_fence_lies_flat_on_the_side_it_fell_to(int side)
    {
        float thickness = WallLine.Thickness(PropKind.Fence);
        MeshData fallen = WallShapes.Fence(WallLine.SegmentLength, WallLine.Height(PropKind.Fence), thickness, Sink)
            .Transformed(FenceFall.Pose(1f, side, thickness));
        var (min, max) = Bounds(fallen);
        Assert.True(max.Y <= 0.3f && min.Y >= -0.01f, $"y {min.Y} … {max.Y}");
        float reach = side > 0 ? max.Z : -min.Z;
        Assert.InRange(reach, 1.5f, 1.65f);   // the panel's 1.5 m now lies along the ground on that side
    }

    [Fact]
    public void A_fence_falls_faster_and_faster_and_then_lies_still()
    {
        Assert.Equal(0f, FenceFall.Progress(0f));
        Assert.True(FenceFall.Progress(FenceFall.Seconds / 2) < 0.5f, "starts slowly");
        Assert.Equal(1f, FenceFall.Progress(FenceFall.Seconds));
        Assert.Equal(1f, FenceFall.Progress(10f));
        Assert.Equal(Matrix4x4.Identity, FenceFall.Pose(0f, 1, 0.2f));
    }

    [Theory]
    [InlineData(PropKind.BrickWall, true)]
    [InlineData(PropKind.StoneWall, false)]
    public void Rubble_is_a_low_heap_spread_over_the_rubble_footprint(PropKind kind, bool blocky)
    {
        float depth = WallLine.Thickness(kind) + 2 * Prop.RubbleSpread;
        MeshData heap = WallShapes.Rubble(WallLine.SegmentLength, depth, 0.8f, blocky, 42u);
        var (min, max) = Bounds(heap);
        Assert.True(min.X >= -1f - 1e-4f && max.X <= 1f + 1e-4f, $"x {min.X} … {max.X}");
        Assert.True(min.Z >= -depth / 2 - 1e-4f && max.Z <= depth / 2 + 1e-4f, $"z {min.Z} … {max.Z}");
        Assert.True(max.Y <= 0.8f + 1e-4f, $"{max.Y} m tall");
        Assert.True(max.Y > 0.4f, "a heap, not a carpet");
        Assert.True(max.X - min.X > 1.4f, "spread along the whole segment");
        Assert.All(heap.Colors, c => Assert.InRange(c.X, 0.8f, 1f));
    }

    [Fact]
    public void Rubble_is_the_same_for_the_same_seed_and_differs_between_seeds()
    {
        MeshData a = WallShapes.Rubble(2f, 1.6f, 0.8f, true, 7u), b = WallShapes.Rubble(2f, 1.6f, 0.8f, true, 7u), c = WallShapes.Rubble(2f, 1.6f, 0.8f, true, 8u);
        Assert.Equal(a.Positions, b.Positions);
        Assert.NotEqual(a.Positions, c.Positions);
    }

    private sealed class Near(float tolerance) : IEqualityComparer<Vector3>
    {
        public bool Equals(Vector3 a, Vector3 b) => Vector3.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector3 v) => 0;
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`WallShapes`、`FenceFall`、`Palette.Stone` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Client/scripts/Art/Mesh/WallShapes.cs`：

```csharp
using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// Toy walls, fences and rubble (stage 2C design §4.1). Each mesh is in its prop's own frame: X along the wall, Y up from
/// the ground under its centre, Z across it; standing pieces reach <c>sink</c> below the ground like the props do.
/// Rubble carries a per-chunk shade in vertex colour R (0.8 … 1) so the chunks read apart.
/// </summary>
public static class WallShapes
{
    private const float PostWidth = 0.16f, PostInset = 0.18f;   // fence posts, and how far in from each end of the panel
    private const float RailHeight = 0.14f, RailThickness = 0.07f;
    private static readonly float[] RailLevels = { 0.55f, 1.15f };   // rail centres over the ground, for a 1.5 m fence
    private const float FenceBevel = 0.025f;   // at most; thin parts get a fifth of their thinnest side
    private const int RubbleChunks = 16;

    /// <summary>A wall's top edges are rounded over this share of its thickness.</summary>
    public const float TopRoundShare = 0.28f;

    /// <summary>
    /// A wall segment: a slab <paramref name="length"/> × <paramref name="thickness"/> from <paramref name="sink"/> below the
    /// ground up to <paramref name="height"/> above it. Its top edges are rounded over <see cref="TopRoundShare"/> of the
    /// thickness and its ends over half that (the most <see cref="RoundedSlab"/> allows), so segments laid end to end show
    /// a soft groove between them.
    /// </summary>
    public static MeshData Block(float length, float height, float thickness, float sink)
    {
        float hz = thickness / 2, top = TopRoundShare * thickness;
        var corners = new[] { new Vector2(-hz, -sink), new Vector2(hz, -sink), new Vector2(hz, height), new Vector2(-hz, height) };
        Vector2[] profile = Polygon2.RoundCorners(corners, new[] { 0f, 0f, top, top }, 4);
        return RoundedSlab.Build(profile, length, top / 2);
    }

    /// <summary>
    /// A fence panel: two square posts from <paramref name="sink"/> below the ground to <paramref name="height"/>, and two
    /// rails between them running the panel's whole <paramref name="length"/>; no thicker than <paramref name="thickness"/>.
    /// </summary>
    public static MeshData Fence(float length, float height, float thickness, float sink)
    {
        float post = MathF.Min(PostWidth, thickness);
        float postHeight = height + sink;
        var parts = new List<(MeshData, Matrix4x4)>();
        foreach (float side in new[] { -1f, 1f })
        {
            MeshData p = Box.Build(new Vector3(post, postHeight, post), Bevel(post));
            parts.Add((p, Matrix4x4.CreateTranslation(side * (length / 2 - PostInset), postHeight / 2 - sink, 0)));
        }
        foreach (float level in RailLevels)
        {
            float railThickness = MathF.Min(RailThickness, thickness);
            MeshData rail = Box.Build(new Vector3(length, RailHeight, railThickness), Bevel(railThickness));
            parts.Add((rail, Matrix4x4.CreateTranslation(0, level * height / 1.5f, 0)));
        }
        return Merge.Build(parts);
    }

    private static float Bevel(float thinnest) => MathF.Min(FenceBevel, thinnest * 0.2f);

    /// <summary>
    /// A low heap of chunks filling <paramref name="length"/> × <paramref name="depth"/> on the ground, at most
    /// <paramref name="height"/> tall, highest along the middle line and thinning out towards both edges: blocky bricks
    /// (<paramref name="blocky"/>) or rounded stones. The same <paramref name="seed"/> gives the same heap.
    /// </summary>
    public static MeshData Rubble(float length, float depth, float height, bool blocky, uint seed)
    {
        MeshData stone = Icosphere.Build(1);
        var parts = new List<(MeshData, Matrix4x4)>();
        for (int k = 0; k < RubbleChunks; k++)
        {
            float R(int salt) => SeededNoise.Hash01(k, salt, 7, seed);
            float size = 0.28f + 0.3f * R(1);
            Vector3 half = blocky ? new Vector3(size, size * 0.5f, size * 0.55f) * 0.5f : new Vector3(size, size * 0.7f, size * 0.85f) * 0.5f;
            float reach = half.Length();   // the chunk stays inside the heap's box however it is turned
            float x = (R(2) * 2 - 1) * (length / 2 - reach);
            float across = (R(3) + R(4) - 1f) * (depth / 2 - reach);   // triangular spread: more in the middle
            float crest = height * (1f - MathF.Abs(across) / (depth / 2)) - reach;   // the heap's top above this spot, less the chunk
            float y = reach * 0.6f + MathF.Max(0f, crest - reach * 0.6f) * R(5);
            y = MathF.Min(y, height - reach);
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll(R(6) * MathF.Tau, (R(7) - 0.5f) * 0.8f, (R(8) - 0.5f) * 0.8f);
            MeshData chunk = blocky ? Box.Build(half * 2, MathF.Min(half.Y, half.Z) * 0.35f) : stone.Transformed(Matrix4x4.CreateScale(half));
            float shade = 0.8f + 0.2f * R(9);
            parts.Add((chunk.WithColor(new Vector4(shade, 0, 0, 1)), turn * Matrix4x4.CreateTranslation(x, y, across)));
        }
        return Merge.Build(parts);
    }
}

/// <summary>
/// A fence panel toppling over (stage 2C design §4.1): it swings about its bottom edge on the side it falls to, speeding
/// up like a falling plank, and lies flat after <see cref="Seconds"/>.
/// </summary>
public static class FenceFall
{
    public const float Seconds = 0.35f;

    /// <summary>How far over it is after <paramref name="time"/> s: 0 standing, 1 flat on the ground.</summary>
    public static float Progress(float time)
    {
        float t = Math.Clamp(time / Seconds, 0f, 1f);
        return t * t;
    }

    /// <summary>
    /// The panel's pose in its own frame (row-vector convention, as <see cref="MeshData.Transformed"/>): turned about the
    /// bottom edge of its +Z face (<paramref name="side"/> +1) or −Z face (−1) by <paramref name="progress"/> × 90°.
    /// </summary>
    public static Matrix4x4 Pose(float progress, int side, float thickness)
    {
        float pivot = Math.Sign(side) * thickness / 2;
        float angle = Math.Sign(side) * progress * MathF.PI / 2;
        return Matrix4x4.CreateTranslation(0, 0, -pivot) * Matrix4x4.CreateRotationX(angle) * Matrix4x4.CreateTranslation(0, 0, pivot);
    }
}
```

修改 `src/Tank.Client/scripts/Art/Palette.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Art/Palette.cs b/src/Tank.Client/scripts/Art/Palette.cs
index 0a6dbf4..742859e 100644
--- a/src/Tank.Client/scripts/Art/Palette.cs
+++ b/src/Tank.Client/scripts/Art/Palette.cs
@@ -45,6 +45,12 @@ public static class Palette
     public static readonly Vector3 HitBlocked = Hex("B9BDC8");   // "未击穿" ("跳弹！" uses UiAccent)
     public static readonly Vector3 Health = Hex("6CD45A");       // a healthy health bar
     public static readonly Vector3 WreckSmoke = Hex("4A4650");   // dark smoke from a wreck
+    public static readonly Vector3 Brick = Hex("C4553A");        // brick walls and their rubble
+    public static readonly Vector3 BrickMortar = Hex("E8C9A8");  // cream joints between the bricks
+    public static readonly Vector3 Stone = Hex("B4B8BF");        // stone walls: a cool grey, apart from the warm rocks
+    public static readonly Vector3 StoneMortar = Hex("7E838C");  // darker joints between the stones
+    public static readonly Vector3 Wood = Hex("B9844F");         // fences, and the chips off them
+    public static readonly Vector3 BrickDust = Hex("E0A88C");    // dust off a brick wall
 
     /// <summary>"RRGGBB" (an optional leading '#') to sRGB 0–1.</summary>
     public static Vector3 Hex(string hex)
PATCH
```

修改 `src/Tank.Client/scripts/Cameras/CameraPresets.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Cameras/CameraPresets.cs b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
index 2a53661..c471f65 100644
--- a/src/Tank.Client/scripts/Cameras/CameraPresets.cs
+++ b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
@@ -7,7 +7,8 @@ namespace Tank.Client.Cameras;
 /// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
 /// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
 /// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at a practice target (the
-/// range demo's, else the first) from its front-right, on the side where a thrown turret lands (pair it with --range-demo).
+/// range demo's, else the first) from its front-right, on the side where a thrown turret lands (pair it with --range-demo);
+/// yard looks down on the walled yard and the cover beside the spawn from the south-east.
 /// </summary>
 public static class CameraPresets
 {
@@ -20,13 +21,16 @@ public static class CameraPresets
     private static readonly Vector2 SpinEye = new(200f, 200f);
     private const float OrbitPeriod = 12f;
     private const float OrbitRadius = 14f;
+    private static readonly Vector2 YardEye = new(204f, 358f);          // south-east of the walled yard, behind the spawn
+    private static readonly Vector3 YardTarget = new(166f, 0f, 334f);   // between the yard and the cover by the spawn
+    private const float YardEyeHeight = 15f;
     private static readonly Vector3 TargetEye = new(7f, 4.5f, -13f);    // front-right of the target (its forward is −Z)
     private static readonly Vector3 TargetFocus = new(1.5f, 1.6f, 0f);  // between the hull and where the turret lands
 
     /// <summary>Presets whose eye is far from the tank: world detail (grass) should then stream around the eye, not the tank.</summary>
     public static bool StreamsAroundCamera(string? name) => name is "hill" or "spin";
 
-    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush" or "target";
+    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush" or "target" or "yard";
 
     /// <summary>Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, the rest use the normal spawn.</summary>
     public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;
@@ -55,6 +59,9 @@ public static class CameraPresets
                 var at = new Vector3(spot.Position.X, groundAt(spot.Position.X, spot.Position.Y), spot.Position.Y);
                 var turn = new Basis(Vector3.Up, spot.Yaw);
                 return (at + turn * TargetEye, at + turn * TargetFocus);
+            case "yard":
+                return (new Vector3(YardEye.X, groundAt(YardEye.X, YardEye.Y) + YardEyeHeight, YardEye.Y),
+                    YardTarget + Vector3.Up * groundAt(YardTarget.X, YardTarget.Z));
             case "hill":
                 return (new Vector3(HillEye.X, groundAt(HillEye.X, HillEye.Y) + 28f, HillEye.Y), HillTarget);
             case "orbit": // circles the parked tank at 14 m, level with the turret: stresses anything keyed to the camera
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
index 69b70ee..69de929 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
@@ -61,7 +61,7 @@ public sealed class SandboxAutomation
     {
         foreach (string warning in _options.Warnings) GD.PushWarning(warning);
         if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
-            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush or target.");
+            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush, target or yard.");
         if (_options.RangeDemo is { } demo && Demo is null)
             GD.PushWarning($"Unknown --range-demo '{demo}'; use {string.Join(", ", RangeDemo.Names)}.");
     }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
index 02fa4c1..7674b88 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
@@ -6,7 +6,8 @@ namespace Tank.Client.Sandbox;
 /// <summary>
 /// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
 /// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
-/// AP or HE (kept until changed); R asks for a restock and F1–F4 for another tank, each consumed once by the next tick.
+/// AP or HE (kept until changed); R asks for a restock, T to restore the walls, fences and bushes, and F1–F4 for another
+/// tank, each consumed once by the next tick.
 /// </summary>
 public sealed class SandboxInput
 {
@@ -15,7 +16,7 @@ public sealed class SandboxInput
     private static readonly StringName DriveForward = "drive_forward", DriveBack = "drive_back";
     private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
     private static readonly StringName FreeLookAction = "free_look";
-    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he", Restock = "restock";
+    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he", Restock = "restock", RestoreProps = "restore_props";
 
     // The tank-pick keys, one row per action so the action and its key can't drift apart; the row index is the
     // TankCatalog.All index that TakeTankPick returns.
@@ -30,6 +31,7 @@ public sealed class SandboxInput
     private bool _fireLatched;
     private bool _fireHeld;
     private bool _restock;
+    private bool _restoreProps;
     private int _pickedTank = -1;
 
     public static void RegisterActions()
@@ -44,6 +46,7 @@ public sealed class SandboxInput
         AddKey(AmmoAp, Key.Key1);
         AddKey(AmmoHe, Key.Key2);
         AddKey(Restock, Key.R);
+        AddKey(RestoreProps, Key.T);
         foreach ((StringName action, Key key) in PickTank) AddKey(action, key);
     }
 
@@ -59,6 +62,7 @@ public sealed class SandboxInput
         else if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
         else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
         else if (e.IsActionPressed(Restock)) _restock = true;
+        else if (e.IsActionPressed(RestoreProps)) _restoreProps = true;
         else if (e is InputEventKey)
         {
             // Keyboard events only: mouse motion (about one per frame while aiming) must not pay four action lookups.
@@ -85,6 +89,14 @@ public sealed class SandboxInput
         return restock;
     }
 
+    /// <summary>True once after T was pressed.</summary>
+    public bool TakeRestoreProps()
+    {
+        bool restore = _restoreProps;
+        _restoreProps = false;
+        return restore;
+    }
+
     /// <summary>Once after F1–F4 was pressed: the tank asked for, as an index into <c>TankCatalog.All</c>.</summary>
     public int? TakeTankPick()
     {
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 7441b85..4dc39a0 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -15,8 +15,8 @@ namespace Tank.Client.Sandbox;
 
 /// <summary>
 /// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0;
-/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock, F1–F4 another tank
-/// (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
+/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock, T restore the walls,
+/// fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
 /// --camera-preset front|side|hill|spin|orbit|bush|target|... --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn
@@ -153,6 +153,7 @@ public partial class SandboxMain : Node3D
     private void StepSimulation()
     {
         if (!HasPreset && _input.TakeRestock()) _sim.Restock(_current.Id);
+        if (!HasPreset && _input.TakeRestoreProps()) _sim.RestoreProps();
         if (_input.TakeTankPick() is int pick && pick < TankCatalog.All.Count) ChangeTank(TankCatalog.All[pick]);
         _targets.ReviveDue(_sim);
         var (throttle, steer, fire) = _input.Sample();
@@ -210,6 +211,7 @@ public partial class SandboxMain : Node3D
         }
         _hud.OnEvent(e);
         _targets.OnEvent(e, _sim, _fx);
+        _world.OnEvent(e);
     }
 
     /// <summary>Poses and animates the player's tank, with the muzzle blast of a shot fired this frame; hidden in the sniper sight.</summary>
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxWorld.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs b/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
index eaf9095..d2e268c 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
@@ -1,6 +1,7 @@
 using Godot;
 using Tank.Client.Vehicles;
 using Tank.Client.World;
+using Tank.Sim;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
@@ -9,17 +10,20 @@ using SN = System.Numerics;
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// The sandbox's scenery, built once from the battlefield: sky and sun, terrain, rocks, bushes, grass and the tree line.
-/// Each frame <see cref="Update"/> lays the grass flat under the player's tank (sized for whichever tank it is now),
-/// streams grass detail around the view and lets the bushes part around the hull.
+/// The sandbox's scenery, built once from the battlefield: sky and sun, terrain, rocks, bushes, walls and fences, grass
+/// and the tree line. Each frame <see cref="Update"/> lays the grass flat under the player's tank (sized for whichever
+/// tank it is now), streams grass detail around the view, lets the bushes part around the hull, and shows the props as
+/// they stand now (rubble, fallen fences, bushes shot away).
 /// </summary>
 public partial class SandboxWorld : Node3D
 {
     private const float GrassCameraLean = 0.4f;    // grass focus sits this fraction of the way from tank to camera
     private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks
 
+    private Battlefield _field = null!;
     private GrassField _grass = null!;
     private BushField _bushes = null!;
+    private WallViews _walls = null!;
     private SN.Vector2 _flatHalfExtents;
     private TankSpec? _flatFor;
     private bool _streamsAroundCamera;
@@ -28,6 +32,7 @@ public partial class SandboxWorld : Node3D
     /// <param name="streamsAroundCamera">The camera is a fixed far-away preset: stream detail around its eye, not the tank.</param>
     public void Build(Battlefield field, in TankState player, bool streamsAroundCamera)
     {
+        _field = field;
         _streamsAroundCamera = streamsAroundCamera;
         AddChild(Scenery.CreateEnvironment());
         AddChild(Scenery.CreateSun());
@@ -42,6 +47,9 @@ public partial class SandboxWorld : Node3D
         _bushes = new BushField();
         AddChild(_bushes);
         _bushes.Build(field.Props, field.Terrain);
+        _walls = new WallViews();
+        AddChild(_walls);
+        _walls.Build(field);
         _grass = new GrassField();
         AddChild(_grass);
         _grass.Build(field.Terrain, dirt, field.Props);
@@ -62,9 +70,14 @@ public partial class SandboxWorld : Node3D
         Vector3 tankPos = pose.Position.ToGodot();
         _grass.Update(_streamsAroundCamera ? cameraPos
             : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
+        _bushes.SyncStanding(_field);
         _bushes.Update(cameraPos, TankMotion.Footprint(player.Spec, player.Position, player.Yaw), delta);
+        _walls.Sync(_field, delta);
     }
 
+    /// <summary>Lets the walls see which way a fence was pushed down.</summary>
+    public void OnEvent(in GameEvent e) => _walls.OnEvent(e);
+
     /// <summary>Half-size of the patch a tank lays flat: its hull and tracks, plus a margin.</summary>
     private static SN.Vector2 FlatHalfExtents(TankSpec spec)
     {
PATCH
```

修改 `src/Tank.Client/scripts/World/BushField.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/World/BushField.cs b/src/Tank.Client/scripts/World/BushField.cs
index fed4f27..3780e29 100644
--- a/src/Tank.Client/scripts/World/BushField.cs
+++ b/src/Tank.Client/scripts/World/BushField.cs
@@ -16,7 +16,8 @@ namespace Tank.Client.World;
 ///   your own view; the shader does it with screen-door dithering, so nothing needs sorting;
 /// - push: while the tank overlaps a bush its leaves part around the hull; after it leaves they spring back over
 ///   <see cref="ReboundSeconds"/> (the shader pushes away from the last overlapping tank position).
-/// Only instances whose values changed are re-uploaded.
+/// Only instances whose values changed are re-uploaded. A bush shot away is scaled to nothing until it is restored
+/// (<see cref="SyncStanding"/>).
 /// </summary>
 public partial class BushField : Node3D
 {
@@ -34,8 +35,10 @@ public partial class BushField : Node3D
 
     private sealed class Bush
     {
+        public required int Id { get; init; }
         public required MultiMesh Multimesh { get; init; }
         public required int Index { get; init; }
+        public required Transform3D Transform { get; init; }
         public required SN.Vector2 Center { get; init; }
         public required float Radius { get; init; }
         public required float Bottom { get; init; }
@@ -44,6 +47,7 @@ public partial class BushField : Node3D
         public float Push;
         public SN.Vector2 PushFrom;
         public Color Uploaded = new(1f, 0f, 0f, 0f);
+        public bool Shown = true;
     }
 
     public int Count => _bushes.Count;
@@ -71,9 +75,14 @@ public partial class BushField : Node3D
                 float radius = prop.Footprint.HalfExtents.X;
                 float yaw = (prop.Id * 0.6180339f % 1f) * Mathf.Tau;
                 var basis = new Basis(Vector3.Up, yaw).Scaled(new Vector3(radius, prop.Top - ground, radius));
-                multimesh.SetInstanceTransform(i, new Transform3D(basis, prop.Footprint.Center.Ground(ground)));
+                var transform = new Transform3D(basis, prop.Footprint.Center.Ground(ground));
+                multimesh.SetInstanceTransform(i, transform);
                 multimesh.SetInstanceCustomData(i, new Color(1f, 0f, 0f, 0f));
-                _bushes.Add(new Bush { Multimesh = multimesh, Index = i, Center = prop.Footprint.Center, Radius = radius, Bottom = ground, Top = prop.Top });
+                _bushes.Add(new Bush
+                {
+                    Id = prop.Id, Multimesh = multimesh, Index = i, Transform = transform,
+                    Center = prop.Footprint.Center, Radius = radius, Bottom = ground, Top = prop.Top,
+                });
             }
             // Two views of the same instances: the visible one never casts shadows (its dithering would speckle them), the
             // shadow one is drawn only into shadow maps, undithered, so a see-through bush still casts a solid shadow.
@@ -122,6 +131,18 @@ public partial class BushField : Node3D
         }
     }
 
+    /// <summary>Hides the bushes that have been shot away (scaled to nothing) and shows restored ones again.</summary>
+    public void SyncStanding(Battlefield field)
+    {
+        foreach (Bush bush in _bushes)
+        {
+            bool standing = field.IsStanding(bush.Id);
+            if (standing == bush.Shown) continue;
+            bush.Shown = standing;
+            bush.Multimesh.SetInstanceTransform(bush.Index, standing ? bush.Transform : bush.Transform.ScaledLocal(Vector3.Zero));
+        }
+    }
+
     /// <summary>Distance from <paramref name="p"/> to the segment a→b.</summary>
     private static float SegmentDistance(SN.Vector2 p, SN.Vector2 a, SN.Vector2 b)
     {
PATCH
```

修改 `src/Tank.Client/scripts/World/SimConvert.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/World/SimConvert.cs b/src/Tank.Client/scripts/World/SimConvert.cs
index 4d1b98b..ef1e0d1 100644
--- a/src/Tank.Client/scripts/World/SimConvert.cs
+++ b/src/Tank.Client/scripts/World/SimConvert.cs
@@ -12,6 +12,10 @@ public static class SimConvert
 
     public static Quaternion ToGodot(this SN.Quaternion q) => new(q.X, q.Y, q.Z, q.W);
 
+    /// <summary>A System.Numerics affine matrix (row vectors: the image of X is row 1, the translation row 4) as a Godot transform.</summary>
+    public static Transform3D ToGodot(this SN.Matrix4x4 m) =>
+        new(new Basis(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33)), new Vector3(m.M41, m.M42, m.M43));
+
     /// <summary>Ground point (x, z) lifted to height <paramref name="y"/>.</summary>
     public static Vector3 Ground(this SN.Vector2 p, float y) => new(p.X, y, p.Y);
 }
PATCH
```

新建 `src/Tank.Client/scripts/World/WallViews.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Art.Meshes;
using Tank.Sim;
using Tank.Sim.Maps;
using SN = System.Numerics;

namespace Tank.Client.World;

/// <summary>
/// Draws the walls and fences (stage 2C design §4.1) and keeps them in step with the battlefield every frame
/// (<see cref="Sync"/>): a wall that is down shows a heap of rubble instead of its block; a fence knocked down topples
/// away from what hit it over <see cref="FenceFall.Seconds"/> (the side comes from its <see cref="GameEventType.PropDestroyed"/>
/// event, <see cref="OnEvent"/>) and lies flat; a restored prop stands up again at once. Meshes are shared per kind
/// (four rubble heaps per wall kind, picked by prop id).
/// </summary>
public partial class WallViews : Node3D
{
    private const int RubbleVariants = 4;
    private const float RubbleHeight = 0.8f;
    private static readonly Vector2 BrickSize = new(0.5f, 0.25f), StoneSize = new(0.95f, 0.6f);
    private const float BrickJoint = 0.022f, StoneJoint = 0.035f;

    private readonly List<View> _views = new();
    private readonly Dictionary<int, SN.Vector2> _pushes = new();   // prop id → the level direction it was last pushed down

    private sealed class View
    {
        public required Prop Prop { get; init; }
        public required Node3D Standing { get; init; }
        public required Transform3D Upright { get; init; }
        public MeshInstance3D? Rubble { get; init; }
        public bool ShownStanding = true;
        public float Fallen = -1f;   // seconds since a fence started toppling; -1 standing
        public int Side = 1;
    }

    public void Build(Battlefield field)
    {
        var brick = Wall(Palette.Brick, Palette.BrickMortar, mode: 0, BrickSize, BrickJoint);
        var stone = Wall(Palette.Stone, Palette.StoneMortar, mode: 1, StoneSize, StoneJoint);
        var brickRubble = Wall(Palette.Brick, Palette.BrickMortar, mode: 2, BrickSize, BrickJoint);
        var stoneRubble = Wall(Palette.Stone, Palette.StoneMortar, mode: 2, StoneSize, StoneJoint);
        var wood = Wall(Palette.Wood, Palette.Wood, mode: 2, BrickSize, BrickJoint);
        var blocks = new Dictionary<PropKind, ArrayMesh>();
        var heaps = new Dictionary<(PropKind, int), ArrayMesh>();
        foreach (Prop prop in field.Props)
        {
            if (!prop.IsWall && prop.Kind != PropKind.Fence) continue;
            float thickness = WallLine.Thickness(prop.Kind);
            if (!blocks.TryGetValue(prop.Kind, out ArrayMesh? mesh))
                blocks[prop.Kind] = mesh = (prop.Kind == PropKind.Fence
                    ? WallShapes.Fence(WallLine.SegmentLength, WallLine.Height(prop.Kind), thickness, WallLine.Sink)
                    : WallShapes.Block(WallLine.SegmentLength, WallLine.Height(prop.Kind), thickness, WallLine.Sink)).ToArrayMesh();
            var upright = new Transform3D(new Basis(Vector3.Up, prop.Footprint.Yaw), prop.Footprint.Center.Ground(prop.Bottom + WallLine.Sink));
            var standing = new MeshInstance3D
            {
                Mesh = mesh,
                MaterialOverride = prop.Kind switch { PropKind.BrickWall => brick, PropKind.StoneWall => stone, _ => wood },
                Transform = upright,
            };
            AddChild(standing);
            MeshInstance3D? rubble = null;
            if (prop.IsWall)
            {
                int variant = prop.Id % RubbleVariants;
                if (!heaps.TryGetValue((prop.Kind, variant), out ArrayMesh? heap))
                    heaps[(prop.Kind, variant)] = heap = WallShapes.Rubble(WallLine.SegmentLength, thickness + 2 * Prop.RubbleSpread,
                        RubbleHeight, blocky: prop.Kind == PropKind.BrickWall, seed: 977u * (uint)(variant + 1) + (uint)prop.Kind).ToArrayMesh();
                rubble = new MeshInstance3D
                {
                    Mesh = heap,
                    MaterialOverride = prop.Kind == PropKind.BrickWall ? brickRubble : stoneRubble,
                    Transform = upright,
                    Visible = false,
                };
                AddChild(rubble);
            }
            _views.Add(new View { Prop = prop, Standing = standing, Upright = upright, Rubble = rubble });
        }
    }

    /// <summary>Notes which way a fence was pushed, so it falls that way when <see cref="Sync"/> sees it down.</summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type == GameEventType.PropDestroyed) _pushes[e.PropId] = new SN.Vector2(e.Velocity.X, e.Velocity.Z);
    }

    /// <summary>Shows each wall and fence as the battlefield has it now, and moves toppling fences on.</summary>
    public void Sync(Battlefield field, float delta)
    {
        foreach (View view in _views)
        {
            bool standing = field.IsStanding(view.Prop.Id);
            if (standing != view.ShownStanding) Change(view, standing);
            if (view.Fallen < 0f || view.Fallen > FenceFall.Seconds) continue;
            view.Fallen += delta;
            float progress = FenceFall.Progress(view.Fallen);
            view.Standing.Transform = view.Upright * FenceFall.Pose(progress, view.Side, WallLine.Thickness(PropKind.Fence)).ToGodot();
        }
    }

    private void Change(View view, bool standing)
    {
        view.ShownStanding = standing;
        if (view.Rubble is { } rubble)
        {
            rubble.Visible = !standing;
            view.Standing.Visible = standing;
            return;
        }
        if (standing)
        {
            view.Fallen = -1f;
            view.Standing.Transform = view.Upright;
            return;
        }
        // Falls away from the push: towards the panel's +Z face if the push points that way.
        SN.Vector2 push = _pushes.GetValueOrDefault(view.Prop.Id, view.Prop.Footprint.AxisForward);
        Vector3 across = view.Upright.Basis.Z;
        view.Side = push.X * across.X + push.Y * across.Z >= 0 ? 1 : -1;
        view.Fallen = 0f;
    }

    private static ShaderMaterial Wall(SN.Vector3 color, SN.Vector3 joint, int mode, Vector2 block, float jointWidth) =>
        Materials.FromShader("wall")
            .With("base_color", color.ToColor())
            .With("joint_color", joint.ToColor())
            .With("mode", mode)
            .With("block", block)
            .With("joint", jointWidth);
}
```

新建 `src/Tank.Client/shaders/wall.gdshader`：

```glsl
// Toy walls and fences: one flat colour per material with the shared toon lighting and a sky-coloured rim light. On a
// standing wall, soft joint lines are drawn in object space (no textures): staggered bricks (mode 0) or big uneven
// stone blocks (mode 1). Mode 2 is plain colour times vertex colour R (rubble chunks; fence wood has none, so 1). The base darkens
// a little near the ground so walls sit in the grass.
shader_type spatial;
render_mode cull_back;

#include "res://shaders/common.gdshaderinc"
#include "res://shaders/toon_light.gdshaderinc"

uniform vec3 base_color : source_color = vec3(0.769, 0.333, 0.227);   // Palette.Brick
uniform vec3 joint_color : source_color = vec3(0.910, 0.788, 0.659);  // Palette.BrickMortar
uniform int mode = 0;
uniform vec2 block = vec2(0.5, 0.25);   // one brick (or stone) along the wall × up, m
uniform float joint = 0.025;            // half-width of a joint line, m
uniform float bottom_shade = 0.8;       // brightness at the ground

varying vec3 local_pos;
varying vec3 local_normal;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
}

// 1 on a joint line, 0 inside a block. Every other row is shifted half a block; stones also get a random shift per row.
float joint_mask(vec2 p) {
	vec2 cell = p / block;
	float row = floor(cell.y);
	cell.x += mod(row, 2.0) * 0.5;
	if (mode == 1) cell.x += hash12(vec2(row, 3.7)) * 0.6;
	vec2 inside = fract(cell) * block;
	vec2 to_edge = min(inside, block - inside);
	float edge = min(to_edge.x, to_edge.y);
	float aa = fwidth(edge);
	return 1.0 - smoothstep(joint - aa, joint + aa, edge);
}

void fragment() {
	vec3 n = abs(local_normal);
	vec3 albedo = base_color;
	if (mode < 2) {
		vec2 p = n.y > 0.7 ? local_pos.xz : (n.x > n.z ? local_pos.zy : local_pos.xy);   // top, ends, faces
		albedo = mix(albedo, joint_color, joint_mask(p));
	} else {
		albedo *= COLOR.r;   // the chunk's own shade, 0.8 … 1 (1 where the mesh has no vertex colours)
	}
	albedo *= mix(bottom_shade, 1.0, smoothstep(0.0, 1.2, local_pos.y));
	ALBEDO = albedo;
	ROUGHNESS = 1.0;
	METALLIC = 0.0;
	SPECULAR = 0.0;
	EMISSION = toon_rim(NORMAL, VIEW) * mix(vec3(1.0), albedo * 2.0, 0.6);
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 452、Art 384；客户端 0 警告 0 错误。

- [ ] **Step 5：截图**

```bash
importall
shot t7-yard yard
```

用 Read 打开 `build/shots/t7-yard.png`，核对：
- 画面中间是院子：朝镜头的东面是灰色石墙，有大块石缝；北、南两面是暖红色砖墙，有错开的砖缝；西面远端中间是木栅栏门；
- 院子正中有一辆蓝灰色靶车，头上有血条；
- 右边远处有一段 3 段长的砖墙，近处有一排栅栏；
- 墙段之间有浅槽，墙底贴着地面，不悬空也不陷进去太多；输出里没有 `SHADER ERROR`。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): toy brick and stone walls, fences and rubble heaps; fences topple, bushes vanish when shot; T restores them all"
```

---

### Task 8：特效、墙的血条和伤害数字

设计 §4.2、§4.3。`BattleFx.Debris` 是新的一组对象池（12 个，高爆弹打进草丛一次就要十几个）：一把向上、顺着推开方向抛出的碎块（彩色小方块，受重力），加上可选的一团尘土。`PropFx` 按事件选特效：墙被直接打中时（`PropDamaged` 带速度）一小把墙色碎块，溅射不放；墙塌时一大团尘土加碎块；栅栏倒时几片木屑；草丛被打掉时从顶上一团叶子（从顶上放，才不会被周围的草丛挡住）。`HealthBars` 的大小改为可调；受损而立着的墙显示一个比坦克小的血条，放在墙面中间偏上（墙顶下 1 m），这样院里靶车的血条不会和它叠在一起；每个 `PropDamaged` 在同一处弹出伤害数字（溅射用小一号）。这些都依赖 Godot，没有单元测试；效果要等 Task 9 的演示才能触发，截图放在 Task 9。

**Files:**
- Create: `src/Tank.Client/scripts/Combat/PropFx.cs`
- Modify: `src/Tank.Client/scripts/Combat/BattleFx.cs`、`scripts/Overlay/HealthBars.cs`、`scripts/Sandbox/SandboxHud.cs`、`SandboxMain.cs`

- [ ] **Step 1：实现**

修改 `src/Tank.Client/scripts/Combat/BattleFx.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Combat/BattleFx.cs b/src/Tank.Client/scripts/Combat/BattleFx.cs
index 3517848..f681bcb 100644
--- a/src/Tank.Client/scripts/Combat/BattleFx.cs
+++ b/src/Tank.Client/scripts/Combat/BattleFx.cs
@@ -12,15 +12,19 @@ public enum PuffColor { White, Grey }
 /// each swelling past full size, settling and shrinking away (~0.8 s).
 /// Impact: an orange fireball that balloons and collapses, 6–10 puffs rolling outward (<see cref="PuffColor"/>: white
 /// smoke, or grey dust off rock and armour) and 8 small stones thrown up under gravity.
-/// Pool sizes (4 muzzle, 6 impact slots) are an estimate: highest fire rate (one shell per reload, ~4 s, plus a few
-/// other tanks) times the longest particle life (1.3 s) stays well under the slot count. If every slot is busy anyway,
-/// the one that started playing longest ago is reused (idle slots always win, being the oldest). Nothing allocates per
-/// shot: no tweens, no closures; each slot advances its own flash animation from <see cref="_Process"/>.
+/// Debris (<see cref="Debris"/>): chunks thrown up and along a push (bricks, stones, wood chips, leaves), with an
+/// optional cloud of dust puffs; sized for a chip off a wall up to a whole wall coming down.
+/// Pool sizes (4 muzzle, 6 impact, 12 debris slots) are an estimate: highest fire rate (one shell per reload, ~4 s, plus
+/// a few other tanks) times the longest particle life (1.6 s) stays well under the slot count; debris comes in bursts,
+/// up to a dozen at once when an HE shell lands in a bush patch. If every slot is busy anyway, the one that started
+/// playing longest ago is reused (idle slots always win, being the oldest). Nothing allocates per shot: no tweens, no closures; each slot
+/// advances its own flash animation from <see cref="_Process"/>.
 /// </summary>
 public partial class BattleFx : Node3D
 {
     private const int MuzzleSlots = 4;
     private const int ImpactSlots = 6;
+    private const int DebrisSlots = 12;
     private const float MuzzleFlashTime = 0.08f;
     private const float MuzzleFlashSize = 1.3f;
     private const float FireballGrowTime = 0.1f;
@@ -32,11 +36,13 @@ public partial class BattleFx : Node3D
 
     private readonly List<MuzzleSlot> _muzzles = new();
     private readonly List<ImpactSlot> _impacts = new();
+    private readonly List<DebrisSlot> _debris = new();
 
     public override void _Ready()
     {
         for (int i = 0; i < MuzzleSlots; i++) _muzzles.Add(new MuzzleSlot(this));
         for (int i = 0; i < ImpactSlots; i++) _impacts.Add(new ImpactSlot(this));
+        for (int i = 0; i < DebrisSlots; i++) _debris.Add(new DebrisSlot(this));
     }
 
     public override void _Process(double delta)
@@ -44,6 +50,7 @@ public partial class BattleFx : Node3D
         float dt = (float)delta;
         foreach (MuzzleSlot slot in _muzzles) slot.Advance(dt);
         foreach (ImpactSlot slot in _impacts) slot.Advance(dt);
+        foreach (DebrisSlot slot in _debris) slot.Advance(dt);
     }
 
     /// <summary>The slot that has been idle or playing for the longest time.</summary>
@@ -68,6 +75,16 @@ public partial class BattleFx : Node3D
         Oldest(_impacts).Play(at, puffs == PuffColor.Grey ? Palette.RockDust.ToColor() : Palette.Smoke.ToColor(), size);
     }
 
+    /// <summary>
+    /// Chunks of <paramref name="chunks"/> colour thrown up from <paramref name="at"/> and along <paramref name="push"/>
+    /// (level; zero for straight up), with puffs of <paramref name="dust"/> if given. <paramref name="size"/> 1 is a chip
+    /// off a wall: about five small chunks; a collapsing wall is about 2, with three times the chunks.
+    /// </summary>
+    public void Debris(Vector3 at, Vector3 push, Color chunks, Color? dust, float size)
+    {
+        Oldest(_debris).Play(at, push, chunks, dust, size);
+    }
+
     private interface ISlot
     {
         /// <summary>Seconds since this slot last started playing (large while idle).</summary>
@@ -259,4 +276,84 @@ public partial class BattleFx : Node3D
             _stones.Restart();
         }
     }
+
+    private sealed class DebrisSlot : ISlot
+    {
+        private const int Chunks = 18, Puffs = 12;
+        private const float ChunkScale = 0.22f, ChunkSpeed = 4.5f, PuffScale = 1.1f, PuffSpeed = 2.5f;
+        private const float Lifetime = 1.6f;
+        private const float AgeCap = 10f;
+
+        private readonly GpuParticles3D _chunks;
+        private readonly ParticleProcessMaterial _chunkProcess;
+        private readonly GpuParticles3D _puffs;
+        private readonly ParticleProcessMaterial _puffProcess;
+        private float _age = float.MaxValue;
+
+        public DebrisSlot(Node parent)
+        {
+            _chunkProcess = new ParticleProcessMaterial
+            {
+                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
+                EmissionSphereRadius = 0.4f,
+                Direction = Vector3.Up,
+                Spread = 55f,
+                Gravity = new Vector3(0, -9.8f, 0),
+                AngularVelocityMin = -540f,
+                AngularVelocityMax = 540f,
+                ScaleCurve = ParticleKit.Pop(start: 1f, overshoot: 1f, peakAt: 0.1f, shrinkFrom: 0.75f),
+            };
+            _chunks = ParticleKit.OneShot(Chunks, Lifetime, 1f, _chunkProcess, new BoxMesh { Size = Vector3.One, Material = ParticleKit.Puff });
+            _puffProcess = new ParticleProcessMaterial
+            {
+                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
+                Direction = Vector3.Up,
+                Spread = 80f,
+                DampingMin = 3f,
+                DampingMax = 5f,
+                Gravity = new Vector3(0, 0.5f, 0),
+                ScaleCurve = ParticleKit.Pop(start: 0.3f, overshoot: 1.15f, peakAt: 0.2f, shrinkFrom: 0.45f),
+            };
+            _puffs = ParticleKit.OneShot(Puffs, Lifetime, 0.85f, _puffProcess, ParticleKit.PuffBall);
+            parent.AddChild(_chunks);
+            parent.AddChild(_puffs);
+        }
+
+        public float Age => _age;
+
+        public void Advance(float dt)
+        {
+            if (_age < Lifetime + AgeCap) _age += dt;
+        }
+
+        public void Play(Vector3 at, Vector3 push, Color chunks, Color? dust, float size)
+        {
+            _age = 0f;
+            Vector3 level = new(push.X, 0, push.Z);
+            _chunkProcess.Direction = (Vector3.Up + (level.LengthSquared() > 1e-6f ? level.Normalized() * 0.7f : Vector3.Zero)).Normalized();
+            _chunkProcess.Color = chunks;
+            _chunkProcess.ScaleMin = ChunkScale * MathF.Sqrt(size);
+            _chunkProcess.ScaleMax = ChunkScale * 1.8f * MathF.Sqrt(size);
+            _chunkProcess.InitialVelocityMin = ChunkSpeed * 0.6f * MathF.Sqrt(size);
+            _chunkProcess.InitialVelocityMax = ChunkSpeed * 1.4f * MathF.Sqrt(size);
+            _chunkProcess.EmissionSphereRadius = 0.3f * size;
+            _chunks.GlobalPosition = at;
+            _chunks.AmountRatio = Math.Clamp(0.28f * size, 0.2f, 1f);
+            _chunks.Restart();
+            if (dust is not { } puffColor)
+            {
+                _puffs.Emitting = false;
+                return;
+            }
+            _puffProcess.Color = puffColor;
+            _puffProcess.EmissionBoxExtents = new Vector3(0.9f, 0.6f, 0.9f) * size;
+            _puffProcess.ScaleMin = PuffScale * 0.7f * size;
+            _puffProcess.ScaleMax = PuffScale * 1.3f * size;
+            _puffProcess.InitialVelocityMin = PuffSpeed * 0.5f * size;
+            _puffProcess.InitialVelocityMax = PuffSpeed * size;
+            _puffs.GlobalPosition = at;
+            _puffs.AmountRatio = Math.Clamp(0.25f * size * size, 0.15f, 1f);
+            _puffs.Restart();
+        }
+    }
 }
PATCH
```

新建 `src/Tank.Client/scripts/Combat/PropFx.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Maps;

namespace Tank.Client.Combat;

/// <summary>
/// Effects for props (stage 2C design §4.2): a shell striking a wall directly (not a splash) throws a few chips of it; a wall coming down
/// bursts into a big cloud of dust and chunks over where it stood; a fence knocked down sheds wood chips; a bush shot
/// away bursts into leaves from near its top. The shell's own burst (<see cref="GameEventType.ShellExploded"/>) plays as before.
/// </summary>
public static class PropFx
{
    private const float ChipSize = 1f;
    private const float CollapseSize = 2f;
    private const float FenceSize = 0.9f;
    private const float LeafSize = 2.4f;
    private const float CollapseHeight = 1.2f;   // the cloud rises from about this high on the wall
    private const float FenceChipHeight = 0.6f;   // wood chips fly from about half-way up the panel
    private const float LeafBelowTop = 1f;

    public static void OnEvent(in GameEvent e, Battlefield field, BattleFx fx)
    {
        if (e.Type is not (GameEventType.PropDamaged or GameEventType.PropDestroyed)) return;
        Prop prop = field.PropById(e.PropId);
        Vector3 push = e.Velocity.ToGodot();
        if (e.Type == GameEventType.PropDamaged)
        {
            bool direct = e.Velocity != System.Numerics.Vector3.Zero;   // a splash carries no velocity
            if (direct) fx.Debris(e.Position.ToGodot(), Vector3.Zero, Chunks(prop.Kind), null, ChipSize);
            return;
        }
        Vector3 ground = prop.Footprint.Center.Ground(prop.Bottom + WallLine.Sink);
        switch (prop.Kind)
        {
            case PropKind.BrickWall or PropKind.StoneWall:
                fx.Debris(ground + Vector3.Up * CollapseHeight, push, Chunks(prop.Kind), Dust(prop.Kind), CollapseSize);
                break;
            case PropKind.Fence:
                fx.Debris(ground + Vector3.Up * FenceChipHeight, push, Palette.Wood.ToColor(), null, FenceSize);
                break;
            case PropKind.Bush:   // from near the top, so the burst shows over the bushes around it
                fx.Debris(prop.Footprint.Center.Ground(prop.Top - LeafBelowTop), push, Palette.BushLight.ToColor(), Palette.BushDark.ToColor(), LeafSize);
                break;
        }
    }

    private static Color Chunks(PropKind kind) => (kind == PropKind.StoneWall ? Palette.Stone : Palette.Brick).ToColor();

    private static Color Dust(PropKind kind) => (kind == PropKind.StoneWall ? Palette.RockDust : Palette.BrickDust).ToColor();
}
```

修改 `src/Tank.Client/scripts/Overlay/HealthBars.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/HealthBars.cs b/src/Tank.Client/scripts/Overlay/HealthBars.cs
index 5b8b6e0..22004d5 100644
--- a/src/Tank.Client/scripts/Overlay/HealthBars.cs
+++ b/src/Tank.Client/scripts/Overlay/HealthBars.cs
@@ -3,18 +3,20 @@ using Godot;
 namespace Tank.Client.Overlay;
 
 /// <summary>
-/// Cartoon capsule health bars floating over other tanks' heads, in screen space: green, red when low
-/// (<see cref="HudStyle.HealthFill"/>), with a dark outline. Shown only for live tanks within <see cref="MaxRange"/> m of
-/// the camera and in front of it (full health too). Feed the bars every frame with <see cref="Set"/>, then they draw themselves.
+/// Cartoon capsule health bars floating over other tanks' heads (and over damaged walls), in screen space: green, red
+/// when low (<see cref="HudStyle.HealthFill"/>), with a dark outline. Shown only for bars marked shown within
+/// <see cref="MaxRange"/> m of the camera and in front of it. Feed the bars every frame with <see cref="Set"/>, then they
+/// draw themselves.
 /// </summary>
 public partial class HealthBars : Control
 {
     private const float MaxRange = 250f;
-    private const float Width = 64f, Height = 11f, Outline = 2.5f;
+    private const float Outline = 2.5f;
     private const int CornerDetail = 6;
 
-    private readonly StyleBoxFlat _back = HudStyle.RoundedBox(HudStyle.OutlineColor, (int)(Height / 2 + Outline), CornerDetail);
-    private readonly HudStyle.HealthFill _fill = new((int)(Height / 2), CornerDetail);
+    private float _width = 64f, _height = 11f;
+    private StyleBoxFlat _back = null!;
+    private HudStyle.HealthFill _fill = null!;
     private Bar[] _bars = Array.Empty<Bar>();
     private Camera3D? _camera;
 
@@ -31,18 +33,21 @@ public partial class HealthBars : Control
         MouseFilter = MouseFilterEnum.Ignore;
     }
 
-    public void Setup(Camera3D camera, int count)
+    /// <param name="width">Bar size in pixels, outline aside: 64 × 11 over tanks, smaller over walls.</param>
+    public void Setup(Camera3D camera, int count, float width = 64f, float height = 11f)
     {
-        _camera = camera;
+        (_camera, _width, _height) = (camera, width, height);
         _bars = new Bar[count];
+        _back = HudStyle.RoundedBox(HudStyle.OutlineColor, (int)(height / 2 + Outline), CornerDetail);
+        _fill = new HudStyle.HealthFill((int)(height / 2), CornerDetail);
     }
 
     /// <param name="head">World point just above the tank, where the bar sits.</param>
     /// <param name="share">Health left, 0 … 1.</param>
-    /// <param name="alive">False for a wreck: no bar.</param>
-    public void Set(int index, Vector3 head, float share, bool alive)
+    /// <param name="shown">False for no bar (a wreck, a wall that is down or unhurt).</param>
+    public void Set(int index, Vector3 head, float share, bool shown)
     {
-        _bars[index] = new Bar { Shown = alive, Head = head, Share = Math.Clamp(share, 0f, 1f) };
+        _bars[index] = new Bar { Shown = shown, Head = head, Share = Math.Clamp(share, 0f, 1f) };
         QueueRedraw();
     }
 
@@ -55,10 +60,10 @@ public partial class HealthBars : Control
             if (!bar.Shown || bar.Head.DistanceTo(eye) > MaxRange || _camera.IsPositionBehind(bar.Head)) continue;
             Vector2 at = _camera.UnprojectPosition(bar.Head);
             if (!GetViewportRect().HasPoint(at)) continue;
-            var back = new Rect2(at.X - Width / 2 - Outline, at.Y - Height / 2 - Outline, Width + 2 * Outline, Height + 2 * Outline);
+            var back = new Rect2(at.X - _width / 2 - Outline, at.Y - _height / 2 - Outline, _width + 2 * Outline, _height + 2 * Outline);
             DrawStyleBox(_back, back);
             if (bar.Share <= 0f) continue;
-            DrawStyleBox(_fill.For(bar.Share), new Rect2(at.X - Width / 2, at.Y - Height / 2, MathF.Max(Height, Width * bar.Share), Height));
+            DrawStyleBox(_fill.For(bar.Share), new Rect2(at.X - _width / 2, at.Y - _height / 2, MathF.Max(_height, _width * bar.Share), _height));
         }
     }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxHud.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
index f05a261..30515a4 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
@@ -11,13 +11,17 @@ using SN = System.Numerics;
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' health bars (over the popups,
-/// so a bar stays in sight while it drops), the reticle and the status panel. <see cref="OnEvent"/> spawns hit verdicts
-/// and damage numbers; <see cref="Refresh"/> updates the rest once per frame.
+/// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' and the damaged walls' health
+/// bars (over the popups, so a bar stays in sight while it drops), the reticle and the status panel.
+/// <see cref="OnEvent"/> spawns hit verdicts and damage numbers (over targets and walls); <see cref="Refresh"/> updates
+/// the rest once per frame. A wall's bar, smaller than a tank's and drawn on its face, shows only while it is standing
+/// and short of full hit points.
 /// </summary>
 public partial class SandboxHud : CanvasLayer
 {
     private const float KmhPerMs = 3.6f;
+    private const float WallBarBelowTop = 1f;    // a wall's bar sits on its face, this far under the top, clear of any tank's bar behind it
+    private const float WallBarWidth = 44f, WallBarHeight = 8f;   // smaller than a tank's, so the two don't read alike
 
     private OrbitCamera _camera = null!;
     private TargetRange _targets = null!;
@@ -25,6 +29,8 @@ public partial class SandboxHud : CanvasLayer
     private SniperVignette _vignette = null!;
     private CombatPopups _popups = null!;
     private HealthBars _bars = null!;
+    private HealthBars _wallBars = null!;
+    private Prop[] _walls = Array.Empty<Prop>();
     private Reticle _reticle = null!;
     private StatusPanel _status = null!;
 
@@ -36,6 +42,9 @@ public partial class SandboxHud : CanvasLayer
         _popups.SetCamera(camera.Camera);
         _bars = Add(new HealthBars());
         _bars.Setup(camera.Camera, targets.Count);
+        _walls = field.Props.Where(p => p.IsWall).ToArray();
+        _wallBars = Add(new HealthBars());
+        _wallBars.Setup(camera.Camera, _walls.Length, WallBarWidth, WallBarHeight);
         _reticle = Add(new Reticle());
         _status = Add(new StatusPanel());
     }
@@ -52,9 +61,16 @@ public partial class SandboxHud : CanvasLayer
             case GameEventType.SplashDamage when _targets.TryGetHead(e.TargetTankId, out Vector3 splashed):
                 _popups.Damage(splashed, e.Damage, splash: true);
                 break;
+            case GameEventType.PropDamaged:
+                Prop wall = _field.PropById(e.PropId);
+                _popups.Damage(WallHead(wall), e.Damage, splash: e.Velocity == SN.Vector3.Zero);   // a direct hit carries the shell's velocity
+                break;
         }
     }
 
+    /// <summary>Where a wall's bar and damage numbers go: on the middle of its face, a little under the top.</summary>
+    private static Vector3 WallHead(in Prop wall) => wall.Footprint.Center.Ground(wall.Top - WallBarBelowTop);
+
     /// <summary>Once per frame, after the targets have been synced: reticle, status panel, vignette and health bars.</summary>
     public void Refresh(in TankState player)
     {
@@ -67,6 +83,11 @@ public partial class SandboxHud : CanvasLayer
             var (head, health, alive) = _targets.Status(i);
             _bars.Set(i, head, health, alive);
         }
+        for (int i = 0; i < _walls.Length; i++)
+        {
+            int left = _field.Durability(_walls[i].Id), full = _walls[i].MaxDurability;
+            _wallBars.Set(i, WallHead(_walls[i]), left / (float)full, shown: left > 0 && left < full);
+        }
     }
 
     private T Add<T>(T control) where T : Node
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 4dc39a0..11a16ef 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -212,6 +212,7 @@ public partial class SandboxMain : Node3D
         _hud.OnEvent(e);
         _targets.OnEvent(e, _sim, _fx);
         _world.OnEvent(e);
+        PropFx.OnEvent(e, _sim.Field, _fx);
     }
 
     /// <summary>Poses and animates the player's tank, with the muzzle blast of a shot fired this frame; hidden in the sniper sight.</summary>
PATCH
```


- [ ] **Step 2：编译、运行测试**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 452、Art 384（和 Task 7 相同）；客户端 0 警告 0 错误。

- [ ] **Step 3：提交**

```bash
git add src/Tank.Client
git commit -m "feat(client): chips, dust and collapsing walls, wood chips and leaf bursts; health bars and damage numbers on walls"
```

---

### Task 9：截图用的演示和机位

设计 §4.5。打靶演示新增瞄准车体后部（`DemoAim.HullRear`）和“开车”模式（`DriveSeconds`：不开炮，从 `FireTick` 起全油门开这么久；每个 tick 的油门由 `RangeDemo.Throttle(tick)` 给出，演示脚本和测试共用）。五个新演示：`wall`（院里靶车正前方 30 m 开一发穿甲弹，打在石墙上）、`breach`（同一位置连续开高爆弹）、`fence`（从靶车车尾西偏北方向 30 m，隔着栅栏门的北边那块打车尾；正西停车的话车和镜头都顶在后面的大石头上）、`ram`（从院子正北 17 m 全油门开 2.6 秒）、`bush`（0 号靶车车尾 50 m，隔着一片草丛）。`--shot-after` 新增 `prop`（第一次有墙受伤）和 `down`（第一次有物件被毁）。`gate` 机位从院子西北方近看栅栏门。测试检查每个演示弹道先碰到的东西，并用真实地图跑一遍 `ram`：只有重坦撞进院子，也不会撞上靶车。

**Files:**
- Modify: `src/Tank.Client/scripts/Sandbox/RangeDemo.cs`、`SandboxAutomation.cs`、`SandboxMain.cs`、`scripts/Bootstrap/LaunchOptions.cs`、`scripts/Cameras/CameraPresets.cs`
- Test: 改 `tests/Tank.Art.Tests/RangeDemoTests.cs`、`LaunchOptionsTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/LaunchOptionsTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/LaunchOptionsTests.cs b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
index 3b53921..542a68e 100644
--- a/tests/Tank.Art.Tests/LaunchOptionsTests.cs
+++ b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
@@ -37,4 +37,23 @@ public class LaunchOptionsTests
         string warning = Assert.Single(options.Warnings);
         Assert.Contains("''", warning);
     }
+
+    [Theory]
+    [InlineData("prop:1.5", "prop", 1.5)]
+    [InlineData("down:0", "down", 0)]
+    [InlineData("kill:2", "kill", 2)]
+    public void Shot_after_waits_for_a_wall_hurt_or_a_prop_down_too(string value, string moment, double delay)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--shot-after", value });
+        Assert.Equal((moment, delay), (options.ShotAfterEvent, options.ShotAfterDelay));
+        Assert.Empty(options.Warnings);
+    }
+
+    [Fact]
+    public void Shot_after_an_unknown_moment_is_ignored_with_a_warning_naming_the_known_ones()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--shot-after", "wall:1" });
+        Assert.Null(options.ShotAfterEvent);
+        Assert.Contains("fire|hit|kill|prop|down", Assert.Single(options.Warnings));
+    }
 }
PATCH
```

修改 `tests/Tank.Art.Tests/RangeDemoTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/RangeDemoTests.cs b/tests/Tank.Art.Tests/RangeDemoTests.cs
index 5c0df24..b16e0b2 100644
--- a/tests/Tank.Art.Tests/RangeDemoTests.cs
+++ b/tests/Tank.Art.Tests/RangeDemoTests.cs
@@ -1,8 +1,11 @@
 using Tank.Client.Sandbox;
+using Tank.Sim;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
 
+using SN = System.Numerics;
+
 namespace Tank.Art.Tests;
 
 public class RangeDemoTests
@@ -32,18 +35,102 @@ public class RangeDemoTests
             Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(s.Spec, s.Position, s.Yaw), out _), "parked on a target");
     }
 
+    /// <summary>The demos whose shots must reach their target with nothing solid in the way.</summary>
+    private static readonly string[] OpenDemos = { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "fence", "bush" };
+
+    public static TheoryData<string, string> OpenDemosAndTanks()
+    {
+        var data = new TheoryData<string, string>();
+        foreach (string name in OpenDemos)
+        foreach (TankSpec spec in TankCatalog.All)
+            data.Add(name, spec.Id);
+        return data;
+    }
+
+    public static TheoryData<string> Tanks() => new() { "light", "medium", "heavy", "td" };
+
+    /// <summary>The parked player's trunnion and the demo's aim point.</summary>
+    private static (SN.Vector3 From, SN.Vector3 To) LineOfFire(RangeDemo d, TankSpec spec)
+    {
+        var parked = new TankState { Spec = spec, Position = d.PlayerPosition, Yaw = d.PlayerYaw };
+        SN.Vector3 trunnion = TankPose.Compute(parked, Field.Terrain).Trunnion(spec);
+        SN.Vector2 spot = d.Spot.Position;
+        return (trunnion, d.AimPoint(Field.Terrain.HeightAt(spot.X, spot.Y)));
+    }
+
     [Theory]
-    [MemberData(nameof(DemosAndTanks))]
+    [MemberData(nameof(OpenDemosAndTanks))]
     public void Shoots_along_a_line_no_wall_or_rock_stands_in(string demo, string tank)
     {
-        RangeDemo d = RangeDemo.Find(demo)!;
-        TankSpec spec = TankCatalog.ById(tank);
-        var parked = new TankState { Spec = spec, Position = d.PlayerPosition, Yaw = d.PlayerYaw };
-        System.Numerics.Vector3 trunnion = TankPose.Compute(parked, Field.Terrain).Trunnion(spec);
-        System.Numerics.Vector2 spot = d.Spot.Position;
-        System.Numerics.Vector3 aim = d.AimPoint(Field.Terrain.HeightAt(spot.X, spot.Y));
-        bool blocked = Field.Raycast(trunnion, aim, out RayHit hit);
-        Assert.False(blocked, $"stopped by {(blocked && hit.PropId != RayHit.TerrainId ? Field.Props.Single(p => p.Id == hit.PropId).Kind : "the ground")} at {hit.Point}");
+        var (from, to) = LineOfFire(RangeDemo.Find(demo)!, TankCatalog.ById(tank));
+        bool blocked = Field.Raycast(from, to, out RayHit hit);
+        Assert.False(blocked, $"stopped by {(blocked && hit.PropId != RayHit.TerrainId ? Field.PropById(hit.PropId).Kind : "the ground")} at {hit.Point}");
+    }
+
+    [Theory]
+    [InlineData("wall")]
+    [InlineData("breach")]
+    public void The_wall_demos_shoot_into_the_yards_stone_wall(string demo)
+    {
+        foreach (TankSpec spec in TankCatalog.All)
+        {
+            var (from, to) = LineOfFire(RangeDemo.Find(demo)!, spec);
+            Assert.True(Field.Raycast(from, to, out RayHit hit) && hit.PropId != RayHit.TerrainId, $"{spec.Id}: nothing in the way");
+            Assert.Equal(PropKind.StoneWall, Field.PropById(hit.PropId).Kind);
+        }
+    }
+
+    [Theory]
+    [MemberData(nameof(Tanks))]
+    public void The_fence_demo_shoots_in_through_a_gate_panel(string tank)
+    {
+        var (from, to) = LineOfFire(RangeDemo.Find("fence")!, TankCatalog.ById(tank));
+        Assert.Contains(Field.Props, p => p.Kind == PropKind.Fence && Intersect.SegmentPrism(p.Footprint, p.Bottom, p.Top, from, to, out _));
+    }
+
+    [Theory]
+    [MemberData(nameof(Tanks))]
+    public void The_bush_demo_shoots_through_bushes(string tank)
+    {
+        var (from, to) = LineOfFire(RangeDemo.Find("bush")!, TankCatalog.ById(tank));
+        Assert.Contains(Field.Props, p => p.Kind == PropKind.Bush
+            && Intersect.SegmentCylinder(p.Footprint.Center, p.Footprint.HalfExtents.X, p.Bottom, p.Top, from, to, out _));
+    }
+
+    [Theory]
+    [InlineData("heavy", true)]
+    [InlineData("medium", false)]
+    [InlineData("light", false)]
+    [InlineData("td", false)]
+    public void The_ram_demo_breaks_into_the_yard_only_in_a_heavy_tank(string tank, bool breaksThrough)
+    {
+        RangeDemo d = RangeDemo.Find("ram")!;
+        var sim = new Simulation(SandboxMap.Create());
+        sim.AddTank(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw);
+        var down = new List<int>();
+        for (int tick = 1; tick < RangeDemo.FireTick + d.DriveTicks + SimConstants.TickRate; tick++)
+        {
+            sim.Step(new[] { new InputCmd(tick, d.Throttle(tick), 0, d.PlayerYaw, 0f, false) });
+            foreach (GameEvent e in sim.Events)
+                if (e.Type == GameEventType.PropDestroyed) down.Add(e.PropId);
+        }
+        Assert.Equal(breaksThrough, down.Count > 0);
+        Assert.All(down, id => Assert.Equal(PropKind.BrickWall, Field.PropById(id).Kind));
+        Obb2 hull = TankMotion.Footprint(sim.Tanks[0].Spec, sim.Tanks[0].Position, sim.Tanks[0].Yaw);
+        TargetSpot yard = SandboxMap.TargetSpots[SandboxMap.YardTarget];
+        Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(yard.Spec, yard.Position, yard.Yaw), out _), "drove into the target");
+    }
+
+    [Fact]
+    public void Only_a_driving_demo_opens_the_throttle_and_only_for_its_drive()
+    {
+        RangeDemo ram = RangeDemo.Find("ram")!;
+        Assert.Equal(78, ram.DriveTicks);   // 2.6 s
+        Assert.Equal(0, ram.Throttle(RangeDemo.FireTick - 1));
+        Assert.Equal(1, ram.Throttle(RangeDemo.FireTick));
+        Assert.Equal(1, ram.Throttle(RangeDemo.FireTick + ram.DriveTicks - 1));
+        Assert.Equal(0, ram.Throttle(RangeDemo.FireTick + ram.DriveTicks));
+        Assert.All(RangeDemo.Names.Where(n => n != "ram"), n => Assert.Equal(0, RangeDemo.Find(n)!.Throttle(RangeDemo.FireTick)));
     }
 
     [Fact]
@@ -63,6 +150,11 @@ public class RangeDemoTests
     [InlineData("turn", "medium")]
     [InlineData("nopen-heavy", "heavy")]
     [InlineData("kill-td", "td")]
+    [InlineData("wall", "medium")]
+    [InlineData("breach", "medium")]
+    [InlineData("fence", "medium")]
+    [InlineData("ram", "medium")]
+    [InlineData("bush", "medium")]
     public void Shoots_the_kind_of_tank_it_was_written_for(string demo, string kind) =>
         Assert.Equal(kind, RangeDemo.Find(demo)!.Spot.Spec.Id);
 }
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS|失败!" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`RangeDemo.DriveTicks`、`RangeDemo.Throttle` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index 9b95cf4..3ad69d9 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -12,7 +12,8 @@ namespace Tank.Client.Bootstrap;
 /// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
 /// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
-/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn（试驾场：自动对靶车开炮，用于截图）  --shot-after fire|hit|kill:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克或第一次击毁坦克后再过 SECONDS 秒截图）
+/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush（试驾场：自动对靶车开炮或开车撞墙，用于截图）
+/// --shot-after fire|hit|kill|prop|down:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤或第一次有物件被毁后再过 SECONDS 秒截图）
 /// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
 /// for the caller to report.
@@ -40,7 +41,7 @@ public sealed record LaunchOptions
     public bool Sniper { get; init; }
     public string? RangeDemo { get; init; }
 
-    /// <summary>"fire", "hit" or "kill" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
+    /// <summary>"fire", "hit", "kill", "prop" or "down" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
     public string? ShotAfterEvent { get; init; }
     public double ShotAfterDelay { get; init; }
 
@@ -98,9 +99,9 @@ public sealed record LaunchOptions
     private static LaunchOptions ParseShotAfter(LaunchOptions o, string value, List<string> warnings)
     {
         string[] parts = value.Split(':');
-        if (parts.Length != 2 || parts[0] is not ("fire" or "hit" or "kill") || !TryParseSeconds(parts[1], out double delay))
+        if (parts.Length != 2 || parts[0] is not ("fire" or "hit" or "kill" or "prop" or "down") || !TryParseSeconds(parts[1], out double delay))
         {
-            warnings.Add($"Bad --shot-after '{value}'; use fire|hit|kill:SECONDS, e.g. kill:2.5.");
+            warnings.Add($"Bad --shot-after '{value}'; use fire|hit|kill|prop|down:SECONDS, e.g. kill:2.5.");
             return o;
         }
         return o with { ShotAfterEvent = parts[0], ShotAfterDelay = delay };
PATCH
```

修改 `src/Tank.Client/scripts/Cameras/CameraPresets.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Cameras/CameraPresets.cs b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
index c471f65..985e087 100644
--- a/src/Tank.Client/scripts/Cameras/CameraPresets.cs
+++ b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
@@ -8,7 +8,8 @@ namespace Tank.Client.Cameras;
 /// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
 /// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at a practice target (the
 /// range demo's, else the first) from its front-right, on the side where a thrown turret lands (pair it with --range-demo);
-/// yard looks down on the walled yard and the cover beside the spawn from the south-east.
+/// yard looks down on the walled yard and the cover beside the spawn from the south-east; gate looks at the yard's fence
+/// gate from just outside it (pair it with --range-demo fence).
 /// </summary>
 public static class CameraPresets
 {
@@ -24,13 +25,15 @@ public static class CameraPresets
     private static readonly Vector2 YardEye = new(204f, 358f);          // south-east of the walled yard, behind the spawn
     private static readonly Vector3 YardTarget = new(166f, 0f, 334f);   // between the yard and the cover by the spawn
     private const float YardEyeHeight = 15f;
+    private static readonly Vector2 GateEye = new(139f, 330.5f);       // just outside the yard's fence gate, to the north-west
+    private static readonly Vector3 GateFocus = new(151f, 0.9f, 339f);  // the gate's north panel
     private static readonly Vector3 TargetEye = new(7f, 4.5f, -13f);    // front-right of the target (its forward is −Z)
     private static readonly Vector3 TargetFocus = new(1.5f, 1.6f, 0f);  // between the hull and where the turret lands
 
     /// <summary>Presets whose eye is far from the tank: world detail (grass) should then stream around the eye, not the tank.</summary>
     public static bool StreamsAroundCamera(string? name) => name is "hill" or "spin";
 
-    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush" or "target" or "yard";
+    public static bool IsKnown(string? name) => name is "front" or "side" or "close" or "closerear" or "sprocket" or "hill" or "spin" or "orbit" or "bush" or "target" or "yard" or "gate";
 
     /// <summary>Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, the rest use the normal spawn.</summary>
     public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;
@@ -62,6 +65,8 @@ public static class CameraPresets
             case "yard":
                 return (new Vector3(YardEye.X, groundAt(YardEye.X, YardEye.Y) + YardEyeHeight, YardEye.Y),
                     YardTarget + Vector3.Up * groundAt(YardTarget.X, YardTarget.Z));
+            case "gate":
+                return (new Vector3(GateEye.X, groundAt(GateEye.X, GateEye.Y) + 3f, GateEye.Y), GateFocus + Vector3.Up * groundAt(GateFocus.X, GateFocus.Z));
             case "hill":
                 return (new Vector3(HillEye.X, groundAt(HillEye.X, HillEye.Y) + 28f, HillEye.Y), HillTarget);
             case "orbit": // circles the parked tank at 14 m, level with the turret: stresses anything keyed to the camera
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/RangeDemo.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/RangeDemo.cs b/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
index 3bb3978..ecc5b9e 100644
--- a/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
+++ b/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
@@ -16,6 +16,8 @@ public enum DemoAim
     TurretFront,
     /// <summary>The turret box's rear face (a casemate's box is the hull's, centred on the hull origin).</summary>
     TurretRear,
+    /// <summary>The hull box's rear face.</summary>
+    HullRear,
 }
 
 /// <summary>
@@ -24,9 +26,11 @@ public enum DemoAim
 /// ahead of it, its hull <see cref="PlayerTurn"/> degrees to the left of facing the target, lays its gun on the
 /// <see cref="Aim"/> face of that target's own armour (<see cref="AimLocal"/>, worked out from <see cref="Spot"/>'s
 /// <see cref="TankSpec"/>, so it can't drift from the kind of tank standing there) and pulls the trigger from
-/// <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded.
+/// <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded. A demo with <see cref="DriveSeconds"/>
+/// fires nothing: from <see cref="FireTick"/> it drives at full throttle for that long, then stops.
 /// </summary>
-public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, DemoAim Aim, AmmoType Ammo, bool KeepFiring, float PlayerTurn = 0f)
+public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, DemoAim Aim, AmmoType Ammo, bool KeepFiring,
+    float PlayerTurn = 0f, float DriveSeconds = 0f)
 {
     public const double FireAfter = 3.0;
 
@@ -40,15 +44,19 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
     public static readonly int FireTick = (int)Math.Ceiling(FireAfter * SimConstants.TickRate);
 
     /// <summary>The demos' names, in the order <see cref="Find"/> documents them.</summary>
-    public static IReadOnlyList<string> Names { get; } = new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn" };
+    public static IReadOnlyList<string> Names { get; } =
+        new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "wall", "breach", "fence", "ram", "bush" };
 
     /// <summary>
     /// pen, ricochet, nopen, he or kill (at target 0, a medium tank); nopen-heavy (the far heavy tank's 200 mm turret
     /// front, head-on from the spawn's side, on the sight line the target tests keep clear: no light tank gets through;
     /// 52 m out, where even the heavy tank parks clear of the rocks); kill-td (the tank
     /// destroyer's rear until it is knocked out); turn (target 0 again, the hull starting 60° off: a tank destroyer has to
-    /// turn its hull to bring the gun round); null for any other name. Each shoots with whatever tank --tank gives the
-    /// player.
+    /// turn its hull to bring the gun round). At the yard target (stage 2C): wall (one AP shot from 30 m in front, into
+    /// the stone wall between), breach (HE from there until the wall is down and the target behind it), fence (AP at its
+    /// rear from 30 m west by north-west, in through a panel of the fence gate), ram (from 17 m north of it, 2.6 s at full throttle into the
+    /// north wall: a heavy tank breaks through, the others are stopped); bush (target 0's rear from 50 m, through a bush
+    /// patch); null for any other name. Each shoots, or drives, with whatever tank --tank gives the player.
     /// </summary>
     public static RangeDemo? Find(string? name) => name switch
     {
@@ -60,9 +68,32 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
         "nopen-heavy" => new RangeDemo(5, 0f, 52f, DemoAim.TurretFront, AmmoType.AP, false),
         "kill-td" => new RangeDemo(3, 180f, 45f, DemoAim.TurretRear, AmmoType.AP, true),
         "turn" => new RangeDemo(0, 0f, 45f, DemoAim.HullFront, AmmoType.AP, false, PlayerTurn: 60f),
+        "wall" => new RangeDemo(SandboxMap.YardTarget, 0f, 30f, DemoAim.HullFront, AmmoType.AP, false),
+        "breach" => new RangeDemo(SandboxMap.YardTarget, 0f, 30f, DemoAim.HullFront, AmmoType.HE, true),
+        "fence" => new RangeDemo(SandboxMap.YardTarget, YardGateOffFront, 30f, DemoAim.HullRear, AmmoType.AP, false),
+        "ram" => new RangeDemo(SandboxMap.YardTarget, YardNorthOffFront, 17f, DemoAim.HullFront, AmmoType.AP, false, DriveSeconds: 2.6f),
+        "bush" => new RangeDemo(0, 210f, 50f, DemoAim.HullRear, AmmoType.AP, false),
         _ => null,
     };
 
+    /// <summary>
+    /// Degrees round from the yard target's front to west by north-west: the line from its rear in through the middle of
+    /// the gate's north panel (straight west would park the player, and the camera behind it, against the rocks there).
+    /// </summary>
+    private static float YardGateOffFront => OffFrontTowards(new SN.Vector2(-1, -0.35f));
+
+    /// <summary>Degrees round from the yard target's front to due north (−Z), square on to the yard's north wall.</summary>
+    private static float YardNorthOffFront => OffFrontTowards(new SN.Vector2(0, -1));
+
+    private static float OffFrontTowards(SN.Vector2 direction) =>
+        Angles.Wrap(Angles.YawOf(direction) - SandboxMap.TargetSpots[SandboxMap.YardTarget].Yaw) * 180f / MathF.PI;
+
+    /// <summary>Ticks of full throttle for a driving demo, starting at <see cref="FireTick"/>.</summary>
+    public int DriveTicks => (int)MathF.Round(DriveSeconds * SimConstants.TickRate);
+
+    /// <summary>The throttle on sim tick <paramref name="tick"/>: full for a driving demo's <see cref="DriveTicks"/> from <see cref="FireTick"/>, else none.</summary>
+    public sbyte Throttle(int tick) => (sbyte)(tick >= FireTick && tick < FireTick + DriveTicks ? 1 : 0);
+
     public TargetSpot Spot => SandboxMap.TargetSpots[Target];
 
     /// <summary>Where the player's tank is parked.</summary>
@@ -90,6 +121,7 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
                 DemoAim.HullFront => FaceMiddle(spec.HullArmor, 0f, front: true),
                 DemoAim.TurretFront => FaceMiddle(spec.TurretArmor, turretAhead, front: true),
                 DemoAim.TurretRear => FaceMiddle(spec.TurretArmor, turretAhead, front: false),
+                DemoAim.HullRear => FaceMiddle(spec.HullArmor, 0f, front: false),
                 _ => throw new InvalidOperationException($"Unknown {nameof(DemoAim)} {Aim}."),
             };
         }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
index 69de929..b9e38ab 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
@@ -14,8 +14,8 @@ namespace Tank.Client.Sandbox;
 /// with --range-demo (lay the gun on a target and fire), --autodrive (drive loops, always firing) or a fixed
 /// --camera-preset (stand still). Timed steps wait for a <see cref="Moment"/>, each noted the first time it happens:
 /// the screenshot is taken a delay after the run's start (--screenshot-at), the player's first shot (--fx-shot, or
-/// --shot-after fire) or the first tank hit or kill (--shot-after hit|kill), and a range demo that fires once stops
-/// after the player's first shot. With --fx-shot, nothing before a warm-up counts. Also --fps-log and --quit-after.
+/// --shot-after fire), the first tank hit or kill (--shot-after hit|kill), or the first wall hurt or prop destroyed
+/// (--shot-after prop|down), and a range demo that fires once stops after the player's first shot. With --fx-shot, nothing before a warm-up counts. Also --fps-log and --quit-after.
 /// Moments and the delays after them are wall-clock seconds (the screenshot is of what is on screen); only a range
 /// demo's first shot is timed in simulation ticks (<see cref="RangeDemo.FireTick"/>), so the shot itself is repeatable.
 /// </summary>
@@ -30,13 +30,13 @@ public sealed class SandboxAutomation
     private const double AutoTurnThreshold = 0.5;
 
     /// <summary>What a timed step waits for.</summary>
-    private enum Moment { Start, Fire, Hit, Kill }
+    private enum Moment { Start, Fire, Hit, Kill, Prop, Down }
 
     private readonly LaunchOptions _options;
     private readonly Node _host;
     private readonly bool _standStill;
     private readonly double _countFrom;
-    private readonly double[] _firstSeen = { 0, -1, -1, -1 };   // per Moment: when it first happened, s; -1 not yet
+    private readonly double[] _firstSeen = { 0, -1, -1, -1, -1, -1 };   // per Moment: when it first happened, s; -1 not yet
     private readonly Moment _shotAfter;
     private readonly double _shotDelay;
     private readonly FrameTimeLog _frameLog = new();
@@ -61,12 +61,12 @@ public sealed class SandboxAutomation
     {
         foreach (string warning in _options.Warnings) GD.PushWarning(warning);
         if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
-            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush, target or yard.");
+            GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush, target, yard or gate.");
         if (_options.RangeDemo is { } demo && Demo is null)
             GD.PushWarning($"Unknown --range-demo '{demo}'; use {string.Join(", ", RangeDemo.Names)}.");
     }
 
-    /// <summary>Notes the first time the player fired, a tank was hit and a tank was destroyed.</summary>
+    /// <summary>Notes the first time the player fired, a tank was hit or destroyed, a wall was hurt and a prop destroyed.</summary>
     public void OnEvent(in GameEvent e, int playerId, double time)
     {
         Moment? moment = e.Type switch
@@ -74,6 +74,8 @@ public sealed class SandboxAutomation
             GameEventType.ShellFired when e.TankId == playerId => Moment.Fire,
             GameEventType.TankHit => Moment.Hit,
             GameEventType.TankDestroyed => Moment.Kill,
+            GameEventType.PropDamaged => Moment.Prop,
+            GameEventType.PropDestroyed => Moment.Down,
             _ => null,
         };
         if (moment is { } m && time >= _countFrom && _firstSeen[(int)m] < 0) _firstSeen[(int)m] = time;
@@ -115,7 +117,8 @@ public sealed class SandboxAutomation
     private static (Moment After, double Delay) PlanScreenshot(LaunchOptions options)
     {
         if (options.ShotAfterEvent is { } awaited)
-            return (awaited switch { "fire" => Moment.Fire, "hit" => Moment.Hit, _ => Moment.Kill }, options.ShotAfterDelay);
+            return (awaited switch { "fire" => Moment.Fire, "hit" => Moment.Hit, "prop" => Moment.Prop, "down" => Moment.Down, _ => Moment.Kill },
+                options.ShotAfterDelay);
         if (options.FxShotDelay is { } fxDelay) return (Moment.Fire, fxDelay);
         return (Moment.Start, options.ScreenshotAt);
     }
@@ -126,15 +129,18 @@ public sealed class SandboxAutomation
         return since >= 0 && time >= since + _shotDelay;
     }
 
-    /// <summary>--range-demo: stand still, lay the gun on the demo's aim point, fire once (or keep firing) from <see cref="RangeDemo.FireTick"/> on.</summary>
+    /// <summary>
+    /// --range-demo: lay the gun on the demo's aim point and, from <see cref="RangeDemo.FireTick"/> on, fire once (or keep
+    /// firing), or for a driving demo drive at full throttle for its <see cref="RangeDemo.DriveTicks"/>; otherwise stand still.
+    /// </summary>
     private InputCmd DemoCommand(RangeDemo demo, in InputCmd player, in TankState tank, Battlefield field)
     {
         TankPose pose = TankPose.Compute(tank, field.Terrain);
         SN.Vector2 spot = demo.Spot.Position;
         SN.Vector3 aim = demo.AimPoint(field.Terrain.HeightAt(spot.X, spot.Y));
         var (yaw, pitch) = Angles.AimAngles(aim - pose.Trunnion(tank.Spec), player.AimYaw);
-        bool fire = player.Tick >= RangeDemo.FireTick && (demo.KeepFiring || _firstSeen[(int)Moment.Fire] < 0);
-        return player with { Throttle = 0, Steer = 0, AimYaw = yaw, AimPitch = pitch, Fire = fire, Ammo = demo.Ammo };
+        bool fire = demo.DriveTicks == 0 && player.Tick >= RangeDemo.FireTick && (demo.KeepFiring || _firstSeen[(int)Moment.Fire] < 0);
+        return player with { Throttle = demo.Throttle(player.Tick), Steer = 0, AimYaw = yaw, AimPitch = pitch, Fire = fire, Ammo = demo.Ammo };
     }
 
     /// <summary>--autodrive: full throttle, steering right part of the time (so it drives loops), always firing.</summary>
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 11a16ef..e48e857 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -19,8 +19,9 @@ namespace Tank.Client.Sandbox;
 /// fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
-/// --camera-preset front|side|hill|spin|orbit|bush|target|... --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn
-/// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
+/// --camera-preset front|side|hill|spin|orbit|bush|target|yard|... --fps-log --sniper
+/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush
+/// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill|prop|down:S --quit-after S]
 /// </summary>
 public partial class SandboxMain : Node3D
 {
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 452、Art 436；客户端 0 警告 0 错误。

- [ ] **Step 5：截图**（同时检查 Task 8 的特效和血条）

```bash
demoshot t9-wall wall prop:0.6 8
demoshot t9-breach breach down:0.3 30 --tank td
demoshot t9-breach-after breach down:4 32 --tank td
demoshot t9-fence fence down:0.6 8 --camera-preset gate
demoshot t9-ram-heavy ram down:0.3 12 --tank heavy
demoshot t9-ram-heavy-after ram down:2.5 14 --tank heavy
godot_run 50 --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo ram --screenshot "$PWD/build/shots/t9-ram-medium.png" --screenshot-at 7 --quit-after 7.5 2>&1 | grep -E "Screenshot|ERROR|Exception"
godot_run 40 --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo bush --screenshot "$PWD/build/shots/t9-bush-before.png" --screenshot-at 2.8 --quit-after 3 2>&1 | grep -E "Screenshot|ERROR|Exception"
demoshot t9-bush bush down:0.15 8
```

用 Read 逐张打开，核对：
- `t9-wall`：石墙上方弹出红色伤害数字（中坦穿甲弹约 80～140）；墙面上有一个小血条，少了一截；靶车自己的大血条在它上方，两者不叠在一起；打中处有灰色烟团；
- `t9-breach`：TD 开了第二发高爆弹，石墙上有伤害数字和尘土；`t9-breach-after`：石墙中间缺了一段，缺口里有一小堆灰色瓦砾，透过缺口能看到院里的靶车，两边受损的墙段带着小血条；
- `t9-fence`：栅栏门北边那块栅栏平躺在地上，倒向院子里面，另外两块还立着；靶车上有“击穿！”；
- `t9-ram-heavy`：重坦正撞进北墙，周围一大团砖红色尘土，面板速度约 13 km/h（撞穿后减半）；`t9-ram-heavy-after`：北墙缺了三段（重坦 3.6 m 宽，跨着三段撞进去），重坦停在缺口里，没有撞到院里的靶车；
- `t9-ram-medium`：中坦停在北墙外，墙完好；
- `t9-bush-before` 和 `t9-bush` 对比：坦克右前方弹道上的几丛草（共 4 丛）不见了，草丛上方有绿色的叶子碎块飞起；靶车上有“击穿！”。

不符合就先修再继续。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): range demos at walls, a fence gate, a bush patch and a ram into the yard; screenshots after a wall hit or a prop down; yard and gate cameras"
```

---

### Task 10：截图与帧率

- [ ] **Step 1：全部测试**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 452、Art 436，Core 62、Net 50、Update 42；客户端 0 警告 0 错误。

- [ ] **Step 2：验收截图**

```bash
shot final-yard yard
demoshot final-wall wall prop:0.6 8
demoshot final-breach breach down:4 32 --tank td
demoshot final-fence fence down:0.6 8 --camera-preset gate
demoshot final-ram ram down:2.5 14 --tank heavy
godot_run 50 --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo ram --screenshot "$PWD/build/shots/final-ram-medium.png" --screenshot-at 7 --quit-after 7.5 2>&1 | grep -E "Screenshot|ERROR|Exception"
demoshot final-bush bush down:0.15 8
demoshot final-pen pen hit:0.3 8
```

逐张核对（要求同 Task 7、Task 9）；`final-pen` 是 2A 的击穿演示，确认原有的打靶不受影响（“击穿！”、伤害数字）。

- [ ] **Step 3：帧率**

```bash
fpslog final-spin spin 40
fpslog final-orbit orbit 40
```

窗口保持在最前面。去掉前 20 秒，记下两段的帧数范围。验收标准是 60 帧以上；对照 2B（spin 220～280、orbit 150～310，当时显示设置限在 145 帧上下），绘制次数只应多十几次（墙段每段一次）。

- [ ] **Step 4：记录**

在 `docs/PROGRESS.md` 的“2C 节点记录”里记下测试数、截图结论和帧率，提交：

```bash
git add docs/PROGRESS.md
git commit -m "docs: progress — stage 2C screenshots and frame rate checked, playtest next"
```

---

### Task 11：用户试玩与收尾

- [ ] **Step 1：请用户试玩**

启动：`godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn`（用户自己开，不加参数）。请用户确认：
- 用高爆弹轰开院子的墙，看到瓦砾；从瓦砾上开过去会变慢；
- F3 换重坦，冲起来撞穿砖墙；中坦撞不动；石墙谁都撞不动；
- 开车撞倒栅栏，栅栏朝车开的方向倒下；
- 打穿草丛，草丛消失；
- 按 T 全部恢复（车压着的墙或栅栏这次不恢复）；
- Mac 上 F1～F4 要按住 fn。

- [ ] **Step 2：按反馈调整**

每条反馈一个提交，改完截图确认，再请用户看。

- [ ] **Step 3：记录**

在 `docs/PROGRESS.md` 记下试玩结论，提交。合并由用户确认后再做（squash 合入 main，不推送远程）。
