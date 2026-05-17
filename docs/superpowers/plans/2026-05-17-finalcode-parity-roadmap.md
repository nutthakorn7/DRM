# FinalCode Parity Roadmap — Phase 5AL → Phase 6

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement each phase. Each phase is a self-contained sub-plan.

**Goal:** Close every remaining feature/UX gap between DRM and FinalCode, taking parity from **80% → 100%**.

**Source research:**
- FinalCode JP catalog (16 features, iCata viewer) — `/tmp/fc/page-*.jpg` screenshots
- FinalCode V6 English datasheet (19 features, FIPS, multi-SKU)
- Earlier feature gap analysis (Phase 5AI/5AJ/5AK shipped)

**Mandatory rule for every phase:** UI must ship on **both** admin console (`src/Drm.Server/wwwroot/admin/`) AND Windows desktop (`Drm.Agent.Tray.Windows` and/or `Drm.Viewer.Windows`). If purely server-side, document why no client UI is needed.

---

## Gap inventory (15 items, sized + ordered)

| # | Gap | Source | Effort | Phase |
|---|---|---|---|---|
| 1 | File tagging (group by category/importance) | V6 #17 | S | 5AL |
| 2 | Macro execution control | JP table | S | 5AL |
| 3 | Ownership transfer control | JP table | S | 5AL |
| 4 | License-tier feature flags (Standard/API/NetFolder/Remote-Delete) | V6 SKU | S | 5AL |
| 5 | Print watermark (separate from screen watermark) | JP #15 | M | 5AM |
| 6 | ZIP file conversion | JP table | S | 5AN |
| 7 | Dynamic policy push UI signal ("policies update live") | V6 #11 | S | 5AN |
| 8 | 3 sales use-case pages (admin docs) | V6 | S | 5AN |
| 9 | 透過暗号 transparent encryption (extension preserved) | JP #01, V6 | L | 5AO |
| 10 | セキュアコンテナ secure container (folder-level encrypt) | JP #04 | L | 5AP |
| 11 | 共有フォルダー自動暗号化 Windows Server module | JP #02 | L | 5AQ |
| 12 | CAD specialized compatibility certification (AutoCAD/SolidWorks/etc) | JP page 14 | M | 5AR |
| 13 | Mobile viewer (iOS + Android) | JP #09, Reader | XL | Phase 6 |
| 14 | License multiplier display (10 paid → 90 free viewers) | JP page 15 | S | 5AL |
| 15 | FIPS 140-2 Level 1 certification | V6 | external | n/a |

---

## Phase 5AL — Quick-wins bundle (S, ~3 days)

**Goal:** Close 5 small gaps in one phase to ship parity bumps fast.

### Tasks

#### Task 1: File tagging
- **Files:** Create `src/Drm.Server/Entities.cs` add `FileTagEntity` (TenantId, FileId, Tag, AssignedAtUtc)
- **Endpoint:** `POST /api/admin/files/{fileId}/tags`, `DELETE`, `GET ...?tag=X` for filter
- **Admin UI:** Tag input next to grant form; filter chip row above files table; tags rendered as colored badges on each file row
- **Windows tray UI:** "Default tag for protect" field saved with credentials
- **Tests:** 4 (add/remove/list/filter)

#### Task 2: Macro execution + Ownership transfer policy flags
- **Files:** Modify `src/Drm.Domain/Policy.cs` add `Permission.RunMacros` and `Permission.TransferOwnership` flags
- **Endpoint:** Existing `PolicyTemplate` endpoints accept new flags
- **Admin UI:** Two new checkboxes in Templates panel: "Allow macros" / "Allow ownership transfer"
- **Windows viewer UI:** Show macro/ownership permission state in PermissionText status bar (alongside existing copy/print/export indicators)
- **Tests:** 2 (flags persisted, viewer respects)

#### Task 3: License-tier feature flags
- **Files:** Create `src/Drm.Server/LicenseTier.cs` enum: Standard / Api / NetFolder / RemoteDelete / Mobile / All
- **Endpoint:** `GET /api/admin/license` returns enabled tiers from `Drm:License:EnabledTiers` config
- **Admin UI:** Header chip showing current tier ("Standard + API"), panels disabled/badged when tier missing
- **Windows tray UI:** "License" line in status footer with current tier
- **Tests:** 2 (config parsing, gated endpoint returns 403 when tier missing)

#### Task 4: License multiplier display
- **Files:** None — UI-only computation: `freeViewerCount = paidEncrypterCount × 9`
- **Admin UI:** Add a "License usage" summary card in Status panel showing `X paid encrypters · Y free viewers (X × 9)`
- **Tests:** none (presentation only)

