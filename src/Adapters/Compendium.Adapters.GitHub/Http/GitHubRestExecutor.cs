// -----------------------------------------------------------------------
// <copyright file="GitHubRestExecutor.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Compendium.Adapters.GitHub.Http;

/// <summary>
/// A thin typed HTTP helper for the GitHub REST endpoints Octokit lags on
/// (Actions variables, repository rulesets, deployment environments, environment
/// and organization secrets, installation lookup). Sends bearer-authenticated
/// requests against a normalized base URL, deserializes JSON, and maps every
/// non-success response onto the neutral <see cref="GitErrors"/> via
/// <see cref="GitHubErrorMapper"/>. Stateless — safe as a singleton.
/// </summary>
internal sealed class GitHubRestExecutor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public GitHubRestExecutor(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>Sends a GET and deserializes the JSON body into <typeparamref name="T"/>.</summary>
    public Task<Result<T>> GetAsync<T>(
        Uri baseUrl, string token, string path, GitRestErrorContext context, CancellationToken cancellationToken)
        => SendWithBodyAsync<T>(HttpMethod.Get, baseUrl, token, path, body: null, context, cancellationToken);

    /// <summary>Sends a request with an optional JSON body and deserializes the JSON response.</summary>
    public Task<Result<T>> SendWithBodyAsync<T>(
        HttpMethod method,
        Uri baseUrl,
        string token,
        string path,
        object? body,
        GitRestErrorContext context,
        CancellationToken cancellationToken)
        => ExecuteAsync(method, baseUrl, token, path, body, context, readBody: true, cancellationToken,
            static async (response, ct) =>
            {
                var value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct).ConfigureAwait(false);
                return value is null
                    ? Result.Failure<T>(Error.Failure(
                        "GitHub.EmptyResponse", "GitHub returned an empty response body where content was expected."))
                    : Result.Success(value);
            });

    /// <summary>Sends a request with an optional JSON body and ignores the response body.</summary>
    public async Task<Result> SendAsync(
        HttpMethod method,
        Uri baseUrl,
        string token,
        string path,
        object? body,
        GitRestErrorContext context,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync<object?>(
            method, baseUrl, token, path, body, context, readBody: false, cancellationToken,
            static (_, _) => Task.FromResult(Result.Success<object?>(null))).ConfigureAwait(false);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <summary>
    /// Sends a DELETE that treats a 404 as success (idempotent delete of an
    /// already-absent resource).
    /// </summary>
    public async Task<Result> DeleteIdempotentAsync(
        Uri baseUrl, string token, string path, GitRestErrorContext context, CancellationToken cancellationToken)
    {
        using var response = await SendRawAsync(HttpMethod.Delete, baseUrl, token, path, body: null, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
        {
            return Result.Success();
        }

        return Result.Failure(await MapFailureAsync(response, context, cancellationToken).ConfigureAwait(false));
    }

    private async Task<Result<T>> ExecuteAsync<T>(
        HttpMethod method,
        Uri baseUrl,
        string token,
        string path,
        object? body,
        GitRestErrorContext context,
        bool readBody,
        CancellationToken cancellationToken,
        Func<HttpResponseMessage, CancellationToken, Task<Result<T>>> onSuccess)
    {
        try
        {
            using var response = await SendRawAsync(method, baseUrl, token, path, body, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<T>(await MapFailureAsync(response, context, cancellationToken).ConfigureAwait(false));
            }

            if (!readBody || response.StatusCode == HttpStatusCode.NoContent)
            {
                return Result.Success<T>(default!);
            }

            return await onSuccess(response, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure<T>(Error.Failure(
                "GitHub.NetworkError", $"HTTP error calling GitHub: {ex.Message}"));
        }
        catch (JsonException ex)
        {
            return Result.Failure<T>(Error.Failure(
                "GitHub.MalformedResponse", $"GitHub returned a response that could not be parsed: {ex.Message}"));
        }
    }

    private async Task<HttpResponseMessage> SendRawAsync(
        HttpMethod method, Uri baseUrl, string token, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(GitHubDefaults.EnsureTrailingSlash(baseUrl), path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", GitHubDefaults.ApiVersion);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(GitHubDefaults.ProductName, "1.0"));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: JsonOptions);
        }

        var http = _httpClientFactory.CreateClient(GitHubDefaults.HttpClientName);
        return await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Error> MapFailureAsync(
        HttpResponseMessage response, GitRestErrorContext context, CancellationToken cancellationToken)
    {
        var detail = await ReadDetailAsync(response, cancellationToken).ConfigureAwait(false);
        var (retryAfter, rateLimited) = ReadRateLimit(response);
        return GitHubErrorMapper.FromStatus((int)response.StatusCode, context, detail, retryAfter, rateLimited);
    }

    private static async Task<string> ReadDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase ?? string.Empty : Truncate(body, 500);
        }
        catch (HttpRequestException)
        {
            return response.ReasonPhrase ?? string.Empty;
        }
    }

    private static (TimeSpan? RetryAfter, bool RateLimited) ReadRateLimit(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta)
        {
            return (delta, true);
        }

        if (response.Headers.TryGetValues("x-ratelimit-remaining", out var remainingValues)
            && remainingValues.FirstOrDefault() == "0")
        {
            if (response.Headers.TryGetValues("x-ratelimit-reset", out var resetValues)
                && long.TryParse(resetValues.FirstOrDefault(), out var resetUnix))
            {
                var reset = DateTimeOffset.FromUnixTimeSeconds(resetUnix) - DateTimeOffset.UtcNow;
                return (reset > TimeSpan.Zero ? reset : null, true);
            }

            return (null, true);
        }

        return (null, false);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
