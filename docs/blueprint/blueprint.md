# LicensingSystem — Blueprint (Slice 1)

> Generado por The Architect el 2026-09-02
> Tipo: cambio brownfield sobre una solución .NET 8 existente (no proyecto nuevo)
> Runtime track: `dotnet` (.NET 8) — derivado de los `.csproj` del repo; no hay archivo de track
> Emission mode: bundle (16 pasos de build, 2 epics de 8)
> Blueprint version: 1
> Versiones verificadas por última vez: 2026-09-02 — ver §11 para procedencia por paquete

Este blueprint cubre **el slice 1** del backlog, no el backlog completo. Alcance: (1) sacar la
credencial de base de datos en texto plano de ambos `appsettings.json` **y de los README**,
(2) autenticación real de administradores contra la tabla `admin_users` existente, (3) una pantalla
"Generar nueva licencia" que emite una clave firmada. Todo lo demás está en §1 Non-Goals.

---

## 1. Project Overview & Non-Goals

### Vision

`LicensingSystem` es un sistema de licencias de software para un vendor: una API de activación
(`LicensingApi`) que consume el DLL de activación del cliente, un panel de administración Blazor
Server (`LicensingAdmin`) para el personal de soporte, y una biblioteca compartida (`LicensingCore`)
con entidades y acceso a datos. Todo corre sobre PostgreSQL en un único host Windows/IIS.

El código ya existe y funciona. Este slice cierra tres huecos concretos que bloquean el uso real del
panel: la cadena de conexión viaja con contraseña en texto plano dentro del repo (en `appsettings.json`
y en dos README); el panel no tiene autenticación (cualquiera que llegue a la URL aprueba activaciones,
y el revisor queda registrado como el literal `"support-staff@vendor.com"`); y no hay forma de emitir
una licencia nueva desde la interfaz. El slice es aditivo: no cambia el esquema de base de datos ni
ningún contrato de cara al DLL de activación.

### Users

| Persona | A qué viene | Frecuencia |
|---|---|---|
| SuperAdmin | Gestionar administradores, emitir licencias, revisar activaciones, ver todo | semanal |
| SupportStaff | Emitir licencias, aprobar/rechazar activaciones pendientes, consultar licencias | diaria |
| ReadOnlyViewer | Consultar el dashboard y el listado de licencias, sin modificar nada | diaria |

### Goals — alcance del slice 1

1. El repo no contiene ninguna credencial de base de datos en texto plano en **ningún** archivo
   rastreado (fuera de `docs/blueprint/`); ambas apps obtienen la cadena de conexión de
   `dotnet user-secrets` o de la variable de entorno `ConnectionStrings__LicensingDb`, y fallan de
   forma ruidosa al arrancar si falta.
2. El panel exige inicio de sesión con usuario y contraseña locales contra `admin_users`; cada
   pantalla y cada acción que muta datos está protegida por autorización server-side (una política de
   fallback que exige sesión + un `[Authorize(Policy = ...)]` por página), un test de integración
   prueba que una petición anónima a una ruta protegida recibe un 302 a `/Account/Login`, y toda
   mutación registra un `AuditLogEntry` con el email del administrador autenticado.
3. Un administrador con rol `SupportStaff` o `SuperAdmin` puede crear (o elegir) un `SoftwareProduct`
   y emitir una `License` con clave que cumple `LicenseKeyFormat()` y firma RSA-SHA256 verificable
   sin conexión.
4. El primer administrador se crea mediante un seed de arranque parametrizado por configuración, y
   existe una pantalla de gestión de administradores (`/admin/users`) restringida a `SuperAdmin`.

### Non-Goals — explícitamente fuera de alcance del slice 1

**El builder no implementa nada de esta tabla.** Si un paso parece requerir una de estas filas, es
un defecto del blueprint: parar y reportar, no ampliar el alcance.

| No se construye | Por qué no ahora | Revisar cuando |
|---|---|---|
| Pantalla de Reportes / exportación PDF·XLS | Nadie la ha pedido con datos concretos; superficie grande | Compliance pida exportaciones |
| Pantalla de Notification Settings (config SMTP) | El envío de correos no está priorizado | Se prioricen las notificaciones por email |
| Rate limiting en `/api/activate` y `/api/checkin` | La API aún no es de cara a internet; sigue como `TODO` en `ActivationController` | Antes de exponer la API a internet |
| Cablear `LicensingAdmin` para llamar a endpoints admin de `LicensingApi` en vez de ir a la BD directo | Ambas apps comparten host; el acceso directo funciona | Admin y API se separen en hosts distintos |
| Migraciones EF Core reales que reemplacen `001_initial_schema.sql` | Este slice no cambia el esquema | Un slice necesite un cambio de esquema real |
| Cambios a `POST /api/activate` o `POST /api/checkin` (forma de request/response, mapeo de status) | Contrato de cable congelado con el DLL de activación | Nunca dentro de este slice |
| Cambios a `GET /health` o al enum `ResultCode` (valores y orden) | Contrato de cable congelado | Nunca dentro de este slice |
| Cambios al sobre del archivo de licencia que produce `LicenseFileService` | Contrato de cable congelado | Nunca dentro de este slice |
| Cambios a la regex `LicenseKeyFormat()` | El generador se ajusta a ella; nunca al revés | Nunca dentro de este slice |
| Suite automatizada de accesibilidad | MudBlazor da una base razonable; coste alto para el valor de este slice | El panel crezca a un público más amplio |
| Tests de integración de datos contra PostgreSQL real | Se difiere a favor de tests unitarios puros con fakes + un test de pipeline HTTP sin BD | Un slice necesite garantías end-to-end de datos |

### Success metrics

| Métrica | Objetivo | Cómo se mide |
|---|---|---|
| Credenciales en texto plano en archivos rastreados del repo | 0 | `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` → exit 1 (§20.1) |
| Acciones admin que mutan datos sin `AuditLogEntry` | 0 | Revisión de código + tests de `LicenseIssuanceService` / `AdminUserService` / login |
| Aprobaciones/rechazos con revisor `"support-staff@vendor.com"` | 0 tras el slice | `grep -RIl "support-staff@vendor.com" LicensingAdmin/` → exit 1 (§20.1) |
| Rutas protegidas que sirven contenido a una petición anónima | 0 | `AuthorizationPipelineTests` (paso 10) — GET anónimo a `/` y `/pending-review` → 302 |

---

## 2. Tech Stack

Esta tabla nombra *decisiones*, no versiones. Cada pin vive en §11 y en ningún otro sitio. La
solución ya existe; casi todo aquí es "lo que el repo ya usa, y no se cambia".

| Capa | Elección | Por qué esta, frente a qué |
|---|---|---|
| Lenguaje / runtime | C# / .NET 8 (`net8.0`) | Es lo que la solución ya usa; sin `global.json`, se toma el SDK 8.0.4xx+ |
| Biblioteca compartida | `LicensingCore` (`Microsoft.NET.Sdk`, class lib) | Ya existe; aloja entidades, `AppDbContext` y ahora el firmador de licencias (compartible) |
| API | `LicensingApi` (`Microsoft.NET.Sdk.Web`, minimal hosting) | Ya existe; este slice solo le añade el guardián de cadena de conexión |
| Panel | `LicensingAdmin` (`Microsoft.NET.Sdk.Web`, Blazor Server) | Ya existe; render `ServerPrerendered`, sin cambios de modelo de render |
| Component layer | MudBlazor 7.15.0 | Ya adoptado en las 3 pantallas actuales; las nuevas componen los mismos componentes |
| Base de datos | PostgreSQL 15+ (`Host=172.16.101.12;Port=5432;Database=licensing_app`) | Ya en producción; el slice no toca el esquema |
| ORM / acceso a datos | EF Core 8 vía `Npgsql.EntityFrameworkCore.PostgreSQL` | Ya en uso; el panel accede directo con `IDbContextFactory<AppDbContext>` |
| Auth | Cookie auth local contra `admin_users` (`Microsoft.AspNetCore.Authentication.Cookies`) + `FallbackPolicy` | Frente a Windows/AD o un IdP externo: un solo host interno, sin infraestructura de identidad; ver §20.3 #1 |
| Hashing de contraseñas | `Microsoft.AspNetCore.Identity.PasswordHasher<AdminUser>` (PBKDF2) | Frente a BCrypt/Argon2: viene en el shared framework, sin dependencia nueva |
| Firma de licencias | `RSA` + RSA-SHA256 / `RSASignaturePadding.Pkcs1` en `LicensingCore/Crypto` | Frente a un endpoint admin nuevo en la API: ambas apps comparten host; ver §20.3 #2 |
| Background work | `IHostedService` para el seed de arranque del primer admin | Frente a un comando `dotnet run -- seed-admin` o SQL manual; ver §20.3 #3 |
| Payments | NOT APPLICABLE | El sistema no cobra nada |
| File storage | NOT APPLICABLE | Sin almacenamiento de archivos en este slice |
| Email / notificaciones | NOT APPLICABLE en este slice | La pantalla de Notification Settings es un Non-Goal |
| Hosting | Único host Windows/IIS, ambas apps + Postgres co-ubicados, sin CI | Es el despliegue actual; el slice no lo cambia |
| Test runner | xUnit v3 en un proyecto nuevo `LicensingSystem.Tests` + `WebApplicationFactory<Program>` para el pipeline | Frente a xUnit v2 / NUnit / MSTest: la entrevista fijó xUnit v3 |
| Package manager | NuGet (integrado en el SDK de .NET) | Estándar de .NET |

### Compatibility check

Checked against the plugin's `knowledge/stack-compatibility.md` — no known-bad
combinations; stack íntegramente Microsoft .NET 8, y los paquetes de test son aditivos. Ninguno de
`Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio`, `coverlet.collector` ni
`Microsoft.AspNetCore.Mvc.Testing` depende de Npgsql, EF Core o MudBlazor; `Microsoft.AspNetCore.Mvc.Testing`
8.0.30 mantiene todas sus dependencias transitivas en la línea 8.0.x, así que no hay conflicto ni
arrastre de un runtime más nuevo que .NET 8.

---

## 3. Directory Structure

Árbol del repo tras el slice. `[EXISTE]` = ya está y no se toca salvo nota. `[EDIT]` = un paso lo
modifica. `[NUEVO]` = un paso lo crea. `[COPIA]` = llega al copiar `docs/blueprint/workspace/` a la
raíz (§10 Bootstrap).

```
licensing-system/
  LicensingSystem.sln              # [EDIT paso 1] añade el proyecto LicensingSystem.Tests
  .gitignore                       # [EXISTE] ya bloquea bin/ obj/ .vs/ secrets.json appsettings.*.Local.json *.pfx *.pem
  LicensingSystem_README.md        # [EDIT paso 3] cadena de conexión como placeholder, sin contraseña real
  tasks.json                       # [NUEVO bundle] DAG de tareas — en la RAÍZ del repo (convención del equipo), no junto a blueprint.md
  CLAUDE.md                        # [COPIA] §19.1
  AGENTS.md                        # [COPIA] §19.2
  .claude/                         # [COPIA]
    settings.json                  # §19.3 — permisos pre-aprobados
    rules/
      entities.md                  # §19.5 — paths: LicensingCore/**
      admin-ui.md                  # §19.5 — paths: LicensingAdmin/**
    skills/
      add-admin-page/SKILL.md      # §19.4

  LicensingCore/
    LicensingCore.csproj           # [EXISTE] Npgsql.EntityFrameworkCore.PostgreSQL 8.0.10
    Configuration/
      ConnectionStringGuard.cs     # [NUEVO paso 2] static string Require(string?) — falla ruidoso si vacía
    Crypto/
      ILicenseSigner.cs            # [NUEVO paso 5] byte[] Sign(License) / bool Verify(License, byte[], RSA)
      LicenseSigner.cs             # [NUEVO paso 5] RSA-SHA256, PKCS1; payload canónico (ver §8 D2)
    Licensing/
      LicenseKeyGenerator.cs       # [NUEVO paso 4] static string NewKey() — cripto-aleatoria, formato 4-5-4-4-4-4-2
    Data/
      AppDbContext.cs              # [EXISTE] expone DbSet<> con nombre: SoftwareProducts, Licenses, Activations, AdminUsers, AuditLogEntries, NotificationConfigs
    Entities/                      # [EXISTE] SoftwareProduct, License, Activation, AdminUser, AuditLogEntry, NotificationConfig, Enums — sin cambios

  LicensingApi/
    LicensingApi.csproj            # [EDIT paso 1] añade <UserSecretsId>
    appsettings.json               # [EDIT paso 3] ConnectionStrings:LicensingDb -> "" ; se retira la contraseña
    Program.cs                     # [EDIT paso 2] envuelve la lectura de la cadena con ConnectionStringGuard.Require
    README.md                      # [EDIT paso 3] cadena de conexión como placeholder, sin contraseña real
    Controllers/ActivationController.cs   # [EXISTE] CONGELADO — /api/activate, /api/checkin
    Services/                      # [EXISTE] ActivationService, HardwareMatchService, LicenseFileService — CONGELADOS
    Dtos/ResultCode.cs             # [EXISTE] CONGELADO — valores y orden
    Migrations/001_initial_schema.sql     # [EXISTE] SQL de referencia, NO cableado a __EFMigrationsHistory — sin cambios

  LicensingAdmin/
    LicensingAdmin.csproj          # [EDIT paso 1] añade <UserSecretsId>
    appsettings.json               # [EDIT paso 3] ConnectionStrings:LicensingDb -> ""
    Program.cs                     # [EDIT pasos 2,6,8,9,10,11] guardián, firmador DI, cookie auth + políticas + FallbackPolicy, auth-state provider, `public partial class Program`, seeder
    App.razor                      # [EDIT paso 8] CascadingAuthenticationState + AuthorizeRouteView + RedirectToLogin
    _Imports.razor                 # [EDIT paso 8] @using Microsoft.AspNetCore.Authorization / .Components.Authorization
    Shared/MainLayout.razor        # [EDIT paso 14] email + logout + <AuthorizeView> por enlace de nav
    Auth/
      PasswordHasherService.cs     # [NUEVO paso 7] Hash(string)->string / Verify(hash,password)->bool
      AuthPolicies.cs              # [NUEVO paso 8] constantes de nombre de política + registro + FallbackPolicy
      CryptoRegistration.cs        # [NUEVO paso 6] AddLicenseSigner(this IServiceCollection, IConfiguration)
      AdminAuthStateProvider.cs    # [NUEVO paso 9] RevalidatingServerAuthenticationStateProvider, intervalo 30 min
      AdminCredentialService.cs    # [NUEVO paso 9] IAdminUserLookup + ValidateAsync(email,password) + static ClaimsPrincipal BuildPrincipal(AdminUser)
      CurrentAdmin.cs              # [NUEVO paso 12] static string Email(ClaimsPrincipal)
      AdminUserService.cs          # [NUEVO paso 15] crear (rechaza email duplicado) / desactivar, con AuditLogEntry
      IAdminUserStore.cs           # [NUEVO paso 15] costura para tests con fake (sin DbContext)
    Licensing/
      LicenseIssuanceService.cs    # [NUEVO paso 13] elegir/crear producto, generar clave con reintento, firmar, persistir
      ILicenseStore.cs             # [NUEVO paso 13] costura: LicenseKeyExistsAsync / AddAsync(SoftwareProduct?, License, AuditLogEntry)
      EfLicenseStore.cs            # [NUEVO paso 13] impl real sobre IDbContextFactory<AppDbContext>
      LicenseIssuanceRequest.cs    # [NUEVO paso 13] DTO (producto existente O campos de producto nuevo; flags; máx activaciones; expiración; cliente)
    Startup/
      AdminSeeder.cs               # [NUEVO paso 11] IHostedService; static bool ShouldSeed(...) / static AdminUser BuildSuperAdmin(...)
    Pages/
      Dashboard.razor              # [EDIT paso 12] [Authorize(Policy = ViewerAccess)]
      Licenses.razor               # [EDIT paso 12] [Authorize(Policy = ViewerAccess)]
      PendingReview.razor          # [EDIT paso 12] [Authorize(Policy = ReviewAccess)] + revisor real, sin literal
      _Host.cshtml                 # [EXISTE] sin cambios
      Error.cshtml                 # [EXISTE] sin cambios
      Account/
        Login.cshtml               # [NUEVO paso 10] formulario de inicio de sesión (Razor Page)
        Login.cshtml.cs            # [NUEVO paso 10] [AllowAnonymous]; GET renderiza; POST -> ValidateAsync -> SignInAsync -> AuditLogEntry "Login"
        Logout.cshtml.cs           # [NUEVO paso 10] SignOutAsync -> redirect /Account/Login
      Licenses/
        New.razor                  # [NUEVO paso 14] /licenses/new, [Authorize(Policy = IssueAccess)]
      Admin/
        Users.razor                # [NUEVO paso 15] /admin/users, [Authorize(Policy = AdminUserAccess)]
    wwwroot/css/site.css           # [EXISTE] sin cambios

  LicensingSystem.Tests/           # [NUEVO paso 1] Microsoft.NET.Sdk, net8.0, xUnit v3, <OutputType>Exe</OutputType>, <IsPackable>false</IsPackable>
    LicensingSystem.Tests.csproj   # ProjectReference -> LicensingCore, LicensingAdmin; FrameworkReference Microsoft.AspNetCore.App; 5 PackageReference (§11)
    SmokeTests.cs                  # [NUEVO paso 1; EDIT paso 16] Assert.True(true) + una aserción de regresión final
    ConnectionStringGuardTests.cs  # [NUEVO paso 2]
    LicenseKeyGeneratorTests.cs    # [NUEVO paso 4]
    LicenseSignerTests.cs          # [NUEVO paso 5]
    CryptoRegistrationTests.cs     # [NUEVO paso 6] métodos prefijados LicenseSignerDi_*
    PasswordHasherServiceTests.cs  # [NUEVO paso 7]
    AuthPoliciesTests.cs          # [NUEVO paso 8]
    AdminCredentialServiceTests.cs # [NUEVO paso 9]
    AuthorizationPipelineTests.cs  # [NUEVO paso 10] WebApplicationFactory<Program>; GET anónimo -> 302 / 200
    AdminSeederTests.cs            # [NUEVO paso 11]
    CurrentAdminTests.cs          # [NUEVO paso 12]
    LicenseIssuanceServiceTests.cs # [NUEVO paso 13]
    AdminUserServiceTests.cs      # [NUEVO paso 15]

  docs/blueprint/                  # [NUEVO bundle] este blueprint — vive DENTRO del proyecto, no en ./blueprints/
    blueprint.md
    epics/01-fundamentos.md
    epics/02-auth-y-licencias.md
    workspace/                     # se copia su CONTENIDO a la raíz del repo (§10 Bootstrap)
      CLAUDE.md
      AGENTS.md
      .claude/settings.json
      .claude/rules/entities.md
      .claude/rules/admin-ui.md
      .claude/skills/add-admin-page/SKILL.md
```

**Boundary rules**

- `LicensingCore` no referencia `LicensingApi` ni `LicensingAdmin`. El firmador (`Crypto/`), el
  generador de claves (`Licensing/`) y el guardián (`Configuration/`) van aquí porque ambas apps —
  o los tests — los necesitan.
- `LicensingAdmin` accede a datos **solo** vía `IDbContextFactory<AppDbContext>` + `await using var
  db`, nunca a través de `LicensingApi`. Esto es intencional en este slice (Non-Goal: admin→API).
- El sign-in con cookie (`HttpContext.SignInAsync`) ocurre **solo** en Razor Pages bajo
  `Pages/Account/`. Un componente Blazor no tiene acceso al `HttpContext` crudo.
- La autorización efectiva es server-side: una `FallbackPolicy` que exige sesión (§8) + un
  `[Authorize(Policy = ...)]` por página. `<AuthorizeView>` en `MainLayout` es **solo cosmético**
  (oculta enlaces), nunca la única barrera.
- `LicensingSystem.Tests` referencia `LicensingCore` y `LicensingAdmin` por `ProjectReference`;
  toda la lógica bajo prueba es `public` (sin `InternalsVisibleTo`), salvo `Program` de
  `LicensingAdmin`, que el paso 10 hace visible con `public partial class Program { }` para
  `WebApplicationFactory<Program>`. Las impl. EF usan los `DbSet<>` con nombre de `AppDbContext`
  (`SoftwareProducts`, `Licenses`, `Activations`, `AdminUsers`, `AuditLogEntries`, `NotificationConfigs`)
  o `db.Set<T>()` — ambos existen.

Este blueprint **no enuncia ninguna convención de import, especificador, alias o enlace**: .NET
resuelve tipos por `ProjectReference` y nombre de ensamblado declarados en `LicensingSystem.sln`, y
`dotnet build` y `dotnet test` usan el mismo resolvedor. Ver §19.6, *Resolution convention matrix*.
Cada ruta de salida dibujada arriba (p. ej. el `.csproj` de tests) tiene un único origen —
el paso que la crea; ver §19.6, *Cross-artifact value reconciliation*.

