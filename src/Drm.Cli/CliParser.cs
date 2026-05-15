using Drm.Domain;

namespace Drm.Cli;

public sealed record CliParseResult(bool IsSuccess, ICliCommand? Command, string? Error)
{
    public static CliParseResult Success(ICliCommand command) => new(true, command, null);

    public static CliParseResult Fail(string error) => new(false, null, error);
}

public interface ICliCommand
{
}

public sealed record CliRecipient(string SubjectType, Guid SubjectId);

public sealed record ProtectCommandOptions(
    string ServerUrl,
    Guid TenantId,
    Guid UserId,
    string FilePath,
    Permission Permissions,
    Guid? PolicyTemplateId,
    IReadOnlyList<CliRecipient> Recipients,
    string? ClientApiKey,
    string? InventoryPath,
    string? KeyStorePath,
    bool DeleteOriginal) : ICliCommand;

public sealed record OpenCommandOptions(
    string ServerUrl,
    Guid UserId,
    Guid DeviceId,
    string FilePath,
    string OutputPath,
    string? ClientApiKey,
    string? KeyStorePath,
    string? PolicyCachePath) : ICliCommand;

public sealed record HelpCommandOptions : ICliCommand;

public static class CliParser
{
    private const Permission DefaultPermissions = Permission.View | Permission.Print;

