// -----------------------------------------------------------------------
// <copyright file="AggregateRoot.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Domain.Events;

namespace Compendium.Core.Domain.Primitives;

/// <summary>
/// Base class for aggregate roots in the domain.
/// Provides domain event management and optimistic concurrency control.
/// </summary>
/// <typeparam name="TId">The type of the aggregate root identifier.</typeparam>
public abstract class AggregateRoot<TId> : Entity<TId>, IDisposable
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];
    private readonly HashSet<string> _eventHashes = [];
    private readonly ILockingStrategy _lockingStrategy;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AggregateRoot{TId}"/> class.
    /// </summary>
    /// <param name="id">The aggregate root identifier.</param>
    /// <param name="lockingStrategy">The locking strategy to use. If null, uses ReaderWriterLockStrategy.</param>
    protected AggregateRoot(TId id, ILockingStrategy? lockingStrategy = null) : base(id)
    {
        Version = 0;
        _lockingStrategy = lockingStrategy ?? new ReaderWriterLockStrategy();
    }

    /// <summary>
    /// Gets the version for optimistic concurrency control.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Gets the uncommitted domain events, in the order they were raised.
    /// </summary>
    /// <remarks>
    /// The order is the data: replaying a stream written out of order rebuilds a
    /// different state. The return type is therefore an ordered sequence, and the
    /// snapshot is a fresh copy so that clearing or appending afterwards cannot
    /// mutate a snapshot already handed out. Uniqueness is carried by
    /// <c>_eventHashes</c> at insertion time, not by the returned collection.
    /// </remarks>
    public IReadOnlyList<IDomainEvent> DomainEvents =>
        _lockingStrategy.ExecuteRead(() => (IReadOnlyList<IDomainEvent>)_domainEvents.ToList().AsReadOnly());

    /// <summary>
    /// Gets a value indicating whether there are uncommitted domain events.
    /// </summary>
    public bool HasDomainEvents =>
        _lockingStrategy.ExecuteRead(() => _domainEvents.Count > 0);

    /// <summary>
    /// Adds a domain event to the aggregate.
    /// Prevents duplicate events using content-based deduplication.
    /// </summary>
    /// <param name="domainEvent">The domain event to add.</param>
    protected void AddDomainEvent(IDomainEvent domainEvent)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AggregateRoot<TId>));
        }

        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventHash = ComputeEventHash(domainEvent);

        _lockingStrategy.ExecuteWrite(() =>
        {
            if (_eventHashes.Add(eventHash))
            {
                _domainEvents.Add(domainEvent);
            }
        });
    }

    /// <summary>
    /// Removes a domain event from the aggregate.
    /// </summary>
    /// <param name="domainEvent">The domain event to remove.</param>
    protected void RemoveDomainEvent(IDomainEvent domainEvent)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AggregateRoot<TId>));
        }

        ArgumentNullException.ThrowIfNull(domainEvent);

        var eventHash = ComputeEventHash(domainEvent);

        _lockingStrategy.ExecuteWrite(() =>
        {
            if (_eventHashes.Remove(eventHash))
            {
                _domainEvents.Remove(domainEvent);
            }
        });
    }

    /// <summary>
    /// Gets all uncommitted domain events and clears the collection.
    /// </summary>
    /// <returns>The uncommitted domain events, in the order they were raised.</returns>
    public IReadOnlyList<IDomainEvent> GetUncommittedEvents()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AggregateRoot<TId>));
        }

        // Execute atomically to avoid race conditions
        IReadOnlyList<IDomainEvent> events = null!;
        _lockingStrategy.ExecuteWrite(() =>
        {
            events = _domainEvents.ToList().AsReadOnly();
            ClearDomainEventsInternal();
        });
        return events;
    }

    /// <summary>
    /// Marks all uncommitted domain events as committed by clearing them.
    /// This method should be called after successfully persisting events.
    /// </summary>
    public void MarkEventsAsCommitted()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AggregateRoot<TId>));
        }

        _lockingStrategy.ExecuteWrite(() => ClearDomainEventsInternal());
    }

    /// <summary>
    /// Clears all domain events from the aggregate.
    /// </summary>
    public void ClearDomainEvents()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AggregateRoot<TId>));
        }

        _lockingStrategy.ExecuteWrite(() => ClearDomainEventsInternal());
    }

    /// <summary>
    /// Internal method to clear domain events (already locked).
    /// </summary>
    private void ClearDomainEventsInternal()
    {
        _domainEvents.Clear();
        _eventHashes.Clear();
    }

    /// <summary>
    /// Increments the version for optimistic concurrency control.
    /// </summary>
    protected void IncrementVersion()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AggregateRoot<TId>));
        }

        _lockingStrategy.ExecuteWrite(() =>
        {
            Version++;
            Touch();
        });
    }

    /// <summary>
    /// Sets the version explicitly (used during rehydration from event store).
    /// </summary>
    /// <param name="version">The version to set.</param>
    protected void SetVersion(long version)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(version);
        Version = version;
    }

    /// <summary>
    /// Computes a hash for the domain event to enable deduplication.
    /// </summary>
    /// <param name="domainEvent">The domain event.</param>
    /// <returns>A hash string representing the event.</returns>
    private static string ComputeEventHash(IDomainEvent domainEvent)
    {
        // Use EventId which is guaranteed to be unique for each event
        return domainEvent.EventId.ToString();
    }

    /// <summary>
    /// Disposes the aggregate root and its resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the aggregate root and its resources.
    /// </summary>
    /// <param name="disposing">True if disposing managed resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _lockingStrategy.Dispose();
            _domainEvents.Clear();
            _eventHashes.Clear();
            _disposed = true;
        }
    }
}
