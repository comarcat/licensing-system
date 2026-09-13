# Licensing API — Activation Client Integration Reference

**Audience:** the developer of the client-side activation tool (DLL/EXE) that runs on
the end customer's machine and talks to `LicensingApi`.
**Status:** matches the code in `LicensingApi` as of 2026-09-13 (branch
`feature/e3-usability-improvements`, on top of the merged `main`). Admin-facing
endpoints (issue/revoke/reports) are a separate, already-built surface in
`LicensingAdmin` — this document covers only the two endpoints the client tool calls.
**Live instance:** `https://api.licensing.miautrix.tech` — public hostname, TLS
terminated at Cloudflare's edge and re-encrypted (Cloudflare Tunnel, Full/strict) to
nginx on the LXC, which itself now presents a real Cloudflare Origin CA certificate on
port 8080 (no more self-signed/plaintext). As of this writing the DNS record and the
origin's own TLS are both confirmed working end to end from the LXC outward, but
Cloudflare's edge certificate for this exact hostname is still finishing provisioning
on their side — if a request to this URL fails at the TLS handshake stage (not a 4xx/5xx
from the app), that's what's still catching up, not a client-side or origin problem.
The LAN-only address (`http://10.11.1.41:8080`, still unauthenticated/plaintext) still
works for local testing while that finishes. See `infra/README.md`.

---

## 1. Overview

The client tool activates a license key once per machine, then periodically checks in.
Both operations return a **signed, encrypted license file** the client persists locally
and re-validates offline between check-ins. The server never trusts the client's local
state — every check-in re-derives the outcome from the database.

```
Client tool                          LicensingApi
    |  POST /api/activate                 |
    |------------------------------------>|
    |  <-- ApiResult { LicenseFileBase64 }|
    |                                     |
    |  (periodically, per Policy.CheckIntervalHours)
    |  POST /api/checkin                  |
    |------------------------------------>|
    |  <-- ApiResult { LicenseFileBase64 }|
```

## 2. Endpoints

### `POST /api/activate`

Called once, the first time the software runs on a machine (or again if the machine's
hardware fingerprint no longer matches any activation on record for this key).

**Request body** (`application/json`):

```csharp
public class ActivateRequest
{
    public required string LicenseKey { get; set; }        // "XXXX-XXXXX-XXXX-XXXX-XXXX-XXXX-XX"
    public required Guid InstallGuid { get; set; }          // generated ONCE by the client, persisted locally
    public required HardwareInfo Hardware { get; set; }
    public VmInfo? Vm { get; set; }                          // optional
    public string? AppVersion { get; set; }                  // optional, informational
    public DateTime ClientTimestampUtc { get; set; }
}

public class HardwareInfo
{
    public required string CpuId { get; set; }
    public required string MotherboardSerial { get; set; }
    public required string TpmId { get; set; }
    public required string MacAddressPrimary { get; set; }
    public string? OsType { get; set; }                       // informational only
    public string? OsVersion { get; set; }                    // informational only
    public string? CpuModel { get; set; }                     // informational only
    public int? RamGb { get; set; }                           // informational only
}

public class VmInfo
{
    public bool HypervisorPresent { get; set; }
    public List<string> Signals { get; set; } = new();        // free-text signal names, e.g. "hypervisor_bit"
}
```

**The 4 required `HardwareInfo` fields are the entire "same machine" identity.** The
server does an exact string match on all four together — no fuzzy matching, no partial
credit. If your tool cannot read one of them reliably on some hardware, populate it with
a stable placeholder rather than an empty string (an empty string still participates in
the exact match and is valid, just make sure it's *consistently* empty on that machine
across activations, not sometimes-empty).

### `POST /api/checkin`

Called periodically per `Policy.CheckIntervalHours` from the last activate/checkin
response (currently always 6 hours — `DefaultCheckIntervalHours` in `ActivationService`).

```csharp
public class CheckinRequest
{
    public required Guid ActivationId { get; set; }          // from the previous activate/checkin response
    public required Guid InstallGuid { get; set; }            // must match what was sent at activation
    public required HardwareInfo Hardware { get; set; }
    public VmInfo? Vm { get; set; }
    public string? LastLocalStatus { get; set; }               // optional, informational
    public DateTime ClientTimestampUtc { get; set; }
}
```

**If the hardware no longer matches** what's on record for this `ActivationId`, the
server re-opens review on this **same** activation row — new fingerprint recorded,
`Status` back to `PendingReview`, a fresh `GraceDays`-day `ReviewDeadlineUtc` — rather
than creating a second row (the `(LicenseId, InstallGuid)` pair is unique per license,
and your `InstallGuid` is stable across a hardware change, so a second row for the same
install could never be created anyway). So a checkin on drifted hardware can come back
`pending_review` even though the client only asked to check in, but `ActivationId`
never changes underneath you.

