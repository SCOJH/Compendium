// -----------------------------------------------------------------------
// <copyright file="CachingErrors.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Caching;

/// <summary>
/// Provides standardized error definitions for caching operations.
/// </summary>
public static class CachingErrors
{
    /// <summary>
    /// Gets the error code prefix for caching errors.
    /// </summary>
    public const string Prefix = "Caching";

    /// <summary>
    /// The supplied TTL was zero or negative.
    /// </summary>
    public static Error InvalidTtl(TimeSpan ttl) =>
        Error.Validation(
            $"{Prefix}.InvalidTtl",
            $"TTL must be positive; received {ttl}.");

    /// <summary>
    /// The cache backend failed to satisfy the request and the failure is non-retriable
    /// from the caller's perspective.
    /// </summary>
    public static Error BackendFailure(string message) =>
        Error.Failure($"{Prefix}.BackendFailure", message);
}
