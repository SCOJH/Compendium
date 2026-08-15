// -----------------------------------------------------------------------
// <copyright file="GitHubEnvironmentService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Auth;
using Compendium.Adapters.GitHub.Http;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// GitHub deployment environments over the REST API. <c>PUT .../environments/{name}</c>
/// is create-or-update, so <see cref="EnsureAsync"/> is naturally idempotent.
/// </summary>
internal sealed class GitHubEnvironmentService : IGitEnvironmentService
{
    private readonly GitHubCredentialBroker _broker;
    private readonly GitHubRestExecutor _rest;

    public GitHubEnvironmentService(GitHubCredentialBroker broker, GitHubRestExecutor rest)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
    }

    /// <inheritdoc />
    public async Task<Result<GitDeploymentEnvironment>> EnsureAsync(
        GitConnection connection, GitRepositoryRef repository, EnsureGitEnvironment request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.DeploymentEnvironments);
        if (guard.IsFailure)
        {
            return Result.Failure<GitDeploymentEnvironment>(guard.Error);
        }

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure<GitDeploymentEnvironment>(auth.Error);
        }

        var path = $"repos/{repository.Namespace}/{repository.Name}/environments/{Uri.EscapeDataString(request.Name)}";
        var result = await _rest.SendWithBodyAsync<GitHubEnvironmentDto>(
            HttpMethod.Put, auth.Value.ApiBase, auth.Value.Token, path, new { },
            GitRestErrorContext.ForRepository(repository), cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<GitDeploymentEnvironment>(result.Error)
            : Result.Success(new GitDeploymentEnvironment(request.Name, result.Value.HtmlUrl));
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        GitConnection connection, GitRepositoryRef repository, string environmentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentName);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure(auth.Error);
        }

        var path = $"repos/{repository.Namespace}/{repository.Name}/environments/{Uri.EscapeDataString(environmentName)}";
        return await _rest.DeleteIdempotentAsync(
            auth.Value.ApiBase, auth.Value.Token, path, GitRestErrorContext.ForRepository(repository), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<GitDeploymentEnvironment>>> ListAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GitDeploymentEnvironment>>(auth.Error);
        }

        var path = $"repos/{repository.Namespace}/{repository.Name}/environments";
        var result = await _rest.GetAsync<GitHubEnvironmentListDto>(
            auth.Value.ApiBase, auth.Value.Token, path, GitRestErrorContext.ForRepository(repository), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GitDeploymentEnvironment>>(result.Error);
        }

        IReadOnlyList<GitDeploymentEnvironment> environments = result.Value.Environments
            .Select(e => new GitDeploymentEnvironment(e.Name, e.HtmlUrl)).ToList();
        return Result.Success(environments);
    }
}
