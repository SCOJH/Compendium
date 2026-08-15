// -----------------------------------------------------------------------
// <copyright file="GitHubAppTokenServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubAppTokenServiceTests
{
    private const string TokenPath = "/app/installations/555/access_tokens";

    [Fact]
    public void CreateAppJwt_SignsAnRs256JwtBoundToTheApp()
    {
        using var harness = new GitHubTestHarness();

        var jwt = harness.TokenService.CreateAppJwt(harness.Options.DefaultApp);

        jwt.IsSuccess.Should().BeTrue();
        var parts = jwt.Value.Split('.');
        parts.Should().HaveCount(3);

        var header = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[0]));
        header.GetProperty("alg").GetString().Should().Be("RS256");

        var payload = JsonSerializer.Deserialize<JsonElement>(Base64UrlDecode(parts[1]));
        payload.GetProperty("iss").GetString().Should().Be("1001");
        var exp = DateTimeOffset.FromUnixTimeSeconds(payload.GetProperty("exp").GetInt64());
        exp.Should().BeAfter(DateTimeOffset.UtcNow);
        exp.Should().BeBefore(DateTimeOffset.UtcNow.AddMinutes(10));

        // The signature verifies against the app's key.
        using var rsa = RSA.Create();
        rsa.ImportFromPem(harness.Options.DefaultApp.PrivateKeyPem);
        var signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        rsa.VerifyData(signingInput, Base64UrlDecode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .Should().BeTrue();
    }

    [Fact]
    public void CreateAppJwt_FailsWhenAppIdMissing()
    {
        using var harness = new GitHubTestHarness();
        var app = new GitHubAppRegistration { PrivateKeyPem = harness.Options.DefaultApp.PrivateKeyPem };

        harness.TokenService.CreateAppJwt(app).Error.Code.Should().Be("GitHubApp.AppIdMissing");
    }

    [Fact]
    public void CreateAppJwt_FailsWhenPrivateKeyMissing()
    {
        using var harness = new GitHubTestHarness();
        harness.TokenService.CreateAppJwt(new GitHubAppRegistration { AppId = "5" }).Error.Code
            .Should().Be("GitHubApp.PrivateKeyMissing");
    }

    [Fact]
    public void CreateAppJwt_FailsOnAMalformedPrivateKey()
    {
        using var harness = new GitHubTestHarness();
        var app = new GitHubAppRegistration { AppId = "5", PrivateKeyPem = "-----BEGIN PRIVATE KEY-----\nnope\n-----END PRIVATE KEY-----" };

        harness.TokenService.CreateAppJwt(app).Error.Code.Should().Be("GitHubApp.JwtSigningFailed");
    }

    [Fact]
    public async Task GetInstallationToken_ReturnsAUsableToken()
    {
        using var harness = new GitHubTestHarness();
        StubMint(harness, "ghs_installation");

        var result = await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, appKey: null, harness.BaseUri, "555", scope: null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("ghs_installation");
        result.Value.HttpBasicUsername.Should().Be("x-access-token");
        result.Value.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task GetInstallationToken_ServesTheCacheOnASecondCall()
    {
        using var harness = new GitHubTestHarness();
        StubMint(harness, "ghs_cached");

        await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);
        await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);

        harness.Server.LogEntries.Count().Should().Be(1, "the second call should hit the in-memory cache");
    }

    [Fact]
    public async Task GetInstallationToken_InvalidateForcesAReMint()
    {
        using var harness = new GitHubTestHarness();
        StubMint(harness, "ghs_x");

        await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);
        harness.TokenService.Invalidate(null, "555");
        await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);

        harness.Server.LogEntries.Count().Should().Be(2);
    }

    [Fact]
    public async Task GetInstallationToken_RetriesOnceOnUnauthorized()
    {
        using var harness = new GitHubTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(TokenPath).UsingPost())
            .InScenario("mint").WillSetStateTo("retried")
            .RespondWith(Response.Create().WithStatusCode(401));
        harness.Server
            .Given(Request.Create().WithPath(TokenPath).UsingPost())
            .InScenario("mint").WhenStateIs("retried")
            .RespondWith(TokenResponse("ghs_after_retry"));

        var result = await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Token.Should().Be("ghs_after_retry");
        harness.Server.LogEntries.Count().Should().Be(2);
    }

    [Fact]
    public async Task GetInstallationToken_ScopedMint_BypassesTheCacheAndSendsRepositories()
    {
        using var harness = new GitHubTestHarness();
        StubMint(harness, "ghs_scoped");
        var scope = new GitAccessTokenScope
        {
            Repositories = [new GitRepositoryRef("acme", "billing")],
            Permissions = new Dictionary<string, string> { ["contents"] = "read" },
        };

        await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", scope, CancellationToken.None);
        await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", scope, CancellationToken.None);

        harness.Server.LogEntries.Count().Should().Be(2, "scoped mints must not be cached");
        var body = harness.Server.LogEntries.First().RequestMessage.Body ?? string.Empty;
        body.Should().Contain("billing").And.Contain("contents");
    }

    [Fact]
    public async Task GetInstallationToken_RejectsAnEmptyInstallationId()
    {
        using var harness = new GitHubTestHarness();

        var result = await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, " ", null, CancellationToken.None);

        result.Error.Code.Should().Be("GitHubApp.InstallationIdRequired");
    }

    [Fact]
    public async Task GetInstallationToken_FailsOnAnEmptyTokenPayload()
    {
        using var harness = new GitHubTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(TokenPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(201).WithBodyAsJson(new { token = "", expires_at = DateTimeOffset.UtcNow }));

        var result = await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);

        result.Error.Code.Should().Be("GitHubApp.InstallationTokenMalformed");
    }

    [Fact]
    public async Task GetInstallationToken_MapsANonAuthFailure()
    {
        using var harness = new GitHubTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(TokenPath).UsingPost())
            .RespondWith(Response.Create().WithStatusCode(404));

        var result = await harness.TokenService.GetInstallationTokenAsync(
            harness.Options.DefaultApp, null, harness.BaseUri, "555", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
    }

    private static void StubMint(GitHubTestHarness harness, string token) =>
        harness.Server.Given(Request.Create().WithPath(TokenPath).UsingPost()).RespondWith(TokenResponse(token));

    private static IResponseBuilder TokenResponse(string token) =>
        Response.Create().WithStatusCode(201).WithBodyAsJson(new { token, expires_at = DateTimeOffset.UtcNow.AddHours(1) });

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch { 2 => padded + "==", 3 => padded + "=", _ => padded };
        return Convert.FromBase64String(padded);
    }
}
