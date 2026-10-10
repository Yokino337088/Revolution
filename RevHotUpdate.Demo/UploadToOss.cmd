@echo off
rem ============================================================
rem UploadToOss.cmd -- upload <project root>\LocalCDN to an Alibaba Cloud OSS bucket.
rem
rem Prerequisite: install ossutil and run "ossutil config" once (keys stay in ~/.ossutilconfig,
rem                never inside this Unity project).
rem
rem Usage:
rem   UploadToOss.cmd <bucket> [endpoint]
rem Example:
rem   UploadToOss.cmd my-game-res oss-cn-hangzhou.aliyuncs.com
rem   (add -DryRun to preview without uploading: edit this file or use powershell directly)
rem
rem NOTE: this .cmd is intentionally ASCII-only (cmd.exe encoding is a minefield);
rem       the Chinese explanation lives in the .ps1 (saved as UTF-8 with BOM) and in README.md.
rem ============================================================
setlocal
set BUCKET=%~1
if "%BUCKET%"=="" (
    echo Usage: UploadToOss.cmd ^<bucket^> [endpoint]
    echo Example: UploadToOss.cmd my-game-res oss-cn-hangzhou.aliyuncs.com
    pause
    exit /b 1
)
set ARGS=-Bucket %BUCKET%
if not "%~2"=="" set ARGS=%ARGS% -Endpoint %~2
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0UploadToOss.ps1" %ARGS%
if errorlevel 1 (
    echo.
    echo [FAILED] Upload did not complete. Common causes: ossutil not installed / "ossutil config" not run / wrong bucket name.
    echo.
    pause
)
endlocal
