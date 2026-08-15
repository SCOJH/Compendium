// -----------------------------------------------------------------------
// <copyright file="GitHubSecretSealer.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;

namespace Compendium.Adapters.GitHub.Security;

/// <summary>
/// Encrypts CI secret values for GitHub Actions using a libsodium sealed box
/// (crypto_box_seal) against the target's Actions public key — the exact scheme
/// GitHub requires for repository, organization, and environment secrets.
/// Secrets are write-only on GitHub: this only ever encrypts, never decrypts.
/// </summary>
internal sealed class GitHubSecretSealer
{
    /// <summary>
    /// Seals <paramref name="secretValue"/> against a base64 Actions public key,
    /// returning the base64 ciphertext to place in the <c>encrypted_value</c>
    /// field of a set-secret request.
    /// </summary>
    /// <param name="secretValue">The plaintext secret value.</param>
    /// <param name="base64PublicKey">The target's Actions public key (base64).</param>
    /// <returns>The base64-encoded sealed box.</returns>
    public string Seal(string secretValue, string base64PublicKey)
    {
        ArgumentNullException.ThrowIfNull(secretValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(base64PublicKey);

        var publicKeyBytes = Convert.FromBase64String(base64PublicKey);
        var secretBytes = Encoding.UTF8.GetBytes(secretValue);
        var sealedBytes = Sodium.SealedPublicKeyBox.Create(secretBytes, publicKeyBytes);
        return Convert.ToBase64String(sealedBytes);
    }
}
