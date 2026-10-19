# 阶段 4A：联网核心 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 不带画面的联网核心：仿真支持坦克中途加入、退出、复活和出生保护；三种对局规则（团队战、团队歼灭、大乱斗）和试驾场地图上的出生点、路点；服务器端的简单机器人；新的对战协议（输入、按客户端裁剪的增量快照、事件批、地图上线）；一个 30 Hz 的对战服务器（房间 + 对局）和一个无画面的对战客户端（预测、对账、插值、炮弹）；专用服务器入口的新模式和一套 4 种链路的 soak。

**Architecture:** 仿真库 `Tank.Sim` 仍不依赖 Godot，新增 `Modes/`（规则）、`Ai/SkirmishBot`、`Vehicles/TankStep`（一辆车走一步的公共单元，服务器和客户端预测共用）、`Maps/MapData`（地图上线的量化形式）。网络库 `Tank.Net` 新增命名空间 `Tank.Net.Battle`，与旧玩法的代码并存到 4C 再删：`BattleProtocol`/`BattleMessages`/`BattleSnapshotCodec` 是编码，`BattleServer` 开房间、跑对局、按 `Visibility` 规则给每个座位发它该知道的快照和事件，`BattleClient` 用 `TankStep.Advance` 预测自己的车、按服务器状态对账、插值别人、显示自己预测的和别人的炮弹。传输层沿用 LiteNetLib，加了连接密钥和对战房间的局域网发现。

**Tech Stack:** .NET 8、C# 12、xUnit 2.9、LiteNetLib；Godot 本阶段不碰（4B 才接画面）。

**设计依据：** `docs/superpowers/specs/2026-10-18-stage4-networking-design.md` §1～§6（§7、§8 是 4B、4C 的范围）；上级文档 `docs/superpowers/specs/2026-10-13-third-person-tank-design.md` §2.1、§8、§11、§12。

---
## 文件结构

**仿真 `src/Tank.Sim`**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `SimConstants.cs`、`Vehicles/TankState.cs`、`Simulation.cs`、`Simulation.Shells.cs` | 修改 | 队伍上限 16；`ProtectedUntil`；`SlotOf`、`RemoveTank`、`Respawn`、`Protect`、`IsProtected`；受保护的车不能开炮、挨打不掉血、不吃溅射 |
| `Vision/VisionTracker.cs` | 修改 | 可见性表的行跟着坦克编号走（`FollowSlots`），车被移除或加入时不错位，不分配内存 |
| `Vehicles/TankStep.cs` | 新建 | 一辆车走一步：`Advance(ref tank, input, tick, field, others, knockDown, felled, canFire, out shot)`；服务器和客户端预测共用 |
| `Vehicles/TankMotion.cs` | 修改 | `Drive` 可以不撞倒物件（客户端预测时物件由服务器说了算）；撞砖墙只在进入时减速 |
| `Maps/Battlefield.cs`、`Maps/MapData.cs` | 修改、新建 | `SetDurability`（客户端按服务器的物件状态改）；地图的上线形式（高度量化到厘米、物件、边界） |
| `Modes/BattleMode.cs` | 新建 | 三种模式和它们的规则常数 `ModeRules`（10 人、至少 2 人、保护 150 tick、复活 300 tick、结算 240 tick、时长） |
| `Modes/SpawnLayout.cs` | 新建 | 出生点：两队各 5 个、单人 10 个 |
| `Modes/MatchRules.cs` | 新建 | 一局的规则状态机：加入、离开、每步更新、阶段、倒计时、胜者、每个座位的 `Standing`（击杀、死亡、复活时刻、淘汰时刻、名次） |
| `Maps/SandboxMap.cs` | 修改 | 试驾场的出生点 `Spawns` 和机器人路点 `Waypoints` |
| `Ai/Steering.cs`、`Ai/PatrolDriver.cs` | 新建、修改 | 朝一个点开的油门和转向，巡逻车和机器人共用 |
| `Ai/SkirmishBot.cs` | 新建 | 机器人：在路点间游走，打本队看得见的 250 m 内最近的敌人，卡住 3 秒换路点 |

**网络 `src/Tank.Net`（新命名空间 `Tank.Net.Battle`）**

| 文件 | 新建/修改 | 职责 |
|---|---|---|
| `Battle/BattleProtocol.cs` | 新建 | 协议版本 2、连接密钥、消息号、量化（瞄准 16 位、偏航 12 位、俯仰 10 位、位置厘米、速度 0.1 m/s、耐久一字节）、上限 |
| `Battle/BattleInputPacket.cs` | 新建 | 输入包：最近 N 个 tick 的输入冗余发送，带最新收到的快照 tick |
| `Battle/BattleSnapshot.cs`、`Battle/BattleSnapshotCodec.cs` | 新建 | 快照（别人的车、自己的车全精度、对局块、计分、反馈）和按客户端基线的增量编码 |
| `Battle/BattleMessages.cs` | 新建 | hello / welcome / reject / 房间状态 / 房间命令 / 开局（带地图和物件状态）/ 事件批（带物件状态）/ 局域网房间信息 |
| `Battle/BattleInputBuffer.cs`、`Battle/BattleSeat.cs`、`Battle/Visibility.cs` | 新建 | 每个座位的输入缓冲、快照历史环；`Viewer`：谁能看到哪些车、哪些事件（设计 §5.5、§5.6 的表） |
| `Battle/BattleServer.cs`、`.Room.cs`、`.Match.cs`、`BattleServerOptions.cs` | 新建 | 房间（房主、模式、机器人数、准备、开局、回房间）、对局循环、机器人、每个客户端的事件和快照 |
| `Battle/BattleClient.cs`、`.Match.cs`、`BattleClientTypes.cs` | 新建 | 客户端：房间命令、开局时按 `MapData` 建场地、预测、对账、插值、自己的和别人的炮弹、事件队列、统计 |
| `Battle/BattlePredictor.cs`、`BattleInterpolator.cs`、`BattleSnapshotStore.cs` | 新建 | 预测与对账（0.5 m 硬拉、视觉偏移 τ=50 ms）、插值（渲染 tick、外推上限、瞬移阈值）、快照环 |
| `Transport/LiteNetServerTransport.cs`、`LiteNetClientTransport.cs`、`LanDiscovery.cs` | 修改 | 连接密钥参数；对战房间的发现回复 `BattleRooms` |
| `Server/ServerRunner.cs`、`Server/GameServer.cs`、`Client/ClockSync.cs` | 修改 | `ITickServer` + tick 率参数（旧服务器 60，对战 30） |

**入口与工具**：`src/Tank.Server/Program.cs`（`--mode teambattle|annihilation|ffa`、`--bots N`，旧模式照旧）；`tests/Tank.Soak/BattleSoak.cs`、`Program.cs`（`--battle`）。

**测试**：`tests/Tank.Sim.Tests` 新增 `RosterTests`、`TankStepTests`、`MatchRulesTests`、`SandboxSpawnTests`、`SkirmishBotTests`；`tests/Tank.Net.Tests/Battle/` 新增 `BattleProtocolTests`、`BattleServerUnitTests`、`BattleIntegrationTests`（`BattleHarness`：一台服务器和 N 个客户端跑在确定性的模拟网络上）、`TestWorld`。

---
## 执行前须知

- **工作目录**：直接在现有 worktree 里执行，不要新建：`/Users/murphy/Desktop/dev/BattleCity-stage4a`（分支 `feat/stage4a-netcore`）。下文所有路径都相对于它。
- 每个 shell 先执行：

```bash
export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"
cd /Users/murphy/Desktop/dev/BattleCity-stage4a
```

- **本阶段不碰 Godot**：全部是测试和控制台程序。客户端项目 `src/Tank.Client` 要能编译（它引用 `Tank.Sim`），但不运行。
- **补丁的用法**：修改已有文件的步骤给的是 `git apply` 补丁，在仓库根目录原样运行即可（heredoc 用了 `'PATCH'`，内容不会被 shell 展开）。打不上时先 `git status` 看是不是漏了前面的步骤，不要手工改。新建的文件给完整内容，用 Write 工具写入。
- `Directory.Build.props` 开启了 `TreatWarningsAsErrors`，`Tank.Sim`、`Tank.Net`、`Tank.Server`、测试项目和 `Tank.Soak` 里任何警告都会让构建失败。`Tank.Art.Tests` 会编译客户端里不依赖 Godot 的源码，本阶段不改客户端，它的数量不变。
- **不要改**：旧玩法的代码（`src/Tank.Core`、`src/Tank.Net/Protocol`、`src/Tank.Net/Server/GameServer*.cs`、`src/Tank.Net/Client/GameClient*.cs`、客户端的 `scripts/UI/`、`scripts/View/`、`scripts/Controls/`）和 `project.godot`，除非某一步的补丁明确改到（传输层、`ServerRunner`、`ClockSync`、`Tank.Server/Program.cs` 会改）。它们在 4C 整体删除。
- **坐标约定**（同前几个阶段）：Y 向上，−Z 为前方（北）；偏航 0 朝 −Z，正值向左转；`InputCmd.Steer` 为 +1 时向右转；`TankState.Position` 是二维 `(x, z)`。
- **仿真约束**：`Tank.Sim` 不依赖 Godot；`Simulation.Step`、`VisionTracker.Update`、`SkirmishBot.Command`、服务器的每 tick 路径和客户端的每 tick / 每包路径不分配内存。
- **测试项目与数量**：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。开始前是 Core 62、Sim 580、Net 50、Update 42、Art 512。每个任务末尾写明了做完之后 Sim 和 Net 的测试数；对不上就说明漏了或多了测试。
- 提交信息用约定式前缀（feat/fix/test/docs/chore）。每完成一批，在 `docs/PROGRESS.md` 的“4A 节点记录”里追加一行（任务、提交号、测试数）。
- 分支最后用 squash merge 并入 `main`，不推送远程，除非用户要求。
## 任务总览

| 批 | 任务 | 完成后 Sim / Net 测试数 |
|---|---|---|
| A 仿真 | 1 坦克的来去；2 一辆车走一步、地图上线；3 对局规则和出生点；4 机器人 | 591 / 50 → 598 / 50 → 614 / 50 → 622 / 50 |
| B 协议与服务器 | 5 对战协议；6 对战服务器 | 622 / 61 → 622 / 67 |
| C 客户端与入口 | 7 对战客户端；8 服务器入口和 soak | 622 / 80 → 622 / 80 |
| D 验收 | 9 全部测试、soak、记录；10 并入 main | — |

每个任务一个提交。任务 1～8 的代码由脚本从草稿分支导出（见文末“写计划时已核实”）。

---

### Task 1：坦克的来去

设计 §3.1～§3.3。`SimConstants.MaxTeams` 从 4 提到 16（大乱斗每人一队）。`TankState.ProtectedUntil`：到这个 tick 为止受出生保护。`Simulation` 新增：`SlotOf(id)`（没有返回 −1）、`RemoveTank(id)`（后面的槽位前移，编号不变）、`Respawn(id, position, yaw)`（整车换新，`ShotCount` 保留以免炮弹随机数重复，下一步报 `TankRevived`）、`Protect(id, ticks)`（之后 `ticks` 步内受保护）、`IsProtected(in tank)`。受保护的车不能开炮；打中它的炮弹停在装甲上、报 `TankHit` 但 `Damage` 为 0；溅射也照常报 `SplashDamage`、伤害 0。`VisionTracker` 的行随坦克编号走：移除或加入时不错位，仍不分配内存；残骸复活到别处的车从“没人看见”开始，原地复活的保持可见。

**Files:**
- Modify: `src/Tank.Sim/SimConstants.cs`、`Vehicles/TankState.cs`、`Simulation.cs`、`Simulation.Shells.cs`、`Vision/VisionTracker.cs`
- Test: 新建 `tests/Tank.Sim.Tests/RosterTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/RosterTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>Tanks coming and going during a battle (stage 4 design §3): removal, respawn, spawn protection.</summary>
public sealed class RosterTests
{
    private const int Tps = SimConstants.TickRate;
    private static readonly Vector3 HullFront = new(0, 1.02f, -3.1f);

    private static InputCmd Hold(in TankState t) => new(0, 0, 0, t.Yaw, 0f, false);

    private static void StepAll(Simulation sim)
    {
        Span<InputCmd> inputs = stackalloc InputCmd[sim.TankCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = Hold(sim.Tanks[i]);
        sim.Step(inputs);
    }

    [Fact]
    public void Sixteen_teams_fit_a_free_for_all()
    {
        var sim = new Simulation(TestWorld.FlatField());
        for (byte team = 0; team < SimConstants.MaxTeams; team++) sim.AddTank(TankCatalog.Light, new Vector2(20 + team * 10, 100), 0f, team);
        Assert.Equal(16, sim.TankCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => sim.AddTank(TankCatalog.Light, new Vector2(100, 150), 0f, SimConstants.MaxTeams));
    }

    [Fact]
    public void Removing_a_tank_closes_its_slot_and_keeps_the_others_ids()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int a = sim.AddTank(TankCatalog.Light, new Vector2(50, 100), 0f, 0);
        int b = sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f, 1);
        int c = sim.AddTank(TankCatalog.Heavy, new Vector2(150, 100), 0f, 1);
        sim.RemoveTank(b);
        Assert.Equal(2, sim.TankCount);
        Assert.Equal(new[] { a, c }, new[] { sim.Tanks[0].Id, sim.Tanks[1].Id });
        Assert.Equal(TankCatalog.Heavy, sim.Tanks[1].Spec);
        Assert.Equal(0, sim.SlotOf(a));
        Assert.Equal(1, sim.SlotOf(c));
        Assert.Equal(-1, sim.SlotOf(b));
        Assert.Throws<ArgumentException>(() => sim.RemoveTank(b));
        StepAll(sim);   // two inputs now
        int d = sim.AddTank(TankCatalog.Td, new Vector2(100, 100), 0f, 2);
        Assert.Equal(2, sim.SlotOf(d));
        Assert.True(d > c, "ids never come back");
    }

    [Fact]
    public void A_removed_tanks_shell_flies_on_and_still_counts_as_its()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Run(aim with { Fire = true }, 1);
        Assert.Equal(1, duel.Sim.ShellCount);
        duel.Sim.RemoveTank(duel.ShooterId);
        var events = new List<GameEvent>();
        for (int i = 0; i < 15; i++)
        {
            StepAll(duel.Sim);
            events.AddRange(duel.Sim.Events);
        }
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(duel.ShooterId, hit.TankId);
        Assert.Equal(duel.TargetId, hit.TargetTankId);
        Assert.True(hit.Damage > 0);
    }

    [Fact]
    public void Respawn_puts_a_wreck_back_fresh_somewhere_else_and_the_next_step_reports_it()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        duel.Sim.TankRef(duel.TargetId).Health = 1;
        duel.Fire(duel.AimAt(duel.OnTarget(HullFront)));
        Assert.True(duel.Target.IsDestroyed);
        int shots = duel.Shooter.ShotCount;
        duel.Sim.TankRef(duel.ShooterId).ApLeft = 3;

        duel.Sim.Respawn(duel.TargetId, new Vector2(60, 60), MathF.PI / 2);
        duel.Sim.Respawn(duel.ShooterId, new Vector2(140, 140), 0f);
        TankState target = duel.Target, shooter = duel.Shooter;
        Assert.False(target.IsDestroyed);
        Assert.Equal(target.Spec.MaxHealth, target.Health);
        Assert.Equal(new Vector2(60, 60), target.Position);
        Assert.Equal(MathF.PI / 2, target.Yaw, 1e-5f);
        Assert.Equal(0f, target.Speed);
        Assert.Equal(AmmoType.AP, target.Ammo);
        Assert.Equal(TankState.NeverFired, target.LastFired);
        Assert.Equal(shooter.Spec.Ap.Capacity, shooter.ApLeft);
        Assert.Equal(shots, shooter.ShotCount);   // kept: shot seeds stay unique
        Assert.Equal(duel.TargetId, target.Id);
        Assert.False(duel.Sim.IsProtected(target));

        var events = duel.Run(Hold(shooter), 1);
        Assert.Equal(2, events.Count(e => e.Type == GameEventType.TankRevived));
        Assert.Contains(events, e => e.Type == GameEventType.TankRevived && e.TargetTankId == duel.TargetId && e.Position.X == 60f);
    }

    [Fact]
    public void A_protected_tank_cannot_fire_until_the_protection_ends()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Sim.Protect(duel.ShooterId, 10);
        Assert.True(duel.Sim.IsProtected(duel.Shooter));
        var events = duel.Run(aim with { Fire = true }, 10);
        Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired);
        Assert.Equal(0, duel.Shooter.ShotCount);
        events = duel.Run(aim with { Fire = true }, 1);   // tick ProtectedUntil: the protection is over
        Assert.Single(events, e => e.Type == GameEventType.ShellFired);
        Assert.False(duel.Sim.IsProtected(duel.Shooter));
    }

    [Fact]
    public void A_protected_tank_stops_the_shell_and_reports_the_hit_but_takes_no_damage()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront));
        duel.Sim.Protect(duel.TargetId, 5 * Tps);
        var events = duel.Fire(aim);
        GameEvent hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.Equal(HitResult.Penetrated, hit.Result);
        Assert.Equal(0, hit.Damage);
        Assert.Equal(900, duel.Target.Health);
        Assert.Equal(0, duel.Sim.ShellCount);   // the shell stopped on the armour

        duel.Run(aim, 5 * Tps);   // the protection runs out
        events = duel.Fire(aim);
        hit = Assert.Single(events, e => e.Type == GameEventType.TankHit);
        Assert.True(hit.Damage > 0);
        Assert.Equal(900 - hit.Damage, duel.Target.Health);
    }

    [Fact]
    public void A_protected_tank_is_reported_splashed_but_takes_no_damage()
    {
        var duel = new Duel(targetYaw: MathF.PI);
        int bystander = duel.AddBystander(Duel.TargetAt + new Vector2(3.4f, 3.1f), MathF.PI);   // beside the target's front, where the shell bursts
        duel.Sim.Protect(bystander, 20 * Tps);   // outlasts the aiming and the HE reload
        InputCmd aim = duel.AimAt(duel.OnTarget(HullFront), AmmoType.HE);
        duel.Run(aim, (int)(Duel.Spec.ReloadTime * Tps) + 1);   // the HE swap costs a reload
        var events = duel.Fire(aim);
        Assert.Single(events, e => e.Type == GameEventType.ShellExploded);
        GameEvent splash = Assert.Single(events, e => e.Type == GameEventType.SplashDamage && e.TargetTankId == bystander);
        Assert.Equal(0, splash.Damage);
        Assert.Equal(900, duel.Sim.Tanks[2].Health);
    }

    [Fact]
    public void Vision_follows_tanks_when_a_slot_is_removed_and_another_added()
    {
        // A tank destroyer (team 0) at (100, 190) looks north; a light dummy (team 1) at 50 m is seen and doesn't watch;
        // a far light tank (team 1) at 180 m is out of range both ways (a still TD is seen from 172 m, a still light tank
        // from 162 m). Remove the observer's team mate in slot 1: the dummy's rows shift.
        var sim = new Simulation(TestWorld.FlatField());
        int observer = sim.AddTank(TankCatalog.Td, new Vector2(100, 190), 0f, 0);
        int mate = sim.AddTank(TankCatalog.Medium, new Vector2(60, 190), 0f, 0);
        int dummy = sim.AddTank(TankCatalog.Light, new Vector2(100, 140), MathF.PI / 2, 1);
        int far = sim.AddTank(TankCatalog.Light, new Vector2(100, 10), MathF.PI / 2, 1);
        var vision = new VisionTracker(sim);
        vision.SetWatching(dummy, false);
        StepAll(sim);
        vision.Update();
        Assert.True(vision.IsVisible(dummy, 0));
        Assert.False(vision.IsVisible(far, 0));
        Assert.False(vision.IsVisible(observer, 1), "the dummy doesn't watch");

        sim.RemoveTank(mate);
        sim.TankRef(dummy).Position = new Vector2(100, 12);   // out of range now, but the linger keeps it in view
        for (int i = 0; i < 4; i++) { StepAll(sim); vision.Update(); }
        Assert.True(vision.IsVisible(dummy, 0), "lingering: its row moved with it");
        int newcomer = sim.AddTank(TankCatalog.Heavy, new Vector2(100, 140), MathF.PI / 2, 1);
        Assert.False(vision.IsVisible(newcomer, 0), "a new tank starts unseen");
        for (int i = 0; i < 6; i++) { StepAll(sim); vision.Update(); }
        Assert.True(vision.IsVisible(newcomer, 0));
        Assert.True(vision.IsVisible(observer, 1), "the newcomer watches, and sees the observer 50 m away");
        Assert.False(vision.IsVisible(far, 0));
    }

    [Fact]
    public void A_tank_respawned_somewhere_else_starts_unseen_while_one_revived_where_it_lay_stays_in_view()
    {
        // A medium observer (team 0) at (190, 190); an enemy medium wrecked 50 m away is seen as a wreck. Respawned at
        // (10, 10), 255 m off and beyond detection, it must not be given away for the linger; revived in place it stays.
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(190, 190), 0f, 0);
        int enemy = sim.AddTank(TankCatalog.Medium, new Vector2(190, 140), MathF.PI, 1);
        var vision = new VisionTracker(sim);
        sim.TankRef(enemy).IsDestroyed = true;
        sim.TankRef(enemy).Health = 0;
        StepAll(sim);
        vision.Update();
        Assert.True(vision.IsVisible(enemy, 0), "a wreck is seen by all");

        sim.Respawn(enemy, new Vector2(10, 10), 0f);
        vision.Update();   // same tick: the server asks right after the respawn
        Assert.False(vision.IsVisible(enemy, 0), "respawned elsewhere: unseen at once");
        for (int i = 0; i < 12; i++) { StepAll(sim); vision.Update(); }
        Assert.False(vision.IsVisible(enemy, 0), "and still unseen 255 m away");

        sim.TankRef(enemy).IsDestroyed = true;
        sim.TankRef(enemy).Health = 0;
        sim.TankRef(enemy).Position = new Vector2(190, 140);
        for (int i = 0; i < 6; i++) { StepAll(sim); vision.Update(); }
        Assert.True(vision.IsVisible(enemy, 0));
        sim.Revive(enemy);
        vision.Update();
        Assert.True(vision.IsVisible(enemy, 0), "revived where it lay: in view for the linger");
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`SlotOf`、`RemoveTank`、`Respawn`、`Protect`、`IsProtected`、`ProtectedUntil` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/SimConstants.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/SimConstants.cs b/src/Tank.Sim/SimConstants.cs
index 6beb2a1..9ace05f 100644
--- a/src/Tank.Sim/SimConstants.cs
+++ b/src/Tank.Sim/SimConstants.cs
@@ -6,6 +6,6 @@ public static class SimConstants
     public const float Dt = 1f / TickRate;
     public const float Gravity = 9.81f;
 
-    /// <summary>Teams are numbered 0 … MaxTeams − 1.</summary>
-    public const int MaxTeams = 4;
+    /// <summary>Teams are numbered 0 … MaxTeams − 1: room for every tank of a free-for-all to be its own team.</summary>
+    public const int MaxTeams = 16;
 }
PATCH
```

修改 `src/Tank.Sim/Simulation.Shells.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.Shells.cs b/src/Tank.Sim/Simulation.Shells.cs
index 766303b..cf1d77b 100644
--- a/src/Tank.Sim/Simulation.Shells.cs
+++ b/src/Tank.Sim/Simulation.Shells.cs
@@ -124,7 +124,8 @@ public sealed partial class Simulation
 
     /// <summary>
     /// A shell meets a tank's armour. A live tank takes the rolled result (AP may bounce; HE never does, and bursts on the
-    /// plate). A wreck just stops the shell: AP is spent, HE still bursts and splashes the tanks around it.
+    /// plate); one under spawn protection takes the hit as 0 damage. A wreck just stops the shell: AP is spent, HE still
+    /// bursts and splashes the tanks around it.
     /// </summary>
     private void StrikeTank(in Shell shell, int slot, in ArmorHit hit, Vector3 direction)
     {
@@ -138,16 +139,18 @@ public sealed partial class Simulation
         Impact impact = shell.Ammo == AmmoType.HE
             ? Penetration.HeDirect(shell.Round, hit.Thickness, angle, shell.Seed)
             : Penetration.Ap(shell.Round, shell.Caliber, hit.Thickness, angle, shell.Seed);
+        int dealt = IsProtected(target) ? 0 : impact.Damage;
         _events.Add(new GameEvent(GameEventType.TankHit, Tick, shell.OwnerTankId, shell.Id, hit.Point, hit.Normal,
-            target.Id, impact.Result, impact.Damage, shell.Ammo));
-        Damage(ref target, impact.Damage, shell.OwnerTankId, shell.Id);
+            target.Id, impact.Result, dealt, shell.Ammo));
+        Damage(ref target, dealt, shell.OwnerTankId, shell.Id);
         if (shell.Ammo == AmmoType.HE) Explode(shell, hit.Point, slot);
     }
 
     /// <summary>
     /// The shell bursts at <paramref name="at"/>. A round with a splash radius (HE) hurts every other live tank in range,
-    /// except the shooter and <paramref name="directSlot"/> (the tank it struck directly, already dealt with), then the
-    /// props around it (<see cref="SplashProps"/>; <paramref name="directProp"/> is the wall it struck, if any).
+    /// except the shooter and <paramref name="directSlot"/> (the tank it struck directly, already dealt with); a tank
+    /// under spawn protection is reported hit for no damage. Then the props around it (<see cref="SplashProps"/>; <paramref name="directProp"/> is the wall
+    /// it struck, if any).
     /// </summary>
     private void Explode(in Shell shell, Vector3 at, int directSlot, int directProp = -1)
     {
@@ -163,9 +166,10 @@ public sealed partial class Simulation
             float distance = TankPose.Compute(target, Field.Terrain).HullBox(target.Spec).DistanceTo(at);
             int damage = Penetration.Splash(shell.Round, distance, target.Spec.HullArmor.Armor.Thinnest);
             if (damage <= 0) continue;
+            if (IsProtected(target)) damage = 0;
             _events.Add(new GameEvent(GameEventType.SplashDamage, Tick, shell.OwnerTankId, shell.Id, at, Vector3.Zero,
                 target.Id, HitResult.None, damage, shell.Ammo));
-            Damage(ref target, damage, shell.OwnerTankId, shell.Id);
+            if (damage > 0) Damage(ref target, damage, shell.OwnerTankId, shell.Id);
         }
         SplashProps(shell, at, directProp);
     }
PATCH
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index a5fd58c..cfca550 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -15,6 +15,11 @@ namespace Tank.Sim;
 /// get to fire first, shell ids follow slot order, and a prop one tank knocks down is already down for the next). Then
 /// the shell phase: shots that burst in a blocked barrel go off, then every shell in flight flies and resolves its hits,
 /// each group in firing order (shots of the same tick in slot order).
+/// <para>
+/// Tanks sit in slots in <see cref="Tanks"/>, in the order they were added; <see cref="RemoveTank"/> closes the gap, so
+/// the slots after it move down by one (ids never change or come back). Hosts that keep per-slot data follow
+/// <see cref="SlotOf"/>, as <see cref="Vision.VisionTracker"/> does.
+/// </para>
 /// </summary>
 public sealed partial class Simulation
 {
@@ -63,6 +68,48 @@ public sealed partial class Simulation
         return id;
     }
 
+    /// <summary>The slot of tank <paramref name="tankId"/> in <see cref="Tanks"/>, or −1 when there is no such tank.</summary>
+    public int SlotOf(int tankId)
+    {
+        for (int i = 0; i < TankCount; i++)
+            if (_tanks[i].Id == tankId) return i;
+        return -1;
+    }
+
+    /// <summary>
+    /// Takes a tank out of the battle (its player left): the slots after it move down by one. Shells it fired fly on and
+    /// still count as its. No event: the host knows.
+    /// </summary>
+    public void RemoveTank(int tankId)
+    {
+        int slot = SlotOf(tankId);
+        if (slot < 0) throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
+        Array.Copy(_tanks, slot + 1, _tanks, slot, TankCount - slot - 1);
+        TankCount--;
+        _tanks[TankCount] = default;
+    }
+
+    /// <summary>
+    /// Puts a tank (a wreck, usually) back into the battle at <paramref name="position"/> facing <paramref name="yaw"/>,
+    /// as fresh as on <see cref="AddTank"/>: full health and ammunition, AP loaded, standing still, not having fired.
+    /// Its id, team and shot count stay (the shot count carries on, so each tank's shot index stays monotonic). The
+    /// <see cref="GameEventType.TankRevived"/> event is reported by the next <see cref="Step"/>; the host sets any
+    /// protection with <see cref="Protect"/>.
+    /// </summary>
+    public void Respawn(int tankId, Vector2 position, float yaw)
+    {
+        ref TankState tank = ref TankRef(tankId);
+        tank = Fresh(tank.Id, tank.Spec, position, yaw, tank.Team) with { ShotCount = tank.ShotCount };
+        Vector3 at = TankPose.Compute(tank, Field.Terrain).Position;
+        _pending.Add(new GameEvent(GameEventType.TankRevived, Tick, tankId, 0, at, Vector3.Zero, TargetTankId: tankId));
+    }
+
+    /// <summary>Protects a tank through the next <paramref name="ticks"/> steps (see <see cref="TankState.ProtectedUntil"/>).</summary>
+    public void Protect(int tankId, int ticks) => TankRef(tankId).ProtectedUntil = Tick + ticks + 1;
+
+    /// <summary>Whether <paramref name="tank"/> is under spawn protection at the current tick.</summary>
+    public bool IsProtected(in TankState tank) => Tick < tank.ProtectedUntil;
+
     /// <summary>
     /// Makes a tank another kind of vehicle where it stands (sandbox only for now). Its id, team, place, heading and shot
     /// count stay (the shot count carries on, so each tank's shot index stays monotonic); everything else starts as on
@@ -88,6 +135,9 @@ public sealed partial class Simulation
         HeLeft = spec.He.Capacity,
     };
 
+    /// <summary>
+    /// One tick: pending events first, then every live tank's step (its props felled, then its shot), then the shells.
+    /// </summary>
     /// <param name="inputs">One command per tank, in <see cref="Tanks"/> order.</param>
     public void Step(ReadOnlySpan<InputCmd> inputs)
     {
@@ -188,7 +238,8 @@ public sealed partial class Simulation
 
     /// <summary>
     /// Counts the reload down, swaps rounds if the input asks for the other type (which restarts the reload), then fires
-    /// if asked, loaded and a round of that type is left. The shell leaves inside the aim circle, which then opens.
+    /// if asked, loaded, not under protection, and a round of that type is left. The shell leaves inside the aim circle,
+    /// which then opens.
     /// </summary>
     private void TryFire(ref TankState tank, in InputCmd input)
     {
@@ -200,7 +251,7 @@ public sealed partial class Simulation
             tank.Reload = tank.Spec.ReloadTime;
             return;
         }
-        if (!input.Fire || tank.Reload > 0 || tank.AmmoLeft(tank.Ammo) == 0 || ShellCount == MaxShells) return;
+        if (!input.Fire || tank.Reload > 0 || tank.AmmoLeft(tank.Ammo) == 0 || IsProtected(tank) || ShellCount == MaxShells) return;
 
         Ballistics.Launch launch = Ballistics.Aim(tank, Field);
         uint seed = ShotRandom.Seed(Tick, tank.Id, tank.ShotCount);
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankState.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankState.cs b/src/Tank.Sim/Vehicles/TankState.cs
index af2c1c6..693f1a1 100644
--- a/src/Tank.Sim/Vehicles/TankState.cs
+++ b/src/Tank.Sim/Vehicles/TankState.cs
@@ -42,6 +42,12 @@ public struct TankState
     /// <summary>A wreck ignores input and takes no more damage, but still blocks tanks and shells.</summary>
     public bool IsDestroyed;
 
+    /// <summary>
+    /// Spawn protection (stage 4 design §2.2): before this tick the gun won't fire, and hits still stop the shell and
+    /// are reported, but do no damage. 0 (or any past tick) means none.
+    /// </summary>
+    public int ProtectedUntil;
+
     /// <summary>Round in the breech; firing uses this type.</summary>
     public AmmoType Ammo;
 
PATCH
```

修改 `src/Tank.Sim/Vision/VisionTracker.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vision/VisionTracker.cs b/src/Tank.Sim/Vision/VisionTracker.cs
index 7850df6..8418f92 100644
--- a/src/Tank.Sim/Vision/VisionTracker.cs
+++ b/src/Tank.Sim/Vision/VisionTracker.cs
@@ -1,3 +1,4 @@
+using System.Numerics;
 using Tank.Sim.Geometry;
 using Tank.Sim.Vehicles;
 
@@ -8,9 +9,11 @@ namespace Tank.Sim.Vision;
 /// <see cref="Simulation.Step"/>. On the first call, and then every <see cref="UpdateInterval"/>-th tick (5 Hz), it has
 /// every live, watching tank look at every live enemy (<see cref="Spotting.Sees"/>) and notes, per target and team,
 /// the last tick a tank of that team saw it. A tank stays visible to a team for <see cref="LingerTicks"/> after that; it
-/// is always visible to its own team, and a wreck to everyone (each update notes it as seen by every team, so a revived
-/// tank stays in view for the linger rather than vanishing until the next update). Tanks are kept by slot in <see cref="Simulation.Tanks"/>
-/// (slots never change: tanks are only ever added). Nothing allocates after construction.
+/// is always visible to its own team, and a wreck to everyone (each update notes it as seen by every team, so a tank
+/// revived where it lay stays in view for the linger rather than vanishing until the next update; one that comes back
+/// somewhere else — a respawn — starts unseen, or its new position would be given away). What it knows about a tank follows
+/// the tank's id, so tanks removed from the battle (the slots after them move down, see <see cref="Simulation.RemoveTank"/>)
+/// and tanks added later are taken in their stride. Nothing allocates after construction.
 /// </summary>
 public sealed class VisionTracker
 {
@@ -20,11 +23,20 @@ public sealed class VisionTracker
     private const int NeverSeen = int.MinValue / 2;
 
     private readonly Simulation _sim;
-    private readonly int[] _lastSeen = new int[Simulation.MaxTanks * MaxTeams];   // [slot * MaxTeams + team]
-    private readonly bool[] _blind = new bool[Simulation.MaxTanks];
-    private readonly bool[] _moving = new bool[Simulation.MaxTanks];
-    private readonly float[] _lastYaw = new float[Simulation.MaxTanks];
-    private int _known;            // slots whose last yaw has been noted
+    private readonly int[] _ids = new int[Simulation.MaxTanks];      // the tank in each slot at the last call; follows the layout
+    private int[] _lastSeen = new int[Simulation.MaxTanks * MaxTeams];   // [slot * MaxTeams + team]
+    private bool[] _blind = new bool[Simulation.MaxTanks];
+    private bool[] _moving = new bool[Simulation.MaxTanks];
+    private float[] _lastYaw = new float[Simulation.MaxTanks];
+    private bool[] _wasWreck = new bool[Simulation.MaxTanks];       // destroyed at the last call
+    private Vector2[] _wreckAt = new Vector2[Simulation.MaxTanks];  // where it lay
+    private int[] _lastSeenScratch = new int[Simulation.MaxTanks * MaxTeams];   // the rows rebuilt when the layout changes
+    private bool[] _blindScratch = new bool[Simulation.MaxTanks];
+    private bool[] _movingScratch = new bool[Simulation.MaxTanks];
+    private float[] _lastYawScratch = new float[Simulation.MaxTanks];
+    private bool[] _wasWreckScratch = new bool[Simulation.MaxTanks];
+    private Vector2[] _wreckAtScratch = new Vector2[Simulation.MaxTanks];
+    private int _count;            // slots followed
     private int _lastUpdate = -1;  // the tick of the last update; -1 before the first
 
     public VisionTracker(Simulation sim)
@@ -34,7 +46,11 @@ public sealed class VisionTracker
     }
 
     /// <summary>A tank that doesn't watch is never an observer (a practice dummy), though others still see it.</summary>
-    public void SetWatching(int tankId, bool watching) => _blind[SlotOf(tankId)] = !watching;
+    public void SetWatching(int tankId, bool watching)
+    {
+        FollowSlots(_sim.Tanks);
+        _blind[SlotOf(tankId)] = !watching;
+    }
 
     /// <summary>
     /// Call after every step: on the first call and every <see cref="UpdateInterval"/>-th tick, works out who sees whom.
@@ -43,7 +59,8 @@ public sealed class VisionTracker
     public void Update()
     {
         ReadOnlySpan<TankState> tanks = _sim.Tanks;
-        for (; _known < tanks.Length; _known++) _lastYaw[_known] = tanks[_known].Yaw;
+        FollowSlots(tanks);
+        ForgetTheRespawned(tanks);
         int tick = _sim.Tick;
         if (tick == _lastUpdate || (_lastUpdate >= 0 && tick % UpdateInterval != 0)) return;
         _lastUpdate = tick;
@@ -72,10 +89,23 @@ public sealed class VisionTracker
         }
     }
 
+    /// <summary>A wreck that is back on its tracks somewhere else than where it lay has respawned: nobody has seen it yet.</summary>
+    private void ForgetTheRespawned(ReadOnlySpan<TankState> tanks)
+    {
+        for (int j = 0; j < tanks.Length; j++)
+        {
+            ref readonly TankState t = ref tanks[j];
+            if (_wasWreck[j] && !t.IsDestroyed && t.Position != _wreckAt[j]) Array.Fill(_lastSeen, NeverSeen, j * MaxTeams, MaxTeams);
+            _wasWreck[j] = t.IsDestroyed;
+            if (t.IsDestroyed) _wreckAt[j] = t.Position;
+        }
+    }
+
     /// <summary>Whether tank <paramref name="tankId"/> is visible to <paramref name="team"/> as of the last update.</summary>
     public bool IsVisible(int tankId, byte team)
     {
         if (team >= MaxTeams) throw new ArgumentOutOfRangeException(nameof(team), team, $"Teams are 0 … {MaxTeams - 1}.");
+        FollowSlots(_sim.Tanks);
         int slot = SlotOf(tankId);
         ref readonly TankState tank = ref _sim.Tanks[slot];
         if (tank.Team == team || tank.IsDestroyed) return true;
@@ -88,15 +118,63 @@ public sealed class VisionTracker
     /// </summary>
     public Sighting Inspect(int observerId, int targetId)
     {
+        FollowSlots(_sim.Tanks);
         int target = SlotOf(targetId);
         return Spotting.Look(_sim.Tanks[SlotOf(observerId)], _sim.Tanks[target], _sim.Field, _sim.Tick, _moving[target]);
     }
 
-    private int SlotOf(int tankId)
+    /// <summary>
+    /// Brings the per-slot rows into line with the simulation's slots. Nothing to do while the same tanks sit in the same
+    /// slots; otherwise each tank's row is found by its id and moved to its new slot (a tank that is new gets a fresh
+    /// row: unseen, watching, still). Swaps the row arrays with scratch ones rather than allocating.
+    /// </summary>
+    private void FollowSlots(ReadOnlySpan<TankState> tanks)
     {
-        ReadOnlySpan<TankState> tanks = _sim.Tanks;
+        bool same = tanks.Length == _count;
+        for (int i = 0; same && i < tanks.Length; i++) same = _ids[i] == tanks[i].Id;
+        if (same) return;
         for (int i = 0; i < tanks.Length; i++)
-            if (tanks[i].Id == tankId) return i;
-        throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
+        {
+            int old = OldSlotOf(tanks[i].Id);
+            if (old >= 0)
+            {
+                Array.Copy(_lastSeen, old * MaxTeams, _lastSeenScratch, i * MaxTeams, MaxTeams);
+                _blindScratch[i] = _blind[old];
+                _movingScratch[i] = _moving[old];
+                _lastYawScratch[i] = _lastYaw[old];
+                _wasWreckScratch[i] = _wasWreck[old];
+                _wreckAtScratch[i] = _wreckAt[old];
+            }
+            else
+            {
+                Array.Fill(_lastSeenScratch, NeverSeen, i * MaxTeams, MaxTeams);
+                _blindScratch[i] = false;
+                _movingScratch[i] = false;
+                _lastYawScratch[i] = tanks[i].Yaw;
+                _wasWreckScratch[i] = tanks[i].IsDestroyed;
+                _wreckAtScratch[i] = tanks[i].Position;
+            }
+        }
+        (_lastSeen, _lastSeenScratch) = (_lastSeenScratch, _lastSeen);
+        (_blind, _blindScratch) = (_blindScratch, _blind);
+        (_moving, _movingScratch) = (_movingScratch, _moving);
+        (_lastYaw, _lastYawScratch) = (_lastYawScratch, _lastYaw);
+        (_wasWreck, _wasWreckScratch) = (_wasWreckScratch, _wasWreck);
+        (_wreckAt, _wreckAtScratch) = (_wreckAtScratch, _wreckAt);
+        for (int i = 0; i < tanks.Length; i++) _ids[i] = tanks[i].Id;
+        _count = tanks.Length;
+    }
+
+    private int OldSlotOf(int tankId)
+    {
+        for (int i = 0; i < _count; i++)
+            if (_ids[i] == tankId) return i;
+        return -1;
+    }
+
+    private int SlotOf(int tankId)
+    {
+        int slot = _sim.SlotOf(tankId);
+        return slot >= 0 ? slot : throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
     }
 }
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 591、Net 50。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): tanks come and go — RemoveTank, Respawn, spawn protection, 16 teams; vision follows tanks across slots"
```

---

### Task 2：一辆车走一步、地图上线

设计 §3.4、§5.3。`Vehicles/TankStep.Advance` 把 `Simulation.Step` 里一辆车的那一步（输入清洗、驾驶、炮塔、开炮判定）拆成公共静态方法，服务器和客户端预测共用；`Simulation.Step` 改为调用它。`TankMotion.Drive` 加 `knockDown` 开关：客户端预测时不撞倒物件（物件状态由服务器说了算），撞砖墙的减速只在进入时算一次（原来每 tick 都减半，不撞倒时会一直减）。`Battlefield.SetDurability` 公开，客户端按服务器发来的物件状态改。`Maps/MapData`：地图的上线形式——高度量化到厘米（ARM 和 x64 的浮点结果有差异，所以地图不在两端各算一遍，而是服务器算好发过去）、物件、边界；`Of(field)` / `Build()` 往返。

**Files:**
- Create: `src/Tank.Sim/Vehicles/TankStep.cs`、`src/Tank.Sim/Maps/MapData.cs`
- Modify: `src/Tank.Sim/Simulation.cs`、`Vehicles/TankMotion.cs`、`Maps/Battlefield.cs`
- Test: 新建 `tests/Tank.Sim.Tests/TankStepTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/TankStepTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>Stepping one tank outside the simulation, as a client predicting its own tank does (stage 4 design §3).</summary>
public sealed class TankStepTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Drive north, swinging the turret about and firing whenever loaded: everything Advance touches.</summary>
    private static InputCmd Busy(int tick) => new(tick, (sbyte)(tick % 90 < 60 ? 1 : -1), (sbyte)(tick % 40 < 10 ? 1 : 0),
        Angles.Deg(tick % 120 - 60), Angles.Deg(5), tick % 7 == 0, tick < 6 * Tps ? AmmoType.AP : AmmoType.HE);

    [Fact]
    public void Advance_steps_a_tank_exactly_as_the_simulation_does()
    {
        Battlefield field = SandboxMap.Create();
        var sim = new Simulation(field);
        int id = sim.AddTank(TankCatalog.Medium, SandboxMap.Spawn, SandboxMap.SpawnYaw);
        TankState mine = sim.Tanks[0];
        var inputs = new InputCmd[1];
        int shots = 0;
        for (int tick = 1; tick <= 10 * Tps; tick++)
        {
            inputs[0] = Busy(tick);
            sim.Step(inputs);
            GameEvent[] events = sim.Events.ToArray();
            bool fired = TankStep.Advance(ref mine, inputs[0], tick, field, ReadOnlySpan<Obb2>.Empty, knockDown: true, null, canFire: true, out TankStep.Shot shot);
            if (fired)
            {
                shots++;
                GameEvent e = Assert.Single(events, e => e.Type == GameEventType.ShellFired);
                Assert.Equal(shot.Shell.Position, e.Position);
                Assert.Equal(shot.Shell.Velocity, e.Velocity);
                Assert.Equal(shot.Shell.Ammo, e.Ammo);
                Assert.Equal(0, shot.Shell.Id);
            }
            else Assert.DoesNotContain(events, e => e.Type == GameEventType.ShellFired);
            Assert.Equal(sim.Tanks[0], mine);   // every field, every tick
        }
        Assert.True(shots >= 2, $"{shots} shots");
        Assert.Equal(id, mine.Id);
    }

    [Fact]
    public void Advance_leaves_a_wreck_alone_and_an_unarmed_or_protected_gun_silent()
    {
        var sim = new Simulation(TestWorld.FlatField());
        sim.AddTank(TankCatalog.Medium, new Vector2(100, 100), 0f);
        TankState wreck = sim.Tanks[0] with { IsDestroyed = true, Health = 0 };
        TankState before = wreck;
        Assert.False(TankStep.Advance(ref wreck, TestWorld.Cmd(throttle: 1, fire: true), 1, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, true, out _));
        Assert.Equal(before, wreck);

        TankState live = sim.Tanks[0];
        Assert.False(TankStep.Advance(ref live, TestWorld.Cmd(fire: true), 1, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, canFire: false, out _));
        Assert.Equal(0, live.ShotCount);
        Assert.True(TankStep.Advance(ref live, TestWorld.Cmd(fire: true), 2, sim.Field, ReadOnlySpan<Obb2>.Empty, true, null, canFire: true, out TankStep.Shot shot));
        Assert.Equal(1, live.ShotCount);
        Assert.Equal(2, live.LastFired);
        Assert.Equal(ShotRandom.Seed(2, live.Id, 0), shot.Shell.Seed);
        Assert.Equal(live.Spec.ReloadTime, live.Reload);
    }

    private static Prop Fence(int id, Vector2 at) => new WallLine(PropKind.Fence, at - new Vector2(3, 0), at + new Vector2(3, 0)).Segments(TestWorld.Flat(), id).First();

    [Fact]
    public void Without_knocking_down_a_tank_drives_through_a_fence_that_stays_standing()
    {
        Prop fence = Fence(1, new Vector2(100, 90));
        Battlefield server = TestWorld.FlatField(fence), client = TestWorld.FlatField(fence);
        TankState onServer = TestWorld.Tank(100, 100), onClient = onServer;
        var felled = new List<int>();
        for (int tick = 1; tick <= 3 * Tps; tick++)
        {
            InputCmd cmd = TestWorld.Cmd(throttle: 1);
            TankStep.Advance(ref onServer, cmd, tick, server, ReadOnlySpan<Obb2>.Empty, knockDown: true, felled, false, out _);
            TankStep.Advance(ref onClient, cmd, tick, client, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, false, out _);
            Assert.Equal(onServer.Position, onClient.Position);
            Assert.Equal(onServer.Speed, onClient.Speed);
        }
        Assert.Equal(new[] { fence.Id }, felled);
        Assert.False(server.IsStanding(fence.Id));
        Assert.True(client.IsStanding(fence.Id), "the client leaves it to the server");
        Assert.True(onClient.Position.Y < 86f, $"drove on through: {onClient.Position}");
    }

    /// <param name="runUp">Where the heavy tank starts, north of the wall at z = 60: 25 m reaches top speed, 13 m only ~7 m/s
    /// (just over the ram speed), the band where the break leaves it under ram speed inside the wall.</param>
    [Theory]
    [InlineData(85f)]
    [InlineData(73f)]
    public void Without_knocking_down_a_ramming_heavy_tank_passes_a_brick_wall_at_half_speed_and_a_slow_one_is_stopped(float runUp)
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(94, 60), new Vector2(106, 60)).Segments(TestWorld.Flat(), 1).ElementAt(2);
        Battlefield server = TestWorld.FlatField(wall), client = TestWorld.FlatField(wall);
        TankState onServer = TestWorld.Tank(100, runUp) with { Spec = TankCatalog.Heavy }, onClient = onServer;
        int brokeAt = -1;
        float serverSpeedBefore = 0f;
        for (int tick = 1; tick <= 6 * Tps; tick++)
        {
            InputCmd cmd = TestWorld.Cmd(throttle: 1);
            if (server.IsStanding(wall.Id)) serverSpeedBefore = onServer.Speed;
            TankStep.Advance(ref onServer, cmd, tick, server, ReadOnlySpan<Obb2>.Empty, knockDown: true, null, false, out _);
            TankStep.Advance(ref onClient, cmd, tick, client, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, false, out _);
            if (brokeAt < 0 && !server.IsStanding(wall.Id))
            {
                brokeAt = tick;
                Assert.True(onServer.Speed < serverSpeedBefore * 0.6f, "the break costs half the speed on the server");
                Assert.Equal(onServer.Speed, onClient.Speed);   // and the same on the client
            }
            // Identical until the wall is down; then the server's tank crosses rubble (slower) while the client's wall still
            // stands, until the server's word arrives: the two drift apart a little there, which reconciliation smooths out.
            if (brokeAt < 0) Assert.Equal(onServer.Position, onClient.Position);
        }
        Assert.True(brokeAt > 0, "never broke through");
        Assert.True(client.IsStanding(wall.Id));
        Assert.True(onClient.Position.Y < 56f, $"broke through: {onClient.Position}");
        Assert.True(Vector2.Distance(onServer.Position, onClient.Position) < 3f, $"server {onServer.Position} vs client {onClient.Position}");

        Battlefield slow = TestWorld.FlatField(wall);
        TankState medium = TestWorld.Tank(100, 70);
        for (int tick = 1; tick <= 3 * Tps; tick++) TankStep.Advance(ref medium, TestWorld.Cmd(throttle: 1), tick, slow, ReadOnlySpan<Obb2>.Empty, knockDown: false, null, false, out _);
        Assert.True(medium.Position.Y > 62f, $"a medium tank is stopped by the wall: {medium.Position}");
        Assert.True(slow.IsStanding(wall.Id));
    }

    [Fact]
    public void SetDurability_puts_a_prop_down_or_back_up_from_outside()
    {
        Prop wall = new WallLine(PropKind.BrickWall, new Vector2(98, 60), new Vector2(102, 60)).Segments(TestWorld.Flat(), 1).First();
        var rock = new Prop(5, PropKind.Rock, new Obb2(new Vector2(50, 50), new Vector2(3, 3), 0), -1f, 4f);
        Battlefield field = TestWorld.FlatField(wall, rock);
        field.SetDurability(wall.Id, 120);
        Assert.True(field.IsStanding(wall.Id));
        Assert.Equal(120, field.Durability(wall.Id));
        field.SetDurability(wall.Id, 0);
        Assert.False(field.IsStanding(wall.Id));
        Assert.False(field.Raycast(new Vector3(100, 1, 80), new Vector3(100, 1, 40), out _), "rubble doesn't stop rays");
        field.SetDurability(wall.Id, wall.MaxDurability);
        Assert.True(field.IsStanding(wall.Id));
        Assert.True(field.Raycast(new Vector3(100, 1, 80), new Vector3(100, 1, 40), out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => field.SetDurability(wall.Id, wall.MaxDurability + 1));
        Assert.Throws<InvalidOperationException>(() => field.SetDurability(rock.Id, 0));
    }

    [Fact]
    public void Map_data_rebuilds_the_same_battlefield_and_is_stable_under_a_round_trip()
    {
        Battlefield original = SandboxMap.Create();
        MapData data = MapData.Of(original);
        Battlefield rebuilt = data.Build();
        MapData again = MapData.Of(rebuilt);
        Assert.Equal(data.HeightsCm, again.HeightsCm);
        Assert.Equal(data.Props, again.Props);
        Assert.Equal(original.Props, rebuilt.Props);
        Assert.Equal(original.Border, rebuilt.Border);
        Assert.Equal(original.Terrain.SizeX, rebuilt.Terrain.SizeX);
        for (float x = 0; x <= 400; x += 37f)
        for (float z = 0; z <= 400; z += 41f)
            Assert.Equal(original.Terrain.HeightAt(x, z), rebuilt.Terrain.HeightAt(x, z), 0.011f);   // within a centimetre
        Assert.All(rebuilt.Props, p => Assert.True(rebuilt.IsStanding(p.Id)));
        Assert.Throws<ArgumentException>(() => new MapData(3, 3, 2f, new int[8], Array.Empty<Prop>(), 4f));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`TankStep`、`MapData`、`SetDurability` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/Battlefield.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/Battlefield.cs b/src/Tank.Sim/Maps/Battlefield.cs
index 5b9314a..1941bbd 100644
--- a/src/Tank.Sim/Maps/Battlefield.cs
+++ b/src/Tank.Sim/Maps/Battlefield.cs
@@ -13,9 +13,10 @@ public readonly record struct RayHit(float T, Vector3 Point, int PropId)
 /// <summary>
 /// Terrain, the props on it and the rectangle tanks may drive in. Props keep their shapes; what changes as the battle
 /// goes on is their state: whether each still stands, and its hit points. Only the simulation changes it
-/// (<see cref="Simulation"/> and the <see cref="Vehicles.TankMotion.Drive"/> it calls), so a battlefield belongs to one
-/// battle: it is not safe to share between simulations, nor to reuse once the battle is over. Every query here (rays,
-/// tank collision, bushes) sees the props as they stand now.
+/// (<see cref="Simulation"/> and the <see cref="Vehicles.TankMotion.Drive"/> it calls), or a network client following
+/// the server's word (<see cref="SetDurability"/>), so a battlefield belongs to one battle: it is not safe to share
+/// between simulations, nor to reuse once the battle is over. Every query here (rays, tank collision, bushes) sees the
+/// props as they stand now.
 /// </summary>
 public sealed class Battlefield
 {
@@ -71,6 +72,20 @@ public sealed class Battlefield
     /// <summary>Hit points left: <see cref="Prop.MaxDurability"/> when intact, 0 once destroyed (and always 0 for a rock, which can't be).</summary>
     public int Durability(int propId) => _durability[IndexOf(propId)];
 
+    /// <summary>
+    /// Sets a destructible prop's hit points from outside the simulation (a client applying the server's prop state):
+    /// 0 puts it down, anything else up to <see cref="Prop.MaxDurability"/> leaves it standing with that many left.
+    /// </summary>
+    public void SetDurability(int propId, int durability)
+    {
+        int index = IndexOf(propId);
+        RequireDestructible(index);
+        int max = _props[index].MaxDurability;
+        if (durability < 0 || durability > max) throw new ArgumentOutOfRangeException(nameof(durability), durability, $"Prop {propId} has 0 … {max} hit points.");
+        _durability[index] = durability;
+        _standing[index] = durability > 0;
+    }
+
     public Vector2 ClampToPlayable(Vector2 p) =>
         Vector2.Clamp(p, new Vector2(Border, Border), new Vector2(Terrain.SizeX - Border, Terrain.SizeZ - Border));
 
PATCH
```

新建 `src/Tank.Sim/Maps/MapData.cs`：

```csharp
using Tank.Sim.Terrain;

namespace Tank.Sim.Maps;

/// <summary>
/// A battlefield's fixed shape as data (stage 4 design §5.4): the terrain's heights in whole centimetres, the props and
/// the border. The server builds its battlefield from this and sends the same data to its clients, who build theirs
/// from it too, so every machine has the same ground and the same props under the same ids, whatever its floating
/// point does to the map generator. Rounding the heights to centimetres happens once, in <see cref="Of"/>; building a
/// battlefield and taking its data again gives the same data back.
/// </summary>
public sealed class MapData
{
    public const float HeightUnit = 0.01f;

    public MapData(int samplesX, int samplesZ, float cellSize, int[] heightsCm, Prop[] props, float border)
    {
        if (heightsCm.Length != samplesX * samplesZ)
            throw new ArgumentException($"Expected {samplesX * samplesZ} heights, got {heightsCm.Length}.", nameof(heightsCm));
        (SamplesX, SamplesZ, CellSize, HeightsCm, Props, Border) = (samplesX, samplesZ, cellSize, heightsCm, props, border);
    }

    public int SamplesX { get; }
    public int SamplesZ { get; }
    public float CellSize { get; }

    /// <summary>Sample (i, j)'s height in centimetres at index <c>j * SamplesX + i</c>, as <see cref="HeightField"/> lays them out.</summary>
    public int[] HeightsCm { get; }

    public Prop[] Props { get; }
    public float Border { get; }

    /// <summary>The data of <paramref name="field"/>, its heights rounded to the nearest centimetre.</summary>
    public static MapData Of(Battlefield field)
    {
        HeightField terrain = field.Terrain;
        var heights = new int[terrain.SamplesX * terrain.SamplesZ];
        for (int j = 0; j < terrain.SamplesZ; j++)
        for (int i = 0; i < terrain.SamplesX; i++)
            heights[j * terrain.SamplesX + i] = (int)MathF.Round(terrain.Sample(i, j) / HeightUnit);
        return new MapData(terrain.SamplesX, terrain.SamplesZ, terrain.CellSize, heights, field.Props.ToArray(), field.Border);
    }

    /// <summary>A fresh battlefield (every prop standing at full hit points) from this data.</summary>
    public Battlefield Build()
    {
        var heights = new float[HeightsCm.Length];
        for (int k = 0; k < heights.Length; k++) heights[k] = HeightsCm[k] * HeightUnit;
        return new Battlefield(new HeightField(SamplesX, SamplesZ, CellSize, heights), Props, Border);
    }
}
```

修改 `src/Tank.Sim/Simulation.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Simulation.cs b/src/Tank.Sim/Simulation.cs
index cfca550..041be4d 100644
--- a/src/Tank.Sim/Simulation.cs
+++ b/src/Tank.Sim/Simulation.cs
@@ -26,9 +26,6 @@ public sealed partial class Simulation
     public const int MaxTanks = 32;
     public const int MaxShells = 256;
 
-    /// <summary>Float drift in the countdown must not cost an extra tick of reload.</summary>
-    private const float ReloadEpsilon = 1e-4f;
-
     private readonly TankState[] _tanks = new TankState[MaxTanks];
     private readonly Shell[] _shells = new Shell[MaxShells];
     private readonly Obb2[] _footprints = new Obb2[MaxTanks];
@@ -152,13 +149,10 @@ public sealed partial class Simulation
         {
             ref TankState tank = ref _tanks[i];
             if (tank.IsDestroyed) continue;   // wrecks don't drive, aim or fire, but still block (see SnapshotFootprints)
-            InputCmd input = TankMotion.TraverseHull(tank, Sanitize(inputs[i], tank), Field.Terrain);
-            float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
-            TankMotion.Drive(ref tank, input, Field, FootprintsExcept(i), _felled);
+            bool fired = TankStep.Advance(ref tank, inputs[i], Tick, Field, FootprintsExcept(i), knockDown: true, _felled,
+                canFire: !IsProtected(tank) && ShellCount < MaxShells, out TankStep.Shot shot);
             ReportFelled(tank);
-            TankMotion.AimGun(ref tank, input, Field.Terrain);
-            UpdateBloom(ref tank, yaw, turretYaw);
-            TryFire(ref tank, input);
+            if (fired) Launch(tank, shot);
         }
         StepShells();
     }
@@ -194,29 +188,8 @@ public sealed partial class Simulation
         throw new ArgumentException($"No tank {tankId}.", nameof(tankId));
     }
 
-    /// <summary>
-    /// Inputs come from the network: clamp the axes, replace a non-finite aim with the turret's current heading so a bad
-    /// packet can't spin the turret, and keep the loaded round if the requested one isn't a known type.
-    /// </summary>
-    internal static InputCmd Sanitize(in InputCmd input, in TankState tank)
-    {
-        sbyte throttle = (sbyte)System.Math.Sign(input.Throttle);
-        sbyte steer = (sbyte)System.Math.Sign(input.Steer);
-        bool aimValid = float.IsFinite(input.AimYaw) && float.IsFinite(input.AimPitch);
-        float aimYaw = aimValid ? input.AimYaw : Angles.Wrap(tank.Yaw + tank.TurretYaw);
-        float aimPitch = aimValid ? input.AimPitch : 0f;
-        AmmoType ammo = input.Ammo is AmmoType.AP or AmmoType.HE ? input.Ammo : tank.Ammo;
-        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch, Ammo = ammo };
-    }
-
-    /// <summary>Opens the aim circle for this tick's driving, hull turn and turret turn, or lets it settle.</summary>
-    private static void UpdateBloom(ref TankState tank, float oldYaw, float oldTurretYaw)
-    {
-        float hullRate = Angles.Wrap(tank.Yaw - oldYaw) / SimConstants.Dt;
-        float turretRate = Angles.Wrap(tank.TurretYaw - oldTurretYaw) / SimConstants.Dt;
-        float target = Dispersion.Target(tank.Spec, tank.Speed, hullRate, turretRate);
-        tank.Bloom = Dispersion.Step(tank.Bloom, target, tank.Spec.AimTime);
-    }
+    /// <summary>See <see cref="TankStep.Sanitize"/>.</summary>
+    internal static InputCmd Sanitize(in InputCmd input, in TankState tank) => TankStep.Sanitize(input, tank);
 
     /// <summary>Every tank collides against where the others were at the start of the tick, independent of slot order.</summary>
     private void SnapshotFootprints()
@@ -237,50 +210,18 @@ public sealed partial class Simulation
     }
 
     /// <summary>
-    /// Counts the reload down, swaps rounds if the input asks for the other type (which restarts the reload), then fires
-    /// if asked, loaded, not under protection, and a round of that type is left. The shell leaves inside the aim circle,
-    /// which then opens.
+    /// The shell a tank just fired (<see cref="TankStep.Advance"/>) gets its id and leaves the muzzle, or, when the barrel
+    /// was pushed into a rock, a wall or the ground, bursts where the barrel meets it instead of spawning inside (a wall
+    /// takes it as a direct hit). That goes off in the shell phase, so every tank has had its turn before the splash,
+    /// whatever the slots.
     /// </summary>
-    private void TryFire(ref TankState tank, in InputCmd input)
+    private void Launch(in TankState tank, in TankStep.Shot shot)
     {
-        tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
-        if (tank.Reload < ReloadEpsilon) tank.Reload = 0f;
-        if (input.Ammo != tank.Ammo)
-        {
-            tank.Ammo = input.Ammo;
-            tank.Reload = tank.Spec.ReloadTime;
-            return;
-        }
-        if (!input.Fire || tank.Reload > 0 || tank.AmmoLeft(tank.Ammo) == 0 || IsProtected(tank) || ShellCount == MaxShells) return;
-
-        Ballistics.Launch launch = Ballistics.Aim(tank, Field);
-        uint seed = ShotRandom.Seed(Tick, tank.Id, tank.ShotCount);
-        float speed = launch.Velocity.Length();
-        var shell = new Shell
-        {
-            Id = _nextId++,
-            OwnerTankId = tank.Id,
-            Position = launch.Origin,
-            Velocity = Dispersion.Deflect(launch.Velocity, Dispersion.Radius(tank.Spec, tank.Bloom), seed) * speed,
-            Ammo = tank.Ammo,
-            Round = tank.Spec.Round(tank.Ammo),
-            Caliber = tank.Spec.Caliber,
-            Seed = seed,
-        };
-        tank.Reload = tank.Spec.ReloadTime;
-        tank.ShotCount++;
-        tank.LastFired = Tick;
-        tank.Bloom += tank.Spec.Bloom.Fire;
-        if (tank.Ammo == AmmoType.HE) tank.HeLeft--;
-        else tank.ApLeft--;
+        Shell shell = shot.Shell with { Id = _nextId++ };
         _events.Add(new GameEvent(GameEventType.ShellFired, Tick, tank.Id, shell.Id, shell.Position, shell.Velocity, Ammo: shell.Ammo));
-
-        // Barrel pushed into a rock, a wall or the ground: the shell bursts where the barrel meets it instead of spawning
-        // inside (a wall takes it as a direct hit). It goes off in the shell phase, so every tank has had its turn before
-        // the splash, whatever the slots.
-        if (launch.Blocked)
+        if (shot.Launch.Blocked)
         {
-            _barrelBursts[_barrelBurstCount++] = new BarrelBurst(shell, launch.BlockPoint, launch.BlockPropId);
+            _barrelBursts[_barrelBurstCount++] = new BarrelBurst(shell, shot.Launch.BlockPoint, shot.Launch.BlockPropId);
             return;
         }
         _shells[ShellCount++] = shell;
PATCH
```

修改 `src/Tank.Sim/Vehicles/TankMotion.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Vehicles/TankMotion.cs b/src/Tank.Sim/Vehicles/TankMotion.cs
index 3fbb682..c7b1b5d 100644
--- a/src/Tank.Sim/Vehicles/TankMotion.cs
+++ b/src/Tank.Sim/Vehicles/TankMotion.cs
@@ -33,14 +33,19 @@ public static class TankMotion
     public static Obb2 Footprint(TankSpec spec, Vector2 center, float yaw) =>
         new(center, new Vector2(spec.HullWidth / 2, spec.HullLength / 2), yaw);
 
+    /// <summary>At most this many props give way to one hull in one tick (a hull spans a few 2 m segments at most).</summary>
+    private const int MaxGivingWay = 16;
+
     /// <summary>
     /// Applies throttle and steering, knocks down what gives way where the hull is going (any fence it touches, and brick
     /// walls when it is faster than its <see cref="TankSpec.BrickWallRamSpeed"/>, at the cost of half its speed; their ids
     /// are added to <paramref name="felled"/> if given), then moves the hull and pushes it out of standing solid props and
     /// <paramref name="obstacles"/>. Knocking props down changes <paramref name="field"/> for everyone using it, so only
-    /// the simulation stepping the battle should drive on it (a client predicting its own tank must not).
+    /// the simulation stepping the battle drives with <paramref name="knockDown"/>; a client predicting its own tank
+    /// drives without it: the props that would give way are then driven through as if down (the speed still halved for a
+    /// brick wall) but left standing, and whether they really fell is the server's to say.
     /// </summary>
-    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles, List<int>? felled = null)
+    public static void Drive(ref TankState t, in InputCmd input, Battlefield field, ReadOnlySpan<Obb2> obstacles, List<int>? felled = null, bool knockDown = true)
     {
         TankSpec spec = t.Spec;
         const float dt = SimConstants.Dt;
@@ -55,10 +60,12 @@ public static class TankMotion
 
         Vector2 start = t.Position;
         Vector2 intended = Angles.Forward(t.Yaw) * (t.Speed * dt);
-        KnockDown(ref t, Footprint(spec, start + intended, t.Yaw), field, felled);
-        Vector2 dest = field.ClampToPlayable(Resolve(Footprint(spec, start + intended, t.Yaw), field, obstacles));
+        Span<int> ignored = stackalloc int[MaxGivingWay];
+        int ignoredCount = KnockDown(ref t, Footprint(spec, start, oldYaw), Footprint(spec, start + intended, t.Yaw), field, felled, knockDown, ignored);
+        ReadOnlySpan<int> through = ignored[..ignoredCount];
+        Vector2 dest = field.ClampToPlayable(Resolve(Footprint(spec, start + intended, t.Yaw), field, obstacles, through));
         if (IsTooSteepToEnter(spec, field.Terrain, start, dest)) dest = start;
-        (t.Position, t.Yaw) = SettleWithoutOverlap(spec, dest, t.Yaw, oldYaw, start, field, obstacles);
+        (t.Position, t.Yaw) = SettleWithoutOverlap(spec, dest, t.Yaw, oldYaw, start, field, obstacles, through);
 
         // Pushed almost straight back (head-on into a rock, a standing wall or fence, a tank or the map edge): stall.
         // A glancing contact keeps its speed so the hull slides along the obstacle.
@@ -115,24 +122,47 @@ public static class TankMotion
     }
 
     /// <summary>
-    /// What gives way to a hull about to stand on <paramref name="hull"/> (design §4.2): any fence, and brick walls when it
-    /// is faster than its <see cref="TankSpec.BrickWallRamSpeed"/>, which then costs it half its speed (once, however many
-    /// it breaks). They go down before the hull is pushed out of what still stands, so it drives on through.
+    /// What gives way to a hull about to move from <paramref name="from"/> onto <paramref name="hull"/> (design §4.2): any
+    /// fence, and brick walls when it is faster than its <see cref="TankSpec.BrickWallRamSpeed"/>, which costs it half its
+    /// speed as it breaks into one (once, however many at a time). With <paramref name="knockDown"/> they go down before
+    /// the hull is pushed out of what still stands, so it drives on through; without it they are left standing and their
+    /// indices put in <paramref name="ignored"/> (as many as fit), for the collision to drive through them all the same.
+    /// A standing wall the hull is already inside can only be one it broke into without knocking down (the break cost it
+    /// half its speed, so it may now be under ram speed): it still gives way, at no further cost, or the hull would be
+    /// pushed back out and stall where the server drives on.
     /// </summary>
-    private static void KnockDown(ref TankState t, in Obb2 hull, Battlefield field, List<int>? felled)
+    /// <returns>How many of <paramref name="ignored"/> were filled.</returns>
+    private static int KnockDown(ref TankState t, in Obb2 from, in Obb2 hull, Battlefield field, List<int>? felled, bool knockDown, Span<int> ignored)
     {
         bool ramming = MathF.Abs(t.Speed) > t.Spec.BrickWallRamSpeed;
         bool broke = false;
+        int count = 0;
         foreach (int i in field.Blockers)
         {
             ref readonly Prop prop = ref field.PropAt(i);
-            bool givesWay = prop.Kind == PropKind.Fence || (ramming && prop.Kind == PropKind.BrickWall);
-            if (!givesWay || !field.StandingAt(i) || !Overlaps(hull, prop.Footprint)) continue;
-            field.Destroy(i);
-            felled?.Add(prop.Id);
-            broke |= prop.Kind == PropKind.BrickWall;
+            if (!field.StandingAt(i) || !Overlaps(hull, prop.Footprint)) continue;
+            bool inside = prop.Kind == PropKind.BrickWall && Overlaps(from, prop.Footprint);
+            bool givesWay = prop.Kind == PropKind.Fence || prop.Kind == PropKind.BrickWall && (ramming || (!knockDown && inside));
+            if (!givesWay) continue;
+            if (knockDown)
+            {
+                field.Destroy(i);
+                felled?.Add(prop.Id);
+            }
+            else if (count < ignored.Length)
+            {
+                ignored[count++] = i;
+            }
+            broke |= prop.Kind == PropKind.BrickWall && !inside;
         }
         if (broke) t.Speed *= RamSpeedKept;
+        return count;
+    }
+
+    private static bool IsIgnored(int index, ReadOnlySpan<int> ignored)
+    {
+        foreach (int i in ignored) if (i == index) return true;
+        return false;
     }
 
     /// <summary>The hull lies partly on the rubble of a wall.</summary>
@@ -181,38 +211,38 @@ public static class TankMotion
     /// can still drive out (ties go to the earlier candidate).
     /// </summary>
     private static (Vector2 Position, float Yaw) SettleWithoutOverlap(
-        TankSpec spec, Vector2 dest, float yaw, float oldYaw, Vector2 oldPosition, Battlefield field, ReadOnlySpan<Obb2> obstacles)
+        TankSpec spec, Vector2 dest, float yaw, float oldYaw, Vector2 oldPosition, Battlefield field, ReadOnlySpan<Obb2> obstacles, ReadOnlySpan<int> ignored)
     {
         Span<(Vector2 Position, float Yaw)> candidates = stackalloc (Vector2, float)[3] { (dest, yaw), (dest, oldYaw), (oldPosition, oldYaw) };
         var best = candidates[0];
         float bestDepth = float.MaxValue;
         foreach (var candidate in candidates)
         {
-            float depth = PenetrationDepth(Footprint(spec, candidate.Position, candidate.Yaw), field, obstacles);
+            float depth = PenetrationDepth(Footprint(spec, candidate.Position, candidate.Yaw), field, obstacles, ignored);
             if (depth <= Skin * 0.5f) return candidate;
             if (depth < bestDepth - TieDepth) (best, bestDepth) = (candidate, depth);
         }
         return best;
     }
 
-    /// <summary>Deepest overlap of <paramref name="box"/> with any standing solid prop or obstacle; 0 when clear.</summary>
-    private static float PenetrationDepth(in Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
+    /// <summary>Deepest overlap of <paramref name="box"/> with any standing solid prop (but those <paramref name="ignored"/>) or obstacle; 0 when clear.</summary>
+    private static float PenetrationDepth(in Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles, ReadOnlySpan<int> ignored)
     {
         float deepest = 0f;
         foreach (int i in field.Blockers)
-            if (field.StandingAt(i) && Obb2.TryGetPenetration(box, field.PropAt(i).Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
+            if (field.StandingAt(i) && !IsIgnored(i, ignored) && Obb2.TryGetPenetration(box, field.PropAt(i).Footprint, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
         foreach (Obb2 other in obstacles)
             if (Obb2.TryGetPenetration(box, other, out Vector2 push)) deepest = MathF.Max(deepest, push.Length());
         return deepest;
     }
 
-    private static Vector2 Resolve(Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles)
+    private static Vector2 Resolve(Obb2 box, Battlefield field, ReadOnlySpan<Obb2> obstacles, ReadOnlySpan<int> ignored)
     {
         for (int iteration = 0; iteration < CollisionIterations; iteration++)
         {
             bool pushed = false;
             foreach (int i in field.Blockers)
-                if (field.StandingAt(i)) pushed |= PushOut(ref box, field.PropAt(i).Footprint);   // rubble, fallen fences and bushes don't block
+                if (field.StandingAt(i) && !IsIgnored(i, ignored)) pushed |= PushOut(ref box, field.PropAt(i).Footprint);   // rubble, fallen fences and bushes don't block
             foreach (Obb2 other in obstacles) pushed |= PushOut(ref box, other);
             if (!pushed) break;
         }
PATCH
```

新建 `src/Tank.Sim/Vehicles/TankStep.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;

namespace Tank.Sim.Vehicles;

/// <summary>
/// One tank's turn in a tick, apart from the shells (stage 4 design §3): the same code steps a tank on the server
/// (<see cref="Simulation.Step"/>) and predicts the player's own tank on a client, so a prediction only ever differs from
/// the server where the inputs did. In order: the input is cleaned up (<see cref="Sanitize"/>), a casemate's hull turns
/// toward an aim outside its arc, the hull drives (with or without knocking props down, see
/// <see cref="TankMotion.Drive"/>), the turret and gun turn, the aim circle opens or settles, the gun reloads or swaps
/// rounds, and it fires if asked and able. Firing changes the tank (reload, shot count, last fired, bloom, ammunition)
/// and hands back the <see cref="Shot"/>: the shell without an id, and the gun's launch (blocked barrel and all); what
/// becomes of the shell is the simulation's business.
/// </summary>
public static class TankStep
{
    /// <summary>Float drift in the countdown must not cost an extra tick of reload.</summary>
    private const float ReloadEpsilon = 1e-4f;

    /// <summary>A shot just fired: the shell as it leaves (its <c>Id</c> 0, for the simulation to give), and the gun's launch.</summary>
    public readonly record struct Shot(Shell Shell, Ballistics.Launch Launch);

    /// <param name="tank">The tank; a wreck is left alone.</param>
    /// <param name="tick">The tick being stepped (the shot's seed depends on it).</param>
    /// <param name="others">The other tanks' footprints, to collide with.</param>
    /// <param name="knockDown">Whether driving knocks fences and rammed brick walls down (the server) or only drives through them (a client predicting).</param>
    /// <param name="felled">Collects the ids of props knocked down, if given.</param>
    /// <param name="canFire">Whether the host lets the gun fire this tick (no spawn protection, room for a shell).</param>
    /// <param name="shot">The shot, when it fired.</param>
    /// <returns>Whether the gun fired.</returns>
    public static bool Advance(ref TankState tank, in InputCmd input, int tick, Battlefield field, ReadOnlySpan<Obb2> others,
        bool knockDown, List<int>? felled, bool canFire, out Shot shot)
    {
        shot = default;
        if (tank.IsDestroyed) return false;
        InputCmd clean = TankMotion.TraverseHull(tank, Sanitize(input, tank), field.Terrain);
        float yaw = tank.Yaw, turretYaw = tank.TurretYaw;
        TankMotion.Drive(ref tank, clean, field, others, felled, knockDown);
        TankMotion.AimGun(ref tank, clean, field.Terrain);
        UpdateBloom(ref tank, yaw, turretYaw);
        return Reload(ref tank, clean) && canFire && Fire(ref tank, tick, field, out shot);
    }

    /// <summary>
    /// Inputs come from the network: clamp the axes, replace a non-finite aim with the turret's current heading so a bad
    /// packet can't spin the turret, and keep the loaded round if the requested one isn't a known type.
    /// </summary>
    public static InputCmd Sanitize(in InputCmd input, in TankState tank)
    {
        sbyte throttle = (sbyte)System.Math.Sign(input.Throttle);
        sbyte steer = (sbyte)System.Math.Sign(input.Steer);
        bool aimValid = float.IsFinite(input.AimYaw) && float.IsFinite(input.AimPitch);
        float aimYaw = aimValid ? input.AimYaw : Angles.Wrap(tank.Yaw + tank.TurretYaw);
        float aimPitch = aimValid ? input.AimPitch : 0f;
        AmmoType ammo = input.Ammo is AmmoType.AP or AmmoType.HE ? input.Ammo : tank.Ammo;
        return input with { Throttle = throttle, Steer = steer, AimYaw = aimYaw, AimPitch = aimPitch, Ammo = ammo };
    }

    /// <summary>Opens the aim circle for this tick's driving, hull turn and turret turn, or lets it settle.</summary>
    private static void UpdateBloom(ref TankState tank, float oldYaw, float oldTurretYaw)
    {
        float hullRate = Angles.Wrap(tank.Yaw - oldYaw) / SimConstants.Dt;
        float turretRate = Angles.Wrap(tank.TurretYaw - oldTurretYaw) / SimConstants.Dt;
        float target = Dispersion.Target(tank.Spec, tank.Speed, hullRate, turretRate);
        tank.Bloom = Dispersion.Step(tank.Bloom, target, tank.Spec.AimTime);
    }

    /// <summary>
    /// Counts the reload down, swaps rounds if the input asks for the other type (which restarts the reload) and says
    /// whether the gun wants to fire: asked, loaded, a round of that type left.
    /// </summary>
    private static bool Reload(ref TankState tank, in InputCmd input)
    {
        tank.Reload = MathF.Max(0, tank.Reload - SimConstants.Dt);
        if (tank.Reload < ReloadEpsilon) tank.Reload = 0f;
        if (input.Ammo != tank.Ammo)
        {
            tank.Ammo = input.Ammo;
            tank.Reload = tank.Spec.ReloadTime;
            return false;
        }
        return input.Fire && tank.Reload == 0 && tank.AmmoLeft(tank.Ammo) > 0;
    }

    /// <summary>The gun fires: the shell leaves inside the aim circle, which then opens; the reload starts over.</summary>
    private static bool Fire(ref TankState tank, int tick, Battlefield field, out Shot shot)
    {
        Ballistics.Launch launch = Ballistics.Aim(tank, field);
        uint seed = ShotRandom.Seed(tick, tank.Id, tank.ShotCount);
        float speed = launch.Velocity.Length();
        var shell = new Shell
        {
            OwnerTankId = tank.Id,
            Position = launch.Origin,
            Velocity = Dispersion.Deflect(launch.Velocity, Dispersion.Radius(tank.Spec, tank.Bloom), seed) * speed,
            Ammo = tank.Ammo,
            Round = tank.Spec.Round(tank.Ammo),
            Caliber = tank.Spec.Caliber,
            Seed = seed,
        };
        tank.Reload = tank.Spec.ReloadTime;
        tank.ShotCount++;
        tank.LastFired = tick;
        tank.Bloom += tank.Spec.Bloom.Fire;
        if (tank.Ammo == AmmoType.HE) tank.HeLeft--;
        else tank.ApLeft--;
        shot = new Shot(shell, launch);
        return true;
    }
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Client/Tank.Client.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 598、Net 50；客户端 0 警告 0 错误。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): one tank's step as a public unit (TankStep.Advance), driving without knocking props down, prop state set from outside, map data for the wire"
```

---

### Task 3：对局规则和出生点

设计 §2.1～§2.3。`Modes/BattleMode`：三种模式；`ModeRules`：10 人、至少 2 人开局、保护 150 tick、复活 300 tick、结算 240 tick、团队战 7 分钟、大乱斗 10 分钟、哪些模式有队伍 / 复活 / 中途加入。`Modes/SpawnLayout`：两队的出生点和单人出生点。`Modes/MatchRules`：一局的状态机——`Join(slot, spec, team)` 挑出生点（第一个 8 m 内没有车也没有残骸的点，都占着就挑最空的）、加车、给保护；`Leave(slot)`（无复活模式里算淘汰）；`Update()` 每步处理击杀计分、复活、淘汰顺序、时间到或一方全灭时结束、名次（大乱斗按淘汰顺序，活着的按血量）；`Of(slot)` 给每个座位的 `Standing`。`SandboxMap.Spawns`（两队相距约 280 m、朝向对方；10 个单人点）和 `Waypoints`（20 个路点）。

**Files:**
- Create: `src/Tank.Sim/Modes/BattleMode.cs`、`SpawnLayout.cs`、`MatchRules.cs`
- Modify: `src/Tank.Sim/Maps/SandboxMap.cs`
- Test: 新建 `tests/Tank.Sim.Tests/MatchRulesTests.cs`、`SandboxSpawnTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/MatchRulesTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The three modes' rules on a flat field with close-set spawns, so kills come quickly (stage 4 design §2).</summary>
public sealed class MatchRulesTests
{
    private const int Tps = SimConstants.TickRate;

    /// <summary>Team 0 along z = 150 facing north, team 1 along z = 100 facing south, 50 m apart; solo spawns in a row.</summary>
    private static readonly SpawnLayout Layout = new(
        Team: Enumerable.Range(0, 5).Select(i => new SpawnPoint(new Vector2(60 + 20 * i, 150), 0f, 0))
            .Concat(Enumerable.Range(0, 5).Select(i => new SpawnPoint(new Vector2(60 + 20 * i, 100), MathF.PI, 1))).ToArray(),
        Solo: Enumerable.Range(0, 10).Select(i => new SpawnPoint(new Vector2(20 + 18 * i, 100 + (i % 2) * 50), (i % 2) * MathF.PI, 0)).ToArray());

    private sealed class Match
    {
        public readonly Simulation Sim = new(TestWorld.FlatField());
        public readonly MatchRules Rules;
        private readonly Dictionary<int, InputCmd> _orders = new();

        public Match(BattleMode mode) => Rules = new MatchRules(mode, Sim, Layout);

        public int Join(byte slot, byte team = 0, TankSpec? spec = null) => Rules.Join(slot, spec ?? TankCatalog.Medium, team);

        /// <summary>What a tank does every tick until told otherwise (default: hold still, gun ahead).</summary>
        public void Order(int tankId, InputCmd cmd) => _orders[tankId] = cmd;

        public List<GameEvent> Run(int ticks)
        {
            var events = new List<GameEvent>();
            var inputs = new InputCmd[Simulation.MaxTanks].AsSpan();
            for (int i = 0; i < ticks; i++)
            {
                inputs = inputs[..Sim.TankCount];
                for (int k = 0; k < inputs.Length; k++)
                {
                    TankState t = Sim.Tanks[k];
                    inputs[k] = _orders.TryGetValue(t.Id, out InputCmd cmd) ? cmd : new InputCmd(0, 0, 0, t.Yaw, 0f, false);
                }
                Sim.Step(inputs);
                Rules.Update();
                events.AddRange(Sim.Events);
            }
            return events;
        }

        /// <summary>Lays <paramref name="shooter"/>'s gun on <paramref name="target"/>'s hull front and keeps firing.</summary>
        public void Attack(int shooter, int target)
        {
            TankState s = Sim.Tanks[Sim.SlotOf(shooter)], t = Sim.Tanks[Sim.SlotOf(target)];
            var point = new Vector3(t.Position.X, 1.02f, t.Position.Y) + Vector3.Transform(new Vector3(0, 0, -3.1f), Quaternion.CreateFromAxisAngle(Vector3.UnitY, t.Yaw));
            var (yaw, pitch) = Angles.AimAngles(point - TankPose.Compute(s, Sim.Field.Terrain).Trunnion(s.Spec));
            Order(shooter, new InputCmd(0, 0, 0, yaw, pitch, true));
        }

        public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];

        public void Weaken(int id, int health) => Sim.TankRef(id).Health = health;

        /// <summary><paramref name="shooter"/> at (100, 150) facing north; the two others 40 m in front of it, 40 m apart, facing it.</summary>
        public void LineUp(int shooter, int first, int second)
        {
            Sim.TankRef(shooter).Position = new Vector2(100, 150);
            Sim.TankRef(shooter).Yaw = 0f;
            Sim.TankRef(first).Position = new Vector2(100, 110);
            Sim.TankRef(first).Yaw = MathF.PI;
            Sim.TankRef(second).Position = new Vector2(140, 110);
            Sim.TankRef(second).Yaw = MathF.PI;
        }
    }

    [Fact]
    public void Players_spawn_on_their_teams_points_facing_the_enemy_under_protection()
    {
        var m = new Match(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1), c = m.Join(2, 0);
        Assert.Equal(new Vector2(60, 150), m.Tank(a).Position);
        Assert.Equal(new Vector2(60, 100), m.Tank(b).Position);
        Assert.Equal(new Vector2(80, 150), m.Tank(c).Position);   // the next free point of team 0
        Assert.Equal(0f, m.Tank(a).Yaw);
        Assert.Equal(MathF.PI, m.Tank(b).Yaw);
        Assert.True(m.Sim.IsProtected(m.Tank(a)));
        Assert.Equal(1, m.Tank(b).Team);
        Assert.Equal(a, m.Rules.Of(0).TankId);
        Assert.True(m.Rules.Of(0).Alive);
        m.Run(ModeRules.ProtectionTicks);
        Assert.True(m.Sim.IsProtected(m.Tank(a)), "protected through the 150th step");
        m.Run(1);
        Assert.False(m.Sim.IsProtected(m.Tank(a)));
        Assert.Throws<InvalidOperationException>(() => m.Join(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => m.Join(3, 2));
    }

    [Fact]
    public void In_a_free_for_all_every_player_is_their_own_team()
    {
        var m = new Match(BattleMode.FreeForAll);
        for (byte slot = 0; slot < 10; slot++) m.Join(slot, team: 1);
        Assert.Equal(10, m.Sim.TankCount);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => (byte)i), m.Sim.Tanks.ToArray().Select(t => t.Team));
        Assert.Equal(Layout.Solo.Select(p => p.Position), m.Sim.Tanks.ToArray().Select(t => t.Position));
    }

    [Fact]
    public void A_kill_scores_for_the_killer_and_the_victim_respawns_protected_after_ten_seconds()
    {
        var m = new Match(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        int fell = m.Rules.Of(1).RespawnAt - ModeRules.RespawnTicks;
        Assert.True(fell > 0, "b was not knocked out");
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.False(m.Rules.Of(1).Alive);
        Assert.Equal(1, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.Of(1).Deaths);
        Assert.Equal(1, m.Rules.TeamScore(0));
        Assert.Equal(0, m.Rules.TeamScore(1));
        m.Order(a, new InputCmd(0, 0, 0, 0f, 0f, false));
        m.Run(fell + ModeRules.RespawnTicks - m.Sim.Tick - 1);
        Assert.True(m.Tank(b).IsDestroyed, "not yet");
        m.Run(1);   // tick RespawnAt: the rules put it back; the simulation reports it on the next step
        Assert.False(m.Tank(b).IsDestroyed);
        var events = m.Run(1);
        Assert.Contains(events, e => e.Type == GameEventType.TankRevived && e.TargetTankId == b);
        Assert.True(m.Rules.Of(1).Alive);
        Assert.True(m.Sim.IsProtected(m.Tank(b)));
        Assert.Equal(1, m.Tank(b).Team);
        Assert.Contains(m.Tank(b).Position, Layout.Team.Where(p => p.Team == 1).Select(p => p.Position));
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
    }

    [Fact]
    public void Killing_a_team_mate_scores_nothing()
    {
        var m = new Match(BattleMode.TeamBattle);
        int a = m.Join(0, 0), c = m.Join(1, 0);
        m.Sim.TankRef(c).Position = new Vector2(60, 100);   // move the mate into the line of fire
        m.Sim.TankRef(c).Yaw = MathF.PI;
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(2 * Tps);
        Assert.True(m.Tank(c).IsDestroyed);
        Assert.Equal(0, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.Of(1).Deaths);
        Assert.Equal(0, m.Rules.TeamScore(0));
    }

    [Fact]
    public void A_team_battle_ends_on_the_clock_for_the_higher_score_or_a_draw()
    {
        var m = new Match(BattleMode.TeamBattle);
        m.Join(0, 0);
        m.Join(1, 1);
        Assert.Equal(7 * 60 * Tps, m.Rules.TicksLeft);
        m.Run(m.Rules.TicksLeft - 1);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Run(1);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);   // 0 : 0
        Assert.Equal(m.Rules.EndTick, m.Rules.EndedAt);
        Assert.Equal(0, m.Rules.TicksLeft);

        var n = new Match(BattleMode.TeamBattle);
        int a = n.Join(0, 0), b = n.Join(1, 1);
        n.Run(ModeRules.ProtectionTicks + 1);
        n.Weaken(b, 1);
        n.Attack(a, b);
        n.Run(2 * Tps);
        n.Order(a, new InputCmd(0, 0, 0, 0f, 0f, false));
        n.Run(n.Rules.TicksLeft);
        Assert.Equal(0, n.Rules.WinnerTeam);
    }

    [Fact]
    public void A_leavers_kills_stay_on_the_team_score()
    {
        var m = new Match(BattleMode.TeamBattle);
        int a = m.Join(0, 0), b = m.Join(1, 1);
        m.Join(2, 0);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.Equal(1, m.Rules.Of(0).Kills);
        Assert.Equal(1, m.Rules.TeamScore(0));
        m.Rules.Leave(0);
        Assert.Equal(1, m.Rules.TeamScore(0));
        Assert.Equal(0, m.Rules.TeamScore(1));
        m.Run(m.Rules.TicksLeft);
        Assert.Equal(0, m.Rules.WinnerTeam);
    }

    [Fact]
    public void Annihilation_has_no_respawns_and_ends_when_a_team_is_wiped_out()
    {
        var m = new Match(BattleMode.Annihilation);
        int a = m.Join(0, 0), b = m.Join(1, 1), c = m.Join(2, 1);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(-1, m.Rules.Of(1).RespawnAt);
        Assert.True(m.Rules.Of(1).EliminatedAt > 0);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);   // c still stands
        m.Run(ModeRules.RespawnTicks + 10);
        Assert.True(m.Tank(b).IsDestroyed, "no respawn");
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(6 * Tps);   // a reload and the turret's swing
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerTeam);
        Assert.Equal(2, m.Rules.Of(0).Kills);
    }

    [Fact]
    public void Annihilation_on_the_clock_is_a_draw_and_a_leaver_counts_as_out()
    {
        var m = new Match(BattleMode.Annihilation);
        m.Join(0, 0);
        m.Join(1, 1);
        int c = m.Join(2, 1);
        m.Run(10);
        m.Rules.Leave(2);
        Assert.Equal(2, m.Sim.TankCount);
        Assert.Equal(-1, m.Sim.SlotOf(c));
        Assert.False(m.Rules.Of(2).Active);
        Assert.Equal(10, m.Rules.Of(2).EliminatedAt);
        Assert.Equal(1, m.Rules.AliveCount(1));
        Assert.False(m.Rules.AcceptsJoins);
        m.Run(m.Rules.TicksLeft);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
    }

    [Fact]
    public void A_free_for_all_ends_with_the_last_one_standing_and_ranks_the_fallen_by_when_they_fell()
    {
        var m = new Match(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        // Line them up: b and c in front of a, 40 m apart, facing it.
        m.Sim.TankRef(a).Position = new Vector2(100, 150);
        m.Sim.TankRef(a).Yaw = 0f;
        m.Sim.TankRef(b).Position = new Vector2(100, 110);
        m.Sim.TankRef(b).Yaw = MathF.PI;
        m.Sim.TankRef(c).Position = new Vector2(140, 110);
        m.Sim.TankRef(c).Yaw = MathF.PI;
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.True(m.Tank(b).IsDestroyed);
        Assert.Equal(MatchPhase.Playing, m.Rules.Phase);
        m.Weaken(c, 1);
        m.Attack(a, c);
        m.Run(6 * Tps);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerSlot);
        Assert.Equal(1, m.Rules.Of(0).Rank);
        Assert.Equal(2, m.Rules.Of(2).Rank);   // fell last
        Assert.Equal(3, m.Rules.Of(1).Rank);
        Assert.Equal(2, m.Rules.Of(0).Kills);
    }

    [Fact]
    public void In_a_free_for_all_a_leaver_is_out_and_ranked_and_ties_go_to_the_one_with_more_kills()
    {
        var m = new Match(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1);
        int c = m.Join(2);
        m.LineUp(a, b, c);
        m.Run(ModeRules.ProtectionTicks + 1);
        m.Rules.Leave(2);   // c quits first: last place
        Assert.True(m.Rules.Of(2).EliminatedAt > 0);
        m.Weaken(b, 1);
        m.Attack(a, b);
        m.Run(2 * Tps);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(0, m.Rules.WinnerSlot);
        Assert.Equal(1, m.Rules.Of(0).Rank);
        Assert.Equal(2, m.Rules.Of(1).Rank);
        Assert.Equal(3, m.Rules.Of(2).Rank);
        _ = c;

        // Time up with equal health: the one with a kill ranks higher.
        var n = new Match(BattleMode.FreeForAll);
        int x = n.Join(0), y = n.Join(1), z = n.Join(2);
        n.LineUp(y, z, x);
        n.Run(ModeRules.ProtectionTicks + 1);
        n.Weaken(z, 1);
        n.Attack(y, z);
        n.Run(2 * Tps);
        n.Order(y, new InputCmd(0, 0, 0, 0f, 0f, false));
        Assert.Equal(n.Tank(x).Health, n.Tank(y).Health);
        n.Run(n.Rules.TicksLeft);
        Assert.Equal(1, n.Rules.WinnerSlot);
        Assert.Equal(1, n.Rules.Of(1).Rank);
        Assert.Equal(2, n.Rules.Of(0).Rank);
    }

    [Fact]
    public void A_free_for_all_on_the_clock_ranks_the_living_by_health()
    {
        var m = new Match(BattleMode.FreeForAll);
        int a = m.Join(0), b = m.Join(1), c = m.Join(2);
        m.Weaken(a, 300);
        m.Weaken(c, 500);
        Assert.Equal(10 * 60 * Tps, m.Rules.TicksLeft);
        m.Run(m.Rules.TicksLeft);
        Assert.Equal(MatchPhase.Ended, m.Rules.Phase);
        Assert.Equal(1, m.Rules.WinnerSlot);   // b at full health
        Assert.Equal(1, m.Rules.Of(1).Rank);
        Assert.Equal(2, m.Rules.Of(2).Rank);
        Assert.Equal(3, m.Rules.Of(0).Rank);
        Assert.Equal(MatchRules.NoTeam, m.Rules.WinnerTeam);
    }

    [Fact]
    public void Only_a_team_battle_takes_players_in_mid_match()
    {
        foreach (BattleMode mode in ModeRules.All)
        {
            var m = new Match(mode);
            Assert.True(m.Rules.AcceptsJoins);
            m.Join(0, 0);
            m.Join(1, 1);
            m.Run(5);
            Assert.Equal(ModeRules.JoinsMidMatch(mode), m.Rules.AcceptsJoins);
            if (mode == BattleMode.TeamBattle) Assert.Equal(3, m.Join(2, 1) > 0 ? m.Sim.TankCount : 0);
            else Assert.Throws<InvalidOperationException>(() => m.Join(2, 1));
        }
        Assert.Equal(new[] { "teambattle", "annihilation", "ffa" }, ModeRules.All.Select(ModeRules.Id));
        Assert.True(ModeRules.TryParse("FFA", out BattleMode ffa) && ffa == BattleMode.FreeForAll);
        Assert.False(ModeRules.TryParse("coop", out _));
    }

    [Fact]
    public void Spawn_points_in_use_are_skipped_and_reused_once_free()
    {
        var m = new Match(BattleMode.TeamBattle);
        for (byte slot = 0; slot < 5; slot++) m.Join(slot, 0);
        Assert.Equal(Layout.Team.Where(p => p.Team == 0).Select(p => p.Position), m.Sim.Tanks.ToArray().Select(t => t.Position));
        m.Sim.TankRef(m.Rules.Of(2).TankId).Position = new Vector2(100, 190);   // slot 2 drives off its point
        int f = m.Join(5, 0);
        Assert.Equal(new Vector2(100, 150), m.Tank(f).Position);   // the freed point
        int g = m.Join(6, 0);
        Assert.Contains(m.Tank(g).Position, Layout.Team.Where(p => p.Team == 0).Select(p => p.Position));   // all busy: the roomiest

        m.Sim.TankRef(m.Rules.Of(3).TankId).IsDestroyed = true;   // a wreck on its point keeps it busy too
        m.Sim.TankRef(m.Rules.Of(2).TankId).Position = new Vector2(100, 190);
        m.Sim.TankRef(f).Position = new Vector2(120, 190);
        int h = m.Join(7, 0);
        Assert.Equal(new Vector2(100, 150), m.Tank(h).Position);
    }
}
```

新建 `tests/Tank.Sim.Tests/SandboxSpawnTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Tests;

/// <summary>The sandbox map's spawn points and bot waypoints (stage 4 design §2.3).</summary>
public sealed class SandboxSpawnTests
{
    private static readonly Battlefield Field = SandboxMap.Create();
    private static readonly Prop[] Solid = Field.Props.Where(p => p.Kind != PropKind.Bush).ToArray();

    private static void AssertOpenGround(Vector2 at, float clearance, string what)
    {
        Assert.Equal(at, Field.ClampToPlayable(at));
        float slope = MathF.Acos(Field.Terrain.NormalAt(at.X, at.Y).Y) * 180f / MathF.PI;
        Assert.True(slope <= 6f, $"{what} at {at}: {slope:F1}° slope");
        Obb2 hull = TankMotion.Footprint(TankCatalog.Heavy, at, 0f);
        Obb2 roomy = hull with { HalfExtents = hull.HalfExtents + new Vector2(clearance) };
        Assert.All(Solid, p => Assert.False(Obb2.TryGetPenetration(roomy, p.Footprint, out _), $"{what} at {at} is within {clearance} m of {p.Kind} {p.Id}"));
        Assert.All(Field.Props.Where(p => p.Kind == PropKind.Bush), b =>
            Assert.True(Vector2.Distance(b.Footprint.Center, at) >= b.Footprint.HalfExtents.X + 4f, $"{what} at {at} is in a bush"));
    }

    [Fact]
    public void Each_team_has_five_spawn_points_on_open_ground_facing_the_other_team()
    {
        IReadOnlyList<SpawnPoint> team = SandboxMap.Spawns.Team;
        Assert.Equal(5, team.Count(p => p.Team == 0));
        Assert.Equal(5, team.Count(p => p.Team == 1));
        foreach (SpawnPoint p in team) AssertOpenGround(p.Position, 6f, $"team {p.Team} spawn");
        Vector2 centre0 = Average(team.Where(p => p.Team == 0)), centre1 = Average(team.Where(p => p.Team == 1));
        Assert.InRange(Vector2.Distance(centre0, centre1), 240f, 320f);
        foreach (SpawnPoint p in team)
        {
            Vector2 enemy = p.Team == 0 ? centre1 : centre0;
            Assert.True(MathF.Abs(Angles.Wrap(p.Yaw - Angles.YawOf(enemy - p.Position))) < Angles.Deg(10), $"spawn at {p.Position} doesn't face the enemy");
            Assert.All(team.Where(q => q != p), q => Assert.True(Vector2.Distance(p.Position, q.Position) >= 8f, $"spawns {p.Position} and {q.Position} crowd each other"));
        }
    }

    [Fact]
    public void Ten_solo_spawns_lie_spread_over_the_map_well_inside_it()
    {
        IReadOnlyList<SpawnPoint> solo = SandboxMap.Spawns.Solo;
        Assert.Equal(10, solo.Count);
        foreach (SpawnPoint p in solo)
        {
            AssertOpenGround(p.Position, 6f, "solo spawn");
            Assert.InRange(p.Position.X, 40f, 360f);
            Assert.InRange(p.Position.Y, 40f, 360f);
            Assert.All(solo.Where(q => q != p), q => Assert.True(Vector2.Distance(p.Position, q.Position) >= 80f, $"solo spawns {p.Position} and {q.Position} are close"));
        }
        Assert.True(solo.Any(p => p.Position.X < 200 && p.Position.Y < 200) && solo.Any(p => p.Position.X >= 200 && p.Position.Y >= 200));
    }

    [Fact]
    public void Twenty_waypoints_cover_the_map_on_open_ground()
    {
        IReadOnlyList<Vector2> points = SandboxMap.Waypoints;
        Assert.Equal(20, points.Count);
        foreach (Vector2 p in points)
        {
            AssertOpenGround(p, 8f, "waypoint");
            Assert.All(points.Where(q => q != p), q => Assert.True(Vector2.Distance(p, q) >= 45f, $"waypoints {p} and {q} are close"));
        }
        for (int qx = 0; qx < 2; qx++)
        for (int qz = 0; qz < 2; qz++)
            Assert.True(points.Count(p => (p.X >= 200) == (qx == 1) && (p.Y >= 200) == (qz == 1)) >= 3, $"quarter {qx},{qz} has too few waypoints");
    }

    private static Vector2 Average(IEnumerable<SpawnPoint> points)
    {
        Vector2 sum = Vector2.Zero;
        int n = 0;
        foreach (SpawnPoint p in points) { sum += p.Position; n++; }
        return sum / n;
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Tank.Sim.Modes`、`MatchRules`、`SandboxMap.Spawns` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Maps/SandboxMap.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Maps/SandboxMap.cs b/src/Tank.Sim/Maps/SandboxMap.cs
index 80a026d..3ddd7b0 100644
--- a/src/Tank.Sim/Maps/SandboxMap.cs
+++ b/src/Tank.Sim/Maps/SandboxMap.cs
@@ -1,6 +1,7 @@
 using System.Numerics;
 using Tank.Sim.Ai;
 using Tank.Sim.Geometry;
+using Tank.Sim.Modes;
 using Tank.Sim.Terrain;
 using Tank.Sim.Vehicles;
 
@@ -103,6 +104,46 @@ public static class SandboxMap
         new(PatrolStart, 0f),
     };
 
+    /// <summary>
+    /// Where the two teams of a match start (stage 4 design §2.3): five spots each on open flat ground about 280 m apart,
+    /// team 0 south-west of the middle, team 1 north-east, every tank facing the other side.
+    /// </summary>
+    private static readonly Vector2 Team0Centre = new(100, 300), Team1Centre = new(300, 100);
+    private static readonly Vector2[] Team0Spots = { new(100, 300), new(110, 303), new(103, 310), new(93, 307), new(90, 297) };
+    private static readonly Vector2[] Team1Spots = { new(300, 100), new(310, 100), new(305, 109), new(295, 109), new(290, 100) };
+
+    /// <summary>Ten spots for a free-for-all, spread over the map at least 100 m apart, each facing the middle.</summary>
+    private static readonly Vector2[] SoloSpots =
+    {
+        new(76, 60), new(360, 360), new(40, 360), new(352, 84), new(200, 228),
+        new(48, 208), new(212, 76), new(344, 224), new(224, 360), new(132, 148),
+    };
+
+    // After the spot arrays: static initialisers run in order.
+    public static readonly SpawnLayout Spawns = new(TeamSpawns(), SoloSpawns());
+
+    /// <summary>Twenty points on open ground for the bots to drive between, at least 45 m apart and covering the map.</summary>
+    public static readonly IReadOnlyList<Vector2> Waypoints = new Vector2[]
+    {
+        new(192, 216), new(40, 40), new(340, 40), new(360, 360), new(40, 360), new(40, 192), new(360, 188), new(212, 360),
+        new(184, 40), new(288, 272), new(124, 128), new(260, 104), new(124, 312), new(48, 116), new(116, 208), new(284, 180),
+        new(56, 280), new(360, 112), new(112, 52), new(284, 348),
+    };
+
+    private static SpawnPoint[] TeamSpawns()
+    {
+        var points = new SpawnPoint[Team0Spots.Length + Team1Spots.Length];
+        for (int i = 0; i < Team0Spots.Length; i++) points[i] = new SpawnPoint(Team0Spots[i], Angles.YawOf(Team1Centre - Team0Spots[i]), 0);
+        for (int i = 0; i < Team1Spots.Length; i++) points[Team0Spots.Length + i] = new SpawnPoint(Team1Spots[i], Angles.YawOf(Team0Centre - Team1Spots[i]), 1);
+        return points;
+    }
+
+    private static SpawnPoint[] SoloSpawns()
+    {
+        var middle = new Vector2(200, 200);
+        return SoloSpots.Select(p => new SpawnPoint(p, Angles.YawOf(middle - p), 0)).ToArray();
+    }
+
     /// <summary>
     /// A place to watch the patrol from: open ground 109 m south-west of its first bush patch, in sight of it (from the
     /// spawn, rocks and the hill hide both patches). From here a medium tank sees the patrol in the open, loses it once it
PATCH
```

新建 `src/Tank.Sim/Modes/BattleMode.cs`：

```csharp
namespace Tank.Sim.Modes;

/// <summary>The three ways to fight a match (stage 4 design §2.1).</summary>
public enum BattleMode : byte
{
    /// <summary>Two teams; a knocked-out tank respawns after ten seconds; kills score; the higher score at seven minutes wins.</summary>
    TeamBattle = 0,
    /// <summary>Two teams, no respawns; wiping the other team out wins; a draw at seven minutes.</summary>
    Annihilation = 1,
    /// <summary>Every tank for itself, no respawns; the last one left wins, the rest rank by when they fell; health decides at ten minutes.</summary>
    FreeForAll = 2,
}

/// <summary>The rules that go with each <see cref="BattleMode"/>, and the numbers shared by all three (design §2.2).</summary>
public static class ModeRules
{
    public const int MaxPlayers = 10;
    public const int MinPlayers = 2;
    public const int ProtectionTicks = 5 * SimConstants.TickRate;
    public const int RespawnTicks = 10 * SimConstants.TickRate;
    public const int ResultsTicks = 8 * SimConstants.TickRate;
    private const int TeamMinutes = 7, SoloMinutes = 10;

    public static readonly IReadOnlyList<BattleMode> All = new[] { BattleMode.TeamBattle, BattleMode.Annihilation, BattleMode.FreeForAll };

    public static bool HasTeams(BattleMode mode) => mode != BattleMode.FreeForAll;

    public static bool Respawns(BattleMode mode) => mode == BattleMode.TeamBattle;

    /// <summary>Whether a player may join a match already under way (otherwise they wait in the room for the next).</summary>
    public static bool JoinsMidMatch(BattleMode mode) => mode == BattleMode.TeamBattle;

    public static int DurationTicks(BattleMode mode) => (mode == BattleMode.FreeForAll ? SoloMinutes : TeamMinutes) * 60 * SimConstants.TickRate;

    /// <summary>The name on the command line and in room messages.</summary>
    public static string Id(BattleMode mode) => mode switch
    {
        BattleMode.TeamBattle => "teambattle",
        BattleMode.Annihilation => "annihilation",
        BattleMode.FreeForAll => "ffa",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown mode."),
    };

    public static bool TryParse(string? id, out BattleMode mode)
    {
        foreach (BattleMode m in All)
            if (string.Equals(Id(m), id, StringComparison.OrdinalIgnoreCase)) { mode = m; return true; }
        mode = default;
        return false;
    }
}
```

新建 `src/Tank.Sim/Modes/MatchRules.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Modes;

public enum MatchPhase : byte { Playing, Ended }

/// <summary>One player's part in the match, as the rules keep it (and the scoreboard shows it).</summary>
public struct Standing
{
    public bool Active;        // in the match (joined, not left)
    public int TankId;
    public byte Team;
    public int Kills;
    public int Deaths;
    public bool Alive;         // false while a wreck, or once eliminated
    public int RespawnAt;      // tick the tank comes back (team battle), or -1
    public int EliminatedAt;   // tick the player went out of a no-respawn match (or left it), or -1
    public byte Rank;          // final place in a free-for-all, 1 best; 0 until the match ends
}

/// <summary>
/// Runs a match's rules on top of a <see cref="Simulation"/> (stage 4 design §2): puts players' tanks on spawn points
/// with protection, scores kills, respawns or eliminates the fallen, keeps the clock and calls the result. Players are
/// known by room slot. Call <see cref="Update"/> after every <see cref="Simulation.Step"/>.
/// </summary>
public sealed class MatchRules
{
    public const byte NoTeam = byte.MaxValue;
    public const byte NoSlot = byte.MaxValue;

    /// <summary>A spawn point is in use while a live tank stands within this of it, m.</summary>
    private const float SpawnBusyRadius = 8f;

    private readonly Simulation _sim;
    private readonly SpawnLayout _spawns;
    private readonly Standing[] _players = new Standing[ModeRules.MaxPlayers];
    private readonly byte[] _rankScratch = new byte[ModeRules.MaxPlayers];
    private readonly int[] _teamScore = new int[2];   // kills by team, kept when a player leaves

    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns)
    {
        (Mode, _sim, _spawns) = (mode, sim, spawns);
        EndTick = sim.Tick + ModeRules.DurationTicks(mode);
        for (int i = 0; i < _players.Length; i++) _players[i] = new Standing { RespawnAt = -1, EliminatedAt = -1 };
    }

    public BattleMode Mode { get; }
    public MatchPhase Phase { get; private set; } = MatchPhase.Playing;

    /// <summary>The tick the clock runs out.</summary>
    public int EndTick { get; }

    /// <summary>The tick the match ended, or -1 while it is on.</summary>
    public int EndedAt { get; private set; } = -1;

    public int TicksLeft => Math.Max(0, EndTick - _sim.Tick);

    /// <summary>The winning team (two-team modes), or <see cref="NoTeam"/> for a draw or while the match is on.</summary>
    public byte WinnerTeam { get; private set; } = NoTeam;

    /// <summary>The winning player's slot (free-for-all), or <see cref="NoSlot"/>.</summary>
    public byte WinnerSlot { get; private set; } = NoSlot;

    public ref readonly Standing Of(byte slot) => ref _players[slot];

    /// <summary>Whether another player may be put into the match now.</summary>
    public bool AcceptsJoins => Phase == MatchPhase.Playing && (_sim.Tick == EndTick - ModeRules.DurationTicks(Mode) || ModeRules.JoinsMidMatch(Mode));

    /// <summary>A team's kills, including those of players who have since left (two-team modes).</summary>
    public int TeamScore(byte team) => team < _teamScore.Length ? _teamScore[team] : 0;

    public int AliveCount(byte team = NoTeam)
    {
        int n = 0;
        foreach (ref readonly Standing p in _players.AsSpan()) if (p.Active && p.Alive && (team == NoTeam || p.Team == team)) n++;
        return n;
    }

    /// <summary>
    /// Puts <paramref name="slot"/>'s tank into the battle on a free spawn point of <paramref name="team"/> (ignored in
    /// a free-for-all, where each player is their own team), under protection.
    /// </summary>
    /// <returns>The tank's id.</returns>
    public int Join(byte slot, TankSpec spec, byte team)
    {
        if (!AcceptsJoins) throw new InvalidOperationException("The match takes no more players now.");
        if (_players[slot].Active) throw new InvalidOperationException($"Slot {slot} is already in the match.");
        if (ModeRules.HasTeams(Mode) && team > 1) throw new ArgumentOutOfRangeException(nameof(team), team, "Teams are 0 and 1.");
        byte actual = ModeRules.HasTeams(Mode) ? team : slot;
        SpawnPoint spawn = PickSpawn(actual);
        int id = _sim.AddTank(spec, spawn.Position, spawn.Yaw, actual);
        _sim.Protect(id, ModeRules.ProtectionTicks);
        _players[slot] = new Standing { Active = true, TankId = id, Team = actual, Alive = true, RespawnAt = -1, EliminatedAt = -1 };
        return id;
    }

    /// <summary>Takes <paramref name="slot"/>'s tank out of the battle; in a no-respawn mode that counts as going out.</summary>
    public void Leave(byte slot)
    {
        ref Standing p = ref _players[slot];
        if (!p.Active) return;
        if (_sim.SlotOf(p.TankId) >= 0) _sim.RemoveTank(p.TankId);
        p.Active = false;
        p.Alive = false;
        p.RespawnAt = -1;
        if (!ModeRules.Respawns(Mode) && p.EliminatedAt < 0) p.EliminatedAt = _sim.Tick;
    }

    /// <summary>After each step: scores this tick's kills, respawns the due, and calls the result when it is decided.</summary>
    public void Update()
    {
        if (Phase == MatchPhase.Ended) return;
        foreach (ref readonly GameEvent e in _sim.Events)
            if (e.Type == GameEventType.TankDestroyed) OnDestroyed(e);
        if (ModeRules.Respawns(Mode)) RespawnDue();
        Judge();
    }

    private void OnDestroyed(in GameEvent e)
    {
        int victim = SlotOfTank(e.TargetTankId), killer = SlotOfTank(e.TankId);
        if (victim < 0) return;
        ref Standing v = ref _players[victim];
        v.Deaths++;
        v.Alive = false;
        if (killer >= 0 && killer != victim && _players[killer].Team != v.Team)
        {
            _players[killer].Kills++;
            if (ModeRules.HasTeams(Mode)) _teamScore[_players[killer].Team]++;
        }
        if (ModeRules.Respawns(Mode)) v.RespawnAt = _sim.Tick + ModeRules.RespawnTicks;
        else v.EliminatedAt = _sim.Tick;
    }

    private void RespawnDue()
    {
        for (byte slot = 0; slot < _players.Length; slot++)
        {
            ref Standing p = ref _players[slot];
            if (!p.Active || p.Alive || p.RespawnAt < 0 || _sim.Tick < p.RespawnAt) continue;
            SpawnPoint spawn = PickSpawn(p.Team);
            _sim.Respawn(p.TankId, spawn.Position, spawn.Yaw);
            _sim.Protect(p.TankId, ModeRules.ProtectionTicks);
            p.Alive = true;
            p.RespawnAt = -1;
        }
    }

    private void Judge()
    {
        bool timeUp = _sim.Tick >= EndTick;
        switch (Mode)
        {
            case BattleMode.TeamBattle when timeUp:
                End(team: Leader());
                break;
            case BattleMode.Annihilation:
                int a = AliveCount(0), b = AliveCount(1);
                if (a == 0 || b == 0) End(team: a > 0 ? (byte)0 : b > 0 ? (byte)1 : NoTeam);
                else if (timeUp) End(team: NoTeam);
                break;
            case BattleMode.FreeForAll:
                if (AliveCount() <= 1 || timeUp) EndSolo();
                break;
        }
    }

    private byte Leader()
    {
        int a = TeamScore(0), b = TeamScore(1);
        return a == b ? NoTeam : a > b ? (byte)0 : (byte)1;
    }

    private void End(byte team)
    {
        WinnerTeam = team;
        Phase = MatchPhase.Ended;
        EndedAt = _sim.Tick;
    }

    /// <summary>
    /// Ranks a free-for-all: the living by health, best first (the last one left, or whoever has the most when time is
    /// up, wins), then the fallen by how long they lasted; ties by kills, then by slot.
    /// </summary>
    private void EndSolo()
    {
        int n = 0;
        for (byte slot = 0; slot < _players.Length; slot++)
            if (_players[slot].Active || _players[slot].EliminatedAt >= 0) _rankScratch[n++] = slot;
        Span<byte> order = _rankScratch.AsSpan(0, n);
        // Insertion sort by (alive first, then health desc, then eliminated later first): n ≤ 10.
        for (int i = 1; i < n; i++)
        {
            byte key = order[i];
            int j = i - 1;
            while (j >= 0 && Before(key, order[j])) { order[j + 1] = order[j]; j--; }
            order[j + 1] = key;
        }
        for (int i = 0; i < n; i++) _players[order[i]].Rank = (byte)(i + 1);
        WinnerSlot = n > 0 ? order[0] : NoSlot;
        Phase = MatchPhase.Ended;
        EndedAt = _sim.Tick;
    }

    /// <summary>Whether <paramref name="x"/> places ahead of <paramref name="y"/>.</summary>
    private bool Before(byte x, byte y)
    {
        ref readonly Standing a = ref _players[x];
        ref readonly Standing b = ref _players[y];
        if (a.Alive != b.Alive) return a.Alive;
        if (a.Alive && HealthOf(a) != HealthOf(b)) return HealthOf(a) > HealthOf(b);
        if (!a.Alive && a.EliminatedAt != b.EliminatedAt) return a.EliminatedAt > b.EliminatedAt;
        return a.Kills > b.Kills;
    }

    private int HealthOf(in Standing p)
    {
        int slot = _sim.SlotOf(p.TankId);
        return slot < 0 ? 0 : _sim.Tanks[slot].Health;
    }

    /// <summary>
    /// The first spawn point of <paramref name="team"/> (any, in a free-for-all) with no tank or wreck on it; when all
    /// are taken, the one with the most room.
    /// </summary>
    private SpawnPoint PickSpawn(byte team)
    {
        IReadOnlyList<SpawnPoint> points = ModeRules.HasTeams(Mode) ? _spawns.Team : _spawns.Solo;
        SpawnPoint best = default;
        float bestRoom = -1f;
        for (int i = 0; i < points.Count; i++)   // indexed: a foreach over the interface would box an enumerator
        {
            SpawnPoint p = points[i];
            if (ModeRules.HasTeams(Mode) && p.Team != team) continue;
            float room = RoomAround(p.Position);
            if (room >= SpawnBusyRadius) return p;
            if (room > bestRoom) (best, bestRoom) = (p, room);
        }
        if (bestRoom < 0) throw new InvalidOperationException($"No spawn points for team {team}.");
        return best;
    }

    private float RoomAround(Vector2 point)
    {
        float nearest = float.MaxValue;
        foreach (ref readonly TankState t in _sim.Tanks)
            nearest = MathF.Min(nearest, Vector2.Distance(t.Position, point));   // wrecks count: a tank may not be put on one
        return nearest;
    }

    private int SlotOfTank(int tankId)
    {
        if (tankId == 0) return -1;
        for (int i = 0; i < _players.Length; i++)
            if (_players[i].TankId == tankId && (_players[i].Active || _players[i].EliminatedAt >= 0)) return i;
        return -1;
    }
}
```

新建 `src/Tank.Sim/Modes/SpawnLayout.cs`：

```csharp
using System.Numerics;

namespace Tank.Sim.Modes;

/// <summary>Where a tank may appear: a spot, the way it faces, and (for team spawns) whose it is.</summary>
public readonly record struct SpawnPoint(Vector2 Position, float Yaw, byte Team);

/// <summary>
/// A map's spawn points (stage 4 design §2.3): <see cref="Team"/> for the two-team modes (each team's own, facing the
/// other), <see cref="Solo"/> spread over the map for a free-for-all.
/// </summary>
public sealed record SpawnLayout(IReadOnlyList<SpawnPoint> Team, IReadOnlyList<SpawnPoint> Solo);
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 614、Net 50。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): match rules — team battle, annihilation and free-for-all; spawn points and bot waypoints on the sandbox map"
```

---

### Task 4：机器人

设计 §4。`Ai/Steering.Toward(in tank, point, stopThere)`：朝一个点开的油门和转向（偏差超过 35° 先原地转，2 m 内算到），从 `PatrolDriver` 里抽出来共用。`Ai/SkirmishBot(tankId, waypoints, seed)`：`Command(sim, vision, tick)` 每 tick 给一条 `InputCmd`——在随机路点间游走（到点 4 m 内换下一个，3 秒没挪 1 m 算卡住、换路点），本队看得见的 250 m 内最近的敌车作为目标，炮口对准其车体中心 1° 以内就打穿甲弹。不分配内存。

**Files:**
- Create: `src/Tank.Sim/Ai/Steering.cs`、`src/Tank.Sim/Ai/SkirmishBot.cs`
- Modify: `src/Tank.Sim/Ai/PatrolDriver.cs`
- Test: 新建 `tests/Tank.Sim.Tests/SkirmishBotTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Sim.Tests/SkirmishBotTests.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Ai;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Tests;

/// <summary>The room-filling bot on the sandbox map (stage 4 design §4).</summary>
public sealed class SkirmishBotTests
{
    private const int Tps = SimConstants.TickRate;

    private sealed class Arena
    {
        public readonly Simulation Sim;
        public readonly VisionTracker Vision;
        public readonly IReadOnlyList<Vector2> Waypoints;
        public readonly List<SkirmishBot> Bots = new();
        public readonly Dictionary<int, InputCmd> Orders = new();
        public readonly List<GameEvent> Events = new();

        /// <summary>The sandbox map and its waypoints, or a flat 200 m field (or the sandbox map) with <paramref name="waypoints"/>.</summary>
        public Arena(IReadOnlyList<Vector2>? waypoints = null, bool sandbox = false)
        {
            Sim = new Simulation(waypoints is null || sandbox ? SandboxMap.Create() : TestWorld.FlatField());
            Waypoints = waypoints ?? SandboxMap.Waypoints;
            Vision = new VisionTracker(Sim);
        }

        public int AddBot(TankSpec spec, Vector2 at, float yaw, byte team, uint seed = 7)
        {
            int id = Sim.AddTank(spec, at, yaw, team);
            Bots.Add(new SkirmishBot(id, Waypoints, seed));
            return id;
        }

        public int AddDummy(TankSpec spec, Vector2 at, float yaw, byte team)
        {
            int id = Sim.AddTank(spec, at, yaw, team);
            Orders[id] = new InputCmd(0, 0, 0, yaw, 0f, false);
            return id;
        }

        public void Run(int ticks, Action<int>? each = null)
        {
            var inputs = new InputCmd[Simulation.MaxTanks];
            for (int i = 0; i < ticks; i++)
            {
                int tick = Sim.Tick + 1;
                for (int k = 0; k < Sim.TankCount; k++)
                {
                    int id = Sim.Tanks[k].Id;
                    SkirmishBot? bot = Bots.Find(b => b.TankId == id);
                    inputs[k] = bot is not null ? bot.Command(Sim, Vision, tick) : Orders[id] with { Tick = tick };
                }
                Sim.Step(inputs.AsSpan(0, Sim.TankCount));
                Vision.Update();
                Events.AddRange(Sim.Events);
                each?.Invoke(tick);
            }
        }

        public TankState Tank(int id) => Sim.Tanks[Sim.SlotOf(id)];
    }

    [Fact]
    public void It_drives_from_waypoint_to_waypoint_without_getting_stuck()
    {
        var arena = new Arena();
        int bot = arena.AddBot(TankCatalog.Medium, SandboxMap.Spawns.Team[0].Position, SandboxMap.Spawns.Team[0].Yaw, 0, seed: 3);
        var visited = new HashSet<int>();
        float travelled = 0f;
        Vector2 last = arena.Tank(bot).Position;
        int idleTicks = 0, longestIdle = 0;
        arena.Run(120 * Tps, tick =>
        {
            visited.Add(arena.Bots[0].Waypoint);
            TankState t = arena.Tank(bot);
            travelled += Vector2.Distance(t.Position, last);
            last = t.Position;
            idleTicks = MathF.Abs(t.Speed) < 0.3f ? idleTicks + 1 : 0;
            longestIdle = Math.Max(longestIdle, idleTicks);
        });
        Assert.True(travelled > 400f, $"only {travelled:F0} m in two minutes");
        Assert.True(visited.Count >= 4, $"only {visited.Count} waypoints");
        Assert.True(longestIdle < 8 * Tps, $"stood still for {longestIdle / (float)Tps:F1} s");   // turning on the spot after a waypoint takes a few seconds
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);   // nobody to shoot at
        Assert.All(arena.Sim.Field.Props.Where(p => p.Kind != PropKind.Bush), p => Assert.True(arena.Sim.Field.IsStanding(p.Id), $"knocked down {p.Kind} {p.Id}"));
    }

    [Fact]
    public void It_fires_at_a_visible_enemy_and_hits_it()
    {
        var arena = new Arena(new Vector2[] { new(20, 20), new(180, 180), new(20, 180) });   // flat, open ground
        int bot = arena.AddBot(TankCatalog.Medium, new Vector2(100, 180), 0f, 0);
        int enemy = arena.AddDummy(TankCatalog.Heavy, new Vector2(100, 80), MathF.PI, 1);   // 100 m north, facing it
        arena.Run(12 * Tps);
        Assert.Equal(enemy, arena.Bots[0].TargetId);
        Assert.Equal(0f, arena.Tank(bot).Speed);
        GameEvent[] shots = arena.Events.Where(e => e.Type == GameEventType.ShellFired && e.TankId == bot).ToArray();
        Assert.True(shots.Length >= 2, $"{shots.Length} shots in 12 s");
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && e.TargetTankId == enemy);
        Assert.True(Vector2.Distance(arena.Tank(bot).Position, new Vector2(100, 180)) < 3f, "it stopped to shoot");
    }

    [Fact]
    public void It_only_shoots_what_its_team_can_see()
    {
        // A team mate stands watch at the watch point, 109 m from the first bush patch, where a light tank standing in the
        // bushes is hidden (two bushes: 40.5 m); the same tank 24 m outside the patch is seen, by the team mate, and the
        // bot (which has driven off a little by then) engages it on its team's word.
        var arena = new Arena();
        int bot = arena.AddBot(TankCatalog.Medium, SandboxMap.PatrolWatch + new Vector2(-10, 0), 0f, 0);
        arena.AddDummy(TankCatalog.Medium, SandboxMap.PatrolWatch, 0f, 0);
        int hidden = arena.AddDummy(TankCatalog.Light, SandboxMap.BushPatches[2], MathF.PI, 1);
        arena.Run(3 * Tps);
        Assert.False(arena.Vision.IsVisible(hidden, 0));
        Assert.Equal(0, arena.Bots[0].TargetId);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);
        Assert.True(arena.Tank(bot).Position != SandboxMap.PatrolWatch + new Vector2(-10, 0), "it drove on, looking for a waypoint");

        arena.Sim.TankRef(hidden).Position = SandboxMap.BushPatches[2] + new Vector2(-24, 0);
        arena.Run(8 * Tps);
        Assert.True(arena.Vision.IsVisible(hidden, 0));
        Assert.Equal(hidden, arena.Bots[0].TargetId);
        Assert.Contains(arena.Events, e => e.Type == GameEventType.ShellFired && e.TankId == bot);
    }

    /// <summary>A bot at (60, 200) with a team mate standing next to an enemy heavy <paramref name="away"/> m east, so the team sees it.</summary>
    private static Arena SpottedEnemy(float away, out int bot, out int enemy)
    {
        var arena = new Arena(new Vector2[] { new(60, 200), new(63, 200), new(60, 203) }, sandbox: true);   // it potters about its spot (the flat field is only 200 m)
        bot = arena.AddBot(TankCatalog.Medium, new Vector2(60, 200), 0f, 0);
        arena.AddDummy(TankCatalog.Medium, new Vector2(60 + away, 215), MathF.PI, 0);
        enemy = arena.AddDummy(TankCatalog.Heavy, new Vector2(60 + away, 200), MathF.PI, 1);
        return arena;
    }

    [Fact]
    public void It_ignores_enemies_beyond_250_m_and_a_wreck_holds_still()
    {
        Arena arena = SpottedEnemy(260f, out int bot, out int far);
        arena.Run(3 * Tps);
        Assert.True(arena.Vision.IsVisible(far, 0), "the team mate sees it");
        Assert.Equal(0, arena.Bots[0].TargetId);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);

        Arena near = SpottedEnemy(240f, out _, out int enemy);
        near.Run(3 * Tps);
        Assert.Equal(enemy, near.Bots[0].TargetId);

        arena.Sim.TankRef(bot).IsDestroyed = true;
        arena.Sim.TankRef(bot).Health = 0;
        InputCmd cmd = arena.Bots[0].Command(arena.Sim, arena.Vision, arena.Sim.Tick + 1);
        Assert.Equal(0, cmd.Throttle);
        Assert.False(cmd.Fire);
        Assert.Equal(0, arena.Bots[0].TargetId);
        _ = far;
    }

    [Fact]
    public void Out_of_AP_it_drives_on_instead_of_standing_in_the_open()
    {
        Arena arena = SpottedEnemy(100f, out int bot, out int enemy);
        arena.Sim.TankRef(bot).ApLeft = 0;
        arena.Run(5 * Tps);
        Assert.True(arena.Vision.IsVisible(enemy, 0));
        Assert.Equal(0, arena.Bots[0].TargetId);
        Assert.DoesNotContain(arena.Events, e => e.Type == GameEventType.ShellFired);
        _ = bot;
    }

    [Fact]
    public void It_turns_in_place_towards_a_waypoint_behind_it_without_giving_up_on_it()
    {
        // A heavy (26°/s) facing north with a waypoint 90 m behind it: turning takes ~7 s, longer than the 3 s stuck window.
        Arena arena;
        int bot;
        uint seed = 0;
        do
        {
            arena = new Arena(new Vector2[] { new(100, 20), new(100, 190) });
            bot = arena.AddBot(TankCatalog.Heavy, new Vector2(100, 100), 0f, 0, seed: ++seed);
        } while (arena.Bots[0].Waypoint != 1);
        arena.Run(8 * Tps);
        Assert.Equal(1, arena.Bots[0].Waypoint);
        TankState t = arena.Tank(bot);
        Assert.True(MathF.Abs(Angles.Wrap(t.Yaw - MathF.PI)) < 0.3f, $"turned round: yaw {t.Yaw:F2}");
        Assert.True(t.Position.Y > 105f, $"on its way: {t.Position}");
    }

    [Fact]
    public void Two_bots_on_opposite_teams_find_and_fight_each_other()
    {
        var arena = new Arena();
        int a = arena.AddBot(TankCatalog.Medium, SandboxMap.Spawns.Team[0].Position, SandboxMap.Spawns.Team[0].Yaw, 0, seed: 11);
        int b = arena.AddBot(TankCatalog.Medium, SandboxMap.Spawns.Team[5].Position, SandboxMap.Spawns.Team[5].Yaw, 1, seed: 23);
        arena.Run(180 * Tps);
        Assert.Contains(arena.Events, e => e.Type == GameEventType.TankHit && (e.TargetTankId == a || e.TargetTankId == b));
    }

    [Fact]
    public void Its_choices_replay_the_same_from_the_same_seed()
    {
        var x = new Arena();
        var y = new Arena();
        x.AddBot(TankCatalog.Light, SandboxMap.Spawns.Solo[0].Position, 0f, 0, seed: 5);
        y.AddBot(TankCatalog.Light, SandboxMap.Spawns.Solo[0].Position, 0f, 0, seed: 5);
        x.Run(30 * Tps);
        y.Run(30 * Tps);
        Assert.Equal(x.Sim.Tanks[0], y.Sim.Tanks[0]);
        Assert.Equal(x.Bots[0].Waypoint, y.Bots[0].Waypoint);
        Assert.Throws<ArgumentException>(() => new SkirmishBot(1, new[] { Vector2.Zero }, 1));
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Sim.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`SkirmishBot`、`Steering` 找不到。

- [ ] **Step 3：实现**

修改 `src/Tank.Sim/Ai/PatrolDriver.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Ai/PatrolDriver.cs b/src/Tank.Sim/Ai/PatrolDriver.cs
index 1b5cb41..a6371a3 100644
--- a/src/Tank.Sim/Ai/PatrolDriver.cs
+++ b/src/Tank.Sim/Ai/PatrolDriver.cs
@@ -32,14 +32,13 @@ public sealed class PatrolDriver
     public const float PassRadius = 3f;
 
     /// <summary>A point to wait at, or a sharp corner, counts as reached once the hull has stopped within this of it, m.</summary>
-    public const float StopRadius = 2f;
+    public const float StopRadius = Steering.StopRadius;
 
     /// <summary>
-    /// A point further off the heading than this (radians) is turned to on the spot; a route point where the route turns
-    /// more than this is stopped at, like a point waited at.
+    /// A point further off the heading than this (radians) is turned to on the spot (<see cref="Steering"/>); a route
+    /// point where the route turns more than this is stopped at, like a point waited at.
     /// </summary>
-    public static readonly float TurnInPlace = Angles.Deg(35);
-    private static readonly float SteerDeadZone = Angles.Deg(2);
+    public static readonly float TurnInPlace = Steering.TurnInPlace;
 
     /// <summary>Seconds between shots while waiting.</summary>
     public const float FireEvery = 4f;
@@ -62,12 +61,6 @@ public sealed class PatrolDriver
     /// <summary>The gun fires only once it is laid this close to the aim, in turret yaw and gun pitch (radians).</summary>
     public static readonly float OnTarget = Angles.Deg(1);
 
-    /// <summary>Slower than this (m/s) the hull counts as stopped.</summary>
-    private const float StoppedSpeed = 0.3f;
-
-    /// <summary>Extra distance allowed for when braking to a stop, m.</summary>
-    private const float BrakeMargin = 0.5f;
-
     private readonly PatrolPoint[] _route;
     private readonly bool[] _sharp;   // per point: the route turns more than TurnInPlace there
     private int _waitUntil = -1;
@@ -105,7 +98,7 @@ public sealed class PatrolDriver
         PatrolPoint point = _route[Next];
         float distance = Vector2.Distance(tank.Position, point.Position);
         bool stopsHere = point.WaitSeconds > 0 || _sharp[Next];
-        if (stopsHere ? distance <= StopRadius && MathF.Abs(tank.Speed) < StoppedSpeed : distance <= PassRadius)
+        if (stopsHere ? Steering.StoppedAt(tank, point.Position) : distance <= PassRadius)
         {
             if (point.WaitSeconds > 0)
             {
@@ -118,10 +111,7 @@ public sealed class PatrolDriver
             distance = Vector2.Distance(tank.Position, point.Position);
             stopsHere = point.WaitSeconds > 0 || _sharp[Next];
         }
-        float off = Angles.Wrap(Angles.YawOf(point.Position - tank.Position) - tank.Yaw);
-        sbyte steer = (sbyte)(MathF.Abs(off) <= SteerDeadZone ? 0 : off > 0 ? -1 : 1);   // +yaw is to the left; Steer +1 turns right
-        bool braking = stopsHere && distance <= tank.Speed * tank.Speed / (2 * tank.Spec.Braking) + BrakeMargin;
-        sbyte throttle = (sbyte)(MathF.Abs(off) > TurnInPlace || braking || distance <= StopRadius / 2 ? 0 : 1);
+        var (throttle, steer) = Steering.Toward(tank, point.Position, stopsHere);
         return hold with { Throttle = throttle, Steer = steer };
     }
 
PATCH
```

新建 `src/Tank.Sim/Ai/SkirmishBot.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Sim.Ai;

/// <summary>
/// The simple bot that fills a room out (stage 4 design §4): it drives from waypoint to randomly chosen waypoint, and
/// when its team can see an enemy within <see cref="EngageRange"/> it stops, lays the gun on the nearest one's hull
/// (no lead) and fires AP whenever loaded and laid, until the enemy is lost from view; out of AP it drives on and
/// ignores enemies. It sees only what its team sees (<see cref="VisionTracker.IsVisible"/>): no cheating. Stuck for
/// <see cref="StuckSeconds"/> on the way somewhere (neither moving nor turning), it picks another waypoint. A wreck holds still and carries on once revived. Its choices come from its own seeded random
/// numbers, so a match replays the same.
/// </summary>
public sealed class SkirmishBot
{
    public const float EngageRange = 250f;

    /// <summary>A waypoint counts as reached this close, m.</summary>
    public const float WaypointRadius = 4f;

    public const float StuckSeconds = 3f;

    /// <summary>Moving less than this in <see cref="StuckSeconds"/> while driving counts as stuck, m …</summary>
    public const float StuckDistance = 1f;

    /// <summary>… unless it turned at least this much meanwhile (turning in place towards a waypoint behind it), radians.</summary>
    public static readonly float StuckTurn = Angles.Deg(20);

    /// <summary>The gun fires only once it is laid this close to the aim, in turret yaw and gun pitch (radians).</summary>
    public static readonly float OnTarget = Angles.Deg(1);

    private static readonly int StuckTicks = (int)(StuckSeconds * SimConstants.TickRate);

    private readonly IReadOnlyList<Vector2> _waypoints;
    private XorShift _rng;
    private Vector2 _lastChecked;
    private float _lastCheckedYaw;
    private int _checkAt = -1;

    public SkirmishBot(int tankId, IReadOnlyList<Vector2> waypoints, uint seed)
    {
        if (waypoints.Count < 2) throw new ArgumentException("A bot needs at least two waypoints.", nameof(waypoints));
        TankId = tankId;
        _waypoints = waypoints;
        _rng = new XorShift(seed);
        Waypoint = (int)(_rng.NextUInt() % (uint)waypoints.Count);
    }

    public int TankId { get; }

    /// <summary>The waypoint being driven to.</summary>
    public int Waypoint { get; private set; }

    /// <summary>The enemy being engaged, or 0.</summary>
    public int TargetId { get; private set; }

    /// <summary>The bot's command for <paramref name="tick"/>.</summary>
    public InputCmd Command(Simulation sim, VisionTracker vision, int tick)
    {
        int slot = sim.SlotOf(TankId);
        if (slot < 0) return new InputCmd(tick, 0, 0, 0f, 0f, false);
        ref readonly TankState me = ref sim.Tanks[slot];
        var hold = new InputCmd(tick, 0, 0, Angles.Wrap(me.Yaw + me.TurretYaw), me.GunPitch, false);
        if (me.IsDestroyed)
        {
            TargetId = 0;
            _checkAt = -1;
            return hold;
        }
        int target = NearestVisibleEnemy(sim, vision, me);
        TargetId = target >= 0 ? sim.Tanks[target].Id : 0;
        if (target >= 0)
        {
            _checkAt = -1;   // standing still on purpose isn't being stuck
            return Engage(me, sim.Tanks[target], sim, tick);
        }
        return Drive(me, tick, hold);
    }

    private int NearestVisibleEnemy(Simulation sim, VisionTracker vision, in TankState me)
    {
        if (me.ApLeft == 0) return -1;   // nothing to shoot with: don't stand in the open "engaging"
        int best = -1;
        float bestDistance = EngageRange;
        ReadOnlySpan<TankState> tanks = sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            ref readonly TankState t = ref tanks[i];
            if (t.Team == me.Team || t.IsDestroyed) continue;
            float distance = Vector2.Distance(t.Position, me.Position);
            if (distance > bestDistance || !vision.IsVisible(t.Id, me.Team)) continue;
            (best, bestDistance) = (i, distance);
        }
        return best;
    }

    /// <summary>Stops, lays the gun on the enemy's hull centre and fires once laid.</summary>
    private static InputCmd Engage(in TankState me, in TankState enemy, Simulation sim, int tick)
    {
        TankPose mine = TankPose.Compute(me, sim.Field.Terrain), theirs = TankPose.Compute(enemy, sim.Field.Terrain);
        Vector3 aim = theirs.HullBox(enemy.Spec).Center;
        var (yaw, pitch) = Angles.AimAngles(aim - mine.Trunnion(me.Spec), me.Yaw);
        var (turret, gun) = mine.ToHullAngles(Angles.Direction(yaw, pitch), me.TurretYaw);
        bool laid = MathF.Abs(Angles.Wrap(turret - me.TurretYaw)) <= OnTarget && MathF.Abs(gun - me.GunPitch) <= OnTarget;
        return new InputCmd(tick, 0, 0, yaw, pitch, laid, AmmoType.AP);
    }

    /// <summary>Drives to the waypoint, choosing the next when it gets there or gets stuck.</summary>
    private InputCmd Drive(in TankState me, int tick, in InputCmd hold)
    {
        if (Vector2.Distance(me.Position, _waypoints[Waypoint]) <= WaypointRadius) NextWaypoint(me);
        if (_checkAt < 0 || tick >= _checkAt)
        {
            bool stuck = Vector2.Distance(me.Position, _lastChecked) < StuckDistance && MathF.Abs(Angles.Wrap(me.Yaw - _lastCheckedYaw)) < StuckTurn;
            if (_checkAt >= 0 && stuck) NextWaypoint(me);
            _lastChecked = me.Position;
            _lastCheckedYaw = me.Yaw;
            _checkAt = tick + StuckTicks;
        }
        var (throttle, steer) = Steering.Toward(me, _waypoints[Waypoint], stopThere: false);
        return hold with { Throttle = throttle, Steer = steer, AimYaw = me.Yaw, AimPitch = 0f };
    }

    private void NextWaypoint(in TankState me)
    {
        int next = (int)(_rng.NextUInt() % (uint)(_waypoints.Count - 1));
        if (next >= Waypoint) next++;   // never the same one again
        Waypoint = next;
        _lastChecked = me.Position;
        _lastCheckedYaw = me.Yaw;
        _checkAt = -1;
    }
}
```

新建 `src/Tank.Sim/Ai/Steering.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Geometry;
using Tank.Sim.Vehicles;

namespace Tank.Sim.Ai;

/// <summary>
/// How a driver gets a tank to a point: steer toward it (turning on the spot when it lies more than
/// <see cref="TurnInPlace"/> off the heading), and brake in time to stop on it when asked to. Shared by the
/// <see cref="PatrolDriver"/> and the <see cref="SkirmishBot"/>.
/// </summary>
public static class Steering
{
    /// <summary>A point further off the heading than this (radians) is turned to on the spot.</summary>
    public static readonly float TurnInPlace = Angles.Deg(35);

    /// <summary>A point to stop at counts as reached once the hull has stopped within this of it, m.</summary>
    public const float StopRadius = 2f;

    /// <summary>Slower than this (m/s) the hull counts as stopped.</summary>
    public const float StoppedSpeed = 0.3f;

    private static readonly float SteerDeadZone = Angles.Deg(2);

    /// <summary>Extra distance allowed for when braking to a stop, m.</summary>
    private const float BrakeMargin = 0.5f;

    /// <summary>Throttle and steer that take <paramref name="tank"/> toward <paramref name="point"/>, braking to stop there if <paramref name="stopThere"/>.</summary>
    public static (sbyte Throttle, sbyte Steer) Toward(in TankState tank, Vector2 point, bool stopThere)
    {
        float distance = Vector2.Distance(tank.Position, point);
        float off = Angles.Wrap(Angles.YawOf(point - tank.Position) - tank.Yaw);
        sbyte steer = (sbyte)(MathF.Abs(off) <= SteerDeadZone ? 0 : off > 0 ? -1 : 1);   // +yaw is to the left; Steer +1 turns right
        bool braking = stopThere && distance <= tank.Speed * tank.Speed / (2 * tank.Spec.Braking) + BrakeMargin;
        sbyte throttle = (sbyte)(MathF.Abs(off) > TurnInPlace || braking || distance <= StopRadius / 2 ? 0 : 1);
        return (throttle, steer);
    }

    /// <summary>Whether the hull has come to a stop within <see cref="StopRadius"/> of <paramref name="point"/>.</summary>
    public static bool StoppedAt(in TankState tank, Vector2 point) =>
        Vector2.Distance(tank.Position, point) <= StopRadius && MathF.Abs(tank.Speed) < StoppedSpeed;
}
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 622、Net 50。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Sim tests/Tank.Sim.Tests
git commit -m "feat(sim): a skirmish bot — drives between waypoints, engages what its team can see within 250 m; steering shared with the patrol"
```

---

### Task 5：对战协议

设计 §5.1～§5.4、§5.7。`Tank.Net` 开始引用 `Tank.Sim`。新命名空间 `Tank.Net.Battle`：`BattleProtocol`（版本 2、连接密钥 `tank3d-v2`、消息号、量化函数和位宽、上限）；`BattleInputPacket`（最新快照 tick + 最近几条输入，每条 油门 2 位、转向 2 位、瞄准偏航 16 位、俯仰 16 位、开火 1 位、弹种 1 位）；`BattleSnapshot`（`BattleEntity` 列表、`OwnTank` 全精度、`MatchBlock`、`PlayerStats`、`ClientFeedback`）和 `BattleSnapshotCodec`（Quake 3 式：对照客户端确认过的基线快照逐字段增量，没有基线就全量）；`BattleMessages`（hello / welcome / reject / 房间状态 / 房间命令 / 开局（地图 `MapData` + 物件状态）/ 事件批（事件 + 物件状态）/ 局域网房间信息）。所有读函数对坏数据抛 `ProtocolException`，不越界。

**Files:**
- Create: `src/Tank.Net/Battle/BattleProtocol.cs`、`BattleInputPacket.cs`、`BattleSnapshot.cs`、`BattleSnapshotCodec.cs`、`BattleMessages.cs`
- Modify: `src/Tank.Net/Tank.Net.csproj`
- Test: 新建 `tests/Tank.Net.Tests/Battle/BattleProtocolTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Net.Tests/Battle/BattleProtocolTests.cs`：

```csharp
using System.Numerics;
using Tank.Net.Battle;
using BitReader = Tank.Net.Protocol.BitReader;
using BitWriter = Tank.Net.Protocol.BitWriter;
using ProtocolException = Tank.Net.Protocol.ProtocolException;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

public sealed class BattleProtocolTests
{
    private static BitReader ReaderFor(BitWriter w, BattleMessage expected)
    {
        var r = new BitReader(w.Data);
        Assert.Equal(expected, (BattleMessage)r.ReadByte());
        return r;
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(3.1f)]
    [InlineData(-3.14f)]
    [InlineData(1.2345f)]
    public void Angles_survive_quantisation_to_within_a_step(float angle)
    {
        float back = BattleProtocol.DequantizeAngle(BattleProtocol.QuantizeAngle(angle, BattleProtocol.AimBits), BattleProtocol.AimBits);
        Assert.True(MathF.Abs(Angles.Wrap(back - angle)) <= Angles.TwoPi / 65536, $"{angle} -> {back}");
        float coarse = BattleProtocol.DequantizeAngle(BattleProtocol.QuantizeAngle(angle, BattleProtocol.YawBits), BattleProtocol.YawBits);
        Assert.True(MathF.Abs(Angles.Wrap(coarse - angle)) <= Angles.TwoPi / 4096);
        float pitch = System.Math.Clamp(angle / 2, -1.5f, 1.5f);
        float pitchBack = BattleProtocol.DequantizePitch(BattleProtocol.QuantizePitch(pitch, BattleProtocol.PitchBits), BattleProtocol.PitchBits);
        Assert.True(MathF.Abs(pitchBack - pitch) <= Angles.Pi / 1023);
        // Quantising a quantised value changes nothing: a client predicting with QuantizedAim matches the server exactly.
        var (qy, qp) = BattleProtocol.QuantizedAim(angle, pitch);
        Assert.Equal((qy, qp), BattleProtocol.QuantizedAim(qy, qp));
    }

    [Fact]
    public void Durability_quantises_with_down_and_untouched_kept_exact()
    {
        Assert.Equal(0, BattleProtocol.QuantizeDurability(0, 900));
        Assert.Equal(255, BattleProtocol.QuantizeDurability(900, 900));
        Assert.InRange(BattleProtocol.QuantizeDurability(1, 900), 1, 254);
        Assert.InRange(BattleProtocol.QuantizeDurability(899, 900), 1, 254);
        Assert.Equal(0, BattleProtocol.DequantizeDurability(0, 900));
        Assert.Equal(900, BattleProtocol.DequantizeDurability(255, 900));
        Assert.InRange(BattleProtocol.DequantizeDurability(BattleProtocol.QuantizeDurability(450, 900), 900), 440, 460);
        Assert.Equal(1, BattleProtocol.DequantizeDurability(BattleProtocol.QuantizeDurability(1, 1), 1));
    }

    [Fact]
    public void Inputs_round_trip_with_the_aim_quantised()
    {
        var w = new BitWriter();
        var (yaw, pitch) = BattleProtocol.QuantizedAim(2.5f, -0.3f);
        var inputs = new[]
        {
            new InputCmd(107, 1, -1, yaw, pitch, true, AmmoType.HE),
            new InputCmd(106, -1, 0, yaw, pitch, false, AmmoType.AP),
            new InputCmd(105, 0, 1, 0f, 0f, true, AmmoType.AP),
        };
        BattleInputPacket.Write(w, 98, inputs);
        Assert.True(w.ByteLength <= 1 + 2 + 2 + 1 + 3 * 5, $"{w.ByteLength} bytes");
        BitReader r = ReaderFor(w, BattleMessage.Input);
        var output = new InputCmd[BattleProtocol.MaxRedundantInputs];
        int count = BattleInputPacket.Read(ref r, out int ack, output);
        Assert.Equal(3, count);
        Assert.Equal(98, ack);
        Assert.Equal(inputs[0], output[0]);
        Assert.Equal(inputs[1], output[1]);
        Assert.Equal(inputs[2] with { AimYaw = BattleProtocol.QuantizedAim(0f, 0f).Yaw, AimPitch = BattleProtocol.QuantizedAim(0f, 0f).Pitch }, output[2]);
    }

    [Fact]
    public void Room_messages_round_trip()
    {
        var w = new BitWriter();
        var players = new[] { new RoomPlayer(0, "安娜", 0, 1, true, true, false), new RoomPlayer(3, "Bot 2", 1, 3, true, true, true) };
        BattleMessages.Write(w, new RoomStateMessage(BattleMode.Annihilation, 0, 2, false, players));
        BitReader r = ReaderFor(w, BattleMessage.RoomState);
        RoomStateMessage state = BattleMessages.ReadRoomState(ref r);
        Assert.Equal(BattleMode.Annihilation, state.Mode);
        Assert.Equal(2, state.Bots);
        Assert.Equal(players, state.Players);

        w.Reset();
        BattleMessages.Write(w, new RoomCommandMessage(RoomCommandKind.SetTank, "heavy"));
        r = ReaderFor(w, BattleMessage.RoomCommand);
        Assert.Equal(new RoomCommandMessage(RoomCommandKind.SetTank, "heavy"), BattleMessages.ReadRoomCommand(ref r));

        w.Reset();
        BattleMessages.Write(w, new HelloMessage(2, "Bob", 0xDEADBEEF));
        r = ReaderFor(w, BattleMessage.Hello);
        Assert.Equal(new HelloMessage(2, "Bob", 0xDEADBEEF), BattleMessages.ReadHello(ref r));

        w.Reset();
        BattleMessages.Write(w, new RoomInfoMessage(2, "周末房", BattleMode.FreeForAll, 3, 10, true, 27015));
        r = ReaderFor(w, BattleMessage.RoomInfo);
        Assert.Equal(new RoomInfoMessage(2, "周末房", BattleMode.FreeForAll, 3, 10, true, 27015), BattleMessages.ReadRoomInfo(ref r));
    }

    [Fact]
    public void The_sandbox_map_goes_over_the_wire_in_under_90_KB_and_comes_back_the_same()
    {
        MapData map = MapData.Of(SandboxMap.Create());
        var w = new BitWriter(128 * 1024);
        var props = new[] { new PropState(300, 0), new PropState(301, 128) };
        BattleMessages.Write(w, new MatchStartMessage(BattleMode.TeamBattle, 3, 150, 12750, map, props, 7));
        Assert.True(w.ByteLength < 90 * 1024, $"{w.ByteLength / 1024} KB");
        BitReader r = ReaderFor(w, BattleMessage.MatchStart);
        MatchStartMessage m = BattleMessages.ReadMatchStart(ref r);
        Assert.Equal(BattleMode.TeamBattle, m.Mode);
        Assert.Equal((3, 150, 12750, 7), (m.MatchNumber, m.CurrentTick, m.EndTick, m.MyTankId));
        Assert.Equal(map.HeightsCm, m.Map.HeightsCm);
        Assert.Equal(map.Props, m.Map.Props);
        Assert.Equal((map.SamplesX, map.SamplesZ, map.CellSize, map.Border), (m.Map.SamplesX, m.Map.SamplesZ, m.Map.CellSize, m.Map.Border));
        Assert.Equal(props, m.Props);
        Battlefield rebuilt = m.Map.Build();
        Assert.Equal(SandboxMap.Create().Props, rebuilt.Props);
    }

    [Fact]
    public void Events_round_trip_field_for_field()
    {
        var events = new[]
        {
            new GameEvent(GameEventType.ShellFired, 40, 3, 17, new Vector3(1.5f, 2.25f, -3f), new Vector3(100.125f, -20f, 700f), Ammo: AmmoType.HE),
            new GameEvent(GameEventType.TankHit, 40, 3, 17, new Vector3(9, 1, 9), Vector3.UnitZ, 5, HitResult.Ricochet, 0, AmmoType.AP),
            new GameEvent(GameEventType.PropDamaged, 40, 3, 17, new Vector3(9, 1, 9), new Vector3(0, 0, 1), Damage: 120, PropId: 333),
            new GameEvent(GameEventType.TankDestroyed, 40, 3, 17, new Vector3(9, 0, 9), Vector3.Zero, TargetTankId: 5),
        };
        var props = new[] { new PropState(333, 200) };
        var w = new BitWriter();
        BattleMessages.Write(w, new EventBatch(40, events, props));
        BitReader r = ReaderFor(w, BattleMessage.Events);
        EventBatch batch = BattleMessages.ReadEvents(ref r);
        Assert.Equal(40, batch.Tick);
        Assert.Equal(events, batch.Events);
        Assert.Equal(props, batch.Props);
    }

    private static BattleSnapshot Snapshot(int tick, params BattleEntity[] entities)
    {
        var s = new BattleSnapshot { Tick = tick, EntityCount = entities.Length };
        Array.Copy(entities, s.Entities, entities.Length);
        s.Match = new MatchBlock { Phase = MatchPhase.Playing, TicksLeft = 12000 - tick, Spotted = tick % 2 == 0, WinnerTeam = MatchRules.NoTeam, WinnerSlot = MatchRules.NoSlot };
        s.HasOwn = true;
        s.Own = new OwnTank { Id = 1, X = 100.123f, Z = 200.456f, Yaw = 0.5f, TurretYaw = -0.25f, GunPitch = 0.05f, Speed = 3.3f, Reload = 1.25f, Bloom = 0.4f, Ammo = AmmoType.HE, ApLeft = 30, HeLeft = 12, ShotCount = tick, Health = 650, ProtectedUntil = 150, LastFired = 30 };
        s.Players[0] = new PlayerStats { Active = true, Team = 0, Spec = 1, TankId = 1, Kills = 2, Deaths = 1, Alive = true };
        s.Players[4] = new PlayerStats { Active = true, Team = 1, Spec = 3, TankId = 7, Kills = 0, Deaths = 3, Alive = false, Rank = 2 };
        return s;
    }

    private static BattleEntity Entity(int id, int x, int z, ushort health = 900) => new()
    {
        Id = id, Slot = (byte)(id % 10), Spec = 2, Team = (byte)(id % 2), X = x, Z = z, Yaw = 1000, TurretYaw = 2000, Pitch = 512, Speed = -23, Health = health, Flags = TankFlags.Protected,
    };

    [Fact]
    public void A_full_snapshot_round_trips_and_a_delta_carries_only_changes()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot a = Snapshot(100, Entity(2, 10000, 20000), Entity(7, 30000, 40000));
        var w = new BitWriter();
        codec.Write(w, a, null, new ClientFeedback { LastProcessedInputTick = 103, InputSlack = 2 });
        int fullBytes = w.ByteLength;
        BitReader r = ReaderFor(w, BattleMessage.Snapshot);
        var decodedA = new BattleSnapshot();
        codec.Read(ref r, _ => null, decodedA, out ClientFeedback feedback, out int baseTick);
        Assert.Equal(0, baseTick);
        Assert.Equal((103, 2), (feedback.LastProcessedInputTick, feedback.InputSlack));
        Assert.Equal(a.Tick, decodedA.Tick);
        Assert.Equal(a.EntitySpan.ToArray(), decodedA.EntitySpan.ToArray());
        Assert.Equal(a.Own, decodedA.Own);
        Assert.Equal(a.Match, decodedA.Match);
        Assert.Equal(a.Players, decodedA.Players);

        // Tick 101: entity 2 moved a little, 7 is gone (out of sight), 9 appeared; own tank and scoreboard unchanged.
        BattleSnapshot b = Snapshot(101, Entity(2, 10050, 20000), Entity(9, 1, 2, 300));
        b.Own = a.Own;
        b.Match = a.Match with { TicksLeft = a.Match.TicksLeft - 1 };
        w.Reset();
        codec.Write(w, b, a, default);
        Assert.True(w.ByteLength < fullBytes / 2, $"delta {w.ByteLength} bytes vs full {fullBytes}");
        r = ReaderFor(w, BattleMessage.Snapshot);
        var decodedB = new BattleSnapshot();
        codec.Read(ref r, tick => tick == 100 ? decodedA : null, decodedB, out _, out baseTick);
        Assert.Equal(100, baseTick);
        Assert.Equal(b.EntitySpan.ToArray(), decodedB.EntitySpan.ToArray());
        Assert.Equal(a.Own, decodedB.Own);
        Assert.Equal(a.Players, decodedB.Players);
        Assert.Equal(b.Match, decodedB.Match);

        // Without the baseline the delta can't be read.
        byte[] delta = w.Data.ToArray();
        Assert.Throws<ProtocolException>(() =>
        {
            var rr = new BitReader(delta);
            rr.ReadByte();
            codec.Read(ref rr, _ => null, new BattleSnapshot(), out _, out _);
        });
    }

    [Fact]
    public void A_snapshot_without_an_own_tank_and_a_truncated_packet()
    {
        var codec = new BattleSnapshotCodec();
        BattleSnapshot s = Snapshot(5, Entity(3, 1, 1));
        s.HasOwn = false;
        var w = new BitWriter();
        codec.Write(w, s, null, default);
        BitReader r = ReaderFor(w, BattleMessage.Snapshot);
        var decoded = new BattleSnapshot();
        codec.Read(ref r, _ => null, decoded, out _, out _);
        Assert.False(decoded.HasOwn);
        Assert.Equal(default, decoded.Own);

        byte[] cut = w.Data[..(w.ByteLength / 2)].ToArray();
        Assert.Throws<ProtocolException>(() =>
        {
            var rr = new BitReader(cut);
            rr.ReadByte();
            codec.Read(ref rr, _ => null, new BattleSnapshot(), out _, out _);
        });
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Net.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`Tank.Net.Battle` 命名空间找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Net/Battle/BattleInputPacket.cs`：

```csharp
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Combat;

namespace Tank.Net.Battle;

/// <summary>
/// A client's input (design §5.3): the newest few commands, newest first (redundancy against loss), and the newest
/// snapshot tick the client has, which the server takes as the delta baseline. Throttle and steer take 2 bits each,
/// the aim 16 bits for yaw and 16 for pitch, fire and ammunition a bit each: 5 bytes a command. A client must predict
/// with the quantised aim (<see cref="BattleProtocol.QuantizedAim"/>) the server will read here.
/// </summary>
public static class BattleInputPacket
{
    public static void Write(BitWriter w, int ackSnapshotTick, ReadOnlySpan<InputCmd> inputs)
    {
        int count = System.Math.Min(inputs.Length, BattleProtocol.MaxRedundantInputs);
        if (count == 0) throw new ArgumentException("At least one input is required.", nameof(inputs));
        w.WriteByte((byte)BattleMessage.Input);
        w.WriteVarUInt((uint)inputs[0].Tick);
        w.WriteVarUInt((uint)System.Math.Max(0, ackSnapshotTick));
        w.WriteBits((uint)count, 3);
        for (int i = 0; i < count; i++)
        {
            InputCmd c = inputs[i];
            w.WriteBits((uint)(System.Math.Sign(c.Throttle) + 1), 2);
            w.WriteBits((uint)(System.Math.Sign(c.Steer) + 1), 2);
            w.WriteBits(BattleProtocol.QuantizeAngle(c.AimYaw, BattleProtocol.AimBits), BattleProtocol.AimBits);
            w.WriteBits(BattleProtocol.QuantizePitch(c.AimPitch, BattleProtocol.AimBits), BattleProtocol.AimBits);
            w.WriteBool(c.Fire);
            w.WriteBits(c.Ammo == AmmoType.HE ? 1u : 0u, 1);
        }
    }

    /// <summary>Decodes into <paramref name="output"/> (newest first; ticks count down from the newest) and returns the count.</summary>
    public static int Read(ref BitReader r, out int ackSnapshotTick, Span<InputCmd> output)
    {
        int newest = (int)r.ReadVarUInt();
        ackSnapshotTick = (int)r.ReadVarUInt();
        int count = (int)r.ReadBits(3);
        if (count == 0 || count > BattleProtocol.MaxRedundantInputs || count > output.Length) throw new ProtocolException("Bad input count.");
        for (int i = 0; i < count; i++)
        {
            uint throttle = r.ReadBits(2), steer = r.ReadBits(2);
            if (throttle > 2 || steer > 2) throw new ProtocolException("Bad axis value.");
            float yaw = BattleProtocol.DequantizeAngle(r.ReadBits(BattleProtocol.AimBits), BattleProtocol.AimBits);
            float pitch = BattleProtocol.DequantizePitch(r.ReadBits(BattleProtocol.AimBits), BattleProtocol.AimBits);
            bool fire = r.ReadBool();
            AmmoType ammo = r.ReadBits(1) == 1 ? AmmoType.HE : AmmoType.AP;
            output[i] = new InputCmd(newest - i, (sbyte)((int)throttle - 1), (sbyte)((int)steer - 1), yaw, pitch, fire, ammo);
        }
        return count;
    }
}
```

新建 `src/Tank.Net/Battle/BattleMessages.cs`：

```csharp
using System.Numerics;
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

public sealed record HelloMessage(int ProtocolVersion, string Name, uint SessionToken);

public sealed record WelcomeMessage(byte Slot, uint SessionToken);

public sealed record RejectMessage(string Reason);

/// <summary>One seat in the room. <see cref="TankIndex"/> is an index into <c>TankCatalog.All</c>.</summary>
public sealed record RoomPlayer(byte Slot, string Name, byte Team, byte TankIndex, bool Ready, bool Connected, bool IsBot);

public sealed record RoomStateMessage(BattleMode Mode, byte HostSlot, byte Bots, bool MatchRunning, IReadOnlyList<RoomPlayer> Players);

public enum RoomCommandKind : byte
{
    SetMode = 0,
    SetTeam = 1,
    SetTank = 2,
    SetBots = 3,
    SetReady = 4,
    StartMatch = 5,
    ReturnToRoom = 6,
}

public sealed record RoomCommandMessage(RoomCommandKind Kind, string Arg);

/// <summary>A destructible prop's hit points, quantised (see <see cref="BattleProtocol.QuantizeDurability"/>).</summary>
public readonly record struct PropState(int PropId, byte Durability);

/// <summary>
/// Everything a client needs to join a match in progress (design §5.4): the map itself, the props that aren't as new,
/// the clock, and which tank is the client's (0: none, it spectates or waits).
/// </summary>
public sealed record MatchStartMessage(BattleMode Mode, int MatchNumber, int CurrentTick, int EndTick, MapData Map, IReadOnlyList<PropState> Props, int MyTankId);

/// <summary>A tick's events for one client, with the new hit points of every prop that changed (design §5.6).</summary>
public readonly record struct EventBatch(int Tick, IReadOnlyList<GameEvent> Events, IReadOnlyList<PropState> Props);

public sealed record RoomInfoMessage(int ProtocolVersion, string RoomName, BattleMode Mode, int Players, int MaxPlayers, bool MatchRunning, int Port);

/// <summary>Encoders and decoders for everything but inputs and snapshots. Decoders assume the type byte was read.</summary>
public static class BattleMessages
{
    private const int MaxEventsPerBatch = 1024;
    private const int MaxPropsPerMessage = 4096;
    private const int MaxMapSamples = 1024 * 1024;

    // ---- Hello / Welcome / Reject ----

    public static void Write(BitWriter w, HelloMessage m)
    {
        w.WriteByte((byte)BattleMessage.Hello);
        w.WriteVarUInt((uint)m.ProtocolVersion);
        w.WriteString(m.Name, BattleProtocol.MaxNameBytes);
        w.WriteBits(m.SessionToken, 32);
    }

    public static HelloMessage ReadHello(ref BitReader r) => new((int)r.ReadVarUInt(), r.ReadString(BattleProtocol.MaxNameBytes), r.ReadBits(32));

    public static void Write(BitWriter w, WelcomeMessage m)
    {
        w.WriteByte((byte)BattleMessage.Welcome);
        w.WriteByte(m.Slot);
        w.WriteBits(m.SessionToken, 32);
    }

    public static WelcomeMessage ReadWelcome(ref BitReader r) => new(r.ReadByte(), r.ReadBits(32));

    public static void Write(BitWriter w, RejectMessage m)
    {
        w.WriteByte((byte)BattleMessage.Reject);
        w.WriteString(m.Reason, 128);
    }

    public static RejectMessage ReadReject(ref BitReader r) => new(r.ReadString(128));

    // ---- Room ----

    public static void Write(BitWriter w, RoomStateMessage m)
    {
        w.WriteByte((byte)BattleMessage.RoomState);
        w.WriteByte((byte)m.Mode);
        w.WriteByte(m.HostSlot);
        w.WriteByte(m.Bots);
        w.WriteBool(m.MatchRunning);
        w.WriteVarUInt((uint)m.Players.Count);
        foreach (RoomPlayer p in m.Players)
        {
            w.WriteByte(p.Slot);
            w.WriteString(p.Name, BattleProtocol.MaxNameBytes);
            w.WriteBits(p.Team, 4);
            w.WriteBits(p.TankIndex, 2);
            w.WriteBool(p.Ready);
            w.WriteBool(p.Connected);
            w.WriteBool(p.IsBot);
        }
    }

    public static RoomStateMessage ReadRoomState(ref BitReader r)
    {
        BattleMode mode = ReadMode(ref r);
        byte host = r.ReadByte(), bots = r.ReadByte();
        bool running = r.ReadBool();
        uint count = r.ReadVarUInt();
        if (count > BattleProtocol.MaxPlayers) throw new ProtocolException("Too many room players.");
        var players = new List<RoomPlayer>((int)count);
        for (int i = 0; i < count; i++)
            players.Add(new RoomPlayer(r.ReadByte(), r.ReadString(BattleProtocol.MaxNameBytes), (byte)r.ReadBits(4), (byte)r.ReadBits(2), r.ReadBool(), r.ReadBool(), r.ReadBool()));
        return new RoomStateMessage(mode, host, bots, running, players);
    }

    public static void Write(BitWriter w, RoomCommandMessage m)
    {
        w.WriteByte((byte)BattleMessage.RoomCommand);
        w.WriteByte((byte)m.Kind);
        w.WriteString(m.Arg);
    }

    public static RoomCommandMessage ReadRoomCommand(ref BitReader r)
    {
        byte kind = r.ReadByte();
        if (kind > (byte)RoomCommandKind.ReturnToRoom) throw new ProtocolException("Unknown room command.");
        return new RoomCommandMessage((RoomCommandKind)kind, r.ReadString());
    }

    // ---- Match start ----

    public static void Write(BitWriter w, MatchStartMessage m)
    {
        w.WriteByte((byte)BattleMessage.MatchStart);
        w.WriteByte((byte)m.Mode);
        w.WriteVarUInt((uint)m.MatchNumber);
        w.WriteVarUInt((uint)m.CurrentTick);
        w.WriteVarUInt((uint)m.EndTick);
        WriteMap(w, m.Map);
        WriteProps(w, m.Props);
        w.WriteVarUInt((uint)m.MyTankId);
    }

    public static MatchStartMessage ReadMatchStart(ref BitReader r)
    {
        BattleMode mode = ReadMode(ref r);
        int number = (int)r.ReadVarUInt(), tick = (int)r.ReadVarUInt(), end = (int)r.ReadVarUInt();
        MapData map = ReadMap(ref r);
        PropState[] props = ReadProps(ref r);
        int mine = (int)r.ReadVarUInt();
        return new MatchStartMessage(mode, number, tick, end, map, props, mine);
    }

    /// <summary>
    /// The map (design §5.4): heights as the difference from the sample before, props field by field. About 60 KB for
    /// the sandbox map's 201 × 201 samples and 450 props.
    /// </summary>
    public static void WriteMap(BitWriter w, MapData map)
    {
        w.WriteVarUInt((uint)map.SamplesX);
        w.WriteVarUInt((uint)map.SamplesZ);
        WriteFloat(w, map.CellSize);
        WriteFloat(w, map.Border);
        int previous = 0;
        foreach (int h in map.HeightsCm)
        {
            w.WriteVarInt(h - previous);
            previous = h;
        }
        w.WriteVarUInt((uint)map.Props.Length);
        foreach (Prop p in map.Props)
        {
            w.WriteVarUInt((uint)p.Id);
            w.WriteByte((byte)p.Kind);
            WriteFloat(w, p.Footprint.Center.X);
            WriteFloat(w, p.Footprint.Center.Y);
            WriteFloat(w, p.Footprint.HalfExtents.X);
            WriteFloat(w, p.Footprint.HalfExtents.Y);
            WriteFloat(w, p.Footprint.Yaw);
            WriteFloat(w, p.Bottom);
            WriteFloat(w, p.Height);
        }
    }

    public static MapData ReadMap(ref BitReader r)
    {
        int sx = (int)r.ReadVarUInt(), sz = (int)r.ReadVarUInt();
        if (sx < 2 || sz < 2 || (long)sx * sz > MaxMapSamples) throw new ProtocolException("Bad map size.");
        float cell = ReadFloat(ref r), border = ReadFloat(ref r);
        var heights = new int[sx * sz];
        int previous = 0;
        for (int k = 0; k < heights.Length; k++) heights[k] = previous += r.ReadVarInt();
        uint count = r.ReadVarUInt();
        if (count > MaxPropsPerMessage) throw new ProtocolException("Too many props.");
        var props = new Prop[count];
        for (int i = 0; i < count; i++)
        {
            int id = (int)r.ReadVarUInt();
            byte kind = r.ReadByte();
            if (kind > (byte)PropKind.Fence) throw new ProtocolException("Unknown prop kind.");
            var center = new Vector2(ReadFloat(ref r), ReadFloat(ref r));
            var half = new Vector2(ReadFloat(ref r), ReadFloat(ref r));
            float yaw = ReadFloat(ref r), bottom = ReadFloat(ref r), height = ReadFloat(ref r);
            props[i] = new Prop(id, (PropKind)kind, new Obb2(center, half, yaw), bottom, height);
        }
        try
        {
            return new MapData(sx, sz, cell, heights, props, border);
        }
        catch (ArgumentException ex)
        {
            throw new ProtocolException($"Bad map: {ex.Message}");
        }
    }

    // ---- Events ----

    public static void Write(BitWriter w, in EventBatch batch)
    {
        w.WriteByte((byte)BattleMessage.Events);
        w.WriteVarUInt((uint)batch.Tick);
        w.WriteVarUInt((uint)batch.Events.Count);
        for (int i = 0; i < batch.Events.Count; i++)   // indexed: a foreach over the interface boxes an enumerator every tick
        {
            GameEvent e = batch.Events[i];
            w.WriteByte((byte)e.Type);
            w.WriteVarUInt((uint)e.TankId);
            w.WriteVarUInt((uint)e.ShellId);
            WriteVector(w, e.Position);
            WriteVector(w, e.Velocity);
            w.WriteVarUInt((uint)e.TargetTankId);
            w.WriteBits((uint)e.Result, 2);
            w.WriteVarUInt((uint)e.Damage);
            w.WriteBits((uint)e.Ammo, 1);
            w.WriteVarUInt((uint)e.PropId);
        }
        WriteProps(w, batch.Props);
    }

    public static EventBatch ReadEvents(ref BitReader r)
    {
        int tick = (int)r.ReadVarUInt();
        uint count = r.ReadVarUInt();
        if (count > MaxEventsPerBatch) throw new ProtocolException("Too many events.");
        var events = new GameEvent[count];
        for (int i = 0; i < count; i++)
        {
            byte type = r.ReadByte();
            if (type > (byte)GameEventType.PropDestroyed) throw new ProtocolException("Unknown event type.");
            int tank = (int)r.ReadVarUInt(), shell = (int)r.ReadVarUInt();
            Vector3 position = ReadVector(ref r), velocity = ReadVector(ref r);
            int target = (int)r.ReadVarUInt();
            var result = (HitResult)r.ReadBits(2);
            int damage = (int)r.ReadVarUInt();
            var ammo = (AmmoType)r.ReadBits(1);
            int prop = (int)r.ReadVarUInt();
            events[i] = new GameEvent((GameEventType)type, tick, tank, shell, position, velocity, target, result, damage, ammo, prop);
        }
        return new EventBatch(tick, events, ReadProps(ref r));
    }

    private static void WriteProps(BitWriter w, IReadOnlyList<PropState> props)
    {
        w.WriteVarUInt((uint)props.Count);
        for (int i = 0; i < props.Count; i++)
        {
            PropState p = props[i];
            w.WriteVarUInt((uint)p.PropId);
            w.WriteByte(p.Durability);
        }
    }

    private static PropState[] ReadProps(ref BitReader r)
    {
        uint count = r.ReadVarUInt();
        if (count > MaxPropsPerMessage) throw new ProtocolException("Too many prop states.");
        var props = new PropState[count];
        for (int i = 0; i < count; i++) props[i] = new PropState((int)r.ReadVarUInt(), r.ReadByte());
        return props;
    }

    // ---- Discovery ----

    public static void Write(BitWriter w, RoomInfoMessage m)
    {
        w.WriteByte((byte)BattleMessage.RoomInfo);
        w.WriteVarUInt((uint)m.ProtocolVersion);
        w.WriteString(m.RoomName, BattleProtocol.MaxRoomNameBytes);
        w.WriteByte((byte)m.Mode);
        w.WriteVarUInt((uint)m.Players);
        w.WriteVarUInt((uint)m.MaxPlayers);
        w.WriteBool(m.MatchRunning);
        w.WriteVarUInt((uint)m.Port);
    }

    public static RoomInfoMessage ReadRoomInfo(ref BitReader r) =>
        new((int)r.ReadVarUInt(), r.ReadString(BattleProtocol.MaxRoomNameBytes), ReadMode(ref r), (int)r.ReadVarUInt(), (int)r.ReadVarUInt(), r.ReadBool(), (int)r.ReadVarUInt());

    // ---- primitives ----

    public static void WriteFloat(BitWriter w, float value) => w.WriteBits(BitConverter.SingleToUInt32Bits(value), 32);

    public static float ReadFloat(ref BitReader r)
    {
        float value = BitConverter.UInt32BitsToSingle(r.ReadBits(32));
        return float.IsFinite(value) ? value : throw new ProtocolException("Non-finite number.");
    }

    public static void WriteVector(BitWriter w, Vector3 v)
    {
        WriteFloat(w, v.X);
        WriteFloat(w, v.Y);
        WriteFloat(w, v.Z);
    }

    public static Vector3 ReadVector(ref BitReader r) => new(ReadFloat(ref r), ReadFloat(ref r), ReadFloat(ref r));

    private static BattleMode ReadMode(ref BitReader r)
    {
        byte mode = r.ReadByte();
        return mode <= (byte)BattleMode.FreeForAll ? (BattleMode)mode : throw new ProtocolException("Unknown battle mode.");
    }
}
```

新建 `src/Tank.Net/Battle/BattleProtocol.cs`：

```csharp
using Tank.Sim.Geometry;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

/// <summary>
/// Message ids of the battle protocol (stage 4 design §5.2). The first byte of every packet. Discovery shares its
/// request with the old game (<see cref="Protocol.MessageType.Discover"/>); a battle room answers with <see cref="RoomInfo"/>.
/// </summary>
public enum BattleMessage : byte
{
    // client -> server
    Hello = 1,
    Input = 2,
    RoomCommand = 3,
    RequestMatchState = 4,

    // server -> client
    Welcome = 20,
    Reject = 21,
    RoomState = 22,
    MatchStart = 23,
    Snapshot = 24,
    Events = 25,

    // unconnected (LAN discovery)
    RoomInfo = 42,
}

/// <summary>Numbers of the battle protocol (stage 4 design §5), and the quantisation the wire uses for angles and positions.</summary>
public static class BattleProtocol
{
    public const int Version = 2;
    public const string ConnectKey = "tank3d-v2";
    public const int MaxPlayers = ModeRules.MaxPlayers;
    public const int MaxRedundantInputs = 4;
    public const int MaxNameBytes = 24;
    public const int MaxRoomNameBytes = 48;

    /// <summary>Positions on the wire are whole centimetres.</summary>
    public const float PositionUnit = 0.01f;

    /// <summary>Other tanks' speed on the wire, m/s per step.</summary>
    public const float SpeedUnit = 0.1f;

    public const int AimBits = 16;
    public const int YawBits = 12;
    public const int PitchBits = 10;

    /// <summary>An angle in (−π, π] as <paramref name="bits"/> bits.</summary>
    public static uint QuantizeAngle(float angle, int bits)
    {
        uint steps = 1u << bits;
        float turns = (Angles.Wrap(angle) + Angles.Pi) / Angles.TwoPi;   // 0 … 1
        return (uint)MathF.Round(turns * steps) & (steps - 1);
    }

    public static float DequantizeAngle(uint q, int bits) => Angles.Wrap(q / (float)(1u << bits) * Angles.TwoPi - Angles.Pi);

    /// <summary>A pitch in [−π/2, π/2] as <paramref name="bits"/> bits.</summary>
    public static uint QuantizePitch(float pitch, int bits)
    {
        uint max = (1u << bits) - 1;
        float share = (System.Math.Clamp(pitch, -Angles.Pi / 2, Angles.Pi / 2) + Angles.Pi / 2) / Angles.Pi;
        return (uint)MathF.Round(share * max);
    }

    public static float DequantizePitch(uint q, int bits) => q / (float)((1u << bits) - 1) * Angles.Pi - Angles.Pi / 2;

    /// <summary>The aim a client should predict with: what the server will read from the input packet.</summary>
    public static (float Yaw, float Pitch) QuantizedAim(float yaw, float pitch) =>
        (DequantizeAngle(QuantizeAngle(yaw, AimBits), AimBits), DequantizePitch(QuantizePitch(pitch, AimBits), AimBits));

    public static int ToCm(float metres) => (int)MathF.Round(metres / PositionUnit);

    public static float FromCm(int cm) => cm * PositionUnit;

    /// <summary>A prop's hit points as a byte: 0 only when down, 255 only when untouched.</summary>
    public static byte QuantizeDurability(int durability, int max)
    {
        if (durability <= 0) return 0;
        if (durability >= max) return 255;
        return (byte)System.Math.Clamp((int)MathF.Round(durability * 254f / max), 1, 254);
    }

    public static int DequantizeDurability(byte q, int max) => q switch
    {
        0 => 0,
        255 => max,
        _ => System.Math.Clamp((int)MathF.Round(q * max / 254f), 1, max - 1),
    };
}
```

新建 `src/Tank.Net/Battle/BattleSnapshot.cs`：

```csharp
using Tank.Sim.Combat;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

[Flags]
public enum TankFlags : byte
{
    None = 0,
    Destroyed = 1,
    Protected = 2,
}

/// <summary>Another tank as a client sees it (design §5.5): whole centimetres, 12-bit headings, 10-bit gun pitch.</summary>
public struct BattleEntity : IEquatable<BattleEntity>
{
    public int Id;
    public byte Slot;      // the player's room slot
    public byte Spec;      // index into TankCatalog.All
    public byte Team;
    public int X;          // cm
    public int Z;          // cm
    public ushort Yaw;     // 12 bits
    public ushort TurretYaw;
    public ushort Pitch;   // 10 bits
    public short Speed;    // tenths of a metre per second
    public ushort Health;
    public TankFlags Flags;

    public readonly bool Has(TankFlags flag) => (Flags & flag) != 0;

    public readonly bool Equals(BattleEntity o) =>
        Id == o.Id && Slot == o.Slot && Spec == o.Spec && Team == o.Team && X == o.X && Z == o.Z && Yaw == o.Yaw &&
        TurretYaw == o.TurretYaw && Pitch == o.Pitch && Speed == o.Speed && Health == o.Health && Flags == o.Flags;

    public override readonly bool Equals(object? obj) => obj is BattleEntity e && Equals(e);

    public override readonly int GetHashCode() => HashCode.Combine(Id, X, Z, Yaw, TurretYaw, Health, Flags);
}

/// <summary>The client's own tank, exactly as the server has it, for reconciliation (design §5.5).</summary>
public struct OwnTank : IEquatable<OwnTank>
{
    public int Id;
    public float X, Z, Yaw, TurretYaw, GunPitch, Speed, Reload, Bloom;
    public AmmoType Ammo;
    public int ApLeft, HeLeft, ShotCount, Health, ProtectedUntil, LastFired;
    public bool Destroyed;

    public readonly bool Equals(OwnTank o) =>
        Id == o.Id && X == o.X && Z == o.Z && Yaw == o.Yaw && TurretYaw == o.TurretYaw && GunPitch == o.GunPitch && Speed == o.Speed &&
        Reload == o.Reload && Bloom == o.Bloom && Ammo == o.Ammo && ApLeft == o.ApLeft && HeLeft == o.HeLeft && ShotCount == o.ShotCount &&
        Health == o.Health && ProtectedUntil == o.ProtectedUntil && LastFired == o.LastFired && Destroyed == o.Destroyed;

    public override readonly bool Equals(object? obj) => obj is OwnTank o && Equals(o);

    public override readonly int GetHashCode() => HashCode.Combine(Id, X, Z, Yaw, ShotCount, Health);
}

/// <summary>The match as this client should show it (design §5.5).</summary>
public struct MatchBlock : IEquatable<MatchBlock>
{
    public MatchPhase Phase;
    public int TicksLeft;
    public bool Spotted;       // an enemy sees the client's tank
    public byte WinnerTeam;    // MatchRules.NoTeam while playing or drawn
    public byte WinnerSlot;    // MatchRules.NoSlot unless a free-for-all has ended
    public int RespawnIn;      // ticks until the client's tank comes back, 0 otherwise

    public readonly bool Equals(MatchBlock o) =>
        Phase == o.Phase && TicksLeft == o.TicksLeft && Spotted == o.Spotted && WinnerTeam == o.WinnerTeam && WinnerSlot == o.WinnerSlot && RespawnIn == o.RespawnIn;

    public override readonly bool Equals(object? obj) => obj is MatchBlock m && Equals(m);

    public override readonly int GetHashCode() => HashCode.Combine(Phase, TicksLeft, Spotted, WinnerTeam, WinnerSlot, RespawnIn);
}

/// <summary>One player's line on the scoreboard.</summary>
public struct PlayerStats : IEquatable<PlayerStats>
{
    public bool Active;
    public byte Team;
    public byte Spec;
    public int TankId;
    public int Kills;
    public int Deaths;
    public bool Alive;
    public byte Rank;

    public readonly bool Equals(PlayerStats o) =>
        Active == o.Active && Team == o.Team && Spec == o.Spec && TankId == o.TankId && Kills == o.Kills && Deaths == o.Deaths && Alive == o.Alive && Rank == o.Rank;

    public override readonly bool Equals(object? obj) => obj is PlayerStats p && Equals(p);

    public override readonly int GetHashCode() => HashCode.Combine(Active, Team, TankId, Kills, Deaths, Alive, Rank);
}

/// <summary>
/// What one client is told about one server tick: the tanks it may know of (sorted by id), its own tank in full, the
/// match, and the scoreboard. Built per client on the server; kept per tick on both sides as a delta baseline.
/// </summary>
public sealed class BattleSnapshot
{
    public const int MaxEntities = Tank.Sim.Simulation.MaxTanks;

    public int Tick = -1;
    public readonly BattleEntity[] Entities = new BattleEntity[MaxEntities];
    public int EntityCount;
    public bool HasOwn;
    public OwnTank Own;
    public MatchBlock Match;
    public readonly PlayerStats[] Players = new PlayerStats[BattleProtocol.MaxPlayers];

    public ReadOnlySpan<BattleEntity> EntitySpan => Entities.AsSpan(0, EntityCount);

    public void CopyFrom(BattleSnapshot other)
    {
        Tick = other.Tick;
        Array.Copy(other.Entities, Entities, other.EntityCount);
        EntityCount = other.EntityCount;
        HasOwn = other.HasOwn;
        Own = other.Own;
        Match = other.Match;
        Array.Copy(other.Players, Players, Players.Length);
    }

    public bool TryFind(int id, out BattleEntity entity)
    {
        int lo = 0, hi = EntityCount - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >> 1;
            int midId = Entities[mid].Id;
            if (midId == id)
            {
                entity = Entities[mid];
                return true;
            }
            if (midId < id) lo = mid + 1;
            else hi = mid - 1;
        }
        entity = default;
        return false;
    }
}

/// <summary>Per-client feedback sent with every snapshot (prediction and clock sync).</summary>
public struct ClientFeedback
{
    public int LastProcessedInputTick;
    public int InputSlack;   // newest received input tick minus the tick it is simulated at
}
```

新建 `src/Tank.Net/Battle/BattleSnapshotCodec.cs`：

```csharp
using Tank.Net.Protocol;
using Tank.Sim.Combat;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

/// <summary>
/// Encodes a client's snapshot as a delta against a snapshot it has acknowledged (Quake 3 model): entities that
/// changed carry a mask of changed fields, new ones every field, those gone a removal; the own tank and the
/// scoreboard go in full when they changed. A lost packet never needs resending: the next snapshot is encoded against
/// an older acknowledged baseline. Instances hold scratch buffers and are not thread-safe.
/// </summary>
public sealed class BattleSnapshotCodec
{
    private const ushort MaskMeta = 1, MaskX = 2, MaskZ = 4, MaskYaw = 8, MaskTurret = 16, MaskPitch = 32, MaskSpeed = 64, MaskHealth = 128, MaskFlags = 256;
    private const ushort MaskAll = 511;
    private const int MaskBits = 9;

    private readonly BattleEntity[] _changed = new BattleEntity[BattleSnapshot.MaxEntities];
    private readonly int[] _removed = new int[BattleSnapshot.MaxEntities];

    public void Write(BitWriter w, BattleSnapshot current, BattleSnapshot? baseline, in ClientFeedback feedback)
    {
        w.WriteByte((byte)BattleMessage.Snapshot);
        w.WriteVarUInt((uint)current.Tick);
        w.WriteVarUInt(baseline is null ? 0u : (uint)baseline.Tick);
        w.WriteVarInt(feedback.LastProcessedInputTick);
        w.WriteVarInt(feedback.InputSlack);
        WriteMatch(w, current.Match);

        w.WriteBool(current.HasOwn);
        if (current.HasOwn)
        {
            bool same = baseline is { HasOwn: true } && baseline.Own.Equals(current.Own);
            w.WriteBool(same);
            if (!same) WriteOwn(w, current.Own);
        }

        bool sameStats = baseline is not null && baseline.Players.AsSpan().SequenceEqual(current.Players);
        w.WriteBool(sameStats);
        if (!sameStats) WriteStats(w, current.Players);

        ReadOnlySpan<BattleEntity> cur = current.EntitySpan;
        ReadOnlySpan<BattleEntity> bas = baseline is null ? ReadOnlySpan<BattleEntity>.Empty : baseline.EntitySpan;
        int prevId = 0, bi = 0;
        foreach (ref readonly BattleEntity e in cur)
        {
            while (bi < bas.Length && bas[bi].Id < e.Id) bi++;
            bool hasBase = bi < bas.Length && bas[bi].Id == e.Id;
            BattleEntity b = hasBase ? bas[bi] : default;
            ushort mask = hasBase ? DiffMask(e, b) : MaskAll;
            if (mask == 0) continue;
            w.WriteBool(true);
            w.WriteVarUInt((uint)(e.Id - prevId));
            prevId = e.Id;
            w.WriteBits(mask, MaskBits);
            WriteFields(w, e, b, mask);
        }
        w.WriteBool(false);

        prevId = 0;
        int ci = 0;
        foreach (ref readonly BattleEntity b in bas)
        {
            while (ci < cur.Length && cur[ci].Id < b.Id) ci++;
            if (ci < cur.Length && cur[ci].Id == b.Id) continue;
            w.WriteBool(true);
            w.WriteVarUInt((uint)(b.Id - prevId));
            prevId = b.Id;
        }
        w.WriteBool(false);
    }

    /// <summary>Decodes into <paramref name="output"/>; the type byte must already be consumed. Throws if the baseline referenced is unavailable.</summary>
    public void Read(ref BitReader r, Func<int, BattleSnapshot?> findBaseline, BattleSnapshot output, out ClientFeedback feedback, out int baseTick)
    {
        int tick = (int)r.ReadVarUInt();
        baseTick = (int)r.ReadVarUInt();
        BattleSnapshot? baseline = null;
        if (baseTick != 0) baseline = findBaseline(baseTick) ?? throw new ProtocolException($"Missing baseline {baseTick}.");
        feedback = new ClientFeedback { LastProcessedInputTick = r.ReadVarInt(), InputSlack = r.ReadVarInt() };
        output.Tick = tick;
        output.Match = ReadMatch(ref r);

        output.HasOwn = r.ReadBool();
        if (output.HasOwn)
        {
            bool same = r.ReadBool();
            if (same)
            {
                if (baseline is not { HasOwn: true }) throw new ProtocolException("Own tank refers to a baseline without one.");
                output.Own = baseline.Own;
            }
            else output.Own = ReadOwn(ref r);
        }
        else output.Own = default;

        if (r.ReadBool())
        {
            if (baseline is null) throw new ProtocolException("Scoreboard refers to a missing baseline.");
            Array.Copy(baseline.Players, output.Players, output.Players.Length);
        }
        else ReadStats(ref r, output.Players);

        ReadOnlySpan<BattleEntity> bas = baseline is null ? ReadOnlySpan<BattleEntity>.Empty : baseline.EntitySpan;
        int changedCount = 0, removedCount = 0, prevId = 0, bi = 0;
        while (r.ReadBool())
        {
            if (changedCount >= _changed.Length) throw new ProtocolException("Too many entities.");
            int id = NextId(prevId, r.ReadVarUInt());
            prevId = id;
            ushort mask = (ushort)r.ReadBits(MaskBits);
            while (bi < bas.Length && bas[bi].Id < id) bi++;
            bool hasBase = bi < bas.Length && bas[bi].Id == id;
            if (!hasBase && mask != MaskAll) throw new ProtocolException($"Delta for unknown entity {id}.");
            BattleEntity e = hasBase ? bas[bi] : default;
            e.Id = id;
            ReadFields(ref r, ref e, mask);
            _changed[changedCount++] = e;
        }
        prevId = 0;
        while (r.ReadBool())
        {
            if (removedCount >= _removed.Length) throw new ProtocolException("Too many removals.");
            prevId = NextId(prevId, r.ReadVarUInt());
            _removed[removedCount++] = prevId;
        }
        Merge(bas, _changed.AsSpan(0, changedCount), _removed.AsSpan(0, removedCount), output);
    }

    private static int NextId(int prevId, uint gap)
    {
        long id = (long)prevId + gap;
        if (gap == 0 || id > int.MaxValue) throw new ProtocolException("Invalid entity id sequence.");
        return (int)id;
    }

    private static void Merge(ReadOnlySpan<BattleEntity> bas, ReadOnlySpan<BattleEntity> changed, ReadOnlySpan<int> removed, BattleSnapshot output)
    {
        int n = 0, bi = 0, ci = 0, ri = 0;
        while (bi < bas.Length || ci < changed.Length)
        {
            if (n >= BattleSnapshot.MaxEntities) throw new ProtocolException("Snapshot overflow.");
            bool takeChanged = ci < changed.Length && (bi >= bas.Length || changed[ci].Id <= bas[bi].Id);
            if (takeChanged)
            {
                if (bi < bas.Length && bas[bi].Id == changed[ci].Id) bi++;
                output.Entities[n++] = changed[ci++];
                continue;
            }
            int id = bas[bi].Id;
            while (ri < removed.Length && removed[ri] < id) ri++;
            if (ri < removed.Length && removed[ri] == id)
            {
                bi++;
                continue;
            }
            output.Entities[n++] = bas[bi++];
        }
        output.EntityCount = n;
    }

    private static ushort DiffMask(in BattleEntity e, in BattleEntity b)
    {
        ushort m = 0;
        if (e.Slot != b.Slot || e.Spec != b.Spec || e.Team != b.Team) m |= MaskMeta;
        if (e.X != b.X) m |= MaskX;
        if (e.Z != b.Z) m |= MaskZ;
        if (e.Yaw != b.Yaw) m |= MaskYaw;
        if (e.TurretYaw != b.TurretYaw) m |= MaskTurret;
        if (e.Pitch != b.Pitch) m |= MaskPitch;
        if (e.Speed != b.Speed) m |= MaskSpeed;
        if (e.Health != b.Health) m |= MaskHealth;
        if (e.Flags != b.Flags) m |= MaskFlags;
        return m;
    }

    private static void WriteFields(BitWriter w, in BattleEntity e, in BattleEntity b, ushort mask)
    {
        if ((mask & MaskMeta) != 0)
        {
            w.WriteByte(e.Slot);
            w.WriteBits(e.Spec, 2);
            w.WriteBits(e.Team, 4);
        }
        if ((mask & MaskX) != 0) w.WriteVarInt(e.X - b.X);
        if ((mask & MaskZ) != 0) w.WriteVarInt(e.Z - b.Z);
        if ((mask & MaskYaw) != 0) w.WriteBits(e.Yaw, BattleProtocol.YawBits);
        if ((mask & MaskTurret) != 0) w.WriteBits(e.TurretYaw, BattleProtocol.YawBits);
        if ((mask & MaskPitch) != 0) w.WriteBits(e.Pitch, BattleProtocol.PitchBits);
        if ((mask & MaskSpeed) != 0) w.WriteVarInt(e.Speed);
        if ((mask & MaskHealth) != 0) w.WriteVarUInt(e.Health);
        if ((mask & MaskFlags) != 0) w.WriteBits((uint)e.Flags, 2);
    }

    private static void ReadFields(ref BitReader r, ref BattleEntity e, ushort mask)
    {
        if ((mask & MaskMeta) != 0)
        {
            e.Slot = r.ReadByte();
            e.Spec = (byte)r.ReadBits(2);
            e.Team = (byte)r.ReadBits(4);
        }
        if ((mask & MaskX) != 0) e.X += r.ReadVarInt();
        if ((mask & MaskZ) != 0) e.Z += r.ReadVarInt();
        if ((mask & MaskYaw) != 0) e.Yaw = (ushort)r.ReadBits(BattleProtocol.YawBits);
        if ((mask & MaskTurret) != 0) e.TurretYaw = (ushort)r.ReadBits(BattleProtocol.YawBits);
        if ((mask & MaskPitch) != 0) e.Pitch = (ushort)r.ReadBits(BattleProtocol.PitchBits);
        if ((mask & MaskSpeed) != 0) e.Speed = checked((short)r.ReadVarInt());
        if ((mask & MaskHealth) != 0) e.Health = checked((ushort)r.ReadVarUInt());
        if ((mask & MaskFlags) != 0) e.Flags = (TankFlags)r.ReadBits(2);
    }

    private static void WriteMatch(BitWriter w, in MatchBlock m)
    {
        w.WriteBits((uint)m.Phase, 1);
        w.WriteVarUInt((uint)m.TicksLeft);
        w.WriteBool(m.Spotted);
        w.WriteByte(m.WinnerTeam);
        w.WriteByte(m.WinnerSlot);
        w.WriteVarUInt((uint)m.RespawnIn);
    }

    private static MatchBlock ReadMatch(ref BitReader r) => new()
    {
        Phase = (MatchPhase)r.ReadBits(1),
        TicksLeft = (int)r.ReadVarUInt(),
        Spotted = r.ReadBool(),
        WinnerTeam = r.ReadByte(),
        WinnerSlot = r.ReadByte(),
        RespawnIn = (int)r.ReadVarUInt(),
    };

    private static void WriteOwn(BitWriter w, in OwnTank o)
    {
        w.WriteVarUInt((uint)o.Id);
        BattleMessages.WriteFloat(w, o.X);
        BattleMessages.WriteFloat(w, o.Z);
        BattleMessages.WriteFloat(w, o.Yaw);
        BattleMessages.WriteFloat(w, o.TurretYaw);
        BattleMessages.WriteFloat(w, o.GunPitch);
        BattleMessages.WriteFloat(w, o.Speed);
        BattleMessages.WriteFloat(w, o.Reload);
        BattleMessages.WriteFloat(w, o.Bloom);
        w.WriteBits(o.Ammo == AmmoType.HE ? 1u : 0u, 1);
        w.WriteVarUInt((uint)o.ApLeft);
        w.WriteVarUInt((uint)o.HeLeft);
        w.WriteVarUInt((uint)o.ShotCount);
        w.WriteVarUInt((uint)o.Health);
        w.WriteVarInt(o.ProtectedUntil);
        w.WriteVarInt(o.LastFired);
        w.WriteBool(o.Destroyed);
    }

    private static OwnTank ReadOwn(ref BitReader r) => new()
    {
        Id = (int)r.ReadVarUInt(),
        X = BattleMessages.ReadFloat(ref r),
        Z = BattleMessages.ReadFloat(ref r),
        Yaw = BattleMessages.ReadFloat(ref r),
        TurretYaw = BattleMessages.ReadFloat(ref r),
        GunPitch = BattleMessages.ReadFloat(ref r),
        Speed = BattleMessages.ReadFloat(ref r),
        Reload = BattleMessages.ReadFloat(ref r),
        Bloom = BattleMessages.ReadFloat(ref r),
        Ammo = r.ReadBits(1) == 1 ? AmmoType.HE : AmmoType.AP,
        ApLeft = (int)r.ReadVarUInt(),
        HeLeft = (int)r.ReadVarUInt(),
        ShotCount = (int)r.ReadVarUInt(),
        Health = (int)r.ReadVarUInt(),
        ProtectedUntil = r.ReadVarInt(),
        LastFired = r.ReadVarInt(),
        Destroyed = r.ReadBool(),
    };

    private static void WriteStats(BitWriter w, PlayerStats[] players)
    {
        foreach (ref readonly PlayerStats p in players.AsSpan())
        {
            w.WriteBool(p.Active);
            if (!p.Active) continue;
            w.WriteBits(p.Team, 4);
            w.WriteBits(p.Spec, 2);
            w.WriteVarUInt((uint)p.TankId);
            w.WriteVarUInt((uint)p.Kills);
            w.WriteVarUInt((uint)p.Deaths);
            w.WriteBool(p.Alive);
            w.WriteBits(p.Rank, 4);
        }
    }

    private static void ReadStats(ref BitReader r, PlayerStats[] players)
    {
        for (int i = 0; i < players.Length; i++)
        {
            ref PlayerStats p = ref players[i];
            p = default;
            p.Active = r.ReadBool();
            if (!p.Active) continue;
            p.Team = (byte)r.ReadBits(4);
            p.Spec = (byte)r.ReadBits(2);
            p.TankId = (int)r.ReadVarUInt();
            p.Kills = (int)r.ReadVarUInt();
            p.Deaths = (int)r.ReadVarUInt();
            p.Alive = r.ReadBool();
            p.Rank = (byte)r.ReadBits(4);
        }
    }
}
```

修改 `src/Tank.Net/Tank.Net.csproj`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Tank.Net.csproj b/src/Tank.Net/Tank.Net.csproj
index b2d6870..45438fc 100644
--- a/src/Tank.Net/Tank.Net.csproj
+++ b/src/Tank.Net/Tank.Net.csproj
@@ -1,6 +1,7 @@
 <Project Sdk="Microsoft.NET.Sdk">
   <ItemGroup>
     <ProjectReference Include="..\Tank.Core\Tank.Core.csproj" />
+    <ProjectReference Include="..\Tank.Sim\Tank.Sim.csproj" />
     <PackageReference Include="LiteNetLib" />
   </ItemGroup>
   <ItemGroup>
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 622、Net 61。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Net tests/Tank.Net.Tests
git commit -m "feat(net): the battle protocol — inputs with a quantised aim, per-client delta snapshots with the own tank in full, event batches with prop states, the map on the wire, room messages"
```

---

### Task 6：对战服务器

设计 §2.4、§5.5、§5.6、§5.8。传输层：`LiteNetServerTransport` / `LiteNetClientTransport` 加连接密钥参数和对战房间的发现回复；`LanDiscovery.BattleRooms`；`ServerRunner` 改为接 `ITickServer` 和 tick 率（旧 `GameServer` 实现该接口，60 Hz；对战 30 Hz）。`Battle/BattleInputBuffer`（每座位的输入环，缺输入时沿用上一条）、`BattleSeat`（连接、会话令牌、队伍、车型、准备、输入缓冲、64 个快照的历史环、确认过的快照 tick）、`Visibility.Viewer`（设计 §5.5 的表：本队总是可见，敌队只在本队点亮时可见，残骸可见，大乱斗每人一队，自己被毁后看全场；§5.6 的事件表：看不见的车开炮事件抹掉开炮者，命中 / 伤害 / 击毁只发给自己造成的或看得见目标的，物件事件全发）。`BattleServer`（`ITickServer`）：hello 校验版本与密钥 → 入座 / 按令牌重连 → 房间状态广播；房主（第一个入座的，或持 `OwnerToken` 的）可设模式、机器人数、开局、回房间；开局：建 `Simulation` 和 `VisionTracker`、`MatchRules`、机器人补到较少的一队（大乱斗各自一队）、给每个座位发开局消息（地图 + 物件状态）；每 tick：取输入 → 机器人出命令 → `Step` → 规则 `Update` → 视野 `Update`（5 Hz）→ 收集物件变化 → 给每个在局的座位发事件批和快照；掉线后 30 秒内可重连，超时离席（无复活模式算淘汰）；结束后 8 秒回房间。

**Files:**
- Create: `src/Tank.Net/Battle/BattleInputBuffer.cs`、`BattleSeat.cs`、`Visibility.cs`、`BattleServer.cs`、`BattleServer.Room.cs`、`BattleServer.Match.cs`、`BattleServerOptions.cs`
- Modify: `src/Tank.Net/Server/GameServer.cs`、`Server/ServerRunner.cs`、`Transport/LanDiscovery.cs`、`Transport/LiteNetClientTransport.cs`、`Transport/LiteNetServerTransport.cs`
- Test: 新建 `tests/Tank.Net.Tests/Battle/BattleServerUnitTests.cs`、`tests/Tank.Net.Tests/Battle/TestWorld.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Net.Tests/Battle/BattleServerUnitTests.cs`：

```csharp
using System.Numerics;
using Tank.Net.Battle;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Tests.Battle;

public sealed class BattleServerUnitTests
{
    private static InputCmd Cmd(int tick, sbyte throttle = 1, bool fire = false) => new(tick, throttle, 0, 1.5f, -0.1f, fire, AmmoType.HE);

    [Fact]
    public void Input_buffer_dedupes_drops_late_and_repeats_without_firing_then_idles()
    {
        var buffer = new BattleInputBuffer();
        buffer.Add(Cmd(10, fire: true), currentTick: 8);
        buffer.Add(Cmd(10, fire: true), currentTick: 8);   // redundant copy
        buffer.Add(Cmd(9), currentTick: 9);                 // late: tick 9 is already simulated
        buffer.Add(Cmd(200), currentTick: 9);               // far ahead: bogus
        Assert.Equal(1, buffer.LateCount);
        Assert.Equal(10, buffer.NewestReceivedTick);
        Assert.Equal(1, buffer.SlackAt(9));

        Assert.Equal(Cmd(10, fire: true), buffer.Take(10));
        buffer.Add(Cmd(10, fire: true), currentTick: 10);   // a redundant copy of a consumed tick isn't late
        Assert.Equal(1, buffer.LateCount);
        for (int tick = 11; tick <= 10 + BattleInputBuffer.MaxRepeatedTicks; tick++)
        {
            InputCmd repeated = buffer.Take(tick);
            Assert.Equal(1, repeated.Throttle);
            Assert.False(repeated.Fire);
            Assert.Equal(1.5f, repeated.AimYaw);
            Assert.Equal(AmmoType.HE, repeated.Ammo);
        }
        InputCmd idle = buffer.Take(17);
        Assert.Equal(0, idle.Throttle);
        Assert.Equal(1.5f, idle.AimYaw);   // the gun stays where it was
        Assert.Equal(AmmoType.HE, idle.Ammo);
        Assert.Equal(7, buffer.MissingCount);
        buffer.Add(Cmd(5), currentTick: 17);   // older than what was consumed
        Assert.Equal(2, buffer.LateCount);
    }

    private static (Simulation Sim, VisionTracker Vision, int Me, int Mate, int Near, int Far) World()
    {
        var sim = new Simulation(TestWorld.FlatField());
        int me = sim.AddTank(TankCatalog.Medium, new Vector2(100, 190), 0f, 0);
        int mate = sim.AddTank(TankCatalog.Medium, new Vector2(60, 190), 0f, 0);
        int near = sim.AddTank(TankCatalog.Light, new Vector2(100, 140), MathF.PI / 2, 1);   // 50 m: seen
        int far = sim.AddTank(TankCatalog.Light, new Vector2(100, 10), MathF.PI / 2, 1);     // 180 m: beyond a still light tank's 168 m
        var vision = new VisionTracker(sim);
        Span<InputCmd> inputs = stackalloc InputCmd[4];
        for (int i = 0; i < 4; i++) inputs[i] = new InputCmd(1, 0, 0, sim.Tanks[i].Yaw, 0f, false);
        sim.Step(inputs);
        vision.Update();
        return (sim, vision, me, mate, near, far);
    }

    [Fact]
    public void A_viewer_sees_its_team_wrecks_and_what_its_team_spots()
    {
        var (sim, vision, me, mate, near, far) = World();
        var viewer = new Viewer(0, me, false, FreeForAll: false);
        Assert.True(viewer.SeesTank(me, sim, vision));
        Assert.True(viewer.SeesTank(mate, sim, vision));
        Assert.True(viewer.SeesTank(near, sim, vision));
        Assert.False(viewer.SeesTank(far, sim, vision));
        Assert.True(viewer.Sees(sim.Tanks[sim.SlotOf(far)] with { IsDestroyed = true }, vision), "wrecks are seen by all");
        Assert.False(viewer.SeesTank(999, sim, vision));
    }

    [Fact]
    public void A_dead_viewer_in_a_free_for_all_sees_everything_a_dead_team_viewer_does_not()
    {
        var (sim, vision, me, _, _, far) = World();
        Assert.False(new Viewer(0, me, OwnDestroyed: true, FreeForAll: false).SeesTank(far, sim, vision));
        Assert.True(new Viewer(0, me, OwnDestroyed: true, FreeForAll: true).SeesTank(far, sim, vision));
        Assert.True(new Viewer(0, 0, false, FreeForAll: true).SeesTank(far, sim, vision), "no tank yet: watching");
        Assert.False(new Viewer(0, me, false, FreeForAll: true).SeesTank(far, sim, vision), "alive: own eyes only");
    }

    [Fact]
    public void Events_are_filtered_by_what_the_viewer_sees()
    {
        var (sim, vision, me, _, near, far) = World();
        var viewer = new Viewer(0, me, false, false);
        var shotByFar = new GameEvent(GameEventType.ShellFired, 1, far, 50, Vector3.One, Vector3.UnitZ);
        Assert.True(viewer.Wants(shotByFar, sim, vision, out GameEvent sent));
        Assert.Equal(0, sent.TankId);   // a shot from the dark: no shooter
        var shotByNear = shotByFar with { TankId = near };
        Assert.True(viewer.Wants(shotByNear, sim, vision, out sent));
        Assert.Equal(near, sent.TankId);

        var hitOnFar = new GameEvent(GameEventType.TankHit, 1, near, 51, Vector3.One, Vector3.UnitZ, TargetTankId: far, Damage: 100);
        Assert.False(viewer.Wants(hitOnFar, sim, vision, out _));
        Assert.True(viewer.Wants(hitOnFar with { TankId = me }, sim, vision, out _), "my own hit on it");
        Assert.True(viewer.Wants(hitOnFar with { TargetTankId = near }, sim, vision, out _));
        Assert.True(viewer.Wants(new GameEvent(GameEventType.ShellExploded, 1, far, 50, Vector3.One, Vector3.Zero), sim, vision, out _));
        Assert.True(viewer.Wants(new GameEvent(GameEventType.PropDestroyed, 1, far, 50, Vector3.One, Vector3.Zero, PropId: 3), sim, vision, out _));
        Assert.False(viewer.Wants(new GameEvent(GameEventType.TankRevived, 1, far, 0, Vector3.One, Vector3.Zero, TargetTankId: far), sim, vision, out _));
        Assert.True(viewer.Wants(new GameEvent(GameEventType.TankRevived, 1, near, 0, Vector3.One, Vector3.Zero, TargetTankId: near), sim, vision, out _));
    }

    [Fact]
    public void Spotted_means_an_enemy_team_sees_me()
    {
        var (sim, vision, me, _, near, far) = World();
        Assert.True(new Viewer(0, me, false, false).IsSpotted(sim, vision), "the light tank 50 m away sees me");
        Assert.False(new Viewer(1, far, false, false).IsSpotted(sim, vision), "nobody sees the far one");
        Assert.False(new Viewer(0, me, OwnDestroyed: true, false).IsSpotted(sim, vision));
        _ = near;
    }

    [Fact]
    public void Tanks_quantise_to_the_wire_and_the_own_tank_is_exact()
    {
        var t = new TankState
        {
            Id = 9, Spec = TankCatalog.Heavy, Team = 1, Position = new Vector2(123.456f, 78.9f), Yaw = 1.1f, TurretYaw = -0.4f, GunPitch = 0.12f,
            Speed = -2.34f, Reload = 1.5f, Bloom = 0.3f, Ammo = AmmoType.HE, ApLeft = 10, HeLeft = 4, ShotCount = 6, Health = 1200, ProtectedUntil = 160, LastFired = 12,
        };
        BattleEntity e = BattleServer.ToEntity(t, 4, 2, tick: 100);
        Assert.Equal((12346, 7890), (e.X, e.Z));
        Assert.Equal(1.1f, BattleProtocol.DequantizeAngle(e.Yaw, BattleProtocol.YawBits), 0.002f);
        Assert.Equal(-0.4f, BattleProtocol.DequantizeAngle(e.TurretYaw, BattleProtocol.YawBits), 0.002f);
        Assert.Equal(0.12f, BattleProtocol.DequantizePitch(e.Pitch, BattleProtocol.PitchBits), 0.004f);
        Assert.Equal(-23, e.Speed);
        Assert.Equal(1200, e.Health);
        Assert.Equal(TankFlags.Protected, e.Flags);
        Assert.Equal(TankFlags.None, BattleServer.ToEntity(t, 4, 2, tick: 160).Flags);   // the window has closed
        Assert.Equal(TankFlags.Destroyed, BattleServer.ToEntity(t with { IsDestroyed = true }, 4, 2, tick: 200).Flags);
        Assert.Equal((4, 2, 1), (e.Slot, e.Spec, e.Team));
        OwnTank own = BattleServer.ToOwn(t);
        Assert.Equal((123.456f, 78.9f, 1.1f, -0.4f, 0.12f, -2.34f, 1.5f, 0.3f), (own.X, own.Z, own.Yaw, own.TurretYaw, own.GunPitch, own.Speed, own.Reload, own.Bloom));
        Assert.Equal((AmmoType.HE, 10, 4, 6, 1200, 160, 12, false), (own.Ammo, own.ApLeft, own.HeLeft, own.ShotCount, own.Health, own.ProtectedUntil, own.LastFired, own.Destroyed));
    }
}
```

新建 `tests/Tank.Net.Tests/Battle/TestWorld.cs`：

```csharp
using System.Numerics;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Terrain;
using Tank.Sim.Vehicles;

namespace Tank.Net.Tests.Battle;

internal static class TestWorld
{
    public const int Samples = 101;
    public const float Cell = 2f; // 200m × 200m
    public const float Size = (Samples - 1) * Cell;
    public const float Border = 4f;

    public static HeightField Flat(float height = 0f) =>
        new(Samples, Samples, Cell, Enumerable.Repeat(height, Samples * Samples).ToArray());

    /// <summary>Ground rising toward -Z (north) by <paramref name="grade"/> metres per metre.</summary>
    public static HeightField RampNorth(float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = (Samples - 1 - j) * Cell * grade;
        return new HeightField(Samples, Samples, Cell, heights);
    }

    /// <summary>Flat south of <paramref name="edgeZ"/>, rising toward -Z at <paramref name="grade"/> beyond it.</summary>
    public static HeightField CliffNorth(float edgeZ, float grade)
    {
        var heights = new float[Samples * Samples];
        for (int j = 0; j < Samples; j++)
        for (int i = 0; i < Samples; i++)
            heights[j * Samples + i] = MathF.Max(0f, (edgeZ - j * Cell) * grade);
        return new HeightField(Samples, Samples, Cell, heights);
    }

    public static Battlefield Field(HeightField terrain, params Prop[] props) => new(terrain, props, Border);

    public static Battlefield FlatField(params Prop[] props) => Field(Flat(), props);

    public static TankState Tank(float x, float z, float yaw = 0f) =>
        new() { Id = 1, Spec = TankCatalog.Medium, Position = new Vector2(x, z), Yaw = yaw };

    public static InputCmd Cmd(sbyte throttle = 0, sbyte steer = 0, float aimYaw = 0f, float aimPitch = 0f, bool fire = false, AmmoType ammo = AmmoType.AP) =>
        new(0, throttle, steer, aimYaw, aimPitch, fire, ammo);

    public static void Drive(ref TankState t, Battlefield field, InputCmd cmd, int ticks, params Obb2[] obstacles)
    {
        for (int i = 0; i < ticks; i++) TankMotion.Drive(ref t, cmd, field, obstacles);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Net.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`BattleServer`、`Viewer`、`BattleInputBuffer` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Net/Battle/BattleInputBuffer.cs`：

```csharp
using Tank.Sim;

namespace Tank.Net.Battle;

/// <summary>
/// One player's inputs indexed by tick (design §5.3). Redundant copies are deduplicated, late inputs dropped, and a
/// missing input repeats the previous one without firing (so a lost packet never makes a tank stutter to a halt);
/// after <see cref="MaxRepeatedTicks"/> missing in a row the tank is taken for idle and holds still.
/// </summary>
public sealed class BattleInputBuffer
{
    private const int Size = 128;
    public const int MaxRepeatedTicks = 6;
    private const int MaxTicksAhead = 64;

    private readonly InputCmd[] _ring = new InputCmd[Size];
    private readonly bool[] _has = new bool[Size];
    private readonly int[] _received = new int[Size];   // the newest tick ever stored in each slot, kept after Take
    private InputCmd _last;
    private int _lastConsumedTick = -1;
    private int _consecutiveMisses;

    public int NewestReceivedTick { get; private set; } = -1;
    public int MissingCount { get; private set; }
    public int LateCount { get; private set; }

    /// <param name="currentTick">The last tick the server has simulated; inputs far beyond it are rejected.</param>
    public void Add(in InputCmd cmd, int currentTick)
    {
        if (cmd.Tick <= _lastConsumedTick || cmd.Tick <= currentTick)
        {
            if (_received[cmd.Tick & (Size - 1)] != cmd.Tick) LateCount++;   // truly late; the redundant copies of consumed ticks aren't
            return;
        }
        if (cmd.Tick > currentTick + MaxTicksAhead) return;   // bogus or badly desynced clock
        int slot = cmd.Tick & (Size - 1);
        if (_has[slot] && _ring[slot].Tick == cmd.Tick) return;   // redundant copy
        _ring[slot] = cmd;
        _has[slot] = true;
        _received[slot] = cmd.Tick;
        if (cmd.Tick > NewestReceivedTick) NewestReceivedTick = cmd.Tick;
    }

    /// <summary>The input for <paramref name="tick"/>: what arrived, or the previous one repeated without firing, or idle.</summary>
    public InputCmd Take(int tick)
    {
        _lastConsumedTick = tick;
        int slot = tick & (Size - 1);
        if (_has[slot] && _ring[slot].Tick == tick)
        {
            _has[slot] = false;
            _last = _ring[slot];
            _consecutiveMisses = 0;
            return _last;
        }
        MissingCount++;
        if (++_consecutiveMisses > MaxRepeatedTicks) return Idle(tick, _last);
        return _last with { Tick = tick, Fire = false };
    }

    /// <summary>Holds still, the gun where it is (the aim stays where the last input put it).</summary>
    public static InputCmd Idle(int tick, in InputCmd last) => new(tick, 0, 0, last.AimYaw, last.AimPitch, false, last.Ammo);

    /// <summary>How many ticks ahead of <paramref name="tick"/> the newest input is (clock sync feedback).</summary>
    public int SlackAt(int tick) => NewestReceivedTick < 0 ? 0 : NewestReceivedTick - tick;

    public void Reset()
    {
        Array.Clear(_has);
        _last = default;
        _lastConsumedTick = -1;
        _consecutiveMisses = 0;
        NewestReceivedTick = -1;
    }
}
```

新建 `src/Tank.Net/Battle/BattleSeat.cs`：

```csharp
using Tank.Sim.Ai;

namespace Tank.Net.Battle;

/// <summary>A seat in the room: a player (surviving a disconnect for the reconnect grace period) or a bot.</summary>
internal sealed class BattleSeat
{
    public const int HistorySize = 64;

    public BattleSeat(byte slot, uint token, bool isBot)
    {
        Slot = slot;
        Token = token;
        IsBot = isBot;
        if (!isBot)
        {
            History = new BattleSnapshot[HistorySize];
            for (int i = 0; i < HistorySize; i++) History[i] = new BattleSnapshot();
        }
    }

    public byte Slot { get; }
    public uint Token { get; }
    public bool IsBot { get; }
    public int PeerId { get; set; } = -1;
    public string Name { get; set; } = "Player";
    public byte Team { get; set; }
    public byte TankIndex { get; set; } = 1;   // the medium tank
    public bool Ready { get; set; }
    public int AckedSnapshotTick { get; set; }
    public long DisconnectedAtFrame { get; set; } = -1;
    public long LastResyncFrame { get; set; } = long.MinValue / 2;
    public BattleInputBuffer Inputs { get; } = new();

    /// <summary>The snapshots sent to this player, by tick, for delta baselines (players only).</summary>
    public BattleSnapshot[]? History { get; }

    public SkirmishBot? Bot { get; set; }

    public bool Connected => PeerId >= 0;

    /// <summary>Whether this seat is in the match (has a tank).</summary>
    public bool InMatch { get; set; }

    public void ResetForMatch()
    {
        Inputs.Reset();
        AckedSnapshotTick = 0;
        if (History is not null) foreach (BattleSnapshot s in History) s.Tick = -1;
    }
}
```

新建 `src/Tank.Net/Battle/BattleServer.Match.cs`：

```csharp
using Tank.Net.Protocol;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

public sealed partial class BattleServer
{
    /// <summary>
    /// Starts a match in the room's mode with everyone seated, plus the bots asked for (named, given random tanks, put on
    /// the thinner team), if there are at least two tanks and, in a team mode, both teams have one.
    /// </summary>
    public bool TryStartMatch(out string? reason)
    {
        if (_sim is not null)
        {
            reason = "a match is running";
            return false;
        }
        uint seed = _options.Seed ?? (uint)Random.Shared.Next(1, int.MaxValue);
        var rng = new Tank.Sim.Geometry.XorShift(seed);
        int humans = _seats.Count(s => s is { IsBot: false });
        for (int i = 0; i < _bots && humans + i < BattleProtocol.MaxPlayers; i++)
        {
            byte slot = (byte)Array.FindIndex(_seats, s => s is null);
            var bot = new BattleSeat(slot, 0, isBot: true) { Name = $"机器人 {i + 1}", TankIndex = (byte)(rng.NextUInt() % (uint)TankCatalog.All.Count) };
            bot.Team = ThinnerTeam(slot);
            _seats[slot] = bot;
        }
        int seated = _seats.Count(s => s is not null);
        bool teamsManned = !ModeRules.HasTeams(_mode) || (_seats.Any(s => s is { Team: 0 }) && _seats.Any(s => s is { Team: 1 }));
        if (seated < ModeRules.MinPlayers || !teamsManned)
        {
            RemoveBots();
            reason = seated < ModeRules.MinPlayers ? "fewer than two tanks" : "a team is empty";
            return false;
        }

        _map = MapData.Of(SandboxMap.Create());
        _sim = new Simulation(_map.Build());
        _vision = new VisionTracker(_sim);
        _rules = new MatchRules(_mode, _sim, SandboxMap.Spawns);
        _matchNumber++;
        _endedAtFrame = -1;
        foreach (BattleSeat? seat in _seats)
        {
            if (seat is null) continue;
            PutIntoMatch(seat);
            if (seat.IsBot) seat.Bot = new SkirmishBot(_rules.Of(seat.Slot).TankId, SandboxMap.Waypoints, seed ^ (uint)(seat.Slot * 7919));
            else seat.ResetForMatch();
        }
        _log($"match started: {ModeRules.Id(_mode)}, {humans} players, {seated - humans} bots, seed {seed}");
        foreach (BattleSeat seat in _byPeer.Values) SendMatchStart(seat);
        _roomDirty = true;
        reason = null;
        return true;
    }

    private void PutIntoMatch(BattleSeat seat)
    {
        _rules!.Join(seat.Slot, TankCatalog.All[seat.TankIndex], seat.Team);
        seat.InMatch = true;
    }

    public void EndMatch()
    {
        if (_sim is null) return;
        _log("match over, back to the room");
        _sim = null;
        _vision = null;
        _rules = null;
        _map = null;
        _endedAtFrame = -1;
        RemoveBots();
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            BattleSeat? seat = _seats[slot];
            if (seat is null) continue;
            seat.InMatch = false;
            if (!seat.Connected) _seats[slot] = null;
            else seat.Ready = false;
        }
        _roomDirty = true;
    }

    private void RemoveBots()
    {
        for (int i = 0; i < _seats.Length; i++)
            if (_seats[i] is { IsBot: true }) _seats[i] = null;
    }

    private void TickMatch()
    {
        Simulation sim = _sim!;
        VisionTracker vision = _vision!;
        MatchRules rules = _rules!;
        int tick = sim.Tick + 1;

        // Inputs, one per tank in slot order: players from their buffers, bots from their brains.
        ReadOnlySpan<TankState> tanks = sim.Tanks;
        for (int i = 0; i < tanks.Length; i++)
        {
            BattleSeat? seat = SeatOfTank(tanks[i].Id);
            _tickInputs[i] = seat is null ? BattleInputBuffer.Idle(tick, default)
                : seat.IsBot ? seat.Bot!.Command(sim, vision, tick)
                : seat.Inputs.Take(tick);
        }
        sim.Step(_tickInputs.AsSpan(0, tanks.Length));
        vision.Update();
        rules.Update();

        CollectPropChanges(sim);
        foreach (BattleSeat seat in _byPeer.Values)
        {
            if (!seat.InMatch) continue;
            SendEvents(seat, sim, vision);
            SendSnapshot(seat, sim, vision, rules);
        }

        if (rules.Phase == MatchPhase.Ended)
        {
            if (_endedAtFrame < 0) _endedAtFrame = _frame;
            if (_frame - _endedAtFrame >= _options.ReturnToRoomSeconds * SimConstants.TickRate) EndMatch();
        }
        else if (!AnyPlayerConnected())
        {
            _log("nobody left in the match");
            EndMatch();
        }
    }

    private bool AnyPlayerConnected()
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { IsBot: false, Connected: true }) return true;
        return false;
    }

    private BattleSeat? SeatOfTank(int tankId)
    {
        foreach (BattleSeat? seat in _seats)
            if (seat is { InMatch: true } && _rules!.Of(seat.Slot).TankId == tankId) return seat;
        return null;
    }

    private Viewer ViewerOf(BattleSeat seat, Simulation sim)
    {
        ref readonly Standing standing = ref _rules!.Of(seat.Slot);
        int slot = sim.SlotOf(standing.TankId);
        bool destroyed = slot < 0 || sim.Tanks[slot].IsDestroyed;
        return new Viewer(standing.Team, standing.Active ? standing.TankId : 0, destroyed, _mode == BattleMode.FreeForAll);
    }

    /// <summary>The hit points, quantised, of every prop this tick's events touched, each once.</summary>
    private void CollectPropChanges(Simulation sim)
    {
        _propChanges.Clear();
        foreach (ref readonly GameEvent e in sim.Events)
        {
            if (e.Type is not (GameEventType.PropDamaged or GameEventType.PropDestroyed)) continue;
            bool known = false;
            foreach (PropState p in _propChanges) known |= p.PropId == e.PropId;
            if (known) continue;
            Prop prop = sim.Field.PropById(e.PropId);
            _propChanges.Add(new PropState(e.PropId, BattleProtocol.QuantizeDurability(sim.Field.Durability(e.PropId), prop.MaxDurability)));
        }
    }

    private void SendEvents(BattleSeat seat, Simulation sim, VisionTracker vision)
    {
        if (sim.Events.Length == 0) return;
        Viewer viewer = ViewerOf(seat, sim);
        _sentEvents.Clear();
        foreach (ref readonly GameEvent e in sim.Events)
            if (viewer.Wants(e, sim, vision, out GameEvent sent)) _sentEvents.Add(sent);
        if (_sentEvents.Count == 0 && _propChanges.Count == 0) return;
        _writer.Reset();
        BattleMessages.Write(_writer, new EventBatch(sim.Tick, _sentEvents, _propChanges));
        Send(seat, _writer.Data, Channel.Reliable);
    }

    private void SendSnapshot(BattleSeat seat, Simulation sim, VisionTracker vision, MatchRules rules)
    {
        int tick = sim.Tick;
        BattleSnapshot snapshot = seat.History![tick % BattleSeat.HistorySize];
        Build(snapshot, seat, sim, vision, rules);
        int ack = seat.AckedSnapshotTick;
        BattleSnapshot? baseline = null;
        if (ack > 0 && ack < tick && tick - ack < BattleSeat.HistorySize && seat.History[ack % BattleSeat.HistorySize].Tick == ack)
            baseline = seat.History[ack % BattleSeat.HistorySize];
        var feedback = new ClientFeedback { LastProcessedInputTick = tick, InputSlack = seat.Inputs.SlackAt(tick) };
        _writer.Reset();
        _codec.Write(_writer, snapshot, baseline, feedback);
        Channel channel = _writer.ByteLength > MaxUnreliableBytes ? Channel.Reliable : Channel.Unreliable;
        Send(seat, _writer.Data, channel);
    }

    /// <summary>What <paramref name="seat"/> may know of tick <paramref name="sim"/>.Tick (design §5.5).</summary>
    private void Build(BattleSnapshot s, BattleSeat seat, Simulation sim, VisionTracker vision, MatchRules rules)
    {
        Viewer viewer = ViewerOf(seat, sim);
        s.Tick = sim.Tick;
        int n = 0;
        s.HasOwn = false;
        foreach (ref readonly TankState t in sim.Tanks)
        {
            if (!viewer.Sees(t, vision)) continue;
            BattleSeat? owner = SeatOfTank(t.Id);
            s.Entities[n++] = ToEntity(t, owner?.Slot ?? MatchRules.NoSlot, owner?.TankIndex ?? 0, sim.Tick);
            if (t.Id == viewer.OwnTankId)
            {
                s.HasOwn = true;
                s.Own = ToOwn(t);
            }
        }
        s.EntityCount = n;
        Array.Sort(s.Entities, 0, n, ById);
        ref readonly Standing mine = ref rules.Of(seat.Slot);
        s.Match = new MatchBlock
        {
            Phase = rules.Phase,
            TicksLeft = rules.TicksLeft,
            Spotted = viewer.IsSpotted(sim, vision),
            WinnerTeam = rules.WinnerTeam,
            WinnerSlot = rules.WinnerSlot,
            RespawnIn = mine.RespawnAt >= 0 ? System.Math.Max(0, mine.RespawnAt - sim.Tick) : 0,
        };
        for (byte slot = 0; slot < s.Players.Length; slot++)
        {
            ref readonly Standing p = ref rules.Of(slot);
            s.Players[slot] = new PlayerStats
            {
                Active = p.Active, Team = p.Team, Spec = _seats[slot]?.TankIndex ?? 0, TankId = p.TankId,
                Kills = p.Kills, Deaths = p.Deaths, Alive = p.Alive, Rank = p.Rank,
            };
        }
    }

    private static readonly IComparer<BattleEntity> ById = Comparer<BattleEntity>.Create(static (a, b) => a.Id.CompareTo(b.Id));

    /// <param name="tick">The snapshot's tick: protection is a window, so the flag needs the time (<see cref="Simulation.IsProtected"/>).</param>
    public static BattleEntity ToEntity(in TankState t, byte slot, byte spec, int tick) => new()
    {
        Id = t.Id,
        Slot = slot,
        Spec = spec,
        Team = t.Team,
        X = BattleProtocol.ToCm(t.Position.X),
        Z = BattleProtocol.ToCm(t.Position.Y),
        Yaw = (ushort)BattleProtocol.QuantizeAngle(t.Yaw, BattleProtocol.YawBits),
        TurretYaw = (ushort)BattleProtocol.QuantizeAngle(t.TurretYaw, BattleProtocol.YawBits),
        Pitch = (ushort)BattleProtocol.QuantizePitch(t.GunPitch, BattleProtocol.PitchBits),
        Speed = (short)MathF.Round(t.Speed / BattleProtocol.SpeedUnit),
        Health = (ushort)System.Math.Clamp(t.Health, 0, ushort.MaxValue),
        Flags = (t.IsDestroyed ? TankFlags.Destroyed : TankFlags.None) | (tick < t.ProtectedUntil ? TankFlags.Protected : TankFlags.None),
    };

    public static OwnTank ToOwn(in TankState t) => new()
    {
        Id = t.Id, X = t.Position.X, Z = t.Position.Y, Yaw = t.Yaw, TurretYaw = t.TurretYaw, GunPitch = t.GunPitch, Speed = t.Speed,
        Reload = t.Reload, Bloom = t.Bloom, Ammo = t.Ammo, ApLeft = t.ApLeft, HeLeft = t.HeLeft, ShotCount = t.ShotCount, Health = t.Health,
        ProtectedUntil = t.ProtectedUntil, LastFired = t.LastFired, Destroyed = t.IsDestroyed,
    };

    private void SendMatchStart(BattleSeat seat)
    {
        Simulation sim = _sim!;
        var props = new List<PropState>();
        foreach (Prop p in sim.Field.Props)
        {
            if (!p.IsDestructible) continue;
            int left = sim.Field.Durability(p.Id);
            if (left != p.MaxDurability) props.Add(new PropState(p.Id, BattleProtocol.QuantizeDurability(left, p.MaxDurability)));
        }
        _mapWriter.Reset();
        BattleMessages.Write(_mapWriter, new MatchStartMessage(_mode, _matchNumber, sim.Tick, _rules!.EndTick, _map!, props, TankOf(seat.Slot)));
        Send(seat, _mapWriter.Data, Channel.Reliable);
    }
}
```

新建 `src/Tank.Net/Battle/BattleServer.Room.cs`：

```csharp
using Tank.Net.Protocol;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

public sealed partial class BattleServer
{
    private void HandleHello(int peerId, HelloMessage hello)
    {
        if (_byPeer.ContainsKey(peerId) || _pendingKicks.Exists(k => k.PeerId == peerId)) return;
        _awaitingHello.Remove(peerId);
        if (hello.ProtocolVersion != BattleProtocol.Version)
        {
            Reject(peerId, $"版本不一致：房主 v{BattleProtocol.Version}，你的客户端 v{hello.ProtocolVersion}。");
            return;
        }
        bool isOwner = _options.OwnerToken is { } owner && owner != 0 && hello.SessionToken == owner;
        BattleSeat? seat = FindReconnect(hello.SessionToken) ?? AllocateSeat(isOwner ? hello.SessionToken : null);
        if (seat is null)
        {
            Reject(peerId, "房间已满。");
            return;
        }
        bool reconnecting = seat.DisconnectedAtFrame >= 0;
        seat.PeerId = peerId;
        seat.DisconnectedAtFrame = -1;
        seat.Name = SanitizeName(hello.Name, seat.Slot);
        _byPeer[peerId] = seat;
        if (_hostSlot == MatchRules.NoSlot || isOwner) _hostSlot = seat.Slot;
        _log($"slot {seat.Slot} {(reconnecting ? "reconnected" : "joined")} as '{seat.Name}'");

        _writer.Reset();
        BattleMessages.Write(_writer, new WelcomeMessage(seat.Slot, seat.Token));
        _transport.Send(peerId, _writer.Data, Channel.Reliable);
        _roomDirty = true;

        if (_sim is null || _rules is null) return;
        if (!seat.InMatch && _rules.AcceptsJoins) PutIntoMatch(seat);
        if (seat.InMatch)
        {
            seat.ResetForMatch();
            SendMatchStart(seat);
        }
    }

    private BattleSeat? FindReconnect(uint token)
    {
        if (token == 0) return null;
        return _seats.FirstOrDefault(s => s is { Connected: false, IsBot: false } && s.Token == token);
    }

    private BattleSeat? AllocateSeat(uint? token = null)
    {
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            if (_seats[slot] is not null) continue;
            var seat = new BattleSeat(slot, token ?? NewToken(), isBot: false) { Team = ThinnerTeam(slot) };
            _seats[slot] = seat;
            return seat;
        }
        return null;
    }

    /// <summary>Reconnect tokens let a client reclaim its seat, so they must not be guessable.</summary>
    private static uint NewToken()
    {
        uint token;
        do token = (uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(1, int.MaxValue) ^ ((uint)System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 2) << 31);
        while (token == 0);
        return token;
    }

    private void Reject(int peerId, string reason)
    {
        _log($"rejecting peer {peerId}: {reason}");
        _writer.Reset();
        BattleMessages.Write(_writer, new RejectMessage(reason));
        _transport.Send(peerId, _writer.Data, Channel.Reliable);
        _pendingKicks.Add((peerId, _frame + RejectDisconnectDelayFrames));
    }

    private void HandleRoomCommand(BattleSeat seat, RoomCommandMessage cmd)
    {
        bool isHost = seat.Slot == _hostSlot;
        switch (cmd.Kind)
        {
            case RoomCommandKind.SetTeam when !seat.InMatch && ModeRules.HasTeams(_mode):   // in the room, or waiting out a match
                if (byte.TryParse(cmd.Arg, out byte team) && team <= 1) seat.Team = team;
                break;
            case RoomCommandKind.SetTank when !seat.InMatch:
                int index = IndexOfTank(cmd.Arg);
                if (index >= 0) seat.TankIndex = (byte)index;
                break;
            case RoomCommandKind.SetReady:
                seat.Ready = cmd.Arg == "1";
                break;
            case RoomCommandKind.SetMode when isHost && _sim is null:
                if (ModeRules.TryParse(cmd.Arg, out BattleMode mode)) _mode = mode;
                break;
            case RoomCommandKind.SetBots when isHost && _sim is null:
                if (int.TryParse(cmd.Arg, out int bots)) _bots = System.Math.Clamp(bots, 0, BattleProtocol.MaxPlayers - 1);
                break;
            case RoomCommandKind.StartMatch when isHost && _sim is null:
                if (!TryStartMatch(out string? why)) _log($"can't start: {why}");
                break;
            case RoomCommandKind.ReturnToRoom when isHost && _sim is not null:
                EndMatch();
                break;
            default:
                return;
        }
        _roomDirty = true;
    }

    private static int IndexOfTank(string id)
    {
        for (int i = 0; i < TankCatalog.All.Count; i++)
            if (TankCatalog.All[i].Id == id) return i;
        return -1;
    }

    /// <summary>The team with fewer seats (other than <paramref name="except"/>), team 0 on a tie.</summary>
    private byte ThinnerTeam(byte except)
    {
        int a = 0, b = 0;
        foreach (BattleSeat? s in _seats)
        {
            if (s is null || s.Slot == except) continue;
            if (s.Team == 0) a++;
            else b++;
        }
        return (byte)(b < a ? 1 : 0);
    }

    private void BroadcastRoom()
    {
        _roomDirty = false;
        var players = new List<RoomPlayer>();
        foreach (BattleSeat? s in _seats)
            if (s is not null) players.Add(new RoomPlayer(s.Slot, s.Name, ModeRules.HasTeams(_mode) ? s.Team : s.Slot, s.TankIndex, s.Ready, s.Connected || s.IsBot, s.IsBot));
        _writer.Reset();
        BattleMessages.Write(_writer, new RoomStateMessage(_mode, _hostSlot, (byte)_bots, MatchRunning, players));
        SendToAll(_writer.Data, Channel.Reliable);
    }

    private void ElectHost()
    {
        _hostSlot = MatchRules.NoSlot;
        foreach (BattleSeat? s in _seats)
        {
            if (s is not { Connected: true }) continue;
            _hostSlot = s.Slot;
            return;
        }
    }

    private static string SanitizeName(string name, byte slot)
    {
        var clean = new string((name ?? string.Empty).Where(ch => !char.IsControl(ch)).ToArray()).Trim();
        if (clean.Length > BattleProtocol.MaxNameBytes) clean = clean[..BattleProtocol.MaxNameBytes];
        return clean.Length == 0 ? $"Player {slot + 1}" : clean;
    }
}
```

新建 `src/Tank.Net/Battle/BattleServer.cs`：

```csharp
using Tank.Net.Protocol;
using Tank.Net.Server;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Ai;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

/// <summary>
/// The authoritative battle server (stage 4 design §5.8): a room of up to ten seats and, while a match runs, a
/// <see cref="Simulation"/> with its <see cref="VisionTracker"/>, <see cref="MatchRules"/> and bots. Transport- and
/// clock-agnostic: call <see cref="Tick"/> at exactly <see cref="SimConstants.TickRate"/> (see <see cref="ServerRunner"/>)
/// or from a test with a virtual clock. Every client gets its own snapshots and events, holding only what it may see.
/// </summary>
public sealed partial class BattleServer : ITransportHandler, ITickServer
{
    private const int MaxUnreliableBytes = 1150;
    private const int HelloTimeoutFrames = 3 * SimConstants.TickRate;
    private const int ResyncCooldownFrames = SimConstants.TickRate;
    private const int RejectDisconnectDelayFrames = 15;

    private readonly IServerTransport _transport;
    private readonly BattleServerOptions _options;
    private readonly Action<string> _log;
    private readonly BattleSeat?[] _seats = new BattleSeat?[BattleProtocol.MaxPlayers];
    private readonly Dictionary<int, BattleSeat> _byPeer = new();
    private readonly BitWriter _writer = new(4096);
    private readonly BitWriter _mapWriter = new(128 * 1024);
    private readonly BattleSnapshotCodec _codec = new();
    private readonly InputCmd[] _packetInputs = new InputCmd[BattleProtocol.MaxRedundantInputs];
    private readonly InputCmd[] _tickInputs = new InputCmd[Simulation.MaxTanks];
    private readonly List<GameEvent> _sentEvents = new(64);
    private readonly List<PropState> _propChanges = new(16);
    private readonly List<(int PeerId, long AtFrame)> _pendingKicks = new();
    private readonly Dictionary<int, long> _awaitingHello = new();

    private BattleMode _mode;
    private int _bots;
    private byte _hostSlot = MatchRules.NoSlot;
    private long _frame;
    private int _matchNumber;
    private bool _roomDirty;

    // the match
    private MapData? _map;
    private Simulation? _sim;
    private VisionTracker? _vision;
    private MatchRules? _rules;
    private long _endedAtFrame = -1;

    public BattleServer(IServerTransport transport, BattleServerOptions? options = null, Action<string>? log = null)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _options = options ?? new BattleServerOptions();
        _log = log ?? (_ => { });
        _mode = _options.Mode;
        _bots = System.Math.Clamp(_options.Bots, 0, BattleProtocol.MaxPlayers - 1);
    }

    public BattleMode Mode => _mode;
    public int Bots => _bots;
    public bool MatchRunning => _sim is not null;
    public Simulation? Simulation => _sim;
    public VisionTracker? Vision => _vision;
    public MatchRules? Rules => _rules;
    public int ConnectedCount => _seats.Count(s => s is { Connected: true });

    /// <summary>The tank of the player in <paramref name="slot"/>, or 0.</summary>
    public int TankOf(byte slot) => _rules is not null && _rules.Of(slot).Active ? _rules.Of(slot).TankId : 0;

    public void Tick()
    {
        _frame++;
        _transport.Poll(this);
        ProcessPendingKicks();
        ExpireSilentPeers();
        ExpireDisconnected();
        if (_sim is not null) TickMatch();
        if (_roomDirty) BroadcastRoom();
    }

    /// <summary>Writes the room's answer to a LAN discovery broadcast.</summary>
    public bool WriteRoomInfo(BitWriter w)
    {
        BattleMessages.Write(w, new RoomInfoMessage(BattleProtocol.Version, _options.RoomName, _mode, ConnectedCount, BattleProtocol.MaxPlayers, MatchRunning, _options.Port));
        return true;
    }

    // ---- transport callbacks ----

    public void OnPeerConnected(int peerId)
    {
        _awaitingHello[peerId] = _frame;
        _log($"peer {peerId} connected, waiting for hello");
    }

    public void OnPeerDisconnected(int peerId, string reason)
    {
        _awaitingHello.Remove(peerId);
        _pendingKicks.RemoveAll(k => k.PeerId == peerId);
        if (!_byPeer.Remove(peerId, out BattleSeat? seat)) return;
        _log($"slot {seat.Slot} ({seat.Name}) disconnected: {reason}");
        seat.PeerId = -1;
        seat.DisconnectedAtFrame = _frame;
        if (_sim is null) FreeSeat(seat.Slot);
        if (_hostSlot == seat.Slot) ElectHost();
        _roomDirty = true;
    }

    public void OnData(int peerId, ReadOnlySpan<byte> data)
    {
        try
        {
            var r = new BitReader(data);
            var type = (BattleMessage)r.ReadByte();
            if (type == BattleMessage.Hello)
            {
                HandleHello(peerId, BattleMessages.ReadHello(ref r));
                return;
            }
            if (!_byPeer.TryGetValue(peerId, out BattleSeat? seat)) return;   // must say hello first
            switch (type)
            {
                case BattleMessage.Input: HandleInput(seat, ref r); break;
                case BattleMessage.RoomCommand: HandleRoomCommand(seat, BattleMessages.ReadRoomCommand(ref r)); break;
                case BattleMessage.RequestMatchState: HandleResync(seat); break;
            }
        }
        catch (ProtocolException ex)
        {
            _log($"dropped malformed packet from peer {peerId}: {ex.Message}");
        }
        catch (Exception ex)
        {
            // One bad packet must never stall the tick for everyone else.
            _log($"error handling packet from peer {peerId}: {ex}");
        }
    }

    private void HandleInput(BattleSeat seat, ref BitReader r)
    {
        int count = BattleInputPacket.Read(ref r, out int ack, _packetInputs);
        if (_sim is null) return;
        if (ack <= _sim.Tick && ack > seat.AckedSnapshotTick) seat.AckedSnapshotTick = ack;
        for (int i = 0; i < count; i++) seat.Inputs.Add(_packetInputs[i], _sim.Tick);
    }

    private void HandleResync(BattleSeat seat)
    {
        if (_sim is null || _frame - seat.LastResyncFrame < ResyncCooldownFrames) return;
        seat.LastResyncFrame = _frame;
        SendMatchStart(seat);
    }

    private void Send(BattleSeat seat, ReadOnlySpan<byte> data, Channel channel)
    {
        if (seat.Connected) _transport.Send(seat.PeerId, data, channel);
    }

    private void SendToAll(ReadOnlySpan<byte> data, Channel channel)
    {
        foreach (BattleSeat seat in _byPeer.Values) _transport.Send(seat.PeerId, data, channel);
    }

    private void ExpireDisconnected()
    {
        double graceFrames = _options.ReconnectGraceSeconds * SimConstants.TickRate;
        for (byte slot = 0; slot < _seats.Length; slot++)
        {
            BattleSeat? seat = _seats[slot];
            if (seat is null || seat.IsBot || seat.Connected || _frame - seat.DisconnectedAtFrame < graceFrames) continue;
            _log($"slot {slot} reconnect window expired");
            FreeSeat(slot);
            _roomDirty = true;
        }
    }

    /// <summary>Disconnects rejected peers a little later, so the reject reason actually reaches them.</summary>
    private void ProcessPendingKicks()
    {
        for (int i = _pendingKicks.Count - 1; i >= 0; i--)
        {
            if (_frame < _pendingKicks[i].AtFrame) continue;
            _transport.Disconnect(_pendingKicks[i].PeerId);
            _pendingKicks.RemoveAt(i);
        }
    }

    /// <summary>Peers that connect but never introduce themselves must not hold a connection forever.</summary>
    private void ExpireSilentPeers()
    {
        foreach (var (peerId, since) in _awaitingHello)
        {
            if (_frame - since < HelloTimeoutFrames || _pendingKicks.Exists(k => k.PeerId == peerId)) continue;
            _log($"peer {peerId} never said hello; disconnecting");
            _pendingKicks.Add((peerId, _frame));
        }
    }

    /// <summary>Empties a seat; a player in the match leaves it (a bot's seat is only freed when the match ends).</summary>
    private void FreeSeat(byte slot)
    {
        BattleSeat? seat = _seats[slot];
        if (seat is null) return;
        if (_rules is not null && seat.InMatch)
        {
            _rules.Leave(slot);
            seat.InMatch = false;
        }
        _seats[slot] = null;
    }
}
```

新建 `src/Tank.Net/Battle/BattleServerOptions.cs`：

```csharp
using Tank.Net.Protocol;
using Tank.Sim.Modes;

namespace Tank.Net.Battle;

public sealed record BattleServerOptions
{
    public string RoomName { get; init; } = "坦克 3D";
    public int Port { get; init; } = ProtocolInfo.DefaultPort;
    public BattleMode Mode { get; init; } = BattleMode.TeamBattle;

    /// <summary>Bots added when a match starts, filling the thinner team first.</summary>
    public int Bots { get; init; }

    public double ReturnToRoomSeconds { get; init; } = 8;
    public double ReconnectGraceSeconds { get; init; } = 30;

    /// <summary>
    /// Session token of the player running a listen server. Whoever says hello with it is host, regardless of who
    /// connected first.
    /// </summary>
    public uint? OwnerToken { get; init; }

    /// <summary>Fixed seed for reproducible matches (tests, bots' choices); null picks a random one per match.</summary>
    public uint? Seed { get; init; }
}
```

新建 `src/Tank.Net/Battle/Visibility.cs`：

```csharp
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;
using Tank.Sim.Vision;

namespace Tank.Net.Battle;

/// <summary>Which tanks one client is told about (design §5.5), decided on the server only.</summary>
public readonly record struct Viewer(byte Team, int OwnTankId, bool OwnDestroyed, bool FreeForAll)
{
    /// <summary>A dead player in a free-for-all watches everything; everyone else sees through their team's eyes.</summary>
    public bool SeesAll => FreeForAll && (OwnTankId == 0 || OwnDestroyed);

    public bool Sees(in TankState tank, VisionTracker vision)
    {
        if (SeesAll || tank.Id == OwnTankId || tank.Team == Team || tank.IsDestroyed) return true;
        return vision.IsVisible(tank.Id, Team);
    }

    public bool SeesTank(int tankId, Simulation sim, VisionTracker vision)
    {
        int slot = sim.SlotOf(tankId);
        return slot >= 0 && Sees(sim.Tanks[slot], vision);
    }

    /// <summary>
    /// Whether this viewer should get <paramref name="e"/>, and how (design §5.6): shots and bursts always (a shot by a
    /// tank out of sight with the shooter's id blanked), hits and kills when the viewer fired or sees the target,
    /// revivals when it sees the tank, prop changes always.
    /// </summary>
    public bool Wants(in GameEvent e, Simulation sim, VisionTracker vision, out GameEvent sent)
    {
        // Whoever fired is named only when the viewer may know: own shots, or a tank the viewer sees (design §5.6).
        sent = e.TankId == 0 || e.TankId == OwnTankId || SeesTank(e.TankId, sim, vision) ? e : e with { TankId = 0 };
        switch (e.Type)
        {
            case GameEventType.ShellFired:
            case GameEventType.ShellExploded:
            case GameEventType.PropDamaged:
            case GameEventType.PropDestroyed:
                return true;
            case GameEventType.TankHit:
            case GameEventType.SplashDamage:
            case GameEventType.TankDestroyed:
                return e.TankId == OwnTankId || SeesTank(e.TargetTankId, sim, vision);
            case GameEventType.TankRevived:
                return SeesTank(e.TargetTankId, sim, vision);
            default:
                return false;
        }
    }

    /// <summary>Whether any enemy team sees the viewer's own tank (the "spotted" lamp).</summary>
    public bool IsSpotted(Simulation sim, VisionTracker vision)
    {
        if (OwnTankId == 0 || OwnDestroyed) return false;
        foreach (ref readonly TankState t in sim.Tanks)
            if (t.Team != Team && !t.IsDestroyed && vision.IsVisible(OwnTankId, t.Team)) return true;
        return false;
    }
}
```

修改 `src/Tank.Net/Server/GameServer.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Server/GameServer.cs b/src/Tank.Net/Server/GameServer.cs
index 581be8c..2d017e2 100644
--- a/src/Tank.Net/Server/GameServer.cs
+++ b/src/Tank.Net/Server/GameServer.cs
@@ -9,7 +9,7 @@ namespace Tank.Net.Server;
 /// Authoritative game server. Transport-agnostic and clock-agnostic: call <see cref="Tick"/> at exactly
 /// 60Hz (see <see cref="ServerRunner"/>) or from a test with a virtual clock.
 /// </summary>
-public sealed partial class GameServer : ITransportHandler, IRoomInfoProvider
+public sealed partial class GameServer : ITransportHandler, IRoomInfoProvider, ITickServer
 {
     private const int HistorySize = 64;
     private const int MaxUnreliableBytes = 1150;
PATCH
```

修改 `src/Tank.Net/Server/ServerRunner.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Server/ServerRunner.cs b/src/Tank.Net/Server/ServerRunner.cs
index 1b4a790..9df2f40 100644
--- a/src/Tank.Net/Server/ServerRunner.cs
+++ b/src/Tank.Net/Server/ServerRunner.cs
@@ -4,24 +4,31 @@ using Tank.Core;
 
 namespace Tank.Net.Server;
 
+/// <summary>A server stepped by a <see cref="ServerRunner"/>.</summary>
+public interface ITickServer
+{
+    void Tick();
+}
+
 /// <summary>
-/// Runs <see cref="GameServer.Tick"/> at a precise 60Hz on a dedicated thread, using a sleep + spin hybrid
-/// wait. If the thread falls behind it catches up (bounded) instead of skipping ticks.
+/// Runs <see cref="ITickServer.Tick"/> at a precise rate (the old game's 60Hz, a battle's 30Hz) on a dedicated thread,
+/// using a sleep + spin hybrid wait. If the thread falls behind it catches up (bounded) instead of skipping ticks.
 /// </summary>
 public sealed class ServerRunner : IDisposable
 {
     private const int MaxCatchUpTicks = 5;
-    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1.0 / Units.TickRate);
+    private readonly TimeSpan TickInterval;
 
-    private readonly GameServer _server;
+    private readonly ITickServer _server;
     private readonly Action<string> _log;
     private readonly Thread _thread;
     private volatile bool _running;
 
-    public ServerRunner(GameServer server, Action<string>? log = null)
+    public ServerRunner(ITickServer server, Action<string>? log = null, int tickRate = Units.TickRate)
     {
         _server = server;
         _log = log ?? (_ => { });
+        TickInterval = TimeSpan.FromSeconds(1.0 / tickRate);
         _thread = new Thread(Loop) { IsBackground = true, Name = "Tank server", Priority = ThreadPriority.AboveNormal };
     }
 
PATCH
```

修改 `src/Tank.Net/Transport/LanDiscovery.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Transport/LanDiscovery.cs b/src/Tank.Net/Transport/LanDiscovery.cs
index 7b38366..9a19f7a 100644
--- a/src/Tank.Net/Transport/LanDiscovery.cs
+++ b/src/Tank.Net/Transport/LanDiscovery.cs
@@ -8,8 +8,12 @@ namespace Tank.Net.Transport;
 
 public sealed record DiscoveredRoom(IPEndPoint EndPoint, RoomInfoMessage Info, int PingMs, double LastSeenSeconds);
 
+/// <summary>A battle room (stage 4) found on the local network.</summary>
+public sealed record DiscoveredBattleRoom(IPEndPoint EndPoint, Battle.RoomInfoMessage Info, int PingMs, double LastSeenSeconds);
+
 /// <summary>
-/// Finds servers on the local network by UDP broadcast (docs/03-netcode.md §8.1).
+/// Finds servers on the local network by UDP broadcast (docs/03-netcode.md §8.1): the old game's rooms in
+/// <see cref="Rooms"/>, battle rooms (stage 4) in <see cref="BattleRooms"/>.
 /// Call <see cref="Poll"/> regularly; rooms not heard from for a few seconds drop off the list.
 /// </summary>
 public sealed class LanDiscovery : IDisposable
@@ -22,6 +26,7 @@ public sealed class LanDiscovery : IDisposable
     private readonly int _port;
     private readonly Stopwatch _clock = Stopwatch.StartNew();
     private readonly Dictionary<string, DiscoveredRoom> _rooms = new();
+    private readonly Dictionary<string, DiscoveredBattleRoom> _battleRooms = new();
     private readonly NetDataWriter _packet = new();
     private double _lastBroadcast = double.NegativeInfinity;
 
@@ -39,6 +44,8 @@ public sealed class LanDiscovery : IDisposable
 
     public IReadOnlyCollection<DiscoveredRoom> Rooms => _rooms.Values;
 
+    public IReadOnlyCollection<DiscoveredBattleRoom> BattleRooms => _battleRooms.Values;
+
     public void Poll()
     {
         double now = _clock.Elapsed.TotalSeconds;
@@ -50,6 +57,8 @@ public sealed class LanDiscovery : IDisposable
         _manager.PollEvents();
         foreach (var key in _rooms.Where(kv => now - kv.Value.LastSeenSeconds > RoomTimeoutSeconds).Select(kv => kv.Key).ToList())
             _rooms.Remove(key);
+        foreach (var key in _battleRooms.Where(kv => now - kv.Value.LastSeenSeconds > RoomTimeoutSeconds).Select(kv => kv.Key).ToList())
+            _battleRooms.Remove(key);
     }
 
     public void Dispose() => _manager.Stop();
@@ -59,12 +68,21 @@ public sealed class LanDiscovery : IDisposable
         try
         {
             var r = new BitReader(reader.GetRemainingBytesSpan());
-            if ((MessageType)r.ReadByte() != MessageType.RoomInfo) return;
-            var info = Messages.ReadRoomInfo(ref r);
+            byte kind = r.ReadByte();
             double now = _clock.Elapsed.TotalSeconds;
             int ping = (int)System.Math.Max(0, (now - _lastBroadcast) * 1000);
-            var endpoint = new IPEndPoint(remote.Address, info.Port);
-            _rooms[endpoint.ToString()] = new DiscoveredRoom(endpoint, info, ping, now);
+            if (kind == (byte)MessageType.RoomInfo)
+            {
+                var info = Messages.ReadRoomInfo(ref r);
+                var endpoint = new IPEndPoint(remote.Address, info.Port);
+                _rooms[endpoint.ToString()] = new DiscoveredRoom(endpoint, info, ping, now);
+            }
+            else if (kind == (byte)Battle.BattleMessage.RoomInfo)
+            {
+                var info = Battle.BattleMessages.ReadRoomInfo(ref r);
+                var endpoint = new IPEndPoint(remote.Address, info.Port);
+                _battleRooms[endpoint.ToString()] = new DiscoveredBattleRoom(endpoint, info, ping, now);
+            }
         }
         catch (ProtocolException)
         {
PATCH
```

修改 `src/Tank.Net/Transport/LiteNetClientTransport.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Transport/LiteNetClientTransport.cs b/src/Tank.Net/Transport/LiteNetClientTransport.cs
index fcc4452..1b5b755 100644
--- a/src/Tank.Net/Transport/LiteNetClientTransport.cs
+++ b/src/Tank.Net/Transport/LiteNetClientTransport.cs
@@ -10,7 +10,7 @@ public sealed class LiteNetClientTransport : IClientTransport
     private NetPeer? _server;
     private ITransportHandler? _handler;
 
-    public LiteNetClientTransport(string host, int port)
+    public LiteNetClientTransport(string host, int port, string connectKey = ProtocolInfo.ConnectKey)
     {
         _manager = new NetManager(_listener)
         {
@@ -33,7 +33,7 @@ public sealed class LiteNetClientTransport : IClientTransport
         _listener.NetworkReceiveEvent += (_, reader, _, _) => _handler?.OnData(0, reader.GetRemainingBytesSpan());
 
         if (!_manager.Start()) throw new InvalidOperationException("无法打开 UDP 套接字。");
-        _manager.Connect(host, port, ProtocolInfo.ConnectKey);
+        _manager.Connect(host, port, connectKey);
     }
 
     public NetManager Manager => _manager;
PATCH
```

修改 `src/Tank.Net/Transport/LiteNetServerTransport.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Transport/LiteNetServerTransport.cs b/src/Tank.Net/Transport/LiteNetServerTransport.cs
index 88b1421..cf1db93 100644
--- a/src/Tank.Net/Transport/LiteNetServerTransport.cs
+++ b/src/Tank.Net/Transport/LiteNetServerTransport.cs
@@ -16,10 +16,18 @@ public sealed class LiteNetServerTransport : IServerTransport
     private readonly NetDataWriter _reply = new();
     private ITransportHandler? _handler;
 
-    public LiteNetServerTransport(int port, IRoomInfoProvider? roomInfo = null, int maxConnections = 8)
+    private readonly string _connectKey;
+    private readonly Func<BitWriter, bool>? _battleRoomInfo;
+
+    /// <param name="connectKey">What a client must present to connect: the old game's key, or <c>BattleProtocol.ConnectKey</c>.</param>
+    /// <param name="battleRoomInfo">Writes a battle room's reply to a discovery broadcast, or returns false for none.</param>
+    public LiteNetServerTransport(int port, IRoomInfoProvider? roomInfo = null, int maxConnections = 8, string connectKey = ProtocolInfo.ConnectKey,
+        Func<BitWriter, bool>? battleRoomInfo = null)
     {
         _port = port;
         _roomInfo = roomInfo;
+        _connectKey = connectKey;
+        _battleRoomInfo = battleRoomInfo;
         _manager = new NetManager(_listener)
         {
             ChannelsCount = 2,
@@ -34,7 +42,7 @@ public sealed class LiteNetServerTransport : IServerTransport
         _listener.ConnectionRequestEvent += request =>
         {
             if (_manager.ConnectedPeersCount >= maxConnections) request.Reject();
-            else request.AcceptIfKey(ProtocolInfo.ConnectKey);
+            else request.AcceptIfKey(_connectKey);
         };
         _listener.PeerConnectedEvent += peer => _handler?.OnPeerConnected(peer.Id);
         _listener.PeerDisconnectedEvent += (peer, info) => _handler?.OnPeerDisconnected(peer.Id, info.Reason.ToString());
@@ -86,14 +94,15 @@ public sealed class LiteNetServerTransport : IServerTransport
 
     private void OnUnconnected(IPEndPoint remote, NetPacketReader reader, UnconnectedMessageType type)
     {
-        if (_roomInfo is null) return;
+        if (_roomInfo is null && _battleRoomInfo is null) return;
         var data = reader.GetRemainingBytesSpan();
         try
         {
             var r = new BitReader(data);
             if ((MessageType)r.ReadByte() != MessageType.Discover) return;
             _discoveryWriter.Reset();
-            Messages.Write(_discoveryWriter, _roomInfo.GetRoomInfo());
+            if (_roomInfo is not null) Messages.Write(_discoveryWriter, _roomInfo.GetRoomInfo());
+            else if (!_battleRoomInfo!(_discoveryWriter)) return;
             _reply.Reset();
             _reply.Put(_discoveryWriter.Data);
             _manager.SendUnconnectedMessage(_reply, remote);
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"` 和 `dotnet build src/Tank.Server/Tank.Server.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"`
Expected: 全部通过，Sim 622、Net 67；服务器 0 警告 0 错误。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Net tests/Tank.Net.Tests
git commit -m "feat(net): the battle server — a room of ten seats with bots, a match on the sandbox map, per-client events and delta snapshots holding only what each may see"
```

---

### Task 7：对战客户端

设计 §5.7～§5.9。`ClockSync` 加 tick 率参数。`Battle/BattleSnapshotStore`（快照环，按 tick 查）、`BattleInterpolator`（渲染 tick 跟着最新快照减插值延迟走，误差大就跳；两帧之间插值，没有新帧时有限外推；20 m 以上算瞬移）、`BattlePredictor`（输入历史；`Step` 用 `TankStep.Advance`（不撞倒物件、按 `ProtectedUntil` 判能否开炮）预测自己的车并生成预测炮弹；`Reconcile` 以服务器的本车状态为准重放未处理的输入，差 0.5 m 以上硬拉、以下记为视觉偏移按 τ=50 ms 衰减；炮弹的确认 / 结束 / 清理；修正统计）、`BattleClientTypes`（`BattlePhase`、`LocalCommand`、`ShellView`、`BattleStats`）、`BattleClient`：连上就 hello，入座后在房间；开局消息按 `MapData` 建场地并套物件状态；每 tick 预测、发输入包（冗余）；收快照：存、对账；收事件批：先套物件状态，再按渲染 tick 到点时播放——自己的 `ShellFired` 若对上预测炮弹就确认、否则当别人的炮弹飞；`ShellExploded` / `TankHit` 结束对应炮弹；其余事件进队列给表现层。`Others`（插值后的别人）、`Own`、`OwnRenderPosition`、`Shells`、`Spotted`、`MatchInfo`、`Stats`。`MatchRules` 加可配置时长，`BattleServerOptions.MatchSeconds`（测试用）。

**Files:**
- Create: `src/Tank.Net/Battle/BattleClient.cs`、`BattleClient.Match.cs`、`BattleClientTypes.cs`、`BattleInterpolator.cs`、`BattlePredictor.cs`、`BattleSnapshotStore.cs`
- Modify: `src/Tank.Net/Battle/BattleServer.Match.cs`、`BattleServerOptions.cs`、`src/Tank.Net/Client/ClockSync.cs`、`src/Tank.Sim/Modes/MatchRules.cs`
- Test: 新建 `tests/Tank.Net.Tests/Battle/BattleHarness.cs`、`BattleIntegrationTests.cs`

- [ ] **Step 1：写失败的测试**

新建 `tests/Tank.Net.Tests/Battle/BattleHarness.cs`：

```csharp
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Net.Tests.Battle;

/// <summary>One battle server and N clients on a deterministic simulated network with a virtual clock.</summary>
internal sealed class BattleHarness
{
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    private double _serverAccumulator;

    public BattleHarness(BattleServerOptions? options = null, uint networkSeed = 1)
    {
        Net = new LoopbackNetwork(networkSeed);
        Server = new BattleServer(Net.CreateServer(), options ?? new BattleServerOptions { Seed = 1 });
    }

    public LoopbackNetwork Net { get; }
    public BattleServer Server { get; }
    public List<BattleClient> Clients { get; } = new();
    public List<LoopbackClientTransport> Links { get; } = new();

    public BattleClient AddClient(string name, LinkConditions? up = null, LinkConditions? down = null, uint token = 0, int version = BattleProtocol.Version)
    {
        LoopbackClientTransport link = Net.Connect(up, down);
        var client = new BattleClient(link, name, () => Net.Now, token, version);
        Links.Add(link);
        Clients.Add(client);
        return client;
    }

    public void Run(double seconds, Func<int, double, LocalCommand>? input = null, double frameDt = 1.0 / 120)
    {
        int frames = (int)System.Math.Round(seconds / frameDt);
        for (int f = 0; f < frames; f++)
        {
            Net.AdvanceTime(frameDt);
            _serverAccumulator += frameDt;
            while (_serverAccumulator >= ServerDt - 1e-9)
            {
                Server.Tick();
                _serverAccumulator -= ServerDt;
            }
            for (int i = 0; i < Clients.Count; i++)
                Clients[i].Update(frameDt, input?.Invoke(i, Net.Now) ?? LocalCommand.None);
        }
    }

    /// <summary>Lets everyone join, then has the host set the mode and the bots and start; runs until the match is a second old.</summary>
    public void StartMatch(BattleMode mode = BattleMode.TeamBattle, int bots = 0)
    {
        Run(0.3);
        BattleClient host = Clients.Single(c => c.IsHost);
        host.SetMode(mode);
        host.SetBots(bots);
        Run(0.1);
        host.StartMatch();
        Run(1.0);
    }

    /// <summary>The server's state of <paramref name="client"/>'s tank.</summary>
    public Tank.Sim.Vehicles.TankState ServerTankOf(BattleClient client)
    {
        Simulation sim = Server.Simulation!;
        int slot = sim.SlotOf(Server.TankOf(client.Slot));
        Assert.True(slot >= 0, "client has no tank on the server");
        return sim.Tanks[slot];
    }
}
```

新建 `tests/Tank.Net.Tests/Battle/BattleIntegrationTests.cs`：

```csharp
using System.Numerics;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Tests.Battle;

public sealed class BattleIntegrationTests
{
    /// <summary>Drives about, swinging the aim and tapping fire now and then: a busy human.</summary>
    private static LocalCommand Wander(int client, double t) => Wander(client, t, fire: true);

    private static LocalCommand Wander(int client, double t, bool fire)
    {
        int phase = (int)(t / 0.7) + client * 3;
        sbyte throttle = (sbyte)((phase % 4) switch { 0 => 1, 1 => 1, 2 => -1, _ => 0 });
        sbyte steer = (sbyte)((phase % 3) switch { 0 => 1, 1 => -1, _ => 0 });
        bool shoot = fire && (int)(t * 10) % 11 == 0;
        return new LocalCommand(throttle, steer, (float)(t * 0.4 + client), 0.05f, shoot);   // one round type: a swap restarts the reload
    }

    private static LocalCommand Still(int client, double t) => new(0, 0, 0f, 0f, false);

    [Fact]
    public void Clients_join_the_room_the_first_is_host_and_the_host_starts_a_match()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("安娜"), b = h.AddClient("Bob");
        h.Run(0.3);
        Assert.Equal(BattlePhase.Room, a.Phase);
        Assert.Equal(BattlePhase.Room, b.Phase);
        Assert.True(a.IsHost);
        Assert.False(b.IsHost);
        Assert.Equal(new[] { "安娜", "Bob" }, b.Room!.Players.Select(p => p.Name));
        Assert.Equal(new byte[] { 0, 1 }, b.Room.Players.Select(p => p.Team));   // balanced on arrival

        b.StartMatch();   // not the host
        b.SetTank("heavy");
        h.Run(0.2);
        Assert.False(h.Server.MatchRunning);
        Assert.Equal(2, a.Room!.Players[1].TankIndex);

        a.SetMode(BattleMode.Annihilation);
        a.StartMatch();
        h.Run(1.0);
        Assert.True(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(BattlePhase.InMatch, c.Phase));
        Assert.All(h.Clients, c => Assert.True(c.HasTank));
        Assert.Equal(TankCatalog.Heavy, b.Own.Spec);
        Assert.Equal(1, b.Own.Team);
        Assert.Equal(BattleMode.Annihilation, a.MatchInfo!.Mode);
        Assert.Equal(h.Server.Simulation!.Field.Props, a.Field!.Props);
        Assert.Equal(MapData.Of(h.Server.Simulation.Field).HeightsCm, MapData.Of(a.Field).HeightsCm);
        Assert.True(a.Match.TicksLeft > 0);
        Assert.Equal(MatchPhase.Playing, a.Match.Phase);
    }

    [Fact]
    public void On_a_perfect_link_the_prediction_is_never_corrected()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        h.Run(8.0, Wander);
        h.Run(1.0, Still);   // the client predicts a few ticks ahead of the server: compare once both stand still
        foreach (BattleClient c in h.Clients)
        {
            Assert.Equal(0, c.Predictor.Corrections);
            Assert.True(c.Own.ShotCount >= 1, "fired at least once");
            Assert.Equal(h.ServerTankOf(c).Position, c.Own.Position);
        }
        _ = (a, b);
    }

    [Fact]
    public void Over_a_bad_link_corrections_stay_small_and_the_tank_settles_on_the_servers_position()
    {
        var bad = new LinkConditions(60, 40, 20);
        var h = new BattleHarness(networkSeed: 7);
        BattleClient a = h.AddClient("A", bad, bad), b = h.AddClient("B", bad, bad);
        h.StartMatch(BattleMode.TeamBattle);
        h.Run(10.0, Wander);
        Assert.All(h.Clients, c => Assert.True(c.Predictor.MaxCorrection < 2f, $"corrected by {c.Predictor.MaxCorrection} m"));
        h.Run(2.0, Still);
        foreach (BattleClient c in h.Clients)
            Assert.True(Vector2.Distance(h.ServerTankOf(c).Position, c.Own.Position) < 0.01f, "settled on the server's position");
        Assert.All(h.Clients, c => Assert.Equal(BattlePhase.InMatch, c.Phase));
        _ = (a, b);
    }

    [Fact]
    public void An_enemy_out_of_sight_is_not_in_the_snapshots_or_the_events_and_a_shot_from_the_dark_has_no_shooter()
    {
        // Team spawns are 280 m apart: two medium tanks standing still don't see each other (206 m).
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        int tankB = h.Server.TankOf(b.Slot), tankA = h.Server.TankOf(a.Slot);
        Assert.NotEqual(0, tankB);
        var fx = new List<GameEvent>();
        for (int i = 0; i < 100; i++)   // 10 s: the first 5 are spawn protection
        {
            h.Run(0.1, (c, t) => c == 1 ? new LocalCommand(0, 0, b.Own.Yaw, -0.02f, (int)(t * 10) % 15 == 0) : LocalCommand.None);   // B fires into the ground ahead: the bursts are events too
            while (a.TryDequeueEvent(out GameEvent e)) fx.Add(e);
            Assert.DoesNotContain(a.Others, o => o.Entity.Id == tankB);
            Assert.False(a.Latest!.TryFind(tankB, out _), "B's tank in A's snapshot");
            Assert.False(h.Server.Vision!.IsVisible(tankB, 0));
        }
        Assert.True(b.Own.ShotCount >= 2, $"B fired {b.Own.ShotCount} times");
        Assert.Contains(fx, e => e.Type == GameEventType.ShellFired && e.TankId == 0);
        Assert.Contains(fx, e => e.Type == GameEventType.ShellExploded && e.TankId == 0);   // the burst names no shooter either
        Assert.DoesNotContain(fx, e => e.TankId == tankB || e.TargetTankId == tankB);
        Assert.Contains(a.Latest!.Players, p => p.Active && p.TankId == tankB);   // the scoreboard still lists everyone
    }

    [Fact]
    public void Prop_damage_reaches_every_client_and_a_late_joiner_gets_the_props_as_they_stand()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        Simulation sim = h.Server.Simulation!;
        // The yard's gate (a brick wall segment just north of it) seen from team 0's spawn: lay A's gun on it and fire AP.
        Prop wall = sim.Field.Props.First(p => p.Kind == PropKind.BrickWall && p.Footprint.Center.X < 152 && p.Footprint.Center.Y > 339);
        var target = new Vector3(wall.Footprint.Center.X, sim.Field.Terrain.HeightAt(wall.Footprint.Center.X, wall.Footprint.Center.Y) + 1.5f, wall.Footprint.Center.Y);
        h.Run(6.0, (c, t) =>
        {
            if (c != 0) return LocalCommand.None;
            TankPose pose = TankPose.Compute(a.Own, a.Field!.Terrain);
            var (yaw, pitch) = Angles.AimAngles(target - pose.Trunnion(a.Own.Spec), a.Own.Yaw);
            return new LocalCommand(0, 0, yaw, pitch, t > 4.0);
        });
        int left = sim.Field.Durability(wall.Id);
        Assert.True(left < wall.MaxDurability, "the wall was not hit");
        int expected = BattleProtocol.DequantizeDurability(BattleProtocol.QuantizeDurability(left, wall.MaxDurability), wall.MaxDurability);
        Assert.Equal(expected, a.Field!.Durability(wall.Id));
        Assert.Equal(expected, b.Field!.Durability(wall.Id));

        BattleClient late = h.AddClient("Late");
        h.Run(1.0);
        Assert.Equal(BattlePhase.InMatch, late.Phase);   // a team battle takes late joiners
        Assert.True(late.HasTank);
        Assert.Equal(expected, late.Field!.Durability(wall.Id));
    }

    [Theory]
    [InlineData(BattleMode.TeamBattle)]
    [InlineData(BattleMode.Annihilation)]
    [InlineData(BattleMode.FreeForAll)]
    public void A_match_with_bots_runs_to_the_clock_and_everyone_returns_to_the_room(BattleMode mode)
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 3, MatchSeconds = 20 });
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(mode, bots: 4);
        Assert.Equal(6, h.Server.Simulation!.TankCount);
        Assert.Equal(6, a.Room!.Players.Count);
        Assert.Equal(4, a.Room.Players.Count(p => p.IsBot));
        h.Run(18.0, Wander);
        Assert.All(h.Clients, c => Assert.Equal(MatchPhase.Playing, c.Match.Phase));
        Assert.All(h.Clients, c => Assert.True(c.Stats.BytesInPerSecond < 20 * 1024, $"{c.Stats.BytesInPerSecond / 1024:F1} KB/s"));
        h.Run(2.0, Wander);
        Assert.Equal(MatchPhase.Ended, h.Server.Rules!.Phase);
        h.Run(0.5);
        Assert.All(h.Clients, c => Assert.Equal(MatchPhase.Ended, c.Match.Phase));
        if (mode == BattleMode.FreeForAll)
        {
            Assert.NotEqual(MatchRules.NoSlot, a.Match.WinnerSlot);
            Assert.All(a.Latest!.Players.Where(p => p.Active), p => Assert.InRange(p.Rank, 1, 6));
        }
        else Assert.Equal(h.Server.Rules.WinnerTeam, a.Match.WinnerTeam);
        h.Run(ModeRules.ResultsTicks / 30.0 + 0.5);
        Assert.False(h.Server.MatchRunning);
        Assert.All(h.Clients, c => Assert.Equal(BattlePhase.Room, c.Phase));
        Assert.Equal(2, a.Room!.Players.Count);   // the bots are gone
        _ = b;
    }

    [Fact]
    public void A_client_that_drops_reconnects_with_its_token_into_the_same_seat_and_tank()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        int tankB = h.Server.TankOf(b.Slot);
        h.Links[1].Dispose();
        h.Run(1.0, Wander);
        Assert.Equal(BattlePhase.Disconnected, b.Phase);
        Assert.Equal(2, h.Server.Simulation!.TankCount);   // kept for the grace period

        BattleClient back = h.AddClient("B again", token: b.SessionToken);
        h.Run(1.5, Wander);
        Assert.Equal(BattlePhase.InMatch, back.Phase);
        Assert.Equal(b.Slot, back.Slot);
        Assert.Equal(tankB, back.OwnTankId);
        Assert.True(back.HasTank);
        Assert.Equal(h.ServerTankOf(back).Position, back.Own.Position);
        _ = a;
    }

    [Fact]
    public void A_late_joiner_waits_in_the_room_during_a_match_without_respawns()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, MatchSeconds = 5 });
        h.AddClient("A");
        h.AddClient("B");
        h.StartMatch(BattleMode.Annihilation);
        BattleClient late = h.AddClient("Late");
        h.Run(1.0);
        Assert.Equal(BattlePhase.Room, late.Phase);
        Assert.True(late.Room!.MatchRunning);
        Assert.Equal(0, h.Server.TankOf(late.Slot));
        h.Run(4.0 + ModeRules.ResultsTicks / 30.0 + 1.0);
        Assert.False(h.Server.MatchRunning);
        Assert.Equal(3, late.Room!.Players.Count);
    }

    [Fact]
    public void A_player_who_leaves_for_good_is_taken_out_of_the_match()
    {
        var h = new BattleHarness(new BattleServerOptions { Seed = 1, ReconnectGraceSeconds = 1 });
        h.AddClient("A");
        BattleClient b = h.AddClient("B");
        h.StartMatch(BattleMode.TeamBattle);
        int tankB = h.Server.TankOf(b.Slot);
        h.Links[1].Dispose();
        h.Run(2.0);
        Assert.Equal(1, h.Server.Simulation!.TankCount);
        Assert.Equal(-1, h.Server.Simulation.SlotOf(tankB));
        Assert.Single(h.Clients[0].Room!.Players);
    }

    [Fact]
    public void A_wrong_version_is_rejected_with_a_reason_and_a_full_room_turns_the_eleventh_away()
    {
        var h = new BattleHarness();
        BattleClient old = h.AddClient("Old", version: 1);
        h.Run(0.5);
        Assert.Equal(BattlePhase.Disconnected, old.Phase);
        Assert.Contains("版本", old.DisconnectReason);

        var full = new BattleHarness();
        for (int i = 0; i < BattleProtocol.MaxPlayers; i++) full.AddClient($"P{i}");
        BattleClient extra = full.AddClient("Extra");
        full.Run(0.5);
        Assert.Equal(BattleProtocol.MaxPlayers, full.Clients.Count(c => c.Phase == BattlePhase.Room));
        Assert.Equal(BattlePhase.Disconnected, extra.Phase);
        Assert.Contains("满", extra.DisconnectReason);
    }

    [Fact]
    public void Own_shots_are_predicted_once_and_confirmed_by_the_server_and_others_shots_fly_on_the_client()
    {
        var h = new BattleHarness();
        BattleClient a = h.AddClient("A"), b = h.AddClient("B");
        // Put B within sight of A: a free-for-all spawns them 100 m or more apart on open ground, but team spawns are
        // out of sight; so use team battle and move B's tank next to A on the server.
        h.StartMatch(BattleMode.TeamBattle);
        Simulation sim = h.Server.Simulation!;
        TankState tankA = h.ServerTankOf(a);
        int tankB = h.Server.TankOf(b.Slot);
        sim.Respawn(tankB, tankA.Position + new Vector2(8, 0), tankA.Yaw);   // put B next to A
        h.Run(1.0);   // B's client snaps to the server's position; both teams' vision catches up
        var fxA = new List<GameEvent>();
        int seenOwnShell = 0, seenRemoteShell = 0;
        LocalCommand Aimed(int c, double t)
        {
            foreach (ShellView s in a.Shells)   // every frame: a shell aimed at the ground lives only a few ticks
            {
                if (s.Own) seenOwnShell++;
                else seenRemoteShell++;
            }
            while (a.TryDequeueEvent(out GameEvent e)) fxA.Add(e);
            // Aimed a little below level: a shell drops into the ground within a few ticks, so the burst is in range; B faces the other way.
            return new LocalCommand(0, 0, c == 0 ? tankA.Yaw : (float)(tankA.Yaw + 3.1), -0.02f, (int)(t * 10) % 25 == 0);
        }
        h.Run(12.0, Aimed);
        Assert.True(h.Server.Vision!.IsVisible(tankB, 0), "A sees B");
        Assert.True(a.Own.ShotCount >= 2, $"{a.Own.ShotCount} shots");
        Assert.True(seenOwnShell > 0, "own shells were shown");
        Assert.DoesNotContain(fxA, e => e.Type == GameEventType.ShellFired && e.TankId == a.OwnTankId);   // confirmed, not replayed
        Assert.Contains(fxA, e => e.Type == GameEventType.ShellExploded && e.TankId == a.OwnTankId);
        Assert.Empty(a.Predictor.Shells.Where(s => !s.Confirmed && !s.Ended).ToArray());
        Assert.True(seenRemoteShell > 0, "B's shells flew on A's client");
        Assert.Contains(fxA, e => e.Type == GameEventType.ShellFired && e.TankId == tankB);
    }
}
```


- [ ] **Step 2：运行，确认失败**

Run: `dotnet test tests/Tank.Net.Tests 2>&1 | grep -E "error CS" | sed 's/.*error/error/' | sort -u | head -5`
Expected: 编译失败，`BattleClient`、`LocalCommand`、`BattlePhase` 找不到。

- [ ] **Step 3：实现**

新建 `src/Tank.Net/Battle/BattleClient.Match.cs`：

```csharp
using System.Numerics;
using Tank.Net.Protocol;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

public sealed partial class BattleClient
{
    private readonly Queue<EventBatch> _pendingEvents = new();
    private readonly List<RemoteShell> _remoteShells = new(16);
    private readonly List<ShellView> _shellViews = new(16);
    private readonly Obb2[] _obstacles = new Obb2[Simulation.MaxTanks];
    private int _droppedPackets;
    private int _lastProcessedInputTick;

    // stats accumulators
    private double _statsTimer;
    private int _snapshotsThisSecond, _snapshotsPerSecond, _lastSnapshotBytes;
    private long _lastBytesIn, _lastBytesOut;
    private double _bytesInRate, _bytesOutRate;

    /// <summary>Another tank's shell, flown here from its shot event (design §5.7); the server's events say what it struck.</summary>
    private struct RemoteShell
    {
        public Shell Shell;
        public Vector3 Previous;
        public bool Ended;
    }

    public MatchStartMessage? MatchInfo { get; private set; }

    /// <summary>The client's battlefield: built from the server's map, its props kept as the server reports them.</summary>
    public Battlefield? Field { get; private set; }

    public int OwnTankId { get; private set; }
    public BattleSnapshot? Latest => Store.Newest;

    /// <summary>The match as the newest snapshot shows it.</summary>
    public MatchBlock Match => Latest?.Match ?? default;

    public bool Spotted => Match.Spotted;

    /// <summary>The own tank as predicted, where to draw it (prediction plus the correction being smoothed away), and the blend between ticks.</summary>
    public bool HasTank => Predictor.HasTank;
    public TankState Own => Predictor.Current;
    public Vector2 OwnRenderPosition => Vector2.Lerp(Predictor.Previous.Position, Predictor.Current.Position, (float)Clock.Alpha) + Predictor.VisualOffset;

    /// <summary>Every shell in flight as of the last tick: the own predicted ones and the others' simulated ones.</summary>
    public IReadOnlyList<ShellView> Shells
    {
        get
        {
            _shellViews.Clear();
            foreach (PredictedShell s in Predictor.Shells)
                if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, true));
            foreach (RemoteShell s in _remoteShells)
                if (!s.Ended) _shellViews.Add(new ShellView(s.Shell.Id, s.Shell.OwnerTankId, s.Previous, s.Shell.Position, s.Shell.Velocity, s.Shell.Ammo, false));
            return _shellViews;
        }
    }

    private void HandleMatchStart(MatchStartMessage msg)
    {
        // Same match number: the server is resyncing us (a reconnect). Anything else is a new match.
        bool resync = Phase == BattlePhase.InMatch && MatchInfo is { } current && current.MatchNumber == msg.MatchNumber;
        MatchInfo = msg;
        Field = msg.Map.Build();
        foreach (PropState p in msg.Props) ApplyProp(p);
        OwnTankId = msg.MyTankId;
        _pendingEvents.Clear();
        _remoteShells.Clear();
        if (resync) return;
        Store.Clear();
        Interpolator.Reset();
        Clock.Reset();
        Predictor.Reset();
        _fxEvents.Clear();
        _lastProcessedInputTick = msg.CurrentTick;
        Phase = BattlePhase.InMatch;
    }

    private void LeaveMatch()
    {
        Phase = BattlePhase.Room;
        MatchInfo = null;
        Field = null;
        OwnTankId = 0;
        _pendingEvents.Clear();
        _remoteShells.Clear();
        Predictor.Reset();
        _others.Clear();
    }

    private void HandleSnapshot(ref BitReader r, int bytes)
    {
        if (Phase != BattlePhase.InMatch || Field is null) return;
        _codec.Read(ref r, _findSnapshot, _decodeScratch, out ClientFeedback feedback, out _);
        if (!Store.Add(_decodeScratch)) return;
        _snapshotsThisSecond++;
        _lastSnapshotBytes = bytes;
        double now = _clock();
        Interpolator.OnSnapshot(_decodeScratch.Tick, now);
        if (_decodeScratch.Tick != Store.NewestTick) return;   // reordered: a baseline and an interpolation frame, but stale feedback

        BattleSnapshot snap = Store.Newest!;
        Clock.OnServerFeedback(feedback.InputSlack, snap.Tick, _transport.Stats.RttMs / 1000.0);
        _lastProcessedInputTick = feedback.LastProcessedInputTick;
        int n = BuildObstacles(snap);
        Predictor.Reconcile(OwnState(snap), feedback.LastProcessedInputTick, snap.Match.Phase == MatchPhase.Ended, Field, _obstacles.AsSpan(0, n));
        Predictor.Prune(feedback.LastProcessedInputTick);
        if (snap.HasOwn) OwnTankId = snap.Own.Id;
    }

    /// <summary>The own tank as the server has it, with the spec and team its entity carries.</summary>
    private static TankState? OwnState(BattleSnapshot snap)
    {
        if (!snap.HasOwn || !snap.TryFind(snap.Own.Id, out BattleEntity e)) return null;
        OwnTank o = snap.Own;
        return new TankState
        {
            Id = o.Id, Spec = TankCatalog.All[e.Spec], Team = e.Team, Position = new Vector2(o.X, o.Z), Yaw = o.Yaw, Speed = o.Speed,
            TurretYaw = o.TurretYaw, GunPitch = o.GunPitch, Reload = o.Reload, ShotCount = o.ShotCount, LastFired = o.LastFired,
            Health = o.Health, IsDestroyed = o.Destroyed, ProtectedUntil = o.ProtectedUntil, Ammo = o.Ammo, ApLeft = o.ApLeft, HeLeft = o.HeLeft, Bloom = o.Bloom,
        };
    }

    private void OnEventBatch(EventBatch batch)
    {
        foreach (PropState p in batch.Props) ApplyProp(p);   // the newest prop state at once: prediction drives on it
        _pendingEvents.Enqueue(batch);
    }

    private void ApplyProp(PropState p)
    {
        if (Field is null) return;
        Prop prop = Field.PropById(p.PropId);
        if (prop.IsDestructible) Field.SetDurability(p.PropId, BattleProtocol.DequantizeDurability(p.Durability, prop.MaxDurability));
    }

    private void SimulateTick(in LocalCommand input)
    {
        int tick = Clock.NextTick();
        bool ended = Latest is { Match.Phase: MatchPhase.Ended };
        var (yaw, pitch) = BattleProtocol.QuantizedAim(input.AimYaw, input.AimPitch);
        InputCmd cmd = ended ? BattleInputBuffer.Idle(tick, new InputCmd(tick, 0, 0, yaw, pitch, false, input.Ammo))
            : new InputCmd(tick, input.Throttle, input.Steer, yaw, pitch, input.Fire, input.Ammo);
        int n = Latest is null ? 0 : BuildObstacles(Latest);
        Predictor.Step(cmd, Field!, _obstacles.AsSpan(0, n));
        StepRemoteShells();

        int count = Predictor.RecentInputs(_sendScratch);
        _writer.Reset();
        BattleInputPacket.Write(_writer, Store.NewestTick, _sendScratch.AsSpan(0, count));
        _transport.Send(_writer.Data, Channel.Unreliable);
    }

    /// <summary>The other tanks' footprints, as the newest snapshot has them: what the own tank bumps into.</summary>
    private int BuildObstacles(BattleSnapshot snap)
    {
        int n = 0;
        foreach (ref readonly BattleEntity e in snap.EntitySpan)
        {
            if (e.Id == OwnTankId || e.Id == snap.Own.Id && snap.HasOwn) continue;
            TankSpec spec = TankCatalog.All[e.Spec];
            var at = new Vector2(BattleProtocol.FromCm(e.X), BattleProtocol.FromCm(e.Z));
            _obstacles[n++] = TankMotion.Footprint(spec, at, BattleProtocol.DequantizeAngle(e.Yaw, BattleProtocol.YawBits));
        }
        return n;
    }

    private void StepRemoteShells()
    {
        for (int i = _remoteShells.Count - 1; i >= 0; i--)
        {
            RemoteShell s = _remoteShells[i];
            if (s.Ended)
            {
                _remoteShells.RemoveAt(i);
                continue;
            }
            s.Previous = s.Shell.Position;
            if (Ballistics.Step(ref s.Shell, Field!, out _) != ShellStep.Flying) s.Ended = true;
            _remoteShells[i] = s;
        }
    }

    /// <summary>Plays the events whose tick the render clock has reached: shots become shells, bursts end them, the rest go to the presentation.</summary>
    private void ApplyDueEvents()
    {
        while (_pendingEvents.Count > 0 && _pendingEvents.Peek().Tick <= Interpolator.RenderTick)
        {
            EventBatch batch = _pendingEvents.Dequeue();
            foreach (GameEvent e in batch.Events) ApplyEvent(e);
        }
    }

    private void ApplyEvent(in GameEvent e)
    {
        switch (e.Type)
        {
            case GameEventType.ShellFired when e.TankId == OwnTankId && Predictor.Confirm(e.Tick, e.ShellId):
                return;   // already shown as our predicted shot
            case GameEventType.ShellFired:
                _remoteShells.Add(new RemoteShell
                {
                    Shell = new Shell { Id = e.ShellId, OwnerTankId = e.TankId, Position = e.Position, Velocity = e.Velocity, Ammo = e.Ammo },
                    Previous = e.Position,
                });
                break;
            case GameEventType.ShellExploded:
            case GameEventType.TankHit:
                EndShell(e.ShellId);
                break;
        }
        _fxEvents.Enqueue(e);
    }

    private void EndShell(int shellId)
    {
        Predictor.End(shellId);
        for (int i = 0; i < _remoteShells.Count; i++)
        {
            if (_remoteShells[i].Shell.Id != shellId) continue;
            RemoteShell s = _remoteShells[i];
            s.Ended = true;
            _remoteShells[i] = s;
        }
    }

    private void UpdateStats(double dt)
    {
        _statsTimer += dt;
        TransportStats t = _transport.Stats;
        if (_statsTimer >= 1.0)
        {
            _bytesInRate = (t.BytesReceived - _lastBytesIn) / _statsTimer;
            _bytesOutRate = (t.BytesSent - _lastBytesOut) / _statsTimer;
            _lastBytesIn = t.BytesReceived;
            _lastBytesOut = t.BytesSent;
            _snapshotsPerSecond = _snapshotsThisSecond;
            _snapshotsThisSecond = 0;
            _statsTimer = 0;
        }
        Stats = new BattleStats(t.RttMs, Interpolator.JitterPeakSeconds * 1000, Interpolator.DelayTicks * 1000.0 / SimConstants.TickRate, t.PacketLossPercent,
            _bytesInRate, _bytesOutRate, _snapshotsPerSecond, _lastSnapshotBytes, Interpolator.ExtrapolatedFrames,
            Predictor.Corrections, Predictor.LastCorrection, Predictor.MeanCorrection, Predictor.MaxCorrection, Clock.SmoothedSlack, Clock.Rate, _droppedPackets);
    }
}
```

新建 `src/Tank.Net/Battle/BattleClient.cs`：

```csharp
using Tank.Net.Client;
using Tank.Net.Protocol;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

/// <summary>
/// The battle client (design §5.9): connects, sits in the room, and in a match owns prediction, reconciliation,
/// interpolation, the shells and the prop state, exposing ready-to-draw state. Drive it with <see cref="Update"/> once
/// per rendered frame. Engine-free: tests and the soak run it as it is.
/// </summary>
public sealed partial class BattleClient : ITransportHandler, IDisposable
{
    private readonly IClientTransport _transport;
    private readonly Func<double> _clock;
    private readonly string _playerName;
    private readonly int _protocolVersion;
    private readonly BitWriter _writer = new(512);
    private readonly BattleSnapshotCodec _codec = new();
    private readonly BattleSnapshot _decodeScratch = new();
    private readonly InputCmd[] _sendScratch = new InputCmd[BattleProtocol.MaxRedundantInputs];
    private readonly List<RemoteTank> _others = new(BattleProtocol.MaxPlayers);
    private readonly Queue<GameEvent> _fxEvents = new();
    private readonly Func<int, BattleSnapshot?> _findSnapshot;
    private const double ResyncRequestInterval = 1.0;   // s
    private double _lastResyncRequest = double.NegativeInfinity;

    public BattleClient(IClientTransport transport, string playerName, Func<double> clock, uint sessionToken = 0)
        : this(transport, playerName, clock, sessionToken, BattleProtocol.Version) { }

    /// <summary>For tests: say hello with another protocol version.</summary>
    internal BattleClient(IClientTransport transport, string playerName, Func<double> clock, uint sessionToken, int protocolVersion)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _playerName = playerName;
        _protocolVersion = protocolVersion;
        SessionToken = sessionToken;
        Store = new BattleSnapshotStore();
        Interpolator = new BattleInterpolator(Store);
        Clock = new ClockSync(SimConstants.TickRate);
        _findSnapshot = Store.Find;
    }

    public BattlePhase Phase { get; private set; } = BattlePhase.Connecting;
    public string? DisconnectReason { get; private set; }
    public byte Slot { get; private set; } = MatchRules.NoSlot;
    public uint SessionToken { get; private set; }
    public RoomStateMessage? Room { get; private set; }
    public bool IsHost => Room is not null && Room.HostSlot == Slot;

    public BattleSnapshotStore Store { get; }
    public BattleInterpolator Interpolator { get; }
    public ClockSync Clock { get; }
    public BattlePredictor Predictor { get; } = new();

    /// <summary>The other tanks at render time, interpolated; the own tank is in <see cref="Predictor"/>.</summary>
    public IReadOnlyList<RemoteTank> Others => _others;

    public BattleStats Stats { get; private set; }

    /// <summary>Why the most recent packet was dropped, if any.</summary>
    public string? LastPacketError { get; private set; }

    /// <summary>How many times the match state was asked for again after an undecodable snapshot.</summary>
    public int Resyncs { get; private set; }

    public void Update(double dt, in LocalCommand input)
    {
        _transport.Poll(this);
        if (Phase == BattlePhase.InMatch && Field is not null)
        {
            int ticks = Clock.Advance(dt);
            for (int i = 0; i < ticks; i++) SimulateTick(input);
            double now = _clock();
            Interpolator.Advance(dt, now);
            ApplyDueEvents();
            Interpolator.Sample(_others, OwnTankId);
            Predictor.DecayVisualOffset(dt);
        }
        UpdateStats(dt);
    }

    /// <summary>Presentation events (shots, bursts, hits) in the order they should play.</summary>
    public bool TryDequeueEvent(out GameEvent e) => _fxEvents.TryDequeue(out e);

    // ---- room commands ----

    public void SetMode(BattleMode mode) => SendRoom(RoomCommandKind.SetMode, ModeRules.Id(mode));
    public void SetTeam(byte team) => SendRoom(RoomCommandKind.SetTeam, team.ToString());
    public void SetTank(string tankId) => SendRoom(RoomCommandKind.SetTank, tankId);
    public void SetBots(int bots) => SendRoom(RoomCommandKind.SetBots, bots.ToString());
    public void SetReady(bool ready) => SendRoom(RoomCommandKind.SetReady, ready ? "1" : "0");
    public void StartMatch() => SendRoom(RoomCommandKind.StartMatch, string.Empty);
    public void ReturnToRoom() => SendRoom(RoomCommandKind.ReturnToRoom, string.Empty);

    public void Dispose() => _transport.Dispose();

    // ---- transport callbacks ----

    public void OnPeerConnected(int peerId)
    {
        _writer.Reset();
        BattleMessages.Write(_writer, new HelloMessage(_protocolVersion, _playerName, SessionToken));
        _transport.Send(_writer.Data, Channel.Reliable);
    }

    public void OnPeerDisconnected(int peerId, string reason)
    {
        Phase = BattlePhase.Disconnected;
        DisconnectReason ??= reason;
    }

    public void OnData(int peerId, ReadOnlySpan<byte> data)
    {
        if (data.Length == 0) return;
        var type = (BattleMessage)data[0];
        try
        {
            var r = new BitReader(data);
            r.ReadByte();
            switch (type)
            {
                case BattleMessage.Welcome: HandleWelcome(BattleMessages.ReadWelcome(ref r)); break;
                case BattleMessage.Reject: DisconnectReason = BattleMessages.ReadReject(ref r).Reason; Phase = BattlePhase.Disconnected; break;
                case BattleMessage.RoomState: HandleRoom(BattleMessages.ReadRoomState(ref r)); break;
                case BattleMessage.MatchStart: HandleMatchStart(BattleMessages.ReadMatchStart(ref r)); break;
                case BattleMessage.Snapshot: HandleSnapshot(ref r, data.Length); break;
                case BattleMessage.Events: OnEventBatch(BattleMessages.ReadEvents(ref r)); break;
            }
        }
        catch (Exception ex) when (ex is ProtocolException or FormatException or ArgumentException or InvalidOperationException or OverflowException)
        {
            // A malformed packet is dropped; the game keeps running and the reason is kept for diagnostics.
            _droppedPackets++;
            LastPacketError = ex.Message;
            if (type == BattleMessage.Snapshot && Phase == BattlePhase.InMatch) RequestMatchState();
        }
    }

    /// <summary>
    /// A snapshot we can't make sense of (a baseline we never got, an entity we don't know) means we have fallen out of
    /// step: ask the server to send the match state again, at most once a second (it also rate-limits).
    /// </summary>
    private void RequestMatchState()
    {
        double now = _clock();
        if (now - _lastResyncRequest < ResyncRequestInterval) return;
        _lastResyncRequest = now;
        _writer.Reset();
        _writer.WriteByte((byte)BattleMessage.RequestMatchState);
        _transport.Send(_writer.Data, Channel.Reliable);
        Resyncs++;
    }

    private void HandleWelcome(WelcomeMessage welcome)
    {
        Slot = welcome.Slot;
        SessionToken = welcome.SessionToken;
        if (Phase == BattlePhase.Connecting) Phase = BattlePhase.Room;
    }

    private void HandleRoom(RoomStateMessage room)
    {
        Room = room;
        if (!room.MatchRunning && Phase == BattlePhase.InMatch) LeaveMatch();
    }

    private void SendRoom(RoomCommandKind kind, string arg)
    {
        _writer.Reset();
        BattleMessages.Write(_writer, new RoomCommandMessage(kind, arg));
        _transport.Send(_writer.Data, Channel.Reliable);
    }
}
```

新建 `src/Tank.Net/Battle/BattleClientTypes.cs`：

```csharp
using System.Numerics;
using Tank.Sim.Combat;

namespace Tank.Net.Battle;

public enum BattlePhase
{
    Connecting,
    Room,
    InMatch,
    Disconnected,
}

/// <summary>What the player wants this frame: the sticks, the aim, the trigger and the round.</summary>
public readonly record struct LocalCommand(sbyte Throttle, sbyte Steer, float AimYaw, float AimPitch, bool Fire, AmmoType Ammo = AmmoType.AP)
{
    public static readonly LocalCommand None = new(0, 0, 0f, 0f, false);
}

/// <summary>A shell to draw: the client's own (predicted) or another tank's (flown here from the server's shot event).</summary>
public readonly record struct ShellView(int ShellId, int OwnerTankId, Vector3 Previous, Vector3 Position, Vector3 Velocity, AmmoType Ammo, bool Own);

/// <summary>Numbers for the net panel (design §5.9).</summary>
public readonly record struct BattleStats(
    int RttMs,
    double JitterMs,
    double InterpDelayMs,
    float PacketLossPercent,
    double BytesInPerSecond,
    double BytesOutPerSecond,
    int SnapshotsPerSecond,
    int SnapshotBytes,
    int Extrapolations,
    int Corrections,
    float LastCorrection,
    double MeanCorrection,
    float MaxCorrection,
    double InputSlackTicks,
    double ClockRate,
    int DroppedPackets);
```

新建 `src/Tank.Net/Battle/BattleInterpolator.cs`：

```csharp
using System.Numerics;
using Tank.Sim;
using Tank.Sim.Geometry;

namespace Tank.Net.Battle;

/// <summary>Another tank, ready to draw: the newest wire data and a smoothed pose in metres and radians.</summary>
public readonly record struct RemoteTank(BattleEntity Entity, Vector2 Position, float Yaw, float TurretYaw, float GunPitch, float Speed, bool Extrapolated);

/// <summary>
/// Draws the other tanks a little in the past, between two received snapshots, so they move smoothly (design §5.1).
/// The delay adapts to measured arrival jitter and falls back to short extrapolation when snapshots stop arriving.
/// </summary>
public sealed class BattleInterpolator
{
    private const double MinDelayTicks = 3.0;           // 100 ms (design §5.1): one lost snapshot doesn't mean extrapolating
    private const double MaxDelayTicks = 6.0;
    private const double BaseDelayTicks = 1.0;          // one snapshot interval …
    private const double JitterMargin = 1.2;            // … plus the measured jitter with a margin …
    private const double HalfTickSlack = 0.5;           // … plus half a tick so the render time sits between two snapshots
    private const double MaxExtrapolationTicks = 3.0;   // 100 ms
    private const double DelayChangePerSecond = 1.0;
    private const double JitterPeakDecay = 0.99;        // per snapshot: a spike fades in a few seconds
    private const double MaxJitterSample = 0.25;        // one long stall must not pin the delay at max
    private const double SnapThresholdTicks = 8.0;
    private const int TeleportCm = 2000;                // a jump of 20 m in a tick is a respawn, not a move

    private readonly BattleSnapshotStore _store;
    private int _newestTick = -1;
    private double _newestArrival;
    private int _prevTick = -1;
    private double _prevArrival;
    private bool _started;

    public BattleInterpolator(BattleSnapshotStore store) => _store = store;

    public double RenderTick { get; private set; }
    public double DelayTicks { get; private set; } = MinDelayTicks;
    public double JitterPeakSeconds { get; private set; }
    public int ExtrapolatedFrames { get; private set; }

    public void Reset()
    {
        _newestTick = _prevTick = -1;
        _started = false;
        JitterPeakSeconds = 0;
        DelayTicks = MinDelayTicks;
    }

    public void OnSnapshot(int tick, double now)
    {
        if (tick <= _newestTick) return;   // reordered or duplicate: stored, but not a timing sample
        if (_prevTick >= 0)
        {
            double expected = (tick - _prevTick) / (double)SimConstants.TickRate;
            double deviation = System.Math.Min(System.Math.Abs(now - _prevArrival - expected), MaxJitterSample);
            JitterPeakSeconds = System.Math.Max(deviation, JitterPeakSeconds * JitterPeakDecay);
        }
        _prevTick = tick;
        _prevArrival = now;
        _newestTick = tick;
        _newestArrival = now;
    }

    public void Advance(double dt, double now)
    {
        if (_newestTick < 0) return;
        double target = System.Math.Clamp(BaseDelayTicks + JitterPeakSeconds * SimConstants.TickRate * JitterMargin + HalfTickSlack, MinDelayTicks, MaxDelayTicks);
        double maxStep = DelayChangePerSecond * dt;
        DelayTicks += System.Math.Clamp(target - DelayTicks, -maxStep, maxStep);
        double estimatedServerTick = _newestTick + (now - _newestArrival) * SimConstants.TickRate;
        double wanted = estimatedServerTick - DelayTicks;
        if (!_started)
        {
            RenderTick = wanted;
            _started = true;
            return;
        }
        RenderTick += dt * SimConstants.TickRate;
        double error = wanted - RenderTick;
        if (System.Math.Abs(error) > SnapThresholdTicks) RenderTick = wanted;
        else RenderTick += error * System.Math.Min(1.0, dt * 3.0);
    }

    /// <summary>Fills <paramref name="output"/> with every tank but <paramref name="ownId"/> at <see cref="RenderTick"/>.</summary>
    public void Sample(List<RemoteTank> output, int ownId)
    {
        output.Clear();
        int floor = (int)System.Math.Floor(RenderTick);
        BattleSnapshot? a = _store.FindAtOrBefore(floor);
        BattleSnapshot? b = _store.FindAfter(floor);
        if (a is null && b is null) return;
        if (a is null)
        {
            foreach (ref readonly BattleEntity e in b!.EntitySpan)
                if (e.Id != ownId) output.Add(At(e, e.X, e.Z, Yaw(e.Yaw), Yaw(e.TurretYaw), Pitch(e.Pitch), Speed(e.Speed), false));
            return;
        }
        if (b is null)
        {
            Extrapolate(a, output, ownId);
            return;
        }
        float t = (float)((RenderTick - a.Tick) / (b.Tick - a.Tick));
        foreach (ref readonly BattleEntity ea in a.EntitySpan)
        {
            if (ea.Id == ownId) continue;
            if (!b.TryFind(ea.Id, out BattleEntity eb) || System.Math.Abs(eb.X - ea.X) > TeleportCm || System.Math.Abs(eb.Z - ea.Z) > TeleportCm)
            {
                BattleEntity pick = b.TryFind(ea.Id, out BattleEntity later) && t >= 0.5f ? later : ea;
                output.Add(At(pick, pick.X, pick.Z, Yaw(pick.Yaw), Yaw(pick.TurretYaw), Pitch(pick.Pitch), Speed(pick.Speed), false));
                continue;
            }
            BattleEntity latest = t < 0.5f ? ea : eb;   // discrete fields (health, flags) switch at the midpoint
            output.Add(At(latest, ea.X + (eb.X - ea.X) * t, ea.Z + (eb.Z - ea.Z) * t,
                Angles.Lerp(Yaw(ea.Yaw), Yaw(eb.Yaw), t), Angles.Lerp(Yaw(ea.TurretYaw), Yaw(eb.TurretYaw), t),
                Pitch(ea.Pitch) + (Pitch(eb.Pitch) - Pitch(ea.Pitch)) * t, Speed(ea.Speed) + (Speed(eb.Speed) - Speed(ea.Speed)) * t, false));
        }
    }

    private void Extrapolate(BattleSnapshot a, List<RemoteTank> output, int ownId)
    {
        float ahead = (float)System.Math.Min(RenderTick - a.Tick, MaxExtrapolationTicks);
        if (ahead > 0.01f) ExtrapolatedFrames++;
        BattleSnapshot? prev = _store.FindAtOrBefore(a.Tick - 1);
        foreach (ref readonly BattleEntity e in a.EntitySpan)
        {
            if (e.Id == ownId) continue;
            float vx = 0, vz = 0;
            if (prev is not null && prev.TryFind(e.Id, out BattleEntity p) && a.Tick > prev.Tick)
            {
                vx = (e.X - p.X) / (float)(a.Tick - prev.Tick);
                vz = (e.Z - p.Z) / (float)(a.Tick - prev.Tick);
                if (System.Math.Abs(vx) > TeleportCm || System.Math.Abs(vz) > TeleportCm) vx = vz = 0;
            }
            output.Add(At(e, e.X + vx * ahead, e.Z + vz * ahead, Yaw(e.Yaw), Yaw(e.TurretYaw), Pitch(e.Pitch), Speed(e.Speed), ahead > 0.01f));
        }
    }

    private static RemoteTank At(in BattleEntity e, float xCm, float zCm, float yaw, float turret, float pitch, float speed, bool extrapolated) =>
        new(e, new Vector2(xCm * BattleProtocol.PositionUnit, zCm * BattleProtocol.PositionUnit), yaw, turret, pitch, speed, extrapolated);

    private static float Yaw(ushort q) => BattleProtocol.DequantizeAngle(q, BattleProtocol.YawBits);
    private static float Pitch(ushort q) => BattleProtocol.DequantizePitch(q, BattleProtocol.PitchBits);
    private static float Speed(short q) => q * BattleProtocol.SpeedUnit;
}
```

新建 `src/Tank.Net/Battle/BattlePredictor.cs`：

```csharp
using System.Numerics;
using Tank.Sim;
using Tank.Sim.Combat;
using Tank.Sim.Geometry;
using Tank.Sim.Maps;
using Tank.Sim.Vehicles;

namespace Tank.Net.Battle;

/// <summary>A shot the client predicted: the shell as it flies on the client, and how the server's word on it stands.</summary>
public struct PredictedShell
{
    public Shell Shell;
    public Vector3 Previous;   // where it was a tick ago, to draw between
    public int SpawnTick;
    public bool Confirmed;     // the server reported the same shot (ShellFired on SpawnTick)
    public bool Ended;         // it struck something, here or on the server: hidden, kept until the server's events are done with it
}

/// <summary>
/// Client-side prediction of the player's own tank (design §5.7): every tick the tank is stepped with
/// <see cref="TankStep.Advance"/> (not knocking props down) on the client's battlefield, exactly as the server will
/// step it; on every snapshot the tank is rewound to the server's state and the inputs the server hasn't processed yet
/// are replayed. A small correction (under <see cref="SnapDistance"/>) is hidden by a visual offset that decays over
/// about 150 ms; a large one snaps.
/// </summary>
public sealed class BattlePredictor
{
    private const int InputHistorySize = 128;
    public const float SnapDistance = 0.5f;

    /// <summary>
    /// A reconciliation moves the tank at least this far to count as a correction, m: smaller ones are the wire's
    /// centimetre quantum and float noise from touching another tank, not a misprediction.
    /// </summary>
    public const float MinCountedCorrection = 0.02f;
    private const double OffsetTimeConstant = 0.05;   // s: 95 % gone in 150 ms
    private const int OrphanShellTicks = 15;          // an unconfirmed shot is dropped this long after the server passed its tick

    private readonly InputCmd[] _inputs = new InputCmd[InputHistorySize];
    private readonly List<PredictedShell> _shells = new(8);
    private readonly List<(int Tick, int ShellId)> _expectedShots = new(4);   // the server's word on shots we haven't predicted yet

    public bool HasTank { get; private set; }
    public TankState Current;
    public TankState Previous;
    public Vector2 VisualOffset { get; private set; }
    public int LastPredictedTick { get; private set; } = -1;

    /// <summary>
    /// Corrections made (reconciliations of at least <see cref="MinCountedCorrection"/>, a respawn not being one), and the
    /// last, mean and largest distance corrected, m.
    /// </summary>
    public int Corrections { get; private set; }
    public float LastCorrection { get; private set; }
    public float MaxCorrection { get; private set; }
    public double MeanCorrection => Corrections == 0 ? 0 : _correctionSum / Corrections;
    private double _correctionSum;

    public List<PredictedShell> Shells => _shells;

    public void Reset()
    {
        HasTank = false;
        _shells.Clear();
        _expectedShots.Clear();
        VisualOffset = Vector2.Zero;
        LastPredictedTick = -1;
        Array.Clear(_inputs);
        Corrections = 0;
        LastCorrection = MaxCorrection = 0;
        _correctionSum = 0;
    }

    public InputCmd InputAt(int tick)
    {
        InputCmd cmd = _inputs[tick & (InputHistorySize - 1)];
        return cmd.Tick == tick ? cmd : BattleInputBuffer.Idle(tick, cmd);
    }

    /// <summary>Copies the newest inputs (newest first) for redundant sending.</summary>
    public int RecentInputs(Span<InputCmd> output)
    {
        int n = 0;
        for (int t = LastPredictedTick; t > LastPredictedTick - output.Length && t >= 0; t--)
        {
            InputCmd cmd = _inputs[t & (InputHistorySize - 1)];
            if (cmd.Tick != t) break;
            output[n++] = cmd;
        }
        return n;
    }

    /// <summary>Predicts one tick; <paramref name="others"/> are the other tanks' footprints as last seen.</summary>
    public void Step(in InputCmd cmd, Battlefield field, ReadOnlySpan<Obb2> others)
    {
        _inputs[cmd.Tick & (InputHistorySize - 1)] = cmd;
        LastPredictedTick = cmd.Tick;
        if (!HasTank) return;
        Previous = Current;
        if (Advance(ref Current, cmd, field, others, out TankStep.Shot shot))
        {
            var predicted = new PredictedShell { Shell = shot.Shell, Previous = shot.Shell.Position, SpawnTick = cmd.Tick };
            for (int i = 0; i < _expectedShots.Count; i++)
            {
                if (_expectedShots[i].Tick != cmd.Tick) continue;
                predicted.Confirmed = true;   // the server told us first (we were behind): one shell, not two
                predicted.Shell.Id = _expectedShots[i].ShellId;
                _expectedShots.RemoveAt(i);
                break;
            }
            _shells.Add(predicted);
        }
        StepShells(field);
    }

    private static bool Advance(ref TankState tank, in InputCmd cmd, Battlefield field, ReadOnlySpan<Obb2> others, out TankStep.Shot shot) =>
        TankStep.Advance(ref tank, cmd, cmd.Tick, field, others, knockDown: false, null, canFire: cmd.Tick >= tank.ProtectedUntil, out shot);

    /// <summary>Rewinds to the server's state for the own tank and replays the inputs it has not processed yet.</summary>
    /// <param name="serverState">The own tank as the server has it, or null when the client has no tank.</param>
    public void Reconcile(TankState? serverState, int lastProcessedInputTick, bool matchEnded, Battlefield field, ReadOnlySpan<Obb2> others)
    {
        if (serverState is not { } state)
        {
            HasTank = false;
            return;
        }
        TankState replayed = state;
        int replayUntil = matchEnded ? lastProcessedInputTick : LastPredictedTick;
        for (int tick = lastProcessedInputTick + 1; tick <= replayUntil; tick++) Advance(ref replayed, InputAt(tick), field, others, out _);

        bool respawned = Current.IsDestroyed && !replayed.IsDestroyed;   // back somewhere else: a new start, not a correction
        if (!HasTank || Current.Id != replayed.Id || respawned)
        {
            HasTank = true;
            Current = Previous = replayed;
            VisualOffset = Vector2.Zero;
            _shells.Clear();
            return;
        }
        Vector2 error = Current.Position - replayed.Position;
        float distance = error.Length();
        if (distance >= MinCountedCorrection)
        {
            Corrections++;
            LastCorrection = distance;
            MaxCorrection = MathF.Max(MaxCorrection, distance);
            _correctionSum += distance;
        }
        if (distance > 0f)
        {
            VisualOffset = distance >= SnapDistance ? Vector2.Zero : VisualOffset + error;
            Previous.Position -= error;
        }
        Current = replayed;
    }

    public void DecayVisualOffset(double dt)
    {
        VisualOffset *= (float)System.Math.Exp(-dt / OffsetTimeConstant);
        if (VisualOffset.LengthSquared() < 1e-6f) VisualOffset = Vector2.Zero;
    }

    /// <summary>
    /// The server reported a shot of ours on <paramref name="tick"/>: the shell predicted for that tick is confirmed and
    /// takes the server's id. A tick not predicted yet (the client is behind after a hard resync) is remembered, and
    /// the shell confirmed as it is predicted.
    /// </summary>
    /// <returns>False when the tick is past and no predicted shell matches (the server's shell is then shown like anyone else's).</returns>
    public bool Confirm(int tick, int shellId)
    {
        if (tick > LastPredictedTick)
        {
            _expectedShots.Add((tick, shellId));
            return true;
        }
        for (int i = 0; i < _shells.Count; i++)
        {
            PredictedShell s = _shells[i];
            if (s.SpawnTick != tick || s.Confirmed) continue;
            s.Confirmed = true;
            s.Shell.Id = shellId;
            _shells[i] = s;
            return true;
        }
        return false;
    }

    /// <summary>The server says shell <paramref name="shellId"/> is done (burst, hit): hide it.</summary>
    public void End(int shellId)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            if (_shells[i].Shell.Id != shellId) continue;
            PredictedShell s = _shells[i];
            s.Ended = true;
            _shells[i] = s;
        }
    }

    /// <summary>Drops shots the server never confirmed once it is well past their tick, and ended shells once stale.</summary>
    public void Prune(int lastProcessedInputTick)
    {
        for (int i = _shells.Count - 1; i >= 0; i--)
        {
            PredictedShell s = _shells[i];
            bool orphan = !s.Confirmed && lastProcessedInputTick > s.SpawnTick + OrphanShellTicks;
            bool done = s.Ended && s.Confirmed;
            if (orphan || done) _shells.RemoveAt(i);
        }
        for (int i = _expectedShots.Count - 1; i >= 0; i--)
            if (_expectedShots[i].Tick <= LastPredictedTick) _expectedShots.RemoveAt(i);   // predicted since, and no shot came of it
    }

    private void StepShells(Battlefield field)
    {
        for (int i = 0; i < _shells.Count; i++)
        {
            PredictedShell s = _shells[i];
            if (s.Ended) continue;
            s.Previous = s.Shell.Position;
            ShellStep step = Ballistics.Step(ref s.Shell, field, out _);
            if (step != ShellStep.Flying) s.Ended = true;   // struck the ground, a rock or a wall here; the server's burst event follows
            _shells[i] = s;
        }
    }
}
```

修改 `src/Tank.Net/Battle/BattleServer.Match.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Battle/BattleServer.Match.cs b/src/Tank.Net/Battle/BattleServer.Match.cs
index ff7c6e4..d047b25 100644
--- a/src/Tank.Net/Battle/BattleServer.Match.cs
+++ b/src/Tank.Net/Battle/BattleServer.Match.cs
@@ -43,7 +43,7 @@ public sealed partial class BattleServer
         _map = MapData.Of(SandboxMap.Create());
         _sim = new Simulation(_map.Build());
         _vision = new VisionTracker(_sim);
-        _rules = new MatchRules(_mode, _sim, SandboxMap.Spawns);
+        _rules = new MatchRules(_mode, _sim, SandboxMap.Spawns, _options.MatchSeconds is { } seconds ? (int)(seconds * SimConstants.TickRate) : null);
         _matchNumber++;
         _endedAtFrame = -1;
         foreach (BattleSeat? seat in _seats)
PATCH
```

修改 `src/Tank.Net/Battle/BattleServerOptions.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Battle/BattleServerOptions.cs b/src/Tank.Net/Battle/BattleServerOptions.cs
index fbcf798..2873de8 100644
--- a/src/Tank.Net/Battle/BattleServerOptions.cs
+++ b/src/Tank.Net/Battle/BattleServerOptions.cs
@@ -23,4 +23,7 @@ public sealed record BattleServerOptions
 
     /// <summary>Fixed seed for reproducible matches (tests, bots' choices); null picks a random one per match.</summary>
     public uint? Seed { get; init; }
+
+    /// <summary>A match length other than the mode's (tests); null for the mode's own.</summary>
+    public double? MatchSeconds { get; init; }
 }
PATCH
```

新建 `src/Tank.Net/Battle/BattleSnapshotStore.cs`：

```csharp
namespace Tank.Net.Battle;

/// <summary>Ring of recently received snapshots, used as delta baselines and for interpolation.</summary>
public sealed class BattleSnapshotStore
{
    public const int Size = BattleSeat.HistorySize;

    private readonly BattleSnapshot[] _ring = new BattleSnapshot[Size];

    public BattleSnapshotStore()
    {
        for (int i = 0; i < Size; i++) _ring[i] = new BattleSnapshot();
    }

    public int NewestTick { get; private set; } = -1;

    public BattleSnapshot? Newest => Find(NewestTick);

    public void Clear()
    {
        foreach (BattleSnapshot s in _ring) s.Tick = -1;
        NewestTick = -1;
    }

    public BattleSnapshot? Find(int tick) => tick >= 0 && _ring[tick % Size].Tick == tick ? _ring[tick % Size] : null;

    /// <summary>Stores a copy. Returns false for packets too old to be useful.</summary>
    public bool Add(BattleSnapshot decoded)
    {
        if (NewestTick >= 0 && decoded.Tick <= NewestTick - Size) return false;
        _ring[decoded.Tick % Size].CopyFrom(decoded);
        if (decoded.Tick > NewestTick) NewestTick = decoded.Tick;
        return true;
    }

    /// <summary>Newest stored snapshot with tick &lt;= <paramref name="tick"/>.</summary>
    public BattleSnapshot? FindAtOrBefore(int tick)
    {
        for (int t = System.Math.Min(tick, NewestTick); t > NewestTick - Size && t >= 0; t--)
        {
            BattleSnapshot? s = Find(t);
            if (s is not null) return s;
        }
        return null;
    }

    /// <summary>Oldest stored snapshot with tick &gt; <paramref name="tick"/>.</summary>
    public BattleSnapshot? FindAfter(int tick)
    {
        for (int t = System.Math.Max(tick + 1, NewestTick - Size + 1); t <= NewestTick; t++)
        {
            BattleSnapshot? s = Find(t);
            if (s is not null) return s;
        }
        return null;
    }
}
```

修改 `src/Tank.Net/Client/ClockSync.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Net/Client/ClockSync.cs b/src/Tank.Net/Client/ClockSync.cs
index 0920e68..4113ccb 100644
--- a/src/Tank.Net/Client/ClockSync.cs
+++ b/src/Tank.Net/Client/ClockSync.cs
@@ -13,13 +13,17 @@ public sealed class ClockSync
     private const double DeadZone = 0.75;
     private const double Gain = 0.02;
     private const double MaxDilation = 0.05;
-    private const int HardResyncThreshold = 20;
-    private const int MaxTicksPerFrame = 8;
+    private const int HardResyncThreshold = 20;   // ticks: 333 ms at 60 Hz, 667 ms at 30 Hz — a stall this long is a reconnect-sized gap either way
+    private const int MaxTicksPerFrame = 8;       // ticks: a long frame catches up this much at once, the rest is dropped
 
+    private readonly int _tickRate;
     private double _accumulator;
     private double _slackEma;
     private bool _hasSlack;
 
+    /// <param name="tickRate">The server's ticks per second: the old game's 60, a battle's 30.</param>
+    public ClockSync(int tickRate = Units.TickRate) => _tickRate = tickRate;
+
     public bool Synced { get; private set; }
     public int Tick { get; private set; }
     public double Rate { get; private set; } = 1.0;
@@ -41,7 +45,7 @@ public sealed class ClockSync
     public void Initialize(int serverTick, double rttSeconds)
     {
         // The snapshot is already RTT/2 old on arrival and our input needs another RTT/2 to get back: lead by a full RTT.
-        Tick = serverTick + (int)System.Math.Ceiling(rttSeconds * Units.TickRate + TargetSlackTicks);
+        Tick = serverTick + (int)System.Math.Ceiling(rttSeconds * _tickRate + TargetSlackTicks);
         _accumulator = 0;
         Synced = true;
     }
@@ -71,7 +75,7 @@ public sealed class ClockSync
     public int Advance(double dt)
     {
         if (!Synced) return 0;
-        _accumulator += dt * Units.TickRate * Rate;
+        _accumulator += dt * _tickRate * Rate;
         int n = (int)_accumulator;
         _accumulator -= n;
         if (n > MaxTicksPerFrame)
PATCH
```

修改 `src/Tank.Sim/Modes/MatchRules.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Sim/Modes/MatchRules.cs b/src/Tank.Sim/Modes/MatchRules.cs
index a8c3904..1f8ed6d 100644
--- a/src/Tank.Sim/Modes/MatchRules.cs
+++ b/src/Tank.Sim/Modes/MatchRules.cs
@@ -37,11 +37,14 @@ public sealed class MatchRules
     private readonly Standing[] _players = new Standing[ModeRules.MaxPlayers];
     private readonly byte[] _rankScratch = new byte[ModeRules.MaxPlayers];
     private readonly int[] _teamScore = new int[2];   // kills by team, kept when a player leaves
+    private readonly int _startTick;
 
-    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns)
+    /// <param name="durationTicks">A match length other than the mode's (tests); null for the mode's own.</param>
+    public MatchRules(BattleMode mode, Simulation sim, SpawnLayout spawns, int? durationTicks = null)
     {
         (Mode, _sim, _spawns) = (mode, sim, spawns);
-        EndTick = sim.Tick + ModeRules.DurationTicks(mode);
+        _startTick = sim.Tick;
+        EndTick = sim.Tick + (durationTicks ?? ModeRules.DurationTicks(mode));
         for (int i = 0; i < _players.Length; i++) _players[i] = new Standing { RespawnAt = -1, EliminatedAt = -1 };
     }
 
@@ -65,7 +68,7 @@ public sealed class MatchRules
     public ref readonly Standing Of(byte slot) => ref _players[slot];
 
     /// <summary>Whether another player may be put into the match now.</summary>
-    public bool AcceptsJoins => Phase == MatchPhase.Playing && (_sim.Tick == EndTick - ModeRules.DurationTicks(Mode) || ModeRules.JoinsMidMatch(Mode));
+    public bool AcceptsJoins => Phase == MatchPhase.Playing && (_sim.Tick == _startTick || ModeRules.JoinsMidMatch(Mode));
 
     /// <summary>A team's kills, including those of players who have since left (two-team modes).</summary>
     public int TeamScore(byte team) => team < _teamScore.Length ? _teamScore[team] : 0;
PATCH
```


- [ ] **Step 4：运行，确认通过**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 622、Net 80。集成测试跑在模拟网络上，整个项目几秒钟。

- [ ] **Step 5：提交**

```bash
git add src/Tank.Net src/Tank.Sim tests/Tank.Net.Tests
git commit -m "feat(net): the battle client — prediction and reconciliation on the shared TankStep, interpolation of the others, own and remote shells, prop state from the server"
```

---

### Task 8：服务器入口和 soak

设计 §6。`Tank.Server/Program.cs`：`--mode teambattle|annihilation|ffa` 跑 `BattleServer`（30 Hz、`BattleProtocol.ConnectKey`、局域网发现回复），`--bots N`；旧模式照旧跑 `GameServer`，4C 再删。`Tank.Soak --battle`：一台服务器、4 个脚本客户端和 4 个机器人，在 4 种链路（有线、WiFi 30±20 ms 5% 丢包、移动网络 80±30 ms 2% 丢包、糟糕 60±40 ms 20% 丢包）上各跑 60 秒，打印每分钟修正次数、平均 / 最大修正距离、上下行 KB/s、快照字节、插值延迟、tick p50/p99、击杀、掉线；通过条件：没人掉线、下行 < 20 KB/s、最大修正不超过场景上限、tick p99 在预算内。

**Files:**
- Create: `tests/Tank.Soak/BattleSoak.cs`
- Modify: `src/Tank.Server/Program.cs`、`tests/Tank.Soak/Program.cs`

- [ ] **Step 1：实现**

修改 `src/Tank.Server/Program.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/src/Tank.Server/Program.cs b/src/Tank.Server/Program.cs
index d409c5e..d0f3104 100644
--- a/src/Tank.Server/Program.cs
+++ b/src/Tank.Server/Program.cs
@@ -1,19 +1,25 @@
 using System.Runtime.InteropServices;
 using Tank.Core.World;
+using Tank.Net.Battle;
 using Tank.Net.Server;
 using Tank.Net.Transport;
+using Tank.Sim;
+using Tank.Sim.Modes;
 
 namespace Tank.Server;
 
 /// <summary>
-/// Headless dedicated server.
-/// Usage: Tank.Server [--port 27015] [--mode coop|basebattle|deathmatch] [--map coop-01] [--name "Room"] [--stage 1]
+/// Headless dedicated server. A battle mode (stage 4) runs a <see cref="BattleServer"/> at 30 Hz; the old modes
+/// still run the old <see cref="GameServer"/> until stage 4C removes them.
+/// Usage: Tank.Server [--port 27015] [--mode teambattle|annihilation|ffa|coop|basebattle|deathmatch] [--bots N] [--name "Room"] [--map coop-01] [--stage 1]
 /// </summary>
 public static class Program
 {
+    private const string Usage = "usage: Tank.Server [--port N] [--mode teambattle|annihilation|ffa|coop|basebattle|deathmatch] [--bots N] [--name ROOM] [--map NAME] [--stage N]";
+
     public static int Main(string[] args)
     {
-        ServerOptions options;
+        Options options;
         try
         {
             options = ParseArgs(args);
@@ -21,69 +27,111 @@ public static class Program
         catch (ArgumentException ex)
         {
             Console.Error.WriteLine($"error: {ex.Message}");
-            Console.Error.WriteLine("usage: Tank.Server [--port N] [--mode coop|basebattle|deathmatch] [--map NAME] [--name ROOM] [--stage N]");
-            Console.Error.WriteLine($"maps: {string.Join(", ", MapLibrary.Names)}");
+            Console.Error.WriteLine(Usage);
+            Console.Error.WriteLine($"old maps: {string.Join(", ", MapLibrary.Names)}");
             return 2;
         }
 
         void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
+        return options.Battle is { } battle ? RunBattle(battle, Log) : RunOld(options.Old, Log);
+    }
+
+    private static int RunBattle(BattleServerOptions options, Action<string> log)
+    {
+        BattleServer? server = null;
+        using var transport = new LiteNetServerTransport(options.Port, null, BattleProtocol.MaxPlayers, BattleProtocol.ConnectKey, w => server!.WriteRoomInfo(w));
+        server = new BattleServer(transport, options, log);
+        if (!StartTransport(transport)) return 1;
+
+        using var runner = new ServerRunner(server, log, SimConstants.TickRate);
+        runner.Start();
+        log($"'{options.RoomName}' listening on UDP {options.Port}: {ModeRules.Id(options.Mode)}, {options.Bots} bots. Ctrl+C to stop.");
+        WaitForStop(() => log($"players {server.ConnectedCount}, {(server.MatchRunning ? "in a match" : "in the room")}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms"));
+        log("shutting down");
+        return 0;
+    }
 
+    private static int RunOld(ServerOptions options, Action<string> log)
+    {
         GameServer? server = null;
         using var transport = new LiteNetServerTransport(options.Port, new RoomInfoRelay(() => server!), options.MaxPlayers);
-        server = new GameServer(transport, options, Log);
+        server = new GameServer(transport, options, log);
+        if (!StartTransport(transport)) return 1;
+
+        using var runner = new ServerRunner(server, log);
+        runner.Start();
+        log($"'{options.RoomName}' listening on UDP {options.Port}: {server.Mode} on {server.MapName}. Ctrl+C to stop.");
+        WaitForStop(() => log($"players {server.ConnectedCount}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms"));
+        log("shutting down");
+        return 0;
+    }
+
+    private static bool StartTransport(LiteNetServerTransport transport)
+    {
         try
         {
             transport.Start();
+            return true;
         }
         catch (InvalidOperationException ex)
         {
             Console.Error.WriteLine($"error: {ex.Message}");
-            return 1;
+            return false;
         }
+    }
 
-        using var runner = new ServerRunner(server, Log);
-        runner.Start();
-        Log($"'{options.RoomName}' listening on UDP {options.Port}: {server.Mode} on {server.MapName}. Ctrl+C to stop.");
-
+    /// <summary>Blocks until Ctrl+C in a terminal or SIGTERM from a service manager, reporting every 30 s.</summary>
+    private static void WaitForStop(Action report)
+    {
         using var stop = new ManualResetEventSlim();
-        // Ctrl+C in a terminal, SIGTERM from a service manager: both shut down cleanly.
         using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, ctx => { ctx.Cancel = true; stop.Set(); });
         using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx => { ctx.Cancel = true; stop.Set(); });
-        while (!stop.Wait(TimeSpan.FromSeconds(30)))
-            Log($"players {server.ConnectedCount}, tick avg {runner.AverageTickMs:F3} ms, max {runner.MaxTickMs:F3} ms");
-
-        Log("shutting down");
-        return 0;
+        while (!stop.Wait(TimeSpan.FromSeconds(30))) report();
     }
 
-    private static ServerOptions ParseArgs(string[] args)
+    /// <summary>Either a battle (stage 4) or an old game; the common options are kept in both.</summary>
+    private sealed record Options(ServerOptions Old, BattleServerOptions? Battle);
+
+    private static Options ParseArgs(string[] args)
     {
-        var options = new ServerOptions();
+        var old = new ServerOptions();
+        var battle = new BattleServerOptions();
+        bool isBattle = true;
         for (int i = 0; i < args.Length; i++)
         {
             string Value() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
             switch (args[i])
             {
                 case "--port":
-                    options = options with { Port = int.TryParse(Value(), out int p) && p is > 0 and < 65536 ? p : throw new ArgumentException("bad port") };
+                    int port = int.TryParse(Value(), out int p) && p is > 0 and < 65536 ? p : throw new ArgumentException("bad port");
+                    old = old with { Port = port };
+                    battle = battle with { Port = port };
                     break;
                 case "--mode":
-                    options = options with { Mode = Enum.TryParse(Value(), true, out GameMode m) && Enum.IsDefined(m) ? m : throw new ArgumentException("bad mode") };
+                    string mode = Value();
+                    if (ModeRules.TryParse(mode, out BattleMode bm)) battle = battle with { Mode = bm };
+                    else if (Enum.TryParse(mode, true, out GameMode gm) && Enum.IsDefined(gm)) { old = old with { Mode = gm }; isBattle = false; }
+                    else throw new ArgumentException($"unknown mode '{mode}'");
+                    break;
+                case "--bots":
+                    battle = battle with { Bots = int.TryParse(Value(), out int bots) && bots >= 0 && bots < BattleProtocol.MaxPlayers ? bots : throw new ArgumentException($"bots: 0 to {BattleProtocol.MaxPlayers - 1}") };
                     break;
                 case "--map":
                     string map = Value();
-                    options = options with { MapName = MapLibrary.Names.Contains(map) ? map : throw new ArgumentException($"unknown map '{map}'") };
+                    old = old with { MapName = MapLibrary.Names.Contains(map) ? map : throw new ArgumentException($"unknown map '{map}'") };
                     break;
                 case "--name":
-                    options = options with { RoomName = Value() };
+                    string name = Value();
+                    old = old with { RoomName = name };
+                    battle = battle with { RoomName = name };
                     break;
                 case "--stage":
-                    options = options with { Stage = int.TryParse(Value(), out int s) && s > 0 ? s : throw new ArgumentException("bad stage") };
+                    old = old with { Stage = int.TryParse(Value(), out int s) && s > 0 ? s : throw new ArgumentException("bad stage") };
                     break;
                 default:
                     throw new ArgumentException($"unknown option '{args[i]}'");
             }
         }
-        return options;
+        return new Options(old, isBattle ? battle : null);
     }
 }
PATCH
```

新建 `tests/Tank.Soak/BattleSoak.cs`：

```csharp
using System.Diagnostics;
using Tank.Net.Battle;
using Tank.Net.Transport;
using Tank.Sim;
using Tank.Sim.Modes;

namespace Tank.Soak;

/// <summary>
/// Battle soak (stage 4 design §6): one battle server, 4 scripted clients and 4 bots on a deterministic simulated
/// network under several link qualities, a minute each. Prints the design's acceptance numbers — downstream under
/// 20 KB/s, corrections small, nobody dropped, the server tick within budget — and returns false if any is violated.
/// </summary>
public static class BattleSoak
{
    private sealed record Scenario(string Name, LinkConditions Link, float MaxCorrectionM);

    private const int Clients = 4;
    private const int Bots = 4;
    private const double MatchSeconds = 60;
    private const double DownBudgetKBps = 20;
    private const double FrameDt = 1.0 / 144;
    private const double ServerDt = 1.0 / SimConstants.TickRate;
    /// <summary>Tick timings from the first seconds are dominated by JIT/tiered compilation, not by the game.</summary>
    private const double TimingWarmupSeconds = 2.5;

    public static bool Run(double tickBudgetMs)
    {
        var scenarios = new[]
        {
            new Scenario("wired LAN", new LinkConditions(0.5, 0.2, 0), 0.6f),
            new Scenario("wifi 30±20ms 5% loss", new LinkConditions(30, 20, 5), 2f),
            new Scenario("mobile 80±30ms 2% loss", new LinkConditions(80, 30, 2), 2f),
            new Scenario("awful 60±40ms 20% loss", new LinkConditions(60, 40, 20), float.MaxValue),
        };

        Run(scenarios[0] with { Name = "(warm-up)" }, tickBudgetMs, seconds: 8, report: false);

        Console.WriteLine($"battle: {Clients} clients + {Bots} bots, team battle, {MatchSeconds:F0} s each; down < {DownBudgetKBps} KB/s, tick p99 < {tickBudgetMs} ms");
        Console.WriteLine($"{"scenario",-26}{"corr/min",10}{"mean m",8}{"max m",8}{"down KB/s",10}{"up KB/s",9}{"snap B",8}{"interp ms",10}{"tick p50/p99 ms",17}{"kills",7}{"drops",7}  result");
        bool ok = true;
        foreach (Scenario s in scenarios) ok &= Run(s, tickBudgetMs);
        return ok;
    }

    private static bool Run(Scenario scenario, double tickBudgetMs, double seconds = MatchSeconds, bool report = true)
    {
        var net = new LoopbackNetwork(seed: 11);
        var server = new BattleServer(net.CreateServer(), new BattleServerOptions { Seed = 5, Mode = BattleMode.TeamBattle, Bots = Bots });
        var clients = Enumerable.Range(0, Clients)
            .Select(i => new BattleClient(net.Connect(scenario.Link, scenario.Link), $"player{i}", () => net.Now))
            .ToList();

        var tickTimes = new List<double>();
        double serverAccumulator = 0;
        bool started = false;
        int frames = (int)((seconds + 1) / FrameDt);
        for (int f = 0; f < frames; f++)
        {
            net.AdvanceTime(FrameDt);
            serverAccumulator += FrameDt;
            while (serverAccumulator >= ServerDt - 1e-9)
            {
                long t0 = Stopwatch.GetTimestamp();
                server.Tick();
                if (started && net.Now > 1 + TimingWarmupSeconds) tickTimes.Add(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                serverAccumulator -= ServerDt;
            }
            for (int i = 0; i < clients.Count; i++) clients[i].Update(FrameDt, Drive(i, net.Now));

            // The host starts once everyone is in the room; a team battle lasts longer than the soak, so no restarts.
            if (!started && net.Now > 0.5 && f % 30 == 0 && clients.All(c => c.Phase == BattlePhase.Room))
            {
                clients.Single(c => c.IsHost).StartMatch();
                started = true;
            }
        }

        int corrections = clients.Sum(c => c.Stats.Corrections);
        double perMinute = corrections / (double)Clients / (seconds / 60);
        double mean = clients.Average(c => c.Stats.MeanCorrection);
        float max = clients.Max(c => c.Stats.MaxCorrection);
        double down = clients.Average(c => c.Stats.BytesInPerSecond) / 1024;
        double up = clients.Average(c => c.Stats.BytesOutPerSecond) / 1024;
        int snapBytes = (int)clients.Average(c => c.Stats.SnapshotBytes);
        double interp = clients.Average(c => c.Stats.InterpDelayMs);
        tickTimes.Sort();
        double p99 = tickTimes.Count == 0 ? 0 : tickTimes[(int)(tickTimes.Count * 0.99)];
        double p50 = tickTimes.Count == 0 ? 0 : tickTimes[tickTimes.Count / 2];
        int dropped = clients.Count(c => c.Phase != BattlePhase.InMatch);
        int kills = server.Rules is { } rules ? Enumerable.Range(0, BattleProtocol.MaxPlayers).Sum(slot => rules.Of((byte)slot).Kills) : 0;

        if (!report) return true;
        bool pass = started && server.MatchRunning && dropped == 0 && max <= scenario.MaxCorrectionM && down < DownBudgetKBps && p99 < tickBudgetMs;
        Console.WriteLine($"{scenario.Name,-26}{perMinute,10:F1}{mean,8:F3}{max,8:F2}{down,10:F1}{up,9:F1}{snapBytes,8}{interp,10:F1}{$"{p50:F3}/{p99:F3}",17}{kills,7}{dropped,7}  {(pass ? "PASS" : "FAIL")}");
        return pass;
    }

    /// <summary>Drive about, turn often, swing the turret and shoot a lot: a worst-case-ish human.</summary>
    private static LocalCommand Drive(int client, double t)
    {
        int phase = (int)(t / (0.6 + client * 0.1)) + client * 3;
        sbyte throttle = (sbyte)((phase % 4) switch { 0 => 1, 1 => 1, 2 => -1, _ => 0 });
        sbyte steer = (sbyte)((phase % 3) switch { 0 => 1, 1 => -1, _ => 0 });
        bool fire = ((int)(t * 8) + client) % 7 == 0;
        return new LocalCommand(throttle, steer, (float)(t * 0.5 + client), 0.02f, fire);   // one round type: a swap restarts the reload
    }
}
```

修改 `tests/Tank.Soak/Program.cs`（在仓库根目录运行）：

```bash
git apply <<'PATCH'
diff --git a/tests/Tank.Soak/Program.cs b/tests/Tank.Soak/Program.cs
index 8f213f4..e52b6f8 100644
--- a/tests/Tank.Soak/Program.cs
+++ b/tests/Tank.Soak/Program.cs
@@ -10,6 +10,7 @@ namespace Tank.Soak;
 /// <summary>
 /// Network soak test (docs/04 §4): 8 bots on a deterministic simulated network under several link qualities.
 /// Prints the acceptance metrics from docs/03 §11 and exits non-zero if any are violated.
+/// <c>--battle</c> runs the stage 4 battle soak (<see cref="BattleSoak"/>) instead.
 /// </summary>
 public static class Program
 {
@@ -28,8 +29,9 @@ public static class Program
         double.TryParse(Environment.GetEnvironmentVariable("SOAK_TICK_BUDGET_MS"), System.Globalization.CultureInfo.InvariantCulture, out double b) ? b : 2.0;
     private const double FrameDt = 1.0 / 144;
 
-    public static int Main()
+    public static int Main(string[] args)
     {
+        if (args.Contains("--battle")) return BattleSoak.Run(TickBudgetMs) ? 0 : 1;
         var scenarios = new[]
         {
             new Scenario("wired LAN", new LinkConditions(0.5, 0.2, 0), GameMode.Deathmatch, 10),
PATCH
```


- [ ] **Step 2：构建、运行**

```bash
dotnet build src/Tank.Server/Tank.Server.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
dotnet build tests/Tank.Soak/Tank.Soak.csproj 2>&1 | grep -E "Warning\(s\)|Error\(s\)"
dotnet run --project tests/Tank.Soak -c Release -- --battle
```

Expected: 两个项目 0 警告 0 错误；soak 打印 4 行，每行结尾 `PASS`，进程退出码 0（约 5 分钟）。实测下行 1.8～2.3 KB/s，平均修正 ≤ 0.1 m，最大 ≤ 1.1 m（糟糕链路）。

然后冒烟一下服务器入口（后台起、3 秒后 SIGTERM；后台子 shell 里的进程会忽略 SIGINT，所以用 TERM）：

```bash
(dotnet src/Tank.Server/bin/Debug/net8.0/Tank.Server.dll --mode ffa --bots 3 --port 27999 --name smoke > build/smoke.log 2>&1 & echo $! > build/smoke.pid)
sleep 3; kill -TERM $(cat build/smoke.pid); sleep 2; cat build/smoke.log
dotnet src/Tank.Server/bin/Debug/net8.0/Tank.Server.dll --mode nope; echo "exit $?"
```

Expected: 日志两行：`'smoke' listening on UDP 27999: ffa, 3 bots. Ctrl+C to stop.` 和 `shutting down`；坏模式打印 `error: unknown mode 'nope'` 和用法，退出码 2。

- [ ] **Step 3：全部测试**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`
Expected: 全部通过，Sim 622、Net 80，Core 62、Update 42、Art 512。

- [ ] **Step 4：提交**

```bash
git add src/Tank.Server tests/Tank.Soak
git commit -m "feat(server): battle modes on the dedicated server (--mode teambattle|annihilation|ffa, --bots N) and a battle soak (Tank.Soak --battle) over four link qualities"
```

---

### Task 9：验收与记录

- [ ] **Step 1：全部测试和构建**

Run: `dotnet test Tank.sln 2>&1 | grep -E "通过!|失败!"`；`for p in src/Tank.Client/Tank.Client.csproj src/Tank.Server/Tank.Server.csproj tests/Tank.Soak/Tank.Soak.csproj; do dotnet build $p 2>&1 | grep -E "Warning\(s\)|Error\(s\)"; done`
Expected: 全部通过，Sim 622、Net 80，Core 62、Update 42、Art 512；三个项目 0 警告 0 错误。

- [ ] **Step 2：soak**

Run: `dotnet run --project tests/Tank.Soak -c Release -- --battle`
Expected: 4 行 `PASS`，退出码 0。把 4 行数字记下来。

- [ ] **Step 3：记录**

在 `docs/PROGRESS.md` 的“4A 节点记录”里写上验收结果（测试数、soak 的 4 行），“当前位置”改为“4A 验收完成，待并入 main；下一步 4B”。提交：

```bash
git add docs/PROGRESS.md
git commit -m "docs: progress — stage 4A accepted"
```

---

### Task 10：并入 main

- [ ] **Step 1：squash merge**

在主仓库 `/Users/murphy/Desktop/dev/BattleCity`（分支 `main`）：

```bash
git merge --squash feat/stage4a-netcore
git commit -m "feat: stage 4A networking core — ..."   # 提交信息列出本阶段的内容
```

- [ ] **Step 2：清理**

删除草稿 worktree 和分支（`BattleCity-stage4a-draft`、`draft/stage4a-netcore`）和计数用的 worktree（`BattleCity-stage4a-counts`）；保留 `feat/stage4a-netcore` 分支和它的 worktree。不推送远程。

---
## 写计划时已核实、与设计的差异

写这份计划时，先在临时 worktree（`/Users/murphy/Desktop/dev/BattleCity-stage4a-draft`，分支 `draft/stage4a-netcore`，从设计提交 `5440a0b` 拉出）里按任务顺序把下文全部实现了一遍，一个任务一个提交。下文的代码和补丁由脚本从这些提交直接导出，没有手抄。实测结果：

- 每个提交单独检出后：客户端、服务器、soak 三个项目编译 0 警告 0 错误，Sim 和 Net 测试全过。Sim 测试数依次是 591、598、614、622、622、622、622、622（Task 1～8），Net 依次是 50、50、50、50、61、67、80、80；Core 62、Update 42、Art 512 不变。
- soak（`Tank.Soak --battle`，4 个脚本客户端 + 4 个机器人，团队战，每种链路 60 秒）四种链路全 PASS：下行 1.7～2.3 KB/s（预算 20），上行 0.7 KB/s，快照 57～98 字节，插值延迟 100 ms，服务器 tick p99 0.2～0.8 ms；修正次数 有线 8.8 / 分钟、WiFi 27.8、移动 3.0、糟糕 3.0，平均修正 0.02～0.12 m，最大 0.13～1.09 m（糟糕链路）。有线和 WiFi 上剩下的修正几乎全是贴着别的车时的接触噪声（客户端用最新快照里厘米级、慢 1～3 tick 的别车位置做碰撞，服务器用精确的 tick 初位置），不是链路问题。
- 服务器每 tick 不分配内存（事件批编码里的接口 foreach 装箱和一处 LINQ 已改掉）；客户端每收到一个事件批分配一次事件数组（约 860 B/s），可以接受。
- 草稿经过两轮并行的代码审查（仿真侧、网络侧），没有 CRITICAL。按审查意见改了（都已并入对应任务的提交）：
  - **队伍得分丢失**：原来按还在场的玩家累加击杀，离队后他的击杀就从本队得分里消失，可能颠倒胜负。改为按队累计（Task 3）；
  - **客户端预测在中速撞砖墙时卡住**：重坦以 15～30 km/h 撞墙，减半后在墙里低于撞墙速度，墙在客户端还立着就把车顶回去、停住，服务器那边早已穿过。现在不撞倒物件时，车已经在里面的墙照样让路（Task 2，补了 13 m 助跑的用例）；
  - **复活位置泄露**：残骸人人可见，复活到别处后视野表里还记着“刚看见”，敌方快照里会出现它的新位置 2 秒。现在残骸复活到别处就清掉它的行；原地复活（试驾场）仍保持可见（Task 1）；
  - **`Protected` 标志永远为真**：快照里的受保护标志原来只看 `ProtectedUntil > 0`，出生过就一直亮。改为按快照 tick 判断（Task 6）；
  - **复活算成预测修正**：复活瞬移 250 m 被记成一次 250 m 的修正，soak 的门槛靠运气。改为残骸变活车时直接接受服务器状态、不计；另外 2 cm 以下（线上量化是 1 cm）的差异不计入修正次数（Task 7）；
  - **看不见的射手车号泄露**：原来只在 `ShellFired` 上抹掉看不见的射手，`ShellExploded`、物件事件、命中事件照带。现在所有事件只在射手是自己或本队看得见时才带车号；集成测试改为朝地面开炮，连爆炸事件一起验（Task 6、7）；
  - **机器人**：原地转向超过 3 秒被当成卡住、放弃路点（重坦只能去前方 78° 以内的路点），现在转了 20° 以上不算卡住；穿甲弹打光后站在空地上“交战”到局终，现在没弹就继续巡逻；250 m 的测试原来打的是看不见的车，没测到距离，改为队友帮忙点亮（Task 4）；
  - **受保护的车挨溅射**：设计说“照常结算、伤害记 0”，草稿直接跳过；改为发 `SplashDamage` 事件、伤害 0（Task 1）；
  - 大乱斗平局（同血、同 tick 被淘汰）按击杀数分先后；出生点上有残骸也算占用；`PickSpawn` 的接口 foreach 改下标；一处恒真的断言、一处死逻辑（Task 3、2）；
  - 客户端解不开快照时发 `RequestMatchState`（设计 §5.2 列了，草稿没发），每秒至多一次；插值延迟下限 1.5 tick 改为 3 tick（100 ms，设计 §5.1）；在局外等待的座位可以改队伍和车型；自己的开炮事件比预测先到时不再画两发；输入缓冲不把已消费 tick 的冗余副本算成“迟到”（Task 7、6）。

与设计文字不同、但有意为之的地方（执行时不要“改回去”，设计文档已同步）：

- **两队出生点中心相距 283 m**（设计 ~250 m）：试驾场上能放下 5 个 6 m 净空、互相朝向的点的两片平地在 (100, 300) 和 (300, 100) 附近。两辆中坦静止时相距 206 m 才能互见，283 m 保证开局互相看不见。
- **出生点挑选**：先挑 8 m 内没有车（含残骸）的点，都占了挑最空的（设计写“轮流”）。
- **物件状态的接口**是 `Battlefield.SetDurability(id, 耐久)`，立着 ⇔ 耐久 > 0（设计写 `SetPropState(id, 立着, 耐久)`），等价。
- **移除坦克**把后面的槽位前移、编号不复用（设计没写死）；`VisionTracker` 的行跟着编号走。
- **机器人**只打穿甲弹、没弹就不交战；卡住判定看位移和转向。
- **修正统计**只计 2 cm 以上。
- **开局消息不带名单**（设计 §5.2 写了“名单”）：名单由房间状态（座位→名字、队伍、车型）加快照里的计分块（座位→车号）拼出，没有信息丢失。
- **计分块列出每个座位的车号和存活状态**，包括看不见的敌人（设计 §5.5 说看不见的敌人“车号、位置都不出现”）：Tab 计分板要显示所有人是否活着；位置、朝向仍只给看得见的车。

## 留给以后的问题（不在本阶段处理）

- 客户端的本车碰撞用的是最新快照里别车的位置（厘米级、慢 1～3 tick），贴车时每 tick 有毫米级修正；要消掉得在快照里带别车的精确位置或在服务器上也用量化位置。
- `MinPlayers` / “每队至少 1 人”只在服务器开局时检查，`MatchRules` 自己不管（一边没人时第一步就会判完）。
- 大乱斗 10 人各一队，`VisionTracker` 的表按 16 队分配；再多就要改 `MaxTeams`。
- 客户端每个事件批分配一次数组；要零分配可以池化。
- `RequestMatchState` 的触发路径没有自动测试（要构造解不开的快照）。
- 掉线 30 秒内重连恢复座位已实现，但客户端的重连（换 transport 再 hello 带令牌）要在 4B 的界面里接。
- `LiteNetServerTransport` 以 `maxConnections = MaxPlayers` 创建，所以真实的第 11 个连接在“房间已满”的拒绝发出前就被 LiteNet 拒掉；满员房间里的重连顶替要等旧连接超时。以后可考虑 `MaxPlayers + 2`。

---
