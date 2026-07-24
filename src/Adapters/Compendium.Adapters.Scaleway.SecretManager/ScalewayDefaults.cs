// -----------------------------------------------------------------------
// <copyright file="ScalewayDefaults.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.Scaleway.SecretManager;

/// <summary>
/// Shared constants for the Scaleway Secret Manager adapter: the provider
/// discriminator, the named <see cref="System.Net.Http.HttpClient"/>, the API
/// base and the provider's per-version payload limit.
/// </summary>
internal static class ScalewayDefaults
{
    /// <summary>The provider discriminator (matches <see cref="Compendium.Abstractions.Secrets.ISecretVault.Provider"/>).</summary>
    public const string Provider = "scaleway";

    /// <summary>The named HttpClient used for Secret Manager REST calls.</summary>
    public const string HttpClientName = "compendium-scaleway-secret-manager";

    /// <summary>The authentication header carrying the IAM API secret key.</summary>
    public const string AuthHeader = "X-Auth-Token";

    /// <summary>The Secret Manager per-version payload limit, in bytes (64 KiB).</summary>
    public const int MaxPayloadBytes = 64 * 1024;

    /// <summary>The api.scaleway.com base URL used when no override is configured.</summary>
    public static readonly Uri DefaultApiBase = new("https://api.scaleway.com");
}
