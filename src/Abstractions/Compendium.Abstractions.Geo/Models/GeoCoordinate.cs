// -----------------------------------------------------------------------
// <copyright file="GeoCoordinate.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Geo.Models;

/// <summary>A WGS-84 geographic coordinate.</summary>
/// <param name="Latitude">Latitude in decimal degrees (-90..90).</param>
/// <param name="Longitude">Longitude in decimal degrees (-180..180).</param>
public readonly record struct GeoCoordinate(double Latitude, double Longitude)
{
    /// <summary>Formats the coordinate as <c>lat,lng</c> (the form most map APIs accept).</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Latitude},{Longitude}");
}
