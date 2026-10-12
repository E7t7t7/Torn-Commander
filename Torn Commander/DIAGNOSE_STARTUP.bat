@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Torn Commander - STAY OPEN STARTUP DIAGNOSTICS
color 0E
echo =============================================================
echo TORN COMMANDER - STARTUP DIAGNOSTICS
 echo Source folder: %~dp0
if exist VERSION.txt type VERSION.txt
echo =============================================================
echo.
if not exist "dist\TornCommander.exe" (
  echo ERROR: The compiled EXE does not exist in the dist folder.
  echo First run REBUILD_AND_LAUNCH.bat, then look at build-last.log.
  goto :LOGS
)
echo Starting app and WAITING for it to close. This CMD will stay open.
echo If Commander never shows a window, look at the logs below.
echo.
start "" /wait "%~dp0dist\TornCommander.exe"
set "EXITCODE=%errorlevel%"
echo.
echo Commander process exited with status %EXITCODE%.
:LOGS
echo.
echo Logs folder: %LOCALAPPDATA%\TornCommander
echo -- LAUNCHER LOG --
if exist "%LOCALAPPDATA%\TornCommander\launcher-last.log" type "%LOCALAPPDATA%\TornCommander\launcher-last.log"
echo.
echo -- STARTUP LOG --
if exist "%LOCALAPPDATA%\TornCommander\startup-diagnostic.log" type "%LOCALAPPDATA%\TornCommander\startup-diagnostic.log"
echo.
echo -- CRASH LOG --
if exist "%LOCALAPPDATA%\TornCommander\startup-crash.log" type "%LOCALAPPDATA%\TornCommander\startup-crash.log"
echo.
echo If the logs are very long, share just their last 30 lines.
echo Do not share API keys or credential files.
pause
