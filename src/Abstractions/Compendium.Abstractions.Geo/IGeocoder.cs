// -----------------------------------------------------------------------
// <copyright file="IGeocoder.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Geo.Models;

namespace Compendium.Abstractions.Geo;

/// <summary>
/// Provider-agnostic geocoding: resolves a postal address to coordinates and back.
/// </summary>
public interface IGeocoder
{
    /// <summary>
    /// Resolves a free-form address to geographic coordinates.
    /// </summary>
    /// <param name="address">The address or place to geocode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The best match, or <see cref="GeoErrors.AddressNotFound"/> when none is found.</returns>
    Task<Result<GeocodeResult>> GeocodeAsync(string address, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves geographic coordinates to the nearest postal address.
    /// </summary>
    /// <param name="coordinate">The coordinate to reverse-geocode.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The best match, or an error.</returns>
    Task<Result<GeocodeResult>> ReverseGeocodeAsync(
        GeoCoordinate coordinate,
        CancellationToken cancellationToken = default);
}
