// -----------------------------------------------------------------------
// <copyright file="ScalewayApiModels.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Compendium.Adapters.Scaleway.SecretManager.Http;

/// <summary>A Secret Manager secret resource.</summary>
internal sealed record ScalewaySecret
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("project_id")]
    public string? ProjectId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("path")]
    public string? Path { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string>? Tags { get; init; }

    [JsonPropertyName("version_count")]
    public long? VersionCount { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>A Secret Manager secret version resource.</summary>
internal sealed record ScalewaySecretVersion
{
    [JsonPropertyName("revision")]
    public required long Revision { get; init; }

    [JsonPropertyName("secret_id")]
    public string? SecretId { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }
}

/// <summary>The payload returned by the version access endpoint.</summary>
internal sealed record ScalewayAccessResponse
{
    [JsonPropertyName("revision")]
    public long Revision { get; init; }

    [JsonPropertyName("data")]
    public required string Data { get; init; }
}

/// <summary>A paginated secret listing.</summary>
internal sealed record ScalewayListSecretsResponse
{
    [JsonPropertyName("secrets")]
    public IReadOnlyList<ScalewaySecret>? Secrets { get; init; }

    [JsonPropertyName("total_count")]
    public long TotalCount { get; init; }
}

/// <summary>A paginated version listing.</summary>
internal sealed record ScalewayListVersionsResponse
{
    [JsonPropertyName("versions")]
    public IReadOnlyList<ScalewaySecretVersion>? Versions { get; init; }

    [JsonPropertyName("total_count")]
    public long TotalCount { get; init; }
}

/// <summary>The request to create a secret.</summary>
internal sealed record ScalewayCreateSecretRequest
{
    [JsonPropertyName("project_id")]
    public required string ProjectId { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("tags")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Tags { get; init; }
}

/// <summary>The request to append a secret version.</summary>
internal sealed record ScalewayCreateVersionRequest
{
    [JsonPropertyName("data")]
    public required string Data { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }
}

/// <summary>A Scaleway API error body.</summary>
internal sealed record ScalewayErrorResponse
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }
}
