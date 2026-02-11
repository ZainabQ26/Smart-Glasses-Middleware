Param(
    [ValidateSet('rag-block','group-only','message-block','cleanup')]
    [string]$Scenario = 'rag-block',
    [string]$ApiBase = 'http://localhost:5000/api/admin'
)

# Conversations endpoints live under /api/conversations (not admin)
$ConversationsBase = 'http://localhost:5000/api/conversations'

# Fixtures directory for temp files
$fixturesDir = Join-Path $PSScriptRoot '..\tests\fixtures'
if (-not (Test-Path $fixturesDir)) { New-Item -ItemType Directory -Path $fixturesDir | Out-Null }
$fixturesPath = (Resolve-Path $fixturesDir).Path

function Invoke-Api {
    param(
        [string]$Method = 'GET',
        [string]$Url,
        [string]$Body = $null,
        [bool]$IsForm = $false,
        [hashtable]$FormFields = $null
    )

    if ($IsForm -and $null -ne $FormFields) {
        # Build curl form args
        $formArgs = ($FormFields.GetEnumerator() | ForEach-Object {
            if ($_.Value -is [System.IO.FileInfo]) { '-F "' + $_.Key + '=@' + ($_.Value).FullName + '"' } else { '-F "' + $_.Key + '=' + $_.Value + '"' }
        }) -join ' '
        $cmd = "curl -s -i $formArgs '$Url'"
        $raw = Invoke-Expression $cmd
    }
    elseif ($null -ne $Body) {
        $cmd = "curl -s -i -X $Method -H 'Content-Type: application/json' -d '$Body' '$Url'"
        $raw = Invoke-Expression $cmd
    }
    else {
        $cmd = "curl -s -i -X $Method '$Url'"
        $raw = Invoke-Expression $cmd
    }

    # Split headers and body
    $parts = $raw -split "\r?\n\r?\n",2
    $headerText = $parts[0]
    $bodyText = if ($parts.Length -gt 1) { $parts[1].Trim() } else { '' }

    # Parse headers into dictionary
    $headers = @{}
    $headerText -split "\r?\n" | ForEach-Object {
        if ($_ -match '^(?<k>[^:]+):\s*(?<v>.*)$') { $headers[$matches.k.Trim()] = $matches.v.Trim() }
        elseif ($_ -match '^HTTP') { $headers['StatusLine'] = $_ }
    }

    $parsed = $null
    if ($bodyText -ne '') {
        try { $parsed = $bodyText | ConvertFrom-Json -ErrorAction Stop } catch { $parsed = $null }
    }

    # Sometimes the raw output contains headers and JSON together without a clean separator
    if ($null -eq $parsed -or '' -eq $parsed) {
        $jsonMatch = [regex]::Match($raw, '\{[\s\S]*\}$')
        if ($jsonMatch.Success) {
            $bodyJson = $jsonMatch.Value
            try { $parsed = $bodyJson | ConvertFrom-Json -ErrorAction Stop } catch { $parsed = $null }
            if ($null -ne $parsed) { $bodyText = $bodyJson }
        }
    }

    return @{ Raw = $raw; Headers = $headers; Body = $bodyText; Parsed = $parsed }
}

function Invoke-UploadFile {
    param($Path, $Label)
    $fileFull = Resolve-Path $Path
    $form = @{ file = (Get-Item $fileFull); label = $Label }
    $resp = Invoke-Api -Method POST -Url "$ApiBase/files" -IsForm:$true -FormFields $form
    if ($null -ne $resp.Parsed) { return $resp.Parsed }
    else { return @{ raw = $resp.Raw } }
}

function Invoke-CreateGroup {
    param($Name, $Description)
    $body = @{ name = $Name; description = $Description } | ConvertTo-Json
    $resp = Invoke-Api -Method POST -Url "$ApiBase/groups" -Body $body
    if ($null -ne $resp.Parsed) { return $resp.Parsed }
    return @{ Raw = $resp.Raw; Headers = $resp.Headers }
}

function Invoke-AddFileToGroup {
    param($GroupId, $FileId)
    $resp = Invoke-Api -Method POST -Url "$ApiBase/groups/$GroupId/files/$FileId"
    if ($null -ne $resp.Parsed) { return $resp.Parsed }
    return @{ Raw = $resp.Raw; Headers = $resp.Headers }
}

function Invoke-CreateConversation {
    param($Title)
    $body = @{ title = $Title } | ConvertTo-Json
    $resp = Invoke-Api -Method POST -Url "$ConversationsBase" -Body $body
    if ($null -ne $resp.Parsed) { return $resp.Parsed }
    # Try Location header
    if ($resp.Headers.ContainsKey('Location')) {
        $loc = $resp.Headers['Location']
        $id = $loc.Split('/')[-1]
        return @{ id = $id }
    }
    return @{ Raw = $resp.Raw }
}

function Invoke-PostMessageToConversation {
    param($ConversationId, $Role, $Content)
    $body = @{ role = $Role; content = $Content } | ConvertTo-Json
    $resp = Invoke-Api -Method POST -Url "$ConversationsBase/$ConversationId/messages" -Body $body
    if ($null -ne $resp.Parsed) { return $resp.Parsed }
    if ($resp.Headers.ContainsKey('Location')) {
        $loc = $resp.Headers['Location']
        $id = $loc.Split('/')[-1]
        return @{ id = $id }
    }
    return @{ Raw = $resp.Raw }
}

