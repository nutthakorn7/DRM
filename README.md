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

Agent core includes a file-key store abstraction and JSON implementation so local desktop workflows can keep an offline fallback without passing raw keys through UI code. `ProtectPdfFileWorkflow` can persist the generated file key locally after server wrapping succeeds.

The JSON key store is a local MVP bridge for development and offline desktop integration work. Production deployments must treat it as a controlled fallback and pair it with server-side key wrapping, tenant keys, KMS/HSM integration, endpoint authentication, and local secret protection.

## Phase 3I Server Key Wrapping

The server now exposes file-key wrapping APIs:

- `POST /api/files/{fileId}/keys/wrap`
- `POST /api/files/{fileId}/keys/unwrap`

Wrap stores an AES-GCM wrapped file key for a registered protected file. Unwrap first evaluates policy for the requested permission and returns the file key only when access is allowed. This moves the product toward server-authorized key release instead of relying only on local key files.

The current implementation derives tenant wrapping keys from `Drm:KeyWrapping:MasterKeyBase64`; if omitted, it uses a development fallback key. Production deployments must configure a durable KMS/HSM-backed master key and migrate stored keys with operational key rotation procedures.

## Phase 3J Desktop Server Key Flow

Desktop protect/open now uses server key wrapping as the primary key path. `ProtectPdfFileWorkflow` registers the protected file, wraps the generated file key with the management server, then writes the `.drmx` output and optional local fallback key. If registration or key wrap fails, no protected output is committed and the original PDF remains in place.

`OpenProtectedPdfFileWorkflow` reads the `.drmx` header and asks the server to unwrap the file key for `View` before using any local key. Server 403/404 unwrap responses are treated as access failures and do not fall back to local JSON keys. The local key store is used only when the unwrap call fails without an HTTP status, which represents transport/server unavailability; the normal policy decision path still runs and can use the offline policy cache.

## Phase 3K Unwrap Decision Metadata

Successful server unwrap responses now include the decision metadata needed by desktop open: allowed permissions, watermark template, and offline lease expiry. `DrmServerClient.UnwrapFileKeyAsync` returns a typed `UnwrappedFileKey` result instead of only raw key bytes.

The viewer file-open workflow uses that unwrap metadata directly to decrypt, watermark, and cache offline lease decisions without making a second `/api/policy/decide` call. Local-key fallback still uses the policy decision path so offline access remains policy-gated.

## Phase 3G Tray Protect MVP

The Windows tray app now provides a visible PDF protection form. Users enter the management server URL, tenant ID, user ID, select a PDF, choose whether to delete the original after successful protection, and run the same `ProtectPdfFileWorkflow` used by agent core tests.

Protected output is written as `<source>.drmx`. The tray app stores local MVP metadata under `%ProgramData%\DRM`: `protected-inventory.json` for managed-copy inventory and `file-keys.json` for offline file-key fallback.

## Phase 3H Viewer Open MVP

The Windows viewer can open `.drmx` files through `OpenProtectedPdfFileWorkflow`. Users enter the server URL, user ID, device ID, and protected-file path. The viewer requests a server policy-gated key unwrap, decrypts the PDF to a temporary local file from the unwrap decision metadata, renders it, and overlays the returned dynamic watermark. It stores fallback keys in `%ProgramData%\DRM\file-keys.json` and policy leases in `%ProgramData%\DRM\policy-decisions.json`.

This viewer path displays returned permissions but does not yet fully enforce copy, print, and export controls.

## Phase 4A Management Install Baseline

On-prem management server install assets are under `deploy/management/`. The baseline includes an example production config, `start-management.sh`, and operator notes for publishing, setting `DRM_KEY_WRAPPING_MASTER_KEY_BASE64`, choosing `DRM_DATA_DIR`, and checking `/healthz`.

This is a runnable management install baseline, not final production hardening. Real deployment still needs TLS, API authentication, service supervision, backups, audit retention, key rotation, and host monitoring.

## Phase 4B Admin API Key Auth

When `Drm:Security:AdminApiKey` is configured, `/api/admin/*` endpoints require `X-DRM-Admin-Key`. Missing keys return 401 and wrong keys return 403. The on-prem management start script now requires `DRM_ADMIN_API_KEY` and exports it as server configuration.

## Phase 4C Management Console Shell

The management server now serves a browser console at `/admin/`. The shell stores the tenant ID and admin API key in browser session storage, sends `X-DRM-Admin-Key` on admin API calls, lists tenant users, creates users, and checks `/healthz`.

This is an MVP operations shell. The admin APIs still enforce the configured API key; production deployments should add TLS, stronger identity, CSRF/CORS policy, and audit review before broad operator rollout.

## Phase 4D Console Admin Operations

The `/admin/` console now includes group creation, group membership management, protected file listing, and file grant updates in addition to user management and health checks. Grant updates call the existing `/api/admin/files/{fileId}/grants` endpoint with subject type, subject ID, and permissions.

## Phase 4E Admin File Revocation

