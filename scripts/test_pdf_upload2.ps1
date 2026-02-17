# Upload a slightly larger PDF to avoid deduplication
$bytes = 0..255 | ForEach-Object { [byte]($_ % 256) }
[System.IO.File]::WriteAllBytes('sample2.pdf',$bytes)
Write-Host 'Uploading sample2.pdf'
$respStr = curl.exe -s -X POST -F file=@sample2.pdf -F label=e2e-pdf http://localhost:5000/api/admin/files | Out-String
Write-Host 'Upload response:'
Write-Host $respStr
$json = $null
try { $json = $respStr | ConvertFrom-Json -ErrorAction Stop } catch { }
$uploadedName = 'sample2.pdf'
if ($json) {
    if ($json.fileName) { $uploadedName = $json.fileName }
    elseif ($json.FileName) { $uploadedName = $json.FileName }
}
Write-Host 'Expecting name:' $uploadedName
$found = $false
for ($i = 0; $i -lt 60 -and -not $found; $i++) {
    try {
        $listing = Invoke-RestMethod -Uri 'http://localhost:5000/api/admin/files?page=1&pageSize=100' -TimeoutSec 5 -ErrorAction Stop
    } catch {
        Start-Sleep -Seconds 1
        continue
    }
    $data = $null
    if ($listing -and $listing.data) { $data = $listing.data } elseif ($listing -and $listing.Data) { $data = $listing.Data }
    if ($data) {
        foreach ($f in $data) {
            if ($f.fileName -eq $uploadedName -or $f.FileName -eq $uploadedName -or ($f.fileName -and $f.fileName -like ($uploadedName -replace '\.', '*'))) {
                $found = $true
                break
            }
        }
    }
    if (-not $found) { Start-Sleep -Seconds 1 }
}
if ($found) { Write-Host 'FILE_FOUND' } else { Write-Host 'FILE_NOT_FOUND' }
