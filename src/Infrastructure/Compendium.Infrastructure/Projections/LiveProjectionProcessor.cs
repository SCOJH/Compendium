// -----------------------------------------------------------------------
// <copyright file="LiveProjectionProcessor.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Compendium.Infrastructure.Projections;

/// <summary>
/// Interface for live projection processing service.
/// </summary>
public interface ILiveProjectionProcessor
{
    /// <summary>
    /// Starts the live projection processor.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the live projection processor.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a projection for live processing. The projection instance is resolved
    /// from the DI container, so <typeparamref name="TProjection"/> must be registered as
    /// a service (typically a singleton) before this call.
    /// </summary>
    /// <typeparam name="TProjection">The type of projection to register.</typeparam>
    void RegisterProjection<TProjection>() where TProjection : IProjection;

    /// <summary>
    /// Unregisters a projection from live processing.
    /// </summary>
    /// <param name="projectionName">The name of the projection to unregister.</param>
    void UnregisterProjection(string projectionName);

    /// <summary>
    /// Gets the status of live projection processing.
    /// </summary>
    /// <returns>Processing status information.</returns>
    LiveProcessingStatus GetStatus();
}

/// <summary>
/// Background service for processing projections as events arrive in real-time.
/// Maintains projection state and ensures eventual consistency.
/// </summary>
public class LiveProjectionProcessor : BackgroundService, ILiveProjectionProcessor
{
    private readonly IStreamingEventStore _eventStore;
    private readonly IProjectionStore _projectionStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<LiveProjectionProcessor> _logger;
    private readonly ProjectionOptions _options;
    private readonly ConcurrentDictionary<string, Type> _registeredProjections;
    private readonly ConcurrentDictionary<string, IProjection> _liveProjections;
    private readonly ConcurrentDictionary<string, DateTime> _lastSnapshotTimes;

    // Per-projection last-successfully-applied global position. Checkpoints are
    // physically per-projection (IProjectionStore.SaveCheckpointAsync keys on name),
    // so a projection that fails on an event holds its own position while its
    // siblings advance — no single global cursor can drag a failed projection past
    // the event it never applied.
    private readonly ConcurrentDictionary<string, long> _projectionPositions;

    // Consecutive apply-failure counts, keyed by projection name. Reset to absent
    // on the next successful apply. Crossing MaxProjectionApplyFailures dead-letters.
    private readonly ConcurrentDictionary<string, int> _projectionFailureCounts;

    // Dead-lettered (halted) projections: name -> reason. A halted projection stops
    // receiving events (its read model is knowingly stale) so the others can advance
    // past a poison event; a restart clears this and re-attempts from the checkpoint.
    private readonly ConcurrentDictionary<string, string> _haltedProjections;

    private readonly SemaphoreSlim _processingLock;

    // The stream read cursor: the MIN over all live (non-halted) projection
    // positions, so an event held back by one projection is re-delivered until it
    // catches up (already-applied events are skipped per-projection by position).
    private long _lastProcessedPosition;
    private readonly Stopwatch _processingStopwatch;
    private long _totalEventsProcessed;
    private DateTime _lastStatsUpdate;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiveProjectionProcessor"/> class.
    /// </summary>
    /// <param name="eventStore">The streaming event store.</param>
    /// <param name="projectionStore">The projection store for checkpoints and snapshots.</param>
    /// <param name="serviceProvider">The service provider for dependency injection.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The projection configuration options.</param>
    public LiveProjectionProcessor(
        IStreamingEventStore eventStore,
        IProjectionStore projectionStore,
        IServiceProvider serviceProvider,
        ILogger<LiveProjectionProcessor> logger,
        IOptions<ProjectionOptions> options)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _projectionStore = projectionStore ?? throw new ArgumentNullException(nameof(projectionStore));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new ProjectionOptions();
        _registeredProjections = new ConcurrentDictionary<string, Type>();
        _liveProjections = new ConcurrentDictionary<string, IProjection>();
        _lastSnapshotTimes = new ConcurrentDictionary<string, DateTime>();
        _projectionPositions = new ConcurrentDictionary<string, long>();
        _projectionFailureCounts = new ConcurrentDictionary<string, int>();
        _haltedProjections = new ConcurrentDictionary<string, string>();
        _processingLock = new SemaphoreSlim(1, 1);
        _processingStopwatch = new Stopwatch();
        _lastStatsUpdate = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public void RegisterProjection<TProjection>() where TProjection : IProjection
    {
        var projection = _serviceProvider.GetRequiredService<TProjection>();
        _registeredProjections.TryAdd(projection.ProjectionName, typeof(TProjection));
        _logger.LogInformation("Registered projection {ProjectionName} for live processing", projection.ProjectionName);
    }

