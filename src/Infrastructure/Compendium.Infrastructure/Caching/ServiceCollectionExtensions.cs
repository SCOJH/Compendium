// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Caching;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compendium.Infrastructure.Caching;

/// <summary>
/// DI extension methods for registering the in-memory <see cref="ICache"/> adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="InMemoryCache"/> as the <see cref="ICache"/> implementation,
    /// backed by <see cref="IMemoryCache"/>. The underlying <see cref="IMemoryCache"/> is
    /// also registered (via <see cref="MemoryCacheServiceCollectionExtensions.AddMemoryCache(IServiceCollection)"/>)
    /// when no other registration exists.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for the underlying <see cref="MemoryCacheOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddInMemoryCache(
        this IServiceCollection services,
        Action<MemoryCacheOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is null)
        {
            services.AddMemoryCache();
        }
        else
        {
            services.AddMemoryCache(configure);
        }

        services.TryAddSingleton<ICache, InMemoryCache>();

        return services;
    }
}
