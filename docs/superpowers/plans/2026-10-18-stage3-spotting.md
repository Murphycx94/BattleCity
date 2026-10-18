# 阶段 3：点亮 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 坦克之间按点亮规则互相看见或看不见（发现距离、车型隐蔽、开火惩罚、草丛加成、视线遮挡、失去后保持 2 秒、队伍共享）；试驾场里没被点亮的敌车不画；加一辆沿路线绕圈、进草丛停下、朝玩家前方开炮的 AI 靶车；玩家被它发现时亮“被点亮”灯；调试文字（V 键）和截图用的机位、演示。

**Architecture:** 判定规则是无状态的 `Vision/Spotting`（一次观察：距离、发现距离、有效隐蔽、草丛、开火、两条视线），汇总是 `Vision/VisionTracker`（宿主每走一步调用一次，每 6 个 tick 算一次，按队伍记下最后一次看见的 tick，保持 60 tick）。仿真本身不依赖视野。坦克状态新增队伍和最后开炮的 tick，车型数据新增视野和行进 / 静止隐蔽。AI 靶车的驾驶是仿真库里的 `Ai/PatrolDriver`：每 tick 给出一条 `InputCmd`，不依赖 Godot，可以测试。客户端里 `TargetRange` 加上这辆车，按插值画所有靶车，看不见的不画；HUD 隐藏看不见的靶车的血条、命中大字和伤害数字，状态面板加灯，另有调试文字。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、Godot 4.7.2 .NET 版（Forward+，macOS 上走 Metal）

**设计依据：** `docs/superpowers/specs/2026-10-16-stage3-spotting-design.md`（全文）；上级文档 `docs/superpowers/specs/2026-10-13-third-person-tank-design.md` §6。

---
## 写计划时已核实、与设计的差异

写这份计划时，先在临时 worktree（`/Users/murphy/Desktop/dev/BattleCity-stage3-draft`，分支 `draft/stage3-spotting`，从设计提交 `032b443` 拉出）里按任务顺序把下文全部实现了一遍，一个任务一个提交。下文的代码和补丁由脚本从这些提交直接导出，没有手抄。实测结果：

- 每个提交单独检出后：客户端编译 0 警告 0 错误，Sim 和 Art 测试全过。Sim 测试数依次是 467、496、509、573、573、573、575（Task 1～7），Art 依次是 445、445、445、445、448、454、510；Core 62、Net 50、Update 42 不变。
- 截图逐张看过：
  - `patrol` 机位：AI 靶车在空地上看得见（开头“看见”或“保持中”）；停进草丛后消失，调试文字“看不见  109 m / 41 m  隐蔽 0.90（草×2）”；开炮后草丛上方出现血条，“看见  109 m / 117 m  隐蔽 0.60（草×2 开火）”；离开草丛后又看得见；
  - `lamp` 演示：开炮前灯灭，开炮后灯亮；
  - `breach` 演示（TD）：院里的靶车隔着石墙看不见（“挡住”），墙打穿后看得见；
  - `fence`、`pen`、`kill` 演示和 `yard`、`front` 机位与 2C 一样，只多了灯（演示里 AI 不看人，灯是暗的）。
- 帧率（关垂直同步）orbit 145 帧，spin 118～120 帧。
- 用 AI 靶车跑过：272 个玩家位置 × 50 秒共 1006 发炮，没有一发打中玩家，最近的落点离玩家 10.9 m；连跑 8 圈不卡住，一圈约 48 秒，最大偏离路线 1.7 m，车体四周离岩石至少 3 m；在试驾场上 60 秒（9 辆车、AI 跑动、每 tick 查询全部可见性、调试用的 `Inspect`）每 tick 不分配内存。
- 草稿经过一轮代码审查，没有严重问题。按审查意见改了：
  - **队伍编号**：`AddTank` 拒绝 `SimConstants.MaxTeams`（4）及以上的队伍，原来会悄悄写进下一辆车的视野表（Task 1）；
  - **“在草丛里”的 0.75 倍半径原来不起作用**：车体中心只要在草丛半径以内，视线线段的终点就落在草丛里，总会被算成“视线穿过”。现在车所在的那一丛只在视线经过它中心 0.75 倍半径以内时才算：草丛挡在车前面算，蹭着边、或草丛在车后面不算。设计文档已同步（Task 2）；
  - **复活的车闪一下**：残骸没人看，复活后要等下一次计算才可见，最多 5 个 tick 不画。现在每次计算都把残骸记为被所有队看见，复活后接着按 2 秒保持（Task 3）；
  - **`fence` 演示看不见靶车**：两条视线都打在栅栏门旁边的砖墙上，靶车被隐藏，命中大字和伤害数字也不显示。站位往西挪了约 4°（方向从 (−1, −0.35) 改为 (−1, −0.27)），四种车的炮线仍只穿过北边那块栅栏，视线从门里穿过去。新测试：除 `wall`、`breach`、`ram` 外，每个演示开炮时四种车都看得见靶车（Task 7）；
  - **停在起点的 AI 仍然看人**：旧演示里“被点亮”灯几乎都亮着，改变了原来的截图。现在停着时也不看人（Task 5）；
  - **隔墙看不见的验收截图**：中坦和重坦站得高，在 `wall`、`breach` 的站位上视线能越过石墙顶看到靶车炮塔，只有轻坦和 TD 看不见，所以用 TD 拍，并加测试固定下来（Task 7）；
  - 补测试：地面挡视线、坦克不挡视线、在真实地图上每 tick 不分配内存、复活后不闪、等待中被打掉复活后接着跑；`PatrolWatch` 的注释数字（在草丛里是 40.5 m，66 m 是刚停下还算行进的那次）；`lamp` 演示只适用中坦（重坦开炮前就被看见，TD 开炮后也看不见），写进注释；几处命名和注释。

与设计文字不同、但有意为之的地方（执行时不要“改回去”，设计文档已同步）：

- **AI 瞄准玩家前方 25 m、再往左 8 m 的地面**（设计原文前方 15 m），玩家在 50 m 以内不开炮。炮弹低平地飞，一点向上的散布就会越过瞄准点很远：瞄 15 m 时，在观察点上实测第一发就打中了玩家。偏左 8 m 之后，越过去的炮弹也从玩家身边至少 8 m 外飞过。
- **转弯超过 35° 的路点也要停稳**，和等待点一样。否则带着惯性甩出路线 5 m；现在最大偏离 1.7 m。
- **`patrol` 机位的玩家放在观察点 (224, 352)**（设计原文“仍在出生点”）：从出生点看，两片草丛都被岩石和小山挡住，AI 一直看不见。
- **`lamp` 演示**：玩家停在 (38, 306)，第 8 秒开炮。AI 照常跑时，前 3 秒它在起点附近，视线被挡住；在第一片草丛里等的 4.5～14.5 秒，视线通、距离 269 m。玩家静止时发现距离 229 m，开炮后 282 m，两边都有余量。
- **调试文字多一种“保持中”**：刚看不见、还在 2 秒保持期内、仍然画着的车。文字是当场算的，画不画按最近一次计算，两者最多差 5 个 tick。

## 留给以后的问题（不在本阶段处理）

- AI 靶车第一次停进每片草丛时，会打掉射线上自己那片里的几丛草（每圈 2～5 丛，按 T 恢复）。规则如此，没有特别处理；它所在的那丛总在（炮弹起点所在的草丛不算被穿过）。
- `ram` 演示里撞不穿墙的车看不见院里的靶车（规则如此）。
- 调试文字在远处一排车上会叠在一起。
- 草丛不会为 AI 靶车分开（只为玩家的车分开），所以它开炮被点亮时，画面上看到的多半是草丛上方的血条，车身藏在叶子里。
- 联网时按可见性裁剪快照留给阶段 4：`VisionTracker` 已经按队伍给出可见性，服务器可以直接用。

---
## 执行前须知

- **工作目录**：直接在现有 worktree 里执行，不要新建：`/Users/murphy/Desktop/dev/BattleCity-stage3`（分支 `feat/stage3-spotting`）。下文所有路径都相对于它。
- 每个 shell 先执行下面这段。`dotnet` 不在默认 PATH 里；`godot_run` 保证 Godot 无论如何都会被强制结束（曾有一次 `--quit-after` 卡住 15 小时）。

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage3
mkdir -p build/shots
# godot_run SECONDS ARGS...: run Godot, kill it hard after SECONDS no matter what
godot_run() {
  local limit=$1; shift
  godot-mono "$@" & local pid=$!
  ( sleep "$limit"; kill -9 "$pid" 2>/dev/null ) >/dev/null 2>&1 & local killer=$!   # off the pipe, or its sleep holds a | grep open
  wait "$pid"; pkill -P "$killer" 2>/dev/null; kill "$killer" 2>/dev/null; wait "$killer" 2>/dev/null
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
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`。`Tank.Sim` 和测试项目里任何警告都会让构建失败。`Tank.Art.Tests` 会编译客户端里不依赖 Godot 的源码（网格代码、外观数据、`TurretToss`、`LaunchOptions.cs`、`RangeDemo.cs`、`PopupCurves.cs`），它们同样不能有警告、不能引用 Godot。本计划新建的 `Sandbox/TankInterpolation.cs`、`Overlay/SpotText.cs` 也加进去。客户端项目关掉了该选项，但也要保持 0 警告。
- 新增 `src/Tank.Client` 下的脚本后，提交前运行 `importall`，把生成的 `.cs.uid` 一起提交。下文的 `git add` 按目录添加，会带上它们。
- **不要改**：旧 Battle City 代码（`src/Tank.Core`、`src/Tank.Net`、`src/Tank.Server`，客户端的 `scripts/Bootstrap/` 中除 `LaunchOptions.cs` 以外的文件、`scripts/UI/`、`scripts/View/`、`scripts/Controls/`）和 `project.godot`。
- **坐标约定**（同前几个阶段）：Y 向上，−Z 为前方；偏航 0 朝 −Z，正值向左转；`InputCmd.Steer` 为 +1 时向右转；车体空间原点在接地中心。`TankState.Position` 是二维 `(x, z)`。
- **测试项目与数量**：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。开始前是 Core 62、Sim 459、Net 50、Update 42、Art 445。每个任务末尾写明了做完之后 Sim 和 Art 的测试数；对不上就说明漏了或多了测试。
- 提交信息用约定式前缀（feat/fix/test/docs/chore）。每完成一批，在 `docs/PROGRESS.md` 的“3 节点记录”里追加一行（任务、提交号、测试数）。
- 分支最后用 squash merge 并入 `main`，不推送远程，除非用户要求。
## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Vehicles/Concealment.cs` | 新建 | 一种车的隐蔽：行进、静止 |
| `Vehicles/TankSpec.cs`、`TankCatalog.cs` | 修改 | 每种车的视野和隐蔽（总体设计 §4.4 的数） |
| `Vehicles/TankState.cs`、`Simulation.cs`、`SimConstants.cs` | 修改 | 队伍编号（`AddTank` 时给，不超过 `MaxTeams`，换车保留）；最后开炮的 tick（开炮时记下，换车清掉） |
| `Maps/Battlefield.cs` | 修改 | 草丛的下标表 `Bushes`，给视线数草丛 |
| `Vision/Spotting.cs` | 新建 | 一次观察的全部规则（无状态）：行进与静止、开火惩罚、草丛加成、有效隐蔽、发现距离、15 m 规则、两条视线；结果 `Sighting` |
| `Vision/VisionTracker.cs` | 新建 | 每 6 个 tick 算一次谁看见谁，按队伍记录、保持 60 tick；`IsVisible`、`Inspect`、`SetWatching` |
| `Ai/PatrolDriver.cs` | 新建 | 沿路线绕圈：转向行驶，偏太多先原地转，在等待点和急转弯处停稳，等待时朝玩家前方偏左的地面开炮 |
| `Maps/SandboxMap.cs` | 修改 | AI 靶车的车型、起点、路线（两片草丛各停 10 秒）；看它的观察点 `PatrolWatch` |

**客户端 `src/Tank.Client`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `scripts/Sandbox/TankInterpolation.cs` | 新建 | 两个 tick 之间的插值（从 `SandboxMain` 挪出来，AI 靶车也用） |
| `scripts/Sandbox/TargetRange.cs` | 修改 | 靶车都是第 1 队，固定靶车不看人；加上 AI 靶车（扬尘、插值）；看不见的不画；`IsHidden`、`IdAt` |
| `scripts/Sandbox/SandboxMain.cs` | 修改 | 建 `VisionTracker`，每步之后更新；打靶演示时 AI 停在起点；V 键 |
| `scripts/Sandbox/SandboxHud.cs` | 修改 | 看不见的靶车不显示血条、命中大字、伤害数字；灯；调试文字 |
| `scripts/Sandbox/SandboxAutomation.cs` | 修改 | `--shot-after` 的命中、击毁、墙受伤、物件被毁只算玩家造成的 |
| `scripts/Overlay/StatusPanel.cs` | 修改 | 面板右上角的“被点亮”灯 |
| `scripts/Overlay/SpotText.cs`、`SpotLabels.cs` | 新建 | 调试文字的内容（不依赖 Godot）和显示 |
| `scripts/Sandbox/SandboxInput.cs`、`scripts/Bootstrap/LaunchOptions.cs` | 修改 | V 键；`--spot-debug` |
| `scripts/Cameras/CameraPresets.cs`、`scripts/Sandbox/RangeDemo.cs` | 修改 | `patrol` 机位；`lamp` 演示（每个演示自己的开炮时间、站位、AI 是否照常跑） |

**测试**：`tests/Tank.Sim.Tests` 新增 `VisionSpecTests`、`SpottingTests`、`VisionTrackerTests`、`PatrolDriverTests`、`SandboxPatrolTests`；`tests/Tank.Art.Tests` 新增 `TankInterpolationTests`、`SpotTextTests`，改 `LaunchOptionsTests`、`RangeDemoTests`。

---
## 任务一览与批次

| 批次 | 任务 | 做完后 Sim / Art 测试数 |
|---|---|---|
| A 点亮规则 | 1 视野、隐蔽、队伍和最后开炮的 tick；2 一次观察的规则；3 汇总：谁看见谁 | 467 / 445 → 496 / 445 → 509 / 445 |
| B AI 靶车 | 4 巡逻驾驶和试驾场路线 | 573 / 445 |
| C 试驾场 | 5 AI 靶车上场，看不见的不画；6 “被点亮”灯和调试文字；7 `patrol` 机位和 `lamp` 演示 | 573 / 448 → 573 / 454 → 575 / 510 |
| D 验收 | 8 截图与帧率；9 用户试玩与收尾 | — |

每批做完先过规格审查，再过代码质量审查，然后在 `docs/PROGRESS.md` 记一行。

---

### Task 1：视野、隐蔽、队伍和最后开炮的 tick

设计 §2.1、§3。车型数据新增视野 `ViewRange` 和隐蔽 `Camo`（`Concealment(Moving, Still)`），数值取总体设计 §4.4：轻坦 300 m、0.30 / 0.40，中坦 270 m、0.18 / 0.25，重坦 250 m、0.08 / 0.12，TD 260 m、0.25 / 0.45。坦克状态新增队伍 `Team`（`AddTank` 的新参数，默认 0，换车时保留）和最后开炮的 tick `LastFired`（开炮时记下；新车和换车后是 `TankState.NeverFired`，一个离任何 tick 都很远的数，这样“3 秒内开过炮”的减法不会溢出）。

**Files:**
- Create: `src/Tank.Sim/Vehicles/Concealment.cs`
- Modify: `src/Tank.Sim/Vehicles/TankSpec.cs`、`TankCatalog.cs`、`TankState.cs`、`src/Tank.Sim/Simulation.cs`、`SimConstants.cs`（队伍数上限 `MaxTeams`，`AddTank` 拒绝超出的队伍）
- Test: 新建 `tests/Tank.Sim.Tests/VisionSpecTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/VisionSpecTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

public sealed class VisionSpecTests
{
    [Theory]
    [InlineData("light", 300f, 0.30f, 0.40f)]
    [InlineData("medium", 270f, 0.18f, 0.25f)]
    [InlineData("heavy", 250f, 0.08f, 0.12f)]
    [InlineData("td", 260f, 0.25f, 0.45f)]
    public void Each_tank_has_the_view_range_and_concealment_of_the_design(string id, float view, float moving, float still)
    {
        TankSpec spec = TankCatalog.ById(id);
        Assert.Equal(view, spec.ViewRange);
        Assert.Equal(new Concealment(moving, still), spec.Camo);
        Assert.InRange(spec.Camo.Moving, 0f, spec.Camo.Still);   // a moving tank is easier to spot
        Assert.InRange(spec.Camo.Still, 0f, 1f);
    }

    [Fact]
    public void A_tank_joins_the_team_it_is_added_to()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        sim.AddTank(TankCatalog.Light, new Vector2(120, 100), 0f, team: 1);
        Assert.Equal(0, sim.Tanks[0].Team);
        Assert.Equal(1, sim.Tanks[1].Team);
    }

    [Fact]
    public void A_team_past_the_last_is_refused()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Light, new Vector2(100, 100), 0f, team: SimConstants.MaxTeams - 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.AddTank(TankCatalog.Light, new Vector2(120, 100), 0f, team: SimConstants.MaxTeams));
        Assert.Equal(1, sim.TankCount);
    }

    [Fact]
    public void Firing_notes_the_tick_and_a_new_tank_has_not_fired()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        Assert.Equal(TankState.NeverFired, sim.Tanks[0].LastFired);
        var hold = new[] { TestWorld.Cmd() };
        for (int i = 0; i < 10; i++) sim.Step(hold);
        sim.Step(new[] { TestWorld.Cmd(fire: true) });
        Assert.Equal(sim.Tick, sim.Tanks[0].LastFired);
        sim.Step(hold);
        Assert.Equal(sim.Tick - 1, sim.Tanks[0].LastFired);
    }

    [Fact]
    public void Changing_tank_keeps_the_team_and_forgets_the_last_shot()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int id = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f, team: 2);
        sim.Step(new[] { TestWorld.Cmd(fire: true) });
        Assert.NotEqual(TankState.NeverFired, sim.Tanks[0].LastFired);
        sim.ChangeSpec(id, TankCatalog.Heavy);
        Assert.Equal(2, sim.Tanks[0].Team);
        Assert.Equal(TankState.NeverFired, sim.Tanks[0].LastFired);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Concealment`、`TankSpec.ViewRange`、`TankState.Team`、`TankState.LastFired` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/SimConstants.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/SimConstants.cs b/src/Tank.Sim/SimConstants.cs
index 3237d49..6beb2a1 100644
--- a/src/Tank.Sim/SimConstants.cs
+++ b/src/Tank.Sim/SimConstants.cs
@@ -5,4 +5,7 @@ public static class SimConstants
     public const int TickRate = 30;
     public const float Dt = 1f / TickRate;
     public const float Gravity = 9.81f;
+
+    /// <summary>Teams are numbered 0 … MaxTeams − 1.</summary>
+    public const int MaxTeams = 4;
 }
PATCH
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index 4be8fa8..a5fd58c 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -52,32 +52,35 @@ public sealed partial class Simulation
     /// <summary>Events produced by the most recent <see cref="Step"/>.</summary>
     public ReadOnlySpan<GameEvent> Events => CollectionsMarshal.AsSpan(_events);
 
-    /// <summary>Adds a tank with full health and ammunition, AP loaded.</summary>
+    /// <summary>Adds a tank of team <paramref name="team"/> (below <see cref="SimConstants.MaxTeams"/>) with full health and ammunition, AP loaded.</summary>
     /// <returns>The new tank's id. It occupies the next slot in <see cref="Tanks"/>.</returns>
-    public int AddTank(TankSpec spec, Vector2 position, float yaw)
+    public int AddTank(TankSpec spec, Vector2 position, float yaw, byte team = 0)
     {
         if (TankCount == MaxTanks) throw new InvalidOperationException($"At most {MaxTanks} tanks.");
+        if (team >= SimConstants.MaxTeams) throw new ArgumentOutOfRangeException(nameof(team), team, $"Teams are 0 … {SimConstants.MaxTeams - 1}.");
         int id = _nextId++;
-        _tanks[TankCount++] = Fresh(id, spec, position, yaw);
+        _tanks[TankCount++] = Fresh(id, spec, position, yaw, team);
         return id;
     }
 
     /// <summary>
-    /// Makes a tank another kind of vehicle where it stands (sandbox only for now). Its id, place, heading and shot count
-    /// stay (the shot count carries on, so each tank's shot index stays monotonic); everything else starts as on
-    /// <see cref="AddTank"/>, a wreck included. Shells already in flight keep their own round. No event: the caller knows.
+    /// Makes a tank another kind of vehicle where it stands (sandbox only for now). Its id, team, place, heading and shot
+    /// count stay (the shot count carries on, so each tank's shot index stays monotonic); everything else starts as on
+    /// <see cref="AddTank"/>, a wreck included, and it has not fired yet. Shells already in flight keep their own round. No event: the caller knows.
     /// </summary>
     public void ChangeSpec(int tankId, TankSpec spec)
     {
         ref TankState tank = ref TankRef(tankId);
-        tank = Fresh(tank.Id, spec, tank.Position, tank.Yaw) with { ShotCount = tank.ShotCount };
+        tank = Fresh(tank.Id, spec, tank.Position, tank.Yaw, tank.Team) with { ShotCount = tank.ShotCount };
     }
 
-    /// <summary>A tank at rest with full health and ammunition, AP loaded.</summary>
-    private TankState Fresh(int id, TankSpec spec, Vector2 position, float yaw) => new()
+    /// <summary>A tank at rest with full health and ammunition, AP loaded, that hasn't fired.</summary>
+    private TankState Fresh(int id, TankSpec spec, Vector2 position, float yaw, byte team) => new()
     {
         Id = id,
         Spec = spec,
+        Team = team,
+        LastFired = TankState.NeverFired,
         Position = Field.ClampToPlayable(position),
         Yaw = Angles.Wrap(yaw),
         Health = spec.MaxHealth,
@@ -215,6 +218,7 @@ public sealed partial class Simulation
         };
         tank.Reload = tank.Spec.ReloadTime;
         tank.ShotCount++;
