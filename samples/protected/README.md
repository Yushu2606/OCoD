# 受保护角色包固定样本

本目录是受保护 `.cvclk` 与锁定 `.cvpart` 的固定样本。契约见 [`docs/specs/protected-cvclk.md`](../../docs/specs/protected-cvclk.md)。

所有文件都是**自製样本**，由 `tools/cleanroom/samples.mjs` 生成；实现方可以使用。逐文件来源见 [`../PROVENANCE.md`](../PROVENANCE.md)。

## 1. 样本与口令

| 样本 | 头部 | 载荷 | 测试口令 | 主要验证点 |
| --- | --- | --- | --- | --- |
| `locked-no-owner.cvclk` | `locked=true` | 普通 | 无 | 不能再导出；无口令入口 |
| `locked-owner.cvclk` | `locked=true` + owner | 普通 | `cleanroom-locked-2026` | 无口令不可再导出；正确口令完全解锁 |
| `editlock.cvclk` | `locked=false` | `editLock=true` + 基线 | 无 | 可再导出；原文锁定；只能追加 |
| `editlock-recovery.cvclk` | `locked=false` | `lines` 仅新增、`editBase` 含原文 | 无 | 导入后原文自动补回；只能追加 |
| `editlock-owner-reexport.cvclk` | `locked=false` + owner | `editLock=true` + 基线 | `cleanroom-reexport-2026` | 无口令可导出但原文锁定；正确口令完全解锁 |
| `locked-owner-editlock.cvclk` | `locked=true` + owner | `editLock=true` + 基线 | `cleanroom-combined-2026` | 无口令双重锁定；正确口令完全解锁 |
| `locked-part.cvpart` | `locked=true` | 3 个头部部件 | 无 | 部件应用但不可再导出 |

**口令是公开的测试口令**，只用于验证恢复流程；不是作者的真实密码。

## 2. 预期导入结果

所有样本的角色图像都是 480×480 透明 PNG（`BODY`/`HEAD1`/`HEAD2` 标签），便于肉眼确认导入成功。角色名与作者信息见 `expected/protected/*.expected.json`。

### 2.1 `locked-no-owner.cvclk`

- 导入成功；角色名 `净室样本·不可再导出`。
- 导入警告：不能再导出。
- 角色 `locked=true`、`editLocked=false`、`authorLocked=true`、`protected=true`；图像本地为密封格式。
- 可正常播放、拖动、编辑非锁定字段；导出列表中该角色被禁用。
- 没有密码输入框。

### 2.2 `locked-owner.cvclk`

- 导入时出现密码输入框。
- 不输入 / 输入错误口令（如 `wrong-password`）：
  - 按收到状态导入：`locked=true`、`authorLocked=true`、图像密封。
  - 不能再导出。
- 输入 `cleanroom-locked-2026`：
  - 解锁导入：`owned=true`、`locked=false`、`editLocked=false`、`authorLocked=false`。
  - 图像以普通 PNG 保存；可以编辑作者信息并再导出。
  - 角色内容与不输入口令时相同。

### 2.3 `editlock.cvclk`

- 导入成功；角色名 `净室样本·锁定原文`。
- `locked=false`、`editLocked=true`、`authorLocked=true`。
- 本地台词为：
  - 点击：`原始点击一。`、`原始点击二。`、`新增点击三。`
  - 自语：`原始自语一。`、`新增自语二。`
- 原始点击/自语行只读；不能改文字、样式、台词音、排序、删除。
- 可以追加新台词；追加后保存成功。
- 可以再导出；再导出仍带 `editLock=true` 和同一 `editBase`，接收者仍只能追加。
- 名称、图片、部件、动作、声音、字体、拖拽等编辑入口被禁用或返回锁定提示。

### 2.4 `editlock-recovery.cvclk`

- 载荷的 `lines` 只有 `新增点击三。`，`idleLines` 只有 `新增自语二。`；`editBase` 含两条原始点击和一条原始自语。
- 导入后本地台词变为：
  - 点击：`原始点击一。`、`原始点击二。`、`新增点击三。`
  - 自语：`原始自语一。`、`新增自语二。`
- 即：基线不是当前台词前缀时，导入方把基线补回到前面。
- 原始行只读，新增行可继续追加。

### 2.5 `editlock-owner-reexport.cvclk`

- 无口令：`locked=false`、`editLocked=true`、`authorLocked=true`；可以再导出，但再导出仍锁定原文。
- 正确口令 `cleanroom-reexport-2026`：`owned=true`、`locked=false`、`editLocked=false`、`authorLocked=false`；完全可编辑/导出。
- 用于验证 owner 与 `locked` 独立：即使本来允许再导出，口令仍能解除编辑锁。

### 2.6 `locked-owner-editlock.cvclk`

- 无口令 / 错误口令：`locked=true`、`editLocked=true`、`authorLocked=true`；不能导出，原文只读。
- 正确口令 `cleanroom-combined-2026`：`locked=false`、`editLocked=false`、`authorLocked=false`；完全可编辑/导出。
- 用于验证“再导出限制 + 编辑锁 + 口令”三者组合。

### 2.7 `locked-part.cvpart`

- 导入到角色后，`headTop`/`headTop2`/`headTop3` 三个头部部件被应用。
- 部件 `Protected=true`、`Locked=true`；部件包不能再导出。
- 角色本身不一定 `locked`；但受保护部件不会出现在再导出的 `.cvpart` 中。
- 没有口令恢复。

## 3. 期望文件

`../expected/protected/` 下的 `.expected.json` 包含：

- 容器头部（`locked`，以及 `owner` 的 `s`/`h`）。
- 测试口令（`ownerCode`，仅测试用途）。
- 角色名、作者、台词、`editLock`、`editBase`、`editSty`、图像摘要。
- 部件包的槽位、设置、图像摘要。

实机步骤见 [`VERIFICATION.md`](VERIFICATION.md)。

## 4. 与普通样本的关系

受保护样本复用普通样本的容器、角色字段、图像格式和部件结构；区别只在：

- 头部 `locked` / `owner`。
- 载荷 `editLock` / `editBase` / `editSty`。
- 导入后的本地 `locked` / `editLocked` / `authorLocked` 标记。
- 导出、编辑和部件操作的权限限制。

普通包契约仍以 [`docs/specs/cvclk-v1.md`](../../docs/specs/cvclk-v1.md)、[`cvpart-v1.md`](../../docs/specs/cvpart-v1.md) 为准。