### `GET /health`

No auth, no body. Returns `{"status":"ok"}`. Use this for connectivity probes only —
it says nothing about license state.

## 3. Response envelope

Every endpoint returns the same wrapper, with the appropriate HTTP status code:

```csharp
public class ApiResult
{
    public bool Success { get; set; }
    public required ResultCode Code { get; set; }
    public string? Message { get; set; }              // present on failures, human-readable
    public ActivationResultData? Data { get; set; }    // present on success
}

public class ActivationResultData
{
    public Guid? ActivationId { get; set; }
    public string Status { get; set; } = "";           // "approved" | "pending_review" | "locked" | "rejected"
    public string? LicenseFileBase64 { get; set; }     // see §4 — absent when Status == "locked"
    public PolicyDto? Policy { get; set; }
    public DateTime? SubscriptionExpiryUtc { get; set; }
    public DateTime? ReviewDeadlineUtc { get; set; }    // set only while Status == "pending_review"
    public string? Reason { get; set; }                 // machine-readable detail, e.g. "SUBSCRIPTION_EXPIRED_GRACE_ENDED"
}

public class PolicyDto
{
    public int CheckIntervalHours { get; set; }         // currently always 6
    public int GraceDays { get; set; }                  // currently always 15 (pending-review deadline)
    public int SubscriptionGraceDays { get; set; }       // currently always 30
}
```

### Result codes → HTTP status

| `ResultCode`             | HTTP | Meaning |
|---|---|---|
| `Activated`              | 200 | New or reactivated, approved immediately |
| `PendingReview`          | 200 | New install, awaiting admin approval (up to `GraceDays`) |
| `Renewed`                | 200 | Checkin succeeded, license file refreshed |
| `Locked`                 | 200 | Checkin succeeded but the license is now locked — see `Reason` |
| `InvalidKeyFormat`       | 400 | `LicenseKey` fails the format regex (§5) |
| `LicenseNotFound`        | 404 | No license row for that key |
| `ActivationNotFound`     | 404 | Checkin: unknown `ActivationId` |
| `LicenseExpired`         | 403 | License row's own `Status` is `Expired` |
| `LicenseRevoked`         | 403 | License row's own `Status` is `Revoked` |
| `InstallGuidMismatch`    | 403 | Checkin: `InstallGuid` doesn't match the activation record |
| `MaxActivationsReached`  | 409 | New install rejected: `License.MaxActivations` already reached — no `Activation` row is created |
| `RateLimited`            | 429 | Per-client-IP throttle tripped — see note below |
| `ServerError`            | 500 | Unhandled exception; retry with backoff |

**Note on `MaxActivationsReached`:** enforced as a hard rejection since 2026-09-13. A
*new* install (no existing hardware match on this license) is rejected outright —
`Data` is `null`, `Message` explains the limit — when the count of currently `Approved`
activations already meets `License.MaxActivations`. This only gates brand-new
installs; reactivating an existing approved/pending-review machine (same hardware) is
never blocked by this check, since it isn't consuming a new slot.

**Rate limiting:** since 2026-09-13, `/api/activate` and `/api/checkin` are throttled
per client IP address — a fixed window of 30 requests/minute, no queueing (the 31st
request in a given minute gets `RateLimited` immediately rather than waiting). This
reads the real client IP from `X-Forwarded-For` (nginx sets it; a live burst test
confirmed the limiter itself works correctly over the LAN path). Cloudflare Tunnel
now sits in front for `api.licensing.miautrix.tech` — it's expected to forward the
real end-client IP the same way, but that specific hop hasn't been re-verified yet
through the public hostname (still finishing edge setup as of this writing per the
note at the top of this document) — worth a quick recheck once it's live, since a
tunnel/proxy that doesn't forward IPs truthfully would make every public client share
one partition. One public IP shared by many installs (e.g. one office behind NAT)
shares the same 30/min
budget — if that's too tight for a real deployment, this is a single number
(`PermitLimit` in `LicensingApi/Program.cs`) to tune, not a redesign.

## 4. License key format

```
^[A-Z0-9]{4}-[A-Z0-9]{5}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{4}-[A-Z0-9]{2}$
```
Example: `BRCX-IH9BC-X9ZV-KQXC-2NLY-CO03-8P` (4-5-4-4-4-4-2 groups, uppercase
alphanumeric, hyphen-separated). Validate client-side before calling `/activate` to
avoid a round trip for an obviously malformed key.

## 5. The license file (`LicenseFileBase64`)

