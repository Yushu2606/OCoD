# OCoD 洁净室固定测试样本

本目录是第 1、2 张票（GitHub #2、#3）的固定样本集。样本与 [../docs/specs/](../docs/specs/) 一起构成实现方的全部输入。实现方**不应**阅读 Bug Clicker 的解包产物。

## 1. 目录

```
samples/
  README.md                 本文件
  VERIFICATION.md           Windows / macOS 原作实机验收清单
  PROVENANCE.md             逐文件来源与交付边界
  SHA256SUMS                分析方全量 SHA-256（含 expected 与 verification）
  SHA256SUMS.handoff        实现方交付清单的 SHA-256（不含 verification）
  cvclk/                    自製完整角色包
    plain-minimal.cvclk
    plain-full.cvclk
    plain-multi.cvclk
    unknown-fields.cvclk
    animated-gif.cvclk
  cvpart/                   自製独立部件包
    headtop.cvpart
    rig.cvpart
  lines/                    自製台词文件
    click-idle.txt          UTF-8 BOM + CRLF
    click-idle.csv          与 .txt 同文本，LF，无 BOM（证明扩展名不影响解析）
    click-idle.md           与 .csv 逐字节相同
    unsectioned.txt         无区块标题
    styles.txt              样式前缀边界
  legacy/                   旧版 carnovella-clicker 纯 JSON
    carnovella-clicker.json
    carnovella-clicker-defaults.json
  protected/                受保护 .cvclk 与锁定 .cvpart（第 2 张票）
    README.md               样本、测试口令与预期语义
    VERIFICATION.md         受保护包实机验收清单
  expected/                 每个样本的机器可读语义摘要
  verification/             原作模块级再导出夹具（见第 6 节）
```

实测截图与证据由分析方在**仓外私有目录**保留，不随公开仓库提交。

## 2. 自製样本清单与 SHA-256

| 样本 | 内容 | SHA-256 |
| --- | --- | --- |
| `cvclk/plain-minimal.cvclk` | 1 角色；3 张图；2 点击台词；1 自语台词 | `480f573a6b867a5c9329b166e0555a8c419b10e67a94b094b7f670283d4616ee` |
| `cvclk/plain-full.cvclk` | 1 角色；19 张槽位图；3 头部部件；4 骨架部件；5 点击/3 自语/16 特殊台词；气泡样式、装饰图、宠物图、自定义音、台词音 | `93547ce83b5829d182ff317dc5820b024364c63afe1f5239c193fb9422c35dd2` |
| `cvclk/plain-multi.cvclk` | 3 角色；中/英/日文名；一个角色缺 `head2`；一个角色缺 `name` | `7aef38630783e4698012f722230b4fb99035168ffbcba1cbda9f6a189e644493` |
| `cvclk/unknown-fields.cvclk` | 1 角色；顶层/角色/作者/气泡/图像/partOrder 内的未知字段；未知 `sp_g1_before`/`groupNames` | `c77bf5db4112a12f055943a18c5af19cfb03f656d7556fd3433605fc17b7b2ff` |
| `cvclk/animated-gif.cvclk` | 1 角色；`head3` 是 96×96 两帧 GIF（睁眼/闭眼）；`blinkMs=1200` | `0c65697241c02a0fefe857b8e59b6d0a7704008eccad838445c54bc50a3e7b4f` |
| `cvpart/headtop.cvpart` | 3 个头部部件（含影子/眨眼图） | `f32181c176af3bad9c55d2e38661e9e4a23f547bfda904368a2b880674a29c28` |
| `cvpart/rig.cvpart` | 耳朵/尾巴/翅膀/道具 4 个骨架部件 | `1173e5b22bf46da7db9db704fb984cfe21557a097f27f0feb7197f8ddaad8307` |
| `lines/click-idle.txt` | 2 区块、4 点击、3 自语、样式与未知样式、`\n` 换行、逗号 | `8aada4364515807dcfeac3f1edfffe2d04f6a40e3f4911feb9392e94016551b3` |
| `lines/click-idle.csv` | 与 `.txt` 同文本；LF、无 BOM | `cb8fbc5ea80d520257cac100275a031ead7d3125045a4d98f3452652f28e0b5b` |
| `lines/click-idle.md` | 与 `.csv` 逐字节相同 | `cb8fbc5ea80d520257cac100275a031ead7d3125045a4d98f3452652f28e0b5b` |
| `lines/unsectioned.txt` | 3 行；无区块标题 | `f5af1546aeb5f118f697ba32e14a79600ee9deee99ffe116a03aade4d1b71b9a` |
| `lines/styles.txt` | 内置/未知/备忘词/数字样式前缀边界 | `13a1b7e48aae4a38c715be6a6904e29d7bfced3fb539bd7a2aeee425d5af4666` |
| `legacy/carnovella-clicker.json` | 旧版 JSON；3 图；2 点击/1 自语 | `04d6ca7843eecbe081ca037e84262d1bdfccde69c37676305908826fb92d2f42` |
| `legacy/carnovella-clicker-defaults.json` | 旧版 JSON；仅 `body`/`head1`，其余默认 | `fb60f48e7b079b3c232cd5bddd8e9837d7fd98030b6ea1596eb4caa229e486df` |

