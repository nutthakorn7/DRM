#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd -P)"

SERVER_DIR="${DRM_SERVER_DIR:-$REPO_ROOT/src/Drm.Server}"
DRM_DATA_DIR="${DRM_DATA_DIR:-$SCRIPT_DIR/data}"
DRM_URL="${DRM_URL:-http://0.0.0.0:5080}"
DOTNET="${DOTNET:-dotnet}"

if [[ -z "${DRM_KEY_WRAPPING_MASTER_KEY_BASE64:-}" ]]; then
  echo "DRM_KEY_WRAPPING_MASTER_KEY_BASE64 is required. Generate one with: openssl rand -base64 32" >&2
  exit 2
fi

if [[ -z "${DRM_ADMIN_API_KEY:-}" ]]; then
  echo "DRM_ADMIN_API_KEY is required for /api/admin/* requests." >&2
  exit 2
fi

mkdir -p "$DRM_DATA_DIR"

export ASPNETCORE_ENVIRONMENT="${ASPNETCORE_ENVIRONMENT:-Production}"
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-$DRM_URL}"
export ConnectionStrings__DrmDb="${ConnectionStrings__DrmDb:-Data Source=$DRM_DATA_DIR/drm-server.db}"
export Drm__Mode="${Drm__Mode:-OnPrem}"
export Drm__KeyWrapping__MasterKeyBase64="$DRM_KEY_WRAPPING_MASTER_KEY_BASE64"
export Drm__Security__AdminApiKey="$DRM_ADMIN_API_KEY"

if [[ -f "$SERVER_DIR/Drm.Server.dll" ]]; then
  exec "$DOTNET" "$SERVER_DIR/Drm.Server.dll"
fi

exec "$DOTNET" run --project "$SERVER_DIR/Drm.Server.csproj" --no-launch-profile
