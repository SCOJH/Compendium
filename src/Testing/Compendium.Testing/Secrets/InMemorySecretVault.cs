// -----------------------------------------------------------------------
// <copyright file="InMemorySecretVault.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using System.Collections.Concurrent;
using Compendium.Abstractions.Secrets;
using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Abstractions.Secrets.Services;
using Compendium.Core.Results;

namespace Compendium.Testing.Secrets;

/// <summary>
/// A full-fidelity in-memory <see cref="ISecretVault"/> for tests and dev
/// hosts: immutable monotonic revisions, enable/disable/destroy semantics,
/// path-prefix listing and tag filters, thread-safe, with a call log for
/// interaction assertions. Subscribes to <see cref="SecretVaultContractTests"/>
/// so the fake and real adapters stay behaviorally aligned.
/// </summary>
public sealed class InMemorySecretVault : ISecretVault, ISecretContainerService, ISecretVersionService
{
    /// <summary>
    /// The provider discriminator of the in-memory vault.
    /// </summary>
    public const string ProviderName = "inmemory";

    private readonly ConcurrentDictionary<string, StoredSecret> _secrets = new();
    private readonly ConcurrentQueue<string> _callLog = new();
    private readonly object _createLock = new();

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public SecretVaultCapabilities Capabilities { get; } = new()
    {
        Provider = ProviderName,
        Entries = new Dictionary<SecretVaultCapability, SecretVaultCapabilitySupport>
        {
            [SecretVaultCapability.ImmutableVersions] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.VersionEnableDisable] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.VersionDestroy] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.PathHierarchy] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.Tags] = new(SecretVaultCapabilityLevel.Full),
            [SecretVaultCapability.LargePayload] = new(SecretVaultCapabilityLevel.Full),
        },
    };

    /// <inheritdoc />
    public ISecretContainerService Secrets => this;

    /// <inheritdoc />
    public ISecretVersionService Versions => this;

    /// <summary>
    /// Gets the ordered log of port calls (<c>"MethodName secretId"</c>) for
    /// interaction assertions.
    /// </summary>
    public IReadOnlyCollection<string> CallLog => _callLog;

    /// <inheritdoc />
    public Task<Result<VaultSecretDescriptor>> CreateAsync(
        SecretVaultConnection connection, CreateVaultSecret request, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"CreateAsync {request.Path}/{request.Name}");
        lock (_createLock)
        {
            var path = request.Path.ToString();
            if (_secrets.Values.Any(s => s.Name == request.Name && s.Path == path))
            {
                return Task.FromResult(Result.Failure<VaultSecretDescriptor>(
                    SecretVaultErrors.ConflictExists(request.Name, path)));
            }

            var stored = new StoredSecret
            {
                Id = Guid.NewGuid().ToString(),
                Name = request.Name,
                Path = path,
                PathSegments = [.. request.Path.Segments],
                Description = request.Description,
                Tags = new Dictionary<string, string>(request.Tags ?? new Dictionary<string, string>()),
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _secrets[stored.Id] = stored;
            return Task.FromResult(Result.Success(Describe(stored)));
        }
    }

    /// <inheritdoc />
    public Task<Result<VaultSecretDescriptor>> GetAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"GetAsync {secretId}");
        return Task.FromResult(_secrets.TryGetValue(secretId, out var stored)
            ? Result.Success(Describe(stored))
            : Result.Failure<VaultSecretDescriptor>(SecretVaultErrors.SecretNotFound(secretId)));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<VaultSecretDescriptor>>> ListAsync(
        SecretVaultConnection connection, SecretScopePath pathPrefix,
        IReadOnlyDictionary<string, string>? tagFilter = null, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"ListAsync {pathPrefix}");
        var matches = _secrets.Values
            .Where(s => IsUnderPrefix(s.PathSegments, pathPrefix.Segments))
            .Where(s => tagFilter is null || tagFilter.All(kv => s.Tags.TryGetValue(kv.Key, out var v) && v == kv.Value))
            .OrderBy(s => s.Path, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(Describe)
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<VaultSecretDescriptor>>(matches));
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"DeleteAsync {secretId}");
        _secrets.TryRemove(secretId, out _);
        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<Result<VaultSecretVersion>> AddAsync(
        SecretVaultConnection connection, string secretId, SecretMaterial material,
        string? description = null, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"AddAsync {secretId}");
        if (!_secrets.TryGetValue(secretId, out var stored))
        {
            return Task.FromResult(Result.Failure<VaultSecretVersion>(SecretVaultErrors.SecretNotFound(secretId)));
        }

        lock (stored.Lock)
        {
            var version = new StoredVersion
            {
                Revision = stored.NextRevision++,
                Material = material,
                Status = VaultSecretVersionStatus.Enabled,
                Description = description,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            stored.Versions[version.Revision] = version;
            return Task.FromResult(Result.Success(DescribeVersion(version)));
        }
    }

    /// <inheritdoc />
    public Task<Result<SecretMaterial>> AccessAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"AccessAsync {secretId}#{revision}");
        if (!_secrets.TryGetValue(secretId, out var stored))
        {
            return Task.FromResult(Result.Failure<SecretMaterial>(SecretVaultErrors.SecretNotFound(secretId)));
        }

        lock (stored.Lock)
        {
            if (!stored.Versions.TryGetValue(revision, out var version) ||
                version.Status == VaultSecretVersionStatus.Destroyed)
            {
                return Task.FromResult(Result.Failure<SecretMaterial>(
                    SecretVaultErrors.VersionNotFound(secretId, revision)));
            }

            return Task.FromResult(version.Status == VaultSecretVersionStatus.Disabled
                ? Result.Failure<SecretMaterial>(SecretVaultErrors.VersionDisabled(secretId, revision))
                : Result.Success(version.Material));
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<VaultSecretVersion>>> ListAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default)
    {
        _callLog.Enqueue($"ListVersionsAsync {secretId}");
        if (!_secrets.TryGetValue(secretId, out var stored))
        {
            return Task.FromResult(Result.Failure<IReadOnlyList<VaultSecretVersion>>(
                SecretVaultErrors.SecretNotFound(secretId)));
        }

        lock (stored.Lock)
        {
            var versions = stored.Versions.Values
                .OrderBy(v => v.Revision)
                .Select(DescribeVersion)
                .ToList();
            return Task.FromResult(Result.Success<IReadOnlyList<VaultSecretVersion>>(versions));
        }
    }

    /// <inheritdoc />
    public Task<Result> EnableAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        SetStatusAsync("EnableAsync", secretId, revision, VaultSecretVersionStatus.Enabled);

    /// <inheritdoc />
    public Task<Result> DisableAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        SetStatusAsync("DisableAsync", secretId, revision, VaultSecretVersionStatus.Disabled);

    /// <inheritdoc />
    public Task<Result> DestroyAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        SetStatusAsync("DestroyAsync", secretId, revision, VaultSecretVersionStatus.Destroyed);

    private static bool IsUnderPrefix(IReadOnlyList<string> path, IReadOnlyList<string> prefix)
    {
        if (prefix.Count > path.Count)
        {
            return false;
        }

        for (var i = 0; i < prefix.Count; i++)
        {
            if (!string.Equals(path[i], prefix[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static VaultSecretDescriptor Describe(StoredSecret stored) => new()
    {
        SecretId = stored.Id,
        Name = stored.Name,
        Path = SecretScopePath.From([.. stored.PathSegments]).Value,
        Description = stored.Description,
        Tags = new Dictionary<string, string>(stored.Tags),
        VersionCount = stored.Versions.Count,
        CreatedAt = stored.CreatedAt,
    };

    private static VaultSecretVersion DescribeVersion(StoredVersion version) => new()
    {
        Revision = version.Revision,
        Status = version.Status,
        Description = version.Description,
        CreatedAt = version.CreatedAt,
    };

    private Task<Result> SetStatusAsync(string operation, string secretId, long revision, VaultSecretVersionStatus status)
    {
        _callLog.Enqueue($"{operation} {secretId}#{revision}");
        if (!_secrets.TryGetValue(secretId, out var stored))
        {
            return Task.FromResult(Result.Failure(SecretVaultErrors.SecretNotFound(secretId)));
        }

        lock (stored.Lock)
        {
            if (!stored.Versions.TryGetValue(revision, out var version) ||
                version.Status == VaultSecretVersionStatus.Destroyed)
            {
                return Task.FromResult(Result.Failure(SecretVaultErrors.VersionNotFound(secretId, revision)));
            }

            version.Status = status;
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class StoredSecret
    {
        public required string Id { get; init; }

        public required string Name { get; init; }

        public required string Path { get; init; }

        public required List<string> PathSegments { get; init; }

        public string? Description { get; init; }

        public required Dictionary<string, string> Tags { get; init; }

        public DateTimeOffset CreatedAt { get; init; }

        public Dictionary<long, StoredVersion> Versions { get; } = [];

        public long NextRevision { get; set; } = 1;

        public object Lock { get; } = new();
    }

    private sealed class StoredVersion
    {
        public required long Revision { get; init; }

        public required SecretMaterial Material { get; init; }

        public required VaultSecretVersionStatus Status { get; set; }

        public string? Description { get; init; }

        public DateTimeOffset CreatedAt { get; init; }
    }
}
