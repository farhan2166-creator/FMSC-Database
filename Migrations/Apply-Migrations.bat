@echo off
setlocal
title FMSC Database Migration Applier
echo.
echo ================================================
echo        FMSC DATABASE MIGRATION APPLIER
echo ================================================
echo.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Apply-Migrations.ps1"
set "status=%ERRORLEVEL%"
echo.
if not "%status%"=="0" echo Migration applier failed. Review the message above before retrying.
pause
exit /b %status%
