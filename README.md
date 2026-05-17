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

The server includes admin and external-sharing APIs for local enterprise administration:

- `POST /api/share-links/redeem`
- `POST /api/share-links/verification/start`
- `POST /api/share-links/verification/confirm`
- `POST /api/share-links/viewer/session`
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
- `POST /api/admin/files/{fileId}/share-links`
- `GET /api/admin/files/{fileId}/share-links?tenantId=...`
- `POST /api/admin/files/{fileId}/share-links/{shareLinkId}/revoke`
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

## Phase 5M Desktop Shell Integration

Desktop shell integration assets are available under `deploy/desktop/`. The PowerShell registration script writes current-user `HKCU:\Software\Classes` entries for a `Protect with DRM` file context menu and `.drmx` viewer association. Shell commands prefill the tray/viewer file path with `--protect` or `--open`; users still review server, identity, policy, and recipient fields before running protect or open.

## Phase 5N Tamper-Evident Audit Queue

New local agent audit queue entries are written as hash-chained JSONL envelopes with the previous entry hash and current entry hash. Flush verifies each envelope before uploading its embedded `AgentAuditRecord`, stops at tampered entries, and preserves the unuploaded suffix for investigation or retry. Legacy raw audit-record queue lines remain flushable for compatibility with older agent builds.

## Phase 5O Agent Health Dashboard

The management API now exposes `GET /api/admin/devices/health?tenantId=...&staleAfterMinutes=...` for tenant-scoped endpoint fleet health counts: total, online, stale, never seen, and disabled. The `/admin/` console shows the summary above the Agent devices table using the same tenant/admin-key context as the device inventory.

## Phase 5P Template Offline Leases

Files registered with a policy template now inherit the template's `offlineLeaseMinutes` value. Policy decisions and file-key unwrap responses return an offline lease expiry based on that stored duration; a zero-minute template still allows online access but returns no offline lease for offline fallback.

## Phase 5Q Remote Delete Console

The `/admin/` console now exposes the existing remote protected-copy delete command queue. Operators can enter a protected file ID and target device ID to enqueue `DeleteProtectedCopy` through `POST /api/admin/files/{fileId}/commands/delete-protected-copy`; endpoint agents still apply the safe inventory-and-container verification rule before deleting any local copy.

## Phase 5R Apply Template Lease Sync

Applying a policy template to an existing protected file now updates the file's stored offline lease duration along with permissions and watermark. Future policy decisions and file-key unwrap responses therefore use the currently applied template lease instead of the file's previous lease.

## Phase 5S Command Status Console

The management API now exposes `GET /api/admin/files/{fileId}/commands?tenantId=...&deviceId=...` so admins can review pending, completed, and failed endpoint commands for a protected file. The `/admin/` console includes a Command status panel in Protected files to inspect remote delete command outcomes by file and optional device filter.

## Phase 5T Watermark Alias Rendering

Desktop open workflows now render `{userId}` and `{fileId}` watermark placeholders as aliases for `{user}` and `{file}`. Policy templates and admin-entered watermark patterns that use explicit ID placeholder names display concrete user/file IDs in the viewer instead of raw template text.

## Phase 5U Integration CLI

`src/Drm.Cli` adds an automation-oriented CLI for workflow integrations. `protect` registers, wraps, and writes `.drmx` output through `ProtectFileWorkflow`; `open` unwraps a `.drmx` container through `OpenProtectedFileWorkflow` and writes decrypted bytes to an output path.

```bash
dotnet run --project src/Drm.Cli -- protect --server-url https://drm.example --tenant-id <tenant-guid> --user-id <user-guid> --file ./contract.docx --policy-template-id <template-guid>
dotnet run --project src/Drm.Cli -- open --server-url https://drm.example --user-id <user-guid> --device-id <device-guid> --file ./contract.docx.drmx --output ./contract.docx
```

## Phase 5V External Share Link Foundation

