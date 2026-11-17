# 旧版 `carnovella-clicker` JSON

Bug Clicker 1.19.6 / 1.4.6 的导入器除 `bug-clicker` v1 外，还接受 `format` 为 `carnovella-clicker` 的角色 JSON。本文件定义该旧版格式的可观察契约与已知证据缺口。

## 1. 接受形式

原作接受两种形式：

1. **纯 JSON 文本**：UTF-8，可带 BOM，顶层是 JSON 对象。
2. **`BCVCLK2` 容器**：载荷 `format` 为 `carnovella-clicker`。原作导入器先解容器，再检查 `format`，所以这种形式也会被接受；但原作不会导出这种形式。

本契约要求实现方：

- 读取 `.cvclk` 时先检查 `BCVCLK2` 魔数；有魔数按容器解包，无魔数按 UTF-8 JSON 读取。
- 两种形式都按本文件校验。
- OCoD **不得**导出 `carnovella-clicker`；统一导出 `bug-clicker` v1（见 [ADR 0001](../adr/0001-character-distribution-compatibility.md)）。

## 2. 顶层结构

```json
{
  "format": "carnovella-clicker",
  "version": 1,
  "characters": [ Character, ... ]
}
```

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `format` | string | 是 | 必须精确等于 `carnovella-clicker` |
| `version` | number | 否 | 原作不检查；可缺失 |
| `characters` | array | 是 | 至少 1 个角色对象 |
| `app` | string | 否 | 旧版应用版本；原作不检查 |
| `exportedAt` | string | 否 | 旧版导出时间；原作不检查 |

- 顶层未知字段忽略。
- `characters` 不是数组或为空 → 拒绝，提示文件内没有角色。
- 单个角色失败不影响其他角色。

## 3. 角色对象

原作 1.19.6 的导入器对 `carnovella-clicker` 与 `bug-clicker` **使用同一套角色字段映射**：

- 角色对象字段、默认值、范围、图像回退、声音、字体、部件、气泡、宠物等规则，全部按 [cvclk-v1.md](cvclk-v1.md) 第 2–14 节处理。
- 图像既可以是 `images` 中的 data URL，也可以是 `imageUrls` 中的 HTTP(S) URL。
- 不存在旧版专用的顶层字段或角色字段映射分支。

因此，实现方可以把旧版 JSON 视为“顶层 `format` 不同、其余同 v1”的角色载荷。

### 3.1 与 v1 的差异

| 项目 | `bug-clicker` v1 | `carnovella-clicker` |
| --- | --- | --- |
| 顶层 `format` | `bug-clicker` | `carnovella-clicker` |
| 外层容器 | 通常 `BCVCLK2` | 通常无容器（纯 JSON） |
| 角色对象 | v1 角色对象 | 同一映射；缺失字段用默认 |
| 导出 | OCoD 统一导出 v1 | 不导出 |

### 3.2 旧版可能缺失的字段

旧版文件可能缺少后来新增的字段，例如：

- `bubbleStyle`、`bubbleStyles`、`lineStyles`
- `pet*`、`drag*`、`lift*`
- `beatMotion`、`keyMotion`
- `ol*`、`neck*`/`floor*` 的后期字段
- `rig`、`rigCommon`、`parts`
- `lineSounds`

这些字段缺失时按 [cvclk-v1.md](cvclk-v1.md) 的默认值处理。实现方**不得**因缺失字段拒绝导入。

## 4. 已知证据缺口

1. 在 1.19.6 / 1.4.6 的导入器中，旧版格式只通过顶层 `format` 区分；未发现旧版专用字段映射。该结论来自对导入器代码的静态检查和模块级执行，尚未用真实旧版文件在应用界面验证。
2. `version` 字段的旧版取值未被使用；无法从现有样本确认旧版是否曾写 `version`。
3. 旧版是否使用过不同的图像槽位名（例如 `image1`）不在 1.19.6 的映射中；若存在，当前原作也无法导入，故不属于本契约的兼容范围。
4. 旧版是否区分点击/自语台词的文件级结构，未发现证据；当前映射只认角色对象内的 `lines` / `idleLines`。

这些缺口不阻塞实现：按“格式名不同、角色对象同 v1”处理即可。真实旧版文件验证记录在 [../samples/VERIFICATION.md](../../samples/VERIFICATION.md)。

## 5. 固定样本

- `samples/legacy/carnovella-clicker.json`：纯 JSON，包含 1 个角色，覆盖必需图像、点击/自语台词、作者字段。
- `samples/legacy/carnovella-clicker-defaults.json`：纯 JSON，只写最低字段，验证默认值。
- 两个样本均**不应**被 OCoD 再导出为 `carnovella-clicker`；再导出必须是 `bug-clicker` v1。

## 6. 导入算法

```
1. 读取文件，限制大小（同 .cvclk 的 200 MiB）。
2. 若有 BCVCLK2 魔数：解包；否则按 UTF-8 文本读取（去 BOM）。
3. JSON.parse；失败 → 拒绝。
4. 校验 format === "carnovella-clicker"；失败 → 拒绝。
5. characters 必须是非空数组；失败 → 拒绝。
6. 对每个角色对象执行 cvclk-v1.md 第 12 节的导入算法。
7. 返回与 v1 相同的导入结果。
```

## 7. 错误条件

| 条件 | 行为 |
| --- | --- |
| 文件 > 200 MiB | 拒绝 |
| JSON 非法 | 拒绝，提示无法读取 |
| `format` 不是 `bug-clicker` 或 `carnovella-clicker` | 拒绝，提示不是角色文件 |
| `characters` 为空/非数组 | 拒绝，提示文件内没有角色 |
| 角色缺少可用 `body`/`head1` | 跳过该角色，记录原因 |
| 未知字段 | 忽略；应当保留以便再导出 |
