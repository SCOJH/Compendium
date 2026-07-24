// -----------------------------------------------------------------------
// <copyright file="SecretVaultCredential.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Connections;

/// <summary>
/// The credential material of a <see cref="SecretVaultConnection"/>, as a
/// closed union. Token-bearing members redact their material in
/// <c>ToString()</c> so a logged connection never leaks a secret.
/// </summary>
public abstract record SecretVaultCredential
{
    private SecretVaultCredential()
    {
    }

    /// <summary>
    /// A durable API token or key (Scaleway IAM API key secret, Vault token,
    /// cloud key-vault access key).
    /// </summary>
    /// <param name="Token">The token material. Redacted in <c>ToString()</c>.</param>
    public sealed record ApiToken(string Token) : SecretVaultCredential
    {
        /// <inheritdoc />
        public override string ToString() => "ApiToken(***)";
    }

    /// <summary>
    /// No credential material: for in-memory and null vaults, or adapters that
    /// authenticate ambiently (workload identity, instance profile).
    /// </summary>
    public sealed record None : SecretVaultCredential;
}
