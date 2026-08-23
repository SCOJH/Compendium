// -----------------------------------------------------------------------
// <copyright file="EventTypeNamePropagationTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.EventSourcing;
using Compendium.Core.EventSourcing.Attributes;
using Compendium.Infrastructure.Projections;
using Microsoft.Extensions.Logging.Abstractions;

namespace Compendium.Infrastructure.Tests.EventSourcing;

/// <summary>
/// Covers the two halves of logical event naming in the in-memory stores: the name an event
/// is written under, and what a read does when a name no longer resolves.
/// </summary>
public sealed class EventTypeNamePropagationTests
{
    private const string AggregateId = "agg-logical-name";

    [Fact]
    public async Task GetEventsAsync_WhenOneEventTypeNoLongerResolves_FailsInsteadOfTruncating()
    {
        // Arrange - two events written, then the second type is taken out of the registry
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(SurvivingEvent));
        registry.RegisterEventType(typeof(VanishingEvent));

        using var store = NewStore(registry);
        await AppendBothEvents(store);

        registry.Clear();
        registry.RegisterEventType(typeof(SurvivingEvent));

        // Act
        var result = await store.GetEventsAsync(AggregateId);

        // Assert - a stream short of one event must not be handed back as a success
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EventStore.EventTypeUnresolved");
        result.Error.Message.Should().Contain(AggregateId);
        result.Error.Message.Should().Contain("VanishingEvent");
    }

    [Fact]
    public async Task GetEventsAsyncFromVersion_WhenOneEventTypeNoLongerResolves_Fails()
    {
        // Arrange
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(SurvivingEvent));
        registry.RegisterEventType(typeof(VanishingEvent));

        using var store = NewStore(registry);
        await AppendBothEvents(store);

        registry.Clear();
        registry.RegisterEventType(typeof(SurvivingEvent));

        // Act
        var result = await store.GetEventsAsync(AggregateId, fromVersion: 0);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EventStore.EventTypeUnresolved");
    }

    [Fact]
    public async Task GetEventsInRangeAsync_WhenOneEventTypeNoLongerResolves_Fails()
    {
        // Arrange
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(SurvivingEvent));
        registry.RegisterEventType(typeof(VanishingEvent));

        using var store = NewStore(registry);
        await AppendBothEvents(store);

        registry.Clear();
        registry.RegisterEventType(typeof(SurvivingEvent));

        // Act
        var result = await store.GetEventsInRangeAsync(AggregateId, fromVersion: 0, toVersion: 10);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EventStore.EventTypeUnresolved");
    }

    [Fact]
    public async Task GetEventsAsync_WhenEveryEventTypeResolves_ReturnsTheWholeStream()
    {
        // Arrange - the control case: nothing is taken out of the registry
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(SurvivingEvent));
        registry.RegisterEventType(typeof(VanishingEvent));

        using var store = NewStore(registry);
        await AppendBothEvents(store);

        // Act
        var result = await store.GetEventsAsync(AggregateId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task AppendEventsAsync_WithRegistry_WritesTheLogicalNameOfADecoratedEvent()
    {
        // Arrange
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(SurvivingEvent));

        var names = new List<string>();
        using var store = NewStore(registry, NameCapturingDeserializer(names));

        // Act
        await store.AppendEventsAsync(AggregateId, new IDomainEvent[] { NewSurvivingEvent(1) }, 0);
        var result = await store.GetEventsAsync(AggregateId);

        // Assert
        result.IsSuccess.Should().BeTrue();
        names.Should().ContainSingle().Which.Should().Be("Compendium.Tests.SurvivingEvent");
    }

    [Fact]
    public async Task AppendEventsAsync_WithRegistry_WritesTheAssemblyQualifiedNameOfAnUndecoratedEvent()
    {
        // Arrange
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(UndecoratedEvent));

        var names = new List<string>();
        using var store = NewStore(registry, NameCapturingDeserializer(names));

        // Act
        await store.AppendEventsAsync(AggregateId, new IDomainEvent[] { NewUndecoratedEvent(1) }, 0);
        await store.GetEventsAsync(AggregateId);

        // Assert - the value from before logical names existed, to the character
        names.Should().ContainSingle().Which.Should().Be(typeof(UndecoratedEvent).AssemblyQualifiedName);
    }

    [Fact]
    public async Task AppendEventsAsync_WithoutRegistry_WritesTheAssemblyQualifiedName()
    {
        // Arrange - no registry supplied: the store behaves exactly as it did before
        var names = new List<string>();
        using var store = new InMemoryEventStore(
            NameCapturingDeserializer(names),
            NullLogger<InMemoryEventStore>.Instance);

        // Act
        await store.AppendEventsAsync(AggregateId, new IDomainEvent[] { NewSurvivingEvent(1) }, 0);
        await store.GetEventsAsync(AggregateId);

        // Assert
        names.Should().ContainSingle().Which.Should().Be(typeof(SurvivingEvent).AssemblyQualifiedName);
    }

    [Fact]
    public async Task StreamingStore_WithRegistry_WritesTheLogicalName()
    {
        // Arrange
        using var registry = new EventTypeRegistry();
        registry.RegisterEventType(typeof(SurvivingEvent));

        using var store = new InMemoryStreamingEventStore(tenantContext: null, eventTypeRegistry: registry);

        // Act
        await store.AppendEventsAsync(AggregateId, new IDomainEvent[] { NewSurvivingEvent(1) }, 0);

        var streamed = new List<EventData>();
        await foreach (var eventData in store.StreamEventsAsync(null, fromPosition: 0))
        {
            streamed.Add(eventData);
        }

        // Assert
        streamed.Should().ContainSingle();
        streamed[0].EventType.Should().Be("Compendium.Tests.SurvivingEvent");
    }

    [Fact]
    public async Task StreamingStore_WithoutRegistry_WritesTheAssemblyQualifiedName()
    {
        // Arrange
        using var store = new InMemoryStreamingEventStore();

        // Act
        await store.AppendEventsAsync(AggregateId, new IDomainEvent[] { NewSurvivingEvent(1) }, 0);

        var streamed = new List<EventData>();
        await foreach (var eventData in store.StreamEventsAsync(null, fromPosition: 0))
        {
            streamed.Add(eventData);
        }

        // Assert
        streamed.Should().ContainSingle();
        streamed[0].EventType.Should().Be(typeof(SurvivingEvent).AssemblyQualifiedName);
    }

    private static InMemoryEventStore NewStore(EventTypeRegistry registry, IEventDeserializer? deserializer = null)
    {
        return new InMemoryEventStore(
            deserializer ?? new SecureEventDeserializer(registry),
            NullLogger<InMemoryEventStore>.Instance,
            tenantContext: null,
            eventTypeRegistry: registry);
    }

    /// <summary>
    /// A deserializer that records the event type name it is handed, so that a test can assert
    /// on the name actually written to the log without reaching into the store's private state.
    /// </summary>
    private static IEventDeserializer NameCapturingDeserializer(List<string> names)
    {
        var deserializer = Substitute.For<IEventDeserializer>();
        deserializer.TryDeserializeEvent(Arg.Any<string>(), Arg.Any<string>())
            .Returns(callInfo =>
            {
                names.Add(callInfo.ArgAt<string>(1));
                return Result.Success<IDomainEvent>(NewSurvivingEvent(1));
            });

        return deserializer;
    }

    private static async Task AppendBothEvents(InMemoryEventStore store)
    {
        var events = new IDomainEvent[] { NewSurvivingEvent(1), NewVanishingEvent(2) };
        var append = await store.AppendEventsAsync(AggregateId, events, 0);

        append.IsSuccess.Should().BeTrue();
    }

    private static SurvivingEvent NewSurvivingEvent(long version) => new()
    {
        EventId = Guid.NewGuid(),
        AggregateId = AggregateId,
        AggregateType = "Aggregate",
        OccurredOn = DateTimeOffset.UtcNow,
        AggregateVersion = version,
        EventVersion = 1,
    };

    private static VanishingEvent NewVanishingEvent(long version) => new()
    {
        EventId = Guid.NewGuid(),
        AggregateId = AggregateId,
        AggregateType = "Aggregate",
        OccurredOn = DateTimeOffset.UtcNow,
        AggregateVersion = version,
        EventVersion = 1,
    };

    private static UndecoratedEvent NewUndecoratedEvent(long version) => new()
    {
        EventId = Guid.NewGuid(),
        AggregateId = AggregateId,
        AggregateType = "Aggregate",
        OccurredOn = DateTimeOffset.UtcNow,
        AggregateVersion = version,
        EventVersion = 1,
    };
}

