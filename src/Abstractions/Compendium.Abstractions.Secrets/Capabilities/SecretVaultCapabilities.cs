// -----------------------------------------------------------------------
// <copyright file="SecretVaultCapabilities.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Capabilities;

/// <summary>
/// The support level an adapter declares for a <see cref="SecretVaultCapability"/>.
/// </summary>
public enum SecretVaultCapabilityLevel
{
    /// <summary>The capability is not available on this provider.</summary>
    None,

    /// <summary>The capability is available with limitations (documented in the adapter's CAPABILITIES.md).</summary>
    Partial,

    /// <summary>The capability is fully supported.</summary>
    Full,
}

/// <summary>
/// An adapter's declared support for a single capability.
/// </summary>
/// <param name="Level">The support level.</param>
/// <param name="Limitation">
/// A short human-readable limitation note, required when <paramref name="Level"/>
/// is <see cref="SecretVaultCapabilityLevel.Partial"/> or when a conceptually
/// available capability is declared <see cref="SecretVaultCapabilityLevel.None"/>
/// for adapter-specific reasons.
/// </param>
public sealed record SecretVaultCapabilitySupport(SecretVaultCapabilityLevel Level, string? Limitation = null);

/// <summary>
/// The declarative capability matrix of a secret-vault adapter. Consumers use
/// it to drive UI affordances (hide or disable unsupported features with a
/// reason) and adapters use <see cref="EnsureSupported"/> to fail uniformly.
/// </summary>
public sealed record SecretVaultCapabilities
{
    /// <summary>
    /// Gets the provider identifier these capabilities describe (matches
    /// <see cref="ISecretVault.Provider"/>).
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the declared support per capability. Capabilities absent from the
    /// dictionary are treated as <see cref="SecretVaultCapabilityLevel.None"/>.
    /// </summary>
    public required IReadOnlyDictionary<SecretVaultCapability, SecretVaultCapabilitySupport> Entries { get; init; }

    /// <summary>
    /// Returns whether the capability is available at any level
    /// (<see cref="SecretVaultCapabilityLevel.Partial"/> or <see cref="SecretVaultCapabilityLevel.Full"/>).
    /// </summary>
    public bool Supports(SecretVaultCapability capability) =>
        Entries.TryGetValue(capability, out var support) && support.Level != SecretVaultCapabilityLevel.None;

    /// <summary>
    /// Returns success when the capability is available, otherwise the standard
    /// <c>SecretVault.CapabilityNotSupported</c> failure. Adapters call this at
    /// the top of optional-capability methods so every provider fails identically.
    /// </summary>
    public Result EnsureSupported(SecretVaultCapability capability) =>
        Supports(capability)
            ? Result.Success()
            : Result.Failure(SecretVaultErrors.NotSupported(
                Provider,
                capability,
                Entries.TryGetValue(capability, out var support) ? support.Limitation : null));
}
