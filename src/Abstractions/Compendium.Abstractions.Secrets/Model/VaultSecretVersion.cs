// -----------------------------------------------------------------------
// <copyright file="VaultSecretVersion.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Model;

/// <summary>
/// The lifecycle state of a single secret version.
/// </summary>
public enum VaultSecretVersionStatus
{
    /// <summary>The version is readable.</summary>
    Enabled,

    /// <summary>The version is kill-switched: access is refused until re-enabled.</summary>
    Disabled,

    /// <summary>The material was permanently destroyed; the revision number remains reserved.</summary>
    Destroyed,
}

/// <summary>
/// One immutable version of a secret, identified by its monotonically
/// increasing <see cref="Revision"/> (1-based). A revision number is never
/// reused, which is what makes <c>(secretId, revision)</c> a stable value
/// reference for history and rollback.
/// </summary>
public sealed record VaultSecretVersion
{
    /// <summary>
    /// Gets the 1-based, monotonically increasing revision number.
    /// </summary>
    public required long Revision { get; init; }

    /// <summary>
    /// Gets the version's lifecycle state.
    /// </summary>
    public required VaultSecretVersionStatus Status { get; init; }

    /// <summary>
    /// Gets the description recorded when the version was written, when any.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Gets the creation timestamp, when the provider reports one.
    /// </summary>
    public DateTimeOffset? CreatedAt { get; init; }
}
