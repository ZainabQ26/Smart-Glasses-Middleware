$tf = 'test_e2e.txt'
'e2e test file' | Out-File -Encoding utf8 $tf

try {
    $upload = Invoke-RestMethod -Uri 'http://localhost:5000/api/documents/upload' -Method Post -Form @{ file = Get-Item $tf }
} catch {
    Write-Output "UPLOAD FAILED: $_"
    exit 1
}

Write-Output 'UPLOAD_RESPONSE:'
$upload | ConvertTo-Json -Compress

$id = $upload.id
if (-not $id) { $id = $upload.Id }
Write-Output "UPLOAD_ID:$id"

$payload = @{ Query = 'E2E test prompt'; TopK = 5; DocumentIds = @($id) } | ConvertTo-Json -Compress
Write-Output 'SENDING QUERY...'

try {
    $qr = Invoke-RestMethod -Uri 'http://localhost:5000/api/documents/query' -Method Post -Body $payload -ContentType 'application/json'
} catch {
    Write-Output "QUERY FAILED: $_"
    exit 2
}

Write-Output 'QUERY_RESPONSE:'
$qr | ConvertTo-Json -Depth 5 -Compress

$convId = $qr.conversationId
if (-not $convId) { $convId = $qr.ConversationId }
Write-Output "CONVERSATION_ID:$convId"

Start-Sleep -Seconds 1

try {
    $conv = Invoke-RestMethod -Uri ("http://localhost:5000/api/conversations/$convId") -Method Get
} catch {
    Write-Output "FETCH CONVERSATION FAILED: $_"
    exit 3
}

Write-Output 'CONVERSATION:'
$conv | ConvertTo-Json -Depth 5
