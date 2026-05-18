# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project loosely follows semantic versioning. Phase identifiers (5AL, 5AM, ...) come from the FinalCode parity roadmap in `docs/superpowers/plans/`.

## [Unreleased]

**v1.1 enterprise — Slice 1: admin identity foundation (zero-downtime).**
First slice of the v1 → v1.1 enterprise upgrade. Adds a real identity layer
for admin users with per-admin API tokens and role-based permissions, while
keeping the v1.0.x `X-DRM-Admin-Key` shared-key auth working unchanged so
existing deployments do not break.

### Added
- `AdminUser`, `AdminRole`, `AdminApiToken` entities + raw SQL migration
  (idempotent on both SQLite and Postgres — drops into v1.0.1 databases
  without an EF migration step).
- Seed: 4 system roles (`SuperAdmin`, `TenantAdmin`, `Auditor`, `ReadOnly`)
  and a synthetic Default SuperAdmin (`00000000-aaaa-aaaa-aaaa-000000000001`)
  that shared-key callers authenticate as until per-admin tokens are
  issued. Audit rows from shared-key callers attribute to this id.
- `AdminIdentityMiddleware` (replaces `AdminApiKeyAuthentication` in the
  pipeline). Accepts either `X-DRM-Admin-Token` (per-admin, v1.1) or
  `X-DRM-Admin-Key` (shared, v1.0.x back-compat) and stamps an
  `AdminIdentity` (id, role, permissions, sharedKeyFallback flag) onto
  `HttpContext.Items` for downstream endpoints.
- 24-permission RBAC set (`admins:read`, `tenants:write`, `audit:export`,
  …) + `AdminIdentityContext.TryRequirePermission` helper. Permissions
  are stored CSV-on-role and re-synced on every boot from the code
  definitions in `AdminSystemRoles.PermissionsFor`.
- `/api/admin/identity/*` endpoints: `whoami`, `roles`, `admins` (list /
  create / disable / enable), `tokens/{id}/revoke`, and
  `admins/{id}/rotate-token`. Token plaintext is returned exactly once on
  create + rotate; the database stores only SHA-256 hashes.
- 9 tests under `AdminIdentityApiTests` covering: shared-key →
  default-admin resolution, invalid-token rejection, role listing,
  admin creation + token round-trip auth, one-time-only token visibility,
  cannot-disable-default-admin guard, revoked-token rejection, ReadOnly
  role cannot create admins, seed idempotency.

### Migration
- **No operator action required.** First start under v1.1 seeds the four
  system roles + default SuperAdmin into the existing database. The
  existing `Drm:Security:AdminApiKey` env var keeps working — it now
  resolves to the Default SuperAdmin identity, so existing automation /
  the admin console / curl scripts all continue to authenticate.
- To start using per-admin tokens: call `POST
  /api/admin/identity/admins` with `{ email, displayName, roleId }`
  using the shared key, then use the returned `token` as
  `X-DRM-Admin-Token` on subsequent requests. The shared key remains
  active in parallel until you choose to retire it (planned in a later
  slice).

### Not in this slice (Slice 2 work)
- RBAC enforcement on the 23 existing admin endpoints (currently every
  authenticated caller has effectively `*` because shared-key resolves
  to SuperAdmin and per-admin tokens get their full role permission set
  but no endpoint reads it yet).
- Admin UI panel for managing admin users + tokens.
- Refactoring existing audit writes to read actor from
  `HttpContext.Items["DrmAdminIdentity"]` instead of body
  `adminUserId`.
- Shared-key deprecation / removal path.

## [1.0.1] — 2026-05-18

**Security hardening — `X-DRM-Tenant-Id` migration completed.** Closes the
last coverage gaps in the SECURITY.md migration started in v1.0.0. Every
admin endpoint that takes a tenant ID in body or query string now rejects
mismatches between the body and the `X-DRM-Tenant-Id` header with
400 `tenant_mismatch`, and the cross-check extends one step beyond the
admin surface to the audit read endpoint.

