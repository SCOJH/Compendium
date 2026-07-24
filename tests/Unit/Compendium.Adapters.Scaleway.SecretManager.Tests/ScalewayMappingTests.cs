// -----------------------------------------------------------------------
// <copyright file="ScalewayMappingTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Model;
using Compendium.Adapters.Scaleway.SecretManager.Http;
using FluentAssertions;
using Xunit;

namespace Compendium.Adapters.Scaleway.SecretManager.Tests;

/// <summary>
/// Pure-mapping guarantees: tag encoding round-trips, defensive path/status
/// parsing, descriptor/version projection.
/// </summary>
public sealed class ScalewayMappingTests
{
    [Fact]
    public void Tags_RoundTrip_IncludingEmptyValueAndColonlessForms()
    {
        var encoded = ScalewayMapping.ToTagList(new Dictionary<string, string>
        {
            ["org"] = "acme",
            ["flag"] = string.Empty,
        });

        encoded.Should().Contain("org:acme");
        encoded.Should().Contain("flag");

        var decoded = ScalewayMapping.FromTagList(["org:acme", "bare-tag", "kv:with:colons"]);
        decoded["org"].Should().Be("acme");
        decoded["bare-tag"].Should().Be(string.Empty);
        decoded["kv"].Should().Be("with:colons");
    }

    [Fact]
    public void ToTagList_EmptyOrNull_ReturnsNull()
    {
        ScalewayMapping.ToTagList(null).Should().BeNull();
        ScalewayMapping.ToTagList(new Dictionary<string, string>()).Should().BeNull();
    }

    [Theory]
    [InlineData(null, "/")]
    [InlineData("/", "/")]
    [InlineData("/a/b", "/a/b")]
    [InlineData("a//b/", "/a/b")]
    public void ParsePath_Tolerant_ProducesCanonicalForm(string? input, string expected)
    {
        ScalewayMapping.ParsePath(input).ToString().Should().Be(expected);
    }

    [Fact]
    public void ParsePath_InvalidSegments_FallsBackToRoot()
    {
        ScalewayMapping.ParsePath("/bad segment/x").ToString().Should().Be("/");
    }

    [Theory]
    [InlineData("enabled", VaultSecretVersionStatus.Enabled)]
    [InlineData("ENABLED", VaultSecretVersionStatus.Enabled)]
    [InlineData("deleted", VaultSecretVersionStatus.Destroyed)]
    [InlineData("destroyed", VaultSecretVersionStatus.Destroyed)]
    [InlineData("disabled", VaultSecretVersionStatus.Disabled)]
    [InlineData("something_new", VaultSecretVersionStatus.Disabled)]
    [InlineData(null, VaultSecretVersionStatus.Disabled)]
    public void ParseStatus_MapsKnownStatuses_AndFailsClosedOnUnknown(string? status, VaultSecretVersionStatus expected)
    {
        ScalewayMapping.ParseStatus(status).Should().Be(expected);
    }

    [Fact]
    public void ToDescriptor_ProjectsAllFields()
    {
        var descriptor = ScalewayMapping.ToDescriptor(new ScalewaySecret
        {
            Id = "sec-1",
            ProjectId = "proj-1",
            Name = "db",
            Path = "/nexus/org",
            Description = "database password",
            Tags = ["org:acme"],
            VersionCount = 4,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        descriptor.SecretId.Should().Be("sec-1");
        descriptor.Description.Should().Be("database password");
        descriptor.VersionCount.Should().Be(4);
        descriptor.CreatedAt.Should().Be(DateTimeOffset.UnixEpoch);
    }

    [Fact]
    public void ToVersion_ProjectsAllFields_AndEmptyDescriptionReadsAsNull()
    {
        var version = ScalewayMapping.ToVersion(new ScalewaySecretVersion
        {
            Revision = 7,
            SecretId = "sec-1",
            Status = "enabled",
            Description = string.Empty,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        version.Revision.Should().Be(7);
        version.Description.Should().BeNull();
        version.Status.Should().Be(VaultSecretVersionStatus.Enabled);
    }
}
