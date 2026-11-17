# 完整角色包 `.cvclk`（`bug-clicker` v1）

本文件定义 `.cvclk` 容器内 JSON 载荷的结构、字段语义、默认值、导入与导出规则。容器本身见 [container-v2.md](container-v2.md)。

## 1. 顶层载荷

```json
{
  "format": "bug-clicker",
  "version": 1,
  "app": "1.19.6",
  "exportedAt": "2026-11-17T00:00:00.000Z",
  "characters": [ Character, ... ]
}
```

| 字段 | 类型 | 必需 | 说明 |
| --- | --- | --- | --- |
| `format` | string | 是 | 必须为 `bug-clicker` |
| `version` | number | 写入必需，读取忽略 | 角色对象版本；本契约固定为 `1` |
| `app` | string | 否 | 导出应用版本，`X.Y.Z`；原作仅用于预览显示 |
| `exportedAt` | string | 否 | ISO-8601 导出时间；原作不校验 |
| `characters` | array | 是 | 至少 1 个角色对象；空数组视为无效文件 |

读取方**必须**忽略 `version`，不得因未来版本号拒绝已知 `format` 的文件。写入方**必须**写 `version: 1`。

### 1.1 多角色

一个 `.cvclk` 可以包含任意多个角色。导入时逐个处理；某个角色失败不影响其他角色。同包角色按数组顺序导入，导入器返回成功、跳过及原因列表。

### 1.2 重复角色