Administrators can now create, list, and revoke external share links for protected files with `POST /api/admin/files/{fileId}/share-links`, `GET /api/admin/files/{fileId}/share-links?tenantId=...`, and `POST /api/admin/files/{fileId}/share-links/{shareLinkId}/revoke`. Create responses return a high-entropy access token once, while the server stores only its SHA-256 hash and list/revoke responses never expose token material.

Share links are tenant- and file-scoped, require a guest email, expiry, and max-use limit, cannot outlive the protected file, and cannot be created for revoked files. This phase intentionally stops at enterprise link lifecycle management; guest identity verification, browser viewing, and public decrypt/file-key release remain future external-sharing work.

## Phase 5W External Share Redemption Foundation

Guests can now redeem an external share token with `POST /api/share-links/redeem` by submitting `tenantId`, `accessToken`, and `guestEmail`. This public endpoint is intentionally exempt from client API-key authentication so external recipients can reach it, but it only validates the token/email, consumes the link's max-use count, records an `external_share_accessed/external_share_link_redeemed` audit event, and returns safe file metadata.

Redemption rejects wrong tokens or guest emails without revealing link state, and it blocks revoked links, expired links, exhausted max-use links, revoked files, and expired files with explicit reason codes. It still does not return wrapped keys, decrypted content, or browser-view data; those remain separate browser viewer and guest identity-verification work.

## Phase 5X External Share Verification Sessions

External recipients can now start and confirm a guest verification session with `POST /api/share-links/verification/start` and `POST /api/share-links/verification/confirm`. Start validates the share token, guest email, and active file/link state, generates a six-digit verification code, stores only its hash, and sends the code through the injectable `IExternalShareVerificationSender` abstraction. The default sender is a no-op placeholder for production mail/SMS integration.

Confirm validates the code, tracks failed attempts, blocks expired or exhausted verifications, stores only a short-lived session-token hash, and returns the plaintext verification session token once. This is still identity/session groundwork only; browser viewing and file-key release remain separate gated work.

## Phase 5Y Verified Viewer Session Foundation

Verified external guests can now open a viewer session with `POST /api/share-links/viewer/session` by submitting `tenantId` and the one-time verification session token from Phase 5X. The endpoint hashes the submitted token, rechecks session expiry plus active share-link and file state, consumes the share link's max-use count only once per verification session, and records an `external_share_viewer/external_share_viewer_opened` audit event.

The response is limited to viewer-safe metadata: IDs, guest email, content type, file/link/session expiry, watermark template, and fixed disabled-action flags for download, print, and export. It still does not return file keys, wrapped keys, ciphertext, decrypted content, or browser-rendered document bytes.

## Phase 5Z External Browser Viewer Shell

External guests can now use the public browser shell at `/share/` to complete the share verification flow and open a metadata-only viewer session. The page calls `POST /api/share-links/verification/start`, `POST /api/share-links/verification/confirm`, and `POST /api/share-links/viewer/session`, then displays safe file/session metadata with download, print, and export visibly disabled.

This is still a locked viewer shell only. It keeps the verification session token in memory, clears it after opening the viewer session, and does not request file keys, wrapped keys, ciphertext, decrypted content, or rendered document bytes.

## Phase 5AA External Share URL Launch Flow

Admin share-link creation now returns a ready-to-open `shareUrl` alongside the one-time `accessToken`. The URL includes `tenantId`, `accessToken`, and `guestEmail` query parameters so recipients can open `/share/` with prefilled access details instead of manually copying values.

The `/share/` shell reads those query parameters, prefills the verification-start form, and keeps the same security boundary: guests still must request and confirm a verification code before a viewer session opens, and no file keys or document bytes are released by this phase.

## Phase 5AB Server-Side File Ciphertext Storage

When an agent or CLI protects a file, the server now stores the encrypted file payload alongside the wrapped key. The protection workflow uploads nonce, ciphertext, tag, and AEAD associated data to `POST /api/files/{fileId}/ciphertext` immediately after writing the `.drmx` container. This payload is the prerequisite for external browser viewers to decrypt and render protected documents without trusting the agent's local filesystem.

