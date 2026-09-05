// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Compendium.Infrastructure.Projections;

/// <summary>
/// Extension methods for configuring projection services in DI container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds projection management services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for projection options.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProjections(
        this IServiceCollection services,
        Action<ProjectionOptions>? configureOptions = null)
    {
        // Configure options
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.Configure<ProjectionOptions>(_ => { });
        }

        // Register projection services
        services.AddSingleton<IProjectionManager, EnhancedProjectionManager>();
        services.AddSingleton<ILiveProjectionProcessor, LiveProjectionProcessor>();

        // Single-process lease by default: correct for one host, and the behaviour every
        // existing caller already had. A multi-replica deployment registers its own
        // implementation (a database-backed lock) BEFORE calling this method — TryAdd
        // leaves it in place.
        services.TryAddSingleton<IProjectionConsumerLease, SingleProcessProjectionConsumerLease>();

        // Register as hosted service for automatic startup
        services.AddSingleton<IHostedService>(provider =>
            (IHostedService)provider.GetRequiredService<ILiveProjectionProcessor>());

        return services;
    }

    /// <summary>
    /// Adds PostgreSQL projection store to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPostgreSqlProjections(this IServiceCollection services)
    {
        // TODO: Add proper PostgreSQL projection store reference
        // services.AddSingleton<IProjectionStore, PostgreSqlProjectionStore>();
        return services;
    }

    /// <summary>
    /// Registers a projection for processing. The projection is also registered as a
    /// singleton in the DI container if it has not been registered already; consumers
    /// that need a custom factory (e.g. to inject a connection string) should call
    /// <c>services.AddSingleton&lt;TProjection&gt;(sp =&gt; ...)</c> explicitly first and
    /// then call this method.
    /// </summary>
    /// <typeparam name="TProjection">The type of projection to register.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddProjection<TProjection>(this IServiceCollection services)
        where TProjection : class, IProjection
    {
        services.TryAddSingleton<TProjection>();

        // Register projection with the manager during startup
        services.Configure<ProjectionRegistrationOptions>(options =>
        {
            options.ProjectionTypes.Add(typeof(TProjection));
        });

        return services;
    }
}

/// <summary>
/// Options for projection registration.
/// </summary>
internal class ProjectionRegistrationOptions
{
    public List<Type> ProjectionTypes { get; } = new();
}

/// <summary>
/// Hosted service that registers projections on startup.
/// </summary>
internal class ProjectionRegistrationService : IHostedService
{
    private readonly IProjectionManager _projectionManager;
    private readonly ILiveProjectionProcessor _liveProcessor;
    private readonly ProjectionRegistrationOptions _options;

    public ProjectionRegistrationService(
        IProjectionManager projectionManager,
        ILiveProjectionProcessor liveProcessor,
        Microsoft.Extensions.Options.IOptions<ProjectionRegistrationOptions> options)
    {
        _projectionManager = projectionManager;
        _liveProcessor = liveProcessor;
        _options = options.Value;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var projectionType in _options.ProjectionTypes)
        {
            var registerMethod = typeof(IProjectionManager)
                .GetMethod(nameof(IProjectionManager.RegisterProjection))!
                .MakeGenericMethod(projectionType);

            registerMethod.Invoke(_projectionManager, null);

            var liveRegisterMethod = typeof(ILiveProjectionProcessor)
                .GetMethod(nameof(ILiveProjectionProcessor.RegisterProjection))!
                .MakeGenericMethod(projectionType);

            liveRegisterMethod.Invoke(_liveProcessor, null);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
