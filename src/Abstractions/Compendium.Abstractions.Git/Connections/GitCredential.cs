// -----------------------------------------------------------------------
// <copyright file="GitCredential.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Connections;

/// <summary>
/// The credential material of a <see cref="GitConnection"/>, as a closed union.
/// Token-bearing members redact their material in <c>ToString()</c> so a logged
/// connection never leaks a secret.
/// </summary>
public abstract record GitCredential
{
    private GitCredential()
    {
    }

    /// <summary>
    /// A platform-owned app installed on the customer namespace (GitHub App
    /// installation). The adapter mints short-lived tokens from the app's
    /// private key; no durable secret is stored per connection.
    /// </summary>
    /// <param name="InstallationId">The provider-side installation identifier.</param>
    /// <param name="AppKey">
    /// Selects an app registration from the adapter options; <see langword="null"/>
    /// selects the default app. Per-instance apps created via a manifest flow
    /// plug in here later without a contract change.
    /// </param>
    public sealed record AppInstallation(string InstallationId, string? AppKey = null) : GitCredential;

    /// <summary>
    /// A durable machine token (GitLab group access token, Gitea/Forgejo bot
    /// PAT, Azure DevOps service principal secret).
    /// </summary>
    /// <param name="Token">The token material. Redacted in <c>ToString()</c>.</param>
    public sealed record ServiceAccountToken(string Token) : GitCredential
    {
        /// <inheritdoc />
        public override string ToString() => "ServiceAccountToken(***)";
    }

    /// <summary>
    /// A caller-supplied user personal access token, held in memory for the
    /// duration of the call only — never persisted, logged, or cached.
    /// </summary>
    /// <param name="Token">The token material. Redacted in <c>ToString()</c>.</param>
    public sealed record PersonalAccessToken(string Token) : GitCredential
    {
        /// <inheritdoc />
        public override string ToString() => "PersonalAccessToken(***)";
    }

    /// <summary>
    /// An OAuth user access token (user-to-server), used to act as — or verify
    /// the identity of — a specific human.
    /// </summary>
    /// <param name="AccessToken">The token material. Redacted in <c>ToString()</c>.</param>
    public sealed record OAuthAccessToken(string AccessToken) : GitCredential
    {
        /// <inheritdoc />
        public override string ToString() => "OAuthAccessToken(***)";
    }
}