Management revocation is available at `POST /api/admin/files/{fileId}/revoke` with `tenantId` and `adminUserId`. Admin file search responses now include `revoked`, and the `/admin/` console shows active/revoked status with a revoke action for active files. Revoked files are denied by the existing policy evaluator and key unwrap flow.

## Phase 4F Policy Template Console

The `/admin/` console now creates and lists policy templates through `/api/admin/policy-templates`. Operators can set template name, permissions, watermark template, offline lease minutes, and print allowance from the management UI.

## Phase 4G Audit Console

The `/admin/` console now lists tenant audit events from `/api/admin/audit`, supports exact event-type filtering, and exports CSV through `/api/admin/audit.csv` while preserving the admin API key header.

## Phase 4H SIEM Console

The `/admin/` console now creates and lists SIEM webhook integrations through `/api/admin/siem-webhooks`. Operators can set webhook ID, HTTPS URL, and enabled state from the management UI.

## Phase 5A Client API Key Auth

When `Drm:Security:ClientApiKey` is configured, non-admin `/api/*` endpoints require `X-DRM-Client-Key`. `/api/admin/*` remains protected separately by `X-DRM-Admin-Key`, and deployments with no client key configured keep the previous unauthenticated client API behavior.

## Phase 5B Desktop Client API Key

Desktop clients can now send `X-DRM-Client-Key`. The Windows service reads `DrmAgent:ClientApiKey`, and the tray protector/viewer include Client API key fields for manual workflows.

## Phase 5C Admin Device Inventory

The management API now exposes `GET /api/admin/devices?tenantId=...&userId=...&status=...` for tenant-scoped desktop agent inventory. The `/admin/` console includes an Agent devices section with status and user filters.

## Phase 5D Device Disable Enforcement

Administrators can disable a registered device with `POST /api/admin/devices/{deviceId}/disable`. Disabled devices keep their inventory record with `status = disabled`, cannot re-register or heartbeat back online, and receive `device_disabled` denials for future policy decisions and file-key unwrap attempts. The `/admin/` console exposes this as a Disable action in the Agent devices table.

## Phase 5E Viewer Permission Controls

The Windows protected viewer now gates its visible Copy, Print, and Export controls from the opened file's returned permissions. `Ctrl+C`, `Ctrl+P`, and `Ctrl+S` are blocked when the matching permission is missing, and original PDF export is only available when `ExportOriginal` is granted. These are viewer-level controls and do not claim to prevent out-of-band capture or endpoint tampering.

## Phase 5F Viewer Action Audit

Viewer-controlled Copy, Print, and Export actions now emit endpoint audit events through `/api/agent/audit` when the action is explicitly allowed or blocked by policy. Events use the existing accepted prefixes: `copy_allowed`, `copy_blocked`, `print_allowed`, `print_blocked`, `export_allowed`, and `export_blocked`.

## Phase 5G Admin Policy Simulator

Administrators can preview policy outcomes with `POST /api/admin/policy-simulator` without issuing file keys or writing endpoint access audit events. The `/admin/` console includes a Policy simulator section for tenant-scoped file, user, device, and permission checks.

## Phase 5H Watermark Template Management

Administrators can manage reusable watermark patterns with `POST /api/admin/watermark-templates`, `GET /api/admin/watermark-templates?tenantId=...`, and `GET /api/admin/watermark-templates/{watermarkTemplateId}?tenantId=...`. The `/admin/` console includes a Watermark templates section for tenant-scoped pattern management.

## Phase 5I Apply Policy Templates

Administrators can apply an existing policy template to a protected file with `POST /api/admin/files/{fileId}/apply-policy-template`. Applying a template updates the file permissions and watermark template, synchronizes the owner grant with the template permissions, and records a `permission_changed/policy_template_applied` audit event. The `/admin/` console exposes this action from the Protected files section.

## Phase 5J Desktop Template Recipients

Client file registration now accepts optional `policyTemplateId` and user/group `recipients`. When a template is supplied, `/api/files` applies the template permissions and watermark to the protected file and grants those permissions to the owner plus requested recipients. The Windows tray protect form includes fields for policy template ID, recipient user IDs, and recipient group IDs so desktop protection can use managed policy templates directly.

## Phase 5K Generic File Protection

The agent core now includes `ProtectFileWorkflow` for protecting arbitrary file types into the existing `.drmx` container. The workflow stores the source content type in the protected-file header, supports common Office, ZIP, CAD, text, CSV, and PDF extensions, and falls back to `application/octet-stream` for unknown extensions. The tray protect form now accepts any source file while keeping the same policy template and recipient controls.

## Phase 5L Generic File Open

The agent core now includes `OpenProtectedFileWorkflow` for opening `.drmx` containers without assuming the payload is a PDF. It returns the protected header content type with the decrypted bytes, policy permissions, and watermark metadata. The Windows viewer still renders PDFs inline, and it can load non-PDF protected files for policy-gated original export when `ExportOriginal` is allowed.
