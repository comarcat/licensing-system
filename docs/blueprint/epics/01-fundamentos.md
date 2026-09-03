# Epic 01: Cripto, secretos y backbone de auth

> Después de este epic existen: el proyecto de tests `LicensingSystem.Tests` en la solución, cero
> credenciales en texto plano en el repo con un guardián de arranque, el generador de claves y el
> firmador de licencias en `LicensingCore`, el servicio de hashing de contraseñas, y el cableado de
> cookie auth + políticas + política de fallback + shell de autorización en `LicensingAdmin` — sin
> ninguna pantalla todavía protegida por su propio `[Authorize]` ni ningún login funcional.

| | |
|---|---|
| **Epic id** | `01-fundamentos` |
| **Tasks** | `E1-T1` … `E1-T8` |
| **Depends on** | nada — empieza aquí |
| **Unlocks** | `02-auth-y-licencias` |
| **Parallel with** | nada (epic único de la fase de fundamentos) |

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
| Pruebas (toda la suite) | `dotnet test` |
| Pruebas (una clase) | `dotnet test --filter <NombreClase>` |
| Secreto local | `dotnet user-secrets set --project <proj> "<clave>" "<valor>"` |

**Gate:** `dotnet build LicensingSystem.sln && dotnet test` pasa antes de marcar cualquier tarea
como hecha.

Ningún `Verify` de este epic necesita un servicio en ejecución: los tests son unitarios puros, sin
BD, sin Docker, sin arrancar los web hosts. No hay servicio local que levantar.

## Directory subtree

Solo lo que este epic toca:

```
global.json                              # NUEVO (T1, raíz): pin SDK 8.0.4xx, rollForward latestFeature
LicensingSystem.sln                       # EDIT (T1): añade el proyecto de tests
LicensingApi/
  LicensingApi.csproj                     # EDIT (T1): <UserSecretsId>
  appsettings.json                        # EDIT (T3): ConnectionStrings:LicensingDb -> ""
  Program.cs                              # EDIT (T2): envuelve la cadena con ConnectionStringGuard.Require
  README.md                               # EDIT (T3): user-secrets / env var — placeholder, sin contraseña real
LicensingCore/
  LicensingCore.csproj                    # EXISTE (read-only)
  Configuration/ConnectionStringGuard.cs  # NUEVO (T2)
  Licensing/LicenseKeyGenerator.cs        # NUEVO (T4)
  Crypto/ILicenseSigner.cs                # NUEVO (T5)
  Crypto/LicenseSigner.cs                 # NUEVO (T5)
  Entities/*.cs                           # EXISTE (read-only) — License, SoftwareProduct, AdminUser, AuditLogEntry, Enums
  Data/AppDbContext.cs                    # EXISTE (read-only) — expone DbSet<> con nombre: SoftwareProducts, Licenses, Activations, AdminUsers, AuditLogEntries, NotificationConfigs
LicensingAdmin/
  LicensingAdmin.csproj                   # EDIT (T1): <UserSecretsId>
  appsettings.json                        # EDIT (T3): ConnectionStrings:LicensingDb -> ""
  Program.cs                              # EDIT (T2 guardián, T6 firmador DI, T8 cookie auth + políticas + FallbackPolicy)
  App.razor                              # EDIT (T8): CascadingAuthenticationState + AuthorizeRouteView + RedirectToLogin
  _Imports.razor                          # EDIT (T8): @using Microsoft.AspNetCore.Authorization / .Components.Authorization
  Auth/PasswordHasherService.cs           # NUEVO (T7)
  Auth/CryptoRegistration.cs              # NUEVO (T6)
  Auth/AuthPolicies.cs                    # NUEVO (T8)
LicensingSystem.Tests/                    # NUEVO (T1) — Microsoft.NET.Sdk, net8.0, xUnit v3, OutputType=Exe, IsPackable=false, RunSettingsFilePath
  LicensingSystem.Tests.csproj
  .runsettings                            # NUEVO (T1) — TreatNoTestsAsError true (backstop del falso-verde de --filter)
  SmokeTests.cs                           # NUEVO (T1)
  ConnectionStringGuardTests.cs           # NUEVO (T2)
  LicenseKeyGeneratorTests.cs             # NUEVO (T4)
  LicenseSignerTests.cs                   # NUEVO (T5)
  CryptoRegistrationTests.cs              # NUEVO (T6) — clase CryptoRegistrationTests, filtro --filter CryptoRegistration
  PasswordHasherServiceTests.cs           # NUEVO (T7)
  AuthPoliciesTests.cs                    # NUEVO (T8)
```

