# ============================================================
# 本地CDN.ps1 —— 给 RevHotUpdate demo 用的"假 CDN"（本机静态文件服务器）
#
# 【它解决什么】
#   真实项目里 AB 包放在对象存储 + CDN 上（腾讯云 COS / 阿里云 OSS …）；
#   demo 不需要真云 —— 这个脚本把本地一个文件夹当成远端来服务，链路完全一样（HTTP + 目录 + 缓存头）。
#
# 【为什么不直接用 python -m http.server / 其它一行命令】
#   因为热更最容易踩的两个坑恰好是"服务器行为"：
#     ① 必须支持 Range（断点续传靠它）—— 不支持时下载器会退化成"整包重下"；
#     ② 缓存头必须分两类：清单 = no-cache（否则玩家永远拿到旧清单），内容 = 长缓存（URL 带版本段，天然不可变）。
#   所以这里手写一个最小实现，把这两条纪律在本地就演示出来（响应头可以在日志里看到）。
#
# 【怎么用】
#   双击同目录的 起本地CDN.cmd（默认端口 8000）；或命令行：
#       powershell -ExecutionPolicy Bypass -File 本地CDN.ps1 -Port 8000 -Root "D:\某个\LocalCDN"
#   然后把 demo 面板里的 RemoteRoot 填成它打印的地址（默认 http://127.0.0.1:8000）。
#   停止：窗口里按 Ctrl+C。
# ============================================================
param(
    [int]$Port = 8000,
    [string]$Root = ""
)

$ErrorActionPreference = "Stop"

# ---------- ① 定位要服务的目录 ----------
# 默认 = 工程根/LocalCDN（本脚本在 Assets/Revolution.Demo/RevHotUpdate.Demo/ 下，往上三层就是工程根）
#   与编辑器工具 RevHotUpdateDemoTools.LocalCdnRoot()（Application.dataPath/../LocalCDN）指向同一个目录
if ([string]::IsNullOrEmpty($Root)) {
    $Root = Join-Path $PSScriptRoot "..\..\..\LocalCDN"
}
$Root = [System.IO.Path]::GetFullPath($Root)

Write-Host ""
Write-Host "==== RevHotUpdate 本地 CDN（假 CDN）====" -ForegroundColor Cyan
Write-Host "服务目录：$Root"

if (-not (Test-Path $Root)) {
    Write-Host ""
    Write-Host "[警告] 这个目录还不存在 —— 先回 Unity 里点菜单：" -ForegroundColor Yellow
    Write-Host "       Revolution.Tools / 热更新 / Demo / ① 一键：打包 + 清单 + 装配本地 CDN" -ForegroundColor Yellow
    Write-Host "       （脚本会照常启动，只是所有请求都会 404，方便你确认 URL 拼得对不对）" -ForegroundColor Yellow
}

# ---------- ② 起 HTTP 服务 ----------
$listener = New-Object System.Net.HttpListener

# 同时听 127.0.0.1 与 localhost：浏览器/Unity 解析 localhost 可能走 IPv6，只绑一个会连不上
$prefixes = @("http://127.0.0.1:$Port/", "http://localhost:$Port/")
foreach ($p in $prefixes) {
    try { $listener.Prefixes.Add($p) } catch { }
}

