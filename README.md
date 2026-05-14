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
    "HeartbeatIntervalSeconds": 60,
    "AgentVersion": "0.1.0"
  }
}
```

The service registers the configured device, sends periodic heartbeat reports, and flushes locally queued JSONL audit events. This is a visible managed agent foundation; stealth installation, hidden persistence, and arbitrary file deletion are outside the product scope.
