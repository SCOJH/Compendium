// -----------------------------------------------------------------------
// <copyright file="SecurityHeadersMiddlewareTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.AspNetCore.Security;
using AwesomeAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.AspNetCore.Tests.Security;

/// <summary>
/// Unit tests for the <see cref="SecurityHeadersMiddleware"/> class.
/// </summary>
public class SecurityHeadersMiddlewareTests
{
    private static SecurityHeadersMiddleware CreateMiddleware(
        SecurityHeadersOptions options,
        RequestDelegate? next = null)
    {
        next ??= _ => Task.CompletedTask;
        return new SecurityHeadersMiddleware(next, Options.Create(options));
    }

    [Fact]
    public void Constructor_WhenNextIsNull_Throws()
    {
        // Arrange
        var options = Options.Create(new SecurityHeadersOptions());

        // Act
        var act = () => new SecurityHeadersMiddleware(null!, options);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("next");
    }

    [Fact]
    public void Constructor_WhenOptionsIsNull_Throws()
    {
        // Arrange
        RequestDelegate next = _ => Task.CompletedTask;

        // Act
        var act = () => new SecurityHeadersMiddleware(next, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task InvokeAsync_WhenContextIsNull_Throws()
    {
        // Arrange
        var middleware = CreateMiddleware(new SecurityHeadersOptions());

        // Act
        Func<Task> act = () => middleware.InvokeAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("context");
    }

    [Fact]
    public async Task InvokeAsync_AllOptionsEnabled_AddsExpectedHeaders()
    {
        // Arrange
        var options = new SecurityHeadersOptions();
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
        context.Response.Headers["X-Frame-Options"].ToString().Should().Be("DENY");
        context.Response.Headers["Content-Security-Policy"].ToString()
            .Should().Be("default-src 'none'; frame-ancestors 'none'");
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"].ToString().Should().Be("none");
        context.Response.Headers["Referrer-Policy"].ToString().Should().Be("strict-origin-when-cross-origin");
        context.Response.Headers["Permissions-Policy"].ToString()
            .Should().Be("geolocation=(), microphone=(), camera=(), payment=(), usb=()");
        context.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().Be("max-age=31536000; includeSubDomains");
    }

    [Fact]
    public async Task InvokeAsync_AllOptionsDisabled_AddsNoHeaders()
    {
        // Arrange
        var options = new SecurityHeadersOptions
        {
            EnableHsts = false,
            EnableNoSniff = false,
            EnableFrameOptions = false,
            EnableContentSecurityPolicy = false,
            EnablePermittedCrossDomainPolicies = false,
            EnableReferrerPolicy = false,
            EnablePermissionsPolicy = false,
            RemoveServerHeader = false,
            RemoveXPoweredByHeader = false
        };
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.Should().NotContainKey("X-Content-Type-Options");
        context.Response.Headers.Should().NotContainKey("X-Frame-Options");
        context.Response.Headers.Should().NotContainKey("Content-Security-Policy");
        context.Response.Headers.Should().NotContainKey("X-Permitted-Cross-Domain-Policies");
        context.Response.Headers.Should().NotContainKey("Referrer-Policy");
        context.Response.Headers.Should().NotContainKey("Permissions-Policy");
        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task InvokeAsync_RemovesServerAndPoweredByHeaders_WhenEnabled()
    {
        // Arrange
        var options = new SecurityHeadersOptions();
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();
        context.Response.Headers["Server"] = "Kestrel";
        context.Response.Headers["X-Powered-By"] = "ASP.NET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.Should().NotContainKey("Server");
        context.Response.Headers.Should().NotContainKey("X-Powered-By");
    }

    [Fact]
    public async Task InvokeAsync_KeepsServerAndPoweredByHeaders_WhenRemovalDisabled()
    {
        // Arrange
        var options = new SecurityHeadersOptions
        {
            RemoveServerHeader = false,
            RemoveXPoweredByHeader = false
        };
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();
        context.Response.Headers["Server"] = "Kestrel";
        context.Response.Headers["X-Powered-By"] = "ASP.NET";

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["Server"].ToString().Should().Be("Kestrel");
        context.Response.Headers["X-Powered-By"].ToString().Should().Be("ASP.NET");
    }

    [Fact]
    public async Task InvokeAsync_WhenNotHttps_DoesNotAddHsts()
    {
        // Arrange
        var middleware = CreateMiddleware(new SecurityHeadersOptions());
        var context = new DefaultHttpContext();
        context.Request.IsHttps = false;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.Should().NotContainKey("Strict-Transport-Security");
    }

    [Fact]
    public async Task InvokeAsync_HstsWithPreload_IncludesPreloadDirective()
    {
        // Arrange
        var options = new SecurityHeadersOptions
        {
            HstsPreload = true
        };
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["Strict-Transport-Security"].ToString()
            .Should().Be("max-age=31536000; includeSubDomains; preload");
    }

    [Fact]
    public async Task InvokeAsync_HstsWithoutSubDomainsAndPreload_HasMaxAgeOnly()
    {
        // Arrange
        var options = new SecurityHeadersOptions
        {
            HstsIncludeSubDomains = false,
            HstsPreload = false,
            HstsMaxAgeSeconds = 600
        };
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers["Strict-Transport-Security"].ToString().Should().Be("max-age=600");
    }

    [Fact]
    public async Task InvokeAsync_EmptyContentSecurityPolicy_DoesNotAddCspHeader()
    {
        // Arrange
        var options = new SecurityHeadersOptions
        {
            EnableContentSecurityPolicy = true,
            ContentSecurityPolicy = "   "
        };
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.Should().NotContainKey("Content-Security-Policy");
    }

    [Fact]
    public async Task InvokeAsync_EmptyPermissionsPolicy_DoesNotAddHeader()
    {
        // Arrange
        var options = new SecurityHeadersOptions
        {
            EnablePermissionsPolicy = true,
            PermissionsPolicyValue = string.Empty
        };
        var middleware = CreateMiddleware(options);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        context.Response.Headers.Should().NotContainKey("Permissions-Policy");
    }

    [Fact]
    public async Task InvokeAsync_CallsNextDelegate()
    {
        // Arrange
        var nextCalled = false;
        RequestDelegate next = _ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(new SecurityHeadersOptions(), next);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_AddsHeadersBeforeCallingNext()
    {
        // Arrange
        string? cspWhenNextRan = null;
        RequestDelegate next = ctx =>
        {
            cspWhenNextRan = ctx.Response.Headers["Content-Security-Policy"].ToString();
            return Task.CompletedTask;
        };

        var middleware = CreateMiddleware(new SecurityHeadersOptions(), next);
        var context = new DefaultHttpContext();

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        cspWhenNextRan.Should().Be("default-src 'none'; frame-ancestors 'none'");
    }
}
