# Test PDF upload and listing
[System.IO.File]::WriteAllBytes('sample.pdf',[byte[]](0x25,0x50,0x44,0x46))
Write-Host 'Uploading sample.pdf'
$respStr = curl.exe -s -X POST -F file=@sample.pdf -F label=e2e-pdf http://localhost:5000/api/admin/files | Out-String
Write-Host 'Upload response:'
Write-Host $respStr
$json = $null
try { $json = $respStr | ConvertFrom-Json -ErrorAction Stop } catch { }
$uploadedName = 'sample.pdf'
if ($json) {
    if ($json.fileName) { $uploadedName = $json.fileName }
    elseif ($json.FileName) { $uploadedName = $json.FileName }
}
Write-Host 'Expecting name:' $uploadedName
$found = $false
for ($i = 0; $i -lt 60 -and -not $found; $i++) {
    try {
        $listing = Invoke-RestMethod -Uri 'http://localhost:5000/api/admin/files?page=1&pageSize=20' -TimeoutSec 5 -ErrorAction Stop
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
