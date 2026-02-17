# Upload a text document, wait for processing, and verify PDF + location data
$text = 'This is a test document. It contains multiple sentences for chunking. Location tracking should work properly.'
[System.IO.File]::WriteAllText('location-test.txt',$text)
Write-Host 'Uploading location-test.txt'
$resp = curl.exe -s -X POST -F file=@location-test.txt -F label=location-test http://localhost:5000/api/documents/upload | Out-String
Write-Host 'Upload response:'
Write-Host $resp
$json = $null
try { $json = $resp | ConvertFrom-Json -ErrorAction Stop } catch { }
if (-not $json) { Write-Host 'Upload failed or non-JSON response'; exit 1 }
$documentId = $json.id ? $json.id : ($json.Id ? $json.Id : $null)
if (-not $documentId) { Write-Host 'No document id returned'; exit 1 }
Write-Host 'Document id:' $documentId
$processed = $false
for ($i=0;$i -lt 60 -and -not $processed;$i++) {
    try {
        $doc = Invoke-RestMethod -Uri "http://localhost:5000/api/documents/$documentId" -TimeoutSec 5 -ErrorAction Stop
    } catch {
        Start-Sleep -Seconds 1
        continue
    }
    if ($doc) {
        if (($doc.fileName -or $doc.FileName) -and (($doc.contentType -eq 'application/pdf') -or ($doc.ContentType -eq 'application/pdf'))) {
            if ($doc.isProcessed -or $doc.IsProcessed) {
                $processed = $true
                break
            }
        }
    }
    Start-Sleep -Seconds 1
}
if (-not $processed) { Write-Host 'DOCUMENT_NOT_PROCESSED'; exit 2 }
Write-Host 'DOCUMENT_PROCESSED'
# Print brief doc info
Write-Host 'Filename:' ($doc.fileName ? $doc.fileName : $doc.FileName)
Write-Host 'ContentType:' ($doc.contentType ? $doc.contentType : $doc.ContentType)
Write-Host 'isProcessed:' ($doc.isProcessed ? $doc.isProcessed : $doc.IsProcessed)
if ($doc.chunks -or $doc.Chunks) { Write-Host 'Chunks count:' (($doc.chunks ? $doc.chunks : $doc.Chunks).Count) } else { Write-Host 'No chunks present' }
