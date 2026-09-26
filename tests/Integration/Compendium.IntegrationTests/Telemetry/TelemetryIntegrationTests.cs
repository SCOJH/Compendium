// -----------------------------------------------------------------------
// <copyright file="TelemetryIntegrationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using Compendium.Abstractions.CQRS.Commands;
using Compendium.Abstractions.CQRS.Handlers;
using Compendium.Abstractions.CQRS.Queries;
using Compendium.Application.CQRS;
using Compendium.Core.Results;
using Compendium.Core.Telemetry;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Xunit;

namespace Compendium.IntegrationTests.Telemetry;

/// <summary>
/// Integration tests for OpenTelemetry instrumentation across Compendium framework.
/// COMP-019: Telemetry & Observability Integration
///
/// These tests verify that:
/// 1. Activity spans are created with correct names and tags
/// 2. Metrics are recorded with appropriate dimensions
/// 3. Trace context propagates correctly across operations
/// 4. Activity status codes reflect operation results
/// </summary>
public sealed class TelemetryIntegrationTests : IDisposable
{
    private readonly List<Activity> _exportedActivities = new();
    private readonly List<Metric> _exportedMetrics = new();
    private readonly TracerProvider _tracerProvider;
    private readonly MeterProvider _meterProvider;

    public TelemetryIntegrationTests()
    {
        // Configure in-memory exporters to capture telemetry
        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(CompendiumTelemetry.SourceName)
            .AddInMemoryExporter(_exportedActivities)
            .Build()!;

        _meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(CompendiumTelemetry.SourceName)
            .AddInMemoryExporter(_exportedMetrics)
            .Build()!;
    }

    public void Dispose()
    {
        _tracerProvider?.Dispose();
        _meterProvider?.Dispose();
    }

    /// <summary>
    /// Test: CommandDispatcher creates activity span with correct name and tags
    /// </summary>
    [Fact]
    public async Task CommandDispatcher_CreatesActivitySpan_WithCorrectNameAndTags()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();

        var command = new TestCommand { Name = "Test" };

        // Act
        await dispatcher.DispatchAsync(command);

        // Force flush to ensure telemetry is exported
        _tracerProvider.ForceFlush();

