# 阶段 2B：车型 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在中坦之外加上轻坦、重坦、坦克歼击车（TD）：按设计重新平衡的车型数据；TD 没有炮塔，炮左右各转 12°，超出时车体自动转向；三种新车的橙色玩具外观；试驾场里 F1～F4 随时换车、`--tank` 指定开局的车、面板上方显示车名；6 辆靶车改为四种车混放。

**Architecture:** 仿真层只加数据和两处规则：`TankSpec.GunArc` 表示炮相对车体能转的最大角度（有炮塔的车填 π，即不限），`TankMotion.AimGun` 把 TD 的炮夹在这个范围内，`TankMotion.TraverseHull` 在没按转向时替 TD 转车体；TD 的第二个装甲盒是战斗室，挂在车体原点上，不随炮转动；`Simulation.ChangeSpec` 原地换车。客户端沿用“每种车一份 `TankLook`”的结构：重坦的圆角方块炮塔靠给车床旋转体（`Lathe`）加一个“方度”参数（截面是超椭圆，`Superellipse`）；TD 的外观没有炮塔圆顶，`TankModel` 在没有炮塔时只让炮盾和炮管转动、击毁时不抛炮塔。所有外观检查改为对每种车都做。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、Godot 4.7.2 .NET 版（Forward+，macOS 上走 Metal）

**设计依据：** `docs/superpowers/specs/2026-10-15-stage2b-vehicles-design.md`（全文）；上级文档 `docs/superpowers/specs/2026-10-13-third-person-tank-design.md` §4.4。

---
## 写计划时已核实、与设计的差异

写这份计划时，先在临时 worktree（`/Users/murphy/Desktop/dev/BattleCity-stage2b-draft`，分支 `draft/stage2b-vehicles`，从 `feat/stage2b-vehicles` 的 `233aed0` 拉出）里按任务顺序把下文全部实现了一遍，一个任务一个提交。下文的代码和补丁由脚本从这些提交直接导出，没有手抄。实测结果：

- 每个提交单独检出后：客户端编译 0 警告 0 错误，5 个测试项目全过。Sim 测试数依次是 281、291、296、296、302、307、313、315（Task 1～8），Art 依次是 199、199、199、202、237、278、297、330；Core 62、Net 50、Update 42 不变。
- 截图逐张看过：车名；三种新车的近景和侧面（轻坦矮小、细长炮管、3 对负重轮；重坦方块炮塔、粗炮管、5 对负重轮、宽履带；TD 楔形车身、圆球炮盾、长炮管、4 对负重轮）；轻坦打重坦炮塔正面“未击穿”；重坦击毁中坦、炮塔飞起；TD 被击毁后焦黑冒烟、没有炮塔飞起；TD 自动转向前后；靶车四种车混放。
- 换车的流程用一段临时代码（运行中依次调用 `ChangeTank`，没有提交）验证过：模型、面板、血量弹药都跟着换，没有报错。
- 草稿经过一轮代码审查：没有严重问题。按审查意见改了：换车时把输入里选的弹种复位为穿甲弹（Task 4）；圆顶平滑度的检查改为对实际扫出的方块网格做，另加用网格顶点验证炮塔落地的测试（Task 6）；补上“炮口在车体之外”的检查（Task 7）；`nopen-heavy` 的停车点从 45 m 改为 52 m，并加了“每个演示配每种车都停在空地上”的测试（Task 8）；两处过时的注释。设计文档里 `HasTurret` 的描述写反了，已改正。审查还实测了：TD 每 tick 多算一次姿态不分配内存；TD 的炮在左右 12°、压到最低、车体点头时，炮管离前装甲还有 2.4 cm。

与设计文字不同、但有意为之的地方（执行时不要“改回去”）：

- **TD 的炮耳和战斗室**：设计 §2.3 给的起点是 `TurretOffset` 1.0 m、炮耳高 1.75 m、炮长 4.4 m，战斗室装甲盒 3.0 × 3.6 m、挂在 `TurretOffset` 处。实际做外观时，炮盾要嵌在斜前装甲上、压到最低的炮管不能插进前装甲，所以炮耳定在前装甲上：`TurretOffset` 2.8 m、炮耳高 1.85 m、炮长 4.1 m。战斗室是车体的一部分，装甲盒改为以车体原点为中心（3.0 × 5.8 m，离地 1.25～2.2 m），盖住整个上部车体；`TurretOffset` 只决定炮耳的位置（`TankSpec.TurretArmor` 的注释写明了）。
- **重坦炮塔盒 3.1 m 宽**（设计起点 3.4 m）：炮盾离炮塔中心太远时，炮抬到最高时看到的炮口和仿真里的炮口差得超过 0.5 m（外观测试的上限），所以炮塔半径取 1.55 m。
- **轻坦 `TurretOffset` 为 0**（设计起点 0.2 m），炮塔在车体正中。
- **圆角方块炮塔的做法**：设计只说“圆角方块”。做法是给车床旋转体 `Lathe` 加“方度”参数，截面是超椭圆（`Superellipse`），重坦取 4；方度为 2 时网格和原来逐位相同。
- **`TankLook.TurretProfile` 为空表示没有炮塔**（`HasTurret`），TD 用它；外观测试检查它和 `TankSpec.HasTurret` 一致。
- **平衡测试取整后比较**：设计 §2.1 写“每秒伤害 52～58”，TD 实为 380 / 6.5 = 58.46，表里写作 58。测试比较取整后的值。
- **外观测试放宽一处**：“车身要厚”原来是中坦专用的 0.7～0.9 m，改成对每种车都要求不小于 0.6 m（轻坦更矮，TD 的战斗室更高）。中坦圆顶的尺寸单独留一个测试。
- **F1～F4 按 `TankCatalog.All` 的顺序**（轻、中、重、TD），不是按名字写死。
- **截图用的新演示和机位**：`nopen-heavy`、`kill-td`、`turn` 三个演示，`target` 机位对准演示打的那辆靶车，`RangeDemo.Bearing`（镜头朝向靶车，`turn` 时车体可以偏开）。
- **`LaunchOptions.cs` 和 `RangeDemo.cs` 链接进 `Tank.Art.Tests`**（都不依赖 Godot），测试项目因此还引用 `Tank.Net`（`LaunchOptions` 用到旧游戏的 `GameMode` 和默认端口）。

## 留给以后的问题（不在本阶段处理）

- 坦克之间按重量分配推开的距离（总体设计 §4.2），等有会动的敌人时再做。
- 视野和隐蔽数值留给阶段 3。
- TD 的战斗室装甲盒是一个长方盒，斜前装甲最前面一段（车头上方）盒子比外观高出最多约 0.4 m，打那里会算命中战斗室。和 2A 的车体盒一样，属于“装甲盒只和外观大致对齐”。
- TD 的炮管伸出车头约 3.4 m，进狭窄地方时容易顶到石头（顶到时炮弹在炮口处爆炸，2A 已处理）。试玩时请用户感受。

---
## 执行前须知

- **工作目录**：直接在现有 worktree 里执行，不要新建：`/Users/murphy/Desktop/dev/BattleCity-stage2b`（分支 `feat/stage2b-vehicles`）。下文所有路径都相对于它。
- 每个 shell 先执行下面这段。`dotnet` 不在默认 PATH 里；`godot_run` 保证 Godot 无论如何都会被强制结束（曾有一次 `--quit-after` 卡住 15 小时）。

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage2b
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
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`。`Tank.Sim` 和测试项目里任何警告都会让构建失败。`Tank.Art.Tests` 会编译客户端里不依赖 Godot 的源码（网格代码、外观数据、`TurretToss`，本计划再加上 `LaunchOptions.cs`），它们同样不能有警告、不能引用 Godot。客户端项目关掉了该选项，但也要保持 0 警告。
- 新增 `src/Tank.Client` 下的脚本后，提交前运行 `importall`，把生成的 `.cs.uid` 一起提交。下文的 `git add` 按目录添加，会带上它们。
- **不要改**：旧 Battle City 代码（`src/Tank.Core`、`src/Tank.Net`、`src/Tank.Server`，客户端的 `scripts/Bootstrap/` 中除 `LaunchOptions.cs` 以外的文件、`scripts/UI/`、`scripts/View/`、`scripts/Controls/`）和 `project.godot`。
- **坐标约定**（同前几个阶段）：Y 向上，−Z 为前方；偏航 0 朝 −Z，正值向左转；`InputCmd.Steer` 为 +1 时向右转；车体空间原点在接地中心。`TankState.Position` 是二维 `(x, z)`。
- **测试项目与数量**：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。开始前是 Core 62、Sim 272、Net 50、Update 42、Art 199。每个任务末尾写明了做完之后 Sim 和 Art 的测试数；对不上就说明漏了或多了测试。
- 提交信息用约定式前缀（feat/fix/test/docs/chore）。每完成一批，在 `docs/PROGRESS.md` 的“2B 节点记录”里追加一行（任务、提交号、测试数）。
- 分支最后用 squash merge 并入 `main`，不推送远程，除非用户要求。
## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Vehicles/TankSpec.cs` | 修改 | 新增 `GunArc`（炮相对车体能转的最大角度）、`FullTraverse`、`HasTurret`；没有炮塔时 `TurretArmor` 是挂在车体原点的战斗室 |
| `Vehicles/TankCatalog.cs` | 修改 | 新增 `Light`、`Heavy`、`Td`，`All` 按轻、中、重、TD 排列；`ById` 按名字查找 |
| `Vehicles/TankMotion.cs` | 修改 | `AimGun` 把 TD 的炮夹在 ±`GunArc` 内；新增 `TraverseHull`：没按转向且瞄准方向超出范围时替 TD 转车体 |
| `Vehicles/TankPose.cs` | 修改 | `TurretBox` 对没有炮塔的车：盒子挂在车体原点、不随炮转 |
| `Simulation.cs` | 修改 | 每 tick 先经过 `TraverseHull`；新增 `ChangeSpec` 原地换车，`AddTank` 和它共用 `Fresh` |
| `Maps/TargetSpot.cs`、`Maps/SandboxMap.cs` | 修改 | 靶车位置带上车型，6 辆靶车四种车混放 |

**客户端 `src/Tank.Client`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `scripts/Bootstrap/LaunchOptions.cs` | 修改 | `--tank`（light、medium、heavy、td），写错时给出警告 |
| `scripts/Sandbox/SandboxInput.cs` | 修改 | F1～F4 换车 |
| `scripts/Sandbox/SandboxMain.cs` | 修改 | 开局按 `--tank` 选车；`ChangeTank` 换车并重建模型和履带扬尘 |
| `scripts/Sandbox/SandboxWorld.cs` | 修改 | 草地下压的范围跟着车的大小变 |
| `scripts/Sandbox/SandboxHud.cs`、`scripts/Overlay/StatusPanel.cs` | 修改 | 面板上方显示车名 |
| `scripts/Art/Mesh/Superellipse.cs` | 新建 | 超椭圆的极坐标半径和法线 |
| `scripts/Art/Mesh/Lathe.cs` | 修改 | “方度”参数：截面按超椭圆扫出圆角方块 |
| `scripts/Vehicles/TankLook.cs` | 修改 | `TurretSquareness`；`TurretProfile` 为空表示没有炮塔（`HasTurret`） |
| `scripts/Vehicles/TankLooks.cs` | 修改 | 轻坦、重坦、TD 三份外观 |
| `scripts/Vehicles/TankParts.cs` | 修改 | 炮塔按方度扫出 |
| `scripts/Vehicles/TurretToss.cs` | 修改 | 落地时按方块炮塔真实的底边贴地，离车身的距离按方块的角算 |
| `scripts/Vehicles/TankModel.cs` | 修改 | 没有炮塔时不建炮塔外壳、击毁时不抛炮塔，黑烟从战斗室顶上冒出 |
| `scripts/Sandbox/TargetRange.cs` | 修改 | 靶车按位置上的车型建 |
| `scripts/Sandbox/RangeDemo.cs`、`SandboxAutomation.cs`、`scripts/Cameras/CameraPresets.cs` | 修改 | 截图用的演示 `nopen-heavy`、`kill-td`、`turn`；`target` 机位对准演示打的那辆靶车 |

**测试**：`tests/Tank.Sim.Tests` 新增 `CasemateTests`、`ChangeSpecTests`，改 `CombatSpecTests`、`TankAimTests`、`TankPoseTests`、`TargetSpotTests`；`tests/Tank.Art.Tests` 新增 `LaunchOptionsTests`，改 `LatheTests`、`TankLookTests`（外观检查改为对每种车都做）、`TurretTossTests`（对每种有炮塔的车都做）和项目文件（链接 `LaunchOptions.cs`，引用 `Tank.Net`）。

---
## 任务一览与批次

| 批次 | 任务 | 做完后 Sim / Art 测试数 |
|---|---|---|
| A 仿真 | 1 炮的转角上限、按名字查车型；2 TD 车体自动转向；3 原地换车 | 281 / 199 → 291 / 199 → 296 / 199 |
| B 试驾场换车 | 4 `--tank`、F1～F4、车名 | 296 / 202 |
| C 新车型 | 5 轻坦；6 重坦；7 TD | 302 / 237 → 307 / 278 → 313 / 297 |
| D 靶车 | 8 靶车四种车混放、截图用的演示 | 315 / 330 |
| E 验收 | 9 截图与帧率；10 用户试玩与收尾 | — |

每批做完先过规格审查，再过代码质量审查，然后在 `docs/PROGRESS.md` 记一行。

---

### Task 1：炮的转角上限、按名字查车型

TD 的炮装在战斗室前板上，只能左右各转 12°。车型数据加一项 `GunArc` 表示这个范围，有炮塔的车填 `TankSpec.FullTraverse`（π，即不限），`HasTurret` 由它推出。`TankMotion.AimGun` 把没有炮塔的车的目标偏转夹在 ±`GunArc` 内；`TankPose.TurretBox` 对没有炮塔的车不再随炮转动（本任务还挂在 `TurretOffset` 处，Task 7 改为挂在车体原点）。另加 `TankCatalog.ById`，后面 `--tank` 和测试都用它。本任务还没有 TD，测试用“把中坦的炮装进战斗室”的变体（`Medium with { GunArc = 12° }`）。

**Files:**
- Modify: `src/Tank.Sim/Vehicles/TankSpec.cs`、`TankCatalog.cs`、`TankMotion.cs`、`TankPose.cs`
- Test: `tests/Tank.Sim.Tests/CombatSpecTests.cs`、`TankAimTests.cs`、`TankPoseTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Sim.Tests/CombatSpecTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/CombatSpecTests.cs b/tests/Tank.Sim.Tests/CombatSpecTests.cs
index 1448281..6226770 100644
--- a/tests/Tank.Sim.Tests/CombatSpecTests.cs
+++ b/tests/Tank.Sim.Tests/CombatSpecTests.cs
@@ -50,6 +50,28 @@ public sealed class CombatSpecTests
         Assert.True(s.TrunnionHeight > s.TurretArmor.Bottom && s.TrunnionHeight < s.TurretArmor.Top, $"{id}: gun pivot outside the turret box");
     }
 
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void GunArc_IsAtMostHalfATurn(string id)
+    {
+        TankSpec s = Get(id);
+        Assert.True(s.GunArc > 0 && s.GunArc <= TankSpec.FullTraverse, $"{id}: gun arc {s.GunArc}");
+    }
+
+    [Fact]
+    public void ById_FindsEveryTank()
+    {
+        foreach (TankSpec spec in TankCatalog.All) Assert.Same(spec, TankCatalog.ById(spec.Id));
+    }
+
+    [Fact]
+    public void ById_UnknownName_ListsTheKnownOnes()
+    {
+        var error = Assert.Throws<KeyNotFoundException>(() => TankCatalog.ById("tiger"));
+        Assert.Contains("'tiger'", error.Message);
+        Assert.Contains("medium", error.Message);
+    }
+
     [Fact]
     public void Medium_MatchesTheDesignTable()
     {
@@ -63,6 +85,7 @@ public sealed class CombatSpecTests
         Assert.Equal(0.36f, m.Dispersion);
         Assert.Equal(2.1f, m.AimTime);
         Assert.Equal(new BloomFactors(2.0f, 1.5f, 1.0f, 3.0f), m.Bloom);
+        Assert.True(m.HasTurret);
     }
 
     [Fact]
PATCH
```

修改 `tests/Tank.Sim.Tests/TankAimTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TankAimTests.cs b/tests/Tank.Sim.Tests/TankAimTests.cs
index c140e33..86c0353 100644
--- a/tests/Tank.Sim.Tests/TankAimTests.cs
+++ b/tests/Tank.Sim.Tests/TankAimTests.cs
@@ -8,6 +8,9 @@ public sealed class TankAimTests
 {
     private static readonly TankSpec Spec = TankCatalog.Medium;
 
+    /// <summary>The medium tank's gun in a casemate, 12° each way like the tank destroyer's.</summary>
+    private static readonly TankSpec Casemate = Spec with { Id = "casemate", GunArc = Angles.Deg(12) };
+
     private static void Aim(ref TankState t, InputCmd cmd, int ticks)
     {
         var terrain = TestWorld.Flat();
@@ -25,6 +28,33 @@ public sealed class TankAimTests
         Assert.Equal(MathF.PI / 2, t.TurretYaw, 1e-4f);
     }
 
+    [Fact]
+    public void Turret_TurnsAllTheWayRound()
+    {
+        var t = TestWorld.Tank(100, 100);
+        Aim(ref t, TestWorld.Cmd(aimYaw: 3f), 300);
+        Assert.Equal(3f, t.TurretYaw, 1e-4f);
+    }
+
+    [Theory]
+    [InlineData(1.2f)]
+    [InlineData(-1.2f)]
+    [InlineData(3f)]
+    public void CasemateGun_StopsAtTheEdgeOfItsArc(float aimYaw)
+    {
+        var t = TestWorld.Tank(100, 100) with { Spec = Casemate };
+        Aim(ref t, TestWorld.Cmd(aimYaw: aimYaw), 300);
+        Assert.Equal(MathF.CopySign(Casemate.GunArc, aimYaw), t.TurretYaw, 1e-4f);
+    }
+
+    [Fact]
+    public void CasemateGun_FollowsAnAimInsideItsArc()
+    {
+        var t = TestWorld.Tank(100, 100) with { Spec = Casemate };
+        Aim(ref t, TestWorld.Cmd(aimYaw: -0.1f), 300);
+        Assert.Equal(-0.1f, t.TurretYaw, 1e-4f);
+    }
+
     [Fact]
     public void Turret_HoldsWorldAim_WhenHullIsTurned()
     {
PATCH
```

修改 `tests/Tank.Sim.Tests/TankPoseTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TankPoseTests.cs b/tests/Tank.Sim.Tests/TankPoseTests.cs
index 6cfe8db..1b21cf8 100644
--- a/tests/Tank.Sim.Tests/TankPoseTests.cs
+++ b/tests/Tank.Sim.Tests/TankPoseTests.cs
@@ -1,4 +1,5 @@
 using System.Numerics;
+using Tank.Sim.Geometry;
 using Tank.Sim.Terrain;
 using Tank.Sim.Vehicles;
 
@@ -20,11 +21,20 @@ public sealed class TankPoseTests
             {
                 s.HullLength, s.HullWidth, s.MaxForwardSpeed, s.MaxReverseSpeed, s.Acceleration, s.Braking, s.HullTraverse,
                 s.TurretTraverse, s.GunPitchSpeed, s.GunDepression, s.GunElevation, s.MaxClimb, s.TrunnionHeight, s.GunLength,
-                s.ReloadTime, s.Caliber, s.Dispersion, s.AimTime,
+                s.ReloadTime, s.Caliber, s.Dispersion, s.AimTime, s.GunArc,
             }, v => Assert.True(v > 0, $"{s.Id} has a non-positive parameter"));
         }
     }
 
+    [Fact]
+    public void CasemateBox_StaysFixedToTheHull_WhileTheGunTurns()
+    {
+        TankSpec casemate = Spec with { Id = "casemate", GunArc = Angles.Deg(12) };
+        TankPose pose = PoseOn(TestWorld.Flat(), yaw: 0.3f);
+        Assert.Equal(pose.TurretBox(casemate, 0f), pose.TurretBox(casemate, 0.2f));
+        Assert.NotEqual(pose.TurretBox(Spec, 0f), pose.TurretBox(Spec, 0.2f));
+    }
+
     [Fact]
     public void FlatGround_SitsOnSurface_FacingYaw()
     {
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`TankCatalog.ById`、`TankSpec.FullTraverse`、`GunArc`、`HasTurret` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index f2aba1c..68d5422 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -20,6 +20,7 @@ public static class TankCatalog
         Braking = 9f,
         HullTraverse = Angles.Deg(42),
         TurretTraverse = Angles.Deg(40),
+        GunArc = TankSpec.FullTraverse,
         GunPitchSpeed = Angles.Deg(30),
         GunDepression = Angles.Deg(7),
         GunElevation = Angles.Deg(20),
@@ -40,4 +41,13 @@ public static class TankCatalog
     };
 
     public static IReadOnlyList<TankSpec> All { get; } = new[] { Medium };
+
+    /// <summary>The spec named <paramref name="id"/>.</summary>
+    /// <exception cref="KeyNotFoundException">No such tank; the message lists the known names.</exception>
+    public static TankSpec ById(string id)
+    {
+        foreach (TankSpec spec in All)
+            if (spec.Id == id) return spec;
+        throw new KeyNotFoundException($"No tank '{id}'. Known: {string.Join(", ", All.Select(s => s.Id))}.");
+    }
 }
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankMotion.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankMotion.cs b/src/Tank.Sim/Vehicles/TankMotion.cs
index 1ff0d0f..00f6c3c 100644
--- a/src/Tank.Sim/Vehicles/TankMotion.cs
+++ b/src/Tank.Sim/Vehicles/TankMotion.cs
@@ -53,12 +53,16 @@ public static class TankMotion
         if (intendedLength > 0 && Vector2.Distance(t.Position, start) < intendedLength * StallRatio) t.Speed = 0f;
     }
 
-    /// <summary>Turns turret and gun toward the input's world aim direction at their rated speeds, within gun limits.</summary>
+    /// <summary>
+    /// Turns turret and gun toward the input's world aim direction at their rated speeds, within gun limits; a casemate
+    /// gun stops at the edge of its arc.
+    /// </summary>
     public static void AimGun(ref TankState t, in InputCmd input, HeightField terrain)
     {
         TankSpec spec = t.Spec;
         TankPose pose = TankPose.Compute(t, terrain);
         var (wantYaw, wantPitch) = pose.ToHullAngles(Angles.Direction(input.AimYaw, input.AimPitch), t.TurretYaw);
+        if (!spec.HasTurret) wantYaw = System.Math.Clamp(wantYaw, -spec.GunArc, spec.GunArc);
         t.TurretYaw = Angles.MoveTowards(t.TurretYaw, wantYaw, spec.TurretTraverse * SimConstants.Dt);
         float pitch = System.Math.Clamp(wantPitch, -spec.GunDepression, spec.GunElevation);
         t.GunPitch = Scalar.MoveTowards(t.GunPitch, pitch, spec.GunPitchSpeed * SimConstants.Dt);
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankPose.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankPose.cs b/src/Tank.Sim/Vehicles/TankPose.cs
index dc6fdb8..191fbf6 100644
--- a/src/Tank.Sim/Vehicles/TankPose.cs
+++ b/src/Tank.Sim/Vehicles/TankPose.cs
@@ -45,11 +45,15 @@ public readonly record struct TankPose(Vector3 Position, Quaternion Rotation)
     public OrientedBox HullBox(TankSpec spec) =>
         new(Position + Vector3.Transform(spec.HullArmor.LocalCenter, Rotation), spec.HullArmor.HalfExtents, Rotation);
 
-    /// <summary>The turret's armour box in world space: centred over the turret ring, tilted with the hull, turned by <paramref name="turretYaw"/>.</summary>
+    /// <summary>
+    /// The turret's armour box in world space: centred over the turret ring, tilted with the hull, turned by
+    /// <paramref name="turretYaw"/>. A casemate doesn't turn with its gun.
+    /// </summary>
     public OrientedBox TurretBox(TankSpec spec, float turretYaw)
     {
         Vector3 ring = new(0, 0, -spec.TurretOffset);
-        Quaternion rotation = Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, turretYaw);
+        float yaw = spec.HasTurret ? turretYaw : 0f;
+        Quaternion rotation = Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
         return new OrientedBox(Position + Vector3.Transform(ring + spec.TurretArmor.LocalCenter, Rotation), spec.TurretArmor.HalfExtents, rotation);
     }
 
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankSpec.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankSpec.cs b/src/Tank.Sim/Vehicles/TankSpec.cs
index c11b335..0ea51ac 100644
--- a/src/Tank.Sim/Vehicles/TankSpec.cs
+++ b/src/Tank.Sim/Vehicles/TankSpec.cs
@@ -15,6 +15,18 @@ public sealed record TankSpec
     public required float Braking { get; init; }
     public required float HullTraverse { get; init; }
     public required float TurretTraverse { get; init; }
+
+    /// <summary>A <see cref="GunArc"/> this wide is a full turret: the gun turns all the way round.</summary>
+    public const float FullTraverse = MathF.PI;
+
+    /// <summary>
+    /// How far the gun turns left or right of the hull's heading. <see cref="FullTraverse"/> for a turret; a casemate gun
+    /// (the tank destroyer) has a narrow arc and the hull turns for the rest (see <c>TankMotion.TraverseHull</c>).
+    /// </summary>
+    public required float GunArc { get; init; }
+
+    /// <summary>The gun sits in a turret (else in a casemate built into the hull).</summary>
+    public bool HasTurret => GunArc >= FullTraverse;
     public required float GunPitchSpeed { get; init; }
 
     /// <summary>How far the gun can point below the hull plane (positive number).</summary>
@@ -40,7 +52,10 @@ public sealed record TankSpec
     /// <summary>Hull hit box, anchored at the hull origin.</summary>
     public required ArmorBox HullArmor { get; init; }
 
-    /// <summary>Turret hit box, anchored at the turret ring (<see cref="TurretOffset"/> ahead of the hull origin); turns with the turret.</summary>
+    /// <summary>
+    /// Turret hit box, anchored at the turret ring (<see cref="TurretOffset"/> ahead of the hull origin); turns with the
+    /// turret. Without a turret it is the casemate, fixed to the hull.
+    /// </summary>
     public required ArmorBox TurretArmor { get; init; }
 
     public required ShellSpec Ap { get; init; }
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 281（Art 仍为 199）。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): gun arc for casemate guns, catalog lookup by id"
```

