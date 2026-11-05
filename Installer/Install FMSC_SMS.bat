@echo off
setlocal
rem Compatibility shortcut for the graphical setup. The complete installer
rem includes FMSC.Setup.exe; core.ps1 still owns validation and recovery.
if not exist "%~dp0FMSC.Setup.exe" (
    echo FMSC graphical installer is missing. Extract the complete release ZIP first.
    pause
    exit /b 1
)
start "" "%~dp0FMSC.Setup.exe"
exit /b 0