Todo fuera de este subárbol está fuera de alcance. Si una tarea parece requerir editar un archivo no
listado, para y reporta — significa que el límite del epic está mal.

## Data model touched here

Ningún cambio de esquema. T2–T8 son lógica pura y de configuración; sus tests usan objetos
construidos a mano. Ninguna tarea de este epic lee ni escribe filas.

| Entity | Campos que este epic usa | Notas |
|---|---|---|
| `License` | `LicenseKey`, `ProductId`, `ModelSnapshot`, `MaxActivations`, `SubscriptionExpiryUtc`, `Signature` | T5 firma el payload canónico sobre estos campos; construidos a mano en los tests |
| `AdminUser` | `Email`, `PasswordHash`, `Role`, `IsActive` | T7/T8 solo los referencian por tipo; sin acceso a BD |
| Enums | `LicenseModel [Flags]` (None=0, Machine=1, User=2, Floating=4, Subscription=8), `AdminRole` (SuperAdmin=0, SupportStaff=1, ReadOnlyViewer=2) | T5 usa `(int)ModelSnapshot`; T8 mapea rol→política |

## Contracts

**Consumido** — ya existe, no lo reconstruyas:

| De | Interfaz | Garantía |
|---|---|---|
| repo (preexistente) | `LicensingApi/Services/ActivationService.cs` regex `LicenseKeyFormat()` | `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$` — el generador de T4 se ajusta a esto; nunca se edita `ActivationService.cs` |
| repo (preexistente) | `LicensingCore/Data/AppDbContext.cs` | expone `DbSet<>` con nombre: `SoftwareProducts`, `Licenses`, `Activations`, `AdminUsers`, `AuditLogEntries`, `NotificationConfigs` |
| repo (preexistente) | `LicensingApi/Program.cs` patrón de fallback cripto | `RSA.Create(2048)` cuando el PEM está vacío; T6 lo replica en el panel con un warning por `ILogger` |
| .NET 8 shared framework | `Microsoft.AspNetCore.Identity.PasswordHasher<AdminUser>` | hashing PBKDF2; sin pin de paquete |
| .NET 8 shared framework | `Microsoft.AspNetCore.Authentication.Cookies` | esquema `CookieAuthenticationDefaults.AuthenticationScheme`; sin pin |

**Producido** — epics posteriores dependen de estas firmas exactas. Cambiar una las rompe:

| Export | Firma | Usado por |
|---|---|---|
| `LicensingCore/Configuration/ConnectionStringGuard.cs` → `Require` | `static string Require(string? value)` — lanza `InvalidOperationException` si vacía/espacios | ambos `Program.cs` |
| `LicensingCore/Licensing/LicenseKeyGenerator.cs` → `NewKey` | `static string NewKey()` — cumple `LicenseKeyFormat()` | `02` T5 (emisión) |
| `LicensingCore/Crypto/ILicenseSigner.cs` | `byte[] Sign(License)` · `bool Verify(License, byte[], RSA)` · `static byte[] CanonicalBytes(License)` | `02` T5 (emisión), `01` T6 (DI) |
| `LicensingAdmin/Auth/PasswordHasherService.cs` | `string Hash(string)` · `bool Verify(string hash, string password)` | `02` T1 (credenciales), T3 (seeder), T7 (admin users) |
| `LicensingAdmin/Auth/AuthPolicies.cs` | constantes `ViewerAccess`, `ReviewAccess`, `IssueAccess`, `AdminUserAccess` + registro + `FallbackPolicy` | `02` T2 (pipeline), T4, T6, T7 |
| `LicensingAdmin/Auth/CryptoRegistration.cs` → `AddLicenseSigner` | `static IServiceCollection AddLicenseSigner(this IServiceCollection, IConfiguration)` | `02` T5 (resuelve `ILicenseSigner`) |

## Conventions that bite in this area

- **`dotnet build` ES la puerta de lint/typecheck.** No hay `dotnet lint` ni `dotnet typecheck` en
  este repo. T1/T8 usan `dotnet build`; el paso de cierre del epic 02 usa `-warnaserror`.
- **Todo tipo bajo prueba es `public`.** Sin `InternalsVisibleTo`. `CanonicalBytes` es `public
  static` a propósito para que `LicenseSignerTests` lo aserte.
- **El proyecto de tests compila como `Exe`** (`<OutputType>Exe</OutputType>`) — xUnit v3 lo
  requiere. Lleva `<FrameworkReference Include="Microsoft.AspNetCore.App" />` para usar
  `PasswordHasher<T>`, autorización, `ClaimsPrincipal` y `WebApplicationFactory<Program>` directo.