---

### Task 2：TD 的车体自动转向

设计 §3.2：TD 没被击毁、这一 tick 没有转向输入、瞄准方向相对车体超出 ±`GunArc` 时，这一 tick 按“朝瞄准方向转向”处理，和玩家按 A/D 完全一样（转速、行进中打 0.75 折、原地转向的碰撞规则、散布的车体转向项）；瞄准方向回到范围内就停；按着 A/D 时听玩家的。做法是 `TankMotion.TraverseHull` 在输入进入 `Drive` 之前改写 `Steer`。注意符号：偏航正值向左，`Steer` 为 +1 时向右转，所以瞄准在左边（偏转为正）时 `Steer` 取 −1。

**Files:**
- Modify: `src/Tank.Sim/Vehicles/TankMotion.cs`、`src/Tank.Sim/Simulation.cs`
- Test: 新建 `tests/Tank.Sim.Tests/CasemateTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/CasemateTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>A casemate gun (the tank destroyer's) turns only within its arc; outside it the hull turns for it.</summary>
public sealed class CasemateTests
{
    /// <summary>The medium tank's gun in a casemate, 12° each way like the tank destroyer's.</summary>
    private static readonly TankSpec Casemate = TankCatalog.Medium with { Id = "casemate", GunArc = Angles.Deg(12) };

    private static readonly float TurnPerTick = Casemate.HullTraverse * SimConstants.Dt;

    private static Simulation NewSim(TankSpec spec, out int id)
    {
        var sim = new Simulation(TestWorld.FlatField());
        id = sim.AddTank(spec, new Vector2(100, 100), 0f);
        return sim;
    }

    private static void Run(Simulation sim, InputCmd cmd, int ticks)
    {
        var inputs = new[] { cmd };
        for (int i = 0; i < ticks; i++) sim.Step(inputs);
    }

    [Theory]
    [InlineData(1.0f, -1)]   // aim to the left (+yaw): steer left
    [InlineData(-1.0f, 1)]
    [InlineData(3.0f, -1)]
    public void TraverseHull_SteersTowardsAnAimOutsideTheArc(float aimYaw, int steer)
    {
        TankState t = TestWorld.Tank(100, 100) with { Spec = Casemate };
        InputCmd cmd = TankMotion.TraverseHull(t, TestWorld.Cmd(aimYaw: aimYaw), TestWorld.Flat());
        Assert.Equal(steer, cmd.Steer);
    }

    [Fact]
    public void TraverseHull_LeavesAnAimInsideTheArcAlone()
    {
        TankState t = TestWorld.Tank(100, 100) with { Spec = Casemate };
        InputCmd cmd = TestWorld.Cmd(aimYaw: 0.15f);
        Assert.Equal(cmd, TankMotion.TraverseHull(t, cmd, TestWorld.Flat()));
    }

    [Fact]
    public void Hull_TurnsTowardsTheAim_AtItsTraverseRate()
    {
        var sim = NewSim(Casemate, out _);
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 1);
        Assert.Equal(TurnPerTick, sim.Tanks[0].Yaw, 1e-4f);
    }

    [Fact]
    public void Hull_StopsOnceTheGunCanReach_AndTheGunIsOnTarget()
    {
        var sim = NewSim(Casemate, out _);
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 90);
        float yaw = sim.Tanks[0].Yaw;
        Assert.InRange(1.0f - yaw, Casemate.GunArc - TurnPerTick, Casemate.GunArc);   // stopped at the arc's edge
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 30);
        Assert.Equal(yaw, sim.Tanks[0].Yaw);
        Assert.Equal(1.0f - yaw, sim.Tanks[0].TurretYaw, 1e-4f);
    }

    [Fact]
    public void HandSteering_Wins()
    {
        var sim = NewSim(Casemate, out _);
        Run(sim, TestWorld.Cmd(steer: 1, aimYaw: 1.0f), 1);   // steering right, aiming left
        Assert.Equal(-TurnPerTick, sim.Tanks[0].Yaw, 1e-4f);
    }

    [Fact]
    public void TurretTank_HullNeverTurnsByItself()
    {
        var sim = NewSim(TankCatalog.Medium, out _);
        Run(sim, TestWorld.Cmd(aimYaw: 2.0f), 30);
        Assert.Equal(0f, sim.Tanks[0].Yaw);
    }

    [Fact]
    public void Wreck_DoesNotTurn()
    {
        var sim = NewSim(Casemate, out int id);
        ref TankState tank = ref sim.TankRef(id);
        tank.Health = 0;
        tank.IsDestroyed = true;
        Run(sim, TestWorld.Cmd(aimYaw: 1.0f), 30);
        Assert.Equal(0f, sim.Tanks[0].Yaw);
    }

    [Fact]
    public void AutoTurn_OpensTheAimCircleLikeSteering()
    {
        var auto = NewSim(Casemate, out _);
        var hand = NewSim(Casemate, out _);
        Run(auto, TestWorld.Cmd(aimYaw: 1.0f), 10);
        Run(hand, TestWorld.Cmd(steer: -1, aimYaw: 1.0f), 10);
        Assert.True(auto.Tanks[0].Bloom > 0f);
        Assert.Equal(hand.Tanks[0].Bloom, auto.Tanks[0].Bloom, 1e-5f);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`TankMotion.TraverseHull` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index c405098..ba94cce 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -8,11 +8,12 @@ using Tank.Sim.Vehicles;
 namespace Tank.Sim;
 
 /// <summary>
-/// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted; each tank drives,
-/// aims, updates its aim circle and fires (in slot order) against that snapshot, touching no other tank, so its turn
-/// doesn't depend on its slot (except that when the shell pool is full lower slots get to fire first, and shell ids
-/// follow slot order). Then the shell phase: shots that burst in a blocked barrel go off, then every shell in flight
-/// flies and resolves its hits, each group in firing order (shots of the same tick in slot order).
+/// Advances the battle by one fixed 30Hz tick. Order inside a tick: tank footprints are snapshotted; each tank drives
+/// (a casemate gun's hull turning by itself towards an aim outside the arc), aims, updates its aim circle and fires (in
+/// slot order) against that snapshot, touching no other tank, so its turn doesn't depend on its slot (except that when
+/// the shell pool is full lower slots get to fire first, and shell ids follow slot order). Then the shell phase: shots
+/// that burst in a blocked barrel go off, then every shell in flight flies and resolves its hits, each group in firing
+/// order (shots of the same tick in slot order).
 /// </summary>
 public sealed partial class Simulation
 {
@@ -80,7 +81,7 @@ public sealed partial class Simulation
         {
             ref TankState tank = ref _tanks[i];
             if (tank.IsDestroyed) continue;   // wrecks don't drive, aim or fire, but still block (see SnapshotFootprints)
-            InputCmd input = Sanitize(inputs[i], tank);
+            InputCmd input = TankMotion.TraverseHull(tank, Sanitize(inputs[i], tank), Field.Terrain);
             float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
             TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i));
             TankMotion.AimGun(ref tank, input, Field.Terrain);
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankMotion.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankMotion.cs b/src/Tank.Sim/Vehicles/TankMotion.cs
index 00f6c3c..7994c35 100644
--- a/src/Tank.Sim/Vehicles/TankMotion.cs
+++ b/src/Tank.Sim/Vehicles/TankMotion.cs
@@ -53,6 +53,21 @@ public static class TankMotion
         if (intendedLength > 0 && Vector2.Distance(t.Position, start) < intendedLength * StallRatio) t.Speed = 0f;
     }
 
+    /// <summary>
+    /// A casemate gun can't follow an aim outside its arc, so with no steering input the hull turns towards it, exactly
+    /// as if steered by hand, until the aim is back inside the arc. Steering by hand always wins.
+    /// </summary>
+    /// <returns><paramref name="input"/>, with <c>Steer</c> set when the hull should turn by itself.</returns>
+    public static InputCmd TraverseHull(in TankState t, in InputCmd input, HeightField terrain)
+    {
+        TankSpec spec = t.Spec;
+        if (spec.HasTurret || input.Steer != 0) return input;
+        TankPose pose = TankPose.Compute(t, terrain);
+        float wantYaw = pose.ToHullAngles(Angles.Direction(input.AimYaw, input.AimPitch), t.TurretYaw).TurretYaw;
+        if (MathF.Abs(wantYaw) <= spec.GunArc) return input;
+        return input with { Steer = (sbyte)(wantYaw > 0 ? -1 : 1) };   // +yaw is to the left; Steer +1 turns right
+    }
+
     /// <summary>
     /// Turns turret and gun toward the input's world aim direction at their rated speeds, within gun limits; a casemate
     /// gun stops at the edge of its arc.
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 291（Art 仍为 199）。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): a casemate gun's hull turns by itself towards an aim outside its arc"
```

---

### Task 3：原地换车

设计 §3.3：`Simulation.ChangeSpec(tankId, spec)` 保留编号、位置、朝向和已开炮数（`ShotCount` 参与随机数种子，保留它就不会有两发炮弹种子相同），其余状态和 `AddTank` 时一样重新开始（残骸也会恢复）。已经飞出去的炮弹带着自己的弹种数据和口径，不受影响。不发事件。`AddTank` 和 `ChangeSpec` 共用一个 `Fresh` 来建初始状态。

**Files:**
- Modify: `src/Tank.Sim/Simulation.cs`
- Test: 新建 `tests/Tank.Sim.Tests/ChangeSpecTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/ChangeSpecTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class ChangeSpecTests
{
    private static readonly TankSpec Medium = TankCatalog.Medium;

    /// <summary>A different vehicle: more health, a smaller AP rack, a harder-hitting gun.</summary>
    private static readonly TankSpec Big = Medium with
    {
        Id = "big",
        MaxHealth = 1400,
        Ap = Medium.Ap with { Damage = 330, Capacity = 25 },
        He = Medium.He with { Capacity = 12 },
    };

    /// <summary>A medium tank that has driven, turned its gun, fired twice (once HE), taken a hit and is reloading.</summary>
    private static Simulation Used(out int id)
    {
        var sim = new Simulation(TestWorld.FlatField());
        id = sim.AddTank(Medium, new Vector2(100, 100), 0.4f);
        var inputs = new[] { TestWorld.Cmd(throttle: 1, aimYaw: 1.2f, aimPitch: 0.1f, fire: true) };
        for (int i = 0; i < 150; i++) sim.Step(inputs);
        inputs[0] = inputs[0] with { Ammo = AmmoType.HE };
        for (int i = 0; i < 150; i++) sim.Step(inputs);
        sim.TankRef(id).Health = 300;
        return sim;
    }

    [Fact]
    public void KeepsIdPlaceHeadingAndShotCount()
    {
        var sim = Used(out int id);
        TankState before = sim.Tanks[0];
        Assert.True(before.ShotCount >= 2);
        sim.ChangeSpec(id, Big);
        TankState after = sim.Tanks[0];
        Assert.Same(Big, after.Spec);
        Assert.Equal(id, after.Id);
        Assert.Equal(before.Position, after.Position);
        Assert.Equal(before.Yaw, after.Yaw);
        Assert.Equal(before.ShotCount, after.ShotCount);
    }

    [Fact]
    public void StartsEverythingElseFresh()
    {
        var sim = Used(out int id);
        sim.ChangeSpec(id, Big);
        TankState t = sim.Tanks[0];
        Assert.Equal(1400, t.Health);
        Assert.False(t.IsDestroyed);
        Assert.Equal(AmmoType.AP, t.Ammo);
        Assert.Equal(25, t.ApLeft);
        Assert.Equal(12, t.HeLeft);
        Assert.Equal(0f, t.Reload);
        Assert.Equal(0f, t.Bloom);
        Assert.Equal(0f, t.Speed);
        Assert.Equal(0f, t.TurretYaw);
        Assert.Equal(0f, t.GunPitch);
    }

    [Fact]
    public void BringsBackAWreck()
    {
        var sim = Used(out int id);
        ref TankState tank = ref sim.TankRef(id);
        tank.Health = 0;
        tank.IsDestroyed = true;
        sim.ChangeSpec(id, Medium);
        Assert.False(sim.Tanks[0].IsDestroyed);
        Assert.Equal(900, sim.Tanks[0].Health);
    }

    [Fact]
    public void ShellsInFlight_KeepTheirOwnRound()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(Medium, new Vector2(100, 100), 0f);
        sim.Step(new[] { TestWorld.Cmd(aimPitch: 0.2f, fire: true) });
        sim.ChangeSpec(id, Big);
        sim.Step(new[] { TestWorld.Cmd() });
        Shell shell = Assert.Single(sim.Shells.ToArray());
        Assert.Equal(Medium.Ap, shell.Round);
        Assert.Equal(Medium.Caliber, shell.Caliber);
    }

    [Fact]
    public void UnknownTank_Throws()
    {
        var sim = new Simulation(TestWorld.FlatField());
        Assert.Throws<ArgumentException>(() => sim.ChangeSpec(7, Big));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Simulation.ChangeSpec` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index ba94cce..2fd018c 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -54,19 +54,33 @@ public sealed partial class Simulation
     {
         if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
         int id = _nextId++;
-        _tanks[TankCount++] = new TankState
-        {
-            Id = id,
-            Spec = spec,
-            Position = Field.ClampToPlayable(position),
-            Yaw = Angles.Wrap(yaw),
-            Health = spec.MaxHealth,
-            ApLeft = spec.Ap.Capacity,
-            HeLeft = spec.He.Capacity,
-        };
+        _tanks[TankCount++] = Fresh(id, spec, position, yaw);
         return id;
     }
 
+    /// <summary>
+    /// Makes a tank another kind of vehicle where it stands (sandbox only for now). Its id, place, heading and shot count
+    /// stay (the count seeds its shots, so no two shells ever share a seed); everything else starts as on
+    /// <see cref="AddTank"/>, a wreck included. Shells already in flight keep their own round. No event: the caller knows.
+    /// </summary>
+    public void ChangeSpec(int tankId, TankSpec spec)
+    {
+        ref TankState tank = ref TankRef(tankId);
+        tank = Fresh(tank.Id, spec, tank.Position, tank.Yaw) with { ShotCount = tank.ShotCount };
+    }
+
+    /// <summary>A tank at rest with full health and ammunition, AP loaded.</summary>
+    private TankState Fresh(int id, TankSpec spec, Vector2 position, float yaw) => new()
+    {
+        Id = id,
+        Spec = spec,
+        Position = Field.ClampToPlayable(position),
+        Yaw = Angles.Wrap(yaw),
+        Health = spec.MaxHealth,
+        ApLeft = spec.Ap.Capacity,
+        HeLeft = spec.He.Capacity,
+    };
+
     /// <param name="inputs">One command per tank, in <see cref="Tanks"/> order.</param>
     public void Step(ReadOnlySpan<InputCmd> inputs)
     {
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 296（Art 仍为 199）。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): change a tank into another vehicle where it stands"
```

---

### Task 4：`--tank`、F1～F4 换车、车名

设计 §4.3。`--tank` 只接受 `TankCatalog` 里有的名字，写错时记一条警告（由试驾场打印）并用默认的中坦。F1～F4 按 `TankCatalog.All` 的顺序换车，所以本任务时只有 F1 有效（只有中坦），Task 5～7 每加一种车就多一个键。换车在下一个仿真 tick 开始时生效：`ChangeSpec`；把输入里选中的弹种复位为穿甲弹（新车装的是穿甲弹，若还按着先前选的高爆弹，下一 tick 就会换弹并白白重新装填）；然后释放旧模型（履带扬尘是它的子节点，一起释放）、建新模型和扬尘；草地下压的范围在 `SandboxWorld.Update` 里发现车型变了就重算；车名由 `SandboxHud.Refresh` 每帧交给 `StatusPanel`，名字没变时不重画。为了测试 `--tank`，`Tank.Art.Tests` 链接 `LaunchOptions.cs`，并引用 `Tank.Net`（`LaunchOptions` 用到旧游戏的 `GameMode` 和默认端口）。

**Files:**
- Modify: `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`、`scripts/Sandbox/SandboxInput.cs`、`SandboxMain.cs`、`SandboxWorld.cs`、`SandboxHud.cs`、`scripts/Overlay/StatusPanel.cs`、`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`
- Test: 新建 `tests/Tank.Art.Tests/LaunchOptionsTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/LaunchOptionsTests.cs`：

```csharp
using Tank.Client.Bootstrap;

namespace Tank.Art.Tests;

public class LaunchOptionsTests
{
    [Fact]
    public void Tank_names_a_catalog_tank()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--tank", "medium" });
        Assert.Equal("medium", options.Tank);
        Assert.Empty(options.Warnings);
    }

    [Fact]
    public void No_tank_by_default()
    {
        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Tank);
    }

    [Fact]
    public void Unknown_tank_is_ignored_with_a_warning_naming_the_known_ones()
    {
        LaunchOptions options = LaunchOptions.Parse(new[] { "--tank", "tiger" });
        Assert.Null(options.Tank);
        string warning = Assert.Single(options.Warnings);
        Assert.Contains("'tiger'", warning);
        Assert.Contains("medium", warning);
    }
}
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index 93252a3..bf49a75 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -14,6 +14,8 @@
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Mesh\**\*.cs" LinkBase="Mesh" />
     <!-- The palette is engine-neutral too, so look data and vertex colours can use it. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
+    <!-- So are the launch options (they need Tank.Core and Tank.Net for the old game's switches). -->
+    <Compile Include="..\..\src\Tank.Client\scripts\Bootstrap\LaunchOptions.cs" LinkBase="Bootstrap" />
     <!-- Popup timing curves are engine-neutral too. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs" LinkBase="Overlay" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
@@ -21,6 +23,7 @@
   </ItemGroup>
   <ItemGroup>
     <ProjectReference Include="..\..\src\Tank.Sim\Tank.Sim.csproj" />
+    <ProjectReference Include="..\..\src\Tank.Net\Tank.Net.csproj" />
   </ItemGroup>
   <ItemGroup>
     <Using Include="Xunit" />
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`LaunchOptions` 没有 `Tank`。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index 24d5084..4d34c03 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -1,6 +1,7 @@
 using System.Globalization;
 using Tank.Core.World;
 using Tank.Net.Protocol;
+using Tank.Sim.Vehicles;
 
 namespace Tank.Client.Bootstrap;
 
@@ -12,6 +13,7 @@ namespace Tank.Client.Bootstrap;
 /// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
 /// --range-demo pen|ricochet|nopen|he|kill（试驾场：自动对靶车开炮，用于截图）  --shot-after fire|hit|kill:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克或第一次击毁坦克后再过 SECONDS 秒截图）
+/// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
 /// for the caller to report.
 /// </summary>
@@ -42,6 +44,9 @@ public sealed record LaunchOptions
     public string? ShotAfterEvent { get; init; }
     public double ShotAfterDelay { get; init; }
 
+    /// <summary>The <c>TankCatalog</c> id to start the sandbox in (<c>--tank</c>); null when not given or not a known tank.</summary>
+    public string? Tank { get; init; }
+
     /// <summary>One line per option value that couldn't be read (and was ignored or replaced by its default).</summary>
     public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
 
@@ -72,6 +77,7 @@ public sealed record LaunchOptions
                 "--fx-shot" => o with { FxShotDelay = Seconds("--fx-shot", Next(), 0.05, warnings) },
                 "--range-demo" => o with { RangeDemo = Next() },
                 "--shot-after" => ParseShotAfter(o, Next(), warnings),
+                "--tank" => ParseTank(o, Next(), warnings),
                 "--screenshot" => o with { ScreenshotPath = Next() },
                 "--screenshot-at" => o with { ScreenshotAt = Seconds("--screenshot-at", Next(), o.ScreenshotAt, warnings) },
                 "--quit-after" => o with { QuitAfter = Seconds("--quit-after", Next(), 0, warnings) },
@@ -100,6 +106,13 @@ public sealed record LaunchOptions
         return o with { ShotAfterEvent = parts[0], ShotAfterDelay = delay };
     }
 
