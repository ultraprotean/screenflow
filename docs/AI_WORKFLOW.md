# AI-assisted development in ScreenFlow

ScreenFlow is built with Claude Code. This file records how, and, more usefully, where the AI was wrong and how that was caught.

## Process

1. **Spec first.** CONTRACT.md and BUILD_SEQUENCE.md are written and reviewed before any code. The AI implements against the spec, so "is this right?" has an answer that doesn't depend on the AI.
2. **One unit at a time, one check per unit.** The AI may not start the next unit until the current unit's check passes. That keeps every change small enough for a human to review.
3. **Guardrails in CLAUDE.md.** Never-do rules (no real PII, no SSN in logs, statuses change only through the domain, no mocked database) are loaded into every session.
4. **Human review of every commit.** Jeff reviews each diff and commit message, and makes the design calls: naming, scope, trade-offs.
5. **Tests against real infrastructure.** Integration tests run against SQL Server, not mocks, so AI-generated persistence code is checked against the real database's behavior (constraints, `rowversion`, collation).

## What AI is good at here, and where it needs watching

| Good at | Watch for |
|---|---|
| Boilerplate: EF configurations, DTOs, endpoint wiring | Plausible but outdated APIs (older .NET or EF Core versions) |
| Writing the test matrix for a state machine | Tests that assert what the code does instead of what the contract says |
| Drafting docs and commit messages | Silently widening scope (adding fields or endpoints not in the contract) |
| Spotting missing edge cases when asked | Security shortcuts: logging request bodies, echoing input in errors |

## Review log

Each entry: what the AI produced, what was wrong, how it was caught, the fix.

| Unit | What happened | Caught by | Fix |
|---|---|---|---|
| 1–2 | The unit 1 commit message said `Guid.CreateVersion7()` keys are time-ordered, "so clustered indexes don't fragment the way random NEWID() keys do." True for Postgres, false for SQL Server: it sorts `uniqueidentifier` by bytes 10–15 first, and v7 puts the timestamp in bytes 0–5. | Reading the generated migration SQL (clustered `uniqueidentifier` PKs) and checking the claim against SQL Server's GUID comparison order. | Corrected CONTRACT.md §2. Kept v7 (still time-ordered in .NET, the warehouse and any future Postgres); at this scale page splits are acceptable. A real high-volume fix would be `NEWSEQUENTIALID()`-style keys or a clustered key on `(SubmittedAt, Id)`. |
| 2 | The first concurrency design put a `rowversion` on both `Orders` and `Searches`. That misses write skew: two requests completing the last two searches of one order touch different rows, both pass, and the order is never completed. | Walking through the concurrent-completion scenario before writing the persistence tests. | Concurrency token on the aggregate root only, with every operation changing the order row. Covered by an integration test. |
| 2 | The first version of that fix relied on `UpdatedAt` changing to force the order-row UPDATE. The integration test failed: both racing requests wrote the same timestamp, EF Core saw no change and skipped the UPDATE, so the token was never checked. | The write-skew integration test (it used one fixed clock, which is exactly the same-tick case). | Replaced the `rowversion` with a domain-managed `int Version` incremented by every operation: it always changes, so the check always runs. A unit test now asserts the increment. Lesson: a test with a fixed clock exposed an assumption a real clock would have hidden. |
| 2 | The join table was generated as `PackageSearchType` (singular class name, no `DbSet`), not the contract's `PackageSearchTypes`. | Reviewing the migration DDL against CONTRACT.md §4. | `ToTable("PackageSearchTypes")`, migration regenerated before commit. |
