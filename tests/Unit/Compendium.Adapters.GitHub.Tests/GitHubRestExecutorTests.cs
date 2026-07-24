// -----------------------------------------------------------------------
// <copyright file="GitHubRestExecutorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Http;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubRestExecutorTests
{
    private const string Token = "tok";

    [Fact]
    public async Task Get_DeserializesTheBody()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBodyAsJson(new { name = "widget" }));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Name.Should().Be("widget");
    }

    [Fact]
    public async Task Get_404WithRepositoryContext_MapsToRepositoryNotFound()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.ForRepository(new GitRepositoryRef("a", "b")),
            CancellationToken.None);

        result.Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task Get_404WithNamespaceContext_MapsToNamespaceNotFound()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.ForNamespace("acme"), CancellationToken.None);

        result.Error.Code.Should().Be("Git.NamespaceNotFound");
    }

    [Fact]
    public async Task Get_404WithNoContext_MapsToProviderRejected()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None);

        result.Error.Code.Should().Be("Git.ProviderRejected");
    }

    [Fact]
    public async Task Get_401_MapsToAuthenticationFailed()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(401));

        (await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None)).Error.Code
            .Should().Be("Git.AuthenticationFailed");
    }

    [Fact]
    public async Task Get_429_MapsToThrottled()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(429));

        (await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None)).Error.Code
            .Should().Be("Git.Throttled");
    }

    [Fact]
    public async Task Get_403RateLimited_MapsToThrottledWithRetryHint()
    {
        using var harness = new GitHubTestHarness();
        var reset = DateTimeOffset.UtcNow.AddSeconds(45).ToUnixTimeSeconds();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(403)
                .WithHeader("x-ratelimit-remaining", "0")
                .WithHeader("x-ratelimit-reset", reset.ToString()));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None);

        result.Error.Code.Should().Be("Git.Throttled");
        result.Error.Metadata.Should().ContainKey("retryAfterSeconds");
    }

    [Fact]
    public async Task Get_422AlreadyExists_MapsToConflict()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(422).WithBody("name already exists on this account"));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", new GitRestErrorContext { ConflictResource = "acme/x" }, CancellationToken.None);

        result.Error.Code.Should().Be("Git.Conflict");
    }

    [Fact]
    public async Task Get_500_MapsToProviderRejected()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(500).WithBody("boom"));

        var result = await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None);

        result.Error.Code.Should().Be("Git.ProviderRejected");
        result.Error.Metadata["statusCode"].Should().Be(500);
    }

    [Fact]
    public async Task Get_MalformedJson_MapsToMalformedResponse()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(200).WithBody("this is not json"));

        (await harness.RestExecutor.GetAsync<RestProbe>(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None)).Error.Code
            .Should().Be("GitHub.MalformedResponse");
    }

    [Fact]
    public async Task Send_NoBody_Succeeds()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));

        (await harness.RestExecutor.SendAsync(
            HttpMethod.Put, harness.BaseUri, Token, "thing", new { a = 1 }, GitRestErrorContext.None, CancellationToken.None))
            .IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteIdempotent_TreatsNotFoundAsSuccess()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));

        (await harness.RestExecutor.DeleteIdempotentAsync(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteIdempotent_PropagatesRealFailures()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/thing").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(500));

        (await harness.RestExecutor.DeleteIdempotentAsync(
            harness.BaseUri, Token, "thing", GitRestErrorContext.None, CancellationToken.None)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Get_NetworkError_MapsToNetworkError()
    {
        using var harness = new GitHubTestHarness();
        var unreachable = new Uri("http://127.0.0.1:1/");

        (await harness.RestExecutor.GetAsync<RestProbe>(
            unreachable, Token, "thing", GitRestErrorContext.None, CancellationToken.None)).Error.Code
            .Should().Be("GitHub.NetworkError");
    }

    private sealed class RestProbe
    {
        public string Name { get; init; } = string.Empty;
    }
}
