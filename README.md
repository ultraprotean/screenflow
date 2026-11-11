# ScreenFlow

[![CI](https://github.com/ultraprotean/screenflow/actions/workflows/ci.yml/badge.svg)](https://github.com/ultraprotean/screenflow/actions/workflows/ci.yml)

A background-screening order pipeline in .NET 10: an employer orders a screening package for a candidate, the package expands into searches (criminal county, motor vehicle record, drug test, verifications), each search moves through a state machine, and every change is audited. Sensitive identifiers are encrypted at rest.

All data is synthetic. This is a portfolio project, not affiliated with any screening company.

## Stack

- ASP.NET Core minimal APIs, .NET 10, C#
- EF Core 10 on SQL Server (LocalDB for development)
- xUnit integration tests against real SQL Server
- GitHub Actions CI
- Planned: MongoDB (raw vendor payloads), AWS S3 + Glue + Athena (warehouse), Claude (court-record normalization with human review)

## How it was built

Spec first, then one unit at a time:

- [CONTRACT.md](CONTRACT.md): data model, state machines, endpoints, error codes
- [BUILD_SEQUENCE.md](BUILD_SEQUENCE.md): nine units, one verification check each
- [docs/AI_WORKFLOW.md](docs/AI_WORKFLOW.md): how AI was used, and what review caught

Read the history in order with `git log --reverse`.

## Run it

Requires the .NET 10 SDK and SQL Server (LocalDB ships with Visual Studio and SQL Server Express).

```bash
dotnet test                       # creates and drops a throwaway LocalDB database
```

The API needs a 32-byte encryption key for SSN and date-of-birth columns. Generate one into user-secrets (never into source control), then run; in Development the database is created and migrated on startup:

```bash
dotnet user-secrets set "Encryption:Key" "$(openssl rand -base64 32)" --project src/ScreenFlow.Api
dotnet run --project src/ScreenFlow.Api   # http://localhost:5129
```

`src/ScreenFlow.Api/ScreenFlow.Api.http` has a request for every endpoint.
