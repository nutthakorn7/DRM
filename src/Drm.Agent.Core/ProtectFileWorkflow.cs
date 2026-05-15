using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class ProtectFileWorkflow(
    IDrmServerClient serverClient,
    IProtectedFileInventory inventory,
    IFileKeyStore? fileKeyStore = null)
{
    private const string ProtectedExtension = ".drmx";

    private static readonly IReadOnlyDictionary<string, string> ContentTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".zip"] = "application/zip",
            [".dwg"] = "image/vnd.dwg",
            [".dxf"] = "image/vnd.dxf",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv"
        };

    public async Task<ProtectedFileResult> ProtectAsync(
        TenantId tenantId,
        UserId ownerUserId,
        string sourcePath,
        byte[] fileKey,
        ProtectFilePolicyOptions policyOptions,
        bool deleteOriginalAfterProtection,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(policyOptions);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source file was not found.", sourcePath);
        }

        var contentType = InferContentType(sourcePath);
        var payload = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        var fileId = ProtectedFileId.New();
        var destinationPath = $"{sourcePath}{ProtectedExtension}";
        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await serverClient.RegisterFileAsync(
                new ProtectedFileRegistration(
                    tenantId.Value,
                    fileId.Value,
                    ownerUserId.Value,
                    contentType,
                    DateTimeOffset.UtcNow.AddDays(7),
                    policyOptions.Permissions,
                    policyOptions.PolicyTemplateId,
                    policyOptions.Recipients),
                cancellationToken);

            await serverClient.WrapFileKeyAsync(
                tenantId.Value,
                fileId.Value,
                fileKey,
                cancellationToken);

            await using (var tempStream = File.Create(tempPath))
            {
                ProtectedFileWriter.Write(tempStream, tenantId, fileId, contentType, fileKey, payload);
            }

            VerifyProtectedOutput(tempPath, tenantId.Value, fileId.Value, contentType);
            File.Move(tempPath, destinationPath, overwrite: false);

            if (fileKeyStore is not null)
            {
                await fileKeyStore.SaveAsync(tenantId.Value, fileId.Value, fileKey, cancellationToken);
            }

            await inventory.UpsertAsync(
                new ProtectedFileInventoryEntry(
                    tenantId.Value,
                    fileId.Value,
                    destinationPath,
                    DateTimeOffset.UtcNow),
                cancellationToken);

            var originalDeleted = false;
            if (deleteOriginalAfterProtection)
            {
                File.Delete(sourcePath);
                originalDeleted = true;
            }

            return new ProtectedFileResult(
                tenantId.Value,
                fileId.Value,
                sourcePath,
                destinationPath,
                contentType,
                originalDeleted);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static string InferContentType(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        return ContentTypes.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
    }

    private static void VerifyProtectedOutput(string path, Guid tenantId, Guid fileId, string contentType)
    {
        using var stream = File.OpenRead(path);
        var package = ProtectedFileReader.Read(stream);
        if (package.Header.TenantId != tenantId ||
            package.Header.FileId != fileId ||
            !string.Equals(package.Header.ContentType, contentType, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Protected file header does not match the registered file.");
        }
    }
}

public sealed record ProtectedFileResult(
    Guid TenantId,
    Guid FileId,
    string SourcePath,
    string DestinationPath,
    string ContentType,
    bool OriginalDeleted);

public sealed record ProtectFilePolicyOptions(
    Permission Permissions,
    Guid? PolicyTemplateId,
    IReadOnlyList<ProtectionRecipient> Recipients)
{
    public static ProtectFilePolicyOptions Default { get; } = new(
        Permission.View | Permission.Print,
        PolicyTemplateId: null,
        Recipients: []);
}
