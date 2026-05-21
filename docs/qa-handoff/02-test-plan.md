# Test Plan — Prioritised Scenarios

> **Tier 0 = smoke** (5 min, run before every session)
> **Tier 1 = critical path** (45 min, must pass before release)
> **Tier 2 = feature coverage** (4 hours, every endpoint exercised)
> **Tier 3 = Windows + edge cases** (1 day, needs real Windows + Office)

Use the bug template in `04-bug-template.md` for anything that fails.

---

## Tier 0 — Smoke (5 minutes)

Goal: confirm production is reachable, brand is intact, health is OK.

| # | Step | Expected |
|---|------|----------|
| 0.1 | Open <https://drm.zcr.ai/healthz> | `{"status":"ok"}` |
| 0.2 | Open <https://drm.zcr.ai/admin/> | Page loads, no console errors, zcrDRM wordmark visible |
| 0.3 | Open <https://drm.zcr.ai/me/> | Wordmark, "Send a protected file" h1, no `Admin →` link |
| 0.4 | Open <https://drm.zcr.ai/share/> | Wordmark + lock SVG, "Open shared file" h1 |
| 0.5 | DevTools → Console on each of the 3 pages above | 0 errors |
| 0.6 | View page source on `/admin/`, search "og:image" | Points to `/static/og-card.svg` |
| 0.7 | Open <https://drm.zcr.ai/static/og-card.svg> directly | 1200×630 branded poster renders |
| 0.8 | Open <https://drm.zcr.ai/static/favicon.svg> directly | Teal tile with white seal icon |

If any of these fail → **stop testing, file a P0 bug**, ping the on-call eng.

---

## Tier 1 — Critical Path (45 minutes)

### T1.1 — Onboarding flow

| Step | Expected |
|------|----------|
| Open `/admin/` (incognito, no saved session) | Welcome modal renders with "zcrDRM — self-hosted DRM" headline + 5-step checklist |
| Click "Create test tenant" | Tenant ID, admin credential, admin user ID auto-fill in the settings form |
| Modal closes | Getting-started checklist shows step 1 ticked |
| Click "Save session" | Persona indicator updates, settings drawer can store these |
| Refresh page | Settings still populated (localStorage persistence) |

### T1.2 — Tab navigation (the v1.2.x bug regression)

| Step | Expected |
|------|----------|
| Click each tab in turn: Overview → Identity → Policy → Files → Integrations → **Tenants** → back to Overview | Each tab's panels render. `body[data-active-tab]` updates accordingly (inspect via DevTools Console: `document.body.dataset.activeTab`) |
| **Tenants tab specifically** | Must show 10 subtabs (Tenants, Tenant registrations, Access requests, Tenant plans, Compliance & data ri…, File retention policy, IP allowlist, Device trust, Key rotation, Usage snapshot). The v1.2.1 bug had Tenants silently falling through to Overview — this MUST stay fixed. |
| URL fragment | Each tab click updates `#tab-X` in the URL |
| Refresh while on Tenants tab | Tenants tab still active after refresh |

### T1.3 — Create user → group → policy template → protect file → grant → share

End-to-end happy path. Use values from `03-test-data.md`.

1. Identity tab → Users subtab → "Create user" form → enter test user → submit → user appears in table
2. Identity → Groups → create test group → assign user as member → both visible
3. Policy tab → Policy templates → create template with **MaxOpens = 3** (new in v1.4.0) → template appears with "Max opens: 3 / user" column
4. (Skip "protect a file" — that requires the Windows agent; use Quick Share at `/me/` instead)

### T1.4 — Quick Share at `/me/`

| Step | Expected |
|------|----------|
| Open `/me/` | Form with Tenant ID, User ID, drop zone, recipient email |
| Fill all required fields | "Send protected file" button enables |
| Drop a real file (PDF, < 5 MB) | File summary appears |
| Click Send | Result panel shows share URL + Copy link button + "Send another file" link |
| Copy link | Clipboard has the share URL |
| Open the share URL in incognito | Lands on `/share/` "Open shared file" page |

### T1.5 — External share verification flow

| Step | Expected |
|------|----------|
| On `/share/` with prefill from URL | Tenant ID, access token populated, only guest email empty |
| Enter the recipient email from T1.4 | "Send verification code" button enables |
| Click | "Verification ID" field gets populated, status: code emailed |
| Check the recipient inbox | Code arrives (if Email integration is configured — otherwise it logs to server console) |
| Type wrong code 5 times | After 5th, `verification_attempts_exceeded` error |
| Continue trying wrong codes via new verification sessions, repeat to **10 total failures** | **`share_link_auto_revoked`** error fires on the 10th — brute-force C2 protection kicks in |
| Try correct code after that | Still rejected — link is permanently dead |