- **`LicensingSystem.Tests/.runsettings` con `TreatNoTestsAsError` true** (referenciado por `RunSettingsFilePath` en el `.csproj`): un `--filter`
  con typo que no selecciona nada debe fallar, no pasar en vacío.
- **El guardián del arranque no rompe puertas anteriores.** `dotnet build`/`dotnet test` nunca
  arrancan los web hosts (la suite del epic 01 no usa `WebApplicationFactory`), así que
  `ConnectionStringGuard.Require` no se dispara durante el build.
- **El firmador del registro `License` es distinto** del archivo de licencia por activación de
  `LicensingApi/Services/LicenseFileService.cs`. No se toca `LicenseFileService`.
- **Orden de middleware y política de fallback en `Program.cs` (T8):**
  `app.UseAuthentication(); app.UseAuthorization();` van **entre** `app.UseRouting()` y
  `app.MapBlazorHub()`; `AddAuthorization` fija `FallbackPolicy = RequireAuthenticatedUser()` para
  que toda ruta exija sesión salvo las marcadas `[AllowAnonymous]`.
- **Las impl. EF usan los `DbSet<>` con nombre de `AppDbContext`** (o `db.Set<T>()`, indistinto —
  ambos existen y funcionan).

Reglas completas del proyecto: `CLAUDE.md`. Reglas de área: `.claude/rules/entities.md`
(`LicensingCore/**`), `.claude/rules/admin-ui.md` (`LicensingAdmin/**`). Ambas en la raíz del repo —
el builder las copió ahí desde `docs/blueprint/workspace/` antes de la tarea 1.

---

## Tasks

Listadas en el mismo orden que `tasks.json`. Ese orden es el orden de build — trabaja de arriba
abajo y no re-ordenes por prioridad ni por lo que parezca rápido.

### `E1-T1` — Scaffold LicensingSystem.Tests y wiring en la solucion

**Depends on:** nada · **Priority:** p0 — metadata para recortes de alcance, no orden de ejecución

