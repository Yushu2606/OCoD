# 构建、发布与运行

本文覆盖工单 #4（桌面角色外壳）、#5（进程拓扑、库级守护进程与管理窗口）和 #6（普通角色包导入），包括多库并行与角色库恢复。所有命令默认在仓库根目录执行。

## 先决条件

- .NET SDK 10（`global.json` 固定主版本为 10，`latestFeature` 允许安装的 10.x 补丁版本）。
- macOS：Xcode Command Line Tools（提供 `clang` 与原生链接器）。
- Windows：Visual Studio 2022/2026 的「使用 C++ 的桌面开发」工作负载，包含 MSVC 生成工具和 Windows SDK。Native AOT 需要本机链接器。

Native AOT 不支持从 macOS 交叉发布 Windows 版本，也不支持从 Windows 交叉发布 macOS 版本。双平台发布各自在对应平台上执行。

## 组件

| 组件 | 作用 |
| --- | --- |
| `OCoD.Launcher` | 开发态启动器；拉起全局守护进程并提供拓扑控制、状态查询和冒烟测试 |
| `OCoD.GlobalDaemon` | 每用户一个；托盘/菜单栏、管理窗口、监管角色运行时和库级守护进程 |
| `OCoD.CharacterHost` | 角色运行时；透明角色窗口、控制通道、库客户端 |
| `OCoD.LibraryDaemon` | 每角色库一个；唯一库写入方，客户端全部退出后自动退出 |

`OCoD.Formats`（容器与角色载荷）、`OCoD.LibraryClient`、`OCoD.Protocol`、`OCoD.Desktop`、`OCoD.Localization` 是共享类库，不单独启动。

## 构建与测试

```bash
dotnet build OCoD.slnx
dotnet test
```

## 开发态运行

### 完整拓扑

```bash
dotnet run --project src/OCoD.Launcher -- --open
dotnet run --project src/OCoD.Launcher -- --status
dotnet run --project src/OCoD.Launcher -- --stop
```

`OCoD.Launcher` 会按需启动全局守护进程，全局守护进程再拉起库级守护进程和角色运行时。启动器把组件路径、角色库路径、运行数据目录和语言显式传给子进程。

常用命令：

| 命令 | 作用 |
| --- | --- |
| `--open` | 启动缺失组件并打开角色库管理窗口 |
| `--status` | 输出全局守护进程、库级守护进程和角色运行时的 PID/版本/状态 |
| `--ensure-runtime` | 启动角色运行时（若未运行） |
| `--stop-runtime` / `--restart-runtime` | 单独停止/重启角色运行时 |
| `--restart-library-daemon` | 重启库级守护进程；角色运行时自动重连 |
| `--import <path>` | 导入普通 `.cvclk`，启动导入的角色并打印拓扑 |
| `--characters` | 列出角色库中的角色 ID、名称与起始大小 |
| `--start-character <id>` / `--stop-character <id>` | 单独启动/停止一个已导入角色 |
| `--activate-character <id>` | 聚焦一个正在运行的角色窗口 |
| `--stop` | 停止全局守护进程及其子进程 |
| `--smoke-test` | 运行完整进程拓扑冒烟测试 |

选项：

- `--library <path>` 或 `OCOD_LIBRARY`
- `--data-dir <path>` 或 `OCOD_DATA_DIR`
- `--components-dir <path>` 或 `OCOD_COMPONENTS_DIR`
- `--lang <tag>`（传给新启动的界面进程）

macOS 默认角色库在 `~/Library/Application Support/OCoD/Library`；Windows 默认在启动器同目录的 `OCoDData\Library`。详见 [进程拓扑](process-topology.md)。

### 导入普通角色包

```bash
dotnet run --project src/OCoD.Launcher -- --import samples/cvclk/plain-minimal.cvclk
dotnet run --project src/OCoD.Launcher -- --characters
```

管理窗口的「导入角色包…」按钮或 `Cmd/Ctrl+I` 使用系统文件选择器导入，并在成功后自动启动角色。细节与角色库布局见 [角色导入与角色库](character-library.md)。

