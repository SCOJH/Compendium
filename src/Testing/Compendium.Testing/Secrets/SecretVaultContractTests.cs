// -----------------------------------------------------------------------
// <copyright file="SecretVaultContractTests.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Core.Results;
using FluentAssertions;
using Xunit;

namespace Compendium.Testing.Secrets;

/// <summary>
/// The behavioral contract every <see cref="ISecretVault"/> adapter must
/// satisfy. Inherit in the adapter's test suite and provide the fixture
/// members; tests for capabilities the adapter does not declare are skipped
/// automatically (capability honesty is itself asserted: a declared
/// capability must work). <see cref="InMemorySecretVault"/> subscribes to
/// this contract, keeping the fake and real adapters aligned. Passing this
/// suite is what makes swapping vault backends safe.
/// </summary>
public abstract class SecretVaultContractTests
{
    /// <summary>Gets the vault under test.</summary>
    protected abstract ISecretVault Vault { get; }

    /// <summary>Gets a connection with valid credentials for <see cref="Vault"/>.</summary>
    protected abstract SecretVaultConnection Connection { get; }

    /// <summary>Gets a path under which the connection can create secrets.</summary>
    protected virtual SecretScopePath TestPath => SecretScopePath.From("contract-tests").Value;

    /// <summary>Generates a unique secret name for a test run.</summary>
    protected virtual string NewSecretName() => $"contract-{Guid.NewGuid():N}"[..24];

    /// <summary>The declared provider must match the facade discriminator and never be empty.</summary>
    [Fact]
    public void Capabilities_DeclaresTheVaultProvider()
    {
        Vault.Provider.Should().NotBeNullOrWhiteSpace();
        Vault.Capabilities.Provider.Should().Be(Vault.Provider);
    }

    /// <summary>An undeclared capability must fail with the standard machine code, never throw.</summary>
    [SkippableFact]
    public void EnsureSupported_UndeclaredCapability_FailsWithCapabilityNotSupported()
    {
        var undeclared = Enum.GetValues<SecretVaultCapability>()
            .FirstOrDefault(c => !Vault.Capabilities.Supports(c), (SecretVaultCapability)(-1));
        Skip.If(undeclared == (SecretVaultCapability)(-1), "Adapter declares every capability.");

        var result = Vault.Capabilities.EnsureSupported(undeclared);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.CapabilityNotSupported");
    }

    /// <summary>Creation round-trips through Get by the returned provider-side id.</summary>
    [Fact]
    public async Task Create_RoundTripsThroughGet()
    {
        var name = NewSecretName();
        var created = await Vault.Secrets.CreateAsync(Connection, new CreateVaultSecret
        {
            Name = name,
            Path = TestPath,
            Description = "contract round-trip",
        });

        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        created.Value.SecretId.Should().NotBeNullOrWhiteSpace();
        created.Value.Name.Should().Be(name);

        var fetched = await Vault.Secrets.GetAsync(Connection, created.Value.SecretId);
        fetched.IsSuccess.Should().BeTrue(fetched.IsFailure ? fetched.Error.Message : string.Empty);
        fetched.Value.Name.Should().Be(name);
        fetched.Value.Path.ToString().Should().Be(TestPath.ToString());
    }

    /// <summary>Creating the same (name, path) twice conflicts with the standard code.</summary>
    [Fact]
    public async Task Create_DuplicateNameAndPath_Conflicts()
    {
        var request = new CreateVaultSecret { Name = NewSecretName(), Path = TestPath };

        (await Vault.Secrets.CreateAsync(Connection, request)).IsSuccess.Should().BeTrue();
        var duplicate = await Vault.Secrets.CreateAsync(Connection, request);

        duplicate.IsFailure.Should().BeTrue();
        duplicate.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.ConflictExists");
    }

    /// <summary>Reading an unknown secret fails with the standard not-found code.</summary>
    [Fact]
    public async Task Get_UnknownSecret_FailsWithSecretNotFound()
    {
        var result = await Vault.Secrets.GetAsync(Connection, Guid.NewGuid().ToString());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.SecretNotFound");
    }

