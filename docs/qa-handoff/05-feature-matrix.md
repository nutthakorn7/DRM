# Feature Matrix — Status × Owner × Tests

Cross-reference: every shipped feature, who built it, what tier of test covers it.

## Core (since v1.0)

| Feature | Module | Test coverage |
|---------|--------|---------------|
| File registration | `src/Drm.Server/Endpoints/FilesEndpoints.cs` | `tests/Drm.Server.Tests/FilesApiTests.cs`, Tier 1 (T1.3) |
| Encryption (AES-256) | `src/Drm.Crypto/EnvelopeCrypto.cs` | `tests/Drm.Crypto.Tests/*` |
| Key wrapping (RSA-2048) | `src/Drm.Crypto/`, `src/Drm.Server/FileKeyProtector.cs` | `tests/Drm.Server.Tests/FileKeyApiTests.cs` |
| Container format | `src/Drm.Container/` | `tests/Drm.Container.Tests/` |
| Policy evaluator | `src/Drm.Domain/PolicyEvaluator.cs` | `tests/Drm.Domain.Tests/PolicyEvaluatorTests.cs` (16 tests) |
| Audit chain | `src/Drm.Server/AuditChainService.cs` | `tests/Drm.Server.Tests/AdminAuditApiTests.cs` |
| Time-based expiry | `PolicyEvaluator` `ExpiresAtUtc` check | Domain tests + Tier 2 (T2.1) |
| Permissions (View/Print/Copy/Edit) | `src/Drm.Domain/Permissions.cs` | Domain tests, Tier 2 (T2.1) |
| Watermark templates | `src/Drm.Server/Endpoints/AdminWatermarkTemplatesEndpoints.cs` | `AdminWatermarkTemplatesApiTests.cs`, Tier 2 (T2.2) |
| External share links | `src/Drm.Server/Endpoints/AdminFilesEndpoints.cs` + `ExternalShareEndpoints.cs` | `ExternalShareApiTests.cs`, Tier 1 (T1.5) |
| Device disable | `src/Drm.Server/Endpoints/AdminDevicesEndpoints.cs` | `AdminDevicesApiTests.cs` |
| Device trust enforcement | `src/Drm.Server/Endpoints/AdminDeviceTrustEndpoints.cs` | `V19FeatureTests.cs` |
| IP allowlist | `src/Drm.Server/IpAllowlistService.cs` | `V19FeatureTests.cs`, Tier 2 (T2.6) |
| Key rotation | `src/Drm.Server/KeyRotationService.cs` | `V17FeatureTests.cs`, Tier 2 (T2.6) |
| Tenant management | `src/Drm.Server/Endpoints/AdminTenantsEndpoints.cs` | `AdminTenantsTests.cs`, `TenantSuspensionTests.cs` |
| Billing webhooks | `src/Drm.Server/BillingWebhookService.cs` | `TenantBillingWebhookTests.cs` |
| Per-tenant API keys | `src/Drm.Server/Endpoints/AdminTenantClientKeysEndpoints.cs` | `TenantClientKeyTests.cs` |
| GDPR erase | `src/Drm.Server/Endpoints/AdminComplianceEndpoints.cs` | `V18FeatureTests.cs` |
| File retention | `src/Drm.Server/Endpoints/AdminRetentionPolicyEndpoints.cs` | `V19FeatureTests.cs` |
| SIEM webhooks | `src/Drm.Server/SiemDispatcher.cs` | `AdminSiemApiTests.cs` |
| Email notifications | `src/Drm.Server/AdminNotificationService.cs` | `AdminNotificationConfigEndpoints` tests |
| Box integration | `src/Drm.Server/BoxIntegrationService.cs` | `AdminBoxIntegrationApiTests.cs` |
| Outlook add-in | `src/Drm.Server/Endpoints/AdminOutlookIntegrationEndpoints.cs` + add-in manifest | Manual Tier 3 (T3.3) |
| Folder watcher | `src/Drm.FolderWatcher.Service/` | `AdminFolderWatcherApiTests.cs` |
| Transparent encryption | `src/Drm.Server/Endpoints/AdminTransparentFilesEndpoints.cs` | Tier 2 (T2.4) |
| Secure containers | `src/Drm.Server/Endpoints/AdminSecureContainersEndpoints.cs` | Tier 2 (T2.4) |
| File collections | `src/Drm.Server/Endpoints/AdminFileCollectionEndpoints.cs` | Tier 2 (T2.4) |
| Admin role-based access | `src/Drm.Server/AdminIdentity.cs` | `V16FeatureTests.cs`, Tier 2 (T2.3) |

## Shipped today (2026-05-20 + 2026-05-21)

| Feature | Version | Module | Test coverage |
|---------|---------|--------|---------------|
| **zcrDRM brand identity** | v1.3.0 | `src/Drm.Server/wwwroot/admin/index.html` + CSS | Tier 0 (T0.2) |
| **Documentation + og-card** | v1.3.1 | `README.md`, `CONTRIBUTING.md`, `/static/og-card.svg` | Tier 0 (T0.6, T0.7) |
| **Access count limit (C1)** | v1.4.0 | `Drm.Domain/Policy.cs`, `FileAccessCountEntity` | Domain tests, `FileKeyApiTests`, Tier 1 (T1.6) |
| **Brute-force auto-revoke (C2)** | v1.5.0 | `BruteForceProtectionService`, `AdminBruteForcePolicyEndpoints` | `ExternalShareApiTests`, Tier 1 (T1.5), Tier 2 (T2.7) |
| **Screen-capture protection (C3)** | v1.6.0 | `Drm.Viewer.Windows/ScreenCaptureProtection.cs`, `Drm.Watermark` library | `Drm.Watermark.Tests` (24 tests), Tier 3 (T3.1, T3.2) |
| **Admin link hidden on /me/** | v1.6.1 | `src/Drm.Server/wwwroot/me/app.css` | Tier 0 (T0.3) |

## Test count today

| Suite | Count | Status |
|-------|-------|--------|
| `Drm.Domain.Tests` | 16 | ✅ green on CI |
| `Drm.Server.Tests` | 406 | ✅ green on CI |
| `Drm.Watermark.Tests` | 24 | ✅ green on CI |
| `Drm.Crypto.Tests` | ~ | ✅ green on CI |
| `Drm.Container.Tests` | ~ | ✅ green on CI |
| `Drm.Agent.Core.Tests` | ~ | ✅ green on CI |
| `Drm.Cli.Tests` | ~ | ✅ green on CI |
| `Drm.Integration.Tests` | ~ | ✅ green on CI |
| **Server total** | **446** | All green |
| Windows-side (manual) | n/a | Tier 3 manual smoke |

## Where to find more

| What | Location |
|------|----------|
| Detailed change history | `CHANGELOG.md` |
| Design tokens + brand decisions | `DESIGN.md` |
| Server architecture | `src/Drm.Server/Program.cs` (top of file = entry point and service wiring) |
| CI workflow | `.github/workflows/ci.yml` |
| Test data shape | `03-test-data.md` |