+        tank.LastFired = Tick;
         tank.Bloom += tank.Spec.Bloom.Fire;
         if (tank.Ammo == AmmoType.HE) tank.HeLeft--;
         else tank.ApLeft--;
PATCH
```

新建 `src/Tank.Sim/Vehicles/Concealment.cs`：

```csharp
namespace Tank.Sim.Vehicles;

/// <summary>
/// How hard a tank is to spot (design §6), 0 … 1: the share of an observer's view range (beyond the first 15 m) it
/// takes away, while the tank is moving and while it stands still.
/// </summary>
public readonly record struct Concealment(float Moving, float Still);
```

修改 `src/Tank.Sim/Vehicles/TankCatalog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankCatalog.cs b/src/Tank.Sim/Vehicles/TankCatalog.cs
index 6e13167..0f5747a 100644
--- a/src/Tank.Sim/Vehicles/TankCatalog.cs
+++ b/src/Tank.Sim/Vehicles/TankCatalog.cs
@@ -42,6 +42,8 @@ public static class TankCatalog
         Dispersion = 0.40f,
         AimTime = 1.8f,
         Bloom = new BloomFactors(Moving: 1.5f, HullTurn: 1.2f, TurretTurn: 0.8f, Fire: 3.0f),
+        ViewRange = 300,
+        Camo = new Concealment(Moving: 0.30f, Still: 0.40f),
     };
 
     public static readonly TankSpec Medium = new()
@@ -75,6 +77,8 @@ public static class TankCatalog
         Dispersion = 0.36f,
         AimTime = 2.1f,
         Bloom = new BloomFactors(Moving: 2.0f, HullTurn: 1.5f, TurretTurn: 1.0f, Fire: 3.0f),
+        ViewRange = 270,
+        Camo = new Concealment(Moving: 0.18f, Still: 0.25f),
     };
 
     public static readonly TankSpec Heavy = new()
@@ -108,6 +112,8 @@ public static class TankCatalog
         Dispersion = 0.44f,
         AimTime = 2.9f,
         Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 1.2f, Fire: 3.5f),
+        ViewRange = 250,
+        Camo = new Concealment(Moving: 0.08f, Still: 0.12f),
     };
 
     /// <summary>
@@ -145,6 +151,8 @@ public static class TankCatalog
         Dispersion = 0.32f,
         AimTime = 1.9f,
         Bloom = new BloomFactors(Moving: 2.5f, HullTurn: 2.0f, TurretTurn: 0.8f, Fire: 3.0f),
+        ViewRange = 260,
+        Camo = new Concealment(Moving: 0.25f, Still: 0.45f),
     };
 
     /// <summary>Light to heavy, then the tank destroyer; F1–F4 in the sandbox pick them in this order.</summary>
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankSpec.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankSpec.cs b/src/Tank.Sim/Vehicles/TankSpec.cs
index a972a4b..c2cacad 100644
--- a/src/Tank.Sim/Vehicles/TankSpec.cs
+++ b/src/Tank.Sim/Vehicles/TankSpec.cs
@@ -77,6 +77,12 @@ public sealed record TankSpec
 
     public required BloomFactors Bloom { get; init; }
 
+    /// <summary>How far the crew spots an enemy that has no concealment at all, m (design §6).</summary>
+    public required float ViewRange { get; init; }
+
+    /// <summary>How hard the tank is to spot, moving and standing still (design §6).</summary>
+    public required Concealment Camo { get; init; }
+
     /// <summary>The stats of this gun's round of type <paramref name="ammo"/>.</summary>
     public ShellSpec Round(AmmoType ammo) => ammo == AmmoType.HE ? He : Ap;
 }
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankState.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankState.cs b/src/Tank.Sim/Vehicles/TankState.cs
index 08accee..af2c1c6 100644
--- a/src/Tank.Sim/Vehicles/TankState.cs
+++ b/src/Tank.Sim/Vehicles/TankState.cs
@@ -6,9 +6,15 @@ namespace Tank.Sim.Vehicles;
 /// <summary>Everything that persists between ticks for one tank. Height and tilt are derived (see <see cref="TankPose"/>).</summary>
 public struct TankState
 {
+    /// <summary><see cref="LastFired"/> of a tank that hasn't fired: far enough back that no "recently" window reaches it.</summary>
+    public const int NeverFired = int.MinValue / 2;
+
     public int Id;
     public TankSpec Spec;
 
+    /// <summary>Which side the tank is on; tanks of one team share what they see (design §6).</summary>
+    public byte Team;
+
     /// <summary>Ground position: X is world X, Y is world Z.</summary>
     public Vector2 Position;
 
@@ -28,6 +34,9 @@ public struct TankState
 
     public int ShotCount;
 
+    /// <summary>The tick the gun last fired, or <see cref="NeverFired"/> (firing gives away a hidden tank for a while).</summary>
+    public int LastFired;
+
     public int Health;
 
     /// <summary>A wreck ignores input and takes no more damage, but still blocks tanks and shells.</summary>
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 467、Art 445。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): view range and concealment per tank, teams, and the tick each tank last fired"
```

---

### Task 2：一次观察的规则

设计 §2.2。`Spotting.Look(observer, target, field, tick, targetMoving)` 把一次观察的全部数据装进 `Sighting`：看没看见、两车车体中心的地面距离、发现距离、有效隐蔽、算进去几丛草、是否 3 秒内开过炮、两条视线是否至少一条通。规则：

- 有效隐蔽 = 车型隐蔽（行进或静止）× 开火惩罚（3 秒 = 90 tick 内开过炮乘 0.25）+ 每丛草 0.25（最多两丛），上限 0.9；
- 草丛：观察者炮塔顶到目标车体中心的视线，在离目标 15 m 以内的那一段穿过的完好草丛（线段对竖直圆柱，`Intersect.SegmentCylinder`）；目标车体中心所在的那一丛另算：视线（连同终点）经过它中心 0.75 倍半径以内才算，所以车体中心在 0.75 倍半径以内（“在草丛里”）、或草丛挡在车前面都算，车只蹭着草丛边、或草丛在车后面不算（不这样的话，视线的终点落在草丛里，蹭边也总会被算上）。同一丛只算一次；
- 发现距离 = 15 + (视野 − 15) × (1 − 有效隐蔽)；
- 15 m 以内一定看得见；否则距离不超过发现距离，并且从观察者炮塔顶到目标车体中心、到目标炮塔顶这两条视线中至少一条没被 `Battlefield.Raycast` 挡住（它只算地面、岩石和立着的墙）。

“炮塔顶”是炮塔装甲盒中心沿车体向上方向加半个盒高（`Spotting.TurretTop`），战斗室的车就是车体盒。`Battlefield` 新增草丛下标表 `Bushes`。“行进还是静止”由调用者给（`VisionTracker` 按两次计算之间的速度和朝向算，见 Task 3）。

**Files:**
- Create: `src/Tank.Sim/Vision/Spotting.cs`
- Modify: `src/Tank.Sim/Maps/Battlefield.cs`
- Test: 新建 `tests/Tank.Sim.Tests/SpottingTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/SpottingTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>A medium tank at (100, 190) looks north (−Z) at a light tank straight ahead of it, on flat ground.</summary>
public sealed class SpottingTests
{
    private static readonly Vector2 ObserverAt = new(100, 190);
    private const int Tick = 1000;

    private static TankState Observer() => new() { Id = 1, Spec = TankCatalog.Medium, Position = ObserverAt, LastFired = TankState.NeverFired };

    /// <summary>The light tank <paramref name="distance"/> m north of the observer, side on.</summary>
    private static TankState Target(float distance) => new()
    {
        Id = 2, Spec = TankCatalog.Light, Team = 1, Position = ObserverAt - new Vector2(0, distance), Yaw = MathF.PI / 2,
        LastFired = TankState.NeverFired,
    };

    private static Prop Bush(int id, Vector2 at, float radius = 3.5f) =>
        new(id, PropKind.Bush, new Obb2(at, new Vector2(radius, radius), 0), -0.2f, 4.5f);

    /// <summary>A rock slab across the line of sight at <paramref name="z"/>, <paramref name="height"/> m tall.</summary>
    private static Prop Slab(int id, float z, float height) =>
        new(id, PropKind.Rock, new Obb2(new Vector2(100, z), new Vector2(6, 0.5f), 0), -1f, height + 1f);

    private static Sighting Look(TankState target, bool moving = false, params Prop[] props) =>
        Spotting.Look(Observer(), target, TestWorld.FlatField(props), Tick, moving);

    [Theory]
    [InlineData(270f, 0f, 270f)]
    [InlineData(270f, 0.4f, 168f)]
    [InlineData(270f, 0.65f, 104.25f)]
    [InlineData(270f, 0.9f, 40.5f)]
    [InlineData(270f, 1f, 15f)]
    public void Detection_range_shrinks_from_the_view_range_towards_15_m(float view, float camo, float range) =>
        Assert.Equal(range, Spotting.DetectionRange(view, camo), 1e-3f);

    [Fact]
    public void Concealment_is_moving_or_still_cut_by_a_recent_shot_and_raised_by_bushes_up_to_a_cap()
    {
        var light = TankCatalog.Light.Camo;   // 0.30 moving, 0.40 still
        Assert.Equal(0.40f, Spotting.Camo(light, moving: false, firedRecently: false, bushes: 0), 1e-5f);
        Assert.Equal(0.30f, Spotting.Camo(light, moving: true, firedRecently: false, bushes: 0), 1e-5f);
        Assert.Equal(0.10f, Spotting.Camo(light, moving: false, firedRecently: true, bushes: 0), 1e-5f);
        Assert.Equal(0.65f, Spotting.Camo(light, moving: false, firedRecently: false, bushes: 1), 1e-5f);
        Assert.Equal(0.35f, Spotting.Camo(light, moving: false, firedRecently: true, bushes: 1), 1e-5f);
        Assert.Equal(0.90f, Spotting.Camo(light, moving: false, firedRecently: false, bushes: 2), 1e-5f);   // 0.90 exactly
        Assert.Equal(0.80f, Spotting.Camo(light, moving: true, firedRecently: false, bushes: 3), 1e-5f);    // two at most
        Assert.Equal(0.90f, Spotting.Camo(TankCatalog.Td.Camo, moving: false, firedRecently: false, bushes: 2), 1e-5f);   // 0.95 capped
    }

    [Fact]
    public void A_shot_gives_a_tank_away_for_three_seconds()
    {
        var tank = new TankState { LastFired = 500 };
        Assert.True(Spotting.FiredRecently(tank, 500));
        Assert.True(Spotting.FiredRecently(tank, 500 + Spotting.FirePenaltyTicks - 1));
        Assert.False(Spotting.FiredRecently(tank, 500 + Spotting.FirePenaltyTicks));
        Assert.False(Spotting.FiredRecently(new TankState { LastFired = TankState.NeverFired }, 0));
        Assert.Equal(90, Spotting.FirePenaltyTicks);
    }

    [Theory]
    [InlineData(160f, false, true)]    // still: seen out to 168 m
    [InlineData(175f, false, false)]
    [InlineData(175f, true, true)]     // moving: seen out to 193.5 m
    [InlineData(200f, true, false)]
    public void In_the_open_a_tank_is_seen_within_the_detection_range(float distance, bool moving, bool seen)
    {
        Sighting look = Look(Target(distance), moving);
        Assert.Equal(seen, look.Seen);
        Assert.True(look.LineClear);
        Assert.Equal(distance, look.Distance, 1e-3f);
        Assert.Equal(moving ? 193.5f : 168f, look.DetectionRange, 1e-3f);
    }

    [Fact]
    public void Within_15_m_a_tank_is_seen_whatever_stands_between()
    {
        Sighting look = Look(Target(12f), false, Slab(1, 184, 6f));
        Assert.False(look.LineClear);
        Assert.True(look.Seen);
    }

    [Fact]
    public void A_tall_wall_between_hides_a_tank_in_range()
    {
        Sighting look = Look(Target(60f), false, Slab(1, 160, 6f));
        Assert.False(look.LineClear);
        Assert.False(look.Seen);
    }

    [Fact]
    public void One_clear_sight_line_is_enough_a_low_wall_hides_only_the_hull()
    {
        // 1.8 m tall, 21.5 m short of the target: the line to its hull centre passes at about 1.4 m (blocked), the one
        // to its turret top at about 2.4 m (clear).
        Sighting look = Look(Target(60f), false, Slab(1, 131.5f, 1.8f));
        Assert.True(look.LineClear);
        Assert.True(look.Seen);
    }

    /// <summary>Flat ground with a bank <paramref name="height"/> m high and 8 m wide across the line of sight at <paramref name="z"/>.</summary>
    private static Battlefield Ridge(float z, float height)
    {
        var heights = new float[TestWorld.Samples * TestWorld.Samples];
        for (int j = 0; j < TestWorld.Samples; j++)
        for (int i = 0; i < TestWorld.Samples; i++)
            heights[j * TestWorld.Samples + i] = MathF.Abs(j * TestWorld.Cell - z) <= 4f ? height : 0f;
        return TestWorld.Field(new Terrain.HeightField(TestWorld.Samples, TestWorld.Samples, TestWorld.Cell, heights));
    }

    [Fact]
    public void A_rise_in_the_ground_between_hides_a_tank_in_range()
    {
        Assert.False(Spotting.Look(Observer(), Target(60f), Ridge(160f, 5f), Tick, false).Seen);
        Sighting low = Spotting.Look(Observer(), Target(60f), Ridge(160f, 1f), Tick, false);   // under both sight lines
        Assert.True(low.LineClear);
        Assert.True(low.Seen);
    }

