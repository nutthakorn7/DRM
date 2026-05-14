using Drm.Agent.Service.Windows;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DRM Agent";
});
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
