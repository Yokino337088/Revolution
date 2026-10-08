# Revolution · 文档

三种看法，内容完全一样，随手挑一种：

| 方式 | 地址 / 操作 | 适合 |
|---|---|---|
| 🌐 **在线文档站** | <https://yokino337088.github.io/Revolution/> | 拿个链接就能看；带模块分组、搜索与阅读顺序建议 |
| 💻 **本地看** | 双击 `预览文档.cmd`（起本地小服务）或直接双击 `index.html` | 没网、离线翻；效果与在线站一致 |
| 🐙 **版本库里看** | 在 GitHub 上点开具体文件 | 想看某一份文档的改动历史 / 参与修改 |

> `index.html` 的内容由 `pages.yml` 工作流自动发布到上面那个在线地址（改动 `Revolution.Document/` 就会重新发布）。

## 文件命名规则（先看这个，再挑文档）

| 名字 | 是什么 | 什么时候读 |
|---|---|---|
| **《XX 使用说明》** | **手把手**（零基础）：照着做就能跑通 | 第一次用这个模块 |
| **《XX 架构解析》** | **设计论证**：为什么这样设计、与别的方案差在哪、失败与边界怎么处理 | 想改框架、做技术选型、写分享 |
| **《XX 对比与设计说明》/《XX 示例讲解》** | 同一类的深读材料 | 想搞清"为什么不是另一种做法" |

同一模块可能两种都有：`.md` 是**权威版**（进版本库对比），`.html` 是**排版版**（浏览器直接看），内容一致。

## 目录一览（点链接直接跳转）

每个模块都有**《使用说明》（手把手）**；带 ✅ 的还有**《架构解析》（设计论证）**。内容一致，两种读法：**网页** = 在线站排版版（也可本地双击 `index.html` 看），**md** = 仓库版（GitHub 直接渲染、便于 diff）。

