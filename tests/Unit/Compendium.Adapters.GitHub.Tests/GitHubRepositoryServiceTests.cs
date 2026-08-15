// -----------------------------------------------------------------------
// <copyright file="GitHubRepositoryServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubRepositoryServiceTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");
    private static readonly GitRepositoryRef Template = new("platform", "template-service");

    [Fact]
    public async Task CreateFromTemplate_MapsTheCreatedRepository()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/platform/template-service/generate").UsingPost())
            .RespondWith(Json.Created(RepoJson("acme", "billing")));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.CreateFromTemplateAsync(harness.AppConnection(), new CreateRepositoryFromTemplate
        {
            Template = Template,
            Namespace = "acme",
            Name = "billing",
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Ref.Should().Be(Repo);
        result.Value.CloneUrl.Should().Be("https://github.com/acme/billing.git");
        result.Value.DefaultBranch.Should().Be("main");
    }

    [Fact]
    public async Task Get_MapsTheRepository()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing").UsingGet())
            .RespondWith(Json.Ok(RepoJson("acme", "billing")));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.GetAsync(harness.AppConnection(), Repo);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.HtmlUrl.Should().Be("https://github.com/acme/billing");
    }

    [Fact]
    public async Task Get_AbsentRepository_MapsToRepositoryNotFound()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.GetAsync(harness.AppConnection(), Repo)).Error.Code.Should().Be("Git.RepositoryNotFound");
    }

    [Fact]
    public async Task List_FallsBackToTheUserEndpointWhenOrgIsNotFound()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/repos").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        harness.Server.Given(Request.Create().WithPath("/api/v3/users/acme/repos").UsingGet())
            .RespondWith(Json.Ok(new[] { RepoJson("acme", "billing") }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListAsync(harness.AppConnection(), "acme");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle().Which.Ref.Name.Should().Be("billing");
    }

    [Fact]
    public async Task FileExists_TrueWhenTheContentEndpointReturnsAnEntry()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/contents/README.md").UsingGet())
            .RespondWith(Json.Ok(new[] { new { type = "file", name = "README.md", path = "README.md", sha = "s" } }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.FileExistsAsync(harness.AppConnection(), Repo, "README.md")).Value.Should().BeTrue();
    }

    [Fact]
    public async Task FileExists_FalseWhenAbsent()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/contents/missing.txt").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.FileExistsAsync(harness.AppConnection(), Repo, "missing.txt")).Value.Should().BeFalse();
    }

    [Fact]
    public async Task ListCommits_MapsCommits()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/commits").UsingGet())
            .RespondWith(Json.Ok(new[]
            {
                new
                {
                    sha = "abc",
                    html_url = "https://github.com/acme/billing/commit/abc",
                    commit = new { message = "init", author = new { name = "Dev", date = "2026-07-24T00:00:00Z" } },
                },
            }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListCommitsAsync(harness.AppConnection(), Repo, reference: null, limit: 10);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle();
        result.Value[0].Sha.Should().Be("abc");
        result.Value[0].Message.Should().Be("init");
    }

    [Fact]
    public async Task ListBranches_MapsBranches()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/branches").UsingGet())
            .RespondWith(Json.Ok(new[] { new { name = "main", commit = new { sha = "abc" }, @protected = true } }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListBranchesAsync(harness.AppConnection(), Repo);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(b => b.Name == "main" && b.Protected);
    }

    [Fact]
    public async Task CreateTag_CreatesAGitRef()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/git/refs").UsingPost())
            .RespondWith(Json.Created(new { @ref = "refs/tags/v1.0.0", @object = new { sha = "deadbeef" } }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.CreateTagAsync(harness.AppConnection(), Repo, "v1.0.0", commitSha: "deadbeef");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Name.Should().Be("v1.0.0");
        result.Value.Sha.Should().Be("deadbeef");
    }

    [Fact]
    public async Task CreateTag_ResolvesTheDefaultBranchHeadWhenNoShaGiven()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing").UsingGet())
            .RespondWith(Json.Ok(RepoJson("acme", "billing")));
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/branches/main").UsingGet())
            .RespondWith(Json.Ok(new { name = "main", commit = new { sha = "headsha" }, @protected = false }));
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/git/refs").UsingPost())
            .RespondWith(Json.Created(new { @ref = "refs/tags/v2", @object = new { sha = "headsha" } }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.CreateTagAsync(harness.AppConnection(), Repo, "v2");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Sha.Should().Be("headsha");
    }

    [Fact]
    public async Task ListTags_MapsTags()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/tags").UsingGet())
            .RespondWith(Json.Ok(new[] { new { name = "v1", commit = new { sha = "abc" } } }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.ListTagsAsync(harness.AppConnection(), Repo);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(t => t.Name == "v1");
    }

    [Fact]
    public async Task CreateRelease_MapsTheRelease()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/releases").UsingPost())
            .RespondWith(Json.Created(new { id = 77, tag_name = "v1.0.0", html_url = "https://github.com/acme/billing/releases/v1.0.0" }));
        var service = new GitHubRepositoryService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.CreateReleaseAsync(harness.AppConnection(), Repo, new CreateGitRelease
        {
            TagName = "v1.0.0",
            Body = "notes",
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Id.Should().Be("77");
        result.Value.TagName.Should().Be("v1.0.0");
    }

    [Fact]
    public async Task Operation_PropagatesAClientProviderFailure()
    {
        using var harness = new GitHubTestHarness();
        var service = new GitHubRepositoryService(StubClientProvider.Failing(GitErrors.NotConfigured("github")));

        (await service.GetAsync(harness.AppConnection(), Repo)).Error.Code.Should().Be("Git.NotConfigured");
    }

    private static object RepoJson(string owner, string name) => new
    {
        id = 1,
        name,
        full_name = $"{owner}/{name}",
        @private = true,
        html_url = $"https://github.com/{owner}/{name}",
        clone_url = $"https://github.com/{owner}/{name}.git",
        default_branch = "main",
        owner = new { login = owner, id = 2 },
    };
}
