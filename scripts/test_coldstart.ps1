#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Cold-start test for the Smart Glasses wakeup flow.

.DESCRIPTION
    1. Scales ECS to 0 (simulates an idle, sleeping backend).
    2. Hits the CloudFront URL every 5 s, printing HTTP status for each path.
    3. Times how long until /health returns 200  (ECS is up).
    4. Prints a pass/fail summary.

.PARAMETER BaseUrl
    Root URL to test against. Defaults to the CloudFront distribution.

.PARAMETER TimeoutSeconds
    Give up waiting after this many seconds. Default 300 (5 min).

.PARAMETER SkipSleep
    Skip the "scale to 0" step and just run the health poll.

.EXAMPLE
    .\scripts\test_coldstart.ps1
    .\scripts\test_coldstart.ps1 -SkipSleep
    .\scripts\test_coldstart.ps1 -TimeoutSeconds 240
#>
param(
    [string]$BaseUrl        = "https://d3t5kcvyyonw8d.cloudfront.net",
    [int]   $TimeoutSeconds = 300,
    [switch]$SkipSleep
)

$Cluster = "smart-glasses-dev-cluster"
$Service = "smart-glasses-dev-api-service"
$PollSec = 5

function Write-Step([string]$msg) { Write-Host "`n>> $msg" -ForegroundColor Cyan }
function Write-Pass([string]$msg) { Write-Host "   PASS  $msg" -ForegroundColor Green }
function Write-Fail([string]$msg) { Write-Host "   FAIL  $msg" -ForegroundColor Red }
function Write-Info([string]$msg) { Write-Host "   $msg" -ForegroundColor Gray }

function Get-EcsCount([string]$field) {
    $val = aws --no-cli-pager ecs describe-services `
        --cluster $Cluster --services $Service `
        --query "services[0].$field" --output text 2>$null
    return [int]$val
}

function Invoke-Check([string]$url) {
    try {
        $r = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 8 -ErrorAction Stop
        return [PSCustomObject]@{
            Status      = [int]$r.StatusCode
            ContentType = "$($r.Headers['Content-Type'])"
            OK          = $true
        }
    } catch [System.Net.WebException] {
        $code = if ($_.Exception.Response) { [int]$_.Exception.Response.StatusCode } else { 0 }
        return [PSCustomObject]@{ Status = $code; ContentType = ""; OK = $false }
    } catch {
        return [PSCustomObject]@{ Status = 0; ContentType = ""; OK = $false }
    }
}

# ---------------------------------------------------------------------------
# Step 0: verify AWS CLI
# ---------------------------------------------------------------------------
Write-Step "Checking AWS credentials"
$identity = aws --no-cli-pager sts get-caller-identity --output json 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "AWS credentials not configured. Run 'aws sso login'."
    exit 1
}
Write-Pass "Authenticated as $(($identity | ConvertFrom-Json).Arn)"

# ---------------------------------------------------------------------------
# Step 1: scale to 0
# ---------------------------------------------------------------------------
if (-not $SkipSleep) {
    Write-Step "Scaling ECS to 0 (cold start simulation)"
    aws --no-cli-pager ecs update-service `
        --cluster $Cluster --service $Service `
        --desired-count 0 --query "service.desiredCount" --output text | Out-Null

    $drainStart = Get-Date
    while ((Get-EcsCount "runningCount") -gt 0) {
        if (((Get-Date) - $drainStart).TotalSeconds -gt 90) { break }
        Write-Info "Draining tasks... running=$(Get-EcsCount 'runningCount')"
        Start-Sleep 5
    }
    # Wait out the ALB target deregistration delay (30s) so the task is fully removed
    # from the target group before we start polling. Without this, the stopping task
    # still handles requests for ~30s after ECS reports running=0.
    Write-Info "Waiting 35s for ALB deregistration delay to elapse..."
    Start-Sleep 35
    Write-Pass "ECS is at 0 tasks. Backend is asleep."
} else {
    Write-Info "Skipping scale-to-0. Current desired=$(Get-EcsCount 'desiredCount')"
}

# ---------------------------------------------------------------------------
# Step 2: verify S3 serves frontend immediately (no ECS needed)
# ---------------------------------------------------------------------------
Write-Step "Checking frontend loads from S3 while ECS is asleep"
$front = Invoke-Check $BaseUrl
if ($front.Status -eq 200 -and $front.ContentType -match "text/html") {
    Write-Pass "/ returned HTTP 200 text/html immediately (S3 is serving correctly)."
} else {
    Write-Fail "/ returned status=$($front.Status) contentType='$($front.ContentType)' -- expected 200 text/html."
    Write-Info "The frontend must be served from S3 even when ECS is at 0 tasks."
    exit 2
}

