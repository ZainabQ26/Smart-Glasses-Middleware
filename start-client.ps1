# Quick start script for Vite + React client

Write-Host "Starting Smart Glasses Client (Vite + React)..." -ForegroundColor Green

# Check if node_modules exists
if (-not (Test-Path "client-app\node_modules")) {
    Write-Host "`nInstalling dependencies..." -ForegroundColor Cyan
    Push-Location client-app
    npm install
    Pop-Location
}

Write-Host "`nStarting .NET backend..." -ForegroundColor Cyan
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd src\SmartGlassesMiddleware; dotnet run"

Write-Host "Waiting for backend to start..." -ForegroundColor Gray
Start-Sleep -Seconds 3

Write-Host "`nStarting Vite dev server..." -ForegroundColor Cyan
Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd client-app; npm run dev"

Write-Host "`nServices starting:" -ForegroundColor Green
Write-Host "  - Backend API: http://localhost:5000" -ForegroundColor Yellow
Write-Host "  - Vite Client: http://localhost:3000" -ForegroundColor Yellow

Write-Host "`nPress any key to continue..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
