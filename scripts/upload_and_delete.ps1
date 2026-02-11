Add-Type -AssemblyName System.Net.Http
$fixturesDir = Join-Path $PSScriptRoot '..\tests\fixtures'
if (-not (Test-Path $fixturesDir)) { New-Item -ItemType Directory -Path $fixturesDir | Out-Null }
$file = Join-Path (Resolve-Path $fixturesDir).Path 'tmp_test_upload.txt'
Set-Content -Path $file -Value 'test file content' -Force
$uri = 'http://localhost:5000/api/admin/files'
$handler = New-Object System.Net.Http.HttpClientHandler
$client = New-Object System.Net.Http.HttpClient($handler)
$content = New-Object System.Net.Http.MultipartFormDataContent
$fs = [System.IO.File]::OpenRead($file)
$streamContent = New-Object System.Net.Http.StreamContent($fs)
$streamContent.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse('application/octet-stream')
$content.Add($streamContent, 'file', [System.IO.Path]::GetFileName($file))
$content.Add((New-Object System.Net.Http.StringContent('test-delete')), 'label')
$resp = $client.PostAsync($uri, $content).Result
$body = $resp.Content.ReadAsStringAsync().Result
Write-Output "Upload response: $body"
try {
    $obj = $body | ConvertFrom-Json
    $id = $obj.id
    Write-Output "Parsed Id: $id"
    $delResp = $client.DeleteAsync("$uri/$id").Result
    Write-Output "Delete status: $($delResp.StatusCode)"
    if ($delResp.Content -ne $null) { Write-Output ($delResp.Content.ReadAsStringAsync().Result) }
} catch {
    Write-Output "Failed to parse JSON or delete: $_"
}
