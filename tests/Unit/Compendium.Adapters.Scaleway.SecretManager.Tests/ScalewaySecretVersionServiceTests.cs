// -----------------------------------------------------------------------
// <copyright file="ScalewaySecretVersionServiceTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Text;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Adapters.Scaleway.SecretManager.Tests.Infrastructure;
using FluentAssertions;
using WireMock.RequestBuilders;
using Xunit;

namespace Compendium.Adapters.Scaleway.SecretManager.Tests;

public sealed class ScalewaySecretVersionServiceTests
{
    [Fact]
    public async Task Add_SendsBase64Payload_AndMapsRevision()
    {
        using var harness = new ScalewayTestHarness();
        var expectedBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hunter2"));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions")).UsingPost()
                .WithBody(b => b!.Contains(expectedBase64)))
            .RespondWith(Json.Ok(new { revision = 3, status = "enabled" }));

        var result = await harness.Versions.AddAsync(
            harness.Connection(), "sec-1", SecretMaterial.FromString("hunter2"), "rotation");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Revision.Should().Be(3);
        result.Value.Status.Should().Be(VaultSecretVersionStatus.Enabled);
    }

    [Fact]
    public async Task Add_PayloadOver64KiB_FailsFastWithoutNetworkCall()
    {
        using var harness = new ScalewayTestHarness();

        var result = await harness.Versions.AddAsync(
            harness.Connection(), "sec-1", SecretMaterial.FromBytes(new byte[64 * 1024 + 1]));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.PayloadTooLarge");
        harness.Server.LogEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task Access_DecodesBase64Material()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/2/access")).UsingGet())
            .RespondWith(Json.Ok(new
            {
                revision = 2,
                data = Convert.ToBase64String(Encoding.UTF8.GetBytes("plaintext")),
            }));

        var result = await harness.Versions.AccessAsync(harness.Connection(), "sec-1", 2);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.AsString().Should().Be("plaintext");
    }

    [Fact]
    public async Task Access_DisabledRevision_DisambiguatesToVersionDisabled()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/2/access")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "version not enabled" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/2")).UsingGet())
            .RespondWith(Json.Ok(new { revision = 2, status = "disabled" }));

        var result = await harness.Versions.AccessAsync(harness.Connection(), "sec-1", 2);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.VersionDisabled");
    }

    [Fact]
    public async Task Access_MissingSecret_DisambiguatesToSecretNotFound()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/ghost/versions/1/access")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/ghost/versions/1")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/ghost")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));

        var result = await harness.Versions.AccessAsync(harness.Connection(), "ghost", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.SecretNotFound");
    }

    [Fact]
    public async Task Access_MissingRevisionOnLiveSecret_IsVersionNotFound()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/9/access")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/9")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1")).UsingGet())
            .RespondWith(Json.Ok(new { id = "sec-1", name = "db", path = "/" }));

        var result = await harness.Versions.AccessAsync(harness.Connection(), "sec-1", 9);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.VersionNotFound");
    }

    [Fact]
    public async Task Disable_NoOpTransition_IsIdempotentSuccess()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/1/disable")).UsingPost())
            .RespondWith(Json.Status(409, new { message = "version is already disabled" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/1")).UsingGet())
            .RespondWith(Json.Ok(new { revision = 1, status = "disabled" }));

        (await harness.Versions.DisableAsync(harness.Connection(), "sec-1", 1)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Destroy_HappyPath_And_NotFound()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/1")).UsingDelete())
            .RespondWith(Json.Ok(new { }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/9")).UsingDelete())
            .RespondWith(Json.Status(404, new { message = "not found" }));

        (await harness.Versions.DestroyAsync(harness.Connection(), "sec-1", 1)).IsSuccess.Should().BeTrue();
        var missing = await harness.Versions.DestroyAsync(harness.Connection(), "sec-1", 9);
        missing.IsFailure.Should().BeTrue();
        missing.Error.Code.Should().Be("SecretVault.VersionNotFound");
    }

    [Fact]
    public async Task Access_InvalidBase64Payload_MapsToProviderRejected()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/1/access")).UsingGet())
            .RespondWith(Json.Ok(new { revision = 1, data = "not-base64!!!" }));

        var result = await harness.Versions.AccessAsync(harness.Connection(), "sec-1", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.ProviderRejected");
    }

    [Fact]
    public async Task ListVersions_MissingSecret_MapsToSecretNotFound()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/ghost/versions")).UsingGet())
            .RespondWith(Json.Status(404, new { message = "not found" }));

        var result = await harness.Versions.ListAsync(harness.Connection(), "ghost");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.SecretNotFound");
    }

    [Fact]
    public async Task Enable_FailureWithMismatchedState_ReturnsTheOriginalError()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/1/enable")).UsingPost())
            .RespondWith(Json.Status(500, new { message = "boom" }));
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions/1")).UsingGet())
            .RespondWith(Json.Ok(new { revision = 1, status = "disabled" }));

        var result = await harness.Versions.EnableAsync(harness.Connection(), "sec-1", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.ProviderRejected");
    }

    [Fact]
    public async Task QuotaError_MapsToQuotaExceeded()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions")).UsingPost())
            .RespondWith(Json.Status(400, new { message = "quota of versions per secret exceeded" }));

        var result = await harness.Versions.AddAsync(
            harness.Connection(), "sec-1", SecretMaterial.FromString("x"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.QuotaExceeded");
    }

    [Fact]
    public async Task NonJsonErrorBody_StillMapsTheStatusCode()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions")).UsingPost())
            .RespondWith(WireMock.ResponseBuilders.Response.Create()
                .WithStatusCode(502).WithHeader("Content-Type", "text/html").WithBody("<html>bad gateway</html>"));

        var result = await harness.Versions.AddAsync(
            harness.Connection(), "sec-1", SecretMaterial.FromString("x"));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.ProviderRejected");
    }

    [Fact]
    public async Task NetworkFailure_MapsToProviderRejected_NotAnException()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server.Stop();

        var result = await harness.Versions.AccessAsync(harness.Connection(), "sec-1", 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SecretVault.ProviderRejected");
    }

    [Fact]
    public async Task ListVersions_PaginatesAndOrdersByRevision()
    {
        using var harness = new ScalewayTestHarness();
        harness.Server
            .Given(Request.Create().WithPath(ScalewayTestHarness.Api("secrets/sec-1/versions")).UsingGet()
                .WithParam("page", "1"))
            .RespondWith(Json.Ok(new
            {
                versions = new object[]
                {
                    new { revision = 2, status = "enabled" },
                    new { revision = 1, status = "deleted" },
                },
                total_count = 2,
            }));

        var result = await harness.Versions.ListAsync(harness.Connection(), "sec-1");

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);
        result.Value.Select(v => v.Revision).Should().ContainInOrder(1L, 2L);
        result.Value[0].Status.Should().Be(VaultSecretVersionStatus.Destroyed);
    }
}
