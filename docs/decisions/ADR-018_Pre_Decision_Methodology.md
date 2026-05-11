# ADR-018: Pre-Decision Methodology for Phase Briefs

**Status**: Accepted
**Date**: 2026-05-13
**Decision makers**: Project owner
**Codified in**: Phase 25 brief format onward

---

## Context

Through Phases 1-24, phase briefs followed a "Q1/Q2/Q3 — please decide
during the audit" pattern. The agent (Claude Code) would:

1. Read the brief
2. Run an audit
3. Pause at decision points to ask Q1, Q2, Q3
4. Wait for user response
5. Resume implementation

This worked but added round-trips at the start of every phase. Phase
boundaries were also where the most context-loading friction occurred
(re-orienting to a new domain, re-reading old code, etc.) — adding
question-back-to-user moments amplified that friction.

Phase 24 experimented with a pre-decided brief: instead of "Q1: should
roles be system-only or also custom?", the brief said "D1: System-only
in v1; custom roles = TD-050". The agent saw a locked decision, audited
to confirm feasibility, and proceeded without pausing.

Phase 24 shipped ~20% faster than equivalently-scoped Phase 23
(measured against the locked-down vs decision-point time per chunk).
Per-chunk velocity was visibly higher.

---

## Decision

**All architectural questions for a phase are decided BEFORE the brief
is handed to the agent.** Briefs from Phase 25 onward follow this
format:

### Brief structure

```
Phase N: <title>

Mode: AUDIT-FIRST then AUTO-PROCEED
Branch: feat/<name>
Tag: vX.Y.Z-<name>
Time estimate: ~Nh

═══════════════════════════════════════════
DECISIONS LOCKED (no mid-flight pauses for these)
═══════════════════════════════════════════

D1: <decision> — <one-line rationale>
D2: <decision> — <one-line rationale>
...

═══════════════════════════════════════════
AUDIT REQUIRED BEFORE T1
═══════════════════════════════════════════

Check existing:
1. <thing to verify>
2. <thing to verify>
...

═══════════════════════════════════════════
MODULE BREAKDOWN
═══════════════════════════════════════════

Module 1: <name> (~Nm)
- <task>
- <task>
...

═══════════════════════════════════════════
PAUSE CONDITIONS
═══════════════════════════════════════════

Pause IF:
- <unexpected reality>
- <build breaks>
- <decision needed that wasn't pre-decided>

═══════════════════════════════════════════
NOW
═══════════════════════════════════════════

Begin with audit, auto-proceed if clean.
```

The agent's job:
1. Read the brief
2. Audit per the "AUDIT REQUIRED" list — verify the locked decisions are still implementable
3. Build per the "MODULE BREAKDOWN" — no Q&A round-trips
4. Pause only if a PAUSE CONDITION trips

---

## Rationale

### Velocity

Phase 25-27 results (locked-decision phases):

| Phase | Estimate | Actual | Tests added | Variance |
|---|---|---|---|---|
| Phase 25 | 6-8h | ~5h | +31 | -25% |
| Phase 26 | 3.5-4h | ~3.5h | +12 | 0% |
| Phase 27 | 4-5h | ~4.5h | +20 | 0% |

vs. Phase 23-24 (mixed decision style):

| Phase | Estimate | Actual | Tests added | Variance |
|---|---|---|---|---|
| Phase 23 | 6-8h | ~3.5h | +26 | -50% (under) |
| Phase 24 | 6-8h | ~6h | +34 | 0% |

The locked-decision phases (25-27) are MORE predictable — actuals
cluster around the estimate. Mid-flight Q&A was the largest source of
estimate variance.

### Quality of decisions

Pre-decisions force the user to think through trade-offs BEFORE
starting work. Questions like "what if we discover X mid-build?" get
covered in the brief's PAUSE CONDITIONS, not improvised mid-flight.

This is exactly how senior engineers write design docs: "here's what
I'm building, here's why, here's what would change my mind". The brief
format formalises that.

### Recovery from bad pre-decisions

Pre-decisions are not infallible — sometimes the audit reveals a
constraint that invalidates a locked decision. That's what PAUSE
CONDITIONS are for. The agent pauses, surfaces the conflict, the user
adjusts the brief, the agent resumes.

In practice: 0 pauses across Phases 25-27. The audit phase catches
issues cheaply.

---

## Consequences

### Positive
- ~20% faster phase delivery
- Predictable phase durations (estimates land within 10-20% of actuals)
- Decisions are recorded in the brief — future-you can re-read why
  something is the way it is
- The audit phase is short + focused (verify, don't decide)
- The agent stays in build flow longer

### Negative
- More upfront planning effort per phase (writing a good brief now
  takes 30-60 min of user time vs 5-10 min of "let's just start")
- Pre-deciding the wrong thing wastes the build cycle until the audit
  catches it (mitigated by the AUDIT REQUIRED list catching most cases)
- Some decisions are genuinely ambiguous until you see the audit
  results — pre-deciding them is awkward (brief allows D-options in
  these cases: "default to D1 unless audit shows X")

### When to deviate

Some phases legitimately need mid-flight decisions:
- **Exploratory phases** (proof-of-concept, new technology evaluation) —
  too many unknowns to pre-decide
- **Cross-cutting refactors** — each module's adjustment depends on
  prior modules' outcomes
- **Bug-fix phases** — the fix path emerges from investigation, not
  from upfront design

For those, retain the older Q1/Q2/Q3 format. The default for net-new
features is locked-decision.

---

## Related ADRs

- [ADR-015 — Land + Expand GTM Strategy](./ADR-015_Land_Expand_GTM_Strategy.md) (the GTM context that makes velocity matter)
- [ADR-020 — Documentation-MVP Approach](./ADR-020_Documentation_MVP_Approach.md) (same "decide once, execute fast" thinking applied to docs)

## Related memory entries

- `feedback_chunk_workflow.md` — chunk-by-chunk implementation
- `feedback_audit_first_for_lookup_integration.md` — audit-first protocol
- `feedback_spec_rename_audit.md` — audit catches reality-vs-brief gaps cheaply
