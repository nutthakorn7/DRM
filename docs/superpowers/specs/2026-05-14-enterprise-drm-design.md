# Enterprise DRM Product Design

Date: 2026-05-14

## Goal

Build an independently designed enterprise file-control platform inspired by the broad category of FinalCode-style IRM products. The product should protect business files from creation through internal use and external sharing, with central policy, a Windows desktop enforcement client, cloud and on-prem management, remote revocation, and audit evidence.

The goal is feature parity at the product-capability level, not copying FinalCode code, UI, branding, proprietary formats, or patented mechanisms.

## Product Positioning

The product is a modern enterprise DRM/IRM platform for sensitive business files. It should compete on:

- A visible, signed, enterprise-managed Windows agent.
- Same management server codebase for SaaS and on-prem deployments.
- Stronger admin experience than older IRM tools.
- Data-centric policies that follow protected files.
- Revocation and delete controls limited to files enrolled in the DRM system.
- Detailed audit trails suitable for compliance, investigations, and customer evidence.
- Extensible file support, starting with PDF and expanding to Office, CAD, ZIP, and generic containers.

## Non-Goals

- No stealth installation, hidden persistence, or deceptive endpoint behavior.
- No deletion of arbitrary user files. Remote delete applies only to protected files created or enrolled by this platform.
- No DRM bypass tooling.
- No claims of perfect screenshot prevention. The product should use watermarking, policy controls, and audit evidence while acknowledging the limits of client-side enforcement.
- No literal copying of competitor UI, file formats, protocols, or branding.

## Target Users

- Security administrators who define policies and review incidents.
- Compliance teams that need evidence of access, revocation, and deletion.
- Employees who protect and share sensitive files.
- External recipients who need controlled access without broad internal system access.
- IT teams that deploy the agent, configure identity, and operate on-prem installations.

## Threat Model

The platform reduces risk from:

- Accidental external sharing of sensitive files.
- Former employees or contractors retaining access.
- Recipients exceeding intended usage rights.
- Files copied to unmanaged locations such as USB drives, personal email, cloud drives, or partner networks.
- Loss of audit evidence after files leave the original repository.
- Oversharing through weak file-level permissions.

The platform does not fully prevent:

- A malicious authorized viewer photographing the screen.
- Manual retyping of visible content.
- Compromise of an endpoint with admin privileges.
- Kernel-level tampering by a determined attacker.
- Leaks from unprotected files never enrolled in the system.

## High-Level Architecture

```text
Admin Browser
  |
  | HTTPS
  v
DRM Management Server
  |-- SaaS mode: multi-tenant hosted platform
  |-- On-prem mode: single-tenant customer appliance
  |
  | HTTPS / mTLS-capable API
  v
Windows DRM Agent
  |-- background service
  |-- tray/status app
  |-- file protection shell integration
  |-- protected viewer
  |-- policy cache
  |-- audit uploader
  |-- revoke/delete command handler
```

## Server Components

### Admin Console

The admin console is a web app for security and IT administrators.

Required capabilities:

- Tenant setup.
- Organization hierarchy.
- User, group, role, and device management.
- Policy templates.
- File search.
- Access logs.
- Permission-change logs.
- System-change logs.
- CSV export.
- Email notifications.
- Watermark templates.
- Printer control policy.
- Remote revoke and protected-file delete.
- Policy simulator and impact preview.
- Bulk permission changes.

### API Service

The API service is the control plane for agents, integrations, and the admin console.

Core API domains:

- Authentication and device registration.
- File registration and metadata.
- Policy decisions.
- Key wrapping and unwrap authorization.
- Audit event ingestion.
- Revocation/delete command queue.
- Admin management APIs.
- Integration APIs and CLI support.

### Policy Engine

The policy engine should support RBAC first and evolve into ABAC.

Policy inputs:

- User identity.
- Group membership.
- Tenant and organization unit.
- Device identity and trust state.
- File classification.
- File owner.
- Recipient list.
- Action requested: view, edit, print, copy, export, decrypt, delete.
- Time and expiration.
- Open count.
- Network/IP/location, where available.
- Online/offline lease state.

Policy outputs:

- Allow or deny.
- Allowed actions.
- Required watermark.
- Offline lease duration.
- Print restrictions.
- Export/original-file extraction permission.
- Required audit event.
- Reason code for user/admin visibility.

### Key Management

Use envelope encryption.

- Each protected file gets a random file data key.
- The file data key is wrapped by a tenant key.
- Tenant keys are stored in KMS/HSM when available.
- SaaS mode supports per-tenant isolation.
- On-prem mode supports customer-controlled key storage.
- File unwrap requires a policy decision.
- Revocation blocks future unwrap or license renewal.
- Offline access uses short-lived signed leases.

