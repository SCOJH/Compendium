// -----------------------------------------------------------------------
// <copyright file="TenantValidationExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using Compendium.Adapters.AspNetCore.Security;
using Compendium.Multitenancy;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.AspNetCore.Tests.Security;

/// <summary>
/// Unit tests for the <see cref="TenantValidationExtensions"/> static class.
/// </summary>
public class TenantValidationExtensionsTests
{
    [Fact]
    public void AddTenantValidation_RegistersAllExpectedServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTenantValidation();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<IOptions<TenantValidationMiddlewareOptions>>().Should().NotBeNull();
        sp.GetService<TenantConsistencyOptions>().Should().NotBeNull();
        sp.GetService<ITenantConsistencyValidator>().Should().NotBeNull();

        using var scope = sp.CreateScope();
        scope.ServiceProvider.GetService<TenantContext>().Should().NotBeNull();
    }

    [Fact]
    public void AddTenantValidation_AppliesMiddlewareConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTenantValidation(
            opt => opt.TenantHeaderName = "X-Custom-Tenant",
            null);
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TenantValidationMiddlewareOptions>>().Value;

        // Assert
        options.TenantHeaderName.Should().Be("X-Custom-Tenant");
    }

    [Fact]
    public void AddTenantValidation_AppliesConsistencyConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTenantValidation(
            null,
            opt => opt.MinimumRequiredSources = 2);
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<TenantConsistencyOptions>();

        // Assert
        options.MinimumRequiredSources.Should().Be(2);
    }

    [Fact]
    public void AddTenantValidation_TenantContextIsScoped()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddTenantValidation();
        var sp = services.BuildServiceProvider();

        // Act
        TenantContext c1, c2, c3;
        using (var scope1 = sp.CreateScope())
        {
            c1 = scope1.ServiceProvider.GetRequiredService<TenantContext>();
            c2 = scope1.ServiceProvider.GetRequiredService<TenantContext>();
        }

        using (var scope2 = sp.CreateScope())
        {
            c3 = scope2.ServiceProvider.GetRequiredService<TenantContext>();
        }

        // Assert - same within scope, different across scopes
        c1.Should().BeSameAs(c2);
        c3.Should().NotBeSameAs(c1);
    }

    [Fact]
    public async Task AddTenantValidationWithInMemoryStore_RegistersTenantStoreAndSeedsTenants()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var tenantA = new TenantInfo { Id = "t-a", Name = "Alpha", IsActive = true };
        var tenantB = new TenantInfo { Id = "t-b", Name = "Beta", IsActive = true };

        // Act
        services.AddTenantValidationWithInMemoryStore(tenantA, tenantB);
        var sp = services.BuildServiceProvider();
        var store = sp.GetRequiredService<ITenantStore>();
        var resultA = await store.GetByIdAsync("t-a");
        var resultB = await store.GetByIdAsync("t-b");

        // Assert
        resultA.IsSuccess.Should().BeTrue();
        resultA.Value!.Name.Should().Be("Alpha");
        resultB.IsSuccess.Should().BeTrue();
        resultB.Value!.Name.Should().Be("Beta");
    }

    [Fact]
    public void AddTenantValidationWithInMemoryStore_NoTenants_StillRegistersStore()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddTenantValidationWithInMemoryStore();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<ITenantStore>().Should().NotBeNull();
    }

    [Fact]
    public void UseTenantValidation_WhenAppIsNull_Throws()
    {
        // Arrange
        IApplicationBuilder? app = null;

        // Act
        var act = () => app!.UseTenantValidation();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void UseTenantValidation_RegistersMiddleware()
    {
        // Arrange
        var sc = new ServiceCollection();
        sc.AddLogging();
        sc.AddTenantValidationWithInMemoryStore();
        sc.AddSingleton(new DiagnosticListener("test"));
        sc.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        var sp = sc.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);

        // Act
        var result = app.UseTenantValidation();

        // Assert
        result.Should().BeSameAs(app);
    }
}
