using Drm.Agent.Core;
using Drm.Agent.Service.Windows;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DRM Agent";
});
builder.Services.Configure<AgentServiceOptions>(builder.Configuration.GetSection("DrmAgent"));
builder.Services.AddHttpClient<IDrmServerClient, DrmServerClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
    if (!Uri.TryCreate(options.ServerUrl, UriKind.Absolute, out var serverUri))
    {
        throw new InvalidOperationException("DrmAgent:ServerUrl must be an absolute URI.");
    }

    client.BaseAddress = serverUri;
});
builder.Services.AddSingleton<IAgentAuditQueue>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
    return new AgentAuditQueue(
        options.ResolveAuditQueuePath(),
        serviceProvider.GetRequiredService<IDrmServerClient>());
});
builder.Services.AddSingleton<IProtectedFileInventory>(serviceProvider =>
{
    var options = serviceProvider.GetRequiredService<IOptions<AgentServiceOptions>>().Value;
    return new JsonProtectedFileInventory(options.ResolveInventoryPath());
});
builder.Services.AddSingleton<AgentHeartbeatWorkflow>();
builder.Services.AddSingleton<AgentCommandProcessor>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
