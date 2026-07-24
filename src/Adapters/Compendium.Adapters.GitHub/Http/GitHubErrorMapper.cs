// -----------------------------------------------------------------------
// <copyright file="GitHubErrorMapper.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using Octokit;

namespace Compendium.Adapters.GitHub.Http;

/// <summary>
/// Maps GitHub failures — both raw HTTP status codes (REST executor) and Octokit
/// exceptions — onto the neutral <see cref="GitErrors"/> so every provider fails
/// uniformly. Never leaks token material: only status codes and provider messages
/// flow through.
/// </summary>
internal static class GitHubErrorMapper
{
    /// <summary>The provider discriminator carried in mapped errors.</summary>
    public const string Provider = GitHubDefaults.Provider;

    /// <summary>
    /// Maps an HTTP status code (plus an optional rate-limit hint) onto a neutral error.
    /// </summary>
    public static Error FromStatus(
        int statusCode,
        GitRestErrorContext context,
        string detail,
        TimeSpan? retryAfter = null,
        bool rateLimited = false)
    {
        switch (statusCode)
        {
            case 401:
                return GitErrors.AuthenticationFailed(Provider, detail);

            case 403 when rateLimited || retryAfter.HasValue:
            case 429:
                return GitErrors.Throttled(retryAfter);

            case 404:
                if (context.RepositoryFullName is { } repo)
                {
                    return GitErrors.RepositoryNotFound(repo);
                }

                if (context.Namespace is { } ns)
                {
                    return GitErrors.NamespaceNotFound(ns);
                }

                if (context.InstallationId is { } installationId)
                {
                    return GitErrors.InstallationNotFound(installationId);
                }

                return GitErrors.ProviderRejected(Provider, statusCode, detail);

            case 409:
            case 422 when LooksLikeConflict(detail):
                return GitErrors.Conflict(context.ConflictResource ?? detail);

            default:
                return GitErrors.ProviderRejected(Provider, statusCode, detail);
        }
    }

    /// <summary>
    /// Maps an Octokit exception onto a neutral error, honoring rate-limit resets
    /// and treating "name already exists" validation failures as conflicts.
    /// </summary>
    public static Error FromException(Exception exception, GitRestErrorContext context)
    {
        switch (exception)
        {
            case RateLimitExceededException rate:
                return GitErrors.Throttled(RetryAfter(rate.Reset));

            case SecondaryRateLimitExceededException:
                return GitErrors.Throttled(null);

            case AbuseException abuse:
                return GitErrors.Throttled(
                    abuse.RetryAfterSeconds is > 0 ? TimeSpan.FromSeconds(abuse.RetryAfterSeconds.Value) : null);

            case AuthorizationException:
                return GitErrors.AuthenticationFailed(Provider, exception.Message);

            case NotFoundException:
                if (context.RepositoryFullName is { } repo)
                {
                    return GitErrors.RepositoryNotFound(repo);
                }

                if (context.Namespace is { } ns)
                {
                    return GitErrors.NamespaceNotFound(ns);
                }

                if (context.InstallationId is { } installationId)
                {
                    return GitErrors.InstallationNotFound(installationId);
                }

                return GitErrors.ProviderRejected(Provider, 404, exception.Message);

            case ApiValidationException validation when LooksLikeConflict(validation.Message)
                || LooksLikeConflict(DescribeValidation(validation)):
                return GitErrors.Conflict(context.ConflictResource ?? validation.Message);

            case ApiException api:
                return GitErrors.ProviderRejected(Provider, (int)api.StatusCode, api.Message);

            default:
                return GitErrors.ProviderRejected(Provider, 0, exception.Message);
        }
    }

    private static TimeSpan? RetryAfter(DateTimeOffset reset)
    {
        var delta = reset - DateTimeOffset.UtcNow;
        return delta > TimeSpan.Zero ? delta : null;
    }

    private static bool LooksLikeConflict(string? message) =>
        message is not null && message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    private static string DescribeValidation(ApiValidationException validation) =>
        validation.ApiError?.Errors is { Count: > 0 } errors
            ? string.Join("; ", errors.Select(e => $"{e.Field} {e.Code} {e.Message}"))
            : string.Empty;
}
