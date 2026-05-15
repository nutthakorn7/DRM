# Enterprise DRM

This repository contains an independently designed enterprise DRM/IRM platform.

## Foundation MVP

The first vertical slice protects PDF files into an encrypted container, registers file policy with the management server, checks policy before opening, applies watermark metadata, audits access, and supports revoke.

## Development Prerequisites

- .NET 10 SDK
- Windows 11 development host for WPF viewer/service work
- PostgreSQL for production-like deployments
- SQLite is used for local smoke tests

## Run Server

```bash
dotnet run --project src/Drm.Server/Drm.Server.csproj
```

Health check:

```bash
curl http://localhost:5000/healthz
```

## Run Tests

Use the repository-local .NET SDK path when running commands:

```bash
PATH=/Users/pop7/.dotnet:$PATH
dotnet test tests/Drm.Domain.Tests/Drm.Domain.Tests.csproj
dotnet test tests/Drm.Crypto.Tests/Drm.Crypto.Tests.csproj
dotnet test tests/Drm.Container.Tests/Drm.Container.Tests.csproj
dotnet test tests/Drm.Server.Tests/Drm.Server.Tests.csproj
dotnet test tests/Drm.Agent.Core.Tests/Drm.Agent.Core.Tests.csproj
dotnet test tests/Drm.Integration.Tests/Drm.Integration.Tests.csproj
```

On non-Windows hosts, Windows-targeted projects use `EnableWindowsTargeting` narrowly in their project files so restore and build can run on macOS.

Full solution:

```bash
PATH=/Users/pop7/.dotnet:$PATH dotnet test Drm.sln
```

Windows UI projects:

```powershell
dotnet build src/Drm.Agent.Service.Windows/Drm.Agent.Service.Windows.csproj
dotnet build src/Drm.Agent.Tray.Windows/Drm.Agent.Tray.Windows.csproj
dotnet build src/Drm.Viewer.Windows/Drm.Viewer.Windows.csproj
```

## Phase 2A Admin and Audit APIs

The server includes admin APIs for local enterprise administration:

- `POST /api/admin/users`
- `GET /api/admin/users?tenantId=...`
- `POST /api/admin/groups`
- `POST /api/admin/groups/{groupId}/members`
- `GET /api/admin/groups/{groupId}/members?tenantId=...`
- `POST /api/admin/policy-templates`
- `GET /api/admin/policy-templates?tenantId=...`
- `GET /api/admin/policy-templates/{templateId}?tenantId=...`
- `GET /api/admin/files?tenantId=...&q=...`
- `POST /api/admin/files/{fileId}/grants`
- `PUT /api/admin/files/{fileId}/grants`
- `GET /api/admin/audit?tenantId=...&eventType=...`
- `GET /api/admin/audit.csv?tenantId=...&eventType=...`
- `POST /api/admin/siem-webhooks`
- `GET /api/admin/siem-webhooks?tenantId=...`

Identity-provider integrations such as AD, Entra ID, SAML/OIDC, and SCIM are intentionally deferred to a later phase. SIEM webhooks are also conservative in this MVP: outbound URLs must be HTTPS with public IP-literal hosts until a production allowlist or pinned resolver is added.

## Phase 3A Agent Control Plane

The server now exposes desktop-agent APIs for a visible, enterprise-managed endpoint client:

- `POST /api/agent/devices/register`
- `POST /api/agent/devices/{deviceId}/heartbeat`
- `POST /api/agent/audit`

Device registration creates tenant-scoped device records and an `agent_registered` audit event. Heartbeat updates device status/version and creates an `agent_heartbeat` audit event. Agent audit ingestion accepts endpoint-originated events with approved prefixes such as `agent_`, `file_`, and `access_`.

Windows service configuration uses the `DrmAgent` section:

