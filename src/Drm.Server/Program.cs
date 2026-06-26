using Drm.Server;
using Drm.Server.Endpoints;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

_ = builder.Configuration.GetValue<ServerMode>("Drm:Mode");

var connectionString = builder.Configuration.GetConnectionString("DrmDb")
    ?? "Data Source=drm-server.db";

var auditChainKey = builder.Configuration["Drm:Security:AuditChainKey"] ?? string.Empty;
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

    // Every audit-event insert gets a tamper-evident hash-chain row (GET /audit/chain/verify).
    options.AddInterceptors(new AuditChainInterceptor(auditChainKey));
});
builder.Services
    .AddHttpClient<ISiemEventSink, HttpSiemEventSink>()
    .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<ISiemDispatcher, SiemDispatcher>();
builder.Services.AddScoped<PolicyDecisionService>();
builder.Services.AddScoped<BruteForceProtectionService>();
// PR #50 hardening: replay protection for signed device requests. Singleton
// because it holds the in-memory nonce ledger across requests.
builder.Services.AddSingleton<IDeviceReplayGuard, InMemoryDeviceReplayGuard>();
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
    DatabaseInitializer.Initialize(dbContext, auditChainKey, app.Logger);
}

// Behind Caddy (TLS terminator) the app sees plain http on the internal network,
// so generated absolute URLs (share links, verification emails) came out http://.
// Honor Caddy's X-Forwarded-Proto so Request.Scheme reflects the real https edge.
// The proxy is the only ingress (app isn't exposed directly), so trust forwarders.
var forwardedOptions = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedProto };
forwardedOptions.KnownIPNetworks.Clear();
forwardedOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedOptions);

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
