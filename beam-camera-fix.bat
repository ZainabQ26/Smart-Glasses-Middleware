@echo off
title Smart Glasses - Beam Camera Fix (ADB Chrome Wrapper)
setlocal enabledelayedexpansion

set ADB=%LOCALAPPDATA%\Android\Sdk\platform-tools\adb.exe
set PORT=5000

echo ===================================================
echo   Smart Glasses - Beam Camera Access Fix
echo   Configures Chrome on Beam to trust this server
echo ===================================================
echo.

:: Auto-detect the PC IP on the ACTIVE network (the adapter that has the default gateway),
:: so this follows school Wi-Fi / home Wi-Fi / hotspot. No IP is hardcoded.
set LOCAL_IP=
for /f "usebackq delims=" %%A in (`powershell -NoProfile -Command "(Get-NetIPConfiguration | Where-Object { $_.IPv4DefaultGateway -ne $null -and $_.NetAdapter.Status -eq 'Up' } | Select-Object -First 1).IPv4Address.IPAddress"`) do (
    if not defined LOCAL_IP set LOCAL_IP=%%A
)
if not defined LOCAL_IP (
    echo Could not auto-detect this PC's IP address.
    set /p LOCAL_IP=Enter this PC's current IP ^(run ipconfig to find it^): 
)

echo Detected PC IP : %LOCAL_IP%
echo App URL        : http://%LOCAL_IP%:%PORT%
echo.

:: Check ADB exists
if not exist "%ADB%" (
    echo [ERROR] ADB not found. Install Android SDK Platform Tools.
    pause & exit /b 1
)

:: Move to system TEMP to avoid OneDrive path issues
pushd %SYSTEMROOT%\Temp

:: Restart ADB daemon cleanly
echo Restarting ADB daemon...
"%ADB%" kill-server >nul 2>&1
timeout /t 2 >nul
"%ADB%" start-server >nul 2>&1
timeout /t 2 >nul

:: Check device is connected
echo Checking Beam connection...
"%ADB%" devices | findstr /v "List" | findstr "device" >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] No Beam detected via ADB!
    echo  1. Connect Beam to PC via USB-C
    echo  2. Enable USB Debugging on Beam:
    echo     Settings ^> About ^> tap Build Number 7x
    echo     then Developer Options ^> USB Debugging ON
    echo  3. Tap "Allow" on the Beam screen when prompted
    echo.
    popd & pause & exit /b 1
)
echo Beam connected!
echo.

:: Write Chrome command-line flags file on device
echo Writing Chrome camera flags to Beam...
"%ADB%" shell "echo '_ --unsafely-treat-insecure-origin-as-secure=http://%LOCAL_IP%:%PORT%' > /data/local/tmp/chrome-command-line"

:: Force-stop Chrome so new flags load
echo Restarting Chrome on Beam...
"%ADB%" shell am force-stop com.android.chrome
timeout /t 2 >nul

:: Launch Chrome pointing at the app
echo Launching app on Beam...
"%ADB%" shell am start -a android.intent.action.VIEW -d "http://%LOCAL_IP%:%PORT%" com.android.chrome

popd

echo.
echo ===================================================
echo   Done! Chrome on Beam now trusts the camera origin.
echo   Say "Iris, take picture" to scan a document.
echo ===================================================
echo.
pause
