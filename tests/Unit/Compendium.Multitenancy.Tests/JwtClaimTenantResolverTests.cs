using Compendium.Core.Results;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Compendium.Multitenancy.Tests;

public class JwtClaimTenantResolverTests
{
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<JwtClaimTenantResolver> _logger;

    public JwtClaimTenantResolverTests()
    {
        _tenantStore = Substitute.For<ITenantStore>();
        _logger = Substitute.For<ILogger<JwtClaimTenantResolver>>();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithValidClaim_ShouldResolveTenant()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions();
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var expectedTenant = new TenantInfo { Id = "tenant-123", Name = "Test Tenant" };
        _tenantStore.GetByIdAsync("tenant-123", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(expectedTenant));

        var claims = new Dictionary<string, string>
        {
            ["tenant_id"] = "tenant-123"
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("tenant-123");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithAlternativeClaim_ShouldResolveTenant()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions();
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var expectedTenant = new TenantInfo { Id = "org-456", Name = "Org Tenant" };
        _tenantStore.GetByIdAsync("org-456", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(expectedTenant));

        var claims = new Dictionary<string, string>
        {
            ["org_id"] = "org-456" // Alternative claim name
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Id.Should().Be("org-456");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithZitadelClaim_ShouldResolveTenant()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions();
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var expectedTenant = new TenantInfo { Id = "zitadel-org", Name = "Zitadel Org" };
        _tenantStore.GetByIdAsync("zitadel-org", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(expectedTenant));

        var claims = new Dictionary<string, string>
        {
            ["urn:zitadel:iam:org:id"] = "zitadel-org"
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("zitadel-org");
    }

    [Fact]
    public async Task ResolveTenantAsync_WithNoClaims_ShouldReturnNull()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions();
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?>()
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithEmptyClaims_ShouldReturnNull()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions();
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var claims = new Dictionary<string, string>();

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithUnknownClaimTypes_ShouldReturnNull()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions();
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var claims = new Dictionary<string, string>
        {
            ["unknown_claim"] = "some-value",
            ["another_claim"] = "another-value"
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ResolveTenantAsync_WithCustomClaimNames_ShouldUseCustomClaims()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions
        {
            ClaimNames = new[] { "custom_tenant", "my_org" }
        };
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var expectedTenant = new TenantInfo { Id = "custom-tenant", Name = "Custom" };
        _tenantStore.GetByIdAsync("custom-tenant", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(expectedTenant));

        var claims = new Dictionary<string, string>
        {
            ["custom_tenant"] = "custom-tenant"
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("custom-tenant");
    }

    [Fact]
    public async Task ResolveTenantAsync_FirstMatchingClaimWins()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions
        {
            ClaimNames = new[] { "first_claim", "second_claim" }
        };
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var expectedTenant = new TenantInfo { Id = "first-tenant", Name = "First" };
        _tenantStore.GetByIdAsync("first-tenant", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(expectedTenant));

        var claims = new Dictionary<string, string>
        {
            ["first_claim"] = "first-tenant",
            ["second_claim"] = "second-tenant"
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("first-tenant");

        // Verify second claim was not queried
        await _tenantStore.DidNotReceive().GetByIdAsync("second-tenant", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolveTenantAsync_WithWhitespaceClaim_ShouldSkipToNext()
    {
        // Arrange
        var options = new JwtClaimTenantResolverOptions
        {
            ClaimNames = new[] { "empty_claim", "valid_claim" }
        };
        var resolver = new JwtClaimTenantResolver(_tenantStore, options, _logger);

        var expectedTenant = new TenantInfo { Id = "valid-tenant", Name = "Valid" };
        _tenantStore.GetByIdAsync("valid-tenant", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(expectedTenant));

        var claims = new Dictionary<string, string>
        {
            ["empty_claim"] = "   ",
            ["valid_claim"] = "valid-tenant"
        };

        var context = new TenantResolutionContext
        {
            Properties = new Dictionary<string, object?> { ["Claims"] = claims }
        };

        // Act
        var result = await resolver.ResolveTenantAsync(context);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.Id.Should().Be("valid-tenant");
    }
}

public class JwtClaimTenantResolverOptionsTests
{
    [Fact]
    public void DefaultClaimNames_ShouldIncludeCommonClaims()
    {
        // Arrange & Act
        var options = new JwtClaimTenantResolverOptions();

        // Assert
        options.ClaimNames.Should().Contain("tenant_id");
        options.ClaimNames.Should().Contain("tid");
        options.ClaimNames.Should().Contain("org_id");
        options.ClaimNames.Should().Contain("organization_id");
        options.ClaimNames.Should().Contain("urn:zitadel:iam:org:id");
    }
}