### Changed
- `GET /api/admin/audit` + `/api/admin/audit.csv` now assert
  `X-DRM-Tenant-Id` matches the body tenant ID. Audit log is the highest-
  sensitivity admin read surface and was the largest remaining gap.
- `GET /api/admin/files/{id}/convert/zip` now asserts the header. ZIP
  export bundles manifest + share-link by tenant; mismatch is rejected.
- `POST /api/admin/policy-simulator` now asserts the header. Prevents
  cross-tenant policy probing with a leaked admin key.
- `POST /api/me/share` (quick-share) now asserts the header. `/api/me/*`
  is in scope per the SECURITY.md "canonical source across all /api/*
  endpoints" guidance.
- `GET /api/audit` (client-API-key surface) now asserts the header. The
  client API key is a single shared secret per deployment, so without the
  cross-check anyone holding the key could read any tenant's audit log by
  guessing the tenant GUID — same single-shared-key shape that motivated
  the admin migration.
- Admin UI dashboard probe (`admin/app.js`) now sends `X-DRM-Tenant-Id`
  on the `/api/audit` health check so the new server-side gate doesn't
  trip the dashboard.

### Tests
- +6 `tenant_mismatch` tests covering all five newly-protected endpoints
  (audit JSON + audit CSV + file-zip + policy-simulator + quick-share +
  client-key audit). Full server suite: 263/263 pass.

### Docs
- `SECURITY.md` migration log gains a `2026-05-18 expansion` subsection
  documenting the read-surface + non-admin tenant-scoped reads, and the
  scope rationale for why `/api/audit` was included but other client-API-
  key endpoints (Files, SCIM, ExternalShare, BoxWebhook, OutlookAddIn,
  Agent) were not.

## [1.0.0] — 2026-05-17

**The parity milestone.** Closes the FinalCode parity roadmap (Phases 5AH → 5AR
+ Phase 5B web polish) and ships the first production-grade, design-systemed
DRM platform. Server, viewer, agent, folder-watcher service, and three web
surfaces (admin / send-file / external viewer) are feature-complete against the
FinalCode V6 and FinalCode JP feature catalogs. Phase 6 (mobile viewer for iOS
and Android) is the next horizon.

**What's in this release:**
- Full SCIM 2.0 provisioning (Phase 5AH) for Entra ID / Okta / OneLogin
- File tagging, macro/ownership/permission controls, license-tier flags, license
  multiplier display (Phase 5AL)
- Print watermark separated from screen watermark, server-side PDF compositor
  (Phase 5AM)
- ZIP conversion, dynamic policy push UI signal, 3 sales-use-case admin pages
  (Phase 5AN)
- Transparent encryption with extension-preserving trailer + admin UI + tray
  + viewer integration (Phase 5AO)
- Secure container (folder-level encryption + viewer UI) with PBKDF2-SHA256
  at 600 000 iterations and per-container random salt (Phase 5AP)
- Windows shared-folder auto-encrypt service with cancellation-aware retry
  (Phase 5AQ)
- CAD compatibility matrix endpoint, viewer notices, admin reference page
  (Phase 5AR)
- Critical security remediations: startup guard for missing admin key /
  trailer secret, server-side HMAC stamp / verify (trailer secret never
  leaves the trust boundary), zip-slip rejection in container pack / unpack,
  folder-watcher graceful cancellation, viewer temp-file cleanup, LRU bound
  on the protection-tracker (C1–C4 + I4 + I7 + M2 + M4)
- Production readiness: GitHub Actions CI workflow + multi-stage Dockerfile
  for `Drm.Server` running as non-root
- Phase 5B web console polish: welcome screen, getting-started checklist,
  5-tab IA + per-tab sub-nav, settings drawer for license + health, sidebar
  collapse + auto-collapse on mobile, jargon tooltips, empty-state pattern,
  44px touch targets, focus-ring system, reduced-motion respect
- Three-tier design token system in `wwwroot/static/tokens.css`: primitives
  → aliases → component tokens
- Teal-on-slate brand direction unified across all web surfaces, replaces
  brown-on-cream