`SHA256SUMS` 是分析方全量清单，包含 `expected/`、`protected/` 与 `verification/` 下全部文件。`SHA256SUMS.handoff` 是实现方交付清单，只含自製样本（含 `protected/`）与 `expected/`，不含 `verification/`。逐文件来源见 [`PROVENANCE.md`](PROVENANCE.md)。

受保护样本（`protected/` 下 7 个文件）的说明与口令见 [`protected/README.md`](protected/README.md)。

## 3. 受保护样本

`samples/protected/` 是第 2 张票的固定样本，覆盖 `locked`、`editLock`、`owner` 口令恢复、编辑锁定和锁定部件。它们同样是自製样本，可由实现方使用；细节见 [`protected/README.md`](protected/README.md) 与 [`protected/VERIFICATION.md`](protected/VERIFICATION.md)。契约见 [`docs/specs/protected-cvclk.md`](../docs/specs/protected-cvclk.md)。

## 4. 预期可见语义

### 4.1 样本素材的统一设计

除 `animated-gif.cvclk` 的 `head3` 外，所有自製图像都是 480×480 透明 PNG；`head3` 是 96×96 两帧 GIF。每个 PNG 图层用不同颜色和形状绘制，并在图层中央印上槽位名（如 `BODY`、`HEAD1`、`EX1`）。导入后：

- 角色是这些图层的叠加；每个槽位名都能在对应位置看到。
- 标签底色为深色半透明，白字带黑边，便于肉眼确认哪一层被正确导入。
- 颜色：橙色系为 `plain-minimal` / `plain-full` / `legacy`；青柠色为英文角色；紫葡萄色为日文角色。
- 透明区域为 `(0,0,0,0)`；不依赖网络或系统字体。

### 4.2 `plain-minimal.cvclk`

- 角色名：`净室样本·小橙`。
- 可见：`BODY` 圆角身体、`HEAD1` 头部、`HEAD2` 按下头部。没有后发、侧发、追加图、部件。
- 点击：依次显示 `你好，我是小橙。`、`这是一条点击台词。`（`inOrder: true`）。
- 自语：`（小橙在发呆。）`，间隔 60 秒。
- 声音：关闭（`sound: false`）。
- 气泡：默认样式。

### 4.3 `plain-full.cvclk`

- 角色名：`净室样本·彩绘`。
- 可见：19 个槽位标签 `BACK1/BACK2/HEAD1..HEAD5/BODY/BODY2/SIDE1/SIDE2/EX1/EX1P/.../EX4P` 全部叠加；头部部件 `TOP/TOP2/TOP3`；骨架部件 `EAR/TAIL/WING/PROP`。
- 头部：`head3` 眨眼、`head4` 抚摸/拖动脸、`head5` 拖动脸、`body2` 拖动身体。
- 图层：`back` 在身体后；`ex1` 在身体与头之间；`side`/`ex4` 在头前；`backX=12, backY=-8`，`ex1X=6, ex1Y=4`，`ex4X=-10, ex4Y=6`。
- 台词：
  - 点击 5 条，其中第 2 条使用自定义 `Glow` 样式，第 3 条含真换行，第 4 条使用内置大喊样式，第 5 条含逗号。
  - 自语 3 条，第 2 条使用内置自语样式。
  - 八个特殊台词键各 2 条，含 `{memo}` 占位符；`sp_alarm_before` 与 `sp_todo_after` 带样式。
