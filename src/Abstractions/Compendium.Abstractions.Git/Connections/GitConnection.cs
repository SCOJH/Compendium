// -----------------------------------------------------------------------
// <copyright file="GitConnection.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Connections;

/// <summary>
/// Identifies which git server to talk to and with what credential. Every port
/// method takes a connection explicitly — ports are stateless singletons and a
/// single adapter instance serves any number of tenants.
/// </summary>
public sealed record GitConnection
{
    /// <summary>
    /// Gets the provider identifier (matches <see cref="IGitServer.Provider"/>,
    /// e.g. <c>"github"</c>, <c>"gitlab"</c>, <c>"gitea"</c>).
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the API base URL for self-hosted instances — the full prefix API
    /// requests are issued against, not the web host root (GitHub Enterprise
    /// Server: <c>https://ghes.example.com/api/v3</c>; GitLab on-prem:
    /// <c>https://gitlab.example.com/api/v4</c>). <see langword="null"/>
    /// targets the provider's cloud service. Adapters must follow this
    /// convention regardless of their SDK's own base-URL handling.
    /// </summary>
    public Uri? ServerUrl { get; init; }

    /// <summary>
    /// Gets the credential used for this connection.
    /// </summary>
    public required GitCredential Credential { get; init; }
}
