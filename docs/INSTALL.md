# Installing zcrDRM

Self-hosted zcrDRM runs as a Docker stack on one Linux server: the app, a
Postgres database, and Caddy (which gets you HTTPS automatically). A fresh
install is **one command** and takes about 5 minutes.

---

## TL;DR (the whole thing)

```bash
# 1. Put the code on the server
git clone <repo-url> /opt/drm        # or rsync/scp the folder — the server needs no git

# 2. Run the installer
cd /opt/drm/deploy/management/docker
sudo DOMAIN=drm.example.com ./install.sh
```

When you see this, you're done:

```
================================================================
  ✅ DEPLOY OK — zcrDRM 1.7.0 is live and healthy
     admin console: https://drm.example.com/admin/
================================================================
```

There is **nothing to watch and nothing else to run** — the installer exits when
the server is healthy. Then do the [two things right after install](#3-right-after-install-dont-skip).

---

## 1. Before you start

Five things, all required:

| ✔ | What | Why |
|---|------|-----|
| ☐ | A Linux server (Ubuntu 22.04+ / Debian 12+), root/sudo | the installer installs Docker + opens the firewall |
| ☐ | A domain name (e.g. `drm.example.com`) | Caddy needs it to get an HTTPS certificate |
| ☐ | A **DNS A record** for that domain → the server's public IP | so the certificate authority can reach it |
| ☐ | Ports **80 and 443** open to the internet | port 80 = certificate challenge, 443 = the actual site |
| ☐ | ~2 GB RAM, ~10 GB free disk | the app + database + your protected files |

> No Docker yet? That's fine — the installer installs it for you.

## 2. Install (step by step)

**Step 1 — get the code onto the server.** Any method works (the server does not
need git):

```bash
ssh root@your-server
git clone <repo-url> /opt/drm
# — or, from your laptop —  rsync -a ./DRM/ root@your-server:/opt/drm/
```

**Step 2 — run the installer**, passing your real domain:

```bash
cd /opt/drm/deploy/management/docker
sudo DOMAIN=drm.example.com ./install.sh
```

That's it. The installer prints what it's doing and finishes with `✅ DEPLOY OK`
(or `❌` with the exact reason). You'll see, in order:

```
==> Installing Docker Engine …            (only if Docker wasn't already there)
==> Generating .env with fresh secrets
==> Opening firewall (22/80/443)
==> Handing off to deploy.sh
==> Building image
==> Starting and waiting for healthy…
  ✅ DEPLOY OK — zcrDRM 1.7.0 is live and healthy
```

If the **last** line shows `⚠️ public healthz != 200` on a brand-new install, it's
almost always the HTTPS certificate still being issued (DNS/Let's Encrypt take a
minute). The server itself is healthy — re-check in a minute with
`curl https://drm.example.com/healthz`.

## 3. Right after install (don't skip)

**a. Back up your master key — immediately.** The installer generated secrets in
`/opt/drm/deploy/management/docker/.env`. One of them, `DRM_MASTER_KEY_BASE64`,
encrypts everything. **If you lose it, all protected data is unrecoverable.**
Copy `.env` into a password manager / secret vault now:

```bash
cat /opt/drm/deploy/management/docker/.env     # copy this somewhere safe
```

**b. Log in to the admin console.** Open `https://your-domain/admin/`. Your admin
API key is the `DRM_ADMIN_API_KEY` value in that same `.env` file.

## 4. Did it work?

```bash
cd /opt/drm/deploy/management/docker
docker compose ps                          # all three services say "Up" / "healthy"
curl https://your-domain/healthz           # 200
```

Then open `https://your-domain/admin/` in a browser.

---

## 5. Upgrading later (not a fresh install)

You never re-run `install.sh` to upgrade. Two ways, both end in `✅ / ❌`:

**From your laptop (recommended) — one command:**
```bash
./ship.sh root@your-server                 # ships the current code + deploys it
```

**On the server** (after the new code is in place):
```bash
cd /opt/drm && ./deploy/management/docker/deploy.sh
```

Before each upgrade, `deploy.sh` automatically backs up the database and tags the
current image `drm-server:rollback-<timestamp>`, and prints the one-line undo.

## 6. If something goes wrong

| You see | Do this |
|---------|---------|
| `❌ DEPLOY FAILED — … did not become healthy` | `docker compose logs --tail 80 drm-server` — the real error is there (usually a missing secret in `.env`). |
| Browser shows **502** | The app didn't start. Same logs command as above. |
| HTTPS / certificate errors | `docker compose logs caddy \| grep -i acme`; check `dig +short your-domain` returns the server IP and port 80 is open. |
| An upgrade broke something | Roll back with the command `deploy.sh` printed: `docker tag drm-server:rollback-<ts> drm-server:local && docker compose up -d --force-recreate drm-server` (restore the DB from `/opt/drm-backups/predeploy-<ts>.sql` only if needed). |

> **Never run `docker compose down -v`** on a production server — the `-v` deletes
> the data volumes (database + files). To just restart, use `docker compose restart`.
> And `docker compose logs -f` follows logs forever — it's for watching, not a
> "did it finish?" check; the scripts already tell you that with `✅ / ❌`.

---

## How it works (optional reading)

### The three containers
```
internet ──443/80──> [ caddy ] ──proxy──> [ drm-server :8080 ] ──> [ postgres :5432 ]
                       auto-TLS              the .NET app             internal only
```
- **caddy** owns ports 80/443 on the host, fetches a Let's Encrypt cert for your
  `DOMAIN`, and proxies HTTPS to the app.
- **drm-server** listens on 8080 *inside the Docker network only* (not exposed to
  the host or internet).
- **postgres** is internal-only too. Inspect it with
  `docker compose exec postgres psql -U drm -d drm`.

### What the installer actually does
`install.sh` → installs Docker if missing → generates `.env` with fresh secrets
(only if one doesn't already exist — it never overwrites yours) → opens the
firewall → hands off to `deploy.sh`.

`deploy.sh` → (on upgrades) backs up Postgres + tags a rollback image → builds the
app image → `docker compose up -d --wait` (this is the health gate: it blocks
until the container is healthy, then exits) → verifies `/healthz` → prints `✅ / ❌`.

### The secrets in `.env`
| Key | Purpose | Note |
|-----|---------|------|
| `DRM_MASTER_KEY_BASE64` | encrypts all tenant keys at rest | **back it up; never rotate after data exists** — losing it loses everything |
| `DRM_ADMIN_API_KEY` | the admin console / `X-DRM-Admin-Key` | |
| `DRM_CLIENT_API_KEY` | desktop/client API auth | |
| `DRM_TRAILER_SECRET` | signs transparent-protected files | |
| `POSTGRES_PASSWORD` | database password | |

### Where your data lives
- Database → Docker volume `postgres-data`; protected-file payloads → volume `drm-data`.
- Secrets → `/opt/drm/deploy/management/docker/.env` (root-only).
- Pre-upgrade backups → `/opt/drm-backups/`. **Ship copies off-site** (S3/B2/another
  host); the master key is intentionally *not* in these backups — store it separately.

### Schema / first boot
There are no manual database migrations. On every start the app creates anything
missing (`EnsureCreated` + idempotent `CREATE TABLE / ADD COLUMN IF NOT EXISTS`),
so the same image safely handles both a fresh install and an upgrade. A default
admin identity is seeded on first boot and mapped to `DRM_ADMIN_API_KEY`.

### Local / dev runs (not production)
To run the server on your laptop without Docker (SQLite, no TLS), see
`deploy/management/README.md` (`start-management.sh`). That's for development only.
