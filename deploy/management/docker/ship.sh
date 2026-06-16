#!/usr/bin/env bash
# Ship the current repo to a server and deploy it — ONE command from your dev machine.
#
#   ./ship.sh root@drm.zcr.ai               # ships HEAD
#   ./ship.sh root@drm.zcr.ai origin/master # ships a specific ref
#
# The server has no git checkout, so this archives the chosen ref, uploads it,
# extracts it into a fresh timestamped dir, preserves the live .env (secrets),
# swaps it into place (snapshotting the old tree for rollback), and runs
# deploy.sh there — which builds, waits for healthy, verifies, and prints ✅/❌.
set -euo pipefail

HOST="${1:?usage: ./ship.sh user@host [git-ref]}"
REF="${2:-HEAD}"
REMOTE_DIR="${REMOTE_DIR:-/opt/drm}"
ENV_REL="deploy/management/docker/.env"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../../.." && pwd -P)"
TS="$(date +%Y%m%d-%H%M%S)"
SHA="$(git -C "$REPO_ROOT" rev-parse --short "$REF")"
TARBALL="/tmp/drm-ship-$TS.tgz"

echo "==> Packaging $REF ($SHA)"
git -C "$REPO_ROOT" archive --format=tar "$REF" | gzip > "$TARBALL"
trap 'rm -f "$TARBALL"' EXIT

echo "==> Uploading to $HOST"
scp -q "$TARBALL" "$HOST:/tmp/drm-ship-$TS.tgz"

echo "==> Extracting + deploying on $HOST"
# stdin is the heredoc (the remote script); the tarball was uploaded via scp,
# so there's no stdin conflict. Vars are passed via the env prefix and expanded
# remotely (heredoc is quoted, so nothing expands locally).
ssh "$HOST" "REMOTE_DIR='$REMOTE_DIR' ENV_REL='$ENV_REL' TS='$TS' bash -s" <<'REMOTE'
set -euo pipefail
TGZ="/tmp/drm-ship-${TS}.tgz"
NEW="${REMOTE_DIR}.ship-${TS}"
mkdir -p "$NEW"
tar -xzf "$TGZ" -C "$NEW"
rm -f "$TGZ"
# Preserve the live secrets (.env is never in git).
if [ -f "$REMOTE_DIR/$ENV_REL" ]; then
  cp "$REMOTE_DIR/$ENV_REL" "$NEW/$ENV_REL"
  echo "    carried .env ($(wc -c < "$NEW/$ENV_REL") bytes)"
elif [ ! -f "$NEW/$ENV_REL" ]; then
  echo "    note: no .env on server — a first install must run install.sh, not ship.sh" >&2
fi
# Snapshot old tree, swap new into place.
[ -d "$REMOTE_DIR" ] && mv "$REMOTE_DIR" "${REMOTE_DIR}.predeploy-${TS}"
mv "$NEW" "$REMOTE_DIR"
echo "    swapped in $REMOTE_DIR (old → ${REMOTE_DIR}.predeploy-${TS})"
# Build + start + verify with the clear done-signal.
exec "$REMOTE_DIR/deploy/management/docker/deploy.sh"
REMOTE
