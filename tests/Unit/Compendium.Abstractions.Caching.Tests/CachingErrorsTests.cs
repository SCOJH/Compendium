// -----------------------------------------------------------------------
// <copyright file="CachingErrorsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Caching.Tests;

public class CachingErrorsTests
{
    [Fact]
    public void Prefix_IsCaching()
    {
        CachingErrors.Prefix.Should().Be("Caching");
    }

    [Fact]
    public void InvalidTtl_ReturnsValidationErrorIncludingTtl()
    {
        // Arrange
        var ttl = TimeSpan.FromSeconds(-5);

        // Act
        var error = CachingErrors.InvalidTtl(ttl);

        // Assert
        error.Code.Should().Be("Caching.InvalidTtl");
        error.Type.Should().Be(ErrorType.Validation);
        error.Message.Should().Contain(ttl.ToString());
    }

    [Fact]
    public void BackendFailure_ReturnsFailureWithMessage()
    {
        // Act
        var error = CachingErrors.BackendFailure("Redis unreachable");

        // Assert
        error.Code.Should().Be("Caching.BackendFailure");
        error.Type.Should().Be(ErrorType.Failure);
        error.Message.Should().Be("Redis unreachable");
    }
}