### Audit Pipeline

Audit logs are first-class product data.

Events:

- File protected.
- File opened.
- Access denied.
- Print attempted.
- Export attempted.
- Copy/capture policy action.
- Permission changed.
- File revoked.
- Protected file deleted.
- Admin action.
- Agent registration.
- Device policy change.
- Offline lease issued or denied.

Audit requirements:

- Tenant-scoped immutable event trail.
- Search and filters.
- CSV export.
- SIEM webhook/export.
- Admin-visible reason codes.
- Tamper-evident log chain in later phases.

## Windows Agent Components

### Background Service

Runs as a signed Windows service.

Responsibilities:

- Device registration.
- Policy sync.
- Protected file inventory for enrolled files.
- Revocation/delete command polling.
- Audit buffering and upload.
- Offline lease validation.
- Local cache encryption.
- Health reporting.

### Tray App

Visible user-facing status app.

Responsibilities:

- Show connection state.
- Show signed-in user and tenant.
- Show recent policy decisions.
- Let users protect files if licensed.
- Display required admin messages.
- Provide support diagnostics.

### File Protection Handler

Adds shell integration for protecting files.

MVP:

- Protect PDF files into the product's encrypted container format.
- Register file metadata with the server.
- Assign policy template and recipients.
- Optionally delete the original unprotected file after successful protection, when policy allows and user/admin confirms.

Later:

- Office, CAD, ZIP, and generic file containers.
- Shared-folder auto-encryption.
- Transparent encryption on save/download.

### Protected Viewer

MVP viewer handles protected PDFs.

Responsibilities:

- Request policy decision before open.
- Request key unwrap or offline lease validation.
- Decrypt only into controlled local memory/cache.
- Render PDF inside the managed viewer.
- Apply dynamic screen watermark.
- Enforce print/export/copy controls as far as the viewer controls the action.
- Emit audit events for open, deny, print, export, and close.

### Endpoint Controls

Endpoint controls should be explicit, supportable, and admin-managed.

MVP controls:

- File association for protected container.
- Viewer-level copy/export/print control.
- Dynamic watermark.
- Protected-file revoke/delete.
- Policy cache and short offline leases.

Later controls:

- Application allowlist for protected files.
- Risky app blocklist while protected content is open.
- Printer policy.
- Clipboard restrictions inside managed viewer.
- Screenshot deterrence where Windows APIs allow, combined with watermarking and audit.

## Protected File Format

Use an independent container format.

Required fields:

- Magic/version.
- File ID.
- Tenant ID.
- Content type.
- Encrypted payload.
- Wrapped file key reference.
- Policy template reference.
- Owner/creator metadata.
- Creation timestamp.
- Integrity tag.
- Signature over metadata.

The container must not depend on file path or storage location. A copied file should still require server authorization or a valid offline lease before opening.

## Deployment Modes

### SaaS Mode

The vendor hosts the management server.

Requirements:

- Multi-tenant isolation.
- Per-tenant keys and policy data.
- Tenant admin console.
- Public agent API endpoint.
- Managed backups.
- Usage billing hooks later.

### On-Prem Mode

The customer runs the management server in their environment.

Requirements:

- Single-tenant deployment.
- Configurable base URL.
- Customer-controlled database.
- Customer-controlled key backend where possible.
- Backup/restore.
- TLS certificate setup.
- Optional offline license file for air-gapped environments later.

The Windows agent must support a configurable server URL so the same client can connect to either SaaS or on-prem.

## Full Capability Roadmap

### Phase 1: Foundation MVP

- Management server.
- Admin console.
- Tenant/user/group model.
- Basic local auth plus preparation for SSO.
- Policy templates.
- PDF protection.
- Protected PDF viewer.
- Windows service and tray app.
- Access decision API.
- Envelope encryption.
- View, print, copy, export permissions.
- Expiration by date and duration.
- Dynamic screen watermark.
- Remote revoke.
- Protected-file delete command.
- Audit logs and CSV export.
- Cloud and on-prem deployment packaging.

### Phase 2: Enterprise Admin Parity

- AD and Microsoft Entra ID integration.
- SAML/OIDC SSO.
- SCIM provisioning.
- Organization hierarchy.
- File search.
- Permission-change logs.
- System-change logs.
- Email notifications.
- Bulk owner/recipient changes.
- Access request and approval workflow.
- Watermark pattern management.
- Printer control configuration.
- SIEM export.