---

## 4. Data Model

### Delta

NONE. This slice adds and alters no tables or columns. `admin_users` (unused until now),
`software_products` and `licenses` are already defined in `LicensingCore/Data/AppDbContext.cs` and
`LicensingApi/Migrations/001_initial_schema.sql`. New rows are written to existing tables only.

### Tablas que el slice lee/escribe

Resumen a partir de `LicensingCore/Entities/*.cs`. Nombres de tabla en `snake_case` vía `ToTable`;
enums como `varchar` con `HasConversion<string>()` **excepto** `LicenseModel`, que es un bitmask
`int` (`[Flags]`).

**`admin_users`** — cuentas del panel. Antes de este slice no la usaba ningún código; a partir de
aquí es la fuente de identidad para el login.

| Campo | Tipo | Restricciones | Significado |
|---|---|---|---|
| `Id` | `Guid` | PK | — |
| `Email` | `string` | requerido, único, ≤320 | identificador de login; búsqueda case-insensitive |
| `PasswordHash` | `string` | requerido | hash PBKDF2 de `PasswordHasher<AdminUser>`; nunca texto plano |
| `Role` | `AdminRole` → `varchar` | — | `SuperAdmin=0`, `SupportStaff=1`, `ReadOnlyViewer=2` |
| `IsActive` | `bool` | default `true` | un usuario inactivo no puede iniciar sesión aunque la contraseña sea correcta |
| `CreatedAtUtc` | `DateTime` | — | — |
| `LastLoginAtUtc` | `DateTime?` | nullable | lo fija el POST de login al autenticar (paso 10) |

**`software_products`** — producto licenciable. El slice crea filas nuevas desde la pantalla de
emisión.

| Campo | Tipo | Restricciones | Significado |
|---|---|---|---|
| `Id` | `Guid` | PK | — |
| `Name` | `string` | requerido, ≤200 | — |
| `Vendor` | `string` | requerido, ≤200 | — |
| `CurrentVersion` | `string?` | ≤50 | — |
| `DefaultLicenseModel` | `LicenseModel` → `int` bitmask | default `Machine` | modelo(s) por defecto ofrecidos |
| `DefaultMaxActivations` | `int` | default 5 | — |
| `CreatedAtUtc` / `UpdatedAtUtc` | `DateTime` / `DateTime?` | — | — |
| `Licenses` | colección | — | navegación inversa |

**`licenses`** — clave de licencia emitida. El slice crea filas nuevas desde la pantalla de emisión.

| Campo | Tipo | Restricciones | Significado |
|---|---|---|---|
| `Id` | `Guid` | PK | — |
| `ProductId` | `Guid` | FK → `software_products`, `RESTRICT` on delete | — |
| `LicenseKey` | `string` | requerido, ≤50, único | cumple `LicenseKeyFormat()` (§5) |
| `ModelSnapshot` | `LicenseModel` → `int` | — | congelado en el momento de emisión, copiado del request/producto |
| `MaxActivations` | `int` | default 5 | congelado en emisión |
| `SubscriptionExpiryUtc` | `DateTime?` | nullable | solo significativo si `ModelSnapshot` incluye `Subscription` |
| `Status` | `LicenseStatus` → `varchar` | — | `Active`, `Revoked`, `Expired` |
| `Signature` | `byte[]` | requerido | RSA-SHA256 sobre los campos canónicos (§8 D2); el DLL valida autenticidad offline |
| `CustomerEmail` / `CustomerName` | `string?` | ≤320 / ≤200 | — |
| `CreatedAtUtc` / `RevokedAtUtc` / `RevokedReason` | — | — | — |

**`activations`** — instalación de una licencia en una máquina. El slice **no** añade columnas; el
paso 12 escribe `ReviewedBy` al aprobar/rechazar, y el código preexistente `PendingReview.Resolve`
también fija `Status`, `ApprovedAtUtc`/`RejectedAtUtc` y `ReviewDeadlineUtc`.

| Campo | Tipo | Restricciones | Significado |
|---|---|---|---|
| `Id` | `Guid` | PK | — |
| `LicenseId` | `Guid` | FK → `licenses` | — |
| `Status` | `ActivationStatus` → `varchar` | — | `PendingReview`, `Approved`, `Rejected`, `Revoked` |
| `ReviewedBy` | `string?` | ≤320 | **antes** de este slice: literal `"support-staff@vendor.com"`; **tras** el paso 12: `CurrentAdmin.Email(...)` |
| `ApprovedAtUtc` / `RejectedAtUtc` / `ReviewDeadlineUtc` | `DateTime?` | nullable | los fija `PendingReview.Resolve` (preexistente) |

**`audit_log_entries`** — registro inmutable de toda acción que muta datos. El slice **añade** filas
en cada login, cada aprobación/rechazo, cada emisión de licencia y cada alta/baja de administrador.

| Campo | Tipo | Restricciones | Significado |
|---|---|---|---|
| `Id` | `Guid` | PK | — |
| `Actor` | `string` | requerido, ≤320 | email del admin autenticado, o `"system"` para acciones automáticas |
| `EntityType` | `string` | requerido, ≤100 | `"AdminUser"`, `"License"`, `"Activation"`, ... |
| `EntityId` | `string` | requerido, ≤100 | — |
| `Action` | `string` | requerido, ≤100 | `"Login"`, `"Created"`, `"Approved"`, `"Rejected"`, `"Updated"`, ... |
| `DetailsJson` | `string?` (`jsonb`) | nullable | contexto extra |
| `CreatedAtUtc` | `DateTime` | — | — |

### Relationships

- `SoftwareProduct` —(1)→(N)— `License` — borrado `RESTRICT` (no se borra un producto con licencias).
- `License` —(1)→(N)— `Activation` — sin cambios de esquema en este slice.
- `AdminUser`, `AuditLogEntry` — sin relaciones FK relevantes para el slice.

### Indexes

| Tabla | Índice | Por qué |
|---|---|---|
| `admin_users` | único en `Email` | login por email; ya definido |
| `licenses` | único en `LicenseKey` | el generador reintenta ante colisión; ya definido |

Este slice **no añade índices**.

### Schema

Sin cambios. El esquema vigente está en `LicensingApi/Migrations/001_initial_schema.sql` (SQL de
referencia, no cableado a `__EFMigrationsHistory`) y en la configuración fluida de
`LicensingCore/Data/AppDbContext.cs`. El builder no edita ninguno de los dos en este slice.

### Migrations

**N/A en este slice.** No hay cambio de esquema, así que no se generan migraciones EF y no se toca
`001_initial_schema.sql`. Reemplazar el SQL de referencia por migraciones EF reales es un Non-Goal.
La regla para futuros slices: un cambio de esquema toca a la vez la entidad, `AppDbContext.OnModelCreating`
y `001_initial_schema.sql`.

### Seed data

El primer `SuperAdmin` se inserta por `LicensingAdmin/Startup/AdminSeeder.cs` (`IHostedService`) al
arrancar el panel, **solo si** `admin_users` está vacía y ambas claves `Admin:BootstrapEmail` y
`Admin:BootstrapPassword` están configuradas (user-secrets / env `Admin__BootstrapEmail`,
`Admin__BootstrapPassword`). Inserta un `AdminUser` con contraseña hasheada + un `AuditLogEntry`
(`Actor`="system", `Action`="Created", `EntityType`="AdminUser"). Es no-op si la tabla no está vacía
o si faltan las claves. No hay comando de seed ni fixture de BD para los tests (son unitarios puros;
`AuthorizationPipelineTests` usa una cadena de conexión ficticia y, desde el paso 11 —el que
registra `AddHostedService<AdminSeeder>()`—, quita el `AdminSeeder` en `ConfigureTestServices`).

---

## 5. API Design

