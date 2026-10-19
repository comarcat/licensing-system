# LicensingSystem

Sistema de licencias de un vendor: API de activación (`LicensingApi`), panel de administración
Blazor Server (`LicensingAdmin`) y biblioteca compartida (`LicensingCore`), sobre PostgreSQL, en un
único host Windows/IIS.

## Comandos

| Tarea | Comando |
|---|---|
| Restaurar | `dotnet restore LicensingSystem.sln` |
| Compilar (= lint + typecheck; no hay `dotnet lint`) | `dotnet build LicensingSystem.sln` |
| Compilar estricto | `dotnet build LicensingSystem.sln -warnaserror` |
| Pruebas (toda la suite) | `dotnet test` |
| Pruebas (una clase) | `dotnet test --filter <NombreClase>` |
| Ejecutar API | `dotnet run --project LicensingApi` |
| Ejecutar panel | `dotnet run --project LicensingAdmin` |
| Secreto local | `dotnet user-secrets set --project <proj> "<clave>" "<valor>"` |
| Migraciones EF | `dotnet ef ...` — **NO se usa en este slice** (esquema congelado) |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` debe pasar antes de marcar cualquier
tarea como hecha. Las versiones de paquete están en los `.csproj` y el lockfile — léelos, no los
adivines.

## Stack

C# / .NET 8 · Blazor Server + MudBlazor 7.15.0 · ASP.NET Core Web API · PostgreSQL 15+ · EF Core 8
(Npgsql) · cookie auth local contra `admin_users` · host único Windows/IIS.

## Arquitectura

**Ruta de una request de panel.** navegador → `LicensingAdmin/Pages/<X>.razor` (circuito Blazor
Server, `ServerPrerendered`) → `await using var db = await DbFactory.CreateDbContextAsync()` **o** un
servicio (`LicensingAdmin/Licensing/LicenseIssuanceService.cs`, `LicensingAdmin/Auth/AdminUserService.cs`)
→ `AppDbContext` (`LicensingCore/Data/AppDbContext.cs`) → PostgreSQL. El panel **no** llama a
`LicensingApi`. El login es una Razor Page (`Pages/Account/Login.cshtml.cs`), no un componente.

**Ruta de una request de API (congelada).** DLL cliente → `LicensingApi/Controllers/ActivationController.cs`
→ `LicensingApi/Services/ActivationService.cs` → `AppDbContext` → PostgreSQL. No se toca en este slice.

**Boundaries.**

| Capa | Puede importar de | Nunca |
|---|---|---|
| `LicensingCore/**` | nada interno del repo | `LicensingApi`, `LicensingAdmin` |
| `LicensingApi/**` | `LicensingCore` | `LicensingAdmin` |
| `LicensingAdmin/**` | `LicensingCore` | `LicensingApi` (Non-Goal: admin→API) |
| `LicensingSystem.Tests/**` | `LicensingCore`, `LicensingAdmin` | `LicensingApi` |

**Dónde vive cada cosa.**

| Asunto | Fuente única |
|---|---|
| Entidades y esquema | `LicensingCore/Entities/*` + `LicensingCore/Data/AppDbContext.cs` + `LicensingApi/Migrations/001_initial_schema.sql` — **este slice no cambia ninguno** |
| Cadena de conexión | `dotnet user-secrets` / env `ConnectionStrings__LicensingDb`; nunca en `appsettings.json`; validada por `ConnectionStringGuard.Require` en ambos `Program.cs` |
| Firma del registro `License` | `LicensingCore/Crypto/LicenseSigner.cs` — RSA-SHA256, PKCS1, sobre el payload canónico de `CanonicalBytes` |
| Generación de clave | `LicensingCore/Licensing/LicenseKeyGenerator.cs` — cumple `LicenseKeyFormat()` |
| Nombres de política de auth | `LicensingAdmin/Auth/AuthPolicies.cs` — constantes, nunca literales sueltos |
| Emisión de licencias | `LicensingAdmin/Licensing/LicenseIssuanceService.cs` |
| Identidad del admin actual | `LicensingAdmin/Auth/CurrentAdmin.Email(ClaimsPrincipal)` |

## Reglas de código

1. **Todo lo que un test prueba es `public`.** Sin `InternalsVisibleTo` — salvo `Program` de
   `LicensingAdmin`, que se hace visible con `public partial class Program { }` para
   `WebApplicationFactory<Program>`.
2. **`AppDbContext` expone `DbSet<>` con nombre** (`SoftwareProducts`, `Licenses`, `Activations`,
   `AdminUsers`, `AuditLogEntries`, `NotificationConfigs`). Las impl. EF pueden usarlos o `db.Set<T>()`.
3. **Las políticas de auth se referencian por las constantes de `AuthPolicies`**, nunca por string
   literal en una página. Toda página del panel lleva `@attribute [Authorize(Policy = ...)]`; además
   `Program.cs` fija una `FallbackPolicy` que exige sesión salvo `[AllowAnonymous]`.
4. **La emisión de licencias pasa por `LicenseIssuanceService`** — nada construye una `License` a mano.
5. **Login/logout son Razor Pages** bajo `Pages/Account/`, no componentes Blazor.
6. **Toda acción admin que muta datos escribe un `AuditLogEntry`** con `Actor` = email autenticado
   (`CurrentAdmin.Email(...)`), nunca un literal.
7. **`ResultCode`, `/api/*`, el sobre del archivo de licencia y `LicenseKeyFormat()` están
   congelados.** No se editan en este slice.
8. **No hay cambio de esquema en este slice.** Si una tarea parece necesitarlo, para y reporta.
9. **Ninguna dependencia nueva sin razón en el mensaje de commit.**

## Sistema de diseño

Sin tokens propios. Se reutiliza el tema por defecto de MudBlazor (`<MudThemeProvider/>` en
`Shared/MainLayout.razor`). Las pantallas nuevas componen `MudTable`, `MudPaper`, `MudButton`,
`MudSelect`, `MudTextField`, `MudForm`, `MudNumericField`, `MudDatePicker`, `MudAlert`.

## Entorno

| Variable | Requerida | Usada por | Fuente |
|---|---|---|---|
| `ConnectionStrings__LicensingDb` | para ejecutar las apps (no para `dotnet test`) | ambos `Program.cs` | `dotnet user-secrets` / env |
| `Crypto__RsaPrivateKeyPem` | opcional (fallback RSA en memoria, dev-only) | `LicensingAdmin` (firmador) | user-secrets / env |
| `Crypto__AesKeyBase64` | preexistente | `LicensingApi` | user-secrets / env |
| `Admin__BootstrapEmail` / `Admin__BootstrapPassword` | opcional (seeder no-op sin ellas) | `AdminSeeder` | user-secrets / env |

No hay `.env` en .NET: config = `appsettings.json` (no secreto) + `dotnet user-secrets` + entorno.

## Reglas diferidas

| Archivo | Aplica a |
|---|---|
| `.claude/rules/entities.md` | `LicensingCore/**` |
| `.claude/rules/admin-ui.md` | `LicensingAdmin/**` |

## Innegociable

1. Nunca commitear un secreto, un PEM, ni una cadena de conexión con contraseña.
2. Nunca tocar `/api/activate`, `/api/checkin`, `/health`, el enum `ResultCode`, el sobre de
   `LicenseFileService` ni la regex `LicenseKeyFormat()`.
3. Nunca cambiar el esquema de base de datos en este slice.
4. Toda acción admin que muta datos deja un `AuditLogEntry`.
5. Nunca marcar una tarea como hecha con la puerta (`dotnet build && dotnet test`) en rojo.
