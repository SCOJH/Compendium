// -----------------------------------------------------------------------
// <copyright file="ScalewayTestHarness.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Connections;
using Compendium.Adapters.Scaleway.SecretManager.Configuration;
using Compendium.Adapters.Scaleway.SecretManager.Http;
using Compendium.Adapters.Scaleway.SecretManager.Services;
using Microsoft.Extensions.Options;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace Compendium.Adapters.Scaleway.SecretManager.Tests.Infrastructure;

/// <summary>
/// A WireMock-backed harness wiring the Scaleway adapter against a local mock
/// Secret Manager API. Disposable — stops the WireMock server.
/// </summary>
internal sealed class ScalewayTestHarness : IDisposable
{
    public const string ProjectId = "11111111-2222-3333-4444-555555555555";

    public ScalewayTestHarness()
    {
        Server = WireMockServer.Start();
        var options = new ScalewaySecretManagerOptions
        {
            ApiBaseUrl = new Uri(Server.Url!),
            DefaultRegion = "fr-par",
            DefaultProjectId = ProjectId,
        };
        Client = new ScalewayApiClient(new TestHttpClientFactory(), Options.Create(options));
        Containers = new ScalewaySecretContainerService(Client);
        Versions = new ScalewaySecretVersionService(Client);
        Vault = new ScalewaySecretVault(Containers, Versions);
    }

    public WireMockServer Server { get; }

    public ScalewayApiClient Client { get; }

    public ScalewaySecretContainerService Containers { get; }

    public ScalewaySecretVersionService Versions { get; }

    public ScalewaySecretVault Vault { get; }

    /// <summary>The regional path prefix every endpoint lives under.</summary>
    public static string Api(string suffix) => $"/secret-manager/v1beta1/regions/fr-par/{suffix}";

    /// <summary>A connection carrying a valid API token.</summary>
    public SecretVaultConnection Connection(string token = "scw_secret_key") => new()
    {
        Provider = "scaleway",
        Credential = new SecretVaultCredential.ApiToken(token),
    };

    public void Dispose() => Server.Stop();

    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}

/// <summary>
/// WireMock JSON response builders that set <c>Content-Type: application/json</c>.
/// </summary>
internal static class Json
{
    public static IResponseBuilder Ok(object body) => Status(200, body);

    public static IResponseBuilder Status(int statusCode, object body) =>
        Response.Create().WithStatusCode(statusCode)
            .WithHeader("Content-Type", "application/json").WithBodyAsJson(body);
}
