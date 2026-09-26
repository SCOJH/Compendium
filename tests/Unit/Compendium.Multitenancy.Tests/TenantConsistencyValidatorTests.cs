using Compendium.Core.Results;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Compendium.Multitenancy.Tests;

public class TenantConsistencyValidatorTests
{
    private readonly ILogger<TenantConsistencyValidator> _logger;

    public TenantConsistencyValidatorTests()
    {
        _logger = Substitute.For<ILogger<TenantConsistencyValidator>>();
    }

    [Fact]
    public void Validate_AllSourcesMatch_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-123",
            SubdomainTenantId = "tenant-123",
            JwtTenantId = "tenant-123"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("tenant-123");
    }

    [Fact]
    public void Validate_OnlyHeaderSource_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-456"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("tenant-456");
    }

    [Fact]
    public void Validate_OnlySubdomainSource_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            SubdomainTenantId = "acme"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("acme");
    }

    [Fact]
    public void Validate_OnlyJwtSource_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            JwtTenantId = "jwt-tenant-789"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("jwt-tenant-789");
    }

    [Fact]
    public void Validate_NoSources_WithRequireAtLeastOne_ShouldReturnFailure()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers();

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.NoIdentifier");
    }

    [Fact]
    public void Validate_NoSources_WithoutRequireAtLeastOne_ShouldReturnEmptySuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = false };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers();

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public void Validate_MismatchedSources_ShouldReturnFailure()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-a",
            SubdomainTenantId = "tenant-b",
            JwtTenantId = "tenant-c"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Mismatch");
    }

    [Fact]
    public void Validate_TwoSourcesMismatch_ShouldReturnFailure()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-x",
            JwtTenantId = "tenant-y"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.Mismatch");
    }

    [Fact]
    public void Validate_TwoSourcesMatch_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-same",
            JwtTenantId = "tenant-same"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("tenant-same");
    }

    [Fact]
    public void Validate_CaseInsensitiveMatch_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions { RequireAtLeastOneSource = true };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "Tenant-ABC",
            SubdomainTenantId = "tenant-abc",
            JwtTenantId = "TENANT-ABC"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Validate_MinimumSourcesRequired_InsufficientSources_ShouldReturnFailure()
    {
        // Arrange
        var options = new TenantConsistencyOptions
        {
            RequireAtLeastOneSource = true,
            MinimumRequiredSources = 2
        };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-123"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Tenant.InsufficientSources");
    }

    [Fact]
    public void Validate_MinimumSourcesRequired_SufficientSources_ShouldReturnSuccess()
    {
        // Arrange
        var options = new TenantConsistencyOptions
        {
            RequireAtLeastOneSource = true,
            MinimumRequiredSources = 2
        };
        var validator = new TenantConsistencyValidator(options, _logger);
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-123",
            JwtTenantId = "tenant-123"
        };

        // Act
        var result = validator.Validate(sources);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("tenant-123");
    }
}

public class TenantSourceIdentifiersTests
{
    [Fact]
    public void GetAllIds_WithAllSources_ShouldReturnAllThree()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "a",
            SubdomainTenantId = "b",
            JwtTenantId = "c"
        };

        // Act
        var ids = sources.GetAllIds().ToList();

        // Assert
        ids.Should().HaveCount(3);
        ids.Should().Contain(new[] { "a", "b", "c" });
    }

    [Fact]
    public void GetAllIds_WithSomeSources_ShouldReturnOnlyNonNull()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "a",
            JwtTenantId = "c"
        };

        // Act
        var ids = sources.GetAllIds().ToList();

        // Assert
        ids.Should().HaveCount(2);
        ids.Should().Contain(new[] { "a", "c" });
    }

    [Fact]
    public void GetAllIds_WithNoSources_ShouldReturnEmpty()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers();

        // Act
        var ids = sources.GetAllIds().ToList();

        // Assert
        ids.Should().BeEmpty();
    }

    [Fact]
    public void SourceCount_ShouldCountNonNullSources()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "a",
            SubdomainTenantId = "b"
        };

        // Assert
        sources.SourceCount.Should().Be(2);
    }

    [Fact]
    public void AreConsistent_AllSame_ShouldReturnTrue()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant",
            SubdomainTenantId = "tenant",
            JwtTenantId = "tenant"
        };

        // Assert
        sources.AreConsistent.Should().BeTrue();
    }

    [Fact]
    public void AreConsistent_AllDifferent_ShouldReturnFalse()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "a",
            SubdomainTenantId = "b",
            JwtTenantId = "c"
        };

        // Assert
        sources.AreConsistent.Should().BeFalse();
    }

    [Fact]
    public void AreConsistent_OnlyOne_ShouldReturnTrue()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant"
        };

        // Assert
        sources.AreConsistent.Should().BeTrue();
    }

    [Fact]
    public void AreConsistent_None_ShouldReturnTrue()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers();

        // Assert
        sources.AreConsistent.Should().BeTrue();
    }

    [Fact]
    public void ResolvedTenantId_WhenConsistent_ShouldReturnTenantId()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-resolved",
            JwtTenantId = "tenant-resolved"
        };

        // Assert
        sources.ResolvedTenantId.Should().Be("tenant-resolved");
    }

    [Fact]
    public void ResolvedTenantId_WhenInconsistent_ShouldReturnNull()
    {
        // Arrange
        var sources = new TenantSourceIdentifiers
        {
            HeaderTenantId = "tenant-a",
            JwtTenantId = "tenant-b"
        };

        // Assert
        sources.ResolvedTenantId.Should().BeNull();
    }
}

public class TenantErrorsTests
{
    [Fact]
    public void NoTenantIdentifier_ShouldReturnValidationError()
    {
        // Act
        var error = TenantErrors.NoTenantIdentifier();

        // Assert
        error.Code.Should().Be("Tenant.NoIdentifier");
        error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void InsufficientSources_ShouldIncludeCounts()
    {
        // Act
        var error = TenantErrors.InsufficientSources(2, 1);

        // Assert
        error.Code.Should().Be("Tenant.InsufficientSources");
        error.Message.Should().Contain("2");
        error.Message.Should().Contain("1");
    }

    [Fact]
    public void TenantMismatch_ShouldIncludeAllSources()
    {
        // Act
        var error = TenantErrors.TenantMismatch("header", "subdomain", "jwt");

        // Assert
        error.Code.Should().Be("Tenant.Mismatch");
        error.Message.Should().Contain("header");
        error.Message.Should().Contain("subdomain");
        error.Message.Should().Contain("jwt");
    }

    [Fact]
    public void TenantNotFound_ShouldReturnNotFoundError()
    {
        // Act
        var error = TenantErrors.TenantNotFound("unknown-tenant");

        // Assert
        error.Code.Should().Be("Tenant.NotFound");
        error.Message.Should().Contain("unknown-tenant");
        error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void TenantAccessDenied_ShouldReturnForbiddenError()
    {
        // Act
        var error = TenantErrors.TenantAccessDenied("restricted-tenant");

        // Assert
        error.Code.Should().Be("Tenant.AccessDenied");
        error.Message.Should().Contain("restricted-tenant");
        error.Type.Should().Be(ErrorType.Forbidden);
    }
}
