// -----------------------------------------------------------------------
// <copyright file="InMemorySecretVaultContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Testing.Secrets;

namespace Compendium.Testing.Tests.Contracts;

/// <summary>
/// Runs the secret-vault contract against <see cref="InMemorySecretVault"/>
/// from inside the Testing test project (see the sibling git contract file
/// for why the duplication with Compendium.Abstractions.Secrets.Tests is
/// deliberate: one max-merged coverage report per assembly).
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
