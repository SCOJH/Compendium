// -----------------------------------------------------------------------
// <copyright file="GitHubPipelineStatusMapperTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubPipelineStatusMapperTests
{
    [Theory]
    [InlineData("completed", "success", GitPipelineStatus.Succeeded)]
    [InlineData("completed", "failure", GitPipelineStatus.Failed)]
    [InlineData("completed", "timed_out", GitPipelineStatus.Failed)]
    [InlineData("completed", "startup_failure", GitPipelineStatus.Failed)]
    [InlineData("completed", "cancelled", GitPipelineStatus.Cancelled)]
    [InlineData("completed", "skipped", GitPipelineStatus.Skipped)]
    [InlineData("completed", "neutral", GitPipelineStatus.Unknown)]
    [InlineData("completed", null, GitPipelineStatus.Unknown)]
    [InlineData("queued", null, GitPipelineStatus.Queued)]
    [InlineData("requested", null, GitPipelineStatus.Queued)]
    [InlineData("waiting", null, GitPipelineStatus.Queued)]
    [InlineData("pending", null, GitPipelineStatus.Queued)]
    [InlineData("in_progress", null, GitPipelineStatus.Running)]
    [InlineData("something_else", null, GitPipelineStatus.Unknown)]
    [InlineData(null, null, GitPipelineStatus.Unknown)]
    public void Map_TranslatesStatusAndConclusion(string? status, string? conclusion, GitPipelineStatus expected)
    {
        GitHubPipelineStatusMapper.Map(status, conclusion).Should().Be(expected);
    }
}
