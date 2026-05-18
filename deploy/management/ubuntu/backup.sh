#!/usr/bin/env bash
# Daily backup of DRM server: postgres dump + data dir tarball.
# Install: copy to /usr/local/sbin/drm-backup, make executable, add to cron:
#   0 3 * * * /usr/local/sbin/drm-backup >> /var/log/drm-backup.log 2>&1
set -euo pipefail

BACKUP_DIR="${BACKUP_DIR:-/var/backups/drm}"
RETENTION_DAYS="${RETENTION_DAYS:-14}"
PG_DB="${PG_DB:-drm}"
DRM_DATA_DIR="${DRM_DATA_DIR:-/var/lib/drm-management}"
DATE=$(date +%Y%m%d-%H%M%S)

mkdir -p "$BACKUP_DIR"

echo "[$(date -Iseconds)] dumping postgres database '$PG_DB'"
# Pipe through tee so pg_dump runs as the postgres user but the file is
# written as the caller (root in cron). Bare `sudo -u postgres pg_dump > file`
# is a shellcheck SC2024 trap: the redirect runs as the caller, not postgres,
# so it fails if the caller can't write to BACKUP_DIR through the redirect.
sudo -u postgres pg_dump -Fc "$PG_DB" | tee "$BACKUP_DIR/drm-db-$DATE.dump" > /dev/null

echo "[$(date -Iseconds)] archiving data dir '$DRM_DATA_DIR'"
tar czf "$BACKUP_DIR/drm-data-$DATE.tar.gz" -C "$(dirname "$DRM_DATA_DIR")" "$(basename "$DRM_DATA_DIR")"

echo "[$(date -Iseconds)] pruning backups older than $RETENTION_DAYS days"
find "$BACKUP_DIR" -name 'drm-*' -mtime "+$RETENTION_DAYS" -delete

echo "[$(date -Iseconds)] done"

# Reminder: this is local backup only. Ship copies offsite (S3, B2, another host).
# The master key in /etc/drm/env is NOT in these archives by design — back it up
# separately to a secret manager (1Password, Vault, AWS Secrets Manager).
