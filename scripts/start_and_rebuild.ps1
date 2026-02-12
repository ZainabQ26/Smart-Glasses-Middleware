<#
scripts\start_and_rebuild.ps1
PowerShell helper to build the app image, recreate the container, wait for health, and open admin UI.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Push-Location (Join-Path $PSScriptRoot '..')

Write-Host "Building 'app' image..."
Write-Host "Ensuring dependent images are available (pulling when needed)..."
try {
    docker compose pull
} catch {
    Write-Host "docker compose pull failed or some images not available; proceeding."
}

Write-Host "Building 'app' image..."
docker compose build app

Write-Host "Recreating 'app' container (start dependencies if missing)..."
docker compose up -d --force-recreate app

Write-Host "Starting any stopped services (so all containers are running)..."
try { docker compose start } catch { }

Write-Host "Waiting for http://localhost:5000/health to return success..."
$maxAttempts = 60
$attempt = 0
while ($attempt -lt $maxAttempts) {
    try {
        $resp = Invoke-RestMethod -Uri http://localhost:5000/health -TimeoutSec 5 -ErrorAction Stop
        Write-Host "App is healthy."
        break
    } catch {
        Write-Host -NoNewline "."
        Start-Sleep -Seconds 5
        $attempt++
    }
}

if ($attempt -ge $maxAttempts) {
    Write-Host "\nTimed out waiting for app health. Check logs with: docker compose logs --tail=200 app"
    Exit 1
}

$adminUrl = 'http://localhost:5000/admin.html'
Write-Host "Opening admin UI at: $adminUrl"
try {
    Start-Process $adminUrl
} catch {
    Write-Host "Could not open browser automatically. Visit: $adminUrl"
}

Pop-Location
