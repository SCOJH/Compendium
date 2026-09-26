// -----------------------------------------------------------------------
// <copyright file="LiveProjectionE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.EventSourcing;
using Compendium.Infrastructure.Projections;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using Compendium.IntegrationTests.EndToEnd.TestProjections;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario 8: Live Projection Processing.
/// Tests real-time projection updates as events are appended to the event store.
/// </summary>
[Trait("Category", "E2E")]
[Trait("Category", "LiveProjection")]
public sealed class LiveProjectionE2ETests : IAsyncLifetime
{
    private InMemoryStreamingEventStore? _eventStore;
    private InMemoryProjectionStore? _projectionStore;
    private LiveProjectionProcessor? _liveProcessor;
    private ServiceProvider? _provider;

    public Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));

        services.Configure<ProjectionOptions>(options =>
        {
            options.RebuildBatchSize = 100;
            options.MaxConcurrentRebuilds = 2;
            options.ProgressReportInterval = 50;
            options.EnableSnapshots = false;
            options.SnapshotInterval = TimeSpan.FromHours(1);
        });

        _eventStore = new InMemoryStreamingEventStore();
        _projectionStore = new InMemoryProjectionStore();

        services.AddSingleton(_eventStore);
        services.AddSingleton<IStreamingEventStore>(_eventStore);
        services.AddSingleton<IProjectionStore>(_projectionStore);
        services.AddSingleton<OrderSummaryProjection>();

        _provider = services.BuildServiceProvider();

        var logger = _provider.GetRequiredService<ILogger<LiveProjectionProcessor>>();
        var projectionOptions = _provider.GetRequiredService<IOptions<ProjectionOptions>>();
        _liveProcessor = new LiveProjectionProcessor(
            _eventStore,
            _projectionStore,
            _provider,
            logger,
            projectionOptions);

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_liveProcessor != null)
        {
            await _liveProcessor.StopAsync(CancellationToken.None);
        }

        _eventStore?.Dispose();
        _provider?.Dispose();
    }

    [Fact]
    public async Task LiveProcessor_UpdatesProjectionInRealTime_WithinLatencyTarget()
    {
        // Arrange
        var orderId = OrderId.New();
        var customerId = "customer-live-001";

        // Register projection and start processor
        _liveProcessor!.RegisterProjection<OrderSummaryProjection>();
        await _liveProcessor.StartAsync(CancellationToken.None);

        // Wait for processor initialization
        await Task.Delay(200);

        // **Step 1: Create order and verify projection updates**
        var order = OrderAggregate.PlaceOrder(orderId, customerId, DateTimeOffset.UtcNow);
        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        // Wait for live processor to pick up event (polling interval + processing time)
        await Task.Delay(300); // 100ms poll + 200ms buffer
        // Query projection state
        var projection1 = GetProjectionInstance();
        var summary1 = projection1.GetOrderSummary(orderId.ToString());

        summary1.Should().NotBeNull("Projection should have processed OrderPlaced event");
        summary1!.OrderId.Should().Be(orderId.ToString());
        summary1.CustomerId.Should().Be(customerId);
        summary1.Status.Should().Be("Created");
        summary1.LineCount.Should().Be(0);
        // **Step 2: Add order lines and verify real-time updates**
        order.AddOrderLine("line-1", "product-A", 2, 25.00m);
        order.AddOrderLine("line-2", "product-B", 1, 50.00m);
        order.AddOrderLine("line-3", "product-C", 3, 10.00m);

        var lineEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore.AppendEventsAsync(orderId.ToString(), lineEvents, 1);

        // Wait for live processor
        await Task.Delay(300);
        // Query updated projection
        var projection2 = GetProjectionInstance();
        var summary2 = projection2.GetOrderSummary(orderId.ToString());

        summary2.Should().NotBeNull();
        summary2!.LineCount.Should().Be(3, "All 3 order lines should be reflected");
        summary2.TotalAmount.Should().Be((2 * 25.00m) + (1 * 50.00m) + (3 * 10.00m));
        // **Step 3: Complete order and verify final state**
        var completeResult = order.Complete(DateTimeOffset.UtcNow);
        completeResult.IsSuccess.Should().BeTrue();

        var completeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore.AppendEventsAsync(orderId.ToString(), completeEvents, 4);

        // Wait for live processor
        await Task.Delay(300);
        // Query final projection state
        var projection3 = GetProjectionInstance();
        var summary3 = projection3.GetOrderSummary(orderId.ToString());

        summary3.Should().NotBeNull();
        summary3!.Status.Should().Be("Completed");
        summary3.CompletedAt.Should().NotBeNull();
        // **Step 4: Verify processor status**
        var status = _liveProcessor.GetStatus();
        status.IsRunning.Should().BeTrue();
        status.RegisteredProjections.Should().Be(1);
        status.ActiveProjections.Should().Be(1);
        status.TotalEventsProcessed.Should().BeGreaterThanOrEqualTo(5);

        // **Expected Results:**
        // ✅ Projection updates within 500ms of event append
        // ✅ All 5 events processed
        // ✅ Final state matches expectations
    }

    [Fact]
    public async Task LiveProcessor_ProcessesMultipleOrders_Concurrently()
    {
        // Arrange
        _liveProcessor!.RegisterProjection<OrderSummaryProjection>();
        await _liveProcessor.StartAsync(CancellationToken.None);

        await Task.Delay(200);

        // **Step 1: Create 5 orders concurrently**
        var orderIds = new List<OrderId>();
        var appendTasks = new List<Task>();

        for (int i = 0; i < 5; i++)
        {
            var orderId = OrderId.New();
            orderIds.Add(orderId);

            var order = OrderAggregate.PlaceOrder(orderId, $"customer-concurrent-{i:000}", DateTimeOffset.UtcNow);
            order.AddOrderLine($"line-{i}", $"product-{i}", i + 1, 10.00m * (i + 1));

            var events = order.DomainEvents.ToList();
            order.ClearDomainEvents();

            appendTasks.Add(_eventStore!.AppendEventsAsync(orderId.ToString(), events, 0));
        }
        await Task.WhenAll(appendTasks);

        // Wait for live processor to catch up
        await Task.Delay(500);
        // **Step 2: Verify all orders processed**
        var projection = GetProjectionInstance();

        foreach (var orderId in orderIds)
        {
            var summary = projection.GetOrderSummary(orderId.ToString());
            summary.Should().NotBeNull($"Order {orderId} should be processed");
            summary!.LineCount.Should().Be(1);
        }
        // **Step 3: Verify processor statistics**
        var status = _liveProcessor.GetStatus();
        status.TotalEventsProcessed.Should().BeGreaterThanOrEqualTo(10, "At least 10 events (5 orders x 2 events each)");

        // **Expected Results:**
        // ✅ All 5 orders processed
        // ✅ Concurrent processing succeeds
        // ✅ No events missed
    }

    [Fact]
    public async Task LiveProcessor_GracefulShutdown_SavesCheckpoint()
    {
        // Arrange
        var orderId = OrderId.New();
        var customerId = "customer-shutdown-001";

        _liveProcessor!.RegisterProjection<OrderSummaryProjection>();
        await _liveProcessor.StartAsync(CancellationToken.None);

        await Task.Delay(200);

        // **Step 1: Append events**
        var order = OrderAggregate.PlaceOrder(orderId, customerId, DateTimeOffset.UtcNow);
        order.AddOrderLine("line-1", "product-A", 1, 10.00m);

        var events = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        await _eventStore!.AppendEventsAsync(orderId.ToString(), events, 0);

        // Wait for processing
        await Task.Delay(300);

        // **Step 2: Get checkpoint before shutdown**
        var checkpointBefore = await _projectionStore!.GetCheckpointAsync("E2E_OrderSummary");
        checkpointBefore.Should().BeGreaterThan(0, "Checkpoint should be saved during processing");

        // **Step 3: Graceful shutdown**
        await _liveProcessor.StopAsync(CancellationToken.None);

        // **Step 4: Verify final checkpoint saved**
        var checkpointAfter = await _projectionStore.GetCheckpointAsync("E2E_OrderSummary");
        checkpointAfter.Should().NotBeNull();
        checkpointAfter!.Value.Should().BeGreaterThanOrEqualTo(checkpointBefore!.Value, "Final checkpoint should be saved on shutdown");

        // **Step 5: Verify processor stopped**
        var status = _liveProcessor.GetStatus();
        status.IsRunning.Should().BeFalse();

        // **Expected Results:**
        // ✅ Checkpoint saved on shutdown
        // ✅ Processor stopped gracefully
    }

    [Fact]
    public async Task LiveProcessor_StartStop_CanRestart()
    {
        // Arrange
        _liveProcessor!.RegisterProjection<OrderSummaryProjection>();

        // **Step 1: Start processor**
        await _liveProcessor.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        var status1 = _liveProcessor.GetStatus();
        status1.IsRunning.Should().BeTrue();

        // **Step 2: Stop processor**
        await _liveProcessor.StopAsync(CancellationToken.None);
        await Task.Delay(100);

        var status2 = _liveProcessor.GetStatus();
        status2.IsRunning.Should().BeFalse();

        // **Step 3: Restart processor**
        await _liveProcessor.StartAsync(CancellationToken.None);
        await Task.Delay(200);

        var status3 = _liveProcessor.GetStatus();
        status3.IsRunning.Should().BeTrue();

        // **Step 4: Append event and verify processing resumes**
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-restart-001", DateTimeOffset.UtcNow);
        var events = order.DomainEvents.ToList();

        await _eventStore!.AppendEventsAsync(orderId.ToString(), events, 0);

        // Wait for processing
        await Task.Delay(300);

        // Verify event processed
        var projection = GetProjectionInstance();
        var summary = projection.GetOrderSummary(orderId.ToString());
        summary.Should().NotBeNull("Event should be processed after restart");

        // **Expected Results:**
        // ✅ Processor can stop and restart
        // ✅ Processing resumes after restart
    }

    /// <summary>
    /// Helper method to get the projection instance from the live processor.
    /// Uses reflection since projections are stored internally.
    /// </summary>
    private OrderSummaryProjection GetProjectionInstance()
    {
        var liveProjectionsField = typeof(LiveProjectionProcessor)
            .GetField("_liveProjections", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var liveProjections = liveProjectionsField!.GetValue(_liveProcessor)
            as System.Collections.Concurrent.ConcurrentDictionary<string, Compendium.Infrastructure.Projections.IProjection>;

        if (liveProjections!.TryGetValue("E2E_OrderSummary", out var projection))
        {
            return (OrderSummaryProjection)projection;
        }

        throw new InvalidOperationException("OrderSummaryProjection not found in live processor");
    }
}
