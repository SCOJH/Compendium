// -----------------------------------------------------------------------
// <copyright file="InMemoryGitServer.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Compendium.Abstractions.Git;
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
using Compendium.Core.Results;

namespace Compendium.Testing.Git;

/// <summary>
/// Full-fidelity in-memory <see cref="IGitServer"/> fake for tests and dev mode.
/// Declares every capability as <see cref="GitCapabilityLevel.Full"/> and keeps
/// all state in memory: repositories created from templates, secret NAMES
/// (values are discarded — mirroring the write-only semantics of real
/// providers), variables, deployment environments, branch policies, teams,
/// webhook subscriptions, and pipeline runs.
/// </summary>
/// <remarks>
/// Thread-safe. Test hooks: <see cref="SeedInstallation"/>,
/// <see cref="SeedRepository"/>, <see cref="SeedFile"/>,
/// <see cref="CompleteRun"/>, and the <see cref="Calls"/> log. Inbound webhook
/// deliveries are parsed from a simple JSON envelope signed by equality with
/// the shared secret via the <c>X-InMemory-Signature</c> header (fail-closed).
/// </remarks>
public sealed class InMemoryGitServer :
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
    /// <summary>The provider discriminator reported by the fake.</summary>
    public const string ProviderName = "in-memory";

    /// <summary>The webhook secret accepted by <see cref="Parse"/> when signing test deliveries.</summary>
    public const string WellKnownWebhookSecret = "in-memory-webhook-secret";

    private readonly object _gate = new();
    private readonly List<string> _calls = [];
    private readonly HashSet<string> _namespaces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RepoState> _repositories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<GitInstallationInfo> _installations = [];
    private readonly Dictionary<string, HashSet<string>> _secretsByScope = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> _variablesByScope = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, GitPipelineRun> _runs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, GitTeam>> _teams = new(StringComparer.OrdinalIgnoreCase);
    private int _nextRunId;
    private int _nextWebhookId;
    private int _nextReleaseId;

    /// <summary>
    /// Gets the identity reported by <see cref="ValidateAsync"/> for
    /// token-based credentials.
    /// </summary>
    public GitConnectionIdentity DefaultIdentity { get; init; } =
        new("in-memory-user", GitAccountType.User, "In-Memory User");

    /// <summary>
    /// Gets the paths seeded into every repository created from a template, so
    /// bootstrap-marker checks (<c>.bootstrapped</c>) succeed in dev mode
    /// without an explicit <see cref="SeedFile"/> call.
    /// </summary>
    public IReadOnlyList<string> InitialFiles { get; init; } = [".bootstrapped"];

    /// <summary>
    /// Gets a snapshot of the operations performed, as
    /// <c>"MethodName arg1 arg2"</c> entries, for order/interaction assertions.
    /// </summary>
    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public GitServerCapabilities Capabilities { get; } = new()
    {
        Provider = ProviderName,
        Entries = Enum.GetValues<GitCapability>()
            .ToDictionary(c => c, _ => new GitCapabilitySupport(GitCapabilityLevel.Full)),
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

    // ---- Test hooks -----------------------------------------------------

    /// <summary>
    /// Registers a platform-app installation so
    /// <see cref="ResolveAppInstallationAsync"/> and
    /// <see cref="ListAppInstallationsAsync"/> can discover it.
    /// </summary>
    public void SeedInstallation(GitInstallationInfo installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        lock (_gate)
        {
            _installations.RemoveAll(i => i.InstallationId == installation.InstallationId);
            _installations.Add(installation);
            _namespaces.Add(installation.AccountLogin);
        }
    }

    /// <summary>
    /// Seeds an existing repository without going through the template flow.
    /// </summary>
    public void SeedRepository(GitRepositoryRef repository, bool @private = true)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            CreateRepoStateLocked(repository, @private);
        }
    }

    /// <summary>
    /// Seeds a file path into a repository so <see cref="FileExistsAsync"/> reports it.
    /// </summary>
    public void SeedFile(GitRepositoryRef repository, string path)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (_repositories.TryGetValue(repository.FullName, out var state))
            {
                state.Files.Add(path);
            }
        }
    }

    /// <summary>
    /// Transitions a pipeline run created by <see cref="TriggerAsync"/> to a
    /// terminal status. Returns false when the run is unknown.
    /// </summary>
    public bool CompleteRun(string runId, GitPipelineStatus status)
    {
        lock (_gate)
        {
            if (!_runs.TryGetValue(runId, out var run))
            {
                return false;
            }

            _runs[runId] = run with { Status = status };
            return true;
        }
    }

    // ---- IGitCredentialBroker -------------------------------------------

    /// <inheritdoc />
    public Task<Result<GitAccessToken>> MintAsync(
        GitConnection connection, GitAccessTokenScope? scope = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Log("Mint", connection.Provider);
        return Task.FromResult(Result.Success(new GitAccessToken
        {
            Token = $"im_{Guid.NewGuid():N}",
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1),
            HttpBasicUsername = "x-access-token",
        }));
    }

    /// <inheritdoc />
    public Task<Result<GitConnectionIdentity>> ValidateAsync(
        GitConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        Log("Validate", connection.Provider);

        if (connection.Credential is GitCredential.AppInstallation app)
        {
            lock (_gate)
            {
                var install = _installations.FirstOrDefault(i => i.InstallationId == app.InstallationId);
                return Task.FromResult(install is null
                    ? Result.Failure<GitConnectionIdentity>(
                        GitErrors.AuthenticationFailed(ProviderName, $"unknown installation '{app.InstallationId}'"))
                    : Result.Success(new GitConnectionIdentity(install.AccountLogin, install.AccountType)));
            }
        }

        return Task.FromResult(Result.Success(DefaultIdentity));
    }

    /// <inheritdoc />
    public Task<Result<GitInstallationInfo>> ResolveAppInstallationAsync(
        string @namespace, string? appKey = null, CancellationToken cancellationToken = default)
    {
        Log("ResolveAppInstallation", @namespace);
        lock (_gate)
        {
            var install = _installations.FirstOrDefault(
                i => string.Equals(i.AccountLogin, @namespace, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(install is null
                ? Result.Failure<GitInstallationInfo>(
                    GitErrors.AppNotInstalled(@namespace, "https://in-memory.invalid/install"))
                : Result.Success(install));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitInstallationInfo>>> ListAppInstallationsAsync(
        string? appKey = null, CancellationToken cancellationToken = default)
    {
        Log("ListAppInstallations");
        lock (_gate)
        {
            return Task.FromResult(Result.Success<IReadOnlyList<GitInstallationInfo>>([.. _installations]));
        }
    }

    // ---- IGitRepositoryService ------------------------------------------

    /// <inheritdoc />
    public Task<Result<GitRepository>> CreateFromTemplateAsync(
        GitConnection connection, CreateRepositoryFromTemplate request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log("CreateFromTemplate", request.Namespace, request.Name);
        lock (_gate)
        {
            var reference = new GitRepositoryRef(request.Namespace, request.Name);
            if (_repositories.ContainsKey(reference.FullName))
            {
                return Task.FromResult(Result.Failure<GitRepository>(GitErrors.Conflict(reference.FullName)));
            }

            var state = CreateRepoStateLocked(reference, request.Private);
            foreach (var file in InitialFiles)
            {
                state.Files.Add(file);
            }

            return Task.FromResult(Result.Success(state.Info));
        }
    }

    /// <inheritdoc />
    public Task<Result<GitRepository>> GetAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            return Task.FromResult(_repositories.TryGetValue(repository.FullName, out var state)
                ? Result.Success(state.Info)
                : Result.Failure<GitRepository>(GitErrors.RepositoryNotFound(repository.FullName)));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitRepository>>> ListAsync(
        GitConnection connection, string @namespace, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IReadOnlyList<GitRepository> repos = _repositories.Values
                .Where(s => string.Equals(s.Info.Ref.Namespace, @namespace, StringComparison.OrdinalIgnoreCase))
                .Select(s => s.Info)
                .ToList();
            return Task.FromResult(Result.Success(repos));
        }
    }

    /// <inheritdoc />
    public Task<Result<bool>> FileExistsAsync(
        GitConnection connection, GitRepositoryRef repository, string path, string? gitRef = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            return Task.FromResult(_repositories.TryGetValue(repository.FullName, out var state)
                ? Result.Success(state.Files.Contains(path))
                : Result.Failure<bool>(GitErrors.RepositoryNotFound(repository.FullName)));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitCommit>>> ListCommitsAsync(
        GitConnection connection, GitRepositoryRef repository, string? reference, int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<GitCommit>>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            IReadOnlyList<GitCommit> commits = state.Commits.AsEnumerable().Reverse().Take(limit).ToList();
            return Task.FromResult(Result.Success(commits));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitBranch>>> ListBranchesAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<GitBranch>>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            IReadOnlyList<GitBranch> branches = state.Branches
                .Select(kvp => new GitBranch(kvp.Key, kvp.Value, Protected: state.Policies.ContainsKey(kvp.Key)))
                .ToList();
            return Task.FromResult(Result.Success(branches));
        }
    }

    /// <inheritdoc />
    public Task<Result<GitTag>> CreateTagAsync(
        GitConnection connection, GitRepositoryRef repository, string tagName, string? commitSha = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Log("CreateTag", repository.FullName, tagName);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<GitTag>(GitErrors.RepositoryNotFound(repository.FullName)));
            }

            var sha = commitSha ?? state.Branches[state.Info.DefaultBranch];
            var tag = new GitTag(tagName, sha);
            state.Tags[tagName] = tag;
            return Task.FromResult(Result.Success(tag));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitTag>>> ListTagsAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<GitTag>>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            IReadOnlyList<GitTag> tags = [.. state.Tags.Values];
            return Task.FromResult(Result.Success(tags));
        }
    }

    /// <inheritdoc />
    public Task<Result<GitRelease>> CreateReleaseAsync(
        GitConnection connection, GitRepositoryRef repository, CreateGitRelease request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        Log("CreateRelease", repository.FullName, request.TagName);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<GitRelease>(GitErrors.RepositoryNotFound(repository.FullName)));
            }

            if (!state.Tags.ContainsKey(request.TagName))
            {
                var sha = request.TargetCommitSha ?? state.Branches[state.Info.DefaultBranch];
                state.Tags[request.TagName] = new GitTag(request.TagName, sha);
            }

            var release = new GitRelease(
                Id: $"rel-{++_nextReleaseId}",
                TagName: request.TagName,
                HtmlUrl: $"https://in-memory.invalid/{repository.FullName}/releases/{request.TagName}");
            state.Releases.Add(release);
            return Task.FromResult(Result.Success(release));
        }
    }

    // ---- IGitPipelineService --------------------------------------------

    /// <inheritdoc />
    public Task<Result<GitPipelineRunHandle>> TriggerAsync(
        GitConnection connection, GitRepositoryRef repository, TriggerGitPipeline request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        Log("TriggerPipeline", repository.FullName, request.Pipeline, request.Reference);
        lock (_gate)
        {
            if (!_repositories.ContainsKey(repository.FullName))
            {
                return Task.FromResult(Result.Failure<GitPipelineRunHandle>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            var runId = $"run-{++_nextRunId}";
            _runs[runId] = new GitPipelineRun(
                Id: runId,
                Pipeline: request.Pipeline,
                Status: GitPipelineStatus.Queued,
                Reference: request.Reference,
                HtmlUrl: $"https://in-memory.invalid/{repository.FullName}/runs/{runId}",
                CreatedAt: DateTimeOffset.UtcNow);
            return Task.FromResult(Result.Success(new GitPipelineRunHandle(runId)));
        }
    }

    /// <inheritdoc />
    public Task<Result<GitPipelineRun>> GetRunAsync(
        GitConnection connection, GitRepositoryRef repository, string runId,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_runs.TryGetValue(runId, out var run)
                ? Result.Success(run)
                : Result.Failure<GitPipelineRun>(Error.NotFound(
                    $"{GitErrors.Prefix}.RunNotFound", $"Pipeline run '{runId}' was not found.")));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<GitPipelineRun>>> ListRunsAsync(
        GitConnection connection, GitRepositoryRef repository, ListGitPipelineRuns query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        lock (_gate)
        {
            IReadOnlyList<GitPipelineRun> runs = _runs.Values
                .Where(r => query.Pipeline is null || r.Pipeline == query.Pipeline)
                .Where(r => query.Reference is null || r.Reference == query.Reference)
                .OrderByDescending(r => r.CreatedAt)
                .Take(query.Limit)
                .ToList();
            return Task.FromResult(Result.Success(runs));
        }
    }

    // ---- IGitCiConfigurationService -------------------------------------

    /// <inheritdoc />
    public Task<Result> SetSecretsAsync(
        GitConnection connection, GitConfigurationScope scope, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(secrets);
        Log("SetSecrets", ScopeKey(scope), string.Join(",", secrets.Keys));
        lock (_gate)
        {
            var names = GetOrAdd(_secretsByScope, ScopeKey(scope), () => new HashSet<string>(StringComparer.Ordinal));
            foreach (var name in secrets.Keys)
            {
                names.Add(name); // values deliberately discarded: secrets are write-only
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteSecretAsync(
        GitConnection connection, GitConfigurationScope scope, string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            if (_secretsByScope.TryGetValue(ScopeKey(scope), out var names))
            {
                names.Remove(name);
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    public Task<Result> SetVariablesAsync(
        GitConnection connection, GitConfigurationScope scope, IReadOnlyDictionary<string, string> variables,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(variables);
        Log("SetVariables", ScopeKey(scope), string.Join(",", variables.Keys));
        lock (_gate)
        {
            var store = GetOrAdd(_variablesByScope, ScopeKey(scope), () => new Dictionary<string, string>(StringComparer.Ordinal));
            foreach (var (key, value) in variables)
            {
                store[key] = value;
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteVariableAsync(
        GitConnection connection, GitConfigurationScope scope, string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            if (_variablesByScope.TryGetValue(ScopeKey(scope), out var store))
            {
                store.Remove(name);
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>
    /// Returns the secret NAMES stored at a scope (test-inspection hook — real
    /// providers cannot read secrets back either).
    /// </summary>
    public IReadOnlyCollection<string> GetSecretNames(GitConfigurationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            return _secretsByScope.TryGetValue(ScopeKey(scope), out var names) ? [.. names] : [];
        }
    }

    /// <summary>
    /// Returns the variables stored at a scope (test-inspection hook).
    /// </summary>
    public IReadOnlyDictionary<string, string> GetVariables(GitConfigurationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            return _variablesByScope.TryGetValue(ScopeKey(scope), out var store)
                ? new Dictionary<string, string>(store)
                : new Dictionary<string, string>();
        }
    }

    // ---- IGitEnvironmentService -----------------------------------------

    /// <inheritdoc />
    public Task<Result<GitDeploymentEnvironment>> EnsureAsync(
        GitConnection connection, GitRepositoryRef repository, EnsureGitEnvironment request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        Log("EnsureEnvironment", repository.FullName, request.Name);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<GitDeploymentEnvironment>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            var environment = new GitDeploymentEnvironment(request.Name);
            state.Environments[request.Name] = environment;
            return Task.FromResult(Result.Success(environment));
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        GitConnection connection, GitRepositoryRef repository, string environmentName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (_repositories.TryGetValue(repository.FullName, out var state))
            {
                state.Environments.Remove(environmentName);
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    Task<Result<IReadOnlyList<GitDeploymentEnvironment>>> IGitEnvironmentService.ListAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<GitDeploymentEnvironment>>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            IReadOnlyList<GitDeploymentEnvironment> environments = [.. state.Environments.Values];
            return Task.FromResult(Result.Success(environments));
        }
    }

    // ---- IGitBranchPolicyService ----------------------------------------

    /// <inheritdoc />
    public Task<Result<GitBranchPolicy>> ApplyAsync(
        GitConnection connection, GitRepositoryRef repository, GitBranchPolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(request);
        Log("ApplyBranchPolicy", repository.FullName, request.Pattern);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<GitBranchPolicy>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            var policy = new GitBranchPolicy(Id: $"policy-{request.Pattern}", Pattern: request.Pattern);
            state.Policies[request.Pattern] = policy;
            return Task.FromResult(Result.Success(policy));
        }
    }

    /// <inheritdoc />
    public Task<Result> RemoveAsync(
        GitConnection connection, GitRepositoryRef repository, string policyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (_repositories.TryGetValue(repository.FullName, out var state))
            {
                var pattern = state.Policies.Values.FirstOrDefault(p => p.Id == policyId)?.Pattern;
                if (pattern is not null)
                {
                    state.Policies.Remove(pattern);
                }
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    Task<Result<IReadOnlyList<GitBranchPolicy>>> IGitBranchPolicyService.ListAsync(
        GitConnection connection, GitRepositoryRef repository, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure<IReadOnlyList<GitBranchPolicy>>(
                    GitErrors.RepositoryNotFound(repository.FullName)));
            }

            IReadOnlyList<GitBranchPolicy> policies = [.. state.Policies.Values];
            return Task.FromResult(Result.Success(policies));
        }
    }

    // ---- IGitAccessControlService ---------------------------------------

    /// <inheritdoc />
    public Task<Result<GitTeam>> EnsureTeamAsync(
        GitConnection connection, string @namespace, EnsureGitTeam request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log("EnsureTeam", @namespace, request.Name);
        lock (_gate)
        {
            var slug = request.Name.ToLowerInvariant().Replace(' ', '-');
            var teams = GetOrAdd(_teams, @namespace, () => new Dictionary<string, GitTeam>(StringComparer.OrdinalIgnoreCase));
            var team = new GitTeam(slug, request.Name);
            teams[slug] = team;
            return Task.FromResult(Result.Success(team));
        }
    }

    /// <inheritdoc />
    public Task<Result> AddTeamMemberAsync(
        GitConnection connection, string @namespace, string teamSlug, string username, GitTeamRole role,
        CancellationToken cancellationToken = default)
        => TeamScopedOperation(@namespace, teamSlug, $"AddTeamMember {username} {role}");

    /// <inheritdoc />
    public Task<Result> SetTeamRepositoryRoleAsync(
        GitConnection connection, string @namespace, string teamSlug, GitRepositoryRef repository,
        GitRepositoryRole role, CancellationToken cancellationToken = default)
        => TeamScopedOperation(@namespace, teamSlug, $"SetTeamRepositoryRole {repository.FullName} {role}");

    /// <inheritdoc />
    public Task<Result> SetUserRepositoryRoleAsync(
        GitConnection connection, GitRepositoryRef repository, string username, GitRepositoryRole role,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        Log("SetUserRepositoryRole", repository.FullName, username, role.ToString());
        lock (_gate)
        {
            if (!_repositories.TryGetValue(repository.FullName, out var state))
            {
                return Task.FromResult(Result.Failure(GitErrors.RepositoryNotFound(repository.FullName)));
            }

            state.Collaborators[username] = role;
            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    public Task<Result> RemoveUserFromRepositoryAsync(
        GitConnection connection, GitRepositoryRef repository, string username,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lock (_gate)
        {
            if (_repositories.TryGetValue(repository.FullName, out var state))
            {
                state.Collaborators.Remove(username);
            }

            return Task.FromResult(Result.Success());
        }
    }

    // ---- IGitWebhookService ---------------------------------------------

    /// <inheritdoc />
    public Task<Result<GitWebhookSubscription>> EnsureAsync(
        GitConnection connection, GitWebhookTarget target, EnsureGitWebhook request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(request);
        Log("EnsureWebhook", TargetKey(target), request.Url.ToString());
        lock (_gate)
        {
            var store = GetWebhookStoreLocked(target);
            var existing = store.Values.FirstOrDefault(s => s.Url == request.Url);
            var subscription = new GitWebhookSubscription(
                Id: existing?.Id ?? $"hook-{++_nextWebhookId}",
                Url: request.Url,
                Events: request.Events,
                Active: request.Active);
            store[subscription.Id] = subscription;
            return Task.FromResult(Result.Success(subscription));
        }
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        GitConnection connection, GitWebhookTarget target, string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            GetWebhookStoreLocked(target).Remove(subscriptionId);
            return Task.FromResult(Result.Success());
        }
    }

    /// <inheritdoc />
    Task<Result<IReadOnlyList<GitWebhookSubscription>>> IGitWebhookService.ListAsync(
        GitConnection connection, GitWebhookTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        lock (_gate)
        {
            IReadOnlyList<GitWebhookSubscription> subscriptions = [.. GetWebhookStoreLocked(target).Values];
            return Task.FromResult(Result.Success(subscriptions));
        }
    }

    // ---- IGitWebhookIngestor --------------------------------------------

    /// <inheritdoc />
    public Result<GitWebhookEvent> Parse(GitWebhookDelivery delivery, string secret)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        if (!delivery.Headers.TryGetValue("X-InMemory-Signature", out var signature)
            || !string.Equals(signature, secret, StringComparison.Ordinal))
        {
            return Result.Failure<GitWebhookEvent>(GitErrors.WebhookSignatureInvalid());
        }

        Envelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<Envelope>(delivery.Body, JsonOptions);
        }
        catch (JsonException)
        {
            return Result.Failure<GitWebhookEvent>(Error.Validation(
                $"{GitErrors.Prefix}.MalformedDelivery", "The webhook delivery body is not a valid envelope."));
        }

        if (envelope?.Type is null || envelope.DeliveryId is null)
        {
            return Result.Failure<GitWebhookEvent>(Error.Validation(
                $"{GitErrors.Prefix}.MalformedDelivery", "The webhook delivery body is not a valid envelope."));
        }

        var repository = envelope.Repository?.Split('/') is [var ns, var name]
            ? new GitRepositoryRef(ns, name)
            : null;

        GitWebhookEvent parsed = envelope.Type switch
        {
            "push" => new GitWebhookEvent.Push(
                envelope.Reference ?? "refs/heads/main",
                envelope.HeadCommitSha ?? string.Empty)
            { DeliveryId = envelope.DeliveryId, Repository = repository },

            "connection_changed" => new GitWebhookEvent.ConnectionChanged(
                envelope.Namespace ?? string.Empty,
                envelope.AccountType ?? GitAccountType.Organization,
                envelope.InstallationId ?? string.Empty,
                envelope.Change ?? GitConnectionChangeKind.Installed)
            { DeliveryId = envelope.DeliveryId },

            _ => new GitWebhookEvent.Unsupported(envelope.Type)
            { DeliveryId = envelope.DeliveryId, Repository = repository },
        };

        return Result.Success(parsed);
    }

    // ---- IGitNamespaceProvisioner ---------------------------------------

    /// <inheritdoc />
    public Task<Result<GitNamespace>> CreateNamespaceAsync(
        GitConnection connection, CreateGitNamespace request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Log("CreateNamespace", request.Name);
        lock (_gate)
        {
            if (!_namespaces.Add(request.Name))
            {
                return Task.FromResult(Result.Failure<GitNamespace>(GitErrors.Conflict(request.Name)));
            }

            return Task.FromResult(Result.Success(
                new GitNamespace(request.Name, $"https://in-memory.invalid/{request.Name}")));
        }
    }

    // ---- internals ------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private sealed record Envelope(
        string? Type,
        string? DeliveryId,
        string? Repository,
        string? Reference,
        string? HeadCommitSha,
        string? Namespace,
        GitAccountType? AccountType,
        string? InstallationId,
        GitConnectionChangeKind? Change);

    private sealed class RepoState
    {
        public required GitRepository Info { get; set; }

        public HashSet<string> Files { get; } = new(StringComparer.Ordinal);

        public List<GitCommit> Commits { get; } = [];

        public Dictionary<string, string> Branches { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, GitTag> Tags { get; } = new(StringComparer.Ordinal);

        public List<GitRelease> Releases { get; } = [];

        public Dictionary<string, GitDeploymentEnvironment> Environments { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, GitBranchPolicy> Policies { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, GitRepositoryRole> Collaborators { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, GitWebhookSubscription> Webhooks { get; } = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, Dictionary<string, GitWebhookSubscription>> _namespaceWebhooks =
        new(StringComparer.OrdinalIgnoreCase);

    private RepoState CreateRepoStateLocked(GitRepositoryRef reference, bool @private)
    {
        _namespaces.Add(reference.Namespace);
        var sha = Guid.NewGuid().ToString("N");
        var state = new RepoState
        {
            Info = new GitRepository(
                Ref: reference,
                CloneUrl: $"https://in-memory.invalid/{reference.FullName}.git",
                HtmlUrl: $"https://in-memory.invalid/{reference.FullName}",
                DefaultBranch: "main",
                Private: @private),
        };
        state.Branches["main"] = sha;
        state.Commits.Add(new GitCommit(
            Sha: sha,
            Message: "Initial commit",
            AuthorName: "in-memory",
            AuthoredAt: DateTimeOffset.UtcNow,
            HtmlUrl: $"https://in-memory.invalid/{reference.FullName}/commit/{sha}"));
        _repositories[reference.FullName] = state;
        return state;
    }

    private Dictionary<string, GitWebhookSubscription> GetWebhookStoreLocked(GitWebhookTarget target) =>
        target switch
        {
            GitWebhookTarget.Repository r when _repositories.TryGetValue(r.Ref.FullName, out var state) => state.Webhooks,
            GitWebhookTarget.Repository r => GetOrAdd(
                _namespaceWebhooks, r.Ref.FullName, () => new Dictionary<string, GitWebhookSubscription>(StringComparer.Ordinal)),
            GitWebhookTarget.Namespace n => GetOrAdd(
                _namespaceWebhooks, n.Name, () => new Dictionary<string, GitWebhookSubscription>(StringComparer.Ordinal)),
            _ => throw new InvalidOperationException($"Unknown webhook target: {target.GetType().Name}"),
        };

    private Task<Result> TeamScopedOperation(string @namespace, string teamSlug, string logEntry)
    {
        Log(logEntry, @namespace, teamSlug);
        lock (_gate)
        {
            if (!_teams.TryGetValue(@namespace, out var teams) || !teams.ContainsKey(teamSlug))
            {
                return Task.FromResult(Result.Failure(Error.NotFound(
                    $"{GitErrors.Prefix}.TeamNotFound", $"Team '{teamSlug}' was not found on '{@namespace}'.")));
            }

            return Task.FromResult(Result.Success());
        }
    }

    private static string ScopeKey(GitConfigurationScope scope) => scope switch
    {
        GitConfigurationScope.Repository r => $"repo:{r.Ref.FullName}",
        GitConfigurationScope.Namespace n => $"ns:{n.Name}",
        GitConfigurationScope.Environment e => $"env:{e.Ref.FullName}#{e.EnvironmentName}",
        _ => throw new InvalidOperationException($"Unknown configuration scope: {scope.GetType().Name}"),
    };

    private static string TargetKey(GitWebhookTarget target) => target switch
    {
        GitWebhookTarget.Repository r => $"repo:{r.Ref.FullName}",
        GitWebhookTarget.Namespace n => $"ns:{n.Name}",
        _ => target.GetType().Name,
    };

    private static TValue GetOrAdd<TValue>(Dictionary<string, TValue> store, string key, Func<TValue> factory)
    {
        if (!store.TryGetValue(key, out var value))
        {
            value = factory();
            store[key] = value;
        }

        return value;
    }

    private void Log(params string[] parts)
    {
        lock (_gate)
        {
            _calls.Add(string.Join(' ', parts));
        }
    }
}
