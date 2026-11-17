# 受保护包实机验收清单（第 2 张票）

状态：**Windows 已执行（2026-11-17，有外壳与保存对话框限制）；macOS 已执行（2026-11-17，官方 1.4.6.app + Electron 44.2.0，原生保存对话框；原件未改动）**。模块级预验证已通过。

把本页交给执行者即可。样本位于本目录；每步在对应平台列记录结果。

## 1. 机器与准备

| 平台 | 机器 | 原作版本 | 记录项 |
| --- | --- | --- | --- |
| Windows | Windows 10/11 x64 | Bug Clicker **1.19.6** | OS 版本、应用版本、记录人、日期 |
| macOS | macOS 13+ | Bug Clicker **1.4.6** | OS 版本、应用版本、记录人、日期 |

每台机器：

- 使用全新数据目录。
- 准备截图文件夹，命名 `平台-步骤号-说明.png`。
- 测试口令是公开的：`cleanroom-locked-2026`、`cleanroom-reexport-2026`、`cleanroom-combined-2026`；错误口令用 `wrong-password`。
- 只使用本目录的自製样本；不要使用 `samples/verification/`（#1 的分析方对照夹具）。

## 2. 模块级预验证（已完成，仅供对照）

分析方已用两平台原作的导入/导出模块验证：

- `locked-no-owner`、`locked-owner`（无/错误口令）导入为 `locked=true`，导出被拒绝。
- `locked-owner` 正确口令导入为 `owned=true`、`locked=false`、`editLocked=false`、`authorLocked=false`；图像以普通文件保存。
- `editlock` / `editlock-recovery` 导入为 `editLocked=true`，基线长度与原文一致；再导出保留 `editLock` 与基线。
- `editlock-owner-reexport` 无口令为 `editLocked=true` 但可导出；正确口令完全解锁。
- `locked-owner-editlock` 无口令为 `locked=true` + `editLocked=true`；正确口令完全解锁。
- `locked-part.cvpart` 三个部件应用为 `Locked=true`，部件包再导出被拒绝。
- `editFilter` 拒绝修改基线，只接受追加；允许的标量字段与自定义提醒组按契约保留。

该结果不能替代界面验收。

## 3. 执行清单

| # | 操作（样本相对本目录） | 期望结果 | Windows | macOS |
| --- | --- | --- | --- | --- |
| 1 | 启动原作，确认版本 | 1.19.6 / 1.4.6 | 通过 | 通过 |
| 2 | 导入 `locked-no-owner.cvclk` | 导入成功；提示不能再导出；无密码输入框；导出列表该角色禁用 | 通过 | 通过 |
| 3 | 尝试导出步骤 2 的角色 | 被拒绝/不可选；提示“다시 내보낼 수 없도록 보호된 캐릭터입니다.” | 通过 | 通过 |
| 4 | 导入 `locked-owner.cvclk`，先输入 `wrong-password` | 出现密码框；错误提示；按收到状态导入；`locked`、原文可读、不能再导出 | 通过 | 通过 |
| 5 | 重新导入 `locked-owner.cvclk`，输入 `cleanroom-locked-2026` | 提示密码正确；角色完全解锁；作者信息可改；可导出 | 通过 | 通过 |
| 6 | 导入 `editlock.cvclk` | 3 条点击/2 条自语；原始行只读；名称/图片/部件/动作等入口禁用 | 通过 | 通过 |
| 7 | 在步骤 6 角色中追加一条新点击台词并保存 | 新台词保存成功；原始 3 条不变；再打开仍只读原始行 | 通过 | 通过 |
| 8 | 再导出步骤 7 角色，再导入新库 | 仍为编辑锁定；`editBase` 仍锁原始 3 条；新台词保留 | 通过 | 通过 |
| 9 | 导入 `editlock-recovery.cvclk` | 本地台词自动补回：原始点击一/二 + 新增点击三；原始自语一 + 新增自语二；原文只读 | 通过 | 通过 |
| 10 | 导入 `editlock-owner-reexport.cvclk`，不输入口令 | `editLocked=true` 但可以再导出；再导出仍锁定原文 | 通过 | 通过 |
| 11 | 重新导入 `editlock-owner-reexport.cvclk`，输入 `cleanroom-reexport-2026` | 完全解锁；作者信息可改；可编辑图片/部件/动作 | 通过 | 通过 |
| 12 | 导入 `locked-owner-editlock.cvclk`，不输入口令 | 双重锁定：不能再导出 + 原文只读 | 通过 | 通过 |
| 13 | 重新导入 `locked-owner-editlock.cvclk`，输入 `cleanroom-combined-2026` | 完全解锁；可导出 | 通过 | 通过 |
| 14 | 导入 `locked-part.cvpart` 到任一角色 | 3 个头部部件应用；部件页提示受保护部件不可再导出 | 通过 | 通过 |
| 15 | 尝试导出步骤 14 角色的 `.cvpart` | 受保护部件被排除；若无其他可导出部件则拒绝并提示 | 通过 | 通过 |
| 16 | 记录每个角色的截图与提示 | 平常/锁定提示/密码框/台词只读/部件页 | 通过 | 通过 |

