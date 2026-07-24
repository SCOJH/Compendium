// -----------------------------------------------------------------------
// <copyright file="GitHubBranchPolicyServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubBranchPolicyServiceTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    private GitHubBranchPolicyService Service(GitHubTestHarness harness) => new(harness.Broker, harness.RestExecutor);

    [Fact]
    public async Task Apply_CreatesARulesetWhenNoneMatches()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingGet())
            .RespondWith(Json.Ok(Array.Empty<object>()));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingPost())
            .RespondWith(Json.Created(new { id = 7, name = "compendium:main" }));

        var result = await Service(harness).ApplyAsync(harness.PatConnection(), Repo, new GitBranchPolicyRequest
        {
            Pattern = "main",
            RequiredApprovals = 2,
            RequiredStatusChecks = ["build"],
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("7");
        result.Value.Pattern.Should().Be("main");
    }

    [Fact]
    public async Task Apply_UpdatesTheExistingRulesetForThePattern()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingGet())
            .RespondWith(Json.Ok(new[] { new { id = 7, name = "compendium:main" } }));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets/7").UsingPut())
            .RespondWith(Json.Ok(new { id = 7, name = "compendium:main" }));

        var result = await Service(harness).ApplyAsync(harness.PatConnection(), Repo, new GitBranchPolicyRequest
        {
            Pattern = "main",
            EnforceForAdmins = true,
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("7");
    }

    [Fact]
    public async Task Remove_IsIdempotent()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets/7").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));

        (await Service(harness).RemoveAsync(harness.PatConnection(), Repo, "7")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task List_RecoversThePatternFromTheRulesetName()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingGet())
            .RespondWith(Json.Ok(new[]
            {
                new { id = 7, name = "compendium:release/*" },
                new { id = 8, name = "hand-authored" },
            }));

        var result = await Service(harness).ListAsync(harness.PatConnection(), Repo);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().Contain(p => p.Pattern == "release/*");
        result.Value.Should().Contain(p => p.Pattern == "hand-authored");
    }
}
