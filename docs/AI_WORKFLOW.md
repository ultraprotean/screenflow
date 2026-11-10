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
| | | | |