function Invoke-AttachFileToMessage {
    param($ConversationId, $MessageId, $FileId)
    # ConversationsController exposes POST {id}/files/{fileId} to attach to conversation
    $resp = Invoke-Api -Method POST -Url "$ConversationsBase/$ConversationId/files/$FileId"
    if ($null -ne $resp.Parsed) { return $resp.Parsed }
    return @{ Raw = $resp.Raw }
}

function Invoke-DeleteFileApi {
    param($FileId)
    $resp = curl -i -s -X DELETE "$ApiBase/files/$FileId"
    return $resp
}

function Remove-RagEntriesSql {
    param($FileId)
    $sql = "DELETE FROM ""RagEntries"" WHERE ""FileId"" = '$FileId';"
    @"
$sql
"@ | docker exec -i postgres psql -U postgres -d smartglasses -q
}

function Remove-ChatMessageFileRowsSql {
    param($FileId)
    $sql = "DELETE FROM ""ChatMessageFiles"" WHERE ""ManagedFileId"" = '$FileId';"
    @"
$sql
"@ | docker exec -i postgres psql -U postgres -d smartglasses -q
}

function Invoke-Scenario-RagBlock {
    Write-Output "Scenario: RAG-block (upload -> attempt delete -> expect conflict)"
    $tmp = Join-Path $fixturesPath 'tmp_scenario_rag.txt'
    Set-Content -Path $tmp -Value "rag scenario $(Get-Random)" -Force
    $up = Invoke-UploadFile -Path $tmp -Label 'scenario-rag'
    Write-Output "Uploaded: $($up.id)"
    Start-Sleep -Milliseconds 400
    $del = Invoke-DeleteFileApi -FileId $up.id
    Write-Output "Delete response:\n$del"
    return $up.id
}

function Invoke-Scenario-GroupOnly {
    Write-Output "Scenario: Group-only (upload -> create group -> attach -> delete -> group mappings removed then deleted)"
    $tmp = Join-Path $fixturesPath 'tmp_scenario_group.txt'
    Set-Content -Path $tmp -Value "group scenario $(Get-Random)" -Force
    $up = Invoke-UploadFile -Path $tmp -Label 'scenario-group'
    Write-Output "Uploaded: $($up.id)"
    $g = Invoke-CreateGroup -Name "scenario-group-$(Get-Random)" -Description 'scenario'
    Write-Output "Group created: $($g.id)"
    Invoke-AddFileToGroup -GroupId $g.id -FileId $up.id | Out-Null
    Start-Sleep -Milliseconds 200
    $del = Invoke-DeleteFileApi -FileId $up.id
    Write-Output "Delete response:\n$del"
    return @{ FileId = $up.id; GroupId = $g.id }
}

function Invoke-Scenario-MessageBlock {
    Write-Output "Scenario: Message-attachment block (upload -> create conv -> post message -> attach file to message -> delete should conflict)"
    $tmp = Join-Path $fixturesPath 'tmp_scenario_msg.txt'
    Set-Content -Path $tmp -Value "message scenario $(Get-Random)" -Force
    $up = Invoke-UploadFile -Path $tmp -Label 'scenario-message'
    Write-Output "Uploaded: $($up.id)"
    $conv = Invoke-CreateConversation -Title "scenario-conv-$(Get-Random)"
    Write-Output "Conversation created: $($conv.id)"
    $msg = Invoke-PostMessageToConversation -ConversationId $conv.id -Role 'user' -Content 'test message'
    Write-Output "Message posted: $($msg.id)"
    # Attach file to conversation (makes it referenced by messages)
    Invoke-AttachFileToMessage -ConversationId $conv.id -MessageId $msg.id -FileId $up.id | Out-Null
    Start-Sleep -Milliseconds 200
    $del = Invoke-DeleteFileApi -FileId $up.id
    Write-Output "Delete response:\n$del"
    return @{ FileId = $up.id; ConversationId = $conv.id; MessageId = $msg.id }
}

switch ($Scenario) {
    'rag-block' { Invoke-Scenario-RagBlock }
    'group-only' { Invoke-Scenario-GroupOnly }
    'message-block' { Invoke-Scenario-MessageBlock }
    'cleanup' {
        Write-Output "Cleanup mode: remove RagEntries and ChatMessageFiles for all tmp_scenario_* uploads"
        # This finds any ManagedFiles with label starting with 'scenario-' and removes refs
        $files = curl -s "$ApiBase/files" | ConvertFrom-Json
        foreach ($f in $files.Data) {
            if ($f.Label -like 'scenario-*') {
                Write-Output "Cleaning file: $($f.Id)"
                Remove-RagEntriesSql -FileId $f.Id | Out-Null
                Remove-ChatMessageFileRowsSql -FileId $f.Id | Out-Null
                Write-Output "Removed DB refs for $($f.Id)"
            }
        }
    }
    default { Write-Output "Unknown scenario: $Scenario" }
}
