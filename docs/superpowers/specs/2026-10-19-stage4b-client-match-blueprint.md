# 阶段 4B 客户端对局 · 实现蓝图

> 由架构子代理按设计 §7 和 main 44bfda6 的代码写成；只读分析，未实测截图和帧率。

约定：路径相对 `/Users/murphy/Desktop/dev/BattleCity`，行号取自 HEAD 44bfda6。本文只读代码得出，没有改任何文件，也没有跑 Godot 或 dotnet，所以截图项、帧率和 `--fixed-fps` 是否可用都没实测。

## 0. 十条关键决定

1. **对局场景 `MatchMain` 是挂在 `Main` 下的 `Node3D` 子节点，不是独立场景。** 会话（`BattleClient`、房主服务器线程）要跨 菜单、房间、对局、结算 存活，切场景会把它们销毁。进入对局时创建，`client.Phase` 离开 `InMatch` 时 `QueueFree`。
2. **沙盒与对局共享部件，不共享基类。** 两边的数据源不同：沙盒读 `Simulation`，对局读 `BattleClient`。共享的有 世界、坦克演员、炮弹、特效、镜头、瞄准、输入、准星/状态面板/血条/命中字。对局代码不引用 `Simulation`、`VisionTracker`、`TargetRange`；对 `SandboxMap` 只经一个 `MapStyle` 取草地岛和种子。
3. **房主服务器放独立线程（`ServerRunner`），不放 Godot 主线程。** 理由见 1.3。确定性冒烟用的回环主机才在主线程推进。
4. **`BattleClient.Update` 每帧调一次，对局期间由 `MatchMain` 独占调用。** 开火点击锁存必须"被一个仿真 tick 吃掉"才清除，否则点击会丢（见风险 R1）。
5. **`scripts/UI/*` 的去留：**
   - `UiKit.cs` 保留，只改默认颜色来源。
   - `MenuScreen.cs` 保留，原地重写。
   - 新增 `RoomScreen.cs`。
   - `Hud.cs`、`LobbyScreen.cs` 不碰，4C 删。
6. **纯逻辑放 `Logic/` 子目录，Art 测试用一条通配符链接。** 这是既有做法（`Art/Mesh/**` 已经这样），`Logic/` 里不许出现 `using Godot`。
7. **`project.godot` 不改。** `scoreboard`（Tab，第 91 行）和 `toggle_netgraph`（F3，第 81 行）已存在；观战键等新动作像 `SandboxInput.RegisterActions`（`SandboxInput.cs:37-52`）那样运行时注册。
8. **新代码只引用 `Tank.Client.Art.Palette`，不引用 `Tank.Client.View.Palette`。** 后者 4C 会并入前者。
9. **新 HUD 一律用 1600×900 基准单位。** `project.godot:15-19` 是 `canvas_items` + `expand`，Control 的 Size 就是 1600×900；1920×1080 窗口只是整体 ×1.2。下文的 1080p 栏已按 ×1.2 换算。
10. **确定性冒烟 = `--loopback`（虚拟网络、主线程、固定步长）+ `--seed` + `--autodrive`。** 同一命令每次得到同一场比赛，不需要第二个进程，也不需要 UDP 端口。

---

## 1. 架构

### 1.1 组件与依赖

```
Main (Node, 场景 Main.tscn)
 ├ MenuScreen / RoomScreen (Control, UiKit 风格)
 ├ BattleSession ── BattleClient (Tank.Net.Battle)
 │     └ 主机后端: ThreadedHost (BattleServer + LiteNetServerTransport + ServerRunner 线程)
 │              或 LoopbackHost (BattleServer + LoopbackNetwork, 主线程, 测试/截图)
 ├ SandboxMain (Node3D, 点"试驾场"时挂上; Sandbox.tscn 仍可直启)
 └ MatchMain (Node3D, InMatch 期间存在)
     ├ BattleWorld (= SandboxWorld 改名搬家: 天空/地形/岩石/草/墙/树)
     ├ TankActor(自己) + TankActors(别人, 按坦克 id 的字典)
     ├ ShellViews, BattleFx, OrbitCamera
     ├ CombatHud (CanvasLayer 1: 准星/状态面板/命中字/血条+名字/墙血条/狙击暗角)
     └ MatchHud  (CanvasLayer 2: 计时比分/击毁提示/复活观战横幅/计分板/结算/F3 面板)
```

### 1.2 共享抽取：从沙盒拿什么、两边怎么用

| 共享件 | 来源 | 抽取方式 | 沙盒用法 | 对局用法 |
|---|---|---|---|---|
| `BattleWorld` | `Sandbox/SandboxWorld.cs`（它已只依赖 `Battlefield`，没碰 `Simulation`） | `git mv` 到 `World/`，改名；`Build` 增加 `MapStyle`（草地岛 + 种子）；新增 `SetField`；`Update` 的坦克参数改可空 | `MapStyle.Sandbox` | `client.Field`（来自服务器的 `MapData.Build()`） |
| `TankActor` | `TargetRange.Target`（`TargetRange.cs:40-54`）和 `SandboxMain.BuildTank`（`SandboxMain.cs:191-198`）里重复的 Model+WreckSmoke+DustTrail | 新节点，包装 `TankModel` | 靶车和玩家都改用它 | 自己和所有别人 |
| `CombatHud` | `SandboxHud` 的通用部分（`SandboxHud.cs:46-62,69-88,100-122,131-159`） | 抽成基类；靶车血条、点亮调试文字留在 `SandboxHud` | `SandboxHud : CombatHud` | 直接用，数据来自 `BattleClient` |
| `HealthBars` | 现有 | 加可选的名字标签 | 不传标签 | 名字 + 相对阵营色 |
| `ShellViews` | `ShellViews.cs:53` 只收 `ReadOnlySpan<Shell>` | 加 `Sync(IReadOnlyList<ShellView>, alpha, delta)` 重载；`ShellView` 加 `Age`（见下） | 旧重载不变 | `client.Shells` |
| `DriveInput` | `SandboxInput` 的驾驶键、开火锁存、弹种、自由视角（`SandboxInput.cs:16-60,113-122`） | 抽成基类；多出 `Peek()` / `EndTick()`；沙盒的 R、T、F1-F4 留在子类 | `SandboxInput : DriveInput` | `MatchInput : DriveInput` 加观战键 |
| `AimSolver` / `AimMath` | `SandboxMain.UpdateAim`（`SandboxMain.cs:257-274`） | 数学部分纯化，Godot 只负责取相机向量 | 调用它 | 调用它 |
| `RenderMath`、`MatchColors` 等 | 无 | 新增纯逻辑 | 不用 | 用 |

**需要动 `Tank.Net` 的唯一一处：** `ShellView`（`BattleClientTypes.cs:21`）没有炮弹年龄，而 `ShellViews` 用年龄让尾迹从炮口逐渐拉长（`ShellViews.cs:65,73`）。给 record 末尾加 `float Age = 0f`（源码兼容），在 `BattleClient.Match.cs:71,74` 两处传 `s.Shell.Age`。炮弹步进里本来就在加龄（`Ballistics.cs:49`）。加一条 Net 测试。

**对局场景不依赖的东西：** `Simulation`、`VisionTracker`、`TargetRange`、`SandboxMap`（除 `MapStyle`）。要看它依赖谁，只看 `MatchMain` 的 `using`。

### 1.3 进程内主机：线程，不是主线程

**做法：** `BattleSession.Host(...)` 起一个 `LiteNetServerTransport` 加 `BattleServer`，放在 `ServerRunner` 线程里按 30 Hz 跑，再用一个 `BattleClient` 经 `127.0.0.1` 连回来。流程和旧 `Session.Host`（`Bootstrap/Session.cs:26-40`）一样，新栈的写法如下：

```csharp
BattleServer? server = null;
var transport = new LiteNetServerTransport(port, null, BattleProtocol.MaxPlayers,
    BattleProtocol.ConnectKey, w => server!.WriteRoomInfo(w));    // LiteNetServerTransport.cs:24
uint owner = (uint)Random.Shared.Next(1, int.MaxValue);
server = new BattleServer(transport, new BattleServerOptions {
    RoomName = $"{name}的房间", Port = port, Mode = s.Mode, Bots = s.Bots,
    OwnerToken = owner, Seed = s.Seed, MatchSeconds = s.MatchSeconds }, log);
transport.Start();                                                // 端口被占时抛 InvalidOperationException (:57-58)
var runner = new ServerRunner(server, log, SimConstants.TickRate);  // ServerRunner.cs:27, 默认参数是旧的 60 Hz!
runner.Start();
var client = new BattleClient(new LiteNetClientTransport("127.0.0.1", port, BattleProtocol.ConnectKey),
                              name, () => Now, owner);
```

