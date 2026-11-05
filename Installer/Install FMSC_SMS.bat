@echo off
setlocal
rem Compatibility entry point. The native GUI launcher requests UAC and shows
rem the existing FMSC installer window. Double-click FMSC.Setup.exe instead.
if not exist "%~dp0FMSC.Setup.exe" (
    echo FMSC.Setup.exe is missing. Extract the complete installer ZIP.
    exit /b 1
)
start "" "%~dp0FMSC.Setup.exe"
exit /b 0
