// -----------------------------------------------------------------------
// <copyright file="GitServerCapabilities.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Capabilities;

/// <summary>
/// The support level an adapter declares for a <see cref="GitCapability"/>.
/// </summary>
public enum GitCapabilityLevel
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
/// is <see cref="GitCapabilityLevel.Partial"/> or <see cref="GitCapabilityLevel.None"/>
/// for a capability the provider conceptually has but the adapter cannot reach
/// (e.g. "github.com org creation requires an enterprise-owner user token").
/// </param>
public sealed record GitCapabilitySupport(GitCapabilityLevel Level, string? Limitation = null);

/// <summary>
/// The declarative capability matrix of a git-server adapter. Consumers use it
/// to drive UI affordances (hide or disable unsupported features with a reason)
/// and adapters use <see cref="EnsureSupported"/> to fail uniformly.
/// </summary>
public sealed record GitServerCapabilities
{
    /// <summary>
    /// Gets the provider identifier these capabilities describe (matches
    /// <see cref="IGitServer.Provider"/>).
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the declared support per capability. Capabilities absent from the
    /// dictionary are treated as <see cref="GitCapabilityLevel.None"/>.
    /// </summary>
    public required IReadOnlyDictionary<GitCapability, GitCapabilitySupport> Entries { get; init; }

    /// <summary>
    /// Returns whether the capability is available at any level
    /// (<see cref="GitCapabilityLevel.Partial"/> or <see cref="GitCapabilityLevel.Full"/>).
    /// </summary>
    public bool Supports(GitCapability capability) =>
        Entries.TryGetValue(capability, out var support) && support.Level != GitCapabilityLevel.None;

    /// <summary>
    /// Returns success when the capability is available, otherwise the standard
    /// <c>Git.CapabilityNotSupported</c> failure. Adapters call this at the top
    /// of optional-capability methods so every provider fails identically.
    /// </summary>
    public Result EnsureSupported(GitCapability capability) =>
        Supports(capability)
            ? Result.Success()
            : Result.Failure(GitErrors.NotSupported(
                Provider,
                capability,
                Entries.TryGetValue(capability, out var support) ? support.Limitation : null));
}
