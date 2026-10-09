# LicensingSystem v2 Admin Hardening Builder Notes

This workspace file is additive. Merge with the repository root `CLAUDE.md`; do not replace it.

## Task protocol

- Use `blueprints/licensing-system-v2-admin-hardening/tasks.json` as the source of truth.
- Pick the first `pending` task whose dependencies are `done`.
- Set status to `in_progress` before editing and `done` only after all verify commands pass.
- Update `docs/v2-admin-hardening-status.md` for every task.
- Commit one task at a time and tag with the task checkpoint.

## Hard rules

- Never commit secrets, SMTP passwords, PEMs, or connection strings.
- Preserve `/api/activate`, `/api/checkin`, `/health`, `ResultCode`, license envelope, and license-key regex.
- All admin mutations write `AuditLogEntry` with actor email from `CurrentAdmin.Email(user)`.
- Final gate: `dotnet build LicensingSystem.sln && dotnet test`.
