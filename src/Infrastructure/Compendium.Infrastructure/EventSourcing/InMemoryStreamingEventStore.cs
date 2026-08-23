// -----------------------------------------------------------------------
// <copyright file="InMemoryStreamingEventStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Compendium.Core.EventSourcing;
using Compendium.Core.Results;
using Compendium.Infrastructure.Projections;
using Compendium.Multitenancy;

namespace Compendium.Infrastructure.EventSourcing;

/// <summary>
/// In-memory implementation of <see cref="IStreamingEventStore"/> for testing
/// and InMemory-backed framework E2E scenarios.
/// </summary>
/// <remarks>
/// Maintains its own storage with a monotonic global position counter so that
/// projection rebuilds and live processing can stream events across all
/// streams from a known position. Optimistic concurrency, tenant isolation,
/// and read methods match the contract of <see cref="InMemoryEventStore"/>.
/// </remarks>
public sealed class InMemoryStreamingEventStore : IStreamingEventStore, IDisposable
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streams = new();
    private readonly List<StoredEvent> _globalLog = new();
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly ITenantContext? _tenantContext;
    private readonly IEventTypeRegistry? _eventTypeRegistry;
    private readonly JsonSerializerOptions _jsonOptions;
    private long _globalSequence;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="InMemoryStreamingEventStore"/> class.</summary>
    /// <param name="tenantContext">The tenant context for multi-tenancy support (optional).</param>
    /// <param name="eventTypeRegistry">
    /// The event type registry used to name written events. When it is not supplied, events are
    /// stamped with their assembly qualified name — the behaviour that predates logical names.
    /// </param>
    public InMemoryStreamingEventStore(
        ITenantContext? tenantContext = null,
        IEventTypeRegistry? eventTypeRegistry = null)
    {
        _tenantContext = tenantContext;
        _eventTypeRegistry = eventTypeRegistry;
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };
    }

    /// <inheritdoc />
    public Task<Result> AppendEventsAsync(
        string aggregateId,
        IEnumerable<IDomainEvent> events,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ArgumentNullException.ThrowIfNull(events);
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
            var stream = _streams.GetOrAdd(streamKey, _ => new List<StoredEvent>());

            var currentVersion = stream.Count;
            if (expectedVersion != -1 && expectedVersion != currentVersion)
            {
                return Task.FromResult(Result.Failure(
                    Error.Conflict(
                        "EventStore.ConcurrencyConflict",
                        $"Expected version {expectedVersion} but current version is {currentVersion}")));
            }

            var now = DateTimeOffset.UtcNow;
            var tenantId = _tenantContext?.TenantId;
            var version = currentVersion;

            foreach (var domainEvent in eventList)
            {
                version++;
                _globalSequence++;

                var stored = new StoredEvent
                {
                    EventId = domainEvent.EventId,
                    StreamKey = streamKey,
                    AggregateId = aggregateId,
                    AggregateType = domainEvent.AggregateType,
                    StreamPosition = version,
                    GlobalPosition = _globalSequence,
                    OccurredOn = domainEvent.OccurredOn,
                    StoredAt = now,
                    TenantId = tenantId,
                    Event = domainEvent,
                    EventTypeName = ResolveEventTypeName(domainEvent),
                    EventDataJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType(), _jsonOptions),
                };

                stream.Add(stored);
                _globalLog.Add(stored);
            }

            return Task.FromResult(Result.Success());
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
        => GetEventsAsync(aggregateId, 0, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<IDomainEvent>>> GetEventsAsync(
        string aggregateId,
        long fromVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);
            if (!_streams.TryGetValue(streamKey, out var stream))
            {
                return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(Array.Empty<IDomainEvent>()));
            }

            var events = stream
                .Where(e => e.StreamPosition > fromVersion)
                .Select(e => e.Event)
                .ToArray();

            return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(events));
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
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);
            if (!_streams.TryGetValue(streamKey, out var stream))
            {
                return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(Array.Empty<IDomainEvent>()));
            }

            var events = stream
                .Where(e => e.StreamPosition >= fromVersion && e.StreamPosition <= toVersion)
                .Select(e => e.Event)
                .ToArray();

            return Task.FromResult(Result.Success<IReadOnlyList<IDomainEvent>>(events));
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
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);
            if (!_streams.TryGetValue(streamKey, out var stream) || stream.Count == 0)
            {
                return Task.FromResult(Result.Failure<IDomainEvent>(
                    Error.NotFound("EventStore.NoEvents", $"No events found for aggregate {aggregateId}")));
            }

            return Task.FromResult(Result.Success(stream[^1].Event));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<long>> GetVersionAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            var streamKey = GetStreamKey(aggregateId);
            var version = _streams.TryGetValue(streamKey, out var stream) ? (long)stream.Count : 0L;
            return Task.FromResult(Result.Success(version));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<Result<bool>> ExistsAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        ThrowIfDisposed();

        var streamKey = GetStreamKey(aggregateId);
        var exists = _streams.TryGetValue(streamKey, out var stream) && stream.Count > 0;
        return Task.FromResult(Result.Success(exists));
    }

    /// <inheritdoc />
    public Task<Result<EventStoreStatistics>> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        _lock.EnterReadLock();
        try
        {
            var tenantId = _tenantContext?.TenantId;
            var snapshot = string.IsNullOrEmpty(tenantId)
                ? _streams.Values.ToList()
                : _streams
                    .Where(kvp => kvp.Key.StartsWith($"{tenantId}:", StringComparison.Ordinal))
                    .Select(kvp => kvp.Value)
                    .ToList();

            var stats = new EventStoreStatistics
            {
                TotalEvents = snapshot.Sum(s => s.Count),
                TotalAggregates = snapshot.Count,
            };
            return Task.FromResult(Result.Success(stats));
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<EventData> StreamEventsAsync(
        string? streamId,
        long fromPosition,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // Snapshot the global log under the lock; yield outside the lock.
        List<StoredEvent> snapshot;
        _lock.EnterReadLock();
        try
        {
            snapshot = _globalLog.Where(e => e.GlobalPosition > fromPosition).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var stored in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (streamId is not null && stored.AggregateId != streamId)
            {
                continue;
            }

            yield return new EventData
            {
                EventId = stored.EventId,
                StreamId = stored.AggregateId,
                StreamPosition = stored.StreamPosition,
                GlobalPosition = stored.GlobalPosition,
                EventType = stored.EventTypeName,
                EventDataJson = stored.EventDataJson,
                Event = stored.Event,
                Timestamp = stored.OccurredOn.UtcDateTime,
                TenantId = stored.TenantId,
            };

            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<long> GetEventCountAsync(string? streamId = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            if (streamId is null)
            {
                return Task.FromResult((long)_globalLog.Count);
            }

            var count = _streams.TryGetValue(GetStreamKey(streamId), out var stream) ? stream.Count : 0;
            return Task.FromResult((long)count);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public Task<long> GetMaxGlobalPositionAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        _lock.EnterReadLock();
        try
        {
            return Task.FromResult(_globalSequence);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _lock.Dispose();
        _disposed = true;
    }

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

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(InMemoryStreamingEventStore));
        }
    }

    private sealed class StoredEvent
    {
        public required Guid EventId { get; init; }

        public required string StreamKey { get; init; }

        public required string AggregateId { get; init; }

        public required string AggregateType { get; init; }

        public required long StreamPosition { get; init; }

        public required long GlobalPosition { get; init; }

        public required DateTimeOffset OccurredOn { get; init; }

        public required DateTimeOffset StoredAt { get; init; }

        public string? TenantId { get; init; }

        public required IDomainEvent Event { get; init; }

        public required string EventTypeName { get; init; }

        public required string EventDataJson { get; init; }
    }
}
