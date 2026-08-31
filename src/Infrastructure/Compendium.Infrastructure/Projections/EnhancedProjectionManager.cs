// -----------------------------------------------------------------------
// <copyright file="EnhancedProjectionManager.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Infrastructure.Projections;

/// <summary>
/// Enhanced projection manager with advanced rebuild capabilities, progress tracking, and performance optimization.
/// </summary>
public class EnhancedProjectionManager : IProjectionManager, IDisposable
{
    private readonly IStreamingEventStore _eventStore;
    private readonly IProjectionStore _projectionStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EnhancedProjectionManager> _logger;
    private readonly ProjectionOptions _options;
    private readonly ConcurrentDictionary<string, ProjectionState> _projectionStates;
    private readonly SemaphoreSlim _rebuildSemaphore;
    private readonly ConcurrentDictionary<string, Type> _registeredProjections;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _projectionCancellations;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnhancedProjectionManager"/> class.
    /// </summary>
    /// <param name="eventStore">The streaming event store.</param>
    /// <param name="projectionStore">The projection store for checkpoints and snapshots.</param>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The projection configuration options.</param>
    public EnhancedProjectionManager(
        IStreamingEventStore eventStore,
        IProjectionStore projectionStore,
        IServiceProvider serviceProvider,
        ILogger<EnhancedProjectionManager> logger,
        IOptions<ProjectionOptions> options)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _projectionStore = projectionStore ?? throw new ArgumentNullException(nameof(projectionStore));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new ProjectionOptions();
        _projectionStates = new ConcurrentDictionary<string, ProjectionState>();
        _rebuildSemaphore = new SemaphoreSlim(_options.MaxConcurrentRebuilds);
        _registeredProjections = new ConcurrentDictionary<string, Type>();
        _projectionCancellations = new ConcurrentDictionary<string, CancellationTokenSource>();
    }

    /// <inheritdoc />
    public async Task RebuildProjectionAsync<TProjection>(
        string? streamId = null,
        DateTime? fromTimestamp = null,
        IProgress<RebuildProgress>? progress = null,
        CancellationToken cancellationToken = default) where TProjection : IProjection
    {
        var projection = _serviceProvider.GetRequiredService<TProjection>();
        var projectionName = projection.ProjectionName;

        // Take this projection — and only this one — out of the live fan-out for the
        // duration. Otherwise the live loop keeps applying new events to a projection
        // whose read model is being cleared underneath it, and writes its in-memory
        // position back over the rewound checkpoint.
        var liveProcessor = _serviceProvider.GetService<ILiveProjectionProcessor>();
        liveProcessor?.SuspendProjection(projectionName);
        var rebuilt = false;

        await _rebuildSemaphore.WaitAsync(cancellationToken);
        try
        {
            _logger.LogInformation("Starting rebuild of projection {ProjectionName}", projectionName);

            // Update state to rebuilding
            await UpdateProjectionStateAsync(projectionName, ProjectionStatus.Rebuilding, position: 0);

            // Rewind the cursor BEFORE clearing the read model, and replay from the
            // beginning of the stream. Reading the checkpoint and replaying from it
            // after ResetAsync made rebuild a delete: in steady state the checkpoint
            // sits at the head, `global_position > checkpoint` selects nothing, and
            // the projection was left with an emptied table. The rewind comes first so
            // a process death mid-rebuild leaves a cursor that replays too much rather
            // than one that claims events already applied to a table that was cleared.
            await _projectionStore.SaveCheckpointAsync(projectionName, 0, cancellationToken);

            const long fromPosition = 0;

            // Reset projection state
            await projection.ResetAsync(cancellationToken);

            // Get total event count for progress tracking
            var totalEvents = await _eventStore.GetEventCountAsync(streamId, cancellationToken);

            // Process events in batches
            var batchSize = _options.RebuildBatchSize;
            var processedCount = 0L;
            var stopwatch = Stopwatch.StartNew();
            var lastSnapshotTime = DateTime.UtcNow;

            await foreach (var eventBatch in GetEventBatchesAsync(streamId, fromPosition, batchSize, cancellationToken))
            {
                var batchStopwatch = Stopwatch.StartNew();

                // Process batch
                foreach (var eventData in eventBatch)
                {
                    var metadata = new EventMetadata(
                        eventData.StreamId,
                        eventData.StreamPosition,
                        eventData.GlobalPosition,
                        eventData.Timestamp,
                        eventData.UserId,
                        eventData.TenantId,
                        eventData.Headers
                    );

                    // Apply event to projection using reflection for type safety
                    await ApplyEventToProjectionAsync(projection, eventData.Event, metadata, cancellationToken);
                    processedCount++;
                }

                // Save checkpoint after each batch
                if (eventBatch.Any())
                {
                    var lastPosition = eventBatch.Last().GlobalPosition;
                    await _projectionStore.SaveCheckpointAsync(projectionName, lastPosition, cancellationToken);
                }

                // Create snapshot if enabled and interval has passed
                if (_options.EnableSnapshots && DateTime.UtcNow - lastSnapshotTime >= _options.SnapshotInterval)
                {
                    await _projectionStore.SaveSnapshotAsync(projection, cancellationToken);
                    lastSnapshotTime = DateTime.UtcNow;
                    _logger.LogDebug("Saved snapshot for projection {ProjectionName}", projectionName);
                }

                // Report progress
                if (progress != null && processedCount % _options.ProgressReportInterval == 0)
                {
                    var elapsed = stopwatch.Elapsed;
                    var eventsPerSecond = processedCount / elapsed.TotalSeconds;
                    var remaining = totalEvents - processedCount;
                    var estimatedTimeRemaining = remaining > 0 && eventsPerSecond > 0
                        ? TimeSpan.FromSeconds(remaining / eventsPerSecond)
                        : TimeSpan.Zero;

                    progress.Report(new RebuildProgress
                    {
                        ProjectionName = projectionName,
                        ProcessedEvents = processedCount,
                        TotalEvents = totalEvents,
                        ElapsedTime = elapsed,
                        EventsPerSecond = eventsPerSecond,
                        EstimatedTimeRemaining = estimatedTimeRemaining,
                        CurrentBatch = (int)(processedCount / batchSize) + 1,
                        BatchSize = batchSize
                    });
                }

                // Check for cancellation
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogDebug("Processed batch of {BatchSize} events in {ElapsedMs}ms",
                    eventBatch.Count, batchStopwatch.ElapsedMilliseconds);
            }

            // Save final snapshot
            if (_options.EnableSnapshots)
            {
                await _projectionStore.SaveSnapshotAsync(projection, cancellationToken);
            }

            // Report final progress (ensure we always report 100%)
            if (progress != null && processedCount > 0)
            {
                var elapsed = stopwatch.Elapsed;
                var eventsPerSecond = processedCount / elapsed.TotalSeconds;

                progress.Report(new RebuildProgress
                {
                    ProjectionName = projectionName,
                    ProcessedEvents = processedCount,
                    TotalEvents = totalEvents,
                    ElapsedTime = elapsed,
                    EventsPerSecond = eventsPerSecond,
                    EstimatedTimeRemaining = TimeSpan.Zero,
                    CurrentBatch = (int)(processedCount / batchSize) + 1,
                    BatchSize = batchSize
                });
            }

            // Update state to completed
            await UpdateProjectionStateAsync(projectionName, ProjectionStatus.Completed);
            rebuilt = true;

            _logger.LogInformation("Completed rebuild of projection {ProjectionName}. Processed {EventCount} events in {ElapsedSeconds}s",
                projectionName, processedCount, stopwatch.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Rebuild of projection {ProjectionName} was cancelled", projectionName);
            await UpdateProjectionStateAsync(projectionName, ProjectionStatus.Paused);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rebuild projection {ProjectionName}", projectionName);
            await UpdateProjectionStateAsync(projectionName, ProjectionStatus.Failed, ex.Message);
            throw;
        }
        finally
        {
            _rebuildSemaphore.Release();

            if (rebuilt)
            {
                // Hand the projection back to the live loop, which re-reads the
                // checkpoint the rebuild just wrote and carries on from there.
                liveProcessor?.ResumeProjection(projectionName);
            }
            else
            {
                // Interrupted or failed: the read model is half-rebuilt and the state
                // row says so. Resuming here would quietly serve a partial read model
                // as if it were whole — that call belongs to whoever reads the state.
                _logger.LogWarning(
                    "Projection {ProjectionName} stays suspended after an unfinished rebuild; " +
                    "resume it explicitly once its state has been examined",
                    projectionName);
            }
        }
    }

    /// <inheritdoc />
    public async Task<ProjectionState> GetProjectionStateAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        if (_projectionStates.TryGetValue(projectionName, out var state))
        {
            return state;
        }

        // Try to load from store
        var storedState = await _projectionStore.GetProjectionStateAsync(projectionName, cancellationToken);
        if (storedState != null)
        {
            _projectionStates.TryAdd(projectionName, storedState);
            return storedState;
        }

        // Return default state
        return new ProjectionState
        {
            ProjectionName = projectionName,
            Status = ProjectionStatus.Idle,
            LastProcessedAt = DateTime.UtcNow
        };
    }

    /// <inheritdoc />
    public async Task PauseProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        if (_projectionCancellations.TryGetValue(projectionName, out var cts))
        {
            cts.Cancel();
        }

        // Writing a Paused row is the report, not the act: the live processor is what
        // holds the cursor still. Without this call the projection kept applying and
        // "paused" was a lie told by the admin surface.
        _serviceProvider.GetService<ILiveProjectionProcessor>()?.SuspendProjection(projectionName);

        await UpdateProjectionStateAsync(projectionName, ProjectionStatus.Paused);
        _logger.LogInformation("Paused projection {ProjectionName}", projectionName);
    }

    /// <inheritdoc />
    public async Task ResumeProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        // The processor re-reads the persisted checkpoint before taking events again,
        // so the projection restarts exactly where the pause stopped it.
        _serviceProvider.GetService<ILiveProjectionProcessor>()?.ResumeProjection(projectionName);

        await UpdateProjectionStateAsync(projectionName, ProjectionStatus.Building);
        _logger.LogInformation("Resumed projection {ProjectionName}", projectionName);
    }

    /// <inheritdoc />
    public async Task DeleteProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        // Cancel any running operations
        if (_projectionCancellations.TryRemove(projectionName, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
        }

        // Remove from memory
        _projectionStates.TryRemove(projectionName, out _);
        _registeredProjections.TryRemove(projectionName, out _);

        // Delete from store
        await _projectionStore.DeleteProjectionDataAsync(projectionName, cancellationToken);

        _logger.LogWarning("Deleted projection {ProjectionName} and all its data", projectionName);
    }

    /// <inheritdoc />
    public void RegisterProjection<TProjection>() where TProjection : IProjection
    {
        var projection = _serviceProvider.GetRequiredService<TProjection>();
        _registeredProjections.TryAdd(projection.ProjectionName, typeof(TProjection));
        _logger.LogInformation("Registered projection {ProjectionName}", projection.ProjectionName);
    }

    /// <inheritdoc />
    public async Task<ProjectionManagerStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var projectionDetails = new Dictionary<string, ProjectionState>();

        foreach (var projectionName in _registeredProjections.Keys)
        {
            var state = await GetProjectionStateAsync(projectionName, cancellationToken);
            projectionDetails[projectionName] = state;
        }

        return new ProjectionManagerStatistics
        {
            TotalProjections = _registeredProjections.Count,
            ActiveProjections = projectionDetails.Values.Count(s => s.Status == ProjectionStatus.Building),
            RebuildingProjections = projectionDetails.Values.Count(s => s.Status == ProjectionStatus.Rebuilding),
            PausedProjections = projectionDetails.Values.Count(s => s.Status == ProjectionStatus.Paused),
            FailedProjections = projectionDetails.Values.Count(s => s.Status == ProjectionStatus.Failed),
            ProjectionDetails = projectionDetails
        };
    }

    /// <summary>
    /// Gets event batches for efficient processing.
    /// </summary>
    private async IAsyncEnumerable<List<EventData>> GetEventBatchesAsync(
        string? streamId,
        long fromPosition,
        int batchSize,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var batch = new List<EventData>();

        await foreach (var eventData in _eventStore.StreamEventsAsync(streamId, fromPosition, cancellationToken))
        {
            batch.Add(eventData);

            if (batch.Count >= batchSize)
            {
                yield return batch;
                batch = new List<EventData>();
            }
        }

        if (batch.Any())
        {
            yield return batch;
        }
    }

    /// <summary>
    /// Applies an event to a projection using reflection for type safety.
    /// </summary>
    private async Task ApplyEventToProjectionAsync(
        IProjection projection,
        IDomainEvent domainEvent,
        EventMetadata metadata,
        CancellationToken cancellationToken)
    {
        var projectionType = projection.GetType();
        var eventType = domainEvent.GetType();

        // Find the generic IProjection<TEvent> interface
        var genericInterface = projectionType.GetInterfaces()
            .FirstOrDefault(i => i.IsGenericType &&
                                i.GetGenericTypeDefinition() == typeof(IProjection<>) &&
                                i.GetGenericArguments()[0].IsAssignableFrom(eventType));

        if (genericInterface != null)
        {
            var applyMethod = genericInterface.GetMethod(nameof(IProjection<IDomainEvent>.ApplyAsync));
            if (applyMethod != null)
            {
                try
                {
                    var task = (Task)applyMethod.Invoke(projection, new object[] { domainEvent, metadata, cancellationToken })!;
                    await task;
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    // Unwrap the inner exception to preserve the original exception type
                    throw ex.InnerException;
                }
            }
        }
    }

    /// <summary>
    /// Updates the projection state both in memory and storage.
    /// </summary>
    private async Task UpdateProjectionStateAsync(
        string projectionName,
        ProjectionStatus status,
        string? errorMessage = null,
        long? position = null)
    {
        // The state row used to be written without a position, so every write put 0 in
        // last_processed_position — a column that reads as "this projection has applied
        // nothing". Carry the checkpoint unless the caller knows better (a rebuild
        // announcing that it has just rewound to 0).
        var lastProcessedPosition = position;
        if (lastProcessedPosition is null)
        {
            try
            {
                lastProcessedPosition = await _projectionStore.GetCheckpointAsync(projectionName) ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not read the checkpoint of projection {ProjectionName} while writing its state",
                    projectionName);
                lastProcessedPosition = 0;
            }
        }

        var state = new ProjectionState
        {
            ProjectionName = projectionName,
            Status = status,
            ErrorMessage = errorMessage,
            LastProcessedPosition = lastProcessedPosition.Value,
            LastProcessedAt = DateTime.UtcNow
        };

        _projectionStates.AddOrUpdate(projectionName, state, (_, _) => state);
        await _projectionStore.SaveProjectionStateAsync(state);
    }

    /// <summary>
    /// Disposes the projection manager and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Cancel all running operations
        foreach (var cts in _projectionCancellations.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _rebuildSemaphore.Dispose();
        _disposed = true;
    }
}
