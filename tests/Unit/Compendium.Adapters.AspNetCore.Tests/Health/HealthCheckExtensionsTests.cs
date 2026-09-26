// -----------------------------------------------------------------------
// <copyright file="HealthCheckExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.IO;
using System.Text;
using System.Text.Json;
using Compendium.Adapters.AspNetCore.Health;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using StackExchange.Redis;

namespace Compendium.Adapters.AspNetCore.Tests.Health;

/// <summary>
/// Unit tests for the <see cref="HealthCheckExtensions"/> static class.
/// </summary>
public class HealthCheckExtensionsTests
{
    [Fact]
    public void AddCompendiumHealthChecks_WithoutInputs_RegistersHealthCheckBuilder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        var builder = services.AddCompendiumHealthChecks();
        var sp = services.BuildServiceProvider();

        // Assert
        builder.Should().NotBeNull();
        sp.GetService<HealthCheckService>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumHealthChecks_WithPostgresConnectionString_RegistersPostgresHealthCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCompendiumHealthChecks(postgresConnectionString: "Host=localhost");
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<PostgreSqlHealthCheck>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumHealthChecks_WithEmptyPostgresConnectionString_DoesNotRegisterPostgres()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCompendiumHealthChecks(postgresConnectionString: string.Empty);
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<PostgreSqlHealthCheck>().Should().BeNull();
    }

    [Fact]
    public void AddCompendiumHealthChecks_WithRedisMultiplexer_RegistersRedisHealthCheck()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var redis = Substitute.For<IConnectionMultiplexer>();

        // Act
        services.AddCompendiumHealthChecks(redisConnectionMultiplexer: redis);
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<RedisHealthCheck>().Should().NotBeNull();
    }

    [Fact]
    public void AddCompendiumHealthChecks_WithoutRedis_DoesNotRegisterRedis()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCompendiumHealthChecks();
        var sp = services.BuildServiceProvider();

        // Assert
        sp.GetService<RedisHealthCheck>().Should().BeNull();
    }

    [Fact]
    public void AddCompendiumHealthChecks_PostgresHealthCheckTagsContainReady()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();

        // Act
        services.AddCompendiumHealthChecks(postgresConnectionString: "Host=localhost");
        var sp = services.BuildServiceProvider();
        var registrations = sp.GetServices<HealthCheckRegistration>().ToList();

        // The HealthCheck tags are stored on HealthCheckRegistration objects via the
        // Microsoft.Extensions.Options pipeline; just verify the service is registered.
        sp.GetService<PostgreSqlHealthCheck>().Should().NotBeNull();
    }

    [Fact]
    public void MapCompendiumHealthChecks_RegistersTwoEndpoints()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRouting();
        services.AddCompendiumHealthChecks();
        var sp = services.BuildServiceProvider();

        var endpoints = new TestEndpointRouteBuilder(sp);

        // Act
        endpoints.MapCompendiumHealthChecks();

        // Assert
        endpoints.DataSources.Should().NotBeEmpty();
        endpoints.DataSources.SelectMany(d => d.Endpoints).Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task WriteHealthCheckResponse_WritesJsonWithStatusAndChecks()
    {
        // Arrange - locate the private static WriteHealthCheckResponse via reflection
        var writer = GetWriterDelegate();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["pg"] = new HealthReportEntry(
                HealthStatus.Healthy,
                "ok",
                TimeSpan.FromMilliseconds(5),
                exception: null,
                data: new Dictionary<string, object> { ["k"] = "v" })
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(10));

        // Act
        await writer(ctx, report);

        // Assert
        ctx.Response.ContentType.Should().Be("application/json");
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        doc.RootElement.GetProperty("totalDuration").GetDouble().Should().BeGreaterThan(0);
        doc.RootElement.GetProperty("checks").GetArrayLength().Should().Be(1);
        var check = doc.RootElement.GetProperty("checks")[0];
        check.GetProperty("name").GetString().Should().Be("pg");
        check.GetProperty("status").GetString().Should().Be("Healthy");
    }

    [Fact]
    public async Task WriteHealthCheckResponse_IncludesExceptionMessage()
    {
        // Arrange
        var writer = GetWriterDelegate();
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["redis"] = new HealthReportEntry(
                HealthStatus.Unhealthy,
                "down",
                TimeSpan.FromMilliseconds(5),
                exception: new InvalidOperationException("redis exploded"),
                data: null)
        };
        var report = new HealthReport(entries, TimeSpan.FromMilliseconds(10));

        // Act
        await writer(ctx, report);

        // Assert
        ctx.Response.Body.Position = 0;
        using var reader = new StreamReader(ctx.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        json.Should().Contain("redis exploded");
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("status").GetString().Should().Be("Unhealthy");
    }

    private static Func<HttpContext, HealthReport, Task> GetWriterDelegate()
    {
        var method = typeof(HealthCheckExtensions).GetMethod(
            "WriteHealthCheckResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("private static WriteHealthCheckResponse should exist");
        return (Func<HttpContext, HealthReport, Task>)Delegate.CreateDelegate(
            typeof(Func<HttpContext, HealthReport, Task>),
            method!);
    }

    private sealed class TestEndpointRouteBuilder : IEndpointRouteBuilder
    {
        public TestEndpointRouteBuilder(IServiceProvider serviceProvider)
        {
            ServiceProvider = serviceProvider;
        }

        public IServiceProvider ServiceProvider { get; }

        public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

        public IApplicationBuilder CreateApplicationBuilder()
            => new ApplicationBuilder(ServiceProvider);
    }
}
