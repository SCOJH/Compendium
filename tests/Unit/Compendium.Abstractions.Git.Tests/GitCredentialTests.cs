// -----------------------------------------------------------------------
// <copyright file="GitCredentialTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for the <see cref="GitCredential"/> closed union: token-bearing
/// members must redact their material in <c>ToString()</c> so a logged
/// connection never leaks a secret, and the value-record members compare by
/// value.
/// </summary>
public sealed class GitCredentialTests
{
    private const string Secret = "ghp_super_secret_token_value";

    [Fact]
    public void ServiceAccountToken_ToString_RedactsTheMaterial()
    {
        // Arrange
        var credential = new GitCredential.ServiceAccountToken(Secret);

        // Act
        var text = credential.ToString();

        // Assert
        text.Should().Be("ServiceAccountToken(***)");
        text.Should().NotBeNullOrEmpty();
        text.Should().NotContain(Secret);
    }

    [Fact]
    public void PersonalAccessToken_ToString_RedactsTheMaterial()
    {
        // Arrange
        var credential = new GitCredential.PersonalAccessToken(Secret);

        // Act
        var text = credential.ToString();

        // Assert
        text.Should().Be("PersonalAccessToken(***)");
        text.Should().NotBeNullOrEmpty();
        text.Should().NotContain(Secret);
    }

    [Fact]
    public void OAuthAccessToken_ToString_RedactsTheMaterial()
    {
        // Arrange
        var credential = new GitCredential.OAuthAccessToken(Secret);

        // Act
        var text = credential.ToString();

        // Assert
        text.Should().Be("OAuthAccessToken(***)");
        text.Should().NotBeNullOrEmpty();
        text.Should().NotContain(Secret);
    }

    [Fact]
    public void ServiceAccountToken_PreservesTheMaterialForUse()
    {
        // Arrange / Act
        var credential = new GitCredential.ServiceAccountToken(Secret);

        // Assert
        credential.Token.Should().Be(Secret);
    }

    [Fact]
    public void AppInstallation_DefaultsAppKeyToNull()
    {
        // Arrange / Act
        var credential = new GitCredential.AppInstallation("inst-1");

        // Assert
        credential.InstallationId.Should().Be("inst-1");
        credential.AppKey.Should().BeNull();
    }

    [Fact]
    public void AppInstallation_WithSameValues_AreEqual()
    {
        // Arrange
        var a = new GitCredential.AppInstallation("inst-1", "app-key");
        var b = new GitCredential.AppInstallation("inst-1", "app-key");

        // Act / Assert
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void AppInstallation_WithDifferentInstallationId_AreNotEqual()
    {
        // Arrange
        var a = new GitCredential.AppInstallation("inst-1");
        var b = new GitCredential.AppInstallation("inst-2");

        // Act / Assert
        a.Should().NotBe(b);
    }

    [Fact]
    public void AppInstallation_DifferentSubtypeWithSameInstallationId_AreNotEqual()
    {
        // Arrange
        GitCredential app = new GitCredential.AppInstallation("token");
        GitCredential token = new GitCredential.ServiceAccountToken("token");

        // Act / Assert
        app.Should().NotBe(token);
    }
}