## 3.1 Windows 执行结果（2026-11-17）

环境、截图与限制见分析方私有证据（仓外，不在仓内）。

- 全部 16 步通过。导入用真实设置页导入表、密码框和按钮；部件导入通过应用 IPC（与部件页相同管线）。
- 导出使用应用副本中的保存对话框桩；`editlock` 再导出文件确认 `editLock:true` 与 `editBase` 保留。
- 正确口令后图像以 `.png` 保存；错误/无口令为 `.bcp`。
- 锁定角色卡片 `disabled`；混合选择只导出未锁定角色。
- 发现：`editLocked` 再导出确认表显示“收件人的编辑：全部可以”，与实际文件 `editLock:true` 不一致；编辑器侧栏 `inert=true`、提示正确。
- 应用代码 1.19.6 ASAR，运行在 1.19.5 Electron 外壳上。

### 3.1.1 Windows 结项限制（#2/#3 记录用）

- 应用代码是 **1.19.6 ASAR**，Electron 外壳为机器自带的 **1.19.5**；关于页显示 v1.19.6，但不是官方 1.19.6 安装包/外壳。
- 导入确认表、密码框、按钮、台词页、编辑器是真实 UI；文件选择框用路径绕过；导出保存使用应用副本中的 `showSaveDialog` 桩，**原生保存对话框未手动操作**。
- #2/#3 的 Windows 结项记录应写“1.19.6 应用代码 + 1.19.5 Electron 外壳 + 保存对话框桩”，不能写成“官方 1.19.6 + 纯鼠标/原生对话框”验收。
- 这些限制不改变 16/16 行为结论；macOS 官方 1.4.6.app 已补做，见 3.2。

## 3.2 macOS 执行结果（2026-11-17）

环境、截图与发现见分析方私有证据（仓外，不在仓内）。

- 全部 16 步通过；导入/导出走真实设置页确认表、按钮、密码框，台词与部件走真实台词页/配件页。
- 使用 **官方 `/Applications/BUG Clicker.app` 1.4.6**（Electron 44.2.0，Universal，app.asar SHA-256 `fcf5…bcf5`）；原件未改动，`userData` 指向 `/tmp` 隔离目录。
- 导出保存使用真实原生 NSSavePanel（System Events 设置目录/文件名并点击“保存”）；导入使用原生 NSOpenPanel；少数从 `/tmp` 再导入的文件通过应用 IPC `importChars(paths)` 传入路径，应用代码未改。
- 界面语言为 zh-CN；清单中的韩文提示显示为中文对应文案。
- 正确口令后图像为 `.png`，错误/无口令为 `.bcp`；无口令 `editlock-owner-reexport` 可导出但 `editLock` 保留；受保护 `.cvpart` 导出被拒绝并提示“这个配件受保护，无法再次导出。”。
- 发现：`editLocked` 再导出确认表同样显示“收件人的编辑：全部可以”，而载荷为 `editLock:true`；与 Windows 发现一致。

## 4. 每步至少记录

- 截图或录像（密码框、导入提示、只读台词、部件页、导出禁用）。
- 角色名、导入后是否 `locked`/`editLocked` 的可见表现。
- 再导出文件路径与再导入结果。
- 错误口令与正确口令的界面文案。

## 5. 失败时贴什么

- 平台、原作版本、OS 版本、样本文件名与测试口令。
- 操作步骤与复现步骤。
- 实际界面提示或截图。
- 期望结果与契约位置（`docs/specs/protected-cvclk.md` 节号）。
- 是否在另一平台同样失败。

## 6. 完成标准

- [ ] Windows 1.19.6 清单全部通过。
- [ ] macOS 1.4.6 清单全部通过。
- [ ] 三种口令样本的正确/错误路径均记录。
- [ ] `editLock` 的追加、再导出、再导入均记录。
- [ ] 受保护 `.cvpart` 的导入与导出拒绝均记录。
- [ ] 失败项已修复并重测，或记录为原作已知限制。
