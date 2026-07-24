// -----------------------------------------------------------------------
// <copyright file="ScalewaySecretContainerServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Adapters.Scaleway.SecretManager.Tests.Infrastructure;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using Xunit;

namespace Compendium.Adapters.Scaleway.SecretManager.Tests;

public sealed class ScalewaySecretContainerServiceTests
{
    private static readonly SecretScopePath Path = SecretScopePath.From("nexus", "org-1").Value;

    [Fact]
    public async Task Create_SendsAuthAndProject_AndMapsDescriptor()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingPost()
                .WithHeader("X-Auth-Token", "scw_secret_key")
                .WithBody(b => b!.Contains(ScalewayTestHarness.ProjectId) && b.Contains("\"/nexus/org-1\"")))
            .RespondWith(Json.Ok(new
            {
                id = "sec-1",
                name = "db-password",
                path = "/nexus/org-1",
                tags = new[] { "org:acme", "managed-by:nexus" },
                version_count = 0,
            }));

        var result = await harness.Containers.CreateAsync(harness.Connection(), new CreateVaultSecret
        {
            Name = "db-password",
            Path = Path,
            Tags = new Dictionary<string, string> { ["org"] = "acme", ["managed-by"] = "nexus" },
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.SecretId.Should().Be("sec-1");
        result.Value.Path.ToString().Should().Be("/nexus/org-1");
        result.Value.Tags.Should().Contain("org", "acme");
    }

    [Fact]
    public async Task Create_Conflict_MapsToConflictExists()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server.Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingPost())
            .RespondWith(Json.Status(409, new { message = "secret already exists" }));

        var result = await harness.Containers.CreateAsync(harness.Connection(), new CreateVaultSecret
        {
            Name = "dup",
            Path = Path,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.ConflictExists");
    }

    [Fact]
    public async Task Get_NotFound_MapsToSecretNotFound()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server.Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/ghost")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));

        var result = await harness.Containers.GetAsync(harness.Connection(), "ghost");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.SecretNotFound");
    }

    [Fact]
    public async Task AnyCall_Unauthorized_MapsToAuthenticationFailed()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server.Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1")).UsingGet())
            .RespondWith(Json.Status(403, new { message = "invalid token" }));

        var result = await harness.Containers.GetAsync(harness.Connection("bad"), "sec-1");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.AuthenticationFailed");
    }

    [Fact]
    public async Task AnyCall_Throttled_CarriesRetryAfter()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server.Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1")).UsingGet())
            .RespondWith(Response.Create().WithStatusCode(429)
                .WithHeader("Retry-After", "30")
                .WithHeader("Content-Type", "application/json")
                .WithBodyAsJson(new { message = "too many requests" }));

        var result = await harness.Containers.GetAsync(harness.Connection(), "sec-1");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.Throttled");
        result.Error.Metadata.Should().ContainKey("retryAfterSeconds").WhoseValue.Should().Be(30);
    }

    [Fact]
    public async Task MissingCredential_FailsWithNotConfigured_WithoutNetworkCall()
    {
        using var harness = new ScalewayTestHarness();
        var connection = harness.Connection() with { Credential = new SecretVaultCredential.None() };

        var result = await harness.Containers.GetAsync(connection, "sec-1");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.NotConfigured");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task List_PaginatesAndFiltersByPrefixAndTags()
    {
        using var harness = new ScalewayTestHarness();
        var pageOne = Enumerable.Range(1, 100).Select(i => new
        {
            id = $"sec-{i}",
            name = $"key-{i}",
            path = "/nexus/other",
            tags = Array.Empty<string>(),
        }).Cast<object>().ToArray();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingGet()
                .WithParam("page", "1"))
            .RespondWith(Json.Ok(new { secrets = pageOne, total_count = 102 }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingGet()
                .WithParam("page", "2"))
            .RespondWith(Json.Ok(new
            {
                secrets = new object[]
                {
                    new { id = "sec-a", name = "wanted", path = "/nexus/org-1/app", tags = new[] { "env:prod" } },
                    new { id = "sec-b", name = "untagged", path = "/nexus/org-1", tags = Array.Empty<string>() },
                },
                total_count = 102,
            }));

        var result = await harness.Containers.ListAsync(
            harness.Connection(),
            SecretScopePath.From("nexus", "org-1").Value,
            new Dictionary<string, string> { ["env"] = "prod" });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(d => d.SecretId == "sec-a");
    }

    [Fact]
    public async Task Create_WithoutProjectAnywhere_FailsWithNotConfigured()
    {
        using var harness = new ScalewayTestHarness(defaultProjectId: null);

        var result = await harness.Containers.CreateAsync(harness.Connection(), new CreateVaultSecret
        {
            Name = "orphan",
            Path = Path,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.NotConfigured");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_ConnectionTenancy_OverridesTheDefaultProject()
    {
        using var harness = new ScalewayTestHarness(defaultProjectId: null);
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingPost()
                .WithBody(b => b!.Contains("tenancy-project")))
            .RespondWith(Json.Ok(new { id = "sec-t", name = "k", path = "/" }));

        var connection = harness.Connection() with { Tenancy = "tenancy-project" };
        var result = await harness.Containers.CreateAsync(connection, new CreateVaultSecret
        {
            Name = "k",
            Path = SecretScopePath.Root,
        });

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
    }

    [Fact]
    public async Task List_WithoutTagFilter_ReturnsEverythingUnderThePrefix()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingGet())
            .RespondWith(Json.Ok(new
            {
                secrets = new object[]
                {
                    new { id = "a", name = "one", path = "/nexus", tags = Array.Empty<string>() },
                    new { id = "b", name = "two", path = "/other", tags = Array.Empty<string>() },
                },
                total_count = 2,
            }));

        var result = await harness.Containers.ListAsync(
            harness.Connection(), SecretScopePath.From("nexus").Value);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Should().ContainSingle(d => d.SecretId == "a");
    }

    [Fact]
    public async Task List_ProviderFailure_PropagatesTheError()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets")).UsingGet())
            .RespondWith(Json.Status(500, new { message = "boom" }));

        var result = await harness.Containers.ListAsync(harness.Connection(), SecretScopePath.Root);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.ProviderRejected");
    }

    [Fact]
    public async Task Delete_NotFound_IsIdempotentSuccess()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server.Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/gone")).UsingDelete())
            .RespondWith(Json.Status(404, new { message = "not found" }));

        (await harness.Containers.DeleteAsync(harness.Connection(), "gone")).IsSuccess.Should().BeTrue();
    }
}
