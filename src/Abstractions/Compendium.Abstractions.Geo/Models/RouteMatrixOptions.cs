// -----------------------------------------------------------------------
// <copyright file="RouteMatrixOptions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Geo.Models;

/// <summary>Options governing a route-matrix computation.</summary>
/// <param name="Mode">The travel mode. Defaults to <see cref="TravelMode.Driving"/>.</param>
/// <param name="AvoidTolls">When <see langword="true"/>, prefer routes without toll roads.</param>
/// <param name="AvoidHighways">When <see langword="true"/>, prefer routes without highways.</param>
public sealed record RouteMatrixOptions(
    TravelMode Mode = TravelMode.Driving,
    bool AvoidTolls = false,
    bool AvoidHighways = false)
{
    /// <summary>The default options (driving, no avoidances).</summary>
    public static RouteMatrixOptions Default { get; } = new();
}
