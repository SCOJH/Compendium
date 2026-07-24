// -----------------------------------------------------------------------
// <copyright file="InMemoryGitServerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Compendium.Abstractions.Git.AccessControl;
using Compendium.Abstractions.Git.CiConfiguration;
using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Environments;
using Compendium.Abstractions.Git.Pipelines;
using Compendium.Abstractions.Git.Protection;
using Compendium.Abstractions.Git.Provisioning;
using Compendium.Abstractions.Git.Repositories;
using Compendium.Abstractions.Git.Webhooks;
using Compendium.Testing.Git;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for behaviors of <see cref="InMemoryGitServer"/> not already
/// exercised by <see cref="InMemoryGitServerContractTests"/>: seeding hooks,
/// write-only secret semantics, run transitions, tag/release/commit reads,
/// installation discovery, the call log, and inbound webhook parsing branches.
/// </summary>
public sealed class InMemoryGitServerTests
{
    private readonly InMemoryGitServer _server = new();

    private static GitConnection TokenConnection => new()
    {
        Provider = InMemoryGitServer.ProviderName,
        Credential = new GitCredential.ServiceAccountToken("token"),
    };

    private static GitRepositoryRef Repo => new("acme", "billing");

    // ---- seeding & repository reads -------------------------------------

    [Fact]
    public async Task SeedFile_ThenFileExists_ReturnsTrueForSeededPathOnly()
    {
        // Arrange
        _server.SeedRepository(Repo);
        _server.SeedFile(Repo, "docs/README.md");

        // Act
        var seeded = await _server.Repositories.FileExistsAsync(TokenConnection, Repo, "docs/README.md");
        var absent = await _server.Repositories.FileExistsAsync(TokenConnection, Repo, "docs/MISSING.md");

        // Assert
        seeded.IsSuccess.Should().BeTrue();
        seeded.Value.Should().BeTrue();
        absent.Value.Should().BeFalse();
    }

    [Fact]
    public async Task CreateFromTemplate_SeedsBootstrappedMarkerFromInitialFiles()
    {
        // Arrange
        var request = new CreateRepositoryFromTemplate
        {
            Template = new GitRepositoryRef("platform", "template-dotnet"),
            Namespace = "acme",
            Name = "new-service",
        };

        // Act
        var created = await _server.Repositories.CreateFromTemplateAsync(TokenConnection, request);
        var exists = await _server.Repositories.FileExistsAsync(TokenConnection, created.Value.Ref, ".bootstrapped");

        // Assert
        created.IsSuccess.Should().BeTrue();
        exists.Value.Should().BeTrue(".bootstrapped is seeded from InitialFiles on every template creation");
    }

    [Fact]
    public async Task ListCommits_ReturnsSeededCommitNewestFirst_AndRespectsLimit()
    {
        // Arrange
        _server.SeedRepository(Repo);

        // Act
        var all = await _server.Repositories.ListCommitsAsync(TokenConnection, Repo, reference: null, limit: 10);
        var none = await _server.Repositories.ListCommitsAsync(TokenConnection, Repo, reference: null, limit: 0);

        // Assert
        all.IsSuccess.Should().BeTrue();
        all.Value.Should().ContainSingle().Which.Message.Should().Be("Initial commit");
        none.Value.Should().BeEmpty("a zero limit yields no commits");
    }

