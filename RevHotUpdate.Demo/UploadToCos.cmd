@echo off
rem ============================================================
rem UploadToCos.cmd -- upload <project root>\LocalCDN to a Tencent Cloud COS bucket.
rem
rem Prerequisite: install COSCLI and run "coscli config init" once (keys stay in ~/.coscli,
rem                never inside this Unity project).
rem
rem Usage:
rem   UploadToCos.cmd <bucket> [region]
rem Example:
rem   UploadToCos.cmd my-game-res-1250000000 ap-guangzhou
rem   (add -DryRun to preview without uploading: edit this file or use powershell directly)
rem
rem NOTE: this .cmd is intentionally ASCII-only (cmd.exe encoding is a minefield);
rem       the Chinese explanation lives in the .ps1 (saved as UTF-8 with BOM) and in README.md.
rem ============================================================
setlocal
set BUCKET=%~1
if "%BUCKET%"=="" (
    echo Usage: UploadToCos.cmd ^<bucket^> [region]
    echo Example: UploadToCos.cmd my-game-res-1250000000 ap-guangzhou
    pause
    exit /b 1
)
set ARGS=-Bucket %BUCKET%
if not "%~2"=="" set ARGS=%ARGS% -Region %~2
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0UploadToCos.ps1" %ARGS%
if errorlevel 1 (
    echo.
    echo [FAILED] Upload did not complete. Common causes: COSCLI not installed / "coscli config init" not run / wrong bucket name.
    echo.
    pause
)
endlocal
