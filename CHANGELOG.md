# Changelog

All notable changes to this project are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project loosely follows semantic versioning. Phase identifiers (5AL, 5AM, ...) come from the FinalCode parity roadmap in `docs/superpowers/plans/`.

## [Unreleased]

Recipient in-browser preview, a CI gate for the browser test suite, two design-system audits, and a CRUD-normalization audit (consistency fixes + two compliance decisions). Shipped as PRs #54–#60 on 2026-06-12.

**2026-06-19 — audit-chain fix + device trust + retention (PRs #46, #50/#51, #66–#68).** A fresh-eyes code review of the #54–65 work caught that the "tamper-evident" audit hash chain was never actually built in production. Fixed, hardened with a secret key, and deployed to prod (verified `valid:true`); audit retention was wired on; and the internal-CAD/AD device-trust feature landed.

- **Audit hash chain was never built in production (#66, Fixed/Security).** `AuditChainService.AppendAsync` had zero callers — `GET /api/admin/audit/chain/verify` returned `missing_chain_entry` for any tenant with ≥1 event, so the "tamper-proof audit" guarantee (and #60's retention design built on it) didn't hold. Now a `SaveChanges` interceptor chains every audit-event insert, an idempotent O(n) startup backfill repairs existing tenants, and the timestamp is folded as Unix-ms so the hash survives Postgres microsecond truncation.
- **Audit chain is now secret-keyed (#67, Security).** `Drm__Security__AuditChainKey` (auto-generated for new installs) — the chain HMAC had been using an empty/public key, so it verified but was forgeable by anyone with DB write access. Now genuinely tamper-evident.
- **Audit retention is configurable + on (#68, Added).** `Drm__Audit__RetentionDays` exposed in the deploy config (new installs default 365; set to 365 on prod). It had been hard-default `0` = disabled, so the depersonalization shipped in #60 never actually ran.
- **Internal CAD protection + AD device trust + signed agent requests (#50/#51, Added/Security).** The Windows agent reports device posture and signs every request (HMAC over a canonical form incl. nonce + timestamp); the server gates key release on a registered, signed device. Hardened with replay protection (nonce/timestamp, ±5-min window, on register/heartbeat/unwrap) and a named-pipe ACL on the agent's signing IPC. *Honest scope: AD posture is **self-reported by the agent, not server-verified** — "registered agent + signed requests," not cryptographic AD attestation. GA follow-ups (asymmetric/TPM device keys, etc.) tracked on #51.*
- **Policy-template macros/transfer-ownership returned HTTP 400 (#66, Fixed).** The `RunMacros`/`TransferOwnership` checkboxes were live in the UI and read by the Windows viewer, but `PermissionParser`'s mask omitted them, so ticking either failed create + update. Added to the mask.
- **Preview object-URL leak + tick-boundary edge (#66, Fixed).** `/share/` preview revokes the prior blob URL on re-preview; `dotnetTicks` strips fractional seconds before parsing so a `.9999999` second-boundary can't roll the whole second.
- **NU1903 / CVE-2025-6965 downgraded to a non-fatal build warning (#66, Build).** A newly-published HIGH advisory in the transitive, dev/test-only SQLite native lib (prod is Postgres; no upstream patch exists) was failing `restore` repo-wide under `TreatWarningsAsErrors`. Only NU1903 is exempted; every other advisory still fails. Remove when SQLitePCLRaw ships a patched bundle.
- **Demo-doc claims un-hedged (#46, Docs).** Restored the Stage 18 (My Shares) + Stage 20 (self-revoke) "live, demo it" claims in the demo + CISO scripts (hedged in #45 while prod lagged; verified accurate against current prod).

### Added

- **In-browser document preview for View-only shares (#54).** Verified recipients of a View-only share can now read the `.drmx` in the browser — no desktop viewer needed. New `POST /api/share-links/viewer/content-key` releases the unwrapped AES key only when the session is verified+opened and the share is *exactly* `View` (anything richer is desktop-viewer-only, since a browser can't enforce print/copy/export). Decryption is fully client-side (`wwwroot/share/drmx-preview.js`, Web Crypto AES-GCM) — the ciphertext never touches the server, preserving the "file never touches our servers" model. The load-bearing detail: the JS reconstructs the .NET 100-ns tick timestamp for the GCM associated data byte-for-byte, verified in a real Chromium against a C#-written container.
- **CI now runs the Playwright UI suite (#56).** New `ui-tests-linux` job runs `Drm.UI.Tests` (admin console, `/share/` viewer, in-browser decrypt round-trip) against real Chromium on every push/PR. The suite previously only ran on the dev machine, so a `wwwroot` regression could land with CI green. Pre-builds the server in Debug and caches the browser to stay inside the fixture's 45 s health-check window.
- **Policy-template editing (#59).** New `PUT /api/admin/policy-templates/{id}` + a per-row Edit affordance in the admin console — policy templates were create-and-read-only while watermark templates already had update. Hard delete intentionally omitted (matches watermark templates; no FK cascade would orphan referencing files).
- **SIEM webhook delete (#59).** New `DELETE /api/admin/siem-webhooks/{id}` + UI button. SIEM webhooks were create-and-list-only while the structurally identical billing webhooks had full CRUD.
- **Agent device re-enable (#59).** New `POST /api/admin/devices/{id}/enable` + UI button — disabling a device was one-way from the console.
- **File-tag removal in the UI (#59).** The `DELETE …/tags/{tag}` route already existed; the admin console never exposed it. Added a Remove button.

### Fixed

- **Alert-rules admin panel never rendered (#59).** `refreshAlertRules`/`openEditAlert` tested `response.ok` and called `.json()`, but `apiFetch` resolves to the parsed body (like the rest of `app.js`) — so the guard always bailed and the alert-rules + alerts-fired tables stayed empty and Edit silently no-opped. Now uses the body directly.
- **Identity sub-nav test was stale, not flaky (#55).** `Tabs_switch_active_panel_and_subnav_rebuilds` hard-coded a 4-panel expectation; the Identity tab grew a 5th panel (`adminIdentity`). The assertion now derives the expected panel set from the DOM.
- **Design-system adoption (#57, #58).** Two rounds against `DESIGN.md`: `/me/` brought back onto the teal/Plex token system (off-brand blue hover, retired 🔒 emoji logo, raw Tailwind grays), one status-color system across all surfaces (success green had rendered as four different hexes), touch targets raised to the documented 44/36 px floors, a shared 760/820 px breakpoint policy written into `DESIGN.md`, font-stack/radii tokens, `aria-live` on the dynamic admin/`/me/` surfaces, and a single `h1` per page. Design score B → ~A-.

### Security

- **`revoke` now crypto-shreds the wrapped key (#60).** Revoking a file (both `/api/files/{id}/revoke` and the admin path) deletes the wrapped `FileKey` in the same transaction as the `Revoked` flag — previously it was flag-only and the key lived on. Each file's key is unique, so removing the row makes the `.drmx` unrecoverable in the live system even with the master key. Data retention applies the same shred and now cascades to a purged file's grant/tag/access-count/collection-item rows (no FK cascade exists, so it had been orphaning them). *Caveat for compliance claims: this destroys the key in the live system; a wrapped-key blob already in a backup is recoverable until that backup ages out.*

### Changed

- **Audit retention depersonalizes instead of deleting (#60).** The retention worker no longer deletes `AuditEvents` (which broke `/audit/chain/verify` on every run — the verifier seeds the chain genesis from the first surviving event). It now nulls the personal columns (`UserId`/`FileId`/`ActorAdminId`) and clears `ReasonCode`. Because the HMAC chain hashes only `Id`/`TenantId`/`EventType`/`CreatedAtUtc`, the tamper-evident chain still verifies — so PDPA data-minimization and "tamper-proof audit" now both hold.

## [1.7.0] — 2026-05-21

**Sender easy-to-use sweep — eight stages (13-20) take Quick Send from "encrypts and shows a URL" to "one-click, file attached, recipient verified, sender self-serve."**

The goal: cut every keystroke and friction step that wasn't strictly necessary, and give the sender the same visibility into their own shares that admins already had.

### Added

- **Stage 13 — Real `.drmx` from Quick Send.** Right-click → Quick Send now runs `ProtectFileWorkflow.ProtectAsync` so the agent actually encrypts the file (AES-256-GCM, content key wrapped server-side) and writes `<name>.drmx` next to the source. Before, Quick Send only POSTed bytes to `/api/me/share` and silently dropped them — a known broken path that Stage 12's token-format fix exposed.
- **Stage 14 — Outlook COM auto-attach.** When Outlook is the default mail client the agent activates it via late-bound COM (`Outlook.Application` ProgID + `MailItem.Attachments.Add`) so the `.drmx` is already in the attachments tray when the composer opens. Sender clicks Send. Falls back to `mailto:` + manual drag-from-Explorer when Outlook isn't installed. Status text branches per-path so the body never lies about attachment state.
- **Stage 15 — Recent-recipients dropdown.** Quick Send + Folder Share recipient boxes are now editable `ComboBox`es backed by a shared MRU store at `%LOCALAPPDATA%\zcrDRM\recent-recipients.json` (capacity 20, case-insensitive dedup, persist-on-success only). Typing behaves identically to the previous TextBox if the user ignores the dropdown.
- **Stage 16 — Post-success picker reset + honest no-mail-client error.** After a successful Quick Send, the file picker (`quickPickedFile` + `QuickDropFile.Text`) resets so the next file drop just works; recipient stays so "same recipient, different file" is one drop away. When both Outlook COM and `mailto:` fail, the status line says so explicitly with the Windows Settings fix path — no more fake "✅ composer opened" claim.
- **Stage 17 — Launch-time mail-client warning banner.** Agent probes `HKEY_CLASSES_ROOT\mailto\shell\open\command` at MainWindow load and surfaces a yellow banner at the top of the Quick Send tab if no handler is registered. Catches missing default mail client before the demo button is pressed.
- **Stage 18 — Sender-side "My Shares" view.** New `GET /api/me/shares?tenantId=X&userId=Y&limit=10` endpoint JOINs `ExternalShareLinks` against `ProtectedFiles` filtered to the caller's `OwnerUserId`. `/me/` renders the last 10 shares with recipient/expiry/opens/permissions/status. Auto-refreshes after each send so a new row lands at the top without a page reload. Privacy guard: filters strictly on owner; cross-user leak prevention covered by a dedicated test.
- **Stage 19 — Bulk Quick Send.** Recipient field accepts comma/semicolon/newline-separated emails. Agent encrypts the file once, mints one share-link per recipient (each with its own access token), opens one composer per recipient. Per-recipient share-link design (not multi-guest link) so each verification is independent, each audit row is distinct, and no recipient ever sees another's token.
- **Stage 20 — Self-revoke from My Shares.** New `POST /api/me/shares/{shareLinkId}/revoke` endpoint flips `Revoked=true` after verifying the caller owns the underlying file. UI: per-row Revoke button that hides on already-dead rows; `confirm()` dialog before the call. Audit `ReasonCode` is `external_share_link_self_revoked` so the audit trail distinguishes user action from admin revoke or brute-force auto-revoke. Idempotent — re-POSTing a revoked share returns 200 silently.

### Cross-cutting infrastructure

- **`Drm.Agent.Core.EmailComposer`** — new abstraction (`IEmailComposer`, `EmailComposition`, `EmailComposeResult`) so the mailto/Outlook fallback is testable cross-platform. `MailtoEmailComposer` lives in Core; `OutlookComEmailComposer` lives in the WPF tray project (Windows-only). Stages 14 + 16 depend on this.
- **`Drm.Agent.Core.BulkRecipientParser`** — cross-platform pure-string parser. Splits on comma / semicolon / newline / tab (so Excel-paste works), drops segments missing `@`, dedups case-insensitively.
- **`Drm.Agent.Core.RecentRecipientsStore`** — JSON-backed MRU store. Mirrors the `JsonFileKeyStore` / `JsonProtectedFileInventory` pattern (atomic write via temp+rename, corrupt-file recovery, capacity cap).

### Compose env vars in production docker-compose

- `Drm__Email__SmtpHost` / `SmtpPort` / `SmtpUsername` / `SmtpPassword` / `FromAddress` / `FromName` now wired in `deploy/management/docker/docker-compose.yml`. Aligns the upstream compose with the manual patch applied during the May 21 Resend wire-up so a fresh deploy doesn't drift from prod.

### Demo collateral

- **CISO answer script (`10-ciso-answer-script.md`)** refreshed twice in this release:
  - First pass added Q&As for share-link token hashing (Stage 12), the Outlook attach data path (file never touches our servers — only sender's and recipient's mail provider), default mail-client warning (Stage 17), and a complete sender data-flow walkthrough.
  - Second pass converted three "Q3-coming-soon" answers to "shipped" — "Can a user see their own share history?", "What if a sender realises they shared with the wrong recipient?" (self-revoke), and "Can a sender share one file with multiple recipients in one go?" (bulk send).
- **Demo script (`03-demo-script.md`)** updated for the Outlook auto-attach reality (status text + click count, 3 → 2 on the Outlook path) plus a Part 2 footer mentioning Stages 19-20 as optional on-stage material.
- **Engineer prep (`08-engineer-windows-msi-setup.md` §6)** smoke-test rewritten for both the Outlook and mailto paths.
- **Preflight checklist (`05-preflight-checklist.md`)** gained five new smoke items covering Stages 14, 17, 18, 19, 20 so nothing post-Stage-13 gets smoked by omission.

### Test coverage

- 11 new unit tests for `BulkRecipientParser` (separators, dedup, whitespace, Excel-paste, single-recipient passthrough).
- 9 endpoint tests for `MeSharesEndpoints` covering list + recipient/expiry surfacing, cross-user privacy, sort order, validation, empty state, self-revoke happy path, cross-user authorization rejection, idempotent re-revoke, 404 for unknown share-link.
- 8 unit tests for `JsonRecentRecipientsStore` (MRU order, dedup, capacity cap, persistence round-trip, corrupt-file recovery, whitespace trim, blank-arg rejection).
- 2 unit tests for `MailtoEmailComposer` (URL encoding through `IMailtoProtocolHandler`, failure propagation).
- 4 new `RecipientUxPolish` source-presence tests covering the Stage 14 body-factory branches, Stage 16 picker-reset markers, Stage 17 mail-client probe wiring, Stage 19 bulk-send loop wiring.
- 1 new `ServerIntegratedWorkflow` integration test exercising the full Stage 13 chain: `ProtectFileWorkflow.ProtectAsync` → `/api/admin/files/{fileId}/share-links` → `/api/share-links/verification/start` returns 200. Guards against another Stage 12-style format drift across endpoints.

### Notes

- The Outlook COM path can be built by CI (`dynamic` + late-bound reflection so no Outlook PIA dependency) but never exercised by it. Manual smoke on Windows + Outlook is required before the Monday demo. Mailto fallback is covered.
- Sender easy-to-use lands at ~9.7/10 across the three personas after this release. Recipient remains ~8/10 (in-browser PDF preview is the biggest remaining recipient lift). Admin remains ~7-8/10.

## [1.6.1] — 2026-05-21

**`/me/` topbar: hide `Admin →` link for non-admin users (CSS specificity fix).**
The `Admin →` link in the `/me/` topbar carries the `hidden` HTML attribute
until the loaded session reports an admin role. The author CSS rule
`.topbar nav a { display: inline-block }` (specificity 0,2,1) outranked
the user-agent `[hidden] { display: none }` rule (specificity 0,1,0), so
the link rendered as a clickable element for every visitor — including
recipients who only land on `/me/` to send a file.

### Fixed
- Added `.topbar nav a[hidden], .topbar nav .topbar-link[hidden] { display: none }`
  rule to `/me/app.css`. Higher specificity than the layout rule, so the
  `hidden` attribute now does what HTML spec says it does.
- Verified `/admin/` and `/share/` do not have the same pattern (no
  `[hidden]` element is rendered visible on either page).
- Found via the inline UI audit on `https://drm.zcr.ai/me/`.

## [1.6.0] — 2026-05-21

**Screen-capture protection + cross-platform watermark library (FinalCode parity, item C3 — Windows surface).**
The WPF viewer now blocks Snipping Tool, Win+Shift+S, Print Screen, OBS
display capture, Teams/Zoom/Meet screen-share, and the rest of the
Windows screen-capture pipeline. Existing PDF print-watermark stamping
extracted to a cross-platform library with full test coverage on Linux CI.

### Added
- **`Drm.Watermark`** — new cross-platform library. `PrintWatermarkComposer`
  (Stamp + ResolveTokens) moved out of `Drm.Viewer.Windows` so it lives
  somewhere that the Linux CI build + Mac dev box can compile and test.
  Production behaviour unchanged — same PdfSharp 6.2.1 dependency, same
  Stamp signature, same token resolution rules.
- **`Drm.Watermark.Tests`** — 24 new tests covering:
  - Stamp no-op on empty/whitespace text (same byte-array reference back)
  - Stamp throws on null/empty PDF bytes
  - Diagonal stamping produces larger PDF + still parses to same page count
  - All-pages stamping handles multi-page documents
  - Every documented position works: `diagonal` / `top` / `bottom` /
    `all-pages` + case-insensitive + unknown position falls back to diagonal
  - Opacity clamping: -100, 0, 5, 50, 100, 500 all produce valid output
  - `ResolveTokens` replaces `{user}`, `{userId}`, `{file}`, `{fileId}`,
    `{time}` correctly; handles null user/file with `anonymous`/empty;
    leaves unknown tokens (e.g. `{tenant}`) untouched so typos are visible
    on the rendered output; uses InvariantCulture so non-Western thread
    cultures don't render Buddhist years etc.
  - `PdfSharpFontFixture` shared collection registers a system-font
    resolver so PdfSharp can render text on Linux CI (it scans DejaVu /
    Liberation / Helvetica / Arial paths and maps every requested family
    to whichever it finds).
- **`Drm.Viewer.Windows.ScreenCaptureProtection`** — wraps the Windows
  `SetWindowDisplayAffinity` user32 API:
  - `Enable(window)` applies `WDA_EXCLUDEFROMCAPTURE` (Win10 build 19041+);
    falls back to `WDA_MONITOR` on older Windows. Defers via
    `SourceInitialized` if the HWND isn't ready yet.
  - `Disable(window)` removes the flag (unused in main viewer, available
    for diagnostic surfaces).
  - Silent best-effort: a failed API call writes to `Debug` and does NOT
    throw — the user is already looking at the document; we won't crash
    them because the capture-blocker couldn't load.
- **PrintScreen + Win+Shift+S key intercept** in `MainWindow.PreviewKeyDown`:
  clears the clipboard (in case Snipping Tool already wrote a clip),
  updates the status text to "Screen capture blocked. This file is
  protected.", and marks the key event handled so it doesn't propagate.
- **CI runs `Drm.Watermark.Tests`** on every push (added to
  `.github/workflows/ci.yml`).

### What this blocks (Windows screen-capture pipeline)
- ✓ Snipping Tool + Win+Shift+S screen snip — captures a black rectangle
- ✓ Print Screen key — captures a black rectangle + intercepted by viewer
- ✓ Windows + G screen recorder — viewer shows black
- ✓ OBS Studio display capture (default capture method)
- ✓ Microsoft Teams / Zoom / Google Meet screen-share streams
- ✓ Most third-party recording tools that go through the standard Windows
  graphics capture API

### What this does NOT block (documented limits)
- Physical camera pointed at the monitor
- Hardware HDMI capture card on the GPU output
- Kernel-level hooks or DRM-bypass utilities
- Capture via "Game capture" mode on some recorders if user manually
  overrides to that mode

The mission, like FinalCode's, is to raise the bar against casual leakage,
not promise mathematical impossibility. Per-frame watermark tiles on the
WPF surface still apply for the physical-camera attack vector.

### Refactor
- `Drm.Viewer.Windows.csproj` removes direct `PdfSharp` package reference;
  picks it up transitively through `Drm.Watermark`.
- `Drm.Viewer.Windows/PrintWatermarkComposer.cs` deleted (moved).
- `MainWindow.xaml.cs` adds `using Drm.Watermark;` — call sites unchanged.

### Tests
- `Drm.Watermark.Tests`: 24 new, all green
- Domain: 16/16 (unchanged)
- Server: 406/406 (unchanged)
- Total now: 446 passing

## [1.5.0] — 2026-05-20

**Brute-force auto-revoke for share links (FinalCode parity, item C2).**
External share links now self-defend against guessing attacks. When the same
share link receives more than N failed verification attempts within a window,
the link is auto-revoked and an admin alert fires. Per-tenant configurable;
defaults to 10 failures in 60 minutes.

### Why
The previous per-verification cap (`MaxAttempts = 5`) only locked one
verification at a time — an attacker could restart verification and get a
fresh counter, attempting indefinitely. This change closes that loop by
tracking failures **per share link** across all verification sessions.

### Added
- **`ShareLinkFailedAttemptEntity`** — append-only log of every failed
  verification attempt: tenant, share link, guest email, IP address, reason,
  timestamp. Indexed `(TenantId, ShareLinkId, OccurredAtUtc)` for the
  windowed-count query.
- **`TenantBruteForcePolicyEntity`** — per-tenant `Enabled` / `Threshold` /
  `WindowMinutes`. Conservative defaults baked in (`10 / 60`) so existing
  tenants get protection without configuring anything.
- **`ExternalShareLinkEntity.RevocationReason`** — distinguishes
  `"brute_force_threshold"` auto-revokes from manual admin revokes in the
  admin console and audit log.
- **`BruteForceProtectionService`** — encapsulates the record-and-decide
  flow. Wired into `POST /api/external-share/verification/confirm` on the
  wrong-code path. SQLite DateTimeOffset translation is known unreliable in
  this codebase, so the windowed count materialises and filters in-memory
  (mirrors the pattern in `PolicyDecisionService`).
- **Admin endpoints** under `/api/admin/brute-force-policy`:
  - `GET ?tenantId=...` — returns current policy or defaults if no row,
    with `usingDefaults: true/false` so the UI can show the source.
  - `PUT` — upsert. Validates `Threshold ∈ [1, 1000]` and
    `WindowMinutes ∈ [1, 10080]` (one week).
  - `GET /recent-failures?tenantId=...&shareLinkId=...&limit=N` — last N
    failures across the tenant, optionally scoped to one share link.
- **Audit event** `share_link_auto_revoked` (reason: `brute_force_threshold`)
  is appended when auto-revoke fires.
- **Admin notification** with the same event type goes out via the existing
  `IAdminNotificationService` (email + webhook depending on tenant config).
- **`ExternalShareLinkResponse.RevocationReason`** surfaced in the
  `GET /api/admin/files/{id}/share-links` list so the admin console can
  show "Auto-revoked (brute force)" next to a dead share link.

### Behaviour
- Disabled (`Enabled = false`) → the policy is a no-op. The per-verification
  `MaxAttempts = 5` cap still applies as before.
- Threshold = 1 → first wrong code revokes. (Useful when you suspect a
  specific share link is actively being attacked.)
- A legitimate user who hits the threshold via typo sees the dedicated
  error `share_link_auto_revoked` instead of the generic
  `invalid_verification_code`, so they stop retrying and contact the sender.
- An attacker sees the same error — no information leak about whether the
  link still exists.
- Auto-revoked share links cannot be re-redeemed even with the correct
  code; admin must issue a new link.

### Tests (+4 new, 406/406 green)
- `Brute_force_threshold_auto_revokes_share_link_after_repeated_failures` —
  threshold=3, 2 wrong codes still allowed, 3rd revokes, response carries
  `share_link_auto_revoked`, share link row shows
  `RevocationReason="brute_force_threshold"`, correct code afterwards is
  also rejected.
- `Brute_force_protection_can_be_disabled_per_tenant` — even with
  threshold=1, 3 wrong codes don't revoke when `Enabled=false`.
- `Brute_force_policy_get_returns_defaults_when_no_row` — `GET` on a
  fresh tenant returns `enabled=true threshold=10 windowMinutes=60
  usingDefaults=true`.
- `Brute_force_policy_rejects_invalid_threshold_and_window` — `PUT` with
  `threshold=0` or `windowMinutes=999999` returns 400.

## [1.4.0] — 2026-05-20

**Access count limit per user (FinalCode parity, item C1).**
Files can now cap how many times each user opens them. Once a user consumes
their allowed opens, further attempts are denied with `opens_exhausted`. The
counter is per-user, so handing the same file to five recipients with a
"3 opens each" cap gives 15 total opens, not 3 shared.

### Added
- **`FilePolicy.MaxOpens` (int?) + `OpensUsed` (int)** in `Drm.Domain`.
  Null means unlimited; the historical behaviour. `PolicyEvaluator.Evaluate`
  now returns `Deny("opens_exhausted")` once a user's count meets the cap,
  and reports `OpensRemaining` on the allow path so clients can show "3 opens
  left" in the viewer UI.
- **`FileAccessCountEntity`** — new table with composite key
  `(TenantId, FileId, UserId)` recording `OpensUsed`, `FirstOpenedAtUtc`, and
  `LastOpenedAtUtc`. Created lazily on the user's first access. Incremented
  only on real access (the policy simulator does NOT burn opens).
- **`PolicyTemplateEntity.MaxOpens`** so an admin can bake "3 opens per user"
  into a reusable template. Applying the template (`POST /api/admin/files/{id}/apply-policy-template`)
  copies `MaxOpens` onto the file. Templates without a cap stay unlimited.
- **Admin console** — Policy template form has a new "Max opens per user"
  input. Templates table shows the value or `Unlimited` per row.
- **`UnwrapFileKeyResponse.MaxOpens`** and **`OpensRemaining`** so the viewer
  can render the remaining count next to the file name.
- **403 unwrap responses now include a JSON body** with `{ "reasonCode": "..." }`
  instead of empty 403, so the client can distinguish `opens_exhausted` from
  `revoked` / `expired` / `device_disabled` / `permission_not_granted`.

### Behaviour
- `MaxOpens` precedence at register-time: explicit `RegisterFileRequest.MaxOpens`
  wins, then the applied template's `MaxOpens`, then null (unlimited).
- Lowering `MaxOpens` after the fact on a file where a user already exceeded
  it: defensive — they're denied immediately, `OpensRemaining` reports `0`.
- Permission check still runs before the opens check, so a user who lacks
  the requested permission never burns one of their opens.

### Tests
- 6 new domain tests in `PolicyEvaluatorTests` covering null cap, opens
  remaining math, the final-open boundary, exhaustion, defensive over-use,
  and precedence over permission denial.
- 2 new integration tests in `FileKeyApiTests` covering the full template →
  register → 3 opens allowed → 4th gets 403 `opens_exhausted` flow, plus
  per-user isolation (User A exhausts their cap, User B still has theirs).
- Total: 402/402 (was 400) green.

## [1.3.1] — 2026-05-20

**Docs + social card.**

### Added
- **1200×630 OpenGraph social card** at `/static/og-card.svg` — full-bleed
  brand poster with wordmark, headline ("Self-hosted DRM, ready in 5 minutes"),
  three pillars (Encrypt/Audit/Revoke), and the trust-badge line. When a
  zcrDRM URL is pasted into Slack, LinkedIn, Twitter, or email, recipients
  see a real product preview instead of a 32×32 favicon thumbnail.
- All three surfaces (`/admin/`, `/me/`, `/share/`) now declare
  `og:image:width=1200`, `og:image:height=630` alongside the og-card URL
  so social platforms render at the intended aspect ratio.

### Changed
- **README.md** renamed from "Enterprise DRM" to "**zcrDRM** — Enterprise DRM",
  with three Shields.io status badges (production URL, encryption stack,
  deployment stack), the brand tagline, the three pillars, and a production
  link to <https://drm.zcr.ai>.
- **CONTRIBUTING.md** retitled "Contributing to zcrDRM" with the production
  URL in the opening paragraph so new contributors know what they're shipping.

## [1.3.0] — 2026-05-20

**Brand identity — zcrDRM.**
First brand layer on top of the teal-on-slate design system. Replaces the generic
"DRM Management" label with the `zcrDRM` wordmark, adds a product hero band to
the Overview tab, and shifts the marketing voice from engineer-internal to
customer-facing.

### Added
- **Product wordmark `zcrDRM`.** Lowercase `zcr` (`var(--accent)` teal) +
  uppercase `DRM` (`var(--ink)` dark). IBM Plex Sans bold, tight letter-spacing.
  Paired with a custom inline-SVG padlock-seal icon (replaces 🔒 emoji).
- **Hero pillars band on Overview tab.** Three pillars — **Encrypt** (AES-256
  per file + RSA-2048 wrap), **Audit** (every open / every device / tamper-proof
  chain), **Revoke** (kill files from anywhere). Lucide-style SVG icons in
  teal-tinted squares. Only visible when `body[data-active-tab="overview"]`.
- **Trust badges row.** Pill chips below pillars: `AES-256` `RSA-2048`
  `FIPS 140-2 ready` `PostgreSQL` `Docker deploy` `On-prem first`. Engineer-
  legible credentials at first glance.

### Changed
- **Welcome modal copy.** "Welcome to DRM Management" → "zcrDRM — self-hosted DRM,
  ready in 5 minutes". Lede shortened to emphasize speed. Logo swapped to the
  same SVG padlock-seal used by the brand mark.
- **Page title.** `<title>` updated to `zcrDRM — Admin console`.
- **Getting started heading.** "set up DRM in 5 steps" → "set up zcrDRM in 5 steps".
- **Brand-logo CSS.** Now accepts an inline SVG child sized to 22×22 inside the
  40×40 teal tile (replaces the emoji approach).

### Design system
- `DESIGN.md` updated with **Product name & positioning** and **Wordmark**
  sections. Brand layer recorded; teal-on-slate tokens unchanged.

### Tests
- `ManagementConsoleTests` updated to assert `zcrDRM` (was `DRM Management`).

## [1.2.2] — 2026-05-19

**Admin console A+ polish — SVG icons, Tenants tab fix, design tokens.**
Follow-on polish targeting A+ grades across every design dimension. Replaces
emoji icons with inline SVG sprite, fixes a silent bug where the Tenants tab
was non-functional, and aligns hardcoded font-sizes and animation durations
to design tokens.

### Added
- **Inline SVG icon sprite.** All 11 emoji icons in tabs, rail nav, and empty
  states (📊👥📋📁🔌🏢⚙️📤📥📘🧩) replaced with Lucide-style stroke icons
  defined once at the top of `index.html` and referenced via `<use href="#icon-*">`.
  Renders identically across macOS / Windows / Linux / Android.

### Fixed
- **Tenants tab was broken.** Clicking the Tenants tab silently fell through
  to Overview because `"tenants"` was missing from the `VALID_TABS` set in
  `app.js`. Added the entry plus the matching `body[data-active-tab="tenants"]`
  CSS hide rule so the 10 tenant subtabs (Tenants, Registrations, Access
  requests, Plans, Compliance, Retention, IP allowlist, Device trust, Key
  rotation, Usage snapshot) are reachable.
- **Tab bar overflow at narrow widths.** Changed `.tab-nav` from
  `flex-wrap: wrap` to `flex-wrap: nowrap` + `overflow-x: auto` so the 6
  tabs never wrap to a second row at sub-720px container widths. Scrollbar
  hidden via `scrollbar-width: none` and `::-webkit-scrollbar { display: none }`.
- **Truncated placeholders.** Shortened 4 placeholders that were getting cut
  off inside their inputs: `"Per-admin token (drm_admin_…) or shared key"`
  → `"drm_admin_… or shared key"`, the global search hint, the Entra tenant
  ID hint, and the directory sync Client ID hint.

### Changed
- **Typography aligned to tokens.** Replaced `font-size: 11px/12px/14px`
  literals throughout `app.css` with `var(--text-xs)`, `var(--text-sm)`,
  `var(--text-body)`. Added `letter-spacing: 0.04em` to `.eyebrow` for
  better uppercase legibility. Added `gap: 16px` to the `.settings` grid.
- **Animation durations tokenized.** Replaced 7 hardcoded transition and
  animation durations (`80ms`/`100ms`/`120ms` → `var(--duration-fast)`;
  `180ms`/`200ms`/`220ms`/`0.25s` → `var(--duration-base)`) on tab-link,
  subtab-link, settings-trigger, drawer, backdrop, `welcomeFadeIn`, and
  `slideIn`. The reduced-motion override in `tokens.css` now fully governs
  motion across the admin console.
- **Integrations subtab order.** Moved Outlook ahead of Box per the design
  audit recommendation (most enterprises use Outlook before Box). New
  order: Email notifications → Outlook → Box → SIEM → Folder watcher.
- **Mobile settings framing.** At ≤820px, the settings form section now
  has a `1px solid var(--line)` border and 8px radius so it reads as an
  intentional panel rather than an exposed form. Pre-set `data-open="false"`
  on `#settingsBackdrop` and `#settingsDrawer` in HTML to eliminate any
  FOUC before JS initializes the drawer state.

## [1.2.1] — 2026-05-19

**Design audit — admin console polish (v1.9 UI).**
Full design audit of the admin console resulting in 12 resolved issues across
accessibility, visual hierarchy, token consistency, and layout density.

### Changed
- **Design token consistency.** Replaced raw inline `style=""` attributes in
  the v1.7–v1.9 panels (`#batchOps`, `#compliance`, `#retentionPolicy`,
  `#keyRotation`, `#ipAllowlist`, `#deviceTrust`) with `.card`, `.card-row`,
  `.btn-row`, `.label-row`, `.field-row`, and `.pre-output` utility classes.
  Added matching CSS utilities to `app.css`.
- **Danger button token.** `gdprEraseBtn`, `batchRevokeBtn`, and
  `applyRetentionBtn` now use the `.danger` CSS class instead of an inline
  `style="background:var(--danger,#e53e3e)"` fallback that resolved to the
  wrong brand red on CSS variable failure.
- **Accessibility.** Added `aria-hidden="true"` to all `.nav-icon` and
  `.tab-icon` emoji spans. Added a visually-hidden skip-navigation link
  (`<a class="skip-link" href="#adminMain">`).
- **Welcome modal.** Expanded from 4 to 5 steps to align with the Getting
  Started checklist (separate "Set admin credential" from "Generate Tenant
  ID").
- **Connection status badge.** `#connectionState` now shows a coloured pill
  with amber dot when disconnected and a green pill when connected, replacing
  the plain muted text.
- **Files tab.** Reorganised into four subtabs — Files, Sharing, Commands,
  Containers — using the existing pill subtab system. Reduces visible form
  density from 9 stacked sections to a focussed subset per subtab.
- **Empty states.** Main tables (Users, Policy templates, Audit events,
  Tenants) now use the rich `.empty-state` component (icon + title + hint)
  instead of bare `.empty` text cells.
- **Forget button placement.** Moved adjacent to Save in the settings grid
  row, eliminating the orphaned second-row button.
- **`#rejectDialog`.** Converted from `<div hidden>` to a native `<dialog>`
  element for built-in focus trapping and Escape dismissal.
- **Integrations tab.** Active integrations show a green "Active" badge and
  accent left-border; unconfigured ones show a muted "Inactive" badge.
  Status is set dynamically when configuration is saved or loaded.

### Fixed
- `ManagementConsoleTests.Admin_console_index_is_served` updated to match the
  redesigned Files tab section heading ("External sharing" replaces
  "External share links").

---

## [1.2.0] — 2026-05-18

**v1.2 — tenant-scoped admin roles.**
Operators can now create admin users whose authority is limited to a single
tenant. A scoped admin can do anything their role permits, but only within
their assigned tenant. Global admins (no scope) behave identically to v1.1.

### Added
- **Tenant-scoped admin roles.**
  - `AdminUserEntity.TenantScope` (`Guid?`) — `null` = global, non-null =
    restricted to one tenant.
  - `AdminIdentity.TenantScope` and `CanAccessTenant(Guid)` helper.
  - `AdminIdentityContext.TryRequirePermissionForTenant` — checks RBAC
    permission AND tenant scope; returns 403 `tenant_scope_denied` on mismatch.
  - `POST /api/admin/identity/admins` accepts optional `tenantScope` UUID.
  - `GET /api/admin/identity/whoami` and admin list return `tenantScope`.
  - Additive schema migrations for SQLite and Postgres.
  - Admin console: tenant scope input on create-admin form; "Scope" column in
    the admin user table (shows "Global" badge or truncated UUID with tooltip).

### Changed
- `TryRequirePermissionForTenant` enforced on all 67 tenant-data endpoint
  call sites across 17 endpoint files (Audit, Box, Devices, DirectorySync,
  ExternalShareSettings, FileTags, FileZip, Files, FolderWatcher,
  NotificationConfig, OutlookIntegration, PolicySimulator, PolicyTemplates,
  SecureContainers, Siem, TransparentFiles, WatermarkTemplates).
  Remaining global call sites: AdminIdentityEndpoints (admin self-management),
  AdminLicenseEndpoints, TransparentFiles VerifyAsync + GetTrailerSecret.

## [1.1.0] — 2026-05-18

**v1.1 enterprise — admin identity upgrade (Slices 1–4).**
Completes the v1 → v1.1 enterprise upgrade. Every admin API call is now
authenticated with a real, revocable, role-bound identity. The shared API
key is supported as a backward-compatible fallback with an operator-controlled
deprecation path, and the management console has been updated throughout to
prefer per-admin tokens.

### Added (v1.1.0 summary)
- Per-admin tokens (`X-DRM-Admin-Token`), roles, and RBAC permission system
  (Slice 1: `AdminUserEntity`, `AdminRoleEntity`, `AdminApiTokenEntity`,
  `AdminTokenCrypto`, 4 system roles with scoped permission sets).
- Admin identity management panel in the console — WhoAmI, role list, admin
  user table with disable/enable, create-admin form with one-time token
  display, per-user token rotation and revocation (Slice 2).
- `ActorAdminId` set on every `AuditEventEntity` across all admin endpoints
  so every change is traceable to the individual admin who made it (Slice 2).
- `Drm:Security:AdminSharedKeyMode` config toggle (`Active` / `Warn` /
  `Disabled`) for the shared-key lifecycle. `Warn` adds a `Deprecation: true`
  response header and writes a `shared_key_deprecated_usage` audit event.
  `Disabled` rejects shared-key requests with 401 `shared_key_disabled`
  before any key comparison (Slice 3).
- Admin console deprecation banner when the active session used the shared key
  (`sharedKeyFallback == true` from WhoAmI) (Slice 3).
- `adminAuthHeader()` helper in the console — routes to `X-DRM-Admin-Token`
  when the credential starts with `drm_admin_`, otherwise falls back to
  `X-DRM-Admin-Key`. All `apiFetch`, `apiFetchBlob`, and inline dashboard
  probe calls updated (Slice 4).

### Changed (v1.1.0 summary)
- RBAC enforcement on all 23 admin endpoints via
  `AdminIdentityContext.TryRequirePermission` (Slice 2).
- `AdminAudit.SystemEvent` and `AdminAudit.PermissionEvent` accept optional
  `HttpContext?` and set `ActorAdminId` from it (Slice 2).
- Development mode with no `Drm:Security:AdminApiKey` now stamps the default
  SuperAdmin identity instead of passing null, keeping the test suite green
  without weakening production paths (Slice 4 / test fix).
- Admin console credential input relabeled "Admin credential" with placeholder
  naming both token and key forms. Checklist step 2 guides toward per-admin
  tokens (Slice 4).

### Fixed (v1.1.0 summary)
- Docker healthcheck used `wget` which is absent from
  `mcr.microsoft.com/dotnet/aspnet:10.0`. Changed to
  `bash -c 'echo > /dev/tcp/localhost/8080'` in `Dockerfile`,
  root `docker-compose.yml`, and `deploy/management/docker/docker-compose.yml`.

### Tests
- +6 `AdminSharedKeyModeTests`: Active/Warn/Disabled mode behaviour,
  `Deprecation` header, audit event write, token still works in Disabled mode.
- `ManagementConsoleTests`: updated assertion from `X-DRM-Admin-Key` to
  `Admin credential` to match the renamed label.
- Full server suite: **278/278 pass**.

---

**v1.1 enterprise — Slice 4: console migration to per-admin tokens.**
Fourth slice. The management console auto-detects whether the stored credential
is a per-admin token or the legacy shared key, and routes to the correct header.

**v1.1 enterprise — Slice 3: shared-key deprecation path.**
Third slice of the v1 → v1.1 enterprise upgrade. Adds a three-mode lifecycle
toggle for the shared admin key so operators can signal deprecation before
removing it, and updates the admin console to surface the warning visually.

### Added
- **`Drm:Security:AdminSharedKeyMode` config toggle** — three modes:
  `Active` (default, no change), `Warn` (shared key accepted; response gains
  `Deprecation: true` header and a `shared_key_deprecated_usage` audit event
  is written), `Disabled` (shared-key requests rejected with 401
  `shared_key_disabled` before any key comparison).
- **Admin console deprecation banner** — `loadWhoAmI()` now calls
  `setSharedKeyBanner(identity.sharedKeyFallback)`. When the active session
  was authenticated via the shared key, a persistent dark-red banner appears
  at the top of the page instructing the admin to migrate to per-admin tokens.

### Not in this slice (Slice 4 work)
- Admin console still sends `X-DRM-Admin-Key` for all requests. The console
  itself needs to be migrated to send `X-DRM-Admin-Token` when a per-admin
  token is stored, so the shared-key deprecation path actually removes the
  console's dependency on the shared key.

---

**v1.1 enterprise — Slice 2: RBAC enforcement, admin UI, audit attribution.**
Second slice of the v1 → v1.1 enterprise upgrade. Enforces the permission model
from Slice 1 on every existing admin endpoint, ships a full admin identity
management panel in the console, and wires `ActorAdminId` into every audit event
so every change is traceable to the individual admin who made it.

### Added
- **Admin identity management panel** — new "Access control" tab in the admin
  console. Shows current session (WhoAmI details), role list, admin user table
  with disable/enable controls, create-admin form (email, display name, role,
  token label), one-time token display on create and rotate, and per-user
  rotate-token action. Reads from the `/api/admin/identity/*` endpoints added
  in Slice 1.
- **`ActorAdminId` on every audit event** — all `AuditEventEntity` constructions
  across admin endpoints now set `ActorAdminId = AdminAudit.ActorId(httpContext)`,
  resolved from the `AdminIdentity` stamped by `AdminIdentityMiddleware`. Covers
  `AdminFilesEndpoints`, `AdminFileZipEndpoints`, `AdminWatermarkTemplatesEndpoints`,
  `AdminTransparentFilesEndpoints`, `AdminSecureContainersEndpoints`, and all 7
  conditional events in `AdminExternalShareSettingsEndpoints`.

### Changed
- **RBAC enforcement on all 23 admin endpoints** — every handler now calls
  `AdminIdentityContext.TryRequirePermission(httpContext, permission, out var fail)`
  before executing. Unauthenticated → 401. Insufficient permission → 403.
  Read-only endpoints require `*:read` permissions; mutating endpoints require
  `*:write`. Permissions map: files (`files:read/write/zip/revoke/grants`),
  audit (`audit:read/export`), devices (`devices:read/write`), policy and
  watermark templates (`policies:read/write`), tenant/settings surfaces
  (`tenants:read/write`, `settings:read/write`), identity (`admins:read/write`).
- `AdminAudit.SystemEvent` and `AdminAudit.PermissionEvent` factory methods now
  accept an optional `HttpContext? httpContext` parameter and set `ActorAdminId`
  from it. All call sites updated.

### Fixed
- Docker healthcheck used `wget` which is absent from
  `mcr.microsoft.com/dotnet/aspnet:10.0`. Changed to
  `bash -c 'echo > /dev/tcp/localhost/8080'` in `Dockerfile`,
  root `docker-compose.yml`, and `deploy/management/docker/docker-compose.yml`.

### Not in this slice (Slice 3 work)
- Shared-key deprecation + retirement path: `Deprecation` response header on
  shared-key requests, audit event per shared-key auth, admin console
  deprecation banner, and a `Drm:Security:AdminSharedKeyMode` config toggle
  (`Active` → `Warn` → `Disabled`).

---

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
