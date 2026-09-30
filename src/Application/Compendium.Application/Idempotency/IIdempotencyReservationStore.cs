// -----------------------------------------------------------------------
// <copyright file="IIdempotencyReservationStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Results;

namespace Compendium.Application.Idempotency;

/// <summary>
/// An <see cref="IIdempotencyStore"/> that can also reserve a key atomically, so that two
/// concurrent callers presenting the same key cannot both run the operation.
/// </summary>
/// <remarks>
/// A separate contract rather than a new member on <see cref="IIdempotencyStore"/>: adding a
/// member would break every existing implementation, and a default implementation built on
/// <see cref="IIdempotencyStore.ExistsAsync"/> then <see cref="IIdempotencyStore.SetAsync{TValue}"/>
/// could not be atomic — which is the whole point.
/// </remarks>
public interface IIdempotencyReservationStore : IIdempotencyStore
{
    /// <summary>
    /// Reserves <paramref name="key"/> if no live reservation holds it.
    /// </summary>
    /// <param name="key">The key to reserve.</param>
    /// <param name="expiration">How long the reservation lives.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when this caller won the reservation, <c>false</c> when a live reservation
    /// already existed, or a failure on store error.
    /// </returns>
    Task<Result<bool>> TryReserveAsync(string key, TimeSpan expiration, CancellationToken cancellationToken = default);
}
