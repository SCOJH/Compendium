// -----------------------------------------------------------------------
// <copyright file="InMemoryStreamingEventStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.EventSourcing;
using Compendium.Infrastructure.Projections;
using AwesomeAssertions;

namespace Compendium.Infrastructure.Tests.EventSourcing;

public sealed class InMemoryStreamingEventStoreTests
{
    private readonly InMemoryStreamingEventStore _sut = new();

    [Fact]
    public async Task Append_ThenGetEvents_ReturnsEventsInOrder()
    {
        // Arrange
        var events = new IDomainEvent[]
        {
            NewEvent("agg-1", "Order", 1),
            NewEvent("agg-1", "Order", 2),
            NewEvent("agg-1", "Order", 3),
        };

        // Act
        var append = await _sut.AppendEventsAsync("agg-1", events, expectedVersion: 0);
        var fetch = await _sut.GetEventsAsync("agg-1");

        // Assert
        append.IsSuccess.Should().BeTrue();
        fetch.Value.Should().HaveCount(3);
    }

    [Fact]
    public async Task Append_ConcurrencyConflict_ReturnsFailure()
    {
        // Arrange
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1) }, expectedVersion: 0);

        // Act — expectedVersion=0 but actual=1.
        var result = await _sut.AppendEventsAsync(
            "agg-1",
            new[] { NewEvent("agg-1", "Order", 2) },
            expectedVersion: 0);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("EventStore.ConcurrencyConflict");
    }

    [Fact]
    public async Task StreamEvents_FromZero_ReturnsAllEventsInGlobalOrder()
    {
        // Arrange — interleave events across two streams.
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1) }, 0);
        await _sut.AppendEventsAsync("agg-2", new[] { NewEvent("agg-2", "Order", 1) }, 0);
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 2) }, 1);

        // Act
        var streamed = new List<EventData>();
        await foreach (var ed in _sut.StreamEventsAsync(streamId: null, fromPosition: 0))
        {
            streamed.Add(ed);
        }

        // Assert — three events streamed in append order (global positions 1, 2, 3).
        streamed.Should().HaveCount(3);
        streamed.Select(e => e.GlobalPosition).Should().BeInAscendingOrder();
        streamed[0].StreamId.Should().Be("agg-1");
        streamed[1].StreamId.Should().Be("agg-2");
        streamed[2].StreamId.Should().Be("agg-1");
    }

    [Fact]
    public async Task StreamEvents_FromPosition_IsExclusive()
    {
        // Append events at global positions 1, 2, 3.
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1) }, 0);
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 2) }, 1);
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 3) }, 2);

        var streamed = new List<EventData>();
        await foreach (var ed in _sut.StreamEventsAsync(streamId: null, fromPosition: 2))
        {
            streamed.Add(ed);
        }

        // Exclusive on fromPosition: only events with GlobalPosition > 2 are yielded.
        // This matches the SQL `WHERE global_position > @FromPosition` in
        // PostgreSqlStreamingEventStore — without exclusive semantics,
        // LiveProjectionProcessor would re-process the checkpoint event on every poll.
        streamed.Should().HaveCount(1);
        streamed.Select(e => e.GlobalPosition).Should().BeEquivalentTo(new long[] { 3 });
    }

    [Fact]
    public async Task StreamEvents_WithStreamFilter_OnlyReturnsMatchingStream()
    {
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1) }, 0);
        await _sut.AppendEventsAsync("agg-2", new[] { NewEvent("agg-2", "Customer", 1) }, 0);
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 2) }, 1);

        var streamed = new List<EventData>();
        await foreach (var ed in _sut.StreamEventsAsync(streamId: "agg-1", fromPosition: 0))
        {
            streamed.Add(ed);
        }

        streamed.Should().HaveCount(2);
        streamed.Should().AllSatisfy(e => e.StreamId.Should().Be("agg-1"));
    }

    [Fact]
    public async Task GetMaxGlobalPosition_ReflectsTotalEventsAppended()
    {
        var initial = await _sut.GetMaxGlobalPositionAsync();
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1), NewEvent("agg-1", "Order", 2) }, 0);
        var after = await _sut.GetMaxGlobalPositionAsync();

        initial.Should().Be(0);
        after.Should().Be(2);
    }

    [Fact]
    public async Task GetEventCount_GlobalAndPerStream()
    {
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1), NewEvent("agg-1", "Order", 2) }, 0);
        await _sut.AppendEventsAsync("agg-2", new[] { NewEvent("agg-2", "Customer", 1) }, 0);

        var total = await _sut.GetEventCountAsync();
        var perAgg1 = await _sut.GetEventCountAsync("agg-1");
        var perAgg2 = await _sut.GetEventCountAsync("agg-2");
        var perMissing = await _sut.GetEventCountAsync("agg-missing");

        total.Should().Be(3);
        perAgg1.Should().Be(2);
        perAgg2.Should().Be(1);
        perMissing.Should().Be(0);
    }

    [Fact]
    public async Task GetVersion_ReturnsStreamLength()
    {
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1), NewEvent("agg-1", "Order", 2) }, 0);
        var version = await _sut.GetVersionAsync("agg-1");
        var noStream = await _sut.GetVersionAsync("agg-missing");

        version.Value.Should().Be(2);
        noStream.Value.Should().Be(0);
    }

    [Fact]
    public async Task Exists_TrueWhenStreamHasEvents()
    {
        await _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1) }, 0);

        var existing = await _sut.ExistsAsync("agg-1");
        var missing = await _sut.ExistsAsync("agg-missing");

        existing.Value.Should().BeTrue();
        missing.Value.Should().BeFalse();
    }

    [Fact]
    public void Dispose_IsIdempotent()
    {
        _sut.Dispose();
        var act = () => _sut.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task UseAfterDispose_Throws()
    {
        _sut.Dispose();
        var act = () => _sut.AppendEventsAsync("agg-1", new[] { NewEvent("agg-1", "Order", 1) }, 0);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    private static TestDomainEvent NewEvent(string aggregateId, string aggregateType, long version)
        => new()
        {
            EventId = Guid.NewGuid(),
            AggregateId = aggregateId,
            AggregateType = aggregateType,
            OccurredOn = DateTimeOffset.UtcNow,
            AggregateVersion = version,
            EventVersion = 1,
        };

    private sealed class TestDomainEvent : IDomainEvent
    {
        public required Guid EventId { get; init; }

        public required string AggregateId { get; init; }

        public required string AggregateType { get; init; }

        public required DateTimeOffset OccurredOn { get; init; }

        public required long AggregateVersion { get; init; }

        public required int EventVersion { get; init; }
    }
}