+    private static LaunchOptions ParseTank(LaunchOptions o, string value, List<string> warnings)
+    {
+        if (TankCatalog.All.Any(spec => spec.Id == value)) return o with { Tank = value };
+        warnings.Add($"Bad --tank '{value}'; use {string.Join("|", TankCatalog.All.Select(spec => spec.Id))}.");
+        return o;
+    }
+
     private static bool TryParseSeconds(string value, out double seconds) =>
         double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
 }
PATCH
```

修改 `src/Tank.Client/scripts/Overlay/StatusPanel.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/StatusPanel.cs b/src/Tank.Client/scripts/Overlay/StatusPanel.cs
index f998304..49701df 100644
--- a/src/Tank.Client/scripts/Overlay/StatusPanel.cs
+++ b/src/Tank.Client/scripts/Overlay/StatusPanel.cs
@@ -8,7 +8,8 @@ namespace Tank.Client.Overlay;
 /// Bottom-left rounded panel in three rows: a capsule health bar with the hit points; a speedometer icon with the speed
 /// as a big outlined number and a shell icon with the reload state ("就绪" in the accent colour, or the seconds left);
 /// and two ammo chips, "1 穿甲 40" and "2 高爆 15", the loaded one outlined in the accent colour, an empty one greyed.
-/// Icons are drawn with primitives, no image files. Redraws only when something it shows changes.
+/// The vehicle's name ("中型坦克") sits just above the panel. Icons are drawn with primitives, no image files. Redraws
+/// only when something it shows changes.
 /// </summary>
 public partial class StatusPanel : Control
 {
@@ -22,6 +23,8 @@ public partial class StatusPanel : Control
     private const int UnitSize = 20;
     private const int HealthSize = 22;
     private const int ChipSize = 22;
+    private const int NameSize = 24;
+    private const float NameGap = 10f;   // between the name's baseline and the panel's top
     private const float HealthRow = 34f, GaugeRow = 64f, ChipRow = 40f, RowGap = 4f;
     private const float BarHeight = 16f;
     private const float ChipPad = 10f, ChipGap = 12f;
@@ -50,6 +53,7 @@ public partial class StatusPanel : Control
     private string _reloadText = "就绪";
     private int _health = -1, _maxHealth = 1;
     private string _healthText = "";
+    private string _vehicle = "";
     private AmmoType _ammo;
     private int _apLeft = -1, _heLeft = -1;
     private string _apText = "", _heText = "";
@@ -69,6 +73,13 @@ public partial class StatusPanel : Control
         _chipWidth = HudStyle.TextWidth("2 高爆 88", ChipSize) + 2 * ChipPad;
     }
 
+    public void SetVehicle(string name)
+    {
+        if (name == _vehicle) return;
+        _vehicle = name;
+        QueueRedraw();
+    }
+
     /// <param name="speedKmh">Ground speed, km/h (sign ignored).</param>
     /// <param name="reloadSeconds">Seconds until the gun is ready; 0 = ready.</param>
     public void UpdateState(float speedKmh, float reloadSeconds, int health, int maxHealth, AmmoType ammo, int apLeft, int heLeft)
@@ -92,6 +103,7 @@ public partial class StatusPanel : Control
     {
         var origin = new Vector2(Margin, Size.Y - Margin - PanelHeight);
         DrawStyleBox(_box, new Rect2(origin, new Vector2(_panelWidth, PanelHeight)));
+        HudStyle.DrawOutlined(this, new Vector2(origin.X + Pad, origin.Y - NameGap), _vehicle, NameSize, HudStyle.Text, 4);
         float y = origin.Y + Pad;
         DrawHealth(origin.X, y + HealthRow / 2);
         y += HealthRow + RowGap;
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxHud.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
index 9e9277c..f05a261 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
@@ -59,6 +59,7 @@ public partial class SandboxHud : CanvasLayer
     public void Refresh(in TankState player)
     {
         UpdateSight(player);
+        _status.SetVehicle(player.Spec.Name);
         _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
         _vignette.Visible = _camera.Sniper;
         for (int i = 0; i < _targets.Count; i++)
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
index 9d59d16..00700e4 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
@@ -6,7 +6,7 @@ namespace Tank.Client.Sandbox;
 /// <summary>
 /// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
 /// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
-/// AP or HE (kept until changed); R asks for a restock, consumed once by the next tick.
+/// AP or HE (kept until changed); R asks for a restock and F1–F4 for another tank, each consumed once by the next tick.
 /// </summary>
 public sealed class SandboxInput
 {
@@ -16,6 +16,8 @@ public sealed class SandboxInput
     private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
     private static readonly StringName FreeLookAction = "free_look";
     private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he", Restock = "restock";
+    private static readonly StringName[] PickTank = { "pick_tank_1", "pick_tank_2", "pick_tank_3", "pick_tank_4" };
+    private static readonly Key[] PickTankKeys = { Key.F1, Key.F2, Key.F3, Key.F4 };
 
     // Also read by SandboxMain's input handlers; UiCancel is Godot's built-in action, not registered here.
     internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", UiCancel = "ui_cancel";
@@ -23,6 +25,7 @@ public sealed class SandboxInput
     private bool _fireLatched;
     private bool _fireHeld;
     private bool _restock;
+    private int _pickedTank = -1;
 
     public static void RegisterActions()
     {
@@ -36,6 +39,7 @@ public sealed class SandboxInput
         AddKey(AmmoAp, Key.Key1);
         AddKey(AmmoHe, Key.Key2);
         AddKey(Restock, Key.R);
+        for (int i = 0; i < PickTank.Length; i++) AddKey(PickTank[i], PickTankKeys[i]);
     }
 
     /// <summary>The round the player wants loaded.</summary>
@@ -50,8 +54,16 @@ public sealed class SandboxInput
         else if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
         else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
         else if (e.IsActionPressed(Restock)) _restock = true;
+        else
+        {
+            for (int i = 0; i < PickTank.Length; i++)
+                if (e.IsActionPressed(PickTank[i])) _pickedTank = i;
+        }
     }
 
+    /// <summary>Back to AP, the round a new tank starts with (after a tank change), until 1 or 2 is pressed again.</summary>
+    public void ResetAmmo() => Ammo = AmmoType.AP;
+
     /// <summary>Drops any pending or held fire (focus lost, so a release may never arrive).</summary>
     public void ReleaseAll()
     {
@@ -67,6 +79,14 @@ public sealed class SandboxInput
         return restock;
     }
 
+    /// <summary>Once after F1–F4 was pressed: the tank asked for, as an index into <c>TankCatalog.All</c>.</summary>
+    public int? TakeTankPick()
+    {
+        int picked = _pickedTank;
+        _pickedTank = -1;
+        return picked < 0 ? null : picked;
+    }
+
     public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
     {
         sbyte throttle = (sbyte)(Held(DriveForward) - Held(DriveBack));
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 1b66dec..2d25771 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -15,10 +15,12 @@ namespace Tank.Client.Sandbox;
 
 /// <summary>
 /// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0;
-/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock. Scenery, HUD and
-/// scripted runs live in <see cref="SandboxWorld"/>, <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
-/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --autodrive --camera-preset front|side|hill|spin|orbit|bush|target|...
-/// --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
+/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock, F1–F4 another tank
+/// (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
+/// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
+/// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
+/// --camera-preset front|side|hill|spin|orbit|bush|target|... --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill
+/// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
 /// </summary>
 public partial class SandboxMain : Node3D
 {
@@ -62,7 +64,8 @@ public partial class SandboxMain : Node3D
 
         RangeDemo? demo = _automation.Demo;
         _sim = new Simulation(SandboxMap.Create());
-        _sim.AddTank(TankCatalog.Medium, demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), demo?.PlayerYaw ?? SandboxMap.SpawnYaw);
+        TankSpec start = _options.Tank is { } id ? TankCatalog.ById(id) : TankCatalog.Medium;
+        _sim.AddTank(start, demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), demo?.PlayerYaw ?? SandboxMap.SpawnYaw);
         _previous = _current = _sim.Tanks[0];
         _targets = Add(new TargetRange());
         _targets.Build(_sim);
@@ -72,11 +75,7 @@ public partial class SandboxMain : Node3D
 
         _world = Add(new SandboxWorld());
         _world.Build(_sim.Field, _current, CameraPresets.StreamsAroundCamera(_options.CameraPreset));
-        _tank = Add(new TankModel());
-        _tank.Build(TankCatalog.Medium);
-        _dust = new DustTrail();
-        _tank.AddChild(_dust);
-        _dust.Build(TankLooks.For(TankCatalog.Medium.Id).TrackX, TankCatalog.Medium.HullLength / 2f);
+        BuildTank(start);
         _shells = Add(new ShellViews());
         _fx = Add(new BattleFx());
         _camera = Add(new OrbitCamera());
@@ -154,6 +153,7 @@ public partial class SandboxMain : Node3D
     private void StepSimulation()
     {
         if (!HasPreset && _input.TakeRestock()) _sim.Restock(_current.Id);
+        if (_input.TakeTankPick() is int pick && pick < TankCatalog.All.Count) ChangeTank(TankCatalog.All[pick]);
         _targets.ReviveDue(_sim);
         var (throttle, steer, fire) = _input.Sample();
         var own = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo);
@@ -166,6 +166,26 @@ public partial class SandboxMain : Node3D
         _current = _sim.Tanks[0];
     }
 
+    /// <summary>Makes the player's tank <paramref name="spec"/> where it stands, with a fresh model and dust trail and AP selected.</summary>
+    private void ChangeTank(TankSpec spec)
+    {
+        if (spec == _current.Spec) return;
+        _sim.ChangeSpec(_current.Id, spec);
+        _input.ResetAmmo();   // the new tank starts with AP loaded; still asking for HE would cost it a full reload
+        _previous = _current = _sim.Tanks[0];
+        _tank.QueueFree();   // the dust trail goes with it
+        BuildTank(spec);
+    }
+
+    private void BuildTank(TankSpec spec)
+    {
+        _tank = Add(new TankModel());
+        _tank.Build(spec);
+        _dust = new DustTrail();
+        _tank.AddChild(_dust);
+        _dust.Build(TankLooks.For(spec.Id).TrackX, spec.HullLength / 2f);
+    }
+
     private void OnEvent(in GameEvent e)
     {
         _automation.OnEvent(e, _current.Id, _time);
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxWorld.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs b/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
index f4a0cf1..eaf9095 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
@@ -10,8 +10,8 @@ namespace Tank.Client.Sandbox;
 
 /// <summary>
 /// The sandbox's scenery, built once from the battlefield: sky and sun, terrain, rocks, bushes, grass and the tree line.
-/// Each frame <see cref="Update"/> lays the grass flat under the player's tank, streams grass detail around the view and
-/// lets the bushes part around the hull.
+/// Each frame <see cref="Update"/> lays the grass flat under the player's tank (sized for whichever tank it is now),
+/// streams grass detail around the view and lets the bushes part around the hull.
 /// </summary>
 public partial class SandboxWorld : Node3D
 {
@@ -21,18 +21,14 @@ public partial class SandboxWorld : Node3D
     private GrassField _grass = null!;
     private BushField _bushes = null!;
     private SN.Vector2 _flatHalfExtents;
+    private TankSpec? _flatFor;
     private bool _streamsAroundCamera;
 
-    /// <param name="player">The player's tank: its size sets the patch of grass it flattens.</param>
+    /// <param name="player">The player's tank, for the first patch of grass streamed in.</param>
     /// <param name="streamsAroundCamera">The camera is a fixed far-away preset: stream detail around its eye, not the tank.</param>
     public void Build(Battlefield field, in TankState player, bool streamsAroundCamera)
     {
         _streamsAroundCamera = streamsAroundCamera;
-        TankSpec spec = player.Spec;
-        TankLook look = TankLooks.For(spec.Id);
-        float trackHalf = look.TrackX + look.TrackWidth / 2 + FootprintMargin;
-        _flatHalfExtents = new SN.Vector2(MathF.Max(spec.HullWidth / 2 + FootprintMargin, trackHalf), spec.HullLength / 2 + FootprintMargin);
-
         AddChild(Scenery.CreateEnvironment());
         AddChild(Scenery.CreateSun());
         Scenery.ConfigureViewport(GetViewport());
@@ -59,6 +55,7 @@ public partial class SandboxWorld : Node3D
     /// <param name="pose">The player's tank on the ground this frame.</param>
     public void Update(in TankState player, TankPose pose, Vector3 cameraPos, float delta)
     {
+        if (player.Spec != _flatFor) (_flatFor, _flatHalfExtents) = (player.Spec, FlatHalfExtents(player.Spec));
         _grass.SetTankFootprint(new Obb2(new SN.Vector2(pose.Position.X, pose.Position.Z), _flatHalfExtents, player.Yaw));
         // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
         // camera is in the dense band); fixed far-away presets stream around their eye.
@@ -67,4 +64,12 @@ public partial class SandboxWorld : Node3D
             : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
         _bushes.Update(cameraPos, TankMotion.Footprint(player.Spec, player.Position, player.Yaw), delta);
     }
+
+    /// <summary>Half-size of the patch a tank lays flat: its hull and tracks, plus a margin.</summary>
+    private static SN.Vector2 FlatHalfExtents(TankSpec spec)
+    {
+        TankLook look = TankLooks.For(spec.Id);
+        float trackHalf = look.TrackX + look.TrackWidth / 2 + FootprintMargin;
+        return new SN.Vector2(MathF.Max(spec.HullWidth / 2 + FootprintMargin, trackHalf), spec.HullLength / 2 + FootprintMargin);
+    }
 }
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Art 202（Sim 仍为 296）；客户端 0 警告 0 错误。

- [ ] **Step 5：截图**

```bash
importall
atshot t4-name 3 --tank medium
godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --tank tiger --quit-after 2 2>&1 | grep -E "tiger|ERROR|Exception"
```

Expected：`t4-name.png` 左下角面板上方有描边的“中型坦克”四个字，面板本身和以前一样；第二条命令打印 `WARNING: Bad --tank 'tiger'; use medium.`。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): --tank and F1–F4 pick the sandbox tank, its name over the status panel"
```

---

### Task 5：轻坦

设计 §2 的轻坦数值和 §4.1 的外观：小而低，小圆顶炮塔，细长炮管，3 对负重轮。先把外观测试从“只测中坦”改成对每种车都测（`SpecIds`），只对有炮塔的车成立的检查用 `TurretIds`；炮管俯仰的范围改用各车自己的俯角和仰角；“车身要厚”的检查从中坦的 0.7～0.9 m 放宽为不小于 0.6 m（轻坦更矮）；中坦专属的圆顶尺寸单独留一个测试。炮塔飞起的测试也改成对每种有炮塔的车都做。再加平衡测试：每种车的穿甲弹每秒伤害取整后在 52～58 之间（设计 §2.1）。

**Files:**
- Modify: `src/Tank.Sim/Vehicles/TankCatalog.cs`、`src/Tank.Client/scripts/Vehicles/TankLooks.cs`
- Test: `tests/Tank.Sim.Tests/CombatSpecTests.cs`、`tests/Tank.Art.Tests/TankLookTests.cs`、`TurretTossTests.cs`

- [ ] **Step 1：改测试**

修改 `tests/Tank.Art.Tests/TankLookTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/TankLookTests.cs b/tests/Tank.Art.Tests/TankLookTests.cs
index 84049d9..fc1fe0f 100644
--- a/tests/Tank.Art.Tests/TankLookTests.cs
+++ b/tests/Tank.Art.Tests/TankLookTests.cs
@@ -10,6 +10,9 @@ public class TankLookTests
 {
     public static TheoryData<string> SpecIds => new(TankCatalog.All.Select(s => s.Id));
 
+    /// <summary>Tanks whose gun sits in a turning turret (the tank destroyer's is in its casemate).</summary>
+    public static TheoryData<string> TurretIds => new(TankCatalog.All.Where(s => s.HasTurret).Select(s => s.Id));
+
     private static float BarrelLength(TankLook look, TankSpec spec) => spec.GunLength + look.GunPivotZ;
 
     [Theory]
@@ -68,16 +71,18 @@ public class TankLookTests
         Assert.True(look.TurretProfile.Max(p => p.X) <= spec.HullWidth / 2, "turret wider than the hull");
     }
 
-    [Fact]
-    public void Paint_is_the_orange_toy_palette()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Paint_is_the_orange_toy_palette(string specId)
     {
-        Assert.Equal(new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub), TankLooks.Medium.Paint);
+        Assert.Equal(new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub), TankLooks.For(specId).Paint);   // shapes, not colours, tell them apart
     }
 
-    [Fact]
-    public void Hull_is_soft_and_round()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Hull_is_soft_and_round(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         foreach (var (profile, width, round) in new[]
                  { (look.LowerHull, look.LowerHullWidth, look.LowerHullRound), (look.UpperHull, look.UpperHullWidth, look.UpperHullRound) })
         {
@@ -88,23 +93,31 @@ public class TankLookTests
     }
 
     [Fact]
-    public void Turret_is_a_round_dome()
+    public void Medium_turret_is_a_big_dome()
     {
-        TankLook look = TankLooks.Medium;
-        Vector2[] dome = look.TurretProfile;
-        Assert.Equal(0f, dome[^1].X);                                     // closed at the top
+        Vector2[] dome = TankLooks.Medium.TurretProfile;
         Assert.InRange(dome.Max(p => p.X), 1.35f, 1.55f);
         Assert.InRange(dome[^1].Y - dome[0].Y, 1.05f, 1.25f);
+    }
+
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Turret_is_a_soft_closed_dome(string specId)
+    {
+        TankLook look = TankLooks.For(specId);
+        Vector2[] dome = look.TurretProfile;
+        Assert.Equal(0f, dome[^1].X);                                     // closed at the top
         for (int i = 1; i < dome.Length; i++) Assert.True(dome[i].Y > dome[i - 1].Y, "the dome rises all the way up");
         MeshData shell = Lathe.Build(dome, TankParts.DomeSides);
         Assert.True(MeshAssert.MaxCreaseDegrees(shell, minY: 0.15f) <= 35f, $"dome crease {MeshAssert.MaxCreaseDegrees(shell, minY: 0.15f):F0}°");
     }
 
-    [Fact]
-    public void Barrel_and_muzzle_bulb_end_where_the_sim_puts_the_muzzle()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Barrel_and_muzzle_bulb_end_where_the_sim_puts_the_muzzle(string specId)
     {
-        TankLook look = TankLooks.Medium;
-        float length = BarrelLength(look, TankCatalog.Medium);
+        TankLook look = TankLooks.For(specId);
+        float length = BarrelLength(look, TankCatalog.ById(specId));
         var (barrelMin, barrelMax) = MeshAssert.Bounds(TankParts.Barrel(look, length));
         var (muzzleMin, muzzleMax) = MeshAssert.Bounds(TankParts.Muzzle(look, length));
         Assert.Equal(-length, muzzleMin.Z, 4);
@@ -116,20 +129,22 @@ public class TankLookTests
         Assert.True(look.MuzzleRadius > look.BarrelRadius * 1.2f, "a fat round bulb");
     }
 
-    [Fact]
-    public void Wheel_hubs_face_outward_on_both_sides()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Wheel_hubs_face_outward_on_both_sides(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         var (_, rightMax) = MeshAssert.Bounds(TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: false));
         var (leftMin, _) = MeshAssert.Bounds(TankParts.Wheel(look.RoadWheelRadius, look.RoadWheelWidth, left: true));
         Assert.True(rightMax.X > look.RoadWheelWidth / 2, "right hub sticks out to +X");
         Assert.True(leftMin.X < -look.RoadWheelWidth / 2, "left hub sticks out to −X");
     }
 
-    [Fact]
-    public void Wheels_do_not_overlap_each_other()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Wheels_do_not_overlap_each_other(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         var wheels = Enumerable.Range(0, look.RoadWheelCount)
             .Select(i => new WheelSpot(look.RoadWheelZ(i), look.RoadWheelY, look.RoadWheelRadius))
             .Append(look.Sprocket).Append(look.Idler).ToArray();
@@ -141,10 +156,11 @@ public class TankLookTests
         }
     }
 
