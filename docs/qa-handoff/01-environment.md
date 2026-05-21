# Environment Setup

## Production target

| Property | Value |
|----------|-------|
| Admin console URL | <https://drm.zcr.ai/admin/> |
| Send-a-file URL | <https://drm.zcr.ai/me/> |
| Open-shared-file URL | <https://drm.zcr.ai/share/> |
| Healthcheck | <https://drm.zcr.ai/healthz> (must return `{"status":"ok"}`) |
| Favicon | <https://drm.zcr.ai/static/favicon.svg> |
| Social card | <https://drm.zcr.ai/static/og-card.svg> |
| Current version | v1.6.1 (check `<title>` and CHANGELOG to confirm) |

## Optional: local development server

If you want a fast iteration loop without touching production:

```bash
# Clone
git clone https://github.com/nutthakorn7/DRM.git
cd DRM

# Run server (requires .NET 10 SDK)
dotnet run --project src/Drm.Server/Drm.Server.csproj

# Default URL
curl http://localhost:5000/healthz
```

A local SQLite DB is created automatically — no Postgres needed for dev mode.

## Optional: deploy preview (Docker)

```bash
cd deploy/management/docker
cp .env.example .env
# Edit .env: set DOMAIN=localhost, generate keys with openssl rand -base64 32
docker compose up -d
docker compose logs -f drm-server
```

## Browser tooling

The QA team can test purely through the browser. No special tools required.
Recommended browsers for cross-platform coverage:

| Browser | OS | Coverage |
|---------|-----|----------|
| Chrome (latest) | macOS / Windows / Linux | Default target — most customers |
| Edge (latest) | Windows | Enterprise customers |
| Safari (latest) | macOS | Mac users |
| Firefox (latest) | All | Privacy-conscious users |

## Windows-specific testing

Some features require a real Windows host:

- **WPF viewer** (`Drm.Viewer.Windows`): runs on Windows 10/11, requires .NET 10 desktop runtime
- **Screen-capture protection** (`SetWindowDisplayAffinity`): Windows 10 build 19041+
- **Office add-ins** (Outlook + Word): Windows Office desktop
- **Folder watcher service**: Windows Server 2019+

These can run on any Windows VM or a colleague's machine — see `02-test-plan.md`
section Tier 3 for the specific scenarios.

## API testing — bare HTTP

For backend-only checks (no UI), use `curl` or Postman. The session in your
browser is just a UI shell; the server is plain REST.

```bash
# Health
curl https://drm.zcr.ai/healthz

# Admin endpoint (requires X-DRM-Admin-Key header — see 03-test-data.md)
curl -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  "https://drm.zcr.ai/api/admin/policy-templates?tenantId=$TENANT_ID"
```

Full endpoint list lives in `src/Drm.Server/Endpoints/` — every file is one
endpoint group. Not yet published as docs.

## Server access (for QA team lead only)

The production server is accessible via SSH:

```bash
ssh root@drm.zcr.ai
```

This is reserved for the QA lead to investigate failures, pull logs, etc.
Individual QA engineers should NOT need SSH access.

Common server-side investigation commands once logged in:

```bash
# Container status
docker ps

# Live logs
docker compose -f /opt/drm/deploy/management/docker/docker-compose.yml logs -f drm-server

# Disk + memory
df -h /
free -h

# Postgres query (read-only sanity)
docker exec docker-postgres-1 psql -U drm -d drm -c 'SELECT COUNT(*) FROM "TenantUsers";'
```

## Test environment hygiene

- **Always use the test tenant** from `03-test-data.md`, not customer-real data
- **Never commit secrets** (admin keys, .env files) to GitHub
- **Each session**: dismiss the welcome modal, save the test tenant session in
  the settings drawer so you don't have to retype GUIDs
- **Between scenarios**: revoke any share links you created so you don't
  pollute the next tester's view of "recent activity"
