// -----------------------------------------------------------------------
// <copyright file="TenantContextAccessorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using AwesomeAssertions;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="TenantContextAccessor"/> class.
/// </summary>
public class TenantContextAccessorTests
{
    [Fact]
    public void TenantContextAccessor_TenantContext_ReturnsNonNullContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor();

        // Act
        var context = accessor.TenantContext;

        // Assert
        context.Should().NotBeNull();
    }

    [Fact]
    public void TenantContextAccessor_SetTenant_UpdatesTenantContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };

        // Act
        accessor.SetTenant(tenant);

        // Assert
        accessor.TenantContext.HasTenant.Should().BeTrue();
        accessor.TenantContext.CurrentTenant!.Id.Should().Be("tenant-123");
    }

    [Fact]
    public void TenantContextAccessor_ClearTenant_RemovesTenantFromContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };
        accessor.SetTenant(tenant);

        // Act
        accessor.ClearTenant();

        // Assert
        accessor.TenantContext.HasTenant.Should().BeFalse();
        accessor.TenantContext.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void TenantContextAccessor_ImplementsITenantContextAccessor()
    {
        // Arrange
        var accessor = new TenantContextAccessor();

        // Assert
        accessor.Should().BeAssignableTo<ITenantContextAccessor>();
    }

    [Fact]
    public void TenantContextAccessor_ImplementsITenantContextSetter()
    {
        // Arrange
        var accessor = new TenantContextAccessor();

        // Assert
        accessor.Should().BeAssignableTo<ITenantContextSetter>();
    }

    [Fact]
    public void TenantContextAccessor_SameInstance_SharesContext()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };

        // Act
        accessor.SetTenant(tenant);

        // Assert - Same accessor sees its own tenant
        accessor.TenantContext.TenantId.Should().Be("tenant-123");

        // Clear and verify
        accessor.ClearTenant();
        accessor.TenantContext.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task TenantContextAccessor_FlowsAcrossAsyncCalls()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };
        accessor.SetTenant(tenant);

        // Act
        var tenantIdFromAsync = await Task.Run(() => accessor.TenantContext.TenantId);

        // Assert
        tenantIdFromAsync.Should().Be("tenant-123");
    }

    [Fact]
    public async Task TenantContextAccessor_ParallelTasks_HaveIsolatedContexts()
    {
        // Arrange
        var accessor = new TenantContextAccessor();

        // Act - Run parallel tasks that set different tenants
        var results = new List<string?>();
        var tasks = Enumerable.Range(1, 10).Select(async i =>
        {
            // Each task sets its own tenant
            var localAccessor = new TenantContextAccessor();
            localAccessor.SetTenant(new TenantInfo { Id = $"tenant-{i}", Name = $"Tenant {i}" });

            await Task.Delay(10); // Simulate some async work

            return localAccessor.TenantContext.TenantId;
        });

        var taskResults = await Task.WhenAll(tasks);

        // Assert - Each task should have its own tenant ID
        taskResults.Should().HaveCount(10);
        taskResults.Should().OnlyHaveUniqueItems();
    }
}
