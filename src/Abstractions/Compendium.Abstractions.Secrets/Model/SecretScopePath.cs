// -----------------------------------------------------------------------
// <copyright file="SecretScopePath.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Secrets.Model;

/// <summary>
/// A validated hierarchical path (<c>/segment/segment/...</c>) locating a
/// secret inside a vault tenancy. The path is organizational only — callers
/// define their own layout conventions; adapters map it onto the provider's
/// folder/path concept (or a name prefix when the provider has none). The
/// path is never the secret's identity: consumers must address secrets by the
/// provider-side id returned at creation.
/// </summary>
public sealed record SecretScopePath
{
    private SecretScopePath(IReadOnlyList<string> segments)
    {
        Segments = segments;
    }

    /// <summary>
    /// Gets the root path (<c>/</c>).
    /// </summary>
    public static SecretScopePath Root { get; } = new([]);

    /// <summary>
    /// Gets the ordered path segments (empty for the root path).
    /// </summary>
    public IReadOnlyList<string> Segments { get; }

    /// <summary>
    /// Builds a path from validated segments: each must be non-empty, at most
    /// 100 characters, and restricted to letters, digits, <c>-</c>, <c>_</c>
    /// and <c>.</c> (no separators, no whitespace).
    /// </summary>
    public static Result<SecretScopePath> From(params string[] segments)
    {
        foreach (var segment in segments)
        {
            if (string.IsNullOrWhiteSpace(segment) || segment.Length > 100 ||
                !segment.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.'))
            {
                return Result.Failure<SecretScopePath>(Error.Validation(
                    $"{SecretVaultErrors.Prefix}.InvalidPathSegment",
                    $"Invalid path segment '{segment}': segments must be 1-100 characters of letters, digits, '-', '_' or '.'."));
            }
        }

        return Result.Success(new SecretScopePath([.. segments]));
    }

    /// <summary>
    /// Returns the canonical string form: <c>/</c>-prefixed joined segments,
    /// or <c>/</c> for the root.
    /// </summary>
    public override string ToString() =>
        Segments.Count == 0 ? "/" : "/" + string.Join('/', Segments);
}
