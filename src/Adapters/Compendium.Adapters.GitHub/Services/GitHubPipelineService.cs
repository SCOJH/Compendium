// -----------------------------------------------------------------------
// <copyright file="GitHubPipelineService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Globalization;
using Compendium.Adapters.GitHub.Http;
using Octokit;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// CI pipeline operations backed by GitHub Actions: dispatch a workflow and read
/// run status. <c>workflow_dispatch</c> is fire-and-forget — <see cref="TriggerAsync"/>
/// returns a handle with a <see langword="null"/> run id and callers correlate the
/// created run via <see cref="ListRunsAsync"/>.
/// </summary>
internal sealed class GitHubPipelineService : IGitPipelineService
{
    private readonly IGitHubClientProvider _clients;

    public GitHubPipelineService(IGitHubClientProvider clients)
    {
        _clients = clients ?? throw new ArgumentNullException(nameof(clients));
    }

    /// <inheritdoc />
    public async Task<Result<GitPipelineRunHandle>> TriggerAsync(
        GitConnection connection, GitRepositoryRef repository, TriggerGitPipeline request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.PipelineTrigger);
        if (guard.IsFailure)
        {
            return Result.Failure<GitPipelineRunHandle>(guard.Error);
        }

        return await ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var dispatch = new CreateWorkflowDispatch(request.Reference);
            if (request.Inputs is { Count: > 0 } inputs)
            {
                dispatch.Inputs = inputs.ToDictionary(kvp => kvp.Key, kvp => (object)kvp.Value);
            }

            await client.Actions.Workflows
                .CreateDispatch(repository.Namespace, repository.Name, request.Pipeline, dispatch).ConfigureAwait(false);

            // workflow_dispatch returns 204 with no run id — see CAPABILITIES.md (PipelineTrigger=Partial).
            return Result.Success(new GitPipelineRunHandle(null));
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result<GitPipelineRun>> GetRunAsync(
        GitConnection connection, GitRepositoryRef repository, string runId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        if (!long.TryParse(runId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numericRunId))
        {
            return Result.Failure<GitPipelineRun>(Error.Validation(
                "GitHub.InvalidRunId", $"'{runId}' is not a valid GitHub Actions run id."));
        }

        return await ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var run = await client.Actions.Workflows.Runs
                .Get(repository.Namespace, repository.Name, numericRunId).ConfigureAwait(false);
            return Result.Success(Map(run));
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitPipelineRun>>> ListRunsAsync(
        GitConnection connection, GitRepositoryRef repository, ListGitPipelineRuns query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(query);

        return ExecuteAsync(connection, GitRestErrorContext.ForRepository(repository), async client =>
        {
            var response = string.IsNullOrWhiteSpace(query.Pipeline)
                ? await client.Actions.Workflows.Runs.List(repository.Namespace, repository.Name).ConfigureAwait(false)
                : await client.Actions.Workflows.Runs
                    .ListByWorkflow(repository.Namespace, repository.Name, query.Pipeline).ConfigureAwait(false);

            IReadOnlyList<GitPipelineRun> runs = response.WorkflowRuns
                .Where(r => string.IsNullOrWhiteSpace(query.Reference) || r.HeadBranch == query.Reference)
                .OrderByDescending(r => r.CreatedAt)
                .Take(query.Limit)
                .Select(Map)
                .ToList();
            return Result.Success(runs);
        });
    }

    private static GitPipelineRun Map(WorkflowRun run) => new(
        run.Id.ToString(CultureInfo.InvariantCulture),
        run.Name,
        GitHubPipelineStatusMapper.Map(run.Status.StringValue, run.Conclusion?.StringValue),
        run.HeadBranch,
        run.HtmlUrl,
        run.CreatedAt);

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
