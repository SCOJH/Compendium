// -----------------------------------------------------------------------
// <copyright file="GitHubCredentialBroker.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Http;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.GitHub.Auth;

/// <summary>
/// The GitHub auth seam. Mints short-lived installation tokens from the platform
/// App's private key for <see cref="GitCredential.AppInstallation"/> connections,
/// and passes through caller-supplied service-account / PAT / OAuth tokens.
/// Validates credentials and discovers App installations across accounts.
/// </summary>
internal sealed class GitHubCredentialBroker : IGitCredentialBroker
{
    private static readonly TimeSpan DurableTokenLifetime = TimeSpan.FromDays(3650);
    private static readonly TimeSpan OAuthTokenLifetime = TimeSpan.FromHours(8);

    private readonly GitHubAdapterOptions _options;
    private readonly GitHubAppTokenService _tokenService;
    private readonly GitHubRestExecutor _rest;

    public GitHubCredentialBroker(
        IOptions<GitHubAdapterOptions> options,
        GitHubAppTokenService tokenService,
        GitHubRestExecutor rest)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
    }

    /// <summary>
    /// Mints an unscoped token for a connection and pairs it with the connection's
    /// resolved API base URL — the common preamble for the REST-backed services.
    /// </summary>
    public async Task<Result<AuthorizedConnection>> AuthorizeAsync(
        GitConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        var mint = await MintAsync(connection, scope: null, cancellationToken).ConfigureAwait(false);
        return mint.IsFailure
            ? Result.Failure<AuthorizedConnection>(mint.Error)
            : Result.Success(new AuthorizedConnection(mint.Value.Token, connection.ApiBase(_options)));
    }

    /// <inheritdoc />
    public Task<Result<GitAccessToken>> MintAsync(
        GitConnection connection, GitAccessTokenScope? scope = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        switch (connection.Credential)
        {
            case GitCredential.AppInstallation app:
                if (_options.ResolveApp(app.AppKey) is not { } registration)
                {
                    return Task.FromResult(Result.Failure<GitAccessToken>(GitErrors.NotConfigured(GitHubDefaults.Provider)));
                }

                return _tokenService.GetInstallationTokenAsync(
                    registration, app.AppKey, connection.ApiBase(_options), app.InstallationId, scope, cancellationToken);

            case GitCredential.ServiceAccountToken sat:
                return Task.FromResult(Result.Success(PassThrough(sat.Token, DurableTokenLifetime)));

            case GitCredential.PersonalAccessToken pat:
                return Task.FromResult(Result.Success(PassThrough(pat.Token, DurableTokenLifetime)));

            case GitCredential.OAuthAccessToken oauth:
                return Task.FromResult(Result.Success(PassThrough(oauth.AccessToken, OAuthTokenLifetime)));

            default:
                return Task.FromResult(Result.Failure<GitAccessToken>(GitErrors.NotConfigured(GitHubDefaults.Provider)));
        }
    }

    /// <inheritdoc />
    public async Task<Result<GitConnectionIdentity>> ValidateAsync(
        GitConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        if (connection.Credential is GitCredential.AppInstallation app)
        {
            if (_options.ResolveApp(app.AppKey) is not { } registration)
            {
                return Result.Failure<GitConnectionIdentity>(GitErrors.NotConfigured(GitHubDefaults.Provider));
            }

            var jwt = _tokenService.CreateAppJwt(registration);
            if (jwt.IsFailure)
            {
                return Result.Failure<GitConnectionIdentity>(jwt.Error);
            }

            var installation = await _rest.GetAsync<GitHubInstallationDto>(
                connection.ApiBase(_options), jwt.Value,
                $"app/installations/{Uri.EscapeDataString(app.InstallationId)}",
                GitRestErrorContext.None, cancellationToken).ConfigureAwait(false);

            if (installation.IsFailure)
            {
                return Result.Failure<GitConnectionIdentity>(installation.Error);
            }

            var account = installation.Value.Account;
            return Result.Success(new GitConnectionIdentity(
                account?.Login ?? string.Empty,
                account?.ToAccountType() ?? GitAccountType.Organization,
                account?.Name));
        }

        var mint = await MintAsync(connection, scope: null, cancellationToken).ConfigureAwait(false);
        if (mint.IsFailure)
        {
            return Result.Failure<GitConnectionIdentity>(mint.Error);
        }

        var user = await _rest.GetAsync<GitHubAccountDto>(
            connection.ApiBase(_options), mint.Value.Token, "user", GitRestErrorContext.None, cancellationToken)
            .ConfigureAwait(false);

        return user.IsFailure
            ? Result.Failure<GitConnectionIdentity>(user.Error)
            : Result.Success(new GitConnectionIdentity(user.Value.Login, user.Value.ToAccountType(), user.Value.Name));
    }

    /// <inheritdoc />
    public async Task<Result<GitInstallationInfo>> ResolveAppInstallationAsync(
        string @namespace, string? appKey = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(@namespace);

        if (_options.ResolveApp(appKey) is not { } registration)
        {
            return Result.Failure<GitInstallationInfo>(GitErrors.NotConfigured(GitHubDefaults.Provider));
        }

        var jwt = _tokenService.CreateAppJwt(registration);
        if (jwt.IsFailure)
        {
            return Result.Failure<GitInstallationInfo>(jwt.Error);
        }

        var apiBase = GitHubDefaults.EnsureTrailingSlash(_options.ApiBaseUrl);
        var escaped = Uri.EscapeDataString(@namespace);

        var org = await _rest.GetAsync<GitHubInstallationDto>(
            apiBase, jwt.Value, $"orgs/{escaped}/installation", GitRestErrorContext.None, cancellationToken)
            .ConfigureAwait(false);
        if (org.IsSuccess)
        {
            return Result.Success(org.Value.ToInstallationInfo());
        }

        var user = await _rest.GetAsync<GitHubInstallationDto>(
            apiBase, jwt.Value, $"users/{escaped}/installation", GitRestErrorContext.None, cancellationToken)
            .ConfigureAwait(false);
        if (user.IsSuccess)
        {
            return Result.Success(user.Value.ToInstallationInfo());
        }

        return Result.Failure<GitInstallationInfo>(GitErrors.AppNotInstalled(@namespace, BuildInstallUrl(registration)));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<GitInstallationInfo>>> ListAppInstallationsAsync(
        string? appKey = null, CancellationToken cancellationToken = default)
    {
        if (_options.ResolveApp(appKey) is not { } registration)
        {
            return Result.Failure<IReadOnlyList<GitInstallationInfo>>(GitErrors.NotConfigured(GitHubDefaults.Provider));
        }

        var jwt = _tokenService.CreateAppJwt(registration);
        if (jwt.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GitInstallationInfo>>(jwt.Error);
        }

        var apiBase = GitHubDefaults.EnsureTrailingSlash(_options.ApiBaseUrl);
        const int perPage = 100;
        var installations = new List<GitInstallationInfo>();

        for (var page = 1; ; page++)
        {
            var pageResult = await _rest.GetAsync<List<GitHubInstallationDto>>(
                apiBase, jwt.Value, $"app/installations?per_page={perPage}&page={page}",
                GitRestErrorContext.None, cancellationToken).ConfigureAwait(false);

            if (pageResult.IsFailure)
            {
                return Result.Failure<IReadOnlyList<GitInstallationInfo>>(pageResult.Error);
            }

            installations.AddRange(pageResult.Value.Select(i => i.ToInstallationInfo()));

            if (pageResult.Value.Count < perPage)
            {
                break;
            }
        }

        return Result.Success<IReadOnlyList<GitInstallationInfo>>(installations);
    }

    private static GitAccessToken PassThrough(string token, TimeSpan lifetime) => new()
    {
        Token = token,
        ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime),
        HttpBasicUsername = GitHubDefaults.HttpBasicUsername,
    };

    private static string? BuildInstallUrl(GitHubAppRegistration registration) =>
        string.IsNullOrWhiteSpace(registration.AppSlug)
            ? null
            : $"https://github.com/apps/{registration.AppSlug}/installations/new";
}
