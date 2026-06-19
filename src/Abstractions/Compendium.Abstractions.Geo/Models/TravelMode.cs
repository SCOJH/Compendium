// -----------------------------------------------------------------------
// <copyright file="TravelMode.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Geo.Models;

/// <summary>The mode of travel used when computing distances and durations.</summary>
public enum TravelMode
{
    /// <summary>By car / motor vehicle.</summary>
    Driving,

    /// <summary>On foot.</summary>
    Walking,

    /// <summary>By bicycle.</summary>
    Bicycling,

    /// <summary>By public transit.</summary>
    Transit,
}
