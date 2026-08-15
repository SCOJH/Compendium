// -----------------------------------------------------------------------
// <copyright file="InMemorySecretVaultContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Testing.Secrets;

namespace Compendium.Abstractions.Secrets.Tests;

/// <summary>
/// Subscribes <see cref="InMemorySecretVault"/> to the vault behavioral
/// contract, keeping the fake aligned with real adapters.
/// </summary>
public sealed class InMemorySecretVaultContractTests : SecretVaultContractTests
{
    private readonly InMemorySecretVault _vault = new();

    /// <inheritdoc />
    protected override ISecretVault Vault => _vault;

    /// <inheritdoc />
    protected override SecretVaultConnection Connection { get; } = new()
    {
        Provider = InMemorySecretVault.ProviderName,
        Credential = new SecretVaultCredential.None(),
    };
}
