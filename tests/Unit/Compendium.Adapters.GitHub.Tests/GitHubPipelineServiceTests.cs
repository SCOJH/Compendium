// -----------------------------------------------------------------------
// <copyright file="GitHubPipelineServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubPipelineServiceTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    [Fact]
    public async Task Trigger_ReturnsAHandleWithNoRunId()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create()
                .WithPath("/api/v3/repos/acme/billing/actions/workflows/bootstrap.yml/dispatches").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(204));
        var service = new GitHubPipelineService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.TriggerAsync(harness.AppConnection(), Repo, new TriggerGitPipeline
        {
            Pipeline = "bootstrap.yml",
            Reference = "main",
            Inputs = new Dictionary<string, string> { ["version"] = "1.2.3" },
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.RunId.Should().BeNull();
    }

    [Fact]
    public async Task GetRun_MapsStatusAndConclusion()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/actions/runs/999").UsingGet())
            .RespondWith(Json.Ok(RunJson(999, "completed", "success")));
        var service = new GitHubPipelineService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.GetRunAsync(harness.AppConnection(), Repo, "999");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("999");
        result.Value.Status.Should().Be(GitPipelineStatus.Succeeded);
        result.Value.Reference.Should().Be("main");
    }

    [Fact]
    public async Task GetRun_RejectsANonNumericRunId()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubPipelineService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.GetRunAsync(harness.AppConnection(), Repo, "not-a-number")).Error.Code
            .Should().Be("GitHub.InvalidRunId");
    }

    [Fact]
    public async Task ListRuns_AllWorkflows_MapsAndFiltersByReference()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/actions/runs").UsingGet())
            .RespondWith(Json.Ok(new
            {
                total_count = 2,
                workflow_runs = new[] { RunJson(1, "completed", "success", "main"), RunJson(2, "in_progress", null, "dev") },
            }));
        var service = new GitHubPipelineService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListRunsAsync(harness.AppConnection(), Repo, new ListGitPipelineRuns { Reference = "main" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(r => r.Id == "1");
    }

    [Fact]
    public async Task ListRuns_ByWorkflow_UsesTheWorkflowScopedEndpoint()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create()
                .WithPath("/api/v3/repos/acme/billing/actions/workflows/ci.yml/runs").UsingGet())
            .RespondWith(Json.Ok(new { total_count = 1, workflow_runs = new[] { RunJson(3, "queued", null) } }));
        var service = new GitHubPipelineService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListRunsAsync(harness.AppConnection(), Repo, new ListGitPipelineRuns { Pipeline = "ci.yml" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(r => r.Id == "3" && r.Status == GitPipelineStatus.Queued);
    }

    private static object RunJson(long id, string status, string? conclusion, string headBranch = "main") => new
    {
        id,
        name = "CI",
        status,
        conclusion,
        head_branch = headBranch,
        html_url = $"https://github.com/acme/billing/actions/runs/{id}",
        created_at = "2026-07-24T00:00:00Z",
    };
}
