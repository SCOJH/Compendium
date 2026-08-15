// -----------------------------------------------------------------------
// <copyright file="GitHubConnectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Configuration;

namespace Compendium.Adapters.GitHub;

/// <summary>
/// Helpers for resolving the REST API base URL a connection should target:
/// a connection's <see cref="GitConnection.ServerUrl"/> (GHES) when present,
/// otherwise the adapter's configured github.com base, always trailing-slashed.
/// </summary>
internal static class GitHubConnectionExtensions
{
    /// <summary>Resolves the normalized API base URL for a connection.</summary>
    public static Uri ApiBase(this GitConnection connection, GitHubAdapterOptions options)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(options);
        return GitHubDefaults.EnsureTrailingSlash(connection.ServerUrl ?? options.ApiBaseUrl);
    }
}
