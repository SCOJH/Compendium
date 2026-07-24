// -----------------------------------------------------------------------
// <copyright file="GitHubEnvironmentServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubEnvironmentServiceTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    private GitHubEnvironmentService Service(GitHubTestHarness harness) => new(harness.Broker, harness.RestExecutor);

    [Fact]
    public async Task Ensure_CreatesOrUpdatesTheEnvironment()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/environments/production").UsingPut())
            .RespondWith(Json.Ok(new { name = "production", html_url = "https://github.com/acme/billing/deployments/activity_log?environments_filter=production" }));

        var result = await Service(harness).EnsureAsync(harness.PatConnection(), Repo, new EnsureGitEnvironment { Name = "production" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Name.Should().Be("production");
        result.Value.HtmlUrl.Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_IsIdempotent()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/environments/gone").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));

        (await Service(harness).DeleteAsync(harness.PatConnection(), Repo, "gone")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task List_MapsEnvironments()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/environments").UsingGet())
            .RespondWith(Json.Ok(new
            {
                total_count = 1,
                environments = new[] { new { name = "production", html_url = "https://x" } },
            }));

        var result = await Service(harness).ListAsync(harness.PatConnection(), Repo);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(e => e.Name == "production");
    }
}
