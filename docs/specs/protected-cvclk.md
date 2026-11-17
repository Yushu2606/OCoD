# 受保护角色包与作者权限契约

本文件定义受保护 `.cvclk` 与锁定 `.cvpart` 的权限标记、锁定字段、口令恢复、编辑限制、再导出限制和错误路径。容器与普通载荷结构见 [container-v2.md](container-v2.md)、[cvclk-v1.md](cvclk-v1.md)、[cvpart-v1.md](cvpart-v1.md)。

## 1. 权限轴与本地标记

受保护包有三条互相独立的权限轴：

| 交换标记 | 位置 | 默认 | 作用 |
| --- | --- | --- | --- |
| `locked` | 容器头部 | 导出 UI 默认 `true` | 接收者不能再导出该角色 |
| `editLock` | 角色载荷 | `false` | 接收者只能追加新台词；原始台词/样式/声音被锁定 |
| `owner` | 容器头部 | 无 | 可选作者口令的 scrypt 哈希；导入时输入正确口令可恢复完整权限 |

导入后本地模型使用以下标记（均**不**写回交换载荷）：

| 本地标记 | 推导 | 作用 |
| --- | --- | --- |
| `protected` | 来自 `BCVCLK2` 容器且口令未匹配 | 图像在本机以密封格式保存 |
| `locked` | `protected && header.locked` | 不能导出该角色；不影响本地编辑 |
| `editLocked` | `protected && payload.editLock === true` | 只能追加新台词；原始内容只读 |
| `authorLocked` | 未凭口令解锁且作者名非空 | 作者信息只读；仍可本地使用 |
| `owned`（导入结果） | 口令匹配 | 本次导入已解锁；`protected`/`locked` 为 false，`editLock` 不应用 |

要点：

- **`locked` 与 `editLocked` 独立**：`locked` 只限制再导出；`editLocked` 只限制编辑。二者可以单独出现或同时出现。
- **`authorLocked` 与 `locked` 独立**：普通包如果带作者名，接收者的作者信息就是只读的；作者名为空时 `authorLocked=false`。
- **口令匹配会同时解除 `locked`、`editLocked` 和 `authorLocked`**；它不会只解除其中一项。
- **`protected` 不是交换字段**；它表示“来自容器且未凭口令解锁，因此图像在本机密封”，**不是**权限判定。权限判定看 `header.locked`、`payload.editLock` 与 `header.owner` 三个独立标记；文件层“受保护包”指其中任一存在。
- **`editLocked` 不是交换字段**；它是 `payload.editLock === true` 的导入结果。读取方不得要求文件里出现 `editLocked`，也不得看到 `editLocked` 才应用编辑锁。

### 1.1 普通导入的识别与路由

普通导入不是一种独立文件格式：所有 `.cvclk` / `.cvpart` 都先按 [container-v2.md](container-v2.md) 解密，再在角色/部件层应用权限。

- `.cvclk`：逐角色检查 `payload.editLock === true`，同时检查 `header.locked` 与 `header.owner`。任一标记存在，该角色必须按本节第 5 节处理；不得因为 `header.locked=false` 就跳过 `editLock`，也不得把 `editLock:true` 的角色当普通角色导入。
- `editLocked` 是导入后的本地字段，不是交换标记；载荷里若作为未知字段出现，按未知字段保留但不产生权限，权限只由 `editLock`/`locked`/`owner` 决定。
- `.cvpart`：检查头部 `locked`；`locked=true` 时部件按受保护部件导入，不能按普通部件接受。
- 混合 `.cvclk`：每个角色独立判定；一个角色带标记不应导致同文件中的普通角色被丢弃。
- 暂不支持受保护包的实现：必须在解密后检测**全部**标记（`locked`、`editLock`、`owner`），任一存在就拒绝整个文件并返回“受保护包不支持”；**不得只拒绝 `locked=true` 而接受 `editLock:true` / owner 文件**。这是允许的降级，但标记识别必须完整。
- 口令流程只影响解锁结果，不改变“是否属于权限包”的判定；正确口令后 `owned=true`、`locked/editLocked/authorLocked=false`，图像按普通文件保存。

## 2. 容器头部

普通 `.cvclk`：

```json
{"format":"bug-clicker","version":2,"locked":true}
```

带作者口令：

```json
{
  "format": "bug-clicker",
  "version": 2,
  "locked": true,
  "owner": {
    "s": "base64(16-byte salt)",
    "h": "base64(32-byte scrypt hash)"
  }
}
```

规则：

