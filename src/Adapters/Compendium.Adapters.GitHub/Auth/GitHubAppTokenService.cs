// -----------------------------------------------------------------------
// <copyright file="GitHubAppTokenService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Http;
using Microsoft.Extensions.Logging;

namespace Compendium.Adapters.GitHub.Auth;

/// <summary>
/// Mints GitHub App credentials: signs App-level JWTs (RS256) with a registration's
/// private key and exchanges them for installation access tokens via
/// <c>POST /app/installations/{id}/access_tokens</c>. Unscoped tokens are cached
/// per (app, installation) and refreshed shortly before expiry; a stale-JWT 401 on
/// mint is retried once with a freshly signed JWT. Independent of Octokit so the
/// minted token works against REST, GraphQL, and git-over-HTTPS alike.
/// </summary>
internal sealed class GitHubAppTokenService
{
    private static readonly TimeSpan JwtLifetime = TimeSpan.FromMinutes(9);
    private static readonly TimeSpan JwtBackdate = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RefreshSkew = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GitHubAppTokenService> _logger;
    private readonly ConcurrentDictionary<string, GitAccessToken> _cache = new(StringComparer.Ordinal);

    public GitHubAppTokenService(IHttpClientFactory httpClientFactory, ILogger<GitHubAppTokenService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Signs an App-level JWT (RS256) for <paramref name="app"/> with
    /// <c>iss</c> = the app id and a lifetime under GitHub's 10-minute ceiling.
    /// </summary>
    public Result<string> CreateAppJwt(GitHubAppRegistration app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (string.IsNullOrWhiteSpace(app.AppId))
        {
            return Result.Failure<string>(Error.Failure(
                "GitHubApp.AppIdMissing", "The GitHub App registration has no AppId configured."));
        }

        if (string.IsNullOrWhiteSpace(app.PrivateKeyPem))
        {
            return Result.Failure<string>(Error.Failure(
                "GitHubApp.PrivateKeyMissing", "The GitHub App registration has no private key configured."));
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(app.PrivateKeyPem);

            var now = DateTimeOffset.UtcNow;
            var payload = new
            {
                iat = now.Subtract(JwtBackdate).ToUnixTimeSeconds(),
                exp = now.Add(JwtLifetime).ToUnixTimeSeconds(),
                iss = app.AppId,
            };

            var encodedHeader = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "RS256", typ = "JWT" }));
            var encodedPayload = Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(payload));
            var signingInput = $"{encodedHeader}.{encodedPayload}";

            var signature = rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            return Result.Success($"{signingInput}.{Base64UrlEncode(signature)}");
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
        {
            _logger.LogError(ex, "Failed to sign a GitHub App JWT");
            return Result.Failure<string>(Error.Failure(
                "GitHubApp.JwtSigningFailed", $"Failed to sign the GitHub App JWT: {ex.Message}"));
        }
    }

    /// <summary>
    /// Returns an installation access token for <paramref name="installationId"/>,
    /// serving a cached unscoped token when it is still fresh. Scoped mints
    /// (<paramref name="scope"/> non-null) always bypass the cache.
    /// </summary>
    public async Task<Result<GitAccessToken>> GetInstallationTokenAsync(
        GitHubAppRegistration app,
        string? appKey,
        Uri apiBaseUrl,
        string installationId,
        GitAccessTokenScope? scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(apiBaseUrl);

        if (string.IsNullOrWhiteSpace(installationId))
        {
            return Result.Failure<GitAccessToken>(Error.Validation(
                "GitHubApp.InstallationIdRequired", "A GitHub App installation id is required."));
        }

        var cacheKey = $"{appKey ?? "default"}:{installationId}";
        if (scope is null
            && _cache.TryGetValue(cacheKey, out var cached)
            && cached.ExpiresAt > DateTimeOffset.UtcNow.Add(RefreshSkew))
        {
            return Result.Success(cached);
        }

        var minted = await MintAsync(app, apiBaseUrl, installationId, scope, retryOnUnauthorized: true, cancellationToken)
            .ConfigureAwait(false);
        if (minted.IsFailure)
        {
            return minted;
        }

        if (scope is null)
        {
            _cache[cacheKey] = minted.Value;
        }

        return minted;
    }

    /// <summary>Drops any cached token for an (app, installation) pair.</summary>
    public void Invalidate(string? appKey, string installationId) =>
        _cache.TryRemove($"{appKey ?? "default"}:{installationId}", out _);

    private async Task<Result<GitAccessToken>> MintAsync(
        GitHubAppRegistration app,
        Uri apiBaseUrl,
        string installationId,
        GitAccessTokenScope? scope,
        bool retryOnUnauthorized,
        CancellationToken cancellationToken)
    {
        var jwtResult = CreateAppJwt(app);
        if (jwtResult.IsFailure)
        {
            return Result.Failure<GitAccessToken>(jwtResult.Error);
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(apiBaseUrl, $"app/installations/{Uri.EscapeDataString(installationId)}/access_tokens"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwtResult.Value);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", GitHubDefaults.ApiVersion);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(GitHubDefaults.ProductName, "1.0"));

            var body = BuildScopeBody(scope);
            if (body is not null)
            {
                request.Content = JsonContent.Create(body);
            }

            var http = _httpClientFactory.CreateClient(GitHubDefaults.HttpClientName);
            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            // A stale App JWT (clock skew) reads as 401; re-sign once and retry.
            if (response.StatusCode == HttpStatusCode.Unauthorized && retryOnUnauthorized)
            {
                _logger.LogWarning(
                    "GitHub rejected the App JWT minting installation {InstallationId}; re-signing and retrying once.",
                    installationId);
                return await MintAsync(app, apiBaseUrl, installationId, scope, retryOnUnauthorized: false, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<GitAccessToken>(GitHubErrorMapper.FromStatus(
                    (int)response.StatusCode,
                    GitRestErrorContext.None,
                    $"installation token request returned {(int)response.StatusCode}"));
            }

            var payload = await response.Content
                .ReadFromJsonAsync<InstallationTokenResponse>(cancellationToken).ConfigureAwait(false);

            if (payload is null || string.IsNullOrEmpty(payload.Token))
            {
                return Result.Failure<GitAccessToken>(Error.Failure(
                    "GitHubApp.InstallationTokenMalformed", "GitHub returned an empty installation-token payload."));
            }

            return Result.Success(new GitAccessToken
            {
                Token = payload.Token,
                ExpiresAt = payload.ExpiresAt,
                HttpBasicUsername = GitHubDefaults.HttpBasicUsername,
            });
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP failure minting installation token for {InstallationId}", installationId);
            return Result.Failure<GitAccessToken>(Error.Failure(
                "GitHubApp.InstallationTokenNetwork", $"HTTP error minting the installation token: {ex.Message}"));
        }
    }

    private static object? BuildScopeBody(GitAccessTokenScope? scope)
    {
        if (scope is null)
        {
            return null;
        }

        var body = new Dictionary<string, object>(StringComparer.Ordinal);

        // The create-installation-token endpoint accepts repository NAMES (which the
        // neutral scope carries) as well as numeric repository_ids; names avoid a lookup.
        if (scope.Repositories is { Count: > 0 } repositories)
        {
            body["repositories"] = repositories.Select(r => r.Name).ToArray();
        }

        if (scope.Permissions is { Count: > 0 } permissions)
        {
            body["permissions"] = permissions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal);
        }

        return body.Count > 0 ? body : null;
    }

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class InstallationTokenResponse
    {
        [JsonPropertyName("token")]
        public string Token { get; init; } = string.Empty;

        [JsonPropertyName("expires_at")]
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
