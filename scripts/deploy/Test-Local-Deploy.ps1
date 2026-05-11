<#
.SYNOPSIS
    Phase 30A - local deployment test runner. Publishes WMS.Web to a
    folder, applies migrations, and (optionally) launches it under
    Kestrel for smoke testing.

.DESCRIPTION
    Goal: validate the full deploy chain end-to-end on the developer
    workstation BEFORE provisioning a real server. Catches missing
    env vars, broken migrations, file-permission issues, and config
    drift cheaper than a remote rollback.

    NOT a production deployment script - produces a self-contained
    artifact under -PublishPath but does NOT configure IIS, TLS, or
    Windows services. That's Phase 30B territory.

.PARAMETER PublishPath
    Where to publish. Default: .\publish-local relative to repo root.

.PARAMETER Port
    Kestrel listen port. Default 5500 (avoids the 5000/5001 conflict
    with `dotnet run`).

.PARAMETER Environment
    ASPNETCORE_ENVIRONMENT value. Default Production so we exercise
    the production config path (ConfigurationValidator strict mode,
    security headers, error pages, file-sink Serilog).

.PARAMETER SkipMigrate
    Skip the migration step. Useful when iterating on UI and the
    schema is unchanged.

.PARAMETER SkipBuild
    Skip publish. Useful when rerunning against an existing artifact.

.PARAMETER LaunchBrowser
    Open http://localhost:{Port} after Kestrel starts.

.PARAMETER NoStart
    Build + migrate only; don't start Kestrel. Pair with manual
    `dotnet WMS.Web.dll` for ad-hoc debugging.

.EXAMPLE
    .\scripts\deploy\Test-Local-Deploy.ps1
    Full chain - publish, migrate Master, start Kestrel on :5500.

.EXAMPLE
    .\scripts\deploy\Test-Local-Deploy.ps1 -SkipBuild -SkipMigrate -LaunchBrowser
    Re-run an existing publish, skip migrations, open browser.

.NOTES
    Requires env vars:
      ConnectionStrings__MasterDb
      ConnectionStrings__TenantTemplate

    If unset, the script prompts and stashes them in the current
    PowerShell session (not persisted to the user environment).
#>

