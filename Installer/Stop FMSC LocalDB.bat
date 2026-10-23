@echo off
setlocal EnableExtensions
title FMSC - Stop LocalDB for database file copy
set "dataFile=C:\ProgramData\FMSC_Database\FMSC_DB.mdf"
set "logFile=C:\ProgramData\FMSC_Database\FMSC_DB_log.ldf"
if not "%~1"=="" set "dataFile=%~1"
if not "%~2"=="" set "logFile=%~2"

if not exist "%~dp0Stop FMSC LocalDB.ps1" (
    echo ERROR: Keep Stop FMSC LocalDB.ps1 beside this batch file.
    goto failure
)

echo Close FMSC_SMS, Visual Studio, and any database tools first.
echo Run this under the same Windows account that runs FMSC_SMS.
echo This stops MSSQLLocalDB and all databases using that instance.
echo No database files will be copied, replaced, deleted, or detached.
echo Data file: %dataFile%
echo Log file:  %logFile%
echo.
pause

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Stop FMSC LocalDB.ps1" -DataFile "%dataFile%" -LogFile "%logFile%"
if errorlevel 1 goto failure

echo.
echo SUCCESS: SQL Server exited and the MDF/LDF files are released.
echo You can now copy the matching MDF and LDF pair.
echo Preserve the current pair before replacing it.
echo Keep all apps and database tools closed until copying is complete.
echo Opening FMSC_SMS or connecting a database tool can start LocalDB again.
pause
exit /b 0

:failure
echo.
echo STOP: shutdown or verification failed. Do not replace database files yet.
echo No database files were changed by this script.
pause
exit /b 1