### 单独运行角色外壳

```bash
dotnet run --project src/OCoD.CharacterHost
dotnet run --project src/OCoD.CharacterHost -- --lang ja
dotnet run --project src/OCoD.CharacterHost -- --smoke-test --lang ja
```

没有 `--control-endpoint` 时角色运行时自行创建托盘/菜单栏入口；由全局守护进程拉起时不创建第二个托盘图标。

## Native AOT 发布

四个可执行组件必须发布到同一个目录，启动器才能找到兄弟组件。

### macOS

```bash
for p in OCoD.Launcher OCoD.LibraryDaemon OCoD.CharacterHost OCoD.GlobalDaemon; do
  dotnet publish "src/$p/$p.csproj" -c Release -r osx-arm64 -o artifacts/osx-arm64
done
```

Intel Mac 将 `osx-arm64` 换成 `osx-x64` 并在 Intel 机器上执行。产物不依赖目标机器的 .NET 运行时：

```bash
./artifacts/osx-arm64/OCoD.Launcher --open
./artifacts/osx-arm64/OCoD.Launcher --status
./artifacts/osx-arm64/OCoD.Launcher --stop
```

### Windows

在 Windows 上执行：

```powershell
foreach ($p in @("OCoD.Launcher", "OCoD.LibraryDaemon", "OCoD.CharacterHost", "OCoD.GlobalDaemon")) {
  dotnet publish "src/$p/$p.csproj" -c Release -r win-x64 -o artifacts\win-x64
}
```

产物不依赖目标机器的 .NET 运行时：

```powershell
.\artifacts\win-x64\OCoD.Launcher.exe --open
.\artifacts\win-x64\OCoD.Launcher.exe --status
.\artifacts\win-x64\OCoD.Launcher.exe --stop
```

## 自动冒烟测试

### 进程拓扑

```bash
# 开发态
dotnet run --project src/OCoD.Launcher -- --smoke-test

# macOS AOT 发布目录
./artifacts/osx-arm64/OCoD.Launcher --smoke-test

# Windows AOT 发布目录（ExitCode 0 为通过；日志见临时目录）
.\artifacts\win-x64\OCoD.Launcher.exe --smoke-test
$LASTEXITCODE
```

测试会启动真实的多进程拓扑并检查：启动器 → 全局守护进程 → 角色运行时、库级守护进程唯一性、库客户端共享、最后客户端退出后库级守护进程自动退出、角色运行时和库级守护进程重启、鼠标穿透远端恢复、管理窗口打开、全局守护进程停止。测试使用临时数据目录和角色库，不会改动默认角色库；结束时打印产物目录。

### 角色外壳

```bash
./artifacts/osx-arm64/OCoD.CharacterHost --smoke-test --lang en
```

检查透明窗口、右键菜单、翻转、锁定、置顶、鼠标穿透开关、窗口尺寸变化和四种界面语言。结果同时写入系统临时目录下的 `ocod-smoke-test.log`。

## 手工验收清单

### 角色外壳

启动后应能看到透明、无边框的占位角色浮窗（桌面背景可见），并确认：

- [ ] 拖动角色区域可移动窗口。
- [ ] 拖动窗口边缘或四角可缩放。
- [ ] 右键菜单的「水平翻转」镜像角色图形，但不镜像说明文字和菜单。
- [ ] 「锁定位置与大小」开启后不能拖动或缩放；再次点击恢复。
- [ ] 「保持在最前」切换窗口置顶。
- [ ] 「鼠标穿透」开启后点击落到桌面；通过系统托盘（Windows 通知区域、macOS 菜单栏）的同名菜单项恢复。
- [ ] 使用 `--lang` 或 `OCOD_LANG` 启动时，至少一个占位界面以对应语言显示。
- [ ] 以 `zh-TW`、`zh-HK` 等系统区域启动时显示简体中文。

### 角色导入与持久化

