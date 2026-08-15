// -----------------------------------------------------------------------
// <copyright file="GitHubWebhookIngestorTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Security.Cryptography;
using System.Text;
using Compendium.Adapters.GitHub.Webhooks;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubWebhookIngestorTests
{
    private const string Secret = "hook-secret";
    private readonly GitHubWebhookIngestor _ingestor = new();

    [Fact]
    public void Parse_InvalidSignature_IsRejectedFailClosed()
    {
        var delivery = Delivery("push", "{}", validSignature: false);

        var result = _ingestor.Parse(delivery, Secret);

        result.Error.Code.Should().Be("Git.WebhookSignatureInvalid");
    }

    [Fact]
    public void Parse_MissingSignature_IsRejected()
    {
        var delivery = Delivery("push", "{}", includeSignature: false);

        _ingestor.Parse(delivery, Secret).Error.Code.Should().Be("Git.WebhookSignatureInvalid");
    }

    [Fact]
    public void Parse_EmptySecret_IsRejected()
    {
        _ingestor.Parse(Delivery("push", "{}"), string.Empty).Error.Code.Should().Be("Git.WebhookSignatureInvalid");
    }

    [Fact]
    public void Parse_MissingDeliveryId_IsMalformed()
    {
        var delivery = Delivery("push", "{}", includeDelivery: false);

        _ingestor.Parse(delivery, Secret).Error.Code.Should().Be("Git.MalformedDelivery");
    }

    [Fact]
    public void Parse_MissingEventHeader_IsMalformed()
    {
        var delivery = Delivery("push", "{}", includeEvent: false);

        _ingestor.Parse(delivery, Secret).Error.Code.Should().Be("Git.MalformedDelivery");
    }

    [Fact]
    public void Parse_MalformedJson_IsMalformed()
    {
        var delivery = Delivery("push", "not json");

        _ingestor.Parse(delivery, Secret).Error.Code.Should().Be("Git.MalformedDelivery");
    }

    [Fact]
    public void Parse_Push_ProducesAPushEventWithRepository()
    {
        var body = """{"ref":"refs/heads/main","after":"abc123","repository":{"full_name":"acme/billing"}}""";

        var result = _ingestor.Parse(Delivery("push", body), Secret);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        var push = result.Value.Should().BeOfType<GitWebhookEvent.Push>().Subject;
        push.Reference.Should().Be("refs/heads/main");
        push.HeadCommitSha.Should().Be("abc123");
        push.DeliveryId.Should().Be("d-1");
        push.Repository.Should().Be(new GitRepositoryRef("acme", "billing"));
    }

    [Fact]
    public void Parse_TagPush_ProducesATagPushedEvent()
    {
        var body = """{"ref":"refs/tags/v1.2.3","after":"tagsha","repository":{"full_name":"acme/billing"}}""";

        var result = _ingestor.Parse(Delivery("push", body), Secret);

        var tag = result.Value.Should().BeOfType<GitWebhookEvent.TagPushed>().Subject;
        tag.Tag.Should().Be("v1.2.3");
        tag.CommitSha.Should().Be("tagsha");
    }

    [Fact]
    public void Parse_PullRequest_ProducesAPullRequestChangedEvent()
    {
        var body = """
        {"action":"opened","number":42,"pull_request":{"head":{"ref":"feature","sha":"headsha42"},"base":{"ref":"main"}},
         "repository":{"full_name":"acme/billing"}}
        """;

        var result = _ingestor.Parse(Delivery("pull_request", body), Secret);

        var pr = result.Value.Should().BeOfType<GitWebhookEvent.PullRequestChanged>().Subject;
        pr.Action.Should().Be("opened");
        pr.Number.Should().Be(42);
        pr.SourceReference.Should().Be("feature");
        pr.TargetReference.Should().Be("main");
        pr.SourceHeadSha.Should().Be("headsha42");
    }

    [Fact]
    public void Parse_PullRequestWithoutHeadSha_LeavesSourceHeadShaNull()
    {
        var body = """
        {"action":"opened","number":7,"pull_request":{"head":{"ref":"feature"},"base":{"ref":"main"}},
         "repository":{"full_name":"acme/billing"}}
        """;

        var result = _ingestor.Parse(Delivery("pull_request", body), Secret);

        var pr = result.Value.Should().BeOfType<GitWebhookEvent.PullRequestChanged>().Subject;
        pr.SourceHeadSha.Should().BeNull();
    }

    [Fact]
    public void Parse_WorkflowRunCompleted_ProducesAPipelineRunCompletedEvent()
    {
        var body = """
        {"action":"completed","workflow_run":{"id":555,"name":"CI","status":"completed","conclusion":"failure","head_branch":"main"},
         "repository":{"full_name":"acme/billing"}}
        """;

        var result = _ingestor.Parse(Delivery("workflow_run", body), Secret);

        var run = result.Value.Should().BeOfType<GitWebhookEvent.PipelineRunCompleted>().Subject;
        run.RunId.Should().Be("555");
        run.Pipeline.Should().Be("CI");
        run.Status.Should().Be(GitPipelineStatus.Failed);
        run.Reference.Should().Be("main");
    }

    [Fact]
    public void Parse_WorkflowRunInProgress_IsUnsupported()
    {
        var body = """{"action":"requested","workflow_run":{"id":1,"status":"in_progress"}}""";

        _ingestor.Parse(Delivery("workflow_run", body), Secret).Value
            .Should().BeOfType<GitWebhookEvent.Unsupported>();
    }

    [Theory]
    [InlineData("created", GitConnectionChangeKind.Installed)]
    [InlineData("deleted", GitConnectionChangeKind.Uninstalled)]
    [InlineData("suspend", GitConnectionChangeKind.Suspended)]
    [InlineData("unsuspend", GitConnectionChangeKind.Unsuspended)]
    [InlineData("new_permissions_accepted", GitConnectionChangeKind.RepositoriesChanged)]
    public void Parse_Installation_MapsTheChangeKind(string action, GitConnectionChangeKind expected)
    {
        var body = "{\"action\":\"" + action
            + "\",\"installation\":{\"id\":777,\"account\":{\"login\":\"acme\",\"type\":\"Organization\"}}}";

        var result = _ingestor.Parse(Delivery("installation", body), Secret);

        var change = result.Value.Should().BeOfType<GitWebhookEvent.ConnectionChanged>().Subject;
        change.Change.Should().Be(expected);
        change.Namespace.Should().Be("acme");
        change.InstallationId.Should().Be("777");
        change.AccountType.Should().Be(GitAccountType.Organization);
    }

    [Fact]
    public void Parse_InstallationRepositories_IsRepositoriesChanged()
    {
        var body = """
        {"action":"added","installation":{"id":777,"account":{"login":"octocat","type":"User"}}}
        """;

        var result = _ingestor.Parse(Delivery("installation_repositories", body), Secret);

        var change = result.Value.Should().BeOfType<GitWebhookEvent.ConnectionChanged>().Subject;
        change.Change.Should().Be(GitConnectionChangeKind.RepositoriesChanged);
        change.AccountType.Should().Be(GitAccountType.User);
    }

    [Fact]
    public void Parse_UnknownEvent_IsUnsupported()
    {
        var result = _ingestor.Parse(Delivery("star", "{}"), Secret);

        result.Value.Should().BeOfType<GitWebhookEvent.Unsupported>()
            .Which.ProviderEventType.Should().Be("star");
    }

    private static GitWebhookDelivery Delivery(
        string eventType,
        string body,
        bool validSignature = true,
        bool includeSignature = true,
        bool includeEvent = true,
        bool includeDelivery = true)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (includeSignature)
        {
            headers["X-Hub-Signature-256"] = validSignature ? Sign(body, Secret) : "sha256=deadbeef";
        }

        if (includeEvent)
        {
            headers["X-GitHub-Event"] = eventType;
        }

        if (includeDelivery)
        {
            headers["X-GitHub-Delivery"] = "d-1";
        }

        return new GitWebhookDelivery { Body = body, Headers = headers };
    }

    private static string Sign(string body, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    }
}
