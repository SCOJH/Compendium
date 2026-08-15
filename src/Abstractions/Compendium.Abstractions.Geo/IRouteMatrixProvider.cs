// -----------------------------------------------------------------------
// <copyright file="IRouteMatrixProvider.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Geo.Models;

namespace Compendium.Abstractions.Geo;

/// <summary>
/// Provider-agnostic route matrix: real travel distance and duration for every
/// origin × destination pair (the input a route optimizer needs to plan real routes,
/// as opposed to straight-line distance).
/// </summary>
public interface IRouteMatrixProvider
{
    /// <summary>
    /// Computes the travel distance and duration between every origin and destination.
    /// </summary>
    /// <param name="origins">The origin coordinates.</param>
    /// <param name="destinations">The destination coordinates.</param>
    /// <param name="options">Travel options (mode, avoidances). Defaults to driving.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An <see cref="RouteMatrix"/> of <c>origins.Count × destinations.Count</c> elements, or an error.</returns>
    Task<Result<RouteMatrix>> GetMatrixAsync(
        IReadOnlyList<GeoCoordinate> origins,
        IReadOnlyList<GeoCoordinate> destinations,
        RouteMatrixOptions? options = null,
        CancellationToken cancellationToken = default);
}
