---
paths:
  - "LicensingAdmin/**"
---

# Panel de administración (LicensingAdmin)

- Acceso a datos: `@inject IDbContextFactory<AppDbContext>` + `await using var db`, o un servicio con
  costura (`ILicenseStore`, `IAdminUserStore`, `IAdminUserLookup`). Las impl. EF usan los `DbSet<>`
  con nombre de `AppDbContext` (`db.AdminUsers`, `db.SoftwareProducts`, `db.Licenses`,
  `db.AuditLogEntries`) o `db.Set<T>()` — ambos existen.
- **El panel no llama a `LicensingApi`.** (Non-Goal: admin→API.)
- Autorización de página: `@attribute [Authorize(Policy = AuthPolicies.<X>)]` con una constante de
  `Auth/AuthPolicies.cs`. Además `Program.cs` fija una `FallbackPolicy` que exige sesión salvo
  `[AllowAnonymous]`. `<AuthorizeView Policy="...">` en `MainLayout` es solo cosmético.
- El sign-in con cookie (`HttpContext.SignInAsync`) solo en Razor Pages bajo `Pages/Account/`.
- Toda mutación escribe un `AuditLogEntry` con `Actor = CurrentAdmin.Email(user)` (nunca un literal
  como `"support-staff@vendor.com"`), `EntityType`, `EntityId`, `Action`
  ("Login"/"Created"/"Approved"/"Rejected"/"Updated").
- La emisión de licencias pasa por `LicenseIssuanceService`; la clave la produce
  `LicenseKeyGenerator.NewKey()` con reintento ante colisión.
- `Program.cs`: `app.UseAuthentication(); app.UseAuthorization();` van **entre** `app.UseRouting()`
  y `app.MapBlazorHub()`.
