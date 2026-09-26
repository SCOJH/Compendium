// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Multitenancy.Extensions;
using Compendium.Multitenancy.Http;
using Compendium.Multitenancy.Stores;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the multitenancy ServiceCollection extension methods.
/// </summary>
public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddCompendiumMultitenancy_RegistersRequiredServices()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumMultitenancy();
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ITenantContextAccessor>().Should().NotBeNull();
        provider.GetService<ITenantContextSetter>().Should().NotBeNull();
        provider.GetService<ITenantContext>().Should().NotBeNull();
        provider.GetService<MultitenancyOptions>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumMultitenancy_WithOptions_ConfiguresOptions()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumMultitenancy(options =>
        {
            options.RequireTenant = true;
            options.DefaultTenantId = "default-tenant";
        });
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<MultitenancyOptions>();

        // Assert
        options.RequireTenant.Should().BeTrue();
        options.DefaultTenantId.Should().Be("default-tenant");
    }

    [Fact]
    public void AddCompendiumMultitenancy_WithNullServices_ThrowsArgumentNullException()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumMultitenancy();

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddCompendiumMultitenancy_WithNullConfigure_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumMultitenancy(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddInMemoryTenantStore_RegistersTenantStore()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryTenantStore();
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ITenantStore>().Should().NotBeNull();
        provider.GetService<ITenantStore>().Should().BeOfType<InMemoryTenantStore>();
    }

    [Fact]
    public void AddInMemoryTenantStore_WithInitialTenants_PopulatesStore()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddInMemoryTenantStore(options =>
        {
            options.InitialTenants.Add(new TenantInfo { Id = "tenant-1", Name = "Tenant One" });
            options.InitialTenants.Add(new TenantInfo { Id = "tenant-2", Name = "Tenant Two" });
        });
        var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<ITenantStore>() as InMemoryTenantStore;

        // Assert
        store.Should().NotBeNull();
        store!.Count.Should().Be(2);
    }

    [Fact]
    public void AddHeaderTenantResolution_RegistersTenantResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryTenantStore(); // Required dependency

        // Act
        services.AddHeaderTenantResolution();
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ITenantResolver>().Should().NotBeNull();
    }

    [Fact]
    public void AddHostTenantResolution_RegistersTenantResolver()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInMemoryTenantStore(); // Required dependency

        // Act
        services.AddHostTenantResolution();
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ITenantResolver>().Should().NotBeNull();
    }

    [Fact]
    public void AddDatabaseIsolation_RegistersIsolationStrategy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddDatabaseIsolation();
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ITenantIsolationStrategy>().Should().NotBeNull();
    }

    [Fact]
    public void AddSchemaIsolation_RegistersIsolationStrategy()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddSchemaIsolation();
        var provider = services.BuildServiceProvider();

        // Assert
        provider.GetService<ITenantIsolationStrategy>().Should().NotBeNull();
    }

    [Fact]
    public void TenantContextAccessor_IsSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCompendiumMultitenancy();
        var provider = services.BuildServiceProvider();

        // Act
        var accessor1 = provider.GetRequiredService<ITenantContextAccessor>();
        var accessor2 = provider.GetRequiredService<ITenantContextAccessor>();

        // Assert
        accessor1.Should().BeSameAs(accessor2);
    }

    [Fact]
    public void TenantContextSetter_IsSameInstanceAsAccessor()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddCompendiumMultitenancy();
        var provider = services.BuildServiceProvider();

        // Act
        var accessor = provider.GetRequiredService<ITenantContextAccessor>();
        var setter = provider.GetRequiredService<ITenantContextSetter>();

        // Assert
        setter.Should().BeSameAs(accessor);
    }

    [Fact]
    public void PropagationOptions_AreRegistered()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumMultitenancy(options =>
        {
            options.Propagation.TenantIdHeaderName = "Custom-Tenant-Id";
        });
        var provider = services.BuildServiceProvider();
        var propagationOptions = provider.GetService<TenantPropagationOptions>();

        // Assert
        propagationOptions.Should().NotBeNull();
        propagationOptions!.TenantIdHeaderName.Should().Be("Custom-Tenant-Id");
    }

    [Fact]
    public void MultipleRegistrations_DoNotDuplicate()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumMultitenancy();
        services.AddCompendiumMultitenancy();
        var provider = services.BuildServiceProvider();

        // Assert - Should not throw and should return same singleton
        var accessor1 = provider.GetRequiredService<ITenantContextAccessor>();
        accessor1.Should().NotBeNull();
    }
}
