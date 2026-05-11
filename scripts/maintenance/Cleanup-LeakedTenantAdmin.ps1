<#
.SYNOPSIS
    Post-fix cleanup: remove the leaked dev-seed admin user from
    tenant DBs that were created BEFORE the P0 #1 / #5 fix.

.DESCRIPTION
    Phase 30A smoke surfaced that Migration_20260504_041_SeedAdminUser
    (a Tenant-tagged migration) seeded nwuthipongworachoke@gmail.com +
    BCrypt(ChangeMe!2026) into every tenant DB the Phase 26 fan-out
    coordinator touched. The fix gates that migration on
    DB_NAME() = 'WMS_Tenant_Template' so new tenants get a clean
    security.Users - but existing tenants (e.g. anything provisioned
    via SuperAdmin between v2.13.0 and v2.16.1) still have the
    leaked row.

    This script:
      1. Lists every WMS_Tenant_* database (excludes WMS_Tenant_Template)
      2. For each, looks for the seeded admin (nwuthipongworachoke
         @gmail.com) AND another active ADMIN user
      3. If both exist, prompts before deleting the leaked one
      4. Cascades cleanly: UserRoles -> Users (FK_UserRoles_Users
         ON DELETE CASCADE). AuditLog FK is ON DELETE NO ACTION so
         we null UserId on the leaked admin's audit rows first

    Safe to re-run - idempotent. If only ONE admin exists, the
    script refuses to delete (defensive - won't strand the tenant).

.PARAMETER Server
    SQL Server instance. Default: localhost.

.PARAMETER WhatIf
    Show what would be deleted without doing it.

.EXAMPLE
    .\scripts\maintenance\Cleanup-LeakedTenantAdmin.ps1 -WhatIf
    Dry run on local instance.

.EXAMPLE
    .\scripts\maintenance\Cleanup-LeakedTenantAdmin.ps1
    Live run. Prompts before each deletion.
#>

[CmdletBinding()]
param(
    [string]$Server = "localhost",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$LeakedEmail = "nwuthipongworachoke@gmail.com"
$TemplateDb = "WMS_Tenant_Template"

function Query($db, $sql, $params = @{}) {
    $cn = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=$db;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5"
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($k in $params.Keys) {
            $null = $cmd.Parameters.AddWithValue("@$k", $params[$k])
        }
        $r = $cmd.ExecuteReader()
        $rows = @()
        while ($r.Read()) {
            $row = [ordered]@{}
            for ($i = 0; $i -lt $r.FieldCount; $i++) {
                $row[$r.GetName($i)] = $r.GetValue($i)
            }
            $rows += [PSCustomObject]$row
        }
        $r.Close()
        return $rows
    } finally {
        $cn.Close()
    }
}

function Execute($db, $sql, $params = @{}) {
    $cn = New-Object System.Data.SqlClient.SqlConnection "Server=$Server;Database=$db;Integrated Security=True;TrustServerCertificate=True;Connect Timeout=5"
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand()
        $cmd.CommandText = $sql
        foreach ($k in $params.Keys) {
            $null = $cmd.Parameters.AddWithValue("@$k", $params[$k])
        }
        return $cmd.ExecuteNonQuery()
    } finally {
        $cn.Close()
    }
}

Write-Host ""
Write-Host "Cleanup-LeakedTenantAdmin" -ForegroundColor Cyan
Write-Host "  Server:       $Server"
Write-Host "  Leaked email: $LeakedEmail"
Write-Host "  Template DB:  $TemplateDb (excluded - keeps the dev seed)"
Write-Host "  Mode:         $(if ($WhatIf) { 'WhatIf (dry run)' } else { 'LIVE' })"
Write-Host ""

# Step 1 - enumerate tenant DBs
$dbs = Query "master" "SELECT name FROM sys.databases WHERE name LIKE 'WMS_Tenant_%' AND name <> @template ORDER BY name" @{ template = $TemplateDb }

if ($dbs.Count -eq 0) {
    Write-Host "No customer tenant DBs found (only $TemplateDb)." -ForegroundColor Yellow
    exit 0
}

Write-Host "Tenant DBs to check:"
$dbs | ForEach-Object { Write-Host "  $($_.name)" }
Write-Host ""

# Step 2 - per-tenant check + cleanup
foreach ($dbRow in $dbs) {
    $db = $dbRow.name
    Write-Host "=== $db ===" -ForegroundColor Cyan

    $admins = Query $db @"
SELECT u.Id, u.Email, u.FullName, u.IsActive, u.CreatedAt
FROM security.Users u
JOIN security.UserRoles ur ON ur.UserId = u.Id
JOIN security.Roles r ON r.Id = ur.RoleId
WHERE r.Code = 'ADMIN'
ORDER BY u.CreatedAt
"@

    if ($admins.Count -eq 0) {
        Write-Host "  No ADMIN users found - skipping." -ForegroundColor Yellow
        continue
    }

    $leaked = $admins | Where-Object { $_.Email -eq $LeakedEmail }
    $others = $admins | Where-Object { $_.Email -ne $LeakedEmail }

    if (-not $leaked) {
        Write-Host "  Clean - no leaked admin present." -ForegroundColor Green
        continue
    }

    if ($others.Count -eq 0) {
        Write-Host "  REFUSING: leaked admin is the ONLY ADMIN in this tenant." -ForegroundColor Red
        Write-Host "  This would strand the tenant. Skipping." -ForegroundColor Red
        continue
    }

    Write-Host "  Leaked admin: $LeakedEmail (Id=$($leaked.Id))"
    Write-Host "  Other admin(s):"
    $others | ForEach-Object { Write-Host "    $($_.Email)  (Id=$($_.Id), Active=$($_.IsActive))" }

    if ($WhatIf) {
        Write-Host "  WhatIf: would delete $LeakedEmail + cascade UserRoles + null AuditLog FK" -ForegroundColor Yellow
        continue
    }

    $confirm = Read-Host "  Delete leaked admin from $db? [y/N]"
    if ($confirm -notin @("y", "Y")) {
        Write-Host "  Skipped." -ForegroundColor Yellow
        continue
    }

    # Null AuditLog UserId first (NO ACTION FK), then delete (UserRoles
    # cascades via FK_UserRoles_Users ON DELETE CASCADE).
    $auditRows = Execute $db @"
UPDATE security.AuditLog
SET UserId = NULL
WHERE UserId = (SELECT Id FROM security.Users WHERE Email = @email)
"@ @{ email = $LeakedEmail }

    $userRows = Execute $db @"
DELETE FROM security.Users WHERE Email = @email
"@ @{ email = $LeakedEmail }

    Write-Host "  Cleaned: nulled $auditRows AuditLog FKs, deleted $userRows user row." -ForegroundColor Green
}

Write-Host ""
Write-Host "Done." -ForegroundColor Cyan
