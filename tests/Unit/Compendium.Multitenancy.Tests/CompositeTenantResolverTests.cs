// -----------------------------------------------------------------------
// <copyright file="CompositeTenantResolverTests.cs" company="Sassy Solutions">
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
/// Unit tests for the <see cref="CompositeTenantResolver"/> class.
/// </summary>
public class CompositeTenantResolverTests
{
    private readonly ILogger<CompositeTenantResolver> _logger = Substitute.For<ILogger<CompositeTenantResolver>>();

    [Fact]
    public void CompositeTenantResolver_Constructor_WithNullResolvers_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new CompositeTenantResolver(null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("resolvers");
    }

    [Fact]
    public void CompositeTenantResolver_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var resolvers = new List<ITenantResolver>();

        // Act
        var act = () => new CompositeTenantResolver(resolvers, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenContextIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var resolver = new CompositeTenantResolver(Array.Empty<ITenantResolver>(), _logger);

        // Act
        var result = await resolver.ResolveTenantAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TenantResolution.NullContext");
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenNoResolvers_ReturnsSuccessWithNull()
    {
        // Arrange
        var resolver = new CompositeTenantResolver(Array.Empty<ITenantResolver>(), _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenFirstResolverSucceeds_ReturnsItsResult()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "tenant-1", Name = "Tenant One" };

        var first = Substitute.For<ITenantResolver>();
        first.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns(Result.Success<TenantInfo?>(tenant));

        var second = Substitute.For<ITenantResolver>();

        var resolver = new CompositeTenantResolver(new[] { first, second }, _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
        await second.DidNotReceive().ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenFirstResolverReturnsNull_FallsBackToSecond()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "tenant-2", Name = "Tenant Two" };

        var first = Substitute.For<ITenantResolver>();
        first.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns(Result.Success<TenantInfo?>(null));

        var second = Substitute.For<ITenantResolver>();
        second.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new CompositeTenantResolver(new[] { first, second }, _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenResolverFails_LogsWarningAndContinues()
    {
        // Arrange
        var first = Substitute.For<ITenantResolver>();
        first.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns(Result.Failure<TenantInfo?>(Error.Validation("First.Failed", "boom")));

        var tenant = new TenantInfo { Id = "tenant-3", Name = "Tenant Three" };
        var second = Substitute.For<ITenantResolver>();
        second.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new CompositeTenantResolver(new[] { first, second }, _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenResolverThrows_SwallowsExceptionAndContinues()
    {
        // Arrange
        var first = Substitute.For<ITenantResolver>();
        first.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns<Task<Result<TenantInfo?>>>(_ => throw new InvalidOperationException("fatal"));

        var tenant = new TenantInfo { Id = "tenant-4", Name = "Tenant Four" };
        var second = Substitute.For<ITenantResolver>();
        second.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(tenant));

        var resolver = new CompositeTenantResolver(new[] { first, second }, _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(tenant);
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenAllResolversReturnNull_ReturnsSuccessWithNull()
    {
        // Arrange
        var first = Substitute.For<ITenantResolver>();
        first.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns(Result.Success<TenantInfo?>(null));

        var second = Substitute.For<ITenantResolver>();
        second.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
              .Returns(Result.Success<TenantInfo?>(null));

        var resolver = new CompositeTenantResolver(new[] { first, second }, _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WhenAllResolversFail_ReturnsSuccessWithNull()
    {
        // Arrange
        var first = Substitute.For<ITenantResolver>();
        first.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns(Result.Failure<TenantInfo?>(Error.Validation("F.E", "first failed")));

        var second = Substitute.For<ITenantResolver>();
        second.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
              .Returns(Result.Failure<TenantInfo?>(Error.Validation("S.E", "second failed")));

        var resolver = new CompositeTenantResolver(new[] { first, second }, _logger);
        var context = new TenantResolutionContext();

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert - Composite returns null when none succeed
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_PropagatesCancellationTokenToResolvers()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var inner = Substitute.For<ITenantResolver>();
        inner.ResolveTenantAsync(Arg.Any<TenantResolutionContext>(), Arg.Any<CancellationToken>())
             .Returns(Result.Success<TenantInfo?>(null));

        var resolver = new CompositeTenantResolver(new[] { inner }, _logger);
        var context = new TenantResolutionContext();

        // Act
        await resolver.ResolveTenantAsync(context, cts.Token);

        // Assert
        await inner.Received(1).ResolveTenantAsync(context, cts.Token);
    }
}