- `locked` 必须存在；`true` 表示接收者不可再导出。
- `owner` 可选；存在时必须同时有 `s`、`h` 两个字符串。
- `owner` 与 `locked` 独立：`locked=false` 时也可以带 `owner`；此时口令用于解除 `editLock`/作者锁定，而不是解除再导出限制（因为本来就可导出）。
- 头部字节是 AES-GCM 的 AAD；修改 `locked` 或 `owner` 会使文件认证失败。
- 未知头部字段按 [container-v2.md](container-v2.md) 保留；不得用未知字段绕过权限。

### 2.1 口令哈希

作者口令 `code` 的规范化与哈希：

```
t = String(code).trim()
if (t.length < 4) -> no owner
salt = 16 random bytes
hash = scrypt(
  password = NFC(t),
  salt     = salt,
  keyLen   = 32,
  N        = 16384,
  r        = 8,
  p        = 1,
  maxmem   = 67108864
)
owner = { s: base64(salt), h: base64(hash) }
```

- 口令长度：导入/导出 UI 均为 4–64 字符（trim 后）；导出时超过 64 字符截断为 64。
- 口令使用 Unicode NFC 规范化；比较区分大小写，忽略首尾空白。
- `s`/`h` 使用标准 Base64（含 `=` 填充）。
- 口令**不写入载荷**，只以 scrypt 哈希形式存在；原作不保存明文，作者需自行保管。
- 校验使用常量时间比较（`timingSafeEqual`）；没有重试锁定或错误计数。
- 口令是**导出者自选的密码**，不证明原作者身份；任何能导出该角色的人都可以设置自己的口令。

## 3. 载荷字段

受保护包与普通包共享 [cvclk-v1.md](cvclk-v1.md) 的全部字段。受保护包额外使用：

| 字段 | 类型 | 默认 | 说明 |
| --- | --- | --- | --- |
| `editLock` | boolean | `false` | 是否锁定原始编辑字段 |
| `editBase` | object | 无 | 锁定基线：台词键 → 字符串数组 |
| `editSty` | object | 无 | 锁定基线的样式：台词键 → 样式 ID 数组 |

### 3.1 `editBase`

- 键为 [cvclk-v1.md](cvclk-v1.md) 第 7.1 节的台词键（`lines`、`idleLines`、八个 `sp_*`）。
- 值为字符串数组；空字符串或纯空白项在导入时被过滤。
- 表示“接收者不能修改的原始台词”。缺键或值非数组 → 该键基线为空（不锁定该键的任何行）。
- `editBase` 存在但 `editLock` 不为 `true` → 忽略，不建立编辑锁。

### 3.2 `editSty`

- 键同 `editBase`；值为与基线等长的样式 ID 数组。
- 元素可为 `""`（默认样式）、`p_shout`/`p_think`/`p_recall` 或 `u_*`。
- 导入时截断到基线长度；缺失项视为 `""`。
- 缺键或值非数组 → 该键不锁定样式；导入时从当前 `lineStyles` 的基线前缀推导。
- 样式 ID 未知时运行时忽略；但再导出应保留。

### 3.3 写入规则

- `editBase`/`editSty` **只应**在 `editLock: true` 时出现；否则读取方忽略、写入方不应生成。
- 作者从零设置 `editLock: true` 时，可以省略 `editBase`/`editSty`；导入方把当前全部台词作为基线。
- 作者对已锁定角色再导出时，应带回 `editBase`/`editSty`（原作从本地 `editLocked` 角色自动带回）。
- `editBase`/`editSty` 不得包含图片、部件、声音文件等非台词内容。

## 4. 导出选项

原作导出 UI 的选项与默认值：

| 选项 | 默认 | 作用 |
| --- | --- | --- |
| “받은 사람이 다시 내보내지 못하게” | **开** | `allowReexport=false` → 头部 `locked=true` |
| “받은 사람은 새 대사만 더하게” | 关 | `editLock=true` |
| “내 비밀번호” | 空 | 非空且 ≥4 字符时生成 `owner`；空则不生成 |
| “머리 위 인형 (파츠)” | 开 | 是否携带部件 |
| “말풍선 꾸밈” | 开 | 是否携带气泡装饰 |
| “대사 소리” | 开 | 是否携带台词音 |

规则：

- `header.locked = !allowReexport`；默认导出是 `locked=true`。
- 若所选角色中存在 `author.redistribute === "no"`，且作者尝试关闭再导出限制，UI 强制重新打开并提示：

  > 다시 나눠 주기가 금지라서 "받은 사람이 다시 내보내지 못하게"를 켰습니다.

