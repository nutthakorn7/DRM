using System.Net.Http.Json;
using System.Text;
using Drm.Crypto;
using Microsoft.Extensions.Options;

namespace Drm.FolderWatcher.Service;

public sealed class FolderProtector
{
    private readonly IOptionsMonitor<FolderWatcherOptions> options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ILogger<FolderProtector> logger;
    private readonly FolderProtectionTracker tracker;

    public FolderProtector(
        IOptionsMonitor<FolderWatcherOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<FolderProtector> logger,
        FolderProtectionTracker tracker)
    {
        this.options = options;
        this.httpClientFactory = httpClientFactory;
        this.logger = logger;
        this.tracker = tracker;
    }

    public async Task ProtectAsync(string folderPath, string fullPath, CancellationToken cancellationToken)
    {
        var opt = options.CurrentValue;

        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                return;
            }
            if (!tracker.ShouldProcess(fullPath, info.Length, info.LastWriteTimeUtc))
            {
                return;
            }

            var bytes = await ReadFileWithRetryAsync(fullPath, cancellationToken);
            if (bytes is null)
            {
                await PostEventAsync(opt, folderPath, info.Name, info.Length, "locked", null, cancellationToken);
                return;
            }

            // If the file already has a transparent trailer, skip.
            var hmacKey = Encoding.UTF8.GetBytes(opt.TrailerSecret);
            if (TransparentEnvelope.TryReadTrailer(bytes, hmacKey, out _, out _))
            {
                tracker.RecordProtected(fullPath, info.Length, info.LastWriteTimeUtc);
                await PostEventAsync(opt, folderPath, info.Name, info.Length, "already_protected", null, cancellationToken);
                return;
            }

            var fileId = Guid.NewGuid();
            var contentType = GuessContentType(info.Extension);
            var metadata = new TransparentMetadata(
                opt.TenantId, fileId, opt.OwnerUserId, contentType, info.Name,
                DateTimeOffset.UtcNow, null);

            var stamped = TransparentEnvelope.AppendTrailer(bytes, metadata, hmacKey);
            await File.WriteAllBytesAsync(fullPath, stamped, cancellationToken);
            var updated = new FileInfo(fullPath);
            tracker.RecordProtected(fullPath, updated.Length, updated.LastWriteTimeUtc);

            await RegisterTransparentAsync(opt, fileId, info.Name, contentType, cancellationToken);
            await PostEventAsync(opt, folderPath, info.Name, updated.Length, "protected", fileId, cancellationToken);
            logger.LogInformation("Protected {File} ({Bytes} bytes) as {FileId}", fullPath, updated.Length, fileId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to protect {File}", fullPath);
            await PostEventAsync(opt, folderPath, Path.GetFileName(fullPath), 0, "error", null, cancellationToken);
        }
    }

    private static async Task<byte[]?> ReadFileWithRetryAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path, cancellationToken);
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), cancellationToken);
            }
        }
        return null;
    }

    private async Task RegisterTransparentAsync(
        FolderWatcherOptions opt, Guid fileId, string fileName, string contentType, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(opt);
            using var response = await client.PostAsJsonAsync(
                "/api/admin/transparent-files",
                new
                {
                    tenantId = opt.TenantId,
                    fileId,
                    ownerUserId = opt.OwnerUserId,
                    originalFileName = fileName,
                    contentType,
                    policyTemplateId = (Guid?)null
                },
                cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Failed to register transparent file {FileId}: HTTP {Status}", fileId, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to register transparent file {FileId}", fileId);
        }
    }

    private async Task PostEventAsync(
        FolderWatcherOptions opt, string folderPath, string fileName, long fileSize,
        string status, Guid? fileId, CancellationToken cancellationToken)
    {
        try
        {
            using var client = CreateClient(opt);
            await client.PostAsJsonAsync(
                "/api/admin/folder-watcher/events",
                new
                {
                    tenantId = opt.TenantId,
                    hostname = Environment.MachineName,
                    folderPath,
                    fileName,
                    fileSize,
                    status,
                    fileId
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to post folder-watcher event");
        }
    }

    private HttpClient CreateClient(FolderWatcherOptions opt)
    {
        var client = httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(opt.ServerUrl);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-DRM-Admin-Key", opt.AdminApiKey);
        return client;
    }

    private static string GuessContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };
}