Tarea de andamiaje: 7 archivos de proyecto/config, **cero lógica** — el tope de "≤5 archivos" no
aplica aquí (aprobado por el Arquitecto, enmienda 2026-09-02). Crea el proyecto de tests como
`Microsoft.NET.Sdk` (no Web), `net8.0`, `<Nullable>enable</Nullable>`, `<IsPackable>false</IsPackable>`,
`<OutputType>Exe</OutputType>`,
`<RunSettingsFilePath>$(MSBuildThisFileDirectory).runsettings</RunSettingsFilePath>`,
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`, los 5 `PackageReference` de §11
Development del blueprint (incluye `Microsoft.AspNetCore.Mvc.Testing`), y `ProjectReference` a
`LicensingCore` y `LicensingAdmin`. **No** uses `<VSTestTreatNoTestsAsError>` — no existe como
propiedad MSBuild (inerte); el backstop real es el `.runsettings`. Crea también
`LicensingSystem.Tests/.runsettings` con `<RunConfiguration><TreatNoTestsAsError>true</TreatNoTestsAsError></RunConfiguration>`
y un `global.json` en la raíz (`{ "sdk": { "version": "8.0.4xx-presente-en-dev", "rollForward":
"latestFeature" } }`) para que `dotnet` no elija el SDK 10.x del nodo `dev`. Una sola prueba en
`SmokeTests.cs`. `dotnet sln LicensingSystem.sln add`. Añade un `<UserSecretsId>` (un GUID) a los
dos `.csproj` web. No fijes ninguna versión de paquete aquí — usa las de §11.

**Files**
- `LicensingSystem.Tests/LicensingSystem.Tests.csproj` — nuevo
- `LicensingSystem.Tests/.runsettings` — nuevo: `TreatNoTestsAsError` true
- `LicensingSystem.Tests/SmokeTests.cs` — nuevo: `[Fact] public void Smoke() => Assert.True(true);`
- `global.json` — nuevo (raíz): pin SDK `8.0.4xx`, `rollForward: latestFeature`
- `LicensingSystem.sln` — edit: `dotnet sln add`
- `LicensingApi/LicensingApi.csproj` — edit: `<UserSecretsId>`
- `LicensingAdmin/LicensingAdmin.csproj` — edit: `<UserSecretsId>`

**Acceptance**

1. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0 with `LicensingSystem.Tests` present as a fourth project in the solution.
2. **WHEN** `dotnet test` runs from the repo root **THE SYSTEM SHALL** report 1 passed, 0 failed, 0 skipped.
3. **WHEN** `LicensingApi.csproj` and `LicensingAdmin.csproj` are inspected **THE SYSTEM SHALL** each contain a non-empty `<UserSecretsId>` element.
4. **WHEN** `LicensingSystem.Tests.csproj` is inspected **THE SYSTEM SHALL** set `<IsPackable>false</IsPackable>`, reference exactly the five pinned test packages (Microsoft.NET.Test.Sdk, xunit.v3, xunit.runner.visualstudio, coverlet.collector, Microsoft.AspNetCore.Mvc.Testing) plus project references to `LicensingCore` and `LicensingAdmin`, and point `<RunSettingsFilePath>` at a committed `LicensingSystem.Tests/.runsettings`.
5. **WHEN** `LicensingSystem.Tests/.runsettings` is inspected **THE SYSTEM SHALL** set `<TreatNoTestsAsError>true</TreatNoTestsAsError>` so that a `dotnet test --filter <nonexistent>` exits non-zero instead of a false green.
6. **WHEN** `dotnet --version` runs at the repo root **THE SYSTEM SHALL** report an `8.0.4xx` SDK, selected by a committed repo-root `global.json` with `rollForward: latestFeature`.

**Verify**

```bash
dotnet build LicensingSystem.sln
dotnet test
grep -q 'TreatNoTestsAsError' LicensingSystem.Tests/.runsettings && grep -q 'RunSettingsFilePath' LicensingSystem.Tests/LicensingSystem.Tests.csproj
dotnet --version | grep -q '^8[.]0[.]'
```

**Checkpoint**

```bash
git add -A && git commit -m "step 1: tests-scaffold"
git tag step-01-tests-scaffold
```

### `E1-T2` — T0a: guardian de cadena de conexion

**Depends on:** E1-T1 · **Priority:** p0

`ConnectionStringGuard.Require(string?)` lanza `InvalidOperationException` con un mensaje que nombra
`dotnet user-secrets` y `ConnectionStrings__LicensingDb` cuando el valor es `null`, vacío o espacios;
en otro caso devuelve el valor tal cual. Llámalo en ambos `Program.cs` envolviendo
`builder.Configuration.GetConnectionString("LicensingDb")` antes de construir las opciones del
`DbContext`. Esta tarea **no** toca `appsettings.json` ni los README — eso es T3.

**Files**
- `LicensingCore/Configuration/ConnectionStringGuard.cs` — nuevo
- `LicensingSystem.Tests/ConnectionStringGuardTests.cs` — nuevo: clase `ConnectionStringGuardTests`
- `LicensingApi/Program.cs` — edit: envuelve la lectura de la cadena
- `LicensingAdmin/Program.cs` — edit: envuelve la lectura de la cadena

**Acceptance**

1. **WHEN** `ConnectionStringGuard.Require(null)`, `Require("")`, or `Require("   ")` is called **THE SYSTEM SHALL** throw `InvalidOperationException` whose message names both `dotnet user-secrets` and the `ConnectionStrings__LicensingDb` environment variable.
2. **WHEN** `ConnectionStringGuard.Require("Host=db;Database=x")` is called with a non-blank value **THE SYSTEM SHALL** return that exact string unchanged.
3. **WHEN** `dotnet test --filter ConnectionStringGuard` runs **THE SYSTEM SHALL** report all `ConnectionStringGuard` tests passed, 0 failed.
4. **WHEN** either `LicensingApi/Program.cs` or `LicensingAdmin/Program.cs` reads the `LicensingDb` connection string **THE SYSTEM SHALL** pass it through `ConnectionStringGuard.Require(...)` before constructing the `DbContext` options.
5. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0.

**Verify**

```bash
dotnet build LicensingSystem.sln
dotnet test --filter ConnectionStringGuard
```

**Checkpoint**

```bash
git add -A && git commit -m "step 2: conn-guard"
git tag step-02-conn-guard
```

### `E1-T3` — T0b: sacar la credencial en texto plano del repo

**Depends on:** E1-T2 · **Priority:** p0

Pon `"LicensingDb": ""` en `LicensingApi/appsettings.json` y `LicensingAdmin/appsettings.json`
(quita `Host=...;Password=***`). En `LicensingSystem_README.md` y `LicensingApi/README.md`
reescribe **toda** aparición de la cadena de conexión con contraseña real —el bloque de ejemplo y
el comando `dotnet user-secrets set`— a un placeholder
`Host=<host>;Port=5432;Database=licensing_app;Username=<user>;Password=<password>`, y quita cualquier
frase tipo "está bien para local". No debe quedar ningún `Password=***REDACTED***` en ningún archivo
rastreado fuera de `docs/blueprint/`.

**Files**
- `LicensingApi/appsettings.json` — edit: `LicensingDb: ""`
- `LicensingAdmin/appsettings.json` — edit: `LicensingDb: ""`
- `LicensingSystem_README.md` — edit: placeholder de cadena de conexión, sin contraseña real
- `LicensingApi/README.md` — edit: placeholder de cadena de conexión, sin contraseña real

**Acceptance**

1. **WHEN** `LicensingApi/appsettings.json` and `LicensingAdmin/appsettings.json` are inspected **THE SYSTEM SHALL** each set `ConnectionStrings:LicensingDb` to the empty string.
2. **WHEN** `grep -RIl "Password=" LicensingApi/appsettings.json LicensingAdmin/appsettings.json` runs **THE SYSTEM SHALL** find no match and exit 1.
3. **WHEN** `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` runs **THE SYSTEM SHALL** find no match and exit 1.
4. **WHEN** `LicensingSystem_README.md` and `LicensingApi/README.md` are inspected **THE SYSTEM SHALL** show the connection string only as a placeholder with no real password value.
5. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0.

**Verify**

```bash
grep -RIl "Password=" LicensingApi/appsettings.json LicensingAdmin/appsettings.json; test $? -eq 1
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1
dotnet build LicensingSystem.sln
```

**Checkpoint**

```bash
git add -A && git commit -m "step 3: secret-scrub"
git tag step-03-secret-scrub
```

### `E1-T4` — Generador de clave de licencia (LicensingCore)

**Depends on:** E1-T1 · **Priority:** p0

`LicenseKeyGenerator.NewKey()`: caracteres cripto-aleatorios de `RandomNumberGenerator` sobre
`ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789`, segmentos 4-5-4-4-4-4-2 unidos por `-`. El test genera 10000
claves, comprueba la regex (copiada literal) y que las 10000 sean distintas, más una prueba de
charset. No uses `Random`; usa `RandomNumberGenerator`.

**Files**
- `LicensingCore/Licensing/LicenseKeyGenerator.cs` — nuevo
- `LicensingSystem.Tests/LicenseKeyGeneratorTests.cs` — nuevo: clase `LicenseKeyGeneratorTests`

**Acceptance**

1. **WHEN** `LicenseKeyGenerator.NewKey()` is called **THE SYSTEM SHALL** return a string matching `^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$`.
2. **WHEN** 10000 keys are generated in one test run **THE SYSTEM SHALL** produce 10000 distinct values with 0 collisions.
3. **WHEN** any generated key is inspected **THE SYSTEM SHALL** contain only characters from `A`-`Z` and `0`-`9` plus the segment separator `-`.
4. **WHEN** `dotnet test --filter LicenseKeyGenerator` runs **THE SYSTEM SHALL** report all `LicenseKeyGenerator` tests passed, 0 failed.

**Verify**

```bash
dotnet test --filter LicenseKeyGenerator
```

**Checkpoint**

```bash
git add -A && git commit -m "step 4: license-key-generator"
git tag step-04-license-key-generator
```

### `E1-T5` — Firmador de licencia (LicensingCore)

**Depends on:** E1-T4 · **Priority:** p0

`ILicenseSigner` + `LicenseSigner(RSA signingKey)`. Firma RSA-SHA256, `RSASignaturePadding.Pkcs1`.
Expón `public static byte[] CanonicalBytes(License)`. **Payload canónico** = bytes UTF-8 (sin BOM) de
`licsig-v1|{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{expiry}` con
`FormattableString.Invariant`:
- prefijo `licsig-v1|` = separación de dominio (la misma clave RSA no debe poder confundir una firma
  de `LicenseSigner` con una de `LicenseFileService`).
- `{expiry}` = `""` si `SubscriptionExpiryUtc` es `null`; en otro caso **normalizado a UTC**
  (`Local`→`ToUniversalTime()`, `Unspecified`→`SpecifyKind(v, Utc)`), **truncado a segundos**,
  formateado `yyyy-MM-ddTHH:mm:ss'Z'` con `CultureInfo.InvariantCulture`. `"O"` variaba por `Kind` y
  por el truncado µs→6 de Postgres → la firma dejaba de verificar cross-máquina y tras round-trip por BD.
`Verify` **blinda entradas**: `ArgumentNullException.ThrowIfNull(license/publicKey)`,
`signature is null or {Length:0}` → `false`, `catch (CryptographicException)` → `false`. El test hace
round-trip, tampering de cada campo, clave/firma inválida, `CanonicalBytes` byte-exacto, y el **mismo
instante como `Unspecified`/`Utc`/`Local` y tras truncar sub-segundo → misma firma, `Verify` true**.

**Files**
- `LicensingCore/Crypto/ILicenseSigner.cs` — nuevo
- `LicensingCore/Crypto/LicenseSigner.cs` — nuevo
- `LicensingSystem.Tests/LicenseSignerTests.cs` — nuevo: clase `LicenseSignerTests`

**Acceptance**

1. **WHEN** `LicenseSigner.Sign(license)` output is passed to `Verify(license, signature, publicKey)` with the matching key **THE SYSTEM SHALL** return true.
2. **WHEN** any one canonical field (`LicenseKey`, `ProductId`, `ModelSnapshot`, `MaxActivations`, `SubscriptionExpiryUtc`) is changed and `Verify` is re-run with the original signature **THE SYSTEM SHALL** return false.
3. **WHEN** `Verify` is called with a different RSA public key, a null `signature`, a zero-length `signature`, or a malformed `signature` **THE SYSTEM SHALL** return false without throwing; **WHEN** `license` or `publicKey` is null **THE SYSTEM SHALL** throw `ArgumentNullException`.
4. **WHEN** `LicenseSigner.CanonicalBytes(license)` is decoded as UTF-8 **THE SYSTEM SHALL** equal `licsig-v1|{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{expiry}`, where `{expiry}` is the empty string when `SubscriptionExpiryUtc` is null and otherwise the value normalized to UTC (`Local` via `ToUniversalTime()`, `Unspecified` treated as UTC), truncated to whole seconds, formatted `yyyy-MM-ddTHH:mm:ss'Z'` with `CultureInfo.InvariantCulture`.
5. **WHEN** the same instant is signed as `DateTimeKind.Unspecified`, as `Utc`, as `Local`, and after a round-trip that drops sub-second precision **THE SYSTEM SHALL** produce the same signature and `Verify` **SHALL** return true in every case.
6. **WHEN** `dotnet test --filter LicenseSigner` runs **THE SYSTEM SHALL** report all `LicenseSigner` tests passed, 0 failed.

**Verify**

```bash
dotnet test --filter LicenseSigner
```

**Checkpoint**

```bash
git add -A && git commit -m "step 5: license-signer"
git tag step-05-license-signer
```

### `E1-T6` — Cablear firmador + config cripto en LicensingAdmin

**Depends on:** E1-T5 · **Priority:** p0

`CryptoRegistration.AddLicenseSigner(this IServiceCollection, IConfiguration)`: lee
`config["Crypto:RsaPrivateKeyPem"]`; si no vacío → `RSA.Create()` + `ImportFromPem`; si vacío →
`RSA.Create(2048)` + warning por `ILogger` (dev-only). Registra `ILicenseSigner` singleton. Llámalo
una vez en `Program.cs`. Extrae el registro a `CryptoRegistration.cs` justamente para poder testearlo.
La clase de test es `CryptoRegistrationTests` con **nombres de método normales** — **no** el prefijo
`LicenseSignerDi_` (colisiona con `--filter LicenseSigner` del paso 5 por ser substring de FQN). La
puerta es `--filter CryptoRegistration`.

**Files**
- `LicensingAdmin/Program.cs` — edit: `builder.Services.AddLicenseSigner(builder.Configuration)`
- `LicensingAdmin/Auth/CryptoRegistration.cs` — nuevo
- `LicensingSystem.Tests/CryptoRegistrationTests.cs` — nuevo: clase `CryptoRegistrationTests`

**Acceptance**

1. **WHEN** `AddLicenseSigner(services, config)` runs with `Crypto:RsaPrivateKeyPem` set to a valid PEM **THE SYSTEM SHALL** register `ILicenseSigner` such that `GetRequiredService<ILicenseSigner>()` resolves without throwing.
2. **WHEN** `AddLicenseSigner(services, config)` runs with no `Crypto:RsaPrivateKeyPem` configured **THE SYSTEM SHALL** still resolve `ILicenseSigner` using an in-memory RSA key and **SHALL** log one warning.
3. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0.
4. **WHEN** `LicensingAdmin/Program.cs` is inspected **THE SYSTEM SHALL** call `AddLicenseSigner(...)` exactly once.
5. **WHEN** `dotnet test --filter CryptoRegistration` runs **THE SYSTEM SHALL** report all `CryptoRegistrationTests` tests passed, 0 failed (the filter token contains no substring that also selects `LicenseSignerTests`).

**Verify**

```bash
dotnet build LicensingSystem.sln
dotnet test --filter CryptoRegistration
```

**Checkpoint**

```bash
git add -A && git commit -m "step 6: signer-di"
git tag step-06-signer-di
```

### `E1-T7` — Servicio de hashing de contrasenas

**Depends on:** E1-T1 · **Priority:** p0

`public sealed class PasswordHasherService` con **constructor que recibe un `PasswordHasher<AdminUser>`
inyectado** (alimentado por `IOptions<PasswordHasherOptions>` de DI) — **no** `_inner = new()` en
field-init, para que el paso 8 pueda subir el `IterationCount` (100k por defecto < OWASP 2024 210k
para SHA512). `string Hash(string)`; `bool Verify(string hash, string password)` **empieza con**
`if (string.IsNullOrEmpty(hash) || password is null) return false;`, devuelve `false` en `Failed`,
`catch (FormatException) => false` — **nunca lanza** (un campo de formulario Blazor puede llegar
`null`). El test cubre round-trip, contraseña equivocada, salt por hash, `Verify("garbage","x")` →
false, `Verify(null,"x")` / `Verify("","x")` / `Verify(hash,null)` → false sin excepción, y que el
ctor acepta un `PasswordHasher<AdminUser>` con `IterationCount = 210_000` y sigue funcionando.

**Files**
- `LicensingAdmin/Auth/PasswordHasherService.cs` — nuevo
- `LicensingSystem.Tests/PasswordHasherServiceTests.cs` — nuevo: clase `PasswordHasherServiceTests`

**Acceptance**

1. **WHEN** `Hash(p)` output is passed to `Verify(hash, p)` **THE SYSTEM SHALL** return true.
2. **WHEN** `Verify(hash, wrongPassword)` is called **THE SYSTEM SHALL** return false.
3. **WHEN** the same password is hashed twice **THE SYSTEM SHALL** produce two different hash strings.
4. **WHEN** `Verify` is called with a malformed, non-matching, `null`, or empty `hash`, or with a `null` `password` **THE SYSTEM SHALL** return false and **SHALL NOT** throw.
5. **WHEN** `PasswordHasherService` is constructed **THE SYSTEM SHALL** take an injected `PasswordHasher<AdminUser>` (fed by `IOptions<PasswordHasherOptions>` from DI) rather than a fixed field initializer, so the iteration count is configurable by `LicensingAdmin` at step 8.
6. **WHEN** `dotnet test --filter PasswordHasher` runs **THE SYSTEM SHALL** report all `PasswordHasher` tests passed, 0 failed.

**Verify**

```bash
dotnet test --filter PasswordHasher
```

**Checkpoint**

```bash
git add -A && git commit -m "step 7: password-hasher"
git tag step-07-password-hasher
```

### `E1-T8` — Cookie auth + politicas + fallback policy + shell de auth

**Depends on:** E1-T6, E1-T7 · **Priority:** p0

`AuthPolicies` con constantes y `AddAdminAuthorization(this IServiceCollection)`: `ViewerAccess` =
`RequireAuthenticatedUser()`; `ReviewAccess` e `IssueAccess` = `RequireRole(nameof(AdminRole.SupportStaff),
nameof(AdminRole.SuperAdmin))`; `AdminUserAccess` = `RequireRole(nameof(AdminRole.SuperAdmin))`;
además `options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()`
(toda ruta exige sesión salvo `[AllowAnonymous]`). En `Program.cs`: `AddAuthentication(Cookie...).
AddCookie(o => { LoginPath = "/Account/Login"; AccessDeniedPath = "/Account/Login"; Cookie.HttpOnly =
true; Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; SlidingExpiration = true; })`;
`AddAdminAuthorization()`; `services.Configure<PasswordHasherOptions>(o => o.IterationCount =
210_000)` (OWASP 2024 para PBKDF2-HMAC-SHA512; el `PasswordHasherService` del paso 7 lo recibe por
DI); `app.UseAuthentication(); app.UseAuthorization();` **entre** `UseRouting` y
`MapBlazorHub`. `App.razor` → `CascadingAuthenticationState` + `AuthorizeRouteView` con
`<NotAuthorized>` que renderiza un componente `RedirectToLogin` (navega a `/Account/Login?returnUrl=`;
durante SSR produce el 302). `_Imports.razor` → los dos `@using` de autorización. El test construye
un `ServiceProvider` con `AddAdminAuthorization()` y evalúa la matriz rol→política + la
`FallbackPolicy` con `IAuthorizationService`.

**Files**
- `LicensingAdmin/Auth/AuthPolicies.cs` — nuevo
- `LicensingSystem.Tests/AuthPoliciesTests.cs` — nuevo: clase `AuthPoliciesTests`
- `LicensingAdmin/Program.cs` — edit: cookie auth + políticas + `FallbackPolicy` + orden de middleware
- `LicensingAdmin/App.razor` — edit: `CascadingAuthenticationState` + `AuthorizeRouteView` + `RedirectToLogin`
- `LicensingAdmin/_Imports.razor` — edit: `@using Microsoft.AspNetCore.Authorization` / `.Components.Authorization`

**Acceptance**

1. **WHEN** `AuthPolicies` is inspected **THE SYSTEM SHALL** expose `ViewerAccess`, `ReviewAccess`, `IssueAccess`, `AdminUserAccess` as string constants.
2. **WHEN** a `ClaimsPrincipal` in role `ReadOnlyViewer` is checked **THE SYSTEM SHALL** satisfy `ViewerAccess` and fail `ReviewAccess`, `IssueAccess`, `AdminUserAccess`.
3. **WHEN** a `ClaimsPrincipal` in role `SupportStaff` is checked **THE SYSTEM SHALL** satisfy `ViewerAccess`, `ReviewAccess`, `IssueAccess` and fail `AdminUserAccess`.
4. **WHEN** a `ClaimsPrincipal` in role `SuperAdmin` is checked **THE SYSTEM SHALL** satisfy all four policies, and **WHEN** an unauthenticated `ClaimsPrincipal` is checked **THE SYSTEM SHALL** fail all four policies and the fallback policy.
5. **WHEN** `LicensingAdmin/Program.cs` is inspected **THE SYSTEM SHALL** place `app.UseAuthentication()` after `app.UseRouting()` and before `app.MapBlazorHub()`, **SHALL** set an authorization `FallbackPolicy` that requires an authenticated user, and **SHALL** configure `PasswordHasherOptions.IterationCount` to at least 210000 (OWASP 2024 for PBKDF2-HMAC-SHA512).
6. **WHEN** `dotnet build LicensingSystem.sln` runs **THE SYSTEM SHALL** exit 0 and `dotnet test --filter AuthPolicies` **SHALL** report all tests passed, 0 failed.

**Verify**

```bash
dotnet build LicensingSystem.sln
dotnet test --filter AuthPolicies
awk '/app\.UseRouting\(\)/{r=NR} /app\.UseAuthentication\(\)/{a=NR} /app\.MapBlazorHub\(\)/{h=NR} END{exit !(r>0 && a>0 && h>0 && r<a && a<h)}' LicensingAdmin/Program.cs
```

**Checkpoint**

```bash
git add -A && git commit -m "step 8: cookie-auth"
git tag step-08-cookie-auth
```

---

## Epic acceptance

El epic está hecho cuando cada tarea está `done` **y**:

1. **WHEN** `dotnet build LicensingSystem.sln && dotnet test` runs from the repo root **THE SYSTEM SHALL** exit 0 with every `ConnectionStringGuard`, `LicenseKeyGenerator`, `LicenseSigner`, `CryptoRegistration`, `PasswordHasher` and `AuthPolicies` test passing, 0 failed, 0 skipped.
2. **WHEN** `git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'` runs **THE SYSTEM SHALL** find no match and exit 1.

```bash
dotnet build LicensingSystem.sln && dotnet test
git grep -n "Password=***REDACTED***" -- ':!docs/blueprint' ':!tasks.json'; test $? -eq 1
```

## Pitfalls

- **`dotnet test --filter X` sale 0 si el filtro no matchea nada.** Mitigado por
  el `.runsettings` (T1) y por nombrar cada clase de test
  para que el filtro la seleccione. Si un `--filter` pasa "sin tests", trátalo como fallo.
- **Referenciar `LicensingAdmin` (SDK Web) desde el proyecto de tests** puede no propagar el
  `FrameworkReference`. Por eso T1 lo añade explícito — sin él, T7/T8 no compilan.
- **Invertir el orden de middleware o quitar la `FallbackPolicy` en `Program.cs`** ya **sí** lo
  detecta el `awk` de T8 (orden) y `AuthPoliciesTests` (fallback). No lo desactives para "que pase".
- **No toques `LicenseFileService`.** El firmador de T5 firma el registro `License`, no el archivo
  de licencia por activación.

## Before moving on

- [ ] Cada tarea de este epic está `done` en `tasks.json` — ninguna quedó `in_progress`.
- [ ] Cada comando `verify` de cada tarea pasó, no solo el primero.
- [ ] Ningún comando `verify` fue editado, y ninguno se saltó porque un archivo que nombra no existía.
- [ ] Cada tarea tiene su tag de checkpoint en git — `git tag -l 'step-0[1-8]-*'` lista 8.
- [ ] `dotnet build LicensingSystem.sln && dotnet test` pasa limpio desde la raíz del repo.
- [ ] Cada contrato "Producido" arriba existe con la firma indicada.
- [ ] Ningún archivo fuera del subárbol fue modificado.
- [ ] Este proyecto no usa `.env` — no hay `.env.example` que actualizar.
- [ ] Un commit por tarea, cada uno prefijado `step NN:`, cada uno seguido de su tag.
