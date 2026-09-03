# Epic 02: Auth aplicada y emisión de licencias

> Después de este epic: el panel exige inicio de sesión (política de fallback + `[Authorize]` por
> página), un test de integración prueba que una petición anónima a las rutas protegidas recibe un
> 302 a `/Account/Login`, el revisor de activaciones es el admin autenticado (no un literal), existe
> un seed del primer `SuperAdmin`, y un `SupportStaff`/`SuperAdmin` puede emitir una licencia firmada
> desde `/licenses/new`. `SuperAdmin` gestiona administradores en `/admin/users`.

| | |
|---|---|
| **Epic id** | `02-auth-y-licencias` |
| **Tasks** | `E2-T1` … `E2-T8` |
| **Depends on** | `01-fundamentos` (todo) |
| **Unlocks** | nada — es el último epic del slice |
| **Parallel with** | nada |

No necesitas ningún otro archivo para completar este epic. Todo lo de abajo se repite aquí a propósito.

---

## Stack

C# / .NET 8 (`net8.0`) · Blazor Server + MudBlazor 7.15.0 · ASP.NET Core Web API · PostgreSQL 15+ ·
EF Core 8 (Npgsql 8.0.10) · cookie auth local contra `admin_users` · host único Windows/IIS.
Package manager: NuGet (integrado en el SDK). Runtime: TFM `net8.0`, pin de SDK 8.0.4xx via `global.json` (T1, rollForward latestFeature). Las versiones de paquete están en los `.csproj` y el lockfile — léelas, no las adivines.

| Tarea | Comando |
|---|---|
| Restaurar | `dotnet restore LicensingSystem.sln` |
| Compilar (= lint + typecheck; no hay `dotnet lint`) | `dotnet build LicensingSystem.sln` |
| Compilar estricto | `dotnet build LicensingSystem.sln -warnaserror` |
| Pruebas (toda la suite) | `dotnet test` |
| Pruebas (una clase) | `dotnet test --filter <NombreClase>` |
| Ejecutar panel (para el `# manual:` de `E2-T6`) | `dotnet run --project LicensingAdmin` |
| Secreto local | `dotnet user-secrets set --project <proj> "<clave>" "<valor>"` |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` pasa antes de marcar cualquier tarea
como hecha.

Los `Verify` de este epic son unitarios/integración en proceso salvo **una** anotación `# manual:`
en `E2-T6` (paso 14), que necesita el panel arrancado contra PostgreSQL en `172.16.101.12`. No es
una puerta: las puertas reales de esa tarea son `dotnet build` y `dotnet test`. El test de pipeline
de `E2-T2` usa `WebApplicationFactory<Program>` con una cadena de conexión ficticia (una petición
anónima recibe 302 antes de tocar la BD) y sin el `AdminSeeder` — no necesita Postgres.

## Directory subtree

Solo lo que este epic toca:

```
LicensingAdmin/
  Program.cs                              # EDIT (T1 auth-state, T2 login/logout + `public partial class Program`, T3 seeder, T6 DI de emisión, T7 DI de admin-users)
  Shared/MainLayout.razor                 # EDIT (T6): email+logout + <AuthorizeView> por enlace de nav (Generar licencia + Usuarios admin)
  Auth/
    AdminAuthStateProvider.cs             # NUEVO (T1)
    AdminCredentialService.cs             # NUEVO (T1) — IAdminUserLookup + ValidateAsync + static BuildPrincipal
    CurrentAdmin.cs                       # NUEVO (T4) — static string Email(ClaimsPrincipal)
    AdminUserService.cs                   # NUEVO (T7)
    IAdminUserStore.cs                    # NUEVO (T7)
    AuthPolicies.cs                       # EXISTE (01/T8, read-only)
    PasswordHasherService.cs              # EXISTE (01/T7, read-only)
    CryptoRegistration.cs                 # EXISTE (01/T6, read-only)
  Pages/
    Account/Login.cshtml                  # NUEVO (T2)
    Account/Login.cshtml.cs               # NUEVO (T2)
    Account/Logout.cshtml.cs             # NUEVO (T2)
    Dashboard.razor                       # EDIT (T4): [Authorize(Policy = ViewerAccess)]
    Licenses.razor                        # EDIT (T4): [Authorize(Policy = ViewerAccess)]
    PendingReview.razor                   # EDIT (T4): [Authorize(Policy = ReviewAccess)] + revisor real
    Licenses/New.razor                    # NUEVO (T6): /licenses/new, [Authorize(Policy = IssueAccess)]
    Admin/Users.razor                     # NUEVO (T7): /admin/users, [Authorize(Policy = AdminUserAccess)]
  Startup/AdminSeeder.cs                  # NUEVO (T3) — IHostedService; static ShouldSeed / BuildSuperAdmin
  Licensing/
    LicenseIssuanceService.cs             # NUEVO (T5)
    ILicenseStore.cs                      # NUEVO (T5)
    EfLicenseStore.cs                     # NUEVO (T5)
    LicenseIssuanceRequest.cs             # NUEVO (T5)
LicensingCore/
  Crypto/ILicenseSigner.cs               # EXISTE (01/T5, read-only)
  Licensing/LicenseKeyGenerator.cs        # EXISTE (01/T4, read-only)
  Entities/*.cs, Data/AppDbContext.cs     # EXISTE (read-only) — DbSet<> con nombre: SoftwareProducts, Licenses, Activations, AdminUsers, AuditLogEntries, NotificationConfigs
LicensingSystem.Tests/
  AdminCredentialServiceTests.cs          # NUEVO (T1)
  AuthorizationPipelineTests.cs           # NUEVO (T2)
  AdminSeederTests.cs                     # NUEVO (T3)
  CurrentAdminTests.cs                    # NUEVO (T4)
  LicenseIssuanceServiceTests.cs          # NUEVO (T5)
  AdminUserServiceTests.cs                # NUEVO (T7)
  SmokeTests.cs                           # EDIT (T8): aserción de regresión final
```

