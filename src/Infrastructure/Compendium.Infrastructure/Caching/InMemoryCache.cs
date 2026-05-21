// -----------------------------------------------------------------------
// <copyright file="InMemoryCache.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Caching;
using Compendium.Multitenancy;
using Microsoft.Extensions.Caching.Memory;

namespace Compendium.Infrastructure.Caching;

/// <summary>
/// In-memory implementation of <see cref="ICache"/> backed by
/// <see cref="IMemoryCache"/>. Suitable for single-process scenarios, tests,
/// and framework E2E samples. For multi-instance deployments, use a distributed
/// adapter (e.g. <c>Compendium.Adapters.Redis</c>).
/// </summary>
/// <remarks>
/// <para><b>Tenant isolation</b>. When an <see cref="ITenantContext"/> with a non-empty
/// <see cref="ITenantContext.TenantId"/> is resolved from DI, every key is prefixed
/// with <c>{tenantId}:</c> before being stored. With no tenant context (null or empty
/// <c>TenantId</c>), keys are written verbatim. The prefix is applied transparently
/// — callers always see the original key.</para>
/// <para><b>Thread safety</b>. <see cref="IMemoryCache"/> is thread-safe, so this
/// adapter is also thread-safe.</para>
/// <para><b>Result contract</b>. Cache-miss is a successful <see cref="Result{T}"/>
/// with a <c>null</c> value, never a failure. Argument validation (null/whitespace
/// key) is permitted to throw <see cref="ArgumentException"/>.</para>
/// </remarks>
public sealed class InMemoryCache : ICache
{
    private readonly IMemoryCache _memoryCache;
    private readonly ITenantContext? _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryCache"/> class.
    /// </summary>
    /// <param name="memoryCache">The underlying <see cref="IMemoryCache"/>.</param>
    /// <param name="tenantContext">Optional tenant context used to scope keys per tenant.</param>
    public InMemoryCache(IMemoryCache memoryCache, ITenantContext? tenantContext = null)
    {
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public Task<Result<T?>> GetAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var scopedKey = ScopeKey(key);

        if (_memoryCache.TryGetValue(scopedKey, out var raw) && raw is T typed)
        {
            return Task.FromResult(Result.Success<T?>(typed));
        }

        return Task.FromResult(Result.Success<T?>(default));
    }

    /// <inheritdoc />
    public Task<Result> SetAsync<T>(
        string key,
        T value,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (ttl.HasValue && ttl.Value <= TimeSpan.Zero)
        {
            return Task.FromResult(Result.Failure(CachingErrors.InvalidTtl(ttl.Value)));
        }

        var options = new MemoryCacheEntryOptions();
        if (ttl.HasValue)
        {
            options.AbsoluteExpirationRelativeToNow = ttl.Value;
        }

        _memoryCache.Set(ScopeKey(key), value, options);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _memoryCache.Remove(ScopeKey(key));
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        var exists = _memoryCache.TryGetValue(ScopeKey(key), out _);
        return Task.FromResult(Result.Success(exists));
    }

    /// <summary>
    /// Applies the active tenant's prefix to <paramref name="key"/>, or returns it unchanged
    /// when no tenant context is bound.
    /// </summary>
    private string ScopeKey(string key)
    {
        var tenantId = _tenantContext?.TenantId;
        return string.IsNullOrEmpty(tenantId) ? key : $"{tenantId}:{key}";
    }
}
