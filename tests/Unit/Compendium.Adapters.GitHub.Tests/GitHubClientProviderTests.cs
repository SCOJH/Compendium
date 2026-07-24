// -----------------------------------------------------------------------
// <copyright file="GitHubClientProviderTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Http;
using Compendium.Adapters.GitHub.Tests.Infrastructure;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubClientProviderTests
{
    [Fact]
    public async Task GetClient_MintsATokenAndReturnsAClient()
    {
        using var harness = new GitHubTestHarness();
        var provider = new GitHubClientProvider(harness.Broker);

        var result = await provider.GetClientAsync(harness.PatConnection(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task GetClient_CachesTheClientPerCredentialIdentity()
    {
        using var harness = new GitHubTestHarness();
        var provider = new GitHubClientProvider(harness.Broker);
        var connection = harness.PatConnection("same-token");

        var first = await provider.GetClientAsync(connection, CancellationToken.None);
        var second = await provider.GetClientAsync(connection, CancellationToken.None);

        ReferenceEquals(first.Value, second.Value).Should().BeTrue("clients are cached per credential and host");
    }

    [Fact]
    public async Task GetClient_PropagatesAMintFailure()
    {
        using var harness = new GitHubTestHarness();
        var provider = new GitHubClientProvider(harness.Broker);
        var connection = harness.AppConnection() with { Credential = new GitCredential.AppInstallation("1", "missing") };

        (await provider.GetClientAsync(connection, CancellationToken.None)).Error.Code.Should().Be("Git.NotConfigured");
    }
}
