Set-StrictMode -Version Latest

$compose = "docker-compose.localstack.yml"
if (-not (Test-Path $compose)) {
    Write-Error "$compose not found in repository root."
    exit 1
}

if (-not (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
    Write-Host "docker-compose not found. Trying docker compose (v2+)"
    $composeCmd = "docker compose"
} else {
    $composeCmd = "docker-compose"
}

Write-Host "Starting LocalStack using $compose"
& $composeCmd -f $compose up -d

Write-Host "LocalStack started. To stop: $composeCmd -f $compose down"
