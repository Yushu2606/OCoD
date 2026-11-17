# 独立部件包 `.cvpart`（`bug-clicker-part` v1）

本文件定义 `.cvpart` 容器内 JSON 载荷、部件条目与导入/导出规则。容器本身见 [container-v2.md](container-v2.md)。`.cvclk` 的 `parts` 字段复用本文件的部件条目结构。

## 1. 顶层载荷

```json
{
  "format": "bug-clicker-part",
  "version": 1,
  "exportedAt": "2026-11-17T00:00:00.000Z",
  "from": "净室样本·彩绘",
  "parts": [ Part, ... ],
  "rigCommon": {"mesh": 11, "joint": 60, "tex": 0.6, "lag": 0}
}
```

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `format` | string | 是 | 必须为 `bug-clicker-part` |
| `version` | number | 写入必需，读取忽略 | 固定为 `1` |
| `exportedAt` | string | 否 | ISO-8601 导出时间 |
| `from` | string | 否 | 来源角色显示名；用于导入提示 |
| `parts` | array | 是 | 1–16 个部件条目；空数组或超过 16 个视为无效 |
| `rigCommon` | object | 否 | 骨架部件公共参数；仅骨架部件使用 |

读取方**必须**忽略 `version`。`.cvpart` 没有纯 JSON 兼容路径：必须是 `BCVPART1` 容器。

顶层未知字段**应当**保留（再导出未修改的部件包时原样带回）。

## 2. 部件条目总览

```json
{
  "slot": "headTop",
  "name": "蝴蝶结",
  "settings": { ... },
  "image": "data:image/png;base64,...",
  "shadowImage": "data:image/png;base64,...",
  "blinkImage": "data:image/png;base64,..."
}
```

```json
{
  "slot": "ear",
  "name": "",
  "rigId": "r1",
  "on": true,
  "at": "mid",
  "settings": { ... },
  "image": "data:image/png;base64,..."
}
```

| 字段 | 头部部件 | 骨架部件 | 说明 |
| --- | --- | --- | --- |
| `slot` | 必需 | 必需 | `headTop`/`headTop2`/`headTop3`，或 `ear`/`tail`/`wing`/`prop` |
| `name` | 可选 | 可选（通常空） | 头部部件显示名，≤ 40 字符；骨架部件名在 `settings.name` |
| `settings` | 可选 | 可选 | 见第 3、4 节；缺失使用默认 |
| `image` | 必需 | 必需 | 部件图 data URL；无效则整个部件丢弃 |
| `shadowImage` | 可选 | 不适用 | 头部部件影子图 |
| `blinkImage` | 可选 | 不适用 | 头部部件眨眼图 |
| `rigId` | 不适用 | 必需 | 来源角色的骨架部件 ID；导入时重新分配 |
| `on` | 不适用 | 可选 | 是否启用；默认 `true` |
| `at` | 不适用 | 可选 | `back`/`mid`/`front`；默认按 `partOrder` 计算 |

### 2.1 图像约束

- `image`、`shadowImage`、`blinkImage` 必须是 `data:image/...;base64,...`。
- 单张解码后 1 字节至 33,554,432 字节（32 MiB）。
- 字节签名必须是 PNG、JPEG、WebP 或 GIF（同 [cvclk-v1.md](cvclk-v1.md) 第 5.2 节）。
- 原作的部件图界面要求透明 PNG，但交换格式层只做上述嗅探；OCoD 编辑器应当提示非 PNG 的兼容风险。
- 图缺失或无效：头部部件/骨架部件跳过，计入 `dropped`；不导致整包失败。

### 2.2 数量限制

- 一个 `.cvpart` 最多 16 个条目；导入器只处理前 16 个，超过 16 个的文件在读取阶段被拒绝。
- 一个角色最多 12 个骨架部件；导入时超过上限的骨架部件不再添加。
- 头部部件槽位只有 3 个；同一槽位多次出现时后者覆盖前者。

