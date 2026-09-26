// -----------------------------------------------------------------------
// <copyright file="SecurityHeadersOptionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.AspNetCore.Security;
using AwesomeAssertions;

namespace Compendium.Adapters.AspNetCore.Tests.Security;

/// <summary>
/// Unit tests for the <see cref="SecurityHeadersOptions"/> class.
/// </summary>
public class SecurityHeadersOptionsTests
{
    [Fact]
    public void SecurityHeadersOptions_Defaults_AreSecureByDefault()
    {
        // Arrange & Act
        var options = new SecurityHeadersOptions();

        // Assert
        options.EnableHsts.Should().BeTrue();
        options.HstsMaxAgeSeconds.Should().Be(31536000);
        options.HstsIncludeSubDomains.Should().BeTrue();
        options.HstsPreload.Should().BeFalse();
        options.EnableNoSniff.Should().BeTrue();
        options.EnableFrameOptions.Should().BeTrue();
        options.FrameOptionsValue.Should().Be("DENY");
        options.EnableContentSecurityPolicy.Should().BeTrue();
        options.ContentSecurityPolicy.Should().Be("default-src 'none'; frame-ancestors 'none'");
        options.EnablePermittedCrossDomainPolicies.Should().BeTrue();
        options.PermittedCrossDomainPoliciesValue.Should().Be("none");
        options.EnableReferrerPolicy.Should().BeTrue();
        options.ReferrerPolicyValue.Should().Be("strict-origin-when-cross-origin");
        options.EnablePermissionsPolicy.Should().BeTrue();
        options.PermissionsPolicyValue.Should().Contain("geolocation=()");
        options.RemoveServerHeader.Should().BeTrue();
        options.RemoveXPoweredByHeader.Should().BeTrue();
    }

    [Fact]
    public void ForApi_ReturnsRestrictiveDefaults()
    {
        // Arrange & Act
        var options = SecurityHeadersOptions.ForApi();

        // Assert
        options.ContentSecurityPolicy.Should().Be("default-src 'none'; frame-ancestors 'none'");
        options.FrameOptionsValue.Should().Be("DENY");
        options.HstsMaxAgeSeconds.Should().Be(31536000);
        options.HstsIncludeSubDomains.Should().BeTrue();
        options.HstsPreload.Should().BeFalse();
        options.PermissionsPolicyValue.Should().Be("geolocation=(), microphone=(), camera=(), payment=(), usb=()");
    }

    [Fact]
    public void ForWebApp_AllowsSelfSourcedAssets()
    {
        // Arrange & Act
        var options = SecurityHeadersOptions.ForWebApp();

        // Assert
        options.ContentSecurityPolicy.Should().Contain("default-src 'self'");
        options.ContentSecurityPolicy.Should().Contain("img-src 'self' data:");
        options.FrameOptionsValue.Should().Be("SAMEORIGIN");
        options.HstsMaxAgeSeconds.Should().Be(31536000);
        options.PermissionsPolicyValue.Should().Be("geolocation=(), microphone=(), camera=()");
    }

    [Fact]
    public void SecurityHeadersOptions_AllSettersAccessible()
    {
        // Arrange
        var options = new SecurityHeadersOptions();

        // Act
        options.EnableHsts = false;
        options.HstsMaxAgeSeconds = 100;
        options.HstsIncludeSubDomains = false;
        options.HstsPreload = true;
        options.EnableNoSniff = false;
        options.EnableFrameOptions = false;
        options.FrameOptionsValue = "SAMEORIGIN";
        options.EnableContentSecurityPolicy = false;
        options.ContentSecurityPolicy = "default-src 'self'";
        options.EnablePermittedCrossDomainPolicies = false;
        options.PermittedCrossDomainPoliciesValue = "all";
        options.EnableReferrerPolicy = false;
        options.ReferrerPolicyValue = "no-referrer";
        options.EnablePermissionsPolicy = false;
        options.PermissionsPolicyValue = "camera=(self)";
        options.RemoveServerHeader = false;
        options.RemoveXPoweredByHeader = false;

        // Assert
        options.EnableHsts.Should().BeFalse();
        options.HstsMaxAgeSeconds.Should().Be(100);
        options.HstsIncludeSubDomains.Should().BeFalse();
        options.HstsPreload.Should().BeTrue();
        options.EnableNoSniff.Should().BeFalse();
        options.EnableFrameOptions.Should().BeFalse();
        options.FrameOptionsValue.Should().Be("SAMEORIGIN");
        options.EnableContentSecurityPolicy.Should().BeFalse();
        options.ContentSecurityPolicy.Should().Be("default-src 'self'");
        options.EnablePermittedCrossDomainPolicies.Should().BeFalse();
        options.PermittedCrossDomainPoliciesValue.Should().Be("all");
        options.EnableReferrerPolicy.Should().BeFalse();
        options.ReferrerPolicyValue.Should().Be("no-referrer");
        options.EnablePermissionsPolicy.Should().BeFalse();
        options.PermissionsPolicyValue.Should().Be("camera=(self)");
        options.RemoveServerHeader.Should().BeFalse();
        options.RemoveXPoweredByHeader.Should().BeFalse();
    }
}
