// -----------------------------------------------------------------------
// <copyright file="GitServerCapabilitiesTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Capabilities;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for <see cref="GitServerCapabilities"/>: the capability matrix
/// drives UI affordances via <see cref="GitServerCapabilities.Supports"/> and
/// uniform failures via <see cref="GitServerCapabilities.EnsureSupported"/>.
/// </summary>
public sealed class GitServerCapabilitiesTests
{
    private static GitServerCapabilities Build(
        IReadOnlyDictionary<GitCapability, GitCapabilitySupport> entries) => new()
        {
            Provider = "github",
            Entries = entries,
        };

    [Theory]
    [InlineData(GitCapabilityLevel.Full, true)]
    [InlineData(GitCapabilityLevel.Partial, true)]
    [InlineData(GitCapabilityLevel.None, false)]
    public void Supports_PresentEntry_ReflectsTheDeclaredLevel(GitCapabilityLevel level, bool expected)
    {
        // Arrange
        var capabilities = Build(new Dictionary<GitCapability, GitCapabilitySupport>
        {
            [GitCapability.RepositoryManagement] = new(level),
        });

        // Act
        var supported = capabilities.Supports(GitCapability.RepositoryManagement);

        // Assert
        supported.Should().Be(expected);
    }

    [Fact]
    public void Supports_AbsentEntry_ReturnsFalse()
    {
        // Arrange
        var capabilities = Build(new Dictionary<GitCapability, GitCapabilitySupport>());

        // Act
        var supported = capabilities.Supports(GitCapability.PipelineTrigger);

        // Assert
        supported.Should().BeFalse();
    }

    [Fact]
    public void EnsureSupported_SupportedCapability_ReturnsSuccess()
    {
        // Arrange
        var capabilities = Build(new Dictionary<GitCapability, GitCapabilitySupport>
        {
            [GitCapability.CiSecrets] = new(GitCapabilityLevel.Full),
        });

        // Act
        var result = capabilities.EnsureSupported(GitCapability.CiSecrets);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void EnsureSupported_AbsentCapability_FailsWithoutLimitation()
    {
        // Arrange
        var capabilities = Build(new Dictionary<GitCapability, GitCapabilitySupport>());

        // Act
        var result = capabilities.EnsureSupported(GitCapability.WebhookIngestion);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.CapabilityNotSupported");
        result.Error.Metadata.Should().ContainKey("provider").WhoseValue.Should().Be("github");
        result.Error.Metadata.Should().ContainKey("capability").WhoseValue.Should().Be("WebhookIngestion");
        result.Error.Metadata.Should().NotContainKey("limitation");
    }

    [Fact]
    public void EnsureSupported_NoneLevelWithLimitation_FailsAndPropagatesTheLimitation()
    {
        // Arrange
        var capabilities = Build(new Dictionary<GitCapability, GitCapabilitySupport>
        {
            [GitCapability.NamespaceProvisioning] = new(GitCapabilityLevel.None, "org creation not reachable"),
        });

        // Act
        var result = capabilities.EnsureSupported(GitCapability.NamespaceProvisioning);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.CapabilityNotSupported");
        result.Error.Message.Should().Contain("org creation not reachable");
        result.Error.Metadata.Should().ContainKey("limitation").WhoseValue.Should().Be("org creation not reachable");
    }

    [Fact]
    public void GitCapabilitySupport_DefaultsLimitationToNull()
    {
        // Arrange / Act
        var support = new GitCapabilitySupport(GitCapabilityLevel.Full);

        // Assert
        support.Level.Should().Be(GitCapabilityLevel.Full);
        support.Limitation.Should().BeNull();
    }
}
