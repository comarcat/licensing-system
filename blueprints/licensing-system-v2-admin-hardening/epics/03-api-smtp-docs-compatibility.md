# Epic 03: API, SMTP, Docs, and Compatibility

> Test-mode activation, SMTP diagnostics, docs portal, and old/new client compatibility become verifiable.

| | |
|---|---|
| **Epic id** | `03-api-smtp-docs-compatibility` |
| **Tasks** | `E3-T1` … `E3-T7` |
| **Depends on** | `01-foundation-shell`, selected tasks from `02-product-versions-issuance` |
| **Unlocks** | release |
| **Parallel with** | product-version UI where files do not overlap |

## Stack

ASP.NET Core Web API · Blazor Server · MailKit · EF Core · xUnit.

| Task | Command |
|---|---|
| Build | `dotnet build LicensingSystem.sln` |
| Test | `dotnet test` |
| Test filtered | `dotnet test --filter <Name>` |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` passes before this epic is marked done.

## Directory subtree

```text
LicensingApi/Services/ActivationService.cs
LicensingCore/Entities/Enums.cs
LicensingCore/Entities/EmailLogEntry.cs
LicensingCore/Data/AppDbContext.cs
LicensingAdmin/Notifications/NotificationConfigService.cs
LicensingAdmin/Pages/Admin/NotificationSettings.razor
LicensingAdmin/Pages/Admin/Docs.razor
LicensingAdmin/wwwroot/docs/README.md
ActivationHelloWorld/Program.cs
ActivationHelloWorld/ApiClient.cs
docs/activation-dll-integration-reference.md
docs/v2-admin-hardening-status.md
infra/README.md
LicensingSystem.Tests/*Compatibility*Tests.cs
```

## Tasks

### E3-T1 — Implement test-license API bypass

**Files**
- `LicensingCore/Entities/Enums.cs`
- `LicensingApi/Services/ActivationService.cs`
- `LicensingSystem.Tests/ActivationServiceTests.cs`

**Do**
- Add `LicenseStatus.Test`.
- In `ActivationService`, bypass max-activation limit, pending review, and subscription checks only for Test licenses.
- Preserve Revoked behavior.
- Preserve Active behavior with tests proving unchanged enforcement.

**Acceptance**
- WHEN a Test license is activated THE SYSTEM SHALL approve activation without max-activation, review, or subscription blocks.
- WHEN an Active license is activated THE SYSTEM SHALL preserve existing rule enforcement.

**Verify**
```bash
dotnet test --filter ActivationService
```

**Checkpoint**: `step-09-test-license-api`

### E3-T2 — Add SMTP diagnostics

**Files**
- `LicensingAdmin/Notifications/NotificationConfigService.cs`
- `LicensingAdmin/Pages/Admin/NotificationSettings.razor`
- `LicensingSystem.Tests/NotificationConfigServiceTests.cs`

**Do**
- Add structured logging around SMTP test send.
- Return or surface sanitized diagnostic text to the page.
- Log full exception with `ILogger`, but never log password or decrypted secret.
- Include host, port, encryption, auth type, exception type, elapsed ms, and recipient domain.

**Acceptance**
- WHEN test email fails THE SYSTEM SHALL show a sanitized failure reason in the UI.
- WHEN test email fails THE SYSTEM SHALL log exception type, host, port, encryption, auth type, elapsed ms, and recipient domain without logging password.

**Verify**
```bash
dotnet test --filter NotificationConfigServiceTests
```

**Checkpoint**: `step-10-smtp-diagnostics`

### E3-T3 — Add email audit foundation

**Files**
- `LicensingCore/Entities/EmailLogEntry.cs`
- `LicensingCore/Data/AppDbContext.cs`
- `LicensingAdmin/Notifications/NotificationConfigService.cs`
- `infra/migrate-v2-admin-hardening.sql`
- `LicensingSystem.Tests/EmailLogTests.cs`

**Do**
- Add `EmailLogEntry` entity and mapping.
- Record test-send and future key-send attempts.
- Include recipient, type, subject, status, sanitized error, created timestamp, optional license/product ids.
- Add idempotent SQL table creation.

**Acceptance**
- WHEN a test or key email send is attempted THE SYSTEM SHALL record recipient, status, type, timestamp, and sanitized error if any.
- WHEN email audit persists THE SYSTEM SHALL not store SMTP password or private key material.

**Verify**
```bash
dotnet test --filter EmailLog
```

**Checkpoint**: `step-11-email-audit`

### E3-T4 — Add admin docs portal

**Files**
- `LicensingAdmin/Pages/Admin/Docs.razor`
- `LicensingAdmin/Shared/MainLayout.razor`
- `LicensingAdmin/wwwroot/docs/README.md`

**Do**
- Create `/admin/docs` page with `[Authorize]` policy.
- List latest integration reference, API docs, HelloWorld sample guidance, and DLL/client package links.
- Ensure static docs are served from admin app.
- Link it from the admin menu.

**Acceptance**
- WHEN admin opens Docs THE SYSTEM SHALL list integration docs and downloadable client artifacts.
- WHEN a doc link is clicked THE SYSTEM SHALL return a static file from the admin app.

**Verify**
```bash
dotnet build LicensingSystem.sln
```

**Checkpoint**: `step-12-docs-portal`

### E3-T5 — Update HelloWorld compatibility harness

**Files**
- `ActivationHelloWorld/Program.cs`
- `ActivationHelloWorld/ApiClient.cs`
- `docs/activation-dll-integration-reference.md`

**Do**
- Add CLI/config option for `VersionId`.
- Add CLI/config option to omit `VersionId` completely for old-schema mode.
- Document both modes.
- Keep DTO optional and retrocompatible.

**Acceptance**
- WHEN HelloWorld runs in new mode THE SYSTEM SHALL send VersionId in ActivateRequest.
- WHEN HelloWorld runs in old-schema mode THE SYSTEM SHALL omit VersionId from the activation payload.

**Verify**
```bash
dotnet build LicensingSystem.sln
```

**Checkpoint**: `step-13-helloworld-harness`

### E3-T6 — Add API compatibility tests

**Files**
- `LicensingSystem.Tests/ActivationCompatibilityTests.cs`

**Do**
- Add tests for matching VersionId success.
- Add tests for mismatched VersionId returning `InvalidKeyFormat`.
- Add tests for old payload with no VersionId activating a default-version license.
- Add tests proving response envelope/result codes stay stable.

**Acceptance**
- WHEN compatibility tests run THE SYSTEM SHALL prove matching VersionId activates and mismatched VersionId returns InvalidKeyFormat.
- WHEN compatibility tests run THE SYSTEM SHALL prove old-schema payload without VersionId still activates default-version license.

**Verify**
```bash
dotnet test --filter Compatibility
```

**Checkpoint**: `step-14-api-compatibility`

### E3-T7 — Finalize docs and gate

**Files**
- `docs/activation-dll-integration-reference.md`
- `docs/v2-admin-hardening-status.md`
- `infra/README.md`

**Do**
- Update docs for menu modules, version management, test mode, SMTP diagnostics, docs portal, archive behavior, and compatibility testing.
- Update `infra/README.md` with PowerShell deploy and correct DB host/user facts.
- Run final build/test gate.

**Acceptance**
- WHEN final gate runs THE SYSTEM SHALL build and test successfully.
- WHEN docs are reviewed THE SYSTEM SHALL describe menu modules, version management, test mode, SMTP diagnostics, docs portal, archive behavior, and compatibility testing.

**Verify**
```bash
dotnet build LicensingSystem.sln
dotnet test
```

**Checkpoint**: `step-15-final-docs-gate`
