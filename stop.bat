@echo off
title Smart Glasses Middleware - Stopper
echo ===================================================
echo   Stopping Smart Glasses Middleware Containers...
echo ===================================================
echo.
docker compose -f docker-compose.localstack.yml down
echo.
echo All containers stopped successfully!
pause
