// -----------------------------------------------------------------------
// <copyright file="GitErrorsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git;
using Compendium.Abstractions.Git.Capabilities;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for <see cref="GitErrors"/>: every factory maps to the expected
/// machine code, <see cref="ErrorType"/>, metadata, and message variant so
/// adapters and consumers can rely on a uniform failure surface.
/// </summary>
public sealed class GitErrorsTests
{
    [Fact]
    public void Prefix_IsGit()
    {
        // Arrange / Act / Assert
        GitErrors.Prefix.Should().Be("Git");
    }

    [Fact]
    public void NotConfigured_WithoutProvider_MapsToUnavailableWithHostMessage()
    {
        // Arrange / Act
        var error = GitErrors.NotConfigured();

        // Assert
        error.Code.Should().Be("Git.NotConfigured");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().Be("No git server is configured on this host.");
    }

    [Fact]
    public void NotConfigured_WithProvider_NamesTheProviderInTheMessage()
    {
        // Arrange / Act
        var error = GitErrors.NotConfigured("github");

        // Assert
        error.Code.Should().Be("Git.NotConfigured");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().Be("The 'github' git server is not configured on this host.");
    }

    [Fact]
    public void NotSupported_WithoutLimitation_MapsToUnavailableWithProviderAndCapabilityMetadata()
    {
        // Arrange / Act
        var error = GitErrors.NotSupported("gitea", GitCapability.BranchPolicies);

        // Assert
        error.Code.Should().Be("Git.CapabilityNotSupported");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().Be("Provider 'gitea' does not support capability 'BranchPolicies'.");
        error.Metadata.Should().ContainKey("provider").WhoseValue.Should().Be("gitea");
        error.Metadata.Should().ContainKey("capability").WhoseValue.Should().Be("BranchPolicies");
        error.Metadata.Should().NotContainKey("limitation");
    }

    [Fact]
    public void NotSupported_WithLimitation_AppendsAndCapturesTheLimitation()
    {
        // Arrange / Act
        var error = GitErrors.NotSupported("github", GitCapability.NamespaceProvisioning, "requires enterprise owner token");

        // Assert
        error.Code.Should().Be("Git.CapabilityNotSupported");
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().Be(
            "Provider 'github' does not support capability 'NamespaceProvisioning': requires enterprise owner token");
        error.Metadata.Should().ContainKey("limitation").WhoseValue.Should().Be("requires enterprise owner token");
    }

    [Fact]
    public void AuthenticationFailed_WithoutDetail_MapsToUnauthorized()
    {
        // Arrange / Act
        var error = GitErrors.AuthenticationFailed("gitlab");

        // Assert
        error.Code.Should().Be("Git.AuthenticationFailed");
        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Message.Should().Be("Authentication against 'gitlab' failed.");
    }

    [Fact]
    public void AuthenticationFailed_WithDetail_AppendsTheDetail()
    {
        // Arrange / Act
        var error = GitErrors.AuthenticationFailed("gitlab", "token expired");

        // Assert
        error.Code.Should().Be("Git.AuthenticationFailed");
        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Message.Should().Be("Authentication against 'gitlab' failed: token expired");
    }

    [Fact]
    public void AppNotInstalled_WithoutInstallUrl_MapsToFailureWithNamespaceMetadataOnly()
    {
        // Arrange / Act
        var error = GitErrors.AppNotInstalled("acme");

        // Assert
        error.Code.Should().Be("Git.AppNotInstalled");
        error.Type.Should().Be(ErrorType.Failure);
        error.Message.Should().Be("The platform app is not installed on 'acme'.");
        error.Metadata.Should().ContainKey("namespace").WhoseValue.Should().Be("acme");
        error.Metadata.Should().NotContainKey("installUrl");
    }

    [Fact]
    public void AppNotInstalled_WithInstallUrl_CarriesTheInstallUrlMetadata()
    {
        // Arrange / Act
        var error = GitErrors.AppNotInstalled("acme", "https://example.invalid/install");

        // Assert
        error.Metadata.Should().ContainKey("namespace").WhoseValue.Should().Be("acme");
        error.Metadata.Should().ContainKey("installUrl").WhoseValue.Should().Be("https://example.invalid/install");
    }

    [Fact]
    public void RepositoryNotFound_MapsToNotFound()
    {
        // Arrange / Act
        var error = GitErrors.RepositoryNotFound("acme/billing");

        // Assert
        error.Code.Should().Be("Git.RepositoryNotFound");
        error.Type.Should().Be(ErrorType.NotFound);
        error.Message.Should().Be("Repository 'acme/billing' was not found.");
    }

    [Fact]
    public void NamespaceNotFound_MapsToNotFound()
    {
        // Arrange / Act
        var error = GitErrors.NamespaceNotFound("acme");

        // Assert
        error.Code.Should().Be("Git.NamespaceNotFound");
        error.Type.Should().Be(ErrorType.NotFound);
        error.Message.Should().Be("Namespace 'acme' was not found.");
    }

    [Fact]
    public void Conflict_MapsToConflict()
    {
        // Arrange / Act
        var error = GitErrors.Conflict("acme/billing");

        // Assert
        error.Code.Should().Be("Git.Conflict");
        error.Type.Should().Be(ErrorType.Conflict);
        error.Message.Should().Be("'acme/billing' already exists.");
    }

    [Fact]
    public void Throttled_WithoutRetryAfter_MapsToTooManyRequestsWithoutMetadata()
    {
        // Arrange / Act
        var error = GitErrors.Throttled();

        // Assert
        error.Code.Should().Be("Git.Throttled");
        error.Type.Should().Be(ErrorType.TooManyRequests);
        error.Message.Should().Be("Git provider throttled the request. Please try again later.");
        error.Metadata.Should().NotContainKey("retryAfterSeconds");
    }

    [Fact]
    public void Throttled_WithRetryAfter_CarriesRetryHintInMessageAndMetadata()
    {
        // Arrange / Act
        var error = GitErrors.Throttled(TimeSpan.FromSeconds(30));

        // Assert
        error.Code.Should().Be("Git.Throttled");
        error.Type.Should().Be(ErrorType.TooManyRequests);
        error.Message.Should().Be("Git provider throttled the request. Retry after 30 seconds.");
        error.Metadata.Should().ContainKey("retryAfterSeconds").WhoseValue.Should().Be(30d);
    }

    [Fact]
    public void WebhookSignatureInvalid_MapsToUnauthorized()
    {
        // Arrange / Act
        var error = GitErrors.WebhookSignatureInvalid();

        // Assert
        error.Code.Should().Be("Git.WebhookSignatureInvalid");
        error.Type.Should().Be(ErrorType.Unauthorized);
        error.Message.Should().Be("The webhook delivery signature is missing or invalid.");
    }

    [Fact]
    public void ProviderRejected_MapsToFailureWithProviderAndStatusCodeMetadata()
    {
        // Arrange / Act
        var error = GitErrors.ProviderRejected("github", 422, "validation failed");

        // Assert
        error.Code.Should().Be("Git.ProviderRejected");
        error.Type.Should().Be(ErrorType.Failure);
        error.Message.Should().Be("Provider 'github' rejected the request (422): validation failed");
        error.Metadata.Should().ContainKey("provider").WhoseValue.Should().Be("github");
        error.Metadata.Should().ContainKey("statusCode").WhoseValue.Should().Be(422);
    }
}
