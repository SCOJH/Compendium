// -----------------------------------------------------------------------
// <copyright file="IGitAccessControlService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.AccessControl;

/// <summary>
/// Teams and repository access management on a namespace. Only meaningful for
/// organization namespaces — adapters fail with <c>Git.CapabilityNotSupported</c>
/// on user accounts. Requires
/// <see cref="Capabilities.GitCapability.TeamsAndPermissions"/>.
/// </summary>
public interface IGitAccessControlService
{
    /// <summary>
    /// Creates the team when absent, updates it otherwise (idempotent).
    /// </summary>
    Task<Result<GitTeam>> EnsureTeamAsync(
        GitConnection connection,
        string @namespace,
        EnsureGitTeam request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a user to a team (or updates their role in it).
    /// </summary>
    Task<Result> AddTeamMemberAsync(
        GitConnection connection,
        string @namespace,
        string teamSlug,
        string username,
        GitTeamRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants (or updates) a team's role on a repository.
    /// </summary>
    Task<Result> SetTeamRepositoryRoleAsync(
        GitConnection connection,
        string @namespace,
        string teamSlug,
        GitRepositoryRef repository,
        GitRepositoryRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grants (or updates) an individual user's role on a repository.
    /// </summary>
    Task<Result> SetUserRepositoryRoleAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string username,
        GitRepositoryRole role,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a user's direct access to a repository. Removing absent access
    /// succeeds (idempotent).
    /// </summary>
    Task<Result> RemoveUserFromRepositoryAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string username,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create or update a team.
/// </summary>
public sealed record EnsureGitTeam
{
    /// <summary>Gets the team display name; the provider derives the slug from it.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the optional team description.</summary>
    public string? Description { get; init; }
}

/// <summary>
/// A team on a namespace.
/// </summary>
/// <param name="Slug">The provider-side team slug used in subsequent calls.</param>
/// <param name="Name">The team display name.</param>
public sealed record GitTeam(string Slug, string Name);

/// <summary>
/// A user's role inside a team.
/// </summary>
public enum GitTeamRole
{
    /// <summary>Regular team member.</summary>
    Member,

    /// <summary>Team maintainer (can manage membership).</summary>
    Maintainer,
}

/// <summary>
/// A neutral repository access role. Adapters map onto their native role set
/// and document lossy mappings in their CAPABILITIES.md.
/// </summary>
public enum GitRepositoryRole
{
    /// <summary>Read-only access.</summary>
    Read,

    /// <summary>Read plus issue/PR triage.</summary>
    Triage,

    /// <summary>Read/write access.</summary>
    Write,

    /// <summary>Write plus repository settings short of admin.</summary>
    Maintain,

    /// <summary>Full administrative access.</summary>
    Admin,
}
