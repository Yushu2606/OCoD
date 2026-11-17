# `.cvclk` / `.cvpart` 二进制容器（cvclk-v2、cvpart-v1）

角色包与部件包都使用同一个加密容器。容器不是安全边界：它使用固定的传输密钥和随机盐，只负责把 JSON 载荷封装成不可直接编辑的二进制文件。实现方**必须**按下述布局读写，才能与 Bug Clicker 1.19.6 / 1.4.6 互通。

## 1. 两种容器的常量

| 常量 | `.cvclk` | `.cvpart` |
| --- | --- | --- |
| 魔数 `MAGIC` | ASCII `BCVCLK2\n`（8 字节，含 LF） | ASCII `BCVPART1`（8 字节） |
| 头部 `format` | `bug-clicker` | `bug-clicker-part` |
| 头部 `version` | `2` | `1` |
| HKDF `info` | ASCII `cvclk-v2` | ASCII `cvpart-v1` |
| 解压后载荷上限 | 419,430,400 字节（400 MiB） | 67,108,864 字节（64 MiB） |
| 文件读取上限 | 209,715,200 字节（200 MiB） | 41,943,040 字节（40 MiB） |

传输密钥 `TRANSPORT` 为 32 字节固定值：

```
73ca62079026ebb61663058e0d16e32be688bc36a8677299b75251a27d6ea2ba
```

该密钥由原作硬编码，不是秘密。实现方**必须**使用同一值，否则无法读取原作文件。

## 2. 文件布局

```
偏移  长度        内容
0     8           MAGIC
8     4           头部长度 N（无符号 32 位大端）
12    N           头部 JSON（UTF-8，无 BOM）
12+N  16          随机盐 SALT
28+N  剩余        密封数据 SEALED
```

`SEALED` 的内部布局：

```
偏移  长度        内容
0     12          AES-GCM nonce
12    16          AES-GCM authentication tag
28    剩余        AES-GCM 密文
```

整个文件不得有尾部多余字节。

### 2.1 头部 JSON

`.cvclk`：

```json
{"format":"bug-clicker","version":2,"locked":false}
```

受保护包在 `locked` 为 `true` 时可带 `owner`（见 [protected-cvclk.md](protected-cvclk.md)）。`.cvpart`：

```json
{"format":"bug-clicker-part","version":1,"locked":false}
```

- 头部**必须**是合法 JSON 对象。
- `N` **必须**在 0 到 4096 之间；原作在 `N > 4096` 时拒绝文件。
- 头部键的顺序不构成契约。
- 未知头部键**应当**在再导出时保留（受保护包除外，见 [protected-cvclk.md](protected-cvclk.md)）。
- 头部 JSON 的字节同时作为 AES-GCM 的 AAD；改变任何字节都会导致解密失败。

## 3. 密钥派生

```
K = HKDF-SHA256(
      ikm  = TRANSPORT,
      salt = SALT,
      info = ASCII("cvclk-v2" 或 "cvpart-v1"),
      L    = 32
    )
```

等价于 RFC 5869 的 HKDF-Extract + HKDF-Expand，哈希为 SHA-256，输出 32 字节。Node.js 的 `crypto.hkdfSync("sha256", TRANSPORT, SALT, info, 32)` 得到同样结果。

- `SALT` **必须**是 16 字节；写入方**必须**使用密码学随机数生成器。
- 读取方只使用文件中的 `SALT`，不得缓存或推断。

## 4. 载荷加密与压缩

```
PLAIN  = UTF-8( JSON 载荷 )
GZ     = gzip( PLAIN )
NONCE  = 12 字节随机数
AAD    = 头部 JSON 的原始字节（长度为 N 的那段）
CT     = AES-256-GCM-Encrypt(key = K, iv = NONCE, aad = AAD, plaintext = GZ)
SEALED = NONCE || TAG || CT
```

- 加密算法**必须**是 AES-256-GCM。
- `TAG` **必须**是 16 字节认证标签。
- 压缩算法**必须**是 gzip（RFC 1952）。原作使用 Node.js `zlib.gzipSync`；任何合法的 gzip 流都可被接受。
- 读取方的解压输出**必须**限制在上表的上限内；超过上限按损坏文件处理。
- 写入方**可以**选择任意 gzip 压缩级别；压缩后的字节不影响语义。
- 加密后再压缩或先压缩再加密的顺序不可交换：**必须**先 gzip，再加密。

## 5. 读取算法

