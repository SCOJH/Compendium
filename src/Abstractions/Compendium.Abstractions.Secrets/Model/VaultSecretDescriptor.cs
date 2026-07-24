// -----------------------------------------------------------------------
// <copyright file="VaultSecretDescriptor.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Model;

/// <summary>
/// A secret container as the provider reports it. Carries no material.
/// </summary>
public sealed record VaultSecretDescriptor
{
    /// <summary>
    /// Gets the provider-side identifier. This is the secret's identity:
    /// consumers persist and address by this id, never by name/path.
    /// </summary>
    public required string SecretId { get; init; }

    /// <summary>
    /// Gets the secret name (unique within <see cref="Path"/>).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the hierarchical path the secret lives under.
    /// </summary>
    public required SecretScopePath Path { get; init; }

    /// <summary>
    /// Gets the human-readable description, when set.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the provider-side tags (empty when untagged or unsupported).
    /// </summary>
    public IReadOnlyDictionary<string, string> Tags { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Gets the number of versions the provider reports for this secret,
    /// when known.
    /// </summary>
    public long? VersionCount { get; init; }

    /// <summary>
    /// Gets the creation timestamp, when the provider reports one.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
