using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class AdminTransparentFilesApiTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-trans-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public AdminTransparentFilesApiTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:TransparentTrailerSecret", "my-secret");
            });
    }

    [Fact]
    public async Task Admin_can_register_list_get_and_deregister_transparent_file()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();

        using var register = await client.PostAsJsonAsync("/api/admin/transparent-files", new
        {
            tenantId,
            fileId,
            ownerUserId,
            originalFileName = "quarterly-report.xlsx",
            contentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            policyTemplateId = (Guid?)null
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetFromJsonAsync<List<TransparentFileResponse>>(
            $"/api/admin/transparent-files?tenantId={tenantId}");
        list.Should().NotBeNull();
        list!.Single().FileId.Should().Be(fileId);
        list[0].OriginalFileName.Should().Be("quarterly-report.xlsx");

        var get = await client.GetFromJsonAsync<TransparentFileResponse>(
            $"/api/admin/transparent-files/{fileId}?tenantId={tenantId}");
        get!.OwnerUserId.Should().Be(ownerUserId);

        using var delete = await client.DeleteAsync(
            $"/api/admin/transparent-files/{fileId}?tenantId={tenantId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var listAfter = await client.GetFromJsonAsync<List<TransparentFileResponse>>(
            $"/api/admin/transparent-files?tenantId={tenantId}");
        listAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task Admin_register_returns_conflict_for_duplicate_file_id()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var body = new
        {
            tenantId,
            fileId,
            ownerUserId = Guid.NewGuid(),
            originalFileName = "x.docx",
            contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            policyTemplateId = (Guid?)null
        };
        using var first = await client.PostAsJsonAsync("/api/admin/transparent-files", body);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        using var second = await client.PostAsJsonAsync("/api/admin/transparent-files", body);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Admin_register_returns_bad_request_for_blank_identifiers()
    {
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/admin/transparent-files", new
        {
            tenantId = Guid.Empty,
            fileId = Guid.Empty,
            ownerUserId = Guid.Empty,
            originalFileName = "x.docx",
            contentType = "application/octet-stream",
            policyTemplateId = (Guid?)null
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Trailer_secret_endpoint_is_disabled_by_default()
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/admin/transparent-files/secret");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trailer_secret_endpoint_returns_secret_only_when_distribution_explicitly_allowed()
    {
        using var allowedFactory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={Path.Combine(Path.GetTempPath(), $"drm-trans-secret-{Guid.NewGuid():N}.db")}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                builder.UseSetting("Drm:Security:TransparentTrailerSecret", "my-secret");
                builder.UseSetting("Drm:Security:AllowTrailerSecretDistribution", "true");
            });
        using var client = allowedFactory.CreateClient();
        var response = await client.GetFromJsonAsync<SecretResponse>(
            "/api/admin/transparent-files/secret");
        response!.Secret.Should().Be("my-secret");
        allowedFactory.Dispose();
    }

    [Fact]
    public async Task Stamp_endpoint_returns_stamped_bytes_round_trips_via_verify()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var payload = System.Text.Encoding.UTF8.GetBytes("Hello DRM");

        var stampReq = new
        {
            tenantId,
            fileId = Guid.Empty,
            ownerUserId,
            originalFileName = "hello.txt",
            contentType = "text/plain",
            policyTemplateId = (Guid?)null,
            fileBytesBase64 = Convert.ToBase64String(payload)
        };
        var stampResp = await client.PostAsJsonAsync(
            "/api/admin/transparent-files/stamp", stampReq);
        stampResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var stamp = await stampResp.Content.ReadFromJsonAsync<StampResponseDto>();
        stamp!.FileId.Should().NotBe(Guid.Empty);
        stamp.StampedSizeBytes.Should().BeGreaterThan(payload.LongLength);
        stamp.TrailerSizeBytes.Should().BeGreaterThan(0);

        var verifyReq = new { fileBytesBase64 = stamp.StampedFileBytesBase64 };
        var verifyResp = await client.PostAsJsonAsync(
            "/api/admin/transparent-files/verify", verifyReq);
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verify = await verifyResp.Content.ReadFromJsonAsync<VerifyResponseDto>();
        verify!.Valid.Should().BeTrue();
        verify.TenantId.Should().Be(tenantId);
        verify.FileId.Should().Be(stamp.FileId);
        verify.OriginalLength.Should().Be(payload.Length);
    }

    [Fact]
    public async Task Verify_endpoint_reports_invalid_for_unstamped_bytes()
    {
        using var client = factory.CreateClient();
        var verifyReq = new { fileBytesBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("plain bytes")) };
        var verifyResp = await client.PostAsJsonAsync(
            "/api/admin/transparent-files/verify", verifyReq);
        verifyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var verify = await verifyResp.Content.ReadFromJsonAsync<VerifyResponseDto>();
        verify!.Valid.Should().BeFalse();
    }

    private sealed record StampResponseDto(Guid FileId, string StampedFileBytesBase64, long StampedSizeBytes, long TrailerSizeBytes);
    private sealed record VerifyResponseDto(bool Valid, Guid? TenantId, Guid? FileId, Guid? OwnerUserId, string? ContentType, string? OriginalFileName, DateTimeOffset? RegisteredAtUtc, Guid? PolicyTemplateId, int? OriginalLength);

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record TransparentFileResponse(
        Guid TenantId,
        Guid FileId,
        Guid OwnerUserId,
        string OriginalFileName,
        string ContentType,
        Guid? PolicyTemplateId,
        DateTimeOffset RegisteredAtUtc);

    private sealed record SecretResponse(string Secret);

    // ─── X-DRM-Tenant-Id header assertion (SECURITY.md migration) ─────────

    [Fact]
    public async Task Register_transparent_file_with_mismatched_header_returns_400()
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/transparent-files")
        {
            Content = JsonContent.Create(new
            {
                tenantId = Guid.NewGuid(),
                fileId = Guid.NewGuid(),
                ownerUserId = Guid.NewGuid(),
                originalFileName = "x.docx",
                contentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            })
        };
        request.Headers.Add("X-DRM-Tenant-Id", Guid.NewGuid().ToString());

        using var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<ErrorBody>();
        body!.ReasonCode.Should().Be("tenant_mismatch");
    }

    private sealed record ErrorBody(string ReasonCode);
}