    /// <inheritdoc />
    public void UnregisterProjection(string projectionName)
    {
        _registeredProjections.TryRemove(projectionName, out _);
        _liveProjections.TryRemove(projectionName, out _);
        _lastSnapshotTimes.TryRemove(projectionName, out _);
        _projectionPositions.TryRemove(projectionName, out _);
        _projectionFailureCounts.TryRemove(projectionName, out _);
        _haltedProjections.TryRemove(projectionName, out _);
        _logger.LogInformation("Unregistered projection {ProjectionName} from live processing", projectionName);
    }

    /// <inheritdoc />
    Task ILiveProjectionProcessor.StartAsync(CancellationToken cancellationToken)
    {
        return StartAsync(cancellationToken);
    }

    /// <inheritdoc />
    Task ILiveProjectionProcessor.StopAsync(CancellationToken cancellationToken)
    {
        return StopAsync(cancellationToken);
    }

    /// <inheritdoc />
    public LiveProcessingStatus GetStatus()
    {
        var elapsed = _processingStopwatch.Elapsed;
        var eventsPerSecond = elapsed.TotalSeconds > 0 ? _totalEventsProcessed / elapsed.TotalSeconds : 0;

        return new LiveProcessingStatus
        {
            IsRunning = _processingStopwatch.IsRunning,
            RegisteredProjections = _registeredProjections.Count,
            ActiveProjections = _liveProjections.Count,
            HaltedProjections = _haltedProjections.Count,
            LastProcessedPosition = _lastProcessedPosition,
            TotalEventsProcessed = _totalEventsProcessed,
            EventsPerSecond = eventsPerSecond,
            UpTime = elapsed,
            LastUpdateTime = _lastStatsUpdate
        };
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting live projection processor with {ProjectionCount} registered projections",
            _registeredProjections.Count);

        _processingStopwatch.Start();

