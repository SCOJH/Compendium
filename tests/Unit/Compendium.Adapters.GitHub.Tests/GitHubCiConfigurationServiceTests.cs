// -----------------------------------------------------------------------
// <copyright file="GitHubCiConfigurationServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubCiConfigurationServiceTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");
    private readonly string _publicKey = Convert.ToBase64String(Sodium.PublicKeyBox.GenerateKeyPair().PublicKey);

    private GitHubCiConfigurationService Service(GitHubTestHarness harness) =>
        new(harness.Broker, harness.RestExecutor, harness.Sealer);

    [Fact]
    public async Task SetSecrets_RepositoryScope_SealsAndUploadsEach()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/secrets/public-key").UsingGet())
            .RespondWith(Json.Ok(new { key_id = "kid", key = _publicKey }));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/secrets/TOKEN").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await Service(harness).SetSecretsAsync(
            harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["TOKEN"] = "value" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task SetSecrets_NamespaceScope_UsesOrgVisibility()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/orgs/acme/actions/secrets/public-key").UsingGet())
            .RespondWith(Json.Ok(new { key_id = "kid", key = _publicKey }));
        harness.Server.Given(Request.Create().WithPath("/orgs/acme/actions/secrets/TOKEN").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await Service(harness).SetSecretsAsync(
            harness.PatConnection(), new GitConfigurationScope.Namespace("acme"),
            new Dictionary<string, string> { ["TOKEN"] = "value" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var body = harness.Server.LogEntries.Last(e => e.RequestMessage.Method == "PUT").RequestMessage.Body ?? string.Empty;
        body.Should().Contain("visibility");
    }

    [Fact]
    public async Task SetSecrets_EnvironmentScope_ResolvesTheRepositoryId()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing").UsingGet())
            .RespondWith(Json.Ok(new { id = 4242 }));
        harness.Server.Given(Request.Create().WithPath("/repositories/4242/environments/prod/secrets/public-key").UsingGet())
            .RespondWith(Json.Ok(new { key_id = "kid", key = _publicKey }));
        harness.Server.Given(Request.Create().WithPath("/repositories/4242/environments/prod/secrets/TOKEN").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await Service(harness).SetSecretsAsync(
            harness.PatConnection(), new GitConfigurationScope.Environment(Repo, "prod"),
            new Dictionary<string, string> { ["TOKEN"] = "value" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task SetSecrets_PropagatesAPublicKeyFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/secrets/public-key").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await Service(harness).SetSecretsAsync(
            harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["TOKEN"] = "value" });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteSecret_IsIdempotent()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/secrets/GONE").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));

        (await Service(harness).DeleteSecretAsync(
            harness.PatConnection(), new GitConfigurationScope.Repository(Repo), "GONE")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SetVariables_CreatesANewVariable()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201));

        var result = await Service(harness).SetVariablesAsync(
            harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["REGION"] = "eu" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task SetVariables_UpdatesOnConflict()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables").UsingPost())
            .RespondWith(Response.Create().WithStatusCode(409).WithBody("variable already exists"));
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables/REGION").UsingPatch())
            .RespondWith(Response.Create().WithStatusCode(204));

        var result = await Service(harness).SetVariablesAsync(
            harness.PatConnection(), new GitConfigurationScope.Repository(Repo),
            new Dictionary<string, string> { ["REGION"] = "eu" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task DeleteVariable_IsIdempotent()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/repos/acme/billing/actions/variables/GONE").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));

        (await Service(harness).DeleteVariableAsync(
            harness.PatConnection(), new GitConfigurationScope.Repository(Repo), "GONE")).IsSuccess.Should().BeTrue();
    }
}
