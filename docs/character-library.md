# 角色导入与角色库

本文对应工单 #6：导入普通角色包并持久化到角色库。当前只实现普通 `.cvclk` 的导入；受保护包、旧版 JSON、部件包、台词文件和再导出分别由后续工单处理。普通导入会明确拒绝任何受保护标记。

## 导入入口

- 管理窗口「导入角色包…」按钮或 `Cmd/Ctrl+I`：选择普通 `.cvclk`，导入后自动启动导入的角色。
- 开发态启动器：

```bash
dotnet run --project src/OCoD.Launcher -- --import <package.cvclk>
dotnet run --project src/OCoD.Launcher -- --characters
dotnet run --project src/OCoD.Launcher -- --start-character <id>
dotnet run --project src/OCoD.Launcher -- --stop-character <id>
```

导入结果按角色返回成功或跳过原因。一个多角色包中的某个角色失败不会阻止其他角色导入。

## 容器与角色解析

- 容器算法按 [container-v2.md](specs/container-v2.md)：`BCVCLK2` 头、HKDF-SHA256、AES-256-GCM、gzip、UTF-8 JSON。
- 载荷按 [cvclk-v1.md](specs/cvclk-v1.md)：只导入 `format=bug-clicker` 的角色；忽略包内的角色 `id`，为每个角色创建本地 ID。
- 导入要求至少有有效的 `body` 或 `head1`；`head2`/`head1`/`body`/`back2`/`side2` 按契约回退。
- 图像按字节嗅探 PNG/JPEG/WebP/GIF，不信任 data URL 的 MIME；GIF 保留原始字节（当前静态显示第一帧）。
- `imageUrls` 仅在本地图像缺失/无效时作为 HTTP(S) 回退，单个图像上限 8 MiB。
- 角色名缺失时依次回退包内 `id` 和本地化未命名角色。
- 未知字段不会阻塞导入：角色原始 JSON 保存在角色记录中，供后续再导出与字段保真使用。
- 普通导入按 [container-v2.md](specs/container-v2.md) 第 7 节和 [protected-cvclk.md](specs/protected-cvclk.md) 检查权限标记：容器头 `locked=true`、容器头存在 `owner`、任一角色载荷 `editLock=true`。检测到任一标记就拒绝整个文件并返回“受保护包不支持”，不会把只带编辑锁或口令标记的包当作普通包导入。`editBase`/`editSty` 单独存在不构成交换标记，按契约忽略。受保护包支持由 #9 接入。
- 纯 JSON 旧版角色当前不会被当作 `bug-clicker` 导入；由 #10 接入。

## 角色库布局

库级守护进程是唯一写入方。普通角色导入后布局如下：

```text
<library>/
  .ocod/
    library.json
    library.lock
    logs/library-daemon.log
  characters/
    index.json
    <character-id>/
      character.json      # 名称、来源、图像索引、原始角色 JSON
      settings.json       # 每个角色的运行设置
      images/
        body.png
        head1.png
        ...
```

`index.json` 是管理窗口列表的依据；角色目录和索引都由库级守护进程提交。每次导入先在新建角色目录写入全部图像和 JSON，最后原子替换 `index.json`；索引提交失败或导入中断时，本次新建的角色目录会回滚删除。库级守护进程启动时还会对账 `characters/` 与索引：完整但未入索引的目录会补入索引并恢复缺失的默认 `settings.json`，不完整目录和悬空索引项会被清理。角色 ID 为本地生成的 32 位十六进制 ID，不沿用包内 `id`。

### character.json

包含本地 ID、显示名、起始大小、来源包文件名、导入时间、图像槽位到文件的索引、`partOrder`、图层偏移，以及导入时的角色原始 JSON。当前运行时只读取图像索引、`partOrder`、图层偏移和运行设置；台词、音频、部件等原始字段保留给后续工单。

### settings.json

```json
{
  "width": 300,
  "height": 320,
  "x": 0,
  "y": 30,
  "flipped": false,
  "topmost": true,
  "locked": false
}
```

- 新角色默认宽度/高度为角色 `startSize`（缺失或 0 时 160），位置未定。
- 翻转、锁定、置顶、窗口缩放和移动会防抖保存；关闭角色时再强制保存一次。
- 再次启动同一角色时恢复大小、位置、翻转、置顶和锁定状态。
- 位置只在窗口确实有有效位置后保存；未移动过时保持为空，下次由系统居中。

## 运行与实例

- 角色运行时通过库级守护进程读取角色记录、逐槽位读取图像字节并保存运行设置；不直接写角色库。
- 全局守护进程为每个已导入角色维护一个运行时进程，以角色 ID 为键；重复启动同一角色只聚焦现有窗口，不创建第二个进程。
- 空角色库时全局守护进程启动占位角色运行时；首次导入后停止占位运行时并启动真实角色。
- 管理窗口列出角色名、运行状态，并提供启动/停止。导入成功后自动启动导入的角色。
- 当前静态显示正常状态图层：`back1`、`body`、`head1`、`side1`、`ex1`–`ex4`，按 `partOrder` 的 `L:` 标记排序并应用 `layers` 偏移。点击/闲置动画、眨眼、部件、气泡、台词与声音由 #7 接入。

## 数据目录

- macOS：默认共享库 `~/Library/Application Support/OCoD/Library`；移动启动器不影响库。
- Windows：启动器同目录的 `OCoDData\Library`；每个分发目录默认独立库，移动整个目录时库随之移动。全局运行时数据仍位于 `%LOCALAPPDATA%\OCoD`。
- `--library` / `OCOD_LIBRARY` 可覆盖默认库；`--data-dir` / `OCOD_DATA_DIR` 可覆盖运行时数据目录。
- 同一个全局守护进程可同时服务多个库（例如两个 Windows 分发目录）。各库的库级守护进程、角色运行时和管理窗口互相独立；关闭一个库的管理窗口或停止其运行时不会影响其他库。

## 已知限制

- 重复角色提示与三种处理方式（#12）。
- 受保护包与作者权限（#9）。
- 旧版 `carnovella-clicker` JSON（#10）。
- 独立部件包、台词文件（#11）、动画/台词/部件/声音（#7）、再导出（#8）。
- 多角色同时启动时若没有保存位置，窗口会居中重叠；拖动一次后位置会被保存。
- 管理窗口导入使用系统文件选择器；文件本身由库级守护进程读取和校验。