- 台词音：点击第 1、3 条与自语第 2 条各有一个 WAV；导入后文件名会改成新的 `lsnd_*`，语义不变。
- 自定义点击音：WAV；`soundType: custom`。
- 气泡：深色渐变 + 实线边框 + 手绘形状 + 弹出出现 + 波浪文字；两个自定义样式 `Glow`、`Paper`。
- 气泡装饰图：四角 + 皮肤九宫格；宠物图：心/音符/星。
- 拖拽：开启；`liftStill` 含耳朵与 `L:side`；`liftAmt` 仅含尾巴 `r2`（避免原作丢弃 `L:` 键）；`liftAux` 含 `L:side`。
- 起始大小：120px。

### 4.4 `plain-multi.cvclk`

- 3 个角色：
  1. `Cleanroom Sample Lime`：青柠色，3 张图，2 点击/1 自语。
  2. `クリーンルーム見本`：紫葡萄色，**只有 `body`/`head1`**；导入器必须把 `head1` 回退为 `head2`。2 点击/1 自语。
  3. 无 `name` 的角色：显示名回退为 `id`（`unnamed-sample-orange`）；1 点击；0 自语。
- 用于验证多角色包、`head2` 回退、`name` 回退。

### 4.5 `cvclk/unknown-fields.cvclk`

- 角色名：`净室样本·未知字段`；常规 `BODY/HEAD1/HEAD2` 与 2 点击/1 自语。
- 未知内容：
  - 顶层 `x_sample = {"keep": true}`
  - 角色 `x_future_field`、`x_future_object`
  - `author.x_future_author`
  - `bubbleStyle.x_future_bubble`
  - `images.xFuture`（未知图像槽位）
  - `partOrder` 中的 `xFutureLayer`
  - `lineStyles.x_future_lines`
  - `groupNames`、`sp_g1_before`
- 原作行为：导入成功；未知字段被忽略，再导出丢失。
- OCoD 行为：导入成功；未知字段在对应对象层级保留，再导出时带回。该样本用于 OCoD 的未知字段保留测试，不用于要求原作再导出保真。

### 4.6 `cvpart/headtop.cvpart`

- `from: 净室样本·彩绘`。
- `headTop`：名 `蝴蝶结`，大小 120，Y=-10，Hop=20，When=up，Soft=40，Squish=40，Every=1800，影子 soft/#552200/0.35，含影子图和眨眼图。
- `headTop2`：名 `光环`，大小 90，Y=-30，影子 dot/#aa8800/0.25。
- `headTop3`：名 `小旗`，大小 80，X=20，Y=-20，含眨眼图。
- 导入到任意角色后，三个槽位都应出现对应的 `TOP/TOP2/TOP3` 标签，且设置值应读回一致。

### 4.7 `cvpart/rig.cvpart`

- `ear`：猫耳，`name=猫耳`，`soft=55`，`bounce=70`，`drop=true`，`at=mid`。
- `tail`：尾巴，`name=尾巴`，`dir=20`，`curve=-30`，`fur=R`，`onPet=wag`，`at=back`。
- `wing`：翅膀，`name=翅膀`，`flaps=2`，`y=-40`，`at=back`。
- `prop`：小球，`name=小球`，`hopH=80`，`hopEvery=2`，`on=false`，`at=front`。
- `rigCommon`：mesh=14，joint=55，tex=0.7，lag=5。
- 导入后的 `partOrder`：背部件在 `body` 前，耳在 `body` 与 `head` 之间，道具在 `head` 后；`on=false` 的道具仍应被添加但不显示。

### 4.8 台词文件

`lines/click-idle.*` 的预期解析：

