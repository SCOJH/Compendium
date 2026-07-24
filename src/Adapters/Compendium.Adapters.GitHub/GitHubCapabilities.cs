// -----------------------------------------------------------------------
// <copyright file="GitHubCapabilities.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub;

/// <summary>
/// The declared capability matrix for the GitHub adapter on github.com / GHES.
/// The full support/limitation table lives in CAPABILITIES.md; this is its
/// executable counterpart, shared by the facade and the sub-services that guard
/// optional capabilities via <see cref="GitServerCapabilities.EnsureSupported"/>.
/// </summary>
internal static class GitHubCapabilities
{
    /// <summary>
    /// Gets the singleton capability matrix. Every capability the adapter can
    /// reach on github.com is <see cref="GitCapabilityLevel.Full"/>, except
    /// <see cref="GitCapability.PipelineTrigger"/> (Partial: <c>workflow_dispatch</c>
    /// returns no run id) and <see cref="GitCapability.NamespaceProvisioning"/>
    /// (None: org creation needs an enterprise-owner user token).
    /// </summary>
    public static GitServerCapabilities Matrix { get; } = new()
    {
        Provider = GitHubDefaults.Provider,
        Entries = new Dictionary<GitCapability, GitCapabilitySupport>
        {
            [GitCapability.RepositoryFromTemplate] = new(GitCapabilityLevel.Full),
            [GitCapability.RepositoryManagement] = new(GitCapabilityLevel.Full),
            [GitCapability.TagsAndReleases] = new(GitCapabilityLevel.Full),
            [GitCapability.CiSecrets] = new(GitCapabilityLevel.Full),
            [GitCapability.CiVariables] = new(GitCapabilityLevel.Full),
            [GitCapability.NamespaceSecrets] = new(GitCapabilityLevel.Full),
            [GitCapability.PipelineTrigger] = new(
                GitCapabilityLevel.Partial,
                "workflow_dispatch returns no run id; correlate the created run via ListRuns."),
            [GitCapability.PipelineStatus] = new(GitCapabilityLevel.Full),
            [GitCapability.DeploymentEnvironments] = new(GitCapabilityLevel.Full),
            [GitCapability.EnvironmentSecrets] = new(GitCapabilityLevel.Full),
            [GitCapability.BranchPolicies] = new(GitCapabilityLevel.Full),
            [GitCapability.TeamsAndPermissions] = new(GitCapabilityLevel.Full),
            [GitCapability.WebhookManagement] = new(GitCapabilityLevel.Full),
            [GitCapability.WebhookIngestion] = new(GitCapabilityLevel.Full),
            [GitCapability.NamespaceProvisioning] = new(
                GitCapabilityLevel.None,
                "github.com organization creation requires an enterprise-owner user token; unreachable with App credentials."),
            [GitCapability.AppInstallationAuth] = new(GitCapabilityLevel.Full),
            [GitCapability.ServiceAccountAuth] = new(GitCapabilityLevel.Full),
            [GitCapability.OAuthUserAuth] = new(GitCapabilityLevel.Full),
            [GitCapability.ScopedTokenMinting] = new(GitCapabilityLevel.Full),
        },
    };
}
