# Claude Code Prompt Templates

> 📌 ใช้ template เหล่านี้เพื่อสื่อสารกับ Claude Code ให้มีประสิทธิภาพ

---

## 🎯 Template 1: New Feature Implementation

```
I'm implementing the [FEATURE NAME] feature for the WMS project.

Context:
- Feature description: [WHAT IT DOES]
- Module: [WHICH MODULE - e.g., outbound, inventory]
- Phase: [Phase 1/2/3/4]
- Related docs: docs/01_Master_Design.md section [X]

Requirements:
1. [REQUIREMENT 1]
2. [REQUIREMENT 2]
3. [REQUIREMENT 3]

Tasks:
1. Read CLAUDE.md and relevant design docs
2. Check existing similar patterns in the codebase
3. Create migration script in db/migrations/
4. Implement domain entities in WMS.Domain
5. Implement repository in WMS.DAL
6. Implement service in WMS.BLL
7. Add controller + views in WMS.Web
8. Write unit tests
9. Write integration test for critical path

Constraints:
- Multi-tenant: ensure tenant isolation
- Owner-aware: handle if applicable
- Use Dapper (no EF)
- Follow existing patterns

Deliverables:
- Working code passing tests
- Migration script tested rollback
- Updated documentation if needed

Please proceed step by step. Show me your plan first, then implement.
```

---

## 🎯 Template 2: Bug Fix

```
I have a bug in [MODULE/FILE].

Symptom:
[WHAT'S HAPPENING - error message, wrong behavior]

Expected:
[WHAT SHOULD HAPPEN]

Reproduction:
1. [STEP 1]
2. [STEP 2]
3. [STEP 3]

Context:
- Related files: [PATHS]
- Recent changes: [WHAT CHANGED RECENTLY]

Tasks:
1. Investigate root cause
2. Check existing tests
3. Fix the bug
4. Add regression test
5. Verify fix doesn't break other things

Please show me:
- Root cause analysis
- Proposed fix (before implementing)
- Then implementation
- Then test verification
```

---

## 🎯 Template 3: Refactoring

```
I want to refactor [MODULE/PATTERN] to [NEW PATTERN].

Current problem:
[WHAT'S WRONG - too coupled, slow, hard to test, etc.]

Target state:
[HOW IT SHOULD BE]

Constraints:
- DO NOT change external API
- DO NOT break existing tests
- Keep migration small and reversible

Tasks:
1. Analyze current implementation
2. Propose refactoring plan
3. Show me before implementing
4. Refactor in small commits
5. Run tests after each commit

Important: This is refactoring, not feature work. Behavior must stay identical.
```

---

## 🎯 Template 4: Code Review

```
Please review this code/PR:

File(s): [PATHS]
Or paste code below:

[CODE]

Review focus:
- [ ] Architecture consistency (CLAUDE.md rules)
- [ ] Security (multi-tenant, input validation, SQL injection)
- [ ] Performance (N+1 queries, big result sets)
- [ ] Testing (coverage, edge cases)
- [ ] Naming and conventions
- [ ] Error handling
- [ ] Logging

Please be critical. Find issues that humans might miss.
Categorize feedback as:
- 🔴 Critical (must fix)
- 🟡 Should fix
- 🟢 Suggestion
- 💭 Discussion point
```

---

## 🎯 Template 5: Schema Change

```
I need to add/modify table: [TABLE NAME]

Purpose:
[WHY THIS CHANGE IS NEEDED]

Schema changes:
[DESCRIBE CHANGES - new columns, indexes, etc.]

Tasks:
1. Generate migration script (FluentMigrator) in db/migrations/
2. Update domain entity in WMS.Domain
3. Update repository methods in WMS.DAL
4. Update services as needed
5. Add tests for new fields/behavior
6. Update docs/02_Database_Schema.md
7. Test rollback script works

Considerations:
- Will this affect existing data?
- Are indexes needed?
- Multi-tenant: per-tenant or master?
- Backwards compatibility for in-flight data
```

---

## 🎯 Template 6: Strategy/Plugin Implementation

```
I'm implementing a new [STRATEGY/PLUGIN] for [MODULE].

Type: [Allocation Strategy / Carrier Plugin / Marketplace Adapter / etc.]
Specific: [E.g., "Lazada marketplace adapter"]

Reference design: docs/01_Master_Design.md section [X]
Existing implementations to learn from:
- [PATH TO EXISTING]

Tasks:
1. Implement interface I[Strategy]
2. Add registration in DI container
3. Add configuration UI in admin
4. Add unit tests
5. Add integration test

Constraints:
- Must follow plugin pattern
- Must be hot-swappable (config-driven, not code-deploy)
- Must handle failures gracefully (circuit breaker)
- Must log all external calls
```