        try
        {
            // Initialize projections
            await InitializeProjectionsAsync(stoppingToken);

            // Start processing loop
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessNewEventsAsync(stoppingToken);

                    // Update statistics periodically
                    if (DateTime.UtcNow - _lastStatsUpdate > TimeSpan.FromMinutes(1))
                    {
                        LogProcessingStatistics();
                        _lastStatsUpdate = DateTime.UtcNow;
                    }

                    // Small delay to prevent excessive polling
                    await Task.Delay(TimeSpan.FromMilliseconds(100), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in live projection processing loop");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Live projection processor was cancelled");
        }
        finally
        {
            _processingStopwatch.Stop();
            await SaveFinalSnapshotsAsync();
            _logger.LogInformation("Live projection processor stopped");
        }
    }

    /// <summary>
    /// Initializes all registered projections by loading snapshots and determining starting positions.
    /// </summary>
    internal async Task InitializeProjectionsAsync(CancellationToken cancellationToken)
    {
        // Track whether ANY projection has a persisted checkpoint, distinct from
        // _lastProcessedPosition staying at 0 (which would also be the case if a
        // projection persisted checkpoint=0 — e.g. just-started, no events yet).
        // Conflating "no checkpoint" with "checkpoint at 0" would cause the cold-start
        // policy to fire every time a projection sits at 0, which is not what we want.
        var anyCheckpointFound = false;

        foreach (var (projectionName, projectionType) in _registeredProjections)
        {
            try
            {
                var projection = (IProjection)_serviceProvider.GetRequiredService(projectionType);

                // Load snapshot if available and enabled
                if (_options.EnableSnapshots)
                {
                    var snapshotMethod = typeof(IProjectionStore)
                        .GetMethod(nameof(IProjectionStore.LoadSnapshotAsync))!
                        .MakeGenericMethod(projectionType);

                    var snapshotTask = (Task)snapshotMethod.Invoke(
                        _projectionStore,
                        new object?[] { projectionName, cancellationToken })!;

                    await snapshotTask;

                    var snapshotResult = snapshotTask.GetType().GetProperty("Result")?.GetValue(snapshotTask);
                    if (snapshotResult is IProjection snapshotProjection)
                    {
                        projection = snapshotProjection;
                        _logger.LogInformation("Loaded snapshot for projection {ProjectionName}", projectionName);
                    }
                }

                _liveProjections[projectionName] = projection;
                _lastSnapshotTimes[projectionName] = DateTime.UtcNow;

                // Get checkpoint to determine this projection's OWN starting position.
                // Absent checkpoint => 0 (a brand-new projection backfills from the
                // start; already-applied events are skipped per-projection by position,
                // and a fresh projection's read model is empty so this is correct).
                var checkpoint = await _projectionStore.GetCheckpointAsync(projectionName, cancellationToken);
                _projectionPositions[projectionName] = checkpoint ?? 0L;
                if (checkpoint.HasValue)
                {
                    anyCheckpointFound = true;
                }

                _logger.LogDebug("Initialized projection {ProjectionName} with checkpoint at position {Position}",
                    projectionName, checkpoint);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize projection {ProjectionName}", projectionName);
            }
        }

        // If no checkpoints exist for any projection, decide where to start. Two policies:
        //   - jump to the current head (default — avoids replaying weeks of events on
        //     every cold restart)
        //   - backfill from position 0 (opt-in via BackfillFromBeginningOnEmptyCheckpoint
        //     — required when projections are the *only* writers to the read model)
        // When at least one projection persisted a checkpoint (even if all were at 0),
        // we trust the persisted state and don't apply the cold-start policy.
        if (!anyCheckpointFound)
        {
            if (_options.BackfillFromBeginningOnEmptyCheckpoint)
            {
                _logger.LogInformation(
                    "No projection checkpoints found; backfilling from position 0 (BackfillFromBeginningOnEmptyCheckpoint=true)");
                // Leave all positions at 0 — the polling loop reads from position > 0
                // and applies every event in the store to every projection.
                _lastProcessedPosition = 0;
            }
            else
            {
                var head = await _eventStore.GetMaxGlobalPositionAsync(cancellationToken);
                foreach (var name in _liveProjections.Keys)
                {
                    _projectionPositions[name] = head;
                }

                _lastProcessedPosition = head;
                _logger.LogInformation("Starting live processing from current position: {Position}", head);
            }
        }
        else
        {
            // Resume from the MIN over per-projection checkpoints, NOT the max: a
            // projection that legitimately lagged (held at a failed event while its
            // siblings advanced, then the process restarted) must re-receive the
            // events it missed. Siblings already past that position skip them by the
            // per-projection position guard, so re-delivery is a no-op for them.
            _lastProcessedPosition = _projectionPositions.Values.Min();
            _logger.LogInformation(
                "Resuming live processing from min projection checkpoint: {Position}", _lastProcessedPosition);
        }
    }

    /// <summary>
    /// Processes new events that have arrived since the last check.
    /// </summary>
    private async Task ProcessNewEventsAsync(CancellationToken cancellationToken)
    {
        if (!_liveProjections.Any())
        {
            return;
        }

        await _processingLock.WaitAsync(cancellationToken);
        try
        {
            var newEvents = new List<EventData>();
            var batchCount = 0;
            const int maxBatchSize = 100; // Smaller batches for live processing

            await foreach (var eventData in _eventStore.StreamEventsAsync(null, _lastProcessedPosition, cancellationToken))
            {
                newEvents.Add(eventData);
                batchCount++;

                // Process in smaller batches for better responsiveness
                if (batchCount >= maxBatchSize)
                {
                    await ProcessEventBatchAsync(newEvents, cancellationToken);
                    newEvents.Clear();
                    batchCount = 0;
                }
            }

            // Process remaining events
            if (newEvents.Any())
            {
                await ProcessEventBatchAsync(newEvents, cancellationToken);
            }
        }
        finally
        {
            _processingLock.Release();
        }
    }

    /// <summary>
    /// Processes a batch of events through all live projections.
    /// </summary>
    /// <summary>
    /// Applies a batch of events to the live projections. Exposed as
    /// <see langword="internal"/> so tests can drive the checkpoint/halt behaviour
    /// deterministically without racing the polling loop.
    /// </summary>
    internal async Task ProcessEventBatchAsync(List<EventData> events, CancellationToken cancellationToken)
    {
        if (events.Count == 0)
        {
            return;
        }

        // Precompute metadata once per event; events arrive in ascending global order.
        var prepared = new (EventData Data, EventMetadata Metadata)[events.Count];
        for (var i = 0; i < events.Count; i++)
        {
            var e = events[i];
            prepared[i] = (e, new EventMetadata(
                e.StreamId, e.StreamPosition, e.GlobalPosition,
                e.Timestamp, e.UserId, e.TenantId, e.Headers));
        }

        var maxPosition = events[^1].GlobalPosition;
        var appliedCount = 0L;

        // Projection-outer / events-inner so each projection advances independently
        // and STOPS at its own first failure — events must apply in order, so we can
        // never skip past an event a projection failed on. A failing projection holds
        // its checkpoint; its siblings are unaffected.
        foreach (var (projectionName, projection) in _liveProjections)
        {
            if (_haltedProjections.ContainsKey(projectionName))
            {
                // Dead-lettered — stays frozen at its last-good checkpoint.
                continue;
            }

            foreach (var (data, metadata) in prepared)
            {
                var current = _projectionPositions.GetValueOrDefault(projectionName, 0L);

                // Idempotent skip: this projection already applied this position
                // (re-delivered because the MIN cursor rewound for a lagging sibling).
                if (data.GlobalPosition <= current)
                {
                    continue;
                }

                try
                {
                    await ApplyEventToProjectionAsync(projection, data.Event, metadata, cancellationToken);
                    _projectionPositions[projectionName] = data.GlobalPosition;
                    _projectionFailureCounts.TryRemove(projectionName, out _);
                    appliedCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // CRITICAL: do NOT advance this projection's checkpoint past the
                    // failed event. Hold at `current` and retry on the next pass.
                    var failures = _projectionFailureCounts.AddOrUpdate(projectionName, 1, (_, c) => c + 1);
                    _logger.LogError(ex,
                        "Projection {ProjectionName} failed to apply event {EventId} at position {Position} " +
                        "(attempt {Attempt}/{Max}); checkpoint held at {Held}",
                        projectionName, data.EventId, data.GlobalPosition,
                        failures, _options.MaxProjectionApplyFailures, current);

                    if (failures >= _options.MaxProjectionApplyFailures)
                    {
                        var reason =
                            $"halted at position {data.GlobalPosition} after {failures} consecutive failed attempts: {ex.Message}";
                        _haltedProjections[projectionName] = reason;
                        _logger.LogCritical(ex,
                            "Projection {ProjectionName} DEAD-LETTERED: {Reason}. Its read model is now STALE " +
                            "until the process is restarted/redeployed; other projections continue past this event.",
                            projectionName, reason);
                    }

                    // Stop applying further (later) events to THIS projection this pass.
                    break;
                }
            }
        }

        _totalEventsProcessed += appliedCount;

        // Advance the shared read cursor to the MIN over live (non-halted) projections
        // so an event held back by one projection is re-streamed until it catches up.
        // If every projection is halted, jump to the batch head to avoid re-reading a
        // tail nobody will consume.
        var livePositions = _liveProjections.Keys
            .Where(name => !_haltedProjections.ContainsKey(name))
            .Select(name => _projectionPositions.GetValueOrDefault(name, 0L))
            .ToList();
        _lastProcessedPosition = livePositions.Count > 0 ? livePositions.Min() : maxPosition;

        // Persist each projection's OWN position (held-back projections included, so a
        // restart resumes from where each actually is — never past a failed event).
        await SaveCheckpointsAsync(cancellationToken);

        // Create snapshots if needed
        await CreateSnapshotsIfNeededAsync(cancellationToken);

        _logger.LogDebug("Processed batch of {EventCount} events ({Applied} applies), cursor: {Position}",
            events.Count, appliedCount, _lastProcessedPosition);
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
                var task = (Task)applyMethod.Invoke(projection, new object[] { domainEvent, metadata, cancellationToken })!;
                await task;
            }
        }
    }

    /// <summary>
    /// Saves each live projection's OWN last-applied position as its checkpoint.
    /// Never writes the shared read cursor here — a projection that failed on an
    /// event must persist the position BEFORE that event, so a restart re-attempts
    /// it instead of silently skipping it (the checkpoint-advance-on-error bug).
    /// </summary>
    private async Task SaveCheckpointsAsync(CancellationToken cancellationToken)
    {
        var tasks = _liveProjections.Keys.Select(projectionName =>
            _projectionStore.SaveCheckpointAsync(
                projectionName,
                _projectionPositions.GetValueOrDefault(projectionName, 0L),
                cancellationToken));

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Creates snapshots if the configured interval has passed.
    /// </summary>
    private async Task CreateSnapshotsIfNeededAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnableSnapshots)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var tasks = new List<Task>();

        foreach (var (projectionName, projection) in _liveProjections)
        {
            var lastSnapshotTime = _lastSnapshotTimes.GetValueOrDefault(projectionName, DateTime.MinValue);

            if (now - lastSnapshotTime >= _options.SnapshotInterval)
            {
                tasks.Add(CreateSnapshotAsync(projectionName, projection, cancellationToken));
                _lastSnapshotTimes[projectionName] = now;
            }
        }

        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Creates a snapshot for a specific projection.
    /// </summary>
    private async Task CreateSnapshotAsync(string projectionName, IProjection projection, CancellationToken cancellationToken)
    {
        try
        {
            var snapshotMethod = typeof(IProjectionStore)
                .GetMethod(nameof(IProjectionStore.SaveSnapshotAsync))!
                .MakeGenericMethod(projection.GetType());

            var task = (Task)snapshotMethod.Invoke(
                _projectionStore,
                new object[] { projection, cancellationToken })!;

            await task;

            _logger.LogDebug("Created snapshot for projection {ProjectionName}", projectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot for projection {ProjectionName}", projectionName);
        }
    }

    /// <summary>
    /// Saves final snapshots when the processor is stopping.
    /// </summary>
    private async Task SaveFinalSnapshotsAsync()
    {
        if (!_options.EnableSnapshots || !_liveProjections.Any())
        {
            return;
        }

        _logger.LogInformation("Saving final snapshots for {ProjectionCount} projections", _liveProjections.Count);

        var tasks = _liveProjections.Select(kvp =>
            CreateSnapshotAsync(kvp.Key, kvp.Value, CancellationToken.None));

        try
        {
            await Task.WhenAll(tasks);
            _logger.LogInformation("Final snapshots saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save some final snapshots");
        }
    }

    /// <summary>
    /// Logs processing statistics.
    /// </summary>
    private void LogProcessingStatistics()
    {
        var status = GetStatus();
        _logger.LogInformation(
            "Live projection processing stats: {ActiveProjections} active, {TotalEvents} events processed, {EventsPerSecond:F1} events/sec, position: {Position}",
            status.ActiveProjections, status.TotalEventsProcessed, status.EventsPerSecond, status.LastProcessedPosition);
    }
}