- IBM Plex Sans + IBM Plex Mono replace the Arial/system-stack default
- `DESIGN.md` source of truth for color, typography, layout, motion, and
  patterns

**Migration notes (from earlier dev builds):**
- Set `Drm:Security:AdminApiKey` in non-Development environments before
  starting the server — `SecurityStartupGuard` now refuses to start without it
- Set `Drm:Security:TransparentTrailerSecret` to a real value (not the
  documented placeholder) — guard refuses placeholder values
- Legacy v1 secure containers continue to open via `DeriveKeyLegacyV1`; new
  containers use v2 with per-container salt. No data migration required.

### Phase 5B — Web console polish + design system (2026-05-17, single session)

Took /admin/ from "functional but unfriendly" (audit grade B−) to A− across a
17-commit session. New user can land cold and ship a protected file without
asking IT. See `DESIGN.md` for the brand decisions and
`~/.gstack/projects/DRM/designs/design-audit-20260517/` for the full audit
report and screenshots.

**Onboarding & wayfinding**
- Welcome screen on first visit: one click generates a test Tenant ID, Admin
  Key, and Admin user ID. Replaces the "wall of GUIDs" greeting that previously
  greeted new users. Dismissed state in `localStorage.drm:bootstrapped`.
- Getting Started checklist on the Overview tab: 5 numbered steps with action
  buttons that auto-check as the user completes each step. Steps fire custom
  `drm:onboarded` events so other parts of the app can listen.
- "First time?" pill next to Tenant operations heading with hover hint.
- Plain-language `ⓘ` info pills on every jargon panel heading (Transparent,
  Containers, SIEM, Folder watcher, Watermarks, Simulator, …). Tooltips defined
  in `PANEL_TIPS` map; tab labels get tooltips from `TAB_TIPS`.

**Information architecture**
- 19 admin panels collapsed into 5 tabs (Overview, Identity, Policy, Files,
  Integrations) via `data-tab` attribute + body-level visibility CSS.
  Persisted active tab in `localStorage.drm:adminActiveTab` and URL hash
  (`#tab-identity`).
- Sub-nav inside each tab: dynamic pill nav rendered from panels grouped under
  the active tab, showing one panel at a time. Per-tab active sub-tab in
  `localStorage.drm:adminActiveSubtab:{tab}`. Getting Started pinned to position
  1 on Overview.
- License + Server Health moved out of Overview into a slide-in **Settings
  drawer** (gear button in page header → drawer from right, 420px, 220ms ease).
  Backdrop + Escape + click-outside all close. Body scroll-locked while open.
- Search mode (`data-active-tab="all"`) clears sub-nav and reveals every panel.
- Sidebar global-nav simplified: 3 cross-surface links (Admin / Send / Open
  shared) + Reference sub-nav (Use cases, Compatibility). Auto-collapses to
  64px icon-only on `≤820px` viewports unless the user explicitly toggles.

**Visual system**
- Three-tier design token system shipped in `src/Drm.Server/wwwroot/static/tokens.css`:
  primitives (color scales, spacing 4-64px, type 11-36px, radii, shadows,
  motion, z-index), aliases (`--accent`, `--page`, `--rail`, `--ink`), and
  component tokens (`--btn-*`, `--field-*`, `--card-*`, `--pill-*`). Surface
  CSS now overrides only what's specific; one source of truth across all 5
  surfaces.
- **Teal-on-slate** palette unified across `/admin/`, `/me/`, `/share/`,
  `/admin/cases/`, `/admin/compatibility/`. Replaces the prior brown-on-cream
  scheme. `/share/`'s teal (judged the most polished surface in the audit) was
  promoted to the system spine.
- **IBM Plex Sans + IBM Plex Mono** loaded via Google Fonts with `preconnect`
  and `font-display: swap`. Replaces `Arial, Helvetica, sans-serif` system
  stack (the "I gave up on typography" signal). OpenType stylistic sets
  ss01/ss03/cv02 enabled for crisper numerals.
- Heading scale on a 1.25 major-third ratio: h1=28 / h2=22 / h3=18 / h4=16,
  applied globally via tokens.css so every surface inherits.