```
1. 读取整个文件；若长度 > 对应文件读取上限，拒绝。
2. 检查前 8 字节等于 MAGIC；否则按“不是该类型文件”处理。
3. 读取 4 字节大端 N；若 N > 4096，拒绝。
4. 读取 N 字节头部；JSON.parse；若失败，拒绝。
5. 读取 16 字节 SALT。
6. 剩余字节必须至少 28 字节；按 NONCE/TAG/CT 切分。
7. 用第 3 节派生 K；AES-GCM 解密，AAD = 头部字节。
8. gunzip；限制输出长度；若失败，拒绝。
9. UTF-8 解码；按对应文档解析 JSON 载荷。
```

读取方**应当**在错误信息中区分“文件损坏 / 不是该类型文件 / 版本不支持”，但错误文案不属于交换契约。

## 6. 写入算法

```
1. 构造头部 JSON（固定键：format、version、locked；受保护包按 [protected-cvclk.md](protected-cvclk.md) 加 owner）。
2. 序列化载荷 JSON 为 UTF-8。
3. gzip；生成 16 字节 SALT、12 字节 NONCE。
4. 按第 3、4 节生成 SEALED。
5. 写入 MAGIC || uint32be(N) || 头部 || SALT || SEALED。
```

写入方**必须**保证：

- 头部长度 ≤ 4096。
- 载荷 JSON 不超过解压后上限。
- 不写 BOM、不写尾部填充。
- `locked` 字段始终存在；普通包写 `false`。
- 头部 `version` 与上表一致；不要写载荷内的 `version` 到头部。

## 7. 普通包与受保护包的区别

| 项目 | 普通包 | 受保护包 |
| --- | --- | --- |
| 头部 `locked` | `false` | `true` |
| 头部 `owner` | 不存在 | 可存在 |
| 载荷角色字段 `editLock` | 不存在或 `false` | 可为 `true` |
| 接收者再导出 | 允许 | 禁止 |

两种包使用完全相同的容器算法。读取方**不得**因为 `locked: false` 就跳过解密或按明文 JSON 读取；所有 `.cvclk` / `.cvpart` 都是容器格式。

## 8. 旧版纯 JSON

原作导入器也接受**未封装**的纯 JSON 角色文件（见 [carnovella-clicker-json.md](carnovella-clicker-json.md)）。实现方**必须**在读取 `.cvclk` 时先检查魔数：

- 有魔数：按本文件读取。
- 无魔数：按 UTF-8 JSON 读取，并按旧版/普通 JSON 规则校验。

`.cvpart` 没有纯 JSON 兼容路径：原作只接受 `BCVPART1` 容器。

## 9. 已知错误条件

| 条件 | 原作行为 |
| --- | --- |
| 文件大于读取上限 | 拒绝，提示文件过大 |
| 魔数不匹配 | `.cvclk` 尝试纯 JSON；`.cvpart` 拒绝 |
| 头部长度 > 4096 | 拒绝 |
| 头部 JSON 非法 | 拒绝 |
| AES-GCM 认证失败 | 拒绝，提示无法读取 |
| gzip 解压失败或超限 | 拒绝 |
| 载荷 JSON 非法 | 拒绝 |
| 载荷 `format` 不匹配 | 拒绝 |
| `characters` / `parts` 不是数组或为空 | 拒绝 |
| `.cvpart` 部件数 > 16 | 拒绝 |
| `.cvpart` 被误当作 `.cvclk` 导入 | 拒绝并提示到部件入口导入 |
| `.cvclk` 被误当作 `.cvpart` 导入 | 拒绝并提示到角色导入入口导入 |

## 10. 测试向量

固定样本中的每个 `.cvclk` / `.cvpart` 都是本容器的测试向量。实现方**应当**至少验证：

1. 能解开 `samples/cvclk/plain-minimal.cvclk`，得到 `format = bug-clicker`、`version = 1` 的载荷。
2. 能解开 `samples/cvpart/headtop.cvpart`，得到 `format = bug-clicker-part`、`version = 1` 的载荷。
3. 用实现方自己的读取器解开自己写出的容器，且头部、载荷和图像摘要与 `samples/expected/` 一致；分析方可用 `tools/cleanroom/samples.mjs inspect` 交叉检查。
4. 篡改任意一个头部字节、密文字节或标签字节，解密必须失败。

分析方的 `tools/cleanroom/samples.mjs` 使用确定性的“固定 Huffman + LZ77”DEFLATE 实现生成 gzip，因此样本字节可跨 zlib 版本稳定重建；读取方不得依赖压缩流的具体字节。