## Phase 5AC External Viewer Content and Key Delivery

Verified external viewer sessions can now retrieve the encrypted file payload and the unwrapped file key from the server. `GET /api/share-links/viewer/content` returns nonce, ciphertext, tag, and AEAD associated data for a file whose ciphertext was uploaded in Phase 5AB. `POST /api/share-links/viewer/key` unwraps and returns the plaintext file key. Both endpoints re-validate session expiry, share-link state, and file revocation. Each key release records an `external_share_viewer_key_released` audit event.

## Phase 5AD Browser PDF Rendering

External guests with a verified viewer session can now view protected PDF files directly in the browser. After opening a viewer session, the `/share/` shell fetches the encrypted file payload from `GET /api/share-links/viewer/content` and the unwrapped file key from `POST /api/share-links/viewer/key`, decrypts the content in-memory using the Web Crypto API (AES-256-GCM), and renders all pages into `<canvas>` elements via PDF.js. The file key and decrypted bytes are never written to browser storage (localStorage, sessionStorage, or IndexedDB), and no download, print, or export actions are exposed. A watermark overlay derived from the viewer session policy is displayed over the rendered pages.

Non-PDF content types receive an "unsupported content type" message and no content is fetched.

## Phase 5AE SMTP Email Verification

External share verification codes are now delivered via SMTP. Configure `Drm:Email:SmtpHost` to activate the SMTP sender; without that setting the server uses the no-op sender suitable for development and test environments.

Example configuration (see `deploy/management/appsettings.onprem.example.json`):

```json
"Drm": {
  "Email": {
    "SmtpHost": "smtp.example.com",
    "SmtpPort": 587,
    "SmtpUseTls": true,
    "SmtpUsername": "noreply@example.com",
    "SmtpPassword": "REPLACE_WITH_SMTP_PASSWORD",
    "FromAddress": "noreply@example.com",
    "FromName": "DRM Security"
  }
}
```

The verification email is plain text containing the six-digit code and its expiry time. When `SmtpHost` is absent (default), the no-op sender is registered and verification codes are only accessible through the `POST /api/share-links/verification/start` response for integration testing and local development.

## Phase 5AF Entra ID Directory Sync

Administrators can now sync users and groups from Microsoft Entra ID (Azure AD) without manually creating them one-by-one. Configure a [Microsoft Entra application registration](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app) with `User.Read.All` and `Group.Read.All` application permissions (not delegated), then grant admin consent.

New admin endpoints:

- `PUT /api/admin/directory/config` — store Entra tenant ID, client ID, and client secret for a DRM tenant
- `GET /api/admin/directory/config?tenantId=...` — retrieve config (client secret is never returned)
- `POST /api/admin/directory/sync` — trigger an immediate sync; returns users, groups, and memberships upserted

The sync maps Entra object IDs to DRM user/group IDs, allowing subsequent SSO sessions to look up the matching DRM user by the Entra `oid` claim. Users already in DRM with the same object ID have their email and display name updated on sync. Group memberships are additive — members deleted from Entra are not removed from DRM in this phase.

The `/admin/` console includes a **Directory sync** section for saving config and triggering sync. Sync errors (invalid credentials, network failure) return HTTP 500 with details in the server log.

Client secret is stored in plain text in the server database. Production deployments should restrict database access and consider a secrets manager or KMS integration for the client secret at rest.

## Phase 5AG Admin Email Notifications

Administrators can now receive email alerts when key DRM events occur. Configure per-tenant notification settings with:

- `PUT /api/admin/notification-config` — set admin email addresses and opt-in flags for each event type
- `GET /api/admin/notification-config?tenantId=...` — retrieve current config

Supported notification events (each independently opt-in):

