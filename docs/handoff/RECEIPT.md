# 洁净室交接接收记录

| 项目 | 值 |
| --- | --- |
| 交接 ID | `OCoD-2026-11-17-01` |
| 日期 | 2026-11-17 |
| 交付方 | OCoD 分析方 |
| 接收方 | OCoD 实现方（经 泠渊 转交） |
| 快照目录 | 仓外（与仓库同级）`OCoD-handoff-2026-11-17/` |
| 归档 | 仓外（与仓库同级）`OCoD-handoff-2026-11-17.tar.gz` |
| 归档 SHA-256 | `47f9a70ee8bf806b5b61ffa5ae2614d090d8f21eaf04d4800d40dacfd9ddfcc2` |
| 归档大小 | 525 KiB（537,422 字节，供参考；以 SHA-256 为准） |
| 文件数 | 58（57 个内容文件 + `docs/handoff/MANIFEST.sha256`） |
| 内容清单 | [`MANIFEST.sha256`](MANIFEST.sha256) |

## 交付内容

- `docs/specs/**` 全部契约。
- `docs/handoff/README.md`、`docs/handoff/MANIFEST.sha256`。
- `samples/README.md`、`samples/VERIFICATION.md`、`samples/PROVENANCE.md`、`samples/SHA256SUMS.handoff`。
- `samples/protected/README.md`、`samples/protected/VERIFICATION.md`。
- `SHA256SUMS.handoff` 列出的 42 个自製样本与 `expected/` 摘要。

## 明确排除（分析方专用）

- `samples/verification/**`：原作模块级再导出夹具。
- `tools/cleanroom/**`：分析方生成/校验工具。
- `samples/evidence/**` / 仓外证据：实机验收截图与说明。
- `.git/**`、`src/**`、`tests/**`、构建产物、原作 ASAR 解包产物。

## 校验记录

分析方在快照根目录运行：

```sh
shasum -a 256 -c docs/handoff/MANIFEST.sha256
```

结果：**57/57 OK**（2026-11-17，分析方本机）。归档 SHA-256 见上表。

## 接收回执（由接收方填写）

- [x] 已收到上述快照或归档
- [x] 已用 `MANIFEST.sha256` 校验全部文件
- [x] 确认实现输入只来自快照内的允许路径，不使用 `samples/verification/**`、`tools/cleanroom/**` 或仓外证据
- 接收方名称：OCoD 实现方（ocod-impl）
- 校验日期：2026-11-17
- 备注/签名：`shasum -a 256 -c docs/handoff/MANIFEST.sha256` 在快照根目录通过，57/57 OK；归档 `OCoD-handoff-2026-11-17.tar.gz` 为 537,422 字节，SHA-256 `47f9a70ee8bf806b5b61ffa5ae2614d090d8f21eaf04d4800d40dacfd9ddfcc2`。实现只使用 `docs/specs/**` 与快照内允许的 `samples/**`，未读取 `samples/verification/**`、`tools/cleanroom/**` 或仓外证据。

> 说明：公开仓库同时包含分析方专用的 `samples/verification/**` 与 `tools/cleanroom/**`；它们不属于本交接。本记录与 `MANIFEST.sha256` 用于证明实际交付集合。
