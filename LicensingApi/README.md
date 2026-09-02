# Licensing API — EF Core / PostgreSQL setup

This is the data-layer starting point: entity classes under `Entities/`, the
`AppDbContext` under `Data/`, and a reference SQL script under `Migrations/`.

## 1. Prerequisites

- .NET 8 SDK
- PostgreSQL 15+ running (locally or on your single IIS host)
- EF Core CLI tools (one-time, per machine):
  ```
  dotnet tool install --global dotnet-ef
  ```

## 2. Restore packages

```
cd LicensingApi
dotnet restore
```

## 3. Set your connection string

Edit `appsettings.json` → `ConnectionStrings:LicensingDb`, or override locally with:
```
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:LicensingDb" "Host=localhost;Port=5432;Database=licensing;Username=licensing_app;Password=..."
```

## 4. Generate the real EF Core migration

The `AppDbContext` now lives in the `LicensingCore` class library (shared with
the `LicensingAdmin` web front end), so point the EF tooling at it explicitly
with `--project`, using `LicensingApi` as the startup project that holds the
connection string:

```
dotnet ef migrations add InitialCreate --project ..\LicensingCore --startup-project .
dotnet ef database update --project ..\LicensingCore --startup-project .
```

(Run from inside the `LicensingApi` folder; adjust the relative path if you
run it from the solution root instead.)

## 5. Reference SQL (optional, immediate)

`Migrations/001_initial_schema.sql` is a hand-written script matching the
current model 1:1 — useful if you want to inspect or stand up the schema by
hand before your tooling is set up, or diff against what `dotnet ef` generates.
It is **not** wired into EF Core's migration history table, so don't mix the
two approaches on the same database — pick one path per environment.

## Notes on the model

- `LicenseModel` is a `[Flags]` enum stored as an `integer` bitmask (e.g.
  `Machine | Subscription` = 1 + 8 = 9), matching the "combinable license
  models" requirement.
- `License.ModelSnapshot` and `MaxActivations` are copied from
  `SoftwareProduct` at issue time, so changing a product's defaults later
  never retroactively changes already-issued keys.
- A hardware mismatch creates a **new** `Activation` row (`PendingReview`)
  rather than mutating the existing approved one — so the full history of
  attempts per license is preserved for the piracy-review report.
- `NotificationConfig.PasswordEncrypted` and `License.Signature` are `bytea`
  — never store these as plaintext strings. Use ASP.NET Core Data Protection
  (or similar) for the SMTP password, and your RSA/ECDSA signing key for
  license signatures.
- Enums are stored as `varchar` (via `HasConversion<string>()`) for
  `Status`/`Role`/etc. so the raw table data stays human-readable in
  PostgreSQL, while `LicenseModel` stays an `int` since it's a bitmask.

## Next steps

Once the migration is applied, the next pieces are the `/activate` and
`/checkin` endpoint implementations (signature validation, hardware-match
logic, and the pending-review creation path) against this schema.

## 6. `/activate` and `/checkin` endpoints

Implemented under `Controllers/ActivationController.cs`, backed by
`Services/ActivationService.cs` (orchestration), `Services/HardwareMatchService.cs`
(the four-field same-machine rule), and `Services/LicenseFileService.cs` (the
signed + encrypted license file the DLL persists locally).

### Generating dev crypto keys

The RSA signing key and AES key are read from `Crypto:RsaPrivateKeyPem` /
`Crypto:AesKeyBase64` in configuration (use `dotnet user-secrets` locally,
never commit real keys). To generate a dev RSA key pair:

```
openssl genrsa -out dev-private.pem 2048
openssl rsa -in dev-private.pem -pubout -out dev-public.pem
```

Set the contents of `dev-private.pem` as `Crypto:RsaPrivateKeyPem` (server-side
signing) and ship `dev-public.pem` with the activation DLL for signature
verification. For the AES key:

```
openssl rand -base64 32
```

Set that as `Crypto:AesKeyBase64`. If neither is configured, `Program.cs`
falls back to a randomly generated in-memory key pair so the app still runs
locally — but every restart invalidates previously issued license files, so
this fallback is dev-only and must not be used once real activations exist.

### What's implemented vs. still open

- Key format validation, license lookup, revoked/expired checks, hardware-match
  same-machine logic, pending-review creation with a 15-day deadline, and
  subscription grace-period locking on check-in are all implemented.
- The license file is a pragmatic sign-then-encrypt envelope (RSA-SHA256 +
  AES-256-GCM), not full W3C XMLDSig/XMLEncrypt — see the comment in
  `LicenseFileService.cs` for the tradeoff and how to swap it if you need
  strict XMLDSig/XMLEncrypt interop.
- Rate limiting on these endpoints is flagged as a `TODO` in the controller —
  add ASP.NET Core's built-in rate limiting middleware before going live.
- Admin-facing endpoints (create license, approve/reject, revoke, dashboard,
  reports, notification config) are not built yet — that's the next slice.

### Sample data

`001_initial_schema.sql` ends with a small optional insert (one product, one
license, one pending-review activation) so the admin front end isn't empty
on first run. If you use `dotnet ef database update` instead of running this
script directly, that seed data won't be included — copy just the `INSERT`
block at the bottom into a query tool against `licensing_app` afterward if
you want it.
