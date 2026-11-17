# Bug Clicker 行为记录

依据本机 Bug Clicker Windows 1.19.6 和 macOS 1.4.6 的 ASAR 静态检查。macOS 版随包说明标明其基于 Windows 1.19.6。以下行为来自随包界面和导入导出逻辑，尚未通过运行验证。

| 样本 | SHA-256 |
| --- | --- |
| `bug-clicker-win.asar` | `5c3c035cf284c527f80bc326afa18ef581a0ec8d3a1ebce44241df11b5a8b903` |
| `bug-clicker-mac.asar` | `ab3065c755021b17d2d4a53856baa38251838cbe7deda2f32053c3d2cfb4f87f` |

两个样本的角色导入、导出、部件和角色模块逐字节一致。设置的一级标签和截图工具栏选项一致。平台帮助、托盘资源和截图合成页面存在差异。

## 作者与用户之间的交付物

| 交付物 | Windows 1.19.6 所见 |
| --- | --- |
| 完整角色 `.cvclk` | 使用 `bug-clicker` v1。导入支持普通与受保护的角色包，也接受旧版 `carnovella-clicker` JSON。 |
| 部件 `.cvpart` | 使用 `bug-clicker-part` v1，一个包最多包含 16 个部件。 |
| 台词文件 | 可导入 `.txt`、`.csv`、`.md`，导出为 `.txt`。可区分点击台词与自言自语，并表达逐行样式和换行。 |

一次可导出一个或多个角色。作者可选择是否携带头顶部件、气泡装饰和台词声音，也可禁止接收者再导出、锁定原始编辑字段，并可设置作者口令用于恢复。角色导入支持多选和拖放。重复导入同一文件会新增角色。部件包通过自定义设置中的部件入口导入。

定位依据为 Windows ASAR 中的 `src/exporter.js`、`src/importer.js`、`src/parts.js` 和 `src/ui/lines-file.js`。

## 角色运行与编辑

原作通过应用数据目录 `userData` 存储状态。Windows 便携版优先使用便携 EXE 旁已有的 `BUG Clicker 데이터`（BUG Clicker 数据）目录。旁边没有该目录时，先尝试已记录且仍有效的数据目录，否则在 EXE 旁创建。目录选择以便携 EXE 所在目录为基准。可写性检查通过后，该目录即成为 `userData`。定位依据为 Windows ASAR 中的 `boot.js`。

`userData` 下的 `state.json` 保存应用级偏好、提醒和浮窗记录。每条浮窗记录包含自己的位置，并引用一个角色 ID。`state.json` 还按角色 ID 记忆大小、翻转、置顶等偏好。角色内容和素材按角色 ID 存在 `characters/<角色 ID>/`，其中 `character.json` 保存角色定义。导入 `.cvclk` 后使用角色库中的副本。存储依据为 `src/main.js`、`src/store.js` 和 `src/characters.js`。

角色以独立桌面浮窗出现。右键菜单提供置顶、鼠标穿透、部件、截图、OBS 背景色、设置、关闭该角色及退出全部。大小、翻转、锁定、置顶、鼠标穿透、静音、音乐反应、打字反应和 OBS 背景色按角色保存。

设置页包含角色、自定义、提醒、反应、交换和信息。角色编辑器可编辑图像、部件、动作、声音及气泡。台词编辑支持点击台词、闲置台词和特殊提醒组，台词可以单独设置气泡样式及声音。

提醒包括按星期和时刻设置的闹钟或倒计时、按星期和多个时刻设置的用药提醒、限定星期和每日时段的饮水提醒，以及待办清单和未完成事项的周期提醒。提醒可配专用台词。

截图工具支持静态 PNG 和短 GIF，可选择角色姿势、捕获区域、背景及录制时长，可同时保存 GIF 和 PNG 帧。

定位依据为 Windows ASAR 中的 `src/capture/bar.html`、`src/main.js`、`src/settings/settings.html` 和 `src/detail/detail.html`。
