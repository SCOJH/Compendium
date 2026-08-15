// -----------------------------------------------------------------------
// <copyright file="IGitRepositoryService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;

namespace Compendium.Abstractions.Git.Repositories;

/// <summary>
/// Repository lifecycle and read operations: create from template, inspect
/// contents, list commits/branches/tags, create tags and releases.
/// </summary>
public interface IGitRepositoryService
{
    /// <summary>
    /// Creates a repository from a template repository. Requires
    /// <see cref="Capabilities.GitCapability.RepositoryFromTemplate"/>.
    /// </summary>
    Task<Result<GitRepository>> CreateFromTemplateAsync(
        GitConnection connection,
        CreateRepositoryFromTemplate request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a repository by reference. Fails with <c>Git.RepositoryNotFound</c>
    /// when absent or not visible to the credential.
    /// </summary>
    Task<Result<GitRepository>> GetAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the repositories of <paramref name="namespace"/> visible to the
    /// connection's credential.
    /// </summary>
    Task<Result<IReadOnlyList<GitRepository>>> ListAsync(
        GitConnection connection,
        string @namespace,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether <paramref name="path"/> exists in the repository at
    /// <paramref name="gitRef"/> (default branch when null). Used e.g. to
    /// verify a repository finished bootstrapping before tagging a release.
    /// </summary>
    Task<Result<bool>> FileExistsAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string path,
        string? gitRef = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists recent commits, newest first.
    /// </summary>
    /// <param name="connection">The connection to operate with.</param>
    /// <param name="repository">The repository to read.</param>
    /// <param name="reference">Optional branch/ref/SHA to list from; the default branch when null or empty.</param>
    /// <param name="limit">Maximum number of commits returned (a single page).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<IReadOnlyList<GitCommit>>> ListCommitsAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string? reference,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the repository's branches.
    /// </summary>
    Task<Result<IReadOnlyList<GitBranch>>> ListBranchesAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tag. When <paramref name="commitSha"/> is null, tags the head
    /// of the default branch. Requires
    /// <see cref="Capabilities.GitCapability.TagsAndReleases"/>.
    /// </summary>
    Task<Result<GitTag>> CreateTagAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string tagName,
        string? commitSha = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the repository's tags.
    /// </summary>
    Task<Result<IReadOnlyList<GitTag>>> ListTagsAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a release. Requires
    /// <see cref="Capabilities.GitCapability.TagsAndReleases"/>.
    /// </summary>
    Task<Result<GitRelease>> CreateReleaseAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        CreateGitRelease request,
        CancellationToken cancellationToken = default);
}
