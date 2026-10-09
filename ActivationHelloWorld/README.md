# ActivationHelloWorld

A minimal Windows console client that exercises `LicensingApi` through the shared
`MiautrixLicensingClient` DLL. It reads hardware via WMI, calls `/api/activate`
and `/api/checkin`, and verifies the returned license file signature.

This app is a test harness, not a production integration. Production clients should
use the DLL directly and implement their own retry, logging, local state, and offline
grace-period UX.

## Run

```bash
dotnet run --project ActivationHelloWorld
```

Options:

- `--base-url <url>` or `ACTIVATION_BASE_URL` — defaults to `https://licensing-api.miautrix.tech`.

## Activation prompts

Option `1` prompts for:

1. License key
2. Optional product version ID
3. Optional customer email

The email is sent to `/api/activate` and updates the license customer email server-side.

State (`InstallGuid`, `ActivationId`, last license file) persists to
`hello-world-state.json` next to the built executable.

## Menu

1. Activate a license key
2. Check in with the current hardware fingerprint
3. Show status and RSA-verify the last license file
4. Simulate hardware drift, then check in
5. Reset local state — new `InstallGuid`, as if this were a fresh machine

Admin actions like approve/reject/revoke are performed in `LicensingAdmin`.
