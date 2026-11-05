@echo off
setlocal
rem Run as administrator, exactly as before. Windows PowerShell is hidden;
rem the branded FMSC setup dialog reports progress, completion and failures.
powershell.exe -NoLogo -NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "%~dp0core.ps1" -ShowInstallerWindow
exit /b %errorlevel%