**必须传 `BattleProtocol.ConnectKey`。** `LiteNetClientTransport` 构造函数的默认值是旧协议的 `"tank3d-v1"`（`LiteNetClientTransport.cs:13`），漏传会得到不透明的 "ConnectionFailed"。

**为什么用线程：**
- `ServerRunner` 的睡眠加自旋循环、最多追 5 个 tick（`ServerRunner.cs:54-81`）是 `Tank.Server` 已经验证的节拍。
- 放主线程会让权威仿真受 Godot 帧节奏支配：窗口拖动、着色器编译的 100 ms 卡顿会冻住所有玩家；144 Hz 下 30 Hz 的 tick 会变得不均匀，插值抖动。
- 两个 `NetManager`（服务端、客户端）各只在一个线程里 `PollEvents`：服务端在 runner 线程，客户端在主线程。LiteNet 回调只在 `Poll` 内同步触发（`ITransport.cs:5`），没有跨线程共享的传输状态。
- `BattleServer` 的状态只被 runner 线程读写。UI 想看的服务器数据（F3 面板的服务器 tick 毫秒）只读 `ServerRunner.AverageTickMs/MaxTickMs` 这两个 double。
- 服务器日志 `GD.Print` 可以跨线程调用（旧 `Session` 就这样做）。

**关闭顺序固定：** 先 `client.Dispose()`，再 `runner.Dispose()`（最多等 2 秒，`ServerRunner.cs:46-50`），最后 `transport.Dispose()`。顺序照旧 `Session.Leave`（`Session.cs:48-57`）。运行线程时先销毁传输会和 `PollEvents` 竞争。

**回环主机（仅测试/截图）：** `LoopbackNetwork` + `BattleServer` + `BattleClient`，全在主线程。`BattleSession.Advance(dt)` 先 `net.AdvanceTime(dt)`，再用累加器每 1/30 秒调一次 `server.Tick()`，逻辑与 `tests/Tank.Net.Tests/Battle/BattleHarness.cs:36-52` 相同，时钟是 `() => net.Now`。`LoopbackNetwork.cs:1` 引用了 `Tank.Core.Math.XorShift`，4C 要换成 `Tank.Sim.Geometry.XorShift`（见第 6 节 4C 衔接）。

### 1.4 生命周期（Main 的状态机）

| 状态 | 进入条件 | 画面 | 退出 |
|---|---|---|---|
| 菜单 | 启动；断线；离开房间 | `MenuScreen`，局域网列表由 `LanBrowser` 每帧轮询 | 开房间 / 加入 / 试驾场 |
| 连接中 | `Host`/`Join` 已建会话，`Phase==Connecting` | 菜单底部状态栏 "正在连接…" | Welcome 后 `Phase==Room` |
| 房间 | `Phase==Room` | `RoomScreen`，半透明深色底，不画 3D | `Phase==InMatch` → 对局；`Disconnected` → 菜单 |
| 对局 | `client.Phase==InMatch` | `MatchMain` 创建；鼠标自动捕获（自动化运行除外） | `Phase != InMatch` 触发 `Finished` |
| 结算 | `client.Match.Phase==Ended`（对局内的子状态） | `MatchMain` 里的结算层，倒计时 8 秒 | 服务器 `EndMatch` 广播 `MatchRunning=false` → `HandleRoom` 调 `LeaveMatch`（`BattleClient.cs:169-173`）→ 回房间 |
| 试驾场 | 菜单点按钮 | `SandboxMain` 作为子节点挂上 | 沙盒里第二次按 Esc 触发 `ExitRequested` |

两点说明：
- 结算到房间是服务器驱动的。服务器在结算 8 秒后 `EndMatch`（`BattleServer.Match.cs:126-130`），客户端不用自己切；房主按 Enter 可提前 `ReturnToRoom`。
- 玩家在不能中途加入的模式里晚进房间时，`Phase` 一直是 `Room` 且 `Room.MatchRunning==true`（`BattleServer.Room.cs:41-46`：座位不 `InMatch` 就不发 `MatchStart`）。房间界面要显示"对局进行中，等下一局"，不能让"开始"可点。

沙盒作为子节点挂载而不切场景，是为了保住后台的 `UpdateService`（`Main.Update.cs:18-25` 每次 `_Ready` 都会重新 `Start`）。`Sandbox.tscn` 直启保持不动，给现有截图自动化用。

### 1.5 无头冒烟（不需要第二个进程）

- **默认路径已经满足"没有第二个进程"。** `--host --bots 3 --autostart` 在本进程里起服务线程，用 UDP 127.0.0.1 连回自己。
- **`--loopback` 路径是确定性的：** 加 `--loopback` 后会话用 `LoopbackHost`，没有套接字、没有线程。同时 `MatchMain` 把每帧步长固定为 `1/60`，不用 Godot 的真实 `delta`（`BattleSession.IsVirtualClock` 控制）。再配 `--seed N` 和 `--autodrive`，比赛进程完全确定（粒子和动画仍用真实 delta，所以像素不逐位相同）。
- 备选：`godot-mono --fixed-fps 60` 能让引擎本身也定步长，Godot 4.x 有这个选项，但 4.7 是否保留要先试；本方案不依赖它。
- 截图计时：`--screenshot-at` 对对局来说指"对局开始后"的秒数，用虚拟或真实时钟与模式一致（见第 5 节 T9）。

---

## 2. 文件清单

### 2.1 新建

行数是估算，全部控制在约 300 行以内。**粗体 = 纯逻辑，位于 `Logic/` 下并链接进 Art 测试。**