| Event | Flag |
|---|---|
| External guest opens a viewer session | `notifyOnExternalShareViewed` |
| A protected file is revoked | `notifyOnFileRevoked` |
| Policy decision denies access | `notifyOnAccessDenied` |
| An external share link is created | `notifyOnShareLinkCreated` |

`adminEmailsCsv` accepts a comma-separated list of admin email addresses. Notifications use the same `Drm:Email:SmtpHost` configuration as Phase 5AE verification codes. When SMTP is unconfigured, the notification sender is a no-op and no emails are sent.

SMTP failures log a warning and do not affect the primary operation (file revoke, policy decision, etc.). The `/admin/` console includes an **Email notifications** section for loading and saving tenant notification config.

## Phase 5AH SCIM 2.0 Provisioning

Enterprise identity providers (Entra ID, Okta, OneLogin) can now automatically provision and deprovision DRM users and groups via the SCIM 2.0 standard (RFC 7644).

**Base URL per tenant:** `/scim/v2/{tenantId}/`

**Authentication:** `Authorization: Bearer <AdminApiKey>` — uses the same key as `X-DRM-Admin-Key`.

### Users

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/scim/v2/{tenantId}/Users` | List users (supports `filter`, `startIndex`, `count`) |
| `POST` | `/scim/v2/{tenantId}/Users` | Create user |
| `GET` | `/scim/v2/{tenantId}/Users/{id}` | Get user by DRM UserId |
| `PUT` | `/scim/v2/{tenantId}/Users/{id}` | Replace user (updates email, displayName, externalId, active) |
| `DELETE` | `/scim/v2/{tenantId}/Users/{id}` | Delete user and remove from all groups |

Supported filter attributes: `userName eq "..."` and `externalId eq "..."`.

The `active` field is stored on the user record. Policy enforcement based on `active = false` is not yet implemented — a deactivated user can still open files in this phase.

### Groups

| Method | Path | Description |
|--------|------|-------------|
| `GET` | `/scim/v2/{tenantId}/Groups` | List groups (supports `filter`, `startIndex`, `count`) |
| `POST` | `/scim/v2/{tenantId}/Groups` | Create group |
| `GET` | `/scim/v2/{tenantId}/Groups/{id}` | Get group with current member list |
| `PUT` | `/scim/v2/{tenantId}/Groups/{id}` | Replace group — replaces entire member list |
| `DELETE` | `/scim/v2/{tenantId}/Groups/{id}` | Delete group and all memberships |

Supported filter attributes: `displayName eq "..."` and `externalId eq "..."`.

Group PUT replaces the entire membership list. Members not present in the request body are removed. Members are referenced by DRM UserId (`value` field).

### ServiceProviderConfig

`GET /scim/v2/{tenantId}/ServiceProviderConfig` returns supported SCIM capabilities. PATCH and bulk operations are not supported in this phase.

### IdP Configuration

Configure the SCIM provisioning app in your IdP with:
- **Tenant URL:** `https://your-server/scim/v2/{tenantId}`
- **Secret Token:** value of `Drm:Security:AdminApiKey`

## Phase 5AI Anti-Camera Capture Watermark

The Windows viewer renders a **tiled, rolling, time-stamped watermark** across the document viewport to deter and forensically trace camera-based screen capture. Each `WatermarkTemplateEntity` now persists anti-capture configuration that admins can manage via console; the viewer continuously refreshes the watermark text and applies a jitter offset every second so that any photo captures unique identifying information.

### Anti-capture fields on `WatermarkTemplateEntity`

| Field | Range | Default | Purpose |
|-------|-------|---------|---------|
| `OpacityPercent` | 5–100 | 33 | Watermark transparency |
| `DensityTiles` | 1–12 | 4 | Tile repetitions (rows × cols target) |
| `DiagonalAngleDegrees` | -90–90 | -28 | Rotation angle for each tile |
| `IncludeUserId` | bool | true | Render viewing user identity |
| `IncludeTimestamp` | bool | true | Render live UTC timestamp |
| `IncludeIpAddress` | bool | false | Render client IP |
| `IncludeSessionId` | bool | false | Render viewer session ID |
| `RollingEnabled` | bool | false | Subtle position jitter (camera defeat) |

### Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `POST` | `/api/admin/watermark-templates` | Create template (anti-capture fields optional, defaults applied) |
| `PUT` | `/api/admin/watermark-templates/{id}` | Update template incl. anti-capture settings |
| `GET` | `/api/admin/watermark-templates` | List templates (returns anti-capture fields) |
| `GET` | `/api/admin/watermark-templates/{id}` | Get template |

Validation returns `invalid_opacity_percent`, `invalid_density_tiles`, or `invalid_diagonal_angle_degrees` for out-of-range values.

### Admin console

The **Watermarks** panel now includes an expandable **Anti-camera capture settings** form to update an existing template by ID. The templates table shows a compact summary column (e.g. `op 33% · tiles 4 · -28° · user+ts`).

### Windows viewer

Replaces the single centered text overlay with a 4×4 `UniformGrid` of tiled diagonal watermarks plus a `DispatcherTimer` that refreshes the rendered timestamp and applies a random ±6 px offset each second, making a photographic capture identify both the user and the exact moment of the capture.

## Phase 5AJ-UX FinalCode-Style UX Polish

Three visible UX changes inspired by FinalCode workflow patterns:

### 1. Drag-and-drop file source — Windows tray protect form

The `Drm.Agent.Tray.Windows` window now accepts file drops. Drop any file onto the window or onto the highlighted Source-file area to populate the path — no Browse click required. The drop zone tints blue on hover and shows a placeholder hint when empty. Folders are rejected with a status message.

### 2. Drag-and-drop open — Windows viewer

`Drm.Viewer.Windows` accepts a dropped `.drmx` file anywhere on the window and populates the **File** field with status `Ready to open: <name>`. Non-`.drmx` drops are politely rejected.

### 3. Protect-in-one-step wizard — admin console

The **Protected files** panel gains an expandable wizard that runs three steps in sequence from a single form:
1. Apply policy template (optional)
2. Grant `View`/`Print`/… to a User or Group recipient (optional)
3. Create an external share link with guest email + expiry (optional)

Each step is skipped when its inputs are blank, producing a checked-step output log instead of separate panel hops. This matches FinalCode\047s single-form recipient + expiry workflow.

## Phase 5AJ Box Integration

Server-side integration with Box (cloud storage) lets administrators connect a Box enterprise account, receive Box webhook events for file uploads/changes, and surface activity in the admin console and Windows tray.

### Capabilities

- **Per-tenant Box configuration** — Client ID, Client Secret, Enterprise ID, Webhook signing secret, Enabled flag, with last-connection status and webhook event counter
- **Test connection** — performs an OAuth2 client-credentials token request against `https://api.box.com/oauth2/token` to verify credentials and stores the result
- **Webhook receiver** — public `POST /api/box/webhook` endpoint verifies the Box `BOX-SIGNATURE-PRIMARY` / `BOX-SIGNATURE-SECONDARY` HMAC-SHA256 signatures, parses the JSON payload (trigger, source.id, source.name, created_by.login), and persists an event row
- **Event activity feed** — admin console table of recent events
- **Tray status indicator** — green/amber/grey dot shows whether Box is connected, enabled, or unconfigured

### Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `PUT` | `/api/admin/box/config` | Upsert per-tenant Box configuration |
| `GET` | `/api/admin/box/config?tenantId=...` | Retrieve current configuration (secrets omitted from response) |
| `POST` | `/api/admin/box/test-connection` | Validates credentials by calling the Box token endpoint and persists status |
| `GET` | `/api/admin/box/events?tenantId=...&limit=N` | Lists the most recent webhook events (newest first, default 50, max 200) |
| `POST` | `/api/box/webhook` | Public Box webhook receiver — requires `X-DRM-Tenant-Id` header and a valid HMAC signature in `BOX-SIGNATURE-PRIMARY` or `BOX-SIGNATURE-SECONDARY`. Returns `202 Accepted` on success, `401` on bad signature, `404` if the tenant has no enabled configuration |

