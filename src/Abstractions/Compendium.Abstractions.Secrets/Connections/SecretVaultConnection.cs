// -----------------------------------------------------------------------
// <copyright file="SecretVaultConnection.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Connections;

/// <summary>
/// Everything an adapter needs to reach one vault tenancy on one provider.
/// Passed explicitly to every port method so adapters stay stateless
/// singletons and a single adapter instance can serve many tenancies
/// (platform-owned today, per-tenant or BYO-vault later) without contract
/// changes.
/// </summary>
public sealed record SecretVaultConnection
{
    /// <summary>
    /// Gets the provider identifier this connection targets (matches
    /// <see cref="ISecretVault.Provider"/>).
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the provider-side tenancy container the connection is scoped to
    /// (Scaleway: the project id; Vault: a namespace). <see langword="null"/>
    /// when the provider has no tenancy concept or the adapter's configured
    /// default applies.
    /// </summary>
    public string? Tenancy { get; init; }

    /// <summary>
    /// Gets the provider region hosting the tenancy, when regional
    /// (e.g. <c>"fr-par"</c>). <see langword="null"/> selects the adapter's
    /// configured default.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// Gets the credential material used to authenticate.
    /// </summary>
    public required SecretVaultCredential Credential { get; init; }
}
