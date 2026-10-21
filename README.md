# Tank 3D

第三人称坦克对战，支持局域网多人。四种车（轻坦、中坦、重坦、坦克歼击车），装甲和击穿、点亮和草丛、可以打穿的墙；三种模式（团队对战、团队歼灭、各自为战），一局最多 10 辆车，人不够用机器人补；另有单机试驾场。30 Hz 权威服务器、客户端预测、快照插值，没被点亮的敌人不会发给你。

> 游戏、名称、美术和地图都是原创，画面全部由代码生成。Battle City 只是仓库名：项目最早是它的 3D 重制，那套代码已经删除。设计文档见 [`docs/`](docs/README.md)。

## 进度

| 阶段（[docs/04 §6](docs/04-architecture-and-plan.md#6-阶段路线)） | 状态 |
|---|---|
| 1 单机试驾场 | ✅ |
| 1.5 画面：卡通风格、玩具坦克、高草丛、更多掩体 | ✅ |
| 2A 战斗核心 · 2B 车型 · 2C 可破坏物 | ✅ |
| 3 点亮 | ✅ |
| 4 联网：4A 联网核心 ✅、4B 客户端对局 ✅、4C 清理 | 🟡 |
| 5 5v5：基地占领、专用地图、会寻路的 AI | ⬜ |
| 6 吃鸡：毒圈、补给箱、大地图、16 人 | ⬜ |

## 目录

```
src/Tank.Sim      玩法仿真：纯 C#，不依赖引擎；30 Hz、浮点；车型、弹道、击穿、点亮、物件、对局规则、机器人
src/Tank.Net      协议、服务器、客户端预测和插值、LiteNetLib 与内存回环传输、局域网发现
src/Tank.Server   无头专用服务器
src/Tank.Update   在线升级（不依赖 Godot）
src/Tank.Client   Godot 4（.NET）工程：渲染、输入、菜单、房间、对局、试驾场
tests/            xUnit 测试（Sim、Net、Art、Update）和 Tank.Soak（网络压测）
tools/            发布脚本
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

主菜单：填名字，然后

- **开房间**：在本机开一个房间（服务器跑在本进程的独立线程里），进入房间界面；
- **局域网内的房间**：列表里选一个（或双击）加入；
- **输入地址加入**：填房主的 IP，端口不是 27015 时写成“地址:端口”；
- **试驾场**：单机打靶，不联网。

房间里：房主选模式和机器人数（0～9），每人选车型，两队模式可以换到对面，房主开局。

带机器人单人快速测试（参数写在 `--` 之后）：

```bash
godot-mono --path src/Tank.Client -- --host --bots 3 --autostart --mode teambattle --netgraph
```

常用启动参数（全表在 `src/Tank.Client/scripts/Bootstrap/LaunchOptions.cs` 文件头）：

| 参数 | 作用 |
|---|---|
| `--host` / `--join 192.168.1.20[:端口]` | 开房间 / 加入房间 |
| `--port N` | 开房或加入用的端口（默认 27015） |
| `--name 名字` | 玩家名 |
| `--mode teambattle\|annihilation\|ffa` | 开房时的模式 |
| `--bots N` | 机器人数，0～9 |
| `--autostart` | 房主人齐后自动开局（`--players N`：等够 N 个真人，默认 1） |
| `--tank light\|medium\|heavy\|td` | 车型：房间里连上后自动选；试驾场的开局车型 |
| `--sandbox` | 直接进试驾场 |
| `--netgraph` | 对局一开始就打开 F3 网络面板 |
| `--seed N`、`--match-seconds S`、`--loopback` | 固定对局种子、改对局时长、房主用虚拟网络（确定性测试） |
| `--no-update` | 不检查在线更新 |

试驾场也能直接启动：`godot-mono --path src/Tank.Client res://scenes/Sandbox.tscn`。

### 操作

WASD 开车 · 鼠标转镜头、炮塔跟着准星 · 左键开火 · 右键按住自由观察 · 1 / 2 穿甲弹 / 高爆弹 · Shift 狙击镜 · 滚轮镜头远近（狙击镜里切倍率）· Tab 计分板 · F3 网络面板 · Esc 暂停层。

阵亡后：Q / E 或左键切换观战对象，F 自由视角（各自为战）。结算页上房主按 Enter 立即回房间。试驾场：R 补弹，T 恢复物件，V 点亮调试，F1～F4 换车。完整键位见 [docs/01 §10](docs/01-game-design.md#10-操作)。

### 专用服务器

```bash
dotnet run --project src/Tank.Server -c Release -- --mode ffa --bots 3 --name "LAN party"
```

参数：`--port N`（默认 27015）、`--mode teambattle|annihilation|ffa`（默认团队对战）、`--bots 0..9`（开局时补进去）、`--name 房间名`。第一个连上的玩家是房主，在房间里开局。每 30 秒打一行状态，Ctrl+C 或 SIGTERM 退出。

### 局域网注意事项

- 需要放行 UDP **27015** 端口（或 `--port` 指定的端口）；首次运行时请在系统防火墙弹窗中允许。
- 房间通过 UDP 广播发现，只搜得到默认端口上的房间。访客 Wi-Fi 或开启了 AP 隔离的路由器会屏蔽广播，此时请直接输入地址加入。
- 建议使用有线网络。Wi-Fi 下插值延迟会自动调大，可按 F3 查看延迟、对账修正和丢包。

## 测试

```bash
dotnet test tests/Tank.Sim.Tests
dotnet test tests/Tank.Art.Tests
dotnet test tests/Tank.Net.Tests
dotnet test tests/Tank.Update.Tests
dotnet run --project tests/Tank.Soak -c Release   # 输出网络手感验收表（docs/03 §11），超标时退出码非零
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

`build` 会检查每个包里都有 C# 程序集，并在 macOS 上把导出的游戏真正启动一次（开房、机器人加入、开局、进入对局画面）才算成功。

## 在线升级

正式包启动后会在后台检查 [公开更新源](https://github.com/Murphycx94/BattleCity-updates)，有新版本就下载对应平台的完整安装包，
主菜单出现「重启并更新」；点了之后由一个小脚本等游戏退出、替换安装目录里的游戏文件（macOS 是整个 .app），再重新启动。

- **安全**：清单用 ECDSA P-256 签名，公钥嵌在游戏里（`src/Tank.Update/UpdateDefaults.cs`）；签名不对、包的 SHA-256 或大小不对、版本不比当前新，一律不装。
- **出错可退**：替换失败会把旧版本放回去并照常启动；装在不能写的位置（如需要管理员权限的目录）时只提示下载新版。
- **开发时不更新**：编辑器和源码运行不检查；正式包可加 `--no-update` 跳过。
- **本地测试更新源**：`TANK_UPDATE_FEED=http://127.0.0.1:8765/stable/` 指向自己起的静态服务器（回环地址允许 http，签名照样校验）。

发布时 `tools/ship.sh feed`（CI 里自动执行）把三平台客户端发到更新仓库的 Release，生成并签名 `stable/manifest.json`。
签名私钥在 `~/.config/tank3d/update_signing_key.pem`，**不进仓库**；丢了就只能换公钥，老版本玩家需要手动装一次新版。
