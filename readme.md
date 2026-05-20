# Customer Entitlement API

A graph-backed entitlement check service built on **ASP.NET Core 8**, **Neo4j AuraDB**, and **MediatR**.  
It answers one question for any calling application:

> _"Is subject **X** allowed to perform action **Y** on resource **Z** (optionally via channel **C**)?"_

---

## Architecture

```
CustomerEntitlement.sln
├── CustomerEntitlement.Api/          ← single Web API project (Clean Architecture folders)
│   ├── Domain/                       ← POCO records — Party, Entitlement, Channel, …
│   ├── Application/                  ← MediatR query / result / handler / repository interface
│   ├── Infrastructure/               ← Neo4j driver implementation + startup seed service
│   └── Api/Controllers/              ← EntitlementsController (POST /api/entitlements/evaluate)
└── CustomerEntitlement.Tests/        ← xUnit unit tests (repository mocked with Moq)
```

### Key design choices

| Concern | Decision |
|---|---|
| Graph traversal | Custom Cypher — no pre-built authorisation library |
| CQRS | MediatR 14 — controller calls `_mediator.Send()` only |
| Graph DB | Neo4j.Driver 6 against AuraDB Free Tier |
| Auth | None — MVP, HTTP only |
| Seed | `IHostedService` seeds demo graph on first startup if DB is empty |

---

## BIAN Graph Model

```
Party ──[:OWNS]──► CustomerAccessProfile
                         │
               ┌─────────┴──────────┐
         [:HAS_ENTITLEMENT]    [:HAS_LIMIT]
               │                    │
          Entitlement         UtilizationLimit
               │                    │
        [:VIA_CHANNEL]       [:VIA_CHANNEL]
               │                    │
           Channel               Channel
               │                    │
       [:GRANTS_ACCESS]      [:CONSTRAINS]
               │                    │
          Service /          ProductInstance
         ProductInstance
```

### Demo data (seeded automatically)

| Node | id | Notes |
|---|---|---|
| Party | `P-001` | Kiran Jivan — fully connected |
| Party | `P-002` | Mark — isolated (party + profile exist, no edges) |
| Entitlement | `ENT-01` | `ServiceRequest` via `CH-CALL` → `SRV-STOP`, `ACC-1001` |
| Entitlement | `ENT-02` | `ExecuteTransfer` via `CH-MOB` → `ACC-1001`, `ACC-2002` |
| UtilizationLimit | `LIM-01` | $500 NZD / 24h via `CH-ATM` → `ACC-1001`, `ACC-2002` |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8) (or .NET 10 SDK targeting net8.0)
- Access to the Neo4j AuraDB instance configured in `appsettings.json`

---

## Running the API

```bash
cd CustomerEntitlement.Api
dotnet run
```

Swagger UI: [http://localhost:5000/swagger](http://localhost:5000/swagger)

> **First run:** the startup seed service checks whether the database is empty.  
> If it is, it loads the demo graph automatically.  
> If the AuraDB instance already contains data, seeding is skipped.

---

## Configuration

Credentials live in `appsettings.json`:

```json
"Neo4j": {
  "Uri":      "neo4j+s://536e0c5a.databases.neo4j.io",
  "Username": "536e0c5a",
  "Password": "...",
  "Database": "536e0c5a"
}
```

> **AuraDB note:** the `Database` field must match the **instance ID** (e.g. `536e0c5a`), not the  
> string `"neo4j"`. Both `Username` and `Database` use the instance ID on AuraDB Free.  
> If you see a `database not found` error, verify this field matches your instance ID exactly.

For production, replace the JSON values with environment variables:

```bash
Neo4j__Uri=neo4j+s://...
Neo4j__Username=...
Neo4j__Password=...
Neo4j__Database=neo4j
```

---

## Entitlement Check Endpoint

### `POST /api/entitlements/evaluate`

**Request body**

```json
{
  "subjectId":  "P-001",
  "action":     "ServiceRequest",
  "resourceId": "SRV-STOP",
  "channelId":  "CH-CALL"
}
```

| Field | Required | Description |
|---|---|---|
| `subjectId` | ✓ | `Party.id` of the identity being checked |
| `action` | ✓ | Permission name (matches `Entitlement.action`) |
| `resourceId` | ✓ | Target node id (`Service.id` or `ProductInstance.id`) |
| `channelId` | optional | If provided, the entitlement must also be reachable via this channel |

**Response — allow**

```json
{
  "isAllowed":   true,
  "reason":      "Access granted.",
  "grantedTerm": "CallCenterServicing",
  "details": {
    "profileId":       "PROF-001",
    "channelValidated": true
  }
}
```

**Response — deny**

```json
{
  "isAllowed":   false,
  "reason":      "Profile 'PROF-002' is not active (status: Pending).",
  "grantedTerm": null,
  "details": {
    "profileId":       "PROF-002",
    "channelValidated": false
  }
}
```

### Example scenarios

| subjectId | action | resourceId | channelId | Expected |
|---|---|---|---|---|
| `P-001` | `ServiceRequest` | `SRV-STOP` | `CH-CALL` | **Allow** — CallCenterServicing |
| `P-001` | `ExecuteTransfer` | `ACC-2002` | `CH-MOB` | **Allow** — MobileTransfer |
| `P-001` | `ServiceRequest` | `SRV-STOP` | `CH-MOB` | **Deny** — wrong channel |
| `P-001` | `ExecuteTransfer` | `SRV-STOP` | | **Deny** — resource not accessible |
| `P-002` | `ServiceRequest` | `SRV-STOP` | | **Deny** — no access profile |
| `P-999` | `ServiceRequest` | `SRV-STOP` | | **Deny** — subject not found |

---

## Running the Tests

```bash
cd CustomerEntitlement.Tests
dotnet test
```

39 tests covering: all evaluator branches (grant + 6 denial paths), domain model round-trips, infrastructure mocks (repository + seed service), and WebApplicationFactory startup/endpoint smoke tests. Line coverage: 92.6%.

---

## Denial reason ladder

The Cypher traversal uses cascading `OPTIONAL MATCH` steps. On a miss at any step, the repository returns a specific reason:

1. `"Subject '{id}' not found."` — Party node absent
2. `"No Customer Access Profile found for subject '{id}'."` — no `[:OWNS]` edge
3. `"Profile '{id}' is not active (status: …)."` — profile exists but status ≠ Active
4. `"No entitlement found for action '{action}' on profile '{id}'."` — no matching `Entitlement.action`
5. `"Action '{action}' does not grant access to resource '{resourceId}'."` — `[:GRANTS_ACCESS]` edge missing
6. `"Action '{action}' is not permitted via channel '{channelId}'. Allowed: […]."` — channel mismatch
7. `"Access granted."` — full path resolved ✓
