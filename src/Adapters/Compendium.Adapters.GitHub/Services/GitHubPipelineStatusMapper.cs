// -----------------------------------------------------------------------
// <copyright file="GitHubPipelineStatusMapper.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// Maps GitHub Actions workflow-run <c>status</c> and <c>conclusion</c> strings
/// onto the neutral <see cref="GitPipelineStatus"/>. Shared by the pipeline
/// service (run reads) and the webhook ingestor (workflow_run deliveries).
/// </summary>
internal static class GitHubPipelineStatusMapper
{
    /// <summary>
    /// Maps a run's status and (when completed) conclusion onto the neutral status.
    /// </summary>
    public static GitPipelineStatus Map(string? status, string? conclusion)
    {
        if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            return conclusion?.ToLowerInvariant() switch
            {
                "success" => GitPipelineStatus.Succeeded,
                "failure" or "timed_out" or "startup_failure" => GitPipelineStatus.Failed,
                "cancelled" => GitPipelineStatus.Cancelled,
                "skipped" => GitPipelineStatus.Skipped,
                _ => GitPipelineStatus.Unknown,
            };
        }

        return status?.ToLowerInvariant() switch
        {
            "queued" or "requested" or "pending" or "waiting" => GitPipelineStatus.Queued,
            "in_progress" => GitPipelineStatus.Running,
            _ => GitPipelineStatus.Unknown,
        };
    }
}
