using Drm.Domain;

namespace Drm.Agent.Core;

public sealed class ProtectPdfFileWorkflow(
    IDrmServerClient serverClient,
    IProtectedFileInventory inventory,
    IFileKeyStore? fileKeyStore = null)
{
    public async Task<ProtectedPdfFileResult> ProtectAsync(
        TenantId tenantId,
        UserId ownerUserId,
        string sourcePath,
        byte[] fileKey,
        bool deleteOriginalAfterProtection,
        CancellationToken cancellationToken)
    {
        return await ProtectAsync(
            tenantId,
            ownerUserId,
            sourcePath,
            fileKey,
            ProtectPdfPolicyOptions.Default,
            deleteOriginalAfterProtection,
            cancellationToken);
    }

    public async Task<ProtectedPdfFileResult> ProtectAsync(
        TenantId tenantId,
        UserId ownerUserId,
        string sourcePath,
        byte[] fileKey,
        ProtectPdfPolicyOptions policyOptions,
        bool deleteOriginalAfterProtection,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(fileKey);
        ArgumentNullException.ThrowIfNull(policyOptions);

        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Source PDF was not found.", sourcePath);
        }

        if (!string.Equals(Path.GetExtension(sourcePath), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only PDF files can be protected by this workflow.");
        }

        var result = await new ProtectFileWorkflow(serverClient, inventory, fileKeyStore)
            .ProtectAsync(
                tenantId,
                ownerUserId,
                sourcePath,
                fileKey,
                new ProtectFilePolicyOptions(
                    policyOptions.Permissions,
                    policyOptions.PolicyTemplateId,
                    policyOptions.Recipients),
                deleteOriginalAfterProtection,
                cancellationToken);

        return new ProtectedPdfFileResult(
            result.TenantId,
            result.FileId,
            result.SourcePath,
            result.DestinationPath,
            result.OriginalDeleted);
    }
}

public sealed record ProtectedPdfFileResult(
    Guid TenantId,
    Guid FileId,
    string SourcePath,
    string DestinationPath,
    bool OriginalDeleted);

public sealed record ProtectPdfPolicyOptions(
    Permission Permissions,
    Guid? PolicyTemplateId,
    IReadOnlyList<ProtectionRecipient> Recipients)
{
    public static ProtectPdfPolicyOptions Default { get; } = new(
        Permission.View | Permission.Print,
        PolicyTemplateId: null,
        Recipients: []);
}
