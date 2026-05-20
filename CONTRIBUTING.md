# Contributing to zcrDRM

Thanks for working on `zcrDRM`, the Information Rights Management platform that
ships from this repo. Production runs at <https://drm.zcr.ai>. This file is the
short version of what we expect from a contribution.

## Repository layout

- `src/Drm.Domain` — pure domain types (Permission flags, value objects)
- `src/Drm.Crypto` — encryption primitives (envelope formats, container, transparent trailer)
- `src/Drm.Container` — file container format
- `src/Drm.Agent.Core` — cross-platform agent workflows (protect, open, audit)
- `src/Drm.Cli` — `drm` command-line tool
- `src/Drm.Server` — ASP.NET Core minimal-API server, the policy + identity + audit plane
- `src/Drm.Server/wwwroot/admin/` — admin console SPA (vanilla HTML/JS, cream theme)
- `src/Drm.Server/wwwroot/share/` — external share viewer SPA
- `src/Drm.Server/wwwroot/outlook-addin/` — Office Web Add-in manifest + taskpane
- `src/Drm.Agent.Tray.Windows` — WPF "DRM Agent" tray app (protect dialog, status indicators)
- `src/Drm.Agent.Service.Windows` — Windows service shell
- `src/Drm.Viewer.Windows` — WPF protected-file viewer
- `src/Drm.FolderWatcher.Service` — Windows service that auto-encrypts dropped files on file shares
- `tests/Drm.*.Tests` — xUnit test projects
- `docs/superpowers/plans/` — per-phase implementation plans
- `docs/architecture.md` — high-level diagrams (see file)

## Local development

Prerequisites: **.NET SDK 10.0** and (for Windows desktop projects) Windows 11.

```bash
# Restore + build everything
dotnet build Drm.sln

# Run the server with SQLite
dotnet run --project src/Drm.Server -- --urls http://localhost:5188

# Run only the main test suite
dotnet test tests/Drm.Server.Tests
```

## Docker

```bash
# One-command stack (server + persistent volume)
docker compose up --build
# Admin console: http://localhost:5188/admin/
```

Set secrets via env vars (see `docker-compose.yml` for the full list):

```bash
export DRM_ADMIN_API_KEY="$(openssl rand -hex 32)"
export DRM_CLIENT_API_KEY="$(openssl rand -hex 32)"
export DRM_TRAILER_SECRET="$(openssl rand -hex 32)"
```

## Rules every PR must follow

1. **Tests come with the change.** Anything more than a doc/CSS tweak ships with new xUnit tests. The main suite is `tests/Drm.Server.Tests`.
2. **UI lives on both sides.** Server features that surface to operators get admin-console UI; features that surface to end users get Windows desktop UI. Purely server-internal endpoints document why no client UI was added.
3. **Multi-tenant isolation.** Every query that loads or mutates tenant-owned data filters by `TenantId`. Every endpoint validates the caller's tenant.
4. **Audit events on side effects.** State changes that the operator should be able to retrace later write an `AuditEventEntity`.
5. **No secret leakage.** Never log API keys, HMAC secrets, container passphrases, or wrapped file keys. Don't return them from list/get endpoints.
6. **Tamper-evident formats.** New on-disk formats use HMAC or AEAD. Use `CryptographicOperations.FixedTimeEquals` for byte comparisons.
7. **Plain SQL migrations gated by `IF NOT EXISTS`.** SQLite migrations live in `src/Drm.Server/Program.cs` and use `CREATE TABLE IF NOT EXISTS` / `CREATE INDEX IF NOT EXISTS`. Column additions check `PRAGMA table_info` first.

## Commit messages

Follow conventional-commits: `feat:`, `fix:`, `docs:`, `chore:`, `refactor:`, `test:`. For feature commits, add a one-paragraph body that explains *why*.

## Code review

Pull requests get a two-stage review:

1. **Spec compliance** — does the code implement what the plan / issue asked for?
2. **Code quality** — multi-tenant isolation, error paths, naming, file responsibility, test coverage.

When you write a plan, save it under `docs/superpowers/plans/YYYY-MM-DD-feature-name.md`. The `superpowers:writing-plans` and `superpowers:subagent-driven-development` skills walk you through the rest.

## Filing an issue

- **Security**: open a private GitHub Security Advisory, do not file a public issue
- **Bug**: include the minimal reproduction, the observed behaviour, and the expected behaviour
- **Feature**: link the relevant phase in the roadmap (or propose a new phase)

## Licensing

The repository ships under the license declared by the maintainers; if no `LICENSE` file exists yet, treat the code as proprietary and coordinate with the maintainers before redistributing.