-    [Fact]
-    public void Track_sits_on_the_ground_and_runs_over_the_road_wheels()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Track_sits_on_the_ground_and_runs_over_the_road_wheels(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         var belt = new BeltPath(TankParts.TrackLoop(look));
         float lowest = float.MaxValue;
         for (int i = 0; i < 500; i++) lowest = MathF.Min(lowest, belt.Sample(belt.Length * i / 500f).Position.Y);
@@ -161,10 +177,11 @@ public class TankLookTests
     /// skirt's rounded bottom edge has already curled up a little — so no link ever shows through a gap. It stops near
     /// the road-wheel tops, so the wheels still show below it.
     /// </summary>
-    [Fact]
-    public void Side_skirts_hide_the_upper_track_run_but_not_the_road_wheels()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Side_skirts_hide_the_upper_track_run_but_not_the_road_wheels(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         var belt = new BeltPath(TankParts.TrackLoop(look));
         const float Cover = 0.01f;
         float gearOuter = MathF.Max(look.TrackX + look.TrackWidth / 2,
@@ -186,25 +203,27 @@ public class TankLookTests
         Assert.True(look.UpperHullWidth / 2 >= gearOuter, "the skirts must reach outboard of tracks and hubs");
         float skirtBottom = look.UpperHull.Min(p => p.Y);
         Assert.InRange(skirtBottom, look.RoadWheelY + 0.2f, look.RoadWheelY + look.RoadWheelRadius);
-        Assert.InRange(look.UpperHull.Max(p => p.Y) - skirtBottom, 0.7f, 0.9f);   // a fat body, not a thin plate
+        Assert.True(look.UpperHull.Max(p => p.Y) - skirtBottom >= 0.6f, "a fat body, not a thin plate");
     }
 
-    [Fact]
-    public void Lower_hull_fits_between_the_tracks()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Lower_hull_fits_between_the_tracks(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         Assert.True(look.LowerHullWidth / 2 < look.TrackX - look.TrackWidth / 2, "lower hull rubs the tracks");
     }
 
-    [Fact]
-    public void Visible_muzzle_matches_the_sim_muzzle_across_the_pitch_range()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Visible_muzzle_matches_the_sim_muzzle_across_the_pitch_range(string specId)
     {
-        TankLook look = TankLooks.Medium;
-        TankSpec spec = TankCatalog.Medium;
+        TankLook look = TankLooks.For(specId);
+        TankSpec spec = TankCatalog.ById(specId);
         var trunnion = new Vector3(0, spec.TrunnionHeight, -spec.TurretOffset);
-        for (float deg = -7; deg <= 20; deg += 1)
+        foreach (float pitch in PitchRange(spec))
         {
-            float pitch = deg * MathF.PI / 180f;
+            float deg = pitch * 180f / MathF.PI;
             var visual = trunnion + new Vector3(0, 0, look.GunPivotZ)
                 + Vector3.Transform(new Vector3(0, 0, -BarrelLength(look, spec)), Quaternion.CreateFromAxisAngle(Vector3.UnitX, pitch));
             var sim = trunnion + Tank.Sim.Geometry.Angles.Direction(0, pitch) * spec.GunLength;
@@ -212,16 +231,17 @@ public class TankLookTests
         }
     }
 
-    [Fact]
-    public void Mantlet_back_stays_buried_in_the_dome_across_the_pitch_range()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Mantlet_back_stays_buried_in_the_dome_across_the_pitch_range(string specId)
     {
-        TankLook look = TankLooks.Medium;
-        TankSpec spec = TankCatalog.Medium;
+        TankLook look = TankLooks.For(specId);
+        TankSpec spec = TankCatalog.ById(specId);
         float r = look.MantletRadius, h = look.MantletWidth / 2 - r;   // capsule: cylinder half-length h, round ends
         Assert.True(h > 0, "a capsule, wider than tall");
-        for (float deg = -7; deg <= 20; deg += 1)
+        foreach (float pitch in PitchRange(spec))
         {
-            float pitch = deg * MathF.PI / 180f;
+            float deg = pitch * 180f / MathF.PI;
             foreach (float x in new[] { -h, 0f, h })
             foreach (float back in new[] { r, 0.5f * r })
             {
@@ -241,11 +261,12 @@ public class TankLookTests
     /// Depressing the gun fully while the hull rocks nose-up (a shot's nod) must not push the barrel, bulb or mantlet
     /// into the hull top. The hull nods about (0, RoadWheelY, 0); the turret is not tilted, it only follows the ring.
     /// </summary>
-    [Fact]
-    public void Gun_and_mantlet_clear_the_hull_top_at_full_depression_while_the_hull_nods()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Gun_and_mantlet_clear_the_hull_top_at_full_depression_while_the_hull_nods(string specId)
     {
-        TankLook look = TankLooks.Medium;
-        TankSpec spec = TankCatalog.Medium;
+        TankLook look = TankLooks.For(specId);
+        TankSpec spec = TankCatalog.ById(specId);
         const float Margin = 0.01f;
         float length = BarrelLength(look, spec);
         foreach (float nod in new[] { 0f, TankAnimator.MaxNod, -TankAnimator.MaxNod })
@@ -255,9 +276,10 @@ public class TankLookTests
             var ring = new Vector3(0, look.TurretBase, -spec.TurretOffset);
             Vector3 turretShift = pivot + Vector3.Transform(ring - pivot, bodyNod) - ring;   // TankModel.Animate
             var gunPivot = new Vector3(0, spec.TrunnionHeight, -spec.TurretOffset + look.GunPivotZ) + turretShift;
-            for (float deg = -7; deg <= 0; deg += 1)
+            foreach (float down in PitchRange(spec).Where(p => p <= 0))
             {
-                Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, deg * MathF.PI / 180f);
+                float deg = down * 180f / MathF.PI;
+                Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, down);
                 var undersides = new List<(Vector3 Point, string Name)> { (gunPivot - new Vector3(0, look.MantletRadius, 0), "mantlet") };
                 for (float d = 0; d <= length; d += 0.05f)
                 {
@@ -275,11 +297,12 @@ public class TankLookTests
         }
     }
 
-    [Fact]
-    public void Turret_rests_on_the_hull_roof_and_the_mantlet_clears_it()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Turret_rests_on_the_hull_roof_and_the_mantlet_clears_it(string specId)
     {
-        TankLook look = TankLooks.Medium;
-        TankSpec spec = TankCatalog.Medium;
+        TankLook look = TankLooks.For(specId);
+        TankSpec spec = TankCatalog.ById(specId);
         float roof = look.UpperHull.Max(p => p.Y);
         Assert.InRange(look.TurretBase, roof - 0.1f, roof);
         Assert.True(spec.TrunnionHeight - look.MantletRadius > roof, "the mantlet sinks into the hull roof");
@@ -302,10 +325,11 @@ public class TankLookTests
         Assert.True(hatch.Position.Y + hatch.Size.Y > look.TurretBase + look.TurretProfile[^1].Y, "hatch lid hidden in the dome");
     }
 
-    [Fact]
-    public void Sprocket_has_one_tooth_per_track_link_around_the_pitch_circle()
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Sprocket_has_one_tooth_per_track_link_around_the_pitch_circle(string specId)
     {
-        TankLook look = TankLooks.Medium;
+        TankLook look = TankLooks.For(specId);
         float pitchRadius = look.Sprocket.Radius + look.TrackThickness / 2;
         Assert.Equal((int)MathF.Round(MathF.Tau * pitchRadius / look.LinkPitch), TankParts.SprocketTeethCount(look));
     }
@@ -336,6 +360,14 @@ public class TankLookTests
         Assert.Contains(hull.Colors, c => c.X == 1f);
     }
 
+    /// <summary>The gun's whole pitch range in 1° steps, both limits included.</summary>
+    private static IEnumerable<float> PitchRange(TankSpec spec)
+    {
+        const float Step = MathF.PI / 180f;
+        for (float pitch = -spec.GunDepression; pitch < spec.GunElevation; pitch += Step) yield return pitch;
+        yield return spec.GunElevation;
+    }
+
     /// <summary>Lowest y of a convex (z, y) profile on the vertical line at <paramref name="z"/>; +∞ outside it.</summary>
     private static float LowerEdge(Vector2[] profile, float z)
     {
PATCH
```

修改 `tests/Tank.Art.Tests/TurretTossTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/TurretTossTests.cs b/tests/Tank.Art.Tests/TurretTossTests.cs
index 21319e2..0f746f3 100644
--- a/tests/Tank.Art.Tests/TurretTossTests.cs
+++ b/tests/Tank.Art.Tests/TurretTossTests.cs
@@ -6,58 +6,75 @@ namespace Tank.Art.Tests;
 
 public class TurretTossTests
 {
-    private static readonly TankLook Look = TankLooks.Medium;
-    private static readonly TurretToss Toss = new(Look, TankCatalog.Medium.TurretOffset);
+    public static TheoryData<string> TurretIds => new(TankCatalog.All.Where(s => s.HasTurret).Select(s => s.Id));
 
-    [Fact]
-    public void Starts_where_the_intact_turret_is()
+    /// <summary>Every turret tank, crossed with kill-time turret yaws all round the clock.</summary>
+    public static TheoryData<string, float> TurretIdsAndYaws()
     {
-        var (position, rotation) = Toss.At(0f, startYaw: 0.7f);
-        Assert.Equal(Toss.Pivot, position);
-        Assert.Equal(new Vector3(0, Look.TurretBase + TurretToss.PivotAboveRing, -TankCatalog.Medium.TurretOffset), position);
+        var data = new TheoryData<string, float>();
+        foreach (TankSpec spec in TankCatalog.All.Where(s => s.HasTurret))
+        foreach (float yaw in new[] { 0f, MathF.PI / 2, -MathF.PI / 2, MathF.PI, -MathF.PI, 2.5f })
+            data.Add(spec.Id, yaw);
+        return data;
+    }
+
+    private static TurretToss TossOf(string specId) => new(TankLooks.For(specId), TankCatalog.ById(specId).TurretOffset);
+
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Starts_where_the_intact_turret_is(string specId)
+    {
+        TurretToss toss = TossOf(specId);
+        TankLook look = TankLooks.For(specId);
+        var (position, rotation) = toss.At(0f, startYaw: 0.7f);
+        Assert.Equal(toss.Pivot, position);
+        Assert.Equal(new Vector3(0, look.TurretBase + TurretToss.PivotAboveRing, -TankCatalog.ById(specId).TurretOffset), position);
         Quaternion expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.7f);
         Assert.True(MathF.Abs(Quaternion.Dot(expected, rotation)) > 0.9999f, $"rotation {rotation}");
     }
 
-    [Fact]
-    public void Flies_about_four_metres_up()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Flies_about_four_metres_up(string specId)
     {
+        TurretToss toss = TossOf(specId);
         float highest = 0f;
-        for (int i = 0; i <= 110; i++) highest = MathF.Max(highest, Toss.At(i / 100f, 0f).Position.Y - Toss.Pivot.Y);
+        for (int i = 0; i <= 110; i++) highest = MathF.Max(highest, toss.At(i / 100f, 0f).Position.Y - toss.Pivot.Y);
         Assert.InRange(highest, TurretToss.Apex - 0.4f, TurretToss.Apex + 0.4f);
     }
 
-    [Fact]
-    public void Lands_beside_the_hull_and_stays()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Lands_beside_the_hull_and_stays(string specId)
     {
-        var landed = Toss.At(TurretToss.FlightTime, 0.3f);
-        Assert.Equal(Toss.Landing, landed.Position);
-        Assert.Equal(landed, Toss.At(TurretToss.FlightTime + 5f, 0.3f));
-        Assert.True(Toss.Landing.X - Toss.DomeRadius >= Look.UpperHullWidth / 2, "the landed dome overlaps the hull");
+        TurretToss toss = TossOf(specId);
+        TankLook look = TankLooks.For(specId);
+        var landed = toss.At(TurretToss.FlightTime, 0.3f);
+        Assert.Equal(toss.Landing, landed.Position);
+        Assert.Equal(landed, toss.At(TurretToss.FlightTime + 5f, 0.3f));
+        Assert.True(toss.Landing.X - toss.DomeRadius >= look.UpperHullWidth / 2, "the landed dome overlaps the hull");
     }
 
-    [Fact]
-    public void Landed_dome_rests_on_the_ground()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Landed_dome_rests_on_the_ground(string specId)
     {
-        var (position, rotation) = Toss.At(TurretToss.FlightTime, 1.1f);
-        float lowest = position.Y + TurretToss.LowestRimPoint(rotation, Toss.DomeRadius);
+        TurretToss toss = TossOf(specId);
+        var (position, rotation) = toss.At(TurretToss.FlightTime, 1.1f);
+        float lowest = position.Y + TurretToss.LowestRimPoint(rotation, toss.DomeRadius);
         Assert.Equal(0f, lowest, 0.02f);
     }
 
     [Theory]
-    [InlineData(0f)]
-    [InlineData(MathF.PI / 2)]
-    [InlineData(-MathF.PI / 2)]
-    [InlineData(MathF.PI)]
-    [InlineData(-MathF.PI)]
-    [InlineData(2.5f)]
-    public void Spins_more_than_a_full_turn_without_jumps(float startYaw)
+    [MemberData(nameof(TurretIdsAndYaws))]
+    public void Spins_more_than_a_full_turn_without_jumps(string specId, float startYaw)
     {
+        TurretToss toss = TossOf(specId);
         // Sample the forward direction's heading; it must sweep past a full circle in small steps.
-        float swept = 0f, last = Heading(Toss.At(0f, startYaw).Rotation);
+        float swept = 0f, last = Heading(toss.At(0f, startYaw).Rotation);
         for (int i = 1; i <= 100; i++)
         {
-            float heading = Heading(Toss.At(TurretToss.FlightTime * i / 100f, startYaw).Rotation);
+            float heading = Heading(toss.At(TurretToss.FlightTime * i / 100f, startYaw).Rotation);
             float step = heading - last;
             if (step > MathF.PI) step -= MathF.Tau;
             if (step < -MathF.PI) step += MathF.Tau;
@@ -69,28 +86,27 @@ public class TurretTossTests
     }
 
     [Theory]
-    [InlineData(0f)]
-    [InlineData(MathF.PI / 2)]
-    [InlineData(-MathF.PI / 2)]
-    [InlineData(MathF.PI)]
-    [InlineData(-MathF.PI)]
-    [InlineData(2.5f)]
-    public void Landed_gun_points_away_from_the_hull(float startYaw)
+    [MemberData(nameof(TurretIdsAndYaws))]
+    public void Landed_gun_points_away_from_the_hull(string specId, float startYaw)
     {
-        var (position, rotation) = Toss.At(TurretToss.FlightTime, startYaw);
-        Vector3 muzzle = LandedMuzzle(startYaw, gunPitch: 0f);
-        Assert.True(MathF.Abs(muzzle.X) > Look.UpperHullWidth / 2, $"muzzle {muzzle} is over the hull");
+        TurretToss toss = TossOf(specId);
+        TankLook look = TankLooks.For(specId);
+        var (position, rotation) = toss.At(TurretToss.FlightTime, startYaw);
+        Vector3 muzzle = LandedMuzzle(specId, startYaw, gunPitch: 0f);
+        Assert.True(MathF.Abs(muzzle.X) > look.UpperHullWidth / 2, $"muzzle {muzzle} is over the hull");
         Assert.True(muzzle.X > position.X, $"muzzle {muzzle} points back towards the hull");
-        Assert.True(muzzle.Y > Look.MuzzleRadius, $"muzzle {muzzle} dips into the ground");
-        Quaternion landed = Toss.At(TurretToss.FlightTime, 0f).Rotation;
+        Assert.True(muzzle.Y > look.MuzzleRadius, $"muzzle {muzzle} dips into the ground");
+        Quaternion landed = toss.At(TurretToss.FlightTime, 0f).Rotation;
         Assert.True(MathF.Abs(Quaternion.Dot(landed, rotation)) > 0.9999f, "the landed pose depends on the start yaw");
     }
 
-    [Fact]
-    public void Landed_gun_at_full_depression_stays_above_the_ground()
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Landed_gun_at_full_depression_stays_above_the_ground(string specId)
     {
+        TurretToss toss = TossOf(specId);
         // A wreck keeps the gun pitch it had at the kill (TankModel.Apply still sets it); the landed pose ignores the start yaw.
-        Vector3 muzzle = LandedMuzzle(0f, -TankCatalog.Medium.GunDepression);
+        Vector3 muzzle = LandedMuzzle(specId, 0f, -TankCatalog.ById(specId).GunDepression);
         Assert.True(muzzle.Y > 0f, $"muzzle {muzzle} is buried in the ground");
     }
 
@@ -98,13 +114,15 @@ public class TurretTossTests
     /// The landed muzzle in hull space, nested as TankModel nests it: Toss (at the pivot) → TurretBody (the pivot height
     /// undone) → Gun (trunnion, GunPivotZ, pitched) → muzzle GunLength + GunPivotZ further along the gun's −Z.
     /// </summary>
-    private static Vector3 LandedMuzzle(float startYaw, float gunPitch)
+    private static Vector3 LandedMuzzle(string specId, float startYaw, float gunPitch)
     {
-        TankSpec spec = TankCatalog.Medium;
-        var (position, rotation) = Toss.At(TurretToss.FlightTime, startYaw);
-        var gun = new Vector3(0, spec.TrunnionHeight, Look.GunPivotZ);
-        Vector3 barrel = Vector3.Transform(new Vector3(0, 0, -(spec.GunLength + Look.GunPivotZ)), Quaternion.CreateFromAxisAngle(Vector3.UnitX, gunPitch));
-        return position + Vector3.Transform(gun + barrel - new Vector3(0, Toss.Pivot.Y, 0), rotation);
+        TankSpec spec = TankCatalog.ById(specId);
+        TankLook look = TankLooks.For(specId);
+        TurretToss toss = TossOf(specId);
+        var (position, rotation) = toss.At(TurretToss.FlightTime, startYaw);
+        var gun = new Vector3(0, spec.TrunnionHeight, look.GunPivotZ);
+        Vector3 barrel = Vector3.Transform(new Vector3(0, 0, -(spec.GunLength + look.GunPivotZ)), Quaternion.CreateFromAxisAngle(Vector3.UnitX, gunPitch));
+        return position + Vector3.Transform(gun + barrel - new Vector3(0, toss.Pivot.Y, 0), rotation);
     }
 
     private static float Heading(Quaternion rotation)
PATCH
```

修改 `tests/Tank.Sim.Tests/CombatSpecTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/CombatSpecTests.cs b/tests/Tank.Sim.Tests/CombatSpecTests.cs
index 6226770..c91ee8d 100644
--- a/tests/Tank.Sim.Tests/CombatSpecTests.cs
+++ b/tests/Tank.Sim.Tests/CombatSpecTests.cs
@@ -1,5 +1,6 @@
 using System.Numerics;
 using Tank.Sim.Combat;
+using Tank.Sim.Geometry;
 using Tank.Sim.Vehicles;
 
 namespace Tank.Sim.Tests;
@@ -88,6 +89,36 @@ public sealed class CombatSpecTests
         Assert.True(m.HasTurret);
     }
 
+    [Fact]
+    public void Light_MatchesTheDesignTable()
+    {
+        TankSpec l = TankCatalog.Light;
+        Assert.Equal(600, l.MaxHealth);
+        Assert.Equal(75f, l.Caliber);
+        Assert.Equal(2.5f, l.ReloadTime);
+        Assert.Equal(60f, l.MaxForwardSpeed * 3.6f, 1e-3f);
+        Assert.Equal(20f, l.MaxReverseSpeed * 3.6f, 1e-3f);
+        Assert.Equal(Angles.Deg(50), l.HullTraverse);
+        Assert.Equal(Angles.Deg(48), l.TurretTraverse);
+        Assert.Equal(new Armor(30, 25, 20, 15, 15), l.HullArmor.Armor);
+        Assert.Equal(new Armor(40, 30, 25, 15, 15), l.TurretArmor.Armor);
+        Assert.Equal(new ShellSpec(140, 130, 800, 0, 40), l.Ap);
+        Assert.Equal(new ShellSpec(40, 170, 640, 2.5f, 15), l.He);
+        Assert.Equal(0.40f, l.Dispersion);
+        Assert.Equal(1.8f, l.AimTime);
+        Assert.Equal(new BloomFactors(1.5f, 1.2f, 0.8f, 3.0f), l.Bloom);
+        Assert.True(l.HasTurret);
+    }
+
+    /// <summary>The 2B balance: tanks differ in the size of each hit, not in how much damage they deal over time.</summary>
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void EveryTank_Deals52To58ApDamageASecond(string id)
+    {
+        TankSpec s = Get(id);
+        Assert.InRange(s.Ap.Damage / s.ReloadTime, 52f, 58f);
+    }
+
     [Fact]
     public void Armor_LooksUpFaces_AndTheThinnest()
     {
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -3`
Expected: 编译失败，`TankCatalog.Light` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Vehicles/TankLooks.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankLooks.cs b/src/Tank.Client/scripts/Vehicles/TankLooks.cs
index 17cd3f0..9766e42 100644
--- a/src/Tank.Client/scripts/Vehicles/TankLooks.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankLooks.cs
@@ -4,11 +4,57 @@ using SN = System.Numerics;
 
 namespace Tank.Client.Vehicles;
 
-/// <summary>Look table, one entry per <c>TankCatalog</c> spec. The medium tank is an orange toy: soap-bar hull, big dome, stubby gun.</summary>
+/// <summary>
+/// Look table, one entry per <c>TankCatalog</c> spec, all orange toys told apart by shape. The light tank is small and
+/// low with a little dome set back and a long thin gun on three road wheels; the medium tank a soap-bar hull, big dome
+/// and stubby gun.
+/// </summary>
 public static class TankLooks
 {
     private const int CornerSegments = 4;
 
+    public static readonly TankLook Light = new()
+    {
+        SpecId = "light",
+        LowerHull = Rounded(
+            (new(-1.95f, 0.4f), 0.3f), (new(1.9f, 0.4f), 0.3f), (new(2.2f, 0.8f), 0.28f),
+            (new(2.2f, 1.3f), 0.28f), (new(-2.2f, 1.3f), 0.28f), (new(-2.2f, 0.9f), 0.28f)),
+        LowerHullWidth = 1.56f,
+        LowerHullRound = 0.26f,
+        // A low soap bar; the long glacis in front of the dome keeps the long gun clear at full depression.
+        UpperHull = Rounded(
+            (new(-2.45f, 0.92f), 0.22f), (new(2.5f, 0.92f), 0.28f), (new(2.5f, 1.55f), 0.32f),
+            (new(-1.6f, 1.55f), 0.45f), (new(-2.45f, 1.34f), 0.18f)),
+        UpperHullWidth = 2.8f,
+        UpperHullRound = 0.15f,
+        TurretBase = 1.49f,
+        TurretProfile = Dome(radius: 1.1f, height: 0.85f, lip: 0.04f, steps: 12),
+        GunPivotZ = -1.0f,
+        MantletRadius = 0.2f,
+        MantletWidth = 0.7f,
+        BarrelRadius = 0.13f,
+        MuzzleRadius = 0.2f,
+        MuzzleLength = 0.34f,
+        TrackX = 1.1f,
+        TrackWidth = 0.56f,
+        TrackThickness = 0.08f,
+        LinkPitch = 0.26f,
+        Sprocket = new WheelSpot(-2.28f, 0.66f, 0.42f),
+        Idler = new WheelSpot(2.28f, 0.66f, 0.42f),
+        RoadWheelCount = 3,
+        RoadWheelRadius = 0.5f,
+        RoadWheelY = 0.58f,
+        RoadWheelSpacing = 1.12f,
+        RoadWheelWidth = 0.46f,
+        Details = new DetailPart[]
+        {
+            new(DetailKind.Headlight, new(-0.7f, 1.42f, -2.3f), new(0.34f, 0.34f, 0.18f)),
+            new(DetailKind.Headlight, new(0.7f, 1.42f, -2.3f), new(0.34f, 0.34f, 0.18f)),
+            new(DetailKind.Cupola, new(0.2f, 2.22f, 0.25f), new(0.46f, 0.16f, 0f), OnTurret: true),
+        },
+        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
+    };
+
     public static readonly TankLook Medium = new()
     {
         SpecId = "medium",
@@ -55,7 +101,7 @@ public static class TankLooks
         Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
     };
 
-    public static IReadOnlyList<TankLook> All { get; } = new[] { Medium };
+    public static IReadOnlyList<TankLook> All { get; } = new[] { Light, Medium };
 
     public static TankLook For(string specId) =>
         All.FirstOrDefault(look => look.SpecId == specId)
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index 68d5422..c487bf8 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -3,11 +3,46 @@ using Tank.Sim.Geometry;
 
 namespace Tank.Sim.Vehicles;
 
-/// <summary>Vehicle table (design §4.4). Only the medium tank so far.</summary>
+/// <summary>
+/// Vehicle table (design §4.4, rebalanced in the stage 2B design §2: every tank deals 52–58 damage a second, the
+/// difference being in the size of each hit).
+/// </summary>
 public static class TankCatalog
 {
     private const float Kmh = 1f / 3.6f;
 
+    public static readonly TankSpec Light = new()
+    {
+        Id = "light",
+        Name = "轻型坦克",
+        HullLength = 5.5f,
+        HullWidth = 2.8f,
+        MaxForwardSpeed = 60 * Kmh,
+        MaxReverseSpeed = 20 * Kmh,
+        Acceleration = 5f,
+        Braking = 10f,
+        HullTraverse = Angles.Deg(50),
+        TurretTraverse = Angles.Deg(48),
+        GunArc = TankSpec.FullTraverse,
+        GunPitchSpeed = Angles.Deg(35),
+        GunDepression = Angles.Deg(8),
+        GunElevation = Angles.Deg(20),
+        MaxClimb = Angles.Deg(32),
+        TrunnionHeight = 1.85f,
+        TurretOffset = 0f,
+        GunLength = 3.0f,
+        ReloadTime = 2.5f,
+        MaxHealth = 600,
+        Caliber = 75,
+        HullArmor = new ArmorBox(Width: 2.8f, Length: 5.2f, Bottom: 0.3f, Top: 1.55f, new Armor(Front: 30, Side: 25, Rear: 20, Top: 15, Bottom: 15)),
+        TurretArmor = new ArmorBox(Width: 2.2f, Length: 2.2f, Bottom: 1.49f, Top: 2.3f, new Armor(Front: 40, Side: 30, Rear: 25, Top: 15, Bottom: 15)),
+        Ap = new ShellSpec(Penetration: 140, Damage: 130, Speed: 800, SplashRadius: 0, Capacity: 40),
+        He = new ShellSpec(Penetration: 40, Damage: 170, Speed: 800 * 0.8f, SplashRadius: 2.5f, Capacity: 15),
+        Dispersion = 0.40f,
+        AimTime = 1.8f,
+        Bloom = new BloomFactors(Moving: 1.5f, HullTurn: 1.2f, TurretTurn: 0.8f, Fire: 3.0f),
+    };
+
     public static readonly TankSpec Medium = new()
     {
         Id = "medium",
@@ -40,7 +75,8 @@ public static class TankCatalog
         Bloom = new BloomFactors(Moving: 2.0f, HullTurn: 1.5f, TurretTurn: 1.0f, Fire: 3.0f),
     };
 
-    public static IReadOnlyList<TankSpec> All { get; } = new[] { Medium };
+    /// <summary>Light to heavy; F1–F4 in the sandbox pick them in this order.</summary>
+    public static IReadOnlyList<TankSpec> All { get; } = new[] { Light, Medium };
 
     /// <summary>The spec named <paramref name="id"/>.</summary>
     /// <exception cref="KeyNotFoundException">No such tank; the message lists the known names.</exception>
PATCH
```


要点：外观数据里每条轮廓都是“角点 + 圆角半径”，一条边放不下两端的圆角时 `TankLooks` 的静态初始化会抛异常，整张外观表（包括中坦）都会加载失败，所有外观测试一起报错。遇到这种情况先看异常信息里的边号。

- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 302、Art 237。

- [ ] **Step 5：截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
shot t5-light-close close --tank light
shot t5-light-side side --tank light
shot t5-medium-close close --tank medium
```

Expected：
- 面板上方是“轻型坦克”，血量 600；
- 轻坦明显比中坦（`t5-medium-close`）矮、小：小圆顶炮塔，细长炮管，炮口是深色圆头；
- 侧面看每边 3 个负重轮加前后两个端轮，履带贴地，侧裙盖住履带上段，看得到负重轮；
- 炮管压在车体上方，没有插进车身。

- [ ] **Step 6：提交**

```bash
git add src tests
git commit -m "feat: light tank — 2.5 s reload, 130 damage, small and low with a long thin gun"
```

---

### Task 6：重坦

设计 §2 的重坦数值和 §4.1 的外观：又宽又高，炮塔是大的圆角方块，炮管粗、炮口大，5 对负重轮，履带宽。圆角方块炮塔的做法：车床旋转体 `Lathe.Build` 加一个参数 `squareness`（默认 2，就是原来的圆），大于 2 时截面是超椭圆 |x|ⁿ + |z|ⁿ = 1，侧面平、四角圆，侧面正好在轮廓的半径处；墙面法线按超椭圆的法线和轮廓法线合成（推导见 `Lathe.Ring` 的注释）。`squareness` 等于 2 时走原来的公式，保证圆形炮塔的网格逐位不变。炮塔飞起后落地时，`TurretToss` 按方块真实的底边找最低点贴地，离车身的距离按方块的角（比侧面远 2^¼ 倍）算。外观测试里圆顶平滑度的检查改为对实际扫出的网格做（带上方度）；另加一个和 `TurretToss` 自己的算法无关的落地测试：把炮塔网格的每个顶点按落地姿态变换，最低点应在地面上下 5 cm 内。

**Files:**
- Create: `src/Tank.Client/scripts/Art/Mesh/Superellipse.cs`
- Modify: `src/Tank.Client/scripts/Art/Mesh/Lathe.cs`、`scripts/Vehicles/TankLook.cs`、`TankLooks.cs`、`TankParts.cs`、`TurretToss.cs`、`src/Tank.Sim/Vehicles/TankCatalog.cs`
- Test: `tests/Tank.Art.Tests/LatheTests.cs`、`TankLookTests.cs`、`TurretTossTests.cs`、`tests/Tank.Sim.Tests/CombatSpecTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/LatheTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/LatheTests.cs b/tests/Tank.Art.Tests/LatheTests.cs
index d76e1e7..5eddcf9 100644
--- a/tests/Tank.Art.Tests/LatheTests.cs
+++ b/tests/Tank.Art.Tests/LatheTests.cs
@@ -49,6 +49,40 @@ public class LatheTests
         if (profile != Barrel) MeshAssert.NormalsPointAwayFrom(mesh, center);
     }
 
+    [Fact]
+    public void Squareness_makes_a_rounded_block_inside_its_square()
+    {
+        MeshData mesh = Lathe.Build(Puck, 72, squareness: 4f);
+        MeshAssert.ConvexSolid(mesh, new Vector3(0, 0.5f, 0));
+        var (min, max) = MeshAssert.Bounds(mesh);
+        Assert.Equal(new Vector3(-1, 0, -1), min, new Vector3EqualityTolerance(1e-4f));
+        Assert.Equal(new Vector3(1, 1, 1), max, new Vector3EqualityTolerance(1e-4f));
+        // Its corners reach past the round puck's: 2^¼ out along the diagonals.
+        float corner = mesh.Positions.Max(p => (MathF.Abs(p.X) + MathF.Abs(p.Z)) / MathF.Sqrt(2));
+        Assert.Equal(MathF.Pow(2f, 0.25f), corner, 3);
+    }
+
+    [Fact]
+    public void Square_wall_normals_are_square_to_the_wall()
+    {
+        MeshData mesh = Lathe.Build(Cylinder, 48, squareness: 4f);
+        for (int i = 0; i + 1 < 49; i++)   // the bottom wall ring: neighbours along the ring span the wall's tangent
+        {
+            Vector3 along = mesh.Positions[i + 1] - mesh.Positions[i];
+            Vector3 normal = (mesh.Normals[i] + mesh.Normals[i + 1]) / 2;
+            Assert.True(MathF.Abs(Vector3.Dot(Vector3.Normalize(along), Vector3.Normalize(normal))) < 0.05f, $"normal {i} is not square to the wall");
+            Assert.Equal(0f, mesh.Normals[i].Y, 4);
+        }
+    }
+
+    [Fact]
+    public void Squareness_two_is_the_round_lathe()
+    {
+        MeshData round = Lathe.Build(Barrel, 10), squareTwo = Lathe.Build(Barrel, 10, squareness: 2f);
+        Assert.Equal(round.Positions, squareTwo.Positions);
+        Assert.Equal(round.Normals, squareTwo.Normals);
+    }
+
     [Fact]
     public void Ring_vertices_sit_at_the_profile_radius()
     {
@@ -104,3 +138,11 @@ public class LatheTests
         return normals.Count;
     }
 }
+
+/// <summary>Component-wise comparison within a tolerance.</summary>
+internal sealed class Vector3EqualityTolerance(float tolerance) : IEqualityComparer<Vector3>
+{
+    public bool Equals(Vector3 a, Vector3 b) => MathF.Abs(a.X - b.X) <= tolerance && MathF.Abs(a.Y - b.Y) <= tolerance && MathF.Abs(a.Z - b.Z) <= tolerance;
+
+    public int GetHashCode(Vector3 v) => 0;
+}
PATCH
```

修改 `tests/Tank.Art.Tests/TankLookTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/TankLookTests.cs b/tests/Tank.Art.Tests/TankLookTests.cs
index fc1fe0f..04d9b39 100644
--- a/tests/Tank.Art.Tests/TankLookTests.cs
+++ b/tests/Tank.Art.Tests/TankLookTests.cs
@@ -100,6 +100,18 @@ public class TankLookTests
         Assert.InRange(dome[^1].Y - dome[0].Y, 1.05f, 1.25f);
     }
 
+    [Fact]
+    public void Heavy_turret_is_a_rounded_block()
+    {
+        TankLook look = TankLooks.Heavy;
+        Assert.True(look.TurretSquareness >= 3f, "squared off, not a dome");
+        float side = look.TurretProfile.Max(p => p.X);
+        Assert.True(look.TurretProfile.Count(p => p.X == side) >= 2, "straight sides");
+        var (min, max) = MeshAssert.Bounds(TankParts.Turret(look));
+        Assert.Equal(side, max.X, 3);   // the block's flat sides, not its corners, set its width
+        Assert.Equal(-side, min.X, 3);
+    }
+
     [Theory]
     [MemberData(nameof(TurretIds))]
     public void Turret_is_a_soft_closed_dome(string specId)
@@ -108,7 +120,7 @@ public class TankLookTests
         Vector2[] dome = look.TurretProfile;
         Assert.Equal(0f, dome[^1].X);                                     // closed at the top
         for (int i = 1; i < dome.Length; i++) Assert.True(dome[i].Y > dome[i - 1].Y, "the dome rises all the way up");
-        MeshData shell = Lathe.Build(dome, TankParts.DomeSides);
+        MeshData shell = Lathe.Build(dome, TankParts.DomeSides, squareness: look.TurretSquareness);   // as built, corners and all
         Assert.True(MeshAssert.MaxCreaseDegrees(shell, minY: 0.15f) <= 35f, $"dome crease {MeshAssert.MaxCreaseDegrees(shell, minY: 0.15f):F0}°");
     }
 
PATCH
```

修改 `tests/Tank.Art.Tests/TurretTossTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/TurretTossTests.cs b/tests/Tank.Art.Tests/TurretTossTests.cs
index 0f746f3..bfff018 100644
--- a/tests/Tank.Art.Tests/TurretTossTests.cs
+++ b/tests/Tank.Art.Tests/TurretTossTests.cs
@@ -61,10 +61,28 @@ public class TurretTossTests
     {
         TurretToss toss = TossOf(specId);
         var (position, rotation) = toss.At(TurretToss.FlightTime, 1.1f);
-        float lowest = position.Y + TurretToss.LowestRimPoint(rotation, toss.DomeRadius);
+        float lowest = position.Y + toss.LowestRimPoint(rotation);
         Assert.Equal(0f, lowest, 0.02f);
     }
 
+    /// <summary>
+    /// The same landing checked against the turret mesh itself rather than the rim sampling <see cref="TurretToss"/>
+    /// places it with: nested as TankModel nests it (Toss at the pivot → TurretBody, the pivot height undone → the shell),
+    /// its lowest vertex sits on the ground, within the flared lip's height.
+    /// </summary>
+    [Theory]
+    [MemberData(nameof(TurretIds))]
+    public void Landed_turret_mesh_touches_the_ground(string specId)
+    {
+        TurretToss toss = TossOf(specId);
+        var (position, rotation) = toss.At(TurretToss.FlightTime, 0.4f);
+        var undoPivot = new Vector3(0, toss.Pivot.Y, 0);
+        float lowest = float.MaxValue;
+        foreach (Vector3 p in TankParts.Turret(TankLooks.For(specId)).Positions)
+            lowest = MathF.Min(lowest, (position + Vector3.Transform(p - undoPivot, rotation)).Y);
+        Assert.InRange(lowest, -0.05f, 0.05f);
+    }
+
     [Theory]
     [MemberData(nameof(TurretIdsAndYaws))]
     public void Spins_more_than_a_full_turn_without_jumps(string specId, float startYaw)
PATCH
```

修改 `tests/Tank.Sim.Tests/CombatSpecTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/CombatSpecTests.cs b/tests/Tank.Sim.Tests/CombatSpecTests.cs
index c91ee8d..1a9145a 100644
--- a/tests/Tank.Sim.Tests/CombatSpecTests.cs
+++ b/tests/Tank.Sim.Tests/CombatSpecTests.cs
@@ -110,6 +110,27 @@ public sealed class CombatSpecTests
         Assert.True(l.HasTurret);
     }
 