## 3. 头部部件设置（`headTop*`）

`settings` 对象包含以下键；缺失键使用默认值，越界值钳制，非法枚举回退默认。

| 键 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `On` | boolean | `true` | — | 是否显示 |
| `Size` | number | `100` | 30–200 | 大小百分比 |
| `X` | number | `0` | -200–200 | 水平偏移（px，基准宽 200） |
| `Y` | number | `0` | -150–150 | 垂直偏移（px，基准宽 200） |
| `Hop` | number | `14` | 0–60 | 点击跳动高度（px） |
| `Soft` | number | `30` | 0–60 | 跟随柔软度 |
| `Squish` | number | `35` | 0–50 | 压扁百分比 |
| `Every` | number | `1600` | 0–10000 | 点击跳动间隔（ms） |
| `Press` | boolean | `true` | — | 是否响应按下 |
| `When` | string | `down` | `down`, `up` | 在按下还是抬起时动 |
| `Beat` | boolean | `true` | — | 是否响应音乐/游戏节拍 |
| `Type` | boolean | `false` | — | 是否响应打字 |
| `ShOn` | boolean | `true` | — | 是否显示影子 |
| `ShShape` | string | `oval` | `oval`, `dot`, `soft`, `png` | 影子形状 |
| `ShColor` | string | `#000000` | `#rrggbb` | 影子颜色 |
| `ShAlpha` | number | `0.3` | 0–1 | 影子不透明度 |
| `ShSize` | number | `100` | 20–200 | 影子大小百分比 |
| `ShBlur` | number | `3` | 0–20 | 影子模糊 |
| `ShY` | number | `0` | -60–60 | 影子垂直偏移 |
| `ShFloat` | boolean | `true` | — | 影子是否随浮动 |

`settings` 中未知键**应当**保留。头部部件的显示名用外层 `name` 交换，不在 `settings` 内；`Protected`/`Locked` 也不属于交换设置，它们由容器 `locked` 和导入结果派生。

## 4. 骨架部件设置

骨架部件有四种 `slot`，每种有独立参数集。所有数值在导入时按下表范围钳制；缺失字段用默认值。所有布尔字段接受 JSON boolean；非 boolean 回退默认。

### 4.1 公共字段（所有骨架部件）

| 键 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `name` | string | `""` | ≤ 24 字符，去除控制字符与 `<>` | 部件显示名 |
| `when` | string | `down` | `down`, `up` | 在按下还是抬起时动 |
| `scale` | number | `100` | 40–200（`prop` 为 30–250） | 缩放百分比 |
| `fade` | number | `0` | 0–60 | 淡出 |
| `range` | number | 见各类型 | 5–120 | 摆动范围 |
| `amp` | number | 见各类型 | 0–100 | 摆幅 |
| `period` | number | 见各类型 | 0.4–5 | 周期（秒） |
| `soft` | number | 见各类型 | 0–100 | 柔软度 |
| `bounce` | number | 见各类型 | 0–100 | 回弹 |
| `kick` | number | 见各类型 | 0–100 | 受击反应 |
| `wobble` | number | 见各类型 | 0–100 | 摇摆 |
| `bones` | number[] | 见各类型 | 1–12 项，每项 > 0，归一化到和为 1 | 骨骼分布 |
| `pinAt` | number[2] 或 null | `null` | 0–800 整数 | 固定点 |
| `tipAt` | number[2] 或 null | `null` | 0–800 整数 | 末端点 |

### 4.2 耳朵 `ear`

