using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Drm.Integration.Tests;

public sealed class ServerIntegratedWorkflowTests
{
    [Fact]
    public async Task Agent_core_can_protect_and_open_against_management_server()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"drm-integrated-{Guid.NewGuid():N}.db");
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={dbPath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
            });
        var httpClient = factory.CreateClient();
        var serverClient = new DrmServerClient(httpClient);
        var tenantId = TenantId.New();
        var userId = UserId.New();
        var deviceId = DeviceId.New();
        var fileKey = EnvelopeCrypto.GenerateKey();
        var pdfBytes = "%PDF-1.7 smoke"u8.ToArray();

        var protectedBytes = await new ProtectPdfWorkflow(serverClient)
            .ProtectAsync(tenantId, userId, pdfBytes, fileKey, CancellationToken.None);

        var opened = await new OpenProtectedPdfWorkflow(serverClient)
            .OpenAsync(protectedBytes, userId, deviceId, fileKey, CancellationToken.None);

        opened.Content.Should().Equal(pdfBytes);
        opened.Permissions.Should().HaveFlag(Permission.View);
    }
}
