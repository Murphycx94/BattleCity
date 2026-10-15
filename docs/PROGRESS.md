# 开发进度

> 每完成一个小节点就更新本文件。中断后，先读这里，再从“下一步”接着做。

最后更新：2026-10-15

## 当前位置

- **阶段**：2A 战斗核心
- **分支 / 工作目录**：`feat/stage2a-combat`，位于 `/Users/murphy/Desktop/dev/BattleCity-stage2a`。从 main 的 `cd70856` 拉出
- **设计**：[docs/superpowers/specs/2026-10-14-stage2a-combat-design.md](superpowers/specs/2026-10-14-stage2a-combat-design.md)
- **计划**：[docs/superpowers/plans/2026-10-14-stage2a-combat.md](superpowers/plans/2026-10-14-stage2a-combat.md)（13 个任务，分 A～E 五批）
- **草稿**：写计划前先在临时工作目录 `/Users/murphy/Desktop/dev/BattleCity-stage2a-draft`（分支 `draft/stage2a-combat`，从 `85722f3` 拉出）把全部代码落地验证，每个提交对应计划里的一个任务。计划写完、执行完后删掉这个工作目录和分支
- **下一步**：批次 B（Task 5～8）

## 阶段总览

| 阶段 | 状态 | 说明 |
|---|---|---|
| 1 单机试驾场 | ✅ 已合入 main（`b0daefb`） | |
| 1.5 画面（卡通风格、玩具坦克、高草丛、更多掩体） | ✅ 已合入 main（`cd70856`，squash） | 用户已试玩确认 |
| 2A 战斗核心 | 🚧 进行中 | 本文件“当前位置” |
| 2B 车型（轻坦、重坦、坦克歼击车） | ⏳ 未开始 | |
| 2C 可破坏物（墙、栅栏、草丛、瓦砾、撞墙） | ⏳ 未开始 | |
| 3 点亮 | ⏳ 未开始 | |
| 4 联网 | ⏳ 未开始 | |
| 5 5v5 | ⏳ 未开始 | |
| 6 吃鸡 | ⏳ 未开始 | |

## 2A 节点记录

| # | 节点 | 状态 | 提交 | 备注 |
|---|---|---|---|---|
| 1 | 设计确认，写成文档 | ✅ | 本次提交 | 用户确认：静止靶车 + 10 秒复活、卡通大字命中提示、炮塔弹飞 + 焦黑 |
| 2 | 实施计划 | ✅ | 本次提交 | 草稿全部落地并截图验证；草稿审查无严重问题，修了“炮弹起点在装甲盒内算成跳弹”，补 5 个测试。最终 Sim 258、Art 187；spin、orbit 不限速 210～330 帧。计划代码由脚本从草稿各提交导出，并在干净目录里重放、与草稿逐字一致 |
| 3 | 按计划逐批实现 + 每批审查 | 🚧 | | 执行方式：逐任务派子代理，每批先规格审查、再代码质量审查 |
| 3A | 批次 A：Task 1～4（有向盒与随机数、装甲弹种数据、击穿规则、散布） | ✅ | `53c9d86` `377f730` `4c78357` `c2aa443`，审查跟进 `17ffa93` | Sim 185。代码与草稿逐字一致；规格审查通过；质量审查无严重问题，补了装甲盒尺寸、六个面映射、散布角度分布、旋转盒距离的断言（测试数不变）。推迟到 Task 8 之后：`TankSpec.Shell(ammo)` 改名 `Round(ammo)`（后续补丁引用旧名） |
| 4 | 帧率检查、最终截图 | ⏳ | | |
| 5 | 用户试玩、按反馈调整 | ⏳ | | |
| 6 | squash 合入 main | ⏳ | | |

## 工作约定（中断恢复时先看）

- 运行环境：`export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"`。Godot 命令是 `godot-mono`。
- Godot 可能卡住不退出，每次运行都要套强制结束：`godot-mono ... & P=$!; ( sleep 40; kill -9 $P ) & wait $P`。
- 截图和帧率：
  - 启动参数：`--camera-preset <front|side|hill|spin|close|closerear|sprocket|orbit|bush>`、`--screenshot <路径>`、`--screenshot-at 3`、`--quit-after <秒>`、`--fps-log`、`--autodrive`、`--fx-shot <秒>`、`--sniper`。
  - `--fx-shot` 要再加 `--quit-after 16`，否则来不及截图。
  - 测帧率时 macOS 会把后台窗口先限到 60 帧，前 7～20 秒的数据不算，至少跑 30 秒。
- 流程：
  - 每批改动先过“规格审查”，再过“代码质量审查”。
  - 截图由用户看，最后由用户试玩确认。
  - 合并到 main 用 squash。不推送远程，除非用户要求。
- 测试项目：`tests/Tank.{Core,Sim,Net,Update,Art}.Tests`。2A 开始前的数量是：Core 62、Sim 144、Net 50、Update 42、Art 178。

## 遗留问题和以后再做

- 阶段 3 点亮系统要用到的内容：
  - 线段对圆形草丛的视线遮挡计数（`Battlefield.BushCover`）；
  - “在草丛里”的判定：车体中心在 0.75 倍草丛半径以内；
  - 草丛被毁需要每个物件各自的存活状态；
  - `MathF` 在不同平台上结果是否一致。
- 狙击镜头开炮时，炮口的烟球显得过大（卡通改造时就记录了，还没处理）。
- 2A 计划“留给以后的问题”：跨平台浮点确定性（阶段 4 前决定）；`InputCmd` 弹种默认 AP，以后的 AI/回放/网络每 tick 都要带弹种；高爆溅射不考虑遮挡。
- 2A 试玩待确认：散布圈按真实比例画，第三人称下很小（约 2～15 px）。
- 场景里图元总数约 160 万，以后加东西时要注意。
