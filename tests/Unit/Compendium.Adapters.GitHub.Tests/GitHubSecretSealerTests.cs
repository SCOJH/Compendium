// -----------------------------------------------------------------------
// <copyright file="GitHubSecretSealerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Compendium.Adapters.GitHub.Security;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubSecretSealerTests
{
    private readonly GitHubSecretSealer _sealer = new();

    [Fact]
    public void Seal_ProducesCiphertext_ThatOpensBackToThePlaintext()
    {
        // Arrange: a fixed libsodium keypair, as GitHub's Actions public key would be.
        var keyPair = Sodium.PublicKeyBox.GenerateKeyPair();
        var publicKeyBase64 = Convert.ToBase64String(keyPair.PublicKey);
        const string plaintext = "s3cret-value";

        // Act
        var sealedBase64 = _sealer.Seal(plaintext, publicKeyBase64);

        // Assert: only the keypair holder can recover the value.
        var opened = Sodium.SealedPublicKeyBox.Open(
            Convert.FromBase64String(sealedBase64), keyPair.PrivateKey, keyPair.PublicKey);
        Encoding.UTF8.GetString(opened).Should().Be(plaintext);
    }

    [Fact]
    public void Seal_IsNonDeterministic_ButAlwaysDecryptsToTheSameValue()
    {
        var keyPair = Sodium.PublicKeyBox.GenerateKeyPair();
        var publicKeyBase64 = Convert.ToBase64String(keyPair.PublicKey);

        var first = _sealer.Seal("value", publicKeyBase64);
        var second = _sealer.Seal("value", publicKeyBase64);

        first.Should().NotBe(second, "sealed boxes use an ephemeral key, so ciphertext varies");
        Encoding.UTF8.GetString(Sodium.SealedPublicKeyBox.Open(
            Convert.FromBase64String(second), keyPair.PrivateKey, keyPair.PublicKey)).Should().Be("value");
    }

    [Fact]
    public void Seal_RejectsAMissingPublicKey()
    {
        var act = () => _sealer.Seal("value", " ");
        act.Should().Throw<ArgumentException>();
    }
}
