# CLAUDE.md — zcrDRM

Self-hosted enterprise DRM (a FinalCode alternative): a **.NET 10** server (`Drm.Server`)
with admin console `/admin/`, sender portal `/me/`, recipient viewer `/share/`, plus a
**Windows** desktop agent + viewer. Files are sealed into AES-256-GCM `.drmx` containers;
each file's content key is wrapped per-tenant with a key derived from `DRM_MASTER_KEY_BASE64`.

`README.md` is a phase-by-phase feature log. This file is the "what will bite you" guide —
read it before changing the server, schema, crypto, or deploy paths.

## Build & test

```bash
dotnet build                 # whole solution
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj   # main suite (SQLite, in-memory app)
```

- Target framework: **net10.0**. `Directory.Build.props` sets `TreatWarningsAsErrors=true`
  for the whole repo — warnings fail the build. (`WarningsNotAsErrors` exempts only `NU1903`;
  see gotchas.)
- The `*.Windows` projects — `Drm.Agent.Service.Windows`, `Drm.Agent.Tray.Windows`,
  `Drm.Viewer.Windows` (WPF / `net10.0-windows`) — and the MSI **only build on Windows**.
  On macOS/Linux build/test the server + cross-platform libs; CI's Windows jobs cover the rest.
- Server runs on SQLite by default; Postgres when the connection string contains `Host=`
  (`Program.cs` picks the provider). Tests use SQLite.

## ⚠️ Load-bearing gotchas (don't learn these the hard way)

- **There are NO EF migrations. Do not run `dotnet ef migrations`.** Schema is applied at
  startup by `DatabaseInitializer.Initialize` (`src/Drm.Server/DatabaseInitializer.cs`):
  `EnsureCreated()` (a no-op on an existing DB) **plus ~120 idempotent `ExecuteSqlRaw`
  patches** (`CREATE TABLE / ADD COLUMN IF NOT EXISTS`), written **twice — once for SQLite,
  once for Postgres**. To add a column/table: add an entity property AND a matching idempotent
  patch in *both* provider branches of `DatabaseInitializer`. A missing Postgres patch =
  prod boot/runtime failure (prod is Postgres).
- **`DRM_MASTER_KEY_BASE64` must never be lost or rotated once data is written.** It wraps
  every tenant key; losing it makes all `.drmx` content permanently unrecoverable.
- **`DRM_AUDIT_CHAIN_KEY` (`Drm:Security:AuditChainKey`) must stay stable.** A `SaveChanges`
  interceptor (`AuditChainInterceptor`) HMAC-chains every audit event with it; changing/losing
  it invalidates the existing chain (recoverable by a rebuild, but it resets the tamper
  baseline). Empty key = the chain verifies but is forgeable.
- **Crypto-shred is "destroyed in the live system," not "everywhere."** Revoke deletes the
  wrapped key row; a copy already in a DB backup is recoverable until that backup ages out.
  Pitch it accordingly.
- **Prod deploy is NOT `git pull`.** It's build-image-from-clone + container swap (Postgres,
  Caddy auto-TLS), reversible with a pre-deploy `pg_dump` + a `rollback-*` image tag. See
  `docs/INSTALL.md` and `deploy/management/docker/{deploy.sh,ship.sh}`. Startup also runs an
  idempotent audit-chain backfill — watch boot logs for `Audit chain backfill: created N rows`.
- **Test repo-root helpers must handle git worktrees.** `.git` is a *directory* in a normal
  clone but a *file* in a worktree; use `Path.Exists(.../".git")`, not `Directory.Exists`
  (several tests `LocateRepoRoot` by walking up — they break in a worktree otherwise).
- **`NU1903`** (CVE-2025-6965 in transitive `SQLitePCLRaw`, dev/test-only — prod is Postgres,
  no upstream fix) is downgraded to a non-fatal warning in `Directory.Build.props`. Every
  *other* advisory still fails the build. Remove the exemption when a patched bundle ships.
- **Device/AD trust is self-reported, not attested.** The agent reports domain posture; the
  server stores it and only verifies the request signature ("holds the device secret"). Don't
  describe it as cryptographic AD attestation.

## Architecture (quick map)

- `src/Drm.Server` — endpoints in `Endpoints/*Endpoints.cs`, EF entities in `Entities.cs`,
  `AppDbContext.cs`; static UI under `wwwroot/{admin,me,share}`.
- `src/Drm.Domain` / `src/Drm.Crypto` / `src/Drm.Container` — permission model, AES-GCM,
  `.drmx` format (cross-platform).
- `src/Drm.Agent.Core` — agent logic (cross-platform); the `*.Windows` projects are the WPF/
  service shells.
- Auth headers: admin = `X-DRM-Admin-Key` (shared) or `X-DRM-Admin-Token`; client = client API
  key; most admin endpoints also require `X-DRM-Tenant-Id` matching the body's `tenantId`.

## Conventions

- Keep changes idempotent on the startup path; never reorder existing schema patches.
- New endpoints: tenant-scope every query, check the permission + `MatchesHeader(tenantId)`,
  and follow the shape of the sibling endpoint in the same file.
- Add tests under `tests/Drm.Server.Tests` (they spin up the app via `WebApplicationFactory`).

## Skill routing

When the user's request matches an available skill, invoke it via the Skill tool. When in doubt, invoke the skill.

- Product ideas / brainstorming → `/office-hours`
- Strategy / scope → `/plan-ceo-review`; architecture → `/plan-eng-review`; full pipeline → `/autoplan`
- Bugs / errors → `/investigate`; QA a running site → `/qa`
- Code review of a diff → `/code-review`; visual polish → `/design-review`
- Ship / deploy / PR → `/ship` or `/land-and-deploy`
