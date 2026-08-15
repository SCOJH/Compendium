// -----------------------------------------------------------------------
// <copyright file="GeocodeResult.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Geo.Models;

/// <summary>The resolved location for an address (or reverse-geocoded coordinate).</summary>
/// <param name="Coordinate">The resolved coordinate.</param>
/// <param name="FormattedAddress">The provider's normalized, human-readable address.</param>
/// <param name="PlaceId">An optional provider place identifier for follow-up lookups.</param>
public sealed record GeocodeResult(
    GeoCoordinate Coordinate,
    string FormattedAddress,
    string? PlaceId = null);
