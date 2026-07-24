// -----------------------------------------------------------------------
// <copyright file="GitCapability.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Git.Capabilities;

/// <summary>
/// The optional capabilities a git-server adapter may support. Adapters declare
/// their support level per capability in <see cref="GitServerCapabilities"/>;
/// operations relying on an unsupported capability fail with
/// <c>Git.CapabilityNotSupported</c> instead of throwing.
/// </summary>
public enum GitCapability
{
    /// <summary>Create a repository from a template/scaffold repository.</summary>
    RepositoryFromTemplate,

    /// <summary>Read repository metadata, contents, commits, and branches.</summary>
    RepositoryManagement,

    /// <summary>Create tags and publish releases.</summary>
    TagsAndReleases,

    /// <summary>Set repository-scoped CI secrets (encrypted, write-only).</summary>
    CiSecrets,

    /// <summary>Set repository-scoped CI variables (plaintext configuration).</summary>
    CiVariables,

    /// <summary>Set namespace-scoped (organization/group) CI secrets and variables.</summary>
    NamespaceSecrets,

    /// <summary>Trigger a CI pipeline / workflow run.</summary>
    PipelineTrigger,

    /// <summary>Read CI pipeline run status and history.</summary>
    PipelineStatus,

    /// <summary>Create and manage deployment environments.</summary>
    DeploymentEnvironments,

    /// <summary>Set secrets scoped to a deployment environment.</summary>
    EnvironmentSecrets,

    /// <summary>Apply branch protection policies (rulesets, protected branches).</summary>
    BranchPolicies,

    /// <summary>Manage teams and repository access roles.</summary>
    TeamsAndPermissions,

    /// <summary>Create and manage outgoing webhook subscriptions on repositories or namespaces.</summary>
    WebhookManagement,

    /// <summary>Verify and parse inbound webhook deliveries into neutral events.</summary>
    WebhookIngestion,

    /// <summary>Create the namespace (organization/group) itself via API.</summary>
    NamespaceProvisioning,

    /// <summary>Authenticate as a platform app installed on the customer namespace (e.g. GitHub App).</summary>
    AppInstallationAuth,

    /// <summary>Authenticate with a durable service-account / bot token.</summary>
    ServiceAccountAuth,

    /// <summary>Authenticate with an OAuth user access token.</summary>
    OAuthUserAuth,

    /// <summary>Narrow a minted credential to specific repositories/permissions at mint time.</summary>
    ScopedTokenMinting,
}
