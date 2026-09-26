// -----------------------------------------------------------------------
// <copyright file="RedisHealthCheckTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.AspNetCore.Health;
using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StackExchange.Redis;

namespace Compendium.Adapters.AspNetCore.Tests.Health;

/// <summary>
/// Unit tests for the <see cref="RedisHealthCheck"/> class.
/// </summary>
public class RedisHealthCheckTests
{
    [Fact]
    public void Constructor_WhenRedisIsNull_Throws()
    {
        // Arrange & Act
        var act = () => new RedisHealthCheck(null!, NullLogger<RedisHealthCheck>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("redis");
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_Throws()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();

        // Act
        var act = () => new RedisHealthCheck(redis, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPingIsFast_ReturnsHealthy()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(10));

        var check = new RedisHealthCheck(redis, NullLogger<RedisHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("Redis is healthy");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPingIsSlow_ReturnsDegraded()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.PingAsync(Arg.Any<CommandFlags>()).Returns(TimeSpan.FromMilliseconds(2000));

        var check = new RedisHealthCheck(redis, NullLogger<RedisHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("Redis is slow");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenPingThrows_ReturnsUnhealthy()
    {
        // Arrange
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object?>()).Returns(db);
        db.PingAsync(Arg.Any<CommandFlags>())
            .Returns<Task<TimeSpan>>(_ => throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "down"));

        var check = new RedisHealthCheck(redis, NullLogger<RedisHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Redis is unhealthy");
        result.Exception.Should().BeOfType<RedisConnectionException>();
    }
}
