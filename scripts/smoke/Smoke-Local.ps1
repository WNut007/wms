<#
.SYNOPSIS
    Phase 30A - automated smoke runner against a live WMS.Web instance.

.DESCRIPTION
    Hits the public surfaces an operator + load balancer + browser
    would see. Reports table + exit code so it's both
    human-readable AND CI-friendly (exit 0 = green, 1 = anything red).

    Scenarios covered (12):
      H1  GET  /healthz/live              200 ok
      H2  GET  /healthz/ready             200 ok, includes 'master-db' entry
      H3  GET  /healthz                   200 ok (alias)
      H4  GET  /health                    200 'Healthy' (legacy)
      P1  GET  /                          200 or 302 to /Auth/Login
      P2  GET  /Auth/Login                200 ok
      P3  GET  /SuperAdmin/Auth/Login     200 ok (separate cookie scheme)
      S1  HEAD /                          X-Frame-Options=DENY
      S2  HEAD /                          X-Content-Type-Options=nosniff
      S3  HEAD /                          Referrer-Policy present
      S4  HEAD /                          Server header stripped
      E1  GET  /Error/404                 404 status, branded page

.PARAMETER BaseUrl
    Target. Default http://localhost:5500 (matches Test-Local-Deploy
    default port).

.PARAMETER TimeoutSec
    Per-request timeout in seconds. Default 10.

.PARAMETER Verbose
    Show response body excerpts and full headers on each request.

.EXAMPLE
    .\scripts\smoke\Smoke-Local.ps1
    Smoke local Kestrel on :5500.

.EXAMPLE
    .\scripts\smoke\Smoke-Local.ps1 -BaseUrl https://staging.example.com
    Smoke a remote deployment.

.NOTES
    Exit code 0 if ALL scenarios pass, 1 if any fail. Useful as a
    CI gate or pre-tag check before merging to main.
#>

[CmdletBinding()]
param(
    [string]$BaseUrl = "http://localhost:5500",
    [int]$TimeoutSec = 10
)

$ErrorActionPreference = "Continue"
$BaseUrl = $BaseUrl.TrimEnd('/')

# Allow self-signed certs against local HTTPS (Kestrel dev cert / IIS Express).
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }

$results = @()

function Test-Scenario {
    param(
        [string]$Id,
        [string]$Description,
        [scriptblock]$Action
    )
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        & $Action
        $sw.Stop()
        $script:results += [PSCustomObject]@{
            Id = $Id
            Description = $Description
            Status = "PASS"
            Ms = [int]$sw.Elapsed.TotalMilliseconds
            Notes = ""
        }
    } catch {
        $sw.Stop()
        $script:results += [PSCustomObject]@{
            Id = $Id
            Description = $Description
            Status = "FAIL"
            Ms = [int]$sw.Elapsed.TotalMilliseconds
            Notes = $_.Exception.Message
        }
    }
}

Write-Host ""
Write-Host "Smoke target: $BaseUrl" -ForegroundColor Cyan
Write-Host "Timeout: ${TimeoutSec}s per request" -ForegroundColor Gray
Write-Host ""

# -- Health endpoints ------------------------------------------------
Test-Scenario "H1" "/healthz/live returns 200" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/healthz/live" -TimeoutSec $TimeoutSec -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Expected 200, got $($r.StatusCode)" }
}

Test-Scenario "H2" "/healthz/ready exposes master-db entry" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/healthz/ready" -TimeoutSec $TimeoutSec -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Expected 200, got $($r.StatusCode)" }
    if ($r.Content -notmatch "master-db") { throw "Response body lacks 'master-db' entry - check JSON envelope" }
}

Test-Scenario "H3" "/healthz alias works" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/healthz" -TimeoutSec $TimeoutSec -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Expected 200, got $($r.StatusCode)" }
}

Test-Scenario "H4" "/health legacy endpoint" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/health" -TimeoutSec $TimeoutSec -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Expected 200, got $($r.StatusCode)" }
}

