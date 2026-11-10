# ScreenFlow — Build Sequence

Atomic build order, per [CONTRACT.md](CONTRACT.md). Each unit has exactly **one** verification check. Do not start unit N+1 until unit N's check passes. Each unit ends with a commit (or a short series of commits) pushed to `main`.

## Units

1. **Domain model and state machines** (`ScreenFlow.Domain`, no infrastructure dependencies).
   *Check:* the unit tests covering every allowed and every forbidden transition in CONTRACT.md §5 pass, and the solution builds with zero warnings (warnings are errors).

2. **Persistence: EF Core on SQL Server, encryption, seed data** (`ScreenFlow.Infrastructure`).
   *Check:* the persistence integration tests pass against a freshly migrated LocalDB database: round-trip of an order with its searches, SSN/DOB unreadable in raw SQL, duplicate `(ClientId, ClientReference)` rejected by the database, stale `RowVersion` rejected.

3. **REST API** (`ScreenFlow.Api`, minimal APIs).
   *Check:* every endpoint and every error case in CONTRACT.md §6 has a passing integration test (`WebApplicationFactory` against real SQL Server), and `dotnet test` exits 0 with no skipped tests.

4. **Continuous integration** (GitHub Actions, SQL Server service container).
   *Check:* a push to `main` produces a green workflow run that builds and runs the full test suite.

5. **Vendor results webhook** (HMAC-signed, idempotent; raw payload to MongoDB, normalized result to SQL Server).
   *Check:* an integration test delivers the same signed webhook twice and the search completes once, with one MongoDB document and one audit event.

6. **Warehouse ETL** (export completed searches to S3 as Parquet; Glue catalog; Athena star schema: `fact_search`, `dim_client`, `dim_search_type`, `dim_jurisdiction`, `dim_date`).
   *Check:* average turnaround hours by search type from Athena matches the same query run against SQL Server.

7. **Security and operations** (per-client API keys, structured logging with Serilog and PII redaction, health checks, `RUNBOOK.md`, one staged incident with a post-mortem in `docs/postmortems/`).
   *Check:* requests without a valid key get 401, and a test asserts that no log line contains an SSN.

8. **Status UI** (a small read-only page listing a client's orders and their searches).
   *Check:* the page renders a seeded client's orders from the running API.

9. **AI-assisted court-record normalization** (Claude turns free-text court record excerpts into structured charges with a confidence score; anything below threshold goes to a human review queue; the AI never sets a search result).
   *Check:* an evaluation over a fixed set of synthetic court records meets the documented accuracy bar, and low-confidence items land in the review queue.
