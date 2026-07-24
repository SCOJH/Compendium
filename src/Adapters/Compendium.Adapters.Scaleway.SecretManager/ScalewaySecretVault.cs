// -----------------------------------------------------------------------
// <copyright file="ScalewaySecretVault.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Abstractions.Secrets.Services;
using Compendium.Adapters.Scaleway.SecretManager.Services;

namespace Compendium.Adapters.Scaleway.SecretManager;

/// <summary>
/// The Scaleway Secret Manager <see cref="ISecretVault"/>: a stateless
/// singleton facade over the regional v1beta1 API. See CAPABILITIES.md for
/// the declared matrix and its limitations.
/// </summary>
public sealed class ScalewaySecretVault : ISecretVault
{
    private readonly ScalewaySecretContainerService _secrets;
    private readonly ScalewaySecretVersionService _versions;

    internal ScalewaySecretVault(
        ScalewaySecretContainerService secrets, ScalewaySecretVersionService versions)
    {
        _secrets = secrets;
        _versions = versions;
    }

    /// <inheritdoc />
    public string Provider => ScalewayDefaults.Provider;

    /// <inheritdoc />
    public SecretVaultCapabilities Capabilities { get; } = new()
    {
        Provider = ScalewayDefaults.Provider,
        Entries = new Dictionary<SecretVaultCapability, SecretVaultCapabilitySupport>
        {
            [SecretVaultCapability.ImmutableVersions] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.VersionEnableDisable] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.VersionDestroy] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.PathHierarchy] = new(
                SecretVaultCapabilityLevel.Full,
                "Prefix listing is filtered adapter-side over the tenancy's secrets."),
            [SecretVaultCapability.Tags] = new(
                SecretVaultCapabilityLevel.Partial,
                "Key/value tags are encoded as 'key:value' strings on the provider's tag list."),
            [SecretVaultCapability.LargePayload] = new(
                SecretVaultCapabilityLevel.None,
                "Secret Manager limits a version payload to 64 KiB."),
            [SecretVaultCapability.EphemeralSecrets] = new(
                SecretVaultCapabilityLevel.None,
                "Supported by the provider but not exposed through the v1 ports."),
            [SecretVaultCapability.ServerSideRotation] = new(SecretVaultCapabilityLevel.None),
        },
    };

    /// <inheritdoc />
    public ISecretContainerService Secrets => _secrets;

    /// <inheritdoc />
    public ISecretVersionService Versions => _versions;
}