---

## 🎯 Template 7: Integration with External System

```
I need to integrate with [EXTERNAL SYSTEM].

Purpose:
[WHAT WE'RE DOING - sending orders, getting tracking, etc.]

API documentation:
[LINK OR PASTE]

Authentication:
[HOW THEY AUTH - API key, OAuth, etc.]

Tasks:
1. Create plugin in WMS.Plugins/[Name]/
2. Implement client with HttpClient + Polly
3. Add retry logic with exponential backoff
4. Add circuit breaker (Polly)
5. Add dead letter queue for failures
6. Add health check
7. Write integration tests with WireMock
8. Document configuration in docs/

Constraints:
- All API calls must be logged
- Sensitive data in env vars or Key Vault
- Timeout: 30s default
- 3 retries max
- Idempotency: use unique IDs
```

---

## 🎯 Template 8: Performance Optimization

```
I have a performance issue:

Module/Endpoint: [WHICH]
Current: [METRICS - response time, query count, etc.]
Target: [GOAL]

What I've measured:
[PROFILER OUTPUT, EXPLAIN PLAN, etc.]

Tasks:
1. Analyze the bottleneck
2. Propose optimization (don't implement yet)
3. Discuss tradeoffs
4. Implement after my approval
5. Measure improvement
6. Update tests if behavior changed

Don't optimize prematurely. Show me the data first.
```

---

## 🎯 Template 9: Test Generation

```
Please write tests for [FILE/METHOD].

Context:
[WHAT IT DOES]

Test types:
- Unit tests: [Y/N]
- Integration tests: [Y/N]
- Edge cases: [Y/N]

Requirements:
- Use xUnit
- Use Moq for mocks
- Use Testcontainers for SQL Server (integration)
- Coverage: aim for 80%+ on new code
- Test edge cases I might have missed

Pattern:
- Arrange / Act / Assert
- One assertion per test (when possible)
- Descriptive test names
```

---

## 🎯 Template 10: Initial Project Setup

```
I'm starting a new WMS project. Set up the solution structure.

Tech stack:
- .NET 8 Core MVC
- Dapper
- SQL Server
- FluentMigrator
- Hangfire
- SignalR
- xUnit + Moq + Testcontainers

Tasks:
1. Create solution: WMS.sln
2. Create projects per docs/03_Roadmap.md "Project Setup"
3. Add NuGet packages with versions
4. Set up project references
5. Create base classes (BaseController, BaseService, BaseRepository)
6. Configure DI container
7. Configure Serilog + Application Insights
8. Set up appsettings.json hierarchy
9. Create CLAUDE.md (use the template I provide)
10. Create initial README.md

Deliverable: Working "dotnet build" success.
```

---

## 💡 Tips for Working with Claude Code

### DO:
- ✅ Always reference design docs in prompts
- ✅ Show existing patterns to follow
- ✅ Specify constraints clearly
- ✅ Ask for plan before implementation
- ✅ Review intermediate output
- ✅ Commit after each successful step

### DON'T:
- ❌ Big bang prompts ("build the whole picker module")
- ❌ Vague requirements ("make it good")
- ❌ Skip reviewing Claude's plan
- ❌ Let Claude introduce new patterns without ADR
- ❌ Forget to update CLAUDE.md after architectural changes

### Iteration Pattern

```
1. PROMPT → Claude proposes plan
2. REVIEW → Adjust plan as needed
3. APPROVE → Claude implements step 1
4. VERIFY → Tests pass, code reviewed
5. COMMIT → Atomic commit with good message
6. NEXT → Repeat for step 2
```

---

## 🎯 Daily Workflow

```
Morning:
- Update "Current Phase" in CLAUDE.md
- Review yesterday's commits
- Plan today's slices

During work:
- Use prompt templates
- Commit frequently
- Run tests after each change
- Update docs alongside code

End of day:
- Push commits
- Update sprint board
- Note blockers in CLAUDE.md
```

---

## 📋 Sprint Routine (Weekly)

```
Monday: Plan week's slices, update CLAUDE.md
Tue-Thu: Build slices
Friday: Demo, test, integrate, doc update
Weekend: Rest 😊
```

---

**Save this file as `docs/CLAUDE_CODE_PROMPTS.md` for team reference.**