| 模块 | 《使用说明》 | 《架构解析》 |
|---|---|---|
| 📝 日志系统 | [网页](https://yokino337088.github.io/Revolution/日志系统/日志系统使用说明.html) · [md](日志系统/日志系统使用说明.md) | — |
| ⏱ 计时器系统 | [网页](https://yokino337088.github.io/Revolution/计时器系统/计时器系统使用说明.html) · [md](计时器系统/计时器系统使用说明.md) | — |
| 🧵 公共Mono模块 | [网页](https://yokino337088.github.io/Revolution/公共Mono模块/公共Mono模块使用说明.html) · [md](公共Mono模块/公共Mono模块使用说明.md) | — |
| 📦 资源加载系统 | [网页](https://yokino337088.github.io/Revolution/资源加载系统/资源加载系统使用说明.html) · [md](资源加载系统/资源加载系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/资源加载系统/资源加载系统架构解析.html) · [md](资源加载系统/资源加载系统架构解析.md) |
| 🗺 场景系统 | [网页](https://yokino337088.github.io/Revolution/场景系统/场景系统使用说明.html) · [md](场景系统/场景系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/场景系统/场景系统架构解析.html) · [md](场景系统/场景系统架构解析.md) |
| ⌨️ 输入系统 | [网页](https://yokino337088.github.io/Revolution/输入系统/输入系统使用说明.html) · [md](输入系统/输入系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/输入系统/输入系统架构解析.html) · [md](输入系统/输入系统架构解析.md) |
| 🎨 UI系统 | [网页](https://yokino337088.github.io/Revolution/UI系统/UI系统使用说明.html) · [md](UI系统/UI系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/UI系统/UI系统架构解析.html) · [md](UI系统/UI系统架构解析.md) |
| 🎬 动作序列 | [网页](https://yokino337088.github.io/Revolution/动作序列/动作序列使用说明.html) · [md](动作序列/动作序列使用说明.md) | [网页](https://yokino337088.github.io/Revolution/动作序列/动作序列架构解析.html) · [md](动作序列/动作序列架构解析.md) |
| 🎮 状态机 | [网页](https://yokino337088.github.io/Revolution/状态机/状态机使用说明.html) · [md](状态机/状态机使用说明.md) | [网页](https://yokino337088.github.io/Revolution/状态机/状态机架构解析.html) · [md](状态机/状态机架构解析.md) · [网页](https://yokino337088.github.io/Revolution/状态机/状态机Demo示例讲解.html) · [md](状态机/状态机Demo示例讲解.md) |
| 🔊 音效系统 | [网页](https://yokino337088.github.io/Revolution/音效系统/音效系统使用说明.html) · [md](音效系统/音效系统使用说明.md) | — |
| 📣 事件系统 | [网页](https://yokino337088.github.io/Revolution/事件系统/事件系统使用说明.html) · [md](事件系统/事件系统使用说明.md) | [网页](https://yokino337088.github.io/Revolution/事件系统/事件系统架构解析.html) · [md](事件系统/事件系统架构解析.md) |
| 📦 对象池 | [网页](https://yokino337088.github.io/Revolution/对象池/对象池使用说明.html) · [md](对象池/对象池使用说明.md) | [网页](https://yokino337088.github.io/Revolution/对象池/对象池架构解析.html) · [md](对象池/对象池架构解析.md) |
| 🧭 服务定位器 | [网页](https://yokino337088.github.io/Revolution/服务定位器/服务定位器使用说明.html) · [md](服务定位器/服务定位器使用说明.md) | [网页](https://yokino337088.github.io/Revolution/服务定位器/依赖注入vs服务定位器.html) |
| 🕹 GM指令 | [网页](https://yokino337088.github.io/Revolution/GM指令/GM指令使用说明.html) · [md](GM指令/GM指令使用说明.md) | — |
| 📊 导表工具 | [网页](https://yokino337088.github.io/Revolution/导表工具/导表工具使用说明.html) · [md](导表工具/导表工具使用说明.md) | [网页](https://yokino337088.github.io/Revolution/导表工具/导表工具架构解析.html) · [md](导表工具/导表工具架构解析.md) · [网页](https://yokino337088.github.io/Revolution/导表工具/导表工具对比与设计说明.html) · [md](导表工具/导表工具对比与设计说明.md) |

- `index.html` —— 文档总入口（= 在线站首页，带搜索）
- `预览文档.cmd` —— 本地预览脚本（等价于在线站）

## 扩展包（不属于框架本体，按需安装）

| 包 | 《使用说明》（手把手） | 《架构解析》（设计论证） | 《技术方案》（要不要做、怎么做） |
|---|---|---|---|
| 🔄 资源热更新（RevHotUpdate） | [网页](https://yokino337088.github.io/Revolution/热更新/RevHotUpdate使用说明.html) · [md](热更新/RevHotUpdate使用说明.md) | [网页](https://yokino337088.github.io/Revolution/热更新/RevHotUpdate架构解析.html) · [md](热更新/RevHotUpdate架构解析.md) | [网页](https://yokino337088.github.io/Revolution/热更新/RevHotUpdate技术方案.html) · [md](热更新/RevHotUpdate技术方案.md) |

> 独立包 `Assets/Revolution.HotUpdate/`（程序集 `Revolution.HotUpdate`）：**导入即有、不导入零影响** ——
> 给框架只加了两个默认为 `null` 的钩子（加载路径重定向 / 映射表覆盖），业务加载代码一行不用改。
>
> 🛠 **配套发布工具**：主推 [Revolution.Could](../../Revolution.Could/README.md)（腾讯云 COS 热更发布 WPF 可视化工具，
> 源码在仓库根：选产物 → 校验 SHA-256 → 按"内容先传、清单最后传"的安全顺序上传）；
> 另有 `UploadToCos` / `UploadToOss` 命令行脚本（`Assets/Revolution.Demo/RevHotUpdate.Demo/`）给 CI 用。
>
> ⚡ **想先跑起来看效果**：`Assets/Revolution.Demo/RevHotUpdate.Demo/`（演示面板 + 编辑器一键"打包 + 清单 +
> 装配本地 CDN" + `起本地CDN.cmd` 假 CDN；3 步跑通，不需要真云账号）。
> 能力：清单驱动版本比对与差量下载、断点续传、多源降级、尺寸 + SHA-256 校验、版本目录原子切换与回滚、
> 首包落地（Android）、URL 模式（小游戏）；明确不做代码热更、加密、二进制差量、灰度。

## 建议阅读顺序

1. **日志系统** → 2. **计时器系统** → 3. **公共 Mono 模块**（这三篇决定"怎么观察、怎么排时序"）
2. **资源加载系统** → **UI 系统** → **动作序列**（业务天天要用的三件套）
3. 其余按需：对象池 / 事件系统 / 服务定位器 / GM 指令 / 状态机 / 音效 / 导表工具

> 文档与代码**同仓库**：改代码时顺手改文档；新增模块请同时补一份《使用说明》。
