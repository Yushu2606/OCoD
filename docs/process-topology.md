# 运行时进程拓扑与角色库

本文对应工单 #5：运行时进程拓扑、库级守护进程与角色库管理窗口。当前阶段只有占位角色，不读取任何角色包；文件格式导入将在后续工单接入。

## 拓扑

```text
OCoD.Launcher（开发态启动器）
        │
        ▼
OCoD.GlobalDaemon（每个系统用户一个）
        ├── OCoD.CharacterHost（角色运行时 / 角色进程）
        ├── 管理窗口（运行在全局守护进程内）
        └── OCoD.LibraryDaemon（每个角色库一个，唯一写入方）
                    ▲
                    │ 同库客户端
                    └── OCoD.CharacterHost + 管理窗口
```

- 全局守护进程负责托盘/菜单栏入口、管理窗口、拉起角色运行时和按需拉起库级守护进程。
- 角色运行时负责单个角色窗口的状态，并通过库级守护进程读写角色库。
- 管理窗口同样是库级守护进程的客户端；空角色库也可以打开和操作。
- 库级守护进程是角色库的唯一持久化写入方。其他进程只发送 IPC 请求，不直接写库。
- 全局守护进程为每个已导入角色维护一个以角色 ID 为键的运行时进程；同一角色只运行一个实例，重复启动会聚焦现有窗口。空库时运行占位角色，首次导入后替换为真实角色。
- 角色导入、库布局与运行设置见 [角色导入与角色库](character-library.md)。

## 启动与停止

开发态入口是 `OCoD.Launcher`。它从构建产物或同一发布目录中找到其他组件，按需拉起全局守护进程并等待其控制端点就绪。

```bash
# 拉起全局守护进程、库级守护进程和占位角色运行时
dotnet run --project src/OCoD.Launcher

# 同时打开管理窗口
dotnet run --project src/OCoD.Launcher -- --open

# 查看拓扑
dotnet run --project src/OCoD.Launcher -- --status

# 只停止/重启角色运行时
dotnet run --project src/OCoD.Launcher -- --stop-runtime
dotnet run --project src/OCoD.Launcher -- --restart-runtime

# 重启库级守护进程（角色运行时会自动重连）
dotnet run --project src/OCoD.Launcher -- --restart-library-daemon

# 停止全局守护进程及其子进程
dotnet run --project src/OCoD.Launcher -- --stop
```

使用 AOT 发布目录时直接运行其中的 `OCoD.Launcher`；组件通过 `OCOD_COMPONENTS_DIR` 或同目录查找。

## 角色库位置

| 平台 | 运行时数据（日志、控制端点、全局锁） | 默认角色库 |
| --- | --- | --- |
| macOS | `~/Library/Application Support/OCoD` | `~/Library/Application Support/OCoD/Library` |
| Windows | `%LOCALAPPDATA%\OCoD` | 启动器同目录下的 `OCoDData\Library`（便携目录） |

`--data-dir`、`OCOD_DATA_DIR`、`--library`、`OCOD_LIBRARY` 可覆盖。开发态启动器将这些路径显式传给全局守护进程，组件之间不约定“可执行文件旁边”就是数据目录。

## 库级守护进程

- 同一角色库只允许一个库级守护进程。启动时通过 `.ocod/library.lock` 独占文件锁保证唯一性；第二个实例会连接已有守护进程并退出。
- 第一次打开库时创建 `.ocod/library.json`（内部库 schema v1）与 `.ocod/logs/library-daemon.log`。这些写入只由库级守护进程执行。
- 客户端与守护进程通过本地 Unix domain socket 交换 JSON 行请求。端点位于系统临时目录的 `ocod/` 下，由角色库路径哈希生成；目录权限限制为当前用户。
- 每个客户端连接保持到关闭为止。最后一个客户端断开后，守护进程先等待待提交写入完成，再在空闲宽限期后退出。
- 角色运行时与库级守护进程断线时会自动重连；重启库级守护进程不需要重启角色。

## 管理窗口

- 从 macOS 菜单栏或 Windows 通知区域的 OCoD 图标打开，也可以由启动器 `--open` 打开。
- 显示全局守护进程、库级守护进程和角色运行时的 PID/版本/状态，库路径，以及当前库客户端（角色运行时、管理窗口）。
- 管理窗口自身保持一个库客户端连接；空库时仍可用。
- 提供开发控制：启动/停止/重启角色、重启库级守护进程、关闭鼠标穿透。
- 管理窗口随全局守护进程的界面语言更新。

## 鼠标穿透

全局守护进程拥有托盘/菜单栏图标。角色窗口仍可从右键菜单开启鼠标穿透，状态通过控制通道上报；全局守护进程的托盘菜单可以再次关闭穿透。角色运行时单独启动（没有控制端点）时保留自己的托盘入口，便于 #4 的独立验收。

## 观察方式

1. 管理窗口：库路径、三个组件的 PID/版本/运行状态、库客户端列表。
2. 启动器 `--status`：同样的拓扑摘要。
3. 日志：`<运行时数据>/logs/global-daemon.log`、`logs/launcher.log`、`logs/character-runtime.log`，以及 `<角色库>/.ocod/logs/library-daemon.log`。

## 自动验收

```bash
# 开发态
dotnet run --project src/OCoD.Launcher -- --smoke-test

# AOT 发布目录
./artifacts/osx-arm64/OCoD.Launcher --smoke-test
```

该冒烟测试会启动真实的多进程拓扑，验证：

- 启动器拉起全局守护进程，全局守护进程拉起库级守护进程和角色运行时；
- 角色运行时和管理窗口都通过库级守护进程连接；
- 第二个库级守护进程不会成为写入方；
- 停止角色运行时后，最后一个客户端的库级守护进程自动退出；
- 按需重启角色运行时与库级守护进程；
- 全局守护进程可以开启/关闭角色鼠标穿透；
- 管理窗口可以打开，并与角色运行时共享同一个库级守护进程；
- 全局守护进程可独立停止，托管子进程一起退出。

真实拖动、窗口缩放、托盘点击和安装包切换仍按 [构建与运行](build-and-run.md) 的手工清单验收。
