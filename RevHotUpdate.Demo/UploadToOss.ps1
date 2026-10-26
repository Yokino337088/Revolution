# ============================================================
# 上传阿里云OSS.ps1 —— 把 {工程根}/LocalCDN 上传到阿里云 OSS（对象存储）
#
# 【它解决什么】
#   真实项目里 AB 包放在对象存储 + CDN 上（阿里云 OSS / 腾讯云 COS …）；
#   demo 的"一键"工具已经把产物按**远端目录约定**装配到 LocalCDN —— 本脚本把同一批文件原样传上 OSS。
#   目录结构不用重排：LocalCDN 里长什么样，OSS 上就是什么样（客户端 RevHotUrlBuilder 按约定拼 URL）。
#
# 【两条上传纪律（脚本内已固化）】
#   ① 内容先传：AB 包 / ResMap.txt —— 设 Cache-Control:immutable（URL 带版本段，内容不可变，CDN 可长缓存）
#   ② 清单最后传：RevHotManifest.txt —— 设 Cache-Control:no-cache（它是"新版本已就绪"的开关，
#      先传清单 = 玩家可能拉到清单却下不到包，出现"半新半旧"）
#
# 【前置条件（一次性）】
#   ① 下载 ossutil：https://help.aliyun.com/zh/oss/developer-reference/install-ossutil2
#   ② 配置密钥（存放在本机 ~/.ossutilconfig，**不要**写进 Unity 工程）：
#        ossutil config
#      （AccessKey 建议用 RAM 子账号，只授予目标 bucket 的读写权限）
#
# 【怎么用】
#   命令行（或双击同目录 上传阿里云OSS.cmd）：
#       powershell -ExecutionPolicy Bypass -File 上传阿里云OSS.ps1 -Bucket my-game-res -Endpoint oss-cn-hangzhou.aliyuncs.com
#   第一次用先看要执行什么、不真传：
#       … -Bucket my-game-res -DryRun
#   传完后：客户端 RevHotConfig.RemoteRoot 填 bucket 域名（或套了 CDN 的加速域名），见脚本末尾打印。
# ============================================================
param(
    [string]$Bucket = "",          # 必填：bucket 名（如 my-game-res）
    [string]$Endpoint = "",        # 建议：bucket 外网 endpoint（如 oss-cn-hangzhou.aliyuncs.com）——只用来打印 RemoteRoot
    [string]$Root = "",            # 要上传的目录；默认 = {工程根}/LocalCDN
    [string]$Ossutil = "ossutil",  # ossutil 可执行文件（已在 PATH 就填命令名，否则填完整路径）
    [switch]$DryRun,               # 只打印要执行的命令，不真传
    [switch]$Yes                   # 跳过确认（CI 里用）
)

$ErrorActionPreference = "Stop"

# ---------- ① 定位源目录（与 本地CDN.ps1 / RevHotUpdateDemoTools.LocalCdnRoot 同一约定） ----------
if ([string]::IsNullOrEmpty($Root)) {
    $Root = Join-Path $PSScriptRoot "..\..\..\LocalCDN"
}
$Root = [System.IO.Path]::GetFullPath($Root)

Write-Host ""
Write-Host "==== RevHotUpdate 上传阿里云 OSS ====" -ForegroundColor Cyan
Write-Host "源目录：$Root"

if (-not (Test-Path $Root)) {
    Write-Host "[错误] 上传目录不存在 —— 先回 Unity 点菜单：" -ForegroundColor Red
    Write-Host "       Revolution.Tools / 热更新 / Demo / ① 一键：打包 + 清单 + 装配本地 CDN" -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrEmpty($Bucket)) {
    Write-Host "[错误] 缺少 -Bucket 参数。用法示例：" -ForegroundColor Red
    Write-Host "       .\上传阿里云OSS.ps1 -Bucket my-game-res -Endpoint oss-cn-hangzhou.aliyuncs.com" -ForegroundColor Red
    exit 1
}

# ---------- ② 前置检查：ossutil 在不在 ----------
try {
    & $Ossutil help *> $null
    if ($LASTEXITCODE -ne 0) { throw "exit $LASTEXITCODE" }
}
catch {
    Write-Host "[错误] 找不到 ossutil（或它没配好）。请先安装并执行 ossutil config 配置密钥：" -ForegroundColor Red
    Write-Host "       https://help.aliyun.com/zh/oss/developer-reference/install-ossutil2" -ForegroundColor Red
    exit 1
}

