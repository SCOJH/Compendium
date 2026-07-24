// -----------------------------------------------------------------------
// <copyright file="ISecretVault.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Abstractions.Secrets.Services;

namespace Compendium.Abstractions.Secrets;

/// <summary>
/// The secret-vault facade: one instance per provider adapter, carrying the
/// provider discriminator, the declarative capability matrix, and the
/// concern-scoped ports. Consumers resolve <c>IEnumerable&lt;ISecretVault&gt;</c>
/// and dispatch on <see cref="Provider"/> (the sub-ports are also registered
/// individually for single-concern consumers).
/// </summary>
/// <remarks>
/// Implementations are stateless singletons: all tenancy state travels in the
/// <see cref="Connections.SecretVaultConnection"/> passed to every method, so
/// a single adapter instance serves any number of tenancies concurrently.
/// </remarks>
public interface ISecretVault
{
    /// <summary>
    /// Gets the provider identifier used for dispatch (e.g. <c>"scaleway"</c>,
    /// <c>"vault"</c>, <c>"inmemory"</c>).
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Gets the adapter's declared capability matrix.
    /// </summary>
    SecretVaultCapabilities Capabilities { get; }

    /// <summary>Gets the secret container lifecycle port.</summary>
    ISecretContainerService Secrets { get; }

    /// <summary>Gets the immutable-version port.</summary>
    ISecretVersionService Versions { get; }
}
