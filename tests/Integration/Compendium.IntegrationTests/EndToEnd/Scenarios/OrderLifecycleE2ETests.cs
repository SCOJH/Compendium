// -----------------------------------------------------------------------
// <copyright file="OrderLifecycleE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Domain.Events;
using Compendium.Infrastructure.EventSourcing;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using AwesomeAssertions;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario 1: Complete Order Lifecycle (Happy Path).
/// Tests the full lifecycle of an order from creation through completion,
/// including event sourcing, aggregate reconstitution, and domain logic.
/// </summary>
/// <remarks>
/// Per ADR-0007, this framework-behaviour test runs against
/// <see cref="InMemoryStreamingEventStore"/>. Postgres-specific concurrency
/// and schema tests live in compendium-adapter-postgresql.
/// </remarks>
[Trait("Category", "E2E")]
public sealed class OrderLifecycleE2ETests : IAsyncLifetime
{
    private InMemoryStreamingEventStore? _eventStore;

    public Task InitializeAsync()
    {
        _eventStore = new InMemoryStreamingEventStore();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _eventStore?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CompleteOrderLifecycle_HappyPath_ShouldSucceed()
    {
        // Arrange
        var orderId = OrderId.New();
        var customerId = "customer-001";
        var createdAt = DateTimeOffset.UtcNow;

        // **Step 1: Create Order (PlaceOrder)**
        var order = OrderAggregate.PlaceOrder(orderId, customerId, createdAt);
        order.Status.Should().Be(OrderStatus.Created);
        order.CustomerId.Should().Be(customerId);
        order.CreatedAt.Should().Be(createdAt);

        // Verify domain event was raised
        var domainEvents = order.DomainEvents.ToList();
        domainEvents.Should().HaveCount(1);
        order.ClearDomainEvents();

        // Append OrderPlaced event (version 1)
        var result1 = await _eventStore!.AppendEventsAsync(orderId.ToString(), domainEvents, 0);
        result1.IsSuccess.Should().BeTrue();

        // **Step 2: Add Order Lines (3 lines)**
        var lineResults = new[]
        {
            order.AddOrderLine("line-1", "product-A", 2, 10.00m),
            order.AddOrderLine("line-2", "product-B", 1, 25.00m),
            order.AddOrderLine("line-3", "product-C", 5, 5.00m)
        };

        lineResults.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        order.OrderLines.Should().HaveCount(3);
        order.TotalAmount.Should().Be(70.00m); // (2*10) + (1*25) + (5*5) = 70

        // Append OrderLineAdded events (versions 2, 3, 4)
        var line1Events = order.DomainEvents.ToList();
        line1Events.Should().HaveCount(3);
        order.ClearDomainEvents();

        var result2 = await _eventStore.AppendEventsAsync(orderId.ToString(), line1Events, 1);
        result2.IsSuccess.Should().BeTrue();

        // **Step 3: Complete Order**
        var completedAt = DateTimeOffset.UtcNow;
        var completeResult = order.Complete(completedAt);
        completeResult.IsSuccess.Should().BeTrue();

        order.Status.Should().Be(OrderStatus.Completed);
        order.CompletedAt.Should().Be(completedAt);

        // Append OrderCompleted event (version 5)
        var completedEvents = order.DomainEvents.ToList();
        completedEvents.Should().HaveCount(1);
        order.ClearDomainEvents();

        var result3 = await _eventStore.AppendEventsAsync(orderId.ToString(), completedEvents, 4);
        result3.IsSuccess.Should().BeTrue();

        // **Step 4: Reconstitute Aggregate from Events**
        var eventsResult = await _eventStore.GetEventsAsync(orderId.ToString());
        eventsResult.IsSuccess.Should().BeTrue();
        eventsResult.Value.Should().HaveCount(5);

        var reconstitutedOrder = OrderAggregate.FromEvents(orderId, eventsResult.Value);

        // **Step 5: Verify Reconstituted State**
        reconstitutedOrder.Id.Should().Be(orderId);
        reconstitutedOrder.CustomerId.Should().Be(customerId);
        reconstitutedOrder.Status.Should().Be(OrderStatus.Completed);
        reconstitutedOrder.OrderLines.Should().HaveCount(3);
        reconstitutedOrder.TotalAmount.Should().Be(70.00m);
        reconstitutedOrder.CreatedAt.Should().Be(createdAt);
        reconstitutedOrder.CompletedAt.Should().Be(completedAt);
        reconstitutedOrder.Version.Should().Be(5);

        // **Expected Results:**
        // ✅ All 5 events appended successfully
        // ✅ Aggregate reconstituted correctly from events
        // ✅ All business rules enforced
        // ✅ No concurrency conflicts
    }

    [Fact]
    public void AddLineToCompletedOrder_ShouldFail()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-002", DateTimeOffset.UtcNow);

        // Add a line and complete the order
        order.AddOrderLine("line-1", "product-A", 1, 10.00m);
        order.Complete(DateTimeOffset.UtcNow);

        // Act - Try to add line to completed order
        var result = order.AddOrderLine("line-2", "product-B", 1, 5.00m);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Order.AddLine.Completed");
    }

    [Fact]
    public void CompleteEmptyOrder_ShouldFail()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-003", DateTimeOffset.UtcNow);

        // Act - Try to complete order with no lines
        var result = order.Complete(DateTimeOffset.UtcNow);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Order.Complete.Empty");
    }

    [Fact]
    public void AddLineWithInvalidQuantity_ShouldFail()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-004", DateTimeOffset.UtcNow);

        // Act - Try to add line with zero quantity
        var result = order.AddOrderLine("line-1", "product-A", 0, 10.00m);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Order.AddLine.InvalidQuantity");
    }

    [Fact]
    public void AddLineWithNegativePrice_ShouldFail()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-005", DateTimeOffset.UtcNow);

        // Act - Try to add line with negative price
        var result = order.AddOrderLine("line-1", "product-A", 1, -10.00m);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Order.AddLine.InvalidPrice");
    }

    [Fact]
    public async Task LargeOrderWithManyLines_ShouldAppendAllEvents()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-006", DateTimeOffset.UtcNow);

        var initialEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();

        var result = await _eventStore!.AppendEventsAsync(orderId.ToString(), initialEvents, 0);
        result.IsSuccess.Should().BeTrue();

        // Act — add 100 order lines.
        var allLineEvents = new List<IDomainEvent>();
        for (int i = 1; i <= 100; i++)
        {
            var lineResult = order.AddOrderLine($"line-{i}", $"product-{i}", 1, 10.00m);
            lineResult.IsSuccess.Should().BeTrue();

            var lineEvents = order.DomainEvents.ToList();
            allLineEvents.AddRange(lineEvents);
            order.ClearDomainEvents();
        }

        var appendResult = await _eventStore.AppendEventsAsync(orderId.ToString(), allLineEvents, 1);
        appendResult.IsSuccess.Should().BeTrue();

        // Assert — verify functional correctness (throughput is measured in PerfTests).
        order.OrderLines.Should().HaveCount(100);
        var storedEvents = await _eventStore.GetEventsAsync(orderId.ToString());
        storedEvents.Value.Should().HaveCount(101); // OrderPlaced + 100 OrderLineAdded
    }
}
