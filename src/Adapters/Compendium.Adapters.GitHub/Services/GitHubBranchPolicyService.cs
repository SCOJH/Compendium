// -----------------------------------------------------------------------
// <copyright file="GitHubBranchPolicyService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using Compendium.Adapters.GitHub.Auth;
using Compendium.Adapters.GitHub.Http;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// Branch protection backed by GitHub repository rulesets (not legacy branch
/// protection). Each neutral policy maps to one ruleset named
/// <c>compendium:{pattern}</c>; <see cref="ApplyAsync"/> finds an existing ruleset
/// by that name and updates it, so applying the same pattern twice is idempotent.
/// </summary>
internal sealed class GitHubBranchPolicyService : IGitBranchPolicyService
{
    private const string NamePrefix = "compendium:";
    private const int RepositoryAdminRoleId = 5;

    private readonly GitHubCredentialBroker _broker;
    private readonly GitHubRestExecutor _rest;

    public GitHubBranchPolicyService(GitHubCredentialBroker broker, GitHubRestExecutor rest)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
    }

    /// <inheritdoc />
    public async Task<Result<GitBranchPolicy>> ApplyAsync(
        GitConnection connection, GitRepositoryRef repository, GitBranchPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.BranchPolicies);
        if (guard.IsFailure)
        {
            return Result.Failure<GitBranchPolicy>(guard.Error);
        }

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure<GitBranchPolicy>(auth.Error);
        }

        var context = GitRestErrorContext.ForRepository(repository);
        var name = NamePrefix + request.Pattern;
        var basePath = $"repos/{repository.Namespace}/{repository.Name}/rulesets";

        var existing = await _rest.GetAsync<List<GitHubRulesetDto>>(
            auth.Value.ApiBase, auth.Value.Token, basePath, context, cancellationToken).ConfigureAwait(false);
        if (existing.IsFailure)
        {
            return Result.Failure<GitBranchPolicy>(existing.Error);
        }

        var match = existing.Value.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.Ordinal));
        var payload = BuildPayload(request, name);

        var applied = match is null
            ? await _rest.SendWithBodyAsync<GitHubRulesetDto>(
                HttpMethod.Post, auth.Value.ApiBase, auth.Value.Token, basePath, payload, context, cancellationToken)
                .ConfigureAwait(false)
            : await _rest.SendWithBodyAsync<GitHubRulesetDto>(
                HttpMethod.Put, auth.Value.ApiBase, auth.Value.Token, $"{basePath}/{match.Id}", payload, context, cancellationToken)
                .ConfigureAwait(false);

        return applied.IsFailure
            ? Result.Failure<GitBranchPolicy>(applied.Error)
            : Result.Success(new GitBranchPolicy(applied.Value.Id.ToString(CultureInfo.InvariantCulture), request.Pattern));
    }

    /// <inheritdoc />
    public async Task<Result> RemoveAsync(
        GitConnection connection, GitRepositoryRef repository, string policyId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyId);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure(auth.Error);
        }

        var path = $"repos/{repository.Namespace}/{repository.Name}/rulesets/{Uri.EscapeDataString(policyId)}";
        return await _rest.DeleteIdempotentAsync(
            auth.Value.ApiBase, auth.Value.Token, path, GitRestErrorContext.ForRepository(repository), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<GitBranchPolicy>>> ListAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GitBranchPolicy>>(auth.Error);
        }

        var path = $"repos/{repository.Namespace}/{repository.Name}/rulesets";
        var result = await _rest.GetAsync<List<GitHubRulesetDto>>(
            auth.Value.ApiBase, auth.Value.Token, path, GitRestErrorContext.ForRepository(repository), cancellationToken)
            .ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GitBranchPolicy>>(result.Error);
        }

        IReadOnlyList<GitBranchPolicy> policies = result.Value
            .Select(r => new GitBranchPolicy(r.Id.ToString(CultureInfo.InvariantCulture), PatternFromName(r.Name)))
            .ToList();
        return Result.Success(policies);
    }

    private static string PatternFromName(string name) =>
        name.StartsWith(NamePrefix, StringComparison.Ordinal) ? name[NamePrefix.Length..] : name;

    private static object BuildPayload(GitBranchPolicyRequest request, string name)
    {
        var rules = new List<object>();

        if (request.RequirePullRequest)
        {
            rules.Add(new
            {
                type = "pull_request",
                parameters = new
                {
                    required_approving_review_count = request.RequiredApprovals,
                    dismiss_stale_reviews_on_push = request.DismissStaleApprovals,
                    require_code_owner_review = false,
                    require_last_push_approval = false,
                    required_review_thread_resolution = false,
                },
            });
        }

        if (request.RequiredStatusChecks is { Count: > 0 } checks)
        {
            rules.Add(new
            {
                type = "required_status_checks",
                parameters = new
                {
                    required_status_checks = checks.Select(c => new { context = c }).ToArray(),
                    strict_required_status_checks_policy = false,
                },
            });
        }

        if (request.BlockForcePush)
        {
            rules.Add(new { type = "non_fast_forward" });
        }

        if (request.BlockDeletion)
        {
            rules.Add(new { type = "deletion" });
        }

        if (request.RequireLinearHistory)
        {
            rules.Add(new { type = "required_linear_history" });
        }

        var payload = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["name"] = name,
            ["target"] = "branch",
            ["enforcement"] = "active",
            ["conditions"] = new
            {
                ref_name = new { include = new[] { $"refs/heads/{request.Pattern}" }, exclude = Array.Empty<string>() },
            },
            ["rules"] = rules,
        };

        if (!request.EnforceForAdmins)
        {
            payload["bypass_actors"] = new[]
            {
                new { actor_id = RepositoryAdminRoleId, actor_type = "RepositoryRole", bypass_mode = "always" },
            };
        }

        return payload;
    }
}