Todo fuera de este subárbol está fuera de alcance. Si una tarea parece requerir editar un archivo no
listado (p. ej. `ActivationController.cs`, `LicenseFileService.cs`, `ResultCode.cs`,
`AppDbContext.cs`, `001_initial_schema.sql`), para y reporta — significa que el límite del epic está
mal.

## Data model touched here

Ningún cambio de esquema. Este epic **añade filas** a tablas existentes.

| Entity | Campos que este epic añade o lee | Notas |
|---|---|---|
| `admin_users` | lee `Email`, `PasswordHash`, `Role`, `IsActive`, `LastLoginAtUtc`; **escribe** filas nuevas (seeder T3, `AdminUserService` T7) y `LastLoginAtUtc` (login T2) | búsqueda por email case-insensitive; impl EF vía `db.AdminUsers` / `db.Set<AdminUser>()` |
| `software_products` | **escribe** filas nuevas cuando se elige "Nuevo producto" (T5/T6) | `DefaultLicenseModel` es bitmask `int` |
| `licenses` | **escribe** filas nuevas (T5/T6); `ModelSnapshot` y `MaxActivations` congelados del request; `Signature` de `ILicenseSigner.Sign` | `LicenseKey` único; regex de §5 del blueprint |
| `activations` | **escribe** `ReviewedBy` al aprobar/rechazar (T4); `PendingReview.Resolve` preexistente también fija `Status`, `ApprovedAtUtc`/`RejectedAtUtc`, `ReviewDeadlineUtc` | sin cambios de columna |
| `audit_log_entries` | **escribe** en cada login, aprobación/rechazo, emisión y alta/baja de admin | `Actor` = email autenticado (`CurrentAdmin.Email`), nunca literal |

## Contracts

**Consumido** — de `01-fundamentos`, no lo reconstruyas:

| De | Interfaz | Garantía |
|---|---|---|
| `01` T2 | `LicensingCore/Configuration/ConnectionStringGuard.Require` | `static string Require(string?)` — lanza si vacía |
| `01` T4 | `LicensingCore/Licensing/LicenseKeyGenerator.NewKey` | `static string NewKey()` — cumple `LicenseKeyFormat()` |
| `01` T5 | `LicensingCore/Crypto/ILicenseSigner` | `byte[] Sign(License)` · `bool Verify(License, byte[], RSA)` · `static byte[] CanonicalBytes(License)` |
| `01` T6 | `LicensingAdmin/Auth/CryptoRegistration.AddLicenseSigner` | `ILicenseSigner` resuelve en DI, con o sin PEM |
| `01` T7 | `LicensingAdmin/Auth/PasswordHasherService` | `string Hash(string)` · `bool Verify(string, string)` |
| `01` T8 | `LicensingAdmin/Auth/AuthPolicies` + `App.razor` | constantes de política + registro + `FallbackPolicy` (auth obligatoria salvo `[AllowAnonymous]`); `App.razor` con `AuthorizeRouteView` + `RedirectToLogin` |

**Producido** — nada fuera de este slice depende de estas firmas, pero las tareas de este epic sí
entre ellas:

| Export | Firma | Usado por |
|---|---|---|
| `LicensingAdmin/Auth/AdminCredentialService` | `Task<ClaimsPrincipal?> ValidateAsync(string,string)` · `static ClaimsPrincipal BuildPrincipal(AdminUser)` | `Login.cshtml.cs` (T2) |
| `LicensingAdmin/Auth/CurrentAdmin.Email` | `static string Email(ClaimsPrincipal)` | `PendingReview.razor` (T4), `New.razor` (T6), `Users.razor` (T7) |
| `LicensingAdmin/Startup/AdminSeeder` | `static bool ShouldSeed(bool,string?,string?)` · `static AdminUser BuildSuperAdmin(string,string,PasswordHasherService)` | `AdminSeederTests` (T3) |
| `LicensingAdmin/Licensing/LicenseIssuanceService.IssueAsync` | `Task<License> IssueAsync(LicenseIssuanceRequest)` | `New.razor` (T6) |
| `LicensingAdmin/Licensing/ILicenseStore` | `Task<bool> LicenseKeyExistsAsync(string)` · `Task AddAsync(SoftwareProduct?, License, AuditLogEntry)` | `LicenseIssuanceService` (T5) |
| `LicensingAdmin/Auth/IAdminUserStore` | `EmailExistsAsync` · `ListAsync` · `AddAsync` · `SetActiveAsync` | `AdminUserService` (T7) |

## Conventions that bite in this area

- **Login/logout son Razor Pages** bajo `Pages/Account/`, no componentes Blazor —
  `HttpContext.SignInAsync` necesita el `HttpContext` crudo. `Login.cshtml.cs` lleva `[AllowAnonymous]`.
- **La autorización efectiva es la `FallbackPolicy` (01/T8) + `[Authorize(Policy = AuthPolicies.X)]`
  en cada página.** `<AuthorizeView>` en `MainLayout` solo oculta enlaces. Quitar un `[Authorize]`
  degrada la página a "cualquier autenticado" (por la fallback), no a anónimo — y el `grep -L`
  (T4, sobre 3 archivos preexistentes) / `grep -q` (T6, T7, sobre el archivo nuevo) lo detecta igual.
- **Toda mutación escribe un `AuditLogEntry`** con `Actor = CurrentAdmin.Email(user)`. El literal
  `"support-staff@vendor.com"` de `PendingReview.razor` se **borra** en T4.
- **Las impl. EF usan los `DbSet<>` con nombre de `AppDbContext`** (`db.AdminUsers`,
  `db.SoftwareProducts`, `db.Licenses`, `db.AuditLogEntries`) o `db.Set<T>()` — ambos existen.
