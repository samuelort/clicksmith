@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\rollback.ps1" %*
set "code=%errorlevel%"
echo %cmdcmdline% | find /i "/c" >nul && pause
exit /b %code%