    [Fact]
    public void Fences_bushes_and_rubble_do_not_block_the_view()
    {
        Prop fence = new WallLine(PropKind.Fence, new Vector2(99, 160), new Vector2(101, 160)).Segments(TestWorld.Flat(), 1).Single();
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(97, 150), new Vector2(99, 150)).Segments(TestWorld.Flat(), 2).Single();
        Prop wall2 = new WallLine(PropKind.BrickWall, new Vector2(99, 150), new Vector2(101, 150)).Segments(TestWorld.Flat(), 3).Single();
        Battlefield field = TestWorld.FlatField(fence, wall, wall2, Bush(4, new Vector2(100, 170)));
        Assert.False(Spotting.Look(Observer(), Target(60f), field, Tick, false).LineClear, "the standing wall blocks");
        field.Destroy(field.IndexOf(3));   // the wall segment in the line turns to rubble
        Assert.True(Spotting.Look(Observer(), Target(60f), field, Tick, false).LineClear);   // tanks aren't props: rays never meet them
    }

    [Fact]
    public void A_bush_the_target_sits_in_adds_a_quarter()
    {
        Sighting look = Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-2f, 80f), 3f));   // hull centre 2 m in: within 0.75 × 3
        Assert.Equal(1, look.Bushes);
        Assert.Equal(0.65f, look.Camo, 1e-5f);
        Assert.Equal(104.25f, look.DetectionRange, 1e-3f);
        Assert.True(look.Seen);
    }

    [Fact]
    public void A_bush_short_of_the_target_hides_it_only_if_the_sight_line_cuts_it()
    {
        // The sight line runs along x = 100. A 3 m bush 5 m short of the target, centred 4 m east, misses it; centred
        // 2.5 m east the line cuts its edge.
        Assert.Equal(0, Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-4f, 75f), 3f)).Bushes);
        Assert.Equal(1, Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(-2.5f, 75f), 3f)).Bushes);
    }

    [Theory]
    [InlineData(-2.5f, 0f, 0)]    // on its east rim, the line passing 2.5 m from its middle (beyond 0.75 × 3 = 2.25 m)
    [InlineData(0f, 2.5f, 1)]     // on its far rim: the line runs through its middle first
    [InlineData(0f, -2.5f, 0)]    // on its near rim, the bush behind it
    [InlineData(0f, 2f, 1)]       // 2 m in: in it
    public void A_bush_the_target_stands_in_hides_it_only_from_its_inner_part(float east, float south, int bushes)
    {
        Vector2 target = ObserverAt - new Vector2(0, 80f);
        Sighting look = Look(Target(80f), false, Bush(1, target + new Vector2(-east, south), 3f));
        Assert.Equal(bushes, look.Bushes);
    }

    [Theory]
    [InlineData(10f, 1)]    // on the line 10 m short of the target
    [InlineData(14f, 1)]    // its edge within 15 m
    [InlineData(25f, 0)]    // too far from the target to hide it
    public void A_bush_on_the_sight_line_hides_the_target_only_near_it(float shortOf, int bushes)
    {
        Sighting look = Look(Target(80f), false, Bush(1, ObserverAt - new Vector2(0, 80f - shortOf), 2f));
        Assert.Equal(bushes, look.Bushes);
    }

    [Fact]
    public void Bushes_count_once_each_and_at_most_two()
    {
        Vector2 at = ObserverAt - new Vector2(0, 80f);
        // The target's own bush also lies on the line; two more on the line close by.
        Sighting look = Look(Target(80f), false, Bush(1, at, 4f), Bush(2, at + new Vector2(0, 6), 2f), Bush(3, at + new Vector2(0, 11), 2f));
        Assert.Equal(2, look.Bushes);
        Assert.Equal(Spotting.MaxCamo, look.Camo, 1e-5f);

        Sighting one = Look(Target(80f), false, Bush(1, at, 4f));
        Assert.Equal(1, one.Bushes);   // in it and behind it: still one bush
    }

    [Fact]
    public void A_bush_shot_away_hides_nothing()
    {
        Battlefield field = TestWorld.FlatField(Bush(1, ObserverAt - new Vector2(0, 80f), 4f));
        field.Destroy(0);
        Assert.Equal(0, Spotting.Look(Observer(), Target(80f), field, Tick, false).Bushes);
    }

    [Fact]
    public void A_shot_from_a_bush_gives_the_tank_away()
    {
        TankState hidden = Target(150f) with { LastFired = Tick - 30 };
        Sighting look = Look(hidden, false, Bush(1, ObserverAt - new Vector2(0, 150f), 4f));
        Assert.True(look.FiredRecently);
        Assert.Equal(0.35f, look.Camo, 1e-5f);
        Assert.True(look.Seen, "181 m against 150 m");
        Assert.False(Look(Target(150f), false, Bush(1, ObserverAt - new Vector2(0, 150f), 4f)).Seen, "104 m without the shot");
    }

    [Fact]
    public void The_eye_is_on_the_turret_top()
    {
        TankState medium = Observer();
        Vector3 top = Spotting.TurretTop(medium, TankPose.Compute(medium, TestWorld.Flat()));
        Assert.Equal(TankCatalog.Medium.TurretArmor.Top, top.Y, 1e-4f);
        Assert.Equal(ObserverAt.Y - TankCatalog.Medium.TurretOffset, top.Z, 1e-4f);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Spotting`、`Sighting` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/Battlefield.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/Battlefield.cs b/src/Tank.Sim/Maps/Battlefield.cs
index fab615c..5b9314a 100644
--- a/src/Tank.Sim/Maps/Battlefield.cs
+++ b/src/Tank.Sim/Maps/Battlefield.cs
@@ -128,6 +128,9 @@ public sealed class Battlefield
     /// <summary>Indices of brick and stone walls: what HE splashes and what leaves rubble.</summary>
     internal ReadOnlySpan<int> Walls => _walls;
 
+    /// <summary>Indices of bushes: what hides a tank from view.</summary>
+    internal ReadOnlySpan<int> Bushes => _bushes;
+
     internal int IndexOf(int propId) =>
         _indexOf.TryGetValue(propId, out int index) ? index : throw new ArgumentException($"No prop {propId}.", nameof(propId));
 
PATCH
```

新建 `src/Tank.Sim/Vision/Spotting.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Vision;

/// <summary>
/// What one look from an observer at a target found (stage 3 design §2.2): whether it is <see cref="Seen"/>, how far
/// away it is (ground distance between hull centres), the observer's <see cref="DetectionRange"/> against it, the
/// target's effective concealment and what went into it (bushes, a recent shot), and whether a sight line is clear.
/// </summary>
public readonly record struct Sighting(bool Seen, float Distance, float DetectionRange, float Camo, int Bushes, bool FiredRecently, bool LineClear);

/// <summary>
/// The spotting rules (design §6, stage 3 design §2.2), stateless. A target's effective concealment is its moving or
/// still <see cref="Concealment"/>, times <see cref="FirePenalty"/> within <see cref="FirePenaltyTicks"/> of a shot,
/// plus <see cref="BushBonus"/> for each bush hiding it (at most <see cref="MaxBushes"/>), capped at <see cref="MaxCamo"/>.
/// An observer spots it within <see cref="DirectRange"/> whatever stands between them; further out, within its
/// <see cref="DetectionRange"/> and with one of two sight lines (its turret top to the target's hull centre, and to the
/// target's turret top) clear of the ground, rocks and standing walls.
/// </summary>
public static class Spotting
{
    public const float DirectRange = 15f;

    /// <summary>Faster than this (m/s) a tank counts as moving.</summary>
    public const float MovingSpeed = 0.5f;

    /// <summary>A hull that turned more than this (radians) since the last look counts as moving too.</summary>
    public const float TurningAngle = 0.5f * MathF.PI / 180f;

    public const int FirePenaltyTicks = 3 * SimConstants.TickRate;
    public const float FirePenalty = 0.25f;
    public const float BushBonus = 0.25f;
    public const int MaxBushes = 2;
    public const float MaxCamo = 0.9f;

    /// <summary>Bushes on the sight line count only this close to the target, m.</summary>
    public const float BushReach = 15f;

    /// <summary>
    /// A tank is in a bush when its hull centre is within this share of the bush's radius; a bush whose edge it only
    /// touches hides it when the sight line runs through that inner part of it.
    /// </summary>
    public const float InsideBush = 0.75f;

    public static float DetectionRange(float viewRange, float camo) => DirectRange + (viewRange - DirectRange) * (1f - camo);

    public static float Camo(Concealment concealment, bool moving, bool firedRecently, int bushes) =>
        MathF.Min(MaxCamo, (moving ? concealment.Moving : concealment.Still) * (firedRecently ? FirePenalty : 1f)
            + BushBonus * System.Math.Min(bushes, MaxBushes));

    public static bool FiredRecently(in TankState tank, int tick) => tick - tank.LastFired < FirePenaltyTicks;

    /// <summary>Where a tank looks from, and is looked at: the top centre of its turret (or casemate) box.</summary>
    public static Vector3 TurretTop(in TankState tank, TankPose pose)
    {
        OrientedBox turret = pose.TurretBox(tank.Spec, tank.TurretYaw);
        return turret.Center + pose.Up * turret.HalfExtents.Y;
    }

    /// <summary>
    /// How many standing bushes hide a target (up to <see cref="MaxBushes"/>), each once: those the sight line from
    /// <paramref name="eye"/> to <paramref name="target"/> crosses within <see cref="BushReach"/> of the target, and the
    /// one its hull centre <paramref name="ground"/> stands in. That one counts only when the sight line, its end
    /// included, comes within <see cref="InsideBush"/> of its radius from its middle: a tank well inside it, or behind
    /// its middle, is hidden; one on its rim, or with the bush behind it, is not (the line would otherwise always end
    /// inside the bush it touches).
    /// </summary>
    public static int BushesHiding(Battlefield field, Vector3 eye, Vector3 target, Vector2 ground)
    {
        float length = Vector3.Distance(eye, target);
        Vector3 near = length > BushReach ? target + (eye - target) * (BushReach / length) : eye;
        int count = 0;
        foreach (int i in field.Bushes)
        {
            if (!field.StandingAt(i)) continue;
            ref readonly Prop bush = ref field.PropAt(i);
            Vector2 center = bush.Footprint.Center;
            float radius = bush.Footprint.HalfExtents.X;
            if (Vector2.Distance(center, ground) > BushReach + radius) continue;
            bool hides = Vector2.Distance(center, ground) <= radius
                ? GroundDistance(center, near, target) <= InsideBush * radius
                : Intersect.SegmentCylinder(center, radius, bush.Bottom, bush.Top, near, target, out _);
            if (hides && ++count == MaxBushes) break;
        }
        return count;
    }

    /// <summary>The ground distance from <paramref name="p"/> to the segment from <paramref name="a"/> to <paramref name="b"/>.</summary>
    private static float GroundDistance(Vector2 p, Vector3 a, Vector3 b)
    {
        Vector2 from = new(a.X, a.Z), along = new Vector2(b.X, b.Z) - from;
        float t = along.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(p - from, along) / along.LengthSquared(), 0f, 1f) : 0f;
        return Vector2.Distance(p, from + along * t);
    }

    /// <summary>
    /// One look from <paramref name="observer"/> at <paramref name="target"/> on <paramref name="tick"/>;
    /// <paramref name="targetMoving"/> says whether the target counts as moving (see <see cref="MovingSpeed"/>).
    /// </summary>
    public static Sighting Look(in TankState observer, in TankState target, Battlefield field, int tick, bool targetMoving)
    {
        float distance = Vector2.Distance(observer.Position, target.Position);
        TankPose seer = TankPose.Compute(observer, field.Terrain), seen = TankPose.Compute(target, field.Terrain);
        Vector3 eye = TurretTop(observer, seer);
        Vector3 hull = seen.HullBox(target.Spec).Center, turret = TurretTop(target, seen);
        int bushes = BushesHiding(field, eye, hull, target.Position);
        bool fired = FiredRecently(target, tick);
        float camo = Camo(target.Spec.Camo, targetMoving, fired, bushes);
        float range = DetectionRange(observer.Spec.ViewRange, camo);
        bool lineClear = !field.Raycast(eye, hull, out _) || !field.Raycast(eye, turret, out _);
        bool spotted = distance <= DirectRange || (distance <= range && lineClear);
        return new Sighting(spotted, distance, range, camo, bushes, fired, lineClear);
    }
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 496、Art 445。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): spotting rules — detection range, concealment, fire penalty, bushes and sight lines"
```

---

### Task 3：汇总：谁看见谁

设计 §2.3、§3。`VisionTracker` 由宿主在仿真每走一步之后调用一次 `Update()`：第一次调用立即计算，之后每第 6 个 tick（5 Hz）计算一次。计算时先按两次计算之间的变化给每辆车定“行进还是静止”（车速超过 0.5 m/s，或朝向变了 0.5° 以上），再让每辆活着的、看人的车看每辆活着的敌车；看见了就记下“这一队在这个 tick 看见了它”，同一队有人看见了就不再重复看。`IsVisible(tankId, team)`：自己队的车和残骸总是可见；否则最后一次被这一队看见是在 60 tick 以内。`SetWatching(tankId, false)` 让一辆车不当观察者（它照样会被看见）。`Inspect(observerId, targetId)` 当场做一次观察，给调试显示用。全部用预先分配的数组，每 tick 不分配内存。

**Files:**
- Create: `src/Tank.Sim/Vision/VisionTracker.cs`
- Test: 新建 `tests/Tank.Sim.Tests/VisionTrackerTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/VisionTrackerTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>A medium tank of team 0 at (100, 190); a light tank of team 1 on the line due north of it, standing still.</summary>
public sealed class VisionTrackerTests
{
    private static readonly Vector2 ObserverAt = new(100, 190);

    private sealed class World
    {
        public readonly Simulation Sim;
        public readonly VisionTracker Vision;
        public readonly int Observer, Target;
        private readonly List<InputCmd> _inputs = new();

        public World(float targetDistance, params Prop[] props)
        {
            Sim = new Simulation(TestWorld.FlatField(props));
            Observer = Add(TankCatalog.Medium, ObserverAt, 0f, team: 0);
            Target = Add(TankCatalog.Light, ObserverAt - new Vector2(0, targetDistance), MathF.PI / 2, team: 1);
            Vision = new VisionTracker(Sim);
        }

        public int Add(TankSpec spec, Vector2 at, float yaw, byte team)
        {
            _inputs.Add(new InputCmd(0, 0, 0, yaw, 0f, false));
            return Sim.AddTank(spec, at, yaw, team);
        }

        /// <summary>Steps <paramref name="ticks"/> times with every tank holding still, updating the vision after each step.</summary>
        public void Run(int ticks, Func<int, InputCmd>? input = null)
        {
            InputCmd[] inputs = _inputs.ToArray();
            for (int i = 0; i < ticks; i++)
            {
                if (input is not null) for (int k = 0; k < inputs.Length; k++) inputs[k] = input(k);
                Sim.Step(inputs);
                Vision.Update();
            }
        }

        public void MoveTarget(float distance) => Sim.TankRef(Target).Position = ObserverAt - new Vector2(0, distance);
    }

    [Fact]
    public void The_first_update_happens_at_once_then_every_sixth_tick()
    {
        var world = new World(100f);
        world.Run(1);
        Assert.True(world.Vision.IsVisible(world.Target, 0), "seen on the first update");

        var far = new World(250f);   // out of range
        far.Run(1);
        Assert.False(far.Vision.IsVisible(far.Target, 0));
        far.MoveTarget(100f);
        far.Run(4);                  // ticks 2–5: no update
        Assert.False(far.Vision.IsVisible(far.Target, 0));
        far.Run(1);                  // tick 6
        Assert.True(far.Vision.IsVisible(far.Target, 0));
    }

    [Fact]
    public void A_tank_out_of_sight_stays_visible_for_two_seconds()
    {
        var world = new World(100f);
        world.Run(6);                 // seen at the tick-6 update
        world.MoveTarget(250f);       // gone out of range
        world.Run(VisionTracker.LingerTicks);
        Assert.True(world.Vision.IsVisible(world.Target, 0), "still within the linger");
        world.Run(1);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        Assert.Equal(6 + VisionTracker.LingerTicks + 1, world.Sim.Tick);
    }

    [Fact]
    public void A_team_sees_what_any_of_its_tanks_sees()
    {
        // The target hides behind a tall slab from the first observer, but a second one off to the side sees round it.
        var slab = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 150), new Vector2(4, 0.5f), 0), -1f, 7f);
        var world = new World(80f, slab);
        world.Run(1);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Add(TankCatalog.Medium, new Vector2(140, 150), 0f, team: 0);
        world.Run(6);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
        Assert.False(world.Vision.Inspect(world.Observer, world.Target).Seen, "not by the first observer");
    }

    [Fact]
    public void Own_tanks_and_wrecks_are_always_visible()
    {
        var world = new World(250f);
        world.Run(1);
        Assert.True(world.Vision.IsVisible(world.Observer, 0));
        Assert.True(world.Vision.IsVisible(world.Target, 1));
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Sim.TankRef(world.Target).IsDestroyed = true;
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_revived_wreck_stays_in_view_for_the_linger()
    {
        var slab = new Prop(1, PropKind.Rock, new Obb2(new Vector2(100, 150), new Vector2(4, 0.5f), 0), -1f, 7f);
        var world = new World(80f, slab);   // hidden behind the slab
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Sim.TankRef(world.Target).IsDestroyed = true;
        world.Run(6);                       // the tick-12 update sees the wreck
        world.Sim.Revive(world.Target);
        world.Run(1);                       // tick 13: no update yet
        Assert.True(world.Vision.IsVisible(world.Target, 0), "blinked out on revival");
        world.Run(VisionTracker.LingerTicks);
        Assert.False(world.Vision.IsVisible(world.Target, 0), "hidden again once the linger is over");
    }

    [Fact]
    public void Tanks_between_do_not_block_the_view()
    {
        var world = new World(100f);
        world.Add(TankCatalog.Heavy, ObserverAt - new Vector2(0, 30f), MathF.PI / 2, team: 0);   // side on, right across the line
        world.Add(TankCatalog.Heavy, ObserverAt - new Vector2(0, 70f), MathF.PI / 2, team: 1);
        world.Run(1);
        Assert.True(world.Vision.Inspect(world.Observer, world.Target).LineClear);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_tank_that_does_not_watch_sees_nothing_but_is_still_seen()
    {
        var world = new World(100f);
        world.Vision.SetWatching(world.Observer, false);
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0), "the only watcher of team 0 is blind");
        Assert.True(world.Vision.IsVisible(world.Observer, 1), "the light tank sees the dummy");
    }

    [Fact]
    public void A_wreck_watches_nothing()
    {
        var world = new World(100f);
        world.Sim.TankRef(world.Observer).IsDestroyed = true;
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void A_tank_driving_or_turning_counts_as_moving()
    {
        // 175 m: out of range of a still light tank (168 m), within range of a moving one (193.5 m).
        var world = new World(175f);
        world.Run(6);
        Assert.False(world.Vision.IsVisible(world.Target, 0));
        world.Run(6, k => k == 1 ? TestWorld.Cmd(steer: 1, aimYaw: MathF.PI / 2) : TestWorld.Cmd());   // turning on the spot
        Assert.Equal(193.5f, world.Vision.Inspect(world.Observer, world.Target).DetectionRange, 1e-3f);
        Assert.True(world.Vision.IsVisible(world.Target, 0));
    }

    [Fact]
    public void Inspect_reports_the_same_look_as_the_rules()
    {
        var world = new World(120f);
        world.Run(6);
        Sighting look = world.Vision.Inspect(world.Observer, world.Target);
        Assert.Equal(Spotting.Look(world.Sim.Tanks[0], world.Sim.Tanks[1], world.Sim.Field, world.Sim.Tick, false), look);
        Assert.True(look.Seen);
    }

    [Fact]
    public void Updating_allocates_nothing()
    {
        var world = new World(100f);
        for (int k = 0; k < 6; k++) world.Add(TankCatalog.Heavy, new Vector2(60 + 15 * k, 120), 0f, (byte)(k % 2));
        world.Run(12);
        InputCmd[] hold = Enumerable.Range(0, world.Sim.TankCount).Select(_ => TestWorld.Cmd()).ToArray();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 120; i++)
        {
            world.Sim.Step(hold);
            world.Vision.Update();
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Updating_on_the_sandbox_map_allocates_nothing()
    {
        // Rocks, bushes and walls in the sight lines, nine tanks of two teams, one of them driving round.
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, new Vector2(224, 352), 0f, team: 0);   // in sight of the bush patch east of the spawn
        foreach (TargetSpot spot in SandboxMap.TargetSpots) sim.AddTank(spot.Spec, spot.Position, spot.Yaw, team: 1);
        sim.AddTank(TankCatalog.Light, SandboxMap.BushPatches[2] + new Vector2(-20, 0), 0f, team: 1);
        var vision = new VisionTracker(sim);
        var inputs = new InputCmd[sim.TankCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = TestWorld.Cmd(throttle: (sbyte)(i == inputs.Length - 1 ? 1 : 0), steer: (sbyte)(i == inputs.Length - 1 ? 1 : 0));
        for (int i = 0; i < 30; i++) { sim.Step(inputs); vision.Update(); }
        long before = GC.GetAllocatedBytesForCurrentThread();
        int seen = 0;
        for (int i = 0; i < 300; i++)
        {
            sim.Step(inputs);
            vision.Update();
            foreach (TankState t in sim.Tanks) if (vision.IsVisible(t.Id, 0)) seen++;
            if (vision.Inspect(sim.Tanks[0].Id, sim.Tanks[^1].Id).Bushes > 0) seen++;
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(seen > 300, "some of them were seen");
    }

    [Fact]
    public void Asking_about_a_team_out_of_range_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new World(100f).Vision.IsVisible(1, VisionTracker.MaxTeams));
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`VisionTracker` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Vision/VisionTracker.cs`：

```csharp
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Vision;

/// <summary>
/// Who can see whom (stage 3 design §2.3), kept beside a <see cref="Simulation"/>: call <see cref="Update"/> after every
/// <see cref="Simulation.Step"/>. On the first call, and then every <see cref="UpdateInterval"/>-th tick (5 Hz), it has
/// every live, watching tank look at every live enemy (<see cref="Spotting.Look"/>) and notes, per target and team,
/// the last tick a tank of that team saw it. A tank stays visible to a team for <see cref="LingerTicks"/> after that; it
/// is always visible to its own team, and a wreck to everyone (each update notes it as seen by every team, so a revived
/// tank stays in view for the linger rather than vanishing until the next update). Tanks are kept by slot in <see cref="Simulation.Tanks"/>
/// (slots never change: tanks are only ever added). Nothing allocates after construction.
/// </summary>
public sealed class VisionTracker
{
    public const int UpdateInterval = 6;
    public const int LingerTicks = 2 * SimConstants.TickRate;
    public const int MaxTeams = SimConstants.MaxTeams;
    private const int NeverSeen = int.MinValue / 2;

    private readonly Simulation _sim;
    private readonly int[] _lastSeen = new int[Simulation.MaxTanks * MaxTeams];   // [slot * MaxTeams + team]
    private readonly bool[] _blind = new bool[Simulation.MaxTanks];
    private readonly bool[] _moving = new bool[Simulation.MaxTanks];
    private readonly float[] _lastYaw = new float[Simulation.MaxTanks];
    private int _known;            // slots whose last yaw has been noted
    private bool _started;         // the first update has run

    public VisionTracker(Simulation sim)
    {
        _sim = sim;
        Array.Fill(_lastSeen, NeverSeen);
    }

    /// <summary>A tank that doesn't watch is never an observer (a practice dummy), though others still see it.</summary>
    public void SetWatching(int tankId, bool watching) => _blind[SlotOf(tankId)] = !watching;

    /// <summary>Call after every step: on the first call and every <see cref="UpdateInterval"/>-th tick, works out who sees whom.</summary>
    public void Update()
    {
        ReadOnlySpan<TankState> tanks = _sim.Tanks;
        for (; _known < tanks.Length; _known++) _lastYaw[_known] = tanks[_known].Yaw;
        int tick = _sim.Tick;
        if (_started && tick % UpdateInterval != 0) return;
        _started = true;
        for (int j = 0; j < tanks.Length; j++)
        {
            ref readonly TankState t = ref tanks[j];
            _moving[j] = MathF.Abs(t.Speed) > Spotting.MovingSpeed || MathF.Abs(Angles.Wrap(t.Yaw - _lastYaw[j])) > Spotting.TurningAngle;
            _lastYaw[j] = t.Yaw;
        }
        for (int j = 0; j < tanks.Length; j++)
        {
            ref readonly TankState target = ref tanks[j];
            if (target.IsDestroyed)
            {
                for (int team = 0; team < MaxTeams; team++) _lastSeen[j * MaxTeams + team] = tick;   // wrecks are seen by all
                continue;
            }
            for (int i = 0; i < tanks.Length; i++)
            {
                ref readonly TankState observer = ref tanks[i];
                if (observer.Team == target.Team || observer.IsDestroyed || _blind[i]) continue;
                int seen = j * MaxTeams + observer.Team;
                if (_lastSeen[seen] == tick) continue;   // a team mate already saw it this time
                if (Spotting.Look(observer, target, _sim.Field, tick, _moving[j]).Seen) _lastSeen[seen] = tick;
            }
        }
    }

    /// <summary>Whether tank <paramref name="tankId"/> is visible to <paramref name="team"/> as of the last update.</summary>
    public bool IsVisible(int tankId, byte team)
    {
        if (team >= MaxTeams) throw new ArgumentOutOfRangeException(nameof(team), team, $"Teams are 0 … {MaxTeams - 1}.");
        int slot = SlotOf(tankId);
        ref readonly TankState tank = ref _sim.Tanks[slot];
        if (tank.Team == team || tank.IsDestroyed) return true;
        return _sim.Tick - _lastSeen[slot * MaxTeams + team] <= LingerTicks;
    }

    /// <summary>
    /// What <paramref name="observerId"/> looking at <paramref name="targetId"/> would find right now, with the target
    /// moving or not as of the last update: the numbers behind the rule, for a debug display.
    /// </summary>
    public Sighting Inspect(int observerId, int targetId)
    {
        int target = SlotOf(targetId);
        return Spotting.Look(_sim.Tanks[SlotOf(observerId)], _sim.Tanks[target], _sim.Field, _sim.Tick, _moving[target]);
    }

    private int SlotOf(int tankId)
    {
        ReadOnlySpan<TankState> tanks = _sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
            if (tanks[i].Id == tankId) return i;
        throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
    }
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 509、Art 445。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): vision tracker — who sees whom at 5 Hz, shared by team, lingering two seconds"
```

---

### Task 4：巡逻驾驶和试驾场路线

设计 §4.2（与设计不同之处见“写计划时已核实”）。`PatrolDriver` 每 tick 为一辆车给出 `InputCmd`：

- 朝下一个路点转向行驶，方向偏出 35° 以上时油门为零、原地转过来；
- 等待点（`WaitSeconds > 0`）和路线在此转弯超过 35° 的路点，都提前刹车、在 2 m 以内停稳才算到达；其余路点 3 m 以内就算经过；
- 等待时第一发不早于停稳后 2.5 秒，之后每 4 秒一发穿甲弹，瞄准 `shootAt`（玩家）前方 25 m、再往左 8 m 的地面，炮转到位（炮塔偏航和炮管俯仰都在 1° 以内）才开；`shootAt` 在 50 m 以内时不开炮；
- 行驶时炮朝正前方；残骸原地不动，复活后接着跑。

试驾场：AI 靶车是一辆轻坦，从 `PatrolStart` (270, 278) 出发，路线经过离出生点 117 m 和 129 m 的两片草丛中心，各停 10 秒，从两片草丛之间那座小山的西边绕回来，一圈约 48 秒。测试在真实地图上检查：路线避开岩石、墙、靶车和出生点到大山的通道；连跑三圈不卡住、不偏离路线、离岩石至少 2 m、不撞倒东西；不管玩家站在出生点还是两片草丛周围 60～150 m 的 48 个位置（贴着地图边的会挤到一起，xUnit 会提示跳过重复的测试用例，不算失败），AI 的炮弹都打不中玩家。

**Files:**
- Create: `src/Tank.Sim/Ai/PatrolDriver.cs`
- Modify: `src/Tank.Sim/Maps/SandboxMap.cs`
- Test: 新建 `tests/Tank.Sim.Tests/PatrolDriverTests.cs`、`SandboxPatrolTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/PatrolDriverTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>A light tank driven by a <see cref="PatrolDriver"/> on flat open ground; a second tank stands still at <c>shootAt</c>.</summary>
public sealed class PatrolDriverTests
{
    private const int Tps = SimConstants.TickRate;

    private sealed class World
    {
        public readonly Simulation Sim = new(TestWorld.FlatField());
        public readonly PatrolDriver Driver;
        public readonly int Patrol, Other;
        public readonly Vector2 ShootAt;
        public readonly List<(int Tick, Vector3 At)> Fired = new(), Bursts = new();
        public readonly List<int> WaitStarts = new();
        public int Laps;

        public World(PatrolPoint[] route, Vector2 start, float yaw, Vector2 shootAt)
        {
            Driver = new PatrolDriver(route);
            ShootAt = shootAt;
            Patrol = Sim.AddTank(TankCatalog.Light, start, yaw);
            Other = Sim.AddTank(TankCatalog.Medium, shootAt, 0f);
        }

        public ref TankState Tank => ref Sim.TankRef(Patrol);

        public void Run(int ticks)
        {
            var inputs = new InputCmd[2];
            for (int i = 0; i < ticks; i++)
            {
                int tick = Sim.Tick + 1;
                bool waiting = Driver.Waiting;
                int next = Driver.Next;
                inputs[0] = Driver.Command(Sim.Tanks[0], Sim.Field, tick, ShootAt);
                inputs[1] = new InputCmd(tick, 0, 0, 0f, 0f, false);
                Sim.Step(inputs);
                if (!waiting && Driver.Waiting) WaitStarts.Add(tick);
                if (Driver.Next == 0 && next != 0) Laps++;
                foreach (GameEvent e in Sim.Events)
                {
                    if (e.TankId != Patrol) continue;
                    if (e.Type == GameEventType.ShellFired) Fired.Add((tick, e.Position));
                    if (e.Type == GameEventType.ShellExploded) Bursts.Add((tick, e.Position));
                    Assert.False(e.Type is GameEventType.TankHit or GameEventType.SplashDamage, $"the patrol hit tank {e.TargetTankId}");
                }
            }
        }
    }

    private static readonly Vector2 Start = new(100, 180);

    /// <summary>Stop 10 s at (100, 120), 60 m north of the start; then 40 m east and back.</summary>
    private static PatrolPoint[] NorthThenEast() => new PatrolPoint[]
    {
        new(new Vector2(100, 120), 10f),
        new(new Vector2(140, 120), 0f),
        new(Start, 0f),
    };

    [Fact]
    public void A_route_needs_two_points()
    {
        Assert.Throws<ArgumentException>(() => new PatrolDriver(new[] { new PatrolPoint(Start, 1f) }));
    }

    [Fact]
    public void It_drives_to_a_point_it_waits_at_and_stops_on_it()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 40));
        world.Run(10 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.True(world.WaitStarts[0] < 8 * Tps, $"took {world.WaitStarts[0] / (float)Tps:F1} s to drive 60 m");
        Assert.True(world.Driver.Waiting);
        Assert.Equal(0, world.Driver.Next);
        Assert.InRange(Vector2.Distance(world.Tank.Position, new Vector2(100, 120)), 0f, PatrolDriver.StopRadius);
        Assert.Equal(0f, world.Tank.Speed, 0.3f);
    }

    [Fact]
    public void It_waits_ten_seconds_then_drives_on_to_the_next_point()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 40));
        world.Run(8 * Tps);
        int stopped = world.WaitStarts.Single();
        world.Run(stopped + 10 * Tps - 1 - world.Sim.Tick);
        Assert.True(world.Driver.Waiting, "still waiting 1 tick short of 10 s");
        Vector2 there = world.Tank.Position;
        world.Run(1);
        Assert.False(world.Driver.Waiting);
        Assert.Equal(1, world.Driver.Next);
        world.Run(3 * Tps);
        Assert.True(world.Tank.Position.X > there.X + 5f, $"has not headed east: {world.Tank.Position}");
    }

    [Fact]
    public void A_point_behind_it_is_turned_to_on_the_spot_first()
    {
        var world = new World(NorthThenEast(), Start, MathF.PI, new Vector2(100, 40));   // facing south, away from the point
        var input = world.Driver.Command(world.Tank, world.Sim.Field, 1, world.ShootAt);
        Assert.Equal(0, input.Throttle);
        Assert.NotEqual(0, input.Steer);
        world.Run(10 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.InRange(Vector2.Distance(world.Tank.Position, new Vector2(100, 120)), 0f, PatrolDriver.StopRadius);
    }

    [Fact]
    public void Where_the_route_turns_sharply_it_slows_down_and_turns_on_the_spot()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));   // turns 124° at (140, 120)
        float speedThere = -1f;
        for (int i = 0; i < 40 * Tps && speedThere < 0; i++)
        {
            int next = world.Driver.Next;
            world.Run(1);
            if (next == 1 && world.Driver.Next == 2) speedThere = world.Tank.Speed;
        }
        Assert.InRange(MathF.Abs(speedThere), 0f, 0.5f);
        Vector2 corner = new(140, 120);
        world.Run(2 * Tps);
        float off = SandboxMap.DistanceToSegment(world.Tank.Position, corner, Start);
        Assert.True(off < 1.5f, $"{off:F1} m off the leg after the corner");
    }

    [Fact]
    public void While_waiting_it_fires_every_four_seconds_starting_once_stopped_for_two_and_a_half()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));   // 100 m due north of the stop
        world.Run(8 * Tps);
        Assert.Empty(world.Fired);
        int stopped = world.WaitStarts.Single();
        world.Run(stopped + 10 * Tps - world.Sim.Tick);
        Assert.Equal(2, world.Fired.Count);
        Assert.InRange(world.Fired[0].Tick - stopped, (int)(PatrolDriver.FirstShot * Tps), (int)((PatrolDriver.FirstShot + 1) * Tps));
        Assert.Equal((int)(PatrolDriver.FireEvery * Tps), world.Fired[1].Tick - world.Fired[0].Tick);
        world.Run(20 * Tps);
        Assert.Equal(2, world.Fired.Count);   // none while driving on
    }

    /// <summary>The stop is at (100, 120); the tank arrives facing north. The point is 100 m north of it, or 100 m south (behind).</summary>
    [Theory]
    [InlineData(-1f)]
    [InlineData(1f)]
    public void It_shoots_the_ground_25_m_short_of_the_point_and_8_m_to_its_left(float south)
    {
        var stop = new Vector2(100, 120);
        var toward = new Vector2(0, south);                // unit vector from the stop to the point
        var left = new Vector2(toward.Y, -toward.X);
        var world = new World(NorthThenEast(), Start, 0f, stop + toward * 100f);
        world.Run(20 * Tps);
        Assert.Equal(2, world.Bursts.Count);
        Assert.All(world.Bursts, b =>
        {
            var at = new Vector2(b.At.X, b.At.Z) - stop;
            float along = Vector2.Dot(at, toward), aside = Vector2.Dot(at, left);   // the shot comes in low: a little dispersion moves it a lot along the line
            Assert.InRange(along, 65f, 85f);
            Assert.InRange(aside, 6.5f, 11f);
            Assert.Equal(0f, b.At.Y, 0.5f);   // on the ground
        });
    }

    [Fact]
    public void A_point_within_fifty_metres_gets_no_shot()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 120 - PatrolDriver.HoldFireWithin + 5f));
        world.Run(20 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.Empty(world.Fired);
    }

    [Fact]
    public void It_keeps_going_round_the_route()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(80 * Tps);
        Assert.InRange(world.Laps, 2, 4);
        Assert.InRange(world.WaitStarts.Count, world.Laps, world.Laps + 1);   // one stop a lap
    }

    [Fact]
    public void Driving_waiting_and_shooting_allocate_nothing_per_tick()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(20 * Tps);   // warm up: drive, stop, wait, shoot, drive on
        var inputs = new InputCmd[2];
        int shots = 0, stops = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 40 * Tps; i++)   // round again to the stop and shoot there
        {
            int tick = world.Sim.Tick + 1;
            bool waiting = world.Driver.Waiting;
            inputs[0] = world.Driver.Command(world.Sim.Tanks[0], world.Sim.Field, tick, world.ShootAt);
            inputs[1] = new InputCmd(tick, 0, 0, 0f, 0f, false);
            world.Sim.Step(inputs);
            if (!waiting && world.Driver.Waiting) stops++;
            foreach (GameEvent e in world.Sim.Events) if (e.TankId == world.Patrol && e.Type == GameEventType.ShellFired) shots++;
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(stops >= 1 && shots >= 2, $"{stops} stops and {shots} shots while measuring");
        Assert.Equal(0, allocated);
    }

    [Fact]
    public void Knocked_out_while_waiting_it_drives_on_once_revived_after_the_wait()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(8 * Tps);
        int stopped = world.WaitStarts.Single();
        world.Tank.IsDestroyed = true;
        world.Tank.Health = 0;
        world.Run(stopped + 12 * Tps - world.Sim.Tick);   // lies wrecked past the end of its wait
        Assert.Equal(new Vector2(100, 120), world.Tank.Position, new Near(PatrolDriver.StopRadius));
        world.Sim.Revive(world.Patrol);
        world.Run(3 * Tps);
        Assert.Equal(1, world.Driver.Next);
        Assert.True(world.Tank.Position.X > 105f, $"has not driven on east: {world.Tank.Position}");
    }

    private sealed class Near(float tolerance) : IEqualityComparer<Vector2>
    {
        public bool Equals(Vector2 a, Vector2 b) => Vector2.Distance(a, b) <= tolerance;
        public int GetHashCode(Vector2 v) => 0;
    }

    [Fact]
    public void A_wreck_holds_still_and_carries_on_once_revived()
    {
        var world = new World(NorthThenEast(), Start, 0f, new Vector2(100, 20));
        world.Run(2 * Tps);
        world.Tank.IsDestroyed = true;
        world.Tank.Health = 0;
        InputCmd input = world.Driver.Command(world.Tank, world.Sim.Field, world.Sim.Tick + 1, world.ShootAt);
        Assert.Equal(0, input.Throttle);
        Assert.Equal(0, input.Steer);
        Assert.False(input.Fire);
        world.Sim.Revive(world.Patrol);
        world.Run(10 * Tps);
        Assert.Single(world.WaitStarts);
        Assert.Equal(0, world.Driver.Next);
    }
}
```

新建 `tests/Tank.Sim.Tests/SandboxPatrolTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The patrolling target's route on the sandbox map (stage 3 design §4.2).</summary>
public sealed class SandboxPatrolTests
{
    private const int Tps = SimConstants.TickRate;
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly IReadOnlyList<PatrolPoint> Route = SandboxMap.PatrolRoute;

    private static IEnumerable<(Vector2 From, Vector2 To)> Legs() =>
        Route.Select((p, i) => (p.Position, Route[(i + 1) % Route.Count].Position));

    private static float DistanceToRoute(Vector2 p) => Legs().Min(leg => SandboxMap.DistanceToSegment(p, leg.From, leg.To));

    [Fact]
    public void It_stops_ten_seconds_in_two_bush_patches_about_120_m_from_the_spawn()
    {
        PatrolPoint[] stops = Route.Where(p => p.WaitSeconds > 0).ToArray();
        Assert.Equal(2, stops.Length);
        Assert.All(stops, stop =>
        {
            Assert.Equal(10f, stop.WaitSeconds);
            Assert.Contains(stop.Position, SandboxMap.BushPatches);
            Assert.InRange(Vector2.Distance(stop.Position, SandboxMap.Spawn), 105f, 135f);
        });
        Assert.Equal(SandboxMap.PatrolStart, Route[^1].Position);
    }

    [Fact]
    public void The_route_keeps_clear_of_rocks_walls_targets_and_the_lane()
    {
        foreach (var (from, to) in Legs())
        {
            float yaw = Angles.YawOf(to - from), length = Vector2.Distance(from, to);
            for (float s = 0; s <= length; s += 0.5f)
            {
                Vector2 at = Vector2.Lerp(from, to, s / length);
                Obb2 hull = TankMotion.Footprint(SandboxMap.PatrolSpec, at, yaw);
                Obb2 roomy = hull with { HalfExtents = hull.HalfExtents + new Vector2(1f) };   // a metre to spare all round
                Assert.All(Field.Props.Where(p => p.Kind != PropKind.Bush), p =>
                    Assert.False(Obb2.TryGetPenetration(roomy, p.Footprint, out _), $"{p.Kind} {p.Id} is in the way at {at}"));
                Assert.All(SandboxMap.TargetSpots, t => Assert.True(Vector2.Distance(t.Position, at) >= 20f, $"target at {t.Position} is near {at}"));
                Assert.True(SandboxMap.DistanceToSegment(at, SandboxMap.Spawn, SandboxMap.BigHill) >= 30f, $"{at} is near the lane");
                float slope = MathF.Acos(Field.Terrain.NormalAt(at.X, at.Y).Y);
                Assert.True(slope <= Angles.Deg(10), $"{slope * 180 / MathF.PI:F1}° at {at}");
            }
        }
    }

    [Fact]
    public void It_laps_three_times_without_straying_coming_near_anything_solid_or_knocking_anything_down()
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw, team: 0);
        foreach (TargetSpot spot in SandboxMap.TargetSpots) sim.AddTank(spot.Spec, spot.Position, spot.Yaw, team: 1);
        int patrol = sim.AddTank(SandboxMap.PatrolSpec, SandboxMap.PatrolStart, Angles.YawOf(Route[0].Position - SandboxMap.PatrolStart), team: 1);
        int slot = sim.TankCount - 1;
        var driver = new PatrolDriver(Route);
        var inputs = new InputCmd[sim.TankCount];
        Prop[] solid = sim.Field.Props.Where(p => p.Kind != PropKind.Bush).ToArray();
        int laps = 0, tick = 0;
        float farthest = 0f;
        while (laps < 3 && tick < 3 * 60 * Tps)
        {
            tick++;
            for (int i = 0; i < inputs.Length; i++) inputs[i] = new InputCmd(tick, 0, 0, sim.Tanks[i].Yaw, 0f, false);
            int next = driver.Next;
            inputs[slot] = driver.Command(sim.Tanks[slot], sim.Field, tick, sim.Tanks[0].Position);
            sim.Step(inputs);
            if (next != 0 && driver.Next == 0) laps++;
            TankState tank = sim.Tanks[slot];
            farthest = MathF.Max(farthest, DistanceToRoute(tank.Position));
            Obb2 hull = TankMotion.Footprint(tank.Spec, tank.Position, tank.Yaw);
            Obb2 roomy = hull with { HalfExtents = hull.HalfExtents + new Vector2(2f) };
            Assert.All(solid, p => Assert.False(Obb2.TryGetPenetration(roomy, p.Footprint, out _), $"within 2 m of {p.Kind} {p.Id} at {tank.Position}"));
            foreach (GameEvent e in sim.Events)
                Assert.False(e.TankId == patrol && e.Type is GameEventType.TankHit or GameEventType.SplashDamage, $"the patrol hit tank {e.TargetTankId}");
        }
        Assert.Equal(3, laps);
        Assert.True(tick <= 3 * 50 * Tps, $"three laps took {tick / (float)Tps:F0} s");
        Assert.True(farthest <= 3f, $"strayed {farthest:F1} m off the route");
        Assert.All(sim.Field.Props.Where(p => p.Kind != PropKind.Bush), p => Assert.True(sim.Field.IsStanding(p.Id), $"{p.Kind} {p.Id} was knocked down"));
    }

    /// <summary>The spawn, and eight points round each bush patch it stops in, 60, 100 and 150 m out.</summary>
    public static TheoryData<float, float> Bystanders()
    {
        var data = new TheoryData<float, float> { { SandboxMap.Spawn.X, SandboxMap.Spawn.Y } };
        foreach (PatrolPoint stop in Route.Where(p => p.WaitSeconds > 0))
        foreach (float distance in new[] { 60f, 100f, 150f })
            for (int k = 0; k < 8; k++)
            {
                Vector2 at = Field.ClampToPlayable(stop.Position + Angles.Forward(k * MathF.PI / 4) * distance);
                data.Add(at.X, at.Y);
            }
        return data;
    }

    [Theory]
    [MemberData(nameof(Bystanders))]
    public void Its_shots_never_hit_the_player(float x, float z)
    {
        var sim = new Simulation(SandboxMap.Create());
        sim.AddTank(TankCatalog.Medium, new Vector2(x, z), 0f, team: 0);
        int patrol = sim.AddTank(SandboxMap.PatrolSpec, SandboxMap.PatrolStart, Angles.YawOf(Route[0].Position - SandboxMap.PatrolStart), team: 1);
        var driver = new PatrolDriver(Route);
        var inputs = new InputCmd[2];
        int shots = 0;
        for (int tick = 1; tick <= 50 * Tps; tick++)
        {
            inputs[0] = new InputCmd(tick, 0, 0, 0f, 0f, false);
            inputs[1] = driver.Command(sim.Tanks[1], sim.Field, tick, sim.Tanks[0].Position);
            sim.Step(inputs);
            foreach (GameEvent e in sim.Events)
            {
                if (e.TankId == patrol && e.Type == GameEventType.ShellFired) shots++;
                Assert.False(e.TankId == patrol && e.Type is GameEventType.TankHit or GameEventType.SplashDamage, $"hit the player after {tick / (float)Tps:F1} s");
            }
        }
        if (Vector2.Distance(new Vector2(x, z), Route[0].Position) > PatrolDriver.HoldFireWithin) Assert.True(shots >= 2, $"only {shots} shots");
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Tank.Sim.Ai`、`PatrolDriver`、`PatrolPoint`、`SandboxMap.PatrolRoute` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Sim/Ai/PatrolDriver.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>A point on a patrol route, and how long to stand there (0: drive straight on).</summary>
public readonly record struct PatrolPoint(Vector2 Position, float WaitSeconds);

/// <summary>
/// Drives one tank round a looped route (stage 3 design §4.2), producing its <see cref="InputCmd"/> each tick: it heads
/// for the next point (turning on the spot first when that lies more than <see cref="TurnInPlace"/> off its heading),
/// brakes in time to stop on a point it waits at or where the route turns sharper than that, and while waiting fires AP
/// every <see cref="FireEvery"/> s at the ground <see cref="AimShort"/> m short of a given point (the player) and
/// <see cref="AimAside"/> m to its left, so that it gives itself away without hitting anything there; the first shot no
/// sooner than <see cref="FirstShot"/> s after it stops, only once the gun is laid, and none at all while the point is
/// within <see cref="HoldFireWithin"/> m. While driving the gun looks straight ahead. A wreck holds still; once revived
/// it drives on from where it stands to the point it was heading for (a wait that ran out while it lay wrecked is over).
/// Its state is its own, not the simulation's.
/// </summary>
public sealed class PatrolDriver
{
    /// <summary>A point driven through counts as reached this close, m.</summary>
    public const float PassRadius = 3f;

    /// <summary>A point to wait at, or a sharp corner, counts as reached once the hull has stopped within this of it, m.</summary>
    public const float StopRadius = 2f;

    public static readonly float TurnInPlace = Angles.Deg(35);
    private static readonly float SteerDeadZone = Angles.Deg(2);
    public const float FireEvery = 4f;
    public const float FirstShot = 2.5f;
    public const float AimShort = 25f;

    /// <summary>
    /// The aim is also this far to the side of the line to the point, m: the shot comes in low, so a little dispersion
    /// upwards carries it well past the aim, and this way it still flies by the point at least this far off.
    /// </summary>
    public const float AimAside = 8f;

    /// <summary>Nearer than this the point gets no shot at all, m.</summary>
    public const float HoldFireWithin = 2 * AimShort;

    /// <summary>The gun fires only once it is laid this close to the aim, in turret yaw and gun pitch (radians).</summary>
    public static readonly float OnTarget = Angles.Deg(1);

    /// <summary>Slower than this (m/s) the hull counts as stopped.</summary>
    private const float StoppedSpeed = 0.3f;

    /// <summary>Extra distance allowed for when braking to a stop, m.</summary>
    private const float BrakeMargin = 0.5f;

    private readonly IReadOnlyList<PatrolPoint> _route;
    private readonly bool[] _sharp;   // per point: the route turns more than TurnInPlace there
    private int _waitUntil = -1;
    private int _nextShot;

    public PatrolDriver(IReadOnlyList<PatrolPoint> route)
    {
        if (route.Count < 2) throw new ArgumentException("A patrol needs at least two points.", nameof(route));
        _route = route;
        _sharp = new bool[route.Count];
        for (int i = 0; i < route.Count; i++)
        {
            Vector2 before = route[(i + route.Count - 1) % route.Count].Position, at = route[i].Position, after = route[(i + 1) % route.Count].Position;
            _sharp[i] = MathF.Abs(Angles.Wrap(Angles.YawOf(after - at) - Angles.YawOf(at - before))) > TurnInPlace;
        }
    }

    /// <summary>The index of the point being driven to, or waited at.</summary>
    public int Next { get; private set; }

    public bool Waiting => _waitUntil >= 0;

    /// <summary>The tank's command for <paramref name="tick"/>; <paramref name="shootAt"/> is the point it fires short of.</summary>
    public InputCmd Command(in TankState tank, Battlefield field, int tick, Vector2 shootAt)
    {
        var hold = new InputCmd(tick, 0, 0, tank.Yaw, 0f, false);
        if (tank.IsDestroyed) return hold;
        if (Waiting)
        {
            if (tick < _waitUntil) return Shoot(tank, field, tick, shootAt);
            _waitUntil = -1;
            Next = (Next + 1) % _route.Count;
        }
        PatrolPoint point = _route[Next];
        float distance = Vector2.Distance(tank.Position, point.Position);
        bool stopsHere = point.WaitSeconds > 0 || _sharp[Next];
        if (stopsHere ? distance <= StopRadius && MathF.Abs(tank.Speed) < StoppedSpeed : distance <= PassRadius)
        {
            if (point.WaitSeconds > 0)
            {
                _waitUntil = tick + (int)MathF.Round(point.WaitSeconds * SimConstants.TickRate);
                _nextShot = tick + (int)MathF.Round(FirstShot * SimConstants.TickRate);
                return hold;
            }
            Next = (Next + 1) % _route.Count;
            point = _route[Next];
            distance = Vector2.Distance(tank.Position, point.Position);
            stopsHere = point.WaitSeconds > 0 || _sharp[Next];
        }
        float off = Angles.Wrap(Angles.YawOf(point.Position - tank.Position) - tank.Yaw);
        sbyte steer = (sbyte)(MathF.Abs(off) <= SteerDeadZone ? 0 : off > 0 ? -1 : 1);   // +yaw is to the left; Steer +1 turns right
        bool braking = stopsHere && distance <= tank.Speed * tank.Speed / (2 * tank.Spec.Braking) + BrakeMargin;
        sbyte throttle = (sbyte)(MathF.Abs(off) > TurnInPlace || braking || distance <= StopRadius / 2 ? 0 : 1);
        return hold with { Throttle = throttle, Steer = steer };
    }

    /// <summary>
    /// Lays the gun on the ground <see cref="AimShort"/> m short of <paramref name="shootAt"/> and <see cref="AimAside"/>
    /// m to its left (seen from the tank) and, when a shot is due and the gun has come round to within
    /// <see cref="OnTarget"/>, fires. Within <see cref="HoldFireWithin"/> of the point the gun looks straight ahead instead.
    /// </summary>
    private InputCmd Shoot(in TankState tank, Battlefield field, int tick, Vector2 shootAt)
    {
        Vector2 toward = shootAt - tank.Position;
        float length = toward.Length();
        if (length <= HoldFireWithin) return new InputCmd(tick, 0, 0, tank.Yaw, 0f, false);
        Vector2 along = toward / length;
        Vector2 ground = shootAt - along * AimShort + new Vector2(along.Y, -along.X) * AimAside;   // (y, −x): to the left
        var aim = new Vector3(ground.X, field.Terrain.HeightAt(ground.X, ground.Y), ground.Y);
        TankPose pose = TankPose.Compute(tank, field.Terrain);
        var (yaw, pitch) = Angles.AimAngles(aim - pose.Trunnion(tank.Spec), tank.Yaw);
        var (turret, gun) = pose.ToHullAngles(Angles.Direction(yaw, pitch), tank.TurretYaw);
        bool laid = MathF.Abs(Angles.Wrap(turret - tank.TurretYaw)) <= OnTarget && MathF.Abs(gun - tank.GunPitch) <= OnTarget;
        bool fire = tick >= _nextShot && laid;
        if (fire) _nextShot = tick + (int)MathF.Round(FireEvery * SimConstants.TickRate);
        return new InputCmd(tick, 0, 0, yaw, pitch, fire, AmmoType.AP);
    }
}
```

修改 `src/Tank.Sim/Maps/SandboxMap.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/SandboxMap.cs b/src/Tank.Sim/Maps/SandboxMap.cs
index 66f00a0..eea89a0 100644
--- a/src/Tank.Sim/Maps/SandboxMap.cs
+++ b/src/Tank.Sim/Maps/SandboxMap.cs
@@ -1,4 +1,5 @@
 using System.Numerics;
+using Tank.Sim.Ai;
 using Tank.Sim.Geometry;
 using Tank.Sim.Terrain;
 using Tank.Sim.Vehicles;
@@ -80,6 +81,28 @@ public static class SandboxMap
     /// <summary>The index in <see cref="TargetSpots"/> of the target inside the yard.</summary>
     public const int YardTarget = 6;
 
+    /// <summary>The kind of tank that drives <see cref="PatrolRoute"/> (stage 3 design §4.2).</summary>
+    public static TankSpec PatrolSpec => TankCatalog.Light;
+
+    /// <summary>Where the patrolling target starts, facing its first point (the bush patch east of it).</summary>
+    public static readonly Vector2 PatrolStart = new(270, 278);
+
+    /// <summary>
+    /// The patrolling target's loop, about 120 m east and north-east of the spawn: it stops 10 s in the middle of two
+    /// bush patches (306, 280) and (256, 214), driving round the west of the hill between them. Clear of rocks, walls,
+    /// targets and the spawn-to-hill lane; it starts at <see cref="PatrolStart"/>, the last point.
+    /// </summary>
+    public static readonly IReadOnlyList<PatrolPoint> PatrolRoute = new PatrolPoint[]
+    {
+        new(BushPatches[2], 10f),
+        new(new Vector2(272, 266), 0f),
+        new(new Vector2(262, 238), 0f),
+        new(BushPatches[3], 10f),
+        new(new Vector2(242, 238), 0f),
+        new(new Vector2(242, 262), 0f),
+        new(PatrolStart, 0f),
+    };
+
     /// <summary>
     /// Hand-placed walls and fences (stage 2C design §3.6). The yard, 18 × 12.8 m inside around (160, 340): a stone wall
     /// on the east side, facing the spawn; brick on the north and south; brick on the west with a gate of three fence
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 573、Art 445。`SandboxPatrolTests` 要跑几秒（每个玩家位置跑一圈）。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): a patrolling target — drives a loop, waits in bushes, fires short of the player and to one side"
```

---

### Task 5：AI 靶车上场，看不见的不画

设计 §4.1～§4.3。玩家是第 0 队，7 辆固定靶车和 AI 靶车是第 1 队；固定靶车不看人。`SandboxMain` 建 `VisionTracker`，每步之后 `Update()`。`TargetRange`：

- 在固定靶车之后加上 AI 靶车（带扬尘），由 `PatrolDriver` 驾驶，`shootAt` 是玩家的位置；打靶演示时它停在起点、不开炮、也不看人（“被点亮”灯不亮），以免打乱原有的截图；
- 每辆靶车记下最近两个 tick 的状态，按插值显示（插值函数从 `SandboxMain` 挪到 `TankInterpolation`，不依赖 Godot，可以测试）；
- 玩家这一队看不见的靶车不画；`IsHidden` 给 HUD 用。

HUD 不显示看不见的靶车的血条、命中大字和伤害数字；炮弹曳光、炮口火光和爆炸照常显示。`SandboxAutomation` 的“命中、击毁、墙受伤、物件被毁”这几个时机只算玩家造成的（AI 靶车会开炮，也可能打掉草丛）。

**Files:**
- Create: `src/Tank.Client/scripts/Sandbox/TankInterpolation.cs`
- Modify: `src/Tank.Client/scripts/Sandbox/TargetRange.cs`、`SandboxMain.cs`、`SandboxHud.cs`、`SandboxAutomation.cs`
- Test: 新建 `tests/Tank.Art.Tests/TankInterpolationTests.cs`；改 `Tank.Art.Tests.csproj`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index bd7691b..7406eeb 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -16,8 +16,8 @@
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
     <!-- So are the launch options (they need Tank.Core and Tank.Net for the old game's switches). -->
     <Compile Include="..\..\src\Tank.Client\scripts\Bootstrap\LaunchOptions.cs" LinkBase="Bootstrap" />
-    <!-- So are the scripted range demos (only sim maths). -->
-    <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs" LinkBase="Sandbox" />
+    <!-- So are the scripted range demos and the tick interpolation (only sim maths). -->
+    <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs;..\..\src\Tank.Client\scripts\Sandbox\TankInterpolation.cs" LinkBase="Sandbox" />
     <!-- Popup timing curves are engine-neutral too. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs" LinkBase="Overlay" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
PATCH
```

