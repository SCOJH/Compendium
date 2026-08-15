// -----------------------------------------------------------------------
// <copyright file="CreateVaultSecret.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Model;

/// <summary>
/// The request to create a secret container (which holds versions, but no
/// material yet — write the first version with
/// <see cref="Services.ISecretVersionService.AddAsync"/>).
/// </summary>
public sealed record CreateVaultSecret
{
    /// <summary>
    /// Gets the secret name, unique within its <see cref="Path"/>. Diagnostic
    /// and console-facing only — the provider-side id returned at creation is
    /// the identity.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the hierarchical path the secret lives under.
    /// </summary>
    public required SecretScopePath Path { get; init; }

    /// <summary>
    /// Gets an optional human-readable description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets optional provider-side tags (list filters, reconciliation
    /// markers). Adapters without the <see cref="Capabilities.SecretVaultCapability.Tags"/>
    /// capability ignore them.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }
}
