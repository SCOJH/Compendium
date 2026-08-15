// -----------------------------------------------------------------------
// <copyright file="GitHubAdapterOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub.Configuration;

/// <summary>
/// Options for the GitHub adapter. Holds the platform's GitHub App registration(s)
/// used to mint installation tokens and to build install URLs, plus the REST API
/// base URL for github.com (overridden per connection for GitHub Enterprise Server
/// via <see cref="GitConnection.ServerUrl"/>).
/// </summary>
public sealed class GitHubAdapterOptions
{
    /// <summary>
    /// Gets or sets the default GitHub App registration, selected when a
    /// <see cref="GitCredential.AppInstallation"/> carries a <see langword="null"/>
    /// <c>AppKey</c>. Its private key and webhook secret are sensitive — bind them
    /// from a secret store, never from source or plaintext configuration files.
    /// </summary>
    public GitHubAppRegistration DefaultApp { get; set; } = new();

    /// <summary>
    /// Gets the additional named App registrations, selected by
    /// <see cref="GitCredential.AppInstallation.AppKey"/>. Reserved for the
    /// per-instance manifest-creation flow; empty in v1.
    /// </summary>
    public Dictionary<string, GitHubAppRegistration> Apps { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets the github.com REST API base URL. Defaults to
    /// <c>https://api.github.com</c>; a connection's
    /// <see cref="GitConnection.ServerUrl"/> overrides it for GHES.
    /// </summary>
    public Uri ApiBaseUrl { get; set; } = GitHubDefaults.DefaultApiBase;

    /// <summary>
    /// Resolves a registration by key: the <see cref="DefaultApp"/> when
    /// <paramref name="appKey"/> is <see langword="null"/>, otherwise the entry in
    /// <see cref="Apps"/>. Returns <see langword="null"/> when a non-null key has
    /// no registration.
    /// </summary>
    internal GitHubAppRegistration? ResolveApp(string? appKey)
    {
        if (appKey is null)
        {
            return DefaultApp;
        }

        return Apps.TryGetValue(appKey, out var app) ? app : null;
    }
}

/// <summary>
/// A single GitHub App registration: the identity and private key used to mint
/// App JWTs and installation tokens, the slug used to build the install URL, and
/// the webhook signing secret.
/// </summary>
public sealed class GitHubAppRegistration
{
    /// <summary>
    /// Gets or sets the numeric GitHub App id, used as the App JWT <c>iss</c> claim.
    /// </summary>
    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the App's URL slug (the segment after
    /// <c>https://github.com/apps/</c>), used to build the install URL surfaced by
    /// <c>Git.AppNotInstalled</c>.
    /// </summary>
    public string AppSlug { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth client id the App advertises, used for the optional
    /// user-authorization leg of the install flow.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the OAuth client secret paired with <see cref="ClientId"/>.
    /// Sensitive — bind from a secret store.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the PEM-encoded RSA private key used to sign App JWTs.
    /// Sensitive — bind from a secret store and keep out of logs.
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the shared secret used to HMAC-verify inbound webhook
    /// deliveries. Rotated independently of <see cref="PrivateKeyPem"/>.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
