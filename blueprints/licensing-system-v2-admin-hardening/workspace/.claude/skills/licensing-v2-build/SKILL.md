# Licensing v2 Build

Use when implementing the LicensingSystem v2 admin hardening blueprint.

1. Open `blueprints/licensing-system-v2-admin-hardening/tasks.json`.
2. Select the first pending task whose dependencies are done.
3. Mark it `in_progress`.
4. Implement only the task scope.
5. Run every command in `verify`.
6. Update `docs/v2-admin-hardening-status.md`.
7. Mark the task `done` only if verification is green.
8. Commit and tag with the task checkpoint if the user asks for commits.

Never store secrets in files. Never change frozen API routes/envelope/result codes.
