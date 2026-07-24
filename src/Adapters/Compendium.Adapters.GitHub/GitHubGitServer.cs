// -----------------------------------------------------------------------
// <copyright file="GitHubGitServer.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Auth;

namespace Compendium.Adapters.GitHub;

/// <summary>
/// The GitHub <see cref="IGitServer"/> facade. Declares <c>Provider = "github"</c>
/// and the capability matrix, and exposes the concern-scoped ports. Stateless and
/// registered as a singleton — all per-tenant state travels in the
/// <see cref="GitConnection"/> passed to each port method.
/// </summary>
internal sealed class GitHubGitServer : IGitServer
{
    /// <summary>The provider discriminator reported by the adapter.</summary>
    public const string ProviderName = GitHubDefaults.Provider;

    public GitHubGitServer(
        GitHubCredentialBroker credentials,
        Services.GitHubRepositoryService repositories,
        Services.GitHubPipelineService pipelines,
        Services.GitHubCiConfigurationService ciConfiguration,
        Services.GitHubEnvironmentService environments,
        Services.GitHubBranchPolicyService branchPolicies,
        Services.GitHubAccessControlService accessControl,
        Services.GitHubWebhookService webhooks,
        Webhooks.GitHubWebhookIngestor webhookIngestor,
        Services.GitHubNamespaceProvisioner namespaceProvisioner)
    {
        Credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        Repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
        Pipelines = pipelines ?? throw new ArgumentNullException(nameof(pipelines));
        CiConfiguration = ciConfiguration ?? throw new ArgumentNullException(nameof(ciConfiguration));
        Environments = environments ?? throw new ArgumentNullException(nameof(environments));
        BranchPolicies = branchPolicies ?? throw new ArgumentNullException(nameof(branchPolicies));
        AccessControl = accessControl ?? throw new ArgumentNullException(nameof(accessControl));
        Webhooks = webhooks ?? throw new ArgumentNullException(nameof(webhooks));
        WebhookIngestor = webhookIngestor ?? throw new ArgumentNullException(nameof(webhookIngestor));
        NamespaceProvisioner = namespaceProvisioner ?? throw new ArgumentNullException(nameof(namespaceProvisioner));
    }

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public GitServerCapabilities Capabilities => GitHubCapabilities.Matrix;

    /// <inheritdoc />
    public IGitCredentialBroker Credentials { get; }

    /// <inheritdoc />
    public IGitRepositoryService Repositories { get; }

    /// <inheritdoc />
    public IGitPipelineService Pipelines { get; }

    /// <inheritdoc />
    public IGitCiConfigurationService CiConfiguration { get; }

    /// <inheritdoc />
    public IGitEnvironmentService Environments { get; }

    /// <inheritdoc />
    public IGitBranchPolicyService BranchPolicies { get; }

    /// <inheritdoc />
    public IGitAccessControlService AccessControl { get; }

    /// <inheritdoc />
    public IGitWebhookService Webhooks { get; }

    /// <inheritdoc />
    public IGitWebhookIngestor WebhookIngestor { get; }

    /// <inheritdoc />
    public IGitNamespaceProvisioner NamespaceProvisioner { get; }
}
