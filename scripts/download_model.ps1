param(
  [string]$Model = "Xenova/whisper-tiny.en",
  [string]$Dest = "src/SmartGlassesMiddleware/wwwroot/models/Xenova/whisper-tiny.en"
)

Write-Host "Downloading model $Model -> $Dest"

$tmpRoot = $env:RUNNER_TEMP
if ([string]::IsNullOrWhiteSpace($tmpRoot)) {
    $tmpRoot = $env:TEMP
}
if ([string]::IsNullOrWhiteSpace($tmpRoot)) {
    $tmpRoot = [System.IO.Path]::GetTempPath()
}
if ([string]::IsNullOrWhiteSpace($tmpRoot)) {
    throw "Unable to resolve a temporary directory (RUNNER_TEMP/TEMP/GetTempPath all empty)."
}

$tmp = Join-Path $tmpRoot ([System.Guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

try {
    # Try git + git-lfs path
    $git = (Get-Command git -ErrorAction SilentlyContinue)
    $gfls = (Get-Command git-lfs -ErrorAction SilentlyContinue)

    if ($git) {
        Write-Host "Cloning via git..."
        git clone "https://huggingface.co/$Model" $tmp
        if ($gfls) {
            Write-Host "Running git lfs pull to fetch large files..."
            Push-Location $tmp
            git lfs pull --include "*" || Write-Host "git lfs pull failed or no lfs files"
            Pop-Location
        } else {
            Write-Host "git-lfs not found; large files may be LFS pointers. Install Git LFS if needed."
        }

        Write-Host "Copying files to $Dest"
        New-Item -ItemType Directory -Path $Dest -Force | Out-Null
        Copy-Item -Path (Join-Path $tmp "*") -Destination $Dest -Recurse -Force
        Write-Host "Model repo cloned to $Dest"
        exit 0
    }

    # Fallback: try huggingface hub `hf` CLI
    $hf = (Get-Command hf -ErrorAction SilentlyContinue)
    if ($hf) {
        Write-Host "Using 'hf' CLI to download repo..."
        hf repo clone $Model $tmp
        Write-Host "Copying files to $Dest"
        New-Item -ItemType Directory -Path $Dest -Force | Out-Null
        Copy-Item -Path (Join-Path $tmp "*") -Destination $Dest -Recurse -Force
        Write-Host "Model downloaded to $Dest"
        exit 0
    }

    Write-Host "Neither 'git' nor 'hf' CLI found."
    Write-Host "Manual steps:"
    Write-Host "  1) Install Git and Git LFS: https://git-lfs.com/"
    Write-Host "  2) git clone https://huggingface.co/$Model"
    Write-Host "  3) git lfs pull"
    Write-Host "  4) Copy the cloned folder into $Dest"
    exit 1
} finally {
    if (Test-Path $tmp) { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
}
