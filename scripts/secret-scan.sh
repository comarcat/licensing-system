#!/bin/sh
# Miautrix secret scanner — MIA-22
#
# ONE code path for every place we scan for secrets. The pre-commit hook and the
# CI job both call this script, so what blocks your commit is exactly what blocks
# the build. Two separate invocations drift; this cannot.
#
# Usage:
#   scripts/secret-scan.sh staged      what is about to be committed (pre-commit hook)
#   scripts/secret-scan.sh history     entire git history (CI, and periodic audit)
#   scripts/secret-scan.sh worktree    files on disk, ignoring git (local spot check)
#
# Exit codes:
#   0  clean
#   2  secret found
#   1  scanner could not run (missing binary, bad config, unreadable repo, ...)
#
# FAILURE-CLOSED: 1 and 2 are both failures. A scanner that cannot run is never
# reported as a pass. gitleaks itself exits 1 on error, so we set its leak exit
# code to 2 to tell "found something" apart from "could not look" — without ever
# letting "could not look" collapse into a zero.
#
# PORTABILITY: POSIX sh, no bashisms, no GNU-only flags. This runs unchanged on
# Linux, on macOS, and on Windows Server via the sh that ships inside Git for
# Windows — which is also the shell git itself uses to run hooks there. Do not
# add [[ ]], arrays, `local`, or `readlink -f`.

set -eu

MODE="${1:-staged}"

# Resolve the repo root rather than trusting the caller's working directory.
# Hooks run from the root, but CI steps and editor integrations do not always.
REPO_ROOT=$(git rev-parse --show-toplevel 2>/dev/null) || {
  printf 'secret-scan: not inside a git repository\n' >&2
  exit 1
}
cd "$REPO_ROOT"

CONFIG="$REPO_ROOT/.gitleaks.toml"
if [ ! -f "$CONFIG" ]; then
  printf 'secret-scan: missing .gitleaks.toml at %s\n' "$CONFIG" >&2
  printf 'secret-scan: refusing to scan with an unknown ruleset (failure-closed)\n' >&2
  exit 1
fi

# gitleaks may be on PATH, vendored by CI into ./.gitleaks-bin, or installed by a
# developer's package manager. Never hard-code an absolute path.
if [ -n "${GITLEAKS_BIN:-}" ]; then
  GITLEAKS="$GITLEAKS_BIN"
elif [ -x "$REPO_ROOT/.gitleaks-bin/gitleaks" ]; then
  GITLEAKS="$REPO_ROOT/.gitleaks-bin/gitleaks"
elif [ -x "$REPO_ROOT/.gitleaks-bin/gitleaks.exe" ]; then
  GITLEAKS="$REPO_ROOT/.gitleaks-bin/gitleaks.exe"
elif command -v gitleaks >/dev/null 2>&1; then
  GITLEAKS=gitleaks
else
  printf 'secret-scan: gitleaks not found.\n' >&2
  printf '  Debian/Ubuntu : sudo apt-get install gitleaks\n' >&2
  printf '  macOS         : brew install gitleaks\n' >&2
  printf '  Windows       : winget install gitleaks.gitleaks\n' >&2
  printf '  Any platform  : https://github.com/gitleaks/gitleaks/releases\n' >&2
  printf 'secret-scan: cannot verify this change is secret-free (failure-closed)\n' >&2
  exit 1
fi

# gitleaks renamed its subcommands in 8.19 (detect -> git/dir, protect -> git
# --staged). The old names remain as deprecated aliases in 8.30, but not forever,
# and Debian trixie still ships 8.16 which only has the old ones. Probe rather
# than pinning ourselves to one era.
if "$GITLEAKS" git --help >/dev/null 2>&1; then
  CMD_STYLE=modern
else
  CMD_STYLE=legacy
fi

run_gitleaks() {
  # --exit-code 2 is what makes "found a secret" distinguishable from "broke".
  # --redact keeps the secret itself out of CI logs; a build log is a second
  # place to leak from.
  set -- --config "$CONFIG" --no-banner --redact --exit-code 2 "$@"
  if [ -n "${GITLEAKS_REPORT_PATH:-}" ]; then
    set -- --report-format json --report-path "$GITLEAKS_REPORT_PATH" "$@"
  fi
  "$GITLEAKS" "$@"
}

# `set -e` would abort before the exit code can be classified, so opt out around
# the call and inspect the status explicitly.
set +e
case "$MODE" in
  staged)
    printf 'secret-scan: scanning staged changes\n' >&2
    if [ "$CMD_STYLE" = modern ]; then
      run_gitleaks git --staged .
    else
      run_gitleaks protect --staged --source .
    fi
    ;;
  history)
    printf 'secret-scan: scanning full git history\n' >&2
    if [ "$CMD_STYLE" = modern ]; then
      run_gitleaks git .
    else
      run_gitleaks detect --source .
    fi
    ;;
  worktree)
    printf 'secret-scan: scanning working tree\n' >&2
    if [ "$CMD_STYLE" = modern ]; then
      run_gitleaks dir .
    else
      run_gitleaks detect --no-git --source .
    fi
    ;;
  *)
    printf 'secret-scan: unknown mode "%s" (expected staged|history|worktree)\n' "$MODE" >&2
    exit 1
    ;;
esac
STATUS=$?
set -e

if [ "$STATUS" -eq 0 ]; then
  printf 'secret-scan: clean\n' >&2
elif [ "$STATUS" -eq 2 ]; then
  printf '\nsecret-scan: SECRET DETECTED — see the findings above.\n' >&2
  printf 'If this is a real credential:\n' >&2
  printf '  1. Do NOT commit. Rotate the credential now; assume it is burned.\n' >&2
  printf '  2. Move the value into the runtime environment, not the repo.\n' >&2
  printf 'If it is a false positive, add an allowlist entry to .gitleaks.toml WITH\n' >&2
  printf 'evidence of why it is safe. Never use --no-verify to get past this.\n' >&2
else
  printf '\nsecret-scan: SCANNER FAILED (exit %s). Treating as a failure.\n' "$STATUS" >&2
  printf 'A scan that could not run is not a pass.\n' >&2
  STATUS=1
fi

exit "$STATUS"
