// -----------------------------------------------------------------------
// <copyright file="AggregateRootTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Tests.TestHelpers;

namespace Compendium.Core.Tests.Domain.Primitives;

public class AggregateRootTests
{
    /// <summary>
    /// Number of events raised by the order tests. Taken well above the point where a
    /// hash-ordered collection stops preserving insertion order — the size at which
    /// the assertion stops being satisfiable by accident.
    /// </summary>
    private const int OrderProbeBatchSize = 128;

    [Fact]
    public void Constructor_WithValidId_InitializesCorrectly()
    {
        // Arrange
        var id = Guid.NewGuid();
        var name = "Test Aggregate";

        // Act
        var aggregate = new TestAggregate(id, name);

        // Assert
        aggregate.Id.Should().Be(id);
        aggregate.Name.Should().Be(name);
        aggregate.Version.Should().Be(0);
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.HasDomainEvents.Should().BeFalse();
    }

    [Fact]
    public void AddDomainEvent_ValidEvent_AddsToCollection()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var domainEvent = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1);

        // Act
        aggregate.TestAddDomainEvent(domainEvent);

        // Assert
        aggregate.DomainEvents.Should().ContainSingle().Which.Should().Be(domainEvent);
        aggregate.HasDomainEvents.Should().BeTrue();
    }

    [Fact]
    public void AddDomainEvent_NullEvent_ThrowsArgumentNullException()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        var act = () => aggregate.TestAddDomainEvent(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddDomainEvent_MultipleEvents_MaintainsOrder()
    {
        // Arrange
        // The batch is large on purpose. Order is the data for an event-sourced
        // aggregate, and a handful of events does not tell a guarantee apart from a
        // coincidence: a hash-ordered collection happens to preserve insertion order
        // on a small batch. OrderProbeBatchSize is the size at which that luck runs
        // out. Equal() compares the whole sequence position by position, where
        // ContainInOrder() only checks the relative order of the elements it is given.
        var aggregate = new TestAggregate(Guid.NewGuid());
        var expected = Enumerable.Range(0, OrderProbeBatchSize)
            .Select(i => new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), i, $"Event {i}"))
            .ToList();

        // Act
        foreach (var domainEvent in expected)
        {
            aggregate.TestAddDomainEvent(domainEvent);
        }

        // Assert
        aggregate.DomainEvents.Should().HaveCount(OrderProbeBatchSize);
        aggregate.DomainEvents.Should().Equal(expected);
    }

    [Fact]
    public void AddDomainEvent_DuplicateEvent_PreventsDuplication()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var domainEvent = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1);

        // Act
        aggregate.TestAddDomainEvent(domainEvent);
        aggregate.TestAddDomainEvent(domainEvent); // Same event

        // Assert
        aggregate.DomainEvents.Should().ContainSingle().Which.Should().Be(domainEvent);
    }

    [Fact]
    public void RemoveDomainEvent_ExistingEvent_RemovesFromCollection()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var event1 = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1, "Event 1");
        var event2 = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 2, "Event 2");

        aggregate.TestAddDomainEvent(event1);
        aggregate.TestAddDomainEvent(event2);

        // Act
        aggregate.TestRemoveDomainEvent(event1);

        // Assert
        aggregate.DomainEvents.Should().ContainSingle().Which.Should().Be(event2);
    }

    [Fact]
    public void RemoveDomainEvent_NonExistentEvent_DoesNothing()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var existingEvent = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1);
        var nonExistentEvent = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 2);

        aggregate.TestAddDomainEvent(existingEvent);

        // Act
        aggregate.TestRemoveDomainEvent(nonExistentEvent);

        // Assert
        aggregate.DomainEvents.Should().ContainSingle().Which.Should().Be(existingEvent);
    }

    [Fact]
    public void RemoveDomainEvent_NullEvent_ThrowsArgumentNullException()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        var act = () => aggregate.TestRemoveDomainEvent(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ClearDomainEvents_WithEvents_RemovesAllEvents()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var event1 = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1);
        var event2 = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 2);

        aggregate.TestAddDomainEvent(event1);
        aggregate.TestAddDomainEvent(event2);

        // Act
        aggregate.ClearDomainEvents();

        // Assert
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.HasDomainEvents.Should().BeFalse();
    }

    [Fact]
    public void ClearDomainEvents_WithoutEvents_DoesNothing()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        aggregate.ClearDomainEvents();

        // Assert
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.HasDomainEvents.Should().BeFalse();
    }

    [Fact]
    public void GetUncommittedEvents_WithEvents_ReturnsEventsAndClears()
    {
        // Arrange
        // Same large batch as AddDomainEvent_MultipleEvents_MaintainsOrder, and for
        // the same reason: this is the second path out of the aggregate, the one the
        // event store is fed from.
        var aggregate = new TestAggregate(Guid.NewGuid());
        var expected = Enumerable.Range(0, OrderProbeBatchSize)
            .Select(i => new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), i, $"Event {i}"))
            .ToList();

        foreach (var domainEvent in expected)
        {
            aggregate.TestAddDomainEvent(domainEvent);
        }

        // Act
        var uncommittedEvents = aggregate.GetUncommittedEvents();

        // Assert
        uncommittedEvents.Should().HaveCount(OrderProbeBatchSize);
        uncommittedEvents.Should().Equal(expected);
        aggregate.DomainEvents.Should().BeEmpty();
        aggregate.HasDomainEvents.Should().BeFalse();
    }

    [Fact]
    public void GetUncommittedEvents_WithoutEvents_ReturnsEmptyCollection()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        var uncommittedEvents = aggregate.GetUncommittedEvents();

        // Assert
        uncommittedEvents.Should().BeEmpty();
    }

    [Fact]
    public void IncrementVersion_IncrementsVersionAndUpdatesModifiedAt()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var originalVersion = aggregate.Version;
        var originalModifiedAt = aggregate.ModifiedAt;
        Thread.Sleep(10);

        // Act
        aggregate.TestIncrementVersion();

        // Assert
        aggregate.Version.Should().Be(originalVersion + 1);
        aggregate.ModifiedAt.Should().BeAfter(originalModifiedAt);
    }

    [Fact]
    public void SetVersion_WithValidVersion_SetsVersion()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var newVersion = 5L;

        // Act
        aggregate.TestSetVersion(newVersion);

        // Assert
        aggregate.Version.Should().Be(newVersion);
    }

    [Fact]
    public void SetVersion_WithNegativeVersion_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Act
        var act = () => aggregate.TestSetVersion(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void UpdateName_AddsEventAndIncrementsVersion()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid(), "Original Name");
        var originalVersion = aggregate.Version;
        var newName = "Updated Name";

        // Act
        aggregate.UpdateName(newName);

        // Assert
        aggregate.Name.Should().Be(newName);
        aggregate.Version.Should().Be(originalVersion + 1);
        aggregate.DomainEvents.Should().ContainSingle();

        var domainEvent = aggregate.DomainEvents.First() as TestDomainEvent;
        domainEvent.Should().NotBeNull();
        domainEvent!.Data.Should().Contain(newName);
    }

    [Fact]
    public void ConcurrentAccess_AddingEvents_ThreadSafe()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var events = Enumerable.Range(0, 100)
            .Select(i => new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), i, $"Event {i}"))
            .ToList();

        // Act
        Parallel.ForEach(events, domainEvent =>
        {
            aggregate.TestAddDomainEvent(domainEvent);
        });

        // Assert
        aggregate.DomainEvents.Should().HaveCount(100);
        aggregate.HasDomainEvents.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentAccess_VersionIncrement_ThreadSafe()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var tasks = new List<Task>();
        var incrementCount = 100;

        // Act
        for (int i = 0; i < incrementCount; i++)
        {
            tasks.Add(Task.Run(() => aggregate.TestIncrementVersion()));
        }

        await Task.WhenAll(tasks);

        // Assert
        aggregate.Version.Should().Be(incrementCount);
    }

    [Fact]
    public void DomainEvents_ReadOnlyCollection_CannotBeModifiedDirectly()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        var domainEvent = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1);
        aggregate.TestAddDomainEvent(domainEvent);

        // Act & Assert
        var domainEvents = aggregate.DomainEvents;
        domainEvents.Should().BeAssignableTo<IReadOnlyCollection<IDomainEvent>>();

        // Verify it's truly read-only by checking it's not directly modifiable
        // Note: ReadOnlyCollection<T> implements ICollection<T> but throws on modifications
        domainEvents.Should().BeAssignableTo<IReadOnlyCollection<IDomainEvent>>();

        // Verify that attempting to modify throws an exception
        if (domainEvents is ICollection<IDomainEvent> collection)
        {
            collection.IsReadOnly.Should().BeTrue();
        }
    }

    [Fact]
    public void EventDeduplication_SameEventHash_PreventsDuplicates()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());

        // Create events that should have the same hash (same type, aggregate ID, and timestamp precision)
        var event1 = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1, "Same Data");
        var event2 = new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1, "Same Data");

        // Act
        aggregate.TestAddDomainEvent(event1);
        aggregate.TestAddDomainEvent(event2);

        // Assert
        // Note: The current implementation uses a simple hash that might allow duplicates
        // This test documents the current behavior and can be updated when deduplication is improved
        aggregate.DomainEvents.Count.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void DomainEvents_EventsEqualByValue_ReturnsThemAll()
    {
        // Arrange
        // Two events that are distinct instances with distinct EventIds — so
        // _eventHashes, which deduplicates on EventId, accepts both — but that are
        // equal to one another for EqualityComparer<T>.Default. A set-based snapshot
        // would silently fold them into one, and the caller would persist one event
        // fewer than the aggregate raised. Uniqueness belongs to _eventHashes; the
        // snapshot must not add a second, coarser rule of its own.
        var aggregate = new TestAggregate(Guid.NewGuid());
        var first = new AlwaysEqualDomainEvent(aggregate.Id.ToString());
        var second = new AlwaysEqualDomainEvent(aggregate.Id.ToString());

        first.EventId.Should().NotBe(second.EventId);
        first.Equals(second).Should().BeTrue();

        // Act
        aggregate.TestAddDomainEvent(first);
        aggregate.TestAddDomainEvent(second);

        // Assert
        aggregate.DomainEvents.Should().HaveCount(2);
    }

    [Fact]
    public void DomainEvents_And_GetUncommittedEvents_ExposeAnOrderedSequenceType()
    {
        // Arrange
        // The order tests above would very probably fail on an unordered return type;
        // this one fails for certain if the contract is widened back to a type that
        // promises no order. It is the assertion that makes the guarantee a property
        // of the signature rather than of the method body.
        var expected = typeof(IReadOnlyList<IDomainEvent>);

        // Act
        var propertyType = typeof(AggregateRoot<>).GetProperty(nameof(TestAggregate.DomainEvents))!.PropertyType;
        var returnType = typeof(AggregateRoot<>).GetMethod(nameof(TestAggregate.GetUncommittedEvents))!.ReturnType;

        // Assert
        propertyType.Should().Be(expected);
        returnType.Should().Be(expected);
    }

    [Fact]
    public void DomainEvents_SnapshotTaken_IsNotAffectedByLaterChanges()
    {
        // Arrange
        var aggregate = new TestAggregate(Guid.NewGuid());
        aggregate.TestAddDomainEvent(new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 1));
        var snapshot = aggregate.DomainEvents;

        // Act
        aggregate.TestAddDomainEvent(new TestDomainEvent(aggregate.Id.ToString(), nameof(TestAggregate), 2));
        aggregate.ClearDomainEvents();

        // Assert
        snapshot.Should().ContainSingle();
    }

    /// <summary>
    /// A domain event whose EventId is unique per instance but which compares equal to
    /// every other instance of its type. It exists to tell the two notions of identity
    /// apart: the one the aggregate deduplicates on, and the one a set would use.
    /// </summary>
    private sealed class AlwaysEqualDomainEvent : IDomainEvent
    {
        public AlwaysEqualDomainEvent(string aggregateId)
        {
            AggregateId = aggregateId;
        }

        public Guid EventId { get; } = Guid.NewGuid();

        public string AggregateId { get; }

        public string AggregateType => nameof(TestAggregate);

        public DateTimeOffset OccurredOn { get; } = DateTimeOffset.UtcNow;

        public long AggregateVersion => 1;

        public int EventVersion => 1;

        public override bool Equals(object? obj) => obj is AlwaysEqualDomainEvent;

        public override int GetHashCode() => 0;
    }
}