This is what the client tool must decrypt and verify, then trust for offline operation
between check-ins. It is **not** a JWT and **not** standard XMLDSig/XMLEncrypt — it's a
deliberately simple sign-then-encrypt envelope (see the comment at the top of
`LicensingApi/Services/LicenseFileService.cs` if the server side ever needs to change
this).

### 5.1 Byte layout (after base64-decoding `LicenseFileBase64`)

```
[ 12 bytes: AES-GCM nonce ] [ 16 bytes: AES-GCM auth tag ] [ N bytes: ciphertext ]
```

### 5.2 Decrypt

- Algorithm: **AES-256-GCM**.
- Key: the 32-byte AES key configured server-side as `Crypto:AesKeyBase64` — **this key
  must be shared with the client tool out of band** (it is symmetric, so it cannot be
  embedded in a way that's safe from extraction from the binary; treat this the same way
  you'd treat any embedded symmetric secret — obfuscation, not real secrecy, is the best
  a client-side key can offer). Decrypting `ciphertext` with `nonce`/`tag` yields the
  **signed XML document** (UTF-8 bytes) described next.

### 5.3 The signed XML document

Once decrypted, you have UTF-8 bytes of XML shaped exactly like this (element order
matters if you need to reproduce the canonicalization for signature verification):

```xml
<LicenseActivation xmlns="urn:licensing:v1">
  <LicenseKey>BRCX-IH9BC-X9ZV-KQXC-2NLY-CO03-8P</LicenseKey>
  <ActivationId>3fa85f64-5717-4562-b3fc-2c963f66afa6</ActivationId>
  <InstallGuid>...</InstallGuid>
  <Status>approved</Status>
  <Hardware>
    <CpuId>...</CpuId>
    <MotherboardSerial>...</MotherboardSerial>
    <TpmId>...</TpmId>
    <MacAddressPrimary>...</MacAddressPrimary>
  </Hardware>
  <Policy>
    <CheckIntervalHours>6</CheckIntervalHours>
    <GraceDays>15</GraceDays>
    <SubscriptionGraceDays>30</SubscriptionGraceDays>
  </Policy>
  <SubscriptionExpiryUtc>2027-01-01T00:00:00.0000000Z</SubscriptionExpiryUtc>
  <IssuedAtUtc>2026-09-13T12:00:00.0000000Z</IssuedAtUtc>
  <Signature>base64-RSA-signature</Signature>
</LicenseActivation>
```

`SubscriptionExpiryUtc` is an empty element (`<SubscriptionExpiryUtc></SubscriptionExpiryUtc>`)
when the license has no subscription expiry (perpetual/machine-model licenses).
Both date fields use .NET's round-trip ("O") format.

### 5.4 Verify the signature

1. Take the decrypted XML **exactly as received**, remove the `<Signature>` element,
   and re-serialize with **no indentation/formatting** (.NET's
   `XElement.ToString(SaveOptions.DisableFormatting)` — the signature was computed over
   this exact byte form; any added/removed whitespace, reordered attributes, or changed
   line endings will make verification fail even though the content is "the same").
2. Get the UTF-8 bytes of that canonical (Signature-less) XML.
3. Verify with **RSA-SHA256, PKCS#1 v1.5 padding** against those bytes, using the
   base64 content of the `<Signature>` element as the signature and the public key
   below.
4. If verification fails, treat the license file as tampered — do not honor `Status`,
   `SubscriptionExpiryUtc`, or anything else in it.

### 5.5 Public key (safe to embed in the client — this is the public half only)

```
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAt6fBjOqh1XA5RCBl/feB
ogm6Hn/ZZSBWv/10PjavfquYPq6+IYckg03bnBzzUeELj3LvWjAunOfgJ3qQ3OsW
Pij0e76FLmb4EJbUTTe/ycKDKPjwG0mtWHRt7tmav+NzsxuBuTVC6vN4Tzxzyhvf
Z6i8l3sfljy/9boxSkbxBAlvL2ZwYVT0ukD00xm4Ycx0l+D3PRDLdGGEsCqrT3cO
iZ5XOxVYve8eddD0eMPUGFw69MfSKleZzSrhMDmieMXZWomU0AAjA5X2fuXZfN89
THwiGD6Q/Pxil99Hh4Hm+9+oj9K+nu7kJqxTNNVB/tp9HJD67xqdIWs7VQoJCPDw
DOE4uwy/frvp8xvcahDcyTUJ5AkD3sk6/UlamHddsdQWT6iT5D3jqybcjjmpHnuC
SGqzcz+0joqlakzmRQUhWlA7EhANELZvBFI2Iz/wABkSFGpNsdqOzRNvOobUnxk9
N+0hXYgWO0XYb0ay0BqosLLAvRYElioZyDMVKkGR4Xh7AgMBAAE=
-----END PUBLIC KEY-----
```
(3072-bit RSA. Corresponds to the private key currently deployed on `10.11.1.41` for
both `LicensingApi` and `LicensingAdmin` — see `team/inbox-arq.md`, 2026-09-13. If this
key is ever rotated, every `LicenseFileBase64` signed with the old key stops verifying;
plan a rotation as a coordinated release, not a silent config change.)

