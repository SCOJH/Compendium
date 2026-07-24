// -----------------------------------------------------------------------
// <copyright file="AuthorizedConnection.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub.Auth;

/// <summary>
/// A minted bearer token paired with the resolved REST API base URL for a
/// connection — the authorization preamble the REST-backed services share.
/// </summary>
/// <param name="Token">The bearer token to send. Redacted in <see cref="ToString"/>.</param>
/// <param name="ApiBase">The trailing-slashed REST API base URL for the connection.</param>
internal sealed record AuthorizedConnection(string Token, Uri ApiBase)
{
    /// <inheritdoc />
    public override string ToString() => $"AuthorizedConnection(***, {ApiBase})";
}
