// -----------------------------------------------------------------------
// <copyright file="GeoErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Geo;

/// <summary>Standardized error definitions for geospatial operations.</summary>
public static class GeoErrors
{
    /// <summary>Gets the error code prefix for geo errors.</summary>
    public const string Prefix = "Geo";

    /// <summary>The address could not be resolved to coordinates.</summary>
    public static Error AddressNotFound(string address) =>
        Error.NotFound($"{Prefix}.AddressNotFound", $"No location found for address '{address}'.");

    /// <summary>No coordinates were provided, or the input list was empty.</summary>
    public static Error InvalidInput(string detail) =>
        Error.Validation($"{Prefix}.InvalidInput", detail);

    /// <summary>The required provider API key/credential was missing.</summary>
    public static Error MissingApiKey() =>
        Error.Validation($"{Prefix}.MissingApiKey", "A geospatial provider API key is required but was not configured.");

    /// <summary>The provider returned an error or an unexpected payload.</summary>
    public static Error ProviderError(string detail) =>
        Error.Failure($"{Prefix}.ProviderError", $"Geospatial provider error: {detail}");

    /// <summary>The provider throttled the request.</summary>
    public static Error Throttled() =>
        Error.TooManyRequests($"{Prefix}.Throttled", "Geospatial provider throttled the request. Please retry later.");
}