# ---------------------------------------------------------------------------
# Step 3: poll /health (mimics WakeupGate) until ECS wakes up
# ---------------------------------------------------------------------------
Write-Step "Polling $BaseUrl/health every ${PollSec}s (max ${TimeoutSeconds}s)"
Write-Info "CloudWatch collects ALB RequestCount every ~60s -> Lambda wakes ECS -> Fargate starts (~60-90s)."
Write-Info "Expected total cold-start: ~2 min."

$start    = Get-Date
$wakeTime = $null
$healthOk = $false

while (-not $healthOk) {
    $elapsed = [int]((Get-Date) - $start).TotalSeconds
    if ($elapsed -ge $TimeoutSeconds) {
        Write-Fail "Timed out after ${elapsed}s."
        exit 3
    }

    $h       = Invoke-Check "$BaseUrl/health"
    $desired = Get-EcsCount "desiredCount"
    $running = Get-EcsCount "runningCount"

    $statusLabel = if ($h.Status -eq 0) { "no-conn" } else { "$($h.Status)" }
    $color = if ($h.Status -eq 200) { "Green" } elseif ($h.Status -eq 0) { "Yellow" } else { "Red" }

    Write-Host ("   [{0,3}s] /health -> " -f $elapsed) -NoNewline
    Write-Host $statusLabel -ForegroundColor $color -NoNewline
    Write-Host ("   ECS desired={0} running={1}" -f $desired, $running) -ForegroundColor Gray

    if ($h.Status -eq 200) {
        $wakeTime = $elapsed
        $healthOk = $true
    } else {
        Start-Sleep $PollSec
    }
}

# ---------------------------------------------------------------------------
# Step 4: parse /health body
# ---------------------------------------------------------------------------
Write-Pass "/health returned 200 after ${wakeTime}s."
$isHealthy = $false
$body = $null
try {
    $body = (Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 10).Content | ConvertFrom-Json
    $isHealthy = $body.status -eq "Healthy"
    $bodyJson  = $body | ConvertTo-Json -Compress
    Write-Info "Health body: $bodyJson"
    if ($isHealthy) { Write-Pass "status = Healthy" } else { Write-Fail "status = $($body.status)" }
} catch {
    Write-Info "(Could not parse health body: $_)"
}

# ---------------------------------------------------------------------------
# Step 5: re-check frontend after wakeup (no stale error cache)
# ---------------------------------------------------------------------------
Write-Step "Re-checking frontend after wakeup"
$front2 = Invoke-Check $BaseUrl
if ($front2.Status -eq 200) {
    Write-Pass "/ still returns 200 after backend woke up."
} else {
    Write-Fail "/ returned $($front2.Status) after wakeup."
}

# ---------------------------------------------------------------------------
# Summary
# ---------------------------------------------------------------------------
$frontPass   = $front.Status -eq 200
$healthPass  = $healthOk
$front2Pass  = $front2.Status -eq 200
$overallPass = $frontPass -and $healthPass -and $front2Pass

Write-Host ""
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host " COLD-START TEST SUMMARY" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
$row = {
    param($label, $pass, $detail)
    $icon  = if ($pass) { "PASS" } else { "FAIL" }
    $color = if ($pass) { "Green" } else { "Red" }
    Write-Host ("  {0,-28}" -f $label) -NoNewline
    Write-Host ("{0,6}" -f $icon) -ForegroundColor $color -NoNewline
    Write-Host "  $detail" -ForegroundColor Gray
}
& $row "Frontend from S3 (cold)"  $frontPass  "HTTP $($front.Status)"
& $row "Backend wake time"        $healthPass "${wakeTime}s"
& $row "Backend /health"          $isHealthy  "status=$(if ($body) { $body.status } else { 'N/A' })"
& $row "Frontend after wakeup"    $front2Pass "HTTP $($front2.Status)"
Write-Host "==========================================" -ForegroundColor Cyan
if ($overallPass) {
    Write-Host "  OVERALL: PASS" -ForegroundColor Green
} else {
    Write-Host "  OVERALL: FAIL" -ForegroundColor Red
}
Write-Host "==========================================" -ForegroundColor Cyan
exit $(if ($overallPass) { 0 } else { 1 })
