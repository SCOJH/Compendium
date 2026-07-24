// -----------------------------------------------------------------------
// <copyright file="ScalewayAdapterWiringTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Adapters.Scaleway.SecretManager.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Compendium.Adapters.Scaleway.SecretManager.Tests;

/// <summary>
/// DI registration and declared capability matrix of the Scaleway adapter.
/// </summary>
public sealed class ScalewayAdapterWiringTests
{
    [Fact]
    public void AddScalewaySecretVault_ContributesTheFacade_AndIsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddScalewaySecretVault(o => o.DefaultRegion = "nl-ams");
        services.AddScalewaySecretVault();

        using var provider = services.BuildServiceProvider();
        var vaults = provider.GetServices<ISecretVault>().ToList();

        vaults.Should().ContainSingle(v => v.Provider == "scaleway");
        provider.GetRequiredService<ScalewaySecretVault>().Should().BeSameAs(vaults.Single());
    }

    [Fact]
    public void Capabilities_DeclareTheDocumentedMatrix()
    {
        var services = new ServiceCollection();
        services.AddScalewaySecretVault();
        using var provider = services.BuildServiceProvider();
        var vault = provider.GetRequiredService<ScalewaySecretVault>();

        vault.Capabilities.Provider.Should().Be("scaleway");
        vault.Capabilities.Supports(SecretVaultCapability.ImmutableVersions).Should().BeTrue();
        vault.Capabilities.Supports(SecretVaultCapability.VersionEnableDisable).Should().BeTrue();
        vault.Capabilities.Supports(SecretVaultCapability.VersionDestroy).Should().BeTrue();
        vault.Capabilities.Supports(SecretVaultCapability.PathHierarchy).Should().BeTrue();
        vault.Capabilities.Supports(SecretVaultCapability.Tags).Should().BeTrue();
        vault.Capabilities.Supports(SecretVaultCapability.LargePayload).Should().BeFalse();
        vault.Capabilities.Supports(SecretVaultCapability.ServerSideRotation).Should().BeFalse();

        var refused = vault.Capabilities.EnsureSupported(SecretVaultCapability.LargePayload);
        refused.IsFailure.Should().BeTrue();
        refused.Error.Metadata.Should().ContainKey("limitation");

        vault.Secrets.Should().NotBeNull();
        vault.Versions.Should().NotBeNull();
    }
}
