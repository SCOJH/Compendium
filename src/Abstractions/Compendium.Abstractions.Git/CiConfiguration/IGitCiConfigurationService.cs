// -----------------------------------------------------------------------
// <copyright file="IGitCiConfigurationService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.CiConfiguration;

/// <summary>
/// CI secrets and variables management. Secrets are write-only on every
/// provider — values can be set and deleted but never read back. Adapters
/// perform any provider-required client-side encryption (GitHub: libsodium
/// sealed box) transparently.
/// </summary>
public interface IGitCiConfigurationService
{
    /// <summary>
    /// Creates or updates CI secrets at <paramref name="scope"/>. Requires
    /// <see cref="Capabilities.GitCapability.CiSecrets"/> (repository scope),
    /// <see cref="Capabilities.GitCapability.NamespaceSecrets"/> (namespace scope), or
    /// <see cref="Capabilities.GitCapability.EnvironmentSecrets"/> (environment scope).
    /// </summary>
    Task<Result> SetSecretsAsync(
        GitConnection connection,
        GitConfigurationScope scope,
        IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a CI secret at <paramref name="scope"/>. Deleting an absent
    /// secret succeeds (idempotent).
    /// </summary>
    Task<Result> DeleteSecretAsync(
        GitConnection connection,
        GitConfigurationScope scope,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates CI variables (plaintext configuration) at
    /// <paramref name="scope"/>. Requires
    /// <see cref="Capabilities.GitCapability.CiVariables"/>.
    /// </summary>
    Task<Result> SetVariablesAsync(
        GitConnection connection,
        GitConfigurationScope scope,
        IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a CI variable at <paramref name="scope"/>. Deleting an absent
    /// variable succeeds (idempotent).
    /// </summary>
    Task<Result> DeleteVariableAsync(
        GitConnection connection,
        GitConfigurationScope scope,
        string name,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Where a CI secret or variable lives, as a closed union: on a repository, on
/// a namespace (organization/group), or on a deployment environment of a
/// repository.
/// </summary>
public abstract record GitConfigurationScope
{
    private GitConfigurationScope()
    {
    }

    /// <summary>
    /// Repository-scoped configuration.
    /// </summary>
    /// <param name="Ref">The repository.</param>
    public sealed record Repository(GitRepositoryRef Ref) : GitConfigurationScope;

    /// <summary>
    /// Namespace-scoped (organization/group) configuration, shared by the
    /// namespace's repositories.
    /// </summary>
    /// <param name="Name">The namespace login.</param>
    public sealed record Namespace(string Name) : GitConfigurationScope;

    /// <summary>
    /// Environment-scoped configuration on a repository's deployment environment.
    /// </summary>
    /// <param name="Ref">The repository.</param>
    /// <param name="EnvironmentName">The deployment environment name.</param>
    public sealed record Environment(GitRepositoryRef Ref, string EnvironmentName) : GitConfigurationScope;
}