- 选择 `locked=true` 且口令为空时，UI 警告“비밀번호 없이 잠가서 내보냅니다”：作者之后重新导入也无法再导出；备份用途应设置口令。
- 口令输入不显示、不保存；导出后作者需自行记住。
- 导出 `editLock=true` 且角色 `editLocked=false` 时，载荷通常只写 `editLock:true`，由导入方以当前台词建立基线。
- 导出已 `editLocked` 的角色时，无论导出选项如何，都会写 `editLock:true` 并带回 `editBase`/`editSty`。

## 5. 导入算法

读取容器、解析载荷后，对每个角色。`fromContainer` 只表示来源与图像密封策略，**不是**权限判定；权限由 `header.locked`、`header.owner` 与 `payload.editLock` 决定。

```
1. fromContainer = true（来自 BCVCLK2 容器；仅影响图像密封）
2. locked = !!header.locked
3. ownerPresent = !!header.owner
4. owned = false
5. 若导入方提供了 ownerCode：
     if (ownerPresent && ownerOk(header, ownerCode)):
         owned = true
         fromContainer = false
         locked = false
6. 若 owned == false 且 fromContainer == true 且 payload.editLock === true：
     对每个 LINE_KEYS 键：
        t = 当前台词数组
        r = editBase 不存在
              ? t.slice()
              : (editBase[key] 是数组 ? editBase[key] 过滤非空字符串 : [])
        local.editBase[key] = r
        if (r 不是 t 的前缀):
            local[key] = r.concat(t)
        l = editSty 存在 ? editSty[key] : (lineStyles[key] || []).slice(0, r.length)
        if (l 是数组且含真值):
            local.editSty[key] = l.slice(0, r.length)
     local.editLocked = true
7. local.protected = fromContainer
8. local.locked = fromContainer && header.locked
9. local.authorLocked = !owned && author.name 非空
10. 图像：fromContainer ? 本机密封格式 : 普通文件
```

### 5.1 口令匹配

- 只有提供了 `ownerCode` 且 `ownerOk` 为真才解锁；空串、过短、错误口令都按“未提供”处理。
- 口令匹配后：
  - `owned=true`；
  - 不应用 `editLock`（`editBase`/`editSty` 不写入本地）；
  - `locked=false`、`authorLocked=false`；
  - 图像以普通文件保存（不套本机密封）。
- 口令错误不会部分解锁；角色按收到的保护状态导入。
- 口令只在导入时校验；没有“在本地角色上输入口令解锁”的入口。要恢复权限，必须重新导入原文件并输入正确口令。

### 5.2 `editLock` 基线合并

- `editBase` 缺失 → 基线 = 当前全部台词；接收者只能在这些台词之后追加。
- `editBase` 存在但某键缺失 → 该键基线为空，不锁定该键的行。
- 基线不是当前台词的前缀时 → 本地台词变为“基线 + 当前台词”。这允许作者只携带新增台词、由接收方自动补回原始台词。
- `editSty` 缺失 → 基线的样式取当前 `lineStyles` 的前缀；`editSty` 存在则用其数组。
- 基线合并后，`editBase`/`editSty` 只保留非空数组。

## 6. 编辑限制（`editLocked=true`）

### 6.1 UI 行为

- 角色编辑窗口显示提示：

  > 보낸 사람이 편집을 잠가 둔 캐릭터입니다. 그림 · 파츠 · 움직임은 볼 수만 있고, 대사 편집에서 새 대사를 더할 수 있습니다.

- 主要编辑面板设为不可交互；角色名不可改；图片、部件、动作、声音、字体、拖拽等入口返回锁定错误：

  > 보낸 사람이 편집을 잠가 둔 캐릭터입니다. 새 대사만 더할 수 있습니다.

- 台词编辑器中：
  - 基线行只读、带固定标记；不能改文字、改样式、改台词音、上移/下移、删除。
  - 可以在基线之后追加新行；插入位置不得早于基线末尾。
  - “전체 교체”被隐藏或降级为“아래에 추가”；“대사 모두 지우기”只删除新增行，保留基线。
  - 新增行可以设置样式和台词音。
- 作者信息只读（`authorLocked`），提示：

  > 받은 캐릭터라 만든 사람 정보는 바꿀 수 없습니다. 내보내도 그대로 담깁니다.

- `editLocked` 不阻止本地播放、拖拽、调整窗口位置等使用行为；它只锁定内容编辑。

### 6.2 保存过滤 `editFilter`

即使绕过 UI，保存时也必须按以下白名单过滤。输入为当前角色 `base` 与提交的补丁 `patch`：

允许的字段：

