# Licensing v2 Admin Hardening Rules

Applies to this blueprint implementation.

- Maintain `docs/v2-admin-hardening-status.md` as the live activity/status table.
- Product versions are real `ProductVersion` rows, not `SoftwareProduct.CurrentVersion` strings.
- `Test` is a license status/mode, never a product version name.
- Archive products instead of hard-deleting them from the normal admin workflow.
- SMTP diagnostics may log host/port/encryption/auth type/exception type, but never password or decrypted secret.
- Old activation clients that omit `VersionId` must remain compatible.
