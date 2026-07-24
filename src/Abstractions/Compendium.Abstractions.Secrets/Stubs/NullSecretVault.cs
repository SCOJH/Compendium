// -----------------------------------------------------------------------
// <copyright file="NullSecretVault.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

using Compendium.Abstractions.Secrets.Capabilities;
using Compendium.Abstractions.Secrets.Connections;
using Compendium.Abstractions.Secrets.Model;
using Compendium.Abstractions.Secrets.Services;

namespace Compendium.Abstractions.Secrets.Stubs;

/// <summary>
/// The fail-fast vault registered when no provider is configured: every
/// operation fails with <c>SecretVault.NotConfigured</c>. Keeps consumers
/// honest — a missing configuration surfaces as a uniform, diagnosable error
/// instead of a null reference or a silent no-op.
/// </summary>
public sealed class NullSecretVault : ISecretVault, ISecretContainerService, ISecretVersionService
{
    /// <summary>
    /// The provider discriminator of the null vault.
    /// </summary>
    public const string ProviderName = "null";

    /// <inheritdoc />
    public string Provider => ProviderName;

    /// <inheritdoc />
    public SecretVaultCapabilities Capabilities { get; } = new()
    {
        Provider = ProviderName,
        Entries = new Dictionary<SecretVaultCapability, SecretVaultCapabilitySupport>(),
    };

    /// <inheritdoc />
    public ISecretContainerService Secrets => this;

    /// <inheritdoc />
    public ISecretVersionService Versions => this;

    /// <inheritdoc />
    public Task<Result<VaultSecretDescriptor>> CreateAsync(
        SecretVaultConnection connection, CreateVaultSecret request, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<VaultSecretDescriptor>(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result<VaultSecretDescriptor>> GetAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<VaultSecretDescriptor>(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<VaultSecretDescriptor>>> ListAsync(
        SecretVaultConnection connection, SecretScopePath pathPrefix,
        IReadOnlyDictionary<string, string>? tagFilter = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<IReadOnlyList<VaultSecretDescriptor>>(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result> DeleteAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result<VaultSecretVersion>> AddAsync(
        SecretVaultConnection connection, string secretId, SecretMaterial material,
        string? description = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<VaultSecretVersion>(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result<SecretMaterial>> AccessAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<SecretMaterial>(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<VaultSecretVersion>>> ListAsync(
        SecretVaultConnection connection, string secretId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure<IReadOnlyList<VaultSecretVersion>>(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result> EnableAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result> DisableAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(SecretVaultErrors.NotConfigured(ProviderName)));

    /// <inheritdoc />
    public Task<Result> DestroyAsync(
        SecretVaultConnection connection, string secretId, long revision, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Failure(SecretVaultErrors.NotConfigured(ProviderName)));
}
