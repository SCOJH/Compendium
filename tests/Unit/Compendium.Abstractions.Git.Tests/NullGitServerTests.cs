// -----------------------------------------------------------------------
// <copyright file="NullGitServerTests.cs" company="Sassy Solutions">
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
using Compendium.Abstractions.Git.Stubs;
using Compendium.Abstractions.Git.Webhooks;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for <see cref="NullGitServer"/>, the fail-fast stub for hosts
/// without a configured git server: every operation across all eleven ports
/// (including the explicit-interface implementations) must return a
/// <c>Git.NotConfigured</c> failure and never throw, and the capability matrix
/// must be empty.
/// </summary>
public sealed class NullGitServerTests
{
    private readonly NullGitServer _server = new();

    private static readonly GitConnection Connection = new()
    {
        Provider = NullGitServer.ProviderName,
        Credential = new GitCredential.AppInstallation("inst-1"),
    };

    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    private static void AssertNotConfigured(Result result)
    {
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.NotConfigured");
        result.Error.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public void Provider_IsNull()
    {
        // Arrange / Act / Assert
        _server.Provider.Should().Be("null");
        NullGitServer.ProviderName.Should().Be("null");
    }

    [Fact]
    public void Capabilities_AreEmpty_AndSupportNothing()
    {
        // Arrange / Act
        var capabilities = _server.Capabilities;

        // Assert
        capabilities.Provider.Should().Be("null");
        capabilities.Entries.Should().BeEmpty();
        foreach (var capability in Enum.GetValues<GitCapability>())
        {
            capabilities.Supports(capability).Should().BeFalse($"{capability} must be unsupported by the null server");
        }
    }

    [Fact]
    public void FacadeProperties_AllReturnTheSameInstance()
    {
        // Arrange / Act / Assert
        _server.Credentials.Should().BeSameAs(_server);
        _server.Repositories.Should().BeSameAs(_server);
        _server.Pipelines.Should().BeSameAs(_server);
        _server.CiConfiguration.Should().BeSameAs(_server);
        _server.Environments.Should().BeSameAs(_server);
        _server.BranchPolicies.Should().BeSameAs(_server);
        _server.AccessControl.Should().BeSameAs(_server);
        _server.Webhooks.Should().BeSameAs(_server);
        _server.WebhookIngestor.Should().BeSameAs(_server);
        _server.NamespaceProvisioner.Should().BeSameAs(_server);
    }

    [Fact]
    public async Task Credentials_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var broker = _server.Credentials;

        // Act / Assert
        AssertNotConfigured(await broker.MintAsync(Connection));
        AssertNotConfigured(await broker.MintAsync(Connection, new GitAccessTokenScope()));
        AssertNotConfigured(await broker.ValidateAsync(Connection));
        AssertNotConfigured(await broker.ResolveAppInstallationAsync("acme"));
        AssertNotConfigured(await broker.ResolveAppInstallationByIdAsync("inst-1"));
        AssertNotConfigured(await broker.ListAppInstallationsAsync());
    }

    [Fact]
    public async Task Repositories_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var repositories = _server.Repositories;
        var template = new CreateRepositoryFromTemplate
        {
            Template = new GitRepositoryRef("platform", "template"),
            Namespace = "acme",
            Name = "billing",
        };

