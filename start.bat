@echo off
title Smart Glasses Middleware - Launcher
echo ===================================================
echo   Smart Glasses Middleware - Starting Services
echo ===================================================
echo.
echo Checking Docker Desktop status...
docker info >nul 2>&1
if %errorlevel% neq 0 (
    echo.
    echo [ERROR] Docker Desktop is not running!
    echo Please start Docker Desktop on your computer and try again.
    echo.
    pause
    exit /b 1
)

echo Starting Docker containers (API + Database + HTTPS Tunnel)...
docker compose -f docker-compose.localstack.yml up -d

echo.
echo ===================================================
echo   Waiting for services and secure HTTPS link...
echo ===================================================
timeout /t 6 >nul

:: Extract the Cloudflare HTTPS Tunnel URL
set TUNNEL_URL=
for /f "usebackq tokens=*" %%U in (`powershell -NoProfile -Command "$m = (docker logs localstack-tunnel 2>&1 | Select-String 'https://[a-zA-Z0-9-]+\.trycloudflare\.com' | Select-Object -First 1); if ($m) { $m.Matches[0].Value }"`) do (
    set TUNNEL_URL=%%U
)

echo.
echo ===================================================
echo   App is Ready!
echo ===================================================
echo   PC Local:        http://localhost:5000/
if defined TUNNEL_URL (
    echo   Phone / Beam:    %TUNNEL_URL%
    echo                    ^(Secure HTTPS - Camera works automatically!^)
) else (
    echo   Phone / Beam:    Run 'docker logs localstack-tunnel' to view HTTPS link
)
echo ===================================================
echo.

start http://localhost:5000/

echo.
echo Press any key to exit this window (app will keep running in Docker).
echo Run stop.bat when you are done.
pause