### T1.6 — Access count enforcement (C1)

| Step | Expected |
|------|----------|
| Apply policy template with MaxOpens=3 to a protected file | File entity gets `maxOpens: 3` |
| Recipient User A opens the file (`POST /api/files/{id}/keys/unwrap`) | Response includes `opensRemaining: 2` |
| Same User A opens again | `opensRemaining: 1` |
| Third open | `opensRemaining: 0` |
| Fourth open by User A | HTTP 403 with `{"reasonCode": "opens_exhausted"}` |
| Different User B opens (with their own grant) | `opensRemaining: 2` — counter is per-user, NOT per-file |

### T1.7 — Admin revoke

| Step | Expected |
|------|----------|
| Files tab → find the file from T1.4 | Row shows with grant info |
| Click "Revoke" | File entity gets `Revoked: true`, audit event written |
| Any new unwrap attempt | HTTP 403 with `{"reasonCode": "revoked"}` |

### T1.8 — Audit log

| Step | Expected |
|------|----------|
| Overview tab → Audit events subtab → Refresh | Recent events visible: `file_registered`, `access_allowed`, `access_denied`, `share_link_auto_revoked`, `policy_template_created`, etc. |
| Export CSV | Downloads valid CSV with all events |
| SIEM webhook configured (Integrations → SIEM webhooks) | Same events POST to the webhook URL |

---

## Tier 2 — Feature Coverage (4 hours)

### T2.1 — All policy templates fields

For each policy template field: create with non-default value, apply to a file,
verify the file inherits the value, verify enforcement at access time.

- Permissions: View / Print / Copy / Edit / DeleteProtectedCopy / RunMacros / TransferOwnership
- WatermarkTemplate: includes `{user}`, `{file}`, `{time}` tokens — must resolve in the rendered watermark
- OfflineLeaseMinutes: 0, 15, 60, 1440 (1 day)
- AllowPrint
- **MaxOpens** (v1.4.0): null = unlimited, 1, 3, 10

### T2.2 — Watermark template tokens

Edit watermark template to use every token (`{user}`, `{userId}`, `{file}`, `{fileId}`, `{time}`).
Open a file in the WPF viewer (or check the PDF print stamp output).
Confirm each token resolves correctly.

### T2.3 — Identity surface

| Subtab | Tests |
|--------|-------|
| Users | Create / edit / disable / delete a user. Filter by external ID. |
| Groups | Create group, add/remove members, delete group. |
| Agent devices | Register a device via API, view in admin, disable it, confirm `device_disabled` denial. |
| Admin console access | Create another admin user with limited permissions, confirm role-based access control. |
| Directory sync | If Entra ID configured, trigger sync. Otherwise leave alone. |

### T2.4 — File operations

| Subtab | Tests |
|--------|-------|
| Protected files | List with filters (content-type, owner, status). Apply policy template to a file. Add tag. |
| Share links | Create share link with `MaxUses=2`, redeem twice, confirm 3rd attempt fails. |
| Remote delete commands | Issue a delete command to a device, see it in the commands table. |
| Transparent-encryption | Configure auto-encrypt for `.docx` content type, verify file gets the DRM trailer. |
| Secure containers | Wrap files into a `.drm` container, import on another device. |
| File collections | Group files into a collection, apply a policy to the whole collection. |
| Batch file operations | Revoke 10 files at once, confirm all 10 audit events fire. |

### T2.5 — Integrations

| Integration | Smoke test |
|-------------|-----------|
| Email notifications | Save config with admin email, trigger an access denial, confirm email arrives. |
| Outlook add-in | Sideload manifest from `https://drm.zcr.ai/outlook-addin/manifest.xml`. Open Outlook, see the DRM ribbon button. |
| Box integration | Connect a Box tenant, upload a file, confirm it's auto-protected. |
| SIEM webhooks | Create a webhook, trigger any event, confirm POST arrives at the webhook URL. |
| Folder watcher | Install the Windows service, drop a file in the watched folder, confirm it gets a DRM trailer. |

### T2.6 — Tenants surface

