// -----------------------------------------------------------------------
// <copyright file="SecurityExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Diagnostics;
using Compendium.Adapters.AspNetCore.Security;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Compendium.Adapters.AspNetCore.Tests.Security;

/// <summary>
/// Unit tests for the <see cref="SecurityExtensions"/> static class.
/// </summary>
public class SecurityExtensionsTests
{
    [Fact]
    public void DefaultCorsPolicyName_IsCompendiumCorsPolicy()
    {
        // Arrange & Act & Assert
        SecurityExtensions.DefaultCorsPolicyName.Should().Be("CompendiumCorsPolicy");
    }

    [Fact]
    public void AddCompendiumSecurityHeaders_WhenServicesIsNull_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumSecurityHeaders();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddCompendiumSecurityHeaders_RegistersOptionsWithApiDefaults()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumSecurityHeaders();
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<SecurityHeadersOptions>>().Value;

        // Assert
        options.FrameOptionsValue.Should().Be("DENY");
        options.ContentSecurityPolicy.Should().Be("default-src 'none'; frame-ancestors 'none'");
        options.HstsMaxAgeSeconds.Should().Be(31536000);
        options.PermittedCrossDomainPoliciesValue.Should().Be("none");
        options.ReferrerPolicyValue.Should().Be("strict-origin-when-cross-origin");
    }

    [Fact]
    public void AddCompendiumSecurityHeaders_AppliesUserConfiguration()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumSecurityHeaders(o =>
        {
            o.FrameOptionsValue = "SAMEORIGIN";
            o.HstsMaxAgeSeconds = 600;
            o.EnableHsts = false;
        });
        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<SecurityHeadersOptions>>().Value;

        // Assert
        options.FrameOptionsValue.Should().Be("SAMEORIGIN");
        options.HstsMaxAgeSeconds.Should().Be(600);
        options.EnableHsts.Should().BeFalse();
    }

    [Fact]
    public void AddCompendiumSecurityHeaders_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCompendiumSecurityHeaders();

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCompendiumCors_WhenServicesIsNull_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumCors(_ => { });

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddCompendiumCors_WhenConfigureIsNull_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumCors(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("configure");
    }

    [Fact]
    public async Task AddCompendiumCors_RegistersCorsAndAppliesPolicy()
    {
        // Arrange
        var services = new ServiceCollection();
        var configureCalled = false;

        // Act
        services.AddCompendiumCors(builder =>
        {
            configureCalled = true;
            builder.WithOrigins("https://example.com");
        });
        var sp = services.BuildServiceProvider();
        var policyProvider = sp.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(new DefaultHttpContext(), SecurityExtensions.DefaultCorsPolicyName);

        // Assert
        policy.Should().NotBeNull();
        configureCalled.Should().BeTrue();
        policy!.Origins.Should().Contain("https://example.com");
    }

    [Fact]
    public void AddCompendiumCors_ReturnsSameServiceCollection()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var result = services.AddCompendiumCors(_ => { });

        // Assert
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void AddCompendiumStrictCors_WhenServicesIsNull_Throws()
    {
        // Arrange
        IServiceCollection? services = null;

        // Act
        var act = () => services!.AddCompendiumStrictCors(new[] { "https://example.com" });

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddCompendiumStrictCors_WhenAllowedOriginsIsNull_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumStrictCors(null!);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("allowedOrigins");
    }

    [Fact]
    public void AddCompendiumStrictCors_WhenAllowedOriginsEmpty_Throws()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        var act = () => services.AddCompendiumStrictCors(Array.Empty<string>());

        // Assert
        act.Should().Throw<ArgumentException>().WithParameterName("allowedOrigins");
    }

    [Fact]
    public async Task AddCompendiumStrictCors_BuildsExpectedPolicy()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumStrictCors(new[] { "https://api.example.com" }, "MyPolicy");
        var sp = services.BuildServiceProvider();
        var policyProvider = sp.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(new DefaultHttpContext(), "MyPolicy");

        // Assert
        policy.Should().NotBeNull();
        policy!.Origins.Should().Contain("https://api.example.com");
        policy.Methods.Should().Contain(new[] { "GET", "POST", "PUT", "DELETE", "PATCH" });
        policy.Headers.Should().Contain(new[] { "Content-Type", "Authorization", "X-Requested-With" });
        policy.SupportsCredentials.Should().BeTrue();
    }

    [Fact]
    public async Task AddCompendiumStrictCors_UsesDefaultPolicyNameWhenNotProvided()
    {
        // Arrange
        var services = new ServiceCollection();

        // Act
        services.AddCompendiumStrictCors(new[] { "https://api.example.com" });
        var sp = services.BuildServiceProvider();
        var policyProvider = sp.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(new DefaultHttpContext(), SecurityExtensions.DefaultCorsPolicyName);

        // Assert
        policy.Should().NotBeNull();
    }

    [Fact]
    public void UseCompendiumSecurityHeaders_WhenAppIsNull_Throws()
    {
        // Arrange
        IApplicationBuilder? app = null;

        // Act
        var act = () => app!.UseCompendiumSecurityHeaders();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void UseCompendiumSecurityHeaders_RegistersMiddlewareInPipeline()
    {
        // Arrange
        var sc = new ServiceCollection();
        sc.AddCompendiumSecurityHeaders();
        sc.AddSingleton(new DiagnosticListener("test"));
        sc.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        var sp = sc.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);

        // Act
        var result = app.UseCompendiumSecurityHeaders();

        // Assert
        result.Should().BeSameAs(app);
    }

    [Fact]
    public void UseCompendiumHsts_WhenAppIsNull_Throws()
    {
        // Arrange
        IApplicationBuilder? app = null;

        // Act
        var act = () => app!.UseCompendiumHsts();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void UseCompendiumHsts_ReturnsSameAppBuilder()
    {
        // Arrange
        var app = Substitute.For<IApplicationBuilder>();
        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        app.ApplicationServices.Returns(serviceProvider);
        app.Use(Arg.Any<Func<RequestDelegate, RequestDelegate>>()).Returns(app);

        // Act
        var result = app.UseCompendiumHsts(maxAgeInSeconds: 60);

        // Assert
        result.Should().BeSameAs(app);
    }

    [Fact]
    public void UseCompendiumCors_WhenAppIsNull_Throws()
    {
        // Arrange
        IApplicationBuilder? app = null;

        // Act
        var act = () => app!.UseCompendiumCors();

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("app");
    }

    [Fact]
    public void UseCompendiumCors_RegistersCorsMiddleware()
    {
        // Arrange
        var sc = new ServiceCollection();
        sc.AddCompendiumStrictCors(new[] { "https://example.com" });
        var sp = sc.BuildServiceProvider();
        var app = new ApplicationBuilder(sp);

        // Act
        var result = app.UseCompendiumCors();

        // Assert
        result.Should().BeSameAs(app);
    }
}
