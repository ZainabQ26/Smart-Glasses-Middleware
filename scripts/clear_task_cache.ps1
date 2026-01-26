param(
    [string]$TasksDir = "${PSScriptRoot}\..\tasks",
    [switch]$Force,
    [switch]$DryRun
)

# Resolve path
try {
    $tasksPath = Resolve-Path -Path $TasksDir -ErrorAction Stop | Select-Object -ExpandProperty Path
} catch {
    Write-Host "Tasks directory not found: $TasksDir" -ForegroundColor Yellow
    exit 0
}

$compiledDir = Join-Path $tasksPath 'compiled'
if (-not (Test-Path $compiledDir)) {
    Write-Host "No compiled cache directory found at: $compiledDir" -ForegroundColor Green
    exit 0
}

$files = Get-ChildItem -Path $compiledDir -Filter 'plugins_*.dll' -File -ErrorAction SilentlyContinue
if (-not $files -or $files.Count -eq 0) {
    Write-Host "No cached plugin assemblies found in: $compiledDir" -ForegroundColor Green
    exit 0
}

Write-Host "Found $($files.Count) cached plugin files in: $compiledDir`n" -ForegroundColor Cyan
foreach ($f in $files) { Write-Host $f.FullName }

if ($DryRun) {
    Write-Host "Dry run: no files will be deleted." -ForegroundColor Yellow
    exit 0
}

if (-not $Force) {
    $confirm = Read-Host "Delete these files? Type 'yes' to confirm"
    if ($confirm -ne 'yes') {
        Write-Host "Aborted by user." -ForegroundColor Yellow
        exit 0
    }
}

foreach ($f in $files) {
    try {
        Remove-Item -LiteralPath $f.FullName -Force -ErrorAction Stop
        Write-Host "Deleted: $($f.FullName)" -ForegroundColor Green
    } catch {
        Write-Host "Failed to delete $($f.FullName): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host "Cache clear complete." -ForegroundColor Cyan
