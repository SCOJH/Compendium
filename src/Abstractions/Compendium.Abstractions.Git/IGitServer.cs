// -----------------------------------------------------------------------
// <copyright file="IGitServer.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.AccessControl;
using Compendium.Abstractions.Git.Capabilities;
using Compendium.Abstractions.Git.CiConfiguration;
using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Environments;
using Compendium.Abstractions.Git.Pipelines;
using Compendium.Abstractions.Git.Protection;
using Compendium.Abstractions.Git.Provisioning;
using Compendium.Abstractions.Git.Repositories;
using Compendium.Abstractions.Git.Webhooks;

namespace Compendium.Abstractions.Git;

/// <summary>
/// The git-server facade: one instance per provider adapter, carrying the
/// provider discriminator, the declarative capability matrix, and the
/// concern-scoped ports. Consumers resolve <c>IEnumerable&lt;IGitServer&gt;</c>
/// and dispatch on <see cref="Provider"/> (the sub-ports are also registered
/// individually for single-concern consumers).
/// </summary>
/// <remarks>
/// Implementations are stateless singletons: all per-tenant state travels in
/// the <see cref="Connections.GitConnection"/> passed to every method, so a
/// single adapter instance serves any number of tenants concurrently.
/// </remarks>
public interface IGitServer
{
    /// <summary>
    /// Gets the provider identifier used for dispatch (e.g. <c>"github"</c>,
    /// <c>"gitlab"</c>, <c>"gitea"</c>).
    /// </summary>
    string Provider { get; }

    /// <summary>
    /// Gets the adapter's declared capability matrix.
    /// </summary>
    GitServerCapabilities Capabilities { get; }

    /// <summary>Gets the credential minting/validation/discovery port.</summary>
    IGitCredentialBroker Credentials { get; }

    /// <summary>Gets the repository lifecycle and read port.</summary>
    IGitRepositoryService Repositories { get; }

    /// <summary>Gets the CI pipeline port.</summary>
    IGitPipelineService Pipelines { get; }

    /// <summary>Gets the CI secrets/variables port.</summary>
    IGitCiConfigurationService CiConfiguration { get; }

    /// <summary>Gets the deployment-environments port.</summary>
    IGitEnvironmentService Environments { get; }

    /// <summary>Gets the branch protection policies port.</summary>
    IGitBranchPolicyService BranchPolicies { get; }

    /// <summary>Gets the teams and repository access port.</summary>
    IGitAccessControlService AccessControl { get; }

    /// <summary>Gets the outgoing webhook subscription port.</summary>
    IGitWebhookService Webhooks { get; }

    /// <summary>Gets the inbound webhook verification/parsing port.</summary>
    IGitWebhookIngestor WebhookIngestor { get; }

    /// <summary>Gets the optional namespace provisioning port.</summary>
    IGitNamespaceProvisioner NamespaceProvisioner { get; }
}