| 键 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `off` | number[2][2] | `[[0,0],[0,0]]` | -400–1200 整数 | 左右耳偏移 |
| `mirror` | boolean | `true` | — | 左右镜像 |
| `tilt` | number | `0` | -180–180 | 倾斜角 |
| `opp` | boolean | `true` | — | 左右反向摆动 |
| `onDrag` | boolean | `true` | — | 拖拽时响应 |
| `onBeat` | boolean | `true` | — | 节拍时响应 |
| `beatSway` | boolean | `false` | — | 节拍摇摆 |
| `onKey` | boolean | `true` | — | 打字时响应 |
| `onPet` | string | `none` | `none`, `droop`, `bounce` | 抚摸反应 |
| `petAmt` | number | `50` | 0–100 | 抚摸反应幅度 |
| `stiff` | number | `0` | 0–100 | 硬度 |
| `drop` | boolean | `false` | — | 下垂形态 |
| `ant` | boolean | `false` | — | 触角形态 |
| `split` | boolean | `false` | — | 左右分开控制 |
| `tiltL` | number 或 null | `null` | -180–180 | 左耳倾斜 |
| `pinAtL` | number[2] 或 null | `null` | 0–800 | 左耳固定点 |
| `tipAtL` | number[2] 或 null | `null` | 0–800 | 左耳末端 |

默认骨骼：3 段均分。默认 `range=35`、`amp=25`、`period=1.2`、`soft=40`、`bounce=89`、`kick=94`、`wobble=49`。

### 4.3 尾巴 `tail`

| 键 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `root` | number[2] | `[400,650]` | -400–1200 整数 | 根部位置 |
| `dir` | number | `8` | -180–180 | 方向角 |
| `curve` | number | `-14` | -90–90 | 弯曲 |
| `flip` | boolean | `false` | — | 翻转 |
| `fur` | string | `L` | `L`, `R` | 毛发方向 |
| `onBeat` | boolean | `true` | — | 节拍响应 |
| `head` | boolean | `false` | — | 是否挂在头上 |
| `rel` | number[2] 或 null | `null` | -400–1200 整数 | 相对根部位置 |
| `rev` | boolean | `false` | — | 反向 |
| `vtip` | boolean | `false` | — | 末端可见 |
| `onPet` | string | `none` | `none`, `wag` | 抚摸反应 |
| `petAmt` | number | `50` | 0–100 | 抚摸幅度 |

默认骨骼：12 段均分。默认 `range=90`、`amp=90`、`period=2.7`、`soft=88`、`bounce=69`、`kick=67`、`wobble=55`。

`head=true` 且 `rel` 为 null 时，导入器按 `root - pinAt`（若 `pinAt` 存在）或 `[0,0]` 生成 `rel`。

### 4.4 翅膀 `wing`

| 键 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `style` | string | `bat` | 当前只支持 `bat` | 翅膀样式；导入时强制 `bat` |
| `x` | number | `0` | -300–300 | 水平偏移 |
| `y` | number | `-27` | -400–400 | 垂直偏移 |
| `tilt` | number | `-19` | -180–180 | 倾斜 |
| `down` | number | `50` | 15–85 | 下垂程度 |
| `depth` | number | `76` | 10–100 | 深度 |
| `flaps` | number | `1` | 1–3 整数 | 每次拍动次数 |
| `flapT` | number | `0.45` | 0.2–1.2 | 拍动时长 |
| `split` | boolean | `false` | — | 左右分开控制 |
| `sym` | boolean | `true` | — | 左右对称 |
| `xL` | number 或 null | `null` | -300–300 | 左翼水平偏移 |
| `yL` | number 或 null | `null` | -400–400 | 左翼垂直偏移 |
| `tiltL` | number 或 null | `null` | -180–180 | 左翼倾斜 |
| `pinAtL` | number[2] 或 null | `null` | 0–800 | 左翼固定点 |
| `tipAtL` | number[2] 或 null | `null` | 0–800 | 左翼末端 |

默认骨骼：6 段均分。默认 `range=30`、`amp=64`、`period=1.4`、`soft=41`、`bounce=79`。

### 4.5 道具 `prop`

