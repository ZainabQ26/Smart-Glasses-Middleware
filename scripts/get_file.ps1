$guid = '0ded4c1e-c096-465d-92ed-7c313c15118b'
$res = Invoke-RestMethod -Uri "http://localhost:5000/api/admin/files?page=1&pageSize=100"
$file = $res.Data | Where-Object { $_.id -eq $guid }
$file | ConvertTo-Json -Depth 6
