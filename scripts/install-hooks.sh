#!/bin/sh
# Miautrix hook installer — MIA-22
#
# One command, no ceremony:
#
#     ./scripts/install-hooks.sh
#
# Points git at the version-controlled .githooks/ directory instead of copying
# files into .git/hooks. That matters: .git/hooks is untracked, so a copied hook
# cannot be reviewed, silently rots when the source changes, and every fresh
# clone starts with no protection at all. core.hooksPath means updating
# .githooks/pre-commit in a commit updates the hook everyone runs.
#
# Rollback:
#
#     git config --unset core.hooksPath
#
# That is the entire rollback. It touches only this clone's .git/config, changes
# no tracked file, and leaves the CI job — the actual enforcement boundary —
# untouched. Blast radius is one developer's working copy.
#
# Requires git >= 2.9 for core.hooksPath (2016); checked below.

set -eu

REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || {
  printf 'install-hooks: not inside a git repository\n' >&2
  exit 1
}
cd "$REPO_ROOT"

# core.hooksPath landed in git 2.9. Fail loudly rather than appearing to succeed
# and leaving the developer unprotected.
GIT_VERSION=$(git --version | sed 's/[^0-9.]*\([0-9][0-9.]*\).*/\1/')
GIT_MAJOR=$(printf '%s' "$GIT_VERSION" | cut -d. -f1)
GIT_MINOR=$(printf '%s' "$GIT_VERSION" | cut -d. -f2)
if [ "$GIT_MAJOR" -lt 2 ] || { [ "$GIT_MAJOR" -eq 2 ] && [ "$GIT_MINOR" -lt 9 ]; }; then
  printf 'install-hooks: git %s is too old; core.hooksPath needs 2.9+\n' "$GIT_VERSION" >&2
  exit 1
fi

git config core.hooksPath .githooks

# Git on Unix requires the execute bit. Git for Windows ignores it, and chmod may
# not exist there, so this is best-effort by design rather than a hard step.
if command -v chmod >/dev/null 2>&1; then
  chmod +x .githooks/* scripts/*.sh 2>/dev/null || true
fi

printf 'install-hooks: core.hooksPath -> .githooks\n'

if command -v gitleaks >/dev/null 2>&1 || [ -x "$REPO_ROOT/.gitleaks-bin/gitleaks" ]; then
  printf 'install-hooks: gitleaks found; pre-commit secret scanning is active.\n'
else
  # Not a failure of the install — the hook is correctly wired. But say plainly
  # that commits will now be REFUSED until gitleaks exists, because the hook is
  # failure-closed and a developer who does not know that will read it as a bug.
  printf '\ninstall-hooks: WARNING — gitleaks is not installed.\n' >&2
  printf 'The hook is failure-closed, so commits will be REFUSED until you install it:\n' >&2
  printf '  Debian/Ubuntu : sudo apt-get install gitleaks\n' >&2
  printf '  macOS         : brew install gitleaks\n' >&2
  printf '  Windows       : winget install gitleaks.gitleaks\n' >&2
  printf '  Any platform  : https://github.com/gitleaks/gitleaks/releases\n' >&2
fi

printf 'install-hooks: verify with  ./scripts/secret-scan.sh worktree\n'
printf 'install-hooks: rollback with  git config --unset core.hooksPath\n'
