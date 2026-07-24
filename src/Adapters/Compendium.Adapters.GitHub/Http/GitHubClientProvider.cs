// -----------------------------------------------------------------------
// <copyright file="GitHubClientProvider.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Compendium.Adapters.GitHub.Auth;
using Octokit;

namespace Compendium.Adapters.GitHub.Http;

/// <summary>
/// Produces authenticated Octokit clients for a connection.
/// </summary>
internal interface IGitHubClientProvider
{
    /// <summary>Mints a token for the connection and returns an Octokit client carrying it.</summary>
    Task<Result<IGitHubClient>> GetClientAsync(GitConnection connection, CancellationToken cancellationToken);
}

/// <summary>
/// Produces authenticated Octokit clients for a connection. A
/// <see cref="GitHubClient"/> is cached per credential identity and target host
/// (so repeated calls do not exhaust sockets), and its credentials are refreshed
/// from a freshly minted token on every retrieval — installation tokens rotate
/// roughly hourly, and the mint is cheap because the broker caches them.
/// </summary>
internal sealed class GitHubClientProvider : IGitHubClientProvider
{
    private readonly GitHubCredentialBroker _broker;
    private readonly ConcurrentDictionary<string, GitHubClient> _clients = new(StringComparer.Ordinal);

    public GitHubClientProvider(GitHubCredentialBroker broker)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    /// <inheritdoc />
    public async Task<Result<IGitHubClient>> GetClientAsync(GitConnection connection, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);

        var token = await _broker.MintAsync(connection, scope: null, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            return Result.Failure<IGitHubClient>(token.Error);
        }

        var client = _clients.GetOrAdd(CacheKey(connection), _ => Build(connection));
        client.Credentials = new Credentials(token.Value.Token);
        return Result.Success<IGitHubClient>(client);
    }

    private static GitHubClient Build(GitConnection connection)
    {
        var product = new ProductHeaderValue(GitHubDefaults.ProductName);
        return connection.ServerUrl is null
            ? new GitHubClient(product)
            : new GitHubClient(product, GitHubDefaults.EnsureTrailingSlash(connection.ServerUrl));
    }

    private static string CacheKey(GitConnection connection)
    {
        var host = connection.ServerUrl?.AbsoluteUri ?? "github.com";
        var identity = connection.Credential switch
        {
            GitCredential.AppInstallation app => $"app:{app.AppKey ?? "default"}:{app.InstallationId}",
            GitCredential.ServiceAccountToken sat => $"tok:{Fingerprint(sat.Token)}",
            GitCredential.PersonalAccessToken pat => $"tok:{Fingerprint(pat.Token)}",
            GitCredential.OAuthAccessToken oauth => $"tok:{Fingerprint(oauth.AccessToken)}",
            _ => "unknown",
        };

        return $"{identity}@{host}";
    }

    // A non-reversible fingerprint so the cache key never carries raw token material.
    private static string Fingerprint(string token)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash.AsSpan(0, 6));
    }
}