[CmdletBinding()]
param(
    [string]$PublishPath = "publish-local",
    [int]$Port = 5500,
    [string]$Environment = "Production",
    [switch]$SkipMigrate,
    [switch]$SkipBuild,
    [switch]$LaunchBrowser,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..\..").Path

function Write-Step($msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Write-Info($msg) {
    Write-Host "    $msg" -ForegroundColor Gray
}

function Write-Ok($msg) {
    Write-Host "    [ok] $msg" -ForegroundColor Green
}

function Write-Fail($msg) {
    Write-Host "    [fail] $msg" -ForegroundColor Red
}

# -- 1. Validate env vars --------------------------------------------
Write-Step "Validating environment"
Write-Info "Environment = $Environment"
Write-Info "Port        = $Port"
Write-Info "PublishPath = $PublishPath"

if (-not $env:ConnectionStrings__MasterDb) {
    Write-Warning "ConnectionStrings__MasterDb not set."
    $masterDb = Read-Host "Enter MasterDb connection string (or Ctrl+C to abort)"
    if ([string]::IsNullOrWhiteSpace($masterDb)) {
        Write-Fail "MasterDb required."; exit 1
    }
    $env:ConnectionStrings__MasterDb = $masterDb
}

if (-not $env:ConnectionStrings__TenantTemplate) {
    Write-Warning "ConnectionStrings__TenantTemplate not set."
    Write-Info "Use {0} as placeholder for the tenant DB name."
    $tenantTpl = Read-Host "Enter TenantTemplate connection string"
    if ([string]::IsNullOrWhiteSpace($tenantTpl)) {
        Write-Fail "TenantTemplate required."; exit 1
    }
    $env:ConnectionStrings__TenantTemplate = $tenantTpl
}
Write-Ok "Connection strings present"

# -- 2. Publish ------------------------------------------------------
$resolvedPublish = Join-Path $repoRoot $PublishPath
if ($SkipBuild) {
    Write-Step "Skipping publish (per -SkipBuild)"
    if (-not (Test-Path $resolvedPublish)) {
        Write-Fail "No existing publish at $resolvedPublish. Remove -SkipBuild."
        exit 1
    }
} else {
    Write-Step "Publishing WMS.Web -> $resolvedPublish"
    if (Test-Path $resolvedPublish) {
        Write-Info "Clearing prior publish output"
        Remove-Item -Recurse -Force $resolvedPublish
    }
    & dotnet publish "$repoRoot\src\WMS.Web\WMS.Web.csproj" `
        --configuration Release `
        --output $resolvedPublish `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "dotnet publish failed (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }
    Write-Ok "Published"
}

# -- 3. Migrate ------------------------------------------------------
if ($SkipMigrate) {
    Write-Step "Skipping migration (per -SkipMigrate)"
} else {
    Write-Step "Applying Master migrations"
    & dotnet run --project "$repoRoot\tools\WMS.Migrate\WMS.Migrate.csproj" `
        --configuration Release `
        --nologo -- up master
    if ($LASTEXITCODE -ne 0) {
        Write-Fail "Master migration failed (exit $LASTEXITCODE)"
        Write-Info "Check MasterDb connection + WMS_Master DB exists."
        exit $LASTEXITCODE
    }
    Write-Ok "Master migrations applied"

    Write-Step "Applying Tenant migrations (fan-out across active tenants)"
    & dotnet run --project "$repoRoot\tools\WMS.Migrate\WMS.Migrate.csproj" `
        --configuration Release `
        --nologo -- up tenants
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Tenant fan-out exited $LASTEXITCODE - may be empty (no tenants yet) or partial failure."
        Write-Info "Review output above; continue with Y to proceed anyway."
        $continue = Read-Host "Continue? [y/N]"
        if ($continue -notin @("y", "Y")) { exit 1 }
    } else {
        Write-Ok "Tenant migrations applied"
    }
}

# -- 4. Health probe (config validation only - pre-launch) -----------
Write-Step "Smoke-testing publish artifact"
$entryDll = Join-Path $resolvedPublish "WMS.Web.dll"
if (-not (Test-Path $entryDll)) {
    Write-Fail "WMS.Web.dll missing from publish output at $entryDll"
    exit 1
}
Write-Ok "WMS.Web.dll present"

# Verify embedded email templates landed in the publish bundle.
$bllDll = Join-Path $resolvedPublish "WMS.BLL.dll"
if (Test-Path $bllDll) {
    Write-Ok "WMS.BLL.dll present (email templates embedded)"
} else {
    Write-Fail "WMS.BLL.dll missing - publish output looks broken"
    exit 1
}

# -- 5. Launch Kestrel -----------------------------------------------
if ($NoStart) {
    Write-Step "Build + migrate complete (per -NoStart)"
    Write-Info "To launch manually:"
    Write-Info "  cd $resolvedPublish"
    Write-Info "  `$env:ASPNETCORE_URLS = 'http://localhost:$Port'"
    Write-Info "  `$env:ASPNETCORE_ENVIRONMENT = '$Environment'"
    Write-Info "  dotnet WMS.Web.dll"
    exit 0
}

Write-Step "Launching Kestrel on http://localhost:$Port"
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ASPNETCORE_ENVIRONMENT = $Environment

if ($LaunchBrowser) {
    Start-Sleep -Seconds 3
    Start-Process "http://localhost:$Port"
}

Write-Info "Press Ctrl+C to stop. Logs stream below."
Write-Info "Smoke runner: scripts\smoke\Smoke-Local.ps1 -BaseUrl http://localhost:$Port"
Write-Host ""

Set-Location $resolvedPublish
& dotnet WMS.Web.dll
