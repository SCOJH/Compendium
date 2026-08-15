// -----------------------------------------------------------------------
// <copyright file="GitWebhookModelsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Pipelines;
using Compendium.Abstractions.Git.Repositories;
using Compendium.Abstractions.Git.Webhooks;
using Compendium.Abstractions.Git.Connections;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for the inbound/outbound webhook DTO records: the neutral event
/// union, the raw delivery, the subscription target union, and the request
/// record whose <c>ToString()</c> must redact its shared secret.
/// </summary>
public sealed class GitWebhookModelsTests
{
    private static readonly GitRepositoryRef Ref = new("acme", "billing");

    [Fact]
    public void Push_CarriesReferenceHeadAndBaseMembers()
    {
        // Arrange / Act
        var push = new GitWebhookEvent.Push("refs/heads/main", "sha1") { DeliveryId = "d-1", Repository = Ref };

        // Assert
        push.Reference.Should().Be("refs/heads/main");
        push.HeadCommitSha.Should().Be("sha1");
        push.DeliveryId.Should().Be("d-1");
        push.Repository.Should().Be(Ref);
    }

    [Fact]
    public void TagPushed_CarriesTagAndCommit()
    {
        // Arrange / Act
        var tagPushed = new GitWebhookEvent.TagPushed("v1.0.0", "sha1") { DeliveryId = "d-2" };

        // Assert
        tagPushed.Tag.Should().Be("v1.0.0");
        tagPushed.CommitSha.Should().Be("sha1");
        tagPushed.DeliveryId.Should().Be("d-2");
        tagPushed.Repository.Should().BeNull();
    }

    [Fact]
    public void PullRequestChanged_CarriesActionNumberAndBranches()
    {
        // Arrange / Act
        var pr = new GitWebhookEvent.PullRequestChanged("opened", 42, "feature/x", "main") { DeliveryId = "d-3" };

        // Assert
        pr.Action.Should().Be("opened");
        pr.Number.Should().Be(42);
        pr.SourceReference.Should().Be("feature/x");
        pr.TargetReference.Should().Be("main");
        pr.DeliveryId.Should().Be("d-3");
    }

    [Fact]
    public void PipelineRunCompleted_CarriesRunPipelineStatusAndReference()
    {
        // Arrange / Act
        var completed = new GitWebhookEvent.PipelineRunCompleted("run-1", "build.yml", GitPipelineStatus.Succeeded, "main")
        {
            DeliveryId = "d-4",
        };

        // Assert
        completed.RunId.Should().Be("run-1");
        completed.Pipeline.Should().Be("build.yml");
        completed.Status.Should().Be(GitPipelineStatus.Succeeded);
        completed.Reference.Should().Be("main");
    }

    [Fact]
    public void ConnectionChanged_CarriesInstallationFields()
    {
        // Arrange / Act
        var changed = new GitWebhookEvent.ConnectionChanged("acme", GitAccountType.Organization, "inst-1", GitConnectionChangeKind.Uninstalled)
        {
            DeliveryId = "d-5",
        };

        // Assert
        changed.Namespace.Should().Be("acme");
        changed.AccountType.Should().Be(GitAccountType.Organization);
        changed.InstallationId.Should().Be("inst-1");
        changed.Change.Should().Be(GitConnectionChangeKind.Uninstalled);
    }

    [Fact]
    public void Unsupported_CarriesProviderEventType()
    {
        // Arrange / Act
        var unsupported = new GitWebhookEvent.Unsupported("issues") { DeliveryId = "d-6" };

        // Assert
        unsupported.ProviderEventType.Should().Be("issues");
        unsupported.DeliveryId.Should().Be("d-6");
    }

    [Fact]
    public void GitWebhookDelivery_ProjectsBodyAndHeaders()
    {
        // Arrange / Act
        var delivery = new GitWebhookDelivery
        {
            Body = "{}",
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["X-Sig"] = "v" },
        };

        // Assert
        delivery.Body.Should().Be("{}");
        delivery.Headers.Should().ContainKey("x-sig").WhoseValue.Should().Be("v");
    }

    [Fact]
    public void WebhookTarget_RepositoryAndNamespaceVariantsProjectTheirValues()
    {
        // Arrange / Act
        var repoTarget = new GitWebhookTarget.Repository(Ref);
        var namespaceTarget = new GitWebhookTarget.Namespace("acme");

        // Assert
        repoTarget.Ref.Should().Be(Ref);
        namespaceTarget.Name.Should().Be("acme");
    }

    [Fact]
    public void EnsureGitWebhook_DefaultsActiveTrue_AndRedactsSecretInToString()
    {
        // Arrange
        var request = new EnsureGitWebhook
        {
            Url = new Uri("https://platform.invalid/webhooks/git"),
            Secret = "top-secret-signing-key",
            Events = ["push", "workflow_run"],
        };

        // Act
        var text = request.ToString();

        // Assert
        request.Active.Should().BeTrue();
        request.Events.Should().BeEquivalentTo("push", "workflow_run");
        text.Should().Contain("Secret=***");
        text.Should().NotContain("top-secret-signing-key");
        text.Should().Contain("push");
    }

    [Fact]
    public void GitWebhookSubscription_ProjectsAllProperties()
    {
        // Arrange
        var url = new Uri("https://platform.invalid/webhooks/git");

        // Act
        var subscription = new GitWebhookSubscription("hook-1", url, ["push"], Active: false);
        var (id, subUrl, events, active) = subscription;

        // Assert
        id.Should().Be("hook-1");
        subUrl.Should().Be(url);
        events.Should().ContainSingle().Which.Should().Be("push");
        active.Should().BeFalse();
    }
}
