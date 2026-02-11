#!/usr/bin/env pwsh
<#
This script removes a manual migration history row and drops the ChatMessageFiles table
so EF Core can re-apply the migration cleanly. It then rebuilds/restarts the app and
runs the migration health check.

WARNING: Dropping the table will delete any data in ChatMessageFiles.
Run only when you understand potential data loss.
#>

$container = "postgres"
$db = "smartglasses"
$user = "postgres"
$migrationId = "20260210120000_AddMessageAttachments"

Write-Host "Removing migration history row and dropping ChatMessageFiles (if present)."

$sql = @"
DELETE FROM "__EFMigrationsHistory" WHERE "MigrationId" = '$migrationId';
DROP TABLE IF EXISTS "ChatMessageFiles";
"@

Write-Host "Executing SQL in container $container..."
try {
    $sql | docker exec -i $container psql -U $user -d $db -v ON_ERROR_STOP=1 -q
    if ($LASTEXITCODE -ne 0) { throw "psql exited with code $LASTEXITCODE" }
}
catch {
    Write-Error "Failed to run SQL against container '$container': $_"
    exit 1
}

Write-Host "SQL executed successfully. Rebuilding and restarting the app (this will re-run migrations)."
.\scripts\start_and_rebuild.ps1

Write-Host "Waiting 5 seconds for services to settle..."
Start-Sleep -Seconds 5

Write-Host "Running migration health check..."
.\scripts\check_migrations.ps1
