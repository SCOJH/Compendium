// -----------------------------------------------------------------------
// <copyright file="ApplicationTestHelpersTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Testing.TestHelpers;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Compendium.Testing.Tests.TestHelpers;

/// <summary>
/// Unit tests for the <see cref="ApplicationTestHelpers"/> extension methods.
/// </summary>
public class ApplicationTestHelpersTests
{
    [Fact]
    public void AddTestApplication_OnEmptyServiceCollection_ReturnsSameInstance()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddTestApplication();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddTestApplication_OnEmptyServiceCollection_DoesNotAddRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        var initialCount = services.Count;

        // Act
        services.AddTestApplication();

        // Assert
        services.Count.Should().Be(initialCount);
    }

    [Fact]
    public void AddTestApplication_OnPopulatedServiceCollection_PreservesExistingRegistrations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ISampleService, SampleService>();
        var initialCount = services.Count;

        // Act
        services.AddTestApplication();

        // Assert
        services.Count.Should().Be(initialCount);
        services.Should().ContainSingle(d => d.ServiceType == typeof(ISampleService));
    }

    [Fact]
    public void AddTestApplication_AllowsBuildingServiceProvider()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddTestApplication();

        // Act
        var provider = services.BuildServiceProvider();

        // Assert
        provider.Should().NotBeNull();
    }

    [Fact]
    public void AddTestApplication_IsChainable_ReturnsBuilderForFluentRegistration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services
            .AddTestApplication()
            .AddSingleton<ISampleService, SampleService>();

        // Assert
        result.Should().BeSameAs(services);
        result.Should().ContainSingle(d => d.ServiceType == typeof(ISampleService));
    }

    private interface ISampleService;

    private sealed class SampleService : ISampleService;
}
