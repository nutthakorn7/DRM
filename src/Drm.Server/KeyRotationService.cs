using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Drm.Server;

public static class KeyRotationService
{
    public static async Task<int> RotateTenantKeysAsync(
        AppDbContext dbContext, Guid tenantId, string triggeredBy, CancellationToken ct)
    {
        var keys = await dbContext.FileKeys
            .Where(k => k.TenantId == tenantId)
            .ToListAsync(ct);

        var now = DateTimeOffset.UtcNow;
        foreach (var key in keys)
        {
            var nonce = new byte[12];
            var ciphertext = new byte[32];
            var tag = new byte[16];
            RandomNumberGenerator.Fill(nonce);
            RandomNumberGenerator.Fill(ciphertext);
            RandomNumberGenerator.Fill(tag);
            key.WrappedKeyNonceBase64 = Convert.ToBase64String(nonce);
            key.WrappedKeyCiphertextBase64 = Convert.ToBase64String(ciphertext);
            key.WrappedKeyTagBase64 = Convert.ToBase64String(tag);
            key.UpdatedAtUtc = now;
        }

        var config = await dbContext.TenantKeyRotationConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId, ct);
        if (config is not null)
        {
            config.LastRotatedAtUtc = now;
            config.NextRotationDueUtc = now.AddDays(config.IntervalDays);
        }

        dbContext.KeyRotationHistory.Add(new KeyRotationHistoryEntity
        {
            TenantId = tenantId,
            FilesRotated = keys.Count,
            TriggeredBy = triggeredBy,
            RotatedAtUtc = now
        });

        await dbContext.SaveChangesAsync(ct);
        return keys.Count;
    }
}
