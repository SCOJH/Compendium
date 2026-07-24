// -----------------------------------------------------------------------
// <copyright file="SecretScopePathTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Model;
using FluentAssertions;
using Xunit;

namespace Compendium.Abstractions.Secrets.Tests;

/// <summary>
/// Validation and canonical-form tests for <see cref="SecretScopePath"/>.
/// </summary>
public sealed class SecretScopePathTests
{
    [Theory]
    [InlineData("nexus")]
    [InlineData("a1b2-c3_d4.e5")]
    [InlineData("0e21a7ab-77b5-4d20-9a4c-000000000001")]
    public void From_ValidSegment_Succeeds(string segment)
    {
        var result = SecretScopePath.From(segment);

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().Be($"/{segment}");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a/b")]
    [InlineData("a b")]
    [InlineData("a\\b")]
    public void From_InvalidSegment_FailsWithValidationError(string segment)
    {
        var result = SecretScopePath.From(segment);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.InvalidPathSegment");
    }

    [Fact]
    public void From_SegmentOver100Characters_Fails()
    {
        SecretScopePath.From(new string('a', 101)).IsFailure.Should().BeTrue();
        SecretScopePath.From(new string('a', 100)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Root_IsSlash_WithNoSegments()
    {
        SecretScopePath.Root.Segments.Should().BeEmpty();
        SecretScopePath.Root.ToString().Should().Be("/");
    }

    [Fact]
    public void From_MultipleSegments_JoinsCanonically()
    {
        var result = SecretScopePath.From("nexus", "org-1", "app-2");

        result.IsSuccess.Should().BeTrue();
        result.Value.ToString().Should().Be("/nexus/org-1/app-2");
        result.Value.Segments.Should().ContainInOrder("nexus", "org-1", "app-2");
    }
}
