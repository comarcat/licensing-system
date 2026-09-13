#!/usr/bin/env bash
# infra/provision.sh — one-time setup for a fresh Debian 13 ("trixie") LXC that will
# host LicensingAdmin (Blazor Server) and LicensingApi (ASP.NET Core Web API).
# Run ONCE, as root, on the LXC itself (not the build machine):
#   ssh root@<lxc-ip> 'bash -s' < infra/provision.sh
#
# Prefer running it as a transient systemd unit instead of directly over the SSH
# session — many systemd-logind configurations kill all of a user's processes when
# their login session ends (KillUserProcesses), which SIGHUPs a plain `nohup ... &`
# mid-install if the client disconnects. A transient unit survives that:
#   scp infra/provision.sh root@<lxc-ip>:/root/provision.sh
#   ssh root@<lxc-ip> "systemd-run --unit=provision --collect \
#     --setenv=DEPLOY_PUBKEY='<pubkey>' \
#     bash -c 'bash /root/provision.sh > /root/provision.log 2>&1'"
#   ssh root@<lxc-ip> "journalctl -u provision.service --no-pager"   # check status/log after
#
# What this does NOT do, on purpose:
#   - Does NOT install PostgreSQL server. The database is a separate, already-running
#     server (see the connection string in each service's EnvironmentFile, set up in
#     infra/deploy.sh, never in this script). It does install postgresql-client, for
#     ad-hoc pg_dump/psql access to that remote server from this box.
#   - Does NOT install the .NET SDK, only the ASP.NET Core *runtime*. Build/publish
#     happens on the build machine (or CI); this box only runs the published output.
#   - Does NOT clone the repo, publish the apps, or write the systemd unit files for
#     licensing-admin/licensing-api — that is infra/deploy.sh's job, run afterwards
#     from the build machine.
#   - Does NOT configure TLS/certbot. This LXC is reached over the LAN by IP
#     (no public DNS name yet) — plain HTTP behind nginx is fine for now. Revisit if
#     this ever becomes internet-facing.
#
# Safe to re-run: every step below is idempotent (apt install is a no-op on an
# installed package, mkdir -p never fails on an existing dir, the nginx vhost is
# overwritten with the same content, systemctl enable is idempotent).

set -euo pipefail

export HOME="${HOME:-/root}"

DEPLOY_USER="${DEPLOY_USER:-deploy}"
DEPLOY_PUBKEY="${DEPLOY_PUBKEY:-}"   # optional: an SSH public key to authorize for $DEPLOY_USER
DOTNET_VERSION="${DOTNET_VERSION:-8.0}"
DOTNET_INSTALL_DIR="${DOTNET_INSTALL_DIR:-/usr/share/dotnet}"

echo "==> apt update/upgrade"
export DEBIAN_FRONTEND=noninteractive
apt-get update -y
apt-get upgrade -y

echo "==> installing nginx, git, curl, unzip, ufw, ca-certificates, gnupg, postgresql-client, libicu (needed by .NET's globalization)"
apt-get install -y \
  nginx \
  git curl unzip ca-certificates gnupg ufw \
  postgresql-client \
  libicu-dev libssl-dev libkrb5-3 zlib1g

# ASP.NET Core Runtime via Microsoft's official dotnet-install.sh script rather than
# apt (packages.microsoft.com's Debian feed lags new releases like trixie — this
# script is Microsoft-maintained, works on any glibc Linux regardless of how mature
# that distro's own apt feed is, and is what to reach for whenever the apt route is
# uncertain). Installs the RUNTIME only (no SDK — nothing is built on this box).
if [ ! -x "$DOTNET_INSTALL_DIR/dotnet" ] || ! "$DOTNET_INSTALL_DIR/dotnet" --list-runtimes 2>/dev/null | grep -q "Microsoft.AspNetCore.App $DOTNET_VERSION"; then
  echo "==> installing .NET $DOTNET_VERSION ASP.NET Core Runtime"
  curl -fsSL -o /tmp/dotnet-install.sh https://dot.net/v1/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel "$DOTNET_VERSION" --runtime aspnetcore --install-dir "$DOTNET_INSTALL_DIR"
  rm -f /tmp/dotnet-install.sh
fi
ln -sf "$DOTNET_INSTALL_DIR/dotnet" /usr/local/bin/dotnet

echo "==> deploy user"
if ! id -u "$DEPLOY_USER" >/dev/null 2>&1; then
  useradd --create-home --shell /bin/bash "$DEPLOY_USER"
fi
mkdir -p "/home/$DEPLOY_USER/.ssh"
chmod 700 "/home/$DEPLOY_USER/.ssh"
if [ -n "$DEPLOY_PUBKEY" ]; then
  touch "/home/$DEPLOY_USER/.ssh/authorized_keys"
  grep -qxF "$DEPLOY_PUBKEY" "/home/$DEPLOY_USER/.ssh/authorized_keys" || \
    echo "$DEPLOY_PUBKEY" >> "/home/$DEPLOY_USER/.ssh/authorized_keys"
  chmod 600 "/home/$DEPLOY_USER/.ssh/authorized_keys"
fi
chown -R "$DEPLOY_USER:$DEPLOY_USER" "/home/$DEPLOY_USER/.ssh"

echo "==> app + secrets directories"
mkdir -p /var/www/licensing/admin /var/www/licensing/api
mkdir -p /etc/licensing-admin /etc/licensing-api
chown -R "$DEPLOY_USER:$DEPLOY_USER" /var/www/licensing
# EnvironmentFile dirs: root-owned, group-readable only by the app's own service
# account, since these will hold the Postgres connection string and the RSA signing
# key (infra/deploy.sh writes the files themselves — this just makes the directories).
chmod 750 /etc/licensing-admin /etc/licensing-api
chown root:"$DEPLOY_USER" /etc/licensing-admin /etc/licensing-api

echo "==> nginx reverse proxy vhosts (licensing-admin: 5000, licensing-api: 5001)"
cat > /etc/nginx/sites-available/licensing-admin.conf <<'NGINX'
server {
    listen 80;
    server_name _;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_cache_bypass $http_upgrade;
        # Blazor Server needs a long-lived SignalR websocket, not the default 60s.
        proxy_read_timeout 100s;
    }
}
NGINX

cat > /etc/nginx/sites-available/licensing-api.conf <<'NGINX'
server {
    listen 8080;
    server_name _;

    location / {
        proxy_pass http://127.0.0.1:5001;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
NGINX

ln -sf /etc/nginx/sites-available/licensing-admin.conf /etc/nginx/sites-enabled/licensing-admin.conf
ln -sf /etc/nginx/sites-available/licensing-api.conf /etc/nginx/sites-enabled/licensing-api.conf
rm -f /etc/nginx/sites-enabled/default
nginx -t
systemctl enable nginx
systemctl restart nginx

echo "==> firewall (OpenSSH + nginx's two ports; app ports 5000/5001 only on loopback, never exposed)"
ufw allow OpenSSH
ufw allow 80/tcp
ufw allow 8080/tcp
ufw --force enable

echo "==> done. Next: run infra/deploy.sh from the build machine to publish and start the apps."
