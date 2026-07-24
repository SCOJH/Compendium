// -----------------------------------------------------------------------
// <copyright file="SecretVaultErrorsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Core.Results;
using FluentAssertions;
using Xunit;

namespace Compendium.Abstractions.Secrets.Tests;

/// <summary>
/// Error-code and metadata guarantees consumers rely on for uniform handling.
/// </summary>
public sealed class SecretVaultErrorsTests
{
    [Fact]
    public void NotSupported_CarriesProviderAndCapabilityMetadata()
    {
        var error = SecretVaultErrors.NotSupported("scaleway", SecretVaultCapability.LargePayload, "64 KiB limit");

        error.Code.Should().Be("SecretVault.CapabilityNotSupported");
        error.Metadata.Should().ContainKey("provider").WhoseValue.Should().Be("scaleway");
        error.Metadata.Should().ContainKey("capability").WhoseValue.Should().Be("LargePayload");
        error.Metadata.Should().ContainKey("limitation");
    }

    [Fact]
    public void Throttled_IsTooManyRequests_WithRetryAfterMetadata()
    {
        var error = SecretVaultErrors.Throttled("scaleway", 30);

        error.Type.Should().Be(ErrorType.TooManyRequests);
        error.Metadata.Should().ContainKey("retryAfterSeconds").WhoseValue.Should().Be(30);
    }

    [Fact]
    public void PayloadTooLarge_CarriesSizes()
    {
        var error = SecretVaultErrors.PayloadTooLarge(100_000, 65_536);

        error.Type.Should().Be(ErrorType.Validation);
        error.Metadata.Should().ContainKey("size").WhoseValue.Should().Be(100_000);
        error.Metadata.Should().ContainKey("maxSize").WhoseValue.Should().Be(65_536);
    }

    [Fact]
    public void NotSupported_WithoutLimitation_OmitsTheMetadataKey()
    {
        var error = SecretVaultErrors.NotSupported("scaleway", SecretVaultCapability.ServerSideRotation);

        error.Code.Should().Be("SecretVault.CapabilityNotSupported");
        error.Metadata.Should().NotContainKey("limitation");
    }

    [Fact]
    public void QuotaExceeded_IsUnavailable_WithDetailInMessage()
    {
        var error = SecretVaultErrors.QuotaExceeded("scaleway", "1000 secrets per project");

        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().Contain("1000 secrets per project");
    }

    [Fact]
    public void ProviderRejected_BothDetailBranches_CarryStatusCode()
    {
        var bare = SecretVaultErrors.ProviderRejected("scaleway", 500);
        var detailed = SecretVaultErrors.ProviderRejected("scaleway", 502, "bad gateway");

        bare.Metadata.Should().ContainKey("statusCode").WhoseValue.Should().Be(500);
        detailed.Message.Should().Contain("bad gateway");
    }

    [Fact]
    public void AuthenticationFailed_And_NotConfigured_BothBranches()
    {
        SecretVaultErrors.AuthenticationFailed("scaleway").Type.Should().Be(ErrorType.Unauthorized);
        SecretVaultErrors.AuthenticationFailed("scaleway", "expired").Message.Should().Contain("expired");
        SecretVaultErrors.NotConfigured().Message.Should().Contain("No secret vault");
        SecretVaultErrors.NotConfigured("null").Message.Should().Contain("'null'");
    }

    [Fact]
    public void Throttled_WithoutRetryAfter_OmitsTheMetadataKey()
    {
        var error = SecretVaultErrors.Throttled("scaleway");

        error.Type.Should().Be(ErrorType.TooManyRequests);
        error.Metadata.Should().NotContainKey("retryAfterSeconds");
    }

    [Fact]
    public void NotFoundAndConflictFamilies_UseStableCodes()
    {
        SecretVaultErrors.SecretNotFound("s1").Code.Should().Be("SecretVault.SecretNotFound");
        SecretVaultErrors.VersionNotFound("s1", 3).Code.Should().Be("SecretVault.VersionNotFound");
        SecretVaultErrors.VersionDisabled("s1", 3).Code.Should().Be("SecretVault.VersionDisabled");
        SecretVaultErrors.ConflictExists("name", "/p").Code.Should().Be("SecretVault.ConflictExists");
    }

    [Fact]
    public void EnsureSupported_DeclaredCapability_Succeeds()
    {
        var capabilities = new SecretVaultCapabilities
        {
            Provider = "test",
            Entries = new Dictionary<SecretVaultCapability, SecretVaultCapabilitySupport>
            {
                [SecretVaultCapability.Tags] = new(SecretVaultCapabilityLevel.Partial, "limited filters"),
            },
        };

        capabilities.EnsureSupported(SecretVaultCapability.Tags).IsSuccess.Should().BeTrue();
        capabilities.Supports(SecretVaultCapability.PathHierarchy).Should().BeFalse();

        var missing = capabilities.EnsureSupported(SecretVaultCapability.PathHierarchy);
        missing.IsFailure.Should().BeTrue();
        missing.Error.Code.Should().Be("SecretVault.CapabilityNotSupported");
    }
}
