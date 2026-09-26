// -----------------------------------------------------------------------
// <copyright file="MultiTenancyE2ETests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Infrastructure.EventSourcing;
using Compendium.IntegrationTests.EndToEnd.TestAggregates;
using Compendium.IntegrationTests.EndToEnd.TestAggregates.ValueObjects;
using Compendium.Multitenancy;
using AwesomeAssertions;
using Xunit;

namespace Compendium.IntegrationTests.EndToEnd.Scenarios;

/// <summary>
/// E2E Test Scenario 3: Multi-Tenancy Isolation.
/// Tests complete data isolation between tenants at all layers (event store, projections, queries).
/// </summary>
/// <remarks>
/// Per ADR-0007, this framework-behaviour test runs against
/// <see cref="InMemoryStreamingEventStore"/>. The tenant-aware stream key
/// is constructed in the event store itself; this test verifies that
/// behaviour identically against InMemory.
/// </remarks>
[Trait("Category", "E2E")]
[Trait("Category", "MultiTenancy")]
public sealed class MultiTenancyE2ETests : IAsyncLifetime
{
    private InMemoryStreamingEventStore? _eventStore;
    private TenantContext? _tenantContext;

    public Task InitializeAsync()
    {
        _tenantContext = new TenantContext();
        _eventStore = new InMemoryStreamingEventStore(_tenantContext);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _eventStore?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task TenantIsolation_OrdersCreatedForDifferentTenants_ShouldBeCompletelyIsolated()
    {
        // Arrange
        var tenantA = new TenantInfo
        {
            Id = "tenant-a",
            Name = "Tenant A Corporation",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var tenantB = new TenantInfo
        {
            Id = "tenant-b",
            Name = "Tenant B Enterprises",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        var orderIdA = OrderId.New();
        var orderIdB = OrderId.New();

        // **Step 1: Tenant A - Create Order**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            _tenantContext!.TenantId.Should().Be("tenant-a");

            var orderA = OrderAggregate.PlaceOrder(orderIdA, "customer-a-001", DateTimeOffset.UtcNow);
            orderA.AddOrderLine("line-a-1", "product-A", 2, 25.00m);

            var eventsA = orderA.DomainEvents.ToList();
            orderA.ClearDomainEvents();

            var resultA = await _eventStore!.AppendEventsAsync(orderIdA.ToString(), eventsA, 0);
            resultA.IsSuccess.Should().BeTrue();
        }

        // **Step 2: Tenant B - Create Order**
        using (new TenantScope(_tenantContext!, tenantB))
        {
            _tenantContext!.TenantId.Should().Be("tenant-b");

            var orderB = OrderAggregate.PlaceOrder(orderIdB, "customer-b-001", DateTimeOffset.UtcNow);
            orderB.AddOrderLine("line-b-1", "product-B", 3, 30.00m);

            var eventsB = orderB.DomainEvents.ToList();
            orderB.ClearDomainEvents();

            var resultB = await _eventStore!.AppendEventsAsync(orderIdB.ToString(), eventsB, 0);
            resultB.IsSuccess.Should().BeTrue();
        }

        // **Step 3: Tenant A - Query Orders (Should ONLY see Tenant A events)**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var orderAEvents = await _eventStore!.GetEventsAsync(orderIdA.ToString());
            orderAEvents.IsSuccess.Should().BeTrue();
            orderAEvents.Value.Should().HaveCount(2); // OrderPlaced + OrderLineAdded

            // Verify Tenant A CANNOT see Tenant B events
            var orderBEvents = await _eventStore.GetEventsAsync(orderIdB.ToString());
            orderBEvents.IsSuccess.Should().BeTrue();
            orderBEvents.Value.Should().BeEmpty("Tenant A should not see Tenant B events");
        }

        // **Step 4: Tenant B - Query Orders (Should ONLY see Tenant B events)**
        using (new TenantScope(_tenantContext!, tenantB))
        {
            var orderBEvents = await _eventStore!.GetEventsAsync(orderIdB.ToString());
            orderBEvents.IsSuccess.Should().BeTrue();
            orderBEvents.Value.Should().HaveCount(2); // OrderPlaced + OrderLineAdded

            // Verify Tenant B CANNOT see Tenant A events
            var orderAEvents = await _eventStore.GetEventsAsync(orderIdA.ToString());
            orderAEvents.IsSuccess.Should().BeTrue();
            orderAEvents.Value.Should().BeEmpty("Tenant B should not see Tenant A events");
        }

        // **Expected Results:**
        // ✅ Events stored with correct tenant_id
        // ✅ Queries return ONLY tenant-specific data
        // ✅ Cross-tenant access prevented
    }

    [Fact]
    public async Task TenantIsolation_StreamExistsCheck_ShouldRespectTenantBoundaries()
    {
        // Arrange
        var tenantA = new TenantInfo { Id = "tenant-a", Name = "Tenant A", IsActive = true, CreatedAt = DateTime.UtcNow };
        var tenantB = new TenantInfo { Id = "tenant-b", Name = "Tenant B", IsActive = true, CreatedAt = DateTime.UtcNow };
        var orderId = OrderId.New();

        // **Step 1: Tenant A creates order**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var order = OrderAggregate.PlaceOrder(orderId, "customer-a-002", DateTimeOffset.UtcNow);
            var events = order.DomainEvents.ToList();
            await _eventStore!.AppendEventsAsync(orderId.ToString(), events, 0);
        }

        // **Step 2: Tenant A checks stream existence (should exist)**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var existsResult = await _eventStore!.ExistsAsync(orderId.ToString());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeTrue("Stream should exist for Tenant A");
        }

