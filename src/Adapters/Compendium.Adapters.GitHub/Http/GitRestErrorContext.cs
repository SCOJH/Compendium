// -----------------------------------------------------------------------
// <copyright file="GitRestErrorContext.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Adapters.GitHub.Http;

/// <summary>
/// Tells the error mapper how to interpret a provider failure for the operation
/// in flight: what a 404 means (a missing repository vs. a missing namespace)
/// and what resource name a 409/422 conflict refers to.
/// </summary>
internal sealed record GitRestErrorContext
{
    /// <summary>Gets the repository this operation targeted; a 404 maps to <c>Git.RepositoryNotFound</c>.</summary>
    public string? RepositoryFullName { get; init; }

    /// <summary>Gets the namespace this operation targeted; a 404 maps to <c>Git.NamespaceNotFound</c>.</summary>
    public string? Namespace { get; init; }

    /// <summary>Gets the resource name a conflict (409/422 "already exists") refers to.</summary>
    public string? ConflictResource { get; init; }

    /// <summary>An empty context: 404 falls through to a generic provider-rejected error.</summary>
    public static readonly GitRestErrorContext None = new();

    /// <summary>Builds a context for a repository-scoped operation.</summary>
    public static GitRestErrorContext ForRepository(GitRepositoryRef repository) =>
        new() { RepositoryFullName = repository.FullName, ConflictResource = repository.FullName };

    /// <summary>Builds a context for a namespace-scoped operation.</summary>
    public static GitRestErrorContext ForNamespace(string @namespace) =>
        new() { Namespace = @namespace, ConflictResource = @namespace };
}
