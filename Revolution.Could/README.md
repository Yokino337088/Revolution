# Revolution.Could · 腾讯云 COS 热更发布工具

独立的 Windows WPF 桌面程序。为 Revolution 热更框架发布新版本的 AssetBundle：选择 Unity 生成的平铺产物目录，核对版本与 COS 路径，点按钮即可按**资源先上传、入口清单最后上传**的顺序发布。不需要在 Unity 工程里保存云密钥，也不依赖 `coscli`。

## 运行要求

- Windows 10/11，安装 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)；从源码构建需要 .NET 10 SDK。
- 能访问腾讯云 COS 的网络、已经创建的 COS 桶，以及有上传权限的腾讯云 CAM 子账号的 `SecretId` / `SecretKey`。
- **当前版本要求桶为“公有读、私有写”**。程序会直接从 COS 源站匿名 GET 已发布内容做版本与 SHA-256 预检，私有读桶会收到 403 并安全停止。不要在桶里放任何不应公开的文件；如必须使用私有读桶，此版本暂不适用。
- Windows 机器上打开生成的 `Revolution.Could.exe`，无需打开 Unity 编辑器。

## 第一次使用：准备腾讯云 COS

1. 腾讯云 COS 控制台创建桶（例如 `game-assets-1250000000`，**包含数字 APPID 后缀**），记下地域（例如广州 `ap-guangzhou`）。设为**公有读、私有写**；只有发布子账号有写入权限。不要开放公共写入。
2. 在腾讯云 CAM 创建专门用于发布的子账号与 API 密钥，对指定桶授予最小上传权限（常见操作：`PutObject`、分片上传、查询桶/对象；具体以 SDK 和控制台权限诊断为准）。不要使用主账号密钥，也不要将密钥保存到 Unity 工程、Git 或截图中。本程序不会持久化输入的密钥。
3. 如需 CDN 加速，在腾讯云 CDN 上绑定 COS 回源的 HTTPS 加速域名；**`RevHotManifest.txt` 对应路径设为不缓存/短缓存，每次发布后刷新该 URL**。AB 包和 `ResMap.txt` URL 含资源版本，可长缓存。仅设置 COS 对象的 Cache-Control 并不能保证 CDN 规则也跟随，CDN 配置需独立检查。
4. WebGL / 小游戏访问跨域资源时，另在 COS/CDN 配置 CORS、HTTPS 及平台合法域名；客户端必须能匿名下载清单和包。断点续传场景须支持 Range 请求。

## 每次发布：四步

### 1. Unity 打包

用 Revolution 的 AB 打包工具针对**目标平台**生成包，然后打开 `Revolution.Tools → 热更新 → 热更清单窗口`，填写与包体 `Application.version` 一致的大版本，以及**高于该路径线上版本**的资源版本（例如 `1.2.0.8` → `1.2.0.9`）。点击「生成清单」和「自检」。务必在完成资源改动与打包后再生成清单。`ResMap.txt` 要和本轮 AB 同批生成。

### 2. 打开工具选择目录

在第 01 步选 Unity **平铺 AB 产物目录**：例如 `<你的工程目录>\AssetBundles\PC`。该目录至少包括：

```text
AssetBundles/PC/
  RevHotManifest.txt       # Unity 清单工具生成，最后上传
  ResMap.txt               # 清单工具复制过来的映射表
  hero                    # 清单里登记的 AB 包
  common                  # 清单里登记的 AB 包
  RevHotBuiltin.txt        # 只属于首包基线；此工具不会上传
  *.manifest              # Unity 辅助文件；此工具不会上传
```

**不要选** `LocalCDN`、项目根目录或 `StreamingAssets`。点击「校验文件并预览上传路径」：工具严格读取 `RevHotManifest.txt` 中登记的包名、大小和 SHA-256，逐个核验磁盘文件及依赖；有文件缺失或被篡改即停止。不会上传目录里未被登记的文件。

### 3. 填写桶信息

