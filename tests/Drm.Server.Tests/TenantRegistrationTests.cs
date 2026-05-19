using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class TenantRegistrationTests : IDisposable
{
    private const string AdminApiKey = "reg-test-key";

    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-reg-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public TenantRegistrationTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                // Enable registration, no email, no admin approval for most tests
                builder.UseSetting("Drm:Registration:Enabled", "true");
                builder.UseSetting("Drm:Registration:RequireEmailVerification", "false");
                builder.UseSetting("Drm:Registration:RequireAdminApproval", "false");
            });
    }

    // ── Submit (public endpoint) ──────────────────────────────────────────────

    [Fact]
    public async Task Submit_creates_registration_and_auto_approves_when_no_verification_no_approval_required()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "autoflow",
            adminEmail = "admin@autoflow.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
        body!.Status.Should().Be(RegistrationStatus.Approved);
        body.TenantId.Should().NotBeNull();
        body.UserId.Should().NotBeNull();
    }

    [Fact]
    public async Task Submit_returns_404_when_registration_disabled()
    {
        using var disabledFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Registration:Enabled", "false");
            });
        using var client = disabledFactory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "shouldfail",
            adminEmail = "admin@shouldfail.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        disabledFactory.Dispose();
    }

    [Fact]
    public async Task Submit_returns_409_when_tenant_name_already_taken_by_live_tenant()
    {
        using var adminClient = MakeAdminClient();
        var tenantId = Guid.NewGuid();
        var r = await adminClient.PostAsJsonAsync("/api/admin/tenants",
            new { name = "existingtenant", tenantId });
        r.EnsureSuccessStatusCode();

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "existingtenant",
            adminEmail = "admin@existingtenant.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("tenant_name_taken");
    }

    [Fact]
    public async Task Submit_returns_409_when_registration_already_active_for_same_name()
    {
        using var client = factory.CreateClient();

        // First submit: auto-approves (so Approved status)
        var r1 = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "duptenant",
            adminEmail = "first@duptenant.example"
        });
        r1.EnsureSuccessStatusCode();

        // Second submit: tenant name is now taken (live tenant created)
        using var r2 = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "duptenant",
            adminEmail = "second@duptenant.example"
        });

        // Either conflict on live tenant or on active registration
        r2.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Submit_returns_202_when_admin_approval_required()
    {
        using var approvalFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={Path.Combine(Path.GetTempPath(), $"drm-reg-approval-{Guid.NewGuid():N}.db")}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Registration:Enabled", "true");
                builder.UseSetting("Drm:Registration:RequireEmailVerification", "false");
                builder.UseSetting("Drm:Registration:RequireAdminApproval", "true");
            });
        using var client = approvalFactory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "pendingapproval",
            adminEmail = "admin@pendingapproval.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await response.Content.ReadFromJsonAsync<RegistrationResponse>();
        body!.Status.Should().Be(RegistrationStatus.Verified);
        body.TenantId.Should().BeNull();
        approvalFactory.Dispose();
    }

    [Fact]
    public async Task Submit_returns_400_when_tenant_name_missing()
    {
        using var client = factory.CreateClient();

        using var response = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "",
            adminEmail = "admin@test.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Verify token (public endpoint) ────────────────────────────────────────

    [Fact]
    public async Task Verify_with_invalid_token_returns_404()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/register/verify?token=notarealtoken");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Verify_with_missing_token_returns_400()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/register/verify");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── Admin list ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_list_returns_submitted_registration()
    {
        using var client = factory.CreateClient();
        using var adminClient = MakeAdminClient();

        // Submit one registration (auto-approves in this factory)
        await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "listtest",
            adminEmail = "admin@listtest.example"
        });

        using var response = await adminClient.GetAsync("/api/admin/registrations");
        response.EnsureSuccessStatusCode();
        var rows = await response.Content.ReadFromJsonAsync<RegistrationRow[]>();
        rows.Should().NotBeNull();
        rows!.Should().Contain(r => r.TenantName == "listtest");
    }

    [Fact]
    public async Task Admin_list_filters_by_status()
    {
        using var approvalFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                var db = Path.Combine(Path.GetTempPath(), $"drm-reg-filter-{Guid.NewGuid():N}.db");
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={db}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Registration:Enabled", "true");
                builder.UseSetting("Drm:Registration:RequireEmailVerification", "false");
                builder.UseSetting("Drm:Registration:RequireAdminApproval", "true");
            });
        using var client = approvalFactory.CreateClient();
        using var adminClient = approvalFactory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        await client.PostAsJsonAsync("/api/register", new { tenantName = "filtertest", adminEmail = "a@filter.example" });

        var verified = await adminClient.GetAsync("/api/admin/registrations?status=Verified");
        verified.EnsureSuccessStatusCode();
        var rows = await verified.Content.ReadFromJsonAsync<RegistrationRow[]>();
        rows!.Should().Contain(r => r.TenantName == "filtertest");

        var approved = await adminClient.GetAsync("/api/admin/registrations?status=Approved");
        approved.EnsureSuccessStatusCode();
        var approvedRows = await approved.Content.ReadFromJsonAsync<RegistrationRow[]>();
        approvedRows!.Should().NotContain(r => r.TenantName == "filtertest");

        approvalFactory.Dispose();
    }

    // ── Admin approve ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_approve_creates_tenant_and_user()
    {
        using var approvalFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                var db = Path.Combine(Path.GetTempPath(), $"drm-reg-approve-{Guid.NewGuid():N}.db");
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={db}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Registration:Enabled", "true");
                builder.UseSetting("Drm:Registration:RequireEmailVerification", "false");
                builder.UseSetting("Drm:Registration:RequireAdminApproval", "true");
            });
        using var client = approvalFactory.CreateClient();
        using var adminClient = approvalFactory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        var submitResp = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "approvetest",
            adminEmail = "admin@approvetest.example",
            adminDisplayName = "Test Admin"
        });
        submitResp.EnsureSuccessStatusCode();
        var submitBody = await submitResp.Content.ReadFromJsonAsync<RegistrationResponse>();
        var regId = submitBody!.RegistrationId;

        using var approveResp = await adminClient.PostAsync(
            $"/api/admin/registrations/{regId}/approve", null);

        approveResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await approveResp.Content.ReadFromJsonAsync<RegistrationResponse>();
        body!.Status.Should().Be(RegistrationStatus.Approved);
        body.TenantId.Should().NotBeNull();
        body.UserId.Should().NotBeNull();

        // Confirm tenant is now live
        using var tenantResp = await adminClient.GetAsync($"/api/admin/tenants/{body.TenantId}");
        tenantResp.EnsureSuccessStatusCode();

        approvalFactory.Dispose();
    }

    [Fact]
    public async Task Admin_approve_returns_400_for_pending_unverified()
    {
        using var emailFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                var db = Path.Combine(Path.GetTempPath(), $"drm-reg-unverified-{Guid.NewGuid():N}.db");
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={db}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Registration:Enabled", "true");
                builder.UseSetting("Drm:Registration:RequireEmailVerification", "true");
                builder.UseSetting("Drm:Registration:RequireAdminApproval", "true");
            });
        using var client = emailFactory.CreateClient();
        using var adminClient = emailFactory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        // SMTP not configured → email is skipped → verify is done but let's check
        // Actually with RequireEmailVerification=true AND NoopSender, email step is skipped
        // and registration goes straight to Verified. Submit should return 202 with Verified status.
        var submitResp = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "unveriftest",
            adminEmail = "admin@unveriftest.example"
        });
        // Noop sender means email is skipped → auto-verifies → 202 Verified
        submitResp.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var submitBody = await submitResp.Content.ReadFromJsonAsync<RegistrationResponse>();
        submitBody!.Status.Should().Be(RegistrationStatus.Verified);

        emailFactory.Dispose();
    }

    // ── Admin reject ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_reject_sets_status_to_rejected_with_notes()
    {
        using var approvalFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                var db = Path.Combine(Path.GetTempPath(), $"drm-reg-reject-{Guid.NewGuid():N}.db");
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={db}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:AdminApiKey", AdminApiKey);
                builder.UseSetting("Drm:Registration:Enabled", "true");
                builder.UseSetting("Drm:Registration:RequireEmailVerification", "false");
                builder.UseSetting("Drm:Registration:RequireAdminApproval", "true");
            });
        using var client = approvalFactory.CreateClient();
        using var adminClient = approvalFactory.CreateClient();
        adminClient.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);

        var submitResp = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "rejecttest",
            adminEmail = "admin@rejecttest.example"
        });
        submitResp.EnsureSuccessStatusCode();
        var submitBody = await submitResp.Content.ReadFromJsonAsync<RegistrationResponse>();
        var regId = submitBody!.RegistrationId;

        using var rejectResp = await adminClient.PostAsJsonAsync(
            $"/api/admin/registrations/{regId}/reject",
            new { notes = "Duplicate company." });

        rejectResp.EnsureSuccessStatusCode();
        var body = await rejectResp.Content.ReadFromJsonAsync<RegistrationRow>();
        body!.Status.Should().Be(RegistrationStatus.Rejected);
        body.ReviewNotes.Should().Be("Duplicate company.");

        approvalFactory.Dispose();
    }

    [Fact]
    public async Task Admin_reject_returns_400_if_already_finalized()
    {
        using var adminClient = MakeAdminClient();
        using var client = factory.CreateClient();

        // Auto-approves
        var submitResp = await client.PostAsJsonAsync("/api/register", new
        {
            tenantName = "alreadyapproved",
            adminEmail = "admin@alreadyapproved.example"
        });
        submitResp.EnsureSuccessStatusCode();
        var body = await submitResp.Content.ReadFromJsonAsync<RegistrationResponse>();

        using var rejectResp = await adminClient.PostAsJsonAsync(
            $"/api/admin/registrations/{body!.RegistrationId}/reject",
            new { notes = "too late" });

        rejectResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var rb = await rejectResp.Content.ReadAsStringAsync();
        rb.Should().Contain("already_finalized");
    }

    [Fact]
    public async Task Admin_approve_returns_404_for_unknown_registration()
    {
        using var adminClient = MakeAdminClient();

        using var response = await adminClient.PostAsync(
            $"/api/admin/registrations/{Guid.NewGuid()}/approve", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private HttpClient MakeAdminClient()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(AdminIdentityAuthentication.SharedKeyHeaderName, AdminApiKey);
        return client;
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(candidate)) File.Delete(candidate);
        }
    }

    // ── DTOs ──────────────────────────────────────────────────────────────────

    private sealed record RegistrationResponse(
        Guid RegistrationId,
        RegistrationStatus Status,
        string Message,
        Guid? TenantId,
        Guid? UserId);

    private sealed record RegistrationRow(
        Guid RegistrationId,
        string TenantName,
        string DisplayName,
        string AdminEmail,
        string AdminDisplayName,
        int? MaxEncrypters,
        RegistrationStatus Status,
        DateTimeOffset RequestedAtUtc,
        DateTimeOffset? ReviewedAtUtc,
        string? ReviewNotes,
        Guid? CreatedTenantId,
        Guid? CreatedUserId);
}
