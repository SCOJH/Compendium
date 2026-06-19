// -----------------------------------------------------------------------
// <copyright file="RouteMatrix.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Geo.Models;

/// <summary>The travel cost between one origin and one destination.</summary>
/// <param name="OriginIndex">Index into the origins list.</param>
/// <param name="DestinationIndex">Index into the destinations list.</param>
/// <param name="DistanceMeters">Travel distance in meters.</param>
/// <param name="DurationSeconds">Travel duration in seconds.</param>
/// <param name="Reachable">Whether a route exists for this pair.</param>
public sealed record RouteMatrixElement(
    int OriginIndex,
    int DestinationIndex,
    double DistanceMeters,
    double DurationSeconds,
    bool Reachable = true);

/// <summary>
/// A dense matrix of travel costs for every origin × destination pair, returned by
/// <see cref="IRouteMatrixProvider"/>.
/// </summary>
public sealed record RouteMatrix(
    int OriginCount,
    int DestinationCount,
    IReadOnlyList<RouteMatrixElement> Elements)
{
    /// <summary>
    /// Returns the element for the given origin/destination indices, or <see langword="null"/>
    /// when out of range.
    /// </summary>
    public RouteMatrixElement? Get(int originIndex, int destinationIndex)
    {
        foreach (var element in Elements)
        {
            if (element.OriginIndex == originIndex && element.DestinationIndex == destinationIndex)
            {
                return element;
            }
        }

        return null;
    }
}