| 路径 | 职责 | 行数 |
|---|---|---|
| **`scripts/Online/Logic/BattleSession.cs`** | `Host/Join/HostLoopback`、`Advance(dt)`、`Dispose`（固定顺序）、`Now`、`IsVirtualClock`、`ServerTick` | ~120 |
| **`scripts/Online/Logic/ThreadedHost.cs`** | 上面 1.3 的 transport + server + runner，实现 `IHostBackend` | ~90 |
| **`scripts/Online/Logic/LoopbackHost.cs`** | 虚拟网络加累加器，实现 `IHostBackend` | ~90 |
| **`scripts/Online/Logic/HostSettings.cs`** | `record HostSettings(PlayerName, RoomName, Port, Mode, Bots, Seed?, MatchSeconds?)` | ~25 |
| **`scripts/Online/Logic/LanBrowser.cs`** + **`LanRoomRow.cs`** | 包装 `LanDiscovery`，只取 `BattleRooms`；每行格式化为文本和"可加入/不可加入原因"；捕获 `SocketException` | ~70 + ~60 |
| **`scripts/Online/Logic/AutoRoom.cs`** | `--mode/--bots/--tank/--autostart/--players` 的状态机，每步至多发一条房间命令 | ~90 |
| `scripts/UI/RoomScreen.cs` | 房间界面（Godot Control） | ~250 |
| **`scripts/UI/Logic/RoomRules.cs`** | `StartBlockReason(room)`：镜像 `TryStartMatch` 的开局检查 | ~70 |
| **`scripts/UI/Logic/RoomModel.cs`** | 房间视图模型：我方/敌方分栏、行标签、按钮可用性 | ~120 |
| **`scripts/UI/Logic/ModeText.cs`** | 模式名与一句话规则、车型名 | ~60 |
| `scripts/Match/MatchMain.cs` | 对局节点：构建、每帧流程（+ `MatchMain.Events.cs` ~160 事件到特效；`MatchMain.Camera.cs` ~120 镜头/瞄准/观战接线） | ~250 |
| `scripts/Match/TankActors.cs` | 别人的坦克：按 id 建/复用/隐藏 `TankActor`，染色、血条与名字、残骸、炮口 | ~160 |
| `scripts/Match/MatchInput.cs` | `DriveInput` 子类：Tab、F3、观战键 | ~90 |
| `scripts/Match/MatchHud.cs` | HUD 根（CanvasLayer 2），喂 `MatchHudFrame` 给各子控件 | ~140 |
| `scripts/Match/ScorePlate.cs`、`KillFeedView.cs`、`CenterBanner.cs`、`ScoreboardView.cs`、`NetPanel.cs`、`PauseOverlay.cs` | 各块 HUD（自绘，用 `HudStyle`） | 110 / 90 / 170 / 170 / 70 / 80 |
| `scripts/Match/HudGallery.cs` | `--hud-demo` 用：拿预制帧在纯色底上画每种 HUD 状态，确定性验收排版 | ~80 |
| `scripts/Match/MatchAutomation.cs` | 对局的自动化：预设机位、`--autodrive`、截图/退出/fps、事件时刻 | ~130 |
| **`scripts/Match/Logic/MatchColors.cs`** | `Side{Own,Ally,Enemy}`、`SideOf`、车体色与界面色 | ~50 |
| **`scripts/Match/Logic/RenderMath.cs`** | `OwnDisplay`、`YawRate`、`ShotDetector` | ~90 |
| **`scripts/Match/Logic/ScoreboardModel.cs`** | 计分板排序、名次、状态文字 | ~130 |
| **`scripts/Match/Logic/HudText.cs`** | 计时、比分板块、复活行、结算标题 | ~130 |
| **`scripts/Match/Logic/KillFeed.cs`** | 击毁提示滚动列表（时间到期，最多 5 行） | ~80 |
| **`scripts/Match/Logic/HudLayout.cs`** | 各 HUD 块的矩形，随窗口大小求值 | ~100 |
| **`scripts/Match/Logic/SpectateState.cs`** | 观战目标切换、自由视角焦点移动 | ~130 |
| **`scripts/Match/Logic/NetPanelText.cs`** | F3 面板文字（`CultureInfo.InvariantCulture`） | ~70 |
| **`scripts/Match/Logic/MatchHudFrame.cs`** | 一帧 HUD 所需数据的记录加 `From(BattleClient…)` 适配 | ~70 |
| **`scripts/Match/Logic/HudDemoFrames.cs`** | 画廊用的预制帧 | ~90 |
| `scripts/Vehicles/TankActor.cs` | 共享：模型、残骸烟、尘土、头顶锚点、后坐、残骸 | ~120 |
| `scripts/Overlay/CombatHud.cs` | 共享：见 1.2 | ~170 |
| `scripts/Driving/DriveInput.cs` | 共享基类 | ~140 |
| `scripts/Driving/AimSolver.cs` + **`Driving/Logic/AimMath.cs`** + **`Driving/Logic/FireLatch.cs`** | 瞄准求解；开火锁存 | 40 / 60 / 30 |
| **`scripts/Bootstrap/Logic/ShotPlan.cs`** + `scripts/Bootstrap/CaptureRun.cs` | 截图时刻表；截图/fps 日志/退出 | ~90 / ~80 |
| `scripts/Bootstrap/Main.Flow.cs` | `Main` 的 Host/Join/Leave/EnterMatch/Sandbox 流程 | ~200 |

### 2.2 修改现有文件

| 文件 | 改动 |
|---|---|
| `scripts/Bootstrap/Main.cs` | 重写为新流程（~230 行）：`_Ready` 建菜单与房间，处理 `--host/--join/--sandbox`；`_Process` 按 1.4 分发；`_UnhandledInput` 只留房间里的 Esc 离开。`Main.Update.cs` 不改（它只用 `_menu.SetVersion/SetUpdate`、`_options.NoUpdate/UpdateNow`）。`DescribeDisconnect`（`Main.cs:255-265`）保留并移到 `Main.Flow.cs`。 |
| `scripts/Bootstrap/LaunchOptions.cs` | `Mode` 由 `GameMode`（Core）改为 `BattleMode`，用 `ModeRules.TryParse`，写错给警告；`Bots` 夹到 0..9 并在被夹时警告；删 `ChaseCamera`、`--camera`；`--netgraph` 的含义改为"F3 面板默认打开"；新增 `Players`（`--players N`）、`Seed`、`Loopback`、`Sandbox`、`MatchSeconds`、`HudDemo`；`--shot-after` 增加 `death|ended`。去掉对 `Tank.Core` 的依赖。 |
| `scripts/UI/MenuScreen.cs` | 重写：去掉模式下拉；按钮 开房间、试驾场；右侧局域网房间列表加手填地址；版本和更新横幅接口不变。事件：`HostRequested(name)`、`JoinRequested(name,host,port)`、`SandboxRequested`。 |
| `scripts/UI/UiKit.cs` | 去掉 `using Tank.Client.View`，默认颜色改用 `Art.Palette`；API 不变，所以 `Hud.cs`、`LobbyScreen.cs` 仍能编译。 |
| `scripts/Sandbox/SandboxMain.cs` | 用 `TankActor`、`AimSolver`、`BattleWorld`；新增 `public event Action? ExitRequested`；Esc 第一次释放鼠标，已释放时再按则触发它（仅被 `Main` 订阅时）。 |
| `scripts/Sandbox/SandboxHud.cs` | 瘦成 `SandboxHud : CombatHud`，只留靶车血条、点亮调试线、靶车的隐藏判断。 |
| `scripts/Sandbox/SandboxInput.cs` | 继承 `DriveInput`，留 R、T、F1-F4。 |
| `scripts/Sandbox/SandboxWorld.cs` | 移到 `World/BattleWorld.cs`。 |
| `scripts/Sandbox/TargetRange.cs` | `Target` 里的 Model/Smoke/Dust 换成 `TankActor`。 |
| `scripts/Sandbox/SandboxAutomation.cs` | T9 才改：截图/fps/退出委托给 `CaptureRun` 和 `ShotPlan`。 |
| `scripts/Combat/ShellViews.cs` | 增加 `IReadOnlyList<ShellView>` 重载。 |
| `scripts/Overlay/HealthBars.cs` | `Set(index, head, share, shown, label=null, labelColor=null)`。 |
| `scripts/Art/Palette.cs` | 新增 `EnemyBody`、`AllyBody`、`UiBackground`、`UiDim`、`UiGood`、`UiDanger`（见第 4 节）。 |
| `src/Tank.Net/Battle/BattleClientTypes.cs`、`BattleClient.Match.cs` | `ShellView.Age`（上文）。 |
| `tests/Tank.Art.Tests/Tank.Art.Tests.csproj` | 新增 `<Compile Include="..\..\src\Tank.Client\scripts\**\Logic\**\*.cs" LinkBase="Logic" />`；`LaunchOptions` 的注释去掉"需要 Tank.Core"。 |
| `docs/PROGRESS.md`、`docs/superpowers/specs/2026-10-18-stage4-networking-design.md` §7 | 每个小节点更新进度；T11 把键位、HUD 布局补进 §7。 |

**不改：** `project.godot`、`Main.tscn`、`Sandbox.tscn`、`Main.Update.cs`、`View/*`、`UI/Hud.cs`、`UI/LobbyScreen.cs`、`Bootstrap/Session.cs`、`BotDriver.cs`、`Controls/*`（这些在 4C 删，重写 `Main` 之后已无人引用，但仍能编译）。

---

## 3. 接口与数据流

### 3.1 关键签名