        // **Step 3: Tenant B checks same stream (should NOT exist)**
        using (new TenantScope(_tenantContext!, tenantB))
        {
            var existsResult = await _eventStore!.ExistsAsync(orderId.ToString());
            existsResult.IsSuccess.Should().BeTrue();
            existsResult.Value.Should().BeFalse("Stream should not exist for Tenant B due to tenant isolation");
        }

        // **Expected Results:**
        // ✅ Stream exists check respects tenant boundaries
        // ✅ Tenant B cannot see Tenant A's stream
    }

    [Fact]
    public async Task TenantIsolation_GetCurrentVersion_ShouldReturnZeroForOtherTenants()
    {
        // Arrange
        var tenantA = new TenantInfo { Id = "tenant-a", Name = "Tenant A", IsActive = true, CreatedAt = DateTime.UtcNow };
        var tenantB = new TenantInfo { Id = "tenant-b", Name = "Tenant B", IsActive = true, CreatedAt = DateTime.UtcNow };
        var orderId = OrderId.New();

        // **Step 1: Tenant A creates order with 3 events**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var order = OrderAggregate.PlaceOrder(orderId, "customer-a-003", DateTimeOffset.UtcNow);
            order.AddOrderLine("line-1", "product-A", 1, 10.00m);
            order.AddOrderLine("line-2", "product-B", 2, 20.00m);

            var events = order.DomainEvents.ToList();
            await _eventStore!.AppendEventsAsync(orderId.ToString(), events, 0);

            // Verify version for Tenant A
            var versionResult = await _eventStore.GetVersionAsync(orderId.ToString());
            versionResult.IsSuccess.Should().BeTrue();
            versionResult.Value.Should().Be(3);
        }

        // **Step 2: Tenant B checks version (should be 0 - stream doesn't exist for them)**
        using (new TenantScope(_tenantContext!, tenantB))
        {
            var versionResult = await _eventStore!.GetVersionAsync(orderId.ToString());
            versionResult.IsSuccess.Should().BeTrue();
            versionResult.Value.Should().Be(0, "Tenant B should not see Tenant A's stream version");
        }

        // **Expected Results:**
        // ✅ Version queries respect tenant boundaries
        // ✅ Tenant B sees version 0 for Tenant A's stream
    }

    [Fact]
    public async Task TenantIsolation_GetStatistics_ShouldOnlyReturnTenantData()
    {
        // Arrange
        var tenantA = new TenantInfo { Id = "tenant-a", Name = "Tenant A", IsActive = true, CreatedAt = DateTime.UtcNow };
        var tenantB = new TenantInfo { Id = "tenant-b", Name = "Tenant B", IsActive = true, CreatedAt = DateTime.UtcNow };

        // **Step 1: Create 2 orders for Tenant A**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var order1 = OrderAggregate.PlaceOrder(OrderId.New(), "customer-a-004", DateTimeOffset.UtcNow);
            await _eventStore!.AppendEventsAsync(order1.Id.ToString(), order1.DomainEvents.ToList(), 0);

            var order2 = OrderAggregate.PlaceOrder(OrderId.New(), "customer-a-005", DateTimeOffset.UtcNow);
            await _eventStore.AppendEventsAsync(order2.Id.ToString(), order2.DomainEvents.ToList(), 0);
        }

        // **Step 2: Create 1 order for Tenant B**
        using (new TenantScope(_tenantContext!, tenantB))
        {
            var order3 = OrderAggregate.PlaceOrder(OrderId.New(), "customer-b-002", DateTimeOffset.UtcNow);
            await _eventStore!.AppendEventsAsync(order3.Id.ToString(), order3.DomainEvents.ToList(), 0);
        }

        // **Step 3: Tenant A gets statistics (should only see 2 aggregates)**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var statsResult = await _eventStore!.GetStatisticsAsync();
            statsResult.IsSuccess.Should().BeTrue();
            statsResult.Value.TotalAggregates.Should().Be(2, "Tenant A should only see their 2 aggregates");
            statsResult.Value.TotalEvents.Should().Be(2, "Tenant A should only see their 2 events");
        }

        // **Step 4: Tenant B gets statistics (should only see 1 aggregate)**
        using (new TenantScope(_tenantContext!, tenantB))
        {
            var statsResult = await _eventStore!.GetStatisticsAsync();
            statsResult.IsSuccess.Should().BeTrue();
            statsResult.Value.TotalAggregates.Should().Be(1, "Tenant B should only see their 1 aggregate");
            statsResult.Value.TotalEvents.Should().Be(1, "Tenant B should only see their 1 event");
        }

        // **Expected Results:**
        // ✅ Statistics queries respect tenant boundaries
        // ✅ Each tenant only sees their own data
    }

    [Fact]
    public async Task TenantIsolation_OptimisticConcurrency_ShouldWorkWithinTenantContext()
    {
        // Arrange
        var tenantA = new TenantInfo { Id = "tenant-a", Name = "Tenant A", IsActive = true, CreatedAt = DateTime.UtcNow };
        var orderId = OrderId.New();

        // **Step 1: Create order for Tenant A**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var order = OrderAggregate.PlaceOrder(orderId, "customer-a-006", DateTimeOffset.UtcNow);
            var events = order.DomainEvents.ToList();
            await _eventStore!.AppendEventsAsync(orderId.ToString(), events, 0);
        }

        // **Step 2: Tenant A tries to append with correct version (should succeed)**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var order = OrderAggregate.PlaceOrder(orderId, "customer-a-006", DateTimeOffset.UtcNow);
            order.AddOrderLine("line-1", "product-A", 1, 10.00m);
            var newEvents = new[] { order.DomainEvents.Last() };

            var result = await _eventStore!.AppendEventsAsync(orderId.ToString(), newEvents, 1);
            result.IsSuccess.Should().BeTrue();
        }

        // **Step 3: Tenant A tries to append with wrong version (should fail)**
        using (new TenantScope(_tenantContext!, tenantA))
        {
            var order = OrderAggregate.PlaceOrder(orderId, "customer-a-006", DateTimeOffset.UtcNow);
            order.AddOrderLine("line-2", "product-B", 2, 20.00m);
            var newEvents = new[] { order.DomainEvents.Last() };

            var result = await _eventStore!.AppendEventsAsync(orderId.ToString(), newEvents, 1); // Expected version 1, but current is 2
            result.IsSuccess.Should().BeFalse();
            result.Error.Type.Should().Be(Core.Results.ErrorType.Conflict);
        }

        // **Expected Results:**
        // ✅ Optimistic concurrency works within tenant context
        // ✅ Version conflicts detected correctly
    }

    [Fact]
    public async Task TenantIsolation_WithoutTenantContext_ShouldStillWork()
    {
        // Arrange — create a separate event store WITHOUT tenant context.
        using var noTenantEventStore = new InMemoryStreamingEventStore(tenantContext: null);

        // **Step 1: Create order without tenant context**
        var orderId = OrderId.New();
        var order = OrderAggregate.PlaceOrder(orderId, "customer-global-001", DateTimeOffset.UtcNow);
        var events = order.DomainEvents.ToList();

        var appendResult = await noTenantEventStore.AppendEventsAsync(orderId.ToString(), events, 0);
        appendResult.IsSuccess.Should().BeTrue();

        // **Step 2: Retrieve events without tenant context**
        var getResult = await noTenantEventStore.GetEventsAsync(orderId.ToString());
        getResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().HaveCount(1);

        // **Step 3: Verify statistics work**
        var statsResult = await noTenantEventStore.GetStatisticsAsync();
        statsResult.IsSuccess.Should().BeTrue();
        statsResult.Value.TotalAggregates.Should().BeGreaterThan(0);

        // **Expected Results:**
        // ✅ Event store works without tenant context (multi-tenancy is optional)
        // ✅ All operations succeed
    }
}
