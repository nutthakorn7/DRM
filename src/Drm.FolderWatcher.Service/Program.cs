using Drm.FolderWatcher.Service;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "DRM Folder Watcher";
});
builder.Services.AddHttpClient();
builder.Services.Configure<FolderWatcherOptions>(builder.Configuration.GetSection("FolderWatcher"));
builder.Services.AddSingleton<FolderProtectionTracker>();
builder.Services.AddSingleton<FolderProtector>();
builder.Services.AddHostedService<FolderWatcherWorker>();

var host = builder.Build();
host.Run();