| 字段 | 合并规则 |
| --- | --- |
| `LINE_KEYS` | 保留 `editBase[key]` 基线；只接受 `patch[key]` 中下标 ≥ 基线长度的项，追加到基线后。基线项的任何修改/删除都忽略。 |
| `inOrder` | 原样替换 |
| `noRepeat` | 原样替换 |
| `bubble` | 原样替换 |
| `idleMs` | 原样替换 |
| `bubbleMs` | 原样替换 |
| `part[A-Za-z0-9]*On`（如 `partTopOn`） | 原样替换 |
| `sp_g<1-6 位数字>_(before|after)` | 原样替换（自定义提醒组） |
| `groupNames` | 对象原样替换 |
| `lineStyles` | 见下 |
| `lineSounds` | 见下 |
| `lineSoundOn` | 原样替换 |
| `lineSoundVol` | 原样替换 |

`lineStyles` 合并：

- 对 `LINE_KEYS` 键：取 `editSty[key]`（不足补 `""`），再接 `patch.lineStyles[key]` 中下标 ≥ 基线长度的项。
- 对非 `LINE_KEYS` 键：原样替换。
- `editSty` 中存在但补丁未提供的键：保留基线样式。

`lineSounds` 合并：

- 对 `LINE_KEYS` 键：取基线台词音（不足补 `null`），再接 `patch.lineSounds[key]` 中下标 ≥ 基线长度的项。
- 对非 `LINE_KEYS` 键：原样替换。
- 基线台词音未在补丁中提供时，保留基线前缀。

其他所有字段（包括 `name`、图像槽位、`author`、`bubbleStyle`、`rig`、`partOrder`、拖拽/举起的参数）都丢弃。过滤后若补丁为空，保存为无操作并返回当前角色。

实现方**应当**在 UI 层同时禁用这些操作，不能只依赖保存过滤。

## 7. 再导出限制

### 7.1 角色

- `locked=true` 的本地角色：
  - 导出选择列表中禁用并标记“내보내기 막힘”。
  - 不能加入导出集合；若用户强行提交，导出器跳过。
  - 若所选角色全部锁定，提示：

    > 다시 내보낼 수 없도록 보호된 캐릭터입니다.
    >
    > 받은 캐릭터 중 보낸 사람이 다시 내보내기를 막아 둔 캐릭터는 내보낼 수 없습니다.

- 混合选择：已解锁角色正常导出；锁定角色被跳过。
- `locked=false` 但 `editLocked=true` 的角色可以再导出；再导出保留 `editLock:true` 与 `editBase`/`editSty`，接收者仍只能追加台词。
- `locked` 不阻止本地编辑；作者可以修改未锁定字段，但仍不能导出。
- 没有“在本地清除 `locked`”的 UI；只能重新导入并输入正确口令。

### 7.2 部件

- `.cvpart` 头部 `locked=true` 时，导入后的部件 `Locked=true`，不能再导出。
- 角色自身 `locked=true` 时，其全部部件都视为不可导出，即使单个部件未锁定。
- 导出时跳过锁定部件；若没有任何可导出部件，提示：

  > 다시 내보낼 수 없도록 보호된 파츠입니다.

- 从普通 `.cvpart` 导入的部件 `Protected=true`、`Locked=false`，可以再导出。
- 受保护 `.cvpart` **没有** owner/口令机制；作者必须保留原包，遗失后无法恢复再导出权限。

## 8. 受保护 `.cvpart`

`.cvpart` 复用同一容器，但头部只有 `locked`，没有 `owner`：

```json
{"format":"bug-clicker-part","version":1,"locked":true}
```

- 导入：`parts.readFile` 返回 `locked`；`applyParts` 把每个部件标记为 `Protected=true`、`Locked=locked`。
- 图像在本机密封保存（与 `.cvclk` 相同）。
- 不能通过口令解锁；没有作者恢复路径。
- 再导出行为见第 7.2 节。
- `editLock` 不适用于 `.cvpart`；但 `editLocked` 的角色不能导入新部件（返回第 6.1 节的锁定错误）。`editLocked` 本身不阻止导出已有部件；只有角色/部件的 `locked` 会阻止。

## 9. 错误路径与文案