- **Nueva instancia de `AppDbContext` por operación:** `await using var db = await
  factory.CreateDbContextAsync()` en cada llamada; no compartas el contexto entre requests del circuito.
- **La emisión pasa por `LicenseIssuanceService`.** Nada construye una `License` a mano. La clave
  viene de `LicenseKeyGenerator.NewKey()` con reintento mientras `LicenseKeyExistsAsync` sea true.
- **`ModelSnapshot` y `MaxActivations` se congelan en emisión** — copia del request/producto.
- **Error de login genérico** — no distingas "no existe" / "contraseña mala" / "inactivo".
- **Una sola anotación `# manual:` en todo el epic** — está en `E2-T6`. `E2-T1`/`E2-T2` se verifican
  con el test puro de `BuildPrincipal`/`ValidateAsync` (fake `IAdminUserLookup`) y el test de
  pipeline con `WebApplicationFactory`.
- **No toques los contratos congelados** — `/api/*`, `ResultCode`, el sobre de `LicenseFileService`,
  `LicenseKeyFormat()`, ni el esquema.

Reglas completas del proyecto: `CLAUDE.md`. Reglas de área: `.claude/rules/entities.md`,
`.claude/rules/admin-ui.md`. Ambas en la raíz del repo — el builder las copió ahí desde
`docs/blueprint/workspace/` antes de la tarea 1.

---

## Tasks

Listadas en el mismo orden que `tasks.json`. Ese orden es el orden de build — trabaja de arriba
abajo y no re-ordenes por prioridad ni por lo que parezca rápido.

### `E2-T1` — Auth-state provider + servicio de credenciales

**Depends on:** E1-T8 · **Priority:** p0

`AdminCredentialService` con costura `IAdminUserLookup` (`Task<AdminUser?> FindByEmailAsync(string)`;
impl real usa `IDbContextFactory<AppDbContext>` + `db.AdminUsers`, case-insensitive).
`ValidateAsync`: sin fila → **`Verify` dummy de coste fijo** + `null` (anti-timing, B-1 de E1-T7);
`Verify` real falla → `null`; `!IsActive` → `null`; OK → `BuildPrincipal`. `static ClaimsPrincipal
BuildPrincipal(AdminUser)` puro: `ClaimTypes.Name`=email, **`ClaimTypes.Role`** (role claim type por
defecto) = `u.Role.ToString()`. `AdminAuthStateProvider`:
`RevalidatingServerAuthenticationStateProvider`, 30 min, revalida recargando el `AdminUser` y
comprobando `IsActive` + rol sin cambio. En `Program.cs`, además de registrar los tres servicios
(`AddScoped`): en el `AddCookie` del paso 8 añade `o.ExpireTimeSpan = TimeSpan.FromHours(8)` y
`o.Events.OnValidatePrincipal` que recarga el `AdminUser` (existe + `IsActive` + rol) y
`context.RejectPrincipal()` si falla — un admin desactivado pierde la sesión sin esperar a Blazor.
El test usa un fake `IAdminUserLookup` — sin `DbContext`.

**Files**
- `LicensingAdmin/Auth/AdminAuthStateProvider.cs` — nuevo
- `LicensingAdmin/Auth/AdminCredentialService.cs` — nuevo (incluye `IAdminUserLookup` y su impl EF)
- `LicensingSystem.Tests/AdminCredentialServiceTests.cs` — nuevo: clase `AdminCredentialServiceTests`
- `LicensingAdmin/Program.cs` — edit: registra los tres servicios + `ExpireTimeSpan` + `OnValidatePrincipal`

**Acceptance**

1. **WHEN** `AdminCredentialService.BuildPrincipal(adminUser)` runs **THE SYSTEM SHALL** return a `ClaimsPrincipal` carrying `ClaimTypes.Name` = the user's email and `ClaimTypes.Role` (the default role claim type, so `RequireRole` matches) = the `AdminRole` name.
2. **WHEN** `ValidateAsync(email, password)` is given an email that matches no `admin_users` row **THE SYSTEM SHALL** return null (and SHALL run a fixed-cost dummy `PasswordHasherService.Verify` first so the no-user path is not distinguishable by timing).
3. **WHEN** `ValidateAsync` matches a row whose `IsActive` is false **THE SYSTEM SHALL** return null even if the password is correct; and the cookie's `OnValidatePrincipal` SHALL reject a principal whose `AdminUser` no longer exists, is inactive, or changed role, with `Program.cs` setting a bounded `ExpireTimeSpan` (e.g. 8h) alongside the existing `SlidingExpiration`.
4. **WHEN** `ValidateAsync` matches an active row and the password verifies **THE SYSTEM SHALL** return a non-null principal built by `BuildPrincipal`.
5. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0.
6. **WHEN** `dotnet test --filter AdminCredential` runs **THE SYSTEM SHALL** report all `AdminCredential` tests passed, 0 failed.

**Verify**

```bash
dotnet build LicensingSystem.sln
dotnet test --filter AdminCredential
```

**Checkpoint**

```bash
git add -A && git commit -m "step 9: auth-state"
git tag step-09-auth-state
```

### `E2-T2` — Paginas Razor de login/logout + test de pipeline de autorizacion

**Depends on:** E2-T1 · **Priority:** p0

