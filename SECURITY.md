# DRM Security Model

What the trust boundaries are, who can touch what, and what's a parameter
versus what's an auth primitive. Audit this document before reasoning about
any "is action X safe?" question.

Latest review: 2026-05-17.

---

## Three roles

| Role | Authenticates with | Scope |
|---|---|---|
| **Admin** | `X-DRM-Admin-Key` header (shared secret) | God-mode across **all tenants** in this deployment |
| **Agent** (desktop client) | Device ID + tenant ID + user ID in request body | Bound to the device it registered as |
| **External recipient** (`/share/`) | Tenant ID + access token + email + verification code | One-time, one-file, one-email viewer session |

There is **no per-tenant admin key today**. The admin role is global
within a deployment. Multi-tenant SaaS-style isolation (one admin key
per tenant) is on the horizon but not shipped. Run one DRM deployment
per customer if you need that boundary.

---

## What the admin key gates

The `AdminApiKeyAuthentication` middleware checks `X-DRM-Admin-Key`
against `Drm:Security:AdminApiKey` for every request whose path starts
with `/api/admin`. The check is constant-time (`CryptographicOperations.FixedTimeEquals`).

In non-Development environments, `SecurityStartupGuard` refuses to start
the server when the admin key is blank or matches the documented
placeholder value. In Development, blank is allowed so local tooling
works.

**What body-side `tenantId` really is in admin endpoints:** a parameter
that tells the admin "operate on this tenant's data." It is **not** an
auth primitive — the admin key has already let the request through, and
the admin can target any tenant. The middleware that derives effective
tenant from auth is not a thing yet.

This is intentional but worth surfacing: a typo in body `tenantId` can
make an admin mutate tenant B while they thought they were on tenant A.
The mistake is harder to make when admins use the web console because
the console always sends the same tenant they typed at the top, but it
is possible via curl or scripts.

---

## The `X-DRM-Tenant-Id` header

`TenantHeaderContext` middleware parks the header value (if present and
parseable as a GUID) into `HttpContext.Items["DrmTenantHeader"]`.
Endpoints can call `httpContext.MatchesHeader(request.TenantId)` to
assert "header agrees with body" and return a 400 `tenant_mismatch`
when they don't.

**Today:**

- The header is **optional**. Endpoints that don't check it behave as
  before.
- Endpoints that **do** check it (the **13** admin endpoints listed in
  the migration log below) act on body `tenantId` if the header is
  absent, and reject the request if the header is present with a
  different value.
- The web admin console sends the header automatically on every call,
  via the `apiFetch` / `apiFetchBlob` wrappers in `admin/app.js`.
  Tenant ID comes from the field at the top of the page, so a typo
  there will fail the same way it does today.

**Long-term:**

- Header becomes the canonical source across every admin endpoint.
- Body `tenantId` is removed from the request shape (one field, one
  source of truth).
- The header migration is one endpoint at a time to keep test churn
  low.

If you're adding a new admin endpoint today, **add `httpContext.MatchesHeader`
right after parameter validation** — that's now the project convention.

### Migration log

| Date | Endpoint | File |
|---|---|---|
| 2026-05-17 | `POST /api/admin/files/{id}/revoke` | `AdminFilesEndpoints.cs` |
| 2026-05-17 | `POST /api/admin/files/{id}/grants` | `AdminFilesEndpoints.cs` |
| 2026-05-17 | `POST /api/admin/files/{id}/share-links` | `AdminFilesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/files/{id}/commands/delete-protected-copy` | `AdminFilesEndpoints.cs` |
| 2026-05-18 | `PUT  /api/admin/files/{id}/grants` (replace) | `AdminFilesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/files/{id}/apply-policy-template` | `AdminFilesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/files/{id}/share-links/{linkId}/revoke` | `AdminFilesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/users` | `AdminUsersEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/groups` | `AdminGroupsEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/groups/{id}/members` | `AdminGroupsEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/policy-templates` | `AdminPolicyTemplatesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/watermark-templates` | `AdminWatermarkTemplatesEndpoints.cs` |
| 2026-05-18 | `PUT  /api/admin/watermark-templates/{id}` | `AdminWatermarkTemplatesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/files/{id}/tags` | `AdminFileTagsEndpoints.cs` |
| 2026-05-18 | `DELETE /api/admin/files/{id}/tags/{tag}` | `AdminFileTagsEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/devices/{id}/disable` | `AdminDevicesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/secure-containers` (register) | `AdminSecureContainersEndpoints.cs` |
| 2026-05-18 | `DELETE /api/admin/secure-containers/{id}` | `AdminSecureContainersEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/transparent-files` (register) | `AdminTransparentFilesEndpoints.cs` |
| 2026-05-18 | `DELETE /api/admin/transparent-files/{id}` (deregister) | `AdminTransparentFilesEndpoints.cs` |
| 2026-05-18 | `POST /api/admin/transparent-files/stamp` | `AdminTransparentFilesEndpoints.cs` |

