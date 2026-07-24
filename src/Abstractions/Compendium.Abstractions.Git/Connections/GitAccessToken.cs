// -----------------------------------------------------------------------
// <copyright file="GitAccessToken.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Connections;

/// <summary>
/// A ready-to-use, short-lived token minted for a <see cref="GitConnection"/>.
/// Usable against the provider's API and for git-over-HTTPS via
/// <see cref="HttpBasicUsername"/>.
/// </summary>
public sealed record GitAccessToken
{
    /// <summary>
    /// Gets the token material. Redacted in <c>ToString()</c>.
    /// </summary>
    public required string Token { get; init; }

    /// <summary>
    /// Gets the instant the token expires. For providers whose tokens do not
    /// expire (durable bot tokens passed through), adapters report a far-future
    /// value and document it in CAPABILITIES.md.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>
    /// Gets the username to pair with the token for HTTP basic authentication
    /// on git operations (GitHub: <c>"x-access-token"</c>).
    /// </summary>
    public required string HttpBasicUsername { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"GitAccessToken(***, ExpiresAt={ExpiresAt:O})";
}

/// <summary>
/// Optional narrowing applied when minting a token (GitHub: <c>repository_ids</c>
/// — at most 500 — plus a <c>permissions</c> subset). Providers without token
/// scoping ignore the request; they declare
/// <see cref="Capabilities.GitCapability.ScopedTokenMinting"/> as unsupported.
/// </summary>
public sealed record GitAccessTokenScope
{
    /// <summary>
    /// Gets the repositories the token should be limited to; <see langword="null"/>
    /// keeps the credential's full repository access.
    /// </summary>
    public IReadOnlyList<GitRepositoryRef>? Repositories { get; init; }

    /// <summary>
    /// Gets the permission subset to request, keyed by provider-native
    /// permission names (stringly-typed in v1; see the adapter's CAPABILITIES.md
    /// for the accepted keys).
    /// </summary>
    public IReadOnlyDictionary<string, string>? Permissions { get; init; }
}
