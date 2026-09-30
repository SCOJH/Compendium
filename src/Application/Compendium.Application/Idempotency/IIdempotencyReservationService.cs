// -----------------------------------------------------------------------
// <copyright file="IIdempotencyReservationService.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Core.Results;

namespace Compendium.Application.Idempotency;

/// <summary>
/// The reservation side of <see cref="IIdempotencyService"/>. <see cref="IdempotencyService"/>
/// implements both; <see cref="CQRS.Behaviors.IdempotencyBehavior{TRequest, TResponse}"/>
/// uses this one when the service it is given supports it.
/// </summary>
public interface IIdempotencyReservationService
{
    /// <summary>
    /// Atomically reserves <paramref name="idempotencyKey"/>, for the same lifetime as the
    /// results the service stores — a reservation that outlived its result would make every
    /// replay answer "in progress" until it expired.
    /// </summary>
    /// <param name="idempotencyKey">The idempotency key.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>
    /// <c>true</c> when this caller won, <c>false</c> when the key was already reserved, or a
    /// failure when the reservation could not be attempted (store error, or a store that
    /// does not implement <see cref="IIdempotencyReservationStore"/>).
    /// </returns>
    Task<Result<bool>> TryReserveAsync(string idempotencyKey, CancellationToken cancellationToken = default);
}
