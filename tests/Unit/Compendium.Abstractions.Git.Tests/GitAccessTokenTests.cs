// -----------------------------------------------------------------------
// <copyright file="GitAccessTokenTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for <see cref="GitAccessToken"/> and
/// <see cref="GitAccessTokenScope"/>: a minted token must never leak its
/// material in <c>ToString()</c>, and the scope carries optional narrowing.
/// </summary>
public sealed class GitAccessTokenTests
{
    private const string Material = "im_ffffffffffffffffffffffffffffffff";

    private static GitAccessToken Build() => new()
    {
        Token = Material,
        ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
        HttpBasicUsername = "x-access-token",
    };

    [Fact]
    public void ToString_RedactsTheMaterialButKeepsExpiry()
    {
        // Arrange
        var token = Build();

        // Act
        var text = token.ToString();

        // Assert
        text.Should().NotBeNullOrEmpty();
        text.Should().NotContain(Material);
        text.Should().StartWith("GitAccessToken(***");
        text.Should().Contain("ExpiresAt=");
    }

    [Fact]
    public void Properties_RoundTripTheSuppliedValues()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(45);

        // Act
        var token = new GitAccessToken
        {
            Token = Material,
            ExpiresAt = expiresAt,
            HttpBasicUsername = "x-access-token",
        };

        // Assert
        token.Token.Should().Be(Material);
        token.ExpiresAt.Should().Be(expiresAt);
        token.HttpBasicUsername.Should().Be("x-access-token");
    }

    [Fact]
    public void Scope_DefaultsBothNarrowingMembersToNull()
    {
        // Arrange / Act
        var scope = new GitAccessTokenScope();

        // Assert
        scope.Repositories.Should().BeNull();
        scope.Permissions.Should().BeNull();
    }

    [Fact]
    public void Scope_CarriesRepositoriesAndPermissions()
    {
        // Arrange
        var repositories = new[] { new GitRepositoryRef("acme", "billing") };
        var permissions = new Dictionary<string, string> { ["contents"] = "write" };

        // Act
        var scope = new GitAccessTokenScope
        {
            Repositories = repositories,
            Permissions = permissions,
        };

        // Assert
        scope.Repositories.Should().ContainSingle().Which.Name.Should().Be("billing");
        scope.Permissions.Should().ContainKey("contents").WhoseValue.Should().Be("write");
    }
}
