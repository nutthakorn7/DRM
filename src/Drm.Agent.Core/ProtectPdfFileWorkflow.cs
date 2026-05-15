using Drm.Container;
using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class ProtectPdfFileWorkflow(
    IDrmServerClient serverClient,
    IProtectedFileInventory inventory,
    IFileKeyStore? fileKeyStore = null)
{
    private const string PdfContentType = "application/pdf";
    private const string ProtectedExtension = ".drmx";

    public async Task<ProtectedPdfFileResult> ProtectAsync(
        TenantId tenantId,
        UserId ownerUserId,
        string sourcePath,
        byte[] fileKey,
        bool deleteOriginalAfterProtection,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(fileKey);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source PDF was not found.", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only PDF files can be protected by this workflow.");
        }

        var pdfBytes = await File.ReadAllBytesAsync(sourcePath, cancellationToken);
        var fileId = ProtectedFileId.New();
        var destinationPath = $"{sourcePath}{ProtectedExtension}";
        var tempPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";

        try
        {
            await serverClient.RegisterFileAsync(
                tenantId.Value,
                fileId.Value,
                ownerUserId.Value,
                PdfContentType,
                DateTimeOffset.UtcNow.AddDays(7),
                Permission.View | Permission.Print,
                cancellationToken);

            await serverClient.WrapFileKeyAsync(
                tenantId.Value,
                fileId.Value,
                fileKey,
                cancellationToken);

            await using (var tempStream = File.Create(tempPath))
            {
                ProtectedFileWriter.Write(tempStream, tenantId, fileId, PdfContentType, fileKey, pdfBytes);
            }

            VerifyProtectedOutput(tempPath, tenantId.Value, fileId.Value);
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

            return new ProtectedPdfFileResult(
                tenantId.Value,
                fileId.Value,
                sourcePath,
                destinationPath,
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

    private static void VerifyProtectedOutput(string path, Guid tenantId, Guid fileId)
    {
        using var stream = File.OpenRead(path);
        var package = ProtectedFileReader.Read(stream);
        if (package.Header.TenantId != tenantId || package.Header.FileId != fileId)
        {
            throw new InvalidDataException("Protected file header does not match the registered file.");
        }
    }
}

public sealed record ProtectedPdfFileResult(
    Guid TenantId,
    Guid FileId,
    string SourcePath,
    string DestinationPath,
    bool OriginalDeleted);
