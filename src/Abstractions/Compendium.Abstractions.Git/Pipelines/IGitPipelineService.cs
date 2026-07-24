// -----------------------------------------------------------------------
// <copyright file="IGitPipelineService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Pipelines;

/// <summary>
/// CI pipeline operations: trigger runs and read their status.
/// </summary>
public interface IGitPipelineService
{
    /// <summary>
    /// Triggers a pipeline run. Requires
    /// <see cref="Capabilities.GitCapability.PipelineTrigger"/>.
    /// </summary>
    /// <remarks>
    /// Some providers do not return a run identifier on trigger (GitHub's
    /// <c>workflow_dispatch</c> is fire-and-forget): the handle's
    /// <see cref="GitPipelineRunHandle.RunId"/> is <see langword="null"/> there
    /// and callers correlate via <see cref="ListRunsAsync"/>. Each adapter
    /// documents this in its CAPABILITIES.md.
    /// </remarks>
    Task<Result<GitPipelineRunHandle>> TriggerAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        TriggerGitPipeline request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a single pipeline run by identifier. Requires
    /// <see cref="Capabilities.GitCapability.PipelineStatus"/>.
    /// </summary>
    Task<Result<GitPipelineRun>> GetRunAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        string runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists pipeline runs, newest first. Requires
    /// <see cref="Capabilities.GitCapability.PipelineStatus"/>.
    /// </summary>
    Task<Result<IReadOnlyList<GitPipelineRun>>> ListRunsAsync(
        GitConnection connection,
        GitRepositoryRef repository,
        ListGitPipelineRuns query,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to trigger a pipeline run.
/// </summary>
public sealed record TriggerGitPipeline
{
    /// <summary>
    /// Gets the pipeline selector. GitHub: the workflow file name or id
    /// (e.g. <c>"bootstrap.yml"</c>); GitLab: the ref's <c>.gitlab-ci.yml</c>
    /// pipeline. Each adapter documents its mapping.
    /// </summary>
    public required string Pipeline { get; init; }

    /// <summary>
    /// Gets the git reference to run on — a branch or tag name, never a raw
    /// commit SHA (GitHub rejects one; to run against a specific commit, pass a
    /// branch/tag here and put the SHA in <see cref="Inputs"/>).
    /// </summary>
    public required string Reference { get; init; }

    /// <summary>
    /// Gets the input parameters passed to the pipeline, when it takes any.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Inputs { get; init; }
}

/// <summary>
/// The immediate result of triggering a pipeline.
/// </summary>
/// <param name="RunId">
/// The created run's identifier, when the provider reports one at trigger time;
/// <see langword="null"/> for fire-and-forget providers.
/// </param>
public sealed record GitPipelineRunHandle(string? RunId);

/// <summary>
/// A pipeline run as reported by the provider.
/// </summary>
/// <param name="Id">The run identifier.</param>
/// <param name="Pipeline">The pipeline the run belongs to.</param>
/// <param name="Status">The neutral run status.</param>
/// <param name="Reference">The git reference the run executed on.</param>
/// <param name="HtmlUrl">The web URL of the run.</param>
/// <param name="CreatedAt">When the run was created.</param>
public sealed record GitPipelineRun(
    string Id,
    string Pipeline,
    GitPipelineStatus Status,
    string Reference,
    string HtmlUrl,
    DateTimeOffset CreatedAt);

/// <summary>
/// The neutral status of a pipeline run. Adapters map provider states onto this
/// set and use <see cref="Unknown"/> for states with no equivalent.
/// </summary>
public enum GitPipelineStatus
{
    /// <summary>The run is waiting to start.</summary>
    Queued,

    /// <summary>The run is executing.</summary>
    Running,

    /// <summary>The run completed successfully.</summary>
    Succeeded,

    /// <summary>The run completed with a failure.</summary>
    Failed,

    /// <summary>The run was cancelled before completion.</summary>
    Cancelled,

    /// <summary>The run was skipped.</summary>
    Skipped,

    /// <summary>The provider reported a state with no neutral equivalent.</summary>
    Unknown,
}

/// <summary>
/// Filters for <see cref="IGitPipelineService.ListRunsAsync"/>.
/// </summary>
public sealed record ListGitPipelineRuns
{
    /// <summary>Gets the pipeline to filter on; all pipelines when null.</summary>
    public string? Pipeline { get; init; }

    /// <summary>Gets the git reference to filter on; all references when null.</summary>
    public string? Reference { get; init; }

    /// <summary>Gets the maximum number of runs returned (a single page). Defaults to 20.</summary>
    public int Limit { get; init; } = 20;
}
