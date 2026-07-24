// -----------------------------------------------------------------------
// <copyright file="GitHubAccessControlService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Http;
using Octokit;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// Teams and repository access on an organization, backed by Octokit. Only
/// meaningful for organization namespaces — GitHub rejects team operations on
/// user accounts, which surfaces as a mapped provider error. The neutral
/// repository roles map onto GitHub's <c>pull/triage/push/maintain/admin</c>.
/// </summary>
internal sealed class GitHubAccessControlService : IGitAccessControlService
{
    private readonly IGitHubClientProvider _clients;

    public GitHubAccessControlService(IGitHubClientProvider clients)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    }

    /// <inheritdoc />
    public async Task<Result<GitTeam>> EnsureTeamAsync(
        GitConnection connection, string @namespace, EnsureGitTeam request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.TeamsAndPermissions);
        if (guard.IsFailure)
        {
            return Result.Failure<GitTeam>(guard.Error);
        }

        return await ExecuteAsync(connection, GitRestErrorContext.ForNamespace(@namespace), async client =>
        {
            var slug = Slugify(request.Name);
            try
            {
                var existing = await client.Organization.Team.GetByName(@namespace, slug).ConfigureAwait(false);
                return Result.Success(new GitTeam(existing.Slug, existing.Name));
            }
            catch (NotFoundException)
            {
                var created = await client.Organization.Team
                    .Create(@namespace, new NewTeam(request.Name) { Description = request.Description })
                    .ConfigureAwait(false);
                return Result.Success(new GitTeam(created.Slug, created.Name));
            }
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result> AddTeamMemberAsync(
        GitConnection connection, string @namespace, string teamSlug, string username, GitTeamRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamSlug);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return ExecuteUnitAsync(connection, GitRestErrorContext.ForNamespace(@namespace), async client =>
        {
            var team = await client.Organization.Team.GetByName(@namespace, teamSlug).ConfigureAwait(false);
            await client.Organization.Team.AddOrEditMembership(
                team.Id, username, new UpdateTeamMembership(role == GitTeamRole.Maintainer ? TeamRole.Maintainer : TeamRole.Member))
                .ConfigureAwait(false);
        });
    }

    /// <inheritdoc />
    public Task<Result> SetTeamRepositoryRoleAsync(
        GitConnection connection, string @namespace, string teamSlug, GitRepositoryRef repository, GitRepositoryRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamSlug);
        ArgumentNullException.ThrowIfNull(repository);

        return ExecuteUnitAsync(connection, GitRestErrorContext.ForRepository(repository), client =>
            client.Organization.Team.AddOrUpdateTeamRepositoryPermissions(
                @namespace, teamSlug, repository.Namespace, repository.Name, MapPermission(role)));
    }

    /// <inheritdoc />
    public Task<Result> SetUserRepositoryRoleAsync(
        GitConnection connection, GitRepositoryRef repository, string username, GitRepositoryRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return ExecuteUnitAsync(connection, GitRestErrorContext.ForRepository(repository), client =>
            client.Repository.Collaborator.Add(
                repository.Namespace, repository.Name, username, new CollaboratorRequest(MapPermission(role))));
    }

    /// <inheritdoc />
    public Task<Result> RemoveUserFromRepositoryAsync(
        GitConnection connection, GitRepositoryRef repository, string username, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        return ExecuteUnitAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            try
            {
                await client.Repository.Collaborator.Delete(repository.Namespace, repository.Name, username)
                    .ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                // Idempotent: removing access that is already absent succeeds.
            }
        });
    }

    private static string MapPermission(GitRepositoryRole role) => role switch
    {
        GitRepositoryRole.Read => "pull",
        GitRepositoryRole.Triage => "triage",
        GitRepositoryRole.Write => "push",
        GitRepositoryRole.Maintain => "maintain",
        GitRepositoryRole.Admin => "admin",
        _ => "pull",
    };

    private static string Slugify(string name) =>
        name.Trim().ToLowerInvariant().Replace(' ', '-');

    private async Task<Result<T>> ExecuteAsync<T>(
        GitConnection connection, GitRestErrorContext context, Func<IGitHubClient, Task<Result<T>>> operation)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var clientResult = await _clients.GetClientAsync(connection, CancellationToken.None).ConfigureAwait(false);
        if (clientResult.IsFailure)
        {
            return Result.Failure<T>(clientResult.Error);
        }

        try
        {
            return await operation(clientResult.Value).ConfigureAwait(false);
        }
        catch (ApiException ex)
        {
            return Result.Failure<T>(GitHubErrorMapper.FromException(ex, context));
        }
    }

    private async Task<Result> ExecuteUnitAsync(
        GitConnection connection, GitRestErrorContext context, Func<IGitHubClient, Task> operation)
    {
        var result = await ExecuteAsync<object?>(connection, context, async client =>
        {
            await operation(client).ConfigureAwait(false);
            return Result.Success<object?>(null);
        }).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }
}