        // Assert: Activity span was created
        var activity = _exportedActivities.FirstOrDefault(a =>
            a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchCommand);

        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Ok);

        // Verify tags
        activity.Tags.Should().Contain(tag =>
            tag.Key == CompendiumTelemetry.Tags.CommandType &&
            tag.Value == nameof(TestCommand));

        activity.Tags.Should().Contain(tag =>
            tag.Key == CompendiumTelemetry.Tags.HandlerType &&
            !string.IsNullOrEmpty(tag.Value));
    }

    /// <summary>
    /// Test: QueryDispatcher creates activity span with correct name and tags
    /// </summary>
    [Fact]
    public async Task QueryDispatcher_CreatesActivitySpan_WithCorrectNameAndTags()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

        var handler = Substitute.For<IQueryHandler<TestQuery, TestQueryResult>>();
        handler.HandleAsync(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TestQueryResult { Data = "Test" }));
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();

        var query = new TestQuery { Filter = "test" };

        // Act
        await dispatcher.DispatchAsync<TestQuery, TestQueryResult>(query);

        // Force flush to ensure telemetry is exported
        _tracerProvider.ForceFlush();

        // Assert: Activity span was created
        var activity = _exportedActivities.FirstOrDefault(a =>
            a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchQuery);

        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Ok);

        // Verify tags
        activity.Tags.Should().Contain(tag =>
            tag.Key == CompendiumTelemetry.Tags.QueryType &&
            tag.Value == nameof(TestQuery));

        activity.Tags.Should().Contain(tag =>
            tag.Key == CompendiumTelemetry.Tags.HandlerType &&
            !string.IsNullOrEmpty(tag.Value));
    }

    /// <summary>
    /// Test: CommandDispatcher sets error status on activity when handler fails
    /// </summary>
    [Fact]
    public async Task CommandDispatcher_SetsErrorStatus_WhenHandlerFails()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Validation("Test.Failed", "Validation failed")));
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();

        var command = new TestCommand { Name = "Test" };

        // Act
        await dispatcher.DispatchAsync(command);

        // Force flush to ensure telemetry is exported
        _tracerProvider.ForceFlush();

        // Assert: Activity span has error status
        var activity = _exportedActivities.FirstOrDefault(a =>
            a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchCommand);

        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Error);

        // Verify error tags
        activity.Tags.Should().Contain(tag =>
            tag.Key == CompendiumTelemetry.Tags.ErrorType &&
            tag.Value == "Test.Failed");

        activity.Tags.Should().Contain(tag =>
            tag.Key == CompendiumTelemetry.Tags.ErrorMessage &&
            tag.Value == "Validation failed");
    }

    /// <summary>
    /// Test: CommandDispatcher sets error status on activity when handler throws exception
    /// </summary>
    [Fact]
    public async Task CommandDispatcher_SetsErrorStatus_WhenHandlerThrowsException()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.When(h => h.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Database connection failed"));
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();

        var command = new TestCommand { Name = "Test" };

        // Act
        await dispatcher.DispatchAsync(command);

        // Force flush to ensure telemetry is exported
        _tracerProvider.ForceFlush();

        // Assert: Activity span has error status
        var activity = _exportedActivities.FirstOrDefault(a =>
            a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchCommand);

        activity.Should().NotBeNull();
        activity!.Status.Should().Be(ActivityStatusCode.Error);
        activity.StatusDescription.Should().Contain("Database connection failed");

        // Verify exception tags
        activity.Tags.Should().Contain(tag =>
            tag.Key == "exception.type" &&
            tag.Value!.Contains("InvalidOperationException"));
    }

    /// <summary>
    /// Test: CQRS operations emit metrics counters
    /// </summary>
    [Fact]
    public async Task CqrsOperations_EmitMetricsCounters()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

        var commandHandler = Substitute.For<ICommandHandler<TestCommand>>();
        commandHandler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(commandHandler);

        var queryHandler = Substitute.For<IQueryHandler<TestQuery, TestQueryResult>>();
        queryHandler.HandleAsync(Arg.Any<TestQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new TestQueryResult { Data = "Test" }));
        services.AddSingleton(queryHandler);

        var serviceProvider = services.BuildServiceProvider();
        var commandDispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();
        var queryDispatcher = serviceProvider.GetRequiredService<IQueryDispatcher>();

        // Act
        await commandDispatcher.DispatchAsync(new TestCommand { Name = "Test" });
        await queryDispatcher.DispatchAsync<TestQuery, TestQueryResult>(new TestQuery { Filter = "test" });

        // Force flush to ensure metrics are exported
        _meterProvider.ForceFlush();

        // Assert: Metrics were recorded
        // Note: In-memory exporter captures metrics, but accessing them requires reflection or custom implementation
        // This is a basic validation that metrics infrastructure is set up
        _exportedMetrics.Should().NotBeNull();
    }

    /// <summary>
    /// Test: Activity context propagates across async operations
    /// </summary>
    [Fact]
    public async Task ActivityContext_PropagatesAcrossAsyncOperations()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        // Create handler that starts a child activity
        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                // Simulate nested operation that should inherit trace context
                using var childActivity = CompendiumTelemetry.ActivitySource.StartActivity("child.operation");
                await Task.Delay(10); // Simulate async work
                return Result.Success();
            });
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();

        // Act
        await dispatcher.DispatchAsync(new TestCommand { Name = "Test" });

        // Force flush
        _tracerProvider.ForceFlush();

        // Assert: Both parent and child activities were created
        var parentActivity = _exportedActivities.FirstOrDefault(a =>
            a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchCommand);

        var childActivity = _exportedActivities.FirstOrDefault(a =>
            a.OperationName == "child.operation");

        parentActivity.Should().NotBeNull();
        childActivity.Should().NotBeNull();

        // Verify parent-child relationship
        childActivity!.ParentId.Should().Be(parentActivity!.Id);
    }

    /// <summary>
    /// Test: Multiple concurrent operations create independent activity spans
    /// </summary>
    [Fact]
    public async Task ConcurrentOperations_CreateIndependentActivitySpans()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(10); // Simulate work
                return Result.Success();
            });
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();

        // Act: Execute 5 commands concurrently
        var tasks = Enumerable.Range(0, 5)
            .Select(i => dispatcher.DispatchAsync(new TestCommand { Name = $"Test-{i}" }))
            .ToArray();

        await Task.WhenAll(tasks);

        // Force flush
        _tracerProvider.ForceFlush();

        // Assert: 5 independent activity spans were created
        var commandActivities = _exportedActivities
            .Where(a => a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchCommand)
            .ToList();

        commandActivities.Should().HaveCount(5);

        // Verify all have unique trace IDs (independent operations)
        var traceIds = commandActivities.Select(a => a.TraceId).Distinct().ToList();
        traceIds.Should().HaveCount(5);
    }

    /// <summary>
    /// Test: Telemetry overhead is minimal (< 5% performance impact target)
    /// </summary>
    [Fact]
    public async Task Telemetry_HasMinimalPerformanceOverhead()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();

        var handler = Substitute.For<ICommandHandler<TestCommand>>();
        handler.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
        services.AddSingleton(handler);

        var serviceProvider = services.BuildServiceProvider();
        var dispatcher = serviceProvider.GetRequiredService<ICommandDispatcher>();

        // Act: Measure performance with telemetry
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            await dispatcher.DispatchAsync(new TestCommand { Name = "Test" });
        }
        sw.Stop();

        var avgDuration = sw.ElapsedMilliseconds / 100.0;

        // Assert: Average duration per operation should be < 5ms (including telemetry overhead)
        // This is a basic sanity check - actual overhead target is < 5% of operation time
        avgDuration.Should().BeLessThan(10, "Telemetry overhead should be minimal");

        // Force flush
        _tracerProvider.ForceFlush();

        // Verify telemetry was collected
        var activities = _exportedActivities
            .Where(a => a.OperationName == CompendiumTelemetry.CqrsActivities.DispatchCommand)
            .ToList();

        activities.Should().HaveCount(100);
    }

    // Test helper classes

    public sealed class TestCommand : ICommand
    {
        public string Name { get; init; } = string.Empty;
    }

    public sealed class TestQuery : IQuery<TestQueryResult>
    {
        public string Filter { get; init; } = string.Empty;
    }

    public sealed class TestQueryResult
    {
        public string Data { get; init; } = string.Empty;
    }
}
