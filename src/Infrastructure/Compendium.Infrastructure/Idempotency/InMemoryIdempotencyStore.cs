// -----------------------------------------------------------------------
// <copyright file="InMemoryIdempotencyStore.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Compendium.Application.Idempotency;
using Compendium.Core.Results;

namespace Compendium.Infrastructure.Idempotency;

/// <summary>
/// In-memory implementation of <see cref="IIdempotencyStore"/> for testing
/// and InMemory-backed framework E2E scenarios. Thread-safe; expiring entries
/// are pruned lazily on read.
/// </summary>
/// <remarks>
/// Semantic contract: matches <c>RedisIdempotencyStore</c>. Each entry has a
/// TTL set via <see cref="SetAsync{TValue}"/>; entries that have outlived
/// their TTL are removed on the next access (lazy eviction).
/// </remarks>
public sealed class InMemoryIdempotencyStore : IIdempotencyReservationStore
{
    private readonly ConcurrentDictionary<string, Entry> _store = new();

    /// <inheritdoc />
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var exists = TryGetLive(key, out _);
        return Task.FromResult(Result.Success(exists));
    }

    /// <inheritdoc />
    public Task<Result<TResult?>> GetAsync<TResult>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (TryGetLive(key, out var entry) && entry.Value is TResult typed)
        {
            return Task.FromResult(Result.Success<TResult?>(typed));
        }

        return Task.FromResult(Result.Success<TResult?>(default));
    }

    /// <inheritdoc />
    public Task<Result> SetAsync<TValue>(
        string key,
        TValue value,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (expiration <= TimeSpan.Zero)
        {
            return Task.FromResult(Result.Failure(
                Error.Validation("Idempotency.InvalidExpiration", "Expiration must be positive.")));
        }

        var expiresAt = DateTimeOffset.UtcNow.Add(expiration);
        _store[key] = new Entry(value, expiresAt);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<bool>> TryReserveAsync(
        string key,
        TimeSpan expiration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (expiration <= TimeSpan.Zero)
        {
            return Task.FromResult(Result.Failure<bool>(
                Error.Validation("Idempotency.InvalidExpiration", "Expiration must be positive.")));
        }

        var candidate = new Entry(ReservationMarker, DateTimeOffset.UtcNow.Add(expiration));

        // TryAdd / TryUpdate only: a read followed by a write would let two callers both
        // see the key free and both "win". An expired holder is replaced with a
        // compare-and-swap against the exact entry we read, so a concurrent winner that
        // replaced it first makes our swap fail and we loop to read it.
        while (true)
        {
            if (_store.TryAdd(key, candidate))
            {
                return Task.FromResult(Result.Success(true));
            }

            if (!_store.TryGetValue(key, out var holder))
            {
                continue;
            }

            if (holder.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return Task.FromResult(Result.Success(false));
            }

            if (_store.TryUpdate(key, candidate, holder))
            {
                return Task.FromResult(Result.Success(true));
            }
        }
    }

    /// <summary>Clears all entries. Test-only helper.</summary>
    public void Clear() => _store.Clear();

    private bool TryGetLive(string key, out Entry entry)
    {
        if (_store.TryGetValue(key, out var found))
        {
            if (found.ExpiresAt > DateTimeOffset.UtcNow)
            {
                entry = found;
                return true;
            }

            // Remove THIS expired entry only: a plain TryRemove(key) could delete an entry a
            // concurrent caller wrote between our read and this line — a live reservation.
            _store.TryRemove(KeyValuePair.Create(key, found));
        }

        entry = default!;
        return false;
    }

    private static readonly object ReservationMarker = new();

    private sealed record Entry(object? Value, DateTimeOffset ExpiresAt);
}
