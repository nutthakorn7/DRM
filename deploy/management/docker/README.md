# Docker Deploy — DRM Management Server

Production-grade Docker Compose deploy. Single-host, three services:

- **drm-server** — the .NET 10 app, built from the repo `Dockerfile`
- **postgres** — metadata database (Postgres 16-alpine)
- **caddy** — reverse proxy with auto-TLS via Let's Encrypt

This is the **one supported deploy path**. Three scripts wrap it so you never
have to guess whether a deploy finished:

- **`install.sh`** — first time on a fresh box: installs Docker, generates `.env`
  with fresh secrets, brings the stack up. `sudo DOMAIN=… ./install.sh`.
- **`deploy.sh`** — build + start/upgrade. Backs up Postgres, tags a rollback
  image, runs `docker compose up -d --wait` (the health gate), verifies, and
  prints **✅ DEPLOY OK** or **❌ FAILED** then exits. Never tails logs.
- **`ship.sh`** — from your dev machine: ship the current repo to the server and
  run `deploy.sh` there, in one command. `./ship.sh root@your-server`.

> The bare-metal systemd installer (`../ubuntu/install.sh`) is **deprecated** and
> now just forwards here — production runs the Docker stack.

## Prerequisites

- A Linux host with Docker Engine 24+ and Docker Compose v2 (`docker compose`).
- A domain name with its A record pointing to the host.
- Ports 80 and 443 reachable from the internet (Let's Encrypt HTTP-01 challenge).
- 2 GB RAM minimum, 10 GB disk. Postgres + the .NET app + protected file
  payloads grow from there.

## Fresh install

```bash
# 1. Get the repo onto the host (any way you like)
ssh root@your-server
git clone <repo-url> /opt/drm        # or rsync / scp the tree
cd /opt/drm/deploy/management/docker

# 2. One command: installs Docker if needed, generates .env with fresh
#    secrets, brings the stack up, and prints ✅ DEPLOY OK when healthy.
sudo DOMAIN=drm.example.com ./install.sh
```

That's it — `install.sh` exits with a clear ✅/❌; there is **nothing to watch**.
The admin console is at `https://<DOMAIN>/admin/`; the admin API key is in the
generated `.env` as `DRM_ADMIN_API_KEY`.

**Back up `.env` (esp. `DRM_MASTER_KEY_BASE64`) to a secret manager NOW** —
losing the master key means losing access to all encrypted tenant wrapping keys.

If you'd rather drive it manually, `./install.sh` just generates `.env` then
calls `./deploy.sh`; you can run those two steps yourself.

## Upgrading

From your **dev machine**, one command ships the current repo and deploys it
(the server needs no git):

```bash
./ship.sh root@your-server               # ships HEAD; add a ref to pin: ./ship.sh root@host origin/master
```

Or **on the server**, after the new code is in place:

```bash
cd /opt/drm && ./deploy/management/docker/deploy.sh
```

Either way you get one of **✅ DEPLOY OK** / **❌ FAILED** and the process exits —
no `logs -f` guessing. `deploy.sh` takes a Postgres backup and tags a
`drm-server:rollback-<ts>` image first, and prints the one-line undo command.

Schema upgrades run on container start (`EnsureCreated` + idempotent raw-SQL
`CREATE TABLE / ADD COLUMN IF NOT EXISTS`); watch for them in
`docker compose logs --tail 80 drm-server` if a deploy ever fails.

## Rolling back

Tag releases as you build them and you can roll back in seconds:

```bash
# At build time (CI or manually after a clean release):
docker compose build drm-server
docker tag drm-server:local drm-server:v1.1.0

# To roll back:
DRM_IMAGE_TAG=v1.0.1 docker compose up -d drm-server
```

## Migrating from the systemd install

The systemd install (`../ubuntu/`) keeps state in two places:
1. Postgres database `drm` (the live data).
2. `/var/lib/drm-management` (the file payload directory) and
   `/etc/drm/env` (the secrets).

Migration plan:

```bash
# 1. Stop the systemd service so nothing changes during the move
sudo systemctl stop drm-management

# 2. Dump the database from the host Postgres
sudo -u postgres pg_dump -Fc drm > /tmp/drm-pre-migration.dump

# 3. Copy the existing data dir somewhere safe
sudo tar czf /tmp/drm-data-pre-migration.tar.gz -C /var/lib drm-management

# 4. Read your existing secrets so you can populate .env
sudo cat /etc/drm/env

# 5. Bring up the Docker stack with those SAME secrets in .env
#    (especially Drm__KeyWrapping__MasterKeyBase64 — it must match or
#     existing encrypted data is unreadable)
cd /opt/drm-source/deploy/management/docker
cp .env.example .env
# edit .env: paste the master key, admin/client/trailer keys, set DOMAIN,
# generate a NEW POSTGRES_PASSWORD (the Docker postgres is fresh)

docker compose up -d postgres
sleep 5
# 6. Restore the dump into the Docker Postgres
cat /tmp/drm-pre-migration.dump | docker compose exec -T postgres \
    pg_restore -U drm -d drm --clean --if-exists

# 7. Copy the file payload dir into the drm-data volume
docker compose run --rm --no-deps -v /tmp/drm-data-pre-migration.tar.gz:/import.tar.gz drm-server \
    tar xzf /import.tar.gz -C /var/lib

# 8. Start the app
docker compose up -d
docker compose logs -f drm-server
curl https://drm.example.com/healthz

# 9. Once verified working, free the systemd resources:
sudo systemctl disable drm-management
sudo systemctl disable caddy            # the host caddy is replaced by container caddy
sudo apt remove --purge caddy postgresql-16   # optional — only after you're sure
```

## Architecture notes

- **App listens on 8080 inside the container.** Caddy proxies
  `:443 → drm-server:8080`. Ports 80 and 443 on the host are owned by
  the Caddy container, not the host OS — make sure nothing else holds
  them (`sudo ss -tlnp | grep -E ':80|:443'`).
- **Postgres is internal-only.** It is not exposed to the host or the
  internet. If you need to connect a SQL client, run
  `docker compose exec postgres psql -U drm -d drm`.
- **Volumes** persist across restarts but are tied to the compose
  project. `docker compose down -v` deletes them — only do that on a
  test environment.
- **Resource limits** are not set by default. For a single-server
  pilot the defaults are fine. For multi-tenant production, add
  `deploy.resources.limits` per service and tune Postgres
  `shared_buffers` via a custom `postgresql.conf`.

## Troubleshooting

**Caddy can't get a cert.**
```bash
docker compose logs caddy | grep -i acme
dig +short $DOMAIN     # should resolve to your server IP
curl -I http://$DOMAIN  # should reach Caddy (not be blocked at port 80)
```

**Admin console returns 502 from Caddy.**
The app failed to start. `docker compose logs drm-server` will show the
real reason. Common: missing env var (the
`SecurityStartupGuard` refuses to start without
`Drm__Security__AdminApiKey` and `Drm__Security__TransparentTrailerSecret`).

**`pg_isready` fails on first start.**
Postgres takes 5-10s on first boot to initialize. The compose `depends_on`
with `service_healthy` waits for it; if you start services individually
just retry.

**Need to inspect the database.**
```bash
docker compose exec postgres psql -U drm -d drm
# inside psql:
\dt           # list tables
\d AuditEvents
SELECT COUNT(*) FROM "AuditEvents";
```

## Not covered here

- **Multi-host / HA.** This compose runs one Postgres on the same host
  as the app. For HA you need an external Postgres (RDS, Crunchy, etc.)
  and to point `ConnectionStrings__DrmDb` at it, plus a load balancer
  in front of multiple `drm-server` containers.
- **Off-site backups.** Add a sidecar that runs `pg_dump` on schedule
  and ships to S3/B2. The systemd install ships a `backup.sh`; the
  equivalent for Docker is `docker compose exec postgres pg_dump`.
- **Log shipping.** Container logs go to Docker's local driver. Wire up
  `loki` / `fluent-bit` / vendor sidecar when needed.
- **Identity (SSO/SAML/OIDC).** Coming in v1.1 Slice 3. For now,
  `DRM_ADMIN_API_KEY` is a shared secret per deployment, mapped to a
  Default SuperAdmin identity inside the app.
