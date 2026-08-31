# Compendium Projection Manager

A high-performance, enterprise-grade projection management system for Event Sourcing with CQRS pipeline support. Designed to process 10,000+ events per minute with checkpoint-based resume capability and snapshot support for faster rebuilds.

## Features

### ✨ Core Capabilities
- **High Performance**: Processes 10,000+ events per minute during rebuilds
- **Checkpoint Resume**: Automatic checkpoint saving with failure recovery
- **Snapshot Support**: Accelerated rebuilds with configurable snapshot intervals
- **Live Processing**: Real-time projection updates as events arrive
- **Progress Tracking**: Detailed rebuild progress with time estimates
- **Concurrent Rebuilds**: Configurable parallel processing limits
- **Multi-tenancy**: Full tenant isolation support
- **PostgreSQL Optimized**: Efficient storage with optimized indexes

### 🏗️ Architecture
- **Event-driven projections** with eventual consistency
- **Multiple projection types** per event stream
- **Projection versioning** for schema evolution
- **Thread-safe** concurrent operation
- **Memory-efficient** batch processing
- **Resilient** error handling with retry logic

## Quick Start

### 1. Setup Dependencies

```csharp
// Program.cs or Startup.cs
services.AddProjections(options =>
{
    options.RebuildBatchSize = 1000;
    options.MaxConcurrentRebuilds = 3;
    options.EnableSnapshots = true;
    options.SnapshotInterval = TimeSpan.FromMinutes(5);
});

// Add PostgreSQL projection store
services.AddPostgreSqlProjections();

// Register your projections in DI. The runner resolves projections via IServiceProvider,
// so they MUST be registered as services. AddProjection<T>() does TryAddSingleton<T>()
// for projections without ctor args; for projections that need DI, register a factory
// first (see below) and then call AddProjection<T>() to wire it into the runner.
services.AddSingleton<OrderSummaryProjection>(sp =>
    new OrderSummaryProjection(
        connectionString,
        sp.GetRequiredService<ILogger<OrderSummaryProjection>>()));
services.AddProjection<OrderSummaryProjection>();
services.AddProjection<CustomerStatsProjection>();
```

### 2. Create a Projection

Projections can use any DI dependency (logger, connection string, cache, metrics).

```csharp
public class OrderSummaryProjection : IProjection<OrderPlacedEvent>, IProjection<OrderShippedEvent>
{
    private readonly string _connectionString;
    private readonly ILogger<OrderSummaryProjection> _logger;
    private readonly Dictionary<Guid, OrderSummary> _summaries = new();

    public OrderSummaryProjection(string connectionString, ILogger<OrderSummaryProjection> logger)
    {
        _connectionString = connectionString;
        _logger = logger;
    }

    public string ProjectionName => "OrderSummary";
    public int Version => 1;
    public IReadOnlyDictionary<Guid, OrderSummary> Summaries => _summaries;

    public Task ApplyAsync(OrderPlacedEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
    {
        _summaries[@event.OrderId] = new OrderSummary
        {
            OrderId = @event.OrderId,
            CustomerId = @event.CustomerId,
            Total = @event.Total,
            Status = OrderStatus.Placed,
            PlacedAt = @event.OccurredOn.DateTime,
            TenantId = metadata.TenantId
        };

        _logger.LogDebug("Projected OrderPlaced for {OrderId}", @event.OrderId);
        return Task.CompletedTask;
    }
    
    public Task ApplyAsync(OrderShippedEvent @event, EventMetadata metadata, CancellationToken cancellationToken = default)
    {
        if (_summaries.TryGetValue(@event.OrderId, out var summary))
        {
            summary.Status = OrderStatus.Shipped;
            summary.ShippedAt = @event.OccurredOn.DateTime;
        }
        
        return Task.CompletedTask;
    }
    
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        _summaries.Clear();
        return Task.CompletedTask;
    }
}
```

### 3. Rebuild Projections

```csharp
public class RebuildService
{
    private readonly IProjectionManager _projectionManager;
    
    public async Task RebuildAllProjectionsAsync()
    {
        var progress = new Progress<RebuildProgress>(report =>
        {
            Console.WriteLine($"Rebuilding {report.ProjectionName}: {report.PercentComplete:F1}% " +
                            $"({report.EventsPerSecond:F0} events/sec)");
        });
        
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
            progress: progress);
    }
    
    public async Task RebuildFromTimestampAsync(DateTime fromDate)
    {
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
            fromTimestamp: fromDate);
    }
    
    public async Task RebuildSpecificStreamAsync(string streamId)
    {
        await _projectionManager.RebuildProjectionAsync<OrderSummaryProjection>(
            streamId: streamId);
    }
}
```

### 4. Monitor Projections

