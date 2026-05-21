# 09 — Pre-seeded demo credentials on `drm.zcr.ai`

> **TL;DR:** The demo tenant + users + template + brute-force policy are already created on production. You don't have to do the 30-minute prep in [01-engineer-prep.md](01-engineer-prep.md) — just verify these IDs still resolve, then jump to MSI install in [08-engineer-windows-msi-setup.md](08-engineer-windows-msi-setup.md).
>
> Seeded on 2026-05-21 by the agent-easy-install rollout.

## Demo tenant

| Field | Value |
|---|---|
| Tenant ID | `dddddddd-1111-2222-3333-dddddddddddd` |
| Name | ABC Co. |
| Display name | ABC Co. (zcrDRM demo) |
| Max encrypters | 10 |

## Seeded users

| Role in demo | Email | User ID | Display name |
|---|---|---|---|
| **Engineer sign-in** (the email you'll type in the agent's first-run dialog) | `demo@zcr.ai` | `eeeeeeee-1111-2222-3333-eeeeeeeeeeee` | Demo Engineer |
| Sender persona (Somchai from ABC Co.) | `somchai@abc.co.th` | `aaaaaaaa-1111-2222-3333-aaaaaaaaaaaa` | Somchai Jaidee |
| External-recipient persona (Malee from XYZ Co.) | `malee@xyz.com` | `bbbbbbbb-1111-2222-3333-bbbbbbbbbbbb` | Malee Wongprasert |

## Policy template — "Confidential Contract"

| Field | Value |
|---|---|
| Template ID | `cccccccc-1111-2222-3333-cccccccccccc` |
| Name | Confidential Contract |
| Permissions | `View` (no print, no copy, no edit) |
| Watermark | `{user} · {time} · ABC CONFIDENTIAL` |
| Offline lease | 60 minutes |
| Max opens per user | **3** (FinalCode C1) |
| Allow print | false |

This template is the tenant's **default** — discover responses return its ID in `defaultPolicyTemplateId`, so the tray's right-click "Protect" lands here automatically.

## Brute-force policy

| Field | Value |
|---|---|
| Enabled | true |
| Threshold | **3** failed attempts (lower than the default 10 so the demo can trigger an auto-revoke quickly) |
| Window | 30 minutes |

## How to verify everything still resolves (30 sec)

```bash
# Discover endpoint returns 200 + the seeded template
curl -s "https://drm.zcr.ai/api/agent/discover?email=demo@zcr.ai" | jq .
# expected: tenantId dddd...dddd, userId eeee...eeee, defaultPolicyTemplateId cccc...cccc

# All three users resolve
for e in demo@zcr.ai somchai@abc.co.th malee@xyz.com; do
  echo "$e → $(curl -s -o /dev/null -w '%{http_code}' "https://drm.zcr.ai/api/agent/discover?email=$e")"
done
# expected: 200 / 200 / 200
```

## The Monday flow

With the seed in place, the engineer's pre-demo prep collapses to:

1. Download `zcrdrm-agent.msi` from the latest green master CI run (see [08-engineer-windows-msi-setup.md](08-engineer-windows-msi-setup.md))
2. Install on the demo laptop, clear SmartScreen
3. Launch the agent → first-run dialog → type **`demo@zcr.ai`** → sign in
4. Main window opens with `Display name = Demo Engineer`, `Tenant ID = dddd...dddd`, `Template = cccc...cccc` all pre-filled
5. Right-click any PDF → "Protect with zcrDRM" → Quick send → recipient `malee@xyz.com` → done

No `/admin/` setup needed.

## If you need a fresh tenant (e.g. seed got corrupted)

Re-run the seed via curl from any laptop with internet:

```bash
ADMIN="<DRM_ADMIN_API_KEY from /opt/drm/deploy/management/docker/.env on the server>"

# 1. Create tenant (skip if it still exists)
curl -X POST -H "X-DRM-Admin-Key: $ADMIN" -H "Content-Type: application/json" \
  -d '{"tenantId":"dddddddd-1111-2222-3333-dddddddddddd","name":"ABC Co.","displayName":"ABC Co. (zcrDRM demo)","maxEncrypters":10}' \
  https://drm.zcr.ai/api/admin/tenants

# 2. Users
for spec in \
  '{"tenantId":"dddddddd-1111-2222-3333-dddddddddddd","userId":"aaaaaaaa-1111-2222-3333-aaaaaaaaaaaa","email":"somchai@abc.co.th","displayName":"Somchai Jaidee"}' \
  '{"tenantId":"dddddddd-1111-2222-3333-dddddddddddd","userId":"bbbbbbbb-1111-2222-3333-bbbbbbbbbbbb","email":"malee@xyz.com","displayName":"Malee Wongprasert"}' \
  '{"tenantId":"dddddddd-1111-2222-3333-dddddddddddd","userId":"eeeeeeee-1111-2222-3333-eeeeeeeeeeee","email":"demo@zcr.ai","displayName":"Demo Engineer"}' ; do
  curl -X POST -H "X-DRM-Admin-Key: $ADMIN" -H "Content-Type: application/json" -d "$spec" https://drm.zcr.ai/api/admin/users
done

# 3. Template
curl -X POST -H "X-DRM-Admin-Key: $ADMIN" -H "Content-Type: application/json" \
  -d '{"tenantId":"dddddddd-1111-2222-3333-dddddddddddd","templateId":"cccccccc-1111-2222-3333-cccccccccccc","name":"Confidential Contract","permissions":"View","watermarkTemplate":"{user} · {time} · ABC CONFIDENTIAL","offlineLeaseMinutes":60,"allowPrint":false,"maxOpens":3}' \
  https://drm.zcr.ai/api/admin/policy-templates

# 4. Brute-force policy
curl -X PUT -H "X-DRM-Admin-Key: $ADMIN" -H "Content-Type: application/json" \
  -d '{"tenantId":"dddddddd-1111-2222-3333-dddddddddddd","enabled":true,"threshold":3,"windowMinutes":30}' \
  https://drm.zcr.ai/api/admin/brute-force-policy
```

## Post-demo cleanup

When the demo is done and you want the prod DB clean again:

```bash
ssh root@drm.zcr.ai "docker exec docker-postgres-1 psql -U drm drm <<SQL
DELETE FROM \"TenantBruteForcePolicies\" WHERE \"TenantId\" = 'dddddddd-1111-2222-3333-dddddddddddd';
DELETE FROM \"PolicyTemplates\"          WHERE \"TenantId\" = 'dddddddd-1111-2222-3333-dddddddddddd';
DELETE FROM \"TenantUsers\"              WHERE \"TenantId\" = 'dddddddd-1111-2222-3333-dddddddddddd';
DELETE FROM \"Tenants\"                  WHERE \"TenantId\" = 'dddddddd-1111-2222-3333-dddddddddddd';
SQL"
```

(If the demo went well and the customer is moving forward, KEEP the tenant — it becomes the starting point for the pilot.)
