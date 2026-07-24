// -----------------------------------------------------------------------
// <copyright file="NullGitServer.cs" company="Sassy Solutions">
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

namespace Compendium.Abstractions.Git.Stubs;

/// <summary>
/// Fail-fast stub for hosts without a configured git server: every operation
/// returns <c>Git.NotConfigured</c> and the capability matrix is empty. Register
/// it in the unconfigured DI branch so a misconfigured deployment fails loudly
/// per-request instead of throwing, and never with fake data.
/// </summary>
public sealed class NullGitServer :
    IGitServer,
    IGitCredentialBroker,
    IGitRepositoryService,
    IGitPipelineService,
    IGitCiConfigurationService,
    IGitEnvironmentService,
    IGitBranchPolicyService,
    IGitAccessControlService,
    IGitWebhookService,
    IGitWebhookIngestor,
    IGitNamespaceProvisioner
{
    /// <summary>
    /// The provider discriminator reported by the stub.
    /// </summary>
    public const string ProviderName = "null";

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public GitServerCapabilities Capabilities { get; } = new()
    {
        Provider = ProviderName,
        Entries = new Dictionary<GitCapability, GitCapabilitySupport>(),
    };

    /// <inheritdoc />
    public IGitCredentialBroker Credentials => this;

    /// <inheritdoc />
    public IGitRepositoryService Repositories => this;

    /// <inheritdoc />
    public IGitPipelineService Pipelines => this;

    /// <inheritdoc />
    public IGitCiConfigurationService CiConfiguration => this;

    /// <inheritdoc />
    public IGitEnvironmentService Environments => this;

    /// <inheritdoc />
    public IGitBranchPolicyService BranchPolicies => this;

    /// <inheritdoc />
    public IGitAccessControlService AccessControl => this;

    /// <inheritdoc />
    public IGitWebhookService Webhooks => this;

    /// <inheritdoc />
    public IGitWebhookIngestor WebhookIngestor => this;

    /// <inheritdoc />
    public IGitNamespaceProvisioner NamespaceProvisioner => this;

    private static Result NotConfigured() => Result.Failure(GitErrors.NotConfigured());

    private static Result<T> NotConfigured<T>() => Result.Failure<T>(GitErrors.NotConfigured());

    private static Task<Result> NotConfiguredTask() => Task.FromResult(NotConfigured());

    private static Task<Result<T>> NotConfiguredTask<T>() => Task.FromResult(NotConfigured<T>());

    /// <inheritdoc />
    public Task<Result<GitAccessToken>> MintAsync(
        GitConnection connection, GitAccessTokenScope? scope = null, CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitAccessToken>();

    /// <inheritdoc />
    public Task<Result<GitConnectionIdentity>> ValidateAsync(
        GitConnection connection, CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitConnectionIdentity>();

    /// <inheritdoc />
    public Task<Result<GitInstallationInfo>> ResolveAppInstallationAsync(
        string @namespace, string? appKey = null, CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitInstallationInfo>();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitInstallationInfo>>> ListAppInstallationsAsync(
        string? appKey = null, CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitInstallationInfo>>();

    /// <inheritdoc />
    public Task<Result<GitRepository>> CreateFromTemplateAsync(
        GitConnection connection, CreateRepositoryFromTemplate request, CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitRepository>();

    /// <inheritdoc />
    public Task<Result<GitRepository>> GetAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitRepository>();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitRepository>>> ListAsync(
        GitConnection connection, string @namespace, CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitRepository>>();

    /// <inheritdoc />
    public Task<Result<bool>> FileExistsAsync(
        GitConnection connection, GitRepositoryRef repository, string path, string? gitRef = null,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<bool>();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitCommit>>> ListCommitsAsync(
        GitConnection connection, GitRepositoryRef repository, string? reference, int limit,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitCommit>>();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitBranch>>> ListBranchesAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitBranch>>();

    /// <inheritdoc />
    public Task<Result<GitTag>> CreateTagAsync(
        GitConnection connection, GitRepositoryRef repository, string tagName, string? commitSha = null,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitTag>();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitTag>>> ListTagsAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitTag>>();

    /// <inheritdoc />
    public Task<Result<GitRelease>> CreateReleaseAsync(
        GitConnection connection, GitRepositoryRef repository, CreateGitRelease request,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitRelease>();

    /// <inheritdoc />
    public Task<Result<GitPipelineRunHandle>> TriggerAsync(
        GitConnection connection, GitRepositoryRef repository, TriggerGitPipeline request,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitPipelineRunHandle>();

    /// <inheritdoc />
    public Task<Result<GitPipelineRun>> GetRunAsync(
        GitConnection connection, GitRepositoryRef repository, string runId,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitPipelineRun>();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitPipelineRun>>> ListRunsAsync(
        GitConnection connection, GitRepositoryRef repository, ListGitPipelineRuns query,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitPipelineRun>>();

    /// <inheritdoc />
    public Task<Result> SetSecretsAsync(
        GitConnection connection, GitConfigurationScope scope, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result> DeleteSecretAsync(
        GitConnection connection, GitConfigurationScope scope, string name,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result> SetVariablesAsync(
        GitConnection connection, GitConfigurationScope scope, IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result> DeleteVariableAsync(
        GitConnection connection, GitConfigurationScope scope, string name,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result<GitDeploymentEnvironment>> EnsureAsync(
        GitConnection connection, GitRepositoryRef repository, EnsureGitEnvironment request,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitDeploymentEnvironment>();

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        GitConnection connection, GitRepositoryRef repository, string environmentName,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitDeploymentEnvironment>>> ListAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
        => NotConfiguredTask<IReadOnlyList<GitDeploymentEnvironment>>();

    /// <inheritdoc />
    public Task<Result<GitBranchPolicy>> ApplyAsync(
        GitConnection connection, GitRepositoryRef repository, GitBranchPolicyRequest request,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitBranchPolicy>();

    /// <inheritdoc />
    public Task<Result> RemoveAsync(
        GitConnection connection, GitRepositoryRef repository, string policyId,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    Task<Result<IReadOnlyList<GitBranchPolicy>>> IGitBranchPolicyService.ListAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken)
        => NotConfiguredTask<IReadOnlyList<GitBranchPolicy>>();

    /// <inheritdoc />
    public Task<Result<GitTeam>> EnsureTeamAsync(
        GitConnection connection, string @namespace, EnsureGitTeam request,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitTeam>();

    /// <inheritdoc />
    public Task<Result> AddTeamMemberAsync(
        GitConnection connection, string @namespace, string teamSlug, string username, GitTeamRole role,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result> SetTeamRepositoryRoleAsync(
        GitConnection connection, string @namespace, string teamSlug, GitRepositoryRef repository,
        GitRepositoryRole role, CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result> SetUserRepositoryRoleAsync(
        GitConnection connection, GitRepositoryRef repository, string username, GitRepositoryRole role,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result> RemoveUserFromRepositoryAsync(
        GitConnection connection, GitRepositoryRef repository, string username,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    public Task<Result<GitWebhookSubscription>> EnsureAsync(
        GitConnection connection, GitWebhookTarget target, EnsureGitWebhook request,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitWebhookSubscription>();

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        GitConnection connection, GitWebhookTarget target, string subscriptionId,
        CancellationToken cancellationToken = default)
        => NotConfiguredTask();

    /// <inheritdoc />
    Task<Result<IReadOnlyList<GitWebhookSubscription>>> IGitWebhookService.ListAsync(
        GitConnection connection, GitWebhookTarget target, CancellationToken cancellationToken)
        => NotConfiguredTask<IReadOnlyList<GitWebhookSubscription>>();

    /// <inheritdoc />
    public Result<GitWebhookEvent> Parse(GitWebhookDelivery delivery, string secret)
        => NotConfigured<GitWebhookEvent>();

    /// <inheritdoc />
    public Task<Result<GitNamespace>> CreateNamespaceAsync(
        GitConnection connection, CreateGitNamespace request, CancellationToken cancellationToken = default)
        => NotConfiguredTask<GitNamespace>();
}
