# Epic 02: Product Versions and Issuance

> Product versions become first-class admin-managed records and every key issuance flow selects one.

| | |
|---|---|
| **Epic id** | `02-product-versions-issuance` |
| **Tasks** | `E2-T1` … `E2-T4` |
| **Depends on** | `01-foundation-shell` |
| **Unlocks** | API compatibility and final gate |
| **Parallel with** | SMTP/docs tasks after menu foundation |

## Stack

Blazor Server · C# · MudBlazor · PostgreSQL · EF Core · xUnit.

| Task | Command |
|---|---|
| Build | `dotnet build LicensingSystem.sln` |
| Test | `dotnet test` |
| Test filtered | `dotnet test --filter <Name>` |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` passes before this epic is marked done.

## Directory subtree

```text
LicensingAdmin/Products/ProductService.cs
LicensingAdmin/Products/IProductStore.cs
LicensingAdmin/Pages/Admin/Products.razor
LicensingAdmin/Pages/Admin/EditProductDialog.razor
LicensingAdmin/Pages/Licenses/New.razor
LicensingAdmin/Pages/Licenses/BulkGenerate.razor
LicensingAdmin/Licensing/LicenseIssuanceService.cs
LicensingAdmin/Licensing/LicenseIssuanceRequest.cs
LicensingAdmin/Services/BulkLicenseService.cs
LicensingSystem.Tests/*ProductVersion*Tests.cs
LicensingSystem.Tests/LicenseIssuanceServiceTests.cs
```

## Tasks

### E2-T1 — Add product version service CRUD

**Files**
- `LicensingAdmin/Products/ProductService.cs`
- `LicensingAdmin/Products/IProductStore.cs`
- `LicensingSystem.Tests/ProductVersionServiceTests.cs`

**Do**
- Add methods to list versions by product, add a version, rename a version, and delete/disable a version.
- Refuse deleting a version referenced by any license; surface a domain exception rather than raw FK failure.
- Write `AuditLogEntry` for version create/update/delete-or-disable.

**Acceptance**
- WHEN a version name is submitted for a product THE SYSTEM SHALL persist a ProductVersion linked to that product and write audit.
- WHEN deleting a referenced ProductVersion THE SYSTEM SHALL refuse with a clear domain exception.

**Verify**
```bash
dotnet test --filter ProductVersion
```

**Checkpoint**: `step-05-version-service`

### E2-T2 — Update Products UI for versions

**Files**
- `LicensingAdmin/Pages/Admin/Products.razor`
- `LicensingAdmin/Pages/Admin/EditProductDialog.razor`

**Do**
- Show versions per product in `Products.razor`.
- Add inline or dialog-based add-version control.
- Keep legacy `CurrentVersion` clear: either hide it from primary UI or label it as legacy/display-only.
- Replace delete icon with archive action for products.

**Acceptance**
- WHEN admin opens Products THE SYSTEM SHALL show each product's version rows and an add-version control.
- WHEN admin archives a product THE SYSTEM SHALL confirm the action and remove it from the normal table.

**Verify**
```bash
dotnet build LicensingSystem.sln
```

**Checkpoint**: `step-06-products-ui`

### E2-T3 — Harden single license issuance

**Files**
- `LicensingAdmin/Licensing/LicenseIssuanceService.cs`
- `LicensingAdmin/Licensing/LicenseIssuanceRequest.cs`
- `LicensingAdmin/Pages/Licenses/New.razor`
- `LicensingSystem.Tests/LicenseIssuanceServiceTests.cs`

**Do**
- Existing product path requires non-empty `VersionId`.
- New product path creates a `Default` version and assigns license to it.
- Add status field to request so a key can be `Active` or `Test`.
- Preserve audit behavior.

**Acceptance**
- WHEN issuing for an existing product THE SYSTEM SHALL reject null or Guid.Empty VersionId before SaveChanges.
- WHEN issuing a new product THE SYSTEM SHALL create a Default ProductVersion and assign license.VersionId to it.
- WHEN issuing a key THE SYSTEM SHALL allow selecting Active or Test status.

**Verify**
```bash
dotnet test --filter LicenseIssuanceServiceTests
```

**Checkpoint**: `step-07-single-issuance`

### E2-T4 — Harden bulk license issuance

**Files**
- `LicensingAdmin/Pages/Licenses/BulkGenerate.razor`
- `LicensingAdmin/Services/BulkLicenseService.cs`
- `LicensingSystem.Tests/BulkLicenseServiceTests.cs`

**Do**
- Ensure bulk page has menu access from Epic 01.
- Require product and ProductVersion before generation.
- Add status selector (`Active`/`Test`) and pass it into each generated license request.
- Excel export includes license key, version id/name, and status.

**Acceptance**
- WHEN bulk page is opened THE SYSTEM SHALL require product and ProductVersion before generation.
- WHEN bulk keys are generated THE SYSTEM SHALL use the selected VersionId and selected Active/Test status for every key.

**Verify**
```bash
dotnet build LicensingSystem.sln
dotnet test --filter BulkLicense
```

**Checkpoint**: `step-08-bulk-issuance`