try {
    $listener.Start()
}
catch {
    Write-Host ""
    Write-Host "[启动失败] 端口 $Port 可能被占用（或没有监听权限）。换一个端口重试，例如：" -ForegroundColor Red
    Write-Host "      起本地CDN.cmd 8123" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "地址前缀（填进 demo 面板的 RemoteRoot）：" -ForegroundColor Green
Write-Host "    http://127.0.0.1:$Port"
Write-Host ""
Write-Host "按 Ctrl+C 停止。" -ForegroundColor DarkGray
Write-Host ""

# ---------- ③ 内容类型表（AB 包没有扩展名，默认 octet-stream；.txt/.json 给文本类型） ----------
$contentTypes = @{
    ".txt"      = "text/plain; charset=utf-8"
    ".json"     = "application/json; charset=utf-8"
    ".manifest" = "text/plain; charset=utf-8"
    ".html"     = "text/html; charset=utf-8"
    ".png"      = "image/png"
    ".jpg"      = "image/jpeg"
    ".jpeg"     = "image/jpeg"
    ".mp3"      = "audio/mpeg"
    ".wav"      = "audio/wav"
    ".ogg"      = "audio/ogg"
    ".unity3d"  = "application/octet-stream"
    ".bundle"   = "application/octet-stream"
}

# 清单类文件名：必须 no-cache（见文件头第 ② 条纪律）
function Test-IsManifest([string]$fileName) {
    if ($fileName -eq "RevHotManifest.txt") { return $true }
    if ($fileName -eq "RevHotBuiltin.txt") { return $true }
    return $false
}

# ---------- ④ 主循环 ----------
while ($listener.IsListening) {
    $ctx = $null
    try {
        $ctx = $listener.GetContext()
    }
    catch {
        break               # Ctrl+C / 停止监听
    }

    $req = $ctx.Request
    $res = $ctx.Response
    $startedAt = Get-Date

    # ---- 路径 → 文件 ----
    $relative = [System.Uri]::UnescapeDataString($req.Url.AbsolutePath).TrimStart('/')

    # 防目录穿越：把 .. 与非法字符统统挡掉（本地 toy server 也要守住这条）
    $relative = $relative -replace '\.\.', ''
    $relative = $relative -replace '\\', '/'
    if ($relative.Length -eq 0) { $relative = "index.html" }

    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $Root $relative))
    $fileName = [System.IO.Path]::GetFileName($fullPath)

    # CORS：小游戏（WebGL）走 XHR 时会校验；本地先一律放开
    $res.Headers["Access-Control-Allow-Origin"]  = "*"
    $res.Headers["Access-Control-Allow-Headers"] = "*"
    $res.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS"
    $res.Headers["Accept-Ranges"] = "bytes"

    if ($req.HttpMethod -eq "OPTIONS") {
        $res.StatusCode = 200
        $res.Close()
        continue
    }

    if (-not (Test-Path $fullPath -PathType Leaf)) {
        # 404 时把"它期望的结构"打印出来 —— 排查"URL 拼错"的最快方式
        $res.StatusCode = 404
        $bytes = [System.Text.Encoding]::UTF8.GetBytes("404 Not Found: /$relative`n提示：目录结构应为 {平台}/{大版本}/{资源版本}/bundles/<包名>，清单在 {平台}/{大版本}/RevHotManifest.txt")
        $res.ContentType = "text/plain; charset=utf-8"
        $res.OutputStream.Write($bytes, 0, $bytes.Length)
        $res.Close()
        Write-Host ("[{0}] {1} /{2} -> 404" -f $startedAt.ToString("HH:mm:ss"), $req.HttpMethod, $relative) -ForegroundColor Yellow
        continue
    }

    $fileInfo = New-Object System.IO.FileInfo($fullPath)
    $total = $fileInfo.Length

    # ---- 缓存头（两条纪律的落地）----
    if (Test-IsManifest $fileName) {
        $res.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate"
    }
    else {
        $res.Headers["Cache-Control"] = "public, max-age=31536000, immutable"
    }

    # ---- 内容类型 ----
    $ext = [System.IO.Path]::GetExtension($fullPath).ToLowerInvariant()
    if ($contentTypes.ContainsKey($ext)) {
        $res.ContentType = $contentTypes[$ext]
    }
    else {
        $res.ContentType = "application/octet-stream"
    }

    # ---- Range（断点续传靠它；不支持时下载器只能整包重下）----
    $rangeHeader = $req.Headers["Range"]
    $start = 0
    $end = $total - 1
    $isRange = $false

    if (-not [string]::IsNullOrEmpty($rangeHeader) -and $rangeHeader -match 'bytes=(\d*)-(\d*)') {
        $isRange = $true
        if ($Matches[1] -ne "") { $start = [int64]$Matches[1] }
        if ($Matches[2] -ne "") { $end = [int64]$Matches[2] }

        if ($start -ge $total) {
            $res.StatusCode = 416                      # Range 越界：按协议回 416
            $res.Headers["Content-Range"] = "bytes */$total"
            $res.Close()
            Write-Host ("[{0}] {1} /{2} -> 416（请求的偏移超出文件大小）" -f $startedAt.ToString("HH:mm:ss"), $req.HttpMethod, $relative) -ForegroundColor Yellow
            continue
        }
        if ($end -ge $total) { $end = $total - 1 }
    }

    $length = $end - $start + 1

    if ($isRange) {
        $res.StatusCode = 206
        $res.Headers["Content-Range"] = "bytes $start-$end/$total"
    }
    else {
        $res.StatusCode = 200
    }
    $res.ContentLength64 = $length

    # ---- 写响应体（HEAD 只回头，不写内容）----
    if ($req.HttpMethod -ne "HEAD" -and $length -gt 0) {
        $buffer = New-Object byte[] 65536
        $stream = [System.IO.File]::OpenRead($fullPath)
        try {
            $stream.Seek($start, [System.IO.SeekOrigin]::Begin) | Out-Null
            $remaining = $length
            while ($remaining -gt 0) {
                $want = [Math]::Min($buffer.Length, $remaining)
                $read = $stream.Read($buffer, 0, $want)
                if ($read -le 0) { break }
                $res.OutputStream.Write($buffer, 0, $read)
                $remaining -= $read
            }
        }
        finally {
            $stream.Close()
        }
    }
    $res.Close()

    # ---- 日志：状态码 + 大小 + 耗时 + 是否分片（排查时一眼看出"有没有续传"）----
    $elapsedMs = [int]((Get-Date) - $startedAt).TotalMilliseconds
    $sizeText = if ($total -ge 1048576) { "{0:N1} MB" -f ($length / 1MB) } else { "{0:N1} KB" -f ($length / 1KB) }
    $rangeText = if ($isRange) { "  Range($start-$end)" } else { "" }
    $cacheText = if (Test-IsManifest $fileName) { "  no-cache" } else { "  immutable" }
    $color = if ($isRange) { "Magenta" } else { "Gray" }
    Write-Host ("[{0}] {1} /{2} -> {3}  {4}{5}{6}  ({7} ms)" -f `
        $startedAt.ToString("HH:mm:ss"), $req.HttpMethod, $relative, $res.StatusCode, $sizeText, $rangeText, $cacheText, $elapsedMs) -ForegroundColor $color
}

$listener.Stop()
Write-Host ""
Write-Host "本地 CDN 已停止。" -ForegroundColor DarkGray