```csharp
public class ProjectionMonitoringService
{
    private readonly IProjectionManager _projectionManager;
    private readonly ILiveProjectionProcessor _liveProcessor;
    
    public async Task<ProjectionManagerStatistics> GetOverallStatsAsync()
    {
        return await _projectionManager.GetStatisticsAsync();
    }
    
    public async Task<ProjectionState> GetProjectionStatusAsync(string projectionName)
    {
        return await _projectionManager.GetProjectionStateAsync(projectionName);
    }
    
    public LiveProcessingStatus GetLiveProcessingStatus()
    {
        return _liveProcessor.GetStatus();
    }
}
```

## Advanced Usage

### Performance Optimization

```csharp
// High-performance configuration
services.Configure<ProjectionOptions>(options =>
{
    options.RebuildBatchSize = 2000;              // Larger batches for bulk processing
    options.MaxConcurrentRebuilds = 5;            // More parallel rebuilds
    options.ProgressReportInterval = 1000;        // Less frequent progress updates
    options.CheckpointInterval = TimeSpan.FromSeconds(5);  // Frequent checkpoints
    options.EnableSnapshots = true;
    options.SnapshotInterval = TimeSpan.FromMinutes(2);    // Frequent snapshots
});
```

### Error Handling

```csharp
public class ResilientProjectionService
{
    private readonly IProjectionManager _projectionManager;
    private readonly ILogger<ResilientProjectionService> _logger;
    
    public async Task SafeRebuildAsync<TProjection>() where TProjection : IProjection
    {
        var retryCount = 0;
        const int maxRetries = 3;
        
        while (retryCount < maxRetries)
        {
            try
            {
                await _projectionManager.RebuildProjectionAsync<TProjection>();
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogWarning(ex, "Rebuild attempt {RetryCount} failed for {ProjectionType}", 
                    retryCount, typeof(TProjection).Name);
                
                if (retryCount >= maxRetries)
                {
                    _logger.LogError("Max retries exceeded for {ProjectionType}", typeof(TProjection).Name);
                    throw;
                }
                
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount))); // Exponential backoff
            }
        }
    }
}
```

### Projection Lifecycle Management

```csharp
public class ProjectionLifecycleService
{
    private readonly IProjectionManager _projectionManager;
    
    public async Task PauseProjectionAsync(string projectionName)
    {
        await _projectionManager.PauseProjectionAsync(projectionName);
    }
    
    public async Task ResumeProjectionAsync(string projectionName)
    {
        await _projectionManager.ResumeProjectionAsync(projectionName);
    }
    
    public async Task DeleteAndRebuildAsync(string projectionName)
    {
        await _projectionManager.DeleteProjectionAsync(projectionName);
        // Projection will be automatically rebuilt by live processor
    }
}
```

## Guarantees, and what a multi-replica host must supply

These are the properties the engine holds itself to. They matter because each one
was, at some point, quietly untrue.

**One cursor per projection.** Every projection carries its own position, persisted
under its own name in `projection_checkpoints`. The stream is read from the MIN over
active projections, and each projection skips what it has already applied. A
projection registered today has no checkpoint, therefore starts at 0 and replays the
whole history — whatever position its siblings have reached.

**A failure stops one projection, not the batch and not the others.** The cursor of a
projection that throws is held at the position *before* the event it failed on, so a
restart re-attempts it instead of skipping it forever. `RetryCount` / `RetryDelay`
govern immediate re-attempts; after `MaxProjectionApplyFailures` consecutive failed
passes the projection is dead-lettered so its siblings can advance past a poison
event. Either way the projection's row in `projection_states` carries `Failed` and
the exception message, and goes back to `Building` when it recovers.

**A pause stops the cursor.** `PauseProjectionAsync` suspends the projection in the
processor: no events, no checkpoint writes. The state is persisted, so the pause
survives the pod that received the request. `ResumeProjectionAsync` re-reads the
persisted checkpoint before the projection takes events again, so it restarts exactly
where it stopped.

**A rebuild rebuilds.** `RebuildProjectionAsync` rewinds the cursor to 0, calls
`ResetAsync()`, replays from 0 and writes the cursor batch by batch — in that order.
Reading the checkpoint and replaying from it *after* clearing the read model made
rebuild a delete, because a healthy checkpoint sits at the head of the stream and
`global_position > checkpoint` then selects nothing. The projection is suspended for
the duration and handed back only if the rebuild completes; an interrupted or failed
rebuild leaves it suspended, with its state saying so.

**One process applies.** `LiveProjectionProcessor` is a `BackgroundService`: every
replica of a host that registers it runs one. It enters its loop only while it holds
`IProjectionConsumerLease`, and re-reads every position at the start of each term, so
no replica can apply an event twice or write back a position it remembers from an
earlier term.

The default implementation always grants the lease. That is correct for a
single-process host and for tests, and it is the behaviour every existing caller
already had — but in a multi-replica deployment it grants the lease to everyone at
once, which is the situation the port exists to end. **Register your own before
calling `AddProjections`:**

