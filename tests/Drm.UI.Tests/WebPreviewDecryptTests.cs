using Drm.Container;
using Drm.Crypto;
using Drm.Domain;
using FluentAssertions;
using Microsoft.Playwright;

namespace Drm.UI.Tests;

/// <summary>
/// Proves the client-side .drmx decryption (wwwroot/share/drmx-preview.js) works
/// in a REAL browser against a .drmx produced by the real C# writer. This is the
/// load-bearing verification for in-browser preview: the JS must parse the DRM1
/// container, reconstruct the AES-GCM associated data (including the .NET
/// 100-nanosecond tick timestamp) byte-for-byte, and decrypt — any mismatch
/// fails GCM authentication. CI's Linux job runs this against Chromium.
/// </summary>
[Collection(nameof(DrmUiTestCollection))]
public sealed class WebPreviewDecryptTests
{
    private readonly DrmServerFixture server;
    private readonly PlaywrightFixture playwright;

    public WebPreviewDecryptTests(DrmServerFixture server, PlaywrightFixture playwright)
    {
        this.server = server;
        this.playwright = playwright;
    }

    [Fact]
    public async Task Browser_decrypts_a_real_drmx_container_with_the_unwrapped_key()
    {
        // Build a real .drmx with the production writer + a known key/plaintext.
        var fileKey = EnvelopeCrypto.GenerateKey();
        // Plaintext with multi-byte UTF-8 + a chunk of real binary bytes (0..255)
        // so the round-trip exercises the full byte range, not just ASCII.
        var text = "%PDF-1.7\nzcrDRM web-preview round-trip ✓\n"u8.ToArray();
        var binary = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        var plaintext = text.Concat(binary).ToArray();
        using var ms = new MemoryStream();
        ProtectedFileWriter.Write(ms, TenantId.New(), ProtectedFileId.New(), "application/pdf", fileKey, plaintext);

        var drmxBase64 = Convert.ToBase64String(ms.ToArray());
        var keyBase64 = Convert.ToBase64String(fileKey);
        var expectedBase64 = Convert.ToBase64String(plaintext);

        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{server.BaseUrl}/share/");
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = $"{server.BaseUrl}/share/drmx-preview.js",
            Type = "module",
        });
        await page.WaitForFunctionAsync("() => !!window.DrmxPreview");

        // Return a single delimited string (contentType|plainBase64) to avoid
        // Playwright object/dictionary deserialization quirks.
        var result = await page.EvaluateAsync<string>(
            @"async ([drmxB64, keyB64]) => {
                const dec = s => Uint8Array.from(atob(s), c => c.charCodeAt(0));
                const drmx = dec(drmxB64);
                const key = dec(keyB64);
                const { bytes, contentType } = await window.DrmxPreview.decryptDrmx(drmx.buffer, key);
                let bin = '';
                for (const b of bytes) bin += String.fromCharCode(b);
                return contentType + '|' + btoa(bin);
            }",
            new[] { drmxBase64, keyBase64 });

        var parts = result!.Split('|', 2);
        parts[0].Should().Be("application/pdf");
        parts[1].Should().Be(expectedBase64,
            "the browser must reproduce the exact original bytes — proving container parse + AAD ticks + GCM all match the writer");
    }

    [Fact]
    public async Task Browser_decrypt_fails_cleanly_with_the_wrong_key()
    {
        var fileKey = EnvelopeCrypto.GenerateKey();
        var wrongKey = EnvelopeCrypto.GenerateKey();
        using var ms = new MemoryStream();
        ProtectedFileWriter.Write(ms, TenantId.New(), ProtectedFileId.New(), "application/pdf", fileKey,
            "%PDF-1.7 secret"u8.ToArray());

        await using var context = await playwright.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        await page.GotoAsync($"{server.BaseUrl}/share/");
        await page.AddScriptTagAsync(new PageAddScriptTagOptions
        {
            Url = $"{server.BaseUrl}/share/drmx-preview.js",
            Type = "module",
        });
        await page.WaitForFunctionAsync("() => !!window.DrmxPreview");

        var threw = await page.EvaluateAsync<bool>(
            @"async ([drmxB64, keyB64]) => {
                const dec = s => Uint8Array.from(atob(s), c => c.charCodeAt(0));
                try {
                    await window.DrmxPreview.decryptDrmx(dec(drmxB64).buffer, dec(keyB64));
                    return false;
                } catch {
                    return true;
                }
            }",
            new[] { Convert.ToBase64String(ms.ToArray()), Convert.ToBase64String(wrongKey) });

        threw.Should().BeTrue("a wrong key must fail GCM authentication, not return garbage");
    }
}
