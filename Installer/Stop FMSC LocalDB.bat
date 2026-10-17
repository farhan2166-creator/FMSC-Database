@echo off
setlocal EnableExtensions
title FMSC - Stop LocalDB for database file copy
set "dataFile=C:\ProgramData\FMSC_Database\FMSC_DB.mdf"
set "logFile=C:\ProgramData\FMSC_Database\FMSC_DB_log.ldf"
if not "%~1"=="" set "dataFile=%~1"
if not "%~2"=="" set "logFile=%~2"

if not exist "%~dp0Verify FMSC Database Unlocked.ps1" (
    echo ERROR: Keep Verify FMSC Database Unlocked.ps1 beside this batch file.
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

where SqlLocalDB.exe >nul 2>&1
if errorlevel 1 (
    echo ERROR: SqlLocalDB.exe was not found on PATH.
    echo Run this on the PC with SQL Server LocalDB installed.
    goto failure
)

rem An open application can reconnect and start LocalDB again.
tasklist /FI "IMAGENAME eq FMSC_SMS.exe" /NH 2>nul | findstr /I /C:"FMSC_SMS.exe" >nul
if not errorlevel 1 (
    echo ERROR: FMSC_SMS is still running. Close it and run this again.
    goto failure
)

rem Skip the stop command when the instance is already stopped.
call :isStopped
if not errorlevel 1 goto verify

echo.
echo Requesting normal shutdown of MSSQLLocalDB...
SqlLocalDB.exe stop "MSSQLLocalDB"
call :isStopped
if not errorlevel 1 goto verify

echo.
echo Normal shutdown did not complete. Requesting shutdown without waiting for queries...
echo Any unfinished work must be completed again when the application is reopened.
rem -i requests SQL Server shutdown with NOWAIT; -k is deliberately not used.
SqlLocalDB.exe stop "MSSQLLocalDB" -i
rem The independently observed state decides success, even if stop returns an error.

:verify
set /a attempts=0, stable=0 >nul
:waitForStop
call :isStopped
if errorlevel 1 (
    set /a stable=0 >nul
) else (
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Verify FMSC Database Unlocked.ps1" -DataFile "%dataFile%" -LogFile "%logFile%" -Quiet
    if errorlevel 1 (
        set /a stable=0 >nul
    ) else (
        set /a stable+=1 >nul
    )
)
if %stable% GEQ 3 goto success
set /a attempts+=1 >nul
if %attempts% GEQ 15 goto notStopped
rem Ping provides a short delay even if console input is redirected.
ping -n 3 127.0.0.1 >nul
goto waitForStop

:notStopped
echo.
SqlLocalDB.exe info "MSSQLLocalDB"
echo Could not confirm that the instance stopped and both files were released.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Verify FMSC Database Unlocked.ps1" -DataFile "%dataFile%" -LogFile "%logFile%"
echo A running app or database tool may be restarting the instance.
echo Also check that this is the Windows account that owns your database instance.
goto failure

:success
echo.
SqlLocalDB.exe info "MSSQLLocalDB"
rem Recheck after displaying info so an intervening restart is treated as failure.
call :isStopped
if errorlevel 1 goto notStopped
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Verify FMSC Database Unlocked.ps1" -DataFile "%dataFile%" -LogFile "%logFile%"
if errorlevel 1 goto failure
echo.
echo SUCCESS: MSSQLLocalDB is stopped and the MDF/LDF files are released.
echo You can now copy the matching MDF and LDF pair.
echo Preserve the current pair before replacing it.
echo Keep all apps and database tools closed until copying is complete.
echo Opening FMSC_SMS or connecting a database tool can start LocalDB again.
pause
exit /b 0

:isStopped
rem Fail closed if the English state cannot be independently confirmed.
SqlLocalDB.exe info "MSSQLLocalDB" 2>nul | findstr /R /C:"^State:.*Stopped" >nul
exit /b %errorlevel%

:failure
echo.
echo STOP: shutdown or verification failed. Do not replace database files yet.
echo No database files were changed by this script.
pause
exit /b 1
