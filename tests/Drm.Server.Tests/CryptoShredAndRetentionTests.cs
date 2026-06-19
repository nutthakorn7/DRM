using System.Net;
using System.Net.Http.Json;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Drm.Server.Tests;

/// <summary>
/// Regression tests for the two CRUD-audit decisions (2026-06-12):
///   Decision 2 (ii) — revoke / retention CRYPTO-SHRED the wrapped key + clean up
///                     orphans (the Playwright suite can't see server-side deletes).
///   Decision 1 (B)  — audit retention DEPERSONALIZES events and the tamper-evident
///                     hash chain still verifies afterward.
/// </summary>
public sealed class CryptoShredAndRetentionTests : IDisposable
{
    private const string ChainKeyHex = "abcdef0123456789";
    private readonly string databasePath = Path.Combine(Path.GetTempPath(), $"drm-cryptoshred-{Guid.NewGuid():N}.db");
    private readonly WebApplicationFactory<Program> factory;

    public CryptoShredAndRetentionTests()
    {
        factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("ConnectionStrings:DrmDb", $"Data Source={databasePath}");
                builder.UseSetting("Drm:Mode", "OnPrem");
                // Same key the AuditChainInterceptor uses to build the chain, so verify can re-check it.
                builder.UseSetting("Drm:Security:AuditChainKey", ChainKeyHex);
                builder.UseSetting("Drm:KeyWrapping:MasterKeyBase64",
                    Convert.ToBase64String(Enumerable.Range(0, 32).Select(v => (byte)v).ToArray()));
            });
    }

    [Fact]
    public async Task Revoking_a_file_destroys_the_wrapped_key()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var fileKey = EnvelopeCrypto.GenerateKey();

        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);
        await WrapKeyAsync(client, tenantId, fileId, fileKey);

        // Sanity: the key is unwrappable before revoke.
        (await UnwrapStatusAsync(client, tenantId, fileId, ownerUserId)).Should().Be(HttpStatusCode.OK);

        using (var revoke = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/revoke", new { tenantId, adminUserId = Guid.NewGuid() }))
        {
            revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // The wrapped key ROW is gone from the live DB — not merely flagged.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.FileKeys.AnyAsync(k => k.TenantId == tenantId && k.FileId == fileId))
                .Should().BeFalse("revoke crypto-shreds the wrapped key, it isn't just marked revoked");
        }

        // And access is denied (revoked check precedes the now-absent key load).
        (await UnwrapStatusAsync(client, tenantId, fileId, ownerUserId)).Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Re_revoking_after_the_key_is_gone_is_safe()
    {
        using var client = factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var ownerUserId = Guid.NewGuid();
        var fileId = Guid.NewGuid();

        await RegisterFileAsync(client, tenantId, ownerUserId, fileId);
        await WrapKeyAsync(client, tenantId, fileId, EnvelopeCrypto.GenerateKey());

        using (var first = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/revoke", new { tenantId, adminUserId = Guid.NewGuid() }))
            first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second revoke must not blow up on the already-deleted key (null-safe).
        using var second = await client.PostAsJsonAsync(
            $"/api/admin/files/{fileId}/revoke", new { tenantId, adminUserId = Guid.NewGuid() });
        second.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Retention_purge_removes_the_file_and_all_its_child_rows()
    {
        var tenantId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var collectionId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProtectedFiles.Add(new ProtectedFileEntity
            {
                Id = fileId, TenantId = tenantId, OwnerUserId = Guid.NewGuid(),
                ContentType = "application/pdf", ExpiresAtUtc = now.AddDays(-30),
                Permissions = Permission.View, WatermarkTemplate = "u",
            });
            db.FileKeys.Add(new FileKeyEntity
            {
                TenantId = tenantId, FileId = fileId,
                WrappedKeyNonceBase64 = "n", WrappedKeyCiphertextBase64 = "c", WrappedKeyTagBase64 = "t",
                CreatedAtUtc = now, UpdatedAtUtc = now,
            });
            db.FileGrants.Add(new FileGrantEntity
            {
                TenantId = tenantId, FileId = fileId, SubjectType = "User", SubjectId = Guid.NewGuid(),
                Permissions = "View", CreatedAtUtc = now,
            });
            db.FileTags.Add(new FileTagEntity { TenantId = tenantId, FileId = fileId, Tag = "hr", AssignedAtUtc = now });
            db.FileAccessCounts.Add(new FileAccessCountEntity
            {
                TenantId = tenantId, FileId = fileId, UserId = Guid.NewGuid(),
                OpensUsed = 1, FirstOpenedAtUtc = now, LastOpenedAtUtc = now,
            });
            db.FileCollectionItems.Add(new FileCollectionItemEntity
            {
                TenantId = tenantId, CollectionId = collectionId, FileId = fileId,
            });
            await db.SaveChangesAsync();

            var (deleted, candidates) = await DataRetentionService.ApplyTenantRetentionAsync(
                db, tenantId, retentionDays: 1, dryRun: false, CancellationToken.None);
            deleted.Should().Be(1);
            candidates.Should().Be(1);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.ProtectedFiles.AnyAsync(f => f.Id == fileId)).Should().BeFalse();
            (await db.FileKeys.AnyAsync(k => k.FileId == fileId)).Should().BeFalse("retention must crypto-shred the key");
            (await db.FileGrants.AnyAsync(g => g.FileId == fileId)).Should().BeFalse("no orphan grants");
            (await db.FileTags.AnyAsync(t => t.FileId == fileId)).Should().BeFalse("no orphan tags");
            (await db.FileAccessCounts.AnyAsync(a => a.FileId == fileId)).Should().BeFalse("no orphan access counts");
            (await db.FileCollectionItems.AnyAsync(i => i.FileId == fileId)).Should().BeFalse("no orphan collection items");
        }
    }

    [Fact]
    public async Task Audit_chain_built_by_interceptor_still_verifies_after_depersonalization()
    {
        var tenantId = Guid.NewGuid();
        var past = DateTimeOffset.UtcNow.AddDays(-30);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Three events carrying personal data. The interceptor chains each on save — no manual
            // AppendAsync, so this proves the chain is actually wired on the real write path.
            for (var i = 0; i < 3; i++)
            {
                db.AuditEvents.Add(new AuditEventEntity
                {
                    TenantId = tenantId,
                    FileId = Guid.NewGuid(),
                    UserId = Guid.NewGuid(),
                    ActorAdminId = Guid.NewGuid(),
                    EventType = "file_revoked",
                    ReasonCode = "revoked",
                    CreatedAtUtc = past.AddMinutes(i),
                });
                await db.SaveChangesAsync();
            }

            (await AuditChainService.VerifyTenantChainAsync(db, tenantId, ChainKeyHex)).Valid
                .Should().BeTrue("the interceptor builds a verifiable chain on every save");

            var scrubbed = await AuditRetentionService.DepersonalizeAsync(
                db, DateTimeOffset.UtcNow, CancellationToken.None);
            scrubbed.Should().Be(3);
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // The load-bearing property: personal data gone, chain still verifies.
            var events = await db.AuditEvents.Where(e => e.TenantId == tenantId).ToListAsync();
            events.Should().OnlyContain(e => e.UserId == null && e.FileId == null
                && e.ActorAdminId == null && e.ReasonCode == "");

            (await AuditChainService.VerifyTenantChainAsync(db, tenantId, ChainKeyHex)).Valid
                .Should().BeTrue("depersonalization nulls only non-hashed fields, so the chain stays valid");
        }
    }

    [Fact]
    public async Task Audit_chain_verify_survives_microsecond_timestamp_truncation()
    {
        // Postgres truncates timestamptz to microseconds and EF does not refresh after insert.
        // The chain hash folds the timestamp in as Unix-ms, which is invariant under that truncation.
        // Emulate the round-trip on SQLite: chain an event, drop its sub-microsecond digit, re-verify.
        var tenantId = Guid.NewGuid();
        var stamp = new DateTimeOffset(2026, 6, 19, 12, 0, 0, TimeSpan.Zero).AddTicks(9_999_999); // .9999999s

        long eventId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = new AuditEventEntity
            {
                TenantId = tenantId, EventType = "file_revoked", ReasonCode = "revoked", CreatedAtUtc = stamp,
            };
            db.AuditEvents.Add(evt);
            await db.SaveChangesAsync(); // interceptor chains it from the in-memory 100ns-precision stamp
            eventId = evt.Id;

            (await AuditChainService.VerifyTenantChainAsync(db, tenantId, ChainKeyHex)).Valid.Should().BeTrue();
        }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var evt = await db.AuditEvents.SingleAsync(e => e.Id == eventId);
            var truncatedTicks = evt.CreatedAtUtc.Ticks - (evt.CreatedAtUtc.Ticks % 10); // 100ns → 1µs
            evt.CreatedAtUtc = new DateTimeOffset(truncatedTicks, TimeSpan.Zero);
            await db.SaveChangesAsync();

            (await AuditChainService.VerifyTenantChainAsync(db, tenantId, ChainKeyHex)).Valid
                .Should().BeTrue("the hash uses Unix-ms, which microsecond truncation cannot change");
        }
    }

    [Fact]
    public async Task Backfill_builds_chain_rows_for_events_written_before_the_interceptor()
    {
        var tenantId = Guid.NewGuid();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            for (var i = 0; i < 3; i++)
            {
                db.AuditEvents.Add(new AuditEventEntity
                {
                    TenantId = tenantId, EventType = "system_changed", ReasonCode = "x",
                    CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(i),
                });
                await db.SaveChangesAsync();
            }
        }

        // Fresh scope = the startup scenario. Simulate the pre-fix prod state: events exist, but
        // their chain rows were never written.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.AuditChain.ExecuteDeleteAsync();
            (await AuditChainService.VerifyTenantChainAsync(db, tenantId, ChainKeyHex)).FailureReason
                .Should().Be("missing_chain_entry", "without chain rows the tamper-evident verify must fail");

            var created = await AuditChainService.BackfillAsync(db, ChainKeyHex);
            created.Should().Be(3);

            (await AuditChainService.VerifyTenantChainAsync(db, tenantId, ChainKeyHex)).Valid
                .Should().BeTrue("backfill reconstructs the chain so verify passes end-to-end");

            (await AuditChainService.BackfillAsync(db, ChainKeyHex)).Should().Be(0, "backfill is idempotent");
        }
    }

    public void Dispose()
    {
        factory.Dispose();
        foreach (var candidate in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
            if (File.Exists(candidate)) File.Delete(candidate);
    }

    private static async Task RegisterFileAsync(HttpClient client, Guid tenantId, Guid ownerUserId, Guid fileId)
    {
        using var response = await client.PostAsJsonAsync("/api/files", new
        {
            tenantId, fileId, ownerUserId,
            contentType = "application/pdf",
            expiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            permissions = "View",
            watermarkTemplate = "user:{userId}",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task WrapKeyAsync(HttpClient client, Guid tenantId, Guid fileId, byte[] fileKey)
    {
        using var response = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/wrap", new
        {
            tenantId, fileKeyBase64 = Convert.ToBase64String(fileKey),
        });
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);
    }

    private static async Task<HttpStatusCode> UnwrapStatusAsync(HttpClient client, Guid tenantId, Guid fileId, Guid userId)
    {
        using var response = await client.PostAsJsonAsync($"/api/files/{fileId}/keys/unwrap", new
        {
            tenantId, userId, deviceId = Guid.NewGuid(), requestedPermission = "View",
        });
        return response.StatusCode;
    }
}
