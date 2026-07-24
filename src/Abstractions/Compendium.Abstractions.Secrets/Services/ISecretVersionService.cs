// -----------------------------------------------------------------------
// <copyright file="ISecretVersionService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;

namespace Compendium.Abstractions.Secrets.Services;

/// <summary>
/// Immutable secret versions. Every read addresses an explicit revision —
/// the port has no "latest" concept, deliberately: which revision is current
/// is the caller's metadata, so history stays stable and rollback is a
/// re-pointing of references, independent of provider-side mutable state.
/// </summary>
public interface ISecretVersionService
{
    /// <summary>
    /// Appends an immutable version and returns it with its stable revision
    /// number (the rollback anchor). Not safely retryable after the payload
    /// was sent — callers own idempotency (deduplicate by content hash before
    /// writing).
    /// </summary>
    Task<Result<VaultSecretVersion>> AddAsync(
        SecretVaultConnection connection,
        string secretId,
        SecretMaterial material,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the material of an exact revision. Revisions are immutable, so a
    /// successful read is cacheable indefinitely by the caller. Fails with
    /// <c>SecretVault.VersionDisabled</c> on a kill-switched revision and
    /// <c>SecretVault.VersionNotFound</c> on a destroyed or unknown one.
    /// </summary>
    Task<Result<SecretMaterial>> AccessAsync(
        SecretVaultConnection connection,
        string secretId,
        long revision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the versions of a secret (metadata only, no material).
    /// </summary>
    Task<Result<IReadOnlyList<VaultSecretVersion>>> ListAsync(
        SecretVaultConnection connection,
        string secretId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-enables a disabled revision. Idempotent on an already-enabled one.
    /// Requires <see cref="Capabilities.SecretVaultCapability.VersionEnableDisable"/>.
    /// </summary>
    Task<Result> EnableAsync(
        SecretVaultConnection connection,
        string secretId,
        long revision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kill-switches a revision: its material stops being readable until
    /// re-enabled. Idempotent on an already-disabled one. Requires
    /// <see cref="Capabilities.SecretVaultCapability.VersionEnableDisable"/>.
    /// </summary>
    Task<Result> DisableAsync(
        SecretVaultConnection connection,
        string secretId,
        long revision,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently destroys a revision's material (retention/cost control).
    /// The revision number stays reserved and subsequently reads as not
    /// found. Requires <see cref="Capabilities.SecretVaultCapability.VersionDestroy"/>.
    /// </summary>
    Task<Result> DestroyAsync(
        SecretVaultConnection connection,
        string secretId,
        long revision,
        CancellationToken cancellationToken = default);
}
