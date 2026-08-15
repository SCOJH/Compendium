// -----------------------------------------------------------------------
// <copyright file="SecretVaultCapability.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Capabilities;

/// <summary>
/// The optional capabilities a secret-vault adapter can declare. Consumers use
/// the declared matrix to drive UI affordances and to select providers;
/// adapters use <see cref="SecretVaultCapabilities.EnsureSupported"/> to fail
/// uniformly when an undeclared capability is invoked.
/// </summary>
public enum SecretVaultCapability
{
    /// <summary>
    /// Versions are immutable once written: the material of a revision can
    /// never change, so a revision reference is a stable rollback anchor.
    /// This is the core contract; adapters that cannot guarantee it must not
    /// declare it, and callers requiring stable history must refuse them.
    /// </summary>
    ImmutableVersions,

    /// <summary>
    /// Individual revisions can be disabled (kill-switch for a leaked value)
    /// and re-enabled without destroying the material.
    /// </summary>
    VersionEnableDisable,

    /// <summary>
    /// Individual revisions can be permanently destroyed (retention/cost
    /// control). Destroyed revisions read as not found.
    /// </summary>
    VersionDestroy,

    /// <summary>
    /// Secrets are organized in a hierarchical path namespace that supports
    /// prefix listing.
    /// </summary>
    PathHierarchy,

    /// <summary>
    /// Secrets carry provider-side key/value tags usable as list filters.
    /// </summary>
    Tags,

    /// <summary>
    /// The provider accepts payloads larger than 64 KiB per version.
    /// </summary>
    LargePayload,

    /// <summary>
    /// The provider supports ephemeral (auto-expiring) secrets. Not surfaced
    /// by the v1 ports; declared for capability-driven UI only.
    /// </summary>
    EphemeralSecrets,

    /// <summary>
    /// The provider can rotate secret material server-side on a schedule. Not
    /// surfaced by the v1 ports; declared for capability-driven UI only.
    /// </summary>
    ServerSideRotation,
}