        // Act / Assert
        AssertNotConfigured(await repositories.CreateFromTemplateAsync(Connection, template));
        AssertNotConfigured(await repositories.GetAsync(Connection, Repo));
        AssertNotConfigured(await repositories.ListAsync(Connection, "acme"));
        AssertNotConfigured(await repositories.FileExistsAsync(Connection, Repo, ".bootstrapped"));
        AssertNotConfigured(await repositories.ListCommitsAsync(Connection, Repo, reference: null, limit: 10));
        AssertNotConfigured(await repositories.ListBranchesAsync(Connection, Repo));
        AssertNotConfigured(await repositories.CreateTagAsync(Connection, Repo, "v1.0.0"));
        AssertNotConfigured(await repositories.ListTagsAsync(Connection, Repo));
        AssertNotConfigured(await repositories.CreateReleaseAsync(Connection, Repo, new CreateGitRelease { TagName = "v1.0.0" }));
    }

    [Fact]
    public async Task Pipelines_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var pipelines = _server.Pipelines;
        var trigger = new TriggerGitPipeline { Pipeline = "bootstrap.yml", Reference = "main" };

        // Act / Assert
        AssertNotConfigured(await pipelines.TriggerAsync(Connection, Repo, trigger));
        AssertNotConfigured(await pipelines.GetRunAsync(Connection, Repo, "run-1"));
        AssertNotConfigured(await pipelines.ListRunsAsync(Connection, Repo, new ListGitPipelineRuns()));
    }

    [Fact]
    public async Task CiConfiguration_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var config = _server.CiConfiguration;
        var scope = new GitConfigurationScope.Repository(Repo);
        var payload = new Dictionary<string, string> { ["KEY"] = "value" };

        // Act / Assert
        AssertNotConfigured(await config.SetSecretsAsync(Connection, scope, payload));
        AssertNotConfigured(await config.DeleteSecretAsync(Connection, scope, "KEY"));
        AssertNotConfigured(await config.SetVariablesAsync(Connection, scope, payload));
        AssertNotConfigured(await config.DeleteVariableAsync(Connection, scope, "KEY"));
    }

    [Fact]
    public async Task Environments_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var environments = _server.Environments;

        // Act / Assert
        AssertNotConfigured(await environments.EnsureAsync(Connection, Repo, new EnsureGitEnvironment { Name = "production" }));
        AssertNotConfigured(await environments.DeleteAsync(Connection, Repo, "production"));
        AssertNotConfigured(await environments.ListAsync(Connection, Repo));
    }

    [Fact]
    public async Task BranchPolicies_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var policies = _server.BranchPolicies;

        // Act / Assert
        AssertNotConfigured(await policies.ApplyAsync(Connection, Repo, new GitBranchPolicyRequest { Pattern = "main" }));
        AssertNotConfigured(await policies.RemoveAsync(Connection, Repo, "policy-main"));
        AssertNotConfigured(await policies.ListAsync(Connection, Repo));
    }

    [Fact]
    public async Task AccessControl_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var accessControl = _server.AccessControl;

        // Act / Assert
        AssertNotConfigured(await accessControl.EnsureTeamAsync(Connection, "acme", new EnsureGitTeam { Name = "Platform" }));
        AssertNotConfigured(await accessControl.AddTeamMemberAsync(Connection, "acme", "platform", "octocat", GitTeamRole.Member));
        AssertNotConfigured(await accessControl.SetTeamRepositoryRoleAsync(Connection, "acme", "platform", Repo, GitRepositoryRole.Write));
        AssertNotConfigured(await accessControl.SetUserRepositoryRoleAsync(Connection, Repo, "octocat", GitRepositoryRole.Admin));
        AssertNotConfigured(await accessControl.RemoveUserFromRepositoryAsync(Connection, Repo, "octocat"));
    }

    [Fact]
    public async Task Webhooks_EveryMethod_ReturnsNotConfigured()
    {
        // Arrange
        var webhooks = _server.Webhooks;
        var target = new GitWebhookTarget.Repository(Repo);
        var request = new EnsureGitWebhook
        {
            Url = new Uri("https://platform.invalid/webhooks/git"),
            Secret = "secret",
            Events = ["push"],
        };

        // Act / Assert
        AssertNotConfigured(await webhooks.EnsureAsync(Connection, target, request));
        AssertNotConfigured(await webhooks.DeleteAsync(Connection, target, "hook-1"));
        AssertNotConfigured(await webhooks.ListAsync(Connection, target));
    }

    [Fact]
    public void WebhookIngestor_Parse_ReturnsNotConfigured()
    {
        // Arrange
        var delivery = new GitWebhookDelivery
        {
            Body = "{}",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };

        // Act
        var result = _server.WebhookIngestor.Parse(delivery, "secret");

        // Assert
        AssertNotConfigured(result);
    }

    [Fact]
    public async Task NamespaceProvisioner_CreateNamespace_ReturnsNotConfigured()
    {
        // Arrange
        var provisioner = _server.NamespaceProvisioner;

        // Act
        var result = await provisioner.CreateNamespaceAsync(Connection, new CreateGitNamespace { Name = "NXS-Acme" });

        // Assert
        AssertNotConfigured(result);
    }
}
