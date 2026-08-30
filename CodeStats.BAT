@echo off
setlocal
chcp 65001 >nul

rem Get the directory where this .BAT resides (strip trailing backslash)
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

rem Call the PowerShell script
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0CodeStats.ps1" -Root "%ROOT%"

pause
endlocal