    public static CliParseResult Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0 || args[0] is "--help" or "-h")
        {
            return CliParseResult.Success(new HelpCommandOptions());
        }

        return args[0] switch
        {
            "protect" => ParseProtect(args.Skip(1).ToArray()),
            "open" => ParseOpen(args.Skip(1).ToArray()),
            _ => CliParseResult.Fail($"Unknown command '{args[0]}'.")
        };
    }

    public static string Usage =>
        """
        Usage:
          drm-cli protect --server-url URL --tenant-id GUID --user-id GUID --file PATH [--permissions "View, Print"] [--policy-template-id GUID] [--recipient-user-id GUID] [--recipient-group-id GUID] [--delete-original]
          drm-cli open --server-url URL --user-id GUID --device-id GUID --file PATH --output PATH
        """;

    private static CliParseResult ParseProtect(IReadOnlyList<string> args)
    {
        var options = ReadOptions(args);
        if (options.Error is not null)
        {
            return CliParseResult.Fail(options.Error);
        }

        string? tenantError = null;
        string? userError = null;
        if (!TryGetRequired(options.Values, "server-url", out var serverUrl) ||
            !TryGetRequiredGuid(options.Values, "tenant-id", out var tenantId, out tenantError) ||
            !TryGetRequiredGuid(options.Values, "user-id", out var userId, out userError) ||
            !TryGetRequired(options.Values, "file", out var filePath))
        {
            return CliParseResult.Fail(tenantError ?? userError ?? "Missing required option for protect.");
        }

        if (!TryParsePermissions(GetOptional(options.Values, "permissions") ?? DefaultPermissions.ToString(), out var permissions))
        {
            return CliParseResult.Fail("Invalid permissions.");
        }

        if (!TryGetOptionalGuid(options.Values, "policy-template-id", out var policyTemplateId, out var templateError))
        {
            return CliParseResult.Fail(templateError!);
        }

        var recipients = options.RepeatedValues
            .Where(pair => pair.Key is "recipient-user-id" or "recipient-group-id")
            .SelectMany(pair => pair.Value.Select(value => ToRecipient(pair.Key, value)))
            .ToList();

        var invalidRecipient = recipients.FirstOrDefault(recipient => recipient.SubjectId == Guid.Empty);
        if (invalidRecipient is not null)
        {
            return CliParseResult.Fail("Invalid recipient ID.");
        }

        return CliParseResult.Success(new ProtectCommandOptions(
            serverUrl,
            tenantId,
            userId,
            filePath,
            permissions,
            policyTemplateId,
            recipients,
            GetOptional(options.Values, "client-api-key"),
            GetOptional(options.Values, "inventory"),
            GetOptional(options.Values, "key-store"),
            options.Flags.Contains("delete-original")));
    }

    private static CliParseResult ParseOpen(IReadOnlyList<string> args)
    {
        var options = ReadOptions(args);
        if (options.Error is not null)
        {
            return CliParseResult.Fail(options.Error);
        }

        string? userError = null;
        string? deviceError = null;
        if (!TryGetRequired(options.Values, "server-url", out var serverUrl) ||
            !TryGetRequiredGuid(options.Values, "user-id", out var userId, out userError) ||
            !TryGetRequiredGuid(options.Values, "device-id", out var deviceId, out deviceError) ||
            !TryGetRequired(options.Values, "file", out var filePath) ||
            !TryGetRequired(options.Values, "output", out var outputPath))
        {
            return CliParseResult.Fail(userError ?? deviceError ?? "Missing required option for open.");
        }

        return CliParseResult.Success(new OpenCommandOptions(
            serverUrl,
            userId,
            deviceId,
            filePath,
            outputPath,
            GetOptional(options.Values, "client-api-key"),
            GetOptional(options.Values, "key-store"),
            GetOptional(options.Values, "policy-cache")));
    }

    private static ParsedOptions ReadOptions(IReadOnlyList<string> args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var repeatedValues = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                return ParsedOptions.Fail($"Unexpected argument '{token}'.");
            }

            var name = token[2..];
            if (name is "delete-original")
            {
                flags.Add(name);
                continue;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return ParsedOptions.Fail($"Missing value for --{name}.");
            }

            var value = args[++index];
            if (name is "recipient-user-id" or "recipient-group-id")
            {
                if (!repeatedValues.TryGetValue(name, out var existing))
                {
                    existing = [];
                    repeatedValues[name] = existing;
                }

                existing.Add(value);
                continue;
            }

            values[name] = value;
        }

        return new ParsedOptions(values, repeatedValues, flags, null);
    }

    private static bool TryGetRequired(IReadOnlyDictionary<string, string> values, string name, out string value)
    {
        return values.TryGetValue(name, out value!) && !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetRequiredGuid(
        IReadOnlyDictionary<string, string> values,
        string name,
        out Guid value,
        out string? error)
    {
        value = Guid.Empty;
        error = null;
        if (!TryGetRequired(values, name, out var text))
        {
            error = $"Missing required option --{name}.";
            return false;
        }

        if (!Guid.TryParse(text, out value) || value == Guid.Empty)
        {
            error = $"Invalid GUID for --{name}.";
            return false;
        }

        return true;
    }

    private static bool TryGetOptionalGuid(
        IReadOnlyDictionary<string, string> values,
        string name,
        out Guid? value,
        out string? error)
    {
        value = null;
        error = null;
        if (!values.TryGetValue(name, out var text) || string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!Guid.TryParse(text, out var parsed) || parsed == Guid.Empty)
        {
            error = $"Invalid GUID for --{name}.";
            return false;
        }

        value = parsed;
        return true;
    }

    private static bool TryParsePermissions(string text, out Permission permissions)
    {
        permissions = Permission.None;
        foreach (var token in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<Permission>(token, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(parsed))
            {
                return false;
            }

            permissions |= parsed;
        }

        return permissions != Permission.None;
    }

    private static string? GetOptional(IReadOnlyDictionary<string, string> values, string name)
        => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static CliRecipient ToRecipient(string optionName, string value)
    {
        if (!Guid.TryParse(value, out var subjectId))
        {
            subjectId = Guid.Empty;
        }

        return new CliRecipient(optionName == "recipient-user-id" ? "User" : "Group", subjectId);
    }

    private sealed record ParsedOptions(
        IReadOnlyDictionary<string, string> Values,
        IReadOnlyDictionary<string, List<string>> RepeatedValues,
        IReadOnlySet<string> Flags,
        string? Error)
    {
        public static ParsedOptions Fail(string error)
            => new(
                new Dictionary<string, string>(StringComparer.Ordinal),
                new Dictionary<string, List<string>>(StringComparer.Ordinal),
                new HashSet<string>(StringComparer.Ordinal),
                error);
    }
}