新建 `tests/Tank.Art.Tests/TankInterpolationTests.cs`：

```csharp
using Tank.Client.Sandbox;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Art.Tests;

public class TankInterpolationTests
{
    private static TankState State(float x, float yaw, float turret, float pitch, float speed, int health) => new()
    {
        Id = 3, Spec = TankCatalog.Light, Position = new SN.Vector2(x, 50), Yaw = yaw, TurretYaw = turret,
        GunPitch = pitch, Speed = speed, Bloom = speed / 10, Health = health,
    };

    [Fact]
    public void Blends_place_headings_gun_speed_and_bloom_and_takes_the_rest_from_the_latest_tick()
    {
        TankState a = State(10, 0.2f, 0.1f, 0f, 4f, 800), b = State(12, 0.4f, -0.1f, 0.1f, 6f, 600);
        TankState mid = TankInterpolation.Between(a, b, 0.25f);
        Assert.Equal(10.5f, mid.Position.X, 4);
        Assert.Equal(0.25f, mid.Yaw, 4);
        Assert.Equal(0.05f, mid.TurretYaw, 4);
        Assert.Equal(0.025f, mid.GunPitch, 4);
        Assert.Equal(4.5f, mid.Speed, 4);
        Assert.Equal(0.45f, mid.Bloom, 4);
        Assert.Equal(600, mid.Health);
    }

    [Fact]
    public void Headings_take_the_short_way_across_a_half_turn()
    {
        TankState a = State(0, 3.1f, 3.1f, 0, 0, 1), b = State(0, -3.1f, -3.1f, 0, 0, 1);
        TankState mid = TankInterpolation.Between(a, b, 0.5f);
        Assert.True(MathF.Abs(mid.Yaw) > 3.1f, $"yaw {mid.Yaw} went the long way round");
        Assert.True(MathF.Abs(mid.TurretYaw) > 3.1f);
    }

    [Fact]
    public void The_ends_are_the_two_ticks()
    {
        TankState a = State(10, 0.2f, 0.1f, 0f, 4f, 800), b = State(12, 0.4f, -0.1f, 0.1f, 6f, 600);
        Assert.Equal(a.Position, TankInterpolation.Between(a, b, 0f).Position);
        Assert.Equal(b with { }, TankInterpolation.Between(a, b, 1f));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS|error MSB|warning MSB" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`TankInterpolation.cs` 不存在或 `TankInterpolation` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
index 4450dca..a51630f 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
@@ -15,7 +15,7 @@ namespace Tank.Client.Sandbox;
 /// --camera-preset (stand still). Timed steps wait for a <see cref="Moment"/>, each noted the first time it happens:
 /// the screenshot is taken a delay after the run's start (--screenshot-at), the player's first shot (--fx-shot, or
 /// --shot-after fire), the first tank hit or kill (--shot-after hit|kill), or the first wall hurt or prop destroyed
-/// (--shot-after prop|down), and a range demo that fires once stops after the player's first shot. With --fx-shot,
+/// (--shot-after prop|down), each by the player (the patrolling target's shots and fences it drives into don't count), and a range demo that fires once stops after the player's first shot. With --fx-shot,
 /// nothing before a warm-up counts. Also --fps-log and --quit-after.
 /// Moments and the delays after them are wall-clock seconds (the screenshot is of what is on screen); only a range
 /// demo's first shot is timed in simulation ticks (<see cref="RangeDemo.FireTick"/>), so the shot itself is repeatable.
@@ -67,12 +67,13 @@ public sealed class SandboxAutomation
             GD.PushWarning($"Unknown --range-demo '{demo}'; use {string.Join(", ", RangeDemo.Names)}.");
     }
 
-    /// <summary>Notes the first time the player fired, a tank was hit or destroyed, a wall was hurt and a prop destroyed.</summary>
+    /// <summary>Notes the first time the player fired, hit or destroyed a tank, hurt a wall and destroyed a prop.</summary>
     public void OnEvent(in GameEvent e, int playerId, double time)
     {
+        if (e.TankId != playerId) return;
         Moment? moment = e.Type switch
         {
-            GameEventType.ShellFired when e.TankId == playerId => Moment.Fire,
+            GameEventType.ShellFired => Moment.Fire,
             GameEventType.TankHit => Moment.Hit,
             GameEventType.TankDestroyed => Moment.Kill,
             GameEventType.PropDamaged => Moment.Prop,
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxHud.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
index 197e418..7b76f63 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
@@ -15,7 +15,7 @@ namespace Tank.Client.Sandbox;
 /// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' and the damaged walls' health
 /// bars (over the popups, so a bar stays in sight while it drops), the reticle and the status panel.
 /// <see cref="OnEvent"/> spawns hit verdicts and damage numbers (over targets and walls); <see cref="Refresh"/> updates
-/// the rest once per frame. A wall's bar, smaller than a tank's and drawn on its face, shows only while it is standing
+/// the rest once per frame. A target the player's team doesn't see gets no bar, verdict or damage number. A wall's bar, smaller than a tank's and drawn on its face, shows only while it is standing
 /// and short of full hit points.
 /// </summary>
 public partial class SandboxHud : CanvasLayer
@@ -58,6 +58,8 @@ public partial class SandboxHud : CanvasLayer
     {
         switch (e.Type)
         {
+            case GameEventType.TankHit or GameEventType.SplashDamage when _targets.IsHidden(e.TargetTankId):
+                break;
             case GameEventType.TankHit:
                 _popups.Verdict(e.Position.ToGodot(), e.Result);
                 if (e.Damage > 0 && _targets.TryGetHead(e.TargetTankId, out Vector3 head)) _popups.Damage(head, e.Damage, splash: false);
@@ -84,8 +86,8 @@ public partial class SandboxHud : CanvasLayer
         _vignette.Visible = _camera.Sniper;
         for (int i = 0; i < _targets.Count; i++)
         {
-            var (head, health, alive) = _targets.Status(i);
-            _bars.Set(i, head, health, alive);
+            var (head, health, alive, shown) = _targets.Status(i);
+            _bars.Set(i, head, health, alive && shown);
         }
         for (int i = 0; i < _walls.Length; i++)
         {
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 350730d..49eaa44 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -9,13 +9,15 @@ using Tank.Sim.Combat;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
+using Tank.Sim.Vision;
 using SN = System.Numerics;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0;
-/// the practice targets (<see cref="TargetRange"/>) take the next slots. Keys: 1 AP, 2 HE, R restock, T restore the walls,
+/// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0
+/// (team 0); the practice targets (<see cref="TargetRange"/>, team 1, the patrolling one last) take the next slots. A
+/// <see cref="VisionTracker"/> works out after every step who sees whom: targets the player's team doesn't see aren't drawn. Keys: 1 AP, 2 HE, R restock, T restore the walls,
 /// fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
@@ -49,6 +51,7 @@ public partial class SandboxMain : Node3D
     private bool _playerFired;
     private ShellViews _shells = null!;
     private TargetRange _targets = null!;
+    private VisionTracker _vision = null!;
     private BattleFx _fx = null!;
     private SandboxHud _hud = null!;
     private double _accumulator;
@@ -66,10 +69,12 @@ public partial class SandboxMain : Node3D
         RangeDemo? demo = _automation.Demo;
         _sim = new Simulation(SandboxMap.Create());
         TankSpec start = _options.Tank is { } id ? TankCatalog.ById(id) : TankCatalog.Medium;
-        _sim.AddTank(start, demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), demo?.PlayerYaw ?? SandboxMap.SpawnYaw);
+        _sim.AddTank(start, demo?.PlayerPosition ?? CameraPresets.Spawn(_options.CameraPreset), demo?.PlayerYaw ?? SandboxMap.SpawnYaw, TargetRange.PlayerTeam);
         _previous = _current = _sim.Tanks[0];
+        _vision = new VisionTracker(_sim);
         _targets = Add(new TargetRange());
-        _targets.Build(_sim);
+        _targets.Build(_sim, _vision, parkPatrol: demo is not null);
+        _vision.Update();   // who is seen at the start, before the first frame is drawn
         _commands = new InputCmd[_sim.TankCount];
         _groundAt = _sim.Field.Terrain.HeightAt;
         _aimYaw = _current.Yaw;
@@ -140,12 +145,12 @@ public partial class SandboxMain : Node3D
         }
 
         float alpha = (float)(_accumulator / SimConstants.Dt);
-        TankState tank = Interpolate(_previous, _current, alpha);
+        TankState tank = TankInterpolation.Between(_previous, _current, alpha);
         TankPose pose = TankPose.Compute(tank, _sim.Field.Terrain);
         ShowPlayer(tank, pose, (float)delta);
         UpdateCamera(tank, pose);
         _world.Update(tank, pose, _camera.Camera.GlobalPosition, (float)delta);
-        _targets.Sync(_sim, (float)delta);
+        _targets.Sync(_sim, alpha, (float)delta);
         _shells.Sync(_sim.Shells, alpha, (float)delta);
         _hud.Refresh(tank);
         _automation.EndFrame(delta, _time);
@@ -161,8 +166,10 @@ public partial class SandboxMain : Node3D
         var own = new InputCmd(_sim.Tick + 1, throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo);
         _commands[0] = _automation.Command(own, _current, _sim.Field, _time);   // a scripted run may drive and aim instead
         (_aimYaw, _aimPitch) = (_commands[0].AimYaw, _commands[0].AimPitch);
-        _targets.FillInputs(_commands.AsSpan(1), _sim.Tick + 1);
+        _targets.FillInputs(_commands.AsSpan(1), _sim.Tick + 1, _sim, _current.Position);
         _sim.Step(_commands);
+        _vision.Update();
+        _targets.AfterStep(_sim);
         foreach (GameEvent e in _sim.Events) OnEvent(e);
         _previous = _current;
         _current = _sim.Tanks[0];
@@ -264,16 +271,6 @@ public partial class SandboxMain : Node3D
         (_aimYaw, _aimPitch) = Angles.AimAngles(target - trunnion, _aimYaw);
     }
 
-    private static TankState Interpolate(in TankState a, in TankState b, float t) => b with
-    {
-        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
-        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
-        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
-        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
-        Speed = a.Speed + (b.Speed - a.Speed) * t,
-        Bloom = a.Bloom + (b.Bloom - a.Bloom) * t,
-    };
-
     private T Add<T>(T node) where T : Node
     {
         AddChild(node);
PATCH
```

