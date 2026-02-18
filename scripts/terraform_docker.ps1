param(
    [string]$Cmd = "validate",
    [string]$Dir = "aws/infrastructure",
    [string]$TF_VERSION = "1.5.7"
)

Set-StrictMode -Version Latest

$script:RepoRoot = (Resolve-Path "..\" -Relative).TrimEnd('\')
$absDir = Resolve-Path $Dir

Write-Host "Running: terraform $Cmd (via Docker image hashicorp/terraform:$TF_VERSION) in $absDir"

# Ensure docker is available
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Error "Docker command not found. Install Docker Desktop and retry."
    exit 1
}

# Pass AWS env vars through if present
$envArgs = @()
if ($env:AWS_ACCESS_KEY_ID) { $envArgs += "-e AWS_ACCESS_KEY_ID=$env:AWS_ACCESS_KEY_ID" }
if ($env:AWS_SECRET_ACCESS_KEY) { $envArgs += "-e AWS_SECRET_ACCESS_KEY=$env:AWS_SECRET_ACCESS_KEY" }
if ($env:AWS_REGION) { $envArgs += "-e AWS_REGION=$env:AWS_REGION" }

# Run terraform inside container mounting the infra folder
# Use /bin/sh -c to ensure multi-argument commands are parsed correctly
$cmdEscaped = $Cmd.Replace('"', '\"')
docker run --rm --entrypoint /bin/sh -v "${absDir}:/work" -w /work $($envArgs -join ' ') hashicorp/terraform:$TF_VERSION -c "terraform $cmdEscaped -input=false"
