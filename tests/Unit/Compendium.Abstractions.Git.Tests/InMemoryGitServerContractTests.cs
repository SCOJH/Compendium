// -----------------------------------------------------------------------
// <copyright file="InMemoryGitServerContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text.Json;
using Compendium.Abstractions.Git;
using Compendium.Abstractions.Git.Connections;
using Compendium.Abstractions.Git.Repositories;
using Compendium.Abstractions.Git.Webhooks;
using Compendium.Testing.Git;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Subscribes <see cref="InMemoryGitServer"/> to the adapter contract — the
/// fake is the first executable proof of <see cref="GitServerContractTests"/>
/// and stays aligned with real adapters through it.
/// </summary>
public sealed class InMemoryGitServerContractTests : GitServerContractTests
{
    private readonly InMemoryGitServer _server;

    public InMemoryGitServerContractTests()
    {
        _server = new InMemoryGitServer();
        _server.SeedInstallation(new GitInstallationInfo(
            InstallationId: "inst-1",
            AccountLogin: "acme",
            AccountType: GitAccountType.Organization));
        _server.SeedRepository(new GitRepositoryRef("platform", "template-dotnet"));
    }

    protected override IGitServer Server => _server;

    protected override GitConnection Connection => new()
    {
        Provider = InMemoryGitServer.ProviderName,
        Credential = new GitCredential.AppInstallation("inst-1"),
    };

    protected override string Namespace => "acme";

    protected override GitRepositoryRef TemplateRepository => new("platform", "template-dotnet");

    protected override string WebhookSecret => InMemoryGitServer.WellKnownWebhookSecret;

    protected override GitWebhookDelivery CreateDelivery(bool validSignature) => new()
    {
        Body = JsonSerializer.Serialize(new
        {
            type = "push",
            deliveryId = Guid.NewGuid().ToString(),
            repository = "acme/some-repo",
            reference = "refs/heads/main",
            headCommitSha = "abc123",
        }),
        Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-InMemory-Signature"] = validSignature ? WebhookSecret : "not-the-secret",
        },
    };
}
