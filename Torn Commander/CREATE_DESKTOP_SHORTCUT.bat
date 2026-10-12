@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Torn Commander - Install Dark T Desktop Icon

echo ======================================================
echo   TORN COMMANDER - T ICON DESKTOP SHORTCUT
echo ======================================================
echo.
if not exist "%~dp0dist\TornCommander.exe" (
  echo The actual Commander EXE does not exist yet.
  echo First run REBUILD_AND_LAUNCH.bat to compile and launch it.
  echo Then close Commander and run this shortcut creator.
  echo.
  pause
  exit /b 1
)

if exist "%USERPROFILE%\Desktop\Torn Commander.lnk" (
  echo Existing shortcut detected. It will be redirected to this folder's EXE.
)
if exist "%OneDrive%\Desktop\Torn Commander.lnk" (
  echo Existing OneDrive Desktop shortcut detected. It will be redirected to this folder's EXE.
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0CREATE_DESKTOP_SHORTCUT.ps1" -Force
set "RC=%errorlevel%"
echo.
if "%RC%"=="0" (
  echo All set. Use the new T-branded Torn Commander desktop shortcut.
) else (
  echo Shortcut creation failed - see the message above.
)
pause
exit /b %RC%
