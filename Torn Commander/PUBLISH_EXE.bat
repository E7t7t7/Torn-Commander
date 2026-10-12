@echo off
setlocal EnableExtensions
cd /d "%~dp0"

title Torn Commander Publisher

set "DOTNET_EXE=%TORNCOMMANDER_DOTNET%"
if defined DOTNET_EXE goto :VERIFY_SDK

if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET_EXE if exist "%LocalAppData%\Microsoft\dotnet\dotnet.exe" set "DOTNET_EXE=%LocalAppData%\Microsoft\dotnet\dotnet.exe"
if not defined DOTNET_EXE (
  for /f "delims=" %%D in ('where dotnet 2^>nul') do (
    if not defined DOTNET_EXE set "DOTNET_EXE=%%D"
  )
)

:VERIFY_SDK
if not defined DOTNET_EXE (
  echo .NET executable not found.
  exit /b 1
)

"%DOTNET_EXE%" --list-sdks 2>nul | findstr /R /B "8\.[0-9][0-9]*\." >nul
if errorlevel 1 (
  echo A .NET runtime is present, but the .NET 8 SDK is not installed.
  echo Install Microsoft.DotNet.SDK.8 and try again.
  exit /b 1
)

echo Publishing source from: %~dp0
echo Project version:
if exist "%~dp0VERSION.txt" type "%~dp0VERSION.txt"
echo Running source sanity checks...

rem Guard against accidentally packaging another full source tree inside this project.
rem SDK-style projects compile **\*.cs recursively, so a nested duplicate copy causes CS0101/CS0111 storms.
if exist "%~dp0Torn Commander\TornCommander.csproj" (
  echo Source sanity check failed: duplicate nested Torn Commander source tree detected.
  echo Delete the nested "Torn Commander" folder inside this source folder and try again.
  exit /b 1
)
findstr /C:"public List<string> StockPerks" "%~dp0Models.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: TornSnapshot.StockPerks is missing from Models.cs.
  exit /b 1
)
rem The privacy migration must have a matching serialized AppSettings flag.
findstr /C:"public bool LegacyExampleLabelsReviewed" "%~dp0Models.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: AppSettings.LegacyExampleLabelsReviewed is missing from Models.cs.
  exit /b 1
)
findstr /C:"public string GuideActionMode" "%~dp0Models.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: AppSettings.GuideActionMode is missing from Models.cs.
  exit /b 1
)
findstr /C:"public async Task<string?> ExecuteScriptAsync" "%~dp0BrowserTab.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: BrowserTab.ExecuteScriptAsync is missing.
  exit /b 1
)
findstr /C:"Could not install {displayName}:\n\n{ex.Message}" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Tools installer error dialog string is malformed.
  exit /b 1
)
findstr /C:"Installed/updated and enabled:\n\n" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Recommended pack result string is malformed.
  exit /b 1
)
if not exist "%~dp0MeritHunter.cs" (
  echo Source sanity check failed: MeritHunter.cs is missing.
  exit /b 1
)
findstr /C:"public List<MeritAward> Awards" "%~dp0Models.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: TornSnapshot.Awards is missing from Models.cs.
  exit /b 1
)
findstr /C:"BuildMerits()" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Merit Hunter UI is missing from MainForm.cs.
  exit /b 1
)
if not exist "%~dp0ShoppingCart.cs" (
  echo Source sanity check failed: ShoppingCart.cs is missing.
  exit /b 1
)
findstr /C:"BuildShoppingCart()" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Shopping Cart tab is missing from MainForm.cs.
  exit /b 1
)
findstr /C:"FetchItemMarketAsync" "%~dp0TornApiClient.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Item Market API scanner is missing from TornApiClient.cs.
  exit /b 1
)
findstr /C:"page.php?sid=crimes#/graffiti" "%~dp0TornRoutes.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Graffiti guided-action route must use page.php, not loader.php.
  exit /b 1
)
findstr /C:"strictGuideColor" "%~dp0BundledScripts\Torn_Graffiti_Paint_Helper.user.js" >nul
if errorlevel 1 (
  echo Source sanity check failed: strict Graffiti guided paint selection is missing.
  exit /b 1
)
findstr /C:"Residential District" "%~dp0RecommendationEngine.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Graffiti guided target must use Residential District.
  exit /b 1
)
findstr /C:"public List<AppProfile> Profiles" "%~dp0Models.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: account profile model is missing.
  exit /b 1
)
findstr /C:"private Control BuildLossSelling()" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: integrated Loss Desk is missing.
  exit /b 1
)
if not exist "%~dp0MainForm.LossAutoCycle.cs" (
  echo Source sanity check failed: integrated loss auto-cycle controller is missing.
  exit /b 1
)
if not exist "%~dp0BundledScripts\Torn_Loss_AutoCycle.js" (
  echo Source sanity check failed: integrated loss auto-cycle browser worker is missing.
  exit /b 1
)
findstr /C:"AUTO CYCLE - INTEGRATED" "%~dp0MainForm.LossAutoCycle.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: integrated loss auto-cycle UI marker is missing.
  exit /b 1
)
findstr /C:"tc-loss-claim" "%~dp0BundledScripts\Torn_Loss_AutoCycle.js" >nul
if errorlevel 1 (
  echo Source sanity check failed: loss auto-cycle claim/lease bridge is missing.
  exit /b 1
)
findstr /C:"595106" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Loss X-change installer is missing.
  exit /b 1
)
findstr /C:"tc-gm-xhr" "%~dp0BrowserTab.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: native GM request bridge is missing.
  exit /b 1
)
if not exist "%~dp0TravelPlanner.cs" (
  echo Source sanity check failed: TravelPlanner.cs is missing.
  exit /b 1
)
if not exist "%~dp0MainForm.ModernDashboard.cs" (
  echo Source sanity check failed: modern Commander dashboard is missing.
  exit /b 1
)
findstr /C:"BuildModernDashboardHost" "%~dp0MainForm.ModernDashboard.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: modern dashboard host is missing.
  exit /b 1
)
findstr /C:"BuildTravel()" "%~dp0MainForm.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: Travel tab is missing from MainForm.cs.
  exit /b 1
)
findstr /C:"DroqsDB public forecast API" "%~dp0TravelPlanner.cs" >nul
if errorlevel 1 (
  echo Source sanity check failed: public travel forecast integration is missing.
  exit /b 1
)
if not exist "%~dp0APPLY_COMMANDER_UPDATE.ps1" (
  echo Source sanity check failed: update staging helper is missing.
  exit /b 1
)
echo Source sanity checks passed.
echo.

echo Restoring packages with:
echo   %DOTNET_EXE%
"%DOTNET_EXE%" restore "%~dp0TornCommander.csproj"
if errorlevel 1 exit /b 1

echo.
echo Publishing Windows x64 self-contained build...
if exist "%~dp0dist" rmdir /s /q "%~dp0dist"
"%DOTNET_EXE%" publish "%~dp0TornCommander.csproj" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "%~dp0dist"
if errorlevel 1 exit /b 1

if not exist "%~dp0dist\TornCommander.exe" (
  echo Publish returned successfully, but TornCommander.exe was not created.
  exit /b 1
)

if exist "%~dp0VERSION.txt" copy /y "%~dp0VERSION.txt" "%~dp0dist\VERSION.txt" >nul

echo.
echo Done:
echo %~dp0dist\TornCommander.exe
echo.
echo For a proper dark T Desktop icon, run:
echo   %~dp0CREATE_DESKTOP_SHORTCUT.bat
echo.
exit /b 0
