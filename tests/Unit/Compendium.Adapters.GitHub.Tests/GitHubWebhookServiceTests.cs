// -----------------------------------------------------------------------
// <copyright file="GitHubWebhookServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubWebhookServiceTests
{
    private const string HookUrl = "https://platform.example/webhooks/git";
    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    private static EnsureGitWebhook Request_() => new()
    {
        Url = new Uri(HookUrl),
        Secret = "shh",
        Events = ["push"],
    };

    [Fact]
    public async Task Ensure_CreatesARepositoryHookWhenNoneMatches()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/hooks").UsingGet())
            .RespondWith(Json.Ok(Array.Empty<object>()));
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/hooks").UsingPost())
            .RespondWith(Json.Created(HookJson(10)));
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.EnsureAsync(harness.AppConnection(), new GitWebhookTarget.Repository(Repo), Request_());

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("10");
        result.Value.Url.Should().Be(new Uri(HookUrl));
    }

    [Fact]
    public async Task Ensure_UpdatesTheExistingHookMatchedByUrl()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/hooks").UsingGet())
            .RespondWith(Json.Ok(new[] { HookJson(10) }));
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/hooks/10").UsingPatch())
            .RespondWith(Json.Ok(HookJson(10)));
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.EnsureAsync(harness.AppConnection(), new GitWebhookTarget.Repository(Repo), Request_());

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("10");
    }

    [Fact]
    public async Task Ensure_CreatesAnOrganizationHook()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/hooks").UsingGet())
            .RespondWith(Json.Ok(Array.Empty<object>()));
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/hooks").UsingPost())
            .RespondWith(Json.Created(HookJson(20)));
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.EnsureAsync(harness.AppConnection(), new GitWebhookTarget.Namespace("acme"), Request_());

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("20");
    }

    [Fact]
    public async Task List_MapsRepositoryHooks()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/hooks").UsingGet())
            .RespondWith(Json.Ok(new[] { HookJson(10) }));
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListAsync(harness.AppConnection(), new GitWebhookTarget.Repository(Repo));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(s => s.Id == "10");
    }

    [Fact]
    public async Task Delete_RemovesTheHook()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/hooks/10").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(204));
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.DeleteAsync(harness.AppConnection(), new GitWebhookTarget.Repository(Repo), "10"))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Delete_NonNumericId_IsANoOpSuccess()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubWebhookService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.DeleteAsync(harness.AppConnection(), new GitWebhookTarget.Repository(Repo), "abc"))
            .IsSuccess.Should().BeTrue();
        harness.Server.LogEntries.Should().BeEmpty();
    }

    private static object HookJson(long id) => new
    {
        id,
        name = "web",
        active = true,
        events = new[] { "push" },
        config = new { url = HookUrl, content_type = "json" },
    };
}
