@echo off
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-DesktopDemo.ps1"
if errorlevel 1 (
    echo Desktop Demo could not start. See the message above.
    pause
    exit /b 1
)
exit /b 0
