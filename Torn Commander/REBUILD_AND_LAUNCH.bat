@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Torn Commander - FORCE REBUILD

echo ==========================================================
echo   TORN COMMANDER - FORCE CLEAN REBUILD
 echo  Folder: %~dp0
 echo  Source version:
if exist "%~dp0VERSION.txt" type "%~dp0VERSION.txt"
echo ==========================================================
echo.
tasklist /FI "IMAGENAME eq TornCommander.exe" 2>nul | findstr /I /C:"TornCommander.exe" >nul
if not errorlevel 1 (
  echo TornCommander.exe is still running. Close ALL Commander windows first.
  echo This prevents an outdated running instance from covering the new build.
  pause
  exit /b 1
)
if exist "%~dp0dist" rmdir /s /q "%~dp0dist"
if exist "%~dp0obj" rmdir /s /q "%~dp0obj"
if exist "%~dp0bin" rmdir /s /q "%~dp0bin"
echo Cleaned old compiled output. Building this folder's source now.
call "%~dp0Torn Commander.bat"
set "RESULT=%errorlevel%"
if not "%RESULT%"=="0" (
  echo.
  echo Startup/build failed with code %RESULT%.
  echo See %LOCALAPPDATA%\TornCommander\build-last.log and launcher-last.log
  pause
)
exit /b %RESULT%
