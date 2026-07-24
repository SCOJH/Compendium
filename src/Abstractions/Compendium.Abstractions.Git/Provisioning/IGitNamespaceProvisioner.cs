// -----------------------------------------------------------------------
// <copyright file="IGitNamespaceProvisioner.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;

namespace Compendium.Abstractions.Git.Provisioning;

/// <summary>
/// OPTIONAL capability: creates the namespace (organization/group) itself.
/// Trivial on GitLab (<c>POST /groups</c>) and Gitea (<c>POST /orgs</c>);
/// on github.com it requires an enterprise-owner user token and is declared
/// <see cref="Capabilities.GitCapabilityLevel.Partial"/> at best; absent on
/// Azure DevOps. Always check
/// <see cref="Capabilities.GitCapability.NamespaceProvisioning"/> first.
/// </summary>
public interface IGitNamespaceProvisioner
{
    /// <summary>
    /// Creates a namespace. Fails with <c>Git.Conflict</c> when the slug is
    /// already taken.
    /// </summary>
    Task<Result<GitNamespace>> CreateNamespaceAsync(
        GitConnection connection,
        CreateGitNamespace request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to create a namespace.
/// </summary>
public sealed record CreateGitNamespace
{
    /// <summary>Gets the namespace slug/login (e.g. <c>"NXS-Acme"</c>).</summary>
    public required string Name { get; init; }

    /// <summary>Gets the display/profile name; defaults to <see cref="Name"/>.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Gets the billing email, where the provider requires one (GitHub enterprise org creation).</summary>
    public string? BillingEmail { get; init; }

    /// <summary>Gets the logins granted admin/owner on the new namespace, where the provider supports it.</summary>
    public IReadOnlyList<string>? AdminLogins { get; init; }
}

/// <summary>
/// A created namespace.
/// </summary>
/// <param name="Name">The namespace slug/login.</param>
/// <param name="HtmlUrl">The web URL of the namespace, when the provider reports one.</param>
public sealed record GitNamespace(string Name, string? HtmlUrl = null);