`Login.cshtml(.cs)` `[AllowAnonymous]`: GET renderiza el formulario (email, contraseña, `returnUrl`);
POST → `AdminCredentialService.ValidateAsync` → si principal: `HttpContext.SignInAsync`, fija
`AdminUser.LastLoginAtUtc`, escribe `AuditLogEntry` (`Actor`=email, `EntityType`="AdminUser",
`Action`="Login"), redirige a `returnUrl` **validado con `Url.IsLocalUrl`** (rechaza absolutos y
`//host`; cae a `/`); si `null`: re-renderiza con **un** mensaje de error genérico (sin distinguir
causa). **Recomendado (no bloqueante):** lockout por email en memoria (5 fallos / 15 min); el
rate-limiting de middleware sigue siendo Non-Goal. `Logout.cshtml.cs`: `SignOutAsync` →
`/Account/Login`. `AccessDenied.cshtml` (`@page "/Account/AccessDenied"`, `@attribute
[AllowAnonymous]`): página mínima "no tienes permiso" (200, sin rebote) — destino del
`AccessDeniedPath` de la cookie (paso 8). Añade `public partial class Program { }` al final de
`LicensingAdmin/Program.cs` para `WebApplicationFactory<Program>` (sin `InternalsVisibleTo`).
`AuthorizationPipelineTests`:
una `WebApplicationFactory<Program>` que en `ConfigureAppConfiguration` fija
`ConnectionStrings:LicensingDb = "Host=localhost;Port=5432;Database=test;Username=test;Password=test"`
(nunca se conecta: una petición anónima recibe 302 antes de tocar la BD). En este paso `Program.cs`
aún no registra ningún `IHostedService`, así que el host arranca sin tocar la BD (el paso 11 —que
añade `AddHostedService<AdminSeeder>()`— extiende este archivo para quitar ese seeder). Aserta:
`GET /Account/Login` anónimo → 200; `GET /` y `GET /pending-review` anónimos → 302 con `Location`
que empieza por `/Account/Login`.

**Files** (7 — páginas de cuenta + wiring; el tope de 5 se exime, igual que E1-T1/E1-T8)
- `LicensingSystem.Tests/AuthorizationPipelineTests.cs` — nuevo: clase `AuthorizationPipelineTests`
- `LicensingAdmin/Pages/Account/Login.cshtml` — nuevo: markup del formulario
- `LicensingAdmin/Pages/Account/Login.cshtml.cs` — nuevo: `[AllowAnonymous]`, GET/POST, `Url.IsLocalUrl`
- `LicensingAdmin/Pages/Account/Logout.cshtml.cs` — nuevo
- `LicensingAdmin/Pages/Account/Logout.cshtml` — nuevo: vista mínima POST-only (botón + antiforgery); un PageModel sin su `.cshtml` no lo enruta Razor Pages
- `LicensingAdmin/Pages/Account/AccessDenied.cshtml` — nuevo: `[AllowAnonymous]`, "sin permiso" (200)
- `LicensingAdmin/Program.cs` — edit: `public partial class Program { }`

**Acceptance**

1. **WHEN** `GET /Account/Login` is requested with no authentication cookie **THE SYSTEM SHALL** return HTTP 200; `Pages/Account/AccessDenied.cshtml` exists, carries `@attribute [AllowAnonymous]`, and returns a 'sin permiso' page (the cookie `AccessDeniedPath` from step 8 points here).
2. **WHEN** `GET /` is requested with no authentication cookie **THE SYSTEM SHALL** return HTTP 302 whose `Location` header starts with `/Account/Login`.
3. **WHEN** `GET /pending-review` is requested with no authentication cookie **THE SYSTEM SHALL** return HTTP 302 whose `Location` header starts with `/Account/Login`.
4. **WHEN** `LicensingAdmin/Pages/Account/Login.cshtml.cs` is inspected **THE SYSTEM SHALL** carry `[AllowAnonymous]`, on any null `ValidateAsync` result re-render with a single generic error message that does not distinguish the failure cause, and validate `returnUrl` with `Url.IsLocalUrl` (rejecting absolute and `//host` URLs, falling back to `/`).
5. **WHEN** a POST to `/Account/Login` succeeds **THE SYSTEM SHALL** call `HttpContext.SignInAsync`, set `AdminUser.LastLoginAtUtc`, and write an `AuditLogEntry` with `Action` = "Login".
6. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0 and `dotnet test --filter AuthorizationPipeline` **SHALL** report all tests passed, 0 failed.

**Verify**

```bash
dotnet build LicensingSystem.sln
dotnet test --filter AuthorizationPipeline
grep -q "\[AllowAnonymous\]" LicensingAdmin/Pages/Account/Login.cshtml.cs
grep -q "\[AllowAnonymous\]" LicensingAdmin/Pages/Account/AccessDenied.cshtml
```

**Checkpoint**

```bash
git add -A && git commit -m "step 10: login-pages"
git tag step-10-login-pages
```

### `E2-T3` — Seeder del primer SuperAdmin

**Depends on:** E2-T2 · **Priority:** p0

`AdminSeeder` : `IHostedService`. `static bool ShouldSeed(bool tableEmpty, string? email, string?
password)` = `tableEmpty && !IsNullOrWhiteSpace(email) && !IsNullOrWhiteSpace(password)`. `static
AdminUser BuildSuperAdmin(string email, string password, PasswordHasherService hasher)` = usuario
`SuperAdmin`, `IsActive = true`, `PasswordHash = hasher.Hash(password)`,
`Email = email.Trim().ToLowerInvariant()` (el índice único de `admin_users.Email` es
case-sensitive y `EfAdminUserLookup` ya busca en minúsculas — normalizar en la escritura del
seeder cierra el desajuste para el admin de arranque). `StartAsync`: si
`ShouldSeed(!await db.AdminUsers.AnyAsync(), config["Admin:BootstrapEmail"],
config["Admin:BootstrapPassword"])`, inserta el usuario + `AuditLogEntry` (`Actor`="system",
`Action`="Created", `EntityType`="AdminUser"). Registra `AddHostedService<AdminSeeder>()` en
`Program.cs`. **En el mismo commit**, extiende `AuthorizationPipelineTests.cs` para que su
`WebApplicationFactory<Program>` quite ese `IHostedService` en `ConfigureTestServices`
(`services.Remove(services.Single(d => d.ImplementationType == typeof(AdminSeeder)))`), de modo que
el host de test no ejecute `AdminSeeder.StartAsync` contra la cadena ficticia — sin esto se rompería
la puerta `dotnet test --filter AuthorizationPipeline` del paso 10 y toda puerta de suite completa
posterior.