#### Task 5: README + commit
Total: 8 tests, ~3 days. Parity 80% → **84%**.

---

## Phase 5AM — Print watermark (M, ~3 days)

**Goal:** Add a separate print-time watermark distinct from screen watermark, so paper printouts identify viewer.

### Tasks

#### Task 1: Schema extension
- Extend `WatermarkTemplateEntity` (already from 5AI) with:
  - `PrintWatermarkEnabled` (bool, default false)
  - `PrintWatermarkPattern` (string — separate token pattern from screen)
  - `PrintWatermarkOpacityPercent`, `PrintWatermarkPosition` (top/bottom/diagonal/all-pages)
- SQLite migration via existing column-add pattern

#### Task 2: Admin UI
- Extend anti-capture form with a `<details>` subsection "Print watermark"
- 4 controls: enable, pattern, opacity, position dropdown

#### Task 3: Print-time watermark rendering
- **Viewer (Drm.Viewer.Windows):** When user clicks Print, the watermark is composited onto a fresh PDF before sending to printer
- Use existing PDFsharp or QuestPDF library to overlay text on each page
- Render Pattern tokens like `{user} • {time} • {file}` resolved client-side

#### Task 4: Audit
- Audit event: `print_watermark_applied` with template ID and rendered text length

#### Task 5: Tests
- 3 (schema, admin endpoint, viewer composition)

#### Task 6: README + commit
Total: ~3 days. Parity 84% → **86%**.

---

## Phase 5AN — Distribution formats + UX showcase (M, ~3 days)

#### Task 1: ZIP file conversion
- Endpoint `POST /api/admin/files/{fileId}/convert/zip` returns `application/zip` stream containing the encrypted payload + a readme.txt explaining how to obtain DRM client
- Use `System.IO.Compression.ZipArchive`
- Admin UI: "Download as ZIP" button per file row
- Windows tray: "Export as ZIP" entry when right-clicking a protected file

#### Task 2: Dynamic policy push UI signal
- When admin updates a policy template, push a SignalR notification to admin browser console showing "Policy X updated, N files will pick up changes on next open"
- Use existing audit feed
- Admin UI: yellow toast banner

#### Task 3: 3 sales use-case pages (under `/admin/cases/`)
- Static HTML: targeted-attacks, insider-fraud, internal-data-repo
- Mirrored from V6 datasheet structure: WHAT / WHO / SOLUTION
- Linked from admin console nav as "Use cases"

#### Task 4: Tests + README + commit
Parity 86% → **88%**.

---

## Phase 5AO — 透過暗号 Transparent Encryption (L, ~1 week)

**Goal:** New file format that preserves extension (`.xlsx` stays `.xlsx`), so users open files in native apps and never see the encryption.

### Architecture

- Add an **MS Office structured storage** layer that wraps the original file
- File looks like a normal Office file to Explorer, but contains DRM payload + encryption envelope
- Shell extension intercepts `IExtractIcon` to draw a lock badge on the icon

### Tasks

#### Task 1: Transparent file format
- Create `src/Drm.Crypto/TransparentEnvelope.cs`
- Spec: prepend 16-byte magic + 256-byte DRM metadata header at the end of file as a custom Office storage part (works because Office formats are ZIP-based and ignore unknown parts)

#### Task 2: Shell extension
- New project `Drm.Agent.Shell.Windows` (C++/CLI or .NET COM)
- Implements `IShellIconOverlayIdentifier` to draw lock on protected files
- Right-click context menu: "Protect (Transparent)" / "Decrypt and Open"

#### Task 3: Server endpoint
- `POST /api/files/register-transparent` accepts file hash + metadata
- New permission set: transparent files cannot revoke (file is local — only audit possible)

#### Task 4: Admin UI
- "Transparent encryption" toggle in policy template editor
- Filter chip "Transparent" in files panel
- Lock badge on file rows

#### Task 5: Windows tray UI
- New tab "Transparent mode" with: enable toggle, folder picker for auto-protect

#### Task 6: Tests
- 5 (envelope round-trip, shell extension registration, server register endpoint, admin toggle, file load in real Word)

Parity 88% → **92%**.

---

## Phase 5AP — Secure Container (L, ~1 week)

**Goal:** Encrypt a folder as a single sealed container; files inside can link to each other (Ai→Ps→Ps).

### Tasks

#### Task 1: Container format
- `.drmcontainer` extension — single encrypted archive
- Internal structure: SQLite catalog + AES-CTR per-file blobs
- Cross-file link table: file A can resolve relative paths to file B

