@echo off
setlocal
chcp 65001 >nul
pushd "%~dp0" || exit /b 1

if exist ".venv\Scripts\python.exe" goto venv_ready
set "REV_ANDROID_PY=py -3"
%REV_ANDROID_PY% -c "import sys; assert sys.version_info >= (3,10)" >nul 2>&1
if not errorlevel 1 goto python_ready
set "REV_ANDROID_PY=python"
%REV_ANDROID_PY% -c "import sys; assert sys.version_info >= (3,10)" >nul 2>&1
if not errorlevel 1 goto python_ready
echo Install Python 3.10 or newer and run this script again.
goto failed

:python_ready
%REV_ANDROID_PY% -c "import cryptography; assert int(cryptography.__version__.split('.')[0]) >= 42" >nul 2>&1
if not errorlevel 1 goto serve
echo Preparing the companion's local Python dependencies.
%REV_ANDROID_PY% -m venv .venv || goto failed

:venv_ready
set "REV_ANDROID_PY=.venv\Scripts\python.exe"
%REV_ANDROID_PY% -c "import cryptography; assert int(cryptography.__version__.split('.')[0]) >= 42" >nul 2>&1
if errorlevel 1 %REV_ANDROID_PY% -m pip install -r requirements.txt || goto failed

:serve
%REV_ANDROID_PY% -X utf8 connect.py lan --stay-open %*
set "REV_ANDROID_EXIT=%ERRORLEVEL%"
goto done

:failed
set "REV_ANDROID_EXIT=1"

:done
popd
pause
exit /b %REV_ANDROID_EXIT%

rem 先检测 Python 与证书依赖。需要安装依赖时使用脚本目录的虚拟环境。
rem 电脑监听后由 Python 输出局域网 IP 和端口，每轮结束继续等待手机。