| 场景 | 行为 | 原作文案（韩文） |
| --- | --- | --- |
| 导入 `locked` 角色 | 成功导入；再导出被禁止 | `만든 사람이 다시 내보내기를 막아 두었습니다. 이 PC에서만 쓸 수 있습니다.` |
| 导入 `editLock` 角色 | 成功导入；原始内容只读 | `만든 사람이 편집을 잠가 두었습니다. 이름 · 그림 · 파츠 · 원래 대사는 바꿀 수 없고, 새 대사만 더할 수 있습니다.` |
| 受保护包提示 | 本地提示图像已密封 | `받은 캐릭터입니다. 그림은 이 PC에서만 열리도록 보호되어 있습니다.` |
| 实现不支持受保护包 | 检测到任一 `locked`/`editLock`/`owner` 后整文件拒绝；不得只拒绝 `locked` | 本地化的“受保护包不支持”错误 |
| 编辑锁定操作 | 返回 `{ok:false, locked:true}` | `보낸 사람이 편집을 잠가 둔 캐릭터입니다. 새 대사만 더할 수 있습니다.` |
| 口令正确 | 解锁导入 | `비밀번호가 맞습니다. 잠금 없이 가져옵니다.` |
| 口令错误 | 按原保护状态导入 | `비밀번호가 맞지 않습니다. 그대로 가져오면 받은 캐릭터로 들어옵니다. 계속 안 되면 X @BUGnDEV 로 DM 문의해 주세요.` |
| 导出全锁定角色 | 拒绝 | `다시 내보낼 수 없도록 보호된 캐릭터입니다.` |
| 导出全锁定部件 | 拒绝 | `다시 내보낼 수 없도록 보호된 파츠입니다.` |
| 无口令锁定导出警告 | 允许导出，但提示无法恢复 | `비밀번호 없이 잠가서 내보냅니다` |
| 作者信息只读 | 修改被忽略 | `받은 캐릭터라 만든 사람 정보는 바꿀 수 없습니다. 내보내도 그대로 담깁니다.` |
| 台词基线只读 | 只能追加 | `앞쪽 점선 대사는 보낸 사람이 잠가 두어 바꿀 수 없습니다. 새 대사는 그 아래에 더할 수 있습니다.` |

OCoD 可以使用本地化文案，但行为必须一致：不部分解锁、不误导出、不把错误口令当成正确口令、不因缺少 owner 崩溃。

## 10. 固定样本

受保护样本位于 [`samples/protected/`](../../samples/protected/)，逐文件来源见 [`samples/PROVENANCE.md`](../../samples/PROVENANCE.md)。

| 样本 | 头部 | 载荷 | 口令 | 预期 |
| --- | --- | --- | --- | --- |
| `locked-no-owner.cvclk` | `locked=true` | 普通 | 无 | 导入成功；不能再导出；无口令入口 |
| `locked-owner.cvclk` | `locked=true` + owner | 普通 | `cleanroom-locked-2026` | 无口令时不可再导出；正确口令完全解锁 |
| `editlock.cvclk` | `locked=false` | `editLock=true` + 基线 | 无 | 可再导出；原文锁定；只能追加 |
| `editlock-recovery.cvclk` | `locked=false` | `lines` 仅新增、`editBase` 含原文 | 无 | 导入后原文自动补回；只能追加 |
| `editlock-owner-reexport.cvclk` | `locked=false` + owner | `editLock=true` + 基线 | `cleanroom-reexport-2026` | 无口令时原文锁定但可导出；正确口令完全解锁 |
| `locked-owner-editlock.cvclk` | `locked=true` + owner | `editLock=true` + 基线 | `cleanroom-combined-2026` | 无口令时双重锁定；正确口令完全解锁 |
| `locked-part.cvpart` | `locked=true` | 3 个头部部件 | 无 | 部件应用但不可再导出 |

每一步的预期与双平台实机记录见 [`samples/protected/VERIFICATION.md`](../../samples/protected/VERIFICATION.md)。

## 11. 已知事实缺口

1. 原作只在导入时校验 owner code；本地已锁定角色没有“输入口令解锁”的入口。OCoD 若增加本地解锁，必须保持“重新导入原文件 + 正确口令”的等价语义，不能绕过原文件校验。
2. `.cvpart` 没有 owner 机制；无法像 `.cvclk` 一样凭口令恢复。OCoD 不应自行扩展 `.cvpart` 头部字段，除非有新版兼容依据。
3. `locked` 与 `editLocked` 的 UI 文案、按钮禁用范围可能随版本变化；本契约以 1.19.6 / 1.4.6 为准。
4. 口令错误时原作不提供延迟/锁定；本契约不要求实现速率限制。
5. 原作 `editFilter` 允许 `part[A-Za-z0-9]*On` 与自定义提醒组；这些字段在 UI 上的可达性未逐项做界面验收。
6. 受保护包的图像密封（`.bcp`）使用本机密钥，与 owner code 无关；换机器/换系统用户后，本地已导入图像可能无法解密，需要重新导入原文件。该行为不在交换格式内，但影响“恢复”流程，需在实现中给出提示。
