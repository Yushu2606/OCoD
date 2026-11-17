# OCoD 洁净室交换契约（首版）

本目录是 OCoD 首版与 Bug Clicker 文件交换的外部契约，以及随契约交付的固定测试样本说明。实现方只阅读本目录、`samples/` 和后续补齐的规格；不阅读 Bug Clicker 的解包产物。

状态：第一版已交付。容器与交换模块已用 Windows 1.19.6 / macOS 1.4.6 的原作模块做导入、再导出、再导入验证；Windows 与 macOS 实机界面验收均已执行（Windows 有外壳与保存对话框限制；macOS 使用官方 1.4.6.app + Electron 44.2.0，原件未改动）。见 [../samples/VERIFICATION.md](../../samples/VERIFICATION.md)；实机证据为分析方私有，不在仓内。

## 文档

| 文档 | 内容 |
| --- | --- |
| [container-v2.md](container-v2.md) | `.cvclk` / `.cvpart` 共用的二进制容器、密钥派生、压缩与错误条件 |
| [cvclk-v1.md](cvclk-v1.md) | 完整角色包 `.cvclk` 的顶层结构、角色对象字段、图像、部件、声音、未知字段保留规则 |
| [cvpart-v1.md](cvpart-v1.md) | 独立部件包 `.cvpart` 的结构、部件条目、头部部件与骨架部件设置 |
| [lines-files.md](lines-files.md) | `.txt` / `.csv` / `.md` 台词文件的语法、导入解析、导出格式、编码与样式前缀 |
| [carnovella-clicker-json.md](carnovella-clicker-json.md) | 旧版 `carnovella-clicker` JSON 的导入契约与已知证据缺口 |
| [protected-cvclk.md](protected-cvclk.md) | 受保护 `.cvclk` / 锁定 `.cvpart` 的权限、口令恢复、编辑与再导出限制 |
| [author-and-bubble-reference.md](author-and-bubble-reference.md) | 作者字段、作者链接白名单、作者名过滤、气泡样式与预设的完整参考 |
| [../samples/README.md](../../samples/README.md) | 固定样本目录、SHA-256、预期语义、生成与校验方式 |
| [../samples/PROVENANCE.md](../../samples/PROVENANCE.md) | 逐文件来源与实现方交付边界 |
| [../samples/VERIFICATION.md](../../samples/VERIFICATION.md) | 普通包 Windows / macOS 原作实机验收清单 |
| [../samples/protected/README.md](../../samples/protected/README.md) | 受保护包固定样本、测试口令与预期语义 |
| [../samples/protected/VERIFICATION.md](../../samples/protected/VERIFICATION.md) | 受保护包 Windows / macOS 原作实机验收清单 |

## 术语

本目录沿用根目录 [GLOSSARY.md](../../GLOSSARY.md) 的术语：

- **角色包**：`.cvclk`，可包含一个或多个角色。
- **部件包**：`.cvpart`，最多 16 个独立部件。
- **台词文件**：`.txt`、`.csv`、`.md`，点击台词与自言自语台词。
- **旧版 JSON**：`format` 为 `carnovella-clicker` 的角色 JSON。
- **原作**：Bug Clicker Windows 1.19.6 与 macOS 1.4.6。
- **实现方**：只依据本契约和样本实现 OCoD 的独立开发者或代理。

## 规范用语

- **必须**（MUST）：不满足即不兼容。
- **应当**（SHOULD）：不满足时须在实现说明中记录理由。
- **可以**（MAY）：可选行为。

JSON 示例中的字段顺序不构成契约；字段存在与否、类型、取值范围和语义才构成契约。除特别注明外，字符串均为 UTF-8，数字均为 JSON number。

## 兼容基线

| 项目 | 原作基线 |
| --- | --- |
| Windows | Bug Clicker 1.19.6，ASAR SHA-256 `5c3c035cf284c527f80bc326afa18ef581a0ec8d3a1ebce44241df11b5a8b903` |
| macOS | Bug Clicker 1.4.6（官方 `/Applications/BUG Clicker.app`，Electron 44.2.0），ASAR SHA-256 `fcf5b823f1dc39036008088ceb6470ded83f129774f71319eba59db0baf5bcf5` |
| 共同模块 | 两个平台的角色导入、导出、部件和角色模块逐字节一致；容器常量与算法一致 |

本契约以静态检查和模块级执行结果为依据。需要真实应用界面确认的项目集中在 [../samples/VERIFICATION.md](../../samples/VERIFICATION.md)。

## 本票范围

本契约覆盖：

- 普通 `.cvclk`（`bug-clicker` v1 角色对象，外层 `BCVCLK2` 容器）。
- 普通 `.cvpart`（`bug-clicker-part` v1，外层 `BCVPART1` 容器）。
- `.txt` / `.csv` / `.md` 台词文件。
- 旧版 `carnovella-clicker` JSON。
- 上述交付物的字段、语义、必填与可选内容、默认值、取值范围、未知字段保留规则。
- 固定样本及预期语义。