新建 `src/Tank.Client/scripts/Sandbox/TankInterpolation.cs`：

```csharp
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Sandbox;

/// <summary>
/// A tank drawn between two simulation ticks: the sandbox renders one tick behind, <c>t</c> of the way from the previous
/// state to the latest. Place, headings, gun, speed and aim circle are blended; everything else is the latest tick's.
/// Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public static class TankInterpolation
{
    public static TankState Between(in TankState a, in TankState b, float t) => b with
    {
        Position = SN.Vector2.Lerp(a.Position, b.Position, t),
        Yaw = Angles.Lerp(a.Yaw, b.Yaw, t),
        TurretYaw = Angles.Lerp(a.TurretYaw, b.TurretYaw, t),
        GunPitch = a.GunPitch + (b.GunPitch - a.GunPitch) * t,
        Speed = a.Speed + (b.Speed - a.Speed) * t,
        Bloom = a.Bloom + (b.Bloom - a.Bloom) * t,
    };
}
```

修改 `src/Tank.Client/scripts/Sandbox/TargetRange.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/TargetRange.cs b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
index 8a87912..8032789 100644
--- a/src/Tank.Client/scripts/Sandbox/TargetRange.cs
+++ b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
@@ -4,66 +4,108 @@ using Tank.Client.Combat;
 using Tank.Client.Vehicles;
 using Tank.Client.World;
 using Tank.Sim;
+using Tank.Sim.Ai;
+using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Terrain;
 using Tank.Sim.Vehicles;
+using Tank.Sim.Vision;
 using SN = System.Numerics;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// The practice targets on <see cref="SandboxMap.TargetSpots"/>: blue-grey tanks of every kind that sit still with
-/// their guns straight ahead. A knocked-out target blows up, throws its turret (a tank destroyer has none), chars and
-/// smokes; ten seconds later it is revived where it stands. Each <see cref="Sync"/> also notes every target's head point and health for the HUD.
+/// The practice targets, all of team <see cref="TargetTeam"/>: blue-grey tanks of every kind on
+/// <see cref="SandboxMap.TargetSpots"/> that sit still with their guns straight ahead and watch nobody, and then the
+/// patrolling target (stage 3 design §4.2), a light tank that a <see cref="PatrolDriver"/> drives round
+/// <see cref="SandboxMap.PatrolRoute"/> from <see cref="SandboxMap.PatrolStart"/>, firing short of the player from the
+/// bushes; while parked it holds still at its start instead. A knocked-out target blows up, throws its turret (a tank
+/// destroyer has none), chars and smokes; ten seconds later it is revived where it stands (the patrol then drives on).
+/// A target the player's team doesn't see (<see cref="VisionTracker"/>) isn't drawn. Each <see cref="Sync"/> also notes
+/// every target's head point, health and whether it is shown, for the HUD.
 /// </summary>
 public partial class TargetRange : Node3D
 {
+    public const byte PlayerTeam = 0, TargetTeam = 1;
     public const float ReviveSeconds = 10f;
     private const float BlastSize = 1.8f;
     private const float HeadMargin = 0.6f;   // the head point floats this far above the turret's armour box
     private static readonly TankPaint Paint = new(Palette.TargetBody, Palette.Track, Palette.TankHub);
 
     private readonly List<Target> _targets = new();
+    private VisionTracker _vision = null!;
+    private PatrolDriver _driver = null!;
+    private bool _patrolParked;
 
     private sealed class Target
     {
         public required int Id;
         public required int Slot;   // index in Simulation.Tanks
-        public required TargetSpot Spot;
+        public required float Yaw;  // the heading it was placed with: a still target's gun stays straight ahead
         public required TankModel Model;
         public required WreckSmoke Smoke;
+        public DustTrail? Dust;     // the patrol's only
+        public TankState Previous, Current;   // the last two ticks, drawn between
         public int WreckedTick = -1;
         public Vector3 Head;        // world point over the turret, as of the latest Sync
         public float Health = 1f;   // share left, 0 … 1
         public bool Alive = true;
+        public bool Shown = true;
     }
 
+    /// <summary>How many targets there are: the still ones, in <see cref="SandboxMap.TargetSpots"/> order, then the patrol.</summary>
     public int Count => _targets.Count;
 
-    /// <summary>Adds the targets to the simulation (after the player, so they take the next slots) and builds their models.</summary>
-    public void Build(Simulation sim)
+    /// <summary>The index of the patrolling target, after the still ones.</summary>
+    public static int PatrolIndex => SandboxMap.TargetSpots.Count;
+
+    /// <summary>The patrolling target's tank id.</summary>
+    public int PatrolId => _targets[PatrolIndex].Id;
+
+    /// <summary>
+    /// Adds the targets to the simulation (after the player, so they take the next slots), tells
+    /// <paramref name="vision"/> the still ones watch nobody, and builds their models. With <paramref name="parkPatrol"/>
+    /// the patrol stays at its start, holds its fire and watches nobody either (range demos, so their screenshots stay as
+    /// they were, the spotted lamp included).
+    /// </summary>
+    public void Build(Simulation sim, VisionTracker vision, bool parkPatrol)
     {
+        (_vision, _patrolParked) = (vision, parkPatrol);
         foreach (TargetSpot spot in SandboxMap.TargetSpots)
         {
-            int slot = sim.TankCount;
-            int id = sim.AddTank(spot.Spec, spot.Position, spot.Yaw);
-            var model = new TankModel();
-            AddChild(model);
-            model.Build(spot.Spec, Paint);
-            var smoke = new WreckSmoke();
-            model.AddChild(smoke);
-            smoke.Build(model.TurretRing);   // pours from the open turret ring (a casemate's roof)
-            var target = new Target { Id = id, Slot = slot, Spot = spot, Model = model, Smoke = smoke };
-            Observe(target, sim.Tanks[slot], sim.Field.Terrain);   // heads are known before the first Sync
-            _targets.Add(target);
+            Target target = Add(sim, spot.Spec, spot.Position, spot.Yaw);
+            vision.SetWatching(target.Id, false);
         }
+        _driver = new PatrolDriver(SandboxMap.PatrolRoute);
+        SN.Vector2 start = SandboxMap.PatrolStart;
+        Target patrol = Add(sim, SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start));
+        if (parkPatrol) vision.SetWatching(patrol.Id, false);
+        patrol.Dust = new DustTrail();
+        patrol.Model.AddChild(patrol.Dust);
+        patrol.Dust.Build(TankLooks.For(SandboxMap.PatrolSpec.Id).TrackX, SandboxMap.PatrolSpec.HullLength / 2f);
+    }
+
+    private Target Add(Simulation sim, TankSpec spec, SN.Vector2 position, float yaw)
+    {
+        int slot = sim.TankCount;
+        int id = sim.AddTank(spec, position, yaw, TargetTeam);
+        var model = new TankModel();
+        AddChild(model);
+        model.Build(spec, Paint);
+        var smoke = new WreckSmoke();
+        model.AddChild(smoke);
+        smoke.Build(model.TurretRing);   // pours from the open turret ring (a casemate's roof)
+        var target = new Target { Id = id, Slot = slot, Yaw = yaw, Model = model, Smoke = smoke, Previous = sim.Tanks[slot], Current = sim.Tanks[slot] };
+        Observe(target, target.Current, sim.Field.Terrain);   // heads are known before the first Sync
+        _targets.Add(target);
+        return target;
     }
 
-    /// <summary>Target <paramref name="index"/>'s head point (where its health bar goes), health left (0 … 1) and whether it is alive.</summary>
-    public (Vector3 Head, float Health, bool Alive) Status(int index)
+    /// <summary>Target <paramref name="index"/>'s head point (where its health bar goes), health left (0 … 1), whether it is alive and whether it is drawn.</summary>
+    public (Vector3 Head, float Health, bool Alive, bool Shown) Status(int index)
     {
         Target target = _targets[index];
-        return (target.Head, target.Health, target.Alive);
+        return (target.Head, target.Health, target.Alive, target.Shown);
     }
 
     /// <summary>The point over target <paramref name="tankId"/>'s turret, for damage numbers; false for a tank that isn't a target.</summary>
@@ -74,10 +116,28 @@ public partial class TargetRange : Node3D
         return target is not null;
     }
 
-    /// <summary>Every target holds still with its gun straight ahead. <paramref name="commands"/> starts at the first target's slot.</summary>
-    public void FillInputs(Span<InputCmd> commands, int tick)
+    /// <summary>True for a target the player's team doesn't see now: it isn't drawn, nor are hits on it.</summary>
+    public bool IsHidden(int tankId) => Find(tankId) is not null && !_vision.IsVisible(tankId, PlayerTeam);
+
+    /// <summary>
+    /// The targets' commands for <paramref name="tick"/>; <paramref name="commands"/> starts at the first target's slot.
+    /// The still ones hold with their guns straight ahead; the patrol drives, firing short of <paramref name="player"/>,
+    /// unless parked.
+    /// </summary>
+    public void FillInputs(Span<InputCmd> commands, int tick, Simulation sim, SN.Vector2 player)
+    {
+        for (int i = 0; i < _targets.Count; i++) commands[i] = new InputCmd(tick, 0, 0, _targets[i].Yaw, 0f, false);
+        if (!_patrolParked)
+        {
+            Target patrol = _targets[PatrolIndex];
+            commands[PatrolIndex] = _driver.Command(sim.Tanks[patrol.Slot], sim.Field, tick, player);
+        }
+    }
+
+    /// <summary>Notes every target's newest state, keeping the one before to draw between. Call after each step.</summary>
+    public void AfterStep(Simulation sim)
     {
-        for (int i = 0; i < _targets.Count; i++) commands[i] = new InputCmd(tick, 0, 0, _targets[i].Spot.Yaw, 0f, false);
+        foreach (Target t in _targets) (t.Previous, t.Current) = (t.Current, sim.Tanks[t.Slot]);
     }
 
     /// <summary>Revives wrecks that have lain for <see cref="ReviveSeconds"/>. Call before stepping the simulation.</summary>
@@ -111,14 +171,21 @@ public partial class TargetRange : Node3D
         }
     }
 
-    /// <summary>Places every target model on its (still) tank, runs its wreck animation and notes its head and health.</summary>
-    public void Sync(Simulation sim, float delta)
+    /// <summary>
+    /// Shows the targets the player's team sees, each <paramref name="alpha"/> of the way from its previous tick to its
+    /// latest, runs their animations and notes their heads, health and visibility.
+    /// </summary>
+    public void Sync(Simulation sim, float alpha, float delta)
     {
         foreach (Target target in _targets)
         {
-            TankState state = sim.Tanks[target.Slot];
+            TankState state = TankInterpolation.Between(target.Previous, target.Current, alpha);
+            target.Shown = _vision.IsVisible(target.Id, PlayerTeam);
+            target.Model.Visible = target.Shown;
             target.Model.Apply(Observe(target, state, sim.Field.Terrain), state.TurretYaw, state.GunPitch);
-            target.Model.Animate(0f, 0f, delta);
+            float yawRate = Angles.Wrap(target.Current.Yaw - target.Previous.Yaw) / SimConstants.Dt;
+            target.Model.Animate(state.Speed, yawRate, delta);
+            target.Dust?.Update(state.Speed);
         }
     }
 
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 573、Art 448；客户端 0 警告 0 错误。

- [ ] **Step 5：截图**

```bash
importall
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
shot t5-front front
demoshot t5-kill kill kill:1.5 30
```

用 Read 打开，核对：
- `t5-front`：和以前一样，镜头在玩家坦克左前方，界面文字齐全；
- `t5-kill`：0 号靶车被打掉（焦黑、炮塔掉在一旁），其他看得见的靶车头上有血条；输出里没有 `ERROR`、`Exception`。

AI 靶车本身要到 Task 7 的机位才看得清。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(sandbox): the patrolling target drives its loop, and targets the player's team doesn't see aren't drawn"
```

