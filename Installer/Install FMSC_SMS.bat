@echo off
setlocal
title FMSC School Management System Installer
echo.
echo ================================================
echo          FMSC SCHOOL MANAGEMENT SYSTEM
echo ================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0core.ps1"
echo.
pause
endlocal
