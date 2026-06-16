#!/usr/bin/env bash
# Build + (re)deploy the zcrDRM Docker stack — with a clear done-signal.
#
#   ./deploy.sh              # from anywhere inside the repo, on the server
#
# Idempotent: handles both the first bring-up and every upgrade. The script
# EXITS when the stack is healthy and verified (✅) or when it fails (❌) — it
# does NOT tail logs. `docker compose up -d --wait` is the health gate, so the
# operator never has to guess whether the deploy finished.
#
# On an upgrade it first takes a Postgres backup and tags the current image
# `drm-server:rollback-<ts>`, so a bad deploy is one command to undo.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"   # deploy/management/docker
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd -P)"
cd "$SCRIPT_DIR"

SERVICE="drm-server"
TS="$(date +%Y%m%d-%H%M%S)"
VERSION="$(cat "$REPO_ROOT/VERSION" 2>/dev/null || echo unknown)"
IMAGE_TAG="${DRM_IMAGE_TAG:-local}"
BACKUP_DIR="${DRM_BACKUP_DIR:-/opt/drm-backups}"

[ -f .env ] || { echo "❌ .env missing in $SCRIPT_DIR — run install.sh first." >&2; exit 1; }
docker compose version >/dev/null 2>&1 || { echo "❌ 'docker compose' (v2) not found." >&2; exit 1; }

envval() { grep -E "^$1=" .env | head -1 | cut -d= -f2-; }

echo "==> Deploying zcrDRM $VERSION  (build $TS, image tag '$IMAGE_TAG')"

# --- Upgrade safety net: backup DB + tag rollback image (skipped on first install) ---
PG_CID="$(docker compose ps -q postgres 2>/dev/null || true)"
IS_UPGRADE=0
if [ -n "$PG_CID" ] && [ "$(docker inspect "$PG_CID" --format '{{.State.Running}}' 2>/dev/null || echo false)" = "true" ]; then
  IS_UPGRADE=1
  echo "==> Existing stack detected — taking a pre-deploy Postgres backup"
  mkdir -p "$BACKUP_DIR"
  PGDB="$(envval POSTGRES_DB)";   PGDB="${PGDB:-drm}"
  PGUSER="$(envval POSTGRES_USER)"; PGUSER="${PGUSER:-drm}"
  PGPW="$(envval POSTGRES_PASSWORD)"
  if docker exec -e PGPASSWORD="$PGPW" "$PG_CID" pg_dump -U "$PGUSER" -d "$PGDB" > "$BACKUP_DIR/predeploy-$TS.sql" 2>/dev/null \
       && [ -s "$BACKUP_DIR/predeploy-$TS.sql" ]; then
    echo "    backup: $BACKUP_DIR/predeploy-$TS.sql ($(du -h "$BACKUP_DIR/predeploy-$TS.sql" | cut -f1))"
  elif [ "${ALLOW_NO_BACKUP:-0}" = "1" ]; then
    echo "    ⚠️  backup failed but ALLOW_NO_BACKUP=1 — continuing"
  else
    echo "❌ Pre-deploy DB backup failed. Fix DB access, or re-run with ALLOW_NO_BACKUP=1 to override." >&2
    exit 1
  fi
  if docker image inspect "drm-server:$IMAGE_TAG" >/dev/null 2>&1; then
    docker tag "drm-server:$IMAGE_TAG" "drm-server:rollback-$TS"
    echo "    rollback image: drm-server:rollback-$TS"
  fi
fi

ROLLBACK_HINT="docker tag drm-server:rollback-$TS drm-server:$IMAGE_TAG && docker compose up -d --force-recreate $SERVICE"

# --- Build (a failed build leaves the running image untouched) ---
echo "==> Building image"
docker compose build "$SERVICE"

# --- Bring up with the health gate. --wait blocks until healthy / fails non-zero. ---
echo "==> Starting and waiting for healthy…"
if ! docker compose up -d --wait "$SERVICE"; then
  echo "❌ DEPLOY FAILED — $SERVICE did not become healthy." >&2
  echo "   inspect: docker compose logs --tail 80 $SERVICE" >&2
  [ "$IS_UPGRADE" = "1" ] && echo "   rollback: $ROLLBACK_HINT" >&2
  exit 1
fi

# --- Verify (the --wait above already proved container health; this confirms the public edge) ---
DOMAIN="$(envval DOMAIN)"
HC="$(curl -fsS -o /dev/null -w '%{http_code}' --max-time 10 "https://${DOMAIN}/healthz" 2>/dev/null || echo "n/a")"

echo
echo "================================================================"
echo "  ✅ DEPLOY OK — zcrDRM $VERSION is live and healthy"
echo "     container: healthy   public https://${DOMAIN}/healthz: $HC"
echo "     admin console: https://${DOMAIN}/admin/"
if [ "$HC" != "200" ]; then
  echo "     ⚠️  public healthz != 200 (got '$HC') — the container is healthy, so this is"
  echo "        usually DNS/Let's-Encrypt still settling on a first install. Re-check in a minute:"
  echo "        curl https://${DOMAIN}/healthz"
fi
[ "$IS_UPGRADE" = "1" ] && echo "     rollback if needed: $ROLLBACK_HINT"
echo "================================================================"