- All interactive elements bumped to **44px min-height** (WCAG/Apple HIG touch
  target). Secondary nav pills allowed down to 36px. Sub-nav pills, primary
  buttons, tab links, sidebar links, and inputs all conform.
- Focus-ring system: `--focus-ring` (3px teal glow at 35% opacity) applied via
  `:focus-visible` on all buttons/links and `:focus` on form fields across
  every surface. Never `outline: none` without replacement.
- `prefers-reduced-motion: reduce` honored globally — collapses all
  `--duration-*` tokens to 0ms and clamps any un-tokenized transition to
  0.01ms (belt-and-suspenders).

**Empty states**
- New `.empty-state` card pattern (dashed border + icon + title + hint) lives
  in tokens.css.
- `emptyStateRow(colspan, opts)` helper embeds it inside a `<td>` for table
  bodies.
- Shipped to Users, Groups, Files, Templates, Devices admin panels — each with
  a panel-specific icon and a one-sentence hint that tells the new user the
  concrete next step.
- `/share/` preview pane now carries `data-has-session="false"` until the
  viewer session loads; empty state shows "Document preview will appear here"
  with a lock icon, hiding the previous `dl`-with-dashes placeholder. Disabled
  Download/Print/Export action buttons also hidden until verification completes.

**`/share/` flow gating**
- Step 2 ("Confirm code") is visually + functionally locked until step 1
  completes: opacity 0.45 + grayscale 0.3 + `pointer-events: none`. Transitions
  cleanly to active state when verification ID is set.
- Native browser validation popup replaced with custom validation written to
  `#viewerStatus`. Both forms marked `novalidate`; `required` attributes
  stripped. Errors now match the design system.

**`/me/` decluttering**
- Killed the persona modal + 4-step product tour that previously blocked the
  send-file form on first visit. The form is self-evident (drop zone +
  recipient + Send button) and doesn't need a tour. Audit measured the two
  modals draining 30 goodwill points per first visit.
- Persona picker survives as an opt-in "Personalize" link in the topbar that
  clears the `localStorage` gate and re-opens the picker on demand.
- Topbar nav touch targets bumped to 48px tall.

**`/admin/cases/` and `/admin/compatibility/` docs shell**
- New sticky brand bar (logo + "DRM" + "Reference" eyebrow + back-to-admin link).
- Sticky TOC sidebar with scroll-spy. `/admin/compatibility/` TOC is
  auto-populated after the matrix loads — one entry per category with row
  count, max-height with independent scroll when the matrix is tall.

**Bug fixes shipped during audit**
- `fix(admin): settings drawer panels were display:none` — the tab-visibility
  rule was hiding every `section.panel` not matching the active tab, including
  drawer panels (no `data-tab`). Scoped the rule to `.surface` only so drawer
  panels render correctly.
- `fix(admin): welcome-screen hidden attribute now actually hides it` — the
  class selector's `display: grid` was overriding the `hidden` attribute's
  user-agent `display: none`, leaving "Create test tenant" and "Skip"
  visually present but with no click handlers attached (the IIFE early-returned
  when a tenant already existed in localStorage).
- `fix(me): kill blocking persona + tour modals on first visit` — see above.

**Design system source of truth**
- New `DESIGN.md` in repo root captures the brand decisions, the three-tier
  token rationale, the surface-by-surface chrome map, and the AI-slop blacklist
  the team committed to avoiding. Future palette / typography / layout changes
  should calibrate against this document.

### Security — Code-review remediation (C1–C4 + I4 + I7 + M2 + M4)

A code review of phases 5AL–5AR surfaced four Critical and several Important findings. Fixed in this release.

