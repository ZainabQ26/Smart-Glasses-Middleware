# Quick start script for Next.js + .NET development

Write-Host "Starting Smart Glasses Middleware with Next.js frontend..." -ForegroundColor Green

# Check if docker-compose is available
if (Get-Command docker-compose -ErrorAction SilentlyContinue) {
    Write-Host "`nStarting services with Docker Compose..." -ForegroundColor Cyan
    docker-compose -f docker-compose.nextjs.yml up -d
    
    Write-Host "`nServices starting. Access:" -ForegroundColor Green
    Write-Host "  - Frontend: http://localhost:3000" -ForegroundColor Yellow
    Write-Host "  - Backend API: http://localhost:5000" -ForegroundColor Yellow
    Write-Host "  - MinIO Console: http://localhost:9001" -ForegroundColor Yellow
    Write-Host "`nTo view logs: docker-compose -f docker-compose.nextjs.yml logs -f" -ForegroundColor Gray
} else {
    Write-Host "`nDocker Compose not found. Starting services manually..." -ForegroundColor Yellow
    
    # Install Next.js dependencies if needed
    if (-not (Test-Path "next-app\node_modules")) {
        Write-Host "`nInstalling Next.js dependencies..." -ForegroundColor Cyan
        Push-Location next-app
        npm install
        Pop-Location
    }
    
    Write-Host "`nStarting .NET backend..." -ForegroundColor Cyan
    Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd src\SmartGlassesMiddleware; dotnet run"
    
    Write-Host "Waiting for backend to start..." -ForegroundColor Gray
    Start-Sleep -Seconds 3
    
    Write-Host "`nStarting Next.js frontend..." -ForegroundColor Cyan
    Start-Process pwsh -ArgumentList "-NoExit", "-Command", "cd next-app; npm run dev"
    
    Write-Host "`nServices starting in separate terminals." -ForegroundColor Green
    Write-Host "  - Frontend: http://localhost:3000" -ForegroundColor Yellow
    Write-Host "  - Backend API: http://localhost:5000" -ForegroundColor Yellow
}

Write-Host "`nPress any key to continue..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