**Files**
- `LicensingAdmin/Startup/AdminSeeder.cs` — nuevo
- `LicensingSystem.Tests/AdminSeederTests.cs` — nuevo: clase `AdminSeederTests`
- `LicensingAdmin/Program.cs` — edit: `AddHostedService<AdminSeeder>()`
- `LicensingSystem.Tests/AuthorizationPipelineTests.cs` — edit: quita el `AdminSeeder` en `ConfigureTestServices` (helper compartido `WithoutAdminSeeder()`)
- `LicensingSystem.Tests/GateScreensPipelineTests.cs` — edit: **toda** `WebApplicationFactory<Program>` de la suite hereda el `AddHostedService<AdminSeeder>()`, así que sus factories también deben aplicar `WithoutAdminSeeder()` o el host de test intentaría sembrar contra la cadena ficticia

**Acceptance**

1. **WHEN** `AdminSeeder.ShouldSeed(true, "a@b.c", "pw")` runs **THE SYSTEM SHALL** return true, and **WHEN** called with `false` as the first argument **THE SYSTEM SHALL** return false.
2. **WHEN** `AdminSeeder.ShouldSeed(true, null, "pw")` or `AdminSeeder.ShouldSeed(true, "a@b.c", null)` runs **THE SYSTEM SHALL** return false.
3. **WHEN** `AdminSeeder.BuildSuperAdmin("a@b.c", "pw", hasher)` runs **THE SYSTEM SHALL** return an `AdminUser` with `Role == SuperAdmin`, `IsActive == true`, and a non-empty `PasswordHash` not equal to `"pw"`.
4. **WHEN** `AdminSeeder.BuildSuperAdmin("  Admin@B.C  ", "pw", hasher)` runs **THE SYSTEM SHALL** return an `AdminUser` whose `Email` is `"admin@b.c"` (trimmed and lower-cased), and the `StartAsync` seed path **SHALL** normalise `config["Admin:BootstrapEmail"]` the same way so the bootstrap admin can sign in regardless of the case configured.
5. **WHEN** `LicensingAdmin/Program.cs` is inspected **THE SYSTEM SHALL** register `AddHostedService<AdminSeeder>()` exactly once.
6. **WHEN** the `WebApplicationFactory<Program>` in `AuthorizationPipelineTests` starts **THE SYSTEM SHALL** remove the `AdminSeeder` hosted service in `ConfigureTestServices` so no database access occurs at host startup.
7. **WHEN** `dotnet test --filter AdminSeeder` and `dotnet test --filter AuthorizationPipeline` run **THE SYSTEM SHALL** both report all tests passed, 0 failed.

**Verify**

```bash
dotnet test --filter AdminSeeder
dotnet test --filter AuthorizationPipeline
```

**Checkpoint**

```bash
git add -A && git commit -m "step 11: admin-seeder"
git tag step-11-admin-seeder
```

### `E2-T4` — Proteger pantallas existentes + revisor real

**Depends on:** E1-T8, E2-T1 · **Priority:** p0

`@attribute [Authorize(Policy = AuthPolicies.ViewerAccess)]` en `Dashboard.razor` y `Licenses.razor`;
`[Authorize(Policy = AuthPolicies.ReviewAccess)]` en `PendingReview.razor`. En `PendingReview.razor`:
inyecta `AuthenticationStateProvider`; en `Resolve(...)` fija `entity.ReviewedBy` y el `Actor` del
`AuditLogEntry` desde `CurrentAdmin.Email((await authState.GetAuthenticationStateAsync()).User)`;
**borra** el literal `"support-staff@vendor.com"` y el `TODO`. `CurrentAdmin.Email(ClaimsPrincipal)`:
si autenticado → `principal.FindFirstValue(ClaimTypes.Name) ?? ""`; si no → `""`. `MainLayout.razor`
**no se toca aquí** (su shell de auth va en T6).

**Files**
- `LicensingAdmin/Pages/Dashboard.razor` — edit: atributo `[Authorize]`
- `LicensingAdmin/Pages/Licenses.razor` — edit: atributo `[Authorize]`
- `LicensingAdmin/Pages/PendingReview.razor` — edit: atributo `[Authorize]` + revisor real, sin literal
- `LicensingAdmin/Auth/CurrentAdmin.cs` — nuevo
- `LicensingSystem.Tests/CurrentAdminTests.cs` — nuevo: clase `CurrentAdminTests`

**Acceptance**

1. **WHEN** `Dashboard.razor` and `Licenses.razor` are inspected **THE SYSTEM SHALL** each carry `@attribute [Authorize(Policy = AuthPolicies.ViewerAccess)]`.
2. **WHEN** `PendingReview.razor` is inspected **THE SYSTEM SHALL** carry `@attribute [Authorize(Policy = AuthPolicies.ReviewAccess)]`.
3. **WHEN** an activation is approved or rejected **THE SYSTEM SHALL** set `entity.ReviewedBy` and the new `AuditLogEntry.Actor` from `CurrentAdmin.Email(...)`, not from any hardcoded literal.
4. **WHEN** `grep -RIl "support-staff@vendor.com" LicensingAdmin/` runs **THE SYSTEM SHALL** find no match and exit 1.
5. **WHEN** `CurrentAdmin.Email(principal)` is given an authenticated principal **THE SYSTEM SHALL** return its `ClaimTypes.Name` value, and given an anonymous principal **SHALL** return `""`.
6. **WHEN** `dotnet build LicensingSystem.sln` and `dotnet test --filter CurrentAdmin` run **THE SYSTEM SHALL** both exit 0 with all `CurrentAdmin` tests passed.

**Verify**

