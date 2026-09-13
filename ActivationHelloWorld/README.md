# ActivationHelloWorld

A minimal Windows console client that exercises `LicensingApi` exactly the way a real
activation DLL would (see `docs/activation-dll-integration-reference.md`): reads
hardware via WMI, calls `/api/activate` and `/api/checkin`, and decrypts + verifies the
returned license file. Built to drive end-to-end lifecycle testing against the live
API — it is **not** a production integration; a real client tool needs far more care
around key storage, retries, and offline grace UX than this does.

## Run

```
dotnet run --project ActivationHelloWorld -- --base-url http://10.11.1.41:8080
```

- `--base-url <url>` or `ACTIVATION_BASE_URL` env var — defaults to the LAN address.
  `https://licensing-api.miautrix.tech` (Cloudflare Tunnel + Origin CA cert) is live and
  confirmed working end to end as of 2026-09-13 — pass
  `--base-url https://licensing-api.miautrix.tech` to test through the real public path
  instead of the LAN.
- `--aes-key-file <path>` or `ACTIVATION_AES_KEY_BASE64` env var — the symmetric AES
  key used to decrypt license files (get it out of band, never commit it; see
  `docs/activation-dll-integration-reference.md` §5.2). Without it the app still
  activates/checks in, it just can't show you the decrypted contents.

State (`InstallGuid`, `ActivationId`, last license file) persists to
`hello-world-state.json` next to the built exe, between runs.

## Menu

1. Activate a license key (obtained from `LicensingAdmin` → Generar licencia)
2. Check in with the real, current hardware fingerprint
3. Show status — decrypts and RSA-verifies the last license file received
4. Simulate hardware drift (mutates the CPU/motherboard fields locally), then checks in
5. Reset local state — new `InstallGuid`, as if this were a fresh machine

Approve/reject (Pending Review) and revoke are admin actions — do those in
`LicensingAdmin`, then come back here and check in to see the result.
