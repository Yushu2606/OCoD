# 作者字段与气泡样式参考

本文件是 [cvclk-v1.md](cvclk-v1.md) 第 4、9 节的完整参考，供独立实现方复现原作的作者信息规范化、作者链接校验、作者名过滤与气泡预设行为。

## 1. 作者字段

### 1.1 规范化

对 `author` 对象：

```
name      = 去控制字符与 < >，折叠空白，trim，截断 40
link      = linkOk(author.link)
memo      = 同上，截断 100
warnEtc   = 同上，截断 40
warn      = 从 ["blood","violence","horror","nudity","bugs","flash"] 中筛选并去重
stream/edit/redistribute/credit = "yes" | "no" | ""
```

- `author` 不是对象 → 全部空值。
- 字符串规范化会删除 `\u0000-\u001f`、`\u007f`、`<`、`>`，把连续空白折叠为一个空格。
- `warn` 中未知值丢弃，保持原数组顺序去重。
- 四个许可字段只接受精确的 `"yes"` / `"no"`；其他值（包括 `true`）变为 `""`。

### 1.2 作者名过滤

原作在导入时过滤作者名、备注、其他提醒和链接；在导出前再次检查整个作者对象。命中过滤名单的角色仍可导入，但 `author` 整体置空；导出时命中会导致整个导出失败（错误码 `AUTHOR_WORDS`）。

算法（逐字段）：

1. 转字符串；`null`/`undefined` → 空串。
2. `normalize("NFKC")`；失败则保持原串。
3. 转小写。
4. 把片假名 `ァ-ヶ` 逐字减 96，转成平假名。
5. 去掉空白（含全角空格）与以下字符：

