// -----------------------------------------------------------------------
// <copyright file="GitHubNamespaceProvisioner.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub.Services;

/// <summary>
/// Namespace provisioning is unreachable on github.com with App credentials —
/// creating an organization needs an enterprise-owner user token. This provider
/// declares <see cref="GitCapability.NamespaceProvisioning"/> as
/// <see cref="GitCapabilityLevel.None"/> and always fails with the standard
/// <c>Git.CapabilityNotSupported</c> rather than attempting the call.
/// </summary>
internal sealed class GitHubNamespaceProvisioner : IGitNamespaceProvisioner
{
    /// <inheritdoc />
    public Task<Result<GitNamespace>> CreateNamespaceAsync(
        GitConnection connection, CreateGitNamespace request, CancellationToken cancellationToken = default)
    {
        var guard = GitHubCapabilities.Matrix.EnsureSupported(GitCapability.NamespaceProvisioning);
        return Task.FromResult(Result.Failure<GitNamespace>(guard.Error));
    }
}