+    [Fact]
+    public void Heavy_MatchesTheDesignTable()
+    {
+        TankSpec h = TankCatalog.Heavy;
+        Assert.Equal(1400, h.MaxHealth);
+        Assert.Equal(122f, h.Caliber);
+        Assert.Equal(6f, h.ReloadTime);
+        Assert.Equal(35f, h.MaxForwardSpeed * 3.6f, 1e-3f);
+        Assert.Equal(12f, h.MaxReverseSpeed * 3.6f, 1e-3f);
+        Assert.Equal(Angles.Deg(26), h.HullTraverse);
+        Assert.Equal(Angles.Deg(24), h.TurretTraverse);
+        Assert.Equal(new Armor(150, 90, 60, 30, 25), h.HullArmor.Armor);
+        Assert.Equal(new Armor(200, 120, 80, 40, 40), h.TurretArmor.Armor);
+        Assert.Equal(new ShellSpec(225, 330, 720, 0, 25), h.Ap);
+        Assert.Equal(new ShellSpec(65, 420, 576, 4, 15), h.He);
+        Assert.Equal(0.44f, h.Dispersion);
+        Assert.Equal(2.9f, h.AimTime);
+        Assert.Equal(new BloomFactors(2.5f, 2.0f, 1.2f, 3.5f), h.Bloom);
+        Assert.True(h.HasTurret);
+    }
+
     /// <summary>The 2B balance: tanks differ in the size of each hit, not in how much damage they deal over time.</summary>
     [Theory]
     [MemberData(nameof(SpecIds))]
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Lathe.Build` 没有 `squareness` 参数，`TankLooks.Heavy`、`TurretSquareness` 找不到，`LowestRimPoint` 的签名对不上。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Art/Mesh/Lathe.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Art/Mesh/Lathe.cs b/src/Tank.Client/scripts/Art/Mesh/Lathe.cs
index 5732242..a04e0e9 100644
--- a/src/Tank.Client/scripts/Art/Mesh/Lathe.cs
+++ b/src/Tank.Client/scripts/Art/Mesh/Lathe.cs
@@ -7,16 +7,19 @@ namespace Tank.Client.Art.Meshes;
 /// the right-hand side of the walk. A point with radius 0 closes that end at the axis; an end with radius &gt; 0 gets a
 /// flat cap (bottom faces −Y, top faces +Y), so the result is always closed.
 /// Neighbouring profile edges that turn by at most <c>smoothAngleDeg</c> share a normal, sharper turns stay crisp.
+/// A <c>squareness</c> above 2 sweeps the profile round a <see cref="Superellipse"/> instead of a circle: a rounded block
+/// whose sides sit at the profile's radius.
 /// </summary>
 public static class Lathe
 {
     /// <summary>Radii below this count as lying on the axis.</summary>
     private const float AxisEpsilon = 1e-5f;
 
-    public static MeshData Build(IReadOnlyList<Vector2> profile, int segments, float smoothAngleDeg = 35f)
+    public static MeshData Build(IReadOnlyList<Vector2> profile, int segments, float smoothAngleDeg = 35f, float squareness = 2f)
     {
         if (profile.Count < 2) throw new ArgumentException("A profile needs at least 2 points.", nameof(profile));
         if (segments < 3) throw new ArgumentOutOfRangeException(nameof(segments), "Need at least 3 segments.");
+        if (!(squareness >= 2f)) throw new ArgumentOutOfRangeException(nameof(squareness), "2 is round; less would pinch the corners.");
         smoothAngleDeg = Math.Clamp(smoothAngleDeg, 0f, 179.9f);
         for (int k = 0; k < profile.Count; k++)
         {
@@ -47,8 +50,8 @@ public static class Lathe
         {
             Vector2 n0 = EndNormal(edgeNormals, k, k - 1, smoothCos);
             Vector2 n1 = EndNormal(edgeNormals, k, k + 1, smoothCos);
-            int low = Ring(b, profile[k], n0, segments, (float)k / edges);
-            int high = Ring(b, profile[k + 1], n1, segments, (float)(k + 1) / edges);
+            int low = Ring(b, profile[k], n0, segments, (float)k / edges, squareness);
+            int high = Ring(b, profile[k + 1], n1, segments, (float)(k + 1) / edges, squareness);
             for (int j = 0; j < segments; j++)
             {
                 int a = low + j, bb = low + j + 1, c = high + j + 1, d = high + j;
@@ -57,8 +60,8 @@ public static class Lathe
                 else b.AddQuad(a, bb, c, d);
             }
         }
-        if (!OnAxis(profile[0])) Cap(b, profile[0], segments, top: false);
-        if (!OnAxis(profile[^1])) Cap(b, profile[^1], segments, top: true);
+        if (!OnAxis(profile[0])) Cap(b, profile[0], segments, top: false, squareness);
+        if (!OnAxis(profile[^1])) Cap(b, profile[^1], segments, top: true, squareness);
         return b.Build();
     }
 
@@ -73,21 +76,34 @@ public static class Lathe
             : normals[edge];
     }
 
-    private static int Ring(MeshBuilder b, Vector2 point, Vector2 normal, int segments, float v)
+    /// <summary>
+    /// One ring of the sweep. Off the circle the wall's normal is the section's normal tilted by the profile's: where the
+    /// section bulges out (k &gt; 1 at a corner) the same rise in the profile leans the wall less, so the profile normal's
+    /// vertical part is scaled by how far the section reaches along its own normal.
+    /// </summary>
+    private static int Ring(MeshBuilder b, Vector2 point, Vector2 normal, int segments, float v, float squareness)
     {
         int first = b.VertexCount;
         for (int j = 0; j <= segments; j++)
         {
             float angle = MathF.Tau * j / segments;
             float cos = MathF.Cos(angle), sin = -MathF.Sin(angle);
-            b.AddVertex(new Vector3(point.X * cos, point.Y, point.X * sin),
-                Vector3.Normalize(new Vector3(normal.X * cos, normal.Y, normal.X * sin)),
+            float k = 1f, nx = cos, nz = sin, rise = 1f;
+            if (squareness != 2f)
+            {
+                k = Superellipse.Radius(cos, sin, squareness);
+                Vector2 across = Superellipse.Normal(cos, sin, squareness);
+                (nx, nz) = (across.X, across.Y);
+                rise = k * (nx * cos + nz * sin);
+            }
+            b.AddVertex(new Vector3(point.X * k * cos, point.Y, point.X * k * sin),
+                Vector3.Normalize(new Vector3(normal.X * nx, normal.Y * rise, normal.X * nz)),
                 new Vector2((float)j / segments, v));
         }
         return first;
     }
 
-    private static void Cap(MeshBuilder b, Vector2 point, int segments, bool top)
+    private static void Cap(MeshBuilder b, Vector2 point, int segments, bool top, float squareness)
     {
         Vector3 normal = top ? Vector3.UnitY : -Vector3.UnitY;
         int center = b.AddVertex(new Vector3(0, point.Y, 0), normal, new Vector2(0.5f, 0.5f));
@@ -96,7 +112,8 @@ public static class Lathe
         {
             float angle = MathF.Tau * j / segments;
             float cos = MathF.Cos(angle), sin = -MathF.Sin(angle);
-            b.AddVertex(new Vector3(point.X * cos, point.Y, point.X * sin), normal, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
+            float k = squareness == 2f ? 1f : Superellipse.Radius(cos, sin, squareness);
+            b.AddVertex(new Vector3(point.X * k * cos, point.Y, point.X * k * sin), normal, new Vector2(0.5f + cos * 0.5f, 0.5f + sin * 0.5f));
         }
         for (int j = 0; j < segments; j++)
         {
PATCH
```

新建 `src/Tank.Client/scripts/Art/Mesh/Superellipse.cs`：

```csharp
using System.Numerics;

namespace Tank.Client.Art.Meshes;

/// <summary>
/// The superellipse |x|ⁿ + |z|ⁿ = 1 in polar form, the cross-section of a squarish lathe: n = 2 is the unit circle, a
/// larger n a squarer shape with flatter sides, always inside the square [−1, 1]² and touching it mid-side.
/// </summary>
public static class Superellipse
{
    /// <summary>Distance from the centre to the curve in the direction (<paramref name="cos"/>, <paramref name="sin"/>).</summary>
    public static float Radius(float cos, float sin, float n) =>
        MathF.Pow(MathF.Pow(MathF.Abs(cos), n) + MathF.Pow(MathF.Abs(sin), n), -1f / n);

    /// <summary>The curve's outward unit normal at the point in the direction (<paramref name="cos"/>, <paramref name="sin"/>).</summary>
    public static Vector2 Normal(float cos, float sin, float n) =>
        Vector2.Normalize(new Vector2(MathF.CopySign(MathF.Pow(MathF.Abs(cos), n - 1), cos), MathF.CopySign(MathF.Pow(MathF.Abs(sin), n - 1), sin)));
}
```

修改 `src/Tank.Client/scripts/Vehicles/TankLook.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankLook.cs b/src/Tank.Client/scripts/Vehicles/TankLook.cs
index eb665eb..48320eb 100644
--- a/src/Tank.Client/scripts/Vehicles/TankLook.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankLook.cs
@@ -47,6 +47,12 @@ public sealed record TankLook
     /// <summary>Dome profile (radius, height above <see cref="TurretBase"/>) for <c>Lathe</c>, bottom to top, closed at the top.</summary>
     public required SN.Vector2[] TurretProfile { get; init; }
 
+    /// <summary>
+    /// The turret's cross-section (a <c>Superellipse</c> exponent): 2 sweeps <see cref="TurretProfile"/> round into a dome,
+    /// more squares it off into a rounded block whose sides sit at the profile's radius.
+    /// </summary>
+    public float TurretSquareness { get; init; } = 2f;
+
     /// <summary>
     /// Turret-space Z of the gun's visual pivot (the mantlet centre). The barrel is shortened by the same amount, so at
     /// zero pitch the muzzle sits exactly where the sim puts it.
PATCH
```

