# Management Server Install Baseline

> **Installing for real? Use the Docker stack — it's the one supported deploy path.**
> ```bash
> cd deploy/management/docker
> sudo DOMAIN=drm.example.com ./install.sh   # first time → prints ✅ DEPLOY OK
> ```
> Upgrades: `./ship.sh root@your-server` (from your dev machine) or `./deploy.sh`
> (on the server). See [`docker/README.md`](docker/README.md). The native systemd
> path (`ubuntu/`) is **deprecated** — its `install.sh` now forwards to the Docker one.

The rest of this page is the **local / dev run** (SQLite, no TLS, no containers) —
handy for hacking on the server on your laptop, **not** for production.

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
export DRM_CLIENT_API_KEY="<long-random-client-api-key>"
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

When `DRM_CLIENT_API_KEY` is set, non-admin `/api/*` calls must include:

```bash
X-DRM-Client-Key: <long-random-client-api-key>
```

## Check

```bash
curl http://localhost:5080/healthz
```

Open the management console:

```text
http://localhost:5080/admin/
```

Enter the tenant ID and admin API key. The console sends `X-DRM-Admin-Key` for `/api/admin/*` operations.

This is an install baseline, not a production hardening checklist. Real deployment still needs TLS termination, API authentication, service supervision, backups, audit retention, key rotation, and host monitoring.
