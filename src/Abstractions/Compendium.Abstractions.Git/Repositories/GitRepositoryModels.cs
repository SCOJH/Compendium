// -----------------------------------------------------------------------
// <copyright file="GitRepositoryModels.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Repositories;

/// <summary>
/// Identifies a repository by its namespace (organization/group/user login) and name.
/// </summary>
/// <param name="Namespace">The owning namespace login (e.g. <c>"acme"</c>).</param>
/// <param name="Name">The repository name (e.g. <c>"billing-api"</c>).</param>
public sealed record GitRepositoryRef(string Namespace, string Name)
{
    /// <summary>
    /// Gets the <c>namespace/name</c> form of the reference.
    /// </summary>
    public string FullName => $"{Namespace}/{Name}";

    /// <inheritdoc />
    public override string ToString() => FullName;
}

/// <summary>
/// A repository as reported by the provider.
/// </summary>
/// <param name="Ref">The repository reference.</param>
/// <param name="CloneUrl">The HTTPS clone URL.</param>
/// <param name="HtmlUrl">The web URL of the repository.</param>
/// <param name="DefaultBranch">The default branch name (e.g. <c>"main"</c>).</param>
/// <param name="Private">Whether the repository is private.</param>
public sealed record GitRepository(
    GitRepositoryRef Ref,
    string CloneUrl,
    string HtmlUrl,
    string DefaultBranch,
    bool Private);

/// <summary>
/// A single commit, surfaced so a caller can pick a commit to deploy.
/// </summary>
/// <param name="Sha">The full commit SHA.</param>
/// <param name="Message">The commit message (first line is the summary).</param>
/// <param name="AuthorName">The author's display name, when available.</param>
/// <param name="AuthoredAt">When the commit was authored, when available.</param>
/// <param name="HtmlUrl">A web URL to view the commit.</param>
public sealed record GitCommit(
    string Sha,
    string Message,
    string? AuthorName,
    DateTimeOffset? AuthoredAt,
    string HtmlUrl);

/// <summary>
/// A branch on a repository.
/// </summary>
/// <param name="Name">The branch name (e.g. <c>"main"</c>).</param>
/// <param name="Sha">The commit SHA the branch currently points at.</param>
/// <param name="Protected">Whether the branch is protected.</param>
public sealed record GitBranch(string Name, string Sha, bool Protected);

/// <summary>
/// A tag on a repository.
/// </summary>
/// <param name="Name">The tag name (e.g. <c>"v1.2.3"</c>).</param>
/// <param name="Sha">The commit SHA the tag points at.</param>
public sealed record GitTag(string Name, string Sha);

/// <summary>
/// A published release.
/// </summary>
/// <param name="Id">The provider-side release identifier.</param>
/// <param name="TagName">The tag the release was published from.</param>
/// <param name="HtmlUrl">The web URL of the release.</param>
public sealed record GitRelease(string Id, string TagName, string HtmlUrl);

/// <summary>
/// Request to create a repository from a template/scaffold repository.
/// </summary>
public sealed record CreateRepositoryFromTemplate
{
    /// <summary>Gets the template repository to instantiate.</summary>
    public required GitRepositoryRef Template { get; init; }

    /// <summary>Gets the namespace the new repository is created under.</summary>
    public required string Namespace { get; init; }

    /// <summary>Gets the new repository's name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the optional repository description.</summary>
    public string? Description { get; init; }

    /// <summary>Gets whether the new repository is private. Defaults to true.</summary>
    public bool Private { get; init; } = true;
}

/// <summary>
/// Request to publish a release.
/// </summary>
public sealed record CreateGitRelease
{
    /// <summary>Gets the tag to release. Created at <see cref="TargetCommitSha"/> when absent.</summary>
    public required string TagName { get; init; }

    /// <summary>Gets the commit SHA to tag when the tag does not exist yet; defaults to the head of the default branch.</summary>
    public string? TargetCommitSha { get; init; }

    /// <summary>Gets the release title; defaults to the tag name.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the release notes body, when any.</summary>
    public string? Body { get; init; }
}
