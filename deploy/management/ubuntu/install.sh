#!/usr/bin/env bash
# DEPRECATED — the native (systemd + host Postgres/Caddy) installer has been
# retired in favour of a single supported path: the Docker stack. Production
# (drm.zcr.ai) already runs that stack; maintaining two install paths was the
# source of "which one do I run?" confusion.
#
# Use the Docker installer instead:
#   sudo DOMAIN=drm.example.com deploy/management/docker/install.sh
#
# This shim forwards to it so existing automation/muscle-memory keeps working.
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
echo "⚠️  The native systemd installer is deprecated — redirecting to the Docker installer." >&2
echo "    (deploy/management/docker/install.sh)" >&2
exec "$SCRIPT_DIR/../docker/install.sh" "$@"