| 键 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `anim` | string | `hop` | `hop`, `float`, `shake`, `roll`, `peek`, `still` | 动画 |
| `pos` | number[2] | `[0,0]` | -600–600 整数 | 位置 |
| `follow` | string | `none` | `none`, `head`, `noPet` | 跟随对象 |
| `hopH` | number | `60` | 5–300 | 跳跃高度 |
| `hopEvery` | number | `1.6` | 0–6 | 跳跃间隔（秒） |
| `hopSquash` | number | `30` | 0–60 | 跳跃压扁 |
| `flRx` | number | `40` | 0–300 | 漂浮 X 半径 |
| `flRy` | number | `24` | 0–300 | 漂浮 Y 半径 |
| `flPeriod` | number | `4` | 1–12 | 漂浮周期 |
| `shAmp` | number | `8` | 0–60 | 抖动幅度 |
| `shDeg` | number | `6` | 0–30 | 抖动角度 |
| `shDur` | number | `0.6` | 0.2–2 | 抖动时长 |
| `shEvery` | number | `3` | 0–10 | 抖动间隔 |
| `rlX1` | number | `-120` | -400–-10 | 滚动起点 X |
| `rlX2` | number | `120` | 10–400 | 滚动终点 X |
| `rlSpeed` | number | `150` | 20–500 | 滚动速度 |
| `rlPause` | number | `0.8` | 0–4 | 滚动暂停 |
| `pkDx` | number | `90` | -400–400 | 探头 X 偏移 |
| `pkDy` | number | `-70` | -400–400 | 探头 Y 偏移 |
| `pkWait` | number | `2.2` | 0.3–10 | 探头等待 |
| `pkStay` | number | `1.2` | 0.2–6 | 探头停留 |

道具的 `settings` 只包含上表字段加上公共的 `name`、`scale`、`when`；它不使用第 4.1 节的 `range`/`amp`/`period`/`soft`/`bounce`/`kick`/`wobble`/`bones` 等摆动参数。导入时按 `Rig.DEF.prop` 的键集处理，未列出的键**不应**写入道具设置；未知键按第 8 节保留。

### 4.6 `rigCommon`

| 键 | 类型 | 默认 | 范围 | 语义 |
| --- | --- | --- | --- | --- |
| `mesh` | number | `11` | 6–24 | 网格细分 |
| `joint` | number | `60` | 0–100 | 关节柔软度 |
| `tex` | number | `0.6` | 0–2 | 纹理拉伸 |
| `lag` | number | `0` | 0–100 | 延迟跟随 |

缺失 `rigCommon` 时使用上表默认。未知键**应当**保留。

## 5. `at` 与绘制顺序

`at` 决定骨架部件插入角色 `partOrder` 的位置：

- `front`：插到 `front` 之前；没有 `front` 时追加到末尾。
- `mid`：插到 `head` 之前。
- `back`：插到 `hair`（若有）或 `body` 之前。

导出时按来源角色的 `partOrder` 计算：

```
bodyIndex = partOrder.indexOf("body")   // 缺失时为 -1
headIndex = partOrder.indexOf("head")   // 缺失时为 -1
rigIndex  = partOrder.indexOf(rigId)
at = rigIndex > headIndex ? "front" : rigIndex > bodyIndex ? "mid" : "back"
```

原作直接按 `indexOf` 的 `-1` 参与比较；实现方**应当**保持同样行为。导入时若 `at` 缺失或非法：`slot === "ear"` → `mid`，其他骨架部件 → `back`。

## 6. 导入算法

