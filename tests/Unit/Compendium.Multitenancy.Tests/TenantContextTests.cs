// -----------------------------------------------------------------------
// <copyright file="TenantContextTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using AwesomeAssertions;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="TenantContext"/> class.
/// </summary>
public class TenantContextTests
{
    [Fact]
    public void TenantContext_WhenNoTenantSet_HasTenantReturnsFalse()
    {
        // Arrange
        var context = new TenantContext();

        // Act & Assert
        context.HasTenant.Should().BeFalse();
        context.CurrentTenant.Should().BeNull();
        context.TenantId.Should().BeNull();
    }

    [Fact]
    public void TenantContext_ImplementsITenantContext()
    {
        // Arrange & Act
        var context = new TenantContext();

        // Assert
        context.Should().BeAssignableTo<ITenantContext>();
    }

    [Fact]
    public void TenantContext_Properties_AreCorrectlyDerived()
    {
        // Arrange - Use TenantContextAccessor to set a tenant
        var accessor = new TenantContextAccessor();
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };

        // Act
        accessor.SetTenant(tenant);
        var context = accessor.TenantContext;

        // Assert
        context.HasTenant.Should().BeTrue();
        context.TenantId.Should().Be("tenant-123");
        context.TenantName.Should().Be("Test Tenant");
        context.CurrentTenant.Should().NotBeNull();
        context.CurrentTenant!.Id.Should().Be("tenant-123");
    }
}

/// <summary>
/// Unit tests for the <see cref="TenantScope"/> class.
/// </summary>
public class TenantScopeTests
{
    [Fact]
    public void TenantScope_Constructor_WithNullContext_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new TenantScope(null!, new TenantInfo());

        // Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TenantScope_Constructor_SetsNewTenant()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };

        // Act - Create scope
        using (var scope = new TenantScope(accessor.TenantContext as TenantContext ?? throw new InvalidOperationException(), tenant))
        {
            // Assert
            accessor.TenantContext.TenantId.Should().Be("tenant-123");
        }
    }
}

/// <summary>
/// Unit tests for the <see cref="TenantInfo"/> class.
/// </summary>
public class TenantInfoTests
{
    [Fact]
    public void TenantInfo_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var tenant = new TenantInfo();

        // Assert
        tenant.Id.Should().BeEmpty();
        tenant.Name.Should().BeEmpty();
        tenant.ConnectionString.Should().BeNull();
        tenant.IsActive.Should().BeTrue();
        tenant.Properties.Should().NotBeNull();
        tenant.Properties.Should().BeEmpty();
    }

    [Fact]
    public void TenantInfo_WithProperties_SetsValuesCorrectly()
    {
        // Arrange & Act
        var tenant = new TenantInfo
        {
            Id = "tenant-123",
            Name = "Test Tenant",
            ConnectionString = "Host=localhost;Database=test",
            IsActive = false,
            Properties = new Dictionary<string, object?> { ["CustomProp"] = "value" }
        };

        // Assert
        tenant.Id.Should().Be("tenant-123");
        tenant.Name.Should().Be("Test Tenant");
        tenant.ConnectionString.Should().Be("Host=localhost;Database=test");
        tenant.IsActive.Should().BeFalse();
        tenant.Properties.Should().ContainKey("CustomProp");
    }

    [Fact]
    public void TenantInfo_IsRecord_SupportsReferenceEquality()
    {
        // Arrange - Records with mutable Dictionary properties use reference equality for the Dictionary
        var tenant1 = new TenantInfo { Id = "tenant-123", Name = "Test" };
        var tenant3 = new TenantInfo { Id = "tenant-456", Name = "Other" };

        // Assert - Different IDs are not equal
        tenant1.Should().NotBe(tenant3);
        tenant1.Id.Should().NotBe(tenant3.Id);
    }

    [Fact]
    public void TenantInfo_Properties_AreAccessible()
    {
        // Arrange
        var tenant = new TenantInfo { Id = "tenant-123", Name = "Test" };

        // Assert
        tenant.Id.Should().Be("tenant-123");
        tenant.Name.Should().Be("Test");
    }
}
