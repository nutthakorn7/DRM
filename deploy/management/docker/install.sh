#!/usr/bin/env bash
# First-time install of the zcrDRM management server (Docker stack: app + Postgres + Caddy).
#
#   sudo DOMAIN=drm.example.com ./install.sh
#
# Installs Docker if missing, generates .env with fresh secrets (once), opens the
# firewall, then hands off to deploy.sh (which builds, starts, waits for healthy,
# and prints a clear ✅/❌). Safe to re-run. For later upgrades, just run ./deploy.sh
# (or ship.sh from your dev machine) — you do NOT re-run install.sh.
set -euo pipefail

[ "${EUID:-$(id -u)}" -eq 0 ] || { echo "Run as root: sudo DOMAIN=drm.example.com $0" >&2; exit 1; }
DOMAIN="${DOMAIN:-}"
[ -n "$DOMAIN" ] || { echo "Set DOMAIN. Example: sudo DOMAIN=drm.example.com $0" >&2; exit 2; }

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
cd "$SCRIPT_DIR"

# 1. Docker Engine + Compose v2
if ! command -v docker >/dev/null 2>&1; then
  echo "==> Installing Docker Engine (get.docker.com)"
  curl -fsSL https://get.docker.com | sh
fi
docker compose version >/dev/null 2>&1 \
  || { echo "❌ Docker Compose v2 plugin not available. Install it, then re-run." >&2; exit 1; }

# 2. .env — generate once with fresh secrets; NEVER overwrite an existing one.
if [ ! -f .env ]; then
  echo "==> Generating .env with fresh secrets"
  umask 077
  # Master key must stay valid base64 (the app decodes it). Postgres password is
  # stripped to URL-safe chars so it can't break the connection string.
  sed \
    -e "s|^DOMAIN=.*|DOMAIN=$DOMAIN|" \
    -e "s|^DRM_MASTER_KEY_BASE64=.*|DRM_MASTER_KEY_BASE64=$(openssl rand -base64 32)|" \
    -e "s|^DRM_ADMIN_API_KEY=.*|DRM_ADMIN_API_KEY=$(openssl rand -hex 32)|" \
    -e "s|^DRM_CLIENT_API_KEY=.*|DRM_CLIENT_API_KEY=$(openssl rand -hex 32)|" \
    -e "s|^DRM_TRAILER_SECRET=.*|DRM_TRAILER_SECRET=$(openssl rand -hex 32)|" \
    -e "s|^DRM_AUDIT_CHAIN_KEY=.*|DRM_AUDIT_CHAIN_KEY=$(openssl rand -hex 32)|" \
    -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=$(openssl rand -base64 24 | tr -d '/+=')|" \
    .env.example > .env
  echo
  echo "    .env created at $SCRIPT_DIR/.env"
  echo "    ⚠️  BACK UP DRM_MASTER_KEY_BASE64 NOW (1Password/Vault/etc.) —"
  echo "        losing it means losing access to ALL encrypted data."
  echo
else
  echo "==> .env already present — keeping existing secrets"
  # Keep DOMAIN in sync if the caller passed a different one on re-run.
  grep -qE "^DOMAIN=$DOMAIN$" .env || echo "    (note: .env DOMAIN differs from \$DOMAIN; leaving .env as-is)"
fi

# 3. Firewall (best-effort; skipped if ufw absent)
if command -v ufw >/dev/null 2>&1; then
  echo "==> Opening firewall (22/80/443)"
  ufw allow 22/tcp; ufw allow 80/tcp; ufw allow 443/tcp; ufw --force enable
fi

# 4. Build + start + verify (clear ✅/❌ — no log-tailing)
echo "==> Handing off to deploy.sh"
exec "$SCRIPT_DIR/deploy.sh"
