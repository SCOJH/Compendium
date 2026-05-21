// -----------------------------------------------------------------------
// <copyright file="ICache.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Caching;

/// <summary>
/// Provides provider-agnostic key/value caching operations with TTL.
/// Implementations may target backends such as in-memory dictionaries, Redis, Memcached,
/// or distributed caches behind <c>IDistributedCache</c>.
/// </summary>
/// <remarks>
/// <para>
/// Implementations should never throw for ordinary cache misses or transient backend failures;
/// they MUST surface failures via <see cref="Result"/> instead. Argument-validation errors
/// (null / whitespace key) are permitted to throw.
/// </para>
/// <para>
/// Tenant isolation, key namespacing, and serialization are implementation concerns and
/// MUST be transparent to callers.
/// </para>
/// </remarks>
public interface ICache
{
    /// <summary>
    /// Retrieves the cached value at <paramref name="key"/>, or <c>null</c> when no
    /// live entry exists. A missing key is a successful result with a <c>null</c> value,
    /// not a failure.
    /// </summary>
    /// <typeparam name="T">The expected value type.</typeparam>
    /// <param name="key">The cache key. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing the cached value (or <c>null</c>), or an error.</returns>
    Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores <paramref name="value"/> at <paramref name="key"/>, replacing any existing entry.
    /// </summary>
    /// <typeparam name="T">The value type.</typeparam>
    /// <param name="key">The cache key. Must be non-null and non-whitespace.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="ttl">Optional absolute time-to-live. When <c>null</c>, the entry has no expiry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or an error.</returns>
    Task<Result> SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the entry at <paramref name="key"/>. Removing a missing key is a no-op success.
    /// </summary>
    /// <param name="key">The cache key. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result indicating success or an error.</returns>
    Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Determines whether a live entry exists at <paramref name="key"/>.
    /// </summary>
    /// <param name="key">The cache key. Must be non-null and non-whitespace.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A result containing <c>true</c> when an entry exists, otherwise <c>false</c>; or an error.</returns>
    Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default);
}
