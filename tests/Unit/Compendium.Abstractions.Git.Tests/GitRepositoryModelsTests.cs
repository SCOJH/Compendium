// -----------------------------------------------------------------------
// <copyright file="GitRepositoryModelsTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Git.Repositories;

namespace Compendium.Abstractions.Git.Tests;

/// <summary>
/// Unit tests for the neutral repository DTO records in
/// <c>GitRepositoryModels.cs</c>: they are part of the public adapter contract,
/// so their construction, property projection, and value semantics are pinned.
/// </summary>
public sealed class GitRepositoryModelsTests
{
    private static readonly GitRepositoryRef Ref = new("acme", "billing");

    [Fact]
    public void GitRepository_ProjectsAllProperties()
    {
        // Arrange / Act
        var repository = new GitRepository(Ref, "https://x.invalid/acme/billing.git", "https://x.invalid/acme/billing", "main", Private: true);

        // Assert
        repository.Ref.Should().Be(Ref);
        repository.CloneUrl.Should().Be("https://x.invalid/acme/billing.git");
        repository.HtmlUrl.Should().Be("https://x.invalid/acme/billing");
        repository.DefaultBranch.Should().Be("main");
        repository.Private.Should().BeTrue();
        repository.Should().Be(repository with { });
    }

    [Fact]
    public void GitCommit_ProjectsAllPropertiesIncludingOptionalAuthorAndTimestamp()
    {
        // Arrange
        var authoredAt = DateTimeOffset.UtcNow;

        // Act
        var commit = new GitCommit("sha1", "message", "octocat", authoredAt, "https://x.invalid/c/sha1");
        var (sha, message, author, at, html) = commit;

        // Assert
        sha.Should().Be("sha1");
        message.Should().Be("message");
        author.Should().Be("octocat");
        at.Should().Be(authoredAt);
        html.Should().Be("https://x.invalid/c/sha1");
        commit.ToString().Should().Contain("sha1");
    }

    [Fact]
    public void GitCommit_AllowsNullAuthorAndTimestamp()
    {
        // Arrange / Act
        var commit = new GitCommit("sha1", "message", AuthorName: null, AuthoredAt: null, "https://x.invalid/c/sha1");

        // Assert
        commit.AuthorName.Should().BeNull();
        commit.AuthoredAt.Should().BeNull();
    }

    [Fact]
    public void GitBranch_ProjectsAllProperties()
    {
        // Arrange / Act
        var branch = new GitBranch("main", "sha1", Protected: true);
        var (name, sha, isProtected) = branch;

        // Assert
        name.Should().Be("main");
        sha.Should().Be("sha1");
        isProtected.Should().BeTrue();
        branch.Should().Be(new GitBranch("main", "sha1", true));
    }

    [Fact]
    public void GitTag_ProjectsAllProperties()
    {
        // Arrange / Act
        var tag = new GitTag("v1.0.0", "sha1");
        var (name, sha) = tag;

        // Assert
        name.Should().Be("v1.0.0");
        sha.Should().Be("sha1");
        tag.Should().Be(new GitTag("v1.0.0", "sha1"));
    }

    [Fact]
    public void GitRelease_ProjectsAllProperties()
    {
        // Arrange / Act
        var release = new GitRelease("rel-1", "v1.0.0", "https://x.invalid/releases/v1.0.0");
        var (id, tag, html) = release;

        // Assert
        id.Should().Be("rel-1");
        tag.Should().Be("v1.0.0");
        html.Should().Be("https://x.invalid/releases/v1.0.0");
    }

    [Fact]
    public void CreateRepositoryFromTemplate_DefaultsPrivateTrueAndDescriptionNull()
    {
        // Arrange / Act
        var request = new CreateRepositoryFromTemplate
        {
            Template = Ref,
            Namespace = "acme",
            Name = "new-service",
        };

        // Assert
        request.Template.Should().Be(Ref);
        request.Namespace.Should().Be("acme");
        request.Name.Should().Be("new-service");
        request.Description.Should().BeNull();
        request.Private.Should().BeTrue();
    }

    [Fact]
    public void CreateRepositoryFromTemplate_CarriesDescriptionAndPrivacyOverride()
    {
        // Arrange / Act
        var request = new CreateRepositoryFromTemplate
        {
            Template = Ref,
            Namespace = "acme",
            Name = "public-docs",
            Description = "the docs",
            Private = false,
        };

        // Assert
        request.Description.Should().Be("the docs");
        request.Private.Should().BeFalse();
    }

    [Fact]
    public void CreateGitRelease_DefaultsOptionalMembersToNull()
    {
        // Arrange / Act
        var request = new CreateGitRelease { TagName = "v1.0.0" };

        // Assert
        request.TagName.Should().Be("v1.0.0");
        request.TargetCommitSha.Should().BeNull();
        request.Title.Should().BeNull();
        request.Body.Should().BeNull();
    }

    [Fact]
    public void CreateGitRelease_CarriesAllOptionalMembers()
    {
        // Arrange / Act
        var request = new CreateGitRelease
        {
            TagName = "v1.0.0",
            TargetCommitSha = "sha1",
            Title = "Release 1.0.0",
            Body = "notes",
        };

        // Assert
        request.TargetCommitSha.Should().Be("sha1");
        request.Title.Should().Be("Release 1.0.0");
        request.Body.Should().Be("notes");
    }
}
