#!/bin/sh
# Miautrix secret-scanner self-test — MIA-22
#
#     ./scripts/secret-scan-selftest.sh
#
# Asserts the two properties that actually matter and that a plain "scan passed"
# cannot distinguish:
#
#   1. DETECTION — a real credential is still caught. An allowlist that has been
#      widened one exception at a time eventually allows everything while every
#      build stays green. This fails loudly when that happens.
#   2. FAILURE-CLOSED — when the scanner cannot run (missing binary, missing or
#      broken config) the result is a failure, never a pass. "Could not look" must
#      never be reported as "nothing there".
#
# CI runs the detection half inline; this script is the version a developer can run
# locally, and it covers the failure-closed half as well. POSIX sh, so it behaves
# the same on Linux, macOS, and Windows under Git for Windows' bash.

set -eu

REPO_ROOT=$(git rev-parse --show-toplevel)
cd "$REPO_ROOT"
SCAN="$REPO_ROOT/scripts/secret-scan.sh"
FAILURES=0

expect() {
  want="$1"; got="$2"; label="$3"
  if [ "$got" -eq "$want" ]; then
    printf 'ok    %s (exit %s)\n' "$label" "$got"
  else
    printf 'FAIL  %s (expected exit %s, got %s)\n' "$label" "$want" "$got" >&2
    FAILURES=$((FAILURES + 1))
  fi
}

# Records the exit status in STATUS instead of returning it. Returning it would
# trip `set -e` on every intentionally-failing case below.
STATUS=0
run() {
  set +e
  "$@" >/dev/null 2>&1
  STATUS=$?
  set -e
}

# --- 1. detection ------------------------------------------------------------
# Token assembled from fragments so this file contains no matchable secret and
# therefore does not trip the scan of the repo it is protecting.
CANARY_DIR=$(mktemp -d)
trap 'rm -rf "$CANARY_DIR"' EXIT INT TERM
prefix="gh"; mid="p_"; body="1234567890abcdefghijklmnopqrstuvwxyzAB"
printf 'token = "%s%s%s"\n' "$prefix" "$mid" "$body" > "$CANARY_DIR/canary.txt"

if [ -n "${GITLEAKS_BIN:-}" ]; then GL="$GITLEAKS_BIN"
elif [ -x "$REPO_ROOT/.gitleaks-bin/gitleaks" ]; then GL="$REPO_ROOT/.gitleaks-bin/gitleaks"
elif [ -x "$REPO_ROOT/.gitleaks-bin/gitleaks.exe" ]; then GL="$REPO_ROOT/.gitleaks-bin/gitleaks.exe"
else GL=gitleaks
fi

run "$GL" detect --no-git --source "$CANARY_DIR" --config .gitleaks.toml \
  --no-banner --redact --exit-code 2 --log-level error
expect 2 "$STATUS" "detects a planted credential"

# --- 2. failure-closed -------------------------------------------------------
run env GITLEAKS_BIN=/nonexistent/gitleaks "$SCAN" worktree
expect 1 "$STATUS" "fails closed when gitleaks is missing"

mv .gitleaks.toml .gitleaks.toml.selftest-bak
run "$SCAN" worktree
expect 1 "$STATUS" "fails closed when .gitleaks.toml is missing"
mv .gitleaks.toml.selftest-bak .gitleaks.toml

run "$SCAN" not-a-real-mode
expect 1 "$STATUS" "rejects an unknown mode"

# --- 3. the repo itself is clean ---------------------------------------------
run "$SCAN" worktree
expect 0 "$STATUS" "working tree is clean"

run "$SCAN" history
expect 0 "$STATUS" "full history is clean"

if [ "$FAILURES" -ne 0 ]; then
  printf '\nsecret-scan-selftest: %s check(s) FAILED\n' "$FAILURES" >&2
  exit 1
fi
printf '\nsecret-scan-selftest: all checks passed\n'
