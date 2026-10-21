# 阶段 4C：清理 — 实施计划

日期：2026-10-21
范围：[阶段 4 联网设计](../specs/2026-10-18-stage4-networking-design.md) §8、§8.1。
分支 / 目录：`feat/stage4c-cleanup`，`/Users/murphy/Desktop/dev/BattleCity-stage4c`，从 main `363524c` 拉出。

4C 主要是删代码和挪共用件，没有新玩法，所以不走“草稿 → 导出计划 → 逐字重放”，直接在 feat 分支上按任务提交，每个任务提交前三个项目 0 警告、全部测试通过；做完统一审查、验收，再 squash 合入 main。

## 调研结论（main `363524c`）

- 旧代码：`src/Tank.Core`（31 个文件，1809 行，含 3 张旧地图）、`tests/Tank.Core.Tests`、`tools/genmaps.py`；`Tank.Net` 的 `Client/*`（除 `ClockSync`）、`Server/*`（除 `ServerRunner`）、`Protocol/{InputPacket,Messages,NetEntity,Snapshot,SnapshotBuilder,SnapshotCodec}`、`MessageType` 枚举和 `ProtocolInfo`；客户端 `View/*`、`Controls/*`、`UI/Hud.cs`、`UI/LobbyScreen.cs`、`Bootstrap/{BotDriver,Session}.cs`；`Tank.Server` 的 `RunOld`；Soak 的旧跑法。
- 新代码引用到的旧件只有 §8.1 列的那些：`ServerRunner`/`ITickServer`、`ClockSync`、`LoopbackNetwork`（用 `Tank.Core.Math.XorShift`）、`Channel`、`ProtocolInfo.DefaultPort`、`BitReader`/`BitWriter`/`ProtocolException`、`LanDiscovery`（发现请求仍是旧 `Messages.WriteDiscover`：字节 40 + 旧版本号 1）、两个传输的默认口令和服务器传输的 `IRoomInfoProvider` 参数。
- `View/Palette` 只剩旧 `Hud`/`LobbyScreen` 在用，界面颜色 4B T4 已并入 `Art/Palette`，跟着删就行。
- `ship.sh` 的 `smoke_test_macos` 找日志 “match started”：房主线程服务器的 “[server] match started …” 还对得上，但证明不了客户端进了对局（客户端打的是 “[match] entered match …”），改成两行都要。（调研时以为它已经坏了，T5 实跑发现服务器那行仍匹配，更正。）`ship.sh test` 只跑 Core/Net/Update 测试，没跑 Sim、Art。
- `Tank.Core.Math.XorShift.NextUInt` 与 `Tank.Sim.Geometry.XorShift.NextUInt` 算法逐字相同，`LoopbackNetwork` 换过去后丢包和抖动序列不变（soak 数字应不变）。

## 整理后的 `Tank.Net` 命名空间

| 命名空间 | 内容 |
|---|---|
| `Tank.Net.Protocol` | `BitReader`、`BitWriter`、`ProtocolException`、`Channel`（位流和通道，与玩法无关） |
| `Tank.Net.Transport` | `ITransport`（`IServerTransport`/`IClientTransport`/`ITransportHandler`/`TransportStats`）、`LiteNetServerTransport`、`LiteNetClientTransport`、`LoopbackNetwork`、`LanDiscovery` |
| `Tank.Net.Timing`（新） | `ServerRunner` + `ITickServer`、`ClockSync` |
| `Tank.Net.Battle` | 对局协议、服务器、客户端（不变） |

- `ProtocolInfo` 删掉：`DefaultPort` 挪到 `BattleProtocol.DefaultPort`（27015）；旧口令、旧版本号、旧名字长度删掉。
- 发现请求：`BattleMessage.Discover = 40`、`BattleMessages.WriteDiscover`（字节 40 + `BattleProtocol.Version`）。服务器传输只看第一个字节，所以 4B 的客户端发的 “40, 1” 照样能搜到 4C 的房间。`LanDiscovery` 只留对局房间：`DiscoveredBattleRoom` 改名 `DiscoveredRoom`，`BattleRooms` 改名 `Rooms`。
- `LiteNetServerTransport(int port, int maxConnections, string connectKey, Func<BitWriter, bool>? roomInfo = null)`、`LiteNetClientTransport(string host, int port, string connectKey)`：口令必填，去掉 `IRoomInfoProvider`。
- `ServerRunner(ITickServer, Action<string>?, int tickRate)`、`ClockSync(int tickRate)`：tick 频率必填。`ClockSync` 保留 4B T5 的“服务器追上客户端就硬重同步”。
- `LoopbackNetwork`：`XorShift` 换成 `Tank.Sim.Geometry.XorShift`（`Next(n)` 改成本地的 `NextBelow`，取模相同）；`Deliver` 不再每次 LINQ 分配，改用复用的列表并按（到达时间, 序号）排序。

## 任务

### T1 服务器和 Soak 只留新跑法
- `Tank.Server/Program.cs`：删 `RunOld`、旧选项（`--map`、`--stage`、旧模式名）、`Options` 包装；用法只列 `--port --mode teambattle|annihilation|ffa --bots --name`。不认识的模式名报错退出 2（`coop` 等旧名也是“unknown mode”）。
- `tests/Tank.Soak`：`Program.Main` 直接跑 `BattleSoak`；删旧场景、`--battle` 开关（仍接受并忽略，免得旧命令报错）。去掉对 `Tank.Server` 的无用引用。
- 两个 csproj 去掉 `Tank.Core` 引用。
- 验证：服务器 `--mode coop` 退出 2、`--mode ffa --bots 3` 起得来；soak 跑一遍，四种链路 PASS。