```csharp
// Online/Logic
public sealed record HostSettings(string PlayerName, string RoomName, int Port, BattleMode Mode, int Bots, uint? Seed, double? MatchSeconds);
public sealed class BattleSession : IDisposable {
    public BattleClient Client { get; }
    public bool IsHosting { get; }
    public bool IsVirtualClock { get; }                  // 回环主机：MatchMain 用固定步长
    public double Now { get; }                           // 秒，单调
    public (double AvgMs, double MaxMs)? ServerTick { get; }   // 仅房主 (ServerRunner)
    public static BattleSession Host(HostSettings s, Action<string> log);       // InvalidOperationException: 端口被占
    public static BattleSession HostLoopback(HostSettings s, Action<string> log);
    public static BattleSession Join(string host, int port, string name, uint token = 0);
    public void Advance(double dt);                      // 线程主机：空操作；回环：推进虚拟网络与服务器 tick
    public void Dispose();                               // client → runner → transport
}
public sealed record LanRoomRow(IPEndPoint EndPoint, string Text, bool Joinable, string? WhyNot);
public sealed class AutoRoom { public AutoStep Next(RoomStateMessage? room, byte slot, bool isHost, int humans); }  // SetMode/SetBots/SetTank/StartMatch/Wait

// UI
public static class RoomRules { public static string? StartBlockReason(RoomStateMessage room); }
public partial class RoomScreen : Control { public event Action? LeaveRequested; public void Bind(BattleClient c, string addresses); public void Refresh(); }

// 共享
public readonly record struct MapStyle(IReadOnlyList<System.Numerics.Vector2> GrassIslands, uint Seed) { public static MapStyle Sandbox => new(SandboxMap.BushPatches, (uint)SandboxMap.Seed); }
public partial class BattleWorld : Node3D {
    public void Build(Battlefield field, in TankState focus, MapStyle style, bool streamsAroundCamera);
    public void SetField(Battlefield field);             // 重同步换了 Field 实例（见 R2）
    public void Update(TankState? tank, TankPose? pose, Vector3 cameraPos, float delta);   // null: 自由视角，草地不压平，围着相机流式加载
    public void OnEvent(in GameEvent e);
}
public partial class TankActor : Node3D {
    public void Build(TankSpec spec, TankPaint? paint = null, bool dust = true);
    public Vector3 Head { get; }  public bool IsWrecked { get; }  public Transform3D MuzzleTransform { get; }
    public void Present(in TankState state, TankPose pose, float yawRate, float delta, bool shown);
    public void Wreck(float turretYaw, bool settled = false);   // settled: 直接到落地烧焦，晚加入/第一次见到残骸时用
    public void Restore();  public void Kick(float turretYaw);
}
public delegate bool HeadLookup(int tankId, out Vector3 head);   // false: 未知或不该画
public partial class CombatHud : CanvasLayer {
    public HealthBars TankBars { get; }
    public void Build(OrbitCamera cam, Battlefield field, int ownTankId, int tankBarCount, HeadLookup headOf);
    public void SetField(Battlefield f);  public void ShowOwn(bool aiming);   // 观战时藏准星和状态面板
    public void OnShotEvent(in GameEvent e);                                  // 只对自己的炮弹出命中字、伤害数字
    public void Refresh(in TankState player, bool spotted);
}
public class DriveInput {
    public static void RegisterActions();
    public AmmoType Ammo { get; }  public bool FreeLook { get; }
    public virtual void OnInputEvent(InputEvent e);
    public (sbyte Throttle, sbyte Steer, bool Fire) Peek();    // 不消耗锁存
    public (sbyte Throttle, sbyte Steer, bool Fire) Sample();  // = Peek + EndTick，沙盒用
    public void EndTick();  public void ResetAmmo();  public void ReleaseAll();
}
public static class AimMath {
    public static (float Yaw, float Pitch) Solve(Battlefield f, SN.Vector3 cameraPos, SN.Vector3 look, float orbitDistance, bool sniper, SN.Vector3 trunnion, float prevYaw);
}

// Match/Logic
public enum Side : byte { Own, Ally, Enemy }
public static class MatchColors { public static Side SideOf(BattleMode m, byte ownTeam, byte team, bool isOwn); }
public static class RenderMath {
    public static TankState OwnDisplay(in TankState prev, in TankState cur, float alpha, SN.Vector2 renderPos);   // TankInterpolation.Between(...) with { Position = renderPos }
    public static float YawRate(float prevYaw, float yaw, float dt);
    public static TankState FromRemote(in RemoteTank r);
}
public sealed class ShotDetector { public bool Update(int shotCount, int tankId); }
public readonly record struct ScoreRow(byte Slot, string Name, byte Team, string TankName, int Kills, int Deaths, bool Alive, int Rank, Side Side, string Status);
public static class ScoreboardModel { public static IReadOnlyList<ScoreRow> Build(BattleMode mode, byte ownSlot, byte ownTeam, ReadOnlySpan<PlayerStats> players, Func<byte,string> nameOf, bool ended); }
public sealed class KillFeed { public void Add(string killer, Side ks, string victim, Side vs, bool friendly, double now); public IReadOnlyList<FeedLine> Visible(double now); }
public sealed class SpectateState { /* Mode{Wreck,Follow,Free}, TargetId, Next/Prev(candidates), Update(...), static MoveFocus(...) */ }
public static class HudLayout { public static HudBoxes For(SN.Vector2 size); }
```

### 3.2 每帧数据流

```
Main._Process
  无 MatchMain: session.Advance(dt); client.Update(dt, LocalCommand.None); RoomScreen.Refresh();
                AutoRoom.Next(...); 若 client.Phase==InMatch → EnterMatch()

MatchMain._Process(delta)          // 对局期间独占 client.Update
 1  dt = session.IsVirtualClock ? 1/60 : delta;  session.Advance(dt)
 2  cmd = new LocalCommand(input.Peek().Throttle/Steer, _aimYaw, _aimPitch, Fire, input.Ammo)   // 死亡/观战: None
 3  long before = client.Predictor.LastPredictedTick;  client.Update(dt, cmd);
    if (client.Predictor.LastPredictedTick != before) input.EndTick();
 4  if (client.Phase != InMatch) { Finished?.Invoke(); return; }
    if (!client.HasTank && client.Latest is null) { 显示"对局加载中…"; return; }   // 此时 Own.Spec 为 null
    if (!ReferenceEquals(client.Field, _field)) { _world.SetField(...); _combat.SetField(...); }
 5  while (client.TryDequeueEvent(out e)) OnEvent(e)               // 事件→特效，见 3.3
 6  own = RenderMath.OwnDisplay(client.Predictor.Previous, client.Predictor.Current,
                                (float)client.Clock.Alpha, client.OwnRenderPosition);
    pose = TankPose.Compute(own, field.Terrain);                  // OwnRenderPosition 已含 VisualOffset (BattleClient.Match.cs:56)
    观战时 focus = SpectateState 选定的 RemoteTank 或自由焦点
 7  camera.Follow(pivot, sniperEye, terrain);  非自由视角且活着: (_aimYaw,_aimPitch) = AimSolver.Solve(...)
 8  _world.Update(focus...);  _actors.Sync(client.Others, ...);  _shells.Sync(client.Shells, (float)client.Clock.Alpha, dt)
 9  _combat.Refresh(own, client.Spotted && !own.IsDestroyed);  _hud.Refresh(MatchHudFrame.From(...))
10  _automation.EndFrame(dt, _time)
```

**三个时间域，不要混：**
- 自己：预测，时钟领先服务器约 RTT 加 2 tick，用 `Clock.Alpha` 在 `Predictor.Previous/Current` 间插值。`Clock.Advance` 每帧最多追 8 tick（`ClockSync.cs:17,81-86`）。
- 别人：`Interpolator.Sample` 在 `RenderTick`（约落后服务器 100 ms）连续采样，不需要 alpha。
- 炮弹：自己的预测弹与他人的本地模拟弹都在客户端 tick 上步进，画面用 `Clock.Alpha` 插值 `Previous→Position`。

沙盒的做法是自己攒 `_accumulator`，`MaxCatchUp 0.25`（`SandboxMain.cs:31,142-147`），一次步进一个 `Simulation`。对局不要再攒，节拍全由 `BattleClient` 给。

### 3.3 事件与特效映射（沙盒 `OnEvent`：`SandboxMain.cs:200-226`）

| 事件 | 沙盒 | 对局 |
|---|---|---|
| 自己开火 | `ShellFired` 且 `TankId==自己` → 记 `_playerFired`，调 `_tank.Fire`（:205-209） | **不用事件。** `ShotDetector` 看 `client.Own.ShotCount` 增加，零延迟闪光/后坐/镜头抖动。事件队列里自己预测过的开火不会出现（`BattleClient.Match.cs:256-257` 直接 `return`）；没预测到的（硬重同步后）当别人的开火处理 |
| 别人开火 | `fx.Muzzle(Transform(e.Position))`（:210-212） | 用 `e.Position` 与 `Basis.LookingAt(e.Velocity)`（枪口 −Z 沿炮管）；若 `TankActors` 有 `e.TankId` 且可见则 `Kick` 后坐。`e.TankId==0` 是看不见的射手（`Visibility.cs` 抹掉），只放枪口火光，这正是"草丛里有人开炮"的设计 |
| `ShellExploded` | 离地超过 0.5 m 为灰尘，否则白烟（:213-217） | 同，用 `client.Field.Terrain` |
| `TankHit` 且 AP | 灰色小爆（:218-220） | 同 |
| 命中字 / 伤害数字 | `SandboxHud.OnEvent`，只对自己的炮弹，隐藏靶车不出字（`SandboxHud.cs:69-88`） | `CombatHud.OnShotEvent`，`HeadLookup` 返回 false（该车不在快照里）就跳过 |
| `TankDestroyed` / `TankRevived` | `TargetRange.OnEvent` 调 Wreck/Restore | `TankActors.OnEvent`，自己的车同样走 `TankActor`；**以事件为准，状态为兜底**：快照里 `Destroyed` 持续超过 0.3 秒还没收到事件（晚加入、丢事件）则 `Wreck(settled:true)` |
| 物件受伤/被毁 | `PropFx.OnEvent` 加 `_world.OnEvent` | 同，参数换 `client.Field` |
| 击毁提示 | 无 | `TankDestroyed` → `KillFeed`；名字从 `Latest.Players[].TankId` 反查槽位，再取 `Room.Players[slot].Name`；射手 id 为 0 时写成"X 被击毁" |

