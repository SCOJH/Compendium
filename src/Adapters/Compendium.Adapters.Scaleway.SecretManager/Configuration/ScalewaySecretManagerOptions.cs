// -----------------------------------------------------------------------
// <copyright file="ScalewaySecretManagerOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Scaleway.SecretManager.Configuration;

/// <summary>
/// Adapter options. Deliberately credential-free: the IAM secret key travels
/// in the <see cref="Compendium.Abstractions.Secrets.Connections.SecretVaultConnection"/>
/// passed to every call, so one adapter instance serves any number of
/// tenancies and no secret lives in host configuration.
/// </summary>
public sealed class ScalewaySecretManagerOptions
{
    /// <summary>
    /// Gets or sets the API base URL. Override for testing against a stub.
    /// </summary>
    public Uri ApiBaseUrl { get; set; } = ScalewayDefaults.DefaultApiBase;

    /// <summary>
    /// Gets or sets the region used when the connection does not specify one
    /// (e.g. <c>"fr-par"</c>, <c>"nl-ams"</c>, <c>"pl-waw"</c>).
    /// </summary>
    public string DefaultRegion { get; set; } = "fr-par";

    /// <summary>
    /// Gets or sets the Scaleway project id used when the connection does not
    /// specify a tenancy.
    /// </summary>
    public string? DefaultProjectId { get; set; }
}
