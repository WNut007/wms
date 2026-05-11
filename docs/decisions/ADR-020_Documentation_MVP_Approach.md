# ADR-020: Documentation-MVP Approach for v3.0.0

**Status**: Accepted
**Date**: 2026-05-16
**Decision makers**: Project owner
**Implemented in**: Phase 28 (v2.14.0-docs)

---

## Context

Heading into v3.0.0 launch, the team had to choose a documentation
posture. Three viable options:

1. **Comprehensive customer-facing docs** — full user guide per module,
   role-based tutorials, screenshots, FAQ, video walkthroughs
2. **Minimum operational docs** — runbook for the operator, onboarding
   playbook for sales, ADRs for future maintainers, polished README
3. **No formal docs** — rely on the codebase + CLAUDE.md + verbal
   handoffs to onboard the first customer

Constraints:

- Single-developer team (you + future hires)
- ADR-015 commits to Land + Expand — first customer is hand-held
- First customer's tech contact will be in 1:1 conversations weekly during the white-glove period
- Customer-facing UI is the de facto user manual (good UX = less docs needed)
- Generic upfront docs become stale quickly (features evolve, screens change)
- Customer-driven docs are MORE useful (real questions → real answers)

---

## Decision

**Option 2 — Minimum Operational Docs.** Customer-facing docs deferred
to post-first-customer.

Phase 28 delivers:

1. **`docs/operations/runbook.md`** — daily ops + incident response for the platform operator
2. **`docs/operations/onboarding-playbook.md`** — sales-led customer onboarding process
3. **6 new ADRs** (ADR-015 through ADR-020) — architectural rationale backfill for v3.0.0 chapter decisions
4. **`README.md` enhancement** — top-level status / tech stack / quick start

Deferred to TDs:

- **TD-089** — Thai-language customer-facing docs (when first Thai customer signs)
- **TD-090** — User guide per module (post-first-customer; structured around their actual questions)
- **TD-091** — Video tutorials (post-launch; needs polished UI)
- **TD-092** — API documentation (when API surface is exposed)
- **TD-093** — Knowledge base / FAQ (after first customer)
- **TD-094** — Translated onboarding materials (when international customers sign)
- **TD-095** — Marketing site content (v3.1+ SaaS launch)
- **TD-096** — Architecture diagrams (visual not text; valuable for sales conversations)

---

## Rationale

### Customer-driven content > generic upfront content

Writing a user guide before the first customer asks any questions is a
gamble on what they'll ask. The first customer's actual questions tell
us:

- Which workflows are intuitive vs confusing
- Which jargon needs translation
- What the FAQ structure should be
- Which screenshots to capture (vs guessing)

Post-customer docs are sharper because the customer's questions are
the table of contents.

### Operator-side docs are immediately useful

The runbook + onboarding playbook are docs WE need NOW:
- Runbook helps when something breaks at 11 PM
- Onboarding playbook helps when the second customer's pre-sales call goes well
- ADRs help future-team understand why a decision was made

These docs serve the team's own velocity, not a hypothetical user.

### MVP fits the funnel stage

For sales-led launch with 1-3 customers:
- White-glove training replaces generic tutorials
- Slack / email Q&A replaces a knowledge base
- 1:1 calls replace video walkthroughs

These replacements scale POORLY (you don't want to do 1:1 calls forever).
But they're the right answer for v3.0.0. Mature docs come when the
funnel widens.

### ADR backfill matters more than user guides

ADRs are the institutional memory. Without them, every future change
re-litigates settled questions ("why DB-per-tenant?"). The Phase 23-27
ADR backfill (Phase 28's ADR-015 through ADR-020) closes a 6-month
gap in the decision log.

---

## Consequences

### Positive
- Phase 28 ships in ~3-4h (vs ~2 weeks for comprehensive customer docs)
- Operator docs are immediately useful (first incident or first onboarding)
- ADR backfill closes the institutional memory gap
- First customer's actual questions shape future doc structure (better outcomes)
- Less doc maintenance burden during the v3.0.0 chapter
- Customer-facing voice / tone / structure decisions defer until we know the customer

### Negative
- Sales conversations lack a downloadable spec sheet (one-pager) — TD-095
- Customer's tech contact has more questions in 1:1 (we trade docs work for support work)
- Internal team onboarding (future hires) leans heavily on CLAUDE.md + ADRs (mitigated by both being well-maintained)
- No public-facing knowledge base for SEO / marketing (TD-093 / TD-095)

### What this enables

- **Phase 29 polish + beta-ready** can focus on UX/UI rather than doc rewriting
- **Phase 30 first customer onboarding** uses the playbook from Phase 28
- **Post-customer docs** (TD-090) inherit the customer's actual questions as the structure
- **Second customer** benefits from the answers we accumulated for the first

### When to revisit

Move customer-facing docs from TD to a phase when:

1. **3+ customers signed** — repeating yourself across customers becomes the pain point
2. **First customer asks for a written copy of something** — the natural trigger
3. **Sales conversations stall on "can I read more?"** — prospects expect to self-educate
4. **v3.1+ SaaS launch is on the roadmap** — self-service signup requires self-service docs

Until then, this ADR + the runbook + the playbook are sufficient.

---

## Style decisions

For all v3.0.0 docs (CLAUDE.md, runbook, playbook, ADRs):

- **Language**: English primary. Thai is TD-089 / TD-094 when sold.
- **Format**: Markdown. GitHub-rendered (already pushed to origin).
- **Length**: comprehensive but scannable (bullets, headers, tables).
- **Examples**: real commands, real SQL, real values — not pseudocode.
- **Cross-references**: link to related docs + ADRs explicitly.
- **Code blocks**: language-tagged for syntax highlighting.
- **Audience callout**: every doc states its audience in the first paragraph.

These style choices apply to the future customer-facing docs too —
just deferred until the audience is concrete.

---

## Related ADRs

- [ADR-015 — Land + Expand GTM Strategy](./ADR-015_Land_Expand_GTM_Strategy.md) — the strategy that makes "first customer-driven" possible
- [ADR-018 — Pre-Decision Methodology](./ADR-018_Pre_Decision_Methodology.md) — same "decide once, execute fast" thinking applied to phase briefs