**关于击毁提示的取舍：** 只用事件，不用 `PlayerStats` 的击杀数增量。服务器只给你看得见目标的击毁事件（`Visibility.cs` 的 `Wants`），用增量会泄露看不见的击杀。计分板数据是全局的，两者不矛盾。

**别人的坦克画面状态：** 每个 `RemoteTank`（`BattleInterpolator.cs:8`）用 `RenderMath.FromRemote` 转成 `TankState`（车型取 `TankCatalog.All[e.Spec]`，血量取 `Entity.Health`，`Destroyed` 取标志位），再 `TankPose.Compute`，履带动画的偏航率用 `YawRate(prevYaw, yaw, dt)`（绕 ±π 要取最短弧，夹到 ±3 rad/s）。不在 `Others` 里的演员立即隐藏（没点亮就不画，没有淡出）。

---

## 4. 颜色与 HUD 布局

### 4.1 颜色（`Art/Palette.cs`）

| 用途 | 条目 | 值 |
|---|---|---|
| 自己的车 | `TankBody`（现有） | F28A2E 玩具橙 |
| 队友 | `AllyBody` = `TargetBody`（别名，现有值） | 6F8FB5 蓝灰，和试驾场靶车同色 |
| 敌人（各自为战里所有别人） | `EnemyBody`（新） | D8344A（暂定，见下） |
| 残骸 | `Charred`（现有，着色器 `char_amount`） | 2B2622 |
| 菜单/房间 | `UiBackground`、`UiDim`、`UiGood`、`UiDanger`（新） | 0B0E16、8C94A3、73E68C、FF4D4D（= `HitPierced`） |

**规则：** `MatchColors.SideOf(mode, ownTeam, team, isOwn)`：自己 → Own；`ModeRules.HasTeams(mode)` 且 `team==ownTeam` → Ally；其余全是 Enemy。各自为战时服务器给的 `Team` 就是槽位号，规则天然给出"人人是敌人"，但 `HasTeams` 判断保留，不靠这个巧合。`ownTeam` 取 `Latest.Players[client.Slot].Team`（自己的车不在快照时也有值）。

**测试约束（`PaletteTests`）：** `EnemyBody` 到 `Brick` 的 sRGB 距离 ≥ 0.15（D8344A 约 0.164）、到 `TankBody` ≥ 0.3、到 `TargetBody` ≥ 0.25；`AllyBody == TargetBody`；`EnemyBody` 与红色命中字 `HitPierced` 区分得开。实测颜色后值可调，规则不变。

**房间里的队伍标签也走相对色：** 每个客户端看到自己的队伍是蓝灰、对方是红，与局内一致。按钮写"换到对面"，发 `SetTeam((byte)(1 - myTeam))`。不要写"金队/蓝队"。

### 4.2 HUD 布局（`HudLayout.For(Vector2 size)`，基准单位 1600×900；1080p = ×1.2）

| 块 | 基准位置 | 1080p 等价 | 复用/新增 |
|---|---|---|---|
| 准星 | 屏幕中心 | 同 | 现有 `Reticle` |
| 状态面板（左下，含"被点亮"灯） | 现有，约 24..362 × 698..876 | 同 ×1.2 | 现有 `StatusPanel`，灯用 `client.Spotted` |
| 计时+比分板 | 顶部居中，360×72，y=16；左右各 112 宽的分数块，中间计时（字号 40，分数 36）；板下 18 号小字写模式名（基线 y=112） | 432×86，y=19 | 新 `ScorePlate`，`HudStyle.RoundedBox` + `DrawOutlinedCentered` |
| 击毁提示 | 右上，右边距 24，y 起点 16，行高 36、间距 6，最多 5 行，字号 22，宽 ≤ 440 | 右距 29 | 新 `KillFeedView` |
| F3 面板 | 左上 (24,16)，500×约 280，约 10 行（字号 20，行距 24） | 600×336 | 新 `NetPanel` |
| 复活/阵亡横幅 | 水平居中，y=0.28H（252），420×210：说明字 28、倒计时数字 96 | y=302 | 新 `CenterBanner` |
| 观战提示条 | 底部居中，680×56，距底 36（y=H−92）；字号 24 | 816×67 | `CenterBanner` 的另一种模式 |
| 计分板（Tab） | 居中 900 宽，y=110；表头 56，两队横幅各 38，每人 42；10 人共约 576 高。列：名次 / 名字 / 车型 / 击毁 / 阵亡 / 状态 | 1080×… | 新 `ScoreboardView` |
| 结算 | 居中 960 宽、y=60、高 780，背景压暗 0.55；标题基线 140（字号 84），副标题 188，榜单从 224 起，页脚"N 秒后返回房间"基线 812 | ×1.2 | `CenterBanner` + `ScoreboardView` 复用同一张表 |
| 暂停层（Esc） | 居中 440×280 | — | `UiKit.Panel/Button`（真正的 Control，可点） |
| 血条+名字 | 车头上方，名字 18 号在血条上 | — | `HealthBars` 加 label |

布局不变量（`HudLayoutTests` 在 1600×900、1600×1200、2100×900 三个尺寸下断言）：所有块在画面内；计分板与击毁提示、F3 面板两两不相交；观战提示条与状态面板区域不相交；击毁提示的左边界在计分板右边界之后（1600 宽时 1116 > 980）。

**比分板块的内容按模式变：**
- 团队对战：我方击毁数 / 计时 / 敌方击毁数，用 `TeamScore0/1` 按自己的队伍翻转成"我方/敌方"。
- 团队歼灭：两边存活数（`Players` 里 `Active && Alive` 的计数）。
- 各自为战：左"击毁 N"，右"存活 a/b"。

**字体与排版：** 全部自绘，用 `HudStyle.Font`（ZCOOL 快乐体，随包带，不依赖系统 CJK 回退）、`DrawOutlined/DrawOutlinedCentered`、`TextWidth`、`Baseline`、`RoundedBox`、`Accent/Text/OutlineColor`、`HealthFill`。菜单、房间、暂停层用 `UiKit` 的 `Label/Panel/Button/Box/FillViewport/Gap` 和 `Theme`。HUD 控件只在内容变化时 `QueueRedraw`（照 `StatusPanel.UpdateState` 的写法，`StatusPanel.cs:100-115`）。

---

## 5. 构建顺序（11 个任务）

通用：环境 `export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`。Godot 命令是 `godot-mono`，每次套强制结束（PROGRESS 工作约定）：

```bash
godot-mono --path src/Tank.Client -- <参数> & P=$!; ( sleep 40; kill -9 $P ) & wait $P
```

对局默认场景是 `Main.tscn`；沙盒用 `godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn -- <参数>`。每个任务结束：`dotnet build` 客户端 0 警告、相关测试全过、更新 `docs/PROGRESS.md` 一个节点。

**开工前先在 HEAD 上拍沙盒基线**（T1、T2 的回归要比对）：
1. `Sandbox.tscn -- --camera-preset orbit --screenshot build/shots/4b-base-orbit.png --screenshot-at 3 --quit-after 5`
2. `-- --range-demo kill --shot-after kill:2.5 --screenshot build/shots/4b-base-kill.png --quit-after 25`
3. `-- --range-demo lamp --screenshot build/shots/4b-base-lamp.png --screenshot-at 12 --quit-after 15`
4. `-- --range-demo wall --shot-after prop:1 --screenshot build/shots/4b-base-wall.png --quit-after 20`