### Box app setup

1. In the Box Developer Console, create a **Custom App** with **Server Authentication (Client Credentials Grant)**
2. Grant the app access to your enterprise and authorize it from the admin console
3. Copy the **Client ID**, **Client Secret**, and **Enterprise ID** into the DRM admin Box panel
4. Create a Box webhook with the URL `https://your-server/api/box/webhook` and the header `X-DRM-Tenant-Id: <tenantId>`. Use the same signing secret on the Box side and the DRM config

### Security

Webhook signatures use `HMAC-SHA256(secret, raw-body)` base64-encoded, compared with `CryptographicOperations.FixedTimeEquals` to prevent timing attacks. The webhook endpoint reads the raw request body before any framework parsing to ensure the signature applies to the exact bytes Box sent.

This phase delivers the data and admin plane for Box integration. The Box file-content download → encrypt → re-upload data plane is deferred to a follow-up phase (configuration and webhook surface ship now so the admin workflow is visible).

## Phase 5AK Outlook Add-in Integration

Delivers an Office Web Add-in that scans outgoing email attachments and registers each one with the DRM server. Auto-encryption policy is configured per tenant from the admin console; the add-in itself is a standard Office 365 manifest sideloadable across Outlook desktop, web, and mobile.

### Capabilities

- **Per-tenant Outlook config** — Enabled flag, auto-encrypt toggle, minimum attachment size in KB, comma-separated skip domains, optional default policy template, lifetime protected counter
- **Skip-domain rules** — recipient domains in the skip list short-circuit protection (e.g., internal-only addresses)
- **Size threshold** — attachments smaller than the configured floor are passed through unchanged
- **Activity feed** — every scanned attachment generates an `OutlookAttachmentEventEntity` row with sender, recipient list, file name + size, status, and optional protected file ID
- **Manifest endpoint** — `GET /outlook-addin/manifest.xml` returns a sideload-ready Office manifest (`${SERVER_BASE_URL}` placeholder must be replaced with the deployment URL)
- **Task pane** — `GET /outlook-addin/taskpane.html` hosts a 250 px Office.js panel with Scan button and credential inputs persisted in `localStorage`

### Endpoints

| Method | Path | Description |
|--------|------|-------------|
| `PUT` | `/api/admin/outlook/config` | Upsert per-tenant configuration |
| `GET` | `/api/admin/outlook/config?tenantId=...` | Retrieve configuration including lifetime protected counter |
| `GET` | `/api/admin/outlook/events?tenantId=...&limit=N` | Recent attachment events newest-first (max 200) |
| `POST` | `/api/outlook/protect-attachment` | Add-in calls this with attachment metadata; returns one of `protected`, `skipped_recipient_domain`, `skipped_below_min_size`, `skipped_auto_disabled`. Requires the standard client API key header. |
| `GET` | `/api/outlook/status?tenantId=...` | Public-by-design status check for the add-in (enabled, auto-encrypt, min size, lifetime count) |

### Sideload instructions

1. In the DRM admin console open **Outlook** panel, click **Load**, and copy the manifest URL shown
2. Download the manifest, replace every `${SERVER_BASE_URL}` token with your DRM server URL (e.g., `https://drm.example.com`)
3. In Outlook: **Get Add-ins → My add-ins → Add a custom add-in → Add from file**, select the edited manifest
4. Open the **DRM Protect Attachments** task pane and enter Server URL, Tenant ID, and Client API key (values are cached in `localStorage` for the next session)
5. Click **Scan & register attachments** on any composed message — every attachment generates an event row visible in the admin events table

This phase ships the configuration plane, classification logic, manifest, and task pane. The attachment-content upload + encrypted .drmx replacement data plane is deferred to a follow-up — current behaviour registers metadata so administrators can audit attachment flow end-to-end.