| 键 | 值 |
| --- | --- |
| `lines` | `你好，我是净室样本。`；`不要戳我！`；`这一行有\n换行。`（真换行）；`逗号,不是列,会原样保留。` |
| `lines` 样式 | `""`；`외침`；`""`；`""` |
| `idleLines` | `（发呆）……`；`这一行保留花括号。`；`{메모} 这也是普通台词。` |
| `idleLines` 样式 | `""`；`unknown`；`""` |

`lines/unsectioned.txt`：无区块；3 行都进入当前台词页（通常是 `lines`），`plain` 有 3 项。

`lines/styles.txt` 的 6 条点击行。下表是**解析器结果**；导入到角色时，解析器记录的样式名还会在目标角色样式表中解析，未命中则整行（含花括号）按普通台词导入：

| 行 | 解析器文本 / 样式 | 导入到无该样式的角色后 |
| --- | --- | --- |
| `{외침} 内置大喊` | `内置大喊` / `외침` | 文本 `内置大喊`，样式解析为内置大喊 |
| `{외침}` | 无正文 → 整行原文 / 空 | 整行 `{외침}` |
| `{unknown} 未知样式` | `未知样式` / `unknown` | 整行 `{unknown} 未知样式`，样式空 |
| `{메모} 备忘词不是样式` | 整行原文 / 空 | 整行原文 |
| `{123} 数字不是样式` | `数字不是样式` / `123` | 整行 `{123} 数字不是样式`，样式空 |
| `普通台词` | `普通台词` / 空 | `普通台词`，样式空 |

### 4.9 `cvclk/animated-gif.cvclk`

- 角色名：`净室样本·动图`；青柠色 `BODY/HEAD1/HEAD2` 与 2 点击/1 自语。
- `head3` 是 96×96 两帧 GIF：第 1 帧睁眼，第 2 帧闭眼；透明背景、4 色调色板、0.5 秒/帧、无限循环。
- `blinkMs=1200`，因此约 1.2 秒触发一次眨眼，肉眼可确认动画在播放。
- 原作导入后把 `head3` 本地保存为 `.bcp`，再导出时 MIME 可能写成 `image/png`，但字节仍是 GIF；读取方必须按字节嗅探。
- 用于验证 GIF 动图导入、显示和再导出保真。

### 4.10 旧版 JSON

- `legacy/carnovella-clicker.json`：角色 `Carnovella 旧角色`，3 图，作者，45 秒自语，2 点击/1 自语，声音关闭。
- `legacy/carnovella-clicker-defaults.json`：角色 `Legacy Defaults`，只有 `body`/`head1`；导入后 `head2` 回退为 `head1`，其余字段默认（`sound=true`、`idleMs=0`、`lines=[]`）。
- OCoD 不得再导出为 `carnovella-clicker`；再导出必须是 `bug-clicker` v1。

## 5. `expected/` 摘要

每个样本对应一个 `.expected.json`，内容包括：

- 样本文件的 SHA-256。
- 容器头部（`format`、`version`、`locked`）。
- 载荷格式与角色/部件数量。
- 每个角色的名称、ID、图像槽位与每张图的 `{mime, bytes, sha256}`、台词、自语、样式、特殊台词计数、部件槽位、气泡摘要、声音/台词音摘要、`partOrder`、`layers`、`rigCommon`。
- 部件包的每个部件槽位、名称、`rigId`、`on`、`at`、设置与图像摘要。

这些摘要用于机器校验，不替代人眼验收；可见语义以第 4 节为准。

## 6. 生成、校验与查看

要求 Node.js ≥ 20（脚本只使用标准库）。

```bash
# 重新生成全部自製样本、expected 摘要与 SHA256SUMS
node tools/cleanroom/samples.mjs generate

# 校验：
#  - 每个自製样本与生成器逐字节一致
#  - expected 摘要一致
#  - SHA256SUMS 与实际文件一致
#  - 容器头部/载荷/数量结构合法
node tools/cleanroom/samples.mjs verify

# 查看一个容器或文本样本
node tools/cleanroom/samples.mjs inspect samples/cvclk/plain-full.cvclk
node tools/cleanroom/samples.mjs inspect samples/lines/click-idle.txt
```

