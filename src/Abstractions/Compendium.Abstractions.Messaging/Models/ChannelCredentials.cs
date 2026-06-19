// -----------------------------------------------------------------------
// <copyright file="ChannelCredentials.cs" company="Sassy Solutions">
//     Copyright (c) 2026 Sassy Solutions. Licensed under the MIT License.
//     See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------------

namespace Compendium.Abstractions.Messaging.Models;

/// <summary>
/// An opaque bag of per-tenant credentials supplied to a connector at call time (bot token,
/// signing secret, app id, ...). Kept generic so each platform reads the keys it needs; the host
/// populates it from its secret store for the tenant owning the inbound conversation.
/// </summary>
public sealed record ChannelCredentials
{
    private readonly IReadOnlyDictionary<string, string> _values;

    /// <summary>Initialises credentials from a key/value map.</summary>
    /// <param name="values">The credential values, keyed by connector-defined names.</param>
    public ChannelCredentials(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values;
    }

    /// <summary>An empty credential set.</summary>
    public static ChannelCredentials Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Returns the value for <paramref name="key"/>, or <see langword="null"/> when absent.</summary>
    public string? Get(string key) => _values.TryGetValue(key, out var value) ? value : null;

    /// <summary>Returns the value for <paramref name="key"/> as a <see cref="Result{T}"/>,
    /// failing with <see cref="MessagingErrors.MissingCredential"/> when absent or blank.</summary>
    public Result<string> Require(string key) =>
        _values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Result.Success(value)
            : Result.Failure<string>(MessagingErrors.MissingCredential(key));
}
