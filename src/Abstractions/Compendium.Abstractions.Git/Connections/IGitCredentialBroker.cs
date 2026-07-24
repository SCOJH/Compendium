// -----------------------------------------------------------------------
// <copyright file="IGitCredentialBroker.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Connections;

/// <summary>
/// Mints, validates, and discovers credentials for git connections. This is the
/// auth seam of the abstraction: app-installation providers (GitHub) mint
/// ephemeral installation tokens from the platform app's private key, while
/// token-based providers (GitLab, Gitea) wrap the connection's durable token.
/// </summary>
public interface IGitCredentialBroker
{
    /// <summary>
    /// Mints a short-lived, ready-to-use token for the connection, optionally
    /// narrowed by <paramref name="scope"/>. Adapters cache minted tokens per
    /// credential identity and re-mint before expiry.
    /// </summary>
    Task<Result<GitAccessToken>> MintAsync(
        GitConnection connection,
        GitAccessTokenScope? scope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the connection's credential and reports the identity behind it.
    /// Fails with <c>Git.AuthenticationFailed</c> when the credential is
    /// expired, revoked, or malformed.
    /// </summary>
    Task<Result<GitConnectionIdentity>> ValidateAsync(
        GitConnection connection,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// App-installation mode only: discovers the platform app's installation on
    /// a namespace. Fails with <c>Git.AppNotInstalled</c> (metadata carries
    /// <c>installUrl</c>) when the app is not installed there.
    /// </summary>
    /// <param name="namespace">The organization/group or user login to probe.</param>
    /// <param name="appKey">Selects an app registration; <see langword="null"/> = default app.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<GitInstallationInfo>> ResolveAppInstallationAsync(
        string @namespace,
        string? appKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// App-installation mode only: resolves a single installation of the platform
    /// app by its provider-side id. Unlike <see cref="ResolveAppInstallationAsync"/>
    /// (which probes by account) and <see cref="ListAppInstallationsAsync"/> (which
    /// pages the whole list), this is an O(1) point lookup — use it when a caller
    /// already holds an installation id and only needs to confirm it still belongs
    /// to the app. Fails with <c>Git.InstallationNotFound</c> when the id does not
    /// belong to the app (never installed, or the installation was deleted).
    /// </summary>
    /// <param name="installationId">The provider-side installation id to resolve.</param>
    /// <param name="appKey">Selects an app registration; <see langword="null"/> = default app.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<GitInstallationInfo>> ResolveAppInstallationByIdAsync(
        string installationId,
        string? appKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// App-installation mode only: lists every installation of the platform app
    /// across all accounts. Adapters page through the provider API internally
    /// and return the full list. Used by reconciliation jobs to heal missed
    /// webhooks.
    /// </summary>
    /// <param name="appKey">Selects an app registration; <see langword="null"/> = default app.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task<Result<IReadOnlyList<GitInstallationInfo>>> ListAppInstallationsAsync(
        string? appKey = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The identity behind a validated credential.
/// </summary>
/// <param name="AccountLogin">The login of the account the credential acts as.</param>
/// <param name="AccountType">Whether that account is an organization or a user.</param>
/// <param name="DisplayName">The account's display name, when the provider reports one.</param>
public sealed record GitConnectionIdentity(
    string AccountLogin,
    GitAccountType AccountType,
    string? DisplayName = null);

/// <summary>
/// A platform-app installation on a customer account.
/// </summary>
/// <param name="InstallationId">The provider-side installation identifier.</param>
/// <param name="AccountLogin">The login of the account the app is installed on.</param>
/// <param name="AccountType">Whether that account is an organization or a user.</param>
/// <param name="Suspended">True when the installation is currently suspended.</param>
public sealed record GitInstallationInfo(
    string InstallationId,
    string AccountLogin,
    GitAccountType AccountType,
    bool Suspended = false);
