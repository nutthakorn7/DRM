# Management Server Install Baseline

This directory contains the on-prem management server baseline for the DRM API.

## Publish

```bash
dotnet publish src/Drm.Server/Drm.Server.csproj -c Release -o ./artifacts/drm-management
```

## Configure

Generate a durable key-wrapping master key and store it in your secret manager:

```bash
openssl rand -base64 32
```

Required environment:

```bash
export DRM_KEY_WRAPPING_MASTER_KEY_BASE64="<32-byte-base64-key>"
export DRM_ADMIN_API_KEY="<long-random-admin-api-key>"
```

Optional environment:

```bash
export DRM_SERVER_DIR="./artifacts/drm-management"
export DRM_DATA_DIR="/var/lib/drm-management"
export DRM_URL="http://0.0.0.0:5080"
```

`appsettings.onprem.example.json` shows the equivalent production shape. Do not deploy the placeholder key value.

## Run

From the repository root:

```bash
./deploy/management/start-management.sh
```

The script creates `DRM_DATA_DIR`, sets the SQLite database path, sets `Drm:Mode` to `OnPrem`, and refuses to start without `DRM_KEY_WRAPPING_MASTER_KEY_BASE64` and `DRM_ADMIN_API_KEY`.

Admin API calls under `/api/admin/*` must include:

```bash
X-DRM-Admin-Key: <long-random-admin-api-key>
```

## Check

```bash
curl http://localhost:5080/healthz
```

This is an install baseline, not a production hardening checklist. Real deployment still needs TLS termination, API authentication, service supervision, backups, audit retention, key rotation, and host monitoring.
