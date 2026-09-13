#!/usr/bin/env bash
# infra/deploy.sh — publish LicensingAdmin + LicensingApi and ship them to the LXC
# provisioned by infra/provision.sh. Run from the build machine (this repo checkout),
# NOT on the LXC itself — the LXC only has the ASP.NET Core runtime, not the SDK.
#
# Usage:
#   LXC_HOST=10.11.1.41 ./infra/deploy.sh
#
# First-time setup on the LXC (do this once, manually, before the first deploy):
#   - infra/provision.sh has already run (nginx, .NET runtime, deploy user, directories).
#   - Copy infra/systemd/*.service to /etc/systemd/system/ on the LXC.
#   - Create /etc/licensing-admin/env and /etc/licensing-api/env (root:deploy, 640) —
#     see infra/README.md for exactly what each needs. These are NOT managed by this
#     script and NEVER committed to git; write them by hand over SSH once.
#   - systemctl daemon-reload && systemctl enable licensing-admin licensing-api
#
# This script only republishes app code — it never touches the EnvironmentFile
# secrets or the systemd unit definitions, so re-running it is safe at any time.

set -euo pipefail

LXC_HOST="${LXC_HOST:?Set LXC_HOST to the container's IP or hostname, e.g. LXC_HOST=10.11.1.41}"
DEPLOY_USER="${DEPLOY_USER:-deploy}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="$REPO_ROOT/publish"

echo "==> publishing LicensingAdmin (framework-dependent, linux-x64)"
rm -rf "$PUBLISH_DIR/admin"
dotnet publish "$REPO_ROOT/LicensingAdmin/LicensingAdmin.csproj" \
  -c Release -o "$PUBLISH_DIR/admin" --self-contained false -r linux-x64

echo "==> publishing LicensingApi (framework-dependent, linux-x64)"
rm -rf "$PUBLISH_DIR/api"
dotnet publish "$REPO_ROOT/LicensingApi/LicensingApi.csproj" \
  -c Release -o "$PUBLISH_DIR/api" --self-contained false -r linux-x64

echo "==> stopping services on $LXC_HOST"
ssh "root@$LXC_HOST" "systemctl stop licensing-admin licensing-api || true"

echo "==> syncing published output"
rsync -az --delete "$PUBLISH_DIR/admin/" "$DEPLOY_USER@$LXC_HOST:/var/www/licensing/admin/"
rsync -az --delete "$PUBLISH_DIR/api/" "$DEPLOY_USER@$LXC_HOST:/var/www/licensing/api/"

echo "==> starting services on $LXC_HOST"
ssh "root@$LXC_HOST" "systemctl daemon-reload && systemctl start licensing-admin licensing-api && systemctl --no-pager status licensing-admin licensing-api"

echo "==> done. Admin: http://$LXC_HOST/  API: http://$LXC_HOST:8080/"
