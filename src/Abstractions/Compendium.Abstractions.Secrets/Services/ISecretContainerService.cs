// -----------------------------------------------------------------------
// <copyright file="ISecretContainerService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;

namespace Compendium.Abstractions.Secrets.Services;

/// <summary>
/// Secret container lifecycle: create, describe, list, delete. Containers
/// hold versions (<see cref="ISecretVersionService"/>); they carry no
/// material themselves.
/// </summary>
public interface ISecretContainerService
{
    /// <summary>
    /// Creates a secret container. Fails with
    /// <c>SecretVault.ConflictExists</c> when a secret with the same name
    /// already exists at the same path.
    /// </summary>
    Task<Result<VaultSecretDescriptor>> CreateAsync(
        SecretVaultConnection connection,
        CreateVaultSecret request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Describes a secret by its provider-side id.
    /// </summary>
    Task<Result<VaultSecretDescriptor>> GetAsync(
        SecretVaultConnection connection,
        string secretId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists secrets under a path prefix, optionally filtered by tags
    /// (all given tags must match). Adapters without
    /// <see cref="Capabilities.SecretVaultCapability.PathHierarchy"/> treat the
    /// path as an exact match; adapters without
    /// <see cref="Capabilities.SecretVaultCapability.Tags"/> fail when a tag
    /// filter is supplied.
    /// </summary>
    Task<Result<IReadOnlyList<VaultSecretDescriptor>>> ListAsync(
        SecretVaultConnection connection,
        SecretScopePath pathPrefix,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a secret container and every version it holds. Idempotent:
    /// deleting an unknown id succeeds.
    /// </summary>
    Task<Result> DeleteAsync(
        SecretVaultConnection connection,
        string secretId,
        CancellationToken cancellationToken = default);
}
