# 本地化资源接入点

本文件说明工单 #4 预留、由工单 #29 补齐正式文案的四语接入点。

## 支持的语言与文化回退

首版界面提供四种语言：

| 语言 | 资源文化名 | 文件 |
| --- | --- | --- |
| 简体中文 | `zh-Hans` | `src/OCoD.Localization/Resources/strings.zh-Hans.json` |
| 英文 | `en` | `src/OCoD.Localization/Resources/strings.en.json` |
| 日文 | `ja` | `src/OCoD.Localization/Resources/strings.ja.json` |
| 韩文 | `ko` | `src/OCoD.Localization/Resources/strings.ko.json` |

解析规则在 `SupportedCultures.Resolve`：

- 所有中文语言区域（`zh`、`zh-CN`、`zh-SG`、`zh-TW`、`zh-HK`、`zh-MO`、`zh-Hans`、`zh-Hant` 等）统一回退到简体中文。
- 英文、日文、韩文的区域变体（如 `en-GB`、`ja-JP`、`ko-KR`）使用对应语言。
- 其他语言（如 `fr-FR`）回退到英文。
- 不变文化（invariant）回退到英文。

角色名、台词等作者内容按原文保存和交换，不经过本地化资源。

## 代码中的接入方式

- 每个用户可见文案都通过 `LocKeys` 中的常量访问，例如 `localization.Get(LocKeys.MenuExit)`。
- 不允许在窗口、菜单或托盘代码中硬编码用户可见文案；`LocKeys` 之外只允许品牌名等非界面文本。
- 所有资源键必须在四个文件中同时存在。`tests/OCoD.Localization.Tests` 会检查：
  - 四个文化的键集合一致；
  - 每个值非空；
  - `LocKeys` 中声明的每个常量都在四个文化中存在。
- 查找顺序：当前语言 → 简体中文 → 返回键名。因此即使某个语言的译文暂时缺失，界面仍能显示简体中文文案。

## 增加一条文案

1. 在 `src/OCoD.Localization/LocKeys.cs` 增加常量（命名形如 `Menu.Flip`、`Shell.Placeholder.Title`）。
2. 在四个 `strings.*.json` 中增加同名字段。
3. 运行 `dotnet test` 验证资源完整性。

## 语言选择

启动时的优先级：

1. 命令行 `--lang <tag>` 或 `--lang=<tag>`；
2. 环境变量 `OCOD_LANG`；
3. 操作系统界面语言。

启动后可以通过两处切换语言：

- 角色窗口右键菜单「语言」；
- macOS 菜单栏或 Windows 通知区域 OCoD 图标中的「语言」子菜单。

全局守护进程切换语言时会更新自己的托盘和管理窗口，并通过角色控制通道把同一语言下发到正在运行的角色。新启动的角色运行时继承全局守护进程的当前语言。当前选择不持久化；持久化属于设置工单的范围。

管理窗口、托盘和角色外壳都只通过 `LocKeys` 读取文案；`Management.*` 覆盖管理窗口的进程拓扑、角色列表、导入、按钮和空库占位文案，`Tray.*` 覆盖托盘入口。角色名、台词等作者内容按原文显示和保存，不进入本地化资源；缺少名称时的回退同样由角色导入层处理。

## 与正式本地化票的分工

本票只提供接入点和占位文案，用于证明四种语言可加载、可切换、中文回退可用。正式四语文案、界面覆盖范围与实机验收由工单 #29 负责。
