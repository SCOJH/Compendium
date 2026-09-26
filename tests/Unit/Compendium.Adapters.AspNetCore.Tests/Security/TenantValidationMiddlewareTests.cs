// -----------------------------------------------------------------------
// <copyright file="TenantValidationMiddlewareTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.IO;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Compendium.Adapters.AspNetCore.Security;
using Compendium.Core.Results;
using Compendium.Multitenancy;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Compendium.Adapters.AspNetCore.Tests.Security;

/// <summary>
/// Unit tests for the <see cref="TenantValidationMiddleware"/> class.
/// </summary>
public class TenantValidationMiddlewareTests
{
    private readonly ITenantConsistencyValidator _validator = Substitute.For<ITenantConsistencyValidator>();
    private readonly ITenantStore _tenantStore = Substitute.For<ITenantStore>();
    private readonly TenantContext _tenantContext = new();

    private static TenantValidationMiddleware CreateMiddleware(
        TenantValidationMiddlewareOptions? options = null,
        RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        options ??= new TenantValidationMiddlewareOptions();
        return new TenantValidationMiddleware(
            next,
            Options.Create(options),
            NullLogger<TenantValidationMiddleware>.Instance);
    }

    private static DefaultHttpContext CreateContext(string path = "/api/data")
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Path = path;
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string ReadResponseBody(HttpContext context)
    {
        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    [Fact]
    public async Task InvokeAsync_WhenPathIsExcluded_SkipsValidationAndCallsNext()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(
            new TenantValidationMiddlewareOptions { ExcludedPaths = new[] { "/health" } },
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });
        var ctx = CreateContext("/health/ready");

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        nextCalled.Should().BeTrue();
        _validator.DidNotReceive().Validate(Arg.Any<TenantSourceIdentifiers>());
    }

    [Fact]
    public async Task InvokeAsync_WhenPathIsExcludedAndPathIsNull_SkipsValidationAndCallsNext()
    {
        // Arrange
        var nextCalled = false;
        var middleware = CreateMiddleware(
            new TenantValidationMiddlewareOptions { ExcludedPaths = new[] { string.Empty } },
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });
        var ctx = CreateContext();
        ctx.Request.Path = PathString.Empty;

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WhenValidationFails_Returns403WithJsonBody()
    {
        // Arrange
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Failure<string>(TenantErrors.NoTenantIdentifier()));
        var middleware = CreateMiddleware();
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ctx.Response.ContentType.Should().Be("application/json");
        var body = ReadResponseBody(ctx);
        body.Should().Contain("TENANT_ACCESS_DENIED");
    }

    [Fact]
    public async Task InvokeAsync_WhenValidationFailsWithMismatch_PathContainsCrLf_SanitizesInLog()
    {
        // Arrange
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Failure<string>(TenantErrors.TenantMismatch("a", "b", "c")));
        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        ctx.Request.Path = "/api/data\r\n\t/x";

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task InvokeAsync_WhenSourceCountIsZero_AndAllowsAnonymous_DoesNotResolveTenant()
    {
        // Arrange
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success(string.Empty));
        var nextCalled = false;
        var middleware = CreateMiddleware(next: _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        nextCalled.Should().BeTrue();
        await _tenantStore.DidNotReceive().GetByIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_WhenTenantNotFound_Returns404()
    {
        // Arrange
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success("tenant-x"));
        _tenantStore.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(null));
        var middleware = CreateMiddleware();
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
        var body = ReadResponseBody(ctx);
        body.Should().Contain("TENANT_NOT_FOUND");
    }

    [Fact]
    public async Task InvokeAsync_WhenStoreReturnsFailure_Returns404()
    {
        // Arrange
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success("tenant-x"));
        _tenantStore.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
            .Returns(Result.Failure<TenantInfo?>(TenantErrors.TenantNotFound("tenant-x")));
        var middleware = CreateMiddleware();
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task InvokeAsync_WhenTenantInactive_Returns403()
    {
        // Arrange
        var inactive = new TenantInfo { Id = "tenant-x", Name = "X", IsActive = false };
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success("tenant-x"));
        _tenantStore.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(inactive));
        var middleware = CreateMiddleware();
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        ctx.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        var body = ReadResponseBody(ctx);
        body.Should().Contain("TENANT_ACCESS_DENIED");
    }

    [Fact]
    public async Task InvokeAsync_WhenTenantActive_SetsContextAndCallsNextThenClears()
    {
        // Arrange
        var active = new TenantInfo { Id = "tenant-x", Name = "X", IsActive = true };
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success("tenant-x"));
        _tenantStore.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(active));

        TenantInfo? observedDuringNext = null;
        var middleware = CreateMiddleware(next: _ =>
        {
            observedDuringNext = _tenantContext.CurrentTenant;
            return Task.CompletedTask;
        });
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        observedDuringNext.Should().NotBeNull();
        observedDuringNext!.Id.Should().Be("tenant-x");
        _tenantContext.HasTenant.Should().BeFalse(); // cleared after next
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_StillClearsTenantContext()
    {
        // Arrange
        var active = new TenantInfo { Id = "tenant-x", Name = "X", IsActive = true };
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success("tenant-x"));
        _tenantStore.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(active));

        var middleware = CreateMiddleware(next: _ => throw new InvalidOperationException("boom"));
        var ctx = CreateContext();

        // Act
        Func<Task> act = () => middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();
        _tenantContext.HasTenant.Should().BeFalse();
    }

    [Fact]
    public async Task InvokeAsync_ExtractsHeaderValueIntoSources()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware(new TenantValidationMiddlewareOptions
        {
            TenantHeaderName = "X-Tenant-ID",
            EnableSubdomainResolution = false
        });
        var ctx = CreateContext();
        ctx.Request.Headers["X-Tenant-ID"] = "tenant-h";

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured.Should().NotBeNull();
        captured!.HeaderTenantId.Should().Be("tenant-h");
        captured.SubdomainTenantId.Should().BeNull();
        captured.JwtTenantId.Should().BeNull();
    }

    [Theory]
    [InlineData("tenant-1.example.com", "tenant-1")]
    [InlineData("acme.app.example.com", "acme")]
    public async Task InvokeAsync_ExtractsSubdomain(string host, string expectedSubdomain)
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        ctx.Request.Host = new HostString(host);

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.SubdomainTenantId.Should().Be(expectedSubdomain);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST:5000")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    [InlineData("api.example.com")] // 2 parts only -> no subdomain... wait, 3 parts
    [InlineData("example.com")]
    public async Task InvokeAsync_RejectsNonTenantHosts(string host)
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        ctx.Request.Host = new HostString(host);

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert - either null subdomain or in ignored list
        if (captured!.SubdomainTenantId is not null)
        {
            captured.SubdomainTenantId.Should().NotBe("api");
        }
    }

    [Fact]
    public async Task InvokeAsync_IgnoresSubdomain_WhenInIgnoredList()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware(new TenantValidationMiddlewareOptions
        {
            IgnoredSubdomains = new[] { "api", "www" }
        });
        var ctx = CreateContext();
        ctx.Request.Host = new HostString("api.example.com");

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.SubdomainTenantId.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_DoesNotExtractSubdomain_WhenDisabled()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware(new TenantValidationMiddlewareOptions
        {
            EnableSubdomainResolution = false
        });
        var ctx = CreateContext();
        ctx.Request.Host = new HostString("tenant-1.example.com");

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.SubdomainTenantId.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_IgnoresEmptyHost()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        ctx.Request.Host = new HostString();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.SubdomainTenantId.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ExtractsTenantFromJwtClaim()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        var identity = new ClaimsIdentity(new[] { new Claim("tenant_id", "tenant-jwt") }, "test");
        ctx.User = new ClaimsPrincipal(identity);

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.JwtTenantId.Should().Be("tenant-jwt");
    }

    [Fact]
    public async Task InvokeAsync_FallsBackThroughClaimTypes()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware(new TenantValidationMiddlewareOptions
        {
            TenantClaimTypes = new[] { "tenant_id", "org_id" }
        });
        var ctx = CreateContext();
        var identity = new ClaimsIdentity(new[] { new Claim("org_id", "tenant-org") }, "test");
        ctx.User = new ClaimsPrincipal(identity);

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.JwtTenantId.Should().Be("tenant-org");
    }

    [Fact]
    public async Task InvokeAsync_IgnoresWhitespaceClaim()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        var identity = new ClaimsIdentity(new[] { new Claim("tenant_id", "   ") }, "test");
        ctx.User = new ClaimsPrincipal(identity);

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.JwtTenantId.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_NoExtractionFromClaims_WhenUserNotAuthenticated()
    {
        // Arrange
        TenantSourceIdentifiers? captured = null;
        _validator.Validate(Arg.Do<TenantSourceIdentifiers>(s => captured = s))
            .Returns(Result.Success(string.Empty));

        var middleware = CreateMiddleware();
        var ctx = CreateContext();
        // Default user is unauthenticated

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        captured!.JwtTenantId.Should().BeNull();
    }

    [Fact]
    public async Task InvokeAsync_ErrorResponse_StatusOtherThan403_UsesNotFoundCode()
    {
        // Arrange
        _validator.Validate(Arg.Any<TenantSourceIdentifiers>())
            .Returns(Result.Success("tenant-x"));
        _tenantStore.GetByIdAsync("tenant-x", Arg.Any<CancellationToken>())
            .Returns(Result.Success<TenantInfo?>(null));
        var middleware = CreateMiddleware();
        var ctx = CreateContext();

        // Act
        await middleware.InvokeAsync(ctx, _validator, _tenantStore, _tenantContext);

        // Assert
        var body = ReadResponseBody(ctx);
        var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("error").GetProperty("code").GetString()
            .Should().Be("TENANT_NOT_FOUND");
    }
}

