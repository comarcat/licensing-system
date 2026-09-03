---
name: add-admin-page
description: Añadir una pantalla de administración Blazor protegida a LicensingAdmin. Úsala cuando
  el pedido sea "add an admin screen", "nueva pantalla admin", "add a gated Blazor page" o similar —
  crea la ruta, la protege con una política de AuthPolicies, la conecta a datos vía IDbContextFactory
  o un servicio con costura, escribe el AuditLogEntry y añade el enlace de nav bajo AuthorizeView.
---

# Add admin page

## Cuándo usarla
Cuando haya que añadir una pantalla nueva a `LicensingAdmin/Pages/` que solo cierto rol pueda ver.

## Pasos
1. Crear `LicensingAdmin/Pages/<Area>/<Nombre>.razor` con `@page "/<ruta>"` y
   `@attribute [Authorize(Policy = AuthPolicies.<Politica>)]` (usa una constante existente de
   `LicensingAdmin/Auth/AuthPolicies.cs`; si necesitas una política nueva, añádela allí y a su
   registro, y a `AuthPoliciesTests`).
2. Datos: para lectura simple, `@inject IDbContextFactory<AppDbContext>` + `await using var db`.
   Para lógica con reglas (duplicados, validación), crea un servicio en `LicensingAdmin/` con una
   costura (`I<Algo>Store`) para poder testearlo con un fake sin `DbContext`.
3. Toda mutación escribe un `AuditLogEntry` con `Actor = CurrentAdmin.Email(user)`,
   `EntityType`, `EntityId`, `Action`.
4. Añade el `MudNavLink` en `LicensingAdmin/Shared/MainLayout.razor` envuelto en
   `<AuthorizeView Policy="<Politica>">`.
5. Escribe `LicensingSystem.Tests/<Nombre>ServiceTests.cs` con un fake store; nombra la clase para
   que `dotnet test --filter <Nombre>` la seleccione.

## Verify
```bash
dotnet build LicensingSystem.sln              # expect: exit 0
dotnet test --filter <Nombre>                 # expect: exit 0 — la clase nueva pasa
```

## No hagas
- No pongas un string literal de política en la página — usa la constante de `AuthPolicies`.
- No dependas de `<AuthorizeView>` como única barrera; la política del `[Authorize]` es la real.
- No metas un `DbContext` en un test; usa el fake de la costura.
- No construyas una `License` a mano — pasa por `LicenseIssuanceService`.
