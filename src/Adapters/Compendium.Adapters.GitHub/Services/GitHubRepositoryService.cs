// -----------------------------------------------------------------------
// <copyright file="GitHubRepositoryService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Http;
using Octokit;
using GitCommit = Compendium.Abstractions.Git.Repositories.GitCommit;
using GitTag = Compendium.Abstractions.Git.Repositories.GitTag;
using OctokitRepository = Octokit.Repository;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// Repository lifecycle and read operations backed by Octokit: create from
/// template, inspect contents, list commits/branches/tags, create tags and
/// publish releases.
/// </summary>
internal sealed class GitHubRepositoryService : IGitRepositoryService
{
    private readonly IGitHubClientProvider _clients;

    public GitHubRepositoryService(IGitHubClientProvider clients)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    }

    /// <inheritdoc />
    public async Task<Result<GitRepository>> CreateFromTemplateAsync(
        GitConnection connection, CreateRepositoryFromTemplate request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.RepositoryFromTemplate);
        if (guard.IsFailure)
        {
            return Result.Failure<GitRepository>(guard.Error);
        }

        var context = new GitRestErrorContext
        {
            RepositoryFullName = request.Template.FullName,
            ConflictResource = $"{request.Namespace}/{request.Name}",
        };

        return await ExecuteAsync(connection, context, async client =>
        {
            var newRepo = new NewRepositoryFromTemplate(request.Name)
            {
                Owner = request.Namespace,
                Description = request.Description,
                Private = request.Private,
            };

            var repo = await client.Repository
                .Generate(request.Template.Namespace, request.Template.Name, newRepo).ConfigureAwait(false);
            return Result.Success(Map(repo));
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<GitRepository>> GetAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var repo = await client.Repository.Get(repository.Namespace, repository.Name).ConfigureAwait(false);
            return Result.Success(Map(repo));
        });
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitRepository>>> ListAsync(
        GitConnection connection, string @namespace, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);
        return ExecuteAsync(connection, GitRestErrorContext.ForNamespace(@namespace), async client =>
        {
            IReadOnlyList<OctokitRepository> repos;
            try
            {
                repos = await client.Repository.GetAllForOrg(@namespace).ConfigureAwait(false);
            }
            catch (NotFoundException)
            {
                repos = await client.Repository.GetAllForUser(@namespace).ConfigureAwait(false);
            }

            IReadOnlyList<GitRepository> mapped = repos.Select(Map).ToList();
            return Result.Success(mapped);
        });
    }

    /// <inheritdoc />
    public Task<Result<bool>> FileExistsAsync(
        GitConnection connection, GitRepositoryRef repository, string path, string? gitRef = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            try
            {
                var contents = string.IsNullOrEmpty(gitRef)
                    ? await client.Repository.Content.GetAllContents(repository.Namespace, repository.Name, path)
                        .ConfigureAwait(false)
                    : await client.Repository.Content.GetAllContentsByRef(repository.Namespace, repository.Name, path, gitRef)
                        .ConfigureAwait(false);
                return Result.Success(contents.Count > 0);
            }
            catch (NotFoundException)
            {
                return Result.Success(false);
            }
        });
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitCommit>>> ListCommitsAsync(
        GitConnection connection, GitRepositoryRef repository, string? reference, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var request = new CommitRequest();
            if (!string.IsNullOrWhiteSpace(reference))
            {
                request.Sha = reference;
            }

            var options = new ApiOptions { PageSize = limit, PageCount = 1 };
            var commits = await client.Repository.Commit
                .GetAll(repository.Namespace, repository.Name, request, options).ConfigureAwait(false);

            IReadOnlyList<GitCommit> mapped = commits.Select(c => new GitCommit(
                c.Sha,
                c.Commit.Message,
                c.Commit.Author?.Name,
                c.Commit.Author?.Date,
                c.HtmlUrl)).ToList();
            return Result.Success(mapped);
        });
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitBranch>>> ListBranchesAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var branches = await client.Repository.Branch
                .GetAll(repository.Namespace, repository.Name).ConfigureAwait(false);
            IReadOnlyList<GitBranch> mapped = branches
                .Select(b => new GitBranch(b.Name, b.Commit.Sha, b.Protected)).ToList();
            return Result.Success(mapped);
        });
    }

    /// <inheritdoc />
    public async Task<Result<GitTag>> CreateTagAsync(
        GitConnection connection, GitRepositoryRef repository, string tagName, string? commitSha = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagName);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.TagsAndReleases);
        if (guard.IsFailure)
        {
            return Result.Failure<GitTag>(guard.Error);
        }

        return await ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var sha = commitSha ?? await ResolveDefaultBranchHeadAsync(client, repository).ConfigureAwait(false);
            var reference = await client.Git.Reference
                .Create(repository.Namespace, repository.Name, new NewReference($"refs/tags/{tagName}", sha))
                .ConfigureAwait(false);
            return Result.Success(new GitTag(tagName, reference.Object.Sha));
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitTag>>> ListTagsAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        return ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var tags = await client.Repository.GetAllTags(repository.Namespace, repository.Name).ConfigureAwait(false);
            IReadOnlyList<GitTag> mapped = tags.Select(t => new GitTag(t.Name, t.Commit.Sha)).ToList();
            return Result.Success(mapped);
        });
    }

    /// <inheritdoc />
    public async Task<Result<GitRelease>> CreateReleaseAsync(
        GitConnection connection, GitRepositoryRef repository, CreateGitRelease request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.TagsAndReleases);
        if (guard.IsFailure)
        {
            return Result.Failure<GitRelease>(guard.Error);
        }

        return await ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var newRelease = new NewRelease(request.TagName)
            {
                Name = request.Title ?? request.TagName,
                Body = request.Body,
                GenerateReleaseNotes = request.Body is null,
            };
            if (!string.IsNullOrWhiteSpace(request.TargetCommitSha))
            {
                newRelease.TargetCommitish = request.TargetCommitSha;
            }

            var release = await client.Repository.Release
                .Create(repository.Namespace, repository.Name, newRelease).ConfigureAwait(false);
            return Result.Success(new GitRelease(
                release.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                release.TagName,
                release.HtmlUrl));
        }).ConfigureAwait(false);
    }

    private static GitRepository Map(OctokitRepository repo) => new(
        new GitRepositoryRef(repo.Owner.Login, repo.Name),
        repo.CloneUrl,
        repo.HtmlUrl,
        string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch,
        repo.Private);

    private static async Task<string> ResolveDefaultBranchHeadAsync(IGitHubClient client, GitRepositoryRef repository)
    {
        var repo = await client.Repository.Get(repository.Namespace, repository.Name).ConfigureAwait(false);
        var branchName = string.IsNullOrWhiteSpace(repo.DefaultBranch) ? "main" : repo.DefaultBranch;
        var branch = await client.Repository.Branch
            .Get(repository.Namespace, repository.Name, branchName).ConfigureAwait(false);
        return branch.Commit.Sha;
    }

    private async Task<Result<T>> ExecuteAsync<T>(
        GitConnection connection,
        GitRestErrorContext context,
        Func<IGitHubClient, Task<Result<T>>> operation)
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
}
