// -----------------------------------------------------------------------
// <copyright file="IGitBranchPolicyService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Protection;

/// <summary>
/// Branch protection policies on a repository. The policy model is neutral:
/// adapters map it onto their native mechanism (GitHub: repository rulesets;
/// GitLab: protected branches + push rules) and document lossy mappings in
/// their CAPABILITIES.md. Requires
/// <see cref="Capabilities.GitCapability.BranchPolicies"/>.
/// </summary>
public interface IGitBranchPolicyService
{
    /// <summary>
    /// Applies a protection policy. When a policy with the same
    /// <see cref="GitBranchPolicyRequest.Pattern"/> already exists, it is
    /// updated (idempotent).
    /// </summary>
    Task<Result<GitBranchPolicy>> ApplyAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        GitBranchPolicyRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a policy by identifier. Removing an absent policy succeeds (idempotent).
    /// </summary>
    Task<Result> RemoveAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string policyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the repository's protection policies.
    /// </summary>
    Task<Result<IReadOnlyList<GitBranchPolicy>>> ListAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A neutral branch protection policy request.
/// </summary>
public sealed record GitBranchPolicyRequest
{
    /// <summary>Gets the branch name pattern the policy targets (e.g. <c>"main"</c>, <c>"release/*"</c>).</summary>
    public required string Pattern { get; init; }

    /// <summary>Gets whether changes must go through a pull/merge request. Defaults to true.</summary>
    public bool RequirePullRequest { get; init; } = true;

    /// <summary>Gets the number of approvals required on the pull request.</summary>
    public int RequiredApprovals { get; init; }

    /// <summary>Gets whether stale approvals are dismissed when new commits are pushed.</summary>
    public bool DismissStaleApprovals { get; init; }

    /// <summary>Gets the CI status checks that must pass before merging, when any.</summary>
    public IReadOnlyList<string>? RequiredStatusChecks { get; init; }

    /// <summary>Gets whether force pushes to matching branches are blocked. Defaults to true.</summary>
    public bool BlockForcePush { get; init; } = true;

    /// <summary>Gets whether deletion of matching branches is blocked. Defaults to true.</summary>
    public bool BlockDeletion { get; init; } = true;

    /// <summary>Gets whether a linear history (no merge commits) is required.</summary>
    public bool RequireLinearHistory { get; init; }

    /// <summary>Gets whether the policy also applies to repository administrators.</summary>
    public bool EnforceForAdmins { get; init; }
}

/// <summary>
/// An applied branch protection policy.
/// </summary>
/// <param name="Id">The provider-side policy identifier.</param>
/// <param name="Pattern">The branch name pattern the policy targets.</param>
public sealed record GitBranchPolicy(string Id, string Pattern);
