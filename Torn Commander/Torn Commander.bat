@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title Torn Commander v0.6.64 Launcher
echo.
echo ==========================================================
echo   TORN COMMANDER - CURRENT SOURCE
echo   Launcher: %~f0
echo   Project : %~dp0
echo ==========================================================
echo Source version:
if exist "%~dp0VERSION.txt" type "%~dp0VERSION.txt"
echo.

set "TC_DIST_DIR=dist"
if exist "%~dp0ACTIVE_DIST.txt" (
  for /f "usebackq delims=" %%D in ("%~dp0ACTIVE_DIST.txt") do set "TC_DIST_DIR=%%D"
)
if not exist "%~dp0%TC_DIST_DIR%\TornCommander.exe" set "TC_DIST_DIR=dist"

if exist "%~dp0FIRST_RUN_REBUILD.flag" (
  echo First run after upgrade: bypassing any old compiled EXE.
  goto :BUILD_FROM_SOURCE
)

if exist "%~dp0%TC_DIST_DIR%\TornCommander.exe" (
  if exist "%~dp0%TC_DIST_DIR%\VERSION.txt" if exist "%~dp0VERSION.txt" (
    fc /b "%~dp0VERSION.txt" "%~dp0%TC_DIST_DIR%\VERSION.txt" >nul 2>&1
    if not errorlevel 1 (
      echo Launching compiled version from:
      echo   %~dp0%TC_DIST_DIR%\TornCommander.exe
      echo If the window shows an older version, close all old TornCommander.exe processes.
      call :LAUNCH_CHECK
      if errorlevel 1 exit /b 1
      exit /b 0
    )
  )
  echo.
  echo A newer Torn Commander source version is present. Rebuilding the EXE...
)

:BUILD_FROM_SOURCE
echo.
echo ==========================================================
echo   TORN COMMANDER - BUILD / UPDATE
echo ==========================================================
echo.
echo Torn Commander will now build the current source version.
echo This happens on first run and after version updates.
echo.

call :FIND_DOTNET8
if not errorlevel 1 goto :HAVE_SDK

echo .NET is installed, but the required .NET 8 SDK was not found.
echo Torn Commander needs the SDK only once to build the Windows EXE.
echo.

where winget >nul 2>&1
if errorlevel 1 goto :NO_WINGET

echo Installing the official Microsoft .NET 8 SDK with winget...
echo Windows may show an installer/UAC prompt. Approve it to continue.
echo.
winget install --id Microsoft.DotNet.SDK.8 -e --source winget --accept-source-agreements --accept-package-agreements
if errorlevel 1 goto :SDK_INSTALL_FAIL

rem Re-discover dotnet after the installer updates the machine.
set "DOTNET_EXE="
call :FIND_DOTNET8
if errorlevel 1 goto :SDK_NOT_VISIBLE

:HAVE_SDK
echo Using .NET SDK:
echo   %DOTNET_EXE%
"%DOTNET_EXE%" --version
echo.

set "TORNCOMMANDER_DOTNET=%DOTNET_EXE%"
set "TC_LOG_ROOT=%LOCALAPPDATA%\TornCommander"
if not exist "%TC_LOG_ROOT%" mkdir "%TC_LOG_ROOT%" >nul 2>&1
set "TC_BUILD_LOG=%TC_LOG_ROOT%\build-last.log"
echo Saving full build output to: %TC_BUILD_LOG%
call "%~dp0PUBLISH_EXE.bat" >"%TC_BUILD_LOG%" 2>&1
set "BUILD_RESULT=%errorlevel%"
if "%BUILD_RESULT%"=="0" set "TC_DIST_DIR=dist"
type "%TC_BUILD_LOG%"
if not "%BUILD_RESULT%"=="0" goto :BUILD_FAIL

if exist "%~dp0%TC_DIST_DIR%\TornCommander.exe" (
  echo.
  if exist "%~dp0FIRST_RUN_REBUILD.flag" del /q "%~dp0FIRST_RUN_REBUILD.flag" >nul 2>&1
  echo Build complete. Launching Torn Commander...
  echo Executable: %~dp0%TC_DIST_DIR%\TornCommander.exe
  call :LAUNCH_CHECK
  if errorlevel 1 exit /b 1
  exit /b 0
)

goto :BUILD_FAIL


:LAUNCH_CHECK
if not exist "%~dp0LAUNCH_CHECK.ps1" (
  echo ERROR: Startup verifier is missing from this package.
  pause
  exit /b 25
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0LAUNCH_CHECK.ps1" -ExePath "%~dp0%TC_DIST_DIR%\TornCommander.exe" -SourceVersion "0.6.64"
set "LAUNCH_RESULT=%errorlevel%"
if not "%LAUNCH_RESULT%"=="0" (
  echo.
  echo The app did not produce a visible window.  Exit code: %LAUNCH_RESULT%
  echo.
  echo IMPORTANT: DO NOT DELETE your app data or browser profile.
  echo Open this folder to find the startup diagnostics:
  echo   %LOCALAPPDATA%\TornCommander
  echo Relevant files: launcher-last.log / startup-diagnostic.log / startup-crash.log
  echo.
  echo Send the log text and any errors printed here; never send your API keys.
  pause
)
exit /b %LAUNCH_RESULT%

:FIND_DOTNET8
set "DOTNET_EXE="

rem Prefer the normal 64-bit machine install.
if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"

rem Also support a per-user dotnet install.
if not defined DOTNET_EXE if exist "%LocalAppData%\Microsoft\dotnet\dotnet.exe" set "DOTNET_EXE=%LocalAppData%\Microsoft\dotnet\dotnet.exe"

rem Fall back to whatever PATH resolves, but do not confuse a runtime-only install with an SDK.
if not defined DOTNET_EXE (
  for /f "delims=" %%D in ('where dotnet 2^>nul') do (
    if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
  )
)

if not defined DOTNET_EXE exit /b 1

"%DOTNET_EXE%" --list-sdks 2>nul | findstr /R /B "8\.[0-9][0-9]*\." >nul
if errorlevel 1 (
  set "DOTNET_EXE="
  exit /b 1
)
exit /b 0

:NO_WINGET
echo.
echo The .NET 8 SDK is not installed and Windows Package Manager ^(winget^) was not found.
echo Install the official .NET 8 SDK from:
echo https://dotnet.microsoft.com/download/dotnet/8.0
echo.
echo IMPORTANT: choose the SDK, not just the Desktop Runtime.
echo Then double-click Torn Commander.bat again.
pause
exit /b 1

:SDK_INSTALL_FAIL
echo.
echo The .NET 8 SDK installation did not complete successfully.
echo You can install it manually from:
echo https://dotnet.microsoft.com/download/dotnet/8.0
echo Then run Torn Commander.bat again.
pause
exit /b 1

:SDK_NOT_VISIBLE
echo.
echo The installer finished, but this launcher still cannot see a .NET 8 SDK.
echo Close this window and double-click Torn Commander.bat again.
echo If it still fails, restart Windows once so the installer PATH changes take effect.
pause
exit /b 1

:BUILD_FAIL
echo.
echo Build failed. See the messages above.
echo A full copy of the build output is saved at:
echo   %LOCALAPPDATA%\TornCommander\build-last.log
echo If package restore failed, verify Windows can reach https://api.nuget.org/
echo.
echo The most useful files for troubleshooting are:
echo   TornCommander.csproj
echo   PUBLISH_EXE.bat
echo.
pause
exit /b 1
