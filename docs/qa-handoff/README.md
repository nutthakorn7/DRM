# zcrDRM — QA Engineer Handoff

> **Production:** <https://drm.zcr.ai> · **Version:** v1.6.1 · **Last shipped:** 2026-05-21
> **Status:** FinalCode functional parity complete (C1 + C2 + C3); brand identity shipped.

This package is everything a QA engineer needs to take over end-to-end testing
of zcrDRM. Read [01-environment.md](01-environment.md) first to set up your
test environment, then work through [02-test-plan.md](02-test-plan.md) by
priority tier.

---

## Files in this package

| File | Purpose | When to use |
|------|---------|-------------|
| `README.md` | This index | Start here |
| `01-environment.md` | Test environments + credentials + tools | First-time setup |
| `02-test-plan.md` | Prioritised test scenarios with expected results | Daily test execution |
| `03-test-data.md` | Tenant IDs, admin keys, sample files, recipient emails | Reference during testing |
| `04-bug-template.md` | Standard bug report format | When you find a defect |
| `05-feature-matrix.md` | Feature → status → owner → tests cross-reference | Track coverage |
| `06-known-issues.md` | Things we know about + workarounds | Avoid wasted tickets |
| `07-regression-checklist.md` | Pre-release smoke checklist (~10 min) | Before every deploy |
| `08-handoff-questions.md` | Open questions the eng team should answer | Unblock yourself |

## TL;DR for the impatient

1. **Smoke (5 min):** open <https://drm.zcr.ai/admin/>, create a test tenant
   via the welcome modal, watch the 5-step checklist tick through
2. **Critical path (45 min):** Tier 0 + Tier 1 scenarios in `02-test-plan.md`
3. **Full pass (1 day):** all 3 tiers + regression checklist

Bug ticket workflow → GitHub Issues on <https://github.com/nutthakorn7/DRM/issues>
with the template in `04-bug-template.md`.

---

## What's been built

| Category | Items |
|----------|-------|
| **Encryption** | AES-256 per file, RSA-2048 key wrapping, FIPS 140-2 ready cipher selection |
| **Policy enforcement** | Per-user open count limit, time-based expiry, instant revoke, IP allowlist, device trust, offline lease |
| **Watermarking** | Per-frame on-screen tiles + PDF print stamp with token substitution (`{user}`, `{time}`, `{file}`) |
| **Audit** | Tamper-proof append-only chain, SIEM webhook stream, CSV export, configurable retention |
| **External sharing** | Email-verified share links with brute-force auto-revoke after N failures |
| **Multi-tenant** | Per-tenant config, billing webhooks, suspension, plan tiers |
| **Integrations** | Box (cloud), Outlook add-in (Office), Folder watcher (Windows service) |
| **Admin console** | 6-tab UI: Overview, Identity, Policy, Files, Integrations, Tenants |
| **External viewer** | `/share/` guest verification flow |
| **Quick-share UI** | `/me/` drop-and-send for non-technical users |
| **Brand** | zcrDRM wordmark, 1200×630 social card, three product pillars |
| **Security** | WPF viewer with `SetWindowDisplayAffinity` screen-capture blocking |

## What's NOT yet built / known gaps

These are NOT defects — they're explicit non-goals or future work:

- ❌ **iOS / Android viewer apps** — out of scope, explicit user decision
- ❌ **Dark mode** — out of scope, explicit user decision
- ⏸️ **Postgres backup automation** — manual snapshots only; deferred until 2026-08-21
- ⏸️ **Uptime alerting** — no Pingdom/Better Uptime/Kuma; deferred until 2026-08-21
- 🚧 **Windows MSI installer pipeline** — viewer ships but customer install is manual
- 🚧 **API documentation site** — endpoints work, no public docs page
- 🚧 **Marketing landing page** at `drm.zcr.ai/` root (currently redirects to `/admin/`)
- 🚧 **Personalize modal on `/me/`** uses emoji icons (👤📚🎯🛠) instead of SVG; cosmetic

See `06-known-issues.md` for current quirks.