- **C1 + C3 — Startup guard.** New `SecurityStartupGuard` refuses to start the server in non-Development environments when `Drm:Security:AdminApiKey` is blank, when `Drm:Security:TransparentTrailerSecret` is blank, or when the trailer secret is left at the documented placeholder value. `AdminApiKeyAuthentication` now returns `503` for `/api/admin/*` when the admin key is unconfigured in non-Development environments (Development still allows blank key for local tooling).
- **C2 — Trailer secret no longer leaves the server.** New `POST /api/admin/transparent-files/stamp` accepts file bytes (base64) and returns the stamped bytes; the HMAC key stays inside the trust boundary. New `POST /api/admin/transparent-files/verify` validates trailers server-side and returns the parsed metadata. The legacy `GET /api/admin/transparent-files/secret` endpoint is now gated by `Drm:Security:AllowTrailerSecretDistribution=true` and returns `404` by default. Windows tray and Windows viewer were refactored to use the new stamp / verify endpoints.
- **C4 — SecureContainer crypto strengthened.** Bumped PBKDF2-SHA256 iterations from 100 000 to 600 000 (OWASP 2023 guidance) and added a random 16-byte per-container salt written into a new v2 header. `DeriveKey(passphrase, salt)` replaces `DeriveKey(passphrase, containerId)`; `TryReadSalt(container)` exposes the salt for unpackers; `DeriveKeyLegacyV1(passphrase, containerId)` is kept (marked obsolete) so existing v1 containers still open. New `ValidateRelativePath` rejects absolute paths and `..` traversal in both Pack and Unpack to close a latent zip-slip risk.
- **I4 — Folder-watcher cancellation.** `FolderWatcherWorker` now threads `stoppingToken` through `HandleEventAsync` → `FolderProtector.ProtectAsync` → `ReadFileWithRetryAsync` so in-flight protects abort cleanly on service shutdown and won't loop after a watched folder is removed.
- **M2 — Print-watermark temp files.** The viewer tracks every stamped temporary PDF in a list and deletes them all on `OnClosed`, preventing sensitive watermark-stamped copies from accumulating in `%TEMP%`.
- **M4 — Tracker memory bound.** `FolderProtectionTracker` is now an LRU capped at 50 000 entries (oldest evicted in insertion order) so long-running services on large file shares cannot leak unbounded state.

### Added — Production Readiness
- GitHub Actions CI workflow (`.github/workflows/ci.yml`) building all non-Windows projects on Ubuntu, all Windows projects on Windows, and a Docker image build job
- `Dockerfile` for `Drm.Server` with multi-stage build and non-root runtime user
- `.dockerignore` excluding build artefacts and secrets
- `docker-compose.yml` for one-command local stand-up
- `CHANGELOG.md` and `CONTRIBUTING.md`

## Parity push — FinalCode roadmap (2026-05-17, single session)

Seven phases shipped in one session taking DRM-vs-FinalCode parity from **80% → 98%**. Each phase ships with admin console UI and Windows desktop UI per the project's UI-on-both-surfaces rule.

### [5AR — Compatibility Matrix + Final Polish] · `f90eb1c`
- `CompatibilityMatrix` server roster for documents (Office 2019-2024, Acrobat 2024, Ichitaro, DocuWorks), design (Illustrator/Photoshop/InDesign), CAD (AutoCAD 2020-2025, SolidWorks 2024/2025, Solid Edge 2022, OrCad, XVL, iCAD SX, FILDER Cube, iCADMX, ZWCAD), video (WMP), simulation (Simulink)
- `GET /api/admin/compatibility-matrix` endpoint
- `/admin/compatibility/` static page with status badges (verified/warn/broken)
- `CompatibilityNotices` mirror in Drm.Viewer.Windows surfacing known-issue guidance on open
- 3 new tests · 218/218 total pass · parity 97% → 98%

### [5AQ — Folder Watcher Windows Service] · `f93823e`
- New `Drm.FolderWatcher.Service` Worker / Windows Service project
- `FolderWatcherOptions`, `FolderProtectionTracker` (anti-loop), `FolderProtector`, `FolderWatcherWorker` (BackgroundService polling config + driving FileSystemWatcher)
- Uses Phase 5AO transparent envelope for on-disk format
- `TenantFolderWatcherConfigEntity`, `FolderWatcherEventEntity` with SQLite migration
- 5 admin endpoints: PUT/GET config, POST report (liveness), POST/GET events
- Admin console panel + Windows tray status row with green/amber/grey/red dot
- 7 new tests · 215/215 total pass · parity 95% → 97%

