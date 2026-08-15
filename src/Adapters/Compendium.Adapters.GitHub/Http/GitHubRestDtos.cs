// -----------------------------------------------------------------------
// <copyright file="GitHubRestDtos.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Compendium.Adapters.GitHub.Http;

/// <summary>A GitHub account (organization or user) as returned by the REST API.</summary>
internal sealed class GitHubAccountDto
{
    [JsonPropertyName("login")]
    public string Login { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>Maps the provider account type onto the neutral enum (defaults to organization).</summary>
    public GitAccountType ToAccountType() =>
        string.Equals(Type, "User", StringComparison.OrdinalIgnoreCase)
            ? GitAccountType.User
            : GitAccountType.Organization;
}

/// <summary>A GitHub App installation as returned by the REST API.</summary>
internal sealed class GitHubInstallationDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("account")]
    public GitHubAccountDto? Account { get; init; }

    [JsonPropertyName("suspended_at")]
    public DateTimeOffset? SuspendedAt { get; init; }

    /// <summary>Maps onto the neutral installation info, defaulting an absent account to an org.</summary>
    public GitInstallationInfo ToInstallationInfo() => new(
        Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Account?.Login ?? string.Empty,
        Account?.ToAccountType() ?? GitAccountType.Organization,
        SuspendedAt is not null);
}

/// <summary>A GitHub Actions public key (used to seal secrets before upload).</summary>
internal sealed class GitHubPublicKeyDto
{
    [JsonPropertyName("key_id")]
    public string KeyId { get; init; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; init; } = string.Empty;
}

/// <summary>A GitHub deployment environment as returned by the REST API.</summary>
internal sealed class GitHubEnvironmentDto
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; init; }
}

/// <summary>The list envelope returned by <c>GET /repos/{o}/{r}/environments</c>.</summary>
internal sealed class GitHubEnvironmentListDto
{
    [JsonPropertyName("environments")]
    public List<GitHubEnvironmentDto> Environments { get; init; } = [];
}

/// <summary>A repository ruleset as returned by the rulesets REST API.</summary>
internal sealed class GitHubRulesetDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}

/// <summary>The minimal repository shape needed to resolve a numeric repository id.</summary>
internal sealed class GitHubRepositoryIdDto
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
}