### Phase 3: Endpoint Automation

- Shared-folder auto-encryption.
- Transparent encryption on save/download.
- Original-file deletion policy.
- Save-location policy.
- Application allowlist.
- Risky application blocklist.
- Better offline access leases.
- Agent health dashboard.
- Tamper-evident local audit queue.

### Phase 4: External Sharing

- Browser-view encrypted file mode.
- Viewer-only lightweight client.
- Guest access.
- One-time access links with identity verification.
- External user lifecycle management.
- Download-disabled browser viewer.

### Phase 5: Broader File Support

- Office documents.
- CAD files.
- ZIP files.
- Generic encrypted container.
- Outlook add-in.
- API/CLI for ECM, DMS, PLM, ERP, and workflow systems.
- Multi-language UI.

## MVP Data Model

Core entities:

- Tenant.
- User.
- Group.
- Device.
- ProtectedFile.
- FileVersion.
- PolicyTemplate.
- FileGrant.
- KeyEnvelope.
- OfflineLease.
- AuditEvent.
- RevocationCommand.
- AgentHeartbeat.

Important relationships:

- A tenant owns users, groups, devices, policies, files, and keys.
- A protected file belongs to one tenant and has one active policy template.
- A file grant maps users or groups to allowed actions.
- An offline lease is short-lived and bound to user, device, file, and actions.
- Audit events are append-only and tenant-scoped.

## MVP User Flows

### Protect a PDF

1. User right-clicks a PDF and chooses Protect.
2. Agent asks for recipients, policy template, and expiration.
3. Agent generates file key and encrypted container.
4. Server registers metadata and wraps key.
5. Agent optionally deletes original file if policy requires.
6. Audit event records protection.

### Open a Protected PDF

1. User double-clicks protected file.
2. Agent identifies tenant and file ID.
3. Agent authenticates user/device.
4. Server evaluates policy.
5. If allowed, server returns a short-lived unwrap authorization or lease.
6. Viewer renders the PDF with watermark and enabled controls.
7. Audit event records the open.

### Revoke Access

1. Admin finds the file in the console.
2. Admin removes recipient access or revokes the file.
3. Server blocks future unwraps and lease renewal.
4. Agent receives command on next sync.
5. If configured, agent deletes local protected copies it manages.
6. Audit event records the revocation and deletion status.

### On-Prem Install

1. Customer deploys server package.
2. Admin configures URL, database, TLS, and key backend.
3. Admin creates first tenant/admin.
4. Windows agent is installed with customer server URL.
5. Agent registers and begins policy sync.

## Security Requirements

- TLS for all network traffic.
- Signed agent installer and binaries.
- Per-tenant key separation.
- Authenticated encryption for file payloads.
- Metadata integrity protection.
- Least-privilege service design.
- Local cache encrypted with OS-protected secrets where possible.
- Short offline leases for high-risk files.
- Explicit audit for remote delete and revoke.
- Admin role separation for policy, audit, and system configuration.
- No hidden destructive behavior.

## Testing Strategy

MVP test coverage should include:

- Unit tests for policy evaluation.
- Unit tests for key wrapping and unwrap authorization.
- Integration tests for protect/open/revoke flows.
- API authorization tests across tenants.
- Windows client tests for file association and viewer controls.
- Offline lease tests.
- Audit event completeness tests.
- SaaS multi-tenant isolation tests.
- On-prem single-tenant deployment smoke tests.
- Failure-mode tests: server unavailable, expired lease, revoked file, wrong user, wrong device.

## Open Implementation Decisions

These are design decisions to resolve in the implementation plan:

- Backend stack.
- Desktop stack for Windows client and viewer.
- PDF rendering engine.
- Database.
- KMS/HSM abstraction.
- Packaging strategy for SaaS and on-prem.
- Initial identity provider support.
- Exact protected-file extension and container serialization format.

## References

- FinalCode public feature list: https://www.finalcode.com/jp/features/list/
- FinalCode overview: https://www.finalcode.com/jp/about/
- FinalCode v6 transparent encryption overview: https://www.finalcode.com/jp/lp/v6/
- Microsoft Purview sensitivity-label encryption: https://learn.microsoft.com/en-us/purview/encryption-sensitivity-labels
- Microsoft Information Protection SDK overview: https://learn.microsoft.com/en-us/information-protection/develop/overview
- NIST SP 800-57 key management: https://csrc.nist.gov/pubs/sp/800/57/pt1/r5/final
- NIST SP 800-162 ABAC: https://www.nist.gov/publications/guide-to-attribute-based-access-control-abac-definition-and-considerations-0