```json
{
  "DrmAgent": {
    "ServerUrl": "https://drm.example",
    "TenantId": "00000000-0000-0000-0000-000000000000",
    "UserId": "00000000-0000-0000-0000-000000000000",
    "DeviceId": "00000000-0000-0000-0000-000000000000",
    "AuditQueuePath": "%ProgramData%\\DRM\\agent-audit.jsonl",
    "InventoryPath": "%ProgramData%\\DRM\\protected-inventory.json",
    "HeartbeatIntervalSeconds": 60,
    "AgentVersion": "0.1.0"
  }
}
```

The service registers the configured device, sends periodic heartbeat reports, and flushes locally queued JSONL audit events. This is a visible managed agent foundation; stealth installation, hidden persistence, and arbitrary file deletion are outside the product scope.

## Phase 3B Offline Policy Cache

Policy decisions now include a short `offlineLeaseExpiresAtUtc` value when access is allowed. The MVP lease duration is 15 minutes from the server decision time.

Agent core can persist allowed decisions in a local JSON policy cache. `OpenProtectedPdfWorkflow` always tries the server first; it only falls back to the cache when the server call fails due to transport errors, and it denies access with `offline_lease_missing` when no valid unexpired lease exists. Denied server decisions are not cached as offline allow decisions.

## Phase 3C Agent Command Queue

The management server includes an endpoint command queue for managed desktop devices:

- `POST /api/admin/files/{fileId}/commands/delete-protected-copy`
- `GET /api/agent/devices/{deviceId}/commands?tenantId=...`
- `POST /api/agent/devices/{deviceId}/commands/{commandId}/complete`

The first command type is `DeleteProtectedCopy`. Admin enqueue requires the protected file and device to exist in the same tenant. Agents poll pending commands and acknowledge either `Completed` or `Failed`.

The Windows service now has a safe delete processor for this command. A local file is deleted only when it is present in the agent inventory and `ProtectedFileReader` verifies that the file is a protected container whose tenant and file IDs match the command. Missing inventory is reported as `not_found`; parse/header mismatch is reported as `verification_failed` and the file is left untouched.

## Phase 3E File Protection Inventory

Agent core includes a file-based PDF protection workflow for desktop entry points such as tray actions and shell integration. Protecting `report.pdf` writes `report.pdf.drmx`, verifies the protected container header, and records the managed copy in the protected-file inventory used by safe remote delete.

Original PDF deletion is opt-in. When requested, the original is deleted only after server registration, protected output creation, protected-container verification, final output move, and inventory update have all succeeded. If registration or output creation fails, no `.drmx` file is committed and the original PDF remains in place.

## Phase 3F Local Key Store

Agent core includes a file-key store abstraction and JSON implementation so local desktop workflows can open protected files without passing raw keys through UI code. `ProtectPdfFileWorkflow` can persist the generated file key, and `OpenProtectedPdfFileWorkflow` reads the `.drmx` header, loads the matching key, performs the server policy decision, and decrypts the file.

The JSON key store is a local MVP bridge for development and desktop integration work. Production deployments must replace it with server-side key wrapping, tenant keys, KMS/HSM integration, and unwrap authorization.

## Phase 3G Tray Protect MVP

The Windows tray app now provides a visible PDF protection form. Users enter the management server URL, tenant ID, user ID, select a PDF, choose whether to delete the original after successful protection, and run the same `ProtectPdfFileWorkflow` used by agent core tests.

Protected output is written as `<source>.drmx`. The tray app stores local MVP metadata under `%ProgramData%\DRM`: `protected-inventory.json` for managed-copy inventory and `file-keys.json` for local file-key lookup.

## Phase 3H Viewer Open MVP

The Windows viewer can open `.drmx` files through `OpenProtectedPdfFileWorkflow`. Users enter the server URL, user ID, device ID, and protected-file path. The viewer loads the local file key from `%ProgramData%\DRM\file-keys.json`, requests a server policy decision, decrypts the PDF to a temporary local file, renders it, and overlays the returned dynamic watermark.

This viewer path displays returned permissions but does not yet fully enforce copy, print, and export controls.
