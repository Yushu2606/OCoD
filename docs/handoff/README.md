# 洁净室交接快照（ADR-0007）

本目录记录分析方向实现方交付的可核实材料。公开仓库同时包含分析方专用路径；实现方可见集以**独立快照**为准，而不是以整个公开仓库为准。

## 快照

| 项目 | 值 |
| --- | --- |
| 交接 ID | `OCoD-2026-11-17-01` |
| 快照目录 | 仓外（与仓库同级）`OCoD-handoff-2026-11-17/` |
| 归档 | 仓外（与仓库同级）`OCoD-handoff-2026-11-17.tar.gz` |
| 文件清单 | [`MANIFEST.sha256`](MANIFEST.sha256)（路径相对快照根目录） |
| 交付记录 | 公开仓 `docs/handoff/RECEIPT.md`（不随快照复制，避免归档哈希自引用） |

快照包含：

- `docs/specs/**`：全部契约文档。
- `docs/handoff/README.md` 与 `docs/handoff/MANIFEST.sha256`。
- `samples/README.md`、`samples/VERIFICATION.md`、`samples/PROVENANCE.md`。
- `samples/SHA256SUMS.handoff`。
- `samples/protected/README.md`、`samples/protected/VERIFICATION.md`。
- `samples/cvclk/**`、`samples/cvpart/**`、`samples/lines/**`、`samples/legacy/**`、`samples/expected/**`、`samples/protected/*.cvclk`、`samples/protected/*.cvpart` 中由 `SHA256SUMS.handoff` 列出的自製样本与预期摘要。

快照**不包含**（分析方专用）：

- `samples/verification/**`：原作模块级再导出对照夹具。
- `tools/cleanroom/**`：分析方生成/校验工具。
- `samples/evidence/**` / 仓外证据：实机验收截图与说明。
- `.git/**`、`src/**`、`tests/**`、构建产物，以及任何原作 ASAR 解包产物。

## 校验

在快照根目录运行：

```sh
shasum -a 256 -c docs/handoff/MANIFEST.sha256
```

清单只覆盖允许交付的内容文件；`docs/handoff/MANIFEST.sha256` 与 `RECEIPT.md` 本身不计入自身哈希。归档 SHA-256、文件数和接收回执见公开仓 `docs/handoff/RECEIPT.md`。

## 边界说明

公开仓中的 `samples/verification/**` 与 `tools/cleanroom/**` 是分析方专用材料，不属于本交接；实现方不应把它们当作输入。若实现方发现事实缺口，由分析方补充规格或新增自製样本，不直接转交对照夹具。
