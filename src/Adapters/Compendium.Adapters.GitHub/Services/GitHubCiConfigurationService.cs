// -----------------------------------------------------------------------
// <copyright file="GitHubCiConfigurationService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Auth;
using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Http;
using Compendium.Adapters.GitHub.Security;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// GitHub Actions secrets and variables at repository, organization, and
/// deployment-environment scope, over the REST API. Secrets are write-only:
/// values are sealed against the target's Actions public key (libsodium sealed
/// box) before upload and never read back. Environment-scoped calls resolve the
/// repository's numeric id, which the environment endpoints key on.
/// </summary>
internal sealed class GitHubCiConfigurationService : IGitCiConfigurationService
{
    private readonly GitHubCredentialBroker _broker;
    private readonly GitHubRestExecutor _rest;
    private readonly GitHubSecretSealer _sealer;

    public GitHubCiConfigurationService(
        GitHubCredentialBroker broker,
        GitHubRestExecutor rest,
        GitHubSecretSealer sealer)
    {
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _rest = rest ?? throw new ArgumentNullException(nameof(rest));
        _sealer = sealer ?? throw new ArgumentNullException(nameof(sealer));
    }

    /// <inheritdoc />
    public async Task<Result> SetSecretsAsync(
        GitConnection connection, GitConfigurationScope scope, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(secrets);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure(auth.Error);
        }

        var paths = await ResolvePathsAsync(auth.Value, scope, cancellationToken).ConfigureAwait(false);
        if (paths.IsFailure)
        {
            return Result.Failure(paths.Error);
        }

        var publicKey = await _rest.GetAsync<GitHubPublicKeyDto>(
            auth.Value.ApiBase, auth.Value.Token, paths.Value.PublicKeyPath, GitRestErrorContext.None, cancellationToken)
            .ConfigureAwait(false);
        if (publicKey.IsFailure)
        {
            return Result.Failure(publicKey.Error);
        }

        foreach (var (name, value) in secrets)
        {
            var body = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["encrypted_value"] = _sealer.Seal(value, publicKey.Value.Key),
                ["key_id"] = publicKey.Value.KeyId,
            };
            if (paths.Value.OrgVisibility is { } visibility)
            {
                body["visibility"] = visibility;
            }

            var set = await _rest.SendAsync(
                HttpMethod.Put, auth.Value.ApiBase, auth.Value.Token, $"{paths.Value.SecretsBase}/{Uri.EscapeDataString(name)}",
                body, GitRestErrorContext.None, cancellationToken).ConfigureAwait(false);
            if (set.IsFailure)
            {
                return set;
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeleteSecretAsync(
        GitConnection connection, GitConfigurationScope scope, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure(auth.Error);
        }

        var paths = await ResolvePathsAsync(auth.Value, scope, cancellationToken).ConfigureAwait(false);
        if (paths.IsFailure)
        {
            return Result.Failure(paths.Error);
        }

        return await _rest.DeleteIdempotentAsync(
            auth.Value.ApiBase, auth.Value.Token, $"{paths.Value.SecretsBase}/{Uri.EscapeDataString(name)}",
            GitRestErrorContext.None, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Result> SetVariablesAsync(
        GitConnection connection, GitConfigurationScope scope, IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(variables);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure(auth.Error);
        }

        var paths = await ResolvePathsAsync(auth.Value, scope, cancellationToken).ConfigureAwait(false);
        if (paths.IsFailure)
        {
            return Result.Failure(paths.Error);
        }

        foreach (var (name, value) in variables)
        {
            var createBody = new Dictionary<string, object>(StringComparer.Ordinal) { ["name"] = name, ["value"] = value };
            if (paths.Value.OrgVisibility is { } visibility)
            {
                createBody["visibility"] = visibility;
            }

            var created = await _rest.SendAsync(
                HttpMethod.Post, auth.Value.ApiBase, auth.Value.Token, paths.Value.VariablesBase, createBody,
                new GitRestErrorContext { ConflictResource = name }, cancellationToken).ConfigureAwait(false);

            if (created.IsSuccess)
            {
                continue;
            }

            if (created.Error.Code != $"{GitErrors.Prefix}.Conflict")
            {
                return created;
            }

            // Already exists — update it (GitHub variable updates PATCH by name).
            var updated = await _rest.SendAsync(
                HttpMethod.Patch, auth.Value.ApiBase, auth.Value.Token,
                $"{paths.Value.VariablesBase}/{Uri.EscapeDataString(name)}",
                new Dictionary<string, object>(StringComparer.Ordinal) { ["name"] = name, ["value"] = value },
                GitRestErrorContext.None, cancellationToken).ConfigureAwait(false);
            if (updated.IsFailure)
            {
                return updated;
            }
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> DeleteVariableAsync(
        GitConnection connection, GitConfigurationScope scope, string name, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var auth = await _broker.AuthorizeAsync(connection, cancellationToken).ConfigureAwait(false);
        if (auth.IsFailure)
        {
            return Result.Failure(auth.Error);
        }

        var paths = await ResolvePathsAsync(auth.Value, scope, cancellationToken).ConfigureAwait(false);
        if (paths.IsFailure)
        {
            return Result.Failure(paths.Error);
        }

        return await _rest.DeleteIdempotentAsync(
            auth.Value.ApiBase, auth.Value.Token, $"{paths.Value.VariablesBase}/{Uri.EscapeDataString(name)}",
            GitRestErrorContext.None, cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<ScopePaths>> ResolvePathsAsync(
        AuthorizedConnection auth, GitConfigurationScope scope, CancellationToken cancellationToken)
    {
        switch (scope)
        {
            case GitConfigurationScope.Repository r:
            {
                var b = $"repos/{r.Ref.Namespace}/{r.Ref.Name}/actions";
                return Result.Success(new ScopePaths($"{b}/secrets", $"{b}/variables", $"{b}/secrets/public-key", null));
            }

            case GitConfigurationScope.Namespace n:
            {
                var b = $"orgs/{Uri.EscapeDataString(n.Name)}/actions";
                return Result.Success(new ScopePaths($"{b}/secrets", $"{b}/variables", $"{b}/secrets/public-key", "all"));
            }

            case GitConfigurationScope.Environment e:
            {
                var repoId = await _rest.GetAsync<GitHubRepositoryIdDto>(
                    auth.ApiBase, auth.Token, $"repos/{e.Ref.Namespace}/{e.Ref.Name}",
                    GitRestErrorContext.ForRepository(e.Ref), cancellationToken).ConfigureAwait(false);
                if (repoId.IsFailure)
                {
                    return Result.Failure<ScopePaths>(repoId.Error);
                }

                var env = Uri.EscapeDataString(e.EnvironmentName);
                var b = $"repositories/{repoId.Value.Id}/environments/{env}";
                return Result.Success(new ScopePaths($"{b}/secrets", $"{b}/variables", $"{b}/secrets/public-key", null));
            }

            default:
                return Result.Failure<ScopePaths>(Error.Failure(
                    "GitHub.UnknownScope", $"Unknown configuration scope '{scope.GetType().Name}'."));
        }
    }

    private sealed record ScopePaths(string SecretsBase, string VariablesBase, string PublicKeyPath, string? OrgVisibility);
}
