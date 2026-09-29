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

    /// <summary>
    /// Gets the partition the key lives in — in a multi-tenant application, the tenant.
    /// </summary>
    /// <remarks>
    /// Keys are chosen by callers, so two tenants will eventually send the same one. Without
    /// a scope they would share one slot: the second would receive the first one's recorded
    /// response, data included, and its own command would never run. A multi-tenant
    /// application <b>must</b> return the tenant identifier here. Returning <c>null</c> is a
    /// deliberate statement that every caller of the command belongs to one trust domain —
    /// which is why the member has no default: forgetting it must not compile.
    /// </remarks>
    string? IdempotencyScope { get; }
}
