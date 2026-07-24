// -----------------------------------------------------------------------
// <copyright file="GitRepositoryRefTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for <see cref="GitRepositoryRef"/>: the <c>namespace/name</c>
/// identifier used throughout the abstraction.
/// </summary>
public sealed class GitRepositoryRefTests
{
    [Fact]
    public void FullName_JoinsNamespaceAndNameWithASlash()
    {
        // Arrange
        var reference = new GitRepositoryRef("acme", "billing-api");

        // Act
        var fullName = reference.FullName;

        // Assert
        fullName.Should().Be("acme/billing-api");
    }

    [Fact]
    public void ToString_ReturnsTheFullName()
    {
        // Arrange
        var reference = new GitRepositoryRef("acme", "billing-api");

        // Act
        var text = reference.ToString();

        // Assert
        text.Should().Be("acme/billing-api");
    }

    [Fact]
    public void References_WithSameNamespaceAndName_AreEqual()
    {
        // Arrange
        var a = new GitRepositoryRef("acme", "billing-api");
        var b = new GitRepositoryRef("acme", "billing-api");

        // Act / Assert
        a.Should().Be(b);
        a.GetHashCode().Should().Be(b.GetHashCode());
    }
}
