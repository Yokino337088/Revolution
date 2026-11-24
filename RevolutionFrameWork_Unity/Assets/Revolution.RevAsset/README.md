# Revolution.RevAsset

RevAsset 是 Revolution 的可选资源后端。它用 Unity 原生 AssetBundle 生成包，提供文件级到目录级的显式分包、原生变体、共享依赖提取、按标签准备首包和签名热更新。RevResManager 使用同一套句柄、引用计数、域和场景预加载流程。

## 安装和首次出包

需要 Unity 2022.3 LTS 与含 RevAsset 后端装配 API 的 Revolution 框架。放入 `Assets/Revolution.RevAsset`，或在 Package Manager 添加 Git URL `https://github.com/Yokino337088/Revolution.git?path=/RevolutionFrameWork_Unity/Assets/Revolution.RevAsset#main`。框架可以从本仓库的原生 package 导出安装。

1. 打开 `Revolution.Tools → 资源 → RevAsset`，选择 RevAsset。
2. 选择真实资源目录并预览分包。已有原生标签会参与采集，细粒度规则可覆盖文件、子目录、变体或逻辑地址。
3. 初次使用可在编辑器打开模拟。真实 AB 使用对应平台的「构建并准备首包」，关闭模拟后再次进入 Play。
4. 使用 Unity 或 RevCLI 选择目标平台构建 Player。框架保留该目标首包的脚本和对象。切换到「Revolution 内置」可恢复已有项目的资源工作流。

## 从逻辑路径加载

```csharp
using Revolution;
using UnityEngine;

using (RevScopeHandle scope = RevScope.Enter("Lobby"))
{
    TextAsset notice = RevResManager.Load<TextAsset>("Config/", "Notice");
    Debug.Log(notice?.text);
}
```

打开 UI、建立资源预热或转场时，沿用项目现有框架门面。业务需要预制体的持有期限长于当前域时，使用明确的长生命周期租约。

## 按版本发布

构建同一内容包和平台的资产，发布序号依次递增。签名工具支持使用 RSA XML 密钥，编辑器面板可生成密钥。把公钥保存在项目运行配置中。上传目录的不可变部分后再部署 `channel.json`，以保证玩家读到的指针对应完整制品。

| 目录或文件 | 交付用途 |
| --- | --- |
| `objects/<SHA-256>.bundle` | 由 Unity 产生的原生 AB，同内容在版本间复用 |
| `catalogs/<SHA-256>.json` | 资源坐标、标签、平台和 Unity 直接依赖 |
| `channel.json` | 发布指针，带包、平台、兼容代号和目录签名 |
| `Library/RevAsset/Builtin` | 本机构建缓存，Unity 仅注入选择的实际目标 |
| `persistentDataPath/RevAsset` | 按内容包、平台、应用兼容代号隔离的本地安装 |

```csharp
using Assets = Revolution.RevAsset.RevAsset;

var receipt = await Assets.UpdateAsync();
if (receipt.Success && receipt.Installed)
{
    // 等业务域和旧资源租约退出后再尝试装配新版本。
    bool applied = Assets.TryActivateInstalled();
}
```

`PrefetchAsync(tags)` 准备所选标签和依赖缓存，`UpdateAsync` 补齐整版缺失文件后提交。已提交版本在下次启动自动挂载，也可以在空闲交接点显式激活。CDN 频道保留支持 Range 的原文件字节语义。回滚优先使用本地保留版本，重发线上回滚内容时签发更高序号。

## 命令行

```powershell
python rev.py revasset status
python rev.py revasset rule --path Assets/GameRes/UI --packing ByFolder --tag core
python rev.py revasset plan --limit 10
python rev.py revasset build --target StandaloneWindows64 --version 1.0.0 --revision 1 --builtin
python rev.py game call revasset.status
```

需要打签名 CDN 版本时通过 `--signing-key-path` 传入本地私钥路径。`revasset.plan` 可分页查实际资产归属。`revasset.configure` 可设逻辑根、兼容代号、频道公钥和首包标签。

## 平台范围

首版验收以 Windows Unity 2022.3 和真实原生 AB 为基线。资源热更新覆盖 Unity 资产内容。发布到移动端或浏览器前在所选目标运行首包、网络、磁盘缓存、更新和回退回归。现有场景入口继续由项目的场景交接负责，优先使用 Player 场景清单。

[开发与发布 Skill](https://github.com/Yokino337088/Revolution/tree/main/Skills/revolution-revasset) · [框架资源规范](https://github.com/Yokino337088/Revolution/blob/main/Skills/revolution-framework/references/resource-and-ab.md)
