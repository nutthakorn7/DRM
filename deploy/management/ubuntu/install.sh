#!/usr/bin/env bash
# One-shot installer for DRM management server on Ubuntu 22.04+ / Debian 12+.
# Installs: .NET 10 runtime, PostgreSQL 16, Caddy, systemd service.
#
# Usage:
#   sudo DOMAIN=drm.example.com ./install.sh
#
# After install, edit /etc/drm/env to add your master keys, then:
#   sudo systemctl restart drm-management
set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Run as root: sudo $0" >&2
  exit 1
fi

DOMAIN="${DOMAIN:-}"
if [[ -z "$DOMAIN" ]]; then
  echo "Set DOMAIN env var. Example: sudo DOMAIN=drm.example.com $0" >&2
  exit 2
fi

DRM_USER="${DRM_USER:-drm}"
DRM_HOME="${DRM_HOME:-/opt/drm}"
DRM_DATA_DIR="${DRM_DATA_DIR:-/var/lib/drm-management}"
PG_DB="${PG_DB:-drm}"
PG_USER="${PG_USER:-drm}"
PG_PASSWORD="${PG_PASSWORD:-$(openssl rand -hex 24)}"
ARTIFACT_DIR="${ARTIFACT_DIR:-$DRM_HOME/server}"

echo "==> Updating apt"
apt-get update -qq

echo "==> Installing .NET 10 runtime + Postgres 16 + Caddy + tooling"
# Microsoft .NET repo
if ! command -v dotnet >/dev/null 2>&1; then
  # shellcheck disable=SC1091
  UBUNTU_VERSION="$(. /etc/os-release && echo "$VERSION_ID")"
  wget -q "https://packages.microsoft.com/config/ubuntu/${UBUNTU_VERSION}/packages-microsoft-prod.deb" \
    -O /tmp/ms-prod.deb
  dpkg -i /tmp/ms-prod.deb
  apt-get update -qq
  apt-get install -y aspnetcore-runtime-10.0
fi

# Postgres
apt-get install -y postgresql postgresql-contrib

# Caddy
if ! command -v caddy >/dev/null 2>&1; then
  apt-get install -y debian-keyring debian-archive-keyring apt-transport-https curl
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' \
    | gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
  curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' \
    > /etc/apt/sources.list.d/caddy-stable.list
  apt-get update -qq
  apt-get install -y caddy
fi

apt-get install -y ufw openssl

echo "==> Creating drm system user"
if ! id -u "$DRM_USER" >/dev/null 2>&1; then
  useradd --system --home-dir "$DRM_HOME" --shell /usr/sbin/nologin "$DRM_USER"
fi
mkdir -p "$DRM_HOME" "$DRM_DATA_DIR" /etc/drm
chown -R "$DRM_USER:$DRM_USER" "$DRM_HOME" "$DRM_DATA_DIR"
chmod 750 /etc/drm

echo "==> Provisioning Postgres database '$PG_DB' / user '$PG_USER'"
sudo -u postgres psql -tAc "SELECT 1 FROM pg_roles WHERE rolname='$PG_USER'" | grep -q 1 || \
  sudo -u postgres psql -c "CREATE ROLE $PG_USER LOGIN PASSWORD '$PG_PASSWORD';"
sudo -u postgres psql -tAc "SELECT 1 FROM pg_database WHERE datname='$PG_DB'" | grep -q 1 || \
  sudo -u postgres psql -c "CREATE DATABASE $PG_DB OWNER $PG_USER;"

echo "==> Writing /etc/drm/env (you must fill in master keys before first start)"
if [[ ! -f /etc/drm/env ]]; then
  MASTER_KEY="$(openssl rand -base64 32)"
  ADMIN_KEY="$(openssl rand -hex 32)"
  CLIENT_KEY="$(openssl rand -hex 32)"
  cat > /etc/drm/env <<EOF
# DRM management server environment.
# This file holds production secrets. Do NOT commit. Do NOT rotate
# DRM_KEY_WRAPPING_MASTER_KEY_BASE64 after data has been written —
# stored tenant wrapping keys are encrypted with it.

DRM_KEY_WRAPPING_MASTER_KEY_BASE64=$MASTER_KEY
DRM_ADMIN_API_KEY=$ADMIN_KEY
DRM_CLIENT_API_KEY=$CLIENT_KEY

# Postgres connection. Host= prefix tells the server to use Npgsql.
ConnectionStrings__DrmDb=Host=127.0.0.1;Port=5432;Database=$PG_DB;Username=$PG_USER;Password=$PG_PASSWORD

# Bind only to localhost; Caddy fronts TLS on 443.
ASPNETCORE_URLS=http://127.0.0.1:5080
ASPNETCORE_ENVIRONMENT=Production
Drm__Mode=OnPrem
DRM_DATA_DIR=$DRM_DATA_DIR
DRM_SERVER_DIR=$ARTIFACT_DIR
EOF
  chown root:"$DRM_USER" /etc/drm/env
  chmod 640 /etc/drm/env
  echo
  echo "    /etc/drm/env created with fresh secrets. Back this file up NOW —"
  echo "    losing DRM_KEY_WRAPPING_MASTER_KEY_BASE64 means losing access to all"
  echo "    encrypted tenant keys."
  echo
fi

echo "==> Installing systemd unit"
install -m 644 "$(dirname "$0")/drm-management.service" /etc/systemd/system/drm-management.service
systemctl daemon-reload
systemctl enable drm-management

echo "==> Installing Caddyfile for $DOMAIN"
cat > /etc/caddy/Caddyfile <<EOF
$DOMAIN {
    reverse_proxy 127.0.0.1:5080
    encode gzip zstd
    header {
        # HSTS — only after you're sure the cert is good.
        Strict-Transport-Security "max-age=31536000; includeSubDomains"
        X-Content-Type-Options "nosniff"
        X-Frame-Options "DENY"
        Referrer-Policy "strict-origin-when-cross-origin"
    }
    log {
        output file /var/log/caddy/drm-access.log
    }
}
EOF
systemctl restart caddy

echo "==> Configuring UFW firewall"
ufw allow 22/tcp
ufw allow 80/tcp
ufw allow 443/tcp
ufw --force enable

echo
echo "================================================================"
echo "  Install complete."
echo
echo "  Next steps:"
echo "    1. Copy the published server artifact to $ARTIFACT_DIR"
echo "       (rsync from your build machine):"
echo "         rsync -av artifacts/drm-management/ root@$(hostname):$ARTIFACT_DIR/"
echo "    2. chown -R $DRM_USER:$DRM_USER $ARTIFACT_DIR"
echo "    3. systemctl start drm-management"
echo "    4. systemctl status drm-management"
echo "    5. curl https://$DOMAIN/healthz"
echo
echo "  Admin console: https://$DOMAIN/admin/"
echo "  Admin API key is in /etc/drm/env (DRM_ADMIN_API_KEY)"
echo "================================================================"