- [ ] 从管理窗口导入 `samples/cvclk/plain-minimal.cvclk`，角色进入角色库并出现在桌面。
- [ ] 管理窗口列出角色名与运行状态，可启动/停止；重复启动不会产生第二个进程。
- [ ] 拖动/缩放角色，切换翻转与置顶，停止并重新启动该角色后大小、位置、翻转和置顶恢复。
- [ ] 导入 `samples/cvclk/plain-multi.cvclk` 后三个角色都进入角色库；缺少 `name` 的角色显示包内 `id`。
- [ ] 导入 `samples/cvclk/plain-full.cvclk` 后能看到多图层合成（BACK1/BODY/HEAD1/SIDE1/EX1–EX4 等正常状态图层）。
- [ ] macOS 上角色库位于 `~/Library/Application Support/OCoD/Library`；Windows 上位于启动器目录的 `OCoDData\Library`；移动 Windows 分发目录后角色仍在。
- [ ] 受保护包与旧版 JSON 当前返回导入失败（后续工单）。

### 进程拓扑与管理窗口

- [ ] macOS 菜单栏或 Windows 通知区域出现 OCoD 图标；从该入口可打开角色库管理窗口。
- [ ] 空角色库时管理窗口可用，显示库路径、全局守护进程、库级守护进程、角色运行时的 PID/版本/状态以及库客户端列表。
- [ ] 管理窗口和角色运行时都显示为同一个库级守护进程的客户端。
- [ ] 关闭管理窗口和角色运行时后，库级守护进程在等待写入完成后退出；`--status` 中显示为停止。
- [ ] 管理窗口的启动/停止/重启角色、重启库级守护进程、关闭鼠标穿透按钮可用。
- [ ] 库 A 运行时，用同一 `--data-dir` 打开库 B：两个管理窗口标题分别带各自的库目录名，两个库级守护进程 PID 不同。
- [ ] 从库 B 导入并启动角色后，库 A 的角色仍可交互；关闭库 B 的管理窗口并停止其运行时后，库 A 的管理窗口和角色仍可用。
- [ ] 普通导入 `samples/protected/editlock.cvclk`、`editlock-owner-reexport.cvclk` 等受保护包时整文件被拒绝，错误明确说明受保护包不支持。
- [ ] 从托盘关闭角色鼠标穿透后，角色窗口恢复可点击。
- [ ] 托盘语言菜单切换语言时，管理窗口和角色窗口同步更新；角色名、台词等作者内容不翻译。
- [ ] 移动 Windows 分发目录后，角色库随 `OCoDData` 一起移动（安装包与迁移工单接入后复验）。

## 当前验证记录