```bash
dotnet build LicensingSystem.sln
grep -RIl "support-staff@vendor.com" LicensingAdmin/; test $? -eq 1
test -z "$(grep -L '@attribute \[Authorize(Policy = AuthPolicies\.' LicensingAdmin/Pages/Dashboard.razor LicensingAdmin/Pages/Licenses.razor LicensingAdmin/Pages/PendingReview.razor)"
dotnet test --filter CurrentAdmin
```

**Checkpoint**

```bash
git add -A && git commit -m "step 12: gate-screens"
git tag step-12-gate-screens
```

### `E2-T5` — Servicio de emision de licencias

**Depends on:** E1-T4, E1-T5 · **Priority:** p0

`LicenseIssuanceRequest` (producto existente O campos de producto nuevo; `LicenseModel Model`; `int
MaxActivations`; `DateTime? SubscriptionExpiryUtc`; `CustomerEmail`/`CustomerName`). `ILicenseStore`
(`LicenseKeyExistsAsync`, `AddAsync(SoftwareProduct?, License, AuditLogEntry)`). `EfLicenseStore` con
`IDbContextFactory<AppDbContext>` + los `DbSet<>` con nombre. `LicenseIssuanceService.IssueAsync`:
elige/crea producto; `LicenseKeyGenerator.NewKey()` reintentando mientras `LicenseKeyExistsAsync`;
snapshot de `ModelSnapshot`+`MaxActivations`; `Signature = signer.Sign(license)`; `AuditLogEntry`
"Created"/"License"; persiste vía `AddAsync`. Test con fake `ILicenseStore` + `ILicenseSigner` real
(RSA en memoria).

**Files**
- `LicensingAdmin/Licensing/LicenseIssuanceService.cs` — nuevo
- `LicensingAdmin/Licensing/ILicenseStore.cs` — nuevo
- `LicensingAdmin/Licensing/EfLicenseStore.cs` — nuevo
- `LicensingAdmin/Licensing/LicenseIssuanceRequest.cs` — nuevo
- `LicensingSystem.Tests/LicenseIssuanceServiceTests.cs` — nuevo: clase `LicenseIssuanceServiceTests`

**Acceptance**

1. **WHEN** `LicenseIssuanceService.IssueAsync(request)` runs with a valid request **THE SYSTEM SHALL** produce a `License` whose `LicenseKey` matches `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$`.
2. **WHEN** the produced `License.Signature` is checked with `ILicenseSigner.Verify` and the issuing public key **THE SYSTEM SHALL** return true.
3. **WHEN** the fake `ILicenseStore` reports the first generated key already exists **THE SYSTEM SHALL** generate another key and retry until `LicenseKeyExistsAsync` returns false.
4. **WHEN** the request carries model flags and `MaxActivations` **THE SYSTEM SHALL** copy them into `License.ModelSnapshot` and `License.MaxActivations`.
5. **WHEN** issuance succeeds **THE SYSTEM SHALL** pass the store one `AuditLogEntry` with `Action == "Created"` and `EntityType == "License"`.
6. **WHEN** `dotnet test --filter LicenseIssuance` runs **THE SYSTEM SHALL** report all `LicenseIssuance` tests passed, 0 failed.

**Verify**

```bash
dotnet test --filter LicenseIssuance
```

**Checkpoint**

```bash
git add -A && git commit -m "step 13: license-issuance"
git tag step-13-license-issuance
```

### `E2-T6` — Pantalla Generar nueva licencia + shell de MainLayout

**Depends on:** E2-T4, E2-T5 · **Priority:** p1