#### Task 2: "FinalCode Explorer" UI in Drm.Viewer.Windows
- New tab: Container view
- TreeView left + ListView right (file name, size, date)
- Double-click opens file in viewer with container context preserved

#### Task 3: Server endpoints
- `POST /api/admin/containers` create
- `GET /api/admin/containers/{id}` list contents
- `POST /api/admin/containers/{id}/add-file`
- Container shares a single policy across all contained files

#### Task 4: Admin UI
- "Containers" panel: list, create, add files, view audit

#### Task 5: Windows tray UI
- "Create container" button
- Drag folder onto window → container created

#### Task 6: Tests
- 5 (create, add, list, open, cross-link)

Parity 92% → **95%**.

---

## Phase 5AQ — Shared Folder Auto-Encrypt (L, ~1 week)

**Goal:** A Windows service that watches a folder on a file server and auto-encrypts files when saved.

### Tasks

#### Task 1: New project `Drm.FolderWatcher.Service`
- Windows Service host
- `FileSystemWatcher` on configured folders
- On `Created`/`Renamed` event, calls existing protect workflow

#### Task 2: Service config
- `appsettings.json`: watched folder list + tenant credentials + policy template ID per folder
- Admin endpoint: `PUT /api/admin/folder-watcher/config`

#### Task 3: Admin UI
- "Folder watcher" panel: list of watched paths + template + status (running/stopped/last event)

#### Task 4: Windows tray UI
- New row in status footer: "Folder watcher: running on X folders"

#### Task 5: Service installer
- PowerShell script for sc.exe registration

#### Task 6: Tests
- 4 (config save/load, watcher triggers protect, error path, multi-tenant isolation)

Parity 95% → **97%**.

---

## Phase 5AR — CAD certification + final polish (M, ~3 days)

**Goal:** Verify and document that DRM-protected files open correctly in major CAD applications.

### Tasks

#### Task 1: Compatibility matrix
- Document open/save/print test results for: AutoCAD 2024/2025, SolidWorks 2024/2025, eDrawings, ZWCAD, FILDER Cube, iCADMX, OrCad Capture, Siemens Solid Edge, XVL Studio/Player

#### Task 2: Admin docs page
- New `/admin/compatibility/` page with table

#### Task 3: Known-issue handler in viewer
- For verified-broken combos, show a friendly notice instead of opening

#### Task 4: README compatibility section

Parity 97% → **98%**.

---

## Phase 6 — Mobile Viewer (XL, 1-3 months — separate sub-project)

**Goal:** Native iOS + Android Reader apps matching FinalCode Reader UX.

### Sub-phases

- **6A:** iOS Reader (Swift, UIKit) — share-extension to open `.drmx` from Mail/Files, file list, message-from-sender card, PDF/Office viewer with watermark overlay
- **6B:** Android Reader (Kotlin, Jetpack Compose) — same scope
- **6C:** Mobile crypto module — wrap AES/RSA on platform crypto (CommonCrypto / Android Keystore)
- **6D:** FIPS 140-2 Level 1 mobile certification — paperwork + lab submission (parallel)

Parity 98% → **100%**.

---

## Summary timeline

| Phase | Duration | Parity after | Cumulative |
|---|---|---|---|
| 5AL Quick wins | 3 d | 84% | 3 d |
| 5AM Print watermark | 3 d | 86% | 6 d |
| 5AN ZIP + UX showcase | 3 d | 88% | 9 d |
| 5AO 透過暗号 | 7 d | 92% | 16 d |
| 5AP Secure Container | 7 d | 95% | 23 d |
| 5AQ Folder Watcher | 7 d | 97% | 30 d |
| 5AR CAD cert + polish | 3 d | 98% | 33 d |
| Phase 6 Mobile | 60-90 d | 100% | 90-120 d |

**Total to 98%: ~5 weeks of focused work**
**Total to 100% (incl. mobile): ~3-4 months**

---

## Execution rules (apply to every phase)

1. **Branch:** Use `superpowers:using-git-worktrees` to isolate each phase
2. **Tests:** Each phase ships with new tests, all 100% pass before merge
3. **UI on both sides:** Admin console + Windows desktop (per memory directive)
4. **Two-stage review:** spec compliance review, then code quality review
5. **Commit cadence:** one commit per task (~5-8 per phase)
6. **README:** Append phase section before declaring done

---

## Self-review checklist before declaring "100%"

- [ ] All 15 gap items have a phase
- [ ] Every phase has admin UI + (where applicable) Windows UI
- [ ] FIPS cert noted as external/compliance, not code
- [ ] Mobile is in its own sub-project (different stack)
- [ ] Sales/marketing pages are coded as static admin docs, not requiring a CMS
