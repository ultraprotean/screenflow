# ScreenFlow — Contract

ScreenFlow is a background-screening order pipeline. An employer (a **client**) orders a screening **package** for a job **candidate**; the package expands into individual **searches** (criminal county, motor vehicle record, drug test, verifications); each search moves through its own lifecycle, and the order's status follows from its searches. Every state change is audited, sensitive identifiers are encrypted at rest, and completed work flows into an analytics warehouse.

All data is synthetic. ScreenFlow is a portfolio project and is not affiliated with any screening company.

## 1. Acceptance criteria

The project is complete when all of the following hold:

- [ ] An order can be placed for a candidate under a package, and it expands into one search per package search type (criminal county searches use the candidate's county).
- [ ] Placing the same order twice (same client, same `clientReference`) returns the original order instead of creating a duplicate.
- [ ] Order and search statuses change only through the state machines in §5; an illegal transition returns `INVALID_TRANSITION` and changes nothing.
- [ ] Concurrent updates to the same order are detected (optimistic concurrency) and return `CONCURRENCY_CONFLICT` rather than losing a write.
- [ ] SSN and date of birth are encrypted at rest (unreadable in raw SQL), never logged, and never returned in full by the API.
- [ ] Every status change writes an append-only audit event.
- [ ] List endpoints use cursor (keyset) pagination.
- [ ] Every endpoint and every error case in §6 has a passing integration test against a real SQL Server; `dotnet test` exits 0 with no skipped tests.
- [ ] CI builds and tests every push to `main`.
- [ ] Vendor results arrive through a signed, idempotent webhook; raw payloads are kept in MongoDB, normalized results in SQL Server. *(unit 5)*
- [ ] Completed searches are exported to S3 as Parquet and queryable in Athena through a star schema. *(unit 6)*
- [ ] Git history shows small, sequential, reviewable units that match BUILD_SEQUENCE.md.

## 2. Conventions

- Base path: `/api/v1` for every endpoint in §6 except the health check (`GET /health`).
- JSON everywhere, camelCase property names. Enums serialize as strings (`"InProgress"`, not `2`).
- Timestamps are UTC, ISO 8601 (`DateTimeOffset` in C#, `datetimeoffset` in SQL Server).
- Identifiers are GUIDs (`Guid.CreateVersion7()`, which is time-ordered and so index-friendly).
- Authentication: none in units 1–4 (the API runs locally only). Unit 7 adds an API key per client. This is a deliberate, documented gap, not an oversight.

### Error envelope

All errors use RFC 9457 Problem Details (`application/problem+json`) with a `code` extension so clients can branch on a stable string:

```json
{
  "type": "https://screenflow.dev/errors/invalid-transition",
  "title": "Invalid status transition",
  "status": 409,
  "detail": "Search 0193... cannot move from Completed to InProgress.",
  "code": "INVALID_TRANSITION",
  "traceId": "00-4bf9..."
}
```

Validation errors add an `errors` object (field name → messages), as ASP.NET Core's `ValidationProblemDetails` does.

| Code | HTTP | When |
|---|---|---|
| `VALIDATION_FAILED` | 400 | The request body or query fails validation |
| `NOT_FOUND` | 404 | The client, package, order or search does not exist (or the search is not on that order) |
| `INVALID_TRANSITION` | 409 | The state machine (§5) forbids the requested change |
| `CONCURRENCY_CONFLICT` | 409 | The row changed since it was read (`rowversion` mismatch) |
| `INTERNAL_ERROR` | 500 | Anything unexpected; the response never includes exception details |

## 3. Sensitive data rules

- **SSN** and **date of birth** are encrypted in the application with AES-256-GCM before they reach SQL Server (an EF Core value converter). Each value gets a random 96-bit nonce; the stored format is `nonce ‖ ciphertext ‖ tag`, `varbinary`. The key comes from configuration (`Encryption:Key`, base64, 32 bytes): user-secrets locally, an environment variable in CI, never the repository.
- `SsnLast4` is stored in plain text so the API can display `***-**-1234` without decrypting.
- The API **accepts** a full SSN and DOB on order creation and **never returns** either. Responses carry `ssnMasked` only.
- Logs never contain SSN or DOB. Request bodies are not logged.
- Test and seed data use SSNs in the 900–999 area range, which the Social Security Administration never issues.

*Why application-level encryption rather than SQL Server Always Encrypted?* It works identically on LocalDB, in CI and in any cloud database, and it makes the key-handling boundary explicit in code. Always Encrypted is the production upgrade path (see `docs/decisions/`).

## 4. Data model (SQL Server, EF Core)

| Table | Purpose | Key columns |
|---|---|---|
| `Clients` | Employers who order screenings | `Id`, `Name` (unique), `Industry`, `CreatedAt` |
| `Packages` | Catalog of screening packages (seeded) | `Id`, `Code` (unique), `Name` |
| `PackageSearchTypes` | Which search types a package contains | `PackageId`, `SearchType` (composite PK) |
| `Candidates` | The person being screened | `Id`, `FirstName`, `LastName`, `Email`, `SsnEncrypted`, `SsnLast4`, `DateOfBirthEncrypted`, `State`, `County`, `CreatedAt` |
| `Orders` | One screening request | `Id`, `ClientId`, `CandidateId`, `PackageId`, `ClientReference`, `Status`, `Result`, `SubmittedAt`, `CompletedAt`, `RowVersion` |
| `Searches` | One unit of screening work | `Id`, `OrderId`, `Type`, `Jurisdiction`, `Status`, `Result`, `CreatedAt`, `StartedAt`, `CompletedAt`, `RowVersion` |
| `AuditEvents` | Append-only history | `Id` (bigint identity), `OrderId`, `SearchId` (nullable), `Action`, `FromStatus`, `ToStatus`, `Actor`, `OccurredAt` |

- Unique index on `Orders (ClientId, ClientReference)`: this is what makes order creation idempotent.
- Index on `Orders (ClientId, Status, SubmittedAt, Id)` for the list endpoint's filter and keyset paging.
- Index on `Searches (OrderId)`, `Searches (Status, Type)` (operations queue), `AuditEvents (OrderId, Id)`.
- Status and type enums are stored as strings (`nvarchar(32)`) so raw SQL and the warehouse export are readable.
- `RowVersion` (`rowversion`) on `Orders` and `Searches` is EF Core's concurrency token.

**Enums**

- `Industry`: `Transportation`, `Healthcare`, `Energy`, `Construction`, `Other`
- `SearchType`: `CriminalCounty`, `CriminalFederal`, `MotorVehicleRecord`, `DrugTest`, `EmploymentVerification`, `EducationVerification`
- `OrderStatus`: `Submitted`, `InProgress`, `ReadyForReview`, `Completed`, `Cancelled`
- `SearchStatus`: `Pending`, `InProgress`, `Completed`, `Cancelled`
- `Result`: `Clear`, `Consider` (FCRA vocabulary: "consider" means a finding the employer must review, not a failure)

**Seeded packages**

| Code | Name | Search types |
|---|---|---|
| `STANDARD` | Standard Employment | CriminalCounty, CriminalFederal, EmploymentVerification |
| `DOT_DRIVER` | DOT Driver | CriminalCounty, MotorVehicleRecord, DrugTest, EmploymentVerification |
| `HEALTHCARE` | Healthcare | CriminalCounty, CriminalFederal, DrugTest, EducationVerification |

`CriminalCounty` searches get `Jurisdiction = "{State}-{County}"` from the candidate (e.g. `AZ-Maricopa`); `CriminalFederal` gets `"{State}"`; the others get `null`.

## 5. State machines

These live in the domain layer (`ScreenFlow.Domain`), with no database or HTTP dependency, and are the **only** way statuses change.

**Search**

```
Pending ──start──▶ InProgress ──complete(result)──▶ Completed
   │                    │
   └──────cancel────────┴──▶ Cancelled
```

**Order** (driven by its searches, except cancel and review)

- `Submitted` → `InProgress` automatically when its first search starts.
- `InProgress` → `Completed` (result `Clear`) automatically when every non-cancelled search is `Completed` with `Clear`.
- `InProgress` → `ReadyForReview` automatically when every non-cancelled search is `Completed` and at least one is `Consider`.
- `ReadyForReview` → `Completed` through a reviewer decision (`Clear` or `Consider`).
- `Submitted` or `InProgress` → `Cancelled` on request; this cancels every search not yet `Completed`.
- `Completed` and `Cancelled` are terminal.

Each transition, automatic or requested, writes one `AuditEvents` row.

## 6. Endpoints

### 6.1 `GET /health`
Liveness and database readiness. `200 {"status":"Healthy"}` or `503`.

### 6.2 `POST /api/v1/clients`
Body `{ "name": string (1–200), "industry": Industry }` → `201` with the client and a `Location` header. Duplicate name → `VALIDATION_FAILED`.

### 6.3 `GET /api/v1/clients/{clientId}`
→ `200` client, or `NOT_FOUND`.

### 6.4 `GET /api/v1/packages`
→ `200` array of `{ "id", "code", "name", "searchTypes": [SearchType] }`.

### 6.5 `POST /api/v1/orders`
```json
{
  "clientId": "guid",
  "packageCode": "DOT_DRIVER",
  "clientReference": "REQ-10442",
  "candidate": {
    "firstName": "Dana", "lastName": "Reyes", "email": "dana@example.com",
    "ssn": "900-12-3456", "dateOfBirth": "1990-04-02",
    "state": "AZ", "county": "Maricopa"
  }
}
```
- New → `201` with the order (§6.6 shape) and `Location`.
- Same `clientId` + `clientReference` already exists → `200` with the existing order (idempotent replay; the body is not compared).
- Unknown client or package → `NOT_FOUND`. Bad SSN format (`NNN-NN-NNNN` or 9 digits), future DOB, candidate under 14, missing fields, `clientReference` over 64 chars → `VALIDATION_FAILED`.

### 6.6 `GET /api/v1/orders/{orderId}`
```json
{
  "id": "guid", "clientId": "guid", "clientReference": "REQ-10442", "packageCode": "DOT_DRIVER",
  "status": "InProgress", "result": null,
  "submittedAt": "...", "completedAt": null,
  "candidate": { "firstName": "Dana", "lastName": "Reyes", "email": "dana@example.com", "ssnMasked": "***-**-3456", "state": "AZ", "county": "Maricopa" },
  "searches": [
    { "id": "guid", "type": "CriminalCounty", "jurisdiction": "AZ-Maricopa", "status": "Completed", "result": "Clear", "startedAt": "...", "completedAt": "..." }
  ]
}
```

### 6.7 `GET /api/v1/orders?clientId=&status=&limit=&cursor=`
- `clientId` required; `status` optional; `limit` 1–100, default 25.
- Newest first by `(submittedAt, id)`. Response `{ "items": [order summary], "nextCursor": string|null }`; the cursor is opaque (base64url).
- Order summary = §6.6 without `candidate` and `searches`, plus `candidateName`.
- Bad cursor or limit → `VALIDATION_FAILED`.

### 6.8 `POST /api/v1/orders/{orderId}/searches/{searchId}/start`
→ `200` order (§6.6). Illegal → `INVALID_TRANSITION`.

### 6.9 `POST /api/v1/orders/{orderId}/searches/{searchId}/complete`
Body `{ "result": "Clear" | "Consider" }` → `200` order. Illegal → `INVALID_TRANSITION`.

*(6.8 and 6.9 are the operator endpoints for unit 3. Unit 5's vendor webhook calls the same domain operations.)*

### 6.10 `POST /api/v1/orders/{orderId}/review`
Body `{ "decision": "Clear" | "Consider", "reviewer": string }` → `200` order. Only from `ReadyForReview`.

### 6.11 `POST /api/v1/orders/{orderId}/cancel`
→ `200` order. Only from `Submitted` or `InProgress`.

### 6.12 `GET /api/v1/orders/{orderId}/audit`
→ `200` array of `{ "id", "searchId", "action", "fromStatus", "toStatus", "actor", "occurredAt" }`, oldest first.

The actor is `"system"` for automatic transitions, `"operator"` for 6.8–6.9 and 6.11, and the `reviewer` value for 6.10, until authentication arrives in unit 7.
