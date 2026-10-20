# 阶段 4B：客户端对局 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把 4A 的联网核心接到画面上：新主菜单（开房间、局域网列表 / 手填地址加入、试驾场）、房间界面（模式、机器人数、选队、选车、准备、开局）、对局画面（自己预测、别人插值、炮弹、特效、被点亮灯）和 HUD（计时比分、击毁提示、复活倒计时、Tab 计分板、结算、阵亡观战、Esc 暂停层、F3 面板带对账误差），外加启动参数与截图自动化、10 车性能、断线重连。验收：两个实例局域网对战，F3 显示对账误差小；截图验收；用户试玩。

**Architecture:** 对局场景 `MatchMain` 是挂在 `Main` 下的 `Node3D`，`BattleSession`（`BattleClient` + 房主的进程内服务器）跨菜单、房间、对局、结算存活。房主服务器放 `ServerRunner` 线程（30 Hz），测试和截图用的回环主机 `--loopback` 在主线程按固定 1/60 步长推进，配 `--seed` 完全确定。沙盒与对局共享部件不共享基类：`BattleWorld`（原 `SandboxWorld`）、`TankActor`、`CombatHud`、`DriveInput`、`AimSolver`、`ShellViews`、`BattleFx`。纯逻辑一律放 `Logic/` 子目录（不许 `using Godot`），Art 测试工程用一条通配符链接进来测。`BattleClient.Update` 每帧只调一次，开火锁存 `Peek` 不清、仿真 tick 推进了才 `EndTick`；重同步换 `Field` 时 `SetField`；别人的残骸“事件为准、状态 0.3 秒兜底”。

**Tech Stack:** .NET 8、C# 12、Godot 4（`godot-mono`，C#）、xUnit 2.9、LiteNetLib。

**设计依据：** `docs/superpowers/specs/2026-10-19-stage4b-client-match-blueprint.md`（实现蓝图：架构、文件、接口、HUD 布局、11 个任务、风险 R1～R16）；`docs/superpowers/specs/2026-10-18-stage4-networking-design.md` §7（4B 范围，Task 11 把定下来的键位和 HUD 布局补进去）、§8（4C）。

---
## 执行前须知

- **工作目录**：直接在现有 worktree 里执行，不要新建：`/Users/murphy/Desktop/dev/BattleCity-stage4b`（分支 `feat/stage4b-client-match`）。下文所有路径都相对于它。
- 每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage4b
```

- **补丁的用法**：修改已有文件的步骤给的是 `git apply` 补丁（改名的文件也是补丁，带 `rename from/to`），在仓库根目录原样运行即可（heredoc 用了 `'PATCH'`，内容不会被 shell 展开）。打不上时先 `git status` 看是不是漏了前面的步骤，不要手工改。新建的文件给完整内容，用 Write 工具写入。推荐直接用 `python3 /Users/murphy/Desktop/dev/BattleCity-dev-scripts/plan_step.py <本计划> <Task> <Step> .`，它逐字写文件、逐个打补丁，先加 `--check` 预演。
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`：客户端、`Tank.Net`、`Tank.Server`、测试项目和 `Tank.Soak` 里任何警告都会让构建失败。
- **跑 Godot 一律套强制结束**：`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/godot_run.sh <worktree> <场景> <超时秒> -- <参数>`（场景为空字符串 = `Main.tscn`，沙盒是 `res://scenes/Sandbox.tscn`；只打印截图和报错行，要全量日志自己重定向）。`--screenshot` 要绝对路径；截图放 `build/shots/`（不提交）。每张截图都要打开看，对照步骤里写的“应看到”。收尾时 `pgrep -fil godot` 确认没有残留进程（`-i`：macOS 上进程名是 `Godot`）。
- **沙盒回归**：`/Users/murphy/Desktop/dev/BattleCity-dev-scripts/baseline4b.sh <worktree> <输出目录>` 拍六张（orbit、kill、lamp、wall、patrol、heavy），`pngdiffdirs.py <目录A> <目录B>` 逐张比像素（单张用 `pngdiff.py A.png B.png`）。开工前先在当前 HEAD（蓝图提交）上拍一份基线到 `build/shots/base/`。差异只应出现在云、地平线、粒子和尘土这些逐帧变化的地方。
- **纯逻辑放 `Logic/` 子目录**，里面不许 `using Godot`；Art 测试工程用 `**/Logic/**` 通配符把它们编进来（Task 1 加这条通配）。新建 `.cs` 不必手工生成 `.cs.uid`，Godot 编辑器首开时会补。
- **不要改**：`project.godot`、`Main.tscn`、`Sandbox.tscn`、`Main.Update.cs`、`View/*`、`UI/Hud.cs`、`UI/LobbyScreen.cs`、`Bootstrap/Session.cs`、`BotDriver.cs`、`Controls/*`（4C 删）。`Tank.Net` 只有三处改动（Task 1 的 `ShellView.Age`、Task 3 的 `LiteNetClientTransport` 连接失败时停掉 NetManager、Task 5 的 `ClockSync` 硬重同步），都在补丁里。
- **坐标约定**（同前几个阶段）：Y 向上，−Z 为前方（北）；偏航 0 朝 −Z，正值向左转；`InputCmd.Steer` 为 +1 时向右转；`TankState.Position` 是二维 `(x, z)`。
- **HUD 一律用 1600×900 基准单位**（`project.godot` 是 `canvas_items` + `expand`）；1920×1080 窗口只是整体 ×1.2。
- **测试项目与数量**：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。开始前是 Core 62、Sim 637、Net 208、Update 42、Art 512。每个任务末尾写明了做完之后 Art 和 Net 的测试数；对不上就说明漏了或多了测试。Sim、Core、Update 本阶段不变。
- **回环时钟**：`--loopback` 的虚拟时钟每帧固定走 1/60 秒，和墙钟的比例随帧率变（120 帧约 2 倍，窗口在后台被限到 60 帧约 1 倍）。对局的 `--screenshot-at` 是虚拟秒、从对局场景创建起算（Task 9 起：一旦进了对局截图就归对局，整次运行只拍一张）；`--autostart` 时 `--quit-after` 走进程墙钟（对局结束后 `AutoRoom` 会开下一局，场景时钟从头算）。
- 提交信息用约定式前缀（feat/fix/refactor/test/docs）。进度记录在主仓库 `main` 上的 `docs/PROGRESS.md` 里由主会话维护，**本分支不改 `docs/PROGRESS.md`**。
- 分支最后用 squash merge 并入 `main`，不推送远程，除非用户要求。
## 文件结构

客户端源码都在 `src/Tank.Client/scripts/` 下（下表省略这个前缀）。**粗体**是纯逻辑（`Logic/` 子目录，Art 测试直接测）。

**共享抽取（沙盒与对局共用，Task 1、2）**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Driving/DriveInput.cs`、`Driving/AimSolver.cs`、**`Driving/Logic/FireLatch.cs`**、**`Driving/Logic/AimMath.cs`** | 新建 | 驾驶键、开火锁存（`Peek` 不清、`EndTick` 才清）、弹种、自由视角；瞄准数学（近于 8 m 时按视线水平方向推出） |
| `World/BattleWorld.cs`、`World/MapStyle.cs` | 由 `Sandbox/SandboxWorld.cs` 改名、新建 | 天空、地形、岩石、草、墙、树；草地岛和种子经 `MapStyle` 取；`SetField` 换场地实例；没有车时不压草 |
| `Vehicles/TankActor.cs`、`Overlay/CombatHud.cs` | 新建 | 坦克模型 + 残骸烟 + 尘土（`Present/Wreck/Restore/Kick`）；准星、状态面板、被点亮灯、命中字、坦克和墙的血条、狙击暗角 |
| `Sandbox/SandboxInput.cs`、`SandboxMain.cs`、`SandboxHud.cs`、`TargetRange.cs`、`Combat/ShellViews.cs`、`Combat/WreckSmoke.cs`、`Overlay/HealthBars.cs`、`Vehicles/TankModel.cs`、`World/BushField.cs`、`World/GrassField.cs`、`Art/Palette.cs` | 修改 | 改用上面的共享件；`ShellViews` 加 `ShellView` 列表重载；血条可带名字；残骸可直接到位；调色板加 `AllyBody/EnemyBody/UiBackground/UiDim/UiGood/UiDanger` |
| `src/Tank.Net/Battle/BattleClientTypes.cs`、`BattleClient.Match.cs` | 修改 | `ShellView.Age`（尾迹从炮口逐渐拉长） |

**在线层、菜单与房间（Task 3、4）**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| **`Online/Logic/BattleSession.cs`**、**`ThreadedHost.cs`**、**`LoopbackHost.cs`**、**`IHostBackend.cs`**、**`HostSettings.cs`** | 新建 | 会话：线程主机 / 回环主机 / 加入；`Advance`；固定顺序 `Dispose`；Task 10 加 `Reconnect` |
| **`Online/Logic/LanBrowser.cs`**、**`LanRoomRow.cs`**、**`TextColumns.cs`**、**`AutoRoom.cs`** | 新建 | 局域网房间列表（版本不符 / 满员 / 对局进行中）；`--mode/--bots/--tank/--autostart/--players` 的自动房间 |
| `src/Tank.Net/Transport/LiteNetClientTransport.cs` | 修改 | 构造时连接失败先停掉 NetManager 再抛 |
| **`UI/Logic/RoomRules.cs`**、**`RoomModel.cs`**、**`ModeText.cs`**、**`NameLimit.cs`**、`UI/RoomScreen.cs`、`UI/MenuScreen.cs`、`UI/UiKit.cs` | 新建、重写、修改 | 开局检查（与真服务器交叉校验）、房间视图模型、文案、名字按 24 字节截断；房间界面；新主菜单 |
| `Bootstrap/Main.cs`、`Bootstrap/Main.Flow.cs`、`Bootstrap/LaunchOptions.cs` | 重写、新建、修改 | 菜单 → 连接 → 房间 → 对局 → 房间；启动参数 |

**对局（Task 5～8）**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Match/MatchMain.cs`（+ `.Events.cs`、`.Camera.cs`）、`Match/TankActors.cs`、`Match/MatchInput.cs` | 新建 | 每帧流程（蓝图 §3.2）、事件到特效、镜头与瞄准、观战接线；别人的坦克；对局按键 |
| **`Match/Logic/MatchColors.cs`**、**`RenderMath.cs`**、**`WreckTracker.cs`** | 新建 | 相对阵营色；本车显示插值、转向速率、开炮检测；残骸“事件为准、状态 0.3 秒兜底” |
| `Match/MatchHud.cs`、`HudPanel.cs`、`ScorePlate.cs`、`KillFeedView.cs`、`CenterBanner.cs`、`NetPanel.cs`、`ScoreboardView.cs`、`PauseOverlay.cs`、`HudGallery.cs` | 新建 | HUD 各块；`--hud-demo` 画廊 |
| **`Match/Logic/HudText.cs`**、**`KillFeed.cs`**、**`HudLayout.cs`**、**`NetPanelText.cs`**、**`MatchHudFrame.cs`**、**`HudDemoFrames.cs`**、**`ScoreboardModel.cs`**、**`ResultsClock.cs`**、**`SpectateState.cs`** | 新建 | HUD 文案、击毁提示、布局、F3 文字、一帧 HUD 数据、画廊预制帧、计分板排序、结算倒计时、观战状态 |
| `src/Tank.Net/Client/ClockSync.cs` | 修改 | 服务器 tick 追上客户端时硬重同步（场景构建卡一帧后预测时钟落后的修复） |

**自动化与性能（Task 9、10）**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| **`Bootstrap/Logic/ShotPlan.cs`**、`Bootstrap/CaptureRun.cs`、`Match/MatchAutomation.cs`、`Sandbox/SandboxAutomation.cs` | 新建、修改 | 截图时刻表（纯逻辑）、截图 / fps 行 / 退出（沙盒和对局共用） |
| **`Online/Logic/Reconnector.cs`**、**`TickProbe.cs`**、`Combat/BattleFx.cs`、`Sandbox/FrameTimeLog.cs` | 新建、修改 | 断线重连的决策（房主会话也重连）；F3 的服务器 tick（不含开局建图）；特效池加大；fps 行带分配速率 |

**测试**：全部在 `tests/Tank.Art.Tests`（每个纯逻辑类一个测试类，另有回环会话、真 UDP 主机、真 UDP 重连演练），外加 `tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs`（`ShellView.Age`）、`ClockSync30Tests.cs`（硬重同步）和 `ComponentTests.cs`（两条 4A 的 ClockSync 测试改回原来的路径）；`tests/Tank.Art.Tests/Tank.Art.Tests.csproj` 加 `**/Logic/**` 通配和 `Sandbox/FrameTimeLog.cs`。

**文档**：Task 11 改 `docs/superpowers/specs/2026-10-18-stage4-networking-design.md` §7。

---
## 任务总览

| 批 | 任务 | 完成后 Art / Net 测试数 |
|---|---|---|
| A 共享抽取 | 1 驾驶输入、开火锁存、瞄准数学、`BattleWorld`；2 `TankActor`、`CombatHud` | 524 / 209 → 524 / 209 |
| B 在线层、菜单、房间 | 3 会话与主机；4 菜单、房间、`Main` 重写 | 596 / 209 → 644 / 209 |
| C 对局与 HUD | 5 对局场景核心；6 HUD 基础件；7 计分板与结算；8 阵亡、观战、暂停层 | 674 / 210 → 758 / 210 → 811 / 210 → 834 / 210 |
| D 自动化、性能、验收 | 9 启动参数与截图自动化；10 性能与打磨；11 验收与文档 | 859 / 210 → 882 / 210 → 882 / 210 |
| E 合入 | 12 用户试玩、并入 main | — |

每个任务一个提交。任务 1～11 的代码由脚本从草稿分支导出（见文末“写计划时已核实”）。下文 `$S` 指 `/Users/murphy/Desktop/dev/BattleCity-dev-scripts`，`$W` 指 `/Users/murphy/Desktop/dev/BattleCity-stage4b`（截图要绝对路径）。

---

### Task 1：共享抽取 A——驾驶输入、开火锁存、瞄准数学、`BattleWorld`

蓝图 §1.2、§5 T1。不改外观，只把沙盒里对局也要用的东西抽出来：`Driving/DriveInput`（驾驶键、开火锁存、弹种、自由视角；`SandboxInput` 继承它，只留 R、T、F1-F4）；`Driving/Logic/FireLatch`（按下锁存，`Peek` 不清，`EndTick` 才清——对局每帧调一次 `BattleClient.Update`，大多数帧没有仿真 tick，先采样再清会把点击丢掉，风险 R1）；`Driving/AimSolver` + `Driving/Logic/AimMath`（瞄准数学纯化：射线打中地面取命中点，落空取射程末端，目标离炮耳不到 8 m 时按视线水平方向推出 8 m）；`Sandbox/SandboxWorld.cs` 改名搬到 `World/BattleWorld.cs`，`Build` 收 `MapStyle`（草地岛和种子的唯一来源），新增 `SetField`（重同步换了 `Field` 实例时用，R2），没有车时不压草；`ShellViews` 加 `IReadOnlyList<ShellView>` 重载；调色板加 `AllyBody/EnemyBody/UiBackground/UiDim/UiGood/UiDanger`；`Tank.Net` 的 `ShellView` 末尾加 `Age`（尾迹从炮口逐渐拉长）。Art 测试工程加 `**/Logic/**` 通配。

**Files:**
- Create: `src/Tank.Client/scripts/Driving/{DriveInput,AimSolver}.cs`、`Driving/Logic/{AimMath,FireLatch}.cs`、`World/MapStyle.cs`
- Rename: `Sandbox/SandboxWorld.cs`（及 `.uid`）→ `World/BattleWorld.cs`
- Modify: `Art/Palette.cs`、`Combat/ShellViews.cs`、`Sandbox/{SandboxInput,SandboxMain}.cs`、`World/{BushField,GrassField}.cs`、`src/Tank.Net/Battle/{BattleClient.Match,BattleClientTypes}.cs`
- Test: 新建 `tests/Tank.Art.Tests/{AimMathTests,FireLatchTests}.cs`；修改 `PaletteTests.cs`、`Tank.Art.Tests.csproj`、`tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs`

- [ ] **Step 1：拍沙盒基线**

在改任何文件之前（HEAD 是蓝图提交）：

Run: `$S/baseline4b.sh $W build/shots/base`
Expected: `build/shots/base/` 下六张图（orbit、kill、lamp、wall、patrol、heavy），逐张打开看过：orbit 是环绕机位的试驾场全景；kill 是靶车被击毁、炮塔弹飞、冒黑烟；lamp 是状态面板旁“被点亮”灯亮着；wall 是墙被打后的墙血条和伤害数字；patrol 有点亮调试线；heavy 是重坦外观。

- [ ] **Step 2：写失败的测试**

新建 `tests/Tank.Art.Tests/AimMathTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Driving;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Art.Tests;

public class AimMathTests
{
    private const int Samples = 101;
    private const float Cell = 2f;   // 200 m square, flat at y = 0

    private static Battlefield Flat() =>
        new(new HeightField(Samples, Samples, Cell, new float[Samples * Samples]), Array.Empty<Prop>(), 4f);

    private static Vector3 Trunnion(float x, float z) => new(x, 2f, z);

    [Fact]
    public void A_ray_that_hits_the_ground_aims_at_the_hit_point()
    {
        // Orbit focus 40 m east of the trunnion, 10 m up, looking 45 degrees down toward +X: lands 10 m further east.
        Vector3 look = Vector3.Normalize(new Vector3(1, -1, 0));
        Vector3 camera = new Vector3(50, 10, 100) - look * 14f;
        (float yaw, float pitch) = AimMath.Solve(Flat(), camera, look, 14f, sniper: false, Trunnion(10, 100), prevYaw: 0f);
        Vector3 expected = new Vector3(60, 0, 100) - Trunnion(10, 100);
        (float wantYaw, float wantPitch) = Angles.AimAngles(expected);
        Assert.Equal(wantYaw, yaw, 4);
        Assert.Equal(wantPitch, pitch, 4);
        Assert.True(pitch < 0, "aiming down at the ground");
    }

    [Fact]
    public void A_ray_that_misses_aims_at_the_end_of_the_range()
    {
        Vector3 look = Vector3.UnitX;   // level and over the map's edge: nothing to hit within the border
        Vector3 camera = new(20, 5, 100);
        var field = Flat();
        (float yaw, float pitch) = AimMath.Solve(field, camera, look, 14f, sniper: false, Trunnion(20, 100), prevYaw: 0f);
        Vector3 end = camera + look * (14f + AimMath.AimRange);
        (float wantYaw, float wantPitch) = Angles.AimAngles(end - Trunnion(20, 100));
        Assert.Equal(wantYaw, yaw, 4);
        Assert.Equal(wantPitch, pitch, 4);
    }

    [Fact]
    public void A_target_nearer_than_the_minimum_is_pushed_out_along_the_horizontal_look()
    {
        // Looking straight down 3 m east of the trunnion would give a yaw of a target almost underneath; it must go 8 m out toward +X.
        Vector3 look = Vector3.Normalize(new Vector3(0.05f, -1, 0));
        Vector3 trunnion = Trunnion(50, 100);
        Vector3 camera = new Vector3(53, 10, 100) - look * 14f;
        (float yaw, float pitch) = AimMath.Solve(Flat(), camera, look, 14f, sniper: false, trunnion, prevYaw: 1.2f);
        var target = new Vector3(trunnion.X + AimMath.MinAimDistance, 0f, trunnion.Z);
        (float wantYaw, float wantPitch) = Angles.AimAngles(target - trunnion);
        Assert.Equal(wantYaw, yaw, 3);
        Assert.Equal(wantPitch, pitch, 3);
    }

    [Fact]
    public void A_vertical_look_close_to_the_gun_keeps_the_previous_yaw()
    {
        Vector3 look = -Vector3.UnitY;
        Vector3 trunnion = Trunnion(50, 100);
        Vector3 camera = new Vector3(52, 10, 100) - look * 14f;
        (float yaw, _) = AimMath.Solve(Flat(), camera, look, 14f, sniper: false, trunnion, prevYaw: 1.2f);
        Assert.Equal(1.2f, yaw, 4);
    }

    [Fact]
    public void The_sniper_sight_casts_from_the_eye_not_the_orbit_focus()
    {
        Vector3 look = Vector3.Normalize(new Vector3(1, -1, 0));
        Vector3 camera = new(50, 10, 100);
        (float yaw, _) = AimMath.Solve(Flat(), camera, look, 14f, sniper: true, Trunnion(0, 100), prevYaw: 0f);
        // From the eye at x = 50 the ray lands at x = 60.
        Assert.Equal(Angles.AimAngles(new Vector3(60, 0, 100) - Trunnion(0, 100)).Yaw, yaw, 4);
    }
}
```

新建 `tests/Tank.Art.Tests/FireLatchTests.cs`：

```csharp
using Tank.Client.Driving;

namespace Tank.Art.Tests;

public class FireLatchTests
{
    [Fact]
    public void Nothing_pressed_means_no_fire() => Assert.False(new FireLatch().Peek());

    [Fact]
    public void A_press_latches_and_peeking_does_not_clear_it()
    {
        var latch = new FireLatch();
        latch.Press();
        Assert.True(latch.Peek());
        Assert.True(latch.Peek());   // a frame without a tick looked and must leave the click for the next frame
    }

    [Fact]
    public void A_tap_released_before_the_tick_still_fires_once_then_ends_with_the_tick()
    {
        var latch = new FireLatch();
        latch.Press();
        latch.Release();
        Assert.True(latch.Peek());
        latch.EndTick();
        Assert.False(latch.Peek());
    }

    [Fact]
    public void A_held_button_stays_true_across_ticks_until_released()
    {
        var latch = new FireLatch();
        latch.Press();
        latch.EndTick();
        Assert.True(latch.Peek());
        latch.EndTick();
        Assert.True(latch.Peek());
        latch.Release();
        Assert.False(latch.Peek());
    }

    [Fact]
    public void Clear_drops_a_latched_and_a_held_press()
    {
        var latch = new FireLatch();
        latch.Press();
        latch.Clear();
        Assert.False(latch.Peek());
    }
}
```

修改 `tests/Tank.Art.Tests/PaletteTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/PaletteTests.cs b/tests/Tank.Art.Tests/PaletteTests.cs
index 7c92ab0..20e1356 100644
--- a/tests/Tank.Art.Tests/PaletteTests.cs
+++ b/tests/Tank.Art.Tests/PaletteTests.cs
@@ -51,6 +51,26 @@ public class PaletteTests
         Assert.True(Luminance(Palette.Stone) - Luminance(Palette.StoneMortar) > 0.15f, "dark joints show up on the stones");
     }
 
+    [Fact]
+    public void Team_colours_keep_enemies_apart_from_everything_else_on_the_field()
+    {
+        Assert.Equal(Palette.TargetBody, Palette.AllyBody);
+        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.Brick) >= 0.15f, "an enemy is not mistaken for a brick wall");
+        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.TankBody) >= 0.3f, "an enemy is not mistaken for the player's orange tank");
+        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.AllyBody) >= 0.25f, "an enemy is not mistaken for a teammate");
+        Assert.True(Vector3.Distance(Palette.EnemyBody, Palette.HitPierced) >= 0.12f, "an enemy hull is not the red of a damage number");
+    }
+
+    [Fact]
+    public void Ui_colours_are_the_designed_ones()
+    {
+        Assert.Equal(Palette.Hex("0B0E16"), Palette.UiBackground);
+        Assert.Equal(Palette.Hex("8C94A3"), Palette.UiDim);
+        Assert.Equal(Palette.Hex("73E68C"), Palette.UiGood);
+        Assert.Equal(Palette.HitPierced, Palette.UiDanger);
+        Assert.Equal(Palette.Hex("D8344A"), Palette.EnemyBody);
+    }
+
     /// <summary>Relative luminance (Rec. 709 weights on the linear colour) of an sRGB palette entry.</summary>
     private static float Luminance(Vector3 srgb) => Vector3.Dot(Palette.Linear(srgb), new Vector3(0.2126f, 0.7152f, 0.0722f));
 
PATCH
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index 7c79701..bb128e7 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -12,6 +12,8 @@
   <ItemGroup>
     <!-- The mesh library lives in the Godot client but must not depend on Godot; compiling it here, without Godot, enforces that. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Mesh\**\*.cs" LinkBase="Mesh" />
+    <!-- Anything under a Logic folder is engine-neutral by rule (no `using Godot`), so it is compiled here too. -->
+    <Compile Include="..\..\src\Tank.Client\scripts\**\Logic\**\*.cs" LinkBase="Logic" />
     <!-- The palette is engine-neutral too, so look data and vertex colours can use it. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
     <!-- So are the launch options (they need Tank.Core and Tank.Net for the old game's switches). -->
PATCH
```

修改 `tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs b/tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs
index 7d2484e..c3547ab 100644
--- a/tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs
+++ b/tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs
@@ -321,4 +321,26 @@ public sealed class BattleIntegrationTests
         Assert.True(seenRemoteShell > 0, "B's shells flew on A's client");
         Assert.Contains(fxA, e => e.Type == GameEventType.ShellFired && e.TankId == tankB);
     }
+
+    [Fact]
+    public void A_shell_in_flight_carries_its_age_and_the_age_grows_between_updates()
+    {
+        var h = new BattleHarness();
+        BattleClient a = h.AddClient("A");
+        h.AddClient("B");
+        h.StartMatch(BattleMode.TeamBattle);
+        float aim = h.ServerTankOf(a).Yaw;
+        var ages = new List<float>();
+        LocalCommand FireOften(int c, double t)
+        {
+            foreach (ShellView s in a.Shells)
+                if (s.Own) ages.Add(s.Age);
+            // Fires in a short window every 2.5 s, aimed a little below level along the hull, as the other shell tests do.
+            return new LocalCommand(0, 0, c == 0 ? aim : aim + 3.1f, -0.02f, (int)(t * 10) % 25 == 0);
+        }
+        h.Run(12.0, FireOften);
+        Assert.NotEmpty(ages);
+        Assert.Contains(ages, age => age > 0f);
+        Assert.True(ages.Max() > ages.First(age => age > 0f), "the age grew while the shell flew");
+    }
 }
PATCH
```


- [ ] **Step 3：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Run: `dotnet test tests/Tank.Net.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: Art 编译失败，命名空间 `Tank.Client.Driving` 找不到（`FireLatch`、`AimMath`；`PaletteTests` 的新颜色在它后面）；Net 编译失败，`ShellView` 没有 `Age`。

- [ ] **Step 4：实现**

修改 `src/Tank.Client/scripts/Art/Palette.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Art/Palette.cs b/src/Tank.Client/scripts/Art/Palette.cs
index 59d155e..e6131b5 100644
--- a/src/Tank.Client/scripts/Art/Palette.cs
+++ b/src/Tank.Client/scripts/Art/Palette.cs
@@ -53,6 +53,17 @@ public static class Palette
     public static readonly Vector3 Wood = Hex("B9844F");         // fences, and the chips off them
     public static readonly Vector3 BrickDust = Hex("E0A88C");    // dust off a brick wall
 
+    // Team colours in a match, seen from the player's seat (the player's own tank is TankBody). Declared after the
+    // entries they alias: static initialisers run in textual order.
+    public static readonly Vector3 AllyBody = TargetBody;        // a teammate: the same blue-grey as the practice targets
+    public static readonly Vector3 EnemyBody = Hex("D8344A");    // an opponent: a red apart from the brick, the orange and the blue-grey
+
+    // Menus, room and scoreboard.
+    public static readonly Vector3 UiBackground = Hex("0B0E16"); // the dark backdrop behind menu and room screens
+    public static readonly Vector3 UiDim = Hex("8C94A3");        // secondary text, disabled controls
+    public static readonly Vector3 UiGood = Hex("73E68C");       // ready, joinable, a win
+    public static readonly Vector3 UiDanger = HitPierced;        // errors, a loss: the same red as damage numbers
+
     /// <summary>"RRGGBB" (an optional leading '#') to sRGB 0–1.</summary>
     public static Vector3 Hex(string hex)
     {
PATCH
```

修改 `src/Tank.Client/scripts/Combat/ShellViews.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Combat/ShellViews.cs b/src/Tank.Client/scripts/Combat/ShellViews.cs
index ca275c9..24143ec 100644
--- a/src/Tank.Client/scripts/Combat/ShellViews.cs
+++ b/src/Tank.Client/scripts/Combat/ShellViews.cs
@@ -2,7 +2,9 @@ using Godot;
 using Tank.Client.Art;
 using Tank.Client.World;
 using Tank.Sim;
+using Tank.Net.Battle;
 using Tank.Sim.Combat;
+using SN = System.Numerics;
 
 namespace Tank.Client.Combat;
 
@@ -52,31 +54,52 @@ public partial class ShellViews : Node3D
     /// <param name="frameDelta">Render frame time, seconds; the tracer covers at least the distance flown in one frame.</param>
     public void Sync(ReadOnlySpan<Shell> shells, float alpha, float frameDelta)
     {
-        while (_pool.Count < shells.Length) _pool.Add(NewTracer());
-        for (int i = 0; i < _pool.Count; i++)
+        int shown = PrepareTracers(shells.Length);
+        for (int i = 0; i < shown; i++)
         {
-            Tracer tracer = _pool[i];
-            Node3D root = tracer.Root;
-            MeshInstance3D trail = tracer.Trail;
-            root.Visible = i < shells.Length;
-            if (!root.Visible) continue;
             Shell shell = shells[i];
-            if (shell.Ammo != tracer.Ammo) Paint(tracer, shell.Ammo);
-            float age = MathF.Max(shell.Age - (1f - alpha) * SimConstants.Dt, 0f);
-            Vector3 position = (shell.Position - shell.Velocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
-            float speed = shell.Velocity.Length();
-            Vector3 heading = speed > 0.01f ? shell.Velocity.ToGodot() / speed : Vector3.Zero;
-            if (heading == Vector3.Zero) root.Position = position;
-            else if (Mathf.Abs(heading.Y) < 0.99f) root.LookAtFromPosition(position, position + heading, Vector3.Up);
-            else root.Position = position;
-            // At least TrailLength, and never shorter than the distance covered per render frame, so the tracer has no gaps.
-            float length = MathF.Min(MathF.Max(TrailLength, speed * Mathf.Clamp(frameDelta, 1f / 240f, 1f / 30f)), age * speed);
-            trail.Visible = length > 0.5f;
-            trail.Scale = new Vector3(1, MathF.Max(length, 0.01f), 1);
-            trail.Position = new Vector3(0, 0, length / 2);
+            Draw(_pool[i], shell.Ammo, shell.Position, shell.Velocity, shell.Age, alpha, frameDelta);
         }
     }
 
+    /// <summary>The same tracers for a client's shells (own predicted and others'), as <c>BattleClient.Shells</c> lists them.</summary>
+    public void Sync(IReadOnlyList<ShellView> shells, float alpha, float frameDelta)
+    {
+        int shown = PrepareTracers(shells.Count);
+        for (int i = 0; i < shown; i++)
+        {
+            ShellView shell = shells[i];
+            Draw(_pool[i], shell.Ammo, shell.Position, shell.Velocity, shell.Age, alpha, frameDelta);
+        }
+    }
+
+    /// <summary>Grows the pool to <paramref name="count"/> tracers and hides the unused ones; returns how many are in use.</summary>
+    private int PrepareTracers(int count)
+    {
+        while (_pool.Count < count) _pool.Add(NewTracer());
+        for (int i = 0; i < _pool.Count; i++) _pool[i].Root.Visible = i < count;
+        return count;
+    }
+
+    private void Draw(Tracer tracer, AmmoType ammoType, SN.Vector3 shellPosition, SN.Vector3 shellVelocity, float shellAge, float alpha, float frameDelta)
+    {
+        Node3D root = tracer.Root;
+        MeshInstance3D trail = tracer.Trail;
+        if (ammoType != tracer.Ammo) Paint(tracer, ammoType);
+        float age = MathF.Max(shellAge - (1f - alpha) * SimConstants.Dt, 0f);
+        Vector3 position = (shellPosition - shellVelocity * ((1f - alpha) * SimConstants.Dt)).ToGodot();
+        float speed = shellVelocity.Length();
+        Vector3 heading = speed > 0.01f ? shellVelocity.ToGodot() / speed : Vector3.Zero;
+        if (heading == Vector3.Zero) root.Position = position;
+        else if (Mathf.Abs(heading.Y) < 0.99f) root.LookAtFromPosition(position, position + heading, Vector3.Up);
+        else root.Position = position;
+        // At least TrailLength, and never shorter than the distance covered per render frame, so the tracer has no gaps.
+        float length = MathF.Min(MathF.Max(TrailLength, speed * Mathf.Clamp(frameDelta, 1f / 240f, 1f / 30f)), age * speed);
+        trail.Visible = length > 0.5f;
+        trail.Scale = new Vector3(1, MathF.Max(length, 0.01f), 1);
+        trail.Position = new Vector3(0, 0, length / 2);
+    }
+
     private void Paint(Tracer tracer, AmmoType ammo)
     {
         var (head, trail) = ammo == AmmoType.HE ? _he : _ap;
PATCH
```

新建 `src/Tank.Client/scripts/Driving/AimSolver.cs`：

```csharp
using Godot;
using Tank.Client.Cameras;
using Tank.Sim.Maps;
using Tank.Client.World;
using SN = System.Numerics;

namespace Tank.Client.Driving;

/// <summary>Reads the orbit camera's view and hands it to <see cref="AimMath"/>.</summary>
public static class AimSolver
{
    /// <param name="trunnion">The gun's pivot, in sim coordinates.</param>
    /// <param name="prevYaw">Last aim yaw, kept when the view gives no direction.</param>
    public static (float Yaw, float Pitch) Solve(OrbitCamera camera, Battlefield field, SN.Vector3 trunnion, float prevYaw)
    {
        Camera3D cam = camera.Camera;
        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
        return AimMath.Solve(field, cam.GlobalPosition.ToSim(), look, camera.Distance, camera.Sniper, trunnion, prevYaw);
    }
}
```

新建 `src/Tank.Client/scripts/Driving/DriveInput.cs`：

```csharp
using Godot;
using Tank.Sim.Combat;

namespace Tank.Client.Driving;

/// <summary>
/// The keys and buttons for driving and shooting, shared by the sandbox and a match: W/A/S/D, the left button to fire,
/// the right button for free look, 1 and 2 to pick AP or HE (kept until changed) and Shift for the sniper sight. The
/// actions are registered at runtime so project.godot and the old game's bindings stay untouched. A fire click shorter
/// than a tick is latched (<see cref="FireLatch"/>); a caller that may see frames without a tick uses <see cref="Peek"/>
/// each frame and <see cref="EndTick"/> only after a tick has run, one that samples once per tick uses <see cref="Sample"/>.
/// </summary>
public class DriveInput
{
    // Action names are cached: a string argument becomes a new StringName on every call, and these are looked up each
    // tick or frame and in the input-event handlers (mouse motion alone arrives about once per frame while aiming).
    private static readonly StringName DriveForward = "drive_forward", DriveBack = "drive_back";
    private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
    private static readonly StringName FreeLookAction = "free_look";
    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he";

    // Also read by the scenes' input handlers; UiCancel is Godot's built-in action, not registered here.
    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", UiCancel = "ui_cancel";

    private readonly FireLatch _fire = new();

    public static void RegisterActions()
    {
        AddKey(DriveForward, Key.W);
        AddKey(DriveBack, Key.S);
        AddKey(DriveLeft, Key.A);
        AddKey(DriveRight, Key.D);
        AddKey(SniperToggle, Key.Shift);
        AddMouse(FireGun, MouseButton.Left);
        AddMouse(FreeLookAction, MouseButton.Right);
        AddKey(AmmoAp, Key.Key1);
        AddKey(AmmoHe, Key.Key2);
    }

    /// <summary>The round the player wants loaded.</summary>
    public AmmoType Ammo { get; private set; } = AmmoType.AP;

    public bool FreeLook => Input.IsActionPressed(FreeLookAction);

    /// <summary>Takes fire and the ammo keys; a subclass overrides this to add its own keys and calls the base first.</summary>
    public virtual void OnInputEvent(InputEvent e)
    {
        if (e.IsActionPressed(FireGun)) _fire.Press();
        else if (e.IsActionReleased(FireGun)) _fire.Release();
        else if (e is InputEventKey)
        {
            // Keyboard events only (both actions are bound to keys): mouse motion, about one per frame while aiming,
            // must not pay the lookups.
            if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
            else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
        }
    }

    /// <summary>Back to AP, the round a new tank starts with (after a tank change), until 1 or 2 is pressed again.</summary>
    public void ResetAmmo() => Ammo = AmmoType.AP;

    /// <summary>Drops any pending or held fire (focus lost, so a release may never arrive).</summary>
    public void ReleaseAll() => _fire.Clear();

    /// <summary>The sticks and trigger as they are now; consumes nothing, so it can be called every frame.</summary>
    public (sbyte Throttle, sbyte Steer, bool Fire) Peek()
    {
        sbyte throttle = (sbyte)(Held(DriveForward) - Held(DriveBack));
        sbyte steer = (sbyte)(Held(DriveRight) - Held(DriveLeft));
        return (throttle, steer, _fire.Peek());
    }

    /// <summary>A tick has run with what <see cref="Peek"/> returned: a latched click is spent (a held button stays down).</summary>
    public void EndTick() => _fire.EndTick();

    /// <summary>Peek and EndTick in one, for a caller that samples exactly once per tick.</summary>
    public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
    {
        var cmd = Peek();
        EndTick();
        return cmd;
    }

    private static int Held(StringName action) => Input.IsActionPressed(action) ? 1 : 0;

    protected static void AddKey(StringName action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });

    protected static void AddMouse(StringName action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });

    private static void Add(StringName action, InputEvent e)
    {
        if (InputMap.HasAction(action)) return;
        InputMap.AddAction(action);
        InputMap.ActionAddEvent(action, e);
    }
}
```

新建 `src/Tank.Client/scripts/Driving/Logic/AimMath.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Client.Driving;

/// <summary>Where the gun should point so its shell lands where the camera looks (engine-neutral, in sim coordinates).</summary>
public static class AimMath
{
    public const float AimRange = 900f;       // how far the view ray reaches when it hits nothing
    public const float MinAimDistance = 8f;   // nearer than this to the trunnion, the target is pushed out

    /// <summary>
    /// Casts from the orbit focus (the eye itself in the sniper sight) along the view; the gun then aims at the hit point
    /// or the end of the range. Looking steeply down puts the target almost under the turret, where yaw swings wildly, so
    /// a target closer than <see cref="MinAimDistance"/> (horizontally) is pushed out along the look direction.
    /// </summary>
    /// <param name="orbitDistance">Camera distance from the orbit focus.</param>
    /// <param name="prevYaw">Aim yaw to keep when the look is vertical and gives no direction.</param>
    public static (float Yaw, float Pitch) Solve(Battlefield f, Vector3 cameraPos, Vector3 look, float orbitDistance, bool sniper, Vector3 trunnion, float prevYaw)
    {
        Vector3 origin = cameraPos + look * (sniper ? 0f : orbitDistance);
        Vector3 end = origin + look * AimRange;
        Vector3 target = f.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
        Vector2 flat = new(target.X - trunnion.X, target.Z - trunnion.Z);
        if (flat.Length() < MinAimDistance)
        {
            Vector2 dir = new(look.X, look.Z);
            dir = dir.LengthSquared() > 1e-6f ? Vector2.Normalize(dir) : Angles.Forward(prevYaw);
            target = new Vector3(trunnion.X + dir.X * MinAimDistance, target.Y, trunnion.Z + dir.Y * MinAimDistance);
        }
        return Angles.AimAngles(target - trunnion, prevYaw);
    }
}
```

新建 `src/Tank.Client/scripts/Driving/Logic/FireLatch.cs`：

```csharp
namespace Tank.Client.Driving;

/// <summary>
/// The fire button as the sim wants it: a press shorter than a tick must still shoot once, and a held button must keep
/// shooting. <see cref="Peek"/> reads without consuming, so a frame that runs no tick can look at it and leave the click
/// for the frame that does; <see cref="EndTick"/> is called once a tick has taken it.
/// </summary>
public sealed class FireLatch
{
    private bool _latched;
    private bool _held;

    /// <summary>The button went down: latched until a tick has seen it, held until it comes up.</summary>
    public void Press() => _latched = _held = true;

    /// <summary>The button came up. A press not yet taken by a tick still counts.</summary>
    public void Release() => _held = false;

    /// <summary>True while held, or when a press has not been taken by a tick yet. Does not clear anything.</summary>
    public bool Peek() => _latched || _held;

    /// <summary>A tick has taken the fire state: drop the latched press (a held button stays down).</summary>
    public void EndTick() => _latched = false;

    /// <summary>Forgets everything, held or not (focus lost, so a release may never arrive).</summary>
    public void Clear() => _latched = _held = false;
}
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
index 9460187..1603ac1 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxInput.cs
@@ -1,22 +1,16 @@
 using Godot;
-using Tank.Sim.Combat;
+using Tank.Client.Driving;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// Registers the sandbox's own input actions at runtime (so project.godot and the old game's bindings stay untouched)
-/// and samples them once per sim tick. A fire click shorter than a tick is latched so it is never lost. 1 and 2 pick
-/// AP or HE (kept until changed); V (read by <c>SandboxMain</c>) shows or hides the spotting debug lines; R asks for a
-/// restock, T to restore the walls, fences and bushes, and F1–F4 for another tank, each consumed once by the next tick.
+/// The sandbox's input: everything in <see cref="DriveInput"/> plus V (read by <c>SandboxMain</c>) to show or hide the
+/// spotting debug lines, R to ask for a restock, T to restore the walls, fences and bushes, and F1–F4 for another tank,
+/// each consumed once by the next tick.
 /// </summary>
-public sealed class SandboxInput
+public sealed class SandboxInput : DriveInput
 {
-    // Action names are cached: a string argument becomes a new StringName on every call, and these are looked up each
-    // tick or frame and in the input-event handlers (mouse motion alone arrives about once per frame while aiming).
-    private static readonly StringName DriveForward = "drive_forward", DriveBack = "drive_back";
-    private static readonly StringName DriveLeft = "drive_left", DriveRight = "drive_right";
-    private static readonly StringName FreeLookAction = "free_look";
-    private static readonly StringName AmmoAp = "ammo_ap", AmmoHe = "ammo_he", Restock = "restock", RestoreProps = "restore_props";
+    private static readonly StringName Restock = "restock", RestoreProps = "restore_props";
 
     // The tank-pick keys, one row per action so the action and its key can't drift apart; the row index is the
     // TankCatalog.All index that TakeTankPick returns.
@@ -25,67 +19,35 @@ public sealed class SandboxInput
         ("pick_tank_1", Key.F1), ("pick_tank_2", Key.F2), ("pick_tank_3", Key.F3), ("pick_tank_4", Key.F4),
     };
 
-    // Also read by SandboxMain's input handlers; UiCancel is Godot's built-in action, not registered here.
-    internal static readonly StringName FireGun = "fire_gun", SniperToggle = "sniper_toggle", SpotDebug = "spot_debug", UiCancel = "ui_cancel";
+    // Also read by SandboxMain's input handler.
+    internal static readonly StringName SpotDebug = "spot_debug";
 
-    private bool _fireLatched;
-    private bool _fireHeld;
     private bool _restock;
     private bool _restoreProps;
     private int _pickedTank = -1;
 
-    public static void RegisterActions()
+    public static new void RegisterActions()
     {
-        AddKey(DriveForward, Key.W);
-        AddKey(DriveBack, Key.S);
-        AddKey(DriveLeft, Key.A);
-        AddKey(DriveRight, Key.D);
-        AddKey(SniperToggle, Key.Shift);
+        DriveInput.RegisterActions();
         AddKey(SpotDebug, Key.V);
-        AddMouse(FireGun, MouseButton.Left);
-        AddMouse(FreeLookAction, MouseButton.Right);
-        AddKey(AmmoAp, Key.Key1);
-        AddKey(AmmoHe, Key.Key2);
         AddKey(Restock, Key.R);
         AddKey(RestoreProps, Key.T);
         foreach ((StringName action, Key key) in PickTank) AddKey(action, key);
     }
 
-    /// <summary>The round the player wants loaded.</summary>
-    public AmmoType Ammo { get; private set; } = AmmoType.AP;
-
-    public bool FreeLook => Input.IsActionPressed(FreeLookAction);
-
-    public void OnInputEvent(InputEvent e)
+    public override void OnInputEvent(InputEvent e)
     {
-        if (e.IsActionPressed(FireGun)) _fireLatched = _fireHeld = true;
-        else if (e.IsActionReleased(FireGun)) _fireHeld = false;
-        else if (e is InputEventKey)
+        base.OnInputEvent(e);
+        if (e is not InputEventKey) return;   // every action below is bound to a key; mouse motion must not pay the lookups
+        if (e.IsActionPressed(Restock)) _restock = true;
+        else if (e.IsActionPressed(RestoreProps)) _restoreProps = true;
+        else
         {
-            // Keyboard events only (every action below is bound to a key): mouse motion, about one per frame while aiming,
-            // must not pay their eight lookups.
-            if (e.IsActionPressed(AmmoAp)) Ammo = AmmoType.AP;
-            else if (e.IsActionPressed(AmmoHe)) Ammo = AmmoType.HE;
-            else if (e.IsActionPressed(Restock)) _restock = true;
-            else if (e.IsActionPressed(RestoreProps)) _restoreProps = true;
-            else
-            {
-                for (int i = 0; i < PickTank.Length; i++)
-                    if (e.IsActionPressed(PickTank[i].Action)) _pickedTank = i;
-            }
+            for (int i = 0; i < PickTank.Length; i++)
+                if (e.IsActionPressed(PickTank[i].Action)) _pickedTank = i;
         }
     }
 
-    /// <summary>Back to AP, the round a new tank starts with (after a tank change), until 1 or 2 is pressed again.</summary>
-    public void ResetAmmo() => Ammo = AmmoType.AP;
-
-    /// <summary>Drops any pending or held fire (focus lost, so a release may never arrive).</summary>
-    public void ReleaseAll()
-    {
-        _fireLatched = false;
-        _fireHeld = false;
-    }
-
     /// <summary>True once after R was pressed.</summary>
     public bool TakeRestock()
     {
@@ -109,26 +71,4 @@ public sealed class SandboxInput
         _pickedTank = -1;
         return picked < 0 ? null : picked;
     }
-
-    public (sbyte Throttle, sbyte Steer, bool Fire) Sample()
-    {
-        sbyte throttle = (sbyte)(Held(DriveForward) - Held(DriveBack));
-        sbyte steer = (sbyte)(Held(DriveRight) - Held(DriveLeft));
-        bool fire = _fireLatched || _fireHeld;
-        _fireLatched = false;
-        return (throttle, steer, fire);
-    }
-
-    private static int Held(StringName action) => Input.IsActionPressed(action) ? 1 : 0;
-
-    private static void AddKey(StringName action, Key key) => Add(action, new InputEventKey { PhysicalKeycode = key });
-
-    private static void AddMouse(StringName action, MouseButton button) => Add(action, new InputEventMouseButton { ButtonIndex = button });
-
-    private static void Add(StringName action, InputEvent e)
-    {
-        if (InputMap.HasAction(action)) return;
-        InputMap.AddAction(action);
-        InputMap.ActionAddEvent(action, e);
-    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index 522e431..c5a29fb 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -2,6 +2,7 @@ using Godot;
 using Tank.Client.Bootstrap;
 using Tank.Client.Cameras;
 using Tank.Client.Combat;
+using Tank.Client.Driving;
 using Tank.Client.Vehicles;
 using Tank.Client.World;
 using Tank.Sim;
@@ -10,7 +11,6 @@ using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
 using Tank.Sim.Vision;
-using SN = System.Numerics;
 
 namespace Tank.Client.Sandbox;
 
@@ -19,7 +19,7 @@ namespace Tank.Client.Sandbox;
 /// (team 0); the practice targets (<see cref="TargetRange"/>, team 1, the patrolling one last) take the next slots. A
 /// <see cref="VisionTracker"/> works out after every step who sees whom: targets the player's team doesn't see aren't drawn.
 /// Keys: 1 AP, 2 HE, R restock, T restore the walls, fences and bushes, F1–F4 another tank (<c>TankCatalog.All</c> in
-/// order), V the spotting numbers over every target. Scenery, HUD and scripted runs live in <see cref="SandboxWorld"/>,
+/// order), V the spotting numbers over every target. Scenery, HUD and scripted runs live in <see cref="BattleWorld"/>,
 /// <see cref="SandboxHud"/> and <see cref="SandboxAutomation"/>.
 /// Launch: godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn [-- --tank light|medium|heavy|td --autodrive
 /// --camera-preset front|side|hill|spin|orbit|bush|target|yard|gate|patrol|... --fps-log --sniper --spot-debug
@@ -29,9 +29,7 @@ namespace Tank.Client.Sandbox;
 public partial class SandboxMain : Node3D
 {
     private const double MaxCatchUp = 0.25;
-    private const float AimRange = 900f;
     private const float SniperEyeAboveTrunnion = 0.45f;
-    private const float MinAimDistance = 8f;
     private const float FireShake = 0.08f;
     private const float FireShakeSeconds = 0.15f;
     private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
@@ -46,7 +44,7 @@ public partial class SandboxMain : Node3D
     private TankState _previous;
     private TankState _current;
     private OrbitCamera _camera = null!;
-    private SandboxWorld _world = null!;
+    private BattleWorld _world = null!;
     private TankModel _tank = null!;
     private DustTrail _dust = null!;
     private bool _playerFired;
@@ -80,8 +78,8 @@ public partial class SandboxMain : Node3D
         _groundAt = _sim.Field.Terrain.HeightAt;
         _aimYaw = _current.Yaw;
 
-        _world = Add(new SandboxWorld());
-        _world.Build(_sim.Field, _current, CameraPresets.StreamsAroundCamera(_options.CameraPreset));
+        _world = Add(new BattleWorld());
+        _world.Build(_sim.Field, _current, MapStyle.Sandbox, CameraPresets.StreamsAroundCamera(_options.CameraPreset));
         BuildTank(start);
         _shells = Add(new ShellViews());
         _fx = Add(new BattleFx());
@@ -104,7 +102,7 @@ public partial class SandboxMain : Node3D
     {
         if (IgnoresInput) return;
         // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
-        if (e.IsActionReleased(SandboxInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
+        if (e.IsActionReleased(DriveInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
     }
 
     public override void _Notification(int what)
@@ -131,9 +129,9 @@ public partial class SandboxMain : Node3D
                 GetViewport().SetInputAsHandled();
                 break;
         }
-        if (e.IsActionPressed(SandboxInput.SniperToggle)) _camera.ToggleSniper();
+        if (e.IsActionPressed(DriveInput.SniperToggle)) _camera.ToggleSniper();
         if (e.IsActionPressed(SandboxInput.SpotDebug)) _hud.ToggleSpotDebug();
-        if (e.IsActionPressed(SandboxInput.UiCancel)) Input.MouseMode = Input.MouseModeEnum.Visible;
+        if (e.IsActionPressed(DriveInput.UiCancel)) Input.MouseMode = Input.MouseModeEnum.Visible;
     }
 
     public override void _Process(double delta)
@@ -253,25 +251,9 @@ public partial class SandboxMain : Node3D
         else if (_automation.Demo is null && !_input.FreeLook) UpdateAim(pose, tank.Spec);
     }
 
-    /// <summary>Casts from the orbit focus along the camera's view; the turret then chases that point.</summary>
-    private void UpdateAim(TankPose pose, TankSpec spec)
-    {
-        Camera3D cam = _camera.Camera;
-        SN.Vector3 look = (-cam.GlobalBasis.Z).ToSim();
-        SN.Vector3 origin = cam.GlobalPosition.ToSim() + look * (_camera.Sniper ? 0f : _camera.Distance);
-        SN.Vector3 end = origin + look * AimRange;
-        SN.Vector3 target = _sim.Field.Raycast(origin, end, out RayHit hit) ? hit.Point : end;
-        SN.Vector3 trunnion = pose.Trunnion(spec);
-        // Looking steeply down puts the target almost under the turret, where yaw swings wildly; keep it at least MinAimDistance out.
-        SN.Vector2 flat = new(target.X - trunnion.X, target.Z - trunnion.Z);
-        if (flat.Length() < MinAimDistance)
-        {
-            SN.Vector2 dir = new(look.X, look.Z);
-            dir = dir.LengthSquared() > 1e-6f ? SN.Vector2.Normalize(dir) : Angles.Forward(_aimYaw);
-            target = new SN.Vector3(trunnion.X + dir.X * MinAimDistance, target.Y, trunnion.Z + dir.Y * MinAimDistance);
-        }
-        (_aimYaw, _aimPitch) = Angles.AimAngles(target - trunnion, _aimYaw);
-    }
+    /// <summary>Lays the gun where the camera looks (<see cref="AimSolver"/>); the turret then chases that point.</summary>
+    private void UpdateAim(TankPose pose, TankSpec spec) =>
+        (_aimYaw, _aimPitch) = AimSolver.Solve(_camera, _sim.Field, pose.Trunnion(spec), _aimYaw);
 
     private T Add<T>(T node) where T : Node
     {
PATCH
```

修改 `src/Tank.Client/scripts/World/BattleWorld.cs`（由 `src/Tank.Client/scripts/Sandbox/SandboxWorld.cs` 改名而来，在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs b/src/Tank.Client/scripts/World/BattleWorld.cs
similarity index 59%
rename from src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
rename to src/Tank.Client/scripts/World/BattleWorld.cs
index d2e268c..58cce72 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs
+++ b/src/Tank.Client/scripts/World/BattleWorld.cs
@@ -1,21 +1,21 @@
 using Godot;
 using Tank.Client.Vehicles;
-using Tank.Client.World;
 using Tank.Sim;
 using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
 using SN = System.Numerics;
 
-namespace Tank.Client.Sandbox;
+namespace Tank.Client.World;
 
 /// <summary>
-/// The sandbox's scenery, built once from the battlefield: sky and sun, terrain, rocks, bushes, walls and fences, grass
-/// and the tree line. Each frame <see cref="Update"/> lays the grass flat under the player's tank (sized for whichever
-/// tank it is now), streams grass detail around the view, lets the bushes part around the hull, and shows the props as
-/// they stand now (rubble, fallen fences, bushes shot away).
+/// The scenery of a battle (the sandbox's or a match's), built once from the battlefield: sky and sun, terrain, rocks,
+/// bushes, walls and fences, grass and the tree line. Each frame <see cref="Update"/> lays the grass flat under the
+/// followed tank (sized for whichever tank it is now), streams grass detail around the view, lets the bushes part
+/// around the hull, and shows the props as they stand now (rubble, fallen fences, bushes shot away). With no tank to
+/// follow (a free camera) nothing is flattened and the detail streams around the camera.
 /// </summary>
-public partial class SandboxWorld : Node3D
+public partial class BattleWorld : Node3D
 {
     private const float GrassCameraLean = 0.4f;    // grass focus sits this fraction of the way from tank to camera
     private const float FootprintMargin = 0.15f;   // grass lies flat this far beyond the hull and tracks
@@ -28,9 +28,10 @@ public partial class SandboxWorld : Node3D
     private TankSpec? _flatFor;
     private bool _streamsAroundCamera;
 
-    /// <param name="player">The player's tank, for the first patch of grass streamed in.</param>
+    /// <param name="focus">The tank in view, for the first patch of grass streamed in.</param>
+    /// <param name="style">The grass islands and dirt seed of this map.</param>
     /// <param name="streamsAroundCamera">The camera is a fixed far-away preset: stream detail around its eye, not the tank.</param>
-    public void Build(Battlefield field, in TankState player, bool streamsAroundCamera)
+    public void Build(Battlefield field, in TankState focus, MapStyle style, bool streamsAroundCamera)
     {
         _field = field;
         _streamsAroundCamera = streamsAroundCamera;
@@ -39,7 +40,7 @@ public partial class SandboxWorld : Node3D
         Scenery.ConfigureViewport(GetViewport());
         var terrain = new TerrainMesh();
         AddChild(terrain);
-        var dirt = new DirtMask(field.Terrain.SizeX, field.Terrain.SizeZ, SandboxMap.BushPatches, SandboxMap.Seed);
+        var dirt = new DirtMask(field.Terrain.SizeX, field.Terrain.SizeZ, style.GrassIslands, style.Seed);
         terrain.Build(field.Terrain, dirt);
         var rocks = new RockMeshes();
         AddChild(rocks);
@@ -54,20 +55,33 @@ public partial class SandboxWorld : Node3D
         AddChild(_grass);
         _grass.Build(field.Terrain, dirt, field.Props);
         if (!streamsAroundCamera)
-            _grass.Update(new Vector3(player.Position.X, 0, player.Position.Y), int.MaxValue);   // fill what is in range before the first frame
+            _grass.Update(new Vector3(focus.Position.X, 0, focus.Position.Y), int.MaxValue);   // fill what is in range before the first frame
         var trees = new TreeLine();
         AddChild(trees);
         trees.Build(field);
     }
 
-    /// <param name="pose">The player's tank on the ground this frame.</param>
-    public void Update(in TankState player, TankPose pose, Vector3 cameraPos, float delta)
+    /// <summary>A resync swapped the battlefield instance; the geometry is the same, only the props' current state is read from it.</summary>
+    public void SetField(Battlefield field) => _field = field;
+
+    /// <param name="tank">The tank the grass and bushes make room for, or null for a free camera.</param>
+    /// <param name="pose">That tank on the ground this frame (null exactly when <paramref name="tank"/> is).</param>
+    public void Update(TankState? tank, TankPose? pose, Vector3 cameraPos, float delta)
     {
+        if (tank is not { } player || pose is not { } ground)
+        {
+            _grass.ClearTankFootprint();
+            _grass.Update(cameraPos);
+            _bushes.SyncStanding(_field);
+            _bushes.Update(cameraPos, null, delta);
+            _walls.Sync(_field, delta);
+            return;
+        }
         if (player.Spec != _flatFor) (_flatFor, _flatHalfExtents) = (player.Spec, FlatHalfExtents(player.Spec));
-        _grass.SetTankFootprint(new Obb2(new SN.Vector2(pose.Position.X, pose.Position.Z), _flatHalfExtents, player.Yaw));
+        _grass.SetTankFootprint(new Obb2(new SN.Vector2(ground.Position.X, ground.Position.Z), _flatHalfExtents, player.Yaw));
         // Detail streams around a point leaning from the tank toward the camera (so the ground right in front of a zoomed-out
         // camera is in the dense band); fixed far-away presets stream around their eye.
-        Vector3 tankPos = pose.Position.ToGodot();
+        Vector3 tankPos = ground.Position.ToGodot();
         _grass.Update(_streamsAroundCamera ? cameraPos
             : new Vector3(Mathf.Lerp(tankPos.X, cameraPos.X, GrassCameraLean), 0f, Mathf.Lerp(tankPos.Z, cameraPos.Z, GrassCameraLean)));
         _bushes.SyncStanding(_field);
PATCH
```

修改 `src/Tank.Client/scripts/World/BattleWorld.cs.uid`（由 `src/Tank.Client/scripts/Sandbox/SandboxWorld.cs.uid` 改名而来，在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxWorld.cs.uid b/src/Tank.Client/scripts/World/BattleWorld.cs.uid
similarity index 100%
rename from src/Tank.Client/scripts/Sandbox/SandboxWorld.cs.uid
rename to src/Tank.Client/scripts/World/BattleWorld.cs.uid
PATCH
```

修改 `src/Tank.Client/scripts/World/BushField.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/World/BushField.cs b/src/Tank.Client/scripts/World/BushField.cs
index aa8ae1a..b470276 100644
--- a/src/Tank.Client/scripts/World/BushField.cs
+++ b/src/Tank.Client/scripts/World/BushField.cs
@@ -104,21 +104,23 @@ public partial class BushField : Node3D
     }
 
     /// <param name="camera">Camera position this frame.</param>
-    /// <param name="hull">The player's tank hull footprint.</param>
-    public void Update(Vector3 camera, Obb2 hull, float delta)
+    /// <param name="followed">The followed tank's hull footprint; null for a free camera, where only the camera clears bushes.</param>
+    public void Update(Vector3 camera, Obb2? followed, float delta)
     {
+        Obb2 hull = followed ?? default;
+        bool hasHull = followed is not null;
         Obb2 own = hull with { HalfExtents = hull.HalfExtents + new SN.Vector2(OwnTankMargin) };
         Obb2 pushing = hull with { HalfExtents = hull.HalfExtents + new SN.Vector2(PushMargin) };
         var eye = new SN.Vector2(camera.X, camera.Z);
         foreach (Bush bush in _bushes)
         {
             if (!bush.Shown) continue;   // shot away: nothing to fade or push, nothing to upload
-            bool clear = DistanceToBush(camera, bush) < NearCamera || own.IntersectsCircle(bush.Center, bush.Radius)
-                || SegmentDistance(bush.Center, eye, hull.Center) <= bush.Radius;
+            bool clear = DistanceToBush(camera, bush) < NearCamera
+                || (hasHull && (own.IntersectsCircle(bush.Center, bush.Radius) || SegmentDistance(bush.Center, eye, hull.Center) <= bush.Radius));
             float target = clear ? SeeThrough : 1f;
             bush.Opacity = Mathf.MoveToward(bush.Opacity, target, (1f - SeeThrough) * delta / FadeSeconds);
 
-            if (pushing.IntersectsCircle(bush.Center, bush.Radius))
+            if (hasHull && pushing.IntersectsCircle(bush.Center, bush.Radius))
             {
                 bush.PushFrom = hull.Center;
                 bush.Push = Mathf.MoveToward(bush.Push, 1f, delta / GrowSeconds);
PATCH
```

修改 `src/Tank.Client/scripts/World/GrassField.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/World/GrassField.cs b/src/Tank.Client/scripts/World/GrassField.cs
index 2897fe2..8216606 100644
--- a/src/Tank.Client/scripts/World/GrassField.cs
+++ b/src/Tank.Client/scripts/World/GrassField.cs
@@ -64,6 +64,7 @@ public partial class GrassField : Node3D
     public int ActiveCells => _cells.Count;
 
     // Global shader parameters read by grass.gdshader (declared under [shader_globals] in project.godot). One tank for now; making these arrays is the way to support several.
+    private const float OffMap = -100000f;                    // project.godot's default for tank_footprint
     private const string FootprintParam = "tank_footprint";   // vec4: centre x, centre z, half width, half length
     private const string AxesParam = "tank_axes";             // vec4: right x, right z, forward x, forward z
     private const string FocusParam = "grass_focus";          // vec2: where the far fade is centred
@@ -76,6 +77,9 @@ public partial class GrassField : Node3D
         RenderingServer.GlobalShaderParameterSet(AxesParam, new Vector4(right.X, right.Y, forward.X, forward.Y));
     }
 
+    /// <summary>Moves the flattened patch off the map (the shader's own default), for a camera that follows no tank.</summary>
+    public void ClearTankFootprint() => SetTankFootprint(new Obb2(new SN.Vector2(OffMap, OffMap), SN.Vector2.Zero, 0f));
+
     public void Build(HeightField terrain, DirtMask dirt, IReadOnlyList<Prop> props)
     {
         _terrain = terrain;
PATCH
```

新建 `src/Tank.Client/scripts/World/MapStyle.cs`：

```csharp
using Tank.Sim.Maps;

namespace Tank.Client.World;

/// <summary>
/// What the scenery needs beyond the battlefield itself: where the grass islands are and the seed of the ground's dirt
/// patches. Kept apart so a match can take them from its own map instead of the sandbox's.
/// </summary>
public readonly record struct MapStyle(IReadOnlyList<System.Numerics.Vector2> GrassIslands, uint Seed)
{
    public static MapStyle Sandbox => new(SandboxMap.BushPatches, (uint)SandboxMap.Seed);
}
```

修改 `src/Tank.Net/Battle/BattleClient.Match.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Battle/BattleClient.Match.cs b/src/Tank.Net/Battle/BattleClient.Match.cs
index 1c6fda9..52fbe39 100644
--- a/src/Tank.Net/Battle/BattleClient.Match.cs
+++ b/src/Tank.Net/Battle/BattleClient.Match.cs
@@ -68,10 +68,10 @@ public sealed partial class BattleClient
         for (int i = 0; i < own.Count; i++)
         {
             PredictedShell s = own[i];
-            if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, true));
+            if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, true, s.Shell.Age));
         }
         foreach (RemoteShell s in _remoteShells)
-            if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, false));
+            if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, false, s.Shell.Age));
     }
 
     private void HandleMatchStart(MatchStartMessage msg)
PATCH
```

修改 `src/Tank.Net/Battle/BattleClientTypes.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Battle/BattleClientTypes.cs b/src/Tank.Net/Battle/BattleClientTypes.cs
index 0077459..00f0abd 100644
--- a/src/Tank.Net/Battle/BattleClientTypes.cs
+++ b/src/Tank.Net/Battle/BattleClientTypes.cs
@@ -17,8 +17,8 @@ public readonly record struct LocalCommand(sbyte Throttle, sbyte Steer, float Ai
     public static readonly LocalCommand None = new(0, 0, 0f, 0f, false);
 }
 
-/// <summary>A shell to draw: the client's own (predicted) or another tank's (flown here from the server's shot event).</summary>
-public readonly record struct ShellView(int ShellId, int OwnerTankId, Vector3 Previous, Vector3 Position, Vector3 Velocity, AmmoType Ammo, bool Own);
+/// <summary>A shell to draw: the client's own (predicted) or another tank's (flown here from the server's shot event). <c>Age</c> is seconds since it left the muzzle.</summary>
+public readonly record struct ShellView(int ShellId, int OwnerTankId, Vector3 Previous, Vector3 Position, Vector3 Velocity, AmmoType Ammo, bool Own, float Age = 0f);
 
 /// <summary>Numbers for the net panel (design §5.9).</summary>
 public readonly record struct BattleStats(
PATCH
```


- [ ] **Step 5：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 客户端 0 警告 0 错误；全部通过，Art 524、Net 209。

- [ ] **Step 6：回归截图**

Run: `$S/baseline4b.sh $W build/shots/t1 && python3 $S/pngdiffdirs.py build/shots/base build/shots/t1`
Expected: 六张和基线内容一致；差异只在云、地平线、粒子、尘土这些逐帧变化的地方（准星、状态面板、血条区域不应有差异）。

- [ ] **Step 7：提交**

```bash
git add -A src/Tank.Client src/Tank.Net tests/Tank.Art.Tests tests/Tank.Net.Tests
git commit -m "refactor(client): shared driving input, fire latch and aim maths; SandboxWorld becomes World/BattleWorld with MapStyle and SetField; ShellViews draws ShellView lists; team and UI colours; ShellView carries the shell's age"
```

---

### Task 2：共享抽取 B——`TankActor`、`CombatHud`

蓝图 §1.2、§5 T2。回归风险最大的一步，放在对局之前单独做，只靠截图验证。`Vehicles/TankActor`：模型 + 残骸烟 + 尘土（`Present`、`Wreck(settled)`、`Restore`、`Kick`），玩家和靶车共用；`Overlay/CombatHud`：沙盒 HUD 的通用部分（准星、状态面板、被点亮灯、命中字、坦克和墙的血条、`HeadLookup` 委托、`ShowOwn`），`SandboxHud : CombatHud` 只留靶车血条和点亮调试线；`HealthBars.Set` 可带名字标签；`TankModel.Wreck(settled)` 和 `WreckSmoke.StartSettled` 给晚加入时直接到位的残骸。

**Files:**
- Create: `src/Tank.Client/scripts/Vehicles/TankActor.cs`、`Overlay/CombatHud.cs`
- Modify: `Combat/WreckSmoke.cs`、`Overlay/HealthBars.cs`、`Sandbox/{SandboxHud,SandboxMain,TargetRange}.cs`、`Vehicles/TankModel.cs`

- [ ] **Step 1：实现**

修改 `src/Tank.Client/scripts/Combat/WreckSmoke.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Combat/WreckSmoke.cs b/src/Tank.Client/scripts/Combat/WreckSmoke.cs
index bc59876..ce3fbe5 100644
--- a/src/Tank.Client/scripts/Combat/WreckSmoke.cs
+++ b/src/Tank.Client/scripts/Combat/WreckSmoke.cs
@@ -11,6 +11,7 @@ namespace Tank.Client.Combat;
 public partial class WreckSmoke : Node3D
 {
     private GpuParticles3D _emitter = null!;
+    private bool _settledStart;
 
     /// <param name="at">Hull-space spot the smoke pours from (the open turret ring).</param>
     public void Build(Vector3 at)
@@ -46,6 +47,17 @@ public partial class WreckSmoke : Node3D
         AddChild(_emitter);
     }
 
+    /// <summary>
+    /// Starts a column that is already full height, as if the wreck had smoked for a while (a wreck first seen dead, so
+    /// there is no build-up to watch). Does nothing while smoking already.
+    /// </summary>
+    public void StartSettled()
+    {
+        if (_emitter.Emitting) return;
+        _settledStart = true;
+        Emitting = true;
+    }
+
     /// <summary>On starts a fresh column; off stops it and clears the puffs already in the air. Setting the current state does nothing.</summary>
     public bool Emitting
     {
@@ -54,7 +66,13 @@ public partial class WreckSmoke : Node3D
         {
             if (value == _emitter.Emitting) return;   // a column already rising keeps rising
             _emitter.Visible = value;
-            if (value) _emitter.Restart();   // clears leftover puffs (a hidden emitter isn't simulated) and starts emitting
+            if (value)
+            {
+                // The engine reads the pre-process time when it handles the restart, so it stays set until the next one rewrites it.
+                _emitter.Preprocess = _settledStart ? _emitter.Lifetime : 0.0;
+                _settledStart = false;
+                _emitter.Restart();   // clears leftover puffs (a hidden emitter isn't simulated) and starts emitting
+            }
             else _emitter.Emitting = false;
         }
     }
PATCH
```

新建 `src/Tank.Client/scripts/Overlay/CombatHud.cs`：

```csharp
using Godot;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Overlay;

/// <summary>
/// Where tank <paramref name="tankId"/>'s head is (the point its bar and damage numbers go on); false for a tank that is
/// unknown or mustn't be drawn now (not in sight), which then gets no bar, verdict or damage number.
/// </summary>
public delegate bool HeadLookup(int tankId, out Vector3 head);

/// <summary>
/// The combat HUD both the sandbox and a match use, back to front: the sniper vignette, hit popups, tank health bars (the
/// caller feeds them through <see cref="TankBars"/>, with a name label if it likes), the damaged walls' bars (over the
/// popups, so a bar stays in sight while it drops), then whatever a subclass adds in <see cref="AddBehindSight"/>, the
/// reticle and the status panel, whose lamp lights while an enemy sees the player.
/// <see cref="OnShotEvent"/> spawns hit verdicts and damage numbers (over tanks and walls) for the player's own shots;
/// <see cref="Refresh"/> updates the rest once per frame. A wall's bar, smaller than a tank's and drawn on its face,
/// shows only while it is standing and short of full hit points.
/// </summary>
public partial class CombatHud : CanvasLayer
{
    private const float KmhPerMs = 3.6f;
    private const float WallBarBelowTop = 1f;    // a wall's bar sits on its face, this far under the top, clear of any tank's bar behind it
    private const float WallBarWidth = 44f, WallBarHeight = 8f;   // smaller than a tank's, so the two don't read alike

    private Battlefield _field = null!;
    private HeadLookup _headOf = null!;
    private SniperVignette _vignette = null!;
    private CombatPopups _popups = null!;
    private HealthBars _wallBars = null!;
    private Prop[] _walls = Array.Empty<Prop>();
    private Reticle _reticle = null!;
    private StatusPanel _status = null!;
    private int _ownTankId;
    private bool _aiming = true;

    protected OrbitCamera Cam { get; private set; } = null!;

    /// <summary>The tank health bars, <c>tankBarCount</c> of them: <see cref="HealthBars.Set"/> each every frame.</summary>
    public HealthBars TankBars { get; private set; } = null!;

    /// <param name="ownTankId">The player's tank: only its shots get verdicts and damage numbers.</param>
    /// <param name="tankBarCount">How many tank bars <see cref="TankBars"/> has.</param>
    /// <param name="headOf">Where a tank's head is, or false for one that gets nothing drawn.</param>
    public void Build(OrbitCamera cam, Battlefield field, int ownTankId, int tankBarCount, HeadLookup headOf)
    {
        (Cam, _ownTankId, _headOf) = (cam, ownTankId, headOf);
        _vignette = Add(new SniperVignette());
        _popups = Add(new CombatPopups());
        _popups.SetCamera(cam.Camera);
        TankBars = Add(new HealthBars());
        TankBars.Setup(cam.Camera, tankBarCount);
        _wallBars = Add(new HealthBars());
        SetField(field);
        AddBehindSight();
        _reticle = Add(new Reticle());
        _status = Add(new StatusPanel());
    }

    /// <summary>A subclass's own layers, between the bars and the reticle (the sandbox's spotting debug lines).</summary>
    protected virtual void AddBehindSight()
    {
    }

    /// <summary>Takes the field a resync swapped in; the wall bars are rebuilt from its walls.</summary>
    public void SetField(Battlefield field)
    {
        _field = field;
        _walls = field.Props.Where(p => p.IsWall).ToArray();
        _wallBars.Setup(Cam.Camera, _walls.Length, WallBarWidth, WallBarHeight);
    }

    /// <summary>Shows the reticle and status panel while the player is at the controls; <paramref name="aiming"/> false (spectating) hides them.</summary>
    public void ShowOwn(bool aiming)
    {
        _aiming = aiming;
        _reticle.Visible = _status.Visible = aiming;
    }

    /// <summary>
    /// For the player's own shots: a verdict where a shell strikes a tank, and damage numbers over a tank's head (other
    /// tanks' shots are skipped) or on the face of a wall that took a shell or a splash (<see cref="PropFx.IsDirectHit"/>
    /// tells them apart). Hits on a tank <see cref="HeadLookup"/> won't place get nothing.
    /// </summary>
    public void OnShotEvent(in GameEvent e)
    {
        if (e.TankId != _ownTankId) return;
        switch (e.Type)
        {
            case GameEventType.TankHit when _headOf(e.TargetTankId, out Vector3 head):
                _popups.Verdict(e.Position.ToGodot(), e.Result);
                if (e.Damage > 0) _popups.Damage(head, e.Damage, splash: false);
                break;
            case GameEventType.SplashDamage when _headOf(e.TargetTankId, out Vector3 splashed):
                _popups.Damage(splashed, e.Damage, splash: true);
                break;
            case GameEventType.PropDamaged:
                Prop wall = _field.PropById(e.PropId);
                _popups.Damage(WallHead(wall), e.Damage, splash: !e.IsDirectHit());
                break;
        }
    }

    /// <summary>Where a wall's bar and damage numbers go: on the middle of its face, a little under the top.</summary>
    private static Vector3 WallHead(in Prop wall) => wall.Footprint.Center.Ground(wall.Top - WallBarBelowTop);

    /// <summary>
    /// Once per frame: reticle, status panel, lamp (<paramref name="spotted"/>: an enemy sees the player), vignette and
    /// the walls' bars. The tank bars are the caller's.
    /// </summary>
    public void Refresh(in TankState player, bool spotted)
    {
        if (_aiming)
        {
            UpdateSight(player);
            _status.SetVehicle(player.Spec.Name);
            _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
            _status.SetSpotted(spotted);
        }
        _vignette.Visible = Cam.Sniper;
        for (int i = 0; i < _walls.Length; i++)
        {
            int left = _field.Durability(_walls[i].Id), full = _walls[i].MaxDurability;
            _wallBars.Set(i, WallHead(_walls[i]), left / (float)full, shown: left > 0 && left < full);
        }
    }

    protected T Add<T>(T control) where T : Node
    {
        AddChild(control);
        return control;
    }

    /// <summary>The reticle, from the same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).</summary>
    private void UpdateSight(in TankState player)
    {
        Ballistics.Launch launch = Ballistics.Aim(player, _field);
        SN.Vector3 impact = launch.BlockPoint;
        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _field, out impact);
        Camera3D cam = Cam.Camera;
        Vector3 point = impact.ToGodot();
        Vector2? screen = null;
        float circle = 0f;
        if (hasImpact && !cam.IsPositionBehind(point))
        {
            screen = cam.UnprojectPosition(point);
            circle = AimCirclePixels(player, launch.Origin, point, screen.Value);
        }
        _reticle.UpdateState(screen, 1f - player.Reload / player.Spec.ReloadTime, Cam.Sniper ? Cam.Zoom : 0f, circle);
    }

    /// <summary>
    /// The aim circle's radius around the predicted impact on screen: the circle is quoted at 100 m from the gun, so it
    /// measures <c>radius × gun distance / 100</c> there; a point that far to the camera's side of the impact is projected
    /// and its distance from <paramref name="screen"/> (the impact's own projection) measured.
    /// </summary>
    private float AimCirclePixels(in TankState player, SN.Vector3 muzzle, Vector3 impact, Vector2 screen)
    {
        Camera3D cam = Cam.Camera;
        float worldRadius = Dispersion.Radius(player.Spec, player.Bloom) * SN.Vector3.Distance(muzzle, impact.ToSim()) / Dispersion.ReferenceRange;
        Vector3 edge = impact + cam.GlobalBasis.X * worldRadius;
        return cam.IsPositionBehind(edge) ? 0f : cam.UnprojectPosition(edge).DistanceTo(screen);
    }
}
```

修改 `src/Tank.Client/scripts/Overlay/HealthBars.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Overlay/HealthBars.cs b/src/Tank.Client/scripts/Overlay/HealthBars.cs
index 042548d..ba2d2d4 100644
--- a/src/Tank.Client/scripts/Overlay/HealthBars.cs
+++ b/src/Tank.Client/scripts/Overlay/HealthBars.cs
@@ -6,13 +6,16 @@ namespace Tank.Client.Overlay;
 /// Cartoon capsule health bars floating over other tanks' heads (and over damaged walls), in screen space: green, red
 /// when low (<see cref="HudStyle.HealthFill"/>), with a dark outline. Shown only for bars marked shown within
 /// <see cref="MaxRange"/> m of the camera and in front of it. Feed the bars every frame with <see cref="Set"/>, then they
-/// draw themselves.
+/// draw themselves. A bar can carry a label (a player's name), drawn centred above it.
 /// </summary>
 public partial class HealthBars : Control
 {
     private const float MaxRange = 250f;
     private const float Outline = 2.5f;
     private const int CornerDetail = 6;
+    private const int LabelSize = 18;
+    private const int LabelOutline = 4;
+    private const float LabelGap = 5f;   // between the bar's outline and the label's baseline
 
     private float _width = 64f, _height = 11f;
     private StyleBoxFlat _back = null!;
@@ -25,6 +28,8 @@ public partial class HealthBars : Control
         public bool Shown;
         public Vector3 Head;
         public float Share;
+        public string? Label;   // the caller's own string: kept by reference, never rebuilt per frame
+        public Color LabelColor;
     }
 
     public override void _Ready()
@@ -48,10 +53,12 @@ public partial class HealthBars : Control
     /// <param name="head">World point the bar sits on: just above a tank, or on a wall's face.</param>
     /// <param name="share">Health left, 0 … 1.</param>
     /// <param name="shown">False for no bar (a wreck, a wall that is down or unhurt).</param>
-    public void Set(int index, Vector3 head, float share, bool shown)
+    /// <param name="label">Text above the bar (a name), or null for the bar alone.</param>
+    /// <param name="labelColor">The label's colour; <see cref="HudStyle.Text"/> when omitted.</param>
+    public void Set(int index, Vector3 head, float share, bool shown, string? label = null, Color? labelColor = null)
     {
         bool wasShown = _bars[index].Shown;
-        _bars[index] = new Bar { Shown = shown, Head = head, Share = Math.Clamp(share, 0f, 1f) };
+        _bars[index] = new Bar { Shown = shown, Head = head, Share = Math.Clamp(share, 0f, 1f), Label = label, LabelColor = labelColor ?? HudStyle.Text };
         if (shown || wasShown) QueueRedraw();   // a bar that stays hidden changes nothing on screen
     }
 
@@ -66,6 +73,7 @@ public partial class HealthBars : Control
             if (!GetViewportRect().HasPoint(at)) continue;
             var back = new Rect2(at.X - _width / 2 - Outline, at.Y - _height / 2 - Outline, _width + 2 * Outline, _height + 2 * Outline);
             DrawStyleBox(_back, back);
+            if (bar.Label is not null) HudStyle.DrawOutlinedCentered(this, at.X, back.Position.Y - LabelGap, bar.Label, LabelSize, bar.LabelColor, LabelOutline);
             if (bar.Share <= 0f) continue;
             DrawStyleBox(_fill.For(bar.Share), new Rect2(at.X - _width / 2, at.Y - _height / 2, MathF.Max(_height, _width * bar.Share), _height));
         }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxHud.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
index c33d816..d5c57ac 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxHud.cs
@@ -1,160 +1,66 @@
 using Godot;
 using Tank.Client.Cameras;
-using Tank.Client.Combat;
 using Tank.Client.Overlay;
-using Tank.Client.World;
 using Tank.Sim;
-using Tank.Sim.Combat;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
 using Tank.Sim.Vision;
-using SN = System.Numerics;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
-/// The sandbox's HUD layer, back to front: the sniper vignette, hit popups, the targets' and the damaged walls' health
-/// bars (over the popups, so a bar stays in sight while it drops), the spotting debug lines (V, or --spot-debug), the
-/// reticle and the status panel, whose lamp lights while an enemy sees the player.
-/// <see cref="OnEvent"/> spawns hit verdicts and damage numbers (over targets and walls) for the player's own shots;
-/// <see cref="Refresh"/> updates the rest once per frame. A target the player's team doesn't see gets no bar, verdict or
-/// damage number. A wall's bar, smaller than a tank's and drawn on its face, shows only while it is standing and short
-/// of full hit points.
+/// The sandbox's HUD: the <see cref="CombatHud"/> (vignette, popups, wall bars, reticle, status panel with the spotted
+/// lamp) plus the targets' health bars and the spotting debug lines (V, or --spot-debug), drawn between the bars and the
+/// reticle. A target the player's team doesn't see gets no bar, verdict or damage number.
 /// </summary>
-public partial class SandboxHud : CanvasLayer
+public partial class SandboxHud : CombatHud
 {
-    private const float KmhPerMs = 3.6f;
-    private const float WallBarBelowTop = 1f;    // a wall's bar sits on its face, this far under the top, clear of any tank's bar behind it
-    private const float WallBarWidth = 44f, WallBarHeight = 8f;   // smaller than a tank's, so the two don't read alike
-
-    private OrbitCamera _camera = null!;
     private TargetRange _targets = null!;
-    private Battlefield _field = null!;
-    private SniperVignette _vignette = null!;
-    private CombatPopups _popups = null!;
-    private HealthBars _bars = null!;
-    private HealthBars _wallBars = null!;
-    private SpotLabels _spotLabels = null!;
     private VisionTracker _vision = null!;
-    private int _playerId;
+    private SpotLabels _spotLabels = null!;
+    private bool _spotDebug;
     private int _labelsTick = -1;   // the simulation tick the debug lines were last worked out for
-    private Prop[] _walls = Array.Empty<Prop>();
-    private Reticle _reticle = null!;
-    private StatusPanel _status = null!;
 
     /// <param name="playerId">The player's tank: only its shots get verdicts and damage numbers.</param>
     public void Build(OrbitCamera camera, TargetRange targets, Battlefield field, VisionTracker vision, int playerId, bool spotDebug)
     {
-        (_camera, _targets, _field, _vision, _playerId) = (camera, targets, field, vision, playerId);
-        _vignette = Add(new SniperVignette());
-        _popups = Add(new CombatPopups());
-        _popups.SetCamera(camera.Camera);
-        _bars = Add(new HealthBars());
-        _bars.Setup(camera.Camera, targets.Count);
-        _walls = field.Props.Where(p => p.IsWall).ToArray();
-        _wallBars = Add(new HealthBars());
-        _wallBars.Setup(camera.Camera, _walls.Length, WallBarWidth, WallBarHeight);
-        _spotLabels = Add(new SpotLabels());
-        _spotLabels.Setup(camera.Camera, targets.Count);
-        _spotLabels.Visible = spotDebug;
-        _reticle = Add(new Reticle());
-        _status = Add(new StatusPanel());
+        (_targets, _vision, _spotDebug) = (targets, vision, spotDebug);
+        Build(camera, field, playerId, targets.Count, TryGetHead);   // the method group is converted once, here
     }
 
-    /// <summary>
-    /// For the player's own shots: a verdict where a shell strikes a tank, and damage numbers over a target's head (other
-    /// tanks are skipped) or on the face of a wall that took a shell or a splash (<see cref="PropFx.IsDirectHit"/> tells
-    /// them apart). Hits on a target the player's team doesn't see get nothing.
-    /// </summary>
-    public void OnEvent(in GameEvent e)
+    protected override void AddBehindSight()
     {
-        if (e.TankId != _playerId) return;   // the patrolling target's shots
-        switch (e.Type)
-        {
-            case GameEventType.TankHit or GameEventType.SplashDamage when _targets.IsHidden(e.TargetTankId):
-                break;
-            case GameEventType.TankHit:
-                _popups.Verdict(e.Position.ToGodot(), e.Result);
-                if (e.Damage > 0 && _targets.TryGetHead(e.TargetTankId, out Vector3 head)) _popups.Damage(head, e.Damage, splash: false);
-                break;
-            case GameEventType.SplashDamage when _targets.TryGetHead(e.TargetTankId, out Vector3 splashed):
-                _popups.Damage(splashed, e.Damage, splash: true);
-                break;
-            case GameEventType.PropDamaged:
-                Prop wall = _field.PropById(e.PropId);
-                _popups.Damage(WallHead(wall), e.Damage, splash: !e.IsDirectHit());
-                break;
-        }
+        _spotLabels = Add(new SpotLabels());
+        _spotLabels.Setup(Cam.Camera, _targets.Count);
+        _spotLabels.Visible = _spotDebug;
     }
 
-    /// <summary>Where a wall's bar and damage numbers go: on the middle of its face, a little under the top.</summary>
-    private static Vector3 WallHead(in Prop wall) => wall.Footprint.Center.Ground(wall.Top - WallBarBelowTop);
+    /// <summary>A target's head, unless the player's team doesn't see it now (or it isn't a target).</summary>
+    private bool TryGetHead(int tankId, out Vector3 head)
+    {
+        head = Vector3.Zero;
+        return !_targets.IsHidden(tankId) && _targets.TryGetHead(tankId, out head);
+    }
 
     /// <summary>Shows or hides the spotting debug lines.</summary>
     public void ToggleSpotDebug() => _spotLabels.Visible = !_spotLabels.Visible;
 
     /// <summary>
-    /// Once per frame, after the targets have been synced: reticle, status panel, lamp, vignette, health bars and debug
-    /// lines (their looks worked out once per simulation <paramref name="tick"/>).
+    /// Once per frame, after the targets have been synced: the combat HUD, the targets' bars and the debug lines (their
+    /// looks worked out once per simulation <paramref name="tick"/>).
     /// </summary>
     public void Refresh(in TankState player, int tick)
     {
-        UpdateSight(player);
-        _status.SetVehicle(player.Spec.Name);
-        _status.UpdateState(player.Speed * KmhPerMs, player.Reload, player.Health, player.Spec.MaxHealth, player.Ammo, player.ApLeft, player.HeLeft);
-        _status.SetSpotted(!player.IsDestroyed && _vision.IsVisible(player.Id, TargetRange.TargetTeam));
-        _vignette.Visible = _camera.Sniper;
+        Refresh(player, spotted: !player.IsDestroyed && _vision.IsVisible(player.Id, TargetRange.TargetTeam));
         bool look = _spotLabels.Visible && tick != _labelsTick;
         for (int i = 0; i < _targets.Count; i++)
         {
             var (head, health, alive, shown) = _targets.Status(i);
-            _bars.Set(i, head, health, alive && shown);
+            TankBars.Set(i, head, health, alive && shown);
             if (!_spotLabels.Visible) continue;
             _spotLabels.Place(i, head, hasLine: alive);
             if (look) _spotLabels.Set(i, _vision.Inspect(player.Id, _targets.IdAt(i)), drawn: shown);
         }
         if (look) _labelsTick = tick;
-        for (int i = 0; i < _walls.Length; i++)
-        {
-            int left = _field.Durability(_walls[i].Id), full = _walls[i].MaxDurability;
-            _wallBars.Set(i, WallHead(_walls[i]), left / (float)full, shown: left > 0 && left < full);
-        }
-    }
-
-    private T Add<T>(T control) where T : Node
-    {
-        AddChild(control);
-        return control;
-    }
-
-    /// <summary>The reticle, from the same launch maths the sim fires with, so the ring shows where the shot really goes (including a blocked barrel).</summary>
-    private void UpdateSight(in TankState player)
-    {
-        Ballistics.Launch launch = Ballistics.Aim(player, _field);
-        SN.Vector3 impact = launch.BlockPoint;
-        bool hasImpact = launch.Blocked || Ballistics.PredictImpact(launch.Origin, launch.Velocity, _field, out impact);
-        Camera3D cam = _camera.Camera;
-        Vector3 point = impact.ToGodot();
-        Vector2? screen = null;
-        float circle = 0f;
-        if (hasImpact && !cam.IsPositionBehind(point))
-        {
-            screen = cam.UnprojectPosition(point);
-            circle = AimCirclePixels(player, launch.Origin, point, screen.Value);
-        }
-        _reticle.UpdateState(screen, 1f - player.Reload / player.Spec.ReloadTime, _camera.Sniper ? _camera.Zoom : 0f, circle);
-    }
-
-    /// <summary>
-    /// The aim circle's radius around the predicted impact on screen: the circle is quoted at 100 m from the gun, so it
-    /// measures <c>radius × gun distance / 100</c> there; a point that far to the camera's side of the impact is projected
-    /// and its distance from <paramref name="screen"/> (the impact's own projection) measured.
-    /// </summary>
-    private float AimCirclePixels(in TankState player, SN.Vector3 muzzle, Vector3 impact, Vector2 screen)
-    {
-        Camera3D cam = _camera.Camera;
-        float worldRadius = Dispersion.Radius(player.Spec, player.Bloom) * SN.Vector3.Distance(muzzle, impact.ToSim()) / Dispersion.ReferenceRange;
-        Vector3 edge = impact + cam.GlobalBasis.X * worldRadius;
-        return cam.IsPositionBehind(edge) ? 0f : cam.UnprojectPosition(edge).DistanceTo(screen);
     }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index c5a29fb..da33e8c 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -45,8 +45,7 @@ public partial class SandboxMain : Node3D
     private TankState _current;
     private OrbitCamera _camera = null!;
     private BattleWorld _world = null!;
-    private TankModel _tank = null!;
-    private DustTrail _dust = null!;
+    private TankActor _tank = null!;
     private bool _playerFired;
     private ShellViews _shells = null!;
     private TargetRange _targets = null!;
@@ -182,17 +181,14 @@ public partial class SandboxMain : Node3D
         _sim.ChangeSpec(_current.Id, spec);
         _input.ResetAmmo();   // the new tank starts with AP loaded; still asking for HE would cost it a full reload
         _previous = _current = _sim.Tanks[0];
-        _tank.QueueFree();   // the dust trail goes with it
+        _tank.QueueFree();   // the smoke and the dust trail go with it
         BuildTank(spec);
     }
 
     private void BuildTank(TankSpec spec)
     {
-        _tank = Add(new TankModel());
+        _tank = Add(new TankActor());
         _tank.Build(spec);
-        _dust = new DustTrail();
-        _tank.AddChild(_dust);
-        _dust.Build(TankLooks.For(spec.Id).TrackX, spec.HullLength / 2f);
     }
 
     private void OnEvent(in GameEvent e)
@@ -203,7 +199,7 @@ public partial class SandboxMain : Node3D
             case GameEventType.ShellFired when e.TankId == _current.Id:
                 // Flash at the visible barrel tip (the sim event keeps its own muzzle position): spawned after this frame's pose update.
                 _playerFired = true;
-                _tank.Fire(_current.TurretYaw);
+                _tank.Kick(_current.TurretYaw);
                 break;
             case GameEventType.ShellFired:
                 _fx.Muzzle(new Transform3D(Basis.Identity, e.Position.ToGodot()));   // no barrel pose for other tanks yet
@@ -217,7 +213,7 @@ public partial class SandboxMain : Node3D
                 _fx.Explosion(e.Position.ToGodot(), PuffColor.Grey, ArmorHitSize);
                 break;
         }
-        _hud.OnEvent(e);
+        _hud.OnShotEvent(e);
         _targets.OnEvent(e, _sim, _fx);
         _world.OnEvent(e);
         PropFx.OnEvent(e, _sim.Field, _fx);
@@ -226,16 +222,13 @@ public partial class SandboxMain : Node3D
     /// <summary>Poses and animates the player's tank, with the muzzle blast of a shot fired this frame; hidden in the sniper sight.</summary>
     private void ShowPlayer(in TankState tank, TankPose pose, float delta)
     {
-        _tank.Apply(pose, tank.TurretYaw, tank.GunPitch);
-        _tank.Animate(tank.Speed, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, delta);
-        _dust.Update(tank.Speed);
+        _tank.Present(tank, pose, Angles.Wrap(_current.Yaw - _previous.Yaw) / SimConstants.Dt, delta, shown: !_camera.Sniper);
         if (_playerFired)
         {
             _fx.Muzzle(_tank.MuzzleTransform, withSmoke: !_camera.Sniper);   // own puffs would block the scope
             _camera.Shake(FireShake, FireShakeSeconds);
             _playerFired = false;
         }
-        _tank.Visible = !_camera.Sniper;
     }
 
     /// <summary>Follows the tank (or looks from a fixed preset), then lays the gun where the camera looks unless a script aims it.</summary>
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/TargetRange.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/TargetRange.cs b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
index 620748d..8908691 100644
--- a/src/Tank.Client/scripts/Sandbox/TargetRange.cs
+++ b/src/Tank.Client/scripts/Sandbox/TargetRange.cs
@@ -29,7 +29,6 @@ public partial class TargetRange : Node3D
     public const byte PlayerTeam = 0, TargetTeam = 1;
     public const float ReviveSeconds = 10f;
     private const float BlastSize = 1.8f;
-    private const float HeadMargin = 0.6f;   // the head point floats this far above the turret's armour box
     private static readonly TankPaint Paint = new(Palette.TargetBody, Palette.Track, Palette.TankHub);
 
     private readonly List<Target> _targets = new();
@@ -42,12 +41,9 @@ public partial class TargetRange : Node3D
         public required int Id;
         public required int Slot;   // index in Simulation.Tanks
         public required float Yaw;  // the heading it was placed with: a still target's gun stays straight ahead
-        public required TankModel Model;
-        public required WreckSmoke Smoke;
-        public DustTrail? Dust;     // the patrol's only
+        public required TankActor Actor;   // the patrol's alone kicks up dust
         public TankState Previous, Current;   // the last two ticks, drawn between
         public int WreckedTick = -1;
-        public Vector3 Head;        // world point over the turret, as of the latest Sync
         public float Health = 1f;   // share left, 0 … 1
         public bool Alive = true;
         public bool Shown = true;
@@ -75,25 +71,19 @@ public partial class TargetRange : Node3D
         }
         _driver = new PatrolDriver(SandboxMap.PatrolRoute);
         SN.Vector2 start = SandboxMap.PatrolStart;
-        Target patrol = Add(sim, SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start));
+        Target patrol = Add(sim, SandboxMap.PatrolSpec, start, Angles.YawOf(SandboxMap.PatrolRoute[0].Position - start), dust: true);
         if (parkPatrol) vision.SetWatching(patrol.Id, false);
-        patrol.Dust = new DustTrail();
-        patrol.Model.AddChild(patrol.Dust);
-        patrol.Dust.Build(TankLooks.For(SandboxMap.PatrolSpec.Id).TrackX, SandboxMap.PatrolSpec.HullLength / 2f);
     }
 
-    private Target Add(Simulation sim, TankSpec spec, SN.Vector2 position, float yaw)
+    private Target Add(Simulation sim, TankSpec spec, SN.Vector2 position, float yaw, bool dust = false)
     {
         int slot = sim.TankCount;
         int id = sim.AddTank(spec, position, yaw, TargetTeam);
-        var model = new TankModel();
-        AddChild(model);
-        model.Build(spec, Paint);
-        var smoke = new WreckSmoke();
-        model.AddChild(smoke);
-        smoke.Build(model.TurretRing);   // pours from the open turret ring (a casemate's roof)
-        var target = new Target { Id = id, Slot = slot, Yaw = yaw, Model = model, Smoke = smoke, Previous = sim.Tanks[slot], Current = sim.Tanks[slot] };
-        Observe(target, target.Current, sim.Field.Terrain);   // heads are known before the first Sync
+        var actor = new TankActor();
+        AddChild(actor);
+        actor.Build(spec, Paint, dust);
+        var target = new Target { Id = id, Slot = slot, Yaw = yaw, Actor = actor, Previous = sim.Tanks[slot], Current = sim.Tanks[slot] };
+        actor.Present(target.Current, Observe(target, target.Current, sim.Field.Terrain), 0f, 0f, shown: true);   // heads are known before the first Sync
         _targets.Add(target);
         return target;
     }
@@ -102,7 +92,7 @@ public partial class TargetRange : Node3D
     public (Vector3 Head, float Health, bool Alive, bool Shown) Status(int index)
     {
         Target target = _targets[index];
-        return (target.Head, target.Health, target.Alive, target.Shown);
+        return (target.Actor.Head, target.Health, target.Alive, target.Shown);
     }
 
     /// <summary>Target <paramref name="index"/>'s tank id.</summary>
@@ -112,7 +102,7 @@ public partial class TargetRange : Node3D
     public bool TryGetHead(int tankId, out Vector3 head)
     {
         Target? target = Find(tankId);
-        head = target?.Head ?? Vector3.Zero;
+        head = target?.Actor.Head ?? Vector3.Zero;
         return target is not null;
     }
 
@@ -165,14 +155,12 @@ public partial class TargetRange : Node3D
         if (e.Type == GameEventType.TankDestroyed)
         {
             target.WreckedTick = e.Tick;
-            target.Model.Wreck(sim.Tanks[target.Slot].TurretYaw);
-            target.Smoke.Emitting = true;
+            target.Actor.Wreck(sim.Tanks[target.Slot].TurretYaw);
             fx.Explosion(e.Position.ToGodot() + Vector3.Up * 1.2f, PuffColor.White, BlastSize);
         }
         else if (e.Type == GameEventType.TankRevived)
         {
-            target.Model.Restore();
-            target.Smoke.Emitting = false;
+            target.Actor.Restore();
         }
     }
 
@@ -186,27 +174,22 @@ public partial class TargetRange : Node3D
         {
             TankState state = TankInterpolation.Between(target.Previous, target.Current, alpha);
             bool shown = _vision.IsVisible(target.Id, PlayerTeam);
-            if (shown != target.Shown) target.Model.Visible = target.Shown = shown;
-            target.Model.Apply(Observe(target, state, sim.Field.Terrain), state.TurretYaw, state.GunPitch);
+            target.Shown = shown;
+            TankPose pose = Observe(target, state, sim.Field.Terrain);
             float yawRate = Angles.Wrap(target.Current.Yaw - target.Previous.Yaw) / SimConstants.Dt;
-            target.Model.Animate(state.Speed, yawRate, delta);
-            target.Dust?.Update(shown ? state.Speed : 0f);   // no trail from a hidden patrol, to pop in when it shows
+            target.Actor.Present(state, pose, yawRate, delta, shown);   // a hidden patrol leaves no trail, to pop in when it shows
         }
     }
 
-    /// <summary>Notes the target's head point and health from <paramref name="state"/>; returns its pose.</summary>
+    /// <summary>Notes the target's health from <paramref name="state"/>; returns its pose.</summary>
     private static TankPose Observe(Target target, in TankState state, HeightField terrain)
     {
         TankPose pose = TankPose.Compute(state, terrain);
-        target.Head = (pose.Position + SN.Vector3.UnitY * HeadHeight(state.Spec)).ToGodot();
         target.Health = state.Health / (float)state.Spec.MaxHealth;
         target.Alive = !state.IsDestroyed;
         return pose;
     }
 
-    /// <summary>Height of the head point over the hull origin: a margin above the turret's armour box (3.4 m on the medium tank).</summary>
-    private static float HeadHeight(TankSpec spec) => spec.TurretArmor.Top + HeadMargin;
-
     private Target? Find(int tankId)
     {
         foreach (Target t in _targets)
PATCH
```

新建 `src/Tank.Client/scripts/Vehicles/TankActor.cs`：

```csharp
using Godot;
using Tank.Client.Combat;
using Tank.Client.World;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Vehicles;

/// <summary>
/// One tank on screen: its <see cref="TankModel"/>, the smoke that pours from it once wrecked (<see cref="WreckSmoke"/>)
/// and, if asked for, the dust behind its tracks (<see cref="DustTrail"/>). The player's tank, the practice targets and
/// every tank of a match are all this. The caller poses it each frame with <see cref="Present"/>; the kill itself
/// (<see cref="Wreck"/>, <see cref="Restore"/>) and the blast cloud over it (<see cref="BattleFx"/>) stay the caller's to
/// time. The actor sits at the origin: the model moves itself to the pose in world space.
/// </summary>
public partial class TankActor : Node3D
{
    private const float HeadMargin = 0.6f;   // the head point floats this far above the turret's armour box

    private TankModel _model = null!;
    private WreckSmoke _smoke = null!;
    private DustTrail? _dust;
    private float _headHeight;

    /// <summary>The model, for what the actor doesn't wrap.</summary>
    public TankModel Model => _model;

    /// <summary>World point over the turret (where a health bar goes), as of the latest <see cref="Present"/>.</summary>
    public Vector3 Head { get; private set; }

    public bool IsWrecked => _model.IsWrecked;

    /// <summary>Barrel tip in world space, for muzzle effects.</summary>
    public Transform3D MuzzleTransform => _model.MuzzleTransform;

    /// <param name="paint">Overrides the look's paint (the practice targets are blue-grey).</param>
    /// <param name="dust">Whether it kicks up dust above 10 km/h.</param>
    public void Build(TankSpec spec, TankPaint? paint = null, bool dust = true)
    {
        _headHeight = spec.TurretArmor.Top + HeadMargin;   // 3.4 m over the hull origin on the medium tank
        _model = new TankModel();
        AddChild(_model);
        _model.Build(spec, paint);
        _smoke = new WreckSmoke();
        _model.AddChild(_smoke);
        _smoke.Build(_model.TurretRing);   // pours from the open turret ring (a casemate's roof)
        if (!dust) return;
        _dust = new DustTrail();
        _model.AddChild(_dust);
        _dust.Build(TankLooks.For(spec.Id).TrackX, spec.HullLength / 2f);
    }

    /// <summary>
    /// Poses and animates the tank for this frame: hull on <paramref name="pose"/>, turret and gun from <paramref name="state"/>,
    /// tracks rolling with its speed and <paramref name="yawRate"/> (rad/s, positive turning left), dust from its speed.
    /// <paramref name="shown"/> false hides the tank and stops its dust, which then starts afresh when it shows again.
    /// </summary>
    public void Present(in TankState state, TankPose pose, float yawRate, float delta, bool shown)
    {
        if (shown != Visible) Visible = shown;
        Head = (pose.Position + SN.Vector3.UnitY * _headHeight).ToGodot();
        _model.Apply(pose, state.TurretYaw, state.GunPitch);
        _model.Animate(state.Speed, yawRate, delta);
        _dust?.Update(shown ? state.Speed : 0f);
    }

    /// <summary>
    /// Knocks the tank out: the turret flies off, the paint chars and smoke rises. <paramref name="settled"/> skips the
    /// flight and the build-up, for a wreck seen for the first time already lying there (joining late, a resync).
    /// </summary>
    public void Wreck(float turretYaw, bool settled = false)
    {
        _model.Wreck(turretYaw, settled);
        if (settled) _smoke.StartSettled();
        else _smoke.Emitting = true;
    }

    /// <summary>Puts the turret back, the paint back to normal and stops the smoke (a revived tank).</summary>
    public void Restore()
    {
        _model.Restore();
        _smoke.Emitting = false;
    }

    /// <summary>
    /// The barrel's recoil and the hull's small rock for a shot: the model half of firing, without the muzzle flash (the
    /// caller spawns that at <see cref="MuzzleTransform"/> when it wants one, and not for a shot it only heard of).
    /// </summary>
    public void Kick(float turretYaw) => _model.Fire(turretYaw);
}
```

修改 `src/Tank.Client/scripts/Vehicles/TankModel.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Vehicles/TankModel.cs b/src/Tank.Client/scripts/Vehicles/TankModel.cs
index 7872212..8d7a2c8 100644
--- a/src/Tank.Client/scripts/Vehicles/TankModel.cs
+++ b/src/Tank.Client/scripts/Vehicles/TankModel.cs
@@ -20,6 +20,7 @@ public partial class TankModel : Node3D
     private const float RimStrength = 0.25f;
     private const int PaintMode = 0, WheelMode = 1, TrackMode = 2;   // tank_toon.gdshader "mode"
     private const float CharSeconds = 0.6f;
+    private const float SettledSeconds = TurretToss.FlightTime + CharSeconds;   // a wreck this old has landed and finished charring
     private static readonly StringName CharAmount = new("char_amount");   // cached: a string would make a new StringName per call
 
     private readonly Node3D _body = new() { Name = "Body" };
@@ -104,10 +105,14 @@ public partial class TankModel : Node3D
 
     public bool IsWrecked => _wreckTime >= 0f;
 
-    /// <summary>Knocks the tank out: the turret (if any) flies off and the paint chars black over <see cref="CharSeconds"/>.</summary>
-    public void Wreck(float turretYaw)
+    /// <summary>
+    /// Knocks the tank out: the turret (if any) flies off and the paint chars black over <see cref="CharSeconds"/>. With
+    /// <paramref name="settled"/> (a wreck first seen already dead) the flight and the charring are skipped: the turret lies
+    /// where it lands and the paint is black from the first frame drawn.
+    /// </summary>
+    public void Wreck(float turretYaw, bool settled = false)
     {
-        _wreckTime = 0f;
+        _wreckTime = settled ? SettledSeconds : 0f;
         _tossYaw = turretYaw;
     }
 
PATCH
```


- [ ] **Step 2：构建与测试**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；Art 524、Net 209（没有新测试）。

- [ ] **Step 3：回归截图**

Run: `$S/baseline4b.sh $W build/shots/t2 && python3 $S/pngdiffdirs.py build/shots/base build/shots/t2`，再逐张打开和 `base/` 对照。
Expected: 六张和基线一致。重点看：kill 的炮塔弹飞与焦黑烟、lamp 的被点亮灯与状态面板、wall 的墙血条与伤害数字、patrol 的调试线、heavy 换车后的外观。

- [ ] **Step 4：提交**

```bash
git add -A src/Tank.Client
git commit -m "refactor(client): TankActor (model, wreck smoke, dust) shared by the player and the targets; CombatHud holds the generic sandbox HUD, SandboxHud keeps the target bars and the spotting debug lines; health bars take name labels; wrecks can start settled"
```

---

### Task 3：在线层与启动参数骨架

蓝图 §1.3、§5 T3。`Online/Logic/BattleSession`：`Host`（线程主机）、`HostLoopback`（回环主机）、`Join`，`Advance(dt)`，固定顺序的 `Dispose`（先客户端、再服务线程、最后套接字）。`ThreadedHost`：`LiteNetServerTransport` + `BattleServer` + `ServerRunner`（30 Hz），端口 0 = 任选空闲端口，端口被占抛中文 `InvalidOperationException`；必须传 `BattleProtocol.ConnectKey`。`LoopbackHost`：`LoopbackNetwork` + 累加器每 1/30 秒 `server.Tick()`，时钟是虚拟的。`LanBrowser` + `LanRoomRow`：只取对战房间，版本不符 / 满员不可加入并写明原因，对局进行中给提示。`AutoRoom`：`--tank/--mode/--bots/--autostart/--players` 的状态机，SetTank → SetMode → SetBots → StartMatch 各发一次，`--players N` 人齐才开。人齐后若等的不止自己，再连续满足约 0.5 秒（30 次调用）才发 StartMatch，让客户端的选车命令先到；回房间后 `Reset` 重新核对车型，房间里没显示该车型才重发。`LaunchOptions` 改用 `BattleMode?`，新增 `--players/--seed/--loopback/--sandbox/--match-seconds/--hud-demo`，`--shot-after` 加 `death|ended`；未知开关、要值的开关后面紧跟另一个开关、`--players` 超过 10 都给警告。房主令牌用 `RandomNumberGenerator`；`Dispose` 用 try/finally 保证服务线程一定停；`LanBrowser` 只在发现的房间变了才重建行。`Tank.Net` 的 `LiteNetClientTransport` 构造时 `Connect` 抛异常（地址解析失败）先停掉 NetManager 再抛，不再泄漏套接字和线程。

**Files:**
- Create: `src/Tank.Client/scripts/Online/Logic/{AutoRoom,BattleSession,HostSettings,IHostBackend,LanBrowser,LanRoomRow,LoopbackHost,TextColumns,ThreadedHost}.cs`
- Modify: `Bootstrap/LaunchOptions.cs`、`Bootstrap/Main.cs`、`src/Tank.Net/Transport/LiteNetClientTransport.cs`
- Test: 新建 `tests/Tank.Art.Tests/{AutoRoomTests,LanRoomRowTests,LoopbackSessionTests,ThreadedHostTests}.cs`；修改 `LaunchOptionsTests.cs`、`Tank.Art.Tests.csproj`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/AutoRoomTests.cs`：

```csharp
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class AutoRoomTests
{
    private const byte Me = 0;

    private static RoomPlayer Human(byte slot, string tank = "medium", bool connected = true) =>
        new(slot, $"P{slot}", 0, (byte)IndexOf(tank), false, connected, false);

    private static int IndexOf(string tank) => TankCatalog.All.Select((t, i) => (t, i)).First(x => x.t.Id == tank).i;

    private static RoomStateMessage Room(BattleMode mode = BattleMode.TeamBattle, int bots = 0, bool running = false, params RoomPlayer[] players) =>
        new(mode, HostSlot: 0, (byte)bots, running, players.Length > 0 ? players : new[] { Human(0) });

    private static AutoRoom Everything() => new("heavy", BattleMode.FreeForAll, 4, autoStart: true);

    [Fact]
    public void Nothing_happens_before_the_first_room_state()
    {
        Assert.Equal(AutoStep.Wait, Everything().Next(null, Me, true, 1));
    }

    [Fact]
    public void A_host_sends_tank_then_mode_then_bots_then_start_each_once_as_the_room_catches_up()
    {
        var auto = Everything();

        AutoStep tank = auto.Next(Room(), Me, true, 1);
        AutoStep mode = auto.Next(Room(), Me, true, 1);
        AutoStep bots = auto.Next(Room(BattleMode.FreeForAll), Me, true, 1);
        AutoStep notYet = auto.Next(Room(BattleMode.FreeForAll, 4), Me, true, 1);   // the tank has not come back yet
        AutoStep start = auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "heavy")), Me, true, 1);
        AutoStep after = auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "heavy")), Me, true, 1);

        Assert.Equal(new AutoStep(AutoStepKind.SetTank, TankId: "heavy"), tank);
        Assert.Equal(new AutoStep(AutoStepKind.SetMode, Mode: BattleMode.FreeForAll), mode);
        Assert.Equal(new AutoStep(AutoStepKind.SetBots, Bots: 4), bots);
        Assert.Equal(AutoStepKind.Wait, notYet.Kind);
        Assert.Equal(AutoStepKind.StartMatch, start.Kind);
        Assert.Equal(AutoStepKind.Done, after.Kind);
    }

    [Fact]
    public void The_start_waits_until_the_room_shows_the_mode_the_bots_and_the_tank()
    {
        var auto = Everything();
        for (int i = 0; i < 3; i++) auto.Next(Room(), Me, true, 1);   // tank, mode, bots sent

        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(BattleMode.TeamBattle, 4, false, Human(0, "heavy")), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(BattleMode.FreeForAll, 0, false, Human(0, "heavy")), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "light")), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(0, "heavy")), Me, true, 1).Kind);
    }

    [Fact]
    public void Players_two_does_not_start_with_one_human_and_counts_only_connected_people()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomPlayer bot = new(1, "Bot", 1, 0, true, true, IsBot: true);
        RoomPlayer gone = Human(2, connected: false);

        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(players: new[] { Human(0), bot, gone }), Me, true, 2).Kind);
        Assert.Equal(AutoStepKind.StartMatch, Settle(auto, Room(players: new[] { Human(0), Human(3), bot, gone }), 2).Kind);
    }

    [Fact]
    public void Waiting_for_others_the_host_starts_only_after_the_room_has_been_ready_for_the_settle_calls()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomStateMessage full = Room(players: new[] { Human(0), Human(1) });

        for (int i = 1; i < AutoRoom.SettleCalls; i++)
            Assert.Equal(AutoStepKind.Wait, auto.Next(full, Me, true, 2).Kind);   // a guest just came in: its --tank is on its way

        Assert.Equal(AutoStepKind.StartMatch, auto.Next(full, Me, true, 2).Kind);
    }

    [Fact]
    public void The_settle_count_starts_over_when_the_room_stops_being_ready()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        RoomStateMessage full = Room(players: new[] { Human(0), Human(1) });
        RoomStateMessage left = Room(players: new[] { Human(0), Human(1, connected: false) });
        for (int i = 1; i < AutoRoom.SettleCalls; i++) auto.Next(full, Me, true, 2);

        Assert.Equal(AutoStepKind.Wait, auto.Next(left, Me, true, 2).Kind);   // the guest dropped just before the start

        for (int i = 1; i < AutoRoom.SettleCalls; i++)
            Assert.Equal(AutoStepKind.Wait, auto.Next(full, Me, true, 2).Kind);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(full, Me, true, 2).Kind);
    }

    [Fact]
    public void A_host_that_waits_for_nobody_starts_at_once()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(Room(), Me, true, 1).Kind);
    }

    [Fact]
    public void The_start_is_not_sent_while_a_match_is_running()
    {
        var auto = new AutoRoom(null, null, null, autoStart: true);
        Assert.Equal(AutoStepKind.Wait, auto.Next(Room(running: true), Me, true, 1).Kind);
        Assert.False(auto.Started);
    }

    [Fact]
    public void Without_autostart_the_room_is_set_up_but_never_started()
    {
        var auto = new AutoRoom(null, BattleMode.Annihilation, 2, autoStart: false);
        Assert.Equal(AutoStepKind.SetMode, auto.Next(Room(), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.SetBots, auto.Next(Room(), Me, true, 1).Kind);
        Assert.Equal(AutoStepKind.Done, auto.Next(Room(BattleMode.Annihilation, 2), Me, true, 1).Kind);
    }

    [Fact]
    public void A_guest_only_picks_its_tank_and_never_starts_or_changes_the_room()
    {
        var auto = Everything();
        Assert.Equal(AutoStepKind.SetTank, auto.Next(Room(), 1, isHost: false, 1).Kind);
        for (int i = 0; i < 3; i++)
            Assert.Equal(AutoStepKind.Done, auto.Next(Room(BattleMode.FreeForAll, 4, false, Human(1, "heavy")), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void A_guest_without_a_tank_choice_is_done_at_once()
    {
        Assert.Equal(AutoStepKind.Done, new AutoRoom(null, BattleMode.FreeForAll, 3, true).Next(Room(), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void A_tank_the_room_already_shows_is_not_sent()
    {
        var auto = new AutoRoom("heavy", null, null, autoStart: false);
        Assert.Equal(AutoStepKind.Done, auto.Next(Room(players: Human(1, "heavy")), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void Reset_sends_the_tank_again_only_when_the_room_does_not_show_it()
    {
        // A guest whose SetTank reached the server after the host's start: the server ignored it (the seat was in the match).
        var lost = new AutoRoom("heavy", null, null, autoStart: false);
        Assert.Equal(AutoStepKind.SetTank, lost.Next(Room(players: Human(1)), 1, isHost: false, 1).Kind);
        Assert.Equal(AutoStepKind.Done, lost.Next(Room(running: true, players: Human(1)), 1, isHost: false, 1).Kind);
        lost.Reset();   // back in the room after the match, still in the medium tank
        Assert.Equal(new AutoStep(AutoStepKind.SetTank, TankId: "heavy"), lost.Next(Room(players: Human(1)), 1, isHost: false, 1));

        var kept = new AutoRoom("heavy", null, null, autoStart: false);
        Assert.Equal(AutoStepKind.SetTank, kept.Next(Room(players: Human(1)), 1, isHost: false, 1).Kind);
        kept.Reset();   // the choice made it: nothing to send
        Assert.Equal(AutoStepKind.Done, kept.Next(Room(players: Human(1, "heavy")), 1, isHost: false, 1).Kind);
    }

    [Fact]
    public void Reset_allows_a_second_start_without_repeating_the_setup()
    {
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 1, autoStart: true);
        RoomStateMessage room = Room(BattleMode.TeamBattle, 1);
        auto.Next(room, Me, true, 1);
        auto.Next(room, Me, true, 1);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(room, Me, true, 1).Kind);
        Assert.True(auto.Started);
        Assert.Equal(AutoStepKind.Done, auto.Next(room, Me, true, 1).Kind);

        auto.Reset();

        Assert.False(auto.Started);
        Assert.Equal(AutoStepKind.StartMatch, auto.Next(room, Me, true, 1).Kind);
    }

    /// <summary>Asks <paramref name="auto"/> with the same room until it stops waiting (at most the settle calls) and returns that step.</summary>
    private static AutoStep Settle(AutoRoom auto, RoomStateMessage room, int humansWanted)
    {
        AutoStep step = AutoStep.Wait;
        for (int i = 0; i < AutoRoom.SettleCalls && step.Kind == AutoStepKind.Wait; i++) step = auto.Next(room, Me, true, humansWanted);
        return step;
    }
}
```

新建 `tests/Tank.Art.Tests/LanRoomRowTests.cs`：

```csharp
using System.Net;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class LanRoomRowTests
{
    private static LanRoomRow Row(string name = "Alpha", BattleMode mode = BattleMode.TeamBattle, int players = 3, bool running = false,
        int version = BattleProtocol.Version, int ping = 7) =>
        LanRoomRow.From(new DiscoveredBattleRoom(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 47001),
            new RoomInfoMessage(version, name, mode, players, BattleProtocol.MaxPlayers, running, 47001), ping, 0));

    [Fact]
    public void A_waiting_room_with_space_is_joinable_and_its_line_shows_the_columns()
    {
        LanRoomRow row = Row();
        Assert.True(row.Joinable);
        Assert.Null(row.WhyNot);
        Assert.Equal(new IPEndPoint(IPAddress.Parse("192.168.1.20"), 47001), row.EndPoint);
        Assert.Contains("Alpha", row.Text);
        Assert.Contains("团队对战", row.Text);
        Assert.Contains("3/10", row.Text);
        Assert.Contains("等待中", row.Text);
        Assert.Contains("  7ms", row.Text);
        Assert.EndsWith("192.168.1.20", row.Text);
    }

    [Fact]
    public void A_room_on_another_protocol_version_cannot_be_joined_and_says_why()
    {
        LanRoomRow row = Row(version: BattleProtocol.Version + 1);
        Assert.False(row.Joinable);
        Assert.Contains("版本不符", row.WhyNot);
        Assert.Contains($"v{BattleProtocol.Version + 1}", row.WhyNot);
        Assert.Contains("版本不符", row.Text);
    }

    [Fact]
    public void A_full_room_cannot_be_joined()
    {
        LanRoomRow row = Row(players: BattleProtocol.MaxPlayers);
        Assert.False(row.Joinable);
        Assert.Equal("房间已满", row.WhyNot);
    }

    [Fact]
    public void A_version_mismatch_is_reported_before_a_full_room()
    {
        Assert.Contains("版本不符", Row(players: BattleProtocol.MaxPlayers, version: 1).WhyNot);
    }

    [Fact]
    public void A_running_match_in_a_mode_without_joins_can_be_entered_but_the_row_notes_the_wait()
    {
        LanRoomRow row = Row(mode: BattleMode.Annihilation, running: true);
        Assert.True(row.Joinable);
        Assert.Contains("等下一局", row.WhyNot);
        Assert.Contains("对战中", row.Text);
    }

    [Fact]
    public void A_running_team_battle_takes_newcomers_so_it_carries_no_note()
    {
        LanRoomRow row = Row(mode: BattleMode.TeamBattle, running: true);
        Assert.True(row.Joinable);
        Assert.Null(row.WhyNot);
        Assert.Contains("对战中", row.Text);
    }

    [Fact]
    public void Cjk_room_names_keep_the_columns_aligned()
    {
        LanRoomRow cjk = Row(name: "火狐的房间");
        LanRoomRow ascii = Row(name: "Fox");
        int ColumnOfMode(LanRoomRow r) => TextColumns.Width(r.Text[..r.Text.IndexOf("团队对战", StringComparison.Ordinal)]);
        Assert.Equal(ColumnOfMode(ascii), ColumnOfMode(cjk));
    }

    [Theory]
    [InlineData("abc", 6, "abc   ")]
    [InlineData("房间", 6, "房间  ")]
    [InlineData("房间a", 6, "房间a ")]
    [InlineData("房间房间房间", 6, "房间房间房间")]
    [InlineData("", 3, "   ")]
    public void PadColumns_counts_wide_characters_as_two_columns(string text, int columns, string expected)
    {
        Assert.Equal(expected, TextColumns.PadColumns(text, columns));
    }

    [Theory]
    [InlineData("abc", 3)]
    [InlineData("坦克3D", 6)]
    [InlineData("ＡＢ", 4)]   // full-width Latin
    [InlineData("", 0)]
    public void Width_is_in_monospace_columns(string text, int width)
    {
        Assert.Equal(width, TextColumns.Width(text));
    }
}
```

修改 `tests/Tank.Art.Tests/LaunchOptionsTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/LaunchOptionsTests.cs b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
index 109bea4..ed1f2ab 100644
--- a/tests/Tank.Art.Tests/LaunchOptionsTests.cs
+++ b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
@@ -1,4 +1,6 @@
 using Tank.Client.Bootstrap;
+using Tank.Net.Battle;
+using Tank.Sim.Modes;
 
 namespace Tank.Art.Tests;
 
@@ -44,7 +46,7 @@ public class LaunchOptionsTests
         LaunchOptions options = LaunchOptions.Parse(new[] { "--tank" });
         Assert.Null(options.Tank);
         string warning = Assert.Single(options.Warnings);
-        Assert.Contains("''", warning);
+        Assert.Contains("--tank needs a value", warning);
     }
 
     [Theory]
@@ -65,4 +67,210 @@ public class LaunchOptionsTests
         Assert.Null(options.ShotAfterEvent);
         Assert.Contains("fire|hit|kill|prop|down", Assert.Single(options.Warnings));
     }
+
+    [Theory]
+    [InlineData("teambattle", BattleMode.TeamBattle)]
+    [InlineData("annihilation", BattleMode.Annihilation)]
+    [InlineData("ffa", BattleMode.FreeForAll)]
+    [InlineData("TeamBattle", BattleMode.TeamBattle)]
+    [InlineData("FFA", BattleMode.FreeForAll)]
+    public void Mode_takes_the_battle_mode_ids_in_any_case(string value, BattleMode expected)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--mode", value });
+        Assert.Equal(expected, options.Mode);
+        Assert.Empty(options.Warnings);
+    }
+
+    [Fact]
+    public void Mode_is_left_to_the_server_unless_given()
+    {
+        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Mode);
+    }
+
+    [Theory]
+    [InlineData("coop")]   // the old game's mode
+    [InlineData("")]
+    public void A_bad_mode_warns_with_the_valid_ones_and_leaves_the_default(string value)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--mode", value });
+        Assert.Null(options.Mode);
+        string warning = Assert.Single(options.Warnings);
+        Assert.Contains("teambattle|annihilation|ffa", warning);
+    }
+
+    [Theory]
+    [InlineData("3", 3)]
+    [InlineData("0", 0)]
+    [InlineData("9", 9)]
+    public void Bots_in_range_pass_without_a_warning(string value, int expected)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", value });
+        Assert.Equal(expected, options.Bots);
+        Assert.Empty(options.Warnings);
+    }
+
+    [Theory]
+    [InlineData("10", 9)]
+    [InlineData("99", 9)]
+    [InlineData("-2", 0)]
+    public void Bots_out_of_range_are_clamped_with_a_warning(string value, int expected)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", value });
+        Assert.Equal(expected, options.Bots);
+        Assert.Contains("out of range", Assert.Single(options.Warnings));
+    }
+
+    [Fact]
+    public void Unreadable_bots_become_zero_with_a_warning()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", "many" });
+        Assert.Equal(0, options.Bots);
+        Assert.Contains("'many'", Assert.Single(options.Warnings));
+    }
+
+    [Fact]
+    public void Players_default_to_one_and_must_be_at_least_one()
+    {
+        Assert.Equal(1, LaunchOptions.Parse(Array.Empty<string>()).Players);
+        Assert.Equal(3, LaunchOptions.Parse(new[] { "--players", "3" }).Players);
+        foreach (string bad in new[] { "0", "-1", "two" })
+        {
+            LaunchOptions options = LaunchOptions.Parse(new[] { "--players", bad });
+            Assert.Equal(1, options.Players);
+            Assert.Contains("--players", Assert.Single(options.Warnings));
+        }
+    }
+
+    [Fact]
+    public void Seed_is_an_unsigned_number_and_a_bad_one_warns()
+    {
+        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).Seed);
+        Assert.Equal(42u, LaunchOptions.Parse(new[] { "--seed", "42" }).Seed);
+        Assert.Equal(uint.MaxValue, LaunchOptions.Parse(new[] { "--seed", "4294967295" }).Seed);
+        foreach (string bad in new[] { "-1", "4294967296", "x" })
+        {
+            LaunchOptions options = LaunchOptions.Parse(new[] { "--seed", bad });
+            Assert.Null(options.Seed);
+            Assert.Contains("--seed", Assert.Single(options.Warnings));
+        }
+    }
+
+    [Fact]
+    public void Loopback_and_sandbox_are_switches_that_are_off_by_default()
+    {
+        LaunchOptions none = LaunchOptions.Parse(Array.Empty<string>());
+        Assert.False(none.Loopback);
+        Assert.False(none.Sandbox);
+        LaunchOptions both = LaunchOptions.Parse(new[] { "--loopback", "--sandbox" });
+        Assert.True(both.Loopback);
+        Assert.True(both.Sandbox);
+    }
+
+    [Fact]
+    public void Match_seconds_must_be_above_zero()
+    {
+        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).MatchSeconds);
+        Assert.Equal(12.5, LaunchOptions.Parse(new[] { "--match-seconds", "12.5" }).MatchSeconds);
+        foreach (string bad in new[] { "0", "-3", "soon", "NaN", "100000" })
+        {
+            LaunchOptions options = LaunchOptions.Parse(new[] { "--match-seconds", bad });
+            Assert.Null(options.MatchSeconds);
+            Assert.Contains("--match-seconds", Assert.Single(options.Warnings));
+        }
+    }
+
+    [Fact]
+    public void Hud_demo_takes_a_name()
+    {
+        Assert.Null(LaunchOptions.Parse(Array.Empty<string>()).HudDemo);
+        Assert.Equal("scoreboard", LaunchOptions.Parse(new[] { "--hud-demo", "scoreboard" }).HudDemo);
+    }
+
+    [Fact]
+    public void Netgraph_means_the_panel_starts_open_and_the_camera_switch_is_gone()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--netgraph", "--camera", "chase" });
+        Assert.True(options.NetGraph);
+        Assert.Contains("Unknown option --camera", Assert.Single(options.Warnings));   // its value goes unread, without a second warning
+    }
+
+    [Theory]
+    [InlineData("--autostrat")]
+    [InlineData("--players2")]
+    public void An_unknown_switch_warns_and_the_rest_still_parse(string typo)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--host", typo, "--bots", "2" });
+        Assert.True(options.Host);
+        Assert.Equal(2, options.Bots);
+        Assert.Contains(typo, Assert.Single(options.Warnings));
+    }
+
+    [Theory]
+    [InlineData("--screenshot")]
+    [InlineData("--name")]
+    [InlineData("--bots")]
+    [InlineData("--mode")]
+    [InlineData("--shot-after")]
+    public void A_switch_followed_by_another_switch_instead_of_its_value_warns_once_and_keeps_its_default(string option)
+    {
+        LaunchOptions defaults = LaunchOptions.Parse(Array.Empty<string>());
+        LaunchOptions options = LaunchOptions.Parse(new[] { option, "--screenshot-at", "20" });
+
+        Assert.Equal(20, options.ScreenshotAt);   // the next switch is read as a switch, with its own value
+        Assert.Equal((defaults.ScreenshotPath, defaults.Name, defaults.Bots, defaults.Mode, defaults.ShotAfterEvent),
+            (options.ScreenshotPath, options.Name, options.Bots, options.Mode, options.ShotAfterEvent));
+        string warning = Assert.Single(options.Warnings);
+        Assert.Contains($"{option} needs a value", warning);
+        Assert.Contains("--screenshot-at", warning);
+    }
+
+    [Fact]
+    public void A_bare_double_dash_is_passed_over_quietly()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--", "--host", "--bots", "3" });   // what "godot … -- -- --host" hands over
+        Assert.True(options.Host);
+        Assert.Equal(3, options.Bots);
+        Assert.Empty(options.Warnings);
+    }
+
+    [Fact]
+    public void A_negative_number_is_a_value_not_a_switch()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--bots", "-2" });
+        Assert.Equal(0, options.Bots);
+        Assert.Contains("out of range", Assert.Single(options.Warnings));
+    }
+
+    [Fact]
+    public void Players_above_a_full_room_are_clamped_with_a_warning()
+    {
+        Assert.Equal(BattleProtocol.MaxPlayers, LaunchOptions.Parse(new[] { "--players", BattleProtocol.MaxPlayers.ToString() }).Players);
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--players", "11" });
+        Assert.Equal(BattleProtocol.MaxPlayers, options.Players);
+        Assert.Contains("--players 11", Assert.Single(options.Warnings));
+    }
+
+    [Theory]
+    [InlineData("death:2", "death", 2)]
+    [InlineData("ended:0.5", "ended", 0.5)]
+    public void Shot_after_waits_for_the_own_death_or_the_end_of_the_match(string value, string moment, double delay)
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--shot-after", value });
+        Assert.Equal((moment, delay), (options.ShotAfterEvent, options.ShotAfterDelay));
+        Assert.Empty(options.Warnings);
+    }
+
+    [Fact]
+    public void The_host_options_of_a_smoke_run_parse_together()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[]
+        {
+            "--host", "--loopback", "--mode", "ffa", "--bots", "4", "--players", "1", "--seed", "9", "--match-seconds", "20",
+            "--autostart", "--tank", "heavy", "--port", "47555",
+        });
+        Assert.True(options.Host && options.Loopback && options.AutoStart);
+        Assert.Equal((BattleMode.FreeForAll, 4, 1, 9u, 20.0, "heavy", 47555),
+            (options.Mode!.Value, options.Bots, options.Players, options.Seed!.Value, options.MatchSeconds!.Value, options.Tank, options.Port));
+        Assert.Empty(options.Warnings);
+    }
 }
PATCH
```

新建 `tests/Tank.Art.Tests/LoopbackSessionTests.cs`：

```csharp
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class LoopbackSessionTests
{
    private const double Frame = 1.0 / 60;

    private static HostSettings Settings(int bots = 3) =>
        new("Tester", "Tester's room", Port: 47100, BattleMode.TeamBattle, bots, Seed: 7, MatchSeconds: null);

    /// <summary>One frame the way the game loop does it: the host's world, the auto-room, then the client.</summary>
    private static void Step(BattleSession session, AutoRoom auto)
    {
        BattleClient client = session.Client;
        session.Advance(Frame);
        auto.Next(client.Room, client.Slot, client.IsHost, 1).SendTo(client);
        client.Update(Frame, LocalCommand.None);
    }

    [Fact]
    public void A_loopback_host_starts_the_match_by_itself_within_three_virtual_seconds()
    {
        using BattleSession session = BattleSession.HostLoopback(Settings(), _ => { });
        var auto = new AutoRoom(tankId: "heavy", BattleMode.TeamBattle, bots: 3, autoStart: true);

        double elapsed = 0;
        while (elapsed < 3 && !(session.Client.Phase == BattlePhase.InMatch && session.Client.HasTank))
        {
            Step(session, auto);
            elapsed += Frame;
        }

        Assert.True(session.IsHosting);
        Assert.True(session.IsVirtualClock);
        Assert.Null(session.ServerTick);
        Assert.True(session.Client.IsHost);
        Assert.Equal(BattlePhase.InMatch, session.Client.Phase);
        Assert.True(session.Client.HasTank);
        Assert.Equal("heavy", session.Client.Own.Spec.Id);
    }

    [Fact]
    public void The_match_runs_ten_more_virtual_seconds_with_its_bots_and_without_trouble()
    {
        using BattleSession session = BattleSession.HostLoopback(Settings(), _ => { });
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 3, autoStart: true);
        for (int i = 0; i < 3 * 60; i++) Step(session, auto);
        Assert.Equal(BattlePhase.InMatch, session.Client.Phase);
        double startedAt = session.Now;

        for (int i = 0; i < 10 * 60; i++) Step(session, auto);

        Assert.Equal(10, session.Now - startedAt, precision: 6);
        Assert.Equal(BattlePhase.InMatch, session.Client.Phase);
        Assert.True(session.Client.HasTank);   // others show only while spotted, so their count is not asserted
        Assert.Null(session.Client.LastPacketError);
    }

    [Fact]
    public void The_clock_only_moves_when_the_session_is_advanced()
    {
        using BattleSession session = BattleSession.HostLoopback(Settings(), _ => { });
        Assert.Equal(0, session.Now);
        session.Advance(0.5);
        Assert.Equal(0.5, session.Now, precision: 9);
    }

    [Fact]
    public void Dispose_can_be_called_twice()
    {
        BattleSession session = BattleSession.HostLoopback(Settings(bots: 0), _ => { });
        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void A_joined_session_hosts_nothing_and_follows_the_wall_clock()
    {
        using BattleSession session = BattleSession.Join("127.0.0.1", 47199, "Guest");
        Assert.False(session.IsHosting);
        Assert.False(session.IsVirtualClock);
        Assert.Null(session.ServerTick);
        Assert.Equal(BattlePhase.Connecting, session.Client.Phase);
        session.Advance(1);   // nothing to step
        session.Dispose();
    }
}
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index bb128e7..8a82a23 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -16,7 +16,7 @@
     <Compile Include="..\..\src\Tank.Client\scripts\**\Logic\**\*.cs" LinkBase="Logic" />
     <!-- The palette is engine-neutral too, so look data and vertex colours can use it. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
-    <!-- So are the launch options (they need Tank.Core and Tank.Net for the old game's switches). -->
+    <!-- So are the launch options (they only need Tank.Sim and Tank.Net for the mode names and limits). -->
     <Compile Include="..\..\src\Tank.Client\scripts\Bootstrap\LaunchOptions.cs" LinkBase="Bootstrap" />
     <!-- So are the scripted range demos and the tick interpolation (only sim maths). -->
     <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs;..\..\src\Tank.Client\scripts\Sandbox\TankInterpolation.cs" LinkBase="Sandbox" />
PATCH
```

新建 `tests/Tank.Art.Tests/ThreadedHostTests.cs`：

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>The real thing on 127.0.0.1: a UDP socket and a server thread. Where the sandbox forbids binding, the tests do nothing.</summary>
public class ThreadedHostTests
{
    private static HostSettings Settings(int port) => new("Tester", "Tester's room", port, BattleMode.TeamBattle, 0, Seed: 1, MatchSeconds: null);

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private static BattleSession? TryHost(int port)
    {
        try
        {
            return BattleSession.Host(Settings(port), _ => { });
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
        {
            return null;   // no UDP here: nothing to test
        }
    }

    [Fact]
    public void A_threaded_host_seats_its_own_client_as_the_host()
    {
        using BattleSession? session = TryHost(FreeUdpPort());
        if (session is null) return;

        var clock = Stopwatch.StartNew();
        double last = 0;
        // the welcome puts the client in the room; the room state (whose host slot IsHost reads) is the next message
        while (clock.Elapsed.TotalSeconds < 5 && (session.Client.Phase != BattlePhase.Room || session.Client.Room is null))
        {
            double now = clock.Elapsed.TotalSeconds;
            session.Advance(now - last);
            session.Client.Update(now - last, LocalCommand.None);
            last = now;
            Thread.Sleep(5);
        }

        Assert.True(session.IsHosting);
        Assert.False(session.IsVirtualClock);
        Assert.NotNull(session.ServerTick);
        Assert.Equal(BattlePhase.Room, session.Client.Phase);
        Assert.NotNull(session.Client.Room);
        Assert.True(session.Client.IsHost);
    }

    [Fact]
    public void A_second_host_on_a_taken_port_throws_InvalidOperationException_and_leaves_the_first_running()
    {
        int port = FreeUdpPort();
        using BattleSession? first = TryHost(port);
        if (first is null) return;

        BattleSession? second = null;
        try
        {
            second = BattleSession.Host(Settings(port), _ => { });
        }
        catch (InvalidOperationException ex)
        {
            Assert.Contains(port.ToString(), ex.Message);
        }
        finally
        {
            second?.Dispose();   // the platform let two sockets share the port: nothing to assert about a busy one
        }
    }

    [Fact]
    public void Dispose_stops_the_thread_and_can_be_called_twice()
    {
        BattleSession? session = TryHost(FreeUdpPort());
        if (session is null) return;
        session.Dispose();
        session.Dispose();
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，命名空间 `Tank.Client.Online` 和 `AutoRoom`、`AutoStep`、`BattleSession`、`HostSettings`、`LanRoomRow` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index e4262dd..1516eef 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -1,33 +1,62 @@
 using System.Globalization;
-using Tank.Core.World;
+using Tank.Net.Battle;
 using Tank.Net.Protocol;
+using Tank.Sim.Modes;
 using Tank.Sim.Vehicles;
 
 namespace Tank.Client.Bootstrap;
 
 /// <summary>
 /// Command-line switches (pass after "--"), used for quick testing and automated screenshots:
-/// --name N  --host  --join IP[:PORT]  --mode coop|basebattle|deathmatch  --bots N  --autostart
-/// --camera chase  --netgraph  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
+/// --name N  --host  --join IP[:PORT]  --port N  --mode teambattle|annihilation|ffa  --bots N  --autostart  --players N（房主等够 N 个真人才自动开局，N 大于 1 时人齐后再等约半秒；默认 1，最多一个房间的人数 10）
+/// --seed N（对局种子，可复现）  --match-seconds S（对局时长，覆盖模式默认值）  --loopback（房主用虚拟网络和固定步长，没有套接字和线程，用于确定性冒烟）
+/// --sandbox（启动后直接进试驾场）  --hud-demo NAME（对局 HUD 画廊：用预制帧在纯色底上画 HUD 的每种状态，用于验收排版）
+/// --netgraph（F3 网络面板一开始就打开）  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
 /// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
 /// --camera-preset front|side|close|closerear|sprocket|hill|spin|orbit|bush|target|yard|gate|patrol（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
 /// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
 /// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush|lamp（试驾场：自动对靶车开炮或开车撞墙，用于截图）
-/// --shot-after fire|hit|kill|prop|down:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤或第一次有物件被毁后再过 SECONDS 秒截图）
-/// --tank light|medium|heavy|td（试驾场：开局的车型，默认中坦）  --spot-debug（试驾场：一开始就显示点亮调试文字，同 V 键）
+/// --shot-after fire|hit|kill|prop|down|death|ended:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤、第一次有物件被毁、
+/// 自己的坦克第一次被击毁（death）或对局结算开始（ended，仅对局）后再过 SECONDS 秒截图）
+/// --tank light|medium|heavy|td（开局的车型：试驾场里默认中坦；房间里则在连上后自动选这辆）  --spot-debug（试驾场：一开始就显示点亮调试文字，同 V 键）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
-/// for the caller to report.
+/// for the caller to report, and so are a switch it doesn't know and a switch that needs a value but is followed by
+/// another switch (or by nothing), which is then left as it was. This is the only place the client reads its arguments.
 /// </summary>
 public sealed record LaunchOptions
 {
+    private static readonly string[] ShotMoments = { "fire", "hit", "kill", "prop", "down", "death", "ended" };
+    private const int MaxBots = BattleProtocol.MaxPlayers - 1;
+
     public string? Name { get; init; }
     public bool Host { get; init; }
     public string? JoinAddress { get; init; }
     public int Port { get; init; } = ProtocolInfo.DefaultPort;
-    public GameMode Mode { get; init; } = GameMode.Coop;
+
+    /// <summary>The mode to host (and the host's auto-room to set); null leaves the server's default.</summary>
+    public BattleMode? Mode { get; init; }
     public int Bots { get; init; }
     public bool AutoStart { get; init; }
-    public bool ChaseCamera { get; init; }
+
+    /// <summary>How many connected people the host waits for before starting by itself (<c>--players</c>); at least 1.</summary>
+    public int Players { get; init; } = 1;
+
+    /// <summary>A fixed match seed (<c>--seed</c>); null for a random one per match.</summary>
+    public uint? Seed { get; init; }
+
+    /// <summary>Host on a virtual network with fixed steps (<c>--loopback</c>): no sockets, no threads.</summary>
+    public bool Loopback { get; init; }
+
+    /// <summary>Start straight in the sandbox (<c>--sandbox</c>).</summary>
+    public bool Sandbox { get; init; }
+
+    /// <summary>A match length in seconds that replaces the mode's (<c>--match-seconds</c>); null for the mode's own.</summary>
+    public double? MatchSeconds { get; init; }
+
+    /// <summary>The HUD gallery to show instead of a match (<c>--hud-demo</c>); null when not given.</summary>
+    public string? HudDemo { get; init; }
+
+    /// <summary>Whether the F3 network panel starts open (<c>--netgraph</c>).</summary>
     public bool NetGraph { get; init; }
     public string? ScreenshotPath { get; init; }
     public double ScreenshotAt { get; init; } = 6;
@@ -42,11 +71,11 @@ public sealed record LaunchOptions
     public bool SpotDebug { get; init; }
     public string? RangeDemo { get; init; }
 
-    /// <summary>"fire", "hit", "kill", "prop" or "down" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
+    /// <summary>"fire", "hit", "kill", "prop", "down", "death" or "ended" (from <c>--shot-after EVENT:SECONDS</c>); null when not given.</summary>
     public string? ShotAfterEvent { get; init; }
     public double ShotAfterDelay { get; init; }
 
-    /// <summary>The <c>TankCatalog</c> id to start the sandbox in (<c>--tank</c>); null when not given or not a known tank.</summary>
+    /// <summary>The <c>TankCatalog</c> id to start in (<c>--tank</c>); null when not given or not a known tank.</summary>
     public string? Tank { get; init; }
 
     /// <summary>One line per option value that couldn't be read (and was ignored or replaced by its default).</summary>
@@ -58,17 +87,30 @@ public sealed record LaunchOptions
         var warnings = new List<string>();
         for (int i = 0; i < args.Count; i++)
         {
-            string Next() => i + 1 < args.Count ? args[++i] : "";
-            o = args[i] switch
+            string arg = args[i];
+            bool missing = false;
+            string Next()
+            {
+                if (i + 1 < args.Count && !IsSwitch(args[i + 1])) return args[++i];
+                missing = true;   // "--screenshot --screenshot-at 20" must not take "--screenshot-at" for the file
+                return "";
+            }
+            int warned = warnings.Count;
+            LaunchOptions parsed = arg switch
             {
                 "--name" => o with { Name = Next() },
                 "--host" => o with { Host = true },
                 "--join" => o with { JoinAddress = Next() },
                 "--port" => o with { Port = int.TryParse(Next(), out int p) ? p : o.Port },
-                "--mode" => o with { Mode = Enum.TryParse(Next(), true, out GameMode m) ? m : o.Mode },
-                "--bots" => o with { Bots = int.TryParse(Next(), out int b) ? Math.Clamp(b, 0, 7) : 0 },
+                "--mode" => ParseMode(o, Next(), warnings),
+                "--bots" => o with { Bots = ParseBots(Next(), warnings) },
+                "--players" => o with { Players = ParsePlayers(Next(), o.Players, warnings) },
+                "--seed" => ParseSeed(o, Next(), warnings),
+                "--match-seconds" => ParseMatchSeconds(o, Next(), warnings),
                 "--autostart" => o with { AutoStart = true },
-                "--camera" => o with { ChaseCamera = Next() == "chase" },
+                "--loopback" => o with { Loopback = true },
+                "--sandbox" => o with { Sandbox = true },
+                "--hud-demo" => o with { HudDemo = Next() },
                 "--netgraph" => o with { NetGraph = true },
                 "--no-update" => o with { NoUpdate = true },
                 "--update-now" => o with { UpdateNow = true },
@@ -84,12 +126,32 @@ public sealed record LaunchOptions
                 "--screenshot" => o with { ScreenshotPath = Next() },
                 "--screenshot-at" => o with { ScreenshotAt = Seconds("--screenshot-at", Next(), o.ScreenshotAt, warnings) },
                 "--quit-after" => o with { QuitAfter = Seconds("--quit-after", Next(), 0, warnings) },
-                _ => o,
+                _ => Unknown(o, arg, warnings),
             };
+            if (missing)
+            {
+                // Whatever the switch made of the empty value, and said about it, gives way to one plain warning.
+                warnings.RemoveRange(warned, warnings.Count - warned);
+                warnings.Add(i + 1 < args.Count
+                    ? $"{arg} needs a value but is followed by {args[i + 1]}; {arg} is ignored."
+                    : $"{arg} needs a value but is the last argument; it is ignored.");
+                continue;
+            }
+            o = parsed;
         }
         return o with { Warnings = warnings };
     }
 
+    /// <summary>"--x", not a bare "--": the dev scripts' "-- args…" passes the separator itself through, and it means nothing here.</summary>
+    private static bool IsSwitch(string arg) => arg.Length > 2 && arg.StartsWith("--", StringComparison.Ordinal);
+
+    /// <summary>A switch this parser doesn't know (an old one like <c>--camera</c>, or a typo) is ignored with a warning; a stray value is just ignored.</summary>
+    private static LaunchOptions Unknown(LaunchOptions o, string arg, List<string> warnings)
+    {
+        if (IsSwitch(arg)) warnings.Add($"Unknown option {arg}; it is ignored. The options are listed in LaunchOptions.cs.");
+        return o;
+    }
+
     /// <summary><paramref name="value"/> as seconds, or <paramref name="fallback"/> (noting a warning) when it isn't a number.</summary>
     private static double Seconds(string option, string value, double fallback, List<string> warnings)
     {
@@ -98,12 +160,60 @@ public sealed record LaunchOptions
         return fallback;
     }
 
+    private static LaunchOptions ParseMode(LaunchOptions o, string value, List<string> warnings)
+    {
+        if (ModeRules.TryParse(value, out BattleMode mode)) return o with { Mode = mode };
+        warnings.Add($"Bad --mode '{value}'; use {string.Join("|", ModeRules.All.Select(ModeRules.Id))}. Using the server's default.");
+        return o with { Mode = null };
+    }
+
+    private static int ParseBots(string value, List<string> warnings)
+    {
+        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int bots))
+        {
+            warnings.Add($"Bad --bots '{value}'; use a number from 0 to {MaxBots}. Using 0.");
+            return 0;
+        }
+        int clamped = Math.Clamp(bots, 0, MaxBots);
+        if (clamped != bots) warnings.Add($"--bots {bots} is out of range; using {clamped} (0 to {MaxBots}).");
+        return clamped;
+    }
+
+    private static int ParsePlayers(string value, int fallback, List<string> warnings)
+    {
+        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int players) || players < 1)
+        {
+            warnings.Add($"Bad --players '{value}'; use a whole number of at least 1. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
+            return fallback;
+        }
+        if (players <= BattleProtocol.MaxPlayers) return players;
+        // More could never be in the room: the host would wait for ever.
+        warnings.Add($"--players {players} is more than a room holds; using {BattleProtocol.MaxPlayers}.");
+        return BattleProtocol.MaxPlayers;
+    }
+
+    private static LaunchOptions ParseSeed(LaunchOptions o, string value, List<string> warnings)
+    {
+        if (uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed)) return o with { Seed = seed };
+        warnings.Add($"Bad --seed '{value}'; use a whole number from 0 to {uint.MaxValue}. Using a random seed.");
+        return o;
+    }
+
+    private static LaunchOptions ParseMatchSeconds(LaunchOptions o, string value, List<string> warnings)
+    {
+        // The server refuses anything outside 0..MaxMatchSeconds, so a value it would throw on is rejected here.
+        if (TryParseSeconds(value, out double seconds) && seconds > 0 && seconds <= BattleServerOptions.MaxMatchSeconds)
+            return o with { MatchSeconds = seconds };
+        warnings.Add($"Bad --match-seconds '{value}'; use SECONDS above 0, e.g. 30. Using the mode's own length.");
+        return o;
+    }
+
     private static LaunchOptions ParseShotAfter(LaunchOptions o, string value, List<string> warnings)
     {
         string[] parts = value.Split(':');
-        if (parts.Length != 2 || parts[0] is not ("fire" or "hit" or "kill" or "prop" or "down") || !TryParseSeconds(parts[1], out double delay))
+        if (parts.Length != 2 || !ShotMoments.Contains(parts[0]) || !TryParseSeconds(parts[1], out double delay))
         {
-            warnings.Add($"Bad --shot-after '{value}'; use fire|hit|kill|prop|down:SECONDS, e.g. kill:2.5.");
+            warnings.Add($"Bad --shot-after '{value}'; use {string.Join("|", ShotMoments)}:SECONDS, e.g. kill:2.5.");
             return o;
         }
         return o with { ShotAfterEvent = parts[0], ShotAfterDelay = delay };
@@ -118,5 +228,5 @@ public sealed record LaunchOptions
     }
 
     private static bool TryParseSeconds(string value, out double seconds) =>
-        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
+        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && double.IsFinite(seconds);
 }
PATCH
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.cs b/src/Tank.Client/scripts/Bootstrap/Main.cs
index b58a06b..5bd29d2 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.cs
@@ -45,7 +45,7 @@ public partial class Main : Node
         _lobby.LeaveRequested += () => LeaveToMenu("已离开房间。");
         AddChild(_lobby);
 
-        if (_options.Host) Host(_menu.PlayerName, _options.Mode);
+        if (_options.Host) Host(_menu.PlayerName, GameMode.Coop);   // T4 rewrites this flow for the battle modes
         else if (_options.JoinAddress is { } address) JoinAddress(address);
         InitUpdates();
     }
@@ -194,7 +194,6 @@ public partial class Main : Node
         _game = new GameView();
         AddChild(_game);
         MoveChild(_game, 0); // draw the 3D world behind the UI screens
-        if (_options.ChaseCamera) _game.Rig.Mode = CameraMode.Chase;
         _hud = new Hud();
         AddChild(_hud);
     }
PATCH
```

新建 `src/Tank.Client/scripts/Online/Logic/AutoRoom.cs`：

```csharp
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.Online;

public enum AutoStepKind { Wait, Done, SetTank, SetMode, SetBots, StartMatch }

/// <summary>One room command to send now (the payload fields that kind uses), or <see cref="AutoStepKind.Wait"/> / <see cref="AutoStepKind.Done"/>.</summary>
public readonly record struct AutoStep(AutoStepKind Kind, string? TankId = null, BattleMode Mode = default, int Bots = 0)
{
    public static readonly AutoStep Wait = new(AutoStepKind.Wait);
    public static readonly AutoStep Done = new(AutoStepKind.Done);

    /// <summary>Sends this step's room command; Wait and Done send nothing.</summary>
    public void SendTo(BattleClient client)
    {
        switch (Kind)
        {
            case AutoStepKind.SetTank: client.SetTank(TankId!); break;
            case AutoStepKind.SetMode: client.SetMode(Mode); break;
            case AutoStepKind.SetBots: client.SetBots(Bots); break;
            case AutoStepKind.StartMatch: client.StartMatch(); break;
        }
    }
}

/// <summary>
/// Drives the room from the launch options (<c>--tank</c>, <c>--mode</c>, <c>--bots</c>, <c>--autostart</c>, <c>--players</c>):
/// asked once per frame what to do next, it answers with at most one command, each sent once, in the order tank, mode,
/// bots, start. Only the host starts, and only once the room shows what was asked for and enough humans are in; when
/// more than one is wanted, that has to hold for <see cref="SettleCalls"/> calls in a row first, so a guest that just
/// came in gets its own <c>--tank</c> to the server before the match starts (the server ignores a tank change once the
/// seat is in the match). A command is never repeated within a round: if the server ignores it (a refused start) the
/// room is left as it is; <see cref="Reset"/> starts the next round, which re-sends the tank only if the room lost it.
/// </summary>
public sealed class AutoRoom
{
    /// <summary>How many calls in a row the room has to be ready before a host waiting for others starts: about half a second at 60 frames a second.</summary>
    public const int SettleCalls = 30;

    private readonly string? _tankId;
    private readonly BattleMode? _mode;
    private readonly int? _bots;
    private readonly bool _autoStart;
    private bool _sentTank, _sentMode, _sentBots, _sentStart;
    private int _readyCalls;   // consecutive calls the room has been ready to start

    /// <param name="tankId">A <c>TankCatalog</c> id to pick, or null to leave the choice alone.</param>
    /// <param name="mode">The mode to set (host only), or null.</param>
    /// <param name="bots">The bot count to set (host only), or null.</param>
    /// <param name="autoStart">Whether the host starts the match by itself.</param>
    public AutoRoom(string? tankId, BattleMode? mode, int? bots, bool autoStart)
    {
        _tankId = tankId;
        _mode = mode;
        _bots = bots;
        _autoStart = autoStart;
    }

    /// <summary>Whether the start command has gone out (until <see cref="Reset"/>).</summary>
    public bool Started => _sentStart;

    /// <summary>
    /// Allows the start to be sent again: call when the room is back after a match. Mode and bots stay as sent; the tank
    /// is looked at again and re-sent if the room doesn't show it (a guest's choice that arrived after the match began).
    /// </summary>
    public void Reset()
    {
        _sentStart = false;
        _sentTank = false;
        _readyCalls = 0;
    }

    /// <param name="room">The latest room state, null before the first one arrives.</param>
    /// <param name="slot">The client's own slot.</param>
    /// <param name="humansWanted">How many connected people the room needs before the host starts (<c>--players</c>).</param>
    public AutoStep Next(RoomStateMessage? room, byte slot, bool isHost, int humansWanted)
    {
        if (room is null) return AutoStep.Wait;
        if (_tankId is not null && !_sentTank)
        {
            _sentTank = true;
            if (!HasTank(room, slot)) return new AutoStep(AutoStepKind.SetTank, TankId: _tankId);
        }
        if (!isHost) return AutoStep.Done;
        if (_mode is { } mode && !_sentMode)
        {
            _sentMode = true;
            return new AutoStep(AutoStepKind.SetMode, Mode: mode);
        }
        if (_bots is { } bots && !_sentBots)
        {
            _sentBots = true;
            return new AutoStep(AutoStepKind.SetBots, Bots: bots);
        }
        if (!_autoStart || _sentStart) return AutoStep.Done;
        if (!ReadyToStart(room, slot, humansWanted))
        {
            _readyCalls = 0;
            return AutoStep.Wait;
        }
        if (humansWanted > 1 && ++_readyCalls < SettleCalls) return AutoStep.Wait;   // let the newcomer's room commands land first
        _sentStart = true;
        return new AutoStep(AutoStepKind.StartMatch);
    }

    private bool ReadyToStart(RoomStateMessage room, byte slot, int humansWanted)
    {
        if (room.MatchRunning) return false;
        if (_mode is { } mode && room.Mode != mode) return false;
        if (_bots is { } bots && room.Bots != bots) return false;
        if (_tankId is not null && !HasTank(room, slot)) return false;
        return room.Players.Count(p => p.Connected && !p.IsBot) >= humansWanted;
    }

    private bool HasTank(RoomStateMessage room, byte slot)
    {
        RoomPlayer? me = room.Players.FirstOrDefault(p => p.Slot == slot);
        return me is not null && me.TankIndex < TankCatalog.All.Count && TankCatalog.All[me.TankIndex].Id == _tankId;
    }
}
```

新建 `src/Tank.Client/scripts/Online/Logic/BattleSession.cs`：

```csharp
using System.Diagnostics;
using System.Security.Cryptography;
using Tank.Net.Battle;
using Tank.Net.Transport;

namespace Tank.Client.Online;

/// <summary>
/// One way into a battle: a <see cref="BattleClient"/> plus, when this process hosts the room, the server it talks to.
/// Hosting runs the server on its own thread (<see cref="Host"/>) or, for tests and screenshots, on a virtual network
/// stepped from the main thread (<see cref="HostLoopback"/>). The owner calls <see cref="Advance"/> every frame and
/// <c>Client.Update</c> itself; nothing here touches the engine.
/// </summary>
public sealed class BattleSession : IDisposable
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly IHostBackend? _backend;
    private bool _disposed;

    private BattleSession(IHostBackend? backend, Func<IClientTransport> connect, string playerName, uint token)
    {
        _backend = backend;
        Client = new BattleClient(connect(), playerName, () => Now, token);
    }

    public BattleClient Client { get; }

    public bool IsHosting => _backend is not null;

    /// <summary>True for a loopback host: time only moves in <see cref="Advance"/>, so the caller steps frames by a fixed amount.</summary>
    public bool IsVirtualClock => _backend?.IsVirtualClock ?? false;

    /// <summary>Seconds, monotonic; the clock the client runs on.</summary>
    public double Now => _backend?.Now ?? _clock.Elapsed.TotalSeconds;

    /// <summary>Average and worst server tick in ms; only when hosting on a thread.</summary>
    public (double AvgMs, double MaxMs)? ServerTick => _backend?.ServerTick;

    /// <summary>
    /// Starts a listen server and joins it as its owner. Throws <see cref="InvalidOperationException"/> (the message is
    /// fit for the menu's status line) when the UDP port is taken, e.g. by a second instance on this machine.
    /// </summary>
    public static BattleSession Host(HostSettings settings, Action<string> log) =>
        Start(settings, owner => new ThreadedHost(settings, owner, log));

    /// <summary>A host on a virtual network and clock: no sockets, no threads, the same match for the same seed.</summary>
    public static BattleSession HostLoopback(HostSettings settings, Action<string> log) =>
        Start(settings, owner => new LoopbackHost(settings, owner, log));

    /// <summary>Connects to a room. A non-zero <paramref name="token"/> reclaims the seat it was issued for.</summary>
    public static BattleSession Join(string host, int port, string name, uint token = 0) =>
        new(null, () => new LiteNetClientTransport(host, port, BattleProtocol.ConnectKey), name, token);

    /// <summary>Moves the host's world on: a loopback host steps its network and server; a threaded host runs by itself.</summary>
    public void Advance(double dt) => _backend?.Advance(dt);

    /// <summary>Closes the client, then stops the server thread, then the socket — the server even if the client throws. Safe to call again.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Client.Dispose();
        }
        finally
        {
            _backend?.Dispose();
        }
    }

    private static BattleSession Start(HostSettings settings, Func<uint, IHostBackend> startBackend)
    {
        uint owner = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);   // it reclaims the host's seat, so as unguessable as the server's own tokens
        IHostBackend backend = startBackend(owner);
        try
        {
            return new BattleSession(backend, backend.ConnectLocal, settings.PlayerName, owner);
        }
        catch
        {
            backend.Dispose();
            throw;
        }
    }
}
```

新建 `src/Tank.Client/scripts/Online/Logic/HostSettings.cs`：

```csharp
using Tank.Sim.Modes;

namespace Tank.Client.Online;

/// <summary>What the host of a room chose: who they are, the room's name and port, and the first mode and bot count.</summary>
/// <param name="Port">The UDP port to listen on; 0 lets the system pick a free one (tests).</param>
/// <param name="Seed">A fixed match seed (reproducible runs); null picks a random one per match.</param>
/// <param name="MatchSeconds">A match length other than the mode's (screenshots, smoke runs); null for the mode's own.</param>
public sealed record HostSettings(string PlayerName, string RoomName, int Port, BattleMode Mode, int Bots, uint? Seed, double? MatchSeconds);
```

新建 `src/Tank.Client/scripts/Online/Logic/IHostBackend.cs`：

```csharp
using Tank.Net.Transport;

namespace Tank.Client.Online;

/// <summary>
/// The server half of a hosted <see cref="BattleSession"/>: where the battle server runs and how the host's own client
/// reaches it. <see cref="ThreadedHost"/> is the real thing (a UDP socket and a server thread); <see cref="LoopbackHost"/>
/// is the deterministic one for tests and screenshots (a virtual network stepped from the main thread).
/// </summary>
internal interface IHostBackend : IDisposable
{
    /// <summary>True when time only moves in <see cref="Advance"/>: the caller must then step frames by a fixed amount.</summary>
    bool IsVirtualClock { get; }

    /// <summary>Seconds, monotonic; the clock the host's client runs on.</summary>
    double Now { get; }

    /// <summary>Average and worst server tick in ms, when something measures them (the threaded host); otherwise null.</summary>
    (double AvgMs, double MaxMs)? ServerTick { get; }

    /// <summary>Opens the host's own link to its server. Called once, after the backend is up.</summary>
    IClientTransport ConnectLocal();

    /// <summary>Moves time on by <paramref name="dt"/> seconds. A no-op when the server runs on its own thread.</summary>
    void Advance(double dt);
}
```

新建 `src/Tank.Client/scripts/Online/Logic/LanBrowser.cs`：

```csharp
using System.Diagnostics;
using System.Net.Sockets;
using Tank.Net.Protocol;
using Tank.Net.Transport;

namespace Tank.Client.Online;

/// <summary>
/// The menu's view of the LAN: wraps <see cref="LanDiscovery"/>, keeps only battle rooms, and turns a failing socket
/// (no network, a firewall) into <see cref="Error"/> text instead of an exception. Call <see cref="Poll"/> every frame;
/// <see cref="Rows"/> is a new list only when a room was heard from, came or went (each reply is a new record), so a
/// caller can tell a change by reference.
/// </summary>
public sealed class LanBrowser : IDisposable
{
    private const double RetrySeconds = 2;   // after a failure, how long before the socket is opened again

    private readonly int _port;
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private LanDiscovery? _discovery;
    private DiscoveredBattleRoom[] _heard = Array.Empty<DiscoveredBattleRoom>();   // what Rows was built from
    private double _retryAt;

    /// <param name="port">The port rooms are listening for discovery on (only the default one is broadcast to).</param>
    public LanBrowser(int port = ProtocolInfo.DefaultPort) => _port = port;

    /// <summary>The battle rooms heard from lately, by name.</summary>
    public IReadOnlyList<LanRoomRow> Rows { get; private set; } = Array.Empty<LanRoomRow>();

    /// <summary>Why searching doesn't work right now (for the menu's status line); null when it does.</summary>
    public string? Error { get; private set; }

    public void Poll()
    {
        double now = _clock.Elapsed.TotalSeconds;
        if (_discovery is null && now < _retryAt) return;
        try
        {
            _discovery ??= new LanDiscovery(_port);
            _discovery.Poll();
            if (!SameAsHeard(_discovery.BattleRooms))
            {
                _heard = _discovery.BattleRooms.ToArray();
                Rows = _heard.OrderBy(r => r.Info.RoomName, StringComparer.Ordinal).ThenBy(r => r.EndPoint.ToString(), StringComparer.Ordinal)
                    .Select(LanRoomRow.From).ToList();
            }
            Error = null;
        }
        catch (Exception ex) when (ex is SocketException or InvalidOperationException)
        {
            Fail(ex.Message, now);
        }
    }

    public void Dispose()
    {
        _discovery?.Dispose();
        _discovery = null;
    }

    /// <summary>Whether the discovered rooms are the very records the rows were built from (none came, went or replied since).</summary>
    private bool SameAsHeard(IReadOnlyCollection<DiscoveredBattleRoom> rooms)
    {
        if (rooms.Count != _heard.Length) return false;
        if (_heard.Length == 0) return true;   // nothing out there: not even an enumerator
        int i = 0;
        foreach (DiscoveredBattleRoom room in rooms)
            if (!ReferenceEquals(room, _heard[i++])) return false;
        return true;
    }

    private void Fail(string message, double now)
    {
        Dispose();
        _heard = Array.Empty<DiscoveredBattleRoom>();
        Rows = Array.Empty<LanRoomRow>();
        Error = $"无法搜索局域网：{message}";
        _retryAt = now + RetrySeconds;
    }
}
```

新建 `src/Tank.Client/scripts/Online/Logic/LanRoomRow.cs`：

```csharp
using System.Globalization;
using System.Net;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim.Modes;

namespace Tank.Client.Online;

/// <summary>
/// A battle room found on the LAN, ready for the menu's list: one line of text, and whether joining it can work.
/// <see cref="WhyNot"/> is set for a room that can't be joined, and also as a note for one that can but won't put the
/// player into the running match.
/// </summary>
public sealed record LanRoomRow(IPEndPoint EndPoint, string Text, bool Joinable, string? WhyNot)
{
    private const int NameColumns = 18, ModeColumns = 12, StateColumns = 7;

    public static LanRoomRow From(DiscoveredBattleRoom room)
    {
        RoomInfoMessage info = room.Info;
        bool versionOk = info.ProtocolVersion == BattleProtocol.Version;
        bool full = info.Players >= info.MaxPlayers;
        string state = !versionOk ? "版本不符" : info.MatchRunning ? "对战中" : "等待中";
        string text = string.Create(CultureInfo.InvariantCulture,
            $"{TextColumns.PadColumns(info.RoomName, NameColumns)} {TextColumns.PadColumns(ModeName(info.Mode), ModeColumns)} " +
            $"{info.Players}/{info.MaxPlayers}  {TextColumns.PadColumns(state, StateColumns)} {room.PingMs,3}ms   {room.EndPoint.Address}");
        (bool joinable, string? why) = (versionOk, full) switch
        {
            (false, _) => (false, $"版本不符：房间 v{info.ProtocolVersion}，本机 v{BattleProtocol.Version}"),
            (_, true) => (false, "房间已满"),
            _ when info.MatchRunning && !ModeRules.JoinsMidMatch(info.Mode) => (true, "对局进行中：进入房间后等下一局"),
            _ => (true, (string?)null),
        };
        return new LanRoomRow(room.EndPoint, text, joinable, why);
    }

    /// <summary>The mode's name for people.</summary>
    internal static string ModeName(BattleMode mode) => mode switch
    {
        BattleMode.TeamBattle => "团队对战",
        BattleMode.Annihilation => "团队歼灭",
        BattleMode.FreeForAll => "各自为战",
        _ => mode.ToString(),
    };
}
```

新建 `src/Tank.Client/scripts/Online/Logic/LoopbackHost.cs`：

```csharp
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;

namespace Tank.Client.Online;

/// <summary>
/// A room without sockets or threads: the server and its clients share a virtual <see cref="LoopbackNetwork"/>, and
/// <see cref="Advance"/> moves its clock and ticks the server every 1/30 s. The same input gives the same match, which
/// is what smoke runs and screenshots need. Mirrors the netcode tests' harness.
/// </summary>
internal sealed class LoopbackHost : IHostBackend
{
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    private const double TickSlack = 1e-9;   // a frame length of 1/60 summed twice must still make one 1/30 tick

    private readonly LoopbackNetwork _net;
    private readonly BattleServer _server;
    private double _accumulator;

    public LoopbackHost(HostSettings settings, uint ownerToken, Action<string> log)
    {
        _net = new LoopbackNetwork(settings.Seed ?? 1);
        _server = new BattleServer(_net.CreateServer(), new BattleServerOptions
        {
            RoomName = settings.RoomName,
            Port = settings.Port,
            Mode = settings.Mode,
            Bots = settings.Bots,
            OwnerToken = ownerToken,
            Seed = settings.Seed,
            MatchSeconds = settings.MatchSeconds,
        }, log);
    }

    public bool IsVirtualClock => true;

    public double Now => _net.Now;

    public (double AvgMs, double MaxMs)? ServerTick => null;

    public IClientTransport ConnectLocal() => _net.Connect();

    public void Advance(double dt)
    {
        _net.AdvanceTime(dt);
        _accumulator += dt;
        while (_accumulator >= ServerDt - TickSlack)
        {
            _server.Tick();
            _accumulator -= ServerDt;
        }
    }

    public void Dispose() { }
}
```

新建 `src/Tank.Client/scripts/Online/Logic/TextColumns.cs`：

```csharp
namespace Tank.Client.Online;

/// <summary>Column arithmetic for the monospace tables (room list, scoreboard); engine-neutral twin of <c>UiKit.Pad</c>.</summary>
public static class TextColumns
{
    /// <summary>How many monospace columns <paramref name="text"/> takes: CJK and full-width characters take two.</summary>
    public static int Width(string text)
    {
        int width = 0;
        foreach (char c in text) width += IsWide(c) ? 2 : 1;
        return width;
    }

    /// <summary>
    /// Right-pads <paramref name="text"/> to <paramref name="columns"/> columns. <see cref="string.PadRight(int)"/> alone would
    /// misalign rows that mix wide characters with ASCII. Text that is already wider is returned as it is.
    /// </summary>
    public static string PadColumns(string text, int columns)
    {
        int width = Width(text);
        return width >= columns ? text : text + new string(' ', columns - width);
    }

    private static bool IsWide(char c) =>
        c is >= 'ᄀ' and <= 'ᅟ' or >= '⺀' and <= '꓏' or >= '가' and <= '힣'
            or >= '豈' and <= '﫿' or >= '︰' and <= '﹏' or >= '＀' and <= '｠' or >= '￠' and <= '￦';
}
```

新建 `src/Tank.Client/scripts/Online/Logic/ThreadedHost.cs`：

```csharp
using System.Diagnostics;
using Tank.Net.Battle;
using Tank.Net.Server;
using Tank.Net.Transport;
using Tank.Sim;

namespace Tank.Client.Online;

/// <summary>
/// The listen server of a real room: a UDP transport and a <see cref="BattleServer"/> stepped at the simulation rate on
/// its own thread, so a hitch of the window (a drag, a shader compile) never stalls the other players. The host's client
/// connects to it over 127.0.0.1 like everyone else.
/// </summary>
internal sealed class ThreadedHost : IHostBackend
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly LiteNetServerTransport _transport;
    private readonly ServerRunner _runner;
    private readonly int _port;
    private bool _disposed;

    /// <summary>Binds the port and starts the server thread. Throws <see cref="InvalidOperationException"/> when the port is taken.</summary>
    public ThreadedHost(HostSettings settings, uint ownerToken, Action<string> log)
    {
        BattleServer? server = null;
        // Discovery replies come from the runner thread's Poll, after `server` is assigned below.
        var transport = new LiteNetServerTransport(settings.Port, null, BattleProtocol.MaxPlayers, BattleProtocol.ConnectKey,
            w => server!.WriteRoomInfo(w));
        try
        {
            transport.Start();
            _port = settings.Port == 0 ? transport.Manager.LocalPort : settings.Port;
            server = new BattleServer(transport, new BattleServerOptions
            {
                RoomName = settings.RoomName,
                Port = _port,
                Mode = settings.Mode,
                Bots = settings.Bots,
                OwnerToken = ownerToken,
                Seed = settings.Seed,
                MatchSeconds = settings.MatchSeconds,
            }, log);
            _runner = new ServerRunner(server, log, SimConstants.TickRate);
            _runner.Start();
        }
        catch
        {
            transport.Dispose();
            throw;
        }
        _transport = transport;
    }

    public bool IsVirtualClock => false;

    public double Now => _clock.Elapsed.TotalSeconds;

    public (double AvgMs, double MaxMs)? ServerTick => (_runner.AverageTickMs, _runner.MaxTickMs);

    public IClientTransport ConnectLocal() => new LiteNetClientTransport("127.0.0.1", _port, BattleProtocol.ConnectKey);

    public void Advance(double dt) { }

    /// <summary>Stops the thread first (up to 2 s), then closes the socket: closing it under a running poll would race.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runner.Dispose();
        _transport.Dispose();
    }
}
```

修改 `src/Tank.Net/Transport/LiteNetClientTransport.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Transport/LiteNetClientTransport.cs b/src/Tank.Net/Transport/LiteNetClientTransport.cs
index 1b5b755..721e237 100644
--- a/src/Tank.Net/Transport/LiteNetClientTransport.cs
+++ b/src/Tank.Net/Transport/LiteNetClientTransport.cs
@@ -33,7 +33,15 @@ public sealed class LiteNetClientTransport : IClientTransport
         _listener.NetworkReceiveEvent += (_, reader, _, _) => _handler?.OnData(0, reader.GetRemainingBytesSpan());
 
         if (!_manager.Start()) throw new InvalidOperationException("无法打开 UDP 套接字。");
-        _manager.Connect(host, port, connectKey);
+        try
+        {
+            _manager.Connect(host, port, connectKey);
+        }
+        catch
+        {
+            _manager.Stop();   // a bad address throws here: the started socket and its thread must not outlive the throw
+            throw;
+        }
     }
 
     public NetManager Manager => _manager;
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 596、Net 209（其中 3 条是 127.0.0.1 上真实 UDP 的主机测试）。本任务没有画面。

- [ ] **Step 5：提交**

```bash
git add -A src/Tank.Client src/Tank.Net tests/Tank.Art.Tests
git commit -m "feat(client): the online session layer — a threaded in-process host and a deterministic loopback host behind BattleSession, LAN room browsing, the auto-room state machine for launch options; LaunchOptions moves to BattleMode"
```

---

### Task 4：菜单、房间与 `Main` 重写

蓝图 §1.4、§5 T4。`UI/Logic/RoomRules.StartBlockReason`（镜像服务器的开局检查，测试里与真 `BattleServer` 的 21 组组合交叉校验）、`RoomModel`（相对阵营两栏：我方蓝灰 / 敌方红；各自为战单栏；机器人“开局时补入”占位行；对局进行中显示等待）、`ModeText`；`UI/RoomScreen`（车型四选一、换到对面、准备、开始及不能开始的原因、房主地址）；`MenuScreen` 重写（开房间、试驾场、局域网列表、手填地址、自定义端口提示）；`UiKit` 颜色改用 `Art.Palette`；`Main` + `Main.Flow`：菜单 → 连接 → 房间 → 对局占位 → 房间，`--host/--join/--sandbox` 启动，`AutoRoom` 每帧驱动，端口被占红字提示；`SandboxMain.ExitRequested`（Esc 第一次放鼠标，第二次回菜单）。房间界面只在房间记录或槽位变了才重建；名字框按线上限制截到 24 个 UTF-8 字节（`UI/Logic/NameLimit`，不拆代理对）；开房间 / 加入前先离开旧会话（重连等待期间点“开房间”不会撞上旧会话占着的端口）。

**Files:**
- Create: `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`、`UI/RoomScreen.cs`、`UI/Logic/{ModeText,NameLimit,RoomModel,RoomRules}.cs`
- Modify: `Bootstrap/Main.cs`、`Online/Logic/LanRoomRow.cs`、`Sandbox/SandboxMain.cs`、`UI/{MenuScreen,UiKit}.cs`
- Test: 新建 `tests/Tank.Art.Tests/{ModeTextTests,NameLimitTests,RoomModelTests,RoomRulesTests}.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/ModeTextTests.cs`：

```csharp
using Tank.Client.UI;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class ModeTextTests
{
    [Fact]
    public void Every_mode_has_a_name_and_a_rule_line()
    {
        foreach (BattleMode mode in ModeRules.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(ModeText.Name(mode)));
            Assert.NotEqual(mode.ToString(), ModeText.Name(mode));
            Assert.False(string.IsNullOrWhiteSpace(ModeText.Rule(mode)));
            Assert.DoesNotContain('\n', ModeText.Rule(mode));
        }
    }

    [Fact]
    public void The_three_modes_are_called_what_the_design_calls_them()
    {
        Assert.Equal("团队对战", ModeText.Name(BattleMode.TeamBattle));
        Assert.Equal("团队歼灭", ModeText.Name(BattleMode.Annihilation));
        Assert.Equal("各自为战", ModeText.Name(BattleMode.FreeForAll));
    }

    [Fact]
    public void The_rule_lines_carry_the_numbers_of_the_mode_rules()
    {
        Assert.Contains("10 秒后复活", ModeText.Rule(BattleMode.TeamBattle));
        Assert.Contains("7 分钟", ModeText.Rule(BattleMode.TeamBattle));
        Assert.Contains("7 分钟", ModeText.Rule(BattleMode.Annihilation));
        Assert.Contains("10 分钟", ModeText.Rule(BattleMode.FreeForAll));
    }

    [Fact]
    public void Every_catalog_tank_has_a_name_and_an_unknown_index_does_not_throw()
    {
        for (int i = 0; i < TankCatalog.All.Count; i++) Assert.Equal(TankCatalog.All[i].Name, ModeText.TankName(i));

        Assert.Equal("未知车型", ModeText.TankName(TankCatalog.All.Count));
        Assert.Equal("未知车型", ModeText.TankName(-1));
    }
}
```

新建 `tests/Tank.Art.Tests/NameLimitTests.cs`：

```csharp
using System.Text;
using Tank.Client.UI;
using Tank.Net.Battle;

namespace Tank.Art.Tests;

public class NameLimitTests
{
    [Theory]
    [InlineData("")]
    [InlineData("阿成")]
    [InlineData("Sergeant_Wolfgang_Becker")]   // 24 ASCII bytes: exactly the limit
    [InlineData("一二三四五六七八")]               // 8 × 3 bytes: exactly the limit
    public void A_name_within_the_limit_is_kept_as_it_is(string name)
    {
        Assert.Equal(name, NameLimit.Clip(name));
    }

    [Fact]
    public void Ten_han_characters_are_cut_to_the_eight_that_fit_in_24_bytes()
    {
        Assert.Equal("一二三四五六七八", NameLimit.Clip("一二三四五六七八九十"));
    }

    [Fact]
    public void A_long_ascii_name_is_cut_to_24_characters()
    {
        Assert.Equal(new string('x', BattleProtocol.MaxNameBytes), NameLimit.Clip(new string('x', 30)));
    }

    [Fact]
    public void A_character_that_would_straddle_the_limit_is_left_out_whole()
    {
        // 22 ASCII bytes leave 2: a 3-byte 汉字 doesn't fit, so the cut falls before it, not inside it.
        string name = new string('a', 22) + "坦克";
        Assert.Equal(new string('a', 22), NameLimit.Clip(name));
    }

    [Fact]
    public void A_surrogate_pair_is_never_split()
    {
        string name = new string('a', 21) + "🚀b";   // the rocket is 4 bytes: 21 + 4 > 24
        string clipped = NameLimit.Clip(name);
        Assert.Equal(new string('a', 21), clipped);
        Assert.True(Encoding.UTF8.GetByteCount(clipped) <= BattleProtocol.MaxNameBytes);
    }
}
```

新建 `tests/Tank.Art.Tests/RoomModelTests.cs`：

```csharp
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class RoomModelTests
{
    private static RoomPlayer P(byte slot, string name, byte team, byte tank = 1, bool ready = false, bool connected = true, bool bot = false) =>
        new(slot, name, team, tank, ready, connected, bot);

    private static RoomStateMessage Room(BattleMode mode, byte host, int bots, bool running, params RoomPlayer[] players) =>
        new(mode, host, (byte)bots, running, players);

    [Fact]
    public void Team_columns_are_mine_and_theirs_whatever_the_team_number()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false,
            P(0, "Ann", 0), P(1, "Bo", 1), P(2, "Cy", 1), P(3, "Di", 0));

        RoomModel fromBo = RoomModel.Build(room, 1);
        RoomModel fromAnn = RoomModel.Build(room, 0);

        Assert.Equal(new[] { "Bo", "Cy" }, fromBo.Columns[0].Rows.Select(r => r.Name));
        Assert.Equal(new[] { "Ann", "Di" }, fromBo.Columns[1].Rows.Select(r => r.Name));
        Assert.Equal(ColumnSide.Mine, fromBo.Columns[0].Side);
        Assert.Equal(ColumnSide.Other, fromBo.Columns[1].Side);
        Assert.StartsWith("我方", fromBo.Columns[0].Title);
        Assert.StartsWith("敌方", fromBo.Columns[1].Title);
        Assert.Equal(new[] { "Ann", "Di" }, fromAnn.Columns[0].Rows.Select(r => r.Name));
        Assert.Equal((byte)0, fromBo.OtherTeam);
        Assert.Equal((byte)1, fromAnn.OtherTeam);
    }

    [Fact]
    public void Free_for_all_is_one_column_without_a_team_switch()
    {
        RoomStateMessage room = Room(BattleMode.FreeForAll, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1), P(2, "Cy", 2));

        RoomModel model = RoomModel.Build(room, 2);

        RoomColumnView only = Assert.Single(model.Columns);
        Assert.Equal(ColumnSide.Everyone, only.Side);
        Assert.Equal(3, only.Rows.Count);
        Assert.False(model.HasTeams);
        Assert.False(model.CanSwitchTeam);
    }

    [Fact]
    public void Only_the_host_sets_the_mode_and_bots_and_starts()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1));

        RoomModel host = RoomModel.Build(room, 0);
        RoomModel guest = RoomModel.Build(room, 1);

        Assert.True(host.CanSetMode && host.CanSetBots && host.ShowStart && host.CanStart);
        Assert.False(guest.CanSetMode || guest.CanSetBots || guest.ShowStart || guest.CanStart);
        Assert.True(guest.CanSwitchTeam);
        Assert.Equal("", guest.StartHint);
    }

    [Fact]
    public void The_start_button_says_why_it_is_disabled()
    {
        RoomStateMessage alone = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0));

        RoomModel model = RoomModel.Build(alone, 0);

        Assert.False(model.CanStart);
        Assert.Equal(RoomRules.StartBlockReason(alone), model.StartHint);
    }

    [Fact]
    public void With_a_match_running_the_room_waits_for_the_next_one()
    {
        RoomStateMessage room = Room(BattleMode.Annihilation, 0, 0, true, P(0, "Ann", 0), P(1, "Bo", 1));

        RoomModel host = RoomModel.Build(room, 0);

        Assert.Equal("对局进行中，等下一局", host.WaitingText);
        Assert.False(host.CanStart);
        Assert.False(host.CanSetMode);
        Assert.False(host.CanSetBots);
        Assert.Null(RoomModel.Build(Room(BattleMode.Annihilation, 0, 0, false, P(0, "Ann", 0)), 0).WaitingText);
    }

    [Fact]
    public void Rows_carry_name_tags_tank_and_state()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false,
            P(0, "Ann", 0, tank: 2, ready: true), P(1, "Bo", 0, connected: false), P(2, "Cy", 0), P(3, "Bot", 0, bot: true));

        IReadOnlyList<RoomRowView> rows = RoomModel.Build(room, 1).Columns[0].Rows;

        RoomRowView ann = rows.Single(r => r.Slot == 0);
        Assert.Equal("房主", ann.Tags);
        Assert.Equal("重型坦克", ann.TankName);
        Assert.Equal("已准备", ann.State);
        Assert.Equal(RowTone.Good, ann.Tone);
        RoomRowView bo = rows.Single(r => r.Slot == 1);
        Assert.Equal("你", bo.Tags);
        Assert.True(bo.IsMe);
        Assert.Equal("重连中", bo.State);
        Assert.Equal("未准备", rows.Single(r => r.Slot == 2).State);
        Assert.True(rows.Single(r => r.Slot == 3).IsBot);
        Assert.Equal("房主 你", RoomModel.Build(room, 0).Columns[0].Rows.Single(r => r.Slot == 0).Tags);
    }

    [Fact]
    public void Bots_to_be_added_show_as_a_placeholder_row_in_the_team_they_will_fill()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 2, false, P(0, "Ann", 0), P(1, "Bo", 0));

        RoomModel model = RoomModel.Build(room, 0);

        Assert.Equal(2, model.Columns[0].Rows.Count);   // my team: the two people
        Assert.Equal("我方 · 2", model.Columns[0].Title);
        Assert.Equal("敌方 · 2", model.Columns[1].Title);   // the titles count tanks, the bots to come included
        RoomRowView ghost = Assert.Single(model.Columns[1].Rows);
        Assert.True(ghost.Ghost);
        Assert.Contains("× 2", ghost.Name);
        Assert.True(model.CanStart);
    }

    [Fact]
    public void The_signature_changes_with_what_is_on_screen_and_not_otherwise()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1));
        RoomStateMessage same = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0), P(1, "Bo", 1));
        RoomStateMessage readied = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0, ready: true), P(1, "Bo", 1));

        Assert.Equal(RoomModel.Build(room, 0).Signature, RoomModel.Build(same, 0).Signature);
        Assert.NotEqual(RoomModel.Build(room, 0).Signature, RoomModel.Build(readied, 0).Signature);
        Assert.NotEqual(RoomModel.Build(room, 0).Signature, RoomModel.Build(room, 1).Signature);
    }

    [Fact]
    public void The_ready_button_toggles_and_the_tank_choice_is_the_players_own()
    {
        RoomStateMessage room = Room(BattleMode.TeamBattle, 0, 0, false, P(0, "Ann", 0, tank: 3, ready: true), P(1, "Bo", 1, tank: 0));

        RoomModel ann = RoomModel.Build(room, 0);
        RoomModel bo = RoomModel.Build(room, 1);

        Assert.True(ann.MyReady);
        Assert.Equal("取消准备", ann.ReadyLabel);
        Assert.Equal(3, ann.MyTankIndex);
        Assert.Equal("准备", bo.ReadyLabel);
        Assert.Equal(0, bo.MyTankIndex);
    }
}
```

新建 `tests/Tank.Art.Tests/RoomRulesTests.cs`：

```csharp
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

/// <summary>
/// <see cref="RoomRules.StartBlockReason"/> is a mirror of the server's start checks, so it is tested against the server:
/// a real <see cref="BattleServer"/> on a virtual network is given each room, the host asks to start, and the room
/// state the host saw just before must predict whether the match began.
/// </summary>
public class RoomRulesTests
{
    private const double Frame = 1.0 / 60;
    private const double ServerDt = 1.0 / SimConstants.TickRate;

    /// <summary>One server, its clients, and the stepping the game loop does; the first client is the host.</summary>
    private sealed class Room
    {
        private readonly LoopbackNetwork _net = new(1);
        private readonly List<BattleClient> _clients = new();
        private double _accumulator;

        public Room(int humans)
        {
            Server = new BattleServer(_net.CreateServer(), new BattleServerOptions { Seed = 5 });
            for (int i = 0; i < humans; i++) _clients.Add(new BattleClient(_net.Connect(), $"P{i}", () => _net.Now));
            Run(0.5);
        }

        public BattleServer Server { get; }
        public BattleClient Host => _clients[0];
        public IReadOnlyList<BattleClient> Clients => _clients;

        public void Run(double seconds)
        {
            for (int f = 0; f < (int)Math.Round(seconds / Frame); f++)
            {
                _net.AdvanceTime(Frame);
                _accumulator += Frame;
                while (_accumulator >= ServerDt - 1e-9)
                {
                    Server.Tick();
                    _accumulator -= ServerDt;
                }
                foreach (BattleClient c in _clients) c.Update(Frame, LocalCommand.None);
            }
        }
    }

    /// <summary>Builds the room, lets the rules predict, then starts it for real; returns (predicted, actual, the reason).</summary>
    private static (bool Predicted, bool Actual, string? Reason) Try(BattleMode mode, int humans, int bots, byte[]? teams)
    {
        var room = new Room(humans);
        room.Host.SetMode(mode);
        room.Host.SetBots(bots);
        if (teams is not null)
            for (int i = 0; i < humans; i++) room.Clients[i].SetTeam(teams[i]);
        room.Run(0.5);

        RoomStateMessage state = room.Host.Room!;
        Assert.Equal(mode, state.Mode);
        Assert.Equal(bots, state.Bots);
        Assert.Equal(humans, state.Players.Count);
        string? reason = RoomRules.StartBlockReason(state);

        room.Host.StartMatch();
        room.Run(0.5);
        Assert.Equal(room.Server.MatchRunning, room.Host.Room!.MatchRunning);
        return (reason is null, room.Server.MatchRunning, reason);
    }

    public static IEnumerable<object[]> Combinations()
    {
        byte[] split = { 0, 1 };
        byte[] together = { 0, 0 };
        foreach (BattleMode mode in ModeRules.All)
        {
            yield return new object[] { mode, 1, 0, null! };
            yield return new object[] { mode, 1, 2, null! };
            yield return new object[] { mode, 2, 0, split };
            yield return new object[] { mode, 2, 2, split };
            yield return new object[] { mode, 2, 0, together };
            yield return new object[] { mode, 2, 1, together };
        }
        yield return new object[] { BattleMode.TeamBattle, 1, 1, null! };
        yield return new object[] { BattleMode.FreeForAll, 1, 1, null! };
        yield return new object[] { BattleMode.TeamBattle, 2, 9, split };
    }

    [Theory]
    [MemberData(nameof(Combinations))]
    public void The_rules_agree_with_the_server_on_whether_the_match_starts(BattleMode mode, int humans, int bots, byte[]? teams)
    {
        (bool predicted, bool actual, string? reason) = Try(mode, humans, bots, teams);

        Assert.True(predicted == actual, $"rules said {(predicted ? "can start" : $"blocked ({reason})")}, the server {(actual ? "started" : "refused")}");
    }

    [Fact]
    public void One_person_alone_cannot_start_and_the_reason_says_two_tanks_are_needed()
    {
        var (predicted, actual, reason) = Try(BattleMode.TeamBattle, 1, 0, null);

        Assert.False(predicted);
        Assert.False(actual);
        Assert.Contains("两辆坦克", reason);
    }

    [Fact]
    public void Two_people_on_one_team_leave_the_other_empty_until_a_bot_fills_it()
    {
        var blocked = Try(BattleMode.TeamBattle, 2, 0, new byte[] { 0, 0 });
        var filled = Try(BattleMode.TeamBattle, 2, 1, new byte[] { 0, 0 });

        Assert.False(blocked.Actual);
        Assert.Contains("一队没有坦克", blocked.Reason);
        Assert.True(filled.Actual);
        Assert.Null(filled.Reason);
    }

    [Fact]
    public void Without_teams_two_people_start_whatever_their_team_field_says()
    {
        var (predicted, actual, _) = Try(BattleMode.FreeForAll, 2, 0, new byte[] { 0, 0 });

        Assert.True(predicted);
        Assert.True(actual);
    }

    [Fact]
    public void Bots_fill_the_thinner_team_first()
    {
        // Two people on team 0 and four bots: the bots fill team 1 until it matches, then alternate (team 0 on a tie).
        var room = new Room(2);
        room.Host.SetBots(4);
        room.Clients[0].SetTeam(0);
        room.Clients[1].SetTeam(0);
        room.Run(0.5);

        StartPlan plan = RoomRules.PlanFor(room.Host.Room!);

        Assert.Equal(new StartPlan(2, 4, 3, 3, 1, 3), plan);
        room.Host.StartMatch();
        room.Run(0.5);
        Assert.True(room.Server.MatchRunning);
        var seated = Enumerable.Range(0, BattleProtocol.MaxPlayers).Select(i => room.Server.Rules!.Of((byte)i)).Where(p => p.Active).ToList();
        Assert.Equal(plan.Team0, seated.Count(p => p.Team == 0));
        Assert.Equal(plan.Team1, seated.Count(p => p.Team == 1));
    }

    [Fact]
    public void A_running_match_blocks_the_start()
    {
        var room = new Room(2);
        room.Host.StartMatch();
        room.Run(0.5);
        Assert.True(room.Server.MatchRunning);

        Assert.NotNull(RoomRules.StartBlockReason(room.Host.Room!));
    }

    [Fact]
    public void Bots_are_capped_by_the_free_places()
    {
        var state = new RoomStateMessage(BattleMode.FreeForAll, 0, 9, false,
            Enumerable.Range(0, 4).Select(i => new RoomPlayer((byte)i, $"P{i}", (byte)i, 0, false, true, false)).ToList());

        StartPlan plan = RoomRules.PlanFor(state);

        Assert.Equal(6, plan.Bots);
        Assert.Equal(10, plan.Seated);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，命名空间 `Tank.Client.UI` 找不到（`RoomRules`、`RoomModel`、`ModeText`、`NameLimit`）。

- [ ] **Step 3：实现**

新建 `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`：

```csharp
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Godot;
using Tank.Client.Online;
using Tank.Client.Sandbox;
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Bootstrap;

/// <summary>Main's transitions: hosting and joining, leaving, entering and leaving the match, and the sandbox.</summary>
public partial class Main
{
    private const string AppTitle = "坦克 3D";

    private void Host(string name)
    {
        LeaveOld();
        var settings = new HostSettings(name, $"{name}的房间", _options.Port, _options.Mode ?? BattleMode.TeamBattle, _options.Bots, _options.Seed, _options.MatchSeconds);
        try
        {
            BattleSession session = _options.Loopback
                ? BattleSession.HostLoopback(settings, ServerLog)
                : BattleSession.Host(settings, ServerLog);
            StartSession(session, "正在启动房间…");
        }
        catch (InvalidOperationException ex)   // the port is taken (a second instance on this machine), or no socket
        {
            _menu.SetStatus(ex.Message, error: true);
        }
    }

    private void JoinAddress(string address)
    {
        int port = _options.Port;
        int colon = address.LastIndexOf(':');
        if (colon > 0 && int.TryParse(address[(colon + 1)..], out int p))
        {
            port = p;
            address = address[..colon];
        }
        Join(_menu.PlayerName, address, port);
    }

    private void Join(string name, string host, int port)
    {
        LeaveOld();
        try
        {
            StartSession(BattleSession.Join(host, port, name), $"正在连接 {host}:{port}…");
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException or ArgumentException)
        {
            _menu.SetStatus($"无法连接：{ex.Message}", error: true);
        }
    }

    /// <summary>
    /// Before a new session is built, the old one goes, and the LAN search's socket with it: the menu is still up while a
    /// session is on its way in, and a hosting one holds the port a new host would bind ("port busy").
    /// </summary>
    private void LeaveOld()
    {
        LeaveSession(null);
        StopLan();
    }

    private void StartSession(BattleSession session, string status)
    {
        _session = session;
        // --bots is only a request when given; 0 is also the default, and the host's own settings already carry it.
        _auto = new AutoRoom(_options.Tank, _options.Mode, _options.Bots > 0 ? _options.Bots : null, _options.AutoStart);
        _room.Bind(session.Client, LocalAddresses(session));
        _menu.SetStatus(status);
    }

    /// <summary>Drops the session (the client, then the server thread and socket) and shows the menu with <paramref name="message"/>.</summary>
    private void LeaveSession(string? message, bool error = false)
    {
        if (_matchView is not null) LeaveMatch();
        _session?.Dispose();
        _session = null;
        _auto = null;
        _room.Visible = false;
        _menu.Visible = true;
        if (message is not null) _menu.SetStatus(message, error);
    }

    private void ShowRoom(BattleClient client)
    {
        if (!_room.Visible)
        {
            _menu.Visible = false;
            _room.Visible = true;
        }
        _auto?.Next(client.Room, client.Slot, client.IsHost, _options.Players).SendTo(client);
        _room.Refresh();
    }

    /// <summary>The match began. T5 puts the match scene here; for now a dark placeholder and one log line.</summary>
    private void EnterMatch(BattleClient client)
    {
        _menu.Visible = false;
        _room.Visible = false;
        string tank = client.HasTank ? client.Own.Spec.Id : "?";
        GD.Print($"[match] entered match {client.Room?.Mode} as slot {client.Slot}, tank {tank}");
        _matchView = BuildMatchPlaceholder();
    }

    /// <summary>The match is over (or the session ended): its scene goes, and the launch options may start the next one.</summary>
    private void LeaveMatch()
    {
        _matchView?.QueueFree();
        _matchView = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _auto?.Reset();
    }

    /// <summary>A dark full-window label standing in for the match scene; already added to the tree.</summary>
    private Control BuildMatchPlaceholder()
    {
        var view = new Control { Theme = UiKit.Theme };
        AddChild(view);   // in the tree first: FillViewport reads the viewport
        UiKit.FillViewport(view);
        var shade = new ColorRect { Color = UiKit.Background, MouseFilter = Control.MouseFilterEnum.Ignore };
        view.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label label = UiKit.Label("对局画面（T5）", 48, UiKit.Dim, HorizontalAlignment.Center);
        label.VerticalAlignment = VerticalAlignment.Center;
        view.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return view;
    }

    private void EnterSandbox()
    {
        if (_sandbox is not null) return;
        StopLan();
        LeaveSession(null);
        _menu.Visible = false;
        _sandbox = new SandboxMain();
        _sandbox.ExitRequested += ExitSandbox;
        AddChild(_sandbox);
    }

    private void ExitSandbox()
    {
        _sandbox?.QueueFree();
        _sandbox = null;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetWindow().Title = AppTitle;
        _menu.Visible = true;
        _menu.SetStatus("");
    }

    private void StopLan()
    {
        _lan?.Dispose();
        _lan = null;
    }

    private static void ServerLog(string message) => GD.Print($"[server] {message}");

    private string LocalAddresses(BattleSession session)
    {
        if (!session.IsHosting) return "";
        string suffix = _options.Port == Net.Protocol.ProtocolInfo.DefaultPort ? "" : $":{_options.Port}";
        return string.Join(", ", LocalIPv4().Select(a => a + suffix));
    }

    /// <summary>Turns a transport disconnect code into player-facing text; server reject reasons pass through as-is.</summary>
    private static string DescribeDisconnect(string reason) => reason switch
    {
        "ConnectionFailed" => "无法连接到房主。",
        "Timeout" => "连接超时。",
        "HostUnreachable" or "NetworkUnreachable" or "UnknownHost" => "找不到房主，请检查地址和网络。",
        "RemoteConnectionClose" => "房主关闭了房间。",
        "DisconnectPeerCalled" => "已断开。",
        "ConnectionRejected" => "房主拒绝了连接。",
        "InvalidProtocol" => "协议不兼容。",
        _ => reason,
    };

    private static IEnumerable<string> LocalIPv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(a => a.Address.ToString())
                .Distinct()
                .ToList();
        }
        catch (NetworkInformationException)
        {
            return Array.Empty<string>();
        }
    }
}
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.cs b/src/Tank.Client/scripts/Bootstrap/Main.cs
index 5bd29d2..e867636 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.cs
@@ -1,283 +1,125 @@
-using System.Net.NetworkInformation;
-using System.Net.Sockets;
 using Godot;
-using Tank.Client.Controls;
+using Tank.Client.Online;
+using Tank.Client.Sandbox;
 using Tank.Client.UI;
-using Tank.Client.View;
-using Tank.Core.World;
-using Tank.Net.Client;
-using Tank.Net.Transport;
+using Tank.Net.Battle;
 
 namespace Tank.Client.Bootstrap;
 
-/// <summary>Root node: screen flow (menu → lobby → match), per-frame client update, bots and automation.</summary>
+/// <summary>
+/// Root node and screen flow: menu → connecting → room → match → room, plus the sandbox as a child. One
+/// <see cref="BattleSession"/> lives from "host" or "join" until leaving or a disconnect; the launch options drive the room
+/// through <see cref="AutoRoom"/>. The flow itself (start, leave, enter and leave the match) is in Main.Flow.cs, the
+/// update banner in Main.Update.cs.
+/// </summary>
 public partial class Main : Node
 {
-    private readonly Session _session = new();
-    private readonly InputSampler _input = new();
-    private readonly List<(GameClient Client, int Index)> _bots = new();
+    private const double VirtualFrame = 1.0 / 60;   // a loopback host steps by this, not by the frame time
+
     private LaunchOptions _options = new();
     private MenuScreen _menu = null!;
-    private LobbyScreen _lobby = null!;
-    private GameView? _game;
-    private Hud? _hud;
-    private LanDiscovery? _discovery;
-    private ServerStatsSource? _serverStats;
+    private RoomScreen _room = null!;
+    private LanBrowser? _lan;
+    private BattleSession? _session;
+    private AutoRoom? _auto;
+    private SandboxMain? _sandbox;
+    private Control? _matchView;
     private double _time;
-    private bool _netGraph;
-    private bool _autoStartSent;
     private bool _screenshotTaken;
-    private double _nextStatsLog = 1;
 
     public override void _Ready()
     {
         _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
         foreach (string warning in _options.Warnings) GD.PushWarning(warning);
-        _netGraph = _options.NetGraph;
-        GetWindow().Title = "坦克 3D";
+        GetWindow().Title = AppTitle;
         _menu = new MenuScreen();
-        _menu.HostRequested += (name, mode) => Host(name, mode);
-        _menu.JoinRequested += (name, host, port) => Join(name, host, port);
+        _menu.HostRequested += Host;
+        _menu.JoinRequested += Join;
+        _menu.SandboxRequested += EnterSandbox;
         AddChild(_menu);
         _menu.PlayerName = _options.Name ?? System.Environment.UserName;
 
-        _lobby = new LobbyScreen { Visible = false, LocalAddresses = string.Join(", ", LocalIPv4()) };
-        _lobby.LeaveRequested += () => LeaveToMenu("已离开房间。");
-        AddChild(_lobby);
+        _room = new RoomScreen { Visible = false };
+        _room.LeaveRequested += () => LeaveSession("已离开房间。");
+        AddChild(_room);
 
-        if (_options.Host) Host(_menu.PlayerName, GameMode.Coop);   // T4 rewrites this flow for the battle modes
+        if (_options.Sandbox) EnterSandbox();
+        else if (_options.Host) Host(_menu.PlayerName);
         else if (_options.JoinAddress is { } address) JoinAddress(address);
         InitUpdates();
     }
 
-    public override void _Input(InputEvent e) => _input.OnInputEvent(e);
-
     public override void _UnhandledInput(InputEvent e)
     {
-        if (e.IsActionPressed("toggle_netgraph")) _netGraph = !_netGraph;
-        if (e.IsActionPressed("toggle_camera")) _game?.Rig.Toggle();
-        if (e.IsActionPressed("ui_cancel") && _session.Client is not null) LeaveToMenu("已离开房间。");
+        // In the room Esc leaves it; the match's own Esc handling comes with the match scene.
+        if (e.IsActionPressed("ui_cancel") && _session is not null && _matchView is null) LeaveSession("已离开房间。");
     }
 
     public override void _Process(double delta)
     {
         _time += delta;
-        var client = _session.Client;
-        if (client is null)
-        {
-            PollDiscovery();
-            PollUpdates();
-        }
-        else
+        if (_sandbox is null)
         {
-            var input = client.Phase == ClientPhase.InMatch ? _input.Sample() : LocalInput.None;
-            client.Update(delta, input);
-            foreach (var (bot, index) in _bots) bot.Update(delta, BotDriver.Think(index, _time));
-            UpdateScreens(client, delta);
+            if (_session is null)
+            {
+                PollLan();
+                PollUpdates();
+            }
+            else RunSession(delta);
         }
         RunAutomation();
     }
 
     public override void _ExitTree()
     {
-        foreach (var (bot, _) in _bots) bot.Dispose();
-        _discovery?.Dispose();
+        _lan?.Dispose();
         _update?.Dispose();
-        _session.Dispose();
+        _session?.Dispose();
     }
 
-    private void UpdateScreens(GameClient client, double delta)
+    /// <summary>One frame with a session: the host's world, the room's launch options, the client, then whichever screen the phase calls for.</summary>
+    private void RunSession(double delta)
     {
+        BattleSession session = _session!;
+        BattleClient client = session.Client;
+        double dt = session.IsVirtualClock ? VirtualFrame : delta;
+        session.Advance(dt);
+        client.Update(dt, LocalCommand.None);   // T5: the match scene takes over Advance and Update while it exists
         switch (client.Phase)
         {
-            case ClientPhase.Connecting:
-            case ClientPhase.Lobby:
-                ShowLobby(client);
-                if (_options.AutoStart && !_autoStartSent && client.IsHost && client.Lobby?.Players.Count >= 1 + _bots.Count)
-                {
-                    _autoStartSent = true;
-                    client.StartMatch();
-                }
+            case BattlePhase.Connecting:
                 break;
-            case ClientPhase.InMatch:
-                ShowGame();
-                _game!.Sync(client, (float)delta, _time);
-                _hud!.NetGraphVisible = _netGraph;
-                _hud.Refresh(client, delta, Godot.Input.IsActionPressed("scoreboard"), _serverStats);
-                if (_options.NetGraph && _time >= _nextStatsLog) LogStats(client);
+            case BattlePhase.Room:
+                if (_matchView is not null) LeaveMatch();
+                ShowRoom(client);
                 break;
-            case ClientPhase.Disconnected:
-                LeaveToMenu(client.DisconnectReason is { } r ? $"连接已断开：{DescribeDisconnect(r)}" : "连接已断开。", error: true);
+            case BattlePhase.InMatch:
+                if (_matchView is null && client.HasTank) EnterMatch(client);   // the match start has set up the own tank by now
+                break;
+            case BattlePhase.Disconnected:
+                LeaveSession(client.DisconnectReason is { } reason ? $"连接已断开：{DescribeDisconnect(reason)}" : "连接已断开。", error: true);
                 break;
         }
     }
 
-    private void Host(string name, GameMode mode)
-    {
-        try
-        {
-            _session.Host(name, $"{name}的房间", _options.Port, mode);
-            var runner = _session.Runner!;
-            _serverStats = new ServerStatsSource(() => (runner.AverageTickMs, runner.MaxTickMs));
-            StopDiscovery();
-            for (int i = 0; i < _options.Bots; i++)
-            {
-                var bot = new GameClient(new LiteNetClientTransport("127.0.0.1", _options.Port), $"机器人 {i + 1}", () => _session.Now);
-                _bots.Add((bot, i));
-            }
-            _menu.SetStatus("正在启动服务器…");
-        }
-        catch (InvalidOperationException ex)
-        {
-            _session.Leave();
-            _menu.SetStatus(ex.Message, error: true);
-        }
-    }
-
-    private void JoinAddress(string address)
-    {
-        int port = _options.Port;
-        int colon = address.LastIndexOf(':');
-        if (colon > 0 && int.TryParse(address[(colon + 1)..], out int p))
-        {
-            port = p;
-            address = address[..colon];
-        }
-        Join(_menu.PlayerName, address, port);
-    }
-
-    private void Join(string name, string host, int port)
-    {
-        StopDiscovery();
-        _session.Join(host, port, name);
-        _menu.SetStatus($"正在连接 {host}:{port}…");
-    }
-
-    private void LeaveToMenu(string message, bool error = false)
-    {
-        foreach (var (bot, _) in _bots) bot.Dispose();
-        _bots.Clear();
-        _session.Leave();
-        _serverStats = null;
-        _autoStartSent = false;
-        HideGame();
-        _lobby.Visible = false;
-        _menu.Visible = true;
-        _menu.SetStatus(message, error);
-    }
-
-    private void ShowLobby(GameClient client)
-    {
-        _menu.Visible = false;
-        EnsureGameView();
-        _hud!.Visible = false;
-        if (client.Lobby is { } lobby) _game!.ShowPreview(lobby.MapName, (float)GetProcessDeltaTime(), _time);
-        if (!_lobby.Visible)
-        {
-            _lobby.Bind(client);
-            _lobby.Visible = true;
-        }
-        _lobby.Refresh();
-    }
-
-    private void ShowGame()
-    {
-        _menu.Visible = false;
-        _lobby.Visible = false;
-        EnsureGameView();
-        _hud!.Visible = true;
-    }
-
-    private void EnsureGameView()
-    {
-        if (_game is not null) return;
-        _game = new GameView();
-        AddChild(_game);
-        MoveChild(_game, 0); // draw the 3D world behind the UI screens
-        _hud = new Hud();
-        AddChild(_hud);
-    }
-
-    private void HideGame()
-    {
-        _game?.QueueFree();
-        _game = null;
-        _hud?.QueueFree();
-        _hud = null;
-    }
-
-    private void PollDiscovery()
+    private void PollLan()
     {
         if (!_menu.Visible) return;
-        try
-        {
-            _discovery ??= new LanDiscovery();
-            _discovery.Poll();
-            _menu.ShowRooms(_discovery.Rooms);
-        }
-        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
-        {
-            _menu.SetStatus($"局域网搜索不可用（{ex.Message}），仍可通过地址加入。", error: true);
-            _discovery?.Dispose();
-            _discovery = null;
-        }
-    }
-
-    private void StopDiscovery()
-    {
-        _discovery?.Dispose();
-        _discovery = null;
+        _lan ??= new LanBrowser();
+        _lan.Poll();
+        _menu.ShowRooms(_lan.Rows, _lan.Error);
     }
 
     private void RunAutomation()
     {
-        if (_options.ScreenshotPath is { } path && !_screenshotTaken && _time >= _options.ScreenshotAt)
+        // A sandbox started from here runs its own screenshot schedule.
+        if (_sandbox is null && _options.ScreenshotPath is { } path && !_screenshotTaken && _time >= _options.ScreenshotAt)
         {
             _screenshotTaken = true;
-            var image = GetViewport().GetTexture().GetImage();
-            var err = image.SavePng(path);
+            Image image = GetViewport().GetTexture().GetImage();
+            Error err = image.SavePng(path);
             GD.Print(err == Error.Ok ? $"screenshot saved: {path}" : $"screenshot failed: {err}");
         }
         if (_options.QuitAfter > 0 && _time >= _options.QuitAfter) GetTree().Quit();
     }
-
-    private void LogStats(GameClient client)
-    {
-        _nextStatsLog = _time + 1;
-        var s = client.Stats;
-        GD.Print($"[net] t={_time:F1} fps={Engine.GetFramesPerSecond()} rtt={s.RttMs} jitter={s.JitterMs:F1} interp={s.InterpDelayMs:F0} " +
-                 $"slack={s.InputSlackTicks:F2} rate={s.ClockRate:F3} snaps={s.SnapshotsPerSecond} corr={s.Mispredictions} " +
-                 $"extrap={s.Extrapolations} down={s.BytesInPerSecond / 1024:F1}KB/s");
-    }
-
-    /// <summary>Turns a transport disconnect code into player-facing text; server reject reasons pass through as-is.</summary>
-    private static string DescribeDisconnect(string reason) => reason switch
-    {
-        "ConnectionFailed" => "无法连接到房主。",
-        "Timeout" => "连接超时。",
-        "HostUnreachable" or "NetworkUnreachable" or "UnknownHost" => "找不到房主，请检查地址和网络。",
-        "RemoteConnectionClose" => "房主关闭了房间。",
-        "DisconnectPeerCalled" => "已断开。",
-        "ConnectionRejected" => "房主拒绝了连接。",
-        "InvalidProtocol" => "协议不兼容。",
-        _ => reason,
-    };
-
-    private static IEnumerable<string> LocalIPv4()
-    {
-        try
-        {
-            return NetworkInterface.GetAllNetworkInterfaces()
-                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
-                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
-                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
-                .Select(a => a.Address.ToString())
-                .Distinct()
-                .ToList();
-        }
-        catch (NetworkInformationException)
-        {
-            return Array.Empty<string>();
-        }
-    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Online/Logic/LanRoomRow.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Online/Logic/LanRoomRow.cs b/src/Tank.Client/scripts/Online/Logic/LanRoomRow.cs
index 3a02fc8..700ab13 100644
--- a/src/Tank.Client/scripts/Online/Logic/LanRoomRow.cs
+++ b/src/Tank.Client/scripts/Online/Logic/LanRoomRow.cs
@@ -1,6 +1,7 @@
 using System.Globalization;
 using System.Net;
 using Tank.Net.Battle;
+using Tank.Client.UI;
 using Tank.Net.Transport;
 using Tank.Sim.Modes;
 
@@ -22,7 +23,7 @@ public sealed record LanRoomRow(IPEndPoint EndPoint, string Text, bool Joinable,
         bool full = info.Players >= info.MaxPlayers;
         string state = !versionOk ? "版本不符" : info.MatchRunning ? "对战中" : "等待中";
         string text = string.Create(CultureInfo.InvariantCulture,
-            $"{TextColumns.PadColumns(info.RoomName, NameColumns)} {TextColumns.PadColumns(ModeName(info.Mode), ModeColumns)} " +
+            $"{TextColumns.PadColumns(info.RoomName, NameColumns)} {TextColumns.PadColumns(ModeText.Name(info.Mode), ModeColumns)} " +
             $"{info.Players}/{info.MaxPlayers}  {TextColumns.PadColumns(state, StateColumns)} {room.PingMs,3}ms   {room.EndPoint.Address}");
         (bool joinable, string? why) = (versionOk, full) switch
         {
@@ -33,13 +34,4 @@ public sealed record LanRoomRow(IPEndPoint EndPoint, string Text, bool Joinable,
         };
         return new LanRoomRow(room.EndPoint, text, joinable, why);
     }
-
-    /// <summary>The mode's name for people.</summary>
-    internal static string ModeName(BattleMode mode) => mode switch
-    {
-        BattleMode.TeamBattle => "团队对战",
-        BattleMode.Annihilation => "团队歼灭",
-        BattleMode.FreeForAll => "各自为战",
-        _ => mode.ToString(),
-    };
 }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index da33e8c..ca354a4 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -36,6 +36,10 @@ public partial class SandboxMain : Node3D
     private const float GroundBurstHeight = 0.5f;   // a shell bursting higher than this over the ground hit a rock or a tank
 
     private readonly SandboxInput _input = new();
+
+    /// <summary>Raised by a second Esc (the first lets go of the mouse); only the menu, which hosts the sandbox as a child, listens.</summary>
+    public event Action? ExitRequested;
+
     private InputCmd[] _commands = Array.Empty<InputCmd>();
     private LaunchOptions _options = new();
     private SandboxAutomation _automation = null!;
@@ -130,7 +134,11 @@ public partial class SandboxMain : Node3D
         }
         if (e.IsActionPressed(DriveInput.SniperToggle)) _camera.ToggleSniper();
         if (e.IsActionPressed(SandboxInput.SpotDebug)) _hud.ToggleSpotDebug();
-        if (e.IsActionPressed(DriveInput.UiCancel)) Input.MouseMode = Input.MouseModeEnum.Visible;
+        if (e.IsActionPressed(DriveInput.UiCancel))
+        {
+            if (captured) Input.MouseMode = Input.MouseModeEnum.Visible;
+            else ExitRequested?.Invoke();
+        }
     }
 
     public override void _Process(double delta)
PATCH
```

新建 `src/Tank.Client/scripts/UI/Logic/ModeText.cs`：

```csharp
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.UI;

/// <summary>The words for the modes and the tanks as the menu, the room and the LAN list show them.</summary>
public static class ModeText
{
    private const int SecondsPerMinute = 60;

    /// <summary>The mode's name for people.</summary>
    public static string Name(BattleMode mode) => mode switch
    {
        BattleMode.TeamBattle => "团队对战",
        BattleMode.Annihilation => "团队歼灭",
        BattleMode.FreeForAll => "各自为战",
        _ => mode.ToString(),
    };

    /// <summary>One line on how the mode is won (the numbers come from <see cref="ModeRules"/>).</summary>
    public static string Rule(BattleMode mode)
    {
        int minutes = ModeRules.DurationTicks(mode) / (SimConstants.TickRate * SecondsPerMinute);
        return mode switch
        {
            BattleMode.TeamBattle => $"两队对抗，被击毁的坦克 {ModeRules.RespawnTicks / SimConstants.TickRate} 秒后复活；{minutes} 分钟内击毁多的一队获胜。",
            BattleMode.Annihilation => $"两队对抗，被击毁就出局；全歼对方获胜，{minutes} 分钟内没分出胜负算平局。",
            BattleMode.FreeForAll => $"人人为敌，被击毁就出局；最后留下的一辆获胜，{minutes} 分钟后按剩余血量定胜负。",
            _ => string.Empty,
        };
    }

    /// <summary>The tank's name by its place in <see cref="TankCatalog.All"/> (what the room messages carry).</summary>
    public static string TankName(int tankIndex) =>
        tankIndex >= 0 && tankIndex < TankCatalog.All.Count ? TankCatalog.All[tankIndex].Name : "未知车型";
}
```

新建 `src/Tank.Client/scripts/UI/Logic/NameLimit.cs`：

```csharp
using System.Text;
using Tank.Net.Battle;

namespace Tank.Client.UI;

/// <summary>
/// The player name's wire limit, applied where the name is typed: <see cref="BattleProtocol.MaxNameBytes"/> bytes of
/// UTF-8, not characters (<c>BitWriter.WriteString</c> cuts a longer name silently, so ten 汉字 would reach the room as
/// eight). Cutting here makes the box show what the room will.
/// </summary>
public static class NameLimit
{
    /// <summary><paramref name="text"/> cut to at most <paramref name="maxBytes"/> UTF-8 bytes, whole characters only (a surrogate pair stays together).</summary>
    public static string Clip(string text, int maxBytes = BattleProtocol.MaxNameBytes)
    {
        if (Encoding.UTF8.GetByteCount(text) <= maxBytes) return text;
        int bytes = 0, end = 0;
        while (end < text.Length)
        {
            int width = char.IsSurrogatePair(text, end) ? 2 : 1;
            int size = Encoding.UTF8.GetByteCount(text.AsSpan(end, width));
            if (bytes + size > maxBytes) break;
            bytes += size;
            end += width;
        }
        return text[..end];
    }
}
```

新建 `src/Tank.Client/scripts/UI/Logic/RoomModel.cs`：

```csharp
using System.Globalization;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.UI;

/// <summary>How a row's state text should be coloured; the screen picks the actual colours.</summary>
public enum RowTone { Normal, Dim, Good }

/// <summary>Whose side a column is, seen from the viewer's seat: teams are never "gold" or "blue", only "mine" and "theirs".</summary>
public enum ColumnSide { Mine, Other, Everyone }

/// <param name="Ghost">A placeholder for bots the server will add when the match starts, not a seat yet.</param>
public sealed record RoomRowView(byte Slot, string Name, string Tags, string TankName, string State, RowTone Tone, bool IsMe, bool IsBot, bool Ghost);

public sealed record RoomColumnView(string Title, ColumnSide Side, IReadOnlyList<RoomRowView> Rows);

/// <summary>
/// Everything the room screen shows and which controls work, worked out from the room state and the viewer's slot.
/// Engine-free so it is tested as it is. In a team mode there are two columns, 我方 (the viewer's team) and 敌方; without
/// teams, one. The host alone sets mode and bots and starts; everyone picks a tank, readies up and (in a team mode) swaps sides.
/// </summary>
public sealed class RoomModel
{
    private const string WaitingNotice = "对局进行中，等下一局";
    private const string BotsNote = "机器人";

    private string? _signature;

    private RoomModel() { }

    public BattleMode Mode { get; private init; }
    public int Bots { get; private init; }
    public bool HasTeams { get; private init; }
    public bool IsHost { get; private init; }
    public string Title { get; private init; } = "";
    public string RuleLine { get; private init; } = "";

    /// <summary>Set while a match is running and the viewer sits in the room: they join the next one.</summary>
    public string? WaitingText { get; private init; }

    public IReadOnlyList<RoomColumnView> Columns { get; private init; } = Array.Empty<RoomColumnView>();

    public bool CanSetMode { get; private init; }
    public bool CanSetBots { get; private init; }
    public bool CanSwitchTeam { get; private init; }

    /// <summary>The team to ask for with 换到对面: the one the viewer isn't on.</summary>
    public byte OtherTeam { get; private init; }

    /// <summary>The viewer's tank as an index into <see cref="TankCatalog.All"/>; -1 when the viewer isn't in the list.</summary>
    public int MyTankIndex { get; private init; } = -1;

    public bool MyReady { get; private init; }
    public string ReadyLabel => MyReady ? "取消准备" : "准备";

    public bool ShowStart { get; private init; }
    public bool CanStart { get; private init; }

    /// <summary>Under the Start button: why it is disabled, or how many are ready.</summary>
    public string StartHint { get; private init; } = "";

    /// <summary>Changes whenever anything on screen would, so the screen rebuilds only then.</summary>
    public string Signature => _signature ??= SignatureOf(this);

    public static RoomModel Build(RoomStateMessage room, byte mySlot)
    {
        RoomPlayer? me = room.Players.FirstOrDefault(p => p.Slot == mySlot);
        bool teams = ModeRules.HasTeams(room.Mode);
        bool isHost = room.HostSlot == mySlot;
        byte myTeam = me?.Team ?? 0;
        StartPlan plan = room.MatchRunning ? default : RoomRules.PlanFor(room);   // bots join only when a match starts
        string? block = isHost ? RoomRules.StartBlockReason(room) : null;
        return new RoomModel
        {
            Mode = room.Mode,
            Bots = room.Bots,
            HasTeams = teams,
            IsHost = isHost,
            Title = ModeText.Name(room.Mode),
            RuleLine = ModeText.Rule(room.Mode),
            WaitingText = room.MatchRunning ? WaitingNotice : null,
            Columns = BuildColumns(room, mySlot, myTeam, teams, plan),
            CanSetMode = isHost && !room.MatchRunning,
            CanSetBots = isHost && !room.MatchRunning,
            CanSwitchTeam = teams && me is not null,
            OtherTeam = (byte)(1 - myTeam),
            MyTankIndex = me is null ? -1 : me.TankIndex,
            MyReady = me?.Ready ?? false,
            ShowStart = isHost,
            CanStart = isHost && block is null,
            StartHint = isHost ? block ?? ReadyText(room) : "",
        };
    }

    private static string ReadyText(RoomStateMessage room)
    {
        int people = room.Players.Count(p => !p.IsBot);
        int ready = room.Players.Count(p => !p.IsBot && p.Ready);
        return string.Create(CultureInfo.InvariantCulture, $"{ready}/{people} 人已准备，随时可以开始。");
    }

    private static IReadOnlyList<RoomColumnView> BuildColumns(RoomStateMessage room, byte mySlot, byte myTeam, bool teams, StartPlan plan)
    {
        IEnumerable<RoomPlayer> ordered = room.Players.OrderBy(p => p.Slot);
        RoomRowView Row(RoomPlayer p) => RowOf(p, room.HostSlot, mySlot);
        if (!teams)
        {
            List<RoomRowView> rows = ordered.Select(Row).ToList();
            int everyone = rows.Count + plan.Bots;
            AddGhost(rows, plan.Bots);
            return new[] { new RoomColumnView(Titled("玩家", everyone), ColumnSide.Everyone, rows) };
        }
        List<RoomRowView> mine = ordered.Where(p => p.Team == myTeam).Select(Row).ToList();
        List<RoomRowView> other = ordered.Where(p => p.Team != myTeam).Select(Row).ToList();
        bool mineIsTeam0 = myTeam == 0;
        int botsMine = mineIsTeam0 ? plan.BotsToTeam0 : plan.BotsToTeam1;
        int botsOther = mineIsTeam0 ? plan.BotsToTeam1 : plan.BotsToTeam0;
        int tanksMine = mine.Count + botsMine, tanksOther = other.Count + botsOther;   // the title counts tanks, bots to come included
        AddGhost(mine, botsMine);
        AddGhost(other, botsOther);
        return new[]
        {
            new RoomColumnView(Titled("我方", tanksMine), ColumnSide.Mine, mine),
            new RoomColumnView(Titled("敌方", tanksOther), ColumnSide.Other, other),
        };
    }

    private static string Titled(string name, int count) => string.Create(CultureInfo.InvariantCulture, $"{name} · {count}");

    private static void AddGhost(List<RoomRowView> rows, int bots)
    {
        if (bots <= 0) return;
        rows.Add(new RoomRowView(byte.MaxValue, string.Create(CultureInfo.InvariantCulture, $"{BotsNote} × {bots}"), "", "", "开局时补入", RowTone.Dim, false, true, true));
    }

    private static RoomRowView RowOf(RoomPlayer p, byte hostSlot, byte mySlot)
    {
        string tags = string.Join(' ', new[] { p.Slot == hostSlot ? "房主" : null, p.Slot == mySlot ? "你" : null }.OfType<string>());
        (string state, RowTone tone) = p switch
        {
            { IsBot: true } => (BotsNote, RowTone.Dim),
            { Connected: false } => ("重连中", RowTone.Dim),
            { Ready: true } => ("已准备", RowTone.Good),
            _ => ("未准备", RowTone.Dim),
        };
        return new RoomRowView(p.Slot, p.Name, tags, ModeText.TankName(p.TankIndex), state, tone, p.Slot == mySlot, p.IsBot, false);
    }

    private static string SignatureOf(RoomModel m)
    {
        IEnumerable<string> rows = m.Columns.SelectMany(c => c.Rows.Select(r => $"{c.Side}:{r.Slot}:{r.Name}:{r.Tags}:{r.TankName}:{r.State}"));
        return string.Create(CultureInfo.InvariantCulture,
            $"{m.Mode}|{m.Bots}|{m.IsHost}|{m.WaitingText}|{m.CanStart}|{m.StartHint}|{m.MyTankIndex}|{m.MyReady}|{string.Join(';', rows)}");
    }
}
```

新建 `src/Tank.Client/scripts/UI/Logic/RoomRules.cs`：

```csharp
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.UI;

/// <summary>What starting would seat: the people already in the room plus the bots the server would add, and how they split.</summary>
/// <param name="Team0">Tanks on team 0 (people and bots); zero in a mode without teams.</param>
/// <param name="Team1">Tanks on team 1 (people and bots); zero in a mode without teams.</param>
/// <param name="BotsToTeam0">Of <see cref="Bots"/>, how many the server would put on team 0; zero in a mode without teams.</param>
/// <param name="BotsToTeam1">Of <see cref="Bots"/>, how many the server would put on team 1; zero in a mode without teams.</param>
public readonly record struct StartPlan(int Humans, int Bots, int Team0, int Team1, int BotsToTeam0, int BotsToTeam1)
{
    public int Seated => Humans + Bots;
}

/// <summary>
/// Whether the host's Start button can work: the same checks as <c>BattleServer.TryStartMatch</c>, worked out from the room
/// state the client sees, so the button can say why not before the server has to refuse. The cross-check test runs both.
/// </summary>
public static class RoomRules
{
    /// <summary>Why the match can't start now, in words for the host; null when it can.</summary>
    public static string? StartBlockReason(RoomStateMessage room)
    {
        if (room.MatchRunning) return "对局进行中，等这一局结束。";
        StartPlan plan = PlanFor(room);
        if (plan.Seated < ModeRules.MinPlayers) return "至少要有两辆坦克：等朋友加入，或加几个机器人。";
        if (ModeRules.HasTeams(room.Mode) && (plan.Team0 == 0 || plan.Team1 == 0))
            return "有一队没有坦克：让人换到对面，或加几个机器人。";
        return null;
    }

    /// <summary>
    /// Who would be seated if the match started now: every person in the room (a dropped one still holds the seat),
    /// and the bots asked for as far as the room has places, each onto the team with fewer tanks (team 0 on a tie).
    /// </summary>
    public static StartPlan PlanFor(RoomStateMessage room)
    {
        int humans = room.Players.Count(p => !p.IsBot);
        int bots = Math.Min(room.Bots, Math.Max(0, BattleProtocol.MaxPlayers - humans));
        if (!ModeRules.HasTeams(room.Mode)) return new StartPlan(humans, bots, 0, 0, 0, 0);

        int team0 = room.Players.Count(p => !p.IsBot && p.Team == 0);
        int team1 = humans - team0;
        int botsToTeam0 = 0;
        for (int i = 0; i < bots; i++)
        {
            if (team1 < team0) team1++;
            else
            {
                team0++;
                botsToTeam0++;
            }
        }
        return new StartPlan(humans, bots, team0, team1, botsToTeam0, bots - botsToTeam0);
    }
}
```

修改 `src/Tank.Client/scripts/UI/MenuScreen.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/UI/MenuScreen.cs b/src/Tank.Client/scripts/UI/MenuScreen.cs
index e3c7dce..e876c6c 100644
--- a/src/Tank.Client/scripts/UI/MenuScreen.cs
+++ b/src/Tank.Client/scripts/UI/MenuScreen.cs
@@ -1,119 +1,136 @@
+using System.Net;
 using Godot;
-using Tank.Client.View;
-using Tank.Core.World;
+using Tank.Client.Online;
 using Tank.Net.Protocol;
-using Tank.Net.Transport;
 
 namespace Tank.Client.UI;
 
-/// <summary>Main menu: player name, host a room, or join one found on the LAN (or by IP).</summary>
+/// <summary>
+/// Main menu: your name, host a room, take the test drive, or join a room found on the LAN (or typed in). It only raises
+/// events; <c>Main</c> starts the session. The LAN list comes from <see cref="LanBrowser"/> through <see cref="ShowRooms"/>.
+/// </summary>
 public partial class MenuScreen : Control
 {
-    private readonly LineEdit _name = new() { PlaceholderText = "你的名字", MaxLength = ProtocolInfo.MaxPlayerNameBytes };
-    private readonly OptionButton _mode = new();
-    private readonly LineEdit _ip = new() { PlaceholderText = "192.168.1.20", CustomMinimumSize = new Vector2(220, 0) };
+    private const int PanelPadding = 22;
+
+    private readonly LineEdit _name = new() { PlaceholderText = "你的名字" };   // held to the wire's 24 bytes by OnNameChanged, not a character count
+    private readonly LineEdit _address = new() { PlaceholderText = "192.168.1.20", CustomMinimumSize = new Vector2(220, 0) };
     private readonly ItemList _rooms = new() { CustomMinimumSize = new Vector2(0, 260), SizeFlagsVertical = SizeFlags.ExpandFill };
-    private readonly Label _status = UiKit.Label("", 16, Palette.UiDim);
-    private readonly List<DiscoveredRoom> _roomList = new();
-    private readonly Label _version = UiKit.Label("", 13, Palette.UiDim);
-    private readonly PanelContainer _updateBanner = UiKit.Panel(new Color(Palette.UiAccent, 0.12f), 14, new Color(Palette.UiAccent, 0.6f));
-    private readonly Label _updateText = UiKit.Label("", 15, Palette.UiText);
+    private readonly Label _status = UiKit.Label("", 16, UiKit.Dim);
+    private readonly Label _lanNote = UiKit.Label("", 14, UiKit.Dim);
+    private readonly Label _version = UiKit.Label("", 13, UiKit.Dim);
+    private readonly PanelContainer _updateBanner = UiKit.Panel(new Color(UiKit.Accent, 0.12f), 14, new Color(UiKit.Accent, 0.6f));
+    private readonly Label _updateText = UiKit.Label("", 15, UiKit.TextColor);
     private readonly Button _updateButton = new() { CustomMinimumSize = new Vector2(0, 40) };
+    private List<LanRoomRow> _roomRows = new();
+    private IPEndPoint? _selected;
+    private IReadOnlyList<LanRoomRow>? _shownRows;   // the list last passed in: LanBrowser replaces it only on a change
+    private string _roomSignature = "";
     private Action? _updateAction;
 
-    public event Action<string, GameMode>? HostRequested;
+    public event Action<string>? HostRequested;
     public event Action<string, string, int>? JoinRequested;
+    public event Action? SandboxRequested;
 
     public string PlayerName
     {
         get => string.IsNullOrWhiteSpace(_name.Text) ? "玩家" : _name.Text.Trim();
-        set => _name.Text = value;
+        set => _name.Text = NameLimit.Clip(value);
     }
 
     public override void _Ready()
     {
         UiKit.FillViewport(this);
         Theme = UiKit.Theme;
-        var bg = new ColorRect { Color = Palette.Background, MouseFilter = MouseFilterEnum.Ignore };
+        var bg = new ColorRect { Color = UiKit.Background, MouseFilter = MouseFilterEnum.Ignore };
         AddChild(bg);
         bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
 
         var margin = new MarginContainer();
-        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 64);
+        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 64);
         AddChild(margin);
         margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
 
         var columns = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
         columns.AddThemeConstantOverride("separation", 48);
         margin.AddChild(columns);
-
         columns.AddChild(BuildLeft());
         columns.AddChild(BuildRight());
+        _rooms.ItemSelected += OnRoomSelected;
+        _rooms.ItemActivated += index => JoinRoomAt((int)index);
+        _name.TextChanged += OnNameChanged;
     }
 
+    /// <summary>The line under the buttons: progress, or what went wrong.</summary>
     public void SetStatus(string text, bool error = false)
     {
         _status.Text = text;
-        _status.AddThemeColorOverride("font_color", error ? Palette.UiDanger : Palette.UiDim);
+        _status.AddThemeColorOverride("font_color", error ? UiKit.Danger : UiKit.Dim);
+    }
+
+    public void SetVersion(string version) => _version.Text = $"v{version}";
+
+    /// <summary>Shows the update banner; a null <paramref name="text"/> hides it.</summary>
+    public void SetUpdate(string? text, string? buttonText = null, Action? onPressed = null)
+    {
+        _updateBanner.Visible = text is not null;
+        _updateText.Text = text ?? string.Empty;
+        _updateAction = onPressed;
+        _updateButton.Visible = buttonText is not null;
+        _updateButton.Text = buttonText ?? string.Empty;
     }
 
-    public void ShowRooms(IReadOnlyCollection<DiscoveredRoom> rooms)
+    /// <summary>
+    /// The rooms heard on the LAN, and why searching fails (null when it works). Called every frame: a list it has
+    /// already shown costs nothing, and the item list is rebuilt only when what it shows changed.
+    /// </summary>
+    public void ShowRooms(IReadOnlyList<LanRoomRow> rows, string? error)
     {
-        var ordered = rooms.OrderBy(r => r.Info.RoomName).ToList();
-        if (ordered.Select(r => r.EndPoint.ToString() + r.Info.Players + r.Info.MatchRunning + r.PingMs / 5)
-            .SequenceEqual(_roomList.Select(r => r.EndPoint.ToString() + r.Info.Players + r.Info.MatchRunning + r.PingMs / 5))) return;
-        int selected = _rooms.GetSelectedItems().FirstOrDefault(-1);
-        _roomList.Clear();
-        _roomList.AddRange(ordered);
+        _lanNote.Text = error ?? (rows.Count == 0 ? "还没有找到房间。可以自己开一个，或者在下面输入地址。" : "双击房间可以直接加入。");
+        _lanNote.AddThemeColorOverride("font_color", error is null ? UiKit.Dim : UiKit.Danger);
+        if (ReferenceEquals(rows, _shownRows)) return;
+        _shownRows = rows;
+        string signature = string.Join('\n', rows.Select(r => $"{r.EndPoint}|{r.Text}|{r.Joinable}"));
+        if (signature == _roomSignature) return;
+        _roomSignature = signature;
+        _roomRows = rows.ToList();
         _rooms.Clear();
-        foreach (var r in _roomList)
+        int keep = -1;
+        for (int i = 0; i < _roomRows.Count; i++)
         {
-            string state = r.Info.MatchRunning ? "对战中" : "等待中";
-            _rooms.AddItem($"{UiKit.Pad(r.Info.RoomName, 18)} {UiKit.Pad(LobbyScreen.ModeName(r.Info.Mode), 12)} {r.Info.Players}/{r.Info.MaxPlayers}  {UiKit.Pad(state, 7)} {r.PingMs,3}ms   {r.EndPoint.Address}");
+            _rooms.AddItem(_roomRows[i].Text);
+            if (!_roomRows[i].Joinable) _rooms.SetItemCustomFgColor(i, UiKit.Dim.Darkened(0.2f));
+            if (Equals(_roomRows[i].EndPoint, _selected)) keep = i;
         }
-        if (selected >= 0 && selected < _rooms.ItemCount) _rooms.Select(selected);
+        if (keep >= 0) _rooms.Select(keep);
+        else _selected = null;
     }
 
     private Control BuildLeft()
     {
         var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 0.9f };
         col.AddThemeConstantOverride("separation", 14);
-        col.AddChild(UiKit.Label("坦克", 112, Palette.UiAccent));
-        col.AddChild(UiKit.Label("3D  ·  局域网对战", 30, Palette.UiText));
-        col.AddChild(UiKit.Label("守住老鹰，打穿砖墙，叫上朋友。", 16, Palette.UiDim));
+        col.AddChild(UiKit.Label("坦克", 112, UiKit.Accent));
+        col.AddChild(UiKit.Label("3D  ·  局域网对战", 30, UiKit.TextColor));
+        col.AddChild(UiKit.Label("分成两队，打穿掩体，击毁对手。叫上朋友，或者带几个机器人。", 16, UiKit.Dim));
         col.AddChild(_version);
         col.AddChild(BuildUpdateBanner());
         col.AddChild(UiKit.Gap(20));
 
-        col.AddChild(UiKit.Label("呼号", 14, Palette.UiDim));
+        col.AddChild(UiKit.Label("呼号", 14, UiKit.Dim));
         col.AddChild(_name);
         col.AddChild(UiKit.Gap(10));
-
-        col.AddChild(UiKit.Label("创建房间", 14, Palette.UiDim));
-        foreach (var m in new[] { GameMode.Coop, GameMode.BaseBattle, GameMode.Deathmatch }) _mode.AddItem(LobbyScreen.ModeName(m), (int)m);
-        col.AddChild(_mode);
-        col.AddChild(UiKit.Button("开设房间", () => HostRequested?.Invoke(PlayerName, (GameMode)_mode.GetSelectedId()), primary: true));
-        col.AddChild(UiKit.Gap(10));
+        col.AddChild(UiKit.Button("开房间", () => HostRequested?.Invoke(PlayerName), primary: true));
+        col.AddChild(UiKit.Button("试驾场", () => SandboxRequested?.Invoke()));
+        col.AddChild(UiKit.Gap(6));
+        _status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
         col.AddChild(_status);
 
-        var spacer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
-        col.AddChild(spacer);
-        col.AddChild(UiKit.Label("WASD / 方向键 移动 · J / 空格 开火 · C 切换镜头 · F3 网络面板 · Tab 计分板 · Esc 离开", 13, Palette.UiDim));
+        col.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
+        col.AddChild(UiKit.Label("WASD 移动 · 鼠标瞄准 · 左键开火 · 1 / 2 换弹种 · Shift 狙击镜 · Tab 计分板 · F3 网络面板", 13, UiKit.Dim));
         return col;
     }
 
-    public void SetVersion(string version) => _version.Text = $"v{version}";
-
-    /// <summary>Shows the update banner; a null <paramref name="text"/> hides it.</summary>
-    public void SetUpdate(string? text, string? buttonText = null, Action? onPressed = null)
-    {
-        _updateBanner.Visible = text is not null;
-        _updateText.Text = text ?? string.Empty;
-        _updateAction = onPressed;
-        _updateButton.Visible = buttonText is not null;
-        _updateButton.Text = buttonText ?? string.Empty;
-    }
-
     private Control BuildUpdateBanner()
     {
         var row = new HBoxContainer();
@@ -131,43 +148,70 @@ public partial class MenuScreen : Control
 
     private Control BuildRight()
     {
-        var panel = UiKit.Panel(pad: 22);
+        var panel = UiKit.Panel(pad: PanelPadding);
         panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
         var col = new VBoxContainer();
         col.AddThemeConstantOverride("separation", 12);
         panel.AddChild(col);
 
-        col.AddChild(UiKit.Label("局域网内的房间", 14, Palette.UiDim));
-        _rooms.ItemActivated += index => JoinSelected((int)index);
+        col.AddChild(UiKit.Label("局域网内的房间", 14, UiKit.Dim));
         col.AddChild(_rooms);
-        col.AddChild(UiKit.Button("加入所选房间", () => JoinSelected(_rooms.GetSelectedItems().FirstOrDefault(-1)), primary: true));
+        _lanNote.AutowrapMode = TextServer.AutowrapMode.WordSmart;
+        col.AddChild(_lanNote);
+        col.AddChild(UiKit.Button("加入所选房间", () => JoinRoomAt(_rooms.GetSelectedItems().FirstOrDefault(-1)), primary: true));
         col.AddChild(UiKit.Gap(8));
 
-        col.AddChild(UiKit.Label("找不到房间？输入地址加入", 14, Palette.UiDim));
+        col.AddChild(UiKit.Label("找不到房间？输入地址加入", 14, UiKit.Dim));
         var row = new HBoxContainer();
         row.AddThemeConstantOverride("separation", 10);
-        _ip.SizeFlagsHorizontal = SizeFlags.ExpandFill;
-        row.AddChild(_ip);
+        _address.SizeFlagsHorizontal = SizeFlags.ExpandFill;
+        row.AddChild(_address);
         row.AddChild(UiKit.Button("加入", JoinByAddress));
         col.AddChild(row);
-        col.AddChild(UiKit.Label("房间通过 UDP 广播发现。访客 Wi-Fi 或开启了 AP 隔离的网络可能搜不到，请改用地址加入。", 13, Palette.UiDim));
+        var note = UiKit.Label($"房间靠 UDP 广播被发现，只会搜到使用默认端口 {ProtocolInfo.DefaultPort} 的房间：房主改了端口时，请输入“地址:端口”加入。" +
+                               "访客 Wi-Fi 或开启了 AP 隔离的网络也搜不到，同样请用地址加入。", 13, UiKit.Dim);
+        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
+        col.AddChild(note);
         return panel;
     }
 
-    private void JoinSelected(int index)
+    /// <summary>Keeps the box within the wire's limit as the player types or pastes, so the room shows the name as typed.</summary>
+    private void OnNameChanged(string text)
+    {
+        string clipped = NameLimit.Clip(text);
+        if (clipped.Length == text.Length) return;
+        _name.Text = clipped;   // setting Text raises no TextChanged
+        _name.CaretColumn = clipped.Length;
+    }
+
+    private void OnRoomSelected(long index)
     {
-        if (index < 0 || index >= _roomList.Count)
+        if (index < 0 || index >= _roomRows.Count) return;
+        LanRoomRow row = _roomRows[(int)index];
+        _selected = row.EndPoint;
+        if (row.WhyNot is { } why) SetStatus(why, error: !row.Joinable);
+        else SetStatus("");
+    }
+
+    private void JoinRoomAt(int index)
+    {
+        if (index < 0 || index >= _roomRows.Count)
         {
             SetStatus("请先选择一个房间。", error: true);
             return;
         }
-        var room = _roomList[index];
-        JoinRequested?.Invoke(PlayerName, room.EndPoint.Address.ToString(), room.EndPoint.Port);
+        LanRoomRow row = _roomRows[index];
+        if (!row.Joinable)
+        {
+            SetStatus(row.WhyNot ?? "这个房间现在不能加入。", error: true);
+            return;
+        }
+        JoinRequested?.Invoke(PlayerName, row.EndPoint.Address.ToString(), row.EndPoint.Port);
     }
 
     private void JoinByAddress()
     {
-        string text = _ip.Text.Trim();
+        string text = _address.Text.Trim();
         if (text.Length == 0)
         {
             SetStatus("请输入房主的 IP 地址。", error: true);
@@ -180,7 +224,12 @@ public partial class MenuScreen : Control
             port = p;
             text = text[..colon];
         }
-        if (!System.Net.IPAddress.TryParse(text, out _) && Uri.CheckHostName(text) == UriHostNameType.Unknown)
+        if (port is < 1 or > 65535)
+        {
+            SetStatus($"端口 {port} 不对，应在 1 到 65535 之间。", error: true);
+            return;
+        }
+        if (!IPAddress.TryParse(text, out _) && Uri.CheckHostName(text) == UriHostNameType.Unknown)
         {
             SetStatus($"“{text}”不是有效的地址。", error: true);
             return;
PATCH
```

新建 `src/Tank.Client/scripts/UI/RoomScreen.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Client.UI;

/// <summary>
/// The room: who is here (two columns seen from your seat, or one in a mode without teams), and on the right the controls
/// — mode and bots for the host, tank for everyone, swap sides, ready, start, leave. What shows and what is enabled comes
/// from <see cref="RoomModel"/>; this class only builds controls from it, and only when the model's signature changes.
/// </summary>
public partial class RoomScreen : Control
{
    private const int PanelPadding = 22;
    private const int ColumnMinWidth = 380;
    private const int SidePanelWidth = 420;

    private static readonly Color MineColor = Palette.AllyBody.ToColor();
    private static readonly Color OtherColor = Palette.EnemyBody.ToColor();
    private static readonly Color OwnColor = Palette.TankBody.ToColor();

    private readonly Label _title = UiKit.Label("", 40, UiKit.Accent);
    private readonly Label _rule = UiKit.Label("", 16, UiKit.Dim);
    private readonly PanelContainer _waitingBanner = UiKit.Panel(new Color(UiKit.Accent, 0.14f), 14, new Color(UiKit.Accent, 0.6f));
    private readonly Label _waitingText = UiKit.Label("", 20, UiKit.Accent);
    private readonly HBoxContainer _columns = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private readonly OptionButton _mode = new();
    private readonly OptionButton _bots = new();
    private readonly GridContainer _tanks = new() { Columns = 2 };
    private readonly List<Button> _tankButtons = new();
    private readonly Button _switchTeam;
    private readonly Button _ready;
    private readonly Button _start;
    private readonly Label _hint = UiKit.Label("", 15, UiKit.Dim);
    private readonly Label _addressLine = UiKit.Label("", 14, UiKit.Dim);
    private BattleClient? _client;
    private RoomModel? _model;
    private string _signature = "";
    private RoomStateMessage? _builtFrom;   // the room state (and slot) the model was last built from: BattleClient replaces Room only when a new one arrives
    private byte _builtForSlot;
    private bool _updating;

    public event Action? LeaveRequested;

    public RoomScreen()
    {
        _switchTeam = UiKit.Button("换到对面", () => { if (_model is { } m) _client?.SetTeam(m.OtherTeam); });
        _ready = UiKit.Button("准备", () => { if (_model is { } m) _client?.SetReady(!m.MyReady); });
        _start = UiKit.Button("开始对战", () => _client?.StartMatch(), primary: true);
    }

    public override void _Ready()
    {
        UiKit.FillViewport(this);
        Theme = UiKit.Theme;
        var bg = new ColorRect { Color = UiKit.Background, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(bg);
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var margin = new MarginContainer();
        foreach (string edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride($"margin_{edge}", 64);
        AddChild(margin);
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        var col = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 14);
        margin.AddChild(col);
        col.AddChild(_title);
        col.AddChild(_rule);
        _waitingBanner.AddChild(_waitingText);
        _waitingBanner.Visible = false;
        col.AddChild(_waitingBanner);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 32);
        col.AddChild(body);
        _columns.AddThemeConstantOverride("separation", 20);
        _columns.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        body.AddChild(_columns);
        body.AddChild(BuildSide());
    }

    /// <summary>Starts showing <paramref name="client"/>'s room; <paramref name="addresses"/> is what friends can type to join the host.</summary>
    public void Bind(BattleClient client, string addresses)
    {
        _client = client;
        _signature = "";
        _model = null;
        _builtFrom = null;
        _addressLine.Text = addresses.Length == 0 ? "" : $"朋友可以在房间列表里找到你，或者输入 {addresses} 加入。";
    }

    /// <summary>Call every frame: the model is built only from a room state (or slot) not seen before, and the controls rebuilt only when it shows something new.</summary>
    public void Refresh()
    {
        if (_client is null) return;
        if (_client.Room is not { } room)
        {
            SetWaiting("正在进入房间…");
            return;
        }
        if (ReferenceEquals(room, _builtFrom) && _client.Slot == _builtForSlot) return;   // RoomModel.Build reads nothing else
        (_builtFrom, _builtForSlot) = (room, _client.Slot);
        RoomModel model = RoomModel.Build(room, _client.Slot);
        if (model.Signature == _signature) return;
        _signature = model.Signature;
        _model = model;
        Apply(model);
    }

    private void SetWaiting(string text)
    {
        _title.Text = text;
        _rule.Text = "";
    }

    private void Apply(RoomModel m)
    {
        _updating = true;
        _title.Text = m.Title;
        _rule.Text = m.RuleLine;
        _waitingBanner.Visible = m.WaitingText is not null;
        _waitingText.Text = m.WaitingText ?? "";

        _mode.Select(_mode.GetItemIndex((int)m.Mode));
        _mode.Disabled = !m.CanSetMode;
        _bots.Select(Math.Clamp(m.Bots, 0, _bots.ItemCount - 1));
        _bots.Disabled = !m.CanSetBots;
        for (int i = 0; i < _tankButtons.Count; i++) _tankButtons[i].SetPressedNoSignal(i == m.MyTankIndex);
        _switchTeam.Visible = m.HasTeams;
        _switchTeam.Disabled = !m.CanSwitchTeam;
        _ready.Text = m.MyReady ? "✓ 已准备（点击取消）" : "准备";
        _start.Visible = m.ShowStart;
        _start.Disabled = !m.CanStart;
        _hint.Visible = m.ShowStart;
        _hint.Text = m.StartHint;
        _hint.AddThemeColorOverride("font_color", m.CanStart ? UiKit.Good : UiKit.Dim);
        _addressLine.Visible = m.IsHost;
        _updating = false;

        foreach (Node child in _columns.GetChildren()) child.QueueFree();
        foreach (RoomColumnView column in m.Columns) _columns.AddChild(BuildColumn(column));
    }

    private Control BuildSide()
    {
        var panel = UiKit.Panel(pad: PanelPadding);
        panel.CustomMinimumSize = new Vector2(SidePanelWidth, 0);
        panel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 10);
        panel.AddChild(side);

        side.AddChild(UiKit.Label("模式（房主）", 14, UiKit.Dim));
        foreach (BattleMode mode in ModeRules.All) _mode.AddItem(ModeText.Name(mode), (int)mode);
        _mode.ItemSelected += i => { if (!_updating) _client?.SetMode((BattleMode)_mode.GetItemId((int)i)); };
        side.AddChild(_mode);

        side.AddChild(UiKit.Label("机器人（房主）", 14, UiKit.Dim));
        for (int n = 0; n <= BattleProtocol.MaxPlayers - 1; n++) _bots.AddItem(n == 0 ? "没有机器人" : $"{n} 个机器人", n);
        _bots.ItemSelected += i => { if (!_updating) _client?.SetBots(_bots.GetItemId((int)i)); };
        side.AddChild(_bots);

        side.AddChild(UiKit.Label("我的坦克", 14, UiKit.Dim));
        _tanks.AddThemeConstantOverride("h_separation", 8);
        _tanks.AddThemeConstantOverride("v_separation", 8);
        var group = new ButtonGroup();
        for (int i = 0; i < TankCatalog.All.Count; i++)
        {
            int index = i;
            Button b = UiKit.Button(ModeText.TankName(i), () => { if (!_updating) _client?.SetTank(TankCatalog.All[index].Id); });
            b.ToggleMode = true;
            b.ButtonGroup = group;
            b.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            _tankButtons.Add(b);
            _tanks.AddChild(b);
        }
        side.AddChild(_tanks);

        side.AddChild(UiKit.Gap(6));
        side.AddChild(_switchTeam);
        side.AddChild(_ready);
        side.AddChild(_start);
        _hint.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        side.AddChild(_hint);
        _addressLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        side.AddChild(_addressLine);
        side.AddChild(UiKit.Gap(14));
        side.AddChild(UiKit.Button("离开房间", () => LeaveRequested?.Invoke()));
        return panel;
    }

    private static Control BuildColumn(RoomColumnView column)
    {
        Color accent = column.Side switch { ColumnSide.Mine => MineColor, ColumnSide.Other => OtherColor, _ => UiKit.Accent };
        var panel = UiKit.Panel(pad: PanelPadding, border: new Color(accent, 0.7f));
        panel.CustomMinimumSize = new Vector2(ColumnMinWidth, 0);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        panel.SizeFlagsVertical = SizeFlags.ShrinkBegin;
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 10);
        panel.AddChild(col);
        col.AddChild(UiKit.Label(column.Title, 22, accent));
        foreach (RoomRowView row in column.Rows) col.AddChild(BuildRow(row, column.Side));
        return panel;
    }

    private static Control BuildRow(RoomRowView row, ColumnSide side)
    {
        var line = new HBoxContainer();
        line.AddThemeConstantOverride("separation", 12);
        // The bar uses the colour the tank has in the match: orange for you, blue-grey for a teammate, red for an opponent.
        Color bar = row.IsMe ? OwnColor : side == ColumnSide.Mine ? MineColor : OtherColor;
        if (row.Ghost) bar = new Color(bar, 0.35f);
        line.AddChild(new ColorRect { Color = bar, CustomMinimumSize = new Vector2(10, 28) });

        Label name = UiKit.Label(row.Name, 20, row.Ghost ? UiKit.Dim : UiKit.TextColor);
        name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        name.ClipText = true;
        line.AddChild(name);
        if (row.Tags.Length > 0) line.AddChild(UiKit.Label(row.Tags, 13, UiKit.Accent));
        if (row.TankName.Length > 0) line.AddChild(UiKit.Label(row.TankName, 14, UiKit.Dim));
        Color stateColor = row.Tone == RowTone.Good ? UiKit.Good : UiKit.Dim;
        line.AddChild(UiKit.Label(row.State, 14, stateColor));
        return line;
    }
}
```

修改 `src/Tank.Client/scripts/UI/UiKit.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/UI/UiKit.cs b/src/Tank.Client/scripts/UI/UiKit.cs
index 0d6b77b..8267b2c 100644
--- a/src/Tank.Client/scripts/UI/UiKit.cs
+++ b/src/Tank.Client/scripts/UI/UiKit.cs
@@ -1,27 +1,39 @@
 using Godot;
-using Tank.Client.View;
+using Tank.Client.Art;
+using Tank.Client.Online;
 
 namespace Tank.Client.UI;
 
 /// <summary>Shared look for all screens: monospace arcade type (CJK fallback), amber accent, hard-edged panels.</summary>
 public static class UiKit
 {
+    private const float ScreenPanelAlpha = 0.92f;   // screen panels sit on a flat backdrop, so they are nearly opaque (HUD plates are 0.7)
+
     private static Theme? _theme;
 
+    // The look's colours as engine colours, from the shared palette.
+    public static readonly Color Accent = Palette.UiAccent.ToColor();
+    public static readonly Color TextColor = Palette.UiText.ToColor();
+    public static readonly Color Dim = Palette.UiDim.ToColor();
+    public static readonly Color Good = Palette.UiGood.ToColor();
+    public static readonly Color Danger = Palette.UiDanger.ToColor();
+    public static readonly Color Background = Palette.UiBackground.ToColor();
+    public static readonly Color PanelFill = Palette.UiPanel.ToColor(ScreenPanelAlpha);
+
     public static Theme Theme => _theme ??= BuildTheme();
 
     public static Label Label(string text, int size = 18, Color? color = null, HorizontalAlignment align = HorizontalAlignment.Left)
     {
         var l = new Label { Text = text, HorizontalAlignment = align };
         l.AddThemeFontSizeOverride("font_size", size);
-        l.AddThemeColorOverride("font_color", color ?? Palette.UiText);
+        l.AddThemeColorOverride("font_color", color ?? TextColor);
         return l;
     }
 
     public static PanelContainer Panel(Color? bg = null, int pad = 18, Color? border = null)
     {
         var p = new PanelContainer();
-        p.AddThemeStyleboxOverride("panel", Box(bg ?? Palette.UiPanel, pad, border ?? new Color(1, 1, 1, 0.08f)));
+        p.AddThemeStyleboxOverride("panel", Box(bg ?? PanelFill, pad, border ?? new Color(1, 1, 1, 0.08f)));
         return p;
     }
 
@@ -30,9 +42,9 @@ public static class UiKit
         var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, 44), FocusMode = Control.FocusModeEnum.All };
         if (primary)
         {
-            b.AddThemeStyleboxOverride("normal", Box(Palette.UiAccent, 10));
-            b.AddThemeStyleboxOverride("hover", Box(Palette.UiAccent.Lightened(0.15f), 10));
-            b.AddThemeStyleboxOverride("pressed", Box(Palette.UiAccent.Darkened(0.2f), 10));
+            b.AddThemeStyleboxOverride("normal", Box(Accent, 10));
+            b.AddThemeStyleboxOverride("hover", Box(Accent.Lightened(0.15f), 10));
+            b.AddThemeStyleboxOverride("pressed", Box(Accent.Darkened(0.2f), 10));
             b.AddThemeStyleboxOverride("focus", Box(new Color(0, 0, 0, 0), 10, Colors.White));
             b.AddThemeColorOverride("font_color", new Color(0.08f, 0.06f, 0.02f));
             b.AddThemeColorOverride("font_hover_color", new Color(0.08f, 0.06f, 0.02f));
@@ -81,19 +93,8 @@ public static class UiKit
 
     public static Control Gap(int px) => new Control { CustomMinimumSize = new Vector2(px, px) };
 
-    /// <summary>
-    /// Right-pads text to a column width for the monospace tables (room list, scoreboard). CJK and full-width
-    /// characters occupy two columns, so string.PadRight alone would misalign rows that mix them with ASCII.
-    /// </summary>
-    public static string Pad(string text, int columns)
-    {
-        int width = text.Sum(c => IsWide(c) ? 2 : 1);
-        return width >= columns ? text : text + new string(' ', columns - width);
-    }
-
-    private static bool IsWide(char c) =>
-        c is >= '\u1100' and <= '\u115F' or >= '\u2E80' and <= '\uA4CF' or >= '\uAC00' and <= '\uD7A3'
-            or >= '\uF900' and <= '\uFAFF' or >= '\uFE30' and <= '\uFE4F' or >= '\uFF00' and <= '\uFF60' or >= '\uFFE0' and <= '\uFFE6';
+    /// <summary>Right-pads text to a column width for the monospace tables; wide (CJK) characters count as two columns.</summary>
+    public static string Pad(string text, int columns) => TextColumns.PadColumns(text, columns);
 
     private static Theme BuildTheme()
     {
@@ -114,18 +115,18 @@ public static class UiKit
         };
         var theme = new Theme { DefaultFont = font, DefaultFontSize = 18 };
         var normal = Box(new Color(0.13f, 0.14f, 0.19f), 10, new Color(1, 1, 1, 0.1f));
-        var hover = Box(new Color(0.18f, 0.19f, 0.25f), 10, Palette.UiAccent);
+        var hover = Box(new Color(0.18f, 0.19f, 0.25f), 10, Accent);
         theme.SetStylebox("normal", "Button", normal);
         theme.SetStylebox("hover", "Button", hover);
-        theme.SetStylebox("pressed", "Button", Box(new Color(0.09f, 0.1f, 0.13f), 10, Palette.UiAccent));
-        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), 10, Palette.UiAccent));
+        theme.SetStylebox("pressed", "Button", Box(new Color(0.09f, 0.1f, 0.13f), 10, Accent));
+        theme.SetStylebox("focus", "Button", Box(new Color(0, 0, 0, 0), 10, Accent));
         theme.SetStylebox("disabled", "Button", Box(new Color(0.1f, 0.1f, 0.12f), 10));
-        theme.SetColor("font_color", "Button", Palette.UiText);
-        theme.SetColor("font_hover_color", "Button", Palette.UiAccent);
+        theme.SetColor("font_color", "Button", TextColor);
+        theme.SetColor("font_hover_color", "Button", Accent);
         theme.SetStylebox("normal", "LineEdit", Box(new Color(0.05f, 0.06f, 0.08f), 10, new Color(1, 1, 1, 0.12f)));
-        theme.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), 10, Palette.UiAccent));
+        theme.SetStylebox("focus", "LineEdit", Box(new Color(0, 0, 0, 0), 10, Accent));
         theme.SetStylebox("panel", "ItemList", Box(new Color(0.05f, 0.06f, 0.08f), 8, new Color(1, 1, 1, 0.12f)));
-        theme.SetColor("font_selected_color", "ItemList", Palette.UiAccent);
+        theme.SetColor("font_selected_color", "ItemList", Accent);
         theme.SetStylebox("normal", "OptionButton", normal);
         theme.SetStylebox("hover", "OptionButton", hover);
         return theme;
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 644、Net 209。

- [ ] **Step 5：截图验证**

```bash
mkdir -p build/shots/t4
$S/godot_run.sh $W "" 20 -- --no-update --screenshot $W/build/shots/t4/menu.png --screenshot-at 3 --quit-after 5
$S/godot_run.sh $W "" 20 -- --no-update --host --bots 3 --screenshot $W/build/shots/t4/room-team.png --screenshot-at 4 --quit-after 6
$S/godot_run.sh $W "" 20 -- --no-update --host --mode ffa --bots 4 --tank heavy --screenshot $W/build/shots/t4/room-ffa.png --screenshot-at 4 --quit-after 6
$S/godot_run.sh $W "" 20 -- --no-update --sandbox --screenshot $W/build/shots/t4/sandbox-in-main.png --screenshot-at 4 --quit-after 6
```

两个实例（A 开房、B 加入，人齐自动开局），以及端口被占（A 占着 27017 时 B 也开房）：

```bash
$S/pair4b.sh $W $W/build/shots/t4 two 30 "--no-update --name A --host --port 27016 --players 2 --autostart --quit-after 20 --screenshot $W/build/shots/t4/two-a.png --screenshot-at 12" "--no-update --name B --join 127.0.0.1:27016 --quit-after 15 --screenshot $W/build/shots/t4/two-b.png --screenshot-at 10"
$S/pair4b.sh $W $W/build/shots/t4 busy 20 "--no-update --host --port 27017 --quit-after 12" "--no-update --host --port 27017 --screenshot $W/build/shots/t4/port-busy.png --screenshot-at 3 --quit-after 5"
pgrep -fil godot
```

Expected：
- `menu.png`：主菜单，开房间、试驾场按钮，右侧局域网房间列表和手填地址框，版本号。
- `room-team.png`：团队对战房间，相对阵营两栏（我方蓝灰、敌方红），机器人“开局时补入”占位行，车型四选一，开始按钮旁写着不能开始的原因或可以开始。
- `room-ffa.png`：各自为战单栏，重坦被选中。
- `sandbox-in-main.png`：从 `Main` 挂上的试驾场。
- `two-a.png`、`two-b.png`：两端都进入对局占位画面；`two-host.log`、`two-guest.log` 里都有进入对局的日志行。
- `port-busy.png`：菜单状态栏红字写端口被占。
- 收尾时 `pgrep -fil godot` 为空。

- [ ] **Step 6：提交**

```bash
git add -A src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): the main menu (host, join by LAN list or address, sandbox) and the room screen (mode, bots, tank, relative teams, ready, start with the reason it can't); Main runs menu → room → match placeholder → room on the battle stack"
```

---

### Task 5：对局场景核心

蓝图 §3.2、§3.3、§5 T5。`Match/MatchMain`（+ `.Events`、`.Camera`）严格按蓝图 §3.2 的每帧流程：回环时固定 1/60 虚拟步长；`Peek` → 一次 `client.Update` → 仿真 tick 推进了才 `EndTick`；`Field` 换实例就 `SetField`；第一份快照到达前只显示“对局加载中…”（R3）。`TankActors`：别人的坦克按 id 懒建、按阵营上色、不在视野立即隐藏，残骸由 `WreckTracker` 按“事件为准、状态 0.3 秒兜底”处理（R5）；隐藏后重新进入视野的车按首见处理（视野外发生的击毁、复活直接到位，转向速率从 0 开始）；头顶血条按建立顺序分给本帧可见的车。自己开炮用 `ShotCount` 增量零延迟出火光。`MatchInput`、`Logic/{MatchColors,RenderMath,WreckTracker}`、最小的 `MatchAutomation`（`--autodrive`、`--fps-log`）。**`Tank.Net` 的 `ClockSync` 修复**：场景构建卡一帧后客户端预测时钟落后服务器，输入全被当成迟到、服务器按空输入模拟，而 slack 反馈为 0 不触发重同步；现在服务器 tick 追上客户端时硬重同步。4A 的 `ComponentTests` 里两条 ClockSync 测试原来把循环下标当服务器 tick 喂，有了硬重同步后是“凑巧通过”，改成服务器 tick 始终远低于客户端，各自走回原来的路径并断言。

**Files:**
- Create: `src/Tank.Client/scripts/Match/{MatchMain,MatchMain.Events,MatchMain.Camera,TankActors,MatchInput,MatchAutomation}.cs`、`Match/Logic/{MatchColors,RenderMath,WreckTracker}.cs`
- Modify: `Bootstrap/{Main,Main.Flow}.cs`、`src/Tank.Net/Client/ClockSync.cs`
- Test: 新建 `tests/Tank.Art.Tests/{MatchColorsTests,RenderMathTests,WreckTrackerTests}.cs`；修改 `tests/Tank.Net.Tests/Battle/ClockSync30Tests.cs`、`tests/Tank.Net.Tests/ComponentTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/MatchColorsTests.cs`：

```csharp
using Tank.Client.Art;
using Tank.Client.Match;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class MatchColorsTests
{
    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    public void The_own_tank_is_own_in_every_mode(BattleMode mode) =>
        Assert.Equal(Side.Own, MatchColors.SideOf(mode, ownTeam: 1, team: 1, isOwn: true));

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    public void Same_team_not_own_is_an_ally_in_a_team_mode(BattleMode mode) =>
        Assert.Equal(Side.Ally, MatchColors.SideOf(mode, ownTeam: 1, team: 1, isOwn: false));

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    public void The_other_team_is_the_enemy(BattleMode mode) =>
        Assert.Equal(Side.Enemy, MatchColors.SideOf(mode, ownTeam: 0, team: 1, isOwn: false));

    [Fact]
    public void In_a_free_for_all_everyone_else_is_an_enemy_even_with_an_equal_team_number() =>
        Assert.Equal(Side.Enemy, MatchColors.SideOf(BattleMode.FreeForAll, ownTeam: 3, team: 3, isOwn: false));

    [Fact]
    public void Body_colours_follow_the_palette_and_differ_per_side()
    {
        Assert.Equal(Palette.TankBody, MatchColors.Body(Side.Own));
        Assert.Equal(Palette.AllyBody, MatchColors.Body(Side.Ally));
        Assert.Equal(Palette.EnemyBody, MatchColors.Body(Side.Enemy));
        Assert.NotEqual(MatchColors.Body(Side.Own), MatchColors.Body(Side.Ally));
        Assert.NotEqual(MatchColors.Body(Side.Own), MatchColors.Body(Side.Enemy));
        Assert.NotEqual(MatchColors.Body(Side.Ally), MatchColors.Body(Side.Enemy));
    }

    [Fact]
    public void Ui_colours_differ_per_side()
    {
        Assert.NotEqual(MatchColors.Ui(Side.Own), MatchColors.Ui(Side.Ally));
        Assert.NotEqual(MatchColors.Ui(Side.Own), MatchColors.Ui(Side.Enemy));
        Assert.NotEqual(MatchColors.Ui(Side.Ally), MatchColors.Ui(Side.Enemy));
    }

    [Fact]
    public void Paint_puts_the_side_colour_on_the_body_over_the_common_tracks_and_hubs()
    {
        var paint = MatchColors.Paint(Side.Enemy);
        Assert.Equal(Palette.EnemyBody, paint.Body);
        Assert.Equal(Palette.Track, paint.Track);
        Assert.Equal(Palette.TankHub, paint.Hub);
    }
}
```

新建 `tests/Tank.Art.Tests/RenderMathTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Art.Tests;

public class RenderMathTests
{
    private static readonly TankState Prev = new()
    {
        Id = 7, Spec = TankCatalog.Medium, Position = new Vector2(10f, 20f), Yaw = 0.2f, TurretYaw = -0.5f, GunPitch = 0.05f, Speed = 2f, Health = 500, Bloom = 0.1f,
    };

    private static readonly TankState Cur = Prev with
    {
        Position = new Vector2(12f, 24f), Yaw = 0.4f, TurretYaw = -0.3f, GunPitch = 0.15f, Speed = 4f, Health = 480, Bloom = 0.3f,
    };

    [Fact]
    public void OwnDisplay_at_alpha_0_is_the_previous_tick_apart_from_the_position()
    {
        var at = new Vector2(11f, 22.5f);
        TankState shown = RenderMath.OwnDisplay(Prev, Cur, 0f, at);
        Assert.Equal(at, shown.Position);
        Assert.Equal(Prev.Yaw, shown.Yaw, 5);
        Assert.Equal(Prev.TurretYaw, shown.TurretYaw, 5);
        Assert.Equal(Prev.GunPitch, shown.GunPitch, 5);
        Assert.Equal(Prev.Speed, shown.Speed, 5);
        Assert.Equal(Cur.Health, shown.Health);   // discrete fields are the latest tick's
    }

    [Fact]
    public void OwnDisplay_at_alpha_1_is_the_current_tick_apart_from_the_position()
    {
        var at = new Vector2(0f, 0f);
        TankState shown = RenderMath.OwnDisplay(Prev, Cur, 1f, at);
        Assert.Equal(at, shown.Position);
        Assert.Equal(Cur.Yaw, shown.Yaw, 5);
        Assert.Equal(Cur.TurretYaw, shown.TurretYaw, 5);
        Assert.Equal(Cur.GunPitch, shown.GunPitch, 5);
        Assert.Equal(Cur.Speed, shown.Speed, 5);
    }

    [Fact]
    public void YawRate_is_the_turn_over_the_time()
    {
        Assert.Equal(1.5f, RenderMath.YawRate(0.1f, 0.4f, 0.2f), 4);
        Assert.Equal(-1.5f, RenderMath.YawRate(0.4f, 0.1f, 0.2f), 4);
    }

    [Fact]
    public void YawRate_takes_the_shorter_arc_across_plus_minus_pi()
    {
        float rate = RenderMath.YawRate(Angles.Pi - 0.1f, -Angles.Pi + 0.1f, 1f);
        Assert.Equal(0.2f, rate, 4);   // a 0.2 rad turn left, not a 6.08 rad turn right
    }

    [Fact]
    public void YawRate_is_clamped()
    {
        Assert.Equal(RenderMath.MaxYawRate, RenderMath.YawRate(0f, 2f, 0.01f));
        Assert.Equal(-RenderMath.MaxYawRate, RenderMath.YawRate(2f, 0f, 0.01f));
    }

    [Fact]
    public void YawRate_is_zero_when_no_time_passed()
    {
        Assert.Equal(0f, RenderMath.YawRate(0f, 1f, 0f));
        Assert.Equal(0f, RenderMath.YawRate(0f, 1f, -0.1f));
    }

    [Fact]
    public void FromRemote_maps_the_entity_and_the_interpolated_pose()
    {
        var entity = new BattleEntity { Id = 42, Slot = 3, Spec = 2, Team = 1, Health = 321, Flags = TankFlags.Destroyed | TankFlags.Protected };
        var remote = new RemoteTank(entity, new Vector2(5f, -6f), Yaw: 1.1f, TurretYaw: -0.7f, GunPitch: 0.12f, Speed: 3.5f, Extrapolated: false);

        TankState state = RenderMath.FromRemote(remote);

        Assert.Equal(42, state.Id);
        Assert.Same(TankCatalog.All[2], state.Spec);
        Assert.Equal(1, state.Team);
        Assert.Equal(new Vector2(5f, -6f), state.Position);
        Assert.Equal(1.1f, state.Yaw);
        Assert.Equal(-0.7f, state.TurretYaw);
        Assert.Equal(0.12f, state.GunPitch);
        Assert.Equal(3.5f, state.Speed);
        Assert.Equal(321, state.Health);
        Assert.True(state.IsDestroyed);
        Assert.True(state.ProtectedUntil > 0);
    }

    [Fact]
    public void FromRemote_leaves_an_intact_unprotected_tank_so()
    {
        var entity = new BattleEntity { Id = 1, Spec = 0, Health = 600, Flags = TankFlags.None };
        TankState state = RenderMath.FromRemote(new RemoteTank(entity, Vector2.Zero, 0f, 0f, 0f, 0f, false));
        Assert.False(state.IsDestroyed);
        Assert.Equal(0, state.ProtectedUntil);
    }

    [Fact]
    public void ShotDetector_fires_once_per_increase_and_not_at_first_sight()
    {
        var shots = new ShotDetector();
        Assert.False(shots.Update(3, tankId: 1));   // the first reading only sets the baseline
        Assert.False(shots.Update(3, tankId: 1));
        Assert.True(shots.Update(4, tankId: 1));
        Assert.False(shots.Update(4, tankId: 1));   // the same count again: no new shot
        Assert.True(shots.Update(6, tankId: 1));    // two shots in one frame: one flash
    }

    [Fact]
    public void ShotDetector_ignores_a_decrease_then_tracks_the_new_count()
    {
        var shots = new ShotDetector();
        shots.Update(5, tankId: 1);
        Assert.False(shots.Update(0, tankId: 1));   // a respawn reset the count
        Assert.True(shots.Update(1, tankId: 1));
    }

    [Fact]
    public void ShotDetector_starts_afresh_on_another_tank_id()
    {
        var shots = new ShotDetector();
        shots.Update(0, tankId: 1);
        Assert.False(shots.Update(9, tankId: 2));   // a new tank with nine shots behind it is not nine flashes
        Assert.True(shots.Update(10, tankId: 2));
    }
}
```

新建 `tests/Tank.Art.Tests/WreckTrackerTests.cs`：

```csharp
using Tank.Client.Match;

namespace Tank.Art.Tests;

public class WreckTrackerTests
{
    private const float Frame = 1f / 60;

    [Fact]
    public void The_destroyed_event_wrecks_at_once_and_only_once()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.Wreck, tracker.Destroyed());
        Assert.True(tracker.Wrecked);
        Assert.Equal(WreckTracker.Change.None, tracker.Destroyed());
    }

    [Fact]
    public void The_revived_event_restores_a_wreck_and_does_nothing_to_an_intact_tank()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.None, tracker.Revived());
        tracker.Destroyed();
        Assert.Equal(WreckTracker.Change.Restore, tracker.Revived());
        Assert.False(tracker.Wrecked);
    }

    [Fact]
    public void A_destroyed_flag_waits_for_the_event_then_settles_the_wreck_after_the_fallback_time()
    {
        var tracker = new WreckTracker();
        float waited = 0f;
        while (waited + Frame <= WreckTracker.FallbackSeconds)
        {
            Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, Frame));
            waited += Frame;
        }
        Assert.Equal(WreckTracker.Change.Settle, tracker.Observe(destroyed: true, Frame));
        Assert.True(tracker.Wrecked);
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, Frame));
    }

    [Fact]
    public void The_event_arriving_during_the_wait_wins_with_the_full_kill()
    {
        var tracker = new WreckTracker();
        tracker.Observe(destroyed: true, 0.1f);
        Assert.Equal(WreckTracker.Change.Wreck, tracker.Destroyed());
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, Frame));
    }

    [Fact]
    public void A_tank_first_seen_destroyed_settles_at_once()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.Settle, tracker.Observe(destroyed: true, Frame, firstSight: true));
    }

    [Fact]
    public void A_tank_first_seen_intact_changes_nothing()
    {
        var tracker = new WreckTracker();
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: false, Frame, firstSight: true));
        Assert.False(tracker.Wrecked);
    }

    [Fact]
    public void An_intact_flag_on_a_wreck_restores_after_the_fallback_time_a_respawn_out_of_sight()
    {
        var tracker = new WreckTracker();
        tracker.Destroyed();
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: false, 0.2f));
        Assert.Equal(WreckTracker.Change.Restore, tracker.Observe(destroyed: false, 0.2f));
        Assert.False(tracker.Wrecked);
    }

    [Fact]
    public void Agreement_resets_the_wait()
    {
        var tracker = new WreckTracker();
        tracker.Observe(destroyed: true, 0.2f);
        tracker.Observe(destroyed: false, Frame);   // a flicker: back in agreement
        Assert.Equal(WreckTracker.Change.None, tracker.Observe(destroyed: true, 0.2f));   // the clock started again
    }
}
```

修改 `tests/Tank.Net.Tests/Battle/ClockSync30Tests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Net.Tests/Battle/ClockSync30Tests.cs b/tests/Tank.Net.Tests/Battle/ClockSync30Tests.cs
index ae38df7..0a4493e 100644
--- a/tests/Tank.Net.Tests/Battle/ClockSync30Tests.cs
+++ b/tests/Tank.Net.Tests/Battle/ClockSync30Tests.cs
@@ -49,13 +49,30 @@ public sealed class ClockSync30Tests
         Assert.Equal(start + 22, clock.Tick);   // … and the other 22 skipped
     }
 
+    [Fact]
+    public void A_client_the_server_has_caught_up_with_resyncs_ahead_even_when_the_slack_feedback_is_quiet()
+    {
+        // A long frame the engine's clamped delta never made up for: the client's tick is behind the server's, every
+        // input it sends is late and rejected, and the server's slack reads 0 (nothing received), close to the target.
+        var clock = new ClockSync(30);
+        clock.OnServerFeedback(2, 100, 0.0);
+        Assert.Equal(102, clock.Tick);
+        clock.OnServerFeedback(0, 101, 0.0);   // still ahead by one: a normal frame
+        Assert.Equal(0, clock.HardResyncs);
+        clock.OnServerFeedback(0, 102, 0.0);   // the server reached the client's tick
+        Assert.Equal(1, clock.HardResyncs);
+        Assert.Equal(104, clock.Tick);         // ahead again by the two ticks of slack
+    }
+
     [Fact]
     public void Slack_feedback_far_from_the_target_resyncs_at_20_ticks_which_is_two_thirds_of_a_second()
     {
         var clock = new ClockSync(30);
         clock.OnServerFeedback(2, 100, 0.05);
+        RunSecond(clock);   // the client keeps ticking along with the server
         clock.OnServerFeedback(2 + 20, 130, 0.05);   // exactly 20 off the target: not yet
         Assert.Equal(0, clock.HardResyncs);
+        RunSecond(clock);
         clock.OnServerFeedback(2 + 21, 160, 0.05);
         Assert.Equal(1, clock.HardResyncs);
         Assert.Equal(160 + 4, clock.Tick);   // ceil(0.05 * 30 + 2) = 4 ticks of lead
@@ -71,4 +88,11 @@ public sealed class ClockSync30Tests
         for (int i = 0; i < 40; i++) early.OnServerFeedback(-4, 100 + i, 0.05);
         Assert.InRange(early.Rate, 1.001, 1.06);
     }
+
+    /// <summary>A second of frames as the client runs them: the ticks Advance asks for are each taken with NextTick.</summary>
+    private static void RunSecond(ClockSync clock)
+    {
+        int ticks = clock.Advance(1.0);
+        for (int i = 0; i < ticks; i++) clock.NextTick();
+    }
 }
PATCH
```

修改 `tests/Tank.Net.Tests/ComponentTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Net.Tests/ComponentTests.cs b/tests/Tank.Net.Tests/ComponentTests.cs
index 5d5b543..e673586 100644
--- a/tests/Tank.Net.Tests/ComponentTests.cs
+++ b/tests/Tank.Net.Tests/ComponentTests.cs
@@ -79,24 +79,29 @@ public sealed class ClockSyncTests
     [Fact]
     public void LowSlack_SpeedsUp_HighSlack_SlowsDown()
     {
+        // The server's ticks stay well below the client's, as they do while the client leads: only the slack moves the
+        // rate (a server tick at or past the client's would take the "fell behind" hard resync instead).
         var clock = new ClockSync();
-        clock.Initialize(0, 0);
+        clock.Initialize(1000, 0);
         for (int i = 0; i < 50; i++) clock.OnServerFeedback(-1, i, 0);
         Assert.True(clock.Rate > 1.0);
 
-        for (int i = 0; i < 100; i++) clock.OnServerFeedback(8, i, 0);
+        for (int i = 0; i < 100; i++) clock.OnServerFeedback(8, 50 + i, 0);
         Assert.True(clock.Rate < 1.0);
         Assert.InRange(clock.Rate, 0.95, 1.05);
+        Assert.Equal(0, clock.HardResyncs);   // all of it on the dilation path
     }
 
     [Fact]
     public void HugeError_HardResyncs()
     {
         var clock = new ClockSync();
-        clock.Initialize(0, 0);
-        clock.OnServerFeedback(-100, 500, 0);
+        clock.Initialize(500, 0);              // the client at 502
+        clock.OnServerFeedback(2, 490, 0);     // the same server tick with the slack on target: the server isn't caught up…
+        Assert.Equal(0, clock.HardResyncs);
+        clock.OnServerFeedback(-100, 490, 0);  // … so this resync is the slack's alone
         Assert.Equal(1, clock.HardResyncs);
-        Assert.Equal(502, clock.Tick);
+        Assert.Equal(492, clock.Tick);
     }
 
     [Fact]
PATCH
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Run: `dotnet test tests/Tank.Net.Tests 2>&1 | grep -E "通过!|失败!|\[FAIL\]"`
Expected: Art 编译失败，命名空间 `Tank.Client.Match` 找不到；Net 能编译，1 条失败：`ClockSync30Tests.A_client_the_server_has_caught_up_with_resyncs_ahead_even_when_the_slack_feedback_is_quiet`（还没有硬重同步），其余 209 条通过（`ComponentTests` 改写后的两条在旧代码上也通过，它们测的是原来的路径）。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
index fc43fe8..e065bc1 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
@@ -1,9 +1,9 @@
 using System.Net.NetworkInformation;
 using System.Net.Sockets;
 using Godot;
+using Tank.Client.Match;
 using Tank.Client.Online;
 using Tank.Client.Sandbox;
-using Tank.Client.UI;
 using Tank.Net.Battle;
 using Tank.Sim.Modes;
 
@@ -98,14 +98,18 @@ public partial class Main
         _room.Refresh();
     }
 
-    /// <summary>The match began. T5 puts the match scene here; for now a dark placeholder and one log line.</summary>
+    /// <summary>The match began: the match scene takes the session (and the client's Update) until it reports <see cref="MatchMain.Finished"/>.</summary>
     private void EnterMatch(BattleClient client)
     {
         _menu.Visible = false;
         _room.Visible = false;
-        string tank = client.HasTank ? client.Own.Spec.Id : "?";
-        GD.Print($"[match] entered match {client.Room?.Mode} as slot {client.Slot}, tank {tank}");
-        _matchView = BuildMatchPlaceholder();
+        GD.Print($"[match] entered match {client.Room?.Mode} as slot {client.Slot}");
+        var match = new MatchMain();
+        match.Setup(_session!, _options);
+        match.Finished += LeaveMatch;
+        _matchView = match;
+        AddChild(match);
+        MoveChild(match, 0);   // the 3D world behind the UI screens
     }
 
     /// <summary>The match is over (or the session ended): its scene goes, and the launch options may start the next one.</summary>
@@ -114,25 +118,10 @@ public partial class Main
         _matchView?.QueueFree();
         _matchView = null;
         Input.MouseMode = Input.MouseModeEnum.Visible;
+        GetWindow().Title = AppTitle;
         _auto?.Reset();
     }
 
-    /// <summary>A dark full-window label standing in for the match scene; already added to the tree.</summary>
-    private Control BuildMatchPlaceholder()
-    {
-        var view = new Control { Theme = UiKit.Theme };
-        AddChild(view);   // in the tree first: FillViewport reads the viewport
-        UiKit.FillViewport(view);
-        var shade = new ColorRect { Color = UiKit.Background, MouseFilter = Control.MouseFilterEnum.Ignore };
-        view.AddChild(shade);
-        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
-        Label label = UiKit.Label("对局画面（T5）", 48, UiKit.Dim, HorizontalAlignment.Center);
-        label.VerticalAlignment = VerticalAlignment.Center;
-        view.AddChild(label);
-        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
-        return view;
-    }
-
     private void EnterSandbox()
     {
         if (_sandbox is not null) return;
PATCH
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.cs b/src/Tank.Client/scripts/Bootstrap/Main.cs
index e867636..e53df5e 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.cs
@@ -1,4 +1,5 @@
 using Godot;
+using Tank.Client.Match;
 using Tank.Client.Online;
 using Tank.Client.Sandbox;
 using Tank.Client.UI;
@@ -23,7 +24,7 @@ public partial class Main : Node
     private BattleSession? _session;
     private AutoRoom? _auto;
     private SandboxMain? _sandbox;
-    private Control? _matchView;
+    private MatchMain? _matchView;
     private double _time;
     private bool _screenshotTaken;
 
@@ -82,9 +83,13 @@ public partial class Main : Node
     {
         BattleSession session = _session!;
         BattleClient client = session.Client;
-        double dt = session.IsVirtualClock ? VirtualFrame : delta;
-        session.Advance(dt);
-        client.Update(dt, LocalCommand.None);   // T5: the match scene takes over Advance and Update while it exists
+        if (_matchView is null)
+        {
+            // The match scene owns Advance and Update while it exists: the client must see exactly one Update per frame (R1).
+            double dt = session.IsVirtualClock ? VirtualFrame : delta;
+            session.Advance(dt);
+            client.Update(dt, LocalCommand.None);
+        }
         switch (client.Phase)
         {
             case BattlePhase.Connecting:
@@ -94,7 +99,7 @@ public partial class Main : Node
                 ShowRoom(client);
                 break;
             case BattlePhase.InMatch:
-                if (_matchView is null && client.HasTank) EnterMatch(client);   // the match start has set up the own tank by now
+                if (_matchView is null) EnterMatch(client);   // the scene shows "loading" until the first snapshot brings the own tank
                 break;
             case BattlePhase.Disconnected:
                 LeaveSession(client.DisconnectReason is { } reason ? $"连接已断开：{DescribeDisconnect(reason)}" : "连接已断开。", error: true);
PATCH
```

新建 `src/Tank.Client/scripts/Match/Logic/MatchColors.cs`：

```csharp
using System.Numerics;
using Tank.Client.Art;
using Tank.Client.Vehicles;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>Whose a tank is, seen from the player's seat.</summary>
public enum Side : byte { Own, Ally, Enemy }

/// <summary>
/// Relative colours in a match (blueprint §4.1): the player's own tank is toy orange, teammates blue-grey like the
/// practice targets, everyone else red. Which side a tank is on depends on the mode: in a free-for-all everyone else is
/// an enemy even if the server's team numbers happen to coincide.
/// </summary>
public static class MatchColors
{
    /// <param name="ownTeam">The player's team as the scoreboard has it.</param>
    /// <param name="team">The other tank's team.</param>
    /// <param name="isOwn">Whether this is the player's own tank.</param>
    public static Side SideOf(BattleMode mode, byte ownTeam, byte team, bool isOwn)
    {
        if (isOwn) return Side.Own;
        return ModeRules.HasTeams(mode) && team == ownTeam ? Side.Ally : Side.Enemy;
    }

    /// <summary>The hull, turret and gun colour of a tank on <paramref name="side"/>.</summary>
    public static Vector3 Body(Side side) => side switch
    {
        Side.Own => Palette.TankBody,
        Side.Ally => Palette.AllyBody,
        _ => Palette.EnemyBody,
    };

    /// <summary>The colour of a tank's name and other HUD text about it.</summary>
    public static Vector3 Ui(Side side) => side switch
    {
        Side.Own => Palette.UiAccent,
        Side.Ally => Palette.AllyBody,
        _ => Palette.UiDanger,
    };

    /// <summary>The paint a <c>TankModel</c> is built with: the side's body colour over the common tracks and hubs.</summary>
    public static TankPaint Paint(Side side) => new(Body(side), Palette.Track, Palette.TankHub);
}
```

新建 `src/Tank.Client/scripts/Match/Logic/RenderMath.cs`：

```csharp
using System.Numerics;
using Tank.Client.Sandbox;
using Tank.Net.Battle;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// The sums between what <c>BattleClient</c> knows and what the match scene draws (blueprint §3.2): the own tank
/// between its last two predicted ticks at the position the client wants it drawn (prediction plus the correction being
/// smoothed away), a track-animation yaw rate from two headings, and another tank's wire data as a <see cref="TankState"/>.
/// </summary>
public static class RenderMath
{
    /// <summary>The yaw rate the tracks animate at is clamped to this, rad/s: a snap or respawn must not spin them.</summary>
    public const float MaxYawRate = 3f;

    /// <summary>The own tank <paramref name="alpha"/> of the way from <paramref name="prev"/> to <paramref name="cur"/>, drawn at <paramref name="renderPos"/>.</summary>
    public static TankState OwnDisplay(in TankState prev, in TankState cur, float alpha, Vector2 renderPos) =>
        TankInterpolation.Between(prev, cur, alpha) with { Position = renderPos };

    /// <summary>The turn rate from <paramref name="prevYaw"/> to <paramref name="yaw"/> over <paramref name="dt"/> s, along the shorter arc, clamped to ±<see cref="MaxYawRate"/>; 0 when no time passed.</summary>
    public static float YawRate(float prevYaw, float yaw, float dt)
    {
        if (dt <= 0f) return 0f;
        return System.Math.Clamp(Angles.Wrap(yaw - prevYaw) / dt, -MaxYawRate, MaxYawRate);
    }

    /// <summary>Another tank as a <see cref="TankState"/>: the pose from the interpolator, spec, team, health and flags from its entity. What the wire doesn't carry (reload, ammunition) stays zero.</summary>
    public static TankState FromRemote(in RemoteTank r)
    {
        BattleEntity e = r.Entity;
        return new TankState
        {
            Id = e.Id,
            Spec = TankCatalog.All[e.Spec],
            Team = e.Team,
            Position = r.Position,
            Yaw = r.Yaw,
            TurretYaw = r.TurretYaw,
            GunPitch = r.GunPitch,
            Speed = r.Speed,
            Health = e.Health,
            IsDestroyed = e.Has(TankFlags.Destroyed),
            ProtectedUntil = e.Has(TankFlags.Protected) ? int.MaxValue : 0,
            LastFired = TankState.NeverFired,
        };
    }
}

/// <summary>
/// Notices the own tank firing from its predicted <c>ShotCount</c> (blueprint §3.3): a predicted shot never reaches the
/// event queue, so the muzzle flash, recoil and camera shake key off the count going up. Fires once per increase, never
/// on a decrease (a respawn resets the count), and starts afresh on another tank id.
/// </summary>
public sealed class ShotDetector
{
    private int _tankId;
    private int _count;
    private bool _primed;

    /// <returns>True once when <paramref name="shotCount"/> rose since the last call for the same <paramref name="tankId"/>.</returns>
    public bool Update(int shotCount, int tankId)
    {
        if (!_primed || tankId != _tankId)
        {
            (_tankId, _count, _primed) = (tankId, shotCount, true);
            return false;
        }
        bool fired = shotCount > _count;
        _count = shotCount;
        return fired;
    }
}
```

新建 `src/Tank.Client/scripts/Match/Logic/WreckTracker.cs`：

```csharp
namespace Tank.Client.Match;

/// <summary>
/// Decides when a tank's model turns into a wreck or back (blueprint §3.3, R5): the event is the word, the snapshot's
/// flag the fallback. A <c>TankDestroyed</c> or <c>TankRevived</c> event changes the look at once; a flag that has
/// disagreed with the look for longer than <see cref="FallbackSeconds"/> without an event (a tank first seen as a wreck,
/// a lost event, a respawn out of sight) changes it quietly, with no blast and no turret flight.
/// </summary>
public sealed class WreckTracker
{
    /// <summary>How long the snapshot may disagree with the look before it wins: the events run about 100 ms behind the state.</summary>
    public const float FallbackSeconds = 0.3f;

    private float _disagreed;   // seconds the state has disagreed with the look

    public enum Change : byte
    {
        None,
        /// <summary>The kill, seen: turret flight, charring, smoke and a blast.</summary>
        Wreck,
        /// <summary>A wreck already lying there: straight to the settled look, no blast.</summary>
        Settle,
        /// <summary>Back to an intact tank.</summary>
        Restore,
    }

    /// <summary>Whether the model is a wreck now.</summary>
    public bool Wrecked { get; private set; }

    /// <summary>A <c>TankDestroyed</c> event for this tank.</summary>
    public Change Destroyed()
    {
        _disagreed = 0f;
        if (Wrecked) return Change.None;
        Wrecked = true;
        return Change.Wreck;
    }

    /// <summary>A <c>TankRevived</c> event for this tank.</summary>
    public Change Revived()
    {
        _disagreed = 0f;
        if (!Wrecked) return Change.None;
        Wrecked = false;
        return Change.Restore;
    }

    /// <summary>
    /// The snapshot's flag this frame. A disagreement is tolerated for <see cref="FallbackSeconds"/> (the event is on its
    /// way), then the state wins; with <paramref name="firstSight"/> (the tank wasn't drawn the frame before: never seen,
    /// or back in sight) it wins at once.
    /// </summary>
    public Change Observe(bool destroyed, float dt, bool firstSight = false)
    {
        if (destroyed == Wrecked)
        {
            _disagreed = 0f;
            return Change.None;
        }
        _disagreed += dt;
        if (!firstSight && _disagreed <= FallbackSeconds) return Change.None;
        _disagreed = 0f;
        Wrecked = destroyed;
        return destroyed ? Change.Settle : Change.Restore;
    }
}
```

新建 `src/Tank.Client/scripts/Match/MatchAutomation.cs`：

```csharp
using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Net.Battle;

namespace Tank.Client.Match;

/// <summary>
/// A match's scripted runs, the minimum for repeatable screenshots: a fixed <c>--camera-preset</c> (nobody drives),
/// <c>--autodrive</c> (full throttle, steering right part of the time so it drives loops, firing whenever the gun is
/// roughly level) and <c>--fps-log</c> (a line every <see cref="FpsLogSeconds"/>). Any of these, or a screenshot or
/// timed quit, makes the run ignore the mouse and keyboard. The screenshot and quit timers stay with <c>Main</c> until the
/// capture task moves them here.
/// </summary>
public sealed class MatchAutomation
{
    private const float AutoCameraYaw = 0.35f;      // --autodrive: the camera looks this far off the hull's heading, rad
    private const float AutoCameraPitch = 0.18f;
    private const double AutoTurnRate = 0.4;        // --autodrive: steers right while sin(rate × time) is above the threshold
    private const double AutoTurnThreshold = 0.5;
    private const float LevelPitch = 0.2f;          // --autodrive: fires while the aim is within this of level, rad
    private const double FpsLogSeconds = 5.0;

    private readonly LaunchOptions _options;
    private double _fpsElapsed;
    private int _fpsFrames;
    private double _fpsWorst;

    public MatchAutomation(LaunchOptions options)
    {
        _options = options;
        HasPreset = CameraPresets.IsKnown(options.CameraPreset);
    }

    /// <summary>A known <c>--camera-preset</c> is on: the camera is fixed and the tank stands still.</summary>
    public bool HasPreset { get; }

    public string? Preset => _options.CameraPreset;

    /// <summary>Whether a script drives the tank (<c>--autodrive</c>).</summary>
    public bool Drives => _options.AutoDrive;

    /// <summary>Scripted and captured runs ignore stray input so screenshots stay repeatable.</summary>
    public bool IgnoresInput => HasPreset || _options.AutoDrive || _options.ScreenshotPath is not null || _options.QuitAfter > 0;

    /// <summary>Warns about launch options that name no known preset.</summary>
    public void ReportBadOptions()
    {
        if (_options.CameraPreset is { } preset && !HasPreset)
            GD.PushWarning($"Unknown --camera-preset '{preset}'; use {string.Join(", ", CameraPresets.Names)}.");
    }

    /// <summary>The command for this frame: <paramref name="player"/>'s own, or the script's in its place.</summary>
    public LocalCommand Command(in LocalCommand player, double time)
    {
        if (_options.AutoDrive)
            return player with
            {
                Throttle = 1,
                Steer = (sbyte)(Math.Sin(time * AutoTurnRate) > AutoTurnThreshold ? 1 : 0),
                Fire = Mathf.Abs(player.AimPitch) < LevelPitch,
            };
        if (HasPreset) return player with { Throttle = 0, Steer = 0, Fire = false };
        return player;
    }

    /// <summary>Where a scripted run points the orbit camera, or null to leave it to the player.</summary>
    public (float Yaw, float Pitch)? CameraAngles(float tankYaw) =>
        _options.AutoDrive ? (tankYaw + AutoCameraYaw, AutoCameraPitch) : null;

    /// <summary>Call once per frame: prints the average frame rate and the worst frame every <see cref="FpsLogSeconds"/> with <c>--fps-log</c>.</summary>
    public void EndFrame(double delta)
    {
        if (!_options.FpsLog) return;
        _fpsElapsed += delta;
        _fpsFrames++;
        _fpsWorst = Math.Max(_fpsWorst, delta);
        if (_fpsElapsed < FpsLogSeconds) return;
        GD.Print($"[fps] {_fpsFrames / _fpsElapsed:0.0} fps over {_fpsElapsed:0.0} s, worst frame {_fpsWorst * 1000:0.0} ms");
        _fpsElapsed = 0;
        _fpsFrames = 0;
        _fpsWorst = 0;
    }
}
```

新建 `src/Tank.Client/scripts/Match/MatchInput.cs`：

```csharp
using Godot;
using Tank.Client.Driving;

namespace Tank.Client.Match;

/// <summary>
/// A match's input: everything in <see cref="DriveInput"/> plus Tab held for the scoreboard, F3 to toggle the network
/// panel (both already in project.godot) and the spectating keys Q (previous), E (next) and F (free camera), each
/// consumed once by the frame that reads it (the spectating itself is wired in a later task).
/// </summary>
public sealed class MatchInput : DriveInput
{
    private static readonly StringName Scoreboard = "scoreboard", ToggleNetPanel = "toggle_netgraph";
    private static readonly StringName SpectatePrev = "spectate_prev", SpectateNext = "spectate_next", SpectateFree = "spectate_free";

    private int _spectateStep;
    private bool _spectateFree;

    public static new void RegisterActions()
    {
        DriveInput.RegisterActions();
        AddKey(SpectatePrev, Key.Q);
        AddKey(SpectateNext, Key.E);
        AddKey(SpectateFree, Key.F);
    }

    /// <summary>Whether Tab is down: the scoreboard shows while it is.</summary>
    public bool ScoreboardHeld => Input.IsActionPressed(Scoreboard);

    /// <summary>Whether the F3 network panel is open; starts as <c>--netgraph</c> says.</summary>
    public bool NetPanelOpen { get; set; }

    public override void OnInputEvent(InputEvent e)
    {
        base.OnInputEvent(e);
        if (e is not InputEventKey) return;   // every action below is bound to a key; mouse motion must not pay the lookups
        if (e.IsActionPressed(ToggleNetPanel)) NetPanelOpen = !NetPanelOpen;
        else if (e.IsActionPressed(SpectatePrev)) _spectateStep = -1;
        else if (e.IsActionPressed(SpectateNext)) _spectateStep = 1;
        else if (e.IsActionPressed(SpectateFree)) _spectateFree = true;
    }

    /// <summary>Once after Q or E: -1 for the previous spectating target, 1 for the next, 0 for neither.</summary>
    public int TakeSpectateStep()
    {
        int step = _spectateStep;
        _spectateStep = 0;
        return step;
    }

    /// <summary>True once after F was pressed.</summary>
    public bool TakeSpectateFree()
    {
        bool free = _spectateFree;
        _spectateFree = false;
        return free;
    }
}
```

新建 `src/Tank.Client/scripts/Match/MatchMain.Camera.cs`：

```csharp
using Godot;
using Tank.Client.Cameras;
using Tank.Client.Driving;
using Tank.Client.World;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// Camera, aim and the mouse (blueprint R14), as in the sandbox: the orbit camera follows the own tank and the gun is
/// laid where it looks, unless a fixed preset frames the shot or the player holds free look; a wreck's gun stays put.
/// The mouse is captured on entering (not in scripted or captured runs), let go by Esc or losing focus, and taken
/// again by a click. Spectating and the pause layer come with a later task.
/// </summary>
public partial class MatchMain
{
    private const float SniperEyeAboveTrunnion = 0.45f;

    public override void _Input(InputEvent e)
    {
        if (_automation.IgnoresInput) return;
        // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
        if (e.IsActionReleased(DriveInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
    }

    public override void _Notification(int what)
    {
        if (what != NotificationApplicationFocusOut) return;
        Input.MouseMode = Input.MouseModeEnum.Visible;
        _input.ReleaseAll();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (_automation.IgnoresInput) return;
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured;
        switch (e)
        {
            case InputEventMouseMotion motion when captured && _camera is not null:
                _camera.Look(motion.ScreenRelative);
                break;
            case InputEventMouseButton { Pressed: true } wheel when captured && _camera is not null && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                break;
            case InputEventMouseButton { Pressed: true } when !captured:
                Input.MouseMode = Input.MouseModeEnum.Captured;
                GetViewport().SetInputAsHandled();
                break;
        }
        if (e.IsActionPressed(DriveInput.SniperToggle) && _camera is not null) _camera.ToggleSniper();
        if (e.IsActionPressed(DriveInput.UiCancel) && captured)
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;   // the pause layer comes later; Main's Esc leaves nothing while the match shows
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Follows the own tank (or looks from a fixed preset), then lays the gun where the camera looks unless a script, free look or a wreck says not to.</summary>
    private void UpdateCamera(in TankState own, TankPose pose, Battlefield field)
    {
        if (_automation.CameraAngles(own.Yaw) is { } angles) _camera.SetAngles(angles.Yaw, angles.Pitch);
        Vector3 pivot = pose.Position.ToGodot();
        _camera.Follow(pivot, (pose.Trunnion(own.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), field.Terrain);
        if (_automation.HasPreset)
        {
            var (eye, target) = CameraPresets.Get(_automation.Preset!, pivot, own.Yaw, _time, _groundAt, targetIndex: 0);
            _camera.Camera.LookAtFromPosition(eye, target, Vector3.Up);
        }
        else if (!own.IsDestroyed && !_input.FreeLook)
        {
            (_aimYaw, _aimPitch) = AimSolver.Solve(_camera, field, pose.Trunnion(own.Spec), _aimYaw);
        }
    }
}
```

新建 `src/Tank.Client/scripts/Match/MatchMain.Events.cs`：

```csharp
using Godot;
using Tank.Client.Combat;
using Tank.Client.World;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Maps;

namespace Tank.Client.Match;

/// <summary>
/// Events to effects (blueprint §3.3). Another tank's shot flashes where the event says, along the shell's flight, with
/// the recoil if the tank is in sight (shooter id 0 is a shooter the server hid: the flash alone). A burst over the
/// ground is dust off rock or armour, on it white smoke; an AP strike a small grey puff. Kills blow up and wreck the
/// actor, the own tank's included; revivals put it back (the state's fallback is in <see cref="WreckTracker"/>). The
/// combat HUD, the walls and the prop effects get every event, as in the sandbox.
/// </summary>
public partial class MatchMain
{
    private const float ArmorHitSize = 0.5f;        // an AP shell striking armour: a small grey puff
    private const float GroundBurstHeight = 0.5f;   // a shell bursting higher than this over the ground hit a rock or a tank
    private const float BlastSize = 1.8f;           // a tank blowing up
    private const float BlastHeight = 1.2f;
    private const float FireShake = 0.08f;
    private const float FireShakeSeconds = 0.15f;

    private void OnEvent(in GameEvent e)
    {
        Battlefield field = _field!;
        switch (e.Type)
        {
            case GameEventType.ShellFired:
                OnShellFired(e);
                break;
            case GameEventType.ShellExploded:
                bool aboveGround = e.Position.Y > field.Terrain.HeightAt(e.Position.X, e.Position.Z) + GroundBurstHeight;
                _fx.Explosion(e.Position.ToGodot(), aboveGround ? PuffColor.Grey : PuffColor.White);
                break;
            case GameEventType.TankHit when e.Ammo == AmmoType.AP:   // HE gets its own ShellExploded
                _fx.Explosion(e.Position.ToGodot(), PuffColor.Grey, ArmorHitSize);
                break;
            case GameEventType.TankDestroyed when e.TargetTankId == _client.OwnTankId:
                _fx.Explosion(e.Position.ToGodot() + Vector3.Up * BlastHeight, PuffColor.White, BlastSize);
                if (_tank is not null && _ownWreck.Destroyed() == WreckTracker.Change.Wreck) _tank.Wreck(_client.Own.TurretYaw);
                break;
            case GameEventType.TankRevived when e.TargetTankId == _client.OwnTankId:
                if (_ownWreck.Revived() == WreckTracker.Change.Restore) RestoreOwn();
                break;
            case GameEventType.TankDestroyed:
            case GameEventType.TankRevived:
                _actors.OnEvent(e);
                break;
        }
        _combat.OnShotEvent(e);
        _world!.OnEvent(e);
        PropFx.OnEvent(e, field, _fx);
    }

    /// <summary>
    /// A shot the client didn't predict. The own tank's (after a hard resync the server's shell flies like anyone's)
    /// shows at its own barrel; another tank's where the event says, with the recoil if that tank is in sight.
    /// </summary>
    private void OnShellFired(in GameEvent e)
    {
        if (e.TankId == _client.OwnTankId && _tank is not null)
        {
            _tank.Kick(_client.Own.TurretYaw);
            _fx.Muzzle(_tank.MuzzleTransform, withSmoke: !_camera.Sniper);
            return;
        }
        _fx.Muzzle(MuzzleOf(e));
        if (e.TankId != 0) _actors.Kick(e.TankId);
    }

    /// <summary>The muzzle of a shot event: at its position, −Z along the shell's flight (upright when the flight is vertical or unknown).</summary>
    private static Transform3D MuzzleOf(in GameEvent e)
    {
        Vector3 at = e.Position.ToGodot();
        Vector3 along = e.Velocity.ToGodot();
        if (along.LengthSquared() < 1e-6f) return new Transform3D(Basis.Identity, at);
        along = along.Normalized();
        if (Mathf.Abs(along.Y) > 0.99f) return new Transform3D(Basis.Identity, at);
        return new Transform3D(Basis.LookingAt(along, Vector3.Up), at);
    }

    /// <summary>The own tank is back: the model intact and AP loaded (a respawned tank starts with AP; still asking for HE would cost a full reload).</summary>
    private void RestoreOwn()
    {
        _tank?.Restore();
        _input.ResetAmmo();
    }
}
```

新建 `src/Tank.Client/scripts/Match/MatchMain.cs`：

```csharp
using Godot;
using Tank.Client.Bootstrap;
using Tank.Client.Cameras;
using Tank.Client.Combat;
using Tank.Client.Online;
using Tank.Client.Overlay;
using Tank.Client.UI;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using SN = System.Numerics;

namespace Tank.Client.Match;

/// <summary>
/// The match scene (blueprint §1.1, §3.2): a child of <c>Main</c> that lives while <c>BattleClient.Phase</c> is
/// <c>InMatch</c> and draws what the client knows. It alone calls <c>client.Update</c> while it exists, once per frame,
/// and feeds it the player's command; the own tank is drawn from the prediction (between its last two ticks, at the
/// client's render position), the others from the interpolator as <see cref="TankActors"/>, the shells from
/// <c>client.Shells</c>, the props as the field has them; events become effects (MatchMain.Events.cs); the camera and
/// aim follow the sandbox's (MatchMain.Camera.cs). Nothing here reads <c>Simulation</c>. A loopback host runs on a
/// virtual clock, so every frame then steps by <see cref="VirtualFrame"/>. <see cref="Finished"/> fires once when the
/// phase leaves the match; the owner then frees the scene.
/// </summary>
public partial class MatchMain : Node3D
{
    private const double VirtualFrame = 1.0 / 60;
    private const string Title = "坦克 3D · 对局";
    private const float StartPitch = 0.25f;   // the orbit camera's pitch on entering, looking a little down on the tank

    private readonly MatchInput _input = new();
    private readonly ShotDetector _shots = new();
    private readonly WreckTracker _ownWreck = new();
    private readonly string?[] _names = new string?[BattleProtocol.MaxPlayers];
    private readonly HeadLookup _headOf;   // cached: a method group would make a new delegate every call

    private BattleSession _session = null!;
    private BattleClient _client = null!;
    private LaunchOptions _options = new();
    private MatchAutomation _automation = null!;
    private RoomStateMessage? _namesFrom;   // the room state the names were read from
    private Battlefield? _field;
    private Func<float, float, float> _groundAt = null!;
    private BattleWorld? _world;   // null until the field is known
    private TankActor? _tank;      // null until the own tank is known
    private TankSpec? _tankSpec;   // the spec it was built for
    private bool _tankFirstSight;
    private TankActors _actors = null!;
    private ShellViews _shells = null!;
    private BattleFx _fx = null!;
    private OrbitCamera _camera = null!;
    private CombatHud _combat = null!;
    private CanvasLayer _loading = null!;
    private double _time;
    private float _aimYaw;
    private float _aimPitch;
    private bool _finished;

    /// <summary>The client left the match (it ended, or the connection did); raised once.</summary>
    public event Action? Finished;

    public MatchMain() => _headOf = TryGetHead;

    /// <summary>Hands over the session to draw; call before adding the node to the tree.</summary>
    public void Setup(BattleSession session, LaunchOptions options)
    {
        (_session, _client, _options) = (session, session.Client, options);
        _automation = new MatchAutomation(options);
    }

    public override void _Ready()
    {
        GetWindow().Title = Title;
        MatchInput.RegisterActions();
        _input.NetPanelOpen = _options.NetGraph;
        _loading = BuildLoadingLayer();
        AddChild(_loading);
        _automation.ReportBadOptions();
        if (!_automation.IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Process(double delta)
    {
        if (_finished) return;
        double dt = _session.IsVirtualClock ? VirtualFrame : delta;
        _time += dt;
        _session.Advance(dt);

        // The command first, then exactly one Update; a latched click is spent only once a tick has taken it (R1).
        LocalCommand cmd = Command();
        int before = _client.Predictor.LastPredictedTick;
        _client.Update(dt, cmd);
        if (_client.Predictor.LastPredictedTick != before) _input.EndTick();

        if (_client.Phase != BattlePhase.InMatch)
        {
            _finished = true;
            Finished?.Invoke();
            return;
        }
        if (_client.Field is not { } field) return;   // InMatch always has a field; a defensive frame if not
        // The scenery is built as soon as the field is known: one long frame, best spent before the first snapshot sets the clocks.
        if (_world is null) BuildScene(field);
        else if (!ReferenceEquals(field, _field)) TakeField(field);   // a resync swapped the field instance (R2)
        if (!_client.HasTank && _client.Latest is null)
        {
            _loading.Visible = true;   // the match start is here, the first snapshot (and so the own tank's spec) not yet (R3)
            return;
        }
        _loading.Visible = false;
        RefreshNames();

        float fdt = (float)dt;
        while (_client.TryDequeueEvent(out GameEvent e)) OnEvent(e);

        bool hasTank = _client.HasTank;
        TankState own = default;
        TankPose pose = default;
        if (hasTank)
        {
            EnsureOwnActor();
            own = RenderMath.OwnDisplay(_client.Predictor.Previous, _client.Predictor.Current, (float)_client.Clock.Alpha, _client.OwnRenderPosition);
            pose = TankPose.Compute(own, field.Terrain);
            ShowOwn(own, pose, fdt);
            UpdateCamera(own, pose, field);
        }
        Vector3 cameraPos = _camera.Camera.GlobalPosition;
        _world!.Update(hasTank ? own : null, hasTank ? pose : null, cameraPos, fdt);   // only the own tank flattens the grass (R12)
        _actors.Sync(_client.Others, Mode, OwnTeam(own), fdt);
        _shells.Sync(_client.Shells, (float)_client.Clock.Alpha, fdt);
        _combat.ShowOwn(hasTank);
        _combat.Refresh(own, spotted: hasTank && _client.Spotted && !own.IsDestroyed);
        _actors.FillBars(_combat.TankBars, BattleProtocol.MaxPlayers, _names);
        _automation.EndFrame(delta);
    }

    /// <summary>The player's sticks, trigger and aim for this frame; only the aim while the tank is a wreck (nothing to drive).</summary>
    private LocalCommand Command()
    {
        if (!_client.HasTank) return LocalCommand.None;
        if (_client.Own.IsDestroyed) return new LocalCommand(0, 0, _aimYaw, _aimPitch, false, _input.Ammo);
        var (throttle, steer, fire) = _input.Peek();
        return _automation.Command(new LocalCommand(throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo), _time);
    }

    private BattleMode Mode => _client.MatchInfo?.Mode ?? _client.Room?.Mode ?? BattleMode.TeamBattle;

    /// <summary>The player's team from the scoreboard (known even while the own tank isn't), else the own tank's.</summary>
    private byte OwnTeam(in TankState own)
    {
        if (_client.Latest is { } latest && _client.Slot < latest.Players.Length) return latest.Players[_client.Slot].Team;
        return own.Team;
    }

    private void BuildScene(Battlefield field)
    {
        _field = field;
        _groundAt = field.Terrain.HeightAt;
        TankState focus = _client.HasTank ? _client.Own : new TankState { Position = new SN.Vector2(field.Terrain.SizeX / 2f, field.Terrain.SizeZ / 2f) };
        _world = Add(new BattleWorld());
        _world.Build(field, focus, MapStyle.Sandbox, CameraPresets.StreamsAroundCamera(_automation.Preset));
        _fx = Add(new BattleFx());
        _actors = Add(new TankActors());
        _actors.Build(field.Terrain, _fx);
        _shells = Add(new ShellViews());
        _camera = Add(new OrbitCamera());
        if (_options.Sniper) _camera.ToggleSniper();
        _combat = Add(new CombatHud());
        _combat.Build(_camera, field, _client.OwnTankId, BattleProtocol.MaxPlayers, _headOf);
    }

    private void TakeField(Battlefield field)
    {
        _field = field;
        _groundAt = field.Terrain.HeightAt;
        _world!.SetField(field);
        _combat.SetField(field);
        _actors.SetTerrain(field.Terrain);
    }

    /// <summary>Builds the own actor once the own tank's spec is known (a new spec, which only a new match brings, rebuilds it).</summary>
    private void EnsureOwnActor()
    {
        TankSpec spec = _client.Own.Spec;
        if (_tank is not null && _tankSpec == spec) return;
        _tank?.QueueFree();
        _tank = Add(new TankActor());
        _tank.Build(spec);   // the look's own paint: toy orange
        _tankSpec = spec;
        _tankFirstSight = true;
        _aimYaw = _client.Own.Yaw;
        _camera.SetAngles(_client.Own.Yaw, StartPitch);
    }

    /// <summary>
    /// Poses the own tank with the recoil, flash and camera shake of a shot it fired since the last frame (seen in its
    /// predicted shot count: a predicted shot never reaches the event queue), and wrecks or restores it as the state says
    /// when no event did.
    /// </summary>
    private void ShowOwn(in TankState own, TankPose pose, float dt)
    {
        TankActor tank = _tank!;
        bool fired = _shots.Update(_client.Own.ShotCount, _client.Own.Id);
        if (fired) tank.Kick(own.TurretYaw);
        switch (_ownWreck.Observe(own.IsDestroyed, dt, _tankFirstSight))
        {
            case WreckTracker.Change.Settle: tank.Wreck(own.TurretYaw, settled: true); break;
            case WreckTracker.Change.Restore: RestoreOwn(); break;
        }
        _tankFirstSight = false;
        float yawRate = RenderMath.YawRate(_client.Predictor.Previous.Yaw, _client.Predictor.Current.Yaw, SimConstants.Dt);
        tank.Present(own, pose, yawRate, dt, shown: !_camera.Sniper);
        if (!fired) return;
        _fx.Muzzle(tank.MuzzleTransform, withSmoke: !_camera.Sniper);   // own puffs would block the scope
        _camera.Shake(FireShake, FireShakeSeconds);
    }

    /// <summary>The names by room slot, re-read whenever the room state changes (a record the client replaces).</summary>
    private void RefreshNames()
    {
        RoomStateMessage? room = _client.Room;
        if (ReferenceEquals(room, _namesFrom)) return;
        _namesFrom = room;
        Array.Clear(_names);
        if (room is null) return;
        for (int i = 0; i < room.Players.Count; i++)
        {
            RoomPlayer p = room.Players[i];
            if (p.Slot < _names.Length) _names[p.Slot] = p.Name;
        }
    }

    private bool TryGetHead(int tankId, out Vector3 head) => _actors.TryGetHead(tankId, out head);

    private static CanvasLayer BuildLoadingLayer()
    {
        var layer = new CanvasLayer { Layer = 2 };
        var shade = new ColorRect { Color = UiKit.Background, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(shade);
        shade.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        Label label = UiKit.Label("对局加载中…", 40, UiKit.Dim, HorizontalAlignment.Center);
        label.VerticalAlignment = VerticalAlignment.Center;
        layer.AddChild(label);
        label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        return layer;
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
```

新建 `src/Tank.Client/scripts/Match/TankActors.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Combat;
using Tank.Client.Overlay;
using Tank.Client.Vehicles;
using Tank.Client.World;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Client.Match;

/// <summary>
/// The other tanks of a match, one <see cref="TankActor"/> per tank id, built the first time the tank is seen (its spec
/// and team from its entity, painted by <see cref="MatchColors"/>). Each frame <see cref="Sync"/> poses the tanks in
/// <c>BattleClient.Others</c> and hides the rest at once (not in sight is not drawn; no fade). A kill is the event's
/// to show, the snapshot's flag the fallback (<see cref="WreckTracker"/>); a tank first seen — or seen again after
/// being out of sight — as a wreck settles without a blast, and one revived out of sight comes back intact at once. The
/// HUD reads heads and health through <see cref="TryGetHead"/> and <see cref="FillBars"/>.
/// </summary>
public partial class TankActors : Node3D
{
    private const float BlastSize = 1.8f;
    private const float BlastHeight = 1.2f;

    private sealed class Entry
    {
        public required TankActor Actor;
        public required Side Side;
        public required Color LabelColor;
        public required byte Slot;
        public readonly WreckTracker Wreck = new();
        public TankState State;
        public TankPose Pose;
        public float PrevYaw;
        public int SeenStamp;   // the Sync that last had this tank in sight
    }

    private readonly Dictionary<int, Entry> _entries = new();
    private readonly List<Entry> _order = new();   // in order of first sight: the order the HUD bars go out in
    private HeightField _terrain = null!;
    private BattleFx _fx = null!;
    private int _stamp;

    public void Build(HeightField terrain, BattleFx fx) => (_terrain, _fx) = (terrain, fx);

    /// <summary>A resync swapped the battlefield instance: the same ground, read from the new one.</summary>
    public void SetTerrain(HeightField terrain) => _terrain = terrain;

    /// <summary>
    /// Poses every tank in <paramref name="others"/> (building an actor for one never seen before) and hides the ones
    /// not there this frame. <paramref name="ownTeam"/> and <paramref name="mode"/> decide a new tank's colours.
    /// </summary>
    public void Sync(IReadOnlyList<RemoteTank> others, BattleMode mode, byte ownTeam, float dt)
    {
        _stamp++;
        for (int i = 0; i < others.Count; i++)
        {
            RemoteTank remote = others[i];
            TankState state = RenderMath.FromRemote(remote);
            bool isNew = !_entries.TryGetValue(state.Id, out Entry? entry);
            if (isNew) entry = Create(state, remote.Entity.Slot, mode, ownTeam);
            // Not drawn last frame (never seen, or out of sight since): whatever happened meanwhile — a kill, a revival, a
            // turn — is the state's to show at once, without the event wait or a yaw rate from a stale heading.
            Show(entry!, state, dt, firstSight: isNew || !entry!.Actor.Visible);
        }
        for (int i = 0; i < _order.Count; i++)
        {
            Entry entry = _order[i];
            if (entry.SeenStamp != _stamp && entry.Actor.Visible) entry.Actor.Present(entry.State, entry.Pose, 0f, dt, shown: false);
        }
    }

    private Entry Create(in TankState state, byte slot, BattleMode mode, byte ownTeam)
    {
        Side side = MatchColors.SideOf(mode, ownTeam, state.Team, isOwn: false);
        var actor = new TankActor();
        AddChild(actor);
        actor.Build(state.Spec, MatchColors.Paint(side));
        var entry = new Entry { Actor = actor, Side = side, LabelColor = MatchColors.Ui(side).ToColor(), Slot = slot, PrevYaw = state.Yaw };
        _entries[state.Id] = entry;
        _order.Add(entry);
        return entry;
    }

    private void Show(Entry entry, in TankState state, float dt, bool firstSight)
    {
        entry.SeenStamp = _stamp;
        entry.Pose = TankPose.Compute(state, _terrain);
        float yawRate = firstSight ? 0f : RenderMath.YawRate(entry.PrevYaw, state.Yaw, dt);
        entry.PrevYaw = state.Yaw;
        switch (entry.Wreck.Observe(state.IsDestroyed, dt, firstSight))
        {
            case WreckTracker.Change.Settle: entry.Actor.Wreck(state.TurretYaw, settled: true); break;
            case WreckTracker.Change.Restore: entry.Actor.Restore(); break;
        }
        entry.Actor.Present(state, entry.Pose, yawRate, dt, shown: true);
        entry.State = state;
    }

    /// <summary>Kills and revivals of the other tanks: the kill blows up where the event says, the actor wrecks or is put back.</summary>
    public void OnEvent(in GameEvent e)
    {
        if (e.Type is not (GameEventType.TankDestroyed or GameEventType.TankRevived)) return;
        _entries.TryGetValue(e.TargetTankId, out Entry? entry);
        if (e.Type == GameEventType.TankDestroyed)
        {
            _fx.Explosion(e.Position.ToGodot() + Vector3.Up * BlastHeight, PuffColor.White, BlastSize);
            if (entry is not null && entry.Wreck.Destroyed() == WreckTracker.Change.Wreck) entry.Actor.Wreck(entry.State.TurretYaw);
        }
        else if (entry is not null && entry.Wreck.Revived() == WreckTracker.Change.Restore)
        {
            entry.Actor.Restore();
        }
    }

    /// <summary>The recoil of a shot by tank <paramref name="tankId"/>, if it is in sight (its muzzle flash is the caller's).</summary>
    public void Kick(int tankId)
    {
        if (_entries.TryGetValue(tankId, out Entry? entry) && entry.Actor.Visible) entry.Actor.Kick(entry.State.TurretYaw);
    }

    /// <summary>The point over tank <paramref name="tankId"/>'s turret; false for a tank unknown or not in sight now.</summary>
    public bool TryGetHead(int tankId, out Vector3 head)
    {
        if (_entries.TryGetValue(tankId, out Entry? entry) && entry.Actor.Visible)
        {
            head = entry.Actor.Head;
            return true;
        }
        head = Vector3.Zero;
        return false;
    }

    /// <summary>
    /// Feeds the HUD's tank bars: one per tank in sight and intact, with the player's name (<paramref name="names"/> by
    /// room slot) in its side's colour; the rest hidden. The bars go to the tanks shown this frame, in the order built,
    /// so a match that has seen more tanks than bars (leavers and joiners get new ids) still has one for each in sight;
    /// the name and colour are set with the bar, so they follow the tank. Tanks beyond the HUD's count get none.
    /// </summary>
    public void FillBars(HealthBars bars, int barCount, IReadOnlyList<string?> names)
    {
        int bar = 0;
        for (int i = 0; i < _order.Count && bar < barCount; i++)
        {
            Entry entry = _order[i];
            if (!entry.Actor.Visible || entry.State.IsDestroyed) continue;
            float share = entry.State.Spec is { } spec ? entry.State.Health / (float)spec.MaxHealth : 0f;
            bars.Set(bar++, entry.Actor.Head, share, shown: true, entry.Slot < names.Count ? names[entry.Slot] : null, entry.LabelColor);
        }
        for (; bar < barCount; bar++) bars.Set(bar, Vector3.Zero, 0f, shown: false);
    }
}
```

修改 `src/Tank.Net/Client/ClockSync.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Client/ClockSync.cs b/src/Tank.Net/Client/ClockSync.cs
index 4113ccb..85004d9 100644
--- a/src/Tank.Net/Client/ClockSync.cs
+++ b/src/Tank.Net/Client/ClockSync.cs
@@ -57,7 +57,11 @@ public sealed class ClockSync
             Initialize(serverTick, rttSeconds);
             return;
         }
-        if (System.Math.Abs(inputSlack - TargetSlackTicks) > HardResyncThreshold)
+        // The server has reached (or passed) the client's own tick: the client fell behind (a long frame the engine's
+        // clamped delta never made up for), so its inputs arrive late and are dropped. The slack feedback then says
+        // nothing, as no input is received at all, so this is the only sign; start again ahead of the server.
+        bool behind = serverTick >= Tick;
+        if (behind || System.Math.Abs(inputSlack - TargetSlackTicks) > HardResyncThreshold)
         {
             HardResyncs++;
             Initialize(serverTick, rttSeconds);
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 674、Net 210。

- [ ] **Step 5：截图验证**

```bash
mkdir -p build/shots/t5
$S/godot_run.sh $W "" 40 -- --no-update --host --bots 3 --autostart --screenshot $W/build/shots/t5/team.png --screenshot-at 8 --quit-after 12
$S/godot_run.sh $W "" 40 -- --no-update --host --bots 3 --autostart --camera-preset orbit --screenshot $W/build/shots/t5/team-orbit.png --screenshot-at 8 --quit-after 12
$S/godot_run.sh $W "" 60 -- --no-update --host --mode ffa --bots 3 --autostart --autodrive --screenshot $W/build/shots/t5/ffa.png --screenshot-at 40 --quit-after 44
$S/godot_run.sh $W "" 40 -- --no-update --host --bots 3 --autostart --autodrive --screenshot $W/build/shots/t5/fx.png --screenshot-at 9 --quit-after 10
```

Expected（本任务的 `--screenshot-at` 仍由 `Main` 从进程启动计时，Task 9 改为从对局开始）：
- `team.png`：自己的车橙色，同队机器人蓝灰、头顶名字和血条；画面没有报错。
- `team-orbit.png`：环绕机位，能看到己方几辆车和地形。
- `ffa.png`：各自为战里别人一律红色（40 秒时多半已有车进入视线；没有就换 `--screenshot-at 25` 再拍一次）。
- `fx.png`：自动驾驶开炮后的炮口火光或炮弹 / 爆炸。
- 以上各次运行的输出里没有 ERROR、Unhandled 或 exception。

- [ ] **Step 6：提交**

```bash
git add -A src/Tank.Client src/Tank.Net tests/Tank.Art.Tests tests/Tank.Net.Tests
git commit -m "feat(client): the match scene — MatchMain renders a BattleClient match (predicted own tank, interpolated others coloured by side, network shells, prop state, FX from events with the wreck fallback, camera and aim); ClockSync resyncs hard when the server catches up"
```

---

### Task 6：HUD 基础件

蓝图 §4.2、§5 T6。`Match/Logic/{HudText,KillFeed,HudLayout,NetPanelText,MatchHudFrame,HudDemoFrames}` + `Match/{MatchHud,HudPanel,ScorePlate,KillFeedView,CenterBanner,NetPanel,HudGallery}`。计时向上取整成 m:ss，30 秒内变红；比分板块按模式（团队：我方 / 敌方击毁；歼灭：双方存活；各自为战：击毁 / 存活）；击毁提示最多 5 行、5 秒，射手不可见写“X 被击毁”，误伤单独文案，名字按像素截断（`HudText.Fit`，每个名字最宽 150，加“…”）使一行不超过 440 宽；复活横幅、观战提示条；F3 面板含对账次数 / 最近 / 平均 / 最大、快照字节、房主的服务器 tick；`--hud-demo timer|feed|respawn|spectate|annihilation|ffa|net` 用预制帧画确定性截图；按 Tab 时隐藏击毁提示和 F3（900 宽的计分板与两者在几何上必然重叠）；都只在内容变化时重画。

**Files:**
- Create: `src/Tank.Client/scripts/Match/{CenterBanner,HudGallery,HudPanel,KillFeedView,MatchHud,NetPanel,ScorePlate}.cs`、`Match/Logic/{HudDemoFrames,HudLayout,HudText,KillFeed,MatchHudFrame,NetPanelText}.cs`
- Modify: `Bootstrap/{Main,Main.Flow}.cs`、`Match/{MatchMain,MatchMain.Events}.cs`
- Test: 新建 `tests/Tank.Art.Tests/{HudDemoFramesTests,HudLayoutTests,HudTextTests,KillFeedTests,MatchHudFrameTests,NetPanelTextTests}.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/HudDemoFramesTests.cs`：

```csharp
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class HudDemoFramesTests
{
    public static TheoryData<string> Demos
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string name in HudDemoFrames.Names) data.Add(name);
            return data;
        }
    }

    [Fact]
    public void The_gallery_has_the_screens_the_blueprint_names()
    {
        foreach (string name in new[] { "timer", "feed", "respawn", "spectate", "annihilation", "ffa" })
            Assert.Contains(name, HudDemoFrames.Names);
    }

    [Fact]
    public void An_unknown_name_gives_no_demo()
    {
        Assert.False(HudDemoFrames.TryGet("nope", out _));
        Assert.False(HudDemoFrames.TryGet(null, out _));
    }

    [Theory]
    [MemberData(nameof(Demos))]
    public void Every_demo_frame_is_self_consistent(string name)
    {
        Assert.True(HudDemoFrames.TryGet(name, out HudDemo demo));
        MatchHudFrame frame = demo.Frame;

        Assert.Equal(name, demo.Name);
        Assert.Equal(BattleProtocol.MaxPlayers, frame.Players.Length);
        Assert.Equal(frame.Players.Length, frame.Names.Length);
        int active = frame.Players.Count(p => p.Active);
        Assert.InRange(active, 2, ModeRules.MaxPlayers);
        for (int slot = 0; slot < frame.Players.Length; slot++)
        {
            PlayerStats p = frame.Players[slot];
            if (!p.Active) { Assert.True(string.IsNullOrEmpty(frame.Names[slot])); continue; }
            Assert.False(string.IsNullOrWhiteSpace(frame.Names[slot]), $"slot {slot} has no name");
            Assert.True(p.Kills >= 0 && p.Deaths >= 0);
            Assert.Equal(0, p.Rank);   // nobody is ranked while the match runs
            Assert.True(p.TankId > 0);
            if (ModeRules.HasTeams(frame.Mode)) Assert.InRange(p.Team, 0, 1);
        }
        Assert.Equal(active, frame.Players.Where(p => p.Active).Select(p => p.TankId).Distinct().Count());
        Assert.True(frame.Players[frame.OwnSlot].Active);
        Assert.True(frame.TicksLeft > 0);
        Assert.True(frame.TicksLeft <= ModeRules.DurationTicks(frame.Mode));
        Assert.True(demo.Kills.Length <= KillFeed.Capacity);
        foreach (FeedLine line in demo.Kills)
        {
            Assert.False(string.IsNullOrWhiteSpace(line.Victim));
            Assert.False(string.IsNullOrWhiteSpace(line.Text));
            Assert.True(line.Killer is null || line.Killer.Length > 0);
        }
    }

    [Theory]
    [MemberData(nameof(Demos))]
    public void The_plate_in_every_demo_matches_its_players(string name)
    {
        HudDemoFrames.TryGet(name, out HudDemo demo);
        MatchHudFrame f = demo.Frame;
        var match = new MatchBlock { TeamScore0 = f.Plate.Kind == PlateKind.TeamKills ? f.Plate.Left : 0, TeamScore1 = f.Plate.Kind == PlateKind.TeamKills ? f.Plate.Right : 0 };
        Assert.Equal(f.Plate, HudText.Plate(f.Mode, f.OwnSlot, f.OwnTeam, match, f.Players));
    }

    [Fact]
    public void The_named_screens_show_what_they_are_named_for()
    {
        HudDemoFrames.TryGet("timer", out HudDemo timer);
        Assert.Equal("3:27", HudText.Timer(timer.Frame.TicksLeft));
        Assert.Equal(new PlateValues(PlateKind.TeamKills, 4, 0, 2, 0), timer.Frame.Plate);

        HudDemoFrames.TryGet("feed", out HudDemo feed);
        Assert.Equal(KillFeed.Capacity, feed.Kills.Length);
        Assert.Contains(feed.Kills, l => l.Killer is null);
        Assert.Contains(feed.Kills, l => l.Friendly);

        HudDemoFrames.TryGet("respawn", out HudDemo respawn);
        Assert.Equal(BannerKind.Respawn, respawn.Frame.Banner);
        Assert.Equal("6 秒后复活", HudText.RespawnLine(respawn.Frame.RespawnIn));

        HudDemoFrames.TryGet("spectate", out HudDemo spectate);
        Assert.Equal("观战 机器人 2 · Q/E 切换", HudText.SpectateLine(spectate.Frame.SpectateTarget!));

        HudDemoFrames.TryGet("annihilation", out HudDemo annihilation);
        Assert.Equal(("3/5", "2/5"), (HudText.Parts(annihilation.Frame.Plate).LeftValue, HudText.Parts(annihilation.Frame.Plate).RightValue));

        HudDemoFrames.TryGet("ffa", out HudDemo ffa);
        Assert.Equal(("3", "4/8"), (HudText.Parts(ffa.Frame.Plate).LeftValue, HudText.Parts(ffa.Frame.Plate).RightValue));

        HudDemoFrames.TryGet("net", out HudDemo net);
        Assert.True(net.NetPanelOpen);
    }
}
```

新建 `tests/Tank.Art.Tests/HudLayoutTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Match;

namespace Tank.Art.Tests;

public class HudLayoutTests
{
    public static TheoryData<float, float> Screens => new()
    {
        { 1600, 900 },
        { 1600, 1200 },
        { 2100, 900 },
    };

    private static IEnumerable<(string Name, HudRect Rect)> All(HudBoxes b)
    {
        yield return ("score plate", b.ScorePlate);
        yield return ("mode label", b.ModeLabel);
        yield return ("kill feed", b.KillFeed);
        yield return ("net panel", b.NetPanel);
        yield return ("banner", b.Banner);
        yield return ("spectate bar", b.SpectateBar);
        yield return ("scoreboard", b.Scoreboard);
        yield return ("status panel", b.StatusPanel);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void Every_block_lies_inside_the_screen(float w, float h)
    {
        var screen = new Vector2(w, h);
        foreach ((string name, HudRect rect) in All(HudLayout.For(screen)))
            Assert.True(rect.FitsIn(screen), $"{name} {rect} is off a {w}x{h} screen");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_always_on_blocks_do_not_overlap_each_other(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        (string, HudRect)[] always =
        {
            ("score plate", b.ScorePlate), ("mode label", b.ModeLabel), ("kill feed", b.KillFeed), ("net panel", b.NetPanel), ("status panel", b.StatusPanel),
        };
        AssertNoOverlaps(always);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_banners_clear_the_status_panel_and_the_corner_blocks(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        foreach (HudRect banner in new[] { b.Banner, b.SpectateBar })
        {
            Assert.False(banner.Intersects(b.StatusPanel), "a banner covers the status panel");
            Assert.False(banner.Intersects(b.KillFeed), "a banner covers the kill feed");
            Assert.False(banner.Intersects(b.NetPanel), "a banner covers the F3 panel");
            Assert.False(banner.Intersects(b.ScorePlate), "a banner covers the score plate");
        }
        Assert.False(b.Banner.Intersects(b.SpectateBar));
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_spectate_bar_sits_on_the_bottom_margin_and_clears_the_status_panel(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(h - 36, b.SpectateBar.Bottom, precision: 3);
        Assert.Equal(w / 2, b.SpectateBar.CenterX, precision: 3);
        Assert.True(b.SpectateBar.X > b.StatusPanel.Right);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_scoreboard_is_centred_900_wide_below_the_plate(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(w / 2, b.Scoreboard.CenterX, precision: 3);
        Assert.Equal(900, b.Scoreboard.Width);
        Assert.Equal(110, b.Scoreboard.Y);
        Assert.True(b.Scoreboard.Y > b.ScorePlate.Bottom);
        Assert.True(b.Scoreboard.Y < b.ModeLabel.Bottom);   // the mode label is the one thing it covers at the top
    }

    [Fact]
    public void The_scoreboard_does_not_fit_between_the_kill_feed_and_the_f3_panel_at_the_base_size()
    {
        // 900 wide leaves 350 either side at 1600, less than the feed (440 + margin) or the panel (500 + margin) need: both
        // step aside while Tab is held (MatchHud.SetScoreboardOpen). The blueprint's "no intersection" cannot hold with this width.
        HudBoxes b = HudLayout.For(new Vector2(1600, 900));
        Assert.True(b.Scoreboard.Intersects(b.KillFeed));
        Assert.True(b.Scoreboard.Intersects(b.NetPanel));
        Assert.True(b.KillFeed.X < b.Scoreboard.Right);
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void The_kill_feed_hangs_from_the_top_right_in_five_rows(float w, float h)
    {
        HudBoxes b = HudLayout.For(new Vector2(w, h));
        Assert.Equal(w - 24, b.KillFeed.Right, precision: 3);
        Assert.Equal(440, b.KillFeed.Width);
        Assert.Equal(b.KillFeed.Y, b.KillRow(0).Y);
        Assert.Equal(b.KillFeed.Bottom, b.KillRow(4).Bottom, precision: 3);
        for (int i = 1; i < HudLayout.KillRows; i++) Assert.False(b.KillRow(i - 1).Intersects(b.KillRow(i)));
    }

    [Fact]
    public void The_banner_follows_the_height_and_the_rest_do_not()
    {
        HudBoxes tall = HudLayout.For(new Vector2(1600, 1200));
        HudBoxes normal = HudLayout.For(new Vector2(1600, 900));
        Assert.Equal(252, normal.Banner.Y, precision: 3);
        Assert.Equal(336, tall.Banner.Y, precision: 3);
        Assert.Equal(normal.ScorePlate, tall.ScorePlate);
        Assert.Equal(normal.NetPanel, tall.NetPanel);
    }

    private static void AssertNoOverlaps((string Name, HudRect Rect)[] blocks)
    {
        for (int i = 0; i < blocks.Length; i++)
            for (int j = i + 1; j < blocks.Length; j++)
                Assert.False(blocks[i].Rect.Intersects(blocks[j].Rect), $"{blocks[i].Name} overlaps {blocks[j].Name}");
    }
}
```

新建 `tests/Tank.Art.Tests/HudTextTests.cs`：

```csharp
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class HudTextTests
{
    private const int Hz = SimConstants.TickRate;

    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(1, "0:01")]
    [InlineData(Hz, "0:01")]
    [InlineData(Hz + 1, "0:02")]
    [InlineData(59 * Hz, "0:59")]
    [InlineData(60 * Hz, "1:00")]
    [InlineData(60 * Hz + 1, "1:01")]
    [InlineData(7 * 60 * Hz, "7:00")]
    [InlineData(10 * 60 * Hz, "10:00")]
    [InlineData(-5, "0:00")]
    public void The_clock_rounds_up_to_the_next_second(int ticks, string expected) =>
        Assert.Equal(expected, HudText.Timer(ticks));

    [Theory]
    [InlineData(1, "1 秒后复活")]
    [InlineData(6 * Hz, "6 秒后复活")]
    [InlineData(6 * Hz - 1, "6 秒后复活")]
    [InlineData(10 * Hz, "10 秒后复活")]
    public void The_respawn_line_counts_whole_seconds_rounded_up(int ticks, string expected) =>
        Assert.Equal(expected, HudText.RespawnLine(ticks));

    private static float TenPerChar(string text) => text.Length * 10f;   // a stand-in for the font: every UTF-16 unit 10 wide

    [Theory]
    [InlineData("阿成", 20f, "阿成")]          // fits exactly: unchanged
    [InlineData("abcdefghij", 55f, "abcd…")]   // five units with the ellipsis is the most that fits
    [InlineData("abcdefghij", 10f, "a…")]      // never less than one character
    [InlineData("x", 5f, "x")]                 // nothing to cut a single one down to
    public void Fit_cuts_a_text_to_its_budget_with_an_ellipsis(string text, float budget, string expected) =>
        Assert.Equal(expected, HudText.Fit(text, budget, TenPerChar));

    [Fact]
    public void Fit_returns_a_text_that_fits_as_the_same_instance()
    {
        string name = "Sergeant_Wolfgang_Becker";
        Assert.Same(name, HudText.Fit(name, 1000f, TenPerChar));
    }

    [Fact]
    public void Fit_never_cuts_a_surrogate_pair_in_half()
    {
        // a, b, the rocket's two halves, c, d: "ab🚀…" is 50 wide, so the cut falls before the rocket, not inside it.
        Assert.Equal("ab…", HudText.Fit("ab🚀cd", 45f, TenPerChar));
    }

    [Fact]
    public void The_spectate_line_names_the_target_and_the_keys() =>
        Assert.Equal("观战 机器人 2 · Q/E 切换", HudText.SpectateLine("机器人 2"));

    private static PlayerStats[] Roster(params (byte Team, bool Alive)[] seats)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < seats.Length; i++) players[i] = new PlayerStats { Active = true, Team = seats[i].Team, Alive = seats[i].Alive };
        return players;
    }

    [Fact]
    public void Team_battle_shows_own_kills_first_for_team_zero()
    {
        var match = new MatchBlock { TeamScore0 = 4, TeamScore1 = 2 };
        PlateValues plate = HudText.Plate(BattleMode.TeamBattle, 0, ownTeam: 0, match, Roster());
        Assert.Equal(new PlateValues(PlateKind.TeamKills, 4, 0, 2, 0), plate);
    }

    [Fact]
    public void Team_battle_flips_the_scores_for_a_player_on_team_one()
    {
        var match = new MatchBlock { TeamScore0 = 4, TeamScore1 = 2 };
        PlateValues plate = HudText.Plate(BattleMode.TeamBattle, 3, ownTeam: 1, match, Roster());
        Assert.Equal(2, plate.Left);
        Assert.Equal(4, plate.Right);
    }

    [Fact]
    public void Annihilation_counts_the_living_of_each_side_out_of_those_who_started()
    {
        PlayerStats[] players = Roster((0, true), (0, true), (0, false), (1, true), (1, false), (1, false), (1, false));
        players[7] = new PlayerStats { Active = false, Team = 1, Alive = true };   // someone who left counts for nothing

        PlateValues plate = HudText.Plate(BattleMode.Annihilation, 0, ownTeam: 0, default, players);
        PlateValues flipped = HudText.Plate(BattleMode.Annihilation, 3, ownTeam: 1, default, players);

        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 2, 3, 1, 4), plate);
        Assert.Equal(new PlateValues(PlateKind.TeamAlive, 1, 4, 2, 3), flipped);
    }

    [Fact]
    public void Free_for_all_shows_the_players_own_kills_and_the_field_left()
    {
        PlayerStats[] players = Roster((0, true), (1, true), (2, false), (3, true));
        players[0].Kills = 3;
        players[1].Kills = 9;

        PlateValues plate = HudText.Plate(BattleMode.FreeForAll, ownSlot: 0, ownTeam: 0, default, players);

        Assert.Equal(new PlateValues(PlateKind.SoloKills, 3, 0, 3, 4), plate);
    }

    [Fact]
    public void The_plate_words_follow_the_mode()
    {
        PlateParts team = HudText.Parts(new PlateValues(PlateKind.TeamKills, 4, 0, 2, 0));
        PlateParts alive = HudText.Parts(new PlateValues(PlateKind.TeamAlive, 3, 5, 2, 5));
        PlateParts solo = HudText.Parts(new PlateValues(PlateKind.SoloKills, 3, 0, 4, 8));

        Assert.Equal(("我方", "4", "敌方", "2"), (team.LeftCaption, team.LeftValue, team.RightCaption, team.RightValue));
        Assert.Equal(("我方存活", "3/5", "敌方存活", "2/5"), (alive.LeftCaption, alive.LeftValue, alive.RightCaption, alive.RightValue));
        Assert.Equal(("击毁", "3", "存活", "4/8"), (solo.LeftCaption, solo.LeftValue, solo.RightCaption, solo.RightValue));
    }

    [Fact]
    public void The_player_slot_out_of_range_shows_no_kills_instead_of_throwing()
    {
        PlateValues plate = HudText.Plate(BattleMode.FreeForAll, ownSlot: MatchRules.NoSlot, ownTeam: 0, default, Roster((0, true)));
        Assert.Equal(0, plate.Left);
    }
}
```

新建 `tests/Tank.Art.Tests/KillFeedTests.cs`：

```csharp
using Tank.Client.Match;

namespace Tank.Art.Tests;

public class KillFeedTests
{
    private static void Kill(KillFeed feed, string victim, double now) =>
        feed.Add("甲", Side.Own, victim, Side.Enemy, friendly: false, now);

    [Fact]
    public void A_line_shows_for_five_seconds_and_then_goes()
    {
        var feed = new KillFeed();
        Kill(feed, "乙", now: 10);

        Assert.Equal(1, feed.Visible(10).Length);
        Assert.Equal(1, feed.Visible(14.99).Length);
        Assert.True(feed.Visible(15).IsEmpty);
    }

    [Fact]
    public void Only_the_five_newest_lines_show_and_the_oldest_goes_first()
    {
        var feed = new KillFeed();
        for (int i = 0; i < 7; i++) Kill(feed, $"敌{i}", now: i * 0.1);

        ReadOnlySpan<FeedLine> lines = feed.Visible(1);

        Assert.Equal(5, lines.Length);
        Assert.Equal("敌2", lines[0].Victim);   // oldest at the top
        Assert.Equal("敌6", lines[4].Victim);   // newest last
    }

    [Fact]
    public void Lines_expire_oldest_first_while_newer_ones_stay()
    {
        var feed = new KillFeed();
        Kill(feed, "早", now: 0);
        Kill(feed, "晚", now: 3);

        ReadOnlySpan<FeedLine> lines = feed.Visible(6);

        Assert.Equal(1, lines.Length);
        Assert.Equal("晚", lines[0].Victim);
    }

    [Fact]
    public void An_unknown_shooter_reads_victim_was_destroyed()
    {
        var feed = new KillFeed();
        feed.Add(null, Side.Enemy, "机器人 2", Side.Enemy, friendly: false, now: 0);
        Assert.Equal("机器人 2 被击毁", feed.Visible(0)[0].Text);
    }

    [Fact]
    public void A_kill_within_a_side_reads_friendly_fire()
    {
        var feed = new KillFeed();
        feed.Add("阿成", Side.Own, "小林", Side.Ally, friendly: true, now: 0);
        Assert.Equal("阿成 误伤 小林", feed.Visible(0)[0].Text);
    }

    [Fact]
    public void An_ordinary_kill_reads_killer_destroyed_victim()
    {
        var feed = new KillFeed();
        Kill(feed, "老周", now: 0);
        Assert.Equal("甲 击毁 老周", feed.Visible(0)[0].Text);
    }

    [Fact]
    public void The_revision_moves_when_a_line_arrives_or_expires_and_not_otherwise()
    {
        var feed = new KillFeed();
        int start = feed.Revision;
        feed.Visible(0);
        Assert.Equal(start, feed.Revision);

        Kill(feed, "乙", now: 0);
        int added = feed.Revision;
        Assert.NotEqual(start, added);
        feed.Visible(1);
        Assert.Equal(added, feed.Revision);

        feed.Visible(6);
        Assert.NotEqual(added, feed.Revision);
    }

    [Theory]
    [InlineData(Side.Own, Side.Ally, true)]
    [InlineData(Side.Ally, Side.Own, true)]
    [InlineData(Side.Own, Side.Enemy, false)]
    [InlineData(Side.Enemy, Side.Own, false)]
    [InlineData(Side.Enemy, Side.Enemy, false)]
    public void Friendly_means_neither_side_is_the_enemy(Side killer, Side victim, bool friendly) =>
        Assert.Equal(friendly, KillFeed.IsFriendly(killer, victim));
}
```

新建 `tests/Tank.Art.Tests/MatchHudFrameTests.cs`：

```csharp
using Tank.Client.Match;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class MatchHudFrameTests
{
    private const double Frame = 1.0 / 60;

    private static void Step(BattleSession session, AutoRoom auto)
    {
        BattleClient client = session.Client;
        session.Advance(Frame);
        auto.Next(client.Room, client.Slot, client.IsHost, 1).SendTo(client);
        client.Update(Frame, LocalCommand.None);
    }

    [Fact]
    public void A_frame_from_a_running_match_has_the_mode_the_clock_and_the_roster()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47101, BattleMode.TeamBattle, Bots: 3, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });
        var auto = new AutoRoom(null, BattleMode.TeamBattle, 3, autoStart: true);
        double startedAt = -1;
        for (int i = 0; i < 12 * 60 && (startedAt < 0 || session.Now - startedAt < 5); i++)
        {
            Step(session, auto);
            if (startedAt < 0 && session.Client.Phase == BattlePhase.InMatch && session.Client.Latest is not null) startedAt = session.Now;
        }
        Assert.True(startedAt >= 0, "the match never started");
        BattleClient client = session.Client;
        var names = new string?[BattleProtocol.MaxPlayers];

        MatchHudFrame frame = MatchHudFrame.From(client, session, ownTeam: 0, names, fps: 60);

        Assert.Equal(BattleMode.TeamBattle, frame.Mode);
        Assert.Equal(MatchPhase.Playing, frame.Phase);
        int expected = ModeRules.DurationTicks(BattleMode.TeamBattle) - 5 * Tank.Sim.SimConstants.TickRate;
        Assert.InRange(frame.TicksLeft, expected - 3 * Tank.Sim.SimConstants.TickRate, expected + 3 * Tank.Sim.SimConstants.TickRate);
        Assert.Matches("^6:[45][0-9]$|^7:00$", HudText.Timer(frame.TicksLeft));
        Assert.Equal(client.Slot, frame.OwnSlot);
        Assert.Equal(4, frame.Players.Count(p => p.Active));   // the player and three bots
        Assert.Same(names, frame.Names);
        Assert.Equal(BannerKind.None, frame.Banner);
        Assert.Equal(PlateKind.TeamKills, frame.Plate.Kind);
        Assert.False(frame.Net.HasServerTick);   // a loopback host has no tick thread to time
        Assert.Equal(60, frame.Net.Fps);
    }

    [Fact]
    public void Before_any_snapshot_the_frame_is_empty_but_safe()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47102, BattleMode.FreeForAll, Bots: 0, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });

        MatchHudFrame frame = MatchHudFrame.From(session.Client, session, 0, new string?[BattleProtocol.MaxPlayers], fps: 0);

        Assert.Equal(0, frame.TicksLeft);
        Assert.Equal(BannerKind.None, frame.Banner);
        Assert.Equal(BattleProtocol.MaxPlayers, frame.Players.Length);
    }

    [Fact]
    public void The_spectate_target_turns_the_banner_to_the_spectate_bar()
    {
        var settings = new HostSettings("Tester", "Tester's room", Port: 47103, BattleMode.Annihilation, Bots: 0, Seed: 7, MatchSeconds: null);
        using BattleSession session = BattleSession.HostLoopback(settings, _ => { });

        MatchHudFrame frame = MatchHudFrame.From(session.Client, session, 0, new string?[BattleProtocol.MaxPlayers], 60, spectateTarget: "机器人 1");

        Assert.Equal(BannerKind.Spectate, frame.Banner);
        Assert.Equal("机器人 1", frame.SpectateTarget);
    }

    [Fact]
    public void A_tank_id_finds_its_slot_among_the_active_players_only()
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        players[2] = new PlayerStats { Active = true, TankId = 7 };
        players[4] = new PlayerStats { Active = false, TankId = 9 };

        Assert.Equal(2, MatchHudFrame.SlotOf(players, 7));
        Assert.Equal(-1, MatchHudFrame.SlotOf(players, 9));
        Assert.Equal(-1, MatchHudFrame.SlotOf(players, 0));
    }
}
```

新建 `tests/Tank.Art.Tests/NetPanelTextTests.cs`：

```csharp
using System.Globalization;
using Tank.Client.Match;
using Tank.Net.Battle;

namespace Tank.Art.Tests;

public class NetPanelTextTests
{
    private static readonly BattleStats Stats = new(
        RttMs: 38, JitterMs: 3.4, InterpDelayMs: 100, PacketLossPercent: 0f, BytesInPerSecond: 7168, BytesOutPerSecond: 1536,
        SnapshotsPerSecond: 30, SnapshotBytes: 236, Extrapolations: 0, Corrections: 14, LastCorrection: 0.031f, MeanCorrection: 0.012,
        MaxCorrection: 0.43f, InputSlackTicks: 2.1, ClockRate: 1.002, DroppedPackets: 3);

    private static string[] Fill(in NetSample sample, out int count)
    {
        var lines = new string[NetPanelText.MaxLines];
        count = NetPanelText.Lines(sample, lines);
        return lines;
    }

    private static string Joined(string[] lines, int count) => string.Join("\n", lines.Take(count));

    [Fact]
    public void The_lines_carry_reconciliation_snapshot_and_traffic_figures()
    {
        string[] lines = Fill(new NetSample(Stats, Resyncs: 1, HasServerTick: false, 0, 0, Fps: 144), out int count);
        string text = Joined(lines, count);

        Assert.Contains("延迟 38 ms", text);
        Assert.Contains("抖动 3.4 ms", text);
        Assert.Contains("插值 100 ms", text);
        Assert.Contains("快照 30/s · 236 B", text);
        Assert.Contains("下行 7.0 KB/s", text);
        Assert.Contains("上行 1.5 KB/s", text);
        Assert.Contains("时钟 ×1.002", text);
        Assert.Contains("余量 2.1 tick", text);
        Assert.Contains("对账 14 次", text);
        Assert.Contains("最近 0.031 m", text);
        Assert.Contains("平均 0.012 m", text);
        Assert.Contains("最大 0.430 m", text);
        Assert.Contains("丢包 3", text);
        Assert.Contains("重同步 1", text);
        Assert.Contains("144 fps", text);
    }

    [Fact]
    public void The_server_tick_line_shows_only_when_this_process_hosts()
    {
        Fill(new NetSample(Stats, 0, HasServerTick: false, 0, 0, 60), out int without);
        string[] lines = Fill(new NetSample(Stats, 0, HasServerTick: true, 0.4, 2.1, 60), out int with);

        Assert.Equal(without + 1, with);
        Assert.Contains("服务器 tick 平均 0.4 ms · 最大 2.1 ms", Joined(lines, with));
        Assert.True(with <= NetPanelText.MaxLines);
    }

    [Fact]
    public void A_comma_decimal_culture_still_prints_dots()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            string[] lines = Fill(new NetSample(Stats, 0, HasServerTick: true, 0.4, 2.1, 60), out int count);
            string text = Joined(lines, count);

            Assert.Contains("0.031 m", text);
            Assert.Contains("抖动 3.4 ms", text);
            Assert.DoesNotContain(",", text);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_clock_text_is_invariant_too()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("3:27", HudText.Timer(207 * 30));
            Assert.Equal("3/5", HudText.Parts(new PlateValues(PlateKind.TeamAlive, 3, 5, 2, 5)).LeftValue);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，`HudBoxes`、`HudRect`、`KillFeed`、`NetSample` 等找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
index e065bc1..b3fa6ef 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
@@ -122,6 +122,19 @@ public partial class Main
         _auto?.Reset();
     }
 
+    /// <summary><c>--hud-demo NAME</c>: just the HUD gallery's screen; an unknown name lists the known ones and exits with code 2.</summary>
+    private void EnterGallery(string name)
+    {
+        if (!HudDemoFrames.TryGet(name, out HudDemo demo))
+        {
+            GD.PrintErr($"Unknown --hud-demo '{name}'; use {string.Join(", ", HudDemoFrames.Names)}.");
+            GetTree().Quit(2);
+            return;
+        }
+        _menu.Visible = false;
+        AddChild(new HudGallery(demo));
+    }
+
     private void EnterSandbox()
     {
         if (_sandbox is not null) return;
PATCH
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.cs b/src/Tank.Client/scripts/Bootstrap/Main.cs
index e53df5e..aa2f484 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.cs
@@ -44,6 +44,11 @@ public partial class Main : Node
         _room.LeaveRequested += () => LeaveSession("已离开房间。");
         AddChild(_room);
 
+        if (_options.HudDemo is { } demo)
+        {
+            EnterGallery(demo);
+            return;   // a gallery run has no session, no update check
+        }
         if (_options.Sandbox) EnterSandbox();
         else if (_options.Host) Host(_menu.PlayerName);
         else if (_options.JoinAddress is { } address) JoinAddress(address);
PATCH
```

新建 `src/Tank.Client/scripts/Match/CenterBanner.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The banner in the middle of the screen (blueprint §4.2). Respawn: a plate at 0.28 of the height with "你被击毁了", the
/// seconds left in big type and "秒后复活" under it. Spectate: a bar at the bottom centre, above the status panel's
/// row, with "观战 name · Q/E 切换". Otherwise nothing. Redraws only when the kind, the second or the target changes.
/// (The results banner is the scoreboard task's.)
/// </summary>
public partial class CenterBanner : HudPanel
{
    private const int CaptionSize = 28, NumberSize = 96, SuffixSize = 24, BarSize = 24;
    private const float CaptionBaseline = 48f, NumberBaseline = 150f, SuffixBaseline = 192f;   // inside the plate, from its top
    private const int PlateRadius = 24, BarRadius = 18;
    private const float PlateAlpha = 0.8f;
    private const string Caption = "你被击毁了", Suffix = "秒后复活";

    private readonly StyleBoxFlat _plate = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), PlateRadius, 8);
    private readonly StyleBoxFlat _bar = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), BarRadius, 8);
    private BannerKind _kind;
    private int _seconds = -1;
    private string _number = "";
    private string? _target;
    private string _barText = "";

    public void Update(in MatchHudFrame frame)
    {
        int seconds = frame.Banner == BannerKind.Respawn ? HudText.CeilSeconds(frame.RespawnIn) : 0;
        string? target = frame.Banner == BannerKind.Spectate ? frame.SpectateTarget : null;
        if (frame.Banner == _kind && seconds == _seconds && target == _target) return;
        if (seconds != _seconds) _number = HudText.RespawnNumber(frame.RespawnIn);
        if (target != _target && target is not null) _barText = HudText.SpectateLine(target);
        (_kind, _seconds, _target) = (frame.Banner, seconds, target);
        QueueRedraw();
    }

    public override void _Draw()
    {
        switch (_kind)
        {
            case BannerKind.Respawn: DrawRespawn(); break;
            case BannerKind.Spectate: DrawSpectate(); break;
        }
    }

    private void DrawRespawn()
    {
        HudRect box = Boxes.Banner;
        DrawStyleBox(_plate, ToRect(box));
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + CaptionBaseline, Caption, CaptionSize, HudStyle.Text);
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + NumberBaseline, _number, NumberSize, HudStyle.Accent, 10);
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + SuffixBaseline, Suffix, SuffixSize, Palette.UiDim.ToColor(), 5);
    }

    private void DrawSpectate()
    {
        HudRect box = Boxes.SpectateBar;
        DrawStyleBox(_bar, ToRect(box));
        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudStyle.Baseline(box.Height / 2, BarSize), _barText, BarSize, HudStyle.Text, 5);
    }
}
```

新建 `src/Tank.Client/scripts/Match/HudGallery.cs`：

```csharp
using Godot;
using Tank.Client.Art;

namespace Tank.Client.Match;

/// <summary>
/// <c>--hud-demo NAME</c>: one screen of the match HUD drawn from canned data (<see cref="HudDemoFrames"/>) over a flat
/// dark background, with no session, so a screenshot shows the layout and is the same every time. The clock the feed
/// reads is frozen at 0, so its lines never expire.
/// </summary>
public partial class HudGallery : Node
{
    private const int BackgroundLayer = 0;

    private readonly HudDemo _demo;
    private MatchHud _hud = null!;

    public HudGallery(HudDemo demo) => _demo = demo;

    public override void _Ready()
    {
        var back = new CanvasLayer { Layer = BackgroundLayer };
        var fill = new ColorRect { Color = Palette.UiBackground.ToColor(), MouseFilter = Control.MouseFilterEnum.Ignore };
        back.AddChild(fill);
        fill.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(back);
        _hud = new MatchHud();
        AddChild(_hud);
        foreach (FeedLine k in _demo.Kills) _hud.OnKill(k.Killer, k.KillerSide, k.Victim, k.VictimSide, k.Friendly, now: 0);
        GetWindow().Title = $"坦克 3D · HUD · {_demo.Name}";
    }

    public override void _Process(double delta) => _hud.Refresh(_demo.Frame, _demo.NetPanelOpen, now: 0);
}
```

新建 `src/Tank.Client/scripts/Match/HudPanel.cs`：

```csharp
using Godot;

namespace Tank.Client.Match;

/// <summary>
/// A full-screen, click-through control that draws one block of the match HUD. It knows where every block goes
/// (<see cref="Boxes"/>, from <see cref="HudLayout"/> for the control's current size) and redraws itself when the
/// window is resized; the block itself redraws only when its content changes (its subclass calls <c>QueueRedraw</c>).
/// </summary>
public abstract partial class HudPanel : Control
{
    protected HudBoxes Boxes { get; private set; }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Resized += OnResized;
        ReadBoxes();
    }

    private void OnResized()
    {
        ReadBoxes();
        QueueRedraw();
    }

    private void ReadBoxes() => Boxes = HudLayout.For(new System.Numerics.Vector2(Size.X, Size.Y));

    protected static Rect2 ToRect(in HudRect r) => new(r.X, r.Y, r.Width, r.Height);
}
```

新建 `src/Tank.Client/scripts/Match/KillFeedView.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The kill feed at the top right (blueprint §4.2): up to five rows, oldest at the top, each a rounded plate right
/// against the margin with "killer 击毁 victim" in the sides' colours (a hidden shooter gives "victim 被击毁", a kill
/// within one side "killer 误伤 victim"). A row the player took part in gets a tint. Each name is cut to
/// <see cref="NameWidth"/> with "…", so even two 24-byte names keep a row inside <see cref="HudLayout.KillWidth"/>, clear
/// of the score plate. Rows expire in <see cref="KillFeed"/>; this view redraws only when the feed's revision changes.
/// </summary>
public partial class KillFeedView : HudPanel
{
    private const int TextSize = 22, TextOutline = 4;
    private const float PadX = 14f, WordGap = 8f;
    private const int RowRadius = 12;
    private const float RowAlpha = 0.7f;
    private const float TintAlpha = 0.45f;

    // Two names, the two-character verb (about 44 at 22 px), two gaps and the padding: 2 × 150 + 44 + 16 + 28 = 388 < 440.
    private const float NameWidth = 150f;

    private static readonly Func<string, float> Measure = text => HudStyle.TextWidth(text, TextSize);   // cached: one delegate, not one per name

    private struct Row
    {
        public string? Killer, Verb, Victim, Tail;
        public Color KillerColor, VictimColor;
        public RowKind Kind;
        public float Width, KillerWidth, VerbWidth, VictimWidth;
    }

    private enum RowKind : byte { Plain, Mine, Died }

    private readonly KillFeed _feed;
    private readonly Row[] _rows = new Row[KillFeed.Capacity];
    private readonly StyleBoxFlat _plain = HudStyle.RoundedBox(new Color(HudStyle.Panel, RowAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _own = HudStyle.RoundedBox(new Color(HudStyle.Accent, TintAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _died = HudStyle.RoundedBox(new Color(Palette.UiDanger.ToColor(), TintAlpha), RowRadius, 6);
    private int _revision = -1;
    private int _count;

    public KillFeedView(KillFeed feed) => _feed = feed;

    /// <summary>Reads the feed at <paramref name="now"/>; the rows are rebuilt (and redrawn) only if it changed.</summary>
    public void Update(double now)
    {
        ReadOnlySpan<FeedLine> lines = _feed.Visible(now);
        if (_feed.Revision == _revision) return;
        _revision = _feed.Revision;
        _count = lines.Length;
        for (int i = 0; i < lines.Length; i++) Fill(ref _rows[i], lines[i]);
        QueueRedraw();
    }

    private static void Fill(ref Row row, in FeedLine line)
    {
        bool hidden = line.Killer is null;
        row.Killer = hidden ? null : HudText.Fit(line.Killer!, NameWidth, Measure);
        row.Verb = hidden ? null : line.Friendly ? FeedLine.FriendlyVerb : FeedLine.KilledVerb;
        row.Victim = HudText.Fit(line.Victim, NameWidth, Measure);
        row.Tail = hidden ? FeedLine.HiddenShooterTail : null;
        row.KillerColor = SideColor(line.KillerSide);
        row.VictimColor = SideColor(line.VictimSide);
        row.KillerWidth = hidden ? 0f : HudStyle.TextWidth(row.Killer!, TextSize) + WordGap;
        row.VerbWidth = hidden ? 0f : HudStyle.TextWidth(row.Verb!, TextSize) + WordGap;
        row.VictimWidth = HudStyle.TextWidth(row.Victim, TextSize);
        row.Width = 2 * PadX + row.KillerWidth + row.VerbWidth + row.VictimWidth + (hidden ? WordGap + HudStyle.TextWidth(FeedLine.HiddenShooterTail, TextSize) : 0f);
        row.Kind = line.KillerSide == Side.Own ? RowKind.Mine : line.VictimSide == Side.Own ? RowKind.Died : RowKind.Plain;
    }

    public override void _Draw()
    {
        for (int i = 0; i < _count; i++)
        {
            ref Row row = ref _rows[i];
            HudRect slot = Boxes.KillRow(i);
            float x = slot.Right - row.Width;
            DrawStyleBox(row.Kind switch { RowKind.Mine => _own, RowKind.Died => _died, _ => _plain }, new Rect2(x, slot.Y, row.Width, slot.Height));
            float baseline = slot.Y + HudStyle.Baseline(slot.Height / 2, TextSize);
            x += PadX;
            if (row.Killer is not null)
            {
                HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Killer, TextSize, row.KillerColor, TextOutline);
                x += row.KillerWidth;
                HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Verb!, TextSize, HudStyle.Text, TextOutline);
                x += row.VerbWidth;
            }
            HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Victim!, TextSize, row.VictimColor, TextOutline);
            if (row.Tail is null) continue;
            x += row.VictimWidth + WordGap;
            HudStyle.DrawOutlined(this, new Vector2(x, baseline), row.Tail, TextSize, HudStyle.Text, TextOutline);
        }
    }

    private static Color SideColor(Side side) => MatchColors.Ui(side).ToColor();
}
```

新建 `src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs`：

```csharp
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>One screen of the HUD gallery: the frame, the kills already on the feed, and whether the F3 panel is open.</summary>
public sealed record HudDemo(string Name, MatchHudFrame Frame, FeedLine[] Kills, bool NetPanelOpen);

/// <summary>
/// Canned HUD frames for <c>--hud-demo NAME</c> (blueprint §2.1): every state the match HUD can show, from fixed data, so
/// a screenshot is the same each time and layout problems show without playing a match. The player is slot 0, team 0.
/// Each frame carries a fake scoreboard (players and names) for the scoreboard task to draw.
/// </summary>
public static class HudDemoFrames
{
    private const string LongName = "Sergeant_Wolfgang_Becker";   // as long as a name gets on the wire: 24 ASCII bytes
    private static readonly string[] Roster = { "阿成", "小林", "老周", "小雨", "机器人 1", "机器人 2", "机器人 3", "机器人 4", "机器人 5", "机器人 6" };

    private static readonly string[] AllNames = { "timer", "feed", "respawn", "spectate", "annihilation", "ffa", "net" };

    /// <summary>The gallery's screen names, in a fixed order.</summary>
    public static IReadOnlyList<string> Names => AllNames;

    public static bool TryGet(string? name, out HudDemo demo)
    {
        demo = name switch
        {
            "timer" => Timer(),
            "feed" => Feed(),
            "respawn" => Respawn(),
            "spectate" => Spectate(),
            "annihilation" => Annihilation(),
            "ffa" => Solo(),
            "net" => Net(),
            _ => null!,
        };
        return demo is not null;
    }

    private static HudDemo Timer() => Make("timer", TeamFrame(Ticks(3, 27), kills0: 4, kills1: 2));

    private static HudDemo Feed()
    {
        MatchHudFrame frame = TeamFrame(Ticks(5, 2), kills0: 7, kills1: 5);
        FeedLine[] kills =
        {
            Line(LongName, Side.Ally, "老周", Side.Enemy),   // a 24-byte name on each side of a row: cut with "…" to keep the feed's width
            Line(null, Side.Enemy, "机器人 2", Side.Enemy),
            Line("阿成", Side.Own, LongName, Side.Ally, friendly: true),
            Line("阿成", Side.Own, "机器人 3", Side.Enemy),
            Line("老周", Side.Enemy, "阿成", Side.Own),
        };
        return new HudDemo("feed", frame, kills, NetPanelOpen: false);
    }

    private static HudDemo Respawn() =>
        Make("respawn", TeamFrame(Ticks(4, 12), kills0: 5, kills1: 6) with { Banner = BannerKind.Respawn, RespawnIn = 6 * SimConstants.TickRate });

    private static HudDemo Spectate()
    {
        PlayerStats[] players = Players(10, i => (byte)(i < 5 ? 0 : 1), i => i is 1 or 2 or 6 or 7, kills: i => i % 4, deaths: i => 1);
        MatchHudFrame frame = Frame(BattleMode.Annihilation, Ticks(2, 36), players, 10) with { Banner = BannerKind.Spectate, SpectateTarget = "机器人 2" };
        return Make("spectate", frame);
    }

    private static HudDemo Annihilation()
    {
        PlayerStats[] players = Players(10, i => (byte)(i < 5 ? 0 : 1), i => i is 0 or 1 or 3 or 7 or 8, kills: i => i % 3, deaths: i => i % 2);
        return Make("annihilation", Frame(BattleMode.Annihilation, Ticks(5, 48), players, 10));
    }

    private static HudDemo Solo()
    {
        PlayerStats[] players = Players(8, i => (byte)i, i => i is 0 or 2 or 5 or 6, kills: i => i == 0 ? 3 : i % 3, deaths: i => i % 2);
        return Make("ffa", Frame(BattleMode.FreeForAll, Ticks(6, 15), players, 8));
    }

    private static HudDemo Net()
    {
        var stats = new BattleStats(38, 3.4, 100, 0f, 7400, 1900, 30, 236, 0, 14, 0.031f, 0.012, 0.43f, 2.1, 1.002, 0);
        MatchHudFrame frame = TeamFrame(Ticks(6, 40), kills0: 2, kills1: 3) with { Net = new NetSample(stats, 0, true, 0.4, 2.1, 144) };
        return new HudDemo("net", frame, Array.Empty<FeedLine>(), NetPanelOpen: true);
    }

    private static HudDemo Make(string name, MatchHudFrame frame) => new(name, frame, Array.Empty<FeedLine>(), NetPanelOpen: false);

    private static MatchHudFrame TeamFrame(int ticksLeft, int kills0, int kills1)
    {
        PlayerStats[] players = Players(6, i => (byte)(i < 3 ? 0 : 1), _ => true, kills: i => i + 1, deaths: i => i % 3);
        MatchHudFrame frame = Frame(BattleMode.TeamBattle, ticksLeft, players, 6);
        var match = new MatchBlock { TeamScore0 = kills0, TeamScore1 = kills1, TicksLeft = ticksLeft };
        return frame with { Plate = HudText.Plate(BattleMode.TeamBattle, 0, 0, match, players) };
    }

    private static MatchHudFrame Frame(BattleMode mode, int ticksLeft, PlayerStats[] players, int count)
    {
        var match = new MatchBlock { TicksLeft = ticksLeft };
        PlateValues plate = HudText.Plate(mode, 0, 0, match, players);
        var names = new string?[BattleProtocol.MaxPlayers];
        Array.Copy(Roster, names, count);
        var net = new NetSample(default, 0, false, 0, 0, 60);
        return new MatchHudFrame(mode, MatchPhase.Playing, ticksLeft, 0, 0, plate, BannerKind.None, 0, null, net, players, names);
    }

    private static PlayerStats[] Players(int count, Func<int, byte> team, Func<int, bool> alive, Func<int, int> kills, Func<int, int> deaths)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < count; i++)
            players[i] = new PlayerStats { Active = true, Team = team(i), TankId = i + 1, Kills = kills(i), Deaths = deaths(i), Alive = alive(i) };
        return players;
    }

    private static FeedLine Line(string? killer, Side killerSide, string victim, Side victimSide, bool friendly = false) =>
        new(killer, killerSide, victim, victimSide, friendly, ExpiresAt: 0);

    private static int Ticks(int minutes, int seconds) => (minutes * 60 + seconds) * SimConstants.TickRate;
}
```

新建 `src/Tank.Client/scripts/Match/Logic/HudLayout.cs`：

```csharp
using System.Numerics;

namespace Tank.Client.Match;

/// <summary>A screen rectangle in the HUD's base units (the 1600×900 canvas, stretched to the window).</summary>
public readonly record struct HudRect(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Vector2 Position => new(X, Y);
    public Vector2 Size => new(Width, Height);
    public float CenterX => X + Width / 2;

    public bool Intersects(HudRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;

    /// <summary>Whether the rectangle lies wholly inside a screen of <paramref name="screen"/>.</summary>
    public bool FitsIn(Vector2 screen) => X >= 0 && Y >= 0 && Right <= screen.X && Bottom <= screen.Y;
}

/// <summary>Where every HUD block goes on one screen size.</summary>
public readonly record struct HudBoxes(
    HudRect ScorePlate, HudRect ModeLabel, HudRect KillFeed, HudRect NetPanel, HudRect Banner, HudRect SpectateBar,
    HudRect Scoreboard, HudRect StatusPanel)
{
    /// <summary>Row <paramref name="index"/> (0 = top) of the kill feed.</summary>
    public HudRect KillRow(int index) => new(KillFeed.X, KillFeed.Y + index * (HudLayout.KillRowHeight + HudLayout.KillRowGap), KillFeed.Width, HudLayout.KillRowHeight);
}

/// <summary>
/// The HUD's layout (blueprint §4.2) in base units: blocks hang off the screen's edges and centre, so a wider or taller
/// window just moves them; only the banner scales with the height (0.28 of it down). The 1600×900 canvas is what
/// <c>canvas_items</c> + <c>expand</c> gives the HUD whatever the window, so nothing here scales by the window's pixels.
/// </summary>
public static class HudLayout
{
    public const float Margin = 24f;
    public const float Top = 16f;

    public const float PlateWidth = 360f, PlateHeight = 72f;
    public const float PlateSideWidth = 112f;   // each score block; the clock takes what is left in the middle
    public const float ModeLabelHeight = 24f, ModeLabelGap = 4f;

    public const int KillRows = 5;
    public const float KillWidth = 440f, KillRowHeight = 36f, KillRowGap = 6f;

    public const float NetWidth = 500f, NetHeight = 280f;

    public const float BannerWidth = 420f, BannerHeight = 210f, BannerTopShare = 0.28f;

    public const float SpectateWidth = 680f, SpectateHeight = 56f, SpectateFromBottom = 36f;

    public const float ScoreboardWidth = 900f, ScoreboardTop = 110f, ScoreboardHeight = 576f;

    /// <summary>The status panel's room, its vehicle name above and the lamp included (see <c>StatusPanel</c>: 24 from the edges, 178 tall, about 360 wide).</summary>
    public const float StatusWidth = 360f, StatusHeight = 178f, StatusNameRoom = 36f;

    public static HudBoxes For(Vector2 size)
    {
        float cx = size.X / 2;
        var plate = new HudRect(cx - PlateWidth / 2, Top, PlateWidth, PlateHeight);
        var label = new HudRect(plate.X, plate.Bottom + ModeLabelGap, PlateWidth, ModeLabelHeight);
        float feedHeight = KillRows * KillRowHeight + (KillRows - 1) * KillRowGap;
        return new HudBoxes(
            plate,
            label,
            new HudRect(size.X - Margin - KillWidth, Top, KillWidth, feedHeight),
            new HudRect(Margin, Top, NetWidth, NetHeight),
            new HudRect(cx - BannerWidth / 2, size.Y * BannerTopShare, BannerWidth, BannerHeight),
            new HudRect(cx - SpectateWidth / 2, size.Y - SpectateFromBottom - SpectateHeight, SpectateWidth, SpectateHeight),
            new HudRect(cx - ScoreboardWidth / 2, ScoreboardTop, ScoreboardWidth, ScoreboardHeight),
            new HudRect(Margin, size.Y - Margin - StatusHeight - StatusNameRoom, StatusWidth, StatusHeight + StatusNameRoom));
    }
}
```

新建 `src/Tank.Client/scripts/Match/Logic/HudText.cs`：

```csharp
using System.Globalization;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>What the score plate's two side blocks count (blueprint §4.2): kills per team, tanks left per team, or the player's own kills and the field.</summary>
public enum PlateKind : byte { TeamKills, TeamAlive, SoloKills }

/// <summary>
/// The numbers behind the score plate's side blocks. <c>Left</c> is the player's side (own team, or the player's own
/// kills); <c>RightOf</c> and <c>LeftOf</c> are 0 where the block shows no "of". A plain value, so a view can tell a change by comparing.
/// </summary>
public readonly record struct PlateValues(PlateKind Kind, int Left, int LeftOf, int Right, int RightOf);

/// <summary>The words on the two side blocks: a small caption over a big value.</summary>
public readonly record struct PlateParts(string LeftCaption, string LeftValue, string RightCaption, string RightValue);

/// <summary>
/// The HUD's words and sums, none of them touching the engine: the match clock (rounded up, so the last second reads 0:01
/// and 0:00 means over), the respawn line, and the score plate per mode. Strings are built here, so a view calls them
/// only when the numbers behind them changed.
/// </summary>
public static class HudText
{
    private const string TeamKillsLeft = "我方", TeamKillsRight = "敌方";
    private const string TeamAliveLeft = "我方存活", TeamAliveRight = "敌方存活";
    private const string SoloKills = "击毁", SoloAlive = "存活";

    /// <summary>What a name cut by <see cref="Fit"/> ends with.</summary>
    public const string Ellipsis = "…";

    /// <summary>Whole seconds in <paramref name="ticks"/>, rounded up: the clock shows a second until it has all gone.</summary>
    public static int CeilSeconds(int ticks) => ticks <= 0 ? 0 : (ticks + SimConstants.TickRate - 1) / SimConstants.TickRate;

    /// <summary>"m:ss" for the time left, rounding up to the next second.</summary>
    public static string Timer(int ticksLeft) => Clock(CeilSeconds(ticksLeft));

    /// <summary>"m:ss" for a whole number of seconds.</summary>
    public static string Clock(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>"N 秒后复活" for the ticks until the own tank is back (rounded up, so it never reads 0 while waiting).</summary>
    public static string RespawnLine(int respawnIn) =>
        string.Create(CultureInfo.InvariantCulture, $"{CeilSeconds(respawnIn)} 秒后复活");

    /// <summary>The big countdown number of the respawn banner.</summary>
    public static string RespawnNumber(int respawnIn) =>
        CeilSeconds(respawnIn).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="text"/> cut to <paramref name="budget"/> as <paramref name="width"/> measures it (the view's font at
    /// its size), with <see cref="Ellipsis"/> where it was cut: whole characters only (a surrogate pair stays together),
    /// at least one kept. A text that fits, or is a single character, comes back as it is, so only a long one allocates.
    /// </summary>
    public static string Fit(string text, float budget, Func<string, float> width)
    {
        if (text.Length <= 1 || width(text) <= budget) return text;
        int length = text.Length;
        string cut;
        do
        {
            length--;
            if (length > 0 && char.IsLowSurrogate(text[length])) length--;   // cut before the pair, not inside it
            cut = string.Concat(text.AsSpan(0, length), Ellipsis);
        }
        while (length > 1 && width(cut) > budget);
        return cut;
    }

    /// <summary>"观战 name · Q/E 切换", the spectating bar's text.</summary>
    public static string SpectateLine(string target) => $"观战 {target} · Q/E 切换";

    /// <summary>
    /// What the plate shows for <paramref name="mode"/>. Team battle: the team kills from <paramref name="match"/>, own
    /// team first. Annihilation: tanks alive of those each side started with. Free-for-all: the player's own kills and
    /// the tanks still alive of the field. <paramref name="players"/> is indexed by room slot.
    /// </summary>
    public static PlateValues Plate(BattleMode mode, byte ownSlot, byte ownTeam, in MatchBlock match, ReadOnlySpan<PlayerStats> players)
    {
        switch (mode)
        {
            case BattleMode.TeamBattle:
                bool first = ownTeam == 0;
                return new PlateValues(PlateKind.TeamKills, first ? match.TeamScore0 : match.TeamScore1, 0, first ? match.TeamScore1 : match.TeamScore0, 0);
            case BattleMode.Annihilation:
                Count(players, ownTeam, out int mine, out int mineOf);
                Count(players, (byte)(1 - ownTeam), out int theirs, out int theirsOf);
                return new PlateValues(PlateKind.TeamAlive, mine, mineOf, theirs, theirsOf);
            default:
                int kills = ownSlot < players.Length ? players[ownSlot].Kills : 0;
                CountAll(players, out int alive, out int total);
                return new PlateValues(PlateKind.SoloKills, kills, 0, alive, total);
        }
    }

    /// <summary>The captions and values for <paramref name="plate"/>; allocates, so call it when the values changed.</summary>
    public static PlateParts Parts(in PlateValues plate) => plate.Kind switch
    {
        PlateKind.TeamKills => new PlateParts(TeamKillsLeft, Number(plate.Left), TeamKillsRight, Number(plate.Right)),
        PlateKind.TeamAlive => new PlateParts(TeamAliveLeft, Fraction(plate.Left, plate.LeftOf), TeamAliveRight, Fraction(plate.Right, plate.RightOf)),
        _ => new PlateParts(SoloKills, Number(plate.Left), SoloAlive, Fraction(plate.Right, plate.RightOf)),
    };

    // T7 hooks in here: the results titles ("胜利", "失败", "平局", the free-for-all's winner) belong with the plate's words.

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Fraction(int alive, int of) => string.Create(CultureInfo.InvariantCulture, $"{alive}/{of}");

    private static void Count(ReadOnlySpan<PlayerStats> players, byte team, out int alive, out int total)
    {
        alive = total = 0;
        foreach (PlayerStats p in players)
        {
            if (!p.Active || p.Team != team) continue;
            total++;
            if (p.Alive) alive++;
        }
    }

    private static void CountAll(ReadOnlySpan<PlayerStats> players, out int alive, out int total)
    {
        alive = total = 0;
        foreach (PlayerStats p in players)
        {
            if (!p.Active) continue;
            total++;
            if (p.Alive) alive++;
        }
    }
}
```

新建 `src/Tank.Client/scripts/Match/Logic/KillFeed.cs`：

```csharp
namespace Tank.Client.Match;

/// <summary>
/// One line of the kill feed: who knocked out whom, each with the side they are on (for the colours). A shooter the
/// server hid from the player (id 0) has no name: the line then reads "victim 被击毁". <c>Friendly</c> is a kill within one side.
/// </summary>
public readonly record struct FeedLine(string? Killer, Side KillerSide, string Victim, Side VictimSide, bool Friendly, double ExpiresAt)
{
    public const string KilledVerb = "击毁", FriendlyVerb = "误伤", HiddenShooterTail = "被击毁";

    /// <summary>The whole line as text; allocates, so read it when the feed changed.</summary>
    public string Text => Killer is null
        ? $"{Victim} {HiddenShooterTail}"
        : $"{Killer} {(Friendly ? FriendlyVerb : KilledVerb)} {Victim}";
}

/// <summary>
/// The kill feed's list (blueprint §3.3, §4.2): the newest kills, at most <see cref="Capacity"/>, each gone
/// <see cref="Lifetime"/> seconds after it was added; the oldest goes when a sixth comes. Order is oldest first, newest
/// last. It lives in a fixed array, so adding and reading allocate nothing; <see cref="Revision"/> changes whenever the
/// visible lines do, so a view redraws only then.
/// </summary>
public sealed class KillFeed
{
    public const int Capacity = 5;
    public const double Lifetime = 5.0;   // s

    private readonly FeedLine[] _lines = new FeedLine[Capacity];
    private int _count;

    /// <summary>Changes whenever a line is added or has expired.</summary>
    public int Revision { get; private set; }

    /// <summary>Whether a kill between these sides is within one side (the player's own or an ally's, never an enemy's).</summary>
    public static bool IsFriendly(Side killer, Side victim) => killer != Side.Enemy && victim != Side.Enemy;

    /// <param name="killer">The shooter's name, or null when the shooter is unknown (id 0).</param>
    /// <param name="now">Seconds on the clock the caller reads <see cref="Visible"/> with.</param>
    public void Add(string? killer, Side killerSide, string victim, Side victimSide, bool friendly, double now)
    {
        if (_count == Capacity)
        {
            Array.Copy(_lines, 1, _lines, 0, Capacity - 1);
            _count--;
        }
        _lines[_count++] = new FeedLine(killer, killerSide, victim, victimSide, friendly, now + Lifetime);
        Revision++;
    }

    /// <summary>The lines still showing at <paramref name="now"/>, oldest first; valid until the next call.</summary>
    public ReadOnlySpan<FeedLine> Visible(double now)
    {
        int expired = 0;
        while (expired < _count && _lines[expired].ExpiresAt <= now) expired++;
        if (expired > 0)
        {
            Array.Copy(_lines, expired, _lines, 0, _count - expired);
            _count -= expired;
            Revision++;
        }
        return new ReadOnlySpan<FeedLine>(_lines, 0, _count);
    }
}
```

新建 `src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs`：

```csharp
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>What the centre banner shows.</summary>
public enum BannerKind : byte { None, Respawn, Spectate }

/// <summary>
/// Everything the match HUD needs for one frame (blueprint §2.1): a plain value the scene fills from the client and the
/// gallery from canned data. <c>Players</c> (indexed by room slot) and <c>Names</c> are the caller's arrays, handed on by
/// reference for the scoreboard; nothing here is copied per frame.
/// </summary>
public readonly record struct MatchHudFrame(
    BattleMode Mode,
    MatchPhase Phase,
    int TicksLeft,
    byte OwnSlot,
    byte OwnTeam,
    PlateValues Plate,
    BannerKind Banner,
    int RespawnIn,
    string? SpectateTarget,
    NetSample Net,
    PlayerStats[] Players,
    string?[] Names)
{
    /// <summary>The slot that has <paramref name="tankId"/> on the scoreboard, or -1 for a tank nobody plays (or has left).</summary>
    public static int SlotOf(PlayerStats[] players, int tankId)
    {
        if (tankId == 0) return -1;
        for (int i = 0; i < players.Length; i++)
            if (players[i].Active && players[i].TankId == tankId) return i;
        return -1;
    }

    /// <summary>
    /// The frame from the client's newest snapshot. The respawn banner shows while the own tank is a wreck that is coming
    /// back (team battle only); <paramref name="spectateTarget"/> turns the bottom bar on. <paramref name="names"/> is the
    /// room's names by slot, <paramref name="fps"/> the engine's frame rate. Reads only; allocates nothing.
    /// </summary>
    public static MatchHudFrame From(BattleClient client, BattleSession? session, byte ownTeam, string?[] names, int fps, string? spectateTarget = null)
    {
        BattleMode mode = client.MatchInfo?.Mode ?? client.Room?.Mode ?? BattleMode.TeamBattle;
        MatchBlock match = client.Match;
        PlayerStats[] players = client.Latest?.Players ?? NoPlayers;
        byte slot = client.Slot;
        var plate = HudText.Plate(mode, slot, ownTeam, match, players);
        BannerKind banner = BannerKind.None;
        if (spectateTarget is not null) banner = BannerKind.Spectate;
        else if (match.RespawnIn > 0 && ModeRules.Respawns(mode) && match.Phase == MatchPhase.Playing) banner = BannerKind.Respawn;
        (double avg, double max) = session?.ServerTick ?? default;
        var net = new NetSample(client.Stats, client.Resyncs, session?.ServerTick is not null, avg, max, fps);
        return new MatchHudFrame(mode, match.Phase, match.TicksLeft, slot, ownTeam, plate, banner, match.RespawnIn, spectateTarget, net, players, names);
    }

    private static readonly PlayerStats[] NoPlayers = new PlayerStats[BattleProtocol.MaxPlayers];
}
```

新建 `src/Tank.Client/scripts/Match/Logic/NetPanelText.cs`：

```csharp
using System.Globalization;
using Tank.Net.Battle;

namespace Tank.Client.Match;

/// <summary>
/// What the F3 panel reports: the client's own figures (<see cref="BattleStats"/>, resyncs) plus, when this process
/// hosts on a thread, the server's tick cost, and the frame rate the engine measures.
/// </summary>
public readonly record struct NetSample(BattleStats Stats, int Resyncs, bool HasServerTick, double ServerTickAvgMs, double ServerTickMaxMs, int Fps);

/// <summary>
/// The F3 panel's lines (blueprint §4.2), culture-invariant so a comma-decimal locale still prints dots. The caller owns
/// the array and refills it only when it wants fresh text (the panel does so a few times a second), so nothing is
/// built while the panel is closed.
/// </summary>
public static class NetPanelText
{
    public const int MaxLines = 9;
    private const double BytesPerKb = 1024.0;

    /// <summary>Writes the lines for <paramref name="s"/> into <paramref name="into"/> (at least <see cref="MaxLines"/> long) and returns how many.</summary>
    public static int Lines(in NetSample s, string[] into)
    {
        BattleStats t = s.Stats;
        CultureInfo c = CultureInfo.InvariantCulture;
        int n = 0;
        into[n++] = string.Create(c, $"延迟 {t.RttMs} ms · 抖动 {t.JitterMs:0.0} ms · 插值 {t.InterpDelayMs:0} ms");
        into[n++] = string.Create(c, $"快照 {t.SnapshotsPerSecond}/s · {t.SnapshotBytes} B");
        into[n++] = string.Create(c, $"下行 {t.BytesInPerSecond / BytesPerKb:0.0} KB/s · 上行 {t.BytesOutPerSecond / BytesPerKb:0.0} KB/s");
        into[n++] = string.Create(c, $"时钟 ×{t.ClockRate:0.000} · 余量 {t.InputSlackTicks:0.0} tick");
        into[n++] = string.Create(c, $"对账 {t.Corrections} 次 · 最近 {t.LastCorrection:0.000} m");
        into[n++] = string.Create(c, $"修正 平均 {t.MeanCorrection:0.000} m · 最大 {t.MaxCorrection:0.000} m");
        into[n++] = string.Create(c, $"丢包 {t.DroppedPackets} · 重同步 {s.Resyncs}");
        if (s.HasServerTick) into[n++] = string.Create(c, $"服务器 tick 平均 {s.ServerTickAvgMs:0.0} ms · 最大 {s.ServerTickMaxMs:0.0} ms");
        into[n++] = string.Create(c, $"帧率 {s.Fps} fps");
        return n;
    }
}
```

新建 `src/Tank.Client/scripts/Match/MatchHud.cs`：

```csharp
using Godot;

namespace Tank.Client.Match;

/// <summary>
/// The match HUD's root (blueprint §1.1): a canvas layer above the combat HUD that owns the score plate, the kill feed,
/// the centre banner and the F3 panel, and hands each the part of a <see cref="MatchHudFrame"/> it draws. The scene calls
/// <see cref="Refresh"/> once a frame and <see cref="OnKill"/> for every kill event; the gallery does the same with canned
/// data. While the scoreboard is up (<see cref="SetScoreboardOpen"/>, the scoreboard task's) the kill feed and the F3
/// panel step aside: the scoreboard is wider than the room between them.
/// </summary>
public partial class MatchHud : CanvasLayer
{
    private const int HudLayer = 2;

    private readonly KillFeed _feed = new();
    private ScorePlate _plate = null!;
    private KillFeedView _feedView = null!;
    private CenterBanner _banner = null!;
    private NetPanel _net = null!;
    private bool _scoreboardOpen;
    private bool _netOpen;

    public MatchHud() => Layer = HudLayer;

    public override void _Ready()
    {
        _plate = Add(new ScorePlate());
        _feedView = Add(new KillFeedView(_feed));
        _banner = Add(new CenterBanner());
        _net = Add(new NetPanel { Visible = false });
    }

    /// <summary>The scoreboard shows or hides: the kill feed and the F3 panel hide while it shows.</summary>
    public void SetScoreboardOpen(bool open)
    {
        if (open == _scoreboardOpen) return;
        _scoreboardOpen = open;
        ApplyVisibility();
    }

    /// <summary>A kill for the feed; <paramref name="killer"/> is null when the shooter is hidden from the player.</summary>
    public void OnKill(string? killer, Side killerSide, string victim, Side victimSide, bool friendly, double now) =>
        _feed.Add(killer, killerSide, victim, victimSide, friendly, now);

    /// <summary>One frame: <paramref name="now"/> is the scene's clock in seconds (the feed's lines expire by it).</summary>
    public void Refresh(in MatchHudFrame frame, bool netPanelOpen, double now)
    {
        _plate.Update(frame);
        _feedView.Update(now);
        _banner.Update(frame);
        if (netPanelOpen != _netOpen)
        {
            _netOpen = netPanelOpen;
            if (!netPanelOpen) _net.Reset();
            ApplyVisibility();
        }
        if (netPanelOpen) _net.Update(frame.Net, now);
    }

    private void ApplyVisibility()
    {
        _feedView.Visible = !_scoreboardOpen;
        _net.Visible = _netOpen && !_scoreboardOpen;
    }

    private T Add<T>(T node) where T : Node
    {
        AddChild(node);
        return node;
    }
}
```

修改 `src/Tank.Client/scripts/Match/MatchMain.Events.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.Events.cs b/src/Tank.Client/scripts/Match/MatchMain.Events.cs
index 4090116..13217ac 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.Events.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.Events.cs
@@ -3,7 +3,9 @@ using Tank.Client.Combat;
 using Tank.Client.World;
 using Tank.Sim;
 using Tank.Sim.Combat;
+using Tank.Net.Battle;
 using Tank.Sim.Maps;
+using Tank.Sim.Modes;
 
 namespace Tank.Client.Match;
 
@@ -22,10 +24,12 @@ public partial class MatchMain
     private const float BlastHeight = 1.2f;
     private const float FireShake = 0.08f;
     private const float FireShakeSeconds = 0.15f;
+    private const string UnknownName = "未知玩家";
 
     private void OnEvent(in GameEvent e)
     {
         Battlefield field = _field!;
+        if (e.Type == GameEventType.TankDestroyed) FeedKill(e);
         switch (e.Type)
         {
             case GameEventType.ShellFired:
@@ -88,4 +92,28 @@ public partial class MatchMain
         _tank?.Restore();
         _input.ResetAmmo();
     }
+
+    /// <summary>
+    /// A kill for the feed (blueprint §3.3): names from the scoreboard's tank ids by room slot, sides from the teams as the
+    /// scene colours tanks. A shooter the server hid (id 0), or one that has since left, gives a line without a killer.
+    /// </summary>
+    private void FeedKill(in GameEvent e)
+    {
+        PlayerStats[]? players = _client.Latest?.Players;
+        if (players is null) return;
+        byte ownTeam = OwnTeam(_client.Own);
+        (string victim, Side victimSide) = Describe(players, e.TargetTankId, ownTeam);
+        string? killer = null;
+        Side killerSide = Side.Enemy;
+        if (MatchHudFrame.SlotOf(players, e.TankId) >= 0) (killer, killerSide) = Describe(players, e.TankId, ownTeam);
+        _hud.OnKill(killer, killerSide, victim, victimSide, killer is not null && KillFeed.IsFriendly(killerSide, victimSide), _time);
+    }
+
+    private (string Name, Side Side) Describe(PlayerStats[] players, int tankId, byte ownTeam)
+    {
+        int slot = MatchHudFrame.SlotOf(players, tankId);
+        string name = slot >= 0 && _names[slot] is { } known ? known : UnknownName;
+        byte team = slot >= 0 ? players[slot].Team : MatchRules.NoTeam;
+        return (name, MatchColors.SideOf(Mode, ownTeam, team, isOwn: tankId == _client.OwnTankId));
+    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.cs b/src/Tank.Client/scripts/Match/MatchMain.cs
index d410181..eca7f52 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.cs
@@ -54,6 +54,7 @@ public partial class MatchMain : Node3D
     private BattleFx _fx = null!;
     private OrbitCamera _camera = null!;
     private CombatHud _combat = null!;
+    private MatchHud _hud = null!;
     private CanvasLayer _loading = null!;
     private double _time;
     private float _aimYaw;
@@ -108,10 +109,12 @@ public partial class MatchMain : Node3D
         else if (!ReferenceEquals(field, _field)) TakeField(field);   // a resync swapped the field instance (R2)
         if (!_client.HasTank && _client.Latest is null)
         {
+            _hud.Visible = false;
             _loading.Visible = true;   // the match start is here, the first snapshot (and so the own tank's spec) not yet (R3)
             return;
         }
         _loading.Visible = false;
+        _hud.Visible = true;
         RefreshNames();
 
         float fdt = (float)dt;
@@ -135,6 +138,7 @@ public partial class MatchMain : Node3D
         _combat.ShowOwn(hasTank);
         _combat.Refresh(own, spotted: hasTank && _client.Spotted && !own.IsDestroyed);
         _actors.FillBars(_combat.TankBars, BattleProtocol.MaxPlayers, _names);
+        _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond()), _input.NetPanelOpen, _time);   // Tab (the scoreboard) is wired by its own task
         _automation.EndFrame(delta);
     }
 
@@ -171,6 +175,7 @@ public partial class MatchMain : Node3D
         if (_options.Sniper) _camera.ToggleSniper();
         _combat = Add(new CombatHud());
         _combat.Build(_camera, field, _client.OwnTankId, BattleProtocol.MaxPlayers, _headOf);
+        _hud = Add(new MatchHud());
     }
 
     private void TakeField(Battlefield field)
PATCH
```

新建 `src/Tank.Client/scripts/Match/NetPanel.cs`：

```csharp
using Godot;
using Tank.Client.Overlay;

namespace Tank.Client.Match;

/// <summary>
/// The F3 panel at the top left (blueprint §4.2): a title and the lines of <see cref="NetPanelText"/> in a dark plate.
/// The text is rebuilt at most <see cref="RefreshSeconds"/> apart and only while the panel is open, and it redraws only
/// when the text changed.
/// </summary>
public partial class NetPanel : HudPanel
{
    private const double RefreshSeconds = 0.25;
    private const int TitleSize = 22, LineSize = 20;
    private const float Pad = 16f, TitleBaseline = 30f, FirstLine = 62f, LineStep = 24f;
    private const int PanelRadius = 16;
    private const string Title = "网络 · F3";

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(new Color(HudStyle.Panel, 0.85f), PanelRadius, 8);
    private readonly string[] _lines = new string[NetPanelText.MaxLines];
    private double _builtAt = double.NegativeInfinity;
    private int _count;

    /// <summary>Takes a fresh sample if the last one is old enough; call every frame while the panel is open.</summary>
    public void Update(in NetSample sample, double now)
    {
        if (now - _builtAt < RefreshSeconds) return;
        _builtAt = now;
        _count = NetPanelText.Lines(sample, _lines);
        QueueRedraw();
    }

    /// <summary>The panel closed: the next time it opens it takes a sample at once.</summary>
    public void Reset() => _builtAt = double.NegativeInfinity;

    public override void _Draw()
    {
        if (_count == 0) return;
        HudRect box = Boxes.NetPanel;
        DrawStyleBox(_box, ToRect(box));
        HudStyle.DrawOutlined(this, new Vector2(box.X + Pad, box.Y + TitleBaseline), Title, TitleSize, HudStyle.Accent, 4);
        for (int i = 0; i < _count; i++)
            HudStyle.DrawOutlined(this, new Vector2(box.X + Pad, box.Y + FirstLine + i * LineStep), _lines[i], LineSize, HudStyle.Text, 4);
    }
}
```

新建 `src/Tank.Client/scripts/Match/ScorePlate.cs`：

```csharp
using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Client.UI;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The plate at the top centre (blueprint §4.2): the clock in the middle with a score block to either side (a small
/// caption over a big value, the words per mode from <see cref="HudText"/>), and the mode's name in small type under it.
/// The clock turns red in its last half minute. Redraws only when the clock's second, a score or the mode changes.
/// </summary>
public partial class ScorePlate : HudPanel
{
    private const int ClockSize = 40, ValueSize = 36, CaptionSize = 16, LabelSize = 18;
    private const int UrgentSeconds = 30;
    private const float CaptionBaseline = 24f, ValueBaseline = 60f;   // inside the plate, from its top
    private const float DividerInset = 14f;
    private const int PlateRadius = 20;
    private const float PlateAlpha = 0.85f;

    private readonly StyleBoxFlat _box = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), PlateRadius, 8);
    private int _seconds = -1;
    private PlateValues _plate;
    private bool _hasPlate;
    private BattleMode _mode;
    private bool _hasMode;
    private string _clock = "";
    private string _label = "";
    private PlateParts _parts;
    private Color _leftColor, _rightColor;

    public void Update(in MatchHudFrame frame)
    {
        int seconds = HudText.CeilSeconds(frame.TicksLeft);
        bool changed = false;
        if (seconds != _seconds)
        {
            _seconds = seconds;
            _clock = HudText.Clock(seconds);
            changed = true;
        }
        if (!_hasPlate || frame.Plate != _plate)
        {
            _plate = frame.Plate;
            _hasPlate = true;
            _parts = HudText.Parts(_plate);
            (_leftColor, _rightColor) = SideColors(_plate.Kind);
            changed = true;
        }
        if (!_hasMode || frame.Mode != _mode)
        {
            _mode = frame.Mode;
            _hasMode = true;
            _label = ModeText.Name(_mode);
            changed = true;
        }
        if (changed) QueueRedraw();
    }

    public override void _Draw()
    {
        if (!_hasPlate) return;
        HudRect plate = Boxes.ScorePlate;
        DrawStyleBox(_box, ToRect(plate));
        float middle = plate.CenterX;
        float leftX = plate.X + HudLayout.PlateSideWidth / 2, rightX = plate.Right - HudLayout.PlateSideWidth / 2;
        Color clockColor = _seconds <= UrgentSeconds ? Palette.UiDanger.ToColor() : HudStyle.Text;
        HudStyle.DrawOutlinedCentered(this, middle, plate.Y + HudStyle.Baseline(HudLayout.PlateHeight / 2, ClockSize), _clock, ClockSize, clockColor);
        DrawBlock(leftX, plate.Y, _parts.LeftCaption, _parts.LeftValue, _leftColor);
        DrawBlock(rightX, plate.Y, _parts.RightCaption, _parts.RightValue, _rightColor);
        var divider = new Color(HudStyle.OutlineColor, 0.7f);
        float top = plate.Y + DividerInset, bottom = plate.Bottom - DividerInset;
        DrawLine(new Vector2(plate.X + HudLayout.PlateSideWidth, top), new Vector2(plate.X + HudLayout.PlateSideWidth, bottom), divider, 2f);
        DrawLine(new Vector2(plate.Right - HudLayout.PlateSideWidth, top), new Vector2(plate.Right - HudLayout.PlateSideWidth, bottom), divider, 2f);
        HudStyle.DrawOutlinedCentered(this, middle, Boxes.ModeLabel.Y + HudStyle.Baseline(HudLayout.ModeLabelHeight / 2, LabelSize), _label, LabelSize, HudStyle.Text, 4);
    }

    private void DrawBlock(float centerX, float top, string caption, string value, Color color)
    {
        HudStyle.DrawOutlinedCentered(this, centerX, top + CaptionBaseline, caption, CaptionSize, Palette.UiDim.ToColor(), 4);
        HudStyle.DrawOutlinedCentered(this, centerX, top + ValueBaseline, value, ValueSize, color);
    }

    /// <summary>The player's side in blue-grey and the enemy's in red for the team modes; the player's kills in the accent colour for a free-for-all.</summary>
    private static (Color Left, Color Right) SideColors(PlateKind kind) => kind == PlateKind.SoloKills
        ? (HudStyle.Accent, HudStyle.Text)
        : (MatchColors.Ui(Side.Ally).ToColor(), MatchColors.Ui(Side.Enemy).ToColor());
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 758、Net 210。

- [ ] **Step 5：截图验证**

```bash
mkdir -p build/shots/t6
for d in timer feed respawn spectate annihilation ffa net; do
  $S/godot_run.sh $W "" 20 -- --no-update --hud-demo $d --screenshot $W/build/shots/t6/demo-$d.png --screenshot-at 2 --quit-after 4
done
$S/godot_run.sh $W "" 40 -- --no-update --host --bots 3 --autostart --netgraph --screenshot $W/build/shots/t6/f3.png --screenshot-at 8 --quit-after 12
```

Expected：
- `demo-timer`：顶部比分板，计时 m:ss（30 秒内红色），团队模式的我方 / 敌方击毁。
- `demo-feed`：右上击毁提示，含“X 被击毁”和误伤两种文案，最多 5 行。
- `demo-respawn`：屏幕中部的复活倒计时横幅。
- `demo-spectate`：观战提示条。
- `demo-annihilation`：比分板是双方存活数；`demo-ffa`：击毁 / 存活。
- `demo-net`：F3 面板，含对账次数 / 最近 / 平均 / 最大、快照字节、服务器 tick。
- `f3.png`：真对局里 F3 面板打开，数字合理（重同步 0）。
- 所有块都不越出 1600×900 画面、彼此不重叠（Tab 计分板除外，它出现时隐藏击毁提示和 F3）。

- [ ] **Step 6：提交**

```bash
git add -A src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): the match HUD — timer and score plate per mode, kill feed from destroy events, respawn and spectate banners, the F3 net panel with reconciliation and snapshot figures, a HUD gallery (--hud-demo) for deterministic layout screenshots"
```

---

### Task 7：计分板与结算

蓝图 §5 T7。`Logic/ScoreboardModel`（零分配写入 span；团队模式我方在前、击毁降序 / 阵亡升序 / 槽位；各自为战局中存活优先，局终用服务器给的 `Rank`；离队的不列；缺名写“玩家 N”）；`Logic/ResultsClock`（结算倒计时自己从看到 `Ended` 起数 `ResultsTicks`——`TicksLeft` 在超时时为 0、提前结束时停在剩余时间，不能用）；`ScoreboardView`（Tab 显示，结算时放到结算位置）；结算横幅（胜利 / 失败 / 平局；各自为战“第 N 名”+ 幸存者 / 无人幸存 / 时间到血量领先；“N 秒后返回房间 · Enter 立即返回”仅房主）；结算期间隐藏准星、状态面板、比分板，释放鼠标，输入为空；Enter 每帧取走、只在结算时生效（对局中误按的 Enter 不会让房主一出结算就回房间）；计分板的名字截断复用 `HudText.Fit`；计分板放在 y 120，不盖住模式标签。画廊加 `scoreboard|results-win|results-lose|results-draw|results-ffa`。

**Files:**
- Create: `src/Tank.Client/scripts/Match/ScoreboardView.cs`、`Match/Logic/{ResultsClock,ScoreboardModel}.cs`
- Modify: `Match/{CenterBanner,HudGallery,MatchHud,MatchInput,MatchMain,MatchMain.Camera}.cs`、`Match/Logic/{HudDemoFrames,HudLayout,HudText,MatchHudFrame}.cs`
- Test: 新建 `tests/Tank.Art.Tests/ScoreboardModelTests.cs`；修改 `HudDemoFramesTests.cs`、`HudLayoutTests.cs`、`HudTextTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/HudDemoFramesTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/HudDemoFramesTests.cs b/tests/Tank.Art.Tests/HudDemoFramesTests.cs
index 5fd9bef..d5b5a85 100644
--- a/tests/Tank.Art.Tests/HudDemoFramesTests.cs
+++ b/tests/Tank.Art.Tests/HudDemoFramesTests.cs
@@ -19,7 +19,7 @@ public class HudDemoFramesTests
     [Fact]
     public void The_gallery_has_the_screens_the_blueprint_names()
     {
-        foreach (string name in new[] { "timer", "feed", "respawn", "spectate", "annihilation", "ffa" })
+        foreach (string name in new[] { "timer", "feed", "respawn", "spectate", "annihilation", "ffa", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa" })
             Assert.Contains(name, HudDemoFrames.Names);
     }
 
@@ -48,14 +48,16 @@ public class HudDemoFramesTests
             if (!p.Active) { Assert.True(string.IsNullOrEmpty(frame.Names[slot])); continue; }
             Assert.False(string.IsNullOrWhiteSpace(frame.Names[slot]), $"slot {slot} has no name");
             Assert.True(p.Kills >= 0 && p.Deaths >= 0);
-            Assert.Equal(0, p.Rank);   // nobody is ranked while the match runs
+            if (frame.Phase == MatchPhase.Playing) Assert.Equal(0, p.Rank);   // nobody is ranked while the match runs
             Assert.True(p.TankId > 0);
             if (ModeRules.HasTeams(frame.Mode)) Assert.InRange(p.Team, 0, 1);
         }
         Assert.Equal(active, frame.Players.Where(p => p.Active).Select(p => p.TankId).Distinct().Count());
         Assert.True(frame.Players[frame.OwnSlot].Active);
-        Assert.True(frame.TicksLeft > 0);
+        Assert.True(frame.Phase == MatchPhase.Ended || frame.TicksLeft > 0);
         Assert.True(frame.TicksLeft <= ModeRules.DurationTicks(frame.Mode));
+        Assert.Equal(frame.Phase == MatchPhase.Ended, frame.Banner == BannerKind.Results);
+        Assert.Equal(1, ScoreboardRows(frame).Count(r => r.Side == Side.Own));
         Assert.True(demo.Kills.Length <= KillFeed.Capacity);
         foreach (FeedLine line in demo.Kills)
         {
@@ -65,6 +67,9 @@ public class HudDemoFramesTests
         }
     }
 
+    private static IReadOnlyList<ScoreRow> ScoreboardRows(MatchHudFrame f) =>
+        ScoreboardModel.Build(f.Mode, f.OwnSlot, f.OwnTeam, f.Players, slot => f.Names[slot], f.Phase == MatchPhase.Ended, f.RespawnIn);
+
     [Theory]
     [MemberData(nameof(Demos))]
     public void The_plate_in_every_demo_matches_its_players(string name)
@@ -103,4 +108,49 @@ public class HudDemoFramesTests
         HudDemoFrames.TryGet("net", out HudDemo net);
         Assert.True(net.NetPanelOpen);
     }
+
+    [Fact]
+    public void The_scoreboard_screen_is_a_team_battle_with_mixed_teams_and_one_down_on_each_side()
+    {
+        HudDemoFrames.TryGet("scoreboard", out HudDemo demo);
+        var rows = ScoreboardRows(demo.Frame);
+
+        Assert.True(demo.ScoreboardOpen);
+        Assert.Equal(BattleMode.TeamBattle, demo.Frame.Mode);
+        Assert.Equal(8, rows.Count);
+        Assert.Equal(4, rows.Count(r => r.Side != Side.Enemy));
+        Assert.Equal(2, rows.Count(r => !r.Alive));
+        Assert.Equal(0, rows[0].Team);   // the own team on top
+        Assert.False(demo.Frame.Players.Where(p => p.Active).Select(p => p.Team).SequenceEqual(demo.Frame.Players.Where(p => p.Active).Select(p => p.Team).OrderBy(t => t)));   // slots are not grouped by team
+    }
+
+    [Theory]
+    [InlineData("results-win", "胜利")]
+    [InlineData("results-lose", "失败")]
+    [InlineData("results-draw", "平局")]
+    public void The_team_results_screens_show_their_verdict(string name, string title)
+    {
+        HudDemoFrames.TryGet(name, out HudDemo demo);
+        MatchHudFrame f = demo.Frame;
+
+        Assert.Equal(MatchPhase.Ended, f.Phase);
+        Assert.False(demo.ScoreboardOpen);   // the results show the table by themselves
+        Assert.Equal(title, HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names).Title);
+        Assert.InRange(f.Results.TicksLeft, 1, ModeRules.ResultsTicks);
+    }
+
+    [Fact]
+    public void The_free_for_all_results_rank_six_players_from_one_to_six_with_the_player_third()
+    {
+        HudDemoFrames.TryGet("results-ffa", out HudDemo demo);
+        MatchHudFrame f = demo.Frame;
+        var rows = ScoreboardRows(f);
+
+        Assert.Equal(6, rows.Count);
+        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, rows.Select(r => r.Rank));
+        Assert.Equal(f.Players.Where(p => p.Active).Select(p => (int)p.Rank).Order(), Enumerable.Range(1, 6));   // unique, from the server
+        Assert.Equal(3, rows.Single(r => r.Side == Side.Own).Rank);
+        Assert.Equal("第 3 名", HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names).Title);
+        Assert.Equal("机器人 1 是最后的幸存者", HudText.Results(f.Mode, f.OwnSlot, f.OwnTeam, f.Results, f.Plate, f.Players, f.Names).Subtitle);
+    }
 }
PATCH
```

修改 `tests/Tank.Art.Tests/HudLayoutTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/HudLayoutTests.cs b/tests/Tank.Art.Tests/HudLayoutTests.cs
index 4b0378a..37dad95 100644
--- a/tests/Tank.Art.Tests/HudLayoutTests.cs
+++ b/tests/Tank.Art.Tests/HudLayoutTests.cs
@@ -21,6 +21,8 @@ public class HudLayoutTests
         yield return ("banner", b.Banner);
         yield return ("spectate bar", b.SpectateBar);
         yield return ("scoreboard", b.Scoreboard);
+        yield return ("results", b.Results);
+        yield return ("results table", b.ResultsTable);
         yield return ("status panel", b.StatusPanel);
     }
 
@@ -77,9 +79,33 @@ public class HudLayoutTests
         HudBoxes b = HudLayout.For(new Vector2(w, h));
         Assert.Equal(w / 2, b.Scoreboard.CenterX, precision: 3);
         Assert.Equal(900, b.Scoreboard.Width);
-        Assert.Equal(110, b.Scoreboard.Y);
-        Assert.True(b.Scoreboard.Y > b.ScorePlate.Bottom);
-        Assert.True(b.Scoreboard.Y < b.ModeLabel.Bottom);   // the mode label is the one thing it covers at the top
+        Assert.Equal(120, b.Scoreboard.Y);
+        Assert.True(b.Scoreboard.Y >= b.ModeLabel.Bottom);   // clear of the plate and its mode label
+        Assert.False(b.Scoreboard.Intersects(b.ScorePlate));
+        Assert.False(b.Scoreboard.Intersects(b.ModeLabel));
+        // At 1600x900 a full 10-player board's bottom-left corner (34 x 24) reaches into the status panel's room; it is a rounded, translucent corner and the panel is under it.
+    }
+
+    [Theory]
+    [MemberData(nameof(Screens))]
+    public void The_results_column_is_960_wide_from_y_60_and_holds_its_table_and_footer(float w, float h)
+    {
+        HudBoxes b = HudLayout.For(new Vector2(w, h));
+        Assert.Equal(w / 2, b.Results.CenterX, precision: 3);
+        Assert.Equal(960, b.Results.Width);
+        Assert.Equal(60, b.Results.Y);
+        Assert.Equal(780, b.Results.Height);
+        Assert.Equal(224, b.ResultsTable.Y);
+        Assert.Equal(b.Scoreboard.Size, b.ResultsTable.Size);
+        Assert.Equal(b.Scoreboard.CenterX, b.ResultsTable.CenterX, precision: 3);
+        Assert.True(b.ResultsTable.Bottom <= b.Results.Bottom);
+        float title = b.Results.Y + HudLayout.ResultsTitleBaseline, subtitle = b.Results.Y + HudLayout.ResultsSubtitleBaseline, footer = b.Results.Y + HudLayout.ResultsFooterBaseline;
+        Assert.Equal(140, title);
+        Assert.Equal(188, subtitle);
+        Assert.Equal(812, footer);
+        Assert.True(footer < b.Results.Bottom);                    // the footer's line sits inside the column
+        Assert.True(footer > b.ResultsTable.Bottom);               // and below the longest table
+        Assert.True(subtitle < b.ResultsTable.Y);
     }
 
     [Fact]
PATCH
```

修改 `tests/Tank.Art.Tests/HudTextTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/HudTextTests.cs b/tests/Tank.Art.Tests/HudTextTests.cs
index 5d91df2..235b44c 100644
--- a/tests/Tank.Art.Tests/HudTextTests.cs
+++ b/tests/Tank.Art.Tests/HudTextTests.cs
@@ -126,4 +126,133 @@ public class HudTextTests
         PlateValues plate = HudText.Plate(BattleMode.FreeForAll, ownSlot: MatchRules.NoSlot, ownTeam: 0, default, Roster((0, true)));
         Assert.Equal(0, plate.Left);
     }
+    private static ResultsInfo Verdict(byte winnerTeam = MatchRules.NoTeam, byte winnerSlot = MatchRules.NoSlot, int ticksLeft = 0, bool isHost = false) =>
+        new(winnerTeam, winnerSlot, ticksLeft, isHost);
+
+    private static ResultsParts TeamResults(BattleMode mode, byte ownTeam, byte winnerTeam, PlateValues plate) =>
+        HudText.Results(mode, 0, ownTeam, Verdict(winnerTeam), plate, Roster(), new string?[BattleProtocol.MaxPlayers]);
+
+    [Theory]
+    [InlineData(0, 0, "胜利", ResultTone.Win)]
+    [InlineData(0, 1, "失败", ResultTone.Lose)]
+    [InlineData(1, 1, "胜利", ResultTone.Win)]
+    [InlineData(1, 0, "失败", ResultTone.Lose)]
+    [InlineData(0, MatchRules.NoTeam, "平局", ResultTone.Neutral)]
+    [InlineData(1, MatchRules.NoTeam, "平局", ResultTone.Neutral)]
+    public void The_team_results_title_is_win_lose_or_draw_from_the_own_side(byte ownTeam, byte winnerTeam, string title, ResultTone tone)
+    {
+        ResultsParts parts = TeamResults(BattleMode.TeamBattle, ownTeam, winnerTeam, new PlateValues(PlateKind.TeamKills, 5, 0, 5, 0));
+
+        Assert.Equal(title, parts.Title);
+        Assert.Equal(tone, parts.Tone);
+    }
+
+    [Fact]
+    public void Team_battle_results_show_the_kills_own_team_first()
+    {
+        ResultsParts parts = TeamResults(BattleMode.TeamBattle, 0, 0, new PlateValues(PlateKind.TeamKills, 12, 0, 9, 0));
+
+        Assert.Equal("击毁 12 : 9", parts.Subtitle);
+    }
+
+    [Fact]
+    public void Annihilation_results_say_who_was_wiped_out()
+    {
+        var alive = new PlateValues(PlateKind.TeamAlive, 2, 5, 0, 5);
+        var wiped = new PlateValues(PlateKind.TeamAlive, 0, 5, 3, 5);
+        var both = new PlateValues(PlateKind.TeamAlive, 0, 5, 0, 5);
+        var timeUp = new PlateValues(PlateKind.TeamAlive, 2, 5, 3, 5);
+
+        Assert.Equal("敌方全军覆没", TeamResults(BattleMode.Annihilation, 0, 0, alive).Subtitle);
+        Assert.Equal("我方全军覆没", TeamResults(BattleMode.Annihilation, 0, 1, wiped).Subtitle);
+        Assert.Equal("双方同归于尽", TeamResults(BattleMode.Annihilation, 0, MatchRules.NoTeam, both).Subtitle);
+        Assert.Equal("时间到，胜负未分", TeamResults(BattleMode.Annihilation, 0, MatchRules.NoTeam, timeUp).Subtitle);
+    }
+
+    private static PlayerStats[] Ranked(byte ownRank, byte winner, bool winnerAlive, int alive = 1)
+    {
+        var players = new PlayerStats[BattleProtocol.MaxPlayers];
+        for (int i = 0; i < 6; i++) players[i] = new PlayerStats { Active = true, Team = (byte)i, Alive = i < alive, Rank = (byte)(i + 1) };
+        players[0].Rank = ownRank;
+        players[winner] = players[winner] with { Alive = winnerAlive, Rank = 1 };
+        return players;
+    }
+
+    [Fact]
+    public void Free_for_all_results_give_the_own_place_and_name_the_survivor()
+    {
+        var names = new string?[BattleProtocol.MaxPlayers];
+        names[4] = "机器人 2";
+        PlayerStats[] players = Ranked(ownRank: 3, winner: 4, winnerAlive: true, alive: 0);
+        players[4].Alive = true;
+
+        ResultsParts parts = HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 4), default, players, names);
+
+        Assert.Equal("第 3 名", parts.Title);
+        Assert.Equal("机器人 2 是最后的幸存者", parts.Subtitle);
+        Assert.Equal(ResultTone.Neutral, parts.Tone);
+    }
+
+    [Fact]
+    public void Coming_first_in_a_free_for_all_is_a_win()
+    {
+        PlayerStats[] players = Ranked(ownRank: 1, winner: 0, winnerAlive: true, alive: 1);
+        var names = new string?[BattleProtocol.MaxPlayers];
+
+        ResultsParts parts = HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 0), default, players, names);
+
+        Assert.Equal("第 1 名", parts.Title);
+        Assert.Equal(ResultTone.Win, parts.Tone);
+        Assert.Equal("玩家 1 是最后的幸存者", parts.Subtitle);   // no name in the room: the slot's number
+    }
+
+    [Fact]
+    public void A_free_for_all_nobody_survived_says_so()
+    {
+        PlayerStats[] players = Ranked(ownRank: 2, winner: 3, winnerAlive: false, alive: 0);
+        var names = new string?[BattleProtocol.MaxPlayers];
+
+        Assert.Equal("没有人活到最后", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 3), default, players, names).Subtitle);
+        Assert.Equal("没有人活到最后", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: MatchRules.NoSlot), default, players, names).Subtitle);
+    }
+
+    [Fact]
+    public void A_free_for_all_that_ran_out_of_time_names_the_health_leader_not_a_survivor()
+    {
+        PlayerStats[] players = Ranked(ownRank: 2, winner: 3, winnerAlive: true, alive: 3);
+        var names = new string?[BattleProtocol.MaxPlayers];
+        names[3] = "小雨";
+
+        Assert.Equal("时间到，小雨 血量领先", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 3), default, players, names).Subtitle);
+    }
+
+    [Fact]
+    public void A_player_without_a_rank_just_sees_the_match_is_over()
+    {
+        PlayerStats[] players = Ranked(ownRank: 0, winner: 4, winnerAlive: true);
+
+        Assert.Equal("对局结束", HudText.Results(BattleMode.FreeForAll, 0, 0, Verdict(winnerSlot: 4), default, players, new string?[BattleProtocol.MaxPlayers]).Title);
+    }
+
+    [Theory]
+    [InlineData(8 * Hz, false, "8 秒后返回房间")]
+    [InlineData(8 * Hz - 1, false, "8 秒后返回房间")]
+    [InlineData(1, false, "1 秒后返回房间")]
+    [InlineData(0, false, "0 秒后返回房间")]
+    [InlineData(5 * Hz, true, "5 秒后返回房间 · Enter 立即返回")]
+    public void The_footer_counts_down_and_tells_the_host_about_enter(int ticks, bool host, string expected) =>
+        Assert.Equal(expected, HudText.ReturnLine(ticks, host));
+
+    [Fact]
+    public void The_results_clock_counts_the_servers_results_time_from_the_frame_the_match_ended()
+    {
+        var clock = new ResultsClock();
+
+        Assert.Equal(0, clock.Observe(MatchPhase.Playing, 1.0));
+        Assert.Equal(ModeRules.ResultsTicks - 3, clock.Observe(MatchPhase.Ended, 0.1));
+        Assert.Equal(ModeRules.ResultsTicks - 15, clock.Observe(MatchPhase.Ended, 0.4));
+        Assert.Equal(0, clock.Observe(MatchPhase.Ended, 30));
+        Assert.Equal(0, clock.Observe(MatchPhase.Playing, 1.0));   // a new match starts over
+        Assert.Equal(ModeRules.ResultsTicks, clock.Observe(MatchPhase.Ended, 0.0));
+    }
 }
PATCH
```

新建 `tests/Tank.Art.Tests/ScoreboardModelTests.cs`：

```csharp
using Tank.Client.Match;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class ScoreboardModelTests
{
    private static readonly string?[] Names = { "阿成", "小林", "老周", "小雨", "机器人 1", "机器人 2", "机器人 3", "机器人 4", null, null };

    private static string? NameOf(byte slot) => Names[slot];

    private static PlayerStats[] Field(params (byte Team, int Kills, int Deaths, bool Alive)[] seats)
    {
        var players = new PlayerStats[BattleProtocol.MaxPlayers];
        for (int i = 0; i < seats.Length; i++)
            players[i] = new PlayerStats { Active = true, Team = seats[i].Team, TankId = i + 1, Kills = seats[i].Kills, Deaths = seats[i].Deaths, Alive = seats[i].Alive };
        return players;
    }

    private static IReadOnlyList<ScoreRow> Team(PlayerStats[] players, byte ownSlot = 0, byte ownTeam = 0, int respawnIn = 0) =>
        ScoreboardModel.Build(BattleMode.TeamBattle, ownSlot, ownTeam, players, NameOf, ended: false, respawnIn);

    [Fact]
    public void Team_modes_list_the_own_team_first_then_the_other()
    {
        PlayerStats[] players = Field((1, 9, 0, true), (0, 1, 0, true), (1, 2, 0, true), (0, 0, 0, true));

        var rows = Team(players, ownSlot: 1, ownTeam: 0);

        Assert.Equal(new byte[] { 1, 3, 0, 2 }, rows.Select(r => r.Slot));
        Assert.Equal(new byte[] { 0, 0, 1, 1 }, rows.Select(r => r.Team));
    }

    [Fact]
    public void A_player_on_team_one_sees_team_one_first()
    {
        PlayerStats[] players = Field((0, 5, 0, true), (1, 1, 0, true), (0, 2, 0, true), (1, 0, 0, true));

        var rows = Team(players, ownSlot: 1, ownTeam: 1);

        Assert.Equal(new byte[] { 1, 3, 0, 2 }, rows.Select(r => r.Slot));
    }

    [Fact]
    public void Sides_are_relative_to_the_player()
    {
        PlayerStats[] players = Field((0, 0, 0, true), (0, 0, 0, true), (1, 0, 0, true));

        var rows = Team(players, ownSlot: 0, ownTeam: 0);

        Assert.Equal(new[] { Side.Own, Side.Ally, Side.Enemy }, rows.Select(r => r.Side));
    }

    [Fact]
    public void In_a_free_for_all_everyone_else_is_an_enemy_even_on_a_matching_team_number()
    {
        PlayerStats[] players = Field((0, 0, 0, true), (0, 0, 0, true));

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, false, 0);

        Assert.Equal(new[] { Side.Own, Side.Enemy }, rows.Select(r => r.Side));
    }

    [Fact]
    public void Within_a_team_kills_decide_then_fewer_deaths_then_the_lower_slot()
    {
        PlayerStats[] players = Field((0, 3, 2, true), (0, 5, 4, true), (0, 3, 1, true), (0, 3, 1, true), (0, 0, 0, true));

        var rows = Team(players);

        Assert.Equal(new byte[] { 1, 2, 3, 0, 4 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void Team_ranks_count_within_the_team()
    {
        PlayerStats[] players = Field((0, 3, 0, true), (1, 2, 0, true), (0, 1, 0, true), (1, 0, 0, true));

        var rows = Team(players);

        Assert.Equal(new[] { 1, 2, 1, 2 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void A_free_for_all_puts_the_living_first_while_it_runs()
    {
        PlayerStats[] players = Field((0, 5, 0, false), (1, 1, 3, true), (2, 2, 1, true), (3, 9, 0, false));
        for (int i = 0; i < 4; i++) players[i].Team = (byte)i;

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, ended: false, 0);

        Assert.Equal(new byte[] { 2, 1, 3, 0 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.Rank));   // the standing, not yet a server rank
    }

    [Fact]
    public void After_a_free_for_all_ends_the_servers_rank_decides_and_is_shown()
    {
        PlayerStats[] players = Field((0, 9, 0, false), (1, 0, 5, false), (2, 4, 1, true), (3, 4, 1, false));
        byte[] ranks = { 3, 4, 1, 2 };
        for (int i = 0; i < 4; i++) { players[i].Team = (byte)i; players[i].Rank = ranks[i]; }

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, ended: true, 0);

        Assert.Equal(new byte[] { 2, 3, 0, 1 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3, 4 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void An_unranked_player_after_the_end_goes_last_by_slot_and_takes_the_place_it_stands_in()
    {
        PlayerStats[] players = Field((0, 0, 0, false), (1, 0, 0, false), (2, 0, 0, false));
        players[0].Team = 0; players[1].Team = 1; players[2].Team = 2;
        players[1].Rank = 1;

        var rows = ScoreboardModel.Build(BattleMode.FreeForAll, 0, 0, players, NameOf, ended: true, 0);

        Assert.Equal(new byte[] { 1, 0, 2 }, rows.Select(r => r.Slot));
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.Rank));
    }

    [Fact]
    public void Players_who_left_are_not_listed()
    {
        PlayerStats[] players = Field((0, 1, 0, true), (0, 9, 0, true), (1, 2, 0, true));
        players[1].Active = false;

        var rows = Team(players);

        Assert.Equal(new byte[] { 0, 2 }, rows.Select(r => r.Slot));
    }

    [Fact]
    public void A_missing_name_falls_back_to_the_slot_number()
    {
        PlayerStats[] players = Field((0, 0, 0, true));
        players[8] = new PlayerStats { Active = true, Team = 0, Alive = true };
        players[9] = new PlayerStats { Active = true, Team = 0, Alive = true };
        Names[9] = "   ";

        var rows = Team(players);
        Names[9] = null;

        Assert.Equal(new[] { "阿成", "玩家 9", "玩家 10" }, rows.Select(r => r.Name));
    }

    [Fact]
    public void The_tank_column_names_the_vehicle()
    {
        PlayerStats[] players = Field((0, 0, 0, true), (0, 0, 0, true));
        players[1].Spec = 2;

        var rows = Team(players);

        Assert.Equal("轻型坦克", rows[0].TankName);
        Assert.Equal("重型坦克", rows[1].TankName);
    }

    [Fact]
    public void Status_is_alive_or_destroyed_and_the_own_row_counts_down_its_respawn()
    {
        PlayerStats[] players = Field((0, 0, 0, false), (0, 0, 0, false), (1, 0, 0, true));

        var waiting = Team(players, ownSlot: 0, respawnIn: 7 * Tank.Sim.SimConstants.TickRate - 1);
        var noCountdown = Team(players, ownSlot: 0, respawnIn: 0);

        Assert.Equal("复活中 7 秒", waiting.Single(r => r.Slot == 0).Status);   // rounded up
        Assert.Equal("已毁", waiting.Single(r => r.Slot == 1).Status);          // others' respawn times are not known
        Assert.Equal("存活", waiting.Single(r => r.Slot == 2).Status);
        Assert.Equal("已毁", noCountdown.Single(r => r.Slot == 0).Status);
        Assert.All(waiting.Concat(noCountdown), r => Assert.Equal(r.Alive, r.Status == "存活"));
    }

    [Fact]
    public void A_living_own_tank_ignores_a_stale_respawn_time()
    {
        PlayerStats[] players = Field((0, 0, 0, true));

        Assert.Equal("存活", Team(players, respawnIn: 300)[0].Status);
    }

    [Fact]
    public void Building_into_a_buffer_allocates_nothing_once_warm()
    {
        PlayerStats[] players = Field((0, 3, 1, true), (1, 2, 0, true), (0, 1, 2, false), (1, 0, 3, true), (0, 4, 0, true), (1, 5, 1, false));
        var buffer = new ScoreRow[BattleProtocol.MaxPlayers];
        Func<byte, string?> nameOf = NameOf;
        for (int i = 0; i < 10; i++) ScoreboardModel.Build(BattleMode.TeamBattle, 0, 0, players, nameOf, false, 0, buffer);

        long before = GC.GetAllocatedBytesForCurrentThread();
        int count = 0;
        for (int i = 0; i < 100; i++) count = ScoreboardModel.Build(BattleMode.TeamBattle, 0, 0, players, nameOf, false, 0, buffer);
        long after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(6, count);
        Assert.Equal(before, after);
    }

    [Fact]
    public void A_buffer_too_small_for_the_slots_is_refused()
    {
        PlayerStats[] players = Field((0, 0, 0, true));

        Assert.Throws<ArgumentException>(() => ScoreboardModel.Build(BattleMode.TeamBattle, 0, 0, players, NameOf, false, 0, new ScoreRow[2]));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，`ResultTone`、`ResultsInfo`、`ResultsParts`、`ScoreRow` 等找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Match/CenterBanner.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/CenterBanner.cs b/src/Tank.Client/scripts/Match/CenterBanner.cs
index f3a6edb..6a42606 100644
--- a/src/Tank.Client/scripts/Match/CenterBanner.cs
+++ b/src/Tank.Client/scripts/Match/CenterBanner.cs
@@ -1,14 +1,16 @@
 using Godot;
 using Tank.Client.Art;
 using Tank.Client.Overlay;
+using Tank.Sim.Modes;
 
 namespace Tank.Client.Match;
 
 /// <summary>
 /// The banner in the middle of the screen (blueprint §4.2). Respawn: a plate at 0.28 of the height with "你被击毁了", the
 /// seconds left in big type and "秒后复活" under it. Spectate: a bar at the bottom centre, above the status panel's
-/// row, with "观战 name · Q/E 切换". Otherwise nothing. Redraws only when the kind, the second or the target changes.
-/// (The results banner is the scoreboard task's.)
+/// row, with "观战 name · Q/E 切换". Results: the screen dimmed, the title in big type with the line under it, and the
+/// return countdown at the foot (the table between them is the scoreboard's). Otherwise nothing. Redraws only when the
+/// kind, the second, the target or the results' words change.
 /// </summary>
 public partial class CenterBanner : HudPanel
 {
@@ -17,6 +19,11 @@ public partial class CenterBanner : HudPanel
     private const int PlateRadius = 24, BarRadius = 18;
     private const float PlateAlpha = 0.8f;
     private const string Caption = "你被击毁了", Suffix = "秒后复活";
+    private const int ResultsTitleSize = 84, ResultsSubtitleSize = 32, ResultsFooterSize = 26;
+    private const float DimAlpha = 0.55f;
+
+    /// <summary>What the results words are made from; they are rebuilt when this changes.</summary>
+    private readonly record struct ResultsKey(BattleMode Mode, byte OwnSlot, byte OwnTeam, byte WinnerTeam, byte WinnerSlot, PlateValues Plate, byte OwnRank, bool WinnerAlive, string? WinnerName);
 
     private readonly StyleBoxFlat _plate = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), PlateRadius, 8);
     private readonly StyleBoxFlat _bar = HudStyle.RoundedBox(new Color(HudStyle.Panel, PlateAlpha), BarRadius, 8);
@@ -25,9 +32,19 @@ public partial class CenterBanner : HudPanel
     private string _number = "";
     private string? _target;
     private string _barText = "";
+    private ResultsKey _resultsKey;
+    private ResultsParts _results;
+    private int _footerSeconds = -1;
+    private bool _footerHost;
+    private string _footer = "";
 
     public void Update(in MatchHudFrame frame)
     {
+        if (frame.Banner == BannerKind.Results)
+        {
+            UpdateResults(frame);
+            return;
+        }
         int seconds = frame.Banner == BannerKind.Respawn ? HudText.CeilSeconds(frame.RespawnIn) : 0;
         string? target = frame.Banner == BannerKind.Spectate ? frame.SpectateTarget : null;
         if (frame.Banner == _kind && seconds == _seconds && target == _target) return;
@@ -37,10 +54,34 @@ public partial class CenterBanner : HudPanel
         QueueRedraw();
     }
 
+    private void UpdateResults(in MatchHudFrame frame)
+    {
+        bool changed = _kind != BannerKind.Results;
+        _kind = BannerKind.Results;
+        var key = new ResultsKey(frame.Mode, frame.OwnSlot, frame.OwnTeam, frame.Results.WinnerTeam, frame.Results.WinnerSlot, frame.Plate,
+            frame.OwnSlot < frame.Players.Length ? frame.Players[frame.OwnSlot].Rank : (byte)0,
+            frame.Results.WinnerSlot < frame.Players.Length && frame.Players[frame.Results.WinnerSlot].Alive,
+            frame.Results.WinnerSlot < frame.Names.Length ? frame.Names[frame.Results.WinnerSlot] : null);
+        if (changed || key != _resultsKey)
+        {
+            _resultsKey = key;
+            _results = HudText.Results(frame.Mode, frame.OwnSlot, frame.OwnTeam, frame.Results, frame.Plate, frame.Players, frame.Names);
+            changed = true;
+        }
+        int seconds = HudText.CeilSeconds(frame.Results.TicksLeft);
+        if (changed || seconds != _footerSeconds || frame.Results.IsHost != _footerHost)
+        {
+            (_footerSeconds, _footerHost) = (seconds, frame.Results.IsHost);
+            _footer = HudText.ReturnLine(frame.Results.TicksLeft, frame.Results.IsHost);
+            QueueRedraw();
+        }
+    }
+
     public override void _Draw()
     {
         switch (_kind)
         {
+            case BannerKind.Results: DrawResults(); break;
             case BannerKind.Respawn: DrawRespawn(); break;
             case BannerKind.Spectate: DrawSpectate(); break;
         }
@@ -61,4 +102,19 @@ public partial class CenterBanner : HudPanel
         DrawStyleBox(_bar, ToRect(box));
         HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudStyle.Baseline(box.Height / 2, BarSize), _barText, BarSize, HudStyle.Text, 5);
     }
+
+    private void DrawResults()
+    {
+        DrawRect(new Rect2(Vector2.Zero, Size), new Color(HudStyle.OutlineColor, DimAlpha));
+        HudRect box = Boxes.Results;
+        Color title = _results.Tone switch
+        {
+            ResultTone.Win => Palette.UiGood.ToColor(),
+            ResultTone.Lose => Palette.UiDanger.ToColor(),
+            _ => HudStyle.Text,
+        };
+        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudLayout.ResultsTitleBaseline, _results.Title, ResultsTitleSize, title, 12);
+        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudLayout.ResultsSubtitleBaseline, _results.Subtitle, ResultsSubtitleSize, HudStyle.Text, 6);
+        HudStyle.DrawOutlinedCentered(this, box.CenterX, box.Y + HudLayout.ResultsFooterBaseline, _footer, ResultsFooterSize, Palette.UiDim.ToColor(), 5);
+    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Match/HudGallery.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/HudGallery.cs b/src/Tank.Client/scripts/Match/HudGallery.cs
index 026a528..b9ed178 100644
--- a/src/Tank.Client/scripts/Match/HudGallery.cs
+++ b/src/Tank.Client/scripts/Match/HudGallery.cs
@@ -27,6 +27,7 @@ public partial class HudGallery : Node
         _hud = new MatchHud();
         AddChild(_hud);
         foreach (FeedLine k in _demo.Kills) _hud.OnKill(k.Killer, k.KillerSide, k.Victim, k.VictimSide, k.Friendly, now: 0);
+        _hud.SetScoreboardOpen(_demo.ScoreboardOpen);
         GetWindow().Title = $"坦克 3D · HUD · {_demo.Name}";
     }
 
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs b/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs
index 7c310dd..bb8c8d0 100644
--- a/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs
+++ b/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs
@@ -4,20 +4,20 @@ using Tank.Sim.Modes;
 
 namespace Tank.Client.Match;
 
-/// <summary>One screen of the HUD gallery: the frame, the kills already on the feed, and whether the F3 panel is open.</summary>
-public sealed record HudDemo(string Name, MatchHudFrame Frame, FeedLine[] Kills, bool NetPanelOpen);
+/// <summary>One screen of the HUD gallery: the frame, the kills already on the feed, whether the F3 panel is open and whether Tab is "held".</summary>
+public sealed record HudDemo(string Name, MatchHudFrame Frame, FeedLine[] Kills, bool NetPanelOpen, bool ScoreboardOpen = false);
 
 /// <summary>
 /// Canned HUD frames for <c>--hud-demo NAME</c> (blueprint §2.1): every state the match HUD can show, from fixed data, so
 /// a screenshot is the same each time and layout problems show without playing a match. The player is slot 0, team 0.
-/// Each frame carries a fake scoreboard (players and names) for the scoreboard task to draw.
+/// Each frame carries a fake scoreboard (players and names); the scoreboard and results screens draw it.
 /// </summary>
 public static class HudDemoFrames
 {
     private const string LongName = "Sergeant_Wolfgang_Becker";   // as long as a name gets on the wire: 24 ASCII bytes
     private static readonly string[] Roster = { "阿成", "小林", "老周", "小雨", "机器人 1", "机器人 2", "机器人 3", "机器人 4", "机器人 5", "机器人 6" };
 
-    private static readonly string[] AllNames = { "timer", "feed", "respawn", "spectate", "annihilation", "ffa", "net" };
+    private static readonly string[] AllNames = { "timer", "feed", "respawn", "spectate", "annihilation", "ffa", "net", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa" };
 
     /// <summary>The gallery's screen names, in a fixed order.</summary>
     public static IReadOnlyList<string> Names => AllNames;
@@ -33,6 +33,11 @@ public static class HudDemoFrames
             "annihilation" => Annihilation(),
             "ffa" => Solo(),
             "net" => Net(),
+            "scoreboard" => Scoreboard(),
+            "results-win" => ResultsWin(),
+            "results-lose" => ResultsLose(),
+            "results-draw" => ResultsDraw(),
+            "results-ffa" => ResultsSolo(),
             _ => null!,
         };
         return demo is not null;
@@ -83,6 +88,41 @@ public static class HudDemoFrames
         return new HudDemo("net", frame, Array.Empty<FeedLine>(), NetPanelOpen: true);
     }
 
+    /// <summary>Team battle, eight players on mixed slots: the player (slot 0) leads a team that has one tank down; one of the others is down too.</summary>
+    private static HudDemo Scoreboard()
+    {
+        PlayerStats[] players = Players(8, i => (byte)(i is 0 or 2 or 3 or 6 ? 0 : 1), i => i is not (3 or 5), kills: i => new[] { 4, 1, 2, 0, 3, 1, 2, 5 }[i], deaths: i => new[] { 1, 2, 2, 3, 1, 4, 2, 0 }[i]);
+        var match = new MatchBlock { TeamScore0 = 8, TeamScore1 = 9 };
+        MatchHudFrame frame = Frame(BattleMode.TeamBattle, Ticks(3, 27), players, 8) with { Plate = HudText.Plate(BattleMode.TeamBattle, 0, 0, match, players) };
+        return Make("scoreboard", frame) with { ScoreboardOpen = true };
+    }
+
+    private static HudDemo ResultsWin() =>
+        Make("results-win", Ended(BattleMode.TeamBattle, Players(6, i => (byte)(i < 3 ? 0 : 1), _ => true, kills: i => 5 - i, deaths: i => i % 3), 6, 12, 9, new ResultsInfo(0, MatchRules.NoSlot, 5 * SimConstants.TickRate, true)));
+
+    private static HudDemo ResultsLose() =>
+        Make("results-lose", Ended(BattleMode.Annihilation, Players(8, i => (byte)(i < 4 ? 0 : 1), i => i is 6 or 7, kills: i => i % 3, deaths: i => i < 4 ? 1 : i % 2), 8, 0, 0, new ResultsInfo(1, MatchRules.NoSlot, 3 * SimConstants.TickRate, false)));
+
+    private static HudDemo ResultsDraw() =>
+        Make("results-draw", Ended(BattleMode.Annihilation, Players(6, i => (byte)(i < 3 ? 0 : 1), i => i is 1 or 4, kills: i => i % 2, deaths: i => i % 2), 6, 0, 0, new ResultsInfo(MatchRules.NoTeam, MatchRules.NoSlot, 7 * SimConstants.TickRate, false)));
+
+    /// <summary>Free-for-all, six players ranked 1 to 6; the player (slot 0) came third and slot 4 was left standing.</summary>
+    private static HudDemo ResultsSolo()
+    {
+        byte[] ranks = { 3, 2, 5, 6, 1, 4 };
+        PlayerStats[] players = Players(6, i => (byte)i, i => i == 4, kills: i => new[] { 2, 3, 1, 0, 4, 2 }[i], deaths: i => i == 4 ? 0 : 1);
+        for (int i = 0; i < ranks.Length; i++) players[i].Rank = ranks[i];
+        return Make("results-ffa", Ended(BattleMode.FreeForAll, players, 6, 0, 0, new ResultsInfo(MatchRules.NoTeam, 4, 4 * SimConstants.TickRate, false)));
+    }
+
+    /// <summary>The match over: the plate and the results banner for <paramref name="info"/>, the clock at 0.</summary>
+    private static MatchHudFrame Ended(BattleMode mode, PlayerStats[] players, int count, int score0, int score1, ResultsInfo info)
+    {
+        var match = new MatchBlock { Phase = MatchPhase.Ended, TeamScore0 = score0, TeamScore1 = score1 };
+        MatchHudFrame frame = Frame(mode, ticksLeft: 0, players, count);
+        return frame with { Phase = MatchPhase.Ended, Plate = HudText.Plate(mode, 0, 0, match, players), Banner = BannerKind.Results, Results = info };
+    }
+
     private static HudDemo Make(string name, MatchHudFrame frame) => new(name, frame, Array.Empty<FeedLine>(), NetPanelOpen: false);
 
     private static MatchHudFrame TeamFrame(int ticksLeft, int kills0, int kills1)
@@ -100,14 +140,14 @@ public static class HudDemoFrames
         var names = new string?[BattleProtocol.MaxPlayers];
         Array.Copy(Roster, names, count);
         var net = new NetSample(default, 0, false, 0, 0, 60);
-        return new MatchHudFrame(mode, MatchPhase.Playing, ticksLeft, 0, 0, plate, BannerKind.None, 0, null, net, players, names);
+        return new MatchHudFrame(mode, MatchPhase.Playing, ticksLeft, 0, 0, plate, BannerKind.None, 0, null, net, players, names, ResultsInfo.None);
     }
 
     private static PlayerStats[] Players(int count, Func<int, byte> team, Func<int, bool> alive, Func<int, int> kills, Func<int, int> deaths)
     {
         var players = new PlayerStats[BattleProtocol.MaxPlayers];
         for (int i = 0; i < count; i++)
-            players[i] = new PlayerStats { Active = true, Team = team(i), TankId = i + 1, Kills = kills(i), Deaths = deaths(i), Alive = alive(i) };
+            players[i] = new PlayerStats { Active = true, Team = team(i), TankId = i + 1, Spec = (byte)(i % 4), Kills = kills(i), Deaths = deaths(i), Alive = alive(i) };
         return players;
     }
 
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/HudLayout.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/HudLayout.cs b/src/Tank.Client/scripts/Match/Logic/HudLayout.cs
index d25adf7..13cf4c5 100644
--- a/src/Tank.Client/scripts/Match/Logic/HudLayout.cs
+++ b/src/Tank.Client/scripts/Match/Logic/HudLayout.cs
@@ -20,7 +20,7 @@ public readonly record struct HudRect(float X, float Y, float Width, float Heigh
 /// <summary>Where every HUD block goes on one screen size.</summary>
 public readonly record struct HudBoxes(
     HudRect ScorePlate, HudRect ModeLabel, HudRect KillFeed, HudRect NetPanel, HudRect Banner, HudRect SpectateBar,
-    HudRect Scoreboard, HudRect StatusPanel)
+    HudRect Scoreboard, HudRect StatusPanel, HudRect Results, HudRect ResultsTable)
 {
     /// <summary>Row <paramref name="index"/> (0 = top) of the kill feed.</summary>
     public HudRect KillRow(int index) => new(KillFeed.X, KillFeed.Y + index * (HudLayout.KillRowHeight + HudLayout.KillRowGap), KillFeed.Width, HudLayout.KillRowHeight);
@@ -49,7 +49,13 @@ public static class HudLayout
 
     public const float SpectateWidth = 680f, SpectateHeight = 56f, SpectateFromBottom = 36f;
 
-    public const float ScoreboardWidth = 900f, ScoreboardTop = 110f, ScoreboardHeight = 576f;
+    // The board hangs below the mode label (which ends at 116), clear of it.
+    public const float ScoreboardWidth = 900f, ScoreboardTop = 120f, ScoreboardHeight = 576f;
+
+    // The results screen (§4.2): a 960-wide column from y 60; title and subtitle baselines, the table (the scoreboard's, at its own
+    // top) and the footer's baseline are measured from the column's top.
+    public const float ResultsWidth = 960f, ResultsTop = 60f, ResultsHeight = 780f;
+    public const float ResultsTitleBaseline = 80f, ResultsSubtitleBaseline = 128f, ResultsTableTop = 164f, ResultsFooterBaseline = 752f;
 
     /// <summary>The status panel's room, its vehicle name above and the lamp included (see <c>StatusPanel</c>: 24 from the edges, 178 tall, about 360 wide).</summary>
     public const float StatusWidth = 360f, StatusHeight = 178f, StatusNameRoom = 36f;
@@ -59,6 +65,7 @@ public static class HudLayout
         float cx = size.X / 2;
         var plate = new HudRect(cx - PlateWidth / 2, Top, PlateWidth, PlateHeight);
         var label = new HudRect(plate.X, plate.Bottom + ModeLabelGap, PlateWidth, ModeLabelHeight);
+        var results = new HudRect(cx - ResultsWidth / 2, ResultsTop, ResultsWidth, ResultsHeight);
         float feedHeight = KillRows * KillRowHeight + (KillRows - 1) * KillRowGap;
         return new HudBoxes(
             plate,
@@ -68,6 +75,8 @@ public static class HudLayout
             new HudRect(cx - BannerWidth / 2, size.Y * BannerTopShare, BannerWidth, BannerHeight),
             new HudRect(cx - SpectateWidth / 2, size.Y - SpectateFromBottom - SpectateHeight, SpectateWidth, SpectateHeight),
             new HudRect(cx - ScoreboardWidth / 2, ScoreboardTop, ScoreboardWidth, ScoreboardHeight),
-            new HudRect(Margin, size.Y - Margin - StatusHeight - StatusNameRoom, StatusWidth, StatusHeight + StatusNameRoom));
+            new HudRect(Margin, size.Y - Margin - StatusHeight - StatusNameRoom, StatusWidth, StatusHeight + StatusNameRoom),
+            results,
+            new HudRect(cx - ScoreboardWidth / 2, results.Y + ResultsTableTop, ScoreboardWidth, ScoreboardHeight));
     }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/HudText.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/HudText.cs b/src/Tank.Client/scripts/Match/Logic/HudText.cs
index 01257c9..80dae9c 100644
--- a/src/Tank.Client/scripts/Match/Logic/HudText.cs
+++ b/src/Tank.Client/scripts/Match/Logic/HudText.cs
@@ -17,6 +17,12 @@ public readonly record struct PlateValues(PlateKind Kind, int Left, int LeftOf,
 /// <summary>The words on the two side blocks: a small caption over a big value.</summary>
 public readonly record struct PlateParts(string LeftCaption, string LeftValue, string RightCaption, string RightValue);
 
+/// <summary>How the match went for the player, for the colour of the results title.</summary>
+public enum ResultTone : byte { Neutral, Win, Lose }
+
+/// <summary>The results screen's title and the line under it.</summary>
+public readonly record struct ResultsParts(string Title, string Subtitle, ResultTone Tone);
+
 /// <summary>
 /// The HUD's words and sums, none of them touching the engine: the match clock (rounded up, so the last second reads 0:01
 /// and 0:00 means over), the respawn line, and the score plate per mode. Strings are built here, so a view calls them
@@ -106,7 +112,48 @@ public static class HudText
         _ => new PlateParts(SoloKills, Number(plate.Left), SoloAlive, Fraction(plate.Right, plate.RightOf)),
     };
 
-    // T7 hooks in here: the results titles ("胜利", "失败", "平局", the free-for-all's winner) belong with the plate's words.
+    /// <summary>
+    /// The results screen's words (allocates: call it when its inputs change). Team modes: 胜利 / 失败 / 平局 from the
+    /// winning team against <paramref name="ownTeam"/>, with the kills or the wipe-out under it. Free-for-all: the player's
+    /// place ("第 N 名", from the server's rank) and who was left. <paramref name="plate"/> and <paramref name="players"/>
+    /// are the frame's; <paramref name="names"/> is by slot.
+    /// </summary>
+    public static ResultsParts Results(BattleMode mode, byte ownSlot, byte ownTeam, in ResultsInfo info, in PlateValues plate, ReadOnlySpan<PlayerStats> players, string?[] names)
+    {
+        if (!ModeRules.HasTeams(mode)) return SoloResults(ownSlot, info, players, names);
+        ResultTone tone = info.WinnerTeam == MatchRules.NoTeam ? ResultTone.Neutral : info.WinnerTeam == ownTeam ? ResultTone.Win : ResultTone.Lose;
+        string title = tone switch { ResultTone.Win => "胜利", ResultTone.Lose => "失败", _ => "平局" };
+        string subtitle = mode == BattleMode.TeamBattle
+            ? string.Create(CultureInfo.InvariantCulture, $"击毁 {plate.Left} : {plate.Right}")
+            : AnnihilationLine(tone, plate);
+        return new ResultsParts(title, subtitle, tone);
+    }
+
+    private static string AnnihilationLine(ResultTone tone, in PlateValues plate) => tone switch
+    {
+        ResultTone.Win => "敌方全军覆没",
+        ResultTone.Lose => "我方全军覆没",
+        _ => plate.Left == 0 && plate.Right == 0 ? "双方同归于尽" : "时间到，胜负未分",
+    };
+
+    private static ResultsParts SoloResults(byte ownSlot, in ResultsInfo info, ReadOnlySpan<PlayerStats> players, string?[] names)
+    {
+        int rank = ownSlot < players.Length ? players[ownSlot].Rank : 0;
+        string title = rank > 0 ? string.Create(CultureInfo.InvariantCulture, $"第 {rank} 名") : "对局结束";
+        ResultTone tone = rank == 1 ? ResultTone.Win : ResultTone.Neutral;
+        int winner = info.WinnerSlot;
+        if (winner >= players.Length || !players[winner].Alive) return new ResultsParts(title, "没有人活到最后", tone);
+        CountAll(players, out int alive, out _);
+        string name = ScoreboardModel.PlayerName(winner < names.Length ? names[winner] : null, winner);
+        return new ResultsParts(title, alive > 1 ? $"时间到，{name} 血量领先" : $"{name} 是最后的幸存者", tone);
+    }
+
+    /// <summary>"N 秒后返回房间", with the host's shortcut when <paramref name="isHost"/>; the seconds round up.</summary>
+    public static string ReturnLine(int ticksLeft, bool isHost)
+    {
+        string line = string.Create(CultureInfo.InvariantCulture, $"{CeilSeconds(ticksLeft)} 秒后返回房间");
+        return isHost ? line + " · Enter 立即返回" : line;
+    }
 
     private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
 
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs b/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs
index 41e96ee..38797da 100644
--- a/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs
+++ b/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs
@@ -5,7 +5,17 @@ using Tank.Sim.Modes;
 namespace Tank.Client.Match;
 
 /// <summary>What the centre banner shows.</summary>
-public enum BannerKind : byte { None, Respawn, Spectate }
+public enum BannerKind : byte { None, Respawn, Spectate, Results }
+
+/// <summary>
+/// What the results screen needs besides the scoreboard: the server's verdict, the ticks left before the server sends
+/// everyone back to the room (counted by the scene from the moment it saw the match end) and whether the player may send them early.
+/// </summary>
+public readonly record struct ResultsInfo(byte WinnerTeam, byte WinnerSlot, int TicksLeft, bool IsHost)
+{
+    /// <summary>No verdict: the match is on.</summary>
+    public static readonly ResultsInfo None = new(MatchRules.NoTeam, MatchRules.NoSlot, 0, false);
+}
 
 /// <summary>
 /// Everything the match HUD needs for one frame (blueprint §2.1): a plain value the scene fills from the client and the
@@ -24,7 +34,8 @@ public readonly record struct MatchHudFrame(
     string? SpectateTarget,
     NetSample Net,
     PlayerStats[] Players,
-    string?[] Names)
+    string?[] Names,
+    ResultsInfo Results)
 {
     /// <summary>The slot that has <paramref name="tankId"/> on the scoreboard, or -1 for a tank nobody plays (or has left).</summary>
     public static int SlotOf(PlayerStats[] players, int tankId)
@@ -38,9 +49,9 @@ public readonly record struct MatchHudFrame(
     /// <summary>
     /// The frame from the client's newest snapshot. The respawn banner shows while the own tank is a wreck that is coming
     /// back (team battle only); <paramref name="spectateTarget"/> turns the bottom bar on. <paramref name="names"/> is the
-    /// room's names by slot, <paramref name="fps"/> the engine's frame rate. Reads only; allocates nothing.
+    /// room's names by slot, <paramref name="fps"/> the engine's frame rate, <paramref name="resultsLeft"/> the ticks left on the results screen. Reads only; allocates nothing.
     /// </summary>
-    public static MatchHudFrame From(BattleClient client, BattleSession? session, byte ownTeam, string?[] names, int fps, string? spectateTarget = null)
+    public static MatchHudFrame From(BattleClient client, BattleSession? session, byte ownTeam, string?[] names, int fps, string? spectateTarget = null, int resultsLeft = 0)
     {
         BattleMode mode = client.MatchInfo?.Mode ?? client.Room?.Mode ?? BattleMode.TeamBattle;
         MatchBlock match = client.Match;
@@ -48,11 +59,13 @@ public readonly record struct MatchHudFrame(
         byte slot = client.Slot;
         var plate = HudText.Plate(mode, slot, ownTeam, match, players);
         BannerKind banner = BannerKind.None;
-        if (spectateTarget is not null) banner = BannerKind.Spectate;
+        if (match.Phase == MatchPhase.Ended) banner = BannerKind.Results;
+        else if (spectateTarget is not null) banner = BannerKind.Spectate;
         else if (match.RespawnIn > 0 && ModeRules.Respawns(mode) && match.Phase == MatchPhase.Playing) banner = BannerKind.Respawn;
         (double avg, double max) = session?.ServerTick ?? default;
         var net = new NetSample(client.Stats, client.Resyncs, session?.ServerTick is not null, avg, max, fps);
-        return new MatchHudFrame(mode, match.Phase, match.TicksLeft, slot, ownTeam, plate, banner, match.RespawnIn, spectateTarget, net, players, names);
+        return new MatchHudFrame(mode, match.Phase, match.TicksLeft, slot, ownTeam, plate, banner, match.RespawnIn, spectateTarget, net, players, names,
+            new ResultsInfo(match.WinnerTeam, match.WinnerSlot, resultsLeft, client.IsHost));
     }
 
     private static readonly PlayerStats[] NoPlayers = new PlayerStats[BattleProtocol.MaxPlayers];
PATCH
```

新建 `src/Tank.Client/scripts/Match/Logic/ResultsClock.cs`：

```csharp
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The results screen's countdown. The server counts <see cref="ModeRules.ResultsTicks"/> from the tick the match ends
/// and then sends everyone back to the room, but the snapshot's <c>TicksLeft</c> is the match clock (it stands still, or
/// is 0), so the client counts from the frame it first saw the match ended. That is a little late by the trip time, so the
/// screen may read 1 for a moment before the room comes back.
/// </summary>
public sealed class ResultsClock
{
    private double _elapsed;

    /// <summary>Call once a frame with the match's phase and the frame's seconds; returns the ticks left (0 while the match is on).</summary>
    public int Observe(MatchPhase phase, double dt)
    {
        if (phase != MatchPhase.Ended)
        {
            _elapsed = 0;
            return 0;
        }
        _elapsed += dt;
        int spent = (int)(_elapsed * SimConstants.TickRate);
        return Math.Max(0, ModeRules.ResultsTicks - spent);
    }
}
```

新建 `src/Tank.Client/scripts/Match/Logic/ScoreboardModel.cs`：

```csharp
using System.Globalization;
using Tank.Client.UI;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// One line of the scoreboard. <c>Rank</c> is 1-based: the place in the player's own team in the team modes, the place
/// in the field in a free-for-all (the server's own after the match, the standing while it runs). <c>Side</c> is the
/// line's colour relative to the player; <c>Status</c> is the words of the last column.
/// </summary>
public readonly record struct ScoreRow(byte Slot, string Name, byte Team, string TankName, int Kills, int Deaths, bool Alive, int Rank, Side Side, string Status);

/// <summary>
/// The scoreboard's lines and their order (blueprint §5 T7), none of it touching the engine. Team modes: the player's
/// team first, each team by kills (most first), then deaths (fewest first), then slot. Free-for-all: while the match
/// runs the living first, then kills and deaths as above; once it has ended the server's <c>Rank</c>. Players who have
/// left are not listed.
/// </summary>
public static class ScoreboardModel
{
    public const string AliveText = "存活", DestroyedText = "已毁";

    private static readonly string[] SlotNames = BuildSlotNames();

    /// <summary>The name shown for <paramref name="slot"/>: <paramref name="name"/>, or "玩家 N" (1-based) when the room has none.</summary>
    public static string PlayerName(string? name, int slot)
    {
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return (uint)slot < (uint)SlotNames.Length ? SlotNames[slot] : string.Create(CultureInfo.InvariantCulture, $"玩家 {slot + 1}");
    }

    /// <summary>"复活中 N 秒": the own row's status while the tank is a wreck that is coming back, rounded up so it never reads 0.</summary>
    public static string RespawnStatus(int respawnIn) => string.Create(CultureInfo.InvariantCulture, $"复活中 {HudText.CeilSeconds(respawnIn)} 秒");

    /// <summary>
    /// Fills <paramref name="output"/> with the lines in display order and returns how many. Allocates nothing unless the
    /// own row's respawn status needs a new string; <paramref name="output"/> needs room for every slot of
    /// <paramref name="players"/>. <paramref name="ownRespawnIn"/> is the ticks until the own tank is back (0 when it isn't waiting).
    /// </summary>
    public static int Build(BattleMode mode, byte ownSlot, byte ownTeam, ReadOnlySpan<PlayerStats> players, Func<byte, string?> nameOf,
        bool ended, int ownRespawnIn, Span<ScoreRow> output)
    {
        if (output.Length < players.Length) throw new ArgumentException("The output cannot hold every slot.", nameof(output));
        bool teams = ModeRules.HasTeams(mode);
        bool byRank = !teams && ended;
        int count = 0;
        for (int slot = 0; slot < players.Length; slot++)
        {
            PlayerStats p = players[slot];
            if (!p.Active) continue;
            bool own = slot == ownSlot;
            string status = p.Alive ? AliveText : own && ownRespawnIn > 0 ? RespawnStatus(ownRespawnIn) : DestroyedText;
            output[count++] = new ScoreRow((byte)slot, PlayerName(nameOf((byte)slot), slot), p.Team, ModeText.TankName(p.Spec),
                p.Kills, p.Deaths, p.Alive, p.Rank, MatchColors.SideOf(mode, ownTeam, p.Team, own), status);
        }
        Span<ScoreRow> rows = output[..count];
        for (int i = 1; i < count; i++)
        {
            ScoreRow key = rows[i];
            int j = i - 1;
            while (j >= 0 && Before(key, rows[j], teams, byRank, ownTeam)) { rows[j + 1] = rows[j]; j--; }
            rows[j + 1] = key;
        }
        Number(rows, teams, byRank);
        return count;
    }

    /// <summary>The same as a list, for callers that don't mind the allocation (tests).</summary>
    public static IReadOnlyList<ScoreRow> Build(BattleMode mode, byte ownSlot, byte ownTeam, ReadOnlySpan<PlayerStats> players, Func<byte, string?> nameOf, bool ended, int ownRespawnIn)
    {
        var buffer = new ScoreRow[players.Length];
        int count = Build(mode, ownSlot, ownTeam, players, nameOf, ended, ownRespawnIn, buffer);
        return buffer[..count];
    }

    private static bool Before(in ScoreRow a, in ScoreRow b, bool teams, bool byRank, byte ownTeam)
    {
        if (teams && a.Team != b.Team) return a.Team == ownTeam;
        if (byRank && a.Rank != b.Rank) return RankKey(a) < RankKey(b);
        if (!teams && !byRank && a.Alive != b.Alive) return a.Alive;
        if (!byRank && a.Kills != b.Kills) return a.Kills > b.Kills;
        if (!byRank && a.Deaths != b.Deaths) return a.Deaths < b.Deaths;
        return a.Slot < b.Slot;
    }

    /// <summary>A server rank of 0 (never ranked) sorts last.</summary>
    private static int RankKey(in ScoreRow row) => row.Rank == 0 ? int.MaxValue : row.Rank;

    /// <summary>Sets <c>Rank</c> to the place within the team, or within the field; the server's rank stands after a free-for-all.</summary>
    private static void Number(Span<ScoreRow> rows, bool teams, bool byRank)
    {
        int place = 0;
        for (int i = 0; i < rows.Length; i++)
        {
            place = teams && i > 0 && rows[i].Team == rows[i - 1].Team ? place + 1 : teams ? 1 : i + 1;
            if (byRank && rows[i].Rank > 0) continue;
            rows[i] = rows[i] with { Rank = place };
        }
    }

    private static string[] BuildSlotNames()
    {
        var names = new string[BattleProtocol.MaxPlayers];
        for (int i = 0; i < names.Length; i++) names[i] = string.Create(CultureInfo.InvariantCulture, $"玩家 {i + 1}");
        return names;
    }
}
```

修改 `src/Tank.Client/scripts/Match/MatchHud.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchHud.cs b/src/Tank.Client/scripts/Match/MatchHud.cs
index 6f372c6..7e9928f 100644
--- a/src/Tank.Client/scripts/Match/MatchHud.cs
+++ b/src/Tank.Client/scripts/Match/MatchHud.cs
@@ -1,4 +1,5 @@
 using Godot;
+using Tank.Sim.Modes;
 
 namespace Tank.Client.Match;
 
@@ -6,8 +7,8 @@ namespace Tank.Client.Match;
 /// The match HUD's root (blueprint §1.1): a canvas layer above the combat HUD that owns the score plate, the kill feed,
 /// the centre banner and the F3 panel, and hands each the part of a <see cref="MatchHudFrame"/> it draws. The scene calls
 /// <see cref="Refresh"/> once a frame and <see cref="OnKill"/> for every kill event; the gallery does the same with canned
-/// data. While the scoreboard is up (<see cref="SetScoreboardOpen"/>, the scoreboard task's) the kill feed and the F3
-/// panel step aside: the scoreboard is wider than the room between them.
+/// data. While the scoreboard is up (Tab held, <see cref="SetScoreboardOpen"/>, or the results screen once the match has
+/// ended) the kill feed and the F3 panel step aside: the scoreboard is wider than the room between them.
 /// </summary>
 public partial class MatchHud : CanvasLayer
 {
@@ -17,8 +18,11 @@ public partial class MatchHud : CanvasLayer
     private ScorePlate _plate = null!;
     private KillFeedView _feedView = null!;
     private CenterBanner _banner = null!;
+    private ScoreboardView _board = null!;
     private NetPanel _net = null!;
     private bool _scoreboardOpen;
+    private bool _boardShown;   // the board is up: Tab is held, or the results screen is
+    private bool _resultsUp;    // the match has ended: the results screen covers the plate
     private bool _netOpen;
 
     public MatchHud() => Layer = HudLayer;
@@ -28,16 +32,12 @@ public partial class MatchHud : CanvasLayer
         _plate = Add(new ScorePlate());
         _feedView = Add(new KillFeedView(_feed));
         _banner = Add(new CenterBanner());
+        _board = Add(new ScoreboardView());   // after the banner: the results' dimming lies under the table
         _net = Add(new NetPanel { Visible = false });
     }
 
-    /// <summary>The scoreboard shows or hides: the kill feed and the F3 panel hide while it shows.</summary>
-    public void SetScoreboardOpen(bool open)
-    {
-        if (open == _scoreboardOpen) return;
-        _scoreboardOpen = open;
-        ApplyVisibility();
-    }
+    /// <summary>Tab is held or let go: the scoreboard shows or hides (the results screen shows it regardless).</summary>
+    public void SetScoreboardOpen(bool open) => _scoreboardOpen = open;
 
     /// <summary>A kill for the feed; <paramref name="killer"/> is null when the shooter is hidden from the player.</summary>
     public void OnKill(string? killer, Side killerSide, string victim, Side victimSide, bool friendly, double now) =>
@@ -49,6 +49,14 @@ public partial class MatchHud : CanvasLayer
         _plate.Update(frame);
         _feedView.Update(now);
         _banner.Update(frame);
+        _board.Update(frame, _scoreboardOpen);
+        bool resultsUp = frame.Phase == MatchPhase.Ended;
+        bool boardShown = _scoreboardOpen || resultsUp;
+        if (boardShown != _boardShown || resultsUp != _resultsUp)
+        {
+            (_boardShown, _resultsUp) = (boardShown, resultsUp);
+            ApplyVisibility();
+        }
         if (netPanelOpen != _netOpen)
         {
             _netOpen = netPanelOpen;
@@ -60,8 +68,9 @@ public partial class MatchHud : CanvasLayer
 
     private void ApplyVisibility()
     {
-        _feedView.Visible = !_scoreboardOpen;
-        _net.Visible = _netOpen && !_scoreboardOpen;
+        _plate.Visible = !_resultsUp;   // its clock would run into the title
+        _feedView.Visible = !_boardShown;
+        _net.Visible = _netOpen && !_boardShown;
     }
 
     private T Add<T>(T node) where T : Node
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchInput.cs b/src/Tank.Client/scripts/Match/MatchInput.cs
index f442467..4df1f60 100644
--- a/src/Tank.Client/scripts/Match/MatchInput.cs
+++ b/src/Tank.Client/scripts/Match/MatchInput.cs
@@ -5,16 +5,18 @@ namespace Tank.Client.Match;
 
 /// <summary>
 /// A match's input: everything in <see cref="DriveInput"/> plus Tab held for the scoreboard, F3 to toggle the network
-/// panel (both already in project.godot) and the spectating keys Q (previous), E (next) and F (free camera), each
+/// panel (both already in project.godot), Enter for the host to leave the results early and the spectating keys Q (previous), E (next) and F (free camera), each
 /// consumed once by the frame that reads it (the spectating itself is wired in a later task).
 /// </summary>
 public sealed class MatchInput : DriveInput
 {
     private static readonly StringName Scoreboard = "scoreboard", ToggleNetPanel = "toggle_netgraph";
+    private static readonly StringName ReturnToRoomAction = "return_to_room";
     private static readonly StringName SpectatePrev = "spectate_prev", SpectateNext = "spectate_next", SpectateFree = "spectate_free";
 
     private int _spectateStep;
     private bool _spectateFree;
+    private bool _returnToRoom;
 
     public static new void RegisterActions()
     {
@@ -22,6 +24,7 @@ public sealed class MatchInput : DriveInput
         AddKey(SpectatePrev, Key.Q);
         AddKey(SpectateNext, Key.E);
         AddKey(SpectateFree, Key.F);
+        AddKey(ReturnToRoomAction, Key.Enter);
     }
 
     /// <summary>Whether Tab is down: the scoreboard shows while it is.</summary>
@@ -38,6 +41,15 @@ public sealed class MatchInput : DriveInput
         else if (e.IsActionPressed(SpectatePrev)) _spectateStep = -1;
         else if (e.IsActionPressed(SpectateNext)) _spectateStep = 1;
         else if (e.IsActionPressed(SpectateFree)) _spectateFree = true;
+        else if (e.IsActionPressed(ReturnToRoomAction)) _returnToRoom = true;
+    }
+
+    /// <summary>True once after Enter was pressed (the host's way out of the results screen).</summary>
+    public bool TakeReturnToRoom()
+    {
+        bool pressed = _returnToRoom;
+        _returnToRoom = false;
+        return pressed;
     }
 
     /// <summary>Once after Q or E: -1 for the previous spectating target, 1 for the next, 0 for neither.</summary>
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.Camera.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.Camera.cs b/src/Tank.Client/scripts/Match/MatchMain.Camera.cs
index 59c98cc..5d2104f 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.Camera.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.Camera.cs
@@ -21,7 +21,8 @@ public partial class MatchMain
     {
         if (_automation.IgnoresInput) return;
         // Releases always pass so a held fire can end; presses only count while captured (the capturing click must not shoot).
-        if (e.IsActionReleased(DriveInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured) _input.OnInputEvent(e);
+        // On the results screen the mouse is free, but the host's Enter still has to arrive.
+        if (e.IsActionReleased(DriveInput.FireGun) || Input.MouseMode == Input.MouseModeEnum.Captured || (_resultsShown && e is InputEventKey)) _input.OnInputEvent(e);
     }
 
     public override void _Notification(int what)
@@ -43,7 +44,7 @@ public partial class MatchMain
             case InputEventMouseButton { Pressed: true } wheel when captured && _camera is not null && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                 _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                 break;
-            case InputEventMouseButton { Pressed: true } when !captured:
+            case InputEventMouseButton { Pressed: true } when !captured && !_resultsShown:
                 Input.MouseMode = Input.MouseModeEnum.Captured;
                 GetViewport().SetInputAsHandled();
                 break;
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.cs b/src/Tank.Client/scripts/Match/MatchMain.cs
index eca7f52..e36c85a 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.cs
@@ -35,6 +35,7 @@ public partial class MatchMain : Node3D
     private readonly MatchInput _input = new();
     private readonly ShotDetector _shots = new();
     private readonly WreckTracker _ownWreck = new();
+    private readonly ResultsClock _resultsClock = new();
     private readonly string?[] _names = new string?[BattleProtocol.MaxPlayers];
     private readonly HeadLookup _headOf;   // cached: a method group would make a new delegate every call
 
@@ -60,6 +61,7 @@ public partial class MatchMain : Node3D
     private float _aimYaw;
     private float _aimPitch;
     private bool _finished;
+    private bool _resultsShown;   // the match has ended and the results screen is up
 
     /// <summary>The client left the match (it ended, or the connection did); raised once.</summary>
     public event Action? Finished;
@@ -103,6 +105,12 @@ public partial class MatchMain : Node3D
             Finished?.Invoke();
             return;
         }
+        bool ended = _client.Match.Phase == MatchPhase.Ended;
+        if (ended != _resultsShown) ShowResults(ended);
+        // Taken every frame: an Enter pressed during play (the captured mouse forwards every key) must not wait for the
+        // results and send the host back the frame they appear.
+        bool returnToRoom = _input.TakeReturnToRoom();
+        if (ended && returnToRoom && _client.IsHost) _client.ReturnToRoom();
         if (_client.Field is not { } field) return;   // InMatch always has a field; a defensive frame if not
         // The scenery is built as soon as the field is known: one long frame, best spent before the first snapshot sets the clocks.
         if (_world is null) BuildScene(field);
@@ -135,22 +143,33 @@ public partial class MatchMain : Node3D
         _world!.Update(hasTank ? own : null, hasTank ? pose : null, cameraPos, fdt);   // only the own tank flattens the grass (R12)
         _actors.Sync(_client.Others, Mode, OwnTeam(own), fdt);
         _shells.Sync(_client.Shells, (float)_client.Clock.Alpha, fdt);
-        _combat.ShowOwn(hasTank);
+        _combat.ShowOwn(hasTank && !ended);
         _combat.Refresh(own, spotted: hasTank && _client.Spotted && !own.IsDestroyed);
         _actors.FillBars(_combat.TankBars, BattleProtocol.MaxPlayers, _names);
-        _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond()), _input.NetPanelOpen, _time);   // Tab (the scoreboard) is wired by its own task
+        int resultsLeft = _resultsClock.Observe(_client.Match.Phase, dt);
+        _hud.SetScoreboardOpen(_input.ScoreboardHeld);
+        _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond(), resultsLeft: resultsLeft), _input.NetPanelOpen, _time);
         _automation.EndFrame(delta);
     }
 
     /// <summary>The player's sticks, trigger and aim for this frame; only the aim while the tank is a wreck (nothing to drive).</summary>
     private LocalCommand Command()
     {
-        if (!_client.HasTank) return LocalCommand.None;
+        if (!_client.HasTank || _resultsShown) return LocalCommand.None;
         if (_client.Own.IsDestroyed) return new LocalCommand(0, 0, _aimYaw, _aimPitch, false, _input.Ammo);
         var (throttle, steer, fire) = _input.Peek();
         return _automation.Command(new LocalCommand(throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo), _time);
     }
 
+    /// <summary>The results screen comes up (or goes, were a match to restart on the same scene): the mouse is let go for it and held keys drop.</summary>
+    private void ShowResults(bool shown)
+    {
+        _resultsShown = shown;
+        if (!shown) return;
+        Input.MouseMode = Input.MouseModeEnum.Visible;
+        _input.ReleaseAll();
+    }
+
     private BattleMode Mode => _client.MatchInfo?.Mode ?? _client.Room?.Mode ?? BattleMode.TeamBattle;
 
     /// <summary>The player's team from the scoreboard (known even while the own tank isn't), else the own tank's.</summary>
PATCH
```

新建 `src/Tank.Client/scripts/Match/ScoreboardView.cs`：

```csharp
using System.Globalization;
using Godot;
using Tank.Client.Art;
using Tank.Client.Overlay;
using Tank.Net.Battle;
using Tank.Sim.Modes;

namespace Tank.Client.Match;

/// <summary>
/// The scoreboard (blueprint §4.2): a header (名次 / 名字 / 车型 / 击毁 / 阵亡 / 状态), in the team modes a banner per team
/// (我方 first, then 敌方, in the sides' colours) and one line per player, the player's own tinted in the accent colour
/// and the fallen dimmed. It shows while Tab is held and, in the same shape at the results position, once the match has
/// ended. The lines are rebuilt only when the scoreboard's numbers, the names or the own respawn second change, and the
/// view redraws only then (or when shown or resized): nothing here allocates on a quiet frame.
/// </summary>
public partial class ScoreboardView : HudPanel
{
    private const float HeaderHeight = 56f, TeamBannerHeight = 38f, RowHeight = 42f, PadBottom = 14f;
    private const int HeaderSize = 20, BodySize = 24, StatusSize = 22, TeamSize = 22, TextOutline = 4;
    private const float RankX = 64f, NameX = 112f, TankX = 350f, KillX = 600f, DeathX = 685f, StatusX = 745f;
    private const float NameWidth = TankX - NameX - 12f;
    private const int PanelRadius = 24, RowRadius = 12;
    private const float PanelAlpha = 0.88f, TeamAlpha = 0.3f, OwnAlpha = 0.3f, DeadAlpha = 0.5f, DividerInset = 18f;

    private static readonly string[] Headers = { "名次", "名字", "车型", "击毁", "阵亡", "状态" };
    private static readonly Func<string, float> MeasureBody = text => HudStyle.TextWidth(text, BodySize);   // cached: one delegate, not one per name

    private struct Line
    {
        public string Rank, Name, Tank, Kills, Deaths, Status;
        public Color NameColor;
        public bool Own, Dim, TeamStart, FirstGroup;
    }

    private readonly ScoreRow[] _rows = new ScoreRow[BattleProtocol.MaxPlayers];
    private readonly Line[] _lines = new Line[BattleProtocol.MaxPlayers];
    private readonly PlayerStats[] _seenPlayers = new PlayerStats[BattleProtocol.MaxPlayers];
    private readonly string?[] _seenNames = new string?[BattleProtocol.MaxPlayers];
    private readonly Func<byte, string?> _nameOf;
    private readonly StyleBoxFlat _panel = HudStyle.RoundedBox(new Color(HudStyle.Panel, PanelAlpha), PanelRadius, 8);
    private readonly StyleBoxFlat _ownRow = HudStyle.RoundedBox(new Color(HudStyle.Accent, OwnAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _allyBanner = HudStyle.RoundedBox(new Color(MatchColors.Ui(Side.Ally).ToColor(), TeamAlpha), RowRadius, 6);
    private readonly StyleBoxFlat _enemyBanner = HudStyle.RoundedBox(new Color(MatchColors.Ui(Side.Enemy).ToColor(), TeamAlpha), RowRadius, 6);
    private string?[] _names = Array.Empty<string?>();
    private int _count, _groups;
    private bool _hasLines, _teams, _results;
    private BattleMode _mode;
    private byte _ownSlot, _ownTeam;
    private bool _ended;
    private int _respawnSeconds;

    public ScoreboardView()
    {
        _nameOf = slot => slot < _names.Length ? _names[slot] : null;
        Visible = false;
    }

    /// <summary>Call once a frame. <paramref name="tabHeld"/> shows the board; so does the end of the match (then at the results position).</summary>
    public void Update(in MatchHudFrame frame, bool tabHeld)
    {
        bool ended = frame.Phase == MatchPhase.Ended;
        if (!tabHeld && !ended)
        {
            Visible = false;   // the lines are left as they were: the next show compares them with the frame's and rebuilds if they differ
            return;
        }
        bool wasVisible = Visible;
        Visible = true;
        int respawnSeconds = HudText.CeilSeconds(frame.RespawnIn);
        bool changed = !wasVisible || ended != _results;
        if (!_hasLines || Differs(frame, ended, respawnSeconds))
        {
            Rebuild(frame, ended, respawnSeconds);
            changed = true;
        }
        _results = ended;
        if (changed) QueueRedraw();
    }

    private bool Differs(in MatchHudFrame frame, bool ended, int respawnSeconds)
    {
        if (frame.Mode != _mode || frame.OwnSlot != _ownSlot || frame.OwnTeam != _ownTeam || ended != _ended || respawnSeconds != _respawnSeconds) return true;
        for (int i = 0; i < _seenPlayers.Length; i++)
            if (!_seenPlayers[i].Equals(frame.Players[i]) || !ReferenceEquals(_seenNames[i], frame.Names[i])) return true;
        return false;
    }

    private void Rebuild(in MatchHudFrame frame, bool ended, int respawnSeconds)
    {
        (_mode, _ownSlot, _ownTeam, _ended, _respawnSeconds, _hasLines) = (frame.Mode, frame.OwnSlot, frame.OwnTeam, ended, respawnSeconds, true);
        Array.Copy(frame.Players, _seenPlayers, _seenPlayers.Length);
        Array.Copy(frame.Names, _seenNames, _seenNames.Length);
        _names = frame.Names;
        _teams = ModeRules.HasTeams(frame.Mode);
        _count = ScoreboardModel.Build(frame.Mode, frame.OwnSlot, frame.OwnTeam, frame.Players, _nameOf, ended, frame.RespawnIn, _rows);
        _groups = 0;
        for (int i = 0; i < _count; i++)
        {
            ScoreRow row = _rows[i];
            bool start = _teams && (i == 0 || row.Team != _rows[i - 1].Team);
            if (start) _groups++;
            _lines[i] = new Line
            {
                Rank = Number(row.Rank), Name = Fit(row.Name), Tank = row.TankName, Kills = Number(row.Kills), Deaths = Number(row.Deaths),
                Status = row.Status, NameColor = NameColor(row), Own = row.Slot == frame.OwnSlot, Dim = !row.Alive,
                TeamStart = start, FirstGroup = _groups == 1,
            };
        }
    }

    private Color NameColor(in ScoreRow row) => _teams ? MatchColors.Ui(row.Side).ToColor() : row.Side == Side.Own ? HudStyle.Accent : HudStyle.Text;

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>The name cut to the name column, with an ellipsis where it was cut.</summary>
    private static string Fit(string name) => HudText.Fit(name, NameWidth, MeasureBody);

    public override void _Draw()
    {
        if (!_hasLines) return;
        HudRect table = _results ? Boxes.ResultsTable : Boxes.Scoreboard;
        float height = HeaderHeight + _groups * TeamBannerHeight + _count * RowHeight + PadBottom;
        DrawStyleBox(_panel, new Rect2(table.X, table.Y, table.Width, height));
        DrawHeader(table);
        float y = table.Y + HeaderHeight;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Line line = ref _lines[i];
            if (line.TeamStart)
            {
                DrawTeamBanner(table, y, line.FirstGroup);
                y += TeamBannerHeight;
            }
            DrawPlayerLine(table, y, line);
            y += RowHeight;
        }
    }

    private void DrawHeader(HudRect table)
    {
        float baseline = table.Y + HudStyle.Baseline(HeaderHeight / 2, HeaderSize);
        Color dim = Palette.UiDim.ToColor();
        HudStyle.DrawOutlinedCentered(this, table.X + RankX, baseline, Headers[0], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + NameX, baseline), Headers[1], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + TankX, baseline), Headers[2], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlinedCentered(this, table.X + KillX, baseline, Headers[3], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlinedCentered(this, table.X + DeathX, baseline, Headers[4], HeaderSize, dim, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + StatusX, baseline), Headers[5], HeaderSize, dim, TextOutline);
        float rule = table.Y + HeaderHeight - 2;
        DrawLine(new Vector2(table.X + DividerInset, rule), new Vector2(table.Right - DividerInset, rule), new Color(HudStyle.OutlineColor, 0.8f), 2f);
    }

    private void DrawTeamBanner(HudRect table, float y, bool own)
    {
        var box = new Rect2(table.X + DividerInset / 2, y + 3, table.Width - DividerInset, TeamBannerHeight - 6);
        DrawStyleBox(own ? _allyBanner : _enemyBanner, box);
        Color color = MatchColors.Ui(own ? Side.Ally : Side.Enemy).ToColor();
        HudStyle.DrawOutlined(this, new Vector2(box.Position.X + 16, y + HudStyle.Baseline(TeamBannerHeight / 2, TeamSize)), own ? "我方" : "敌方", TeamSize, color, TextOutline);
    }

    private void DrawPlayerLine(HudRect table, float y, in Line line)
    {
        if (line.Own) DrawStyleBox(_ownRow, new Rect2(table.X + DividerInset / 2, y + 2, table.Width - DividerInset, RowHeight - 4));
        float baseline = y + HudStyle.Baseline(RowHeight / 2, BodySize);
        float statusBaseline = y + HudStyle.Baseline(RowHeight / 2, StatusSize);
        float alpha = line.Dim ? DeadAlpha : 1f;
        Color text = new(HudStyle.Text, alpha), name = new(line.NameColor, alpha), dim = new(Palette.UiDim.ToColor(), alpha);
        HudStyle.DrawOutlinedCentered(this, table.X + RankX, baseline, line.Rank, BodySize, text, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + NameX, baseline), line.Name, BodySize, name, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + TankX, baseline), line.Tank, BodySize, dim, TextOutline);
        HudStyle.DrawOutlinedCentered(this, table.X + KillX, baseline, line.Kills, BodySize, text, TextOutline);
        HudStyle.DrawOutlinedCentered(this, table.X + DeathX, baseline, line.Deaths, BodySize, text, TextOutline);
        HudStyle.DrawOutlined(this, new Vector2(table.X + StatusX, statusBaseline), line.Status, StatusSize, StatusColor(line, alpha), TextOutline);
    }

    private static Color StatusColor(in Line line, float alpha) =>
        line.Dim ? new Color(Palette.UiDanger.ToColor(), alpha) : new Color(Palette.UiGood.ToColor(), alpha);
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 811、Net 210。

- [ ] **Step 5：截图验证**

```bash
mkdir -p build/shots/t7
for d in scoreboard results-win results-lose results-draw results-ffa; do
  $S/godot_run.sh $W "" 20 -- --no-update --hud-demo $d --screenshot $W/build/shots/t7/$d.png --screenshot-at 2 --quit-after 4
done
$S/godot_run.sh $W "" 60 -- --no-update --host --mode annihilation --bots 1 --match-seconds 20 --autostart --screenshot $W/build/shots/t7/real-results.png --screenshot-at 28 --quit-after 45
```

Expected：
- `scoreboard`：Tab 计分板，我方在上（蓝灰）、敌方在下（红），击毁 / 阵亡 / 状态列；不盖住顶部模式标签。
- `results-win/lose/draw`：结算标题（胜利 / 失败 / 平局）、计分表、“N 秒后返回房间 · Enter 立即返回”。
- `results-ffa`：“第 N 名”和幸存者行，表按服务器名次排。
- `real-results.png`：20 秒对局结束后的真结算页“平局 · 时间到，胜负未分”；日志里 8 秒后回到房间，`--autostart` 又开了第二局。

- [ ] **Step 6：提交**

```bash
git add -A src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): the Tab scoreboard (teams relative to the player, kills/deaths/status, FFA ranks from the server after the end) and the results screen (title, survivor line, table, return countdown, host's Enter to return early)"
```

---

### Task 8：阵亡、观战与暂停层

蓝图 §5 T8。`Logic/SpectateState`（纯逻辑：击毁特写 3 秒；无复活模式到时自动跟随一个活着的目标；Q / E / 左键 `Step(±1)` 回绕，残骸态和自由态下 +1 取首、−1 取尾；目标死了换候选第一个；没有候选时团队模式回残骸、各自为战转自由视角，团队的残骸态接住迟到的候选；`MoveFocus` 相对相机偏航移动焦点，偏航 0 朝 −Z，60 m/s，Shift ×3，斜向归一化，夹在可玩区内）。`MatchMain` 处理死 / 活的边沿（死：`OnDeath`、关狙击镜、清掉残留的观战键；活：镜头回自己、`ResetAmmo`）；死亡、结算、暂停期间不开不动不开火，但瞄准角照发（发 `LocalCommand.None` 会把瞄准清成偏航 0，服务器把暂停玩家的炮塔转向正北）；自己复活以状态为准当场恢复模型（不等复活事件）；`ShowOwn` 藏准星和状态面板；压草换成被跟随的车（自由视角时不压）。观战键 Q / E / 左键，F 仅各自为战；死亡期间 Shift 挪用为加速；暂停、结算、阵亡时 Shift 不切狙击镜。`PauseOverlay`（CanvasLayer 3，全屏压暗 + 440×280 面板：继续 / 离开房间 / 房主多一个“结束对局回到房间”走 `ReturnToRoom`；结算出现时自动关，不吞房主的 Enter）；“离开房间”经 `MatchMain.LeaveRequested` 走 `Main.LeaveSession`。横幅优先级：结算 > 复活 > 观战。画廊加 `spectate-team|spectate-ffa`。

**Files:**
- Create: `src/Tank.Client/scripts/Match/PauseOverlay.cs`、`Match/Logic/SpectateState.cs`
- Modify: `Bootstrap/Main.Flow.cs`、`Match/{CenterBanner,MatchInput,MatchMain,MatchMain.Camera}.cs`、`Match/Logic/{HudDemoFrames,HudText,MatchHudFrame}.cs`
- Test: 新建 `tests/Tank.Art.Tests/SpectateStateTests.cs`；修改 `HudDemoFramesTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/HudDemoFramesTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/HudDemoFramesTests.cs b/tests/Tank.Art.Tests/HudDemoFramesTests.cs
index d5b5a85..ba449d8 100644
--- a/tests/Tank.Art.Tests/HudDemoFramesTests.cs
+++ b/tests/Tank.Art.Tests/HudDemoFramesTests.cs
@@ -19,7 +19,7 @@ public class HudDemoFramesTests
     [Fact]
     public void The_gallery_has_the_screens_the_blueprint_names()
     {
-        foreach (string name in new[] { "timer", "feed", "respawn", "spectate", "annihilation", "ffa", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa" })
+        foreach (string name in new[] { "timer", "feed", "respawn", "spectate", "spectate-team", "spectate-ffa", "annihilation", "ffa", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa" })
             Assert.Contains(name, HudDemoFrames.Names);
     }
 
@@ -99,6 +99,24 @@ public class HudDemoFramesTests
         HudDemoFrames.TryGet("spectate", out HudDemo spectate);
         Assert.Equal("观战 机器人 2 · Q/E 切换", HudText.SpectateLine(spectate.Frame.SpectateTarget!));
 
+        // spectate-team: the wreck waits for the respawn while the camera follows a teammate — the countdown and the bar together.
+        HudDemoFrames.TryGet("spectate-team", out HudDemo spectateTeam);
+        Assert.Equal(BattleMode.TeamBattle, spectateTeam.Frame.Mode);
+        Assert.Equal(BannerKind.Respawn, spectateTeam.Frame.Banner);
+        Assert.True(spectateTeam.Frame.RespawnIn > 0);
+        Assert.False(spectateTeam.Frame.Players[0].Alive);
+        int targetSlot = Array.IndexOf(spectateTeam.Frame.Names, spectateTeam.Frame.SpectateTarget);
+        Assert.True(targetSlot >= 0 && spectateTeam.Frame.Players[targetSlot] is { Team: 0, Alive: true }, "the watched tank is a living teammate");
+
+        // spectate-ffa: dead for good, the camera free.
+        HudDemoFrames.TryGet("spectate-ffa", out HudDemo spectateFfa);
+        Assert.Equal(BattleMode.FreeForAll, spectateFfa.Frame.Mode);
+        Assert.Equal(BannerKind.Spectate, spectateFfa.Frame.Banner);
+        Assert.True(spectateFfa.Frame.SpectateFree);
+        Assert.Null(spectateFfa.Frame.SpectateTarget);
+        Assert.False(spectateFfa.Frame.Players[0].Alive);
+        Assert.Equal(0, spectateFfa.Frame.RespawnIn);
+
         HudDemoFrames.TryGet("annihilation", out HudDemo annihilation);
         Assert.Equal(("3/5", "2/5"), (HudText.Parts(annihilation.Frame.Plate).LeftValue, HudText.Parts(annihilation.Frame.Plate).RightValue));
 
PATCH
```

新建 `tests/Tank.Art.Tests/SpectateStateTests.cs`：

```csharp
using System.Numerics;
using Tank.Client.Match;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;

namespace Tank.Art.Tests;

public class SpectateStateTests
{
    private const float Dt = 1f / 60;
    private static readonly int[] Three = { 4, 7, 9 };

    private static SpectateState Dead(bool autoFollow)
    {
        var s = new SpectateState();
        s.OnDeath(autoFollow);
        return s;
    }

    // 200 m square, flat at y = 0, 4 m border: playable X and Z are 4 … 196.
    private static Battlefield Flat()
    {
        const int samples = 101;
        return new Battlefield(new HeightField(samples, samples, 2f, new float[samples * samples]), Array.Empty<Prop>(), 4f);
    }

    [Fact]
    public void Death_starts_on_the_wreck_with_no_target()
    {
        SpectateState s = Dead(autoFollow: true);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        Assert.Equal(0, s.TargetId);
    }

    [Fact]
    public void The_close_up_holds_three_seconds_then_follows_the_first_candidate()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Update(SpectateState.CloseUpSeconds - 0.1f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        s.Update(0.2f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Follow, s.Current);
        Assert.Equal(4, s.TargetId);
    }

    [Fact]
    public void A_respawning_mode_waits_on_the_wreck_until_the_player_steps_away()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Update(10f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        s.Step(1, Three);
        Assert.Equal((SpectateState.Mode.Follow, 4), (s.Current, s.TargetId));
    }

    [Fact]
    public void Stepping_wraps_at_both_ends()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, Three);
        s.Step(1, Three);
        s.Step(1, Three);
        Assert.Equal(9, s.TargetId);
        s.Step(1, Three);
        Assert.Equal(4, s.TargetId);   // past the end, round to the front
        s.Step(-1, Three);
        Assert.Equal(9, s.TargetId);   // and back again
    }

    [Fact]
    public void Stepping_back_from_the_wreck_picks_the_last_candidate()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(-1, Three);
        Assert.Equal((SpectateState.Mode.Follow, 9), (s.Current, s.TargetId));
    }

    [Fact]
    public void A_step_with_nobody_to_watch_changes_nothing()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, ReadOnlySpan<int>.Empty);
        Assert.Equal((SpectateState.Mode.Wreck, 0), (s.Current, s.TargetId));
    }

    [Fact]
    public void A_dead_target_is_swapped_for_the_next()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, Three);
        s.Step(1, Three);
        Assert.Equal(7, s.TargetId);
        s.Update(Dt, stackalloc int[] { 4, 9 }, freeWhenNone: false);   // 7 died
        Assert.Equal((SpectateState.Mode.Follow, 4), (s.Current, s.TargetId));
    }

    [Fact]
    public void Nobody_left_returns_a_team_mode_to_the_wreck()
    {
        SpectateState s = Dead(autoFollow: false);
        s.Step(1, Three);
        s.Update(Dt, ReadOnlySpan<int>.Empty, freeWhenNone: false);
        Assert.Equal((SpectateState.Mode.Wreck, 0), (s.Current, s.TargetId));
    }

    [Fact]
    public void Nobody_left_frees_the_camera_in_a_free_for_all()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Step(1, Three);
        s.Update(Dt, ReadOnlySpan<int>.Empty, freeWhenNone: true);
        Assert.Equal((SpectateState.Mode.Free, 0), (s.Current, s.TargetId));
    }

    [Fact]
    public void The_close_up_with_nobody_to_watch_waits_in_a_team_mode_and_takes_a_late_arrival()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Update(SpectateState.CloseUpSeconds + 0.5f, ReadOnlySpan<int>.Empty, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);
        s.Update(Dt, stackalloc int[] { 7 }, freeWhenNone: false);   // a teammate came back in sight
        Assert.Equal((SpectateState.Mode.Follow, 7), (s.Current, s.TargetId));
    }

    [Fact]
    public void The_close_up_with_nobody_to_watch_frees_the_camera_in_a_free_for_all()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Update(SpectateState.CloseUpSeconds + 0.5f, ReadOnlySpan<int>.Empty, freeWhenNone: true);
        Assert.Equal(SpectateState.Mode.Free, s.Current);
    }

    [Fact]
    public void Stepping_from_the_free_camera_follows_again()
    {
        SpectateState s = Dead(autoFollow: true);
        s.GoFree();
        Assert.Equal(SpectateState.Mode.Free, s.Current);
        s.Step(1, Three);
        Assert.Equal((SpectateState.Mode.Follow, 4), (s.Current, s.TargetId));
    }

    [Fact]
    public void A_fresh_death_starts_the_close_up_over()
    {
        SpectateState s = Dead(autoFollow: true);
        s.Step(1, Three);
        s.OnDeath(autoFollow: true);
        Assert.Equal((SpectateState.Mode.Wreck, 0), (s.Current, s.TargetId));
        s.Update(1f, Three, freeWhenNone: false);
        Assert.Equal(SpectateState.Mode.Wreck, s.Current);   // the three seconds count anew
    }

    [Fact]
    public void The_focus_moves_forward_toward_minus_z_at_yaw_zero()
    {
        Vector2 moved = SpectateState.MoveFocus(new Vector2(100, 100), cameraYaw: 0f, forward: 1, right: 0, fast: false, dt: 0.5f, Flat());
        Assert.Equal(100f, moved.X, 3);
        Assert.Equal(100f - SpectateState.FocusSpeed * 0.5f, moved.Y, 3);
    }

    [Fact]
    public void The_focus_moves_right_toward_plus_x_at_yaw_zero()
    {
        Vector2 moved = SpectateState.MoveFocus(new Vector2(100, 100), cameraYaw: 0f, forward: 0, right: 1, fast: false, dt: 0.5f, Flat());
        Assert.Equal(100f + SpectateState.FocusSpeed * 0.5f, moved.X, 3);
        Assert.Equal(100f, moved.Y, 3);
    }

    [Fact]
    public void A_quarter_turn_of_the_camera_turns_forward_toward_minus_x()
    {
        Vector2 moved = SpectateState.MoveFocus(new Vector2(100, 100), cameraYaw: MathF.PI / 2, forward: 1, right: 0, fast: false, dt: 0.5f, Flat());
        Assert.Equal(100f - SpectateState.FocusSpeed * 0.5f, moved.X, 3);
        Assert.Equal(100f, moved.Y, 3);
    }

    [Fact]
    public void Shift_triples_the_speed_and_a_diagonal_is_no_faster()
    {
        Vector2 fast = SpectateState.MoveFocus(new Vector2(100, 100), 0f, forward: 1, right: 0, fast: true, dt: 0.5f, Flat());
        Assert.Equal(100f - SpectateState.FocusSpeed * SpectateState.FastMultiplier * 0.5f, fast.Y, 3);

        Vector2 diagonal = SpectateState.MoveFocus(new Vector2(100, 100), 0f, forward: 1, right: 1, fast: false, dt: 0.5f, Flat());
        Assert.Equal(SpectateState.FocusSpeed * 0.5f, Vector2.Distance(new Vector2(100, 100), diagonal), 3);
    }

    [Fact]
    public void The_focus_stops_at_the_playable_border()
    {
        Battlefield field = Flat();
        Vector2 forward = SpectateState.MoveFocus(new Vector2(10, 10), cameraYaw: 0f, forward: 1, right: 0, fast: true, dt: 10f, field);
        Assert.Equal(new Vector2(10, field.Border), forward);

        Vector2 right = SpectateState.MoveFocus(new Vector2(190, 50), cameraYaw: 0f, forward: 0, right: 1, fast: true, dt: 10f, field);
        Assert.Equal(new Vector2(field.Terrain.SizeX - field.Border, 50), right);
    }

    [Fact]
    public void Standing_still_only_clamps()
    {
        Vector2 still = SpectateState.MoveFocus(new Vector2(1, 100), 0.7f, forward: 0, right: 0, fast: false, dt: Dt, Flat());
        Assert.Equal(new Vector2(4, 100), still);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，`SpectateState` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
index b3fa6ef..9aabb94 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
@@ -107,6 +107,7 @@ public partial class Main
         var match = new MatchMain();
         match.Setup(_session!, _options);
         match.Finished += LeaveMatch;
+        match.LeaveRequested += () => LeaveSession("已离开房间。");   // 离开房间 on the pause layer: the whole session goes
         _matchView = match;
         AddChild(match);
         MoveChild(match, 0);   // the 3D world behind the UI screens
PATCH
```

修改 `src/Tank.Client/scripts/Match/CenterBanner.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/CenterBanner.cs b/src/Tank.Client/scripts/Match/CenterBanner.cs
index 6a42606..3eb1b48 100644
--- a/src/Tank.Client/scripts/Match/CenterBanner.cs
+++ b/src/Tank.Client/scripts/Match/CenterBanner.cs
@@ -7,10 +7,11 @@ namespace Tank.Client.Match;
 
 /// <summary>
 /// The banner in the middle of the screen (blueprint §4.2). Respawn: a plate at 0.28 of the height with "你被击毁了", the
-/// seconds left in big type and "秒后复活" under it. Spectate: a bar at the bottom centre, above the status panel's
-/// row, with "观战 name · Q/E 切换". Results: the screen dimmed, the title in big type with the line under it, and the
+/// seconds left in big type and "秒后复活" under it — and the spectate bar besides, when the waiting player watches a
+/// teammate. Spectate: a bar at the bottom centre, above the status panel's row, with "观战 name · Q/E 切换" (or the
+/// free camera's hints). Results: the screen dimmed, the title in big type with the line under it, and the
 /// return countdown at the foot (the table between them is the scoreboard's). Otherwise nothing. Redraws only when the
-/// kind, the second, the target or the results' words change.
+/// kind, the second, the target, the free camera or the results' words change.
 /// </summary>
 public partial class CenterBanner : HudPanel
 {
@@ -31,6 +32,7 @@ public partial class CenterBanner : HudPanel
     private int _seconds = -1;
     private string _number = "";
     private string? _target;
+    private bool _free;
     private string _barText = "";
     private ResultsKey _resultsKey;
     private ResultsParts _results;
@@ -46,11 +48,13 @@ public partial class CenterBanner : HudPanel
             return;
         }
         int seconds = frame.Banner == BannerKind.Respawn ? HudText.CeilSeconds(frame.RespawnIn) : 0;
-        string? target = frame.Banner == BannerKind.Spectate ? frame.SpectateTarget : null;
-        if (frame.Banner == _kind && seconds == _seconds && target == _target) return;
+        string? target = frame.Banner == BannerKind.None ? null : frame.SpectateTarget;
+        bool free = frame.Banner != BannerKind.None && target is null && frame.SpectateFree;
+        if (frame.Banner == _kind && seconds == _seconds && target == _target && free == _free) return;
         if (seconds != _seconds) _number = HudText.RespawnNumber(frame.RespawnIn);
-        if (target != _target && target is not null) _barText = HudText.SpectateLine(target);
-        (_kind, _seconds, _target) = (frame.Banner, seconds, target);
+        if (free) _barText = HudText.SpectateFreeLine;
+        else if (target != _target && target is not null) _barText = HudText.SpectateLine(target);
+        (_kind, _seconds, _target, _free) = (frame.Banner, seconds, target, free);
         QueueRedraw();
     }
 
@@ -82,7 +86,10 @@ public partial class CenterBanner : HudPanel
         switch (_kind)
         {
             case BannerKind.Results: DrawResults(); break;
-            case BannerKind.Respawn: DrawRespawn(); break;
+            case BannerKind.Respawn:
+                DrawRespawn();
+                if (_target is not null || _free) DrawSpectate();   // waiting to respawn with the camera on a teammate
+                break;
             case BannerKind.Spectate: DrawSpectate(); break;
         }
     }
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs b/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs
index bb8c8d0..74e1528 100644
--- a/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs
+++ b/src/Tank.Client/scripts/Match/Logic/HudDemoFrames.cs
@@ -17,7 +17,7 @@ public static class HudDemoFrames
     private const string LongName = "Sergeant_Wolfgang_Becker";   // as long as a name gets on the wire: 24 ASCII bytes
     private static readonly string[] Roster = { "阿成", "小林", "老周", "小雨", "机器人 1", "机器人 2", "机器人 3", "机器人 4", "机器人 5", "机器人 6" };
 
-    private static readonly string[] AllNames = { "timer", "feed", "respawn", "spectate", "annihilation", "ffa", "net", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa" };
+    private static readonly string[] AllNames = { "timer", "feed", "respawn", "spectate", "spectate-team", "spectate-ffa", "annihilation", "ffa", "net", "scoreboard", "results-win", "results-lose", "results-draw", "results-ffa" };
 
     /// <summary>The gallery's screen names, in a fixed order.</summary>
     public static IReadOnlyList<string> Names => AllNames;
@@ -30,6 +30,8 @@ public static class HudDemoFrames
             "feed" => Feed(),
             "respawn" => Respawn(),
             "spectate" => Spectate(),
+            "spectate-team" => SpectateTeam(),
+            "spectate-ffa" => SpectateFfa(),
             "annihilation" => Annihilation(),
             "ffa" => Solo(),
             "net" => Net(),
@@ -69,6 +71,33 @@ public static class HudDemoFrames
         return Make("spectate", frame);
     }
 
+    /// <summary>Team battle, the player's wreck waiting to respawn while the camera follows a teammate (blueprint §5 T8): the countdown plate and the spectate bar together, the fatal kill on the feed.</summary>
+    private static HudDemo SpectateTeam()
+    {
+        PlayerStats[] players = Players(6, i => (byte)(i < 3 ? 0 : 1), i => i != 0, kills: i => i + 1, deaths: i => i == 0 ? 2 : i % 3);
+        var match = new MatchBlock { TeamScore0 = 6, TeamScore1 = 7, TicksLeft = Ticks(4, 41) };
+        MatchHudFrame frame = Frame(BattleMode.TeamBattle, Ticks(4, 41), players, 6) with
+        {
+            Plate = HudText.Plate(BattleMode.TeamBattle, 0, 0, match, players),
+            Banner = BannerKind.Respawn,
+            RespawnIn = 7 * SimConstants.TickRate,
+            SpectateTarget = Roster[1],
+        };
+        return new HudDemo("spectate-team", frame, new[] { Line("老周", Side.Enemy, "阿成", Side.Own) }, NetPanelOpen: false);
+    }
+
+    /// <summary>Free-for-all, the player dead for good and the camera free (blueprint §5 T8): the bar carries the free camera's hints.</summary>
+    private static HudDemo SpectateFfa()
+    {
+        PlayerStats[] players = Players(8, i => (byte)i, i => i is 1 or 3 or 4 or 6, kills: i => i % 4, deaths: i => i is 1 or 3 or 4 or 6 ? 0 : 1);
+        MatchHudFrame frame = Frame(BattleMode.FreeForAll, Ticks(2, 52), players, 8) with
+        {
+            Banner = BannerKind.Spectate,
+            SpectateFree = true,
+        };
+        return new HudDemo("spectate-ffa", frame, new[] { Line("小雨", Side.Enemy, "阿成", Side.Own) }, NetPanelOpen: false);
+    }
+
     private static HudDemo Annihilation()
     {
         PlayerStats[] players = Players(10, i => (byte)(i < 5 ? 0 : 1), i => i is 0 or 1 or 3 or 7 or 8, kills: i => i % 3, deaths: i => i % 2);
@@ -140,7 +169,7 @@ public static class HudDemoFrames
         var names = new string?[BattleProtocol.MaxPlayers];
         Array.Copy(Roster, names, count);
         var net = new NetSample(default, 0, false, 0, 0, 60);
-        return new MatchHudFrame(mode, MatchPhase.Playing, ticksLeft, 0, 0, plate, BannerKind.None, 0, null, net, players, names, ResultsInfo.None);
+        return new MatchHudFrame(mode, MatchPhase.Playing, ticksLeft, 0, 0, plate, BannerKind.None, 0, null, false, net, players, names, ResultsInfo.None);
     }
 
     private static PlayerStats[] Players(int count, Func<int, byte> team, Func<int, bool> alive, Func<int, int> kills, Func<int, int> deaths)
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/HudText.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/HudText.cs b/src/Tank.Client/scripts/Match/Logic/HudText.cs
index 80dae9c..fdf8510 100644
--- a/src/Tank.Client/scripts/Match/Logic/HudText.cs
+++ b/src/Tank.Client/scripts/Match/Logic/HudText.cs
@@ -81,6 +81,9 @@ public static class HudText
     /// <summary>"观战 name · Q/E 切换", the spectating bar's text.</summary>
     public static string SpectateLine(string target) => $"观战 {target} · Q/E 切换";
 
+    /// <summary>The spectating bar while the camera is free (a free-for-all with nobody left to follow, or F).</summary>
+    public const string SpectateFreeLine = "自由视角 · WASD 移动 · Q/E 跟随";
+
     /// <summary>
     /// What the plate shows for <paramref name="mode"/>. Team battle: the team kills from <paramref name="match"/>, own
     /// team first. Annihilation: tanks alive of those each side started with. Free-for-all: the player's own kills and
PATCH
```

修改 `src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs b/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs
index 38797da..b45ec1a 100644
--- a/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs
+++ b/src/Tank.Client/scripts/Match/Logic/MatchHudFrame.cs
@@ -32,6 +32,7 @@ public readonly record struct MatchHudFrame(
     BannerKind Banner,
     int RespawnIn,
     string? SpectateTarget,
+    bool SpectateFree,
     NetSample Net,
     PlayerStats[] Players,
     string?[] Names,
@@ -47,11 +48,12 @@ public readonly record struct MatchHudFrame(
     }
 
     /// <summary>
-    /// The frame from the client's newest snapshot. The respawn banner shows while the own tank is a wreck that is coming
-    /// back (team battle only); <paramref name="spectateTarget"/> turns the bottom bar on. <paramref name="names"/> is the
+    /// The frame from the client's newest snapshot. The results banner outranks everything; the respawn banner shows while
+    /// the own tank is a wreck that is coming back (team battle only), and then <paramref name="spectateTarget"/> or
+    /// <paramref name="spectateFree"/> still turn the bottom bar on under it. <paramref name="names"/> is the
     /// room's names by slot, <paramref name="fps"/> the engine's frame rate, <paramref name="resultsLeft"/> the ticks left on the results screen. Reads only; allocates nothing.
     /// </summary>
-    public static MatchHudFrame From(BattleClient client, BattleSession? session, byte ownTeam, string?[] names, int fps, string? spectateTarget = null, int resultsLeft = 0)
+    public static MatchHudFrame From(BattleClient client, BattleSession? session, byte ownTeam, string?[] names, int fps, string? spectateTarget = null, bool spectateFree = false, int resultsLeft = 0)
     {
         BattleMode mode = client.MatchInfo?.Mode ?? client.Room?.Mode ?? BattleMode.TeamBattle;
         MatchBlock match = client.Match;
@@ -60,11 +62,11 @@ public readonly record struct MatchHudFrame(
         var plate = HudText.Plate(mode, slot, ownTeam, match, players);
         BannerKind banner = BannerKind.None;
         if (match.Phase == MatchPhase.Ended) banner = BannerKind.Results;
-        else if (spectateTarget is not null) banner = BannerKind.Spectate;
         else if (match.RespawnIn > 0 && ModeRules.Respawns(mode) && match.Phase == MatchPhase.Playing) banner = BannerKind.Respawn;
+        else if (spectateTarget is not null || spectateFree) banner = BannerKind.Spectate;
         (double avg, double max) = session?.ServerTick ?? default;
         var net = new NetSample(client.Stats, client.Resyncs, session?.ServerTick is not null, avg, max, fps);
-        return new MatchHudFrame(mode, match.Phase, match.TicksLeft, slot, ownTeam, plate, banner, match.RespawnIn, spectateTarget, net, players, names,
+        return new MatchHudFrame(mode, match.Phase, match.TicksLeft, slot, ownTeam, plate, banner, match.RespawnIn, spectateTarget, spectateFree, net, players, names,
             new ResultsInfo(match.WinnerTeam, match.WinnerSlot, resultsLeft, client.IsHost));
     }
 
PATCH
```

新建 `src/Tank.Client/scripts/Match/Logic/SpectateState.cs`：

```csharp
using SN = System.Numerics;
using Tank.Sim.Maps;

namespace Tank.Client.Match;

/// <summary>
/// What the camera does while the player's tank is a wreck (blueprint §5 T8). The kill close-up holds the wreck for
/// <see cref="CloseUpSeconds"/>; a mode with no respawn then follows the first living candidate on its own, a respawning
/// one stays on the wreck until the player steps away. Q, E and a click step through the candidates (teammates in a team
/// mode, everyone alive in a free-for-all), wrapping at both ends; F frees the camera (free-for-all only). A followed
/// tank that dies is swapped for the next, and when nobody is left a free-for-all turns to the free camera, a team mode
/// back to the wreck. Pure logic: the scene owns the camera and feeds the candidates (living tank ids, in a stable
/// order) each frame.
/// </summary>
public sealed class SpectateState
{
    /// <summary>How long the camera holds the fresh wreck before anything else.</summary>
    public const float CloseUpSeconds = 3f;

    /// <summary>The free camera's focus speed over the ground, m/s.</summary>
    public const float FocusSpeed = 60f;

    /// <summary>How much faster the focus moves while Shift is held.</summary>
    public const float FastMultiplier = 3f;

    public enum Mode : byte
    {
        /// <summary>The camera orbits the player's own wreck.</summary>
        Wreck,
        /// <summary>The camera orbits the tank <see cref="TargetId"/>.</summary>
        Follow,
        /// <summary>The camera orbits a focus the player drives over the ground.</summary>
        Free,
    }

    private float _closeUpLeft;
    private bool _autoFollow;

    public Mode Current { get; private set; }

    /// <summary>The tank the camera follows in <see cref="Mode.Follow"/>; 0 otherwise.</summary>
    public int TargetId { get; private set; }

    /// <summary>The own tank was just destroyed: the close-up begins. <paramref name="autoFollow"/> hands the camera on once it ends (modes with no respawn).</summary>
    public void OnDeath(bool autoFollow)
    {
        Current = Mode.Wreck;
        TargetId = 0;
        _closeUpLeft = CloseUpSeconds;
        _autoFollow = autoFollow;
    }

    /// <summary>
    /// Q (−1), E or a click (+1): the previous or next of <paramref name="candidates"/>, wrapping at both ends. From the
    /// wreck or the free camera, the first (stepping on) or last (stepping back). No candidates, no change.
    /// </summary>
    public void Step(int step, ReadOnlySpan<int> candidates)
    {
        if (step == 0 || candidates.IsEmpty) return;
        int index = candidates.IndexOf(TargetId);
        int count = candidates.Length;
        int next = index < 0
            ? (step > 0 ? 0 : count - 1)
            : ((index + step) % count + count) % count;
        TargetId = candidates[next];
        Current = Mode.Follow;
    }

    /// <summary>F in a free-for-all: the free camera (the scene seeds the focus where the view was).</summary>
    public void GoFree()
    {
        Current = Mode.Free;
        TargetId = 0;
    }

    /// <summary>
    /// Once per frame while dead. The close-up runs down and, if it is to hand over, follows the first candidate; a
    /// followed tank no longer among <paramref name="candidates"/> is swapped for the next. With no candidates at all,
    /// <paramref name="freeWhenNone"/> (a free-for-all) goes to the free camera, a team mode back to the wreck.
    /// </summary>
    public void Update(float dt, ReadOnlySpan<int> candidates, bool freeWhenNone)
    {
        switch (Current)
        {
            case Mode.Wreck:
                _closeUpLeft = Math.Max(0f, _closeUpLeft - dt);
                if (_autoFollow && _closeUpLeft <= 0f) FollowOrFallBack(candidates, freeWhenNone);
                break;
            case Mode.Follow when candidates.IndexOf(TargetId) < 0:
                FollowOrFallBack(candidates, freeWhenNone);
                break;
        }
    }

    private void FollowOrFallBack(ReadOnlySpan<int> candidates, bool freeWhenNone)
    {
        if (!candidates.IsEmpty)
        {
            TargetId = candidates[0];
            Current = Mode.Follow;
            return;
        }
        TargetId = 0;
        Current = freeWhenNone ? Mode.Free : Mode.Wreck;
    }

    /// <summary>
    /// The free camera's focus moved over the XZ plane, relative to the camera's yaw (yaw 0 faces −Z): <paramref name="forward"/>
    /// is W−S, <paramref name="right"/> D−A, <paramref name="fast"/> Shift; a diagonal is no faster. The result is
    /// clamped to the field's playable area; the scene reads the height off the terrain.
    /// </summary>
    public static SN.Vector2 MoveFocus(SN.Vector2 focus, float cameraYaw, int forward, int right, bool fast, float dt, Battlefield field)
    {
        var direction = new SN.Vector2(
            -MathF.Sin(cameraYaw) * forward + MathF.Cos(cameraYaw) * right,
            -MathF.Cos(cameraYaw) * forward - MathF.Sin(cameraYaw) * right);
        if (direction.LengthSquared() > 0f)
            focus += SN.Vector2.Normalize(direction) * (FocusSpeed * (fast ? FastMultiplier : 1f) * dt);
        return field.ClampToPlayable(focus);
    }
}
```

修改 `src/Tank.Client/scripts/Match/MatchInput.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchInput.cs b/src/Tank.Client/scripts/Match/MatchInput.cs
index 4df1f60..6ed8fb6 100644
--- a/src/Tank.Client/scripts/Match/MatchInput.cs
+++ b/src/Tank.Client/scripts/Match/MatchInput.cs
@@ -5,8 +5,8 @@ namespace Tank.Client.Match;
 
 /// <summary>
 /// A match's input: everything in <see cref="DriveInput"/> plus Tab held for the scoreboard, F3 to toggle the network
-/// panel (both already in project.godot), Enter for the host to leave the results early and the spectating keys Q (previous), E (next) and F (free camera), each
-/// consumed once by the frame that reads it (the spectating itself is wired in a later task).
+/// panel (both already in project.godot), Enter for the host to leave the results early and the spectating keys Q (previous), E (next), F (free camera)
+/// and the left button as another "next", each consumed once by the frame that reads it (only the dead read them).
 /// </summary>
 public sealed class MatchInput : DriveInput
 {
@@ -16,6 +16,7 @@ public sealed class MatchInput : DriveInput
 
     private int _spectateStep;
     private bool _spectateFree;
+    private bool _spectateClick;
     private bool _returnToRoom;
 
     public static new void RegisterActions()
@@ -33,9 +34,13 @@ public sealed class MatchInput : DriveInput
     /// <summary>Whether the F3 network panel is open; starts as <c>--netgraph</c> says.</summary>
     public bool NetPanelOpen { get; set; }
 
+    /// <summary>Whether Shift is held: the free spectating camera moves three times as fast (the key doubles as the scope toggle, which the dead don't get).</summary>
+    public bool SpectateFast => Input.IsActionPressed(SniperToggle);
+
     public override void OnInputEvent(InputEvent e)
     {
         base.OnInputEvent(e);
+        if (e is InputEventMouseButton && e.IsActionPressed(FireGun)) _spectateClick = true;
         if (e is not InputEventKey) return;   // every action below is bound to a key; mouse motion must not pay the lookups
         if (e.IsActionPressed(ToggleNetPanel)) NetPanelOpen = !NetPanelOpen;
         else if (e.IsActionPressed(SpectatePrev)) _spectateStep = -1;
@@ -67,4 +72,12 @@ public sealed class MatchInput : DriveInput
         _spectateFree = false;
         return free;
     }
+
+    /// <summary>True once after a left click: the spectating "next" (the trigger of the living; the fire latch gets the same click and sends nothing while dead).</summary>
+    public bool TakeSpectateClick()
+    {
+        bool clicked = _spectateClick;
+        _spectateClick = false;
+        return clicked;
+    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.Camera.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.Camera.cs b/src/Tank.Client/scripts/Match/MatchMain.Camera.cs
index 5d2104f..4e3a798 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.Camera.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.Camera.cs
@@ -1,17 +1,21 @@
+using System.Runtime.InteropServices;
 using Godot;
 using Tank.Client.Cameras;
 using Tank.Client.Driving;
 using Tank.Client.World;
+using Tank.Net.Battle;
 using Tank.Sim.Maps;
+using Tank.Sim.Modes;
 using Tank.Sim.Vehicles;
 
 namespace Tank.Client.Match;
 
 /// <summary>
-/// Camera, aim and the mouse (blueprint R14), as in the sandbox: the orbit camera follows the own tank and the gun is
-/// laid where it looks, unless a fixed preset frames the shot or the player holds free look; a wreck's gun stays put.
-/// The mouse is captured on entering (not in scripted or captured runs), let go by Esc or losing focus, and taken
-/// again by a click. Spectating and the pause layer come with a later task.
+/// Camera, aim, the mouse and the pause layer (blueprint R14), as in the sandbox: the orbit camera follows the own tank
+/// and the gun is laid where it looks, unless a fixed preset frames the shot or the player holds free look; a wreck's gun
+/// stays put. Dead, the camera walks the spectate chain (<see cref="SpectateState"/>): the wreck close-up, a followed
+/// tank, or the free focus WASD drives. The mouse is captured on entering (not in scripted or captured runs), let go by
+/// Esc — which brings up the pause layer — or by losing focus, and taken again by 继续 or a click.
 /// </summary>
 public partial class MatchMain
 {
@@ -44,19 +48,67 @@ public partial class MatchMain
             case InputEventMouseButton { Pressed: true } wheel when captured && _camera is not null && wheel.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown:
                 _camera.Scroll(wheel.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                 break;
-            case InputEventMouseButton { Pressed: true } when !captured && !_resultsShown:
+            case InputEventMouseButton { Pressed: true } when !captured && !_resultsShown && !Paused:
                 Input.MouseMode = Input.MouseModeEnum.Captured;
                 GetViewport().SetInputAsHandled();
                 break;
         }
-        if (e.IsActionPressed(DriveInput.SniperToggle) && _camera is not null) _camera.ToggleSniper();
-        if (e.IsActionPressed(DriveInput.UiCancel) && captured)
+        // The scope is the gun's: not for the dead (they hold Shift to speed the free camera), nor paused, on the results or before the own tank is there.
+        if (e.IsActionPressed(DriveInput.SniperToggle) && _camera is not null && _client.HasTank && !_dead && !Paused && !_resultsShown) _camera.ToggleSniper();
+        if (e.IsActionPressed(DriveInput.UiCancel) && !_resultsShown)
         {
-            Input.MouseMode = Input.MouseModeEnum.Visible;   // the pause layer comes later; Main's Esc leaves nothing while the match shows
+            if (Paused) Resume();
+            else ShowPause();
             GetViewport().SetInputAsHandled();
         }
     }
 
+    /// <summary>Esc: the mouse goes free and the pause layer comes up (built once, host button and all).</summary>
+    private void ShowPause()
+    {
+        Input.MouseMode = Input.MouseModeEnum.Visible;
+        _input.ReleaseAll();
+        if (_pause is null)
+        {
+            _pause = new PauseOverlay(_client.IsHost);
+            _pause.ResumeRequested += Resume;
+            _pause.EndMatchRequested += _client.ReturnToRoom;
+            _pause.LeaveRequested += OnLeaveRequested;
+            AddChild(_pause);
+        }
+        _pause.Open();
+    }
+
+    /// <summary>继续 (or Esc again): the layer goes and the mouse is captured back, unless the results freed it meanwhile.</summary>
+    private void Resume()
+    {
+        _pause?.Close();
+        if (!_resultsShown) Input.MouseMode = Input.MouseModeEnum.Captured;
+    }
+
+    /// <summary>离开房间: this scene stops touching the client at once; the owner tears the session down (and frees the scene).</summary>
+    private void OnLeaveRequested()
+    {
+        _finished = true;
+        LeaveRequested?.Invoke();
+    }
+
+    /// <summary>
+    /// The camera and the world's focus for this frame (R12: the grass flattens under exactly one tank). At the controls
+    /// it is the own tank; dead, the spectate chain picks the wreck, a followed tank, or nothing at all under the free
+    /// camera (the world then streams around the camera's eye). A fixed preset keeps framing the own tank, dead or not.
+    /// </summary>
+    private (TankState?, TankPose?) UpdateView(bool hasTank, in TankState own, TankPose pose, Battlefield field, float fdt)
+    {
+        if (!hasTank) return (null, null);
+        if (!_dead || _automation.HasPreset)
+        {
+            UpdateCamera(own, pose, field);
+            return (own, pose);
+        }
+        return UpdateSpectate(own, pose, field, fdt);
+    }
+
     /// <summary>Follows the own tank (or looks from a fixed preset), then lays the gun where the camera looks unless a script, free look or a wreck says not to.</summary>
     private void UpdateCamera(in TankState own, TankPose pose, Battlefield field)
     {
@@ -73,4 +125,77 @@ public partial class MatchMain
             (_aimYaw, _aimPitch) = AimSolver.Solve(_camera, field, pose.Trunnion(own.Spec), _aimYaw);
         }
     }
+
+    /// <summary>
+    /// One spectating frame (blueprint §5 T8): Q, E and a click step through the living candidates, F frees the camera
+    /// (free-for-all only), and <see cref="SpectateState"/> runs the close-up and the fallbacks. Returns what the world
+    /// should treat as the followed tank.
+    /// </summary>
+    private (TankState?, TankPose?) UpdateSpectate(in TankState own, TankPose ownPose, Battlefield field, float fdt)
+    {
+        FillCandidates();
+        ReadOnlySpan<int> candidates = CollectionsMarshal.AsSpan(_candidates);
+        int step = _input.TakeSpectateStep() + (_input.TakeSpectateClick() ? 1 : 0);
+        if (step != 0) _spectate.Step(step, candidates);
+        bool freeForAll = Mode == BattleMode.FreeForAll;
+        if (_input.TakeSpectateFree() && freeForAll) _spectate.GoFree();
+        _spectate.Update(fdt, candidates, freeWhenNone: freeForAll);
+
+        (_spectateName, _spectateFree) = (null, false);
+        if (_spectate.Current == SpectateState.Mode.Follow && TryFindRemote(_spectate.TargetId, out RemoteTank remote))
+        {
+            TankState target = RenderMath.FromRemote(remote);
+            TankPose pose = TankPose.Compute(target, field.Terrain);
+            _focus = target.Position;
+            _spectateName = SpectateNameOf(target.Id);
+            _camera.Follow(pose.Position.ToGodot(), (pose.Trunnion(target.Spec) + pose.Up * SniperEyeAboveTrunnion).ToGodot(), field.Terrain);
+            return (target, pose);
+        }
+        if (_spectate.Current == SpectateState.Mode.Free)
+        {
+            _spectateFree = true;
+            var (throttle, steer, _) = Paused ? default : _input.Peek();
+            _focus = SpectateState.MoveFocus(_focus, _camera.Yaw, throttle, steer, _input.SpectateFast, fdt, field);
+            Vector3 point = _focus.Ground(field.Terrain.HeightAt(_focus.X, _focus.Y));
+            _camera.Follow(point, point, field.Terrain);
+            return (null, null);
+        }
+        UpdateCamera(own, ownPose, field);   // the wreck close-up (and the odd frame a followed tank just vanished)
+        return (own, ownPose);
+    }
+
+    /// <summary>The tanks a dead player may watch, in the interpolator's order: living teammates in a team mode, everyone alive in a free-for-all.</summary>
+    private void FillCandidates()
+    {
+        _candidates.Clear();
+        bool teams = ModeRules.HasTeams(Mode);
+        byte ownTeam = OwnTeam(_client.Own);
+        IReadOnlyList<RemoteTank> others = _client.Others;
+        for (int i = 0; i < others.Count; i++)
+        {
+            BattleEntity entity = others[i].Entity;
+            if (entity.Has(TankFlags.Destroyed) || (teams && entity.Team != ownTeam)) continue;
+            _candidates.Add(entity.Id);
+        }
+    }
+
+    private bool TryFindRemote(int tankId, out RemoteTank remote)
+    {
+        IReadOnlyList<RemoteTank> others = _client.Others;
+        for (int i = 0; i < others.Count; i++)
+        {
+            if (others[i].Entity.Id != tankId) continue;
+            remote = others[i];
+            return true;
+        }
+        remote = default;
+        return false;
+    }
+
+    /// <summary>The name on the spectate bar, from the scoreboard's tank ids by room slot.</summary>
+    private string SpectateNameOf(int tankId)
+    {
+        int slot = _client.Latest is { } latest ? MatchHudFrame.SlotOf(latest.Players, tankId) : -1;
+        return slot >= 0 && _names[slot] is { } name ? name : UnknownName;
+    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.Events.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.Events.cs b/src/Tank.Client/scripts/Match/MatchMain.Events.cs
index 13217ac..b5a59cb 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.Events.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.Events.cs
@@ -13,7 +13,8 @@ namespace Tank.Client.Match;
 /// Events to effects (blueprint §3.3). Another tank's shot flashes where the event says, along the shell's flight, with
 /// the recoil if the tank is in sight (shooter id 0 is a shooter the server hid: the flash alone). A burst over the
 /// ground is dust off rock or armour, on it white smoke; an AP strike a small grey puff. Kills blow up and wreck the
-/// actor, the own tank's included; revivals put it back (the state's fallback is in <see cref="WreckTracker"/>). The
+/// actor, the own tank's included; revivals put it back (the state's fallback is in <see cref="WreckTracker"/>; the
+/// own tank's revival is the state's at once — <c>SyncDeath</c> — so its event usually finds nothing left to do). The
 /// combat HUD, the walls and the prop effects get every event, as in the sandbox.
 /// </summary>
 public partial class MatchMain
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.cs b/src/Tank.Client/scripts/Match/MatchMain.cs
index e36c85a..578fa84 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.cs
@@ -22,7 +22,8 @@ namespace Tank.Client.Match;
 /// and feeds it the player's command; the own tank is drawn from the prediction (between its last two ticks, at the
 /// client's render position), the others from the interpolator as <see cref="TankActors"/>, the shells from
 /// <c>client.Shells</c>, the props as the field has them; events become effects (MatchMain.Events.cs); the camera and
-/// aim follow the sandbox's (MatchMain.Camera.cs). Nothing here reads <c>Simulation</c>. A loopback host runs on a
+/// aim follow the sandbox's, a dead player's camera the spectate chain, and Esc the pause layer (MatchMain.Camera.cs).
+/// Nothing here reads <c>Simulation</c>. A loopback host runs on a
 /// virtual clock, so every frame then steps by <see cref="VirtualFrame"/>. <see cref="Finished"/> fires once when the
 /// phase leaves the match; the owner then frees the scene.
 /// </summary>
@@ -36,6 +37,8 @@ public partial class MatchMain : Node3D
     private readonly ShotDetector _shots = new();
     private readonly WreckTracker _ownWreck = new();
     private readonly ResultsClock _resultsClock = new();
+    private readonly SpectateState _spectate = new();
+    private readonly List<int> _candidates = new(BattleProtocol.MaxPlayers);   // the living tanks a dead player may watch, refilled each frame
     private readonly string?[] _names = new string?[BattleProtocol.MaxPlayers];
     private readonly HeadLookup _headOf;   // cached: a method group would make a new delegate every call
 
@@ -57,15 +60,23 @@ public partial class MatchMain : Node3D
     private CombatHud _combat = null!;
     private MatchHud _hud = null!;
     private CanvasLayer _loading = null!;
+    private PauseOverlay? _pause;   // built on the first Esc
     private double _time;
     private float _aimYaw;
     private float _aimPitch;
     private bool _finished;
-    private bool _resultsShown;   // the match has ended and the results screen is up
+    private bool _resultsShown;    // the match has ended and the results screen is up
+    private bool _dead;            // the own tank is a wreck: commands are empty and the spectate chain has the camera
+    private SN.Vector2 _focus;     // where the spectating camera looks (the free camera moves it)
+    private string? _spectateName; // the followed player's name for the HUD bar
+    private bool _spectateFree;    // the free camera is on: the bar shows its hints
 
     /// <summary>The client left the match (it ended, or the connection did); raised once.</summary>
     public event Action? Finished;
 
+    /// <summary>离开房间 on the pause layer: the owner drops the whole session (this scene has already stopped itself).</summary>
+    public event Action? LeaveRequested;
+
     public MatchMain() => _headOf = TryGetHead;
 
     /// <summary>Hands over the session to draw; call before adding the node to the tree.</summary>
@@ -137,35 +148,70 @@ public partial class MatchMain : Node3D
             own = RenderMath.OwnDisplay(_client.Predictor.Previous, _client.Predictor.Current, (float)_client.Clock.Alpha, _client.OwnRenderPosition);
             pose = TankPose.Compute(own, field.Terrain);
             ShowOwn(own, pose, fdt);
-            UpdateCamera(own, pose, field);
+            SyncDeath(own);
         }
+        (TankState? focus, TankPose? focusPose) = UpdateView(hasTank, own, pose, field, fdt);
         Vector3 cameraPos = _camera.Camera.GlobalPosition;
-        _world!.Update(hasTank ? own : null, hasTank ? pose : null, cameraPos, fdt);   // only the own tank flattens the grass (R12)
+        _world!.Update(focus, focusPose, cameraPos, fdt);   // only one tank flattens the grass: the own, or the one followed (R12)
         _actors.Sync(_client.Others, Mode, OwnTeam(own), fdt);
         _shells.Sync(_client.Shells, (float)_client.Clock.Alpha, fdt);
-        _combat.ShowOwn(hasTank && !ended);
+        _combat.ShowOwn(hasTank && !ended && !_dead);   // the dead get no reticle or status panel
         _combat.Refresh(own, spotted: hasTank && _client.Spotted && !own.IsDestroyed);
         _actors.FillBars(_combat.TankBars, BattleProtocol.MaxPlayers, _names);
         int resultsLeft = _resultsClock.Observe(_client.Match.Phase, dt);
-        _hud.SetScoreboardOpen(_input.ScoreboardHeld);
-        _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond(), resultsLeft: resultsLeft), _input.NetPanelOpen, _time);
+        _hud.SetScoreboardOpen(_input.ScoreboardHeld && !Paused);   // Tab is polled, so the pause layer has to veto it here
+        _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond(), _spectateName, _spectateFree, resultsLeft), _input.NetPanelOpen, _time);
         _automation.EndFrame(delta);
     }
 
-    /// <summary>The player's sticks, trigger and aim for this frame; only the aim while the tank is a wreck (nothing to drive).</summary>
+    /// <summary>
+    /// The own tank crossed between fighting and wreck. Death starts the spectate chain (the close-up, then — with no
+    /// respawn coming — a target on its own) with the scope off and the fight's stray Q/E/F/clicks dropped; the respawn
+    /// puts the model back at once (the own state is the word: the revive event, a frame or so behind, then finds it
+    /// intact and does nothing) with AP loaded (<see cref="RestoreOwn"/>), and snaps the camera back behind the tank.
+    /// </summary>
+    private void SyncDeath(in TankState own)
+    {
+        if (own.IsDestroyed == _dead) return;
+        _dead = own.IsDestroyed;
+        (_spectateName, _spectateFree) = (null, false);
+        if (!_dead)
+        {
+            if (_ownWreck.Revived() == WreckTracker.Change.Restore) RestoreOwn();
+            _aimYaw = own.Yaw;
+            _camera.SetAngles(own.Yaw, StartPitch);
+            return;
+        }
+        _spectate.OnDeath(autoFollow: !ModeRules.Respawns(Mode));
+        _focus = own.Position;
+        if (_camera.Sniper) _camera.ToggleSniper();   // the scope belongs to the gun; the dead orbit
+        _input.TakeSpectateStep();
+        _input.TakeSpectateClick();
+        _input.TakeSpectateFree();
+    }
+
+    /// <summary>
+    /// The player's sticks, trigger and aim for this frame (blueprint §5 T8). Dead or spectating, on the results screen or
+    /// paused, the tank stands and holds its fire, but the aim stays where it was: an aim of zero would swing the turret
+    /// to world yaw 0 for everyone watching. With no tank yet there is nothing to aim.
+    /// </summary>
     private LocalCommand Command()
     {
-        if (!_client.HasTank || _resultsShown) return LocalCommand.None;
-        if (_client.Own.IsDestroyed) return new LocalCommand(0, 0, _aimYaw, _aimPitch, false, _input.Ammo);
+        if (!_client.HasTank) return LocalCommand.None;
+        if (_resultsShown || _client.Own.IsDestroyed || Paused) return new LocalCommand(0, 0, _aimYaw, _aimPitch, false, _input.Ammo);
         var (throttle, steer, fire) = _input.Peek();
         return _automation.Command(new LocalCommand(throttle, steer, _aimYaw, _aimPitch, fire, _input.Ammo), _time);
     }
 
-    /// <summary>The results screen comes up (or goes, were a match to restart on the same scene): the mouse is let go for it and held keys drop.</summary>
+    /// <summary>Whether the pause layer is up (the mouse is then free and the commands empty; the match runs on underneath).</summary>
+    private bool Paused => _pause is { Visible: true };
+
+    /// <summary>The results screen comes up (or goes, were a match to restart on the same scene): the mouse is let go for it, held keys drop and the pause layer closes (it must not cover the verdict or eat the host's Enter).</summary>
     private void ShowResults(bool shown)
     {
         _resultsShown = shown;
         if (!shown) return;
+        _pause?.Close();
         Input.MouseMode = Input.MouseModeEnum.Visible;
         _input.ReleaseAll();
     }
PATCH
```

新建 `src/Tank.Client/scripts/Match/PauseOverlay.cs`：

```csharp
using Godot;
using Tank.Client.UI;

namespace Tank.Client.Match;

/// <summary>
/// The pause layer (blueprint §4.2, R14): Esc lets the mouse go and brings this up over the match — a real, clickable
/// control, a 440×280 panel in the middle over a dimmed screen. 继续 asks for the mouse back, 离开房间 hands the leave
/// upward (the scene must not drop the session itself), and the host alone gets 结束对局回到房间 (<c>ReturnToRoom</c>).
/// Pausing is local: the match runs on underneath, and the results screen closes the layer so it never covers the verdict
/// or eats the host's Enter.
/// </summary>
public partial class PauseOverlay : CanvasLayer
{
    private const int OverlayLayer = 3;   // over the match HUD (2)
    private const float PanelWidth = 440f, PanelHeight = 280f;
    private const float DimAlpha = 0.45f;
    private const int TitleSize = 28, Spacing = 10;

    private readonly bool _isHost;
    private Button _resume = null!;

    /// <summary>继续: the owner hides the layer and takes the mouse back.</summary>
    public event Action? ResumeRequested;

    /// <summary>结束对局回到房间 (host only): the owner should send <c>ReturnToRoom</c>.</summary>
    public event Action? EndMatchRequested;

    /// <summary>离开房间: the owner should leave the whole session.</summary>
    public event Action? LeaveRequested;

    public PauseOverlay(bool isHost)
    {
        _isHost = isHost;
        Layer = OverlayLayer;
    }

    public override void _Ready()
    {
        // The dim swallows clicks beside the panel (MouseFilter Stop), so a stray click can't recapture the mouse under the layer.
        var dim = new ColorRect { Color = new Color(0, 0, 0, DimAlpha) };
        AddChild(dim);
        dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var center = new CenterContainer { Theme = UiKit.Theme };
        AddChild(center);
        center.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        PanelContainer panel = UiKit.Panel();
        panel.CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        center.AddChild(panel);
        var rows = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        rows.AddThemeConstantOverride("separation", Spacing);
        panel.AddChild(rows);
        rows.AddChild(UiKit.Label("已暂停", TitleSize, align: HorizontalAlignment.Center));
        rows.AddChild(UiKit.Gap(Spacing));
        _resume = UiKit.Button("继续", () => ResumeRequested?.Invoke(), primary: true);
        rows.AddChild(_resume);
        if (_isHost) rows.AddChild(UiKit.Button("结束对局回到房间", () => EndMatchRequested?.Invoke()));
        rows.AddChild(UiKit.Button("离开房间", () => LeaveRequested?.Invoke()));
    }

    /// <summary>Shows the layer with the keyboard on 继续 (Enter resumes).</summary>
    public void Open()
    {
        Visible = true;
        _resume.GrabFocus();
    }

    public void Close() => Visible = false;
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 834、Net 210。

- [ ] **Step 5：截图验证**

```bash
mkdir -p build/shots/t8
for d in spectate-team spectate-ffa; do
  $S/godot_run.sh $W "" 20 -- --no-update --hud-demo $d --screenshot $W/build/shots/t8/$d.png --screenshot-at 3 --quit-after 5
done
$S/godot_run.sh $W "" 45 -- --no-update --host --mode annihilation --bots 1 --match-seconds 20 --autostart --screenshot $W/build/shots/t8/real-smoke.png --screenshot-at 24 --quit-after 30
```

Expected：
- `spectate-team`：团队模式观战条（“正在观看 队友名 · Q/E 切换”），复活横幅与观战条可同屏。
- `spectate-ffa`：各自为战观战条，提示 F 自由视角。
- `real-smoke.png`：20 秒对局正常结算；日志无 ERROR，结算后回房间。
- 真正的死亡 → 观战 → 复活流程、Q / E / 左键、F 自由视角、Esc 暂停层放进 Task 11 的手动清单。

- [ ] **Step 6：提交**

```bash
git add -A src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): death, spectating and the pause layer — the kill close-up, following teammates or anyone alive with Q/E/click, FFA's free camera, empty commands while dead, and Esc's pause layer (resume, leave room, host's end-match)"
```

---

### Task 9：启动参数完整化与截图自动化

蓝图 §5 T9。`Bootstrap/Logic/ShotPlan`（纯逻辑时刻表：`--screenshot-at`、`--fx-shot` 的 3 秒预热、`--shot-after fire|hit|kill|prop|down|death|ended[:延迟]`、`--quit-after`；写错或本场景到不了的时刻报警告而不是吞掉）+ `Bootstrap/CaptureRun`（截图、fps 行、退出，沙盒和对局共用）。`MatchAutomation` 完整版：对局的 `--screenshot-at` 从 `MatchMain` 创建起算、走对局时钟（回环 = 虚拟 1/60）；fire 取预测开炮、death 取残骸状态（比事件早约 100 ms，先到先记）、ended 取结算层；`--camera-preset/--autodrive/--fps-log/--sniper/--netgraph/--shot-after/--quit-after` 全通，fps 行与沙盒同款 `FrameTimeLog`。`SandboxAutomation` 委托后外在行为和截图时机不变。截图只由一方负责：还没进过对局、且只给了 `--screenshot-at` 时 `Main` 按进程时钟拍；一旦进了对局就归对局；`Main` 已经拍过的，对局不再拍（`ShotPlan.MarkScreenshotTaken`），整次运行只拍一张。`--autostart` 时 `--quit-after` 仍留在进程时钟上兜底（对局结束 `AutoRoom` 会开下一局、对局时钟每局清零，只按对局时钟永远退不出）。`--shot-after ended:N` 的 N 不短于结算停留时长（8 秒）时警告；由 `Main` 挂上的沙盒用 `Main` 解析好的选项，警告只打一次；坏的 `--port` 给警告。

**Files:**
- Create: `src/Tank.Client/scripts/Bootstrap/CaptureRun.cs`、`Bootstrap/Logic/ShotPlan.cs`
- Modify: `Bootstrap/{LaunchOptions,Main,Main.Flow}.cs`、`Match/{MatchAutomation,MatchMain,MatchMain.Events}.cs`、`Sandbox/{SandboxAutomation,SandboxMain}.cs`
- Test: 新建 `tests/Tank.Art.Tests/ShotPlanTests.cs`；修改 `LaunchOptionsTests.cs`

- [ ] **Step 1：写失败的测试**

修改 `tests/Tank.Art.Tests/LaunchOptionsTests.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/LaunchOptionsTests.cs b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
index ed1f2ab..942bf9a 100644
--- a/tests/Tank.Art.Tests/LaunchOptionsTests.cs
+++ b/tests/Tank.Art.Tests/LaunchOptionsTests.cs
@@ -260,6 +260,32 @@ public class LaunchOptionsTests
         Assert.Empty(options.Warnings);
     }
 
+    [Fact]
+    public void Port_takes_a_number_in_range_and_a_bad_one_warns_and_keeps_the_default()
+    {
+        Assert.Equal(47555, LaunchOptions.Parse(new[] { "--port", "47555" }).Port);
+        int fallback = LaunchOptions.Parse(Array.Empty<string>()).Port;
+        foreach (string bad in new[] { "0", "-1", "65536", "lan" })
+        {
+            LaunchOptions options = LaunchOptions.Parse(new[] { "--port", bad });
+            Assert.Equal(fallback, options.Port);
+            Assert.Contains("--port", Assert.Single(options.Warnings));
+        }
+    }
+
+    [Fact]
+    public void Bad_timer_values_warn_and_keep_their_defaults()
+    {
+        LaunchOptions options = LaunchOptions.Parse(new[] { "--screenshot-at", "soon", "--quit-after", "later", "--fx-shot", "now" });
+        Assert.Equal(6, options.ScreenshotAt);
+        Assert.Equal(0, options.QuitAfter);
+        Assert.Equal(0.05, options.FxShotDelay);
+        Assert.Equal(3, options.Warnings.Count);
+        Assert.Contains(options.Warnings, w => w.Contains("--screenshot-at"));
+        Assert.Contains(options.Warnings, w => w.Contains("--quit-after"));
+        Assert.Contains(options.Warnings, w => w.Contains("--fx-shot"));
+    }
+
     [Fact]
     public void The_host_options_of_a_smoke_run_parse_together()
     {
PATCH
```

新建 `tests/Tank.Art.Tests/ShotPlanTests.cs`：

```csharp
using Tank.Client.Bootstrap;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Art.Tests;

public class ShotPlanTests
{
    private const int Own = 1;
    private const int Other = 2;

    private static ShotPlan Sandbox(params string[] args) => ShotPlan.ForSandbox(LaunchOptions.Parse(args));
    private static ShotPlan Match(params string[] args) => ShotPlan.ForMatch(LaunchOptions.Parse(args));

    private static GameEvent Event(GameEventType type, int tankId = Own, int targetTankId = 0) =>
        new(type, Tick: 1, TankId: tankId, ShellId: 1, Position: default, Velocity: default, TargetTankId: targetTankId);

    [Fact]
    public void Screenshot_at_counts_from_the_start()
    {
        ShotPlan plan = Sandbox("--screenshot", "s.png", "--screenshot-at", "6");
        Assert.False(plan.TakeScreenshot(5.9));
        Assert.True(plan.TakeScreenshot(6.0));
        Assert.False(plan.TakeScreenshot(7.0));   // taken once, never again
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData("fire", GameEventType.ShellFired)]
    [InlineData("hit", GameEventType.TankHit)]
    [InlineData("kill", GameEventType.TankDestroyed)]
    [InlineData("prop", GameEventType.PropDamaged)]
    [InlineData("down", GameEventType.PropDestroyed)]
    public void Shot_after_waits_for_the_players_first_event_plus_the_delay(string moment, GameEventType type)
    {
        ShotPlan plan = Sandbox("--shot-after", $"{moment}:2");
        Assert.False(plan.TakeScreenshot(100));              // the moment hasn't happened, however long the run
        plan.OnEvent(Event(type, tankId: Other), Own, 3);    // somebody else's doesn't count
        Assert.False(plan.TakeScreenshot(100));
        plan.OnEvent(Event(type), Own, 4);
        Assert.False(plan.TakeScreenshot(5.9));
        Assert.True(plan.TakeScreenshot(6.0));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Only_the_first_moment_counts()
    {
        ShotPlan plan = Sandbox("--shot-after", "fire:1");
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 2);
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 50);   // a later shot doesn't push the screenshot back
        Assert.True(plan.TakeScreenshot(3));
        Assert.True(plan.Seen(ShotMoment.Fire));
    }

    [Fact]
    public void Death_is_the_own_tank_destroyed_by_anyone()
    {
        ShotPlan plan = Match("--shot-after", "death:1");
        plan.OnEvent(Event(GameEventType.TankDestroyed, tankId: Own, targetTankId: Other), Own, 2);   // the player's kill, not a death
        Assert.False(plan.TakeScreenshot(100));
        plan.OnEvent(Event(GameEventType.TankDestroyed, tankId: Other, targetTankId: Own), Own, 5);
        Assert.False(plan.TakeScreenshot(5.9));
        Assert.True(plan.TakeScreenshot(6.0));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Ended_comes_straight_from_the_caller()
    {
        ShotPlan plan = Match("--shot-after", "ended:3");
        Assert.False(plan.TakeScreenshot(100));
        plan.OnMoment(ShotMoment.Ended, 20);
        plan.OnMoment(ShotMoment.Ended, 21);   // the next frame's repeat doesn't move it
        Assert.False(plan.TakeScreenshot(22.9));
        Assert.True(plan.TakeScreenshot(23.0));
        Assert.Empty(plan.Warnings);
    }

    [Theory]
    [InlineData(8.0)]    // the results stay 8 s: the scene is gone by then
    [InlineData(12.5)]
    public void Ended_later_than_the_results_stay_warns_that_it_can_never_be_taken(double delay)
    {
        ShotPlan plan = Match("--shot-after", $"ended:{delay.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("--shot-after ended:", warning);
        Assert.Contains($"{ShotPlan.ResultsStaySeconds} s", warning);
    }

    [Fact]
    public void Ended_within_the_results_stay_is_fine_and_the_stay_is_the_servers()
    {
        Assert.Equal(ModeRules.ResultsTicks / (double)SimConstants.TickRate, ShotPlan.ResultsStaySeconds);
        Assert.Empty(Match("--shot-after", "ended:7.5").Warnings);
    }

    [Fact]
    public void A_screenshot_taken_elsewhere_is_not_taken_again()
    {
        ShotPlan plan = Match("--screenshot", "s.png", "--screenshot-at", "6");
        Assert.False(plan.ScreenshotTaken);
        plan.MarkScreenshotTaken();   // Main took it in the room, or an earlier match did
        Assert.True(plan.ScreenshotTaken);
        Assert.False(plan.TakeScreenshot(6.0));
        Assert.False(plan.TakeScreenshot(100));
    }

    [Fact]
    public void Taking_the_screenshot_marks_it_taken_for_the_next_scene()
    {
        ShotPlan plan = Match("--screenshot", "s.png", "--screenshot-at", "2");
        Assert.True(plan.TakeScreenshot(2));
        Assert.True(plan.ScreenshotTaken);
    }

    [Theory]
    [InlineData(true, "--screenshot", "s.png")]
    [InlineData(true, "--screenshot", "s.png", "--screenshot-at", "20")]
    [InlineData(false, "--screenshot", "s.png", "--shot-after", "kill:2")]
    [InlineData(false, "--screenshot", "s.png", "--fx-shot", "0.5")]
    public void Only_a_plain_screenshot_at_is_on_the_start_schedule_that_Main_can_take(bool expected, params string[] args)
    {
        Assert.Equal(expected, ShotPlan.StartScheduleOnly(LaunchOptions.Parse(args)));
    }

    [Fact]
    public void Fx_shot_waits_for_the_first_shot_after_the_warmup()
    {
        ShotPlan plan = Sandbox("--fx-shot", "1.5");
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 2);   // inside the warm-up: doesn't count
        Assert.False(plan.Seen(ShotMoment.Fire));
        Assert.False(plan.TakeScreenshot(100));
        plan.OnEvent(Event(GameEventType.ShellFired), Own, 4);
        Assert.True(plan.Seen(ShotMoment.Fire));
        Assert.False(plan.TakeScreenshot(5.4));
        Assert.True(plan.TakeScreenshot(5.5));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Quit_after_runs_out_and_zero_means_never()
    {
        ShotPlan plan = Sandbox("--quit-after", "12");
        Assert.False(plan.QuitDue(11.9));
        Assert.True(plan.QuitDue(12.0));
        Assert.False(Sandbox().QuitDue(100000));
    }

    [Fact]
    public void A_moment_the_sandbox_never_reaches_warns_and_never_fires()
    {
        ShotPlan plan = Sandbox("--shot-after", "ended:1");
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("'ended'", warning);
        Assert.Contains("the sandbox", warning);
        Assert.False(plan.TakeScreenshot(100));
    }

    [Fact]
    public void A_moment_a_match_never_reaches_warns_too()
    {
        ShotPlan plan = Match("--shot-after", "prop:1");
        Assert.Contains("'prop'", Assert.Single(plan.Warnings));
        plan.OnEvent(Event(GameEventType.PropDamaged), Own, 2);
        Assert.True(plan.TakeScreenshot(3));   // the moment is still honoured if it does happen
    }

    [Fact]
    public void An_unknown_moment_warns_and_falls_back_to_the_start_schedule()
    {
        // LaunchOptions rejects these itself; a plan handed one anyway must warn, not throw or go silent.
        var options = LaunchOptions.Parse(new[] { "--screenshot-at", "4" }) with { ShotAfterEvent = "wall", ShotAfterDelay = 1 };
        ShotPlan plan = ShotPlan.ForSandbox(options);
        string warning = Assert.Single(plan.Warnings);
        Assert.Contains("'wall'", warning);
        Assert.Contains("fire|hit|kill|prop|down|death|ended", warning);
        Assert.False(plan.TakeScreenshot(3.9));
        Assert.True(plan.TakeScreenshot(4.0));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，`ShotPlan` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Client/scripts/Bootstrap/CaptureRun.cs`：

```csharp
using Godot;
using Tank.Client.Sandbox;

namespace Tank.Client.Bootstrap;

/// <summary>
/// The Godot side of a scripted run's captures, shared by the sandbox and the match: takes the screenshot the frame its
/// <see cref="ShotPlan"/> says it falls due, logs render costs once a second with <c>--fps-log</c>
/// (<see cref="FrameTimeLog"/>'s line) and quits the tree at <c>--quit-after</c>. Call <see cref="EndFrame"/> at the end
/// of every frame with the owner's clock — wall time in the sandbox and a live match, the virtual clock of a loopback
/// run — so the schedule matches what the mode simulates. Build it only once the host is in the tree: the fps log turns
/// on render-time measuring for the host's viewport.
/// </summary>
public sealed class CaptureRun
{
    private readonly LaunchOptions _options;
    private readonly Node _host;
    private readonly ShotPlan _plan;
    private readonly FrameTimeLog _frameLog = new();

    /// <param name="host">The node whose viewport is captured and measured, and whose tree is quit.</param>
    public CaptureRun(LaunchOptions options, Node host, ShotPlan plan)
    {
        (_options, _host, _plan) = (options, host, plan);
        if (options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(host.GetViewport().GetViewportRid(), true);
    }

    /// <summary>Call at the end of every frame: logs render costs, takes the screenshot when due and quits when time is up.</summary>
    public void EndFrame(double delta, double time)
    {
        if (_options.FpsLog) LogFrame(delta);
        if (_options.ScreenshotPath is { } path && _plan.TakeScreenshot(time))
        {
            Error error = _host.GetViewport().GetTexture().GetImage().SavePng(path);
            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
        }
        if (_plan.QuitDue(time)) _host.GetTree().Quit();
    }

    private void LogFrame(double delta)
    {
        Rid rid = _host.GetViewport().GetViewportRid();
        string? line = _frameLog.Add(delta,
            RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
        if (line is not null) GD.Print(line);
    }
}
```

修改 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
index 1516eef..a36983b 100644
--- a/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
+++ b/src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs
@@ -11,13 +11,14 @@ namespace Tank.Client.Bootstrap;
 /// --name N  --host  --join IP[:PORT]  --port N  --mode teambattle|annihilation|ffa  --bots N  --autostart  --players N（房主等够 N 个真人才自动开局，N 大于 1 时人齐后再等约半秒；默认 1，最多一个房间的人数 10）
 /// --seed N（对局种子，可复现）  --match-seconds S（对局时长，覆盖模式默认值）  --loopback（房主用虚拟网络和固定步长，没有套接字和线程，用于确定性冒烟）
 /// --sandbox（启动后直接进试驾场）  --hud-demo NAME（对局 HUD 画廊：用预制帧在纯色底上画 HUD 的每种状态，用于验收排版）
-/// --netgraph（F3 网络面板一开始就打开）  --screenshot FILE  --screenshot-at SECONDS  --quit-after SECONDS
-/// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场：自动驾驶并开火，用于截图）
-/// --camera-preset front|side|close|closerear|sprocket|hill|spin|orbit|bush|target|yard|gate|patrol（试驾场：固定镜头，用于截图和测帧）  --fps-log（试驾场：每秒打印平均和最差帧时间）
-/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场：以狙击镜视角启动，用于截图）
+/// --netgraph（F3 网络面板一开始就打开）  --screenshot FILE（一次运行只截一张）  --screenshot-at SECONDS（还没进过对局时从启动起算，在菜单、房间或 HUD 画廊里截；
+/// 一进对局就交给对局，从对局场景创建起算；--shot-after、--fx-shot 只由对局或试驾场截）  --quit-after SECONDS
+/// --no-update（不检查在线更新）  --update-now（更新就绪后不询问直接安装，用于测试）  --autodrive（试驾场和对局：自动驾驶并开火，用于截图）
+/// --camera-preset front|side|close|closerear|sprocket|hill|spin|orbit|bush|target|yard|gate|patrol（试驾场和对局：固定镜头，用于截图和测帧）  --fps-log（试驾场和对局：每秒打印平均和最差帧时间）
+/// --fx-shot SECONDS（试驾场，配合 --autodrive：炮口压低，3 秒后第一次开炮再过 SECONDS 秒截图，用于检查特效）  --sniper（试驾场和对局：以狙击镜视角启动，用于截图）
 /// --range-demo pen|ricochet|nopen|he|kill|nopen-heavy|kill-td|turn|wall|breach|fence|ram|bush|lamp（试驾场：自动对靶车开炮或开车撞墙，用于截图）
-/// --shot-after fire|hit|kill|prop|down|death|ended:SECONDS（试驾场：玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤、第一次有物件被毁、
-/// 自己的坦克第一次被击毁（death）或对局结算开始（ended，仅对局）后再过 SECONDS 秒截图）
+/// --shot-after fire|hit|kill|prop|down|death|ended:SECONDS（玩家第一次开炮、第一次命中坦克、第一次击毁坦克、第一次有墙受伤（prop，仅试驾场）、第一次有物件被毁（down，仅试驾场）、
+/// 自己的坦克第一次被击毁（death，仅对局）或对局结算开始（ended，仅对局）后再过 SECONDS 秒截图）
 /// --tank light|medium|heavy|td（开局的车型：试驾场里默认中坦；房间里则在连上后自动选这辆）  --spot-debug（试驾场：一开始就显示点亮调试文字，同 V 键）
 /// A plain parser, free of the engine: values it can't read fall back to defaults and are listed in <see cref="Warnings"/>
 /// for the caller to report, and so are a switch it doesn't know and a switch that needs a value but is followed by
@@ -101,7 +102,7 @@ public sealed record LaunchOptions
                 "--name" => o with { Name = Next() },
                 "--host" => o with { Host = true },
                 "--join" => o with { JoinAddress = Next() },
-                "--port" => o with { Port = int.TryParse(Next(), out int p) ? p : o.Port },
+                "--port" => o with { Port = ParsePort(Next(), o.Port, warnings) },
                 "--mode" => ParseMode(o, Next(), warnings),
                 "--bots" => o with { Bots = ParseBots(Next(), warnings) },
                 "--players" => o with { Players = ParsePlayers(Next(), o.Players, warnings) },
@@ -179,6 +180,14 @@ public sealed record LaunchOptions
         return clamped;
     }
 
+    private static int ParsePort(string value, int fallback, List<string> warnings)
+    {
+        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= ushort.MaxValue)
+            return port;
+        warnings.Add($"Bad --port '{value}'; use 1 to {ushort.MaxValue}. Using {fallback.ToString(CultureInfo.InvariantCulture)}.");
+        return fallback;
+    }
+
     private static int ParsePlayers(string value, int fallback, List<string> warnings)
     {
         if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int players) || players < 1)
PATCH
```

新建 `src/Tank.Client/scripts/Bootstrap/Logic/ShotPlan.cs`：

```csharp
using System.Globalization;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Client.Bootstrap;

/// <summary>A moment a timed capture step can wait for, each noted the first time it happens.</summary>
public enum ShotMoment
{
    /// <summary>The run began (always already seen, at time 0).</summary>
    Start,
    /// <summary>The player fired.</summary>
    Fire,
    /// <summary>The player's shell struck a tank.</summary>
    Hit,
    /// <summary>The player destroyed a tank.</summary>
    Kill,
    /// <summary>The player's shell hurt a wall.</summary>
    Prop,
    /// <summary>The player destroyed a prop (a wall, a fence, a bush).</summary>
    Down,
    /// <summary>The player's own tank was destroyed (a match only).</summary>
    Death,
    /// <summary>The results came up (a match only).</summary>
    Ended,
}

/// <summary>
/// The timetable of a scripted run (blueprint §5 T9), pure logic shared by the sandbox and the match; <c>CaptureRun</c>
/// executes it. The screenshot is due a delay after one moment: the one <c>--shot-after EVENT:SECONDS</c> names, the
/// player's first shot with <c>--fx-shot SECONDS</c> (where nothing before a <see cref="FxShotWarmup"/> counts), or the
/// run's start (<c>--screenshot-at</c>, the default). The quit is due at <c>--quit-after</c>. Moments come from the
/// game events (<see cref="OnEvent"/>) or straight from the caller (<see cref="OnMoment"/>: the results screen is no
/// event, and the own shot in a match is seen in the predicted shot count). A <c>--shot-after</c> this plan can't map,
/// or one its run can never reach (<c>death</c> in the sandbox, say, or <c>ended</c> later than the results stay up), is
/// a line in <see cref="Warnings"/>, never silently swallowed; an unmappable one falls back to the start schedule.
/// A run takes one screenshot: a plan told it was already taken elsewhere (<see cref="MarkScreenshotTaken"/>) takes none.
/// </summary>
public sealed class ShotPlan
{
    /// <summary>--fx-shot: moments before this many seconds don't count, so the first shot caught is a warmed-up one.</summary>
    public const double FxShotWarmup = 3;

    /// <summary>How long the results stay up before the server sends everyone back to the room (and the match scene goes).</summary>
    public const double ResultsStaySeconds = ModeRules.ResultsTicks / (double)SimConstants.TickRate;

    private static readonly ShotMoment[] SandboxMoments = { ShotMoment.Fire, ShotMoment.Hit, ShotMoment.Kill, ShotMoment.Prop, ShotMoment.Down };
    private static readonly ShotMoment[] MatchMoments = { ShotMoment.Fire, ShotMoment.Hit, ShotMoment.Kill, ShotMoment.Death, ShotMoment.Ended };

    private readonly double[] _firstSeen = NotSeenYet();   // per ShotMoment: when it first happened, s; -1 not yet
    private readonly double _countFrom;
    private readonly double _quitAfter;
    private readonly ShotMoment _shotAfter;
    private readonly double _shotDelay;
    private bool _screenshotTaken;

    private ShotPlan(LaunchOptions options, string where, ShotMoment[] supported)
    {
        var warnings = new List<string>();
        _countFrom = options.FxShotDelay is null ? 0 : FxShotWarmup;
        _quitAfter = options.QuitAfter;
        (_shotAfter, _shotDelay) = PlanScreenshot(options, where, supported, warnings);
        Warnings = warnings;
    }

    /// <summary>A sandbox run's plan: the player can't die and nothing ends, so <c>death</c> and <c>ended</c> warn.</summary>
    public static ShotPlan ForSandbox(LaunchOptions options) => new(options, "the sandbox", SandboxMoments);

    /// <summary>A match's plan: walls and props aren't the point there, so <c>prop</c> and <c>down</c> warn.</summary>
    public static ShotPlan ForMatch(LaunchOptions options) => new(options, "a match", MatchMoments);

    /// <summary>One line per capture option that can't work as written, for the caller to report.</summary>
    public IReadOnlyList<string> Warnings { get; }

    /// <summary>
    /// Whether the screenshot is a plain <c>--screenshot-at</c> (no <c>--shot-after</c>, no <c>--fx-shot</c>): the only
    /// kind <c>Main</c> takes itself, on the process clock, in the menu, the room or the HUD gallery before any match.
    /// </summary>
    public static bool StartScheduleOnly(LaunchOptions options) => options.ShotAfterEvent is null && options.FxShotDelay is null;

    /// <summary>Whether this plan's screenshot is spent: taken here, or marked taken elsewhere.</summary>
    public bool ScreenshotTaken => _screenshotTaken;

    /// <summary>The run's one screenshot was taken by someone else (Main in the room, an earlier match): this plan takes none.</summary>
    public void MarkScreenshotTaken() => _screenshotTaken = true;

    /// <summary>Whether <paramref name="moment"/> has already happened (the range demo fires once, then stops).</summary>
    public bool Seen(ShotMoment moment) => _firstSeen[(int)moment] >= 0;

    /// <summary>
    /// Notes the first time the player (<paramref name="ownTankId"/>) fired, hit or destroyed a tank, hurt a wall or
    /// destroyed a prop — and the first time the player's own tank was destroyed, whoever did it.
    /// </summary>
    public void OnEvent(in GameEvent e, int ownTankId, double time)
    {
        if (e.Type == GameEventType.TankDestroyed && e.TargetTankId == ownTankId) OnMoment(ShotMoment.Death, time);
        if (e.TankId != ownTankId) return;
        ShotMoment? moment = e.Type switch
        {
            GameEventType.ShellFired => ShotMoment.Fire,
            GameEventType.TankHit => ShotMoment.Hit,
            GameEventType.TankDestroyed => ShotMoment.Kill,
            GameEventType.PropDamaged => ShotMoment.Prop,
            GameEventType.PropDestroyed => ShotMoment.Down,
            _ => null,
        };
        if (moment is { } m) OnMoment(m, time);
    }

    /// <summary>Notes a moment the caller saw itself (the results screen, a predicted own shot); only the first time counts.</summary>
    public void OnMoment(ShotMoment moment, double time)
    {
        if (time >= _countFrom && _firstSeen[(int)moment] < 0) _firstSeen[(int)moment] = time;
    }

    /// <summary>True exactly once, the frame the screenshot falls due.</summary>
    public bool TakeScreenshot(double time)
    {
        if (_screenshotTaken || !ScreenshotDue(time)) return false;
        _screenshotTaken = true;
        return true;
    }

    /// <summary>Whether <c>--quit-after</c> has run out.</summary>
    public bool QuitDue(double time) => _quitAfter > 0 && time >= _quitAfter;

    /// <summary>--shot-after waits for its moment, --fx-shot for the player's first shot, otherwise --screenshot-at counts from the start.</summary>
    private static (ShotMoment After, double Delay) PlanScreenshot(LaunchOptions options, string where, ShotMoment[] supported, List<string> warnings)
    {
        if (options.ShotAfterEvent is { } awaited)
        {
            if (TryParseMoment(awaited, out ShotMoment moment))
            {
                if (Array.IndexOf(supported, moment) < 0)
                    warnings.Add($"--shot-after '{awaited}' never happens in {where}; the screenshot will not be taken.");
                else if (moment == ShotMoment.Ended && options.ShotAfterDelay >= ResultsStaySeconds)
                    warnings.Add(string.Create(CultureInfo.InvariantCulture,
                        $"--shot-after ended:{options.ShotAfterDelay} falls after the results have gone (they stay {ResultsStaySeconds} s); the screenshot will not be taken."));
                return (moment, options.ShotAfterDelay);
            }
            warnings.Add($"Unknown --shot-after moment '{awaited}'; use fire|hit|kill|prop|down|death|ended. Using --screenshot-at.");
        }
        if (options.FxShotDelay is { } fxDelay) return (ShotMoment.Fire, fxDelay);
        return (ShotMoment.Start, options.ScreenshotAt);
    }

    private static bool TryParseMoment(string name, out ShotMoment moment)
    {
        (bool known, moment) = name switch
        {
            "fire" => (true, ShotMoment.Fire),
            "hit" => (true, ShotMoment.Hit),
            "kill" => (true, ShotMoment.Kill),
            "prop" => (true, ShotMoment.Prop),
            "down" => (true, ShotMoment.Down),
            "death" => (true, ShotMoment.Death),
            "ended" => (true, ShotMoment.Ended),
            _ => (false, ShotMoment.Start),
        };
        return known;
    }

    private bool ScreenshotDue(double time)
    {
        double since = _firstSeen[(int)_shotAfter];
        return since >= 0 && time >= since + _shotDelay;
    }

    /// <summary>Every moment unseen (-1), but the start, which is at 0.</summary>
    private static double[] NotSeenYet()
    {
        var seen = new double[Enum.GetValues<ShotMoment>().Length];
        Array.Fill(seen, -1);
        seen[(int)ShotMoment.Start] = 0;
        return seen;
    }
}
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
index 9aabb94..7403a3c 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
@@ -105,7 +105,8 @@ public partial class Main
         _room.Visible = false;
         GD.Print($"[match] entered match {client.Room?.Mode} as slot {client.Slot}");
         var match = new MatchMain();
-        match.Setup(_session!, _options);
+        match.Setup(_session!, _options, _screenshotTaken);
+        _matchEntered = true;   // from now on the matches own the screenshot (RunAutomation)
         match.Finished += LeaveMatch;
         match.LeaveRequested += () => LeaveSession("已离开房间。");   // 离开房间 on the pause layer: the whole session goes
         _matchView = match;
@@ -116,6 +117,7 @@ public partial class Main
     /// <summary>The match is over (or the session ended): its scene goes, and the launch options may start the next one.</summary>
     private void LeaveMatch()
     {
+        if (_matchView is { ScreenshotTaken: true }) _screenshotTaken = true;   // the next match must not take it again
         _matchView?.QueueFree();
         _matchView = null;
         Input.MouseMode = Input.MouseModeEnum.Visible;
@@ -143,6 +145,7 @@ public partial class Main
         LeaveSession(null);
         _menu.Visible = false;
         _sandbox = new SandboxMain();
+        _sandbox.Setup(_options);
         _sandbox.ExitRequested += ExitSandbox;
         AddChild(_sandbox);
     }
PATCH
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.cs b/src/Tank.Client/scripts/Bootstrap/Main.cs
index aa2f484..4972d69 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.cs
@@ -26,7 +26,8 @@ public partial class Main : Node
     private SandboxMain? _sandbox;
     private MatchMain? _matchView;
     private double _time;
-    private bool _screenshotTaken;
+    private bool _screenshotTaken;   // the run's one screenshot: taken by Main, or by a match (read back as its scene goes)
+    private bool _matchEntered;      // a match scene has been entered: Main no longer takes the screenshot
 
     public override void _Ready()
     {
@@ -122,8 +123,14 @@ public partial class Main : Node
 
     private void RunAutomation()
     {
-        // A sandbox started from here runs its own screenshot schedule.
-        if (_sandbox is null && _options.ScreenshotPath is { } path && !_screenshotTaken && _time >= _options.ScreenshotAt)
+        // One screenshot per run (T9). Main takes a plain --screenshot-at on its own clock while no match has been entered
+        // — the menu, the room, the HUD gallery; once a match scene is entered, the matches own it (counted from the scene's
+        // creation, and --shot-after and --fx-shot wait for moments only a match or the sandbox has), and whoever takes
+        // it first is the only one: Main hands its flag to each match and reads the match's back. A sandbox run schedules
+        // its own. The timed quit stays here, on the process clock: --autostart starts match after match, and a quit
+        // counted from each match's creation would never fall due were the match to keep ending sooner.
+        if (_sandbox is null && !_matchEntered && !_screenshotTaken && _options.ScreenshotPath is { } path
+            && ShotPlan.StartScheduleOnly(_options) && _time >= _options.ScreenshotAt)
         {
             _screenshotTaken = true;
             Image image = GetViewport().GetTexture().GetImage();
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchAutomation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchAutomation.cs b/src/Tank.Client/scripts/Match/MatchAutomation.cs
index 8516f0c..bb94e52 100644
--- a/src/Tank.Client/scripts/Match/MatchAutomation.cs
+++ b/src/Tank.Client/scripts/Match/MatchAutomation.cs
@@ -2,15 +2,22 @@ using Godot;
 using Tank.Client.Bootstrap;
 using Tank.Client.Cameras;
 using Tank.Net.Battle;
+using Tank.Sim;
 
 namespace Tank.Client.Match;
 
 /// <summary>
-/// A match's scripted runs, the minimum for repeatable screenshots: a fixed <c>--camera-preset</c> (nobody drives),
-/// <c>--autodrive</c> (full throttle, steering right part of the time so it drives loops, firing whenever the gun is
-/// roughly level) and <c>--fps-log</c> (a line every <see cref="FpsLogSeconds"/>). Any of these, or a screenshot or
-/// timed quit, makes the run ignore the mouse and keyboard. The screenshot and quit timers stay with <c>Main</c> until the
-/// capture task moves them here.
+/// A match's scripted runs, for repeatable screenshots and frame-time logs (blueprint §5 T9): a fixed
+/// <c>--camera-preset</c> (nobody drives), <c>--autodrive</c> (full throttle, steering right part of the time so it
+/// drives loops, firing whenever the gun is roughly level), and the shared capture pieces — the timetable is a
+/// <see cref="ShotPlan"/> (<c>--screenshot-at</c> counts from the match scene's creation, <c>--shot-after</c> waits for
+/// <c>fire|hit|kill|death|ended</c>, <c>--quit-after</c> quits) and a <see cref="CaptureRun"/> executes it, with
+/// <c>--fps-log</c>'s once-a-second line; a screenshot Main or an earlier match already took is not taken again. Any
+/// of these makes the run ignore the mouse and keyboard. The moments come
+/// from the scene: the server's events (<see cref="OnEvent"/>), the predicted own shot (<see cref="NotifyFired"/>: an
+/// own shot never reaches the event queue), the own wreck state (<see cref="NotifyDeath"/>, which beats the late
+/// destroy event) and the results screen (<see cref="NotifyEnded"/>). Times are the match clock — wall seconds, or the
+/// virtual clock of a loopback run.
 /// </summary>
 public sealed class MatchAutomation
 {
@@ -19,16 +26,17 @@ public sealed class MatchAutomation
     private const double AutoTurnRate = 0.4;        // --autodrive: steers right while sin(rate × time) is above the threshold
     private const double AutoTurnThreshold = 0.5;
     private const float LevelPitch = 0.2f;          // --autodrive: fires while the aim is within this of level, rad
-    private const double FpsLogSeconds = 5.0;
 
     private readonly LaunchOptions _options;
-    private double _fpsElapsed;
-    private int _fpsFrames;
-    private double _fpsWorst;
+    private readonly ShotPlan _plan;
+    private CaptureRun? _capture;   // built in Start: the fps log needs the host's viewport, so the host must be in the tree
 
-    public MatchAutomation(LaunchOptions options)
+    /// <param name="screenshotTaken">The run's screenshot is already taken (by Main in the room, or by an earlier match): none here.</param>
+    public MatchAutomation(LaunchOptions options, bool screenshotTaken)
     {
         _options = options;
+        _plan = ShotPlan.ForMatch(options);
+        if (screenshotTaken) _plan.MarkScreenshotTaken();
         HasPreset = CameraPresets.IsKnown(options.CameraPreset);
     }
 
@@ -40,16 +48,35 @@ public sealed class MatchAutomation
     /// <summary>Whether a script drives the tank (<c>--autodrive</c>).</summary>
     public bool Drives => _options.AutoDrive;
 
+    /// <summary>Whether the run's screenshot is spent (taken here, or before this match), for the owner to hand on to the next match.</summary>
+    public bool ScreenshotTaken => _plan.ScreenshotTaken;
+
     /// <summary>Scripted and captured runs ignore stray input so screenshots stay repeatable.</summary>
     public bool IgnoresInput => HasPreset || _options.AutoDrive || _options.ScreenshotPath is not null || _options.QuitAfter > 0;
 
-    /// <summary>Warns about launch options that name no known preset.</summary>
+    /// <summary>Call from the scene's <c>_Ready</c> (the host now in the tree): the screenshot, fps log and quit arm here.</summary>
+    public void Start(Node host) => _capture = new CaptureRun(_options, host, _plan);
+
+    /// <summary>Warns about launch options that name no known preset or wait for a moment a match never reaches.</summary>
     public void ReportBadOptions()
     {
+        foreach (string warning in _plan.Warnings) GD.PushWarning(warning);
         if (_options.CameraPreset is { } preset && !HasPreset)
             GD.PushWarning($"Unknown --camera-preset '{preset}'; use {string.Join(", ", CameraPresets.Names)}.");
     }
 
+    /// <summary>Notes the first server event of each kind by (or destroying) the own tank.</summary>
+    public void OnEvent(in GameEvent e, int ownTankId, double time) => _plan.OnEvent(e, ownTankId, time);
+
+    /// <summary>The own tank fired (seen in the predicted shot count; a predicted shot never reaches the event queue).</summary>
+    public void NotifyFired(double time) => _plan.OnMoment(ShotMoment.Fire, time);
+
+    /// <summary>The own tank became a wreck (the state beats the destroy event by about the interpolation delay).</summary>
+    public void NotifyDeath(double time) => _plan.OnMoment(ShotMoment.Death, time);
+
+    /// <summary>The results screen came up.</summary>
+    public void NotifyEnded(double time) => _plan.OnMoment(ShotMoment.Ended, time);
+
     /// <summary>The command for this frame: <paramref name="player"/>'s own, or the script's in its place.</summary>
     public LocalCommand Command(in LocalCommand player, double time)
     {
@@ -68,17 +95,6 @@ public sealed class MatchAutomation
     public (float Yaw, float Pitch)? CameraAngles(float tankYaw) =>
         _options.AutoDrive ? (tankYaw + AutoCameraYaw, AutoCameraPitch) : null;
 
-    /// <summary>Call once per frame: prints the average frame rate and the worst frame every <see cref="FpsLogSeconds"/> with <c>--fps-log</c>.</summary>
-    public void EndFrame(double delta)
-    {
-        if (!_options.FpsLog) return;
-        _fpsElapsed += delta;
-        _fpsFrames++;
-        _fpsWorst = Math.Max(_fpsWorst, delta);
-        if (_fpsElapsed < FpsLogSeconds) return;
-        GD.Print($"[fps] {_fpsFrames / _fpsElapsed:0.0} fps over {_fpsElapsed:0.0} s, worst frame {_fpsWorst * 1000:0.0} ms");
-        _fpsElapsed = 0;
-        _fpsFrames = 0;
-        _fpsWorst = 0;
-    }
+    /// <summary>Call once per frame, last: logs render costs, takes the screenshot when due and quits when time is up.</summary>
+    public void EndFrame(double delta, double time) => _capture?.EndFrame(delta, time);
 }
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.Events.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.Events.cs b/src/Tank.Client/scripts/Match/MatchMain.Events.cs
index b5a59cb..1c37033 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.Events.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.Events.cs
@@ -30,6 +30,7 @@ public partial class MatchMain
     private void OnEvent(in GameEvent e)
     {
         Battlefield field = _field!;
+        _automation.OnEvent(e, _client.OwnTankId, _time);
         if (e.Type == GameEventType.TankDestroyed) FeedKill(e);
         switch (e.Type)
         {
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.cs b/src/Tank.Client/scripts/Match/MatchMain.cs
index 578fa84..b74914a 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.cs
@@ -79,13 +79,19 @@ public partial class MatchMain : Node3D
 
     public MatchMain() => _headOf = TryGetHead;
 
-    /// <summary>Hands over the session to draw; call before adding the node to the tree.</summary>
-    public void Setup(BattleSession session, LaunchOptions options)
+    /// <summary>
+    /// Hands over the session to draw; call before adding the node to the tree. <paramref name="screenshotTaken"/>: the
+    /// run's one screenshot is already taken (by Main before any match, or by an earlier match), so this one takes none.
+    /// </summary>
+    public void Setup(BattleSession session, LaunchOptions options, bool screenshotTaken)
     {
         (_session, _client, _options) = (session, session.Client, options);
-        _automation = new MatchAutomation(options);
+        _automation = new MatchAutomation(options, screenshotTaken);
     }
 
+    /// <summary>Whether the run's screenshot is spent, this match's own or an earlier one; the owner reads it as the scene goes.</summary>
+    public bool ScreenshotTaken => _automation.ScreenshotTaken;
+
     public override void _Ready()
     {
         GetWindow().Title = Title;
@@ -93,6 +99,7 @@ public partial class MatchMain : Node3D
         _input.NetPanelOpen = _options.NetGraph;
         _loading = BuildLoadingLayer();
         AddChild(_loading);
+        _automation.Start(this);   // --screenshot-at (and --quit-after) count from here, not from the process start
         _automation.ReportBadOptions();
         if (!_automation.IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
     }
@@ -116,6 +123,13 @@ public partial class MatchMain : Node3D
             Finished?.Invoke();
             return;
         }
+        StepMatch(dt);
+        _automation.EndFrame(delta, _time);   // last, and on loading frames too: the capture schedule runs on the match clock
+    }
+
+    /// <summary>The frame's match work once the client is known to be in one; early returns while the field or the first snapshot is still missing.</summary>
+    private void StepMatch(double dt)
+    {
         bool ended = _client.Match.Phase == MatchPhase.Ended;
         if (ended != _resultsShown) ShowResults(ended);
         // Taken every frame: an Enter pressed during play (the captured mouse forwards every key) must not wait for the
@@ -161,7 +175,6 @@ public partial class MatchMain : Node3D
         int resultsLeft = _resultsClock.Observe(_client.Match.Phase, dt);
         _hud.SetScoreboardOpen(_input.ScoreboardHeld && !Paused);   // Tab is polled, so the pause layer has to veto it here
         _hud.Refresh(MatchHudFrame.From(_client, _session, OwnTeam(own), _names, (int)Engine.GetFramesPerSecond(), _spectateName, _spectateFree, resultsLeft), _input.NetPanelOpen, _time);
-        _automation.EndFrame(delta);
     }
 
     /// <summary>
@@ -174,6 +187,7 @@ public partial class MatchMain : Node3D
     {
         if (own.IsDestroyed == _dead) return;
         _dead = own.IsDestroyed;
+        if (_dead) _automation.NotifyDeath(_time);   // the state beats the destroy event (R5); the plan takes the first
         (_spectateName, _spectateFree) = (null, false);
         if (!_dead)
         {
@@ -211,6 +225,7 @@ public partial class MatchMain : Node3D
     {
         _resultsShown = shown;
         if (!shown) return;
+        _automation.NotifyEnded(_time);
         _pause?.Close();
         Input.MouseMode = Input.MouseModeEnum.Visible;
         _input.ReleaseAll();
@@ -275,7 +290,11 @@ public partial class MatchMain : Node3D
     {
         TankActor tank = _tank!;
         bool fired = _shots.Update(_client.Own.ShotCount, _client.Own.Id);
-        if (fired) tank.Kick(own.TurretYaw);
+        if (fired)
+        {
+            tank.Kick(own.TurretYaw);
+            _automation.NotifyFired(_time);   // a predicted own shot never reaches the event queue
+        }
         switch (_ownWreck.Observe(own.IsDestroyed, dt, _tankFirstSight))
         {
             case WreckTracker.Change.Settle: tank.Wreck(own.TurretYaw, settled: true); break;
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
index fec8450..c3077b2 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxAutomation.cs
@@ -2,28 +2,22 @@ using Godot;
 using Tank.Client.Bootstrap;
 using Tank.Client.Cameras;
 using Tank.Sim;
-using Tank.Sim.Geometry;
 using Tank.Sim.Maps;
 using Tank.Sim.Vehicles;
-using SN = System.Numerics;
 
 namespace Tank.Client.Sandbox;
 
 /// <summary>
 /// The sandbox's scripted runs, for repeatable screenshots and frame-time logs. A script drives instead of the player
 /// with --range-demo (lay the gun on a target and fire), --autodrive (drive loops, always firing) or a fixed
-/// --camera-preset (stand still). Timed steps wait for a <see cref="Moment"/>, each noted the first time it happens:
-/// the screenshot is taken a delay after the run's start (--screenshot-at), the player's first shot (--fx-shot, or
-/// --shot-after fire), the first tank hit or kill (--shot-after hit|kill), or the first wall hurt or prop destroyed
-/// (--shot-after prop|down), each by the player (the patrolling target's shots and fences it drives into don't
-/// count), and a range demo that fires once stops after the player's first shot. With --fx-shot, nothing before a
-/// warm-up counts. Also --fps-log and --quit-after.
+/// --camera-preset (stand still); a range demo that fires once stops after the player's first shot. The timetable (the
+/// screenshot's moment and delay, the --fx-shot warm-up, --quit-after) is a <see cref="ShotPlan"/> and the screenshot,
+/// fps log and quit themselves a <see cref="CaptureRun"/>, both shared with the match.
 /// Moments and the delays after them are wall-clock seconds (the screenshot is of what is on screen); only a range
 /// demo's first shot is timed in simulation ticks (<see cref="RangeDemo.FireTick"/>), so the shot itself is repeatable.
 /// </summary>
 public sealed class SandboxAutomation
 {
-    private const double FxShotWarmup = 3;          // --fx-shot: moments before this many seconds don't count
     private const float FxShotPitch = -0.1f;        // --fx-shot: gun dipped so shells land about 40 m ahead
     private const float DemoCameraPitch = 0.1f;     // --range-demo: looking over the tank at the target
     private const float AutoCameraYaw = 0.35f;      // --autodrive: the camera looks this far off the hull's heading, rad
@@ -31,37 +25,33 @@ public sealed class SandboxAutomation
     private const double AutoTurnRate = 0.4;        // --autodrive: steers right while sin(rate × time) is above the threshold
     private const double AutoTurnThreshold = 0.5;
 
-    /// <summary>What a timed step waits for.</summary>
-    private enum Moment { Start, Fire, Hit, Kill, Prop, Down }
-
     private readonly LaunchOptions _options;
-    private readonly Node _host;
     private readonly bool _standStill;
-    private readonly double _countFrom;
-    private readonly double[] _firstSeen = NotSeenYet();   // per Moment: when it first happened, s; -1 not yet
-    private readonly Moment _shotAfter;
-    private readonly double _shotDelay;
-    private readonly FrameTimeLog _frameLog = new();
-    private bool _screenshotTaken;
+    private readonly ShotPlan _plan;
+    private readonly CaptureRun _capture;
 
     /// <param name="host">The sandbox node: its viewport is captured and measured, its tree quit.</param>
     /// <param name="standStill">A fixed camera preset is on: nobody drives.</param>
     public SandboxAutomation(LaunchOptions options, Node host, bool standStill)
     {
-        (_options, _host, _standStill) = (options, host, standStill);
+        (_options, _standStill) = (options, standStill);
         Demo = RangeDemo.Find(options.RangeDemo);
-        _countFrom = options.FxShotDelay is null ? 0 : FxShotWarmup;
-        (_shotAfter, _shotDelay) = PlanScreenshot(options);
-        if (options.FpsLog) RenderingServer.ViewportSetMeasureRenderTime(host.GetViewport().GetViewportRid(), true);
+        _plan = ShotPlan.ForSandbox(options);
+        _capture = new CaptureRun(options, host, _plan);
     }
 
     /// <summary>The scripted range demo, or null.</summary>
     public RangeDemo? Demo { get; }
 
-    /// <summary>Warns about launch options that couldn't be read or name no known preset or demo.</summary>
-    public void ReportBadOptions()
+    /// <summary>
+    /// Warns about launch options that name no known preset or demo or wait for a moment the sandbox never reaches, and —
+    /// with <paramref name="unreadOptions"/>, when the sandbox parsed the arguments itself — the ones that couldn't be read
+    /// (under Main, Main has already said those).
+    /// </summary>
+    public void ReportBadOptions(bool unreadOptions)
     {
-        foreach (string warning in _options.Warnings) GD.PushWarning(warning);
+        if (unreadOptions) foreach (string warning in _options.Warnings) GD.PushWarning(warning);
+        foreach (string warning in _plan.Warnings) GD.PushWarning(warning);
         if (_options.CameraPreset is { } preset && !CameraPresets.IsKnown(preset))
             GD.PushWarning($"Unknown --camera-preset '{preset}'; use {string.Join(", ", CameraPresets.Names)}.");
         if (_options.RangeDemo is { } demo && Demo is null)
@@ -69,26 +59,13 @@ public sealed class SandboxAutomation
     }
 
     /// <summary>Notes the first time the player fired, hit or destroyed a tank, hurt a wall and destroyed a prop.</summary>
-    public void OnEvent(in GameEvent e, int playerId, double time)
-    {
-        if (e.TankId != playerId) return;
-        Moment? moment = e.Type switch
-        {
-            GameEventType.ShellFired => Moment.Fire,
-            GameEventType.TankHit => Moment.Hit,
-            GameEventType.TankDestroyed => Moment.Kill,
-            GameEventType.PropDamaged => Moment.Prop,
-            GameEventType.PropDestroyed => Moment.Down,
-            _ => null,
-        };
-        if (moment is { } m && time >= _countFrom && _firstSeen[(int)m] < 0) _firstSeen[(int)m] = time;
-    }
+    public void OnEvent(in GameEvent e, int playerId, double time) => _plan.OnEvent(e, playerId, time);
 
     /// <summary>The player's command for the next tick: <paramref name="player"/>'s own, or a script's in its place.</summary>
     public InputCmd Command(in InputCmd player, in TankState tank, Battlefield field, double time)
     {
         InputCmd command = player;
-        if (Demo is { } demo) command = demo.Command(player, tank, field, hasFired: _firstSeen[(int)Moment.Fire] >= 0);
+        if (Demo is { } demo) command = demo.Command(player, tank, field, hasFired: _plan.Seen(ShotMoment.Fire));
         else if (_options.AutoDrive) command = AutoDrive(player, time);
         else if (_standStill) command = player with { Throttle = 0, Steer = 0, Fire = false };
         if (_options.AutoDrive && _options.FxShotDelay is not null) command = command with { AimPitch = FxShotPitch };   // shells land in view
@@ -104,49 +81,7 @@ public sealed class SandboxAutomation
     }
 
     /// <summary>Call at the end of every frame: logs render costs, takes the screenshot when due and quits when time is up.</summary>
-    public void EndFrame(double delta, double time)
-    {
-        if (_options.FpsLog) LogFrame(delta);
-        if (_options.ScreenshotPath is { } path && !_screenshotTaken && ScreenshotDue(time))
-        {
-            _screenshotTaken = true;
-            Error error = _host.GetViewport().GetTexture().GetImage().SavePng(path);
-            GD.Print(error == Error.Ok ? $"Screenshot saved: {path}" : $"Screenshot failed ({error}): {path}");
-        }
-        if (_options.QuitAfter > 0 && time >= _options.QuitAfter) _host.GetTree().Quit();
-    }
-
-    /// <summary>--shot-after waits for its event, --fx-shot for the player's first shot, otherwise --screenshot-at counts from the start.</summary>
-    private static (Moment After, double Delay) PlanScreenshot(LaunchOptions options)
-    {
-        if (options.ShotAfterEvent is { } awaited)
-            return (awaited switch
-            {
-                "fire" => Moment.Fire,
-                "hit" => Moment.Hit,
-                "kill" => Moment.Kill,
-                "prop" => Moment.Prop,
-                "down" => Moment.Down,
-                _ => throw new InvalidOperationException($"--shot-after '{awaited}' got past LaunchOptions."),
-            }, options.ShotAfterDelay);
-        if (options.FxShotDelay is { } fxDelay) return (Moment.Fire, fxDelay);
-        return (Moment.Start, options.ScreenshotAt);
-    }
-
-    private bool ScreenshotDue(double time)
-    {
-        double since = _firstSeen[(int)_shotAfter];
-        return since >= 0 && time >= since + _shotDelay;
-    }
-
-    /// <summary>Every moment unseen (-1), but the start, which is at 0.</summary>
-    private static double[] NotSeenYet()
-    {
-        var seen = new double[Enum.GetValues<Moment>().Length];
-        Array.Fill(seen, -1);
-        seen[(int)Moment.Start] = 0;
-        return seen;
-    }
+    public void EndFrame(double delta, double time) => _capture.EndFrame(delta, time);
 
     /// <summary>--autodrive: full throttle, steering right part of the time (so it drives loops), always firing.</summary>
     private static InputCmd AutoDrive(in InputCmd player, double time) => player with
@@ -155,15 +90,4 @@ public sealed class SandboxAutomation
         Steer = (sbyte)(Math.Sin(time * AutoTurnRate) > AutoTurnThreshold ? 1 : 0),
         Fire = true,
     };
-
-    private void LogFrame(double delta)
-    {
-        Rid rid = _host.GetViewport().GetViewportRid();
-        string? line = _frameLog.Add(delta,
-            RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
-            Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
-            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
-            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
-        if (line is not null) GD.Print(line);
-    }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Sandbox/SandboxMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
index ca354a4..10f3efb 100644
--- a/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
+++ b/src/Tank.Client/scripts/Sandbox/SandboxMain.cs
@@ -41,6 +41,7 @@ public partial class SandboxMain : Node3D
     public event Action? ExitRequested;
 
     private InputCmd[] _commands = Array.Empty<InputCmd>();
+    private LaunchOptions? _given;   // Main's options (already reported); null when the sandbox is the scene run on its own
     private LaunchOptions _options = new();
     private SandboxAutomation _automation = null!;
     private Simulation _sim = null!;
@@ -61,9 +62,12 @@ public partial class SandboxMain : Node3D
     private float _aimYaw;
     private float _aimPitch;
 
+    /// <summary>Hands over Main's launch options, whose unreadable values Main has already reported; call before adding the node to the tree.</summary>
+    public void Setup(LaunchOptions options) => _given = options;
+
     public override void _Ready()
     {
-        _options = LaunchOptions.Parse(OS.GetCmdlineUserArgs());
+        _options = _given ?? LaunchOptions.Parse(OS.GetCmdlineUserArgs());
         GetWindow().Title = "坦克 3D · 试驾场";
         SandboxInput.RegisterActions();
         _automation = new SandboxAutomation(_options, this, standStill: HasPreset);
@@ -92,7 +96,7 @@ public partial class SandboxMain : Node3D
         _hud = Add(new SandboxHud());
         _hud.Build(_camera, _targets, _sim.Field, _vision, _current.Id, _options.SpotDebug);
 
-        _automation.ReportBadOptions();
+        _automation.ReportBadOptions(unreadOptions: _given is null);
         if (!_options.AutoDrive && !IgnoresInput) Input.MouseMode = Input.MouseModeEnum.Captured;
     }
 
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 859、Net 210。

- [ ] **Step 5：确定性冒烟与截图**

```bash
mkdir -p build/shots/t9
for i in 1 2; do
  (cd $W && godot-mono --path src/Tank.Client -- --no-update --loopback --host --bots 3 --autostart --seed 7 --autodrive --screenshot $W/build/shots/t9/smoke$i.png --screenshot-at 20 --quit-after 40 > build/shots/t9/smoke$i.log 2>&1) & P=$!; (sleep 60; kill -9 $P 2>/dev/null) & wait $P
done
diff <(grep -vE "Screenshot|Godot Engine|Metal" build/shots/t9/smoke1.log) <(grep -vE "Screenshot|Godot Engine|Metal" build/shots/t9/smoke2.log) && echo SAME
python3 $S/pngdiff.py build/shots/t9/smoke1.png build/shots/t9/smoke2.png
$S/godot_run.sh $W "" 260 -- --no-update --loopback --host --mode ffa --bots 3 --autostart --seed 7 --autodrive --screenshot $W/build/shots/t9/ffa.png --screenshot-at 157 --quit-after 240
$S/godot_run.sh $W "" 120 -- --no-update --loopback --host --mode annihilation --bots 1 --match-seconds 20 --autostart --seed 7 --shot-after ended:3 --screenshot $W/build/shots/t9/results.png --quit-after 90
$S/godot_run.sh $W res://scenes/Sandbox.tscn 40 -- --autodrive --fx-shot 1 --screenshot $W/build/shots/t9/fx-shot.png --quit-after 16
$S/baseline4b.sh $W build/shots/t9-sandbox && python3 $S/pngdiffdirs.py build/shots/base build/shots/t9-sandbox
```

Expected：
- 两次冒烟的日志（去掉截图行和引擎横幅）逐行相同，打印 `SAME`；`smoke1.png` 和 `smoke2.png` 里车辆位置和 HUD 一致（像素差异约一成，全在云、草、尘土这些逐帧变化的地方），计时约 6:40（420 − 20，证明 `--screenshot-at` 从对局开始计）。
- `ffa.png`：seed 7 的各自为战，自己已阵亡、观战镜头下能看到红色敌车（生前没有敌人入镜，所以取 157 秒）。
- `results.png`：歼灭模式 20 秒后的结算页。
- `fx-shot.png`：沙盒自动驾驶开炮的特效。
- `t9-sandbox/` 六张与 `base/` 一致（差异只在云、地平线、粒子）。
- 每次运行的日志里没有 `ShotPlan` 的“到不了 / 写错”警告。

- [ ] **Step 6：提交**

```bash
git add -A src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): launch options completed and the capture automation shared — ShotPlan as pure logic and CaptureRun for both scenes; the match counts --screenshot-at from its own start on the match clock and takes --camera-preset, --autodrive, --fps-log, --sniper, --netgraph, --shot-after and --quit-after"
```

---

### Task 10：性能与打磨

蓝图 §5 T10。`TankActors.Prewarm`：第一份快照到达时按名单一次建好除自己以外的全部车（之后只有团队对战中途加入的新 id 才懒建；预建的车第一次被看到时仍按首见处理残骸和转向速率；血条顺序是建立顺序）。`BattleFx` 池 4/6/12 → 10/10/16（炮口 / 爆炸 / 碎块：10 辆车可能在一次炮口火光的寿命内全部开火）。断线重连：纯逻辑 `Online/Logic/Reconnector`（`Timeout/ConnectionFailed/HostUnreachable/NetworkUnreachable` 重试 3 次、间隔 2 秒，房主会话也一样——房主进程停顿超过 5 秒时连自己服务器的本地连接会超时，而服务线程和端口都还在，按房主令牌就能拿回座位和房间；对方关闭、主动断开、被拒绝、协议不符、从没拿到座位都立即回菜单）+ `BattleSession.Reconnect()`（同一 `SessionToken` 新建 `BattleClient`、释放旧的）+ `Main.OnDisconnected`（等待期间退出对局场景，菜单状态栏“连接断开，正在重连…（第 n/3 次）”；回到房间或对局时次数清零；三次都失败写“重连失败：原因”）。`--fps-log` 每行末尾加 `alloc X KB/s`（进程的 `GC.GetTotalAllocatedBytes`）。F3 的“服务器 tick 平均 · 最大”改由 `ThreadedHost` 里的 `TickProbe` 包住服务器自己量：开局建地图那一 tick（约 50 ms）不计，并在那里把最大值清零，所以读数是本局的稳态。窗口失焦释放鼠标 + `ReleaseAll` 在 Task 8 里已经有了。

**Files:**
- Create: `src/Tank.Client/scripts/Online/Logic/{Reconnector,TickProbe}.cs`
- Modify: `Bootstrap/{CaptureRun,Main,Main.Flow}.cs`、`Combat/BattleFx.cs`、`Match/{MatchMain,TankActors}.cs`、`Online/Logic/{BattleSession,ThreadedHost}.cs`、`Sandbox/FrameTimeLog.cs`
- Test: 新建 `tests/Tank.Art.Tests/{FrameTimeLogTests,ReconnectSessionTests,ReconnectorTests,TickProbeTests}.cs`；修改 `Tank.Art.Tests.csproj`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Art.Tests/FrameTimeLogTests.cs`：

```csharp
using Tank.Client.Sandbox;

namespace Tank.Art.Tests;

public class FrameTimeLogTests
{
    private const double Frame = 0.25;   // four frames fill a window

    private static string? AddSecond(FrameTimeLog log, long allocAtStart, long allocPerFrame)
    {
        string? line = null;
        for (int i = 0; i < 4; i++)
            line = log.Add(Frame, cpuMs: 2, gpuMs: 3, processMs: 4, drawCalls: 100, primitives: 50_000, allocAtStart + (i + 1) * allocPerFrame);
        return line;
    }

    [Fact]
    public void The_startup_window_is_dropped_and_the_second_reports()
    {
        var log = new FrameTimeLog();

        Assert.Null(AddSecond(log, 0, 1024));
        string? line = AddSecond(log, 4 * 1024, 1024);

        Assert.NotNull(line);
        Assert.Contains("4 fps", line);
        Assert.Contains("100 draws", line);
        Assert.Contains("50k prims", line);
    }

    [Fact]
    public void The_allocation_rate_is_the_windows_delta_per_second()
    {
        var log = new FrameTimeLog();
        AddSecond(log, 0, 0);

        // 2048 B over 4 frames of 0.25 s = 8192 B/s = 8.0 KB/s; the window starts at the first frame's figure.
        string? line = AddSecond(log, 0, 2048);

        Assert.NotNull(line);
        Assert.Contains("alloc 6.0 KB/s", line);   // deltas are measured from the window's first sample: 3 × 2048 B / 1 s
    }

    [Fact]
    public void Without_the_allocation_figure_the_field_stays_out()
    {
        var log = new FrameTimeLog();
        for (int i = 0; i < 4; i++) log.Add(Frame, 2, 3, 4, 100, 50_000);
        string? line = null;
        for (int i = 0; i < 4; i++) line = log.Add(Frame, 2, 3, 4, 100, 50_000);

        Assert.NotNull(line);
        Assert.DoesNotContain("alloc", line);
    }
}
```

新建 `tests/Tank.Art.Tests/ReconnectSessionTests.cs`：

```csharp
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Tank.Client.Online;
using Tank.Net.Battle;
using Tank.Sim.Modes;
using Xunit.Abstractions;

namespace Tank.Art.Tests;

/// <summary>
/// The reconnect drill (blueprint §5 T10), over real UDP on 127.0.0.1: a guest joins a threaded host, the match starts,
/// a client's connection is torn down, and <see cref="BattleSession.Reconnect"/> — a new client with the same session
/// token — gets the same seat back mid-match (4A's token reconnect and <c>TakeOver</c>): the guest's, and the host's own
/// client, whose server keeps running on its thread meanwhile. The server's log goes to the test output, so a detailed
/// run is the drill's record. Where the sandbox forbids binding, the tests do nothing, like <see cref="ThreadedHostTests"/>.
/// </summary>
public class ReconnectSessionTests
{
    private readonly ITestOutputHelper _output;
    private readonly List<string> _serverLog = new();   // written from the server thread: lock it

    public ReconnectSessionTests(ITestOutputHelper output) => _output = output;

    private void ServerLog(string line)
    {
        lock (_serverLog) _serverLog.Add(line);
        _output.WriteLine($"[server] {line}");
    }

    private static int FreeUdpPort()
    {
        using var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)probe.Client.LocalEndPoint!).Port;
    }

    private BattleSession? TryHost(int port)
    {
        try
        {
            return BattleSession.Host(new HostSettings("Host", "Host's room", port, BattleMode.FreeForAll, Bots: 0, Seed: 7, MatchSeconds: null), ServerLog);
        }
        catch (Exception ex) when (ex is InvalidOperationException or SocketException)
        {
            return null;   // no UDP here: nothing to test
        }
    }

    /// <summary>Pumps both sessions on the wall clock until <paramref name="done"/> or <paramref name="seconds"/> run out.</summary>
    private static bool Pump(BattleSession host, AutoRoom hostAuto, BattleSession guest, double seconds, Func<bool> done)
    {
        var clock = Stopwatch.StartNew();
        double last = 0;
        while (clock.Elapsed.TotalSeconds < seconds)
        {
            if (done()) return true;
            double now = clock.Elapsed.TotalSeconds;
            double dt = now - last;
            last = now;
            host.Advance(dt);
            hostAuto.Next(host.Client.Room, host.Client.Slot, host.Client.IsHost, humansWanted: 2).SendTo(host.Client);
            host.Client.Update(dt, LocalCommand.None);
            guest.Advance(dt);
            guest.Client.Update(dt, LocalCommand.None);
            Thread.Sleep(5);
        }
        return done();
    }

    [Fact]
    public void A_guest_reconnecting_mid_match_with_its_token_gets_the_same_seat_back()
    {
        int port = FreeUdpPort();
        using BattleSession? host = TryHost(port);
        if (host is null) return;
        var hostAuto = new AutoRoom(null, BattleMode.FreeForAll, bots: null, autoStart: true);
        using BattleSession guest = BattleSession.Join("127.0.0.1", port, "Guest");

        Assert.True(Pump(host, hostAuto, guest, 10, () => guest.Client.Phase == BattlePhase.InMatch && guest.Client.HasTank),
            $"the match never started: host {host.Client.Phase}, guest {guest.Client.Phase}");
        byte slot = guest.Client.Slot;
        uint token = guest.Client.SessionToken;
        Assert.NotEqual(0u, token);
        _output.WriteLine($"[guest] in the match as slot {slot}, tank {guest.Client.OwnTankId}, token {token:x8}; reconnecting");

        guest.Reconnect();   // a fresh client and connection, the same token: the server's TakeOver path

        Assert.True(Pump(host, hostAuto, guest, 10, () => guest.Client.Phase == BattlePhase.InMatch && guest.Client.HasTank),
            $"the guest never made it back: {guest.Client.Phase} ({guest.Client.DisconnectReason})");
        _output.WriteLine($"[guest] back in the match as slot {guest.Client.Slot}, tank {guest.Client.OwnTankId}, token {guest.Client.SessionToken:x8}");
        Assert.Equal(slot, guest.Client.Slot);
        Assert.Equal(token, guest.Client.SessionToken);
        // Which line the server writes depends on whether the old connection's goodbye beats the new hello: a take-over
        // ("reconnecting over peer") or a seat found disconnected ("reconnected"). Either way it found the seat by token.
        lock (_serverLog) Assert.Contains(_serverLog, l => l.StartsWith($"slot {slot} ") && l.Contains("reconnect"));
    }

    [Fact]
    public void A_host_reconnecting_its_own_client_mid_match_keeps_its_seat_and_the_room()
    {
        int port = FreeUdpPort();
        using BattleSession? host = TryHost(port);
        if (host is null) return;
        var hostAuto = new AutoRoom(null, BattleMode.FreeForAll, bots: null, autoStart: true);
        using BattleSession guest = BattleSession.Join("127.0.0.1", port, "Guest");

        Assert.True(Pump(host, hostAuto, guest, 10, () => host.Client.Phase == BattlePhase.InMatch && host.Client.HasTank && guest.Client.HasTank),
            $"the match never started: host {host.Client.Phase}, guest {guest.Client.Phase}");
        byte slot = host.Client.Slot;
        uint token = host.Client.SessionToken;
        Assert.True(host.Client.IsHost);
        _output.WriteLine($"[host] in the match as slot {slot}, tank {host.Client.OwnTankId}, token {token:x8}; reconnecting its own client");

        host.Reconnect();   // what Main does after the host's own link timed out: the server thread never stopped

        Assert.True(Pump(host, hostAuto, guest, 10, () => host.Client.Phase == BattlePhase.InMatch && host.Client.HasTank && host.Client.IsHost),
            $"the host never made it back: {host.Client.Phase} ({host.Client.DisconnectReason})");
        _output.WriteLine($"[host] back in the match as slot {host.Client.Slot}, tank {host.Client.OwnTankId}, host {host.Client.IsHost}");
        Assert.True(host.IsHosting);
        Assert.Equal(slot, host.Client.Slot);
        Assert.Equal(token, host.Client.SessionToken);
        Assert.Equal(BattlePhase.InMatch, guest.Client.Phase);   // the room never closed under the guest
        lock (_serverLog) Assert.Contains(_serverLog, l => l.StartsWith($"slot {slot} ") && l.Contains("reconnect"));
    }
}
```

新建 `tests/Tank.Art.Tests/ReconnectorTests.cs`：

```csharp
using Tank.Client.Online;

namespace Tank.Art.Tests;

public class ReconnectorTests
{
    private const string Dropped = "Timeout";

    [Fact]
    public void A_timeout_of_a_seated_guest_schedules_an_attempt_two_seconds_later()
    {
        var r = new Reconnector();

        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, hadSeat: true, now: 10));
        Assert.Equal(1, r.Attempt);
        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, true, 11.9));
        Assert.Equal(Reconnector.Step.Connect, r.Observe(Dropped, true, 12));
    }

    [Fact]
    public void Three_failed_attempts_give_up_on_the_fourth_drop()
    {
        var r = new Reconnector();
        double now = 0;
        for (int attempt = 1; attempt <= Reconnector.MaxAttempts; attempt++)
        {
            Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, true, now));
            Assert.Equal(attempt, r.Attempt);
            now += Reconnector.RetrySeconds;
            Assert.Equal(Reconnector.Step.Connect, r.Observe(Dropped, true, now));
        }

        Assert.Equal(Reconnector.Step.GiveUp, r.Observe(Dropped, true, now));
        Assert.Equal(Reconnector.MaxAttempts, r.Attempt);
    }

    [Fact]
    public void Making_it_back_resets_the_attempts()
    {
        var r = new Reconnector();
        r.Observe(Dropped, true, 0);
        r.Observe(Dropped, true, 2);   // Connect

        r.OnConnected();

        Assert.Equal(0, r.Attempt);
        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, true, 30));
        Assert.Equal(1, r.Attempt);
    }

    [Fact]
    public void A_host_retries_like_a_guest()
    {
        // There is no hosting exception to pass: a host whose process stalled past the timeout lost only its own link,
        // the server thread kept running, and the owner token takes the seat back — the same schedule as anyone's.
        var r = new Reconnector();
        Assert.Equal(Reconnector.Step.Wait, r.Observe(Dropped, hadSeat: true, now: 0));
        Assert.Equal(1, r.Attempt);
        Assert.Equal(Reconnector.Step.Connect, r.Observe(Dropped, hadSeat: true, now: Reconnector.RetrySeconds));
    }

    [Fact]
    public void A_client_that_never_got_a_seat_is_not_retried()
    {
        var r = new Reconnector();
        Assert.Equal(Reconnector.Step.GiveUp, r.Observe("ConnectionFailed", hadSeat: false, now: 0));
    }

    [Theory]
    [InlineData("RemoteConnectionClose")]   // the host closed the room
    [InlineData("DisconnectPeerCalled")]    // we left
    [InlineData("ConnectionRejected")]
    [InlineData("InvalidProtocol")]
    [InlineData("房间已满。")]               // a server reject reason passes through as text
    [InlineData(null)]
    public void Deliberate_closes_and_rejections_are_not_retried(string? reason)
    {
        var r = new Reconnector();
        Assert.Equal(Reconnector.Step.GiveUp, r.Observe(reason, hadSeat: true, now: 0));
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("ConnectionFailed")]   // a retry's own connect failing must count against the same budget
    [InlineData("HostUnreachable")]
    [InlineData("NetworkUnreachable")]
    public void Accidental_drops_are_retried(string reason) => Assert.True(Reconnector.IsRetryable(reason));
}
```

修改 `tests/Tank.Art.Tests/Tank.Art.Tests.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
index 8a82a23..da39ee6 100644
--- a/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
+++ b/tests/Tank.Art.Tests/Tank.Art.Tests.csproj
@@ -18,8 +18,8 @@
     <Compile Include="..\..\src\Tank.Client\scripts\Art\Palette.cs" LinkBase="Art" />
     <!-- So are the launch options (they only need Tank.Sim and Tank.Net for the mode names and limits). -->
     <Compile Include="..\..\src\Tank.Client\scripts\Bootstrap\LaunchOptions.cs" LinkBase="Bootstrap" />
-    <!-- So are the scripted range demos and the tick interpolation (only sim maths). -->
-    <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs;..\..\src\Tank.Client\scripts\Sandbox\TankInterpolation.cs" LinkBase="Sandbox" />
+    <!-- So are the scripted range demos, the tick interpolation (only sim maths) and the fps log's text. -->
+    <Compile Include="..\..\src\Tank.Client\scripts\Sandbox\RangeDemo.cs;..\..\src\Tank.Client\scripts\Sandbox\TankInterpolation.cs;..\..\src\Tank.Client\scripts\Sandbox\FrameTimeLog.cs" LinkBase="Sandbox" />
     <!-- Popup timing curves and the spotting debug text are engine-neutral too. -->
     <Compile Include="..\..\src\Tank.Client\scripts\Overlay\PopupCurves.cs;..\..\src\Tank.Client\scripts\Overlay\SpotText.cs" LinkBase="Overlay" />
     <!-- Tank look data, part builders and animation state are engine-neutral too. -->
PATCH
```

新建 `tests/Tank.Art.Tests/TickProbeTests.cs`：

```csharp
using Tank.Client.Online;
using Tank.Net.Server;

namespace Tank.Art.Tests;

public class TickProbeTests
{
    private const int SlowMs = 40;   // a tick that sleeps this long takes at least as long

    /// <summary>A server whose next tick can be made slow, and whose match can be started inside a tick (as a StartMatch command does).</summary>
    private sealed class FakeServer : ITickServer
    {
        public bool MatchRunning;
        public bool SlowNext;
        public bool StartsMatchNext;

        public void Tick()
        {
            if (SlowNext) Thread.Sleep(SlowMs);
            if (StartsMatchNext) MatchRunning = true;
            SlowNext = StartsMatchNext = false;
        }
    }

    private static (TickProbe Probe, FakeServer Server) Make()
    {
        var server = new FakeServer();
        return (new TickProbe(server, () => server.MatchRunning), server);
    }

    [Fact]
    public void A_slow_tick_during_play_shows_as_the_worst()
    {
        var (probe, server) = Make();
        server.MatchRunning = true;
        for (int i = 0; i < 5; i++) probe.Tick();
        server.SlowNext = true;
        probe.Tick();
        Assert.True(probe.MaxMs >= SlowMs, $"max {probe.MaxMs} ms");
        Assert.True(probe.AverageMs > 0);
    }

    [Fact]
    public void The_tick_a_match_starts_in_is_left_out_and_starts_the_worst_over()
    {
        var (probe, server) = Make();
        server.SlowNext = true;   // something slow in the room, before any match
        probe.Tick();
        Assert.True(probe.MaxMs >= SlowMs);
        double average = probe.AverageMs;

        server.SlowNext = server.StartsMatchNext = true;   // the map build
        probe.Tick();
        Assert.Equal(0, probe.MaxMs);
        Assert.Equal(average, probe.AverageMs);   // not in the average either

        for (int i = 0; i < 5; i++) probe.Tick();   // ordinary match ticks: nowhere near the build's cost
        Assert.True(probe.MaxMs < SlowMs, $"max {probe.MaxMs} ms");
    }

    [Fact]
    public void A_second_match_starts_the_worst_over_again()
    {
        var (probe, server) = Make();
        server.MatchRunning = true;
        server.SlowNext = true;
        probe.Tick();   // a spike in the first match
        server.MatchRunning = false;   // back in the room
        probe.Tick();
        server.StartsMatchNext = true;
        probe.Tick();
        Assert.Equal(0, probe.MaxMs);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Art.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -8`
Expected: 编译失败，`TickProbe` 找不到（它出现在测试的声明里，编译器先只报这一处；`Reconnector`、`FrameTimeLog` 的新成员在它之后）。

- [ ] **Step 3：实现**

修改 `src/Tank.Client/scripts/Bootstrap/CaptureRun.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/CaptureRun.cs b/src/Tank.Client/scripts/Bootstrap/CaptureRun.cs
index 9c1bd04..1b3e0da 100644
--- a/src/Tank.Client/scripts/Bootstrap/CaptureRun.cs
+++ b/src/Tank.Client/scripts/Bootstrap/CaptureRun.cs
@@ -44,7 +44,8 @@ public sealed class CaptureRun
             RenderingServer.ViewportGetMeasuredRenderTimeCpu(rid), RenderingServer.ViewportGetMeasuredRenderTimeGpu(rid),
             Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0,
             (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
-            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame));
+            (long)RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame),
+            GC.GetTotalAllocatedBytes());   // the whole process's managed churn: the zero-allocation audit reads this
         if (line is not null) GD.Print(line);
     }
 }
PATCH
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.Flow.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
index 7403a3c..b92f259 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.Flow.cs
@@ -69,6 +69,7 @@ public partial class Main
     private void StartSession(BattleSession session, string status)
     {
         _session = session;
+        _reconnect = new Reconnector();
         // --bots is only a request when given; 0 is also the default, and the host's own settings already carry it.
         _auto = new AutoRoom(_options.Tank, _options.Mode, _options.Bots > 0 ? _options.Bots : null, _options.AutoStart);
         _room.Bind(session.Client, LocalAddresses(session));
@@ -81,6 +82,7 @@ public partial class Main
         if (_matchView is not null) LeaveMatch();
         _session?.Dispose();
         _session = null;
+        _reconnect = null;
         _auto = null;
         _room.Visible = false;
         _menu.Visible = true;
@@ -125,6 +127,43 @@ public partial class Main
         _auto?.Reset();
     }
 
+    /// <summary>
+    /// The connection died (blueprint §5 T10). An unexpected drop of a seated client is retried with the same token, up
+    /// to three times two seconds apart: the match scene has already left (its phase check fired), the menu shows
+    /// 正在重连…, and the server's grace window hands the same slot back — mid-match it puts the client straight back in.
+    /// The host's own client too: its server thread has kept running (a stalled host process timed out only its own link),
+    /// and the owner token takes the host's seat and rights back. A deliberate close and a rejection go out at once, as
+    /// before, with the described reason.
+    /// </summary>
+    private void OnDisconnected(BattleSession session, BattleClient client)
+    {
+        string reason = client.DisconnectReason is { } r ? DescribeDisconnect(r) : "连接已断开。";
+        switch (_reconnect?.Observe(client.DisconnectReason, hadSeat: client.SessionToken != 0, _time) ?? Reconnector.Step.GiveUp)
+        {
+            case Reconnector.Step.Wait:
+                if (_matchView is not null) LeaveMatch();
+                _room.Visible = false;
+                _menu.Visible = true;
+                _menu.SetStatus($"连接断开，正在重连…（第 {_reconnect!.Attempt}/{Reconnector.MaxAttempts} 次）");
+                break;
+            case Reconnector.Step.Connect:
+                try
+                {
+                    session.Reconnect();
+                    _room.Bind(session.Client, LocalAddresses(session));
+                }
+                catch (Exception ex) when (ex is InvalidOperationException or SocketException or ArgumentException)   // as Join's
+                {
+                    GD.Print($"[reconnect] attempt {_reconnect!.Attempt} could not start a connection: {ex.Message}");
+                    // the client stays disconnected: the next frame schedules another attempt, or gives up
+                }
+                break;
+            case Reconnector.Step.GiveUp:
+                LeaveSession(_reconnect is { Attempt: > 0 } ? $"重连失败：{reason}" : $"连接已断开：{reason}", error: true);
+                break;
+        }
+    }
+
     /// <summary><c>--hud-demo NAME</c>: just the HUD gallery's screen; an unknown name lists the known ones and exits with code 2.</summary>
     private void EnterGallery(string name)
     {
PATCH
```

修改 `src/Tank.Client/scripts/Bootstrap/Main.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Bootstrap/Main.cs b/src/Tank.Client/scripts/Bootstrap/Main.cs
index 4972d69..6e4b8ff 100644
--- a/src/Tank.Client/scripts/Bootstrap/Main.cs
+++ b/src/Tank.Client/scripts/Bootstrap/Main.cs
@@ -23,6 +23,7 @@ public partial class Main : Node
     private LanBrowser? _lan;
     private BattleSession? _session;
     private AutoRoom? _auto;
+    private Reconnector? _reconnect;   // one per session: what to do when its connection dies
     private SandboxMain? _sandbox;
     private MatchMain? _matchView;
     private double _time;
@@ -101,14 +102,16 @@ public partial class Main : Node
             case BattlePhase.Connecting:
                 break;
             case BattlePhase.Room:
+                _reconnect?.OnConnected();
                 if (_matchView is not null) LeaveMatch();
                 ShowRoom(client);
                 break;
             case BattlePhase.InMatch:
+                _reconnect?.OnConnected();
                 if (_matchView is null) EnterMatch(client);   // the scene shows "loading" until the first snapshot brings the own tank
                 break;
             case BattlePhase.Disconnected:
-                LeaveSession(client.DisconnectReason is { } reason ? $"连接已断开：{DescribeDisconnect(reason)}" : "连接已断开。", error: true);
+                OnDisconnected(session, client);
                 break;
         }
     }
PATCH
```

修改 `src/Tank.Client/scripts/Combat/BattleFx.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Combat/BattleFx.cs b/src/Tank.Client/scripts/Combat/BattleFx.cs
index bab6910..4393b30 100644
--- a/src/Tank.Client/scripts/Combat/BattleFx.cs
+++ b/src/Tank.Client/scripts/Combat/BattleFx.cs
@@ -14,17 +14,17 @@ public enum PuffColor { White, Grey }
 /// smoke, or grey dust off rock and armour) and 8 small stones thrown up under gravity.
 /// Debris (<see cref="Debris"/>): chunks thrown up and along a push (bricks, stones, wood chips, leaves), with an
 /// optional cloud of dust puffs; sized for a chip off a wall up to a whole wall coming down.
-/// Pool sizes (4 muzzle, 6 impact, 12 debris slots) are an estimate: highest fire rate (one shell per reload, ~4 s, plus
-/// a few other tanks) times the longest particle life (1.6 s) stays well under the slot count; debris comes in bursts,
-/// up to a dozen at once when an HE shell lands in a bush patch. If every slot is busy anyway, the one that started
-/// playing longest ago is reused (idle slots always win, being the oldest). Nothing allocates per shot: no tweens, no closures; each slot
-/// advances its own flash animation from <see cref="_Process"/>.
+/// Pool sizes (10 muzzle, 10 impact, 16 debris slots) are sized for a full match (blueprint §5 T10): ten tanks can all
+/// fire inside one muzzle life (~0.8 s), so there is a muzzle and an impact slot per possible shooter; debris comes in
+/// bursts, up to a dozen at once when an HE shell lands in a bush patch, with headroom for two overlapping. If every
+/// slot is busy anyway, the one that started playing longest ago is reused (idle slots always win, being the oldest).
+/// Nothing allocates per shot: no tweens, no closures; each slot advances its own flash animation from <see cref="_Process"/>.
 /// </summary>
 public partial class BattleFx : Node3D
 {
-    private const int MuzzleSlots = 4;
-    private const int ImpactSlots = 6;
-    private const int DebrisSlots = 12;
+    private const int MuzzleSlots = 10;   // one per tank of a full match: all ten can fire inside one muzzle life
+    private const int ImpactSlots = 10;   // likewise: their shells can all burst inside one impact life (~1.2 s)
+    private const int DebrisSlots = 16;
     private const float MuzzleFlashTime = 0.08f;
     private const float MuzzleFlashSize = 1.3f;
     private const float FireballGrowTime = 0.1f;
PATCH
```

修改 `src/Tank.Client/scripts/Match/MatchMain.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/MatchMain.cs b/src/Tank.Client/scripts/Match/MatchMain.cs
index b74914a..193af73 100644
--- a/src/Tank.Client/scripts/Match/MatchMain.cs
+++ b/src/Tank.Client/scripts/Match/MatchMain.cs
@@ -65,6 +65,7 @@ public partial class MatchMain : Node3D
     private float _aimYaw;
     private float _aimPitch;
     private bool _finished;
+    private bool _prewarmed;       // the roster's actors were built from the first snapshot
     private bool _resultsShown;    // the match has ended and the results screen is up
     private bool _dead;            // the own tank is a wreck: commands are empty and the spectate chain has the camera
     private SN.Vector2 _focus;     // where the spectating camera looks (the free camera moves it)
@@ -146,6 +147,14 @@ public partial class MatchMain : Node3D
             _loading.Visible = true;   // the match start is here, the first snapshot (and so the own tank's spec) not yet (R3)
             return;
         }
+        if (!_prewarmed && _client.Latest is { } roster)
+        {
+            // The first snapshot names the whole roster: every tank's model is built now, before the loading shade comes
+            // off (the long frame is spent with the shade still on screen), so nobody's first sight costs a hitch
+            // mid-fight (a later joiner still builds lazily).
+            _prewarmed = true;
+            _actors.Prewarm(roster.Players, _client.OwnTankId, Mode, OwnTeam(_client.Own));
+        }
         _loading.Visible = false;
         _hud.Visible = true;
         RefreshNames();
PATCH
```

修改 `src/Tank.Client/scripts/Match/TankActors.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Match/TankActors.cs b/src/Tank.Client/scripts/Match/TankActors.cs
index c3a49f0..4613bd7 100644
--- a/src/Tank.Client/scripts/Match/TankActors.cs
+++ b/src/Tank.Client/scripts/Match/TankActors.cs
@@ -13,8 +13,10 @@ using Tank.Sim.Vehicles;
 namespace Tank.Client.Match;
 
 /// <summary>
-/// The other tanks of a match, one <see cref="TankActor"/> per tank id, built the first time the tank is seen (its spec
-/// and team from its entity, painted by <see cref="MatchColors"/>). Each frame <see cref="Sync"/> poses the tanks in
+/// The other tanks of a match, one <see cref="TankActor"/> per tank id. <see cref="Prewarm"/> builds the whole roster's
+/// models the frame the first snapshot names it (one long frame before play, instead of a hitch per tank coming into
+/// sight); a tank joining later (team battle) is still built the first time it is seen (spec and team from its entity,
+/// painted by <see cref="MatchColors"/>). Entries are never removed: each frame <see cref="Sync"/> poses the tanks in
 /// <c>BattleClient.Others</c> and hides the rest at once (not in sight is not drawn; no fade). A kill is the event's
 /// to show, the snapshot's flag the fallback (<see cref="WreckTracker"/>); a tank first seen — or seen again after
 /// being out of sight — as a wreck settles without a blast, and one revived out of sight comes back intact at once. The
@@ -36,10 +38,11 @@ public partial class TankActors : Node3D
         public TankPose Pose;
         public float PrevYaw;
         public int SeenStamp;   // the Sync that last had this tank in sight
+        public bool Seen;       // ever in sight: a prewarmed tank's first sight still counts as one (wreck settle, yaw rate)
     }
 
     private readonly Dictionary<int, Entry> _entries = new();
-    private readonly List<Entry> _order = new();   // in order of first sight: the order the HUD bars go out in
+    private readonly List<Entry> _order = new();   // in the order built (the roster by slot, then late joiners): the order the HUD bars go out in
     private HeightField _terrain = null!;
     private BattleFx _fx = null!;
     private int _stamp;
@@ -50,7 +53,23 @@ public partial class TankActors : Node3D
     public void SetTerrain(HeightField terrain) => _terrain = terrain;
 
     /// <summary>
-    /// Poses every tank in <paramref name="others"/> (building an actor for one never seen before) and hides the ones
+    /// Builds an actor for every listed tank ahead of its first sight (blueprint §5 T10): the first snapshot carries the
+    /// whole roster in <paramref name="players"/> (by room slot), so the models — the expensive part — are built in one
+    /// frame, under the loading shade, instead of one hitch each as tanks come into view. The actors stay hidden until
+    /// <see cref="Sync"/> first sees them; a tank already built, the own one, and an empty slot are passed over.
+    /// </summary>
+    public void Prewarm(ReadOnlySpan<PlayerStats> players, int ownTankId, BattleMode mode, byte ownTeam)
+    {
+        for (int slot = 0; slot < players.Length; slot++)
+        {
+            PlayerStats p = players[slot];
+            if (!p.Active || p.TankId == 0 || p.TankId == ownTankId || _entries.ContainsKey(p.TankId)) continue;
+            Create(p.TankId, TankCatalog.All[p.Spec], p.Team, (byte)slot, mode, ownTeam);
+        }
+    }
+
+    /// <summary>
+    /// Poses every tank in <paramref name="others"/> (building an actor for one never known before) and hides the ones
     /// not there this frame. <paramref name="ownTeam"/> and <paramref name="mode"/> decide a new tank's colours.
     /// </summary>
     public void Sync(IReadOnlyList<RemoteTank> others, BattleMode mode, byte ownTeam, float dt)
@@ -60,11 +79,9 @@ public partial class TankActors : Node3D
         {
             RemoteTank remote = others[i];
             TankState state = RenderMath.FromRemote(remote);
-            bool isNew = !_entries.TryGetValue(state.Id, out Entry? entry);
-            if (isNew) entry = Create(state, remote.Entity.Slot, mode, ownTeam);
-            // Not drawn last frame (never seen, or out of sight since): whatever happened meanwhile — a kill, a revival, a
-            // turn — is the state's to show at once, without the event wait or a yaw rate from a stale heading.
-            Show(entry!, state, dt, firstSight: isNew || !entry!.Actor.Visible);
+            if (!_entries.TryGetValue(state.Id, out Entry? entry))
+                entry = Create(state.Id, state.Spec, state.Team, remote.Entity.Slot, mode, ownTeam);
+            Show(entry, state, dt);
         }
         for (int i = 0; i < _order.Count; i++)
         {
@@ -73,20 +90,25 @@ public partial class TankActors : Node3D
         }
     }
 
-    private Entry Create(in TankState state, byte slot, BattleMode mode, byte ownTeam)
+    private Entry Create(int id, TankSpec spec, byte team, byte slot, BattleMode mode, byte ownTeam)
     {
-        Side side = MatchColors.SideOf(mode, ownTeam, state.Team, isOwn: false);
+        Side side = MatchColors.SideOf(mode, ownTeam, team, isOwn: false);
         var actor = new TankActor();
         AddChild(actor);
-        actor.Build(state.Spec, MatchColors.Paint(side));
-        var entry = new Entry { Actor = actor, Side = side, LabelColor = MatchColors.Ui(side).ToColor(), Slot = slot, PrevYaw = state.Yaw };
-        _entries[state.Id] = entry;
+        actor.Build(spec, MatchColors.Paint(side));
+        actor.Visible = false;   // nothing shows before Sync first sees the tank
+        var entry = new Entry { Actor = actor, Side = side, LabelColor = MatchColors.Ui(side).ToColor(), Slot = slot };
+        _entries[id] = entry;
         _order.Add(entry);
         return entry;
     }
 
-    private void Show(Entry entry, in TankState state, float dt, bool firstSight)
+    private void Show(Entry entry, in TankState state, float dt)
     {
+        // Not drawn last frame (never seen, or out of sight since): whatever happened meanwhile — a kill, a revival, a turn
+        // — is the state's to show at once, without the event wait or a yaw rate from a stale heading.
+        bool firstSight = !entry.Seen || !entry.Actor.Visible;
+        entry.Seen = true;
         entry.SeenStamp = _stamp;
         entry.Pose = TankPose.Compute(state, _terrain);
         float yawRate = firstSight ? 0f : RenderMath.YawRate(entry.PrevYaw, state.Yaw, dt);
PATCH
```

修改 `src/Tank.Client/scripts/Online/Logic/BattleSession.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Online/Logic/BattleSession.cs b/src/Tank.Client/scripts/Online/Logic/BattleSession.cs
index e2a14f4..e102435 100644
--- a/src/Tank.Client/scripts/Online/Logic/BattleSession.cs
+++ b/src/Tank.Client/scripts/Online/Logic/BattleSession.cs
@@ -15,15 +15,21 @@ public sealed class BattleSession : IDisposable
 {
     private readonly Stopwatch _clock = Stopwatch.StartNew();
     private readonly IHostBackend? _backend;
+    private readonly Func<IClientTransport> _connect;
+    private readonly string _playerName;
+    private readonly Func<double> _now;
     private bool _disposed;
 
     private BattleSession(IHostBackend? backend, Func<IClientTransport> connect, string playerName, uint token)
     {
         _backend = backend;
-        Client = new BattleClient(connect(), playerName, () => Now, token);
+        _connect = connect;
+        _playerName = playerName;
+        _now = () => Now;
+        Client = new BattleClient(connect(), playerName, _now, token);
     }
 
-    public BattleClient Client { get; }
+    public BattleClient Client { get; private set; }
 
     public bool IsHosting => _backend is not null;
 
@@ -54,6 +60,18 @@ public sealed class BattleSession : IDisposable
     /// <summary>Moves the host's world on: a loopback host steps its network and server; a threaded host runs by itself.</summary>
     public void Advance(double dt) => _backend?.Advance(dt);
 
+    /// <summary>
+    /// Replaces a dead client with a fresh one over a new connection, carrying the same session token: the server finds
+    /// the seat by it within its reconnect grace window and hands the same slot back (blueprint §5 T10). The old client
+    /// is disposed. When and how often to try is the caller's (<see cref="Reconnector"/>'s) business.
+    /// </summary>
+    public void Reconnect()
+    {
+        BattleClient old = Client;
+        Client = new BattleClient(_connect(), _playerName, _now, old.SessionToken);
+        old.Dispose();
+    }
+
     /// <summary>Closes the client, then stops the server thread, then the socket — the server even if the client throws. Safe to call again.</summary>
     public void Dispose()
     {
PATCH
```

新建 `src/Tank.Client/scripts/Online/Logic/Reconnector.cs`：

```csharp
namespace Tank.Client.Online;

/// <summary>
/// Decides, frame by frame, what to do about a dead connection (blueprint §5 T10). An unexpected drop of a session that
/// held a seat (a non-zero token to reclaim it by) is retried up to <see cref="MaxAttempts"/> times,
/// <see cref="RetrySeconds"/> apart, each by a fresh <c>BattleClient</c> carrying the same token: the server finds the
/// seat by it inside its 30 s grace window and kicks the stale connection (4A's <c>TakeOver</c>). A host's own client is
/// no different: a host process stalled past the 5 s timeout (a closed lid, a breakpoint) times out its link to its own
/// server, whose thread and socket are still running, and the owner token gets it the seat and the room back. A
/// deliberate close, a rejection or a protocol mismatch is never retried — that would only knock on a door that said
/// no. Pure logic; the owner replaces the client and shows the words.
/// </summary>
public sealed class Reconnector
{
    public const int MaxAttempts = 3;
    public const double RetrySeconds = 2;

    public enum Step : byte
    {
        /// <summary>An attempt is scheduled and not yet due: keep showing 正在重连….</summary>
        Wait,
        /// <summary>The attempt is due now: replace the client, keeping the session token.</summary>
        Connect,
        /// <summary>No (more) reconnecting: drop the session and show the reason.</summary>
        GiveUp,
    }

    private double _due = double.NaN;   // when the scheduled attempt falls due; NaN while none is scheduled

    /// <summary>Attempts begun: 0 until the first drop, and back to 0 once a connection makes it back.</summary>
    public int Attempt { get; private set; }

    /// <summary>
    /// Call once a frame while the client is disconnected. <paramref name="hadSeat"/> is whether the client ever got a
    /// seat (its session token is non-zero); a connection that never made it in is not "re"-connected, its error shows
    /// at once. The first call after a drop schedules the next attempt (or gives up); later calls wait it out.
    /// </summary>
    public Step Observe(string? reason, bool hadSeat, double now)
    {
        if (!double.IsNaN(_due))
        {
            if (now < _due) return Step.Wait;
            _due = double.NaN;
            return Step.Connect;
        }
        if (!hadSeat || Attempt >= MaxAttempts || !IsRetryable(reason)) return Step.GiveUp;
        Attempt++;
        _due = now + RetrySeconds;
        return Step.Wait;
    }

    /// <summary>The client is back in the room or the match: the next drop starts a fresh set of attempts.</summary>
    public void OnConnected()
    {
        Attempt = 0;
        _due = double.NaN;
    }

    /// <summary>
    /// Drops worth another try: the connection died under us (timed out, failed outright, or the way there vanished).
    /// A close from the other side, our own disconnect, a rejection and a protocol mismatch are answers, not accidents.
    /// </summary>
    public static bool IsRetryable(string? reason) =>
        reason is "Timeout" or "ConnectionFailed" or "HostUnreachable" or "NetworkUnreachable";
}
```

修改 `src/Tank.Client/scripts/Online/Logic/ThreadedHost.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Online/Logic/ThreadedHost.cs b/src/Tank.Client/scripts/Online/Logic/ThreadedHost.cs
index 0751c9d..449807c 100644
--- a/src/Tank.Client/scripts/Online/Logic/ThreadedHost.cs
+++ b/src/Tank.Client/scripts/Online/Logic/ThreadedHost.cs
@@ -9,13 +9,14 @@ namespace Tank.Client.Online;
 /// <summary>
 /// The listen server of a real room: a UDP transport and a <see cref="BattleServer"/> stepped at the simulation rate on
 /// its own thread, so a hitch of the window (a drag, a shader compile) never stalls the other players. The host's client
-/// connects to it over 127.0.0.1 like everyone else.
+/// connects to it over 127.0.0.1 like everyone else. The ticks are timed by a <see cref="TickProbe"/> for the F3 panel.
 /// </summary>
 internal sealed class ThreadedHost : IHostBackend
 {
     private readonly Stopwatch _clock = Stopwatch.StartNew();
     private readonly LiteNetServerTransport _transport;
     private readonly ServerRunner _runner;
+    private readonly TickProbe _probe;
     private readonly int _port;
     private bool _disposed;
 
@@ -40,7 +41,8 @@ internal sealed class ThreadedHost : IHostBackend
                 Seed = settings.Seed,
                 MatchSeconds = settings.MatchSeconds,
             }, log);
-            _runner = new ServerRunner(server, log, SimConstants.TickRate);
+            _probe = new TickProbe(server, () => server!.MatchRunning);
+            _runner = new ServerRunner(_probe, log, SimConstants.TickRate);
             _runner.Start();
         }
         catch
@@ -55,7 +57,7 @@ internal sealed class ThreadedHost : IHostBackend
 
     public double Now => _clock.Elapsed.TotalSeconds;
 
-    public (double AvgMs, double MaxMs)? ServerTick => (_runner.AverageTickMs, _runner.MaxTickMs);
+    public (double AvgMs, double MaxMs)? ServerTick => (_probe.AverageMs, _probe.MaxMs);
 
     public IClientTransport ConnectLocal() => new LiteNetClientTransport("127.0.0.1", _port, BattleProtocol.ConnectKey);
 
PATCH
```

新建 `src/Tank.Client/scripts/Online/Logic/TickProbe.cs`：

```csharp
using System.Diagnostics;
using Tank.Net.Server;

namespace Tank.Client.Online;

/// <summary>
/// Times a threaded host's server ticks for the F3 panel's "服务器 tick 平均 · 最大" (blueprint §5 T10), standing between
/// the <see cref="ServerRunner"/> and the server. The worst tick is the match's: the tick a match starts in builds the
/// map (some 50 ms, once) and would hide every later spike, so that tick is left out and the worst starts over from it
/// (<c>ServerRunner.MaxTickMs</c>, the worst since the thread started, only ever showed the map build). The average is
/// the runner's own kind, an exponential one. Written on the runner thread, read on the main thread.
/// </summary>
public sealed class TickProbe : ITickServer
{
    private const double Smoothing = 0.05;   // as ServerRunner's average

    private readonly ITickServer _server;
    private readonly Func<bool> _matchRunning;
    private double _averageMs, _maxMs;

    /// <param name="server">The server to step.</param>
    /// <param name="matchRunning">Whether a match is running, asked before and after each tick on the runner thread.</param>
    public TickProbe(ITickServer server, Func<bool> matchRunning) => (_server, _matchRunning) = (server, matchRunning);

    /// <summary>Exponential average of a tick's cost, ms (match starts left out).</summary>
    public double AverageMs => Volatile.Read(ref _averageMs);

    /// <summary>The worst tick since the current match started (its start excluded), or since the thread started before any match, ms.</summary>
    public double MaxMs => Volatile.Read(ref _maxMs);

    public void Tick()
    {
        bool wasRunning = _matchRunning();
        long start = Stopwatch.GetTimestamp();
        _server.Tick();
        double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (!wasRunning && _matchRunning())
        {
            Volatile.Write(ref _maxMs, 0);   // the map build: not this match's cost to show
            return;
        }
        Volatile.Write(ref _averageMs, _averageMs * (1 - Smoothing) + ms * Smoothing);
        if (ms > _maxMs) Volatile.Write(ref _maxMs, ms);
    }
}
```

修改 `src/Tank.Client/scripts/Sandbox/FrameTimeLog.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Client/scripts/Sandbox/FrameTimeLog.cs b/src/Tank.Client/scripts/Sandbox/FrameTimeLog.cs
index 958c366..b880f39 100644
--- a/src/Tank.Client/scripts/Sandbox/FrameTimeLog.cs
+++ b/src/Tank.Client/scripts/Sandbox/FrameTimeLog.cs
@@ -3,7 +3,10 @@ namespace Tank.Client.Sandbox;
 /// <summary>
 /// Collects per-frame render costs and reports average and worst once per second (<c>--fps-log</c>). Wall-clock frame time
 /// is meaningless under the 60 fps cap, so CPU render, GPU render and process time (all ms) are tracked separately,
-/// together with the latest frame's draw calls and primitives. The first (startup) window is dropped.
+/// together with the latest frame's draw calls and primitives, and — when the caller passes the runtime's cumulative
+/// managed-allocation figure — the window's allocation rate, the zero-allocation audit's number (blueprint §5 T10): a
+/// steady per-frame allocation shows as a KB/s floor that never settles. The first (startup) window is dropped.
+/// The report line itself is this code's only allocation, once a second, and is part of the figure it prints.
 /// </summary>
 public sealed class FrameTimeLog
 {
@@ -11,6 +14,7 @@ public sealed class FrameTimeLog
     private double _elapsed;
     private int _frames;
     private bool _startupDropped;
+    private long _allocAtWindowStart = -1;
     private readonly Stat _cpu = new(), _gpu = new(), _process = new();
 
     private sealed class Stat
@@ -20,9 +24,11 @@ public sealed class FrameTimeLog
         public void Reset() => Sum = Worst = 0;
     }
 
+    /// <param name="allocatedBytes">The runtime's cumulative managed allocation (<c>GC.GetTotalAllocatedBytes</c>), or -1 to leave the figure out.</param>
     /// <returns>A report line when a full second has been collected, otherwise null.</returns>
-    public string? Add(double frameSeconds, double cpuMs, double gpuMs, double processMs, long drawCalls, long primitives)
+    public string? Add(double frameSeconds, double cpuMs, double gpuMs, double processMs, long drawCalls, long primitives, long allocatedBytes = -1)
     {
+        if (_frames == 0) _allocAtWindowStart = allocatedBytes;
         _elapsed += frameSeconds;
         _frames++;
         _cpu.Add(cpuMs);
@@ -33,9 +39,10 @@ public sealed class FrameTimeLog
         if (_startupDropped)
         {
             string gpu = _gpu.Worst <= 0 ? "gpu n/a" : $"gpu {_gpu.Sum / _frames:0.00}/{_gpu.Worst:0.00}";
+            string alloc = allocatedBytes < 0 || _allocAtWindowStart < 0 ? "" : $", alloc {(allocatedBytes - _allocAtWindowStart) / _elapsed / 1024:0.0} KB/s";
             line = $"fps-log (avg/worst ms): cpu {_cpu.Sum / _frames:0.00}/{_cpu.Worst:0.00}, {gpu}, "
                    + $"process {_process.Sum / _frames:0.00}/{_process.Worst:0.00}, {_frames / _elapsed:0} fps, "
-                   + $"{drawCalls} draws, {primitives / 1000}k prims";
+                   + $"{drawCalls} draws, {primitives / 1000}k prims{alloc}";
         }
         _startupDropped = true;
         _elapsed = 0;
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet build src/Tank.Client 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`；`for p in Art Net; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`
Expected: 0 警告 0 错误；全部通过，Art 882、Net 210（`ReconnectSessionTests` 是真 UDP、线程主机、对局中途重连的演练——客户端一条、房主自己一条，输出里有服务器日志 `slot N reconnected`）。

- [ ] **Step 5：帧率与分配**

```bash
mkdir -p build/shots/t10
(cd $W && godot-mono --path src/Tank.Client -- --no-update --loopback --host --mode ffa --bots 9 --autostart --seed 7 --camera-preset orbit --fps-log --quit-after 100 > build/shots/t10/ffa10-fps-long.log 2>&1) & P=$!; (sleep 130; kill -9 $P 2>/dev/null) & wait $P
(cd $W && godot-mono --path src/Tank.Client -- --no-update --host --mode ffa --bots 9 --autostart --seed 7 --camera-preset orbit --netgraph --fps-log --screenshot $W/build/shots/t10/ffa10-netgraph.png --screenshot-at 12 --quit-after 25 > build/shots/t10/ffa10-netgraph.log 2>&1) & P=$!; (sleep 50; kill -9 $P 2>/dev/null) & wait $P
(cd $W && godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn -- --camera-preset orbit --fps-log --quit-after 25 > build/shots/t10/sandbox-orbit-fps.log 2>&1) & P=$!; (sleep 50; kill -9 $P 2>/dev/null) & wait $P
grep -c "fps-log" build/shots/t10/*.log
```

Expected（显示器 120 Hz 时帧率顶在约 120）：
- 回环 10 车长跑去掉前 10 秒至少 30 个一秒窗口，帧率与沙盒同条件（`sandbox-orbit-fps.log`）同一水平，不明显劣化；交战中没有百毫秒级的长帧（AutoRoom 开下一局、场景重建那一帧除外，在加载遮罩下）。
- 线程主机（`ffa10-netgraph`）的 `alloc` 和沙盒底线同一水平（约 30～40 KB/s），说明对局每帧路径没有新增稳定分配；回环会多出约 120 KB/s，来自 `Tank.Net` 的 `LoopbackNetwork.Deliver`，本阶段不改。
- `ffa10-netgraph.png`：F3 面板，服务器 tick 平均约 1 ms、最大是本局的（不含开局建图那一 tick），对账次数很少，重同步和丢包为 0。

- [ ] **Step 6：沙盒回归**

Run: `$S/baseline4b.sh $W build/shots/t10-sandbox && python3 $S/pngdiffdirs.py build/shots/base build/shots/t10-sandbox`（`BattleFx` 沙盒也用）。
Expected: 六张一致（差异只在云、地平线、粒子）。

- [ ] **Step 7：提交**

```bash
git add -A src/Tank.Client tests/Tank.Art.Tests
git commit -m "feat(client): performance and polish — the roster prebuilds every other tank's model on the first snapshot; bigger effect pools; an unexpected drop (the host's own link included) is retried with the same session token three times two seconds apart (正在重连…); the F3 server tick leaves out the match start's map build; --fps-log carries the managed allocation rate"
```

---

### Task 11：验收与文档

蓝图 §5 T11。不再改代码：全部测试、三个项目 0 警告、沙盒回归，两个实例在本机联机（团队对战打到结算回房间、F3 对账数字、房主离开、冻结房主和冻结客户端两种重连演练、各自为战阵亡观战），然后把实现中定下来的启动参数、键位表、HUD 布局、手动验收清单、已知遗留和 4C 衔接写进设计文档 §7、§8.1。下面的联机命令用 `$S/pair4b.sh`（同时起房主和客户端两个实例、窗口错开、带强制结束；`STOP_WHO=guest STOP_AT=秒 STOP_FOR=秒` 冻结一个进程做重连演练）。客户端用 `--join` 加入；点局域网列表加入在 Task 4 已截图验证，两台机器的验收在 Task 12 由用户做。

**Files:**
- Modify: `docs/superpowers/specs/2026-10-18-stage4-networking-design.md`（§7 启动参数、§7.1 键位、§7.2 HUD 布局、§7.3 手动验收清单、§7.4 已知遗留、§8.1 留给 4C 的衔接）

- [ ] **Step 1：全部测试和构建**

Run: `for p in Sim Net Art Core Update; do dotnet test tests/Tank.$p.Tests 2>&1 | grep -E "通过!|失败!"; done`；`for p in src/Tank.Client/Tank.Client.csproj src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do dotnet build --no-incremental $p 2>&1 | grep -E "Warning\(s\)|Error\(s\)"; done`
Expected: 全部通过，Art 882、Net 210，Sim 637、Core 62、Update 42；三个项目 0 警告 0 错误。

- [ ] **Step 2：沙盒回归**

Run: `$S/baseline4b.sh $W build/shots/t11-sandbox && python3 $S/pngdiffdirs.py build/shots/base build/shots/t11-sandbox`
Expected: 六张与基线内容一致，差异只在地平线带（环绕镜头按时间转，截图那一帧错开 1～2 像素）、云和灌木摇动；状态面板、车、墙没有变化。

- [ ] **Step 3：两个实例联机**

```bash
O=$W/build/shots/t11; mkdir -p $O
# 团队对战：对局中两端 F3
$S/pair4b.sh $W $O team-a 85 "--host --port 27016 --name A --mode teambattle --bots 2 --players 2 --autostart --match-seconds 30 --autodrive --netgraph --no-update --screenshot $O/team-a-host-f3.png --screenshot-at 25 --quit-after 60" "--join 127.0.0.1:27016 --name B --autostart --autodrive --netgraph --no-update --screenshot $O/team-a-guest-f3.png --screenshot-at 25 --quit-after 70"
# 团队对战：结算页
$S/pair4b.sh $W $O team-b 70 "--host --port 27016 --name A --mode teambattle --bots 2 --players 2 --autostart --match-seconds 30 --autodrive --netgraph --no-update --shot-after ended:3 --screenshot $O/team-b-host-results.png --quit-after 50" "--join 127.0.0.1:27016 --name B --autostart --autodrive --netgraph --no-update --shot-after ended:3 --screenshot $O/team-b-guest-results.png --quit-after 52"
# 房主 20 秒时退出
$S/pair4b.sh $W $O hostleave 45 "--host --port 27016 --name A --mode teambattle --bots 2 --players 2 --autostart --autodrive --no-update --quit-after 20" "--join 127.0.0.1:27016 --name B --autodrive --no-update --screenshot $O/hostleave-guest-menu.png --screenshot-at 26 --quit-after 30"
# 冻结房主 7 秒的重连演练（房主自己的本地连接和客户端都会超时，然后都按令牌回来）
STOP_WHO=host STOP_AT=12 STOP_FOR=7 $S/pair4b.sh $W $O drill-host 70 "--host --port 27016 --name A --mode teambattle --bots 2 --players 2 --autostart --autodrive --no-update --quit-after 45" "--join 127.0.0.1:27016 --name B --autodrive --netgraph --no-update --screenshot $O/drill-host-guest.png --screenshot-at 34 --quit-after 40"
# 冻结客户端 7 秒的重连演练
STOP_WHO=guest STOP_AT=12 STOP_FOR=7 $S/pair4b.sh $W $O drill-guest 45 "--host --port 27016 --name A --mode teambattle --bots 2 --players 2 --autostart --autodrive --no-update --quit-after 32" "--join 127.0.0.1:27016 --name B --autodrive --netgraph --no-update --screenshot $O/drill-guest-reconnecting.png --screenshot-at 10.5 --quit-after 16"
# 各自为战：客户端阵亡后观战
$S/pair4b.sh $W $O ffa 125 "--host --port 27016 --name A --tank light --mode ffa --bots 8 --players 2 --autostart --match-seconds 100 --autodrive --netgraph --no-update --quit-after 112" "--join 127.0.0.1:27016 --name B --autostart --autodrive --netgraph --no-update --shot-after death:4 --screenshot $O/ffa-guest-death4.png --quit-after 114"
pgrep -fil godot
```

Expected（模式名是 `teambattle`；客户端加 `--autostart` 只是让截图按对局时钟算）：
- `team-a-*-f3.png`：两端都在对局里、F3 打开；阵营色和比分方向是相对的（互为对面）；对账次数个位数、平均修正厘米级（对照 soak 有线：平均 0.045 m、最大 0.43 m），丢包、重同步 0；服务器 tick（只有房主看得到）的最大值约 50 ms 是开局建地图那一 tick。
- `team-b-*-results.png`：两端结算页（30 秒没人击毁时是“平局 击毁 0:0”），表按我方 / 敌方分组，只有房主页脚有“Enter 立即返回”；两端日志里有服务器的 “match over, back to the room”，之后 `--autostart` 开了第二局（`[match] entered match` 出现两次）。
- `hostleave-guest-menu.png`：客户端回到菜单，红字“连接已断开：房主关闭了房间。”，没有重连。
- 冻结房主：房主日志里客户端和房主自己先后 `disconnected: Timeout`，然后 “slot 1 reconnected as 'B'”、“slot 0 reconnected as 'A'”，房间没有关闭；`drill-host-guest.png` 是恢复后的对局画面。（被冻结的那一端 `pair4b.sh` 会报 rc=145，是 zsh 的 `wait` 在 STOP 时返回，不影响结果。）
- `drill-guest-reconnecting.png`：客户端菜单“连接断开，正在重连…（第 1/3 次）”；房主日志依次 “slot 1 (B) disconnected: Timeout”、“slot 1 reconnected as 'B'”，客户端日志第二次进入对局。
- `ffa-guest-death4.png`：客户端被击毁 4 秒后：击毁特写过后自动跟随一个活着的车（红色），底部“观战 X · Q/E 切换”，右上击毁提示是红底行。
- 收尾 `pgrep -fil godot` 为空。

- [ ] **Step 4：文档**

修改 `docs/superpowers/specs/2026-10-18-stage4-networking-design.md`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/docs/superpowers/specs/2026-10-18-stage4-networking-design.md b/docs/superpowers/specs/2026-10-18-stage4-networking-design.md
index 8d62824..cad0fe5 100644
--- a/docs/superpowers/specs/2026-10-18-stage4-networking-design.md
+++ b/docs/superpowers/specs/2026-10-18-stage4-networking-design.md
@@ -272,7 +272,107 @@ tick 频率原来写死为 `Units.TickRate`（60），改为参数。4C 删掉
   - 结算界面 8 秒；
   - “被点亮”灯，用服务器给的标志；
   - F3 面板沿用，加上对账误差和快照大小。
-- **启动参数**：`--host`、`--join`、`--mode`、`--bots`、`--tank`、`--autostart`，截图自动化用。
+- **启动参数**：`--host`、`--join`、`--mode`、`--bots`、`--tank`、`--autostart`，截图自动化用。实现后又加了 `--port`、`--name`、`--players`、`--seed`、`--match-seconds`、`--loopback`、`--sandbox`、`--hud-demo`，对局也接受 `--netgraph`、`--autodrive`、`--camera-preset`、`--sniper`、`--fps-log`、`--screenshot`/`--screenshot-at`/`--shot-after`/`--quit-after`；模式名是 `teambattle|annihilation|ffa`。全表见 `LaunchOptions.cs` 文件头。
+
+### 7.1 键位（实现中定下来的）
+
+驾驶和开火的键由 `DriveInput` 运行时注册，观战键和 Enter 由 `MatchInput` 注册；Tab、F3 用 `project.godot` 里原有的 `scoreboard`、`toggle_netgraph`。键位按物理位置。
+
+| 键 | 活着 | 阵亡后（观战） |
+|---|---|---|
+| W / S | 前进 / 后退 | 自由视角：焦点前后移动（按镜头朝向，60 m/s） |
+| A / D | 左转 / 右转 | 自由视角：焦点左右移动 |
+| 鼠标移动 | 转镜头，炮塔和炮管跟着准星 | 转镜头 |
+| 鼠标左键 | 开火（短于一个 tick 的点击也会锁存，不会丢） | 观战下一个 |
+| 鼠标右键（按住） | 自由观察：镜头转，炮不跟 | — |
+| 滚轮 | 镜头距离 8～30 m，每格 2 m；狙击镜里切倍率 2/4/8 | 镜头距离 |
+| 1 / 2 | 穿甲弹 / 高爆弹（一直有效，复活后回到穿甲） | — |
+| Shift | 狙击镜开关（加载中、暂停、结算时不起作用） | 按住：自由视角三倍速 |
+| Q / E | — | 观战上一个 / 下一个（到头回绕） |
+| F | — | 自由视角（仅各自为战） |
+| Tab（按住） | 计分板 | 计分板 |
+| F3 | 网络面板开关（`--netgraph` 时一开局就开着） | 同左 |
+| Esc | 放开鼠标并打开暂停层；再按一次关闭 | 同左 |
+| Enter | 结算页上房主立即回房间；暂停层上等于“继续” | 同左 |
+| 点一下画面 | 鼠标没被捕获时重新捕获（这一下不开火） | 同左 |
+
+- 方向键、J、空格、C（`project.godot` 的 `move_*`、`fire`、`toggle_camera`）是旧游戏的，对局不用。
+- 结算页上 Esc、Tab 不起作用（结算本身就摊开计分板）；暂停时 Tab 不起作用。暂停、结算和阵亡时发出去的指令只留瞄准：车停下、不开火，炮塔停在原来的方向（瞄准角不归零，别人不会看到炮塔转向正北），对局照常进行。
+- 窗口失去焦点时放开鼠标，并松开按住的开火（切回来不会一直开火）。
+- 房间里 Esc 是离开房间。菜单底部有一行键位提示。
+- 试驾场的键不变：另有 R 补弹、T 恢复物件、V 点亮调试、F1～F4 换车；从菜单进去时，Esc 第一次放开鼠标，第二次回菜单。
+
+### 7.2 HUD 布局（实现中定下来的）
+
+基准单位 1600×900（`project.godot` 是 `canvas_items` + `expand`，HUD 画布至少 1600×900，窗口更宽或更高时往外扩），1920×1080 窗口等于整体 ×1.2。位置由 `Match/Logic/HudLayout.cs` 算，`HudLayoutTests` 在 1600×900、1600×1200、2100×900 三个尺寸下检查。各块贴着屏幕边缘或中线摆，窗口变大只是挪位置，只有复活横幅按高度比例放。层次从下到上：`CombatHud`（层 1）、加载遮罩和 `MatchHud`（层 2）、暂停层（层 3）。字体全是 `HudStyle.Font`（ZCOOL 快乐体）加深色描边；菜单、房间和暂停层用 `UiKit`。
+
+| 块 | 位置和大小（基准单位） | 1080p | 内容 |
+|---|---|---|---|
+| 准星 | 屏幕中心 | 同 | 原有 `Reticle`；阵亡、结算时隐藏 |
+| 状态面板 | 左下，距左、距底各 24，约 360×178，上方留 36 写车名 | ×1.2 | 车名 24 号；血量、速度、装填、两种弹的签；右上角“被点亮”灯（服务器给的标志）。阵亡、结算时隐藏 |
+| 比分板 | 顶部居中 360×72，y=16；左右分数块各宽 112，中间是计时；板下 4 处写模式名（高 24，到 y=116） | 432×86，y=19 | 计时 40 号，最后 30 秒变红；分数 36 号，上面 16 号小标题：团队对战“我方 / 敌方”击毁数，团队歼灭“我方存活 / 敌方存活”，各自为战“击毁 N / 存活 a/b”；模式名 18 号 |
+| 击毁提示 | 右上，距右 24，从 y=16 往下，宽不超过 440，行高 36、行距 6，最多 5 行，每行 5 秒 | 距右 29 | 22 号“甲 击毁 乙”，名字按阵营色；射手看不见时写“乙 被击毁”，同队写“甲 误伤 乙”；自己击毁的那行黄底，自己被毁的那行红底。每个名字最宽 150，超出截断加“…”（两个 24 字节的名字也不出 440 宽，压不到比分板） |
+| F3 面板 | 左上 (24, 16)，500×280 | 600×336 | 标题“网络 · F3”22 号；正文 20 号、行距 24，客户端 8 行、房主 9 行：延迟·抖动·插值；快照条数·字节；下行·上行；时钟倍率·输入余量；对账次数·最近一次；修正平均·最大；丢包·重同步；（房主）服务器 tick 平均·最大（最大是本局的：开局建图的那一 tick 不算，每局重新算）；帧率。每 0.25 秒刷新 |
+| 复活横幅 | 水平居中，y = 0.28 × 高（900 高时 252），420×210 | y=302 | “你被击毁了”28 号，倒计时数字 96 号（黄），“秒后复活”24 号 |
+| 观战提示条 | 底部居中 680×56，底边距屏幕底 36（y=808） | 816×67 | 24 号“观战 名字 · Q/E 切换”；自由视角时“自由视角 · WASD 移动 · Q/E 跟随”。团队对战等复活时看队友，横幅和提示条同时出现 |
+| 计分板（Tab） | 居中 900 宽，y=120；表头 56，两队各一条 38 高的横幅，每人 42，下边距 14（10 人分两队时画出来 566 高；`HudLayout` 给它留的框是 576） | 1080 宽，y=144 | 列（相对表的左边）：名次 64、名字 112、车型 350、击毁 600、阵亡 685、状态 745；表头 20 号，正文 24 号，状态 22 号。两队模式我方在上；自己那行黄底，阵亡的人变暗；状态写“存活 / 已毁 / 复活中 N 秒” |
+| 结算 | 居中 960 宽，y=60，高 780，全屏压暗 0.55 | ×1.2 | 标题基线 y=140（84 号：胜利绿、失败红、平局白；各自为战写“第 N 名”），副标题基线 188（32 号），表从 y=224 开始（就是计分板），页脚基线 812（26 号“N 秒后返回房间”，房主多一句“ · Enter 立即返回”）。这时比分板隐藏 |
+| 暂停层 | 居中 440×280，全屏压暗 0.45 | — | “已暂停”28 号；按钮：继续 / 结束对局回到房间（只有房主）/ 离开房间 |
+| 加载遮罩 | 全屏 | — | 第一份快照到之前：“对局加载中…”40 号 |
+| 血条和名字 | 车头上方；只给 250 m 内、看得见、没毁的车 | — | 名字 18 号，在血条上方 5，按阵营色 |
+
+- **让位**：计分板（按住 Tab 或结算）出来时，击毁提示和 F3 面板隐藏。900 宽的表在 1600 宽的屏幕上两边各剩 350，放不下 440 宽的击毁提示和 500 宽的 F3 面板，所以蓝图 §4.2 的“两两不相交”改成了让位。
+- **计分板下移**：蓝图写 y=110，实现放在 y=120，避开板下的模式名。10 人分两队的满表（画出来 566 高）左下圆角会压进状态面板上方留给车名的位置一点（约 34×24），表是半透明圆角，可以接受。
+
+颜色（`Art/Palette`，新代码只用它）：
+
+| 用途 | 条目 | 值 |
+|---|---|---|
+| 自己的车 | `TankBody` | F28A2E 玩具橙 |
+| 队友的车、队友的名字、我方分数 | `AllyBody`（= `TargetBody`） | 6F8FB5 蓝灰 |
+| 敌人的车 | `EnemyBody` | D8344A |
+| 敌人的名字（击毁提示、血条上的名字、两队模式的计分板；各自为战里别人在击毁提示和血条上都算敌人）、敌方分数、最后 30 秒的计时、失败、自己被毁的提示行 | `UiDanger`（= `HitPierced`） | FF4D4D |
+| 自己的名字、强调、复活倒计时数字、各自为战比分板上自己的击毁数 | `UiAccent` | FFC93C |
+| 正文（计时、模式名、“击毁 / 误伤”、F3 正文）、各自为战计分板上别人的名字、各自为战比分板的存活数 | `UiText`（`HudStyle.Text`） | FFFFFF 白 |
+| 胜利、“存活” | `UiGood` | 73E68C |
+| 次要文字、表头 | `UiDim` | 8C94A3 |
+| 菜单、房间、加载遮罩的底色 | `UiBackground` | 0B0E16 |
+| HUD 底板 | `UiPanel` | 1E2333，透明度：击毁提示行 0.7，横幅 0.8，比分板和 F3 面板 0.85，计分板 0.88 |
+| 残骸 | `Charred` | 2B2622 |
+
+阵营按 `MatchColors.SideOf`：自己 → 自己；两队模式里同队 → 队友；其余都是敌人（各自为战里别人全是敌人）。房间里的队伍也按“我方 / 对面”显示，不用“金队 / 蓝队”。
+
+### 7.3 4B 手动验收清单
+
+截图验证不了的部分，用户试玩时逐条过。T11 在一台机器上开两个实例自动跑过的，在条目里注明了。客户端是用 `--join` 加入的，没点局域网列表；点选加入在 T4 截图验证过。
+
+0. **两台机器局域网**：A 开房间（团队对战，2 个机器人），B 在局域网列表里点选 A 的房间加入；各自选车，B 点“换到对面”再换回来，准备，A 开局。开局后两边按 F3：有线网络下对账误差应在厘米级（对照 soak 的有线数字：平均 0.045 m、最大 0.43 m；Wi-Fi 会大一些），丢包 0，重同步 0。打满一局，看到结算，8 秒后两端回到房间。（T11 单机：两端开局、F3、结算、回房间并开下一局都已自动跑通；本机回环上平稳行驶时对账平均约 0.03 m、最大约 0.05 m，个别大修正见 7.4。）
+1. **阵亡和复活（团队对战）**：被击毁后镜头停在残骸上，直到按 Q/E 或左键切到队友（3 秒特写之后自动跟人的只有不复活的团队歼灭和各自为战，见第 2 条）；中间横幅“你被击毁了 / 数字 / 秒后复活”每秒减一；Tab 里自己那行写“复活中 N 秒”；10 秒后在本队出生区复活，车模当场恢复完整，镜头回到车后，弹种回到穿甲，前 5 秒（保护期）开不了炮。
+2. **观战切换**：团队模式阵亡后，按 Q/E 或左键在活着的队友之间切换（到头回绕），底部提示条写着名字；被跟的队友死了自动换下一个；没有活着的队友时回到自己的残骸。团队歼灭和各自为战：3 秒特写后自动跟第一个活着的人（T11 已在各自为战里自动验证）。各自为战按 F 进自由视角：WASD 按镜头朝向移动焦点，按住 Shift 快三倍，滚轮缩放，焦点出不了场地；Q/E 回到跟随。全场没人活着时自动进自由视角。
+3. **Esc 暂停层**：鼠标放开，车停下，炮塔停在原来的方向（别人看着也不转），对局照常进行；暂停时 Shift 不开狙击镜；“继续”或再按 Esc 关闭并重新捕获鼠标；“离开房间”回菜单，显示“已离开房间。”；房主多一个“结束对局回到房间”，按下后两端都回房间；结算一出来暂停层自动关闭。
+4. **房主离开**：房主关窗或在暂停层点“离开房间”，客户端回菜单，红字“连接已断开：房主关闭了房间。”，不重连。（T11 已自动验证。）
+5. **断线重连**：对局中把一个进程 `kill -STOP <pid>` 停 6～8 秒再 `kill -CONT`（两台机器时也可以拔网线或关 Wi-Fi 6～8 秒），客户端和房主各做一次。
+   - 冻结客户端：客户端菜单上出现“连接断开，正在重连…（第 n/3 次）”，然后回到同一局、同一个座位（计分板上名字和战绩不变）。
+   - 冻结房主：恢复后房主自己连自己的那条连接按超时断开，房主这边同样显示“正在重连…”，再用房主令牌连回自己的服务器（服务器线程一直在，座位和房主身份都还在）；客户端那边也超时一次、重连回来。房间不关，两边都回到同一局、原来的座位。
+   - 最多三次、每次隔 2 秒，加上 5 秒超时，都在服务器 30 秒的保留期内（保留期按服务器 tick 算，房主被冻结时它也停着）。（单机自动验证过：T11 冻结客户端 7 秒，修复后冻结房主 7 秒，都是第 1 次就回到原座位，服务器日志两边都是“reconnected”。）
+6. **Tab 计分板**：按住出现，松开消失；两队模式我方在上；自己那行黄底，阵亡的人变暗；按住 Tab 时右上的击毁提示和 F3 面板让开；暂停时按 Tab 不出。
+7. **被点亮灯**：敌人看见自己时，状态面板右上的灯亮起并写“被点亮”；躲进草丛或离开视线约 2 秒后熄灭；阵亡后整个面板消失。
+8. **车型外观**：房间里四种车（轻坦、中坦、重坦、TD）各选一次进局，自己的车外观和状态面板上的车名对得上；别人看到的车型和计分板“车型”列一致；自己橙色，队友蓝灰，敌人红（各自为战里别人全红）。
+9. **失去焦点**：按住左键开火或按住 W 时 Cmd+Tab 切走再切回来：不会一直开火、一直开；鼠标是放开的，点一下画面重新捕获，这一下不开火。
+10. **团队对战中途加入**：第三个人在对局进行中加入，其他人第一次看见他的车时可能卡一下（他的模型这时才建），只有一次，可以接受。
+11. **多车同时开火**：各自为战加 8 个机器人，十辆车同时开火时，枪口火光不会突然消失。
+12. **结算页的 Enter**：房主在结算页按 Enter 提前回房间；客户端的结算页上没有“Enter 立即返回”。
+
+### 7.4 已知遗留
+
+- **个别接近 1 m 的对账修正。** 本机回环（没有延迟和丢包）上，平稳行驶时修正都在几厘米；但中途重连回来的客户端（3 次，最大 0.91 m）和十车各自为战里的客户端（5 次，最大 0.93 m）见过超过 0.5 m、直接拉回的修正。多半是撞上栅栏或砖墙：按 §3 的设计，预测不撞倒物件，服务器撞倒后接着开，客户端要等物件状态到了才跟上，全速约 14 m/s × 2 tick ≈ 0.9 m，和看到的数一致；没逐帧确认。两台机器试玩时留意 F3 的“最大”。
+- **加入不支持 IPv6 地址。** 菜单的“地址:端口”和 `--join` 都按最后一个冒号拆端口，`::1`、`fe80::…` 这类地址会被拆错；房间列表和本机地址提示也只有 IPv4。
+- **自己“没预测到的”开炮可能闪两次（未确认）。** 硬重同步之后服务器那发炮弹按事件画（`OnShellFired` 在自己炮口放火光），如果同一发也让预测的开炮计数变了，`ShowOwn` 会再放一次。没见过，也没逐帧查。
+- **满员房间里重连可能在传输层就被拒（未确认）。** 旧连接还没超时、还占着一个连接名额时，新连接会被 `LiteNetServerTransport` 的连接上限（10）挡在 hello 之前，服务器的 `TakeOver` 没机会执行；`ConnectionRejected` 不重试，于是直接放弃。只有 10 个真人满员时才会碰到。
+- **团队歼灭比分板的分母会在有人离开时变小。** “存活 a/b”的 b 数的是名单上现有的人：有人离开、30 秒保留期过后座位清掉，就少一个，不是开局时的人数（各自为战的“存活 a/b”也一样）。
+- **房主相关的测试在没有 UDP 的环境里会空过。** `ThreadedHostTests`、`ReconnectSessionTests` 绑不了端口时直接返回，算作通过；xUnit 2.9 没有运行时跳过，报告里看不出来。
+- **复活后的第一个 tick 可能还瞄着死前的方向。** `Command()` 在 `StepMatch` 之前取指令，复活那一帧的指令还带着死前的瞄准角，`SyncDeath` 同一帧稍后才把瞄准对回车头；只有那一帧。
+- 按设计不做的（蓝图 R16）：被击中方向提示、小地图、音效、聊天、出生保护的护盾画面、改键、房主迁移。
 
 ## 8. 4C 范围：清理
 
@@ -293,6 +393,27 @@ tick 频率原来写死为 `Units.TickRate`（60），改为参数。4C 删掉
   - Soak 只保留新跑法；
   - 两个 `.sln` 去掉 `Tank.Core`、加上 `Tank.Sim`。
 
+### 8.1 4B 留给 4C 的衔接
+
+T11 按草稿分支的代码核对过。**4B 的新代码依赖、删旧代码时必须保留或挪走的共用件：**
+
+- `Tank.Net/Server/ServerRunner.cs` 和 `ITickServer`：构造参数默认 `tickRate = Units.TickRate`（`Tank.Core`，60）。新的调用方（客户端 `ThreadedHost`、`Tank.Server` 的 `RunBattle`）都显式传 `SimConstants.TickRate`（30），删掉默认值即可。客户端 F3 的服务器 tick 不读 runner 的 `AverageTickMs`/`MaxTickMs`，而是 `ThreadedHost` 包在服务器外面的 `TickProbe`（`Online/Logic`，开局那一 tick 不算，每局重新算最大值）；`RunBattle` 每 30 秒的日志还读 runner 的，最大值仍是启动以来的（开局建图那次）。
+- `Tank.Net/Client/ClockSync.cs`：构造默认也是 `Units.TickRate`，同上（`BattleClient` 显式传 30）。**4B 改过它（T5）**：服务器的 tick 追上客户端自己的 tick 时也硬重同步。客户端落后时它的输入全被丢掉，输入余量的反馈也就没了，这是唯一的信号；`ClockSync30Tests` 有对应用例。这一改动同时作用于旧的 `GameClient`，旧客户端删掉后就无所谓了。挪的时候保留这段。
+- `Tank.Net/Transport/LoopbackNetwork.cs`：`using Tank.Core.Math` 的 `XorShift` 换成 `Tank.Sim.Geometry.XorShift`。另外 `Deliver` 每次调用都 `Where/OrderBy/ThenBy/ToList`（T10 发现），`--loopback` 运行时两端每帧都要分配；只影响测试和确定性截图，顺手换成复用的列表。
+- `Tank.Net/Protocol/MessageType.cs`：旧的 `MessageType` 枚举旁边放着新代码要用的 `ProtocolInfo`（`DefaultPort` 给菜单、启动参数、`LanBrowser`、`BattleServerOptions`；旧的 `MaxPlayerNameBytes` 新代码不再用：名字框按 `BattleProtocol.MaxNameBytes` 截 UTF-8 字节，见 `UI/Logic/NameLimit.cs`）和 `Channel` 枚举（新协议收发都用）。删旧消息时把这两个留下或挪走。
+- `BitReader`、`BitWriter`、`ProtocolException`：新协议的编解码全靠它们。
+- `LanDiscovery`：发出去的搜索包仍是旧的 `Messages.WriteDiscover`（旧 `MessageType.Discover` 加旧 `ProtocolInfo.Version`），对局服务器认这个包、回 `RoomInfo`。删旧 `Messages` 时要保留这几个字节（或者两端一起改）；旧的 `Rooms` / `DiscoveredRoom` 分支可以删。`BattleProtocol` 的注释也提到 `MessageType.Discover`。
+- `LiteNetServerTransport` 构造参数里旧的 `IRoomInfoProvider`，以及两个传输默认的连接口令 `ProtocolInfo.ConnectKey`（旧的 `"tank3d-v1"`）：新代码都显式传 `BattleProtocol.ConnectKey`。4C 去掉旧参数，口令改成必填或默认用新口令，免得漏传时得到看不出原因的 “ConnectionFailed”。
+- `project.godot` 的 `scoreboard`（Tab）、`toggle_netgraph`（F3）不要删，`MatchInput` 读它们；`move_*`、`fire`、`toggle_camera` 只有旧的 `Controls/InputSampler` 用，可以跟着删。
+- `ShellView.Age`（T1 加在 `BattleClientTypes.cs` 那个 record 的末尾，默认 0）：对局里曳光从炮口逐渐拉长靠它，保留。
+
+**客户端这边删的时候：**
+
+- 新代码不引用 `View/*`、`UI/Hud.cs`、`UI/LobbyScreen.cs`、`Controls/*`、`Bootstrap/BotDriver.cs`、`Bootstrap/Session.cs`（T11 用 grep 核对过），可以整块删。
+- 还在用旧 `View.Palette` 的只剩 `UI/Hud.cs`、`UI/LobbyScreen.cs`（蓝图 R6），跟着删；`UiKit` 已经改用 `Art.Palette`，新界面在用，保留。
+- `Tank.Client.csproj`、`Tank.Net.csproj`、`Tank.Server.csproj`、`tests/Tank.Soak/Tank.Soak.csproj` 直接引用 `Tank.Core`，要去掉；`Tank.Server` 和 Soak 的旧跑法一起删。
+- `Sandbox/TankInterpolation.cs` 虽然在试驾场目录，对局的 `RenderMath.OwnDisplay` 也用它，`Tank.Art.Tests` 也链接了它，别当旧代码删掉。
+
 ## 9. 不做
 
 - 公网联机、匹配、NAT 穿透（总体设计 §1）。
PATCH
```


- [ ] **Step 5：提交**

```bash
git add docs/superpowers/specs/2026-10-18-stage4-networking-design.md
git commit -m "docs: stage 4B acceptance — design §7 gains the launch options, keymap and HUD layout as built, a manual acceptance checklist for the playtest and the known leftovers; §8.1 lists what 4C must keep or move"
```

---

### Task 12：用户试玩与并入 main

- [ ] **Step 1：用户试玩**

把 Task 11 的手动验收清单交给用户，在 `/Users/murphy/Desktop/dev/BattleCity-stage4b` 上试玩（`godot-mono --path src/Tank.Client`）。试玩发现的问题在本分支修，修完重跑受影响任务的测试和截图。

- [ ] **Step 2：squash merge**

用户确认后，在主仓库 `/Users/murphy/Desktop/dev/BattleCity`（分支 `main`）：

```bash
git merge --squash feat/stage4b-client-match
git commit -m "feat: stage 4B client match — ..."   # 提交信息列出本阶段的内容
```

`main` 上的 `docs/PROGRESS.md` 记 4B 完成、下一步 4C。

- [ ] **Step 3：清理**

删除草稿 worktree 和分支（`BattleCity-stage4b-draft`、`draft/stage4b-client-match`）和计数用的 worktree（`BattleCity-stage4b-counts`）；保留 `feat/stage4b-client-match` 分支和它的 worktree。不推送远程。

---
## 写计划时已核实、与蓝图的差异

写这份计划时，先在临时 worktree（`/Users/murphy/Desktop/dev/BattleCity-stage4b-draft`，分支 `draft/stage4b-client-match`，从蓝图提交 `68efe6a` 拉出）里按任务顺序把下文全部实现了一遍，一个任务一个提交，每个任务都截图验证过。下文的代码和补丁由脚本从这些提交直接导出，没有手抄。实测结果：

- 每个提交单独检出后：客户端、服务器、soak 三个项目编译 0 警告 0 错误，Art 和 Net 测试全过。Art 测试数依次是 524、524、596、644、674、758、811、834、859、882、882（Task 1～11），Net 依次是 209、209、209、209、210、210、210、210、210、210、210；Sim 637、Core 62、Update 42 不变。
- 沙盒六张回归图（orbit、kill、lamp、wall、patrol、heavy）在 Task 1、2、9、10、11 之后都和基线一致，差异只在云、地平线带（环绕镜头按时间转、截图那一帧错开 1～2 像素）、灌木摇动、粒子和尘土。
- 回环确定性冒烟（`--loopback --host --bots 3 --autostart --seed 7 --autodrive`）跑两次，日志逐行相同，两张截图车辆和 HUD 一致。
- 10 车各自为战（回环，seed 7，orbit 机位）100 秒长跑：84 个一秒窗口 118～120 fps（显示器上限），最差 CPU 渲染 1.34 ms，交战中最差 process 25 ms；沙盒同条件 114～120 fps。线程主机下进程分配速率 31.8～39.9 KB/s，与沙盒底线相同（对局每帧路径无新增稳定分配）。
- 两个实例本机联机：团队对战打到结算、两端回房间并开下一局；F3 对账 0～9 次、平均 0.03 m、最大 0.05 m（独立服务器 120 秒），丢包、重同步 0；房主退出时客户端立刻提示“房主关闭了房间”不重连；冻结客户端 7 秒后“正在重连…（第 1/3 次）”回到同一局同一座位，冻结房主 7 秒后房主自己和客户端也都按令牌回到原座位、房间没有关闭；各自为战客户端阵亡后自动跟随观战。
- 草稿经过一轮三路并行的代码审查（客户端渲染、HUD 与界面、网络接线与自动化），没有 CRITICAL。按审查意见改了（都已并入对应任务的提交）：
  - **房主停顿就关房（HIGH）**：房主进程停顿超过 5 秒（合盖、断点、冻结）时，它连自己服务器的本地连接超时，而房主会话原来从不重连，`LeaveSession` 关掉还在跑的服务器、全员掉线。现在房主会话和访客一样按令牌重连（服务线程和端口都还在，宽限期按服务器 tick 计、停顿期间不走）；加了真 UDP 的房主重连测试，本机冻结房主 7 秒的演练两端都回到原座位（Task 10）。
  - **暂停时炮塔转向正北**：暂停发的是 `LocalCommand.None`（瞄准偏航 0），服务器把暂停玩家的炮塔转向世界 0 方向，所有人都看得到。改为不开不动、但瞄准角照发（Task 8）。
  - **对局中按过的 Enter 残留**：鼠标捕获时所有按键都进 `MatchInput`，对局中误按的 Enter 一直锁存到结算，房主一出结算就被送回房间。改为每帧取走、只在结算时生效（Task 7）。
  - **重新进入视野的车闪一下**：视野外被击毁或复活的车，重新看见时要等 0.3 秒兜底才换模型，转向速率还有一帧尖峰。改为按首见处理（Task 5）。
  - **血条用光**：血条按建立顺序编号，团队对战中途有人进出（新车号）后 10 条用完，后来的车没有血条。改为按顺序分给本帧可见的车（Task 5）。
  - **客户端的 `--tank` 被开局吞掉**：房主人齐当帧就开局，客户端的选车命令晚到、服务器不收，`AutoRoom` 也不再重发。改为人齐后再等约 0.5 秒才开局、回房间后重新核对车型（Task 3）。
  - **截图被覆盖成错的画面**：不带 `--autostart` 的客户端先由对局在正确时刻截图，再被 `Main` 按进程时钟用同一路径覆盖。改为一进对局截图就归对局、整次运行只拍一张（Task 9）。
  - **F3 的服务器 tick 最大值只反映开局**：原来读的是服务线程启动以来的最大值，永远是开局建地图那一 tick（约 50 ms）。改为 `TickProbe` 自己量、开局那一 tick 不计并清零（Task 10）。
  - **4A 的两条 ClockSync 测试“凑巧通过”**：有了 Task 5 的硬重同步后，它们把循环下标当服务器 tick 喂、走的已不是原来的路径。改写输入并断言路径（Task 5）。
  - 其余：自己复活当场恢复模型（Task 8）；暂停 / 结算 / 阵亡时 Shift 不切狙击镜（Task 8）；击毁提示名字按像素截断（Task 6）；房间界面只在房间数据变了才重建、局域网列表只在发现的房间变了才重建（Task 4、3）；名字框按 24 个 UTF-8 字节截断（Task 4）；开房 / 加入前先离开旧会话（Task 4）；`BattleSession.Dispose` 用 try/finally、房主令牌改用密码学随机数、`LiteNetClientTransport` 连接失败时停掉 NetManager（Task 3）；`LaunchOptions` 对未知开关、缺值、`--players` 越界给警告（Task 3）；`ended:N` 超过结算停留时长给警告、`--sandbox` 的警告只打一次（Task 9）；`ThreadedHostTests` 等到房间状态到了再断言 `IsHost`（Welcome 和 RoomState 是两条消息，约 1/20 的概率先断言，Task 3）。

与蓝图文字不同、但有意为之的地方（执行时不要“改回去”，设计文档 §7 已同步）：

- **横幅优先级**：结算 > 复活 > 观战；复活横幅和观战提示条可以同屏（团队对战阵亡等复活时看队友）。
- **Tab 计分板出现时隐藏击毁提示和 F3 面板**：900 宽的计分板和它们在几何上必然重叠（蓝图说“两两不相交”做不到）；计分板放在 y 120（蓝图 110），不盖住比分板下的模式名。
- **重连提示在菜单状态栏**，不是叠在对局画面上：断线时对局场景拆掉，重连回到对局时重建一次。
- **`--autostart` 时 `Main` 的 `--quit-after` 仍按进程墙钟**：对局结束 `AutoRoom` 会开下一局、对局时钟每局清零，只按对局时钟永远退不出。
- **血条顺序**是建立顺序（第一份快照的名单按槽位，再追加中途加入者），只分给当前可见的车。
- **断线重连**只对“意外”断开重试（超时、连接失败、主机或网络不可达），房主会话也一样；对方关闭、主动断开、被拒绝、协议不符、从没拿到座位都立即回菜单。
- **分配的观测**用 `--fps-log` 每行的进程分配速率（`GC.GetTotalAllocatedBytes`），没用 `dotnet-counters`：测的是速率不是堆大小。
- **`Tank.Net` 动了三处**：`ShellView.Age`（Task 1）、`ClockSync` 在服务器 tick 追上客户端时硬重同步（Task 5，场景构建卡一帧后预测时钟落后、输入全被当迟到的修复）、`LiteNetClientTransport` 构造里连接失败时停掉 NetManager（Task 3）。

## 留给以后的问题（不在本阶段处理）

- 断线重连会拆掉对局场景、回来重建一次（一次整场景的长帧）；做成对局内的浮层需要 `MatchMain` 能换用新客户端。
- `LoopbackNetwork.Deliver`（`Tank.Net`）每次 Poll 都用 LINQ 分配（回环约 120 KB/s，只影响 `--loopback`）；4C 或下次动 `Tank.Net` 时改成复用列表。沙盒本来就有约 32 KB/s 的分配底线（120 帧约 270 B/帧），没追到来源。
- 特效池大小按最坏情况推理定的，没实测抢占次数；分配只测了速率、没测堆大小。
- 按地址加入不支持 IPv6 字面量；局域网列表只向默认端口广播（R8），自定义端口要手填地址。
- 自己“没预测到的开炮”（服务器开了、客户端没预测）可能闪两次火光（待确认）；满员房间里重连的新连接可能在传输层被拒、旧连接还占着名额（待确认）。
- 偶尔有接近 1 m 的预测修正：全速撞倒栅栏或砖墙时客户端按设计不撞倒物件，约 14 m/s × 2 tick。
- 歼灭模式比分块的分母会随离开的人变小；复活后第一个 tick 的瞄准可能沿用阵亡前的角度。
- 主机测试在没有 UDP 的环境里直接返回（xUnit 2.9 没有动态跳过），等于空过。
- 团队对战中途加入的车第一次被看见时懒建模型，会卡一下（只一次）。
- 暂停层只是本地暂停，对局照跑（联机不可真暂停，设计内）；观战自由视角焦点高度固定 4.5 m。
- 新建的 `.cs` 没有 `.cs.uid`，Godot 编辑器首开时会补。

---
