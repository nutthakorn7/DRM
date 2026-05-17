using Drm.Agent.Core;
using Drm.Crypto;
using Drm.Domain;

namespace Drm.Cli;

public static class Program
{
    public static Task<int> Main(string[] args)
        => DrmCli.RunAsync(args, Console.Out, Console.Error, CancellationToken.None);
}

public static class DrmCli
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var parseResult = CliParser.Parse(args);
        if (!parseResult.IsSuccess)
        {
            await error.WriteLineAsync(parseResult.Error);
            await error.WriteLineAsync(CliParser.Usage);
            return 2;
        }

        switch (parseResult.Command)
        {
            case HelpCommandOptions:
                await output.WriteLineAsync(CliParser.Usage);
                return 0;
            case ProtectCommandOptions protect:
                return await ProtectAsync(protect, output, cancellationToken);
            case OpenCommandOptions open:
                return await OpenAsync(open, output, cancellationToken);
            case ShareCommandOptions share:
                return await ShareAsync(share, output, error, cancellationToken);
            default:
                await error.WriteLineAsync("No command was supplied.");
                return 2;
        }
    }

    private static async Task<int> ShareAsync(
        ShareCommandOptions options,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var bytes = await File.ReadAllBytesAsync(options.FilePath, cancellationToken);
        using var http = new HttpClient { BaseAddress = new Uri(options.ServerUrl) };
        if (!string.IsNullOrEmpty(options.AdminApiKey))
        {
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-DRM-Admin-Key", options.AdminApiKey);
        }

        var body = new
        {
            tenantId = options.TenantId,
            userId = options.UserId,
            recipientEmail = options.RecipientEmail,
            fileName = Path.GetFileName(options.FilePath),
            contentType = "application/octet-stream",
            fileBytesBase64 = Convert.ToBase64String(bytes),
            expiresInHours = options.ExpiresInHours,
            allowPrint = options.AllowPrint
        };

        using var content = new StringContent(
            System.Text.Json.JsonSerializer.Serialize(body),
            System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/api/me/share", content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            await error.WriteLineAsync($"Share failed: HTTP {(int)response.StatusCode} {responseBody}");
            return 1;
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
        var shareUrl = doc.RootElement.GetProperty("shareUrl").GetString();
        await output.WriteLineAsync(shareUrl);
        return 0;
    }

    private static async Task<int> ProtectAsync(
        ProtectCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(options.ServerUrl, options.ClientApiKey);
        var inventory = new JsonProtectedFileInventory(options.InventoryPath ?? DefaultDataPath("protected-inventory.json"));
        var keyStore = new JsonFileKeyStore(options.KeyStorePath ?? DefaultDataPath("file-keys.json"));
        var recipients = options.Recipients
            .Select(recipient => new ProtectionRecipient(recipient.SubjectType, recipient.SubjectId))
            .ToList();
        var result = await new ProtectFileWorkflow(client, inventory, keyStore)
            .ProtectAsync(
                new TenantId(options.TenantId),
                new UserId(options.UserId),
                options.FilePath,
                EnvelopeCrypto.GenerateKey(),
                new ProtectFilePolicyOptions(
                    options.Permissions,
                    options.PolicyTemplateId,
                    recipients),
                options.DeleteOriginal,
                cancellationToken);

        await output.WriteLineAsync(result.DestinationPath);
        return 0;
    }

    private static async Task<int> OpenAsync(
        OpenCommandOptions options,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var client = CreateClient(options.ServerUrl, options.ClientApiKey);
        var keyStore = new JsonFileKeyStore(options.KeyStorePath ?? DefaultDataPath("file-keys.json"));
        var policyCache = new JsonPolicyDecisionCache(options.PolicyCachePath ?? DefaultDataPath("policy-decisions.json"));
        var opened = await new OpenProtectedFileWorkflow(client, keyStore, policyCache)
            .OpenAsync(
                options.FilePath,
                new UserId(options.UserId),
                new DeviceId(options.DeviceId),
                cancellationToken);

        var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        await File.WriteAllBytesAsync(options.OutputPath, opened.Content, cancellationToken);
        await output.WriteLineAsync(options.OutputPath);
        return 0;
    }

    private static DrmServerClient CreateClient(string serverUrl, string? clientApiKey)
    {
        return new DrmServerClient(
            new HttpClient { BaseAddress = new Uri(serverUrl, UriKind.Absolute) },
            clientApiKey);
    }

    private static string DefaultDataPath(string fileName)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
        {
            root = AppContext.BaseDirectory;
        }

        var directory = Path.Combine(root, "DRM");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, fileName);
    }
}
