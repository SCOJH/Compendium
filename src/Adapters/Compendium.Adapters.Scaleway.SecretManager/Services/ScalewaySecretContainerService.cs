// -----------------------------------------------------------------------
// <copyright file="ScalewaySecretContainerService.cs" company="Sassy Solutions">
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
/// Secret container lifecycle against the Secret Manager API. Listing fetches
/// the tenancy's secrets page by page and applies path-prefix and tag filters
/// client-side, which keeps prefix semantics uniform across providers.
/// </summary>
internal sealed class ScalewaySecretContainerService : ISecretContainerService
{
    private const int PageSize = 100;

    private readonly ScalewayApiClient _client;

    public ScalewaySecretContainerService(ScalewayApiClient client)
    {
        _client = client;
    }

    /// <inheritdoc />
    public async Task<Result<VaultSecretDescriptor>> CreateAsync(
        SecretVaultConnection connection, CreateVaultSecret request, CancellationToken cancellationToken = default)
    {
        var project = _client.ResolveProject(connection);
        if (project.IsFailure)
        {
            return Result.Failure<VaultSecretDescriptor>(project.Error);
        }

        var result = await _client.SendAsync<ScalewaySecret>(
            connection,
            HttpMethod.Post,
            "secrets",
            new ScalewayCreateSecretRequest
            {
                ProjectId = project.Value,
                Name = request.Name,
                Path = request.Path.ToString(),
                Description = request.Description,
                Tags = ScalewayMapping.ToTagList(request.Tags),
            },
            notFound: SecretVaultErrors.ProviderRejected(ScalewayDefaults.Provider, 404, "Project or region not found."),
            conflict: SecretVaultErrors.ConflictExists(request.Name, request.Path.ToString()),
            cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<VaultSecretDescriptor>(result.Error)
            : Result.Success(ScalewayMapping.ToDescriptor(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<VaultSecretDescriptor>> GetAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default)
    {
        var result = await _client.SendAsync<ScalewaySecret>(
            connection,
            HttpMethod.Get,
            $"secrets/{Uri.EscapeDataString(secretId)}",
            body: null,
            notFound: SecretVaultErrors.SecretNotFound(secretId),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        return result.IsFailure
            ? Result.Failure<VaultSecretDescriptor>(result.Error)
            : Result.Success(ScalewayMapping.ToDescriptor(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<VaultSecretDescriptor>>> ListAsync(
        SecretVaultConnection connection, SecretScopePath pathPrefix,
        IReadOnlyDictionary<string, string>? tagFilter = null, CancellationToken cancellationToken = default)
    {
        var project = _client.ResolveProject(connection);
        if (project.IsFailure)
        {
            return Result.Failure<IReadOnlyList<VaultSecretDescriptor>>(project.Error);
        }

        var all = new List<ScalewaySecret>();
        for (var page = 1; ; page++)
        {
            var result = await _client.SendAsync<ScalewayListSecretsResponse>(
                connection,
                HttpMethod.Get,
                $"secrets?project_id={Uri.EscapeDataString(project.Value)}&page={page}&page_size={PageSize}",
                body: null,
                notFound: SecretVaultErrors.ProviderRejected(ScalewayDefaults.Provider, 404, "Project or region not found."),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result.Failure<IReadOnlyList<VaultSecretDescriptor>>(result.Error);
            }

            var batch = result.Value.Secrets ?? [];
            all.AddRange(batch);
            if (batch.Count < PageSize || all.Count >= result.Value.TotalCount)
            {
                break;
            }
        }

        var prefix = pathPrefix.ToString();
        var matches = all
            .Select(ScalewayMapping.ToDescriptor)
            .Where(d => IsUnderPrefix(d.Path.ToString(), prefix))
            .Where(d => tagFilter is null ||
                tagFilter.All(kv => d.Tags.TryGetValue(kv.Key, out var v) && v == kv.Value))
            .OrderBy(d => d.Path.ToString(), StringComparer.Ordinal)
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

        return Result.Success<IReadOnlyList<VaultSecretDescriptor>>(matches);
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default)
    {
        var result = await _client.SendAsync<ScalewayApiClient.Unit>(
            connection,
            HttpMethod.Delete,
            $"secrets/{Uri.EscapeDataString(secretId)}",
            body: null,
            notFound: SecretVaultErrors.SecretNotFound(secretId),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        // Deleting an already-gone secret is a success (idempotent contract).
        return result.IsSuccess || result.Error.Code == $"{SecretVaultErrors.Prefix}.SecretNotFound"
            ? Result.Success()
            : Result.Failure(result.Error);
    }

    private static bool IsUnderPrefix(string path, string prefix) =>
        prefix == "/" || path == prefix || path.StartsWith(prefix + "/", StringComparison.Ordinal);
}