**Endpoints with no tenant context** (not migrated, by design):
- `POST /api/admin/transparent-files/verify` — request body is only `FileBytesBase64`;
  the HMAC trailer carries its own tenant info that's verified server-side.

**Still on the list** (~15 mutating admin endpoints): folder-watcher config,
SIEM webhook CRUD, Box / Outlook / Directory integration upsert, external-share
settings, license updates, audit ingest. Migrate one file at a time per session;
test coverage = at least one `tenant_mismatch` assertion per new file.

---

## What the agent endpoints trust

`/api/agent/*` endpoints do **not** have a separate auth layer today.
The agent identifies itself by passing `tenantId`, `userId`, and
`deviceId` in the request body. Once the agent has registered, the
server treats every subsequent request from any caller knowing those
three GUIDs as coming from that agent.

What protects this in practice:

- Device registration emits an audit event; a stolen `deviceId` after
  the fact is visible to an admin.
- Heartbeat updates `LastSeenAtUtc`; the admin console flags devices
  that go silent.
- `/api/policy/decide` is the per-action gate. Even a compromised
  device can't bypass policy: the AES file key only comes back from
  `/api/files/{id}/keys/unwrap` when the policy decision says
  `allowed`. The viewer never persists the key.

What this doesn't protect:

- Network adversaries with all three GUIDs can impersonate the agent
  for the duration of the share. HTTPS + mutual TLS would close this
  in v1.x. Until then, treat the GUIDs as session-bearer credentials
  and rotate (revoke + re-register the device) if they leak.

---

## What `/api/policy/decide` actually decides

```
input  : { tenantId, fileId, userId, requestedPermission }
output : { allowed, allowedPermissions, reasonCode, watermarkTemplate,
           offlineLeaseExpiresAtUtc }
```

Reason codes emitted:

| reasonCode | When |
|---|---|
| `allowed` | Grant exists and includes the requested permission |
| `no_grant` | No `FileGrantEntity` for this user (or via group) |
| `permission_not_granted` | Grant exists, doesn't cover the requested action |
| `revoked` | `file.Revoked == true` |
| `expired` | `file.ExpiresAtUtc < now` |
| `tenant_inactive` | License lapsed or tenant suspended |
| `no_grant` after `file_owner_changed` | Owner transfer cleared prior grants |

`offlineLeaseExpiresAtUtc` tells the viewer how long it may open the
file without re-checking with the server. When the lease expires, the
viewer must re-call `/api/policy/decide` (or close the file).

---

## What's in scope vs out of scope for v1.0

In scope (shipped):

- Shared-secret admin key with constant-time compare and startup guards
- `tenant_mismatch` assertion via `X-DRM-Tenant-Id` on 3 admin file
  endpoints (revoke, grants, share-links)
- Per-file revoke (server-side kill switch)
- Remote-delete-protected-copy command from admin to agent
- External-recipient verification (3-factor: token + email + emailed
  code) for `/share/`
- Append-only audit log of every protect/open/print/deny/revoke event
- HMAC trailer for transparent-encryption with secret pinned server-side
  (never returned to clients in non-Development)
- PBKDF2-SHA256 @ 600 000 iterations + per-container random salt for
  Secure Container (v2 header; legacy v1 still opens)
- Zip-slip rejection in container pack/unpack
- LRU bound on the folder-watcher tracker (50 000 entries)

Out of scope (not in v1.0, may ship in v1.x):

- Per-tenant admin keys / multi-tenant SaaS isolation
- mTLS between agent and server
- FIPS 140-2 Level 1 certification (external auditor)
- Hardware Security Module (HSM) for the tenant master key
- Signed agent binaries / attestation
- DRM container offline crypto on iOS/Android (Phase 6)

---

## Reporting a vulnerability

This repository is single-developer and has no remote. To report a
security issue, contact `pop@cyberdefense.co.th` directly. Do not file
public GitHub issues for security findings.
