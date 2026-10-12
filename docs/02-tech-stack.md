# 02 · 技术选型

## 1. 选型结论

| 层 | 选型 | 说明 |
|---|---|---|
| 引擎 / 渲染 | **Godot 4.x 稳定版（.NET 版）**，Forward+ 渲染器 | 只用它的渲染、输入、UI、音频；**不用**它的物理引擎和高层多人 API |
| 语言 | **C# 12，.NET 8 LTS 起** | 客户端、服务器、测试共用同一份代码 |
| 玩法仿真 | 自研 `Tank.Core`：纯 C# 类库，不依赖引擎 | 固定 60Hz 步长、整数坐标、轴对齐包围盒（AABB）网格碰撞 |
| 网络传输 | **LiteNetLib 2.x**（基于 UDP） | 提供 Unreliable / ReliableOrdered / Sequenced 三种通道；内置广播发现、MTU 探测、丢包和延迟模拟 |
| 序列化 | 自研位打包（BitWriter / BitReader）+ 快照增量压缩 | 包体最小、不产生 GC 分配；协议完全由我们控制 |
| 专用服务器 | .NET 控制台程序（无头，**不依赖 Godot**） | 体积小、启动快；开房的玩家可以在客户端进程里内嵌同一个服务器 |
| 资源管线 | MagicaVoxel / Blender 制作，导出 glTF 2.0 | 导入 Godot |
| 测试 | xUnit + FluentAssertions；Coverlet 统计覆盖率；自研网络压测机器人 | 见 [04 测试策略](04-architecture-and-plan.md#4-测试策略) |
| CI | GitHub Actions：构建、单测、覆盖率门禁、三平台导出 | |

> 已锁定版本：Godot 4.7.2（.NET 版）、.NET SDK 8.0.425（`global.json`）、LiteNetLib 2.1.4、xUnit 2.9.3（`Directory.Packages.props`）。

## 2. 为什么是 Godot + C#

1. **3D 需求不重**：同屏不到百个物体，加上几千块微砖（用 MultiMesh，一次绘制调用），Godot 4 的 Forward+ 渲染完全够用，无需 Unreal 级别的引擎。
2. **玩法逻辑必须与引擎解耦**：实现 CS 式的网络手感，要求客户端能「把仿真回滚到旧状态再重放若干 tick」。引擎内置的物理引擎做不到确定性回放，所以仿真必须是可控的纯代码。C# 库可以被 Godot 客户端和无头服务器同时引用，在 xUnit 中脱离引擎测试。
3. **场景和资源都是文本**：`.tscn` / `.tres` 是纯文本，git diff 友好，Claude Code 等 AI 工具能直接读写；Unity 的场景文件和 meta 文件对此很不友好。
4. **免费、MIT 协议、导出包小**：无授权费，桌面导出包体积在几十 MB 量级。
5. **C# 比 GDScript 更适合写网络代码**：有静态类型、`struct` / `Span<T>` 实现零分配、单元测试生态成熟、性能更高。

## 3. 为什么是 LiteNetLib，而不是 Godot 自带的 ENet 和 MultiplayerAPI

| 维度 | Godot MultiplayerSynchronizer / RPC | LiteNetLib + 自研同步 |
|---|---|---|
| 客户端预测和对账 | 无，需要自己写 | 自己写（反正都要写） |
| 快照增量压缩 | 无 | 自己控制 |
| 能否在无引擎的服务器中运行 | 需要 Godot 无头模式 | 纯 .NET 即可运行 |
| 局域网发现 | 需要自己用 PacketPeerUDP 实现 | 内置 `SendBroadcast` 和未连接消息 |
| 网络环境模拟 | 无 | 内置 `SimulateLatency` / `SimulatePacketLoss` |
| 单元测试 | 依赖引擎 | 纯 C#，可以在进程内同时启动服务器和客户端 |

Godot 的高层多人 API 适合回合制和休闲游戏，**做不出 CS 级别的手感**，因此只把 Godot 当作渲染层。

## 4. 备选方案对比

| 方案 | 优点 | 主要问题 | 结论 |
|---|---|---|---|
| **Godot 4 + C# + LiteNetLib** | 轻量、免费、文本资源、代码全掌控 | 网络同步需要自研（本文档已给出完整设计） | ✅ **采用** |
| Unity 6 + Netcode for Entities | 自带预测、插值和延迟补偿，最接近 CS 模型 | 依赖 DOTS / ECS，学习曲线陡；场景文件不便于文本协作；有授权政策风险 | 次选。团队已有 Unity DOTS 经验时可考虑 |
| Unity + FishNet / Mirror | 生态丰富、教程多 | 预测功能各家实现不一，深度定制时仍要改源码 | 不采用 |
| Unreal 5 | 网络复制框架成熟、画质顶级 | 对这个规模的游戏严重过重：C++ 编译慢、包体大、上手成本高 | 不采用 |
| Web（Three.js + TypeScript + WebRTC DataChannel） | 免安装，打开浏览器就能玩 | 浏览器不能做 UDP 广播，局域网发现需要另起信令服务器；GC 和后台标签页节流会造成卡顿；WebRTC 在局域网部署繁琐 | 不采用。如果「免安装」是硬需求再评估 |
| Bevy（Rust）+ lightyear | 性能好、ECS、lightyear 自带预测和回滚 | 生态尚不稳定，编辑器缺失，美术工作流弱 | 不采用 |

## 5. 同步模型选型：状态同步 vs 帧同步（回滚）

| | 权威服务器状态同步（**采用**） | 确定性帧同步 / 回滚（GGPO 式） |
|---|---|---|
| 代表作 | CS 1.6、Quake、Overwatch、Valorant | 格斗游戏、RTS |
| 自身操作的手感 | 预测后零延迟 | 回滚后零延迟 |
| 其他玩家的表现 | 插值，平滑但落后约 2～3 个 tick | 预测 + 回滚，人数多时容易抖动、穿帮 |
| 确定性要求 | 只要求自己的坦克能重放（**部分确定**即可） | **所有平台逐位一致**，一处浮点误差就会不同步 |
| 中途加入和断线重连 | 发一份完整快照即可 | 需要状态同步和追帧，复杂 |
| 作弊防护 | 服务器权威 | 客户端拥有全部状态 |
| 8 人 + AI 的扩展性 | 好 | 回滚成本随人数上升 |

**结论**：采用状态同步。同时让 `Tank.Core` 保持整数运算和种子化随机数，保留将来加回滚的可能，也支持「录制输入、回放复现」的调试手段。
