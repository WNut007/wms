# ADR-015: Land + Expand GTM Strategy for v3.0.0

**Status**: Accepted
**Date**: 2026-05-11
**Decision makers**: Project owner

---

## Context

Heading into v3.0.0 (first customer launch), the team had to commit to a
go-to-market posture. Two viable strategies were on the table:

1. **SaaS-first** — public landing page, self-service signup, free trial,
   credit-card billing, automated provisioning, customer success funnel.
2. **Sales-led (Land + Expand)** — enterprise prospects, sales-led
   onboarding, custom contracts, manual provisioning via SuperAdmin
   tooling, white-glove first 90 days.

Constraints that shaped the choice:

- Single-developer team — can't operate a SaaS funnel + the product simultaneously.
- v3.0.0 has no billing module (TD per ADR-006 ADR is deferred).
- v3.0.0 has no email reset / SMTP infrastructure (TD-078).
- v3.0.0 has no self-signup auth surface — only sales-provisioned tenants.
- Cash-funded growth — first customer revenue funds the next features.
- Validation matters — better to learn from 1-2 deep customers than 100 shallow ones.

---

## Decision

**Land Enterprise first, Expand into SaaS later.**

### v3.0.0 launch (Phase 30)

- 1-3 enterprise reference customers
- Sales-led: discovery → contract → SuperAdmin provisions → manual handoff
- Each tenant = full DB-per-tenant isolation (ADR-001 / ADR-016)
- Custom contracts, custom pricing, ~$15K-50K ACV per customer
- Manual onboarding + 90-day high-touch white-glove support

### v3.1+ (post-launch)

- Self-service signup surface (build on existing /Auth flow)
- Email integration (TD-078 + TD-056)
- Billing module (ADR-006 implementation)
- Hybrid DB strategy (TD-065 / ADR-019) for SMB tier on shared DB
- Lower-touch trial-to-paid funnel for SMB market

---

## Rationale

- **Cash-funded growth**: enterprise ACV funds the next 6 months of dev.
  SaaS requires monthly retention to be solved before any cash comes in.
- **Validated PMF before scale**: 1-2 deep customers tell us more about
  what to build next than 100 free-trial signups. Pre-revenue feedback
  is mostly noise.
- **Single-developer team**: a SaaS funnel has many moving parts
  (acquisition, conversion, support, billing, churn) — sales-led has
  exactly one (close the deal). Team size = 1 today.
- **Product fits enterprise model**: WMS is enterprise software by
  nature (multi-tenant + heavy schema + complex workflows). SMB
  customers often don't need this depth — wait until v3.1 to serve them.
- **Less competitive pressure at the high end**: SMB WMS has 50+
  players; enterprise has 10. Bigger pie share, less price erosion.

---

## Consequences

### Positive
- Cash-positive faster
- Higher first-customer ACV = more runway per closed deal
- Validated PMF before broadening the funnel
- Deeper product feedback from 1-2 design partners
- Reference customer story for v3.1+ SaaS launch
- No premature optimisation of a self-service funnel that may not match the eventual SMB needs

### Negative
- Slower top-of-funnel growth (no inbound trials = no compounding signups)
- Higher acquisition cost per customer (sales effort)
- Single-customer concentration risk (if first customer churns, big hit)
- Forces honest conversations about timeline / feature gaps (good in the long run, hard short-term)

### Operational implications

- **SuperAdmin tooling** (Phase 27) is the launch UX, not a stopgap
- **Onboarding playbook** (Phase 28) is the runbook for the sales cycle
- **Tests posture** stays disciplined — broken software at $15K ACV is much worse than at $0
- **TDs accumulate intentionally** — features we can sell around get deferred until a customer asks; features that block deals get prioritised
- **First customer call** at Day 30 is the most important call in the company's history — schedule it before signing the contract

---

## Related ADRs

- [ADR-001 — Multi-tenant DB per tenant](./ADR-001_Multi_Tenant_DB_per_Tenant.md) (informally — no file yet; CLAUDE.md is authoritative)
- [ADR-016 — DB-per-Tenant confirmed for v3.0.0](./ADR-016_DB_per_Tenant_Confirmed_v3.md)
- [ADR-018 — Pre-decision methodology](./ADR-018_Pre_Decision_Methodology.md)
- [ADR-019 — Hybrid DB Strategy Deferred](./ADR-019_Hybrid_DB_Strategy_Deferred.md)
- [ADR-020 — Documentation-MVP Approach](./ADR-020_Documentation_MVP_Approach.md)
