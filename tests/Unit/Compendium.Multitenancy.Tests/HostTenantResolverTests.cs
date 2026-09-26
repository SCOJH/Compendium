// -----------------------------------------------------------------------
// <copyright file="HostTenantResolverTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Results;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="HostTenantResolver"/> class.
/// </summary>
public class HostTenantResolverTests
{
    private readonly ITenantStore _store = Substitute.For<ITenantStore>();
    private readonly ILogger<HostTenantResolver> _logger = Substitute.For<ILogger<HostTenantResolver>>();

    [Fact]
    public void HostTenantResolver_Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new HostTenantResolver(null!, new HostTenantResolverOptions(), _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantStore");
    }

    [Fact]
    public void HostTenantResolver_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new HostTenantResolver(_store, null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void HostTenantResolver_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new HostTenantResolver(_store, new HostTenantResolverOptions(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveTenantAsync_WhenHostMissingOrBlank_ReturnsSuccessWithNull(string? host)
    {
        // Arrange
        var resolver = new HostTenantResolver(_store, new HostTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext { Host = host };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        await _store.DidNotReceive().GetByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenSubdomainEnabled_ExtractsSubdomain()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "acme", Name = "Acme" };
        _store.GetByIdentifierAsync("acme", Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new HostTenantResolver(_store, new HostTenantResolverOptions { UseSubdomain = true }, _logger);
        var context = new TenantResolutionContext { Host = "Acme.example.com" };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
        await _store.Received(1).GetByIdentifierAsync("acme", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenSubdomainEnabledButOnlyTwoSegments_FallsBackToFullHost()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "example.com", Name = "Apex" };
        _store.GetByIdentifierAsync("example.com", Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new HostTenantResolver(_store, new HostTenantResolverOptions { UseSubdomain = true }, _logger);
        var context = new TenantResolutionContext { Host = "Example.com" };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
        await _store.Received(1).GetByIdentifierAsync("example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenSubdomainDisabled_UsesFullHostLowercased()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "tenant-host", Name = "Host Tenant" };
        _store.GetByIdentifierAsync("acme.example.com", Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new HostTenantResolver(_store, new HostTenantResolverOptions { UseSubdomain = false }, _logger);
        var context = new TenantResolutionContext { Host = "Acme.Example.COM" };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
        await _store.Received(1).GetByIdentifierAsync("acme.example.com", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenStoreReturnsNull_PropagatesNull()
    {
        // Arrange
        _store.GetByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(null));

        var resolver = new HostTenantResolver(_store, new HostTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext { Host = "missing.example.com" };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_PropagatesCancellationTokenToStore()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _store.GetByIdentifierAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(null));

        var resolver = new HostTenantResolver(_store, new HostTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext { Host = "tenant.example.com" };

        // Act
        await resolver.ResolveTenantAsync(context, cts.Token);

        // Assert
        await _store.Received(1).GetByIdentifierAsync("tenant", cts.Token);
    }

    [Fact]
    public void HostTenantResolverOptions_Defaults_AreSensible()
    {
        // Arrange & Act
        var options = new HostTenantResolverOptions();

        // Assert
        options.UseSubdomain.Should().BeTrue();
    }
}
