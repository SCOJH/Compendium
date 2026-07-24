// -----------------------------------------------------------------------
// <copyright file="ScalewaySecretVersionService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Abstractions.Secrets.Services;
using Compendium.Adapters.Scaleway.SecretManager.Http;

namespace Compendium.Adapters.Scaleway.SecretManager.Services;

/// <summary>
/// Immutable versions against the Secret Manager API. Access is by explicit
/// revision only; ambiguous provider failures (a 4xx on access) are
/// disambiguated by reading the version metadata so callers always get the
/// precise <c>VersionDisabled</c> / <c>VersionNotFound</c> / <c>SecretNotFound</c>
/// code. Enable/disable are made idempotent by treating "already in target
/// state" as success.
/// </summary>
internal sealed class ScalewaySecretVersionService : ISecretVersionService
{
    private const int PageSize = 100;

    private readonly ScalewayApiClient _client;

    public ScalewaySecretVersionService(ScalewayApiClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<Result<VaultSecretVersion>> AddAsync(
        SecretVaultConnection connection, string secretId, SecretMaterial material,
        string? description = null, CancellationToken cancellationToken = default)
    {
        if (material.Length > ScalewayDefaults.MaxPayloadBytes)
        {
            return Result.Failure<VaultSecretVersion>(
                SecretVaultErrors.PayloadTooLarge(material.Length, ScalewayDefaults.MaxPayloadBytes));
        }

        var result = await _client.SendAsync<ScalewaySecretVersion>(
            connection,
            HttpMethod.Post,
            $"secrets/{Uri.EscapeDataString(secretId)}/versions",
            new ScalewayCreateVersionRequest
            {
                Data = Convert.ToBase64String(material.Data.Span),
                Description = description,
            },
            notFound: SecretVaultErrors.SecretNotFound(secretId),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<VaultSecretVersion>(result.Error)
            : Result.Success(ScalewayMapping.ToVersion(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<SecretMaterial>> AccessAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default)
    {
        var result = await _client.SendAsync<ScalewayAccessResponse>(
            connection,
            HttpMethod.Get,
            $"secrets/{Uri.EscapeDataString(secretId)}/versions/{revision}/access",
            body: null,
            notFound: SecretVaultErrors.VersionNotFound(secretId, revision),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            try
            {
                return Result.Success(SecretMaterial.FromBytes(Convert.FromBase64String(result.Value.Data)));
            }
            catch (FormatException)
            {
                return Result.Failure<SecretMaterial>(SecretVaultErrors.ProviderRejected(
                    ScalewayDefaults.Provider, 200, "Version payload was not valid base64."));
            }
        }

        // The provider refuses access to disabled versions with the same 4xx
        // shape as a missing one; disambiguate through the version metadata.
        if (result.Error.Type is ErrorType.NotFound or ErrorType.Failure)
        {
            var version = await GetVersionAsync(connection, secretId, revision, cancellationToken).ConfigureAwait(false);
            if (version.IsSuccess && version.Value.Status == VaultSecretVersionStatus.Disabled)
            {
                return Result.Failure<SecretMaterial>(SecretVaultErrors.VersionDisabled(secretId, revision));
            }

            if (version.IsFailure && version.Error.Code == $"{SecretVaultErrors.Prefix}.SecretNotFound")
            {
                return Result.Failure<SecretMaterial>(version.Error);
            }
        }

        return Result.Failure<SecretMaterial>(result.Error);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<VaultSecretVersion>>> ListAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default)
    {
        var all = new List<ScalewaySecretVersion>();
        for (var page = 1; ; page++)
        {
            var result = await _client.SendAsync<ScalewayListVersionsResponse>(
                connection,
                HttpMethod.Get,
                $"secrets/{Uri.EscapeDataString(secretId)}/versions?page={page}&page_size={PageSize}",
                body: null,
                notFound: SecretVaultErrors.SecretNotFound(secretId),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result.Failure<IReadOnlyList<VaultSecretVersion>>(result.Error);
            }

            var batch = result.Value.Versions ?? [];
            all.AddRange(batch);
            if (batch.Count < PageSize || all.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        return Result.Success<IReadOnlyList<VaultSecretVersion>>(
            [.. all.Select(ScalewayMapping.ToVersion).OrderBy(v => v.Revision)]);
    }

    /// <inheritdoc />
    public Task<Result> EnableAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        SetStatusAsync(connection, secretId, revision, enable: true, cancellationToken);

    /// <inheritdoc />
    public Task<Result> DisableAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        SetStatusAsync(connection, secretId, revision, enable: false, cancellationToken);

    /// <inheritdoc />
    public async Task<Result> DestroyAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default)
    {
        var result = await _client.SendAsync<ScalewayApiClient.Unit>(
            connection,
            HttpMethod.Delete,
            $"secrets/{Uri.EscapeDataString(secretId)}/versions/{revision}",
            body: null,
            notFound: SecretVaultErrors.VersionNotFound(secretId, revision),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.IsFailure ? Result.Failure(result.Error) : Result.Success();
    }

    private async Task<Result> SetStatusAsync(
        SecretVaultConnection connection, string secretId, long revision, bool enable,
        CancellationToken cancellationToken)
    {
        var action = enable ? "enable" : "disable";
        var result = await _client.SendAsync<ScalewaySecretVersion>(
            connection,
            HttpMethod.Post,
            $"secrets/{Uri.EscapeDataString(secretId)}/versions/{revision}/{action}",
            body: new { },
            notFound: SecretVaultErrors.VersionNotFound(secretId, revision),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            return Result.Success();
        }

        // The provider rejects a no-op transition; the contract requires
        // idempotency, so "already in the target state" is a success.
        var version = await GetVersionAsync(connection, secretId, revision, cancellationToken).ConfigureAwait(false);
        var target = enable ? VaultSecretVersionStatus.Enabled : VaultSecretVersionStatus.Disabled;
        return version.IsSuccess && version.Value.Status == target
            ? Result.Success()
            : Result.Failure(result.Error);
    }

    private async Task<Result<VaultSecretVersion>> GetVersionAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken)
    {
        var result = await _client.SendAsync<ScalewaySecretVersion>(
            connection,
            HttpMethod.Get,
            $"secrets/{Uri.EscapeDataString(secretId)}/versions/{revision}",
            body: null,
            notFound: SecretVaultErrors.VersionNotFound(secretId, revision),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (result.IsFailure && result.Error.Code == $"{SecretVaultErrors.Prefix}.VersionNotFound")
        {
            // Distinguish "secret gone" from "version gone".
            var secret = await _client.SendAsync<ScalewaySecret>(
                connection,
                HttpMethod.Get,
                $"secrets/{Uri.EscapeDataString(secretId)}",
                body: null,
                notFound: SecretVaultErrors.SecretNotFound(secretId),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (secret.IsFailure)
            {
                return Result.Failure<VaultSecretVersion>(secret.Error);
            }
        }

        return result.IsFailure
            ? Result.Failure<VaultSecretVersion>(result.Error)
            : Result.Success(ScalewayMapping.ToVersion(result.Value));
    }
}