```
1. 读取文件；若 > 40 MiB，拒绝。
2. 若不是 BCVPART1 容器：若是 BCVCLK2，提示到角色导入入口；否则拒绝。
3. 解包、解析 JSON；校验 format、parts 数组、1 ≤ parts.length ≤ 16。
4. 对每个条目（最多 16 个）：
   a. 解码 image；无效 → dropped++，跳过。
   b. slot 为 headTop/headTop2/headTop3：
        - 复制 settings 中已知 PART_KEYS；缺失用默认，越界钳制。
        - 写入 partTop*/partTop2*/partTop3* 图像；shadowImage、blinkImage 若有效一并写入。
        - 设置 Name（≤ 40）、Protected=true、Locked=header.locked、On=true。
        - applied 记录 slot。
   c. slot 为 ear/tail/wing/prop：
        - 放入待添加列表；记录 rigId、on、at、settings。
   d. 其他 slot：unknown 记录，跳过。
5. 对每个待添加骨架部件：
   - 若已有 12 个骨架部件，停止。
   - 分配新 ID：r1, r2, ...（第一个未占用；都用则时间戳后缀）。
   - 写入 rig 条目 {id, kind, on (默认 true), img, prot:true, locked:header.locked, s:规范化 settings}。
   - 按第 5 节插入 partOrder。
   - 写入图像文件（本地密封格式）。
   - 建立 old rigId → new ID 映射。
6. 应用 rigCommon（若存在）。
7. 返回 applied、unknown、dropped、rigMap、locked。
```

导入失败条件：

- 不是 `BCVPART1` 容器。
- 载荷 JSON 非法、`format` 不匹配、`parts` 为空或超过 16。
- 没有任何可应用的部件且存在被跳过的条目 → 返回失败原因（未知槽位或图像无效）。

### 6.1 覆盖行为

- 头部部件按槽位覆盖：导入 `headTop` 会替换现有 `partTop` 图像、影子、眨眼图和设置；`headTop2`/`headTop3` 同理。
- 骨架部件总是新增；同一 `rigId` 不会覆盖现有部件，因为导入后 ID 重新分配。
- `on=false` 的骨架部件仍会添加，但默认不显示；`applied` 仍记录。

## 7. 导出算法

```
1. 读取角色；若角色本地 locked，拒绝导出（受保护包）。
2. 遍历 headTop/headTop2/headTop3：
   - 若图像存在且未被锁定：读取 image、shadowImage、blinkImage，复制 settings，
     写条目 {slot, name, settings, image?, shadowImage?, blinkImage?}。
   - 若图像存在但被锁定：加入 held，不导出。
3. 遍历本地 rig：
   - 若图像存在且未被锁定：读取 image，复制 settings，
     按第 5 节计算 at，写条目 {slot: kind, name:"", rigId:id, on, at, settings, image}。
   - 若被锁定：加入 held，不导出。
4. 若没有任何可导出部件：
   - 若 held 非空：报“受保护部件不可再导出”。
   - 否则报“没有可导出的部件”。
5. rigCommon = 规范化后的公共参数。
6. 组装载荷；写容器（locked 由导出选项决定）。
```

写入方**应当**：

- 只导出 `PART_KEYS` 与各骨架类型的已知设置键；未知设置键按第 8 节保留策略处理。
- 保证 `parts.length ≤ 16`。
- 头部部件显示名只写外层 `name`（`settings` 不含 `Name`）；骨架部件外层 `name` 写空字符串，显示名放在 `settings.name`。
- `image` 用实际字节嗅探 MIME，不要仅凭扩展名。

## 8. 未知字段保留

| 位置 | 规则 |
| --- | --- |
| 顶层载荷 | 未修改的部件包再导出时原样保留未知字段 |
| 部件条目 | 未知字段原样保留；已知字段规范化 |
| 头部部件 `settings` | 未知键**应当**保留 |
| 骨架部件 `settings` | 未知键**应当**保留 |
| `rigCommon` | 未知键**应当**保留 |
| 未知 `slot` | 导入到角色时跳过并报告；若只是预览/再导出部件包，原样保留 |

## 9. 受保护 `.cvpart`

`header.locked=true` 的完整行为见 [protected-cvclk.md](protected-cvclk.md) 第 7.2、8、9 节。摘要：

- 导入后每个部件 `Protected=true`、`Locked=true`；不能再导出。
- 角色自身 `locked=true` 时，其全部部件都不可导出。
- 受保护 `.cvpart` 没有 owner/口令恢复机制。
- 读取方**必须**把 `locked=true` 传递到导入结果；不得把受保护部件当作普通部件再导出。
