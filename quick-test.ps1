# Quick Start and Test Script for Smart Glasses Middleware
# This script will start all services and test the prompt functionality

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Smart Glasses Middleware - Quick Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Check Docker is running
Write-Host "[1/7] Checking Docker..." -ForegroundColor Yellow
try {
    $dockerVersion = docker --version 2>$null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  ✓ Docker is running: $dockerVersion" -ForegroundColor Green
    } else {
        throw "Docker not found"
    }
} catch {
    Write-Host "  ✗ Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}

# Step 2: Start containers
Write-Host ""
Write-Host "[2/7] Starting Docker containers..." -ForegroundColor Yellow
docker-compose up -d
if ($LASTEXITCODE -ne 0) {
    Write-Host "  ✗ Failed to start containers" -ForegroundColor Red
    exit 1
}
Write-Host "  ✓ Containers started" -ForegroundColor Green

# Step 3: Wait for services to be healthy
Write-Host ""
Write-Host "[3/7] Waiting for services to be healthy (this may take 30-60 seconds)..." -ForegroundColor Yellow
$maxWait = 60
$waited = 0
$interval = 5

while ($waited -lt $maxWait) {
    Write-Host "  Checking health... ($waited/$maxWait seconds)" -ForegroundColor Gray
    
    try {
        $response = Invoke-RestMethod -Uri "http://localhost:5000/health" -Method Get -TimeoutSec 5 -ErrorAction SilentlyContinue
        if ($response.status -eq "Healthy") {
            Write-Host "  ✓ All services are healthy!" -ForegroundColor Green
            break
        } else {
            Write-Host "  Services starting... Status: $($response.status)" -ForegroundColor Gray
        }
    } catch {
        Write-Host "  Services not ready yet..." -ForegroundColor Gray
    }
    
    Start-Sleep -Seconds $interval
    $waited += $interval
}

if ($waited -ge $maxWait) {
    Write-Host "  ⚠ Health check timeout. Continuing anyway..." -ForegroundColor Yellow
}

# Step 4: Check Ollama and pull models
Write-Host ""
Write-Host "[4/7] Checking Ollama models..." -ForegroundColor Yellow
Write-Host "  Checking for llama-3.2-3b..." -ForegroundColor Gray
$models = docker exec smartglasses-ollama ollama list 2>$null
if ($models -match "llama-3.2-3b") {
    Write-Host "  ✓ llama-3.2-3b model found" -ForegroundColor Green
} else {
    Write-Host "  ! Pulling llama-3.2-3b (this will take several minutes)..." -ForegroundColor Yellow
    docker exec smartglasses-ollama ollama pull llama-3.2-3b
    Write-Host "  ✓ llama-3.2-3b model pulled" -ForegroundColor Green
}

Write-Host "  Checking for nomic-embed-text..." -ForegroundColor Gray
if ($models -match "nomic-embed-text") {
    Write-Host "  ✓ nomic-embed-text model found" -ForegroundColor Green
} else {
    Write-Host "  ! Pulling nomic-embed-text (this will take a few minutes)..." -ForegroundColor Yellow
    docker exec smartglasses-ollama ollama pull nomic-embed-text
    Write-Host "  ✓ nomic-embed-text model pulled" -ForegroundColor Green
}

# Step 5: Test Ollama directly
Write-Host ""
Write-Host "[5/7] Testing Ollama directly..." -ForegroundColor Yellow
try {
    $ollamaTest = Invoke-RestMethod -Uri "http://localhost:11434/api/tags" -Method Get -TimeoutSec 10
    Write-Host "  ✓ Ollama is accessible" -ForegroundColor Green
} catch {
    Write-Host "  ✗ Cannot reach Ollama at http://localhost:11434" -ForegroundColor Red
    Write-Host "    Error: $($_.Exception.Message)" -ForegroundColor Red
}

# Step 6: Open monitoring page
Write-Host ""
Write-Host "[6/7] Opening monitoring page..." -ForegroundColor Yellow
Start-Process "http://localhost:5000/monitoring.html"
Start-Sleep -Seconds 2
Write-Host "  ✓ Monitoring page opened in browser" -ForegroundColor Green

# Step 7: Test the prompt endpoint
Write-Host ""
Write-Host "[7/7] Testing prompt endpoint..." -ForegroundColor Yellow
Write-Host "  Sending test query to /api/documents/query..." -ForegroundColor Gray

$testQuery = @{
    Query = "What is the weather today?"
    TopK = 5
} | ConvertTo-Json

try {
    Write-Host "  Request: $testQuery" -ForegroundColor Gray
    $startTime = Get-Date
    
    $response = Invoke-RestMethod -Uri "http://localhost:5000/api/documents/query" `
        -Method Post `
        -Body $testQuery `
        -ContentType "application/json" `
        -TimeoutSec 60
    
    $duration = ((Get-Date) - $startTime).TotalSeconds
    
    Write-Host ""
    Write-Host "  ✓ Request succeeded in $([math]::Round($duration, 2)) seconds!" -ForegroundColor Green
    Write-Host ""
    Write-Host "  Response:" -ForegroundColor Cyan
    Write-Host "  Answer: $($response.answer)" -ForegroundColor White
    Write-Host "  Model Used: $($response.modelUsed)" -ForegroundColor Gray
    Write-Host "  Sources: $($response.sources.Count)" -ForegroundColor Gray
    
} catch {
    Write-Host ""
    Write-Host "  ✗ Request failed!" -ForegroundColor Red
    Write-Host "  Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "  Response: $responseBody" -ForegroundColor Yellow
    }
}

# Final instructions
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Next Steps:" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "1. Check monitoring page: http://localhost:5000/monitoring.html" -ForegroundColor White
Write-Host "2. Open admin panel: http://localhost:5000/admin.html" -ForegroundColor White
Write-Host "3. Go to Prompts tab and test sending queries" -ForegroundColor White
Write-Host ""
Write-Host "To view Ollama logs:" -ForegroundColor Yellow
Write-Host "  docker logs smartglasses-ollama -f" -ForegroundColor Gray
Write-Host ""
Write-Host "To view app logs:" -ForegroundColor Yellow  
Write-Host "  docker logs smartglasses-app -f" -ForegroundColor Gray
Write-Host ""
Write-Host "To stop all services:" -ForegroundColor Yellow
Write-Host "  docker-compose down" -ForegroundColor Gray
Write-Host ""
