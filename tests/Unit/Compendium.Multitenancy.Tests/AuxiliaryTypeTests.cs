// -----------------------------------------------------------------------
// <copyright file="AuxiliaryTypeTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using AwesomeAssertions;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Top-up tests for small auxiliary types that hold uncovered getters / defaults.
/// </summary>
public class AuxiliaryTypeTests
{
    [Fact]
    public void TenantInfo_CreatedAt_RoundtripsValue()
    {
        // Arrange
        var createdAt = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc);

        // Act
        var tenant = new TenantInfo { Id = "t-1", CreatedAt = createdAt };

        // Assert
        tenant.CreatedAt.Should().Be(createdAt);
    }

    [Fact]
    public void TenantInfo_UpdatedAt_RoundtripsValue()
    {
        // Arrange
        var updatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var tenant = new TenantInfo { Id = "t-1", UpdatedAt = updatedAt };

        // Assert
        tenant.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void TenantInfo_UpdatedAt_DefaultsToNull()
    {
        // Arrange & Act
        var tenant = new TenantInfo { Id = "t-1" };

        // Assert
        tenant.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void TenantResolutionContext_Host_RoundtripsValue()
    {
        // Arrange & Act
        var context = new TenantResolutionContext { Host = "tenant.example.com" };

        // Assert
        context.Host.Should().Be("tenant.example.com");
    }

    [Fact]
    public void TenantResolutionContext_Path_RoundtripsValue()
    {
        // Arrange & Act
        var context = new TenantResolutionContext { Path = "/api/v1/orders" };

        // Assert
        context.Path.Should().Be("/api/v1/orders");
    }

    [Fact]
    public void TenantResolutionContext_Defaults_AreNotNull()
    {
        // Arrange & Act
        var context = new TenantResolutionContext();

        // Assert
        context.Headers.Should().NotBeNull().And.BeEmpty();
        context.QueryParameters.Should().NotBeNull().And.BeEmpty();
        context.Properties.Should().NotBeNull().And.BeEmpty();
        context.Host.Should().BeNull();
        context.Path.Should().BeNull();
    }

    [Fact]
    public void TenantConsistencyOptions_AllowAnonymous_RoundtripsValue()
    {
        // Arrange & Act
        var options = new TenantConsistencyOptions { AllowAnonymous = true };

        // Assert
        options.AllowAnonymous.Should().BeTrue();
    }

    [Fact]
    public void TenantConsistencyOptions_AllowAnonymous_DefaultIsFalse()
    {
        // Arrange & Act
        var options = new TenantConsistencyOptions();

        // Assert
        options.AllowAnonymous.Should().BeFalse();
    }

    [Fact]
    public void TenantConsistencyOptions_ExcludedPaths_DefaultsContainCommonHealthChecks()
    {
        // Arrange & Act
        var options = new TenantConsistencyOptions();

        // Assert
        options.ExcludedPaths.Should().Contain("/health");
        options.ExcludedPaths.Should().Contain("/healthz");
        options.ExcludedPaths.Should().Contain("/metrics");
        options.ExcludedPaths.Should().Contain("/.well-known");
    }

    [Fact]
    public void TenantSourceIdentifiers_GetAllIds_FiltersWhitespaceAndNull()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-1",
            SubdomainTenantId = "   ",
            JwtTenantId = null
        };

        // Act
        var ids = sources.GetAllIds().ToList();

        // Assert
        ids.Should().ContainSingle().Which.Should().Be("tenant-1");
        sources.SourceCount.Should().Be(1);
    }

    [Fact]
    public void TenantSourceIdentifiers_AreConsistent_WithSingleSource_ReturnsTrue()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers { HeaderTenantId = "tenant-1" };

        // Act & Assert
        sources.AreConsistent.Should().BeTrue();
        sources.ResolvedTenantId.Should().Be("tenant-1");
    }

    [Fact]
    public void TenantSourceIdentifiers_AreConsistent_WhenAllMatchCaseInsensitively_ReturnsTrue()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "Tenant-1",
            SubdomainTenantId = "tenant-1",
            JwtTenantId = "TENANT-1"
        };

        // Act & Assert
        sources.AreConsistent.Should().BeTrue();
    }

    [Fact]
    public void TenantSourceIdentifiers_AreConsistent_WhenMismatched_ReturnsFalse()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-1",
            SubdomainTenantId = "tenant-2"
        };

        // Act & Assert
        sources.AreConsistent.Should().BeFalse();
        sources.ResolvedTenantId.Should().BeNull();
    }

    [Fact]
    public void TenantSourceIdentifiers_NoSources_AreConsistentReturnsTrue()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers();

        // Act & Assert
        sources.AreConsistent.Should().BeTrue();
        sources.SourceCount.Should().Be(0);
        sources.ResolvedTenantId.Should().BeNull();
    }

    [Fact]
    public void TenantErrors_NoTenantIdentifier_HasExpectedCode()
    {
        // Arrange & Act
        var error = TenantErrors.NoTenantIdentifier();

        // Assert
        error.Code.Should().Be("Tenant.NoIdentifier");
    }

    [Fact]
    public void TenantErrors_InsufficientSources_FormatsMessage()
    {
        // Arrange & Act
        var error = TenantErrors.InsufficientSources(2, 1);

        // Assert
        error.Code.Should().Be("Tenant.InsufficientSources");
        error.Message.Should().Contain("2");
        error.Message.Should().Contain("1");
    }

    [Fact]
    public void TenantErrors_TenantMismatch_IncludesAllSources()
    {
        // Arrange & Act
        var error = TenantErrors.TenantMismatch("hdr", "sub", "jwt");

        // Assert
        error.Code.Should().Be("Tenant.Mismatch");
        error.Message.Should().Contain("hdr").And.Contain("sub").And.Contain("jwt");
    }

    [Fact]
    public void TenantErrors_TenantMismatch_WithNullSources_RendersAsNone()
    {
        // Arrange & Act
        var error = TenantErrors.TenantMismatch(null, null, null);

        // Assert
        error.Message.Should().Contain("none");
    }

    [Fact]
    public void TenantErrors_TenantNotFound_HasExpectedCode()
    {
        // Arrange & Act
        var error = TenantErrors.TenantNotFound("tenant-x");

        // Assert
        error.Code.Should().Be("Tenant.NotFound");
        error.Message.Should().Contain("tenant-x");
    }

    [Fact]
    public void TenantErrors_TenantAccessDenied_HasExpectedCode()
    {
        // Arrange & Act
        var error = TenantErrors.TenantAccessDenied("tenant-x");

        // Assert
        error.Code.Should().Be("Tenant.AccessDenied");
        error.Message.Should().Contain("tenant-x");
    }

    [Fact]
    public void TenantContextAccessor_ClearTenant_RemovesTenant()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        accessor.SetTenant(new TenantInfo { Id = "t-1" });

        // Act
        accessor.ClearTenant();

        // Assert
        accessor.TenantContext.HasTenant.Should().BeFalse();
        accessor.TenantContext.CurrentTenant.Should().BeNull();
    }

    [Fact]
    public void TenantScope_Dispose_RestoresPreviousTenant()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        accessor.SetTenant(new TenantInfo { Id = "outer" });
        var ctx = (TenantContext)accessor.TenantContext;

        // Act
        using (new TenantScope(ctx, new TenantInfo { Id = "inner" }))
        {
            accessor.TenantContext.TenantId.Should().Be("inner");
        }

        // Assert
        accessor.TenantContext.TenantId.Should().Be("outer");
    }

    [Fact]
    public void TenantScope_Dispose_FromNullPreviousTenant_RestoresNull()
    {
        // Arrange
        var accessor = new TenantContextAccessor();
        var ctx = (TenantContext)accessor.TenantContext;

        // Act
        using (new TenantScope(ctx, new TenantInfo { Id = "scoped" }))
        {
            accessor.TenantContext.TenantId.Should().Be("scoped");
        }

        // Assert
        accessor.TenantContext.HasTenant.Should().BeFalse();
    }
}
