# Epic 01: Foundation Shell

> The admin surface becomes reachable, status-tracked, and ready for archive-aware product management.

| | |
|---|---|
| **Epic id** | `01-foundation-shell` |
| **Tasks** | `E1-T1` … `E1-T4` |
| **Depends on** | nothing — start here |
| **Unlocks** | `02-product-versions-issuance`, `03-api-smtp-docs-compatibility` |
| **Parallel with** | nothing for first task; later tasks can branch after status log |

## Stack

Blazor Server · C# · MudBlazor · PostgreSQL · EF Core · cookie auth · LXC/systemd.

| Task | Command |
|---|---|
| Restore | `dotnet restore LicensingSystem.sln` |
| Build | `dotnet build LicensingSystem.sln` |
| Test | `dotnet test` |
| Test filtered | `dotnet test --filter <Name>` |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` passes before this epic is marked done.

## Directory subtree

```text
LicensingAdmin/Shared/MainLayout.razor
LicensingAdmin/Products/ProductService.cs
LicensingAdmin/Products/IProductStore.cs
LicensingCore/Entities/SoftwareProduct.cs
LicensingCore/Data/AppDbContext.cs
LicensingApi/Migrations/001_initial_schema.sql
infra/migrate-v2-admin-hardening.sql
docs/v2-admin-hardening-status.md
LicensingSystem.Tests/ProductServiceTests.cs
```

## Tasks

### E1-T1 — Add v2 hardening status log

**Files**
- `docs/v2-admin-hardening-status.md`

**Do**
- Create the status document with a table for all tasks from `tasks.json`.
- Include columns: task id, status, files changed, verification, notes.
- Mark this task `in_progress` then `done` as work proceeds.

**Acceptance**
- WHEN the repository opens THE SYSTEM SHALL contain docs/v2-admin-hardening-status.md with a task/status table.

**Verify**
```bash
test -f docs/v2-admin-hardening-status.md
```

**Checkpoint**: `step-01-status-log`

### E1-T2 — Expose v2 admin menu links

**Files**
- `LicensingAdmin/Shared/MainLayout.razor`

**Do**
- Add menu link to `licenses/bulk` under `AuthPolicies.IssueAccess`.
- Add menu link to `admin/docs` under `AuthPolicies.IssueAccess` or ViewerAccess if docs should be visible to readers.
- Keep SMTP and Users under `AdminUserAccess`.
- Do not weaken page-level `[Authorize]` attributes.

**Acceptance**
- WHEN MainLayout renders for IssueAccess THE SYSTEM SHALL show links to single license, bulk licenses, products, and docs.
- WHEN MainLayout renders for AdminUserAccess THE SYSTEM SHALL show SMTP and users links.

**Verify**
```bash
dotnet build LicensingSystem.sln
```

**Checkpoint**: `step-02-menu-links`

### E1-T3 — Add additive archive migration

**Files**
- `infra/migrate-v2-admin-hardening.sql`
- `LicensingCore/Entities/SoftwareProduct.cs`
- `LicensingCore/Data/AppDbContext.cs`
- `LicensingApi/Migrations/001_initial_schema.sql`

**Do**
- Add `SoftwareProduct.IsArchived` boolean property.
- Map it to `software_products.is_archived` with default false.
- Update initial schema SQL so fresh installs match.
- Add idempotent migration SQL with verification SELECTs.

**Acceptance**
- WHEN the migration is run twice THE SYSTEM SHALL keep software_products.is_archived present without duplicate-object failure.
- WHEN verification SELECTs run THE SYSTEM SHALL report archived column present.

**Verify**
```bash
dotnet build LicensingSystem.sln
```

**Checkpoint**: `step-03-product-archive-schema`

### E1-T4 — Implement product archive service

**Files**
- `LicensingAdmin/Products/ProductService.cs`
- `LicensingAdmin/Products/IProductStore.cs`
- `LicensingSystem.Tests/ProductServiceTests.cs`

**Do**
- Replace normal UI delete flow with archive flow.
- Keep hard delete only if explicitly retained as an internal method; do not expose as primary UI action.
- `ListAsync()` excludes archived products by default.
- Add audit entry with action `Updated` or `Archived`; if introducing `Archived`, document it in project rules/status.

**Acceptance**
- WHEN ProductService archives a product THE SYSTEM SHALL set IsArchived true and write an AuditLogEntry with actor email.
- WHEN products are listed for normal admin use THE SYSTEM SHALL exclude archived products by default.

**Verify**
```bash
dotnet test --filter ProductService
```

**Checkpoint**: `step-04-product-archive-service`