/// <summary>
/// Status information for live projection processing.
/// </summary>
public class LiveProcessingStatus
{
    /// <summary>
    /// Gets or sets whether the processor is currently running.
    /// </summary>
    public bool IsRunning { get; init; }

    /// <summary>
    /// Gets or sets the number of registered projections.
    /// </summary>
    public int RegisteredProjections { get; init; }

    /// <summary>
    /// Gets or sets the number of active projections.
    /// </summary>
    public int ActiveProjections { get; init; }

    /// <summary>
    /// Gets the number of projections that have been dead-lettered (halted) after
    /// repeatedly failing to apply an event. A non-zero value means one or more read
    /// models are knowingly stale and the process needs attention/redeploy.
    /// </summary>
    public int HaltedProjections { get; init; }

    /// <summary>
    /// Gets or sets the last processed global position.
    /// </summary>
    public long LastProcessedPosition { get; init; }

    /// <summary>
    /// Gets or sets the total number of events processed.
    /// </summary>
    public long TotalEventsProcessed { get; init; }

    /// <summary>
    /// Gets or sets the current processing rate in events per second.
    /// </summary>
    public double EventsPerSecond { get; init; }

    /// <summary>
    /// Gets or sets the total uptime of the processor.
    /// </summary>
    public TimeSpan UpTime { get; init; }

    /// <summary>
    /// Gets or sets the last time statistics were updated.
    /// </summary>
    public DateTime LastUpdateTime { get; init; }
}
