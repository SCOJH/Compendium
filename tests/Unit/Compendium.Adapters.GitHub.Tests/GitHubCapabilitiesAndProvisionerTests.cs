// -----------------------------------------------------------------------
// <copyright file="GitHubCapabilitiesAndProvisionerTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub;
using Compendium.Adapters.GitHub.Configuration;
using Compendium.Adapters.GitHub.Services;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubCapabilitiesAndProvisionerTests
{
    [Fact]
    public void Capabilities_DeclareGitHubAsTheProvider()
    {
        GitHubCapabilities.Matrix.Provider.Should().Be("github");
    }

    [Fact]
    public void Capabilities_DeclareEveryCapability()
    {
        foreach (var capability in Enum.GetValues<GitCapability>())
        {
            GitHubCapabilities.Matrix.Entries.Should().ContainKey(capability);
        }
    }

    [Fact]
    public void Capabilities_PipelineTriggerIsPartial_WithALimitation()
    {
        var support = GitHubCapabilities.Matrix.Entries[GitCapability.PipelineTrigger];
        support.Level.Should().Be(GitCapabilityLevel.Partial);
        support.Limitation.Should().Contain("workflow_dispatch");
    }

    [Fact]
    public void Capabilities_NamespaceProvisioningIsNone_AndNotSupported()
    {
        GitHubCapabilities.Matrix.Supports(GitCapability.NamespaceProvisioning).Should().BeFalse();
        GitHubCapabilities.Matrix.EnsureSupported(GitCapability.NamespaceProvisioning).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Capabilities_RepositoryManagementIsSupported()
    {
        GitHubCapabilities.Matrix.EnsureSupported(GitCapability.RepositoryManagement).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task NamespaceProvisioner_AlwaysFailsWithCapabilityNotSupported()
    {
        var provisioner = new GitHubNamespaceProvisioner();
        var connection = new GitConnection
        {
            Provider = "github",
            Credential = new GitCredential.PersonalAccessToken("x"),
        };

        var result = await provisioner.CreateNamespaceAsync(connection, new CreateGitNamespace { Name = "acme" });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Git.CapabilityNotSupported");
    }

    [Fact]
    public void ResolveApp_ReturnsDefaultForNullKey_AndLooksUpNamedApps()
    {
        var options = new GitHubAdapterOptions();
        options.Apps["secondary"] = new GitHubAppRegistration { AppId = "222" };

        options.ResolveApp(null).Should().BeSameAs(options.DefaultApp);
        options.ResolveApp("secondary")!.AppId.Should().Be("222");
        options.ResolveApp("missing").Should().BeNull();
    }
}