```csharp
// TryAdd inside AddProjections leaves your registration in place.
services.AddSingleton<IProjectionConsumerLease, PostgresProjectionConsumerLease>();
services.AddProjections(options => { ... });
```

Make release a property of the mechanism rather than of your error handling: a lock
bound to a database connection is released by the server when the pod dies, a row
holding a "leader" flag is not unless it also carries an expiry. It is a port and not
a lock in this assembly on purpose — `Compendium.Infrastructure` has no database
dependency and must not acquire one.

## Database Schema

The PostgreSQL projection store creates the following tables:

```sql
-- Projection checkpoints for resume capability
CREATE TABLE projection_checkpoints (
    projection_name VARCHAR(255) PRIMARY KEY,
    position BIGINT NOT NULL,
    updated_at TIMESTAMP WITH TIME ZONE DEFAULT NOW()
);

-- Projection snapshots for fast rebuilds
CREATE TABLE projection_snapshots (
    id SERIAL PRIMARY KEY,
    projection_name VARCHAR(255) NOT NULL,
    version INT NOT NULL,
    snapshot_data JSONB NOT NULL,
    created_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    UNIQUE(projection_name, version)
);

-- Projection states for monitoring
CREATE TABLE projection_states (
    projection_name VARCHAR(255) PRIMARY KEY,
    version INT NOT NULL DEFAULT 1,
    last_processed_position BIGINT DEFAULT 0,
    last_processed_at TIMESTAMP WITH TIME ZONE DEFAULT NOW(),
    status VARCHAR(50) NOT NULL DEFAULT 'Idle',
    error_message TEXT
);
```

## Performance Benchmarks

### Target Performance
- **Rebuild Speed**: 10,000+ events per minute
- **Memory Usage**: < 100MB for 100k events
- **Checkpoint Latency**: < 10ms
- **Snapshot Operations**: < 100ms for typical projections

### Benchmark Results

```
| EventCount | BatchSize | Mean Time | Events/Min | Memory (MB) |
|------------|-----------|-----------|------------|-------------|
| 10,000     | 1,000     | 45.2s     | 13,274     | 45.2        |
| 25,000     | 2,000     | 98.7s     | 15,213     | 67.8        |
| 50,000     | 2,000     | 189.4s    | 15,834     | 89.3        |
```

## Best Practices

### 1. Projection Design
- Keep projections focused on specific query patterns
- Use immutable data structures where possible
- Implement proper error handling for invalid events
- Consider memory usage for large datasets

### 2. Performance Optimization
- Tune batch sizes based on your event volume
- Use snapshots for projections with expensive rebuilds
- Monitor checkpoint intervals to balance performance and safety
- Consider projection versioning for schema changes

### 3. Monitoring and Observability
- Track rebuild times and progress
- Monitor checkpoint lag for live processing
- Set up alerts for failed projections
- Use structured logging for debugging

### 4. Deployment
- Initialize database schema before first run
- Configure proper connection pooling
- Set up health checks for live processing
- Plan for projection versioning and migrations

## Integration Testing

The system includes comprehensive integration tests with real PostgreSQL:

```csharp
[Fact]
public async Task RebuildProjection_WithLargeEventStream_MeetsPerformanceTarget()
{
    // Arrange
    var events = GenerateTestEvents(10000);
    await SeedEventsAsync("perf-stream", events);
    
    // Act
    var stopwatch = Stopwatch.StartNew();
    await _projectionManager.RebuildProjectionAsync<TestCounterProjection>(streamId: "perf-stream");
    stopwatch.Stop();
    
    // Assert
    var eventsPerMinute = 10000 * 60.0 / stopwatch.Elapsed.TotalSeconds;
    eventsPerMinute.Should().BeGreaterThan(10000, "Should meet 10k events/minute target");
}
```

Run integration tests with:
```bash
dotnet test Compendium.IntegrationTests --filter "ProjectionManagerIntegrationTests"
```

## Troubleshooting

### Common Issues

**Slow Rebuild Performance**
- Increase batch size: `RebuildBatchSize = 2000`
- Check database connection pooling
- Verify indexes on event store tables
- Monitor memory usage and GC pressure

**Checkpoint Issues**
- Verify PostgreSQL connection string
- Check database permissions
- Ensure tables are created with `InitializeAsync()`
- Monitor checkpoint save frequency

**Live Processing Lag**
- Check event store streaming performance
- Verify projection logic complexity
- Monitor live processor status
- Consider horizontal scaling

### Debugging

Enable detailed logging:
```csharp
services.AddLogging(builder => 
    builder.AddConsole()
           .SetMinimumLevel(LogLevel.Debug)
           .AddFilter("Compendium.Infrastructure.Projections", LogLevel.Trace));
```

## Contributing

See the main Compendium contributing guidelines. For projection-specific contributions:

1. Add comprehensive tests for new projection types
2. Include performance benchmarks for significant changes
3. Update documentation with usage examples
4. Ensure thread-safety for concurrent operations

## License

MIT License - see LICENSE file for details.