受保护角色包的权限行为（锁定原始编辑字段、禁止再导出、口令恢复）见 [protected-cvclk.md](protected-cvclk.md)。受保护包与普通包共享的容器和角色字段在本契约中说明。

## 受保护角色包

受保护 `.cvclk` 与锁定 `.cvpart` 的契约见 [protected-cvclk.md](protected-cvclk.md)。第 1 张票（GitHub #2）只记录普通包共享字段和缺口；第 2 张票（GitHub #3）已补齐：

- 容器头部 `locked` / `owner` 的语义与生成、校验。
- 普通导入对 `locked`、`editLock`、`owner` 三类标记的识别与逐角色路由；不支持受保护包时也必须识别全部标记（见 [container-v2.md](container-v2.md) 第 7 节、[protected-cvclk.md](protected-cvclk.md) 第 1.1 节）。
- 角色字段 `editLock`、`editBase`、`editSty` 的基线、合并与编辑白名单。
- 口令恢复流程与错误路径。
- 再导出限制、编辑限制、受保护 `.cvpart` 行为。
- 固定受保护样本与实机验收清单。

## 未知字段保留规则（总则）

原作 1.19.6 的导入器只读取已知字段，未知字段在再导出时丢失。OCoD 按 [ADR 0001](../adr/0001-character-distribution-compatibility.md) 的要求比原作更严格：

- 读取方**必须**忽略未知字段，不得因未知字段报错。
- 读取方**应当**在角色对象、气泡样式对象、部件设置对象等每个对象层级保留未知字段的原始 JSON 值。
- 再导出时，未修改的对象**必须**原样带回未知字段；被修改的对象**应当**保留未被修改的未知字段。
- 已知字段的类型、默认值和规范化规则优先于未知字段保留；同名已知字段按本契约规范化。
- 未知字段保留不得绕过受保护包的权限约束。

各文档会给出更具体的保留位置和例外。

## 交付边界

实现方应收到：

- `docs/specs/**`（本目录全部契约）。
- `samples/cvclk/**`、`samples/cvpart/**`、`samples/lines/**`、`samples/legacy/**`、`samples/protected/**`（自製样本，含受保护样本）。
- `samples/expected/**`（预期摘要，含 `expected/protected/**`）。
- `samples/SHA256SUMS.handoff`（交付清单）。
- `samples/README.md`、`samples/VERIFICATION.md`、`samples/PROVENANCE.md`（说明文档）。
- `docs/handoff/README.md` 与 `docs/handoff/MANIFEST.sha256`（独立交接快照的说明与逐文件哈希；接收记录见 `docs/handoff/RECEIPT.md`）。

**不交给实现方**：

- `samples/verification/**`：分析方的原作模块级再导出对照夹具。
- 实机证据：分析方仓外私有目录，不随公开仓库提交。
- `tools/cleanroom/**`：分析方生成/校验工具。
- 任何原作 ASAR 解包产物。

逐文件来源见 [`samples/PROVENANCE.md`](../../samples/PROVENANCE.md)。若实现方发现事实缺口，由分析方补充规格或新增自製样本，不直接转交对照夹具。

## 交接快照

按 [ADR-0007](../adr/0007-cleanroom-handoff.md)，实际交付集合以独立的 `OCoD-handoff-2026-11-17` 快照为准，不以下面整个公开仓为准。快照说明与方法见 [`docs/handoff/README.md`](../handoff/README.md)，逐文件哈希见 [`docs/handoff/MANIFEST.sha256`](../handoff/MANIFEST.sha256)，交付记录见 [`docs/handoff/RECEIPT.md`](../handoff/RECEIPT.md)。公开仓中的 `samples/verification/**` 与 `tools/cleanroom/**` 是分析方专用，不属于快照。

## 样本与验证

- 固定样本位于 [`samples/`](../../samples/)，包含普通 `.cvclk`、受保护 `.cvclk`、`.cvpart`、三种台词文件和旧版 JSON。
- 实现方交付清单的 SHA-256 见 [`samples/SHA256SUMS.handoff`](../../samples/SHA256SUMS.handoff)；分析方全量清单见 `samples/SHA256SUMS`。
- 样本生成器 `tools/cleanroom/samples.mjs` 是分析方工具，不读取原作，可离线重建全部自製样本；不属于实现方交付物。
- 样本语义见 [`samples/README.md`](../../samples/README.md)。
- 普通包实机验收见 [`samples/VERIFICATION.md`](../../samples/VERIFICATION.md)；受保护包实机验收见 [`samples/protected/VERIFICATION.md`](../../samples/protected/VERIFICATION.md)。在 Windows 与 macOS 两个平台完成对应清单之前，相关票不关闭。

## 变更规则

本契约是首版冻结基线。发现事实缺口时：

1. 分析方补充本目录或新增附录。
2. 补充内容必须附带可重跑的样本或模块级证据。
3. 不修改已发布样本的字节；新增样本单独列出 SHA-256。
4. 与本契约冲突的实现决策，须在 `docs/adr/` 新开 ADR 说明。
