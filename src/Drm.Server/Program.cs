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

        if (openedHere)
        {
            connection.Close();
        }
    }
}

app.UseAdminApiKeyAuthentication();
app.UseScimBearerAuthentication();
app.UseClientApiKeyAuthentication();
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
app.MapAdminDirectorySyncEndpoints();
app.MapAdminBoxIntegrationEndpoints();
app.MapBoxWebhookEndpoints();
app.MapAdminOutlookIntegrationEndpoints();
app.MapOutlookAddInEndpoints();
app.MapAdminFileTagsEndpoints();
app.MapAdminLicenseEndpoints();
app.MapAdminFileZipEndpoints();
app.MapAdminTransparentFilesEndpoints();
app.MapAdminSecureContainersEndpoints();
app.MapAdminFolderWatcherEndpoints();
app.MapCompatibilityEndpoints();
app.MapPersonaEndpoints();
app.MapQuickShareEndpoints();
app.MapRecentRecipientsEndpoints();
app.MapAdminNotificationConfigEndpoints();
app.MapScimEndpoints();
app.MapScimUsersEndpoints();
app.MapScimGroupsEndpoints();
app.MapAgentEndpoints();

app.Run();

public partial class Program;
