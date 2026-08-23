// -----------------------------------------------------------------------
// <copyright file="InMemoryEventStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Text.Json;
using Compendium.Core.EventSourcing;
using Compendium.Multitenancy;

namespace Compendium.Infrastructure.EventSourcing;

/// <summary>
/// In-memory implementation of IEventStore for testing and development purposes.
/// Provides thread-safe event storage with multi-tenancy support and comprehensive functionality.
/// </summary>
public sealed class InMemoryEventStore : IEventStore, IDisposable
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _events = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly ILogger<InMemoryEventStore>? _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly IEventDeserializer _eventDeserializer;
    private readonly IEventTypeRegistry? _eventTypeRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryEventStore"/> class.
    /// </summary>
    /// <param name="eventDeserializer">The secure event deserializer.</param>
    /// <param name="logger">The logger instance (optional for testing).</param>
    /// <param name="tenantContext">The tenant context for multi-tenancy support (optional for testing).</param>
    /// <param name="eventTypeRegistry">
    /// The event type registry used to name written events. When it is not supplied, events are
    /// stamped with their assembly qualified name — the behaviour that predates logical names.
    /// </param>
    public InMemoryEventStore(
        IEventDeserializer eventDeserializer,
        ILogger<InMemoryEventStore>? logger = null,
        ITenantContext? tenantContext = null,
        IEventTypeRegistry? eventTypeRegistry = null)
    {
        _eventDeserializer = eventDeserializer ?? throw new ArgumentNullException(nameof(eventDeserializer));
        _logger = logger;
        _tenantContext = tenantContext;
        _eventTypeRegistry = eventTypeRegistry;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
    }

    /// <inheritdoc />
    public Task<Result> AppendEventsAsync(
        string aggregateId,
        IEnumerable<IDomainEvent> events,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId, nameof(aggregateId));
        ArgumentNullException.ThrowIfNull(events, nameof(events));

        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            throw new ArgumentException("AggregateId cannot be empty or whitespace", nameof(aggregateId));
        }

        ThrowIfDisposed();

        var eventList = events.ToList();
        if (eventList.Count == 0)
        {
            return Task.FromResult(Result.Success());
        }

        _lock.EnterWriteLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);
            var existingEvents = _events.GetOrAdd(streamKey, _ => new List<StoredEvent>());

            // Concurrency check
            var currentVersion = existingEvents.Count;
            if (expectedVersion != -1 && expectedVersion != currentVersion)
            {
                _logger?.LogWarning(
                    "Concurrency conflict for aggregate {AggregateId}. Expected version: {ExpectedVersion}, Current version: {CurrentVersion}",
                    aggregateId, expectedVersion, currentVersion);

                return Task.FromResult(Result.Failure(
                    Error.Conflict("EventStore.ConcurrencyConflict",
                        $"Expected version {expectedVersion} but current version is {currentVersion}")));
            }

            // Store events with metadata
            var version = currentVersion;
            var now = DateTimeOffset.UtcNow;
            var tenantId = _tenantContext?.TenantId;

            foreach (var domainEvent in eventList)
            {
                version++;
                var storedEvent = new StoredEvent
                {
                    EventId = domainEvent.EventId,
                    AggregateId = aggregateId,
                    AggregateType = domainEvent.AggregateType,
                    EventType = ResolveEventTypeName(domainEvent),
                    EventData = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), _jsonOptions),
                    Version = version,
                    AggregateVersion = domainEvent.AggregateVersion,
                    OccurredOn = domainEvent.OccurredOn,
                    StoredAt = now,
                    TenantId = tenantId,
                    Metadata = new Dictionary<string, object>
                    {
                        ["CorrelationId"] = Guid.NewGuid().ToString(),
                        ["Timestamp"] = now.Ticks,
                        ["EventVersion"] = version
                    }
                };

                existingEvents.Add(storedEvent);
            }

            _logger?.LogDebug(
                "Appended {EventCount} events to aggregate {AggregateId} (tenant: {TenantId})",
                eventList.Count, aggregateId, tenantId);

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to append events to aggregate {AggregateId}", aggregateId);
            return Task.FromResult(Result.Failure(Error.Failure("EventStore.AppendFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<IDomainEvent>>> GetEventsAsync(
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Validation("EventStore.InvalidAggregateId", "AggregateId cannot be null or empty")));
        }

        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);

            if (!_events.TryGetValue(streamKey, out var events))
            {
                _logger?.LogDebug("No events found for aggregate {AggregateId}", aggregateId);
                return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(
                    Array.Empty<IDomainEvent>()));
            }

            var materialization = MaterializeEvents(aggregateId, events.OrderBy(e => e.Version));
            if (materialization.IsFailure)
            {
                return Task.FromResult(materialization);
            }

            _logger?.LogDebug(
                "Retrieved {EventCount} events for aggregate {AggregateId}",
                materialization.Value.Count, aggregateId);

            return Task.FromResult(materialization);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get events for aggregate {AggregateId}", aggregateId);
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Failure("EventStore.GetEventsFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<IDomainEvent>>> GetEventsAsync(
        string aggregateId,
        long fromVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId, nameof(aggregateId));

        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            throw new ArgumentException("AggregateId cannot be empty or whitespace", nameof(aggregateId));
        }

        if (fromVersion < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fromVersion), fromVersion, "FromVersion must be greater than or equal to 0");
        }

        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);

            if (!_events.TryGetValue(streamKey, out var events))
            {
                return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(
                    Array.Empty<IDomainEvent>()));
            }

            var materialization = MaterializeEvents(
                aggregateId,
                events.Where(e => e.Version > fromVersion).OrderBy(e => e.Version));

            return Task.FromResult(materialization);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get events for aggregate {AggregateId} from version {FromVersion}", aggregateId, fromVersion);
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Failure("EventStore.GetEventsFromVersionFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<IDomainEvent>>> GetEventsInRangeAsync(
        string aggregateId,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Validation("EventStore.InvalidAggregateId", "AggregateId cannot be null or empty")));
        }

        if (fromVersion < 0 || toVersion < 0)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Validation("EventStore.InvalidVersionRange", "Versions must be greater than or equal to 0")));
        }

        if (fromVersion > toVersion)
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Validation("EventStore.InvalidVersionRange", "FromVersion cannot be greater than ToVersion")));
        }

        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);

            if (!_events.TryGetValue(streamKey, out var events))
            {
                return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(
                    Array.Empty<IDomainEvent>()));
            }

            var materialization = MaterializeEvents(
                aggregateId,
                events.Where(e => e.Version >= fromVersion && e.Version <= toVersion).OrderBy(e => e.Version));

            return Task.FromResult(materialization);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get events for aggregate {AggregateId} in range {FromVersion}-{ToVersion}",
                aggregateId, fromVersion, toVersion);
            return Task.FromResult(Result.Failure<IReadOnlyList<IDomainEvent>>(
                Error.Failure("EventStore.GetEventsInRangeFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<IDomainEvent>> GetLastEventAsync(
        string aggregateId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            return Task.FromResult(Result.Failure<IDomainEvent>(
                Error.Validation("EventStore.InvalidAggregateId", "AggregateId cannot be null or empty")));
        }

        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);

            if (!_events.TryGetValue(streamKey, out var events) || !events.Any())
            {
                return Task.FromResult(Result.Failure<IDomainEvent>(
                    Error.NotFound("EventStore.NoEvents", $"No events found for aggregate {aggregateId}")));
            }

            var lastStoredEvent = events.OrderByDescending(e => e.Version).First();
            var lastEvent = DeserializeEvent(lastStoredEvent);

            if (lastEvent == null)
            {
                return Task.FromResult(Result.Failure<IDomainEvent>(
                    Error.Failure("EventStore.DeserializationFailed", "Failed to deserialize last event")));
            }

            return Task.FromResult(Result.Success<IDomainEvent>(lastEvent));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get last event for aggregate {AggregateId}", aggregateId);
            return Task.FromResult(Result.Failure<IDomainEvent>(
                Error.Failure("EventStore.GetLastEventFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<long>> GetVersionAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId, nameof(aggregateId));

        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            throw new ArgumentException("AggregateId cannot be empty or whitespace", nameof(aggregateId));
        }

        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);

            if (!_events.TryGetValue(streamKey, out var events))
            {
                return Task.FromResult(Result.Success<long>(0L));
            }

            var version = (long)events.Count;
            _logger?.LogDebug("Aggregate {AggregateId} current version: {Version}", aggregateId, version);

            return Task.FromResult(Result.Success<long>(version));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get version for aggregate {AggregateId}", aggregateId);
            return Task.FromResult(Result.Failure<long>(Error.Failure("EventStore.GetVersionFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<bool>> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(aggregateId, nameof(aggregateId));

        if (string.IsNullOrWhiteSpace(aggregateId))
        {
            throw new ArgumentException("AggregateId cannot be empty or whitespace", nameof(aggregateId));
        }

        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);
            var exists = _events.ContainsKey(streamKey) && _events[streamKey].Any();

            return Task.FromResult(Result.Success<bool>(exists));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to check existence for aggregate {AggregateId}", aggregateId);
            return Task.FromResult(Result.Failure<bool>(Error.Failure("EventStore.ExistsFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<EventStoreStatistics>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var stats = new EventStoreStatistics
            {
                TotalAggregates = _events.Count,
                TotalEvents = _events.Values.Sum(e => e.Count),
                AggregateStatistics = _events.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new AggregateStatistics
                    {
                        EventCount = kvp.Value.Count,
                        FirstEventDate = kvp.Value.FirstOrDefault()?.StoredAt,
                        LastEventDate = kvp.Value.LastOrDefault()?.StoredAt,
                        CurrentVersion = kvp.Value.Count
                    })
            };

            return Task.FromResult(Result.Success<EventStoreStatistics>(stats));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to get event store statistics");
            return Task.FromResult(Result.Failure<EventStoreStatistics>(
                Error.Failure("EventStore.GetStatisticsFailed", ex.Message)));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// Gets the stream key for multi-tenant isolation.
    /// </summary>
    /// <param name="aggregateId">The aggregate identifier.</param>
    /// <returns>The tenant-isolated stream key.</returns>
    private string GetStreamKey(string aggregateId)
    {
        var tenantId = _tenantContext?.TenantId;
        return string.IsNullOrEmpty(tenantId) ? aggregateId : $"{tenantId}:{aggregateId}";
    }

    /// <summary>
    /// Gets the name an event is written under: its logical name when a registry is available,
    /// its assembly qualified name otherwise.
    /// </summary>
    /// <param name="domainEvent">The event being stored.</param>
    /// <returns>The name to stamp on the stored event.</returns>
    private string ResolveEventTypeName(IDomainEvent domainEvent)
    {
        var eventType = domainEvent.GetType();

        return _eventTypeRegistry?.GetLogicalName(eventType) ?? eventType.AssemblyQualifiedName!;
    }

    /// <summary>
    /// Deserializes a whole stream, refusing to return it at all when one of its events
    /// cannot be read. A stream returned short of an event the caller cannot see is worse
    /// than a failure: it rebuilds an aggregate from an incomplete history, silently.
    /// </summary>
    /// <param name="aggregateId">The aggregate the events belong to.</param>
    /// <param name="storedEvents">The stored events, already filtered and ordered.</param>
    /// <returns>The deserialized events, or a failure naming the aggregate and the unresolved name.</returns>
    private Result<IReadOnlyList<IDomainEvent>> MaterializeEvents(string aggregateId, IEnumerable<StoredEvent> storedEvents)
    {
        var domainEvents = new List<IDomainEvent>();

        foreach (var storedEvent in storedEvents)
        {
            var result = TryDeserializeStoredEvent(storedEvent);

            if (result.IsFailure)
            {
                return Result.Failure<IReadOnlyList<IDomainEvent>>(
                    Error.Failure(
                        "EventStore.EventTypeUnresolved",
                        $"Event {storedEvent.EventId} (version {storedEvent.Version}) of aggregate {aggregateId} " +
                        $"could not be read: event type name '{storedEvent.EventType}' did not resolve. " +
                        $"Underlying error: {result.Error.Message}"));
            }

            domainEvents.Add(result.Value);
        }

        return Result.Success<IReadOnlyList<IDomainEvent>>(domainEvents);
    }

    /// <summary>
    /// Securely deserializes a stored event back to a domain event using the whitelisted type registry.
    /// </summary>
    /// <param name="storedEvent">The stored event.</param>
    /// <returns>The deserialized domain event, or the error that prevented it.</returns>
    private Result<IDomainEvent> TryDeserializeStoredEvent(StoredEvent storedEvent)
    {
        try
        {
            // Use secure deserializer which checks whitelist and prevents attacks
            var result = _eventDeserializer.TryDeserializeEvent(storedEvent.EventData, storedEvent.EventType);

            if (result.IsFailure)
            {
                _logger?.LogWarning("Failed to securely deserialize event {EventId} of type {EventType}: {Error}",
                    storedEvent.EventId, storedEvent.EventType, result.Error.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Unexpected error deserializing event {EventId} of type {EventType}",
                storedEvent.EventId, storedEvent.EventType);

            return Result.Failure<IDomainEvent>(
                Error.Failure("EventStore.DeserializationFailed", ex.Message));
        }
    }

    /// <summary>
    /// Securely deserializes a stored event back to a domain event using the whitelisted type registry.
    /// </summary>
    /// <param name="storedEvent">The stored event.</param>
    /// <returns>The deserialized domain event, or null if deserialization fails or type is not whitelisted.</returns>
    private IDomainEvent? DeserializeEvent(StoredEvent storedEvent)
    {
        var result = TryDeserializeStoredEvent(storedEvent);

        return result.IsFailure ? null : result.Value;
    }

    /// <summary>
    /// Throws an exception if the instance has been disposed.
    /// </summary>
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryEventStore));
        }
    }

    /// <summary>
    /// Disposes the resources used by the event store.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _lock?.Dispose();
            _events.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// Represents a stored event with metadata.
    /// </summary>
    private sealed class StoredEvent
    {
        public required Guid EventId { get; init; }
        public required string AggregateId { get; init; }
        public required string AggregateType { get; init; }
        public required string EventType { get; init; }
        public required string EventData { get; init; }
        public required long Version { get; init; }
        public required long AggregateVersion { get; init; }
        public required DateTimeOffset OccurredOn { get; init; }
        public required DateTimeOffset StoredAt { get; init; }
        public string? TenantId { get; init; }
        public Dictionary<string, object> Metadata { get; init; } = new();
    }
}
