# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project loosely follows semantic versioning. Phase identifiers (5AL, 5AM, ...) come from the FinalCode parity roadmap in `docs/superpowers/plans/`.

## [Unreleased]

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
