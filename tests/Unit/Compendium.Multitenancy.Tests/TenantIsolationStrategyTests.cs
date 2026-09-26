// -----------------------------------------------------------------------
// <copyright file="TenantIsolationStrategyTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Results;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Compendium.Multitenancy.Tests;

/// <summary>
/// Unit tests for the <see cref="DatabaseIsolationStrategy"/> class.
/// </summary>
public class DatabaseIsolationStrategyTests
{
    private readonly ILogger<DatabaseIsolationStrategy> _logger = Substitute.For<ILogger<DatabaseIsolationStrategy>>();

    [Fact]
    public void DatabaseIsolationStrategy_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new DatabaseIsolationStrategy(null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void DatabaseIsolationStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task GetConnectionStringAsync_WhenTenantIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var strategy = new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), _logger);

        // Act
        var result = await strategy.GetConnectionStringAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Null");
    }

    [Fact]
    public async Task GetConnectionStringAsync_WhenTenantHasConnectionString_UsesTenantSpecific()
    {
        // Arrange
        var strategy = new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), _logger);
        var tenant = new TenantInfo
        {
            Id = "tenant-1",
            ConnectionString = "Server=tenant-host;Database=Tenant1;"
        };

        // Act
        var result = await strategy.GetConnectionStringAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Server=tenant-host;Database=Tenant1;");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetConnectionStringAsync_WhenTenantConnectionStringMissing_BuildsFromTemplate(string? cs)
    {
        // Arrange
        var options = new DatabaseIsolationOptions
        {
            ConnectionStringTemplate = "Server=shared;Database=App_{TenantId};"
        };
        var strategy = new DatabaseIsolationStrategy(options, _logger);
        var tenant = new TenantInfo { Id = "abc", ConnectionString = cs };

        // Act
        var result = await strategy.GetConnectionStringAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Server=shared;Database=App_abc;");
    }

    [Fact]
    public async Task GetSchemaNameAsync_WhenTenantIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var strategy = new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), _logger);

        // Act
        var result = await strategy.GetSchemaNameAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Null");
    }

    [Fact]
    public async Task GetSchemaNameAsync_WhenTenantValid_ReturnsDefaultSchema()
    {
        // Arrange
        var options = new DatabaseIsolationOptions { DefaultSchema = "public" };
        var strategy = new DatabaseIsolationStrategy(options, _logger);
        var tenant = new TenantInfo { Id = "abc" };

        // Act
        var result = await strategy.GetSchemaNameAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("public");
    }

    [Fact]
    public async Task EnsureTenantResourcesAsync_WhenTenantIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var strategy = new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), _logger);

        // Act
        var result = await strategy.EnsureTenantResourcesAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Null");
    }

    [Fact]
    public async Task EnsureTenantResourcesAsync_WhenTenantValid_ReturnsSuccess()
    {
        // Arrange
        var strategy = new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), _logger);
        var tenant = new TenantInfo { Id = "abc" };

        // Act
        var result = await strategy.EnsureTenantResourcesAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureTenantResourcesAsync_WhenCancelled_ReturnsFailure()
    {
        // Arrange
        var strategy = new DatabaseIsolationStrategy(new DatabaseIsolationOptions(), _logger);
        var tenant = new TenantInfo { Id = "abc" };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await strategy.EnsureTenantResourcesAsync(tenant, cts.Token);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TenantResources.EnsureFailed");
    }

    [Fact]
    public void DatabaseIsolationOptions_Defaults_AreSensible()
    {
        // Arrange & Act
        var options = new DatabaseIsolationOptions();

        // Assert
        options.ConnectionStringTemplate.Should().Contain("{TenantId}");
        options.DefaultSchema.Should().Be("dbo");
    }
}

/// <summary>
/// Unit tests for the <see cref="SchemaIsolationStrategy"/> class.
/// </summary>
public class SchemaIsolationStrategyTests
{
    private readonly ILogger<SchemaIsolationStrategy> _logger = Substitute.For<ILogger<SchemaIsolationStrategy>>();

    [Fact]
    public void SchemaIsolationStrategy_Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new SchemaIsolationStrategy(null!, _logger);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void SchemaIsolationStrategy_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange & Act
        var act = () => new SchemaIsolationStrategy(new SchemaIsolationOptions(), null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task GetConnectionStringAsync_AlwaysReturnsSharedConnectionString()
    {
        // Arrange
        var options = new SchemaIsolationOptions { SharedConnectionString = "Server=shared;Database=AppShared;" };
        var strategy = new SchemaIsolationStrategy(options, _logger);
        var tenant = new TenantInfo { Id = "abc" };

        // Act
        var result = await strategy.GetConnectionStringAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("Server=shared;Database=AppShared;");
    }

    [Fact]
    public async Task GetConnectionStringAsync_WhenTenantNull_StillReturnsSharedConnectionString()
    {
        // Arrange
        var options = new SchemaIsolationOptions { SharedConnectionString = "shared-cs" };
        var strategy = new SchemaIsolationStrategy(options, _logger);

        // Act
        var result = await strategy.GetConnectionStringAsync(null!);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("shared-cs");
    }

    [Fact]
    public async Task GetSchemaNameAsync_WhenTenantIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var strategy = new SchemaIsolationStrategy(new SchemaIsolationOptions(), _logger);

        // Act
        var result = await strategy.GetSchemaNameAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Null");
    }

    [Fact]
    public async Task GetSchemaNameAsync_WhenTenantValid_BuildsSchemaFromTemplate()
    {
        // Arrange
        var options = new SchemaIsolationOptions { SchemaNameTemplate = "schema_{TenantId}" };
        var strategy = new SchemaIsolationStrategy(options, _logger);
        var tenant = new TenantInfo { Id = "abc" };

        // Act
        var result = await strategy.GetSchemaNameAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("schema_abc");
    }

    [Fact]
    public async Task EnsureTenantResourcesAsync_WhenTenantIsNull_ReturnsValidationFailure()
    {
        // Arrange
        var strategy = new SchemaIsolationStrategy(new SchemaIsolationOptions(), _logger);

        // Act
        var result = await strategy.EnsureTenantResourcesAsync(null!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Null");
    }

    [Fact]
    public async Task EnsureTenantResourcesAsync_WhenTenantValid_ReturnsSuccess()
    {
        // Arrange
        var strategy = new SchemaIsolationStrategy(new SchemaIsolationOptions(), _logger);
        var tenant = new TenantInfo { Id = "abc" };

        // Act
        var result = await strategy.EnsureTenantResourcesAsync(tenant);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureTenantResourcesAsync_WhenCancelled_ReturnsFailure()
    {
        // Arrange
        var strategy = new SchemaIsolationStrategy(new SchemaIsolationOptions(), _logger);
        var tenant = new TenantInfo { Id = "abc" };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act
        var result = await strategy.EnsureTenantResourcesAsync(tenant, cts.Token);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TenantResources.EnsureFailed");
    }

    [Fact]
    public void SchemaIsolationOptions_Defaults_AreSensible()
    {
        // Arrange & Act
        var options = new SchemaIsolationOptions();

        // Assert
        options.SharedConnectionString.Should().NotBeNullOrEmpty();
        options.SchemaNameTemplate.Should().Contain("{TenantId}");
    }

    [Fact]
    public void IsolationLevel_HasExpectedValues()
    {
        // Arrange & Act
        var values = Enum.GetValues<IsolationLevel>();

        // Assert
        values.Should().Contain(new[] { IsolationLevel.Database, IsolationLevel.Schema, IsolationLevel.Row });
    }
}