修改 `src/Tank.Client/scripts/Vehicles/TankLooks.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankLooks.cs b/src/Tank.Client/scripts/Vehicles/TankLooks.cs
index 9766e42..a8383ad 100644
--- a/src/Tank.Client/scripts/Vehicles/TankLooks.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankLooks.cs
@@ -6,8 +6,8 @@ namespace Tank.Client.Vehicles;
 
 /// <summary>
 /// Look table, one entry per <c>TankCatalog</c> spec, all orange toys told apart by shape. The light tank is small and
-/// low with a little dome set back and a long thin gun on three road wheels; the medium tank a soap-bar hull, big dome
-/// and stubby gun.
+/// low with a little dome and a long thin gun on three road wheels; the medium tank a soap-bar hull, big dome and stubby
+/// gun; the heavy tank wide and tall, a big rounded-block turret, a fat gun, five road wheels and wide tracks.
 /// </summary>
 public static class TankLooks
 {
@@ -101,7 +101,50 @@ public static class TankLooks
         Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
     };
 
-    public static IReadOnlyList<TankLook> All { get; } = new[] { Light, Medium };
+    public static readonly TankLook Heavy = new()
+    {
+        SpecId = "heavy",
+        LowerHull = Rounded(
+            (new(-3.1f, 0.45f), 0.35f), (new(3.05f, 0.45f), 0.35f), (new(3.35f, 0.95f), 0.32f),
+            (new(3.35f, 1.8f), 0.32f), (new(-3.35f, 1.8f), 0.32f), (new(-3.35f, 1.05f), 0.32f)),
+        LowerHullWidth = 1.86f,
+        LowerHullRound = 0.3f,
+        // A tall, wide slab; the glacis dips in front of the turret like the medium's.
+        UpperHull = Rounded(
+            (new(-3.7f, 0.95f), 0.3f), (new(3.7f, 0.95f), 0.3f), (new(3.7f, 2.0f), 0.4f),
+            (new(-2.4f, 2.0f), 0.55f), (new(-3.7f, 1.72f), 0.24f)),
+        UpperHullWidth = 3.6f,
+        UpperHullRound = 0.18f,
+        TurretBase = 1.94f,
+        TurretProfile = Block(radius: 1.55f, height: 1.3f, edge: 0.45f, crown: 0.08f, lip: 0.04f, steps: 6),
+        TurretSquareness = 4f,
+        GunPivotZ = -1.57f,
+        MantletRadius = 0.34f,
+        MantletWidth = 1.2f,
+        BarrelRadius = 0.26f,
+        MuzzleRadius = 0.36f,
+        MuzzleLength = 0.5f,
+        TrackX = 1.38f,
+        TrackWidth = 0.8f,
+        TrackThickness = 0.1f,
+        LinkPitch = 0.32f,
+        Sprocket = new WheelSpot(-3.24f, 0.8f, 0.42f),
+        Idler = new WheelSpot(3.24f, 0.8f, 0.42f),
+        RoadWheelCount = 5,
+        RoadWheelRadius = 0.55f,
+        RoadWheelY = 0.65f,
+        RoadWheelSpacing = 1.12f,
+        RoadWheelWidth = 0.66f,
+        Details = new DetailPart[]
+        {
+            new(DetailKind.Headlight, new(-0.95f, 1.82f, -3.3f), new(0.46f, 0.46f, 0.22f)),
+            new(DetailKind.Headlight, new(0.95f, 1.82f, -3.3f), new(0.46f, 0.46f, 0.22f)),
+            new(DetailKind.Cupola, new(0.5f, 3.22f, 0.55f), new(0.66f, 0.24f, 0f), OnTurret: true),
+        },
+        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
+    };
+
+    public static IReadOnlyList<TankLook> All { get; } = new[] { Light, Medium, Heavy };
 
     public static TankLook For(string specId) =>
         All.FirstOrDefault(look => look.SpecId == specId)
@@ -111,6 +154,27 @@ public static class TankLooks
     private static SN.Vector2[] Rounded(params (SN.Vector2 Corner, float Radius)[] corners) =>
         Polygon2.RoundCorners(corners.Select(c => c.Corner).ToArray(), corners.Select(c => c.Radius).ToArray(), CornerSegments);
 
+    /// <summary>
+    /// Lathe profile of a rounded block's side, for a squared-off sweep: a short flared lip, straight sides up to a top
+    /// edge rounded over <paramref name="edge"/>, then a roof crowned <paramref name="crown"/> higher in the middle.
+    /// </summary>
+    private static SN.Vector2[] Block(float radius, float height, float edge, float crown, float lip, int steps)
+    {
+        var points = new List<SN.Vector2> { new(radius * 0.95f, 0f), new(radius, lip) };
+        for (int k = 0; k <= steps; k++)
+        {
+            float theta = MathF.PI / 2 * k / steps;
+            points.Add(new SN.Vector2(radius - edge + edge * MathF.Cos(theta), height - edge + edge * MathF.Sin(theta)));
+        }
+        float roof = radius - edge;
+        for (int k = 1; k <= 3; k++)
+        {
+            float r = roof * (1f - k / 3f);
+            points.Add(new SN.Vector2(r, height + crown * (1f - r * r / (roof * roof))));
+        }
+        return points.ToArray();
+    }
+
     /// <summary>
     /// Lathe profile of a full, toy-like dome: a short flared lip from 95% of <paramref name="radius"/> up to
     /// <paramref name="lip"/>, then a quarter ellipse closing at the axis.
PATCH
```

修改 `src/Tank.Client/scripts/Vehicles/TankParts.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankParts.cs b/src/Tank.Client/scripts/Vehicles/TankParts.cs
index b3a0444..43b6fb5 100644
--- a/src/Tank.Client/scripts/Vehicles/TankParts.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankParts.cs
@@ -34,10 +34,11 @@ public static class TankParts
         return Merge.Build(parts);
     }
 
-    /// <summary>Dome turret and its hatch in turret space (origin at the ring centre on the hull floor).</summary>
+    /// <summary>Dome (or rounded-block) turret and its hatch in turret space (origin at the ring centre on the hull floor).</summary>
     public static MeshData Turret(TankLook look)
     {
-        var parts = new List<(MeshData, M)> { (Lathe.Build(look.TurretProfile, DomeSides), M.CreateTranslation(0, look.TurretBase, 0)) };
+        MeshData shell = Lathe.Build(look.TurretProfile, DomeSides, squareness: look.TurretSquareness);
+        var parts = new List<(MeshData, M)> { (shell, M.CreateTranslation(0, look.TurretBase, 0)) };
         parts.AddRange(Details(look, onTurret: true));
         return Merge.Build(parts);
     }
PATCH
```

修改 `src/Tank.Client/scripts/Vehicles/TurretToss.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TurretToss.cs b/src/Tank.Client/scripts/Vehicles/TurretToss.cs
index 45ca64d..10d5a94 100644
--- a/src/Tank.Client/scripts/Vehicles/TurretToss.cs
+++ b/src/Tank.Client/scripts/Vehicles/TurretToss.cs
@@ -1,3 +1,4 @@
+using Tank.Client.Art.Meshes;
 using SN = System.Numerics;
 
 namespace Tank.Client.Vehicles;
@@ -22,15 +23,22 @@ public sealed class TurretToss
     private static readonly float LandingPitch = 8f * MathF.PI / 180f;   // gun drooping towards the ground
     private static readonly float WobbleRoll = 25f * MathF.PI / 180f;
 
+    private readonly float _baseRadius;
+    private readonly float _squareness;
+
     /// <param name="turretOffset">Turret ring centre ahead of the hull origin (<c>TankSpec.TurretOffset</c>).</param>
     public TurretToss(TankLook look, float turretOffset)
     {
-        DomeRadius = look.TurretProfile.Max(p => p.X);
+        _baseRadius = look.TurretProfile.Max(p => p.X);
+        _squareness = look.TurretSquareness;
+        float diagonal = MathF.Sqrt(0.5f);
+        DomeRadius = _baseRadius * Superellipse.Radius(diagonal, diagonal, _squareness);
         Pivot = new SN.Vector3(0, look.TurretBase + PivotAboveRing, -turretOffset);
-        float rest = -LowestRimPoint(Landed(0f), DomeRadius);
+        float rest = -LowestRimPoint(Landed(0f));
         Landing = new SN.Vector3(look.UpperHullWidth / 2 + DomeRadius + Clearance, rest, -turretOffset + LandBehind);
     }
 
+    /// <summary>How far the turret's base reaches from its axis at most (a rounded block's corners reach past its sides).</summary>
     public float DomeRadius { get; }
 
     /// <summary>The pivot's place on an intact tank.</summary>
@@ -63,14 +71,15 @@ public sealed class TurretToss
     /// <summary>The landed tilt without the final spin (yaw doesn't change heights).</summary>
     private static SN.Quaternion Landed(float yaw) => SN.Quaternion.CreateFromYawPitchRoll(yaw, -LandingPitch, LandingRoll);
 
-    /// <summary>Lowest point of the dome's base rim, relative to the pivot, for a turret turned by <paramref name="rotation"/>.</summary>
-    public static float LowestRimPoint(SN.Quaternion rotation, float radius)
+    /// <summary>Lowest point of the turret's base rim, relative to the pivot, for a turret turned by <paramref name="rotation"/>.</summary>
+    public float LowestRimPoint(SN.Quaternion rotation)
     {
         float lowest = float.MaxValue;
         for (int i = 0; i < 72; i++)
         {
-            float a = MathF.Tau * i / 72;
-            var rim = new SN.Vector3(radius * MathF.Cos(a), -PivotAboveRing, radius * MathF.Sin(a));
+            float a = MathF.Tau * i / 72, cos = MathF.Cos(a), sin = MathF.Sin(a);
+            float radius = _baseRadius * Superellipse.Radius(cos, sin, _squareness);
+            var rim = new SN.Vector3(radius * cos, -PivotAboveRing, radius * sin);
             lowest = MathF.Min(lowest, SN.Vector3.Transform(rim, rotation).Y);
         }
         return lowest;
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index c487bf8..8985067 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -75,8 +75,40 @@ public static class TankCatalog
         Bloom = new BloomFactors(Moving: 2.0f, HullTurn: 1.5f, TurretTurn: 1.0f, Fire: 3.0f),
     };
 
+    public static readonly TankSpec Heavy = new()
+    {
+        Id = "heavy",
+        Name = "重型坦克",
+        HullLength = 7.5f,
+        HullWidth = 3.6f,
+        MaxForwardSpeed = 35 * Kmh,
+        MaxReverseSpeed = 12 * Kmh,
+        Acceleration = 2.8f,
+        Braking = 7f,
+        HullTraverse = Angles.Deg(26),
+        TurretTraverse = Angles.Deg(24),
+        GunArc = TankSpec.FullTraverse,
+        GunPitchSpeed = Angles.Deg(22),
+        GunDepression = Angles.Deg(6),
+        GunElevation = Angles.Deg(18),
+        MaxClimb = Angles.Deg(32),
+        TrunnionHeight = 2.55f,
+        TurretOffset = 0.2f,
+        GunLength = 3.8f,
+        ReloadTime = 6f,
+        MaxHealth = 1400,
+        Caliber = 122,
+        HullArmor = new ArmorBox(Width: 3.6f, Length: 7.2f, Bottom: 0.35f, Top: 2.0f, new Armor(Front: 150, Side: 90, Rear: 60, Top: 30, Bottom: 25)),
+        TurretArmor = new ArmorBox(Width: 3.1f, Length: 3.1f, Bottom: 1.94f, Top: 3.3f, new Armor(Front: 200, Side: 120, Rear: 80, Top: 40, Bottom: 40)),
+        Ap = new ShellSpec(Penetration: 225, Damage: 330, Speed: 720, SplashRadius: 0, Capacity: 25),
+        He = new ShellSpec(Penetration: 65, Damage: 420, Speed: 720 * 0.8f, SplashRadius: 4, Capacity: 15),
+        Dispersion = 0.44f,
+        AimTime = 2.9f,
+        Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 1.2f, Fire: 3.5f),
+    };
+
     /// <summary>Light to heavy; F1–F4 in the sandbox pick them in this order.</summary>
-    public static IReadOnlyList<TankSpec> All { get; } = new[] { Light, Medium };
+    public static IReadOnlyList<TankSpec> All { get; } = new[] { Light, Medium, Heavy };
 
     /// <summary>The spec named <paramref name="id"/>.</summary>
     /// <exception cref="KeyNotFoundException">No such tank; the message lists the known names.</exception>
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 307、Art 278。

- [ ] **Step 5：截图**

```bash
importall
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
shot t6-heavy-close close --tank heavy
shot t6-heavy-side side --tank heavy
shot t6-medium-close close --tank medium
```

Expected：
- 面板上方是“重型坦克”，血量 1400，穿甲弹 25 发；
- 炮塔是平侧面、圆角的方块，不是圆顶；炮管粗，炮口是大的深色圆头；
- 侧面看每边 5 个负重轮加两个端轮，履带比中坦宽；
- 中坦（`t6-medium-close`）的圆顶炮塔和以前一样。

- [ ] **Step 6：提交**

```bash
git add src tests
git commit -m "feat: heavy tank — 6 s reload, 330 damage, rounded-block turret on a wide, tall hull"
```

---

### Task 7：TD

设计 §2、§3.2 的 TD 数值和 §4.1、§4.2 的外观与击毁表现：低矮的楔形车身，没有炮塔，长炮管从斜前装甲正中的圆球炮盾伸出，只有炮盾和炮管左右转；4 对负重轮。几处和设计数值不同的地方见“与设计不同、但有意为之”：炮耳（`TurretOffset`）在前装甲上，战斗室装甲盒挂在车体原点上。

- 仿真：`TankPose.TurretBox` 对没有炮塔的车，盒子以车体原点为中心（和车体盒一样），`TurretOffset` 只决定炮耳的位置。
- 外观：`TankLook.TurretProfile` 为空表示没有炮塔，`HasTurret` 由它推出；`TurretBase` 对 TD 是炮耳高度（车体点头时炮盾跟着这一点移动）。`GunPivotZ` 为 0，炮盾绕自己的球心转，转动时只有炮管扫过。
- 模型：没有炮塔时不建炮塔外壳，不建 `TurretToss`，击毁时不抛炮塔；黑烟从战斗室顶的中心冒出（`TurretRing`）。
- 测试：外观检查里“炮压到最低时不碰车体”对 TD 只查炮盾以外的炮管（炮盾本来就嵌在前装甲里）；另有一个测试确认炮盾一半露在前装甲外面。车型数据检查补上设计 §3.1 要求的“炮口在车体之外”。

**Files:**
- Modify: `src/Tank.Sim/Vehicles/TankSpec.cs`、`TankCatalog.cs`、`TankPose.cs`、`src/Tank.Client/scripts/Vehicles/TankLook.cs`、`TankLooks.cs`、`TankModel.cs`
- Test: `tests/Tank.Sim.Tests/CombatSpecTests.cs`、`TankPoseTests.cs`、`tests/Tank.Art.Tests/TankLookTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/TankLookTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/TankLookTests.cs b/tests/Tank.Art.Tests/TankLookTests.cs
index 04d9b39..94979bc 100644
--- a/tests/Tank.Art.Tests/TankLookTests.cs
+++ b/tests/Tank.Art.Tests/TankLookTests.cs
@@ -22,6 +22,13 @@ public class TankLookTests
         Assert.Equal(specId, TankLooks.For(specId).SpecId);
     }
 
+    [Theory]
+    [MemberData(nameof(SpecIds))]
+    public void Look_has_a_turret_exactly_when_the_tank_does(string specId)
+    {
+        Assert.Equal(TankCatalog.ById(specId).HasTurret, TankLooks.For(specId).HasTurret);
+    }
+
     [Theory]
     [MemberData(nameof(SpecIds))]
     public void Every_part_is_a_closed_outward_solid(string specId)
@@ -31,7 +38,6 @@ public class TankLookTests
         var parts = new Dictionary<string, MeshData>
         {
             ["hull"] = TankParts.Hull(look),
-            ["turret"] = TankParts.Turret(look),
             ["mantlet"] = TankParts.Mantlet(look),
             ["barrel"] = TankParts.Barrel(look, BarrelLength(look, spec)),
             ["muzzle"] = TankParts.Muzzle(look, BarrelLength(look, spec)),
@@ -40,6 +46,7 @@ public class TankLookTests
             ["left sprocket"] = TankParts.Sprocket(look.Sprocket.Radius, look.RoadWheelWidth, left: true),
             ["track link"] = TankParts.TrackLink(look),
         };
+        if (look.HasTurret) parts["turret"] = TankParts.Turret(look);
         foreach (var (name, mesh) in parts)
         {
             MeshAssert.Closed(mesh);
@@ -68,7 +75,7 @@ public class TankLookTests
             float z = belt.Sample(belt.Length * i / 400f).Position.X;
             Assert.InRange(MathF.Abs(z) + look.TrackThickness / 2, 0, spec.HullLength / 2 + Slack);
         }
-        Assert.True(look.TurretProfile.Max(p => p.X) <= spec.HullWidth / 2, "turret wider than the hull");
+        if (look.HasTurret) Assert.True(look.TurretProfile.Max(p => p.X) <= spec.HullWidth / 2, "turret wider than the hull");
     }
 
     [Theory]
@@ -271,10 +278,12 @@ public class TankLookTests
 
     /// <summary>
     /// Depressing the gun fully while the hull rocks nose-up (a shot's nod) must not push the barrel, bulb or mantlet
-    /// into the hull top. The hull nods about (0, RoadWheelY, 0); the turret is not tilted, it only follows the ring.
+    /// into the hull top. The hull nods about (0, RoadWheelY, 0); the turret is not tilted, it only follows the ring. A
+    /// casemate's ball mount, and the stub of barrel inside the ball, sit in the front plate by design: only the barrel
+    /// beyond the ball is checked.
     /// </summary>
     [Theory]
-    [MemberData(nameof(TurretIds))]
+    [MemberData(nameof(SpecIds))]
     public void Gun_and_mantlet_clear_the_hull_top_at_full_depression_while_the_hull_nods(string specId)
     {
         TankLook look = TankLooks.For(specId);
@@ -292,8 +301,9 @@ public class TankLookTests
             {
                 float deg = down * 180f / MathF.PI;
                 Quaternion pitch = Quaternion.CreateFromAxisAngle(Vector3.UnitX, down);
-                var undersides = new List<(Vector3 Point, string Name)> { (gunPivot - new Vector3(0, look.MantletRadius, 0), "mantlet") };
-                for (float d = 0; d <= length; d += 0.05f)
+                var undersides = new List<(Vector3 Point, string Name)>();
+                if (look.HasTurret) undersides.Add((gunPivot - new Vector3(0, look.MantletRadius, 0), "mantlet"));
+                for (float d = look.HasTurret ? 0f : look.MantletRadius; d <= length; d += 0.05f)
                 {
                     float radius = d >= length - look.MuzzleLength ? look.MuzzleRadius : look.BarrelRadius;
                     undersides.Add((gunPivot + Vector3.Transform(new Vector3(0, -radius, -d), pitch), d >= length - look.MuzzleLength ? "bulb" : "barrel"));
@@ -309,6 +319,19 @@ public class TankLookTests
         }
     }
 
+    /// <summary>The tank destroyer's ball sits half out of the sloped front plate, at every pitch and across its arc.</summary>
+    [Fact]
+    public void Td_ball_mount_sits_half_out_of_the_front_plate()
+    {
+        TankLook look = TankLooks.Td;
+        TankSpec spec = TankCatalog.Td;
+        Assert.Equal(0f, look.GunPivotZ);   // the ball turns about its own centre, so turning moves nothing but the barrel
+        float z = -spec.TurretOffset, y = spec.TrunnionHeight, r = look.MantletRadius;
+        Assert.True(y > UpperEdge(look.UpperHull, z - r), "the ball's front is buried in the plate");
+        Assert.True(y < UpperEdge(look.UpperHull, z + r) - r * 0.5f, "the ball's back pokes out of the casemate");
+        Assert.InRange(UpperEdge(look.UpperHull, z) - y, -r * 0.5f, r * 0.5f);   // the plate runs through the ball's middle
+    }
+
     [Theory]
     [MemberData(nameof(TurretIds))]
     public void Turret_rests_on_the_hull_roof_and_the_mantlet_clears_it(string specId)
PATCH
```

修改 `tests/Tank.Sim.Tests/CombatSpecTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/CombatSpecTests.cs b/tests/Tank.Sim.Tests/CombatSpecTests.cs
index 1a9145a..fa4a726 100644
--- a/tests/Tank.Sim.Tests/CombatSpecTests.cs
+++ b/tests/Tank.Sim.Tests/CombatSpecTests.cs
@@ -45,7 +45,10 @@ public sealed class CombatSpecTests
         TankSpec s = Get(id);
         Assert.True(s.HullArmor.Width <= s.HullWidth && s.HullArmor.Length <= s.HullLength, $"{id}: hull box sticks out");
         Assert.True(s.TurretArmor.Width <= s.HullWidth, $"{id}: turret box wider than the hull");
-        Assert.True(s.TurretOffset + s.TurretArmor.Length / 2 <= s.HullLength / 2, $"{id}: turret box past the nose");
+        float anchor = s.HasTurret ? s.TurretOffset : 0f;   // a casemate box hangs off the hull origin
+        Assert.True(anchor + s.TurretArmor.Length / 2 <= s.HullLength / 2, $"{id}: turret box past the nose");
+        Assert.True(s.TurretOffset < s.HullLength / 2, $"{id}: gun pivot past the nose");
+        Assert.True(s.TurretOffset + s.GunLength > s.HullLength / 2, $"{id}: muzzle inside the hull");
         Assert.True(s.HullArmor.Bottom >= 0, $"{id}: hull box under the ground");
         Assert.True(s.TurretArmor.Bottom > s.HullArmor.Bottom && s.TurretArmor.Top > s.HullArmor.Top, $"{id}: turret box not above the hull box");
         Assert.True(s.TrunnionHeight > s.TurretArmor.Bottom && s.TrunnionHeight < s.TurretArmor.Top, $"{id}: gun pivot outside the turret box");
@@ -131,13 +134,35 @@ public sealed class CombatSpecTests
         Assert.True(h.HasTurret);
     }
 
+    [Fact]
+    public void Td_MatchesTheDesignTable()
+    {
+        TankSpec td = TankCatalog.Td;
+        Assert.Equal(950, td.MaxHealth);
+        Assert.Equal(105f, td.Caliber);
+        Assert.Equal(6.5f, td.ReloadTime);
+        Assert.Equal(40f, td.MaxForwardSpeed * 3.6f, 1e-3f);
+        Assert.Equal(15f, td.MaxReverseSpeed * 3.6f, 1e-3f);
+        Assert.Equal(Angles.Deg(32), td.HullTraverse);
+        Assert.Equal(Angles.Deg(30), td.TurretTraverse);
+        Assert.Equal(Angles.Deg(12), td.GunArc);
+        Assert.False(td.HasTurret);
+        Assert.Equal(new Armor(140, 50, 35, 20, 20), td.HullArmor.Armor);
+        Assert.Equal(new Armor(140, 50, 35, 20, 20), td.TurretArmor.Armor);
+        Assert.Equal(new ShellSpec(250, 380, 850, 0, 30), td.Ap);
+        Assert.Equal(new ShellSpec(55, 480, 680, 3.5f, 12), td.He);
+        Assert.Equal(0.32f, td.Dispersion);
+        Assert.Equal(1.9f, td.AimTime);
+        Assert.Equal(new BloomFactors(2.5f, 2.0f, 0.8f, 3.0f), td.Bloom);
+    }
+
     /// <summary>The 2B balance: tanks differ in the size of each hit, not in how much damage they deal over time.</summary>
     [Theory]
     [MemberData(nameof(SpecIds))]
     public void EveryTank_Deals52To58ApDamageASecond(string id)
     {
         TankSpec s = Get(id);
-        Assert.InRange(s.Ap.Damage / s.ReloadTime, 52f, 58f);
+        Assert.InRange(MathF.Round(s.Ap.Damage / s.ReloadTime), 52f, 58f);   // the design table's whole numbers (TD: 58.46)
     }
 
     [Fact]
PATCH
```

修改 `tests/Tank.Sim.Tests/TankPoseTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TankPoseTests.cs b/tests/Tank.Sim.Tests/TankPoseTests.cs
index 1b21cf8..cc3a43f 100644
--- a/tests/Tank.Sim.Tests/TankPoseTests.cs
+++ b/tests/Tank.Sim.Tests/TankPoseTests.cs
@@ -35,6 +35,18 @@ public sealed class TankPoseTests
         Assert.NotEqual(pose.TurretBox(Spec, 0f), pose.TurretBox(Spec, 0.2f));
     }
 
