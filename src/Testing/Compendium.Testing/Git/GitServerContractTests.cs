// -----------------------------------------------------------------------
// <copyright file="GitServerContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git;
using Compendium.Abstractions.Git.Capabilities;
using Compendium.Abstractions.Git.CiConfiguration;
using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Environments;
using Compendium.Abstractions.Git.Pipelines;
using Compendium.Abstractions.Git.Protection;
using Compendium.Abstractions.Git.Repositories;
using Compendium.Abstractions.Git.Webhooks;
using FluentAssertions;
using Xunit;

namespace Compendium.Testing.Git;

/// <summary>
/// The behavioral contract every <see cref="IGitServer"/> adapter must satisfy.
/// Inherit in the adapter's test suite and provide the fixture members; tests
/// for capabilities the adapter does not declare are skipped automatically
/// (capability honesty is itself asserted: a declared capability must work).
/// <see cref="InMemoryGitServer"/> subscribes to this contract, keeping the
/// fake and real adapters aligned.
/// </summary>
public abstract class GitServerContractTests
{
    /// <summary>Gets the server under test.</summary>
    protected abstract IGitServer Server { get; }

    /// <summary>Gets a connection with valid credentials for <see cref="Server"/>.</summary>
    protected abstract GitConnection Connection { get; }

    /// <summary>Gets a namespace where the connection can create repositories.</summary>
    protected abstract string Namespace { get; }

    /// <summary>Gets a template repository that exists and can be instantiated.</summary>
    protected abstract GitRepositoryRef TemplateRepository { get; }

    /// <summary>Gets the webhook secret used when building signed deliveries.</summary>
    protected abstract string WebhookSecret { get; }

    /// <summary>
    /// Builds a raw webhook delivery for <see cref="IGitWebhookIngestor.Parse"/>:
    /// signed with <see cref="WebhookSecret"/> when <paramref name="validSignature"/>
    /// is true, deliberately mis-signed otherwise.
    /// </summary>
    protected abstract GitWebhookDelivery CreateDelivery(bool validSignature);

    /// <summary>Generates a unique repository name for a test run.</summary>
    protected virtual string NewRepositoryName() => $"contract-{Guid.NewGuid():N}"[..24];

    /// <summary>The declared provider must match the facade discriminator and never be empty.</summary>
    [Fact]
    public void Capabilities_DeclaresTheServerProvider()
    {
        Server.Provider.Should().NotBeNullOrWhiteSpace();
        Server.Capabilities.Provider.Should().Be(Server.Provider);
    }

    /// <summary>An undeclared capability must fail with the standard machine code, never throw.</summary>
    [SkippableFact]
    public void EnsureSupported_UndeclaredCapability_FailsWithCapabilityNotSupported()
    {
        var undeclared = Enum.GetValues<GitCapability>()
            .FirstOrDefault(c => !Server.Capabilities.Supports(c), (GitCapability)(-1));
        Skip.If(undeclared == (GitCapability)(-1), "Adapter declares every capability.");

        var result = Server.Capabilities.EnsureSupported(undeclared);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{GitErrors.Prefix}.CapabilityNotSupported");
    }

