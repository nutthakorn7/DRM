using Drm.Server;
using Drm.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

_ = builder.Configuration.GetValue<ServerMode>("Drm:Mode");

var connectionString = builder.Configuration.GetConnectionString("DrmDb")
    ?? "Data Source=drm-server.db";

builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseNpgsql(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});
builder.Services
    .AddHttpClient<ISiemEventSink, HttpSiemEventSink>()
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<ISiemDispatcher, SiemDispatcher>();
builder.Services.AddScoped<PolicyDecisionService>();
builder.Services.AddScoped<BruteForceProtectionService>();
builder.Services.AddSingleton<IFileKeyProtector, FileKeyProtector>();
builder.Services.AddHttpClient("EntraGraph");
builder.Services.AddScoped<IDirectorySyncService, EntraIdDirectorySyncService>();
builder.Services.AddHttpClient("BoxApi");
builder.Services.AddScoped<IBoxIntegrationService, BoxIntegrationService>();

var emailSettings = builder.Configuration.GetSection("Drm:Email").Get<SmtpEmailSettings>() ?? new SmtpEmailSettings();
builder.Services.AddSingleton(emailSettings);
if (emailSettings.IsConfigured)
    builder.Services.AddSingleton<IExternalShareVerificationSender, SmtpExternalShareVerificationSender>();
else
    builder.Services.AddSingleton<IExternalShareVerificationSender, NoopExternalShareVerificationSender>();
if (emailSettings.IsConfigured)
    builder.Services.AddSingleton<IAdminNotificationSender, SmtpAdminNotificationSender>();
else
    builder.Services.AddSingleton<IAdminNotificationSender, NoopAdminNotificationSender>();
builder.Services.AddScoped<IAdminNotificationService, AdminNotificationService>();

var registrationSettings = builder.Configuration.GetSection("Drm:Registration").Get<RegistrationSettings>() ?? new RegistrationSettings();
builder.Services.AddSingleton(registrationSettings);
if (emailSettings.IsConfigured)
    builder.Services.AddSingleton<IRegistrationEmailSender, SmtpRegistrationEmailSender>();
else
    builder.Services.AddSingleton<IRegistrationEmailSender, NoopRegistrationEmailSender>();

var auditSettings = builder.Configuration.GetSection("Drm:Audit").Get<AuditSettings>() ?? new AuditSettings();
builder.Services.AddSingleton(auditSettings);
builder.Services.AddHostedService<AuditRetentionWorker>();
builder.Services.AddHostedService<FileExpiryWorker>();
builder.Services.AddHostedService<AlertEvaluationWorker>();
builder.Services.AddHostedService<KeyRotationWorker>();
builder.Services.AddHostedService<DataRetentionWorker>();

var app = builder.Build();

