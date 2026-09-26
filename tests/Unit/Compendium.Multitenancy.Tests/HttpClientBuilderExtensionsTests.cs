// -----------------------------------------------------------------------
// <copyright file="HttpClientBuilderExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Multitenancy.Extensions;
using Compendium.Multitenancy.Http;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="HttpClientBuilderExtensions"/> class.
/// </summary>
public class HttpClientBuilderExtensionsTests
{
    [Fact]
    public void AddTenantPropagation_WithoutConfigure_RegistersHandler()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCompendiumMultitenancy();

        // Act
        services.AddHttpClient("downstream").AddTenantPropagation();
        var provider = services.BuildServiceProvider();

        // Assert
        var factory = provider.GetRequiredService<IHttpClientFactory>();
        var client = factory.CreateClient("downstream");
        client.Should().NotBeNull();

        // The propagation options should be registered as a singleton
        provider.GetService<TenantPropagationOptions>().Should().NotBeNull();
    }

    [Fact]
    public void AddTenantPropagation_WithConfigure_AppliesOptions()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCompendiumMultitenancy();

        // Act
        services
            .AddHttpClient("downstream")
            .AddTenantPropagation(opts =>
            {
                opts.TenantIdHeaderName = "X-My-Tenant";
                opts.IncludeTenantName = true;
            });

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<TenantPropagationOptions>();

        // Assert
        options.TenantIdHeaderName.Should().Be("X-My-Tenant");
        options.IncludeTenantName.Should().BeTrue();
    }

    [Fact]
    public void AddTenantPropagation_WithNullBuilder_ThrowsArgumentNullException()
    {
        // Arrange
        IHttpClientBuilder? builder = null;

        // Act
        var act = () => builder!.AddTenantPropagation();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void AddTenantPropagation_WithNullConfigure_ThrowsArgumentNullException()
    {
        // Arrange
        var services = new ServiceCollection();
        var builder = services.AddHttpClient("downstream");

        // Act
        var act = () => builder.AddTenantPropagation(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public void AddTenantPropagation_HandlerCanBeResolvedFromContainer()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCompendiumMultitenancy();

        // Act
        services.AddHttpClient("downstream").AddTenantPropagation();

        // The handler factory uses ITenantContextAccessor + TenantPropagationOptions + ILogger
        var provider = services.BuildServiceProvider();
        var accessor = provider.GetRequiredService<ITenantContextAccessor>();
        var opts = provider.GetRequiredService<TenantPropagationOptions>();
        var logger = provider.GetRequiredService<ILogger<TenantPropagatingDelegatingHandler>>();

        // Assert - Re-create the handler the same way the factory does
        var handler = new TenantPropagatingDelegatingHandler(accessor, opts, logger);
        handler.Should().NotBeNull();
    }
}
