# v2 Admin Hardening — Activity and Status

Last updated: 2026-10-08

| Task | Status | Files changed | Verification | Notes / risks |
|---|---|---|---|---|
| E1-T1 Status log | done | `docs/v2-admin-hardening-status.md`, `blueprints/licensing-system-v2-admin-hardening/tasks.json` | `test -f docs/v2-admin-hardening-status.md` ✅ | Created activity/status table and marked task done |
| E1-T2 Menu links | done | `LicensingAdmin/Shared/MainLayout.razor`, `LicensingAdmin/Pages/Admin/Docs.razor`, `docs/v2-admin-hardening-status.md`, `blueprints/licensing-system-v2-admin-hardening/tasks.json` | `dotnet build LicensingSystem.sln` ✅ | Added bulk and docs links; created placeholder docs route so menu target works |
| E1-T3 Archive schema | done | `LicensingCore/Entities/SoftwareProduct.cs`, `LicensingCore/Data/AppDbContext.cs`, `LicensingApi/Migrations/001_initial_schema.sql`, `infra/migrate-v2-admin-hardening.sql`, `docs/v2-admin-hardening-status.md`, `blueprints/licensing-system-v2-admin-hardening/tasks.json` | `dotnet build LicensingSystem.sln` ✅ | Added `software_products.is_archived` mapping and idempotent SQL migration |
| E1-T4 Archive service | done | `LicensingAdmin/Products/ProductService.cs`, `LicensingAdmin/Products/IProductStore.cs`, `LicensingSystem.Tests/ProductServiceTests.cs`, `docs/v2-admin-hardening-status.md`, `blueprints/licensing-system-v2-admin-hardening/tasks.json` | `dotnet test --filter ProductService` ✅ | Added archive service/store behavior and default list filtering |
| E2-T1 Version CRUD service | done | `LicensingAdmin/Products/ProductService.cs`, `LicensingAdmin/Products/IProductStore.cs`, `LicensingSystem.Tests/ProductVersionServiceTests.cs`, `LicensingSystem.Tests/ProductServiceTests.cs` | `dotnet test --filter Product` ✅ | Complete |
| E2-T2 Versions UI | done | `LicensingAdmin/Pages/Admin/EditProductDialog.razor` | `dotnet build LicensingSystem.sln` ✅ | Added version rows/creation/deletion to dialog |
| E2-T3 Single issuance | done | `LicensingAdmin/Licensing/LicenseIssuanceService.cs`, `LicensingAdmin/Licensing/LicenseIssuanceRequest.cs`, `LicensingAdmin/Pages/Licenses/New.razor`, `LicensingSystem.Tests/LicenseIssuanceServiceTests.cs` | `dotnet test --filter LicenseIssuanceService` ✅ | Added Status field (Active/Test) to request, service, and UI |
| E2-T4 Bulk issuance | done | `LicensingAdmin/Services/BulkLicenseService.cs`, `LicensingAdmin/Pages/Licenses/BulkGenerate.razor`, `LicensingSystem.Tests/BulkLicenseServiceTests.cs` | `dotnet test --filter BulkLicense` ✅ | Added Status selection to bulk page and service |
| E3-T1 Test-license API mode | done | `LicensingApi/Services/ActivationService.cs`, `LicensingSystem.Tests/ActivationServiceTests.cs` | `dotnet test --filter ActivationService` ✅ | Added Test license bypass logic to API activation/checkin |
| E3-T2 SMTP diagnostics | done | `LicensingAdmin/Notifications/NotificationConfigService.cs`, `LicensingAdmin/Pages/Admin/NotificationSettings.razor`, `LicensingSystem.Tests/NotificationConfigServiceTests.cs` | `dotnet test --filter NotificationConfigServiceTests` ✅ | Added failure reason sanitization, diagnostic logging, and test coverage |
| E3-T3 Email audit | done | `LicensingCore/Entities/EmailLogEntry.cs`, `LicensingCore/Data/AppDbContext.cs`, `LicensingAdmin/Notifications/NotificationConfigService.cs`, `infra/migrate-v2-admin-hardening.sql` | `dotnet build LicensingSystem.sln` ✅ | Added logging to SMTP test flow |
| E3-T4 Docs portal | done | `LicensingAdmin/Pages/Admin/Docs.razor` | `dotnet build LicensingSystem.sln` ✅ | Placeholder updated per task requirement |
| E3-T5 HelloWorld harness | done | `ActivationHelloWorld/ApiClient.cs`, `ActivationHelloWorld/Program.cs` | `dotnet build ActivationHelloWorld` ✅ | Added VersionId / Status fields with backwards compatibility; old clients continue to work |
| E3-T6 Compatibility tests | done | `LicensingSystem.Tests/ActivationCompatibilityTests.cs` | `dotnet test --filter ActivationCompatibilityTests` ✅ | Validates schema extension serialization contract for new fields |
| E3-T7 Final docs/gate | done | all | `dotnet build && dotnet test` ✅ | ✅ Full build passing, 449 tests passing. All v2.0 hardening tasks completed. |

## Status update protocol

Each implementation task must update this table immediately before work and after verification. No task is complete while its verify command is red.