    [Fact]
    public async Task ListCommits_OnMissingRepository_FailsRepositoryNotFound()
    {
        // Arrange / Act
        var result = await _server.Repositories.ListCommitsAsync(TokenConnection, Repo, reference: null, limit: 5);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task CreateTag_OnMissingRepository_FailsRepositoryNotFound()
    {
        // Arrange / Act
        var result = await _server.Repositories.CreateTagAsync(TokenConnection, Repo, "v1.0.0");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task CreateRelease_WhenTagAbsent_CreatesTheTag()
    {
        // Arrange
        _server.SeedRepository(Repo);

        // Act
        var release = await _server.Repositories.CreateReleaseAsync(
            TokenConnection, Repo, new CreateGitRelease { TagName = "v2.0.0" });
        var tags = await _server.Repositories.ListTagsAsync(TokenConnection, Repo);

        // Assert
        release.IsSuccess.Should().BeTrue();
        release.Value.TagName.Should().Be("v2.0.0");
        tags.Value.Should().ContainSingle(t => t.Name == "v2.0.0");
    }

    // ---- CI configuration: write-only secrets, readable variables -------

    [Fact]
    public async Task GetSecretNames_ReturnsNamesOnlyAfterSetSecrets()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var scope = new GitConfigurationScope.Repository(Repo);
        _server.GetSecretNames(scope).Should().BeEmpty("no secrets set yet");

        // Act
        await _server.CiConfiguration.SetSecretsAsync(
            TokenConnection, scope, new Dictionary<string, string> { ["API_KEY"] = "s3cret", ["DB_PASS"] = "hunter2" });
        var names = _server.GetSecretNames(scope);

        // Assert
        names.Should().BeEquivalentTo("API_KEY", "DB_PASS");
        names.Should().NotContain("s3cret", "only names are retained; secret values are write-only");
    }

    [Fact]
    public async Task GetVariables_RoundTripsValues()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var scope = new GitConfigurationScope.Repository(Repo);

        // Act
        await _server.CiConfiguration.SetVariablesAsync(
            TokenConnection, scope, new Dictionary<string, string> { ["LOG_LEVEL"] = "debug" });
        var variables = _server.GetVariables(scope);

        // Assert
        variables.Should().ContainKey("LOG_LEVEL").WhoseValue.Should().Be("debug");
    }

    [Fact]
    public void GetVariables_ForUnknownScope_ReturnsEmpty()
    {
        // Arrange
        var scope = new GitConfigurationScope.Namespace("nobody");

        // Act
        var variables = _server.GetVariables(scope);

        // Assert
        variables.Should().BeEmpty();
    }

    // ---- pipelines ------------------------------------------------------

    [Fact]
    public async Task CompleteRun_TransitionsStatus_AndReturnsFalseForUnknownRun()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var triggered = await _server.Pipelines.TriggerAsync(
            TokenConnection, Repo, new TriggerGitPipeline { Pipeline = "bootstrap.yml", Reference = "main" });
        var runId = triggered.Value.RunId!;

        // Act
        var transitioned = _server.CompleteRun(runId, GitPipelineStatus.Succeeded);
        var unknown = _server.CompleteRun("run-does-not-exist", GitPipelineStatus.Failed);
        var run = await _server.Pipelines.GetRunAsync(TokenConnection, Repo, runId);