- macOS arm64：解决方案构建与单元测试通过；四个组件均完成 Native AOT 发布；开发态与 AOT 拓扑冒烟测试全部通过；AOT 管理窗口已截图确认（简体中文、日文），菜单栏显示 `OCoD` 与托盘图标；鼠标穿透可由全局守护进程远端开启/恢复；第二个库级守护进程会退出，最后客户端离开后库级守护进程自动退出。
- macOS arm64 角色导入：开发态与 AOT `--import` 均通过；`plain-minimal`、`plain-multi`、`plain-full` 导入成功，多角色/名称回退/图层合成与起始大小已截图确认；管理窗口列出、启动、停止角色；通过菜单修改翻转、拖动边缘缩放后设置写回，停止再启动恢复大小/翻转/置顶；多角色同时运行且重复启动同一角色不新增进程。
- macOS arm64 多库、受保护包与恢复：开发态与 AOT 均验证库 A 运行时打开库 B、从 B 导入并启动角色、关闭 B 管理窗口后 A 仍可重启运行时并交互；两库库级守护进程 PID 不同；拓扑冒烟新增的多库检查通过。`editlock.cvclk`、`editlock-owner-reexport.cvclk` 等 6 个受保护样本通过普通导入均整文件返回 Protected；`editBase`/`editSty` 单独存在不误拒。角色库索引提交失败回滚与启动时索引对账由 5 项 LibraryDaemon 测试覆盖。
- Windows 10 22H2（19045，x64）：全部组件完成 `win-x64` 托管编译（0 警告 0 错误）；SSH 会话中的拓扑冒烟通过；交互式桌面会话中的角色外壳冒烟通过（透明窗口、四语切换、翻转/锁定/置顶/缩放、鼠标穿透开关）。
- Windows 10 22H2 新测试机（DESKTOP-ETJJIIM）：Release 构建 0 警告 0 错误，63 项测试全部通过（含 Windows `UnauthorizedAccessException` 提交失败分支）；托管发布 `dist-a` / `dist-b` 双分发目录验证：A 占位运行中，B 导入 `plain-minimal.cvclk` 并启动角色，两库库级守护进程 PID 不同；关闭 B 管理窗口后 A `--restart-runtime` 成功启动新 PID；`editlock-owner-reexport.cvclk` 被整文件拒绝。
- Windows 10 Native AOT（旧测试机，单库基线）：使用 Visual Studio 2026 的 MSVC 14.52 工具链，四个组件均发布到 `win-aot`；AOT 拓扑冒烟以退出码 0 通过，AOT 导入 `plain-minimal.cvclk` 后角色窗口以 160×160 显示实际角色图层，管理窗口列出角色与运行状态。通过 `--set-settings` 保存 200×180、水平翻转和置顶后停止并重新启动角色，窗口尺寸变为 200×180，状态报告翻转/置顶已恢复，`settings.json` 与截图一致。通知区域图标在溢出区域确认（本机交互桌面会话处于锁定状态，窗口用 `PrintWindow` 截图；全屏抓取不可用）。
- Windows 10 Native AOT（新测试机 DESKTOP-ETJJIIM，最终 `9bf0064`）：VS Build Tools 2026 18.12.12405.243，MSVC 14.52.37025，Windows SDK 10.0.26100.0。四个组件重发布成功（BuildTools 未被 vswhere 登记，使用 `vcvars64` 环境加 `-p:IlcUseEnvironmentalTools=true`）；AOT 拓扑冒烟 27 项全部通过。AOT `dist-a`/`dist-b` 双分发目录：A 占位运行中，B 导入 `plain-minimal.cvclk` 并启动角色，两库级守护进程 PID 不同（21088 / 4428）；关闭 B 管理窗口后 A `--restart-runtime` 成功启动新 PID（14296），B 角色仍运行；`editlock-owner-reexport.cvclk` 被 AOT 整文件拒绝。
- macOS Intel（`osx-x64`）：尚未在 Intel 机器上执行。

## 排查

- 先运行 `OCoD.Launcher --status`。若全局守护进程未运行，用 `--open` 或默认命令启动。
- 日志：
  - `<运行时数据>/logs/global-daemon.log`
  - `<运行时数据>/logs/launcher.log`
  - `<运行时数据>/logs/character-runtime.log`
  - `<角色库>/.ocod/logs/library-daemon.log`
- IPC 端点位于系统临时目录的 `ocod/` 下，按路径哈希命名；正常退出会删除对应 socket 文件。
- 鼠标穿透后无法通过角色窗口菜单恢复：这是预期行为，使用全局守护进程的托盘/菜单栏入口。
- AOT 发布报缺少链接器：确认 Windows 安装了 C++ 生成工具和 Windows SDK，macOS 安装了 Xcode Command Line Tools。
- Windows 上确认 MSVC 与 Windows SDK 已安装，但 NativeAOT 仍报 `Platform linker not found`（常见于 BuildTools 未被 vswhere 登记）：先在 `vcvars64.bat` 环境中运行 `dotnet publish`，并加 `-p:IlcUseEnvironmentalTools=true`，让 NativeAOT 使用 `PATH` 中的 `link.exe`/`lib.exe` 与 `LIB` 中的 SDK 库。
- 首版只交付 Windows 与 macOS；Linux 不在当前范围。
