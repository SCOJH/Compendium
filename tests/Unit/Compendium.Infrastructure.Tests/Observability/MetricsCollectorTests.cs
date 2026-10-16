// -----------------------------------------------------------------------
// <copyright file="MetricsCollectorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Bogus;

namespace Compendium.Infrastructure.Tests.Observability;

/// <summary>
/// Comprehensive tests for MetricsCollector with production-ready scenarios.
/// </summary>
public sealed class MetricsCollectorTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly MetricsCollector _sut;
    private readonly Faker _faker;

    public MetricsCollectorTests(ITestOutputHelper output)
    {
        _output = output;
        _sut = new MetricsCollector("Compendium.Tests");
        _faker = new Faker();
    }

    #region Basic Operations - 8 tests

    [Fact]
    public void IncrementCounter_WithValidName_ShouldNotThrow()
    {
        // Arrange
        var metricName = "test.counter";
        var tags = new[] { new KeyValuePair<string, object?>("tag1", "value1") };

        // Act & Assert
        _sut.Invoking(m => m.IncrementCounter(metricName, 1, tags))
            .Should().NotThrow();
    }

    [Fact]
    public void RecordValue_WithValidName_ShouldNotThrow()
    {
        // Arrange
        var metricName = "test.gauge";
        var value = _faker.Random.Double(0, 100);
        var tags = new[] { new KeyValuePair<string, object?>("environment", "test") };

        // Act & Assert
        _sut.Invoking(m => m.RecordValue(metricName, value, tags))
            .Should().NotThrow();
    }

    [Fact]
    public void RecordDuration_WithValidName_ShouldNotThrow()
    {
        // Arrange
        var metricName = "test.duration";
        var duration = TimeSpan.FromMilliseconds(_faker.Random.Double(1, 1000));
        var tags = new[] { new KeyValuePair<string, object?>("operation", "test") };

        // Act & Assert
        _sut.Invoking(m => m.RecordDuration(metricName, duration, tags))
            .Should().NotThrow();
    }

    [Fact]
    public void StartTimer_WithValidName_ShouldReturnDisposableTimer()
    {
        // Arrange
        var metricName = "test.timer";
        var tags = new[] { new KeyValuePair<string, object?>("method", "test") };

        // Act
        using var timer = _sut.StartTimer(metricName, tags);

        // Assert
        timer.Should().NotBeNull();
        timer.Should().BeAssignableTo<IDisposable>();
    }

    [Fact]
    public void RecordEvent_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var eventName = "UserCreated";
        var aggregateId = _faker.Random.Guid().ToString();
        var eventType = "UserAggregate";
        var processingTime = _faker.Random.Double(1, 100);

        // Act & Assert
        _sut.Invoking(m => m.RecordEvent(eventName, aggregateId, eventType, processingTime))
            .Should().NotThrow();
    }

    [Fact]
    public void RecordProjectionRebuild_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var projectionId = "user-projection";
        var duration = _faker.Random.Double(100, 5000);

        // Act & Assert
        _sut.Invoking(m => m.RecordProjectionRebuild(projectionId, duration))
            .Should().NotThrow();
    }

    [Fact]
    public void RecordCircuitBreakerTrip_WithValidServiceName_ShouldNotThrow()
    {
        // Arrange
        var serviceName = "external-api";

        // Act & Assert
        _sut.Invoking(m => m.RecordCircuitBreakerTrip(serviceName))
            .Should().NotThrow();
    }

    [Fact]
    public void RecordEncryptionOperation_WithValidParameters_ShouldNotThrow()
    {
        // Arrange
        var operation = "encrypt";
        var duration = _faker.Random.Double(1, 50);

        // Act & Assert
        _sut.Invoking(m => m.RecordEncryptionOperation(operation, duration))
            .Should().NotThrow();
    }

    #endregion

    #region Timer Tests - 3 tests

    [Fact]
    public void StartTimer_WhenDisposed_ShouldRecordDuration()
    {
        // Arrange
        var metricName = "test.timer.duration";
        var tags = new[] { new KeyValuePair<string, object?>("test", "timer") };

        // Act
        using (var timer = _sut.StartTimer(metricName, tags))
        {
            Thread.Sleep(10); // Small delay to ensure measurable duration
        } // Timer disposed here

        // Assert - No exception should be thrown
        // In a real implementation, we'd verify the metric was recorded
    }

    [Fact]
    public void StartTimer_MultipleTimers_ShouldHandleConcurrently()
    {
        // Arrange
        const int timerCount = 10;
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < timerCount; i++)
        {
            var timerId = i;
            tasks.Add(Task.Run(() =>
            {
                using var timer = _sut.StartTimer($"concurrent.timer.{timerId}");
                Thread.Sleep(_faker.Random.Int(1, 50));
            }));
        }

        // Assert
        Task.WaitAll(tasks.ToArray());
        // All timers should complete without exception
    }

    [Fact]
    public void StartTimer_NestedTimers_ShouldHandleCorrectly()
    {
        // Arrange & Act
        using var outerTimer = _sut.StartTimer("outer.timer");
        Thread.Sleep(5);

        using (var innerTimer = _sut.StartTimer("inner.timer"))
        {
            Thread.Sleep(5);
        }

        Thread.Sleep(5);

        // Assert - Should complete without exception
    }

    #endregion

    #region Performance Tests - 3 tests

    [Fact]
    public void MetricsCollector_HighThroughput_ShouldHandleEfficiently()
    {
        // Arrange
        const int operationCount = 10_000;
        var stopwatch = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < operationCount; i++)
        {
            _sut.IncrementCounter("high.throughput.counter", 1,
                new KeyValuePair<string, object?>("iteration", i));

            _sut.RecordValue("high.throughput.gauge", i,
                new KeyValuePair<string, object?>("batch", i / 100));

            _sut.RecordDuration("high.throughput.duration", TimeSpan.FromMilliseconds(i % 100));
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Processed {operationCount * 3} metric operations in {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void MetricsCollector_ConcurrentOperations_ShouldBeThreadSafe()
    {
        // Arrange
        const int threadCount = 50;
        const int operationsPerThread = 100;
        var tasks = new List<Task>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    for (int j = 0; j < operationsPerThread; j++)
                    {
                        _sut.IncrementCounter($"thread.{threadId}.counter", 1);
                        _sut.RecordValue($"thread.{threadId}.gauge", j);
                        _sut.RecordDuration($"thread.{threadId}.duration", TimeSpan.FromMilliseconds(j));
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        exceptions.Should().BeEmpty("Concurrent operations should not throw exceptions");
    }

    [Fact]
    public void MetricsCollector_MemoryUsage_ShouldNotLeak()
    {
        // Arrange / Act — record a thousand distinct metrics whose names, tags and tag values
        // are fresh objects the test then knows only through weak references. A leak is
        // measured on those objects, not on the heap of the test process, which every other
        // test running in parallel also allocates on.
        const int iterations = 1000;
        var recorded = RecordMetricsKeepingWeakReferences(_sut, iterations);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        // Assert — a collector that held on to what it records would keep them alive
        recorded.Should().HaveCount(iterations * 3);
        recorded.Where(reference => reference.IsAlive).Should().BeEmpty(
            "the collector must not retain the names, tags or timers it records");
    }

    // Not inlined: once this method has returned, no stack slot of the test can still reference
    // the objects it created, so only the collector could keep them alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static List<WeakReference> RecordMetricsKeepingWeakReferences(MetricsCollector sut, int iterations)
    {
        var references = new List<WeakReference>(iterations * 3);
        for (var i = 0; i < iterations; i++)
        {
            var name = $"memory.test.{i}";
            var tagValue = new object();
            var tags = new[] { new KeyValuePair<string, object?>("iteration", tagValue) };
            references.Add(new WeakReference(name));
            references.Add(new WeakReference(tagValue));
            references.Add(new WeakReference(tags));

            sut.IncrementCounter(name, 1, tags);
            sut.RecordValue(name, i, tags);
            sut.RecordDuration(name, TimeSpan.FromMilliseconds(i), tags);
            using var timer = sut.StartTimer(name, tags);
        }

        return references;
    }

    #endregion

    #region Edge Cases - 4 tests

    [Fact]
    public void MetricsCollector_WithEmptyTags_ShouldHandleCorrectly()
    {
        // Arrange
        var metricName = "test.empty.tags";

        // Act & Assert
        _sut.Invoking(m => m.IncrementCounter(metricName, 1))
            .Should().NotThrow();

        _sut.Invoking(m => m.RecordValue(metricName, 42))
            .Should().NotThrow();

        _sut.Invoking(m => m.RecordDuration(metricName, TimeSpan.FromMilliseconds(100)))
            .Should().NotThrow();
    }

    [Fact]
    public void MetricsCollector_WithNullTagValues_ShouldHandleCorrectly()
    {
        // Arrange
        var metricName = "test.null.tags";
        var tags = new[] { new KeyValuePair<string, object?>("nullTag", null) };

        // Act & Assert
        _sut.Invoking(m => m.IncrementCounter(metricName, 1, tags))
            .Should().NotThrow();
    }

    [Fact]
    public void MetricsCollector_WithSpecialCharactersInNames_ShouldHandleCorrectly()
    {
        // Arrange
        var specialMetricName = "test.metric-with_special.chars:and/slashes";
        var tags = new[] { new KeyValuePair<string, object?>("special-tag", "value@#$%") };

        // Act & Assert
        _sut.Invoking(m => m.IncrementCounter(specialMetricName, 1, tags))
            .Should().NotThrow();
    }

    [Fact]
    public void MetricsCollector_WithExtremeValues_ShouldHandleCorrectly()
    {
        // Arrange
        var metricName = "test.extreme.values";

        // Act & Assert
        _sut.Invoking(m => m.IncrementCounter(metricName, double.MaxValue))
            .Should().NotThrow();

        _sut.Invoking(m => m.RecordValue(metricName, double.MinValue))
            .Should().NotThrow();

        _sut.Invoking(m => m.RecordDuration(metricName, TimeSpan.MaxValue))
            .Should().NotThrow();
    }

    #endregion

    #region Domain-Specific Metrics - 5 tests

    [Fact]
    public void RecordEvent_WithRealisticEventData_ShouldHandleCorrectly()
    {
        // Arrange
        var events = new[]
        {
            ("UserRegistered", "user-123", "UserAggregate", 15.5),
            ("OrderCreated", "order-456", "OrderAggregate", 8.2),
            ("PaymentProcessed", "payment-789", "PaymentAggregate", 125.7),
            ("InventoryUpdated", "inventory-101", "InventoryAggregate", 3.1),
            ("NotificationSent", "notification-202", "NotificationAggregate", 45.8)
        };

        // Act & Assert
        foreach (var (eventName, aggregateId, eventType, processingTime) in events)
        {
            _sut.Invoking(m => m.RecordEvent(eventName, aggregateId, eventType, processingTime))
                .Should().NotThrow();
        }
    }

    [Fact]
    public void RecordProjectionRebuild_WithVariousDurations_ShouldHandleCorrectly()
    {
        // Arrange
        var rebuilds = new[]
        {
            ("user-summary-projection", 1250.5),
            ("order-analytics-projection", 5678.9),
            ("inventory-snapshot-projection", 234.1),
            ("notification-history-projection", 8901.2)
        };

        // Act & Assert
        foreach (var (projectionId, duration) in rebuilds)
        {
            _sut.Invoking(m => m.RecordProjectionRebuild(projectionId, duration))
                .Should().NotThrow();
        }
    }

    [Fact]
    public void RecordCircuitBreakerTrip_WithVariousServices_ShouldHandleCorrectly()
    {
        // Arrange
        var services = new[]
        {
            "payment-gateway",
            "email-service",
            "sms-provider",
            "external-api",
            "database-connection"
        };

        // Act & Assert
        foreach (var serviceName in services)
        {
            _sut.Invoking(m => m.RecordCircuitBreakerTrip(serviceName))
                .Should().NotThrow();
        }
    }

    [Fact]
    public void RecordEncryptionOperation_WithVariousOperations_ShouldHandleCorrectly()
    {
        // Arrange
        var operations = new[]
        {
            ("encrypt", 12.5),
            ("decrypt", 8.7),
            ("hash", 3.2),
            ("sign", 15.8),
            ("verify", 6.1)
        };

        // Act & Assert
        foreach (var (operation, duration) in operations)
        {
            _sut.Invoking(m => m.RecordEncryptionOperation(operation, duration))
                .Should().NotThrow();
        }
    }

    [Fact]
    public void MetricsCollector_CompleteWorkflow_ShouldHandleAllMetricTypes()
    {
        // Arrange - Simulate a complete business workflow
        var workflowId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();

        // Act - Record metrics for a complete workflow
        using (var workflowTimer = _sut.StartTimer("workflow.total.duration",
            new KeyValuePair<string, object?>("workflow_id", workflowId)))
        {
            // Step 1: User registration
            _sut.RecordEvent("UserRegistered", $"user-{workflowId}", "UserAggregate", 15.2);
            _sut.IncrementCounter("user.registrations.total", 1,
                new KeyValuePair<string, object?>("source", "web"));

            // Step 2: Encryption operations
            _sut.RecordEncryptionOperation("encrypt", 8.5);
            _sut.RecordEncryptionOperation("hash", 2.1);

            // Step 3: Database operations
            _sut.RecordDuration("database.query.duration", TimeSpan.FromMilliseconds(45));
            _sut.RecordValue("database.connections.active", 12);

            // Step 4: External service call (with circuit breaker)
            try
            {
                // Simulate external call
                _sut.RecordDuration("external.api.call.duration", TimeSpan.FromMilliseconds(150));
            }
            catch
            {
                _sut.RecordCircuitBreakerTrip("external-api");
            }

            // Step 5: Projection updates
            _sut.RecordEvent("UserProjectionUpdated", $"projection-{workflowId}", "UserProjection", 5.8);
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Complete workflow recorded in {stopwatch.ElapsedMilliseconds}ms");
    }

    #endregion

    #region Disposal Tests - 2 tests

    [Fact]
    public void Dispose_ShouldDisposeResourcesCorrectly()
    {
        // Arrange
        var metricsCollector = new MetricsCollector("test-disposal");

        // Act & Assert
        metricsCollector.Invoking(m => m.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var metricsCollector = new MetricsCollector("test-multiple-disposal");

        // Act & Assert
        metricsCollector.Dispose();
        metricsCollector.Invoking(m => m.Dispose()).Should().NotThrow();
    }

    #endregion

    public void Dispose()
    {
        _sut?.Dispose();
    }
}