+    [Fact]
+    public void CasemateBox_IsCentredOverTheHullOrigin_NotTheGunPivot()
+    {
+        TankSpec td = TankCatalog.Td;
+        TankPose pose = PoseOn(TestWorld.Flat(), yaw: 0f);
+        OrientedBox box = pose.TurretBox(td, 0f);
+        Assert.Equal(100f, box.Center.X, 1e-4f);
+        Assert.Equal(100f, box.Center.Z, 1e-4f);
+        Assert.Equal((td.TurretArmor.Bottom + td.TurretArmor.Top) / 2, box.Center.Y, 1e-4f);
+        Assert.Equal(-td.TurretOffset, pose.Trunnion(td).Z - 100f, 1e-4f);   // the gun still pivots on the front plate
+    }
+
     [Fact]
     public void FlatGround_SitsOnSurface_FacingYaw()
     {
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -3`
Expected: 编译失败，`TankCatalog.Td` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Vehicles/TankLook.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankLook.cs b/src/Tank.Client/scripts/Vehicles/TankLook.cs
index 48320eb..eb07406 100644
--- a/src/Tank.Client/scripts/Vehicles/TankLook.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankLook.cs
@@ -41,12 +41,20 @@ public sealed record TankLook
     public required float LowerHullRound { get; init; }
     public required float UpperHullRound { get; init; }
 
-    /// <summary>Hull-space height of the turret's flat bottom (a little below the roof, so no seam shows).</summary>
+    /// <summary>
+    /// Hull-space height of the turret's flat bottom (a little below the roof, so no seam shows). Without a turret, the
+    /// height where the gun mount rides the hull's nod (the trunnion's).
+    /// </summary>
     public required float TurretBase { get; init; }
 
-    /// <summary>Dome profile (radius, height above <see cref="TurretBase"/>) for <c>Lathe</c>, bottom to top, closed at the top.</summary>
+    /// <summary>
+    /// Dome profile (radius, height above <see cref="TurretBase"/>) for <c>Lathe</c>, bottom to top, closed at the top;
+    /// empty for a casemate (no turret: the upper hull is the casemate and only the mantlet and barrel turn).
+    /// </summary>
     public required SN.Vector2[] TurretProfile { get; init; }
 
+    public bool HasTurret => TurretProfile.Length > 0;
+
     /// <summary>
     /// The turret's cross-section (a <c>Superellipse</c> exponent): 2 sweeps <see cref="TurretProfile"/> round into a dome,
     /// more squares it off into a rounded block whose sides sit at the profile's radius.
PATCH
```

修改 `src/Tank.Client/scripts/Vehicles/TankLooks.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankLooks.cs b/src/Tank.Client/scripts/Vehicles/TankLooks.cs
index a8383ad..c872003 100644
--- a/src/Tank.Client/scripts/Vehicles/TankLooks.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankLooks.cs
@@ -7,7 +7,8 @@ namespace Tank.Client.Vehicles;
 /// <summary>
 /// Look table, one entry per <c>TankCatalog</c> spec, all orange toys told apart by shape. The light tank is small and
 /// low with a little dome and a long thin gun on three road wheels; the medium tank a soap-bar hull, big dome and stubby
-/// gun; the heavy tank wide and tall, a big rounded-block turret, a fat gun, five road wheels and wide tracks.
+/// gun; the heavy tank wide and tall, a big rounded-block turret, a fat gun, five road wheels and wide tracks; the tank
+/// destroyer a low wedge with no turret, its long gun in a ball mount on the sloped front plate.
 /// </summary>
 public static class TankLooks
 {
@@ -144,7 +145,50 @@ public static class TankLooks
         Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
     };
 
-    public static IReadOnlyList<TankLook> All { get; } = new[] { Light, Medium, Heavy };
+    public static readonly TankLook Td = new()
+    {
+        SpecId = "td",
+        LowerHull = Rounded(
+            (new(-2.9f, 0.42f), 0.3f), (new(2.85f, 0.42f), 0.3f), (new(3.15f, 0.85f), 0.3f),
+            (new(3.15f, 1.3f), 0.3f), (new(-3.15f, 1.3f), 0.2f), (new(-3.15f, 0.9f), 0.2f)),
+        LowerHullWidth = 1.84f,
+        LowerHullRound = 0.18f,
+        // The casemate: a wedge whose sloped front plate holds the ball mount, sloping away fast enough in front of it
+        // that the fully depressed barrel clears it.
+        UpperHull = Rounded(
+            (new(-3.45f, 0.92f), 0.22f), (new(3.45f, 0.92f), 0.28f), (new(3.45f, 1.55f), 0.3f),
+            (new(2.4f, 2.15f), 0.45f), (new(-2.4f, 2.15f), 0.4f), (new(-3.45f, 1.35f), 0.2f)),
+        UpperHullWidth = 3.3f,
+        UpperHullRound = 0.16f,
+        TurretBase = 1.85f,
+        TurretProfile = Array.Empty<SN.Vector2>(),
+        GunPivotZ = 0f,              // the ball turns about its own centre
+        MantletRadius = 0.36f,
+        MantletWidth = 0.76f,        // all but a ball
+        BarrelRadius = 0.18f,
+        MuzzleRadius = 0.3f,
+        MuzzleLength = 0.44f,
+        TrackX = 1.3f,
+        TrackWidth = 0.66f,
+        TrackThickness = 0.08f,
+        LinkPitch = 0.28f,
+        Sprocket = new WheelSpot(-2.95f, 0.68f, 0.44f),
+        Idler = new WheelSpot(2.95f, 0.68f, 0.44f),
+        RoadWheelCount = 4,
+        RoadWheelRadius = 0.52f,
+        RoadWheelY = 0.6f,
+        RoadWheelSpacing = 1.16f,
+        RoadWheelWidth = 0.5f,
+        Details = new DetailPart[]
+        {
+            new(DetailKind.Headlight, new(-1.0f, 1.18f, -3.38f), new(0.36f, 0.36f, 0.2f)),
+            new(DetailKind.Headlight, new(1.0f, 1.18f, -3.38f), new(0.36f, 0.36f, 0.2f)),
+            new(DetailKind.Cupola, new(-0.5f, 2.1f, 0.8f), new(0.56f, 0.2f, 0f)),   // on the casemate roof
+        },
+        Paint = new TankPaint(Palette.TankBody, Palette.Track, Palette.TankHub),
+    };
+
+    public static IReadOnlyList<TankLook> All { get; } = new[] { Light, Medium, Heavy, Td };
 
     public static TankLook For(string specId) =>
         All.FirstOrDefault(look => look.SpecId == specId)
PATCH
```

修改 `src/Tank.Client/scripts/Vehicles/TankModel.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankModel.cs b/src/Tank.Client/scripts/Vehicles/TankModel.cs
index 9d3259a..2a30b3e 100644
--- a/src/Tank.Client/scripts/Vehicles/TankModel.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankModel.cs
@@ -12,6 +12,8 @@ namespace Tank.Client.Vehicles;
 /// Body (visual nod) → Hull; Turret (a sibling of Body, so nod never tilts the gun) → Toss (pivot in the dome, moved only
 /// when the tank is wrecked) → TurretBody → TurretShell, Gun → Mantlet, Barrel (recoil: tube and bulb), Muzzle;
 /// Running gear → Wheels, Tracks. A wreck chars black and throws its turret (<see cref="TurretToss"/>) until <see cref="Restore"/>.
+/// A casemate tank (no turret) keeps the same nodes, but "Turret" carries only the gun, which turns in its ball mount,
+/// and nothing is thrown.
 /// </summary>
 public partial class TankModel : Node3D
 {
@@ -42,7 +44,7 @@ public partial class TankModel : Node3D
     private Vector3 _nodPivot;
     private Vector3 _turretBase;
     private TankPaint _paint;
-    private TurretToss _tossPath = null!;
+    private TurretToss? _tossPath;   // null without a turret
     private float _wreckTime = -1f;   // seconds since the kill; negative while intact
     private float _tossYaw;
     private float _charShown;
@@ -52,8 +54,8 @@ public partial class TankModel : Node3D
     /// <summary>Barrel tip in world space, for muzzle effects.</summary>
     public Transform3D MuzzleTransform => _muzzle.GlobalTransform;
 
-    /// <summary>Middle of the turret ring on the hull roof, in hull space (where a wreck's smoke pours out).</summary>
-    public Vector3 TurretRing => _turretBase + Vector3.Up * _look.TurretBase;
+    /// <summary>Middle of the turret ring on the hull roof (or of a casemate's roof), in hull space: where a wreck's smoke pours out.</summary>
+    public Vector3 TurretRing => _look.HasTurret ? _turretBase + Vector3.Up * _look.TurretBase : Vector3.Up * _look.UpperHull.Max(p => p.Y);
 
     /// <param name="paint">Overrides the look's paint (the practice targets are blue-grey).</param>
     public void Build(TankSpec spec, TankPaint? paint = null)
@@ -61,7 +63,7 @@ public partial class TankModel : Node3D
         _look = TankLooks.For(spec.Id);
         _paint = paint ?? _look.Paint;
         Animator = new TankAnimator(_look.TrackX);
-        _tossPath = new TurretToss(_look, spec.TurretOffset);
+        _tossPath = _look.HasTurret ? new TurretToss(_look, spec.TurretOffset) : null;
         _nodPivot = new Vector3(0, _look.RoadWheelY, 0);
         ShaderMaterial paintMaterial = ToonMaterial(PaintMode);
         // Same paint on the gun; its origin is the trunnion, so lift it by that height for the bottom-to-top shading.
@@ -74,12 +76,13 @@ public partial class TankModel : Node3D
         _turret.Position = _turretBase;
         AddChild(_turret); // sibling of Body: the hull nods under it, but the gun line stays exactly the sim's
         // The toss pivot sits in the dome; TurretBody undoes its height, so intact the turret is exactly where it was.
-        float pivotHeight = _tossPath.Pivot.Y;
+        float pivotHeight = _tossPath?.Pivot.Y ?? 0f;
         _toss.Position = new Vector3(0, pivotHeight, 0);
         _turret.AddChild(_toss);
         _turretBody.Position = new Vector3(0, -pivotHeight, 0);
         _toss.AddChild(_turretBody);
-        _turretBody.AddChild(new MeshInstance3D { Name = "TurretShell", Mesh = TankParts.Turret(_look).ToArrayMesh(), MaterialOverride = paintMaterial });
+        if (_look.HasTurret)
+            _turretBody.AddChild(new MeshInstance3D { Name = "TurretShell", Mesh = TankParts.Turret(_look).ToArrayMesh(), MaterialOverride = paintMaterial });
 
         _gun.Position = new Vector3(0, spec.TrunnionHeight, _look.GunPivotZ);
         _turretBody.AddChild(_gun);
@@ -99,7 +102,7 @@ public partial class TankModel : Node3D
 
     public bool IsWrecked => _wreckTime >= 0f;
 
-    /// <summary>Knocks the tank out: the turret flies off and the paint chars black over <see cref="CharSeconds"/>.</summary>
+    /// <summary>Knocks the tank out: the turret (if any) flies off and the paint chars black over <see cref="CharSeconds"/>.</summary>
     public void Wreck(float turretYaw)
     {
         _wreckTime = 0f;
@@ -110,7 +113,7 @@ public partial class TankModel : Node3D
     public void Restore()
     {
         _wreckTime = -1f;
-        _toss.Transform = new Transform3D(Basis.Identity, new Vector3(0, _tossPath.Pivot.Y, 0));
+        _toss.Transform = new Transform3D(Basis.Identity, new Vector3(0, _tossPath?.Pivot.Y ?? 0f, 0));
         SetChar(0f);
     }
 
@@ -152,9 +155,12 @@ public partial class TankModel : Node3D
     private void AnimateWreck(float delta)
     {
         _wreckTime += delta;
-        var (position, rotation) = _tossPath.At(_wreckTime, _tossYaw);
-        _turret.Transform = Transform3D.Identity;   // the toss path already includes the turret's yaw and ring offset
-        _toss.Transform = new Transform3D(new Basis(rotation.ToGodot()), position.ToGodot());
+        if (_tossPath is not null)
+        {
+            var (position, rotation) = _tossPath.At(_wreckTime, _tossYaw);
+            _turret.Transform = Transform3D.Identity;   // the toss path already includes the turret's yaw and ring offset
+            _toss.Transform = new Transform3D(new Basis(rotation.ToGodot()), position.ToGodot());
+        }
         SetChar(Mathf.Clamp(_wreckTime / CharSeconds, 0f, 1f));
     }
 
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index 8985067..f8b44a9 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -107,8 +107,44 @@ public static class TankCatalog
         Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 1.2f, Fire: 3.5f),
     };
 
-    /// <summary>Light to heavy; F1–F4 in the sandbox pick them in this order.</summary>
-    public static IReadOnlyList<TankSpec> All { get; } = new[] { Light, Medium, Heavy };
+    /// <summary>
+    /// The tank destroyer: no turret. Its gun sits in a ball mount on the casemate's front plate (<c>TurretOffset</c> is
+    /// that pivot) and turns 12° each way; the casemate box spans most of the hull (see <c>TankSpec.TurretArmor</c>).
+    /// </summary>
+    public static readonly TankSpec Td = new()
+    {
+        Id = "td",
+        Name = "坦克歼击车",
+        HullLength = 7.0f,
+        HullWidth = 3.3f,
+        MaxForwardSpeed = 40 * Kmh,
+        MaxReverseSpeed = 15 * Kmh,
+        Acceleration = 3.5f,
+        Braking = 8f,
+        HullTraverse = Angles.Deg(32),
+        TurretTraverse = Angles.Deg(30),
+        GunArc = Angles.Deg(12),
+        GunPitchSpeed = Angles.Deg(25),
+        GunDepression = Angles.Deg(6),
+        GunElevation = Angles.Deg(15),
+        MaxClimb = Angles.Deg(32),
+        TrunnionHeight = 1.85f,
+        TurretOffset = 2.8f,
+        GunLength = 4.1f,
+        ReloadTime = 6.5f,
+        MaxHealth = 950,
+        Caliber = 105,
+        HullArmor = new ArmorBox(Width: 3.3f, Length: 6.8f, Bottom: 0.3f, Top: 1.3f, new Armor(Front: 140, Side: 50, Rear: 35, Top: 20, Bottom: 20)),
+        TurretArmor = new ArmorBox(Width: 3.0f, Length: 5.8f, Bottom: 1.25f, Top: 2.2f, new Armor(Front: 140, Side: 50, Rear: 35, Top: 20, Bottom: 20)),
+        Ap = new ShellSpec(Penetration: 250, Damage: 380, Speed: 850, SplashRadius: 0, Capacity: 30),
+        He = new ShellSpec(Penetration: 55, Damage: 480, Speed: 850 * 0.8f, SplashRadius: 3.5f, Capacity: 12),
+        Dispersion = 0.32f,
+        AimTime = 1.9f,
+        Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 0.8f, Fire: 3.0f),
+    };
+
+    /// <summary>Light to heavy, then the tank destroyer; F1–F4 in the sandbox pick them in this order.</summary>
+    public static IReadOnlyList<TankSpec> All { get; } = new[] { Light, Medium, Heavy, Td };
 
     /// <summary>The spec named <paramref name="id"/>.</summary>
     /// <exception cref="KeyNotFoundException">No such tank; the message lists the known names.</exception>
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankPose.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankPose.cs b/src/Tank.Sim/Vehicles/TankPose.cs
index 191fbf6..c8571dc 100644
--- a/src/Tank.Sim/Vehicles/TankPose.cs
+++ b/src/Tank.Sim/Vehicles/TankPose.cs
@@ -47,13 +47,14 @@ public readonly record struct TankPose(Vector3 Position, Quaternion Rotation)
 
     /// <summary>
     /// The turret's armour box in world space: centred over the turret ring, tilted with the hull, turned by
-    /// <paramref name="turretYaw"/>. A casemate doesn't turn with its gun.
+    /// <paramref name="turretYaw"/>. A casemate is part of the hull: centred over the hull origin and never turning.
     /// </summary>
     public OrientedBox TurretBox(TankSpec spec, float turretYaw)
     {
+        if (!spec.HasTurret)
+            return new OrientedBox(Position + Vector3.Transform(spec.TurretArmor.LocalCenter, Rotation), spec.TurretArmor.HalfExtents, Rotation);
         Vector3 ring = new(0, 0, -spec.TurretOffset);
-        float yaw = spec.HasTurret ? turretYaw : 0f;
-        Quaternion rotation = Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw);
+        Quaternion rotation = Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, turretYaw);
         return new OrientedBox(Position + Vector3.Transform(ring + spec.TurretArmor.LocalCenter, Rotation), spec.TurretArmor.HalfExtents, rotation);
     }
 
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankSpec.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankSpec.cs b/src/Tank.Sim/Vehicles/TankSpec.cs
index 0ea51ac..f2557f3 100644
--- a/src/Tank.Sim/Vehicles/TankSpec.cs
+++ b/src/Tank.Sim/Vehicles/TankSpec.cs
@@ -38,7 +38,7 @@ public sealed record TankSpec
     /// <summary>Gun pivot height above the hull origin.</summary>
     public required float TrunnionHeight { get; init; }
 
-    /// <summary>Turret ring centre ahead (+) of the hull centre.</summary>
+    /// <summary>Turret ring centre ahead (+) of the hull centre; for a casemate gun, its pivot.</summary>
     public required float TurretOffset { get; init; }
 
     public required float GunLength { get; init; }
@@ -54,7 +54,8 @@ public sealed record TankSpec
 
     /// <summary>
     /// Turret hit box, anchored at the turret ring (<see cref="TurretOffset"/> ahead of the hull origin); turns with the
-    /// turret. Without a turret it is the casemate, fixed to the hull.
+    /// turret. Without a turret it is the casemate, part of the hull: anchored at the hull origin like the hull box, and
+    /// <see cref="TurretOffset"/> places only the gun's pivot.
     /// </summary>
     public required ArmorBox TurretArmor { get; init; }
 
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 313、Art 297。

- [ ] **Step 5：截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
shot t7-td-close close --tank td
shot t7-td-side side --tank td
shot t7-td-front front --tank td
```

Expected：
- 面板上方是“坦克歼击车”，血量 950，穿甲弹 30、高爆弹 12；
- 没有炮塔：低矮的楔形车身，前装甲斜着向上，正中有个圆球炮盾，长炮管从球里伸出，伸出车头很远；
- 侧面看每边 4 个负重轮加两个端轮；
- 炮管和前装甲之间没有穿模。

- [ ] **Step 6：提交**

```bash
git add src tests
git commit -m "feat: tank destroyer — 6.5 s reload, 380 damage, a low casemate with a ball-mounted gun turning 12° each way"
```

---

### Task 8：靶车四种车混放、截图用的演示

设计 §3.4：`TargetSpot` 带上车型，6 个位置和朝向不变：正面中坦（打靶演示仍然打它）、侧面重坦、45° 轻坦、车尾 TD、半躲在石头后的中坦、远处重坦。靶车测试按每辆车自己的尺寸检查，“半躲”的视线检查也按各自车宽。客户端 `TargetRange` 按位置上的车型建靶车。

另外为验收截图加三个演示（设计 §5.2 要的几张图），并让 `target` 机位对准演示打的那辆靶车：
- `nopen-heavy`：停在远处那辆重坦（5 号）正前方 52 m，打它的炮塔正面（200 mm）。这条线和出生点看它的视线重合，靶车测试已经保证中间没有遮挡；52 m 处四种车都不压石头（45 m 时中坦、重坦、TD 会压到 87 号石头）。配合 `--tank light` 时轻坦一定打不穿（最高 140 × 1.25 = 175）；
- `kill-td`：停在 TD（3 号）车尾后方 45 m，一直打它的战斗室后部直到击毁；
- `turn`：和 `pen` 一样打 0 号靶车，但玩家的车体开局向左偏开 60°。配合 `--tank td` 时可以看到车体自动转过去；演示的镜头始终朝向靶车（`RangeDemo.Bearing`）。

演示只用仿真的数学，不依赖 Godot，所以 `RangeDemo.cs` 也链接进 `Tank.Art.Tests`，新测试检查每个演示配每种车，停车的位置都在地图内、不压石头、不压靶车。

**Files:**
- Modify: `src/Tank.Sim/Maps/TargetSpot.cs`、`SandboxMap.cs`、`src/Tank.Client/scripts/Sandbox/TargetRange.cs`、`RangeDemo.cs`、`SandboxAutomation.cs`、`SandboxMain.cs`、`scripts/Cameras/CameraPresets.cs`、`scripts/Bootstrap/LaunchOptions.cs`、`tests/Tank.Art.Tests/Tank.Art.Tests.csproj`
- Test: `tests/Tank.Sim.Tests/TargetSpotTests.cs`；新建 `tests/Tank.Art.Tests/RangeDemoTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/RangeDemoTests.cs`：

```csharp
using Tank.Client.Sandbox;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class RangeDemoTests
{
    private static readonly string[] Names = { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn" };
    private static readonly Battlefield Field = SandboxMap.Create();

    /// <summary>Every demo, with every tank --tank can give the player.</summary>
    public static TheoryData<string, string> DemosAndTanks()
    {
        var data = new TheoryData<string, string>();
        foreach (string name in Names)
        foreach (TankSpec spec in TankCatalog.All)
            data.Add(name, spec.Id);
        return data;
    }

    [Theory]
    [MemberData(nameof(DemosAndTanks))]
    public void Parks_the_player_on_open_ground(string demo, string tank)
    {
        RangeDemo d = RangeDemo.Find(demo)!;
        Obb2 hull = TankMotion.Footprint(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw);
        Assert.Equal(d.PlayerPosition, Field.ClampToPlayable(d.PlayerPosition));
        foreach (Prop p in Field.Props)
            Assert.False(p.IsSolid && Obb2.TryGetPenetration(hull, p.Footprint, out _), $"parked on rock {p.Id}");
        foreach (TargetSpot s in SandboxMap.TargetSpots)
            Assert.False(Obb2.TryGetPenetration(hull, TankMotion.Footprint(s.Spec, s.Position, s.Yaw), out _), "parked on a target");
    }

    [Fact]
    public void Knows_every_demo_and_nothing_else()
    {
        Assert.All(Names, name => Assert.NotNull(RangeDemo.Find(name)));
        Assert.Null(RangeDemo.Find("tiger"));
        Assert.Null(RangeDemo.Find(null));
    }
}
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index bf49a75..bd7691b 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -16,6 +16,8 @@
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
     <!-- So are the launch options (they need Tank.Core and Tank.Net for the old game's switches). -->
     <Compile Include="..\..\src\Tank.Client\scripts\Bootstrap\LaunchOptions.cs" LinkBase="Bootstrap" />
+    <!-- So are the scripted range demos (only sim maths). -->
+    <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs" LinkBase="Sandbox" />
     <!-- Popup timing curves are engine-neutral too. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs" LinkBase="Overlay" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
PATCH
```

修改 `tests/Tank.Sim.Tests/TargetSpotTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/TargetSpotTests.cs b/tests/Tank.Sim.Tests/TargetSpotTests.cs
index 7509267..8c1e66c 100644
--- a/tests/Tank.Sim.Tests/TargetSpotTests.cs
+++ b/tests/Tank.Sim.Tests/TargetSpotTests.cs
@@ -15,7 +15,7 @@ public sealed class TargetSpotTests
 
     public static TheoryData<int> Indices() => new() { 0, 1, 2, 3, 4, 5 };
 
-    private static Obb2 Hull(TargetSpot s) => TankMotion.Footprint(TankCatalog.Medium, s.Position, s.Yaw);
+    private static Obb2 Hull(TargetSpot s) => TankMotion.Footprint(s.Spec, s.Position, s.Yaw);
 
     private static IEnumerable<Vector2> Corners(Obb2 b)
     {
@@ -27,6 +27,15 @@ public sealed class TargetSpotTests
     [Fact]
     public void ThereAreSix() => Assert.Equal(6, Spots.Count);
 
+    [Fact]
+    public void AllFourKindsOfTankStandThere()
+    {
+        Assert.Equal(TankCatalog.All.Select(s => s.Id).Order(), Spots.Select(s => s.Spec.Id).Distinct().Order());
+    }
+
+    [Fact]
+    public void TheFrontTarget_IsAMediumTank() => Assert.Same(TankCatalog.Medium, Spots[0].Spec);   // the range demos aim at its armour
+
     [Theory]
     [MemberData(nameof(Indices))]
     public void StandsAtTheRightRange(int i)
@@ -113,7 +122,8 @@ public sealed class TargetSpotTests
     {
         Vector2 toSpawn = Vector2.Normalize(SandboxMap.Spawn - spot.Position);
         Vector2 across = new(-toSpawn.Y, toSpawn.X);
-        foreach (float side in new[] { -1.6f, 0f, 1.6f })
+        float half = spot.Spec.HullWidth / 2;
+        foreach (float side in new[] { -half, 0f, half })
             yield return Field.Raycast(Eye(), Plate(spot.Position + across * side), out RayHit hit) ? hit : null;
     }
 
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test Tank.sln 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -3`
Expected: `Tank.Sim.Tests` 和 `Tank.Art.Tests` 都编译失败：`TargetSpot` 没有 `Spec`。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index 4d34c03..62e3341 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -12,7 +12,7 @@ namespace Tank.Client.Bootstrap;
 /// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
 /// --camera-preset front|side|hill|spin（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
-/// --range-demo pen|ricochet|nopen|he|kill（试驾场：自动对靶车开炮，用于截图）  --shot-after fire|hit|kill:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克或第一次击毁坦克后再过 SECONDS 秒截图）
+/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn（试驾场：自动对靶车开炮，用于截图）  --shot-after fire|hit|kill:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克或第一次击毁坦克后再过 SECONDS 秒截图）
 /// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
 /// for the caller to report.
PATCH
```

修改 `src/Tank.Client/scripts/Cameras/CameraPresets.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Cameras/CameraPresets.cs b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
index 0f7a766..e8a1104 100644
--- a/src/Tank.Client/scripts/Cameras/CameraPresets.cs
+++ b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
@@ -6,8 +6,8 @@ namespace Tank.Client.Cameras;
 /// Fixed viewpoints for repeatable screenshots and frame-time runs (<c>--camera-preset NAME</c>).
 /// front/side follow the tank in its own frame; hill is a fixed overview; spin turns a full circle every 10 s from the
 /// middle of the map, the worst case for draw distance; orbit circles the tank at 14 m every 12 s; bush starts the tank
-/// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at the first practice
-/// target from its front-right, on the side where its turret lands (pair it with --range-demo).
+/// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at a practice target (the
+/// range demo's, else the first) from its front-right, on the side where a thrown turret lands (pair it with --range-demo).
 /// </summary>
 public static class CameraPresets
 {
@@ -32,7 +32,8 @@ public static class CameraPresets
     public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;
 
     /// <param name="groundAt">Terrain height at (x, z), used to keep fixed eyes above the ground.</param>
-    public static (Vector3 Eye, Vector3 Target) Get(string name, Vector3 tank, float tankYaw, double time, Func<float, float, float> groundAt)
+    /// <param name="target">The practice target the "target" preset looks at (index into <c>SandboxMap.TargetSpots</c>).</param>
+    public static (Vector3 Eye, Vector3 Target) Get(string name, Vector3 tank, float tankYaw, double time, Func<float, float, float> groundAt, int target = 0)
     {
         var yaw = new Basis(Vector3.Up, tankYaw);
         switch (name)
@@ -50,7 +51,7 @@ public static class CameraPresets
             case "bush": // the tank parked inside a bush patch, seen from outside it (west, towards the lane)
                 return (tank + new Vector3(-16f, 3.4f, 6f), tank + TankFocus);
             case "target":
-                var spot = Tank.Sim.Maps.SandboxMap.TargetSpots[0];
+                var spot = Tank.Sim.Maps.SandboxMap.TargetSpots[target];
                 var at = new Vector3(spot.Position.X, groundAt(spot.Position.X, spot.Position.Y), spot.Position.Y);
                 var turn = new Basis(Vector3.Up, spot.Yaw);
                 return (at + turn * TargetEye, at + turn * TargetFocus);
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/RangeDemo.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/RangeDemo.cs b/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
index 83a6871..693dc51 100644
--- a/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
+++ b/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
@@ -10,10 +10,11 @@ namespace Tank.Client.Sandbox;
 /// <summary>
 /// Scripted shooting at the practice targets, for screenshots (<c>--range-demo NAME</c>): the player's tank is parked
 /// <see cref="Distance"/> m from target <see cref="Target"/>, <see cref="AngleOffFront"/> degrees round from straight
-/// ahead of it, lays its gun on <see cref="AimLocal"/> (target hull space: x right, y up, −z forward) and pulls the
-/// trigger from <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded.
+/// ahead of it, its hull <see cref="PlayerTurn"/> degrees to the left of facing the target, lays its gun on
+/// <see cref="AimLocal"/> (target hull space: x right, y up, −z forward) and pulls the trigger from <see cref="FireTick"/>
+/// on, once or (<see cref="KeepFiring"/>) whenever reloaded.
 /// </summary>
-public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, SN.Vector3 AimLocal, AmmoType Ammo, bool KeepFiring)
+public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, SN.Vector3 AimLocal, AmmoType Ammo, bool KeepFiring, float PlayerTurn = 0f)
 {
     public const double FireAfter = 3.0;
 
@@ -27,8 +28,17 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
     public static readonly int FireTick = (int)Math.Ceiling(FireAfter * SimConstants.TickRate);
     private static readonly SN.Vector3 HullFront = FrontFace(TankCatalog.Medium.HullArmor, 0f);
     private static readonly SN.Vector3 TurretFront = FrontFace(TankCatalog.Medium.TurretArmor, TankCatalog.Medium.TurretOffset);
+    private static readonly SN.Vector3 HeavyTurretFront = FrontFace(TankCatalog.Heavy.TurretArmor, TankCatalog.Heavy.TurretOffset);
+    private static readonly SN.Vector3 TdRear = RearFace(TankCatalog.Td.TurretArmor);   // a casemate's box sits on the hull origin
 
-    /// <summary>pen, ricochet, nopen, he or kill; null for any other name.</summary>
+    /// <summary>
+    /// pen, ricochet, nopen, he or kill (at target 0, a medium tank); nopen-heavy (the far heavy tank's 200 mm turret
+    /// front, head-on from the spawn's side, on the sight line the target tests keep clear: no light tank gets through;
+    /// 52 m out, where even the heavy tank parks clear of the rocks); kill-td (the tank
+    /// destroyer's rear until it is knocked out); turn (target 0 again, the hull starting 60° off: a tank destroyer has to
+    /// turn its hull to bring the gun round); null for any other name. Each shoots with whatever tank --tank gives the
+    /// player.
+    /// </summary>
     public static RangeDemo? Find(string? name) => name switch
     {
         "pen" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, false),
@@ -36,6 +46,9 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
         "nopen" => new RangeDemo(0, 60f, 45f, TurretFront, AmmoType.AP, false),
         "he" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.HE, false),
         "kill" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, true),