生成器**不读取**原作，不联网；PNG 与容器使用确定性的固定 Huffman LZ77 压缩，因此同一脚本可跨 zlib 版本重建相同字节。`generate` 会重写 `SHA256SUMS`（含 `verification/`）与 `SHA256SUMS.handoff`（不含 `verification/`）。

`tools/cleanroom/` 是分析方工具，不属于实现方交付物。实现方**不需要**运行生成器；用自己的实现读取 `samples/` 并与 `expected/`、`SHA256SUMS.handoff` 对照即可。分析方用 `inspect` 做交叉检查。

## 7. `verification/` 再导出夹具

`samples/verification/<平台>/` 下的文件由**原作 1.19.6 / 1.4.6 的导入导出模块**在模块级执行：

1. 用原作导入器导入本目录的自製样本。
2. 用原作导出器再导出。
3. 保存再导出结果。

用途：证明“样本经原作导出后可再次导入”。它们**不是**真实应用界面的验收结果；真实应用验收见 [VERIFICATION.md](VERIFICATION.md)。

**交付边界**：`verification/` 是分析方的对照夹具，**不交给实现方**；实现方只拿自製样本、`expected/` 与 `SHA256SUMS.handoff`。详见 [`PROVENANCE.md`](PROVENANCE.md)。

| 目录 | 说明 |
| --- | --- |
| `verification/win-1.19.6/` | 使用 Windows 1.19.6 的导入/导出/部件模块，`app` 写 `1.19.6` |
| `verification/mac-1.4.6/` | 使用 macOS 1.4.6 的导入/导出/部件模块，`app` 写 `1.4.6` |

这些文件包含随机盐/nonce/导出时间，因此每次再生成字节不同；它们由 SHA256SUMS 固定为当前版本，不作为确定性重建目标。

## 8. 验收状态

- 自製样本：`samples.mjs verify` 通过。
- 原作模块级导入/再导出/再导入：通过（Windows 1.19.6 与 macOS 1.4.6 的交换模块逐字节一致）。
- Windows 实机界面验收：通过（2026-11-17，应用代码 1.19.6 ASAR + 1.19.5 Electron 外壳；保存对话框桩；证据为分析方私有，不在仓内）。
- macOS 实机界面验收：通过（2026-11-17，官方 `/Applications/BUG Clicker.app` 1.4.6 + Electron 44.2.0；原件未改动，`userData` 指向临时目录；原生保存对话框；证据为分析方私有，不在仓内）。
- 受保护样本：模块级原作验证通过（锁定、编辑锁、口令正确/错误、锁定部件）；Windows 与 macOS 界面验收均通过，见 [`protected/VERIFICATION.md`](protected/VERIFICATION.md)。

## 9. 已知限制与注意

1. `lineSoundFiles` 的文件名必须匹配 `^lsnd_[a-z0-9]{6,16}\.(wav|mp3|ogg|m4a)$`；少于 6 个字母数字的文件名会被原作忽略。样本使用 `lsnd_click1.wav`、`lsnd_click2.wav`、`lsnd_idle01.wav`。
2. 原作导入器会丢弃 `liftAmt` 中的 `L:` 键；样本只在该字段使用骨架部件 ID。OCoD 应保留 `L:` 键作为兼容性超集。
3. 原作导入器会丢弃 `partOrder` 中的 `top`/`top2`/`top3`/`hair`/`front`；样本的头部部件仍通过 `parts` 正确导入。OCoD 应保留这些值。
4. 台词文件 `.csv`/`.md` 不是真正的 CSV/Markdown；样本的 `.csv`/`.md` 与 `.txt` 文本相同，仅编码/换行不同。
5. 台词样式名跨语言解析有局限；样本使用内置韩文名 `외침` 作为可跨语言解析的前缀。
6. 样本图像是示意用的几何图形，不是美术素材；它们只用于验证字段、层级和交换语义。
7. 原作会忽略并丢弃未知字段（见 `unknown-fields.cvclk`）；OCoD 按契约保留，不以原作再导出结果作为未知字段保真的判据。
8. 原作再导出本地 `.bcp` 图像时固定写 `image/png`；GIF/JPEG/WebP 的字节可能被标成 PNG。读取方必须按字节嗅探，不得按 MIME 拒绝。