$Remote = "oss://$Bucket/"
$SourceWithSlash = $Root.TrimEnd('\', '/') + "/"      # ossutil 带尾斜杠 = 按"目录内容"上传（不带 LocalCDN 本身这一层）

# 清单类文件名：内容先传时要排除、最后单独传（见文件头两条纪律）
$manifestNames = @("RevHotManifest.txt", "RevHotBuiltin.txt")

function Invoke-Oss([string[]]$OssArgs) {
    Write-Host ("> " + $Ossutil + " " + ($OssArgs -join " ")) -ForegroundColor DarkGray
    if ($DryRun) { return }
    & $Ossutil @OssArgs
    if ($LASTEXITCODE -ne 0) { throw "ossutil 执行失败（exit $LASTEXITCODE）：目标 $Remote" }
}

# ---------- ③ 确认 ----------
if (-not $DryRun -and -not $Yes) {
    Write-Host ""
    Write-Host "将上传：$SourceWithSlash  →  $Remote" -ForegroundColor Yellow
    Write-Host "（内容设 immutable 长缓存；清单设 no-cache 且最后传 —— 由脚本保证顺序）" -ForegroundColor Yellow
    $answer = Read-Host "确认上传？(y/n)"
    if ($answer -ne "y") { Write-Host "已取消。"; exit 0 }
}

# ---------- ④ 纪律一：先传内容（排除清单；-u 增量：没变的文件自动跳过） ----------
Write-Host ""
Write-Host "---- ① 上传内容（AB 包 + ResMap，immutable 长缓存）----" -ForegroundColor Green
Invoke-Oss @(
    "cp", "-rf", "-u", $SourceWithSlash, $Remote,
    "--exclude", $manifestNames[0],
    "--exclude", $manifestNames[1],
    "--meta", "Cache-Control:public,max-age=31536000,immutable"
)

# ---------- ⑤ 纪律二：清单最后传（no-cache；逐个覆盖，保持相对路径不变） ----------
Write-Host ""
Write-Host "---- ② 上传清单（no-cache，最后传）----" -ForegroundColor Green
$manifests = @(Get-ChildItem -Path $Root -Recurse -File | Where-Object { $manifestNames -contains $_.Name })
if ($manifests.Count -eq 0) {
    Write-Host "[警告] 目录里没有清单文件（RevHotManifest.txt）—— 先回 Unity 生成清单再上传。" -ForegroundColor Yellow
}

foreach ($m in $manifests) {
    $relative = $m.FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
    Invoke-Oss @(
        "cp", "-f", $m.FullName, ($Remote + $relative),
        "--meta", "Cache-Control:no-cache,no-store,must-revalidate"
    )
}

# ---------- ⑥ 汇总：告诉使用者"接下来做什么" ----------
$displayEndpoint = if ([string]::IsNullOrEmpty($Endpoint)) { "$Bucket.oss-<区域>.aliyuncs.com（回填你 bucket 的 endpoint）" } else { "$Bucket.$Endpoint" }

Write-Host ""
Write-Host "上传完成 ✓（DryRun 模式除外）" -ForegroundColor Cyan
Write-Host "  RemoteRoot（填进 RevHotConfig / Demo 面板）：https://$displayEndpoint"
if ([string]::IsNullOrEmpty($Endpoint) -eq $false -and $manifests.Count -gt 0) {
    $relative = $manifests[0].FullName.Substring($Root.Length).TrimStart('\', '/').Replace('\', '/')
    Write-Host "  清单 URL（浏览器打开应看到清单文本）：https://$Bucket.$Endpoint/$relative"
}
Write-Host ""
Write-Host "如果 bucket 前面套了 CDN 加速域名，还有两件事："
Write-Host "  ① CDN 缓存规则：RevHotManifest.txt 设不缓存（或每次上传后手动刷新 URL），"
Write-Host "     否则边缘节点可能一直回旧清单 —— 玩家永远收不到新版本。"
Write-Host "  ② WebGL / 小游戏需要在 OSS 配 CORS 跨域规则（Allow Origin / GET+HEAD）。"
Write-Host ""