+        "nopen-heavy" => new RangeDemo(5, 0f, 52f, HeavyTurretFront, AmmoType.AP, false),
+        "kill-td" => new RangeDemo(3, 180f, 45f, TdRear, AmmoType.AP, true),
+        "turn" => new RangeDemo(0, 0f, 45f, HullFront, AmmoType.AP, false, PlayerTurn: 60f),
         _ => null,
     };
 
@@ -44,12 +57,18 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
     /// <summary>Where the player's tank is parked.</summary>
     public SN.Vector2 PlayerPosition => Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;
 
-    /// <summary>The player's hull faces the target.</summary>
-    public float PlayerYaw => Angles.YawOf(Spot.Position - PlayerPosition);
+    /// <summary>Heading from the player's tank to the target (where the demo camera looks).</summary>
+    public float Bearing => Angles.YawOf(Spot.Position - PlayerPosition);
+
+    /// <summary>The player's hull: facing the target, turned <see cref="PlayerTurn"/> degrees to the left.</summary>
+    public float PlayerYaw => Angles.Wrap(Bearing + Angles.Deg(PlayerTurn));
 
     /// <summary>Middle of a target armour box's front face (hull space), for a box centred <paramref name="ahead"/> m ahead of the hull origin.</summary>
     private static SN.Vector3 FrontFace(ArmorBox box, float ahead) => new(0, box.LocalCenter.Y, -ahead - box.Length / 2);
 
+    /// <summary>Middle of the rear face of an armour box centred on the hull origin (hull space).</summary>
+    private static SN.Vector3 RearFace(ArmorBox box) => new(0, box.LocalCenter.Y, box.Length / 2);
+
     /// <summary>The aim point in world space, given the ground height under the target.</summary>
     public SN.Vector3 AimPoint(float groundY)
     {
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
index 44e3480..367acba 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
@@ -63,7 +63,7 @@ public sealed class SandboxAutomation
         if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
             GD.PushWarning($"Unknown --camera-preset '{preset}'; use front, side, close, closerear, sprocket, hill, spin, orbit, bush or target.");
         if (_options.RangeDemo is { } demo && Demo is null)
-            GD.PushWarning($"Unknown --range-demo '{demo}'; use pen, ricochet, nopen, he or kill.");
+            GD.PushWarning($"Unknown --range-demo '{demo}'; use pen, ricochet, nopen, he, kill, nopen-heavy, kill-td or turn.");
     }
 
     /// <summary>Notes the first time the player fired, a tank was hit and a tank was destroyed.</summary>
@@ -94,7 +94,7 @@ public sealed class SandboxAutomation
     public (float Yaw, float Pitch)? CameraAngles(float tankYaw)
     {
         if (_options.AutoDrive) return (tankYaw + AutoCameraYaw, AutoCameraPitch);
-        if (Demo is { } demo) return (demo.PlayerYaw, DemoCameraPitch);
+        if (Demo is { } demo) return (demo.Bearing, DemoCameraPitch);
         return null;
     }
 
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 2d25771..2466f48 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -19,7 +19,7 @@ namespace Tank.Client.Sandbox;
 /// (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
-/// --camera-preset front|side|hill|spin|orbit|bush|target|... --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill
+/// --camera-preset front|side|hill|spin|orbit|bush|target|... --fps-log --sniper --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn
 /// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill:S --quit-after S]
 /// </summary>
 public partial class SandboxMain : Node3D
@@ -234,7 +234,7 @@ public partial class SandboxMain : Node3D
         _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(tank.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), _sim.Field.Terrain);
         if (HasPreset)
         {
-            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _groundAt);
+            var (eye, target) = CameraPresets.Get(_options.CameraPreset!, pose.Position.ToGodot(), tank.Yaw, _time, _groundAt, _automation.Demo?.Target ?? 0);
             _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
         }
         else if (_automation.Demo is null && !_input.FreeLook) UpdateAim(pose, tank.Spec);
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/TargetRange.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/TargetRange.cs b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
index 1827921..56ae014 100644
--- a/src/Tank.Client/scripts/Sandbox/TargetRange.cs
+++ b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
@@ -12,9 +12,9 @@ using SN = System.Numerics;
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// The six practice targets on <see cref="SandboxMap.TargetSpots"/>: blue-grey medium tanks that sit still with their
-/// guns straight ahead. A knocked-out target blows up, throws its turret, chars and smokes; ten seconds later it is
-/// revived where it stands. Each <see cref="Sync"/> also notes every target's head point and health for the HUD.
+/// The six practice targets on <see cref="SandboxMap.TargetSpots"/>: blue-grey tanks of every kind that sit still with
+/// their guns straight ahead. A knocked-out target blows up, throws its turret (a tank destroyer has none), chars and
+/// smokes; ten seconds later it is revived where it stands. Each <see cref="Sync"/> also notes every target's head point and health for the HUD.
 /// </summary>
 public partial class TargetRange : Node3D
 {
@@ -46,13 +46,13 @@ public partial class TargetRange : Node3D
         foreach (TargetSpot spot in SandboxMap.TargetSpots)
         {
             int slot = sim.TankCount;
-            int id = sim.AddTank(TankCatalog.Medium, spot.Position, spot.Yaw);
+            int id = sim.AddTank(spot.Spec, spot.Position, spot.Yaw);
             var model = new TankModel();
             AddChild(model);
-            model.Build(TankCatalog.Medium, Paint);
+            model.Build(spot.Spec, Paint);
             var smoke = new WreckSmoke();
             model.AddChild(smoke);
-            smoke.Build(model.TurretRing);   // pours from the open turret ring
+            smoke.Build(model.TurretRing);   // pours from the open turret ring (a casemate's roof)
             var target = new Target { Id = id, Slot = slot, Spot = spot, Model = model, Smoke = smoke };
             Observe(target, sim.Tanks[slot], sim.Field.Terrain);   // heads are known before the first Sync
             _targets.Add(target);
PATCH
```

修改 `src/Tank.Sim/Maps/SandboxMap.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/SandboxMap.cs b/src/Tank.Sim/Maps/SandboxMap.cs
index a3c3df5..679da9c 100644
--- a/src/Tank.Sim/Maps/SandboxMap.cs
+++ b/src/Tank.Sim/Maps/SandboxMap.cs
@@ -1,6 +1,7 @@
 using System.Numerics;
 using Tank.Sim.Geometry;
 using Tank.Sim.Terrain;
+using Tank.Sim.Vehicles;
 
 namespace Tank.Sim.Maps;
 
@@ -59,17 +60,18 @@ public static class SandboxMap
 
     /// <summary>
     /// Six practice targets, hand-placed on flat open ground 30–200 m from the spawn, clear of rocks, bushes and the
-    /// spawn-to-hill lane. Seen from the spawn they show, in order: the front, the side, a 45° angle, the rear, a front
-    /// half hidden behind a rock, and a front far away (180–200 m).
+    /// spawn-to-hill lane, all four kinds of tank among them. Seen from the spawn they show, in order: a medium tank's
+    /// front (the range demos shoot this one), a heavy tank's side, a light tank at 45°, a tank destroyer's rear, a medium
+    /// tank's front half hidden behind a rock, and a heavy tank's front far away (180–200 m).
     /// </summary>
     public static readonly IReadOnlyList<TargetSpot> TargetSpots = new[]
     {
-        Facing(new Vector2(160, 304), turnDegrees: 0),
-        Facing(new Vector2(248, 300), turnDegrees: 90),
-        Facing(new Vector2(216, 244), turnDegrees: 45),
-        Facing(new Vector2(140, 256), turnDegrees: 180),
-        Facing(new Vector2(280, 328), turnDegrees: 0),
-        Facing(new Vector2(380, 272), turnDegrees: 0),
+        Facing(new Vector2(160, 304), turnDegrees: 0, TankCatalog.Medium),
+        Facing(new Vector2(248, 300), turnDegrees: 90, TankCatalog.Heavy),
+        Facing(new Vector2(216, 244), turnDegrees: 45, TankCatalog.Light),
+        Facing(new Vector2(140, 256), turnDegrees: 180, TankCatalog.Td),
+        Facing(new Vector2(280, 328), turnDegrees: 0, TankCatalog.Medium),
+        Facing(new Vector2(380, 272), turnDegrees: 0, TankCatalog.Heavy),
     };
 
     private static readonly TerrainGenerator.Hill[] Hills =
@@ -190,9 +192,9 @@ public static class SandboxMap
         return true;
     }
 
-    /// <summary>A target whose hull is turned <paramref name="turnDegrees"/> to its left from facing the spawn.</summary>
-    private static TargetSpot Facing(Vector2 position, float turnDegrees) =>
-        new(position, Angles.Wrap(Angles.YawOf(Spawn - position) + Angles.Deg(turnDegrees)));
+    /// <summary>A <paramref name="spec"/> target whose hull is turned <paramref name="turnDegrees"/> to its left from facing the spawn.</summary>
+    private static TargetSpot Facing(Vector2 position, float turnDegrees, TankSpec spec) =>
+        new(position, Angles.Wrap(Angles.YawOf(Spawn - position) + Angles.Deg(turnDegrees)), spec);
 
     internal static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
     {
PATCH
```

修改 `src/Tank.Sim/Maps/TargetSpot.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/TargetSpot.cs b/src/Tank.Sim/Maps/TargetSpot.cs
index 7ca786f..b952bda 100644
--- a/src/Tank.Sim/Maps/TargetSpot.cs
+++ b/src/Tank.Sim/Maps/TargetSpot.cs
@@ -1,6 +1,7 @@
 using System.Numerics;
+using Tank.Sim.Vehicles;
 
 namespace Tank.Sim.Maps;
 
-/// <summary>Where a practice target stands: ground position (X is world X, Y is world Z) and hull yaw.</summary>
-public readonly record struct TargetSpot(Vector2 Position, float Yaw);
+/// <summary>Where a practice target stands, ground position (X is world X, Y is world Z) and hull yaw, and what tank it is.</summary>
+public readonly record struct TargetSpot(Vector2 Position, float Yaw, TankSpec Spec);
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 315、Art 330。靶车位置的测试按新车型全部通过，说明重坦、TD 放在原来的位置上也不压石头、不挡通道。

- [ ] **Step 5：截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
atshot t8-targets 3
demoshot t8-nopen-heavy nopen-heavy hit:0.3 6 --sniper --tank light
demoshot t8-kill-heavy kill kill:0.45 45 --camera-preset target --tank heavy
demoshot t8-kill-td kill-td kill:4 60 --camera-preset target --tank heavy
atshot t8-turn-before 0.2 --range-demo turn --tank td
atshot t8-turn-after 2.6 --range-demo turn --tank td
```

Expected：
- `t8-targets`：远处的蓝灰色靶车里能认出轻坦、中坦和侧对着的重坦（5 个负重轮、方块炮塔）；
- `t8-nopen-heavy`：狙击镜里重坦正面，炮塔上弹出灰色“未击穿”，血条满（换 `--tank td` 再拍一次应是红色“击穿！”，说明 250 穿深打得穿）；
- `t8-kill-heavy`：中坦靶车被重坦击毁，炮塔飞在空中，伤害数字约 −250～−410；
- `t8-kill-td`：TD 靶车焦黑、冒黑烟，烟从战斗室顶上冒出，旁边没有飞落的炮塔；
- `t8-turn-before`：TD 车体斜着，炮已经转到左侧极限；`t8-turn-after`：车体已经转向靶车，准星圈落在靶车上。

- [ ] **Step 6：提交**

```bash
git add src tests
git commit -m "feat(client): practice targets of all four kinds; range demos at the heavy, the tank destroyer and from a hull turned away"
```

---

### Task 9：截图与帧率

设计 §5 的验收。全部测试先跑一遍。

- [ ] **Step 1：全部测试**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: Sim 315、Art 330、Core 62、Net 50、Update 42，全部通过。

- [ ] **Step 2：验收截图**

```bash
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
for t in light medium heavy td; do shot final-$t close --tank $t; done
atshot final-turn-before 0.2 --range-demo turn --tank td
atshot final-turn-after 2.6 --range-demo turn --tank td
demoshot final-nopen-heavy nopen-heavy hit:0.3 6 --sniper --tank light
demoshot final-toss-heavy kill kill:0.45 45 --camera-preset target --tank heavy
demoshot final-wreck-td kill-td kill:4 60 --camera-preset target --tank heavy
demoshot final-pen pen hit:0.3 6 --sniper
```

对照设计 §5.2 逐张核对：四种车各一张近景，轮廓一眼能分开；TD 自动转向前后；轻坦打重坦正面“未击穿”；重坦击毁目标、炮塔飞起；TD 被击毁（焦黑、冒烟、没有炮塔飞起）；`final-pen` 确认 2A 的打靶没有被改坏（红色“击穿！”、“-数字”）。

- [ ] **Step 3：测帧**（窗口必须在最前面，期间不要切走）

```bash
fpslog fps-spin spin 35
fpslog fps-orbit orbit 35
```

Expected: 去掉前面被限速的 60 帧行之后，两个机位都稳定在 60 帧以上（2A 结束时实测两个机位都是 145 帧，看起来被限在 145）。如果整段都是 60 帧，多半是窗口被挡到后台了，重测一次。把结果记进 `docs/PROGRESS.md`。

- [ ] **Step 4：确认 Godot 都已退出**

Run: `pgrep -fl 'MacOS/Godot' || echo none`
Expected: `none`。

---

### Task 10：用户试玩与收尾

- [ ] **Step 1：交给用户试玩**

告诉用户启动命令和操作（窗口模式，鼠标瞄准，左键开炮，右键自由视角，Shift 狙击镜，滚轮缩放，1/2 换弹种，R 补弹，F1～F4 换车：轻坦、中坦、重坦、TD，Esc 释放鼠标）：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage2b
dotnet build src/Tank.Client/Tank.Client.csproj && godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn
```

请用户确认设计 §5.4：F1～F4 换车后能感到四种车速度和转向的差别；TD 的车体会自动转向瞄准方向；轻坦打不穿重坦正面；重坦两三炮就能打掉一辆车。另外请用户看：
1. 三种新车的外观（轮廓是否好认、TD 的长炮管是否太长）；
2. 四种车的装填和伤害手感（设计 §2.1 的平衡）。

- [ ] **Step 2：按反馈调整**

每条反馈单独修改、截图确认、提交，并在 `docs/PROGRESS.md` 的节点记录里追加一行。

- [ ] **Step 3：合入 main**

用户确认后：

```bash
cd /Users/murphy/Desktop/dev/BattleCity
git merge --squash feat/stage2b-vehicles
git commit -m "feat: stage 2B vehicles — light, heavy and tank destroyer, tank switching in the sandbox"
```

然后更新 `docs/PROGRESS.md`（阶段总览里 2B 标为已合入，写上提交号；“当前位置”改为阶段 2C），一起提交到 main。不推送远程，除非用户要求。删除写计划用的临时 worktree 和分支：

```bash
git worktree remove /Users/murphy/Desktop/dev/BattleCity-stage2b-draft --force
git branch -D draft/stage2b-vehicles
```