### T1 共享抽取 A：不改外观的纯逻辑与搬家

- **文件：** 新建 `Driving/DriveInput.cs`、`Driving/AimSolver.cs`、`Driving/Logic/AimMath.cs`、`Driving/Logic/FireLatch.cs`；`git mv Sandbox/SandboxWorld.cs → World/BattleWorld.cs` 并加 `MapStyle`、`SetField`；改 `SandboxInput`、`SandboxMain`（只换 `UpdateAim` 与世界类型名）、`ShellViews`（重载）、`Palette`（新增颜色）、`BattleClientTypes.cs` / `BattleClient.Match.cs`（`Age`）、Art csproj（`Logic` 通配）。
- **测试（Art）：**
  - `FireLatchTests`：按下锁存；`Peek` 不清；`EndTick` 才清；按住时持续为真；松开后清。
  - `AimMathTests`：射线打中地面取命中点；射线落空取射程末端；目标离炮耳 < 8 m 时按视线水平方向推出 8 m，不再原地乱转（沿用 `SandboxMain.cs:266-272` 的行为）。
  - `PaletteTests`：新颜色约束（4.1）。
- **测试（Net）：** 一条：预测弹经若干 tick 后 `ShellView.Age > 0`。
- **验证：** `dotnet test tests/Tank.Art.Tests tests/Tank.Net.Tests`；跑基线命令 1-4 后与基线逐张比对（目视加状态面板区域像素比对，粒子区域允许差异）。

### T2 共享抽取 B：Godot 节点（回归风险最大，放在对局之前单独做）

- **文件：** 新建 `Vehicles/TankActor.cs`、`Overlay/CombatHud.cs`；改 `SandboxHud`（瘦身成子类）、`TargetRange`（`Target` 持 `TankActor`）、`SandboxMain`（玩家用 `TankActor`，`ChangeTank` 里重建）、`HealthBars`（标签）。
- **测试：** 无新增纯逻辑；靠回归截图。
- **验证：** 重跑基线 1-4，重点看：炮塔弹飞与焦黑烟（命令 2）、被点亮灯与状态面板（命令 3）、墙血条与伤害数字（命令 4）。另加：`--camera-preset patrol --spot-debug --screenshot-at 8`（调试线）与 `--tank heavy --camera-preset orbit`（换车后外观），各一张。

### T3 在线层与启动参数骨架

- **文件：** `Online/Logic/*`（`BattleSession`、`ThreadedHost`、`LoopbackHost`、`HostSettings`、`LanBrowser`、`LanRoomRow`、`AutoRoom`）；`LaunchOptions.cs` 按 2.2 改。
- **测试（Art）：**
  - `LoopbackSessionTests`：`HostLoopback`，`--bots 3`，`AutoRoom` 自动开局，虚拟 3 秒内 `Client.Phase==InMatch` 且 `HasTank`；跑 10 秒虚拟时间不抛异常；`Dispose` 可重复调用。
  - `AutoRoomTests`：命令次序 SetTank→SetMode→SetBots→StartMatch；`--players 2` 没到人数不开局；只发一次。
  - `LanRoomRowTests`：版本不符行不可加入，写明原因；满员、对局进行中。
  - `LaunchOptionsTests` 扩充：模式名、坏值警告、`--bots` 夹范围、新选项。
- **验证：** 测试即可；无画面。可选：一条真实 UDP 的会话测试（127.0.0.1 临时端口），绑定失败时跳过。

### T4 菜单、房间与 `Main` 重写

- **文件：** 重写 `MenuScreen`；新 `RoomScreen`、`UI/Logic/{RoomRules,RoomModel,ModeText}`；改 `UiKit`、`Main.cs`、新 `Main.Flow.cs`；`SandboxMain.ExitRequested`；`--host/--join/--mode/--bots/--tank/--autostart/--players` 通过 `AutoRoom` 在 `Main` 里接上。本任务只做这些最基本的启动参数，因为不接上就没有任何东西可以截图验证；`--screenshot/--screenshot-at/--quit-after` 沿用 `Main.cs:233-243` 的现有实现。
- **测试（Art）：**
  - `RoomRulesTests`：与真服务器交叉校验。在 `LoopbackNetwork` 上建真 `BattleServer`，对约 12 组（模式 × 真人数 × 机器人数 × 队伍分布）算 `StartBlockReason(room)==null`，并与同一状态下 `server.TryStartMatch` 的结果比较，包括"一队为空""不足两辆""机器人补进较少的队"（`BattleServer.Match.cs:19-43`、`Room.cs:144-155`）。
  - `RoomModelTests`：分栏按相对队伍；各自为战单栏；对局进行中显示等待。
- **验证（截图）：**
  - 菜单：`-- --screenshot build/shots/4b-t4-menu.png --screenshot-at 3 --quit-after 5`
  - 团队房间：`-- --host --bots 3 --screenshot build/shots/4b-t4-room-team.png --screenshot-at 4 --quit-after 6`
  - 各自为战房间：`-- --host --mode ffa --bots 4 --tank heavy --screenshot build/shots/4b-t4-room-ffa.png --screenshot-at 4 --quit-after 6`
  - 两个实例：A 用 `--host --port 27016 --players 2 --autostart`，B 用 `--join 127.0.0.1:27016`，确认两端进房间、换队、选车、准备、开局。对局画面此时还没有，进入 `InMatch` 后先 `Main` 里打一行日志证明状态切换到位。
  - 点"试驾场"进沙盒，Esc 两次回菜单。

### T5 对局场景核心

- **文件：** `Match/MatchMain.cs`（+ `.Events.cs` + `.Camera.cs`）、`TankActors.cs`、`MatchInput.cs`；`Match/Logic/{MatchColors,RenderMath}`；`Main.Flow.cs` 的 `EnterMatch/LeaveMatch`；`CombatHud` 已在 T2 完成，这里接线（准星、状态面板、命中字、血条与名字、"被点亮"灯）。
- **测试（Art）：**
  - `MatchColorsTests`：自己、队友、敌人；各自为战人人为敌；队伍相同但不是自己 → Ally。
  - `RenderMathTests`：`OwnDisplay` 在 `alpha=0/1` 取到两端，位置等于 `renderPos`；`YawRate` 绕 ±π 取最短弧、`dt<=0` 返回 0；`ShotDetector` 递增只触发一次、回退不触发。
- **验证（截图，Main.tscn）：**
  - `-- --host --bots 3 --autostart --screenshot build/shots/4b-t5-team.png --screenshot-at 8 --quit-after 12`：自己橙色；第 2 个机器人（团队里它与房主同队，出生点相距约 10 m）蓝灰；`--screenshot-at` 此时仍由 `Main` 计时，对局内的计时在 T9 修正。
  - `-- --host --mode ffa --bots 3 --autostart ...`：别人一律红（需要等到有人进入视线，挑时间见 T9）。
  - 手动：开火有零延迟枪口闪光；撞物件、打墙有特效。
  - 帧率先看 `--netgraph`（`Main.LogStats` 已打印 fps，`Main.cs:245-252`，改写成新字段）。

### T6 HUD 基础件

- **文件：** `MatchHud.cs`、`ScorePlate`、`KillFeedView`、`CenterBanner`（复活/观战两种）、`NetPanel`、`HudGallery`；`Match/Logic/{HudText,KillFeed,HudLayout,NetPanelText,MatchHudFrame,HudDemoFrames}`。
- **测试（Art）：**
  - `HudTextTests`：`TicksLeft` 向上取整成 m:ss；复活秒数；三种模式的比分块。
  - `KillFeedTests`：5 秒到期、最多 5 行、射手未知的文案、误伤文案。
  - `HudLayoutTests`：见 4.2 的不变量。
  - `NetPanelTextTests`：包含对账次数/最近/平均/最大与快照字节数；`de-DE` 文化下仍是 `.`。
  - `MatchHudFrameTests`：用 T3 的回环会话跑 5 秒，断言 `From(client…)` 给出的模式、计时（约 7:00 减去已过时间）、名单。
  - `HudDemoFramesTests`：每帧数据自洽（行数 ≤ 10、排名连续）。
