// -----------------------------------------------------------------------
// <copyright file="GitHubTestHarness.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using Compendium.Adapters.GitHub.Auth;
using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Http;
using Compendium.Adapters.GitHub.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WireMock.Server;

namespace Compendium.Adapters.GitHub.Tests.Infrastructure;

/// <summary>
/// A WireMock-backed harness that wires the adapter's REST-facing collaborators
/// (token service, REST executor, credential broker) against a local mock GitHub
/// API. Disposable — stops the WireMock server.
/// </summary>
internal sealed class GitHubTestHarness : IDisposable
{
    public GitHubTestHarness()
    {
        Server = WireMockServer.Start();
        BaseUri = new Uri(Server.Url!);

        var pem = CreateRsaPrivateKeyPem();
        Options = new GitHubAdapterOptions
        {
            ApiBaseUrl = BaseUri,
            DefaultApp = new GitHubAppRegistration
            {
                AppId = "1001",
                AppSlug = "compendium-app",
                PrivateKeyPem = pem,
                WebhookSecret = "hook-secret",
            },
        };

        HttpClientFactory = new TestHttpClientFactory();
        RestExecutor = new GitHubRestExecutor(HttpClientFactory);
        TokenService = new GitHubAppTokenService(HttpClientFactory, NullLogger<GitHubAppTokenService>.Instance);
        Broker = new GitHubCredentialBroker(MicrosoftOptions.Create(Options), TokenService, RestExecutor);
        Sealer = new GitHubSecretSealer();
    }

    public WireMockServer Server { get; }

    public Uri BaseUri { get; }

    public GitHubAdapterOptions Options { get; }

    public TestHttpClientFactory HttpClientFactory { get; }

    public GitHubRestExecutor RestExecutor { get; }

    public GitHubAppTokenService TokenService { get; }

    public GitHubCredentialBroker Broker { get; }

    public GitHubSecretSealer Sealer { get; }

    /// <summary>A connection carrying a personal access token (pass-through mint, no HTTP).</summary>
    public GitConnection PatConnection(string token = "pat_test") => new()
    {
        Provider = "github",
        ServerUrl = BaseUri,
        Credential = new GitCredential.PersonalAccessToken(token),
    };

    /// <summary>A connection carrying an app installation (mints via the token service).</summary>
    public GitConnection AppConnection(string installationId = "555") => new()
    {
        Provider = "github",
        ServerUrl = BaseUri,
        Credential = new GitCredential.AppInstallation(installationId),
    };

    /// <summary>A real Octokit client pointed at the WireMock server.</summary>
    public Octokit.IGitHubClient OctokitClient() =>
        new Octokit.GitHubClient(new Octokit.ProductHeaderValue("compendium-test"), BaseUri)
        {
            Credentials = new Octokit.Credentials("test-token"),
        };

    public void Dispose() => Server.Stop();

    private static string CreateRsaPrivateKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}

/// <summary>Static alias so the harness can new up <see cref="IOptions{T}"/> without a name clash.</summary>
internal static class MicrosoftOptions
{
    public static IOptions<T> Create<T>(T value)
        where T : class, new() => Microsoft.Extensions.Options.Options.Create(value);
}
