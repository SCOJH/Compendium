// -----------------------------------------------------------------------
// <copyright file="InMemoryTenantStoreTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Multitenancy.Extensions;
using Compendium.Multitenancy.Stores;
using AwesomeAssertions;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="InMemoryTenantStore"/> class.
/// </summary>
public class InMemoryTenantStoreTests
{
    [Fact]
    public void InMemoryTenantStore_Constructor_WithNoOptions_CreatesEmptyStore()
    {
        // Arrange & Act
        var store = new InMemoryTenantStore();

        // Assert
        store.Count.Should().Be(0);
    }

    [Fact]
    public void InMemoryTenantStore_Constructor_WithInitialTenants_PopulatesStore()
    {
        // Arrange
        var options = new InMemoryTenantStoreOptions
        {
            InitialTenants =
            [
                new TenantInfo { Id = "tenant-1", Name = "Tenant One" },
                new TenantInfo { Id = "tenant-2", Name = "Tenant Two" }
            ]
        };

        // Act
        var store = new InMemoryTenantStore(options);

        // Assert
        store.Count.Should().Be(2);
    }

    [Fact]
    public async Task GetByIdAsync_WithExistingTenant_ReturnsTenant()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.GetByIdAsync("tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("tenant-1");
        result.Value.Name.Should().Be("Tenant One");
    }

    [Fact]
    public async Task GetByIdAsync_WithNonExistingTenant_ReturnsNull()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.GetByIdAsync("non-existent");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_WithNullOrEmptyId_ReturnsNull()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var resultNull = await store.GetByIdAsync(null!);
        var resultEmpty = await store.GetByIdAsync("");
        var resultWhitespace = await store.GetByIdAsync("   ");

        // Assert
        resultNull.Value.Should().BeNull();
        resultEmpty.Value.Should().BeNull();
        resultWhitespace.Value.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdentifierAsync_WithTenantId_ReturnsTenant()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.GetByIdentifierAsync("tenant-1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("tenant-1");
    }

    [Fact]
    public async Task GetByIdentifierAsync_WithTenantName_ReturnsTenant()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.GetByIdentifierAsync("Tenant One");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("tenant-1");
    }

    [Fact]
    public async Task GetByIdentifierAsync_WithMappedIdentifier_ReturnsTenant()
    {
        // Arrange
        var store = CreateStoreWithTenants();
        store.AddIdentifierMapping("custom-identifier", "tenant-2");

        // Act
        var result = await store.GetByIdentifierAsync("custom-identifier");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("tenant-2");
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllTenants()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.GetAllAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_WithNewTenant_AddsTenantToStore()
    {
        // Arrange
        var store = new InMemoryTenantStore();
        var tenant = new TenantInfo { Id = "new-tenant", Name = "New Tenant" };

        // Act
        var saveResult = await store.SaveAsync(tenant);
        var getResult = await store.GetByIdAsync("new-tenant");

        // Assert
        saveResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().NotBeNull();
        getResult.Value!.Name.Should().Be("New Tenant");
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task SaveAsync_WithExistingTenant_UpdatesTenant()
    {
        // Arrange
        var store = CreateStoreWithTenants();
        var updatedTenant = new TenantInfo { Id = "tenant-1", Name = "Updated Tenant One" };

        // Act
        var saveResult = await store.SaveAsync(updatedTenant);
        var getResult = await store.GetByIdAsync("tenant-1");

        // Assert
        saveResult.IsSuccess.Should().BeTrue();
        getResult.Value!.Name.Should().Be("Updated Tenant One");
        store.Count.Should().Be(2); // Count should remain the same
    }

    [Fact]
    public async Task SaveAsync_WithNullTenant_ThrowsArgumentNullException()
    {
        // Arrange
        var store = new InMemoryTenantStore();

        // Act
        var act = () => store.SaveAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SaveAsync_WithEmptyTenantId_ReturnsValidationError()
    {
        // Arrange
        var store = new InMemoryTenantStore();
        var tenant = new TenantInfo { Id = "", Name = "Test Tenant" };

        // Act
        var result = await store.SaveAsync(tenant);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.IdRequired");
    }

    [Fact]
    public async Task DeleteAsync_WithExistingTenant_RemovesTenant()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var deleteResult = await store.DeleteAsync("tenant-1");
        var getResult = await store.GetByIdAsync("tenant-1");

        // Assert
        deleteResult.IsSuccess.Should().BeTrue();
        getResult.Value.Should().BeNull();
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task DeleteAsync_WithNonExistingTenant_ReturnsNotFoundError()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.DeleteAsync("non-existent");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.NotFound");
    }

    [Fact]
    public async Task DeleteAsync_WithEmptyId_ReturnsValidationError()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        var result = await store.DeleteAsync("");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.IdRequired");
    }

    [Fact]
    public async Task DeleteAsync_RemovesIdentifierMappings()
    {
        // Arrange
        var store = CreateStoreWithTenants();
        store.AddIdentifierMapping("custom-id", "tenant-1");

        // Verify mapping works before delete
        var beforeDelete = await store.GetByIdentifierAsync("custom-id");
        beforeDelete.Value.Should().NotBeNull();

        // Act
        await store.DeleteAsync("tenant-1");

        // Assert - mapping should be removed
        var afterDelete = await store.GetByIdentifierAsync("custom-id");
        afterDelete.Value.Should().BeNull();
    }

    [Fact]
    public void AddIdentifierMapping_CreatesMapping()
    {
        // Arrange
        var store = CreateStoreWithTenants();

        // Act
        store.AddIdentifierMapping("subdomain", "tenant-1");

        // Assert - Verify through GetByIdentifierAsync
        var result = store.GetByIdentifierAsync("subdomain").GetAwaiter().GetResult();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("tenant-1");
    }

    [Fact]
    public async Task ConcurrentAccess_ThreadSafe()
    {
        // Arrange
        var store = new InMemoryTenantStore();
        var tasks = new List<Task>();

        // Act - Concurrent writes and reads
        for (int i = 0; i < 100; i++)
        {
            var tenantId = $"tenant-{i}";
            tasks.Add(Task.Run(async () =>
            {
                var tenant = new TenantInfo { Id = tenantId, Name = $"Tenant {i}" };
                await store.SaveAsync(tenant);
                await store.GetByIdAsync(tenantId);
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        store.Count.Should().Be(100);
    }

    private static InMemoryTenantStore CreateStoreWithTenants()
    {
        var options = new InMemoryTenantStoreOptions
        {
            InitialTenants =
            [
                new TenantInfo { Id = "tenant-1", Name = "Tenant One" },
                new TenantInfo { Id = "tenant-2", Name = "Tenant Two" }
            ]
        };

        return new InMemoryTenantStore(options);
    }
}
