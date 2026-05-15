# Phase 4A Management Install Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a concrete on-prem management server install baseline so the DRM management API can be configured and started predictably outside the development IDE.

**Architecture:** Keep the server binary unchanged and add deploy assets around it: an example production config, a start script that sets required environment variables, creates a data directory, and refuses to start without a configured key-wrapping master key, plus docs. Tests verify the assets remain present and contain the required operational safeguards.

**Tech Stack:** .NET 10, ASP.NET Core configuration, Bash, xUnit, System.Text.Json.

---

## File Structure

- Create `deploy/management/appsettings.onprem.example.json`: example on-prem management server config.
- Create `deploy/management/start-management.sh`: local/on-prem start script for published or source-tree server runs.
- Create `deploy/management/README.md`: operator install/run instructions.
- Create `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs`: verifies deploy asset JSON and script safeguards.
- Modify `README.md`: link the management install baseline.

## Tasks

### Task 1: Asset Tests

- [x] **Step 1: Write failing tests**

Create `tests/Drm.Server.Tests/ManagementInstallAssetsTests.cs` with tests that assert:
- `deploy/management/appsettings.onprem.example.json` exists, is valid JSON, sets `Drm:Mode` to `OnPrem`, contains `ConnectionStrings:DrmDb`, contains a replaceable key wrapping master key placeholder, and exposes HTTP on `http://0.0.0.0:5080`.
- `deploy/management/start-management.sh` exists, starts with a Bash shebang, uses `set -euo pipefail`, creates `DRM_DATA_DIR`, exports `Drm__KeyWrapping__MasterKeyBase64`, refuses to start when `DRM_KEY_WRAPPING_MASTER_KEY_BASE64` is missing, and runs `Drm.Server`.

- [x] **Step 2: Run failing tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementInstallAssetsTests
```

Expected: FAIL because deploy assets do not exist yet.

### Task 2: Install Assets

- [x] **Step 1: Add example config**

Create `deploy/management/appsettings.onprem.example.json` with:
- `Drm.Mode = OnPrem`
- `Drm.KeyWrapping.MasterKeyBase64 = REPLACE_WITH_32_BYTE_BASE64_MASTER_KEY`
- `ConnectionStrings.DrmDb = Data Source=/var/lib/drm-management/drm-server.db`
- `Kestrel.Endpoints.Http.Url = http://0.0.0.0:5080`

- [x] **Step 2: Add start script**

Create `deploy/management/start-management.sh` that:
- sets `set -euo pipefail`
- resolves `SERVER_DIR`, `DRM_DATA_DIR`, `DRM_URL`, and `DOTNET`
- exits with code `2` if `DRM_KEY_WRAPPING_MASTER_KEY_BASE64` is empty
- creates the data directory
- exports `ASPNETCORE_ENVIRONMENT`, `ASPNETCORE_URLS`, `ConnectionStrings__DrmDb`, `Drm__Mode`, and `Drm__KeyWrapping__MasterKeyBase64`
- runs `Drm.Server.dll` if present, otherwise runs the source project with `dotnet run --project`

- [x] **Step 3: Add operator README**

Create `deploy/management/README.md` documenting:
- `dotnet publish src/Drm.Server/Drm.Server.csproj -c Release -o ./artifacts/drm-management`
- `openssl rand -base64 32`
- required `DRM_KEY_WRAPPING_MASTER_KEY_BASE64`
- optional `DRM_DATA_DIR`, `DRM_SERVER_DIR`, and `DRM_URL`
- health check `GET /healthz`

- [x] **Step 4: Run passing asset tests**

Run:

```bash
/Users/pop7/.dotnet/dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj --filter ManagementInstallAssetsTests
```

Expected: PASS.

### Task 3: Docs, Verification, Commit

- [x] **Step 1: Update root README**

Add a Phase 4A section that points to `deploy/management/README.md` and states the baseline still requires production TLS/auth/service hardening before real deployment.

- [x] **Step 2: Full verification**

Run:

```bash
/Users/pop7/.dotnet/dotnet test Drm.sln
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
/Users/pop7/.dotnet/dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj -m:1
```

Expected: all pass.

- [x] **Step 3: Commit**

Run:

```bash
git add deploy tests README.md docs/superpowers/plans/2026-05-15-phase-4a-management-install-baseline.md
git commit -m "feat: add management install baseline"
```

## Self-Review

- Spec coverage: Gives operators a concrete management server install/run baseline without pretending it is production-hardened.
- Security note: The start script refuses to run without an explicit file-key wrapping master key.
- Placeholder scan: No TBD/TODO placeholders.
