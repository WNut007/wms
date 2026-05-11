# Phase 30A — local deploy + smoke test results

Captured by: **\<your name>**
Date: **\<YYYY-MM-DD>**
Branch: **feat/phase-30a-local-test** (tag v2.16.0-deploy-test-ready)
SQL Server: **\<localhost / instance>**
.NET version: **\<dotnet --version output>**

---

## Pre-flight

| Item | Status | Notes |
|------|--------|-------|
| `WMS_Master` DB created in SSMS | ☐ | |
| `ConnectionStrings__MasterDb` set | ☐ | |
| `ConnectionStrings__TenantTemplate` set | ☐ | with `{0}` placeholder |
| `Email__*` env vars set (optional) | ☐ | TestMode default if not set |
| Phase 27 `InitialSuperAdmin` configured | ☐ | Email + TempPassword |

---

## Deploy script (`Test-Local-Deploy.ps1`)

| Step | Status | Notes |
|------|--------|-------|
| 1. Env validation | ☐ | |
| 2. Publish (`dotnet publish`) | ☐ | Time: |
| 3. Master migrations (`up master`) | ☐ | Time: |
| 4. Tenant migrations fan-out (`up tenants`) | ☐ | # tenants processed: |
| 5. Publish artifact verified | ☐ | WMS.Web.dll + WMS.BLL.dll present |
| 6. Kestrel launched on :5500 | ☐ | |

---

## Automated smoke (`Smoke-Local.ps1`)

| ID | Scenario | Result | Time (ms) | Notes |
|----|----------|--------|-----------|-------|
| H1 | `/healthz/live` | ☐ | | |
| H2 | `/healthz/ready` master-db entry | ☐ | | |
| H3 | `/healthz` alias | ☐ | | |
| H4 | `/health` legacy | ☐ | | |
| P1 | Root → 200 or 302 | ☐ | | |
| P2 | `/Auth/Login` | ☐ | | |
| P3 | `/SuperAdmin/Auth/Login` | ☐ | | |
| S1 | X-Frame-Options: DENY | ☐ | | |
| S2 | X-Content-Type-Options: nosniff | ☐ | | |
| S3 | Referrer-Policy present | ☐ | | |
| S4 | Server header stripped | ☐ | | |
| E1 | 404 branded page | ☐ | | |

Total: **__ / 12 passed**

---

## E2E manual smoke (12 scenarios per `local-smoke-checklist.md`)

| ID | Scenario | Result | Notes |
|----|----------|--------|-------|
| S1  | SuperAdmin bootstrap login + forced password change | ☐ | |
| S2  | Provision new tenant | ☐ | Tenant code: |
| S3  | Tenant first login + MustChangePassword | ☐ | |
| S4  | Add team member with role | ☐ | |
| S5  | Master Data: Warehouse + Location | ☐ | |
| S6  | Inbound desktop: PO → Goods Receipt → Stock | ☐ | |
| S7  | Mobile receive PWA | ☐ | |
| S8  | Mobile putaway | ☐ | |
| S9  | Outbound: SO → Allocate → mobile Pick | ☐ | |
| S10 | Outbound finish: mobile Pack → Ship | ☐ | |
| S11 | Reports + Excel export | ☐ | |
| S12 | Suspend + reactivate tenant | ☐ | |

Total: **__ / 12 passed**

---

## Email validation (if `TestMode=false`)

| Item | Status | Notes |
|------|--------|-------|
| TenantCreated email arrived (S2) | ☐ | Inbox: |
| Subject correct | ☐ | "Your WMS workspace … is ready" |
| TempPassword renders in monospace block | ☐ | |
| HTML + text both render | ☐ | Open "raw" view |
| PasswordReset email (S12 → ResetAdminPassword) | ☐ | Subject: "Your WMS password has been reset" |

---

## Issues found

Log each as a 1-line summary + reproduction; file follow-up TDs if
needed. Don't tag until empty or all entries are explicitly
deferred.

1.
2.
3.

---

## Recommendation

☐ **Ready for tag** `v2.16.0-deploy-test-ready` — all scenarios pass.

☐ **NOT ready** — see issues above. Defer tag until resolved.

---

## Performance baseline (informational)

| Endpoint | p50 (ms) | p95 (ms) | Notes |
|----------|----------|----------|-------|
| `/healthz/live` | | | |
| `/healthz/ready` | | | Master DB probe |
| `/Auth/Login` | | | Cold start |
| `/SuperAdmin/Dashboard` | | | After login |
| `/Reports/Inventory` | | | After some activity |

Capture via Smoke-Local.ps1's `Ms` column or browser DevTools
Network panel. Useful as a regression marker for future Phase 30B+
benchmarks (TD-073).
