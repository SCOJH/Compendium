// -----------------------------------------------------------------------
// <copyright file="GitConnectionTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for <see cref="GitConnection"/>: the stateless per-call handle
/// identifying which git server to talk to and with what credential.
/// </summary>
public sealed class GitConnectionTests
{
    [Fact]
    public void ServerUrl_DefaultsToNull_TargetingProviderCloud()
    {
        // Arrange / Act
        var connection = new GitConnection
        {
            Provider = "github",
            Credential = new GitCredential.AppInstallation("inst-1"),
        };

        // Assert
        connection.Provider.Should().Be("github");
        connection.ServerUrl.Should().BeNull();
    }

    [Fact]
    public void ServerUrl_WhenSet_TargetsTheSelfHostedInstance()
    {
        // Arrange
        var serverUrl = new Uri("https://git.acme.invalid/api/v4");

        // Act
        var connection = new GitConnection
        {
            Provider = "gitlab",
            ServerUrl = serverUrl,
            Credential = new GitCredential.ServiceAccountToken("token"),
        };

        // Assert
        connection.ServerUrl.Should().Be(serverUrl);
    }

    [Fact]
    public void Connections_WithSameValues_AreEqual()
    {
        // Arrange
        var a = new GitConnection
        {
            Provider = "github",
            Credential = new GitCredential.AppInstallation("inst-1"),
        };
        var b = new GitConnection
        {
            Provider = "github",
            Credential = new GitCredential.AppInstallation("inst-1"),
        };

        // Act / Assert
        a.Should().Be(b);
    }

    [Fact]
    public void Connections_DifferingOnlyByServerUrl_AreNotEqual()
    {
        // Arrange
        var cloud = new GitConnection
        {
            Provider = "gitlab",
            Credential = new GitCredential.ServiceAccountToken("token"),
        };
        var selfHosted = cloud with { ServerUrl = new Uri("https://git.acme.invalid") };

        // Act / Assert
        selfHosted.Should().NotBe(cloud);
    }
}