---

### Task 6：“被点亮”灯和调试文字

设计 §4.3。

- 状态面板右上角（和车名同一高度）加一个卡通小灯：被敌方看见时（`IsVisible(玩家, 1)`）亮起，暖黄色、带光晕和光芒，左边写“被点亮”；没被看见时是暗的。玩家是残骸时不亮。
- 调试文字：每辆靶车头上一行（`SpotText.Format`，不依赖 Godot），例如“看不见  109 m / 66 m  隐蔽 0.80（草×2）”；括号里列出算进去几丛草、刚开过炮、两条视线都被挡（15 m 以外才列），没有就不写括号。刚看不见、还在 2 秒保持期内、所以仍然画着的车，开头写“保持中”。画着的车字是白的，看不见的车也显示，字是淡灰的。数字变了才重新拼字符串。V 键开关，启动参数 `--spot-debug` 一开始就打开。

**Files:**
- Create: `src/Tank.Client/scripts/Overlay/SpotText.cs`、`SpotLabels.cs`
- Modify: `src/Tank.Client/scripts/Overlay/StatusPanel.cs`、`scripts/Sandbox/SandboxHud.cs`、`SandboxInput.cs`、`SandboxMain.cs`、`TargetRange.cs`、`scripts/Bootstrap/LaunchOptions.cs`
- Test: 新建 `tests/Tank.Art.Tests/SpotTextTests.cs`；改 `LaunchOptionsTests.cs`、`Tank.Art.Tests.csproj`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/LaunchOptionsTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/LaunchOptionsTests.cs b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
index 542a68e..109bea4 100644
--- a/tests/Tank.Art.Tests/LaunchOptionsTests.cs
+++ b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
@@ -12,6 +12,15 @@ public class LaunchOptionsTests
         Assert.Empty(options.Warnings);
     }
 