- **验证（截图）：** `--hud-demo timer|feed|respawn|spectate`（T9 才有 `--hud-demo` 解析，这里先由 `Main` 临时读命令行参数，T9 改进 `LaunchOptions`）。再加真对局里 `--netgraph` 看 F3 面板：`-- --host --bots 3 --autostart --netgraph --screenshot build/shots/4b-t6-f3.png --screenshot-at 8 --quit-after 12`。

### T7 计分板与结算

- **文件：** `ScoreboardView`、`CenterBanner` 的结算模式；`Match/Logic/ScoreboardModel`、`HudText` 的结算标题。
- **排序规则：**
  - 团队模式：我方队伍在上，组内击毁数降序、阵亡升序、槽位升序。
  - 各自为战对局中：存活优先，其次击毁降序、阵亡升序；**结束后用服务器给的 `Rank`**（`PlayerStats.Rank`，对局结束前为 0）。
  - 状态列：存活 / 已毁 / 复活中 N 秒（仅自己的行知道复活秒数，来自 `Match.RespawnIn`）。
- **测试（Art）：** `ScoreboardModelTests`：团队分组与相对色、各自为战结束前后的排序、平局与击毁并列、离队玩家（`Active=false`）不出现、名字缺失时的兜底。
- **验证：** `--hud-demo scoreboard|results-win|results-lose|results-draw|results-ffa`。真对局的结算要等 T9 的 `--match-seconds`。

### T8 阵亡、观战与暂停层

- **文件：** `Match/Logic/SpectateState`、`MatchMain.Camera.cs` 的观战接线、`PauseOverlay`；`BattleSession.Reconnect`（可选，3 次、每 2 秒，用 `client.SessionToken` 重连）。
- **规则：**
  - 自己被毁后镜头先留在残骸上 3 秒（击毁特写）。
  - 无复活模式（团歼、各自为战）3 秒后自动切到观战目标。
  - 团队对战留在残骸等复活，期间 Q / E 或鼠标左键可以看队友。
  - 复活时：`DriveInput.ResetAmmo()`（避免沿用上次选的 HE 触发一次满装填，同 `SandboxMain.cs:185` 的理由）、镜头回自己。
  - 观战键：Q 上一个、E 下一个、F 自由（仅各自为战），鼠标左键也是下一个。
  - 候选：团队模式 = `Others` 里 `Team==ownTeam && !Destroyed`；各自为战 = 所有存活的别人。
  - 自由视角：焦点由 WASD 在 XZ 平面上移动（相对相机偏航，60 m/s，Shift 三倍），夹在 `field.ClampToPlayable`，高度取地形高度，`OrbitCamera` 继续负责环绕和滚轮缩放。不新写飞行相机。
  - Esc 释放鼠标并弹出暂停层：继续 / 离开房间；房主多一个"结束对局回到房间"（`ReturnToRoom`）。
- **测试（Art）：** `SpectateStateTests`：切换回绕；目标死后换下一个，没有候选回到残骸或自由；`MoveFocus` 方向相对相机偏航正确（偏航 0 朝 −Z）；夹到边界。
- **验证：** 画廊 `--hud-demo spectate-team|spectate-ffa`。真正的死亡流程用手动试玩，清单放在 T11。

### T9 启动参数完整化与截图自动化

- **文件：** `Bootstrap/Logic/ShotPlan.cs`、`Bootstrap/CaptureRun.cs`、`Match/MatchAutomation.cs`；改 `SandboxAutomation`（委托）、`LaunchOptions`（见 2.2）、`Main`。
- **要点：**
  - `ShotPlan` 把 `SandboxAutomation.cs:120-149` 的"时刻表"抽成纯逻辑，时刻增加 `Death`、`Ended`。
  - 对局的 `--screenshot-at` 从 `MatchMain` 创建起算；`--autostart` 时 `Main` 不再自己截图，交给 `MatchAutomation`。
  - 对局支持的现有选项：`--camera-preset`（`CameraPresets.Get`，`hill/spin/orbit/front/side` 等）、`--autodrive`、`--fps-log`、`--sniper`、`--netgraph`、`--shot-after fire|hit|kill|death|ended`、`--quit-after`。
  - 新增 `--match-seconds S`（作用于主机的 `BattleServerOptions.MatchSeconds`，让结算页能在几十秒内出现）、`--seed N`、`--loopback`、`--players N`、`--hud-demo NAME`。
- **测试（Art）：** `ShotPlanTests`（各时刻、`fx-shot` 预热、`shot-after` 延迟、写错时报警告）；`LaunchOptionsTests` 补 `--hud-demo`、`--seed`、`--match-seconds` 的解析与坏值。
- **验证：**
  - 确定性冒烟：`-- --loopback --host --bots 3 --autostart --seed 7 --autodrive --screenshot build/shots/4b-t9-smoke.png --screenshot-at 20 --quit-after 25`，跑两次，比赛进程应一致。
  - 各自为战红色敌人：`-- --loopback --host --mode ffa --bots 3 --autostart --seed 7 --autodrive --screenshot build/shots/4b-t9-ffa.png --screenshot-at 40`。先手动看一次日志确定敌人进入视线的时间，再写进命令，`--seed` 固定后不再变。
  - 结算页：`-- --loopback --host --mode annihilation --bots 1 --match-seconds 20 --autostart --seed 7 --shot-after ended:3 --screenshot build/shots/4b-t9-results.png`
  - 回归：沙盒基线命令 1-4，确认 `SandboxAutomation` 迁到 `CaptureRun` 后截图时机不变，另加 `--autodrive --fx-shot 1`。

### T10 性能与打磨

- **内容：**
  - 10 辆坦克同屏的帧率：`--loopback --host --mode ffa --bots 9 --autostart --seed 7 --camera-preset orbit --fps-log --quit-after 40`，至少 30 秒（PROGRESS 约定，前 7-20 秒不算）。预算以沙盒同条件为参照，不明显劣化。
  - 每帧不分配：`MatchMain` 里的委托、`StringName`、列表缓存复用，参照沙盒的做法（`SandboxMain.cs:45` 缓存 `_groundAt`）。
  - `TankModel.Build` 的开局卡顿：第一份快照到来时一次建好全部名单（`Latest.Players` 里 `Active` 的每个 `TankId` + `Spec`），之后只在新 id（团队对战中途加入）出现时才懒建。
  - `BattleFx` 池（4/6/12，`BattleFx.cs:25-27`）10 辆车同时开火时是否被抢占，必要时调大。
  - 窗口失焦：`_Notification(NotificationApplicationFocusOut)` 释放鼠标、`ReleaseAll()`（照 `SandboxMain.cs:110-115`）。
  - 重连（T8 若未做）：断线时 `Main` 以同一 `SessionToken` 新建 `BattleClient`，提示"正在重连…"。
- **验证：** 上面的 fps 命令加 `--netgraph` 看服务器 tick 毫秒；用 Activity Monitor 或 `dotnet-counters` 看托管堆无持续增长。

### T11 验收与文档

- **自动：** `dotnet test`（Sim、Net、Art、Core、Update）全过；客户端、服务器、soak 三个项目 0 警告；沙盒回归截图全部与基线一致。
- **两台机器或两个实例（设计 §1 的 4B 验收）：** 房主选团队对战加 2 个机器人，另一个客户端经局域网列表加入，各自选车、换队、准备、开局。开局后 F3 面板看对账误差（有线下平均应在厘米级，对照 soak 数字：有线平均 0.045 m、最大 0.43 m）。打满一局看到结算，8 秒后两端回房间；再开一局各自为战，一方阵亡后在观战里自由视角看全场。
- **手动清单（无法截图验证）：** 死亡的复活倒计时与重生、观战切换、Esc 暂停层、房主离开后客户端的提示、断线重连、`Tab` 计分板、被点亮灯、车型四选一的外观。
- **文档：** `docs/PROGRESS.md` 记节点；设计 §7 补"实现中定下来的"键位表和 HUD 布局；列出 4C 衔接（第 6 节）。

---

## 6. 风险与已解决的问题

**R1 开火点击在每帧调 `Update` 时会丢。** `SandboxInput.Sample`（`SandboxInput.cs:113-120`）每次采样就清锁存，沙盒因为每个 tick 采样一次所以没事。`BattleClient.Update(dt, input)` 每渲染帧调一次，而 `Clock.Advance` 在 60-144 Hz 帧率下大多数帧返回 0 个 tick。若先 `Sample` 再 `Update`，点击会在一个"没有 tick 的帧"里被消耗掉。**解决：** `Peek` 不清；`Update` 之后发现 `client.Predictor.LastPredictedTick` 变了才调 `EndTick()`。`FireLatchTests` 覆盖。