```
· • . , _ - ~ ! ? @ # $ % ^ & * ( ) + = [ ] { } | \ / : ; " ' < > ` 0-9
```

6. 若结果为空 → 不命中。
7. 先删除“例外词”（它们是正常词汇的一部分）：

```
시발점 시발역 시발택시 씹다 씹어 씹는 씹고 씹으 되씹
```

8. 若结果包含下列任一子串 → 命中：

```
씨발 시발 씨빨 씨바 ㅅㅂ ㅆㅂ 씹 좆 존나 지랄 ㅈㄹ 병신 ㅂㅅ 븅신 빙신
개새끼 개새기 개색기 개색히 ㄱㅅㄲ 미친놈 미친년 엠창 니미 느금 니애미
느금마 애미뒤 썅 쌍년 쌍놈 염병 육시랄 창녀 걸레년 한남충 김치녀 틀딱
애자새 좆같 fuck motherfuck shit bitch nigger nigga faggot whore
死ね 殺すぞ ころすぞ くたばれ クソ野郎 くそやろう クソが ちんこ まんこ
キチガイ きちがい 気違い ガイジ 幹你娘 幹你 操你 肏 他媽的 王八蛋 婊子
雞巴 機掰 靠北 智障 干你娘 他妈的 鸡巴 傻逼 傻屄 草泥马 操你妈 贱人
```

9. 另外，把原始字符串按非 `[a-z]` 分段，若任一分段精确等于下列英文词 → 命中：

```
cunt asshole bastard retard slut dickhead wtf stfu
```

`authorBad(author)` 返回命中的字段名列表（`name`、`memo`、`warnEtc`、`link`）。只要列表非空，导入时整个 `author` 置空；导出前对完整作者对象调用 `authorBad`。

> 该名单是原作 1.19.6 / 1.4.6 的内容过滤名单。它只影响作者信息是否保留/能否导出，不影响台词语义。

### 1.3 作者链接白名单

`linkOk` 规则：

1. 去所有空白，截断 100 字符；空 → `""`。
2. 若匹配 Fediverse 句柄：

```
^@?[a-z0-9_][a-z0-9_.]{0,29}@[a-z0-9-]+(\.[a-z0-9-]+)*\.[a-z]{2,}$
```

   - 已以 `@` 开头则原样返回；否则加 `@` 返回。
3. 否则匹配：

```
^(?:https?://)?([^/?#]+)([/?#][^\s"'<>`]*)?$
```

4. 取 host，转小写，去掉开头的 `www.` 或 `m.`。
5. 若 host 含 `@` 或 `:`，或不是仅由 `[a-z0-9.-]` 组成 → 不合法。
6. host 必须精确等于下列域名，或以 `.<域名>` 结尾：

```
x.com twitter.com bsky.app instagram.com threads.net threads.com
facebook.com youtube.com youtu.be twitch.tv tumblr.com pixiv.net
fanbox.cc booth.pm ko-fi.com patreon.com linktr.ee carrd.co
straw.page artstation.com deviantart.com postype.com tistory.com
blog.naver.com chzzk.naver.com crepe.cm sooplive.co.kr afreecatv.com
skeb.jp fantia.jp lit.link potofu.me nicovideo.jp misskey.io
plurk.com gamer.com.tw portaly.cc vocus.cc bilibili.com weibo.com
xiaohongshu.com lofter.com afdian.com afdian.net douyin.com
```

7. 合法则原样返回（保留用户写的 scheme、路径、大小写）；不合法 → `""`。

## 2. 气泡样式

### 2.1 预设

`bubbleStyle.preset` 可记录以下预设。预设只影响 UI 选择和初始颜色；实际显示以 `bubbleStyle` 的显式字段为准。

| preset | 名称（韩文） | bg | fg | line |
| --- | --- | --- | --- | --- |
| `dark` | 회색조 | `#212121` | `#e8e8e8` | `#e8e8e824` |
| `light` | 회색조 밝게 | `#ffffff` | `#1c1c1c` | `#1c1c1c24` |
| `amber` | 앰버 | `#211d18` | `#ece4d4` | `#dfa74880` |
| `night` | 다크 | `#1c1e24` | `#e6e8ec` | `#9bb8ff66` |
| `paper` | 양피지 | `#f3ead6` | `#3a2f1f` | `#8b3a2a55` |
| `cool` | 쿨 그레이 | `#ffffff` | `#1e2733` | `#41668f66` |
| `black` | 완전 검정 | `#000000` | `#f2f2f2` | `#f2f2f233` |

`preset` 校验正则：`^[a-z-]{1,20}$`。未知 preset 保留，不报错。

### 2.2 默认气泡对象

```json
{
  "preset": "dark",
  "bg": "#212121",
  "fg": "#e8e8e8",
  "line": "#e8e8e824",
  "radius": 16,
  "font": 14,
  "size": 100,
  "weight": 500,
  "shadow": true,
  "tail": true,
  "gap": 12,
  "align": "left",
  "family": "",
  "grad": false,
  "bg2": "#4a4a4a",
  "gdir": "d",
  "frame": "default",
  "lineW": 2,
  "lineC": "#e8e8e8",
  "cSize": 28,
  "cOver": "half",
  "cPop": true,
  "cLayer": "front",
  "skSlice": 24,
  "skW": 16,
  "skRep": "stretch",
  "cUse": true,
  "skUse": true,
  "shape": "box",
  "appear": "grow",
  "textFx": "none",
  "speed": 1,
  "split": false,
  "speedA": 1,
  "speedT": 1,
  "speedS": 1
}
```

`bubbleStyle` 缺失或 `null` → 使用上表。`bubbleStyle.family`：

- `"@file"` → 使用角色内嵌字体。
- 其他合法字符串（≤ 80 字符，不含 `" ' \` ; { } < > \`）→ 字体族名。
- 非法 → `""`（默认字体栈）。

### 2.3 形状、出现、文字效果

| 字段 | 枚举 | 含义 |
| --- | --- | --- |
| `shape` | `box` | 方框 |
| | `hand` | 手绘 |
| | `pixel` | 像素 |
| | `caption` | 字幕 |
| | `focus` | 集中线 |
| | `recall` | 回忆 |
| | `dark` | 暗色 |
| | `dream` | 梦境 |
| | `paper` | 纸张 |
| | `shout` | 大喊 |
| `appear` | `grow` | 放大出现 |
| | `fade` | 淡入 |
| | `pop` | 弹出 |
| | `rise` | 上升 |
| | `none` | 无动画 |
| `textFx` | `none` | 无 |
| | `type` | 打字机 |
| | `blur` | 模糊 |
| | `wave` | 波动 |
| | `shake` | 抖动 |
| | `glitch` | 故障 |

### 2.4 自定义样式 `bubbleStyles`

```json
{
  "id": "u_ab12",
  "name": "我的样式",
  "o": {"bg": "#101010", "shape": "shout", "weight": 700}
}
```

- `id`: `^u_[a-z0-9]{2,16}$`，唯一，最多 24 个。
- `name`: 去掉 `{}`、`\n`、`\r`，trim，≤ 20 字符；空 → 本地化“样式”。
- `o`: 只允许第 2.2 节中的已知键；未知键保留。
- 自定义样式只覆盖 `o` 中出现的键；其余继承当前 `bubbleStyle`。

### 2.5 台词样式 `lineStyles`

`lineStyles` 的数组元素是样式 ID：

- `p_shout` → 大喊
- `p_think` → 自言自语
- `p_recall` → 回忆
- `u_...` → 自定义样式
- `""` → 默认

原作只在 `lines` 与 `idleLines` 上写 `lineStyles`；其他特殊台词键的样式数组即使存在也不保证被运行时使用。OCoD 应当保留并尽量支持。
