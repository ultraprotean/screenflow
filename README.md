# ScreenFlow

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
dotnet test
dotnet run --project src/ScreenFlow.Api
```
