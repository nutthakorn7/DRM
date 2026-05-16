using System.Text.Json.Serialization;

namespace Drm.Server;

public sealed class ScimUser
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:schemas:core:2.0:User"];

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("externalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalId { get; init; }

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("emails")]
    public IReadOnlyList<ScimEmail> Emails { get; init; } = [];

    [JsonPropertyName("active")]
    public bool Active { get; init; } = true;

    [JsonPropertyName("meta")]
    public ScimMeta Meta { get; init; } = new();
}

public sealed record ScimEmail(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("primary")] bool Primary);

public sealed class ScimGroup
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:schemas:core:2.0:Group"];

    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonPropertyName("externalId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ExternalId { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("members")]
    public IReadOnlyList<ScimGroupMember> Members { get; init; } = [];

    [JsonPropertyName("meta")]
    public ScimMeta Meta { get; init; } = new();
}

public sealed record ScimGroupMember(
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("display")] string? Display);

public sealed class ScimListResponse<T>
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:api:messages:2.0:ListResponse"];

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; init; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; init; } = 1;

    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; init; }

    [JsonPropertyName("Resources")]
    public IReadOnlyList<T> Resources { get; init; } = [];
}

public sealed class ScimMeta
{
    [JsonPropertyName("resourceType")]
    public string ResourceType { get; init; } = string.Empty;

    [JsonPropertyName("created")]
    public DateTimeOffset Created { get; init; }

    [JsonPropertyName("lastModified")]
    public DateTimeOffset LastModified { get; init; }

    [JsonPropertyName("location")]
    public string Location { get; init; } = string.Empty;
}

public sealed class ScimError
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string> Schemas { get; init; } = ["urn:ietf:params:scim:api:messages:2.0:Error"];

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;
}

public sealed class ScimUserRequest
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string>? Schemas { get; init; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    [JsonPropertyName("userName")]
    public string UserName { get; init; } = string.Empty;

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; init; }

    [JsonPropertyName("emails")]
    public IReadOnlyList<ScimEmail>? Emails { get; init; }

    [JsonPropertyName("active")]
    public bool Active { get; init; } = true;
}

public sealed class ScimGroupRequest
{
    [JsonPropertyName("schemas")]
    public IReadOnlyList<string>? Schemas { get; init; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; init; }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    [JsonPropertyName("members")]
    public IReadOnlyList<ScimGroupMemberRef>? Members { get; init; }
}

public sealed class ScimGroupMemberRef
{
    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;
}