+    [Fact]
+    public void Spot_debug_is_off_unless_asked_for()
+    {
+        Assert.False(LaunchOptions.Parse(Array.Empty<string>()).SpotDebug);
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--spot-debug", "--camera-preset", "patrol" });
+        Assert.True(options.SpotDebug);
+        Assert.Equal("patrol", options.CameraPreset);
+    }
+
     [Fact]
     public void No_tank_by_default()
     {
PATCH
```

新建 `tests/Tank.Art.Tests/SpotTextTests.cs`：

```csharp
using Tank.Client.Overlay;
using Tank.Sim.Vision;

namespace Tank.Art.Tests;

public class SpotTextTests
{
    [Fact]
    public void A_tank_hiding_in_two_bushes()
    {
        var s = new Sighting(Seen: false, Distance: 109.4f, DetectionRange: 66.1f, Camo: 0.8f, Bushes: 2, FiredRecently: false, LineClear: true);
        Assert.Equal("看不见  109 m / 66 m  隐蔽 0.80（草×2）", SpotText.Format(s));
    }

    [Fact]
    public void A_tank_lost_a_moment_ago_is_still_held()
    {
        var s = new Sighting(false, 95f, 193.5f, 0.3f, 0, false, LineClear: false);
        Assert.StartsWith("保持中  95 m", SpotText.Format(s, lingering: true));
        Assert.StartsWith("看不见", SpotText.Format(s));
        Assert.StartsWith("看见", SpotText.Format(s with { Seen = true }, lingering: true));
    }

    [Fact]
    public void A_tank_in_the_open_lists_nothing_in_brackets()
    {
        var s = new Sighting(true, 91f, 193.5f, 0.3f, 0, false, true);
        Assert.Equal("看见  91 m / 194 m  隐蔽 0.30", SpotText.Format(s));
    }

    [Fact]
    public void A_shot_and_blocked_sight_lines_are_listed()
    {
        var s = new Sighting(false, 120f, 117f, 0.6f, 1, FiredRecently: true, LineClear: false);
        Assert.Equal("看不见  120 m / 117 m  隐蔽 0.60（草×1 开火 挡住）", SpotText.Format(s));
    }

    [Fact]
    public void Blocked_sight_lines_dont_matter_inside_the_direct_range()
    {
        var s = new Sighting(true, 12f, 40.5f, 0.9f, 2, false, LineClear: false);
        Assert.DoesNotContain("挡住", SpotText.Format(s));
    }
}
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index 7406eeb..7c79701 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -18,8 +18,8 @@
     <Compile Include="..\..\src\Tank.Client\scripts\Bootstrap\LaunchOptions.cs" LinkBase="Bootstrap" />
     <!-- So are the scripted range demos and the tick interpolation (only sim maths). -->
     <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs;..\..\src\Tank.Client\scripts\Sandbox\TankInterpolation.cs" LinkBase="Sandbox" />
-    <!-- Popup timing curves are engine-neutral too. -->
-    <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs" LinkBase="Overlay" />
+    <!-- Popup timing curves and the spotting debug text are engine-neutral too. -->
+    <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs;..\..\src\Tank.Client\scripts\Overlay\SpotText.cs" LinkBase="Overlay" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Vehicles\TankLook.cs;..\..\src\Tank.Client\scripts\Vehicles\TankLooks.cs;..\..\src\Tank.Client\scripts\Vehicles\TankParts.cs;..\..\src\Tank.Client\scripts\Vehicles\TankAnimator.cs;..\..\src\Tank.Client\scripts\Vehicles\TurretToss.cs" LinkBase="Vehicles" />
   </ItemGroup>
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS|error MSB|warning MSB" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`SpotText.cs` 不存在或 `SpotText`、`LaunchOptions.SpotDebug` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index ceac3e0..5585e78 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -14,7 +14,7 @@ namespace Tank.Client.Bootstrap;
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
 /// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush（试驾场：自动对靶车开炮或开车撞墙，用于截图）
 /// --shot-after fire|hit|kill|prop|down:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤或第一次有物件被毁后再过 SECONDS 秒截图）
-/// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）
+/// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）  --spot-debug（试驾场：一开始就显示点亮调试文字，同 V 键）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
 /// for the caller to report.
 /// </summary>
@@ -39,6 +39,7 @@ public sealed record LaunchOptions
     public bool FpsLog { get; init; }
     public double? FxShotDelay { get; init; }
     public bool Sniper { get; init; }
+    public bool SpotDebug { get; init; }
     public string? RangeDemo { get; init; }
 
     /// <summary>"fire", "hit", "kill", "prop" or "down" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
@@ -75,6 +76,7 @@ public sealed record LaunchOptions
                 "--camera-preset" => o with { CameraPreset = Next() },
                 "--fps-log" => o with { FpsLog = true },
                 "--sniper" => o with { Sniper = true },
+                "--spot-debug" => o with { SpotDebug = true },
                 "--fx-shot" => o with { FxShotDelay = Seconds("--fx-shot", Next(), 0.05, warnings) },
                 "--range-demo" => o with { RangeDemo = Next() },
                 "--shot-after" => ParseShotAfter(o, Next(), warnings),
PATCH
```

新建 `src/Tank.Client/scripts/Overlay/SpotLabels.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Sim.Vision;

namespace Tank.Client.Overlay;

/// <summary>
/// The spotting debug display: one <see cref="SpotText"/> line over each enemy's head, above its health bar, in white
/// while the tank is shown and faded grey while it is hidden (hidden tanks get a line too, to check the rule against). Shown only within
/// <see cref="MaxRange"/> m of the camera and in front of it. Feed it every frame with <see cref="Set"/>; a line's text
/// is rebuilt only when its numbers change.
/// </summary>
public partial class SpotLabels : Control
{
    private const float MaxRange = 400f;
    private const int TextSize = 18;
    private const float Above = 26f;        // pixels from the head point up to the line's middle, clear of the health bar
    private const float HiddenAlpha = 0.55f;

    private Label[] _labels = Array.Empty<Label>();
    private Camera3D? _camera;

    private struct Label
    {
        public bool HasLine;
        public Vector3 Head;
        public Sighting Sighting;
        public bool Drawn;     // the tank is on screen: seen now, or lately
        public string Text;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Setup(Camera3D camera, int count)
    {
        _camera = camera;
        _labels = new Label[count];
    }

    /// <param name="drawn">The tank is drawn: seen now, or lately.</param>
    /// <param name="hasLine">False for no line (a wreck).</param>
    public void Set(int index, Vector3 head, in Sighting sighting, bool drawn, bool hasLine)
    {
        ref Label label = ref _labels[index];
        if (label.Text is null || sighting != label.Sighting || drawn != label.Drawn) label.Text = SpotText.Format(sighting, lingering: drawn && !sighting.Seen);
        (label.HasLine, label.Head, label.Sighting, label.Drawn) = (hasLine, head, sighting, drawn);
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_camera is null) return;
        Vector3 eye = _camera.GlobalPosition;
        foreach (Label label in _labels)
        {
            if (!label.HasLine || label.Head.DistanceTo(eye) > MaxRange || _camera.IsPositionBehind(label.Head)) continue;
            Vector2 at = _camera.UnprojectPosition(label.Head);
            if (!GetViewportRect().HasPoint(at)) continue;
            Color color = label.Drawn ? HudStyle.Text : new Color(Palette.HitBlocked.ToColor(), HiddenAlpha);
            HudStyle.DrawOutlinedCentered(this, at.X, HudStyle.Baseline(at.Y - Above, TextSize), label.Text, TextSize, color, 4);
        }
    }
}
```

新建 `src/Tank.Client/scripts/Overlay/SpotText.cs`：

```csharp
using System.Globalization;
using System.Text;
using Tank.Sim.Vision;

namespace Tank.Client.Overlay;

/// <summary>
/// The spotting debug line over an enemy (stage 3 design §4.3): seen or not ("保持中" when not seen now but still shown,
/// for up to 2 s after it was last seen), distance and detection range, and the effective concealment with what went
/// into it — "看不见  109 m / 66 m  隐蔽 0.80（草×2）". The bracket lists the
/// bushes counted, a recent shot ("开火") and both sight lines blocked ("挡住", only past the 15 m rule); it is left out
/// when there is nothing to list. Engine-neutral so Tank.Art.Tests can check it.
/// </summary>
public static class SpotText
{
    /// <param name="lingering">The tank is still shown, though not seen now.</param>
    public static string Format(in Sighting s, bool lingering = false)
    {
        var text = new StringBuilder(48);
        text.Append(s.Seen ? "看见" : lingering ? "保持中" : "看不见").Append("  ");
        text.Append(Metres(s.Distance)).Append(" / ").Append(Metres(s.DetectionRange)).Append("  ");
        text.Append("隐蔽 ").Append(s.Camo.ToString("F2", CultureInfo.InvariantCulture));
        var why = new List<string>(3);
        if (s.Bushes > 0) why.Add($"草×{s.Bushes}");
        if (s.FiredRecently) why.Add("开火");
        if (!s.LineClear && s.Distance > Spotting.DirectRange) why.Add("挡住");
        if (why.Count > 0) text.Append('（').Append(string.Join(' ', why)).Append('）');
        return text.ToString();
    }

    private static string Metres(float m) => MathF.Round(m).ToString("F0", CultureInfo.InvariantCulture) + " m";
}
```

修改 `src/Tank.Client/scripts/Overlay/StatusPanel.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/StatusPanel.cs b/src/Tank.Client/scripts/Overlay/StatusPanel.cs
index efd1628..ee32094 100644
--- a/src/Tank.Client/scripts/Overlay/StatusPanel.cs
+++ b/src/Tank.Client/scripts/Overlay/StatusPanel.cs
@@ -8,8 +8,9 @@ namespace Tank.Client.Overlay;
 /// Bottom-left rounded panel in three rows: a capsule health bar with the hit points; a speedometer icon with the speed
 /// as a big outlined number and a shell icon with the reload state ("就绪" in the accent colour, or the seconds left);
 /// and two ammo chips, "1 穿甲 40" and "2 高爆 15", the loaded one outlined in the accent colour, an empty one greyed.
-/// The vehicle's name ("中型坦克") sits just above the panel. Icons are drawn with primitives, no image files. Redraws
-/// only when something it shows changes.
+/// The vehicle's name ("中型坦克") sits just above the panel, and the "被点亮" lamp on its top-right corner: dark, or
+/// lit warm yellow with a glow, rays and the words "被点亮" while an enemy sees the player (stage 3 design §4.3). Icons are
+/// drawn with primitives, no image files. Redraws only when something it shows changes.
 /// </summary>
 public partial class StatusPanel : Control
 {
@@ -30,6 +31,11 @@ public partial class StatusPanel : Control
     private const float ChipPad = 10f, ChipGap = 12f;
     private const int ChipRadius = 10;
     private const float PanelHeight = Pad + HealthRow + RowGap + GaugeRow + RowGap + ChipRow + Pad;
+    private const float LampRadius = 12f;
+    private const float LampRise = 10f;   // the lamp's centre sits this far above the panel's top edge, level with the name
+    private const int LampRays = 8;
+    private const int SpottedSize = 22;
+    private static readonly Color LampOff = new(0.29f, 0.31f, 0.37f);
 
     // Shell icon, centred on the origin; the outline is the same polygon scaled up. Built once.
     private static readonly Vector2[] ShellBody =
@@ -60,6 +66,7 @@ public partial class StatusPanel : Control
     private float _panelWidth;
     private float _shellOffset;   // x of the shell column relative to the speedometer centre
     private float _chipWidth;
+    private bool _spotted;
 
     public override void _Ready()
     {
@@ -81,6 +88,14 @@ public partial class StatusPanel : Control
         QueueRedraw();
     }
 
+    /// <summary>Lights the lamp while an enemy sees the player; redraws only when that changes.</summary>
+    public void SetSpotted(bool spotted)
+    {
+        if (spotted == _spotted) return;
+        _spotted = spotted;
+        QueueRedraw();
+    }
+
     /// <param name="speedKmh">Ground speed, km/h (sign ignored).</param>
     /// <param name="reloadSeconds">Seconds until the gun is ready; 0 = ready.</param>
     public void UpdateState(float speedKmh, float reloadSeconds, int health, int maxHealth, AmmoType ammo, int apLeft, int heLeft)
@@ -105,6 +120,7 @@ public partial class StatusPanel : Control
         var origin = new Vector2(Margin, Size.Y - Margin - PanelHeight);
         DrawStyleBox(_box, new Rect2(origin, new Vector2(_panelWidth, PanelHeight)));
         HudStyle.DrawOutlined(this, new Vector2(origin.X + Pad, origin.Y - NameGap), _vehicle, NameSize, HudStyle.Text, 4);
+        DrawLamp(new Vector2(origin.X + _panelWidth - Pad - LampRadius, origin.Y - LampRise));
         float y = origin.Y + Pad;
         DrawHealth(origin.X, y + HealthRow / 2);
         y += HealthRow + RowGap;
@@ -176,6 +192,29 @@ public partial class StatusPanel : Control
         return box;
     }
 
+    /// <summary>The "被点亮" lamp: a round bulb, dark; or lit with a soft glow, short rays and the words to its left.</summary>
+    private void DrawLamp(Vector2 c)
+    {
+        Color lit = HudStyle.Accent;
+        if (_spotted)
+        {
+            DrawCircle(c, LampRadius * 2.3f, new Color(lit, 0.16f), true, -1f, true);
+            DrawCircle(c, LampRadius * 1.65f, new Color(lit, 0.3f), true, -1f, true);
+            for (int i = 0; i < LampRays; i++)
+            {
+                float a = Mathf.Tau * i / LampRays;
+                var dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
+                DrawLine(c + dir * LampRadius * 1.45f, c + dir * LampRadius * 2.05f, HudStyle.OutlineColor, 6f, true);
+                DrawLine(c + dir * LampRadius * 1.45f, c + dir * LampRadius * 2.05f, lit, 3f, true);
+            }
+            float textX = c.X - LampRadius * 2.2f - HudStyle.TextWidth("被点亮", SpottedSize);
+            HudStyle.DrawOutlined(this, new Vector2(textX, HudStyle.Baseline(c.Y, SpottedSize)), "被点亮", SpottedSize, lit, 4);
+        }
+        DrawCircle(c, LampRadius + 2.5f, HudStyle.OutlineColor, true, -1f, true);
+        DrawCircle(c, LampRadius, _spotted ? lit : LampOff, true, -1f, true);
+        DrawCircle(c + new Vector2(-0.35f, -0.35f) * LampRadius, LampRadius * 0.3f, new Color(1f, 1f, 1f, _spotted ? 0.85f : 0.25f), true, -1f, true);
+    }
+
     /// <summary>A half-dial with a needle.</summary>
     private void DrawSpeedometer(Vector2 c)
     {
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxHud.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
index 7b76f63..de4cca3 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
@@ -7,13 +7,15 @@ using Tank.Sim;
 using Tank.Sim.Combat;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
+using Tank.Sim.Vision;
 using SN = System.Numerics;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
 /// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' and the damaged walls' health
-/// bars (over the popups, so a bar stays in sight while it drops), the reticle and the status panel.
+/// bars (over the popups, so a bar stays in sight while it drops), the spotting debug lines (V, or --spot-debug), the
+/// reticle and the status panel, whose lamp lights while an enemy sees the player.
 /// <see cref="OnEvent"/> spawns hit verdicts and damage numbers (over targets and walls); <see cref="Refresh"/> updates
 /// the rest once per frame. A target the player's team doesn't see gets no bar, verdict or damage number. A wall's bar, smaller than a tank's and drawn on its face, shows only while it is standing
 /// and short of full hit points.
@@ -31,13 +33,15 @@ public partial class SandboxHud : CanvasLayer
     private CombatPopups _popups = null!;
     private HealthBars _bars = null!;
     private HealthBars _wallBars = null!;
+    private SpotLabels _spotLabels = null!;
+    private VisionTracker _vision = null!;
     private Prop[] _walls = Array.Empty<Prop>();
     private Reticle _reticle = null!;
     private StatusPanel _status = null!;
 
-    public void Build(OrbitCamera camera, TargetRange targets, Battlefield field)
+    public void Build(OrbitCamera camera, TargetRange targets, Battlefield field, VisionTracker vision, bool spotDebug)
     {
-        (_camera, _targets, _field) = (camera, targets, field);
+        (_camera, _targets, _field, _vision) = (camera, targets, field, vision);
         _vignette = Add(new SniperVignette());
         _popups = Add(new CombatPopups());
         _popups.SetCamera(camera.Camera);
@@ -46,6 +50,9 @@ public partial class SandboxHud : CanvasLayer
         _walls = field.Props.Where(p => p.IsWall).ToArray();
         _wallBars = Add(new HealthBars());
         _wallBars.Setup(camera.Camera, _walls.Length, WallBarWidth, WallBarHeight);
+        _spotLabels = Add(new SpotLabels());
+        _spotLabels.Setup(camera.Camera, targets.Count);
+        _spotLabels.Visible = spotDebug;
         _reticle = Add(new Reticle());
         _status = Add(new StatusPanel());
     }
@@ -77,17 +84,22 @@ public partial class SandboxHud : CanvasLayer
     /// <summary>Where a wall's bar and damage numbers go: on the middle of its face, a little under the top.</summary>
     private static Vector3 WallHead(in Prop wall) => wall.Footprint.Center.Ground(wall.Top - WallBarBelowTop);
 
-    /// <summary>Once per frame, after the targets have been synced: reticle, status panel, vignette and health bars.</summary>
+    /// <summary>Shows or hides the spotting debug lines.</summary>
+    public void ToggleSpotDebug() => _spotLabels.Visible = !_spotLabels.Visible;
+
+    /// <summary>Once per frame, after the targets have been synced: reticle, status panel, lamp, vignette, health bars and debug lines.</summary>
     public void Refresh(in TankState player)
     {
         UpdateSight(player);
         _status.SetVehicle(player.Spec.Name);
         _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
+        _status.SetSpotted(!player.IsDestroyed && _vision.IsVisible(player.Id, TargetRange.TargetTeam));
         _vignette.Visible = _camera.Sniper;
         for (int i = 0; i < _targets.Count; i++)
         {
             var (head, health, alive, shown) = _targets.Status(i);
             _bars.Set(i, head, health, alive && shown);
+            if (_spotLabels.Visible) _spotLabels.Set(i, head, _vision.Inspect(player.Id, _targets.IdAt(i)), drawn: shown, hasLine: alive);
         }
         for (int i = 0; i < _walls.Length; i++)
         {
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
index 0d54316..5509678 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
@@ -6,7 +6,7 @@ namespace Tank.Client.Sandbox;
 /// <summary>
 /// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
 /// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
-/// AP or HE (kept until changed); R asks for a restock, T to restore the walls, fences and bushes, and F1–F4 for another
+/// AP or HE (kept until changed); V (read by <c>SandboxMain</c>) shows or hides the spotting debug lines; R asks for a restock, T to restore the walls, fences and bushes, and F1–F4 for another
 /// tank, each consumed once by the next tick.
 /// </summary>
 public sealed class SandboxInput
@@ -26,7 +26,7 @@ public sealed class SandboxInput
     };
 
     // Also read by SandboxMain's input handlers; UiCancel is Godot's built-in action, not registered here.
-    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", UiCancel = "ui_cancel";
+    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", SpotDebug = "spot_debug", UiCancel = "ui_cancel";
 
     private bool _fireLatched;
     private bool _fireHeld;
@@ -41,6 +41,7 @@ public sealed class SandboxInput
         AddKey(DriveLeft, Key.A);
         AddKey(DriveRight, Key.D);
         AddKey(SniperToggle, Key.Shift);
+        AddKey(SpotDebug, Key.V);
         AddMouse(FireGun, MouseButton.Left);
         AddMouse(FreeLookAction, MouseButton.Right);
         AddKey(AmmoAp, Key.Key1);
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 49eaa44..68ee8e3 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -17,11 +17,12 @@ namespace Tank.Client.Sandbox;
 /// <summary>
 /// Offline test drive: runs Tank.Sim locally at 30Hz and renders one tick behind, interpolated. The player's tank is slot 0
 /// (team 0); the practice targets (<see cref="TargetRange"/>, team 1, the patrolling one last) take the next slots. A
-/// <see cref="VisionTracker"/> works out after every step who sees whom: targets the player's team doesn't see aren't drawn. Keys: 1 AP, 2 HE, R restock, T restore the walls,
-/// fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in order). Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
+/// <see cref="VisionTracker"/> works out after every step who sees whom: targets the player's team doesn't see aren't drawn.
+/// Keys: 1 AP, 2 HE, R restock, T restore the walls, fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in
+/// order), V the spotting numbers over every target. Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
-/// --camera-preset front|side|hill|spin|orbit|bush|target|yard|gate|... --fps-log --sniper
+/// --camera-preset front|side|hill|spin|orbit|bush|target|yard|gate|... --fps-log --sniper --spot-debug
 /// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush
 /// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill|prop|down:S --quit-after S]
 /// </summary>
@@ -88,7 +89,7 @@ public partial class SandboxMain : Node3D
         _camera.SetAngles(_current.Yaw, 0.25f);
         if (_options.Sniper) _camera.ToggleSniper();
         _hud = Add(new SandboxHud());
-        _hud.Build(_camera, _targets, _sim.Field);
+        _hud.Build(_camera, _targets, _sim.Field, _vision, _options.SpotDebug);
 
         _automation.ReportBadOptions();
         if (!_options.AutoDrive && !IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
@@ -131,6 +132,7 @@ public partial class SandboxMain : Node3D
                 break;
         }
         if (e.IsActionPressed(SandboxInput.SniperToggle)) _camera.ToggleSniper();
+        if (e.IsActionPressed(SandboxInput.SpotDebug)) _hud.ToggleSpotDebug();
         if (e.IsActionPressed(SandboxInput.UiCancel)) Input.MouseMode = Input.MouseModeEnum.Visible;
     }
 
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/TargetRange.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/TargetRange.cs b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
index 8032789..1e14809 100644
--- a/src/Tank.Client/scripts/Sandbox/TargetRange.cs
+++ b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
@@ -108,6 +108,9 @@ public partial class TargetRange : Node3D
         return (target.Head, target.Health, target.Alive, target.Shown);
     }
 
+    /// <summary>Target <paramref name="index"/>'s tank id.</summary>
+    public int IdAt(int index) => _targets[index].Id;
+
     /// <summary>The point over target <paramref name="tankId"/>'s turret, for damage numbers; false for a tank that isn't a target.</summary>
     public bool TryGetHead(int tankId, out Vector3 head)
     {
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 573、Art 454；客户端 0 警告 0 错误。

- [ ] **Step 5：截图**

```bash
importall
dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
shot t6-yard yard --spot-debug
```

用 Read 打开 `build/shots/t6-yard.png`，核对：
- 院里的靶车和右边两辆靶车头上各有一行白字“看见  … m / … m  隐蔽 0.25”之类，在血条上方；
- 面板右上角的灯亮着（黄色、有光芒），左边写着“被点亮”（AI 靶车从起点看得见出生点的玩家）；
- 界面文字齐全，没有 `ERROR`。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(sandbox): the spotted lamp on the status panel, and spotting numbers over every target (V, --spot-debug)"
```

---

### Task 7：`patrol` 机位和 `lamp` 演示

设计 §4.4（站位与设计不同之处见“写计划时已核实”）。

- `--camera-preset patrol`：玩家在 `SandboxMap.PatrolWatch`；镜头在 AI 靶车起点和第一片草丛之间的一点往观察点方向 32 m、高 7 m 处，看着它从起点开进草丛。玩家的车不在画面里。
- `lamp` 演示：玩家停在 `RangeDemo.LampStand` (38, 306)，AI 照常跑；第 8 秒（AI 正在第一片草丛里等，离玩家 269 m，视线通）朝 0 号靶车开一发穿甲弹。中坦静止时 AI 的发现距离是 229 m，看不见；开炮后 3 秒内是 282 m，看得见。
- 为此 `RangeDemo` 的开炮时间改为每个演示自己的（`FireAfter`，默认 3 秒，`FireTick` 变成实例属性），新增站位 `Stand`（给了就不按靶车推算）和 `PatrolDrives`（AI 照常跑）。`SandboxMain` 只在 `PatrolDrives` 为假时让 AI 停在起点。
- 观察点 `SandboxMap.PatrolWatch` (224, 352)：从出生点看两片草丛都被岩石和小山挡住。测试检查从这里看，AI 在空地上看得见、进草丛后看不见、开炮后又看得见，并把它加进“打不中玩家”的位置。
- 原有的 `fence` 演示站位往西挪约 4°（方向 (−1, −0.35) → (−1, −0.27)）：原来两条视线都打在栅栏门旁的砖墙上，靶车被隐藏，命中大字和伤害数字也不显示。新测试：除 `wall`、`breach`、`ram` 外，每个演示开炮时四种车都看得见靶车；TD 的 `breach` 演示里，院里的靶车在石墙倒下之前看不见、之后看得见。

**Files:**
- Modify: `src/Tank.Client/scripts/Cameras/CameraPresets.cs`、`scripts/Sandbox/RangeDemo.cs`、`SandboxMain.cs`、`scripts/Bootstrap/LaunchOptions.cs`、`src/Tank.Sim/Maps/SandboxMap.cs`
- Test: 改 `tests/Tank.Art.Tests/RangeDemoTests.cs`、`tests/Tank.Sim.Tests/SandboxPatrolTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/RangeDemoTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/RangeDemoTests.cs b/tests/Tank.Art.Tests/RangeDemoTests.cs
index d1b3cf7..c12085b 100644
--- a/tests/Tank.Art.Tests/RangeDemoTests.cs
+++ b/tests/Tank.Art.Tests/RangeDemoTests.cs
@@ -1,8 +1,10 @@
 using Tank.Client.Sandbox;
 using Tank.Sim;
+using Tank.Sim.Ai;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
+using Tank.Sim.Vision;
 
 using SN = System.Numerics;
 
@@ -36,7 +38,7 @@ public class RangeDemoTests
     }
 
     /// <summary>The demos whose shots must reach their target with nothing solid in the way.</summary>
-    private static readonly string[] OpenDemos = { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "fence", "bush" };
+    private static readonly string[] OpenDemos = { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "fence", "bush", "lamp" };
 
     public static TheoryData<string, string> OpenDemosAndTanks()
     {
@@ -112,7 +114,7 @@ public class RangeDemoTests
         var sim = new Simulation(SandboxMap.Create());
         sim.AddTank(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw);
         var down = new List<int>();
-        for (int tick = 1; tick < RangeDemo.FireTick + d.DriveTicks + SimConstants.TickRate; tick++)
+        for (int tick = 1; tick < d.FireTick + d.DriveTicks + SimConstants.TickRate; tick++)
         {
             sim.Step(new[] { d.Command(new InputCmd(tick, 0, 0, d.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, hasFired: false) });   // as SandboxAutomation sends it
             foreach (GameEvent e in sim.Events)
@@ -192,16 +194,108 @@ public class RangeDemoTests
         Assert.True(RangeDemo.Find("bush")!.AimLocal.Z > 0);
     }
 
+    /// <summary>The demos that shoot at a target the player can see (the rest aim at, or drive into, the wall in front of it).</summary>
+    public static TheoryData<string, string> SightedDemosAndTanks()
+    {
+        var data = new TheoryData<string, string>();
+        foreach (string name in RangeDemo.Names.Except(new[] { "wall", "breach", "ram" }))
+        foreach (TankSpec spec in TankCatalog.All)
+            data.Add(name, spec.Id);
+        return data;
+    }
+
+    [Theory]
+    [MemberData(nameof(SightedDemosAndTanks))]
+    public void The_target_is_in_sight_when_the_demo_fires(string demo, string tank)
+    {
+        RangeDemo d = RangeDemo.Find(demo)!;
+        var sim = new Simulation(SandboxMap.Create());
+        sim.AddTank(TankCatalog.ById(tank), d.PlayerPosition, d.PlayerYaw, team: 0);
+        sim.AddTank(d.Spot.Spec, d.Spot.Position, d.Spot.Yaw, team: 1);
+        Sighting look = Spotting.Look(sim.Tanks[0], sim.Tanks[1], sim.Field, d.FireTick, targetMoving: false);
+        Assert.True(look.Seen, $"{look}");   // hidden, it would get no verdict or damage number on screen
+    }
+
+    [Fact]
+    public void The_breach_demo_in_a_tank_destroyer_shows_the_yard_target_only_once_the_wall_is_down()
+    {
+        RangeDemo demo = RangeDemo.Find("breach")!;
+        var sim = new Simulation(SandboxMap.Create());
+        sim.AddTank(TankCatalog.Td, demo.PlayerPosition, demo.PlayerYaw, team: 0);
+        int target = sim.AddTank(demo.Spot.Spec, demo.Spot.Position, demo.Spot.Yaw, team: 1);
+        var vision = new VisionTracker(sim);
+        bool fired = false;
+        int down = -1;
+        for (int tick = 1; tick <= 22 * SimConstants.TickRate; tick++)
+        {
+            var inputs = new[]
+            {
+                demo.Command(new InputCmd(tick, 0, 0, demo.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, fired),
+                new InputCmd(tick, 0, 0, demo.Spot.Yaw, 0f, false),
+            };
+            sim.Step(inputs);
+            vision.Update();
+            foreach (GameEvent e in sim.Events)
+            {
+                fired |= e.Type == GameEventType.ShellFired;
+                if (e.Type == GameEventType.PropDestroyed && sim.Field.PropById(e.PropId).Kind == PropKind.StoneWall && down < 0) down = tick;
+            }
+            if (down < 0) Assert.False(vision.IsVisible(target, 0), $"seen through the wall at tick {tick}");
+            else if (tick >= down + VisionTracker.UpdateInterval) Assert.True(vision.IsVisible(target, 0), $"unseen {tick - down} ticks after the breach");
+        }
+        Assert.True(down > 0, "the wall never came down");
+    }
+
+    [Fact]
+    public void Only_the_lamp_demo_lets_the_patrol_drive()
+    {
+        Assert.Equal(new[] { "lamp" }, RangeDemo.Names.Where(n => RangeDemo.Find(n)!.PatrolDrives));
+    }
+
+    /// <summary>
+    /// The lamp demo as the sandbox runs it: the player commanded by the demo, the still targets watching nobody, the
+    /// patrol driving its route. The patrol doesn't see the player until the shot, then does within a vision update
+    /// and for at least two seconds after.
+    /// </summary>
+    [Fact]
+    public void The_lamp_demo_is_unseen_until_its_shot_then_seen()
+    {
+        RangeDemo demo = RangeDemo.Find("lamp")!;
+        var sim = new Simulation(SandboxMap.Create());
+        int player = sim.AddTank(TankCatalog.Medium, demo.PlayerPosition, demo.PlayerYaw, team: 0);
+        var vision = new VisionTracker(sim);
+        foreach (TargetSpot t in SandboxMap.TargetSpots) vision.SetWatching(sim.AddTank(t.Spec, t.Position, t.Yaw, team: 1), false);
+        SN.Vector2 start = SandboxMap.PatrolStart;
+        sim.AddTank(SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start), team: 1);
+        var driver = new PatrolDriver(SandboxMap.PatrolRoute);
+        var inputs = new InputCmd[sim.TankCount];
+        int fired = -1;
+        for (int tick = 1; tick <= demo.FireTick + 3 * SimConstants.TickRate; tick++)
+        {
+            inputs[0] = demo.Command(new InputCmd(tick, 0, 0, demo.PlayerYaw, 0f, false), sim.Tanks[0], sim.Field, hasFired: fired >= 0);
+            for (int i = 1; i < inputs.Length - 1; i++) inputs[i] = new InputCmd(tick, 0, 0, SandboxMap.TargetSpots[i - 1].Yaw, 0f, false);
+            inputs[^1] = driver.Command(sim.Tanks[^1], sim.Field, tick, sim.Tanks[0].Position);
+            sim.Step(inputs);
+            vision.Update();
+            foreach (GameEvent e in sim.Events) if (e.Type == GameEventType.ShellFired && e.TankId == player && fired < 0) fired = tick;
+            bool seen = vision.IsVisible(player, 1);
+            if (fired < 0) Assert.False(seen, $"seen at {tick / (float)SimConstants.TickRate:F1} s, before the shot");
+            else if (tick >= fired + VisionTracker.UpdateInterval && tick <= fired + 2 * SimConstants.TickRate) Assert.True(seen, $"unseen {tick - fired} ticks after the shot");
+        }
+        Assert.Equal(demo.FireTick, fired);
+        Assert.True(driver.Waiting && driver.Next == 0, "the patrol is in its first bush patch when the player fires");
+    }
+
     [Fact]
     public void Only_a_driving_demo_opens_the_throttle_and_only_for_its_drive()
     {
         RangeDemo ram = RangeDemo.Find("ram")!;
         Assert.Equal(78, ram.DriveTicks);   // 2.6 s
-        Assert.Equal(0, ram.Throttle(RangeDemo.FireTick - 1));
-        Assert.Equal(1, ram.Throttle(RangeDemo.FireTick));
-        Assert.Equal(1, ram.Throttle(RangeDemo.FireTick + ram.DriveTicks - 1));
-        Assert.Equal(0, ram.Throttle(RangeDemo.FireTick + ram.DriveTicks));
-        Assert.All(RangeDemo.Names.Where(n => n != "ram"), n => Assert.Equal(0, RangeDemo.Find(n)!.Throttle(RangeDemo.FireTick)));
+        Assert.Equal(0, ram.Throttle(ram.FireTick - 1));
+        Assert.Equal(1, ram.Throttle(ram.FireTick));
+        Assert.Equal(1, ram.Throttle(ram.FireTick + ram.DriveTicks - 1));
+        Assert.Equal(0, ram.Throttle(ram.FireTick + ram.DriveTicks));
+        Assert.All(RangeDemo.Names.Where(n => n != "ram"), n => Assert.Equal(0, RangeDemo.Find(n)!.Throttle(RangeDemo.Find(n)!.FireTick)));
     }
 
     [Fact]
@@ -226,6 +320,7 @@ public class RangeDemoTests
     [InlineData("fence", "medium")]
     [InlineData("ram", "medium")]
     [InlineData("bush", "medium")]
+    [InlineData("lamp", "medium")]
     public void Shoots_the_kind_of_tank_it_was_written_for(string demo, string kind) =>
         Assert.Equal(kind, RangeDemo.Find(demo)!.Spot.Spec.Id);
 
PATCH
```

修改 `tests/Tank.Sim.Tests/SandboxPatrolTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Sim.Tests/SandboxPatrolTests.cs b/tests/Tank.Sim.Tests/SandboxPatrolTests.cs
index b0cfe4b..0b5bf67 100644
--- a/tests/Tank.Sim.Tests/SandboxPatrolTests.cs
+++ b/tests/Tank.Sim.Tests/SandboxPatrolTests.cs
@@ -3,6 +3,7 @@ using Tank.Sim.Ai;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
+using Tank.Sim.Vision;
 
 namespace Tank.Sim.Tests;
 
@@ -88,17 +89,16 @@ public sealed class SandboxPatrolTests
         Assert.All(sim.Field.Props.Where(p => p.Kind != PropKind.Bush), p => Assert.True(sim.Field.IsStanding(p.Id), $"{p.Kind} {p.Id} was knocked down"));
     }
 
-    /// <summary>The spawn, and eight points round each bush patch it stops in, 60, 100 and 150 m out.</summary>
+    /// <summary>The spawn, the watch point, and eight points round each bush patch it stops in, 60, 100 and 150 m out (kept on the map).</summary>
     public static TheoryData<float, float> Bystanders()
     {
-        var data = new TheoryData<float, float> { { SandboxMap.Spawn.X, SandboxMap.Spawn.Y } };
+        var places = new HashSet<Vector2> { SandboxMap.Spawn, SandboxMap.PatrolWatch };
         foreach (PatrolPoint stop in Route.Where(p => p.WaitSeconds > 0))
         foreach (float distance in new[] { 60f, 100f, 150f })
             for (int k = 0; k < 8; k++)
-            {
-                Vector2 at = Field.ClampToPlayable(stop.Position + Angles.Forward(k * MathF.PI / 4) * distance);
-                data.Add(at.X, at.Y);
-            }
+                places.Add(Field.ClampToPlayable(stop.Position + Angles.Forward(k * MathF.PI / 4) * distance));
+        var data = new TheoryData<float, float>();
+        foreach (Vector2 at in places) data.Add(at.X, at.Y);
         return data;
     }
 
@@ -125,4 +125,34 @@ public sealed class SandboxPatrolTests
         }
         if (Vector2.Distance(new Vector2(x, z), Route[0].Position) > PatrolDriver.HoldFireWithin) Assert.True(shots >= 2, $"only {shots} shots");
     }
