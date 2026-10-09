# Cross-Platform SDK Plan

## Goal

Extend Miautrix licensing integrations beyond the current .NET DLL while preserving the existing public API contract:

- `POST /api/activate`
- `POST /api/checkin`
- `GET /health`
- Signed XML license file format
- RSA-SHA256 signature verification with the public key
- Current activation rules for approved count, pending review, test licenses, revoked licenses, and email updates

## Current .NET SDK baseline

The current official integration surface is `MiautrixLicensingClient.dll`.

It provides:

- DTOs for activation and check-in requests/responses
- HTTP client wrapper for `/api/activate` and `/api/checkin`
- Windows hardware fingerprint reader
- Signed license file verification and parsing
- Embedded public RSA key

This DLL should remain the reference implementation for behavior and contract tests.

## Shared contract package

Before adding Java or web-specific SDKs, create shared contract artifacts that every SDK can consume:

1. JSON schemas or documented DTO tables for:
   - `ActivateRequest`
   - `CheckinRequest`
   - `ApiResult`
   - `ActivationResultData`
   - `PolicyDto`
   - `HardwareInfo`
   - `VmInfo`
2. Golden JSON examples for:
   - approved activation
   - pending-review activation
   - revoked license
   - expired license
   - rate-limited request
   - non-JSON gateway/server failure mapped to `ServerError`
3. Signed sample license files for verifier tests.
4. A public key rotation note.

## Java SDK plan

### Package shape

Recommended package name:

```text
tech.miautrix.licensing
```

Primary classes:

- `ActivationClient`
- `ActivateRequest`
- `CheckinRequest`
- `ApiResult`
- `ActivationResultData`
- `HardwareInfo`
- `VmInfo`
- `LicenseFileVerifier`
- `HardwareFingerprintProvider`

### Responsibilities

The Java SDK should provide:

- HTTP calls to `/api/activate` and `/api/checkin`
- JSON DTO serialization compatible with the .NET client
- RSA-SHA256 / PKCS#1 v1.5 XML license signature verification
- License XML parsing
- Pluggable hardware fingerprint providers
- Clear error handling for non-JSON responses

### Hardware strategy

Do not hard-code one cross-platform hardware fingerprint implementation at first.

Use an interface:

```java
public interface HardwareFingerprintProvider {
    HardwareInfo read();
}
```

Then add providers incrementally:

1. Windows provider
   - WMI or PowerShell fallback
   - CPU ID, motherboard serial, TPM ID, primary MAC
2. Linux provider
   - `/etc/machine-id`, DMI data, MAC address
   - TPM optional
3. macOS provider
   - IOPlatform UUID, serial, MAC address
   - TPM/Secure Enclave signal optional

Each provider must prefer stable identifiers and must not generate random values for missing fields.

### Build/distribution

Recommended distribution:

- Maven Central package when stable
- GitHub Packages for preview/internal builds
- JAR with no server secrets
- Source and binary compatibility policy documented per SDK version

### Java verification gates

- Unit tests for JSON serialization parity
- Unit tests for signed XML verification
- Mock HTTP tests for activate/checkin
- Fixture tests shared with .NET SDK
- No live API dependency in default tests

## Web application integration plan

### Server-side web apps

Preferred model for web apps is backend/server-side integration.

Examples:

- ASP.NET backend using `MiautrixLicensingClient.dll`
- Java/Spring backend using future Java SDK
- Node.js backend using a lightweight REST client and verifier
- Python backend using direct REST calls and RSA verification

The backend should:

1. Store the customer license key securely.
2. Generate or store one install/server instance ID.
3. Send activation/check-in requests from the server.
4. Verify signed license files before trusting them.
5. Expose only application-specific entitlement state to the browser.

### Browser-only applications

Browser-only activation is not recommended for strong licensing enforcement.

Limitations:

- Browser hardware fingerprinting is weak, privacy-sensitive, and unstable.
- Browser storage can be cleared or copied.
- Any public key is safe, but any secret in browser code is exposed.
- Offline grace behavior is hard to enforce securely.

If browser-only support is required, treat it as soft entitlement UX, not tamper-resistant licensing.

### Recommended web SDK shape

Provide a small server-side REST reference first:

- Request/response DTOs
- Signed license verification examples
- Activation/check-in sequence diagrams
- Error/result-code handling table

Then consider packages:

- `@miautrix/licensing-node` for Node.js backends
- `miautrix-licensing-python` for Python backends
- Java SDK for JVM/Spring backends

Avoid a browser SDK until there is a clear product requirement and threat model.

## Contract stability rules

These must remain stable unless a versioned migration is planned:

- `/api/activate`
- `/api/checkin`
- `/health`
- `ResultCode` names
- signed license XML namespace
- signature algorithm
- public key rotation behavior
- license key format

## Suggested phases

### Phase 1 — Contract hardening

- Finalize .NET DLL docs.
- Add shared JSON fixtures.
- Add signed license verification fixtures.
- Add tests proving .NET DTO parity with API DTOs.

### Phase 2 — Java SDK MVP

- Implement DTOs.
- Implement `ActivationClient`.
- Implement `LicenseFileVerifier`.
- Implement pluggable hardware provider interface.
- Ship Windows hardware provider first.

### Phase 3 — Server-side web references

- Publish Node.js backend example.
- Publish Java/Spring example.
- Publish REST-only integration guide.
- Document recommended server storage for activation state and license files.

### Phase 4 — Cross-SDK compatibility suite

- Run the same fixtures across .NET, Java, and web/server SDKs.
- Add CI matrix for SDK contract verification.
- Version contract fixtures with the API.

## Non-goals

- No private keys in any SDK.
- No admin-panel functionality in client SDKs.
- No browser-side strong hardware binding claim.
- No API route or database schema changes for SDK support.
