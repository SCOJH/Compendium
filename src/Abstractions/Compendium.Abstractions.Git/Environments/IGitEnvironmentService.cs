// -----------------------------------------------------------------------
// <copyright file="IGitEnvironmentService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Environments;

/// <summary>
/// Deployment-environment management on a repository (GitHub Environments,
/// GitLab environments). Requires
/// <see cref="Capabilities.GitCapability.DeploymentEnvironments"/>.
/// Environment-scoped secrets are set via
/// <see cref="CiConfiguration.IGitCiConfigurationService"/> with an
/// <see cref="CiConfiguration.GitConfigurationScope.Environment"/> scope.
/// </summary>
public interface IGitEnvironmentService
{
    /// <summary>
    /// Creates the environment when absent, updates it otherwise (idempotent).
    /// </summary>
    Task<Result<GitDeploymentEnvironment>> EnsureAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        EnsureGitEnvironment request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the environment. Deleting an absent environment succeeds (idempotent).
    /// </summary>
    Task<Result> DeleteAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string environmentName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the repository's deployment environments.
    /// </summary>
    Task<Result<IReadOnlyList<GitDeploymentEnvironment>>> ListAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create or update a deployment environment.
/// </summary>
public sealed record EnsureGitEnvironment
{
    /// <summary>Gets the environment name (e.g. <c>"production"</c>).</summary>
    public required string Name { get; init; }
}

/// <summary>
/// A deployment environment on a repository.
/// </summary>
/// <param name="Name">The environment name.</param>
/// <param name="HtmlUrl">The web URL of the environment, when the provider reports one.</param>
public sealed record GitDeploymentEnvironment(string Name, string? HtmlUrl = null);
