# E2E test: create conversation and post message
$base = 'http://localhost:5000'
# Create conversation
$conv = Invoke-RestMethod -Uri "$base/api/conversations" -Method Post -Body (@{ title = 'E2E Test Conversation' } | ConvertTo-Json) -ContentType 'application/json'
Write-Host "Created conversation: $($conv.id)"
# Post message
$body = @{ role = 'user'; content = 'Hello from E2E test' } | ConvertTo-Json
$msg = Invoke-RestMethod -Uri "$base/api/conversations/$($conv.id)/messages" -Method Post -Body $body -ContentType 'application/json'
Write-Host "Posted message: $($msg.id)"
# Fetch conversation
$full = Invoke-RestMethod -Uri "$base/api/conversations/$($conv.id)" -Method Get
Write-Host "Conversation has $($full.Messages.Count) messages"
Write-Host ($full | ConvertTo-Json -Compress)
