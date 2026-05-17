using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Server.Tests;

public sealed class PersonaEndpointsTests : IDisposable
{
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-persona-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public PersonaEndpointsTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(b => {
                b.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                b.UseSetting("Drm:Mode", "OnPrem");
            });
    }

    [Fact]
    public async Task Get_persona_returns_employee_default_for_unassigned_user()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var resp = await client.GetFromJsonAsync<PersonaResponse>(
            $"/api/me/persona?tenantId={tenantId}&userId={userId}");
        resp.Should().NotBeNull();
        resp!.Persona.Should().Be("Employee");
        resp.CanProtect.Should().BeTrue();
        resp.CanRevoke.Should().BeFalse();
        resp.CanInviteGuests.Should().BeTrue();
        resp.CanViewAuditLog.Should().BeFalse();
        resp.CanAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task Admin_can_assign_persona_and_capabilities_reflect_role()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        using var put = await client.PutAsJsonAsync(
            $"/api/admin/personas/{userId}",
            new { tenantId, persona = "Admin" });
        put.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        var resp = await client.GetFromJsonAsync<PersonaResponse>(
            $"/api/me/persona?tenantId={tenantId}&userId={userId}");
        resp!.Persona.Should().Be("Admin");
        resp.CanAdmin.Should().BeTrue();
        resp.CanRevoke.Should().BeTrue();
        resp.CanViewAuditLog.Should().BeTrue();
    }

    [Fact]
    public async Task Admin_assign_persona_rejects_unknown_persona_value()
    {
        using var client = factory.CreateClient();
        using var put = await client.PutAsJsonAsync(
            $"/api/admin/personas/{Guid.NewGuid()}",
            new { tenantId = Guid.NewGuid(), persona = "Nonexistent" });
        put.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var c in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(c)) File.Delete(c);
    }

    private sealed record PersonaResponse(
        Guid TenantId, Guid UserId, string Persona,
        bool CanProtect, bool CanRevoke, bool CanInviteGuests,
        bool CanViewAuditLog, bool CanAdmin,
        DateTimeOffset? AssignedAtUtc);
}