**R2 重同步会换掉 `client.Field`。** `HandleMatchStart` 每次都新建 `Battlefield` 并赋给 `Field`（`BattleClient.Match.cs:80,88`），同局重同步也是（:85-86 只是决定要不要清状态）。`BattleWorld`、`CombatHud` 缓存了旧引用。**解决：** `MatchMain` 每帧比较 `ReferenceEquals(client.Field, _field)`，变了就 `SetField`。地形、岩石、草的几何不用重建（数据相同）。`WallViews.Sync(field, …)`、`BushField.SyncStanding(field)` 本来就每帧传 `field`（`SandboxWorld.cs:73-75`）。

**R3 第一份快照之前 `Own.Spec` 为 null。** `MatchStart` 先到，`Phase` 已是 `InMatch`，但 `HasTank` 为假、`Latest` 为空。**解决：** `MatchMain` 在这段时间只画"对局加载中…"并直接返回；任何读 `Own.Spec` 的路径先查 `HasTank`。

**R4 地图精确一致吗。** 客户端 `Field` 来自 `MapData.Build()`（`MapData.cs`）：高度按 1 cm 取整，物件按服务器的 id 原样带过来，所以玩法相关的一切（碰撞、射线、物件 id）和服务器位位相同，不依赖 Mac ARM 与 Windows x64 的 `MathF` 差异。客户端自己还依赖两样静态数据：`SandboxMap.BushPatches` 和 `SandboxMap.Seed`，用于 `DirtMask`（草地岛）。它们是编译期常量，同一协议版本一致；就算有差异也只影响草的外观，不影响玩法。`MapStyle` 把这个依赖隔离到一处，以后多地图时把它放进 `MapData`。不要在房间阶段用本地 `SandboxMap.Create()` 预建世界当背景：本地生成的地形在不同平台上可能和服务器的略有不同。

**R5 自己是残骸时别人必须继续画。** 服务器对阵亡玩家照常发快照（`BattleServer.Match.cs:119-124` 只看 `seat.InMatch`），团队模式看本队视野加所有残骸，各自为战阵亡后全部可见（`Visibility.cs` 的 `SeesAll`）。`Interpolator.Sample(_others, OwnTankId)` 只排除自己。客户端自己的残骸来自 `Predictor`：`TankStep.Advance` 对残骸直接返回（`TankStep.cs:41`），`Reconcile` 把服务器的 `Destroyed` 带过来，复活时走 `respawned` 分支重置（`BattlePredictor.cs:155-163`）。要注意：自己的 `Destroyed` 随快照到，比对应的 `TankDestroyed` 事件（走渲染时钟，约晚 100 ms）早，直接用状态会"先变残骸后爆炸"。所以按 3.3 里"事件为准，状态 0.3 秒兜底"处理。

**R6 `Palette` 同名冲突。** `Tank.Client.View.Palette`（旧界面色）和 `Tank.Client.Art.Palette` 同名。新文件只 `using Tank.Client.Art`；`UiKit` 改完后，仍用旧 `View.Palette` 的只剩 `Hud.cs`、`LobbyScreen.cs`，4C 一起删。

**R7 命名空间遮蔽。** 不要建 `Tank.Client.Input`（会遮住 `Godot.Input`，`SandboxInput.cs` 就是用 `Input.IsActionPressed`），也不要建 `Tank.Client.Hud`（设计文档 §9 已记录过这个坑）。本方案用 `Driving`、`Match`、`Online`；`Match` 命名空间下没有叫 `Match` 的类型，`client.Match` 是成员访问，不受影响。

**R8 `LanDiscovery` 只向默认端口广播**（`LanDiscovery.cs:33,36`），房主用自定义 `--port` 时搜不到，只能手填地址。菜单文案要写明。房间信息里 `Info.Port` 已在 `OnReply` 里被用来构造 `EndPoint`（:83）。

**R9 `RoomInfoMessage` 有两个同名类型**（`Tank.Net.Protocol` 与 `Tank.Net.Battle`）。新代码只 `using Tank.Net.Battle`。

**R10 同机双实例。** 第二个实例开房间会因端口被占失败：`ThreadedHost` 抛 `InvalidOperationException`（`LiteNetServerTransport.cs:57-58`），`Main` 捕获后显示在菜单状态栏。测试用 `--port` 区分。

**R11 `ServerRunner.Stop` 最多阻塞主线程 2 秒**（`ServerRunner.cs:46-50`）。`Leave` 时可能感到一次短暂停顿；这是可接受的，线程是后台线程，进程退出不会被拦住。

**R12 草地压平的全局着色器参数只有一辆车。** `tank_footprint`/`tank_axes` 是单值全局（`GrassField.cs:60-62` 的注释已写明）。对局里只压平自己（观战时压平被跟随的车），别人的车不压草。接受。

**R13 `ConfigureViewport` 是全局的**（`Scenery.cs` 注释）。对局与沙盒都调用，且在两者之间切换后设置仍保留；所有场景统一用同一设置，所以无碍。

**R14 鼠标捕获。** 对局开始时自动捕获（自动化运行除外），Esc 释放并弹暂停层，点"继续"重新捕获。离开对局/回菜单时必须 `Input.MouseMode = Visible`，否则菜单点不了。

**R15 4C 衔接。** 4B 的新代码依赖以下"共用"件，4C 删旧代码时必须保留或迁移：
- `Tank.Net/Server/ServerRunner.cs` 与 `ITickServer`（默认参数用了 `Tank.Core.Units.TickRate`，`ServerRunner.cs:27`；4C 删掉默认值，调用方都已显式传 30）。
- `Tank.Net/Client/ClockSync.cs`（构造默认用 `Units.TickRate`，`ClockSync.cs:23` 附近，同理）。
- `Tank.Net/Transport/LoopbackNetwork.cs`（`using Tank.Core.Math` 的 `XorShift`，换成 `Tank.Sim.Geometry.XorShift`）。
- `Tank.Net.Protocol.ProtocolInfo.DefaultPort`、`BitReader/BitWriter`。
- `project.godot` 里的 `scoreboard`（Tab）、`toggle_netgraph`（F3）两个动作不要删。

**R16 已知缺口（与设计 §9"不做"一致，4B 不做）：** 被击中方向指示、小地图、音效、聊天、出生保护的护盾画面、按键改键、房主迁移（房主服务器与房主进程同生共死，房主退出时客户端提示"房主关闭了房间"，`Main.cs:261` 已有对应文案）。

---

## 7. 被引用的关键文件（绝对路径）

- `/Users/murphy/Desktop/dev/BattleCity/docs/superpowers/specs/2026-10-18-stage4-networking-design.md`（§5.9、§7、§8）
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Net/Battle/BattleClient.cs`、`BattleClient.Match.cs`、`BattleClientTypes.cs`、`BattleServer.Room.cs`、`BattleServer.Match.cs`、`Visibility.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Net/Server/ServerRunner.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Net/Transport/LiteNetServerTransport.cs`、`LiteNetClientTransport.cs`、`LanDiscovery.cs`、`LoopbackNetwork.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Client/scripts/Bootstrap/Main.cs`、`Main.Update.cs`、`LaunchOptions.cs`、`Session.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Client/scripts/Sandbox/SandboxMain.cs`、`SandboxHud.cs`、`SandboxWorld.cs`、`SandboxInput.cs`、`SandboxAutomation.cs`、`TargetRange.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Client/scripts/Overlay/HudStyle.cs`、`HealthBars.cs`、`StatusPanel.cs`、`CombatPopups.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Client/scripts/Combat/ShellViews.cs`、`PropFx.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Client/scripts/UI/MenuScreen.cs`、`UiKit.cs`
- `/Users/murphy/Desktop/dev/BattleCity/src/Tank.Client/project.godot`
- `/Users/murphy/Desktop/dev/BattleCity/tests/Tank.Art.Tests/Tank.Art.Tests.csproj`
- `/Users/murphy/Desktop/dev/BattleCity/tests/Tank.Net.Tests/Battle/BattleHarness.cs`
- `/Users/murphy/Desktop/dev/BattleCity/docs/PROGRESS.md`
