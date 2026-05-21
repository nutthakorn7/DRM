# Test Data + Fixtures

> **Important:** these are TEST values intended for sandbox tenants only.
> Do NOT use production credentials for testing. Generate a fresh test
> tenant on first use via the welcome modal's "Create test tenant" button.

## Generating a clean test tenant (recommended)

The fastest way to get a working test environment:

1. Open <https://drm.zcr.ai/admin/> in **incognito** (so no saved session)
2. Welcome modal → click "Create test tenant"
3. The modal auto-generates a `tenantId`, `adminKey`, and `adminUserId` for you
4. Click "Save session" to persist them in localStorage
5. Copy these into a private password manager — they're your TEST set

Each QA engineer should keep their own test tenant. Don't share the same
GUID — concurrent edits cause flaky audit events.

## Test data shape (fill in your own values)

```yaml
# Tenant
tenant_id: 00000000-0000-0000-0000-000000000000   # ← generate fresh

# Admin
admin_user_id: 00000000-0000-0000-0000-000000000000
admin_credential: drm_admin_xxxxx                  # treat as password

# Test users (create via Identity tab)
user_alice:
  user_id: 11111111-1111-1111-1111-111111111111
  email: alice@example.test
  display_name: "Alice Tester"

user_bob:
  user_id: 22222222-2222-2222-2222-222222222222
  email: bob@example.test
  display_name: "Bob Tester"

# Test group
group_legal:
  group_id: 33333333-3333-3333-3333-333333333333
  name: "Legal"
  members: [alice, bob]

# Test policy templates
template_strict:
  template_id: 44444444-4444-4444-4444-444444444444
  name: "Strict — 3 opens, 1h lease"
  permissions: "View"
  watermark_template: "user:{user} time:{time}"
  offline_lease_minutes: 60
  max_opens: 3            # NEW in v1.4.0

template_loose:
  template_id: 55555555-5555-5555-5555-555555555555
  name: "Loose — 1 day lease, unlimited"
  permissions: "View, Print, Copy"
  watermark_template: "ENGINEER DRAFT — {user}"
  offline_lease_minutes: 1440
  max_opens: null         # unlimited

# Recipient emails (for share-link testing)
guest_emails:
  - guest1@example.test
  - guest2@example.test
  - typo-test@example.test    # for the brute-force scenario
```

## Sample files to upload

Keep these in your local test fixtures directory:

| File | Size | Purpose |
|------|------|---------|
| `small.pdf` | < 50 KB | Quick smoke + watermark check |
| `medium.pdf` | 1-5 MB | Realistic enterprise doc |
| `large.pdf` | 25-50 MB | Upload boundary test |
| `oversize.pdf` | > 100 MB | Should fail with size error |
| `report.docx` | 1 MB | Office content-type check |
| `spreadsheet.xlsx` | 500 KB | Spreadsheet path |
| `presentation.pptx` | 5 MB | PowerPoint path |
| `image.jpg` | 200 KB | Non-document path |
| `text.txt` | 5 KB | Plain text |
| `archive.zip` | 10 MB | ZIP content-type |
| `script.js` | 10 KB | Should be rejected or flagged (executable risk) |
| `corrupt.pdf` | 100 B | Truncated header, should reject gracefully |

Don't commit these to the repo — keep them in `~/qa-test-files/` locally.

## Brute-force test sequence (C2)

To trigger the share-link auto-revoke:

```yaml
# Preconditions:
#   1. Test tenant with brute-force policy set to threshold=3, windowMinutes=60
#      PUT /api/admin/brute-force-policy
#      { tenantId, enabled: true, threshold: 3, windowMinutes: 60 }
#   2. Share link active, guest email = guest1@example.test
#   3. Verification started, code received in email

# Steps:
- attempt 1: enter "000000" (wrong)        → 400 invalid_verification_code
- attempt 2: enter "111111" (wrong)        → 400 invalid_verification_code
- attempt 3: enter "222222" (wrong)        → 400 share_link_auto_revoked    ← bug fix C2
- attempt 4: enter the CORRECT code        → 400 (link is revoked)
- admin view share-link list               → revocationReason: "brute_force_threshold"
```

## Access count test sequence (C1)

```yaml
# Preconditions:
#   1. Policy template with max_opens: 3 applied to a file
#   2. Two users (Alice, Bob) both have View grant on the file

# Steps:
- Alice POST /api/files/{id}/keys/unwrap   → 200, opensRemaining: 2
- Alice POST /api/files/{id}/keys/unwrap   → 200, opensRemaining: 1
- Alice POST /api/files/{id}/keys/unwrap   → 200, opensRemaining: 0
- Alice POST /api/files/{id}/keys/unwrap   → 403, reasonCode: opens_exhausted
- Bob POST /api/files/{id}/keys/unwrap     → 200, opensRemaining: 2  (per-user!)
```

## Endpoint cheatsheet

Add `X-DRM-Admin-Key: $admin_key` to every `/api/admin/*` call:

```bash
# Set once at start of session
export TENANT_ID="..."
export ADMIN_KEY="drm_admin_..."
export DRM=https://drm.zcr.ai

# Healthcheck (no auth)
curl $DRM/healthz

# Recent audit events
curl -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  "$DRM/api/admin/audit?tenantId=$TENANT_ID&limit=20"

# Create policy template
curl -X POST -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  "$DRM/api/admin/policy-templates" \
  -d "{
    \"tenantId\": \"$TENANT_ID\",
    \"templateId\": \"44444444-4444-4444-4444-444444444444\",
    \"name\": \"Strict\",
    \"permissions\": \"View\",
    \"watermarkTemplate\": \"user:{user} time:{time}\",
    \"offlineLeaseMinutes\": 60,
    \"allowPrint\": false,
    \"maxOpens\": 3
  }"

# Get brute-force policy
curl -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  "$DRM/api/admin/brute-force-policy?tenantId=$TENANT_ID"

# Recent brute-force failures
curl -H "X-DRM-Admin-Key: $ADMIN_KEY" \
  "$DRM/api/admin/brute-force-policy/recent-failures?tenantId=$TENANT_ID&limit=50"
```

All endpoint groups live under `src/Drm.Server/Endpoints/*.cs` — there's no
published API docs site yet, but each file maps to one URL prefix.