+
+    [Fact]
+    public void From_the_watch_point_it_is_seen_in_the_open_lost_in_the_bushes_and_seen_again_when_it_fires()
+    {
+        var sim = new Simulation(SandboxMap.Create());
+        int player = sim.AddTank(TankCatalog.Medium, SandboxMap.PatrolWatch, SandboxMap.SpawnYaw, team: 0);
+        int patrol = sim.AddTank(SandboxMap.PatrolSpec, SandboxMap.PatrolStart, Angles.YawOf(Route[0].Position - SandboxMap.PatrolStart), team: 1);
+        var vision = new VisionTracker(sim);
+        var driver = new PatrolDriver(Route);
+        var inputs = new InputCmd[2];
+        bool seenDriving = false, lostWaiting = false, seenAfterShot = false;
+        int fired = -1;
+        for (int tick = 1; tick <= 20 * Tps; tick++)
+        {
+            inputs[0] = new InputCmd(tick, 0, 0, SandboxMap.SpawnYaw, 0f, false);
+            inputs[1] = driver.Command(sim.Tanks[1], sim.Field, tick, sim.Tanks[0].Position);
+            sim.Step(inputs);
+            vision.Update();
+            foreach (GameEvent e in sim.Events) if (e.Type == GameEventType.ShellFired && fired < 0) fired = tick;
+            bool seen = vision.IsVisible(patrol, 0);
+            if (!driver.Waiting && driver.Next == 0 && seen) seenDriving = true;
+            if (driver.Waiting && fired < 0 && !seen) lostWaiting = true;
+            if (fired >= 0 && tick <= fired + Tps && seen) seenAfterShot = true;
+        }
+        Assert.True(seenDriving, "never seen driving to the patch");
+        Assert.True(lostWaiting, "never lost in the bushes");
+        Assert.True(fired > 0, "never fired");
+        Assert.True(seenAfterShot, "not seen after its shot");
+        Assert.True(vision.IsVisible(player, 0));
+    }
 }
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test Tank.sln 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`SandboxMap.PatrolWatch`、`RangeDemo.PatrolDrives` 找不到，`FireTick` 不能通过实例访问。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index 5585e78..e4262dd 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -10,9 +10,9 @@ namespace Tank.Client.Bootstrap;
 /// --name N  --host  --join IP[:PORT]  --mode coop|basebattle|deathmatch  --bots N  --autostart
 /// --camera chase  --netgraph  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
 /// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
-/// --camera-preset front|side|close|closerear|sprocket|hill|spin|orbit|bush|target|yard|gate（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
+/// --camera-preset front|side|close|closerear|sprocket|hill|spin|orbit|bush|target|yard|gate|patrol（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
-/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush（试驾场：自动对靶车开炮或开车撞墙，用于截图）
+/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush|lamp（试驾场：自动对靶车开炮或开车撞墙，用于截图）
 /// --shot-after fire|hit|kill|prop|down:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤或第一次有物件被毁后再过 SECONDS 秒截图）
 /// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）  --spot-debug（试驾场：一开始就显示点亮调试文字，同 V 键）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
PATCH
```

修改 `src/Tank.Client/scripts/Cameras/CameraPresets.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Cameras/CameraPresets.cs b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
index cca92e5..f096965 100644
--- a/src/Tank.Client/scripts/Cameras/CameraPresets.cs
+++ b/src/Tank.Client/scripts/Cameras/CameraPresets.cs
@@ -9,7 +9,9 @@ namespace Tank.Client.Cameras;
 /// inside the bush patch nearest the spawn and looks at it from outside the patch; target looks at a practice target (the
 /// range demo's, else the first) from its front-right, on the side where a thrown turret lands (pair it with --range-demo);
 /// yard looks down on the walled yard and the cover beside the spawn from the south-east; gate looks at the yard's fence
-/// gate from just outside it (pair it with --range-demo fence).
+/// gate from just outside it (pair it with --range-demo fence); patrol starts the tank at <c>SandboxMap.PatrolWatch</c> and
+/// looks at the patrolling target's first bush patch from beside it, to watch it drive in, vanish and give itself away
+/// when it fires.
 /// </summary>
 public static class CameraPresets
 {
@@ -30,18 +32,29 @@ public static class CameraPresets
     private const float GateEyeHeight = 3f;
     private static readonly Vector3 TargetEye = new(7f, 4.5f, -13f);    // front-right of the target (its forward is −Z)
     private static readonly Vector3 TargetFocus = new(1.5f, 1.6f, 0f);  // between the hull and where the turret lands
+    private const float PatrolEyeOut = 32f;     // the patrol eye stands this far from its focus, towards the watch point
+    private const float PatrolEyeHeight = 7f;
+    private const float PatrolFocusAlong = 0.8f;   // the focus lies this share of the way from the patrol's start to its first bush patch
 
     /// <summary>Presets whose eye is far from the tank: world detail (grass) should then stream around the eye, not the tank.</summary>
     public static bool StreamsAroundCamera(string? name) => name is "hill" or "spin";
 
     /// <summary>Every preset's name.</summary>
     public static IReadOnlyList<string> Names { get; } =
-        new[] { "front", "side", "close", "closerear", "sprocket", "hill", "spin", "orbit", "bush", "target", "yard", "gate" };
+        new[] { "front", "side", "close", "closerear", "sprocket", "hill", "spin", "orbit", "bush", "target", "yard", "gate", "patrol" };
 
     public static bool IsKnown(string? name) => name is not null && Names.Contains(name);
 
-    /// <summary>Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, the rest use the normal spawn.</summary>
-    public static System.Numerics.Vector2 Spawn(string? name) => name == "bush" ? Tank.Sim.Maps.SandboxMap.BushPatches[0] : Tank.Sim.Maps.SandboxMap.Spawn;
+    /// <summary>
+    /// Where the tank starts for this preset: "bush" parks it in the bush patch nearest the spawn, "patrol" at
+    /// <c>SandboxMap.PatrolWatch</c>, the rest use the normal spawn.
+    /// </summary>
+    public static System.Numerics.Vector2 Spawn(string? name) => name switch
+    {
+        "bush" => Tank.Sim.Maps.SandboxMap.BushPatches[0],
+        "patrol" => Tank.Sim.Maps.SandboxMap.PatrolWatch,
+        _ => Tank.Sim.Maps.SandboxMap.Spawn,
+    };
 
     /// <param name="groundAt">Terrain height at (x, z), used to keep fixed eyes above the ground.</param>
     /// <param name="targetIndex">The practice target the "target" preset looks at (index into <c>SandboxMap.TargetSpots</c>).</param>
@@ -72,6 +85,11 @@ public static class CameraPresets
                     YardTarget + Vector3.Up * groundAt(YardTarget.X, YardTarget.Z));
             case "gate":
                 return (new Vector3(GateEye.X, groundAt(GateEye.X, GateEye.Y) + GateEyeHeight, GateEye.Y), GateFocus + Vector3.Up * groundAt(GateFocus.X, GateFocus.Z));
+            case "patrol": // the patrol's drive from its start into its first bush patch, from the watch point's side
+                System.Numerics.Vector2 focus = System.Numerics.Vector2.Lerp(Tank.Sim.Maps.SandboxMap.PatrolStart, Tank.Sim.Maps.SandboxMap.PatrolRoute[0].Position, PatrolFocusAlong);
+                System.Numerics.Vector2 stand = focus + System.Numerics.Vector2.Normalize(Tank.Sim.Maps.SandboxMap.PatrolWatch - focus) * PatrolEyeOut;
+                return (new Vector3(stand.X, groundAt(stand.X, stand.Y) + PatrolEyeHeight, stand.Y),
+                    new Vector3(focus.X, groundAt(focus.X, focus.Y) + 1.5f, focus.Y));
             case "hill":
                 return (new Vector3(HillEye.X, groundAt(HillEye.X, HillEye.Y) + 28f, HillEye.Y), HillTarget);
             case "orbit": // circles the parked tank at 14 m, level with the turret: stresses anything keyed to the camera
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/RangeDemo.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/RangeDemo.cs b/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
index db1afea..967f878 100644
--- a/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
+++ b/src/Tank.Client/scripts/Sandbox/RangeDemo.cs
@@ -23,16 +23,18 @@ public enum DemoAim
 /// <summary>
 /// Scripted shooting at the practice targets, for screenshots (<c>--range-demo NAME</c>): the player's tank is parked
 /// <see cref="Distance"/> m from target <see cref="Target"/>, <see cref="AngleOffFront"/> degrees round from straight
-/// ahead of it, its hull <see cref="PlayerTurn"/> degrees to the left of facing the target, lays its gun on the
-/// <see cref="Aim"/> face of that target's own armour (<see cref="AimLocal"/>, worked out from <see cref="Spot"/>'s
-/// <see cref="TankSpec"/>, so it can't drift from the kind of tank standing there) and pulls the trigger from
-/// <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded. A demo with <see cref="DriveSeconds"/>
-/// fires nothing: from <see cref="FireTick"/> it drives at full throttle for that long, then stops.
+/// ahead of it (or at <see cref="Stand"/>), its hull <see cref="PlayerTurn"/> degrees to the left of facing the target,
+/// lays its gun on the <see cref="Aim"/> face of that target's own armour (<see cref="AimLocal"/>, worked out from
+/// <see cref="Spot"/>'s <see cref="TankSpec"/>, so it can't drift from the kind of tank standing there) and pulls the
+/// trigger from <see cref="FireTick"/> on, once or (<see cref="KeepFiring"/>) whenever reloaded. A demo with
+/// <see cref="DriveSeconds"/> fires nothing: from <see cref="FireTick"/> it drives at full throttle for that long, then
+/// stops. The patrolling target stays parked at its start unless <see cref="PatrolDrives"/>.
 /// </summary>
 public sealed record RangeDemo(int Target, float AngleOffFront, float Distance, DemoAim Aim, AmmoType Ammo, bool KeepFiring,
-    float PlayerTurn = 0f, float DriveSeconds = 0f)
+    float PlayerTurn = 0f, float DriveSeconds = 0f, double FireAfter = RangeDemo.DefaultFireAfter, SN.Vector2? Stand = null,
+    bool PatrolDrives = false)
 {
-    public const double FireAfter = 3.0;
+    public const double DefaultFireAfter = 3.0;
 
     /// <summary>
     /// The earliest sim tick the demo pulls the trigger: <see cref="FireAfter"/> s of simulation time, not wall-clock
@@ -41,11 +43,19 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
     /// (<c>he</c>) first waits out the full reload that switching starts (<c>TryFire</c> in the sim): it then fires
     /// <c>ReloadTime</c> after tick 1 rather than at <see cref="FireTick"/>.
     /// </summary>
-    public static readonly int FireTick = (int)Math.Ceiling(FireAfter * SimConstants.TickRate);
+    public int FireTick => (int)Math.Ceiling(FireAfter * SimConstants.TickRate);
+
+    /// <summary>
+    /// Where the <c>lamp</c> demo parks the player: open ground 269 m west of the patrolling target's first bush patch,
+    /// in sight of it, about 120 m from target 0. A medium tank standing still is out of the patrol's detection range
+    /// there (229 m) and inside it for 3 s after a shot (282 m). Worked out for the medium tank only: a heavy tank is seen
+    /// before its shot, a tank destroyer not even after it.
+    /// </summary>
+    public static readonly SN.Vector2 LampStand = new(38, 306);
 
     /// <summary>The demos' names, in the order <see cref="Find"/> documents them.</summary>
     public static IReadOnlyList<string> Names { get; } =
-        new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "wall", "breach", "fence", "ram", "bush" };
+        new[] { "pen", "ricochet", "nopen", "he", "kill", "nopen-heavy", "kill-td", "turn", "wall", "breach", "fence", "ram", "bush", "lamp" };
 
     /// <summary>
     /// pen, ricochet, nopen, he or kill (at target 0, a medium tank); nopen-heavy (the far heavy tank's 200 mm turret
@@ -54,9 +64,12 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
     /// destroyer's rear until it is knocked out); turn (target 0 again, the hull starting 60° off: a tank destroyer has to
     /// turn its hull to bring the gun round). At the yard target (stage 2C): wall (one AP shot from 30 m in front, into
     /// the stone wall between), breach (HE from there until the wall is down and the target behind it), fence (AP at its
-    /// rear from 30 m west by north-west, in through a panel of the fence gate), ram (from 17 m north of it, 2.6 s at full throttle into the
+    /// rear from 30 m west by north-west, in through a panel of the fence gate, in sight of the target through the gate), ram (from 17 m north of it, 2.6 s at full throttle into the
     /// north wall: a heavy tank breaks through, the others are stopped); bush (target 0's rear from 50 m, through a bush
-    /// patch); null for any other name. Each shoots, or drives, with whatever tank --tank gives the player.
+    /// patch). For the spotting lamp (stage 3): lamp (from <see cref="LampStand"/>, with the patrol driving, one AP shot at
+    /// target 0 after 8 s, while the patrol waits in its first bush patch: the player is unseen until the shot, then seen);
+    /// null for any other name. Each shoots, or drives, with whatever tank --tank gives the player (lamp is meant for the
+    /// medium tank, the default).
     /// </summary>
     public static RangeDemo? Find(string? name) => name switch
     {
@@ -73,16 +86,18 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
         "fence" => new RangeDemo(SandboxMap.YardTarget, YardGateOffFront, 30f, DemoAim.HullRear, AmmoType.AP, false),
         "ram" => new RangeDemo(SandboxMap.YardTarget, YardNorthOffFront, 17f, DemoAim.HullFront, AmmoType.AP, false, DriveSeconds: 2.6f),
         "bush" => new RangeDemo(0, 210f, 50f, DemoAim.HullRear, AmmoType.AP, false),
+        "lamp" => new RangeDemo(0, 0f, 0f, DemoAim.HullFront, AmmoType.AP, false, FireAfter: 8.0, Stand: LampStand, PatrolDrives: true),
         _ => null,
     };
 
     private const float DegreesPerRadian = 180f / MathF.PI;
 
     /// <summary>
-    /// Degrees round from the yard target's front to west by north-west: the line from its rear in through the middle of
-    /// the gate's north panel (straight west would park the player, and the camera behind it, against the rocks there).
+    /// Degrees round from the yard target's front to west by north-west: the line from its rear in through the gate's
+    /// north panel (straight west would park the player, and the camera behind it, against the rocks there), close enough
+    /// to west that a sight line to the target passes through the gate too, not into the brick wall north of it.
     /// </summary>
-    private static float YardGateOffFront => YardOffFront(new SN.Vector2(-1, -0.35f));
+    private static float YardGateOffFront => YardOffFront(new SN.Vector2(-1, -0.27f));
 
     /// <summary>Degrees round from the yard target's front to due north (−Z), square on to the yard's north wall.</summary>
     private static float YardNorthOffFront => YardOffFront(new SN.Vector2(0, -1));
@@ -120,8 +135,8 @@ public sealed record RangeDemo(int Target, float AngleOffFront, float Distance,
 
     public TargetSpot Spot => SandboxMap.TargetSpots[Target];
 
-    /// <summary>Where the player's tank is parked.</summary>
-    public SN.Vector2 PlayerPosition => Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;
+    /// <summary>Where the player's tank is parked: <see cref="Stand"/>, else round from the target.</summary>
+    public SN.Vector2 PlayerPosition => Stand ?? Spot.Position + Angles.Forward(Spot.Yaw + Angles.Deg(AngleOffFront)) * Distance;
 
     /// <summary>Heading from the player's tank to the target (where the demo camera looks).</summary>
     public float Bearing => Angles.YawOf(Spot.Position - PlayerPosition);
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 68ee8e3..6068215 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -22,8 +22,8 @@ namespace Tank.Client.Sandbox;
 /// order), V the spotting numbers over every target. Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
-/// --camera-preset front|side|hill|spin|orbit|bush|target|yard|gate|... --fps-log --sniper --spot-debug
-/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush
+/// --camera-preset front|side|hill|spin|orbit|bush|target|yard|gate|patrol|... --fps-log --sniper --spot-debug
+/// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush|lamp
 /// --screenshot FILE --screenshot-at S --fx-shot S --shot-after fire|hit|kill|prop|down:S --quit-after S]
 /// </summary>
 public partial class SandboxMain : Node3D
@@ -74,7 +74,7 @@ public partial class SandboxMain : Node3D
         _previous = _current = _sim.Tanks[0];
         _vision = new VisionTracker(_sim);
         _targets = Add(new TargetRange());
-        _targets.Build(_sim, _vision, parkPatrol: demo is not null);
+        _targets.Build(_sim, _vision, parkPatrol: demo is { PatrolDrives: false });
         _vision.Update();   // who is seen at the start, before the first frame is drawn
         _commands = new InputCmd[_sim.TankCount];
         _groundAt = _sim.Field.Terrain.HeightAt;
PATCH
```

修改 `src/Tank.Sim/Maps/SandboxMap.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/SandboxMap.cs b/src/Tank.Sim/Maps/SandboxMap.cs
index eea89a0..80a026d 100644
--- a/src/Tank.Sim/Maps/SandboxMap.cs
+++ b/src/Tank.Sim/Maps/SandboxMap.cs
@@ -103,6 +103,13 @@ public static class SandboxMap
         new(PatrolStart, 0f),
     };
 
+    /// <summary>
+    /// A place to watch the patrol from: open ground 109 m south-west of its first bush patch, in sight of it (from the
+    /// spawn, rocks and the hill hide both patches). From here a medium tank sees the patrol in the open, loses it once it
+    /// stands in the bushes (two of them hide it: 40.5 m) and sees it again for 3 s after each shot it fires (117 m).
+    /// </summary>
+    public static readonly Vector2 PatrolWatch = new(224, 352);
+
     /// <summary>
     /// Hand-placed walls and fences (stage 2C design §3.6). The yard, 18 × 12.8 m inside around (160, 340): a stone wall
     /// on the east side, facing the spawn; brick on the north and south; brick on the west with a gate of three fence
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 575、Art 510；客户端 0 警告 0 错误。

- [ ] **Step 5：截图**

```bash
for t in 2.5 6 8 21; do
  godot_run 70 --path src/Tank.Client res://scenes/Sandbox.tscn -- --camera-preset patrol --spot-debug \
    --screenshot "$PWD/build/shots/t7-patrol-$t.png" --screenshot-at $t --quit-after $(( ${t%.*} + 1 )) 2>&1 | grep -E "Screenshot|ERROR|Exception"
done
godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo lamp \
  --screenshot "$PWD/build/shots/t7-lamp-off.png" --screenshot-at 7 --quit-after 8 2>&1 | grep -E "Screenshot|ERROR|Exception"
demoshot t7-lamp-on lamp fire:1 12
godot_run 60 --path src/Tank.Client res://scenes/Sandbox.tscn -- --range-demo breach --tank td --spot-debug \
  --screenshot "$PWD/build/shots/t7-yard-before.png" --screenshot-at 2.5 --quit-after 3 2>&1 | grep -E "Screenshot|ERROR|Exception"
demoshot t7-yard-after breach down:1.5 30 --tank td --spot-debug
demoshot t7-fence fence down:0.6 8 --camera-preset gate
```

截图按墙上时间计，仿真可能慢几帧；对不上时把时间前后挪半秒再拍。用 Read 逐张打开，核对：
- `t7-patrol-2.5`：一辆蓝灰色轻坦在草丛左边的空地上，头上有血条，文字开头是“看见”或“保持中”（起点附近有岩石挡视线，刚被看见又挡住时是“保持中”）；
- `t7-patrol-6`：草丛里看不到车，也没有血条；草丛上方一行淡灰字“看不见  109 m / 41 m  隐蔽 0.90（草×2）”上下（66 m、0.80 也对：刚停下的那次计算还算行进）；
- `t7-patrol-8`：AI 开过炮，草丛被它自己的炮弹打掉一部分，草丛上方出现血条，白字“看见  109 m / 117 m  隐蔽 0.60（草×2 开火）”；
- `t7-patrol-21`：轻坦离开草丛开向左边，白字“看见  …  隐蔽 0.30”；
- 这几张里“被点亮”灯都亮着（玩家站在空地上，离 AI 109 m，在它的发现距离 229 m 以内）；
- `t7-lamp-off`：镜头从玩家车后看向东边，灯是暗的，没有“被点亮”字样；
- `t7-lamp-on`：0 号靶车上有命中大字，灯亮了，左边写着“被点亮”；
- `t7-yard-before`：TD 停在石墙前，墙后的靶车看不到，墙上方一行淡灰字“看不见  30 m / … m  隐蔽 0.25（挡住）”；
- `t7-yard-after`：石墙中间打穿了，缺口里看得到靶车和它的血条，白字“看见  30 m / … m  隐蔽 0.25”；
- `t7-fence`（下面单独拍）：栅栏门北边那块被打倒，靶车上有“击穿！”和伤害数字，灯是暗的。

不符合就先修再继续。

- [ ] **Step 6：提交**

```bash
git add src/Tank.Client src/Tank.Sim tests
git commit -m "feat(sandbox): a patrol camera preset from the watch point, and a lamp demo — unseen until the shot, then seen"
```

---

### Task 8：截图与帧率

- [ ] **Step 1：全部测试**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 575、Art 510，Core 62、Net 50、Update 42；客户端 0 警告 0 错误。

- [ ] **Step 2：验收截图**

按 Task 7 Step 5 再拍一遍（文件名前缀改成 `final-`），另加 2A～2C 的几张，确认原有的打靶不受影响：

```bash
demoshot final-pen pen hit:0.3 8
demoshot final-wall wall prop:0.6 8
shot final-yard yard
```

- `final-pen`：0 号靶车上有“击穿！”和伤害数字；
- `final-wall`：石墙上的伤害数字和小血条；
- `final-yard`：院子、掩体、三辆靶车都在，和 2C 的截图相比只多了“被点亮”灯。

- [ ] **Step 3：帧率**

```bash
fpslog final-spin spin 40
fpslog final-orbit orbit 40
```

窗口保持在最前面。去掉前 20 秒，记下两段的帧数范围。验收标准是 60 帧以上；草稿实测 orbit 145、spin 118～120（显示设置限在 145 帧上下）。

- [ ] **Step 4：记录**

在 `docs/PROGRESS.md` 的“3 节点记录”里记下测试数、截图结论和帧率，提交：

```bash
git add docs/PROGRESS.md
git commit -m "docs: progress — stage 3 screenshots and frame rate checked, playtest next"
```

---

### Task 9：用户试玩与收尾

- [ ] **Step 1：请用户试玩**

启动：`godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn`（用户自己开，不加参数）。请用户确认：
- AI 靶车在出生点东北边绕圈（从出生点看过去被岩石挡着，开到观察点 (224, 352) 一带看得最清楚）；它停进草丛后消失，开炮后露出来，离开草丛又看得见；
- 自己停在空地上，离它 230 m 以外时灯不亮，开一炮灯就亮；躲进草丛不动，它靠近也不容易亮；
- 按 V 看每辆车头上的点亮数字；
- 打靶演示和截图时机都和以前一样。

- [ ] **Step 2：按反馈调整**

每条反馈一个提交，改完截图确认，再请用户看。

- [ ] **Step 3：记录**

在 `docs/PROGRESS.md` 记下试玩结论，提交。合并由用户确认后再做（squash 合入 main，不推送远程）。