Este slice **no añade ni modifica ningún endpoint HTTP**. La emisión de licencias ocurre in-process
dentro del panel Blazor (`LicenseIssuanceService`), no como endpoint (ver §20.3 #2). Esta sección
documenta lo que queda **congelado**.

### Convenciones (existentes, sin cambios)

- Base path de la API: `/api` (`[Route("api")]` en `ActivationController`).
- El cliente (DLL) hace `switch` sobre el enum `ResultCode`, no parsea texto libre. Un código no
  reconocido debe hacer que el cliente falle **seguro** (mantiene el último estado local conocido).
- `GET /health` devuelve `{ "status": "ok" }` y **no** comprueba la base de datos (se deja así).

### Interfaces held constant (contratos de cable — el builder NO los toca)

| # | Contrato | Cómo se prueba la paridad | Tolerancia |
|---|---|---|---|
| 1 | `POST /api/activate` — forma de request/response y mapeo de status | Ningún paso del §9 lo modifica; `dotnet build` compila `ActivationController`/`ActivationService` sin cambios | coincidencia exacta (0 diffs) |
| 2 | `POST /api/checkin` — forma de request/response y mapeo de status | Igual que arriba | coincidencia exacta |
| 3 | `GET /health` → `{ "status": "ok" }` | Igual que arriba | coincidencia exacta |
| 4 | Enum `ResultCode` — valores y orden: `Activated, PendingReview, Renewed, Locked, InvalidKeyFormat, LicenseNotFound, LicenseExpired, LicenseRevoked, MaxActivationsReached, InstallGuidMismatch, ActivationNotFound, RateLimited, ServerError` | `grep` de `ResultCode.cs` sin cambios; `dotnet build` | orden exacto |
| 5 | Sobre del archivo de licencia que produce `LicensingApi/Services/LicenseFileService.cs` | `LicenseFileService.cs` no aparece en ningún paso; **es distinto del firmado del registro `License` del §8 D2** | coincidencia exacta |
| 6 | Regex `LicenseKeyFormat()` en `LicensingApi/Services/ActivationService.cs`: `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$` | El generador del paso 4 y la emisión del paso 13 se ajustan a esta regex (copiada literal en sus tests); nunca se edita `ActivationService.cs` | coincidencia exacta |

**Este es el único literal que un paso del §9 reproduce**: la regex de la fila 6. Su fuente única es
esta tabla; los pasos 4, 13 y 16 y sus tests la copian carácter por carácter. No es una salida
producida por el runtime, así que no requiere reconciliación de `§19.6 Byte-exact` (ver esa subsección).

### Routes

| Method | Path | Descripción | Auth | Rate limit |
|---|---|---|---|---|
| POST | `/api/activate` | CONGELADO — activación del DLL | pública (firma en el cuerpo) | ninguno (Non-Goal) |
| POST | `/api/checkin` | CONGELADO — check-in periódico del DLL | pública | ninguno (Non-Goal) |
| GET | `/health` | CONGELADO — liveness, no chequea BD | pública | ninguno |

### Critical endpoints — full detail

NOT APPLICABLE — este slice no añade endpoints y no modifica los tres congelados; sus esquemas de
request/response no se reproducen aquí porque ningún criterio de aceptación del §9 depende de ellos.
El detalle vive en el código existente (`ActivationController`, `ActivationService`, `ActivationDtos.cs`).

---

## 6. Frontend Architecture

Blazor Server + MudBlazor 7.15.0, ya establecido. Render `ServerPrerendered` (como hoy). El panel
accede a datos directo con `@inject IDbContextFactory<AppDbContext>` + `await using var db`, salvo
las pantallas nuevas que pasan por servicios (`LicenseIssuanceService`, `AdminUserService`).

### Routes

| Route | Página | Fuente de datos | Auth (política) |
|---|---|---|---|
| `/` | `Pages/Dashboard.razor` [EXISTE] | DB-directo (`IDbContextFactory`) | `ViewerAccess` (paso 12) |
| `/licenses` | `Pages/Licenses.razor` [EXISTE] | DB-directo | `ViewerAccess` (paso 12) |
| `/pending-review` | `Pages/PendingReview.razor` [EXISTE] | DB-directo; escribe `Activation` + `AuditLogEntry` | `ReviewAccess` (paso 12) |
| `/Account/Login` | `Pages/Account/Login.cshtml` [NUEVO paso 10] — **Razor Page**, no componente | `AdminCredentialService` (DB-directo vía costura) | anónima (`[AllowAnonymous]`) |
| `/Account/Logout` | `Pages/Account/Logout.cshtml.cs` [NUEVO paso 10] — Razor Page (solo handler) | `HttpContext.SignOutAsync` | autenticada |
| `/licenses/new` | `Pages/Licenses/New.razor` [NUEVO paso 14] | `LicenseIssuanceService` (elige/crea `SoftwareProduct`, emite `License`) | `IssueAccess` (paso 14) |
| `/admin/users` | `Pages/Admin/Users.razor` [NUEVO paso 15] | `AdminUserService` (costura `IAdminUserStore`) | `AdminUserAccess` (paso 15) |

### Rendering strategy

Todas las páginas Blazor: `ServerPrerendered` vía `MapFallbackToPage("/_Host")` — sin cambios.
`/Account/Login` y `/Account/Logout` son Razor Pages servidas por `AddRazorPages()` (ya registrado);
el sign-in con cookie necesita el `HttpContext` crudo, imposible desde un componente Blazor.
`App.razor` pasa de `<RouteView>` a `<CascadingAuthenticationState>` + `<AuthorizeRouteView>` con
un `<NotAuthorized>` que renderiza un componente `RedirectToLogin` (navega a
`/Account/Login?returnUrl=`; durante el SSR produce el 302 que asserta `AuthorizationPipelineTests`).

### Component hierarchy

```
App.razor  [EDIT paso 8]
  CascadingAuthenticationState
    Router
      AuthorizeRouteView (DefaultLayout = MainLayout)
        NotAuthorized -> RedirectToLogin (a /Account/Login?returnUrl=)
        Authorized    -> la página, con @attribute [Authorize(Policy = ...)]

MainLayout.razor  [EDIT paso 14]
  MudAppBar
    <AuthorizeView>  -> email del usuario + enlace "Cerrar sesión" (a /Account/Logout)
  MudDrawer / MudNavMenu
    <AuthorizeView Policy="ViewerAccess">    -> Dashboard, Licencias
    <AuthorizeView Policy="ReviewAccess">    -> Pending Review
    <AuthorizeView Policy="IssueAccess">     -> Generar licencia
    <AuthorizeView Policy="AdminUserAccess"> -> Usuarios admin

Pages/Licenses/New.razor  [NUEVO paso 14]
  MudForm
    MudSelect (software_products existentes) + switch "Nuevo producto" -> Name/Vendor/CurrentVersion/DefaultMaxActivations
    checkboxes de LicenseModel [Flags]
    MudNumericField MaxActivations
    MudDatePicker SubscriptionExpiryUtc  (habilitado solo si el flag Subscription está marcado)
    MudTextField CustomerEmail / CustomerName
  -> LicenseIssuanceService.IssueAsync
  MudPaper con la clave emitida + botón copiar + enlace "Volver a Licencias"
```

### State management

Estado de servidor: el circuito Blazor Server. Cada acción abre su propio `await using var db` o
llama a un servicio con costura. Sin librería de estado global, sin store cliente. El
`AuthenticationState` lo provee `AdminAuthStateProvider` (revalidación cada 30 min recargando el
`AdminUser` y comprobando `IsActive`).

### Loading, empty, and error states

| Superficie | Loading | Empty | Error |
|---|---|---|---|
| `PendingReview` [EXISTE] | `MudProgressCircular` | "Nothing waiting on review right now." | Snackbar en fallo de `SaveChanges` |
| `/licenses/new` — `MudSelect` de productos | `MudProgressCircular` mientras carga productos | opción "Nuevo producto" siempre disponible aunque no haya productos | `MudAlert` de error de validación; Snackbar si `IssueAsync` lanza |
| `/licenses/new` — resultado | botón deshabilitado mientras emite | — | si `IssueAsync` falla, `MudAlert` con el mensaje y el formulario intacto |
| `/admin/users` — listado | `MudProgressCircular` | "No hay administradores." (no debería ocurrir tras el seed) | `MudAlert` "Ese email ya existe" en alta duplicada; Snackbar en fallo |

---

## 7. Design System

NOT APPLICABLE — reuses the existing MudBlazor default theme from `Shared/MainLayout.razor`
(`<MudThemeProvider/>`, sin tokens propios). Las pantallas nuevas componen componentes Mud
existentes (`MudTable`, `MudPaper`, `MudButton`, `MudSelect`, `MudTextField`, `MudForm`,
`MudNumericField`, `MudDatePicker`, `MudAlert`) al estilo de las páginas actuales
`Dashboard`/`Licenses`/`PendingReview`. Ningún paso del §9 depende de §7 para un contrato.

---

## 8. Authentication & Authorization

### Provider and rationale

Cookie auth local (`Microsoft.AspNetCore.Authentication.Cookies`, en el shared framework .NET 8 —
sin pin de paquete), `LoginPath = "/Account/Login"`, más una **política de autorización de fallback**
(`FallbackPolicy = RequireAuthenticatedUser()`) que exige sesión en toda ruta salvo las marcadas
`[AllowAnonymous]`. Frente a Windows/AD o un IdP externo: es un único host interno del vendor, sin
infraestructura de identidad y sin apetito por añadirla en este slice (ver §20.3 #1). Hashing con
`Microsoft.AspNetCore.Identity.PasswordHasher<AdminUser>` (PBKDF2, shared framework), envuelto en
`LicensingAdmin/Auth/PasswordHasherService.cs`.

### Flows

**Login** (`Pages/Account/Login.cshtml` + `.cshtml.cs`, `[AllowAnonymous]`):
1. GET `/Account/Login?returnUrl=...` → renderiza el formulario (email, contraseña).
2. POST → `AdminCredentialService.ValidateAsync(email, password)`:
   - busca en `admin_users` por email (case-insensitive);
   - si no hay fila → devuelve `null`;
   - si `PasswordHasherService.Verify` falla → `null`;
   - si `!IsActive` → `null` (aunque la contraseña sea correcta);
   - si todo OK → `ClaimsPrincipal` de `BuildPrincipal(adminUser)` con `ClaimTypes.Name` = email y
     `ClaimTypes.Role` = nombre del `AdminRole`.
3. Si `null` → se re-renderiza el formulario con **un** texto de error genérico (sin enumeración de
   usuarios: no se distingue "no existe" de "contraseña mala" de "inactivo").
4. Si principal → `HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal)`,
   se fija `AdminUser.LastLoginAtUtc = DateTime.UtcNow`, se escribe un `AuditLogEntry`
   (`Actor`=email, `EntityType`="AdminUser", `Action`="Login"), y redirección a `returnUrl` o `/`.

**Logout** (`Pages/Account/Logout.cshtml.cs`): `HttpContext.SignOutAsync(...)` → redirección a
`/Account/Login`.

**Expiración de sesión**: cookie con `SlidingExpiration = true`. `AdminAuthStateProvider`
(`RevalidatingServerAuthenticationStateProvider`, intervalo 30 min) recarga el `AdminUser` y, si
`IsActive` pasó a `false`, invalida el estado de autenticación del circuito.

**Primer administrador**: seed de arranque (`AdminSeeder`, §4 Seed data) + pantalla `/admin/users`
para altas posteriores.

**Baja de administrador**: `/admin/users` alterna `IsActive` (no borra la fila) y escribe un
`AuditLogEntry` `Action`="Updated". Un usuario inactivo no puede iniciar sesión y pierde el circuito
en la siguiente revalidación.

### Route protection

| Superficie | Regla | Aplicada en |
|---|---|---|
| Cualquier ruta sin `[AllowAnonymous]` | autenticado (`FallbackPolicy`) | `AddAuthorization(o => o.FallbackPolicy = ...)` en `Program.cs` (paso 8) |
| `/` , `/licenses` | autenticado con política `ViewerAccess` | `@attribute [Authorize(Policy = AuthPolicies.ViewerAccess)]` en `Dashboard.razor`, `Licenses.razor` |
| `/pending-review` | rol ∈ {SupportStaff, SuperAdmin} (`ReviewAccess`) | `@attribute [Authorize(Policy = AuthPolicies.ReviewAccess)]` en `PendingReview.razor` |
| `/licenses/new` | rol ∈ {SupportStaff, SuperAdmin} (`IssueAccess`) | `@attribute [Authorize(Policy = AuthPolicies.IssueAccess)]` en `New.razor` |
| `/admin/users` | rol == SuperAdmin (`AdminUserAccess`) | `@attribute [Authorize(Policy = AuthPolicies.AdminUserAccess)]` en `Users.razor` |
| `/Account/Login` | anónima | `[AllowAnonymous]` en `Login.cshtml.cs` |
| Cualquier ruta no reconocida y no autorizada | redirección a `/Account/Login` | `AuthorizeRouteView` + `RedirectToLogin` en `App.razor` |

**Enforcement rule:** la autorización se comprueba server-side en cada request vía la `FallbackPolicy`
y las políticas de página. Los `<AuthorizeView>` de `MainLayout` solo ocultan enlaces; nunca son la
única barrera. Un botón oculto no es un permiso. `AuthorizationPipelineTests` (paso 10) prueba que
una petición anónima a `/` y `/pending-review` recibe un 302 a `/Account/Login`.

### Roles and permissions

| Rol | Puede | No puede |
|---|---|---|
| `ReadOnlyViewer` | ver `/` y `/licenses` | aprobar/rechazar, emitir licencias, gestionar admins |
| `SupportStaff` | lo anterior + aprobar/rechazar en `/pending-review` + emitir en `/licenses/new` | gestionar administradores (`/admin/users`) |
| `SuperAdmin` | todo lo anterior + `/admin/users` (alta, cambio de rol, activar/desactivar) | — |

Mapeo política → roles: `ViewerAccess` = cualquier autenticado; `ReviewAccess` = `IssueAccess` =
{SupportStaff, SuperAdmin}; `AdminUserAccess` = {SuperAdmin}; `FallbackPolicy` = cualquier
autenticado. Constantes de nombre y registro en `LicensingAdmin/Auth/AuthPolicies.cs` (fuente única
de estos nombres).

### Sessions

Cookie de autenticación del esquema `CookieAuthenticationDefaults.AuthenticationScheme`. Flags:
`Cookie.HttpOnly = true`, `Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest` (host único, puede
servir por http en la LAN — se acepta explícitamente), `SlidingExpiration = true`. `LoginPath` y
`AccessDeniedPath` = `/Account/Login`. CSRF: los formularios Razor Pages llevan el antiforgery token
por defecto de ASP.NET Core; los componentes Blazor Server van sobre el circuito SignalR, no sobre
POST de formulario.

### D1 — Autenticación admin (detalle de implementación)

- `PasswordHasherService.cs`: `Hash(string) -> string`, `Verify(string hash, string password) ->
  bool` devolviendo `false` en `PasswordVerificationResult.Failed` y sin lanzar ante un hash basura.
- `AuthPolicies.cs`: constantes `ViewerAccess`, `ReviewAccess`, `IssueAccess`, `AdminUserAccess` +
  `AddAdminAuthorization(this IServiceCollection)` que registra las 4 políticas **y**
  `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`.
- `Program.cs`: `AddAuthentication(Cookie...).AddCookie(...)` + `AddAdminAuthorization()` +
  `app.UseAuthentication(); app.UseAuthorization();` **entre** `app.UseRouting()` y
  `app.MapBlazorHub()`; más `public partial class Program { }` al final (paso 10) para
  `WebApplicationFactory<Program>`.
- `AdminAuthStateProvider.cs`: `RevalidatingServerAuthenticationStateProvider`, `RevalidationInterval
  = TimeSpan.FromMinutes(30)`, revalida recargando el `AdminUser` y comprobando `IsActive`.
- `AdminCredentialService.cs`: costura `IAdminUserLookup` (`Task<AdminUser?> FindByEmailAsync(string)`),
  `Task<ClaimsPrincipal?> ValidateAsync(string, string)`, y `public static ClaimsPrincipal
  BuildPrincipal(AdminUser)` puro.
- `App.razor`: `<CascadingAuthenticationState>` + `<AuthorizeRouteView>` con `<NotAuthorized>` →
  `RedirectToLogin` (navega a `/Account/Login`).
- `_Imports.razor`: `@using Microsoft.AspNetCore.Authorization`,
  `@using Microsoft.AspNetCore.Components.Authorization`.
- Cablear políticas en cada página existente: `Dashboard` + `Licenses` = `ViewerAccess`,
  `PendingReview` = `ReviewAccess` (paso 12). `MainLayout` muestra el email + "Cerrar sesión" y
  oculta los enlaces de nav que el rol no puede usar (`<AuthorizeView Policy="...">`, paso 14).

### D2 — Firmador del registro de licencia (`LicensingCore`, compartido)

- `LicensingCore/Crypto/ILicenseSigner.cs` + `LicenseSigner.cs`. Firma el **registro `License`**
  (`License.Signature`), artefacto **distinto** del archivo de licencia por activación que ya produce
  `LicensingApi/Services/LicenseFileService.cs` — **no se toca `LicenseFileService`**.
- Payload canónico = bytes UTF-8 de la cadena exacta
  `{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{SubscriptionExpiryUtc:O-or-empty-string}`.
  Firma RSA-SHA256, `RSASignaturePadding.Pkcs1`.
- Interfaz: `byte[] Sign(License license)` y `bool Verify(License license, byte[] signature, RSA
  publicKey)`. El constructor recibe un `RSA` (clave privada para firmar). Se expone además
  `public static byte[] CanonicalBytes(License license)` para que los tests aserten el payload exacto.
- `LicensingAdmin` carga `Crypto:RsaPrivateKeyPem` de configuración (user-secrets / env
  `Crypto__RsaPrivateKeyPem`) y registra `ILicenseSigner` en DI, replicando el patrón de fallback
  en memoria de `LicensingApi/Program.cs` (el fallback loguea un warning; solo dev; cada reinicio
  invalida las firmas emitidas previamente).

### D3 — Primer admin por seed + pantalla de administradores

- `LicensingAdmin/Startup/AdminSeeder.cs`: `IHostedService`. Al arrancar, si `admin_users` está
  vacía y ambas `Admin:BootstrapEmail` y `Admin:BootstrapPassword` están configuradas, inserta UN
  `SuperAdmin` (contraseña hasheada) + un `AuditLogEntry` (`Actor`="system", `Action`="Created",
  `EntityType`="AdminUser"). No-op si la tabla no está vacía o faltan las claves. Lógica de decisión
  pura y testeable: `static bool ShouldSeed(bool tableEmpty, string? email, string? password)` y
  `static AdminUser BuildSuperAdmin(string email, string password, PasswordHasherService hasher)`.
- `LicensingAdmin/Pages/Admin/Users.razor` (`/admin/users`, `[Authorize(Policy = AdminUserAccess)]`):
  lista `admin_users`, crea (email + `AdminRole` + contraseña temporal → hasheada), alterna
  `IsActive`. Respaldada por `LicensingAdmin/Auth/AdminUserService.cs` (crear → rechaza email
  duplicado case-insensitive, escribe `AuditLogEntry`; desactivar → escribe `AuditLogEntry`).
  Lógica de duplicado + construcción tras una costura (`IAdminUserStore`) para tests con fake store
  (sin `DbContext`).

### Multi-tenancy / row-level isolation

NOT APPLICABLE — panel interno de un único vendor, sin fronteras de tenant. Todas las filas son
visibles para cualquier administrador autenticado según su rol.

---

## 9. BUILD ORDER

16 pasos, 2 epics de 8. `01-fundamentos` (pasos 1–8): cripto, secretos y backbone de auth.
`02-auth-y-licencias` (pasos 9–16): auth aplicada y emisión de licencias.

### Reglas de este §9 (particularizadas para .NET)

1. **No hay `dotnet lint` ni `dotnet typecheck` en este repo.** `dotnet build LicensingSystem.sln`
   con los analizadores del SDK **es** la puerta de lint/typecheck. El paso 16 usa `-warnaserror`.
2. **Los tests son unitarios puros o de pipeline HTTP sin BD** — sin Docker, sin Postgres. El único
   test que arranca un web host es `AuthorizationPipelineTests` (creado en el paso 10), con
   `WebApplicationFactory<Program>` y una cadena de conexión ficticia. Por eso el guardián de cadena
   de conexión del paso 2 y la validación de arranque **no rompen** ninguna puerta anterior: ninguna
   petición de ese test toca la BD (una GET anónima recibe 302 antes). Cualquier comprobación que
   necesite Postgres real es una anotación `# manual:`, máximo **una por epic** (está en el paso 14).
3. **`Verify` es shell literal, sale 0 cuando el paso es correcto.** Las comprobaciones `grep`/`git
   grep` que esperan "sin coincidencia" se escriben `... ; test $? -eq 1` — el `1` distingue "no hay
   coincidencia" (propiedad) de `2` / `128` (error de uso / archivo inexistente). Para afirmar que un
   archivo **contiene** un patrón se usa `grep -q <patrón> <archivo>` (sale 0 = presente, 1 = patrón
   ausente, 2 = archivo inexistente — los dos últimos fallan la puerta). El `grep -L` + `test -z`
   solo se usa sobre archivos que ya existen en el repo (paso 12: `Dashboard`/`Licenses`/`PendingReview`).
4. **`Checkpoint` es un bloque shell literal:** `git add -A && git commit -m "step NN: <slug>"`
   seguido de `git tag step-NN-<slug>`. El repositorio ya existe (baseline brownfield con un commit);
   `HEAD` ya sirve de ancla para los tags. Ver §10 Bootstrap.
5. **Un `Verify` no depende de lo que produce su propio `Checkpoint`.** El conteo de 16 tags se
   asserta en el bloque `Checkpoint` del paso 16, después de su propio `git tag`, y también en la
   puerta manual de §20.1.
6. **Cada paso `dotnet test --filter <Nombre>` nombra en su Do la clase de test** y métodos cuyo
   nombre contiene `<Nombre>`, de modo que el filtro selecciona ≥1 test. El `--filter` de VSTest es
   **substring sobre el FQN** (namespace + clase + método), no nombre exacto, así que los tokens de
   filtro se eligen sin prefijo colisionante entre tareas (p. ej. `CryptoRegistration` para el paso
   6, no `LicenseSignerDi`, para no seleccionar también `LicenseSignerTests` del paso 5). Backstop
   contra el falso-verde de un `--filter` con typo: `LicensingSystem.Tests/.runsettings` con
   `<RunConfiguration><TreatNoTestsAsError>true</TreatNoTestsAsError></RunConfiguration>`, referenciado
   por `<RunSettingsFilePath>` en el `.csproj` (paso 1). **La propiedad MSBuild
   `<VSTestTreatNoTestsAsError>` no existe — es inerte;** el mecanismo real es el `.runsettings` o
   `dotnet test -- RunConfiguration.TreatNoTestsAsError=true`. Además los pasos 14 y 16 corren
   `dotnet test` sin filtro sobre toda la suite.
7. **Ningún paso introduce una regla que retroactivamente rompa la puerta de un paso anterior.** La
   `FallbackPolicy` del paso 8 y el guardián del paso 2 nunca se ejecutan bajo `dotnet build`; solo
   bajo `WebApplicationFactory` (paso 10 en adelante), que provee su propia configuración. **El
   `AdminSeeder` (`IHostedService`, paso 11) sí se ejecuta al arrancar ese `WebApplicationFactory` y
   tocaría la BD; por eso el paso 11 —en el mismo commit que registra `AddHostedService<AdminSeeder>()`—
   añade su eliminación en `ConfigureTestServices` de `AuthorizationPipelineTests` y re-corre
   `dotnet test --filter AuthorizationPipeline`.** Cualquier futuro `IHostedService` que toque un
   recurso externo al arrancar debe neutralizarse en `AuthorizationPipelineTests` en el mismo paso.

### Step map

| # | Paso | Depende de | Toca | Puerta |
|---|---|---|---|---|
| 1 | Scaffold `LicensingSystem.Tests` + wiring en la solución | — | `LicensingSystem.Tests.csproj`, `SmokeTests.cs`, `LicensingSystem.sln`, ambos `.csproj` web | `dotnet build LicensingSystem.sln` && `dotnet test` |
| 2 | T0a — guardián de cadena de conexión | 1 | `ConnectionStringGuard.cs` (+test), ambos `Program.cs` | `dotnet build` + `dotnet test --filter ConnectionStringGuard` |
| 3 | T0b — sacar la credencial en texto plano del repo | 2 | ambos `appsettings.json`, `LicensingSystem_README.md`, `LicensingApi/README.md` | `grep` + `git grep` + `dotnet build` |
| 4 | Generador de clave de licencia (Core) | 1 | `LicenseKeyGenerator.cs` (+test) | `dotnet test --filter LicenseKeyGenerator` |
| 5 | Firmador de licencia (Core) | 4 | `ILicenseSigner.cs`, `LicenseSigner.cs` (+test) | `dotnet test --filter LicenseSigner` |
| 6 | Cablear firmador + config cripto en `LicensingAdmin` | 5 | `Program.cs`, `CryptoRegistration.cs` (+test) | `dotnet build` + `dotnet test --filter LicenseSignerDi` |
| 7 | Servicio de hashing de contraseñas | 1 | `PasswordHasherService.cs` (+test) | `dotnet test --filter PasswordHasher` |
| 8 | Cookie auth + políticas + FallbackPolicy + shell de auth | 6, 7 | `AuthPolicies.cs` (+test), `Program.cs`, `App.razor`, `_Imports.razor` | `dotnet build` + `dotnet test --filter AuthPolicies` + `awk` de orden de middleware |
| 9 | Auth-state provider + servicio de credenciales | 8 | `AdminAuthStateProvider.cs`, `AdminCredentialService.cs`, `Program.cs` (+test) | `dotnet build` + `dotnet test --filter AdminCredential` |
| 10 | Páginas Razor de login/logout + test de pipeline de autorización | 9 | `Login.cshtml(.cs)`, `Logout.cshtml.cs`, `Program.cs`, `AuthorizationPipelineTests.cs` | `dotnet build` + `dotnet test --filter AuthorizationPipeline` + `grep [AllowAnonymous]` |
| 11 | Seeder del primer `SuperAdmin` | 10 | `AdminSeeder.cs` (+test), `Program.cs`, `AuthorizationPipelineTests.cs` (quita el seeder) | `dotnet test --filter AdminSeeder` + `dotnet test --filter AuthorizationPipeline` |
| 12 | Proteger pantallas existentes + revisor real | 8, 9 | `Dashboard.razor`, `Licenses.razor`, `PendingReview.razor`, `CurrentAdmin.cs` (+test) | `dotnet build` + `grep support-staff` + `grep -L [Authorize]` + `dotnet test --filter CurrentAdmin` |
| 13 | Servicio de emisión de licencias | 4, 5 | `LicenseIssuanceService.cs`, `ILicenseStore.cs`, `EfLicenseStore.cs`, `LicenseIssuanceRequest.cs` (+test) | `dotnet test --filter LicenseIssuance` |
| 14 | Pantalla "Generar nueva licencia" + shell de `MainLayout` | 12, 13 | `Pages/Licenses/New.razor`, `Shared/MainLayout.razor` | `dotnet build` + `grep -q [Authorize]` + `dotnet test` + `# manual:` |
| 15 | Pantalla de administradores | 7, 11, 12 | `Pages/Admin/Users.razor`, `AdminUserService.cs`, `IAdminUserStore.cs` (+test) | `dotnet build` + `grep -q [Authorize]` + `dotnet test --filter AdminUserService` |
| 16 | Smoke de solución + regresión + auditoría de checkpoints | 14, 15 | `SmokeTests.cs` | `dotnet build -warnaserror` + `dotnet test` + `grep` + `git grep` |

Orden: primero el andamiaje de tests y el cierre de la brecha de secreto (aditivo, no rompe nada),
luego las piezas de `LicensingCore` que ambas apps necesitan (generador, firmador), luego el
backbone de auth y su política de fallback, luego auth aplicada y la emisión. El paso 16 no añade
código de producto: corre la puerta completa y cuenta los tags.

---

### Step 1 — Scaffold `LicensingSystem.Tests` y wiring en la solución

**Do**
Tarea de andamiaje: 7 archivos de proyecto/config, **cero lógica** — el tope de "≤5 archivos" no
aplica (aprobado por el Arquitecto en la enmienda de 2026-09-02). Crear:
- `LicensingSystem.Tests/LicensingSystem.Tests.csproj` — `Microsoft.NET.Sdk`, `net8.0`,
  `<Nullable>enable</Nullable>`, `<IsPackable>false</IsPackable>`, `<OutputType>Exe</OutputType>`
  (xUnit v3 corre como ejecutable),
  `<RunSettingsFilePath>$(MSBuildThisFileDirectory).runsettings</RunSettingsFilePath>`,
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (los tests usan `PasswordHasher<T>`,
  autorización, `ClaimsPrincipal` y `WebApplicationFactory<Program>` directamente). Los 5
  `PackageReference` de §11. `ProjectReference` a `..\LicensingCore\LicensingCore.csproj` y
  `..\LicensingAdmin\LicensingAdmin.csproj`. **No** uses `<VSTestTreatNoTestsAsError>` — esa
  propiedad MSBuild no existe (inerte); ver el `.runsettings`.
- `LicensingSystem.Tests/.runsettings` — `<RunSettings><RunConfiguration><TreatNoTestsAsError>true</TreatNoTestsAsError></RunConfiguration></RunSettings>`.
  Sin esto, `dotnet test --filter <clase-inexistente>` sale **exit 0 con 0 tests** (falso verde), y
  todas las puertas `--filter` de los pasos 2–16 serían inseguras ante un typo o un test no cableado.
- `global.json` en la **raíz** del repo — `{ "sdk": { "version": "8.0.400", "rollForward": "latestFeature" } }`
  (ajusta el patch a un `8.0.4xx` presente en el nodo `dev`; `latestFeature` permite bumps de patch).
  El nodo `dev` tiene SDK 8.0.4xx **y** 10.0.4xx; sin `global.json`, `dotnet` elegiría el 10.x y
  compilaría `net8.0` con analizadores de otra major → riesgo en `dotnet build -warnaserror` del
  paso 16. Ver §20.3 #8.
- `LicensingSystem.Tests/SmokeTests.cs` — una sola prueba `[Fact] public void Smoke() =>
  Assert.True(true);`.
- `dotnet sln LicensingSystem.sln add LicensingSystem.Tests/LicensingSystem.Tests.csproj`.
- Añadir un `<UserSecretsId>` no vacío (un GUID) a `LicensingApi/LicensingApi.csproj` y
  `LicensingAdmin/LicensingAdmin.csproj` en un `<PropertyGroup>`.

Versiones: exclusivamente las de §11; no se fija ninguna en este texto.

**Done when**
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0 with `LicensingSystem.Tests` present as a fourth project in the solution.
- [ ] WHEN `dotnet test` runs from the repo root THE SYSTEM SHALL report 1 passed, 0 failed, 0 skipped.
- [ ] WHEN `LicensingApi.csproj` and `LicensingAdmin.csproj` are inspected THE SYSTEM SHALL each contain a non-empty `<UserSecretsId>` element.
- [ ] WHEN `LicensingSystem.Tests.csproj` is inspected THE SYSTEM SHALL set `<IsPackable>false</IsPackable>`, reference exactly the five pinned test packages (Microsoft.NET.Test.Sdk, xunit.v3, xunit.runner.visualstudio, coverlet.collector, Microsoft.AspNetCore.Mvc.Testing) plus project references to `LicensingCore` and `LicensingAdmin`, and point `<RunSettingsFilePath>` at a committed `LicensingSystem.Tests/.runsettings`.
- [ ] WHEN `LicensingSystem.Tests/.runsettings` is inspected THE SYSTEM SHALL set `<TreatNoTestsAsError>true</TreatNoTestsAsError>`.
- [ ] WHEN `dotnet --version` runs at the repo root THE SYSTEM SHALL report an `8.0.4xx` SDK selected by a committed `global.json` with `rollForward: latestFeature`.

**Verify**
```bash
dotnet build LicensingSystem.sln        # expect: exit 0
dotnet test                             # expect: exit 0 — 1 passed, 0 failed, 0 skipped
grep -q 'TreatNoTestsAsError' LicensingSystem.Tests/.runsettings && grep -q 'RunSettingsFilePath' LicensingSystem.Tests/LicensingSystem.Tests.csproj
# expect: exit 0 — the no-tests-is-error backstop is wired
dotnet --version | grep -q '^8[.]0[.]'  # expect: exit 0 — global.json pins the 8.0.x SDK
```

**Checkpoint**
```bash
git add -A && git commit -m "step 1: tests-scaffold"
git tag step-01-tests-scaffold
```

---

### Step 2 — T0a: guardián de cadena de conexión

**Do**
- `LicensingCore/Configuration/ConnectionStringGuard.cs` — `public static class ConnectionStringGuard`
  con `public static string Require(string? value)`: lanza
  `new InvalidOperationException("ConnectionStrings:LicensingDb is not configured. Set it with 'dotnet user-secrets set' or the ConnectionStrings__LicensingDb environment variable.")`
  cuando `value` es `null`, vacío o solo espacios; en otro caso devuelve `value` sin cambios.
- `LicensingApi/Program.cs` y `LicensingAdmin/Program.cs` — envolver la lectura de la cadena:
  `ConnectionStringGuard.Require(builder.Configuration.GetConnectionString("LicensingDb"))` antes de
  construir las opciones del `DbContext`.
- Esta tarea **no** toca `appsettings.json` ni los README — eso es el paso 3.

**Done when**
- [ ] WHEN `ConnectionStringGuard.Require(null)`, `Require("")`, or `Require("   ")` is called THE SYSTEM SHALL throw `InvalidOperationException` whose message names both `dotnet user-secrets` and the `ConnectionStrings__LicensingDb` environment variable.
- [ ] WHEN `ConnectionStringGuard.Require("Host=db;Database=x")` is called with a non-blank value THE SYSTEM SHALL return that exact string unchanged.
- [ ] WHEN `dotnet test --filter ConnectionStringGuard` runs THE SYSTEM SHALL report all `ConnectionStringGuard` tests passed, 0 failed.
- [ ] WHEN either `LicensingApi/Program.cs` or `LicensingAdmin/Program.cs` reads the `LicensingDb` connection string THE SYSTEM SHALL pass it through `ConnectionStringGuard.Require(...)` before constructing the `DbContext` options.
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
dotnet test --filter ConnectionStringGuard    # expect: exit 0 — all ConnectionStringGuard tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 2: conn-guard"
git tag step-02-conn-guard
```

---

### Step 3 — T0b: sacar la credencial en texto plano del repo

**Do**
- `LicensingApi/appsettings.json` y `LicensingAdmin/appsettings.json` — poner
  `"ConnectionStrings": { "LicensingDb": "" }` (cadena vacía; se retira `Host=...;Password=***`).
- `LicensingSystem_README.md` y `LicensingApi/README.md` — reescribir **toda** aparición de la
  cadena de conexión con contraseña real (el bloque de ejemplo y el comando `dotnet user-secrets
  set`) a un placeholder
  `Host=<host>;Port=5432;Database=licensing_app;Username=<user>;Password=<password>`, y quitar
  cualquier frase tipo "está bien para local". No debe quedar `Password=***REDACTED***` en ningún archivo
  rastreado fuera de `docs/blueprint/` **y de `tasks.json`** (que contiene el literal en el texto de
  sus propios criterios/comandos — auto-referencia inofensiva; el pathspec la excluye).

**Done when**
- [ ] WHEN `LicensingApi/appsettings.json` and `LicensingAdmin/appsettings.json` are inspected THE SYSTEM SHALL each set `ConnectionStrings:LicensingDb` to the empty string.
- [ ] WHEN `grep -RIl "Password=" LicensingApi/appsettings.json LicensingAdmin/appsettings.json` runs THE SYSTEM SHALL find no match and exit 1.
- [ ] WHEN `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` runs THE SYSTEM SHALL find no match and exit 1.
- [ ] WHEN `LicensingSystem_README.md` and `LicensingApi/README.md` are inspected THE SYSTEM SHALL show the connection string only as a placeholder with no real password value.
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0.

**Verify**
```bash
grep -RIl "Password=" LicensingApi/appsettings.json LicensingAdmin/appsettings.json; test $? -eq 1
# expect: exit 0 — grep exits 1 (no plaintext credential in either file); exit 2 (missing file) would fail the gate
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1
# expect: exit 0 — git grep exits 1. Excludes the bundle (docs/blueprint/) and tasks.json, whose own
#         criterion/verify text quotes the literal — the pathspec is what keeps the gate from self-matching.
dotnet build LicensingSystem.sln             # expect: exit 0
```

**Checkpoint**
```bash
git add -A && git commit -m "step 3: secret-scrub"
git tag step-03-secret-scrub
```

---

### Step 4 — Generador de clave de licencia (`LicensingCore`)

**Do**
- `LicensingCore/Licensing/LicenseKeyGenerator.cs` — `public static class LicenseKeyGenerator` con
  `public static string NewKey()`: caracteres cripto-aleatorios de
  `System.Security.Cryptography.RandomNumberGenerator` sobre el alfabeto
  `ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789`, segmentos de longitud 4-5-4-4-4-4-2 unidos por `-`.
  Debe cumplir la regex de la fila 6 de §5.
- `LicensingSystem.Tests/LicenseKeyGeneratorTests.cs` — clase `LicenseKeyGeneratorTests`: genera
  10000 claves; asserta que cada una casa con
  `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$` (copiada
  literal) y que las 10000 son distintas; una prueba de charset que asserta que ningún carácter cae
  fuera de `A–Z0–9`.

**Done when**
- [ ] WHEN `LicenseKeyGenerator.NewKey()` is called THE SYSTEM SHALL return a string matching `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$`.
- [ ] WHEN 10000 keys are generated in one test run THE SYSTEM SHALL produce 10000 distinct values with 0 collisions.
- [ ] WHEN any generated key is inspected THE SYSTEM SHALL contain only characters from `A`–`Z` and `0`–`9` plus the segment separator `-`.
- [ ] WHEN `dotnet test --filter LicenseKeyGenerator` runs THE SYSTEM SHALL report all `LicenseKeyGenerator` tests passed, 0 failed.

**Verify**
```bash
dotnet test --filter LicenseKeyGenerator      # expect: exit 0 — all LicenseKeyGenerator tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 4: license-key-generator"
git tag step-04-license-key-generator
```

---

### Step 5 — Firmador de licencia (`LicensingCore`)

**Do**
- `LicensingCore/Crypto/ILicenseSigner.cs` — `byte[] Sign(License license)` y
  `bool Verify(License license, byte[] signature, System.Security.Cryptography.RSA publicKey)`.
- `LicensingCore/Crypto/LicenseSigner.cs` — implementa `ILicenseSigner` según §8 D2: constructor
  `LicenseSigner(RSA signingKey)`; firma `RSA-SHA256` con `RSASignaturePadding.Pkcs1`. Expone
  `public static byte[] CanonicalBytes(License license)`. **Payload canónico** = bytes UTF-8 (sin
  BOM) de `licsig-v1|{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{expiry}` con
  `FormattableString.Invariant`, donde:
  - prefijo fijo `licsig-v1|` — separación de dominio: la misma clave RSA no debe poder confundir una
    firma de `LicenseSigner` con una de `LicenseFileService` si algún formato evoluciona (§8 D2, §20.3 #9).
  - `{expiry}` = cadena vacía si `SubscriptionExpiryUtc` es `null`; en otro caso **normalizado a UTC**
    (`Kind == Local` → `ToUniversalTime()`; `Kind == Unspecified` → `DateTime.SpecifyKind(v, Utc)`),
    **truncado a segundos enteros**, formateado `yyyy-MM-ddTHH:mm:ss'Z'` con `CultureInfo.InvariantCulture`.
    Motivo: `DateTime.ToString("O")` varía por `Kind` (offset según la zona de la máquina) y emite 7
    dígitos fraccionarios que Postgres `timestamptz` trunca a 6 → la firma dejaba de verificar
    cross-máquina y tras round-trip por BD. La expiración de una licencia no necesita sub-segundo.
- `Verify` **blinda entradas** (§8 D2 M-1): `ArgumentNullException.ThrowIfNull` para `license` y
  `publicKey`; `signature is null or { Length: 0 }` → `false`; `catch (CryptographicException)` → `false`.
- `LicensingSystem.Tests/LicenseSignerTests.cs` — clase `LicenseSignerTests`: round-trip
  sign→verify true; tampering de cada campo canónico → verify false; clave pública distinta / firma
  null / vacía / malformada → verify false sin lanzar; `license`/`publicKey` null → `ArgumentNullException`;
  `CanonicalBytes` byte-exacto con y sin expiración; y **el mismo instante firmado como `Unspecified`,
  `Utc`, `Local` y tras truncar sub-segundo → misma firma y `Verify` true en los cuatro casos**.

**Done when**
- [ ] WHEN `LicenseSigner.Sign(license)` output is passed to `Verify(license, signature, publicKey)` with the matching key THE SYSTEM SHALL return true.
- [ ] WHEN any one canonical field (`LicenseKey`, `ProductId`, `ModelSnapshot`, `MaxActivations`, `SubscriptionExpiryUtc`) is changed and `Verify` is re-run with the original signature THE SYSTEM SHALL return false.
- [ ] WHEN `Verify` is called with a different RSA public key, a null `signature`, a zero-length `signature`, or a malformed `signature` THE SYSTEM SHALL return false without throwing; WHEN `license` or `publicKey` is null THE SYSTEM SHALL throw `ArgumentNullException`.
- [ ] WHEN `LicenseSigner.CanonicalBytes(license)` is decoded as UTF-8 THE SYSTEM SHALL equal `licsig-v1|{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{expiry}`, `{expiry}` empty when null else the value normalized to UTC, truncated to whole seconds, formatted `yyyy-MM-ddTHH:mm:ss'Z'` invariant.
- [ ] WHEN the same instant is signed as `DateTimeKind.Unspecified`, as `Utc`, as `Local`, and after a round-trip that drops sub-second precision THE SYSTEM SHALL produce the same signature and `Verify` SHALL return true in every case.
- [ ] WHEN `dotnet test --filter LicenseSigner` runs THE SYSTEM SHALL report all `LicenseSigner` tests passed, 0 failed.

**Verify**
```bash
dotnet test --filter LicenseSigner            # expect: exit 0 — all LicenseSigner tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 5: license-signer"
git tag step-05-license-signer
```

---

### Step 6 — Cablear firmador + config cripto en `LicensingAdmin`

**Do**
- `LicensingAdmin/Auth/CryptoRegistration.cs` — `public static class CryptoRegistration` con
  `public static IServiceCollection AddLicenseSigner(this IServiceCollection services, IConfiguration
  config, IHostEnvironment env)`. **Valida la clave de forma EAGER** (en la llamada, antes de
  `builder.Build()` — no en el factory perezoso):
  - `config["Crypto:RsaPrivateKeyPem"]` no vacío → `RSA.Create()` + `ImportFromPem(...)`; luego
    comprueba que **tiene material privado** (`rsa.ExportParameters(true)` no lanza) y
    `rsa.KeySize >= 2048`. Cualquier fallo (PEM malformado, solo-público, clave corta) →
    `throw new InvalidOperationException` cuyo mensaje **nombra `Crypto:RsaPrivateKeyPem`** y **nunca
    vuelca el valor**.
  - vacío/ausente **y** `env.IsDevelopment()` → `RSA.Create(2048)` fallback en memoria + **un**
    `LogWarning` (dev-only; cada reinicio invalida las firmas previas).
  - vacío/ausente **y** NO `IsDevelopment()` → `throw new InvalidOperationException("Crypto:RsaPrivateKeyPem
    must be configured outside Development (dotnet user-secrets / environment variable).")`. Fail-closed,
    igual que `ConnectionStringGuard` (§20.3 #12).
  Registra `ILicenseSigner` singleton con la `RSA` ya validada. El warning usa categoría tipada
  (`ILogger<CryptoRegistration>` / `typeof(CryptoRegistration).FullName`), no un string literal.
- `LicensingAdmin/Program.cs` — `builder.Services.AddLicenseSigner(builder.Configuration,
  builder.Environment)` una sola vez.
- `LicensingSystem.Tests/CryptoRegistrationTests.cs` — clase `CryptoRegistrationTests` (nombres de
  método sin el prefijo `LicenseSignerDi_` — colisiona con `--filter LicenseSigner` del paso 5).
  Cubre: PEM válido → resuelve + round-trip; PEM vacío + `IHostEnvironment` Development → resuelve
  con 1 warning exacto; PEM vacío + entorno Production → `InvalidOperationException` en la llamada;
  PEM malformado / solo-público / clave < 2048 → `InvalidOperationException` en la llamada (no
  perezosa); mensaje sin material de clave. Puerta: `dotnet test --filter CryptoRegistration`.

**Done when**
- [ ] WHEN `AddLicenseSigner(services, config, env)` runs with `Crypto:RsaPrivateKeyPem` set to a valid RSA private-key PEM THE SYSTEM SHALL register `ILicenseSigner` such that `GetRequiredService<ILicenseSigner>()` resolves without throwing.
- [ ] WHEN `Crypto:RsaPrivateKeyPem` is blank AND `env.IsDevelopment()` THE SYSTEM SHALL resolve `ILicenseSigner` with an in-memory RSA key and log exactly one warning; WHEN blank AND NOT `IsDevelopment()` THE SYSTEM SHALL throw `InvalidOperationException` at registration time whose message names `Crypto:RsaPrivateKeyPem` and never contains key material.
- [ ] WHEN `Crypto:RsaPrivateKeyPem` is set but malformed, a public-key-only PEM, or an RSA key shorter than 2048 bits THE SYSTEM SHALL throw `InvalidOperationException` at registration time naming `Crypto:RsaPrivateKeyPem`, not fail lazily on the first `Sign`.
- [ ] WHEN `LicensingAdmin/Program.cs` is inspected THE SYSTEM SHALL call `AddLicenseSigner(builder.Configuration, builder.Environment)` exactly once, and the fallback warning SHALL use a typed logger category.
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0 and `dotnet test --filter CryptoRegistration` SHALL report all `CryptoRegistrationTests` passed, 0 failed (the filter selects no `LicenseSignerTests`).

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
dotnet test --filter CryptoRegistration       # expect: exit 0 — all CryptoRegistrationTests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 6: signer-di"
git tag step-06-signer-di
```

---

### Step 7 — Servicio de hashing de contraseñas

**Do**
- `LicensingAdmin/Auth/PasswordHasherService.cs` según §8 D1: `public sealed class
  PasswordHasherService` con **constructor que recibe un `PasswordHasher<AdminUser>` inyectado**
  (alimentado por `IOptions<PasswordHasherOptions>` de DI) — **no** `_inner = new()` en field-init,
  para que el paso 8 pueda subir el `IterationCount` (§8 D1 M-1). `string Hash(string password)`;
  `bool Verify(string hash, string password)` **empieza con** `if (string.IsNullOrEmpty(hash) ||
  password is null) return false;`, devuelve `false` en `PasswordVerificationResult.Failed`,
  `catch (FormatException) => false` para hash no decodable — **nunca lanza** (§8 D1 M-2: un campo
  de formulario Blazor puede llegar `null`).
- `LicensingSystem.Tests/PasswordHasherServiceTests.cs` — clase `PasswordHasherServiceTests`:
  hash→verify true; contraseña equivocada → false; salt por hash; `Verify("garbage", "x")` → false;
  **`Verify(null, "x")`, `Verify("", "x")` y `Verify(hash, null)` → false sin excepción**; el ctor
  acepta un `PasswordHasher<AdminUser>` construido con `Options.Create(new PasswordHasherOptions{
  IterationCount = 210_000 })` y sigue funcionando.

**Done when**
- [ ] WHEN `Hash(p)` output is passed to `Verify(hash, p)` THE SYSTEM SHALL return true.
- [ ] WHEN `Verify(hash, wrongPassword)` is called THE SYSTEM SHALL return false.
- [ ] WHEN the same password is hashed twice THE SYSTEM SHALL produce two different hash strings.
- [ ] WHEN `Verify` is called with a malformed, non-matching, `null`, or empty `hash`, or with a `null` `password` THE SYSTEM SHALL return false and SHALL NOT throw.
- [ ] WHEN `PasswordHasherService` is constructed THE SYSTEM SHALL take an injected `PasswordHasher<AdminUser>` (fed by `IOptions<PasswordHasherOptions>`) rather than a fixed field initializer.
- [ ] WHEN `dotnet test --filter PasswordHasher` runs THE SYSTEM SHALL report all `PasswordHasher` tests passed, 0 failed.

**Verify**
```bash
dotnet test --filter PasswordHasher           # expect: exit 0 — all PasswordHasher tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 7: password-hasher"
git tag step-07-password-hasher
```

---

### Step 8 — Cookie auth + políticas + FallbackPolicy + shell de auth

**Do**
- `LicensingAdmin/Auth/AuthPolicies.cs` — constantes `public const string ViewerAccess = "ViewerAccess"`
  (y `ReviewAccess`, `IssueAccess`, `AdminUserAccess`) + `public static void AddAdminAuthorization(this
  IServiceCollection services)` que registra las 4 políticas: `ViewerAccess` = `RequireAuthenticatedUser()`;
  `ReviewAccess` e `IssueAccess` = `RequireRole(nameof(AdminRole.SupportStaff), nameof(AdminRole.SuperAdmin))`;
  `AdminUserAccess` = `RequireRole(nameof(AdminRole.SuperAdmin))`; **y**
  `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`.
- `LicensingAdmin/Program.cs` —
  `AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => { o.LoginPath =
  "/Account/Login"; o.AccessDeniedPath = "/Account/AccessDenied"; o.Cookie.HttpOnly = true;
  o.Cookie.SameSite = SameSiteMode.Lax; o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ?
  CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always; o.SlidingExpiration = true; })`;
  `services.AddAdminAuthorization()`; y `app.UseAuthentication(); app.UseAuthorization();` **entre**
  `app.UseRouting()` y `app.MapBlazorHub()` (orden crítico — el `awk` del Verify lo comprueba).
  En la rama `if (!app.Environment.IsDevelopment())` (junto a `UseHsts()`): `app.UseHttpsRedirection()`.
  Además `services.Configure<PasswordHasherOptions>(o => o.IterationCount = 210_000)` — OWASP 2024
  para PBKDF2-HMAC-SHA512 (el `PasswordHasher<AdminUser>` del shared framework por defecto usa
  100 000); `PasswordHasherService` (paso 7) lo recibe por DI (§8 D1 M-1, §20.3 #10).
  `AccessDeniedPath` apunta a `/Account/AccessDenied` (no `/Account/Login`) para evitar el bucle
  login↔returnUrl de un usuario autenticado con rol insuficiente (§20.3 #13); esa página la crea el
  paso 10.
- `LicensingAdmin/App.razor` — `<CascadingAuthenticationState>` + `<Router>` +
  `<AuthorizeRouteView DefaultLayout="typeof(MainLayout)" Context="ctx">` con `<NotAuthorized>` que:
  **si `ctx.User.Identity?.IsAuthenticated == true`** muestra un mensaje "no tienes permiso" dentro
  del `MainLayout` (sin redirigir — así no hay bucle); **si es anónimo**, llama `RedirectToLogin()`
  (`NavigateTo("/Account/Login?returnUrl=...", forceLoad: true)`).
- `LicensingAdmin/_Imports.razor` — añadir `@using Microsoft.AspNetCore.Authorization` y
  `@using Microsoft.AspNetCore.Components.Authorization`.
- `LicensingAdmin/Pages/Error.cshtml` — añadir `@attribute [AllowAnonymous]` en la cabecera, para
  que la `FallbackPolicy` no rompa la página de error (un fallo no manejado en una request anónima
  re-ejecuta a `/Error`, que si no sería denegada → bucle) (auditor B6).
- `LicensingSystem.Tests/AuthPoliciesTests.cs` — clase `AuthPoliciesTests`: `ServiceProvider` con
  `AddAdminAuthorization()`, `IAuthorizationService`, matriz `ClaimsPrincipal` por rol contra las 4
  políticas **y** la `FallbackPolicy`, más el principal anónimo que falla todas.

**Done when**
- [ ] WHEN `AuthPolicies` is inspected THE SYSTEM SHALL expose `ViewerAccess`, `ReviewAccess`, `IssueAccess`, `AdminUserAccess` as string constants.
- [ ] WHEN a `ClaimsPrincipal` in role `ReadOnlyViewer` is checked THE SYSTEM SHALL satisfy only `ViewerAccess`; in role `SupportStaff` THE SYSTEM SHALL satisfy `ViewerAccess`, `ReviewAccess`, `IssueAccess` and fail `AdminUserAccess`.
- [ ] WHEN a `ClaimsPrincipal` in role `SuperAdmin` is checked THE SYSTEM SHALL satisfy all four policies, and WHEN an unauthenticated `ClaimsPrincipal` is checked THE SYSTEM SHALL fail all four policies and the fallback policy.
- [ ] WHEN `LicensingAdmin/Program.cs` is inspected THE SYSTEM SHALL: place `app.UseAuthentication()` after `app.UseRouting()` and before `app.MapBlazorHub()`; set a `FallbackPolicy` requiring an authenticated user; configure `PasswordHasherOptions.IterationCount` >= 210000; set `Cookie.SecurePolicy = IsDevelopment() ? SameAsRequest : Always`; set `Cookie.SameSite = Lax`; set `AccessDeniedPath = "/Account/AccessDenied"`; and call `app.UseHttpsRedirection()` in the non-Development branch.
- [ ] WHEN an authenticated user hits `<NotAuthorized>` (insufficient role) THE SYSTEM SHALL show an "acceso denegado" message rather than redirecting to `/Account/Login`; WHEN an anonymous user hits it THE SYSTEM SHALL redirect to `/Account/Login?returnUrl=`; and `Pages/Error.cshtml` SHALL carry `@attribute [AllowAnonymous]`.
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0 and `dotnet test --filter AuthPolicies` SHALL report all tests passed, 0 failed.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
dotnet test --filter AuthPolicies             # expect: exit 0 — all AuthPolicies tests pass
awk '/app\.UseRouting\(\)/{r=NR} /app\.UseAuthentication\(\)/{a=NR} /app\.MapBlazorHub\(\)/{h=NR} END{exit !(r>0 && a>0 && h>0 && r<a && a<h)}' LicensingAdmin/Program.cs
# expect: exit 0 — UseAuthentication sits between UseRouting and MapBlazorHub
grep -q 'AllowAnonymous' LicensingAdmin/Pages/Error.cshtml   # expect: exit 0
```

**Checkpoint**
```bash
git add -A && git commit -m "step 8: cookie-auth"
git tag step-08-cookie-auth
```

---

### Step 9 — Auth-state provider + servicio de credenciales

**Do**
- `LicensingAdmin/Auth/AdminCredentialService.cs` — costura de búsqueda `IAdminUserLookup` con
  `Task<AdminUser?> FindByEmailAsync(string email)` (impl real usa `IDbContextFactory<AppDbContext>`
  + `db.AdminUsers`, búsqueda case-insensitive). `Task<ClaimsPrincipal?> ValidateAsync(string
  email, string password)`: `FindByEmailAsync` → si `null`, **ejecuta un `PasswordHasherService.Verify`
  de coste fijo contra un hash dummy** y devuelve `null` (para que la rama "usuario no existe" no se
  distinga por tiempo — auditor B-1 de E1-T7); si el `Verify` real falla → `null`; `!IsActive` →
  `null`; OK → `BuildPrincipal(user)`. `public static ClaimsPrincipal BuildPrincipal(AdminUser u)`
  puro: `ClaimTypes.Name`=`u.Email`, **`ClaimTypes.Role`** (el role claim type por defecto, para que
  `RequireRole` case) = `u.Role.ToString()`, esquema de cookie.
- `LicensingAdmin/Auth/AdminAuthStateProvider.cs` — `RevalidatingServerAuthenticationStateProvider`,
  `RevalidationInterval = TimeSpan.FromMinutes(30)`, revalida recargando el `AdminUser` y comprobando
  `IsActive` + que el rol no cambió.
- `LicensingAdmin/Program.cs` — registrar `AdminCredentialService`, `IAdminUserLookup` (impl EF), y
  `AdminAuthStateProvider` como `AuthenticationStateProvider` (`AddScoped`). Además, en el `AddCookie`
  del paso 8: `o.ExpireTimeSpan = TimeSpan.FromHours(8)` (además del `SlidingExpiration`) y
  `o.Events.OnValidatePrincipal` que recarga el `AdminUser` (existe + `IsActive` + rol sin cambio) y
  llama `context.RejectPrincipal()` si algo falla — así un admin desactivado pierde la sesión sin
  esperar a la revalidación de Blazor (§20.3 #14).
- `LicensingSystem.Tests/AdminCredentialServiceTests.cs` — clase `AdminCredentialServiceTests` con un
  fake `IAdminUserLookup`: `ValidateAsync` con email sin fila → `null`; fila con `IsActive=false` y
  contraseña correcta → `null`; fila activa + contraseña correcta → principal no nulo con claims
  Name+Role; `BuildPrincipal(user)` produce `ClaimTypes.Name`=email y `ClaimTypes.Role`=nombre del rol.

**Done when**
- [ ] WHEN `AdminCredentialService.BuildPrincipal(adminUser)` runs THE SYSTEM SHALL return a `ClaimsPrincipal` carrying `ClaimTypes.Name` = the user's email and `ClaimTypes.Role` (the default role claim type, so `RequireRole` matches) = the `AdminRole` name.
- [ ] WHEN `ValidateAsync(email, password)` is given an email that matches no `admin_users` row THE SYSTEM SHALL return null, having first run a fixed-cost dummy `PasswordHasherService.Verify` so the no-user path is not distinguishable by timing.
- [ ] WHEN `ValidateAsync` matches a row whose `IsActive` is false THE SYSTEM SHALL return null even if the password is correct; and the cookie's `OnValidatePrincipal` SHALL reject a principal whose `AdminUser` no longer exists, is inactive, or changed role, with `Program.cs` setting a bounded `ExpireTimeSpan` (8h).
- [ ] WHEN `ValidateAsync` matches an active row and the password verifies THE SYSTEM SHALL return a non-null principal built by `BuildPrincipal`.
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0.
- [ ] WHEN `dotnet test --filter AdminCredential` runs THE SYSTEM SHALL report all `AdminCredential` tests passed, 0 failed.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
dotnet test --filter AdminCredential          # expect: exit 0 — all AdminCredential tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 9: auth-state"
git tag step-09-auth-state
```

---

### Step 10 — Páginas Razor de login/logout + test de pipeline de autorización

**Do**
- `LicensingAdmin/Pages/Account/Login.cshtml` (+ `.cshtml.cs`) — `[AllowAnonymous]`. GET renderiza
  el formulario (email, contraseña, `returnUrl`). POST → `AdminCredentialService.ValidateAsync` → si
  principal: `HttpContext.SignInAsync(...)`, fija `AdminUser.LastLoginAtUtc`, escribe `AuditLogEntry`
  (`Actor`=email, `EntityType`="AdminUser", `Action`="Login"), redirige a `returnUrl` **validado con
  `Url.IsLocalUrl`** (rechaza absolutos y `//host`; cae a `/` si no es local — evita open redirect);
  si `null`: re-renderiza con **un** mensaje de error genérico (sin distinguir causa).
  **Recomendado (no bloqueante):** un lockout por email en memoria (p. ej. 5 fallos / 15 min) — el
  rate-limiting de middleware sigue siendo Non-Goal (§1), pero un lockout barato en el límite de
  auth es prudente.
- `LicensingAdmin/Pages/Account/Logout.cshtml.cs` — `HttpContext.SignOutAsync(...)` → redirige a
  `/Account/Login`.
- `LicensingAdmin/Pages/Account/AccessDenied.cshtml` — `@page "/Account/AccessDenied"`,
  `@attribute [AllowAnonymous]`. Página mínima "no tienes permiso para ver esta página" (HTTP 200
  con el mensaje; es el destino del `AccessDeniedPath` de la cookie, fijado en el paso 8). No hace
  rebote a login.
- `LicensingAdmin/Program.cs` — añadir `public partial class Program { }` al final del archivo, para
  que `LicensingSystem.Tests` pueda referenciar `WebApplicationFactory<Program>` sin
  `InternalsVisibleTo`.
- `LicensingSystem.Tests/AuthorizationPipelineTests.cs` — clase `AuthorizationPipelineTests` con una
  `WebApplicationFactory<Program>` que:
  - en `ConfigureAppConfiguration` fija
    `ConnectionStrings:LicensingDb = "Host=localhost;Port=5432;Database=test;Username=test;Password=test"`
    (sintácticamente válida, nunca se conecta: una GET anónima recibe 302 antes de tocar la BD;
    `AddDbContextFactory` + `UseNpgsql` no conectan al registrar);
  - deja `Crypto:RsaPrivateKeyPem` sin fijar (fallback RSA en memoria).
  Con `AllowAutoRedirect = false`, asserta: `GET /Account/Login` → 200; `GET /` → 302 con `Location`
  que empieza por `/Account/Login`; `GET /pending-review` → 302 con `Location` que empieza por
  `/Account/Login`. **Nota:** en este paso `Program.cs` aún no registra ningún `IHostedService`, así
  que el host arranca sin tocar la BD. El paso 11 —que añade `AddHostedService<AdminSeeder>()`—
  extiende este archivo para quitar ese seeder en `ConfigureTestServices`.

**Done when**
- [ ] WHEN `GET /Account/Login` is requested with no authentication cookie THE SYSTEM SHALL return HTTP 200; `Pages/Account/AccessDenied.cshtml` exists, carries `@attribute [AllowAnonymous]`, and returns a "sin permiso" page (the cookie `AccessDeniedPath` from step 8 points here).
- [ ] WHEN `GET /` is requested with no authentication cookie THE SYSTEM SHALL return HTTP 302 whose `Location` header starts with `/Account/Login`.
- [ ] WHEN `GET /pending-review` is requested with no authentication cookie THE SYSTEM SHALL return HTTP 302 whose `Location` header starts with `/Account/Login`.
- [ ] WHEN `LicensingAdmin/Pages/Account/Login.cshtml.cs` is inspected THE SYSTEM SHALL carry `[AllowAnonymous]`, on any null `ValidateAsync` result re-render with a single generic error message that does not distinguish the failure cause, and validate `returnUrl` with `Url.IsLocalUrl` (rejecting absolute and `//host` URLs, falling back to `/`).
- [ ] WHEN a POST to `/Account/Login` succeeds THE SYSTEM SHALL call `HttpContext.SignInAsync`, set `AdminUser.LastLoginAtUtc`, and write an `AuditLogEntry` with `Action` = "Login".
- [ ] WHEN `dotnet build LicensingSystem.sln` runs THE SYSTEM SHALL exit 0 and `dotnet test --filter AuthorizationPipeline` SHALL report all tests passed, 0 failed.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
dotnet test --filter AuthorizationPipeline    # expect: exit 0 — all AuthorizationPipeline tests pass
grep -q "\[AllowAnonymous\]" LicensingAdmin/Pages/Account/Login.cshtml.cs        # expect: exit 0
grep -q "\[AllowAnonymous\]" LicensingAdmin/Pages/Account/AccessDenied.cshtml    # expect: exit 0
```

**Checkpoint**
```bash
git add -A && git commit -m "step 10: login-pages"
git tag step-10-login-pages
```

---

### Step 11 — Seeder del primer `SuperAdmin`

**Do**
- `LicensingAdmin/Startup/AdminSeeder.cs` según §8 D3: `IHostedService`. `static bool ShouldSeed(bool
  tableEmpty, string? email, string? password)` = `tableEmpty && !string.IsNullOrWhiteSpace(email) &&
  !string.IsNullOrWhiteSpace(password)`. `static AdminUser BuildSuperAdmin(string email, string
  password, PasswordHasherService hasher)` = `new AdminUser { Email = email, PasswordHash =
  hasher.Hash(password), Role = AdminRole.SuperAdmin, IsActive = true }`. `StartAsync`: si
  `ShouldSeed(!await db.AdminUsers.AnyAsync(), config["Admin:BootstrapEmail"],
  config["Admin:BootstrapPassword"])`, inserta el usuario + un `AuditLogEntry` (`Actor`="system",
  `Action`="Created", `EntityType`="AdminUser").
- `LicensingAdmin/Program.cs` — `builder.Services.AddHostedService<AdminSeeder>()`.
- `LicensingSystem.Tests/AdminSeederTests.cs` — clase `AdminSeederTests`: la matriz de `ShouldSeed`
  y las propiedades de `BuildSuperAdmin`.
- `LicensingSystem.Tests/AuthorizationPipelineTests.cs` — **en este mismo commit**, extender la
  `WebApplicationFactory<Program>` para que en `ConfigureTestServices` quite el `IHostedService`
  cuyo `ImplementationType` es `AdminSeeder`
  (`services.Remove(services.Single(d => d.ImplementationType == typeof(AdminSeeder)))`), de modo
  que el host de test no ejecute `AdminSeeder.StartAsync` contra la cadena ficticia. Sin esto, el
  paso 11 rompería la puerta `dotnet test --filter AuthorizationPipeline` del paso 10 y toda puerta
  de suite completa posterior (§9 regla 7).

**Done when**
- [ ] WHEN `AdminSeeder.ShouldSeed(true, "a@b.c", "pw")` runs THE SYSTEM SHALL return true, and WHEN called with `false` as the first argument THE SYSTEM SHALL return false.
- [ ] WHEN `AdminSeeder.ShouldSeed(true, null, "pw")` or `AdminSeeder.ShouldSeed(true, "a@b.c", null)` runs THE SYSTEM SHALL return false.
- [ ] WHEN `AdminSeeder.BuildSuperAdmin("a@b.c", "pw", hasher)` runs THE SYSTEM SHALL return an `AdminUser` with `Role == SuperAdmin`, `IsActive == true`, and a non-empty `PasswordHash` not equal to `"pw"`.
- [ ] WHEN `LicensingAdmin/Program.cs` is inspected THE SYSTEM SHALL register `AddHostedService<AdminSeeder>()` exactly once.
- [ ] WHEN the `WebApplicationFactory<Program>` in `AuthorizationPipelineTests` starts THE SYSTEM SHALL remove the `AdminSeeder` hosted service in `ConfigureTestServices` so no database access occurs at host startup.
- [ ] WHEN `dotnet test --filter AdminSeeder` and `dotnet test --filter AuthorizationPipeline` run THE SYSTEM SHALL both report all tests passed, 0 failed.

**Verify**
```bash
dotnet test --filter AdminSeeder              # expect: exit 0 — all AdminSeeder tests pass
dotnet test --filter AuthorizationPipeline    # expect: exit 0 — still passes with AddHostedService<AdminSeeder>() registered
```

**Checkpoint**
```bash
git add -A && git commit -m "step 11: admin-seeder"
git tag step-11-admin-seeder
```

---

### Step 12 — Proteger pantallas existentes + revisor real

**Do**
- `LicensingAdmin/Pages/Dashboard.razor` y `Licenses.razor` — añadir
  `@attribute [Authorize(Policy = AuthPolicies.ViewerAccess)]`.
- `LicensingAdmin/Pages/PendingReview.razor` — añadir
  `@attribute [Authorize(Policy = AuthPolicies.ReviewAccess)]`; inyectar
  `AuthenticationStateProvider`; en `Resolve(...)` fijar `entity.ReviewedBy` y `AuditLogEntry.Actor`
  desde `CurrentAdmin.Email((await authState.GetAuthenticationStateAsync()).User)`; **borrar** el
  literal `"support-staff@vendor.com"` y el comentario `TODO`.
- `LicensingAdmin/Auth/CurrentAdmin.cs` — `public static class CurrentAdmin` con
  `public static string Email(ClaimsPrincipal principal)` = `principal?.Identity?.IsAuthenticated ==
  true ? (principal.FindFirstValue(ClaimTypes.Name) ?? "") : ""`.
- `LicensingSystem.Tests/CurrentAdminTests.cs` — clase `CurrentAdminTests`: principal autenticado
  con claim Name → devuelve ese valor; principal anónimo (`new ClaimsPrincipal()`) → `""`.

`MainLayout.razor` **no se toca aquí** (su shell de auth va en el paso 14).

**Done when**
- [ ] WHEN `Dashboard.razor` and `Licenses.razor` are inspected THE SYSTEM SHALL each carry `@attribute [Authorize(Policy = AuthPolicies.ViewerAccess)]`.
- [ ] WHEN `PendingReview.razor` is inspected THE SYSTEM SHALL carry `@attribute [Authorize(Policy = AuthPolicies.ReviewAccess)]`.
- [ ] WHEN an activation is approved or rejected THE SYSTEM SHALL set `entity.ReviewedBy` and the new `AuditLogEntry.Actor` from `CurrentAdmin.Email(...)`, not from any hardcoded literal.
- [ ] WHEN `grep -RIl "support-staff@vendor.com" LicensingAdmin/` runs THE SYSTEM SHALL find no match and exit 1.
- [ ] WHEN `CurrentAdmin.Email(principal)` is given an authenticated principal THE SYSTEM SHALL return its `ClaimTypes.Name` value, and given an anonymous principal SHALL return `""`.
- [ ] WHEN `dotnet build LicensingSystem.sln` and `dotnet test --filter CurrentAdmin` run THE SYSTEM SHALL both exit 0 with all `CurrentAdmin` tests passed.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
grep -RIl "support-staff@vendor.com" LicensingAdmin/; test $? -eq 1
# expect: exit 0 — grep exits 1 (literal gone)
test -z "$(grep -L '@attribute \[Authorize(Policy = AuthPolicies\.' LicensingAdmin/Pages/Dashboard.razor LicensingAdmin/Pages/Licenses.razor LicensingAdmin/Pages/PendingReview.razor)"
# expect: exit 0 — every listed page contains an AuthPolicies-based [Authorize] attribute (grep -L prints nothing)
dotnet test --filter CurrentAdmin             # expect: exit 0 — all CurrentAdmin tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 12: gate-screens"
git tag step-12-gate-screens
```

---

### Step 13 — Servicio de emisión de licencias

**Do**
- `LicensingAdmin/Licensing/LicenseIssuanceRequest.cs` — DTO: `Guid? ExistingProductId`; campos de
  producto nuevo (`string? NewProductName`, `NewProductVendor`, `NewProductCurrentVersion`, `int?
  NewProductDefaultMaxActivations`); `LicenseModel Model`; `int MaxActivations`; `DateTime?
  SubscriptionExpiryUtc`; `string? CustomerEmail`, `CustomerName`.
- `LicensingAdmin/Licensing/ILicenseStore.cs` — `Task<bool> LicenseKeyExistsAsync(string key)`;
  `Task AddAsync(SoftwareProduct? newProduct, License license, AuditLogEntry audit)`.
- `LicensingAdmin/Licensing/EfLicenseStore.cs` — impl real con `IDbContextFactory<AppDbContext>`
  (`db.SoftwareProducts`, `db.Licenses`, `db.AuditLogEntries`, o `db.Set<T>()`).
- `LicensingAdmin/Licensing/LicenseIssuanceService.cs` — `Task<License> IssueAsync(LicenseIssuanceRequest
  req)`: elige el producto existente o crea uno; genera clave con `LicenseKeyGenerator.NewKey()`
  reintentando mientras `LicenseKeyExistsAsync` sea true; snapshot de `ModelSnapshot` y
  `MaxActivations` desde el request/producto; `Signature = signer.Sign(license)`; construye `License`
  + `AuditLogEntry` (`Action`="Created", `EntityType`="License", `Actor` = email pasado por la
  página); persiste vía `ILicenseStore.AddAsync`.
- `LicensingSystem.Tests/LicenseIssuanceServiceTests.cs` — clase `LicenseIssuanceServiceTests` con
  un fake `ILicenseStore` y un `ILicenseSigner` real (RSA en memoria): la clave emitida casa la
  regex; `signer.Verify` pasa sobre el resultado; reintenta cuando el fake reporta que la primera
  clave existe; `ModelSnapshot`/`MaxActivations` copiados del request; el `AuditLogEntry` pasado al
  store tiene `Action`="Created".

**Done when**
- [ ] WHEN `LicenseIssuanceService.IssueAsync(request)` runs with a valid request THE SYSTEM SHALL produce a `License` whose `LicenseKey` matches `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$`.
- [ ] WHEN the produced `License.Signature` is checked with `ILicenseSigner.Verify` and the issuing public key THE SYSTEM SHALL return true.
- [ ] WHEN the fake `ILicenseStore` reports the first generated key already exists THE SYSTEM SHALL generate another key and retry until `LicenseKeyExistsAsync` returns false.
- [ ] WHEN the request carries model flags and `MaxActivations` THE SYSTEM SHALL copy them into `License.ModelSnapshot` and `License.MaxActivations`.
- [ ] WHEN issuance succeeds THE SYSTEM SHALL pass the store one `AuditLogEntry` with `Action == "Created"` and `EntityType == "License"`.
- [ ] WHEN `dotnet test --filter LicenseIssuance` runs THE SYSTEM SHALL report all `LicenseIssuance` tests passed, 0 failed.

**Verify**
```bash
dotnet test --filter LicenseIssuance          # expect: exit 0 — all LicenseIssuance tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 13: license-issuance"
git tag step-13-license-issuance
```

---

### Step 14 — Pantalla "Generar nueva licencia" + shell de `MainLayout`

**Do**
- `LicensingAdmin/Pages/Licenses/New.razor` — `@page "/licenses/new"`,
  `@attribute [Authorize(Policy = AuthPolicies.IssueAccess)]`. `MudForm`: `MudSelect` de
  `software_products` existentes + switch "Nuevo producto" que revela Name/Vendor/CurrentVersion/
  DefaultMaxActivations; flags de `LicenseModel` como checkboxes; `MudNumericField` MaxActivations;
  `MudDatePicker` SubscriptionExpiryUtc habilitado **solo** si el flag `Subscription` está marcado;
  `MudTextField` CustomerEmail/CustomerName. Submit → `LicenseIssuanceService.IssueAsync` con el
  email de `CurrentAdmin.Email(...)` → muestra la clave en un `MudPaper` con botón copiar + enlace
  "Volver a Licencias". Estados loading/empty/error de §6.
- `LicensingAdmin/Shared/MainLayout.razor` — en `MudAppBar`, `<AuthorizeView>` que muestra
  `context.User.Identity?.Name` y un enlace "Cerrar sesión" a `/Account/Logout`; reemplaza el texto
  estático "Support Staff". En `MudNavMenu`, envolver **cada** `MudNavLink` en `<AuthorizeView
  Policy="...">`: Dashboard/Licencias = `ViewerAccess`, Pending Review = `ReviewAccess`, un enlace
  nuevo "Generar licencia" (`Href="licenses/new"`) = `IssueAccess`, y un enlace "Usuarios admin"
  (`Href="admin/users"`) = `AdminUserAccess`. El enlace a `/admin/users` apunta a una ruta que aún
  no existe hasta el paso 15 — es válido (`dotnet build`/`dotnet test` no lo renderizan) y solo lo ve
  `SuperAdmin`.

**Done when**
- [ ] WHEN `LicensingAdmin/Pages/Licenses/New.razor` is inspected THE SYSTEM SHALL carry `@attribute [Authorize(Policy = AuthPolicies.IssueAccess)]` so a `ReadOnlyViewer` or anonymous user is denied.
- [ ] WHEN the form is submitted with the "new product" switch on THE SYSTEM SHALL create the `SoftwareProduct` and then issue the license through `LicenseIssuanceService`, and WHEN an existing product is chosen THE SYSTEM SHALL reuse it.
- [ ] WHEN issuance succeeds THE SYSTEM SHALL display the generated license key in a `MudPaper` with a copy button and a link back to `/licenses`.
- [ ] WHEN the `Subscription` model flag is not selected THE SYSTEM SHALL keep the subscription-expiry date picker disabled.
- [ ] WHEN `MainLayout.razor` is rendered THE SYSTEM SHALL show the signed-in user's email and a logout link and SHALL wrap every nav link, including "Generar licencia" (`IssueAccess`) and "Usuarios admin" (`AdminUserAccess`), in `<AuthorizeView Policy="...">`.
- [ ] WHEN `dotnet build LicensingSystem.sln` and the full `dotnet test` suite run THE SYSTEM SHALL both exit 0 with 0 failed and 0 skipped.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
grep -q '@attribute \[Authorize(Policy = AuthPolicies\.IssueAccess)\]' LicensingAdmin/Pages/Licenses/New.razor
# expect: exit 0 — New.razor carries the attribute (1 = pattern absent, 2 = file missing — both fail the gate)
dotnet test                                   # expect: exit 0 — whole suite, 0 failed, 0 skipped
# manual: compare /licenses/new against epics/02-auth-y-licencias.md task `E2-T6`; run `dotnet run --project LicensingAdmin` against 172.16.101.12 with a seeded SuperAdmin, sign in, issue a license, confirm the rendered key matches the format regex
```

**Checkpoint**
```bash
git add -A && git commit -m "step 14: generate-license-page"
git tag step-14-generate-license-page
```

---

### Step 15 — Pantalla de administradores

**Do**
- `LicensingAdmin/Auth/IAdminUserStore.cs` — `Task<bool> EmailExistsAsync(string email)` (case-insensitive);
  `Task<IReadOnlyList<AdminUser>> ListAsync()`; `Task AddAsync(AdminUser user, AuditLogEntry audit)`;
  `Task SetActiveAsync(Guid id, bool active, AuditLogEntry audit)`.
- `LicensingAdmin/Auth/AdminUserService.cs` — `CreateAsync(string email, AdminRole role, string
  tempPassword)`: `EmailExistsAsync` → si true, rechaza (excepción de dominio o resultado tipado) sin
  escribir; en otro caso construye `AdminUser` con `PasswordHash = hasher.Hash(tempPassword)` y
  `AuditLogEntry` `Action`="Created", `EntityType`="AdminUser". `DeactivateAsync(Guid id)` /
  `ActivateAsync` → `SetActiveAsync` + `AuditLogEntry` `Action`="Updated". Impl EF de `IAdminUserStore`
  con `IDbContextFactory<AppDbContext>` (puede ir en el mismo archivo o junto a `EfLicenseStore`).
- `LicensingAdmin/Pages/Admin/Users.razor` — `@page "/admin/users"`,
  `@attribute [Authorize(Policy = AuthPolicies.AdminUserAccess)]`. `MudTable` con la lista; formulario
  de alta (email + `MudSelect<AdminRole>` + contraseña temporal); botón activar/desactivar por fila.
  El enlace de nav a esta pantalla ya lo añadió el paso 14.
- `LicensingSystem.Tests/AdminUserServiceTests.cs` — clase `AdminUserServiceTests` con un fake
  `IAdminUserStore`: crear construye un `AdminUser` con hash no plano y el rol dado; email duplicado
  (cualquier caso) se rechaza y no llama `AddAsync`; desactivar invoca `SetActiveAsync(id, false, ...)`
  con un `AuditLogEntry` `Action`="Updated".

**Done when**
- [ ] WHEN `LicensingAdmin/Pages/Admin/Users.razor` is inspected THE SYSTEM SHALL carry `@attribute [Authorize(Policy = AuthPolicies.AdminUserAccess)]`.
- [ ] WHEN `AdminUserService.CreateAsync(email, role, tempPassword)` runs THE SYSTEM SHALL build an `AdminUser` whose `PasswordHash` comes from `PasswordHasherService.Hash` (never the plaintext) and whose `Role` is the requested role.
- [ ] WHEN `CreateAsync` is given an email that already exists case-insensitively THE SYSTEM SHALL reject it and call no store write.
- [ ] WHEN a successful create or an `IsActive` toggle occurs THE SYSTEM SHALL write an `AuditLogEntry` (`Action` "Created" or "Updated", `EntityType` "AdminUser").
- [ ] WHEN `dotnet build LicensingSystem.sln` and `dotnet test --filter AdminUserService` run THE SYSTEM SHALL both exit 0 with all `AdminUserService` tests passed.

**Verify**
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
grep -q '@attribute \[Authorize(Policy = AuthPolicies\.AdminUserAccess)\]' LicensingAdmin/Pages/Admin/Users.razor
# expect: exit 0 — Users.razor carries the attribute (1 = pattern absent, 2 = file missing — both fail the gate)
dotnet test --filter AdminUserService         # expect: exit 0 — all AdminUserService tests pass
```

**Checkpoint**
```bash
git add -A && git commit -m "step 15: admin-users"
git tag step-15-admin-users
```

---

### Step 16 — Smoke de solución + regresión + auditoría de checkpoints (cierre)

**Do**
Sin código de producto nuevo. Añadir a `LicensingSystem.Tests/SmokeTests.cs` una aserción de
regresión final: `[Fact]` que genera una clave con `LicenseKeyGenerator.NewKey()` y asserta que casa
la misma regex que usa `ActivationService.LicenseKeyFormat()` (copiada literal de la fila 6 de §5).
Correr la puerta completa.

**Done when**
- [ ] WHEN `dotnet build LicensingSystem.sln -warnaserror` runs THE SYSTEM SHALL exit 0 with no analyzer warning escalated to an error.
- [ ] WHEN `dotnet test` runs the full suite THE SYSTEM SHALL exit 0 with 0 failed and 0 skipped.
- [ ] WHEN `grep -RIl "support-staff@vendor.com" LicensingAdmin/` runs THE SYSTEM SHALL find no match and exit 1.
- [ ] WHEN `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` runs THE SYSTEM SHALL find no match and exit 1.
- [ ] WHEN this step's own checkpoint tag exists THE SYSTEM SHALL make `git tag -l 'step-*'` list exactly 16 tags, one per build step.

**Verify**
```bash
dotnet build LicensingSystem.sln -warnaserror         # expect: exit 0, 0 warnings
dotnet test                                           # expect: exit 0 — 0 failed, 0 skipped
grep -RIl "support-staff@vendor.com" LicensingAdmin/; test $? -eq 1   # expect: exit 0 (no match)
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1  # expect: exit 0 (no match in any tracked file outside the bundle)
```

**Checkpoint**
```bash
git add -A && git commit -m "step 16: smoke"
git tag step-16-smoke
test "$(git tag -l 'step-*' | wc -l)" -eq 16   # expect: exit 0 — 16 checkpoint tags, one per step (asserted here, after this step's own tag exists — §9 rule 5)
```

---

### 9.1 Parity and cutover

NOT APPLICABLE — no system is being replaced and no schema changes; this is an additive feature slice
on a running codebase, not a migration.

---

## 10. Environment Setup

### Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 8.0.4xx o más nuevo (TFM `net8.0`, sin `global.json`) | `dotnet --version` |
| `dotnet-ef` (global tool) | 8.x | `dotnet ef --version` — **no se usa en este slice**; se instala por si un futuro slice lo necesita |
| PostgreSQL | 15+ accesible en `172.16.101.12:5432` | `psql -h <host> -U <user> -d licensing_app -c 'select 1'` — **ni `dotnet build` ni `dotnet test` lo necesitan** (el test de pipeline usa una cadena ficticia y no conecta); solo los chequeos `# manual:` y ejecutar las apps |

### Accounts to create first

Ninguna cuenta de terceros. Todo el build corre con el SDK de .NET local. El único servicio externo
es la base de datos PostgreSQL ya existente (no la crea el build).

### Environment variables

`.env.example` **NOT APPLICABLE** para una solución .NET: la configuración viene de `appsettings.json`
(no secreto), `dotnet user-secrets` (secretos locales, indexados por el `<UserSecretsId>` añadido en
el paso 1) y variables de entorno (CI/prod). Los archivos de `dotnet user-secrets` viven **fuera**
del árbol del repo por diseño. No hay cargador de dotenv en .NET.

| Variable | Propósito | De dónde sacarla | Requerida desde el paso | ¿Secreta? |
|---|---|---|---|---|
| `ConnectionStrings__LicensingDb` | Cadena de conexión a Postgres de ambas apps | El DBA / `dotnet user-secrets set` | **paso 3** — para *ejecutar* cualquiera de las apps; **no** para `dotnet build`/`dotnet test` (el guardián del paso 2 solo se dispara al arrancar un host, y el test de pipeline provee una cadena ficticia) | sí |
| `Crypto__RsaPrivateKeyPem` | Clave privada RSA (PEM) para firmar registros `License` en el panel | Almacén de claves del vendor / `dotnet user-secrets set` | **paso 6** — opcional: sin ella hay fallback RSA en memoria (dev-only, con warning) | sí |
| `Crypto__AesKeyBase64` | Clave AES del archivo de licencia — **preexistente, solo `LicensingApi`** | Almacén de claves del vendor | preexistente — este slice no la toca | sí |
| `Admin__BootstrapEmail` | Email del primer `SuperAdmin` a sembrar | Decisión de operaciones | **paso 11** — opcional: el seeder es no-op sin ella | no |
| `Admin__BootstrapPassword` | Contraseña del primer `SuperAdmin` a sembrar | Decisión de operaciones | **paso 11** — opcional | sí |

**"Requerida desde el paso" es un contrato con §9.** Ninguna de estas variables condiciona
`dotnet build` ni `dotnet test`: la suite es unitaria pura salvo `AuthorizationPipelineTests`, que
arranca el host con una cadena de conexión ficticia y sin el `AdminSeeder`, y cuyas peticiones
anónimas reciben 302 antes de tocar la BD. El guardián del paso 2 solo falla al *ejecutar* una app
sin la cadena configurada (§9 regla 7).

**Cargar variables:** ningún comando `Verify`, de Bootstrap o de un **Do** de §9 invoca una
herramienta que lea estas variables — `dotnet build` y `dotnet test` no leen `appsettings.json` ni el
entorno para la cadena de conexión de forma que dispare el guardián. `dotnet ef` no se usa. Ver §19.6.

### Files that must be committed

El repo ya tiene `.gitignore`. Bloquea `bin/`, `obj/`, `[Dd]ebug/`, `[Rr]elease/`, `.vs/`,
`*.user`, `*.suo`, `.idea/`, `_ReSharper*/`, `secrets.json`, `appsettings.*.Local.json`, `*.pfx`,
`*.pem`, `dev-private.pem`, `dev-public.pem`, `.DS_Store`, `Thumbs.db`, `/status.local.md`. Ninguno
de los archivos que este blueprint describe como *committed* casa con esos patrones:

| File | Por qué se commitea | Línea de excepción en el ignore |
|---|---|---|
| `docs/blueprint/**` | El bundle vive dentro del proyecto (convención del equipo) | — no lo captura ningún patrón |
| `tasks.json` (raíz) | DAG de tareas del bundle; convención del equipo lo pone en la raíz | — no lo captura ningún patrón |
| `LicensingSystem.Tests/**` (excepto `bin/`,`obj/`) | Proyecto de tests del slice | — `bin/`/`obj/` sí se ignoran (correcto); el resto no |
| `.claude/**` (raíz, tras la copia) | Config de agentes | — no lo captura ningún patrón (`.idea/` sí, `.claude/` no) |
| `CLAUDE.md`, `AGENTS.md` (raíz, tras la copia) | Instrucciones de agentes | — no los captura ningún patrón |
| `LicensingApi/appsettings.json`, `LicensingAdmin/appsettings.json` | Config no secreta, ahora con `LicensingDb: ""` | — solo `appsettings.*.Local.json` se ignora, no `appsettings.json` |

**El `.gitignore` es preexistente y ya está en el commit inicial del baseline** — ningún paso de §9
lo crea ni lo necesita crear, así que el requisito "el ignore precede al primer commit" se cumple
por construcción. Ningún paso de §9 edita `.gitignore`.

### Bootstrap

```bash
# order: repo ya existe (baseline, 1 commit) -> copia workspace -> restore -> tool -> build
git rev-parse --git-dir >/dev/null 2>&1 || git init -b main   # idempotente: el repo ya existe, no-op
# copiar la config de agentes a la raíz sin pisar nada ya presente:
rsync -a --ignore-existing docs/blueprint/workspace/ ./       # nunca clobbera; añade CLAUDE.md/AGENTS.md/.claude/ solo si faltan; sale 0 aunque salte archivos
# fallback en máquinas sin rsync (BSD/macOS cp -n sale 1 al saltar; el || true lo neutraliza):
# cp -Rn docs/blueprint/workspace/. ./ || true
dotnet restore LicensingSystem.sln                            # idempotente
dotnet tool install --global dotnet-ef || dotnet tool update --global dotnet-ef   # el || cubre el re-run (un install repetido sale 1)
dotnet build LicensingSystem.sln                              # idempotente
```

**El repositorio y el primer commit ya existen** (baseline brownfield con un único commit); `HEAD`
sirve de ancla para los `git tag` de cada Checkpoint. No hace falta un commit de bootstrap.

**Todo comando de este bloque es seguro de re-ejecutar.** `rsync --ignore-existing` no pisa archivos
y sale 0 aunque salte todos; **no se emite ningún manifiesto de paquete bajo `workspace/`**, así que
la copia no puede revertir dependencias — `--ignore-existing` solo protege ediciones locales a
`settings.json` o a las reglas. El único archivo que nunca se sobrescribe es cualquier `.claude/*`
que el operador haya tocado a mano tras el primer bootstrap.

---

## 11. Dependencies

Única sección con números de versión (más los valores literales de los archivos emitidos en §19,
que aquí no hay: ningún compose, ningún `package.json`). `Source` y `Checked` vienen del informe de
`stack-researcher` de esta sesión (2026-09-02, contra nuget.org).

### Runtime

| Package | Version | Source | Checked | Installed by | Purpose |
|---|---|---|---|---|---|
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.10 | repo `LicensingCore.csproj` / `LicensingApi.csproj` / `LicensingAdmin.csproj` | preexistente | preexistente; restaurado por §10 Bootstrap `dotnet restore` | Provider EF Core para Postgres — acceso a datos de las 3 capas |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.10 | repo `LicensingApi.csproj` | preexistente | preexistente; restaurado por §10 Bootstrap | Soporte de diseño EF; no se usa activamente en el slice |
| `MudBlazor` | 7.15.0 | repo `LicensingAdmin.csproj` | preexistente | preexistente; restaurado por §10 Bootstrap | Librería de componentes del panel; las pantallas nuevas la componen |
| Cookie authentication (`Microsoft.AspNetCore.Authentication.Cookies`) | — (sin pin) | .NET 8 shared framework (`Microsoft.AspNetCore.App`) | 2026-09-02 | preexistente (shared framework) | Auth por cookie del panel — paso 8 |
| `PasswordHasher<T>` (`Microsoft.AspNetCore.Identity`) | — (sin pin) | .NET 8 shared framework (`Microsoft.AspNetCore.App`) | 2026-09-02 | preexistente (shared framework) | Hashing PBKDF2 de contraseñas admin — paso 7 |

### Development

| Package | Version | Source | Checked | Installed by | Purpose |
|---|---|---|---|---|---|
| `Microsoft.NET.Test.Sdk` | 18.9.0 | https://api.nuget.org/v3-flatcontainer/microsoft.net.test.sdk/index.json | 2026-09-02 | **paso 1** — `<PackageReference>` en `LicensingSystem.Tests.csproj`, restaurado por `dotnet build`/`dotnet test` | Host de plataforma de test para `dotnet test` |
| `xunit.v3` | 3.2.2 | https://www.nuget.org/packages/xunit.v3 | 2026-09-02 | **paso 1** | Framework de tests unitarios (proyecto compila como `Exe`) |
| `xunit.runner.visualstudio` | 3.1.5 | https://www.nuget.org/packages/xunit.runner.visualstudio | 2026-09-02 | **paso 1** | Adaptador VSTest para `dotnet test` / `--filter` |
| `coverlet.collector` | 10.0.1 | https://api.nuget.org/v3-flatcontainer/coverlet.collector/index.json | 2026-09-02 | **paso 1** | Recolección de cobertura (opcional, no puerta) |
| `Microsoft.AspNetCore.Mvc.Testing` | 8.0.30 | https://www.nuget.org/packages/Microsoft.AspNetCore.Mvc.Testing/8.0.30 | 2026-09-02 | **paso 1** | `WebApplicationFactory<Program>` para `AuthorizationPipelineTests` (paso 10). Dependencias transitivas (`Microsoft.AspNetCore.TestHost 8.0.30`, `Microsoft.Extensions.Hosting 8.0.1`, `Microsoft.Extensions.DependencyModel 8.0.2`) todas en 8.0.x |

Ninguno de los 5 paquetes de test depende de Npgsql / EF Core / MudBlazor — sin conflicto de
versiones transitivas. `xunit.v3` requiere que el proyecto de test compile como `Exe`; el paso 1
añade `<OutputType>Exe</OutputType>` explícitamente por si el SDK no lo hace solo.
**.NET 8 es LTS con soporte hasta 2026-11-10**; tras esa fecha la línea 8.0.x deja de recibir
parches — planificar un salto a net10.0 antes de entonces (ver §20.2).

### Deliberately not used

| Rejected | Instead | Why |
|---|---|---|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (Identity completo) | Solo `PasswordHasher<AdminUser>` contra la tabla `admin_users` existente | Este slice no cambia el esquema; Identity completo trae ~7 tablas |
| `xunit` (v2) | `xunit.v3` 3.2.2 | La entrevista fijó la línea v3 |
| `Moq` / `NSubstitute` | Fakes escritos a mano detrás de `ILicenseStore` / `IAdminUserStore` / `IAdminUserLookup` | Mantiene los tests con dependencias mínimas; el brief exige fake stores |
| `Testcontainers` / `Microsoft.EntityFrameworkCore.InMemory` / fixture de Postgres | Tests unitarios puros + `AuthorizationPipelineTests` con cadena ficticia (nunca conecta) | §20.3 #6 |
| `BCrypt.Net-Next` / `Isopoh.Cryptography.Argon2` | `PasswordHasher<T>` (PBKDF2) del shared framework | Sin dependencia nueva |

---

## 12. Deployment Strategy

### Hosting

Sin cambios. Único host Windows/IIS; `LicensingApi`, `LicensingAdmin` y PostgreSQL co-ubicados. No
hay CI en el repo. "Build" para desplegar = `dotnet publish` de cada app y copiar la salida al sitio
IIS (procedimiento operativo existente, fuera de este blueprint).

### Environments

| Environment | Branch | URL | Database | Modo terceros |
|---|---|---|---|---|
| Local | — | `http://localhost:<puerto>` | `172.16.101.12` (dev) o local | — |
| Producción | `main` | host IIS interno | `172.16.101.12` | — |

No hay entorno de preview.

### CI/CD

No hay pipeline en el repo. La puerta de aceptación (§20.1) se corre a mano en el nodo `dev` antes de
publicar. Si se añade CI en un slice futuro, corre exactamente el set de §20.1.

### Release and rollback

- **Release**: recompilar y re-desplegar la salida publicada.
- **Rollback**: `git reset --hard step-<NN>-<slug>` al último checkpoint bueno + re-desplegar.
- **Migraciones**: N/A en este slice (sin cambios de esquema).

### Domain, DNS, TLS

Sin cambios. Host interno; la cookie usa `CookieSecurePolicy.SameAsRequest` porque la LAN puede
servir por http (se acepta explícitamente en §8 y §14).

---

## 13. Testing Strategy

| Layer | Framework | Qué cubre | Dónde | Corre |
|---|---|---|---|---|
| Unit | xUnit v3 (`LicensingSystem.Tests`) | Lógica pura: guardián de cadena, generación de clave, firma/verificación RSA + tamper, hash de contraseña round-trip, matriz rol→política + fallback, decisión de seed, emisión (fake store), servicio de administradores (fake store), extracción de email de `ClaimsPrincipal` | `LicensingSystem.Tests/*.cs` | cada commit (`dotnet test`) |
| Pipeline HTTP | `WebApplicationFactory<Program>` (xUnit v3) | Una petición anónima a `/` y `/pending-review` recibe 302 a `/Account/Login`; `/Account/Login` responde 200 | `LicensingSystem.Tests/AuthorizationPipelineTests.cs` | cada commit (`dotnet test`) |
| Integration (datos) | — | Diferida | — | — |
| E2E | — | Ninguna | — | — |

Comandos: `dotnet test` (toda la suite) y `dotnet test --filter <ClassName>` (una clase).

### Critical flows to cover E2E

Ninguno automatizado en este slice. El flujo end-to-end de emisión (iniciar sesión en el panel real
contra `172.16.101.12`, sembrar un `SuperAdmin`, emitir una licencia y confirmar que la clave
renderiza y casa la regex) es la **única** anotación `# manual:` del epic 02, en el paso 14.

### Test data

Los tests unitarios son puros: cada test construye sus propios objetos y fakes.
`AuthorizationPipelineTests` arranca el host con una cadena de conexión ficticia y sin el
`AdminSeeder`; ninguna de sus peticiones toca la BD. Ninguna capa corre contra un servicio real, así
que §19.6 no emite ningún archivo de provisión de infraestructura.

### What is deliberately not tested

- Integración de datos contra PostgreSQL real (§20.3 #6) — se prueba a mano en el paso 14.
- El sign-in real con cookie / `HttpContext.SignInAsync` / la escritura de `LastLoginAtUtc` y del
  `AuditLogEntry` "Login" — glue fino contra la BD, cubierto por el paso 14 `# manual:`
  (`AuthorizationPipelineTests` solo ejercita el challenge anónimo).
- El arranque completo del web host de `LicensingApi` — `dotnet build` cubre la compilación.
- Accesibilidad automatizada — Non-Goal de este slice.

---

## 14. Security & Secrets

| Concern | Control | Implementado en |
|---|---|---|
| Almacenamiento de secretos | `dotnet user-secrets` (local) / variables de entorno (prod); nunca en el repo | `appsettings.json` con `LicensingDb: ""` tras el paso 3; READMEs con placeholder tras el paso 3 |
| Guardián de arranque | `ConnectionStringGuard.Require` lanza `InvalidOperationException` con instrucciones si la cadena está vacía/ausente | `LicensingCore/Configuration/ConnectionStringGuard.cs`, llamado en ambos `Program.cs` (paso 2) |
| Rotación de secretos | Manual: re-set en user-secrets/env + reinicio de la app. La RSA en memoria de fallback invalida firmas previas en cada reinicio (dev-only) | procedimiento operativo |
| Hashing de contraseñas | `PasswordHasher<AdminUser>` (PBKDF2, shared framework) | `LicensingAdmin/Auth/PasswordHasherService.cs` (paso 7) |
| Validación de entrada | `MudForm` + validación de modelo en las páginas; `AdminUserService` rechaza email duplicado; el generador de clave garantiza el formato | pasos 13, 14, 15 |
| Output encoding / XSS | Razor/Blazor escapan por defecto; las pantallas no renderizan HTML crudo | pantallas nuevas |
| SQL injection | EF Core parametriza todo; no hay SQL construido por concatenación | `EfLicenseStore`, impl EF de las costuras |
| AuthN / AuthZ | Cookie auth + `FallbackPolicy` + políticas server-side en cada página (§8); `<AuthorizeView>` solo cosmético; `AuthorizationPipelineTests` prueba el challenge anónimo | pasos 8, 10, 12, 14, 15 |
| CSRF | Antiforgery token por defecto de Razor Pages en `/Account/Login`; Blazor Server va por circuito SignalR | `Login.cshtml` |
| Rate limiting / abuso | Fuera de alcance (Non-Goal); login con error genérico para no facilitar fuerza bruta por enumeración | — |
| Enumeración de usuarios | Un solo texto de error genérico en todo fallo de login (no se distingue causa) | `Login.cshtml.cs` (paso 10) |
| Webhook verification | N/A — no hay webhooks en este slice | — |
| Auditoría de dependencias | `dotnet list package --vulnerable` a mano antes de publicar | operativo |
| Logging hygiene | Nunca se loguea la contraseña ni el PEM; el fallback RSA loguea solo un warning sin material de clave | `CryptoRegistration.cs` |
| PII | `admin_users.Email`, `licenses.CustomerEmail/CustomerName` — ya existentes; sin cambios de retención en este slice | — |

**Hard rules**
- Ningún secreto se commitea, se imprime en un log, se manda a un tracker de errores ni se
  embebe en un bundle de cliente.
- Toda comprobación de autorización server-side corre **antes** del trabajo, no después.
- El hallazgo preexistente de credencial en texto plano (`Password=***` en ambos `appsettings.json`
  **y** en dos README) es exactamente lo que cierran los pasos 2–3; §20.1 lo verifica con `git grep`
  sobre todos los archivos rastreados fuera del bundle.

Datos regulados: ninguno. El sistema no maneja datos de salud, financieros, de menores ni datos
personales de la UE más allá de emails de contacto de cliente ya presentes.

---

## 15. Accessibility

**Target: WCAG 2.2 AA**, aplicado con criterio ligero — las pantallas nuevas son formularios y
tablas MudBlazor.

### Baseline para las pantallas nuevas (`/Account/Login`, `/licenses/new`, `/admin/users`)

| Requisito | Regla en este slice |
|---|---|
| Teclado | Todos los campos y botones alcanzables y operables por teclado; orden de tabulación lógico; sin trampas |
| Etiquetas | Todo `MudTextField` / `MudSelect` / `MudNumericField` / `MudDatePicker` con `Label` programática |
| Errores | Texto de error, no solo color (`MudAlert` / `HelperText`), y asociado al campo |
| Foco visible | Se conserva el indicador de foco por defecto de MudBlazor (≥3:1) |
| Semántica | Un `<h1>`/`MudText Typo="Typo.h4"` por pantalla; encabezados en orden |

### Verification

```bash
# No hay suite automatizada de accesibilidad en este slice (decisión — Non-Goal).
# Antes de publicar, pase manual: recorrido solo-teclado de login -> emitir licencia -> /admin/users.
```

Deliberadamente no probado: cualquier chequeo automatizado de accesibilidad (axe, Lighthouse) — se
revisa cuando el panel llegue a un público más amplio.

---

## 16. Observability & Cost

### Instrumentation

| Señal | Herramienta | Qué captura | Quién lo mira |
|---|---|---|---|
| Errores | `ILogger` a los logs de la app / Event Log de IIS | Excepciones no manejadas | Operaciones del vendor |
| Logs | `ILogger` estructurado por defecto de ASP.NET Core | Arranque, warning del fallback RSA, fallos de `SaveChanges` | Operaciones |
| Auditoría | tabla `audit_log_entries` | Login, aprobación/rechazo, emisión de licencia, alta/baja de admin | SuperAdmin, compliance |
| Uptime | — | Sin monitor de uptime en este slice | — |

Sin stack de APM ni métricas en este slice — se declara explícitamente.

### The metrics that matter

| Métrica | Objetivo | Alerta |
|---|---|---|
| Filas nuevas en `audit_log_entries` por acción admin que muta datos | 1:1 | 0 filas tras una mutación (revisión manual) |
| Warnings del fallback RSA en prod | 0 | ≥1 (indica `Crypto__RsaPrivateKeyPem` sin configurar en prod) |

### Health check

`GET /health` en la API devuelve `{ "status": "ok" }`. **No** comprueba la base de datos ni el estado
de migraciones — se deja así en este slice (cambiarlo tocaría un contrato congelado del §5).

### Cost model

| Servicio | Coste a escala v1 | Cliff |
|---|---|---|
| Host Windows/IIS + PostgreSQL (self-hosted, existente) | sin cambio por este slice | — |

**Coste mensual estimado por el slice: $0** — solo añade código a apps ya desplegadas; sin servicios
nuevos.

---

## 17. Model Routing

NOT APPLICABLE — this project does not call an LLM at runtime.

---

## 18. Skills to Use During Build

NOT APPLICABLE — the build uses only the .NET SDK toolchain; no plugin skills are required.

---

## 19. Agent Workspace

El bundle vive en `docs/blueprint/` **dentro** del repo (no en `./blueprints/`), y `tasks.json` en
la **raíz** del repo — ambas cosas son convención del equipo (`CLAUDE.md` del repo `dev-team`), no
el layout canónico. Al resumir con `/architect-next`, los archivos de epic están en
`docs/blueprint/epics/<slug>.md`. En bundle mode los archivos de abajo son archivos reales bajo
`docs/blueprint/workspace/`; el builder copia el *contenido* de `workspace/` a la raíz del repo con
el `rsync --ignore-existing` de §10 antes del paso 1. `.claude/commands/` no se emite.

### 19.1 `CLAUDE.md`

Ver `docs/blueprint/workspace/CLAUDE.md` (archivo real). Contenido idéntico, byte a byte, al de
ese archivo. Resumen: comandos (`dotnet build`/`test`/`test --filter`/`run`/`user-secrets`),
arquitectura (3 proyectos, `LicensingCore` compartido, panel DB-directo, login como Razor Page),
convenciones (entidades en `LicensingCore`, `AppDbContext` expone `DbSet<>` con nombre, enums como
`varchar` salvo `LicenseModel`, secretos solo por user-secrets/env, `AuditLogEntry` en cada mutación,
`ResultCode`/`/api/*`/sobre de `LicenseFileService`/`LicenseKeyFormat()` congelados, políticas de
auth por constantes de `AuthPolicies` + `FallbackPolicy`, `public partial class Program` para tests).

### 19.2 `AGENTS.md`

Ver `docs/blueprint/workspace/AGENTS.md` (archivo real). Puente corto: qué es, los comandos clave,
las 3 reglas que muerden (sin secretos en `appsettings.json`; auditar cada mutación admin; no tocar
los contratos congelados), y un puntero a `CLAUDE.md`.

### 19.3 `.claude/settings.json`

Ver `docs/blueprint/workspace/.claude/settings.json` (archivo real). `permissions.allow` cubre cada
comando `Verify` de §9, cada línea de la puerta de §20.1 **y** cada comando del bloque Bootstrap de
§10: `dotnet build|test|restore|run|sln|tool|user-secrets|ef`, `git
rev-parse|init|status|diff|log|add|commit|tag|grep|ls-files|check-ignore|reset`, y `rsync` / `cp` /
`grep` / `awk` / `wc` / `test`. `deny`: leer `appsettings*.Local.json`, `git push`,
`dotnet ef database drop`.

### 19.4 Project skills — `.claude/skills/add-admin-page/SKILL.md`

Ver `docs/blueprint/workspace/.claude/skills/add-admin-page/SKILL.md` (archivo real).

| Skill | Triggers on | Qué automatiza |
|---|---|---|
| `add-admin-page` | "add an admin screen", "nueva pantalla admin", "gated Blazor page" | ruta + `[Authorize(Policy)]` + acceso a datos + `AuditLogEntry` + enlace de nav + test con fake |

### 19.5 `.claude/rules/*.md`

Ver los archivos reales `docs/blueprint/workspace/.claude/rules/entities.md`
(`paths: LicensingCore/**` — snake_case, enums, `LicenseModel` int, sin cambio de esquema, payload
canónico de firma) y `docs/blueprint/workspace/.claude/rules/admin-ui.md`
(`paths: LicensingAdmin/**` — DB-directo vía `IDbContextFactory` o costura, `db.AdminUsers`/etc. o
`db.Set<T>()`, `FallbackPolicy` + `[Authorize(Policy)]` por página, sign-in solo en Razor Pages,
`AuditLogEntry` en cada mutación, orden de middleware).

### 19.6 Verify-critical config and local infrastructure

**NOT APPLICABLE (config de runner) —** todo comando `Verify` de §9 es `dotnet build
LicensingSystem.sln`, `dotnet test [--filter <Nombre>]`, `grep`, `git grep`, `awk` o `test`.
`dotnet test` descubre las pruebas a través de `LicensingSystem.Tests/LicensingSystem.Tests.csproj`
(creado por el paso 1) y de `LicensingSystem.sln`; no hay archivo de configuración de runner que
emitir. `AuthorizationPipelineTests` arranca el host con `WebApplicationFactory<Program>` y provee
su configuración en código (cadena de conexión ficticia, sin `AdminSeeder`) — no hay archivo de
setup que emitir. No se crea ningún golden file ni salida esperada byte-exact.

**Env-loading —** ningún comando `Verify`, de Bootstrap o de un **Do** de §9 invoca una herramienta
que lea variables de entorno de forma que dispare el guardián: `dotnet build` y `dotnet test` no
leen la cadena de conexión real, y `dotnet ef` / `dotnet user-secrets` no aparecen en ningún
`Verify`. `AuthorizationPipelineTests` fija su cadena ficticia en código. Nada que cargar.

**Bundle-path exclusion —** n/a. `dotnet build` y `dotnet test` resuelven proyectos a través de
`LicensingSystem.sln` (lista explícita de `.csproj`), no recorriendo el árbol de directorios.
`docs/blueprint/workspace/` no contiene ningún `.csproj` ni `.cs`, y `docs/` no está bajo ningún
directorio de proyecto, así que MSBuild no compila nada de ahí. No hay formateador ni linter con
descubrimiento por árbol configurado en el repo. No hace falta ninguna línea de exclusión. Los
`grep -RIl` de §9 apuntan a rutas concretas (`LicensingAdmin/`, archivos nombrados); los `git grep`
llevan `-- ':!docs/blueprint'` para no encontrarse a sí mismos.

| File | Path en el proyecto | Qué `Verify` lo necesita | Resolución / env que lleva | Exclusión del bundle |
|---|---|---|---|---|
| — | — | — | ninguno: todo paquete requerido resuelve por `ProjectReference`; ninguna herramienta de `Verify` lee env real | n/a — ni `dotnet build` ni `dotnet test` recorren el árbol; los `git grep` excluyen `docs/blueprint` |

#### Resolution convention matrix

NOT APPLICABLE — this blueprint states no import, specifier, alias, or link convention. .NET resuelve
tipos por las entradas `ProjectReference` y los nombres de ensamblado declarados en
`LicensingSystem.sln`; `dotnet build` y `dotnet test` usan el mismo resolvedor, y el build no tiene
contexto de script suelto, bundler ni codegen.

#### Cross-artifact value reconciliation

| Shared value | Single source | Literal value | Every other place it appears | Compared |
|---|---|---|---|---|
| Archivo de solución | repo (preexistente) | `LicensingSystem.sln` | §19.1 · §19.2 · tasks.json `verify` de cada tarea · epics (Stack) · §9 pasos y §10 Bootstrap · §20.1 | yes |
| Proyecto de tests | paso 1 crea `LicensingSystem.Tests/LicensingSystem.Tests.csproj` | `LicensingSystem.Tests` | §3 árbol · §11 Development · tasks.json `files` de las tareas con test · epics (Directory subtree) | yes |
| Paquete `WebApplicationFactory` | §11 Development | `Microsoft.AspNetCore.Mvc.Testing` 8.0.30 | §2 (Test runner) · §3 (`.csproj`) · §9 paso 1 (instala), paso 10 (crea `AuthorizationPipelineTests`), paso 11 (lo extiende) · §13 (Pipeline HTTP) | yes |
| Var. cadena de conexión (env) | §10 tabla de entorno | `ConnectionStrings__LicensingDb` | §14 · §19.1 · epic 01 (Contracts) · mensaje de `ConnectionStringGuard` | yes |
| Var. PEM (env) | §10 | `Crypto__RsaPrivateKeyPem` | §8 D2 · §14 · §19.1 · epic 01 paso 6 | yes |
| Clave de config PEM | §8 D2 | `Crypto:RsaPrivateKeyPem` | §9 paso 6 Do · `CryptoRegistration.cs` (paso 6) | yes |
| Vars bootstrap admin | §10 | `Admin__BootstrapEmail` / `Admin__BootstrapPassword` | §4 Seed data · §8 D3 · §19.1 · epic 02 paso 11 | yes |
| Constantes de política | `LicensingAdmin/Auth/AuthPolicies.cs` (paso 8) | `ViewerAccess` · `ReviewAccess` · `IssueAccess` · `AdminUserAccess` | §8 (varias tablas) · §9 pasos 8,12,14,15 · §19.1 · `.claude/rules/admin-ui.md` · epics · SKILL.md | yes |
| Regex de clave de licencia | §5 fila 6 (Interfaces held constant) | `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$` | §9 pasos 4, 13, 16 (Do y Done when) · epics 01/02 · tests de los pasos 4, 13, 16 | yes |
| Payload canónico de firma | §8 D2 | `{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{SubscriptionExpiryUtc:O-or-empty-string}` | §9 paso 5 · `.claude/rules/entities.md` · epic 01 paso 5 | yes |
| Literal de revisor a eliminar | código preexistente `PendingReview.razor` | `support-staff@vendor.com` | §1 Success metrics · §9 pasos 12, 16 (grep) · §20.1 · epic 02 pasos 12, 16 | yes |
| Fragmento de credencial a grepear | código preexistente `appsettings.json` / README | `Password=***REDACTED***` (solo dentro de comandos `git grep`/`grep`) | §1 Success metrics · §9 pasos 3, 16 · §20.1 · epic 01 paso 3, epic 02 paso 16 | yes |
| Nombres de tabla | §4 (esquema existente) | `admin_users` · `software_products` · `licenses` · `activations` · `audit_log_entries` | §8 · §9 varios · `.claude/rules/entities.md` · epics | yes |
| Conteo de pasos | §9 step map | `16` | §9 (16 encabezados de paso) · §9 paso 16 Checkpoint · §20.1 · tasks.json (16 objetos) · epics (8 + 8) | yes |
| Slugs de epic | §9 / nombres de archivo de epic | `01-fundamentos` · `02-auth-y-licencias` | `epics/01-fundamentos.md` · `epics/02-auth-y-licencias.md` · tasks.json `epic` de cada tarea | yes |
| Tags de checkpoint | §9 bloques Checkpoint | `step-01-tests-scaffold` … `step-16-smoke` | tasks.json `checkpoint` · epics (bloques Checkpoint) · §20.1 (conteo) | yes |

#### Byte-exact artifact reconciliation

NOT APPLICABLE — this blueprint authors no byte-exact expected output. Todo `Verify` asserta una
propiedad (coincidencia de regex, igualdad de round-trip firma/verificación, evaluación de política,
código de estado HTTP 200/302, código de salida, ausencia de coincidencia de `grep`/`git grep`,
`grep -L` vacío), nunca un `diff` carácter por carácter contra un literal almacenado. La regex de
`LicenseKeyFormat()` que los tests de los pasos 4/13/16 copian literal es un **contrato de entrada**
tomado de §5 (que a su vez lo tomó del código real `ActivationService.cs`), no una salida producida
por el runtime.

---

## 20. Acceptance Gate, Risks & Decision Log

### 20.1 Global acceptance gate

El slice está **hecho** cuando todo lo de abajo sale 0 en un checkout limpio, y no antes.

```bash
dotnet restore LicensingSystem.sln                    # expect: exit 0
dotnet build LicensingSystem.sln -warnaserror         # expect: exit 0, 0 warnings
dotnet test                                           # expect: exit 0, 0 failed, 0 skipped
grep -RIl "support-staff@vendor.com" LicensingAdmin/; test $? -eq 1     # expect: exit 0 (grep sale 1: sin coincidencia)
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1   # expect: exit 0 (git grep sale 1: sin coincidencia en archivos rastreados fuera del bundle)
```

No hay `test:e2e` ni comando de accesibilidad automatizado en este slice — se declara.

Cada expectativa de arriba es una propiedad, no un conteo, salvo "16 tags", que se cuenta del propio
step map de §9 y coincide en todos lados. Cada línea sale 0 en un build correcto; las dos líneas
`grep`/`git grep` afirman el código **1** específico (no un genérico "≠ 0"), de modo que un `2`/`128`
por archivo inexistente o error de uso falla la puerta en vez de satisfacerla (§9 reglas 3 y 5).

Más estas puertas manuales, cada una comprobada una vez antes de publicar:

- [ ] Cada paso de §9 tiene su tag de checkpoint en git: `test "$(git tag -l 'step-*' | wc -l)" -eq 16`.
      El repositorio de esos tags ya existe (baseline brownfield); §10 Bootstrap no lo crea, solo lo
      verifica idempotente.
- [ ] Cada archivo de la tabla *Files that must be committed* de §10 está presente en un checkout
      limpio: `git ls-files --error-unmatch <path>` sale 0 para cada uno (un path por invocación).
      Y el compañero no-ignorado por path: `git check-ignore -q <path>; test $? -eq 1` para cada uno
      (1 = no ignorado · 128 = uso, y ese sí falla).
- [ ] El `.gitignore` es preexistente y está en el commit del baseline
      (`git log --diff-filter=A --format=%H -- .gitignore` apunta a un commit anterior al paso 1);
      ningún paso de §9 lo introduce ni lo edita.
- [ ] La tabla *Byte-exact artifact reconciliation* de §19.6 es `NOT APPLICABLE` — no hay filas que
      confirmar.
- [ ] §10 Bootstrap se ha re-ejecutado una vez sobre un árbol ya inicializado, **salió 0**, y no
      cambió nada que importe: `rsync --ignore-existing` no pisó archivos y no se emite manifiesto
      bajo `workspace/`, así que no hay dependencias que revertir.
- [ ] Cada fila de la tabla *Cross-artifact value reconciliation* de §19.6 lee `Compared: yes`, y
      `dotnet build` / `dotnet test` se corrieron desde la raíz del repo con el bundle presente en
      `docs/blueprint/` sin efecto (ni `dotnet` ni MSBuild recorren `docs/`).
- [ ] §9.1 no aplica — nada que verificar.
- [ ] Todo Non-Goal de §1 sigue sin construir.
- [ ] Cada variable de §10 está puesta en producción y ausente del repo
      (`git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` → exit 1;
      `dotnet user-secrets list --project LicensingApi` y `--project LicensingAdmin` muestran la
      cadena; el entorno de prod tiene `ConnectionStrings__LicensingDb` y `Crypto__RsaPrivateKeyPem`).
- [ ] Pase manual del paso 14: `dotnet run --project LicensingAdmin` contra `172.16.101.12`, sembrar
      un `SuperAdmin` con `Admin__BootstrapEmail`/`Admin__BootstrapPassword`, iniciar sesión, emitir
      una licencia en `/licenses/new`, confirmar que la clave renderiza y casa la regex.
- [ ] Pase solo-teclado por login → `/licenses/new` → `/admin/users` (§15).
- [ ] No hay tracker de errores en este slice — el chequeo de "error de prueba" de la plantilla no
      aplica; se declara.
- [ ] Un rollback se ha ensayado una vez a propósito: `git reset --hard step-15-admin-users` en un
      clon desechable y `dotnet build` verde.

**Ningún warning se ignora.**

### 20.2 Risk register

| Risk | Likelihood | Impact | Early signal | Mitigation |
|---|---|---|---|---|
| El fallback RSA en memoria queda activo en prod (PEM no configurado) e invalida cada firma emitida al reiniciar | M | H | Warning "in-memory RSA fallback" en los logs de prod; firmas que dejan de verificar tras un reinicio | Puerta manual de §20.1 comprueba `Crypto__RsaPrivateKeyPem` en prod; `CryptoRegistration` loguea el warning en cada arranque sin PEM — Owner: operaciones |
| La clave privada RSA ahora vive también en el panel (radio de exposición mayor) | M | H | Auditoría de quién puede leer user-secrets/env del host del panel | §20.3 #2 registra el disparador de reversión (mover la emisión a un endpoint admin de la API) — Owner: arquitecto |
| `dotnet test --filter <X>` sale 0 si un typo del filtro no selecciona nada (puerta vacía) | M | M | Un `--filter` verde pero la suite total no crece | `LicensingSystem.Tests/.runsettings` con `<TreatNoTestsAsError>true</TreatNoTestsAsError>` referenciado por `<RunSettingsFilePath>` en el `.csproj` (paso 1 — la propiedad MSBuild `<VSTestTreatNoTestsAsError>` **no existe**); los pasos 14 y 16 corren `dotnet test` sin filtro; los tokens de `--filter` se eligen sin prefijo colisionante (`CryptoRegistration`, no `LicenseSignerDi`) — Owner: builder |
| El nodo `dev` tiene SDK 8.0.4xx **y** 10.0.4xx; sin pin, `dotnet` compila `net8.0` con analizadores de otra major → `dotnet build -warnaserror` del paso 16 puede fallar por warnings ajenos al código | M | M | Warnings nuevos al pasar de 8.x a 10.x SDK; `-warnaserror` rojo en el paso 16 sin cambio de código | `global.json` en la raíz (paso 1) fija SDK `8.0.4xx` con `rollForward: latestFeature`; §20.3 #8 registra la reversión — Owner: builder |
| `CanonicalBytes` con formato de fecha no determinista rompe la autenticidad offline (firma no verifica cross-máquina / tras BD) | M | H | `Verify` false para una licencia legítima tras recargar de Postgres o en otra zona horaria | Paso 5 normaliza a UTC y trunca a segundos (`yyyy-MM-ddTHH:mm:ss'Z'`); `LicenseFileService` tiene el mismo patrón sin normalizar pero está **congelado** (§20.4) — Owner: arquitecto |
| `PasswordHasher` del shared framework usa 100 000 iteraciones PBKDF2-HMAC-SHA512, < OWASP 2024 (≥210 000) | M | M | Auditoría de coste de hashing; fuerza bruta más barata de lo previsto | Paso 8 fija `PasswordHasherOptions.IterationCount = 210_000`; `PasswordHasherService` (paso 7) lo recibe por DI (§20.3 #10) — Owner: builder |
| `***REDACTED***` del baseline (`b877e55:LicensingApi/appsettings.json`) es recuperable del historial git — hallazgo ALTO, **MITIGADO** | M | H | `git show b877e55:...` sigue exponiendo el literal, pero ya no es una credencial válida | **Rotada el 2026-09-02** en el PostgreSQL `172.16.101.12` (`ALTER ROLE licensing WITH PASSWORD`); `***REDACTED***` está quemada, la exposición del historial queda inerte. No se purga historial (repo interno de LAN). Nueva credencial fuera de git (`dotnet user-secrets` / env). Falta: aplicar la nueva cadena en cualquier instancia desplegada — Owner: operaciones |
| `xunit.v3` 3.2.2 / runner 3.1.5 / `Mvc.Testing` 8.0.30 son relativamente nuevos; incompatibilidad con el SDK del nodo `dev` | L | M | `dotnet test` falla al descubrir o al arrancar el host de test en el paso 1 o el `WebApplicationFactory` no levanta en el paso 10 | El paso 1 es la primera puerta; si `xunit.v3` falla, se degrada a `xunit` v2 y se anota; `Mvc.Testing` 8.0.30 mantiene todas sus transitivas en 8.0.x — Owner: builder |
| `WebApplicationFactory<Program>` de un Blazor Server no produce el 302 esperado (el `RedirectToLogin` no dispara en SSR) | M | M | `AuthorizationPipelineTests` falla en la aserción de `Location` | El `RedirectToLogin` estándar de la plantilla navega vía `NavigationManager`, que en SSR lanza y el framework lo convierte en 302; si aun así falla, respaldar con `options.FallbackPolicy` + `MapRazorPages().RequireAuthorization()` y ajustar el test — Owner: builder |
| .NET 8 sale de soporte el 2026-11-10; la línea 8.0.x deja de recibir parches de seguridad | M | M | Avisos de fin de vida de .NET; CVEs sin parche en 8.0.x | Planificar el salto a net10.0 (LTS) en un slice posterior; este slice se mantiene en 8.0.x por ser brownfield — Owner: arquitecto |
| Alcance: el revisor pide "ya que estamos" añadir la pantalla de Reportes o el rate limiting | M | M | Un PR toca `ActivationController` o crea `Pages/Reports` | §1 Non-Goals es la valla; el builder para y reporta si un paso parece requerir una fila de esa tabla — Owner: arquitecto |

### 20.3 Decision log

| # | Decisión | Alternativa rechazada | Por qué | Se revertiría si |
|---|---|---|---|---|
| 1 | Auth admin = cookie auth local contra `admin_users` + `FallbackPolicy` | Windows/AD o un IdP externo (OIDC) | Host único interno del vendor, sin infraestructura de identidad; coste alto para el valor | El panel se abre a usuarios fuera del dominio del vendor, o aparece un requisito de SSO corporativo |
| 2 | Firmador compartido en `LicensingCore`, la emisión ocurre in-process en el panel | Un endpoint nuevo `POST /api/admin/licenses` en `LicensingApi` que sea el único con la clave | Ambas apps comparten host; duplicar el firmador evita un endpoint y su superficie de auth | El panel deja el host IIS único, o aparece un segundo host de panel → mover la emisión a la API y dejar la clave solo ahí |
| 3 | Primer admin por seed de arranque (`IHostedService`) parametrizado por config | Un comando `dotnet run -- seed-admin` o un `INSERT` SQL manual | Cero pasos manuales en el despliegue; idempotente y no-op sin las claves | El equipo quiere sembrar sin variables de entorno, o necesita sembrar varios admins de golpe |
| 4 | Mantener `001_initial_schema.sql` como SQL de referencia; sin migraciones EF | Introducir migraciones EF reales cableadas a `__EFMigrationsHistory` en este slice | El slice no cambia el esquema; migrar el mecanismo es trabajo aparte con su propio riesgo | Un slice necesita un cambio de esquema real |
| 5 | Login/logout como Razor Pages | Componentes Blazor | `HttpContext.SignInAsync` necesita el `HttpContext` crudo, inaccesible desde un componente | Blazor gane una API soportada de sign-in por cookie desde componente |
| 6 | Tests unitarios puros + un test de pipeline HTTP sin BD; integración de datos con Postgres diferida a `# manual:` | `Testcontainers` / `EntityFrameworkCore.InMemory` o una BD de test dedicada en CI | No hay CI; el valor de la integración de datos automatizada no justifica su coste en este slice; el `WebApplicationFactory` con cadena ficticia cubre el pipeline de auth sin BD | Se añade CI, o un incidente de datos muestra que los fakes ocultaron un bug de EF |
| 7 | `FallbackPolicy` que exige sesión en toda ruta salvo `[AllowAnonymous]`, además del `[Authorize]` por página | Solo `[Authorize]` por página | Fail-closed: si un builder olvida un atributo, la página degrada a "cualquier autenticado", no a anónimo; y el pipeline es testeable desde el paso 8 | Se necesite exponer rutas anónimas nuevas sin `[AllowAnonymous]` explícito (improbable) |
| 8 | `global.json` en la raíz fija el SDK a `8.0.4xx` (`rollForward: latestFeature`) | Sin `global.json` (como decía §2 inicialmente) | El nodo `dev` tiene 8.0.4xx y 10.0.4xx; sin pin `dotnet` elige el 10.x y compila `net8.0` con analizadores de otra major → riesgo en `dotnet build -warnaserror` del paso 16 | El equipo estandariza en un SDK más nuevo único y valida `net8.0` bajo él, o se migra el TFM |
| 9 | `CanonicalBytes` firma un payload **normalizado a UTC + truncado a segundos** con prefijo de dominio `licsig-v1|` | El formato `"O"` literal que decía el criterio 4 original | `"O"` varía por `DateTime.Kind` (offset de la máquina) y emite 7 dígitos fraccionarios que Postgres trunca a 6 → la firma dejaba de verificar cross-máquina y tras round-trip por BD; el prefijo evita confusión de tipos si algún formato evoluciona con la misma clave RSA | Se necesite precisión sub-segundo en la expiración (improbable para una licencia), o `LicenseFileService` deje de estar congelado y se unifique el formato |
| 10 | `PasswordHasherService` con `PasswordHasher<AdminUser>` inyectado; el paso 8 fija `IterationCount = 210_000` | `_inner = new()` en field-init (100k iter, no configurable por DI) | OWASP 2024 pide ≥210 000 para PBKDF2-HMAC-SHA512; inyectar deja el coste bajo control del app sin salirse del alcance de E1-T7 | Se adopte Argon2id (paquete nuevo), o el panel gane rate-limiting/lockout que permita bajar el coste |
| 11 | Riesgo aceptado: la firma de `License` no cubre `IssuedAtUtc` ni `Status`/revocación | Firmar también `IssuedAtUtc` (+ opcional `MaxOfflineDays`) | Diseño "snapshot de emisión": la revocación es 100% online (`/checkin` consulta `licenses.Status` en BD); una licencia perpetua no necesita ancla temporal firmada en este slice | Aparezca un requisito de validación offline con caducidad de confianza, o de revocación que funcione sin conexión |
| 12 | `AddLicenseSigner` (paso 6) **fail-closea** fuera de Development: sin `Crypto:RsaPrivateKeyPem` lanza; PEM malformado/solo-público/<2048 lanza en el registro | Replicar el fallback laxo "solo warning" de `LicensingApi/Program.cs` (lo que decía el blueprint original) | Un admin que emite licencias firmadas con una clave efímera que muere en cada reinicio es un problema de integridad, no una comodidad de dev; el auditor lo escalaría a ALTA si se aceptara. Consistente con `ConnectionStringGuard` | El equipo estandarice un mecanismo de arranque distinto para el material de clave (Key Vault, PFX en el host) que ya garantice presencia |
| 13 | `AccessDeniedPath = "/Account/AccessDenied"` (página propia, HTTP 200 "sin permiso"); `<NotAuthorized>` muestra mensaje si el usuario está autenticado, redirige solo si es anónimo | Ambos paths a `/Account/Login` (lo que decía el blueprint original) | Un `ReadOnlyViewer` autenticado que abre `/pending-review` (con `[Authorize(Policy=ReviewAccess)]` desde E2-T4) entraría en bucle login↔returnUrl | Nunca — es el patrón correcto de ASP.NET Core; el original era un error |
| 14 | Cookie: `SecurePolicy = IsDevelopment() ? SameAsRequest : Always`, `SameSite = Lax` explícito, `UseHttpsRedirection` en prod, `ExpireTimeSpan = 8h`, `OnValidatePrincipal` que revalida `AdminUser` (existe+activo+rol) | `SameAsRequest` fijo + solo `SlidingExpiration` (lo que decía el blueprint original) | `UseHsts()` ya asume HTTPS en prod; `Always` no cuesta nada tras TLS y evita fuga de cookie por http; sin `ExpireTimeSpan` + sliding, un admin desactivado conserva sesión hasta 14 días | La prod del host IIS resulta ser **HTTP puro** (entonces `UseHsts()` también estaría mal y hay que revisar ambos), o aparece un requisito de sesiones más largas |
| 15 | `AdminUser.Email` se **normaliza en la escritura** (minúsculas + `Trim()`) — el seeder (paso 11) lo hace en `BuildSuperAdmin` y en la ruta de seed; `AdminUserService` (paso 15) hará lo mismo. `EfAdminUserLookup` sigue comparando `u.Email.ToLower() == normalized` en este slice | Índice funcional `lower("Email")` vía migración, o dejar el desajuste de mayúsculas | El índice único de `admin_users.Email` es case-sensitive; normalizar en la escritura da unicidad efectiva case-insensitive sin migración (Non-Goal §1). El predicado no-sargable es un seq scan barato en un panel interno de bajo volumen | Se añade CI/carga que haga notar el seq scan por request autenticada → predicado `u.Email == normalized` + índice; o entran migraciones EF (§20.4) |
| 16 | Riesgo aceptado en este slice: `AdminRole.SuperAdmin = 0` (== `default(AdminRole)`) y `AdminUser.Role` se materializa sin `Enum.IsDefined` | Reordenar `Enums.cs` a `ReadOnlyViewer = 0` + `Enum.IsDefined` al leer la fila | La columna usa `HasConversion<string>()` (no guarda el ordinal) y toda ruta de creación fija `Role` explícitamente (seeder → `SuperAdmin` a propósito; `AdminUserService` lo pedirá); el hueco `(AdminRole)99` sólo da un principal con `FallbackPolicy`. Endurecerlo toca `LicensingCore/Entities/Enums.cs`, fuera de los `files` de E2-T1/E2-T3 | Slice de hardening (§20.4 #9); o una inserción de `AdminUser` sin fijar `Role` entra en el código |
| 17 | `LicenseIssuanceRequest` reusa `int MaxActivations` para `SoftwareProduct.DefaultMaxActivations` del producto nuevo | Un campo separado `int? NewProductDefaultMaxActivations` (como listaba el paso 13) | El default del producto = el tope de la primera licencia emitida es un valor de partida razonable y ahorra un campo del formulario de E2-T6; siempre editable luego en el producto | Se necesite emitir la primera licencia de un producto con un tope distinto del default del producto |

### 20.4 What to build next

Del §1 Non-Goals y de los hallazgos diferidos del build, en orden:

1. **Rate limiting en `/api/activate` y `/api/checkin`** — disparador: antes de exponer la API a
   internet (sigue como `TODO` en `ActivationController`).
2. **Normalizar el formato de fecha en `LicensingApi/Services/LicenseFileService.cs`** (mismo patrón
   `"O"` sin normalizar que se arregló en `LicenseSigner`) — disparador: cuando el contrato del
   archivo de activación con el DLL pueda versionarse; hoy está **congelado** (§5).
3. **Aplicar el fail-closed de material de clave a `LicensingApi`** (`LicenseFileService` — `Crypto:RsaPrivateKeyPem`
   / `Crypto:AesKeyBase64`, hoy fallback en memoria "solo warning" en su `Program.cs` preexistente),
   igual que el `AddLicenseSigner` del paso 6 hace en el admin (§20.3 #12) — disparador: slice de
   hardening del host, o antes de exponer la API.
4. **Migraciones EF Core reales** que reemplacen `001_initial_schema.sql` — disparador: el primer
   slice que necesite un cambio de esquema.
5. **Pantalla de Reportes / exportación PDF·XLS** — disparador: compliance pide exportaciones.
6. **Pantalla de Notification Settings (config SMTP)** — disparador: se priorizan las notificaciones
   por email.
7. **Desacoplar `LicensingAdmin` → llamadas a endpoints admin de `LicensingApi`** — disparador:
   admin y API se separan en hosts distintos (ver §20.3 #2).
8. **Predicado de email sargable + DRY de `EfAdminUserLookup`** — cuando el seed normalizado (§20.3
   #15) esté en `main`: cambiar `EfAdminUserLookup.FindByEmailAsync` a `u.Email == normalized`
   (usa el índice único), y hacer que `Login.cshtml.cs::RecordLoginAsync` llame a
   `IAdminUserLookup.FindByEmailAsync` en vez de reimplementar la consulta (3er call site del
   patrón interino) y fije `Id = Guid.NewGuid()` en su `AuditLogEntry` como el resto del repo —
   disparador: se añade CI/carga, o el siguiente slice que toque auth.
9. **`AdminRole` reordenado (`ReadOnlyViewer = 0`) + `Enum.IsDefined` al materializar la fila**
   (`LicensingCore/Entities/Enums.cs`) — cierra §20.3 #16 — disparador: slice de hardening, o
   antes de exponer la creación de admins a más de una persona de confianza.
10. **Lockout de login en memoria + fila de auditoría `LoginFailed`** — `IMemoryCache` por email
    (p. ej. 5 fallos / 15 min) en el POST de `/Account/Login` + `AuditLogEntry`
    `Action="LoginFailed"` con `Actor` = email normalizado (sin cambio de esquema). El
    rate-limiting de middleware sigue Non-Goal (§1). Disparador: el panel gana exposición fuera de
    la LAN de confianza, o auditoría/compliance pide traza de intentos fallidos.
11. **Middleware de cabeceras de seguridad app-wide** — `X-Frame-Options: DENY` / CSP
    `frame-ancestors 'none'`, `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` en
    `LicensingAdmin/Program.cs`. La página de login es un documento `Layout = null` independiente
    → hoy embebible en iframe (clickjacking del formulario). Disparador: slice de hardening del
    host, o antes de exponer el panel fuera de la LAN.
12. **Endurecer `EfLicenseStore.AddAsync` y la validación del formulario de emisión** — capturar
    `DbUpdateException` con `SqlState == "23505"` del índice `ux_licenses_license_key` → excepción
    de dominio tipada (para reintento/error limpio en vez de que suba cruda al error boundary);
    `Trim()` + acotar longitud de `CustomerEmail` (≤320) / `CustomerName` (≤200) antes de EF.
    (E2-T6 ya impone el mínimo de 1 en `MaxActivations` en `New.razor`.) Disparador: la pantalla
    de emisión entra en uso real, o el siguiente slice que toque `Licensing/`.

---

*Fin del blueprint. El orden de build es §9. Parar cuando §20.1 esté en verde.*
