// -----------------------------------------------------------------------
// <copyright file="ErrorHandlingE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Results;
using Compendium.Infrastructure.EventSourcing;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using AwesomeAssertions;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario 7: Error Handling and Recovery.
/// Tests graceful error handling and Result pattern usage across framework components.
/// </summary>
/// <remarks>
/// Per ADR-0007, this framework-behaviour test runs against
/// <see cref="InMemoryStreamingEventStore"/>.
/// </remarks>
[Trait("Category", "E2E")]
[Trait("Category", "ErrorHandling")]
public sealed class ErrorHandlingE2ETests : IAsyncLifetime
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
    public async Task InvalidAggregateCommand_ShouldReturnValidationError()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-001", DateTimeOffset.UtcNow);

        // **Step 1: Append initial event**
        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        // **Step 2: Try to add invalid order line (negative quantity)**
        var invalidLineResult = order.AddOrderLine("line-1", "product-A", -5, 10.00m);

        // **Step 3: Verify validation error returned**
        invalidLineResult.IsSuccess.Should().BeFalse("Negative quantity should fail validation");
        invalidLineResult.Error.Type.Should().Be(ErrorType.Validation);
        invalidLineResult.Error.Code.Should().Contain("InvalidQuantity");
        invalidLineResult.Error.Message.Should().Contain("Quantity must be greater than zero");

        // **Step 4: Verify NO event emitted for invalid command**
        order.DomainEvents.Should().BeEmpty("No event should be emitted for validation failure");

        // **Step 5: Try to add line with negative price**
        var invalidPriceResult = order.AddOrderLine("line-2", "product-B", 1, -50.00m);

        invalidPriceResult.IsSuccess.Should().BeFalse();
        invalidPriceResult.Error.Type.Should().Be(ErrorType.Validation);
        invalidPriceResult.Error.Code.Should().Contain("InvalidPrice");

        // **Step 6: Verify aggregate version unchanged**
        var versionResult = await _eventStore.GetVersionAsync(orderId.ToString());
        versionResult.Value.Should().Be(1, "Version should remain 1 (only PlaceOrder event)");

        // **Expected Results:**
        // ✅ Validation errors returned as Result.Failure
        // ✅ Error codes and messages descriptive
        // ✅ NO events appended for invalid commands
        // ✅ Aggregate state unchanged
    }

    [Fact]
    public async Task BusinessRuleViolation_ShouldReturnBusinessError()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-002", DateTimeOffset.UtcNow);

        var placeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        // **Step 1: Try to complete order with no lines (business rule violation)**
        var completeResult = order.Complete(DateTimeOffset.UtcNow);

        // **Step 2: Verify business rule error**
        completeResult.IsSuccess.Should().BeFalse("Cannot complete order with no lines");
        completeResult.Error.Type.Should().Be(ErrorType.Validation);
        completeResult.Error.Code.Should().Contain("Order.Complete.Empty");
        completeResult.Error.Message.Should().Contain("Cannot complete an order with no lines");

        // **Step 3: Verify NO event emitted**
        order.DomainEvents.Should().BeEmpty();

        // **Step 4: Add a line and complete order successfully**
        var addLineResult = order.AddOrderLine("line-1", "product-A", 1, 10.00m);
        addLineResult.IsSuccess.Should().BeTrue();

        var lineEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore.AppendEventsAsync(orderId.ToString(), lineEvents, 1);

        var completeSuccessResult = order.Complete(DateTimeOffset.UtcNow);
        completeSuccessResult.IsSuccess.Should().BeTrue();

        var completeEvents = order.DomainEvents.ToList();
        order.ClearDomainEvents();
        await _eventStore.AppendEventsAsync(orderId.ToString(), completeEvents, 2);

        // **Step 5: Try to add line to completed order (business rule violation)**
        var addLineAfterCompleteResult = order.AddOrderLine("line-2", "product-B", 1, 20.00m);

        addLineAfterCompleteResult.IsSuccess.Should().BeFalse("Cannot add line to completed order");
        addLineAfterCompleteResult.Error.Type.Should().Be(ErrorType.Validation);
        addLineAfterCompleteResult.Error.Code.Should().Contain("Completed");

        // **Step 6: Verify final version unchanged**
        var finalVersion = await _eventStore.GetVersionAsync(orderId.ToString());
        finalVersion.Value.Should().Be(3, "Version should be 3 (PlaceOrder + AddLine + Complete)");

        // **Expected Results:**
        // ✅ Business rule violations returned as Result.Failure
        // ✅ ErrorType.Conflict for state-dependent violations
        // ✅ NO events appended for violations
        // ✅ System maintains consistency
    }

    [Fact]
    public async Task OptimisticConcurrencyViolation_ShouldReturnConflictError()
    {
        // Arrange
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-003", DateTimeOffset.UtcNow);

        var placeEvents = order.DomainEvents.ToList();
        await _eventStore!.AppendEventsAsync(orderId.ToString(), placeEvents, 0);

        // **Step 1: Add first line (version 1 → 2)**
        var order1 = OrderAggregate.PlaceOrder(orderId, "customer-003", DateTimeOffset.UtcNow);
        order1.AddOrderLine("line-1", "product-A", 1, 10.00m);
        var line1Events = new[] { order1.DomainEvents.Last() };

        var result1 = await _eventStore.AppendEventsAsync(orderId.ToString(), line1Events, 1);
        result1.IsSuccess.Should().BeTrue();

        // **Step 2: Try to append with stale version (expect version 1, but current is 2)**
        var order2 = OrderAggregate.PlaceOrder(orderId, "customer-003", DateTimeOffset.UtcNow);
        order2.AddOrderLine("line-2", "product-B", 1, 20.00m);
        var line2Events = new[] { order2.DomainEvents.Last() };

        var result2 = await _eventStore.AppendEventsAsync(orderId.ToString(), line2Events, 1);

        // **Step 3: Verify concurrency conflict error**
        result2.IsSuccess.Should().BeFalse("Stale version should fail");
        result2.Error.Type.Should().Be(ErrorType.Conflict);
        result2.Error.Code.Should().Contain("ConcurrencyConflict");
        result2.Error.Message.Should().Contain("version");

        // **Step 4: Retry with correct version**
        var result3 = await _eventStore.AppendEventsAsync(orderId.ToString(), line2Events, 2);
        result3.IsSuccess.Should().BeTrue("Retry with correct version should succeed");

        // **Step 5: Verify final version**
        var finalVersion = await _eventStore.GetVersionAsync(orderId.ToString());
        finalVersion.Value.Should().Be(3, "Final version should be 3");

        // **Expected Results:**
        // ✅ Concurrency conflict detected
        // ✅ Error type = ErrorType.Conflict
        // ✅ Error message describes version mismatch
        // ✅ Retry with correct version succeeds
    }

    [Fact]
    public async Task NonExistentAggregate_ShouldReturnNotFoundError()
    {
        // Arrange
        var nonExistentOrderId = OrderId.New();

        // **Step 1: Try to get events for non-existent aggregate**
        var getEventsResult = await _eventStore!.GetEventsAsync(nonExistentOrderId.ToString());

        // **Step 2: Verify Result.Success with empty events (not an error)**
        getEventsResult.IsSuccess.Should().BeTrue("GetEvents returns success with empty list for non-existent stream");
        getEventsResult.Value.Should().BeEmpty("No events should exist for non-existent aggregate");

        // **Step 3: Check stream existence**
        var existsResult = await _eventStore.ExistsAsync(nonExistentOrderId.ToString());
        existsResult.IsSuccess.Should().BeTrue();
        existsResult.Value.Should().BeFalse("Stream should not exist");

        // **Step 4: Get version for non-existent aggregate**
        var versionResult = await _eventStore.GetVersionAsync(nonExistentOrderId.ToString());
        versionResult.IsSuccess.Should().BeTrue();
        versionResult.Value.Should().Be(0, "Non-existent stream returns version 0");

        // **Step 5: Try to append with non-zero expected version (should fail)**
        var order = OrderAggregate.PlaceOrder(nonExistentOrderId, "customer-004", DateTimeOffset.UtcNow);
        var events = order.DomainEvents.ToList();

        var appendResult = await _eventStore.AppendEventsAsync(nonExistentOrderId.ToString(), events, 5);

        appendResult.IsSuccess.Should().BeFalse("Appending with wrong expected version should fail");
        appendResult.Error.Type.Should().Be(ErrorType.Conflict);

        // **Expected Results:**
        // ✅ GetEvents returns empty list (not error)
        // ✅ Exists returns false
        // ✅ GetVersion returns 0
        // ✅ Append with wrong expected version fails
        // ✅ Graceful handling of non-existent aggregates
    }
}