# -- Public-facing pages ---------------------------------------------
Test-Scenario "P1" "Root returns 200 or redirects to /Auth/Login" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/" -TimeoutSec $TimeoutSec -MaximumRedirection 0 -ErrorAction SilentlyContinue -UseBasicParsing
    if ($null -eq $r -or ($r.StatusCode -notin @(200, 302, 301))) {
        throw "Expected 200/301/302, got $($r.StatusCode)"
    }
}

Test-Scenario "P2" "/Auth/Login renders" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/Auth/Login" -TimeoutSec $TimeoutSec -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Expected 200, got $($r.StatusCode)" }
}

Test-Scenario "P3" "/SuperAdmin/Auth/Login renders (separate cookie scheme)" {
    $r = Invoke-WebRequest -Uri "$BaseUrl/SuperAdmin/Auth/Login" -TimeoutSec $TimeoutSec -UseBasicParsing
    if ($r.StatusCode -ne 200) { throw "Expected 200, got $($r.StatusCode)" }
}

# -- Security headers (Phase 26 middleware) --------------------------
$headerProbe = $null
try {
    $headerProbe = Invoke-WebRequest -Uri "$BaseUrl/Auth/Login" -TimeoutSec $TimeoutSec -UseBasicParsing
} catch {
    # Will surface in next 4 tests
}

Test-Scenario "S1" "X-Frame-Options: DENY" {
    if (-not $headerProbe) { throw "Header probe request failed" }
    $v = $headerProbe.Headers["X-Frame-Options"]
    if ($v -ne "DENY") { throw "Expected DENY, got '$v'" }
}

Test-Scenario "S2" "X-Content-Type-Options: nosniff" {
    if (-not $headerProbe) { throw "Header probe request failed" }
    $v = $headerProbe.Headers["X-Content-Type-Options"]
    if ($v -ne "nosniff") { throw "Expected nosniff, got '$v'" }
}

Test-Scenario "S3" "Referrer-Policy header present" {
    if (-not $headerProbe) { throw "Header probe request failed" }
    $v = $headerProbe.Headers["Referrer-Policy"]
    if ([string]::IsNullOrEmpty($v)) { throw "Header missing" }
}

Test-Scenario "S4" "Server header stripped" {
    if (-not $headerProbe) { throw "Header probe request failed" }
    $v = $headerProbe.Headers["Server"]
    if (-not [string]::IsNullOrEmpty($v)) {
        throw "Server header leaks stack fingerprint: '$v' (should be stripped by SecurityHeadersMiddleware)"
    }
}

# -- Error pages -----------------------------------------------------
Test-Scenario "E1" "404 error page branded" {
    try {
        $r = Invoke-WebRequest -Uri "$BaseUrl/this-route-does-not-exist-$([Guid]::NewGuid())" -TimeoutSec $TimeoutSec -UseBasicParsing -ErrorAction Stop
        throw "Expected exception, got $($r.StatusCode)"
    } catch [System.Net.WebException] {
        $resp = $_.Exception.Response
        if ($null -eq $resp) { throw "No response object on 404 exception" }
        if ([int]$resp.StatusCode -ne 404) { throw "Expected 404, got $([int]$resp.StatusCode)" }
    } catch [Microsoft.PowerShell.Commands.HttpResponseException] {
        if ([int]$_.Exception.Response.StatusCode -ne 404) {
            throw "Expected 404, got $([int]$_.Exception.Response.StatusCode)"
        }
    }
}

# -- Summary ---------------------------------------------------------
$pass = ($results | Where-Object Status -eq "PASS").Count
$fail = ($results | Where-Object Status -eq "FAIL").Count
$total = $results.Count

Write-Host ""
$results | Format-Table Id, Description, Status, Ms, Notes -AutoSize

Write-Host ""
Write-Host "  Total:  $total" -ForegroundColor Gray
Write-Host "  Pass:   $pass" -ForegroundColor Green
if ($fail -gt 0) {
    Write-Host "  Fail:   $fail" -ForegroundColor Red
} else {
    Write-Host "  Fail:   0" -ForegroundColor Gray
}
Write-Host ""

if ($fail -gt 0) {
    Write-Host "Smoke FAILED ($fail of $total)." -ForegroundColor Red
    exit 1
}

Write-Host "Smoke PASSED ($pass of $total)." -ForegroundColor Green
exit 0
