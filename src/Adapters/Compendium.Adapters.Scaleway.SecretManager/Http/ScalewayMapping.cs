// -----------------------------------------------------------------------
// <copyright file="ScalewayMapping.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Model;

namespace Compendium.Adapters.Scaleway.SecretManager.Http;

/// <summary>
/// Pure mappings between the neutral abstraction model and the Secret Manager
/// wire model: key/value tags to the provider's string list
/// (<c>key:value</c>), paths, statuses, and resource descriptors.
/// </summary>
internal static class ScalewayMapping
{
    /// <summary>
    /// Encodes neutral key/value tags as the provider's string list.
    /// </summary>
    public static IReadOnlyList<string>? ToTagList(IReadOnlyDictionary<string, string>? tags) =>
        tags is null or { Count: 0 }
            ? null
            : [.. tags.Select(kv => string.IsNullOrEmpty(kv.Value) ? kv.Key : $"{kv.Key}:{kv.Value}")];

    /// <summary>
    /// Decodes the provider's string list back into key/value tags (first
    /// <c>:</c> splits; colon-less tags map to an empty value).
    /// </summary>
    public static IReadOnlyDictionary<string, string> FromTagList(IReadOnlyList<string>? tags)
    {
        var result = new Dictionary<string, string>();
        foreach (var tag in tags ?? [])
        {
            var separator = tag.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                result[tag] = string.Empty;
            }
            else
            {
                result[tag[..separator]] = tag[(separator + 1)..];
            }
        }

        return result;
    }

    /// <summary>
    /// Parses a provider path (<c>/a/b</c>) back into a scope path, tolerating
    /// separators and empty segments.
    /// </summary>
    public static SecretScopePath ParsePath(string? path)
    {
        var segments = (path ?? "/").Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parsed = SecretScopePath.From(segments);
        return parsed.IsSuccess ? parsed.Value : SecretScopePath.Root;
    }

    /// <summary>
    /// Maps a provider version status onto the neutral lifecycle. Unknown
    /// statuses read as disabled (fail-closed for consumption).
    /// </summary>
    public static VaultSecretVersionStatus ParseStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "enabled" => VaultSecretVersionStatus.Enabled,
            "deleted" or "destroyed" => VaultSecretVersionStatus.Destroyed,
            _ => VaultSecretVersionStatus.Disabled,
        };

    /// <summary>
    /// Maps a provider secret onto the neutral descriptor.
    /// </summary>
    public static VaultSecretDescriptor ToDescriptor(ScalewaySecret secret) => new()
    {
        SecretId = secret.Id,
        Name = secret.Name,
        Path = ParsePath(secret.Path),
        Description = string.IsNullOrEmpty(secret.Description) ? null : secret.Description,
        Tags = FromTagList(secret.Tags),
        VersionCount = secret.VersionCount,
        CreatedAt = secret.CreatedAt,
    };

    /// <summary>
    /// Maps a provider version onto the neutral version.
    /// </summary>
    public static VaultSecretVersion ToVersion(ScalewaySecretVersion version) => new()
    {
        Revision = version.Revision,
        Status = ParseStatus(version.Status),
        Description = string.IsNullOrEmpty(version.Description) ? null : version.Description,
        CreatedAt = version.CreatedAt,
    };
}
