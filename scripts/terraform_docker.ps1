# Run Terraform commands via the official Docker image.
# Usage: .\terraform_docker.ps1 -Cmd "<terraform args>" -Dir "<relative dir from repo root>"
# Example: .\terraform_docker.ps1 -Cmd "validate" -Dir "aws/infrastructure"

param(
    [Parameter(Mandatory=$true)][string]$Cmd,
    [string]$Dir = "aws/infrastructure"
)

$RepoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$WorkDir  = "/workspace/$Dir"

docker run --rm `
    -v "${RepoRoot}:/workspace" `
    -w "$WorkDir" `
    -e AWS_ACCESS_KEY_ID=$env:AWS_ACCESS_KEY_ID `
    -e AWS_SECRET_ACCESS_KEY=$env:AWS_SECRET_ACCESS_KEY `
    -e AWS_REGION=$env:AWS_REGION `
    hashicorp/terraform:1.5.7 `
    sh -c "terraform $Cmd"
