// -----------------------------------------------------------------------
// <copyright file="GitHubCredentialBrokerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubCredentialBrokerTests
{
    [Fact]
    public async Task Mint_PassesThroughAPersonalAccessToken_WithAFarFutureExpiry()
    {
        using var harness = new GitHubTestHarness();

        var result = await harness.Broker.MintAsync(harness.PatConnection("pat_abc"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("pat_abc");
        result.Value.HttpBasicUsername.Should().Be("x-access-token");
        result.Value.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddYears(5));
    }

    [Fact]
    public async Task Mint_PassesThroughAServiceAccountToken()
    {
        using var harness = new GitHubTestHarness();
        var connection = harness.PatConnection() with { Credential = new GitCredential.ServiceAccountToken("bot_tok") };

        (await harness.Broker.MintAsync(connection)).Value.Token.Should().Be("bot_tok");
    }

    [Fact]
    public async Task Mint_OAuthToken_ReportsAnEightHourExpiry()
    {
        using var harness = new GitHubTestHarness();
        var connection = harness.PatConnection() with { Credential = new GitCredential.OAuthAccessToken("oauth_tok") };

        var result = await harness.Broker.MintAsync(connection);

        result.Value.ExpiresAt.Should().BeBefore(DateTimeOffset.UtcNow.AddHours(9));
        result.Value.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddHours(7));
    }

    [Fact]
    public async Task Mint_AppInstallation_MintsViaTheTokenService()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/app/installations/555/access_tokens").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201)
                .WithBodyAsJson(new { token = "ghs_from_app", expires_at = DateTimeOffset.UtcNow.AddHours(1) }));

        (await harness.Broker.MintAsync(harness.AppConnection())).Value.Token.Should().Be("ghs_from_app");
    }

    [Fact]
    public async Task Mint_AppInstallation_UnknownAppKey_FailsNotConfigured()
    {
        using var harness = new GitHubTestHarness();
        var connection = harness.AppConnection() with { Credential = new GitCredential.AppInstallation("555", "missing") };

        (await harness.Broker.MintAsync(connection)).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task Validate_AppInstallation_ReportsTheInstalledAccount()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/app/installations/555").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                id = 555,
                account = new { login = "acme", type = "Organization", name = "Acme Inc" },
            }));

        var result = await harness.Broker.ValidateAsync(harness.AppConnection());

        result.IsSuccess.Should().BeTrue();
        result.Value.AccountLogin.Should().Be("acme");
        result.Value.AccountType.Should().Be(GitAccountType.Organization);
        result.Value.DisplayName.Should().Be("Acme Inc");
    }

    [Fact]
    public async Task Validate_TokenCredential_ReadsTheAuthenticatedUser()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/user").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { login = "octocat", type = "User" }));

        var result = await harness.Broker.ValidateAsync(harness.PatConnection());

        result.Value.AccountLogin.Should().Be("octocat");
        result.Value.AccountType.Should().Be(GitAccountType.User);
    }

    [Fact]
    public async Task Validate_TokenCredential_PropagatesAnAuthFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/user").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        (await harness.Broker.ValidateAsync(harness.PatConnection())).Error.Code.Should().Be("Git.AuthenticationFailed");
    }

    [Fact]
    public async Task ResolveAppInstallation_FindsAnOrganizationInstallation()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/orgs/acme/installation").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { id = 999, account = new { login = "acme", type = "Organization" } }));

        var result = await harness.Broker.ResolveAppInstallationAsync("acme");

        result.IsSuccess.Should().BeTrue();
        result.Value.InstallationId.Should().Be("999");
        result.Value.AccountLogin.Should().Be("acme");
    }

    [Fact]
    public async Task ResolveAppInstallation_FallsBackToTheUserInstallation()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/orgs/octocat/installation").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        harness.Server.Given(Request.Create().WithPath("/users/octocat/installation").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { id = 42, account = new { login = "octocat", type = "User" } }));

        var result = await harness.Broker.ResolveAppInstallationAsync("octocat");

        result.Value.InstallationId.Should().Be("42");
        result.Value.AccountType.Should().Be(GitAccountType.User);
    }

    [Fact]
    public async Task ResolveAppInstallation_NotInstalled_ReturnsAnInstallUrl()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/orgs/ghost/installation").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        harness.Server.Given(Request.Create().WithPath("/users/ghost/installation").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await harness.Broker.ResolveAppInstallationAsync("ghost");

        result.Error.Code.Should().Be("Git.AppNotInstalled");
        result.Error.Metadata["installUrl"].Should().Be("https://github.com/apps/compendium-app/installations/new");
    }

    [Fact]
    public async Task ResolveAppInstallationById_FindsTheInstallation()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/app/installations/555").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new
            {
                id = 555,
                account = new { login = "acme", type = "Organization" },
                suspended_at = "2026-01-02T03:04:05Z",
            }));

        var result = await harness.Broker.ResolveAppInstallationByIdAsync("555");

        result.IsSuccess.Should().BeTrue();
        result.Value.InstallationId.Should().Be("555");
        result.Value.AccountLogin.Should().Be("acme");
        result.Value.AccountType.Should().Be(GitAccountType.Organization);
        result.Value.Suspended.Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAppInstallationById_MapsAUserAccount_AndReportsNotSuspended()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/app/installations/42").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new { id = 42, account = new { login = "octocat", type = "User" } }));

        var result = await harness.Broker.ResolveAppInstallationByIdAsync("42");

        result.Value.AccountType.Should().Be(GitAccountType.User);
        result.Value.Suspended.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAppInstallationById_WhenUnknown_FailsInstallationNotFound()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/app/installations/999").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await harness.Broker.ResolveAppInstallationByIdAsync("999");

        result.Error.Code.Should().Be("Git.InstallationNotFound");
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task ResolveAppInstallationById_UnknownAppKey_FailsNotConfigured()
    {
        using var harness = new GitHubTestHarness();

        (await harness.Broker.ResolveAppInstallationByIdAsync("555", "missing"))
            .Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task ListAppInstallations_PagesThroughEveryInstallation()
    {
        using var harness = new GitHubTestHarness();
        var firstPage = Enumerable.Range(1, 100)
            .Select(i => new { id = i, account = new { login = $"org{i}", type = "Organization" } }).ToArray();
        harness.Server.Given(Request.Create().WithPath("/app/installations").WithParam("page", "1").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(firstPage));
        harness.Server.Given(Request.Create().WithPath("/app/installations").WithParam("page", "2").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200)
                .WithBodyAsJson(new[] { new { id = 101, account = new { login = "last", type = "Organization" } } }));

        var result = await harness.Broker.ListAppInstallationsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(101);
        result.Value[^1].AccountLogin.Should().Be("last");
    }

    [Fact]
    public async Task Validate_AppInstallation_UnknownAppKey_FailsNotConfigured()
    {
        using var harness = new GitHubTestHarness();
        var connection = harness.AppConnection() with { Credential = new GitCredential.AppInstallation("1", "missing") };

        (await harness.Broker.ValidateAsync(connection)).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task ResolveAppInstallation_UnknownAppKey_FailsNotConfigured()
    {
        using var harness = new GitHubTestHarness();

        (await harness.Broker.ResolveAppInstallationAsync("acme", "missing")).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task ListAppInstallations_UnknownAppKey_FailsNotConfigured()
    {
        using var harness = new GitHubTestHarness();

        (await harness.Broker.ListAppInstallationsAsync("missing")).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task ListAppInstallations_PropagatesAPageFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/app/installations").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500));

        (await harness.Broker.ListAppInstallationsAsync()).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Authorize_PropagatesAMintFailure()
    {
        using var harness = new GitHubTestHarness();
        var connection = harness.AppConnection() with { Credential = new GitCredential.AppInstallation("1", "missing") };

        (await harness.Broker.AuthorizeAsync(connection)).Error.Code.Should().Be("Git.NotConfigured");
    }

    [Fact]
    public async Task Authorize_PairsAMintedTokenWithTheApiBase()
    {
        using var harness = new GitHubTestHarness();

        var result = await harness.Broker.AuthorizeAsync(harness.PatConnection("pat_xyz"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("pat_xyz");
        result.Value.ApiBase.Should().Be(GitHubDefaults.EnsureTrailingSlash(harness.BaseUri));
        result.Value.ToString().Should().NotContain("pat_xyz");
    }
}
