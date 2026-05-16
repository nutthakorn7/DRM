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

        if (openedHere)
        {
            connection.Close();
        }
    }
}

app.UseAdminApiKeyAuthentication();
app.UseClientApiKeyAuthentication();
app.Use(async (context, next) =>
{
    if (context.Request.Path.Equals("/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.Redirect("/share/");
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
app.MapAdminNotificationConfigEndpoints();
app.MapAgentEndpoints();

app.Run();

public partial class Program;
