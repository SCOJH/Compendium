// -----------------------------------------------------------------------
// <copyright file="ServiceErrorPathTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

/// <summary>
/// Auth-failure, provider-error, and edge-case paths across the services — the
/// branches the happy-path suites do not reach.
/// </summary>
public sealed class ServiceErrorPathTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    // A connection whose credential cannot mint a token (unknown app registration).
    private static GitConnection BadAuth(GitHubTestHarness harness) =>
        harness.AppConnection() with { Credential = new GitCredential.AppInstallation("1", "missing") };

    // ---- REST services: authorization failures ----

    [Fact]
    public async Task CiConfiguration_SetSecrets_PropagatesAuthFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubCiConfigurationService(harness.Broker, harness.RestExecutor, harness.Sealer);

        (await service.SetSecretsAsync(BadAuth(harness), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["A"] = "b" })).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task CiConfiguration_SetSecrets_PropagatesAnUploadFailure()
    {
        using var harness = new GitHubTestHarness();
        var publicKey = Convert.ToBase64String(Sodium.PublicKeyBox.GenerateKeyPair().PublicKey);
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/secrets/public-key").UsingGet())
            .RespondWith(Json.Ok(new { key_id = "kid", key = publicKey }));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/secrets/A").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(500));
        var service = new GitHubCiConfigurationService(harness.Broker, harness.RestExecutor, harness.Sealer);

        (await service.SetSecretsAsync(harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["A"] = "b" })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task CiConfiguration_EnvironmentScope_PropagatesRepositoryIdFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        var service = new GitHubCiConfigurationService(harness.Broker, harness.RestExecutor, harness.Sealer);

        (await service.SetSecretsAsync(harness.PatConnection(), new GitConfigurationScope.Environment(Repo, "prod"),
            new Dictionary<string, string> { ["A"] = "b" })).Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task CiConfiguration_SetVariables_NonConflictFailure_Propagates()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(500));
        var service = new GitHubCiConfigurationService(harness.Broker, harness.RestExecutor, harness.Sealer);

        (await service.SetVariablesAsync(harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["A"] = "b" })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task CiConfiguration_SetVariables_UpdateFailureAfterConflict_Propagates()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409).WithBody("already exists"));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables/A").UsingPatch())
            .RespondWith(Response.Create().WithStatusCode(500));
        var service = new GitHubCiConfigurationService(harness.Broker, harness.RestExecutor, harness.Sealer);

        (await service.SetVariablesAsync(harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["A"] = "b" })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Environment_Ensure_PropagatesAuthFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubEnvironmentService(harness.Broker, harness.RestExecutor);

        (await service.EnsureAsync(BadAuth(harness), Repo, new EnsureGitEnvironment { Name = "prod" }))
            .Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task Environment_List_PropagatesProviderFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/environments").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));
        var service = new GitHubEnvironmentService(harness.Broker, harness.RestExecutor);

        (await service.ListAsync(harness.PatConnection(), Repo)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task BranchPolicy_Apply_PropagatesAuthFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubBranchPolicyService(harness.Broker, harness.RestExecutor);

        (await service.ApplyAsync(BadAuth(harness), Repo, new GitBranchPolicyRequest { Pattern = "main" }))
            .Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task BranchPolicy_Apply_PropagatesListFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));
        var service = new GitHubBranchPolicyService(harness.Broker, harness.RestExecutor);

        (await service.ApplyAsync(harness.PatConnection(), Repo, new GitBranchPolicyRequest { Pattern = "main" }))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task BranchPolicy_Apply_WithAllRuleOptions_BuildsTheRuleset()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingGet())
            .RespondWith(Json.Ok(Array.Empty<object>()));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/rulesets").UsingPost())
            .RespondWith(Json.Created(new { id = 1, name = "compendium:release/*" }));
        var service = new GitHubBranchPolicyService(harness.Broker, harness.RestExecutor);

        var result = await service.ApplyAsync(harness.PatConnection(), Repo, new GitBranchPolicyRequest
        {
            Pattern = "release/*",
            RequirePullRequest = false,
            BlockForcePush = false,
            BlockDeletion = false,
            RequireLinearHistory = true,
            EnforceForAdmins = true,
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var body = harness.Server.LogEntries.Last(e => e.RequestMessage.Method == "POST").RequestMessage.Body ?? string.Empty;
        body.Should().Contain("required_linear_history");
        body.Should().NotContain("bypass_actors");
    }

    // ---- Octokit services: client-provider failures and error mapping ----

    [Fact]
    public async Task Pipeline_Trigger_PropagatesAClientProviderFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubPipelineService(StubClientProvider.Failing(GitErrors.NotConfigured("github")));

        (await service.TriggerAsync(harness.AppConnection(), Repo, new TriggerGitPipeline
        {
            Pipeline = "ci.yml",
            Reference = "main",
        })).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task AccessControl_EnsureTeam_MapsAProviderError()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/teams/eng").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.EnsureTeamAsync(harness.AppConnection(), "acme", new EnsureGitTeam { Name = "eng" }))
            .Error.Code.Should().Be("Git.ProviderRejected");
    }

    [Fact]
    public async Task AccessControl_PropagatesAClientProviderFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubAccessControlService(StubClientProvider.Failing(GitErrors.NotConfigured("github")));

        (await service.SetUserRepositoryRoleAsync(harness.AppConnection(), Repo, "bob", GitRepositoryRole.Read))
            .Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task Webhook_PropagatesAClientProviderFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubWebhookService(StubClientProvider.Failing(GitErrors.NotConfigured("github")));

        (await service.ListAsync(harness.AppConnection(), new GitWebhookTarget.Repository(Repo)))
            .Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task Webhook_DeletesAndListsOrganizationHooks()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/hooks/9").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/hooks").UsingGet())
            .RespondWith(Json.Ok(new[]
            {
                new { id = 9, name = "web", active = true, events = new[] { "push" }, config = new { url = "https://p/x" } },
            }));
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));
        var target = new GitWebhookTarget.Namespace("acme");

        (await service.DeleteAsync(harness.AppConnection(), target, "9")).IsSuccess.Should().BeTrue();
        (await service.ListAsync(harness.AppConnection(), target)).Value.Should().ContainSingle(s => s.Id == "9");
    }
}
