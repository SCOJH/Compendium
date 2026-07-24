// -----------------------------------------------------------------------
// <copyright file="NullSecretVaultTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Abstractions.Secrets.Stubs;
using FluentAssertions;
using Xunit;

namespace Compendium.Abstractions.Secrets.Tests;

/// <summary>
/// The null vault must fail every operation with the standard
/// not-configured error and declare no capabilities.
/// </summary>
public sealed class NullSecretVaultTests
{
    private static readonly SecretVaultConnection Connection = new()
    {
        Provider = NullSecretVault.ProviderName,
        Credential = new SecretVaultCredential.None(),
    };

    private readonly NullSecretVault _vault = new();

    [Fact]
    public void DeclaresNoCapabilities()
    {
        _vault.Provider.Should().Be("null");
        _vault.Capabilities.Entries.Should().BeEmpty();
        Enum.GetValues<SecretVaultCapability>()
            .Should().OnlyContain(c => !_vault.Capabilities.Supports(c));
    }

    [Fact]
    public async Task EveryOperation_FailsWithNotConfigured()
    {
        var expected = $"{SecretVaultErrors.Prefix}.NotConfigured";
        var create = await _vault.Secrets.CreateAsync(Connection, new CreateVaultSecret
        {
            Name = "any",
            Path = SecretScopePath.Root,
        });
        var get = await _vault.Secrets.GetAsync(Connection, "id");
        var list = await _vault.Secrets.ListAsync(Connection, SecretScopePath.Root);
        var delete = await _vault.Secrets.DeleteAsync(Connection, "id");
        var add = await _vault.Versions.AddAsync(Connection, "id", SecretMaterial.FromString("x"));
        var access = await _vault.Versions.AccessAsync(Connection, "id", 1);
        var versions = await _vault.Versions.ListAsync(Connection, "id");
        var enable = await _vault.Versions.EnableAsync(Connection, "id", 1);
        var disable = await _vault.Versions.DisableAsync(Connection, "id", 1);
        var destroy = await _vault.Versions.DestroyAsync(Connection, "id", 1);

        create.Error.Code.Should().Be(expected);
        get.Error.Code.Should().Be(expected);
        list.Error.Code.Should().Be(expected);
        delete.Error.Code.Should().Be(expected);
        add.Error.Code.Should().Be(expected);
        access.Error.Code.Should().Be(expected);
        versions.Error.Code.Should().Be(expected);
        enable.Error.Code.Should().Be(expected);
        disable.Error.Code.Should().Be(expected);
        destroy.Error.Code.Should().Be(expected);
    }
}
