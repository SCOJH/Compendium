// -----------------------------------------------------------------------
// <copyright file="ScalewayApiClient.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Adapters.Scaleway.SecretManager.Configuration;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.Scaleway.SecretManager.Http;

/// <summary>
/// The raw HTTP executor for the Secret Manager regional API: builds
/// authenticated requests from the per-call connection, deserializes
/// snake_case bodies, and maps provider failures onto the uniform
/// <c>SecretVault.*</c> errors. Requests are sent exactly once — write
/// endpoints are not idempotent, so retry policy belongs to callers who can
/// deduplicate.
/// </summary>
internal sealed class ScalewayApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ScalewaySecretManagerOptions _options;

    public ScalewayApiClient(IHttpClientFactory httpClientFactory, IOptions<ScalewaySecretManagerOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    /// <summary>
    /// Resolves the Scaleway project id from the connection tenancy or the
    /// configured default.
    /// </summary>
    public Result<string> ResolveProject(SecretVaultConnection connection)
    {
        var project = connection.Tenancy ?? _options.DefaultProjectId;
        return string.IsNullOrWhiteSpace(project)
            ? Result.Failure<string>(SecretVaultErrors.NotConfigured(ScalewayDefaults.Provider))
            : Result.Success(project);
    }

    /// <summary>
    /// Sends a request and deserializes the successful body. <c>notFound</c>
    /// and <c>conflict</c> supply the caller's context-specific mapping for
    /// 404/409; every other failure maps uniformly.
    /// </summary>
    public async Task<Result<TResponse>> SendAsync<TResponse>(
        SecretVaultConnection connection,
        HttpMethod method,
        string path,
        object? body,
        Error notFound,
        Error? conflict = null,
        CancellationToken cancellationToken = default)
    {
        if (connection.Credential is not SecretVaultCredential.ApiToken token ||
            string.IsNullOrWhiteSpace(token.Token))
        {
            return Result.Failure<TResponse>(SecretVaultErrors.NotConfigured(ScalewayDefaults.Provider));
        }

        using var request = new HttpRequestMessage(method, BuildUri(connection, path));
        request.Headers.Add(ScalewayDefaults.AuthHeader, token.Token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json);
        }

        HttpResponseMessage response;
        try
        {
            var client = _httpClientFactory.CreateClient(ScalewayDefaults.HttpClientName);
            response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Result.Failure<TResponse>(SecretVaultErrors.ProviderRejected(
                ScalewayDefaults.Provider, 0, ex.Message));
        }

        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                if (typeof(TResponse) == typeof(Unit))
                {
                    return Result.Success((TResponse)(object)Unit.Value);
                }

                var payload = await response.Content
                    .ReadFromJsonAsync<TResponse>(Json, cancellationToken)
                    .ConfigureAwait(false);
                return payload is null
                    ? Result.Failure<TResponse>(SecretVaultErrors.ProviderRejected(
                        ScalewayDefaults.Provider, (int)response.StatusCode, "Empty response body."))
                    : Result.Success(payload);
            }

            var error = await MapErrorAsync(response, notFound, conflict, cancellationToken).ConfigureAwait(false);
            return Result.Failure<TResponse>(error);
        }
    }

    /// <summary>A no-body marker for void endpoints.</summary>
    internal readonly record struct Unit
    {
        public static Unit Value => default;
    }

    private Uri BuildUri(SecretVaultConnection connection, string path)
    {
        var region = connection.Region ?? _options.DefaultRegion;
        return new Uri(_options.ApiBaseUrl, $"secret-manager/v1beta1/regions/{region}/{path}");
    }

    private static async Task<Error> MapErrorAsync(
        HttpResponseMessage response, Error notFound, Error? conflict, CancellationToken cancellationToken)
    {
        var detail = await ReadErrorMessageAsync(response, cancellationToken).ConfigureAwait(false);
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                SecretVaultErrors.AuthenticationFailed(ScalewayDefaults.Provider, detail),
            HttpStatusCode.NotFound => notFound,
            HttpStatusCode.Conflict when conflict is not null => conflict,
            HttpStatusCode.TooManyRequests =>
                SecretVaultErrors.Throttled(ScalewayDefaults.Provider, ReadRetryAfterSeconds(response)),
            HttpStatusCode.BadRequest or HttpStatusCode.PreconditionFailed when
                detail is not null && detail.Contains("quota", StringComparison.OrdinalIgnoreCase) =>
                SecretVaultErrors.QuotaExceeded(ScalewayDefaults.Provider, detail),
            _ => SecretVaultErrors.ProviderRejected(
                ScalewayDefaults.Provider, (int)response.StatusCode, detail),
        };
    }

    private static async Task<string?> ReadErrorMessageAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content
                .ReadFromJsonAsync<ScalewayErrorResponse>(Json, cancellationToken)
                .ConfigureAwait(false);
            return body?.Message;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int? ReadRetryAfterSeconds(HttpResponseMessage response) =>
        response.Headers.RetryAfter?.Delta is { } delta ? (int)delta.TotalSeconds : null;
}
