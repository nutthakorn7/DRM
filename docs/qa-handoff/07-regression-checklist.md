# Pre-release Regression Checklist (~10 minutes)

Run this **before every deploy to drm.zcr.ai**. It catches the bugs that
have actually hit production in the last six versions.

If any line fails, **block the deploy**.

---

## A — Connectivity (1 minute)

- [ ] `curl https://drm.zcr.ai/healthz` returns `{"status":"ok"}`
- [ ] `curl -I https://drm.zcr.ai/admin/` returns HTTP 200, content-type text/html
- [ ] `curl -I https://drm.zcr.ai/static/favicon.svg` returns HTTP 200, content-type image/svg+xml
- [ ] `curl -I https://drm.zcr.ai/static/og-card.svg` returns HTTP 200, content-type image/svg+xml

## B — Brand integrity (1 minute)

Open `/admin/` in a fresh incognito window:

- [ ] Page `<title>` is `zcrDRM — Admin console`
- [ ] Wordmark shows `zcrDRM` (with `zcr` in teal, `DRM` in dark ink)
- [ ] Subtitle under wordmark: `drm.zcr.ai · Admin console` (mono chip)
- [ ] Favicon shows in browser tab (teal tile with white seal)
- [ ] Welcome modal title: `zcrDRM — self-hosted DRM, ready in 5 minutes`
- [ ] Welcome modal has 5 steps (NOT 4)

## C — Console errors (1 minute)

DevTools → Console with no extensions enabled:

- [ ] `/admin/` shows 0 errors
- [ ] `/me/` shows 0 errors
- [ ] `/share/` shows 0 errors

## D — Tab navigation regression (v1.2.x guard) (2 minutes)

- [ ] Click each of the 6 admin tabs in order:
      Overview → Identity → Policy → Files → Integrations → Tenants
- [ ] On each, `document.body.dataset.activeTab` matches the clicked tab
- [ ] **Tenants tab** specifically shows 10 subtabs (not redirecting to Overview)
- [ ] Each tab's URL fragment updates (`#tab-X`)

## E — `/me/` visibility (v1.6.1 guard) (1 minute)

In a fresh incognito window:

- [ ] `Admin →` link in topbar is NOT visible (`#adminLink` should have `display: none`)
- [ ] Page header shows: Send · Open shared file · Personalize (3 items, NOT 4)

## F — Policy template MaxOpens (v1.4.0 guard) (1 minute)

- [ ] Create a policy template with `maxOpens: 3` via the create form
- [ ] Template appears in the table with "Max opens: 3 / user"
- [ ] Get template via API:
      `GET /api/admin/policy-templates/{templateId}?tenantId=...`
      → response includes `"maxOpens": 3`

## G — Brute-force policy (v1.5.0 guard) (1 minute)

- [ ] `GET /api/admin/brute-force-policy?tenantId=$NEW_TENANT`
      → returns `{enabled:true, threshold:10, windowMinutes:60, usingDefaults:true}`
- [ ] `PUT` with `threshold=0` → HTTP 400

## H — Mobile responsive (1 minute)

DevTools → device toolbar, switch to 375×800:

- [ ] Rail collapses to icon-only
- [ ] Tab nav scrolls horizontally (does NOT wrap to 2 rows)
- [ ] Settings form has framing border (intentional panel feel)
- [ ] No horizontal scroll on the page itself

## I — Social-share preview (1 minute)

- [ ] Paste `https://drm.zcr.ai/admin/` into Slack message field
- [ ] Slack preview shows 1200×630 og-card poster (NOT 32×32 favicon)
- [ ] Preview text: "zcrDRM — Admin console" / "Self-hosted DRM, ready in 5 minutes..."

## J — CI status (1 minute)

- [ ] `gh run list --branch master --limit 1 --json conclusion` → `success`
- [ ] No pending PRs with failed required checks
- [ ] `git log master --oneline -5` matches the released version

---

## Pass / Fail

Tick every box → **deploy OK to proceed**.
Any box red → block the deploy, file the bug, ping eng on-call.

## Where the regressions originally bit us

| Box | Bug it catches |
|-----|----------------|
| D (Tenants tab) | v1.2.x: Tenants tab was silently broken — `VALID_TABS` didn't include `tenants` |
| E (Admin link) | v1.6.0: CSS specificity bug exposed `Admin →` to non-admins on `/me/` |
| F (MaxOpens) | v1.4.0: template field could be lost in transit if response DTO wasn't updated |
| G (Brute-force) | v1.5.0: validation rejected legitimate values if `Threshold` clamp wrong |
| C (Console errors) | v1.3.x: brand work introduced JS errors on /share/ without a test catching it |
| I (Social preview) | v1.3.1: og:image pointed to 32×32 favicon by default; needs 1200×630 |
