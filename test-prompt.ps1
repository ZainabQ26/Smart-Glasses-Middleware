# PowerShell test script for the prompt functionality
# This script tests the /api/documents/query endpoint

param(
    [string]$BaseUrl = "http://localhost:5000",
    [string]$Query = "What is in the documents?"
)

Write-Host "Testing prompt endpoint..." -ForegroundColor Cyan
Write-Host "Base URL: $BaseUrl" -ForegroundColor Gray
Write-Host "Query: $Query" -ForegroundColor Gray
Write-Host ""

# Test the documents/query endpoint
$url = "$BaseUrl/api/documents/query"
$body = @{
    Query = $Query
    TopK = 5
} | ConvertTo-Json

Write-Host "Sending POST request to: $url" -ForegroundColor Yellow
Write-Host "Request body: $body" -ForegroundColor Gray
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri $url -Method Post -Body $body -ContentType "application/json" -TimeoutSec 150
    
    Write-Host "✓ Request successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Response:" -ForegroundColor Cyan
    Write-Host ($response | ConvertTo-Json -Depth 10)
    
} catch {
    Write-Host "✗ Request failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    
    if ($_.Exception.Response) {
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $responseBody = $reader.ReadToEnd()
        Write-Host "Response body: $responseBody" -ForegroundColor Yellow
    }
    
    exit 1
}

# Also test the monitoring endpoint
Write-Host ""
Write-Host "Checking request monitoring..." -ForegroundColor Cyan
$monitorUrl = "$BaseUrl/api/monitoring/requests?count=5"

try {
    $monitorResponse = Invoke-RestMethod -Uri $monitorUrl -Method Get -TimeoutSec 10
    Write-Host "✓ Monitoring endpoint working!" -ForegroundColor Green
    Write-Host "Recent requests:" -ForegroundColor Cyan
    Write-Host ($monitorResponse | ConvertTo-Json -Depth 10)
} catch {
    Write-Host "✗ Monitoring endpoint failed!" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Yellow
}
