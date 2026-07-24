// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Adapters.Scaleway.SecretManager.Configuration;
using Compendium.Adapters.Scaleway.SecretManager.Http;
using Compendium.Adapters.Scaleway.SecretManager.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compendium.Adapters.Scaleway.SecretManager.DependencyInjection;

/// <summary>
/// DI extensions for registering the Scaleway Secret Manager
/// <see cref="ISecretVault"/> adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Scaleway Secret Manager adapter. The facade is
    /// contributed to <c>IEnumerable&lt;ISecretVault&gt;</c> for
    /// provider-dispatch consumers and also resolvable as
    /// <see cref="ScalewaySecretVault"/> directly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback to configure adapter options.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddScalewaySecretVault(
        this IServiceCollection services, Action<ScalewaySecretManagerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<ScalewaySecretManagerOptions>();
        }

        services.AddHttpClient(ScalewayDefaults.HttpClientName);

        services.TryAddSingleton<ScalewayApiClient>();
        services.TryAddSingleton<ScalewaySecretContainerService>();
        services.TryAddSingleton<ScalewaySecretVersionService>();
        services.TryAddSingleton(sp => new ScalewaySecretVault(
            sp.GetRequiredService<ScalewaySecretContainerService>(),
            sp.GetRequiredService<ScalewaySecretVersionService>()));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretVault>(
            sp => sp.GetRequiredService<ScalewaySecretVault>()));

        return services;
    }
}