    /// <summary>Revisions start at 1 and increase monotonically.</summary>
    [Fact]
    public async Task Add_RevisionsAreMonotonicFromOne()
    {
        var secretId = await CreateSecretAsync();

        var first = await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("v1"));
        var second = await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("v2"));

        first.IsSuccess.Should().BeTrue(first.IsFailure ? first.Error.Message : string.Empty);
        second.IsSuccess.Should().BeTrue(second.IsFailure ? second.Error.Message : string.Empty);
        first.Value.Revision.Should().Be(1);
        second.Value.Revision.Should().Be(2);
    }

    /// <summary>Writing a new version never alters an older revision's material (immutability).</summary>
    [SkippableFact]
    public async Task Access_OlderRevision_IsImmutableAfterNewerWrites()
    {
        Skip.IfNot(Vault.Capabilities.Supports(SecretVaultCapability.ImmutableVersions));
        var secretId = await CreateSecretAsync();
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("original"));
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("replacement"));

        var original = await Vault.Versions.AccessAsync(Connection, secretId, 1);
        var replacement = await Vault.Versions.AccessAsync(Connection, secretId, 2);

        original.IsSuccess.Should().BeTrue(original.IsFailure ? original.Error.Message : string.Empty);
        original.Value.AsString().Should().Be("original");
        replacement.Value.AsString().Should().Be("replacement");
    }

    /// <summary>Accessing a never-written revision fails with the standard code.</summary>
    [Fact]
    public async Task Access_UnknownRevision_FailsWithVersionNotFound()
    {
        var secretId = await CreateSecretAsync();

        var result = await Vault.Versions.AccessAsync(Connection, secretId, 42);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.VersionNotFound");
    }

    /// <summary>Accessing a version of an unknown secret fails with secret-not-found.</summary>
    [Fact]
    public async Task Access_UnknownSecret_FailsWithSecretNotFound()
    {
        var result = await Vault.Versions.AccessAsync(Connection, Guid.NewGuid().ToString(), 1);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.SecretNotFound");
    }

    /// <summary>Version listing reports every written revision with its status and description.</summary>
    [Fact]
    public async Task ListVersions_ReportsAllRevisions()
    {
        var secretId = await CreateSecretAsync();
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("v1"), "first");
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("v2"), "second");

        var versions = await Vault.Versions.ListAsync(Connection, secretId);

        versions.IsSuccess.Should().BeTrue(versions.IsFailure ? versions.Error.Message : string.Empty);
        versions.Value.Should().HaveCount(2);
        versions.Value.Select(v => v.Revision).Should().ContainInOrder(1L, 2L);
        versions.Value.Should().OnlyContain(v => v.Status == VaultSecretVersionStatus.Enabled);
    }

    /// <summary>Disable kill-switches a revision (access refused) and Enable restores it.</summary>
    [SkippableFact]
    public async Task DisableEnable_KillSwitchRoundTrip()
    {
        Skip.IfNot(Vault.Capabilities.Supports(SecretVaultCapability.VersionEnableDisable));
        var secretId = await CreateSecretAsync();
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("hot"));

        (await Vault.Versions.DisableAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();
        var whileDisabled = await Vault.Versions.AccessAsync(Connection, secretId, 1);
        whileDisabled.IsFailure.Should().BeTrue();
        whileDisabled.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.VersionDisabled");

        (await Vault.Versions.EnableAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();
        var reEnabled = await Vault.Versions.AccessAsync(Connection, secretId, 1);
        reEnabled.IsSuccess.Should().BeTrue(reEnabled.IsFailure ? reEnabled.Error.Message : string.Empty);
        reEnabled.Value.AsString().Should().Be("hot");
    }

    /// <summary>Disable and Enable are idempotent on a revision already in the target state.</summary>
    [SkippableFact]
    public async Task DisableEnable_AreIdempotent()
    {
        Skip.IfNot(Vault.Capabilities.Supports(SecretVaultCapability.VersionEnableDisable));
        var secretId = await CreateSecretAsync();
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("v1"));

        (await Vault.Versions.DisableAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();
        (await Vault.Versions.DisableAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();
        (await Vault.Versions.EnableAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();
        (await Vault.Versions.EnableAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();
    }

    /// <summary>Destroyed material reads as not found and its revision number is never reused.</summary>
    [SkippableFact]
    public async Task Destroy_MaterialGoneAndRevisionNumberReserved()
    {
        Skip.IfNot(Vault.Capabilities.Supports(SecretVaultCapability.VersionDestroy));
        var secretId = await CreateSecretAsync();
        await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("doomed"));

        (await Vault.Versions.DestroyAsync(Connection, secretId, 1)).IsSuccess.Should().BeTrue();

        var access = await Vault.Versions.AccessAsync(Connection, secretId, 1);
        access.IsFailure.Should().BeTrue();
        access.Error.Code.Should().Be($"{SecretVaultErrors.Prefix}.VersionNotFound");

        var next = await Vault.Versions.AddAsync(Connection, secretId, SecretMaterial.FromString("survivor"));
        next.IsSuccess.Should().BeTrue(next.IsFailure ? next.Error.Message : string.Empty);
        next.Value.Revision.Should().Be(2, "destroyed revision numbers must stay reserved");
    }

    /// <summary>Deleting a container removes it (and delete is idempotent).</summary>
    [Fact]
    public async Task Delete_RemovesContainer_AndIsIdempotent()
    {
        var secretId = await CreateSecretAsync();

        (await Vault.Secrets.DeleteAsync(Connection, secretId)).IsSuccess.Should().BeTrue();
        (await Vault.Secrets.GetAsync(Connection, secretId)).IsFailure.Should().BeTrue();
        (await Vault.Secrets.DeleteAsync(Connection, secretId)).IsSuccess.Should().BeTrue();
    }

    /// <summary>Prefix listing finds secrets nested under the prefix, not siblings.</summary>
    [SkippableFact]
    public async Task List_ByPathPrefix_FindsNestedSecretsOnly()
    {
        Skip.IfNot(Vault.Capabilities.Supports(SecretVaultCapability.PathHierarchy));
        var marker = $"nest{Guid.NewGuid():N}"[..12];
        var nestedPath = SecretVaultContractTestsHelper.Append(TestPath, marker, "inner").Value;
        var siblingPath = SecretVaultContractTestsHelper.Append(TestPath, $"other{Guid.NewGuid():N}"[..12]).Value;
        var nestedName = NewSecretName();
        await Vault.Secrets.CreateAsync(Connection, new CreateVaultSecret { Name = nestedName, Path = nestedPath });
        await Vault.Secrets.CreateAsync(Connection, new CreateVaultSecret { Name = NewSecretName(), Path = siblingPath });

        var listed = await Vault.Secrets.ListAsync(
            Connection, SecretVaultContractTestsHelper.Append(TestPath, marker).Value);

        listed.IsSuccess.Should().BeTrue(listed.IsFailure ? listed.Error.Message : string.Empty);
        listed.Value.Should().ContainSingle(d => d.Name == nestedName);
    }

    /// <summary>Tag filters match secrets carrying every requested tag.</summary>
    [SkippableFact]
    public async Task List_ByTagFilter_MatchesAllRequestedTags()
    {
        Skip.IfNot(Vault.Capabilities.Supports(SecretVaultCapability.Tags));
        var marker = Guid.NewGuid().ToString("N");
        var taggedName = NewSecretName();
        await Vault.Secrets.CreateAsync(Connection, new CreateVaultSecret
        {
            Name = taggedName,
            Path = TestPath,
            Tags = new Dictionary<string, string> { ["contract-marker"] = marker, ["managed-by"] = "contract" },
        });
        await Vault.Secrets.CreateAsync(Connection, new CreateVaultSecret
        {
            Name = NewSecretName(),
            Path = TestPath,
            Tags = new Dictionary<string, string> { ["managed-by"] = "contract" },
        });

        var listed = await Vault.Secrets.ListAsync(
            Connection, TestPath, new Dictionary<string, string> { ["contract-marker"] = marker });

        listed.IsSuccess.Should().BeTrue(listed.IsFailure ? listed.Error.Message : string.Empty);
        listed.Value.Should().ContainSingle(d => d.Name == taggedName);
    }

    /// <summary>Secret material and token credentials never leak through ToString().</summary>
    [Fact]
    public void Redaction_MaterialAndCredentialsNeverStringify()
    {
        SecretMaterial.FromString("hunter2").ToString().Should().NotContain("hunter2");
        new SecretVaultCredential.ApiToken("hunter2").ToString().Should().NotContain("hunter2");
        var connection = Connection with { Credential = new SecretVaultCredential.ApiToken("hunter2") };
        connection.ToString().Should().NotContain("hunter2");
    }

    private async Task<string> CreateSecretAsync()
    {
        var created = await Vault.Secrets.CreateAsync(Connection, new CreateVaultSecret
        {
            Name = NewSecretName(),
            Path = TestPath,
        });
        created.IsSuccess.Should().BeTrue(created.IsFailure ? created.Error.Message : string.Empty);
        return created.Value.SecretId;
    }
}

/// <summary>
/// Path helpers for <see cref="SecretVaultContractTests"/>.
/// </summary>
internal static class SecretVaultContractTestsHelper
{
    /// <summary>
    /// Appends segments to a path.
    /// </summary>
    public static Result<SecretScopePath> Append(SecretScopePath path, params string[] segments) =>
        SecretScopePath.From([.. path.Segments, .. segments]);
}
