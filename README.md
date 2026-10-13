# Tank 3D

3D 重制版「坦克大战 / Battle City」玩法（Namco，1985），支持局域网多人。网络手感对标 CS 1.6：60Hz 权威服务器、客户端预测、快照插值、延迟补偿。

> 只借鉴原作玩法；本仓库中的名称、美术、地图和音频均为原创。设计文档见 [`docs/`](docs/README.md)。

## 进度

| 里程碑（docs/04 §5） | 状态 |
|---|---|
| M0 工程骨架、CI | ✅ |
| M1 单机核心：移动与贴格、砖墙/钢墙/水/树林/冰、子弹、AI、道具、老鹰 | ✅ |
| M2 联网基础：局域网发现、大厅、快照、插值 | ✅ |
| M3 CS 级手感：预测、对账、增量快照、时钟同步、延迟补偿、net_graph、网络模拟 | ✅ |
| M4 内容：3 种模式 ✅、断线重连 ✅、地图 3 张（目标 15 张以上） | 🟡 |
| M5 打磨：程序化低多边形美术 ✅、特效 ✅、镜头切换 ✅；音效、设置菜单、改键待做 | 🟡 |
| M6 发布：三平台导出、防火墙规则、录像 | ⬜ |

## 目录

```
src/Tank.Core     纯玩法仿真（无引擎、无网络、整数运算、种子随机数）+ 内置地图
src/Tank.Net      协议、增量编解码、服务器、客户端预测/插值、LiteNetLib 与内存回环传输
src/Tank.Server   无头专用服务器
src/Tank.Client   Godot 4（.NET）工程：渲染、输入、界面
tests/            xUnit 测试（Core、Net）和 Tank.Soak（8 个机器人在模拟网络下压测）
tools/genmaps.py  重新生成内置地图
```

## 环境

- .NET SDK 8.0.4xx（见 `global.json`）
- Godot 4.7.x **.NET 版**（macOS：`brew install --cask godot-mono`）

## 运行

```bash
# 用编辑器打开，按「运行」
godot-mono --editor --path src/Tank.Client

# 或直接运行
godot-mono --path src/Tank.Client
```

**开房**：选择模式 → *HOST GAME*。**加入**：在 *ROOMS ON THIS NETWORK* 里选房间，或直接输入房主 IP。

操作：WASD / 方向键移动 · J / 空格开火 · C 切换俯视/跟随镜头 · F3 网络面板 · Tab 计分板 · Esc 离开。

带机器人单人快速测试（参数写在 `--` 之后）：

```bash
godot-mono --path src/Tank.Client -- --host --bots 3 --autostart --mode coop --netgraph
# 模式：coop | basebattle | deathmatch    其他：--camera chase  --join 192.168.1.20
```

### 专用服务器

```bash
dotnet run --project src/Tank.Server -c Release -- --mode deathmatch --map arena-01 --name "LAN party"
```

### 局域网注意事项

- 需要放行 UDP **27015** 端口；首次运行时请在系统防火墙弹窗中允许。
- 房间通过 UDP 广播发现。访客 Wi-Fi 或开启了 AP 隔离的路由器会屏蔽广播，此时请直接输入 IP 加入。
- 建议使用有线网络。Wi-Fi 下插值缓冲会自动调整，可按 F3 查看。

## 测试

```bash
dotnet test tests/Tank.Core.Tests
dotnet test tests/Tank.Net.Tests
dotnet run --project tests/Tank.Soak -c Release   # 输出网络手感验收表
```

## 发布

版本号和一句话说明只写在 [`build.json`](build.json)。推到 `main` 时如果远端还没有 `v<version>` 这个 tag，
[Release 工作流](.github/workflows/release.yml) 会自动跑测试、打包并建 GitHub Release：

```bash
tools/ship.sh bump 0.2.0 "新增：……"   # 改 build.json 并提交
git push                               # CI 自动打包、发布
```

附件：三平台客户端（macOS 通用版、Windows x64、Linux x64）、四平台专用服务器（自带运行时）、`SHA256SUMS.txt`。
发布说明默认按模板生成；想自己写就放在 `docs/releases/v<version>.md`。

本机也能走同一套流程（需要 Godot 4.7.2 .NET 版和同版本导出模板）：

```bash
tools/ship.sh check                  # 发布前检查
tools/ship.sh build                  # 在当前提交的干净 worktree 里测试并打包到 build/release
DRY_RUN=1 tools/ship.sh github       # 只核对、打印将要发布的内容
tools/ship.sh github                 # 建 tag 和 Release（可重复运行；REPAIR=1 覆盖对不上的附件）
```

`build` 会检查每个包里都有 C# 程序集，并在 macOS 上把导出的游戏真正启动一次（开房、机器人加入、开局）才算成功。
