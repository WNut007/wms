# Phase 30B prep checklist

What needs to happen between Phase 30A (local validation) and
Phase 30B (real-server deployment for the first customer).

Phase 30A proved the artifact works on a workstation. Phase 30B is
the same artifact running on a Windows Server + IIS in a network
the customer can reach.

---

## Pre-flight (before Phase 30B starts)

### Phase 30A signoff

- [ ] `Test-Local-Deploy.ps1` runs green
- [ ] `Smoke-Local.ps1` 12/12 pass
- [ ] Manual smoke (12 scenarios) 12/12 pass
- [ ] `phase-30a-test-results.md` filled in
- [ ] Tag `v2.16.0-deploy-test-ready` exists

### Customer commitments locked

- [ ] Customer agreement signed
- [ ] Hosting model decided (their server vs ours vs Azure)
- [ ] DNS plan decided (subdomain on our DNS vs theirs)
- [ ] TLS cert source decided (Let's Encrypt / commercial CA / customer-provided)
- [ ] Master data export from customer's current system (Excel / CSV)
- [ ] First go-live date pencilled (recommend mid-week, AM)

---

## Phase 30B scope (per `local-test-procedure.md` "What this does NOT cover")

### Infrastructure provisioning

- [ ] Windows Server 2019+ VM provisioned (4 vCPU / 8 GB RAM minimum)
- [ ] SQL Server 2019+ installed (Express OK for first customer; Standard for v3.1+)
- [ ] .NET 8 Hosting Bundle installed
- [ ] IIS installed + ASP.NET Core Module v2 verified
- [ ] Folder structure: `C:\inetpub\wms\app` + `C:\wms\logs` + `C:\wms\storage`
- [ ] Service account created (`WMS_Service`) with file-system grants

### IIS configuration

- [ ] Application Pool: `WMS` (No Managed Code, AlwaysRunning, ServiceAccount identity)
- [ ] Site: `WMS` bound to chosen hostname:443
- [ ] `web.config` env vars set:
  - [ ] `ASPNETCORE_ENVIRONMENT=Production`
  - [ ] `ConnectionStrings__MasterDb`
  - [ ] `ConnectionStrings__TenantTemplate`
  - [ ] `Email__Username` / `Password` / `FromAddress` / `TestMode=false`
  - [ ] `Email__LoginUrl=https://wms.customer.com/Auth/Login`
- [ ] Application Initialization module configured (preload `https://wms.customer.com/healthz/live`)
- [ ] HTTPS-only bindings; HTTP redirects to HTTPS

### Database setup

- [ ] `WMS_Master` DB created in SSMS
- [ ] `wms_app` SQL login created with `db_owner` on master + `dbcreator` server-role
- [ ] Master migrations applied via `tools/WMS.Migrate up master`
- [ ] `InitialSuperAdmin` config-driven seed completes on first request
- [ ] Manual SuperAdmin password change forced
- [ ] First customer tenant provisioned via `/SuperAdmin/Onboarding` (creates `WMS_Tenant_<CODE>` + applies Tenant migrations + seeds bootstrap admin)
- [ ] **Tenant pollution audit** — run `docs/deployment/phase-30a2-pollution-audit.sql`
      against the new tenant DB to verify clean bootstrap (regression guard for
      Phase 30A.2 P0-6). Expected counts:
  - [ ] `master.Warehouses` = 1 (only `WH-MAIN`)
  - [ ] `master.Owners` ≥ 1 (`SELF` present)
  - [ ] `master.UnitsOfMeasure` ≥ 1 (`EA` present)
  - [ ] `master.Customers` = 0, `master.Products` = 0, `master.ProductCategories` = 0
  - [ ] Zero rows matching `WH-DM%`, `CUST-XXXX`, `PROD-XXXX`, `DEMO-001`
  - [ ] If any demo pattern has `RowsPresent > 0`: a migration gate has regressed
        — STOP, investigate, do not hand over to customer

### Email

- [ ] SMTP provider chosen (Gmail App Password OK for first month; SendGrid for scale per TD-110)
- [ ] DKIM / SPF / DMARC configured on the FromAddress domain (TD-107)
- [ ] Test send from production server to internal mailbox (latency + bounce check)

### Security

- [ ] TLS cert installed + bound to :443
- [ ] HSTS preload candidate confirmed (or post-launch)
- [ ] Hangfire dashboard reachable on `/hangfire` AND gated to ADMIN role
- [ ] `/SuperAdmin/*` accessible from authorized IPs only (optional — IP allowlist via IIS)
- [ ] Windows Update + SQL Server CU current
- [ ] Firewall rules: only :443 inbound; SQL :1433 internal only

### Operational readiness

- [ ] Log aggregation set up (or local file rotation verified — `logs/wms-{Date}.log` with 30d retention from Phase 26)
- [ ] Backup automation per `docs/operations/runbook.md` (daily SQL backup + weekly off-site copy)
- [ ] DR runbook drafted (TD-075) and one drill completed
- [ ] Monitoring set up (or `/healthz/ready` polled by IIS Application Initialization + manual checks until TD-068 Application Insights)
- [ ] Status page or comms plan agreed with customer
- [ ] Support escalation path documented

### CI/CD (optional for Phase 30B)

- [ ] GitHub Actions workflow created (TD-067)
- [ ] Build artifact published to GitHub Releases / Azure Artifacts
- [ ] Deployment script wired (PowerShell DSC / Octopus / manual via runbook for v1)

---

## Go-live procedure (Day 0)

Suggested order for the launch day, all done in 1 session (not
spread across days — minimizes the "state of half-deployed" window):

1. **08:00** — Customer ack received, "go" given.
2. **08:30** — Provision SuperAdmin on the deployed instance (S1).
3. **08:45** — Provision the customer's first tenant via SuperAdmin (S2).
4. **09:00** — Hand off temp password to customer's first ADMIN via secure channel (TD-101).
5. **09:30** — Customer first login; walk them through ChangePassword (S3) + warehouse setup (S5).
6. **10:30** — Customer imports their master data (products / customers / owners) via the admin CRUD pages.
7. **13:00** — Customer's first PO → first Receive (S6) — observe + screen-share.
8. **14:00** — Customer's first SO → first Pick → Pack → Ship (S9, S10).
9. **15:00** — Verify all stats + reports render (S11).
10. **16:00** — Confirm everything working; promise next-day check-in.

If at any point: rollback = take instance offline (stop IIS site)
+ communicate; restart can wait until next AM. Don't try to debug
under pressure during the launch window.

---

## Day 1-7 watching

Daily checks for the first week:

- [ ] `/healthz/ready` polled OK every 5 min
- [ ] Hangfire dashboard — pack-video retention job ran successfully overnight
- [ ] `master.SystemAuditLog` recent rows look sane (no churning failed logins)
- [ ] Disk usage on storage volume not climbing fast
- [ ] Log file rotation working (`logs/wms-{Date}.log` cycles)
- [ ] Customer using the system daily (not just trial-then-abandoned)

---

## Things that will NOT be ready for Phase 30B

These are post-launch v3.0.x / v3.1+ items. Don't block 30B on them.

- 2FA / TOTP (TD-055)
- Forgot password email flow (TD-056) — depends on M1's IEmailService
- Customer-facing user guides per module (TD-090) — needs customer questions first
- Multi-tenant per-tenant settings UI (TD-083)
- Distributed cache / multi-instance (TD-072) — single-instance fine for first customer
- Application Insights (TD-068)
- CI/CD pipeline (TD-067) — manual deploy via runbook for v1
- Performance benchmarks baseline (TD-073)
- Auto-scaling (TD-074)
- DR drill cadence (TD-075)

---

## Open questions to resolve BEFORE 30B starts

1. **Where does the temp password go?** — Out-of-band secure channel
   (1Password / Signal / direct call) per TD-101 until one-time-view
   URL ships.
2. **Who owns the SuperAdmin credentials?** — Currently 1-2 people
   on our side. Document in runbook.
3. **Maintenance window policy?** — Agree with customer when
   migrations / restarts are OK (suggested: Sundays 02:00-04:00
   local).
4. **Support hours?** — Set expectations: business hours +
   emergency on-call number.
5. **Audit log retention?** — Default forever; agree explicit
   archival policy if customer has compliance constraints.
