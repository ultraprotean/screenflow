# CLAUDE.md

Guidance for Claude Code in this repository.

## What this is

ScreenFlow is a background-screening order pipeline: ASP.NET Core (.NET 10) REST API, EF Core on SQL Server, later MongoDB for raw vendor payloads and an AWS warehouse (S3, Glue, Athena). It is a portfolio project built by Jeff (senior C#/.NET Framework engineer, modernizing his stack) with Claude Code. All data is synthetic; it is not affiliated with any screening company.

## Source-of-truth documents

Read before writing code. They are the spec, not background reading.

- **[CONTRACT.md](CONTRACT.md)**: data model, state machines, endpoints, error codes, sensitive-data rules.
- **[BUILD_SEQUENCE.md](BUILD_SEQUENCE.md)**: the 9 units, one verification check each. Do not start unit N+1 until unit N's check passes.
- **[docs/AI_WORKFLOW.md](docs/AI_WORKFLOW.md)**: how AI is used here and what was caught in review. Add an entry whenever AI output is wrong or needed correcting.

When a decision changes the contract, update CONTRACT.md in the same commit.

## How to work

- Small, reviewable commits. Message format: `unit N: <topic>` (or `docs:`, `fix:`, `chore:`), then a short "Key points" list: what changed, why, and how it differs from .NET Framework where that helps Jeff learn.
- Comment code briefly where a concept is being taught; deeper explanation goes in commit messages.
- Build and test after every change. When testing reveals a bug, fix it and say so in the commit: those are interview stories.
- You make most decisions and explain the reasoning. Ask Jeff only when the choice is genuinely his.

## Never-do rules

- Never use real personal data. Test SSNs use the 900–999 area range.
- Never log, return or put in an exception message a full SSN or date of birth.
- Never change an order or search status outside the domain state machines (`ScreenFlow.Domain`).
- Never update or delete `AuditEvents` rows.
- Never edit a migration that has been committed; add a new one.
- Never block on async code (`.Result`, `.Wait()`); pass `CancellationToken` through.
- Never commit secrets. The encryption key lives in user-secrets locally and environment variables in CI.
- Never mock the database in integration tests: they run against real SQL Server.

## Layout

```
src/ScreenFlow.Domain          entities, enums, state machines, domain exceptions (no dependencies)
src/ScreenFlow.Infrastructure  EF Core DbContext, configurations, migrations, encryption, seeding
src/ScreenFlow.Api             minimal API endpoints, contracts (DTOs), validation, error mapping
tests/ScreenFlow.UnitTests     domain tests (fast, no I/O)
tests/ScreenFlow.IntegrationTests  persistence and API tests against SQL Server
```

## Commands

```bash
dotnet build                     # warnings are errors (Directory.Build.props)
dotnet test                      # everything
dotnet test tests/ScreenFlow.UnitTests
dotnet test tests/ScreenFlow.IntegrationTests   # needs SQL Server (LocalDB on Windows)
dotnet run --project src/ScreenFlow.Api
```
