// -----------------------------------------------------------------------
// <copyright file="IIdempotentRequest.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Application.Idempotency;

/// <summary>
/// A request whose idempotency key is chosen by the caller (for instance from an HTTP
/// <c>Idempotency-Key</c> header) rather than derived from its content.
/// </summary>
/// <remarks>
/// A content hash cannot tell a retry from a second, legitimate, identical request — two
/// deployments of the same tag are both real. Only the caller knows which one it is sending.
/// A <c>null</c> or blank key means "no deduplication": the request runs, every time.
/// </remarks>
public interface IIdempotentRequest
{
    /// <summary>Gets the caller-supplied idempotency key, or <c>null</c> for none.</summary>
    string? IdempotencyKey { get; }
}