    /// <summary>Minted tokens are non-empty, not yet expired, and carry a basic-auth username.</summary>
    [SkippableFact]
    public async Task Mint_ReturnsUsableShortLivedToken()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.AppInstallationAuth)
            || Server.Capabilities.Supports(GitCapability.ServiceAccountAuth));

        var result = await Server.Credentials.MintAsync(Connection);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Token.Should().NotBeNullOrWhiteSpace();
        result.Value.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
        result.Value.HttpBasicUsername.Should().NotBeNullOrWhiteSpace();
        result.Value.ToString().Should().NotContain(result.Value.Token, "tokens must be redacted in ToString()");
    }

    /// <summary>A valid credential validates and reports an identity.</summary>
    [Fact]
    public async Task Validate_ValidCredential_ReportsIdentity()
    {
        var result = await Server.Credentials.ValidateAsync(Connection);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.AccountLogin.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>Repository creation from a template round-trips through Get, and repeating the name conflicts.</summary>
    [SkippableFact]
    public async Task CreateFromTemplate_RoundTrips_AndDuplicateFails()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryFromTemplate));

        var request = new CreateRepositoryFromTemplate
        {
            Template = TemplateRepository,
            Namespace = Namespace,
            Name = NewRepositoryName(),
        };

        var created = await Server.Repositories.CreateFromTemplateAsync(Connection, request);
        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        created.Value.Ref.Namespace.Should().BeEquivalentTo(request.Namespace);
        created.Value.CloneUrl.Should().NotBeNullOrWhiteSpace();

        var fetched = await Server.Repositories.GetAsync(Connection, created.Value.Ref);
        fetched.IsSuccess.Should().BeTrue();

        var duplicate = await Server.Repositories.CreateFromTemplateAsync(Connection, request);
        duplicate.IsFailure.Should().BeTrue("creating the same repository twice must fail");
    }

    /// <summary>Reading an absent repository fails with RepositoryNotFound, never throws.</summary>
    [SkippableFact]
    public async Task Get_AbsentRepository_FailsWithRepositoryNotFound()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryManagement));

        var result = await Server.Repositories.GetAsync(
            Connection, new GitRepositoryRef(Namespace, NewRepositoryName()));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{GitErrors.Prefix}.RepositoryNotFound");
    }

    /// <summary>Secrets set at repository scope succeed and deleting an absent secret is idempotent.</summary>
    [SkippableFact]
    public async Task SetSecrets_Succeeds_AndDeleteIsIdempotent()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryFromTemplate)
            && Server.Capabilities.Supports(GitCapability.CiSecrets));

        var repo = await CreateRepositoryAsync();
        var scope = new GitConfigurationScope.Repository(repo);

        var set = await Server.CiConfiguration.SetSecretsAsync(
            Connection, scope, new Dictionary<string, string> { ["CONTRACT_SECRET"] = "s3cret" });
        set.IsSuccess.Should().BeTrue(set.IsFailure ? set.Error.Message : string.Empty);

        var deleteAbsent = await Server.CiConfiguration.DeleteSecretAsync(Connection, scope, "NEVER_SET");
        deleteAbsent.IsSuccess.Should().BeTrue("deleting an absent secret must be idempotent");
    }

    /// <summary>Triggering a pipeline succeeds; when a run id is returned, its status is readable.</summary>
    [SkippableFact]
    public async Task TriggerPipeline_Succeeds_AndRunIsReadableWhenIdReturned()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryFromTemplate)
            && Server.Capabilities.Supports(GitCapability.PipelineTrigger));

        var repo = await CreateRepositoryAsync();
        var triggered = await Server.Pipelines.TriggerAsync(Connection, repo, new TriggerGitPipeline
        {
            Pipeline = "bootstrap.yml",
            Reference = "main",
        });

        triggered.IsSuccess.Should().BeTrue(triggered.IsFailure ? triggered.Error.Message : string.Empty);

        if (triggered.Value.RunId is { } runId && Server.Capabilities.Supports(GitCapability.PipelineStatus))
        {
            var run = await Server.Pipelines.GetRunAsync(Connection, repo, runId);
            run.IsSuccess.Should().BeTrue(run.IsFailure ? run.Error.Message : string.Empty);
            run.Value.Id.Should().Be(runId);
        }
    }

    /// <summary>Ensuring the same environment twice is idempotent.</summary>
    [SkippableFact]
    public async Task EnsureEnvironment_IsIdempotent()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryFromTemplate)
            && Server.Capabilities.Supports(GitCapability.DeploymentEnvironments));

        var repo = await CreateRepositoryAsync();
        var request = new EnsureGitEnvironment { Name = "production" };

        (await Server.Environments.EnsureAsync(Connection, repo, request)).IsSuccess.Should().BeTrue();
        (await Server.Environments.EnsureAsync(Connection, repo, request)).IsSuccess.Should().BeTrue();

        var list = await Server.Environments.ListAsync(Connection, repo);
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle(e => e.Name == "production");
    }

    /// <summary>Applying the same branch policy twice is idempotent.</summary>
    [SkippableFact]
    public async Task ApplyBranchPolicy_IsIdempotent()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryFromTemplate)
            && Server.Capabilities.Supports(GitCapability.BranchPolicies));

        var repo = await CreateRepositoryAsync();
        var request = new GitBranchPolicyRequest { Pattern = "main" };

        (await Server.BranchPolicies.ApplyAsync(Connection, repo, request)).IsSuccess.Should().BeTrue();
        (await Server.BranchPolicies.ApplyAsync(Connection, repo, request)).IsSuccess.Should().BeTrue();

        var list = await Server.BranchPolicies.ListAsync(Connection, repo);
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle(p => p.Pattern == "main");
    }

    /// <summary>Ensuring the same webhook URL twice yields a single subscription.</summary>
    [SkippableFact]
    public async Task EnsureWebhook_SameUrl_IsIdempotent()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.RepositoryFromTemplate)
            && Server.Capabilities.Supports(GitCapability.WebhookManagement));

        var repo = await CreateRepositoryAsync();
        var target = new GitWebhookTarget.Repository(repo);
        var request = new EnsureGitWebhook
        {
            Url = new Uri("https://platform.invalid/webhooks/git"),
            Secret = WebhookSecret,
            Events = ["push"],
        };

        var first = await Server.Webhooks.EnsureAsync(Connection, target, request);
        var second = await Server.Webhooks.EnsureAsync(Connection, target, request);
        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        second.IsSuccess.Should().BeTrue();
        second.Value.Id.Should().Be(first.Value.Id, "ensuring the same URL twice must not duplicate");

        var list = await Server.Webhooks.ListAsync(Connection, target);
        list.IsSuccess.Should().BeTrue();
        list.Value.Should().ContainSingle(s => s.Url == request.Url);
    }

    /// <summary>The ingestor is fail-closed: an invalid signature is rejected before parsing.</summary>
    [SkippableFact]
    public void Ingestor_InvalidSignature_IsRejected()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.WebhookIngestion));

        var result = Server.WebhookIngestor.Parse(CreateDelivery(validSignature: false), WebhookSecret);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{GitErrors.Prefix}.WebhookSignatureInvalid");
    }

    /// <summary>A correctly signed delivery parses to a neutral event with a delivery id.</summary>
    [SkippableFact]
    public void Ingestor_ValidSignature_ParsesToNeutralEvent()
    {
        Skip.IfNot(Server.Capabilities.Supports(GitCapability.WebhookIngestion));

        var result = Server.WebhookIngestor.Parse(CreateDelivery(validSignature: true), WebhookSecret);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.DeliveryId.Should().NotBeNullOrWhiteSpace();
    }

    private async Task<GitRepositoryRef> CreateRepositoryAsync()
    {
        var created = await Server.Repositories.CreateFromTemplateAsync(Connection, new CreateRepositoryFromTemplate
        {
            Template = TemplateRepository,
            Namespace = Namespace,
            Name = NewRepositoryName(),
        });
        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return created.Value.Ref;
    }
}
