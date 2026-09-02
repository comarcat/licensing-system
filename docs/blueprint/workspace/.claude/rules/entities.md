---
paths:
  - "LicensingCore/**"
---

# Entidades y datos (LicensingCore)

- Tablas en `snake_case` vía `ToTable`. Enums como `varchar` con `HasConversion<string>()` **excepto
  `LicenseModel`**, que es un bitmask `int` (`[Flags]`: None=0, Machine=1, User=2, Floating=4,
  Subscription=8).
- **Este slice no cambia el esquema.** Un cambio de esquema futuro toca a la vez: la entidad,
  `AppDbContext.OnModelCreating` y `LicensingApi/Migrations/001_initial_schema.sql`.
- `License.Signature` es la firma RSA-SHA256 (`RSASignaturePadding.Pkcs1`) sobre el payload canónico
  que produce `LicenseSigner.CanonicalBytes`:
  `{LicenseKey}|{ProductId:D}|{(int)ModelSnapshot}|{MaxActivations}|{SubscriptionExpiryUtc:O-or-empty-string}`.
- `ModelSnapshot` y `MaxActivations` de una `License` se **congelan** en el momento de emisión
  (copia del request/producto); cambiar el producto después no los altera.
- `LicensingCore` no referencia `LicensingApi` ni `LicensingAdmin`.
- Todo tipo que un test prueba es `public`. Sin `InternalsVisibleTo`.
