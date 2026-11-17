# OCoD

[English](README.md) | 简体中文 | [日本語](README.ja.md) | [한국어](README.ko.md)

OCoD（Original Character on Desktop）是以 Bug Clicker 为行为参考的洁净室桌面角色项目。
设计基线见 [docs/design-baseline.md](docs/design-baseline.md)，领域术语见 [GLOSSARY.md](GLOSSARY.md)。

本仓库只接收分析方提供的规格与固定测试样本，不包含原作代码、原作解包内容或原作素材。

## 仓库结构

| 路径 | 内容 |
| --- | --- |
| `src/OCoD.Launcher` | 开发态启动器：拉起全局守护进程、管理拓扑、运行拓扑冒烟测试 |
| `src/OCoD.GlobalDaemon` | 每用户全局守护进程：托盘/菜单栏、管理窗口、组件监管 |
| `src/OCoD.CharacterHost` | 角色运行时：透明占位浮窗、窗口菜单、控制通道 |
| `src/OCoD.LibraryDaemon` | 每角色库一个的库级守护进程，角色库的唯一写入方 |
| `src/OCoD.LibraryClient` | 库客户端与库级守护进程监管器 |
| `src/OCoD.Formats` | `.cvclk` 容器与普通角色载荷解析 |
| `src/OCoD.Protocol` | IPC 契约、路径解析、子进程与单实例基础设施 |
| `src/OCoD.Desktop` | 角色运行时与全局守护进程共享的 Avalonia 资源 |
| `src/OCoD.Localization` | 四语资源接入点：简体中文、英文、日文、韩文及中文区域回退 |
| `tests/` | 本地化、IPC 契约与 `.cvclk` 容器/导入测试 |
| `docs/adr` | 首版设计决策 |

## 当前实现状态

工单 #4（桌面角色外壳）、#5（进程拓扑、库级守护进程与管理窗口）与 #6（普通角色包导入与持久化）已实现：

- 同一解决方案提供 Windows 与 macOS 的 Native AOT 发布目标；目标机器无需安装 .NET 运行时。
- 透明无边框角色浮窗，支持拖动、边缘缩放、水平翻转、位置与大小锁定、置顶和鼠标穿透。
- 普通 `.cvclk` 容器解密、gzip 解压、角色字段与图像解析；多角色包逐个导入，失败角色不影响其他角色。
- 角色按本地 ID 存入角色库；管理窗口可导入、列出、启动和停止角色；导入后角色自动显示在桌面。
- 大小、位置、翻转、置顶与锁定按角色保存，重启后恢复；同一角色重复启动只聚焦现有窗口。
- 每个角色库只有一个库级守护进程，它统一提交所有写入；角色运行时和管理窗口通过 IPC 访问角色库。
- 最后一个客户端断开且待提交写入完成后，库级守护进程自动退出；角色运行时可在管理窗口独立停止、启动和重启。
- 全局守护进程提供 macOS 菜单栏/Windows 通知区域图标和角色库管理窗口；空库可用，并可观察各进程 PID、版本、状态和库客户端。
- 同一个全局守护进程可并行服务多个角色库：每个库有独立的库级守护进程、运行时与管理窗口；托盘子菜单按库打开，关闭一个库不影响其他库。
- 简体中文、英文、日文、韩文资源接入点，任意中文区域回退到简体中文；全局守护进程可把界面语言变化下发到角色运行时。
- 受保护包、旧版 JSON、部件包/台词文件、点击闲置动画与台词、再导出、安装更新和编辑器属于后续工单。

## 快速开始

```bash
dotnet test

dotnet run --project src/OCoD.Launcher -- --open   # 启动拓扑并打开管理窗口
dotnet run --project src/OCoD.Launcher -- --status
dotnet run --project src/OCoD.Launcher -- --stop

dotnet run --project src/OCoD.Launcher -- --import samples/cvclk/plain-minimal.cvclk
dotnet run --project src/OCoD.Launcher -- --characters

dotnet run --project src/OCoD.Launcher -- --smoke-test
```

单独验收角色浮窗：

```bash
dotnet run --project src/OCoD.CharacterHost -- --smoke-test --lang ja
```

Native AOT 发布需要目标平台的本机工具链，且不能跨平台交叉发布。四个组件应发布到同一目录：

```bash
for p in OCoD.Launcher OCoD.LibraryDaemon OCoD.CharacterHost OCoD.GlobalDaemon; do
  dotnet publish src/$p/$p.csproj -c Release -r osx-arm64 -o artifacts/osx-arm64
done

./artifacts/osx-arm64/OCoD.Launcher --smoke-test
```

完整命令、先决条件与验收步骤见 [docs/build-and-run.md](docs/build-and-run.md)，进程拓扑与数据目录见
[docs/process-topology.md](docs/process-topology.md)，角色导入与角色库布局见
[docs/character-library.md](docs/character-library.md)，本地化约定见 [docs/localization.md](docs/localization.md)。
