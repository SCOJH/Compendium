// -----------------------------------------------------------------------
// <copyright file="SecretMaterialTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Model;
using FluentAssertions;
using Xunit;

namespace Compendium.Abstractions.Secrets.Tests;

/// <summary>
/// Byte/string round-trips and copy semantics of <see cref="SecretMaterial"/>.
/// </summary>
public sealed class SecretMaterialTests
{
    [Fact]
    public void FromBytes_CopiesTheSource_SoLaterMutationDoesNotAlterTheMaterial()
    {
        var source = new byte[] { 1, 2, 3 };
        var material = SecretMaterial.FromBytes(source);

        source[0] = 99;

        material.Data.ToArray().Should().ContainInOrder((byte)1, (byte)2, (byte)3);
        material.Length.Should().Be(3);
    }

    [Fact]
    public void FromString_RoundTripsUtf8()
    {
        var material = SecretMaterial.FromString("héllo wörld");

        material.AsString().Should().Be("héllo wörld");
        material.Length.Should().BeGreaterThan(11, "non-ASCII chars take multiple UTF-8 bytes");
    }
}