SecurityStartupGuard.Validate(app.Configuration, app.Environment);

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    dbContext.Database.EnsureCreated();
    if (dbContext.Database.IsSqlite())
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantExternalShareSettings" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantExternalShareSettings" PRIMARY KEY,
                "ExternalSharingEnabled" INTEGER NOT NULL,
                "AllowedGuestEmailDomainsCsv" TEXT NOT NULL DEFAULT '',
                "BlockedGuestEmailsCsv" TEXT NOT NULL DEFAULT '',
                "MaxShareLinkLifetimeHours" INTEGER NULL,
                "MaxShareLinkMaxUses" INTEGER NULL,
                "MaxActiveShareLinksPerFile" INTEGER NULL,
                "UpdatedAtUtc" TEXT NOT NULL,
                "UpdatedByUserId" TEXT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileViewerPreviewPages" (
                "TenantId" TEXT NOT NULL,
                "FileId" TEXT NOT NULL,
                "PageNumber" INTEGER NOT NULL,
                "SvgMarkup" TEXT NOT NULL DEFAULT '',
                "UploadedAtUtc" TEXT NOT NULL,
                "UploadedByUserId" TEXT NOT NULL,
                CONSTRAINT "PK_FileViewerPreviewPages" PRIMARY KEY ("TenantId", "FileId", "PageNumber")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileViewerPreviewPages_TenantId_FileId"
            ON "FileViewerPreviewPages" ("TenantId", "FileId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantDirectorySyncConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantDirectorySyncConfigs" PRIMARY KEY,
                "EntraTenantId" TEXT NOT NULL DEFAULT '',
                "ClientId" TEXT NOT NULL DEFAULT '',
                "ClientSecret" TEXT NOT NULL DEFAULT '',
                "LastSyncStatus" TEXT NULL,
                "LastSyncAtUtc" TEXT NULL,
                "LastSyncUserCount" INTEGER NULL,
                "LastSyncGroupCount" INTEGER NULL,
                "UpdatedAtUtc" TEXT NOT NULL DEFAULT ''
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantAdminNotificationConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantAdminNotificationConfigs" PRIMARY KEY,
                "AdminEmailsCsv" TEXT NOT NULL DEFAULT '',
                "NotifyOnExternalShareViewed" INTEGER NOT NULL DEFAULT 0,
                "NotifyOnFileRevoked" INTEGER NOT NULL DEFAULT 0,
                "NotifyOnAccessDenied" INTEGER NOT NULL DEFAULT 0,
                "NotifyOnShareLinkCreated" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAtUtc" TEXT NOT NULL DEFAULT ''
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantBoxIntegrationConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantBoxIntegrationConfigs" PRIMARY KEY,
                "ClientId" TEXT NOT NULL DEFAULT '',
                "ClientSecret" TEXT NOT NULL DEFAULT '',
                "EnterpriseId" TEXT NOT NULL DEFAULT '',
                "WebhookSecret" TEXT NOT NULL DEFAULT '',
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "LastConnectionStatus" TEXT NULL,
                "LastConnectionAtUtc" TEXT NULL,
                "LastWebhookEventCount" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAtUtc" TEXT NOT NULL DEFAULT ''
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "BoxWebhookEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BoxWebhookEvents" PRIMARY KEY AUTOINCREMENT,
                "TenantId" TEXT NOT NULL,
                "EventType" TEXT NOT NULL DEFAULT '',
                "SourceItemId" TEXT NOT NULL DEFAULT '',
                "SourceItemName" TEXT NOT NULL DEFAULT '',
                "CreatedByEmail" TEXT NULL,
                "ReceivedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_BoxWebhookEvents_TenantId_ReceivedAtUtc"
            ON "BoxWebhookEvents" ("TenantId", "ReceivedAtUtc");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantOutlookIntegrationConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantOutlookIntegrationConfigs" PRIMARY KEY,
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "AutoEncryptOutgoingAttachments" INTEGER NOT NULL DEFAULT 1,
                "MinAttachmentSizeKb" INTEGER NOT NULL DEFAULT 0,
                "SkipDomainsCsv" TEXT NOT NULL DEFAULT '',
                "DefaultPolicyTemplateId" TEXT NULL,
                "LifetimeProtectedCount" INTEGER NOT NULL DEFAULT 0,
                "UpdatedAtUtc" TEXT NOT NULL DEFAULT ''
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "OutlookAttachmentEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OutlookAttachmentEvents" PRIMARY KEY AUTOINCREMENT,
                "TenantId" TEXT NOT NULL,
                "SenderEmail" TEXT NOT NULL DEFAULT '',
                "RecipientCsv" TEXT NOT NULL DEFAULT '',
                "AttachmentName" TEXT NOT NULL DEFAULT '',
                "AttachmentSizeBytes" INTEGER NOT NULL DEFAULT 0,
                "Status" TEXT NOT NULL DEFAULT '',
                "ProtectedFileId" TEXT NULL,
                "OccurredAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_OutlookAttachmentEvents_TenantId_Id"
            ON "OutlookAttachmentEvents" ("TenantId", "Id");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileTags" (
                "TenantId" TEXT NOT NULL,
                "FileId" TEXT NOT NULL,
                "Tag" TEXT NOT NULL,
                "AssignedAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_FileTags" PRIMARY KEY ("TenantId", "FileId", "Tag")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileTags_TenantId_Tag"
            ON "FileTags" ("TenantId", "Tag");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantUserPersonas" (
                "TenantId" TEXT NOT NULL,
                "UserId" TEXT NOT NULL,
                "Persona" TEXT NOT NULL DEFAULT 'Employee',
                "AssignedAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_TenantUserPersonas" PRIMARY KEY ("TenantId", "UserId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TransparentProtectedFiles" (
                "TenantId" TEXT NOT NULL,
                "FileId" TEXT NOT NULL,
                "OwnerUserId" TEXT NOT NULL,
                "OriginalFileName" TEXT NOT NULL DEFAULT '',
                "ContentType" TEXT NOT NULL DEFAULT '',
                "PolicyTemplateId" TEXT NULL,
                "RegisteredAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_TransparentProtectedFiles" PRIMARY KEY ("TenantId", "FileId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_TransparentProtectedFiles_TenantId"
            ON "TransparentProtectedFiles" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "SecureContainers" (
                "TenantId" TEXT NOT NULL,
                "ContainerId" TEXT NOT NULL,
                "OwnerUserId" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL DEFAULT '',
                "FileCount" INTEGER NOT NULL DEFAULT 0,
                "TotalBytes" INTEGER NOT NULL DEFAULT 0,
                "PolicyTemplateId" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_SecureContainers" PRIMARY KEY ("TenantId", "ContainerId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_SecureContainers_TenantId"
            ON "SecureContainers" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "SecureContainerFiles" (
                "TenantId" TEXT NOT NULL,
                "ContainerId" TEXT NOT NULL,
                "OrdinalIndex" INTEGER NOT NULL,
                "RelativePath" TEXT NOT NULL DEFAULT '',
                "Size" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_SecureContainerFiles" PRIMARY KEY ("TenantId", "ContainerId", "OrdinalIndex")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_SecureContainerFiles_TenantId_ContainerId"
            ON "SecureContainerFiles" ("TenantId", "ContainerId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantFolderWatcherConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantFolderWatcherConfigs" PRIMARY KEY,
                "WatchedFoldersJson" TEXT NOT NULL DEFAULT '[]',
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "LastReportStatus" TEXT NULL,
                "LastReportAtUtc" TEXT NULL,
                "LastFilesProtected" INTEGER NOT NULL DEFAULT 0,
                "Hostname" TEXT NULL,
                "UpdatedAtUtc" TEXT NOT NULL DEFAULT ''
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FolderWatcherEvents" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FolderWatcherEvents" PRIMARY KEY AUTOINCREMENT,
                "TenantId" TEXT NOT NULL,
                "Hostname" TEXT NOT NULL DEFAULT '',
                "FolderPath" TEXT NOT NULL DEFAULT '',
                "FileName" TEXT NOT NULL DEFAULT '',
                "FileSize" INTEGER NOT NULL DEFAULT 0,
                "Status" TEXT NOT NULL DEFAULT '',
                "FileId" TEXT NULL,
                "OccurredAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FolderWatcherEvents_TenantId_Id"
            ON "FolderWatcherEvents" ("TenantId", "Id");
            """);

        var connection = dbContext.Database.GetDbConnection();
        var openedHere = connection.State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            connection.Open();
        }

        var hasAllowedDomainsColumn = false;
        var hasBlockedGuestEmailsColumn = false;
        var hasMaxShareLinkLifetimeHoursColumn = false;
        var hasMaxShareLinkMaxUsesColumn = false;
        var hasMaxActiveShareLinksPerFileColumn = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"TenantExternalShareSettings\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var columnName = reader.GetString(1);
                if (string.Equals(columnName, "AllowedGuestEmailDomainsCsv", StringComparison.Ordinal))
                {
                    hasAllowedDomainsColumn = true;
                }

                if (string.Equals(columnName, "BlockedGuestEmailsCsv", StringComparison.Ordinal))
                {
                    hasBlockedGuestEmailsColumn = true;
                }

                if (string.Equals(columnName, "MaxShareLinkLifetimeHours", StringComparison.Ordinal))
                {
                    hasMaxShareLinkLifetimeHoursColumn = true;
                }

                if (string.Equals(columnName, "MaxShareLinkMaxUses", StringComparison.Ordinal))
                {
                    hasMaxShareLinkMaxUsesColumn = true;
                }

                if (string.Equals(columnName, "MaxActiveShareLinksPerFile", StringComparison.Ordinal))
                {
                    hasMaxActiveShareLinksPerFileColumn = true;
                }
            }
        }

        if (!hasAllowedDomainsColumn)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantExternalShareSettings"
                ADD COLUMN "AllowedGuestEmailDomainsCsv" TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!hasBlockedGuestEmailsColumn)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantExternalShareSettings"
                ADD COLUMN "BlockedGuestEmailsCsv" TEXT NOT NULL DEFAULT '';
                """);
        }

        if (!hasMaxShareLinkLifetimeHoursColumn)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantExternalShareSettings"
                ADD COLUMN "MaxShareLinkLifetimeHours" INTEGER NULL;
                """);
        }

        if (!hasMaxShareLinkMaxUsesColumn)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantExternalShareSettings"
                ADD COLUMN "MaxShareLinkMaxUses" INTEGER NULL;
                """);
        }

        if (!hasMaxActiveShareLinksPerFileColumn)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantExternalShareSettings"
                ADD COLUMN "MaxActiveShareLinksPerFile" INTEGER NULL;
                """);
        }

        var hasTenantUsersExternalId = false;
        var hasTenantUsersActive = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"TenantUsers\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var col = reader.GetString(1);
                if (string.Equals(col, "ExternalId", StringComparison.Ordinal)) hasTenantUsersExternalId = true;
                if (string.Equals(col, "Active", StringComparison.Ordinal)) hasTenantUsersActive = true;
            }
        }

        if (!hasTenantUsersExternalId)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantUsers" ADD COLUMN "ExternalId" TEXT NULL;
                """);
        }

        if (!hasTenantUsersActive)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantUsers" ADD COLUMN "Active" INTEGER NOT NULL DEFAULT 1;
                """);
        }

        var hasTenantGroupsExternalId = false;
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"TenantGroups\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "ExternalId", StringComparison.Ordinal))
                    hasTenantGroupsExternalId = true;
            }
        }

        if (!hasTenantGroupsExternalId)
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "TenantGroups" ADD COLUMN "ExternalId" TEXT NULL;
                """);
        }

        var watermarkColumns = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"WatermarkTemplates\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                watermarkColumns.Add(reader.GetString(1));
            }
        }

        var watermarkColumnDdl = new (string Name, string Ddl)[]
        {
            ("OpacityPercent", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"OpacityPercent\" INTEGER NOT NULL DEFAULT 33;"),
            ("DensityTiles", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"DensityTiles\" INTEGER NOT NULL DEFAULT 4;"),
            ("DiagonalAngleDegrees", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"DiagonalAngleDegrees\" INTEGER NOT NULL DEFAULT -28;"),
            ("IncludeUserId", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"IncludeUserId\" INTEGER NOT NULL DEFAULT 1;"),
            ("IncludeTimestamp", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"IncludeTimestamp\" INTEGER NOT NULL DEFAULT 1;"),
            ("IncludeIpAddress", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"IncludeIpAddress\" INTEGER NOT NULL DEFAULT 0;"),
            ("IncludeSessionId", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"IncludeSessionId\" INTEGER NOT NULL DEFAULT 0;"),
            ("RollingEnabled", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"RollingEnabled\" INTEGER NOT NULL DEFAULT 0;"),
            ("PrintWatermarkEnabled", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"PrintWatermarkEnabled\" INTEGER NOT NULL DEFAULT 0;"),
            ("PrintWatermarkPattern", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"PrintWatermarkPattern\" TEXT NOT NULL DEFAULT '';"),
            ("PrintWatermarkOpacityPercent", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"PrintWatermarkOpacityPercent\" INTEGER NOT NULL DEFAULT 33;"),
            ("PrintWatermarkPosition", "ALTER TABLE \"WatermarkTemplates\" ADD COLUMN \"PrintWatermarkPosition\" TEXT NOT NULL DEFAULT 'diagonal';"),
        };

        foreach (var (name, ddl) in watermarkColumnDdl)
        {
            if (!watermarkColumns.Contains(name))
            {
                dbContext.Database.ExecuteSqlRaw(ddl);
            }
        }

        // AuditEvents.ActorAdminId — added in v1.1 Slice 2 for RBAC actor attribution
        var auditColumns = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"AuditEvents\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                auditColumns.Add(reader.GetString(1));
        }

        if (!auditColumns.Contains("ActorAdminId"))
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "AuditEvents" ADD COLUMN "ActorAdminId" TEXT NULL;
                """);
        }

        // AdminUsers.TenantScope — added in v1.2 for tenant-scoped admin roles
        var adminUserColumns = new HashSet<string>(StringComparer.Ordinal);
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"AdminUsers\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
                adminUserColumns.Add(reader.GetString(1));
        }

        if (!adminUserColumns.Contains("TenantScope"))
        {
            dbContext.Database.ExecuteSqlRaw("""
                ALTER TABLE "AdminUsers" ADD COLUMN "TenantScope" TEXT NULL;
                """);
        }

        // Tenants table — added in v1.3 for multi-tenant SaaS mode
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Tenants" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_Tenants" PRIMARY KEY,
                "Name" TEXT NOT NULL DEFAULT '',
                "DisplayName" TEXT NOT NULL DEFAULT '',
                "Status" INTEGER NOT NULL DEFAULT 0,
                "MaxEncrypters" INTEGER NULL,
                "CreatedAtUtc" TEXT NOT NULL DEFAULT (datetime('now')),
                "SuspendedAtUtc" TEXT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tenants_Name" ON "Tenants" ("Name");
            """);
        // Backfill: insert a row for every tenant that already has data but no Tenants row
        dbContext.Database.ExecuteSqlRaw("""
            INSERT OR IGNORE INTO "Tenants" ("TenantId", "Name", "DisplayName", "Status", "CreatedAtUtc")
            SELECT DISTINCT t, t, t, 0, datetime('now')
            FROM (
                SELECT TenantId AS t FROM "TenantUsers"
                UNION SELECT TenantId FROM "TenantGroups"
                UNION SELECT TenantId FROM "ProtectedFiles"
            ) all_tenants;
            """);
        // TenantClientKeys table — added in v1.3.1 for per-tenant client API keys
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantClientKeys" (
                "TenantId" TEXT NOT NULL,
                "KeyId" TEXT NOT NULL,
                "KeyHash" TEXT NOT NULL,
                "Label" TEXT NOT NULL DEFAULT '',
                "CreatedAtUtc" TEXT NOT NULL DEFAULT (datetime('now')),
                "LastUsedAtUtc" TEXT NULL,
                "Revoked" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "PK_TenantClientKeys" PRIMARY KEY ("TenantId", "KeyId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantClientKeys_KeyHash"
            ON "TenantClientKeys" ("KeyHash");
            """);
        // TenantBillingWebhooks table — added in v1.3.2 for usage/billing event webhooks
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantBillingWebhooks" (
                "TenantId" TEXT NOT NULL,
                "WebhookId" TEXT NOT NULL,
                "Url" TEXT NOT NULL DEFAULT '',
                "Secret" TEXT NOT NULL DEFAULT '',
                "Events" TEXT NOT NULL DEFAULT '*',
                "Enabled" INTEGER NOT NULL DEFAULT 1,
                "CreatedAtUtc" TEXT NOT NULL DEFAULT (datetime('now')),
                CONSTRAINT "PK_TenantBillingWebhooks" PRIMARY KEY ("TenantId", "WebhookId")
            );
            """);
        // TenantRegistrations table — added in v1.4 for self-serve tenant onboarding
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantRegistrations" (
                "RegistrationId" TEXT NOT NULL CONSTRAINT "PK_TenantRegistrations" PRIMARY KEY,
                "TenantName" TEXT NOT NULL DEFAULT '',
                "DisplayName" TEXT NOT NULL DEFAULT '',
                "AdminEmail" TEXT NOT NULL DEFAULT '',
                "AdminDisplayName" TEXT NOT NULL DEFAULT '',
                "MaxEncrypters" INTEGER NULL,
                "Status" INTEGER NOT NULL DEFAULT 0,
                "TokenHash" TEXT NOT NULL DEFAULT '',
                "TokenExpiresAtUtc" TEXT NOT NULL DEFAULT (datetime('now')),
                "RequestedAtUtc" TEXT NOT NULL DEFAULT (datetime('now')),
                "ReviewedAtUtc" TEXT NULL,
                "ReviewNotes" TEXT NULL,
                "CreatedTenantId" TEXT NULL,
                "CreatedUserId" TEXT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_TenantRegistrations_AdminEmail"
            ON "TenantRegistrations" ("AdminEmail");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_TenantRegistrations_TenantName"
            ON "TenantRegistrations" ("TenantName");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantRegistrations_TokenHash"
            ON "TenantRegistrations" ("TokenHash");
            """);

        if (openedHere)
        {
            connection.Close();
        }
    }

    // Admin identity tables — created idempotently for both SQLite and Postgres so
    // existing v1.0.1 databases pick them up without an EF migration step.
    if (dbContext.Database.IsSqlite())
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AdminUsers" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AdminUsers" PRIMARY KEY,
                "Email" TEXT NOT NULL,
                "DisplayName" TEXT NOT NULL DEFAULT '',
                "RoleId" TEXT NOT NULL,
                "Disabled" INTEGER NOT NULL DEFAULT 0,
                "CreatedAtUtc" TEXT NOT NULL,
                "LastUsedAtUtc" TEXT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminUsers_Email" ON "AdminUsers" ("Email");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AdminRoles" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AdminRoles" PRIMARY KEY,
                "Name" TEXT NOT NULL,
                "PermissionsCsv" TEXT NOT NULL DEFAULT '',
                "IsSystem" INTEGER NOT NULL DEFAULT 0,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminRoles_Name" ON "AdminRoles" ("Name");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AdminApiTokens" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_AdminApiTokens" PRIMARY KEY,
                "AdminUserId" TEXT NOT NULL,
                "TokenHash" TEXT NOT NULL,
                "Label" TEXT NOT NULL DEFAULT '',
                "CreatedAtUtc" TEXT NOT NULL,
                "LastUsedAtUtc" TEXT NULL,
                "ExpiresAtUtc" TEXT NULL,
                "Revoked" INTEGER NOT NULL DEFAULT 0
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminApiTokens_TokenHash" ON "AdminApiTokens" ("TokenHash");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_AdminApiTokens_AdminUserId" ON "AdminApiTokens" ("AdminUserId");
            """);
        // OperatorAlertRules + OperatorAlertsFired — added in v1.5 for operator alert engine
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "OperatorAlertRules" (
                "RuleId" TEXT NOT NULL CONSTRAINT "PK_OperatorAlertRules" PRIMARY KEY,
                "Name" TEXT NOT NULL DEFAULT '',
                "TenantId" TEXT NULL,
                "Condition" INTEGER NOT NULL DEFAULT 0,
                "Threshold" REAL NOT NULL DEFAULT 0,
                "Enabled" INTEGER NOT NULL DEFAULT 1,
                "FireWebhook" INTEGER NOT NULL DEFAULT 0,
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_OperatorAlertRules_TenantId" ON "OperatorAlertRules" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "OperatorAlertsFired" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OperatorAlertsFired" PRIMARY KEY AUTOINCREMENT,
                "RuleId" TEXT NOT NULL,
                "TenantId" TEXT NULL,
                "RuleName" TEXT NOT NULL DEFAULT '',
                "ActualValue" REAL NOT NULL DEFAULT 0,
                "Threshold" REAL NOT NULL DEFAULT 0,
                "FiredAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_OperatorAlertsFired_RuleId_FiredAtUtc"
            ON "OperatorAlertsFired" ("RuleId", "FiredAtUtc");
            """);
        // v1.6: FileAccessRequests, TenantPlans, AuditChain (SQLite)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileAccessRequests" (
                "RequestId" TEXT NOT NULL CONSTRAINT "PK_FileAccessRequests" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "FileId" TEXT NOT NULL,
                "RequesterEmail" TEXT NOT NULL DEFAULT '',
                "Message" TEXT NOT NULL DEFAULT '',
                "Status" INTEGER NOT NULL DEFAULT 0,
                "ReviewedByAdminId" TEXT NULL,
                "RequestedAtUtc" TEXT NOT NULL,
                "ReviewedAtUtc" TEXT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileAccessRequests_TenantId_FileId"
            ON "FileAccessRequests" ("TenantId", "FileId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileAccessRequests_TenantId_Status"
            ON "FileAccessRequests" ("TenantId", "Status");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantPlans" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantPlans" PRIMARY KEY,
                "Tier" INTEGER NOT NULL DEFAULT 0,
                "MaxFiles" INTEGER NULL,
                "MaxStorageMb" INTEGER NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AuditChain" (
                "AuditEventId" INTEGER NOT NULL CONSTRAINT "PK_AuditChain" PRIMARY KEY,
                "Hash" TEXT NOT NULL DEFAULT '',
                "PrevHash" TEXT NOT NULL DEFAULT ''
            );
            """);
        // v1.7: FileCollections, FileCollectionItems, TenantKeyRotationConfigs, KeyRotationHistory (SQLite)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileCollections" (
                "CollectionId" TEXT NOT NULL CONSTRAINT "PK_FileCollections" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "Name" TEXT NOT NULL DEFAULT '',
                "Description" TEXT NULL,
                "CreatedAtUtc" TEXT NOT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileCollections_TenantId" ON "FileCollections" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileCollectionItems" (
                "CollectionId" TEXT NOT NULL,
                "FileId" TEXT NOT NULL,
                "TenantId" TEXT NOT NULL,
                "AddedAtUtc" TEXT NOT NULL,
                CONSTRAINT "PK_FileCollectionItems" PRIMARY KEY ("CollectionId", "FileId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileCollectionItems_TenantId" ON "FileCollectionItems" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantKeyRotationConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantKeyRotationConfigs" PRIMARY KEY,
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "IntervalDays" INTEGER NOT NULL DEFAULT 90,
                "LastRotatedAtUtc" TEXT NULL,
                "NextRotationDueUtc" TEXT NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "KeyRotationHistory" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_KeyRotationHistory" PRIMARY KEY AUTOINCREMENT,
                "TenantId" TEXT NOT NULL,
                "FilesRotated" INTEGER NOT NULL DEFAULT 0,
                "TriggeredBy" TEXT NOT NULL DEFAULT 'schedule',
                "RotatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_KeyRotationHistory_TenantId" ON "KeyRotationHistory" ("TenantId");
            """);
        // v1.8: TenantRetentionPolicies (SQLite)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantRetentionPolicies" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantRetentionPolicies" PRIMARY KEY,
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "FileRetentionDays" INTEGER NULL,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """);

        // v1.9: TenantIpAllowlistRules, TenantDeviceTrustConfigs (SQLite)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantIpAllowlistRules" (
                "RuleId" TEXT NOT NULL CONSTRAINT "PK_TenantIpAllowlistRules" PRIMARY KEY,
                "TenantId" TEXT NOT NULL,
                "Cidr" TEXT NOT NULL DEFAULT '',
                "Label" TEXT NOT NULL DEFAULT '',
                "CreatedAtUtc" TEXT NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_TenantIpAllowlistRules_TenantId" ON "TenantIpAllowlistRules" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantDeviceTrustConfigs" (
                "TenantId" TEXT NOT NULL CONSTRAINT "PK_TenantDeviceTrustConfigs" PRIMARY KEY,
                "Enabled" INTEGER NOT NULL DEFAULT 0,
                "RequiredCheckinDays" INTEGER NOT NULL DEFAULT 7,
                "UpdatedAtUtc" TEXT NOT NULL
            );
            """);

        // v1.9: time-windowed grants — add columns to existing FileGrants table
        var hasFileGrantsValidFromUtc = false;
        var hasFileGrantsValidUntilUtc = false;
        var grantsConn = dbContext.Database.GetDbConnection();
        if (grantsConn.State != System.Data.ConnectionState.Open) grantsConn.Open();
        using (var command = grantsConn.CreateCommand())
        {
            command.CommandText = "PRAGMA table_info(\"FileGrants\");";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var col = reader.GetString(1);
                if (string.Equals(col, "ValidFromUtc", StringComparison.Ordinal)) hasFileGrantsValidFromUtc = true;
                if (string.Equals(col, "ValidUntilUtc", StringComparison.Ordinal)) hasFileGrantsValidUntilUtc = true;
            }
        }
        if (!hasFileGrantsValidFromUtc)
            dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "FileGrants" ADD COLUMN "ValidFromUtc" TEXT NULL;""");
        if (!hasFileGrantsValidUntilUtc)
            dbContext.Database.ExecuteSqlRaw("""ALTER TABLE "FileGrants" ADD COLUMN "ValidUntilUtc" TEXT NULL;""");
    }
    else
    {
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AdminUsers" (
                "Id" uuid NOT NULL CONSTRAINT "PK_AdminUsers" PRIMARY KEY,
                "Email" text NOT NULL,
                "DisplayName" text NOT NULL DEFAULT '',
                "RoleId" uuid NOT NULL,
                "Disabled" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "LastUsedAtUtc" timestamp with time zone NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminUsers_Email" ON "AdminUsers" ("Email");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AdminRoles" (
                "Id" uuid NOT NULL CONSTRAINT "PK_AdminRoles" PRIMARY KEY,
                "Name" text NOT NULL,
                "PermissionsCsv" text NOT NULL DEFAULT '',
                "IsSystem" boolean NOT NULL DEFAULT FALSE,
                "CreatedAtUtc" timestamp with time zone NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminRoles_Name" ON "AdminRoles" ("Name");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AdminApiTokens" (
                "Id" uuid NOT NULL CONSTRAINT "PK_AdminApiTokens" PRIMARY KEY,
                "AdminUserId" uuid NOT NULL,
                "TokenHash" text NOT NULL,
                "Label" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL,
                "LastUsedAtUtc" timestamp with time zone NULL,
                "ExpiresAtUtc" timestamp with time zone NULL,
                "Revoked" boolean NOT NULL DEFAULT FALSE
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_AdminApiTokens_TokenHash" ON "AdminApiTokens" ("TokenHash");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_AdminApiTokens_AdminUserId" ON "AdminApiTokens" ("AdminUserId");
            """);
        // AuditEvents.ActorAdminId — added in v1.1 Slice 2 for RBAC actor attribution (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            ALTER TABLE "AuditEvents" ADD COLUMN IF NOT EXISTS "ActorAdminId" uuid NULL;
            """);
        // AdminUsers.TenantScope — added in v1.2 for tenant-scoped admin roles (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            ALTER TABLE "AdminUsers" ADD COLUMN IF NOT EXISTS "TenantScope" uuid NULL;
            """);
        // Tenants table — added in v1.3 for multi-tenant SaaS mode (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "Tenants" (
                "TenantId" uuid NOT NULL CONSTRAINT "PK_Tenants" PRIMARY KEY,
                "Name" text NOT NULL DEFAULT '',
                "DisplayName" text NOT NULL DEFAULT '',
                "Status" integer NOT NULL DEFAULT 0,
                "MaxEncrypters" integer NULL,
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
                "SuspendedAtUtc" timestamp with time zone NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_Tenants_Name" ON "Tenants" ("Name");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            INSERT INTO "Tenants" ("TenantId", "Name", "DisplayName", "Status", "CreatedAtUtc")
            SELECT DISTINCT t, t::text, t::text, 0, NOW()
            FROM (
                SELECT "TenantId" AS t FROM "TenantUsers"
                UNION SELECT "TenantId" FROM "TenantGroups"
                UNION SELECT "TenantId" FROM "ProtectedFiles"
            ) all_tenants
            ON CONFLICT DO NOTHING;
            """);
        // TenantClientKeys table — added in v1.3.1 for per-tenant client API keys (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantClientKeys" (
                "TenantId" uuid NOT NULL,
                "KeyId" uuid NOT NULL,
                "KeyHash" text NOT NULL,
                "Label" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
                "LastUsedAtUtc" timestamp with time zone NULL,
                "Revoked" boolean NOT NULL DEFAULT FALSE,
                CONSTRAINT "PK_TenantClientKeys" PRIMARY KEY ("TenantId", "KeyId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantClientKeys_KeyHash"
            ON "TenantClientKeys" ("KeyHash");
            """);
        // TenantBillingWebhooks table — added in v1.3.2 for usage/billing event webhooks (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantBillingWebhooks" (
                "TenantId" uuid NOT NULL,
                "WebhookId" uuid NOT NULL,
                "Url" text NOT NULL DEFAULT '',
                "Secret" text NOT NULL DEFAULT '',
                "Events" text NOT NULL DEFAULT '*',
                "Enabled" boolean NOT NULL DEFAULT TRUE,
                "CreatedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
                CONSTRAINT "PK_TenantBillingWebhooks" PRIMARY KEY ("TenantId", "WebhookId")
            );
            """);
        // TenantRegistrations table — added in v1.4 for self-serve tenant onboarding (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantRegistrations" (
                "RegistrationId" uuid NOT NULL CONSTRAINT "PK_TenantRegistrations" PRIMARY KEY,
                "TenantName" text NOT NULL DEFAULT '',
                "DisplayName" text NOT NULL DEFAULT '',
                "AdminEmail" text NOT NULL DEFAULT '',
                "AdminDisplayName" text NOT NULL DEFAULT '',
                "MaxEncrypters" integer NULL,
                "Status" integer NOT NULL DEFAULT 0,
                "TokenHash" text NOT NULL DEFAULT '',
                "TokenExpiresAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
                "RequestedAtUtc" timestamp with time zone NOT NULL DEFAULT NOW(),
                "ReviewedAtUtc" timestamp with time zone NULL,
                "ReviewNotes" text NULL,
                "CreatedTenantId" uuid NULL,
                "CreatedUserId" uuid NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_TenantRegistrations_AdminEmail"
            ON "TenantRegistrations" ("AdminEmail");
            CREATE INDEX IF NOT EXISTS "IX_TenantRegistrations_TenantName"
            ON "TenantRegistrations" ("TenantName");
            CREATE UNIQUE INDEX IF NOT EXISTS "IX_TenantRegistrations_TokenHash"
            ON "TenantRegistrations" ("TokenHash");
            """);

        // OperatorAlertRules + OperatorAlertsFired — added in v1.5 for operator alert engine (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "OperatorAlertRules" (
                "RuleId" uuid NOT NULL CONSTRAINT "PK_OperatorAlertRules" PRIMARY KEY,
                "Name" text NOT NULL DEFAULT '',
                "TenantId" uuid NULL,
                "Condition" integer NOT NULL DEFAULT 0,
                "Threshold" double precision NOT NULL DEFAULT 0,
                "Enabled" boolean NOT NULL DEFAULT true,
                "FireWebhook" boolean NOT NULL DEFAULT false,
                "CreatedAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_OperatorAlertRules_TenantId" ON "OperatorAlertRules" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "OperatorAlertsFired" (
                "Id" bigserial NOT NULL CONSTRAINT "PK_OperatorAlertsFired" PRIMARY KEY,
                "RuleId" uuid NOT NULL,
                "TenantId" uuid NULL,
                "RuleName" text NOT NULL DEFAULT '',
                "ActualValue" double precision NOT NULL DEFAULT 0,
                "Threshold" double precision NOT NULL DEFAULT 0,
                "FiredAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_OperatorAlertsFired_RuleId_FiredAtUtc"
            ON "OperatorAlertsFired" ("RuleId", "FiredAtUtc");
            """);

        // v1.6: FileAccessRequests, TenantPlans, AuditChain (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileAccessRequests" (
                "RequestId" uuid NOT NULL CONSTRAINT "PK_FileAccessRequests" PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "FileId" uuid NOT NULL,
                "RequesterEmail" text NOT NULL DEFAULT '',
                "Message" text NOT NULL DEFAULT '',
                "Status" integer NOT NULL DEFAULT 0,
                "ReviewedByAdminId" uuid NULL,
                "RequestedAtUtc" timestamptz NOT NULL,
                "ReviewedAtUtc" timestamptz NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileAccessRequests_TenantId_FileId"
            ON "FileAccessRequests" ("TenantId", "FileId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileAccessRequests_TenantId_Status"
            ON "FileAccessRequests" ("TenantId", "Status");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantPlans" (
                "TenantId" uuid NOT NULL CONSTRAINT "PK_TenantPlans" PRIMARY KEY,
                "Tier" integer NOT NULL DEFAULT 0,
                "MaxFiles" integer NULL,
                "MaxStorageMb" integer NULL,
                "UpdatedAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "AuditChain" (
                "AuditEventId" bigint NOT NULL CONSTRAINT "PK_AuditChain" PRIMARY KEY,
                "Hash" text NOT NULL DEFAULT '',
                "PrevHash" text NOT NULL DEFAULT ''
            );
            """);
        // v1.7: FileCollections, FileCollectionItems, TenantKeyRotationConfigs, KeyRotationHistory (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileCollections" (
                "CollectionId" uuid NOT NULL CONSTRAINT "PK_FileCollections" PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Name" text NOT NULL DEFAULT '',
                "Description" text NULL,
                "CreatedAtUtc" timestamptz NOT NULL,
                "UpdatedAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileCollections_TenantId" ON "FileCollections" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "FileCollectionItems" (
                "CollectionId" uuid NOT NULL,
                "FileId" uuid NOT NULL,
                "TenantId" uuid NOT NULL,
                "AddedAtUtc" timestamptz NOT NULL,
                CONSTRAINT "PK_FileCollectionItems" PRIMARY KEY ("CollectionId", "FileId")
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_FileCollectionItems_TenantId" ON "FileCollectionItems" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantKeyRotationConfigs" (
                "TenantId" uuid NOT NULL CONSTRAINT "PK_TenantKeyRotationConfigs" PRIMARY KEY,
                "Enabled" boolean NOT NULL DEFAULT FALSE,
                "IntervalDays" integer NOT NULL DEFAULT 90,
                "LastRotatedAtUtc" timestamptz NULL,
                "NextRotationDueUtc" timestamptz NULL,
                "UpdatedAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "KeyRotationHistory" (
                "Id" bigserial NOT NULL CONSTRAINT "PK_KeyRotationHistory" PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "FilesRotated" integer NOT NULL DEFAULT 0,
                "TriggeredBy" text NOT NULL DEFAULT 'schedule',
                "RotatedAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_KeyRotationHistory_TenantId" ON "KeyRotationHistory" ("TenantId");
            """);
        // v1.8: TenantRetentionPolicies (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantRetentionPolicies" (
                "TenantId" uuid NOT NULL CONSTRAINT "PK_TenantRetentionPolicies" PRIMARY KEY,
                "Enabled" boolean NOT NULL DEFAULT FALSE,
                "FileRetentionDays" integer NULL,
                "UpdatedAtUtc" timestamptz NOT NULL
            );
            """);

        // v1.9: TenantIpAllowlistRules, TenantDeviceTrustConfigs (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantIpAllowlistRules" (
                "RuleId" uuid NOT NULL CONSTRAINT "PK_TenantIpAllowlistRules" PRIMARY KEY,
                "TenantId" uuid NOT NULL,
                "Cidr" text NOT NULL DEFAULT '',
                "Label" text NOT NULL DEFAULT '',
                "CreatedAtUtc" timestamptz NOT NULL
            );
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE INDEX IF NOT EXISTS "IX_TenantIpAllowlistRules_TenantId" ON "TenantIpAllowlistRules" ("TenantId");
            """);
        dbContext.Database.ExecuteSqlRaw("""
            CREATE TABLE IF NOT EXISTS "TenantDeviceTrustConfigs" (
                "TenantId" uuid NOT NULL CONSTRAINT "PK_TenantDeviceTrustConfigs" PRIMARY KEY,
                "Enabled" boolean NOT NULL DEFAULT FALSE,
                "RequiredCheckinDays" integer NOT NULL DEFAULT 7,
                "UpdatedAtUtc" timestamptz NOT NULL
            );
            """);

        // v1.9: time-windowed grants — add columns to existing FileGrants table (Postgres)
        dbContext.Database.ExecuteSqlRaw("""
            ALTER TABLE "FileGrants" ADD COLUMN IF NOT EXISTS "ValidFromUtc" timestamptz NULL;
            """);
        dbContext.Database.ExecuteSqlRaw("""
            ALTER TABLE "FileGrants" ADD COLUMN IF NOT EXISTS "ValidUntilUtc" timestamptz NULL;
            """);
    }

    AdminIdentitySeed.Run(dbContext);
}

