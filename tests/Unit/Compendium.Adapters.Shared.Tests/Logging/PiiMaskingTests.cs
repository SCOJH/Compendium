// -----------------------------------------------------------------------
// <copyright file="PiiMaskingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.Shared.Logging;
using AwesomeAssertions;

namespace Compendium.Adapters.Shared.Tests.Logging;

/// <summary>
/// Unit tests for the <see cref="PiiMasking"/> static helpers.
/// </summary>
public class PiiMaskingTests
{
    [Fact]
    public void MaskEmail_WhenNull_ReturnsEmptyPlaceholder()
    {
        // Arrange
        string? email = null;

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("<empty>");
    }

    [Fact]
    public void MaskEmail_WhenEmptyString_ReturnsEmptyPlaceholder()
    {
        // Arrange
        var email = string.Empty;

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("<empty>");
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void MaskEmail_WhenWhitespaceOnly_ReturnsEmptyPlaceholder(string email)
    {
        // Arrange / Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("<empty>");
    }

    [Theory]
    [InlineData("@acme.com")]
    [InlineData("@")]
    public void MaskEmail_WhenAtSignAtStartOrOnly_ReturnsTripleStar(string email)
    {
        // Arrange / Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("***");
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("plainstring")]
    [InlineData("john.doe")]
    public void MaskEmail_WhenNoAtSign_ReturnsTripleStar(string email)
    {
        // Arrange / Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("***");
    }

    [Fact]
    public void MaskEmail_WhenStandardEmail_MasksLocalPartExceptFirstChar()
    {
        // Arrange
        var email = "john.doe@acme.com";

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("j***@acme.com");
    }

    [Fact]
    public void MaskEmail_WhenSingleCharLocalPart_MasksToFirstCharThenStars()
    {
        // Arrange
        var email = "a@example.com";

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("a***@example.com");
    }

    [Fact]
    public void MaskEmail_WhenMultipleAtSigns_UsesFirstAtSignAsBoundary()
    {
        // Arrange
        var email = "user@inner@example.com";

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("u***@inner@example.com");
    }

    [Fact]
    public void MaskEmail_WhenEmailHasUnicode_PreservesFirstCharAndDomain()
    {
        // Arrange
        var email = "élise@éxample.com";

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("é***@éxample.com");
    }

    [Fact]
    public void MaskEmail_WhenLongLocalPart_StillReturnsFirstCharAndStars()
    {
        // Arrange
        var email = "verylongemailaddress@acme.com";

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("v***@acme.com");
        result.Should().NotContain("erylongemailaddress");
    }

    [Fact]
    public void MaskEmail_WhenSubdomainPresent_PreservesEntireDomain()
    {
        // Arrange
        var email = "alice@mail.acme.co.uk";

        // Act
        var result = PiiMasking.MaskEmail(email);

        // Assert
        result.Should().Be("a***@mail.acme.co.uk");
    }
}