**The symmetric AES key is not published in this document** — request it through a
secure, out-of-band channel (it lives only in the server's `/etc/licensing-api/env`,
never in git). Unlike the RSA key, this one must stay confidential since it's used for
both directions (only the server encrypts today, but anyone with the AES key can
decrypt any license file).

## 6. Business rules the client should anticipate

- **New install, no hardware match on record** → `PendingReview`. `ReviewDeadlineUtc` is
  15 days out. The license file's `Status` will be `pending_review` in this window —
  the client tool should decide its own local grace-period UX (e.g., run in a limited
  mode) rather than blocking entirely, since a human has up to 15 days to approve it.
- **Same hardware reactivating** → immediately re-approved (unless still pending) and
  the check-in cadence continues normally.
- **Hardware fingerprint changes** (new machine, or a component swap that changes one of
  the 4 fields) → the *same* `ActivationId` re-enters `PendingReview` with the new
  fingerprint, *including on a `/checkin` call* — a checkin is not guaranteed to return
  `Renewed`. Keep using the `ActivationId`/`InstallGuid` you already have; nothing new
  is issued for this case.
- **Subscription expiry**: once `SubscriptionExpiryUtc` + `SubscriptionGraceDays` (30
  days) has passed, checkin returns `Locked` with `Reason = "SUBSCRIPTION_EXPIRED_GRACE_ENDED"`
  and no `LicenseFileBase64` — the client should stop honoring any previously cached
  license file at that point regardless of what it says locally.
- **Revoked license**: any checkin against a revoked license returns `Locked` with
  `Reason = "LICENSE_REVOKED"`, no license file.
- **Perpetual licenses** (no `SubscriptionExpiryUtc`) never lock on subscription grounds
  — only revocation locks them.

## 7. Sample activate request/response

Request:
```json
{
  "licenseKey": "BRCX-IH9BC-X9ZV-KQXC-2NLY-CO03-8P",
  "installGuid": "8b1e6f2a-1111-2222-3333-000000000001",
  "hardware": {
    "cpuId": "BFEBFBFF000A0655",
    "motherboardSerial": "MB-9F21-0001",
    "tpmId": "TPM-0001",
    "macAddressPrimary": "00:1A:2B:3C:4D:5E",
    "osType": "Windows 11 Pro",
    "osVersion": "10.0.26100",
    "cpuModel": "Intel Core i7-13700",
    "ramGb": 32
  },
  "appVersion": "1.4.2",
  "clientTimestampUtc": "2026-09-13T12:00:00Z"
}
```

Response (`200 OK`, new install — **every** brand-new activation starts in
`PendingReview`; the client only ever sees `Activated`/`approved` on a later
reactivation of hardware that an admin already approved, never on the first call):
```json
{
  "success": true,
  "code": "PendingReview",
  "message": null,
  "data": {
    "activationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "status": "pending_review",
    "licenseFileBase64": "…(base64 envelope, see §5)…",
    "policy": { "checkIntervalHours": 6, "graceDays": 15, "subscriptionGraceDays": 30 },
    "subscriptionExpiryUtc": null,
    "reviewDeadlineUtc": "2026-09-28T12:00:00.0000000Z",
    "reason": null
  }
}
```

## 8. Open items (flag to the Builder/Architect if the client tool needs them sooner)

- Admin-configurable `Policy` (check interval, grace days) doesn't exist yet — these
  three numbers are hard-coded constants in `ActivationService` today.
- `Policy`/business rules (§6) don't yet distinguish requests arriving over the public
  `api.licensing.miautrix.tech` hostname from LAN-direct ones — both are treated
  identically today, which is fine as long as the LAN address isn't also exposed
  publicly by accident.

Resolved since the first version of this document:
- Rate limiting and `MaxActivationsReached` hard enforcement (both 2026-09-13).
- TLS end-to-end: `https://api.licensing.miautrix.tech` → Cloudflare Tunnel (Full/
  strict) → nginx on the LXC presenting a real Cloudflare Origin CA cert on port 8080
  (2026-09-13). The API is no longer plaintext-on-the-wire from a public client's
  point of view. `X-Forwarded-For`/`-Proto` are forwarded correctly through the whole
  chain, confirmed against the rate limiter (§3).
