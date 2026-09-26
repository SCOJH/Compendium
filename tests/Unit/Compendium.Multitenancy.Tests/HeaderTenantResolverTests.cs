// -----------------------------------------------------------------------
// <copyright file="HeaderTenantResolverTests.cs" company="Sassy Solutions">
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
/// Unit tests for the <see cref="HeaderTenantResolver"/> class.
/// </summary>
public class HeaderTenantResolverTests
{
    private readonly ITenantStore _store = Substitute.For<ITenantStore>();
    private readonly ILogger<HeaderTenantResolver> _logger = Substitute.For<ILogger<HeaderTenantResolver>>();

    [Fact]
    public void HeaderTenantResolver_Constructor_WithNullStore_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new HeaderTenantResolver(null!, new HeaderTenantResolverOptions(), _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("tenantStore");
    }

    [Fact]
    public void HeaderTenantResolver_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new HeaderTenantResolver(_store, null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void HeaderTenantResolver_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenHeaderMissing_ReturnsSuccessWithNull()
    {
        // Arrange
        var resolver = new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        await _store.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ResolveTenantAsync_WhenHeaderValueEmptyOrWhitespace_ReturnsSuccessWithNull(string headerValue)
    {
        // Arrange
        var resolver = new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext
        {
            Headers = new Dictionary<string, string> { ["X-Tenant-ID"] = headerValue }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        await _store.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenHeaderPresent_ResolvesViaStore()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "tenant-1", Name = "Tenant One" };
        _store.GetByIdAsync("tenant-1", Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext
        {
            Headers = new Dictionary<string, string> { ["X-Tenant-ID"] = "tenant-1" }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenCustomHeaderName_UsesIt()
    {
        // Arrange
        var options = new HeaderTenantResolverOptions { HeaderName = "X-Custom-Tenant" };
        var tenant = new TenantInfo { Id = "tenant-2", Name = "Tenant Two" };
        _store.GetByIdAsync("tenant-2", Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new HeaderTenantResolver(_store, options, _logger);
        var context = new TenantResolutionContext
        {
            Headers = new Dictionary<string, string>
            {
                ["X-Tenant-ID"] = "ignored",
                ["X-Custom-Tenant"] = "tenant-2"
            }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("tenant-2");
        await _store.DidNotReceive().GetByIdAsync("ignored", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenStoreReturnsNull_PropagatesNull()
    {
        // Arrange
        _store.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(null));

        var resolver = new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext
        {
            Headers = new Dictionary<string, string> { ["X-Tenant-ID"] = "tenant-x" }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenStoreFails_PropagatesFailure()
    {
        // Arrange
        var error = Error.Failure("Store.Down", "boom");
        _store.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
              .Returns(Result.Failure<TenantInfo?>(error));

        var resolver = new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext
        {
            Headers = new Dictionary<string, string> { ["X-Tenant-ID"] = "tenant-x" }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Store.Down");
    }

    [Fact]
    public async Task ResolveTenantAsync_PropagatesCancellationTokenToStore()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        _store.GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(null));

        var resolver = new HeaderTenantResolver(_store, new HeaderTenantResolverOptions(), _logger);
        var context = new TenantResolutionContext
        {
            Headers = new Dictionary<string, string> { ["X-Tenant-ID"] = "tenant-1" }
        };

        // Act
        await resolver.ResolveTenantAsync(context, cts.Token);

        // Assert
        await _store.Received(1).GetByIdAsync("tenant-1", cts.Token);
    }
}
