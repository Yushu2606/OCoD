# 原作模块级再导出夹具

这些文件不是自製样本，也不是真实应用界面的验收结果。它们由分析方用 Bug Clicker 原作 ASAR 中的导入/导出模块，对 `samples/cvclk/` 与 `samples/cvpart/` 的自製样本执行：

1. 原作导入器导入样本。
2. 原作导出器再导出。

交付边界：本目录是**分析方对照夹具**，不交给实现方。实现方使用 `samples/cvclk/`、`samples/cvpart/`、`samples/lines/`、`samples/legacy/`、`samples/expected/` 与 `samples/SHA256SUMS.handoff`。逐文件来源见 [../PROVENANCE.md](../PROVENANCE.md)。

用途：

- 证明“样本经原作导出后仍可再次导入”。
- 给 Windows / macOS 实机验收提供语义对照。
- 覆盖两平台 1.19.6 / 1.4.6 的导出路径。

目录：

| 目录 | 来源 | `app` 字段 |
| --- | --- | --- |
| `win-1.19.6/` | Windows 1.19.6 ASAR | `1.19.6` |
| `mac-1.4.6/` | macOS 1.4.6 ASAR（基于 Windows 1.19.6） | `1.4.6` |

注意：

- 文件包含随机盐、随机 nonce 和导出时间；每次重新生成字节不同，因此它们不是确定性重建目标。
- 它们已列入 `samples/SHA256SUMS`，`node tools/cleanroom/samples.mjs verify` 会校验当前字节。
- 实机再导出结果无需与这些文件逐字节一致；角色名、台词、部件槽位、气泡样式等语义应一致。
- 真实应用验收见 [../VERIFICATION.md](../VERIFICATION.md)。
