# Secret scanning

Every Miautrix repo blocks credentials at two boundaries. Both run the same
script, so what stops your commit is exactly what stops the build.

## One command

```sh
./scripts/install-hooks.sh
```

That is the whole setup. It points `core.hooksPath` at the tracked `.githooks/`
directory, so `git commit` now refuses any commit containing a detected secret.

Install `gitleaks` first — the hook is failure-closed and will refuse commits
until it exists:

| Platform      | Command                              |
| ------------- | ------------------------------------ |
| Debian/Ubuntu | `sudo apt-get install gitleaks`      |
| macOS         | `brew install gitleaks`              |
| Windows       | `winget install gitleaks.gitleaks`   |
| Any           | <https://github.com/gitleaks/gitleaks/releases> |

## Scanning by hand

```sh
./scripts/secret-scan.sh staged     # what you are about to commit
./scripts/secret-scan.sh worktree   # files on disk now
./scripts/secret-scan.sh history    # every commit ever
./scripts/secret-scan-selftest.sh   # prove the scanner still works
```

Exit codes: `0` clean, `2` secret found, `1` scanner could not run.

## Failure-closed

`1` and `2` are both failures. A missing binary, a missing or malformed
`.gitleaks.toml`, or an unreadable repo fails the check — a scan that could not
run is never reported as a pass. Verified by `secret-scan-selftest.sh`.

The self-test also plants a real-shaped credential and asserts it is caught. This
is the guard against the failure mode that matters most: an allowlist widened one
exception at a time until everything passes and every build is green.

## CI

`.github/workflows/secret-scan.yml` runs on every branch push and pull request, on
both `ubuntu-latest` and `windows-latest`. It checks out full history
(`fetch-depth: 0` — a shallow clone would scan only the tip and call a repo with a
buried secret clean), installs a version-pinned `gitleaks` verified by SHA-256,
then scans history, the working tree, and the canary.

The hook is a courtesy a developer can skip with `--no-verify`. CI is the
enforcement boundary.

## Allowlist

`.gitleaks.toml` is shared verbatim across all repos — do not fork it per repo, or
the same value gets waved through in one place and caught in another.

Every entry names the repo and file it came from and why it is not a credential.
If you add one, add that evidence. Scoped exact values, never blanket format
allows: the test licence fixtures are allowlisted individually so a genuine
customer key is still caught.

Never use `git commit --no-verify` to get past a finding. If it is real, rotate the
credential and move it into the runtime environment. If it is not, allowlist it
with evidence.

## Rollback

```sh
git config --unset core.hooksPath
```

Local only — touches this clone's `.git/config`, changes no tracked file, and
leaves CI enforcement in place. To roll back CI, revert the commit that added
`.github/workflows/secret-scan.yml`.
