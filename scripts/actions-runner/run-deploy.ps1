$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $scriptDir

$repoSlug = "robertbiv/Smart-Glasses-Middleware"
$keepRunner = (($env:KEEP_RUNNER ?? "false").ToLowerInvariant() -eq "true")
$keepRunnerData = (($env:KEEP_RUNNER_DATA ?? "false").ToLowerInvariant() -eq "true")
$useLocalRunnerOverride = (($env:USE_LOCAL_RUNNER_OVERRIDE ?? "true").ToLowerInvariant() -eq "true")
$runnerPrefix = "smart-glasses-local-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
$runnerId = ""
$runId = ""
$repoRoot = ""
$tmpWorktree = ""
$tmpBranch = ""

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
  throw "docker is required."
}
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
  throw "gh (GitHub CLI) is required."
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
  throw "git is required."
}

try {
  gh auth status | Out-Null
} catch {
  throw "gh is not authenticated. Run: gh auth login"
}

if (-not (Test-Path ".env")) {
  Copy-Item ".env.example" ".env"
}

$envFile = Get-Content ".env"
if ($envFile -match "^RUNNER_NAME_PREFIX=") {
  $envFile = $envFile -replace "^RUNNER_NAME_PREFIX=.*$", "RUNNER_NAME_PREFIX=$runnerPrefix"
} else {
  $envFile += "RUNNER_NAME_PREFIX=$runnerPrefix"
}

$runnerToken = gh api -X POST "repos/$repoSlug/actions/runners/registration-token" --jq .token
if ([string]::IsNullOrWhiteSpace($runnerToken)) {
  throw "Failed to retrieve runner registration token via gh API."
}

if ($envFile -match "^RUNNER_TOKEN=") {
  $envFile = $envFile -replace "^RUNNER_TOKEN=.*$", "RUNNER_TOKEN=$runnerToken"
} else {
  $envFile += "RUNNER_TOKEN=$runnerToken"
}

# Optional PAT fallback for users who still prefer it.
$token = if ($env:ACCESS_TOKEN) { $env:ACCESS_TOKEN } elseif ($env:GH_PAT) { $env:GH_PAT } elseif ($env:GITHUB_PAT) { $env:GITHUB_PAT } else { "" }
if (-not [string]::IsNullOrWhiteSpace($token)) {
  if ($envFile -match "^ACCESS_TOKEN=") {
    $envFile = $envFile -replace "^ACCESS_TOKEN=.*$", "ACCESS_TOKEN=$token"
  } else {
    $envFile += "ACCESS_TOKEN=$token"
  }
}
Set-Content ".env" $envFile

try {
  $repoRoot = git -C $scriptDir rev-parse --show-toplevel
  if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    throw "Could not determine repository root from script location."
  }

  Write-Host "Starting Dockerized GitHub runner..."
  docker compose up -d

  Write-Host "Waiting briefly for runner registration..."
  Start-Sleep -Seconds 8

  $runnerId = gh api "repos/$repoSlug/actions/runners" --jq ".runners[] | select(.name | startswith(\"$runnerPrefix\")) | .id" | Select-Object -First 1
  if ([string]::IsNullOrWhiteSpace($runnerId)) {
    throw "Could not locate newly registered runner in GitHub."
  }

  Write-Host "Registered runner id=$runnerId (prefix=$runnerPrefix)."

  $ref = if ($env:DEPLOY_REF) { $env:DEPLOY_REF } else { "main" }
  $environment = if ($env:DEPLOY_ENVIRONMENT) { $env:DEPLOY_ENVIRONMENT } else { "dev" }
  $forceRecreateDev = if ($env:DEPLOY_FORCE_RECREATE_DEV) { $env:DEPLOY_FORCE_RECREATE_DEV } else { "false" }

  if ($useLocalRunnerOverride) {
    $tmpBranch = "tmp/local-runner-$($ref -replace '/','-')-$([DateTimeOffset]::UtcNow.ToUnixTimeSeconds())"
    $tmpWorktree = Join-Path ([System.IO.Path]::GetTempPath()) ("sgm-local-runner-" + [Guid]::NewGuid().ToString("N"))

    Write-Host "Creating temporary override branch: $tmpBranch"
    git -C $repoRoot fetch origin $ref
    git -C $repoRoot worktree add $tmpWorktree -b $tmpBranch "origin/$ref"

    $wfPath = Join-Path $tmpWorktree ".github/workflows/deploy-main.yml"
    if (-not (Test-Path $wfPath)) {
      throw "Missing workflow file in temporary worktree: $wfPath"
    }

    (Get-Content $wfPath) -replace 'runs-on: ubuntu-latest', 'runs-on: [self-hosted, linux]' | Set-Content $wfPath

    git -C $tmpWorktree config user.name "local-runner-script"
    git -C $tmpWorktree config user.email "local-runner-script@local"
    git -C $tmpWorktree add .github/workflows/deploy-main.yml
    git -C $tmpWorktree commit -m "chore(ci): temporary self-hosted override for local deploy"
    git -C $tmpWorktree push -u origin $tmpBranch

    $ref = $tmpBranch
    Write-Host "Using temporary ref: $ref"
  }

  Write-Host "Dispatching deploy-main.yml (ref=$ref, environment=$environment)..."
  gh workflow run deploy-main.yml `
    --ref "$ref" `
    --field "environment=$environment" `
    --field "force_recreate_dev=$forceRecreateDev"

  Write-Host "Waiting for dispatched run id..."
  for ($i = 0; $i -lt 30; $i++) {
    $runId = gh run list `
      --workflow deploy-main.yml `
      --branch "$ref" `
      --event workflow_dispatch `
      --limit 10 `
      --json databaseId,status `
      --jq '.[] | select(.status == "queued" or .status == "in_progress" or .status == "waiting" or .status == "requested" or .status == "pending") | .databaseId' | Select-Object -First 1
    if (-not [string]::IsNullOrWhiteSpace($runId)) {
      break
    }
    Start-Sleep -Seconds 2
  }

  if ([string]::IsNullOrWhiteSpace($runId)) {
    throw "Could not find the dispatched workflow run in time."
  }

  Write-Host "Watching run id=$runId until completion..."
  gh run watch "$runId" --exit-status
  Write-Host "Workflow finished. Cleanup will run automatically."
}
finally {
  if (-not [string]::IsNullOrWhiteSpace($tmpBranch) -and -not [string]::IsNullOrWhiteSpace($repoRoot)) {
    Write-Host "Deleting temporary remote branch: $tmpBranch"
    git -C $repoRoot push origin --delete $tmpBranch | Out-Null
  }

  if (-not [string]::IsNullOrWhiteSpace($tmpWorktree) -and -not [string]::IsNullOrWhiteSpace($repoRoot)) {
    Write-Host "Removing temporary worktree..."
    git -C $repoRoot worktree remove --force $tmpWorktree | Out-Null
  }

  if ($keepRunner) {
    Write-Host "KEEP_RUNNER=true; leaving runner container and registration in place."
    return
  }

  if (-not [string]::IsNullOrWhiteSpace($runnerId)) {
    Write-Host "Unregistering runner id=$runnerId from GitHub..."
    gh api -X DELETE "repos/$repoSlug/actions/runners/$runnerId" | Out-Null
  }

  if ($keepRunnerData) {
    Write-Host "Stopping runner container (keeping volume)..."
    docker compose down
  } else {
    Write-Host "Stopping runner container and removing volume..."
    docker compose down -v
  }
}