        // Assert
        transitioned.Should().BeTrue();
        unknown.Should().BeFalse();
        run.IsSuccess.Should().BeTrue();
        run.Value.Status.Should().Be(GitPipelineStatus.Succeeded);
    }

    [Fact]
    public async Task GetRun_ForUnknownRun_FailsRunNotFound()
    {
        // Arrange / Act
        var result = await _server.Pipelines.GetRunAsync(TokenConnection, Repo, "run-404");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.RunNotFound");
    }

    [Fact]
    public async Task ListRuns_FiltersByPipelineAndReference()
    {
        // Arrange
        _server.SeedRepository(Repo);
        await _server.Pipelines.TriggerAsync(
            TokenConnection, Repo, new TriggerGitPipeline { Pipeline = "build.yml", Reference = "main" });
        await _server.Pipelines.TriggerAsync(
            TokenConnection, Repo, new TriggerGitPipeline { Pipeline = "deploy.yml", Reference = "release" });

        // Act
        var filtered = await _server.Pipelines.ListRunsAsync(
            TokenConnection, Repo, new ListGitPipelineRuns { Pipeline = "build.yml" });

        // Assert
        filtered.IsSuccess.Should().BeTrue();
        filtered.Value.Should().ContainSingle().Which.Pipeline.Should().Be("build.yml");
    }

    // ---- access control -------------------------------------------------

    [Fact]
    public async Task AddTeamMember_OnUnknownTeam_FailsTeamNotFound()
    {
        // Arrange / Act
        var result = await _server.AccessControl.AddTeamMemberAsync(
            TokenConnection, "acme", "ghost-team", "octocat", GitTeamRole.Member);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.TeamNotFound");
    }

    [Fact]
    public async Task EnsureTeam_ThenAddTeamMember_Succeeds()
    {
        // Arrange
        var team = await _server.AccessControl.EnsureTeamAsync(
            TokenConnection, "acme", new EnsureGitTeam { Name = "Platform Team" });

        // Act
        var added = await _server.AccessControl.AddTeamMemberAsync(
            TokenConnection, "acme", team.Value.Slug, "octocat", GitTeamRole.Maintainer);

        // Assert
        team.Value.Slug.Should().Be("platform-team");
        added.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SetUserRepositoryRole_OnMissingRepository_FailsRepositoryNotFound()
    {
        // Arrange / Act
        var result = await _server.AccessControl.SetUserRepositoryRoleAsync(
            TokenConnection, Repo, "octocat", GitRepositoryRole.Write);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    // ---- credential broker ----------------------------------------------

    [Fact]
    public async Task ResolveAppInstallation_WhenAbsent_FailsAppNotInstalledWithInstallUrl()
    {
        // Arrange / Act
        var result = await _server.Credentials.ResolveAppInstallationAsync("nobody");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.AppNotInstalled");
        result.Error.Metadata.Should().ContainKey("installUrl");
    }

    [Fact]
    public async Task ResolveAppInstallation_WhenSeeded_ReturnsInstallation()
    {
        // Arrange
        _server.SeedInstallation(new GitInstallationInfo("inst-1", "acme", GitAccountType.Organization));

        // Act
        var result = await _server.Credentials.ResolveAppInstallationAsync("acme");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.InstallationId.Should().Be("inst-1");
    }

    [Fact]
    public async Task ListAppInstallations_ReturnsSeededInstallations()
    {
        // Arrange
        _server.SeedInstallation(new GitInstallationInfo("inst-1", "acme", GitAccountType.Organization));
        _server.SeedInstallation(new GitInstallationInfo("inst-2", "globex", GitAccountType.User));

        // Act
        var result = await _server.Credentials.ListAppInstallationsAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Select(i => i.InstallationId).Should().BeEquivalentTo("inst-1", "inst-2");
    }

    [Fact]
    public async Task Validate_WithUnknownAppInstallation_FailsAuthenticationFailed()
    {
        // Arrange
        var connection = new GitConnection
        {
            Provider = InMemoryGitServer.ProviderName,
            Credential = new GitCredential.AppInstallation("ghost"),
        };

        // Act
        var result = await _server.Credentials.ValidateAsync(connection);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.AuthenticationFailed");
    }

    [Fact]
    public async Task Validate_WithSeededAppInstallation_ReportsInstallationIdentity()
    {
        // Arrange
        _server.SeedInstallation(new GitInstallationInfo("inst-1", "acme", GitAccountType.Organization));
        var connection = new GitConnection
        {
            Provider = InMemoryGitServer.ProviderName,
            Credential = new GitCredential.AppInstallation("inst-1"),
        };

        // Act
        var result = await _server.Credentials.ValidateAsync(connection);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AccountLogin.Should().Be("acme");
        result.Value.AccountType.Should().Be(GitAccountType.Organization);
    }

    // ---- namespace provisioning -----------------------------------------

    [Fact]
    public async Task CreateNamespace_OnDuplicate_FailsConflict()
    {
        // Arrange
        var first = await _server.NamespaceProvisioner.CreateNamespaceAsync(
            TokenConnection, new CreateGitNamespace { Name = "NXS-Acme" });

        // Act
        var second = await _server.NamespaceProvisioner.CreateNamespaceAsync(
            TokenConnection, new CreateGitNamespace { Name = "NXS-Acme" });

        // Assert
        first.IsSuccess.Should().BeTrue();
        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("Git.Conflict");
    }

    // ---- inbound webhook parsing ----------------------------------------

    [Fact]
    public void Parse_MissingSignatureHeader_IsRejected()
    {
        // Arrange
        var delivery = new GitWebhookDelivery
        {
            Body = "{\"type\":\"push\",\"deliveryId\":\"d-1\"}",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, InMemoryGitServer.WellKnownWebhookSecret);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.WebhookSignatureInvalid");
    }

    [Fact]
    public void Parse_MalformedJsonBody_FailsMalformedDelivery()
    {
        // Arrange
        var delivery = SignedDelivery("{ this is not json");

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, InMemoryGitServer.WellKnownWebhookSecret);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.MalformedDelivery");
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void Parse_EnvelopeMissingType_FailsMalformedDelivery()
    {
        // Arrange
        var delivery = SignedDelivery("{}");

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, InMemoryGitServer.WellKnownWebhookSecret);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.MalformedDelivery");
    }

    [Fact]
    public void Parse_PushDelivery_ParsesReferenceHeadAndRepository()
    {
        // Arrange
        var delivery = SignedDelivery(JsonSerializer.Serialize(new
        {
            type = "push",
            deliveryId = "d-push",
            repository = "acme/billing",
            reference = "refs/heads/main",
            headCommitSha = "abc123",
        }));

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, InMemoryGitServer.WellKnownWebhookSecret);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var push = result.Value.Should().BeOfType<GitWebhookEvent.Push>().Which;
        push.DeliveryId.Should().Be("d-push");
        push.Reference.Should().Be("refs/heads/main");
        push.HeadCommitSha.Should().Be("abc123");
        push.Repository.Should().Be(new GitRepositoryRef("acme", "billing"));
    }

    [Fact]
    public void Parse_UnknownType_ReturnsUnsupportedEventWithDeliveryId()
    {
        // Arrange
        var delivery = SignedDelivery(JsonSerializer.Serialize(new
        {
            type = "issues",
            deliveryId = "d-unsupported",
            repository = "acme/billing",
        }));

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, InMemoryGitServer.WellKnownWebhookSecret);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var unsupported = result.Value.Should().BeOfType<GitWebhookEvent.Unsupported>().Which;
        unsupported.ProviderEventType.Should().Be("issues");
        unsupported.DeliveryId.Should().Be("d-unsupported");
    }

    [Fact]
    public void Parse_ConnectionChanged_ParsesInstallationFields()
    {
        // Arrange
        var delivery = SignedDelivery(JsonSerializer.Serialize(new
        {
            type = "connection_changed",
            deliveryId = "d-conn",
            @namespace = "acme",
            accountType = "User",
            installationId = "inst-9",
            change = "Suspended",
        }));

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, InMemoryGitServer.WellKnownWebhookSecret);

        // Assert
        result.IsSuccess.Should().BeTrue();
        var changed = result.Value.Should().BeOfType<GitWebhookEvent.ConnectionChanged>().Which;
        changed.DeliveryId.Should().Be("d-conn");
        changed.Namespace.Should().Be("acme");
        changed.AccountType.Should().Be(GitAccountType.User);
        changed.InstallationId.Should().Be("inst-9");
        changed.Change.Should().Be(GitConnectionChangeKind.Suspended);
    }

    // ---- call log -------------------------------------------------------

    [Fact]
    public async Task Calls_RecordsPerformedOperationsInOrder()
    {
        // Arrange / Act
        await _server.NamespaceProvisioner.CreateNamespaceAsync(
            TokenConnection, new CreateGitNamespace { Name = "acme" });
        await _server.Credentials.MintAsync(TokenConnection);

        // Assert
        _server.Calls.Should().Contain("CreateNamespace acme");
        _server.Calls.Should().Contain(c => c.StartsWith("Mint"));
    }

    // ---- additional fake behaviors (branch coverage) --------------------

    [Fact]
    public async Task Validate_WithTokenCredential_ReportsDefaultIdentity()
    {
        // Arrange / Act
        var result = await _server.Credentials.ValidateAsync(TokenConnection);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.AccountLogin.Should().Be("in-memory-user");
        result.Value.AccountType.Should().Be(GitAccountType.User);
    }

    [Fact]
    public async Task ListRepositories_ReturnsOnlyTheNamespacesRepositories()
    {
        // Arrange
        _server.SeedRepository(new GitRepositoryRef("acme", "one"));
        _server.SeedRepository(new GitRepositoryRef("acme", "two"));
        _server.SeedRepository(new GitRepositoryRef("globex", "other"));

        // Act
        var result = await _server.Repositories.ListAsync(TokenConnection, "acme");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Select(r => r.Ref.Name).Should().BeEquivalentTo("one", "two");
    }

    [Fact]
    public async Task ListBranches_ReturnsTheSeededDefaultBranch()
    {
        // Arrange
        _server.SeedRepository(Repo);

        // Act
        var result = await _server.Repositories.ListBranchesAsync(TokenConnection, Repo);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle().Which.Name.Should().Be("main");
        result.Value[0].Protected.Should().BeFalse();
    }

    [Fact]
    public async Task ListBranches_OnMissingRepository_FailsRepositoryNotFound()
    {
        // Arrange / Act
        var result = await _server.Repositories.ListBranchesAsync(TokenConnection, Repo);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task Webhooks_OnRepositoryTargetForUnseededRepo_AreStoredAndListed()
    {
        // Arrange
        var target = new GitWebhookTarget.Repository(new GitRepositoryRef("ghost", "repo"));
        var request = new EnsureGitWebhook
        {
            Url = new Uri("https://platform.invalid/webhooks/git"),
            Secret = "secret",
            Events = ["push"],
        };

        // Act
        var ensured = await _server.Webhooks.EnsureAsync(TokenConnection, target, request);
        var list = await _server.Webhooks.ListAsync(TokenConnection, target);

        // Assert
        ensured.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle().Which.Id.Should().Be(ensured.Value.Id);
    }

    [Fact]
    public async Task CreateTag_OnSeededRepository_TagsTheDefaultBranchHead()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var branches = await _server.Repositories.ListBranchesAsync(TokenConnection, Repo);
        var headSha = branches.Value.Single().Sha;

        // Act
        var tag = await _server.Repositories.CreateTagAsync(TokenConnection, Repo, "v1.0.0");

        // Assert
        tag.IsSuccess.Should().BeTrue();
        tag.Value.Name.Should().Be("v1.0.0");
        tag.Value.Sha.Should().Be(headSha);
    }

    [Fact]
    public async Task RepositoryReads_OnMissingRepository_AllFailRepositoryNotFound()
    {
        // Arrange / Act / Assert
        (await _server.Repositories.ListTagsAsync(TokenConnection, Repo)).Error.Code.Should().Be("Git.RepositoryNotFound");
        (await _server.Repositories.CreateReleaseAsync(TokenConnection, Repo, new CreateGitRelease { TagName = "v1" }))
            .Error.Code.Should().Be("Git.RepositoryNotFound");
        (await _server.Pipelines.TriggerAsync(TokenConnection, Repo, new TriggerGitPipeline { Pipeline = "b.yml", Reference = "main" }))
            .Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task DeleteVariable_RemovesTheVariable_AndIsIdempotentWhenAbsent()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var scope = new GitConfigurationScope.Repository(Repo);
        await _server.CiConfiguration.SetVariablesAsync(
            TokenConnection, scope, new Dictionary<string, string> { ["LOG_LEVEL"] = "debug" });

        // Act
        var removed = await _server.CiConfiguration.DeleteVariableAsync(TokenConnection, scope, "LOG_LEVEL");
        var absent = await _server.CiConfiguration.DeleteVariableAsync(TokenConnection, scope, "NEVER_SET");

        // Assert
        removed.IsSuccess.Should().BeTrue();
        absent.IsSuccess.Should().BeTrue();
        _server.GetVariables(scope).Should().NotContainKey("LOG_LEVEL");
    }

    [Fact]
    public async Task SetSecrets_AtEnvironmentScope_RetainsTheNameUnderThatScope()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var scope = new GitConfigurationScope.Environment(Repo, "production");

        // Act
        await _server.CiConfiguration.SetSecretsAsync(
            TokenConnection, scope, new Dictionary<string, string> { ["DEPLOY_KEY"] = "value" });

        // Assert
        _server.GetSecretNames(scope).Should().ContainSingle().Which.Should().Be("DEPLOY_KEY");
    }

    [Fact]
    public async Task Environment_EnsureDeleteList_CoverMissingRepositoryAndRoundTrip()
    {
        // Arrange
        var missing = await _server.Environments.EnsureAsync(TokenConnection, Repo, new EnsureGitEnvironment { Name = "production" });
        missing.Error.Code.Should().Be("Git.RepositoryNotFound");
        (await _server.Environments.ListAsync(TokenConnection, Repo)).Error.Code.Should().Be("Git.RepositoryNotFound");

        _server.SeedRepository(Repo);
        await _server.Environments.EnsureAsync(TokenConnection, Repo, new EnsureGitEnvironment { Name = "production" });

        // Act
        var deleted = await _server.Environments.DeleteAsync(TokenConnection, Repo, "production");
        var list = await _server.Environments.ListAsync(TokenConnection, Repo);

        // Assert
        deleted.IsSuccess.Should().BeTrue();
        list.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task BranchPolicy_ApplyRemoveList_CoverMissingRepositoryAndRoundTrip()
    {
        // Arrange
        var missing = await _server.BranchPolicies.ApplyAsync(TokenConnection, Repo, new GitBranchPolicyRequest { Pattern = "main" });
        missing.Error.Code.Should().Be("Git.RepositoryNotFound");
        (await _server.BranchPolicies.ListAsync(TokenConnection, Repo)).Error.Code.Should().Be("Git.RepositoryNotFound");

        _server.SeedRepository(Repo);
        var policy = await _server.BranchPolicies.ApplyAsync(TokenConnection, Repo, new GitBranchPolicyRequest { Pattern = "main" });

        // Act
        var removed = await _server.BranchPolicies.RemoveAsync(TokenConnection, Repo, policy.Value.Id);
        var list = await _server.BranchPolicies.ListAsync(TokenConnection, Repo);

        // Assert
        removed.IsSuccess.Should().BeTrue();
        list.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task AccessControl_UserAndTeamRepositoryRoles_RoundTrip()
    {
        // Arrange
        _server.SeedRepository(Repo);
        var team = await _server.AccessControl.EnsureTeamAsync(TokenConnection, "acme", new EnsureGitTeam { Name = "Platform" });

        // Act
        var teamRole = await _server.AccessControl.SetTeamRepositoryRoleAsync(
            TokenConnection, "acme", team.Value.Slug, Repo, GitRepositoryRole.Maintain);
        var userRole = await _server.AccessControl.SetUserRepositoryRoleAsync(
            TokenConnection, Repo, "octocat", GitRepositoryRole.Admin);
        var removed = await _server.AccessControl.RemoveUserFromRepositoryAsync(TokenConnection, Repo, "octocat");

        // Assert
        teamRole.IsSuccess.Should().BeTrue();
        userRole.IsSuccess.Should().BeTrue();
        removed.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Webhooks_OnNamespaceTarget_EnsureListDeleteRoundTrip()
    {
        // Arrange
        var target = new GitWebhookTarget.Namespace("acme");
        var request = new EnsureGitWebhook
        {
            Url = new Uri("https://platform.invalid/webhooks/git"),
            Secret = "secret",
            Events = ["push"],
        };

        // Act
        var ensured = await _server.Webhooks.EnsureAsync(TokenConnection, target, request);
        var list = await _server.Webhooks.ListAsync(TokenConnection, target);
        var deleted = await _server.Webhooks.DeleteAsync(TokenConnection, target, ensured.Value.Id);
        var afterDelete = await _server.Webhooks.ListAsync(TokenConnection, target);

        // Assert
        ensured.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle();
        deleted.IsSuccess.Should().BeTrue();
        afterDelete.Value.Should().BeEmpty();
    }

    private static GitWebhookDelivery SignedDelivery(string body) => new()
    {
        Body = body,
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-InMemory-Signature"] = InMemoryGitServer.WellKnownWebhookSecret,
        },
    };
}