| Subtab | Tests |
|--------|-------|
| Tenants | Create / suspend / resume / delete a tenant. |
| Tenant registrations | Approve a pending registration. |
| Access requests | Approve / deny a pending access request. |
| Tenant plans | Set plan tier, confirm encrypter count limit enforces. |
| Compliance & data retention | Trigger GDPR erase for a user, confirm their data is purged. |
| File retention policy | Set retention to 30 days, fast-forward the worker, confirm old files are deleted. |
| IP allowlist | Add `127.0.0.1/32` rule, try unwrap from non-allowed IP, see `ip_not_allowed`. |
| Device trust | Enable trust enforcement with 7-day check-in, expire a device's heartbeat, see `device_trust_expired`. |
| Key rotation | Trigger manual key rotation, confirm old + new wrapped keys both decrypt successfully. |
| Usage snapshot | View dashboard, sanity-check counts. |

### T2.7 — Brute-force protection (C2 detailed)

| Setting | Behaviour |
|---------|-----------|
| `GET /api/admin/brute-force-policy?tenantId=...` on new tenant | Returns `{enabled:true, threshold:10, windowMinutes:60, usingDefaults:true}` |
| `PUT` with `threshold=0` | HTTP 400 `invalid_threshold` |
| `PUT` with `windowMinutes=999999` | HTTP 400 `invalid_window_minutes` |
| `PUT` with valid values | HTTP 200, `usingDefaults=false` |
| `PUT enabled=false threshold=1` then 5 wrong codes | NO auto-revoke (disabled trumps threshold) |
| `PUT enabled=true threshold=3` then 3 wrong codes | Auto-revoke on 3rd, audit event `share_link_auto_revoked`, admin notification fires |
| `GET /api/admin/brute-force-policy/recent-failures?tenantId=...` | Lists recent failed attempts with guest email, IP, reason, timestamp |

---

## Tier 3 — Windows + Edge Cases (1 day)

### T3.1 — WPF viewer

| Step | Expected |
|------|----------|
| Build `Drm.Viewer.Windows` for Release | `dotnet publish -c Release` produces `Drm.Viewer.Windows.exe` |
| Launch viewer with a protected file (`drm-viewer.exe --open file.drm`) | File decrypts and renders |
| Watermark tiles visible | Per-frame overlay shows user + timestamp |
| Press Ctrl+P with Print allowed | PDF prints with diagonal watermark across every page |
| Press Ctrl+P with Print denied | Print blocked, message visible |
| Press Ctrl+C with Copy denied | Clipboard does NOT receive content |
| Press Ctrl+S with Export denied | Save dialog does NOT open |

### T3.2 — Screen-capture protection (C3) — **must run on real Windows**

| Step | Expected |
|------|----------|
| Open viewer with a file | Window renders normally on the monitor |
| Press Print Screen key | Status bar: "Screen capture blocked"; clipboard cleared |
| Open Snipping Tool, drag over the viewer | Captured image is a **black rectangle** (not the document) |
| Win+Shift+S, drag over viewer | Same: black rectangle |
| Win+G (Game Bar) screen record | Recording shows black for the viewer area |
| OBS Studio "Display Capture" mode, point at viewer | Viewer region is **black** in the OBS preview |
| Microsoft Teams call, share screen "Window" with viewer selected | Other participant sees **black** |
| Zoom screen share | Same: black |

### T3.3 — Office add-ins

| Step | Expected |
|------|----------|
| Outlook: sideload `https://drm.zcr.ai/outlook-addin/manifest.xml` | "DRM Protect" appears in compose ribbon |
| Compose email, attach a file, click DRM Protect | File auto-encrypted before send, recipient gets share URL not the raw file |
| Word: sideload `https://drm.zcr.ai/word-addin/manifest.xml` | "zcrDRM Protect" button in Home ribbon |
| Click it on an open Word doc | Doc gets protected, original file gets DRM trailer |

### T3.4 — Mobile / responsive (browser-only, no native app)

Open `/admin/` at viewport widths 375, 768, 1024, 1280, 1920:

- Rail collapses at < 820px to icon-only sidebar
- Tab nav scrolls horizontally instead of wrapping
- Settings form gets a framing border on mobile
- Hero pillars stack vertically below 820px
- No horizontal scroll on the page itself

### T3.5 — Long-running / load

- Create 1000 files, confirm admin file list paginates correctly
- Apply policy template to a 100-file collection, confirm batch operation completes
- Trigger 1000 access events, confirm audit table grows + SIEM webhook fires 1000 times
- Issue 100 share links, confirm `share_link_id` GUID collision rate is 0
- 24-hour soak: leave viewer open, confirm offline lease renewal works as expected

### T3.6 — Cross-browser

Run Tier 0 + Tier 1 on each browser in `01-environment.md`. Document any
browser-specific defects (typical: Safari date parsing, Firefox file-input
quirks).
