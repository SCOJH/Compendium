// -----------------------------------------------------------------------
// <copyright file="GitHubDefaults.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub;

/// <summary>
/// Shared constants for the GitHub adapter: the provider discriminator, the
/// product/user-agent header, the named <see cref="System.Net.Http.HttpClient"/>
/// used for raw REST calls, and the GitHub REST API version pin.
/// </summary>
internal static class GitHubDefaults
{
    /// <summary>The provider discriminator (matches <see cref="IGitServer.Provider"/>).</summary>
    public const string Provider = "github";

    /// <summary>The Octokit / user-agent product name. Must contain no spaces.</summary>
    public const string ProductName = "compendium-git";

    /// <summary>The named HttpClient used for raw REST calls the Octokit client lags on.</summary>
    public const string HttpClientName = "compendium-github";

    /// <summary>The REST API version pin sent as <c>X-GitHub-Api-Version</c>.</summary>
    public const string ApiVersion = "2022-11-28";

    /// <summary>The HTTP-basic username paired with a minted token for git-over-HTTPS.</summary>
    public const string HttpBasicUsername = "x-access-token";

    /// <summary>The github.com REST API base URL used when no override is configured.</summary>
    public static readonly Uri DefaultApiBase = new("https://api.github.com");

    /// <summary>
    /// Returns <paramref name="uri"/> with a trailing slash so relative paths combine
    /// against it without truncating a GHES <c>/api/v3</c> prefix.
    /// </summary>
    public static Uri EnsureTrailingSlash(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
    }
}