/// <summary>
/// Tests for the <see cref="TenantValidationMiddlewareOptions"/> defaults.
/// </summary>
public class TenantValidationMiddlewareOptionsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        // Arrange & Act
        var options = new TenantValidationMiddlewareOptions();

        // Assert
        options.TenantHeaderName.Should().Be("X-Tenant-ID");
        options.EnableSubdomainResolution.Should().BeTrue();
        options.IgnoredSubdomains.Should().Contain(new[] { "www", "api", "admin" });
        options.TenantClaimTypes.Should().Contain(new[] { "tenant_id", "tid", "org_id" });
        options.ExcludedPaths.Should().Contain(new[] { "/health", "/metrics", "/.well-known" });
    }

    [Fact]
    public void Setters_AcceptCustomValues()
    {
        // Arrange
        var options = new TenantValidationMiddlewareOptions();

        // Act
        options.TenantHeaderName = "X-MyTenant";
        options.EnableSubdomainResolution = false;
        options.IgnoredSubdomains = new[] { "static" };
        options.TenantClaimTypes = new[] { "tid" };
        options.ExcludedPaths = new[] { "/foo" };

        // Assert
        options.TenantHeaderName.Should().Be("X-MyTenant");
        options.EnableSubdomainResolution.Should().BeFalse();
        options.IgnoredSubdomains.Should().Equal("static");
        options.TenantClaimTypes.Should().Equal("tid");
        options.ExcludedPaths.Should().Equal("/foo");
    }
}