`Pages/Licenses/New.razor` (`@page "/licenses/new"`, `@attribute [Authorize(Policy =
AuthPolicies.IssueAccess)]`): `MudForm` con `MudSelect` de productos + switch "Nuevo producto";
checkboxes de `LicenseModel`; `MudNumericField` MaxActivations (`Min="1"` — el servicio firma
verbatim lo que reciba, así que la validación de rango vive en el formulario); `MudDatePicker`
habilitado solo con el flag `Subscription`; `MudTextField` cliente (los campos de cliente se
`Trim()`ean antes de enviar). Submit → `LicenseIssuanceService.IssueAsync` con
`CurrentAdmin.Email(...)` → `MudPaper` con la clave, botón copiar, enlace "Volver a Licencias".
Estados loading/empty/error. `MainLayout.razor`: en `MudAppBar`, `<AuthorizeView>` con
`context.User.Identity?.Name` + enlace "Cerrar sesión" a `/Account/Logout` (reemplaza "Support
Staff"); en `MudNavMenu`, **cada** `MudNavLink` envuelto en `<AuthorizeView Policy="...">`
(Dashboard/Licencias = `ViewerAccess`, Pending Review = `ReviewAccess`, "Generar licencia" →
`licenses/new` = `IssueAccess`, "Usuarios admin" → `admin/users` = `AdminUserAccess`). El enlace a
`/admin/users` puede apuntar a una ruta que aún no existe hasta T7 — es válido y solo lo ve
`SuperAdmin`.

**Files**
- `LicensingAdmin/Pages/Licenses/New.razor` — nuevo (`@namespace LicensingAdmin.Pages.NewLicense` explícito — la carpeta `Pages/Licenses/` colisiona con el tipo `Pages/Licenses.razor`, §20.3 #18)
- `LicensingAdmin/Shared/MainLayout.razor` — edit: email + logout + `<AuthorizeView>` por enlace + enlaces "Generar licencia" y "Usuarios admin"
- `LicensingAdmin/Program.cs` — edit: `AddScoped<ILicenseStore, EfLicenseStore>()` + `AddScoped<LicenseIssuanceService>()` (el wiring DI de E2-T5 vive aquí)

**Acceptance**

1. **WHEN** `LicensingAdmin/Pages/Licenses/New.razor` is inspected **THE SYSTEM SHALL** carry `@attribute [Authorize(Policy = AuthPolicies.IssueAccess)]` so a `ReadOnlyViewer` or anonymous user is denied.
2. **WHEN** the form is submitted with the "new product" switch on **THE SYSTEM SHALL** create the `SoftwareProduct` and then issue the license through `LicenseIssuanceService`, and **WHEN** an existing product is chosen **THE SYSTEM SHALL** reuse it.
3. **WHEN** issuance succeeds **THE SYSTEM SHALL** display the generated license key in a `MudPaper` with a copy button and a link back to `/licenses`.
4. **WHEN** the `Subscription` model flag is not selected **THE SYSTEM SHALL** keep the subscription-expiry date picker disabled.
5. **WHEN** `MainLayout.razor` is rendered **THE SYSTEM SHALL** show the signed-in user's email and a logout link and **SHALL** wrap every nav link, including "Generar licencia" (`IssueAccess`) and "Usuarios admin" (`AdminUserAccess`), in `<AuthorizeView Policy="...">`.
6. **WHEN** `dotnet build LicensingSystem.sln` and the full `dotnet test` suite run **THE SYSTEM SHALL** both exit 0 with 0 failed and 0 skipped.

**Verify** — la cuarta línea es una anotación `# manual:` (la única del epic); no es una puerta,
solo la comprueba una persona. Las puertas son `dotnet build`, el `grep -q` y `dotnet test`.

```bash
dotnet build LicensingSystem.sln
grep -q '@attribute \[Authorize(Policy = AuthPolicies\.IssueAccess)\]' LicensingAdmin/Pages/Licenses/New.razor
dotnet test
# manual: compare /licenses/new against epics/02-auth-y-licencias.md task `E2-T6`; run `dotnet run --project LicensingAdmin` against 172.16.101.12 with a seeded SuperAdmin, sign in, issue a license, confirm the rendered key matches the format regex
```

**Checkpoint**

```bash
git add -A && git commit -m "step 14: generate-license-page"
git tag step-14-generate-license-page
```

### `E2-T7` — Pantalla de administradores

**Depends on:** E1-T7, E2-T3, E2-T4 · **Priority:** p1

`IAdminUserStore` (`EmailExistsAsync` case-insensitive, `ListAsync`, `AddAsync(AdminUser,
AuditLogEntry)`, `SetActiveAsync(Guid, bool, AuditLogEntry)`). `AdminUserService.CreateAsync(email,
role, tempPassword)`: si `EmailExistsAsync` → rechaza sin escribir; si no → `AdminUser` con
`PasswordHash = hasher.Hash(tempPassword)` + `AuditLogEntry` "Created"/"AdminUser".
`DeactivateAsync`/`ActivateAsync` → `SetActiveAsync` + `AuditLogEntry` "Updated". Impl EF de
`IAdminUserStore` con `IDbContextFactory<AppDbContext>` + `db.AdminUsers`. `Pages/Admin/Users.razor`
(`@page "/admin/users"`, `[Authorize(Policy = AuthPolicies.AdminUserAccess)]`): `MudTable` + alta +
toggle. El enlace de nav a esta pantalla ya lo añadió T6. Test con fake `IAdminUserStore`.

**Files**
- `LicensingAdmin/Pages/Admin/Users.razor` — nuevo
- `LicensingAdmin/Auth/AdminUserService.cs` — nuevo (puede incluir la impl EF de `IAdminUserStore`)
- `LicensingAdmin/Auth/IAdminUserStore.cs` — nuevo (`IAdminUserStore` + `EfAdminUserStore` en el mismo archivo, como `AdminCredentialService.cs`)
- `LicensingSystem.Tests/AdminUserServiceTests.cs` — nuevo: clase `AdminUserServiceTests`
- `LicensingAdmin/Program.cs` — edit: `AddScoped<IAdminUserStore, EfAdminUserStore>()` + `AddScoped<AdminUserService>()`

**Acceptance**

1. **WHEN** `LicensingAdmin/Pages/Admin/Users.razor` is inspected **THE SYSTEM SHALL** carry `@attribute [Authorize(Policy = AuthPolicies.AdminUserAccess)]`.
2. **WHEN** `AdminUserService.CreateAsync(email, role, tempPassword)` runs **THE SYSTEM SHALL** build an `AdminUser` whose `PasswordHash` comes from `PasswordHasherService.Hash` (never the plaintext) and whose `Role` is the requested role.
3. **WHEN** `CreateAsync` is given an email that already exists case-insensitively **THE SYSTEM SHALL** reject it and call no store write.
4. **WHEN** a successful create or an `IsActive` toggle occurs **THE SYSTEM SHALL** write an `AuditLogEntry` (`Action` "Created" or "Updated", `EntityType` "AdminUser").
5. **WHEN** `dotnet build LicensingSystem.sln` and `dotnet test --filter AdminUserService` run **THE SYSTEM SHALL** both exit 0 with all `AdminUserService` tests passed.

**Verify**

```bash
dotnet build LicensingSystem.sln
grep -q '@attribute \[Authorize(Policy = AuthPolicies\.AdminUserAccess)\]' LicensingAdmin/Pages/Admin/Users.razor
dotnet test --filter AdminUserService
```

**Checkpoint**

```bash
git add -A && git commit -m "step 15: admin-users"
git tag step-15-admin-users
```

### `E2-T8` — Smoke de solucion + regresion + auditoria de checkpoints

**Depends on:** E2-T6, E2-T7 · **Priority:** p0

Sin código de producto nuevo. Añade a `SmokeTests.cs` una `[Fact]` de regresión: genera una clave
con `LicenseKeyGenerator.NewKey()` y aserta que casa la misma regex que
`ActivationService.LicenseKeyFormat()` (copiada literal de §5 del blueprint). Corre la puerta
completa. El conteo de tags se aserta en el bloque **Checkpoint**, después del propio `git tag` de
este paso — un `Verify` no puede depender de lo que produce su propio `Checkpoint`.

**En el mismo commit**, cierra los 2 warnings-as-errors preexistentes que hacen fallar
`dotnet build -warnaserror` (puerta de este paso, criterio 1): `MUD0002` en
`LicensingAdmin/Pages/PendingReview.razor` (`AlignItems` en `MudGrid` — quitar el atributo o
`Justify`) y `xUnit2012` en `LicensingSystem.Tests/CurrentAdminEmailEdgeCasesTests.cs`
(`Assert.Contains`/`Assert.Single` mal usados — usar la sobrecarga tipada). Son fixes de lint, no
lógica de producto.

**Files**
- `LicensingSystem.Tests/SmokeTests.cs` — edit: aserción de regresión
- `LicensingAdmin/Pages/PendingReview.razor` — edit: cerrar `MUD0002`
- `LicensingSystem.Tests/CurrentAdminEmailEdgeCasesTests.cs` — edit: cerrar `xUnit2012`

**Acceptance**

1. **WHEN** `dotnet build LicensingSystem.sln -warnaserror` runs **THE SYSTEM SHALL** exit 0 with no analyzer warning escalated to an error, including the two pre-existing ones this step clears: `MUD0002` in `LicensingAdmin/Pages/PendingReview.razor` and `xUnit2012` in `LicensingSystem.Tests/CurrentAdminEmailEdgeCasesTests.cs`.
2. **WHEN** `dotnet test` runs the full suite **THE SYSTEM SHALL** exit 0 with 0 failed and 0 skipped.
3. **WHEN** `grep -RIl "support-staff@vendor.com" LicensingAdmin/` runs **THE SYSTEM SHALL** find no match and exit 1.
4. **WHEN** `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` runs **THE SYSTEM SHALL** find no match and exit 1.
5. **WHEN** this step's own checkpoint tag exists **THE SYSTEM SHALL** make `git tag -l 'step-*'` list exactly 16 tags, one per build step.

**Verify**

```bash
dotnet build LicensingSystem.sln -warnaserror
dotnet test
grep -RIl "support-staff@vendor.com" LicensingAdmin/; test $? -eq 1
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1
```

**Checkpoint**

```bash
git add -A && git commit -m "step 16: smoke"
git tag step-16-smoke
test "$(git tag -l 'step-*' | wc -l)" -eq 16   # expect: exit 0 — 16 tags, uno por paso; se comprueba aquí, ya con el tag de este paso creado
```

---

## Epic acceptance

El epic está hecho cuando cada tarea está `done` **y**:

1. **WHEN** `dotnet build LicensingSystem.sln -warnaserror && dotnet test` runs from the repo root **THE SYSTEM SHALL** exit 0 with 0 failed and 0 skipped across the whole suite.
2. **WHEN** `grep -RIl "support-staff@vendor.com" LicensingAdmin/` and `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` run **THE SYSTEM SHALL** each find no match and exit 1.

```bash
dotnet build LicensingSystem.sln -warnaserror && dotnet test
grep -RIl "support-staff@vendor.com" LicensingAdmin/; test $? -eq 1
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1
```

## Pitfalls

- **`AdminCredentialService` con `DbContext` en el test.** No. Usa el fake `IAdminUserLookup`. Solo
  `ValidateAsync` + `BuildPrincipal` se prueban en unidad; el `WebApplicationFactory` de `E2-T2`
  cubre el pipeline anónimo; el sign-in real con cookie lo cubre el `# manual:` de `E2-T6`.
- **`WebApplicationFactory<Program>` no arranca porque `Program` es `internal`.** `E2-T2` añade
  `public partial class Program { }` al final de `Program.cs`. Sin `InternalsVisibleTo`.
- **El `WebApplicationFactory` intenta conectar a Postgres.** No debe: la petición anónima recibe
  302 antes de tocar la BD; `E2-T2` fija una cadena ficticia y `E2-T3` —en el mismo commit que
  registra `AddHostedService<AdminSeeder>()`— quita ese `IHostedService` en `ConfigureTestServices`.
  Si un test del pipeline necesita la BD, el test está mal planteado.
- **Borrar solo la asignación de `entity.ReviewedBy` en `PendingReview.razor` y olvidar `Actor`.**
  Ambos usan `CurrentAdmin.Email(...)`; el `grep` de `support-staff@vendor.com` falla si queda
  cualquiera de los dos.
- **`MudDatePicker` habilitado sin el flag `Subscription`.** Debe estar `Disabled` salvo que el flag
  esté marcado (criterio 4 de `E2-T6`).
- **Marcar `E2-T8` done antes de que exista `step-16-smoke`.** El conteo de 16 tags va en el
  Checkpoint, después del `git tag` — no en `Verify`.
- **`# manual:` como puerta.** No lo es. `E2-T6` está `done` cuando `dotnet build`, el `grep -q` y
  `dotnet test` salen 0; la línea `# manual:` solo anota el pase humano.

## Before moving on

- [ ] Cada tarea de este epic está `done` en `tasks.json` — ninguna quedó `in_progress`.
- [ ] Cada comando `verify` de cada tarea pasó, no solo el primero (la línea `# manual:` de `E2-T6`
      no cuenta como puerta).
- [ ] Ningún comando `verify` fue editado, y ninguno se saltó porque un archivo que nombra no existía.
- [ ] Cada tarea tiene su tag de checkpoint en git — `git tag -l 'step-*' | wc -l` da 16.
- [ ] `dotnet build LicensingSystem.sln -warnaserror && dotnet test` pasa limpio desde la raíz.
- [ ] Cada contrato "Producido" arriba existe con la firma indicada.
- [ ] Ningún archivo fuera del subárbol fue modificado — en particular ningún contrato congelado.
- [ ] Este proyecto no usa `.env` — no hay `.env.example` que actualizar.
- [ ] Un commit por tarea, cada uno prefijado `step NN:`, cada uno seguido de su tag.
