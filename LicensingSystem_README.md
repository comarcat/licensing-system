# Licensing System — Visual Studio Solution

Three projects, tied together by `LicensingSystem.sln`:

| Project | Type | Purpose |
|---|---|---|
| `LicensingCore` | Class library | `Entities/`, `AppDbContext` — shared by both apps below |
| `LicensingApi` | ASP.NET Core Web API | `/api/activate`, `/api/checkin` (DLL-facing) |
| `LicensingAdmin` | Blazor Server + MudBlazor | Admin web front end (Dashboard, Licenses, Pending Review x2) |

## Opening in Visual Studio

1. Double-click `LicensingSystem.sln`.
2. Right-click the solution → **Set Startup Projects** → **Multiple startup
   projects** → set both `LicensingApi` and `LicensingAdmin` to **Start**, so
   `F5` launches both together.
3. Restore NuGet packages (Visual Studio usually does this automatically on
   open; otherwise right-click the solution → **Restore NuGet Packages**).

## Database

Both `LicensingApi/appsettings.json` and `LicensingAdmin/appsettings.json`
ship with an empty `ConnectionStrings:LicensingDb`. The expected connection
string has the shape:

```
Host=<host>;Port=5432;Database=licensing_app;Username=<user>;Password=<password>
```

**Never put a real connection string (with a password) in `appsettings.json`.**
The credential always goes in `dotnet user-secrets` or in the
`ConnectionStrings__LicensingDb` environment variable; `appsettings.json`
stays with the empty placeholder. From each of the `LicensingApi` and
`LicensingAdmin` folders:

```
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:LicensingDb" "Host=<host>;Port=5432;Database=licensing_app;Username=<user>;Password=<password>"
```

## First run

1. Apply the EF Core migration against `licensing_app` — see
   `LicensingApi/README.md` §4 for the exact `dotnet ef` commands
   (needs the `dotnet-ef` global tool installed once per machine).
2. There's no data yet, so `Licenses` and `Pending Review` will show empty
   states. To see them populated, either:
   - Call `POST /api/activate` on the running `LicensingApi` a few times
     (e.g. via Postman/curl) with different license keys/hardware — but
     note license keys have to already exist in the `licenses` table first,
     since `/activate` doesn't create licenses, only activations. Insert a
     row or two directly into `licenses` (and a matching `software_products`
     row) to get started, or
   - Wait for the admin "Generate New License" flow, which isn't built yet
     (see below).
3. Run the solution (`F5` with both startup projects set). `LicensingAdmin`
   opens to the Dashboard; `LicensingApi` exposes `GET /health` to confirm
   it's up.

## What's built vs. what's next

Built: entities/migration, `/activate` + `/checkin`, and three admin
screens (Dashboard, Licenses list, Pending Review with working
Approve/Reject that writes back to the database).

Still open, in roughly the order I'd tackle them:
- **License generation UI** — the "+ Generate New License" flow from the
  wireframes (create a `SoftwareProduct`, issue a signed key).
- **Admin authentication** — cookie login is in place (`/Account/Login`),
  the admin screens require an authenticated admin, and Pending Review now
  records the reviewer from the signed-in account. Remaining: the first
  SuperAdmin seeder and the admin-user management screen.
- **Reports screen** (PDF/XLS export) and **Notification Settings** screen.
- Wiring `LicensingAdmin` to call `LicensingApi`'s admin endpoints instead
  of (or in addition to) querying the database directly — fine as-is for a
  single-host internal tool, but worth revisiting if the admin app and API
  are ever split across hosts.
