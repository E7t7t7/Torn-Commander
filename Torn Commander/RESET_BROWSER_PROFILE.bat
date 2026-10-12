@echo off
setlocal
set "DATA=%LOCALAPPDATA%\TornCommander\WebView2Profile"
echo This removes Torn Commander's browser cookies/cache/extensions.
echo It DOES NOT remove the API key from Windows Credential Manager.
echo.
choice /m "Reset the Torn Commander WebView2 browser profile"
if errorlevel 2 exit /b 0
if exist "%DATA%" rmdir /s /q "%DATA%"
echo Browser profile reset.
pause
