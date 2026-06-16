# Ubuntu Deploy — DRM Management Server  ⚠️ DEPRECATED

> **This native (systemd + host Postgres/Caddy) path is retired.** Production runs
> the Docker stack, and `install.sh` here now just forwards to the Docker installer.
> Use that instead:
> ```bash
> cd ../docker
> sudo DOMAIN=drm.example.com ./install.sh
> ```
> See [`../docker/README.md`](../docker/README.md). The notes below are kept only
> for historical reference and for the migration plan in the Docker README.

End-to-end install on Ubuntu 22.04+ / Debian 12+ with PostgreSQL, Caddy (auto-TLS via Let's Encrypt), and systemd.

## What this package gives you

- `install.sh` — one-shot installer: .NET 10 runtime, PostgreSQL 16, Caddy, systemd unit, UFW rules, `/etc/drm/env` with freshly-minted secrets.
- `drm-management.service` — hardened systemd unit (NoNewPrivileges, ProtectSystem=strict, restart-on-failure).
- `backup.sh` — daily Postgres dump + data dir tarball, cron-ready.

## Prerequisites

- Ubuntu 22.04+ or Debian 12+ server, fresh box recommended.
- A domain name with its A record pointing at the server (Caddy needs this to get a cert).
- Port 80 and 443 reachable from the internet (Let's Encrypt HTTP-01 challenge).
- Root or sudo.

## Step 1 — Build artifact on your dev machine

```bash
cd /path/to/DRM
dotnet publish src/Drm.Server/Drm.Server.csproj -c Release -o ./artifacts/drm-management
```

This produces a self-contained set of DLLs in `./artifacts/drm-management/`.

## Step 2 — Copy the deploy scripts to the server

```bash
# Pack everything needed on the server side.
tar czf drm-deploy.tar.gz deploy/management/ubuntu artifacts/drm-management VERSION CHANGELOG.md
scp drm-deploy.tar.gz user@your-server:/tmp/
ssh user@your-server "cd /tmp && sudo tar xzf drm-deploy.tar.gz -C /root/"
```

## Step 3 — Run the installer

On the server:

```bash
sudo DOMAIN=drm.example.com /root/deploy/management/ubuntu/install.sh
```

The installer will:

1. Install .NET 10 runtime, PostgreSQL 16, Caddy, UFW.
2. Create `drm` system user.
3. Provision Postgres role `drm` + database `drm` with a random password.
4. Write `/etc/drm/env` with fresh `DRM_KEY_WRAPPING_MASTER_KEY_BASE64`, `DRM_ADMIN_API_KEY`, `DRM_CLIENT_API_KEY`, and the Postgres connection string.
5. Install the systemd unit and enable it.
6. Configure Caddy for your domain with auto-TLS, gzip, security headers.
7. Open firewall ports 22, 80, 443.

**Back up `/etc/drm/env` to your secret manager NOW.** Losing the master key means losing access to every encrypted tenant wrapping key on disk.

## Step 4 — Copy the published artifact

```bash
sudo rsync -av /root/artifacts/drm-management/ /opt/drm/server/
sudo chown -R drm:drm /opt/drm/server
```

## Step 5 — Start the service

```bash
sudo systemctl start drm-management
sudo systemctl status drm-management
sudo journalctl -u drm-management -f   # tail logs
```

## Step 6 — Verify

```bash
# Health from the server itself (bypasses Caddy)
curl http://127.0.0.1:5080/healthz

# Health through Caddy (real cert + TLS)
curl https://drm.example.com/healthz

# Admin console
open https://drm.example.com/admin/

# Tenant-mismatch check (proves the v1.0.1 security gate is live)
ADMIN_KEY=$(sudo grep DRM_ADMIN_API_KEY /etc/drm/env | cut -d= -f2)
curl -s -H "X-DRM-Admin-Key: $ADMIN_KEY" \
     -H "X-DRM-Tenant-Id: 00000000-0000-0000-0000-000000000001" \
     "https://drm.example.com/api/admin/audit?tenantId=00000000-0000-0000-0000-000000000002" \
     | jq .
# expected: {"reasonCode":"tenant_mismatch"}
```

## Step 7 — Schedule backups

```bash
sudo install -m 755 /root/deploy/management/ubuntu/backup.sh /usr/local/sbin/drm-backup
echo "0 3 * * * root /usr/local/sbin/drm-backup >> /var/log/drm-backup.log 2>&1" \
  | sudo tee /etc/cron.d/drm-backup
```

Test it once:

```bash
sudo /usr/local/sbin/drm-backup
ls -lh /var/backups/drm/
```

Then set up offsite shipping (S3, B2, restic-to-another-host). Local backups protect against app bugs, not against the server burning down.

## Upgrading to a new version

```bash
# On dev machine: build fresh artifact
dotnet publish src/Drm.Server/Drm.Server.csproj -c Release -o ./artifacts/drm-management

# Ship to server, stop service, swap files, restart
rsync -av ./artifacts/drm-management/ user@your-server:/tmp/drm-new/
ssh user@your-server <<'SSH'
sudo systemctl stop drm-management
sudo rsync -av --delete /tmp/drm-new/ /opt/drm/server/
sudo chown -R drm:drm /opt/drm/server
sudo systemctl start drm-management
sudo systemctl status drm-management
SSH

# Verify health + version
curl -s https://drm.example.com/healthz
ssh user@your-server "cat /opt/drm/server/VERSION 2>/dev/null || cat /opt/drm/VERSION"
```

EF Core migrations run automatically at startup (`AppDbContext` ensure-created).

## Troubleshooting

**Caddy can't get a cert.** DNS A record probably wrong, or port 80 blocked.
`sudo journalctl -u caddy -n 50` will tell you. Test with:
`dig +short drm.example.com` should show your server's IP.

**Service fails to start, "address already in use".** Something else on :5080.
`sudo ss -tlnp | grep 5080` to find it.

**Service fails to start, "FATAL: password authentication failed".** Postgres
password in `/etc/drm/env` doesn't match. Reset:
```bash
sudo -u postgres psql -c "ALTER USER drm WITH PASSWORD 'newpass';"
sudo sed -i 's/Password=.*$/Password=newpass/' /etc/drm/env
sudo systemctl restart drm-management
```

**500s after upgrade.** Most likely a missed migration. Check
`sudo journalctl -u drm-management -n 200`. Roll back: stop service, restore
previous `/opt/drm/server` from your local backup, restart.

**Tests on the server.** You don't need the .NET SDK on the server — the
runtime is enough to run the published binary. Don't install the SDK.

## What's NOT done by this install

- Postgres tuning (defaults are conservative; tune `shared_buffers`, `work_mem` for your workload).
- Audit retention policy (the `AuditEvents` table will grow forever — add a cron `DELETE FROM audit_events WHERE created_at_utc < NOW() - INTERVAL '180 days'` once you've decided your retention).
- Multi-instance deploy (this is single-server; for HA you'd need shared Postgres + a load balancer + sticky-session-free verification).
- Identity (SSO/SAML/OIDC) — the admin and client API keys are shared secrets per deployment. Real identity is roadmap.
- Log shipping (Caddy access log + journald are local-only).
