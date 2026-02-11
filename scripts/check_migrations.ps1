#!/usr/bin/env pwsh
<#
Checks whether the ChatMessageFiles table exists and whether the AddMessageAttachments
migration is recorded in __EFMigrationsHistory on the postgres container.
#>
$container = "postgres"
$db = "smartglasses"
$user = "postgres"
$migrationId = "20260210120000_AddMessageAttachments"

Write-Host "Checking for ChatMessageFiles table..."
$tblSql = "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema='public' AND table_name='chatmessagefiles');"
$tblSql | docker exec -i $container psql -U $user -d $db -t -q | ForEach-Object { $_.Trim() }

Write-Host "Checking __EFMigrationsHistory for migration: $migrationId"
$histSql = "SELECT COUNT(*) FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '$migrationId';"
$histSql | docker exec -i $container psql -U $user -d $db -t -q | ForEach-Object { $_.Trim() }

Write-Host "Done."
