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

echo Starting Docker containers...
docker compose -f docker-compose.localstack.yml up --build -d

echo.
echo ===================================================
echo   App is starting up!
echo   Opening http://localhost:5000 in your browser...
echo ===================================================
echo.
timeout /t 5 >nul
start http://localhost:5000/
echo.
echo Press any key to exit this window (app will keep running in Docker).
pause