- `Bucket`：完整桶名，例如 `game-assets-1250000000`。
- `Region`：桶的真实地域，例如 `ap-guangzhou`。
- `SecretId` / `SecretKey`：CAM 子账号的 API 密钥。不会持久化，也不会写入日志。
- 程序会展示完整的目标清单路径，检查平台、环境、渠道、大版本和资源版本是否符合预期，再点「确认并开始上传」。

### 4. 发布与验证

程序会先从 COS **源站**检查旧清单版本、已存在的同版本资源（流式 SHA-256）；相同内容可跳过，不同内容**拒绝覆盖**。它会再次核验本地文件，按顺序上传全部 AB 与 `ResMap.txt`，最后上传 `RevHotManifest.txt`。先传清单会造成客户端看到新版本却找不到新包，因此不能手动调换顺序。

对于 `appVersion=1.2.0`、`resVersion=1.2.0.9`、`platform=PC`、空环境/渠道，发布路径如下：

```text
cos://game-assets-1250000000/PC/1.2.0/1.2.0.9/bundles/hero
cos://game-assets-1250000000/PC/1.2.0/1.2.0.9/bundles/common
cos://game-assets-1250000000/PC/1.2.0/1.2.0.9/ResMap.txt
cos://game-assets-1250000000/PC/1.2.0/RevHotManifest.txt    # 最后传
```

如果清单填写 `env=prod` / `channel=android`，路径变成 `prod/PC/1.2.0/android/...`。**不要手动在桶里再套一层 `LocalCDN/` 目录**。

客户端代码需把 `RevHotConfig.RemoteRoot` 设置为 `https://game-assets-1250000000.cos.ap-guangzhou.myqcloud.com`，或者已经配置好的 CDN 加速域名（只填根地址，不加 `/PC/1.2.0`）；如果清单里用了环境/渠道，客户端 `RevHotConfig.Env` / `Channel` 要与清单一致。发布成功后检查浏览器访问清单 URL，并在实际客户端测试「检查更新 → 执行更新 → 加载新资源」。使用 CDN 时刷新清单 URL 并检查响应缓存头。

## 异常与恢复

| 现象 | 处理 |
| --- | --- |
| 文件 SHA-256 不符 | 回到 Unity 重新打包、生成清单并自检；不要直接修改产物。 |
| COS 预检 403 | 检查桶是否公有读；该版本不支持私有读桶。密钥只用于上传，读取通过匿名 HTTPS。 |
| COS 预检 404 | 还没发布对应路径是正常的；若此前发过版本，请核对桶名/地域/路径。 |
| 远端版本不低于本次 | 增加资源版本并重新生成清单；不能覆盖旧资源版本的不同内容。 |
| 部分包上传后网络失败 | 未发布入口清单，旧版仍正常；不修改产物直接重新点击上传，已存在且哈希相同的包会跳过。 |
| 点取消后当前文件还在传 | SDK 的同步单文件上传不会立即中断；当前文件结束后停止下一步。如取消发生在最后清单提交期间，请到 COS 控制台确认实际结果。 |
| 客户端仍然看到旧版本 | 核对 `RemoteRoot`/平台/版本/环境/渠道，刷新 CDN 的 `RevHotManifest.txt` 缓存（不是只刷新包）。 |

**注意：**跨进程并发发布无法做到数据库式原子提交；本工具在开始与提交前分别检查远端清单，但发布时仍应确保同一平台/大版本由单一发布者操作。COS SDK 的上传成功不等同于 CDN 边缘节点已经刷新。不要在上传时让 Unity 重写 AB 产物。

## 构建

在本目录打开 PowerShell 执行：

```powershell
dotnet restore .\Revolution.Could.csproj
dotnet build .\Revolution.Could.csproj -c Release
dotnet run --project .\Revolution.Could.csproj
```

生成可拷贝到另一台 Windows 电脑的自包含版本：

```powershell
dotnet publish .\Revolution.Could.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

输出在 `bin\Release\net10.0-windows\win-x64\publish\`。固定依赖：腾讯云官方 `Tencent.QCloud.Cos.Sdk` 5.4.51；首次还原依赖需要连接 NuGet。工具只使用腾讯云 SDK 上传，不需要腾讯云管理控制台账号登录功能或 `coscli`。