重复角色的识别与处理（跳过、保留两份、更新）属于 [#12](https://github.com/Yushu2606/OCoD/issues/12)，不在本契约定义。本契约只要求导入器能独立导入每个角色对象。

## 2. 角色对象总览

角色对象是载荷 `characters` 数组的元素。以下字段按功能分组；所有字段均为可选，除第 2.1 节明确要求外，缺失时使用默认值。

```json
{
  "id": "sample-full",
  "name": "净室样本·彩绘",
  "author": { ... },
  "depth": 5,
  "hold": 110,
  "squash": 0,
  "bubbleMs": 1500,
  "volume": 0.45,
  "idleMs": 60000,
  "bubble": true,
  "breathe": true,
  "breathMs": 3400,
  "breathAmp": 12,
  "blinkMs": 4200,
  "sound": true,
  "noRepeat": true,
  "inOrder": false,
  "soundType": "click",
  "soundPitch": 0,
  "reactStyle": "nod",
  "reactAmp": 1,
  "beatMotion": { ... },
  "keyMotion": { ... },
  "shiverHead": 0,
  "shiverBody": 0,
  "neckOn": true,
  "neckDepth": 6,
  "neckColor": "#000000",
  "neckAlpha": 0.3,
  "floorOn": true,
  "floorDepth": 6,
  "floorColor": "#000000",
  "floorAlpha": 0.34,
  "olMode": "none",
  "olColor": "#ffffff",
  "olWidth": 3,
  "olAlpha": 1,
  "bubbleStyle": { ... },
  "bubbleStyles": [ ... ],
  "lineStyles": { ... },
  "petOn": false,
  "petKinds": ["heart"],
  "petSizeMode": "random",
  "petSize": 26,
  "petFill": "#ffffff",
  "petLine": "#ffffff",
  "petColorMode": "one",
  "petKindColors": {},
  "petLineFollow": false,
  "petLineW": 0,
  "petGlow": true,
  "petEdge": "glow",
  "petGlowSize": 5,
  "petSway": 3,
  "petAmount": 4,
  "petSpeed": 1100,
  "petTilt": 3,
  "petArea": "all",
  "petCut": 0,
  "dragOn": false,
  "dragSwing": 100,
  "dragBodyY": 0,
  "dropSquish": 100,
  "dropWobble": 100,
  "liftShadow": true,
  "liftShadowY": 0,
  "dragBodyS": 100,
  "liftHide": [],
  "liftStill": [],
  "liftAmt": {},
  "liftSad": [],
  "liftOnly": [],
  "liftAux": {},
  "startSize": 0,
  "bubbleX": 0,
  "bubbleY": 0,
  "bubbleBehind": false,
  "soundIdle": false,
  "headBack": false,
  "lineSoundOn": true,
  "lineSoundVol": 1,
  "partOrder": ["body", "head"],
  "layers": { ... },
  "fontName": "",
  "lines": [],
  "idleLines": [],
  "sp_alarm_before": [],
  "sp_alarm_after": [],
  "sp_pill_before": [],
  "sp_pill_after": [],
  "sp_water_before": [],
  "sp_water_after": [],
  "sp_todo_before": [],
  "sp_todo_after": [],
  "images": { ... },
  "imageUrls": {},
  "lineSounds": { ... },
  "lineSoundFiles": { ... },
  "parts": [ ... ],
  "rigCommon": { ... },
  "bubbleImages": { ... },
  "petImages": { ... },
  "font": "data:font/..."
}
```

上面的示意同时出现了 `sound` 的布尔形式和字符串形式；同一对象只能出现一种。

### 2.1 可用角色的最低要求

导入时，一个角色要成为可用角色，必须满足：

1. 能从 `images.head1` 或 `images.body` 获得有效图像；或从 `imageUrls.head1` / `imageUrls.body` 下载成功。
2. 至少有 `head1`/`body` 之一；随后按第 5.4 节回退补齐。

`name` 缺失时导入器使用角色 `id`，再缺失使用本地化的“未命名角色”。写入方**必须**至少写 `images.body` 与 `images.head1`，**应当**写 `images.head2`。

`id` 是导出方角色库中的 ID，仅用于展示和调试。导入方**必须**忽略它并按自己的命名规则创建新 ID；不得用它作为跨包身份标识。

## 3. 核心字段

| 字段 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `name` | string | `이름 없는 캐릭터` | 非空，建议 ≤ 200 字符 | 显示名；导入时 trim；空则回退 `id`，再回退本地化未命名 |
| `depth` | number | `5` | 0–40 | 按下时下沉深度（px，基准宽 200） |
| `hold` | number | `110` | 30–600 | 按下姿态保持时间（ms） |
| `squash` | number | `0` | 0–20 | 按下时压扁百分比 |
| `bubbleMs` | number | `1500` | 300–6000 | 气泡停留时间（ms） |
| `volume` | number | `0.45` | 0–1 | 点击音音量 |
| `idleMs` | number | `0` | 0–86400000 | 自言自语间隔（ms）；0 表示不主动说话 |
| `bubble` | boolean | `true` | — | 是否显示气泡；false 时点击不显示台词 |
| `breathe` | boolean | `true` | — | 是否播放呼吸动作 |
| `breathMs` | number | `3400` | 1200–8000 | 呼吸周期（ms） |
| `breathAmp` | number | `12` | 0–40 | 呼吸幅度（基准宽 200 的比例量） |
| `blinkMs` | number | `4200` | 0–60000 | 眨眼间隔（ms）；0 关闭；需要 `head3` |
| `sound` | boolean 或 data URL | `true` | — | 布尔值表示是否播放点击音；字符串为自定义音频 data URL（见第 8.1 节） |
| `noRepeat` | boolean | `true` | — | 避免连续重复同一句台词 |
| `inOrder` | boolean | `false` | — | true 时按顺序播放台词，false 时随机 |
| `soundType` | string | `click` | `click`, `tock`, `pop`, `beep`, `wood`, `type`, `bell`, `blip`, `soft`, `custom` | 内置点击音；`custom` 需要有效自定义音频 |
| `soundPitch` | number | `0` | 0–30 | 1.19.6 中保留但未使用；原样导入导出 |
| `reactStyle` | string | `nod` | `nod`, `bob`, `press` | 旧版反应样式；作为 `beatMotion`/`keyMotion` 缺失时的回退 |
| `reactAmp` | number | `1` | 0.2–3 | 旧版反应幅度；同上 |
| `shiverHead` | number | `0` | 0–6 | 长按时头部抖动（px，基准宽 200） |
| `shiverBody` | number | `0` | 0–6 | 长按时身体抖动（px，基准宽 200） |
| `neckOn` | boolean | `true` | — | 头与身体之间的投影 |
| `neckDepth` | number | `6` | 0–24 | 颈影深度（px，基准宽 200） |
| `neckColor` | string | `#000000` | `#rrggbb` | 颈影颜色 |
| `neckAlpha` | number | `0.3` | 0–1 | 颈影不透明度 |
| `floorOn` | boolean | `true` | — | 脚下投影 |
| `floorDepth` | number | `6` | 0–24 | 脚影深度（px，基准宽 200） |
| `floorColor` | string | `#000000` | `#rrggbb` | 脚影颜色 |
| `floorAlpha` | number | `0.34` | 0–1 | 脚影不透明度 |
| `olMode` | string | `none` | `none`, `line`, `glow` | 角色整体描边/发光 |
| `olColor` | string | `#ffffff` | `#rrggbb` | 描边/发光颜色 |
| `olWidth` | number | `3` | 1–12，步长 0.5 | 描边宽度/发光半径（px，基准宽 200） |
| `olAlpha` | number | `1` | 0.1–1，步长 0.01 | 描边/发光不透明度 |
| `bubbleX` | number | `0` | -80–80 | 气泡水平偏移（基准宽 200） |
| `bubbleY` | number | `0` | -60–60 | 气泡垂直偏移（基准宽 200） |
| `bubbleBehind` | boolean | `false` | — | 气泡是否在角色后面 |
| `soundIdle` | boolean | `false` | — | 自言自语时是否也播放点击音 |
| `headBack` | boolean | `false` | — | 头是否绘制在身体后面 |
| `lineSoundOn` | boolean | `true` | — | 是否播放台词音 |
| `lineSoundVol` | number | `1` | 0–2，步长 0.01 | 台词音全局倍率 |
| `fontName` | string | `""` | ≤ 60 字符 | 内嵌字体显示名；仅用于 UI |
| `startSize` | number | `0` | 0 或 60–600 | 首次显示尺寸（px）；0 表示运行时默认（原作 160） |

### 3.1 反应动作 `beatMotion` / `keyMotion`

两者结构相同，`keyMotion` 额外有 `fast`、`gap`。

| 字段 | 类型 | `beatMotion` 默认 | `keyMotion` 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- | --- |
| `style` | string | `nod` | `bob` | `nod`, `bob`, `press`, `custom` | 动作形状 |
| `amp` | number | `1` | `1` | 0.2–3 | 幅度 |
| `dy` | number | `3.2` | `-2.4` | -16–16 | 自定义垂直位移 |
| `rot` | number | `2.6` | `0` | 0–15 | 自定义旋转角度 |
| `alt` | boolean | `true` | `false` | — | 是否左右交替 |
| `dur` | number | `0` | `160` | 0–800 | 单次时长（ms）；0 表示自动 |
| `blinkEvery` | number | `0` | `0` | 0–10 整数 | 每 N 次动作眨一次眼；0 关闭 |
| `fast` | string | — | `cap` | `cap`, `loop`, `all` | 仅 `keyMotion`：快速输入时的处理 |
| `gap` | number | — | `200` | 60–800 | 仅 `keyMotion`：动作间隔（ms） |

缺失 `beatMotion`/`keyMotion` 时，读取方按 `reactStyle`/`reactAmp` 生成；两者都缺失时使用上表默认。写入方**应当**写完整对象。

### 3.2 受保护包独有字段（本票只列名）

以下字段只应出现在受保护包中。普通包**应当**省略它们；读取方遇到时**必须**安全保留或按 [protected-cvclk.md](protected-cvclk.md) 处理，不得因字段存在而误解包权限。

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `editLock` | boolean | `false` | 是否锁定原始编辑字段；普通包写 `false` 或不写 |
| `editBase` | object | 无 | 受保护包导入时的原始台词基线；键为台词键 |
| `editSty` | object | 无 | 受保护包导入时的原始台词样式基线；键为台词键 |

完整语义（锁定字段集合、合并规则、口令恢复、错误路径）见 [protected-cvclk.md](protected-cvclk.md)。普通包导出器不生成 `editBase`/`editSty`，并且 `editLock` 不是 `true`。

本地库字段 `protected`、`locked`、`authorLocked`、`soundFile`、`fontFile` 和本地 `rig` 数组**不是** `.cvclk` 交换字段；读取方不应期待它们出现在载荷中。交换格式用 `parts`、`sound` data URL、`font` data URL 等表达对应内容。

## 4. 作者字段 `author`

```json
{
  "name": "作者名",
  "link": "https://x.com/author",
  "stream": "yes",
  "edit": "no",
  "redistribute": "no",
  "credit": "yes",
  "memo": "备注",
  "warn": ["flash", "bugs"],
  "warnEtc": "其他提醒"
}
```

| 字段 | 类型 | 默认 | 约束 |
| --- | --- | --- | --- |
| `name` | string | `""` | ≤ 40 字符；去除控制字符与 `<>`；折叠空白 |
| `link` | string | `""` | ≤ 100 字符；必须匹配作者链接白名单，否则导入为空 |
| `stream` | string | `""` | `yes` / `no` / `""`：是否允许直播 |
| `edit` | string | `""` | 同上：是否允许编辑 |
| `redistribute` | string | `""` | 同上：是否允许再分发 |
| `credit` | string | `""` | 同上：是否要求署名 |
| `memo` | string | `""` | ≤ 100 字符 |
| `warn` | array | `[]` | 仅允许 `blood`, `violence`, `horror`, `nudity`, `bugs`, `flash`；去重 |
| `warnEtc` | string | `""` | ≤ 40 字符 |

语义与完整白名单见 [author-and-bubble-reference.md](author-and-bubble-reference.md)。

导入时：

- 作者对象缺失 → 全部空值。
- `author.name` 命中作者名过滤名单 → 整个 `author` 置空，角色仍导入。
- `author.link` 不合法 → `link` 置空，角色仍导入。
- 作者名非空且包未被作者口令解锁 → 本地模型 `authorLocked = true`（作者栏不可编辑）。该字段不写入交换载荷。
- 若作者名为空 → `authorLocked = false`，接收者可添加自己的作者信息。

写入方**应当**在导出前对完整 `author` 对象做作者过滤检查；命中时原作拒绝导出整个包（错误码 `AUTHOR_WORDS`）。OCoD 应当保持一致，避免产出原作会拒绝的文件。

## 5. 图像 `images`

`images` 是“槽位 → 图像 data URL”的对象。导出方**必须**至少提供 `body`、`head1`；**应当**提供 `head2`。

### 5.1 槽位

| 槽位 | 必需 | 可见语义 |
| --- | --- | --- |
| `body` | 是 | 身体（按下时不变） |
| `head1` | 是 | 平常头部 |
| `head2` | 应当 | 按下时头部；缺失时用 `head1` |
| `head3` | 否 | 闭眼/眨眼头部；缺失时不眨眼 |
| `head4` | 否 | 被抚摸或拖动时头部；缺失时回退 `head3` 或 `head1` |
| `head5` | 否 | 拖动时头部；缺失时回退 `head4` |
| `body2` | 否 | 拖动时身体；缺失时用 `body` |
| `back1` | 否 | 后发/背后装饰，平常（身体后面） |
| `back2` | 否 | 后发/背后装饰，按下时；缺失时用 `back1` |
| `side1` | 否 | 侧发/装饰，平常（头部前面） |
| `side2` | 否 | 侧发/装饰，按下时；缺失时用 `side1` |
| `ex1` / `ex1p` | 否 | 追加图 1 平常/按下 |
| `ex2` / `ex2p` | 否 | 追加图 2 平常/按下 |
| `ex3` / `ex3p` | 否 | 追加图 3 平常/按下 |
| `ex4` / `ex4p` | 否 | 追加图 4 平常/按下 |

### 5.2 图像 data URL

格式：

```
data:image/<subtype>;base64,<base64>
```

- 导出方按文件扩展名选 MIME：`.webp` → `image/webp`，`.jpg`/`.jpeg` → `image/jpeg`，`.gif` → `image/gif`，其他（包括本地密封的 `.bcp`）→ `image/png`。MIME 只是提示；读取方**必须**按字节嗅探实际格式。
- **已知原作行为**：原作导入后把图像保存为 `.bcp`，再导出时固定写 `image/png`，即使底层字节是 GIF/JPEG/WebP。因此“MIME 与字节不一致”是可能出现的正常情况，读取方不得据此拒绝文件。
- 读取方接受且仅接受以下图像字节签名：PNG（`89 50 4E 47`）、JPEG（`FF D8`）、WebP（`RIFF....WEBP`）、GIF（`GIF8`）。
- 单张图像解码后 1 字节至 8,388,608 字节（8 MiB）；空或超限视为无效。
- 允许 GIF/APNG 动图；读取方不要求解码动画，但**应当**至少保留原始字节。
- 图像宽高不设硬上限；原作在显示时会按需缩放。

### 5.3 `imageUrls`（仅导入）

`imageUrls` 是“槽位 → HTTP(S) URL”的对象，作为 `images` 缺失或无效时的回退。

- URL 必须以 `http://` 或 `https://` 开头。
- 下载结果 1 字节至 8,388,608 字节，且通过图像签名嗅探。
- 下载失败计入 `missing`，不影响同角色其他槽位。
- 导出方**必须**写 `imageUrls: {}`（原作总是写空对象），不得依赖接收方联网。

### 5.4 图像回退

导入时按以下顺序补齐：

1. `head2` 缺失 → `head1`
2. `head1` 缺失 → `body`
3. `body` 缺失 → `head1`
4. `back2` 缺失 → `back1`
5. `side2` 缺失 → `side1`

补齐发生在图像有效性检查之后。若 `body` 与 `head1` 都无效，角色被跳过，原因为“无法读取图像”。

## 6. 图层位置与绘制顺序

### 6.1 `layers`

`.cvclk` 把 `<layer>Pos` / `<layer>Move` / `<layer>X` / `<layer>Y` 收进 `layers` 对象。`layer` 为：

```
back, side, ex1, ex2, ex3, ex4
```

| 字段 | 类型 | 取值 | 默认 |
| --- | --- | --- | --- |
| `<layer>Pos` | string | `front`, `behindHead`, `behindBody` | `back` → `behindBody`；`side`/`ex*` → `front` |
| `<layer>Move` | string | `head`, `fixed` | `back` → `fixed`；`side`/`ex*` → `head` |
| `<layer>X` | number | -400–400 整数 | 0（不写） |
| `<layer>Y` | number | -400–400 整数 | 0（不写） |

- 写入方只写非零/非默认的键；读取方对缺失键使用默认。
- `X`/`Y` 是相对偏移，导出时保留整数值；0 视为默认并省略。
- `Pos` 与 `partOrder` 中的 `L:<layer>` 冲突时，以 `partOrder` 的顺序为准：`L:` 项在 `body` 之前 → `behindBody`；在 `body` 与 `head` 之间 → `behindHead`；在 `head` 之后 → `front`。
- `Move = head` 表示图层跟随头部动作；`fixed` 表示固定在身体坐标系。

### 6.2 `partOrder`

`partOrder` 是字符串数组，定义绘制顺序。已知值：

| 值 | 含义 |
| --- | --- |
| `body` | 身体；缺失时导入器补到最前 |
| `head` | 头部；缺失时导入器补到 `body` 后 |
| `L:back`, `L:side`, `L:ex1` … `L:ex4` | 对应图层的位置标记 |
| 骨架部件 ID（如 `r1`） | 该骨架部件的绘制位置 |
| `top`, `top2`, `top3` | 头部部件（head-top）位置标记 |
| `hair`, `front` | 旧版绘制位置标记；当前导出通常不写 |

导入时：

- `body`、`head`、`L:back`、`L:side`、`L:ex1` … `L:ex4` 原样保留。
- 骨架部件 ID 按导入后重新分配的 ID 映射。
- `top`/`top2`/`top3`/`hair`/`front` 在当前原作导入器中会被丢弃；OCoD **应当**把它们作为未知/兼容值保留，再导出时原样带回。
- 未知字符串**应当**保留；不得导致导入失败。

## 7. 台词

### 7.1 台词键

| 字段 | 类型 | 默认 | 语义 |
| --- | --- | --- | --- |
| `lines` | string[] | `[]` | 点击台词 |
| `idleLines` | string[] | `[]` | 自言自语台词 |
| `sp_alarm_before` | string[] | `[]` | 闹钟提醒响起前 |
| `sp_alarm_after` | string[] | `[]` | 闹钟提醒被按掉后 |
| `sp_pill_before` | string[] | `[]` | 吃药提醒响起前 |
| `sp_pill_after` | string[] | `[]` | 吃药提醒被按掉后 |
| `sp_water_before` | string[] | `[]` | 喝水提醒响起前 |
| `sp_water_after` | string[] | `[]` | 喝水提醒被按掉后 |
| `sp_todo_before` | string[] | `[]` | 待办提醒响起前 |
| `sp_todo_after` | string[] | `[]` | 待办被勾掉后 |

- 每个数组的元素**应当**是字符串；读取方**必须**把非字符串元素转换为字符串。
- 空字符串或纯空白行在导入时被丢弃。
- `lineStyles` 与数组按下标对齐；台词被丢弃时对应样式和声音一起丢弃。
- `{memo}` 是特殊占位符，由运行时替换为提醒备注；它属于台词内容，不改变格式。

### 7.2 自定义提醒组（v1 缺口）

原作 1.19.6 本地模型支持 `sp_g<数字>_before` / `sp_g<数字>_after` 与 `groupNames`，但**导出器不写这些字段**，导入器也不读取。因此：

- `.cvclk` v1 不携带自定义提醒组；原作之间也无法通过 `.cvclk` 交换它们。
- OCoD 读取到这些字段时**应当**作为未知字段保留；再导出时**可以**原样带回。原作会忽略它们。
- OCoD 不应依赖这些字段实现跨应用兼容。

### 7.3 `lineStyles`

对象，键为第 7.1 节的台词键（原作编辑器会为 `lines`、`idleLines` 及特殊提醒台词键保存样式；导出器原样写出），值为与台词数组等长的字符串数组。

- 空字符串表示默认气泡样式。
- 非空值必须是样式 ID：内置 `p_shout`、`p_think`、`p_recall`，或 `bubbleStyles` 中的 `u_*`。
- 未知样式 ID **必须**被忽略（按默认样式显示），但**应当**原样保留以便再导出。
- 数组长度不足时，缺失项视为 `""`；超长部分忽略。

## 8. 声音、台词音与字体

### 8.1 点击音 `sound`

`sound` 有两种形式：

- 布尔值：`true` 表示使用内置音效（由 `soundType` 决定），`false` 表示静音。
- 字符串：音频 data URL；表示自定义点击音。

音频 data URL 约束：

| 项目 | 约束 |
| --- | --- |
| MIME | `audio/wav`, `audio/x-wav`, `audio/wave`, `audio/mpeg`, `audio/ogg`, `audio/mp4` |
| 大小 | 12 字节至 2,097,152 字节（2 MiB） |
| 字节签名 | `RIFF....WAVE`、`OggS`、`ID3`、MPEG 帧同步（`FF` 且 `E0 & ~byte1 == 0`）、或偏移 4 处 `ftyp` |
| 扩展名 | 由 MIME 决定：`.wav`、`.mp3`、`.ogg`、`.m4a` |

导入规则：

- 有效 data URL → 保存为 `sound.<ext>`，`soundType` 保持原值；仅当 `soundType === "custom"` 时保持 `custom`。
- 无效 data URL 或缺失 → `sound` 按布尔处理；若 `soundType === "custom"` 且没有有效自定义音频，导入后 `soundType` 重置为 `click`。
- 导出方有自定义音频时**必须**写 data URL；没有时**必须**写布尔值。

### 8.2 台词音 `lineSounds` / `lineSoundFiles`

`lineSoundFiles` 是“文件名 → 音频 data URL”的对象；`lineSounds` 是台词音引用。

```json
{
  "lineSounds": {
    "lines": [
      {"f": "lsnd_abc123.wav", "a": 0.05, "b": 0.15, "v": 1.2},
      null
    ],
    "idleLines": [
      {"f": "lsnd_def456.wav", "a": 0, "b": 0, "v": 0.5}
    ]
  },
  "lineSoundFiles": {
    "lsnd_abc123.wav": "data:audio/wav;base64,...",
    "lsnd_def456.wav": "data:audio/wav;base64,..."
  }
}
```

- `lineSounds` 的键仅允许 `lines`、`idleLines`；数组与对应台词数组按下标对齐。
- 数组元素为 `null` 或对象。
- `f`：文件名，必须匹配 `^lsnd_[a-z0-9]{6,16}\.(wav|mp3|ogg|m4a)$`。
- `a`：起始秒，0–600，保留两位小数。
- `b`：结束秒，0–600，保留两位小数；`b <= a` 时按 0 处理（表示播到结尾）。
- `v`：音量倍率，0–2，保留两位小数。
- 每个角色最多 60 个不同音频文件。
- `lineSoundFiles` 的每个值必须满足第 8.1 节的音频 data URL 约束。
- 导入时按“台词文本 trim 后完全相等”匹配第一条台词；重复文本只匹配第一次。文件被重新命名为新的 `lsnd_*`，因此再导出的文件名与导入前不同，语义相同。
- 导出时只保留音频有效且能解码的条目；无效条目的对应位置写 `null`。若没有任何有效条目，省略 `lineSounds` 与 `lineSoundFiles`。

`lineSounds` 只覆盖 `lines` 与 `idleLines`；特殊提醒台词不支持台词音。

### 8.3 内嵌字体 `font` / `fontName`

- `font`：字体 data URL，MIME 为 `font/ttf`、`font/otf`、`font/woff`、`font/woff2`；大小 12 字节至 12,582,912 字节（12 MiB）；字节签名 `wOFF`、`wOF2`、`OTTO`、`true` 或 `00 01 00 00`。
- `fontName`：≤ 60 字符的显示名；可为空。
- `bubbleStyle.family === "@file"` 表示使用内嵌字体。若没有有效 `font`，导入时把 `family` 清空。
- 导出方有字体文件时写 `font` 与 `fontName`；没有时省略 `font`，`fontName` 可写空字符串。

## 9. 气泡与样式

### 9.1 `bubbleStyle`

`bubbleStyle` 是单个气泡样式对象；缺失或 `null` 时使用默认。字段：

| 字段 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `preset` | string | `dark` | `^[a-z-]{1,20}$` | 预设 ID；仅用于 UI 记录，实际颜色以显式字段为准 |
| `bg` | string | `#212121` | `#rrggbb`（允许 `#rrggbbaa` 的输入） | 背景色 |
| `fg` | string | `#e8e8e8` | 同上 | 文字色 |
| `line` | string | `#e8e8e824` | 同上 | 边框色 |
| `radius` | number | `16` | 0–28 | 圆角半径 |
| `font` | number | `14` | 11–22 | 字号 |
| `size` | number | `100` | 80–160 整数 | 整体缩放百分比 |
| `weight` | number | `500` | `400`, `500`, `700` | 字重 |
| `shadow` | boolean | `true` | — | 是否投影 |
| `tail` | boolean | `true` | — | 是否显示尾巴 |
| `gap` | number | `12` | 0–60 | 文字内边距 |
| `align` | string | `left` | `left`, `center`, `right` | 对齐 |
| `family` | string | `""` | ≤ 80 字符，或 `@file` | 字体族；`@file` 使用内嵌字体 |
| `grad` | boolean | `false` | — | 是否渐变 |
| `bg2` | string | `#4a4a4a` | `#rrggbb` | 渐变第二色 |
| `gdir` | string | `d` | `v`, `h`, `d` | 渐变方向（垂直/水平/对角） |
| `frame` | string | `default` | `default`, `solid`, `dashed`, `double`, `none` | 边框样式 |
| `lineW` | number | `2` | 1–8 | 边框宽度 |
| `lineC` | string | `#e8e8e8` | `#rrggbb` | 边框颜色 |
| `cSize` | number | `28` | 12–80 整数 | 角饰大小 |
| `cOver` | string | `half` | `out`, `half`, `in` | 角饰覆盖方式 |
| `cPop` | boolean | `true` | — | 角饰弹出动画 |
| `cLayer` | string | `front` | `back`, `mid`, `front` | 角饰层级 |
| `skSlice` | number | `24` | 1–200 整数 | 九宫格切片 |
| `skW` | number | `16` | 2–80 整数 | 皮肤边框宽度 |
| `skRep` | string | `stretch` | `stretch`, `round` | 皮肤重复方式 |
| `cUse` | boolean | `true` | — | 是否使用角饰图 |
| `skUse` | boolean | `true` | — | 是否使用皮肤图 |
| `shape` | string | `box` | `box`, `hand`, `pixel`, `caption`, `focus`, `recall`, `dark`, `dream`, `paper`, `shout` | 气泡形状 |
| `appear` | string | `grow` | `grow`, `fade`, `pop`, `rise`, `none` | 出现动画 |
| `textFx` | string | `none` | `none`, `type`, `blur`, `wave`, `shake`, `glitch` | 文字效果 |
| `speed` | number | `1` | 0.3–2 | 动画速度倍率 |
| `split` | boolean | `false` | — | 是否对 `speedA`/`speedT`/`speedS` 分别调速 |
| `speedA` | number | `1` | 0.3–2 | 出现速度 |
| `speedT` | number | `1` | 0.3–2 | 文字速度 |
| `speedS` | number | `1` | 0.3–2 | 逐字速度（`split` 时） |

颜色字段接受 `#rrggbb` 或 `#rrggbbaa`；规范化后小写。`bubbleStyle` 中未知字段**应当**保留。

### 9.2 `bubbleStyles`

自定义气泡样式数组，最多 24 个：

```json
[
  {
    "id": "u_test1",
    "name": "我的样式",
    "o": {"bg": "#ff0000", "shape": "shout", "weight": 700}
  }
]
```

- `id` 必须匹配 `^u_[a-z0-9]{2,16}$`，数组内唯一。
- `name` 去除 `{}`、换行、回车，trim，≤ 20 字符；空则使用本地化“样式”。
- `o` 是第 9.1 节字段的**部分**对象；未写字段继承当前 `bubbleStyle`。
- 超过 24 个的条目丢弃；无效 ID 丢弃。
- 未知字段**应当**保留。

### 9.3 气泡装饰图 `bubbleImages`

键：`tl`, `tr`, `bl`, `br`, `skin`，分别对应四角装饰与皮肤/九宫格图。

- 每张解码后 ≤ 2,097,152 字节。
- 只接受 PNG 或 GIF 字节签名（不接受 JPEG/WebP）。
- 导出时仅当“包含气泡装饰”选项为真时写出；读取方不得因装饰图无效而拒绝角色。
- `bubbleStyle.cUse=false` 或 `skUse=false` 时运行时可不显示对应图，但数据仍保留。

## 10. 宠物与拖拽

### 10.1 宠物 `pet*`

| 字段 | 类型 | 默认 | 范围/枚举 | 语义 |
| --- | --- | --- | --- | --- |
| `petOn` | boolean | `false` | — | 是否启用抚摸/点击爱心效果 |
| `petKinds` | string[] | `["heart"]` | `heart`, `note`, `anger`, `sweat`, `sparkle`, `sleepy`, `img1`, `img2`, `img3` | 粒子种类；去重；`img1`–`img3` 对应 `petImages` |
| `petSizeMode` | string | `random` | `random`, `fixed` | 粒子大小模式 |
| `petSize` | number | `26` | 12–60 | 固定大小 |
| `petFill` | string | `#ffffff` | `#rrggbb` | 填充色 |
| `petLine` | string | `#ffffff` | `#rrggbb` | 描边色 |
| `petLineW` | number | `0` | 0–6 | 描边宽度 |
| `petColorMode` | string | `one` | `one`, `each`, `candy`, `rainbow` | 配色模式 |
| `petKindColors` | object | `{}` | 键为六种内置粒子，值为 `#rrggbb` | 每种粒子的颜色 |
| `petLineFollow` | boolean | `false` | — | 描边是否跟随填充色 |
| `petGlow` | boolean | `true` | — | 是否发光；导出时由 `petEdge` 派生 |
| `petEdge` | string | 由字段派生 | `glow`, `line`, `none` | 边缘模式；优先于 `petGlow`/`petLineW` |
| `petGlowSize` | number | `5` | 1–12 | 发光半径 |
| `petAmount` | number | `4` | 1–10 整数 | 每次粒子数量 |
| `petSpeed` | number | `1100` | 400–2400 | 粒子速度 |
| `petTilt` | number | `3` | 0–10 | 倾斜 |
| `petArea` | string | `all` | `all`, `head` | 发射区域 |
| `petCut` | number | `0` | 0–100 整数 | 裁剪百分比；1–9 视为 10 |
| `petSway` | number | `3` | 0–10 | 摇摆 |

`petEdge` 缺失时按 `petGlow ? "glow" : petLineW > 0 ? "line" : "none"` 派生。写入方**应当**同时写 `petEdge`、`petGlow`、`petLineW`，且保持三者一致：`glow` → `petGlow=true`；`line` → `petLineW>=1`；`none` → `petGlow=false`、`petLineW=0`。

### 10.2 拖拽/举起 `drag*`、`lift*`

| 字段 | 类型 | 默认 | 范围 | 语义 |
| --- | --- | --- | --- | --- |
| `dragOn` | boolean | `false` | — | 是否允许抓起/拖动 |
| `dragSwing` | number | `100` | 0–200 整数 | 拖动摆动幅度 |
| `dragBodyY` | number | `0` | -120–120 整数 | `body2` 垂直位置（基准高 800） |
| `dragBodyS` | number | `100` | 50–200 整数 | `body2` 缩放百分比 |
| `dropSquish` | number | `100` | 0–200 整数 | 放下时压扁 |
| `dropWobble` | number | `100` | 0–200 整数 | 放下时头部晃动 |
| `liftShadow` | boolean | `true` | — | 举起时是否显示落点影子 |
| `liftShadowY` | number | `0` | -120–120 整数 | 落点影子垂直位置（基准高 800） |
| `liftHide` | string[] | `[]` | 元素 < 40 字符，≤ 64 项 | 举起时隐藏的部件/图层 ID |
| `liftStill` | string[] | `[]` | 同上 | 举起时固定的部件/图层 ID |
| `liftOnly` | string[] | `[]` | 同上；不得与 `liftHide` 重复 | 仅举起时显示的部件/图层 ID |
| `liftSad` | string[] | `[]` | 同上 | 举起时呈“沮丧/下垂”的部件 ID |
| `liftAmt` | object | `{}` | 键 < 40 字符，值 0–100 整数，≤ 64 项 | 各部件举起幅度 |
| `liftAux` | object | `{}` | 键 < 40 字符，值 `{drop,spd,fol}` 各 0–100 整数，≤ 64 项 | 部件辅助参数：下垂、速度、跟随 |

ID 可以是骨架部件 ID（如 `r1`）或图层键（`L:back`、`L:side`、`L:ex1` …）。导入时骨架部件 ID 按新 ID 映射；`L:` 键在 `liftHide`、`liftStill`、`liftSad`、`liftOnly`、`liftAux` 中原样保留。

**已知原作限制**：原作 1.19.6 的导入器在 `liftAmt` 中只保留骨架部件 ID，会丢弃 `L:` 键。OCoD **应当**保留 `L:` 键（兼容性超集）；为保证经原作再导出仍保真，样本的 `liftAmt` 只使用骨架部件 ID。

## 11. 部件 `parts` 与 `rigCommon`

`.cvclk` 可携带部件（头部部件与骨架部件）。条目结构与 `.cvpart` 的 `parts` **完全相同**，见 [cvpart-v1.md](cvpart-v1.md) 第 3 节。

差异：

- `.cvclk` 的 `parts` 只在“包含部件”选项为真时出现；该选项同时控制头部部件与骨架部件，最多 16 个。
- 只有包含至少一个骨架部件（`rigId` 存在）时，才写 `rigCommon`。
- `.cvclk` 导入时把头部部件应用到 `partTop`/`partTop2`/`partTop3` 槽位，把骨架部件加入本地 `rig` 列表；不会在本地保留 `parts` 数组。
- 头部部件在 `partOrder` 中的位置标记 `top`/`top2`/`top3` 可被原作导入器丢弃；OCoD 应当保留。

## 12. 导入算法（角色对象）

对每个角色对象：

```
1. name = trim(object.name || object.id || 本地化未命名)
2. 对每个图像槽位：
     a. 若 images[slot] 存在：按 data URL 解码；校验大小与签名。
     b. 若无效或缺失：若 imageUrls[slot] 是 http(s) URL，下载并校验。
     c. 仍然无效：槽位为空。
3. 若 body 与 head1 均为空：跳过该角色，记录原因。
4. 按第 5.4 节补齐 head1/head2/body/back2/side2。
5. 创建本地角色目录与 ID（按本地命名规则；原作按 name 生成安全 ID）。
6. 写入所有有效槽位图像。
7. 写入标量字段；缺失字段使用默认值；按第 3 节范围钳制。
8. 处理 author、bubbleStyle、bubbleStyles、lineStyles。
9. 处理 lineSounds：校验 60 个文件上限；按台词文本匹配；重新命名音频文件。
10. 处理 sound：有效则保存并设置 soundFile；custom 无有效文件则重置 click。
11. 处理 font：有效则保存并设置 fontFile/fontName。
12. 处理 parts：头部部件写入 partTop* 槽位；骨架部件加入 rig 并映射 ID。
13. 处理 partOrder：映射骨架 ID；保留 L: 键；丢弃或保留未知值（见 6.2）。
14. 处理 bubbleImages / petImages：仅 PNG/GIF，≤ 2 MiB。
15. 按本地模型规范化所有字段（范围、枚举、颜色、数组长度）。
```

任何单个角色失败只影响该角色；导入器返回成功列表、跳过列表及原因。

## 13. 导出算法（角色对象）

```
1. 选择角色；跳过本地 locked（受保护不可再导出）的角色。
2. 对完整作者对象做过滤检查；命中则拒绝整个导出。
3. 读取并规范化角色。
4. 对每个图像槽位：读取本地文件；若为本地密封格式则解密；转为 data URL。
5. 写入所有标量字段（第 3–10 节）。
6. sound：有自定义音频则写 data URL，否则写布尔值。
7. lineSounds：按第 8.2 节收集有效音频与引用；无有效条目则省略。
8. font：有字体则写 data URL 与 fontName。
9. parts：按“包含部件”选项收集头部部件与骨架部件；最多 16 个；有骨架部件时写 rigCommon。
10. bubbleImages：按“包含气泡装饰”选项收集；petImages 总是收集有效图。
11. 写 images / imageUrls:{}, lines / idleLines / 八个 sp_* 数组、bubbleStyle(s)、lineStyles。
12. 组装顶层载荷并交给容器写入。
```

写入方**必须**保证：

- `images.body`、`images.head1` 存在且有效；`head2` 应当存在。
- 任何 data URL 都不超过对应大小上限。
- 图像 MIME 应当尽量与字节一致；原作对本地 `.bcp` 图像固定写 `image/png`，读取方必须以嗅探结果为准。
- `lineStyles` 数组长度不超过对应台词数组；超出部分截断。
- `partOrder` 中的骨架 ID 与 `parts` 中的 `rigId` 一致。
- 不写受保护包独有字段，除非按 [protected-cvclk.md](protected-cvclk.md) 导出受保护包。

## 14. 未知字段保留的具体位置

以下位置**应当**保留未知字段：

| 位置 | 说明 |
| --- | --- |
| 顶层载荷 | 未来新增的 `foo`、`extensions` 等 |
| 角色对象 | 未来新增的角色字段；也包括 `sp_g*`、`groupNames` |
| `author` | 未来新增的作者字段 |
| `bubbleStyle` / `bubbleStyles[].o` | 未来新增的气泡字段 |
| `layers` | 未来新增的图层键 |
| `partOrder` | 未知的绘制位置值（不得导致导入失败） |
| `lineStyles` | 未知台词键或未知样式 ID（运行时忽略，但再导出保留） |
| `parts[]` / `parts[].settings` | 未来新增的部件字段 |
| `rigCommon` | 未来新增的骨架公共参数 |
| `images` | 未来新增的图像槽位（值按 data URL 处理） |
| `bubbleImages` / `petImages` | 未来新增的装饰/宠物槽位 |
| `lineSounds` / `lineSoundFiles` | 未来新增的台词音键（但运行时只使用 lines/idleLines） |

例外：

- 受保护包的权限字段按 [protected-cvclk.md](protected-cvclk.md) 处理，不得通过未知字段保留绕过。
- 已知字段类型错误时按本契约规范化，不保留错误类型。
- 若未知字段与已知字段同名但类型不同，以已知字段规则为准。

## 15. 必填/可选速查

| 类别 | 必填 | 可选（有默认） |
| --- | --- | --- |
| 顶层 | `format`, `characters` | `version`, `app`, `exportedAt` |
| 角色 | 至少 `images.body`/`images.head1` 之一；`name` 可回退 | 其余全部 |
| 图像 | `body`, `head1` | `head2` 及其余槽位 |
| 台词 | 无 | 所有数组 |
| 声音 | 无 | `sound`, `lineSounds`, `font` |
| 部件 | 无 | `parts`, `rigCommon` |
| 气泡 | 无 | `bubbleStyle`, `bubbleStyles`, `bubbleImages` |
| 宠物 | 无 | 所有 `pet*`, `petImages` |
| 拖拽 | 无 | 所有 `drag*`, `lift*` |

## 16. 已知事实缺口

以下项目需要真实应用或更多样本确认，已记录但不阻塞普通包实现：

1. `soundPitch` 在 1.19.6 的运行时未被引用；确认是否为历史遗留字段。
2. `reactStyle`/`reactAmp` 仅作为 `beatMotion`/`keyMotion` 的回退；确认旧文件是否只带这两个字段。
3. `top`/`top2`/`top3`、`hair`、`front` 在 `partOrder` 导入时被原作丢弃；确认这是否影响头部部件显示。
4. 自定义提醒组 `sp_g*`/`groupNames` 不被 v1 导出器写出；确认原作未来版本是否会补。
5. `imageUrls` 的网络回退在实际应用中的超时与并发行为未通过运行验证。
6. 图像尺寸/动画帧数对运行时性能的影响不在交换契约内；样本已覆盖 PNG 与 GIF 两种签名。
7. 原作把本地 `.bcp` 图像在再导出时统一标为 `image/png`，即使底层是 GIF/JPEG/WebP；读取方必须按字节嗅探。
8. 原作导入器会丢弃未知字段、`liftAmt` 中的 `L:` 键，以及 `partOrder` 中的 `top`/`top2`/`top3`/`hair`/`front`；OCoD 按本契约作为兼容性超集保留。
