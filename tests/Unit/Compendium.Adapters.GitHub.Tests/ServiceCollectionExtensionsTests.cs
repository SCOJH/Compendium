// -----------------------------------------------------------------------
// <copyright file="ServiceCollectionExtensionsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private static ServiceProvider Build(Action<GitHubAdapterOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGitHubGitServer(configure);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddGitHubGitServer_RegistersTheServerForProviderDispatch()
    {
        using var provider = Build();

        var servers = provider.GetServices<IGitServer>().ToList();

        servers.Should().ContainSingle(s => s.Provider == "github");
        servers[0].Capabilities.Provider.Should().Be("github");
    }

    [Fact]
    public void AddGitHubGitServer_RegistersEveryConcernScopedPort()
    {
        using var provider = Build();

        provider.GetService<IGitCredentialBroker>().Should().NotBeNull();
        provider.GetService<IGitRepositoryService>().Should().NotBeNull();
        provider.GetService<IGitPipelineService>().Should().NotBeNull();
        provider.GetService<IGitCiConfigurationService>().Should().NotBeNull();
        provider.GetService<IGitEnvironmentService>().Should().NotBeNull();
        provider.GetService<IGitBranchPolicyService>().Should().NotBeNull();
        provider.GetService<IGitAccessControlService>().Should().NotBeNull();
        provider.GetService<IGitWebhookService>().Should().NotBeNull();
        provider.GetService<IGitWebhookIngestor>().Should().NotBeNull();
        provider.GetService<IGitNamespaceProvisioner>().Should().NotBeNull();
    }

    [Fact]
    public void AddGitHubGitServer_TheSameFacadeBacksTheEnumerableAndThePorts()
    {
        using var provider = Build();

        var facade = provider.GetServices<IGitServer>().Single();
        provider.GetRequiredService<IGitRepositoryService>().Should().BeSameAs(facade.Repositories);
    }

    [Fact]
    public void AddGitHubGitServer_AppliesTheConfigureCallback()
    {
        using var provider = Build(o => o.DefaultApp.AppId = "12345");

        provider.GetRequiredService<IOptions<GitHubAdapterOptions>>().Value.DefaultApp.AppId.Should().Be("12345");
    }

    [Fact]
    public void AddGitHubGitServer_ThrowsOnNullServices()
    {
        var act = () => ((IServiceCollection)null!).AddGitHubGitServer();
        act.Should().Throw<ArgumentNullException>();
    }
}
