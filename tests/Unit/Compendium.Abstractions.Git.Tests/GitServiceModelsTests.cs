// -----------------------------------------------------------------------
// <copyright file="GitServiceModelsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.AccessControl;
using Compendium.Abstractions.Git.CiConfiguration;
using Compendium.Abstractions.Git.Environments;
using Compendium.Abstractions.Git.Protection;
using Compendium.Abstractions.Git.Provisioning;
using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for the configuration-scope union and the per-service request and
/// result DTO records, pinning their default values and optional members.
/// </summary>
public sealed class GitServiceModelsTests
{
    private static readonly GitRepositoryRef Ref = new("acme", "billing");

    [Fact]
    public void GitConfigurationScope_RepositoryVariant_ProjectsRepositoryRef()
    {
        // Arrange / Act
        var scope = new GitConfigurationScope.Repository(Ref);

        // Assert
        scope.Ref.Should().Be(Ref);
    }

    [Fact]
    public void GitConfigurationScope_NamespaceVariant_ProjectsName()
    {
        // Arrange / Act
        var scope = new GitConfigurationScope.Namespace("acme");

        // Assert
        scope.Name.Should().Be("acme");
    }

    [Fact]
    public void GitConfigurationScope_EnvironmentVariant_ProjectsRefAndEnvironmentName()
    {
        // Arrange / Act
        var scope = new GitConfigurationScope.Environment(Ref, "production");

        // Assert
        scope.Ref.Should().Be(Ref);
        scope.EnvironmentName.Should().Be("production");
    }

    [Fact]
    public void EnsureGitEnvironment_ProjectsName()
    {
        // Arrange / Act
        var request = new EnsureGitEnvironment { Name = "production" };

        // Assert
        request.Name.Should().Be("production");
    }

    [Fact]
    public void GitDeploymentEnvironment_DefaultsHtmlUrlToNull()
    {
        // Arrange / Act
        var withUrl = new GitDeploymentEnvironment("production", "https://x.invalid/envs/production");
        var withoutUrl = new GitDeploymentEnvironment("staging");

        // Assert
        withUrl.Name.Should().Be("production");
        withUrl.HtmlUrl.Should().Be("https://x.invalid/envs/production");
        withoutUrl.HtmlUrl.Should().BeNull();
    }

    [Fact]
    public void GitBranchPolicyRequest_DefaultsMatchTheSafeProtectionProfile()
    {
        // Arrange / Act
        var request = new GitBranchPolicyRequest { Pattern = "main" };

        // Assert
        request.Pattern.Should().Be("main");
        request.RequirePullRequest.Should().BeTrue();
        request.RequiredApprovals.Should().Be(0);
        request.DismissStaleApprovals.Should().BeFalse();
        request.RequiredStatusChecks.Should().BeNull();
        request.BlockForcePush.Should().BeTrue();
        request.BlockDeletion.Should().BeTrue();
        request.RequireLinearHistory.Should().BeFalse();
        request.EnforceForAdmins.Should().BeFalse();
    }

    [Fact]
    public void GitBranchPolicyRequest_CarriesEveryOverride()
    {
        // Arrange / Act
        var request = new GitBranchPolicyRequest
        {
            Pattern = "release/*",
            RequirePullRequest = false,
            RequiredApprovals = 2,
            DismissStaleApprovals = true,
            RequiredStatusChecks = ["build", "test"],
            BlockForcePush = false,
            BlockDeletion = false,
            RequireLinearHistory = true,
            EnforceForAdmins = true,
        };

        // Assert
        request.RequiredApprovals.Should().Be(2);
        request.DismissStaleApprovals.Should().BeTrue();
        request.RequiredStatusChecks.Should().BeEquivalentTo("build", "test");
        request.RequireLinearHistory.Should().BeTrue();
        request.EnforceForAdmins.Should().BeTrue();
    }

    [Fact]
    public void GitBranchPolicy_ProjectsIdAndPattern()
    {
        // Arrange / Act
        var policy = new GitBranchPolicy("policy-main", "main");

        // Assert
        policy.Id.Should().Be("policy-main");
        policy.Pattern.Should().Be("main");
    }

    [Fact]
    public void EnsureGitTeam_DefaultsDescriptionToNull_AndCarriesItWhenSet()
    {
        // Arrange / Act
        var minimal = new EnsureGitTeam { Name = "Platform" };
        var described = new EnsureGitTeam { Name = "Platform", Description = "owns the platform" };

        // Assert
        minimal.Name.Should().Be("Platform");
        minimal.Description.Should().BeNull();
        described.Description.Should().Be("owns the platform");
    }

    [Fact]
    public void GitTeam_ProjectsSlugAndName()
    {
        // Arrange / Act
        var team = new GitTeam("platform", "Platform");

        // Assert
        team.Slug.Should().Be("platform");
        team.Name.Should().Be("Platform");
    }

    [Fact]
    public void CreateGitNamespace_DefaultsOptionalMembersToNull()
    {
        // Arrange / Act
        var request = new CreateGitNamespace { Name = "NXS-Acme" };

        // Assert
        request.Name.Should().Be("NXS-Acme");
        request.DisplayName.Should().BeNull();
        request.BillingEmail.Should().BeNull();
        request.AdminLogins.Should().BeNull();
    }

    [Fact]
    public void CreateGitNamespace_CarriesEveryOptionalMember()
    {
        // Arrange / Act
        var request = new CreateGitNamespace
        {
            Name = "NXS-Acme",
            DisplayName = "Acme Inc.",
            BillingEmail = "billing@acme.invalid",
            AdminLogins = ["octocat"],
        };

        // Assert
        request.DisplayName.Should().Be("Acme Inc.");
        request.BillingEmail.Should().Be("billing@acme.invalid");
        request.AdminLogins.Should().ContainSingle().Which.Should().Be("octocat");
    }

    [Fact]
    public void GitNamespace_DefaultsHtmlUrlToNull()
    {
        // Arrange / Act
        var withUrl = new GitNamespace("NXS-Acme", "https://x.invalid/NXS-Acme");
        var withoutUrl = new GitNamespace("NXS-Acme");

        // Assert
        withUrl.Name.Should().Be("NXS-Acme");
        withUrl.HtmlUrl.Should().Be("https://x.invalid/NXS-Acme");
        withoutUrl.HtmlUrl.Should().BeNull();
    }
}
