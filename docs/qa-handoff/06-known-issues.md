# Known Issues + Workarounds

Things we know about. **Don't file new bugs for these.** If you see one of
these, link to this doc instead.

## Cosmetic (low priority — not bugs)

### K-1: Personalize modal on `/me/` uses emoji icons
The role chooser on `/me/` uses 👤📚🎯🛠 emojis instead of the SVG icon system
that the rest of the brand uses. Inconsistent with the v1.2.2+ migration but
not user-blocking. Backlog: replace with SVG icons from the sprite.

### K-2: `Drm.Viewer.Windows` does NOT have an MSI installer
The viewer can be built (`dotnet publish`) but there's no automated MSI build
or Windows installer pipeline. Customers manually unzip + run today. Backlog:
WiX installer + signed MSI.

### K-3: No public API docs site
Every endpoint group is in `src/Drm.Server/Endpoints/*.cs`. Integrators need
to read source. Backlog: OpenAPI spec + ReDoc page.

### K-4: Root `drm.zcr.ai/` redirects to `/admin/`
Customers landing on the brand domain see the admin login form instead of a
marketing landing page. Brand identity ships everywhere else (/me/, /share/,
og-card) but the marketing surface is still TODO.

## Operational (won't fix until snooze expires)

### K-5: No automated Postgres backup
Production `docker_postgres-data` volume has no off-server backup.
**Snoozed until 2026-08-21** by user direction. If the volume dies between
now and then, data loss is on the snooze. Internal note: bring this up
again on 2026-08-22.

### K-6: No uptime monitoring / alerting
If `drm.zcr.ai` goes down at 3 AM nobody finds out until 9 AM.
**Snoozed until 2026-08-21** by user direction. Workaround: customers
report it.

### K-7: CI auto-merge can race with red checks
Auto-merge on a PR fires when "all required" checks pass — but if no required
checks are configured in repo settings, an auto-merge can race a still-running
CI run. Caught for PR #4 (v1.3.0): the PR merged with CI red because the
required-checks setting in GitHub repo settings hasn't been configured.
Backlog: configure branch protection on `master`.

## CI / test infrastructure

### K-8: PdfSharp font resolver on non-Windows CI
`Drm.Watermark.Tests` uses an `xUnit` collection fixture
(`PdfSharpFontFixture`) that scans system font paths (`/usr/share/fonts/...`,
macOS Arial paths, Windows fonts dir) and registers a resolver. If a future
CI image strips `dejavu-fonts-ttf-core` or similar, tests will fail with
"No appropriate font found for family name 'Helvetica'". Workaround: keep
DejaVu Sans installed on the CI image, or embed a TTF in the test project.

### K-9: `SetWindowDisplayAffinity` cannot be CI-tested
The screen-capture protection is a Windows-only `user32.dll` P/Invoke. It
runs on real Windows boxes but Linux CI skips the project. Verification
relies on manual Tier 3 (T3.2) smoke tests.

### K-10: SQLite DateTimeOffset WHERE/ORDER is unreliable
The codebase materializes results then filters in-memory in several
places (`PolicyDecisionService`, `BruteForceProtectionService`) because
EF Core's SQLite provider translates DateTimeOffset comparisons inconsistently.
This is a known codebase quirk, not a bug. Production uses Postgres which
doesn't have this issue, but tests run on SQLite for speed.

## Authentication / behaviour subtleties

### K-11: `/api/admin/*` returns 401 without `X-DRM-Admin-Key`
By design. Not a bug — it's the auth gate.

### K-12: Same `verification_code` doesn't unlock a revoked share link
The brute-force auto-revoke (C2) marks the share link `Revoked=true`.
Subsequent attempts even with the correct code are rejected. Admin must
issue a new share link. This is by design — the link is dead after auto-revoke.

### K-13: `opensRemaining` reports the count AFTER consumption
On a successful unwrap, the response carries the value AFTER one open has
been consumed by THIS request — so a fresh file with `maxOpens=3` returns
`opensRemaining: 2` on the first call (not 3). By design — the value most
clients want to display is "after this open, X are left".

### K-14: Policy simulator does NOT consume an open
`POST /api/admin/policy-simulator` is read-only — it returns the decision
without incrementing `FileAccessCountEntity`. Confirmed by the `writeAudit`
branch in `PolicyDecisionService`. By design.

### K-15: Welcome modal "Create test tenant" generates a tenant on the SERVER
Clicking the button POSTs to create a real tenant in the production database.
This is fine for testing — these tenants are isolated from real customers —
but don't accidentally onboard a tenant you didn't mean to keep. Clean up
test tenants periodically via the Tenants admin tab.

## UI quirks worth knowing about

### K-16: Tabs scroll horizontally below 720px container width
Intentional after v1.2.2 fix. The previous behaviour was wrapping which
caused the Tenants tab to disappear at narrow widths. Now it scrolls.

### K-17: Rail collapses to icon-only below 820px viewport
Intentional. The user can manually expand with the toggle button at the top
of the rail. State persists in localStorage.

### K-18: Settings drawer (gear icon) is a slide-in panel, NOT a modal
The drawer slides in from the right with `transform: translateX`. It does
not block interaction with the rest of the page until you click outside.
This is intentional — admins often need to glance at server health while
configuring something.

### K-19: `/me/` and `/share/` don't have the Tenants tab — they only have a topbar
Different surface, different scope. Only `/admin/` has the 6-tab navigation.
This is intentional — `/me/` is for end users sending files, `/share/` is
for guest recipients.

### K-20: Connection state pill is "Not connected" until session is saved
The pill turns to "Connected" green only after a successful API call with
saved session credentials. A page reload before save shows "Not connected"
even if you just clicked "Save session" — refresh again and it picks up.
