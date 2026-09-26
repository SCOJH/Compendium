// -----------------------------------------------------------------------
// <copyright file="ConcurrencyE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Results;
using Compendium.Infrastructure.EventSourcing;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using AwesomeAssertions;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario 5: Concurrent Commands with Optimistic Concurrency.
/// Tests optimistic concurrency control prevents lost updates under concurrent load.
/// </summary>
/// <remarks>
/// Per ADR-0007, this framework-behaviour test runs against
/// <see cref="InMemoryStreamingEventStore"/>. Postgres-specific concurrency
/// tests live in compendium-adapter-postgresql.
/// </remarks>
[Trait("Category", "E2E")]
[Trait("Category", "Concurrency")]
public sealed class ConcurrencyE2ETests : IAsyncLifetime
{
    private InMemoryStreamingEventStore? _eventStore;

    public Task InitializeAsync()
    {
        _eventStore = new InMemoryStreamingEventStore();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _eventStore?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task ConcurrentAppends_WithSameExpectedVersion_OnlyOneSucceeds()
    {
        // Arrange
        var orderId = OrderId.New();

        // **Step 1: Create initial order (version 1)**
        var order = OrderAggregate.PlaceOrder(orderId, "customer-001", DateTimeOffset.UtcNow);
        var initialEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        var createResult = await _eventStore!.AppendEventsAsync(orderId.ToString(), initialEvents, 0);
        createResult.IsSuccess.Should().BeTrue();

        // **Step 2: Simulate 10 concurrent commands all expecting version 1**
        var concurrentTasks = new List<Task<Result>>();
        for (int i = 0; i < 10; i++)
        {
            var lineId = $"line-{i}";
            var task = Task.Run(async () =>
            {
                var concurrentOrder = OrderAggregate.PlaceOrder(orderId, "customer-001", DateTimeOffset.UtcNow);
                concurrentOrder.AddOrderLine(lineId, $"product-{i}", 1, 10.00m);
                var lineEvent = new[] { concurrentOrder.DomainEvents.Last() };

                // All commands expect version 1 (the version after initial creation)
                return await _eventStore!.AppendEventsAsync(orderId.ToString(), lineEvent, 1);
            });

            concurrentTasks.Add(task);
        }

        var results = await Task.WhenAll(concurrentTasks);

        // **Step 3: Verify only ONE succeeded**
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().Be(1, "Only one concurrent append should succeed");
        failureCount.Should().Be(9, "9 concurrent appends should fail due to version conflict");

        // **Step 4: Verify all failures are concurrency conflicts**
        var failures = results.Where(r => !r.IsSuccess).ToList();
        failures.Should().AllSatisfy(r =>
        {
            r.Error.Type.Should().Be(ErrorType.Conflict);
            r.Error.Code.Should().Contain("ConcurrencyConflict");
        });

        // **Step 5: Verify final version is 2 (not 11)**
        var finalVersion = await _eventStore.GetVersionAsync(orderId.ToString());
        finalVersion.IsSuccess.Should().BeTrue();
        finalVersion.Value.Should().Be(2, "Only the single successful append should have incremented version");

        // **Expected Results:**
        // ✅ Only 1 of 10 concurrent commands succeeds
        // ✅ 9 commands fail with ConcurrencyConflict error
        // ✅ Final aggregate version = 2 (no lost updates)
    }

    [Fact]
    public async Task ConcurrentAppends_WithRetry_AllEventuallySucceed()
    {
        // Arrange
        var orderId = OrderId.New();

        // **Step 1: Create initial order**
        var order = OrderAggregate.PlaceOrder(orderId, "customer-002", DateTimeOffset.UtcNow);
        await _eventStore!.AppendEventsAsync(orderId.ToString(), order.DomainEvents.ToList(), 0);

        // **Step 2: Launch 5 concurrent tasks with retry logic**
        var concurrentTasks = new List<Task<int>>();
        for (int i = 0; i < 5; i++)
        {
            var lineId = $"line-{i}";
            var productId = $"product-{i}";

            var task = Task.Run(async () =>
            {
                var retryCount = 0;
                while (retryCount < 20) // Max 20 retries
                {
                    // Get current version
                    var versionResult = await _eventStore!.GetVersionAsync(orderId.ToString());
                    if (!versionResult.IsSuccess)
                    {
                        await Task.Delay(10); // Brief delay before retry
                        retryCount++;
                        continue;
                    }

                    var currentVersion = versionResult.Value;

                    // Create event
                    var tempOrder = OrderAggregate.PlaceOrder(orderId, "customer-002", DateTimeOffset.UtcNow);
                    tempOrder.AddOrderLine(lineId, productId, 1, 10.00m);
                    var lineEvent = new[] { tempOrder.DomainEvents.Last() };

                    // Try to append with current version
                    var appendResult = await _eventStore.AppendEventsAsync(
                        orderId.ToString(),
                        lineEvent,
                        currentVersion);

                    if (appendResult.IsSuccess)
                    {
                        return retryCount; // Success - return number of retries needed
                    }

                    // Conflict - retry with updated version
                    retryCount++;
                    await Task.Delay(10); // Brief delay before retry
                }

                throw new Exception("Failed to append after max retries");
            });

            concurrentTasks.Add(task);
        }

        var retryCounts = await Task.WhenAll(concurrentTasks);

        // **Step 3: Verify all tasks succeeded**
        retryCounts.Should().HaveCount(5);
        retryCounts.Should().AllSatisfy(count => count.Should().BeLessThan(20, "All appends should succeed within retry limit"));

        // **Step 4: Verify final state**
        var finalVersion = await _eventStore!.GetVersionAsync(orderId.ToString());
        finalVersion.Value.Should().Be(6, "Initial order (v1) + 5 lines (v2-v6)");

        var events = await _eventStore.GetEventsAsync(orderId.ToString());
        events.Value.Should().HaveCount(6);

        Console.WriteLine($"Retry statistics: Min={retryCounts.Min()}, Max={retryCounts.Max()}, Avg={retryCounts.Average():F1}");

        // **Expected Results:**
        // ✅ All 5 concurrent tasks eventually succeed
        // ✅ Retry mechanism handles version conflicts
        // ✅ Final version = 6 (all events appended)
        // ✅ No lost updates
    }

    [Fact]
    public async Task HighConcurrency_50ParallelAppends_MaintainsConsistency()
    {
        // Arrange
        var orderId = OrderId.New();

        // **Step 1: Create initial order**
        var order = OrderAggregate.PlaceOrder(orderId, "customer-003", DateTimeOffset.UtcNow);
        await _eventStore!.AppendEventsAsync(orderId.ToString(), order.DomainEvents.ToList(), 0);

        // **Step 2: Launch 50 parallel appends with retry**
        var concurrentCount = 50;
        var successCount = 0;
        var failureCount = 0;
        var totalRetries = 0;

        var tasks = Enumerable.Range(0, concurrentCount).Select(async i =>
        {
            var retries = 0;
            while (retries < 100) // Higher retry limit for high concurrency
            {
                var versionResult = await _eventStore!.GetVersionAsync(orderId.ToString());
                if (!versionResult.IsSuccess)
                {
                    retries++;
                    await Task.Delay(5);
                    continue;
                }

                var tempOrder = OrderAggregate.PlaceOrder(orderId, "customer-003", DateTimeOffset.UtcNow);
                tempOrder.AddOrderLine($"line-{i}", $"product-{i}", 1, 10.00m);
                var lineEvent = new[] { tempOrder.DomainEvents.Last() };

                var result = await _eventStore.AppendEventsAsync(
                    orderId.ToString(),
                    lineEvent,
                    versionResult.Value);

                if (result.IsSuccess)
                {
                    Interlocked.Increment(ref successCount);
                    Interlocked.Add(ref totalRetries, retries);
                    return true;
                }

                retries++;
                await Task.Delay(5);
            }

            Interlocked.Increment(ref failureCount);
            return false;
        }).ToArray();

        var results = await Task.WhenAll(tasks);

        // **Step 3: Verify all succeeded**
        successCount.Should().Be(concurrentCount, "All concurrent appends should eventually succeed");
        failureCount.Should().Be(0, "No permanent failures should occur");

        // **Step 4: Verify final consistency**
        var finalVersion = await _eventStore!.GetVersionAsync(orderId.ToString());
        finalVersion.Value.Should().Be(concurrentCount + 1, "Version should reflect initial order + all appends");

        var events = await _eventStore.GetEventsAsync(orderId.ToString());
        events.Value.Should().HaveCount(concurrentCount + 1);

        var avgRetries = totalRetries / (double)concurrentCount;
        Console.WriteLine($"High concurrency stats: {concurrentCount} parallel appends, Avg retries: {avgRetries:F1}");

        // **Expected Results:**
        // ✅ All 50 parallel appends succeed
        // ✅ No data loss or corruption
        // ✅ Final version = 51 (1 initial + 50 appends)
        // ✅ All events present in correct order
    }

    [Fact]
    public async Task SequentialAppends_NoConflicts_AllSucceed()
    {
        // Arrange
        var orderId = OrderId.New();

        // **Step 1: Create initial order**
        var order = OrderAggregate.PlaceOrder(orderId, "customer-004", DateTimeOffset.UtcNow);
        await _eventStore!.AppendEventsAsync(orderId.ToString(), order.DomainEvents.ToList(), 0);

        // **Step 2: Sequential appends (no concurrency)**
        for (int i = 0; i < 10; i++)
        {
            var currentVersion = await _eventStore.GetVersionAsync(orderId.ToString());
            currentVersion.IsSuccess.Should().BeTrue();

            var tempOrder = OrderAggregate.PlaceOrder(orderId, "customer-004", DateTimeOffset.UtcNow);
            tempOrder.AddOrderLine($"line-{i}", $"product-{i}", 1, 10.00m);
            var lineEvent = new[] { tempOrder.DomainEvents.Last() };

            var result = await _eventStore.AppendEventsAsync(
                orderId.ToString(),
                lineEvent,
                currentVersion.Value);

            result.IsSuccess.Should().BeTrue($"Sequential append {i} should succeed");
        }

        // **Step 3: Verify final state**
        var finalVersion = await _eventStore!.GetVersionAsync(orderId.ToString());
        finalVersion.Value.Should().Be(11, "1 initial + 10 sequential appends");

        // **Expected Results:**
        // ✅ All 10 sequential appends succeed
        // ✅ No version conflicts
        // ✅ Final version = 11
    }

    [Fact]
    public async Task ConcurrentReads_WithConcurrentWrites_ReadsRemainConsistent()
    {
        // Arrange
        var orderId = OrderId.New();

        // **Step 1: Create initial order**
        var order = OrderAggregate.PlaceOrder(orderId, "customer-005", DateTimeOffset.UtcNow);
        await _eventStore!.AppendEventsAsync(orderId.ToString(), order.DomainEvents.ToList(), 0);

        // **Step 2: Launch concurrent reads and writes**
        var writeTask = Task.Run(async () =>
        {
            for (int i = 0; i < 20; i++)
            {
                var versionResult = await _eventStore!.GetVersionAsync(orderId.ToString());
                if (!versionResult.IsSuccess)
                {
                    await Task.Delay(10);
                    continue;
                }

                var tempOrder = OrderAggregate.PlaceOrder(orderId, "customer-005", DateTimeOffset.UtcNow);
                tempOrder.AddOrderLine($"line-{i}", $"product-{i}", 1, 10.00m);
                var lineEvent = new[] { tempOrder.DomainEvents.Last() };

                var result = await _eventStore.AppendEventsAsync(
                    orderId.ToString(),
                    lineEvent,
                    versionResult.Value);

                if (!result.IsSuccess)
                {
                    i--; // Retry on conflict
                }

                await Task.Delay(10);
            }
        });

        var readTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            var eventCounts = new List<int>();
            for (int i = 0; i < 30; i++)
            {
                var events = await _eventStore!.GetEventsAsync(orderId.ToString());
                if (events.IsSuccess)
                {
                    eventCounts.Add(events.Value.Count);
                }
                await Task.Delay(15);
            }
            return eventCounts;
        })).ToArray();

        await Task.WhenAll(writeTask);
        var readResults = await Task.WhenAll(readTasks);

        // **Step 3: Verify read consistency**
        foreach (var eventCounts in readResults)
        {
            eventCounts.Should().NotBeEmpty();
            // Event counts should be monotonically increasing (reads are consistent)
            for (int i = 1; i < eventCounts.Count; i++)
            {
                eventCounts[i].Should().BeGreaterThanOrEqualTo(eventCounts[i - 1],
                    "Event count should never decrease during concurrent reads");
            }
        }

        // **Step 4: Verify final state**
        var finalVersion = await _eventStore!.GetVersionAsync(orderId.ToString());
        finalVersion.Value.Should().Be(21, "1 initial + 20 writes");

        // **Expected Results:**
        // ✅ Concurrent reads remain consistent
        // ✅ Event counts monotonically increase
        // ✅ No stale or corrupt reads
        // ✅ Final version correct
    }
}
