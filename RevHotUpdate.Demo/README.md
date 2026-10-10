# RevHotUpdate.Demo —— 资源热更演示（本机假 CDN 跑通全链路）

> 一句话：**改一次资源 → 打一次包 → 客户端只下变化的那一个包 → 加载到新内容**，
> 全程不需要真的云账号（本地一个静态服务器就是"CDN"）。

## 依赖

本 demo 属于**扩展包**，需要先有热更包（框架本体是前提）：

```text
git clone -b package   https://github.com/Yokino337088/Revolution.git Assets/Revolution
git clone -b hotupdate https://github.com/Yokino337088/Revolution.git Assets/Revolution.HotUpdate
```

> 没装热更包时，只有本目录（`Revolution.Demo.HotUpdate` 程序集）会报"找不到程序集引用"，其余 demo 不受影响。

## 三步跑通

```text
① 菜单：Revolution.Tools / 热更新 / Demo / ① 一键：打包 + 清单 + 装配本地 CDN
     （内部：演示资源"构建序号"+1 → 收集校验 → 打 AB → 写 ResMap/产物清单 → 拷首包进 StreamingAssets
       → 生成 RevHotManifest.txt → 按远端目录约定装配到 {工程根}/LocalCDN，内容先、清单最后）

② 双击本目录的 起本地CDN.cmd（默认 http://127.0.0.1:8000）—— 把 LocalCDN 当远端服务起来

③ 打开 RevHotUpdateDemo.unity 点 Play → 面板上依次点：
     ① 检查更新 → ② 执行更新 → ③ 加载演示资源
```

看到"构建序号"变大 = **热更生效**。想再看一轮：再跑一次菜单 ①（序号又 +1）→ Play 里点 ② → ③
—— 注意第二轮日志里的下载量应该只有一个包（这就是"差量"）。

## 面板按钮

| 按钮 | 做什么 | 看什么 |
|---|---|---|
| ① 检查更新 | 只拉清单 + 算差量（`CheckAsync`），不下包 | 远端资源版本、新增/变更几个包、要下多少字节 |
| ② 执行更新 | 下载 → 校验 → 原子落盘 → 切版本 → 重装资源系统（`UpdateAsync`） | 下载进度、耗时、"成功/失败 + 原因" |
| ②' 一键 Initialize | 检查 + 更新一步到位（`InitializeAsync`）—— **启动流程推荐就这一行** | 同上 |
| ③ 加载演示资源 | 先 `Release` 再 `LoadAsync` 读包里的文本 | **"构建序号"有没有变大** |
| ④ 状态与诊断 | 打印 `State` / `LastError` / `LocalResVersion` / `Dump()` | 排查时的第一手信息 |
| ⑤ 卸载全部资源 | `RevResManager.UnloadAll()`，模拟重启 | 再点 ③ 会重新读包 |

## 本示例演示了什么

1. **差量**：第二轮只下"内容变了的那一个包"（不是全量）；
2. **覆盖式语义**：持久化目录优先、没有就回退 `StreamingAssets` 首包
   → 首装 / 清数据 / 想临时关掉热更（菜单 ③ 删本地目录）都不会崩；
3. **大版本锚定**：清单里的 `@appVersion` 必须等于客户端的 `Application.version`，否则拒绝更新（防串版本）；
4. **加载代码零改动**：业务加载仍是 `RevResManager.LoadAsync(分组, 名字, 类型, 回调)`。

## 两个最容易踩的坑（demo 已经替你处理了）

| 坑 | 现象 | demo 的处理 |
|---|---|---|
| 编辑器默认只走 **AssetDatabase 直读** | "更新成功了，但内容还是旧的" | `Start()` 自动打开 `RevResBootstrap.UseABInEditor`（等价于菜单 Revolution.Tools / 资源 / AB 加载模式） |
| 资源系统有**缓存 + 引用计数** | 不释放就再 Load，永远命中旧缓存 | ③ 按钮先 `Release` 再 `LoadAsync`；另给了 ⑤ 一键卸载全部 |

## 文件与目录

| 位置 | 是什么 |
|---|---|
| `RevHotUpdateDemo.cs` | 演示面板（OnGUI：左侧按钮 + 右侧步骤日志） |
| `RevHotUpdateDemo.unity` | 可运行场景（Main Camera + Light + 挂面板的 RevDemoRoot） |
| `Editor/RevHotUpdateDemoTools.cs` | 编辑器工具：菜单 ①②③④（打包 + 清单 + 装配 CDN / 打开目录 / 清空客户端本地热更目录 / 打印 URL） |
| `起本地CDN.cmd` + `本地CDN.ps1` | 本机静态服务器：支持 **Range（断点续传）**，并按纪律分两类缓存头（清单 `no-cache` / 内容 `immutable`） |
| `Assets/GameRes/RevHotDemo/revhot_demo.txt` | **被热更的那个资源**（工具每次跑都会 +1 构建序号；`assetBundleName = revhotdemo`） |
| `{工程根}/LocalCDN/` | 装配出来的"远端"：`{平台}/{大版本}/{资源版本}/bundles/<包名>` + 清单 |
| `Library/Revolution/RevHotDemo/state.txt` | 构建序号（放 Library：不进版本库） |

## 排错顺序（30 秒）

```text
① 检查更新报 ManifestFetchFailed → CDN 起了吗？RemoteRoot 对吗？（窗口里会 404 并打印它期望的结构）
② 检查更新成功但 HasUpdate=false → 没跑过菜单 ①，或远端清单还是上一轮（重跑菜单 ①）
③ 更新成功但内容没变 → ③ 按钮点了吗（先 Release 再 Load）；或本地包没被覆盖（看 ④ 的 Dump）
④ 想看"回退出包基线" → 菜单 ③ 清空客户端本地热更目录 → 再点 ②（会发现又要重新下一遍）
⑤ 编辑器里 Build Target 切过 Android → 菜单 ① 的日志会警告"平台段对不上"（编辑器客户端平台恒为 PC）
```

## 说明

- 跑本 demo 会修改 `Assets/GameRes/RevHotDemo/revhot_demo.txt`（构建序号 +1）→ 在版本库里会显示为改动，**这是预期的**；
  想让 git 干净可以把这一行加进 `.gitignore`（不影响运行）。
- 本 demo 只动了"内容"，不动"新增包"—— 这是刻意的：**新增包会改动依赖结构**，属于大版本内的破坏性重构，
  建议留到大版本（见《RevHotUpdate 技术方案》第十四章与第十六章）。
