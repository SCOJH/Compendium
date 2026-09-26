// -----------------------------------------------------------------------
// <copyright file="PostgreSqlHealthCheckTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.AspNetCore.Health;
using AwesomeAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Adapters.AspNetCore.Tests.Health;

/// <summary>
/// Unit tests for the <see cref="PostgreSqlHealthCheck"/> class.
/// </summary>
public class PostgreSqlHealthCheckTests
{
    [Fact]
    public void Constructor_WhenConnectionStringIsNull_Throws()
    {
        // Arrange & Act
        var act = () => new PostgreSqlHealthCheck(null!, NullLogger<PostgreSqlHealthCheck>.Instance);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionString");
    }

    [Fact]
    public void Constructor_WhenLoggerIsNull_Throws()
    {
        // Arrange & Act
        var act = () => new PostgreSqlHealthCheck("Host=localhost", null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task CheckHealthAsync_WhenConnectionFails_ReturnsUnhealthy()
    {
        // Arrange
        // Use an unreachable host to force a connection failure quickly
        var check = new PostgreSqlHealthCheck(
            "Host=127.0.0.1;Port=1;Username=u;Password=p;Database=x;Timeout=1;Command Timeout=1",
            NullLogger<PostgreSqlHealthCheck>.Instance);

        // Act
        var result = await check.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);

        // Assert
        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("PostgreSQL is unhealthy");
        result.Exception.Should().NotBeNull();
    }

    [Fact]
    public void CheckHealthAsync_AccessibleAsHealthCheckInstance()
    {
        // Arrange & Act
        var check = new PostgreSqlHealthCheck(
            "Host=localhost",
            NullLogger<PostgreSqlHealthCheck>.Instance);

        // Assert - sanity that it implements IHealthCheck
        check.Should().BeAssignableTo<IHealthCheck>();
    }
}