### T2 客户端删旧代码
- 删 `scripts/View/`、`scripts/Controls/`、`UI/Hud.cs`、`UI/LobbyScreen.cs`、`Bootstrap/BotDriver.cs`、`Bootstrap/Session.cs` 及对应 `.uid`。
- `project.godot` 删只有旧 `InputSampler` 用的输入动作 `move_up/down/left/right`、`fire`、`toggle_camera`；`scoreboard`、`toggle_netgraph` 保留（`MatchInput` 在用）。先 grep 确认没有新代码读这几个动作。
- `Tank.Client.csproj`、`Tank.Client.sln` 去掉 `Tank.Core`。
- 验证：客户端 0 警告；Art 测试全过；回环冒烟一次。

### T3 `Tank.Net` 删旧代码、整理命名空间
- 按上表删、挪、改名；`Tank.Net.csproj` 去掉 `Tank.Core`。
- 调用方跟着改：`BattleClient`、`BattleServer`、`BattleServerOptions`、`BattleProtocol` 注释、客户端 `LaunchOptions`/`Main.Flow`/`MenuScreen`/`LanBrowser`/`ThreadedHost`/`TickProbe`/`BattleSession`、`Tank.Server`、Soak、Art 测试。
- `Tank.Net.Tests`：删旧测试（`ClientPredictorTests`、`IntegrationTests`、`NetHarness`、`SnapshotCodecTests`、`Protocol/LegacyRoomInfoTests`、`ComponentTests` 里的 `ServerInputBufferTests`/`InterpolatorTests`、`ProtocolTests` 里的旧消息往返）；保留并挪位置：位流四条 → `Protocol/BitStreamTests.cs`，`ClockSyncTests` 四条 → `Timing/ClockSyncTests.cs`（显式 60 Hz）；新增：发现请求的字节（40 + 版本）、`LoopbackNetwork` 的到达顺序（同时到达按发送顺序）和同种子丢包序列不变、服务器传输只认发现字节。`csproj` 去掉 `Tank.Core`。
- 验证：三项目 0 警告，Net/Art 全过；soak 数字与 T1 相同。

### T4 删 `Tank.Core`
- 删 `src/Tank.Core`、`tests/Tank.Core.Tests`、`tools/genmaps.py`；`Tank.sln` 去掉两项。
- 纯度测试挪到 `tests/Tank.Sim.Tests/ArchitectureTests.cs`，检查 `Tank.Sim` 程序集：只引用 `System*`/`netstandard`/`mscorlib`；没有 `Random`/`DateTime`/`Stopwatch` 类型的字段；另加一条：`Tank.Sim` 里没有类型引用 `Godot` 命名空间（同一检查的程序集版本已覆盖，写成断言便于读）。
- 验证：`git grep -n "Tank\.Core"` 只剩文档里的历史记录；全部测试过。

### T5 CI 和发布
- `ci.yml`：测试列表换成 Sim、Art、Net、Update；soak 不变（现在跑的就是对局 soak）。
- `ship.sh`：`cmd_test` 跑 Sim、Art、Net、Update 测试 + soak（T4 已改测试列表，免得删 Core.Tests 后那个提交的 CI 跑不了）；`smoke_test_macos` 要求 “[server] match started” 和 “[match] entered match” 都有，`--quit-after 6`；成功提示改成新说法。
- 实跑 `ship.sh test` 发现：macOS 上临时 worktree 在 `$TMPDIR`（`/var` → `/private/var` 符号链接）下，NuGet 还原对不上被引用项目的路径，丢掉项目引用和传递来的包，`Tank.Art.Tests` 编译报 CS0012（缺 LiteNetLib）；worktree 改取物理路径（`pwd -P`）。
- 本地验证：用源码目录跑同一组参数（`godot-mono --headless --path src/Tank.Client -- …`），日志里有 “entered match”。`ship.sh test` 在本机跑一遍（它会建临时 worktree）。

### T6 文档
- 按新游戏改写 `docs/01-game-design.md`（第三人称坦克 PVP：四种车、点亮、可破坏物、三种模式、试驾场）、`02-tech-stack.md`（Godot 4 .NET、C#、LiteNetLib、30 Hz 浮点仿真）、`03-netcode.md`（权威服务器、按可见性发快照、预测与对账、插值、事件、重连、带宽、soak 数字）、`04-architecture-and-plan.md`（项目结构、`Tank.Sim`/`Tank.Net`/`Tank.Client` 分层、测试、阶段路线 1～6）、`docs/README.md`、根 `README.md`（运行、启动参数、键位、专用服务器、测试、发布）。内容以设计文档和代码为准，不写没做的东西。
- 设计文档 §8 末尾记 4C 的实际做法和偏差。

### T7 验收
- 三项目 0 警告；Sim、Art、Net、Update 全过；soak 四种链路 PASS。
- Godot：回环冒烟、结算冒烟、沙盒六张回归、两实例联机（`pair4b.sh`，`--join`，团队对战到结算回房）、局域网发现（两实例，客户端不带 `--join`，菜单列表里看到房间，截图）。
- 服务器：`dotnet run --project src/Tank.Server -- --mode ffa --bots 3` 起来，客户端 `--join 127.0.0.1` 进局。
- 审查：代码审查（删得干不干净、有没有误删新代码在用的、命名空间是否一致）+ 文档审查（与代码是否一致）。
- squash 合入 main，更新 PROGRESS。
