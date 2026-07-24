// -----------------------------------------------------------------------
// <copyright file="GitPipelineModelsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Pipelines;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for the pipeline DTO records: the trigger request, the
/// fire-and-forget run handle, the reported run, and the list filters.
/// </summary>
public sealed class GitPipelineModelsTests
{
    [Fact]
    public void TriggerGitPipeline_DefaultsInputsToNull()
    {
        // Arrange / Act
        var request = new TriggerGitPipeline { Pipeline = "bootstrap.yml", Reference = "main" };

        // Assert
        request.Pipeline.Should().Be("bootstrap.yml");
        request.Reference.Should().Be("main");
        request.Inputs.Should().BeNull();
    }

    [Fact]
    public void TriggerGitPipeline_CarriesInputs()
    {
        // Arrange / Act
        var request = new TriggerGitPipeline
        {
            Pipeline = "bootstrap.yml",
            Reference = "main",
            Inputs = new Dictionary<string, string> { ["sha"] = "abc123" },
        };

        // Assert
        request.Inputs.Should().ContainKey("sha").WhoseValue.Should().Be("abc123");
    }

    [Fact]
    public void GitPipelineRunHandle_AllowsNullRunIdForFireAndForgetProviders()
    {
        // Arrange / Act
        var withId = new GitPipelineRunHandle("run-1");
        var withoutId = new GitPipelineRunHandle(RunId: null);

        // Assert
        withId.RunId.Should().Be("run-1");
        withoutId.RunId.Should().BeNull();
    }

    [Fact]
    public void GitPipelineRun_ProjectsAllProperties()
    {
        // Arrange
        var createdAt = DateTimeOffset.UtcNow;

        // Act
        var run = new GitPipelineRun("run-1", "bootstrap.yml", GitPipelineStatus.Running, "main", "https://x.invalid/runs/run-1", createdAt);
        var (id, pipeline, status, reference, html, at) = run;

        // Assert
        id.Should().Be("run-1");
        pipeline.Should().Be("bootstrap.yml");
        status.Should().Be(GitPipelineStatus.Running);
        reference.Should().Be("main");
        html.Should().Be("https://x.invalid/runs/run-1");
        at.Should().Be(createdAt);
    }

    [Fact]
    public void ListGitPipelineRuns_DefaultsLimitTo20AndFiltersToNull()
    {
        // Arrange / Act
        var query = new ListGitPipelineRuns();

        // Assert
        query.Pipeline.Should().BeNull();
        query.Reference.Should().BeNull();
        query.Limit.Should().Be(20);
    }

    [Fact]
    public void ListGitPipelineRuns_CarriesFiltersAndLimit()
    {
        // Arrange / Act
        var query = new ListGitPipelineRuns { Pipeline = "build.yml", Reference = "main", Limit = 5 };

        // Assert
        query.Pipeline.Should().Be("build.yml");
        query.Reference.Should().Be("main");
        query.Limit.Should().Be(5);
    }
}
