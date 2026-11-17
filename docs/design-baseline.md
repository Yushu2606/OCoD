# OCoD 首版设计基线

2026 年 11 月 17 日确认以下决策作为首版设计基线，本轮设计访谈结束。

- [格式兼容与角色分发](adr/0001-character-distribution-compatibility.md)
- [进程职责、安装和更新](adr/0002-process-boundaries.md)
- [首版平台与实机验收](adr/0003-first-release-platforms.md)
- [Windows 便携持久化](adr/0004-windows-portable-persistence.md)
- [macOS 角色库](adr/0005-macos-character-library.md)
- [首版桌面行为与作者编辑器](adr/0006-first-release-desktop-behavior.md)
- [洁净室交接](adr/0007-cleanroom-handoff.md)

[术语表](../GLOSSARY.md)和[原作行为记录](bug-clicker-behavior.md)提供背景。原作行为记录基于 ASAR 静态检查。洁净室交换规格与固定测试样本见[规格目录](specs/README.md)与[样本目录](../samples/README.md)；Windows 与 macOS 的实机界面验收已执行（Windows 有外壳与保存对话框限制；macOS 使用官方 1.4.6.app + Electron 44.2.0），结果见[普通包验收](../samples/VERIFICATION.md)与[受保护包验收](../samples/protected/VERIFICATION.md)。
