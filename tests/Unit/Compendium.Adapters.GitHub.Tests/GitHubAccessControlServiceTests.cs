// -----------------------------------------------------------------------
// <copyright file="GitHubAccessControlServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Adapters.GitHub.Services;
using Compendium.Adapters.GitHub.Tests.Infrastructure;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;

namespace Compendium.Adapters.GitHub.Tests;

public sealed class GitHubAccessControlServiceTests
{
    private static readonly GitRepositoryRef Repo = new("acme", "billing");

    [Fact]
    public async Task EnsureTeam_CreatesTheTeamWhenAbsent()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/teams/engineering").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(404));
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/teams").UsingPost())
            .RespondWith(Json.Created(TeamJson()));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.EnsureTeamAsync(harness.AppConnection(), "acme", new EnsureGitTeam { Name = "Engineering" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Slug.Should().Be("engineering");
    }

    [Fact]
    public async Task EnsureTeam_ReturnsTheExistingTeam()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/teams/engineering").UsingGet())
            .RespondWith(Json.Ok(TeamJson()));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.EnsureTeamAsync(harness.AppConnection(), "acme", new EnsureGitTeam { Name = "Engineering" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Name.Should().Be("Engineering");
    }

    [Fact]
    public async Task AddTeamMember_ResolvesTheTeamAndSetsMembership()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/orgs/acme/teams/eng").UsingGet())
            .RespondWith(Json.Ok(TeamJson(slug: "eng")));
        harness.Server.Given(Request.Create().WithPath("/api/v3/teams/5/memberships/bob").UsingPut())
            .RespondWith(Json.Ok(new { url = "https://x", role = "member", state = "active" }));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.AddTeamMemberAsync(harness.AppConnection(), "acme", "eng", "bob", GitTeamRole.Member);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task SetTeamRepositoryRole_GrantsTheRole()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/orgs/acme/teams/eng/repos/acme/billing").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.SetTeamRepositoryRoleAsync(
            harness.AppConnection(), "acme", "eng", Repo, GitRepositoryRole.Write);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task SetUserRepositoryRole_AddsTheCollaborator()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/collaborators/bob").UsingPut())
            .RespondWith(Response.Create().WithStatusCode(204));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        var result = await service.SetUserRepositoryRoleAsync(harness.AppConnection(), Repo, "bob", GitRepositoryRole.Admin);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task RemoveUser_IsIdempotentOnNotFound()
    {
        using var harness = new GitHubTestHarness();
        harness.Server.Given(Request.Create().WithPath("/api/v3/repos/acme/billing/collaborators/bob").UsingDelete())
            .RespondWith(Response.Create().WithStatusCode(404));
        var service = new GitHubAccessControlService(StubClientProvider.Returning(harness.OctokitClient()));

        (await service.RemoveUserFromRepositoryAsync(harness.AppConnection(), Repo, "bob")).IsSuccess.Should().BeTrue();
    }

    private static object TeamJson(string slug = "engineering") => new
    {
        id = 5,
        node_id = "T_1",
        slug,
        name = "Engineering",
    };
}