### [5AP — Secure Container] · `8d2a175`
- `Drm.Crypto.SecureContainer` AES-GCM sealed folder archive (`.drmcontainer`)
- PBKDF2-HMAC-SHA256 key derivation (100k iters, container GUID as salt)
- `SecureContainerEntity` + `SecureContainerFileEntity` + SQLite migration
- 4 admin endpoints (register/list/get/delete) + audit event
- Admin Containers panel
- Windows tray drag-folder drop zone with passphrase input
- Windows viewer drop-to-open flow with manifest listing
- 8 new tests · 208/208 total pass · parity 92% → 95%

### [5AO — Transparent Encryption] · `d69c726`
- `Drm.Crypto.TransparentEnvelope` tamper-evident HMAC-SHA256 trailer appended to original files (`.xlsx` stays `.xlsx`)
- `TransparentProtectedFileEntity` + SQLite migration
- 5 admin endpoints (register, list, get, delete, secret retrieval)
- Admin Transparent panel
- Windows tray drop zone — drops produce `<name>-drm<ext>` and auto-register
- Windows viewer drop detection with yellow banner showing tenant/file/registered/size
- 9 new tests · 200/200 total pass · parity 88% → 92%

### [5AN — ZIP Convert + Policy Push Toast + Sales Use Cases] · `8b375b1`
- `GET /api/admin/files/{fileId}/convert/zip` returning README + manifest + share-link archive
- Admin Files per-row ZIP link
- Policy push toast on apply-template / watermark-update
- `/admin/cases/` 3-card static page (Targeted attacks / Insider / Data repos)
- `Use cases ↗` admin nav link
- 3 new tests · 191/191 total pass · parity 86% → 88%

### [5AM — Print Watermark] · `5f15e26`
- `WatermarkTemplateEntity` gains `PrintWatermarkEnabled`, `PrintWatermarkPattern`, `PrintWatermarkOpacityPercent`, `PrintWatermarkPosition` (diagonal/top/bottom/all-pages) with SQLite migration
- Admin anti-capture form gains a Print watermark fieldset
- Windows viewer toolbar adds `Print WM` checkbox + pattern textbox
- `Drm.Viewer.Windows.PrintWatermarkComposer` using PdfSharp 6.2.1 to stamp PDF pages with token-resolved overlays
- 2 new tests · 188/188 total pass · parity 84% → 86%

### [5AL — Quick-Wins Bundle] · `180bd23`
- `FileTagEntity` + 5 tag endpoints (add/remove/list/summary/files-by-tag) + admin Add-tag form + filter chip row + Default-tag tray field
- `Permission.RunMacros` and `Permission.TransferOwnership` flags + admin Templates checkboxes + viewer status badges
- `LicenseTier` flag enum + CSV parser + `GET /api/admin/license` + admin License chips panel + Windows tray License status line
- Free-viewer multiplier (paid × 9) computed server-side
- 8 new tests · 186/186 total pass · parity 80% → 84%

### Plan
- `docs/superpowers/plans/2026-05-17-finalcode-parity-roadmap.md` — 8-phase roadmap from 80% to 100% parity covering 15 gap items · `a40eb01`

## Earlier work (Phase 5A — 5AH)

Earlier in the project lifecycle the team shipped phases covering foundation MVP, admin audit, agent control plane, offline policy cache, command queue, file protection, key wrapping, management console shell, audit/SIEM consoles, client API keys, device inventory + disable, viewer permission controls, policy simulator, watermark templates and alias rendering, external share verification + redemption, browser viewer shell, integration CLI, Entra ID directory sync, admin email notifications, and SCIM 2.0 provisioning. See the `docs/superpowers/plans/` directory for the per-phase implementation plans.

Phase 6 (Mobile Reader iOS + Android + mobile crypto + FIPS cert) is the remaining 2% and is intentionally deferred to a separate sub-project.
