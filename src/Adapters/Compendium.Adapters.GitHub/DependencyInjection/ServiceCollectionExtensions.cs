// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensions.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Auth;
using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Http;
using Compendium.Adapters.GitHub.Security;
using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Compendium.Adapters.GitHub.DependencyInjection;

/// <summary>
/// DI extensions for registering the GitHub <see cref="IGitServer"/> adapter.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GitHub git-server adapter and its concern-scoped ports. The
    /// facade is contributed to <c>IEnumerable&lt;IGitServer&gt;</c> for
    /// provider-dispatch consumers, and each port is also resolvable directly for
    /// single-concern consumers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">An optional callback to configure adapter options.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddGitHubGitServer(
        this IServiceCollection services, Action<GitHubAdapterOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }
        else
        {
            services.AddOptions<GitHubAdapterOptions>();
        }

        // A named HttpClient backs both installation-token minting and the raw REST executor.
        services.AddHttpClient(GitHubDefaults.HttpClientName);

        services.TryAddSingleton<GitHubSecretSealer>();
        services.TryAddSingleton<GitHubAppTokenService>();
        services.TryAddSingleton<GitHubRestExecutor>();
        services.TryAddSingleton<GitHubCredentialBroker>();
        services.TryAddSingleton<GitHubClientProvider>();
        services.TryAddSingleton<IGitHubClientProvider>(sp => sp.GetRequiredService<GitHubClientProvider>());
        services.TryAddSingleton<GitHubRepositoryService>();
        services.TryAddSingleton<GitHubPipelineService>();
        services.TryAddSingleton<GitHubCiConfigurationService>();
        services.TryAddSingleton<GitHubEnvironmentService>();
        services.TryAddSingleton<GitHubBranchPolicyService>();
        services.TryAddSingleton<GitHubAccessControlService>();
        services.TryAddSingleton<GitHubWebhookService>();
        services.TryAddSingleton<GitHubWebhookIngestor>();
        services.TryAddSingleton<GitHubNamespaceProvisioner>();
        services.TryAddSingleton<GitHubGitServer>();

        // The facade participates in provider dispatch.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IGitServer, GitHubGitServer>(
            sp => sp.GetRequiredService<GitHubGitServer>()));

        // Each concern-scoped port is resolvable on its own for single-concern consumers.
        services.TryAddSingleton<IGitCredentialBroker>(sp => sp.GetRequiredService<GitHubGitServer>().Credentials);
        services.TryAddSingleton<IGitRepositoryService>(sp => sp.GetRequiredService<GitHubGitServer>().Repositories);
        services.TryAddSingleton<IGitPipelineService>(sp => sp.GetRequiredService<GitHubGitServer>().Pipelines);
        services.TryAddSingleton<IGitCiConfigurationService>(sp => sp.GetRequiredService<GitHubGitServer>().CiConfiguration);
        services.TryAddSingleton<IGitEnvironmentService>(sp => sp.GetRequiredService<GitHubGitServer>().Environments);
        services.TryAddSingleton<IGitBranchPolicyService>(sp => sp.GetRequiredService<GitHubGitServer>().BranchPolicies);
        services.TryAddSingleton<IGitAccessControlService>(sp => sp.GetRequiredService<GitHubGitServer>().AccessControl);
        services.TryAddSingleton<IGitWebhookService>(sp => sp.GetRequiredService<GitHubGitServer>().Webhooks);
        services.TryAddSingleton<IGitWebhookIngestor>(sp => sp.GetRequiredService<GitHubGitServer>().WebhookIngestor);
        services.TryAddSingleton<IGitNamespaceProvisioner>(sp => sp.GetRequiredService<GitHubGitServer>().NamespaceProvisioner);

        return services;
    }
}
