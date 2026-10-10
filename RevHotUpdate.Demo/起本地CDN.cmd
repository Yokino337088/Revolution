@echo off
rem ============================================================
rem StartLocalCdn.cmd -- start a tiny local static server ("fake CDN") for the RevHotUpdate demo.
rem
rem It serves  <project root>\LocalCDN  over HTTP so you do NOT need a real cloud bucket.
rem
rem Usage:
rem   Double-click this file (default port 8000) -> copy the printed URL into the demo panel's RemoteRoot.
rem   Another port:  StartLocalCdn.cmd 8123
rem
rem Stop: press Ctrl+C in the window (or just close it).
rem
rem NOTE: this .cmd is intentionally ASCII-only (cmd.exe encoding is a minefield);
rem       the Chinese explanation lives in the .ps1 (which is saved as UTF-8 with BOM) and in README.md.
rem ============================================================
setlocal
set PORT=%~1
if "%PORT%"=="" set PORT=8000
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0本地CDN.ps1" -Port %PORT%
if errorlevel 1 (
    echo.
    echo [FAILED] Could not start. A common cause: the port is already in use.
    echo          Try another port:  StartLocalCdn.cmd 8123
    echo.
    pause
)
endlocal