/// <summary>An event carrying a logical name, kept in the registry for the whole of a test.</summary>
[EventTypeName("Compendium.Tests.SurvivingEvent")]
public sealed class SurvivingEvent : IDomainEvent
{
    public required Guid EventId { get; init; }

    public required string AggregateId { get; init; }

    public required string AggregateType { get; init; }

    public required DateTimeOffset OccurredOn { get; init; }

    public required long AggregateVersion { get; init; }

    public required int EventVersion { get; init; }
}

/// <summary>An event carrying a logical name, taken out of the registry mid-test.</summary>
[EventTypeName("Compendium.Tests.VanishingEvent")]
public sealed class VanishingEvent : IDomainEvent
{
    public required Guid EventId { get; init; }

    public required string AggregateId { get; init; }

    public required string AggregateType { get; init; }

    public required DateTimeOffset OccurredOn { get; init; }

    public required long AggregateVersion { get; init; }

    public required int EventVersion { get; init; }
}

/// <summary>An event with no logical name: it must keep being written under its AQN.</summary>
public sealed class UndecoratedEvent : IDomainEvent
{
    public required Guid EventId { get; init; }

    public required string AggregateId { get; init; }

    public required string AggregateType { get; init; }

    public required DateTimeOffset OccurredOn { get; init; }

    public required long AggregateVersion { get; init; }

    public required int EventVersion { get; init; }
}
