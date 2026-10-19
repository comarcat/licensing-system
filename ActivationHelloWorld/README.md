# ActivationHelloWorld

A minimal Windows console client that exercises `LicensingApi` exactly the way a real
activation DLL would (see `docs/activation-dll-integration-reference.md`): reads
hardware via WMI, calls `/api/activate` and `/api/checkin`, and verifies the returned
license file's signature. Built to drive end-to-end lifecycle testing against the live
API — it is **not** a production integration; a real client tool needs far more care
around retries and offline grace UX than this does.

## Run

```
dotnet run --project ActivationHelloWorld
```

- `--base-url <url>` or `ACTIVATION_BASE_URL` env var — defaults to
  `https://licensing-api.miautrix.tech`, the live public endpoint (Cloudflare Tunnel +
  Origin CA cert, confirmed working end to end as of 2026-09-13). Pass
  `--base-url http://10.11.1.41:8080` to test against the LAN-direct address instead.

That's the only configuration needed — the license file is signed only (no
encryption, no key to obtain out of band; see `docs/activation-dll-integration-reference.md`
§5), so the RSA public key already embedded in `Keys.cs` is all this app needs to
verify what the server sends back.

State (`InstallGuid`, `ActivationId`, last license file) persists to
`hello-world-state.json` next to the built exe, between runs.

## Menu

1. Activate a license key (obtained from `LicensingAdmin` → Generar licencia)
2. Check in with the real, current hardware fingerprint
3. Show status — RSA-verifies the last license file received
4. Simulate hardware drift (mutates the CPU/motherboard fields locally), then checks in
5. Reset local state — new `InstallGuid`, as if this were a fresh machine

Approve/reject (Pending Review) and revoke are admin actions — do those in
`LicensingAdmin`, then come back here and check in to see the result.