app.UseAdminIdentityAuthentication();
app.UseScimBearerAuthentication();
app.UseClientApiKeyAuthentication();
app.UseTenantHeaderContext();
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/me/");
        return;
    }

    if (context.Request.Path.Equals("/admin", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/admin/");
        return;
    }

    if (context.Request.Path.Equals("/share", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/share/");
        return;
    }

    if (context.Request.Path.Equals("/me", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/me/");
        return;
    }

    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
app.MapFilesEndpoints();
app.MapExternalShareEndpoints();
app.MapFileKeyEndpoints();
app.MapPolicyEndpoints();
app.MapAuditEndpoints();
app.MapAdminUsersEndpoints();
app.MapAdminGroupsEndpoints();
app.MapAdminDevicesEndpoints();
app.MapAdminFilesEndpoints();
app.MapAdminPolicyTemplatesEndpoints();
app.MapAdminWatermarkTemplatesEndpoints();
app.MapAdminPolicySimulatorEndpoints();
app.MapAdminAuditEndpoints();
app.MapAdminSiemEndpoints();
app.MapAdminExternalShareSettingsEndpoints();
app.MapAdminBruteForcePolicyEndpoints();
app.MapAgentDiscoverEndpoints();
app.MapAdminDirectorySyncEndpoints();
app.MapAdminBoxIntegrationEndpoints();
app.MapBoxWebhookEndpoints();
app.MapAdminOutlookIntegrationEndpoints();
app.MapOutlookAddInEndpoints();
app.MapAdminFileTagsEndpoints();
app.MapAdminLicenseEndpoints();
app.MapAdminTenantsEndpoints();
app.MapAdminTenantClientKeysEndpoints();
app.MapAdminTenantBillingWebhooksEndpoints();
app.MapAdminUsageEndpoints();
app.MapPublicRegistrationEndpoints();
app.MapAdminRegistrationsEndpoints();
app.MapAdminMetricsEndpoints();
app.MapAdminAlertEndpoints();
app.MapAdminIdentityEndpoints();
app.MapAdminFileZipEndpoints();
app.MapAdminTransparentFilesEndpoints();
app.MapAdminSecureContainersEndpoints();
app.MapAdminFolderWatcherEndpoints();
app.MapCompatibilityEndpoints();
app.MapPersonaEndpoints();
app.MapQuickShareEndpoints();
app.MapMeSharesEndpoints();
app.MapRecentRecipientsEndpoints();
app.MapAdminNotificationConfigEndpoints();
app.MapAdminAuditChainEndpoints();
app.MapAdminAccessRequestEndpoints();
app.MapAdminTenantPlanEndpoints();
app.MapAccessRequestEndpoints();
app.MapAdminFileCollectionEndpoints();
app.MapAdminBatchFileEndpoints();
app.MapAdminKeyRotationEndpoints();
app.MapAdminComplianceEndpoints();
app.MapAdminRetentionPolicyEndpoints();
app.MapAdminIpAllowlistEndpoints();
app.MapAdminDeviceTrustEndpoints();
app.MapScimEndpoints();
app.MapScimUsersEndpoints();
app.MapScimGroupsEndpoints();
app.MapAgentEndpoints();

app.Run();

public partial class Program;
